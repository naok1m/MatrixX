using InputBusX.Domain.Enums;

namespace InputBusX.Domain.Entities;

/// <summary>
/// Pure-data description of a parametric stick motion. All radii and
/// amplitudes are expressed in normalised stick space <c>[-1, 1]</c> and
/// converted to <see cref="short"/> at sampling time so the same script can
/// be reused across stick targets and intensities without rescaling.
///
/// ── Field roles by <see cref="ShapeKind"/> ────────────────────────────────
/// <list type="bullet">
///   <item><c>Flick</c>: <see cref="DirectionDeg"/> and <see cref="AmplitudeNorm"/>
///   describe a one-shot displacement; <see cref="DurationMs"/> + <see cref="Easing"/>
///   shape its time profile; <see cref="PeriodMs"/> is ignored.</item>
///   <item><c>Circle</c>: <see cref="RadiusXNorm"/> sets the radius; <see cref="PeriodMs"/>
///   sets one full revolution; <see cref="Clockwise"/> flips the direction.</item>
///   <item><c>HorizontalOval</c> / <c>VerticalOval</c>: <see cref="RadiusXNorm"/>
///   and <see cref="RadiusYNorm"/> independently control the two semi-axes.</item>
///   <item><c>DiagonalOval</c>: same as the ovals plus <see cref="RotationDeg"/>
///   to rotate the whole ellipse around its centre.</item>
/// </list>
///
/// <see cref="DurationMs"/> == 0 means "run indefinitely while the trigger is held"
/// for orbital shapes; the flick uses <see cref="DurationMs"/> directly as its
/// single-shot span.
/// </summary>
public sealed class MotionScript
{
    public ShapeKind Shape { get; set; } = ShapeKind.Flick;
    public StickTargetKind Target { get; set; } = StickTargetKind.Left;

    /// <summary>Horizontal semi-axis / radius (normalised, [-1..1]).</summary>
    public double RadiusXNorm { get; set; } = 0.35;

    /// <summary>Vertical semi-axis / radius (normalised, [-1..1]).</summary>
    public double RadiusYNorm { get; set; } = 0.35;

    /// <summary>Rotation of the motion envelope in degrees (used by DiagonalOval).</summary>
    public double RotationDeg { get; set; } = 0;

    /// <summary>One full shape revolution in milliseconds (orbital shapes).</summary>
    public double PeriodMs { get; set; } = 400;

    /// <summary>Total run-time in milliseconds. 0 = indefinite (orbital only).</summary>
    public double DurationMs { get; set; } = 140;

    /// <summary>Flick direction in degrees. 0 = +X (right), 90 = +Y (up).</summary>
    public double DirectionDeg { get; set; } = 90;

    /// <summary>Single-shot flick displacement, normalised.</summary>
    public double AmplitudeNorm { get; set; } = 0.55;

    /// <summary>Starting phase for orbital shapes, in degrees.</summary>
    public double StartPhaseDeg { get; set; } = 0;

    public bool Clockwise { get; set; } = true;

    public EasingKind Easing { get; set; } = EasingKind.EaseOutCubic;

    /// <summary>Final multiplier stacked on top of the macro / weapon intensity.</summary>
    public double IntensityMul { get; set; } = 1.0;

    /// <summary>
    /// When true, the sampled motion is added to the live stick value instead of
    /// overriding it. Additive mode preserves player input (feels like "assist"),
    /// override mode forces the stick (feels like "auto aim").
    /// </summary>
    public bool Additive { get; set; } = true;

    public MotionScript Clone() => new()
    {
        Shape = Shape,
        Target = Target,
        RadiusXNorm = RadiusXNorm,
        RadiusYNorm = RadiusYNorm,
        RotationDeg = RotationDeg,
        PeriodMs = PeriodMs,
        DurationMs = DurationMs,
        DirectionDeg = DirectionDeg,
        AmplitudeNorm = AmplitudeNorm,
        StartPhaseDeg = StartPhaseDeg,
        Clockwise = Clockwise,
        Easing = Easing,
        IntensityMul = IntensityMul,
        Additive = Additive,
    };
}
