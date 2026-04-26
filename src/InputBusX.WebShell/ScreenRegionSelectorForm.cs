using System.Drawing;
using System.Windows.Forms;

namespace InputBusX.WebShell;

internal sealed class ScreenRegionSelectorForm : Form
{
    private readonly TaskCompletionSource<Rectangle?> _selectionSource = new();
    private readonly Rectangle _virtualBounds = SystemInformation.VirtualScreen;
    private Point _dragStart;
    private Rectangle _selection;
    private bool _dragging;

    public Task<Rectangle?> SelectionTask => _selectionSource.Task;

    public ScreenRegionSelectorForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.Black;
        Opacity = 0.35;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        Bounds = _virtualBounds;

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += OnKeyDown;
    }

    public static async Task<Rectangle?> SelectAsync(IWin32Window? owner = null)
    {
        using var selector = new ScreenRegionSelectorForm();
        if (owner is not null)
        {
            selector.Show(owner);
        }
        else
        {
            selector.Show();
        }

        selector.Activate();
        return await selector.SelectionTask.ConfigureAwait(true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_selection.Width <= 0 || _selection.Height <= 0)
        {
            DrawHint(e.Graphics);
            return;
        }

        using var clearBrush = new SolidBrush(Color.FromArgb(210, 0, 0, 0));
        using var borderPen = new Pen(Color.FromArgb(0, 245, 184), 2f);
        using var fillBrush = new SolidBrush(Color.FromArgb(32, 0, 245, 184));
        using var badgeBrush = new SolidBrush(Color.FromArgb(16, 20, 28));
        using var badgeBorder = new Pen(Color.FromArgb(52, 70, 92), 1f);
        using var textBrush = new SolidBrush(Color.FromArgb(0, 245, 184));

        e.Graphics.FillRectangle(clearBrush, ClientRectangle);
        e.Graphics.FillRectangle(fillBrush, _selection);
        e.Graphics.DrawRectangle(borderPen, _selection);

        var sizeText = $"{_selection.Width} x {_selection.Height} px";
        var badgeRect = new Rectangle(
            Math.Min(_selection.Right + 10, ClientSize.Width - 132),
            Math.Min(_selection.Bottom + 10, ClientSize.Height - 34),
            122,
            24);
        e.Graphics.FillRectangle(badgeBrush, badgeRect);
        e.Graphics.DrawRectangle(badgeBorder, badgeRect);
        e.Graphics.DrawString(sizeText, Font, textBrush, badgeRect.X + 8, badgeRect.Y + 4);

        DrawHint(e.Graphics);
    }

    private void DrawHint(Graphics graphics)
    {
        using var badgeBrush = new SolidBrush(Color.FromArgb(18, 24, 34));
        using var borderPen = new Pen(Color.FromArgb(42, 58, 77), 1f);
        using var textBrush = new SolidBrush(Color.FromArgb(225, 235, 248));
        using var muteBrush = new SolidBrush(Color.FromArgb(126, 147, 171));

        var rect = new Rectangle((ClientSize.Width / 2) - 230, 24, 460, 42);
        graphics.FillRectangle(badgeBrush, rect);
        graphics.DrawRectangle(borderPen, rect);
        graphics.DrawString("Drag to select the weapon-name region", Font, textBrush, rect.X + 14, rect.Y + 8);
        graphics.DrawString("ESC to cancel", Font, muteBrush, rect.Right - 108, rect.Y + 8);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Escape)
        {
            return;
        }

        _selectionSource.TrySetResult(null);
        Close();
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _dragStart = e.Location;
        _selection = Rectangle.Empty;
        Invalidate();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _selection = Normalize(_dragStart, e.Location);
        Invalidate();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _selection = Normalize(_dragStart, e.Location);

        if (_selection.Width < 8 || _selection.Height < 8)
        {
            _selectionSource.TrySetResult(null);
            Close();
            return;
        }

        var physical = new Rectangle(
            _virtualBounds.Left + _selection.Left,
            _virtualBounds.Top + _selection.Top,
            _selection.Width,
            _selection.Height);

        _selectionSource.TrySetResult(physical);
        Close();
    }

    private static Rectangle Normalize(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var width = Math.Abs(b.X - a.X);
        var height = Math.Abs(b.Y - a.Y);
        return new Rectangle(x, y, width, height);
    }
}
