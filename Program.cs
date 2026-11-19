using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SnapTranslate;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 화면 없이 OCR·번역 경로만 확인하는 자체 점검 모드
        if (args.Length >= 2 && args[0] == "--selftest")
            return SelfTest.Run(args[1]);

        // 창을 화면에 보이지 않게 띄워 그림 파일로 저장하는 모양 점검 모드
        if (args.Length >= 2 && args[0] == "--shot")
            return ShotTest.Run(args[1]);

        // 주어진 글을 여러 모양(크기·색)으로 그려, 엔진마다 어떻게 읽고 어느 것이 골라지는지 적는 점검 모드
        if (args.Length >= 3 && args[0] == "--ocr-probe")
            return SelfTest.RunOcrProbe(args[1], args[2]);

        // 업데이트를 설치하지만 다시 시작하지는 않는 점검 모드(설치 과정 확인용)
        if (args.Length >= 2 && args[0] == "--update-test")
            return SelfTest.RunUpdate(args[1]);

        // 업데이트 직후 새로 띄워진 경우: 이전 프로그램이 완전히 끝날 때까지 기다린다(중복 실행으로 오해하지 않게)
        if (args.Length >= 2 && args[0] == "--after-update" && int.TryParse(args[1], out int oldPid))
        {
            try { Process.GetProcessById(oldPid).WaitForExit(10000); }
            catch (ArgumentException) { /* 이미 끝났다 */ }
        }

        // 중복 실행 방지
        using var mutex = new Mutex(true, "SnapTranslate.SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("SnapTranslate 가 이미 실행 중입니다. 트레이 아이콘을 확인하세요.", "SnapTranslate");
            return 0;
        }

        UpdateService.CleanupOldFiles();
        Application.Run(new TrayContext());
        return 0;
    }
}

/// <summary>실행 파일 옆 config.json 에 저장하는 설정</summary>
internal sealed class AppConfig
{
    /// <summary>캡처 단축키. 예: Ctrl+Alt+T, Win+Shift+Z, Ctrl+F9</summary>
    public string Hotkey { get; set; } = "Win+Shift+Q";

    /// <summary>번역 결과 언어</summary>
    public string TargetLang { get; set; } = "ko";

    /// <summary>원문이 이미 TargetLang 이면 이 언어로 번역한다</summary>
    public string AltTargetLang { get; set; } = "en";

    /// <summary>
    /// 번역기 우선순위(google / mymemory / gemini). 앞의 것부터 쓰고, 실패하면 다음 것으로 넘어간다.
    /// 하나만 적으면 그 번역기만 쓴다.
    /// </summary>
    public List<string> TranslateOrder { get; set; } = [Translator.Google, Translator.MyMemory, Translator.Gemini];

    /// <summary>Gemini 번역에 쓰는 사용자 API 키. 비어 있으면 Gemini 를 쓰지 않는다</summary>
    public string GeminiApiKey { get; set; } = "";

    /// <summary>Gemini 모델 이름. 보통 바꿀 일이 없다</summary>
    public string GeminiModel { get; set; } = Translator.DefaultGeminiModel;

    /// <summary>번역이 끝나면 번역문을 클립보드에 자동 복사</summary>
    public bool CopyTranslation { get; set; } = false;

    /// <summary>true 면 결과 창 하나를 계속 쓰며 내용만 바꾼다. false 면 캡처할 때마다 새 창을 연다</summary>
    public bool ReuseWindow { get; set; } = true;

    /// <summary>
    /// 캡처·번역하는 동안 원래 쓰던 프로그램(게임 등)이 포커스를 잃지 않게 할지.
    /// 켜면 선택 화면과 결과 창이 포커스를 가져가지 않는다(결과 창은 클릭하면 포커스를 받는다).
    /// </summary>
    public bool KeepFocusOnApp { get; set; } = true;

    /// <summary>결과 창에서 Esc 를 누르면 창을 닫을지</summary>
    public bool CloseOnEsc { get; set; } = true;

    /// <summary>멈춤·지연 원인을 찾는 진단 기록을 실행 파일 옆 debug.log 에 남길지(기본은 끔)</summary>
    public bool DebugLog { get; set; } = false;

    /// <summary>프로그램을 시작할 때 Windows 알림으로 단축키 안내를 보일지(기본은 끔)</summary>
    public bool ShowStartupNotice { get; set; } = false;

