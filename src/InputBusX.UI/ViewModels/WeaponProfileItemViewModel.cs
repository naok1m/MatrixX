using CommunityToolkit.Mvvm.ComponentModel;
using InputBusX.Domain.Entities;

namespace InputBusX.UI.ViewModels;

/// <summary>Editable row for a single weapon profile in the WeaponDetection UI.</summary>
public sealed partial class WeaponProfileItemViewModel : ObservableObject
{
    [ObservableProperty] private string _id;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _keywords;      // comma-separated
    [ObservableProperty] private int _recoilCompensationX;
    [ObservableProperty] private int _recoilCompensationY;
    [ObservableProperty] private double _intensity;
    [ObservableProperty] private bool _rapidFireEnabled;
    [ObservableProperty] private int _rapidFireIntervalMs;

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
    }

    /// <summary>Creates a blank new-weapon row.</summary>
    public WeaponProfileItemViewModel()
    {
        _id = Guid.NewGuid().ToString("N")[..8];
        _name = "New Weapon";
        _keywords = "";
        _recoilCompensationX = 0;
        _recoilCompensationY = -5000;
        _intensity = 1.0;
        _rapidFireEnabled = false;
        _rapidFireIntervalMs = 50;
    }

    public WeaponProfile ToEntity() => new()
    {
        Id = Id,
        Name = Name,
        Keywords = Keywords
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList(),
        RecoilCompensationX = RecoilCompensationX,
        RecoilCompensationY = RecoilCompensationY,
        Intensity = Intensity,
        RapidFireEnabled = RapidFireEnabled,
        RapidFireIntervalMs = RapidFireIntervalMs
    };
}
