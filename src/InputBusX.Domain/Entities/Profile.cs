using System.Collections.ObjectModel;

namespace InputBusX.Domain.Entities;

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public required string Name { get; set; }
    public bool IsDefault { get; set; }
    public ObservableCollection<string> AssociatedProcesses { get; set; } = [];
    public List<MacroDefinition> Macros { get; set; } = [];
    public FilterSettings Filters { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    public Profile Duplicate(string newName) => new()
    {
        Name = newName,
        AssociatedProcesses = new ObservableCollection<string>(AssociatedProcesses),
        Macros = Macros.Select(m => new MacroDefinition
        {
            Name = m.Name,
            Type = m.Type,
            Enabled = m.Enabled,
            Priority = m.Priority,
            ActivationButton = m.ActivationButton,
            ActivationAxis = m.ActivationAxis,
            ToggleMode = m.ToggleMode,
            DelayMs = m.DelayMs,
            IntervalMs = m.IntervalMs,
            DurationMs = m.DurationMs,
            Loop = m.Loop,
            Intensity = m.Intensity,
            RandomizationFactor = m.RandomizationFactor,
            PingButton = m.PingButton,
            SourceButton = m.SourceButton,
            TargetButton = m.TargetButton,
            SourceAxis = m.SourceAxis,
            TargetAxis = m.TargetAxis,
            TriggerSource = m.TriggerSource,
            RecoilCompensationX = m.RecoilCompensationX,
            RecoilCompensationY = m.RecoilCompensationY,
            FlickStrength = m.FlickStrength,
            FlickIntervalMs = m.FlickIntervalMs,
            Steps = m.Steps.Select(s => new MacroStep
            {
                ButtonPress = s.ButtonPress,
                ButtonRelease = s.ButtonRelease,
                Axis = s.Axis,
                AxisValue = s.AxisValue,
                DelayAfterMs = s.DelayAfterMs
            }).ToList()
        }).ToList(),
        Filters = new FilterSettings
        {
            LeftStickDeadzone = Filters.LeftStickDeadzone,
            RightStickDeadzone = Filters.RightStickDeadzone,
            LeftStickAntiDeadzone = Filters.LeftStickAntiDeadzone,
            RightStickAntiDeadzone = Filters.RightStickAntiDeadzone,
            TriggerDeadzone = Filters.TriggerDeadzone,
            ResponseCurveExponent = Filters.ResponseCurveExponent,
            SmoothingFactor = Filters.SmoothingFactor,
            SmoothingEnabled = Filters.SmoothingEnabled
        }
    };
}
