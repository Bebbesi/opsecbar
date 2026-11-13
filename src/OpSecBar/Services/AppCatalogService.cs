using System.Diagnostics;
using System.Windows.Media;
using OpSecBar.Core;
using OpSecBar.Models;

namespace OpSecBar.Services;

/// <summary>Un'applicazione installata (voce di shell:AppsFolder). L'icona viene caricata solo quando serve.</summary>
public sealed class AppEntry : ObservableObject
{
    private ImageSource? _icon;
    private bool _iconRequested;

    public string Name { get; init; } = "";
    public string AppId { get; init; } = "";
    public string? TargetPath { get; init; }

    public string IconKey => !string.IsNullOrEmpty(AppId) ? "shell:AppsFolder\\" + AppId : TargetPath ?? "";
    public string Key => (string.IsNullOrEmpty(AppId) ? TargetPath ?? Name : AppId).ToLowerInvariant();

    public static AppEntry From(PinnedApp p) => new() { Name = p.Name, AppId = p.AppId ?? "", TargetPath = p.TargetPath };

    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                LoadIcon();
            }
            return _icon;
        }
    }

    private async void LoadIcon()
    {
        try
        {
            _icon = await IconService.GetAsync(IconKey);
            Raise(nameof(Icon));
        }
        catch (Exception ex) { Log.Error("AppEntry.LoadIcon", ex); }
    }

    public PinnedApp ToPinned() => new() { Name = Name, AppId = string.IsNullOrEmpty(AppId) ? null : AppId, TargetPath = TargetPath };
}

/// <summary>Elenco delle app installate (Win32 e UWP), come lo vede il menu Start di Windows.</summary>
public sealed class AppCatalogService
{
    private Task<List<AppEntry>>? _loadTask;

    private static readonly string[] SkippedExtensions = { ".url", ".chm", ".txt", ".htm", ".html", ".pdf", ".rtf", ".msc" };
    private static readonly string[] SkippedWords = { "uninstall", "disinstalla", "readme", "leggimi" };

    public Task<List<AppEntry>> GetAppsAsync(bool refresh = false)
    {
        if (_loadTask == null || refresh || _loadTask.IsFaulted) _loadTask = LoadAsync();
        return _loadTask;
    }

