using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;

namespace InputBusX.UI;

internal static class Program
{
    private const string MutexName = "Global\\ReflexX_SingleInstance";

    [STAThread]
    public static void Main(string[] args)
    {
        // Kill any previous ReflexX instance before starting.
        // This prevents ViGEm virtual devices from accumulating when the user
        // replaces the exe with a newer version without closing the old one first.
        KillPreviousInstances();
        EnableLowLatencyProcessMode();

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance just started at the same moment — bail out
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            static extern int MessageBoxW(IntPtr h, string t, string c, uint f);
            MessageBoxW(IntPtr.Zero,
                "ReflexX is already running.\nClose it before starting a new instance.",
                "ReflexX", 0x00000030 /* MB_ICONWARNING */);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Finds all other ReflexX / MatrixX / InputBusX.UI processes and terminates them gracefully
    /// (giving them 3 s to exit so ViGEm can clean up its virtual devices),
    /// then force-kills any that are still alive.
    /// </summary>
    private static void KillPreviousInstances()
    {
        var current = Process.GetCurrentProcess();
        var names   = new[] { "ReflexX", "MatrixX", "InputBusX.UI" };

        var old = names
            .SelectMany(n => Process.GetProcessesByName(n))
            .Where(p => p.Id != current.Id)
            .ToList();

        if (old.Count == 0) return;

        // Ask them to close gracefully so ViGEm calls Disconnect() properly
        foreach (var p in old)
            try { p.CloseMainWindow(); } catch { }

        // Wait up to 3 s for graceful exit
        var deadline = DateTime.UtcNow.AddSeconds(3);
        foreach (var p in old)
        {
            var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remaining > 0)
                try { p.WaitForExit(remaining); } catch { }
        }

        // Force-kill whatever is still alive
        foreach (var p in old)
        {
            try
            {
                if (!p.HasExited) p.Kill(entireProcessTree: true);
            }
            catch { }
            finally { p.Dispose(); }
        }

        // Brief pause so the OS and ViGEm driver finish cleanup
        Thread.Sleep(500);
    }

    private static void EnableLowLatencyProcessMode()
    {
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        }
        catch
        {
            // Priority changes are best-effort; keep startup resilient.
        }
    }
}
