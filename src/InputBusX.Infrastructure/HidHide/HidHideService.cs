using System.Runtime.InteropServices;
using System.Text;
using InputBusX.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Nefarius.Drivers.HidHide;

namespace InputBusX.Infrastructure.HidHide;

public sealed class HidHideService : IHidHideService
{
    private readonly ILogger<HidHideService> _logger;
    private readonly List<string> _autoHiddenDevices = new();
    private HidHideControlService? _client;

    // GUID_DEVINTERFACE_HID — enumerates only HID device interfaces
    private static Guid _hidInterfaceGuid = new("{4d1e55b2-f16f-11cf-88cb-001111000030}");

    public bool IsAvailable
    {
        get
        {
            try
            {
                _client ??= new HidHideControlService();
                return _client.IsInstalled;
            }
            catch { return false; }
        }
    }

    public HidHideService(ILogger<HidHideService> logger)
    {
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    public IReadOnlyList<HidControllerInfo> GetControllers()
    {
        if (!IsAvailable) return [];

        var blocklist = _client!.BlockedInstanceIds
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return EnumeratePhysicalGamepads()
            .Select(d => new HidControllerInfo(d.InstanceId, d.FriendlyName,
                blocklist.Contains(d.InstanceId)))
            .ToList();
    }

    public void HideDevice(string instanceId)
    {
        if (!IsAvailable) return;
        try
        {
            EnsureWhitelisted();
            var blocked = _client!.BlockedInstanceIds;
            if (!blocked.Any(b => string.Equals(b, instanceId, StringComparison.OrdinalIgnoreCase)))
            {
                _client.AddBlockedInstanceId(instanceId);
                _logger.LogInformation("HidHide: hidden {Id}", instanceId);
            }
            _client.IsActive = true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "HidHide: failed to hide {Id}", instanceId); }
    }

