using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using OpSecBar.Controls;
using OpSecBar.Core;
using OpSecBar.Models;
using OpSecBar.Services;

namespace OpSecBar.Windows;

/// <summary>
/// La barra. È una AppBar registrata presso Windows: riserva lo spazio sullo schermo (le finestre massimizzate
/// non la coprono), gestisce auto-hide, schermo intero, DPI e più monitor tramite la classe base di ManagedShell.
/// </summary>
public partial class TaskbarWindow : AppBarWindow
{
    private readonly App _app;
    private readonly AppSettings _s;
    private readonly DispatcherTimer _clock;
    private AppItem? _pressedItem;
    private bool _pressedWasActive;
    private int _openPopups;
    private bool _hiddenByAutoHide, _shapeUpdatePending;
    private BackdropWindow? _backdrop;

    public TaskbarWindow(App app, AppBarScreen screen)
        : base(app.Shell.AppBarManager, app.Shell.ExplorerHelper, app.Shell.FullScreenHelper, screen,
            ToEdge(app.Settings.Position),
            app.Settings.Behavior == BarBehavior.AutoHide ? AppBarMode.AutoHide : AppBarMode.Normal,
            BarThickness(app.Settings.Theme))
    {
        _app = app;
        _s = app.Settings;
        InitializeComponent();

        AutoHideElement = RootGrid;
        AutoHideShowMargin = 3;

        TaskItems.ItemsSource = app.TaskList.Items;
        Media.Attach(app.Media, AppBarEdge);
        Media.FlyoutOpenChanged += OnPopupStateChanged;
        Tray.Attach(app.Shell.NotificationArea, AppBarEdge);
        Tray.OverflowOpenChanged += OnPopupStateChanged;
        app.SysInfo.Updated += UpdateSysInfo;
        app.Audio.Changed += UpdateVolume;

        ApplyLayout();
        ApplyElements();
        ApplyTheme();

        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();

        BarBorder.SizeChanged += (_, _) => UpdateShape();
        Loaded += (_, _) => UpdateShape();
        // Mostrare la finestra può riportarla in cima alle finestre normali: se ha ceduto il posto a un'app a schermo
        // intero la si rimette in fondo.
        ContentRendered += (_, _) => { if (!Topmost && Handle != IntPtr.Zero) WindowHelper.ShowWindowBottomMost(Handle); };
        IsVisibleChanged += (_, _) => ScheduleShapeUpdate();
        Closed += (_, _) =>
        {
            _clock.Stop();
            _app.SysInfo.Updated -= UpdateSysInfo;
            _app.Audio.Changed -= UpdateVolume;
            _backdrop?.Dispose();
            _backdrop = null;
        };
    }

    private static AppBarEdge ToEdge(BarPosition p) => p switch
    {
        BarPosition.Bottom => AppBarEdge.Bottom,
        BarPosition.Left => AppBarEdge.Left,
        BarPosition.Right => AppBarEdge.Right,
        _ => AppBarEdge.Top
    };

    /// <summary>Spessore totale riservato: barra + eventuali margini della barra flottante.</summary>
    private static double BarThickness(ThemeData t) =>
        Math.Clamp(t.BarHeight, 24, 96) + (t.Margin > 0 ? Math.Clamp(t.Margin, 0, 40) * 1.5 : 0);

    private bool IsHorizontal => Orientation == Orientation.Horizontal;

    // ---- Layout ----------------------------------------------------------------------------------

