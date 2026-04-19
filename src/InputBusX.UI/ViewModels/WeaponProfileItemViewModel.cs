using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using InputBusX.Domain.Entities;

namespace InputBusX.UI.ViewModels;

/// <summary>
/// Editable row for a single weapon profile in the WeaponDetection UI.
/// Wraps a <see cref="WeaponProfile"/> for two-way binding.
/// </summary>
public sealed partial class WeaponProfileItemViewModel : ObservableObject
{
    [ObservableProperty] private string _id;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _keywords; // legacy (kept for backward-compat only)
    [ObservableProperty] private int _recoilCompensationX;
    [ObservableProperty] private int _recoilCompensationY;
    [ObservableProperty] private double _intensity;
    [ObservableProperty] private bool _rapidFireEnabled;
    [ObservableProperty] private int _rapidFireIntervalMs;

    /// <summary>Mutable collection backing <see cref="WeaponProfile.ReferenceImagePaths"/>.</summary>
    public ObservableCollection<string> ReferenceImagePaths { get; } = [];

    /// <summary>Convenience binding — "3 references" / "No reference captured".</summary>
    public string ReferenceSummary =>
        ReferenceImagePaths.Count switch
        {
            0 => "No reference captured",
            1 => "1 reference",
            _ => $"{ReferenceImagePaths.Count} references"
        };

    /// <summary>True when at least one reference is registered.</summary>
    public bool HasReferences => ReferenceImagePaths.Count > 0;

    public WeaponProfileItemViewModel(WeaponProfile profile)
    {
        _id = profile.Id;
        _name = profile.Name;
        _keywords = string.Join(", ", profile.Keywords);
        _recoilCompensationX = profile.RecoilCompensationX;
        _recoilCompensationY = profile.RecoilCompensationY;
        _intensity = profile.Intensity;
        _rapidFireEnabled = profile.RapidFireEnabled;
        _rapidFireIntervalMs = profile.RapidFireIntervalMs;

        foreach (var p in profile.ReferenceImagePaths)
            ReferenceImagePaths.Add(p);

        ReferenceImagePaths.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ReferenceSummary));
            OnPropertyChanged(nameof(HasReferences));
        };
    }

    /// <summary>Creates a blank new-weapon row.</summary>
    public WeaponProfileItemViewModel() : this(new WeaponProfile()) { }

    public WeaponProfile ToEntity() => new()
    {
        Id = Id,
        Name = Name,
        Keywords = Keywords
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList(),
        ReferenceImagePaths = [..ReferenceImagePaths],
        RecoilCompensationX = RecoilCompensationX,
        RecoilCompensationY = RecoilCompensationY,
        Intensity = Intensity,
        RapidFireEnabled = RapidFireEnabled,
        RapidFireIntervalMs = RapidFireIntervalMs
    };
}
