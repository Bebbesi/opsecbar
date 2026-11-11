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

    public static void Launch(PinnedApp app, bool asAdmin = false)
    {
        try
        {
            if (asAdmin && !string.IsNullOrEmpty(app.TargetPath))
            {
                Process.Start(new ProcessStartInfo(app.TargetPath) { UseShellExecute = true, Verb = "runas" });
            }
            else if (!string.IsNullOrEmpty(app.AppId))
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
    }

    public static void OpenFileLocation(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false }); }
        catch (Exception ex) { Log.Error("OpenFileLocation", ex); }
    }
}
