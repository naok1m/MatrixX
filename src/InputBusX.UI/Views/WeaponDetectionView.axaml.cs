using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using InputBusX.UI.Services;
using InputBusX.UI.ViewModels;

namespace InputBusX.UI.Views;

public partial class WeaponDetectionView : UserControl
{
    public WeaponDetectionView()
    {
        InitializeComponent();
        BtnSelectRegion.Click  += OnSelectRegionClick;
        BtnCopyOcrResult.Click += OnCopyOcrResultClick;

        // Every "Selecionar" button inside a weapon row shares this class —
        // intercept the click at the UserControl level instead of wiring
        // each button individually (they are created dynamically by the ItemTemplate).
        AddHandler(Button.ClickEvent, OnAnyButtonClick, handledEventsToo: false);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is WeaponDetectionViewModel vm)
                vm.OpenLibraryRequested += OnOpenLibraryRequested;
        };
    }

    // ── Copy OCR result to clipboard ──────────────────────────────────────

    private async void OnCopyOcrResultClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WeaponDetectionViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null || string.IsNullOrEmpty(vm.TestCaptureResult)) return;
        await clipboard.SetTextAsync(vm.TestCaptureResult);
    }

    // ── Region selector ───────────────────────────────────────────────────

    private async void OnSelectRegionClick(object? sender, RoutedEventArgs e)
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        var selector = new ScreenRegionSelectorWindow();
        selector.Show(parentWindow!);

        var result = await selector.SelectionTask;

        if (result.HasValue && DataContext is WeaponDetectionViewModel vm)
        {
            vm.CaptureX      = result.Value.X;
            vm.CaptureY      = result.Value.Y;
            vm.CaptureWidth  = result.Value.Width;
            vm.CaptureHeight = result.Value.Height;
        }
    }

    // ── Per-weapon region selector (bubbled from ItemsControl rows) ───────

    private async void OnAnyButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button btn) return;
        if (!btn.Classes.Contains("weapon-pick-region")) return;
        if (btn.CommandParameter is not WeaponProfileItemViewModel weapon) return;

        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        var selector = new ScreenRegionSelectorWindow();
        selector.Show(parentWindow!);

        var result = await selector.SelectionTask;
        if (!result.HasValue) return;

        weapon.CaptureX      = result.Value.X;
        weapon.CaptureY      = result.Value.Y;
        weapon.CaptureWidth  = result.Value.Width;
        weapon.CaptureHeight = result.Value.Height;
        weapon.UseCustomRegion = true;
    }

    // ── Weapon library picker ─────────────────────────────────────────────

    private async void OnOpenLibraryRequested()
    {
        if (DataContext is not WeaponDetectionViewModel detectionVm) return;

        var parentWindow = TopLevel.GetTopLevel(this) as Window;

        var libVm  = ServiceLocator.Resolve<WeaponLibraryViewModel>();
        var libWin = new WeaponLibraryWindow { DataContext = libVm };

        // When user picks a weapon, add it and close
        libVm.EntrySelected += entry =>
        {
            detectionVm.AddFromLibrary(entry);
            libWin.Close();
        };

        await libWin.ShowDialog(parentWindow!);
    }
}
