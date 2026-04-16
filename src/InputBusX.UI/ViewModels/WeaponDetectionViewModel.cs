using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using InputBusX.UI.Services;
using Microsoft.Extensions.Logging;

namespace InputBusX.UI.ViewModels;

public partial class WeaponDetectionViewModel : ViewModelBase
{
    private readonly IWeaponDetectionService _detection;
    private readonly IMacroProcessor _macroProcessor;
    private readonly IConfigurationStore _configStore;
    private readonly IWeaponLibraryService _library;
    private readonly INotificationService _notifications;
    private readonly ILogger<WeaponDetectionViewModel> _logger;

    // ── Detection state ───────────────────────────────────────────────────
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _currentWeaponName   = "None";
    [ObservableProperty] private string _statusMessage       = "Detection is stopped.";
    [ObservableProperty] private string _testCaptureResult   = "";
    [ObservableProperty] private Bitmap? _capturePreview;
    [ObservableProperty] private bool _capturePreviewVisible;

    // ── Capture region ────────────────────────────────────────────────────
    [ObservableProperty] private int _captureX = 1700;
    [ObservableProperty] private int _captureY = 950;
    [ObservableProperty] private int _captureWidth = 300;
    [ObservableProperty] private int _captureHeight = 60;
    [ObservableProperty] private int _intervalMs = 500;

    // ── Weapons list ──────────────────────────────────────────────────────
    public ObservableCollection<WeaponProfileItemViewModel> Weapons { get; } = [];

    /// <summary>
    /// Raised when the user clicks "Banco de Armas" — the View opens the
    /// library window and calls AddFromLibrary() with the chosen entry.
    /// </summary>
    public event Action? OpenLibraryRequested;

    public WeaponDetectionViewModel(
        IWeaponDetectionService detection,
        IMacroProcessor macroProcessor,
        IConfigurationStore configStore,
        IWeaponLibraryService library,
        INotificationService notifications,
        ILogger<WeaponDetectionViewModel> logger)
    {
        _detection      = detection;
        _macroProcessor = macroProcessor;
        _configStore    = configStore;
        _library        = library;
        _notifications  = notifications;
        _logger         = logger;

        _detection.WeaponChanged += OnWeaponChanged;

        LoadSettings();
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Commands
    // ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleDetection()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (IsRunning)
            {
                await _detection.StopAsync();
                IsRunning = false;
                StatusMessage = "Detection is stopped.";
                CurrentWeaponName = "None";
                _notifications.ShowInfo("Weapon detection stopped.");
            }
            else
            {
                var settings = BuildSettings();
                await _detection.StartAsync(settings);
                IsRunning = true;
                StatusMessage = $"Detecting every {settings.IntervalMs} ms…";
                _notifications.ShowSuccess("Weapon detection started.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle weapon detection");
            _notifications.ShowError($"Detection error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestCapture()
    {
        if (IsBusy) return;
        IsBusy = true;
        TestCaptureResult = "Capturing…";
        try
        {
            var settings = BuildSettings();
            var text = await _detection.TestCaptureAsync(settings);
            TestCaptureResult = string.IsNullOrWhiteSpace(text) ? "[no text detected]" : text;
        }
        catch (Exception ex)
        {
            TestCaptureResult = $"Error: {ex.Message}";
            _notifications.ShowError("Capture failed. Check the region coordinates.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PreviewCapture()
    {
        try
        {
            var bmp = await Task.Run(() =>
            {
                using var gdi = new System.Drawing.Bitmap(
                    Math.Max(1, CaptureWidth),
                    Math.Max(1, CaptureHeight));
                using var g = System.Drawing.Graphics.FromImage(gdi);
                g.CopyFromScreen(CaptureX, CaptureY, 0, 0,
                    new System.Drawing.Size(gdi.Width, gdi.Height));

                using var ms = new MemoryStream();
                gdi.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                return new Bitmap(ms);
            });

            CapturePreview?.Dispose();
            CapturePreview        = bmp;
            CapturePreviewVisible = true;
        }
        catch (Exception ex)
        {
            _notifications.ShowError($"Preview failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void HidePreview()
    {
        CapturePreviewVisible = false;
        CapturePreview?.Dispose();
        CapturePreview = null;
    }

    [RelayCommand]
    private void AddWeapon()
    {
        Weapons.Add(new WeaponProfileItemViewModel());
    }

    [RelayCommand]
    private void RemoveWeapon(WeaponProfileItemViewModel item)
    {
        Weapons.Remove(item);
    }

    /// <summary>Called by the View after the user picks a weapon from the library window.</summary>
    public void AddFromLibrary(WeaponLibraryEntry entry)
    {
        var profile = entry.ToWeaponProfile();
        Weapons.Add(new WeaponProfileItemViewModel(profile));
        _logger.LogInformation("Added weapon from library: {Name}", profile.Name);
    }

    [RelayCommand]
    private void OpenLibrary()
    {
        OpenLibraryRequested?.Invoke();
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            var cfg = _configStore.Load();
            cfg.WeaponDetection = BuildSettings();
            _configStore.Save(cfg);
            StatusMessage = "Settings saved ✓";
            _notifications.ShowSuccess("Weapon settings saved.");
            _logger.LogInformation("Weapon detection settings saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save weapon detection settings");
            _notifications.ShowError($"Save failed: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────

    private void LoadSettings()
    {
        var cfg = _configStore.Load();
        var s = cfg.WeaponDetection;

        CaptureX      = s.CaptureX;
        CaptureY      = s.CaptureY;
        CaptureWidth  = s.CaptureWidth;
        CaptureHeight = s.CaptureHeight;
        IntervalMs    = s.IntervalMs;

        Weapons.Clear();
        foreach (var w in s.Weapons)
            Weapons.Add(new WeaponProfileItemViewModel(w));
    }

    private WeaponDetectionSettings BuildSettings() => new()
    {
        Enabled       = IsRunning,
        CaptureX      = CaptureX,
        CaptureY      = CaptureY,
        CaptureWidth  = CaptureWidth,
        CaptureHeight = CaptureHeight,
        IntervalMs    = IntervalMs,
        Weapons       = Weapons.Select(w => w.ToEntity()).ToList()
    };

    private void OnWeaponChanged(WeaponProfile? weapon)
    {
        // OCR detection fires from a background thread — UI properties must be on UI thread
        _macroProcessor.SetWeaponProfile(weapon);  // thread-safe (just sets a field)
        Dispatcher.UIThread.Post(() =>
            CurrentWeaponName = weapon?.Name ?? "None");
    }
}
