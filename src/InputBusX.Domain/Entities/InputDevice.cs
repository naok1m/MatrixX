using InputBusX.Domain.Enums;

namespace InputBusX.Domain.Entities;

public sealed class InputDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public DeviceType Type { get; init; }
    public int PlayerIndex { get; init; }
    public bool IsConnected { get; set; }
    public DateTime LastSeen { get; set; }
    public string? PhysicalIdentity { get; init; }
    public ushort? VendorId { get; init; }
    public ushort? ProductId { get; init; }
    public Guid? ProductGuid { get; init; }
    public Guid? InstanceGuid { get; init; }
    public string? DevicePath { get; init; }

    public override string ToString() => $"[{Type}] {Name} (Player {PlayerIndex + 1})";
}
