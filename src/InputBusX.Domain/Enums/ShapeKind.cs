namespace InputBusX.Domain.Enums;

/// <summary>
/// Parametric stick motion shapes used by scripted macros. Each shape is a
/// continuous time function <c>t → (x, y)</c> sampled by the motion engine
/// every frame while the macro is active. Inspired by the "shape" motion
/// patterns on the Cronus Zen, but driven by actual math rather than recorded
/// traces so they scale with <see cref="EasingKind"/>, period and intensity.
/// </summary>
public enum ShapeKind
{
    /// <summary>Linear directional flick with easing. Base primitive for Head Assist.</summary>
    Flick,

    /// <summary>Circle — equal radius X/Y. Orbital aim.</summary>
    Circle,

    /// <summary>Ellipse with the long axis aligned horizontally.</summary>
    HorizontalOval,

    /// <summary>Ellipse with the long axis aligned vertically.</summary>
    VerticalOval,

    /// <summary>Ellipse rotated by an arbitrary angle (simulates diagonal recoil).</summary>
    DiagonalOval,
}
