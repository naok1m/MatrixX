using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InputBusX.Application.Interfaces;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using ScriptStep = InputBusX.Domain.Entities.ScriptStep;

namespace InputBusX.UI.ViewModels;

public partial class MacroEditorViewModel : ViewModelBase
{
    private readonly IProfileManager _profileManager;
    private readonly IInputPipeline _pipeline;
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
    [ObservableProperty] private bool _showScriptedShapeSettings;
    [ObservableProperty] private bool _showHeadAssistSettings;
    [ObservableProperty] private bool _showProgressiveRecoilSettings;
    [ObservableProperty] private bool _showTrackingAssistSettings;
    [ObservableProperty] private bool _showAutoFireNoRecoilSettings;
    [ObservableProperty] private bool _showInstaDropShotSettings;
    [ObservableProperty] private bool _showJumpShotSettings;
    [ObservableProperty] private bool _showStrafeShotSettings;
    [ObservableProperty] private bool _showHoldBreathSettings;
    [ObservableProperty] private bool _showSlideCancelSettings;
    [ObservableProperty] private bool _showFastDropSettings;
    [ObservableProperty] private bool _showCrowBarSettings;
    [ObservableProperty] private bool _showCustomScriptSettings;

    [ObservableProperty] private string _saveStatus = "";

    // ── CrowBar ─────────────────────────────────────────────────────────
    [ObservableProperty] private CrowBarMode _crowBarMode = CrowBarMode.Padrao;
    [ObservableProperty] private int _crowBarBaseHtg = 16;
    [ObservableProperty] private double _crowBarAssistFactor = 0.90;
    [ObservableProperty] private double _crowBarDeflectionThreshold = 0.05;
    [ObservableProperty] private double _crowBarDeflectionCurve = 1.0;
    [ObservableProperty] private int _crowBarMaxCompensation = 10000;
    [ObservableProperty] private double _crowBarNoiseFactor = 0.10;
    [ObservableProperty] private double _crowBarHtgScalePadrao = 1.125;

    // ── Custom Script ───────────────────────────────────────────────────
    [ObservableProperty] private ScriptTriggerKind _scriptTriggerMode = ScriptTriggerKind.WhileHeld;
    [ObservableProperty] private bool _scriptAutoLoop = true;
    [ObservableProperty] private double _scriptSpeedMultiplier = 1.0;
    [ObservableProperty] private string _scriptDescription = "";
    [ObservableProperty] private ScriptStepViewModel? _selectedScriptStep;

    // ── New macro fields ─────────────────────────────────────────────────
    [ObservableProperty] private GamepadButton _crouchButton = GamepadButton.B;
    [ObservableProperty] private GamepadButton _jumpButton = GamepadButton.A;
    [ObservableProperty] private int _jumpIntervalMs = 500;
    [ObservableProperty] private double _strafeAmplitude = 0.60;
    [ObservableProperty] private int _strafeIntervalMs = 120;
    [ObservableProperty] private GamepadButton _breathButton = GamepadButton.LeftThumb;
    [ObservableProperty] private GamepadButton _slideButton = GamepadButton.B;
    [ObservableProperty] private int _slideCancelDelayMs = 180;
    [ObservableProperty] private GamepadButton _slideCancelButton = GamepadButton.B;
    [ObservableProperty] private GamepadButton _pingButton;

    // ── Progressive Recoil ───────────────────────────────────────────────
    [ObservableProperty] private int _prTotalAmmo = 60;
    [ObservableProperty] private double _prFullMagDurationMs = 2500;
    [ObservableProperty] private int _prStartCompX;
    [ObservableProperty] private int _prStartCompY = -3000;
    [ObservableProperty] private int _prMidCompX;
    [ObservableProperty] private int _prMidCompY = -5000;
    [ObservableProperty] private int _prEndCompX;
    [ObservableProperty] private int _prEndCompY = -7000;
    [ObservableProperty] private EasingKind _prPhaseEasing = EasingKind.Smoothstep;
    [ObservableProperty] private double _prNoiseFactor = 0.15;
    [ObservableProperty] private double _prSensitivityScale = 1.0;

