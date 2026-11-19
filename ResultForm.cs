namespace SnapTranslate;

/// <summary>
/// 인식한 원문과 번역문을 보여 주는 창. 언어·투명도·크기를 이 창에서 바로 바꿀 수 있다.
/// preview 로 만들면 설정 창 안에 넣는 미리보기가 되어, 모양만 보여 주고 동작은 하지 않는다.
/// </summary>
internal sealed class ResultForm : Form
{
    private const int WM_NCHITTEST = 0x84;
    private const int ResizeGrip = 6;

    private readonly AppConfig _config;
    private Bitmap? _crop;            // 원문 언어를 바꾸면 다시 인식하려고 들고 있는다(미리보기는 없음)
    private readonly bool _preview;
    private Rectangle _anchor;
    private readonly bool _keepFocus;      // 창이 포커스를 가져가지 않는 모드(창을 만들 때 정해지고 바뀌지 않는다)
    private readonly Button _floatClose;  // 제목 줄을 숨겼을 때 내용 위에 겹쳐 보이는 닫기 단추
    private readonly BusyDots _busyDots;  // "…중" 안내 뒤에서 움직이는 색 점
    private readonly Label _engineLabel;  // 어느 번역기로 번역했는지. 내용 오른쪽 아래에 겹쳐 보인다
    private string _engineName = "";      // 마지막 번역에 쓴 번역기 이름(번역 전·실패면 빈 값)

    /// <summary>모양 점검용: 창을 화면 밖 좌표에 띄운다</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool TestOffscreen { get; set; }
    private readonly Font _titleFont = new(Theme.FontName, 9f, FontStyle.Bold);
    private ToolStripItem? _closeMenuItem;   // 우클릭 메뉴의 닫기 항목(Esc 설정에 따라 문구가 바뀐다)

    private readonly TableLayoutPanel _root;
    private readonly Panel _titleBar;
    private readonly Label _title;
    private readonly Button _close;
    private readonly Button _settings;
    private readonly FlowLayoutPanel _langRow;
    private readonly ComboBox _source;
    private readonly ComboBox _target;
    private readonly Label _arrow;
    private readonly SplitContainer _split;   // 위: 원문, 아래: 번역. 사이 막대를 끌어 높이를 바꾼다
    private readonly Panel _originalCard;
    private readonly Panel _translatedCard;
    private readonly Label _originalCaption;
    private readonly Label _translatedCaption;
    private readonly TextBox _original;
    private readonly TextBox _translated;
    private readonly TableLayoutPanel _bottom;
    private readonly Label _status;
    private readonly Label _opacityLabel;
    private readonly SlimSlider _opacity;
    private readonly Button _retranslate;
    private readonly Button _copy;

    private float[] _rowHeights = [32, 40, 44];   // 제목 줄·언어 줄·아래 줄의 원래 높이(배율 적용 뒤 값으로 바뀐다)
    private readonly System.Windows.Forms.Timer _closeTimer = new() { Interval = 250 };
    private bool _controlsReady;    // 자식 컨트롤 핸들이 모두 만들어졌는지(OnLoad 이후 true)
    private bool _darkCard = true; // 칸 배경이 어두운지(스크롤 막대 테마 선택용)
    private bool _translatedIsNote;  // 번역 칸에 번역문이 아니라 안내 글(진행 상태·오류)이 들어 있는지
    private bool _autoCloseArmed;  // 번역이 끝나 자동 닫힘 시간을 재기 시작했는지
    private int _idleMs;            // 마우스가 창 밖에 있었던 시간
    private bool _layoutReady;      // 처음 배치가 끝난 뒤에만 크기 변경을 설정에 저장한다
    private bool _suppressEvents;   // 코드에서 콤보 값을 바꿀 때 이벤트 처리를 막는다
    private int _generation;        // 늦게 도착한 이전 요청 결과를 버리기 위한 번호

    public ResultForm(AppConfig config, Rectangle anchor, Bitmap? crop, Action openSettings, bool preview = false)
    {
        _config = config;
        _crop = crop;
        _anchor = anchor;
        _preview = preview;
        _keepFocus = config.KeepFocusOnApp;

        Text = "SnapTranslate";
        Font = new Font(Theme.FontName, 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        if (preview) TopLevel = false;   // 설정 창 안에 넣는 자식 창
        // TopMost 속성은 창을 띄울 때 포커스를 가져오므로, 포커스 유지 모드에서는 CreateParams 의 확장 스타일로 맨 위에 둔다
        else if (!config.KeepFocusOnApp) TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        ForeColor = Theme.Text;
        Padding = new Padding(ResizeGrip);   // 가장자리를 비워 두어 크기 조절 손잡이로 쓴다
        ClientSize = new Size(560, 400);
        MinimumSize = new Size(80, 40);

        // ---- 제목 줄 ----
        _title = new Label
        {
            Text = "SnapTranslate",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = _titleFont,
            Padding = new Padding(6, 0, 0, 0),
        };
        _close = CreateTitleButton("✕");
        _settings = CreateTitleButton("⚙");
        _close.FlatAppearance.MouseOverBackColor = Theme.Danger;

        _titleBar = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _titleBar.Controls.Add(_title);
        _titleBar.Controls.Add(_settings);
        _titleBar.Controls.Add(_close);

        // ---- 언어 선택 줄 ----
        _source = Theme.CreateLangCombo(true, config.SourceLang);
        _target = Theme.CreateLangCombo(false, config.TargetLang);
        _source.Margin = _target.Margin = new Padding(0, 4, 0, 0);
        _arrow = new Label { Text = "→", AutoSize = true, Margin = new Padding(8, 7, 8, 0) };

        _langRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(6, 2, 0, 0) };
        _langRow.Controls.Add(_source);
        _langRow.Controls.Add(_arrow);
        _langRow.Controls.Add(_target);

        // ---- 원문 / 번역 ----
        _original = CreateTextBox(false);
        _translated = CreateTextBox(true);
        foreach (TextBox box in new[] { _original, _translated })
        {
            // 글이 칸을 넘칠 때만 스크롤 막대를 보인다
            box.TextChanged += (_, _) => UpdateScrollBar(box);
            box.SizeChanged += (_, _) => UpdateScrollBar(box);
            box.MouseWheel += (_, e) => OnTextBoxWheel(box, e);
            // 핸들이 다시 만들어져도 스크롤 막대 테마가 유지되게 한다
            box.HandleCreated += (_, _) => Theme.ApplyScrollTheme(box, _darkCard);
        }
        _originalCard = CreateCard("원문", _original, out _originalCaption);
        _translatedCard = CreateCard("번역", _translated, out _translatedCaption);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel1,   // 창 세로를 늘리면 번역 칸이 늘어난다
            SplitterWidth = 6,
            Panel1MinSize = AppConfig.MinPaneHeight,
            Panel2MinSize = AppConfig.MinPaneHeight,
            Margin = Padding.Empty,
            TabStop = false,
            IsSplitterFixed = preview,
        };
        _split.Panel1.Padding = new Padding(4, 4, 4, 0);
        _split.Panel2.Padding = new Padding(4, 0, 4, 4);
        _split.Panel1.Controls.Add(_originalCard);
        _split.Panel2.Controls.Add(_translatedCard);

