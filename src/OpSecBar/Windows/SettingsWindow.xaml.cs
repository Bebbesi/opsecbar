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
/// Finestra Impostazioni, in stile Windows 11: i colori della finestra seguono il tema della barra (menu, testo, accento).
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
        ("Layout", "\uE90E"),
        ("Pinned apps", ""), ("System tray", ""), ("Start menu", ""), ("Widgets", "\uE823"), ("GIFs", "\uE8B9"),
        ("System", "")
    };

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        ApplyPalette();
        VersionText.Text = "Version " + _app.Updates.CurrentLabel;

        SourceInitialized += (_, _) => ApplyTitleBar();
        _app.SettingsService.Changed += OnSettingChanged;
        Closed += (_, _) => _app.SettingsService.Changed -= OnSettingChanged;

        // Lo stato dell'aggiornamento cambia in background: la pagina System si aggiorna da sola.
        Action onUpdateChanged = () => Dispatcher.BeginInvoke(() => { if (CurrentPage == "System") BuildPage(false); });
        _app.Updates.Changed += onUpdateChanged;
        Closed += (_, _) => _app.Updates.Changed -= onUpdateChanged;

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
    //  Tema della finestra (derivato dal tema della barra)
    // =============================================================================================

    /// <summary>Quando cambiano i colori del tema (o si applica un altro tema) la finestra si ridipinge.</summary>
    private void OnSettingChanged(string name)
    {
        // GIF aggiunte trascinandole sulla barra, icone del tray spostate a mano: la pagina aperta si aggiorna.
        if ((name == nameof(AppSettings.Gifs) && CurrentPage == "GIFs") ||
            (name == nameof(AppSettings.PinnedTrayIcons) && CurrentPage == "System tray"))
        {
            Dispatcher.BeginInvoke(() => BuildPage(false));
            return;
        }
        if (name is not ("Theme" or "Theme.PanelColor" or "Theme.ForegroundColor" or "Theme.AccentColor")) return;
        Dispatcher.BeginInvoke(() =>
        {
            ApplyPalette();
            ApplyTitleBar();
            BuildPage(false);
        });
    }

    private static double Luma(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    private static Color Blend(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    /// <summary>Valori dei token Fluent calcolati dal colore dei menu e dall'accento del tema corrente.</summary>
    private void ApplyPalette()
    {
        Color panel = ThemeService.ParseColor(T.PanelColor, Color.FromRgb(0x20, 0x20, 0x20));
        panel = Color.FromRgb(panel.R, panel.G, panel.B);
        _dark = Luma(panel) < 140;

        Color accent = ThemeService.ParseColor(T.AccentColor, Color.FromRgb(0x60, 0xCD, 0xFF));
        accent = Color.FromRgb(accent.R, accent.G, accent.B);
        // Un accento troppo simile allo sfondo sarebbe invisibile: in quel caso si usa il colore del testo.
        if (Math.Abs(Luma(accent) - Luma(panel)) < 60)
            accent = _dark ? Colors.White : Color.FromRgb(0x17, 0x18, 0x1C);

        void Set(string key, string hex) => Put(key, (Color)ColorConverter.ConvertFromString(hex));
        void Put(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Resources[key] = brush;
        }

        if (_dark)
        {
            Put("S.Window", panel); Set("S.Card", "#0DFFFFFF"); Set("S.CardStroke", "#19000000"); Set("S.Divider", "#15FFFFFF");
            Set("S.Text", "#FFFFFF"); Set("S.Text2", "#C5FFFFFF"); Set("S.Text3", "#87FFFFFF");
            Set("S.Control", "#0FFFFFFF"); Set("S.ControlHover", "#15FFFFFF"); Set("S.ControlPressed", "#08FFFFFF");
            Set("S.ControlStroke", "#12FFFFFF"); Set("S.StrongStroke", "#8BFFFFFF");
            Set("S.SubtleHover", "#0FFFFFFF"); Set("S.SubtlePressed", "#0AFFFFFF");
            Set("S.ToggleOff", "#19000000"); Put("S.Thumb", Blend(panel, Colors.White, 0.22)); Put("S.Flyout", Blend(panel, Colors.White, 0.08));
        }
        else
        {
            Put("S.Window", Blend(panel, Colors.Black, 0.04)); Set("S.Card", "#B3FFFFFF"); Set("S.CardStroke", "#0F000000"); Set("S.Divider", "#0F000000");
            Set("S.Text", "#E4000000"); Set("S.Text2", "#9E000000"); Set("S.Text3", "#72000000");
            Set("S.Control", "#B3FFFFFF"); Set("S.ControlHover", "#80F9F9F9"); Set("S.ControlPressed", "#4DF9F9F9");
            Set("S.ControlStroke", "#0F000000"); Set("S.StrongStroke", "#72000000");
            Set("S.SubtleHover", "#09000000"); Set("S.SubtlePressed", "#06000000");
            Set("S.ToggleOff", "#06000000"); Put("S.Thumb", Colors.White); Put("S.Flyout", Blend(panel, Colors.White, 0.5));
        }

        Put("S.Accent", accent);
        Put("S.OnAccent", Luma(accent) > 150 ? Colors.Black : Colors.White);
    }

    /// <summary>Barra del titolo dello stesso colore della finestra (Windows 11), scura o chiara come il tema della barra.</summary>
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

    private string CurrentPage => Nav.SelectedIndex >= 0 && Nav.SelectedIndex < Pages.Length ? Pages[Nav.SelectedIndex].Title : "";

    /// <summary>Apre una pagina per titolo (es. dal menu di una GIF sulla barra).</summary>
    public void ShowPage(string title)
    {
        int index = Array.FindIndex(Pages, p => p.Title == title);
        if (index >= 0) Nav.SelectedIndex = index;
    }

    private void BuildPage(bool animate)
    {
        double offset = animate ? 0 : PageScroll.VerticalOffset;
        PageHost.Children.Clear();
        try
        {
            switch (CurrentPage)
            {
                case "Appearance": BuildAppearance(); break;
                case "Themes": BuildThemes(); break;
                case "Behavior": BuildBehavior(); break;
                case "Taskbar items": BuildItems(); break;
                case "Layout": BuildLayout(); break;
                case "Pinned apps": BuildPinnedApps(); break;
                case "System tray": BuildTray(); break;
                case "Start menu": BuildStartMenu(); break;
                case "Widgets": BuildWidgets(); break;
                case "GIFs": BuildGifs(); break;
                case "System": BuildSystem(); break;
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
        Row(bg, "Gradient", "Blends the background into a second color along the bar.",
            OptionalColorCtl(T.BackgroundColor2, "#7C3AED", v => Edit(t => t.BackgroundColor2 = v)));
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
        var border = Section("Border");
        Row(border, "Width", null, SliderCtl(T.BorderThickness, 0, 6, 0.5, "0.# px", v => Edit(t => t.BorderThickness = v)));
        Row(border, "Sides", "Draw the border all around, or as a single line on one side.", ComboCtl(new[]
        {
            (BorderSides.All, "All sides"), (BorderSides.DesktopSide, "Desktop side only"), (BorderSides.ScreenSide, "Screen edge only")
        }, T.BorderSides, v => Edit(t => t.BorderSides = v), 200));
        Row(border, "Color", null, ColorCtl(T.BorderColor, v => Edit(t => t.BorderColor = v)));
        Row(border, "Gradient", "Fades the border into a second color, great for neon looks.",
            OptionalColorCtl(T.BorderColor2, "#00F0FF", v => Edit(t => t.BorderColor2 = v)));

        var shadow = Section("Shadow and glow");
        Row(shadow, "Shadow", "Needs a floating margin.", Toggle(T.Shadow, v => Edit(t => t.Shadow = v)));
        Row(shadow, "Glow", "Spreads the shadow evenly around the bar, like a neon light.", Toggle(T.ShadowGlow, v => Edit(t => t.ShadowGlow = v)));
        Row(shadow, "Color", null, ColorCtl(T.ShadowColor, v => Edit(t => t.ShadowColor = v)));
        Row(shadow, "Size", null, SliderCtl(T.ShadowSize, 4, 40, 1, "0 px", v => Edit(t => t.ShadowSize = v)));

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
                Foreground = (Brush)FindResource("S.Text"), Template = TileTemplate(active)
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
                _dark ? Color.FromRgb(0x6B, 0x6B, 0x6B) : Color.FromRgb(0xD9, 0xD9, 0xD9),
                _dark ? Color.FromRgb(0x47, 0x47, 0x47) : Color.FromRgb(0xBD, 0xBD, 0xBD), 90)
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
        Color clock = ThemeService.ParseColor(string.IsNullOrWhiteSpace(theme.ClockColor) ? null : theme.ClockColor, ThemeService.WithAlpha(fg, 0.7));
        right.Children.Add(new Border { Width = 22, Height = 3, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(clock), Margin = new Thickness(0, 0, 6, 0) });
        right.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(accent) });
        content.Children.Add(left);
        content.Children.Add(right);

        double border = Math.Min(theme.BorderThickness, 1);
        double alpha = Math.Max(theme.Opacity, 0.3);
        Color bc = ThemeService.ParseColor(theme.BorderColor, Colors.Transparent);
        var bar = new Border
        {
            Height = Math.Clamp(theme.BarHeight * 0.42, 13, 22), VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(margin, margin, margin, 0), CornerRadius = new CornerRadius(radius), Child = content,
            Background = noFill ? Brushes.Transparent
                : string.IsNullOrWhiteSpace(theme.BackgroundColor2) ? new SolidColorBrush(ThemeService.WithAlpha(bg, alpha))
                : new LinearGradientBrush(ThemeService.WithAlpha(bg, alpha), ThemeService.WithAlpha(ThemeService.ParseColor(theme.BackgroundColor2, bg), alpha), 0),
            BorderThickness = border <= 0 ? new Thickness(0) : theme.BorderSides switch
            {
                BorderSides.DesktopSide => new Thickness(0, 0, 0, border),
                BorderSides.ScreenSide => new Thickness(0, border, 0, 0),
                _ => new Thickness(border)
            },
            BorderBrush = border <= 0 ? null
                : string.IsNullOrWhiteSpace(theme.BorderColor2) ? new SolidColorBrush(bc)
                : new LinearGradientBrush(bc, ThemeService.ParseColor(theme.BorderColor2, bc), 0)
        };
        if (theme.Shadow && theme.ShadowGlow && theme.Margin > 0)
        {
            Color glow = ThemeService.ParseColor(theme.ShadowColor, Colors.Black);
            bar.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(glow.R, glow.G, glow.B), ShadowDepth = 0, BlurRadius = 10, Opacity = 0.9 };
        }
        desktop.Child = bar;
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

        var arrange = Card();
        Row(arrange, "Arrange items", "Choose where each item sits on the bar.", Btn("Open Layout", () => ShowPage("Layout")));

        var apps = Section("Apps");
        Row(apps, "Pinned apps", null, Toggle(S.ShowPinnedApps, v => S.ShowPinnedApps = v));
        Row(apps, "Running apps", null, Toggle(S.ShowRunningApps, v => S.ShowRunningApps = v));
        Row(apps, "Combine windows of the same app", "One icon per app. Clicking it lists the open windows.", Toggle(S.GroupWindows, v => S.GroupWindows = v));
        Row(apps, "Window previews", "Hover an app to see its windows. Hover a preview to peek at that window.", Toggle(S.ShowWindowPreviews, v => S.ShowWindowPreviews = v));

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
        Row(other, "Volume", "Style it in Widgets.", Toggle(S.ShowVolume, v => S.ShowVolume = v));
        Row(other, "Quick settings button", "Opens Windows quick settings for Wi‑Fi, sound and brightness.", Toggle(S.ShowQuickSettings, v => S.ShowQuickSettings = v));
        Row(other, "Start button", null, Toggle(S.ShowStartButton, v => S.ShowStartButton = v));
        Row(other, "System monitor", "CPU, memory and battery.", Toggle(S.ShowSysInfo, v => S.ShowSysInfo = v));
    }

    // ---- Layout avanzato: l'utente decide in quale zona della barra sta ogni elemento, e in che ordine ----

    private const string LayoutDragFormat = "OpSecBar.LayoutWidget";
    private Border? _dropMarker;
    private int _dropMarkerIndex = -1;

    private static readonly (BarWidget Widget, string Name, string Glyph)[] LayoutWidgets =
    {
        (BarWidget.Apps, "App icons", ""), (BarWidget.Media, "Media player", ""), (BarWidget.Gifs, "GIFs", ""),
        (BarWidget.SysInfo, "System monitor", ""), (BarWidget.Volume, "Volume", ""),
        (BarWidget.Clock, "Clock and calendar", ""), (BarWidget.Tray, "System tray", ""),
        (BarWidget.QuickSettings, "Quick settings", ""), (BarWidget.Start, "Start button", "")
    };

    /// <summary>Vero se l'elemento è acceso (in "Taskbar items"): quelli spenti si possono disporre lo stesso, ma appaiono attenuati.</summary>
    private bool WidgetShown(BarWidget widget) => widget switch
    {
        BarWidget.Apps => S.ShowPinnedApps || S.ShowRunningApps,
        BarWidget.Media => S.ShowMediaPlayer,
        BarWidget.Gifs => S.Gifs.Any(g => g.Enabled),
        BarWidget.SysInfo => S.ShowSysInfo,
        BarWidget.Volume => S.ShowVolume,
        BarWidget.Clock => S.ShowClock,
        BarWidget.Tray => S.ShowTray,
        BarWidget.QuickSettings => S.ShowQuickSettings,
        _ => S.ShowStartButton
    };

    private void BuildLayout()
    {
        PageTitle("Layout");

        var mode = Card();
        Row(mode, "Advanced layout", "Decide where every item sits on the bar. When off, items keep the classic arrangement.",
            Toggle(S.AdvancedLayout, v =>
            {
                // La prima volta si parte dalla disposizione classica: attivarlo non sposta nulla.
                if (v && S.Layout == null) S.Layout = BarLayout.Default(S.GifPlacement);
                S.AdvancedLayout = v;
                Refresh();
            }));

        bool on = S.AdvancedLayout && S.Layout != null;
        BarLayout layout = S.EffectiveLayout;
        // Le zone seguono la barra: sinistra/centro/destra se è orizzontale, alto/mezzo/basso se è verticale.
        bool vertical = S.Position is BarPosition.Left or BarPosition.Right;
        var zones = new[]
        {
            (BarZone.Start, vertical ? "Top" : "Left"), (BarZone.Center, vertical ? "Middle" : "Center"), (BarZone.End, vertical ? "Bottom" : "Right")
        };

        SectionHeader("Arrange items", on
            ? "Drag an item to a new spot, or right-click it for more options. The bar updates as you go."
            : "Turn on Advanced layout to move these items.");
        _dropMarker = new Border
        {
            // Larghezza netta zero (margini negativi): mostrare il segnaposto non sposta gli elementi vicini.
            Width = 2, Height = 26, CornerRadius = new CornerRadius(1), Margin = new Thickness(-5, 0, 3, 0),
            Background = (Brush)FindResource("S.Accent"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center
        };
        _dropMarkerIndex = -1;
        var card = Card();
        foreach (var (zone, label) in zones) LayoutZoneRow(card, layout, zone, label, zones, on);
        PageHost.Children.Add(new TextBlock
        {
            Text = "Dimmed items are turned off. Turn them on in Taskbar items.", FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("S.Text2"), Margin = new Thickness(1, 8, 0, 0)
        });

        if (!on) return;
        var presets = Section("Start from a preset");
        Row(presets, "Classic", "Apps on the left, media player in the middle, system items on the right.",
            Btn("Apply", () => SetLayout(BarLayout.Default(S.GifPlacement))));
        Row(presets, "Centered", "Start button and apps in the middle, like Windows 11.", Btn("Apply", () => SetLayout(BarLayout.Centered())));
    }

    private void SetLayout(BarLayout layout)
    {
        S.Layout = layout;
        _app.SettingsService.Notify(nameof(AppSettings.Layout));
        Refresh();
    }

    /// <summary>Una zona della barra: etichetta + area in cui trascinare gli elementi.</summary>
    private void LayoutZoneRow(Panel card, BarLayout layout, BarZone zone, string label, (BarZone Zone, string Label)[] zones, bool enabled)
    {
        var chips = new WrapPanel { Margin = new Thickness(8, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        foreach (BarWidget widget in layout[zone]) chips.Children.Add(LayoutChip(widget, zone, zones, enabled));
        if (layout[zone].Count == 0)
            chips.Children.Add(new TextBlock
            {
                Text = enabled ? "Nothing here. Drop an item to place it." : "Nothing here.", FontSize = 12,
                Foreground = (Brush)FindResource("S.Text3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 12, 0, 12)
            });

        var drop = new Border
        {
            CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, MinHeight = 48, Child = chips,
            AllowDrop = enabled, Opacity = enabled ? 1 : 0.55
        };
        if (enabled)
        {
            drop.DragOver += (_, e) =>
            {
                e.Handled = true;
                if (!e.Data.GetDataPresent(LayoutDragFormat))
                {
                    e.Effects = DragDropEffects.None;
                    return;
                }
                e.Effects = DragDropEffects.Move;
                drop.Background = (Brush)FindResource("S.SubtleHover");
                ShowDropMarker(chips, LayoutInsertIndex(chips, e.GetPosition(chips)));
            };
            drop.DragLeave += (_, e) =>
            {
                // L'evento arriva anche passando da un elemento all'altro dentro la stessa zona: lì non si fa nulla.
                Point p = e.GetPosition(drop);
                if (p.X > 0 && p.Y > 0 && p.X < drop.ActualWidth && p.Y < drop.ActualHeight) return;
                drop.Background = Brushes.Transparent;
                HideDropMarker();
            };
            drop.Drop += (_, e) =>
            {
                e.Handled = true;
                int index = LayoutInsertIndex(chips, e.GetPosition(chips));
                drop.Background = Brushes.Transparent;
                HideDropMarker();
                if (e.Data.GetData(LayoutDragFormat) is string name && Enum.TryParse(name, out BarWidget widget))
                    Safe(() => MoveLayoutWidget(widget, zone, index));
            };
        }

        var grid = new Grid { Margin = new Thickness(16, 6, 10, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(drop, 1);
        grid.Children.Add(drop);

        if (card.Children.Count > 0) card.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("S.Divider") });
        card.Children.Add(grid);
    }

    /// <summary>Un elemento della barra, da trascinare in un'altra posizione (o da spostare dal menu del tasto destro).</summary>
    private FrameworkElement LayoutChip(BarWidget widget, BarZone zone, (BarZone Zone, string Label)[] zones, bool enabled)
    {
        var info = LayoutWidgets.First(w => w.Widget == widget);
        bool shown = WidgetShown(widget);
        double opacity = shown ? 1 : 0.45;

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = info.Glyph, FontFamily = (FontFamily)FindResource("S.Icons"), FontSize = 14, Margin = new Thickness(0, 0, 8, 0),
            Foreground = (Brush)FindResource("S.Text2"), VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock { Text = info.Name, VerticalAlignment = VerticalAlignment.Center });

        var chip = new Border
        {
            Tag = widget, Child = content, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            Background = (Brush)FindResource("S.Control"), BorderBrush = (Brush)FindResource("S.ControlStroke"),
            Padding = new Thickness(10, 6, 12, 7), Margin = new Thickness(0, 4, 8, 4), Opacity = opacity,
            ToolTip = shown ? null : widget == BarWidget.Gifs ? "No GIFs to show. Add some in GIFs." : "Turned off. Turn it on in Taskbar items."
        };
        if (!enabled) return chip;

        chip.Cursor = Cursors.SizeAll;
        chip.MouseEnter += (_, _) => chip.Background = (Brush)FindResource("S.ControlHover");
        chip.MouseLeave += (_, _) => chip.Background = (Brush)FindResource("S.Control");

        // Trascinamento: parte quando il mouse, premuto sull'elemento, si è mosso di qualche pixel.
        Point? pressed = null;
        chip.MouseLeftButtonDown += (_, e) =>
        {
            pressed = e.GetPosition(chip);
            chip.CaptureMouse();
        };
        chip.MouseLeftButtonUp += (_, _) =>
        {
            pressed = null;
            chip.ReleaseMouseCapture();
        };
        chip.MouseMove += (_, e) =>
        {
            if (pressed is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
            Point now = e.GetPosition(chip);
            if (Math.Abs(now.X - from.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(now.Y - from.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            pressed = null;
            chip.ReleaseMouseCapture();
            chip.Opacity = 0.3;
            try { DragDrop.DoDragDrop(chip, new DataObject(LayoutDragFormat, widget.ToString()), DragDropEffects.Move); }
            catch (Exception ex) { Log.Error("Trascinamento elemento del layout", ex); }
            finally
            {
                chip.Opacity = opacity;
                HideDropMarker();
            }
        };

        chip.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (S.Layout is not { } layout) return;
            List<BarWidget> list = layout[zone];
            int at = list.IndexOf(widget);
            var menu = new ContextMenu { PlacementTarget = chip, Placement = PlacementMode.Bottom };
            foreach (var (other, label) in zones)
                if (other != zone)
                    menu.Items.Add(Ui.MenuEntry($"Move to the {label.ToLowerInvariant()}", "", () => MoveLayoutWidget(widget, other, layout[other].Count)));
            if (list.Count > 1)
            {
                menu.Items.Add(new Separator());
                // L'indice è quello di inserimento prima di togliere l'elemento: "dopo il successivo" è at + 2.
                if (at > 0) menu.Items.Add(Ui.MenuEntry("Move earlier", "", () => MoveLayoutWidget(widget, zone, at - 1)));
                if (at < list.Count - 1) menu.Items.Add(Ui.MenuEntry("Move later", "", () => MoveLayoutWidget(widget, zone, at + 2)));
            }
            menu.IsOpen = true;
        };
        return chip;
    }

    private static List<FrameworkElement> LayoutChips(Panel panel) =>
        panel.Children.OfType<FrameworkElement>().Where(c => c.Tag is BarWidget).ToList();

    /// <summary>Posizione (tra gli elementi della zona) in cui finirebbe ciò che viene rilasciato nel punto indicato.</summary>
    private static int LayoutInsertIndex(Panel panel, Point point)
    {
        int index = 0;
        foreach (FrameworkElement chip in LayoutChips(panel))
        {
            Point origin = chip.TranslatePoint(new Point(0, 0), panel);
            if (point.Y < origin.Y - chip.Margin.Top) return index; // il punto è su una riga precedente
            bool sameRow = point.Y <= origin.Y + chip.ActualHeight + chip.Margin.Bottom;
            if (sameRow && point.X < origin.X + chip.ActualWidth / 2) return index;
            index++;
        }
        return index;
    }

    /// <summary>Mostra la lineetta d'accento nel punto in cui verrà inserito l'elemento trascinato.</summary>
    private void ShowDropMarker(Panel panel, int index)
    {
        if (_dropMarker == null || (ReferenceEquals(_dropMarker.Parent, panel) && _dropMarkerIndex == index)) return;
        HideDropMarker();
        var chips = LayoutChips(panel);
        if (index < chips.Count) panel.Children.Insert(panel.Children.IndexOf(chips[index]), _dropMarker);
        else panel.Children.Add(_dropMarker);
        _dropMarkerIndex = index;
    }

    private void HideDropMarker()
    {
        (_dropMarker?.Parent as Panel)?.Children.Remove(_dropMarker);
        _dropMarkerIndex = -1;
    }

    /// <summary>Sposta un elemento in una zona, alla posizione indicata (contata prima di toglierlo da dov'era).</summary>
    private void MoveLayoutWidget(BarWidget widget, BarZone zone, int index)
    {
        if (S.Layout is not { } layout) return;
        List<BarWidget> target = layout[zone];
        int current = target.IndexOf(widget);
        if (current >= 0 && current < index) index--; // togliendolo, ciò che lo segue scala di un posto
        foreach (BarZone z in Enum.GetValues<BarZone>()) layout[z].Remove(widget);
        target.Insert(Math.Clamp(index, 0, target.Count), widget);
        _app.SettingsService.Notify(nameof(AppSettings.Layout));
        // La pagina si ricostruisce a trascinamento concluso: questo codice può girare dentro DoDragDrop.
        Dispatcher.BeginInvoke(Refresh);
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
        // Solo i veri percorsi di file sono utili da leggere (non gli identificativi interni della shell).
        string? detail = app.TargetPath;
        if (!string.IsNullOrEmpty(detail) && System.IO.Path.IsPathFullyQualified(detail))
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

        SectionHeader("Always show on the taskbar",
            "Icons that are turned off stay in the hidden icons flyout. Tip: drag an icon out of the flyout onto the bar to show it, " +
            "drag it along the bar to reorder, or drag it off the bar to hide it again.");
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

    private List<string> FontNames() =>
        Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

    private void BuildWidgets()
    {
        PageTitle("Widgets");

        var clock = Section("Clock");
        Row(clock, "Show the clock", null, Toggle(S.ShowClock, v => S.ShowClock = v));
        Row(clock, "Time format", "Leave empty for automatic. Examples: HH:mm · h:mm tt · HH:mm:ss · H.mm",
            FormatCtl(S.ClockFormat, "HH:mm", v => S.ClockFormat = v));
        Row(clock, "Color", "Off = same as the bar text.", OptionalColorCtl(T.ClockColor, T.AccentColor, v => Edit(t => t.ClockColor = v)));
        var fonts = FontNames();
        fonts.Insert(0, "Same as the bar");
        string clockFont = string.IsNullOrWhiteSpace(T.ClockFont) ? "Same as the bar" : T.ClockFont.Split(',')[0].Trim();
        if (!fonts.Contains(clockFont)) fonts.Insert(1, clockFont);
        Row(clock, "Font", null, ComboCtl(fonts.Select(f => (f, f)).ToArray(), clockFont,
            v => Edit(t => t.ClockFont = v == "Same as the bar" ? "" : v == "Segoe UI" ? v : v + ", Segoe UI"), 240));
        Row(clock, "Text size", "0 = same as the bar.", SliderCtl(T.ClockFontSize, 0, 28, 0.5, "0.#", v => Edit(t => t.ClockFontSize = v)));
        Row(clock, "Bold", null, Toggle(T.ClockBold, v => Edit(t => t.ClockBold = v)));
        Row(clock, "24-hour clock", null, Toggle(S.Clock24h, v => S.Clock24h = v));
        Row(clock, "Seconds", null, Toggle(S.ShowSeconds, v => S.ShowSeconds = v));

        var date = Section("Calendar (date)");
        Row(date, "Show the date", null, Toggle(S.ShowDate, v => S.ShowDate = v));
        Row(date, "Date format", "Leave empty for automatic. Examples: ddd d MMM · dd/MM/yyyy · dddd · d MMMM",
            FormatCtl(S.DateFormat, "ddd d MMM", v => S.DateFormat = v));
        Row(date, "Color", "Off = dimmed bar text.", OptionalColorCtl(T.DateColor, T.ForegroundColor, v => Edit(t => t.DateColor = v)));
        Row(date, "Show below the time", "Puts time and date on two lines (horizontal bars).", Toggle(S.DateBelowTime, v => S.DateBelowTime = v));

        var volume = Section("Volume");
        Row(volume, "Show volume", "Click for a slider, scroll to change it, right-click to mute.", Toggle(S.ShowVolume, v => S.ShowVolume = v));
        Row(volume, "Show percentage", null, Toggle(S.ShowVolumePercent, v => S.ShowVolumePercent = v));
        Row(volume, "Icon color", "Off = same as the bar text.", OptionalColorCtl(T.VolumeColor, T.AccentColor, v => Edit(t => t.VolumeColor = v)));

        var pill = Section("Widget background");
        Row(pill, "Background", "A rounded pill behind the clock and the volume.",
            OptionalColorCtl(T.WidgetBackground, "#26FFFFFF", v => Edit(t => t.WidgetBackground = v)));
        Row(pill, "Corner radius", null, SliderCtl(T.WidgetRadius, 0, 20, 1, "0 px", v => Edit(t => t.WidgetRadius = v)));
    }

    /// <summary>Casella per un formato data/ora .NET con anteprima dal vivo.</summary>
    private FrameworkElement FormatCtl(string value, string placeholder, Action<string> set)
    {
        var box = new TextBox
        {
            Style = (Style)FindResource("S.TextBox"), Width = 180, Text = value,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13
        };
        var preview = new TextBlock { FontSize = 12, Foreground = (Brush)FindResource("S.Text2"), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        void ShowPreview()
        {
            string f = box.Text.Trim();
            try { preview.Text = "Preview: " + DateTime.Now.ToString(f.Length == 0 ? placeholder : f, CultureInfo.CurrentCulture); }
            catch (FormatException) { preview.Text = "Invalid format"; }
        }
        void Commit()
        {
            string f = box.Text.Trim();
            if (f.Length > 0)
            {
                try { _ = DateTime.Now.ToString(f, CultureInfo.CurrentCulture); }
                catch (FormatException) { return; }
            }
            Safe(() => set(f));
        }
        box.TextChanged += (_, _) => ShowPreview();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        box.LostKeyboardFocus += (_, _) => Commit();
        ShowPreview();
        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(preview);
        return panel;
    }

    private void BuildGifs()
    {
        PageTitle("GIFs");

        // Area di caricamento: trascina qui i file o clicca per sceglierli.
        var dashed = new System.Windows.Shapes.Rectangle
        {
            RadiusX = 8, RadiusY = 8, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 },
            Stroke = (Brush)FindResource("S.StrongStroke")
        };
        var dropText = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 26, 16, 26) };
        dropText.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("S.Icons"), FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource("S.Accent") });
        dropText.Children.Add(new TextBlock { Text = "Drop GIF files here", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 2) });
        dropText.Children.Add(new TextBlock
        {
            Text = "or click to browse. You can also drop a GIF straight onto the taskbar.", FontSize = 12,
            Foreground = (Brush)FindResource("S.Text2"), HorizontalAlignment = HorizontalAlignment.Center
        });
        var zoneGrid = new Grid();
        zoneGrid.Children.Add(dashed);
        zoneGrid.Children.Add(dropText);
        var zone = new Border
        {
            Background = (Brush)FindResource("S.Card"), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 16, 0, 0),
            AllowDrop = true, Cursor = Cursors.Hand, Child = zoneGrid
        };
        zone.MouseEnter += (_, _) => dashed.Stroke = (Brush)FindResource("S.Accent");
        zone.MouseLeave += (_, _) => dashed.Stroke = (Brush)FindResource("S.StrongStroke");
        zone.DragEnter += (_, _) => { dashed.Stroke = (Brush)FindResource("S.Accent"); zone.Background = (Brush)FindResource("S.SubtleHover"); };
        zone.DragLeave += (_, _) => { dashed.Stroke = (Brush)FindResource("S.StrongStroke"); zone.Background = (Brush)FindResource("S.Card"); };
        zone.DragOver += (_, e) =>
        {
            bool ok = e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsGif);
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        zone.Drop += (_, e) =>
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddGifs(files);
        };
        zone.MouseLeftButtonUp += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Choose GIFs", Filter = "Animated GIF (*.gif)|*.gif", Multiselect = true };
            if (dialog.ShowDialog(this) == true) AddGifs(dialog.FileNames);
        };
        PageHost.Children.Add(zone);

        var display = Section("Display");
        if (S.AdvancedLayout)
            Row(display, "Position", "Set by the advanced layout.", Btn("Open Layout", () => ShowPage("Layout")));
        else
            Row(display, "Position", null, ComboCtl(new[]
            {
                (GifPlacement.Left, "Left, before the apps"), (GifPlacement.Center, "Center"), (GifPlacement.Right, "Right, before the clock")
            }, S.GifPlacement, v => S.GifPlacement = v, 220));
        Row(display, "Size", "0 = fit the bar height.", SliderCtl(S.GifSize, 0, 96, 1, "0 px", v => S.GifSize = v));

        SectionHeader("Your GIFs", "Turn a GIF off to keep it here without showing it on the bar.");
        var list = Card();
        if (S.Gifs.Count == 0) list.Children.Add(Empty("No GIFs yet. Drop some in the area above."));
        for (int i = 0; i < S.Gifs.Count; i++)
        {
            int index = i;
            GifItem item = S.Gifs[i];
            var thumb = new Controls.GifImage { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            thumb.Load(GifService.FullPath(item), 36);
            var label = new DockPanel();
            DockPanel.SetDock(thumb, Dock.Left);
            label.Children.Add(thumb);
            label.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetFileNameWithoutExtension(item.File), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Toggle(item.Enabled, v =>
            {
                item.Enabled = v;
                _app.SettingsService.Notify(nameof(AppSettings.Gifs));
            }));
            buttons.Children.Add(Gap(12));
            buttons.Children.Add(IconBtn("", "Move up", () => MoveGif(index, -1), index > 0));
            buttons.Children.Add(IconBtn("", "Move down", () => MoveGif(index, 1), index < S.Gifs.Count - 1));
            buttons.Children.Add(IconBtn("", "Delete", () =>
            {
                S.Gifs.RemoveAt(index);
                GifService.Delete(item);
                GifsChanged();
            }));
            RowCustom(list, label, buttons);
        }
    }

    private static bool IsGif(string path) => string.Equals(System.IO.Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase);

    private void AddGifs(IEnumerable<string> files)
    {
        bool added = false;
        foreach (string file in files.Where(IsGif))
        {
            if (GifService.Import(file) is { } item)
            {
                S.Gifs.Add(item);
                added = true;
            }
        }
        if (added) GifsChanged();
    }

    private void MoveGif(int index, int delta)
    {
        int target = index + delta;
        if (target < 0 || target >= S.Gifs.Count) return;
        (S.Gifs[index], S.Gifs[target]) = (S.Gifs[target], S.Gifs[index]);
        GifsChanged();
    }

    private void GifsChanged()
    {
        _app.SettingsService.Notify(nameof(AppSettings.Gifs));
        Refresh();
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

        var updates = Section("Updates");
        Row(updates, "Install updates automatically", "Checks the latest GitHub release every time OpSec Bar starts.", Toggle(S.AutoUpdate, v => S.AutoUpdate = v));
        Row(updates, "Get pre-release updates", "Updates to the newest version on GitHub, even if it's a pre-release. Turning this off goes back to the latest release.",
            Toggle(S.PreReleaseUpdates, v => S.PreReleaseUpdates = v));
        UpdateService u = _app.Updates;
        string found = u.LeavingPrerelease ? $"Latest release {u.LatestLabel}"
            : u.LatestIsPrerelease ? $"Pre-release {u.LatestLabel}" : $"Version {u.LatestLabel}";
        (string status, string? action, Action? run) = u.State switch
        {
            UpdateState.Checking => ("Checking for updates…", null, null),
            UpdateState.Downloading => ($"Downloading {char.ToLowerInvariant(found[0])}{found[1..]}…", null, null),
            UpdateState.Available => ($"{found} is available.", "Install", (Action)_app.InstallUpdate),
            UpdateState.Ready => ($"{found} is ready to install.", "Restart and install", (Action)_app.InstallUpdate),
            UpdateState.UpToDate => ("You're up to date.", "Check now", (Action)(() => _app.CheckForUpdates(false))),
            UpdateState.Failed => ("Couldn't check for updates. " + u.Error, "Try again", (Action)(() => _app.CheckForUpdates(false))),
            _ => ("Not checked yet.", "Check now", (Action)(() => _app.CheckForUpdates(false)))
        };
        FrameworkElement control = action != null && run != null ? Btn(action, run, u.State is UpdateState.Available or UpdateState.Ready) : Gap(0);
        Row(updates, "OpSec Bar " + u.CurrentLabel, status, control);

        var about = Section("About");
        Row(about, "Version", null, new TextBlock
        {
            Text = u.CurrentLabel, Foreground = (Brush)FindResource("S.Text2")
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
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(box.Text); AddRecentColor(hex); } };
        swatch.Click += (_, _) => OpenColorFlyout(swatch, () => hex, (c, remember) =>
        {
            Commit(c);
            if (remember) AddRecentColor(hex);
        });

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(swatch);
        panel.Children.Add(box);
        return panel;
    }

    /// <summary>Colore opzionale: interruttore + selettore. Spento = stringa vuota (si usa il valore predefinito).</summary>
    private FrameworkElement OptionalColorCtl(string hex, string fallback, Action<string> set)
    {
        bool on = !string.IsNullOrWhiteSpace(hex);
        var picker = ColorCtl(on ? hex : fallback, v => set(v));
        picker.IsEnabled = on;
        picker.Opacity = on ? 1 : 0.4;
        picker.Margin = new Thickness(12, 0, 0, 0);
        var toggle = new CheckBox { Style = (Style)FindResource("S.Toggle"), IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        toggle.Checked += (_, _) => Safe(() => { set(fallback); Refresh(); });
        toggle.Unchecked += (_, _) => Safe(() => { set(""); Refresh(); });
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(toggle);
        panel.Children.Add(picker);
        return panel;
    }

    private static readonly string[] Palette =
    {
        "#FFFFFF", "#C8CCD4", "#8A8F98", "#4B4F57", "#1E1F24", "#000000", "#FF3B5C", "#FF2E97", "#FF7A00", "#FFC233",
        "#F9F871", "#7CFF6B", "#00E676", "#00F0FF", "#3DA9FC", "#2563EB", "#7C5CFF", "#B967FF", "#FFB3C7", "#C3B1E1",
    };

    private void AddRecentColor(string hex)
    {
        var list = S.RecentColors;
        list.RemoveAll(c => string.Equals(c, hex, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, hex);
        if (list.Count > 14) list.RemoveRange(14, list.Count - 14);
        _app.SettingsService.Notify(nameof(AppSettings.RecentColors));
    }

    /// <summary>
    /// Flyout del selettore colore: anteprima, contagocce (prendi un colore dallo schermo), tavolozza, colori recenti,
    /// opacità e la finestra classica di Windows per tutti gli altri colori.
    /// </summary>
    private void OpenColorFlyout(FrameworkElement anchor, Func<string> current, Action<string, bool> apply)
    {
        Brush text2 = (Brush)FindResource("S.Text2");
        Color Now() => ThemeService.ParseColor(current(), Colors.White);

        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade, VerticalOffset = 4
        };
        var root = new StackPanel { Width = 284 };

        // Anteprima grande + valore
        var preview = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(8), BorderBrush = (Brush)FindResource("S.ControlStroke"), BorderThickness = new Thickness(1) };
        var previewHex = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        var previewRgb = new TextBlock { FontSize = 12, Foreground = text2, Margin = new Thickness(0, 2, 0, 0) };
        var previewText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        previewText.Children.Add(previewHex);
        previewText.Children.Add(previewRgb);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(preview);
        head.Children.Add(previewText);
        root.Children.Add(head);

        Slider? alpha = null;
        TextBlock? alphaLabel = null;
        bool syncing = false;
        void ShowCurrent()
        {
            Color c = Now();
            preview.Background = new SolidColorBrush(c);
            previewHex.Text = ThemeService.ToHex(c);
            previewRgb.Text = $"RGB {c.R}, {c.G}, {c.B}" + (c.A < 255 ? $" · {Math.Round(c.A / 2.55)}%" : "");
            if (alpha != null)
            {
                syncing = true;
                alpha.Value = Math.Round(c.A / 2.55);
                syncing = false;
            }
        }

        // Applica un colore RGB mantenendo l'opacità attuale.
        void PickRgb(Color c, bool remember)
        {
            Color now = Now();
            apply(ThemeService.ToHex(Color.FromArgb(now.A, c.R, c.G, c.B)), remember);
            ShowCurrent();
        }

        FrameworkElement Swatch(string hex, Action click)
        {
            Color c = ThemeService.ParseColor(hex, Colors.Transparent);
            // Scacchiera sotto ai colori semitrasparenti
            var checker = new DrawingBrush
            {
                TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute,
                Drawing = new GeometryDrawing(Brushes.LightGray, null, new GeometryGroup
                {
                    Children = { new RectangleGeometry(new Rect(0, 0, 4, 4)), new RectangleGeometry(new Rect(4, 4, 4, 4)) }
                })
            };
            var cell = new Grid { Width = 22, Height = 22 };
            if (c.A < 255) cell.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = checker });
            cell.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(c),
                BorderBrush = (Brush)FindResource("S.ControlStroke"), BorderThickness = new Thickness(1)
            });
            var b = new Button
            {
                Style = (Style)FindResource("S.IconButton"), Width = 26, Height = 26, Margin = new Thickness(0, 0, 2, 2),
                Content = cell, ToolTip = hex, Cursor = Cursors.Hand
            };
            b.Click += (_, _) => Safe(click);
            return b;
        }

        TextBlock Label(string t) => new() { Text = t, FontSize = 12, Foreground = text2, Margin = new Thickness(0, 4, 0, 6) };

        // Pulsanti principali: contagocce e finestra classica
        var tools = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 10) };
        var dropper = new Button { Style = (Style)FindResource("S.Button"), Margin = new Thickness(0, 0, 4, 0), ToolTip = "Pick a color anywhere on the screen" };
        dropper.Content = GlyphText("", "Eyedropper");
        dropper.Click += async (_, _) =>
        {
            popup.IsOpen = false;
            // Si aspetta che il flyout sparisca, così non finisce nella fotografia dello schermo.
            await Task.Delay(180);
            Color? picked = await Eyedropper.PickAsync();
            Activate();
            if (picked is { } p) PickRgb(p, true);
        };
        var more = new Button { Style = (Style)FindResource("S.Button"), Margin = new Thickness(4, 0, 0, 0) };
        more.Content = GlyphText("", "More colors…");
        more.Click += (_, _) =>
        {
            popup.IsOpen = false;
            Color now = Now();
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true, AnyColor = true, Color = System.Drawing.Color.FromArgb(now.R, now.G, now.B),
                CustomColors = S.RecentColors.Take(16).Select(h => ThemeService.ParseColor(h, Colors.White)).Select(c => c.R | c.G << 8 | c.B << 16).ToArray()
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            // La finestra dei colori non gestisce la trasparenza: si conserva quella attuale.
            PickRgb(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B), true);
        };
        tools.Children.Add(dropper);
        tools.Children.Add(more);
        root.Children.Add(tools);

        root.Children.Add(Label("Palette"));
        var palette = new WrapPanel();
        foreach (string hex in Palette) palette.Children.Add(Swatch(hex, () => PickRgb(ThemeService.ParseColor(hex, Colors.White), true)));
        root.Children.Add(palette);

        root.Children.Add(Label("Recent colors"));
        var recent = new WrapPanel();
        if (S.RecentColors.Count == 0)
            recent.Children.Add(new TextBlock { Text = "Colors you pick will appear here.", FontSize = 12, Foreground = (Brush)FindResource("S.Text3"), Margin = new Thickness(0, 0, 0, 4) });
        foreach (string hex in S.RecentColors.ToList())
            recent.Children.Add(Swatch(hex, () =>
            {
                // I recenti si applicano esattamente come salvati (opacità compresa).
                apply(hex, true);
                ShowCurrent();
            }));
        root.Children.Add(recent);

        root.Children.Add(Label("Opacity"));
        var alphaRow = new DockPanel();
        alphaLabel = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, Foreground = text2, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(alphaLabel, Dock.Right);
        alpha = new Slider
        {
            Style = (Style)FindResource("S.Slider"), Minimum = 0, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true,
            SmallChange = 1, LargeChange = 10
        };
        alpha.ValueChanged += (_, e) =>
        {
            alphaLabel.Text = e.NewValue.ToString("0", CultureInfo.InvariantCulture) + "%";
            if (syncing) return;
            Color c = Now();
            apply(ThemeService.ToHex(Color.FromArgb((byte)Math.Round(e.NewValue * 2.55), c.R, c.G, c.B)), false);
            ShowCurrent();
        };
        alphaRow.Children.Add(alphaLabel);
        alphaRow.Children.Add(alpha);
        root.Children.Add(alphaRow);

        ShowCurrent();
        alphaLabel.Text = alpha.Value.ToString("0", CultureInfo.InvariantCulture) + "%";

        popup.Child = new Border
        {
            Background = (Brush)FindResource("S.Flyout"), BorderBrush = (Brush)FindResource("S.ControlStroke"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(8, 0, 8, 12),
            Child = root,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 6, Direction = 270, Opacity = 0.28 }
        };
        TextElement.SetForeground(popup.Child, (Brush)FindResource("S.Text"));
        TextElement.SetFontFamily(popup.Child, FontFamily);
        TextElement.SetFontSize(popup.Child, 14);
        // Chiudendo il flyout, l'ultimo colore regolato con l'opacità finisce tra i recenti.
        string opened = current();
        popup.Closed += (_, _) => { if (!string.Equals(opened, current(), StringComparison.OrdinalIgnoreCase)) AddRecentColor(current()); };
        popup.IsOpen = true;
    }

    private FrameworkElement GlyphText(string glyph, string text)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("S.Icons"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        p.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return p;
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
