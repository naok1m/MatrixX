using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InputBusX.UI.ViewModels;

public partial class MacroEditorViewModel : ViewModelBase
{
    private readonly IProfileManager _profileManager;
    private readonly ILogger<MacroEditorViewModel> _logger;

    [ObservableProperty] private MacroDefinition? _selectedMacro;
    [ObservableProperty] private string _macroName = "";
    [ObservableProperty] private MacroType _selectedMacroType;
    [ObservableProperty] private bool _macroEnabled = true;
    [ObservableProperty] private int _macroPriority;
    [ObservableProperty] private int _macroDelayMs;
    [ObservableProperty] private int _macroIntervalMs = 16;
    [ObservableProperty] private double _macroIntensity = 1.0;
    [ObservableProperty] private double _macroRandomization;
    [ObservableProperty] private bool _macroLoop;
    [ObservableProperty] private bool _toggleMode;
    [ObservableProperty] private int _recoilCompensationX;
    [ObservableProperty] private int _recoilCompensationY = -5000;
    [ObservableProperty] private TriggerSource _triggerSource = TriggerSource.RightTrigger;

    // Activation
    [ObservableProperty] private GamepadButton _activationButton;
    [ObservableProperty] private GamepadButton _actionButton;

    // Remap
    [ObservableProperty] private GamepadButton _sourceButton;
    [ObservableProperty] private GamepadButton _targetButton;

    // Aim assist buff
    [ObservableProperty] private int _flickStrength = 32767;
    [ObservableProperty] private int _flickIntervalMs = 8;

    // UI visibility helpers
    [ObservableProperty] private bool _showNoRecoilSettings;
    [ObservableProperty] private bool _showAutoPingSettings;
    [ObservableProperty] private bool _showAutoFireSettings;
    [ObservableProperty] private bool _showRemapSettings;
    [ObservableProperty] private bool _showTimingSettings;
    [ObservableProperty] private bool _showAimAssistBuffSettings;

    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private GamepadButton _pingButton;

    public ObservableCollection<MacroDefinition> Macros { get; } = [];
    public Array MacroTypes => Enum.GetValues(typeof(MacroType));
    public GamepadButton[] ButtonList { get; } = (GamepadButton[])Enum.GetValues(typeof(GamepadButton));
    public Array TriggerSources => Enum.GetValues(typeof(TriggerSource));

    public MacroEditorViewModel(IProfileManager profileManager, ILogger<MacroEditorViewModel> logger)
    {
        _profileManager = profileManager;
        _logger = logger;
        _profileManager.ActiveProfileChanged += _ => RefreshMacros();
        RefreshMacros();
    }

    partial void OnSelectedMacroTypeChanged(MacroType value) => UpdateVisibility();

    private void UpdateVisibility()
    {
        ShowNoRecoilSettings = SelectedMacroType == MacroType.NoRecoil;
        ShowAutoFireSettings = SelectedMacroType == MacroType.AutoFire;
        ShowAutoPingSettings = SelectedMacroType == MacroType.AutoPing;
        ShowRemapSettings = SelectedMacroType == MacroType.Remap;
        ShowAimAssistBuffSettings = SelectedMacroType == MacroType.AimAssistBuff;
        ShowTimingSettings = SelectedMacroType is MacroType.AutoFire or MacroType.AutoPing or MacroType.Sequence;
    }

    [RelayCommand]
    private void AddMacro()
    {
        var macro = new MacroDefinition
        {
            Name = $"New Macro {Macros.Count + 1}",
            Type = MacroType.AutoPing
        };

        var profile = _profileManager.ActiveProfile;
        profile.Macros.Add(macro);
        _profileManager.SaveProfile(profile);
        Macros.Add(macro);
        SelectedMacro = macro;
        _logger.LogInformation("Created macro: {Name}", macro.Name);
    }

    [RelayCommand]
    private void DeleteMacro()
    {
        if (SelectedMacro is null) return;

        var profile = _profileManager.ActiveProfile;
        profile.Macros.RemoveAll(m => m.Id == SelectedMacro.Id);
        _profileManager.SaveProfile(profile);
        Macros.Remove(SelectedMacro);
        SelectedMacro = Macros.FirstOrDefault();
        _logger.LogInformation("Deleted macro");
    }

