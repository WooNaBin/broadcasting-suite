using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace BroadcastNasBridge.Services;

/// <summary>로컬 LAN에서 SMB(445) 후보를 찾습니다. (NAS IP 선택용)</summary>
public static class LanScanner
{
    private static readonly Regex ArpWindows = new(
        @"^\s*(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9a-fA-F\-]{11,17})\s+(\w+)",
        RegexOptions.Compiled);
    private static readonly Regex ArpUnix = new(
        @"\((\d{1,3}(?:\.\d{1,3}){3})\)\s+at\s+([0-9a-fA-F:]{11,17})",
        RegexOptions.Compiled);

    public static async Task<object[]> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var found = new ConcurrentDictionary<string, Device>(StringComparer.Ordinal);

        foreach (var (ip, mac) in ReadArp())
        {
            found[ip] = new Device { Ip = ip, Mac = mac };
        }

        var selfIps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (networkBase, self) in LocalSlash24())
        {
            selfIps.Add(self.ToString());
            await ProbeSlash24Async(networkBase, self, found, cancellationToken);
        }

        foreach (var self in selfIps)
            found.TryRemove(self, out _);

        using var gate = new SemaphoreSlim(16);
        await Task.WhenAll(found.Values.Select(async d =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                d.Hostname = await TryDnsAsync(d.Ip, cancellationToken);
                if (string.Equals(d.Hostname, d.Ip, StringComparison.OrdinalIgnoreCase))
                    d.Hostname = null;
            }
            finally { gate.Release(); }
        }));

        return found.Values
            .OrderByDescending(d => d.SmbOpen)
            .ThenByDescending(d => !string.IsNullOrWhiteSpace(d.Hostname))
            .ThenBy(d => d.Ip)
            .Select(d => (object)new
            {
                ip = d.Ip,
                hostname = d.Hostname,
                displayName = string.IsNullOrWhiteSpace(d.Hostname) ? $"장치 {d.Ip}" : d.Hostname,
                mac = d.Mac,
                smbOpen = d.SmbOpen,
                likelyNas = d.SmbOpen,
            })
            .ToArray();
    }

    private sealed class Device
    {
        public string Ip { get; init; } = "";
        public string? Mac { get; set; }
        public string? Hostname { get; set; }
        public bool SmbOpen { get; set; }
    }

    private static IEnumerable<(string Ip, string Mac)> ReadArp()
    {
        string output;
        try { output = Run("arp", "-a"); }
        catch { yield break; }

        foreach (var line in output.Split('\n'))
        {
            if (OperatingSystem.IsWindows())
            {
                var m = ArpWindows.Match(line);
                if (!m.Success) continue;
                var mac = m.Groups[2].Value.Replace('-', ':').ToLowerInvariant();
                if (mac is "00:00:00:00:00:00" or "ff:ff:ff:ff:ff:ff") continue;
                yield return (m.Groups[1].Value, mac);
            }
            else
            {
                var m = ArpUnix.Match(line);
                if (!m.Success) continue;
                var mac = m.Groups[2].Value.Replace('-', ':').ToLowerInvariant();
                if (mac.Contains("incomplete", StringComparison.OrdinalIgnoreCase)) continue;
                yield return (m.Groups[1].Value, mac);
            }
        }
    }

    private static IEnumerable<(IPAddress NetworkBase, IPAddress Self)> LocalSlash24()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(address.Address)) continue;
                var bytes = address.Address.GetAddressBytes();
                if (bytes.Length != 4) continue;
                bytes[3] = 0;
                var network = new IPAddress(bytes);
                if (!seen.Add(network.ToString())) continue;
                yield return (network, address.Address);
            }
        }
    }

    private static async Task ProbeSlash24Async(
        IPAddress networkBase,
        IPAddress self,
        ConcurrentDictionary<string, Device> found,
        CancellationToken ct)
    {
        var baseBytes = networkBase.GetAddressBytes();
        using var gate = new SemaphoreSlim(40);
        var tasks = new List<Task>();
        for (var host = 1; host <= 254; host++)
        {
            var bytes = (byte[])baseBytes.Clone();
            bytes[3] = (byte)host;
            var ip = new IPAddress(bytes);
            if (ip.Equals(self)) continue;
            var ipText = ip.ToString();
            tasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var open = await IsPortOpenAsync(ip, 445, 200, ct);
                    if (!open && !found.ContainsKey(ipText)) return;
                    found.AddOrUpdate(
                        ipText,
                        _ => new Device { Ip = ipText, SmbOpen = open },
                        (_, existing) =>
                        {
                            if (open) existing.SmbOpen = true;
                            return existing;
                        });
                }
                finally { gate.Release(); }
            }, ct));
        }
        await Task.WhenAll(tasks);
    }

    private static async Task<bool> IsPortOpenAsync(IPAddress ip, int port, int timeoutMs, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);
        try
        {
            await client.ConnectAsync(ip, port, timeout.Token);
            return client.Connected;
        }
        catch { return false; }
    }

    private static async Task<string?> TryDnsAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(700);
            var entry = await Dns.GetHostEntryAsync(ip, timeout.Token);
            var name = entry.HostName?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (name.Contains('.')) name = name.Split('.')[0];
            return name;
        }
        catch { return null; }
    }

    private static string Run(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(4000);
        return output;
    }
}
