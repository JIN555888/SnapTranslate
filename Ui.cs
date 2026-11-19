using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SnapTranslate;

/// <summary>언어 선택 목록의 한 항목</summary>
internal sealed record LangItem(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>창들이 같이 쓰는 색·글꼴·언어 목록·네이티브 호출</summary>
internal static class Theme
{
    public static readonly Color Back = Color.FromArgb(30, 31, 36);
    public static readonly Color Card = Color.FromArgb(42, 44, 51);
    public static readonly Color Field = Color.FromArgb(54, 57, 66);
    public static readonly Color Text = Color.FromArgb(236, 238, 242);
    public static readonly Color SubText = Color.FromArgb(150, 155, 168);
    public static readonly Color Accent = Color.FromArgb(79, 140, 255);
    public static readonly Color AccentHover = Color.FromArgb(104, 158, 255);
    public static readonly Color Danger = Color.FromArgb(232, 76, 76);

    public const string FontName = "맑은 고딕";

    public static readonly LangItem[] Languages =
    [
        new("ko", "한국어"),
        new("en", "영어"),
        new("zh-CN", "중국어(간체)"),
        new("zh-TW", "중국어(번체)"),
        new("ja", "일본어"),
        new("tl", "필리핀어"),
        new("ru", "러시아어"),
        new("vi", "베트남어"),
        new("th", "태국어"),
        new("id", "인도네시아어"),
        new("es", "스페인어"),
        new("fr", "프랑스어"),
        new("de", "독일어"),
    ];

    public static readonly LangItem AutoDetect = new("auto", "자동 감지");

    public static string LangName(string code)
    {
        var item = Languages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                   ?? Languages.FirstOrDefault(l => l.Code.Split('-')[0].Equals(code.Split('-')[0], StringComparison.OrdinalIgnoreCase));
        return item?.Name ?? code;
    }

    /// <summary>언어 목록 콤보를 만든다. withAuto 면 맨 위에 "자동 감지"를 넣는다</summary>
    public static ComboBox CreateLangCombo(bool withAuto, string selectedCode)
    {
        var combo = new DarkCombo
        {
            BackColor = Field,
            ForeColor = Text,
            Width = 150,
        };
        if (withAuto) combo.Items.Add(AutoDetect);
        combo.Items.AddRange(Languages);
        SelectLang(combo, selectedCode);
        return combo;
    }

    /// <summary>코드에 해당하는 항목을 고른다. 목록에 없는 코드면 항목을 추가한다</summary>
    public static void SelectLang(ComboBox combo, string code)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is LangItem item && item.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        var added = new LangItem(code, code);
        combo.Items.Add(added);
        combo.SelectedItem = added;
    }

    public static string SelectedCode(ComboBox combo) => (combo.SelectedItem as LangItem)?.Code ?? "auto";

    /// <summary>"#RRGGBB" 를 색으로 바꾼다. 형식이 틀리면 fallback 을 쓴다</summary>
    public static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex)) return ColorTranslator.FromHtml(hex.Trim());
        }
        catch (Exception)
        {
            // 잘못 적힌 색 값은 기본 색으로 대신한다
        }
        return fallback;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>실행 파일에 넣어 둔 앱 아이콘에서 원하는 크기를 꺼낸다</summary>
    public static Icon LoadAppIcon(Size size)
    {
        using Stream? stream = typeof(Theme).Assembly.GetManifestResourceStream("app.ico");
        return stream == null ? SystemIcons.Application : new Icon(stream, size);
    }

    /// <summary>배경이 밝은지(밝으면 어두운 글자·밝은 스크롤 막대를 쓴다)</summary>
    public static bool IsLight(Color background) =>
        (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0 > 0.6;

    /// <summary>배경색 위에서 읽기 좋은 글자색</summary>
    public static Color ContrastText(Color background) => IsLight(background) ? Back : Text;

    /// <summary>컨트롤의 스크롤 막대를 어두운/밝은 테마로 바꾼다</summary>
    public static void ApplyScrollTheme(Control control, bool dark)
    {
        if (control.IsHandleCreated)
            _ = SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
    }

    public static void StyleButton(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = primary ? Accent : Field;
        button.ForeColor = primary ? Color.White : Text;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(70, 74, 86);
        button.FlatAppearance.MouseDownBackColor = primary ? Accent : Card;
        button.Cursor = Cursors.Hand;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.Padding = new Padding(10, 3, 10, 3);
        button.Margin = new Padding(6, 0, 0, 0);
        button.Anchor = AnchorStyles.None;
    }

    // ---- 네이티브 ----

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hwnd, int bar, bool show);

    /// <summary>
    /// 세로 스크롤 막대를 보이거나 숨긴다.
    /// TextBox.ScrollBars 속성을 바꾸면 핸들을 다시 만들다가 실패(오류 1400)할 수 있어 Win32 로 직접 처리한다.
    /// </summary>
    public static void ShowVerticalScrollBar(Control control, bool show)
    {
        if (control.IsHandleCreated) ShowScrollBar(control.Handle, 1 /*SB_VERT*/, show);
    }

    /// <summary>테두리 없는 창의 모서리를 둥글게 한다(Windows 11, 실패해도 무시)</summary>
    public static void RoundCorners(IntPtr hwnd)
    {
        int round = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
    }

    /// <summary>제목 표시줄을 어두운 색으로 바꾼다</summary>
    public static void DarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        _ = DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
    }

    /// <summary>스크롤 막대 등 컨트롤 내부를 어두운 테마로 그리게 한다</summary>
    public static void DarkControl(Control control)
    {
        void Apply() => _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        if (control.IsHandleCreated) Apply();
        control.HandleCreated += (_, _) => Apply();
    }

    /// <summary>마우스로 잡은 컨트롤을 창 제목 표시줄처럼 끌게 한다</summary>
    public static void DragWindow(Form form)
    {
        ReleaseCapture();
        SendMessage(form.Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, 2 /*HTCAPTION*/, IntPtr.Zero);
    }

    public static bool IsWinKeyDown() => GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0;
}

