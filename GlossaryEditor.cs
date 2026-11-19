namespace SnapTranslate;

/// <summary>
/// 용어집 편집 표. 맨 위에 칸 제목(원문 | 번역), 그 아래 입력 줄, 그 아래 낱말 목록이 같은 칸 너비로 이어진다.
/// 낱말은 한 번에 하나씩 추가하고, 줄 끝의 ✕ 로 지운다.
/// </summary>
internal sealed class GlossaryEditor : Control
{
    private static readonly Color LineColor = Color.FromArgb(64, 67, 78);
    private static readonly Color HeaderColor = Color.FromArgb(34, 36, 42);
    private static readonly Color RowAltColor = Color.FromArgb(48, 51, 59);
    private static readonly Color RowSelectedColor = Color.FromArgb(62, 72, 98);

    private readonly Panel _header = new();
    private readonly TextBox _source;
    private readonly TextBox _target;
    private readonly Button _add;
    private readonly ListBox _list;
    private readonly Label _footer;

    private int _column;         // 원문·번역 칸 하나의 너비
    private int _action;         // 맨 오른쪽(추가 단추 / ✕) 칸의 너비
    private int _hoverDelete = -1;   // 마우스가 ✕ 위에 올라가 있는 줄

    public GlossaryEditor()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Field;
        ForeColor = Theme.Text;

        _header.BackColor = HeaderColor;
        _header.Paint += OnPaintHeader;

