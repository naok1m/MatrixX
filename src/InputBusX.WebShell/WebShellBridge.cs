using System.Text.Json;
using System.Text.Json.Serialization;
using InputBusX.Application.Interfaces;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.Interfaces;
using InputBusX.Infrastructure.Input;

namespace InputBusX.WebShell;

public sealed class WebShellBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions PortableJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IInputPipeline _pipeline;
    private readonly IInputProvider _inputProvider;
    private readonly IProfileManager _profileManager;
    private readonly IConfigurationStore _configStore;
    private readonly ILogSink _logSink;
    private readonly XInputProvider _xInputProvider;
    private readonly IWeaponDetectionService _weaponDetection;
    private readonly IWeaponLibraryService _weaponLibrary;
    private readonly IMacroProcessor _macroProcessor;

    private readonly object _sync = new();
    private ShellState _state = new();
    private long _lastInputPublishTick;
    private int _inputPublishQueued;
    private int _latestLeftStickX;
    private int _latestLeftStickY;
    private int _latestRightStickX;
    private int _latestRightStickY;
    private int _latestLeftTrigger;
    private int _latestRightTrigger;
    private int _latestRawButtons;
    private int _latestOutputButtons;

    public event EventHandler<string>? StateChanged;

    public WebShellBridge(WebShellServices services)
    {
        _pipeline = services.Resolve<IInputPipeline>();
        _inputProvider = services.Resolve<IInputProvider>();
        _profileManager = services.Resolve<IProfileManager>();
        _configStore = services.Resolve<IConfigurationStore>();
        _logSink = services.Resolve<ILogSink>();
        _xInputProvider = services.Resolve<XInputProvider>();
        _weaponDetection = services.Resolve<IWeaponDetectionService>();
        _weaponLibrary = services.Resolve<IWeaponLibraryService>();
        _macroProcessor = services.Resolve<IMacroProcessor>();

        _state = BuildInitialState();

        _pipeline.RawInputReceived += OnRawInput;
        _pipeline.InputProcessed += OnProcessedInput;
        _inputProvider.DeviceConnected += device => UpdateState(s =>
            s with { ConnectedDevices = s.ConnectedDevices.Append(device.ToString()).Distinct().ToArray() });
        _inputProvider.DeviceDisconnected += device => UpdateState(s =>
            s with { ConnectedDevices = s.ConnectedDevices.Where(d => d != device.ToString()).ToArray() });
        _profileManager.ActiveProfileChanged += profile => UpdateState(s => s with
        {
            ActiveProfileId = profile.Id,
            ActiveProfileName = profile.Name,
            Profiles = BuildProfiles(),
            Filters = FilterState.From(profile.Filters),
            Macros = BuildMacros(profile),
            SelectedMacroId = SelectMacroId(profile, s.SelectedMacroId)
        });
        _configStore.ConfigurationChanged += _ => UpdateState(s => s with
        {
            Settings = SettingsState.From(_configStore.Load().General),
            WeaponDetection = BuildWeaponDetectionState()
        });
        _logSink.LogReceived += entry => UpdateState(s => s with
        {
            Logs = s.Logs.Append(LogState.From(entry)).TakeLast(250).ToArray()
        });
        _weaponDetection.WeaponChanged += weapon =>
        {
            _macroProcessor.SetWeaponProfile(weapon);
            UpdateState(s => s with
            {
                WeaponDetection = s.WeaponDetection with
                {
                    CurrentWeaponName = weapon?.Name ?? "None",
                    StatusMessage = s.WeaponDetection.IsRunning
                        ? weapon is null ? "Scanning HUD for a known weapon..." : $"Matched {weapon.Name}"
                        : "Detection is stopped."
                }
            });
        };
    }

    public async Task HandleAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();

        switch (type)
        {
            case "ready":
            case "getState":
                PublishState();
                break;
            case "toggleConnection":
                await ToggleConnectionAsync();
                break;
            case "setGameProfile":
                UpdateState(s => s with { GameProfile = ReadString(root, "value", s.GameProfile) });
                break;
            case "createProfile":
                CreateProfile(ReadString(root, "value", ""));
                break;
            case "duplicateProfile":
                DuplicateProfile(ReadString(root, "value", ""));
                break;
            case "deleteProfile":
                DeleteProfile(ReadString(root, "value", ""));
                break;
            case "activateProfile":
                ActivateProfile(ReadString(root, "value", ""));
                break;
            case "addProfileProcess":
                AddProcess(ReadString(root, "profileId", ""), ReadString(root, "value", ""));
                break;
            case "removeProfileProcess":
                RemoveProcess(ReadString(root, "profileId", ""), ReadString(root, "value", ""));
                break;
            case "saveFilters":
                SaveFilters(root);
                break;
            case "resetFilters":
                ResetFilters();
                break;
            case "saveSettings":
                SaveSettings(root);
                break;
            case "clearLogs":
                UpdateState(s => s with { Logs = [] });
                break;
            case "selectMacro":
                UpdateState(s => s with { SelectedMacroId = ReadString(root, "value", s.SelectedMacroId) });
                break;
            case "createMacro":
                CreateMacro();
                break;
            case "deleteMacro":
                DeleteMacro(ReadString(root, "value", ""));
                break;
            case "saveMacro":
                SaveMacro(root);
                break;
            case "exportMacro":
                ExportMacrosToFile(false);
                break;
            case "exportAllMacros":
                ExportMacrosToFile(true);
                break;
            case "importMacros":
                ImportMacrosFromFile();
                break;
            case "exportProfile":
                ExportProfileToFile(ReadString(root, "value", ""));
                break;
            case "importProfile":
                ImportProfileFromFile();
                break;
            case "toggleWeaponDetection":
                await ToggleWeaponDetectionAsync();
                break;
            case "saveWeaponDetection":
                SaveWeaponDetection(root);
                break;
            case "addWeapon":
                AddWeapon();
                break;
            case "removeWeapon":
                RemoveWeapon(ReadString(root, "value", ""));
                break;
            case "saveWeapon":
                SaveWeapon(root);
                break;
            case "saveWeaponSettings":
                SaveWeaponSettings(root);
                break;
            case "addWeaponFromLibrary":
                AddWeaponFromLibrary(ReadString(root, "value", ""));
                break;
            case "activateLibraryWeapon":
                ActivateLibraryWeapon(ReadString(root, "value", ""));
                break;
            case "selectWeaponRegion":
                await SelectWeaponRegionAsync();
                break;
            case "previewWeaponCapture":
                PreviewWeaponCapture(root);
                break;
            case "closeWeaponPreview":
                CloseWeaponPreview();
                break;
            case "testWeaponCapture":
                await TestWeaponCaptureAsync(root);
                break;
            case "captureWeaponReference":
                await CaptureWeaponReferenceAsync(ReadString(root, "value", ""));
                break;
            case "clearWeaponReferences":
                await ClearWeaponReferencesAsync(ReadString(root, "value", ""));
                break;
            case "selectWeaponCustomRegion":
                await SelectWeaponCustomRegionAsync(ReadString(root, "value", ""));
                break;
            case "previewWeaponCustomRegion":
                PreviewWeaponCustomRegion(ReadString(root, "value", ""));
                break;
            case "clearActiveWeapon":
                _macroProcessor.SetWeaponProfile(null);
                UpdateState(s => s with
                {
                    WeaponDetection = s.WeaponDetection with { CurrentWeaponName = "None" }
                });
                break;
            case "setWeaponSearch":
                UpdateState(s => s with
                {
                    WeaponDetection = s.WeaponDetection with
                    {
                        SearchText = ReadString(root, "value", s.WeaponDetection.SearchText),
                        LibraryWeapons = BuildLibraryWeapons(
                            ReadString(root, "value", s.WeaponDetection.SearchText),
                            s.WeaponDetection.SelectedGame,
                            s.WeaponDetection.SelectedCategory)
                    }
                });
                break;
            case "setWeaponGame":
                SetWeaponGame(ReadString(root, "value", ""));
                break;
            case "setWeaponCategory":
                SetWeaponCategory(ReadString(root, "value", ""));
                break;
        }
    }

    private ShellState BuildInitialState()
    {
        var config = _configStore.Load();
        var activeProfile = _profileManager.ActiveProfile;
        var selectedMacroId = activeProfile.Macros.FirstOrDefault()?.Id ?? "";

        return new ShellState
        {
            ActiveProfileId = activeProfile.Id,
            ActiveProfileName = activeProfile.Name,
            ConnectionStatus = "Disconnected",
            GameProfile = "Warzone",
            Profiles = BuildProfiles(),
            Filters = FilterState.From(activeProfile.Filters),
            Settings = SettingsState.From(config.General),
            Logs = _logSink.RecentEntries.Select(LogState.From).TakeLast(250).ToArray(),
            Macros = BuildMacros(activeProfile),
            SelectedMacroId = selectedMacroId,
            WeaponDetection = BuildWeaponDetectionState()
        };
    }

    private ProfileState[] BuildProfiles() => _profileManager.Profiles
        .Select(profile => ProfileState.From(profile, profile.Id == _profileManager.ActiveProfile.Id))
        .ToArray();

    private MacroState[] BuildMacros(Profile profile) => profile.Macros
        .Select(MacroState.From)
        .ToArray();

    private string SelectMacroId(Profile profile, string previousSelection)
    {
        if (profile.Macros.Any(m => m.Id == previousSelection))
        {
            return previousSelection;
        }

        return profile.Macros.FirstOrDefault()?.Id ?? "";
    }

    private WeaponDetectionState BuildWeaponDetectionState()
    {
        var settings = _configStore.Load().WeaponDetection;
        var selectedGame = _weaponLibrary.GetGames().FirstOrDefault() ?? "Warzone";
        var categories = _weaponLibrary.GetCategories(selectedGame);
        var selectedCategory = categories.FirstOrDefault() ?? "All";
        return new WeaponDetectionState
        {
            IsRunning = _weaponDetection.IsRunning,
            CurrentWeaponName = _weaponDetection.CurrentWeapon?.Name ?? "None",
            StatusMessage = _weaponDetection.IsRunning ? "Scanning HUD for a known weapon..." : "Detection is stopped.",
            CaptureX = settings.CaptureX,
            CaptureY = settings.CaptureY,
            CaptureWidth = settings.CaptureWidth,
            CaptureHeight = settings.CaptureHeight,
            IntervalMs = settings.IntervalMs,
            MatchThreshold = settings.MatchThreshold,
            Weapons = settings.Weapons.Select(WeaponState.From).ToArray(),
            Games = _weaponLibrary.GetGames().ToArray(),
            Categories = ["All", .. categories],
            SelectedGame = selectedGame,
            SelectedCategory = "All",
            SearchText = "",
            LibraryWeapons = BuildLibraryWeapons("", selectedGame, "All")
        };
    }

    private LibraryWeaponState[] BuildLibraryWeapons(string query, string game, string category)
    {
        var results = _weaponLibrary.Search(query, game, category == "All" ? null : category);
        return results.Select(LibraryWeaponState.From).ToArray();
    }

    private WeaponDetectionState RebuildWeaponState(Func<WeaponDetectionState, WeaponDetectionState>? amend = null)
    {
        var preserved = _state.WeaponDetection;
        var next = BuildWeaponDetectionState() with
        {
            IsRunning = _weaponDetection.IsRunning,
            CurrentWeaponName = _weaponDetection.CurrentWeapon?.Name ?? preserved.CurrentWeaponName,
            StatusMessage = _weaponDetection.IsRunning
                ? preserved.StatusMessage
                : preserved.StatusMessage is "Detection is stopped." or "" ? "Detection is stopped." : preserved.StatusMessage,
            PreviewImageDataUrl = preserved.PreviewImageDataUrl,
            PreviewTitle = preserved.PreviewTitle,
            TestCaptureResult = preserved.TestCaptureResult
        };

        return amend is null ? next : amend(next);
    }

    private async Task ToggleConnectionAsync()
    {
        ShellState snapshot;
        lock (_sync)
        {
            snapshot = _state;
            _state = _state with
            {
                IsConnecting = true,
                ConnectionStatus = snapshot.IsRunning ? "Stopping..." : "Connecting..."
            };
        }
        PublishState();

        try
        {
            if (snapshot.IsRunning)
            {
                await _pipeline.StopAsync();
                UpdateState(s => s with
                {
                    IsRunning = false,
                    IsConnecting = false,
                    ConnectionStatus = "Disconnected"
                });
                return;
            }

            await _pipeline.StartAsync(CancellationToken.None);
            var status = _pipeline.ViGEmAvailable
                ? _pipeline.VirtualXInputSlot is { } slot
                    ? $"Connected - virtual controller on slot {slot}"
                    : "Connected"
                : "Monitor Only - install ViGEmBus for output";

            UpdateState(s => s with
            {
                IsRunning = true,
                IsConnecting = false,
                ConnectionStatus = status
            });
        }
        catch (Exception ex)
        {
            UpdateState(s => s with
            {
                IsRunning = false,
                IsConnecting = false,
                ConnectionStatus = $"Error: {ex.Message}"
            });
        }
    }

    private async Task ToggleWeaponDetectionAsync()
    {
        var config = _configStore.Load();
        var settings = config.WeaponDetection;

        if (_weaponDetection.IsRunning)
        {
            await _weaponDetection.StopAsync();
            UpdateState(s => s with
            {
                WeaponDetection = s.WeaponDetection with
                {
                    IsRunning = false,
                    StatusMessage = "Detection is stopped."
                }
            });
            return;
        }

        await _weaponDetection.StartAsync(settings);
        UpdateState(s => s with
        {
            WeaponDetection = s.WeaponDetection with
            {
                IsRunning = true,
                StatusMessage = $"Detecting every {settings.IntervalMs} ms at threshold {settings.MatchThreshold:F2}"
            }
        });
    }

    private void CreateProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var profile = _profileManager.CreateProfile(name.Trim());
        UpdateState(s => s with
        {
            ActiveProfileId = profile.Id,
            ActiveProfileName = profile.Name,
            Profiles = BuildProfiles(),
            Filters = FilterState.From(profile.Filters),
            Macros = BuildMacros(profile),
            SelectedMacroId = SelectMacroId(profile, "")
        });
    }

    private void DuplicateProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        var source = _profileManager.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (source is null)
        {
            return;
        }

        _profileManager.DuplicateProfile(profileId, $"{source.Name} Copy");
        UpdateState(s => s with { Profiles = BuildProfiles() });
    }

    private void DeleteProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        var profile = _profileManager.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null || profile.IsDefault)
        {
            return;
        }

        _profileManager.DeleteProfile(profileId);
        var activeProfile = _profileManager.ActiveProfile;
        UpdateState(s => s with
        {
            ActiveProfileId = activeProfile.Id,
            ActiveProfileName = activeProfile.Name,
            Profiles = BuildProfiles(),
            Filters = FilterState.From(activeProfile.Filters),
            Macros = BuildMacros(activeProfile),
            SelectedMacroId = SelectMacroId(activeProfile, s.SelectedMacroId)
        });
    }

    private void ActivateProfile(string profileId)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            _profileManager.SetActiveProfile(profileId);
        }
    }

    private void AddProcess(string profileId, string processName)
    {
        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        var profile = _profileManager.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null)
        {
            return;
        }

        var trimmed = processName.Trim();
        if (!profile.AssociatedProcesses.Contains(trimmed))
        {
            profile.AssociatedProcesses.Add(trimmed);
            _profileManager.SaveProfile(profile);
            UpdateState(s => s with { Profiles = BuildProfiles() });
        }
    }

    private void RemoveProcess(string profileId, string processName)
    {
        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        var profile = _profileManager.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null)
        {
            return;
        }

        if (profile.AssociatedProcesses.Remove(processName))
        {
            _profileManager.SaveProfile(profile);
            UpdateState(s => s with { Profiles = BuildProfiles() });
        }
    }

    private void SaveFilters(JsonElement root)
    {
        var profile = _profileManager.ActiveProfile;
        var filters = profile.Filters;

        filters.LeftStickDeadzone = ReadDouble(root, "leftStickDeadzone", filters.LeftStickDeadzone);
        filters.RightStickDeadzone = ReadDouble(root, "rightStickDeadzone", filters.RightStickDeadzone);
        filters.LeftStickAntiDeadzone = ReadDouble(root, "leftStickAntiDeadzone", filters.LeftStickAntiDeadzone);
        filters.RightStickAntiDeadzone = ReadDouble(root, "rightStickAntiDeadzone", filters.RightStickAntiDeadzone);
        filters.TriggerDeadzone = ReadDouble(root, "triggerDeadzone", filters.TriggerDeadzone);
        filters.ResponseCurveExponent = ReadDouble(root, "responseCurveExponent", filters.ResponseCurveExponent);
        filters.SmoothingFactor = ReadDouble(root, "smoothingFactor", filters.SmoothingFactor);
        filters.SmoothingEnabled = ReadBool(root, "smoothingEnabled", filters.SmoothingEnabled);

        _profileManager.SaveProfile(profile);
        UpdateState(s => s with { Filters = FilterState.From(filters) });
    }

    private void ResetFilters()
    {
        var profile = _profileManager.ActiveProfile;
        profile.Filters = new FilterSettings();
        _profileManager.SaveProfile(profile);
        UpdateState(s => s with { Filters = FilterState.From(profile.Filters) });
    }

    private void SaveSettings(JsonElement root)
    {
        var config = _configStore.Load();
        var general = config.General;

        general.PollingRateMs = Math.Clamp(ReadInt(root, "pollingRateMs", general.PollingRateMs), 1, 100);
        general.MinimizeToTray = ReadBool(root, "minimizeToTray", general.MinimizeToTray);
        general.StartMinimized = ReadBool(root, "startMinimized", general.StartMinimized);
        general.AutoConnect = ReadBool(root, "autoConnect", general.AutoConnect);
        general.ShowNotifications = ReadBool(root, "showNotifications", general.ShowNotifications);
        general.LogLevel = ReadString(root, "logLevel", general.LogLevel);

        _configStore.Save(config);
        _xInputProvider.SetPollingRate(general.PollingRateMs);
        UpdateState(s => s with { Settings = SettingsState.From(general) });
    }

    private void CreateMacro()
    {
        var profile = _profileManager.ActiveProfile;
        var macro = new MacroDefinition
        {
            Name = $"New Macro {profile.Macros.Count + 1}",
            Type = MacroType.AutoPing
        };
        profile.Macros.Add(macro);
        _profileManager.SaveProfile(profile);
        _pipeline.InvalidateMacroCache();

        UpdateState(s => s with
        {
            Macros = BuildMacros(profile),
            SelectedMacroId = macro.Id,
            Profiles = BuildProfiles()
        });
    }

    private void DeleteMacro(string macroId)
    {
        if (string.IsNullOrWhiteSpace(macroId))
        {
            return;
        }

        var profile = _profileManager.ActiveProfile;
        var macro = profile.Macros.FirstOrDefault(m => m.Id == macroId);
        if (macro is null)
        {
            return;
        }

        profile.Macros.Remove(macro);
        _profileManager.SaveProfile(profile);
        _pipeline.InvalidateMacroCache();

        UpdateState(s => s with
        {
            Macros = BuildMacros(profile),
            SelectedMacroId = SelectMacroId(profile, s.SelectedMacroId == macroId ? "" : s.SelectedMacroId),
            Profiles = BuildProfiles()
        });
    }

    private void SaveMacro(JsonElement root)
    {
        var macroId = ReadString(root, "id", "");
        if (string.IsNullOrWhiteSpace(macroId))
        {
            return;
        }

        var profile = _profileManager.ActiveProfile;
        var macro = profile.Macros.FirstOrDefault(m => m.Id == macroId);
        if (macro is null)
        {
            return;
        }

        macro.Name = ReadString(root, "name", macro.Name);
        macro.Type = ParseEnum(ReadString(root, "macroType", macro.Type.ToString()), macro.Type);
        macro.Enabled = ReadBool(root, "enabled", macro.Enabled);
        macro.Priority = ReadInt(root, "priority", macro.Priority);
        macro.ToggleMode = ReadBool(root, "toggleMode", macro.ToggleMode);
        macro.DelayMs = ReadInt(root, "delayMs", macro.DelayMs);
        macro.IntervalMs = ReadInt(root, "intervalMs", macro.IntervalMs);
        macro.Intensity = ReadDouble(root, "intensity", macro.Intensity);
        macro.Loop = ReadBool(root, "loop", macro.Loop);
        macro.RandomizationFactor = ReadDouble(root, "randomizationFactor", macro.RandomizationFactor);
        macro.RecoilCompensationX = ReadInt(root, "recoilCompensationX", macro.RecoilCompensationX);
        macro.RecoilCompensationY = ReadInt(root, "recoilCompensationY", macro.RecoilCompensationY);
        macro.FlickStrength = ReadInt(root, "flickStrength", macro.FlickStrength);
        macro.FlickIntervalMs = ReadInt(root, "flickIntervalMs", macro.FlickIntervalMs);
        macro.JumpIntervalMs = ReadInt(root, "jumpIntervalMs", macro.JumpIntervalMs);
        macro.StrafeIntervalMs = ReadInt(root, "strafeIntervalMs", macro.StrafeIntervalMs);
        macro.StrafeAmplitude = ReadDouble(root, "strafeAmplitude", macro.StrafeAmplitude);
        macro.SlideCancelDelayMs = ReadInt(root, "slideCancelDelayMs", macro.SlideCancelDelayMs);
        macro.TriggerSource = ParseEnum(ReadString(root, "triggerSource", macro.TriggerSource.ToString()), macro.TriggerSource);
        macro.ActivationButton = ParseNullableButton(ReadString(root, "activationButton", macro.ActivationButton?.ToString() ?? "None"));
        macro.PingButton = ParseNullableButton(ReadString(root, "pingButton", macro.PingButton?.ToString() ?? "None"));
        if (macro.Type == MacroType.AutoPing && !macro.PingButton.HasValue)
        {
            macro.PingButton = GamepadButton.DPadUp;
        }

        macro.SourceButton = ParseNullableButton(ReadString(root, "sourceButton", macro.SourceButton?.ToString() ?? "None"));
        macro.TargetButton = ParseNullableButton(ReadString(root, "targetButton", macro.TargetButton?.ToString() ?? "None"));
        macro.CrouchButton = ParseEnum(ReadString(root, "crouchButton", macro.CrouchButton.ToString()), macro.CrouchButton);
        macro.JumpButton = ParseEnum(ReadString(root, "jumpButton", macro.JumpButton.ToString()), macro.JumpButton);
        macro.BreathButton = ParseEnum(ReadString(root, "breathButton", macro.BreathButton.ToString()), macro.BreathButton);
        macro.SlideButton = ParseEnum(ReadString(root, "slideButton", macro.SlideButton.ToString()), macro.SlideButton);
        macro.SlideCancelButton = ParseEnum(ReadString(root, "slideCancelButton", macro.SlideCancelButton.ToString()), macro.SlideCancelButton);

        if (root.TryGetProperty("motion", out var motionElem) && motionElem.ValueKind == JsonValueKind.Object)
        {
            ApplyMotion(motionElem, macro.Motion);
        }
        if (root.TryGetProperty("trackingAssist", out var taElem) && taElem.ValueKind == JsonValueKind.Object)
        {
            ApplyTrackingAssist(taElem, macro.TrackingAssist);
        }
        if (root.TryGetProperty("headAssist", out var haElem) && haElem.ValueKind == JsonValueKind.Object)
        {
            ApplyHeadAssist(haElem, macro.HeadAssist);
        }
        if (root.TryGetProperty("progressiveRecoil", out var prElem) && prElem.ValueKind == JsonValueKind.Object)
        {
            ApplyProgressiveRecoil(prElem, macro.ProgressiveRecoil);
        }
        if (root.TryGetProperty("crowBar", out var cbElem) && cbElem.ValueKind == JsonValueKind.Object)
        {
            ApplyCrowBar(cbElem, macro.CrowBar);
        }
        if (root.TryGetProperty("script", out var scriptElem) && scriptElem.ValueKind == JsonValueKind.Object)
        {
            ApplyScript(scriptElem, macro.Script);
        }

        _profileManager.SaveProfile(profile);
        _pipeline.InvalidateMacroCache();

        UpdateState(s => s with
        {
            Macros = BuildMacros(profile),
            Profiles = BuildProfiles(),
            SelectedMacroId = macro.Id
        });
    }

    private void SaveWeaponDetection(JsonElement root)
    {
        var config = _configStore.Load();
        var settings = config.WeaponDetection;
        settings.CaptureX = ReadInt(root, "captureX", settings.CaptureX);
        settings.CaptureY = ReadInt(root, "captureY", settings.CaptureY);
        settings.CaptureWidth = ReadInt(root, "captureWidth", settings.CaptureWidth);
        settings.CaptureHeight = ReadInt(root, "captureHeight", settings.CaptureHeight);
        settings.IntervalMs = ReadInt(root, "intervalMs", settings.IntervalMs);
        settings.MatchThreshold = ReadDouble(root, "matchThreshold", settings.MatchThreshold);
        _configStore.Save(config);

        UpdateState(s => s with
        {
            WeaponDetection = BuildWeaponDetectionState() with
            {
                IsRunning = s.WeaponDetection.IsRunning,
                CurrentWeaponName = s.WeaponDetection.CurrentWeaponName,
                StatusMessage = s.WeaponDetection.IsRunning
                    ? $"Detecting every {settings.IntervalMs} ms at threshold {settings.MatchThreshold:F2}"
                    : "Detection is stopped."
            }
        });
    }

    private void AddWeapon()
    {
        var config = _configStore.Load();
        config.WeaponDetection.Weapons.Add(new WeaponProfile());
        _configStore.Save(config);
        UpdateState(s => s with
        {
            WeaponDetection = BuildWeaponDetectionState() with
            {
                IsRunning = s.WeaponDetection.IsRunning,
                CurrentWeaponName = s.WeaponDetection.CurrentWeaponName
            }
        });
    }

    private void RemoveWeapon(string weaponId)
    {
        var config = _configStore.Load();
        var weapon = config.WeaponDetection.Weapons.FirstOrDefault(w => w.Id == weaponId);
        if (weapon is null)
        {
            return;
        }

        config.WeaponDetection.Weapons.Remove(weapon);
        _configStore.Save(config);
        UpdateState(s => s with
        {
            WeaponDetection = BuildWeaponDetectionState() with
            {
                IsRunning = s.WeaponDetection.IsRunning,
                CurrentWeaponName = s.WeaponDetection.CurrentWeaponName
            }
        });
    }

    private void SaveWeapon(JsonElement root)
    {
        var weaponId = ReadString(root, "id", "");
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            return;
        }

        var config = _configStore.Load();
        var weapon = config.WeaponDetection.Weapons.FirstOrDefault(w => w.Id == weaponId);
        if (weapon is null)
        {
            return;
        }

        weapon.Name = ReadString(root, "name", weapon.Name);
        weapon.RecoilCompensationX = ReadInt(root, "recoilCompensationX", weapon.RecoilCompensationX);
        weapon.RecoilCompensationY = ReadInt(root, "recoilCompensationY", weapon.RecoilCompensationY);
        weapon.Intensity = ReadDouble(root, "intensity", weapon.Intensity);
        weapon.RapidFireEnabled = ReadBool(root, "rapidFireEnabled", weapon.RapidFireEnabled);
        weapon.RapidFireIntervalMs = ReadInt(root, "rapidFireIntervalMs", weapon.RapidFireIntervalMs);
        weapon.UseCustomRegion = ReadBool(root, "useCustomRegion", weapon.UseCustomRegion);
        weapon.CaptureX = ReadInt(root, "captureX", weapon.CaptureX);
        weapon.CaptureY = ReadInt(root, "captureY", weapon.CaptureY);
        weapon.CaptureWidth = ReadInt(root, "captureWidth", weapon.CaptureWidth);
        weapon.CaptureHeight = ReadInt(root, "captureHeight", weapon.CaptureHeight);

        _configStore.Save(config);
        UpdateState(s => s with
        {
            WeaponDetection = BuildWeaponDetectionState() with
            {
                IsRunning = s.WeaponDetection.IsRunning,
                CurrentWeaponName = s.WeaponDetection.CurrentWeaponName
            }
        });
    }

    private void SaveWeaponSettings(JsonElement root)
    {
        var config = _configStore.Load();
        var settings = config.WeaponDetection;

        settings.CaptureX = ReadInt(root, "captureX", settings.CaptureX);
        settings.CaptureY = ReadInt(root, "captureY", settings.CaptureY);
        settings.CaptureWidth = ReadInt(root, "captureWidth", settings.CaptureWidth);
        settings.CaptureHeight = ReadInt(root, "captureHeight", settings.CaptureHeight);
        settings.IntervalMs = ReadInt(root, "intervalMs", settings.IntervalMs);
        settings.MatchThreshold = ReadDouble(root, "matchThreshold", settings.MatchThreshold);

        if (root.TryGetProperty("weapons", out var weaponsElement) && weaponsElement.ValueKind == JsonValueKind.Array)
        {
            settings.Weapons = weaponsElement.EnumerateArray()
                .Select(ParseWeapon)
                .ToList();
        }

        _configStore.Save(config);
        UpdateState(s => s with
        {
            WeaponDetection = RebuildWeaponState()
        });
    }

    private void AddWeaponFromLibrary(string libraryWeaponId)
    {
        var entry = _weaponLibrary.GetAll().FirstOrDefault(w => w.Id == libraryWeaponId);
        if (entry is null)
        {
            return;
        }

        var config = _configStore.Load();
        config.WeaponDetection.Weapons.Add(entry.ToWeaponProfile());
        _configStore.Save(config);
        UpdateState(s => s with
        {
            WeaponDetection = BuildWeaponDetectionState() with
            {
                IsRunning = s.WeaponDetection.IsRunning,
                CurrentWeaponName = s.WeaponDetection.CurrentWeaponName
            }
        });
    }

    private void ActivateLibraryWeapon(string libraryWeaponId)
    {
        var entry = _weaponLibrary.GetAll().FirstOrDefault(w => w.Id == libraryWeaponId);
        if (entry is null)
        {
            return;
        }

        var profile = entry.ToWeaponProfile();
        _macroProcessor.SetWeaponProfile(profile);
        UpdateState(s => s with
        {
            WeaponDetection = s.WeaponDetection with
            {
                CurrentWeaponName = profile.Name,
                StatusMessage = $"Manual weapon override: {profile.Name}"
            }
        });
    }

    private async Task SelectWeaponRegionAsync()
    {
        var selection = await ScreenRegionSelectorForm.SelectAsync();
        if (!selection.HasValue)
        {
            return;
        }

        var config = _configStore.Load();
        config.WeaponDetection.CaptureX = selection.Value.X;
        config.WeaponDetection.CaptureY = selection.Value.Y;
        config.WeaponDetection.CaptureWidth = selection.Value.Width;
        config.WeaponDetection.CaptureHeight = selection.Value.Height;
        _configStore.Save(config);

        UpdateState(s => s with
        {
            WeaponDetection = RebuildWeaponState()
        });
    }

    private void PreviewWeaponCapture(JsonElement root)
    {
        var settings = _configStore.Load().WeaponDetection;
        var x = ReadInt(root, "captureX", settings.CaptureX);
        var y = ReadInt(root, "captureY", settings.CaptureY);
        var width = ReadInt(root, "captureWidth", settings.CaptureWidth);
        var height = ReadInt(root, "captureHeight", settings.CaptureHeight);
        var dataUrl = CaptureRegionDataUrl(x, y, width, height);
        UpdateState(s => s with
        {
            WeaponDetection = s.WeaponDetection with
            {
                PreviewImageDataUrl = dataUrl,
                PreviewTitle = $"Region Preview ({x}, {y}, {width}x{height})"
            }
        });
    }

    private void CloseWeaponPreview()
    {
        UpdateState(s => s with
        {
            WeaponDetection = s.WeaponDetection with
            {
                PreviewImageDataUrl = "",
                PreviewTitle = ""
            }
        });
    }

    private async Task TestWeaponCaptureAsync(JsonElement root)
    {
        var config = _configStore.Load();
        var settings = config.WeaponDetection;
        settings.CaptureX = ReadInt(root, "captureX", settings.CaptureX);
        settings.CaptureY = ReadInt(root, "captureY", settings.CaptureY);
        settings.CaptureWidth = ReadInt(root, "captureWidth", settings.CaptureWidth);
        settings.CaptureHeight = ReadInt(root, "captureHeight", settings.CaptureHeight);
        settings.IntervalMs = ReadInt(root, "intervalMs", settings.IntervalMs);
        settings.MatchThreshold = ReadDouble(root, "matchThreshold", settings.MatchThreshold);
        var result = await _weaponDetection.TestCaptureAsync(settings);
        UpdateState(s => s with
        {
            WeaponDetection = s.WeaponDetection with
            {
                TestCaptureResult = result
            }
        });
    }

    private async Task CaptureWeaponReferenceAsync(string weaponId)
    {
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            return;
        }

        var config = _configStore.Load();
        var weapon = config.WeaponDetection.Weapons.FirstOrDefault(w => w.Id == weaponId);
        if (weapon is null)
        {
            return;
        }

        await _weaponDetection.CaptureReferenceAsync(config.WeaponDetection, weapon);
        _configStore.Save(config);
        UpdateState(s => s with
        {
            WeaponDetection = RebuildWeaponState(next => next with
            {
                StatusMessage = $"Reference captured for {weapon.Name}"
            })
        });
    }

    private async Task ClearWeaponReferencesAsync(string weaponId)
    {
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            return;
        }

        var config = _configStore.Load();
        var weapon = config.WeaponDetection.Weapons.FirstOrDefault(w => w.Id == weaponId);
        if (weapon is null)
        {
            return;
        }

        await _weaponDetection.ClearReferencesAsync(weapon);
        _configStore.Save(config);
        UpdateState(s => s with
        {
            WeaponDetection = RebuildWeaponState(next => next with
            {
                StatusMessage = $"References cleared for {weapon.Name}"
            })
        });
    }

    private async Task SelectWeaponCustomRegionAsync(string weaponId)
    {
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            return;
        }

        var selection = await ScreenRegionSelectorForm.SelectAsync();
        if (!selection.HasValue)
        {
            return;
        }

        var config = _configStore.Load();
        var weapon = config.WeaponDetection.Weapons.FirstOrDefault(w => w.Id == weaponId);
        if (weapon is null)
        {
            return;
        }

        weapon.UseCustomRegion = true;
        weapon.CaptureX = selection.Value.X;
        weapon.CaptureY = selection.Value.Y;
        weapon.CaptureWidth = selection.Value.Width;
        weapon.CaptureHeight = selection.Value.Height;
        _configStore.Save(config);

        UpdateState(s => s with
        {
            WeaponDetection = RebuildWeaponState(next => next with
            {
                StatusMessage = $"Custom region selected for {weapon.Name}"
            })
        });
    }

    private void PreviewWeaponCustomRegion(string weaponId)
    {
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            return;
        }

        var weapon = _configStore.Load().WeaponDetection.Weapons.FirstOrDefault(w => w.Id == weaponId);
        if (weapon is null)
        {
            return;
        }

        var dataUrl = CaptureRegionDataUrl(weapon.CaptureX, weapon.CaptureY, weapon.CaptureWidth, weapon.CaptureHeight);
        UpdateState(s => s with
        {
            WeaponDetection = s.WeaponDetection with
            {
                PreviewImageDataUrl = dataUrl,
                PreviewTitle = $"{weapon.Name} Preview"
            }
        });
    }

    private void SetWeaponGame(string game)
    {
        if (string.IsNullOrWhiteSpace(game))
        {
            return;
        }

        var categories = _weaponLibrary.GetCategories(game);
        UpdateState(s => s with
        {
            WeaponDetection = s.WeaponDetection with
            {
                SelectedGame = game,
                Categories = ["All", .. categories],
                SelectedCategory = "All",
                LibraryWeapons = BuildLibraryWeapons(s.WeaponDetection.SearchText, game, "All")
            }
        });
    }

    private void SetWeaponCategory(string category)
    {
        UpdateState(s => s with
        {
            WeaponDetection = s.WeaponDetection with
            {
                SelectedCategory = category,
                LibraryWeapons = BuildLibraryWeapons(
                    s.WeaponDetection.SearchText,
                    s.WeaponDetection.SelectedGame,
                    category)
            }
        });
    }

    private void OnRawInput(GamepadState state)
    {
        Volatile.Write(ref _latestLeftStickX, state.LeftStick.X);
        Volatile.Write(ref _latestLeftStickY, state.LeftStick.Y);
        Volatile.Write(ref _latestRightStickX, state.RightStick.X);
        Volatile.Write(ref _latestRightStickY, state.RightStick.Y);
        Volatile.Write(ref _latestLeftTrigger, state.LeftTrigger.Value);
        Volatile.Write(ref _latestRightTrigger, state.RightTrigger.Value);
        Volatile.Write(ref _latestRawButtons, (int)state.Buttons);
    }

    private void OnProcessedInput(GamepadState state)
    {
        Volatile.Write(ref _latestOutputButtons, (int)state.Buttons);

        var now = Environment.TickCount64;
        var lastPublish = Interlocked.Read(ref _lastInputPublishTick);
        if (now - lastPublish < 33 ||
            Interlocked.CompareExchange(ref _lastInputPublishTick, now, lastPublish) != lastPublish ||
            Interlocked.Exchange(ref _inputPublishQueued, 1) != 0)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { PublishInputState(); }
            finally { Volatile.Write(ref _inputPublishQueued, 0); }
        });
    }

    private void UpdateState(Func<ShellState, ShellState> update, bool publish = true)
    {
        lock (_sync)
        {
            _state = update(_state);
        }

        if (publish)
        {
            PublishState();
        }
    }

    private void PublishState()
    {
        ShellState snapshot;
        lock (_sync)
        {
            snapshot = _state;
        }

        StateChanged?.Invoke(this, JsonSerializer.Serialize(new { type = "state", payload = snapshot }, JsonOptions));
    }

    private void PublishInputState()
    {
        var leftStickX = Volatile.Read(ref _latestLeftStickX) / (double)short.MaxValue;
        var leftStickY = Volatile.Read(ref _latestLeftStickY) / (double)short.MaxValue;
        var rightStickX = Volatile.Read(ref _latestRightStickX) / (double)short.MaxValue;
        var rightStickY = Volatile.Read(ref _latestRightStickY) / (double)short.MaxValue;
        var leftTrigger = Volatile.Read(ref _latestLeftTrigger) / 255d;
        var rightTrigger = Volatile.Read(ref _latestRightTrigger) / 255d;
        var rawButtons = ButtonState.From((GamepadButton)Volatile.Read(ref _latestRawButtons));
        var outputButtons = ButtonState.From((GamepadButton)Volatile.Read(ref _latestOutputButtons));

        StateChanged?.Invoke(this, JsonSerializer.Serialize(new
        {
            type = "inputState",
            payload = new
            {
                LeftStickX = leftStickX,
                LeftStickY = leftStickY,
                RightStickX = rightStickX,
                RightStickY = rightStickY,
                LeftTrigger = leftTrigger,
                RightTrigger = rightTrigger,
                RawButtons = rawButtons,
                OutputButtons = outputButtons
            }
        }, JsonOptions));
    }

    private void ExportMacrosToFile(bool all)
    {
        var profile = _profileManager.ActiveProfile;
        var selectedId = _state.SelectedMacroId;

        List<MacroDefinition> toExport;
        string suggested;
        if (all)
        {
            if (profile.Macros.Count == 0)
            {
                ShowToast("warn", "No macros to export.");
                return;
            }
            toExport = [.. profile.Macros];
            suggested = "ReflexX_Macros_Export.matrixmacros";
        }
        else
        {
            var selected = profile.Macros.FirstOrDefault(m => m.Id == selectedId);
            if (selected is null)
            {
                ShowToast("warn", "Select a macro to export.");
                return;
            }
            toExport = [selected];
            suggested = $"{selected.Name.Replace(' ', '_')}.matrixmacros";
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Exportar Macros — ReflexX",
            Filter = "ReflexX Macros (*.matrixmacros)|*.matrixmacros|All files (*.*)|*.*",
            DefaultExt = "matrixmacros",
            FileName = suggested,
            AddExtension = true,
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            var export = new MacroExportFile("2", DateTimeOffset.UtcNow,
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?",
                toExport);
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(export, PortableJsonOptions));
            ShowToast("info", $"{toExport.Count} macro(s) exportado(s).");
        }
        catch (Exception ex)
        {
            ShowToast("error", $"Erro ao exportar: {ex.Message}");
        }
    }

    private void ImportMacrosFromFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Importar Macros — ReflexX",
            Filter = "ReflexX Macros (*.matrixmacros)|*.matrixmacros|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var file = JsonSerializer.Deserialize<MacroExportFile>(json, PortableJsonOptions);
            if (file?.Macros is not { Count: > 0 })
            {
                ShowToast("warn", "Arquivo vazio ou inválido.");
                return;
            }

            var profile = _profileManager.ActiveProfile;
            int added = 0;
            foreach (var m in file.Macros)
            {
                m.Id = Guid.NewGuid().ToString("N")[..8];
                profile.Macros.Add(m);
                added++;
            }
            _profileManager.SaveProfile(profile);
            _pipeline.InvalidateMacroCache();

            UpdateState(s => s with
            {
                Macros = BuildMacros(profile),
                SelectedMacroId = profile.Macros.LastOrDefault()?.Id ?? s.SelectedMacroId,
                Profiles = BuildProfiles()
            });
            ShowToast("info", $"{added} macro(s) importado(s).");
        }
        catch (Exception ex)
        {
            ShowToast("error", $"Erro ao importar: {ex.Message}");
        }
    }

    private void ExportProfileToFile(string profileId)
    {
        var profile = string.IsNullOrWhiteSpace(profileId)
            ? _profileManager.ActiveProfile
            : _profileManager.Profiles.FirstOrDefault(p => p.Id == profileId) ?? _profileManager.ActiveProfile;

        using var dialog = new SaveFileDialog
        {
            Title = "Exportar Perfil — ReflexX",
            Filter = "ReflexX Profile (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            FileName = $"{profile.Name.Replace(' ', '_')}.json",
            AddExtension = true,
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(profile, PortableJsonOptions));
            ShowToast("info", $"Profile '{profile.Name}' exportado.");
        }
        catch (Exception ex)
        {
            ShowToast("error", $"Erro ao exportar: {ex.Message}");
        }
    }

    private void ImportProfileFromFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Importar Perfil — ReflexX",
            Filter = "ReflexX Profile (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var imported = JsonSerializer.Deserialize<Profile>(json, PortableJsonOptions);
            if (imported is null)
            {
                ShowToast("error", "Arquivo de perfil inválido.");
                return;
            }

            var newProfile = _profileManager.CreateProfile(imported.Name);
            newProfile.Macros = imported.Macros;
            foreach (var process in imported.AssociatedProcesses)
            {
                newProfile.AssociatedProcesses.Add(process);
            }
            newProfile.Filters = imported.Filters;
            _profileManager.SaveProfile(newProfile);
            _pipeline.InvalidateMacroCache();

            UpdateState(s => s with
            {
                Profiles = BuildProfiles(),
                Macros = BuildMacros(_profileManager.ActiveProfile),
                Filters = FilterState.From(_profileManager.ActiveProfile.Filters)
            });
            ShowToast("info", $"Profile '{newProfile.Name}' importado.");
        }
        catch (Exception ex)
        {
            ShowToast("error", $"Erro ao importar: {ex.Message}");
        }
    }

    private void ShowToast(string level, string message)
    {
        StateChanged?.Invoke(this, JsonSerializer.Serialize(new
        {
            type = "toast",
            payload = new { level, message }
        }, JsonOptions));
    }

    private static void ApplyMotion(JsonElement root, MotionScript m)
    {
        m.Shape = ParseEnum(ReadString(root, "shape", m.Shape.ToString()), m.Shape);
        m.Target = ParseEnum(ReadString(root, "target", m.Target.ToString()), m.Target);
        m.RadiusXNorm = ReadDouble(root, "radiusXNorm", m.RadiusXNorm);
        m.RadiusYNorm = ReadDouble(root, "radiusYNorm", m.RadiusYNorm);
        m.RotationDeg = ReadDouble(root, "rotationDeg", m.RotationDeg);
        m.PeriodMs = ReadDouble(root, "periodMs", m.PeriodMs);
        m.DurationMs = ReadDouble(root, "durationMs", m.DurationMs);
        m.DirectionDeg = ReadDouble(root, "directionDeg", m.DirectionDeg);
        m.AmplitudeNorm = ReadDouble(root, "amplitudeNorm", m.AmplitudeNorm);
        m.StartPhaseDeg = ReadDouble(root, "startPhaseDeg", m.StartPhaseDeg);
        m.Clockwise = ReadBool(root, "clockwise", m.Clockwise);
        m.Easing = ParseEnum(ReadString(root, "easing", m.Easing.ToString()), m.Easing);
        m.IntensityMul = ReadDouble(root, "intensityMul", m.IntensityMul);
        m.Additive = ReadBool(root, "additive", m.Additive);
    }

    private static void ApplyTrackingAssist(JsonElement root, TrackingAssistConfig c)
    {
        c.Shape = ParseEnum(ReadString(root, "shape", c.Shape.ToString()), c.Shape);
        c.Target = ParseEnum(ReadString(root, "target", c.Target.ToString()), c.Target);
        c.BaseRadiusNorm = ReadDouble(root, "baseRadiusNorm", c.BaseRadiusNorm);
        c.MaxRadiusNorm = ReadDouble(root, "maxRadiusNorm", c.MaxRadiusNorm);
        c.PeriodMs = ReadDouble(root, "periodMs", c.PeriodMs);
        c.Clockwise = ReadBool(root, "clockwise", c.Clockwise);
        c.DeflectionThreshold = ReadDouble(root, "deflectionThreshold", c.DeflectionThreshold);
        c.ScaleCurve = ReadDouble(root, "scaleCurve", c.ScaleCurve);
        c.Easing = ParseEnum(ReadString(root, "easing", c.Easing.ToString()), c.Easing);
        c.IntensityMul = ReadDouble(root, "intensityMul", c.IntensityMul);
        c.FreeOrbit = ReadBool(root, "freeOrbit", c.FreeOrbit);
    }

    private static void ApplyHeadAssist(JsonElement root, HeadAssistConfig c)
    {
        if (root.TryGetProperty("shortRange", out var s) && s.ValueKind == JsonValueKind.Object)
            ApplyMotion(s, c.ShortRange);
        if (root.TryGetProperty("mediumRange", out var m) && m.ValueKind == JsonValueKind.Object)
            ApplyMotion(m, c.MediumRange);
        if (root.TryGetProperty("longRange", out var l) && l.ValueKind == JsonValueKind.Object)
            ApplyMotion(l, c.LongRange);
        c.DistanceSource = ParseEnum(ReadString(root, "distanceSource", c.DistanceSource.ToString()), c.DistanceSource);
        c.ShortHoldMsMax = ReadDouble(root, "shortHoldMsMax", c.ShortHoldMsMax);
        c.MediumHoldMsMax = ReadDouble(root, "mediumHoldMsMax", c.MediumHoldMsMax);
        c.DeflectionShortMax = ReadDouble(root, "deflectionShortMax", c.DeflectionShortMax);
        c.DeflectionMediumMax = ReadDouble(root, "deflectionMediumMax", c.DeflectionMediumMax);
        c.RecoilShortMax = ReadDouble(root, "recoilShortMax", c.RecoilShortMax);
        c.RecoilMediumMax = ReadDouble(root, "recoilMediumMax", c.RecoilMediumMax);
        c.WeightTrigger = ReadDouble(root, "weightTrigger", c.WeightTrigger);
        c.WeightDeflection = ReadDouble(root, "weightDeflection", c.WeightDeflection);
        c.WeightRecoil = ReadDouble(root, "weightRecoil", c.WeightRecoil);
        c.CycleButton = ParseNullableButton(ReadString(root, "cycleButton", c.CycleButton?.ToString() ?? "None"));
        c.ReFireCooldownMs = ReadInt(root, "reFireCooldownMs", c.ReFireCooldownMs);
        c.MinTriggerHoldMs = ReadInt(root, "minTriggerHoldMs", c.MinTriggerHoldMs);
        c.FireOnPress = ReadBool(root, "fireOnPress", c.FireOnPress);
        c.FireOnce = ReadBool(root, "fireOnce", c.FireOnce);
    }

    private static void ApplyProgressiveRecoil(JsonElement root, ProgressiveRecoilConfig c)
    {
        c.TotalAmmo = ReadInt(root, "totalAmmo", c.TotalAmmo);
        c.FullMagDurationMs = ReadDouble(root, "fullMagDurationMs", c.FullMagDurationMs);
        c.StartCompX = ReadInt(root, "startCompX", c.StartCompX);
        c.StartCompY = ReadInt(root, "startCompY", c.StartCompY);
        c.MidCompX = ReadInt(root, "midCompX", c.MidCompX);
        c.MidCompY = ReadInt(root, "midCompY", c.MidCompY);
        c.EndCompX = ReadInt(root, "endCompX", c.EndCompX);
        c.EndCompY = ReadInt(root, "endCompY", c.EndCompY);
        c.PhaseEasing = ParseEnum(ReadString(root, "phaseEasing", c.PhaseEasing.ToString()), c.PhaseEasing);
        c.NoiseFactor = ReadDouble(root, "noiseFactor", c.NoiseFactor);
        c.SensitivityScale = ReadDouble(root, "sensitivityScale", c.SensitivityScale);
    }

    private static void ApplyCrowBar(JsonElement root, CrowBarConfig c)
    {
        c.Mode = ParseEnum(ReadString(root, "mode", c.Mode.ToString()), c.Mode);
        c.BaseHtgValue = ReadInt(root, "baseHtgValue", c.BaseHtgValue);
        c.AssistFactor = ReadDouble(root, "assistFactor", c.AssistFactor);
        c.DeflectionThreshold = ReadDouble(root, "deflectionThreshold", c.DeflectionThreshold);
        c.DeflectionCurve = ReadDouble(root, "deflectionCurve", c.DeflectionCurve);
        c.MaxCompensation = ReadInt(root, "maxCompensation", c.MaxCompensation);
        c.NoiseFactor = ReadDouble(root, "noiseFactor", c.NoiseFactor);
        c.HtgScalePadrao = ReadDouble(root, "htgScalePadrao", c.HtgScalePadrao);
    }

    private static void ApplyScript(JsonElement root, ScriptDefinition d)
    {
        d.TriggerMode = ParseEnum(ReadString(root, "triggerMode", d.TriggerMode.ToString()), d.TriggerMode);
        d.AutoLoop = ReadBool(root, "autoLoop", d.AutoLoop);
        d.SpeedMultiplier = ReadDouble(root, "speedMultiplier", d.SpeedMultiplier);
        d.Description = ReadString(root, "description", d.Description);
        if (root.TryGetProperty("steps", out var stepsElem) && stepsElem.ValueKind == JsonValueKind.Array)
        {
            d.Steps = stepsElem.EnumerateArray().Select(ParseScriptStep).ToList();
        }
    }

    private static ScriptStep ParseScriptStep(JsonElement root)
    {
        var step = new ScriptStep
        {
            Action = ParseEnum(ReadString(root, "action", "Wait"), ScriptActionKind.Wait),
            Button = ParseNullableButton(ReadString(root, "button", "None")),
            Value = (short)ReadInt(root, "value", 0),
            DurationMs = ReadInt(root, "durationMs", 16),
            LoopTargetIndex = ReadInt(root, "loopTargetIndex", 0),
            RepeatCount = ReadInt(root, "repeatCount", 0),
            Label = ReadString(root, "label", ""),
            Disabled = ReadBool(root, "disabled", false),
        };
        var axisStr = ReadString(root, "axis", "None");
        step.Axis = string.Equals(axisStr, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(axisStr)
            ? null
            : ParseEnum<AnalogAxis>(axisStr, AnalogAxis.LeftStickX);
        return step;
    }

    private static bool ReadBool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static double ReadDouble(JsonElement root, string name, double fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var result)
            ? result
            : fallback;

    private static int ReadInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : fallback;

    private static string ReadString(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var value) ? value.GetString() ?? fallback : fallback;

    private static WeaponProfile ParseWeapon(JsonElement root) => new()
    {
        Id = ReadString(root, "id", Guid.NewGuid().ToString("N")[..8]),
        Name = ReadString(root, "name", "New Weapon"),
        RecoilCompensationX = ReadInt(root, "recoilCompensationX", 0),
        RecoilCompensationY = ReadInt(root, "recoilCompensationY", -5000),
        Intensity = ReadDouble(root, "intensity", 1.0),
        RapidFireEnabled = ReadBool(root, "rapidFireEnabled", false),
        RapidFireIntervalMs = ReadInt(root, "rapidFireIntervalMs", 50),
        UseCustomRegion = ReadBool(root, "useCustomRegion", false),
        CaptureX = ReadInt(root, "captureX", 1700),
        CaptureY = ReadInt(root, "captureY", 950),
        CaptureWidth = ReadInt(root, "captureWidth", 300),
        CaptureHeight = ReadInt(root, "captureHeight", 60),
        ReferenceImagePaths = root.TryGetProperty("referenceImagePaths", out var refs) && refs.ValueKind == JsonValueKind.Array
            ? refs.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToList()
            : []
    };

    private static string CaptureRegionDataUrl(int x, int y, int width, int height)
    {
        var screen = SystemInformation.VirtualScreen;
        width = Math.Clamp(width, 1, screen.Width);
        height = Math.Clamp(height, 1, screen.Height);
        x = Math.Clamp(x, screen.Left, screen.Right - width);
        y = Math.Clamp(y, screen.Top, screen.Bottom - height);

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : fallback;

    private static GamepadButton? ParseNullableButton(string value) =>
        string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value)
            ? null
            : ParseEnum(value, GamepadButton.None);
}

