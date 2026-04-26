using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace InputBusX.UI.Controls;

public sealed class ResponseCurveCanvas : Control
{
    public static readonly StyledProperty<double> DeadzoneProperty =
        AvaloniaProperty.Register<ResponseCurveCanvas, double>(nameof(Deadzone));

    public static readonly StyledProperty<double> AntiDeadzoneProperty =
        AvaloniaProperty.Register<ResponseCurveCanvas, double>(nameof(AntiDeadzone));

    public static readonly StyledProperty<double> ExponentProperty =
        AvaloniaProperty.Register<ResponseCurveCanvas, double>(nameof(Exponent), defaultValue: 1.0);

    public static readonly StyledProperty<double> CurrentInputProperty =
        AvaloniaProperty.Register<ResponseCurveCanvas, double>(nameof(CurrentInput), defaultValue: double.NaN);

    private bool _hasHoverInput;
    private double _hoverInput;

    static ResponseCurveCanvas()
    {
        AffectsRender<ResponseCurveCanvas>(DeadzoneProperty, AntiDeadzoneProperty, ExponentProperty, CurrentInputProperty);
    }

    public ResponseCurveCanvas()
    {
        PointerMoved += OnPointerMoved;
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
    }

    public double Deadzone
    {
        get => GetValue(DeadzoneProperty);
        set => SetValue(DeadzoneProperty, value);
    }

    public double AntiDeadzone
    {
        get => GetValue(AntiDeadzoneProperty);
        set => SetValue(AntiDeadzoneProperty, value);
    }

    public double Exponent
    {
        get => GetValue(ExponentProperty);
        set => SetValue(ExponentProperty, value);
    }

