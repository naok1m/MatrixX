using InputBusX.Domain.Enums;

namespace InputBusX.Domain.Entities;

/// <summary>
/// Configuration for the Tracking Assist macro — a progressive aim-assist
/// buff that adds small orbital stick motion while the player tracks an enemy.
///
/// Unlike the basic AimAssistBuff (static left-right oscillation), this macro:
/// <list type="bullet">
///   <item>Reads the aim stick direction and magnitude in real time.</item>
///   <item>Adds a configurable orbital pattern (circle/oval) <b>around</b> the
///   player's current stick position (always additive).</item>
///   <item>Scales the orbit radius with stick deflection — bigger deflection
///   (tracking a moving target) = larger orbit = more aim-assist "pull".</item>
///   <item>Has a minimum deflection threshold so stationary sticks don't jitter.</item>
/// </list>
///
/// The net effect feels like the crosshair "magnetises" to the target while
/// the player tracks — Cronus Zen-tier aim-assist boosting.
/// </summary>
public sealed class TrackingAssistConfig
{
    /// <summary>Orbital shape used for the tracking wobble.</summary>
    public ShapeKind Shape { get; set; } = ShapeKind.Circle;

    /// <summary>Which stick receives the orbital overlay.</summary>
    public StickTargetKind Target { get; set; } = StickTargetKind.Right;

    /// <summary>Base orbit radius (normalised 0..1). Scaled by stick deflection at runtime.</summary>
    public double BaseRadiusNorm { get; set; } = 0.08;

    /// <summary>Maximum orbit radius when stick is at full deflection.</summary>
    public double MaxRadiusNorm { get; set; } = 0.25;

    /// <summary>One full orbit revolution in milliseconds.</summary>
    public double PeriodMs { get; set; } = 120;

    /// <summary>Orbital direction (true = clockwise).</summary>
    public bool Clockwise { get; set; } = true;

    /// <summary>
    /// Minimum normalised stick deflection (0..1) before the tracking overlay activates.
    /// Below this the macro stays silent — prevents jitter when the stick is idle.
    /// </summary>
    public double DeflectionThreshold { get; set; } = 0.10;

    /// <summary>
    /// How much stick magnitude scales the orbit radius.
    /// <c>radius = lerp(BaseRadiusNorm, MaxRadiusNorm, pow(magnitude, ScaleCurve))</c>
    /// A value &lt; 1 biases toward larger radii earlier; &gt; 1 biases toward smaller.
    /// </summary>
    public double ScaleCurve { get; set; } = 0.7;

    /// <summary>Easing applied to the orbital phase (controls "smoothness" of the wobble).</summary>
    public EasingKind Easing { get; set; } = EasingKind.EaseInOutSine;

    /// <summary>Final intensity multiplier stacked on top of the computed radius.</summary>
    public double IntensityMul { get; set; } = 1.0;

    /// <summary>
    /// When true, the orbital motion runs continuously at <see cref="BaseRadiusNorm"/>
    /// without requiring any stick deflection. The orbit still scales up if the player
    /// moves the stick, but the baseline is always active — no trigger or aim input needed.
    /// Combined with <c>TriggerSource.None</c> and no ActivationButton, this turns the
    /// macro into a permanent passive aim-assist overlay on the camera stick.
    /// </summary>
    public bool FreeOrbit { get; set; }
}
