namespace InputBusX.UI.Services;

public sealed class NotificationService : INotificationService
{
    public event Action<AppNotification>? NotificationReceived;

    public void ShowSuccess(string message) =>
        NotificationReceived?.Invoke(new AppNotification(message, NotificationLevel.Success));

    public void ShowError(string message) =>
        NotificationReceived?.Invoke(new AppNotification(message, NotificationLevel.Error));

    public void ShowWarning(string message) =>
        NotificationReceived?.Invoke(new AppNotification(message, NotificationLevel.Warning));

    public void ShowInfo(string message) =>
        NotificationReceived?.Invoke(new AppNotification(message, NotificationLevel.Info));
}
