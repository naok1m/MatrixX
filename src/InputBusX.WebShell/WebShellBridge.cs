using System.Text.Json;
using InputBusX.Application.Interfaces;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.Interfaces;
using InputBusX.Infrastructure.Input;

namespace InputBusX.WebShell;

public sealed class WebShellBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
    private long _lastUiTick;
    private long _lastInputPublishTick;

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
                PreviewWeaponCapture();
                break;
            case "closeWeaponPreview":
                CloseWeaponPreview();
                break;
            case "testWeaponCapture":
                await TestWeaponCaptureAsync();
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
            Name = $"Macro {profile.Macros.Count + 1}",
            Type = MacroType.NoRecoil,
            ActivationButton = GamepadButton.RightShoulder
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
        macro.SourceButton = ParseNullableButton(ReadString(root, "sourceButton", macro.SourceButton?.ToString() ?? "None"));
        macro.TargetButton = ParseNullableButton(ReadString(root, "targetButton", macro.TargetButton?.ToString() ?? "None"));
        macro.CrouchButton = ParseEnum(ReadString(root, "crouchButton", macro.CrouchButton.ToString()), macro.CrouchButton);
        macro.JumpButton = ParseEnum(ReadString(root, "jumpButton", macro.JumpButton.ToString()), macro.JumpButton);
        macro.BreathButton = ParseEnum(ReadString(root, "breathButton", macro.BreathButton.ToString()), macro.BreathButton);
        macro.SlideButton = ParseEnum(ReadString(root, "slideButton", macro.SlideButton.ToString()), macro.SlideButton);
        macro.SlideCancelButton = ParseEnum(ReadString(root, "slideCancelButton", macro.SlideCancelButton.ToString()), macro.SlideCancelButton);

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

    private void PreviewWeaponCapture()
    {
        var settings = _configStore.Load().WeaponDetection;
        var dataUrl = CaptureRegionDataUrl(settings.CaptureX, settings.CaptureY, settings.CaptureWidth, settings.CaptureHeight);
        UpdateState(s => s with
        {
            WeaponDetection = s.WeaponDetection with
            {
                PreviewImageDataUrl = dataUrl,
                PreviewTitle = "Region Preview"
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

    private async Task TestWeaponCaptureAsync()
    {
        var settings = _configStore.Load().WeaponDetection;
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
        if (Environment.TickCount64 - _lastUiTick < 16)
        {
            return;
        }

        _lastUiTick = Environment.TickCount64;

        UpdateState(s => s with
        {
            LeftStickX = state.LeftStick.X / (double)short.MaxValue,
            LeftStickY = state.LeftStick.Y / (double)short.MaxValue,
            RightStickX = state.RightStick.X / (double)short.MaxValue,
            RightStickY = state.RightStick.Y / (double)short.MaxValue,
            LeftTrigger = state.LeftTrigger.Normalized,
            RightTrigger = state.RightTrigger.Normalized,
            RawButtons = ButtonState.From(state.Buttons)
        }, publish: false);
    }

    private void OnProcessedInput(GamepadState state)
    {
        var now = Environment.TickCount64;
        var shouldPublish = now - Interlocked.Read(ref _lastInputPublishTick) >= 33;
        if (shouldPublish)
        {
            Interlocked.Exchange(ref _lastInputPublishTick, now);
        }

        UpdateState(s => s with { OutputButtons = ButtonState.From(state.Buttons) }, shouldPublish);
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
        SlideCancelButton = macro.SlideCancelButton.ToString()
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
