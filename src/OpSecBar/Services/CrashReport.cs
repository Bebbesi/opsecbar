using System.IO;
using System.Reflection;
using System.Text;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// Rapporto dell'ultimo crash. Quando l'app cade si scrive latest.log (intestazione + log della sessione caduta) e un
/// segnale "crash da segnalare"; al prossimo avvio l'app chiede all'utente se vuole segnalarlo (vedi App).
/// Non dipende da WPF: lo usa anche il watchdog, che si accorge dei crash in cui l'app non riesce a scrivere nulla
/// (crash nativi, stack overflow...).
/// </summary>
public static class CrashReport
{
    public const string IssuesUrl = "https://github.com/Bebbesi/opsecbar/issues/new";

    public static string LogPath => Path.Combine(AppPaths.DataDir, "latest.log");
    private static string PendingPath => Path.Combine(AppPaths.DataDir, "crash.pending");

    /// <summary>C'è un crash di cui l'utente non è ancora stato avvisato.</summary>
    public static bool Pending => File.Exists(PendingPath) && File.Exists(LogPath);

    /// <summary>L'utente è stato avvisato (abbia segnalato o no): non lo si chiede più.</summary>
    public static void ClearPending()
    {
        try { File.Delete(PendingPath); } catch { }
    }

    /// <summary>
    /// Versione in esecuzione come la mostra l'app: "1.1.6" per una release, "1.1.6 (b20261117)" per una build di prova
    /// (la pipeline scrive il tag nell'InformationalVersion, es. "1.1.6-b20261117", più l'eventuale "+commit" dell'SDK).
    /// </summary>
    public static string Version
    {
        get
        {
            Assembly asm = typeof(CrashReport).Assembly;
            string info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? asm.GetName().Version?.ToString(3) ?? "?";
            info = info.Split('+')[0];
            int dash = info.IndexOf('-');
            return dash > 0 ? $"{info[..dash]} ({info[(dash + 1)..]})" : info;
        }
    }

    /// <summary>
    /// Pagina per aprire la issue: modulo "Crash report" (etichetta bug), titolo e versione già compilati.
    /// </summary>
    public static string IssueUrl()
    {
        string version = Uri.EscapeDataString(Version);
        string title = Uri.EscapeDataString($"Crash report: {Version}");
        return $"{IssuesUrl}?template=crash_report.yml&labels=bug&title={title}&version={version}";
    }

    /// <summary>
    /// Scrive latest.log e segna il crash come da segnalare. Con <paramref name="overwrite"/> false non tocca un crash
    /// già registrato e non ancora segnalato (il watchdog arriva dopo l'app, che ha già scritto i dettagli dell'eccezione).
    /// </summary>
    public static void Record(string reason, Exception? ex = null, bool overwrite = true)
    {
        try
        {
            if (!overwrite && Pending) return;
            var sb = new StringBuilder();
            sb.AppendLine($"{AppInfo.Name} crash report");
            sb.AppendLine($"Version: {Version}");
            sb.AppendLine($"Windows: {Environment.OSVersion.Version}{(Environment.Is64BitOperatingSystem ? " x64" : "")}");
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Reason: {reason}");
            if (ex != null)
            {
                sb.AppendLine();
                sb.AppendLine(ex.ToString());
            }
            sb.AppendLine();
            sb.AppendLine("---- Log of the session that crashed ----");
            sb.Append(SessionLog());
            File.WriteAllText(LogPath, sb.ToString());
            File.WriteAllText(PendingPath, DateTime.Now.ToString("O"));
        }
        catch { /* il rapporto è un di più: non deve mai peggiorare un crash */ }
    }

    /// <summary>Le righe del log dall'ultimo avvio dell'app (al massimo le ultime 3000).</summary>
    private static string SessionLog()
    {
        try
        {
            if (!File.Exists(AppPaths.LogFile)) return "(no log)" + Environment.NewLine;
            var lines = new List<string>();
            using (var fs = new FileStream(AppPaths.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs))
            {
                for (string? line; (line = reader.ReadLine()) != null;)
                {
                    if (line.Contains("[INFO ] Avvio OpSecBar", StringComparison.Ordinal)) lines.Clear();
                    lines.Add(line);
                }
            }
            if (lines.Count > 3000) lines.RemoveRange(0, lines.Count - 3000);
            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }
        catch (Exception ex) { return $"(log not readable: {ex.Message})" + Environment.NewLine; }
    }

    /// <summary>
    /// True se il codice d'uscita indica un crash: eccezione .NET non gestita (0xE0434352), FailFast, violazione di
    /// accesso, stack overflow... sono tutti codici con il bit alto acceso. Le chiusure forzate no: il Task Manager e
    /// taskkill escono con 1, Stop-Process di PowerShell con -1.
    /// </summary>
    public static bool IsCrashExitCode(int code) => code < 0 && code != -1;
}
