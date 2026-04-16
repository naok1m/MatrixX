using System.Diagnostics;
using System.Runtime.InteropServices;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.Interfaces;
using InputBusX.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace InputBusX.Infrastructure.Input;

public sealed class XInputProvider : IInputProvider
{
    private readonly ILogger<XInputProvider> _logger;
    private readonly Dictionary<int, InputDevice> _devices = new();
    private readonly bool[] _wasConnected = new bool[4];
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private int _pollingRateMs = 1;
    private volatile int _excludedSlotMask = 0; // bitmask of XInput slots to skip (ViGEm virtual)

    public event Action<InputDevice>? DeviceConnected;
    public event Action<InputDevice>? DeviceDisconnected;
    public event Action<string, GamepadState>? StateUpdated;

    public XInputProvider(ILogger<XInputProvider> logger)
    {
        _logger = logger;
    }

    public void SetPollingRate(int ms) => _pollingRateMs = Math.Max(1, ms);

    public void ExcludeXInputSlots(int[] slots)
    {
        int mask = 0;
        foreach (var s in slots)
            if (s >= 0 && s < 4) mask |= (1 << s);
        _excludedSlotMask = mask;
        if (slots.Length > 0)
            _logger.LogInformation("XInput: excluding virtual slot(s) {Slots} from polling", string.Join(",", slots));
        else
            _logger.LogInformation("XInput: slot exclusion cleared");
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pollTask = Task.Factory.StartNew(
            () => PollLoop(_cts.Token),
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        _logger.LogInformation("XInput provider started (polling rate: {Rate}ms)", _pollingRateMs);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _logger.LogInformation("XInput provider stopped");
        return Task.CompletedTask;
    }

    public IReadOnlyList<InputDevice> GetConnectedDevices()
        => _devices.Values.Where(d => d.IsConnected).ToList();

    private void PollLoop(CancellationToken ct)
    {
        var state = new XInputNative.XINPUT_STATE();

        // Set Windows timer resolution to 1ms so Thread.Sleep(1) actually sleeps ~1ms
        // instead of the default ~15.6ms. This is required for ≥500Hz polling.
        WinMm.timeBeginPeriod(1);

        var sw = Stopwatch.StartNew();
        long targetTicks = sw.ElapsedTicks;
        long ticksPerMs = Stopwatch.Frequency / 1000L;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                for (int i = 0; i < 4; i++)
                {
                    // Skip this slot if it belongs to the ViGEm virtual controller
                    if ((_excludedSlotMask & (1 << i)) != 0) continue;

                    uint result = XInputNative.XInputGetState((uint)i, ref state);
                    bool connected = result == 0;

                    if (connected && !_wasConnected[i])
                    {
                        var device = new InputDevice
                        {
                            Id = $"xinput_{i}",
                            Name = $"Xbox Controller {i + 1}",
                            Type = DeviceType.XInput,
                            PlayerIndex = i,
                            IsConnected = true,
                            LastSeen = DateTime.UtcNow
                        };
                        _devices[i] = device;
                        _wasConnected[i] = true;
                        DeviceConnected?.Invoke(device);
                        _logger.LogInformation("Controller connected: {Device}", device);
                    }
                    else if (!connected && _wasConnected[i])
                    {
                        if (_devices.TryGetValue(i, out var device))
                        {
                            device.IsConnected = false;
                            DeviceDisconnected?.Invoke(device);
                            _logger.LogInformation("Controller disconnected: {Device}", device);
                        }
                        _wasConnected[i] = false;
                    }

                    if (connected)
                    {
                        var gamepadState = ConvertState(ref state);
                        StateUpdated?.Invoke($"xinput_{i}", gamepadState);
                    }
                }

                if (_pollingRateMs > 0)
                {
                    // High-resolution sleep: advance target by interval, sleep until ~0.5ms before,
                    // then spin-wait for the exact moment. This gives <0.1ms jitter at 1000Hz.
                    targetTicks += _pollingRateMs * ticksPerMs;
                    long sleepUntil = targetTicks - (ticksPerMs / 2); // stop sleeping 0.5ms early
                    long remaining = sleepUntil - sw.ElapsedTicks;
                    if (remaining > ticksPerMs)
                        Thread.Sleep((int)(remaining / ticksPerMs));
                    // Spin for the final sub-millisecond
                    while (sw.ElapsedTicks < targetTicks)
                        Thread.SpinWait(10);

                    // If we're running behind (e.g. heavy processing), reset target to now
                    // so we don't try to catch up with a burst of polls
                    if (sw.ElapsedTicks - targetTicks > 2 * _pollingRateMs * ticksPerMs)
                        targetTicks = sw.ElapsedTicks;
                }
            }
        }
        finally
        {
            WinMm.timeEndPeriod(1);
        }
    }

    private static GamepadState ConvertState(ref XInputNative.XINPUT_STATE state)
    {
        return new GamepadState
        {
            Buttons = (GamepadButton)state.Gamepad.wButtons,
            LeftStick = new StickPosition(state.Gamepad.sThumbLX, state.Gamepad.sThumbLY),
            RightStick = new StickPosition(state.Gamepad.sThumbRX, state.Gamepad.sThumbRY),
            LeftTrigger = new TriggerValue(state.Gamepad.bLeftTrigger),
            RightTrigger = new TriggerValue(state.Gamepad.bRightTrigger),
            TimestampTicks = Environment.TickCount64
        };
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

internal static class WinMm
{
    [DllImport("winmm.dll", SetLastError = true)]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", SetLastError = true)]
    public static extern uint timeEndPeriod(uint uPeriod);
}

internal static class XInputNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    public static extern uint XInputGetState(uint dwUserIndex, ref XINPUT_STATE pState);
}
