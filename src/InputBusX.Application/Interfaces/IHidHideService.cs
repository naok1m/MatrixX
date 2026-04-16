namespace InputBusX.Application.Interfaces;

public record HidControllerInfo(string InstanceId, string FriendlyName, bool IsHidden);

public interface IHidHideService
{
    bool IsAvailable { get; }

    /// <summary>Enumerate all physical HID game controllers (excludes ViGEm virtual devices).</summary>
    IReadOnlyList<HidControllerInfo> GetControllers();

    void HideDevice(string instanceId);
    void UnhideDevice(string instanceId);

    /// <summary>Hide ALL detected physical controllers at once (auto mode).</summary>
    void HidePhysicalControllers();
    void UnhidePhysicalControllers();

    /// <summary>
    /// Ensures this executable is in the HidHide whitelist using the correct
    /// NT device-path format. Safe to call at any time; no-op if already listed.
    /// Must be called on startup so the app can read hidden controllers even
    /// when they were left hidden from a previous session.
    /// </summary>
    void EnsureWhitelisted();
}