    // ── Tracking Assist ─────────────────────────────────────────────────
    [ObservableProperty] private ShapeKind _taShape = ShapeKind.Circle;
    [ObservableProperty] private StickTargetKind _taTarget = StickTargetKind.Right;
    [ObservableProperty] private double _taBaseRadius = 0.08;
    [ObservableProperty] private double _taMaxRadius = 0.25;
    [ObservableProperty] private double _taPeriodMs = 120;
    [ObservableProperty] private bool _taClockwise = true;
    [ObservableProperty] private double _taDeflectionThreshold = 0.10;
    [ObservableProperty] private double _taScaleCurve = 0.7;
    [ObservableProperty] private EasingKind _taEasing = EasingKind.EaseInOutSine;
    [ObservableProperty] private double _taIntensityMul = 1.0;
    [ObservableProperty] private bool _taFreeOrbit;

    // ── ScriptedShape (single MotionScript) ──────────────────────────────
    [ObservableProperty] private ShapeKind _shape = ShapeKind.Circle;
    [ObservableProperty] private StickTargetKind _stickTarget = StickTargetKind.Right;
    [ObservableProperty] private double _radiusXNorm = 0.35;
    [ObservableProperty] private double _radiusYNorm = 0.35;
    [ObservableProperty] private double _rotationDeg;
    [ObservableProperty] private double _periodMs = 400;
    [ObservableProperty] private double _motionDurationMs;
    [ObservableProperty] private double _directionDeg = 90;
    [ObservableProperty] private double _amplitudeNorm = 0.55;
    [ObservableProperty] private double _startPhaseDeg;
    [ObservableProperty] private bool _clockwise = true;
    [ObservableProperty] private EasingKind _easing = EasingKind.EaseOutCubic;
    [ObservableProperty] private double _intensityMul = 1.0;
    [ObservableProperty] private bool _additive = true;

    // ── HeadAssist ───────────────────────────────────────────────────────
    [ObservableProperty] private DistanceSource _distanceSource = DistanceSource.Auto;
    [ObservableProperty] private double _shortHoldMsMax = 150;
    [ObservableProperty] private double _mediumHoldMsMax = 500;
    [ObservableProperty] private double _deflectionShortMax = 0.30;
    [ObservableProperty] private double _deflectionMediumMax = 0.65;
    [ObservableProperty] private double _recoilShortMax = 2500;
    [ObservableProperty] private double _recoilMediumMax = 6000;
    [ObservableProperty] private double _weightTrigger = 1.0;
    [ObservableProperty] private double _weightDeflection = 1.0;
    [ObservableProperty] private double _weightRecoil = 0.5;
    [ObservableProperty] private GamepadButton _cycleButton;
    [ObservableProperty] private int _reFireCooldownMs = 250;
    [ObservableProperty] private int _minTriggerHoldMs = 20;
    [ObservableProperty] private bool _fireOnPress = true;
    [ObservableProperty] private bool _fireOnce = true;
    [ObservableProperty] private StickTargetKind _haStickTarget = StickTargetKind.Right;
    [ObservableProperty] private bool _haAdditive = true;

    // Head Assist per-range amplitude/duration sliders (flick-only simplified UI)
    [ObservableProperty] private double _haShortAmp = 0.45;
    [ObservableProperty] private double _haShortDurMs = 90;
    [ObservableProperty] private double _haShortDirDeg = 90;
    [ObservableProperty] private double _haMediumAmp = 0.70;
    [ObservableProperty] private double _haMediumDurMs = 140;
    [ObservableProperty] private double _haMediumDirDeg = 90;
    [ObservableProperty] private double _haLongAmp = 0.90;
    [ObservableProperty] private double _haLongDurMs = 220;
    [ObservableProperty] private double _haLongDirDeg = 90;

    public ObservableCollection<MacroDefinition> Macros { get; } = [];
    public Array MacroTypes => Enum.GetValues(typeof(MacroType));
    public GamepadButton[] ButtonList { get; } = (GamepadButton[])Enum.GetValues(typeof(GamepadButton));
    public Array TriggerSources => Enum.GetValues(typeof(TriggerSource));
    public Array ShapeKinds => Enum.GetValues(typeof(ShapeKind));
    public Array EasingKinds => Enum.GetValues(typeof(EasingKind));
    public Array StickTargetKinds => Enum.GetValues(typeof(StickTargetKind));
    public Array DistanceSources => Enum.GetValues(typeof(DistanceSource));
    public Array CrowBarModes => Enum.GetValues(typeof(CrowBarMode));
    public Array ScriptActionKinds => Enum.GetValues(typeof(ScriptActionKind));
    public Array ScriptTriggerKinds => Enum.GetValues(typeof(ScriptTriggerKind));
    public AnalogAxis[] AxisList { get; } = (AnalogAxis[])Enum.GetValues(typeof(AnalogAxis));
    public ObservableCollection<ScriptStepViewModel> ScriptSteps { get; } = [];

