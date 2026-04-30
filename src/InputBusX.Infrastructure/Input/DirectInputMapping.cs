using System.Globalization;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.ValueObjects;
using SharpDX.DirectInput;

namespace InputBusX.Infrastructure.Input;

public sealed record DirectInputDeviceIdentity(
    ushort VendorId,
    ushort ProductId,
    Guid ProductGuid,
    Guid InstanceGuid,
    string Name,
    string? DevicePath = null)
{
    public string StableKey
    {
        get
        {
            var path = string.IsNullOrWhiteSpace(DevicePath)
                ? $"instance_{InstanceGuid:N}"
                : Normalize(DevicePath);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"hid:vid_{VendorId:x4}&pid_{ProductId:x4}:{path}");
        }
    }

    public bool IsSony => VendorId == 0x054C;

    public static DirectInputDeviceIdentity From(Guid productGuid, Guid instanceGuid, string name, string? devicePath = null)
    {
        var bytes = productGuid.ToByteArray();
        var vendorId = BitConverter.ToUInt16(bytes, 0);
        var productId = BitConverter.ToUInt16(bytes, 2);

        return new DirectInputDeviceIdentity(
            vendorId,
            productId,
            productGuid,
            instanceGuid,
            name.Trim('\0'),
            devicePath);
    }

    private static string Normalize(string value) =>
        value.Trim().Trim('\0').ToLowerInvariant();
}

public enum DirectInputAxisLayout
{
    SonyHid,
    Modern,
    Legacy,
}

public sealed record DirectInputProfile(
    string Name,
    DirectInputAxisLayout Layout,
    bool IsSony,
    bool IsExplicit)
{
    public static DirectInputProfile For(DirectInputDeviceIdentity identity)
    {
        if (identity.VendorId == 0x054C)
        {
            return identity.ProductId switch
            {
                0x05C4 => new DirectInputProfile("Sony DualShock 4", DirectInputAxisLayout.SonyHid, true, true),
                0x09CC => new DirectInputProfile("Sony DualShock 4 v2", DirectInputAxisLayout.SonyHid, true, true),
                0x0BA0 => new DirectInputProfile("Sony DualShock 4 Wireless Adapter", DirectInputAxisLayout.SonyHid, true, true),
                0x0CE6 => new DirectInputProfile("Sony DualSense", DirectInputAxisLayout.SonyHid, true, true),
                0x0DF2 => new DirectInputProfile("Sony DualSense Edge", DirectInputAxisLayout.SonyHid, true, true),
                _ => new DirectInputProfile("Sony DirectInput Gamepad", DirectInputAxisLayout.SonyHid, true, true),
            };
        }

        return identity.VendorId switch
        {
            0x2F24 => new DirectInputProfile("Flydigi DirectInput Gamepad", DirectInputAxisLayout.Modern, false, true),
            0x2DC8 => new DirectInputProfile("8BitDo DirectInput Gamepad", DirectInputAxisLayout.Modern, false, true),
            0x0C12 => new DirectInputProfile("Zeroplus DirectInput Gamepad", DirectInputAxisLayout.Modern, false, true),
            0x2563 => new DirectInputProfile("Betop DirectInput Gamepad", DirectInputAxisLayout.Modern, false, true),
            0x05AC => new DirectInputProfile("GameSir DirectInput Gamepad", DirectInputAxisLayout.Modern, false, true),
            0x046D => new DirectInputProfile("Logitech DirectInput Gamepad", DirectInputAxisLayout.Legacy, false, true),
            _ => new DirectInputProfile("Generic DirectInput Gamepad", DirectInputAxisLayout.Modern, false, false),
        };
    }
}

public sealed record DirectInputSnapshot(
    int X,
    int Y,
    int Z,
    int RotationX,
    int RotationY,
    int RotationZ,
    IReadOnlyList<int> Sliders,
    IReadOnlyList<bool> Buttons,
    IReadOnlyList<int> PointOfViewControllers)
{
    public static DirectInputSnapshot From(JoystickState state) =>
        new(
            state.X,
            state.Y,
            state.Z,
            state.RotationX,
            state.RotationY,
            state.RotationZ,
            state.Sliders,
            state.Buttons,
            state.PointOfViewControllers);
}

public sealed class DirectInputMappingSession
{
    private const int RequiredCalibrationSamples = 8;
    private const int AxisUnstableRange = 8192;
    private const int TriggerUnstableRange = 4096;
    private const int StickDeadzone = 2500;
    private const int StickHysteresisDeadzone = 3200;
    private const int TriggerDeadzone = 8;
    private const int TriggerHysteresisDeadzone = 12;

