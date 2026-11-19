using System.Drawing.Drawing2D;

namespace SnapTranslate;

/// <summary>
/// 화면 전체를 덮고 마우스 드래그로 사각 영역을 고르게 하는 창.
/// keepFocus 로 만들면 창이 포커스를 가져가지 않는다 — 게임 등 원래 쓰던 프로그램이 포커스를 잃지 않게 하기 위함.
/// 그 경우 키 입력을 받지 못하므로 Esc 취소는 밖에서 전역 단축키로 걸어 <see cref="Cancel"/> 을 호출한다.
/// </summary>
internal sealed class OverlayForm : Form
{
    private const int WM_DPICHANGED = 0x02E0;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly Bitmap _shot;   // 원본 화면 (소유권은 호출자)
    private readonly Bitmap _dim;    // 어둡게 만든 사본
    private readonly bool _keepFocus;
    private readonly TaskCompletionSource<Rectangle?> _done = new();
    private Point _start;
    private bool _dragging;
    private Rectangle _selection;

    public OverlayForm(Bitmap shot, Rectangle virtualScreen, bool keepFocus)
    {
        _shot = shot;
        _keepFocus = keepFocus;
        _dim = new Bitmap(shot);
        using (var g = Graphics.FromImage(_dim))
        using (var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            g.FillRectangle(shade, 0, 0, _dim.Width, _dim.Height);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = virtualScreen;
        ShowInTaskbar = false;
        // TopMost 속성은 창을 띄울 때 포커스를 가져오므로, 포커스 유지 모드에서는 CreateParams 의 확장 스타일로 맨 위에 둔다
        if (!keepFocus) TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
    }

    protected override bool ShowWithoutActivation => _keepFocus;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (_keepFocus) cp.ExStyle |= WS_EX_TOPMOST | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>창을 띄우고 선택이 끝날 때까지 기다린다. 취소하면 null, 고르면 캡처 그림 기준 픽셀 좌표</summary>
    public Task<Rectangle?> PickAsync()
    {
        Show();
        if (!_keepFocus) Activate();
        return _done.Task;
    }

    /// <summary>선택을 취소한다(Esc)</summary>
    public void Cancel() => Finish(false);

    private void Finish(bool ok)
    {
        if (_done.Task.IsCompleted) return;
        _done.SetResult(ok ? _selection : null);
        Hide();
    }

    protected override void WndProc(ref Message m)
    {
        // 그림과 창이 1:1 로 맞아야 하므로 DPI 변경에 따른 자동 크기 조정을 막는다
        if (m.Msg == WM_DPICHANGED) return;
        base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 배경은 OnPaint 에서 그림으로 다 덮으므로 지우지 않는다(깜박임 방지)
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_dim, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);

        if (_selection.Width > 0 && _selection.Height > 0)
        {
            // 선택 영역만 원래 밝기로 보여 준다
            g.DrawImage(_shot, _selection, _selection, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.FromArgb(0, 174, 255), 2);
            g.DrawRectangle(pen, _selection);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            Finish(false);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _start = e.Location;
        UpdateSelection(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) UpdateSelection(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left) return;
        _dragging = false;
        UpdateSelection(e.Location);
        Finish(true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Finish(false);
        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // 밖에서 창이 닫혀도 기다리는 쪽이 멈춰 있지 않게 한다
        _done.TrySetResult(null);
        base.OnFormClosed(e);
    }

    private void UpdateSelection(Point current)
    {
        var old = _selection;
        var rect = Rectangle.FromLTRB(
            Math.Min(_start.X, current.X), Math.Min(_start.Y, current.Y),
            Math.Max(_start.X, current.X), Math.Max(_start.Y, current.Y));
        rect.Intersect(new Rectangle(0, 0, _shot.Width, _shot.Height));
        _selection = rect;

        // 바뀐 부분만 다시 그린다
        var dirty = Rectangle.Union(old, rect);
        dirty.Inflate(4, 4);
        Invalidate(dirty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _dim.Dispose();
        base.Dispose(disposing);
    }
}
