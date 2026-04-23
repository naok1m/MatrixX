using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    // JSON options for deep-clone (handles all enums and nested types automatically)
    private static readonly JsonSerializerOptions _cloneOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public Profile Duplicate(string newName)
    {
        // Deep clone macros via JSON round-trip to capture ALL fields (including
        // Motion, HeadAssist, ProgressiveRecoil, TrackingAssist, CrowBar, Script, etc.)
        var macrosJson = JsonSerializer.Serialize(Macros, _cloneOptions);
        var clonedMacros = JsonSerializer.Deserialize<List<MacroDefinition>>(macrosJson, _cloneOptions) ?? [];

        // Regenerate IDs so the duplicated macros are distinct
        foreach (var m in clonedMacros)
            m.Id = Guid.NewGuid().ToString("N")[..8];

        return new Profile
        {
            Name = newName,
            AssociatedProcesses = new ObservableCollection<string>(AssociatedProcesses),
            Macros = clonedMacros,
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
}