        // ---- 아래 줄 ----
        _status = new Label { Text = "글자 인식 중…", AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(Theme.FontName, 9f) };
        _opacityLabel = new Label { Text = "투명도", AutoSize = true, Anchor = AnchorStyles.None, Font = new Font(Theme.FontName, 9f) };
        _opacity = new SlimSlider { Minimum = 30, Maximum = 100, Width = 100, Anchor = AnchorStyles.None };
        _opacity.Value = Math.Clamp(config.Opacity, 30, 100);

        _retranslate = new Button { Text = "다시 번역", Enabled = preview };
        _copy = new Button { Text = "복사", Enabled = preview };
        Theme.StyleButton(_retranslate, false);
        Theme.StyleButton(_copy, true);

        _bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(6, 0, 2, 0) };
        _bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) _bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _bottom.Controls.Add(_status, 0, 0);
        _bottom.Controls.Add(_opacityLabel, 1, 0);
        _bottom.Controls.Add(_opacity, 2, 0);
        _bottom.Controls.Add(_retranslate, 3, 0);
        _bottom.Controls.Add(_copy, 4, 0);

        // ---- 전체 배치 ----
        _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        _root.Controls.Add(_titleBar, 0, 0);
        _root.Controls.Add(_langRow, 0, 1);
        _root.Controls.Add(_split, 0, 2);
        _root.Controls.Add(_bottom, 0, 3);
        Controls.Add(_root);

        // 제목 줄을 숨겼을 때 쓰는 닫기 단추. 배치 표에 넣지 않고 창 위에 직접 올려 내용과 겹치게 한다
        _floatClose = CreateTitleButton("✕");
        _floatClose.Dock = DockStyle.None;
        _floatClose.Size = new Size(20, 18);
        _floatClose.Font = new Font("Segoe UI Symbol", 8f);
        // 위치는 PlaceFloatClose 에서 직접 정한다(창 바깥 여백 위에 두면 Anchor 로는 오른쪽에 붙어 있지 않았다)
        _floatClose.FlatAppearance.MouseOverBackColor = Theme.Danger;
        _floatClose.Visible = false;
        Controls.Add(_floatClose);
        _floatClose.BringToFront();

        // 번역기 이름. 아래 줄을 숨겨도 보이게, 닫기 단추처럼 창 위에 직접 올린다
        _engineLabel = new Label
        {
            AutoSize = true,
            Font = new Font(Theme.FontName, 8.5f, FontStyle.Bold),
            Padding = new Padding(6, 2, 6, 2),
            Visible = false,
        };
        Controls.Add(_engineLabel);
        // 진행 중 안내 글 뒤에 붙는 색 점. 글 상자는 한 가지 색만 쓸 수 있어서 따로 그려 겹친다
        _busyDots = new BusyDots { Visible = false };
        Controls.Add(_busyDots);
        _split.SplitterMoved += (_, _) => PlaceEngineLabel();
        _split.SizeChanged += (_, _) => PlaceEngineLabel();
        VisibleChanged += (_, _) => PlaceEngineLabel();

        if (preview)
        {
            // 미리보기는 모양만 보여 주므로 예시 글을 넣고 동작은 연결하지 않는다
            SetText(_original, "The quick brown fox jumps over the lazy dog.\r\nPress the hotkey and drag to select a region.");
            SetText(_translated, "날렵한 갈색 여우가 게으른 개를 뛰어넘습니다.\r\n단축키를 누르고 끌어서 영역을 선택하세요.");
            _status.Text = "영어(감지) → 한국어";
            _engineName = Translator.EngineName(Translator.Google);
        }
        else
        {
            WireEvents(openSettings);
            Opacity = _opacity.Value / 100.0;
        }
    }

    /// <summary>원문·번역 칸에 글을 직접 넣는다(모양 점검·화면 그림용)</summary>
    public void SetTexts(string original, string translated, string? status = null)
    {
        SetText(_original, original);
        ShowTranslation(translated);
        _retranslate.Enabled = original.Length > 0;
        _copy.Enabled = translated.Length > 0;
        if (status != null) _status.Text = status;
        _engineName = translated.Length > 0 ? Translator.EngineName(Translator.Google) : "";
        PlaceEngineLabel();
    }

    /// <summary>닫기 단추를 창 오른쪽 위 구석에 바짝 붙인다</summary>
    private void PlaceFloatClose()
    {
        _floatClose.Location = new Point(ClientSize.Width - _floatClose.Width - 2, 2);
        _floatClose.BringToFront();
    }

    /// <summary>번역기 이름을 내용(원문·번역 칸) 오른쪽 아래 구석에 놓는다. 번역 전이거나 옵션이 꺼져 있으면 숨긴다</summary>
    private void PlaceEngineLabel()
    {
        if (_engineLabel == null) return;   // 생성자 도중에도 불린다
        _engineLabel.Text = "번역기 · " + _engineName;
        _engineLabel.Visible = _config.ShowEngine && _engineName.Length > 0;
        if (!_engineLabel.Visible) return;

        // 세로 스크롤 막대와 겹치지 않게 그 폭만큼 왼쪽에 둔다
        Rectangle content = RectangleToClient(_split.RectangleToScreen(_split.ClientRectangle));
        int right = content.Right - 6 - SystemInformation.VerticalScrollBarWidth;
        _engineLabel.Location = new Point(right - _engineLabel.Width, content.Bottom - _engineLabel.Height - 6);
        _engineLabel.BringToFront();
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (_floatClose != null) PlaceFloatClose();   // 생성자 도중에도 불리므로 만들어진 뒤에만
        PlaceEngineLabel();
    }

    /// <summary>Ctrl + 마우스 휠로 그 칸의 글자 크기를 0.5 씩 바꾸고 설정에 저장한다</summary>
    private void OnTextBoxWheel(TextBox box, MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) == 0 || _preview) return;
        if (e is HandledMouseEventArgs handled) handled.Handled = true;   // 글이 같이 스크롤되지 않게

        double step = e.Delta > 0 ? 0.5 : -0.5;
        if (box == _original)
            _config.OriginalFontSize = Math.Clamp(_config.OriginalFontSize + step, 6, 40);
        else
            _config.TranslatedFontSize = Math.Clamp(_config.TranslatedFontSize + step, 6, 40);

        SetFont(_original, _config.FontName, (float)_config.OriginalFontSize);
        SetFont(_translated, _config.FontName, (float)_config.TranslatedFontSize);
        UpdateScrollBar(box);
        try { _config.Save(); }
        catch (Exception ex) { _status.Text = "설정 저장 실패: " + ex.Message; }
    }

    /// <summary>
    /// 번역 칸에 진행 상태·오류 같은 안내 글을 흐린 색으로 보인다.
    /// 아래 줄(상태 표시)을 숨기고 쓰면 번역되는 동안 칸이 빈 채로 보여, 번역 칸 자체에 상태를 적는다.
    /// </summary>
    private void ShowNote(string text) => ShowNote(text, false);

    // 진행 중 표시의 점이 강조되는 순서(왼쪽 → 오른쪽 → 다시 왼쪽). 멈춘 것이 아니라 일하는 중임을 보인다
    private static readonly int[] BusySteps = [0, 1, 2, 1];
    private readonly System.Windows.Forms.Timer _busyTimer = new() { Interval = 300 };
    private int _busyStep;

    /// <summary>진행 중 안내를 움직이는 색 점과 함께 보인다. 다른 글(번역문·오류)이 들어오면 멈춘다</summary>
    private void ShowBusy(string text)
    {
        _busyStep = 0;
        ShowNote(text, true);
        _busyDots.Frame = BusySteps[0];
        PlaceBusyDots();
        _busyTimer.Start();
    }

    private void OnBusyTick()
    {
        // 안내가 이미 다른 글로 바뀌었으면 멈춘다
        if (!_translatedIsNote || IsDisposed) { StopBusy(); return; }
        _busyStep = (_busyStep + 1) % BusySteps.Length;
        _busyDots.Frame = BusySteps[_busyStep];
        PlaceBusyDots();   // 그 사이 창 크기나 글자 크기가 바뀌었을 수 있다
    }

    private void StopBusy()
    {
        _busyTimer.Stop();
        _busyDots.Visible = false;
    }

    /// <summary>색 점을 안내 글의 마지막 글자 바로 뒤에 놓는다. 번역 칸이 안 보이면 점도 숨긴다</summary>
    private void PlaceBusyDots()
    {
        if (!_busyTimer.Enabled && !_translatedIsNote) return;
        if (!_translated.Visible || !_translated.IsHandleCreated || _translated.TextLength == 0 || _translated.Width < 40)
        {
            _busyDots.Visible = false;
            return;
        }

        int last = _translated.TextLength - 1;
        Point at = _translated.GetPositionFromCharIndex(last);
        string lastChar = _translated.Text.Substring(last, 1);
        Size charSize = TextRenderer.MeasureText(lastChar, _translated.Font, Size.Empty, TextFormatFlags.NoPadding);
        Size size = BusyDots.SizeFor(charSize.Height);
        Point origin = PointToClient(_translated.PointToScreen(at));

        _busyDots.Size = size;
        _busyDots.Location = new Point(origin.X + charSize.Width + size.Height / 3, origin.Y);
        _busyDots.BackColor = Theme.ParseColor(_config.CardColor, Theme.Card);
        _busyDots.ActiveColor = Theme.ParseColor(_config.AccentColor, Theme.Accent);
        _busyDots.IdleColor = Theme.ParseColor(_config.FieldColor, Theme.Field);
        _busyDots.Visible = true;
        _busyDots.BringToFront();
    }

    private void ShowNote(string text, bool busy)
    {
        if (!busy) StopBusy();
        _translatedIsNote = true;
        _engineName = "";   // 번역 중·실패 안내에는 번역기 이름을 붙이지 않는다
        PlaceEngineLabel();
        _translated.ForeColor = Theme.ParseColor(_config.LabelColor, Theme.SubText);
        SetText(_translated, text);
        _copy.Enabled = false;
    }

    /// <summary>번역 칸에 실제 번역문을 보인다(안내 글이었으면 글자 색을 되돌린다)</summary>
    private void ShowTranslation(string text)
    {
        StopBusy();
        _translatedIsNote = false;
        _translated.ForeColor = Theme.ParseColor(_config.TranslatedColor, Theme.Text);
        SetText(_translated, text);
    }

    /// <summary>
    /// 글 상자에 글을 넣고 선택을 푼다.
    /// TextBox 는 글을 넣은 뒤 처음 포커스를 받으면 전체를 선택해 파랗게 보이므로, 선택 위치를 직접 정해 그것을 막는다.
    /// </summary>
    private static void SetText(TextBox box, string text)
    {
        box.Text = text;
        box.Select(0, 0);
    }

    /// <summary>실제 결과 창의 동작(끌기·언어 변경·버튼·우클릭 메뉴·자동 닫힘)을 연결한다</summary>
    private void WireEvents(Action openSettings)
    {
        _close.Click += (_, _) => Close();
        _floatClose.Click += (_, _) => Close();
        _busyTimer.Tick += (_, _) => OnBusyTick();
        _settings.Click += (_, _) => openSettings();

        // 제목 줄을 숨겨도 창을 옮길 수 있게 칸 여백·칸 제목도 끌 수 있게 한다
        foreach (Control handle in new Control[] { _title, _originalCard, _translatedCard, _originalCaption, _translatedCaption })
            handle.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) Theme.DragWindow(this); };

        _source.SelectedIndexChanged += async (_, _) => { if (!_suppressEvents) await RecognizeAndTranslateAsync(false); };
        _target.SelectedIndexChanged += async (_, _) => { if (!_suppressEvents) await TranslateAsync(false); };
        _split.SplitterMoved += (_, _) => SaveLayout();
        _opacity.ValueChanged += (_, _) => Opacity = _opacity.Value / 100.0;
        _opacity.Committed += (_, _) => SaveOpacity();
        _retranslate.Click += async (_, _) => await TranslateAsync(false);
        _copy.Click += (_, _) => CopyTranslation();

        ContextMenuStrip = BuildMenu(openSettings);
        _closeTimer.Tick += (_, _) => OnCloseTimerTick();
    }

    /// <summary>우클릭 메뉴. 제목 줄·아래 줄을 숨긴 상태에서도 표시 항목·설정·닫기를 쓸 수 있게 한다</summary>
    private ContextMenuStrip BuildMenu(Action openSettings)
    {
        var menu = new ContextMenuStrip();
        var toggles = new List<(ToolStripMenuItem Item, Func<bool> Get)>();

        void AddToggle(string text, Func<bool> get, Action<bool> set)
        {
            var item = new ToolStripMenuItem(text) { CheckOnClick = true };
            item.Click += (_, _) =>
            {
                set(item.Checked);
                try { _config.Save(); }
                catch (Exception ex) { _status.Text = "설정 저장 실패: " + ex.Message; }
                ApplyLayout();
            };
            menu.Items.Add(item);
            toggles.Add((item, get));
        }

        AddToggle("제목 줄 표시", () => _config.ShowTitleBar, v => _config.ShowTitleBar = v);
        AddToggle("언어 선택 줄 표시", () => _config.ShowLanguageRow, v => _config.ShowLanguageRow = v);
        AddToggle("아래 줄 표시", () => _config.ShowBottomBar, v => _config.ShowBottomBar = v);
        AddToggle("칸 제목 표시", () => _config.ShowCaptions, v => _config.ShowCaptions = v);
        AddToggle("원문 칸 표시", () => _config.ShowOriginal, v => _config.ShowOriginal = v);
        AddToggle("번역 칸 표시", () => _config.ShowTranslated, v => _config.ShowTranslated = v);
        AddToggle("번역기 이름 표시", () => _config.ShowEngine, v => _config.ShowEngine = v);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("번역 복사", null, (_, _) => CopyTranslation());
        menu.Items.Add("설정…", null, (_, _) => openSettings());
        _closeMenuItem = menu.Items.Add(_config.CloseOnEsc ? "닫기 (Esc)" : "닫기", null, (_, _) => Close());

        // 설정 창에서 바꿨을 수도 있으므로 열 때마다 현재 값으로 맞춘다
        menu.Opening += (_, _) => { foreach (var (item, get) in toggles) item.Checked = get(); };
        return menu;
    }

    private static Button CreateTitleButton(string glyph)
    {
        var button = new Button
        {
            Text = glyph,
            Dock = DockStyle.Right,
            Width = 38,
            FlatStyle = FlatStyle.Flat,
            TabStop = false,
            Font = new Font("Segoe UI Symbol", 9.5f),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    /// <summary>글 높이가 칸 높이를 넘을 때만 세로 스크롤 막대를 켠다</summary>
    private void UpdateScrollBar(TextBox box)
    {
        // ScrollBars 속성은 바꾸지 않는다(핸들을 다시 만들다 오류 1400 발생). 막대만 보이고 숨긴다
        if (!_controlsReady || !box.IsHandleCreated) return;
        if (box.Width <= 0 || box.Height <= 0) return;
        // 막대가 생기면 줄바꿈 폭이 줄어들므로 항상 막대 폭을 뺀 폭으로 잰다(켜졌다 꺼졌다 하지 않게)
        int width = Math.Max(1, box.Width - SystemInformation.VerticalScrollBarWidth);
        Size size = TextRenderer.MeasureText(box.Text.Length == 0 ? " " : box.Text, box.Font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
        Theme.ShowVerticalScrollBar(box, size.Height > box.Height);
    }

    private static TextBox CreateTextBox(bool readOnly) => new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        ReadOnly = readOnly,
    };

    /// <summary>작은 제목이 붙은 글 상자 묶음</summary>
    private static Panel CreateCard(string caption, TextBox box, out Label captionLabel)
    {
        var card = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 6, 8) };
        captionLabel = new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font(Theme.FontName, 8.5f, FontStyle.Bold),
        };
        // Dock 은 나중에 넣은 컨트롤부터 자리를 잡으므로 Fill 인 글 상자를 먼저 넣는다
        card.Controls.Add(box);
        card.Controls.Add(captionLabel);
        return card;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (!_preview) cp.ClassStyle |= 0x20000; // CS_DROPSHADOW (최상위 창만 가능)
            if (!_preview && _keepFocus) cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            return cp;
        }
    }

    /// <summary>
    /// 포커스 유지 모드에서는 창을 띄워도 원래 쓰던 프로그램(게임 등)의 포커스를 빼앗지 않는다.
    /// 창을 클릭하면 그때 포커스를 받으므로 원문 편집·Esc 닫기는 클릭한 뒤에 쓸 수 있다.
    /// </summary>
    protected override bool ShowWithoutActivation => _keepFocus && !_preview;

    /// <summary>
    /// "같은 창에 계속 표시" 설정이면 닫을 때 창을 없애지 않고 숨기기만 한다.
    /// 창을 새로 만드는 데 0.4~0.6초가 걸려(진단 기록으로 확인) 매번 만들면 번역이 그만큼 늦게 뜬다.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_preview && !TestOffscreen && _config.ReuseWindow && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _generation++;            // 진행 중이던 인식·번역 결과는 버린다
            _autoCloseArmed = false;
            Hide();
            DebugLog.Write("결과 창 숨김");
        }
        base.OnFormClosing(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_preview) Theme.RoundCorners(Handle);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // 배율 조정이 끝난 뒤라야 실제 픽셀 크기를 맞출 수 있다
        _rowHeights = [_root.RowStyles[0].Height, _root.RowStyles[1].Height, _root.RowStyles[3].Height];
        _controlsReady = true;
        ApplyLayout();
        if (_preview) return;
        // 모양 점검 때는 화면 밖에 띄운다(실제 화면에 번쩍이지 않게)
        Location = TestOffscreen ? new Point(-30000, -30000) : Place();
        _layoutReady = true;
        _closeTimer.Start();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        SaveLayout();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // 테두리가 없으므로 가장자리를 크기 조절 영역으로 알려 준다
        if (_preview || m.Msg != WM_NCHITTEST || m.Result.ToInt32() != 1 /*HTCLIENT*/) return;

        long lp = m.LParam.ToInt64();
        Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
        int grip = Padding.Left;
        bool left = p.X < grip, right = p.X >= ClientSize.Width - grip;
        bool top = p.Y < grip, bottom = p.Y >= ClientSize.Height - grip;
        int hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17
                : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1;
        m.Result = hit;
    }

    // ---- 모양·배치 ----

    /// <summary>설정의 색·글꼴·글자 크기와 표시 항목을 창에 반영한다</summary>
    private void ApplyAppearance()
    {
        Color back = Theme.ParseColor(_config.BackgroundColor, Theme.Back);
        Color card = Theme.ParseColor(_config.CardColor, Theme.Card);
        Color accent = Theme.ParseColor(_config.AccentColor, Theme.Accent);
        Color label = Theme.ParseColor(_config.LabelColor, Theme.SubText);
        Color field = Theme.ParseColor(_config.FieldColor, Theme.Field);

        // 창 배경
        BackColor = back;
        _root.BackColor = back;
        _split.BackColor = back;
        _close.BackColor = _settings.BackColor = back;
        _settings.FlatAppearance.MouseOverBackColor = field;
        _opacity.BackColor = back;
        _opacity.AccentColor = accent;
        _opacity.TrackColor = field;
        _opacity.Invalidate();

        // 칸
        _originalCard.BackColor = _translatedCard.BackColor = card;
        _original.BackColor = _translated.BackColor = card;
        _original.ForeColor = Theme.ParseColor(_config.OriginalColor, Theme.SubText);
        _translated.ForeColor = _translatedIsNote ? label : Theme.ParseColor(_config.TranslatedColor, Theme.Text);
        SetFont(_original, _config.FontName, (float)_config.OriginalFontSize);
        SetFont(_translated, _config.FontName, (float)_config.TranslatedFontSize);
        _darkCard = !Theme.IsLight(card);
        Theme.ApplyScrollTheme(_original, _darkCard);
        Theme.ApplyScrollTheme(_translated, _darkCard);
        UpdateScrollBar(_original);
        UpdateScrollBar(_translated);

        // 글자·강조 색
        _originalCaption.ForeColor = _translatedCaption.ForeColor = accent;
        _title.ForeColor = _status.ForeColor = _arrow.ForeColor = _opacityLabel.ForeColor = label;
        _close.ForeColor = _settings.ForeColor = label;

        // 언어 목록·버튼
        _source.BackColor = _target.BackColor = field;
        _source.ForeColor = _target.ForeColor = Theme.ContrastText(field);
        _retranslate.BackColor = field;
        _retranslate.ForeColor = Theme.ContrastText(field);
        _retranslate.FlatAppearance.MouseOverBackColor = ControlPaint.Light(field, 0.3f);
        _retranslate.FlatAppearance.MouseDownBackColor = field;
        _copy.BackColor = accent;
        _copy.ForeColor = Theme.ContrastText(accent);
        _copy.FlatAppearance.MouseOverBackColor = ControlPaint.Light(accent, 0.3f);
        _copy.FlatAppearance.MouseDownBackColor = accent;

        // 표시 항목
        // 제목 줄을 숨기면 줄 자체는 없애고(위쪽 여백이 생기지 않게), 닫기 단추만 내용 오른쪽 위에 겹쳐 띄운다
        SetRow(0, _titleBar, _config.ShowTitleBar, _rowHeights[0]);
        _floatClose.Visible = !_config.ShowTitleBar;
        _floatClose.BackColor = back;   // 창 구석(바깥 여백) 위에 놓이므로 창 배경색에 맞춘다
        _floatClose.ForeColor = label;
        PlaceFloatClose();
        _floatClose.BringToFront();
        if (_closeMenuItem != null) _closeMenuItem.Text = _config.CloseOnEsc ? "닫기 (Esc)" : "닫기";
        SetRow(1, _langRow, _config.ShowLanguageRow, _rowHeights[1]);
        SetRow(3, _bottom, _config.ShowBottomBar, _rowHeights[2]);
        _originalCaption.Visible = _translatedCaption.Visible = _config.ShowCaptions;

        // 원문 칸·번역 칸. 먼저 보일 쪽을 펴고 나서 숨길 쪽을 접는다(둘이 동시에 접힌 상태를 거치지 않게)
        if (ShowsOriginal) _split.Panel1Collapsed = false;
        if (ShowsTranslated) _split.Panel2Collapsed = false;
        _split.Panel1Collapsed = !ShowsOriginal;
        _split.Panel2Collapsed = !ShowsTranslated;
        // 한 칸만 보일 때는 그 칸의 위아래 여백을 같게 한다
        _split.Panel1.Padding = new Padding(4, 4, 4, ShowsTranslated ? 0 : 4);
        _split.Panel2.Padding = new Padding(4, ShowsOriginal ? 0 : 4, 4, 4);
        PerformLayout();
        _root.PerformLayout();

        // 번역기 이름은 한눈에 보이게 강조색 배지로 그린다. 칸 크기가 정해진 뒤에 자리를 잡는다
        _engineLabel.BackColor = accent;
        _engineLabel.ForeColor = Theme.ContrastText(accent);
        PlaceEngineLabel();
    }

    private static void SetFont(TextBox box, string name, float size)
    {
        if (box.Font.Name == name && Math.Abs(box.Font.SizeInPoints - size) < 0.01f) return;
        Font font;
        try { font = new Font(name, size); }
        catch (ArgumentException) { font = new Font(Theme.FontName, size); }   // 없는 글꼴이면 기본 글꼴
        box.Font = font;
    }

    private bool ShowsOriginal => _config.ShowOriginal;

    /// <summary>둘 다 끈 설정이면 번역 칸은 보인다</summary>
    private bool ShowsTranslated => _config.ShowTranslated || !_config.ShowOriginal;

    /// <summary>줄을 숨길 때는 높이를 0 으로, 보일 때는 원래 높이로 되돌린다</summary>
    private void SetRow(int row, Control control, bool visible, float height)
    {
        control.Visible = visible;
        _root.RowStyles[row].Height = visible ? height : 0;
    }

    /// <summary>설정의 모양과 가로·원문 세로·번역 세로 크기대로 창과 칸을 맞춘다</summary>
    public void ApplyLayout()
    {
        bool wasReady = _layoutReady;
        _layoutReady = false;   // 맞추는 동안 생기는 크기 변경은 저장하지 않는다
        try
        {
            ApplyAppearance();
            float scale = DeviceDpi / 96f;
            int original = (int)(_config.OriginalHeight * scale);
            int translated = (int)(_config.TranslatedHeight * scale);
            // 원문·번역 칸을 뺀 나머지(제목 줄·언어 줄·아래 줄·여백) 높이
            int chrome = ClientSize.Height - _split.Height;
            bool both = ShowsOriginal && ShowsTranslated;
            // 숨긴 칸의 높이만큼 창도 줄인다
            int body = (ShowsOriginal ? original : 0) + (ShowsTranslated ? translated : 0) + (both ? _split.SplitterWidth : 0);
            ClientSize = new Size((int)(_config.WindowWidth * scale), chrome + body);

            if (both)
            {
                int max = _split.Height - _split.Panel2MinSize - _split.SplitterWidth;
                if (max >= _split.Panel1MinSize)
                    _split.SplitterDistance = Math.Clamp(original, _split.Panel1MinSize, max);
            }
        }
        finally
        {
            _layoutReady = wasReady;
        }
    }

    /// <summary>마우스로 바꾼 창·칸 크기(위치가 "직접 지정"이면 자리도)를 설정에 저장해 다음 창에도 쓴다</summary>
    private void SaveLayout()
    {
        if (_preview || !_layoutReady || IsDisposed) return;
        float scale = DeviceDpi / 96f;
        int width = (int)Math.Round(ClientSize.Width / scale);
        // 숨긴 칸의 높이는 건드리지 않고, 한 칸만 보일 때는 그 칸이 전체 높이를 쓴다
        bool both = ShowsOriginal && ShowsTranslated;
        int original = !ShowsOriginal ? _config.OriginalHeight
            : (int)Math.Round((both ? _split.Panel1.Height : _split.Height) / scale);
        int translated = !ShowsTranslated ? _config.TranslatedHeight
            : (int)Math.Round((both ? _split.Panel2.Height : _split.Height) / scale);
        bool custom = _config.PositionMode == "Custom";
        bool moved = custom && (Left != _config.PositionX || Top != _config.PositionY);
        if (!moved && width == _config.WindowWidth && original == _config.OriginalHeight && translated == _config.TranslatedHeight) return;

        if (custom)
        {
            _config.PositionX = Left;
            _config.PositionY = Top;
        }
        _config.WindowWidth = width;
        _config.OriginalHeight = original;
        _config.TranslatedHeight = translated;
        try { _config.Save(); }
        catch (Exception ex) { _status.Text = "설정 저장 실패: " + ex.Message; }
    }

    /// <summary>설정의 창 위치에 따라 창을 놓을 자리를 정한다</summary>
    private Point Place()
    {
        // 구석 위치는 영역을 선택한 모니터의 작업 영역 기준
        Rectangle area = Screen.FromRectangle(_anchor).WorkingArea;
        const int margin = 12;
        switch (_config.PositionMode)
        {
            case "TopLeft": return new Point(area.Left + margin, area.Top + margin);
            case "TopRight": return new Point(area.Right - Width - margin, area.Top + margin);
            case "BottomLeft": return new Point(area.Left + margin, area.Bottom - Height - margin);
            case "BottomRight": return new Point(area.Right - Width - margin, area.Bottom - Height - margin);
            case "Custom":
                // 모니터 구성이 바뀌어도 창이 화면 밖으로 사라지지 않게 한다
                Rectangle all = SystemInformation.VirtualScreen;
                return new Point(
                    Math.Clamp(_config.PositionX, all.Left, Math.Max(all.Left, all.Right - Width)),
                    Math.Clamp(_config.PositionY, all.Top, Math.Max(all.Top, all.Bottom - Height)));
            default:
                // 선택 영역 바로 아래(자리가 없으면 위)에, 화면 밖으로 나가지 않게 놓는다
                int x = _anchor.Left;
                int y = _anchor.Bottom + 8;
                if (y + Height > area.Bottom) y = _anchor.Top - Height - 8;
                x = Math.Max(area.Left, Math.Min(x, area.Right - Width));
                y = Math.Max(area.Top, Math.Min(y, area.Bottom - Height));
                return new Point(x, y);
        }
    }

    /// <summary>설정에 저장된 불투명도를 다시 읽어 적용한다</summary>
    public void ApplyOpacity() => _opacity.Value = Math.Clamp(_config.Opacity, 30, 100);

    private void SaveOpacity()
    {
        _config.Opacity = _opacity.Value;
        try { _config.Save(); }
        catch (Exception ex) { _status.Text = "설정 저장 실패: " + ex.Message; }
    }

    // ---- 자동 닫힘 ----

    /// <summary>번역이 끝난 뒤 마우스가 창 밖에 있는 시간이 설정값을 넘으면 창을 닫는다</summary>
    private void OnCloseTimerTick()
    {
        if (!_autoCloseArmed || _config.AutoCloseSeconds <= 0) return;
        bool busy = Bounds.Contains(Cursor.Position) || _source.DroppedDown || _target.DroppedDown ||
                    MouseButtons != MouseButtons.None || Application.OpenForms.OfType<SettingsForm>().Any();
        if (busy)
        {
            _idleMs = 0;
            return;
        }
        _idleMs += _closeTimer.Interval;
        if (_idleMs >= _config.AutoCloseSeconds * 1000)
        {
            DebugLog.Write("결과 창 자동 닫힘");
            Close();
        }
    }

    /// <summary>결과가 나온 시점부터 자동 닫힘 시간을 다시 잰다</summary>
    private void ArmAutoClose()
    {
        _idleMs = 0;
        _autoCloseArmed = true;
    }

    // ---- 인식·번역 ----

    /// <summary>캡처 그림을 인식하고 번역한다</summary>
    public async void Start() => await RecognizeAndTranslateAsync(true);

    /// <summary>열려 있는 이 창에 새 캡처를 넣어 다시 인식·번역한다("같은 창에 계속 표시" 설정)</summary>
    public void LoadCapture(Bitmap crop, Rectangle anchor)
    {
        _generation++;   // 진행 중이던 이전 요청 결과는 버린다
        _crop?.Dispose();
        _crop = crop;
        _anchor = anchor;

        SetText(_original, "");
        ShowBusy("화면에서 글자 읽는 중");
        _copy.Enabled = false;
        _autoCloseArmed = false;
        // 이전 캡처에서 바꾼 언어 선택은 설정 기본값으로 되돌린다
        _suppressEvents = true;
        Theme.SelectLang(_source, _config.SourceLang);
        Theme.SelectLang(_target, _config.TargetLang);
        _suppressEvents = false;

        // "직접 지정"은 사용자가 놓은 자리를 그대로 두고, 나머지는 새 선택 영역 기준으로 다시 놓는다
        if (_config.PositionMode != "Custom") Location = Place();
        if (!Visible) Show();   // 닫아서 숨겨 둔 창이면 다시 보인다
        if (!_keepFocus) Activate();
        Start();
    }

    /// <summary>
    /// 고른 원문 언어로 글자를 인식한 뒤 번역한다.
    /// initial 이면 설정의 "원문이 번역 언어일 때" 규칙을 적용한다.
    /// </summary>
    private async Task RecognizeAndTranslateAsync(bool initial)
    {
        if (_crop == null) return;
        int generation = ++_generation;
        string source = Theme.SelectedCode(_source);
        string note = "";
        try
        {
            string[]? languages;   // 빈 배열 = 설치된 모든 언어로 읽어 가장 맞는 것을 고름, null = 다시 인식하지 않음
            if (source == "auto")
                languages = [];
            else if (OcrService.CanRead(source))
                languages = [source];
            else
            {
                // 그 언어의 OCR 이 없으면 다시 인식하지 않고 번역 쪽 원문 언어만 바꾼다
                languages = null;
                note = $" · {Theme.LangName(source)} OCR 미설치";
            }

            if (languages != null || _original.Text.Length == 0)
            {
                _status.Text = "글자 인식 중…";
                ShowBusy("화면에서 글자 읽는 중");
                var ocrTimer = System.Diagnostics.Stopwatch.StartNew();
                var ocr = await OcrService.RecognizeAsync(_crop, languages ?? []);
                DebugLog.Write($"글자 인식 {_crop.Width}x{_crop.Height} {ocrTimer.ElapsedMilliseconds}ms 글자수={ocr.Text.Length} 엔진={ocr.Language}");
                if (IsDisposed || generation != _generation) return;
                SetText(_original, ocr.Text);
            }
            _retranslate.Enabled = true;
            if (_original.Text.Trim().Length == 0)
            {
                ShowNote("글자를 찾지 못했습니다.");
                _copy.Enabled = false;
                _status.Text = "글자를 찾지 못했습니다." + note;
                ArmAutoClose();
                return;
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                DebugLog.Error("글자 인식", ex);
                _status.Text = "인식 실패: " + ex.Message;
                ShowNote("인식 실패: " + ex.Message);
            }
            return;
        }
        await TranslateAsync(initial, note);
    }

    /// <summary>원문 칸의 글을 고른 언어로 번역한다</summary>
    private async Task TranslateAsync(bool initial, string note = "")
    {
        string text = _original.Text.Trim();
        if (text.Length == 0) return;

        int generation = ++_generation;
        string source = Theme.SelectedCode(_source);
        string target = Theme.SelectedCode(_target);
        // 사용자가 번역 언어를 직접 골랐으면 그대로 쓰고, 처음 한 번만 자동 전환 규칙을 쓴다
        string? alt = initial && source == "auto" ? _config.AltTargetLang : null;

        _status.Text = "번역 중…";
        ShowBusy("번역 준비 중");
        var options = TranslateOptions.From(_config) with
        {
            // 어느 번역기에 묻는 중인지, 앞의 번역기가 막혀 넘어온 것인지 보인다
            OnTry = (engine, failed) =>
            {
                if (IsDisposed || generation != _generation) return;
                string name = Translator.EngineName(engine);
                ShowBusy(failed == null
                    ? $"{name}에 물어보는 중"
                    : $"{Translator.EngineName(failed)} 응답 없음 → {name}에 다시 물어보는 중");
            },
        };
        try
        {
            var translateTimer = System.Diagnostics.Stopwatch.StartNew();
            var result = await Translator.TranslateAsync(text, source, target, alt, options);
            DebugLog.Write($"번역 {translateTimer.ElapsedMilliseconds}ms 글자수={result.Text.Length} 번역기={result.Engine} 맨 앞 창: {DebugLog.Foreground()}");
            if (IsDisposed || generation != _generation) return;

            ShowTranslation(result.Text);
            _engineName = result.Engine.Length > 0 ? Translator.EngineName(result.Engine) : "";
            PlaceEngineLabel();
            _copy.Enabled = result.Text.Length > 0;
            // 자동 전환으로 번역 언어가 바뀌었으면 콤보에도 반영한다
            _suppressEvents = true;
            Theme.SelectLang(_target, result.TargetLang);
            _suppressEvents = false;

            string from = source == "auto" && result.SourceLang.Length > 0
                ? $"{Theme.LangName(result.SourceLang)}(감지)"
                : Theme.LangName(source);
            _status.Text = $"{from} → {Theme.LangName(result.TargetLang)}{note}";
            if (_config.SaveHistory && result.Text.Length > 0)
                HistoryStore.Add(new HistoryEntry(DateTime.Now, text, result.Text,
                    result.SourceLang.Length > 0 ? result.SourceLang : source, result.TargetLang, result.Engine));
            if (_config.CopyTranslation) CopyTranslation();
            ArmAutoClose();
        }
        catch (Exception ex)
        {
            if (!IsDisposed && generation == _generation)
            {
                _status.Text = "번역 실패: " + ex.Message;
                ShowNote("번역 실패: " + ex.Message);
            }
        }
    }

    private void CopyTranslation()
    {
        if (_translatedIsNote || _translated.Text.Length == 0) return;
        try
        {
            Clipboard.SetText(_translated.Text);
            _status.Text = "번역문을 복사했습니다.";
        }
        catch (Exception ex)
        {
            _status.Text = "복사 실패: " + ex.Message;
        }
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_preview) return;
        if (e.KeyCode == Keys.Escape)
        {
            if (!_config.CloseOnEsc) return;
            // 언어 목록이 펼쳐져 있을 때의 Esc 는 목록만 닫는다
            if (_source.DroppedDown || _target.DroppedDown) return;
            Close();
        }
        else if (e.Control && e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            await TranslateAsync(false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _closeTimer.Dispose();
            _crop?.Dispose();
        }
        base.Dispose(disposing);
    }
}
