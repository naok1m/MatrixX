namespace InputBusX.Domain.Entities;

public sealed class FilterSettings
{
    public double LeftStickDeadzone { get; set; } = 0.05;
    public double RightStickDeadzone { get; set; } = 0.05;
    public double LeftStickAntiDeadzone { get; set; }
    public double RightStickAntiDeadzone { get; set; }
    public double TriggerDeadzone { get; set; } = 0.02;
    public double ResponseCurveExponent { get; set; } = 1.0;
    public double SmoothingFactor { get; set; } = 0.0;
    public bool SmoothingEnabled { get; set; }
}
