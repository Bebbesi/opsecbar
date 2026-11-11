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

    /// <summary>
    /// SHAppBarMessage recapita il messaggio alla prima finestra "Shell_TrayWnd" in ordine Z. Mentre OpSecBar è in
    /// esecuzione ne esiste una nostra (creata da ManagedShell per il tray) e, con la taskbar di Explorer spinta in
    /// fondo, è quella a ricevere la richiesta: lo stato di Explorer non verrebbe né letto né modificato.
    /// Per la durata della chiamata si porta quindi la taskbar di Explorer davanti alla nostra.
    /// La nostra finta Shell_TrayWnd è TOPMOST mentre quella di Explorer, una volta nascosta, non lo è più: HWND_TOP
    /// la porterebbe solo in cima alle finestre normali, sempre sotto la nostra. Serve quindi HWND_TOPMOST.
    /// </summary>
    private static T SendToExplorer<T>(Func<IntPtr, T> send)
    {
        IntPtr tray = FindPrimaryTaskbar();
        if (tray == IntPtr.Zero) return send(IntPtr.Zero);
        const int HWND_TOPMOST = -1, HWND_NOTOPMOST = -2, HWND_BOTTOM = 1;
        const uint flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
        bool wasTopmost = (Native.GetWindowLong(tray, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
        Native.SetWindowPos(tray, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, flags);
        try { return send(tray); }
        finally
        {
            if (!wasTopmost) Native.SetWindowPos(tray, (IntPtr)HWND_NOTOPMOST, 0, 0, 0, 0, flags);
            // Se Explorer la teneva nascosta in fondo, ce la si rimette (ManagedShell fa lo stesso).
            if (!Native.IsWindowVisible(tray))
                Native.SetWindowPos(tray, (IntPtr)HWND_BOTTOM, 0, 0, 0, 0, flags);
        }
    }

    private static int GetTaskbarState() => SendToExplorer(tray =>
    {
        var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = tray };
        return (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd).ToUInt32();
    });

    private static void SetTaskbarState(int state) => SendToExplorer(tray =>
    {
        var abd = new Native.APPBARDATA
        {
            cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = tray, lParam = (IntPtr)state
        };
        Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
        return 0;
    });

    /// <summary>
    /// Mette la taskbar di Explorer in auto-hide. Nascondere soltanto la finestra non basta: la taskbar resta
    /// registrata come AppBar e continua a riservare il suo spazio (es. 48 px sul lato sinistro quando la taskbar di
    /// Windows 11 è a sinistra), spingendo verso l'interno OpSecBar e lasciando una fascia vuota.
    /// Da chiamare dopo <see cref="PrepareHide"/>, che salva lo stato originale per il ripristino.
    /// </summary>
    public static void ForceAutoHide()
    {
        try
        {
            int state = GetTaskbarState();
            if ((state & 0x1) == 0) SetTaskbarState(state | 0x1);
        }
        catch (Exception ex) { Log.Error("ForceAutoHide fallito", ex); }
    }

    /// <summary>
    /// Spessore (px fisici) della taskbar di Explorer ancorata al lato <paramref name="edge"/> (ABE_*: 0 sinistra,
    /// 1 alto, 2 destra, 3 basso) dello schermo indicato; 0 se su quel lato non ce n'è una.
    /// Anche nascosta, la taskbar resta registrata come AppBar e Windows ne tiene riservato lo spazio.
    /// </summary>
    internal static int DockedTaskbarThickness(Native.RECT screen, int edge)
    {
        int best = 0;
        try
        {
            foreach (IntPtr h in FindExplorerTaskbars())
            {
                if (!Native.GetWindowRect(h, out Native.RECT r) || r.Width <= 0 || r.Height <= 0) continue;
                int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
                if (cx < screen.Left || cx >= screen.Right || cy < screen.Top || cy >= screen.Bottom) continue;

                bool vertical = edge is 0 or 2;
                bool spansSide = vertical
                    ? r.Top <= screen.Top + 1 && r.Bottom >= screen.Bottom - 1
                    : r.Left <= screen.Left + 1 && r.Right >= screen.Right - 1;
                if (!spansSide) continue;

                int t = edge switch
                {
                    0 => r.Left <= screen.Left + 1 ? r.Right - screen.Left : 0,
                    1 => r.Top <= screen.Top + 1 ? r.Bottom - screen.Top : 0,
                    2 => r.Right >= screen.Right - 1 ? screen.Right - r.Left : 0,
                    _ => r.Bottom >= screen.Bottom - 1 ? screen.Bottom - r.Top : 0
                };
                int max = (vertical ? screen.Width : screen.Height) / 2;
                if (t > 0 && t < max) best = Math.Max(best, t);
            }
        }
        catch (Exception ex) { Log.Error("DockedTaskbarThickness", ex); }
        return best;
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