    private void ApplyLayout()
    {
        WidgetPanel.Orientation = Orientation;
        if (IsHorizontal)
        {
            WidgetPanel.Margin = new Thickness(6, 0, 6, 0);
            return;
        }

        WidgetPanel.Margin = new Thickness(0, 6, 0, 6);
        GifHost.Orientation = Orientation.Vertical;
        GifHost.HorizontalAlignment = HorizontalAlignment.Center;
        VolumePanel.Orientation = Orientation.Vertical;
        VolumeText.Margin = new Thickness(0, 3, 0, 0);
        VolumeText.FontSize = 10;

        TasksScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        TasksScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        TasksScroll.HorizontalAlignment = HorizontalAlignment.Center;

        ClockPanel.Orientation = Orientation.Vertical;
        ClockDate.Margin = new Thickness(0);
        ClockDate.FontSize = 10;
        ClockDate.TextAlignment = TextAlignment.Center;
        SysInfoText.Margin = new Thickness(0, 6, 0, 6);
        SysInfoText.FontSize = 10;

        foreach (Button b in new[] { ClockButton, VolumeButton, QuickButton, StartButton })
        {
            b.Padding = new Thickness(0, 7, 0, 7);
            b.Margin = new Thickness(3, 1, 3, 1);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private FrameworkElement ElementOf(BarWidget widget) => widget switch
    {
        BarWidget.Apps => TasksScroll,
        BarWidget.Media => Media,
        BarWidget.Gifs => GifHost,
        BarWidget.SysInfo => SysInfoText,
        BarWidget.Volume => VolumeButton,
        BarWidget.Clock => ClockButton,
        BarWidget.Tray => Tray,
        BarWidget.QuickSettings => QuickButton,
        _ => StartButton
    };

    /// <summary>
    /// Assegna a ogni elemento la sua zona e il suo ordine: disposizione classica, oppure quella del layout avanzato.
    /// Gli elementi non vengono spostati nell'albero visuale: ci pensa <see cref="BarPanel"/> a disporli.
    /// </summary>
    private void ApplyWidgetLayout()
    {
        BarLayout layout = _s.EffectiveLayout;
        int order = 0;
        foreach (BarZone zone in Enum.GetValues<BarZone>())
        {
            foreach (BarWidget widget in layout[zone])
            {
                FrameworkElement element = ElementOf(widget);
                BarPanel.SetZone(element, zone);
                BarPanel.SetOrder(element, order++);
            }
        }
    }

    /// <summary>Mostra/nasconde gli elementi opzionali in base alle impostazioni.</summary>
    public void ApplyElements()
    {
        ApplyWidgetLayout();
        Media.Configure(_s.ShowMediaPlayer, _s.ShowMediaTitle);
        ClockButton.Visibility = Vis(_s.ShowClock);
        Tray.Visibility = Vis(_s.ShowTray);
        QuickButton.Visibility = Vis(_s.ShowQuickSettings);
        StartButton.Visibility = Vis(_s.ShowStartButton);
        SysInfoText.Visibility = Vis(_s.ShowSysInfo);
        VolumeButton.Visibility = Vis(_s.ShowVolume);
        ApplyClockLayout();
        _lastClock = "";
        UpdateClock();
        UpdateSysInfo();
        UpdateVolume();
        BuildGifs();
    }

    private static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    // ---- Aspetto ---------------------------------------------------------------------------------

    public void ApplyTheme()
    {
        ThemeData t = _s.Theme;
        double m = Math.Clamp(t.Margin, 0, 40);
        double radius = Math.Clamp(t.CornerRadius, 0, 48);

        // Dimensione della AppBar
        double size = BarThickness(t);
        bool sizeChanged;
        if (IsHorizontal)
        {
            sizeChanged = Math.Abs(DesiredHeight - size) > 0.01;
            DesiredHeight = size;
        }
        else
        {
            sizeChanged = Math.Abs(DesiredWidth - size) > 0.01;
            DesiredWidth = size;
        }
        if (sizeChanged && Handle != IntPtr.Zero)
        {
            try { UpdatePosition(); } catch (Exception ex) { Log.Error("UpdatePosition", ex); }
        }

        // Margini: pieni verso il bordo dello schermo e ai lati, dimezzati verso il desktop.
        Thickness margin = m <= 0
            ? new Thickness(0)
            : AppBarEdge switch
            {
                AppBarEdge.Bottom => new Thickness(m, m / 2, m, m),
                AppBarEdge.Left => new Thickness(m, m, m / 2, m),
                AppBarEdge.Right => new Thickness(m / 2, m, m, m),
                _ => new Thickness(m, m, m, m / 2)
            };
        BarBorder.Margin = margin;
        BarBorder.CornerRadius = new CornerRadius(radius);

        // Sfondo
        Color bg = ThemeService.ParseColor(t.BackgroundColor, Color.FromRgb(0x1B, 0x1C, 0x22));
        double opacity = Math.Clamp(t.Opacity, 0, 1);
        Color hitTestOnly = Color.FromArgb(1, bg.R, bg.G, bg.B); // quasi invisibile ma cliccabile
        bool blurActive = BackdropHelper.BlurActive(t.Backdrop);
        // Secondo colore facoltativo: lo sfondo diventa una sfumatura lungo la barra.
        bool hasBg2 = !string.IsNullOrWhiteSpace(t.BackgroundColor2);
        Color bg2 = ThemeService.ParseColor(t.BackgroundColor2, bg);
        double angle = IsHorizontal ? 0 : 90;
        BarBorder.Background = t.Backdrop switch
        {
            BackdropMode.Transparent => new SolidColorBrush(hitTestOnly),
            // Con l'acrylic il colore lo mette Windows; se la trasparenza di sistema è spenta lo disegna la barra.
            BackdropMode.Acrylic when blurActive => new SolidColorBrush(hitTestOnly),
            BackdropMode.Glass when hasBg2 => new LinearGradientBrush(
                ThemeService.WithAlpha(bg, Math.Min(1, opacity * 1.35)),
                ThemeService.WithAlpha(bg2, opacity * 0.6), angle),
            BackdropMode.Glass => new LinearGradientBrush(
                ThemeService.WithAlpha(bg, Math.Min(1, opacity * 1.35)),
                ThemeService.WithAlpha(bg, opacity * 0.6),
                IsHorizontal ? 90 : 0),
            _ when hasBg2 => new LinearGradientBrush(
                ThemeService.WithAlpha(bg, Math.Max(opacity, 1.0 / 255)),
                ThemeService.WithAlpha(bg2, Math.Max(opacity, 1.0 / 255)), angle),
            _ => new SolidColorBrush(ThemeService.WithAlpha(bg, Math.Max(opacity, 1.0 / 255)))
        };

        // Bordo: spessore, lati (tutti / solo verso il desktop / solo verso il bordo dello schermo), tinta unita o sfumata.
        double border = Math.Clamp(t.BorderThickness, 0, 6);
        BarBorder.BorderThickness = border <= 0 ? new Thickness(0) : t.BorderSides switch
        {
            BorderSides.DesktopSide => AppBarEdge switch
            {
                AppBarEdge.Bottom => new Thickness(0, border, 0, 0),
                AppBarEdge.Left => new Thickness(0, 0, border, 0),
                AppBarEdge.Right => new Thickness(border, 0, 0, 0),
                _ => new Thickness(0, 0, 0, border)
            },
            BorderSides.ScreenSide => AppBarEdge switch
            {
                AppBarEdge.Bottom => new Thickness(0, 0, 0, border),
                AppBarEdge.Left => new Thickness(border, 0, 0, 0),
                AppBarEdge.Right => new Thickness(0, 0, border, 0),
                _ => new Thickness(0, border, 0, 0)
            },
            _ => new Thickness(border)
        };
        Color bc = ThemeService.ParseColor(t.BorderColor, Color.FromArgb(0x33, 255, 255, 255));
        BarBorder.BorderBrush = border <= 0 ? null
            : string.IsNullOrWhiteSpace(t.BorderColor2) ? new SolidColorBrush(bc)
            : new LinearGradientBrush(bc, ThemeService.ParseColor(t.BorderColor2, bc), angle);

        // Ombra o alone colorato: richiede spazio trasparente attorno alla barra, quindi un margine > 0 (barra flottante).
        bool shadow = t.Shadow && m > 0;
        ShadowLayer.Visibility = Vis(shadow);
        ShadowShape.Margin = margin;
        ShadowShape.CornerRadius = new CornerRadius(radius);
        Color sc = ThemeService.ParseColor(t.ShadowColor, Colors.Black);
        double strength = sc.A / 255.0;
        ShadowShape.Background = new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B));
        ShadowShape.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(sc.R, sc.G, sc.B), BlurRadius = Math.Clamp(t.ShadowSize, 2, 40),
            ShadowDepth = t.ShadowGlow ? 0 : 2, Direction = 270, Opacity = (t.ShadowGlow ? 0.95 : 0.5) * strength
        };