    /// <summary>프로그램을 시작할 때 새 버전이 있는지 확인할지</summary>
    public bool CheckUpdateOnStart { get; set; } = true;

    /// <summary>원문 언어 기본값. auto 면 자동 감지</summary>
    public string SourceLang { get; set; } = "auto";

    /// <summary>결과 창 불투명도(30~100, 100 이 완전 불투명)</summary>
    public int Opacity { get; set; } = 95;

    /// <summary>결과 창 가로 크기(100% 배율 기준 픽셀)</summary>
    public int WindowWidth { get; set; } = 560;

    /// <summary>원문 칸 세로 크기(100% 배율 기준 픽셀)</summary>
    public int OriginalHeight { get; set; } = 100;

    /// <summary>번역 칸 세로 크기(100% 배율 기준 픽셀)</summary>
    public int TranslatedHeight { get; set; } = 170;

    /// <summary>결과 창 위치: Selection(선택 영역 근처) / TopLeft / TopRight / BottomLeft / BottomRight / Custom(직접 지정)</summary>
    public string PositionMode { get; set; } = "Selection";

    /// <summary>PositionMode 가 Custom 일 때 창 왼쪽 위 화면 좌표</summary>
    public int PositionX { get; set; } = 100;
    public int PositionY { get; set; } = 100;

    /// <summary>번역이 끝난 뒤 창이 저절로 닫히는 시간(초). 0 이면 닫지 않는다</summary>
    public int AutoCloseSeconds { get; set; } = 60;

    public double OriginalFontSize { get; set; } = 9.5;
    public double TranslatedFontSize { get; set; } = 12;

    /// <summary>글자 색(#RRGGBB)</summary>
    public string OriginalColor { get; set; } = "#969BA8";
    public string TranslatedColor { get; set; } = "#ECEEF2";

    /// <summary>원문·번역 글꼴</summary>
    public string FontName { get; set; } = "맑은 고딕";

    // 결과 창 색(#RRGGBB)
    public string BackgroundColor { get; set; } = "#1E1F24";   // 창 배경
    public string CardColor { get; set; } = "#2A2C33";         // 원문·번역 칸 배경
    public string AccentColor { get; set; } = "#4F8CFF";       // 칸 제목·복사 버튼·슬라이더
    public string LabelColor { get; set; } = "#969BA8";        // 제목·상태 등 보조 글자
    public string FieldColor { get; set; } = "#363942";        // 언어 목록·버튼 배경

    /// <summary>설정 창 미리보기용 사본을 만든다</summary>
    public AppConfig Clone() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    /// <summary>모양(크기·글자·색·표시 항목) 값만 other 에서 가져온다</summary>
    public void CopyAppearanceFrom(AppConfig other)
    {
        Opacity = other.Opacity;
        WindowWidth = other.WindowWidth;
        OriginalHeight = other.OriginalHeight;
        TranslatedHeight = other.TranslatedHeight;
        OriginalFontSize = other.OriginalFontSize;
        TranslatedFontSize = other.TranslatedFontSize;
        OriginalColor = other.OriginalColor;
        TranslatedColor = other.TranslatedColor;
        FontName = other.FontName;
        BackgroundColor = other.BackgroundColor;
        CardColor = other.CardColor;
        AccentColor = other.AccentColor;
        LabelColor = other.LabelColor;
        FieldColor = other.FieldColor;
        ShowTitleBar = other.ShowTitleBar;
        ShowLanguageRow = other.ShowLanguageRow;
        ShowBottomBar = other.ShowBottomBar;
        ShowCaptions = other.ShowCaptions;
        ShowOriginal = other.ShowOriginal;
        ShowTranslated = other.ShowTranslated;
        ShowEngine = other.ShowEngine;
    }

    // 원문·번역 칸 말고 나머지 부분을 보일지
    public bool ShowTitleBar { get; set; } = true;
    public bool ShowLanguageRow { get; set; } = true;
    public bool ShowBottomBar { get; set; } = true;
    public bool ShowCaptions { get; set; } = true;

    // 원문 칸·번역 칸을 보일지. 둘 다 끄면 번역 칸은 보인다
    public bool ShowOriginal { get; set; } = true;
    public bool ShowTranslated { get; set; } = true;

    /// <summary>설정 창 오른쪽에 미리보기를 보일지</summary>
    public bool ShowSettingsPreview { get; set; } = true;

