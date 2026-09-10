using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BroadcastNasBridge.Services;

public sealed class SmbConnection
{
    private string? root;
    private string? macMountPoint;
    /// <summary>앱이 mount_smbfs 로 만든 마운트면 true. /Volumes Finder 마운트면 false.</summary>
    private bool macMountOwned;
    public bool IsConnected => root is not null;
    public string? Root => root;

    /// <summary>이미 마운트된 공유의 하위 경로만 바인딩 (마운트 소유권 없음).</summary>
    public void UseLocalRoot(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !Directory.Exists(absolutePath))
            throw new DirectoryNotFoundException($"경로를 찾을 수 없습니다: {absolutePath}");
        root = absolutePath;
        macMountPoint = null;
        macMountOwned = false;
    }

    public void Connect(string host, string share, string username, string password)
    {
        var (normalizedHost, normalizedShare) = NormalizeHostAndShare(host, share);
        if (string.IsNullOrWhiteSpace(normalizedHost))
            throw new ArgumentException("NAS IP 또는 호스트 이름을 확인하세요.");
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("사용자 계정을 입력하세요.");
        if (string.IsNullOrWhiteSpace(normalizedShare))
            throw new ArgumentException("공유 폴더명(Share 이름)을 입력하세요. 예: schedule");

        if (OperatingSystem.IsWindows())
            ConnectWindows(normalizedHost, normalizedShare, username, password);
        else if (OperatingSystem.IsMacOS())
            ConnectMac(normalizedHost, normalizedShare, username, password);
        else
            throw new PlatformNotSupportedException("현재 OS에서는 SMB 연결을 지원하지 않습니다. Windows 또는 macOS를 사용하세요.");
    }

    private void ConnectWindows(string normalizedHost, string normalizedShare, string username, string password)
    {
        var parts = normalizedShare.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var rootShareName = parts[0];
        // 공유명에 공백이 있어도 UNC를 직접 조합 (Path.Combine은 UNC+공백에서 깨질 수 있음)
        var rootNetworkPath = $@"\\{normalizedHost}\{rootShareName}";
        var fullNetworkPath = parts.Length > 1
            ? $@"{rootNetworkPath}\{string.Join('\\', parts.Skip(1))}"
            : rootNetworkPath;

        // WorkLog 등과 같은 NAS를 동시에 쓸 수 있게, 기존 세션을 먼저 끊지 않음
        DisconnectLocalOnly();

        if (CanUseUncPath(fullNetworkPath))
        {
            root = fullNetworkPath;
            return;
        }

        var candidates = BuildUsernameCandidates(username, normalizedHost);
        var lastError = 0;
        string? usedUser = null;
        foreach (var candidate in candidates)
        {
            var result = TryAddConnection(rootNetworkPath, password, candidate);
            if (result == 0)
            {
                usedUser = candidate;
                lastError = 0;
                break;
            }

            // 다른 자격 증명으로 이미 연결된 경우: 강제 끊기 전에 경로 접근만 확인
            if (result is 1219 or 85 or 2404)
            {
                if (CanUseUncPath(fullNetworkPath))
                {
                    usedUser = candidate;
                    lastError = 0;
                    break;
                }
            }

            lastError = result;
        }

        // 이미 동일 서버에 연결돼 있고 경로만 쓸 수 있으면 성공 처리
        if (usedUser is null && CanUseUncPath(fullNetworkPath))
        {
            usedUser = username;
            lastError = 0;
        }

        // 다른 앱(WorkLog·FileChecker·탐색기)이 쓰는 SMB 세션을 끊지 않음.
        // 자격 증명 충돌(1219 등)이면 강제 해제 대신 안내만 한다.
        if (lastError != 0 || usedUser is null)
            throw new InvalidOperationException(DescribeSmbError(lastError, rootNetworkPath, fullNetworkPath));

        root = fullNetworkPath;
        try
        {
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException(
                    $"SMB 인증은 됐지만 경로에 접근할 수 없습니다.\n확인 경로: {root}\n공유명 아래 하위 폴더가 있다면 '공유명\\하위폴더' 형식으로 입력하세요.");
            _ = Directory.GetFiles(root);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Disconnect();
            throw new InvalidOperationException(
                $"SMB 연결 후 경로 접근 중 오류입니다.\n경로: {root}\n{ex.Message}", ex);
        }
    }

    private void ConnectMac(string normalizedHost, string normalizedShare, string username, string password)
    {
        Disconnect();

        var parts = normalizedShare.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rootShareName = parts[0];
        var subPath = parts.Length > 1 ? string.Join(Path.DirectorySeparatorChar, parts.Skip(1)) : null;

        // 이미 같은 공유가 마운트돼 있으면 재사용 (브리지·레거시·Finder 중복 마운트 방지 → 최대 Temp+Permanent 2개)
        var existing = FindExistingSmbfsMount(normalizedHost, rootShareName);
        if (existing is not null)
        {
            macMountPoint = existing;
            macMountOwned = false;
            root = string.IsNullOrEmpty(subPath) ? existing : Path.Combine(existing, subPath);
            try
            {
                if (!Directory.Exists(root))
                    throw new DirectoryNotFoundException(
                        $"SMB 경로에 접근할 수 없습니다.\n확인 경로: {root}\n공유명 아래 하위 폴더가 있다면 '공유명/하위폴더' 형식으로 입력하세요.");
                _ = Directory.GetFiles(root);
                StartupConsole.WriteLine($"Mac SMB 재사용: //{normalizedHost}/{rootShareName} → {existing}");
                return;
            }
            catch (Exception ex) when (ex is not InvalidOperationException and not DirectoryNotFoundException)
            {
                macMountPoint = null;
                root = null;
                throw new InvalidOperationException(
                    $"이미 마운트된 경로 접근 중 오류입니다.\n경로: {existing}\n{ex.Message}", ex);
            }
        }

        // 호스트+공유별 고정 마운트 경로 (Guid 매번 생성 → File exists 재발 방지)
        var safeShare = SanitizeShareForPath(rootShareName);
        var mountPoint = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "BroadcastNasBridge",
            "mnt",
            $"{normalizedHost}_{safeShare}");

        PrepareEmptyMountPoint(mountPoint);

        Exception? lastError = null;
        foreach (var candidate in BuildMacUsernameCandidates(username, normalizedHost))
        {
            try
            {
                MountSmbfs(normalizedHost, rootShareName, candidate, password, mountPoint);
                lastError = null;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
                TryUnmount(mountPoint, force: true);
                PrepareEmptyMountPoint(mountPoint);
            }
        }

        if (lastError is not null)
        {
            // 실패 후 Finder·다른 앱이 마운트했을 수 있음
            var fallback = FindExistingSmbfsMount(normalizedHost, rootShareName);
            if (fallback is not null)
            {
                macMountPoint = fallback;
                macMountOwned = false;
                root = string.IsNullOrEmpty(subPath) ? fallback : Path.Combine(fallback, subPath);
                if (Directory.Exists(root))
                {
                    _ = Directory.GetFiles(root);
                    return;
                }
            }

            TryDeleteDirectory(mountPoint);
            throw new InvalidOperationException(
                $"SMB 연결에 실패했습니다.\n대상: //{normalizedHost}/{rootShareName}\n{lastError.Message}\n" +
                "· NAS IP·공유명·계정·비밀번호를 확인하세요.\n" +
                "· Finder에서 해당 공유를 먼저 연결해 두면 /Volumes 경로를 재사용할 수 있습니다.\n" +
                "· macOS 시스템 설정 → 개인정보 보호 및 보안 → 로컬 네트워크에서 이 앱을 허용했는지 확인하세요.");
        }

        macMountPoint = mountPoint;
        macMountOwned = true;
        root = string.IsNullOrEmpty(subPath) ? mountPoint : Path.Combine(mountPoint, subPath);
        try
        {
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException(
                    $"SMB 인증은 됐지만 경로에 접근할 수 없습니다.\n확인 경로: {root}\n공유명 아래 하위 폴더가 있다면 '공유명/하위폴더' 형식으로 입력하세요.");
            _ = Directory.GetFiles(root);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Disconnect();
            throw new InvalidOperationException(
                $"SMB 연결 후 경로 접근 중 오류입니다.\n경로: {root}\n{ex.Message}", ex);
        }
    }

    private static string SanitizeShareForPath(string shareName) =>
        string.Concat(shareName.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

    /// <summary>
    /// 동일 호스트+공유의 기존 smbfs 마운트를 찾는다. 있으면 새 mount_smbfs 하지 않는다.
    /// </summary>
    private static string? FindExistingSmbfsMount(string host, string shareName)
    {
        foreach (var candidate in EnumerateLikelyMountPoints(host, shareName))
        {
            if (CanAccessDirectory(candidate))
                return candidate;
        }

        foreach (var mounted in ListSmbfsMountPoints())
        {
            if (!MountedShareMatches(mounted.Source, host, shareName))
                continue;
            if (CanAccessDirectory(mounted.Path))
                return mounted.Path;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateLikelyMountPoints(string host, string shareName)
    {
        yield return Path.Combine("/Volumes", shareName);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var safe = SanitizeShareForPath(shareName);
        var support = Path.Combine(home, "Library", "Application Support");
        yield return Path.Combine(support, "BroadcastNasBridge", "mnt", $"{host}_{safe}");
        yield return Path.Combine(support, "BroadcastingSchedule", "mnt", $"{host}_{safe}");
        yield return Path.Combine(support, "FileChecker", "mnt", $"{host}_{safe}");
        yield return Path.Combine(support, "ScheduleDataManager", "mnt", $"{host}_{safe}");
        yield return Path.Combine(support, "WorkLog", "mnt");
    }

    private static bool CanAccessDirectory(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
            _ = Directory.GetFileSystemEntries(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool MountedShareMatches(string source, string host, string shareName)
    {
        // //user@host/Share 또는 smb://user@host/Share%20Name
        var s = source.Trim();
        if (s.StartsWith("smb:", StringComparison.OrdinalIgnoreCase))
            s = s[4..];
        s = s.TrimStart('/');
        // user@host/share...
        var at = s.LastIndexOf('@');
        var pathPart = at >= 0 ? s[(at + 1)..] : s;
        var slash = pathPart.IndexOf('/');
        if (slash < 0) return false;
        var mountedHost = pathPart[..slash];
        var mountedShareEnc = pathPart[(slash + 1)..].Split('/', 2)[0];
        string mountedShare;
        try { mountedShare = Uri.UnescapeDataString(mountedShareEnc); }
        catch { mountedShare = mountedShareEnc; }

        return mountedHost.Equals(host, StringComparison.OrdinalIgnoreCase) &&
               mountedShare.Equals(shareName, StringComparison.OrdinalIgnoreCase);
    }

    private static List<(string Source, string Path)> ListSmbfsMountPoints()
    {
        var list = new List<(string, string)>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/sbin/mount",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return list;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            // //user@host/Share on /path (smbfs, ...)
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.Contains("smbfs", StringComparison.OrdinalIgnoreCase)) continue;
                var onIdx = line.IndexOf(" on ", StringComparison.Ordinal);
                if (onIdx < 0) continue;
                var source = line[..onIdx].Trim();
                var rest = line[(onIdx + 4)..];
                var paren = rest.IndexOf(" (", StringComparison.Ordinal);
                var path = (paren >= 0 ? rest[..paren] : rest).Trim();
                if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(path))
                    list.Add((source, path));
            }
        }
        catch
        {
            // ignore
        }
        return list;
    }

    private static void PrepareEmptyMountPoint(string mountPoint)
    {
        TryUnmount(mountPoint, force: true);
        try
        {
            if (Directory.Exists(mountPoint))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(mountPoint))
                {
                    try
                    {
                        if (Directory.Exists(entry)) Directory.Delete(entry, true);
                        else File.Delete(entry);
                    }
                    catch { /* ignore */ }
                }
                try { Directory.Delete(mountPoint, false); }
                catch
                {
                    TryUnmount(mountPoint, force: true);
                    try { Directory.Delete(mountPoint, true); } catch { /* ignore */ }
                }
            }
        }
        catch { /* ignore */ }

        Directory.CreateDirectory(Path.GetDirectoryName(mountPoint)!);
        Directory.CreateDirectory(mountPoint);
    }

    private static void MountSmbfs(string host, string shareName, string username, string password, string mountPoint)
    {
        var sources = new[]
        {
            BuildMacMountSource(host, shareName, username, password, smbScheme: false),
            BuildMacMountSource(host, shareName, username, password, smbScheme: true),
        };

        Exception? lastError = null;
        foreach (var source in sources.Distinct(StringComparer.Ordinal))
        {
            // nobrowse 우선 — Finder에 드라이브가 중복 표시되는 것 완화. 실패 시에만 browse 허용.
            foreach (var nobrowse in new[] { true, false })
            {
                try
                {
                    PrepareEmptyMountPoint(mountPoint);
                    RunMountSmbfs(source, mountPoint, nobrowse);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    TryUnmount(mountPoint, force: true);
                }
            }
        }

        throw lastError ?? new InvalidOperationException("mount_smbfs 실패");
    }

    private static void RunMountSmbfs(string source, string mountPoint, bool nobrowse = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/sbin/mount_smbfs",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (nobrowse)
        {
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("nobrowse");
        }
        psi.ArgumentList.Add(source);
        psi.ArgumentList.Add(mountPoint);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("mount_smbfs를 실행할 수 없습니다.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException("SMB 마운트 시간이 초과되었습니다.");
        }

        if (process.ExitCode != 0)
        {
            var detail = string.Join('\n', new[] { stderr, stdout }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"mount_smbfs 실패 (코드 {process.ExitCode})"
                : detail);
        }
    }

    private static string BuildMacMountSource(
        string host,
        string shareName,
        string username,
        string password,
        bool smbScheme = false)
    {
        // mount_smbfs URL: //[[[domain;]user[:password]@]server[/share]
        // 공백 포함 공유명(예: Temp DATA)은 반드시 percent-encoding 필요.
        var user = username.Trim();
        string auth;
        if (user.Contains('\\'))
        {
            var parts = user.Split('\\', 2);
            auth = $"{EscapeMountComponent(parts[0])};{EscapeMountComponent(parts[1])}:{EscapeMountComponent(password)}";
        }
        else if (user.Contains(';'))
        {
            var sep = user.IndexOf(';');
            auth = $"{EscapeMountComponent(user[..sep])};{EscapeMountComponent(user[(sep + 1)..])}:{EscapeMountComponent(password)}";
        }
        else
        {
            auth = $"{EscapeMountComponent(user)}:{EscapeMountComponent(password)}";
        }

        var encodedShare = string.Join('/',
            shareName.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(EscapeMountComponent));

        var path = $"//{auth}@{host}/{encodedShare}";
        return smbScheme ? $"smb:{path}" : path;
    }

    private static string EscapeMountComponent(string value) =>
        Uri.EscapeDataString(value ?? string.Empty);

    private static IEnumerable<string> BuildMacUsernameCandidates(string username, string host)
    {
        var user = username.Trim();
        var set = new List<string>();
        void Add(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !set.Contains(value, StringComparer.OrdinalIgnoreCase))
                set.Add(value);
        }

        Add(user);
        if (!user.Contains('\\') && !user.Contains(';') && !user.Contains('@'))
        {
            Add($"{host};{user}");
            Add($".;{user}");
        }
        return set;
    }

    private static void TryUnmount(string mountPoint, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(mountPoint)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/sbin/umount",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (force) psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(mountPoint);
            using var process = Process.Start(psi);
            process?.WaitForExit(10000);
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: false);
        }
        catch
        {
            // ignore busy mount leftovers
        }
    }

    private static int TryAddConnection(string remoteName, string password, string username)
    {
        var resource = new NetResource
        {
            Scope = 0,
            Type = 1,
            DisplayType = 0,
            Usage = 0,
            LocalName = null,
            RemoteName = remoteName,
            Comment = null,
            Provider = null,
        };
        return NativeMethods.WNetAddConnection2(ref resource, password, username, 0);
    }

    private static (string host, string share) NormalizeHostAndShare(string host, string share)
    {
        var rawHost = (host ?? string.Empty).Trim().Trim('"');
        var rawShare = (share ?? string.Empty).Trim().Trim('"');

        // 호스트 칸에 \\IP\share 또는 IP\share 를 붙여 넣은 경우 분리
        var fromHost = TryParseUnc(rawHost);
        if (fromHost is not null)
        {
            rawHost = fromHost.Value.host;
            if (string.IsNullOrWhiteSpace(rawShare))
                rawShare = fromHost.Value.shareAndPath;
            else if (!rawShare.Contains('\\') && !rawShare.Contains('/'))
                rawShare = $"{fromHost.Value.shareAndPath}";
            else
                rawShare = $"{fromHost.Value.shareAndPath}\\{rawShare.Trim('\\', '/')}";
        }

        // 공유 칸에 \\IP\share\path 형태가 들어온 경우
        var fromShare = TryParseUnc(rawShare);
        if (fromShare is not null)
        {
            if (string.IsNullOrWhiteSpace(rawHost))
                rawHost = fromShare.Value.host;
            rawShare = fromShare.Value.shareAndPath;
        }

        rawHost = rawHost
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .Trim('\\', '/');

        // 포트가 붙은 경우 제거 (192.168.0.10:445)
        var colon = rawHost.IndexOf(':');
        if (colon > 0 && rawHost.IndexOf(':') == rawHost.LastIndexOf(':') && !rawHost.Contains(']'))
        {
            var maybePort = rawHost[(colon + 1)..];
            if (int.TryParse(maybePort, out _))
                rawHost = rawHost[..colon];
        }

        rawShare = rawShare.Replace('/', '\\').Trim().Trim('\\');
        return (rawHost, rawShare);
    }

    private static (string host, string shareAndPath)? TryParseUnc(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim().Replace('/', '\\');
        var hadUncPrefix = text.StartsWith(@"\\", StringComparison.Ordinal);
        while (text.StartsWith(@"\\", StringComparison.Ordinal))
            text = text[2..];

        if (!text.Contains('\\')) return null;
        var parts = text.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var maybeHost = parts[0];
        // "Temp DATA\_data" 같은 공유명\하위경로 는 UNC가 아님
        if (!hadUncPrefix && !LooksLikeHost(maybeHost))
            return null;

        return (maybeHost, string.Join('\\', parts.Skip(1)));
    }

    private static bool LooksLikeHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Contains(' ')) return false;
        if (System.Net.IPAddress.TryParse(value, out _)) return true;
        if (value.Contains('.')) return true;
        return value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    private static IEnumerable<string> BuildUsernameCandidates(string username, string host)
    {
        var user = username.Trim();
        var set = new List<string>();
        void Add(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !set.Contains(value, StringComparer.OrdinalIgnoreCase))
                set.Add(value);
        }

        Add(user);
        if (!user.Contains('\\') && !user.Contains('@'))
        {
            Add($@"{host}\{user}");
            Add($@".\{user}");
        }
        return set;
    }

    private static string DescribeSmbError(int code, string rootSharePath, string fullPath)
    {
        var hint = code switch
        {
            5 => "권한이 없습니다. 계정 권한 또는 공유 권한을 확인하세요.",
            53 => "네트워크 경로를 찾을 수 없습니다. NAS IP/이름과 PC가 같은 네트워크인지 확인하세요.",
            67 => "네트워크 이름(공유 폴더)을 찾을 수 없습니다.\n· '공유 폴더명'에는 탐색기 주소 \\IP\\ 다음에 보이는 첫 공유 이름을 넣으세요.\n· 하위 폴더면 공유명\\하위폴더 형식입니다.\n· Synology/QNAP의 표시 이름과 공유 이름이 다를 수 있습니다.",
            86 => "지정한 네트워크 암호가 올바르지 않습니다.",
            1326 => "사용자 이름 또는 비밀번호가 올바르지 않습니다.\nNAS 로컬 계정이면 '사용자명'만, 도메인이면 DOMAIN\\user 형식을 시도하세요.",
            1219 => "같은 NAS에 다른 계정으로 이미 연결되어 있습니다.\n형제 앱(WorkLog·FileChecker)과 탐색기가 쓰는 세션을 보호하기 위해 자동으로 끊지 않습니다.\n· 모든 앱·탐색기에서 같은 NAS 계정을 쓰거나\n· 해당 NAS 탐색기 창을 닫고 cmd에서  net use \\\\서버 /delete /y  후 다시 연결하세요.",
            64 => "네트워크 이름이 삭제되었거나 서버에 닿지 않습니다.",
            1231 => "네트워크 위치가 원격 연결을 수락하지 않습니다. NAS SMB 서비스 활성화를 확인하세요.",
            _ => "공유명, IP, 계정 정보를 다시 확인하세요.",
        };
        return $"SMB 연결에 실패했습니다.\n대상: {rootSharePath}\n작업 경로: {fullPath}\n오류 코드: {code} (0x{code:X})\n{hint}";
    }

    public void Disconnect()
    {
        if (root is null && macMountPoint is null) return;

        if (OperatingSystem.IsMacOS())
        {
            var mount = macMountPoint;
            var owned = macMountOwned;
            root = null;
            macMountPoint = null;
            macMountOwned = false;
            if (owned && !string.IsNullOrWhiteSpace(mount))
            {
                TryUnmount(mount, force: true);
                TryDeleteDirectory(mount);
            }
            return;
        }

        // Windows: SMB 세션은 유지 (WorkLog 등 동일 NAS 동시 사용)
        DisconnectLocalOnly();
    }

    private void DisconnectLocalOnly()
    {
        root = null;
        // macMountPoint 는 ConnectMac 쪽에서만 다룸
    }

    private static bool CanUseUncPath(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return false;
            _ = Directory.GetFileSystemEntries(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string[] GetJsonFiles()
    {
        EnsureConnected();
        return Directory.EnumerateFiles(root!, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Where(name => !name.StartsWith('_'))
            .Where(name => !name.Equals("schedule-presets.json", StringComparison.OrdinalIgnoreCase))
            .Where(name => !name.Equals("speaker-roster.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name)
            .ToArray();
    }

    public string ReadJson(string name)
    {
        return ReadJsonWithRevision(name).Json;
    }

    public (string Json, string Revision) ReadJsonWithRevision(string name)
    {
        var path = Resolve(name);
        var json = File.ReadAllText(path);
        return (json, GetFileRevision(path));
    }

    public void WriteJson(string name, string contents)
    {
        WriteJsonWithRevision(name, contents, expectedRevision: null);
    }

    public WriteJsonResult WriteJsonWithRevision(string name, string contents, string? expectedRevision)
    {
        var path = Resolve(name);
        if (!string.IsNullOrEmpty(expectedRevision) && File.Exists(path))
        {
            var current = GetFileRevision(path);
            if (!string.Equals(current, expectedRevision, StringComparison.Ordinal))
            {
                return new WriteJsonResult
                {
                    Saved = false,
                    Revision = current,
                    Content = File.ReadAllText(path),
                };
            }
        }

        using var document = JsonDocument.Parse(contents);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }));
        return new WriteJsonResult
        {
            Saved = true,
            Revision = GetFileRevision(path),
        };
    }

    private static string GetFileRevision(string path) =>
        File.GetLastWriteTimeUtc(path).Ticks.ToString();

    public sealed class WriteJsonResult
    {
        public bool Saved { get; init; }
        public string Revision { get; init; } = "";
        public string? Content { get; init; }
    }

    public object CreateBackup(string name, string contents)
    {
        EnsureConnected();
        ValidateJsonFileName(name);
        using var document = JsonDocument.Parse(contents);
        var pretty = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });

        var backupDir = Path.Combine(root!, "_backup");
        Directory.CreateDirectory(backupDir);

        var baseName = Path.GetFileNameWithoutExtension(name);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipName = $"{baseName}_{stamp}.zip";
        var zipPath = Path.Combine(backupDir, zipName);

        if (File.Exists(zipPath)) File.Delete(zipPath);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry($"{baseName}.json", CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(pretty);
        }

        var info = new FileInfo(zipPath);
        return new
        {
            backup = zipName,
            folder = "_backup",
            size = info.Length,
            created = info.LastWriteTime,
        };
    }

    public object ListBackups(string name)
    {
        EnsureConnected();
        ValidateJsonFileName(name);
        var backupDir = Path.Combine(root!, "_backup");
        if (!Directory.Exists(backupDir)) return Array.Empty<object>();

        var baseName = Path.GetFileNameWithoutExtension(name);
        return Directory.EnumerateFiles(backupDir, $"{baseName}_*.zip")
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new
                {
                    name = info.Name,
                    size = info.Length,
                    modified = info.LastWriteTime,
                };
            })
            .OrderByDescending(item => item.modified)
            .ToArray();
    }

    public string RestoreBackup(string name, string backup) =>
        RestoreBackupWithRevision(name, backup).Json;

    public (string Json, string Revision) RestoreBackupWithRevision(string name, string backup)
    {
        EnsureConnected();
        ValidateJsonFileName(name);
        if (string.IsNullOrWhiteSpace(backup) || Path.GetFileName(backup) != backup || !backup.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("허용되지 않은 백업 파일명입니다.");

        var zipPath = Path.Combine(root!, "_backup", backup);
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("백업 파일을 찾을 수 없습니다.", backup);

        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(item =>
            item.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(Path.GetDirectoryName(item.FullName)?.Trim('\\', '/')));
        if (entry is null)
            throw new InvalidOperationException("백업 ZIP 안에 JSON 파일이 없습니다.");

        using var reader = new StreamReader(entry.Open());
        var contents = reader.ReadToEnd();
        var written = WriteJsonWithRevision(name, contents, expectedRevision: null);
        return (contents, written.Revision);
    }

    private const string OfficialDocumentsFolder = "_official_documents";
    private const string OfficialDocumentsIndexFile = "links.json";
    private static readonly HashSet<string> AllowedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
    };

    public object[] ListOfficialDocuments()
    {
        EnsureConnected();
        var dir = GetOfficialDocumentsDirectory();
        if (!Directory.Exists(dir)) return Array.Empty<object>();
        return Directory.EnumerateFiles(dir)
            .Where(path => AllowedDocumentExtensions.Contains(Path.GetExtension(path)))
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new
                {
                    name = info.Name,
                    size = info.Length,
                    modified = info.LastWriteTime,
                };
            })
            .OrderByDescending(item => item.modified)
            .Cast<object>()
            .ToArray();
    }

    public (byte[] Bytes, string ContentType) ReadOfficialDocument(string name)
    {
        EnsureConnected();
        var safeName = ValidateDocumentFileName(name);
        var path = Path.Combine(GetOfficialDocumentsDirectory(), safeName);
        if (!File.Exists(path)) throw new FileNotFoundException("공문 파일을 찾을 수 없습니다.", safeName);
        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg",
        };
        return (File.ReadAllBytes(path), contentType);
    }

    public object UploadOfficialDocument(
        byte[] bytes,
        string originalFileName,
        string scheduleId,
        string scheduleDate,
        string scheduleTitle)
    {
        EnsureConnected();
        if (bytes.Length == 0) throw new ArgumentException("빈 파일은 업로드할 수 없습니다.");
        if (bytes.Length > 20 * 1024 * 1024) throw new ArgumentException("공문 이미지는 20MB 이하여야 합니다.");

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedDocumentExtensions.Contains(ext))
            throw new ArgumentException("jpg, jpeg, png, gif, webp, bmp 이미지만 업로드할 수 있습니다.");

        var dir = GetOfficialDocumentsDirectory();
        Directory.CreateDirectory(dir);
        var storedName = BuildDocumentStoredName(dir, originalFileName, scheduleDate, ext);
        var targetPath = Path.Combine(dir, storedName);
        File.WriteAllBytes(targetPath, bytes);

        var entry = UpsertDocumentIndexEntry(storedName, originalFileName, scheduleId, scheduleDate, scheduleTitle);
        return new
        {
            fileName = storedName,
            folder = OfficialDocumentsFolder,
            originalName = originalFileName,
            entry,
        };
    }

    public object LinkOfficialDocument(
        string fileName,
        string scheduleId,
        string scheduleDate,
        string scheduleTitle)
    {
        EnsureConnected();
        var safeName = ValidateDocumentFileName(fileName);
        var path = Path.Combine(GetOfficialDocumentsDirectory(), safeName);
        if (!File.Exists(path)) throw new FileNotFoundException("공문 파일을 찾을 수 없습니다.", safeName);
        var entry = UpsertDocumentIndexEntry(safeName, safeName, scheduleId, scheduleDate, scheduleTitle);
        return new
        {
            fileName = safeName,
            folder = OfficialDocumentsFolder,
            entry,
        };
    }

    public string ReadOfficialDocumentIndex()
    {
        EnsureConnected();
        var path = GetOfficialDocumentsIndexPath();
        if (!File.Exists(path))
        {
            return """{"documents":[]}""";
        }
        return File.ReadAllText(path);
    }

    public void WriteOfficialDocumentIndex(string contents)
    {
        EnsureConnected();
        using var document = JsonDocument.Parse(contents);
        var dir = GetOfficialDocumentsDirectory();
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            GetOfficialDocumentsIndexPath(),
            JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }));
    }

    private object UpsertDocumentIndexEntry(
        string fileName,
        string originalName,
        string scheduleId,
        string scheduleDate,
        string scheduleTitle)
    {
        var path = GetOfficialDocumentsIndexPath();
        Directory.CreateDirectory(GetOfficialDocumentsDirectory());

        using var document = File.Exists(path)
            ? JsonDocument.Parse(File.ReadAllText(path))
            : JsonDocument.Parse("""{"documents":[]}""");

        var documents = new List<Dictionary<string, object?>>();
        if (document.RootElement.TryGetProperty("documents", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var map = new Dictionary<string, object?>();
                foreach (var prop in item.EnumerateObject())
                {
                    map[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.TryGetInt64(out var n) ? n : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText(),
                    };
                }
                documents.Add(map);
            }
        }

        documents.RemoveAll(item =>
            string.Equals(Convert.ToString(item.GetValueOrDefault("scheduleId")), scheduleId, StringComparison.OrdinalIgnoreCase));

        var entry = new Dictionary<string, object?>
        {
            ["scheduleId"] = scheduleId ?? "",
            ["scheduleDate"] = scheduleDate ?? "",
            ["scheduleTitle"] = scheduleTitle ?? "",
            ["fileName"] = fileName,
            ["originalName"] = originalName ?? fileName,
            ["folder"] = OfficialDocumentsFolder,
            ["linkedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["schedule"] = new Dictionary<string, object?>
            {
                ["id"] = scheduleId ?? "",
                ["scheduleDate"] = scheduleDate ?? "",
                ["scheduleTitle"] = scheduleTitle ?? "",
                ["officialDocument"] = fileName,
            },
        };
        documents.Add(entry);

        var payload = new Dictionary<string, object?> { ["documents"] = documents };
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return entry;
    }

    private string GetOfficialDocumentsDirectory() =>
        Path.Combine(root!, OfficialDocumentsFolder);

    private string GetOfficialDocumentsIndexPath() =>
        Path.Combine(GetOfficialDocumentsDirectory(), OfficialDocumentsIndexFile);

    private static string BuildDocumentStoredName(
        string directory,
        string originalFileName,
        string scheduleDate,
        string extension)
    {
        var ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        var rawBase = Path.GetFileNameWithoutExtension(originalFileName ?? "");
        var baseName = SanitizeFileToken(rawBase, "document");
        var ymd = ToOfficialDocYmd8(scheduleDate);

        // 연결 목록 규칙: YYYYMMDD(8자리)로 시작해야 함
        if (!Regex.IsMatch(baseName, @"^\d{8}(?!\d)"))
            baseName = $"{ymd}_{baseName}";

        if (baseName.Length > 80)
            baseName = baseName[..80].TrimEnd('_');

        return EnsureUniqueDocumentFileName(directory, baseName + ext);
    }

    private static string ToOfficialDocYmd8(string scheduleDate)
    {
        if (!string.IsNullOrWhiteSpace(scheduleDate))
        {
            var trimmed = scheduleDate.Trim();
            if (Regex.IsMatch(trimmed, @"^\d{8}$"))
                return trimmed;
            if (DateTime.TryParse(trimmed, out var parsed))
                return parsed.ToString("yyyyMMdd");
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            if (digits.Length >= 8)
                return digits[..8];
        }
        return DateTime.Now.ToString("yyyyMMdd");
    }

    private static string EnsureUniqueDocumentFileName(string directory, string fileName)
    {
        var candidate = fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var n = 1; File.Exists(Path.Combine(directory, candidate)); n++)
        {
            candidate = n <= 99
                ? $"{stem}_{n:00}{ext}"
                : $"{stem}_{Guid.NewGuid().ToString("N")[..6]}{ext}";
            if (n > 99) break;
        }
        return candidate;
    }

    private static string SanitizeFileToken(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalid.Contains(ch) || ch is '/' or '\\' or ' ' ? '_' : ch).ToArray());
        while (cleaned.Contains("__", StringComparison.Ordinal)) cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        cleaned = cleaned.Trim('_');
        if (cleaned.Length > 40) cleaned = cleaned[..40].Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static string ValidateDocumentFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name)
            throw new ArgumentException("허용되지 않은 공문 파일명입니다.");
        if (!AllowedDocumentExtensions.Contains(Path.GetExtension(name)))
            throw new ArgumentException("허용되지 않은 공문 확장자입니다.");
        return name;
    }

    private string Resolve(string name)
    {
        EnsureConnected();
        ValidateJsonFileName(name);
        return Path.Combine(root!, name);
    }

    private static void ValidateJsonFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("허용되지 않은 파일명입니다.");
    }

    private void EnsureConnected()
    {
        if (root is null) throw new InvalidOperationException("먼저 SMB 연결을 확인하세요.");
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NetResource
{
    public int Scope;
    public int Type;
    public int DisplayType;
    public int Usage;
    public string? LocalName;
    public string? RemoteName;
    public string? Comment;
    public string? Provider;
}

internal static class NativeMethods
{
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    public static extern int WNetAddConnection2(ref NetResource netResource, string? password, string? username, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}