public sealed record ShellState
{
    public bool IsRunning { get; init; }
    public bool IsConnecting { get; init; }
    public string ConnectionStatus { get; init; } = "Disconnected";
    public string ActiveProfileId { get; init; } = "";
    public string ActiveProfileName { get; init; } = "Default";
    public string GameProfile { get; init; } = "Warzone";
    public string[] ConnectedDevices { get; init; } = [];
    public double LeftStickX { get; init; }
    public double LeftStickY { get; init; }
    public double RightStickX { get; init; }
    public double RightStickY { get; init; }
    public double LeftTrigger { get; init; }
    public double RightTrigger { get; init; }
    public ButtonState RawButtons { get; init; } = new();
    public ButtonState OutputButtons { get; init; } = new();
    public ProfileState[] Profiles { get; init; } = [];
    public FilterState Filters { get; init; } = new();
    public SettingsState Settings { get; init; } = new();
    public LogState[] Logs { get; init; } = [];
    public MacroState[] Macros { get; init; } = [];
    public string SelectedMacroId { get; init; } = "";
    public WeaponDetectionState WeaponDetection { get; init; } = new();
}

public sealed record ProfileState
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public int MacroCount { get; init; }
    public string[] AssociatedProcesses { get; init; } = [];

    public static ProfileState From(Profile profile, bool isActive) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        IsDefault = profile.IsDefault,
        IsActive = isActive,
        MacroCount = profile.Macros.Count,
        AssociatedProcesses = profile.AssociatedProcesses.ToArray()
    };
}

