using InputBusX.Domain.Enums;

namespace InputBusX.Domain.Entities;

/// <summary>
/// Configuration for the distance-adaptive Head Assist macro. Three
/// <see cref="MotionScript"/> presets (short / medium / long range) are
/// sampled by the motion engine; a runtime estimator picks which preset
/// fires based on the configured <see cref="DistanceSource"/>.
///
/// ── Distance estimation ───────────────────────────────────────────────────
/// <list type="bullet">
///   <item><c>TriggerHoldTime</c>: tap (close) → burst (medium) → sustained (long).
///   Uses <see cref="ShortHoldMsMax"/> and <see cref="MediumHoldMsMax"/>.</item>
///   <item><c>AimStickDeflection</c>: magnitude of the aim stick input at the
///   moment of activation. Large flicks imply long-range tracking. Uses
///   <see cref="DeflectionShortMax"/> and <see cref="DeflectionMediumMax"/>.</item>
///   <item><c>RecoilMagnitude</c>: derived from the active weapon's compensation
///   vector — heavier recoil biases toward longer range.</item>
///   <item><c>Manual</c>: <see cref="CycleButton"/> cycles the level explicitly.</item>
///   <item><c>Auto</c>: weighted fusion of the three auto signals.</item>
/// </list>
/// </summary>
public sealed class HeadAssistConfig
{
    public MotionScript ShortRange  { get; set; } = new()
    {
        Shape = ShapeKind.Flick,
        Target = StickTargetKind.Right,
        DirectionDeg = 90,
        AmplitudeNorm = 0.45,
        DurationMs = 90,
        Easing = EasingKind.EaseOutCubic,
    };

    public MotionScript MediumRange { get; set; } = new()
    {
        Shape = ShapeKind.Flick,
        Target = StickTargetKind.Right,
        DirectionDeg = 90,
        AmplitudeNorm = 0.70,
        DurationMs = 140,
        Easing = EasingKind.EaseOutCubic,
    };

    public MotionScript LongRange   { get; set; } = new()
    {
        Shape = ShapeKind.Flick,
        Target = StickTargetKind.Right,
        DirectionDeg = 90,
        AmplitudeNorm = 0.90,
        DurationMs = 220,
        Easing = EasingKind.EaseOutBack,
    };

    public DistanceSource DistanceSource { get; set; } = DistanceSource.Auto;

    // ── TriggerHoldTime estimator ────────────────────────────────────────
    public double ShortHoldMsMax  { get; set; } = 150;
    public double MediumHoldMsMax { get; set; } = 500;

    // ── AimStickDeflection estimator ─────────────────────────────────────
    /// <summary>Normalised magnitude (0..1) below which the shot is considered close-range.</summary>
    public double DeflectionShortMax  { get; set; } = 0.30;

    /// <summary>Normalised magnitude (0..1) below which the shot is considered medium-range.</summary>
    public double DeflectionMediumMax { get; set; } = 0.65;

    // ── RecoilMagnitude estimator ────────────────────────────────────────
    public double RecoilShortMax  { get; set; } = 2500;
    public double RecoilMediumMax { get; set; } = 6000;

    // ── Auto fusion weights ──────────────────────────────────────────────
    public double WeightTrigger    { get; set; } = 1.0;
    public double WeightDeflection { get; set; } = 1.0;
    public double WeightRecoil     { get; set; } = 0.5;

    // ── Manual mode ──────────────────────────────────────────────────────
    public GamepadButton? CycleButton { get; set; }

    /// <summary>
    /// Minimum interval between successive flicks while the trigger is held.
    /// Prevents the macro from stacking on itself during sustained fire.
    /// </summary>
    public int ReFireCooldownMs { get; set; } = 250;

    /// <summary>Minimum input time before Head Assist is allowed to fire at all — stops accidental taps.</summary>
    public int MinTriggerHoldMs { get; set; } = 20;

    /// <summary>When true, the assist also fires on the FIRST frame the trigger goes down (ADS snap).</summary>
    public bool FireOnPress { get; set; } = true;
}
