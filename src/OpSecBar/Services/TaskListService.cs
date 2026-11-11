using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using ManagedShell;
using ManagedShell.WindowsTasks;
using OpSecBar.Core;
using OpSecBar.Models;

namespace OpSecBar.Services;

/// <summary>Un'icona della barra: un'app fissata, un'app aperta (una o più finestre) o entrambe le cose.</summary>
public sealed class AppItem : ObservableObject
{
    private ImageSource? _icon;
    private string _title = "";
    private bool _isRunning, _isActive, _isFlashing;
    private PinnedApp? _pin;
    private string? _loadedIconKey;

    public string Key { get; }
    public List<ApplicationWindow> Windows { get; } = new();

    public PinnedApp? Pin
    {
        get => _pin;
        set { if (Set(ref _pin, value)) Raise(nameof(IsPinned)); }
    }

    public bool IsPinned => _pin != null;
    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }
    public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }
    public bool IsFlashing { get => _isFlashing; private set => Set(ref _isFlashing, value); }

    public AppItem(string key) => Key = key;

    public void Refresh()
    {
        IsRunning = Windows.Count > 0;
        IsActive = Windows.Any(w => w.State == ApplicationWindow.WindowState.Active);
        IsFlashing = Windows.Any(w => w.State == ApplicationWindow.WindowState.Flashing);

        if (Pin != null) Title = Windows.Count == 1 ? Windows[0].Title ?? Pin.Name : Pin.Name;
        else if (Windows.Count == 1) Title = Windows[0].Title ?? "";
        else if (Windows.Count > 1) Title = FirstNonEmpty(Windows[0].WinFileDescription, Windows[0].Title) + $" ({Windows.Count})";

        if (Pin != null && !string.IsNullOrEmpty(Pin.IconKey))
        {
            if (_loadedIconKey != Pin.IconKey)
            {
                _loadedIconKey = Pin.IconKey;
                if (Windows.Count > 0 && Icon == null) Icon = Windows[0].Icon;
                LoadPinIcon(Pin.IconKey);
            }
        }
        else if (Windows.Count > 0)
        {
            _loadedIconKey = null;
            Icon = Windows[0].Icon;
        }
    }

    private async void LoadPinIcon(string key)
    {
        try
        {
            var img = await IconService.GetAsync(key);
            if (_loadedIconKey == key && img != null) Icon = img;
            else if (img == null && Windows.Count > 0) Icon = Windows[0].Icon;
        }
        catch (Exception ex) { Log.Error("LoadPinIcon", ex); }
    }

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}

/// <summary>
/// Unisce le app fissate e le finestre aperte (fornite da ManagedShell) nell'elenco di icone mostrato a sinistra.
/// </summary>
public sealed class TaskListService
{
    private readonly ShellManager _shell;
    private readonly SettingsService _settings;
    private readonly Dispatcher _ui;
    private readonly HashSet<ApplicationWindow> _hooked = new();
    private bool _rebuildPending;

    public ObservableCollection<AppItem> Items { get; } = new();

    public TaskListService(ShellManager shell, SettingsService settings, Dispatcher ui)
    {
        _shell = shell;
        _settings = settings;
        _ui = ui;

        if (_shell.Tasks.GroupedWindows is INotifyCollectionChanged incc)
            incc.CollectionChanged += (_, _) => ScheduleRebuild();

        Rebuild();
    }

