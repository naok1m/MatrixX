using InputBusX.Domain.Enums;

namespace InputBusX.Domain.Entities;

/// <summary>
/// Progressive no-recoil that adapts compensation over time, split into three
/// phases (start / mid / end) that map to the magazine lifecycle. Each phase has
/// independent X/Y compensation values; transitions use a configurable easing
/// curve so the crosshair doesn't "snap" between zones.
///
/// ── How it works ──────────────────────────────────────────────────────────────
/// <list type="number">
///   <item>When the fire trigger goes down, a timer starts.</item>
///   <item><c>progress = elapsedMs / FullMagDurationMs</c> (clamped 0–1).</item>
///   <item>The magazine is split into 3 zones by ammo count (roughly thirds
///   unless custom splits are set). Each zone maps to a time slice via the
///   constant fire-rate assumption.</item>
///   <item>Compensation X/Y is interpolated between adjacent phases using
///   <see cref="PhaseEasing"/> (Smoothstep recommended).</item>
///   <item>Gaussian-like noise scaled by <see cref="NoiseFactor"/> is added
///   every frame — anti-pattern detection.</item>
///   <item><see cref="SensitivityScale"/> globally multiplies timing <b>and</b>
///   inversely scales values: high-sens players need less compensation applied
///   faster.</item>
/// </list>
///
/// When the weapon-detection system identifies a weapon that has its own
/// <see cref="ProgressiveRecoilConfig"/> on the <see cref="WeaponProfile"/>,
/// those values take precedence over this macro-level config.
/// </summary>
public sealed class ProgressiveRecoilConfig
{
    // ── Magazine / timing ────────────────────────────────────────────────
    /// <summary>Total rounds in the magazine (used to split phases).</summary>
    public int TotalAmmo { get; set; } = 60;

    /// <summary>Time in ms to empty the full magazine at max fire rate.</summary>
    public double FullMagDurationMs { get; set; } = 2500;

    // ── Phase 1 — Start (first ~1/3 of mag) ─────────────────────────────
    public int StartCompX { get; set; } = 0;
    public int StartCompY { get; set; } = -3000;

    // ── Phase 2 — Mid (middle ~1/3) ─────────────────────────────────────
    public int MidCompX { get; set; } = 0;
    public int MidCompY { get; set; } = -5000;

    // ── Phase 3 — End (last ~1/3) ───────────────────────────────────────
    public int EndCompX { get; set; } = 0;
    public int EndCompY { get; set; } = -7000;

    // ── Interpolation / noise ───────────────────────────────────────────
    /// <summary>Easing applied when blending between adjacent phases.</summary>
    public EasingKind PhaseEasing { get; set; } = EasingKind.Smoothstep;

    /// <summary>Random noise multiplier (0 = none, 0.15 = 15% variance, recommended).</summary>
    public double NoiseFactor { get; set; } = 0.15;

    // ── Sensitivity scaling ─────────────────────────────────────────────
    /// <summary>
    /// Global sensitivity factor. Higher sensitivity → faster timing AND lower
    /// compensation magnitudes. <c>1.0</c> = default, <c>2.0</c> = double sens
    /// (halves comp values, halves duration).
    /// </summary>
    public double SensitivityScale { get; set; } = 1.0;
}
