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
    /// <summary>True mentre l'icona viene trascinata per cambiarne la posizione.</summary>
    public bool IsDragging { get => _isDragging; set => Set(ref _isDragging, value); }
    private bool _isDragging;

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

    private readonly Dictionary<IntPtr, string> _appIds = new();

    /// <summary>
    /// AUMID della finestra. Le app Win32 in pacchetto (es. Claude) non lo mettono sulla finestra: lo si chiede al processo,
    /// altrimenti un'app fissata dal menu Start non riconoscerebbe le proprie finestre e comparirebbe due volte.
    /// </summary>
    private string AppIdOf(ApplicationWindow w)
    {
        string? id = w.AppUserModelID;
        if (!string.IsNullOrEmpty(id)) return id;
        if (_appIds.TryGetValue(w.Handle, out id)) return id;
        try { id = Native.PackagedAppId(w.Handle); } catch { id = null; }
        return _appIds[w.Handle] = id ?? "";
    }

    private bool Matches(PinnedApp pin, ApplicationWindow w)
    {
        if (!string.IsNullOrEmpty(pin.AppId) && string.Equals(pin.AppId, AppIdOf(w), StringComparison.OrdinalIgnoreCase)) return true;
        string? file = w.WinFileName;
        if (string.IsNullOrEmpty(file)) return false;
        if (!string.IsNullOrEmpty(pin.TargetPath))
        {
            if (string.Equals(pin.TargetPath, file, StringComparison.OrdinalIgnoreCase)) return true;
            // App Squirrel (Discord, ...): il collegamento punta a Update.exe, l'app vera sta in una sottocartella "app-x.y.z".
            if (pin.TargetPath.EndsWith("\\Update.exe", StringComparison.OrdinalIgnoreCase) &&
                file.StartsWith(pin.TargetPath[..^"Update.exe".Length] + "app-", StringComparison.OrdinalIgnoreCase)) return true;
        }
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
            _appIds.Remove(gone.Handle);
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

        // Le app fissate restano nell'ordine delle fissate; le altre seguono l'ordine scelto trascinandole (le nuove in coda).
        if (s.TaskOrder.Count > 0)
        {
            var order = new Dictionary<string, int>();
            foreach (string key in s.TaskOrder) order.TryAdd(key, order.Count);
            var pinned = desired.Where(d => d.Pin != null);
            var others = desired.Where(d => d.Pin == null).OrderBy(d => order.TryGetValue(d.Key, out int i) ? i : int.MaxValue);
            desired = pinned.Concat(others).ToList();
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
        // App in pacchetto (UWP o Win32 MSIX): si fissa l'AUMID, come fa il menu Start. Il percorso dell'eseguibile
        // contiene la versione e cambierebbe a ogni aggiornamento.
        string appId = AppIdOf(w);
        bool uwp = (w.IsUWP && !string.IsNullOrEmpty(appId)) || appId.Contains('!');
        if (!uwp && string.IsNullOrEmpty(file)) return;

        string name = !string.IsNullOrWhiteSpace(w.WinFileDescription) ? w.WinFileDescription : w.Title ?? "App";
        Pin(new PinnedApp { Name = name, AppId = uwp ? appId : null, TargetPath = uwp ? null : file });
    }

    public void Unpin(PinnedApp app)
    {
        if (_settings.Settings.PinnedApps.RemoveAll(p => p.Key == app.Key) > 0)
            _settings.Notify(nameof(AppSettings.PinnedApps));
    }

    // ---- Riordino col trascinamento --------------------------------------------------------------

    /// <summary>Sposta l'icona nella posizione di un'altra, ma solo dentro il proprio gruppo (fissate / non fissate).</summary>
    public void MoveItem(AppItem item, AppItem target)
    {
        if (item.IsPinned != target.IsPinned) return;
        int from = Items.IndexOf(item), to = Items.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        Items.Move(from, to);
    }

    /// <summary>Salva l'ordine attuale delle icone: fissate nell'elenco delle fissate, le altre in TaskOrder.</summary>
    public void CommitOrder()
    {
        AppSettings s = _settings.Settings;

        var pins = Items.Where(i => i.Pin != null).Select(i => i.Pin!).ToList();
        if (pins.Count > 1 && pins.Count == s.PinnedApps.Count && !pins.SequenceEqual(s.PinnedApps))
        {
            s.PinnedApps.Clear();
            s.PinnedApps.AddRange(pins);
            _settings.Notify(nameof(AppSettings.PinnedApps));
        }

        var order = Items.Where(i => i.Pin == null).Select(i => i.Key).ToList();
        if (!order.SequenceEqual(s.TaskOrder))
        {
            s.TaskOrder = order;
            _settings.Notify(nameof(AppSettings.TaskOrder));
        }
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

    // ---- File trascinati su un'icona ----------------------------------------------------------------

    /// <summary>Nome, eseguibile e identificativo con cui riconoscere l'app dell'icona tra quelle di "Apri con".</summary>
    private (string Name, string? Exe, string? AppId) OpenTarget(AppItem item)
    {
        ApplicationWindow? w = item.Windows.FirstOrDefault();
        string? exe = item.Pin?.TargetPath;
        if (string.IsNullOrEmpty(exe) && w is { IsUWP: false }) exe = w.WinFileName;
        string? appId = item.Pin?.AppId;
        if (string.IsNullOrEmpty(appId) && w != null) appId = AppIdOf(w);
        string name = item.Pin?.Name is { Length: > 0 } pinName ? pinName : item.Title;
        return (name, exe, string.IsNullOrEmpty(appId) ? null : appId);
    }

    /// <summary>True se rilasciando questi file sull'icona l'app li apre (Windows la elenca in "Apri con" per quei tipi).</summary>
    public bool CanOpenFiles(AppItem item, IReadOnlyList<string> paths)
    {
        var (name, exe, appId) = OpenTarget(item);
        return AppCatalogService.CanOpenFiles(name, exe, appId, paths);
    }

    /// <summary>Apre i file con l'app rappresentata dall'icona (in esecuzione oppure no).</summary>
    public void OpenFiles(AppItem item, IReadOnlyList<string> paths)
    {
        var (name, exe, appId) = OpenTarget(item);
        AppCatalogService.OpenFiles(name, exe, appId, paths);
    }

    /// <summary>
    /// Porta in primo piano una finestra mentre l'utente sta trascinando qualcosa. L'input del trascinamento appartiene
    /// all'app di origine, quindi Windows può negarci il cambio di primo piano: in quel caso lo si forza.
    /// </summary>
    public static void ActivateDuringDrag(ApplicationWindow w)
    {
        try
        {
            w.BringToFront();
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == w.Handle) return;
            // Un popup della stessa app in primo piano va bene così (es. una finestra di dialogo modale).
            Native.GetWindowThreadProcessId(foreground, out uint foregroundPid);
            Native.GetWindowThreadProcessId(w.Handle, out uint pid);
            if (foregroundPid != pid) Native.ForceForeground(w.Handle, allowKeyTap: false);
        }
        catch (Exception ex) { Log.Error("Attivazione durante il trascinamento", ex); }
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
