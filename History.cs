using System.Text.Json;
using Microsoft.Win32;

namespace SnapTranslate;

/// <summary>번역 한 건의 기록</summary>
internal sealed record HistoryEntry(DateTime Time, string Original, string Translated, string SourceLang, string TargetLang, string Engine);

/// <summary>
/// 번역 기록. 실행 파일 옆 history.json 에 최근 것부터 MaxEntries 건까지 둔다.
/// 설정의 "번역 기록 저장"이 꺼져 있으면 아무것도 쓰지 않는다.
/// </summary>
internal static class HistoryStore
{
    public const int MaxEntries = 200;

    private static readonly object Gate = new();
    private static List<HistoryEntry>? _items;   // 최근 것이 앞
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "history.json");

    /// <summary>기록이 바뀌면 불린다(열려 있는 기록 창이 다시 그리도록)</summary>
    public static event Action? Changed;

    public static IReadOnlyList<HistoryEntry> Items
    {
        get { lock (Gate) return Load().ToList(); }
    }

    private static List<HistoryEntry> Load()
    {
        if (_items != null) return _items;
        try
        {
            if (File.Exists(FilePath))
                _items = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath), JsonOptions);
        }
        catch (Exception ex)
        {
            // 파일이 깨졌으면 빈 기록으로 시작한다
            DebugLog.Error("번역 기록 읽기", ex);
        }
        return _items ??= [];
    }

    public static void Add(HistoryEntry entry)
    {
        lock (Gate)
        {
            var items = Load();
            // "다시 번역"처럼 같은 원문·같은 언어를 연달아 번역하면 앞의 것을 바꿔 넣는다
            if (items.Count > 0 && items[0].Original == entry.Original && items[0].TargetLang == entry.TargetLang)
                items.RemoveAt(0);
            items.Insert(0, entry);
            if (items.Count > MaxEntries) items.RemoveRange(MaxEntries, items.Count - MaxEntries);
            Save(items);
        }
        Changed?.Invoke();
    }

    public static void Remove(HistoryEntry entry)
    {
        lock (Gate)
        {
            var items = Load();
            if (!items.Remove(entry)) return;
            Save(items);
        }
        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Load().Clear();
            try { File.Delete(FilePath); }
            catch (Exception ex) { DebugLog.Error("번역 기록 지우기", ex); }
        }
        Changed?.Invoke();
    }

    private static void Save(List<HistoryEntry> items)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(items, JsonOptions));
        }
        catch (Exception ex)
        {
            // 저장하지 못해도 번역은 계속되어야 한다
            DebugLog.Error("번역 기록 저장", ex);
        }
    }
}

/// <summary>번역 기록을 보는 창. 왼쪽 목록에서 고르면 오른쪽에 원문과 번역문이 보인다</summary>
internal sealed class HistoryForm : Form
{
    private readonly ListBox _list;
    private readonly TextBox _original;
    private readonly TextBox _translated;
    private readonly Label _info;
    private readonly Label _empty;
    private readonly Button _copyOriginal;
    private readonly Button _copyTranslated;
    private readonly Button _delete;
    private readonly Button _clear;
    private List<HistoryEntry> _items = [];
    private readonly IReadOnlyList<HistoryEntry>? _sample;   // 모양 점검용 예시(주면 실제 기록 대신 이것을 보인다)

    public HistoryForm(IReadOnlyList<HistoryEntry>? sample = null)
    {
        _sample = sample;
        Text = "SnapTranslate 번역 기록";
        Icon = Theme.LoadAppIcon(SystemInformation.SmallIconSize);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font(Theme.FontName, 9.5f);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        ClientSize = new Size(900, 520);
        MinimumSize = new Size(640, 360);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        _list = new ListBox
        {
            Dock = DockStyle.Left,
            Width = 320,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 46,
        };
        _list.DrawItem += OnDrawItem;
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        Theme.DarkControl(_list);

        _info = new Label { Dock = DockStyle.Top, Height = 26, ForeColor = Theme.SubText, TextAlign = ContentAlignment.MiddleLeft };
        _original = CreateBox(Theme.SubText);
        _translated = CreateBox(Theme.Text);
        _translated.Font = new Font(Theme.FontName, 11f);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BackColor = Theme.Back,
            TabStop = false,
        };
        split.Panel1.Controls.Add(_original);
        split.Panel2.Controls.Add(_translated);

        _copyOriginal = CreateButton("원문 복사", false, () => Copy(_original.Text));
        _copyTranslated = CreateButton("번역 복사", true, () => Copy(_translated.Text));
        _delete = CreateButton("이 기록 삭제", false, DeleteSelected);
        _clear = CreateButton("모두 지우기", false, ClearAll);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(_copyTranslated);
        buttons.Controls.Add(_copyOriginal);
        buttons.Controls.Add(_delete);
        buttons.Controls.Add(_clear);

        _empty = new Label
        {
            Dock = DockStyle.Fill,
            Text = "아직 번역 기록이 없습니다.",
            ForeColor = Theme.SubText,
            TextAlign = ContentAlignment.MiddleCenter,
            Visible = false,
        };

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
        // Dock 은 나중에 넣은 것부터 자리를 잡는다: 아래 단추 → 위 안내 → 나머지를 원문·번역이 채운다
        right.Controls.Add(split);
        right.Controls.Add(_empty);
        right.Controls.Add(_info);
        right.Controls.Add(buttons);

        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        root.Controls.Add(right);
        root.Controls.Add(_list);
        Controls.Add(root);

