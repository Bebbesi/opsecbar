using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ManagedShell;
using ManagedShell.AppBar;
using ManagedShell.Common.Enums;
using OpSecBar.Core;
using OpSecBar.Models;
using OpSecBar.Services;
using OpSecBar.Windows;

namespace OpSecBar;

/// <summary>Composizione dell'applicazione: servizi, finestre della barra e ciclo di vita (avvio, uscita, crash).</summary>
public partial class App : Application
{
    public static App Instance => (App)Current;

    public SettingsService SettingsService { get; private set; } = null!;
    public AppSettings Settings => SettingsService.Settings;
    public ShellManager Shell { get; private set; } = null!;
    public TaskListService TaskList { get; private set; } = null!;
    public MediaService Media { get; private set; } = null!;
    public SystemInfoService SysInfo { get; private set; } = null!;
    public AppCatalogService Catalog { get; } = new();
    public UpdateService Updates { get; } = new();

    /// <summary>Controlla l'ultima release su GitHub; con <paramref name="install"/> la scarica e la installa subito.</summary>
    public async void CheckForUpdates(bool install)
    {
        try
        {
            if (await Updates.CheckAsync(install) && !_exiting) InstallUpdate();
        }
        catch (Exception ex) { Log.Error("Controllo aggiornamenti", ex); }
    }

    /// <summary>Scarica (se serve) e applica l'aggiornamento trovato: l'app si chiude e riparte nella nuova versione.</summary>
    public async void InstallUpdate()
    {
        try
        {
            if (Updates.State == UpdateState.Available && !await Updates.DownloadAsync()) return;
            if (Updates.ApplyAndRestart()) ExitApp();
        }
        catch (Exception ex) { Log.Error("Installazione aggiornamento", ex); }
    }

    private readonly List<TaskbarWindow> _bars = new();
    private StartMenuWindow? _startMenu;
    private SettingsWindow? _settingsWindow;
    private Mutex? _mutex;
    private EventWaitHandle? _exitEvent, _showSettingsEvent;
    private DispatcherTimer? _recreateTimer;
    private WinKeyHook? _winKey;
    private bool _exiting, _started;
    private int _recentErrors;
    private DateTime _lastError = DateTime.MinValue;

    private const int EmergencyHotkeyId = 0x4E42;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _mutex = new Mutex(true, TaskbarGuard.MainMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Già in esecuzione: chiedi all'istanza attiva di aprire le Impostazioni.
            if (EventWaitHandle.TryOpenExisting(Program.ShowSettingsEventName, out EventWaitHandle? ev))
            {
                // Questa istanza è quella appena lanciata dall'utente: cede all'altra il diritto di andare in primo piano.
                Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                ev.Set();
                ev.Dispose();
            }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { if (_started && !_exiting) TaskbarGuard.RestoreWindowsTaskbar(); };
        SessionEnding += (_, _) => ExitApp();

        try
        {
            Log.Info($"Avvio OpSecBar {GetType().Assembly.GetName().Version} su {Environment.OSVersion.Version}");

            SettingsService = new SettingsService();
            ThemeService.ApplyResources(Settings.Theme);
            AutoStartService.Sync(Settings.AutoStart);

            ShellConfig config = ShellManager.DefaultShellConfig;
            config.EnableTasksService = true;
            config.AutoStartTasksService = false;
            config.EnableTrayService = true;
            config.AutoStartTrayService = true;
            config.TaskIconSize = IconSize.Large;
            if (Settings.PinnedTrayIcons != null) config.PinnedNotifyIcons = Settings.PinnedTrayIcons;
            Shell = new ShellManager(config);
            Shell.Tasks.Initialize(false);
            _started = true;

            ApplyTaskbarHiding();
            TaskbarGuard.StartWatchdog();

            TaskList = new TaskListService(Shell, SettingsService, Dispatcher);
            Media = new MediaService(Dispatcher);
            if (Settings.ShowMediaPlayer) Media.Start();
            SysInfo = new SystemInfoService();
            SysInfo.SetEnabled(Settings.ShowSysInfo);

            SettingsService.Changed += OnSettingChanged;
            // Se l'utente attiva/disattiva gli effetti di trasparenza di Windows, blur e acrylic si adeguano subito.
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (_exiting) return;
                ThemeService.ApplyResources(Settings.Theme);
                foreach (var bar in _bars) bar.ApplyTheme();
            });
            OpenTaskbars();
            RegisterSignals();
            ApplyWinKey();
            SeedDefaultsOnFirstRun();
            _ = Catalog.GetAppsAsync();