    public MacroEditorViewModel(IProfileManager profileManager, IInputPipeline pipeline, ILogger<MacroEditorViewModel> logger)
    {
        _profileManager = profileManager;
        _pipeline = pipeline;
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
        ShowScriptedShapeSettings = SelectedMacroType == MacroType.ScriptedShape;
        ShowHeadAssistSettings = SelectedMacroType == MacroType.HeadAssist;
        ShowProgressiveRecoilSettings = SelectedMacroType == MacroType.ProgressiveRecoil;
        ShowTrackingAssistSettings = SelectedMacroType == MacroType.TrackingAssist;
        ShowAutoFireNoRecoilSettings = SelectedMacroType == MacroType.AutoFireNoRecoil;
        ShowInstaDropShotSettings = SelectedMacroType == MacroType.InstaDropShot;
        ShowJumpShotSettings = SelectedMacroType == MacroType.JumpShot;
        ShowStrafeShotSettings = SelectedMacroType == MacroType.StrafeShot;
        ShowHoldBreathSettings = SelectedMacroType == MacroType.HoldBreath;
        ShowSlideCancelSettings = SelectedMacroType == MacroType.SlideCancel;
        ShowFastDropSettings = SelectedMacroType == MacroType.FastDrop;
        ShowCrowBarSettings = SelectedMacroType == MacroType.CrowBar;
        ShowCustomScriptSettings = SelectedMacroType == MacroType.Custom;
        ShowTimingSettings = SelectedMacroType is MacroType.AutoFire or MacroType.AutoPing or MacroType.Sequence;
    }

    // ── JSON options shared between save/load and import/export ────────────
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly FilePickerFileType _macroFileType = new("MatrixX Macros")
    {
        Patterns = ["*.matrixmacros"],
        MimeTypes = ["application/json"],
    };

    // ── Import / Export ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExportSelectedMacro()
    {
        if (SelectedMacro is null) return;
        await ExportMacrosToFileAsync([SelectedMacro]);
    }

    [RelayCommand]
    private async Task ExportAllMacros()
    {
        if (Macros.Count == 0) return;
        await ExportMacrosToFileAsync([.. Macros]);
    }

