namespace InputBusX.Domain.Enums;

/// <summary>
/// Time-warping curves applied to <see cref="ShapeKind.Flick"/> (and as a
/// phase modulator on orbital shapes). Linear is the no-op baseline; the
/// ease-out curves bias motion toward the start, which is what makes a Head
/// Assist feel "snappy" — most of the displacement happens in the first
/// few ms, then the stick eases back.
/// </summary>
public enum EasingKind
{
    Linear,
    EaseOutQuad,
    EaseOutCubic,
    EaseInOutSine,
    EaseOutBack,
}
