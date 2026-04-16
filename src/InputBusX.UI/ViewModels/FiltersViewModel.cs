using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InputBusX.UI.ViewModels;

public partial class FiltersViewModel : ViewModelBase
{
    private readonly IProfileManager _profileManager;
    private readonly ILogger<FiltersViewModel> _logger;

    [ObservableProperty] private double _leftStickDeadzone = 0.05;
    [ObservableProperty] private double _rightStickDeadzone = 0.05;
    [ObservableProperty] private double _leftStickAntiDeadzone;
    [ObservableProperty] private double _rightStickAntiDeadzone;
    [ObservableProperty] private double _triggerDeadzone = 0.02;
    [ObservableProperty] private double _responseCurveExponent = 1.0;
    [ObservableProperty] private double _smoothingFactor;
    [ObservableProperty] private bool _smoothingEnabled;

    public FiltersViewModel(IProfileManager profileManager, ILogger<FiltersViewModel> logger)
    {
        _profileManager = profileManager;
        _logger = logger;
        _profileManager.ActiveProfileChanged += _ => LoadFromProfile();
        LoadFromProfile();
    }

    [RelayCommand]
    private void Apply()
    {
        var profile = _profileManager.ActiveProfile;
        profile.Filters.LeftStickDeadzone = LeftStickDeadzone;
        profile.Filters.RightStickDeadzone = RightStickDeadzone;
        profile.Filters.LeftStickAntiDeadzone = LeftStickAntiDeadzone;
        profile.Filters.RightStickAntiDeadzone = RightStickAntiDeadzone;
        profile.Filters.TriggerDeadzone = TriggerDeadzone;
        profile.Filters.ResponseCurveExponent = ResponseCurveExponent;
        profile.Filters.SmoothingFactor = SmoothingFactor;
        profile.Filters.SmoothingEnabled = SmoothingEnabled;
        _profileManager.SaveProfile(profile);
        _logger.LogInformation("Filter settings applied");
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        LeftStickDeadzone = 0.05;
        RightStickDeadzone = 0.05;
        LeftStickAntiDeadzone = 0;
        RightStickAntiDeadzone = 0;
        TriggerDeadzone = 0.02;
        ResponseCurveExponent = 1.0;
        SmoothingFactor = 0;
        SmoothingEnabled = false;
        Apply();
    }

    private void LoadFromProfile()
    {
        var f = _profileManager.ActiveProfile.Filters;
        LeftStickDeadzone = f.LeftStickDeadzone;
        RightStickDeadzone = f.RightStickDeadzone;
        LeftStickAntiDeadzone = f.LeftStickAntiDeadzone;
        RightStickAntiDeadzone = f.RightStickAntiDeadzone;
        TriggerDeadzone = f.TriggerDeadzone;
        ResponseCurveExponent = f.ResponseCurveExponent;
        SmoothingFactor = f.SmoothingFactor;
        SmoothingEnabled = f.SmoothingEnabled;
    }
}
