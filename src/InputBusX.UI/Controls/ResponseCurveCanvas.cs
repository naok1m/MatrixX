using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace InputBusX.UI.Controls;

/// <summary>
/// Premium response curve visualizer — bright green curve with multi-pass glow halo,
/// fixed sample-point markers along the curve, and an interactive live marker that
/// follows either the bound CurrentInput value or the user's hover position.
/// </summary>
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
        AffectsRender<ResponseCurveCanvas>(DeadzoneProperty, AntiDeadzoneProperty,
                                           ExponentProperty, CurrentInputProperty);
    }

    public ResponseCurveCanvas()
    {
        PointerMoved   += OnPointerMoved;
        PointerEntered += OnPointerEntered;
        PointerExited  += OnPointerExited;
    }

    public double Deadzone     { get => GetValue(DeadzoneProperty);     set => SetValue(DeadzoneProperty, value); }
    public double AntiDeadzone { get => GetValue(AntiDeadzoneProperty); set => SetValue(AntiDeadzoneProperty, value); }
    public double Exponent     { get => GetValue(ExponentProperty);     set => SetValue(ExponentProperty, value); }
    public double CurrentInput { get => GetValue(CurrentInputProperty); set => SetValue(CurrentInputProperty, value); }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (Bounds.Width < 2) return;
        var pos = e.GetPosition(this);
        _hoverInput = Math.Clamp(pos.X / Bounds.Width, 0, 1);
        _hasHoverInput = true;
        InvalidateVisual();
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _hasHoverInput = true;
        InvalidateVisual();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _hasHoverInput = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w < 2 || h < 2) return;

        // ── Palette — pure dark background, very subtle grid, vivid green curve ──
        var bgBrush  = new SolidColorBrush(Color.Parse("#06080C"));
        var gridPen  = new Pen(new SolidColorBrush(Color.Parse("#10161F")), 1);
        var dzFill   = new SolidColorBrush(Color.Parse("#0A0E16"));

        var greenColor = Color.Parse("#00FF95");
        var greenBrush = new SolidColorBrush(greenColor);

        // Three-pass glow — wide soft halo, mid halo, tight halo, then crisp curve
        var glowFarPen   = new Pen(new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0xFF, 0x95)), 18,
                               lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var glowMidPen   = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xFF, 0x95)), 10,
                               lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var glowNearPen  = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0xFF, 0x95)), 5,
                               lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var curvePen     = new Pen(greenBrush, 2.5,
                               lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        // Marker brushes — outer soft halo + mid halo + bright core
        var markerHaloOuter = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0xFF, 0x95));
        var markerHaloInner = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0xFF, 0x95));

        // ── Background ──
        ctx.FillRectangle(bgBrush, new Rect(0, 0, w, h));

        // ── Subtle 6×6 grid ──
        for (int i = 1; i < 6; i++)
        {
            double gx = w * i / 6.0;
            double gy = h * i / 6.0;
            ctx.DrawLine(gridPen, new Point(gx, 0), new Point(gx, h));
            ctx.DrawLine(gridPen, new Point(0, gy), new Point(w, gy));
        }

        double dz  = Math.Clamp(Deadzone, 0, 0.99);
        double adz = Math.Clamp(AntiDeadzone, 0, 1);
        double exp = Math.Max(Exponent, 0.01);

        // ── Deadzone shaded region ──
        if (dz > 0)
        {
            double dzX = dz * w;
            ctx.FillRectangle(dzFill, new Rect(0, 0, dzX, h));
        }

        // ── Build curve geometry ──
        const int steps = 240;
        var geo = BuildCurveGeometry(w, h, dz, adz, exp, steps);

        // ── Multi-pass glow → crisp curve on top ──
        ctx.DrawGeometry(null, glowFarPen,  geo);
        ctx.DrawGeometry(null, glowMidPen,  geo);
        ctx.DrawGeometry(null, glowNearPen, geo);
        ctx.DrawGeometry(null, curvePen,    geo);

        // ── Fixed sample-point markers at 0.2 / 0.4 / 0.6 / 0.8 / 1.0 ──
        double[] markerInputs = [0.2, 0.4, 0.6, 0.8, 1.0];
        foreach (var input in markerInputs)
        {
            double output = ComputeOutput(input, dz, adz, exp);
            var pt = new Point(input * w, (1.0 - output) * h);

            ctx.DrawEllipse(markerHaloOuter, null, pt, 12, 12);
            ctx.DrawEllipse(markerHaloInner, null, pt, 7, 7);
            ctx.DrawEllipse(greenBrush, null, pt, 4, 4);
        }

        // ── Live marker — bound CurrentInput takes priority, falls back to hover ──
        double liveInput = double.NaN;
        if (!double.IsNaN(CurrentInput))
            liveInput = Math.Clamp(CurrentInput, 0, 1);
        else if (_hasHoverInput)
            liveInput = _hoverInput;

        if (!double.IsNaN(liveInput))
        {
            double liveOutput = ComputeOutput(liveInput, dz, adz, exp);
            var livePt = new Point(liveInput * w, (1.0 - liveOutput) * h);

            // Vertical guide line — subtle dashed
            var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0xFF, 0x95)), 1,
                               dashStyle: new DashStyle([3, 4], 0));
            ctx.DrawLine(guidePen, new Point(livePt.X, 0), new Point(livePt.X, h));

            // Pulsing live marker — larger, brighter than fixed markers
            ctx.DrawEllipse(markerHaloOuter, null, livePt, 16, 16);
            ctx.DrawEllipse(markerHaloInner, null, livePt, 10, 10);
            ctx.DrawEllipse(greenBrush, null, livePt, 5, 5);
            ctx.DrawEllipse(new SolidColorBrush(Colors.White), null, livePt, 2, 2);
        }
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