    public double CurrentInput
    {
        get => GetValue(CurrentInputProperty);
        set => SetValue(CurrentInputProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w < 2 || h < 2) return;

        // ── Palette ──────────────────────────────────────────────────────────
        var bgBrush = new SolidColorBrush(Color.Parse("#080B11"));

        // Very subtle grid — barely visible, doesn't compete with curve
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#111928")), 1);

        // Deadzone shading
        var dzFill    = new SolidColorBrush(Color.Parse("#0C1018"));
        var dzEdgePen = new Pen(new SolidColorBrush(Color.Parse("#1F2533")), 1,
                            dashStyle: new DashStyle([3, 4], 0));

        // Reference 1:1 line — dotted gray, subtle
        var linearPen = new Pen(new SolidColorBrush(Color.Parse("#5A667E")), 1,
                            dashStyle: new DashStyle([5, 5], 0));

        // Curve glow passes — wide outer, medium inner
        var glowOuterPen = new Pen(new SolidColorBrush(Color.Parse("#1A00B7FF")), 14,
                               lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var glowInnerPen = new Pen(new SolidColorBrush(Color.Parse("#3600B7FF")), 7,
                               lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        // Curve — cyan gradient stroke
        var curveBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#00FF95"), 0.0),
                new GradientStop(Color.Parse("#00D4E8"), 0.5),
                new GradientStop(Color.Parse("#00B7FF"), 1.0),
            ]
        };
        var curvePen = new Pen(curveBrush, 3.2,
                           lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        var markerGlowBrush = new SolidColorBrush(Color.Parse("#5000B7FF"));
        var markerBrush = new SolidColorBrush(Color.Parse("#00B7FF"));
        var markerRingPen = new Pen(new SolidColorBrush(Color.Parse("#B000FF95")), 1.5);

        var borderPen = new Pen(new SolidColorBrush(Color.Parse("#1F2533")), 1);

        // ── Background ───────────────────────────────────────────────────────
        ctx.FillRectangle(bgBrush, new Rect(0, 0, w, h));

        // ── Subtle grid (5×5) ────────────────────────────────────────────────
        for (int i = 1; i < 5; i++)
        {
            double gx = w * i / 5.0;
            double gy = h * i / 5.0;
            ctx.DrawLine(gridPen, new Point(gx, 0), new Point(gx, h));
            ctx.DrawLine(gridPen, new Point(0, gy), new Point(w, gy));
        }

        double dz  = Math.Clamp(Deadzone, 0, 0.99);
        double adz = Math.Clamp(AntiDeadzone, 0, 1);
        double exp = Math.Max(Exponent, 0.01);

        // ── Deadzone band ────────────────────────────────────────────────────
        if (dz > 0)
        {
            double dzX = dz * w;
            ctx.FillRectangle(dzFill, new Rect(0, 0, dzX, h));
            ctx.DrawLine(dzEdgePen, new Point(dzX, 0), new Point(dzX, h));
        }

        // ── 1:1 linear reference (dotted) ────────────────────────────────────
        ctx.DrawLine(linearPen, new Point(0, h), new Point(w, 0));

        // ── Response curve — multi-pass glow + crisp line ────────────────────
        const int steps = 200;
        var geo = BuildCurveGeometry(w, h, dz, adz, exp, steps);

        ctx.DrawGeometry(null, glowOuterPen, geo);
        ctx.DrawGeometry(null, glowInnerPen, geo);
        ctx.DrawGeometry(null, curvePen, geo);

        // Live marker follows either bound input or user hover to make the curve interactive.
        double markerInput = double.NaN;
        if (!double.IsNaN(CurrentInput))
        {
            markerInput = Math.Clamp(CurrentInput, 0, 1);
        }
        else if (_hasHoverInput)
        {
            markerInput = Math.Clamp(_hoverInput, 0, 1);
        }

        if (!double.IsNaN(markerInput))
        {
            double markerOutput = ComputeOutput(markerInput, dz, adz, exp);
            var markerPoint = new Point(markerInput * w, (1.0 - markerOutput) * h);

            ctx.FillEllipse(markerGlowBrush, markerPoint, 9, 9);
            ctx.FillEllipse(markerBrush, markerPoint, 4.5, 4.5);
            ctx.DrawEllipse(null, markerRingPen, markerPoint, 6, 6);
        }

        // ── Border ───────────────────────────────────────────────────────────
        ctx.DrawRectangle(null, borderPen, new Rect(0.5, 0.5, w - 1, h - 1));
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        UpdateHoverInput(e);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        UpdateHoverInput(e);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _hasHoverInput = false;
        InvalidateVisual();
    }

    private void UpdateHoverInput(PointerEventArgs e)
    {
        if (Bounds.Width <= 0)
        {
            _hasHoverInput = false;
            return;
        }

        var point = e.GetPosition(this);
        _hoverInput = Math.Clamp(point.X / Bounds.Width, 0, 1);
        _hasHoverInput = true;
        InvalidateVisual();
    }

    private static StreamGeometry BuildCurveGeometry(
        double w, double h, double dz, double adz, double exp, int steps)
    {
        var geo = new StreamGeometry();
        using var gctx = geo.Open();

        bool started = false;
        for (int i = 0; i <= steps; i++)
        {
            double input  = i / (double)steps;
            double output = ComputeOutput(input, dz, adz, exp);
            var pt = new Point(input * w, (1.0 - output) * h);

            if (!started) { gctx.BeginFigure(pt, false); started = true; }
            else           gctx.LineTo(pt);
        }

        gctx.EndFigure(false);
        return geo;
    }

    private static double ComputeOutput(double input, double deadzone, double antiDeadzone, double exponent)
    {
        if (input < deadzone) return 0.0;
        double remapped = (input - deadzone) / (1.0 - deadzone);
        remapped = Math.Min(remapped, 1.0);
        if (antiDeadzone > 0)
            remapped = antiDeadzone + remapped * (1.0 - antiDeadzone);
        if (Math.Abs(exponent - 1.0) > 0.001)
            remapped = Math.Pow(remapped, exponent);
        return remapped;
    }
}