    /// <summary>어느 번역기로 번역했는지 결과 창 오른쪽 아래에 보일지</summary>
    public bool ShowEngine { get; set; } = true;

    /// <summary>결과 창 가로와 원문·번역 칸 세로의 최소 크기(100% 배율 기준 픽셀). 작게 쓰고 싶은 경우를 위해 낮게 둔다</summary>
    public const int MinWidth = 120;
    public const int MinPaneHeight = 20;

    /// <summary>값이 허용 범위를 벗어나면 범위 안으로 맞춘다</summary>
    public void Normalize()
    {
        if (!ShowOriginal && !ShowTranslated) ShowTranslated = true;
        TranslateOrder = Translator.CleanOrder(TranslateOrder);
        GeminiApiKey = (GeminiApiKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(GeminiModel)) GeminiModel = Translator.DefaultGeminiModel;
        AutoCloseSeconds = Math.Clamp(AutoCloseSeconds, 0, 600);
        OriginalFontSize = Math.Clamp(OriginalFontSize, 6, 40);
        TranslatedFontSize = Math.Clamp(TranslatedFontSize, 6, 40);
        Opacity = Math.Clamp(Opacity, 30, 100);
        WindowWidth = Math.Clamp(WindowWidth, MinWidth, 2000);
        OriginalHeight = Math.Clamp(OriginalHeight, MinPaneHeight, 1000);
        TranslatedHeight = Math.Clamp(TranslatedHeight, MinPaneHeight, 1200);
    }

    public void Save()
    {
        Normalize();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppConfig Load(out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded != null) return loaded;
            }
            else
            {
                // 처음 실행이면 기본값 파일을 만들어 둔다
                var created = new AppConfig();
                File.WriteAllText(FilePath, JsonSerializer.Serialize(created, JsonOptions));
                return created;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        return new AppConfig();
    }
}

/// <summary>전역 단축키(RegisterHotKey)를 받는 숨은 창</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public event Action? Pressed;
    private bool _registered;
    private const int EscapeId = 2;
    private bool _escapeRegistered;
    private Action? _onEscape;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    /// <summary>"Ctrl+Alt+T" 같은 문자열을 등록한다. 실패하면 false</summary>
    public bool Register(string hotkey)
    {
        Unregister();
        if (!TryParse(hotkey, out uint mods, out Keys key)) return false;
        _registered = RegisterHotKey(Handle, HotkeyId, mods | MOD_NOREPEAT, (uint)key);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    /// <summary>Esc 를 잠시 전역 단축키로 건다(포커스 없이 떠 있는 선택 화면을 취소할 수 있게)</summary>
    public void RegisterEscape(Action onEscape)
    {
        _onEscape = onEscape;
        _escapeRegistered = RegisterHotKey(Handle, EscapeId, MOD_NOREPEAT, (uint)Keys.Escape);
    }

    public void UnregisterEscape()
    {
        if (_escapeRegistered) UnregisterHotKey(Handle, EscapeId);
        _escapeRegistered = false;
        _onEscape = null;
    }

    private static bool TryParse(string text, out uint mods, out Keys key)
    {
        mods = 0;
        key = Keys.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": case "windows": mods |= MOD_WIN; break;
                default:
                    // 숫자 한 글자는 Keys.D0~D9 로 바꾼다
                    string name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (!Enum.TryParse(name, true, out key)) return false;
                    break;
            }
        }
        return key != Keys.None;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            Pressed?.Invoke();
        else if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == EscapeId)
            _onEscape?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}