        ApplyWidgetStyle(t);
        BuildGifs();

        // In auto-hide la finestra deve restare "toccabile" anche dove è trasparente.
        RootGrid.Background = AppBarMode == AppBarMode.AutoHide ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : null;

        UpdateClock();
        UpdateShape();
    }

    /// <summary>
    /// Posiziona la lastra sfocata (blur/acrylic/glass) esattamente dietro la barra, con gli stessi angoli arrotondati,
    /// e aggiorna il ritaglio dell'ombra.
    /// </summary>
    private void UpdateShape()
    {
        _shapeUpdatePending = false;
        if (Handle == IntPtr.Zero || !IsLoaded || IsClosing) return;
        ThemeData t = _s.Theme;
        // Niente lastra se la barra è nascosta (auto-hide) o ha ceduto il posto a un'app a schermo intero.
        bool blur = BackdropHelper.BlurActive(t.Backdrop) && !_hiddenByAutoHide && Topmost && IsVisible && BarBorder.ActualWidth > 0;
        Color bg = ThemeService.ParseColor(t.BackgroundColor, Color.FromRgb(0x1B, 0x1C, 0x22));

        try
        {
            if (blur)
            {
                var source = PresentationSource.FromVisual(BarBorder);
                double scale = source?.CompositionTarget?.TransformToDevice.M11 ?? (DpiScale > 0 ? DpiScale : 1);
                Point origin = BarBorder.PointToScreen(new Point(0, 0));
                _backdrop ??= new BackdropWindow();
                _backdrop.Show(Handle, (int)Math.Round(origin.X), (int)Math.Round(origin.Y),
                    (int)Math.Round(BarBorder.ActualWidth * scale), (int)Math.Round(BarBorder.ActualHeight * scale),
                    (int)Math.Round(Math.Clamp(t.CornerRadius, 0, 48) * scale), t.Backdrop, bg, t.Opacity);
            }
            else _backdrop?.Hide();

            if (ShadowLayer.Visibility == Visibility.Visible && BarBorder.ActualWidth > 0)
            {
                Point o = BarBorder.TranslatePoint(new Point(0, 0), ShadowLayer);
                double r = Math.Clamp(t.CornerRadius, 0, 48);
                var outer = new RectangleGeometry(new Rect(-50, -50, ShadowLayer.ActualWidth + 100, ShadowLayer.ActualHeight + 100));
                var inner = new RectangleGeometry(new Rect(o.X, o.Y, BarBorder.ActualWidth, BarBorder.ActualHeight), r, r);
                ShadowLayer.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            }
        }
        catch (Exception ex) { Log.Error("UpdateShape", ex); }
    }

    // ---- Comportamento AppBar --------------------------------------------------------------------

    protected override void OnSourceInitialized(object sender, EventArgs e)
    {
        base.OnSourceInitialized(sender, e);
        if (CoversHiddenTaskbar) UpdatePosition(); // la registrazione di ManagedShell ci ha messo dopo la taskbar nascosta
        SyncFullScreenState(); // prima che la finestra compaia: se c'è un gioco a schermo intero non deve mai finirgli sopra
        UpdateShape();
    }

    // ---- Taskbar di Windows nascosta ma ancora ancorata ----------------------------------------
    // Explorer non accetta più ABM_SETSTATE (auto-hide) e la sua taskbar, pur nascosta, resta registrata come AppBar:
    // riserva il suo lato (es. 53 px a sinistra) e ABM_QUERYPOS/SETPOS spingono OpSecBar oltre, lasciando una fascia
    // vuota tra il bordo dello schermo e la barra. Soluzione: la finestra si disegna a filo del bordo, sopra la
    // fascia della taskbar nascosta, e presso Windows si registra solo lo spessore che eccede quella fascia.
    // L'area di lavoro risultante coincide così esattamente con il bordo interno della barra.

    private static readonly FieldInfo? AppBarMessageIdField =
        typeof(AppBarWindow).GetField("AppBarMessageId", BindingFlags.Instance | BindingFlags.NonPublic);
    private int _appBarMessageId = -1;

    private bool CoversHiddenTaskbar =>
        _s.HideWindowsTaskbar && AppBarMode == AppBarMode.Normal && !DeferWorkArea && Handle != IntPtr.Zero;

    public override bool UpdatePosition()
    {
        if (CoversHiddenTaskbar)
        {
            try
            {
                if (TryPositionOverHiddenTaskbar(out bool moved)) return moved;
            }
            catch (Exception ex) { Log.Error("Posizionamento sopra la taskbar nascosta", ex); }
        }
        return base.UpdatePosition();
    }

    private bool TryPositionOverHiddenTaskbar(out bool moved)
    {
        moved = false;
        NativeMethods.Rect desired = GetDesiredRect(); // a filo del bordo, spessore pieno
        int edge = (int)AppBarEdge;
        bool vertical = AppBarEdge is AppBarEdge.Left or AppBarEdge.Right;
        int thickness = vertical ? desired.Right - desired.Left : desired.Bottom - desired.Top;
        var bounds = new Native.RECT
        {
            Left = (int)Screen.Bounds.Left, Top = (int)Screen.Bounds.Top,
            Right = (int)Screen.Bounds.Right, Bottom = (int)Screen.Bounds.Bottom
        };

        int taskbar = TaskbarGuard.DockedTaskbarThickness(bounds, edge);
        if (taskbar <= 0) return false;

        // Di quanto Windows ci sposterebbe verso l'interno: se la taskbar non riserva spazio (es. auto-hide) è 0.
        var abd = new Native.APPBARDATA
        {
            cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = Handle, uEdge = (uint)edge, rc = ToNative(desired)
        };
        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
        int pushed = AppBarEdge switch
        {
            AppBarEdge.Left => abd.rc.Left - desired.Left,
            AppBarEdge.Right => desired.Right - abd.rc.Right,
            AppBarEdge.Top => abd.rc.Top - desired.Top,
            _ => desired.Bottom - abd.rc.Bottom
        };
        int covered = Math.Min(taskbar, pushed);
        if (covered <= 0) return false;

        // Si registra solo la parte che sporge oltre la fascia della taskbar nascosta.
        int reserve = Math.Max(1, thickness - covered);
        abd.rc = ToNative(desired);
        Shrink(ref abd.rc, reserve);
        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
        Shrink(ref abd.rc, reserve);
        Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);

        // La finestra parte dal bordo interno dell'area riservata e si estende per tutto lo spessore verso lo schermo.
        Native.RECT r = abd.rc;
        var win = new NativeMethods.Rect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
        switch (AppBarEdge)
        {
            case AppBarEdge.Left:
                win.Left = Math.Max(bounds.Left, r.Right - thickness); win.Right = win.Left + thickness; break;
            case AppBarEdge.Right:
                win.Right = Math.Min(bounds.Right, r.Left + thickness); win.Left = win.Right - thickness; break;
            case AppBarEdge.Top:
                win.Top = Math.Max(bounds.Top, r.Bottom - thickness); win.Bottom = win.Top + thickness; break;
            default:
                win.Bottom = Math.Min(bounds.Bottom, r.Top + thickness); win.Top = win.Bottom - thickness; break;
        }
        moved = SetWindowPosition(win);
        return true;
    }

    /// <summary>Riduce il rettangolo allo spessore indicato mantenendo il lato verso il bordo dello schermo.</summary>
    private void Shrink(ref Native.RECT rc, int size)
    {
        switch (AppBarEdge)
        {
            case AppBarEdge.Left: rc.Right = rc.Left + size; break;
            case AppBarEdge.Right: rc.Left = rc.Right - size; break;
            case AppBarEdge.Top: rc.Bottom = rc.Top + size; break;
            default: rc.Top = rc.Bottom - size; break;
        }
    }

    private static Native.RECT ToNative(NativeMethods.Rect r) =>
        new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };

    private bool IsAppBarPosChanged(int msg, IntPtr wParam)
    {
        if (wParam.ToInt64() != Native.ABN_POSCHANGED) return false;
        if (_appBarMessageId <= 0 && AppBarMessageIdField?.GetValue(this) is int id) _appBarMessageId = id;
        return _appBarMessageId > 0 && msg == _appBarMessageId;
    }

    protected override void SetScreenProperties(ScreenSetupReason reason)
    {
        if (reason == ScreenSetupReason.DpiChange)
        {
            base.SetScreenProperties(reason);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateShape);
            return;
        }
        // Monitor aggiunti/rimossi o risoluzione cambiata: si ricreano tutte le barre. Questo avviso arriva però anche
        // quando la disposizione degli schermi è rimasta identica (basta collegare una periferica USB o un controller):
        // in quel caso le barre restano dove sono.
        _fullScreenHelper.NotifyScreensChanged();
        _app.ScheduleRecreateTaskbars(onlyIfScreensChanged: true);
    }

    protected override void OnFullScreenEnter(FullScreenApp app)
    {
        if (!_s.HideOnFullscreen) return;
        base.OnFullScreenEnter(app);
        UpdateShape();
    }

    /// <summary>
    /// Allinea la barra alle app a schermo intero già note. La classe base reagisce solo quando la lista *cambia*:
    /// una barra creata mentre un gioco è già a schermo intero (le barre vengono ricreate quando il gioco cambia
    /// risoluzione) nascerebbe in primo piano sopra il gioco e ci resterebbe. Lì è visibile, oppure invisibile in
    /// modalità esclusiva ma comunque cliccabile: un click le darebbe il focus e il gioco tornerebbe al desktop.
    /// </summary>
    public void SyncFullScreenState()
    {
        if (Handle == IntPtr.Zero || IsClosing) return;
        try
        {
            FullScreenApp? app = _s.HideOnFullscreen
                ? _fullScreenHelper.FullScreenApps.FirstOrDefault(a => a.screen.IsVirtualScreen || a.screen.DeviceName == Screen.DeviceName)
                : null;
            if (app != null && Topmost) OnFullScreenEnter(app);
            else if (app == null && !Topmost) OnFullScreenLeave();
        }
        catch (Exception ex) { Log.Error("SyncFullScreenState", ex); }
    }

    protected override void OnFullScreenLeave()
    {
        base.OnFullScreenLeave();
        UpdateShape();
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // La base risponderebbe con ABSetPos, rimettendo la barra dopo la taskbar nascosta.
        if (CoversHiddenTaskbar && IsAppBarPosChanged(msg, wParam))
        {
            try { UpdatePosition(); } catch (Exception ex) { Log.Error("UpdatePosition", ex); }
            handled = true;
            return IntPtr.Zero;
        }

        IntPtr result = base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        if (msg == Native.WM_WINDOWPOSCHANGED)
        {
            // La barra si è mossa o ha cambiato z-order: la lastra sfocata la segue.
            _backdrop?.Restack(hwnd);
            ScheduleShapeUpdate();
        }
        return result;
    }

    private void ScheduleShapeUpdate()
    {
        if (_shapeUpdatePending) return;
        _shapeUpdatePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateShape);
    }

    protected override bool ShouldAllowAutoHide() =>
        base.ShouldAllowAutoHide() && _openPopups == 0 && !_app.IsStartMenuOpen;

    protected override void OnAutoHideAnimationBegin(bool isHiding)
    {
        if (isHiding)
        {
            _hiddenByAutoHide = true;
            UpdateShape(); // il blur non segue l'animazione: lo spengo mentre la barra è nascosta
        }
    }

    protected override void OnAutoHideAnimationComplete(bool isHiding)
    {
        if (!isHiding)
        {
            _hiddenByAutoHide = false;
            UpdateShape();
        }
    }

    private void OnPopupStateChanged(bool open)
    {
        _openPopups = Math.Max(0, _openPopups + (open ? 1 : -1));
        OnPropertyChanged(nameof(AllowAutoHide));
    }

    /// <summary>Chiamato quando il menu Start si apre o si chiude (influenza l'auto-hide).</summary>
    public void NotifyAutoHideStateChanged() => OnPropertyChanged(nameof(AllowAutoHide));

    public PlacementMode PopupPlacement => AppBarEdge switch
    {
        AppBarEdge.Bottom => PlacementMode.Top,
        AppBarEdge.Left => PlacementMode.Right,
        AppBarEdge.Right => PlacementMode.Left,
        _ => PlacementMode.Bottom
    };

    // ---- Orologio e info di sistema --------------------------------------------------------------

    private string _lastClock = "";

    /// <summary>Formatta con un formato personalizzato; se non è valido usa quello predefinito.</summary>
    private static string Format(DateTime now, string custom, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(custom))
        {
            try { return now.ToString(custom, CultureInfo.CurrentCulture); }
            catch (FormatException) { }
        }
        return now.ToString(fallback, CultureInfo.CurrentCulture);
    }

    /// <summary>Data accanto all'ora oppure sotto (due righe). Sulle barre verticali è sempre sopra.</summary>
    private void ApplyClockLayout()
    {
        if (!IsHorizontal) return;
        bool below = _s.DateBelowTime;
        ClockPanel.Orientation = below ? Orientation.Vertical : Orientation.Horizontal;
        ClockPanel.Children.Clear();
        if (below)
        {
            ClockPanel.Children.Add(ClockTime);
            ClockPanel.Children.Add(ClockDate);
            ClockDate.Margin = new Thickness(0, -1, 0, 0);
            ClockDate.TextAlignment = TextAlignment.Center;
            ClockDate.HorizontalAlignment = HorizontalAlignment.Center;
            ClockTime.HorizontalAlignment = HorizontalAlignment.Center;
            ClockDate.FontSize = Math.Max(8, Math.Clamp(_s.Theme.FontSize, 8, 24) * 0.82);
        }
        else
        {
            ClockPanel.Children.Add(ClockDate);
            ClockPanel.Children.Add(ClockTime);
            ClockDate.Margin = new Thickness(0, 0, 8, 0);
            ClockDate.ClearValue(TextBlock.FontSizeProperty);
        }
    }

    /// <summary>Colori, font e "pillola" di sfondo di orologio, data e volume.</summary>
    private void ApplyWidgetStyle(ThemeData t)
    {
        static void SetColor(TextBlock tb, string hex, string resource)
        {
            if (string.IsNullOrWhiteSpace(hex)) tb.SetResourceReference(TextBlock.ForegroundProperty, resource);
            else tb.Foreground = new SolidColorBrush(ThemeService.ParseColor(hex, Colors.White));
        }

        SetColor(ClockTime, t.ClockColor, "FgBrush");
        SetColor(ClockDate, t.DateColor, "FgDimBrush");
        SetColor(VolumeGlyph, t.VolumeColor, "FgBrush");
        SetColor(VolumeText, t.VolumeColor, "FgBrush");

        if (string.IsNullOrWhiteSpace(t.ClockFont)) ClockTime.ClearValue(TextBlock.FontFamilyProperty);
        else
        {
            try { ClockTime.FontFamily = new FontFamily(t.ClockFont); } catch { ClockTime.ClearValue(TextBlock.FontFamilyProperty); }
        }
        if (t.ClockFontSize > 0) ClockTime.FontSize = Math.Clamp(t.ClockFontSize, 8, 28);
        else ClockTime.ClearValue(TextBlock.FontSizeProperty);
        ClockTime.FontWeight = t.ClockBold ? FontWeights.SemiBold : FontWeights.Normal;

        Brush pill = string.IsNullOrWhiteSpace(t.WidgetBackground)
            ? Brushes.Transparent
            : new SolidColorBrush(ThemeService.ParseColor(t.WidgetBackground, Colors.Transparent));
        var corner = new CornerRadius(Math.Clamp(t.WidgetRadius, 0, 20));
        foreach (Button b in new[] { ClockButton, VolumeButton })
        {
            b.Background = pill;
            if (!string.IsNullOrWhiteSpace(t.WidgetBackground)) b.Margin = IsHorizontal ? new Thickness(3, 4, 3, 4) : new Thickness(4, 3, 4, 3);
            else if (IsHorizontal) b.ClearValue(MarginProperty);
            else b.Margin = new Thickness(3, 1, 3, 1);
            b.ApplyTemplate();
            if (b.Template?.FindName("Bd", b) is Border bd) bd.CornerRadius = corner;
        }
        ApplyClockLayout();
        _lastClock = "";
        UpdateClock();
    }

    private void UpdateClock()
    {
        if (!_s.ShowClock) return;
        DateTime now = DateTime.Now;
        string time = Format(now, _s.ClockFormat, _s.Clock24h
            ? (_s.ShowSeconds ? "HH:mm:ss" : "HH:mm")
            : (_s.ShowSeconds ? "h:mm:ss tt" : "h:mm tt"));
        string date = _s.ShowDate ? Format(now, _s.DateFormat, IsHorizontal ? "ddd d MMM" : "d/M") : "";
        if (!IsHorizontal) time = time.Replace(":", "\n").Replace(" ", "\n");

        string key = time + "|" + date;
        if (key == _lastClock) return;
        _lastClock = key;
        ClockTime.Text = time;
        ClockDate.Text = date;
        ClockDate.Visibility = Vis(date.Length > 0);
        ClockButton.ToolTip = now.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);
    }

    private void UpdateSysInfo()
    {
        if (!_s.ShowSysInfo) return;
        var si = _app.SysInfo;
        string sep = IsHorizontal ? "   " : "\n";
        string text = $"CPU {si.Cpu}%{sep}RAM {si.Ram}%";
        if (si.Battery >= 0) text += $"{sep}{(si.Charging ? "⚡" : "")}{si.Battery}%";
        SysInfoText.Text = text;
    }

    // ---- Volume ----------------------------------------------------------------------------------

    private bool _syncingVolume;

    private void UpdateVolume()
    {
        if (!_s.ShowVolume) return;
        AudioService a = _app.Audio;
        int v = Math.Max(0, a.Volume);
        string glyph = !a.Available ? "" : a.Muted || v == 0 ? "" : v < 34 ? "" : v < 67 ? "" : "";
        VolumeGlyph.Text = glyph;
        MuteGlyph.Text = glyph;
        bool showText = _s.ShowVolumePercent && a.Available;
        VolumeText.Text = a.Muted ? "Mute" : v + (IsHorizontal ? "%" : "");
        VolumeText.Visibility = Vis(showText);
        VolumeButton.ToolTip = a.Available ? (a.Muted ? $"Volume: muted ({v}%)" : $"Volume: {v}%") + "\nScroll to change · Right-click to mute" : "No audio device";
        VolumeValue.Text = v.ToString(CultureInfo.CurrentCulture);
        _syncingVolume = true;
        VolumeSlider.Value = v;
        _syncingVolume = false;
    }

    private void Volume_Click(object sender, RoutedEventArgs e)
    {
        VolumeFlyout.Placement = PopupPlacement;
        UpdateVolume();
        VolumeFlyout.IsOpen = !VolumeFlyout.IsOpen;
    }

    private void Volume_Wheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        _app.Audio.SetVolume(Math.Max(0, _app.Audio.Volume) + (e.Delta > 0 ? 2 : -2));
    }

    private void Volume_RightUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _app.Audio.ToggleMute();
    }

    private void Mute_Click(object sender, RoutedEventArgs e) => _app.Audio.ToggleMute();

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        VolumeValue.Text = Math.Round(e.NewValue).ToString(CultureInfo.CurrentCulture);
        if (!_syncingVolume) _app.Audio.SetVolume((int)Math.Round(e.NewValue));
    }

    private void SoundSettings_Click(object sender, RoutedEventArgs e)
    {
        VolumeFlyout.IsOpen = false;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Impostazioni audio", ex); }
    }

    private void VolumeFlyout_Opened(object? sender, EventArgs e) => OnPopupStateChanged(true);
    private void VolumeFlyout_Closed(object? sender, EventArgs e) => OnPopupStateChanged(false);

    // ---- GIF -------------------------------------------------------------------------------------

    private string _gifSignature = "";

    /// <summary>Mostra le GIF abilitate (la posizione sulla barra la decide il layout). Non fa nulla se nulla è cambiato.</summary>
    private void BuildGifs()
    {
        var items = _s.Gifs.Where(g => g.Enabled && System.IO.File.Exists(GifService.FullPath(g))).ToList();
        double thickness = Math.Clamp(_s.Theme.BarHeight, 24, 96);
        double size = _s.GifSize > 0 ? _s.GifSize : thickness - 10;
        size = Math.Clamp(size, 12, thickness - 2);
        string signature = $"{size}|{string.Join("|", items.Select(g => g.File))}";
        if (signature == _gifSignature) return;
        _gifSignature = signature;

        GifHost.Children.Clear();

        foreach (GifItem item in items)
        {
            var gif = new GifImage
            {
                Margin = IsHorizontal ? new Thickness(3, 0, 3, 0) : new Thickness(0, 3, 0, 3),
                ToolTip = System.IO.Path.GetFileNameWithoutExtension(item.File),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            };
            if (!IsHorizontal) gif.MaxWidth = thickness - 8;
            gif.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                var menu = new ContextMenu();
                menu.Items.Add(MenuEntry("Hide this GIF", "", () =>
                {
                    item.Enabled = false;
                    _app.SettingsService.Notify(nameof(AppSettings.Gifs));
                }));
                menu.Items.Add(MenuEntry("GIF settings…", "", () => _app.ShowSettings("GIFs")));
                OpenMenu(menu, gif);
            };
            gif.Load(GifService.FullPath(item), size);
            GifHost.Children.Add(gif);
        }
    }

    private static IEnumerable<string> DroppedGifs(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => string.Equals(System.IO.Path.GetExtension(f), ".gif", StringComparison.OrdinalIgnoreCase))
            : Enumerable.Empty<string>();

    private void Bar_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedGifs(e).Any() ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>File .gif trascinati sulla barra: copiati nella cartella dell'app e mostrati subito.</summary>
    private void Bar_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        bool added = false;
        foreach (string file in DroppedGifs(e).ToList())
        {
            if (GifService.Import(file) is { } item)
            {
                _s.Gifs.Add(item);
                added = true;
            }
        }
        if (added) _app.SettingsService.Notify(nameof(AppSettings.Gifs));
    }

    // ---- Pulsanti a destra -----------------------------------------------------------------------

    private void Clock_Click(object sender, RoutedEventArgs e)
    {
        try { ShellHelper.ShowNotificationCenter(); } catch (Exception ex) { Log.Error("ShowNotificationCenter", ex); }
    }

    private void Quick_Click(object sender, RoutedEventArgs e)
    {
        try { ShellHelper.ShowActionCenter(); } catch (Exception ex) { Log.Error("ShowActionCenter", ex); }
    }

    private void Start_Click(object sender, RoutedEventArgs e) => _app.ToggleStartMenu(this, StartButton);

    /// <summary>
    /// Elemento accanto al quale aprire il menu Start: il pulsante oppure, se è nascosto, l'elemento visibile che gli
    /// sta più vicino nella stessa zona (in mancanza, l'intera barra).
    /// </summary>
    public FrameworkElement StartAnchor
    {
        get
        {
            if (StartButton.IsVisible) return StartButton;
            BarZone zone = BarPanel.GetZone(StartButton);
            int order = BarPanel.GetOrder(StartButton);
            return WidgetPanel.Children.OfType<FrameworkElement>()
                .Where(el => el.IsVisible && el.ActualWidth > 0 && el.ActualHeight > 0 && BarPanel.GetZone(el) == zone)
                .OrderBy(el => Math.Abs(BarPanel.GetOrder(el) - order))
                .FirstOrDefault() ?? WidgetPanel;
        }
    }

    /// <summary>In auto-hide: mostra la barra per un momento (es. quando il menu Start si apre da tastiera).</summary>
    public void Peek() => PeekDuringAutoHide(1500);

    private void Start_RightUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowBarMenu(StartButton);
    }

    private void Bar_RightUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        ShowBarMenu(null);
    }

    private void ShowBarMenu(FrameworkElement? anchor)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuEntry("OpSec Bar settings", "", _app.ShowSettings));
        menu.Items.Add(MenuEntry("Task Manager", "", ShellHelper.StartTaskManager));
        menu.Items.Add(MenuEntry("Run…", "", () => ShellHelper.ShowRunDialog("", "")));
        menu.Items.Add(MenuEntry("Windows Start menu", "", ShellHelper.ShowStartMenu));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry("Exit OpSec Bar", "", () => _app.ExitApp()));
        OpenMenu(menu, anchor);
    }

    // ---- Icone delle app -------------------------------------------------------------------------

    private static AppItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as AppItem;

    private static void AnimatePress(object sender, bool pressed)
    {
        // L'animazione è solo estetica: non deve mai impedire l'azione del click.
        try
        {
            if (sender is not Panel panel || panel.Children.OfType<Image>().FirstOrDefault() is not { } image) return;
            // La trasformazione dichiarata nel modello XAML è condivisa e "congelata": ne serve una propria per animarla.
            if (image.RenderTransform is not ScaleTransform st || st.IsFrozen)
            {
                st = new ScaleTransform();
                image.RenderTransform = st;
            }
            var anim = new DoubleAnimation(pressed ? 0.84 : 1, TimeSpan.FromMilliseconds(pressed ? 70 : 180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
        catch (Exception ex) { Log.Warn("Animazione icona: " + ex.Message); }
    }

    private void Task_Down(object sender, MouseButtonEventArgs e)
    {
        // Lo stato va letto ORA: al rilascio la nostra barra sarà la finestra attiva.
        _pressedItem = ItemOf(sender);
        _pressedWasActive = _pressedItem?.IsActive == true;
        if (TaskListService.DebugEnabled) Log.Info($"[down] item={_pressedItem?.Key} attiva={_pressedWasActive}");
        AnimatePress(sender, true);
    }

    private void Task_Leave(object sender, MouseEventArgs e)
    {
        if (_pressedItem != null && ReferenceEquals(_pressedItem, ItemOf(sender)))
        {
            if (TaskListService.DebugEnabled) Log.Info($"[leave] item={_pressedItem.Key} (pressione annullata)");
            _pressedItem = null;
            AnimatePress(sender, false);
        }
    }

    private void Task_Up(object sender, MouseButtonEventArgs e)
    {
        AnimatePress(sender, false);
        AppItem? item = ItemOf(sender);
        bool sameItem = item != null && ReferenceEquals(item, _pressedItem);
        if (TaskListService.DebugEnabled)
            Log.Info($"[click] item={item?.Key} same={sameItem} finestre={item?.Windows.Count} eraAttiva={_pressedWasActive} " +
                     $"stato={(item?.Windows.Count > 0 ? item.Windows[0].State.ToString() : "-")}");
        _pressedItem = null;
        if (!sameItem || item == null) return;

        try
        {
            if (item.Windows.Count == 0)
            {
                _app.TaskList.Launch(item);
            }
            else if (item.Windows.Count == 1)
            {
                ApplicationWindow w = item.Windows[0];
                if (_pressedWasActive && w.CanMinimize) w.Minimize();
                else w.BringToFront();
            }
            else
            {
                ShowWindowList(item, (FrameworkElement)sender);
            }
        }
        catch (Exception ex) { Log.Error("Attivazione app", ex); }
    }

    private void Task_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Click centrale: nuova istanza
        if (e.ChangedButton == MouseButton.Middle && ItemOf(sender) is { } item)
        {
            e.Handled = true;
            _app.TaskList.Launch(item);
        }
    }

    private void ShowWindowList(AppItem item, FrameworkElement anchor)
    {
        var menu = new ContextMenu();
        foreach (ApplicationWindow w in item.Windows.ToList())
        {
            var entry = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(w.Title) ? "(untitled)" : w.Title,
                Icon = new Image { Source = w.Icon, Width = 16, Height = 16 }
            };
            entry.Click += (_, _) => w.BringToFront();
            menu.Items.Add(entry);
        }
        OpenMenu(menu, anchor);
    }

    private void Task_RightUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        e.Handled = true;
        var tasks = _app.TaskList;
        var menu = new ContextMenu();

        if (item.Windows.Count > 1)
        {
            foreach (ApplicationWindow w in item.Windows.ToList())
            {
                var entry = new MenuItem
                {
                    Header = string.IsNullOrWhiteSpace(w.Title) ? "(untitled)" : w.Title,
                    Icon = new Image { Source = w.Icon, Width = 16, Height = 16 }
                };
                entry.Click += (_, _) => w.BringToFront();
                menu.Items.Add(entry);
            }
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(MenuEntry(item.IsRunning ? "New window" : "Open", "", () => tasks.Launch(item)));

        bool hasExe = !string.IsNullOrEmpty(item.Pin?.TargetPath) ||
                      (item.Pin == null && item.Windows.Count > 0 && !item.Windows[0].IsUWP && !string.IsNullOrEmpty(item.Windows[0].WinFileName));
        if (hasExe) menu.Items.Add(MenuEntry("Run as administrator", "", () => tasks.Launch(item, true)));

        menu.Items.Add(item.Pin is { } pin
            ? MenuEntry("Unpin from taskbar", "", () => tasks.Unpin(pin))
            : MenuEntry("Pin to taskbar", "", () => tasks.Pin(item)));

        if (item.IsRunning)
        {
            menu.Items.Add(new Separator());
            if (item.Windows.Count == 1)
            {
                ApplicationWindow w = item.Windows[0];
                if (w.CanMinimize) menu.Items.Add(MenuEntry("Minimize", "", w.Minimize));
                menu.Items.Add(MenuEntry("Maximize", "", w.Maximize));
                menu.Items.Add(MenuEntry("Close window", "", w.Close));
            }
            else
            {
                var all = item.Windows.ToList();
                menu.Items.Add(MenuEntry($"Close all windows ({all.Count})", "", () => { foreach (var w in all) w.Close(); }));
            }
            // Chiusura forzata del processo, per le app che non rispondono.
            if (tasks.CanEndTask(item)) menu.Items.Add(MenuEntry("End task", "", () => tasks.EndTask(item)));
        }

        OpenMenu(menu, (FrameworkElement)sender);
    }

    private static MenuItem MenuEntry(string text, string glyph, Action action) => Ui.MenuEntry(text, glyph, action);

    private void OpenMenu(ContextMenu menu, FrameworkElement? anchor)
    {
        if (anchor != null)
        {
            menu.PlacementTarget = anchor;
            menu.Placement = PopupPlacement;
        }
        else
        {
            menu.Placement = PlacementMode.MousePoint;
        }
        menu.Opened += (_, _) => OnPopupStateChanged(true);
        menu.Closed += (_, _) => OnPopupStateChanged(false);
        menu.IsOpen = true;
    }

    private void TasksScroll_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (IsHorizontal)
        {
            TasksScroll.ScrollToHorizontalOffset(TasksScroll.HorizontalOffset - e.Delta / 2.0);
            e.Handled = true;
        }
    }
}
