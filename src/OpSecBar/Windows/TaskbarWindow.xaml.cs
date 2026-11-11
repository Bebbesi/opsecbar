using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTasks;
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

        ApplyLayout();
        ApplyElements();
        ApplyTheme();

        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();

        BarBorder.SizeChanged += (_, _) => UpdateShape();
        Loaded += (_, _) => UpdateShape();
        IsVisibleChanged += (_, _) => ScheduleShapeUpdate();
        Closed += (_, _) =>
        {
            _clock.Stop();
            _app.SysInfo.Updated -= UpdateSysInfo;
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
        LayoutGrid.ColumnDefinitions.Clear();
        LayoutGrid.RowDefinitions.Clear();

        if (IsHorizontal)
        {
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(TasksScroll, 0);
            Grid.SetColumn(CenterPanel, 1);
            Grid.SetColumn(RightPanel, 2);
            LayoutGrid.Margin = new Thickness(6, 0, 6, 0);
            TasksScroll.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(TasksScroll, 0);
            Grid.SetRow(CenterPanel, 1);
            Grid.SetRow(RightPanel, 2);
            LayoutGrid.Margin = new Thickness(0, 6, 0, 6);

            TasksScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            TasksScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            TasksScroll.VerticalAlignment = VerticalAlignment.Top;
            TasksScroll.HorizontalAlignment = HorizontalAlignment.Center;

            CenterPanel.Orientation = Orientation.Vertical;
            RightPanel.Orientation = Orientation.Vertical;
            RightPanel.HorizontalAlignment = HorizontalAlignment.Center;
            RightPanel.VerticalAlignment = VerticalAlignment.Bottom;
            ClockPanel.Orientation = Orientation.Vertical;
            ClockDate.Margin = new Thickness(0);
            ClockDate.FontSize = 10;
            ClockDate.TextAlignment = TextAlignment.Center;
            SysInfoText.Margin = new Thickness(0, 6, 0, 6);
            SysInfoText.FontSize = 10;

            foreach (Button b in new[] { ClockButton, QuickButton, StartButton })
            {
                b.Padding = new Thickness(0, 7, 0, 7);
                b.Margin = new Thickness(3, 1, 3, 1);
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }
    }

    /// <summary>Mostra/nasconde gli elementi opzionali in base alle impostazioni.</summary>
    public void ApplyElements()
    {
        Media.Configure(_s.ShowMediaPlayer, _s.ShowMediaTitle);
        ClockButton.Visibility = Vis(_s.ShowClock);
        Tray.Visibility = Vis(_s.ShowTray);
        QuickButton.Visibility = Vis(_s.ShowQuickSettings);
        StartButton.Visibility = Vis(_s.ShowStartButton);
        SysInfoText.Visibility = Vis(_s.ShowSysInfo);
        UpdateClock();
        UpdateSysInfo();
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
        BarBorder.Background = t.Backdrop switch
        {
            BackdropMode.Transparent => new SolidColorBrush(hitTestOnly),
            // Con l'acrylic il colore lo mette Windows; se la trasparenza di sistema è spenta lo disegna la barra.
            BackdropMode.Acrylic when blurActive => new SolidColorBrush(hitTestOnly),
            BackdropMode.Glass => new LinearGradientBrush(
                ThemeService.WithAlpha(bg, Math.Min(1, opacity * 1.35)),
                ThemeService.WithAlpha(bg, opacity * 0.6),
                IsHorizontal ? 90 : 0),
            _ => new SolidColorBrush(ThemeService.WithAlpha(bg, Math.Max(opacity, 1.0 / 255)))
        };

        double border = Math.Clamp(t.BorderThickness, 0, 6);
        BarBorder.BorderThickness = new Thickness(border);
        BarBorder.BorderBrush = border > 0 ? new SolidColorBrush(ThemeService.ParseColor(t.BorderColor, Color.FromArgb(0x33, 255, 255, 255))) : null;

        // Ombra: richiede spazio trasparente attorno alla barra, quindi un margine > 0 (barra flottante).
        bool shadow = t.Shadow && m > 0;
        ShadowLayer.Visibility = Vis(shadow);
        ShadowShape.Margin = margin;
        ShadowShape.CornerRadius = new CornerRadius(radius);

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
        UpdateShape();
    }

    protected override void SetScreenProperties(ScreenSetupReason reason)
    {
        if (reason == ScreenSetupReason.DpiChange)
        {
            base.SetScreenProperties(reason);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateShape);
            return;
        }
        // Monitor aggiunti/rimossi o risoluzione cambiata: si ricreano tutte le barre.
        _fullScreenHelper.NotifyScreensChanged();
        _app.ScheduleRecreateTaskbars();
    }

    protected override void OnFullScreenEnter(FullScreenApp app)
    {
        if (!_s.HideOnFullscreen) return;
        base.OnFullScreenEnter(app);
        UpdateShape();
    }

    protected override void OnFullScreenLeave()
    {
        base.OnFullScreenLeave();
        UpdateShape();
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
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

    private void UpdateClock()
    {
        if (!_s.ShowClock) return;
        DateTime now = DateTime.Now;
        string time = _s.Clock24h
            ? now.ToString(_s.ShowSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.CurrentCulture)
            : now.ToString(_s.ShowSeconds ? "h:mm:ss tt" : "h:mm tt", CultureInfo.CurrentCulture);
        string date = _s.ShowDate ? now.ToString(IsHorizontal ? "ddd d MMM" : "d/M", CultureInfo.CurrentCulture) : "";
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

    /// <summary>Elemento accanto al quale aprire il menu Start (il pulsante, o la barra se il pulsante è nascosto).</summary>
    public FrameworkElement StartAnchor => StartButton.IsVisible ? StartButton : RightPanel;

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