public sealed record FilterState
{
    public double LeftStickDeadzone { get; init; } = 0.05;
    public double RightStickDeadzone { get; init; } = 0.05;
    public double LeftStickAntiDeadzone { get; init; }
    public double RightStickAntiDeadzone { get; init; }
    public double TriggerDeadzone { get; init; } = 0.02;
    public double ResponseCurveExponent { get; init; } = 1.0;
    public double SmoothingFactor { get; init; }
    public bool SmoothingEnabled { get; init; }

    public static FilterState From(FilterSettings filters) => new()
    {
        LeftStickDeadzone = filters.LeftStickDeadzone,
        RightStickDeadzone = filters.RightStickDeadzone,
        LeftStickAntiDeadzone = filters.LeftStickAntiDeadzone,
        RightStickAntiDeadzone = filters.RightStickAntiDeadzone,
        TriggerDeadzone = filters.TriggerDeadzone,
        ResponseCurveExponent = filters.ResponseCurveExponent,
        SmoothingFactor = filters.SmoothingFactor,
        SmoothingEnabled = filters.SmoothingEnabled
    };
}

public sealed record SettingsState
{
    public int PollingRateMs { get; init; } = 1;
    public bool MinimizeToTray { get; init; } = true;
    public bool StartMinimized { get; init; }
    public bool AutoConnect { get; init; } = true;
    public bool ShowNotifications { get; init; } = true;
    public string LogLevel { get; init; } = "Information";

