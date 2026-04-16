using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace InputBusX.UI.Views;

public partial class ScreenRegionSelectorWindow : Window
{
    // ── Result ────────────────────────────────────────────────────────────
    private readonly TaskCompletionSource<PixelRect?> _tcs = new();
    public Task<PixelRect?> SelectionTask => _tcs.Task;

    // ── Drag state ────────────────────────────────────────────────────────
    private Point _startPoint;
    private bool _isDragging;

    public ScreenRegionSelectorWindow()
    {
        InitializeComponent();

        RootCanvas.PointerPressed  += OnPointerPressed;
        RootCanvas.PointerMoved    += OnPointerMoved;
        RootCanvas.PointerReleased += OnPointerReleased;
        KeyDown                    += OnKeyDown;
    }

    // ── Window opened → size to cover all monitors ────────────────────────
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var screens = Screens.All;
        if (screens.Count == 0) return;

        var minX = screens.Min(s => s.Bounds.X);
        var minY = screens.Min(s => s.Bounds.Y);
        var maxX = screens.Max(s => s.Bounds.Right);
        var maxY = screens.Max(s => s.Bounds.Bottom);

        var scale = Screens.Primary?.Scaling ?? 1.0;

        Position = new PixelPoint(minX, minY);
        Width    = (maxX - minX) / scale;
        Height   = (maxY - minY) / scale;
    }

    // ── Input handlers ────────────────────────────────────────────────────
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _tcs.TrySetResult(null);
            Close();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(RootCanvas).Properties.IsLeftButtonPressed) return;

        _isDragging  = true;
        _startPoint  = e.GetPosition(RootCanvas);

        SelectionRect.IsVisible = true;
        SetCornerVisibility(true);

        Canvas.SetLeft(SelectionRect, _startPoint.X);
        Canvas.SetTop(SelectionRect,  _startPoint.Y);
        SelectionRect.Width  = 0;
        SelectionRect.Height = 0;

        e.Pointer.Capture(RootCanvas);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;

        var current = e.GetPosition(RootCanvas);
        var rect    = NormalizedRect(_startPoint, current);

        // Move selection rectangle
        Canvas.SetLeft(SelectionRect, rect.X);
        Canvas.SetTop(SelectionRect,  rect.Y);
        SelectionRect.Width  = rect.Width;
        SelectionRect.Height = rect.Height;

        UpdateCorners(rect);
        UpdateSizeBadge(rect, current);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        var end  = e.GetPosition(RootCanvas);
        var rect = NormalizedRect(_startPoint, end);

        // Ignore tiny accidental clicks
        if (rect.Width < 8 || rect.Height < 8)
        {
            _tcs.TrySetResult(null);
            Close();
            return;
        }

        // Convert logical Avalonia coordinates → physical screen pixels
        var scale  = Screens.Primary?.Scaling ?? 1.0;
        var winPos = Position;

        var physX = (int)(rect.X * scale) + winPos.X;
        var physY = (int)(rect.Y * scale) + winPos.Y;
        var physW = (int)(rect.Width  * scale);
        var physH = (int)(rect.Height * scale);

        _tcs.TrySetResult(new PixelRect(physX, physY, physW, physH));
        Close();
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static Rect NormalizedRect(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    private void SetCornerVisibility(bool visible)
    {
        CornerTL.IsVisible  = visible; CornerTLv.IsVisible = visible;
        CornerTR.IsVisible  = visible; CornerTRv.IsVisible = visible;
        CornerBL.IsVisible  = visible; CornerBLv.IsVisible = visible;
        CornerBR.IsVisible  = visible; CornerBRv.IsVisible = visible;
        SizeBadge.IsVisible = visible;
    }

    private void UpdateCorners(Rect r)
    {
        // TL horizontal
        Canvas.SetLeft(CornerTL, r.X);      Canvas.SetTop(CornerTL, r.Y);
        // TL vertical
        Canvas.SetLeft(CornerTLv, r.X);     Canvas.SetTop(CornerTLv, r.Y);

        // TR horizontal (right side, aligned right)
        Canvas.SetLeft(CornerTR,  r.Right - 12); Canvas.SetTop(CornerTR,  r.Y);
        // TR vertical
        Canvas.SetLeft(CornerTRv, r.Right - 2);  Canvas.SetTop(CornerTRv, r.Y);

        // BL horizontal
        Canvas.SetLeft(CornerBL,  r.X);          Canvas.SetTop(CornerBL,  r.Bottom - 2);
        // BL vertical
        Canvas.SetLeft(CornerBLv, r.X);          Canvas.SetTop(CornerBLv, r.Bottom - 12);

        // BR horizontal
        Canvas.SetLeft(CornerBR,  r.Right - 12); Canvas.SetTop(CornerBR,  r.Bottom - 2);
        // BR vertical
        Canvas.SetLeft(CornerBRv, r.Right - 2);  Canvas.SetTop(CornerBRv, r.Bottom - 12);
    }

    private void UpdateSizeBadge(Rect r, Point cursor)
    {
        var scale  = Screens.Primary?.Scaling ?? 1.0;
        var physW  = (int)(r.Width  * scale);
        var physH  = (int)(r.Height * scale);

        SizeText.Text = $"{physW} × {physH} px";

        // Position badge below and to the right of cursor, keep on screen
        var bx = cursor.X + 12;
        var by = cursor.Y + 18;
        if (bx + 120 > Bounds.Width)  bx = cursor.X - 130;
        if (by + 30  > Bounds.Height) by = cursor.Y - 36;

        Canvas.SetLeft(SizeBadge, bx);
        Canvas.SetTop(SizeBadge,  by);
    }
}
