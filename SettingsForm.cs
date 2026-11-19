using System.Drawing.Text;

namespace SnapTranslate;

/// <summary>
/// 단축키·언어·창 위치·크기·글자·색·표시 항목 설정 창.
/// 왼쪽에서 값을 바꾸면 오른쪽 미리보기(실제 결과 창과 같은 컨트롤)에 바로 반영된다.
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly LangItem[] Engines =
    [
        new(Translator.Google, "Google 번역"),
        new(Translator.MyMemory, "MyMemory"),
        new(Translator.Gemini, "Gemini (AI, API 키 필요)"),
    ];

    // 2·3순위에서 고르는 "이 순위는 쓰지 않음"
    private static readonly LangItem NoEngine = new("", "사용 안 함");

    private static readonly LangItem[] PositionModes =
    [
        new("Selection", "선택한 영역 근처"),
        new("TopLeft", "화면 왼쪽 위"),
        new("TopRight", "화면 오른쪽 위"),
        new("BottomLeft", "화면 왼쪽 아래"),
        new("BottomRight", "화면 오른쪽 아래"),
        new("Custom", "직접 지정"),
    ];

    private readonly AppConfig _config;   // 저장 대상(실제 설정)
    private readonly AppConfig _draft;    // 미리보기에 쓰는 사본. 저장을 누르기 전까지 실제 설정은 바뀌지 않는다
    private readonly Func<string, bool> _tryRegisterHotkey;
    private bool _loading;                // 컨트롤에 값을 채우는 동안 변경 이벤트를 무시한다

    private readonly TextBox _hotkey;
    private readonly ComboBox _source;
    private readonly ComboBox _target;
    private readonly ComboBox _altTarget;
    private readonly ComboBox _position;
    private readonly Label _positionValue;    // 직접 지정 좌표 표시
    private readonly Button _pickPosition;    // 마우스로 자리 고르기
    private int _positionX;
    private int _positionY;
    private readonly NumericUpDown _autoClose;
    private readonly CheckBox _autoCopy;
    private readonly CheckBox _reuseWindow;
    private readonly CheckBox _closeOnEsc;
    private readonly CheckBox _keepFocus;
    private readonly CheckBox _startupNotice;
    private readonly CheckBox _autostart;     // Windows 시작 시 자동 실행(레지스트리에 등록)
    private readonly CheckBox _saveHistory;   // 번역 기록 저장
    private readonly GlossaryEditor _glossary;   // 용어집 편집 표
    private readonly CheckBox _debugLog;      // 진단 기록 남기기
    private readonly Label _logStatus;        // 진단 기록 파일 상태
    private readonly Label _ocrInstalled;   // "읽을 수 있는 언어" 표시
    private readonly ComboBox[] _engines;    // 번역기 우선순위(1·2·3순위)
    private readonly TextBox _geminiKey;     // Gemini API 키(가려서 표시)
    private readonly CheckBox _checkUpdate;
    private readonly Label _updateStatus;

    private readonly ValueSlider _opacity;
    private readonly ValueSlider _width;
    private readonly ValueSlider _originalHeight;
    private readonly ValueSlider _translatedHeight;

    private readonly ComboBox _fontName;
    private readonly ValueSlider _originalFontSize;
    private readonly ValueSlider _translatedFontSize;
    private readonly Button _originalColor;
    private readonly Button _translatedColor;

    private readonly Button _backgroundColor;
    private readonly Button _cardColor;
    private readonly Button _accentColor;
    private readonly Button _labelColor;
    private readonly Button _fieldColor;
    private readonly Button _badgeColor;          // 번역기 이름 표시 색
    private readonly Button _handleColor;         // 창 이동 손잡이 색
    private readonly ValueSlider _badgeFontSize;  // 번역기 이름 표시 글자 크기
    private readonly ValueSlider _handleSize;     // 창 이동 손잡이 크기
    private readonly Button _selectionColor;      // 영역 고를 때 드래그 선 색
    private readonly ValueSlider _selectionWidth; // 영역 고를 때 드래그 선 두께
    private readonly CheckBox _showHandle;        // 창 이동 손잡이 표시

    private readonly CheckBox _showTitleBar;
    private readonly CheckBox _showLanguageRow;
    private readonly CheckBox _showBottomBar;
    private readonly CheckBox _showCaptions;
    private readonly CheckBox _showOriginal;
    private readonly CheckBox _showTranslated;
    private readonly CheckBox _showEngine;

    private static readonly Color NavColor = Color.FromArgb(23, 24, 28);
    private static readonly Color SeparatorColor = Color.FromArgb(64, 67, 78);

    // 왼쪽 분류 메뉴와 가운데 항목 면
    private readonly Panel _navPanel = new() { Dock = DockStyle.Left, Width = 150, BackColor = NavColor, Padding = new Padding(8, 14, 8, 8) };
    private readonly FlowLayoutPanel _nav = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = NavColor };
    private readonly Panel _content = new() { Dock = DockStyle.Left, Width = 560, Padding = new Padding(10, 10, 10, 0) };
    private readonly List<Panel> _pages = [];
    private readonly List<Button> _navButtons = [];
    private int _selectedPage;

    private readonly Label _message;
    private readonly Panel _previewHost;
    private readonly Panel _previewArea;        // 미리보기 전체(제목 + 그림). 숨길 수 있다
    private readonly Button _previewToggle;     // 미리보기 숨기기/보이기(화살표)
    private readonly Panel _previewToggleHost = new() { Dock = DockStyle.Left, Width = 26, BackColor = Theme.Back };
    private readonly ToolTip _previewTip = new();
    // 미리보기는 오른쪽에 있으므로, 접을 때는 왼쪽(◀)으로 밀고 펼 때는 오른쪽(▶)으로 연다
    private const string HidePreviewArrow = "◀";
    private const string ShowPreviewArrow = "▶";
    private int _widthWithPreview;              // 미리보기를 보일 때의 창 안쪽 너비
    private ResultForm? _preview;

    public SettingsForm(AppConfig config, Func<string, bool> tryRegisterHotkey)
    {
        _config = config;
        _draft = config.Clone();
        _tryRegisterHotkey = tryRegisterHotkey;

        Text = "SnapTranslate 설정";
        Icon = Theme.LoadAppIcon(new Size(32, 32));
        Font = new Font(Theme.FontName, 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        // 크기는 고정한다(늘려도 쓸모가 없다). 미리보기를 숨기거나 보일 때만 프로그램이 너비를 바꾼다
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        ClientSize = new Size(1344, 600);
        MinimumSize = new Size(600, 400);

        // ---- 동작 ----
        _hotkey = new TextBox
        {
            ReadOnly = true,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Width = 220,
            TextAlign = HorizontalAlignment.Center,
            ShortcutsEnabled = false,
        };
        _hotkey.KeyDown += OnHotkeyKeyDown;

        _source = Theme.CreateLangCombo(true, "auto");
        _target = Theme.CreateLangCombo(false, "ko");
        _altTarget = Theme.CreateLangCombo(false, "en");
        _position = CreateCombo();
        _position.Items.AddRange(PositionModes);
        _source.Width = _target.Width = _altTarget.Width = 220;

        _positionValue = new Label { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(3, 8, 10, 0) };
        _pickPosition = new Button { Text = "마우스로 지정…" };
        Theme.StyleButton(_pickPosition, false);
        _pickPosition.Margin = new Padding(0, 3, 0, 3);
        _pickPosition.Click += (_, _) => PickPosition();
        _autoClose = CreateNumber(0, 600, 1);
        _autoCopy = CreateCheck("번역이 끝나면 번역문을 자동 복사");
        _reuseWindow = CreateCheck("같은 창에 계속 표시");
        _closeOnEsc = CreateCheck("Esc 키로 창 닫기");
        _keepFocus = CreateCheck("쓰던 프로그램의 포커스 유지 (게임 권장)");
        _startupNotice = CreateCheck("시작할 때 Windows 알림 표시");
        _autostart = CreateCheck("Windows 시작 시 자동 실행");
        _saveHistory = CreateCheck("번역 기록 저장 (최근 200건)");
        _glossary = new GlossaryEditor { Size = new Size(440, 300), Margin = new Padding(3, 8, 3, 6) };
        _debugLog = CreateCheck("진단 기록 남기기 (문제 신고용)");
        _logStatus = new Label { AutoSize = true, ForeColor = Theme.SubText, Margin = new Padding(3, 4, 3, 4) };

        // ---- 번역기 ----
        _engines = new ComboBox[Engines.Length];
        for (int i = 0; i < _engines.Length; i++)
        {
            _engines[i] = CreateCombo();
            _engines[i].Items.AddRange(Engines);
            if (i > 0) _engines[i].Items.Add(NoEngine);   // 1순위는 반드시 고른다
        }
        _geminiKey = new TextBox
        {
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Width = 300,
            UseSystemPasswordChar = true,   // 화면을 공유하거나 찍을 때 키가 보이지 않게 가린다
        };

        // ---- 크기 ----
        _opacity = new ValueSlider(30, 100, 1, "%", Theme.Card);
        // 크기는 10 단위, 글자 크기는 0.5 단위로 움직인다
        _width = new ValueSlider(AppConfig.MinWidth, 2000, 10, " px", Theme.Card);
        _originalHeight = new ValueSlider(AppConfig.MinPaneHeight, 1000, 10, " px", Theme.Card);
        _translatedHeight = new ValueSlider(AppConfig.MinPaneHeight, 1200, 10, " px", Theme.Card);

        // ---- 글자 ----
        _fontName = CreateCombo();
        using (var fonts = new InstalledFontCollection())
            _fontName.Items.AddRange(fonts.Families.Select(f => (object)f.Name).ToArray());
        _originalFontSize = new ValueSlider(6, 40, 0.5m, " pt", Theme.Card);
        _translatedFontSize = new ValueSlider(6, 40, 0.5m, " pt", Theme.Card);
        _originalColor = CreateColorButton();
        _translatedColor = CreateColorButton();

        // ---- 색 ----
        _backgroundColor = CreateColorButton();
        _cardColor = CreateColorButton();
        _accentColor = CreateColorButton();
        _labelColor = CreateColorButton();
        _fieldColor = CreateColorButton();
        _badgeColor = CreateColorButton();
        _handleColor = CreateColorButton();
        _badgeFontSize = new ValueSlider(6, 20, 0.5m, " pt", Theme.Card);
        _handleSize = new ValueSlider(8, 40, 1, " px", Theme.Card);
        _selectionColor = CreateColorButton();
        _selectionWidth = new ValueSlider(1, 10, 1, " px", Theme.Card);
        _showHandle = CreateCheck("창 이동 손잡이 (왼쪽 위)");
        var reset = new Button { Text = "기본 모양으로" };
        Theme.StyleButton(reset, false);
        reset.Margin = new Padding(3, 6, 3, 6);
        reset.Click += (_, _) => ResetAppearance();

        // ---- 표시 항목 ----
        _showTitleBar = CreateCheck("제목 줄");
        _showLanguageRow = CreateCheck("언어 선택 줄");
        _showBottomBar = CreateCheck("아래 줄(상태·투명도·버튼)");
        _showCaptions = CreateCheck("칸 제목(원문/번역)");
        _showOriginal = CreateCheck("원문 칸");
        _showTranslated = CreateCheck("번역 칸");
        _showEngine = CreateCheck("번역기 이름 (오른쪽 아래)");

        // 왼쪽 메뉴에서 분류를 고르면 가운데에 그 분류의 항목만 보인다
        TableLayoutPanel grid = NewPage("동작", "단축키, 언어, 창이 뜨는 자리, 자동 닫힘");
        AddRow(grid, "단축키", _hotkey);
        AddHint(grid, "칸을 누른 뒤 원하는 키 조합을 누르세요");
        AddSeparator(grid);
        AddRow(grid, "원문 언어", _source);
        AddHint(grid, "번역할 화면의 언어. 보통 \"자동 감지\"로 두세요");
        _ocrInstalled = InfoLabel(string.Join(", ", OcrService.InstalledLanguageNames()));
        _ocrInstalled.MaximumSize = new Size(255, 0);   // 언어가 많으면 잘리지 않게 여러 줄로 넘긴다
        var installOcr = new Button { Text = "언어 추가 설치…" };
        Theme.StyleButton(installOcr, false);
        installOcr.Margin = new Padding(3, 4, 0, 4);
        installOcr.Click += (_, _) => ShowOcrInstallMenu(installOcr);
        AddRow(grid, "읽을 수 있는 언어", _ocrInstalled);
        AddRow(grid, "", installOcr);
        AddHint(grid, "중국어·일본어·러시아어 등은 설치해야 읽습니다");
        AddHint(grid, "필리핀어·베트남어 등은 영어 인식으로 읽습니다");
        AddRow(grid, "번역 언어", _target);
        AddRow(grid, "원문이 번역 언어일 때", _altTarget);
        AddSeparator(grid);
        AddRow(grid, "창 위치", _position);
        AddRow(grid, "직접 지정 자리", Row(_positionValue, _pickPosition));
        AddHint(grid, "마우스로 지정: 화면에서 창이 뜰 자리를 클릭");
        AddHint(grid, "직접 지정이면 결과 창을 끌어 놓은 자리도 저장");
        AddSeparator(grid);
        AddRow(grid, "결과 창", _reuseWindow);
        AddHint(grid, "끄면 캡처할 때마다 새 창이 열립니다");
        AddRow(grid, "", _closeOnEsc);
        AddRow(grid, "", _keepFocus);
        AddHint(grid, "켜면 번역 창이 떠도 게임이 멈추지 않습니다");
        AddHint(grid, "원문 편집·Esc 닫기는 번역 창을 클릭한 뒤 가능");
        AddRow(grid, "자동 닫힘(초)", _autoClose);
        AddHint(grid, "0 = 닫지 않음. 마우스가 창 위에 있으면 안 닫힘");
        AddRow(grid, "", _autoCopy);
        AddRow(grid, "", _saveHistory);
        AddHint(grid, "켜면 원문·번역문이 이 PC 의 history.json 에 남습니다");
        AddHint(grid, "트레이 우클릭 → 번역 기록… 에서 봅니다");
        AddSeparator(grid);
        AddRow(grid, "알림", _startupNotice);
        AddHint(grid, "단축키 등록 실패 같은 경고는 꺼도 표시됩니다");
        AddRow(grid, "시작", _autostart);
        AddHint(grid, "프로그램 폴더를 옮기면 다시 체크해야 합니다");

        grid = NewPage("번역기", "번역에 쓸 서비스와 우선순위");
        for (int i = 0; i < _engines.Length; i++) AddRow(grid, $"{i + 1}순위", _engines[i]);
        AddHint(grid, "1순위부터 쓰고, 실패하면 다음 순위로 넘어갑니다");
        AddHint(grid, "한 번역기만 쓰려면 2·3순위를 \"사용 안 함\"으로 두세요");
        AddHint(grid, "같은 번역기를 두 번 고르면 뒤의 것은 무시됩니다");
        AddSeparator(grid);
        AddHint(grid, "Google 번역: 키 없이 사용. 많이 쓰면 잠시 막힐 수 있음");
        AddHint(grid, "MyMemory: 키 없이 사용. 하루 약 5,000자까지");
        AddHint(grid, "Gemini: AI 번역. 문맥·게임 용어에 강함. 무료 API 키 필요");
        AddHint(grid, "Gemini 응답: 보통 1~3초, 붐비면 최대 30초");
        AddHint(grid, "인식한 글자는 번역한 서비스로 전송됩니다");
        AddSeparator(grid);
        AddRow(grid, "Gemini API 키", _geminiKey);
        AddRow(grid, "", LinkButton("키 발급 페이지 열기", Translator.GeminiKeyUrl, false));
        AddHint(grid, "1. 단추를 눌러 Google 계정으로 로그인");
        AddHint(grid, "2. \"Create API key\" (API 키 만들기)를 누름");
        AddHint(grid, "3. 만들어진 키를 복사해 위 칸에 붙여 넣고 저장");
        AddHint(grid, "무료 등급에는 하루 사용량 한도가 있습니다");
        AddHint(grid, "키는 이 PC 의 config.json 에만 저장됩니다");

        grid = NewPage("용어집", "정해 둔 낱말은 항상 같은 말로 번역");
        AddWide(grid, new Label { Text = "원문과 번역을 적고 추가를 누르세요 (Enter 로도 추가)", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(3, 4, 3, 0) });
        AddWide(grid, _glossary);
        AddWide(grid, new Label { Text = "줄을 누르면 위 칸에 불러와 고칠 수 있습니다. 줄 끝의 ✕ 로 지웁니다.", AutoSize = true, ForeColor = Theme.SubText, Font = new Font(Theme.FontName, 8.5f), Margin = new Padding(3, 0, 3, 6) });
        AddWide(grid, new Label { Text = "게임 고유 명사, 캐릭터 이름처럼 번역이 매번 달라지는 낱말에 씁니다.\r\n대소문자는 구분하지 않습니다. Gemini 에서 가장 정확하게 적용됩니다.", AutoSize = true, ForeColor = Theme.SubText, Font = new Font(Theme.FontName, 8.5f), Margin = new Padding(3, 0, 3, 4) });

        grid = NewPage("크기", "결과 창의 불투명도와 가로·세로 크기");
        AddRow(grid, "불투명도", _opacity);
        AddHint(grid, "불투명도는 미리보기에 반영되지 않습니다(실제 결과 창에만 적용)");
        AddSeparator(grid);
        AddRow(grid, "결과 창 가로", _width);
        AddRow(grid, "원문 칸 세로", _originalHeight);
        AddRow(grid, "번역 칸 세로", _translatedHeight);
        AddHint(grid, "결과 창 가장자리·가운데 막대를 끌어도 바뀝니다");

        grid = NewPage("글자", "원문·번역 글꼴과 글자 크기·색");
        AddRow(grid, "글꼴", _fontName);
        AddSeparator(grid);
        AddRow(grid, "원문 크기", _originalFontSize);
        AddRow(grid, "원문 색", _originalColor);
        AddSeparator(grid);
        AddRow(grid, "번역 크기", _translatedFontSize);
        AddRow(grid, "번역 색", _translatedColor);

        grid = NewPage("색", "결과 창 각 부분의 색");
        AddRow(grid, "창 배경", _backgroundColor);
        AddRow(grid, "원문·번역 칸 배경", _cardColor);
        AddRow(grid, "강조(칸 제목·복사 버튼)", _accentColor);
        AddRow(grid, "보조 글자(제목·상태)", _labelColor);
        AddRow(grid, "언어 목록·버튼 배경", _fieldColor);
        AddSeparator(grid);
        AddRow(grid, "번역기 이름 표시", _badgeColor);
        AddRow(grid, "번역기 이름 글자 크기", _badgeFontSize);
        AddSeparator(grid);
        AddRow(grid, "창 이동 손잡이", _handleColor);
        AddRow(grid, "창 이동 손잡이 크기", _handleSize);
        AddHint(grid, "손잡이는 제목 줄을 숨겼을 때 보입니다");
        AddSeparator(grid);
        AddRow(grid, "영역 고를 때 선", _selectionColor);
        AddRow(grid, "영역 고를 때 선 두께", _selectionWidth);
        AddSeparator(grid);
        AddRow(grid, "크기·글자·색·표시 항목", reset);

        grid = NewPage("표시 항목", "결과 창에서 보일 부분");
        AddRow(grid, "내용", _showOriginal);
        AddRow(grid, "", _showTranslated);
        AddHint(grid, "둘 다 끄면 번역 칸은 보입니다");
        AddSeparator(grid);
        AddRow(grid, "그 밖의 부분", _showTitleBar);
        AddRow(grid, "", _showLanguageRow);
        AddRow(grid, "", _showBottomBar);
        AddRow(grid, "", _showCaptions);
        AddRow(grid, "", _showEngine);
        AddRow(grid, "", _showHandle);
        AddHint(grid, "손잡이는 제목 줄을 숨겼을 때만 보입니다");
        AddHint(grid, "숨긴 뒤에는 결과 창 우클릭 메뉴로 다시 켤 수 있습니다");

        // ---- 정보 ----
        grid = NewPage("정보", "버전, 저작권, 업데이트");
        var logo = new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(3, 4, 14, 4) };
        using (var icon = Theme.LoadAppIcon(new Size(256, 256))) logo.Image = icon.ToBitmap();
        var nameLabel = new Label { Text = AppInfo.Name, AutoSize = true, ForeColor = Theme.Text, Font = new Font(Theme.FontName, 15f, FontStyle.Bold), Margin = new Padding(0, 8, 0, 0) };
        var versionLabel = new Label { Text = "버전 " + AppInfo.VersionText, AutoSize = true, ForeColor = Theme.Accent, Margin = new Padding(2, 2, 0, 0) };
        var nameColumn = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        nameColumn.Controls.Add(nameLabel);
        nameColumn.Controls.Add(versionLabel);
        AddWide(grid, Row(logo, nameColumn));
        AddWide(grid, new Label { Text = AppInfo.Description, AutoSize = true, ForeColor = Theme.SubText, Margin = new Padding(3, 6, 3, 0) });
        AddSeparator(grid);
        AddRow(grid, "버전", InfoLabel(AppInfo.VersionText));
        AddRow(grid, "저작권", InfoLabel(AppInfo.Copyright));
        AddRow(grid, "라이선스", InfoLabel(AppInfo.License));
        AddRow(grid, "글자 인식", InfoLabel("Windows 내장 OCR (PC 안에서 처리)"));
        AddRow(grid, "번역", InfoLabel("Google 번역 · MyMemory · Gemini (글자가 전송됨)"));
        AddRow(grid, "오픈소스", InfoLabel(".NET 9, Windows Forms, C#/WinRT (MIT)"));
        AddSeparator(grid);
        // 주소를 글자로 보이지 않고 단추로 연다
        var links = new List<Control> { LinkButton("제작자 블로그", AppInfo.BlogUrl, true) };
        if (AppInfo.RepoUrl.Length > 0) links.Add(LinkButton("GitHub", AppInfo.RepoUrl, false));
        AddRow(grid, "바로 가기", Row(links.ToArray()));
        AddSeparator(grid);
        _checkUpdate = CreateCheck("시작할 때 새 버전 확인");
        var checkNow = new Button { Text = "지금 업데이트 확인" };
        Theme.StyleButton(checkNow, false);
        checkNow.Margin = new Padding(3, 4, 0, 4);
        _updateStatus = new Label { AutoSize = true, ForeColor = Theme.SubText, Margin = new Padding(3, 4, 3, 4) };
        checkNow.Click += async (_, _) =>
        {
            checkNow.Enabled = false;
            _updateStatus.Text = "확인 중…";
            UpdateResult result = await UpdateService.CheckAsync();
            if (IsDisposed) return;
            _updateStatus.Text = result.Message;
            // 새 버전이면 설치할지 묻고, 내려받아 설치한 뒤 다시 시작한다
            await UpdateService.RunInteractiveAsync(this, result, text => { if (!IsDisposed) _updateStatus.Text = text; });
            if (!IsDisposed) checkNow.Enabled = true;
        };
        AddRow(grid, "업데이트", _checkUpdate);
        AddRow(grid, "", checkNow);
        AddRow(grid, "", _updateStatus);
        if (AppInfo.Repo.Length == 0) _updateStatus.Text = "업데이트 주소가 아직 설정되지 않았습니다.";

        // 문제 신고용 진단 기록. 켜고 끄는 것은 저장을 누르지 않아도 바로 적용된다(켜고 → 문제 재현 → 파일 전달)
        AddSeparator(grid);
        var openLog = new Button { Text = "기록 파일 위치 열기" };
        var copyLog = new Button { Text = "기록 내용 복사" };
        var clearLog = new Button { Text = "기록 지우기" };
        foreach (var button in new[] { openLog, copyLog, clearLog })
        {
            Theme.StyleButton(button, false);
            button.Margin = new Padding(3, 4, 6, 4);
        }
        openLog.Click += (_, _) => OpenLogLocation();
        copyLog.Click += (_, _) => CopyLog();
        clearLog.Click += (_, _) => { DebugLog.Clear(_config); UpdateLogStatus(); };
        _debugLog.CheckedChanged += (_, _) => OnDebugLogToggled();
        AddRow(grid, "문제 신고", _debugLog);
        AddHint(grid, "1. 체크  2. 문제를 다시 일으킴  3. 아래 단추로 파일을 보냄");
        AddHint(grid, "번역한 글 내용·화면 그림·API 키는 기록하지 않습니다");
        AddHint(grid, "꺼 두면 기록하지 않습니다 (프로그램이 죽는 오류만 남김)");
        AddRow(grid, "", Row(openLog, copyLog));
        AddRow(grid, "", clearLog);
        AddRow(grid, "", _logStatus);

        _navPanel.Controls.Add(_nav);
        SelectPage(0);

        // ---- 오른쪽: 미리보기 ----
        var previewTitle = new Label
        {
            Text = "미리보기  (불투명도·창 위치·자동 닫힘은 실제 창에만 적용)",
            Dock = DockStyle.Top,
            Height = 30,
            ForeColor = Theme.SubText,
            Font = new Font(Theme.FontName, 9f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
        };
        _previewHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(16, 16, 18) };
        Theme.DarkControl(_previewHost);
        _previewArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0) };
        _previewArea.Controls.Add(_previewHost);
        _previewArea.Controls.Add(previewTitle);

        // ---- 아래: 안내 문구와 버튼 ----
        _message = new Label { AutoSize = true, ForeColor = Theme.Danger, Margin = new Padding(3, 8, 12, 0) };
        var save = new Button { Text = "저장" };
        var cancel = new Button { Text = "취소" };
        Theme.StyleButton(save, true);
        Theme.StyleButton(cancel, false);
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(12, 8, 12, 12) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        buttons.Controls.Add(_message);
        // 미리보기 접기/펴기. 항목 영역과 미리보기 사이에 세로로 긴 화살표 단추를 둔다(서랍 손잡이처럼)
        _previewToggle = new Button
        {
            Text = HidePreviewArrow,
            Size = new Size(18, 64),
            FlatStyle = FlatStyle.Flat,
            // 주변(창 배경)·왼쪽 메뉴의 회색과 겹치지 않게 가라앉힌 파랑으로 칠한다(누를 수 있는 것임이 보이게)
            BackColor = Color.FromArgb(48, 74, 128),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Symbol", 9f),
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        _previewToggle.FlatAppearance.BorderSize = 0;
        _previewToggle.FlatAppearance.MouseOverBackColor = Theme.Accent;
        _previewToggle.FlatAppearance.MouseDownBackColor = Theme.AccentHover;
        // 단추는 짧게 두고, 세로 가운데에 놓는다(놓일 자리만 세로로 길다)
        _previewToggleHost.Controls.Add(_previewToggle);
        _previewToggleHost.Resize += (_, _) => _previewToggle.Location = new Point(
            (_previewToggleHost.Width - _previewToggle.Width) / 2, (_previewToggleHost.Height - _previewToggle.Height) / 2);
        _previewToggle.Click += (_, _) => TogglePreview();
        _previewTip.SetToolTip(_previewToggle, "미리보기 숨기기");
        var resetAll = new Button { Text = "설정 초기화" };
        Theme.StyleButton(resetAll, false);
        resetAll.Click += (_, _) => ResetAll();
        buttons.Controls.Add(resetAll);

        // Dock 은 나중에 넣은 컨트롤부터 자리를 잡는다: 아래 버튼 → 왼쪽 메뉴 → 항목 → 나머지를 미리보기가 채운다
        Controls.Add(_previewArea);
        Controls.Add(_previewToggleHost);
        Controls.Add(_content);
        Controls.Add(_navPanel);
        Controls.Add(buttons);
        CancelButton = cancel;

        LoadControls(_draft);
        WatchChanges();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // 설정 창의 배율 조정이 끝난 뒤에 넣어야 미리보기가 두 번 확대되지 않는다
        _preview = new ResultForm(_draft, Rectangle.Empty, null, () => { }, preview: true) { Location = new Point(12, 12) };
        _previewHost.Controls.Add(_preview);
        _preview.Show();
        // 미리보기 크기나 설정 창 크기가 바뀔 때마다 가운데로 다시 놓는다
        _preview.SizeChanged += (_, _) => CenterPreview();
        _previewHost.SizeChanged += (_, _) => CenterPreview();
        CenterPreview();
        _widthWithPreview = ClientSize.Width;
        if (!_config.ShowSettingsPreview) SetPreviewVisible(false);
    }

    /// <summary>진단 기록을 켜거나 끈다. 저장 단추와 관계없이 바로 적용하고 설정 파일에도 적는다</summary>
    private void OnDebugLogToggled()
    {
        if (_loading) return;
        bool on = _debugLog.Checked;
        _config.DebugLog = _draft.DebugLog = on;
        DebugLog.SetEnabled(on, _config);
        try { _config.Save(); }
        catch (Exception ex) { _message.Text = "config.json 저장 실패: " + ex.Message; }
        UpdateLogStatus();
    }

    private void UpdateLogStatus()
    {
        string state = DebugLog.Enabled ? "기록 중" : "꺼짐";
        string file = DebugLog.Exists
            ? $"{DebugLog.FileName} ({new FileInfo(DebugLog.FilePath).Length / 1024.0:0.#} KB)"
            : "기록 파일 없음";
        _logStatus.Text = $"{state} · {file}";
    }

    /// <summary>탐색기를 열어 기록 파일을 고른 채로 보여 준다(그대로 끌어다 보내면 된다)</summary>
    private void OpenLogLocation()
    {
        try
        {
            string arguments = DebugLog.Exists ? $"/select,\"{DebugLog.FilePath}\"" : $"\"{AppContext.BaseDirectory}\"";
            System.Diagnostics.Process.Start("explorer.exe", arguments);
            if (!DebugLog.Exists) _logStatus.Text = "아직 기록 파일이 없습니다. 체크한 뒤 문제를 다시 일으켜 보세요.";
        }
        catch (Exception ex)
        {
            _logStatus.Text = "폴더를 열지 못했습니다: " + ex.Message;
        }
    }

    /// <summary>기록 내용을 클립보드에 복사한다(댓글·메일에 바로 붙여 넣을 수 있게)</summary>
    private void CopyLog()
    {
        string text = DebugLog.ReadAll();
        if (text.Length == 0)
        {
            _logStatus.Text = "복사할 기록이 없습니다.";
            return;
        }
        try
        {
            Clipboard.SetText(text);
            _logStatus.Text = $"기록을 복사했습니다 ({text.Length:N0}자). 붙여 넣어 보내 주세요.";
        }
        catch (Exception ex)
        {
            _logStatus.Text = "복사 실패: " + ex.Message;
        }
    }

    /// <summary>미리보기를 숨기거나 보이고, 그 선택을 바로 저장한다(다음에 설정 창을 열 때도 유지)</summary>
    private void TogglePreview()
    {
        bool show = !_previewArea.Visible;
        SetPreviewVisible(show);
        _config.ShowSettingsPreview = _draft.ShowSettingsPreview = show;
        try { _config.Save(); }
        catch (Exception ex) { _message.Text = "config.json 저장 실패: " + ex.Message; }
    }

    /// <summary>미리보기를 숨기면 창을 메뉴·항목 너비로 줄이고, 보이면 원래 너비로 되돌린다</summary>
    private void SetPreviewVisible(bool show)
    {
        if (_previewArea.Visible == show) return;
        if (!show) _widthWithPreview = ClientSize.Width;
        _previewArea.Visible = show;
        _previewToggle.Text = show ? HidePreviewArrow : ShowPreviewArrow;
        _previewTip.SetToolTip(_previewToggle, show ? "미리보기 숨기기" : "미리보기 보이기");
        int narrow = _navPanel.Width + _content.Width + _previewToggleHost.Width;
        ClientSize = new Size(show ? Math.Max(_widthWithPreview, narrow + 300) : narrow, ClientSize.Height);
    }

    /// <summary>미리보기를 미리보기 영역 한가운데에 놓는다. 영역보다 크면 왼쪽 위에 붙이고 스크롤한다</summary>
    private void CenterPreview()
    {
        if (_preview == null) return;
        const int margin = 12;
        Size area = _previewHost.ClientSize;
        Point scroll = _previewHost.AutoScrollPosition;   // 스크롤된 만큼 음수
        int x = Math.Max(margin, (area.Width - _preview.Width) / 2);
        int y = Math.Max(margin, (area.Height - _preview.Height) / 2);
        var target = new Point(x + scroll.X, y + scroll.Y);
        if (_preview.Location != target) _preview.Location = target;
    }

    // ---- 값 채우기 / 읽기 ----

    /// <summary>설정 값을 컨트롤에 채운다</summary>
    private void LoadControls(AppConfig c)
    {
        _loading = true;
        try
        {
            _hotkey.Text = c.Hotkey;
            Theme.SelectLang(_source, c.SourceLang);
            Theme.SelectLang(_target, c.TargetLang);
            Theme.SelectLang(_altTarget, c.AltTargetLang);
            _position.SelectedItem = PositionModes.FirstOrDefault(p => p.Code.Equals(c.PositionMode, StringComparison.OrdinalIgnoreCase)) ?? PositionModes[0];
            _positionX = c.PositionX;
            _positionY = c.PositionY;
            SetNumber(_autoClose, c.AutoCloseSeconds);
            _autoCopy.Checked = c.CopyTranslation;
            _reuseWindow.Checked = c.ReuseWindow;
            _closeOnEsc.Checked = c.CloseOnEsc;
            _keepFocus.Checked = c.KeepFocusOnApp;
            _checkUpdate.Checked = c.CheckUpdateOnStart;
            _startupNotice.Checked = c.ShowStartupNotice;
            _autostart.Checked = Autostart.IsEnabled();   // 설정 파일이 아니라 실제 등록 상태를 보인다
            _saveHistory.Checked = c.SaveHistory;
            _glossary.SetLines(c.Glossary);
            _debugLog.Checked = c.DebugLog;
            UpdateLogStatus();
            var order = Translator.NormalizeSlots(c.TranslateOrder);
            for (int i = 0; i < _engines.Length; i++)
                _engines[i].SelectedItem = Engines.FirstOrDefault(e => e.Code == order[i]) ?? NoEngine;
            _geminiKey.Text = c.GeminiApiKey;

            _opacity.Value = c.Opacity;
            _width.Value = c.WindowWidth;
            _originalHeight.Value = c.OriginalHeight;
            _translatedHeight.Value = c.TranslatedHeight;

            if (!_fontName.Items.Contains(c.FontName)) _fontName.Items.Add(c.FontName);
            _fontName.SelectedItem = c.FontName;
            _originalFontSize.Value = (decimal)c.OriginalFontSize;
            _translatedFontSize.Value = (decimal)c.TranslatedFontSize;
            _originalColor.BackColor = Theme.ParseColor(c.OriginalColor, Theme.SubText);
            _translatedColor.BackColor = Theme.ParseColor(c.TranslatedColor, Theme.Text);

            _backgroundColor.BackColor = Theme.ParseColor(c.BackgroundColor, Theme.Back);
            _cardColor.BackColor = Theme.ParseColor(c.CardColor, Theme.Card);
            _accentColor.BackColor = Theme.ParseColor(c.AccentColor, Theme.Accent);
            _labelColor.BackColor = Theme.ParseColor(c.LabelColor, Theme.SubText);
            _fieldColor.BackColor = Theme.ParseColor(c.FieldColor, Theme.Field);
            _badgeColor.BackColor = Theme.ParseColor(c.EngineBadgeColor, Theme.Accent);
            _handleColor.BackColor = Theme.ParseColor(c.DragHandleColor, Color.FromArgb(255, 204, 64));
            _badgeFontSize.Value = (decimal)c.EngineBadgeFontSize;
            _handleSize.Value = c.DragHandleSize;
            _selectionColor.BackColor = Theme.ParseColor(c.SelectionColor, Color.FromArgb(0, 174, 255));
            _selectionWidth.Value = c.SelectionLineWidth;

            _showTitleBar.Checked = c.ShowTitleBar;
            _showLanguageRow.Checked = c.ShowLanguageRow;
            _showBottomBar.Checked = c.ShowBottomBar;
            _showCaptions.Checked = c.ShowCaptions;
            _showOriginal.Checked = c.ShowOriginal;
            _showTranslated.Checked = c.ShowTranslated;
            _showEngine.Checked = c.ShowEngine;
            _showHandle.Checked = c.ShowDragHandle;
            UpdatePositionInputs();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>컨트롤 값을 설정 객체에 옮긴다(단축키 제외 — 단축키는 저장할 때 등록 확인 뒤 넣는다)</summary>
    private void ReadControls(AppConfig c)
    {
        c.SourceLang = Theme.SelectedCode(_source);
        c.TargetLang = Theme.SelectedCode(_target);
        c.AltTargetLang = Theme.SelectedCode(_altTarget);
        c.PositionMode = (_position.SelectedItem as LangItem)?.Code ?? "Selection";
        c.PositionX = _positionX;
        c.PositionY = _positionY;
        c.AutoCloseSeconds = (int)_autoClose.Value;
        c.CopyTranslation = _autoCopy.Checked;
        c.ReuseWindow = _reuseWindow.Checked;
        c.CloseOnEsc = _closeOnEsc.Checked;
        c.KeepFocusOnApp = _keepFocus.Checked;
        c.CheckUpdateOnStart = _checkUpdate.Checked;
        c.ShowStartupNotice = _startupNotice.Checked;
        c.SaveHistory = _saveHistory.Checked;
        c.Glossary = _glossary.GetLines();
        c.DebugLog = _debugLog.Checked;
        c.TranslateOrder = Translator.NormalizeSlots(_engines.Select(combo => (combo.SelectedItem as LangItem)?.Code ?? ""));
        c.GeminiApiKey = _geminiKey.Text.Trim();

        c.Opacity = (int)_opacity.Value;
        c.WindowWidth = (int)_width.Value;
        c.OriginalHeight = (int)_originalHeight.Value;
        c.TranslatedHeight = (int)_translatedHeight.Value;

        c.FontName = _fontName.SelectedItem as string ?? Theme.FontName;
        c.OriginalFontSize = (double)_originalFontSize.Value;
        c.TranslatedFontSize = (double)_translatedFontSize.Value;
        c.OriginalColor = Theme.ToHex(_originalColor.BackColor);
        c.TranslatedColor = Theme.ToHex(_translatedColor.BackColor);

        c.BackgroundColor = Theme.ToHex(_backgroundColor.BackColor);
        c.CardColor = Theme.ToHex(_cardColor.BackColor);
        c.AccentColor = Theme.ToHex(_accentColor.BackColor);
        c.LabelColor = Theme.ToHex(_labelColor.BackColor);
        c.FieldColor = Theme.ToHex(_fieldColor.BackColor);
        c.EngineBadgeColor = Theme.ToHex(_badgeColor.BackColor);
        c.DragHandleColor = Theme.ToHex(_handleColor.BackColor);
        c.EngineBadgeFontSize = (double)_badgeFontSize.Value;
        c.DragHandleSize = (int)_handleSize.Value;
        c.SelectionColor = Theme.ToHex(_selectionColor.BackColor);
        c.SelectionLineWidth = (int)_selectionWidth.Value;

        c.ShowTitleBar = _showTitleBar.Checked;
        c.ShowLanguageRow = _showLanguageRow.Checked;
        c.ShowBottomBar = _showBottomBar.Checked;
        c.ShowCaptions = _showCaptions.Checked;
        c.ShowOriginal = _showOriginal.Checked;
        c.ShowTranslated = _showTranslated.Checked;
        c.ShowEngine = _showEngine.Checked;
        c.ShowDragHandle = _showHandle.Checked;
    }

    /// <summary>값이 바뀔 때마다 미리보기를 다시 그리도록 모든 입력에 연결한다</summary>
    private void WatchChanges()
    {
        foreach (var combo in new[] { _source, _target, _altTarget, _position, _fontName })
            combo.SelectedIndexChanged += (_, _) => OnChanged();
        _autoClose.ValueChanged += (_, _) => OnChanged();
        foreach (var slider in new[] { _opacity, _width, _originalHeight, _translatedHeight, _originalFontSize, _translatedFontSize, _badgeFontSize, _handleSize })
            slider.ValueChanged += (_, _) => OnChanged();
        foreach (var check in new[] { _autoCopy, _reuseWindow, _closeOnEsc, _showTitleBar, _showLanguageRow, _showBottomBar, _showCaptions, _showOriginal, _showTranslated, _showEngine, _showHandle })
            check.CheckedChanged += (_, _) => OnChanged();
        foreach (var color in new[] { _originalColor, _translatedColor, _backgroundColor, _cardColor, _accentColor, _labelColor, _fieldColor, _badgeColor, _handleColor })
            color.BackColorChanged += (_, _) => OnChanged();
    }

    /// <summary>아직 설치되지 않은 OCR 언어를 메뉴로 보여 주고, 고르면 설치를 시작한다</summary>
    private void ShowOcrInstallMenu(Control anchor)
    {
        var menu = new ContextMenuStrip();
        foreach (var (check, capability, name) in OcrInstaller.Languages)
        {
            if (OcrService.IsAvailable(check)) continue;
            menu.Items.Add(name, null, async (_, _) => await InstallOcrAsync(capability, name));
        }
        if (menu.Items.Count == 0)
            menu.Items.Add("설치할 수 있는 언어가 모두 설치돼 있습니다").Enabled = false;
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    /// <summary>관리자 권한 창에서 설치를 실행하고, 그 창이 닫히면 "읽을 수 있는 언어"를 다시 읽어 보여 준다</summary>
    private async Task InstallOcrAsync(string capability, string name)
    {
        var answer = MessageBox.Show(this,
            $"{name} 글자 인식 기능을 Windows 에 설치할까요?\n\n관리자 권한 확인 창이 뜨고, 설치 창(PowerShell)이 열립니다.",
            AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        using var process = OcrInstaller.Start(capability, name);
        if (process == null)
        {
            _message.Text = "설치를 취소했습니다(관리자 권한 필요).";
            return;
        }
        _message.Text = $"{name} 설치 창이 닫힐 때까지 기다리는 중…";
        await process.WaitForExitAsync();
        if (IsDisposed) return;
        _ocrInstalled.Text = string.Join(", ", OcrService.InstalledLanguageNames());
        _message.Text = "설치 창이 닫혔습니다. 목록에 안 보이면 프로그램을 다시 시작하세요.";
    }

    /// <summary>화면에서 결과 창이 뜰 자리를 마우스로 고른다. 고르면 창 위치가 "직접 지정"으로 바뀐다</summary>
    private void PickPosition()
    {
        // 미리보기는 실제 결과 창과 크기가 같으므로 그 크기의 사각형을 보여 준다
        Size size = _preview?.Size ?? new Size(560, 400);
        double opacity = Opacity;
        Opacity = 0;   // 고르는 동안 설정 창이 화면을 가리지 않게 한다(숨기면 대화 상자가 닫히므로 투명하게만)
        try
        {
            using var picker = new PositionPicker(size);
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            _positionX = picker.Picked.X;
            _positionY = picker.Picked.Y;
        }
        finally
        {
            Opacity = opacity;
        }
        _position.SelectedItem = PositionModes.First(p => p.Code == "Custom");
        OnChanged();
    }

    private void OnChanged()
    {
        if (_loading) return;
        UpdatePositionInputs();
        ReadControls(_draft);
        _preview?.ApplyLayout();
    }

    /// <summary>크기·글자·색·표시 항목을 처음 값으로 되돌린다(저장을 눌러야 실제로 적용된다)</summary>
    /// <summary>
    /// 모든 설정을 처음 설치했을 때의 값으로 되돌린다(저장을 눌러야 실제로 적용된다).
    /// 사용자가 직접 넣은 자료(Gemini API 키, 용어집)는 설정이 아니므로 그대로 둔다.
    /// </summary>
    private void ResetAll()
    {
        if (MessageBox.Show(this,
                "모든 설정을 처음 값으로 되돌릴까요?\n\n" +
                "· 단축키, 언어, 번역기 순위, 창 위치·크기·글자·색, 표시 항목이 기본값이 됩니다.\n" +
                "· Gemini API 키와 용어집은 그대로 둡니다.\n" +
                "· 저장을 눌러야 적용됩니다. 취소를 누르면 되돌리지 않습니다.",
                AppInfo.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        ReadControls(_draft);   // 지금 화면에 있는 키·용어집을 먼저 읽어 둔다
        var defaults = new AppConfig
        {
            GeminiApiKey = _draft.GeminiApiKey,
            Glossary = _draft.Glossary,
            ShowSettingsPreview = _draft.ShowSettingsPreview,
        };
        LoadControls(defaults);
        // 자동 실행은 설정 파일이 아니라 Windows 등록 상태를 보이므로 따로 끈다(기본값은 끔)
        _loading = true;
        _autostart.Checked = false;
        _loading = false;

        ReadControls(_draft);
        _preview?.ApplyLayout();
        _message.Text = "기본값으로 되돌렸습니다. 저장을 눌러야 적용됩니다.";
    }

    private void ResetAppearance()
    {
        _draft.CopyAppearanceFrom(new AppConfig());
        string hotkey = _hotkey.Text;
        LoadControls(_draft);
        _hotkey.Text = hotkey;   // 고르던 단축키는 그대로 둔다
        _preview?.ApplyLayout();
    }

    /// <summary>직접 지정 좌표를 표시한다. "직접 지정"이 아닐 때는 흐리게 보인다</summary>
    private void UpdatePositionInputs()
    {
        bool custom = (_position.SelectedItem as LangItem)?.Code == "Custom";
        _positionValue.Text = $"X {_positionX}, Y {_positionY}";
        _positionValue.ForeColor = custom ? Theme.Text : Theme.SubText;
    }

    // ---- 배치 도우미 ----

    private static void AddRow(TableLayoutPanel grid, string caption, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.SubText, Margin = new Padding(3, 7, 14, 7) }, 0, row);
        control.Anchor = AnchorStyles.Left;
        grid.Controls.Add(control, 1, row);
    }

    private static void AddHint(TableLayoutPanel grid, string text) =>
        AddRow(grid, "", new Label { Text = text, AutoSize = true, ForeColor = Theme.SubText, Font = new Font(Theme.FontName, 8.5f), Margin = new Padding(3, 0, 3, 4) });

    /// <summary>정보 화면의 값 글자</summary>
    private static Label InfoLabel(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(3, 7, 3, 7) };

    /// <summary>누르면 기본 브라우저로 주소를 여는 단추</summary>
    private static Button LinkButton(string text, string url, bool primary)
    {
        var button = new Button { Text = text };
        Theme.StyleButton(button, primary);
        button.Margin = new Padding(3, 4, 8, 4);
        button.Click += (_, _) => AppInfo.OpenUrl(url);
        return button;
    }

    /// <summary>두 칸을 다 쓰는 줄을 넣는다</summary>
    private static void AddWide(TableLayoutPanel grid, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(control, 0, row);
        grid.SetColumnSpan(control, 2);
    }

    /// <summary>항목 묶음 사이의 가는 구분선</summary>
    private static void AddSeparator(TableLayoutPanel grid) =>
        AddWide(grid, new Label { AutoSize = false, Height = 1, BackColor = SeparatorColor, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3, 10, 3, 10) });

    /// <summary>분류 하나(왼쪽 메뉴 단추 + 가운데 항목 면)를 만들고, 항목을 넣을 표를 돌려준다</summary>
    private TableLayoutPanel NewPage(string title, string description)
    {
        // 아래·오른쪽 여백은 표 안쪽에 둔다(스크롤되는 판의 여백은 끝까지 내렸을 때 적용되지 않는다)
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Padding = new Padding(0, 0, 14, 28) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddWide(grid, new Label { Text = title, AutoSize = true, ForeColor = Theme.Text, Font = new Font(Theme.FontName, 13f, FontStyle.Bold), Margin = new Padding(3, 0, 3, 2) });
        AddWide(grid, new Label { Text = description, AutoSize = true, ForeColor = Theme.SubText, Font = new Font(Theme.FontName, 9f), Margin = new Padding(3, 0, 3, 0) });
        AddSeparator(grid);

        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Card, Padding = new Padding(20, 16, 10, 16), Visible = false };
        page.Controls.Add(grid);
        Theme.DarkControl(page);
        _content.Controls.Add(page);

        int index = _pages.Count;
        _pages.Add(page);

        var button = new Button
        {
            Text = title,
            Width = 134,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Margin = new Padding(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Theme.Card;
        button.FlatAppearance.MouseDownBackColor = Theme.Card;
        button.Click += (_, _) => SelectPage(index);
        // 고른 분류는 왼쪽에 강조 색 막대를 그려 표시한다
        button.Paint += (_, e) =>
        {
            if (index != _selectedPage) return;
            using var bar = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(bar, 0, 8, 3, button.Height - 16);
        };
        _navButtons.Add(button);
        _nav.Controls.Add(button);
        return grid;
    }

    /// <summary>왼쪽 메뉴에서 고른 분류의 항목 면만 보인다</summary>
    public void SelectPage(int index)
    {
        _selectedPage = index;
        for (int i = 0; i < _pages.Count; i++)
        {
            bool selected = i == index;
            _pages[i].Visible = selected;
            _navButtons[i].BackColor = selected ? Theme.Card : NavColor;
            _navButtons[i].ForeColor = selected ? Theme.Text : Theme.SubText;
            _navButtons[i].Font = new Font(Theme.FontName, 10f, selected ? FontStyle.Bold : FontStyle.Regular);
            _navButtons[i].Invalidate();
        }
    }

    public int PageCount => _pages.Count;

    /// <summary>지금 보이는 페이지를 맨 아래까지 내린다(모양 점검용)</summary>
    public void ScrollPageToEnd() => _pages[_selectedPage].AutoScrollPosition = new Point(0, 100000);

    /// <summary>한 줄에 컨트롤 여러 개를 나란히 놓는다</summary>
    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(controls);
        return row;
    }

    private static ComboBox CreateCombo() => new DarkCombo
    {
        BackColor = Theme.Field,
        ForeColor = Theme.Text,
        Width = 220,
    };

    private static NumericUpDown CreateNumber(decimal minimum, decimal maximum, decimal increment = 10, int decimals = 0) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        DecimalPlaces = decimals,
        Width = 100,
        BackColor = Theme.Field,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        TextAlign = HorizontalAlignment.Right,
    };

    private static void SetNumber(NumericUpDown number, decimal value) =>
        number.Value = Math.Clamp(value, number.Minimum, number.Maximum);

    private static CheckBox CreateCheck(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(3, 4, 3, 4) };

    /// <summary>누르면 색 선택 창이 뜨고, 고른 색을 버튼 바탕색으로 보여 주는 버튼</summary>
    private static Button CreateColorButton()
    {
        var button = new Button { Width = 70, Height = 27, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(3, 3, 0, 3) };
        button.FlatAppearance.BorderColor = Theme.SubText;
        button.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = button.BackColor, FullOpen = true };
            if (dialog.ShowDialog(button.FindForm()) == DialogResult.OK) button.BackColor = dialog.Color;
        };
        return button;
    }

    // ---- 단축키 입력 / 저장 ----

    /// <summary>누른 키 조합을 "Ctrl+Alt+T" 형식 문자열로 바꿔 칸에 넣는다</summary>
    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        Keys key = e.KeyCode;
        // 보조키만 누른 상태는 아직 조합이 끝나지 않은 것
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None) return;

        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Alt) parts.Add("Alt");
        if (e.Shift) parts.Add("Shift");
        if (Theme.IsWinKeyDown()) parts.Add("Win");

        bool functionKey = key is >= Keys.F1 and <= Keys.F24;
        if (parts.Count == 0 && !functionKey)
        {
            _message.Text = "Ctrl·Alt·Shift·Win 중 하나 이상과 함께 누르세요.";
            return;
        }

        string name = key is >= Keys.D0 and <= Keys.D9 ? ((int)(key - Keys.D0)).ToString() : key.ToString();
        parts.Add(name);
        _hotkey.Text = string.Join("+", parts);
        _message.Text = "";
    }

    private void Save()
    {
        string hotkey = _hotkey.Text.Trim();
        // 실제로 등록되는지 먼저 확인한다(다른 프로그램이 쓰는 조합이면 실패)
        if (!_tryRegisterHotkey(hotkey))
        {
            _message.Text = $"{hotkey} 는 등록할 수 없습니다. 다른 조합을 고르세요.";
            return;
        }

        // 자동 실행은 실제 등록 상태와 다를 때만 바꾼다
        if (_autostart.Checked != Autostart.IsEnabled())
        {
            string? failed = Autostart.Set(_autostart.Checked);
            if (failed != null)
            {
                _message.Text = "자동 실행 설정 실패: " + failed;
                return;
            }
        }

        _config.Hotkey = hotkey;
        ReadControls(_config);
        try
        {
            _config.Save();
        }
        catch (Exception ex)
        {
            _message.Text = "config.json 저장 실패: " + ex.Message;
            return;
        }
        DebugLog.SetEnabled(_config.DebugLog, _config);
        DialogResult = DialogResult.OK;
    }
}
