using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using CtrlOne.Devices;

namespace CtrlOne.Services;

public sealed class DiscoveredDevice
{
    public string Address { get; init; } = "";
    public string Model { get; init; } = "HyperDeck";
    public string ProtocolVersion { get; init; } = "";
    public string UniqueId { get; init; } = "";
    public string SuggestedName { get; init; } = "";
    public bool AlreadyRegistered { get; set; }
}

public sealed class NetworkScanResult
{
    public IReadOnlyList<string> ScannedSubnets { get; init; } = [];
    public IReadOnlyList<DiscoveredDevice> Devices { get; init; } = [];
    public int HostsChecked { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// 로컬 IPv4 서브넷에서 HyperDeck TCP(9993) 배너를 읽어 장비를 검색한다.
/// </summary>
public sealed class HyperDeckDiscoveryService
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(800);
    private const int MaxConcurrency = 64;

    private readonly DeviceManager _devices;

    public HyperDeckDiscoveryService(DeviceManager devices)
    {
        _devices = devices;
    }

    public async Task<NetworkScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        var subnets = EnumerateScanTargets().ToList();
        if (subnets.Count == 0)
        {
            return new NetworkScanResult
            {
                Message = "검색할 로컬 IPv4 네트워크를 찾지 못했습니다. 유선/무선 연결을 확인하세요."
            };
        }

        var registered = new HashSet<string>(
            _devices.GetSnapshots().Select(d => d.Address),
            StringComparer.OrdinalIgnoreCase);

        var found = new ConcurrentDictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        var hosts = subnets.SelectMany(s => s.Hosts).Distinct().ToList();
        using var gate = new SemaphoreSlim(MaxConcurrency);

        var tasks = hosts.Select(async ip =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var hit = await ProbeAsync(ip, cancellationToken).ConfigureAwait(false);
                if (hit is null)
                    return;

                hit.AlreadyRegistered = registered.Contains(hit.Address);
                found[hit.Address] = hit;
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var devices = found.Values
            .OrderBy(d => d.AlreadyRegistered)
            .ThenBy(d => ToSortableIp(d.Address))
            .ToList();

        return new NetworkScanResult
        {
            ScannedSubnets = subnets.Select(s => s.Label).Distinct().ToList(),
            Devices = devices,
            HostsChecked = hosts.Count,
            Message = devices.Count == 0
                ? $"검색 완료 ({hosts.Count}호스트). HyperDeck을 찾지 못했습니다."
                : $"검색 완료: HyperDeck {devices.Count}대 발견"
        };
    }

    private static async Task<DiscoveredDevice?> ProbeAsync(IPAddress ip, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(ConnectTimeout);

        try
        {
            await client.ConnectAsync(ip, HyperDeckDevice.DefaultPort, connectCts.Token).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        try
        {
            await using var stream = client.GetStream();
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(ReadTimeout);

            var banner = await ReadBannerAsync(stream, readCts.Token).ConfigureAwait(false);
            if (!IsHyperDeckBanner(banner))
                return null;

            var model = ExtractField(banner, "model") ?? "HyperDeck";
            var protocol = ExtractField(banner, "protocol version") ?? "";
            var uniqueId = ExtractField(banner, "unique id") ?? "";
            var address = ip.ToString();
            var suggested = BuildSuggestedName(model, address);

            return new DiscoveredDevice
            {
                Address = address,
                Model = model,
                ProtocolVersion = protocol,
                UniqueId = uniqueId,
                SuggestedName = suggested
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ReadBannerAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[2048];
        var sb = new StringBuilder();

        while (!ct.IsCancellationRequested && sb.Length < 4096)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read <= 0)
                break;

            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
            var text = sb.ToString();

            // HyperDeck connection info 블록은 빈 줄로 끝나는 경우가 많음
            if (text.Contains("connection info", StringComparison.OrdinalIgnoreCase)
                && (text.Contains("\n\n") || text.Contains("\r\n\r\n") || text.Contains("model:", StringComparison.OrdinalIgnoreCase)))
            {
                // model 라인이 올 때까지 조금 더 기다릴 수 있음
                if (text.Contains("model:", StringComparison.OrdinalIgnoreCase) || text.Contains("\n\n"))
                    break;
            }
        }

        return sb.ToString();
    }

    private static bool IsHyperDeckBanner(string banner)
    {
        if (string.IsNullOrWhiteSpace(banner))
            return false;

        var lower = banner.ToLowerInvariant();
        return lower.Contains("connection info")
               || lower.Contains("hyperdeck")
               || lower.Contains("protocol version");
    }

    private static string? ExtractField(string banner, string key)
    {
        var match = Regex.Match(
            banner,
            $@"^\s*{Regex.Escape(key)}\s*:\s*(.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string BuildSuggestedName(string model, string address)
    {
        var compact = Regex.Replace(model, @"[^A-Za-z0-9]+", "_").Trim('_');
        if (string.IsNullOrEmpty(compact))
            compact = "HyperDeck";

        var suffix = address.Split('.').LastOrDefault() ?? "0";
        return $"{compact}_{suffix}";
    }

    private static IEnumerable<ScanSubnet> EnumerateScanTargets()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var props = nic.GetIPProperties();
            foreach (var uni in props.UnicastAddresses)
            {
                if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (IPAddress.IsLoopback(uni.Address))
                    continue;

                var prefix = uni.PrefixLength;
                if (prefix is < 16 or > 30)
                    continue;

                // 너무 넓은 대역은 로컬 주소 기준 /24만 스캔
                var scanPrefix = Math.Max(prefix, 24);
                var hosts = ExpandHosts(uni.Address, scanPrefix).ToList();
                if (hosts.Count == 0)
                    continue;

                var network = ToNetworkAddress(uni.Address, scanPrefix);
                yield return new ScanSubnet($"{network}/{scanPrefix}", hosts);
            }
        }
    }

    private static IEnumerable<IPAddress> ExpandHosts(IPAddress local, int prefixLength)
    {
        var localBytes = local.GetAddressBytes();
        var mask = PrefixToMask(prefixLength);
        var network = new byte[4];
        var broadcast = new byte[4];

        for (var i = 0; i < 4; i++)
        {
            network[i] = (byte)(localBytes[i] & mask[i]);
            broadcast[i] = (byte)(network[i] | ~mask[i]);
        }

        var start = ToUInt(network) + 1;
        var end = ToUInt(broadcast) - 1;
        if (end < start)
            yield break;

        // 안전장치: 최대 254호스트
        var count = end - start + 1;
        if (count > 254)
            end = start + 253;

        for (var value = start; value <= end; value++)
        {
            var ip = FromUInt(value);
            if (ip.Equals(local))
                continue;
            yield return ip;
        }
    }

    private static byte[] PrefixToMask(int prefix)
    {
        var mask = new byte[4];
        for (var i = 0; i < 32; i++)
        {
            if (i < prefix)
                mask[i / 8] |= (byte)(0x80 >> (i % 8));
        }
        return mask;
    }

    private static string ToNetworkAddress(IPAddress address, int prefix)
    {
        var bytes = address.GetAddressBytes();
        var mask = PrefixToMask(prefix);
        for (var i = 0; i < 4; i++)
            bytes[i] = (byte)(bytes[i] & mask[i]);
        return new IPAddress(bytes).ToString();
    }

    private static uint ToUInt(byte[] bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    private static uint ToUInt(IPAddress ip) => ToUInt(ip.GetAddressBytes());

    private static IPAddress FromUInt(uint value) =>
        new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    private static uint ToSortableIp(string address)
    {
        return IPAddress.TryParse(address, out var ip)
               && ip.AddressFamily == AddressFamily.InterNetwork
            ? ToUInt(ip)
            : uint.MaxValue;
    }

    private sealed record ScanSubnet(string Label, List<IPAddress> Hosts);
}