/// <summary>
/// 배경·글자 색을 그대로 따르는 선택 목록.
/// 기본 ComboBox 는 접힌 상태의 칸을 시스템 색(흰색)으로 그리므로 직접 그린다.
/// </summary>
internal sealed class DarkCombo : ComboBox
{
    public DarkCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ItemHeight = Font.Height + 4;   // 글자가 잘리지 않게 줄 높이를 글꼴에 맞춘다
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ItemHeight = Font.Height + 4;
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        base.OnDropDownClosed(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    /// <summary>접힌 상태의 칸: 배경, 고른 항목 글자, 아래 화살표, 테두리</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Rectangle r = ClientRectangle;
        g.Clear(BackColor);

        int arrowWidth = r.Height;
        Color fore = Enabled ? ForeColor : Theme.SubText;
        TextRenderer.DrawText(g, GetItemText(SelectedItem), Font,
            new Rectangle(6, 0, Math.Max(0, r.Width - arrowWidth - 6), r.Height), fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = r.Right - arrowWidth / 2f, cy = r.Height / 2f, s = Math.Max(3f, r.Height / 8f);
        using var brush = new SolidBrush(fore);
        g.FillPolygon(brush, new[] { new PointF(cx - s, cy - s / 2), new PointF(cx + s, cy - s / 2), new PointF(cx, cy + s / 2 + 1) });

        g.SmoothingMode = SmoothingMode.None;
        using var border = new Pen(ControlPaint.Light(BackColor, 0.4f));
        g.DrawRectangle(border, 0, 0, r.Width - 1, r.Height - 1);
    }

    /// <summary>펼친 목록의 각 항목</summary>
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Theme.Accent : BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
            selected ? Color.White : ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>일정 단위로 움직이는 슬라이더와 그 옆의 현재 값 표시</summary>
internal sealed class ValueSlider : FlowLayoutPanel
{
    private readonly SlimSlider _slider;
    private readonly Label _label;
    private readonly decimal _minimum;
    private readonly decimal _step;
    private readonly string _suffix;

    public event EventHandler? ValueChanged;

    /// <param name="step">한 칸 움직일 때 바뀌는 값(예: 10, 0.5)</param>
    /// <param name="suffix">값 뒤에 붙일 단위(예: " px", " pt", "%")</param>
    public ValueSlider(decimal minimum, decimal maximum, decimal step, string suffix, Color back)
    {
        _minimum = minimum;
        _step = step;
        _suffix = suffix;
        AutoSize = true;
        WrapContents = false;
        Margin = Padding.Empty;
        BackColor = back;

        // 슬라이더 자체는 0 부터 "칸 수"까지의 정수로 움직이고, 실제 값은 최소값 + 칸 × 단위로 계산한다
        _slider = new SlimSlider { Minimum = 0, Maximum = (int)((maximum - minimum) / step), Width = 220, BackColor = back, Margin = new Padding(0, 4, 0, 4) };
        _label = new Label { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(10, 5, 0, 0) };
        _slider.ValueChanged += (_, _) =>
        {
            UpdateLabel();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
        Controls.Add(_slider);
        Controls.Add(_label);
        UpdateLabel();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Value
    {
        get => _minimum + _slider.Value * _step;
        set
        {
            _slider.Value = (int)Math.Round((value - _minimum) / _step);
            UpdateLabel();
        }
    }

    private void UpdateLabel() => _label.Text = Value.ToString("0.#") + _suffix;
}

/// <summary>
/// 화면 전체를 어둡게 덮고, 마우스를 따라다니는 결과 창 크기의 사각형을 보여 준다.
/// 클릭한 자리가 결과 창 왼쪽 위 좌표가 된다.
/// </summary>
internal sealed class PositionPicker : Form
{
    private const int WM_DPICHANGED = 0x02E0;
    private readonly Size _windowSize;
    private Point _cursor;

    /// <summary>고른 자리(화면 좌표)</summary>
    public Point Picked { get; private set; }

    public PositionPicker(Size windowSize)
    {
        _windowSize = windowSize;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.Black;
        Opacity = 0.55;
        Cursor = Cursors.Cross;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _cursor = PointToClient(Cursor.Position);
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        // 화면 좌표와 1:1 로 맞아야 하므로 DPI 변경에 따른 자동 크기 조정을 막는다
        if (m.Msg == WM_DPICHANGED) return;
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = new Rectangle(_cursor, _windowSize);
        using var fill = new SolidBrush(Theme.Accent);
        using var border = new Pen(Color.White, 2);
        e.Graphics.FillRectangle(fill, rect);
        e.Graphics.DrawRectangle(border, rect);
        using var font = new Font(Theme.FontName, 11f, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, "결과 창이 여기에 뜹니다\n클릭: 확정  ·  Esc/우클릭: 취소", font, rect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Location == _cursor) return;
        _cursor = e.Location;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Picked = PointToScreen(e.Location);
            DialogResult = DialogResult.OK;
        }
        else
        {
            DialogResult = DialogResult.Cancel;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
        base.OnKeyDown(e);
    }
}

/// <summary>어두운 테마용 얇은 슬라이더</summary>
internal sealed class SlimSlider : Control
{
    private int _value;
    private bool _dragging;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color AccentColor { get; set; } = Theme.Accent;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color TrackColor { get; set; } = Theme.Field;

    // 디자이너를 쓰지 않으므로 속성 직렬화는 끈다(WFO1000)
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Minimum { get; set; } = 0;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Maximum { get; set; } = 100;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, Minimum, Maximum);
            if (clamped == _value) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>끄는 동안 값이 바뀔 때마다</summary>
    public event EventHandler? ValueChanged;

    /// <summary>마우스를 놓아 값이 확정됐을 때</summary>
    public event EventHandler? Committed;

    public SlimSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(110, 22);
        Cursor = Cursors.Hand;
    }

    private int Radius => Math.Max(5, Height / 3);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        int r = Radius;
        float left = r, right = Width - r - 1, mid = Height / 2f;
        float ratio = Maximum == Minimum ? 0 : (float)(_value - Minimum) / (Maximum - Minimum);
        float x = left + (right - left) * ratio;

        using var track = new Pen(TrackColor, Math.Max(3f, Height / 6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fill = new Pen(AccentColor, track.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(track, left, mid, right, mid);
        g.DrawLine(fill, left, mid, x, mid);
        using var thumb = new SolidBrush(Color.White);
        g.FillEllipse(thumb, x - r, mid - r, r * 2, r * 2);
    }

    private void SetFromX(int mouseX)
    {
        int r = Radius;
        float left = r, right = Width - r - 1;
        float ratio = Math.Clamp((mouseX - left) / Math.Max(1f, right - left), 0f, 1f);
        Value = Minimum + (int)Math.Round(ratio * (Maximum - Minimum));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Committed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>진행 중임을 알리는 점 세 개. 지금 차례인 점만 강조색으로 칠한다</summary>
internal sealed class BusyDots : Control
{
    private int _frame;

    public BusyDots()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        TabStop = false;
    }

    /// <summary>강조할 점(0~2)</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Frame
    {
        get => _frame;
        set { _frame = value; Invalidate(); }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color ActiveColor { get; set; } = Theme.Accent;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color IdleColor { get; set; } = Theme.Field;

    /// <summary>글자 높이에 맞춘 크기(점 지름은 높이의 40%)</summary>
    public static Size SizeFor(int textHeight)
    {
        int dot = Math.Max(5, (int)(textHeight * 0.4));
        return new Size(dot * 3 + (dot / 2 + 2) * 2 + 2, textHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int dot = Math.Max(5, (int)(Height * 0.4));
        int gap = dot / 2 + 2;
        int y = (Height - dot) / 2 + 1;
        for (int i = 0; i < 3; i++)
        {
            using var brush = new SolidBrush(i == _frame ? ActiveColor : IdleColor);
            e.Graphics.FillEllipse(brush, 1 + i * (dot + gap), y, dot, dot);
        }
    }
}