    [RelayCommand]
    private async Task ImportMacros()
    {
        var sp = GetStorageProvider();
        if (sp is null) return;

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar Macros — MatrixX",
            AllowMultiple = false,
            FileTypeFilter = [_macroFileType, FilePickerFileTypes.All],
        });

        if (files is not { Count: > 0 }) return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            var file = await JsonSerializer.DeserializeAsync<MacroExportFile>(stream, _jsonOptions);
            if (file?.Macros is not { Count: > 0 })
            {
                SaveStatus = "Arquivo vazio ou inválido.";
                return;
            }

            var profile = _profileManager.ActiveProfile;
            int added = 0;
            foreach (var m in file.Macros)
            {
                // Re-generate ID to avoid collisions with existing macros
                m.Id = Guid.NewGuid().ToString("N")[..8];
                profile.Macros.Add(m);
                Macros.Add(m);
                added++;
            }

            _profileManager.SaveProfile(profile);
            _pipeline.InvalidateMacroCache();
            SaveStatus = $"{added} macro(s) importado(s)!";
            SelectedMacro = Macros.LastOrDefault();
            _logger.LogInformation("Imported {Count} macros from file", added);
        }
        catch (Exception ex)
        {
            SaveStatus = "Erro ao importar.";
            _logger.LogError(ex, "Failed to import macros");
        }

        _ = Task.Delay(3000).ContinueWith(_ => SaveStatus = "", TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task ExportMacrosToFileAsync(List<MacroDefinition> macros)
    {
        var sp = GetStorageProvider();
        if (sp is null) return;

        var suggested = macros.Count == 1
            ? $"{macros[0].Name.Replace(' ', '_')}.matrixmacros"
            : "MatrixX_Macros_Export.matrixmacros";

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar Macros — MatrixX",
            SuggestedFileName = suggested,
            FileTypeChoices = [_macroFileType],
            DefaultExtension = "matrixmacros",
        });

        if (file is null) return;

        try
        {
            var export = new MacroExportFile(
                Version: "2",
                ExportedAt: DateTimeOffset.UtcNow,
                AppVersion: System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?",
                Macros: macros);

            await using var stream = await file.OpenWriteAsync();
            await JsonSerializer.SerializeAsync(stream, export, _jsonOptions);

            SaveStatus = $"{macros.Count} macro(s) exportado(s)!";
            _logger.LogInformation("Exported {Count} macros to {File}", macros.Count, file.Name);
        }
        catch (Exception ex)
        {
            SaveStatus = "Erro ao exportar.";
            _logger.LogError(ex, "Failed to export macros");
        }

        _ = Task.Delay(3000).ContinueWith(_ => SaveStatus = "", TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static IStorageProvider? GetStorageProvider()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return TopLevel.GetTopLevel(desktop.MainWindow)?.StorageProvider;
        return null;
    }

    // ── Macro CRUD ───────────────────────────────────────────────────────

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
        _pipeline.InvalidateMacroCache();
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
        _pipeline.InvalidateMacroCache();
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

        // ScriptedShape
        if (SelectedMacroType == MacroType.ScriptedShape)
        {
            SelectedMacro.Motion.Shape = Shape;
            SelectedMacro.Motion.Target = StickTarget;
            SelectedMacro.Motion.RadiusXNorm = RadiusXNorm;
            SelectedMacro.Motion.RadiusYNorm = RadiusYNorm;
            SelectedMacro.Motion.RotationDeg = RotationDeg;
            SelectedMacro.Motion.PeriodMs = PeriodMs;
            SelectedMacro.Motion.DurationMs = MotionDurationMs;
            SelectedMacro.Motion.DirectionDeg = DirectionDeg;
            SelectedMacro.Motion.AmplitudeNorm = AmplitudeNorm;
            SelectedMacro.Motion.StartPhaseDeg = StartPhaseDeg;
            SelectedMacro.Motion.Clockwise = Clockwise;
            SelectedMacro.Motion.Easing = Easing;
            SelectedMacro.Motion.IntensityMul = IntensityMul;
            SelectedMacro.Motion.Additive = Additive;
            SelectedMacro.TriggerSource = TriggerSource;
        }

        // HeadAssist
        if (SelectedMacroType == MacroType.HeadAssist)
        {
            var h = SelectedMacro.HeadAssist;
            h.DistanceSource = DistanceSource;
            h.ShortHoldMsMax = ShortHoldMsMax;
            h.MediumHoldMsMax = MediumHoldMsMax;
            h.DeflectionShortMax = DeflectionShortMax;
            h.DeflectionMediumMax = DeflectionMediumMax;
            h.RecoilShortMax = RecoilShortMax;
            h.RecoilMediumMax = RecoilMediumMax;
            h.WeightTrigger = WeightTrigger;
            h.WeightDeflection = WeightDeflection;
            h.WeightRecoil = WeightRecoil;
            h.CycleButton = CycleButton != GamepadButton.None ? CycleButton : null;
            h.ReFireCooldownMs = ReFireCooldownMs;
            h.MinTriggerHoldMs = MinTriggerHoldMs;
            h.FireOnPress = FireOnPress;
            h.FireOnce = FireOnce;
            h.ShortRange.AmplitudeNorm = HaShortAmp;
            h.ShortRange.DurationMs = HaShortDurMs;
            h.ShortRange.DirectionDeg = HaShortDirDeg;
            h.MediumRange.AmplitudeNorm = HaMediumAmp;
            h.MediumRange.DurationMs = HaMediumDurMs;
            h.MediumRange.DirectionDeg = HaMediumDirDeg;
            h.LongRange.AmplitudeNorm = HaLongAmp;
            h.LongRange.DurationMs = HaLongDurMs;
            h.LongRange.DirectionDeg = HaLongDirDeg;
            // Shared target + additive mode propagate to all 3 presets
            h.ShortRange.Target  = HaStickTarget;
            h.MediumRange.Target = HaStickTarget;
            h.LongRange.Target   = HaStickTarget;
            h.ShortRange.Additive  = HaAdditive;
            h.MediumRange.Additive = HaAdditive;
            h.LongRange.Additive   = HaAdditive;
            SelectedMacro.TriggerSource = TriggerSource;
        }

        // ProgressiveRecoil
        if (SelectedMacroType == MacroType.ProgressiveRecoil)
        {
            var pr = SelectedMacro.ProgressiveRecoil;
            pr.TotalAmmo = PrTotalAmmo;
            pr.FullMagDurationMs = PrFullMagDurationMs;
            pr.StartCompX = PrStartCompX;
            pr.StartCompY = PrStartCompY;
            pr.MidCompX = PrMidCompX;
            pr.MidCompY = PrMidCompY;
            pr.EndCompX = PrEndCompX;
            pr.EndCompY = PrEndCompY;
            pr.PhaseEasing = PrPhaseEasing;
            pr.NoiseFactor = PrNoiseFactor;
            pr.SensitivityScale = PrSensitivityScale;
            SelectedMacro.TriggerSource = TriggerSource;
        }

        // TrackingAssist
        if (SelectedMacroType == MacroType.TrackingAssist)
        {
            var ta = SelectedMacro.TrackingAssist;
            ta.Shape = TaShape;
            ta.Target = TaTarget;
            ta.BaseRadiusNorm = TaBaseRadius;
            ta.MaxRadiusNorm = TaMaxRadius;
            ta.PeriodMs = TaPeriodMs;
            ta.Clockwise = TaClockwise;
            ta.DeflectionThreshold = TaDeflectionThreshold;
            ta.ScaleCurve = TaScaleCurve;
            ta.Easing = TaEasing;
            ta.IntensityMul = TaIntensityMul;
            ta.FreeOrbit = TaFreeOrbit;
            SelectedMacro.TriggerSource = TriggerSource;
        }

        // AutoFireNoRecoil — uses existing RecoilCompensation + TriggerSource fields
        if (SelectedMacroType == MacroType.AutoFireNoRecoil)
            SelectedMacro.TriggerSource = TriggerSource;

        // InstaDropShot
        if (SelectedMacroType == MacroType.InstaDropShot)
            SelectedMacro.CrouchButton = CrouchButton;

        // JumpShot
        if (SelectedMacroType == MacroType.JumpShot)
        {
            SelectedMacro.JumpButton = JumpButton;
            SelectedMacro.JumpIntervalMs = JumpIntervalMs;
            SelectedMacro.TriggerSource = TriggerSource;
        }

        // StrafeShot
        if (SelectedMacroType == MacroType.StrafeShot)
        {
            SelectedMacro.StrafeAmplitude = StrafeAmplitude;
            SelectedMacro.StrafeIntervalMs = StrafeIntervalMs;
            SelectedMacro.TriggerSource = TriggerSource;
        }

        // HoldBreath
        if (SelectedMacroType == MacroType.HoldBreath)
        {
            SelectedMacro.BreathButton = BreathButton;
            SelectedMacro.TriggerSource = TriggerSource;
        }

        // SlideCancel
        if (SelectedMacroType == MacroType.SlideCancel)
        {
            SelectedMacro.SlideButton = SlideButton;
            SelectedMacro.SlideCancelDelayMs = SlideCancelDelayMs;
            SelectedMacro.SlideCancelButton = SlideCancelButton;
        }

        // FastDrop
        if (SelectedMacroType == MacroType.FastDrop)
        {
            SelectedMacro.CrouchButton = CrouchButton;
            SelectedMacro.TriggerSource = TriggerSource;
        }

        // CrowBar
        if (SelectedMacroType == MacroType.CrowBar)
        {
            var cb = SelectedMacro.CrowBar;
            cb.Mode = CrowBarMode;
            cb.BaseHtgValue = CrowBarBaseHtg;
            cb.AssistFactor = CrowBarAssistFactor;
            cb.DeflectionThreshold = CrowBarDeflectionThreshold;
            cb.DeflectionCurve = CrowBarDeflectionCurve;
            cb.MaxCompensation = CrowBarMaxCompensation;
            cb.NoiseFactor = CrowBarNoiseFactor;
            cb.HtgScalePadrao = CrowBarHtgScalePadrao;
            SelectedMacro.TriggerSource = TriggerSource;
        }

        // Custom Script
        if (SelectedMacroType == MacroType.Custom)
        {
            var script = SelectedMacro.Script;
            script.TriggerMode = ScriptTriggerMode;
            script.AutoLoop = ScriptAutoLoop;
            script.SpeedMultiplier = ScriptSpeedMultiplier;
            script.Description = ScriptDescription;
            script.Steps = ScriptSteps.Select(s => s.ToModel()).ToList();
            // TriggerSource controls WHICH trigger (RT/LT/None) is required alongside the ActivationButton
            SelectedMacro.TriggerSource = TriggerSource;
        }

        _profileManager.SaveProfile(_profileManager.ActiveProfile);
        _pipeline.InvalidateMacroCache();
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
        TriggerSource = value.TriggerSource;
        ActionButton = value.TargetButton ?? GamepadButton.None;
        SourceButton = value.SourceButton ?? GamepadButton.None;
        TargetButton = value.TargetButton ?? GamepadButton.None;
        RecoilCompensationX = value.RecoilCompensationX;
        RecoilCompensationY = value.RecoilCompensationY;
        FlickStrength = value.FlickStrength > 0 ? value.FlickStrength : 32767;
        FlickIntervalMs = value.FlickIntervalMs > 0 ? value.FlickIntervalMs : 8;

        // ScriptedShape
        Shape = value.Motion.Shape;
        StickTarget = value.Motion.Target;
        RadiusXNorm = value.Motion.RadiusXNorm;
        RadiusYNorm = value.Motion.RadiusYNorm;
        RotationDeg = value.Motion.RotationDeg;
        PeriodMs = value.Motion.PeriodMs;
        MotionDurationMs = value.Motion.DurationMs;
        DirectionDeg = value.Motion.DirectionDeg;
        AmplitudeNorm = value.Motion.AmplitudeNorm;
        StartPhaseDeg = value.Motion.StartPhaseDeg;
        Clockwise = value.Motion.Clockwise;
        Easing = value.Motion.Easing;
        IntensityMul = value.Motion.IntensityMul;
        Additive = value.Motion.Additive;

        // HeadAssist
        var ha = value.HeadAssist;
        DistanceSource = ha.DistanceSource;
        ShortHoldMsMax = ha.ShortHoldMsMax;
        MediumHoldMsMax = ha.MediumHoldMsMax;
        DeflectionShortMax = ha.DeflectionShortMax;
        DeflectionMediumMax = ha.DeflectionMediumMax;
        RecoilShortMax = ha.RecoilShortMax;
        RecoilMediumMax = ha.RecoilMediumMax;
        WeightTrigger = ha.WeightTrigger;
        WeightDeflection = ha.WeightDeflection;
        WeightRecoil = ha.WeightRecoil;
        CycleButton = ha.CycleButton ?? GamepadButton.None;
        ReFireCooldownMs = ha.ReFireCooldownMs;
        MinTriggerHoldMs = ha.MinTriggerHoldMs;
        FireOnPress = ha.FireOnPress;
        FireOnce = ha.FireOnce;
        HaShortAmp = ha.ShortRange.AmplitudeNorm;
        HaShortDurMs = ha.ShortRange.DurationMs;
        HaShortDirDeg = ha.ShortRange.DirectionDeg;
        HaMediumAmp = ha.MediumRange.AmplitudeNorm;
        HaMediumDurMs = ha.MediumRange.DurationMs;
        HaMediumDirDeg = ha.MediumRange.DirectionDeg;
        HaLongAmp = ha.LongRange.AmplitudeNorm;
        HaLongDurMs = ha.LongRange.DurationMs;
        HaLongDirDeg = ha.LongRange.DirectionDeg;
        HaStickTarget = ha.ShortRange.Target;
        HaAdditive = ha.ShortRange.Additive;

        // ProgressiveRecoil
        var pr = value.ProgressiveRecoil;
        PrTotalAmmo = pr.TotalAmmo;
        PrFullMagDurationMs = pr.FullMagDurationMs;
        PrStartCompX = pr.StartCompX;
        PrStartCompY = pr.StartCompY;
        PrMidCompX = pr.MidCompX;
        PrMidCompY = pr.MidCompY;
        PrEndCompX = pr.EndCompX;
        PrEndCompY = pr.EndCompY;
        PrPhaseEasing = pr.PhaseEasing;
        PrNoiseFactor = pr.NoiseFactor;
        PrSensitivityScale = pr.SensitivityScale;

        // TrackingAssist
        var ta = value.TrackingAssist;
        TaShape = ta.Shape;
        TaTarget = ta.Target;
        TaBaseRadius = ta.BaseRadiusNorm;
        TaMaxRadius = ta.MaxRadiusNorm;
        TaPeriodMs = ta.PeriodMs;
        TaClockwise = ta.Clockwise;
        TaDeflectionThreshold = ta.DeflectionThreshold;
        TaScaleCurve = ta.ScaleCurve;
        TaEasing = ta.Easing;
        TaIntensityMul = ta.IntensityMul;
        TaFreeOrbit = ta.FreeOrbit;

        // New macros
        CrouchButton = value.CrouchButton;
        JumpButton = value.JumpButton;
        JumpIntervalMs = value.JumpIntervalMs > 0 ? value.JumpIntervalMs : 500;
        StrafeAmplitude = value.StrafeAmplitude > 0 ? value.StrafeAmplitude : 0.60;
        StrafeIntervalMs = value.StrafeIntervalMs > 0 ? value.StrafeIntervalMs : 120;
        BreathButton = value.BreathButton;
        SlideButton = value.SlideButton;
        SlideCancelDelayMs = value.SlideCancelDelayMs > 0 ? value.SlideCancelDelayMs : 180;
        SlideCancelButton = value.SlideCancelButton;

        // CrowBar
        var cb = value.CrowBar;
        CrowBarMode = cb.Mode;
        CrowBarBaseHtg = cb.BaseHtgValue;
        CrowBarAssistFactor = cb.AssistFactor;
        CrowBarDeflectionThreshold = cb.DeflectionThreshold;
        CrowBarDeflectionCurve = cb.DeflectionCurve;
        CrowBarMaxCompensation = cb.MaxCompensation;
        CrowBarNoiseFactor = cb.NoiseFactor;
        CrowBarHtgScalePadrao = cb.HtgScalePadrao;

        // Custom Script
        ScriptTriggerMode = value.Script.TriggerMode;
        ScriptAutoLoop = value.Script.AutoLoop;
        ScriptSpeedMultiplier = value.Script.SpeedMultiplier;
        ScriptDescription = value.Script.Description;
        ScriptSteps.Clear();
        for (int i = 0; i < value.Script.Steps.Count; i++)
            ScriptSteps.Add(ScriptStepViewModel.FromModel(value.Script.Steps[i], i));

        SaveStatus = "";
        UpdateVisibility();
    }

    partial void OnCrowBarModeChanged(CrowBarMode value)
    {
        if (value == CrowBarMode.Rapido)
            CrowBarAssistFactor = 0.40;
        else
            CrowBarAssistFactor = 0.90;
    }

    // ── Custom Script step commands ─────────────────────────────────────

    [RelayCommand]
    private void AddScriptStep()
    {
        var step = new ScriptStepViewModel { Index = ScriptSteps.Count, Action = ScriptActionKind.Wait, DurationMs = 50 };
        ScriptSteps.Add(step);
        SelectedScriptStep = step;
        ReindexScriptSteps();
    }

    [RelayCommand]
    private void AddPressStep()
    {
        var step = new ScriptStepViewModel { Index = ScriptSteps.Count, Action = ScriptActionKind.PressButton, Button = GamepadButton.A };
        ScriptSteps.Add(step);
        SelectedScriptStep = step;
        ReindexScriptSteps();
    }

    [RelayCommand]
    private void AddReleaseStep()
    {
        var step = new ScriptStepViewModel { Index = ScriptSteps.Count, Action = ScriptActionKind.ReleaseButton, Button = GamepadButton.A };
        ScriptSteps.Add(step);
        SelectedScriptStep = step;
        ReindexScriptSteps();
    }

    [RelayCommand]
    private void AddAxisStep()
    {
        var step = new ScriptStepViewModel { Index = ScriptSteps.Count, Action = ScriptActionKind.SetAxis, Axis = AnalogAxis.RightStickY };
        ScriptSteps.Add(step);
        SelectedScriptStep = step;
        ReindexScriptSteps();
    }

    [RelayCommand]
    private void DeleteScriptStep()
    {
        if (SelectedScriptStep is null) return;
        ScriptSteps.Remove(SelectedScriptStep);
        ReindexScriptSteps();
        SelectedScriptStep = ScriptSteps.LastOrDefault();
    }

    [RelayCommand]
    private void MoveScriptStepUp()
    {
        if (SelectedScriptStep is null) return;
        int idx = ScriptSteps.IndexOf(SelectedScriptStep);
        if (idx > 0)
        {
            ScriptSteps.Move(idx, idx - 1);
            ReindexScriptSteps();
        }
    }

    [RelayCommand]
    private void MoveScriptStepDown()
    {
        if (SelectedScriptStep is null) return;
        int idx = ScriptSteps.IndexOf(SelectedScriptStep);
        if (idx < ScriptSteps.Count - 1)
        {
            ScriptSteps.Move(idx, idx + 1);
            ReindexScriptSteps();
        }
    }

    [RelayCommand]
    private void DuplicateScriptStep()
    {
        if (SelectedScriptStep is null) return;
        var clone = ScriptStepViewModel.FromModel(SelectedScriptStep.ToModel(), ScriptSteps.Count);
        ScriptSteps.Insert(ScriptSteps.IndexOf(SelectedScriptStep) + 1, clone);
        ReindexScriptSteps();
        SelectedScriptStep = clone;
    }

    [RelayCommand]
    private void InsertRapidFireCombo()
    {
        // Press RT → Wait 40ms → Release RT → Wait 30ms (classic rapid fire)
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.LoopStart, RepeatCount = 0 });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.PressButton, Button = GamepadButton.None }); // RT via trigger
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.SetTrigger, Axis = AnalogAxis.RightTrigger, Value = 255 });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.Wait, DurationMs = 40 });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.SetTrigger, Axis = AnalogAxis.RightTrigger, Value = 0 });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.Wait, DurationMs = 30 });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.LoopBack, LoopTargetIndex = ScriptSteps.Count - 6 });
        ReindexScriptSteps();
    }

    [RelayCommand]
    private void InsertDropShotCombo()
    {
        // Press RT + Press B (crouch) simultaneously → Wait 50ms
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.SetTrigger, Axis = AnalogAxis.RightTrigger, Value = 255 });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.PressButton, Button = GamepadButton.B });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.Wait, DurationMs = 50 });
        ReindexScriptSteps();
    }

    [RelayCommand]
    private void InsertYYCancelCombo()
    {
        // Y → Wait 80ms → Y → Wait 80ms (weapon swap cancel)
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.PressButton, Button = GamepadButton.Y });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.Wait, DurationMs = 80 });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.ReleaseButton, Button = GamepadButton.Y });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.Wait, DurationMs = 40 });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.PressButton, Button = GamepadButton.Y });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.Wait, DurationMs = 80 });
        ScriptSteps.Add(new ScriptStepViewModel { Action = ScriptActionKind.ReleaseButton, Button = GamepadButton.Y });
        ReindexScriptSteps();
    }

    [RelayCommand]
    private void ClearAllScriptSteps()
    {
        ScriptSteps.Clear();
    }

    private void ReindexScriptSteps()
    {
        for (int i = 0; i < ScriptSteps.Count; i++)
            ScriptSteps[i].Index = i;
    }

    private void RefreshMacros()
    {
        Macros.Clear();
        foreach (var macro in _profileManager.ActiveProfile.Macros)
            Macros.Add(macro);
        SelectedMacro = Macros.FirstOrDefault();
    }
}

/// <summary>File envelope written/read by the import-export feature.</summary>
internal sealed record MacroExportFile(
    string Version,
    DateTimeOffset ExportedAt,
    string AppVersion,
    List<MacroDefinition> Macros)
{
    // Parameterless ctor required by System.Text.Json deserialization
    public MacroExportFile() : this("2", DateTimeOffset.UtcNow, "?", []) { }
}