/// <summary>트레이 아이콘과 캡처 흐름 전체를 관리한다</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly AppConfig _config;
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkey = new();
    private bool _capturing;
    private bool _settingsOpen;
    private readonly HitchWatcher _hitchWatcher = new();   // 진단 기록이 켜져 있을 때만 동작
    private UpdateResult? _pendingUpdate;  // 시작할 때 찾은 새 버전(알림을 누르면 설치)
    private readonly ToolStripItem _updateItem;
    private ResultForm? _lastResult;  // 가장 최근에 연 결과 창(같은 창 재사용용)
    private readonly ToolStripItem _captureItem;

    public TrayContext()
    {
        _config = AppConfig.Load(out string? configError);
        _config.Normalize();
        DebugLog.Enabled = _config.DebugLog;

        var menu = new ContextMenuStrip();
        _captureItem = menu.Items.Add($"영역 캡처 번역 ({_config.Hotkey})", null, (_, _) => StartCapture());
        menu.Items.Add("설정…", null, (_, _) => OpenSettings());
        _updateItem = menu.Items.Add("업데이트 확인", null, (_, _) => CheckUpdateFromMenu());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = Theme.LoadAppIcon(SystemInformation.SmallIconSize),
            Text = $"SnapTranslate ({_config.Hotkey})",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => StartCapture();

        _hotkey.Pressed += StartCapture;
        if (configError != null)
            _tray.ShowBalloonTip(5000, "SnapTranslate", "config.json 을 읽지 못해 기본값으로 실행합니다: " + configError, ToolTipIcon.Warning);
        else if (!_hotkey.Register(_config.Hotkey))
            _tray.ShowBalloonTip(5000, "SnapTranslate", $"단축키 {_config.Hotkey} 등록 실패. 다른 프로그램이 쓰고 있거나 형식이 잘못됐습니다. config.json 의 Hotkey 를 바꾸세요.", ToolTipIcon.Warning);
        else if (_config.ShowStartupNotice)
            // 시작 안내는 설정에서 켠 경우에만 보인다. 위의 실패 경고는 설정과 관계없이 항상 보인다
            _tray.ShowBalloonTip(2500, "SnapTranslate", $"{_config.Hotkey} 를 누르고 마우스로 영역을 끌어 선택하세요.", ToolTipIcon.Info);

        if (configError != null) _hotkey.Register(_config.Hotkey);

        _tray.BalloonTipClicked += async (_, _) =>
        {
            // 새 버전 알림을 누르면 업데이트할지 묻고 설치한다
            if (_pendingUpdate is not { } update) return;
            _pendingUpdate = null;
            await UpdateService.RunInteractiveAsync(null, update, _ => { });
        };
        if (_config.CheckUpdateOnStart && AppInfo.Repo.Length > 0) CheckUpdateOnStart();
    }

    /// <summary>시작할 때 한 번 새 버전을 확인하고, 있으면 트레이 알림으로 알려 준다(없거나 실패하면 조용히 넘어간다)</summary>
    private async void CheckUpdateOnStart()
    {
        UpdateResult result = await UpdateService.CheckAsync();
        if (!result.Available) return;
        _pendingUpdate = result;
        _updateItem.Text = $"업데이트 ({result.LatestVersion})…";
        _tray.ShowBalloonTip(8000, "SnapTranslate", result.Message + " 이 알림을 누르면 업데이트합니다.", ToolTipIcon.Info);
    }

    /// <summary>트레이 메뉴의 "업데이트 확인": 새 버전이 있으면 설치할지 묻고, 없으면 결과만 알려 준다</summary>
    private async void CheckUpdateFromMenu()
    {
        UpdateResult result = await UpdateService.CheckAsync();
        if (result.Available)
            await UpdateService.RunInteractiveAsync(null, result, _ => { });
        else
            MessageBox.Show($"{result.Message}\n(현재 {AppInfo.VersionText})", AppInfo.Name);
    }

    /// <summary>설정 창을 띄운다. 저장하면 단축키·투명도가 재시작 없이 바로 적용된다</summary>
    private void OpenSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        // 설정 창에서 단축키를 새로 누를 때 기존 단축키가 캡처를 띄우지 않게 잠시 해제한다
        _hotkey.Unregister();
        try
        {
            using var dialog = new SettingsForm(_config, _hotkey.Register);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _captureItem.Text = $"영역 캡처 번역 ({_config.Hotkey})";
                _tray.Text = $"SnapTranslate ({_config.Hotkey})";
                foreach (var form in Application.OpenForms.OfType<ResultForm>().ToList())
                {
                    form.ApplyOpacity();
                    form.ApplyLayout();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("설정 저장 실패: " + ex.Message, "SnapTranslate");
        }
        finally
        {
            // 취소했거나 실패했으면 원래 단축키를 다시 건다
            if (!_hotkey.Register(_config.Hotkey))
                _tray.ShowBalloonTip(5000, "SnapTranslate", $"단축키 {_config.Hotkey} 등록 실패. 설정에서 다른 단축키를 고르세요.", ToolTipIcon.Warning);
            _settingsOpen = false;
        }
    }

    private async void StartCapture()
    {
        if (_capturing || _settingsOpen) return;
        _capturing = true;
        try
        {
            DebugLog.Write($"=== 단축키 (v{AppInfo.VersionText}) 맨 앞 창: {DebugLog.Foreground()}");
            _hitchWatcher.Start();
            var timer = Stopwatch.StartNew();

            // 전체 가상 화면(모든 모니터)을 먼저 찍고, 그 그림 위에서 영역을 고르게 한다
            Rectangle screen = SystemInformation.VirtualScreen;
            using var shot = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(shot))
                g.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
            DebugLog.Write($"화면 찍기 {screen.Width}x{screen.Height} {timer.ElapsedMilliseconds}ms");

            Rectangle selection;
            timer.Restart();
            using (var overlay = new OverlayForm(shot, screen, _config.KeepFocusOnApp))
            {
                DebugLog.Write($"선택 화면 준비 {timer.ElapsedMilliseconds}ms");
                // 포커스를 가져오지 않는 모드에서는 선택 화면이 키를 받지 못하므로, 선택하는 동안만 Esc 를 전역 단축키로 건다
                if (_config.KeepFocusOnApp) _hotkey.RegisterEscape(overlay.Cancel);
                Rectangle? picked;
                try { picked = await overlay.PickAsync(); }
                finally { _hotkey.UnregisterEscape(); }
                DebugLog.Write($"선택 끝 영역={(picked is { } r ? $"{r.Width}x{r.Height}" : "취소")} 맨 앞 창: {DebugLog.Foreground()}");
                if (picked == null) return;
                selection = picked.Value;
            }
            if (selection.Width < 5 || selection.Height < 5) return;
            _hitchWatcher.Start();   // 선택에 걸린 시간은 빼고, 선택이 끝난 시점부터 다시 감시한다

            // 잘라 낸 그림은 결과 창이 가져간다(원문 언어를 바꾸면 다시 인식해야 하므로)
            var crop = shot.Clone(selection, shot.PixelFormat);
            var anchor = new Rectangle(selection.X + screen.X, selection.Y + screen.Y, selection.Width, selection.Height);

            // "같은 창에 계속 표시" 설정이면 열려 있는 결과 창에 새 캡처를 넣는다
            if (_config.ReuseWindow && _lastResult is { IsDisposed: false })
            {
                DebugLog.Write("결과 창 재사용");
                _lastResult.LoadCapture(crop, anchor);
                return;
            }

            timer.Restart();
            var result = new ResultForm(_config, anchor, crop, OpenSettings);
            _lastResult = result;
            result.Show();
            if (!_config.KeepFocusOnApp) result.Activate();
            DebugLog.Write($"결과 창 새로 만들어 표시 {timer.ElapsedMilliseconds}ms 맨 앞 창: {DebugLog.Foreground()}");
            result.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show("캡처 실패: " + ex.Message, "SnapTranslate");
        }
        finally
        {
            _capturing = false;
        }
    }

    protected override void ExitThreadCore()
    {
        _hitchWatcher.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _hotkey.Dispose();
        base.ExitThreadCore();
    }
}

