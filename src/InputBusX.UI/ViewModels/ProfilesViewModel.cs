using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using InputBusX.UI.Services;
using Microsoft.Extensions.Logging;

namespace InputBusX.UI.ViewModels;

public partial class ProfilesViewModel : ViewModelBase
{
    private readonly IProfileManager      _profileManager;
    private readonly INotificationService _notifications;
    private readonly IFileDialogService   _fileDialog;
    private readonly ILogger<ProfilesViewModel> _logger;

    [ObservableProperty] private Profile? _selectedProfile;
    [ObservableProperty] private string _newProfileName     = "";
    [ObservableProperty] private string _associatedProcess  = "";

    public ObservableCollection<Profile> Profiles { get; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        Converters             = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProfilesViewModel(
        IProfileManager          profileManager,
        INotificationService     notifications,
        IFileDialogService       fileDialog,
        ILogger<ProfilesViewModel> logger)
    {
        _profileManager = profileManager;
        _notifications  = notifications;
        _fileDialog     = fileDialog;
        _logger         = logger;
        RefreshProfiles();
    }

    // ── Profile CRUD ──────────────────────────────────────────────────────

    [RelayCommand]
    private void CreateProfile()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            _notifications.ShowWarning("Enter a name for the new profile.");
            return;
        }

        try
        {
            var profile = _profileManager.CreateProfile(NewProfileName);
            Profiles.Add(profile);
            SelectedProfile = profile;
            NewProfileName  = "";
            _notifications.ShowSuccess($"Profile '{profile.Name}' created.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create profile");
            _notifications.ShowError($"Could not create profile: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        if (SelectedProfile is null) return;

        try
        {
            var dup = _profileManager.DuplicateProfile(SelectedProfile.Id, $"{SelectedProfile.Name} (Copy)");
            Profiles.Add(dup);
            SelectedProfile = dup;
            _notifications.ShowSuccess($"Profile duplicated as '{dup.Name}'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to duplicate profile");
            _notifications.ShowError($"Could not duplicate profile: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null || SelectedProfile.IsDefault)
        {
            _notifications.ShowWarning("Cannot delete the default profile.");
            return;
        }

        try
        {
            var name = SelectedProfile.Name;
            _profileManager.DeleteProfile(SelectedProfile.Id);
            RefreshProfiles();
            _notifications.ShowSuccess($"Profile '{name}' deleted.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot delete profile");
            _notifications.ShowError($"Could not delete profile: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ActivateProfile()
    {
        if (SelectedProfile is null) return;
        _profileManager.SetActiveProfile(SelectedProfile.Id);
        _notifications.ShowSuccess($"Active profile: {SelectedProfile.Name}");
    }

    // ── Process association ───────────────────────────────────────────────

    [RelayCommand]
    private void AddProcess()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(AssociatedProcess)) return;

        if (!SelectedProfile.AssociatedProcesses.Contains(AssociatedProcess))
        {
            SelectedProfile.AssociatedProcesses.Add(AssociatedProcess);
            _profileManager.SaveProfile(SelectedProfile);
        }
        AssociatedProcess = "";
    }

    [RelayCommand]
    private void RemoveProcess(string process)
    {
        if (SelectedProfile is null) return;
        SelectedProfile.AssociatedProcesses.Remove(process);
        _profileManager.SaveProfile(SelectedProfile);
    }

    // ── Import / Export ───────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExportProfile()
    {
        if (SelectedProfile is null)
        {
            _notifications.ShowWarning("Select a profile to export.");
            return;
        }

        try
        {
            var path = await _fileDialog.SaveFileAsync(
                "Export Profile",
                $"{SelectedProfile.Name}.json",
                ("MatrixX Profile", new[] { "*.json" }));

            if (path is null) return;   // user cancelled

            var json = JsonSerializer.Serialize(SelectedProfile, JsonOptions);
            await File.WriteAllTextAsync(path, json);
            _notifications.ShowSuccess($"Profile '{SelectedProfile.Name}' exported.");
            _logger.LogInformation("Profile exported to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export profile");
            _notifications.ShowError($"Export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportProfile()
    {
        try
        {
            var path = await _fileDialog.OpenFileAsync(
                "Import Profile",
                ("MatrixX Profile", new[] { "*.json" }));

            if (path is null) return;   // user cancelled

            var json     = await File.ReadAllTextAsync(path);
            var imported = JsonSerializer.Deserialize<Profile>(json, JsonOptions);
            if (imported is null)
            {
                _notifications.ShowError("Invalid profile file.");
                return;
            }

            // Create a fresh registered profile, then copy the imported data into it
            var newProfile       = _profileManager.CreateProfile(imported.Name);
            newProfile.Macros    = imported.Macros;
            newProfile.AssociatedProcesses = imported.AssociatedProcesses;
            newProfile.Filters   = imported.Filters;
            _profileManager.SaveProfile(newProfile);

            RefreshProfiles();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == newProfile.Id);
            _notifications.ShowSuccess($"Profile '{newProfile.Name}' imported.");
            _logger.LogInformation("Profile imported from {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import profile");
            _notifications.ShowError($"Import failed: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (var p in _profileManager.Profiles)
            Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _profileManager.ActiveProfile.Id)
                          ?? Profiles.FirstOrDefault();
    }
}
