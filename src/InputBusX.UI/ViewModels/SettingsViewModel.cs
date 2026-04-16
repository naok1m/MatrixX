using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InputBusX.Domain.Interfaces;
using InputBusX.Infrastructure.Input;
using Microsoft.Extensions.Logging;

namespace InputBusX.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationStore _configStore;
    private readonly XInputProvider _xInput;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private int _pollingRateMs;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _autoConnect;
    [ObservableProperty] private bool _showNotifications;
    [ObservableProperty] private string _logLevel = "Information";

    [ObservableProperty] private string _saveStatus = "";

    public IReadOnlyList<string> LogLevels { get; } =
        ["Verbose", "Debug", "Information", "Warning", "Error"];

    public SettingsViewModel(
        IConfigurationStore configStore,
        XInputProvider xInput,
        ILogger<SettingsViewModel> logger)
    {
        _configStore  = configStore;
        _xInput       = xInput;
        _logger       = logger;

        Load();
    }

    private void Load()
    {
        var cfg = _configStore.Load().General;
        PollingRateMs     = cfg.PollingRateMs;
        MinimizeToTray    = cfg.MinimizeToTray;
        StartMinimized    = cfg.StartMinimized;
        AutoConnect       = cfg.AutoConnect;
        ShowNotifications = cfg.ShowNotifications;
        LogLevel          = cfg.LogLevel;
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            var app = _configStore.Load();
            app.General.PollingRateMs     = Math.Clamp(PollingRateMs, 1, 100);
            app.General.MinimizeToTray    = MinimizeToTray;
            app.General.StartMinimized    = StartMinimized;
            app.General.AutoConnect       = AutoConnect;
            app.General.ShowNotifications = ShowNotifications;
            app.General.LogLevel          = LogLevel;
            _configStore.Save(app);

            // Apply polling rate immediately (no restart needed)
            _xInput.SetPollingRate(app.General.PollingRateMs);

            SaveStatus = "Saved ✓";
            _logger.LogInformation("General settings saved (polling={Rate}ms)", app.General.PollingRateMs);

            // Clear status after 3s (must dispatch to UI thread)
            _ = Task.Delay(3000).ContinueWith(
                _ => Dispatcher.UIThread.Post(() => SaveStatus = ""),
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            SaveStatus = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to save general settings");
        }
    }
}