    public static SettingsState From(GeneralSettings settings) => new()
    {
        PollingRateMs = settings.PollingRateMs,
        MinimizeToTray = settings.MinimizeToTray,
        StartMinimized = settings.StartMinimized,
        AutoConnect = settings.AutoConnect,
        ShowNotifications = settings.ShowNotifications,
        LogLevel = settings.LogLevel
    };
}

public sealed record LogState
{
    public string Timestamp { get; init; } = "";
    public string Level { get; init; } = "";
    public string Category { get; init; } = "";
    public string Message { get; init; } = "";

    public static LogState From(LogEntry entry) => new()
    {
        Timestamp = entry.Timestamp.ToString("HH:mm:ss"),
        Level = entry.Level,
        Category = entry.Category,
        Message = entry.Message
    };
}

public sealed record MacroState
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public bool Enabled { get; init; }
    public int Priority { get; init; }
    public string ActivationButton { get; init; } = "None";
    public bool ToggleMode { get; init; }
    public int DelayMs { get; init; }
    public int IntervalMs { get; init; }
    public double Intensity { get; init; } = 1.0;
    public bool Loop { get; init; }
    public double RandomizationFactor { get; init; }
    public string TriggerSource { get; init; } = "RightTrigger";
    public int RecoilCompensationX { get; init; }
    public int RecoilCompensationY { get; init; } = -5000;
    public string PingButton { get; init; } = "None";
    public string SourceButton { get; init; } = "None";
    public string TargetButton { get; init; } = "None";
    public int FlickStrength { get; init; } = 32767;
    public int FlickIntervalMs { get; init; } = 8;
    public string CrouchButton { get; init; } = nameof(GamepadButton.B);
    public string JumpButton { get; init; } = nameof(GamepadButton.A);
    public int JumpIntervalMs { get; init; } = 500;
    public double StrafeAmplitude { get; init; } = 0.60;
    public int StrafeIntervalMs { get; init; } = 120;
    public string BreathButton { get; init; } = nameof(GamepadButton.LeftThumb);
    public string SlideButton { get; init; } = nameof(GamepadButton.B);
    public int SlideCancelDelayMs { get; init; } = 180;
    public string SlideCancelButton { get; init; } = nameof(GamepadButton.B);

    public MotionScriptState Motion { get; init; } = new();
    public TrackingAssistStateRec TrackingAssist { get; init; } = new();
    public HeadAssistStateRec HeadAssist { get; init; } = new();
    public ProgressiveRecoilStateRec ProgressiveRecoil { get; init; } = new();
    public CrowBarStateRec CrowBar { get; init; } = new();
    public ScriptDefinitionStateRec Script { get; init; } = new();

    public static MacroState From(MacroDefinition macro) => new()
    {
        Id = macro.Id,
        Name = macro.Name,
        Type = macro.Type.ToString(),
        Enabled = macro.Enabled,
        Priority = macro.Priority,
        ActivationButton = macro.ActivationButton?.ToString() ?? "None",
        ToggleMode = macro.ToggleMode,
        DelayMs = macro.DelayMs,
        IntervalMs = macro.IntervalMs,
        Intensity = macro.Intensity,
        Loop = macro.Loop,
        RandomizationFactor = macro.RandomizationFactor,
        TriggerSource = macro.TriggerSource.ToString(),
        RecoilCompensationX = macro.RecoilCompensationX,
        RecoilCompensationY = macro.RecoilCompensationY,
        PingButton = macro.PingButton?.ToString() ?? "None",
        SourceButton = macro.SourceButton?.ToString() ?? "None",
        TargetButton = macro.TargetButton?.ToString() ?? "None",
        FlickStrength = macro.FlickStrength,
        FlickIntervalMs = macro.FlickIntervalMs,
        CrouchButton = macro.CrouchButton.ToString(),
        JumpButton = macro.JumpButton.ToString(),
        JumpIntervalMs = macro.JumpIntervalMs,
        StrafeAmplitude = macro.StrafeAmplitude,
        StrafeIntervalMs = macro.StrafeIntervalMs,
        BreathButton = macro.BreathButton.ToString(),
        SlideButton = macro.SlideButton.ToString(),
        SlideCancelDelayMs = macro.SlideCancelDelayMs,
        SlideCancelButton = macro.SlideCancelButton.ToString(),
        Motion = MotionScriptState.From(macro.Motion),
        TrackingAssist = TrackingAssistStateRec.From(macro.TrackingAssist),
        HeadAssist = HeadAssistStateRec.From(macro.HeadAssist),
        ProgressiveRecoil = ProgressiveRecoilStateRec.From(macro.ProgressiveRecoil),
        CrowBar = CrowBarStateRec.From(macro.CrowBar),
        Script = ScriptDefinitionStateRec.From(macro.Script)
    };
}