        if (_sample == null) HistoryStore.Changed += OnStoreChanged;
        ActiveControl = _list;   // 글 상자가 먼저 포커스를 받으면 글 전체가 선택된다
        Reload();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        HistoryStore.Changed -= OnStoreChanged;
        base.OnFormClosed(e);
    }

    // 번역은 화면 스레드에서 끝나지만, 안전하게 창 스레드로 넘겨서 다시 그린다
    private void OnStoreChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(Reload);
    }

    private static TextBox CreateBox(Color color)
    {
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.Card,
            ForeColor = color,
            BorderStyle = BorderStyle.None,
        };
        Theme.DarkControl(box);
        return box;
    }

    private static Button CreateButton(string text, bool primary, Action action)
    {
        var button = new Button { Text = text };
        Theme.StyleButton(button, primary);
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += (_, _) => action();
        return button;
    }

    private void Reload()
    {
        HistoryEntry? selected = _list.SelectedIndex >= 0 && _list.SelectedIndex < _items.Count ? _items[_list.SelectedIndex] : null;
        _items = (_sample ?? HistoryStore.Items).ToList();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in _items) _list.Items.Add(item);
        _list.EndUpdate();

        int index = selected == null ? -1 : _items.IndexOf(selected);
        if (index < 0 && _items.Count > 0) index = 0;
        _list.SelectedIndex = index;
        ShowSelected();
    }

    private void ShowSelected()
    {
        HistoryEntry? entry = _list.SelectedIndex >= 0 && _list.SelectedIndex < _items.Count ? _items[_list.SelectedIndex] : null;
        bool has = entry != null;
        _empty.Visible = _items.Count == 0;
        _copyOriginal.Enabled = _copyTranslated.Enabled = _delete.Enabled = has;
        _clear.Enabled = _items.Count > 0;

        _original.Text = entry?.Original ?? "";
        _translated.Text = entry?.Translated ?? "";
        // 글을 넣으면 전체가 선택되어 파랗게 보이므로 선택을 푼다
        _original.Select(0, 0);
        _translated.Select(0, 0);
        _info.Text = entry == null
            ? ""
            : $"{entry.Time:yyyy-MM-dd HH:mm}   {Theme.LangName(entry.SourceLang)} → {Theme.LangName(entry.TargetLang)}   {Translator.EngineName(entry.Engine)}   ({_list.SelectedIndex + 1}/{_items.Count})";
    }

    /// <summary>목록 한 칸: 윗줄에 번역문 첫머리, 아랫줄에 시각과 원문 첫머리</summary>
    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _items.Count) return;
        HistoryEntry entry = _items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;

        using var back = new SolidBrush(selected ? Theme.Field : Theme.Card);
        e.Graphics.FillRectangle(back, e.Bounds);
        if (selected)
        {
            using var bar = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(bar, e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12);
        }

        const TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
        var top = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 4, e.Bounds.Width - 20, 20);
        var bottom = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 23, e.Bounds.Width - 20, 18);
        TextRenderer.DrawText(e.Graphics, OneLine(entry.Translated), Font, top, Theme.Text, flags);
        using var small = new Font(Theme.FontName, 8.5f);
        TextRenderer.DrawText(e.Graphics, $"{entry.Time:MM-dd HH:mm}  {OneLine(entry.Original)}", small, bottom, Theme.SubText, flags);
    }

    private static string OneLine(string text)
    {
        string line = text.ReplaceLineEndings(" ").Trim();
        return line.Length > 120 ? line[..120] : line;
    }

    private void Copy(string text)
    {
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); }
        catch (Exception ex) { MessageBox.Show(this, "복사 실패: " + ex.Message, AppInfo.Name); }
    }

    private void DeleteSelected()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _items.Count) return;
        HistoryStore.Remove(_items[_list.SelectedIndex]);
    }

    private void ClearAll()
    {
        if (MessageBox.Show(this, $"번역 기록 {_items.Count}건을 모두 지울까요? 되돌릴 수 없습니다.", AppInfo.Name,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        HistoryStore.Clear();
    }
}

/// <summary>Windows 에 로그인할 때 자동으로 실행되게 하는 등록(현재 사용자의 Run 레지스트리 값)</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SnapTranslate";

    private static string Command => $"\"{Application.ExecutablePath}\"";

    /// <summary>지금 이 실행 파일이 자동 실행으로 등록되어 있는지</summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value &&
                   value.Trim().Equals(Command, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            DebugLog.Error("자동 실행 확인", ex);
            return false;
        }
    }

    /// <summary>등록하거나 지운다. 실패하면 이유를 돌려준다(성공이면 null)</summary>
    public static string? Set(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, false);
            DebugLog.Write($"자동 실행 {(enabled ? "등록" : "해제")}");
            return null;
        }
        catch (Exception ex)
        {
            DebugLog.Error("자동 실행 설정", ex);
            return ex.Message;
        }
    }
}
