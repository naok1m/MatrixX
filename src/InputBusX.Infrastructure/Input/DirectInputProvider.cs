using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using InputBusX.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using SharpDX.DirectInput;
using DomainDeviceType = InputBusX.Domain.Enums.DeviceType;

namespace InputBusX.Infrastructure.Input;

public sealed class DirectInputProvider : IInputProvider
{
    private readonly ILogger<DirectInputProvider> _logger;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private int _pollingRateMs = 4;
    private int _nextIndex = 10;

    // Events fired from the poll thread — consumers must be thread-safe
    public event Action<InputDevice>? DeviceConnected;
    public event Action<InputDevice>? DeviceDisconnected;
    public event Action<string, GamepadState>? StateUpdated;

    public DirectInputProvider(ILogger<DirectInputProvider> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // All DirectInput COM objects are created and used exclusively on this LongRunning task.
        // Creating them on any other thread and then calling methods from here would cause
        // COM apartment violations that produce garbage state / phantom inputs.
        _pollTask = Task.Factory.StartNew(
            () => PollLoop(_cts.Token),
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_pollTask != null)
            await _pollTask.ConfigureAwait(false);
        _cts?.Dispose();
        _cts = null;
        _pollTask = null;
        _logger.LogInformation("DirectInput provider stopped");
    }

    public IReadOnlyList<InputDevice> GetConnectedDevices() => [];

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    // -----------------------------------------------------------------------
    // Poll loop — ALL DirectInput work happens here, on one dedicated thread
    // -----------------------------------------------------------------------