    private static Task<List<AppEntry>> LoadAsync()
    {
        var tcs = new TaskCompletionSource<List<AppEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(Enumerate()); }
            catch (Exception ex)
            {
                Log.Error("Enumerazione app installate fallita", ex);
                tcs.SetResult(new List<AppEntry>());
            }
        }) { IsBackground = true, Name = "OpSecBar.AppCatalog" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static List<AppEntry> Enumerate()
    {
        var list = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Type? shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null) return list;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");
        if (folder == null) return list;

        foreach (dynamic item in folder.Items())
        {
            try
            {
                string name = item.Name;
                string id = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;

                string? target = null;
                try { target = item.ExtendedProperty("System.Link.TargetParsingPath") as string; } catch { }

                string lower = name.ToLowerInvariant();
                if (SkippedWords.Any(lower.Contains)) continue;
                string probe = (target ?? id).ToLowerInvariant();
                if (SkippedExtensions.Any(probe.EndsWith)) continue;
                if (probe.StartsWith("http://") || probe.StartsWith("https://")) continue;

                list.Add(new AppEntry { Name = name, AppId = id, TargetPath = string.IsNullOrWhiteSpace(target) ? null : target });
            }
            catch { /* voce non leggibile: si salta */ }
        }

        return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ---- Avvio ---------------------------------------------------------------------------------

    /// <summary>Avvia l'app. Restituisce false solo se l'avvio come amministratore non è supportato da quell'app.</summary>
    public static bool Launch(PinnedApp app, bool asAdmin = false)
    {
        if (asAdmin) return RunAsAdmin(app);
        try
        {
            if (!string.IsNullOrEmpty(app.AppId))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + app.AppId) { UseShellExecute = false });
            }
            else if (!string.IsNullOrEmpty(app.TargetPath))
            {
                Process.Start(new ProcessStartInfo(app.TargetPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(app.TargetPath) ?? ""
                });
            }
        }
        catch (System.ComponentModel.Win32Exception) { /* annullato dall'utente (UAC) o file mancante */ }
        catch (Exception ex) { Log.Error($"Avvio di {app.Name} fallito", ex); }
        return true;
    }

    private const int ErrorCancelled = 1223; // l'utente ha rifiutato il prompt UAC

    /// <summary>
    /// Avvio con privilegi elevati per qualsiasi voce del menu Start: prima il file di destinazione (exe, bat, cmd,
    /// msc...), poi la voce di shell:AppsFolder, che espone il verbo "runas" per le app Win32 e per le app
    /// pacchettizzate che lo dichiarano (es. Windows Terminal). False se l'app non può essere elevata.
    /// </summary>
    private static bool RunAsAdmin(PinnedApp app)
    {
        var attempts = new List<ProcessStartInfo>();
        if (!string.IsNullOrEmpty(app.TargetPath) && System.IO.File.Exists(app.TargetPath))
            attempts.Add(new ProcessStartInfo(app.TargetPath)
            {
                UseShellExecute = true, Verb = "runas",
                WorkingDirectory = System.IO.Path.GetDirectoryName(app.TargetPath) ?? ""
            });
        if (!string.IsNullOrEmpty(app.AppId))
            attempts.Add(new ProcessStartInfo("shell:AppsFolder\\" + app.AppId) { UseShellExecute = true, Verb = "runas" });

        foreach (ProcessStartInfo psi in attempts)
        {
            try
            {
                Process.Start(psi);
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled) { return true; }
            catch (Exception ex) { Log.Warn($"Esegui come amministratore ({app.Name}, {psi.FileName}): {ex.Message}"); }
        }
        return false;
    }

    // ---- Apertura di file con un'app (file trascinati sulla sua icona) --------------------------
    // Stesso meccanismo di "Apri con" in Esplora file: l'app riceve i file solo se Windows la elenca per quel tipo di file
    // (vale anche per le app in pacchetto, es. Claude). Passare i file sulla riga di comando a un eseguibile qualsiasi
    // spesso non fa nulla, e il cursore "copia" prometterebbe un'apertura che non avviene.

    private static bool IsExplorer(string? exe, string? appId) =>
        appId == "Microsoft.Windows.Explorer" ||
        string.Equals(System.IO.Path.GetFileName(exe), "explorer.exe", StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, (DateTime At, List<(string Name, string UiName)> Handlers)> HandlerCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static (string Key, bool Result) _lastQuery;

    /// <summary>
    /// True se l'app può aprire tutti i file trascinati. Chiamata a ogni movimento del trascinamento: il risultato
    /// resta in memoria per lo stesso gruppo di file e la stessa app.
    /// </summary>
    public static bool CanOpenFiles(string name, string? exe, string? appId, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return false;
        if (IsExplorer(exe, appId)) return true;
        string key = string.Join("|", paths.Count, paths[0], paths[^1], name, exe, appId);
        if (_lastQuery.Key == key) return _lastQuery.Result;

        bool result = false;
        if (Extensions(paths) is { } extensions)
            result = extensions.All(ext => HandlersFor(ext).Any(h => HandlerMatches(h.Name, h.UiName, name, exe, appId)));
        _lastQuery = (key, result);
        return result;
    }

    /// <summary>Estensioni distinte dei file; null se c'è una cartella o un file senza estensione (nessuna app li "apre").</summary>
    private static List<string>? Extensions(IReadOnlyList<string> paths)
    {
        var list = new List<string>();
        foreach (string path in paths)
        {
            string ext = System.IO.Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext) || System.IO.Directory.Exists(path)) return null;
            if (!list.Contains(ext, StringComparer.OrdinalIgnoreCase)) list.Add(ext);
        }
        return list;
    }

    private static List<(string Name, string UiName)> HandlersFor(string ext)
    {
        lock (HandlerCache)
            if (HandlerCache.TryGetValue(ext, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(30))
                return cached.Handlers;

        var list = new List<(string, string)>();
        ForEachHandler(ext, (_, handlerName, uiName) =>
        {
            list.Add((handlerName, uiName));
            return false;
        });
        lock (HandlerCache) HandlerCache[ext] = (DateTime.UtcNow, list);
        return list;
    }

    /// <summary>Visita le app di "Apri con" per l'estensione finché <paramref name="visit"/> non restituisce true.</summary>
    private static void ForEachHandler(string ext, Func<Native.IAssocHandler, string, string, bool> visit)
    {
        Native.IEnumAssocHandlers? handlers = null;
        try
        {
            if (Native.SHAssocEnumHandlers(ext, 0, out handlers) != 0 || handlers == null) return;
            while (handlers.Next(1, out Native.IAssocHandler? handler, out int fetched) == 0 && fetched == 1 && handler != null)
            {
                try
                {
                    handler.GetName(out string handlerName);
                    handler.GetUIName(out string uiName);
                    if (visit(handler, handlerName ?? "", uiName ?? "")) return;
                }
                finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(handler); }
            }
        }
        catch (Exception ex) { Log.Warn($"Elenco \"Apri con\" per {ext}: {ex.Message}"); }
        finally { if (handlers != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(handlers); }
    }

    /// <summary>True se la voce di "Apri con" è l'app indicata (stesso eseguibile, stesso pacchetto o, per le UWP, stesso nome).</summary>
    private static bool HandlerMatches(string handlerName, string uiName, string name, string? exe, string? appId)
    {
        if (!handlerName.Contains('\\'))
            return !string.IsNullOrEmpty(name) && string.Equals(uiName, name, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(exe) && string.Equals(handlerName, exe, StringComparison.OrdinalIgnoreCase)) return true;

        // App in pacchetto: la voce può puntare all'alias in %LOCALAPPDATA%\Microsoft\WindowsApps\<famiglia>\ o alla
        // cartella d'installazione con la versione; conta solo la famiglia del pacchetto.
        string? family = !string.IsNullOrEmpty(appId) && appId.Contains('!') ? appId[..appId.IndexOf('!')] : PackageFamily(exe);
        return family != null && string.Equals(PackageFamily(handlerName), family, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"Nome_IdEditore" da un percorso dentro WindowsApps (cartella d'installazione "Nome_Versione_Arch__IdEditore" o alias "Nome_IdEditore").</summary>
    private static string? PackageFamily(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        const string marker = "\\WindowsApps\\";
        int start = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += marker.Length;
        int end = path.IndexOf('\\', start);
        if (end < 0) return null;
        string folder = path[start..end];
        int first = folder.IndexOf('_'), last = folder.LastIndexOf('_');
        return first > 0 && last < folder.Length - 1 ? folder[..first] + "_" + folder[(last + 1)..] : null;
    }

    /// <summary>Apre i file (o le cartelle, solo con Esplora file) con l'app indicata, come "Apri con" di Windows.</summary>
    public static void OpenFiles(string name, string? exe, string? appId, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        Native.AllowSetForegroundWindow(Native.ASFW_ANY); // la finestra che si apre deve poter venire in primo piano

        if (IsExplorer(exe, appId))
        {
            OpenInExplorer(paths);
            return;
        }

        // Le chiamate alla shell possono richiedere qualche istante: fuori dal thread dell'interfaccia, in un thread STA.
        var groups = paths.GroupBy(p => System.IO.Path.GetExtension(p), StringComparer.OrdinalIgnoreCase).ToList();
        var thread = new Thread(() =>
        {
            foreach (var group in groups)
            {
                bool opened = false;
                ForEachHandler(group.Key, (handler, handlerName, uiName) =>
                {
                    if (!HandlerMatches(handlerName, uiName, name, exe, appId)) return false;
                    opened = InvokeHandler(handler, group.ToList());
                    return true;
                });
                if (!opened) Log.Warn($"{name}: impossibile aprire i file {group.Key} trascinati sull'icona");
            }

            // Alcune attivazioni proseguono in modo asincrono sul thread che le ha chiamate: lo si tiene vivo (e in
            // ascolto dei messaggi) ancora qualche secondo.
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
        }) { IsBackground = true, Name = "OpSecBar.OpenFiles" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static bool InvokeHandler(Native.IAssocHandler handler, IReadOnlyList<string> paths)
    {
        var pidls = new List<IntPtr>();
        IntPtr items = IntPtr.Zero, data = IntPtr.Zero;
        try
        {
            foreach (string path in paths)
                if (Native.SHParseDisplayName(path, IntPtr.Zero, out IntPtr pidl, 0, out _) == 0 && pidl != IntPtr.Zero) pidls.Add(pidl);
            if (pidls.Count == 0 || Native.SHCreateShellItemArrayFromIDLists((uint)pidls.Count, pidls.ToArray(), out items) != 0) return false;

            var array = (Native.IShellItemArray)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(items);
            try
            {
                Guid bhid = Native.BHID_DataObject, iid = Native.IID_IDataObject;
                if (array.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out data) != 0 || data == IntPtr.Zero) return false;
            }
            finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(array); }

            int hr = handler.Invoke(data);
            if (hr != 0) Log.Warn($"Apri con: 0x{hr:X8}");
            return hr == 0;
        }
        catch (Exception ex)
        {
            Log.Error("Apri con", ex);
            return false;
        }
        finally
        {
            if (data != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(data);
            if (items != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(items);
            foreach (IntPtr pidl in pidls) System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>Esplora file non "apre" i file: mostra le cartelle e seleziona i file nella loro cartella.</summary>
    private static void OpenInExplorer(IReadOnlyList<string> paths)
    {
        foreach (string path in paths.Take(8)) // un trascinamento di centinaia di file non deve aprire centinaia di finestre
        {
            try
            {
                string argument = System.IO.Directory.Exists(path) ? $"\"{path}\"" : $"/select,\"{path}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", argument) { UseShellExecute = false })?.Dispose();
            }
            catch (Exception ex) { Log.Error("Apertura in Esplora file", ex); }
        }
    }

    public static void OpenFileLocation(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false }); }
        catch (Exception ex) { Log.Error("OpenFileLocation", ex); }
    }
}