public sealed record MotionScriptState
{
    public string Shape { get; init; } = nameof(ShapeKind.Flick);
    public string Target { get; init; } = nameof(StickTargetKind.Left);
    public double RadiusXNorm { get; init; } = 0.35;
    public double RadiusYNorm { get; init; } = 0.35;
    public double RotationDeg { get; init; }
    public double PeriodMs { get; init; } = 400;
    public double DurationMs { get; init; } = 140;
    public double DirectionDeg { get; init; } = 90;
    public double AmplitudeNorm { get; init; } = 0.55;
    public double StartPhaseDeg { get; init; }
    public bool Clockwise { get; init; } = true;
    public string Easing { get; init; } = nameof(EasingKind.EaseOutCubic);
    public double IntensityMul { get; init; } = 1.0;
    public bool Additive { get; init; } = true;

    public static MotionScriptState From(MotionScript m) => new()
    {
        Shape = m.Shape.ToString(),
        Target = m.Target.ToString(),
        RadiusXNorm = m.RadiusXNorm,
        RadiusYNorm = m.RadiusYNorm,
        RotationDeg = m.RotationDeg,
        PeriodMs = m.PeriodMs,
        DurationMs = m.DurationMs,
        DirectionDeg = m.DirectionDeg,
        AmplitudeNorm = m.AmplitudeNorm,
        StartPhaseDeg = m.StartPhaseDeg,
        Clockwise = m.Clockwise,
        Easing = m.Easing.ToString(),
        IntensityMul = m.IntensityMul,
        Additive = m.Additive,
    };
}

