using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
            ApplyFilter(); // la disposizione delle app (o la larghezza, da cui dipendono le colonne) può essere cambiata
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
        StartAppLayout layout = ApplyAppLayout(searching: query.Length > 0);
        if (layout == StartAppLayout.Grouped)
        {
            var view = new ListCollectionView(list);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AppEntry.Name), LetterConverter.Instance));
            AppList.ItemsSource = view;
        }
        else AppList.ItemsSource = list;
        if (query.Length > 0 && list.Count > 0) AppList.SelectedIndex = 0;

        EmptyText.Text = !_loaded ? "Loading apps…" : "No results";
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSections();
    }

    // ---- Disposizione delle app -----------------------------------------------------------------

    private ItemsPanelTemplate? _listPanel;   // il pannello virtualizzato predefinito dell'elenco
    private GroupStyle? _letterGroups;
    private (StartAppLayout Layout, int Columns)? _appliedLayout;
    private int _columns = 1;                 // colonne della disposizione attuale (per le frecce)

    /// <summary>
    /// Imposta modello, pannello e gruppi dell'elenco secondo la disposizione scelta nelle impostazioni.
    /// I risultati della ricerca sono ordinati per pertinenza, quindi restano un elenco (compatto se lo è la disposizione).
    /// </summary>
    private StartAppLayout ApplyAppLayout(bool searching)
    {
        AppSettings s = _app.Settings;
        StartAppLayout layout = Enum.IsDefined(s.StartAppLayout) ? s.StartAppLayout : StartAppLayout.List;
        if (searching) layout = layout == StartAppLayout.Compact ? StartAppLayout.Compact : StartAppLayout.List;

        // Larghezza utile dell'elenco: pannello meno i margini interni e la barra di scorrimento.
        double width = Math.Clamp(s.StartMenuWidth, 320, 900) - 32 - SystemParameters.VerticalScrollBarWidth;
        int columns = layout switch
        {
            StartAppLayout.Columns => 2,
            StartAppLayout.Grid => Math.Max(2, (int)(width / 92)),
            _ => 1
        };
        _columns = columns;
        if (_appliedLayout == (layout, columns)) return layout;
        _appliedLayout = (layout, columns);

        _listPanel ??= AppList.ItemsPanel;
        _letterGroups ??= new GroupStyle { HeaderTemplate = (DataTemplate)FindResource("LetterHeaderTemplate") };

        AppList.ItemsSource = null; // niente contenitori vecchi da riciclare nel nuovo pannello
        AppList.GroupStyle.Clear();
        if (layout == StartAppLayout.Grouped) AppList.GroupStyle.Add(_letterGroups);

        AppList.ItemTemplate = (DataTemplate)FindResource(layout switch
        {
            StartAppLayout.Compact => "AppCompactTemplate",
            StartAppLayout.Grid => "AppTileTemplate",
            _ => "AppRowTemplate"
        });

        if (columns > 1)
        {
            var grid = new FrameworkElementFactory(typeof(UniformGrid));
            grid.SetValue(UniformGrid.ColumnsProperty, columns);
            grid.SetValue(VerticalAlignmentProperty, VerticalAlignment.Top);
            AppList.ItemsPanel = new ItemsPanelTemplate(grid);
        }
        else AppList.ItemsPanel = _listPanel;
        return layout;
    }

    /// <summary>Lettera iniziale del nome (senza accenti) per i gruppi; "#" per cifre e simboli.</summary>
    private sealed class LetterConverter : System.Windows.Data.IValueConverter
    {
        public static readonly LetterConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string name = (value as string ?? "").TrimStart();
            if (name.Length == 0) return "#";
            char c = char.ToUpperInvariant(name.Normalize(System.Text.NormalizationForm.FormD)[0]);
            return char.IsLetter(c) ? c.ToString() : "#";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
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
            case Key.Left:
            case Key.Right:
                // Solo nelle disposizioni a più colonne, che non compaiono mai mentre si cerca: il cursore della ricerca resta libero.
                if (_columns > 1 && SearchBox.Text.Length == 0) goto case Key.Down;
                break;
            case Key.Down:
            case Key.Up:
                if (AppList.Items.Count == 0) break;
                int current = AppList.SelectedIndex, last = AppList.Items.Count - 1;
                int step = e.Key switch { Key.Down => _columns, Key.Up => -_columns, Key.Right => 1, _ => -1 };
                int index = current < 0 ? 0 : current + step;
                if (index < 0) index = current;                       // già sulla prima riga
                if (index > last) index = step == 1 ? current : last; // ultima riga incompleta: si va all'ultima app
                AppList.SelectedIndex = index;
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