        _source = CreateBox("예: Mana Potion");
        _target = CreateBox("예: 마나 물약");
        // 두 칸 어디서든 Enter 를 누르면 추가한다
        foreach (var box in new[] { _source, _target })
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;   // 삑 소리와 저장 단추 눌림을 막는다
                AddTerm();
            };

        _add = new Button { Text = "추가", TabStop = true };
        Theme.StyleButton(_add, true);
        // 입력 칸과 높이·줄을 맞추려고 자동 크기와 여백을 끈다
        _add.AutoSize = false;
        _add.Padding = Padding.Empty;
        _add.Margin = Padding.Empty;
        _add.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _add.Click += (_, _) => AddTerm();

        _list = new ListBox
        {
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 28,
        };
        Theme.DarkControl(_list);
        _list.DrawItem += OnDrawItem;
        _list.SelectedIndexChanged += (_, _) => OnSelected();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveAt(_list.SelectedIndex); };
        _list.MouseMove += (_, e) => SetHover(DeleteIndexAt(e.Location));
        _list.MouseLeave += (_, _) => SetHover(-1);
        _list.MouseDown += (_, e) =>
        {
            int index = DeleteIndexAt(e.Location);
            if (e.Button == MouseButtons.Left && index >= 0) RemoveAt(index);
        };

        _footer = new Label { AutoSize = false, ForeColor = Theme.SubText, BackColor = HeaderColor, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };

        Controls.AddRange([_header, _source, _target, _add, _list, _footer]);
        UpdateFooter();
    }

    private static TextBox CreateBox(string placeholder) => new()
    {
        BackColor = Theme.Card,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        PlaceholderText = placeholder,
    };

    // ---- 값 넣기 / 꺼내기 ----

    /// <summary>"원문=번역" 줄들을 목록에 채운다</summary>
    public void SetLines(IEnumerable<string> lines)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var (source, target) in Translator.ParseGlossary(lines).OrderBy(term => term.Source, StringComparer.OrdinalIgnoreCase))
            _list.Items.Add(new LangItem(source, target));
        _list.EndUpdate();
        _source.Clear();
        _target.Clear();
        UpdateFooter();
    }

    /// <summary>목록을 "원문=번역" 줄들로 돌려준다(설정 파일에 저장하는 형식)</summary>
    public List<string> GetLines() =>
        _list.Items.OfType<LangItem>().Select(term => $"{term.Code}={term.Name}").ToList();

    // ---- 배치 ----

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        // 테두리 1픽셀 안쪽에 놓는다
        int width = ClientSize.Width - 2, height = ClientSize.Height - 2;
        if (width < 60 || height < 60) return;

        _action = Math.Max(48, (int)(width * 0.16));
        _column = (width - _action) / 2;

        int boxHeight = _source.PreferredHeight;
        int headerHeight = boxHeight + 4;
        int inputHeight = boxHeight + 12;
        int footerHeight = boxHeight + 2;

        int y = 1;
        _header.SetBounds(1, y, width, headerHeight);
        y += headerHeight;
        _source.SetBounds(1 + 6, y + 6, _column - 10, boxHeight);
        _target.SetBounds(1 + _column + 6, y + 6, _column - 10, boxHeight);
        _add.SetBounds(1 + _column * 2 + 4, y + 6, width - _column * 2 - 10, boxHeight);
        y += inputHeight;
        _list.SetBounds(1, y + 1, width, height - (y + 1) - footerHeight + 1);
        _footer.SetBounds(1, 1 + height - footerHeight, width, footerHeight);
        _header.Invalidate();
        _list.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var pen = new Pen(LineColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        // 입력 줄과 목록 사이의 가로줄, 입력 줄의 세로 칸 줄
        int lineY = _list.Top - 1;
        e.Graphics.DrawLine(pen, 1, lineY, Width - 2, lineY);
        e.Graphics.DrawLine(pen, 1 + _column, _header.Bottom, 1 + _column, lineY);
        e.Graphics.DrawLine(pen, 1 + _column * 2, _header.Bottom, 1 + _column * 2, lineY);
    }

    private void OnPaintHeader(object? sender, PaintEventArgs e)
    {
        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        using var font = new Font(Font, FontStyle.Bold);
        var bounds = _header.ClientRectangle;
        TextRenderer.DrawText(e.Graphics, "원문", font, new Rectangle(10, 0, _column - 10, bounds.Height), Theme.SubText, flags);
        TextRenderer.DrawText(e.Graphics, "번역", font, new Rectangle(_column + 10, 0, _column - 10, bounds.Height), Theme.SubText, flags);
        using var pen = new Pen(LineColor);
        e.Graphics.DrawLine(pen, _column, 0, _column, bounds.Height);
        e.Graphics.DrawLine(pen, _column * 2, 0, _column * 2, bounds.Height);
        e.Graphics.DrawLine(pen, 0, bounds.Height - 1, bounds.Width, bounds.Height - 1);
    }

    /// <summary>목록 한 줄: 원문 | 번역 | ✕. 칸 줄과 줄 사이 가로줄을 그어 표처럼 보이게 한다</summary>
    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count || _list.Items[e.Index] is not LangItem term) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        Rectangle row = e.Bounds;

        using (var back = new SolidBrush(selected ? RowSelectedColor : e.Index % 2 == 1 ? RowAltColor : Theme.Field))
            e.Graphics.FillRectangle(back, row);
        if (selected)
        {
            using var bar = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(bar, row.X, row.Y, 3, row.Height);
        }

        const TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(e.Graphics, term.Code, _list.Font, new Rectangle(row.X + 10, row.Y, _column - 14, row.Height), Theme.Text, flags);
        TextRenderer.DrawText(e.Graphics, term.Name, _list.Font, new Rectangle(row.X + _column + 10, row.Y, _column - 14, row.Height), Theme.Text, flags);
        var delete = new Rectangle(row.X + _column * 2, row.Y, row.Width - _column * 2, row.Height);
        using (var symbol = new Font("Segoe UI Symbol", 8.5f))
            TextRenderer.DrawText(e.Graphics, "✕", symbol, delete, e.Index == _hoverDelete ? Theme.Danger : Theme.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        using var pen = new Pen(LineColor);
        e.Graphics.DrawLine(pen, row.X + _column, row.Y, row.X + _column, row.Bottom);
        e.Graphics.DrawLine(pen, row.X + _column * 2, row.Y, row.X + _column * 2, row.Bottom);
        e.Graphics.DrawLine(pen, row.X, row.Bottom - 1, row.Right, row.Bottom - 1);
    }

    // ---- 동작 ----

    /// <summary>그 자리가 어느 줄의 ✕ 칸이면 그 줄 번호, 아니면 -1</summary>
    private int DeleteIndexAt(Point point)
    {
        if (point.X < _column * 2) return -1;
        int index = _list.IndexFromPoint(point);
        return index >= 0 && index < _list.Items.Count ? index : -1;
    }

    private void SetHover(int index)
    {
        if (index == _hoverDelete) return;
        _hoverDelete = index;
        _list.Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        _list.Invalidate();
    }

    /// <summary>입력 줄의 낱말을 목록에 넣는다. 같은 원문이 이미 있으면 번역만 바꾼다</summary>
    private void AddTerm()
    {
        string source = _source.Text.Trim();
        string target = _target.Text.Trim();
        if (source.Length == 0) { _source.Focus(); return; }
        if (target.Length == 0) { _target.Focus(); return; }
        // 저장 형식이 "원문=번역" 이라 원문에는 등호를 쓸 수 없다
        if (source.Contains('='))
        {
            _footer.ForeColor = Theme.Danger;
            _footer.Text = "원문에는 = 를 쓸 수 없습니다";
            _source.Focus();
            return;
        }

        var item = new LangItem(source, target);
        int index = -1;
        for (int i = 0; i < _list.Items.Count; i++)
            if (_list.Items[i] is LangItem term && term.Code.Equals(source, StringComparison.OrdinalIgnoreCase))
                index = i;

        if (index >= 0) _list.Items[index] = item;
        else index = _list.Items.Add(item);

        _list.SelectedIndex = -1;
        _list.TopIndex = Math.Max(0, index - 3);
        _source.Clear();
        _target.Clear();
        _source.Focus();   // 이어서 다음 낱말을 바로 적을 수 있게
        UpdateFooter();
    }

    private void RemoveAt(int index)
    {
        if (index < 0 || index >= _list.Items.Count) return;
        _list.Items.RemoveAt(index);
        _list.SelectedIndex = -1;
        _hoverDelete = -1;
        _source.Clear();
        _target.Clear();
        UpdateFooter();
    }

    /// <summary>목록에서 고른 낱말을 입력 줄에 불러온다(고친 뒤 추가를 누르면 바뀐다)</summary>
    private void OnSelected()
    {
        if (_list.SelectedItem is not LangItem term) return;
        _source.Text = term.Code;
        _target.Text = term.Name;
    }

    private void UpdateFooter()
    {
        _footer.ForeColor = Theme.SubText;
        _footer.Text = _list.Items.Count == 0 ? "아직 낱말이 없습니다" : $"낱말 {_list.Items.Count}개";
    }
}
