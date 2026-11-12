using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using OpSecBar.Core;
using OpSecBar.Models;
using OpSecBar.Services;

namespace OpSecBar.Windows;

/// <summary>Menu Start personalizzato: ricerca, preferiti, tutte le app installate, alimentazione.</summary>
public partial class StartMenuWindow : Window
{
    private const double ShadowMargin = 14; // margine trasparente attorno al pannello (in DIP)

    private readonly App _app;
    private List<AppEntry> _allApps = new();
    private TaskbarWindow? _bar;
    private FrameworkElement? _anchor;
    private IntPtr _hwnd;
    private bool _forceClose, _loaded;
    private BackdropWindow? _backdrop;
    private DateTime _hiddenAt = DateTime.MinValue;

    public StartMenuWindow(App app)
    {
        _app = app;
        InitializeComponent();
        UserName.Text = Environment.UserName;
        SourceInitialized += (_, _) => _hwnd = new WindowInteropHelper(this).Handle;
    }

    // ---- Apertura / chiusura ---------------------------------------------------------------------

    public void Toggle(TaskbarWindow bar, FrameworkElement anchor)
    {
        if (IsVisible)
        {
            HideMenu();
            return;
        }
        // Il click sul pulsante Start ha appena chiuso il menu (per disattivazione): non riaprirlo.
        if ((DateTime.Now - _hiddenAt).TotalMilliseconds < 250) return;

        _bar = bar;
        _anchor = anchor;
        ApplyTheme();
        SearchBox.Text = "";
        RefreshFavorites();
        ApplyFilter();

        Opacity = 0;
        Show();
        PositionNearAnchor();
        Activate();
        Native.ForceForeground(_hwnd); // necessario quando il menu è aperto dal tasto Windows
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        bar.NotifyAutoHideStateChanged();

        // Dopo il primo layout (e l'eventuale cambio DPI del monitor) si riposiziona e si mostra con una breve animazione.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            PositionNearAnchor();
            ApplyBackdrop();
            AnimateIn();
        });

        if (!_loaded) LoadApps();
    }

    public void HideMenu()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.Now;
        _backdrop?.Hide();
        Hide();
        _bar?.NotifyAutoHideStateChanged();
    }

    public void ForceClose()
    {
        _forceClose = true;
        _backdrop?.Dispose();
        _backdrop = null;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            HideMenu();
        }
        base.OnClosing(e);
    }

    private void Window_Deactivated(object? sender, EventArgs e) => HideMenu();

    private void AnimateIn()
    {
        AppBarEdge edge = _bar?.AppBarEdge ?? AppBarEdge.Top;
        double from = edge == AppBarEdge.Bottom ? 10 : -10;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        Slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
    }

    /// <summary>Posiziona il menu accanto al pulsante Start, in pixel fisici (corretto con qualsiasi DPI e monitor).</summary>
    private void PositionNearAnchor()
    {
        if (_bar == null || _anchor == null || _hwnd == IntPtr.Zero) return;
        try
        {
            if (!Native.GetWindowRect(_bar.Handle, out Native.RECT barRect)) return;
            if (!Native.GetWindowRect(_hwnd, out Native.RECT self)) return;
            var screen = _bar.Screen.Bounds;
            double scale = _bar.DpiScale > 0 ? _bar.DpiScale : 1;
            int pad = (int)Math.Round(ShadowMargin * scale);
            int gap = (int)Math.Round(6 * scale);
            int w = self.Width, h = self.Height;

            Point a = _anchor.PointToScreen(new Point(0, 0));
            int anchorLeft = (int)a.X, anchorTop = (int)a.Y;
            int anchorRight = (int)(a.X + _anchor.ActualWidth * scale);
            int anchorBottom = (int)(a.Y + _anchor.ActualHeight * scale);

            // Lungo la barra il menu si allinea al lato in cui si trova il pulsante (che il layout avanzato può mettere
            // ovunque): all'inizio parte dal pulsante, al centro gli sta sopra centrato, alla fine termina con lui.
            static int Along(int from, int to, int barFrom, int barTo, int size, int pad)
            {
                int middle = (from + to) / 2, third = (barTo - barFrom) / 3;
                if (middle < barFrom + third) return from - pad;
                if (middle > barTo - third) return to - size + pad;
                return middle - size / 2;
            }

            int x, y;
            switch (_bar.AppBarEdge)
            {
                case AppBarEdge.Bottom:
                    x = Along(anchorLeft, anchorRight, barRect.Left, barRect.Right, w, pad);
                    y = barRect.Top - h + pad - gap;
                    break;
                case AppBarEdge.Left:
                    x = barRect.Right - pad + gap;
                    y = Along(anchorTop, anchorBottom, barRect.Top, barRect.Bottom, h, pad);
                    break;
                case AppBarEdge.Right:
                    x = barRect.Left - w + pad - gap;
                    y = Along(anchorTop, anchorBottom, barRect.Top, barRect.Bottom, h, pad);
                    break;
                default:
                    x = Along(anchorLeft, anchorRight, barRect.Left, barRect.Right, w, pad);
                    y = barRect.Bottom - pad + gap;
                    break;
            }

            // Il pannello visibile resta sempre dentro lo schermo.
            x = Math.Clamp(x, screen.Left - pad + gap, Math.Max(screen.Left - pad + gap, screen.Right - w + pad - gap));
            y = Math.Clamp(y, screen.Top - pad + gap, Math.Max(screen.Top - pad + gap, screen.Bottom - h + pad - gap));

            Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
        catch (Exception ex) { Log.Error("Posizionamento menu Start", ex); }
    }

    // ---- Aspetto ---------------------------------------------------------------------------------

    public void ApplyTheme()
    {
        AppSettings s = _app.Settings;
        Width = Math.Clamp(s.StartMenuWidth, 320, 900) + ShadowMargin * 2;
        Height = Math.Clamp(s.StartMenuHeight, 380, 1200) + ShadowMargin * 2;
        if (IsVisible)
        {
            RefreshFavorites();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                PositionNearAnchor();
                ApplyBackdrop();
            });
        }
    }

    /// <summary>Con i temi blur/acrylic/glass anche il menu Start sfoca ciò che ha dietro.</summary>
    private void ApplyBackdrop()
    {
        if (_hwnd == IntPtr.Zero) return;
        ThemeData t = _app.Settings.Theme;
        bool blur = IsVisible && BackdropHelper.BlurActive(t.Backdrop) && t.PanelOpacity < 0.99;
        try
        {
            if (blur && Native.GetWindowRect(_hwnd, out Native.RECT r))
            {
                double scale = _bar?.DpiScale > 0 ? _bar!.DpiScale : 1;
                int pad = (int)Math.Round(ShadowMargin * scale);
                _backdrop ??= new BackdropWindow();
                _backdrop.Show(_hwnd, r.Left + pad, r.Top + pad, r.Width - pad * 2, r.Height - pad * 2, (int)Math.Round(14 * scale),
                    BackdropMode.Blur, ThemeService.ParseColor(t.PanelColor, Colors.Black), 0);
            }
            else _backdrop?.Hide();
        }
        catch (Exception ex) { Log.Error("Backdrop menu Start", ex); }
    }

    // ---- Dati ------------------------------------------------------------------------------------

    private async void LoadApps()
    {
        try
        {
            _allApps = await _app.Catalog.GetAppsAsync();
            _loaded = true;
            ApplyFilter();
        }
        catch (Exception ex) { Log.Error("Caricamento app", ex); }
    }

    private void RefreshFavorites()
    {
        AppSettings s = _app.Settings;
        var favorites = s.StartFavorites.Select(AppEntry.From).ToList();
        FavoritesList.ItemsSource = favorites;
        FavoritesHint.Visibility = favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSections();
    }

    private void UpdateSections()
    {
        bool searching = !string.IsNullOrWhiteSpace(SearchBox.Text);
        FavoritesSection.Visibility = _app.Settings.StartShowFavorites && !searching ? Visibility.Visible : Visibility.Collapsed;
        ListHeader.Text = searching ? "Results" : "All apps";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyFilter()
    {
        string query = SearchBox.Text.Trim();
        IEnumerable<AppEntry> result = _allApps;
        if (query.Length > 0)
        {
            result = _allApps
                .Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(a => a.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        }
        var list = result.ToList();
        AppList.ItemsSource = list;
        if (query.Length > 0 && list.Count > 0) AppList.SelectedIndex = 0;

        EmptyText.Text = !_loaded ? "Loading apps…" : "No results";
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSections();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    // ---- Interazione -----------------------------------------------------------------------------

    private void Launch(AppEntry app, bool asAdmin = false)
    {
        HideMenu();
        AppCatalogService.Launch(app.ToPinned(), asAdmin);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (SearchBox.Text.Length > 0) SearchBox.Text = "";
                else HideMenu();
                e.Handled = true;
                break;
            case Key.Enter:
                if ((AppList.SelectedItem ?? (SearchBox.Text.Length > 0 && AppList.Items.Count > 0 ? AppList.Items[0] : null)) is AppEntry app)
                    Launch(app);
                e.Handled = true;
                break;
            case Key.Down:
            case Key.Up:
                if (AppList.Items.Count == 0) break;
                int index = AppList.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                AppList.SelectedIndex = Math.Clamp(index, 0, AppList.Items.Count - 1);
                AppList.ScrollIntoView(AppList.SelectedItem);
                e.Handled = true;
                break;
        }
    }

    private void AppRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AppEntry app) Launch(app);
    }

    private void AppRow_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AppEntry app)
        {
            e.Handled = true;
            ShowAppMenu(app, fe);
        }
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AppEntry app) Launch(app);
    }

    private void Favorite_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AppEntry app)
        {
            e.Handled = true;
            ShowAppMenu(app, fe);
        }
    }

    private void ShowAppMenu(AppEntry app, FrameworkElement target)
    {
        AppSettings s = _app.Settings;
        PinnedApp pinned = app.ToPinned();
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };

        menu.Items.Add(Ui.MenuEntry("Open", "", () => Launch(app)));
        if (!string.IsNullOrEmpty(app.TargetPath) && app.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            menu.Items.Add(Ui.MenuEntry("Run as administrator", "", () => Launch(app, true)));
        menu.Items.Add(new Separator());

        bool isFavorite = s.StartFavorites.Any(f => f.Key == pinned.Key);
        menu.Items.Add(isFavorite
            ? Ui.MenuEntry("Remove from Start favorites", "", () =>
            {
                s.StartFavorites.RemoveAll(f => f.Key == pinned.Key);
                _app.SettingsService.Notify(nameof(AppSettings.StartFavorites));
            })
            : Ui.MenuEntry("Add to Start favorites", "", () =>
            {
                s.StartFavorites.Add(pinned);
                _app.SettingsService.Notify(nameof(AppSettings.StartFavorites));
            }));

        menu.Items.Add(_app.TaskList.IsPinned(pinned)
            ? Ui.MenuEntry("Unpin from taskbar", "", () => _app.TaskList.Unpin(pinned))
            : Ui.MenuEntry("Pin to taskbar", "", () => _app.TaskList.Pin(pinned)));

        if (!string.IsNullOrEmpty(app.TargetPath) && System.IO.File.Exists(app.TargetPath))
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Ui.MenuEntry("Open file location", "", () =>
            {
                HideMenu();
                AppCatalogService.OpenFileLocation(app.TargetPath);
            }));
        }
        menu.IsOpen = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        HideMenu();
        _app.ShowSettings();
    }

    private void Power_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = PowerButton, Placement = PlacementMode.Top };
        menu.Items.Add(Ui.MenuEntry("Lock", "", () => { HideMenu(); ShellHelper.Lock(); }));
        menu.Items.Add(Ui.MenuEntry("Sign out", "", () => { HideMenu(); ShellHelper.Logoff(); }));
        menu.Items.Add(new Separator());
        if (PowerHelper.CanSleep())
            menu.Items.Add(Ui.MenuEntry("Sleep", "", () => { HideMenu(); PowerHelper.Sleep(); }));
        menu.Items.Add(Ui.MenuEntry("Restart", "", () => { HideMenu(); RunShutdown("/r /t 0"); }));
        menu.Items.Add(Ui.MenuEntry("Shut down", "", () => { HideMenu(); RunShutdown("/s /t 0"); }));
        menu.IsOpen = true;
    }

    private static void RunShutdown(string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", args) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex) { Log.Error("shutdown " + args, ex); }
    }
}