/// <summary>설정 창과 결과 창을 투명하게 띄워 그림으로 저장한다(배치·색 확인용, 설정 파일은 건드리지 않는다)</summary>
internal static class ShotTest
{
    public static int Run(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var config = new AppConfig();

            // 미리보기를 숨긴 설정 창(창이 메뉴·항목 너비로 줄어드는지 확인)
            var narrowConfig = new AppConfig { ShowSettingsPreview = false };
            using (var settings = new SettingsForm(narrowConfig, _ => true) { Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) })
            {
                settings.Show();
                Application.DoEvents();
                Save(settings, Path.Combine(directory, "settings-no-preview.png"));
            }

            using (var settings = new SettingsForm(config, _ => true) { Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) })
            {
                settings.Show();
                Application.DoEvents();
                // 분류(왼쪽 메뉴)마다 한 장씩 저장한다
                for (int page = 0; page < settings.PageCount; page++)
                {
                    settings.SelectPage(page);
                    Application.DoEvents();
                    Save(settings, Path.Combine(directory, $"settings-{page}.png"));
                }
            }

            // 포커스 유지 모드: 결과 창을 띄우고 숨겼다 다시 띄워도 맨 앞 창이 바뀌지 않는지 확인한다
            {
                var lines = new List<string> { "띄우기 전: " + DebugLog.Foreground() };
                var timer = Stopwatch.StartNew();
                using var probe = new ResultForm(new AppConfig { KeepFocusOnApp = true }, new Rectangle(100, 100, 50, 20), null, () => { }, preview: false) { TestOffscreen = true };
                probe.Show();
                Application.DoEvents();
                lines.Add($"처음 띄운 뒤({timer.ElapsedMilliseconds}ms): " + DebugLog.Foreground());
                probe.Hide();
                Application.DoEvents();
                timer.Restart();
                probe.Show();
                Application.DoEvents();
                lines.Add($"숨겼다 다시 띄운 뒤({timer.ElapsedMilliseconds}ms): " + DebugLog.Foreground());
                File.WriteAllLines(Path.Combine(directory, "focus.txt"), lines);
            }

            // 문서(README)에 넣을 화면 그림: 설정 창과 번역 결과 창
            using (var settings = new SettingsForm(config, _ => true) { Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) })
            {
                settings.Show();
                Application.DoEvents();
                Save(settings, Path.Combine(directory, "doc-settings.png"), clientOnly: true);
            }
            using (var sample = new ResultForm(config, new Rectangle(100, 100, 50, 20), null, () => { }, preview: false) { Opacity = 0 })
            {
                sample.Show();
                Application.DoEvents();
                sample.SetTexts(
                    "Autosave is enabled. Your progress will be saved automatically when you reach a checkpoint.",
                    "자동 저장이 켜져 있습니다. 체크포인트에 도달하면 진행 상황이 자동으로 저장됩니다.",
                    "영어(감지) → 한국어");
                Application.DoEvents();
                Save(sample, Path.Combine(directory, "doc-result.png"));
            }

            // 줄을 숨긴 상태도 확인한다
            var compact = new AppConfig { ShowTitleBar = false, ShowLanguageRow = false, ShowBottomBar = false, ShowCaptions = false };
            var translatedOnly = new AppConfig { ShowOriginal = false };
            var originalOnly = new AppConfig { ShowTranslated = false, ShowBottomBar = false };
            // 최소 크기 근처의 아주 작은 창
            var tiny = new AppConfig { WindowWidth = 160, OriginalHeight = 20, TranslatedHeight = 40, ShowTitleBar = false, ShowLanguageRow = false, ShowBottomBar = false, ShowCaptions = false };
            foreach (var (name, cfg) in new[] { ("result.png", config), ("result-compact.png", compact), ("result-translated-only.png", translatedOnly), ("result-original-only.png", originalOnly), ("result-tiny.png", tiny) })
            {
                // 겹쳐 놓은 컨트롤까지 실제와 같게 찍으려고, 투명하게 만들지 않고 화면 밖에 띄워 창 내용을 그대로 받아 온다
                using var result = new ResultForm(cfg, new Rectangle(100, 100, 50, 20), null, () => { }, preview: false) { TestOffscreen = true };
                // 점검 도중 창이 저절로 닫히면 어디서 닫았는지 남긴다
                result.FormClosed += (_, _) => File.WriteAllText(Path.Combine(directory, "closed-" + name + ".txt"), Environment.StackTrace);
                result.Show();
                Application.DoEvents();

                // 글 길이와 창 크기를 여러 번 바꿔 스크롤 막대 전환·크기 변경 경로에서 예외가 없는지 본다
                string longText = string.Join("\r\n", Enumerable.Range(1, 40).Select(i => $"Line {i}: the quick brown fox jumps over the lazy dog."));
                Size normal = result.Size;
                foreach (string text in new[] { longText, "short", longText, "" })
                {
                    result.SetTexts(text, text);
                    Application.DoEvents();
                    foreach (Size size in new[] { new Size(420, 300), new Size(900, 700), new Size(300, 200), normal })
                    {
                        result.Size = size;
                        Application.DoEvents();
                    }
                }
                result.SetTexts(longText, "짧은 번역");
                Application.DoEvents();
                SaveComposed(result, Path.Combine(directory, name));
            }
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "error.txt"), ex.ToString());
            return 1;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    /// <summary>
    /// 창을 화면에 합성된 모습 그대로 저장한다(겹친 컨트롤의 앞뒤 순서가 실제와 같다).
    /// DrawToBitmap 은 겹친 컨트롤을 실제와 다른 순서로 그릴 수 있어, 겹침을 확인할 때는 이쪽을 쓴다.
    /// </summary>
    private static void SaveComposed(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            IntPtr dc = g.GetHdc();
            try { PrintWindow(form.Handle, dc, 2 /*PW_RENDERFULLCONTENT*/); }
            finally { g.ReleaseHdc(dc); }
        }
        bitmap.Save(path, ImageFormat.Png);
    }

    /// <param name="clientOnly">true 면 제목 표시줄·테두리를 빼고 창 안쪽만 저장한다</param>
    private static void Save(Form form, string path, bool clientOnly = false)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
        if (!clientOnly)
        {
            bitmap.Save(path, ImageFormat.Png);
            return;
        }
        // 창 왼쪽 위에서 안쪽 영역까지의 거리만큼 잘라 낸다
        Point origin = form.PointToScreen(Point.Empty);
        var client = new Rectangle(origin.X - form.Left, origin.Y - form.Top, form.ClientSize.Width, form.ClientSize.Height);
        client.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        using Bitmap cropped = bitmap.Clone(client, bitmap.PixelFormat);
        cropped.Save(path, ImageFormat.Png);
    }
}

