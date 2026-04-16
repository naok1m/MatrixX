using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InputBusX.Application.Services;

public sealed class ProfileManagerService : IProfileManager
{
    private readonly IConfigurationStore _configStore;
    private readonly IProcessMonitor _processMonitor;
    private readonly ILogger<ProfileManagerService> _logger;
    private AppConfiguration _config;
    private Profile _activeProfile;

    public event Action<Profile>? ActiveProfileChanged;

    public Profile ActiveProfile => _activeProfile;
    public IReadOnlyList<Profile> Profiles => _config.Profiles.AsReadOnly();

    public ProfileManagerService(
        IConfigurationStore configStore,
        IProcessMonitor processMonitor,
        ILogger<ProfileManagerService> logger)
    {
        _configStore = configStore;
        _processMonitor = processMonitor;
        _logger = logger;
        _config = configStore.Load();

        if (_config.Profiles.Count == 0)
        {
            var defaultProfile = new Profile { Name = "Default", IsDefault = true };
            _config.Profiles.Add(defaultProfile);
            _config.ActiveProfileId = defaultProfile.Id;
            configStore.Save(_config);
        }

        _activeProfile = _config.Profiles.FirstOrDefault(p => p.Id == _config.ActiveProfileId)
                         ?? _config.Profiles[0];

        _configStore.ConfigurationChanged += OnConfigChanged;
    }

    public void SetActiveProfile(string profileId)
    {
        var profile = _config.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new ArgumentException($"Profile '{profileId}' not found");

        _activeProfile = profile;
        _config.ActiveProfileId = profileId;
        _configStore.Save(_config);
        ActiveProfileChanged?.Invoke(profile);
        _logger.LogInformation("Switched to profile: {ProfileName}", profile.Name);
    }

    public Profile CreateProfile(string name)
    {
        var profile = new Profile { Name = name };
        _config.Profiles.Add(profile);
        _configStore.Save(_config);
        _logger.LogInformation("Created profile: {ProfileName}", name);
        return profile;
    }

    public Profile DuplicateProfile(string profileId, string newName)
    {
        var source = _config.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new ArgumentException($"Profile '{profileId}' not found");

        var duplicate = source.Duplicate(newName);
        _config.Profiles.Add(duplicate);
        _configStore.Save(_config);
        _logger.LogInformation("Duplicated profile '{Source}' as '{New}'", source.Name, newName);
        return duplicate;
    }

    public void DeleteProfile(string profileId)
    {
        var profile = _config.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new ArgumentException($"Profile '{profileId}' not found");

        if (profile.IsDefault)
            throw new InvalidOperationException("Cannot delete the default profile");

        _config.Profiles.Remove(profile);

        if (_activeProfile.Id == profileId)
        {
            _activeProfile = _config.Profiles.First(p => p.IsDefault) ?? _config.Profiles[0];
            _config.ActiveProfileId = _activeProfile.Id;
            ActiveProfileChanged?.Invoke(_activeProfile);
        }

        _configStore.Save(_config);
        _logger.LogInformation("Deleted profile: {ProfileName}", profile.Name);
    }

    public void SaveProfile(Profile profile)
    {
        profile.ModifiedAt = DateTime.UtcNow;
        var idx = _config.Profiles.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0)
            _config.Profiles[idx] = profile;
        _configStore.Save(_config);
    }

    public void CheckProcessSwitch()
    {
        var processName = _processMonitor.GetForegroundProcessName();
        if (string.IsNullOrEmpty(processName)) return;

        var match = _config.Profiles.FirstOrDefault(p =>
            p.AssociatedProcesses.Any(proc =>
                processName.Contains(proc, StringComparison.OrdinalIgnoreCase)));

        if (match != null && match.Id != _activeProfile.Id)
        {
            _logger.LogInformation("Auto-switching to profile '{Profile}' for process '{Process}'",
                match.Name, processName);
            SetActiveProfile(match.Id);
        }
    }

    private void OnConfigChanged(AppConfiguration newConfig)
    {
        _config = newConfig;
        _activeProfile = _config.Profiles.FirstOrDefault(p => p.Id == _config.ActiveProfileId)
                         ?? _config.Profiles[0];
        ActiveProfileChanged?.Invoke(_activeProfile);
    }
}