            if (e.Args.Contains("--settings")) ShowSettings();
            if (e.Args.Contains("--updated")) Log.Info($"Aggiornato alla versione {Updates.CurrentVersion}");

            // A ogni avvio, poco dopo che la barra è comparsa, si controlla se c'è una nuova release.
            var updateCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            updateCheck.Tick += (_, _) =>
            {
                updateCheck.Stop();
                CheckForUpdates(Settings.AutoUpdate);
            };
            updateCheck.Start();

            // A regime la barra usa pochissima memoria: dopo l'avvio si restituiscono a Windows le pagine non più usate.
            var trim = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            trim.Tick += (_, _) =>
            {
                trim.Stop();
                try
                {
                    GC.Collect(2, GCCollectionMode.Optimized, false);
                    Native.EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
                }
                catch { }
            };
            trim.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Avvio fallito", ex);
            TaskbarGuard.RestoreWindowsTaskbar();
            MessageBox.Show("OpSec Bar could not start. The Windows taskbar has been restored.\n\n" + ex.Message,
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // ---- Barre ---------------------------------------------------------------------------------

    public IReadOnlyList<TaskbarWindow> Bars => _bars;

    private void OpenTaskbars()
    {
        var screens = Settings.AllMonitors ? AppBarScreen.FromAllScreens() : new List<AppBarScreen> { AppBarScreen.FromPrimaryScreen() };
        foreach (AppBarScreen screen in screens)
        {
            try
            {
                var bar = new TaskbarWindow(this, screen);
                _bars.Add(bar);
                bar.Show();
            }
            catch (Exception ex) { Log.Error($"Barra non creata su {screen.DeviceName}", ex); }
        }
    }

    private void CloseTaskbars()
    {
        _startMenu?.HideMenu();
        foreach (TaskbarWindow bar in _bars.ToList())
        {
            try
            {
                bar.AllowClose = true;
                bar.Close();
            }
            catch (Exception ex) { Log.Error("Chiusura barra", ex); }
        }
        _bars.Clear();
    }

    /// <summary>Ricrea le barre (cambio di posizione, monitor collegati/scollegati...). Più richieste ravvicinate vengono unite.</summary>
    public void ScheduleRecreateTaskbars()
    {
        if (_exiting) return;
        if (_recreateTimer == null)
        {
            _recreateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _recreateTimer.Tick += (_, _) =>
            {
                _recreateTimer!.Stop();
                if (_exiting) return;
                CloseTaskbars();
                OpenTaskbars();
            };
        }
        _recreateTimer.Stop();
        _recreateTimer.Start();
    }

    private void ApplyTaskbarHiding()
    {
        if (Settings.HideWindowsTaskbar)
        {
            TaskbarGuard.PrepareHide();
            Shell.ExplorerHelper.HideExplorerTaskbar = true;
        }
        else
        {
            Shell.ExplorerHelper.HideExplorerTaskbar = false;
            TaskbarGuard.RestoreWindowsTaskbar();
        }
    }

    // ---- Impostazioni --------------------------------------------------------------------------

    private void OnSettingChanged(string name)
    {
        try
        {
            if (name.StartsWith("Theme", StringComparison.Ordinal))
            {
                ThemeService.ApplyResources(Settings.Theme);
                foreach (var bar in _bars) bar.ApplyTheme();
                _startMenu?.ApplyTheme();
                return;
            }

            switch (name)
            {
                case nameof(AppSettings.Position):
                case nameof(AppSettings.Behavior):
                case nameof(AppSettings.AllMonitors):
                    ScheduleRecreateTaskbars();
                    break;
                case nameof(AppSettings.HideWindowsTaskbar):
                    ApplyTaskbarHiding();
                    break;
                case nameof(AppSettings.AutoStart):
                    AutoStartService.Set(Settings.AutoStart);
                    break;
                case nameof(AppSettings.UseWinKey):
                    ApplyWinKey();
                    break;
                case nameof(AppSettings.PinnedApps):
                case nameof(AppSettings.ShowPinnedApps):
                case nameof(AppSettings.ShowRunningApps):
                case nameof(AppSettings.GroupWindows):
                    TaskList.ScheduleRebuild();
                    break;
                case nameof(AppSettings.StartFavorites):
                case nameof(AppSettings.StartShowFavorites):
                case nameof(AppSettings.StartMenuWidth):
                case nameof(AppSettings.StartMenuHeight):
                    _startMenu?.ApplyTheme();
                    break;
                default:
                    if (name == nameof(AppSettings.ShowMediaPlayer))
                    {
                        if (Settings.ShowMediaPlayer) Media.Start(); else Media.Stop();
                    }
                    if (name == nameof(AppSettings.ShowSysInfo)) SysInfo.SetEnabled(Settings.ShowSysInfo);
                    foreach (var bar in _bars) bar.ApplyElements();
                    break;
            }
        }
        catch (Exception ex) { Log.Error($"Applicazione impostazione {name}", ex); }
    }

    public void ShowSettings()
    {
        _startMenu?.HideMenu();
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
        // Se la richiesta arriva da una seconda istanza, Windows non porterebbe la finestra in primo piano.
        IntPtr handle = new WindowInteropHelper(_settingsWindow).Handle;
        Native.ForceForeground(handle);
        if (Native.GetForegroundWindow() != handle)
        {
            // Se Windows nega comunque il primo piano, almeno la finestra sale sopra le altre.
            _settingsWindow.Topmost = true;
            _settingsWindow.Topmost = false;
        }
    }

    public void ToggleStartMenu(TaskbarWindow bar, FrameworkElement anchor)
    {
        _startMenu ??= new StartMenuWindow(this);
        _startMenu.Toggle(bar, anchor);
    }

    public bool IsStartMenuOpen => _startMenu?.IsVisible == true;

    /// <summary>Installa o rimuove l'intercettazione del tasto Windows in base alle impostazioni.</summary>
    private void ApplyWinKey()
    {
        if (Settings.UseWinKey)
        {
            _winKey ??= new WinKeyHook(() => Dispatcher.BeginInvoke(ToggleStartMenuFromKeyboard));
            _winKey.Install();
        }
        else _winKey?.Uninstall();
    }

    /// <summary>Tasto Windows: apre/chiude il menu Start sulla barra del monitor in cui si trova il mouse.</summary>
    private void ToggleStartMenuFromKeyboard()
    {
        if (_exiting || _bars.Count == 0) return;
        try
        {
            TaskbarWindow? bar = null;
            if (Native.GetCursorPos(out Native.POINT p))
                bar = _bars.FirstOrDefault(b => b.Screen.Bounds.Contains(p.X, p.Y));
            bar ??= _bars.FirstOrDefault(b => b.Screen.Primary) ?? _bars[0];

            if (!IsStartMenuOpen) bar.Peek();
            ToggleStartMenu(bar, bar.StartAnchor);
        }
        catch (Exception ex) { Log.Error("Menu Start da tastiera", ex); }
    }

    private async void SeedDefaultsOnFirstRun()
    {
        try
        {
            if (System.IO.File.Exists(AppPaths.SettingsFile)) return;
            var apps = await Catalog.GetAppsAsync();
            foreach (string id in new[]
                     {
                         "Microsoft.Windows.Explorer",
                         "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel"
                     })
            {
                var entry = apps.FirstOrDefault(a => string.Equals(a.AppId, id, StringComparison.OrdinalIgnoreCase));
                if (entry == null) continue;
                Settings.StartFavorites.Add(entry.ToPinned());
                if (id == "Microsoft.Windows.Explorer") Settings.PinnedApps.Add(entry.ToPinned());
            }
            SettingsService.Notify(nameof(AppSettings.PinnedApps));
            SettingsService.Notify(nameof(AppSettings.StartFavorites));
            SettingsService.SaveNow();
        }
        catch (Exception ex) { Log.Error("SeedDefaults", ex); }
    }

    // ---- Segnali esterni e tasto di emergenza --------------------------------------------------

    private void RegisterSignals()
    {
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEventName);
        _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowSettingsEventName);
        ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => Dispatcher.BeginInvoke(() => ExitApp()), null, -1, false);
        ThreadPool.RegisterWaitForSingleObject(_showSettingsEvent, (_, _) => Dispatcher.BeginInvoke(ShowSettings), null, -1, false);

        // Ctrl+Alt+Shift+Q: chiude OpSecBar e ripristina la taskbar di Windows, qualunque cosa accada alla UI.
        if (Native.RegisterHotKey(IntPtr.Zero, EmergencyHotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT, 0x51))
            ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;
        else
            Log.Warn("Tasto di emergenza Ctrl+Alt+Shift+Q non registrato (già in uso)");
    }

    private void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message == Native.WM_HOTKEY && msg.wParam.ToInt32() == EmergencyHotkeyId)
        {
            handled = true;
            ExitApp();
        }
    }

    // ---- Uscita e gestione errori --------------------------------------------------------------

    public void ExitApp(bool restart = false)
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info(restart ? "Riavvio richiesto" : "Uscita richiesta");

        try { SettingsService?.SaveNow(); } catch { }
        try { _winKey?.Dispose(); } catch { }
        try { Native.UnregisterHotKey(IntPtr.Zero, EmergencyHotkeyId); } catch { }
        try { _settingsWindow?.Close(); } catch { }
        try { _startMenu?.ForceClose(); } catch { }
        try { Media?.Stop(); } catch { }
        try { Shell?.AppBarManager.SignalGracefulShutdown(); } catch { }
        try { CloseTaskbars(); } catch { }
        try { if (Shell != null) Shell.ExplorerHelper.HideExplorerTaskbar = false; } catch (Exception ex) { Log.Error("Ripristino via ExplorerHelper", ex); }
        try { Shell?.Dispose(); } catch (Exception ex) { Log.Error("Shell.Dispose", ex); }
        TaskbarGuard.RestoreWindowsTaskbar();

        try { _mutex?.ReleaseMutex(); _mutex?.Dispose(); } catch { }
        _mutex = null;

        if (restart)
        {
            try
            {
                if (Environment.ProcessPath is string exe)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false });
            }
            catch (Exception ex) { Log.Error("Riavvio fallito", ex); }
        }
        Shutdown();
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Eccezione non gestita (UI)", e.Exception);
        e.Handled = true;

        // Un errore isolato non deve far cadere la barra; una raffica di errori sì (e ripristina la taskbar di Windows).
        if ((DateTime.Now - _lastError).TotalSeconds > 20) _recentErrors = 0;
        _lastError = DateTime.Now;
        if (++_recentErrors >= 8)
        {
            Log.Error("Troppi errori consecutivi: esco e ripristino la taskbar di Windows");
            ExitApp();
        }
    }

    private void OnFatalException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Eccezione fatale", e.ExceptionObject as Exception);
        TaskbarGuard.RestoreWindowsTaskbar();
    }
}
