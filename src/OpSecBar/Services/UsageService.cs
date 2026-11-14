using System.IO;
using System.Text.Json;
using OpSecBar.Core;
using OpSecBar.Models;

namespace OpSecBar.Services;

/// <summary>
/// Cronologia degli avvii delle app fatti da OpSecBar (menu Start e icone della barra): quante volte un'app è stata
/// aperta negli ultimi 30 giorni (ordine dei risultati di ricerca) e quali sono state aperte di recente.
/// </summary>
public static class UsageService
{
    /// <summary>Finestra su cui si contano gli avvii per l'ordinamento della ricerca.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);
    /// <summary>Un'app non aperta da più di così sparisce del tutto dalla cronologia.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);
    private const int MaxLaunchesPerApp = 500;

    public sealed class Record
    {
        public string Name { get; set; } = "";
        public string? AppId { get; set; }
        public string? TargetPath { get; set; }
        public DateTime LastUsed { get; set; }
        /// <summary>Istanti (UTC) degli avvii negli ultimi <see cref="Window"/>.</summary>
        public List<DateTime> Launches { get; set; } = new();

        public PinnedApp ToPinned() => new() { Name = Name, AppId = AppId, TargetPath = TargetPath };
    }

    private static readonly object Gate = new();
    private static Dictionary<string, Record>? _records;
    private static string FilePath => Path.Combine(AppPaths.DataDir, "usage.json");

    /// <summary>Cambia quando si registra un avvio o si toglie un'app dai recenti.</summary>
    public static event Action? Changed;

    private static Dictionary<string, Record> Records
    {
        get
        {
            if (_records != null) return _records;
            try
            {
                if (File.Exists(FilePath))
                    _records = JsonSerializer.Deserialize<Dictionary<string, Record>>(File.ReadAllText(FilePath), JsonOptions.Default);
            }
            catch (Exception ex) { Log.Error("Cronologia avvii illeggibile", ex); }
            _records = _records != null
                ? new Dictionary<string, Record>(_records, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
            Prune();
            return _records;
        }
    }

    /// <summary>Annota un avvio dell'app.</summary>
    public static void RecordLaunch(PinnedApp app)
    {
        string key = app.Key;
        if (string.IsNullOrWhiteSpace(key)) return;
        lock (Gate)
        {
            DateTime now = DateTime.UtcNow;
            if (!Records.TryGetValue(key, out Record? r)) Records[key] = r = new Record();
            if (!string.IsNullOrWhiteSpace(app.Name)) r.Name = app.Name;
            r.AppId ??= app.AppId;
            r.TargetPath ??= app.TargetPath;
            r.LastUsed = now;
            r.Launches.Add(now);
            if (r.Launches.Count > MaxLaunchesPerApp) r.Launches.RemoveRange(0, r.Launches.Count - MaxLaunchesPerApp);
            Prune();
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Avvii negli ultimi 30 giorni dell'app con questa chiave (o questo eseguibile).</summary>
    public static int LaunchCount(string key, string? targetPath = null)
    {
        lock (Gate)
        {
            DateTime since = DateTime.UtcNow - Window;
            int count = 0;
            if (Records.TryGetValue(key, out Record? r)) count = r.Launches.Count(t => t >= since);
            // La stessa app avviata dall'icona di una finestra aperta (eseguibile) invece che dal suo collegamento.
            if (!string.IsNullOrEmpty(targetPath) && !string.Equals(targetPath, key, StringComparison.OrdinalIgnoreCase) &&
                Records.TryGetValue(targetPath, out Record? byPath))
                count += byPath.Launches.Count(t => t >= since);
            return count;
        }
    }

    /// <summary>Ultimo avvio (UTC) dell'app, o null se mai avviata.</summary>
    public static DateTime? LastUsed(string key)
    {
        lock (Gate) return Records.TryGetValue(key, out Record? r) ? r.LastUsed : null;
    }

    /// <summary>Le app aperte più di recente, la più recente per prima.</summary>
    public static List<Record> Recent(int count)
    {
        lock (Gate)
            return Records.Values
                .Where(r => !string.IsNullOrEmpty(r.Name) && StillExists(r))
                .OrderByDescending(r => r.LastUsed)
                .Take(count)
                .ToList();
    }

    public static void Forget(string key)
    {
        lock (Gate)
        {
            if (!Records.Remove(key)) return;
            Save();
        }
        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Records.Clear();
            Save();
        }
        Changed?.Invoke();
    }

    private static bool StillExists(Record r)
    {
        if (!string.IsNullOrEmpty(r.AppId)) return true; // app in pacchetto / voce di shell:AppsFolder
        if (string.IsNullOrEmpty(r.TargetPath)) return false;
        try { return File.Exists(r.TargetPath); } catch { return false; }
    }

    private static void Prune()
    {
        DateTime now = DateTime.UtcNow;
        Dictionary<string, Record> records = _records!;
        foreach (var (key, r) in records.ToList())
        {
            r.Launches ??= new();
            r.Launches.RemoveAll(t => t < now - Window);
            if (r.LastUsed < now - Retention) records.Remove(key);
        }
    }

    private static void Save()
    {
        try
        {
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_records, JsonOptions.Default));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex) { Log.Error("Salvataggio cronologia avvii fallito", ex); }
    }
}