public sealed record TrackingAssistStateRec
{
    public string Shape { get; init; } = nameof(ShapeKind.Circle);
    public string Target { get; init; } = nameof(StickTargetKind.Right);
    public double BaseRadiusNorm { get; init; } = 0.08;
    public double MaxRadiusNorm { get; init; } = 0.25;
    public double PeriodMs { get; init; } = 120;
    public bool Clockwise { get; init; } = true;
    public double DeflectionThreshold { get; init; } = 0.10;
    public double ScaleCurve { get; init; } = 0.7;
    public string Easing { get; init; } = nameof(EasingKind.EaseInOutSine);
    public double IntensityMul { get; init; } = 1.0;
    public bool FreeOrbit { get; init; }

    public static TrackingAssistStateRec From(TrackingAssistConfig c) => new()
    {
        Shape = c.Shape.ToString(),
        Target = c.Target.ToString(),
        BaseRadiusNorm = c.BaseRadiusNorm,
        MaxRadiusNorm = c.MaxRadiusNorm,
        PeriodMs = c.PeriodMs,
        Clockwise = c.Clockwise,
        DeflectionThreshold = c.DeflectionThreshold,
        ScaleCurve = c.ScaleCurve,
        Easing = c.Easing.ToString(),
        IntensityMul = c.IntensityMul,
        FreeOrbit = c.FreeOrbit,
    };
}

