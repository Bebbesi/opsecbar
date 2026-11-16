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
public partial class StartMenuWindow : FlyoutWindow
{
    private App _app => App;
    private List<AppEntry> _allApps = new();
    private bool _loaded;

    public StartMenuWindow(App app) : base(app)
    {
        InitializeComponent();
    }

    // ---- Apertura --------------------------------------------------------------------------------

    protected override void OnOpening()
    {
        SearchBox.Text = "";
        RefreshFavorites();
        RefreshRecent();
        ApplyFilter();
    }

    protected override void OnOpened()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        LoadApps(); // subito pronto se l'elenco è già in memoria; riletto se la cartella del menu Start è cambiata
    }

    public override void ApplyTheme()
    {
        AppSettings s = _app.Settings;
        Width = Math.Clamp(s.StartMenuWidth, 320, 900) + ShadowMargin * 2;
        Height = Math.Clamp(s.StartMenuHeight, 380, 1200) + ShadowMargin * 2;
        UserName.Text = s.StartHideUserName ? new string('*', Environment.UserName.Length) : Environment.UserName;
        if (IsVisible)
        {
            RefreshFavorites();
            RefreshRecent();
        }
        base.ApplyTheme();
    }

    // ---- Dati ------------------------------------------------------------------------------------

    /// <summary>Carica l'elenco delle app dalla sorgente scelta nelle impostazioni (cartella del menu Start o tutte le app).</summary>
    public async void LoadApps()
    {
        try
        {
            StartAppSource source = _app.Settings.StartAppSource;
            List<AppEntry> apps = await _app.Catalog.GetStartAppsAsync(source);
            if (source != _app.Settings.StartAppSource) return;      // sorgente cambiata nel frattempo
            if (_loaded && ReferenceEquals(apps, _allApps)) return;  // stesso elenco già mostrato
            _allApps = apps;
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
        RecentSection.Visibility = _hasRecent && !searching ? Visibility.Visible : Visibility.Collapsed;
        ListHeader.Text = searching ? "Results" : "All apps";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyFilter()
    {
        string query = SearchBox.Text.Trim();
        IEnumerable<AppEntry> result = _allApps;
        if (query.Length > 0)
        {
            // Prima le app il cui nome (o una sua parola) inizia con la ricerca, poi quelle che la contengono a metà
            // parola; in ciascun gruppo le più aperte negli ultimi 30 giorni, poi le più recenti, poi in ordine alfabetico.
            result = _allApps
                .Select(a => (App: a, Rank: MatchRank(a.Name, query)))
                .Where(x => x.Rank >= 0)
                .Select(x => (x.App, x.Rank, Uses: UsageService.LaunchCount(x.App.Key, x.App.TargetPath),
                              Last: UsageService.LastUsed(x.App.Key) ?? DateTime.MinValue))
                .OrderBy(x => x.Rank)
                .ThenByDescending(x => x.Uses)
                .ThenByDescending(x => x.Last)
                .ThenBy(x => x.App.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => x.App);
        }
        var list = result.ToList();
        AppList.ItemsSource = list;
        if (query.Length > 0 && list.Count > 0) AppList.SelectedIndex = 0;

        EmptyText.Text = !_loaded ? "Loading apps…" : "No results";
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSections();
    }

    /// <summary>0 = il nome o una sua parola inizia con la ricerca; 1 = la contiene a metà parola; -1 = nessuna corrispondenza.</summary>
    private static int MatchRank(string name, string query)
    {
        var cmp = StringComparison.CurrentCultureIgnoreCase;
        int index = name.IndexOf(query, cmp);
        if (index < 0) return -1;
        while (index >= 0)
        {
            if (index == 0 || !char.IsLetterOrDigit(name[index - 1])) return 0;
            index = name.IndexOf(query, index + 1, cmp);
        }
        return 1;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    // ---- Recenti ---------------------------------------------------------------------------------

    private const int RecentCount = 4;

    private void RefreshRecent()
    {
        var recent = _app.Settings.StartShowRecent
            ? UsageService.Recent(RecentCount).Select(r => new RecentEntry(AppEntry.From(r.ToPinned()), r.LastUsed)).ToList()
            : new List<RecentEntry>();
        RecentList.ItemsSource = recent;
        _hasRecent = recent.Count > 0;
        UpdateSections();
    }

    private bool _hasRecent;

    /// <summary>Voce della sezione "Recent": l'app e quanto tempo fa è stata aperta.</summary>
    public sealed class RecentEntry
    {
        public RecentEntry(AppEntry app, DateTime lastUsedUtc)
        {
            App = app;
            When = Ago(DateTime.UtcNow - lastUsedUtc);
        }

        public AppEntry App { get; }
        public string Name => App.Name;
        public string When { get; }

        private static string Ago(TimeSpan t) =>
            t.TotalMinutes < 1 ? "Just now"
            : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min ago"
            : t.TotalDays < 1 ? $"{(int)t.TotalHours} h ago"
            : t.TotalDays < 2 ? "Yesterday"
            : $"{(int)t.TotalDays} days ago";
    }

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RecentEntry r) Launch(r.App, AdminModifiers);
    }

    private void Recent_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RecentEntry r)
        {
            e.Handled = true;
            ShowAppMenu(r.App, fe, recent: true);
        }
    }

    // ---- Interazione -----------------------------------------------------------------------------

    private void Launch(AppEntry app, bool asAdmin = false)
    {
        HideFlyout();
        if (!AppCatalogService.Launch(app.ToPinned(), asAdmin))
            MessageBox.Show($"\"{app.Name}\" can't be run as administrator.\nWindows doesn't allow elevating this app.",
                "Open TaskBar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool AdminModifiers =>
        (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                // Prima svuota la ricerca; a ricerca vuota chiude il menu (lo fa la base).
                if (SearchBox.Text.Length > 0)
                {
                    SearchBox.Text = "";
                    e.Handled = true;
                }
                break;
            case Key.Enter:
                // Ctrl+Maiusc+Invio: come amministratore (come nel menu Start di Windows).
                if ((AppList.SelectedItem ?? (SearchBox.Text.Length > 0 && AppList.Items.Count > 0 ? AppList.Items[0] : null)) is AppEntry app)
                    Launch(app, AdminModifiers);
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
        base.OnPreviewKeyDown(e);
    }

    private void AppRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AppEntry app) Launch(app, AdminModifiers);
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
        if ((sender as FrameworkElement)?.DataContext is AppEntry app) Launch(app, AdminModifiers);
    }

    private void Favorite_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AppEntry app)
        {
            e.Handled = true;
            ShowAppMenu(app, fe);
        }
    }

    private void ShowAppMenu(AppEntry app, FrameworkElement target, bool recent = false)
    {
        AppSettings s = _app.Settings;
        PinnedApp pinned = app.ToPinned();
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };

        menu.Items.Add(Ui.MenuEntry("Open", "", () => Launch(app)));
        // Per ogni app (anche Ctrl+Maiusc+click/Invio); se Windows non permette di elevarla lo si dice all'utente.
        var admin = Ui.MenuEntry("Run as administrator", "", () => Launch(app, true));
        menu.Items.Add(admin);
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
                HideFlyout();
                AppCatalogService.OpenFileLocation(app.TargetPath);
            }));
        }

        if (recent)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Ui.MenuEntry("Remove from Recent", "", () =>
            {
                UsageService.Forget(pinned.Key);
                RefreshRecent();
            }));
            menu.Items.Add(Ui.MenuEntry("Clear Recent", "", () =>
            {
                UsageService.Clear();
                RefreshRecent();
                ApplyFilter();
            }));
        }
        menu.IsOpen = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        _app.ShowSettings();
    }

    private void Power_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = PowerButton, Placement = PlacementMode.Top };
        menu.Items.Add(Ui.MenuEntry("Lock", "", () => { HideFlyout(); ShellHelper.Lock(); }));
        menu.Items.Add(Ui.MenuEntry("Sign out", "", () => { HideFlyout(); ShellHelper.Logoff(); }));
        menu.Items.Add(new Separator());
        if (PowerHelper.CanSleep())
            menu.Items.Add(Ui.MenuEntry("Sleep", "", () => { HideFlyout(); PowerHelper.Sleep(); }));
        menu.Items.Add(Ui.MenuEntry("Restart", "", () => { HideFlyout(); RunShutdown("/r /t 0"); }));
        menu.Items.Add(Ui.MenuEntry("Shut down", "", () => { HideFlyout(); RunShutdown("/s /t 0"); }));
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