    public void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        _ui.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _rebuildPending = false;
            try { Rebuild(); } catch (Exception ex) { Log.Error("TaskList.Rebuild", ex); }
        });
    }

    private static string WindowKey(ApplicationWindow w)
    {
        if (w.IsUWP && !string.IsNullOrEmpty(w.AppUserModelID)) return "aumid:" + w.AppUserModelID.ToLowerInvariant();
        string? file = w.WinFileName;
        return string.IsNullOrEmpty(file) ? "hwnd:" + w.Handle : "exe:" + file.ToLowerInvariant();
    }

    private static bool Matches(PinnedApp pin, ApplicationWindow w)
    {
        if (!string.IsNullOrEmpty(pin.AppId) && string.Equals(pin.AppId, w.AppUserModelID, StringComparison.OrdinalIgnoreCase)) return true;
        string? file = w.WinFileName;
        if (string.IsNullOrEmpty(file)) return false;
        if (!string.IsNullOrEmpty(pin.TargetPath) && string.Equals(pin.TargetPath, file, StringComparison.OrdinalIgnoreCase)) return true;
        // Esplora file non ha un percorso di destinazione nel catalogo delle app.
        return pin.AppId == "Microsoft.Windows.Explorer" && file.EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    public void Rebuild()
    {
        AppSettings s = _settings.Settings;
        var windows = new List<ApplicationWindow>();
        foreach (object o in _shell.Tasks.GroupedWindows)
            if (o is ApplicationWindow w) windows.Add(w);

        // Sottoscrizione ai cambi di stato delle finestre.
        foreach (var w in windows)
            if (_hooked.Add(w)) w.PropertyChanged += OnWindowPropertyChanged;
        foreach (var gone in _hooked.Where(h => !windows.Contains(h)).ToList())
        {
            gone.PropertyChanged -= OnWindowPropertyChanged;
            _hooked.Remove(gone);
        }

        var existing = Items.ToDictionary(i => i.Key);
        var desired = new List<AppItem>();
        var byKey = new Dictionary<string, AppItem>();

        AppItem GetOrCreate(string key)
        {
            if (byKey.TryGetValue(key, out var item)) return item;
            if (!existing.TryGetValue(key, out item)) item = new AppItem(key);
            item.Windows.Clear();
            item.Pin = null;
            byKey[key] = item;
            desired.Add(item);
            return item;
        }

        if (s.ShowPinnedApps)
        {
            foreach (PinnedApp pin in s.PinnedApps)
            {
                var item = GetOrCreate("pin:" + pin.Key);
                item.Pin = pin;
            }
        }

        foreach (var w in windows)
        {
            AppItem? target = desired.FirstOrDefault(d => d.Pin != null && Matches(d.Pin, w));
            if (target == null)
            {
                if (!s.ShowRunningApps) continue;
                target = GetOrCreate(s.GroupWindows ? WindowKey(w) : "hwnd:" + w.Handle);
            }
            target.Windows.Add(w);
        }

        // Sincronizza la collezione osservabile con il minimo di operazioni (così la UI non lampeggia).
        for (int i = Items.Count - 1; i >= 0; i--)
            if (!byKey.ContainsKey(Items[i].Key)) Items.RemoveAt(i);

        for (int i = 0; i < desired.Count; i++)
        {
            if (i < Items.Count && ReferenceEquals(Items[i], desired[i])) continue;
            int current = Items.IndexOf(desired[i]);
            if (current >= 0) Items.Move(current, i);
            else Items.Insert(i, desired[i]);
        }

        foreach (var item in desired) item.Refresh();

        // Diagnostica opzionale: OPSECBAR_DEBUG=1 scrive nel log come sono state raggruppate le finestre.
        if (DebugEnabled)
            foreach (var item in desired)
                foreach (var w in item.Windows)
                    Log.Info($"[tasks] {item.Key} <= '{w.Title}' exe='{w.WinFileName}' aumid='{w.AppUserModelID}' uwp={w.IsUWP}");
    }

    internal static readonly bool DebugEnabled = Environment.GetEnvironmentVariable("OPSECBAR_DEBUG") == "1";

    private void OnWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ApplicationWindow w) return;
        void Apply()
        {
            if (e.PropertyName is "ShowInTaskbar" or "AppUserModelID" or "WinFileName") ScheduleRebuild();
            else Items.FirstOrDefault(i => i.Windows.Contains(w))?.Refresh();
        }
        if (_ui.CheckAccess()) Apply(); else _ui.BeginInvoke(Apply);
    }

    // ---- Preferiti ------------------------------------------------------------------------------

    public bool IsPinned(PinnedApp app) => _settings.Settings.PinnedApps.Any(p => p.Key == app.Key);

    public void Pin(PinnedApp app)
    {
        if (IsPinned(app)) return;
        _settings.Settings.PinnedApps.Add(app);
        _settings.Notify(nameof(AppSettings.PinnedApps));
    }

    public void Pin(AppItem item)
    {
        if (item.IsPinned || item.Windows.Count == 0) return;
        var w = item.Windows[0];
        string? file = w.WinFileName;
        bool uwp = w.IsUWP && !string.IsNullOrEmpty(w.AppUserModelID);
        if (!uwp && string.IsNullOrEmpty(file)) return;

        string name = !string.IsNullOrWhiteSpace(w.WinFileDescription) ? w.WinFileDescription : w.Title ?? "App";
        Pin(new PinnedApp { Name = name, AppId = uwp ? w.AppUserModelID : null, TargetPath = uwp ? null : file });
    }

    public void Unpin(PinnedApp app)
    {
        if (_settings.Settings.PinnedApps.RemoveAll(p => p.Key == app.Key) > 0)
            _settings.Notify(nameof(AppSettings.PinnedApps));
    }

    // ---- Termina attività --------------------------------------------------------------------------

    // Processi che ospitano finestre di altri (o la shell stessa): terminarli farebbe danni ben oltre l'app scelta.
    private static readonly string[] ProtectedProcesses = { "explorer", "ApplicationFrameHost", "dwm", "csrss", "winlogon", "sihost" };

    private static System.Diagnostics.Process? KillableProcessOf(ApplicationWindow w)
    {
        try
        {
            int pid = (int)(w.ProcId ?? 0);
            if (pid <= 0 || pid == Environment.ProcessId) return null;
            var process = System.Diagnostics.Process.GetProcessById(pid);
            if (ProtectedProcesses.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                process.Dispose();
                return null;
            }
            return process;
        }
        catch { return null; }
    }

    /// <summary>True se l'app ha almeno un processo che si può terminare in sicurezza.</summary>
    public bool CanEndTask(AppItem item)
    {
        foreach (var w in item.Windows)
        {
            using var p = KillableProcessOf(w);
            if (p != null) return true;
        }
        return false;
    }

    /// <summary>Termina forzatamente i processi dell'app (come "Termina attività" di Gestione attività).</summary>
    public void EndTask(AppItem item)
    {
        var done = new HashSet<int>();
        foreach (var w in item.Windows.ToList())
        {
            using var process = KillableProcessOf(w);
            if (process == null)
            {
                try { w.Close(); } catch { } // processo condiviso: si chiude solo la finestra
                continue;
            }
            if (!done.Add(process.Id)) continue;
            try
            {
                Log.Info($"End task: {process.ProcessName} ({process.Id})");
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) { Log.Warn($"End task fallito per {process.ProcessName}: {ex.Message}"); }
        }
    }

    /// <summary>Avvia una nuova istanza dell'app rappresentata dall'icona.</summary>
    public void Launch(AppItem item, bool asAdmin = false)
    {
        if (item.Pin != null)
        {
            AppCatalogService.Launch(item.Pin, asAdmin);
            return;
        }
        if (item.Windows.Count == 0) return;
        var w = item.Windows[0];
        if (w.IsUWP && !string.IsNullOrEmpty(w.AppUserModelID))
            AppCatalogService.Launch(new PinnedApp { Name = w.Title ?? "", AppId = w.AppUserModelID });
        else if (!string.IsNullOrEmpty(w.WinFileName))
            AppCatalogService.Launch(new PinnedApp { Name = w.Title ?? "", TargetPath = w.WinFileName }, asAdmin);
    }
}