public sealed record HeadAssistStateRec
{
    public MotionScriptState ShortRange { get; init; } = new();
    public MotionScriptState MediumRange { get; init; } = new();
    public MotionScriptState LongRange { get; init; } = new();
    public string DistanceSource { get; init; } = nameof(Domain.Enums.DistanceSource.Auto);
    public double ShortHoldMsMax { get; init; } = 150;
    public double MediumHoldMsMax { get; init; } = 500;
    public double DeflectionShortMax { get; init; } = 0.30;
    public double DeflectionMediumMax { get; init; } = 0.65;
    public double RecoilShortMax { get; init; } = 2500;
    public double RecoilMediumMax { get; init; } = 6000;
    public double WeightTrigger { get; init; } = 1.0;
    public double WeightDeflection { get; init; } = 1.0;
    public double WeightRecoil { get; init; } = 0.5;
    public string CycleButton { get; init; } = "None";
    public int ReFireCooldownMs { get; init; } = 250;
    public int MinTriggerHoldMs { get; init; } = 20;
    public bool FireOnPress { get; init; } = true;
    public bool FireOnce { get; init; } = true;

    public static HeadAssistStateRec From(HeadAssistConfig c) => new()
    {
        ShortRange = MotionScriptState.From(c.ShortRange),
        MediumRange = MotionScriptState.From(c.MediumRange),
        LongRange = MotionScriptState.From(c.LongRange),
        DistanceSource = c.DistanceSource.ToString(),
        ShortHoldMsMax = c.ShortHoldMsMax,
        MediumHoldMsMax = c.MediumHoldMsMax,
        DeflectionShortMax = c.DeflectionShortMax,
        DeflectionMediumMax = c.DeflectionMediumMax,
        RecoilShortMax = c.RecoilShortMax,
        RecoilMediumMax = c.RecoilMediumMax,
        WeightTrigger = c.WeightTrigger,
        WeightDeflection = c.WeightDeflection,
        WeightRecoil = c.WeightRecoil,
        CycleButton = c.CycleButton?.ToString() ?? "None",
        ReFireCooldownMs = c.ReFireCooldownMs,
        MinTriggerHoldMs = c.MinTriggerHoldMs,
        FireOnPress = c.FireOnPress,
        FireOnce = c.FireOnce,
    };
}

