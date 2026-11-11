using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// Rete di sicurezza per la taskbar originale di Windows.
/// - Explorer.exe NON viene mai terminato: la taskbar viene solo nascosta (stato auto-hide + finestra nascosta).
/// - Lo stato originale viene scritto su disco prima di nasconderla, così può essere ripristinato anche dopo un crash.
/// - Un processo "watchdog" separato attende la fine del processo principale e ripristina la taskbar
///   se OpSecBar termina in modo anomalo (crash, kill dal Task Manager...).
/// Questa classe non dipende da WPF né da ManagedShell, così il watchdog resta leggerissimo.
/// </summary>
public static class TaskbarGuard
{
    public const string MainMutexName = "Local\\OpSecBar.Main.Instance";

    /// <summary>Finestre taskbar reali di Explorer (esclude la finta Shell_TrayWnd creata dal nostro processo per il tray).</summary>
    private static List<IntPtr> FindExplorerTaskbars()
    {
        var result = new List<IntPtr>();
        uint ownPid = (uint)Environment.ProcessId;
        foreach (string cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            IntPtr h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, cls, null)) != IntPtr.Zero)
            {
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (pid != ownPid) result.Add(h);
            }
        }
        return result;
    }

    private static IntPtr FindPrimaryTaskbar()
    {
        uint ownPid = (uint)Environment.ProcessId;
        IntPtr h = IntPtr.Zero;
        while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null)) != IntPtr.Zero)
        {
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid != ownPid) return h;
        }
        return IntPtr.Zero;
    }

    private static int GetTaskbarState()
    {
        var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = FindPrimaryTaskbar() };
        return (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd).ToUInt32();
    }

    private static void SetTaskbarState(int state)
    {
        var abd = new Native.APPBARDATA
        {
            cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = FindPrimaryTaskbar(), lParam = (IntPtr)state
        };
        Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
    }

    /// <summary>
    /// Da chiamare PRIMA di nascondere la taskbar. Se esiste un file di stato significa che la sessione precedente
    /// non si è chiusa bene: prima si ripristina lo stato reale, poi si salva quello corrente.
    /// </summary>
    public static void PrepareHide()
    {
        try
        {
            if (File.Exists(AppPaths.TaskbarStateFile)) RestoreWindowsTaskbar();
            File.WriteAllText(AppPaths.TaskbarStateFile, GetTaskbarState().ToString());
        }
        catch (Exception ex) { Log.Error("PrepareHide fallito", ex); }
    }

    /// <summary>Rende di nuovo visibile e funzionante la taskbar originale. Idempotente, non lancia eccezioni.</summary>
    public static void RestoreWindowsTaskbar()
    {
        try
        {
            int state = 0; // ABS_DEFAULT: sempre visibile
            bool hadFile = File.Exists(AppPaths.TaskbarStateFile);
            if (hadFile && int.TryParse(File.ReadAllText(AppPaths.TaskbarStateFile).Trim(), out int saved)) state = saved & 0x3;

            if (hadFile) SetTaskbarState(state);
            foreach (IntPtr h in FindExplorerTaskbars())
                if (!Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_SHOWNA);

            if (hadFile) File.Delete(AppPaths.TaskbarStateFile);
        }
        catch (Exception ex) { Log.Error("Ripristino taskbar fallito", ex); }
    }

    /// <summary>Ripristino forzato (comando --restore): mostra la taskbar e toglie l'auto-hide se non c'è uno stato salvato.</summary>
    public static void ForceRestore()
    {
        bool hadFile = File.Exists(AppPaths.TaskbarStateFile);
        RestoreWindowsTaskbar();
        if (!hadFile)
        {
            try
            {
                foreach (IntPtr h in FindExplorerTaskbars()) Native.ShowWindow(h, Native.SW_SHOWNA);
            }
            catch { }
        }
    }

    public static void StartWatchdog()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe == null) return;
            Process.Start(new ProcessStartInfo(exe, $"--watchdog {Environment.ProcessId}")
            {
                UseShellExecute = false, CreateNoWindow = true
            });
        }
        catch (Exception ex) { Log.Error("Avvio watchdog fallito", ex); }
    }

    /// <summary>Corpo del processo watchdog: aspetta la fine del processo principale e ripristina la taskbar.</summary>
    public static int RunWatchdog(int parentPid)
    {
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            parent.WaitForExit();
        }
        catch { /* il processo è già terminato */ }

        Thread.Sleep(400);

        // Se nel frattempo è partita una nuova istanza di OpSecBar (riavvio), non toccare nulla.
        if (Mutex.TryOpenExisting(MainMutexName, out Mutex? m))
        {
            m.Dispose();
            return 0;
        }

        if (File.Exists(AppPaths.TaskbarStateFile))
        {
            Log.Warn("Watchdog: OpSecBar è terminato in modo anomalo, ripristino la taskbar di Windows");
            RestoreWindowsTaskbar();
        }
        else
        {
            // Uscita pulita: la taskbar è già stata ripristinata. Controllo comunque che sia visibile.
            foreach (IntPtr h in FindExplorerTaskbars())
                if (!Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_SHOWNA);
        }
        return 0;
    }
}