    private readonly DirectInputProfile _profile;
    private readonly AxisCalibration[] _axes = Enumerable.Range(0, 8)
        .Select(_ => new AxisCalibration())
        .ToArray();
    private int _sampleCount;
    private bool _finalized;
    private GamepadState _previous = GamepadState.Empty;

    public DirectInputMappingSession(DirectInputProfile profile)
    {
        _profile = profile;
    }

    public bool IsReady => _finalized;

    public GamepadState? TryMap(DirectInputSnapshot snapshot)
    {
        var raw = ReadAxes(snapshot);

        if (!_finalized)
        {
            for (var i = 0; i < _axes.Length; i++)
                _axes[i].Observe(raw[i]);

            _sampleCount++;
            if (_sampleCount < RequiredCalibrationSamples)
                return null;

            FinalizeCalibration();
        }

        var state = MapState(snapshot, raw);
        _previous = state;
        return state;
    }

    public static GamepadState MapAfterCalibration(
        DirectInputProfile profile,
        IEnumerable<DirectInputSnapshot> calibrationSnapshots,
        DirectInputSnapshot snapshot)
    {
        var session = new DirectInputMappingSession(profile);
        GamepadState? state = null;

        foreach (var calibrationSnapshot in calibrationSnapshots)
            state = session.TryMap(calibrationSnapshot);

        return session.TryMap(snapshot) ?? state ?? GamepadState.Empty;
    }

    private GamepadState MapState(DirectInputSnapshot snapshot, int[] raw)
    {
        var leftX = NormalizeStick(raw[0], _axes[0], _previous.LeftStick.X);
        var leftY = (short)-NormalizeStick(raw[1], _axes[1], (short)-_previous.LeftStick.Y);

        int rightXRaw;
        int rightYRaw;
        int leftTriggerRaw;
        int rightTriggerRaw;
        AxisCalibration rightXCalibration;
        AxisCalibration rightYCalibration;
        AxisCalibration leftTriggerCalibration;
        AxisCalibration rightTriggerCalibration;

        if (_profile.Layout == DirectInputAxisLayout.SonyHid)
        {
            rightXRaw = raw[2];
            rightYRaw = raw[5];
            leftTriggerRaw = raw[3];
            rightTriggerRaw = raw[4];
            rightXCalibration = _axes[2];
            rightYCalibration = _axes[5];
            leftTriggerCalibration = _axes[3];
            rightTriggerCalibration = _axes[4];
        }
        else if (_profile.Layout == DirectInputAxisLayout.Modern)
        {
            rightXRaw = raw[2];
            rightYRaw = raw[3];
            leftTriggerRaw = raw[4];
            rightTriggerRaw = raw[5];
            rightXCalibration = _axes[2];
            rightYCalibration = _axes[3];
            leftTriggerCalibration = _axes[4];
            rightTriggerCalibration = _axes[5];
        }
        else
        {
            leftTriggerRaw = raw[2];
            rightXRaw = raw[3];
            rightYRaw = raw[4];
            rightTriggerRaw = raw[5];
            leftTriggerCalibration = _axes[2];
            rightXCalibration = _axes[3];
            rightYCalibration = _axes[4];
            rightTriggerCalibration = _axes[5];
        }

        var rightX = NormalizeStick(rightXRaw, rightXCalibration, _previous.RightStick.X);
        var rightY = (short)-NormalizeStick(rightYRaw, rightYCalibration, (short)-_previous.RightStick.Y);
        var leftTrigger = NormalizeTrigger(leftTriggerRaw, leftTriggerCalibration, _previous.LeftTrigger.Value);
        var rightTrigger = NormalizeTrigger(rightTriggerRaw, rightTriggerCalibration, _previous.RightTrigger.Value);

        return new GamepadState
        {
            Buttons = MapButtons(snapshot),
            LeftStick = new StickPosition(leftX, leftY),
            RightStick = new StickPosition(rightX, rightY),
            LeftTrigger = new TriggerValue(leftTrigger),
            RightTrigger = new TriggerValue(rightTrigger),
            TimestampTicks = Environment.TickCount64
        };
    }

