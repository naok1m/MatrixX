namespace InputBusX.Domain.Entities;

public sealed class AppConfiguration
{
    public string ActiveProfileId { get; set; } = "";
    public List<Profile> Profiles { get; set; } = [];
    public GeneralSettings General { get; set; } = new();
    public WeaponDetectionSettings WeaponDetection { get; set; } = new();
}

public sealed class GeneralSettings
{
    public int PollingRateMs { get; set; } = 1;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool AutoConnect { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
}