    [RelayCommand]
    private void SaveMacro()
    {
        if (SelectedMacro is null) return;

        SelectedMacro.Name = MacroName;
        SelectedMacro.Type = SelectedMacroType;
        SelectedMacro.Enabled = MacroEnabled;
        SelectedMacro.Priority = MacroPriority;
        SelectedMacro.DelayMs = MacroDelayMs;
        SelectedMacro.IntervalMs = MacroIntervalMs;
        SelectedMacro.Intensity = MacroIntensity;
        SelectedMacro.RandomizationFactor = MacroRandomization;
        SelectedMacro.Loop = MacroLoop;
        SelectedMacro.ToggleMode = ToggleMode;

        // Activation button
        SelectedMacro.ActivationButton = ActivationButton != GamepadButton.None ? ActivationButton : null;

        // Auto ping
        if (SelectedMacroType == MacroType.AutoPing)
            SelectedMacro.PingButton = PingButton != GamepadButton.None ? PingButton : GamepadButton.DPadUp;

        // No Recoil
        if (SelectedMacroType == MacroType.NoRecoil)
            SelectedMacro.TriggerSource = TriggerSource;
        SelectedMacro.RecoilCompensationX = RecoilCompensationX;
        SelectedMacro.RecoilCompensationY = RecoilCompensationY;

        // AutoFire — must save TriggerSource so RT rapid fire works
        if (SelectedMacroType == MacroType.AutoFire)
            SelectedMacro.TriggerSource = TriggerSource;

        // Aim Assist Buff
        if (SelectedMacroType == MacroType.AimAssistBuff)
        {
            SelectedMacro.TriggerSource = TriggerSource;
            SelectedMacro.FlickStrength = FlickStrength;
            SelectedMacro.FlickIntervalMs = FlickIntervalMs;
        }

        // Remap
        SelectedMacro.SourceButton = SourceButton != GamepadButton.None ? SourceButton : null;
        SelectedMacro.TargetButton = TargetButton != GamepadButton.None ? TargetButton : null;

        _profileManager.SaveProfile(_profileManager.ActiveProfile);
        SaveStatus = "Saved!";
        _logger.LogInformation("Saved macro: {Name} (Type: {Type}, Activation: {Activation})",
            SelectedMacro.Name, SelectedMacro.Type, SelectedMacro.ActivationButton);

        // Clear status after a moment
        Task.Delay(2000).ContinueWith(_ => SaveStatus = "", TaskScheduler.FromCurrentSynchronizationContext());
    }

    partial void OnSelectedMacroChanged(MacroDefinition? value)
    {
        if (value is null) return;
        MacroName = value.Name;
        SelectedMacroType = value.Type;
        MacroEnabled = value.Enabled;
        MacroPriority = value.Priority;
        MacroDelayMs = value.DelayMs;
        MacroIntervalMs = value.IntervalMs;
        MacroIntensity = value.Intensity;
        MacroRandomization = value.RandomizationFactor;
        MacroLoop = value.Loop;
        ToggleMode = value.ToggleMode;
        ActivationButton = value.ActivationButton ?? GamepadButton.None;
        PingButton = value.PingButton ?? GamepadButton.DPadUp;
        TriggerSource = (value.TriggerSource != TriggerSource.LeftTrigger && value.TriggerSource != TriggerSource.RightTrigger)
            ? TriggerSource.RightTrigger
            : value.TriggerSource;
        ActionButton = value.TargetButton ?? GamepadButton.None;
        SourceButton = value.SourceButton ?? GamepadButton.None;
        TargetButton = value.TargetButton ?? GamepadButton.None;
        RecoilCompensationX = value.RecoilCompensationX;
        RecoilCompensationY = value.RecoilCompensationY;
        FlickStrength = value.FlickStrength > 0 ? value.FlickStrength : 32767;
        FlickIntervalMs = value.FlickIntervalMs > 0 ? value.FlickIntervalMs : 8;
        SaveStatus = "";
        UpdateVisibility();
    }

    private void RefreshMacros()
    {
        Macros.Clear();
        foreach (var macro in _profileManager.ActiveProfile.Macros)
            Macros.Add(macro);
        SelectedMacro = Macros.FirstOrDefault();
    }
}
