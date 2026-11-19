using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SnapTranslate;

/// <summary>
/// 멈춤·지연 원인을 찾기 위한 진단 기록. config.json 의 DebugLog 가 true 일 때만 실행 파일 옆 debug.log 에 쓴다.
/// 시각·걸린 시간·크기·앞에 있는 프로그램 이름만 적고, 인식한 글이나 화면 그림은 적지 않는다.
/// </summary>
internal static class DebugLog
{
    public static bool Enabled { get; set; }

    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "debug.log");

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate) File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}\r\n");
        }
        catch (Exception)
        {
            // 기록 실패가 프로그램 동작을 막으면 안 된다
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    /// <summary>지금 맨 앞(포커스)에 있는 창의 프로그램 이름과, 그 창이 모니터 전체를 덮는지</summary>
    public static string Foreground()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "없음";
            GetWindowThreadProcessId(hwnd, out uint pid);
            string name;
            try { name = Process.GetProcessById((int)pid).ProcessName; }
            catch (Exception) { name = "pid" + pid; }

            if (!GetWindowRect(hwnd, out Rect r)) return name;
            var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            bool fullscreen = Screen.FromRectangle(bounds).Bounds == bounds;
            return $"{name} {bounds.Width}x{bounds.Height}{(fullscreen ? " 전체화면크기" : "")}";
        }
        catch (Exception ex)
        {
            return "확인 실패: " + ex.Message;
        }
    }
}

/// <summary>
/// 캡처 뒤 일정 시간 동안 멈춤을 감시한다.
/// - 화면(UI) 스레드가 늦게 깨면 "UI 멈춤" — 이 프로그램 자신이 바빴다는 뜻
/// - 별도 스레드까지 늦게 깨면 "전체 멈춤" — 이 프로그램 밖(시스템·그래픽) 문제일 가능성
/// - 맨 앞 창이 바뀌면 그 시각을 적는다(게임이 포커스를 잃는지 확인)
/// </summary>
internal sealed class HitchWatcher : IDisposable
{
    private const int IntervalMs = 50;
    private const int ThresholdMs = 250;
    private const int WatchMs = 15000;

    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = IntervalMs };
    private readonly System.Threading.Timer _poolTimer;
    private readonly Stopwatch _clock = new();
    private long _lastUi, _lastPool;
    private string _foreground = "";

    public HitchWatcher()
    {
        _uiTimer.Tick += (_, _) => OnUiTick();
        _poolTimer = new System.Threading.Timer(_ => OnPoolTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>감시를 (다시) 시작한다</summary>
    public void Start()
    {
        if (!DebugLog.Enabled) return;
        _clock.Restart();
        _lastUi = _lastPool = 0;
        _foreground = "";
        _uiTimer.Start();
        _poolTimer.Change(IntervalMs, IntervalMs);
    }

    private void OnUiTick()
    {
        long now = _clock.ElapsedMilliseconds;
        long gap = now - _lastUi;
        _lastUi = now;
        if (gap > ThresholdMs) DebugLog.Write($"  UI 멈춤 {gap}ms");

        string foreground = DebugLog.Foreground();
        if (foreground != _foreground)
        {
            _foreground = foreground;
            DebugLog.Write($"  맨 앞 창: {foreground}");
        }

        if (now > WatchMs)
        {
            _uiTimer.Stop();
            _poolTimer.Change(Timeout.Infinite, Timeout.Infinite);
            DebugLog.Write("  감시 끝");
        }
    }

    private void OnPoolTick()
    {
        long now = _clock.ElapsedMilliseconds;
        long gap = now - Interlocked.Exchange(ref _lastPool, now);
        if (gap > ThresholdMs && gap < WatchMs) DebugLog.Write($"  전체 멈춤 {gap}ms");
    }

    public void Dispose()
    {
        _uiTimer.Dispose();
        _poolTimer.Dispose();
    }
}
