using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ManagedShell.AppBar;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// Decide quali "app a schermo intero" segnalate da ManagedShell devono davvero far cedere il posto alla barra.
/// I programmi di streaming VR (Virtual Desktop, SteamVR, Quest Link) tengono aperte finestre grandi quanto lo
/// schermo, o le fanno segnalare come tali da Windows, anche mentre si usa il desktop dal visore: la barra finiva
/// in fondo a tutte le finestre e restava lì, coperta e non cliccabile. Le loro finestre non contano.
/// Ogni finestra che fa scendere la barra viene scritta una volta nel log, per capire i casi non ancora noti.
/// </summary>
public static class FullScreenFilter
{
    /// <summary>Nomi (o prefissi) degli eseguibili le cui finestre non sono mai giochi o video a schermo intero.</summary>
    private static readonly string[] IgnoredProcessPrefixes =
    {
        "VirtualDesktop.",                         // Virtual Desktop: Streamer, Service, Server...
        "vrcompositor", "vrmonitor", "vrdashboard", // SteamVR
        "OVRServer_x64", "OculusDash"               // Meta Quest Link
    };

    private static readonly Dictionary<IntPtr, (uint Pid, bool Ignored)> Cache = new();
    private static readonly HashSet<IntPtr> Logged = new();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

    public static bool IsIgnored(FullScreenApp app)
    {
        try
        {
            Native.GetWindowThreadProcessId(app.hWnd, out uint pid);
            if (Cache.TryGetValue(app.hWnd, out var hit) && hit.Pid == pid) return hit.Ignored;

            string exe = ExeName(pid);
            bool ignored = IgnoredProcessPrefixes.Any(p => exe.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (Cache.Count > 64) Cache.Clear();
            Cache[app.hWnd] = (pid, ignored);
            if (ignored) Log.Info($"Schermo intero ignorato (streaming VR): {Describe(app, exe)}");
            return ignored;
        }
        catch (Exception ex)
        {
            Log.Error("FullScreenFilter", ex);
            return false;
        }
    }

    /// <summary>Scrive nel log, una volta per finestra, chi ha fatto cedere il posto alla barra.</summary>
    public static void LogEnter(FullScreenApp app, string barScreen)
    {
        try
        {
            if (Logged.Count > 64) Logged.Clear();
            if (!Logged.Add(app.hWnd)) return;
            Native.GetWindowThreadProcessId(app.hWnd, out uint pid);
            Log.Info($"La barra su {barScreen} cede il posto a: {Describe(app, ExeName(pid))}");
        }
        catch (Exception ex) { Log.Error("FullScreenFilter.LogEnter", ex); }
    }

    private static string ExeName(uint pid) =>
        Native.ProcessImagePath(pid) is { } path ? Path.GetFileNameWithoutExtension(path) : "";

    private static string Describe(FullScreenApp app, string exe)
    {
        var cls = new StringBuilder(256);
        GetClassName(app.hWnd, cls, cls.Capacity);
        var r = app.rect;
        string screen = app.screen.IsVirtualScreen ? "tutti gli schermi" : app.screen.DeviceName;
        return $"{(exe.Length > 0 ? exe : "?")} [{cls}] \"{app.title}\" {r.Left},{r.Top}-{r.Right},{r.Bottom} " +
               $"su {screen}{(app.fromTasksService ? " (segnalata da Windows)" : "")}";
    }
}
