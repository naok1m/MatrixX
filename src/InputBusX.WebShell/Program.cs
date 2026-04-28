using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InputBusX.WebShell;

internal static class Program
{
    private const string MutexName = "Global\\ReflexX_SingleInstance";

    [STAThread]
    private static void Main()
    {
        KillPreviousInstances();

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBoxW(IntPtr.Zero,
                "ReflexX is already running.\nClose it before starting a new instance.",
                "ReflexX", 0x00000030);
            return;
        }

        ApplicationConfiguration.Initialize();
        using var services = new WebShellServices();
        services.Initialize();

        // Defensive shutdown: if the process exits abnormally (unhandled
        // exception, taskbar close, log-off) FormClosing may not fire and
        // the ViGEm virtual controller stays plugged in the driver until
        // reboot — which then makes Windows refuse to recognize the next
        // virtual instance. Hook ProcessExit to disconnect cleanly.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => services.Shutdown();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => services.Shutdown();

        System.Windows.Forms.Application.Run(new ShellForm(services));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr h, string t, string c, uint f);

    private static void KillPreviousInstances()
    {
        var current = Process.GetCurrentProcess();
        var names = new[] { "ReflexX", "MatrixX", "InputBusX.UI", "InputBusX.WebShell" };
        var old = names
            .SelectMany(Process.GetProcessesByName)
            .Where(p => p.Id != current.Id)
            .ToList();

        foreach (var process in old)
        {
            try { process.CloseMainWindow(); } catch { }
        }

        var deadline = DateTime.UtcNow.AddSeconds(3);
        foreach (var process in old)
        {
            var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remaining > 0)
            {
                try { process.WaitForExit(remaining); } catch { }
            }
        }

        foreach (var process in old)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }
            finally
            {
                process.Dispose();
            }
        }

        Thread.Sleep(500);
    }
}
