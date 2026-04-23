using InputBusX.Domain.Enums;

namespace InputBusX.Domain.Entities;

/// <summary>
/// Configuration for the CrowBar cooperative anti-recoil mechanic.
/// Works WITH the player's manual input: the system detects stick-down
/// deflection and amplifies it with a fixed HTG (Hold-To-Ground) value.
/// </summary>
public sealed class CrowBarConfig
{
    /// <summary>CrowBar operating mode (Rapido = 40% assist, Padrao = 90% assist).</summary>
    public CrowBarMode Mode { get; set; } = CrowBarMode.Padrao;

    /// <summary>Base HTG (Hold-To-Ground) value. Default 16 (game-visible sensitivity units).</summary>
    public int BaseHtgValue { get; set; } = 16;

    /// <summary>Aim assist factor (0.0-1.0). Rapido = 0.40, Padrao = 0.90.</summary>
    public double AssistFactor { get; set; } = 0.90;

    /// <summary>Minimum stick deflection (normalised 0..1) before compensation activates.
    /// Prevents jitter when the stick is near center.</summary>
    public double DeflectionThreshold { get; set; } = 0.05;

    /// <summary>Deflection curve exponent. 1.0 = linear, &lt;1.0 = aggressive at low deflections, &gt;1.0 = gradual.</summary>
    public double DeflectionCurve { get; set; } = 1.0;

    /// <summary>Maximum compensation value per frame (prevents over-correction at full deflection).</summary>
    public int MaxCompensation { get; set; } = 10000;

    /// <summary>Random noise factor (0..1) to make compensation less pattern-detectable.</summary>
    public double NoiseFactor { get; set; } = 0.10;

    /// <summary>Scale factor for Padrao mode HTG. 1.125 = base 16 becomes effective 18.</summary>
    public double HtgScalePadrao { get; set; } = 1.125;
}