    private void FinalizeCalibration()
    {
        for (var i = 0; i < _axes.Length; i++)
        {
            var unstableRange = IsTriggerAxis(i)
                ? TriggerUnstableRange
                : AxisUnstableRange;
            _axes[i].Finalize(unstableRange);
        }

        _finalized = true;
    }

    private static int[] ReadAxes(DirectInputSnapshot snapshot) =>
    [
        ClampAxis(snapshot.X),
        ClampAxis(snapshot.Y),
        ClampAxis(snapshot.Z),
        ClampAxis(snapshot.RotationX),
        ClampAxis(snapshot.RotationY),
        ClampAxis(snapshot.RotationZ),
        ClampOptionalAxis(snapshot.Sliders, 0),
        ClampOptionalAxis(snapshot.Sliders, 1),
    ];

    private static GamepadButton MapButtons(DirectInputSnapshot snapshot)
    {
        var buttons = GamepadButton.None;

        void Btn(int index, GamepadButton flag)
        {
            if (index < snapshot.Buttons.Count && snapshot.Buttons[index])
                buttons |= flag;
        }

        Btn(0, GamepadButton.X);
        Btn(1, GamepadButton.A);
        Btn(2, GamepadButton.B);
        Btn(3, GamepadButton.Y);
        Btn(4, GamepadButton.LeftShoulder);
        Btn(5, GamepadButton.RightShoulder);
        Btn(8, GamepadButton.Back);
        Btn(9, GamepadButton.Start);
        Btn(10, GamepadButton.LeftThumb);
        Btn(11, GamepadButton.RightThumb);

        if (snapshot.PointOfViewControllers.Count == 0)
            return buttons;

        var pov = snapshot.PointOfViewControllers[0];
        if (pov == -1)
            return buttons;

        if (pov >= 31500 || pov <= 4500) buttons |= GamepadButton.DPadUp;
        if (pov >= 4500 && pov <= 13500) buttons |= GamepadButton.DPadRight;
        if (pov >= 13500 && pov <= 22500) buttons |= GamepadButton.DPadDown;
        if (pov >= 22500 && pov <= 31500) buttons |= GamepadButton.DPadLeft;

        return buttons;
    }

    private static short NormalizeStick(int raw, AxisCalibration calibration, short previous)
    {
        if (calibration.Disabled)
            return 0;

        var delta = raw - calibration.Center;
        var range = delta >= 0 ? 65535 - calibration.Center : calibration.Center;
        if (range <= 0)
            return 0;

        var scaled = (int)Math.Round(delta * (double)short.MaxValue / range);
        scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
        var threshold = previous == 0 ? StickHysteresisDeadzone : StickDeadzone;
        return Math.Abs(scaled) < threshold ? (short)0 : (short)scaled;
    }

    private static byte NormalizeTrigger(int raw, AxisCalibration calibration, byte previous)
    {
        if (calibration.Disabled)
            return 0;

        var rest = calibration.Center;
        var delta = rest >= 49152 ? rest - raw : raw - rest;
        var range = rest >= 49152 ? rest : 65535 - rest;
        if (range <= 0 || delta <= 0)
            return 0;

        var scaled = (int)Math.Round(delta * 255d / range);
        scaled = Math.Clamp(scaled, 0, 255);
        var threshold = previous == 0 ? TriggerHysteresisDeadzone : TriggerDeadzone;
        return scaled < threshold ? (byte)0 : (byte)scaled;
    }

    private static int ClampAxis(int value) => Math.Clamp(value, 0, 65535);

    private static int ClampOptionalAxis(IReadOnlyList<int> values, int index) =>
        index < values.Count ? ClampAxis(values[index]) : 32767;

    private bool IsTriggerAxis(int index) =>
        _profile.Layout == DirectInputAxisLayout.SonyHid
            ? index is 3 or 4
            : index >= 4;

    private sealed class AxisCalibration
    {
        private int _sum;
        private int _count;
        private int _min = int.MaxValue;
        private int _max = int.MinValue;

        public int Center { get; private set; } = 32767;
        public bool Disabled { get; private set; }

        public void Observe(int raw)
        {
            raw = ClampAxis(raw);
            _sum += raw;
            _count++;
            _min = Math.Min(_min, raw);
            _max = Math.Max(_max, raw);
        }

        public void Finalize(int unstableRange)
        {
            if (_count == 0)
            {
                Disabled = true;
                return;
            }

            Center = Math.Clamp((int)Math.Round(_sum / (double)_count), 0, 65535);
            Disabled = _max - _min > unstableRange;
        }
    }
}
