using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using OpSecBar.Core;
using OpSecBar.Models;

namespace OpSecBar.Services;

/// <summary>Carica le impostazioni, le salva automaticamente (con debounce) e notifica i cambiamenti.</summary>
public sealed class SettingsService
{
    private readonly DispatcherTimer _saveTimer;
    private ThemeData? _hookedTheme;

    public AppSettings Settings { get; }

    /// <summary>Nome della proprietà cambiata. Le proprietà del tema arrivano come "Theme.Xxx".</summary>
    public event Action<string>? Changed;

    public SettingsService()
    {
        Settings = Load();
        Settings.PropertyChanged += OnSettingsChanged;
        HookTheme();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions.Default);
                if (s != null)
                {
                    s.Theme ??= ThemeService.Presets[0].Clone();
                    s.PinnedApps ??= new();
                    s.TaskOrder ??= new();
                    s.StartFavorites ??= new();
                    s.RecentColors ??= new();
                    s.Gifs ??= new();
                    s.Layout?.Normalize();
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Impostazioni illeggibili, uso quelle predefinite", ex);
            try { File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + ".bak", true); } catch { }
        }
        return new AppSettings { Theme = ThemeService.Presets[0].Clone() };
    }

    private void HookTheme()
    {
        if (_hookedTheme != null) _hookedTheme.PropertyChanged -= OnThemeChanged;
        _hookedTheme = Settings.Theme;
        _hookedTheme.PropertyChanged += OnThemeChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Theme)) HookTheme();
        Notify(e.PropertyName ?? "");
    }

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e) => Notify("Theme." + e.PropertyName);

    /// <summary>Da chiamare dopo aver modificato un elenco (app fissate, icone tray...).</summary>
    public void Notify(string property)
    {
        ScheduleSave();
        Changed?.Invoke(property);
    }

    public void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        try
        {
            string tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, JsonOptions.Default));
            File.Move(tmp, AppPaths.SettingsFile, true);
        }
        catch (Exception ex) { Log.Error("Salvataggio impostazioni fallito", ex); }
    }
}
