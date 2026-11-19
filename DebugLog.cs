using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SnapTranslate;

/// <summary>
/// 문제 신고용 진단 기록. 설정(정보 화면)에서 켰을 때만 실행 파일 옆 SnapTranslate-log.txt 에 쓴다.
/// 시각·걸린 시간·크기·오류 내용·앞에 있는 프로그램 이름만 적고, 인식한 글·번역문·화면 그림·API 키는 적지 않는다.
/// 꺼져 있으면 파일을 만들지도 열지도 않는다(프로그램이 죽는 오류만 예외로 남긴다).
/// </summary>
internal static class DebugLog
{
    public const string FileName = "SnapTranslate-log.txt";

    // 기록 파일이 이 크기를 넘으면 뒤쪽(최근)만 남긴다
    private const long MaxBytes = 1024 * 1024;
    private const int KeepBytes = 300 * 1024;

    public static bool Enabled { get; private set; }

    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>기록을 켜거나 끈다. 켤 때는 환경 정보(버전·Windows·화면·설정 요약)를 먼저 적는다</summary>
    public static void SetEnabled(bool enabled, AppConfig? config)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        if (!enabled) return;
        Trim();
        Append(Header(config));
    }

    public static void Write(string message)
    {
        if (!Enabled) return;
        Append($"{DateTime.Now:HH:mm:ss.fff} {message}\r\n");
    }

    /// <summary>실패를 예외 종류·내용·호출 위치와 함께 적는다(기록이 켜져 있을 때만)</summary>
    public static void Error(string where, Exception ex)
    {
        if (!Enabled) return;
        Append($"{DateTime.Now:HH:mm:ss.fff} [오류] {where}: {Describe(ex)}\r\n");
    }

    /// <summary>
    /// 처리하지 못한 오류(프로그램이 죽거나 오류 창이 뜨는 경우)는 기록이 꺼져 있어도 남긴다.
    /// 평소에는 불리지 않으므로 동작에 영향이 없고, 신고받을 때 원인을 알 수 있는 유일한 단서다.
    /// </summary>
    public static void Crash(string where, Exception ex)
    {
        string head = Enabled ? "" : $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} SnapTranslate {AppInfo.VersionText} (진단 기록 꺼짐, 오류만 기록) ===\r\n";
        Append($"{head}{DateTime.Now:HH:mm:ss.fff} [처리 못 한 오류] {where}: {Describe(ex)}\r\n");
    }

    /// <summary>기록 파일 내용을 읽는다(없으면 빈 글)</summary>
    public static string ReadAll()
    {
        try
        {
            lock (Gate) return File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
        }
        catch (Exception ex)
        {
            return "기록 파일을 읽지 못했습니다: " + ex.Message;
        }
    }

    public static bool Exists => File.Exists(FilePath);

    /// <summary>기록 파일을 지운다. 켜져 있으면 환경 정보부터 다시 적는다</summary>
    public static void Clear(AppConfig? config)
    {
        try
        {
            lock (Gate) File.Delete(FilePath);
        }
        catch (Exception)
        {
            // 지우지 못해도 프로그램 동작에는 영향이 없다
        }
        if (Enabled) Append(Header(config));
    }

    private static void Append(string text)
    {
        try
        {
            lock (Gate) File.AppendAllText(FilePath, text, Encoding.UTF8);
        }
        catch (Exception)
        {
            // 기록 실패가 프로그램 동작을 막으면 안 된다
        }
    }

    /// <summary>파일이 너무 커졌으면 최근 부분만 남긴다</summary>
    private static void Trim()
    {
        try
        {
            lock (Gate)
            {
                var info = new FileInfo(FilePath);
                if (!info.Exists || info.Length <= MaxBytes) return;
                byte[] all = File.ReadAllBytes(FilePath);
                int start = all.Length - KeepBytes;
                // 줄 중간에서 시작하지 않게 다음 줄 처음으로 옮긴다
                while (start < all.Length && all[start] != (byte)'\n') start++;
                start = Math.Min(all.Length, start + 1);
                File.WriteAllText(FilePath, "(앞부분은 파일이 커져서 지움)\r\n" + Encoding.UTF8.GetString(all, start, all.Length - start), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // 줄이지 못해도 기록은 계속한다
        }
    }

    /// <summary>신고받은 사람이 환경을 알 수 있게 적는 머리말. 개인 정보(계정 이름·경로·API 키)는 넣지 않는다</summary>
    private static string Header(AppConfig? config)
    {
        var sb = new StringBuilder();
        sb.Append($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} SnapTranslate {AppInfo.VersionText} 진단 기록 시작 ===\r\n");
        try
        {
            sb.Append($"Windows: {Environment.OSVersion.Version} / .NET {Environment.Version} / {(AppInfo.IsSelfContained ? "런타임 포함판" : "런타임 별도판")}\r\n");
            sb.Append("화면: " + string.Join(", ", Screen.AllScreens.Select(s =>
                $"{s.Bounds.Width}x{s.Bounds.Height}@({s.Bounds.X},{s.Bounds.Y}){(s.Primary ? " 주" : "")}")) + "\r\n");
            sb.Append("읽을 수 있는 언어: " + string.Join(", ", OcrService.InstalledLanguageNames()) + "\r\n");
            if (config != null)
            {
                sb.Append($"설정: 단축키={config.Hotkey} 원문={config.SourceLang} 번역={config.TargetLang}/{config.AltTargetLang} " +
                          $"번역기={string.Join(">", config.TranslateOrder.Select(e => e.Length == 0 ? "-" : e))} " +
                          $"Gemini키={(config.GeminiApiKey.Length > 0 ? "있음" : "없음")} 모델={config.GeminiModel}\r\n");
                sb.Append($"설정: 포커스유지={config.KeepFocusOnApp} 창재사용={config.ReuseWindow} 자동닫힘={config.AutoCloseSeconds}초 " +
                          $"위치={config.PositionMode} 불투명도={config.Opacity} 크기={config.WindowWidth}x{config.OriginalHeight}+{config.TranslatedHeight} " +
                          $"기록저장={config.SaveHistory} 용어={config.Glossary.Count}개\r\n");
            }
        }
        catch (Exception ex)
        {
            sb.Append("환경 정보 일부를 읽지 못함: " + ex.Message + "\r\n");
        }
        return sb.ToString();
    }

    /// <summary>예외를 한 덩어리 글로 만든다. 경로에 섞인 계정 이름은 가린다</summary>
    private static string Describe(Exception ex)
    {
        string text = ex.ToString();
        try
        {
            string user = Environment.UserName;
            if (user.Length > 1) text = text.Replace(user, "<사용자>", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // 가리지 못해도 기록은 남긴다
        }
        return text;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    /// <summary>
    /// 지금 맨 앞(포커스)에 있는 창의 프로그램 이름과, 그 창이 모니터 전체를 덮는지.
    /// 기록이 꺼져 있으면 아무것도 조사하지 않고 빈 글을 돌려준다(호출하는 쪽이 조건을 따지지 않아도 비용이 들지 않게).
    /// </summary>
    public static string Foreground()
    {
        if (!Enabled) return "";
        return ForegroundNow();
    }

    /// <summary>기록 설정과 관계없이 맨 앞 창을 조사한다(점검 모드용)</summary>
    public static string ForegroundNow()
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
/// 캡처 뒤 일정 시간 동안 멈춤을 감시한다(진단 기록이 켜져 있을 때만 움직인다).
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

    private void Stop()
    {
        _uiTimer.Stop();
        _poolTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnUiTick()
    {
        // 감시하는 도중에 기록을 껐으면 바로 멈춘다
        if (!DebugLog.Enabled) { Stop(); return; }

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
            Stop();
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