public sealed record ProgressiveRecoilStateRec
{
    public int TotalAmmo { get; init; } = 60;
    public double FullMagDurationMs { get; init; } = 2500;
    public int StartCompX { get; init; }
    public int StartCompY { get; init; } = -3000;
    public int MidCompX { get; init; }
    public int MidCompY { get; init; } = -5000;
    public int EndCompX { get; init; }
    public int EndCompY { get; init; } = -7000;
    public string PhaseEasing { get; init; } = nameof(EasingKind.Smoothstep);
    public double NoiseFactor { get; init; } = 0.15;
    public double SensitivityScale { get; init; } = 1.0;

    public static ProgressiveRecoilStateRec From(ProgressiveRecoilConfig c) => new()
    {
        TotalAmmo = c.TotalAmmo,
        FullMagDurationMs = c.FullMagDurationMs,
        StartCompX = c.StartCompX,
        StartCompY = c.StartCompY,
        MidCompX = c.MidCompX,
        MidCompY = c.MidCompY,
        EndCompX = c.EndCompX,
        EndCompY = c.EndCompY,
        PhaseEasing = c.PhaseEasing.ToString(),
        NoiseFactor = c.NoiseFactor,
        SensitivityScale = c.SensitivityScale,
    };
}

public sealed record CrowBarStateRec
{
    public string Mode { get; init; } = nameof(CrowBarMode.Padrao);
    public int BaseHtgValue { get; init; } = 16;
    public double AssistFactor { get; init; } = 0.90;
    public double DeflectionThreshold { get; init; } = 0.05;
    public double DeflectionCurve { get; init; } = 1.0;
    public int MaxCompensation { get; init; } = 10000;
    public double NoiseFactor { get; init; } = 0.10;
    public double HtgScalePadrao { get; init; } = 1.125;

    public static CrowBarStateRec From(CrowBarConfig c) => new()
    {
        Mode = c.Mode.ToString(),
        BaseHtgValue = c.BaseHtgValue,
        AssistFactor = c.AssistFactor,
        DeflectionThreshold = c.DeflectionThreshold,
        DeflectionCurve = c.DeflectionCurve,
        MaxCompensation = c.MaxCompensation,
        NoiseFactor = c.NoiseFactor,
        HtgScalePadrao = c.HtgScalePadrao,
    };
}

public sealed record ScriptStepStateRec
{
    public string Action { get; init; } = nameof(ScriptActionKind.Wait);
    public string Button { get; init; } = "None";
    public string Axis { get; init; } = "None";
    public int Value { get; init; }
    public int DurationMs { get; init; } = 16;
    public int LoopTargetIndex { get; init; }
    public int RepeatCount { get; init; }
    public string Label { get; init; } = "";
    public bool Disabled { get; init; }

    public static ScriptStepStateRec From(ScriptStep s) => new()
    {
        Action = s.Action.ToString(),
        Button = s.Button?.ToString() ?? "None",
        Axis = s.Axis?.ToString() ?? "None",
        Value = s.Value,
        DurationMs = s.DurationMs,
        LoopTargetIndex = s.LoopTargetIndex,
        RepeatCount = s.RepeatCount,
        Label = s.Label,
        Disabled = s.Disabled,
    };
}

public sealed record ScriptDefinitionStateRec
{
    public string TriggerMode { get; init; } = nameof(ScriptTriggerKind.WhileHeld);
    public bool AutoLoop { get; init; } = true;
    public double SpeedMultiplier { get; init; } = 1.0;
    public string Description { get; init; } = "";
    public ScriptStepStateRec[] Steps { get; init; } = [];

    public static ScriptDefinitionStateRec From(ScriptDefinition d) => new()
    {
        TriggerMode = d.TriggerMode.ToString(),
        AutoLoop = d.AutoLoop,
        SpeedMultiplier = d.SpeedMultiplier,
        Description = d.Description,
        Steps = d.Steps.Select(ScriptStepStateRec.From).ToArray(),
    };
}

public sealed record WeaponDetectionState
{
    public bool IsRunning { get; init; }
    public string CurrentWeaponName { get; init; } = "None";
    public string StatusMessage { get; init; } = "Detection is stopped.";
    public int CaptureX { get; init; } = 1700;
    public int CaptureY { get; init; } = 950;
    public int CaptureWidth { get; init; } = 300;
    public int CaptureHeight { get; init; } = 60;
    public int IntervalMs { get; init; } = 250;
    public double MatchThreshold { get; init; } = 0.80;
    public WeaponState[] Weapons { get; init; } = [];
    public string[] Games { get; init; } = [];
    public string[] Categories { get; init; } = [];
    public string SelectedGame { get; init; } = "";
    public string SelectedCategory { get; init; } = "All";
    public string SearchText { get; init; } = "";
    public LibraryWeaponState[] LibraryWeapons { get; init; } = [];
    public string PreviewImageDataUrl { get; init; } = "";
    public string PreviewTitle { get; init; } = "";
    public string TestCaptureResult { get; init; } = "";
}

public sealed record WeaponState
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int RecoilCompensationX { get; init; }
    public int RecoilCompensationY { get; init; } = -5000;
    public double Intensity { get; init; } = 1.0;
    public bool RapidFireEnabled { get; init; }
    public int RapidFireIntervalMs { get; init; } = 50;
    public bool UseCustomRegion { get; init; }
    public int CaptureX { get; init; } = 1700;
    public int CaptureY { get; init; } = 950;
    public int CaptureWidth { get; init; } = 300;
    public int CaptureHeight { get; init; } = 60;
    public int ReferenceCount { get; init; }
    public string[] ReferenceImagePaths { get; init; } = [];

    public static WeaponState From(WeaponProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        RecoilCompensationX = profile.RecoilCompensationX,
        RecoilCompensationY = profile.RecoilCompensationY,
        Intensity = profile.Intensity,
        RapidFireEnabled = profile.RapidFireEnabled,
        RapidFireIntervalMs = profile.RapidFireIntervalMs,
        UseCustomRegion = profile.UseCustomRegion,
        CaptureX = profile.CaptureX,
        CaptureY = profile.CaptureY,
        CaptureWidth = profile.CaptureWidth,
        CaptureHeight = profile.CaptureHeight,
        ReferenceCount = profile.ReferenceImagePaths.Count,
        ReferenceImagePaths = profile.ReferenceImagePaths.ToArray()
    };
}

public sealed record LibraryWeaponState
{
    public string Id { get; init; } = "";
    public string Game { get; init; } = "";
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public int RecoilCompensationY { get; init; } = -5000;
    public double Intensity { get; init; } = 1.0;

    public static LibraryWeaponState From(WeaponLibraryEntry entry) => new()
    {
        Id = entry.Id,
        Game = entry.Game,
        Category = entry.Category,
        Name = entry.Name,
        RecoilCompensationY = entry.RecoilCompensationY,
        Intensity = entry.Intensity
    };
}

public sealed record ButtonState
{
    public bool A { get; init; }
    public bool B { get; init; }
    public bool X { get; init; }
    public bool Y { get; init; }
    public bool Lb { get; init; }
    public bool Rb { get; init; }
    public bool Start { get; init; }
    public bool Back { get; init; }
    public bool DpadUp { get; init; }
    public bool DpadDown { get; init; }
    public bool DpadLeft { get; init; }
    public bool DpadRight { get; init; }

    public static ButtonState From(GamepadButton buttons) => new()
    {
        A = (buttons & GamepadButton.A) != 0,
        B = (buttons & GamepadButton.B) != 0,
        X = (buttons & GamepadButton.X) != 0,
        Y = (buttons & GamepadButton.Y) != 0,
        Lb = (buttons & GamepadButton.LeftShoulder) != 0,
        Rb = (buttons & GamepadButton.RightShoulder) != 0,
        Start = (buttons & GamepadButton.Start) != 0,
        Back = (buttons & GamepadButton.Back) != 0,
        DpadUp = (buttons & GamepadButton.DPadUp) != 0,
        DpadDown = (buttons & GamepadButton.DPadDown) != 0,
        DpadLeft = (buttons & GamepadButton.DPadLeft) != 0,
        DpadRight = (buttons & GamepadButton.DPadRight) != 0
    };
}

internal sealed record MacroExportFile(
    string Version,
    DateTimeOffset ExportedAt,
    string AppVersion,
    List<MacroDefinition> Macros)
{
    public MacroExportFile() : this("2", DateTimeOffset.UtcNow, "?", []) { }
}
