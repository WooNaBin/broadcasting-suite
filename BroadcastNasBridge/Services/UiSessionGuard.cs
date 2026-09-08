using System.Runtime.InteropServices;

namespace BroadcastNasBridge.Services;

/// <summary>
/// 시작 인덱스·기능 탭 세션을 추적한다. 모두 닫히면 5초 후 호스트 종료.
/// </summary>
public sealed class UiSessionGuard
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<UiSessionGuard> _logger;
    private readonly object _sync = new();
    private int _clients;
    private bool _hadClient;
    private CancellationTokenSource? _exitCts;

    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(5);

    public UiSessionGuard(IHostApplicationLifetime lifetime, ILogger<UiSessionGuard> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public int ActiveSessions
    {
        get { lock (_sync) return _clients; }
    }

    public void ClientConnected()
    {
        lock (_sync)
        {
            _clients++;
            _hadClient = true;
            CancelExitLocked();
        }

        StartupConsole.WriteLine($"UI 연결됨 (세션 {_clients})");
    }

    public void ClientDisconnected()
    {
        lock (_sync)
        {
            _clients = Math.Max(0, _clients - 1);
            if (_clients == 0 && _hadClient)
                ScheduleExitLocked("마지막 UI 연결 종료");
        }
    }

    public void NotifyUiClosing()
    {
        lock (_sync)
        {
            if (_hadClient)
                ScheduleExitLocked("브라우저 종료 신호");
        }
    }

    public void Ping()
    {
        lock (_sync)
        {
            if (_clients == 0 && _hadClient)
            {
                _clients = 1;
                CancelExitLocked();
            }
            else
            {
                CancelExitLocked();
            }
        }
    }

    private void ScheduleExitLocked(string reason)
    {
        CancelExitLocked();
        _exitCts = new CancellationTokenSource();
        var token = _exitCts.Token;
        StartupConsole.WriteLine($"{reason} — {ExitGrace.TotalSeconds:0.#}초 후 종료 (재연결 시 취소)");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ExitGrace, token).ConfigureAwait(false);
                lock (_sync)
                {
                    if (_clients > 0)
                        return;
                }

                StartupConsole.WriteLine("BroadcastNasBridge 종료 중…");
                _logger.LogInformation("UI 세션 없음 — 애플리케이션 종료 ({Reason})", reason);
                _lifetime.StopApplication();
            }
            catch (OperationCanceledException)
            {
                // 재연결됨
            }
        }, CancellationToken.None);
    }

    private void CancelExitLocked()
    {
        if (_exitCts is null)
            return;
        _exitCts.Cancel();
        _exitCts.Dispose();
        _exitCts = null;
    }
}

public static class StartupConsole
{
    private static bool _enabled;

    public static bool Enabled => _enabled;

    public static void EnableIfRequested(string[] args)
    {
        var want = args.Any(a =>
            a.Equals("--console", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-console", StringComparison.OrdinalIgnoreCase));

#if DEBUG
        want = true;
#endif

        if (!want)
            return;

        if (OperatingSystem.IsWindows())
            AllocConsole();

        try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
        catch { /* ignore */ }

        _enabled = true;
    }

    public static void WriteLine(string message)
    {
        if (!_enabled) return;
        try { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}"); }
        catch { /* ignore */ }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
}
