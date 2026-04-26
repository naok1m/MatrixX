using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace InputBusX.UI.Controls;

/// <summary>
/// Custom control that renders the stick response curve — deadzone band, optional
/// anti-deadzone jump, and the exponent-shaped output curve — in real time as the
/// user drags the filter sliders.
/// </summary>
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

        var bgBrush      = new SolidColorBrush(Color.Parse("#0E1118"));
        var gridPen      = new Pen(new SolidColorBrush(Color.Parse("#151C28")), 1);
        var dzFill       = new SolidColorBrush(Color.Parse("#111720"));
        var dzLinePen    = new Pen(new SolidColorBrush(Color.Parse("#252B3F")), 1);
        var linearPen    = new Pen(new SolidColorBrush(Color.Parse("#1D2235")), 1);
        var curvePen     = new Pen(new SolidColorBrush(Color.Parse("#00FF9C")), 2,
                               lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var glowPen      = new Pen(new SolidColorBrush(Color.Parse("#2000FF9C")), 6,
                               lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var borderPen    = new Pen(new SolidColorBrush(Color.Parse("#1D2235")), 1);

        // Background
        ctx.FillRectangle(bgBrush, new Rect(0, 0, w, h));

        // Grid (4 divisions)
        for (int i = 1; i < 4; i++)
        {
            double gx = w * i / 4.0;
            double gy = h * i / 4.0;
            ctx.DrawLine(gridPen, new Point(gx, 0), new Point(gx, h));
            ctx.DrawLine(gridPen, new Point(0, gy), new Point(w, gy));
        }

        double dz  = Math.Clamp(Deadzone, 0, 0.99);
        double adz = Math.Clamp(AntiDeadzone, 0, 1);
        double exp = Math.Max(Exponent, 0.01);

        // Deadzone band (shaded region on left)
        if (dz > 0)
        {
            double dzX = dz * w;
            ctx.FillRectangle(dzFill, new Rect(0, 0, dzX, h));
            ctx.DrawLine(dzLinePen, new Point(dzX, 0), new Point(dzX, h));
        }

        // 1:1 linear reference
        ctx.DrawLine(linearPen, new Point(0, h), new Point(w, 0));

        // Response curve — build StreamGeometry for a smooth polyline
        const int steps = 160;
        var geo = BuildCurveGeometry(w, h, dz, adz, exp, steps);

        // Draw glow pass first, then crisp line on top
        ctx.DrawGeometry(null, glowPen, geo);
        ctx.DrawGeometry(null, curvePen, geo);

        // Border
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

    // Mirrors CompositeInputFilter.ApplyStickFilters magnitude path exactly.
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
