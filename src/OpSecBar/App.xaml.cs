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
    public AudioService Audio { get; private set; } = null!;
    public AppCatalogService Catalog { get; } = new();
    public UpdateService Updates { get; } = new();

    /// <summary>
    /// Controlla l'ultima release su GitHub (o la più recente in assoluto, con le pre-release attive); con
    /// <paramref name="install"/> la scarica e, se <paramref name="restart"/>, la installa subito.
    /// </summary>
    public async void CheckForUpdates(bool install, bool restart = true)
    {
        try
        {
            if (await Updates.CheckAsync(install, Settings.PreReleaseUpdates) && restart && !_exiting) InstallUpdate();
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
    private CalendarWindow? _calendar;
    private QuickSettingsWindow? _quickSettings;
    private SettingsWindow? _settingsWindow;
    private Mutex? _mutex;
    private EventWaitHandle? _exitEvent, _showSettingsEvent;
    private DispatcherTimer? _recreateTimer;
    private WinKeyHook? _winKey;
    private TrayKeeper? _trayKeeper;
    private bool _exiting, _started;
    private readonly long _startedAt = Environment.TickCount64;

    /// <summary>True da quando è iniziata l'uscita (o il riavvio) dell'app.</summary>
    public bool IsExiting => _exiting;
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
            if (Program.TryOpenEvent(Program.ShowSettingsEventName, out EventWaitHandle? ev))
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
            // Il tray parte per ultimo, a barre aperte: vedi StartTray.
            config.AutoStartTrayService = false;
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
            Audio = new AudioService(Dispatcher);
            Audio.SetEnabled(Settings.ShowVolume);

            SettingsService.Changed += OnSettingChanged;
            // Se l'utente attiva/disattiva gli effetti di trasparenza di Windows, blur e acrylic si adeguano subito.
            // L'avviso arriva a raffiche (e per qualsiasi preferenza, anche un cambio di sfondo): le si unisce in un solo
            // aggiornamento, fatto quando la UI è libera.
            bool preferenceRefreshPending = false;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (_exiting || preferenceRefreshPending) return;
                preferenceRefreshPending = true;
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                {
                    preferenceRefreshPending = false;
                    if (_exiting) return;
                    ThemeService.ApplyResources(Settings.Theme);
                    foreach (var bar in _bars) bar.ApplyTheme();
                });
            });
            OpenTaskbars();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, StartTray);
            RegisterSignals();
            ApplyWinKey();
            SeedDefaultsOnFirstRun();
            _ = Catalog.GetStartAppsAsync(Settings.StartAppSource);
            if (Settings.ShowQuickSettings && Settings.CustomQuickSettings) QuickSettingsWindow.Preload();

            if (e.Args.Contains("--settings")) ShowSettings();
            if (e.Args.Contains("--updated")) Log.Info($"Aggiornato alla versione {Updates.CurrentLabel}");

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

            // Di norma un paio di secondi, ma la registrazione delle barre presso Explorer a volte resta ferma molto
            // più a lungo (causa ancora da trovare): il tempo nel log dice quando succede.
            Log.Info($"Barre aperte in {Environment.TickCount64 - _startedAt} ms");
        }
        catch (Exception ex)
        {
            Log.Error("Avvio fallito", ex);
            TaskbarGuard.RestoreWindowsTaskbar();
            MessageBox.Show("Open TaskBar could not start. The Windows taskbar has been restored.\n\n" + ex.Message,
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // ---- Barre ---------------------------------------------------------------------------------

    public IReadOnlyList<TaskbarWindow> Bars => _bars;

    /// <summary>Lato su cui la taskbar di Windows nascosta lascia una fascia vuota su uno degli schermi; -1 se nessuno.</summary>
    public int HiddenTaskbarGapEdge()
    {
        foreach (TaskbarWindow bar in _bars)
        {
            try
            {
                int edge = bar.HiddenTaskbarGapEdge();
                if (edge >= 0) return edge;
            }
            catch (Exception ex) { Log.Error("HiddenTaskbarGapEdge", ex); }
        }
        return -1;
    }

    private string _screenLayout = "";
    private bool _recreateOnlyIfScreensChanged;

    /// <summary>Impronta della disposizione degli schermi (quali, dove, quanto grandi) con cui sono state create le barre.</summary>
    private static string ScreenLayout() =>
        string.Join("|", AppBarScreen.FromAllScreens().Select(s => $"{s.DeviceName}:{s.Bounds}:{s.Primary}"));

    private void OpenTaskbars()
    {
        _screenLayout = ScreenLayout();
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
        HideFlyouts();
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

    /// <summary>
    /// Ricrea le barre (cambio di posizione, monitor collegati/scollegati...). Più richieste ravvicinate vengono unite.
    /// Con <paramref name="onlyIfScreensChanged"/> le barre vengono ricreate solo se la disposizione degli schermi è
    /// davvero cambiata; altrimenti ci si limita a riconfermarne la posizione.
    /// </summary>
    public void ScheduleRecreateTaskbars(bool onlyIfScreensChanged = false)
    {
        if (_exiting) return;
        // Una richiesta incondizionata già in attesa (es. cambio di impostazioni) prevale su quelle condizionate.
        bool pending = _recreateTimer?.IsEnabled == true;
        _recreateOnlyIfScreensChanged = onlyIfScreensChanged && (!pending || _recreateOnlyIfScreensChanged);
        if (_recreateTimer == null)
        {
            _recreateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _recreateTimer.Tick += (_, _) =>
            {
                _recreateTimer!.Stop();
                if (_exiting) return;
                if (_recreateOnlyIfScreensChanged && ScreenLayout() == _screenLayout)
                {
                    foreach (TaskbarWindow bar in _bars)
                    {
                        try { bar.UpdatePosition(); } catch (Exception ex) { Log.Error("UpdatePosition", ex); }
                    }
                    return;
                }
                CloseTaskbars();
                OpenTaskbars();
            };
        }
        _recreateTimer.Stop();
        _recreateTimer.Start();
    }

    /// <summary>
    /// Avvia l'area di notifica. Appena parte, il tray chiede a tutte le app di registrare la propria icona: ogni app
    /// risponde subito e aspetta solo pochi secondi che la barra le dia retta. Va quindi fatto per ultimo, a barre
    /// aperte e con la UI libera:
    /// - prima di nascondere la taskbar di Windows no, perché nasconderla porta per un attimo quella di Explorer
    ///   davanti alla nostra finestra, e le icone registrate in quell'attimo finivano su Explorer invece che sulla
    ///   barra (ad alcuni avvii sì e ad altri no, a seconda dei tempi);
    /// - prima di aprire le barre nemmeno: registrare una barra presso Explorer a volte tiene ferma la UI a lungo
    ///   (misurati anche più di un minuto), e le app che in quel tempo provano a registrarsi rinunciano.
    /// </summary>
    private void StartTray()
    {
        if (_exiting) return;
        try
        {
            Shell.NotificationArea.Initialize();
            if (Shell.NotificationArea.IsFailed) Log.Warn("Area di notifica non avviata");
            // Le barre sono nate prima del tray: ora possono mostrarne le icone.
            foreach (TaskbarWindow bar in _bars) bar.AttachTray();
            _trayKeeper = new TrayKeeper(Shell);
        }
        catch (Exception ex) { Log.Error("Avvio dell'area di notifica", ex); }
    }

    private void ApplyTaskbarHiding()
    {
        if (Settings.HideWindowsTaskbar)
        {
            TaskbarGuard.PrepareHide();
            Shell.ExplorerHelper.HideExplorerTaskbar = true;
            // Tentativo di liberare lo spazio che la taskbar nascosta continuerebbe a riservare. Le build recenti di
            // Windows 11 lo ignorano: in quel caso ci pensa TaskbarWindow, disegnandosi sopra la fascia riservata.
            TaskbarGuard.ForceAutoHide();
        }
        else
        {
            Shell.ExplorerHelper.HideExplorerTaskbar = false;
            TaskbarGuard.RestoreWindowsTaskbar();
        }

        // Leggere e cambiare lo stato della taskbar di Windows la porta per un attimo davanti al nostro tray.
        _trayKeeper?.RequestRefresh();

        // Le barre già aperte si riposizionano: sopra la fascia della taskbar nascosta, oppure di nuovo accanto a quella visibile.
        foreach (TaskbarWindow bar in _bars)
        {
            try { bar.UpdatePosition(); } catch (Exception ex) { Log.Error("UpdatePosition", ex); }
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
                foreach (FlyoutWindow flyout in Flyouts) flyout.ApplyTheme();
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
                case nameof(AppSettings.HideOnFullscreen):
                    foreach (var bar in _bars) bar.SyncFullScreenState();
                    break;
                case nameof(AppSettings.AutoStart):
                    AutoStartService.Set(Settings.AutoStart);
                    break;
                case nameof(AppSettings.UseWinKey):
                    ApplyWinKey();
                    break;
                case nameof(AppSettings.PinnedApps):
                case nameof(AppSettings.TaskOrder):
                case nameof(AppSettings.ShowPinnedApps):
                case nameof(AppSettings.ShowRunningApps):
                case nameof(AppSettings.GroupWindows):
                    TaskList.ScheduleRebuild();
                    break;
                case nameof(AppSettings.PreReleaseUpdates):
                    // Cambio di canale: si cerca subito la versione giusta (spegnendolo si torna all'ultima release).
                    // Con l'aggiornamento automatico la si scarica, ma il riavvio resta all'utente.
                    CheckForUpdates(Settings.AutoUpdate, restart: false);
                    break;
                case nameof(AppSettings.AutoUpdate):
                case nameof(AppSettings.ShowWindowPreviews): // letto al passaggio del mouse
                case nameof(AppSettings.CustomCalendar):     // letti al click su orologio e impostazioni rapide
                case nameof(AppSettings.CustomQuickSettings):
                case nameof(AppSettings.RecentColors):
                case nameof(AppSettings.PinnedTrayIcons):
                    break; // solo da salvare
                case nameof(AppSettings.StartAppSource):
                    // L'elenco si prepara subito, così alla prossima apertura il menu mostra già la sorgente giusta.
                    if (_startMenu != null) _startMenu.LoadApps();
                    else _ = Catalog.GetStartAppsAsync(Settings.StartAppSource);
                    break;
                case nameof(AppSettings.StartFavorites):
                case nameof(AppSettings.StartShowFavorites):
                case nameof(AppSettings.StartShowRecent):
                case nameof(AppSettings.StartHideUserName):
                case nameof(AppSettings.StartAppLayout):
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
                    if (name == nameof(AppSettings.ShowVolume)) Audio.SetEnabled(Settings.ShowVolume);
                    foreach (var bar in _bars) bar.ApplyElements();
                    break;
            }
        }
        catch (Exception ex) { Log.Error($"Applicazione impostazione {name}", ex); }
    }

    public void ShowSettings() => ShowSettings(null);

    /// <summary>Apre le Impostazioni, facoltativamente su una pagina precisa (es. "GIFs").</summary>
    public void ShowSettings(string? page)
    {
        HideFlyouts();
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        if (page != null) _settingsWindow.ShowPage(page);
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

    /// <summary>Calendario di Open TaskBar (si apre dall'orologio).</summary>
    public void ToggleCalendar(TaskbarWindow bar, FrameworkElement anchor)
    {
        _calendar ??= new CalendarWindow(this);
        _calendar.Toggle(bar, anchor);
    }

    /// <summary>Impostazioni rapide di Open TaskBar: rete, Bluetooth, volume.</summary>
    public void ToggleQuickSettings(TaskbarWindow bar, FrameworkElement anchor)
    {
        _quickSettings ??= new QuickSettingsWindow(this);
        _quickSettings.Toggle(bar, anchor);
    }

    public bool IsStartMenuOpen => _startMenu?.IsVisible == true;

    /// <summary>I pannelli già creati (menu Start, calendario, impostazioni rapide).</summary>
    private IEnumerable<FlyoutWindow> Flyouts => new FlyoutWindow?[] { _startMenu, _calendar, _quickSettings }.OfType<FlyoutWindow>();

    /// <summary>True se uno dei pannelli è aperto: la barra non si nasconde finché resta aperto.</summary>
    public bool IsFlyoutOpen => Flyouts.Any(f => f.IsVisible);

    private void HideFlyouts()
    {
        foreach (FlyoutWindow flyout in Flyouts) flyout.HideFlyout();
    }

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

    // ---- Disinstallazione ----------------------------------------------------------------------

    /// <summary>Il disinstallatore creato dal programma di installazione; null se Open TaskBar non è stato installato così.</summary>
    public static string? UninstallerPath
    {
        get
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "unins000.exe");
            return System.IO.File.Exists(path) ? path : null;
        }
    }

    /// <summary>
    /// Avvia il disinstallatore. La barra resta aperta finché l'utente non conferma: il disinstallatore la chiude da sé
    /// (e ripristina la taskbar di Windows) solo dopo il "Sì", così annullando non si perde la barra.
    /// </summary>
    public void Uninstall()
    {
        if (UninstallerPath is not { } path) return;
        try
        {
            Log.Info("Disinstallazione richiesta");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Error("Avvio del disinstallatore", ex); }
    }

    // ---- Uscita e gestione errori --------------------------------------------------------------

    public void ExitApp(bool restart = false)
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info(restart ? "Riavvio richiesto" : "Uscita richiesta");

        try { SettingsService?.SaveNow(); } catch { }
        try { _winKey?.Dispose(); } catch { }
        try { _trayKeeper?.Dispose(); } catch { }
        try { Native.UnregisterHotKey(IntPtr.Zero, EmergencyHotkeyId); } catch { }
        try { _settingsWindow?.Close(); } catch { }
        foreach (FlyoutWindow flyout in Flyouts.ToList())
        {
            try { flyout.ForceClose(); } catch { }
        }
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
        if (_exiting) return;

        // Il thread di rendering di WPF è morto (driver video riavviato, memoria video esaurita, cambio di modalità
        // dello schermo...): da qui la barra non si ridisegna più e ogni operazione grafica fallisce. Non si recupera
        // dall'interno: si riavvia la barra. Se succede appena avviati non si insiste, per non riavviarsi all'infinito.
        if (IsRenderThreadFailure(e.Exception))
        {
            bool restart = Environment.TickCount64 - _startedAt > 60_000;
            Log.Error(restart ? "Rendering di WPF interrotto: riavvio la barra" : "Rendering di WPF interrotto subito dopo l'avvio: esco");
            ExitApp(restart);
            return;
        }

        // Un errore isolato non deve far cadere la barra; una raffica di errori sì (e ripristina la taskbar di Windows).
        if ((DateTime.Now - _lastError).TotalSeconds > 20) _recentErrors = 0;
        _lastError = DateTime.Now;
        if (++_recentErrors >= 8)
        {
            Log.Error("Troppi errori consecutivi: esco e ripristino la taskbar di Windows");
            ExitApp();
        }
    }

    private static bool IsRenderThreadFailure(Exception? ex)
    {
        for (; ex != null; ex = ex.InnerException)
            if (ex is System.Runtime.InteropServices.COMException { HResult: unchecked((int)0x88980406) }) return true; // UCEERR_RENDERTHREADFAILURE
        return false;
    }

    private void OnFatalException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Eccezione fatale", e.ExceptionObject as Exception);
        TaskbarGuard.RestoreWindowsTaskbar();
    }
}
