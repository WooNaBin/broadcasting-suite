using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

namespace CtrlOne.Services;

/// <summary>
/// 대시보드 UI(SignalR) 세션을 추적한다. 브라우저가 모두 닫히면 호스트를 종료한다.
/// </summary>
public sealed class UiSessionGuard
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<UiSessionGuard> _logger;
    private readonly object _sync = new();
    private int _clients;
    private bool _hadClient;
    private CancellationTokenSource? _exitCts;

    /// <summary>마지막 UI 연결이 끊긴 뒤 종료까지 유예 (새로고침/재연결 여유).</summary>
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(8);

    public UiSessionGuard(IHostApplicationLifetime lifetime, ILogger<UiSessionGuard> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
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

    /// <summary>pagehide/sendBeacon — 새로고침 시 재연결되면 취소됨.</summary>
    public void NotifyUiClosing()
    {
        lock (_sync)
        {
            if (_hadClient)
                ScheduleExitLocked("브라우저 종료 신호");
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

                StartupConsole.WriteLine("CtrlOne 종료 중…");
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

/// <summary>
/// --console 또는 디버그에서만 보이는 시작/상태 메시지.
/// WinExe(기본)에서는 콘솔 창이 없다.
/// </summary>
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
        {
            // 이미 콘솔에 붙어 있으면(dotnet run) AllocConsole는 실패해도 됨
            AllocConsole();
        }

        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // ignore
        }

        _enabled = true;
    }

    public static void WriteLine(string message)
    {
        if (!_enabled)
            return;
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
        catch
        {
            // ignore
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
}
