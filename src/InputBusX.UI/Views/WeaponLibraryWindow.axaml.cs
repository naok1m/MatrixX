using Avalonia.Controls;
using Avalonia.Interactivity;
using InputBusX.Domain.Entities;
using InputBusX.UI.ViewModels;

namespace InputBusX.UI.Views;

public partial class WeaponLibraryWindow : Window
{
    public WeaponLibraryWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