    public void UnhideDevice(string instanceId)
    {
        if (!IsAvailable) return;
        try
        {
            _client!.RemoveBlockedInstanceId(instanceId);
            if (!_client.BlockedInstanceIds.Any())
                _client.IsActive = false;
            _logger.LogInformation("HidHide: unhidden {Id}", instanceId);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "HidHide: failed to unhide {Id}", instanceId); }
    }

    public void HidePhysicalControllers()
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("HidHide not installed — install from github.com/nefarius/HidHide");
            return;
        }
        try
        {
            EnsureWhitelisted();
            var devices = EnumeratePhysicalGamepads().ToList();
            if (devices.Count == 0) { _logger.LogWarning("HidHide: no physical controllers found"); return; }

            var blocked = _client!.BlockedInstanceIds
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var d in devices)
            {
                if (blocked.Contains(d.InstanceId)) continue;
                _client.AddBlockedInstanceId(d.InstanceId);
                _autoHiddenDevices.Add(d.InstanceId);
                _logger.LogInformation("HidHide: hiding {Name} ({Id})", d.FriendlyName, d.InstanceId);
            }

            _client.IsActive = true;
            _logger.LogInformation("HidHide active");
        }
        catch (Exception ex) { _logger.LogWarning(ex, "HidHide: failed"); }
    }

    public void UnhidePhysicalControllers()
    {
        if (!IsAvailable || _autoHiddenDevices.Count == 0) return;
        try
        {
            foreach (var id in _autoHiddenDevices)
                _client!.RemoveBlockedInstanceId(id);
            _autoHiddenDevices.Clear();
            if (!_client!.BlockedInstanceIds.Any())
                _client.IsActive = false;
            _logger.LogInformation("HidHide: unhidden all");
        }
        catch (Exception ex) { _logger.LogWarning(ex, "HidHide: unhide failed"); }
    }

    // -----------------------------------------------------------------------
    // Device enumeration — uses GUID_DEVINTERFACE_HID + HidP_GetCaps to
    // accept only UsagePage=1 (Generic Desktop) Usage=4 (Joystick) or 5 (Gamepad)
    // -----------------------------------------------------------------------

    private record DeviceEntry(string InstanceId, string FriendlyName);

    private IEnumerable<DeviceEntry> EnumeratePhysicalGamepads()
    {
        var hDevInfo = SetupApi.SetupDiGetClassDevs(
            ref _hidInterfaceGuid, null, IntPtr.Zero,
            SetupApi.DIGCF_PRESENT | SetupApi.DIGCF_DEVICEINTERFACE);

        if (hDevInfo == new IntPtr(-1)) yield break;

        try
        {
            var ifaceData = new SetupApi.SP_DEVICE_INTERFACE_DATA();
            ifaceData.cbSize = Marshal.SizeOf(ifaceData);

            for (uint i = 0; SetupApi.SetupDiEnumDeviceInterfaces(
                     hDevInfo, IntPtr.Zero, ref _hidInterfaceGuid, i, ref ifaceData); i++)
            {
                // Step 1: get required buffer size
                uint requiredSize = 0;
                SetupApi.SetupDiGetDeviceInterfaceDetail(
                    hDevInfo, ref ifaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);
                if (requiredSize == 0) continue;

                var detailPtr = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize:
                    //   8 on 64-bit (DWORD cbSize padded to pointer alignment + WCHAR[1])
                    //   6 on 32-bit (DWORD + WCHAR)
                    Marshal.WriteInt32(detailPtr, IntPtr.Size == 8 ? 8 : 6);

                    var devInfoData = new SetupApi.SP_DEVINFO_DATA();
                    devInfoData.cbSize = Marshal.SizeOf(devInfoData);

                    if (!SetupApi.SetupDiGetDeviceInterfaceDetail(
                            hDevInfo, ref ifaceData, detailPtr, requiredSize,
                            ref requiredSize, ref devInfoData))
                        continue;

                    // DevicePath starts at byte offset 4 (after the DWORD cbSize)
                    var devicePath = Marshal.PtrToStringUni(detailPtr + 4);
                    if (string.IsNullOrEmpty(devicePath)) continue;

                    // Get instance ID first — needed for the game-controller check
                    var idBuf = new char[512];
                    if (CfgMgr32.CM_Get_Device_ID(devInfoData.DevInst, idBuf, 512, 0) != 0) continue;
                    var instanceId = new string(idBuf).TrimEnd('\0');

                    // Filter: only gamepads/joysticks.
                    // NOTE: pass instanceId so we can do a VID fallback when the device
                    // is already hidden (CreateFile would fail in that case).
                    if (!IsGameController(devicePath, instanceId)) continue;

                    // Get friendly name (fallback to device description)
                    var friendlyName =
                        GetDeviceString(hDevInfo, ref devInfoData, SetupApi.SPDRP_FRIENDLYNAME)
                        ?? GetDeviceString(hDevInfo, ref devInfoData, SetupApi.SPDRP_DEVICEDESC)
                        ?? instanceId;

                    // Skip ViGEm virtual devices (two independent checks).
                    // 1) Walk the parent chain for VIGEMBUS — catches all ViGEm types.
                    // 2) Fast path: Xbox 360 virtual has "&IG_" in instance ID AND its
                    //    parent is ViGEm, so the ancestor walk handles it too. But we
                    //    also check the instance ID directly for an extra safety net.
                    if (IsViGEmAncestor(devInfoData.DevInst)) continue;

                    _logger.LogDebug("HidHide: found controller [{Name}] ({Id})", friendlyName, instanceId);
                    yield return new DeviceEntry(instanceId, friendlyName);
                }
                finally
                {
                    Marshal.FreeHGlobal(detailPtr);
                }
            }
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(hDevInfo);
        }
    }

    /// <summary>
    /// Walks the device-tree parents (up to 6 levels) to check whether any ancestor
    /// is the ViGEmBus virtual bus. Returns true → device is a ViGEm virtual device.
    /// This catches both Xbox 360 and DS4 virtual controllers regardless of tree depth.
    /// </summary>
    private bool IsViGEmAncestor(uint devInst)
    {
        var current = devInst;
        for (int depth = 0; depth < 6; depth++)
        {
            uint parent = 0;
            if (CfgMgr32.CM_Get_Parent(ref parent, current, 0) != 0) break; // no more parents

            var buf = new char[512];
            if (CfgMgr32.CM_Get_Device_ID(parent, buf, 512, 0) != 0) break;

            var id = new string(buf).TrimEnd('\0');
            _logger.LogDebug("HidHide: parent[{D}] of {I} = {P}", depth, devInst, id);

            if (id.Contains("VIGEMBUS", StringComparison.OrdinalIgnoreCase))
                return true;

            current = parent;
        }
        return false;
    }

    // Known controller vendor IDs (subset — covers Xbox, PlayStation, Nintendo, common OEM)
    private static readonly HashSet<string> KnownControllerVids = new(StringComparer.OrdinalIgnoreCase)
    {
        "VID_045E", // Microsoft (Xbox 360, Xbox One, Xbox Series)
        "VID_054C", // Sony (DualShock, DualSense)
        "VID_057E", // Nintendo (Switch Pro, Joy-Con)
        "VID_0079", // DragonRise / generic USB gamepad
        "VID_0E8F", // GreenAsia / Genius
        "VID_0F0D", // HORI
        "VID_044F", // Thrustmaster
        "VID_046D", // Logitech
        "VID_24C6", // Power A (Xbox licensed)
        "VID_1BAD", // Harmonix / Mad Catz (Xbox licensed)
        "VID_0738", // Mad Catz
        "VID_1532", // Razer
    };

    /// <summary>
    /// Opens the HID device and checks its Usage Page / Usage via HidP_GetCaps.
    /// Accepts:
    ///   • Generic Desktop (0x01) Joystick (0x04) or Gamepad (0x05) — standard HID gamepads
    ///   • Vendor-specific page (0xFF / 0xFFxx) — Xbox 360 controllers use this
    /// If the device is inaccessible (e.g. already hidden by HidHide), falls back to a
    /// VID allowlist check on the instance ID so hidden controllers are still listed.
    /// </summary>
    private bool IsGameController(string devicePath, string instanceId)
    {
        var handle = Kernel32.CreateFile(
            devicePath, 0,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle == new IntPtr(-1))
        {
            // Device inaccessible — likely hidden by HidHide (or access denied).
            // Fall back to VID allowlist: if we recognise the vendor we include it.
            var err = Marshal.GetLastWin32Error();
            _logger.LogDebug("HidHide: can't open {Path} (win32 err {E}) — using VID fallback", devicePath, err);
            return KnownControllerVids.Any(vid =>
                instanceId.Contains(vid, StringComparison.OrdinalIgnoreCase));
        }

        try
        {
            if (!HidApi.HidD_GetPreparsedData(handle, out var preparsedData))
                return false;
            try
            {
                if (HidApi.HidP_GetCaps(preparsedData, out var caps) != HidApi.HIDP_STATUS_SUCCESS)
                    return false;

                // UsagePage 0x01 = Generic Desktop Controls
                // Usage 0x04 = Joystick, Usage 0x05 = Gamepad
                if (caps.UsagePage == 0x01 && (caps.Usage == 0x04 || caps.Usage == 0x05))
                    return true;

                // Vendor-specific pages (0xFF00–0xFFFF): used by Xbox 360 and other
                // XInput controllers that don't expose standard HID gamepad descriptors.
                if (caps.UsagePage >= 0xFF00)
                    return KnownControllerVids.Any(vid =>
                        instanceId.Contains(vid, StringComparison.OrdinalIgnoreCase));

                return false;
            }
            finally
            {
                HidApi.HidD_FreePreparsedData(preparsedData);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HidHide: failed to read HID caps for {Path}", devicePath);
            return false;
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    private static string? GetDeviceString(IntPtr hDevInfo, ref SetupApi.SP_DEVINFO_DATA devInfoData, uint property)
    {
        uint regType = 0, reqSize = 0;
        SetupApi.SetupDiGetDeviceRegistryProperty(hDevInfo, ref devInfoData, property, ref regType, null, 0, ref reqSize);
        if (reqSize == 0) return null;
        var buf = new byte[reqSize];
        if (!SetupApi.SetupDiGetDeviceRegistryProperty(hDevInfo, ref devInfoData, property, ref regType, buf, reqSize, ref reqSize))
            return null;
        return Encoding.Unicode.GetString(buf).TrimEnd('\0', ' ');
    }

    // -----------------------------------------------------------------------
    // Whitelist management
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public void EnsureWhitelisted()
    {
        if (!IsAvailable) return;
        try
        {
            var dosPath = Environment.ProcessPath
                          ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;

            // HidHide stores and compares paths in NT device-path format:
            //   \Device\HarddiskVolume3\Users\...\MatrixX.exe
            // Passing a DOS path (C:\...) causes the comparison to always fail,
            // so we convert before comparing/adding.
            var devicePath = DosPathToDevicePath(dosPath);

            var existing = _client!.ApplicationPaths;

            // Remove any stale entries for the same file under a different format
            // (e.g. old DOS-path entries from before this fix, or renamed exe).
            foreach (var stale in existing
                .Where(p => !string.Equals(p, devicePath, StringComparison.OrdinalIgnoreCase)
                         && NormalizeName(p) == NormalizeName(devicePath))
                .ToList())
            {
                try { _client.RemoveApplicationPath(stale); }
                catch { /* best-effort */ }
                _logger.LogInformation("HidHide: removed stale whitelist entry {P}", stale);
            }

            // Add if not already present
            existing = _client.ApplicationPaths;
            if (!existing.Any(p => string.Equals(p, devicePath, StringComparison.OrdinalIgnoreCase)))
            {
                _client.AddApplicationPath(devicePath);
                _logger.LogInformation("HidHide: whitelisted {Path}", devicePath);
            }
            else
            {
                _logger.LogDebug("HidHide: already whitelisted");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HidHide: EnsureWhitelisted failed");
        }
    }

    /// <summary>
    /// Converts a DOS path ("C:\foo\bar.exe") to an NT device path
    /// ("\Device\HarddiskVolume3\foo\bar.exe") using QueryDosDevice.
    /// Falls back to the original string if conversion fails.
    /// </summary>
    private static string DosPathToDevicePath(string dosPath)
    {
        var root = Path.GetPathRoot(dosPath); // e.g. "C:\" or "C:"
        if (string.IsNullOrEmpty(root)) return dosPath;

        var drive = root.TrimEnd('\\');       // "C:"
        var sb    = new StringBuilder(512);

        if (QueryDosDevice(drive, sb, (uint)sb.Capacity) == 0)
            return dosPath;                   // conversion failed — return as-is

        // e.g. "\Device\HarddiskVolume3"
        var deviceRoot = sb.ToString().Split('\0', StringSplitOptions.RemoveEmptyEntries).First();

        // Replace "C:" with "\Device\HarddiskVolume3"
        return deviceRoot + dosPath[drive.Length..];
    }

    /// <summary>Lowercased filename only — used to detect stale/renamed entries.</summary>
    private static string NormalizeName(string path) =>
        Path.GetFileName(path).ToLowerInvariant();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(
        string lpDeviceName, StringBuilder lpTargetPath, uint ucchMax);

    // -----------------------------------------------------------------------
    // P/Invoke
    // -----------------------------------------------------------------------

    private static class SetupApi
    {
        public const uint DIGCF_PRESENT = 0x2;
        public const uint DIGCF_DEVICEINTERFACE = 0x10;
        public const uint SPDRP_DEVICEDESC = 0;
        public const uint SPDRP_FRIENDLYNAME = 12;

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

        // Overload used when we DON'T want DeviceInfoData back (pass IntPtr.Zero)
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData,
            uint DeviceInterfaceDetailDataSize,
            ref uint RequiredSize,
            IntPtr DeviceInfoData);

        // Overload used when we DO want DeviceInfoData back
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData,
            uint DeviceInterfaceDetailDataSize,
            ref uint RequiredSize,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData,
            uint Property, ref uint PropertyRegDataType,
            byte[]? PropertyBuffer, uint PropertyBufferSize, ref uint RequiredSize);
    }

    private static class Kernel32
    {
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
    }

    private static class HidApi
    {
        // HIDP_STATUS_SUCCESS = 0x00110000
        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(IntPtr HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);
    }

    private static class CfgMgr32
    {
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern uint CM_Get_Device_ID(uint dnDevInst, char[] Buffer, uint BufferLen, uint ulFlags);

        [DllImport("cfgmgr32.dll")]
        public static extern uint CM_Get_Parent(ref uint pdnDevInst, uint dnDevInst, uint ulFlags);
    }
}
