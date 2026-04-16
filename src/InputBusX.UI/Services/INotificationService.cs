namespace InputBusX.UI.Services;

public enum NotificationLevel { Info, Success, Warning, Error }

public record AppNotification(string Message, NotificationLevel Level);

public interface INotificationService
{
    void ShowSuccess(string message);
    void ShowError(string message);
    void ShowWarning(string message);
    void ShowInfo(string message);
    event Action<AppNotification>? NotificationReceived;
}