/// <summary>글자를 그린 그림으로 OCR → 번역 경로를 화면 없이 점검한다</summary>
internal static class SelfTest
{
    /// <summary>글을 크기·색을 바꿔 가며 그려 엔진별 인식 결과와 최종 선택을 파일에 적는다(짧은 글·작은 글자에서 감지가 틀리는 경우 확인)</summary>
    public static int RunOcrProbe(string text, string outputPath)
    {
        var log = new List<string>();
        try
        {
            var styles = new (string Name, string Font, float Size, Color Fore, Color Back, int Pad)[]
            {
                ("큰 글자·흰 바탕", "Segoe UI", 16, Color.Black, Color.White, 20),
                ("보통 글자·흰 바탕·딱 맞게 자름", "Segoe UI", 11, Color.Black, Color.White, 3),
                ("작은 글자·흰 바탕·딱 맞게 자름", "Segoe UI", 8, Color.Black, Color.White, 3),
                ("보통 글자·어두운 바탕", "Segoe UI", 11, Color.White, Color.FromArgb(24, 26, 32), 6),
                ("작은 글자·어두운 바탕", "Arial", 8, Color.FromArgb(220, 220, 220), Color.FromArgb(24, 26, 32), 4),
                ("굵은 글자·색 바탕", "Tahoma", 10, Color.FromArgb(255, 230, 120), Color.FromArgb(60, 40, 30), 5),
            };
            foreach (var (name, fontName, size, fore, back, pad) in styles)
            {
                using var font = new Font(fontName, size);
                Size measured = TextRenderer.MeasureText(text, font);
                using var image = new Bitmap(measured.Width + pad * 2, measured.Height + pad * 2);
                using (var g = Graphics.FromImage(image))
                using (var brush = new SolidBrush(fore))
                {
                    g.Clear(back);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    g.DrawString(text, font, brush, pad, pad);
                }
                OcrOutput chosen = Task.Run(() => OcrService.RecognizeAsync(image, [])).GetAwaiter().GetResult();
                log.Add($"== {name} ({image.Width}x{image.Height}) → 선택 {chosen.Language}: [{chosen.Text}]");
                foreach (string line in Task.Run(() => OcrService.DescribeAsync(image)).GetAwaiter().GetResult())
                    log.Add("   " + line);
            }
            File.WriteAllLines(outputPath, log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("실패: " + ex);
            File.WriteAllLines(outputPath, log);
            return 1;
        }
    }

    /// <summary>새 버전을 확인해 내려받고 파일을 바꾸는 데까지 진행하고(다시 시작은 안 함) 과정을 파일에 적는다</summary>
    public static int RunUpdate(string outputPath)
    {
        var log = new List<string> { $"시작 버전: {AppInfo.VersionText}", $"런타임 포함 배포본: {AppInfo.IsSelfContained}" };
        try
        {
            UpdateResult update = Task.Run(UpdateService.CheckAsync).GetAwaiter().GetResult();
            log.Add($"확인: {update.Message} / 내려받기: {update.DownloadUrl}");
            if (update.Available)
            {
                Task.Run(() => UpdateService.InstallAsync(update, null)).GetAwaiter().GetResult();
                log.Add("설치 완료");
            }
            File.WriteAllLines(outputPath, log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("실패: " + ex);
            File.WriteAllLines(outputPath, log);
            return 1;
        }
    }

    public static int Run(string outputPath)
    {
        try
        {
            using var bmp = new Bitmap(900, 140);
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font("Segoe UI", 16))
            {
                g.Clear(Color.White);
                g.DrawString("The quick brown fox jumps over the lazy dog.", font, Brushes.Black, 20, 20);
                g.DrawString("Press the hotkey and drag to select a region.", font, Brushes.Black, 20, 60);
            }

            var config = new AppConfig();
            // UI 스레드 동기화 컨텍스트와 얽히지 않게 작업 스레드에서 돌린다
            OcrOutput ocr = Task.Run(() => OcrService.RecognizeAsync(bmp, [])).GetAwaiter().GetResult();
            // 번역 요청은 여기 한 번만 보낸다(점검을 여러 번 돌려도 번역 서버 사용량 제한에 걸리지 않게)
            string translatedLine;
            bool translateOk = true;
            try
            {
                var options = TranslateOptions.From(config);
                var t = Task.Run(() => Translator.TranslateAsync(ocr.Text, config.SourceLang, config.TargetLang, config.AltTargetLang, options)).GetAwaiter().GetResult();
                translatedLine = $"번역기: {Translator.EngineName(t.Engine)}\r\n감지 언어: {t.SourceLang}\r\n번역: {t.Text}";
            }
            catch (Exception ex)
            {
                translateOk = false;
                translatedLine = "번역 실패: " + ex.Message;
            }
            UpdateResult update = Task.Run(UpdateService.CheckAsync).GetAwaiter().GetResult();

            // 한글 그림은 한국어 엔진이, 영문 그림은 영어 엔진이 골라지는지(자동 감지) 확인한다
            using var korean = new Bitmap(900, 80);
            using (var g = Graphics.FromImage(korean))
            using (var font = new Font("Malgun Gothic", 16))
            {
                g.Clear(Color.White);
                g.DrawString("자동 저장이 켜져 있습니다. 진행 상황이 저장됩니다.", font, Brushes.Black, 20, 20);
            }
            OcrOutput koreanOcr = Task.Run(() => OcrService.RecognizeAsync(korean, [])).GetAwaiter().GetResult();

            // 다른 문자의 글도 맞는 엔진이 골라지는지 본다(해당 OCR 언어가 설치된 PC 에서만 의미가 있다)
            var samples = new (string Name, string Font, string Text)[]
            {
                ("일본어", "Yu Gothic UI", "設定を保存しました。ゲームを続けますか?"),
                ("중국어(번체)", "Microsoft JhengHei UI", "設定已儲存。您要繼續遊戲嗎?"),
                ("중국어(간체)", "Microsoft YaHei UI", "设置已保存。您要继续游戏吗?"),
                ("러시아어", "Segoe UI", "Настройки сохранены. Продолжить игру?"),
                ("러시아어 한 단어", "Segoe UI", "обманет"),
                ("베트남어", "Segoe UI", "Cài đặt đã được lưu. Bạn có muốn tiếp tục trò chơi không?"),
                ("필리핀어", "Segoe UI", "Nai-save na ang mga setting. Gusto mo bang ituloy ang laro?"),
            };
            var detectLines = new List<string>();
            foreach (var (name, fontName, text) in samples)
            {
                using var image = new Bitmap(1100, 80);
                using (var g = Graphics.FromImage(image))
                using (var font = new Font(fontName, 16))
                {
                    g.Clear(Color.White);
                    g.DrawString(text, font, Brushes.Black, 20, 20);
                }
                OcrOutput found = Task.Run(() => OcrService.RecognizeAsync(image, [])).GetAwaiter().GetResult();
                detectLines.Add($"{name} 그림 → 엔진 {found.Language}: {found.Text}");
            }

            File.WriteAllText(outputPath,
                $"버전: {AppInfo.VersionText}\r\n업데이트 확인: {update.Message} (최신 {update.LatestVersion})\r\n" +
                $"읽을 수 있는 언어: {string.Join(", ", OcrService.InstalledLanguageNames())}\r\n" +
                $"한글 그림 → 엔진 {koreanOcr.Language}: {koreanOcr.Text}\r\n" +
                string.Join("\r\n", detectLines) + "\r\n" +
                $"OCR 언어: {ocr.Language}\r\nOCR 결과: {ocr.Text}\r\n{translatedLine}\r\n");
            return translateOk ? 0 : 2;   // 2 = 번역만 실패(글자 인식 등 나머지는 통과)
        }
        catch (Exception ex)
        {
            File.WriteAllText(outputPath, "실패: " + ex);
            return 1;
        }
    }
}
