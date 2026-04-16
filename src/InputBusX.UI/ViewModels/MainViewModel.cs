using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InputBusX.UI.Services;

namespace InputBusX.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase _currentView;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _statusText = "Ready";

    // Tab fade animation — bound to the ContentControl's Opacity
    [ObservableProperty] private double _contentOpacity = 1.0;

    // Update banner
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _latestVersion = "";

    /// <summary>Assembly version read from the .csproj &lt;Version&gt; tag.</summary>
    public string AppVersion { get; } =
        Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion.Split('+')[0]          // strip git-hash suffix
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "1.0.0";

    public DashboardViewModel Dashboard       { get; }
    public MacroEditorViewModel MacroEditor   { get; }
    public ProfilesViewModel    Profiles      { get; }
    public FiltersViewModel     Filters       { get; }
    public LogsViewModel        Logs          { get; }
    public WeaponDetectionViewModel WeaponDetection { get; }
    public SettingsViewModel    Settings      { get; }
    public ToastViewModel       Toast         { get; } = new();

    private bool _isFading;

    public MainViewModel(
        DashboardViewModel      dashboard,
        MacroEditorViewModel    macroEditor,
        ProfilesViewModel       profiles,
        FiltersViewModel        filters,
        LogsViewModel           logs,
        WeaponDetectionViewModel weaponDetection,
        SettingsViewModel       settings,
        INotificationService    notifications,
        IUpdateService          updateService)
    {
        Dashboard       = dashboard;
        MacroEditor     = macroEditor;
        Profiles        = profiles;
        Filters         = filters;
        Logs            = logs;
        WeaponDetection = weaponDetection;
        Settings        = settings;
        _currentView    = dashboard;

        notifications.NotificationReceived += Toast.Show;

        Dashboard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DashboardViewModel.ConnectionStatus)
                                or nameof(DashboardViewModel.IsRunning))
                StatusText = Dashboard.IsRunning
                    ? $"● {Dashboard.ConnectionStatus}"
                    : "○ Disconnected";
        };

        // Fire-and-forget: check for updates in the background
        _ = CheckUpdatesAsync(updateService);
    }

    // ── Tab navigation ────────────────────────────────────────────────────

    partial void OnSelectedTabIndexChanged(int value)
    {
        _ = FadeToViewAsync(value);
    }

    private async Task FadeToViewAsync(int index)
    {
        // If already mid-fade (e.g. rapid clicks), skip animation
        if (_isFading)
        {
            CurrentView    = IndexToView(index);
            ContentOpacity = 1.0;
            return;
        }

        _isFading      = true;
        ContentOpacity = 0.0;
        await Task.Delay(120);   // let the 120ms DoubleTransition finish
        CurrentView    = IndexToView(index);
        ContentOpacity = 1.0;
        _isFading      = false;
    }

    private ViewModelBase IndexToView(int index) => index switch
    {
        0 => Dashboard,
        1 => MacroEditor,
        2 => Profiles,
        3 => Filters,
        4 => WeaponDetection,
        5 => Logs,
        6 => Settings,
        _ => Dashboard
    };

    [RelayCommand]
    private void NavigateTo(string tab)
    {
        SelectedTabIndex = tab switch
        {
            "Dashboard" => 0,
            "Macros"    => 1,
            "Profiles"  => 2,
            "Filters"   => 3,
            "Weapons"   => 4,
            "Logs"      => 5,
            "Settings"  => 6,
            _           => 0
        };
    }

    // ── Global keyboard shortcut commands ────────────────────────────────
    // Wired via <Window.KeyBindings> in MainWindow.axaml.
    // Each delegate to the active tab's relevant command so the user
    // gets Ctrl+S / Ctrl+N / Delete without moving the mouse.

    [RelayCommand]
    private void SaveCurrent()
    {
        switch (SelectedTabIndex)
        {
            case 1: MacroEditor.SaveMacroCommand.Execute(null);  break;
            case 4: WeaponDetection.SaveCommand.Execute(null);   break;
            case 6: Settings.SaveCommand.Execute(null);          break;
        }
    }

    [RelayCommand]
    private void NewItem()
    {
        switch (SelectedTabIndex)
        {
            case 1: MacroEditor.AddMacroCommand.Execute(null);      break;
            case 2: Profiles.CreateProfileCommand.Execute(null);    break;
        }
    }

    [RelayCommand]
    private void DeleteCurrent()
    {
        switch (SelectedTabIndex)
        {
            case 1: MacroEditor.DeleteMacroCommand.Execute(null);   break;
            case 2: Profiles.DeleteProfileCommand.Execute(null);    break;
        }
    }

    // ── Update check ─────────────────────────────────────────────────────

    private async Task CheckUpdatesAsync(IUpdateService updateService)
    {
        var (available, version) = await updateService.CheckAsync().ConfigureAwait(false);
        if (available)
        {
            UpdateAvailable = true;
            LatestVersion   = version;
        }
    }
}
