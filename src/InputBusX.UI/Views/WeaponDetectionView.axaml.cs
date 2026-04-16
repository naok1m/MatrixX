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
        BtnSelectRegion.Click += OnSelectRegionClick;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is WeaponDetectionViewModel vm)
                vm.OpenLibraryRequested += OnOpenLibraryRequested;
        };
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
