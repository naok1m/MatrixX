using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using InputBusX.UI.Services;

namespace InputBusX.UI.ViewModels;

public partial class ToastViewModel : ObservableObject
{
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _background = "#1A2A1A";
    [ObservableProperty] private string _foreground = "#00FF9C";
    [ObservableProperty] private string _icon = "✓";

    private CancellationTokenSource? _dismissCts;

    public void Show(AppNotification notification)
    {
        // Cancel any pending auto-dismiss from previous toast
        _dismissCts?.Cancel();
        _dismissCts = new CancellationTokenSource();

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Message    = notification.Message;
            Background = notification.Level switch
            {
                NotificationLevel.Success => "#1A2A1A",
                NotificationLevel.Error   => "#2A1A1A",
                NotificationLevel.Warning => "#2A2210",
                _                         => "#141820"
            };
            Foreground = notification.Level switch
            {
                NotificationLevel.Success => "#00FF9C",
                NotificationLevel.Error   => "#FF6B6B",
                NotificationLevel.Warning => "#FFD93D",
                _                         => "#8AB4F8"
            };
            Icon = notification.Level switch
            {
                NotificationLevel.Success => "✓",
                NotificationLevel.Error   => "✕",
                NotificationLevel.Warning => "⚠",
                _                         => "ℹ"
            };
            IsVisible = true;
        });

        var token = _dismissCts.Token;
        _ = Task.Delay(3500, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                Dispatcher.UIThread.InvokeAsync(() => IsVisible = false);
        }, TaskScheduler.Default);
    }
}
