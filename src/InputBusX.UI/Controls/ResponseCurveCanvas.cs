using Avalonia;
using Avalonia.Controls;
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

    static ResponseCurveCanvas()
    {
        AffectsRender<ResponseCurveCanvas>(DeadzoneProperty, AntiDeadzoneProperty, ExponentProperty);
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

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w < 2 || h < 2) return;

        // ── Palette ──────────────────────────────────────────────────────────
        var bgBrush = new SolidColorBrush(Color.Parse("#080B11"));

        // Very subtle grid — barely visible, doesn't compete with curve
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#0E1520")), 1);

        // Deadzone shading
        var dzFill    = new SolidColorBrush(Color.Parse("#0C1018"));
        var dzEdgePen = new Pen(new SolidColorBrush(Color.Parse("#1F2533")), 1,
                            dashStyle: new DashStyle([3, 4], 0));

        // Reference 1:1 line — dotted gray, subtle
        var linearPen = new Pen(new SolidColorBrush(Color.Parse("#2A3345")), 1,
                            dashStyle: new DashStyle([5, 5], 0));

        // Curve glow passes — wide outer, medium inner
        var glowOuterPen = new Pen(new SolidColorBrush(Color.Parse("#1800B7FF")), 12,
                               lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var glowInnerPen = new Pen(new SolidColorBrush(Color.Parse("#3000B7FF")), 6,
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
        var curvePen = new Pen(curveBrush, 2.5,
                           lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

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

        // ── Border ───────────────────────────────────────────────────────────
        ctx.DrawRectangle(null, borderPen, new Rect(0.5, 0.5, w - 1, h - 1));
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