    private void PollLoop(CancellationToken ct)
    {
        DirectInput? di;
        try
        {
            di = new DirectInput();
            _logger.LogInformation("DirectInput provider started");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DirectInput not available — DInput controllers won't be read");
            return;
        }

        // Per-device state, all owned by this thread
        var active = new Dictionary<Guid, ActiveEntry>();
        var xinputVidPids = ScanXInputVidPids();
        int scanCounter = 0;
        int scanInterval = Math.Max(1, 2000 / _pollingRateMs);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Periodically scan for new devices
                if (scanCounter++ % scanInterval == 0)
                    ScanDevices(di, active, xinputVidPids);

                // Poll every active device
                foreach (var guid in active.Keys.ToList())
                {
                    if (!active.TryGetValue(guid, out var entry)) continue;

                    try
                    {
                        entry.Joystick.Poll();
                        var js = entry.Joystick.GetCurrentState();
                        StateUpdated?.Invoke(entry.Info.Id, ConvertState(js, entry.Layout));
                    }
                    catch (SharpDX.SharpDXException sdxEx) when (
                        unchecked((uint)sdxEx.ResultCode.Code) == 0x8007001E || // DIERR_INPUTLOST
                        unchecked((uint)sdxEx.ResultCode.Code) == 0x80040001)   // DIERR_NOTACQUIRED
                    {
                        // Device temporarily lost — try to re-acquire silently
                        try { entry.Joystick.Acquire(); }
                        catch { /* will be removed on next failure */ }
                    }
                    catch
                    {
                        // Device truly gone
                        active.Remove(guid);
                        entry.Info.IsConnected = false;
                        DeviceDisconnected?.Invoke(entry.Info);
                        _logger.LogInformation("DirectInput: disconnected [{Name}]", entry.Info.Name);
                        try { entry.Joystick.Unacquire(); entry.Joystick.Dispose(); } catch { }
                    }
                }

                if (_pollingRateMs > 0)
                    Thread.Sleep(_pollingRateMs);
            }
        }
        finally
        {
            foreach (var e in active.Values)
            {
                try { e.Joystick.Unacquire(); e.Joystick.Dispose(); } catch { }
            }
            active.Clear();
            di.Dispose();
        }
    }

    private void ScanDevices(
        DirectInput di,
        Dictionary<Guid, ActiveEntry> active,
        HashSet<(ushort vid, ushort pid)> xinputVidPids)
    {
        try
        {
            var instances = di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
            foreach (var inst in instances)
            {
                if (active.ContainsKey(inst.InstanceGuid)) continue;
                if (IsXInputDevice(inst.ProductGuid, xinputVidPids)) continue;
                TryAddDevice(di, active, inst);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DirectInput: error scanning devices");
        }
    }

    private void TryAddDevice(
        DirectInput di,
        Dictionary<Guid, ActiveEntry> active,
        DeviceInstance inst)
    {
        try
        {
            var joystick = new Joystick(di, inst.InstanceGuid);
            joystick.SetCooperativeLevel(
                GetDesktopWindow(),
                CooperativeLevel.Background | CooperativeLevel.NonExclusive);
            joystick.Acquire();

            var layout = DetectAxisLayout(inst.ProductGuid, inst.InstanceName);
            var info = new InputDevice
            {
                Id   = $"dinput_{inst.InstanceGuid:N}",
                Name = inst.InstanceName.Trim('\0'),
                Type = DomainDeviceType.DirectInput,
                PlayerIndex = _nextIndex++,
                IsConnected = true,
                LastSeen    = DateTime.UtcNow
            };

            active[inst.InstanceGuid] = new ActiveEntry(joystick, info, layout);
            DeviceConnected?.Invoke(info);
            _logger.LogInformation("DirectInput: connected [{Name}] layout={Layout}", info.Name, layout);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DirectInput: failed to open [{Name}]", inst.InstanceName);
        }
    }

    // -----------------------------------------------------------------------
    // Axis layout detection
    // -----------------------------------------------------------------------

    private enum AxisLayout
    {
        /// <summary>
        /// PS4 / PS5 / Flydigi Vader / most modern non-XInput gamepads.
        /// X=LStick.X  Y=LStick.Y  Z=RStick.X  Rx=RStick.Y  Ry=LT  Rz=RT
        /// </summary>
        Modern,

        /// <summary>
        /// Older generic DInput pads (Logitech, etc.).
        /// X=LStick.X  Y=LStick.Y  Z=LT  Rx=RStick.X  Ry=RStick.Y  Rz=RT
        /// </summary>
        Legacy,
    }

    private static AxisLayout DetectAxisLayout(Guid productGuid, string instanceName)
    {
        var b   = productGuid.ToByteArray();
        ushort vid = BitConverter.ToUInt16(b, 0);

        return vid switch
        {
            0x054C => AxisLayout.Modern,  // Sony (PS4, PS5)
            0x2F24 => AxisLayout.Modern,  // Flydigi (Vader 4 Pro, Apex)
            0x2DC8 => AxisLayout.Modern,  // 8BitDo
            0x0C12 => AxisLayout.Modern,  // Zeroplus / IFYOO
            0x2563 => AxisLayout.Modern,  // Betop / IFYOO
            0x05AC => AxisLayout.Modern,  // GameSir (some models use Apple VID)
            0x046D => AxisLayout.Legacy,  // Logitech
            _ => AxisLayout.Modern        // Default: modern layout is more common today
        };
    }

    // -----------------------------------------------------------------------
    // State conversion
    //
    // Modern layout (PS4 / PS5 / Flydigi Vader 4 / most modern non-XInput):
    //   X  = Left Stick X        Y  = Left Stick Y (inverted)
    //   Z  = Right Stick X       Rx = Right Stick Y (inverted)
    //   Ry = Left Trigger        Rz = Right Trigger
    //
    // Legacy layout (older DInput pads):
    //   X  = Left Stick X        Y  = Left Stick Y (inverted)
    //   Z  = Left Trigger        Rx = Right Stick X
    //   Ry = Right Stick Y (inv) Rz = Right Trigger
    //
    // All axis values from DInput: 0–65535 (sticks center at 32767).
    // Y-axis inverted: push up → value decreases → negate after normalizing.
    // -----------------------------------------------------------------------

    private static GamepadState ConvertState(JoystickState js, AxisLayout layout)
    {
        // Clamp to valid DInput range before any math — guards against
        // occasional garbage values from drivers or USB glitches.
        static int Clamp(int v) => Math.Clamp(v, 0, 65535);

        static short NormalizeStick(int v) =>
            (short)Math.Clamp((Clamp(v) - 32767L) * 32767L / 32767L, short.MinValue, short.MaxValue);

        static byte NormalizeTrigger(int v) =>
            (byte)Math.Clamp(Clamp(v) * 255L / 65535L, 0, 255);

        var buttons = Domain.Enums.GamepadButton.None;

        void Btn(int i, Domain.Enums.GamepadButton f)
        {
            if (i < js.Buttons.Length && js.Buttons[i]) buttons |= f;
        }

        Btn(0,  Domain.Enums.GamepadButton.X);             // Square  / West
        Btn(1,  Domain.Enums.GamepadButton.A);             // Cross   / South
        Btn(2,  Domain.Enums.GamepadButton.B);             // Circle  / East
        Btn(3,  Domain.Enums.GamepadButton.Y);             // Triangle / North
        Btn(4,  Domain.Enums.GamepadButton.LeftShoulder);  // L1 / LB
        Btn(5,  Domain.Enums.GamepadButton.RightShoulder); // R1 / RB
        Btn(8,  Domain.Enums.GamepadButton.Back);          // Share / Create
        Btn(9,  Domain.Enums.GamepadButton.Start);         // Options / Menu
        Btn(10, Domain.Enums.GamepadButton.LeftThumb);     // L3
        Btn(11, Domain.Enums.GamepadButton.RightThumb);    // R3

        // D-Pad from POV[0] in hundredths of a degree; -1 = centered
        if (js.PointOfViewControllers.Length > 0)
        {
            int pov = js.PointOfViewControllers[0];
            if (pov != -1)
            {
                if (pov >= 31500 || pov <= 4500)  buttons |= Domain.Enums.GamepadButton.DPadUp;
                if (pov >= 4500  && pov <= 13500) buttons |= Domain.Enums.GamepadButton.DPadRight;
                if (pov >= 13500 && pov <= 22500) buttons |= Domain.Enums.GamepadButton.DPadDown;
                if (pov >= 22500 && pov <= 31500) buttons |= Domain.Enums.GamepadButton.DPadLeft;
            }
        }

        StickPosition leftStick, rightStick;
        TriggerValue  leftTrigger, rightTrigger;

        if (layout == AxisLayout.Modern)
        {
            leftStick    = new StickPosition(NormalizeStick(js.X),         (short)-NormalizeStick(js.Y));
            rightStick   = new StickPosition(NormalizeStick(js.Z),         (short)-NormalizeStick(js.RotationX));
            leftTrigger  = new TriggerValue(NormalizeTrigger(js.RotationY));
            rightTrigger = new TriggerValue(NormalizeTrigger(js.RotationZ));
        }
        else // Legacy
        {
            leftStick    = new StickPosition(NormalizeStick(js.X),           (short)-NormalizeStick(js.Y));
            rightStick   = new StickPosition(NormalizeStick(js.RotationX),   (short)-NormalizeStick(js.RotationY));
            leftTrigger  = new TriggerValue(NormalizeTrigger(js.Z));
            rightTrigger = new TriggerValue(NormalizeTrigger(js.RotationZ));
        }

        return new GamepadState
        {
            Buttons        = buttons,
            LeftStick      = leftStick,
            RightStick     = rightStick,
            LeftTrigger    = leftTrigger,
            RightTrigger   = rightTrigger,
            TimestampTicks = Environment.TickCount64
        };
    }

    // -----------------------------------------------------------------------
    // XInput detection — skips devices already handled by XInputProvider
    // -----------------------------------------------------------------------

    private static bool IsXInputDevice(Guid productGuid, HashSet<(ushort, ushort)> xinputSet)
    {
        var b   = productGuid.ToByteArray();
        ushort vid = BitConverter.ToUInt16(b, 0);
        ushort pid = BitConverter.ToUInt16(b, 2);
        return xinputSet.Contains((vid, pid));
    }

    private HashSet<(ushort vid, ushort pid)> ScanXInputVidPids()
    {
        var result = new HashSet<(ushort, ushort)>();
        var hidIfaceGuid = new Guid("{4d1e55b2-f16f-11cf-88cb-001111000030}");

        var hDevInfo = NativeSetup.SetupDiGetClassDevs(
            ref hidIfaceGuid, null, IntPtr.Zero,
            NativeSetup.DIGCF_PRESENT | NativeSetup.DIGCF_DEVICEINTERFACE);

        if (hDevInfo == new IntPtr(-1)) return result;

        try
        {
            var ifaceData = new NativeSetup.SP_DEVICE_INTERFACE_DATA();
            ifaceData.cbSize = Marshal.SizeOf(ifaceData);

            for (uint i = 0; NativeSetup.SetupDiEnumDeviceInterfaces(
                     hDevInfo, IntPtr.Zero, ref hidIfaceGuid, i, ref ifaceData); i++)
            {
                var devInfoData = new NativeSetup.SP_DEVINFO_DATA();
                devInfoData.cbSize = Marshal.SizeOf(devInfoData);

                uint reqSize = 0;
                NativeSetup.SetupDiGetDeviceInterfaceDetail(
                    hDevInfo, ref ifaceData, IntPtr.Zero, 0, ref reqSize, IntPtr.Zero);
                if (reqSize == 0) continue;

                var ptr = Marshal.AllocHGlobal((int)reqSize);
                try
                {
                    Marshal.WriteInt32(ptr, IntPtr.Size == 8 ? 8 : 6);
                    if (!NativeSetup.SetupDiGetDeviceInterfaceDetail(
                            hDevInfo, ref ifaceData, ptr, reqSize, ref reqSize, ref devInfoData))
                        continue;

                    var idBuf = new char[512];
                    if (NativeSetup.CM_Get_Device_ID(devInfoData.DevInst, idBuf, 512, 0) != 0) continue;
                    var instanceId = new string(idBuf).TrimEnd('\0');

                    if (!instanceId.Contains("&IG_", StringComparison.OrdinalIgnoreCase)) continue;

                    var m = Regex.Match(instanceId,
                        @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                    if (!m.Success) continue;

                    result.Add((
                        Convert.ToUInt16(m.Groups[1].Value, 16),
                        Convert.ToUInt16(m.Groups[2].Value, 16)));
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DirectInput: failed to scan XInput VID/PID list");
        }
        finally
        {
            NativeSetup.SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        _logger.LogDebug("DirectInput: {Count} XInput VID/PID pair(s) excluded", result.Count);
        return result;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    // -----------------------------------------------------------------------
    // Internal types
    // -----------------------------------------------------------------------

    private sealed record ActiveEntry(Joystick Joystick, InputDevice Info, AxisLayout Layout);

    private static class NativeSetup
    {
        public const uint DIGCF_PRESENT = 0x2;
        public const uint DIGCF_DEVICEINTERFACE = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid, string? Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr DeviceInfoSet, IntPtr DeviceInfoData,
            ref Guid InterfaceClassGuid, uint MemberIndex,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize,
            ref uint RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize,
            ref uint RequiredSize, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern uint CM_Get_Device_ID(
            uint dnDevInst, char[] Buffer, uint BufferLen, uint ulFlags);
    }
}
