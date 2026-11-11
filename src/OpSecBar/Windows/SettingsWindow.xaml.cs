using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ManagedShell.WindowsTray;
using Microsoft.Win32;
using OpSecBar.Core;
using OpSecBar.Models;
using OpSecBar.Services;

namespace OpSecBar.Windows;

/// <summary>
/// Finestra Impostazioni, in stile Windows 11: segue il tema chiaro/scuro e il colore d'accento di sistema.
/// Le pagine sono costruite da codice con pochi "mattoncini" (Section, Card, Row, Toggle, SliderCtl, ComboCtl...):
/// per aggiungere un'opzione basta una riga nella pagina giusta. Ogni modifica è applicata subito e salvata da sola.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly App _app;
    private AppSettings S => _app.Settings;
    private ThemeData T => _app.Settings.Theme;
    private bool _dark = true;

    private static readonly (string Title, string Glyph)[] Pages =
    {
        ("Appearance", ""), ("Themes", ""), ("Behavior", ""), ("Taskbar items", ""),
        ("Pinned apps", ""), ("System tray", ""), ("Start menu", ""), ("System", "")
    };

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        ApplyPalette();
        VersionText.Text = "Version " + (GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

        SourceInitialized += (_, _) => ApplyTitleBar();
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        Closed += (_, _) => SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;

        foreach (var (title, glyph) in Pages)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)FindResource("S.Icons"), FontSize = 16, Width = 30, VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            Nav.Items.Add(new ListBoxItem { Content = row });
        }
        Nav.SelectedIndex = 0;
    }

    // =============================================================================================
    //  Tema della finestra (chiaro/scuro e accento di Windows)
    // =============================================================================================

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
        Dispatcher.BeginInvoke(() =>
        {
            ApplyPalette();
            ApplyTitleBar();
            BuildPage(false);
        });
    }

    private static bool SystemUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    private static Color SystemAccent(bool dark)
    {
        try
        {
            var ui = new global::Windows.UI.ViewManagement.UISettings();
            var c = ui.GetColorValue(dark ? global::Windows.UI.ViewManagement.UIColorType.AccentLight2 : global::Windows.UI.ViewManagement.UIColorType.AccentDark1);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch { return dark ? Color.FromRgb(0x60, 0xCD, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8); }
    }

    /// <summary>Valori dei token Fluent per il tema corrente.</summary>
    private void ApplyPalette()
    {
        _dark = SystemUsesDarkApps();
        void Set(string key, string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            Resources[key] = brush;
        }

        if (_dark)
        {
            Set("S.Window", "#202020"); Set("S.Card", "#0DFFFFFF"); Set("S.CardStroke", "#19000000"); Set("S.Divider", "#15FFFFFF");
            Set("S.Text", "#FFFFFF"); Set("S.Text2", "#C5FFFFFF"); Set("S.Text3", "#87FFFFFF");
            Set("S.Control", "#0FFFFFFF"); Set("S.ControlHover", "#15FFFFFF"); Set("S.ControlPressed", "#08FFFFFF");
            Set("S.ControlStroke", "#12FFFFFF"); Set("S.StrongStroke", "#8BFFFFFF");
            Set("S.SubtleHover", "#0FFFFFFF"); Set("S.SubtlePressed", "#0AFFFFFF");
            Set("S.ToggleOff", "#19000000"); Set("S.Thumb", "#454545"); Set("S.Flyout", "#2C2C2C"); Set("S.OnAccent", "#000000");
        }
        else
        {
            Set("S.Window", "#F3F3F3"); Set("S.Card", "#B3FFFFFF"); Set("S.CardStroke", "#0F000000"); Set("S.Divider", "#0F000000");
            Set("S.Text", "#E4000000"); Set("S.Text2", "#9E000000"); Set("S.Text3", "#72000000");
            Set("S.Control", "#B3FFFFFF"); Set("S.ControlHover", "#80F9F9F9"); Set("S.ControlPressed", "#4DF9F9F9");
            Set("S.ControlStroke", "#0F000000"); Set("S.StrongStroke", "#72000000");
            Set("S.SubtleHover", "#09000000"); Set("S.SubtlePressed", "#06000000");
            Set("S.ToggleOff", "#06000000"); Set("S.Thumb", "#FFFFFF"); Set("S.Flyout", "#F9F9F9"); Set("S.OnAccent", "#FFFFFF");
        }

        var accent = new SolidColorBrush(SystemAccent(_dark));
        accent.Freeze();
        Resources["S.Accent"] = accent;
    }

    /// <summary>Barra del titolo dello stesso colore della finestra (Windows 11), scura o chiara come il tema.</summary>
    private void ApplyTitleBar()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = _dark ? 1 : 0;
            if (Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                Native.DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
            Color bg = ((SolidColorBrush)Resources["S.Window"]).Color;
            int colorRef = bg.R | bg.G << 8 | bg.B << 16;
            Native.DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref colorRef, sizeof(int));
        }
        catch { }
    }

    // =============================================================================================
    //  Navigazione
    // =============================================================================================

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e) => BuildPage(true);

    private void BuildPage(bool animate)
    {
        double offset = animate ? 0 : PageScroll.VerticalOffset;
        PageHost.Children.Clear();
        try
        {
            switch (Nav.SelectedIndex)
            {
                case 0: BuildAppearance(); break;
                case 1: BuildThemes(); break;
                case 2: BuildBehavior(); break;
                case 3: BuildItems(); break;
                case 4: BuildPinnedApps(); break;
                case 5: BuildTray(); break;
                case 6: BuildStartMenu(); break;
                case 7: BuildSystem(); break;
            }
        }
        catch (Exception ex) { Log.Error("Costruzione pagina impostazioni", ex); }

        if (animate)
        {
            PageScroll.ScrollToTop();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            PageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        }
        else PageScroll.ScrollToVerticalOffset(offset);
    }

    /// <summary>Ricostruisce la pagina corrente mantenendo la posizione di scorrimento.</summary>
    private void Refresh() => BuildPage(false);

    // =============================================================================================
    //  Pagine
    // =============================================================================================

    private void BuildAppearance()
    {
        PageTitle("Appearance");

        if (BackdropHelper.UsesCompositionBlur(T.Backdrop) && !BackdropHelper.SystemTransparencyEnabled)
            InfoBar("Blur is turned off in Windows",
                "Transparency effects are disabled, so Blur, Acrylic and Glass are shown as plain transparency.",
                "Open Windows settings", () => Process.Start(new ProcessStartInfo("ms-settings:colors") { UseShellExecute = true }));

        var bg = Section("Background");
        Row(bg, "Material", "Blur, Acrylic and Glass blur whatever is behind the bar.", ComboCtl(new[]
        {
            (BackdropMode.Solid, "Solid"), (BackdropMode.Transparent, "Transparent"), (BackdropMode.Blur, "Blur"),
            (BackdropMode.Acrylic, "Acrylic"), (BackdropMode.Glass, "Glass")
        }, T.Backdrop, v => { Edit(t => t.Backdrop = v); Refresh(); }));
        Row(bg, "Color", null, ColorCtl(T.BackgroundColor, v => Edit(t => t.BackgroundColor = v)));
        Row(bg, "Opacity", null, SliderCtl(T.Opacity * 100, 0, 100, 1, "0'%'", v => Edit(t => t.Opacity = v / 100)));

        var colors = Section("Colors");
        Row(colors, "Text and icons", null, ColorCtl(T.ForegroundColor, v => Edit(t => t.ForegroundColor = v)));
        Row(colors, "Accent", "Active app indicator, media controls and the Start logo.", ColorCtl(T.AccentColor, v => Edit(t => t.AccentColor = v)));
        Row(colors, "Menus and flyouts", "Context menus, flyouts and the Start menu.", ColorCtl(T.PanelColor, v => Edit(t => t.PanelColor = v)));
        Row(colors, "Menu opacity", null, SliderCtl(T.PanelOpacity * 100, 55, 100, 1, "0'%'", v => Edit(t => t.PanelOpacity = v / 100)));

        var shape = Section("Shape");
        Row(shape, "Height", "Becomes the width when the bar is docked left or right.", SliderCtl(T.BarHeight, 24, 72, 1, "0 px", v => Edit(t => t.BarHeight = v)));
        Row(shape, "Corner radius", null, SliderCtl(T.CornerRadius, 0, 30, 1, "0 px", v => Edit(t => t.CornerRadius = v)));
        Row(shape, "Floating margin", "Gap between the bar and the screen edges.", SliderCtl(T.Margin, 0, 24, 1, "0 px", v => Edit(t => t.Margin = v)));
        Row(shape, "Border width", null, SliderCtl(T.BorderThickness, 0, 4, 0.5, "0.# px", v => Edit(t => t.BorderThickness = v)));
        Row(shape, "Border color", null, ColorCtl(T.BorderColor, v => Edit(t => t.BorderColor = v)));
        Row(shape, "Shadow", "Needs a floating margin.", Toggle(T.Shadow, v => Edit(t => t.Shadow = v)));

        var icons = Section("Icons");
        Row(icons, "Icon size", null, SliderCtl(T.IconSize, 14, 40, 1, "0 px", v => Edit(t => t.IconSize = v)));
        Row(icons, "Icon spacing", null, SliderCtl(T.IconSpacing, 0, 20, 1, "0 px", v => Edit(t => t.IconSpacing = v)));

        var text = Section("Text");
        var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        string current = T.FontFamily.Split(',')[0].Trim();
        if (!fonts.Contains(current)) fonts.Insert(0, current);
        Row(text, "Font", null, ComboCtl(fonts.Select(f => (f, f)).ToArray(), current,
            v => Edit(t => t.FontFamily = v == "Segoe UI" ? v : v + ", Segoe UI"), 240));
        Row(text, "Text size", null, SliderCtl(T.FontSize, 9, 18, 0.5, "0.# px", v => Edit(t => t.FontSize = v)));
    }

    private void BuildThemes()
    {
        PageTitle("Themes");

        SectionHeader("Built-in");
        var builtIn = new List<(ThemeData Theme, bool User, bool Unsaved)>();
        if (T.Name == ThemeService.CustomName) builtIn.Add((T, false, true));
        builtIn.AddRange(ThemeService.Presets.Select(p => (p, false, false)));
        PageHost.Children.Add(ThemeGallery(builtIn));

        var userThemes = ThemeService.LoadUserThemes();
        SectionHeader("Your themes");
        if (userThemes.Count == 0)
            Card().Children.Add(Empty("No saved themes yet. Adjust the look in Appearance, then save it here."));
        else
            PageHost.Children.Add(ThemeGallery(userThemes.Select(t => (t, true, false)).ToList()));

        var manage = Section("Manage");
        var nameBox = new TextBox
        {
            Style = (Style)FindResource("S.TextBox"), Width = 200,
            Text = T.Name == ThemeService.CustomName || ThemeService.IsPreset(T.Name) ? "My theme" : T.Name
        };
        var error = new TextBlock { Foreground = (Brush)FindResource("S.Text2"), FontSize = 12, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal };
        saveRow.Children.Add(nameBox);
        saveRow.Children.Add(Gap(8));
        saveRow.Children.Add(Btn("Save", () =>
        {
            string name = nameBox.Text.Trim();
            if (name.Length == 0 || ThemeService.IsPreset(name) || name == ThemeService.CustomName)
            {
                // Errore mostrato sul posto, non in una finestra di dialogo.
                error.Text = name.Length == 0 ? "Enter a name for the theme." : "That name belongs to a built-in theme. Choose another one.";
                error.Visibility = Visibility.Visible;
                return;
            }
            T.Name = name;
            ThemeService.SaveUserTheme(T);
            Refresh();
        }, true));
        var saveControl = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        saveControl.Children.Add(saveRow);
        saveControl.Children.Add(error);
        Row(manage, "Save current theme", "Keeps the current appearance as a reusable theme.", saveControl);

        var io = new StackPanel { Orientation = Orientation.Horizontal };
        io.Children.Add(Btn("Import…", ImportTheme));
        io.Children.Add(Gap(8));
        io.Children.Add(Btn("Export…", () => ExportTheme(T)));
        Row(manage, "Theme files", "Share themes as .json files.", io);
    }

    /// <summary>Griglia di anteprime: ogni tessera disegna una barra in miniatura con i valori del tema.</summary>
    private FrameworkElement ThemeGallery(List<(ThemeData Theme, bool User, bool Unsaved)> themes)
    {
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -12, 0) };
        foreach (var (theme, user, unsaved) in themes)
        {
            bool active = unsaved || string.Equals(T.Name, theme.Name, StringComparison.OrdinalIgnoreCase);

            var footer = new Grid { Margin = new Thickness(12, 0, 6, 0), Height = 44 };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var caption = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            caption.Children.Add(new TextBlock { Text = unsaved ? "Custom" : theme.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            if (active)
                caption.Children.Add(new TextBlock
                {
                    Text = unsaved ? "Current · not saved" : "Current", FontSize = 12, Foreground = (Brush)FindResource("S.Text2")
                });
            footer.Children.Add(caption);

            if (user)
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                actions.Children.Add(IconBtn("", "Export", () => ExportTheme(theme)));
                actions.Children.Add(IconBtn("", "Delete", () =>
                {
                    ThemeService.DeleteUserTheme(theme.Name);
                    Refresh();
                }));
                Grid.SetColumn(actions, 1);
                footer.Children.Add(actions);
            }

            var body = new StackPanel();
            body.Children.Add(ThemePreview(theme));
            body.Children.Add(footer);

            var tile = new Button
            {
                Margin = new Thickness(0, 0, 12, 12), Cursor = Cursors.Hand, Content = body, ToolTip = active ? null : "Apply " + theme.Name,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, FocusVisualStyle = (Style)FindResource("S.Focus"),
                Template = TileTemplate(active)
            };
            if (!unsaved)
                tile.Click += (_, _) => Safe(() =>
                {
                    S.Theme = theme.Clone();
                    Refresh();
                });
            grid.Children.Add(tile);
        }
        return grid;
    }

    private ControlTemplate TileTemplate(bool active)
    {
        // Bordo d'accento spesso per il tema attivo; bordo neutro (più marcato al passaggio del mouse) per gli altri.
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, FindResource("S.Card"));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(active ? 2 : 1));
        border.SetValue(Border.BorderBrushProperty, FindResource(active ? "S.Accent" : "S.CardStroke"));
        border.SetValue(Border.PaddingProperty, new Thickness(active ? 0 : 1));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        if (!active)
        {
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, FindResource("S.StrongStroke"), "Bd"));
            template.Triggers.Add(hover);
        }
        return template;
    }

    private FrameworkElement ThemePreview(ThemeData theme)
    {
        Color bg = ThemeService.ParseColor(theme.BackgroundColor, Colors.Black);
        Color fg = ThemeService.ParseColor(theme.ForegroundColor, Colors.White);
        Color accent = ThemeService.ParseColor(theme.AccentColor, Colors.White);
        double margin = Math.Min(theme.Margin, 10) * 0.6;
        double radius = Math.Min(theme.CornerRadius * 0.45, 7);
        bool noFill = theme.Backdrop == BackdropMode.Transparent;

        // "Desktop" neutro su cui appoggiare la barra in miniatura.
        var desktop = new Border
        {
            Height = 92, CornerRadius = new CornerRadius(5, 5, 0, 0), ClipToBounds = true,
            Background = new LinearGradientBrush(
                _dark ? Color.FromRgb(0x4A, 0x50, 0x59) : Color.FromRgb(0xC9, 0xD0, 0xDA),
                _dark ? Color.FromRgb(0x2A, 0x2D, 0x33) : Color.FromRgb(0xAE, 0xB7, 0xC3), 90)
        };

        var content = new Grid { Margin = new Thickness(5, 0, 5, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 4; i++)
        {
            var icon = new Grid { Width = 9, Margin = new Thickness(0, 0, 4, 0) };
            icon.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(ThemeService.WithAlpha(fg, 0.85)), VerticalAlignment = VerticalAlignment.Center });
            if (i == 1)
                icon.Children.Add(new Border { Width = 7, Height = 1.5, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(accent), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -3.5) });
            left.Children.Add(icon);
        }
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new Border { Width = 22, Height = 3, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(ThemeService.WithAlpha(fg, 0.7)), Margin = new Thickness(0, 0, 6, 0) });
        right.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(accent) });
        content.Children.Add(left);
        content.Children.Add(right);

        double border = Math.Min(theme.BorderThickness, 1);
        desktop.Child = new Border
        {
            Height = Math.Clamp(theme.BarHeight * 0.42, 13, 22), VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(margin, margin, margin, 0), CornerRadius = new CornerRadius(radius), Child = content,
            Background = noFill ? Brushes.Transparent : new SolidColorBrush(ThemeService.WithAlpha(bg, Math.Max(theme.Opacity, 0.3))),
            BorderThickness = new Thickness(border),
            BorderBrush = border > 0 ? new SolidColorBrush(ThemeService.ParseColor(theme.BorderColor, Colors.Transparent)) : null
        };
        return desktop;
    }

    private void ImportTheme()
    {
        var dialog = new OpenFileDialog { Title = "Import theme", Filter = "OpSec Bar theme (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        ThemeData? theme = ThemeService.ReadThemeFile(dialog.FileName);
        if (theme == null)
        {
            MessageBox.Show(this, "This file is not a valid OpSec Bar theme.", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (ThemeService.IsPreset(theme.Name) || theme.Name == ThemeService.CustomName) theme.Name += " (imported)";
        ThemeService.SaveUserTheme(theme);
        S.Theme = theme.Clone();
        Refresh();
    }

    private void ExportTheme(ThemeData theme)
    {
        var dialog = new SaveFileDialog { Title = "Export theme", Filter = "OpSec Bar theme (*.json)|*.json", FileName = theme.Name + ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try { ThemeService.WriteThemeFile(theme, dialog.FileName); }
        catch (Exception ex)
        {
            Log.Error("Esportazione tema", ex);
            MessageBox.Show(this, "The theme could not be saved.\n" + ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BuildBehavior()
    {
        PageTitle("Behavior");

        var pos = Section("Position");
        Row(pos, "Screen edge", null, ComboCtl(new[]
        {
            (BarPosition.Top, "Top"), (BarPosition.Bottom, "Bottom"), (BarPosition.Left, "Left"), (BarPosition.Right, "Right")
        }, S.Position, v => S.Position = v));
        Row(pos, "Show on all displays", "When off, the bar appears on the main display only.", Toggle(S.AllMonitors, v => S.AllMonitors = v));

        var vis = Section("Visibility");
        Row(vis, "Automatically hide the taskbar", "Move the pointer to the screen edge to bring it back.",
            Toggle(S.Behavior == BarBehavior.AutoHide, v => S.Behavior = v ? BarBehavior.AutoHide : BarBehavior.AlwaysVisible));
        Row(vis, "Hide for full-screen apps", "Games, videos and presentations cover the bar.", Toggle(S.HideOnFullscreen, v => S.HideOnFullscreen = v));
    }

    private void BuildItems()
    {
        PageTitle("Taskbar items");

        var apps = Section("Apps");
        Row(apps, "Pinned apps", null, Toggle(S.ShowPinnedApps, v => S.ShowPinnedApps = v));
        Row(apps, "Running apps", null, Toggle(S.ShowRunningApps, v => S.ShowRunningApps = v));
        Row(apps, "Combine windows of the same app", "One icon per app. Clicking it lists the open windows.", Toggle(S.GroupWindows, v => S.GroupWindows = v));

        var media = Section("Media");
        Row(media, "Media player", "Appears only while something is playing.", Toggle(S.ShowMediaPlayer, v => S.ShowMediaPlayer = v));
        Row(media, "Track title and artist", null, Toggle(S.ShowMediaTitle, v => S.ShowMediaTitle = v));

        var clock = Section("Clock");
        Row(clock, "Clock", null, Toggle(S.ShowClock, v => S.ShowClock = v));
        Row(clock, "Date", null, Toggle(S.ShowDate, v => S.ShowDate = v));
        Row(clock, "Seconds", null, Toggle(S.ShowSeconds, v => S.ShowSeconds = v));
        Row(clock, "24-hour clock", null, Toggle(S.Clock24h, v => S.Clock24h = v));

        var other = Section("Corner");
        Row(other, "System tray", "Icons of apps running in the background.", Toggle(S.ShowTray, v => S.ShowTray = v));
        Row(other, "Quick settings button", "Opens Windows quick settings for Wi‑Fi, sound and brightness.", Toggle(S.ShowQuickSettings, v => S.ShowQuickSettings = v));
        Row(other, "Start button", null, Toggle(S.ShowStartButton, v => S.ShowStartButton = v));
        Row(other, "System monitor", "CPU, memory and battery.", Toggle(S.ShowSysInfo, v => S.ShowSysInfo = v));
    }

    private void BuildPinnedApps()
    {
        PageTitle("Pinned apps");

        var add = Card();
        Row(add, "Add an app", "You can also right-click an app on the bar or in the Start menu.", Btn("Browse…", AddPinnedFromFile));

        SectionHeader("On the taskbar");
        var card = Card();
        if (S.PinnedApps.Count == 0) card.Children.Add(Empty("Nothing is pinned yet."));

        for (int i = 0; i < S.PinnedApps.Count; i++)
        {
            int index = i;
            PinnedApp app = S.PinnedApps[i];
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(IconBtn("", "Move up", () => MovePinned(index, -1), index > 0));
            buttons.Children.Add(IconBtn("", "Move down", () => MovePinned(index, 1), index < S.PinnedApps.Count - 1));
            buttons.Children.Add(IconBtn("", "Unpin", () =>
            {
                S.PinnedApps.RemoveAt(index);
                PinnedChanged();
            }));
            RowCustom(card, AppLabel(app), buttons);
        }
    }

    /// <summary>Icona reale dell'app (caricata in background) + nome + percorso.</summary>
    private FrameworkElement AppLabel(PinnedApp app)
    {
        var image = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        LoadIcon(image, app.IconKey);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = app.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        string? detail = app.TargetPath ?? app.AppId;
        if (!string.IsNullOrEmpty(detail))
            text.Children.Add(new TextBlock
            {
                Text = detail, FontSize = 12, Foreground = (Brush)FindResource("S.Text2"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0)
            });

        var panel = new DockPanel();
        DockPanel.SetDock(image, Dock.Left);
        panel.Children.Add(image);
        panel.Children.Add(text);
        return panel;
    }

    private static async void LoadIcon(Image target, string key)
    {
        try { target.Source = await IconService.GetAsync(key); }
        catch (Exception ex) { Log.Warn("Icona impostazioni: " + ex.Message); }
    }

    private void MovePinned(int index, int delta)
    {
        int target = index + delta;
        if (target < 0 || target >= S.PinnedApps.Count) return;
        (S.PinnedApps[index], S.PinnedApps[target]) = (S.PinnedApps[target], S.PinnedApps[index]);
        PinnedChanged();
    }

    private void PinnedChanged()
    {
        _app.SettingsService.Notify(nameof(AppSettings.PinnedApps));
        Refresh();
    }

    private void AddPinnedFromFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an app", Filter = "Apps (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*", DereferenceLinks = true
        };
        if (dialog.ShowDialog(this) != true) return;

        string path = dialog.FileName;
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) name = description;
        }
        catch { }
        _app.TaskList.Pin(new PinnedApp { Name = name, TargetPath = path });
        Refresh();
    }

    private void BuildTray()
    {
        PageTitle("System tray");

        NotificationArea area = _app.Shell.NotificationArea;
        var icons = area.TrayIcons.ToList();

        SectionHeader("Always show on the taskbar", "Icons that are turned off stay in the hidden icons flyout.");
        var card = Card();
        if (icons.Count == 0) card.Children.Add(Empty("No apps are showing a tray icon right now."));

        foreach (NotifyIcon icon in icons)
        {
            var image = new Image { Source = icon.Icon, Width = 16, Height = 16, Margin = new Thickness(4, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            var label = new DockPanel();
            DockPanel.SetDock(image, Dock.Left);
            label.Children.Add(image);
            label.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(icon.Title) ? System.IO.Path.GetFileNameWithoutExtension(icon.Path ?? "") : icon.Title.Split('\n')[0].Trim(),
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            });
            RowCustom(card, label, Toggle(icon.IsPinned, v =>
            {
                if (v) icon.Pin(); else icon.Unpin();
                S.PinnedTrayIcons = area.PinnedNotifyIcons;
                _app.SettingsService.Notify(nameof(AppSettings.PinnedTrayIcons));
            }));
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(Btn("Refresh list", Refresh));
        PageHost.Children.Add(actions);
    }

    private void BuildStartMenu()
    {
        PageTitle("Start menu");

        var open = Section("Open");
        Row(open, "Open with the Windows key", "Shortcuts such as Win+E, Win+D and Win+L keep working as usual.", Toggle(S.UseWinKey, v => S.UseWinKey = v));

        var layout = Section("Layout");
        Row(layout, "Width", null, SliderCtl(S.StartMenuWidth, 320, 800, 10, "0 px", v => S.StartMenuWidth = v));
        Row(layout, "Height", null, SliderCtl(S.StartMenuHeight, 380, 1000, 10, "0 px", v => S.StartMenuHeight = v));
        Row(layout, "Show favorites", null, Toggle(S.StartShowFavorites, v => S.StartShowFavorites = v));

        SectionHeader("Favorites", "Right-click an app in the Start menu to add it here.");
        var fav = Card();
        if (S.StartFavorites.Count == 0) fav.Children.Add(Empty("No favorites yet."));
        for (int i = 0; i < S.StartFavorites.Count; i++)
        {
            int index = i;
            RowCustom(fav, AppLabel(S.StartFavorites[i]), IconBtn("", "Remove", () =>
            {
                S.StartFavorites.RemoveAt(index);
                _app.SettingsService.Notify(nameof(AppSettings.StartFavorites));
                Refresh();
            }));
        }
    }

    private void BuildSystem()
    {
        PageTitle("System");

        var startup = Section("Startup");
        Row(startup, "Start OpSec Bar when I sign in", null, Toggle(S.AutoStart, v => S.AutoStart = v));

        var win = Section("Windows taskbar");
        Row(win, "Hide the Windows taskbar", "Explorer keeps running. The Windows taskbar comes back when OpSec Bar exits, even after a crash.",
            Toggle(S.HideWindowsTaskbar, v => S.HideWindowsTaskbar = v));

        var recovery = Section("Recovery");
        Row(recovery, "Emergency exit", "Closes OpSec Bar and restores the Windows taskbar from anywhere.", KeyCaps("Ctrl", "Alt", "Shift", "Q"));
        Row(recovery, "Restart OpSec Bar", null, Btn("Restart", () => _app.ExitApp(true)));
        Row(recovery, "Exit OpSec Bar", "Restores the Windows taskbar.", Btn("Exit", () => _app.ExitApp()));

        var about = Section("About");
        Row(about, "Version", null, new TextBlock
        {
            Text = GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0", Foreground = (Brush)FindResource("S.Text2")
        });
        Row(about, "Data folder", "Settings, themes and the log file.", Btn("Open folder", () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = false })));
    }

    // =============================================================================================
    //  Mattoncini
    // =============================================================================================

    /// <summary>Modifica il tema corrente; se era un tema con nome diventa "Custom".</summary>
    private void Edit(Action<ThemeData> change)
    {
        change(T);
        if (T.Name != ThemeService.CustomName) T.Name = ThemeService.CustomName;
    }

    private void PageTitle(string title) => PageHost.Children.Add(new TextBlock
    {
        Text = title, FontFamily = (FontFamily)FindResource("S.Display"), FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4)
    });

    private void SectionHeader(string header, string? description = null)
    {
        PageHost.Children.Add(new TextBlock { Text = header, FontWeight = FontWeights.SemiBold, Margin = new Thickness(1, 24, 0, description == null ? 8 : 2) });
        if (description != null)
            PageHost.Children.Add(new TextBlock
            {
                Text = description, FontSize = 12, Foreground = (Brush)FindResource("S.Text2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 0, 0, 8)
            });
    }

    private StackPanel Section(string header)
    {
        SectionHeader(header);
        return Card();
    }

    private StackPanel Card()
    {
        var inner = new StackPanel();
        PageHost.Children.Add(new Border
        {
            Background = (Brush)FindResource("S.Card"), BorderBrush = (Brush)FindResource("S.CardStroke"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Margin = new Thickness(0, PageHost.Children.Count == 1 ? 16 : 0, 0, 0), Child = inner
        });
        return inner;
    }

    private void Row(Panel card, string label, string? description, FrameworkElement control)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(description))
            text.Children.Add(new TextBlock
            {
                Text = description, FontSize = 12, Foreground = (Brush)FindResource("S.Text2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
            });
        RowCustom(card, text, control);
    }

    private void RowCustom(Panel card, FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid { MinHeight = 52, Margin = new Thickness(16, 0, 16, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(0, 12, 24, 13);
        right.VerticalAlignment = VerticalAlignment.Center;
        right.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);

        // Divisore a tutta larghezza tra le righe della stessa scheda.
        if (card.Children.Count > 0) card.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("S.Divider") });
        card.Children.Add(grid);
    }

    private TextBlock Empty(string text) => new()
    {
        Text = text, Foreground = (Brush)FindResource("S.Text2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 16, 16, 17)
    };

    private static FrameworkElement Gap(double width) => new Border { Width = width };

    /// <summary>Avviso informativo in cima alla pagina, con un'azione.</summary>
    private void InfoBar(string title, string message, string actionText, Action action)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = "", FontFamily = (FontFamily)FindResource("S.Icons"), FontSize = 16, Foreground = (Brush)FindResource("S.Accent"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0)
        });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = message, FontSize = 12, Foreground = (Brush)FindResource("S.Text2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Button button = Btn(actionText, action);
        button.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);

        PageHost.Children.Add(new Border
        {
            Background = (Brush)FindResource("S.Card"), BorderBrush = (Brush)FindResource("S.CardStroke"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 16, 0, 0), Child = grid
        });
    }

    private FrameworkElement KeyCaps(params string[] keys)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0) panel.Children.Add(new TextBlock { Text = "+", Foreground = (Brush)FindResource("S.Text3"), Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new Border
            {
                Background = (Brush)FindResource("S.Control"), BorderBrush = (Brush)FindResource("S.ControlStroke"), BorderThickness = new Thickness(1, 1, 1, 2),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 3), MinWidth = 30,
                Child = new TextBlock { Text = keys[i], FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center }
            });
        }
        return panel;
    }

    private CheckBox Toggle(bool value, Action<bool> set)
    {
        var box = new CheckBox { Style = (Style)FindResource("S.Toggle"), IsChecked = value };
        box.Checked += (_, _) => Safe(() => set(true));
        box.Unchecked += (_, _) => Safe(() => set(false));
        return box;
    }

    private FrameworkElement SliderCtl(double value, double min, double max, double step, string format, Action<double> set)
    {
        var label = new TextBlock
        {
            Width = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
            Foreground = (Brush)FindResource("S.Text2"), Text = value.ToString(format, CultureInfo.InvariantCulture)
        };
        Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular); // cifre a larghezza fissa: il valore non "balla"
        var slider = new Slider
        {
            Style = (Style)FindResource("S.Slider"), Minimum = min, Maximum = max, Width = 220,
            TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 5,
            Value = Math.Clamp(value, min, max)
        };
        slider.ValueChanged += (_, e) =>
        {
            label.Text = e.NewValue.ToString(format, CultureInfo.InvariantCulture);
            Safe(() => set(e.NewValue));
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(label);
        panel.Children.Add(slider);
        return panel;
    }

    private FrameworkElement ComboCtl<TValue>((TValue Value, string Label)[] options, TValue current, Action<TValue> set, double width = 180)
    {
        var combo = new ComboBox { Style = (Style)FindResource("S.Combo"), Width = width, ItemsSource = options.Select(o => o.Label).ToList() };
        combo.SelectedIndex = Array.FindIndex(options, o => EqualityComparer<TValue>.Default.Equals(o.Value, current));
        combo.SelectionChanged += (_, _) =>
        {
            int index = combo.SelectedIndex;
            if (index >= 0 && index < options.Length) Safe(() => set(options[index].Value));
        };
        return combo;
    }

    private FrameworkElement ColorCtl(string hex, Action<string> set)
    {
        Color color = ThemeService.ParseColor(hex, Colors.White);
        var fill = new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color) };
        var swatch = new Button
        {
            Style = (Style)FindResource("S.Button"), Width = 44, MinWidth = 44, Height = 32, Padding = new Thickness(4), ToolTip = "Choose a color",
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Content = fill
        };
        fill.Width = 34;
        fill.Height = 22;
        var box = new TextBox
        {
            Style = (Style)FindResource("S.TextBox"), Width = 104, Margin = new Thickness(8, 0, 0, 0), Text = hex, MaxLength = 9,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, CharacterCasing = CharacterCasing.Upper
        };

        void Commit(string text)
        {
            text = text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            Color parsed;
            try { parsed = (Color)ColorConverter.ConvertFromString(text); }
            catch
            {
                box.Text = hex; // valore non valido: si ripristina l'ultimo buono
                return;
            }
            hex = ThemeService.ToHex(parsed);
            box.Text = hex;
            fill.Background = new SolidColorBrush(parsed);
            Safe(() => set(hex));
        }

        box.LostKeyboardFocus += (_, _) => { if (!string.Equals(box.Text, hex, StringComparison.OrdinalIgnoreCase)) Commit(box.Text); };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(box.Text); };
        swatch.Click += (_, _) =>
        {
            Color now = ThemeService.ParseColor(hex, Colors.White);
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true, AnyColor = true, Color = System.Drawing.Color.FromArgb(now.R, now.G, now.B)
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            // La finestra dei colori non gestisce la trasparenza: si conserva quella attuale.
            Commit(ThemeService.ToHex(Color.FromArgb(now.A, dialog.Color.R, dialog.Color.G, dialog.Color.B)));
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(swatch);
        panel.Children.Add(box);
        return panel;
    }

    private Button Btn(string text, Action click, bool accent = false)
    {
        var button = new Button { Style = (Style)FindResource(accent ? "S.AccentButton" : "S.Button"), Content = text };
        button.Click += (_, _) => Safe(click);
        return button;
    }

    private Button IconBtn(string glyph, string tooltip, Action click, bool enabled = true)
    {
        var button = new Button
        {
            Style = (Style)FindResource("S.IconButton"), Margin = new Thickness(2, 0, 0, 0), ToolTip = tooltip, IsEnabled = enabled,
            Content = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("S.Icons"), FontSize = 14 }
        };
        button.Click += (_, e) =>
        {
            e.Handled = true; // dentro una tessera cliccabile non deve attivare anche la tessera
            Safe(click);
        };
        return button;
    }

    private static void Safe(Action action)
    {
        try { action(); } catch (Exception ex) { Log.Error("Impostazioni", ex); }
    }
}
