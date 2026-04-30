using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;

namespace InputBusX.Infrastructure.Input;

public sealed class InputDeviceDeduplicator
{
    private readonly Dictionary<string, InputDevice> _devices = new();
    private readonly HashSet<string> _suppressedDirectInputIds = new(StringComparer.OrdinalIgnoreCase);

    public bool Connect(InputDevice device)
    {
        _devices[device.Id] = device;
        Recalculate();
        return !IsSuppressed(device.Id);
    }

    public bool Disconnect(InputDevice device)
    {
        _devices.Remove(device.Id);
        Recalculate();
        return !IsSuppressed(device.Id);
    }

    public bool ShouldPublish(string deviceId) => !IsSuppressed(deviceId);

    public IReadOnlyList<InputDevice> FilterConnected(IEnumerable<InputDevice> devices) =>
        devices.Where(d => !IsSuppressed(d.Id)).ToList();

    private bool IsSuppressed(string deviceId) => _suppressedDirectInputIds.Contains(deviceId);

    private void Recalculate()
    {
        _suppressedDirectInputIds.Clear();

        var xinputDevices = _devices.Values
            .Where(d => d is { IsConnected: true, Type: DeviceType.XInput })
            .ToList();

        foreach (var dinput in _devices.Values.Where(d => d is { IsConnected: true, Type: DeviceType.DirectInput }))
        {
            if (xinputDevices.Any(xinput => IsSamePhysicalDevice(xinput, dinput)))
                _suppressedDirectInputIds.Add(dinput.Id);
        }
    }

    private static bool IsSamePhysicalDevice(InputDevice xinput, InputDevice dinput)
    {
        if (!string.IsNullOrWhiteSpace(xinput.PhysicalIdentity) &&
            string.Equals(xinput.PhysicalIdentity, dinput.PhysicalIdentity, StringComparison.OrdinalIgnoreCase))
            return true;

        return xinput.VendorId.HasValue &&
               xinput.ProductId.HasValue &&
               xinput.ProductGuid.HasValue &&
               xinput.VendorId == dinput.VendorId &&
               xinput.ProductId == dinput.ProductId &&
               xinput.ProductGuid == dinput.ProductGuid;
    }
}
