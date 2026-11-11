using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using OpSecBar.Core;
using OpSecBar.Models;

namespace OpSecBar.Services;

/// <summary>Temi predefiniti, temi dell'utente (salva/importa/esporta) e risorse dinamiche condivise dalla UI.</summary>
public static class ThemeService
{
    public const string CustomName = "Custom";

    public static IReadOnlyList<ThemeData> Presets { get; } = new List<ThemeData>
    {
        new()
        {
            Name = "Windows 11 Minimal", Backdrop = BackdropMode.Acrylic, BackgroundColor = "#1C1C1F", Opacity = 0.70,
            ForegroundColor = "#FFFFFF", AccentColor = "#60CDFF", PanelColor = "#202024", PanelOpacity = 0.92,
            CornerRadius = 0, BarHeight = 40, Margin = 0, IconSize = 22, IconSpacing = 4, FontSize = 12.5
        },
        new()
        {
            Name = "Glassmorphism", Backdrop = BackdropMode.Glass, BackgroundColor = "#FFFFFF", Opacity = 0.16,
            ForegroundColor = "#FFFFFF", AccentColor = "#9BE7FF", PanelColor = "#222838", PanelOpacity = 0.78,
            CornerRadius = 16, BarHeight = 42, Margin = 8, IconSize = 22, IconSpacing = 6, FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#59FFFFFF", Shadow = true
        },
        new()
        {
            Name = "macOS Inspired", Backdrop = BackdropMode.Blur, BackgroundColor = "#26262B", Opacity = 0.45,
            ForegroundColor = "#FFFFFF", AccentColor = "#0A84FF", PanelColor = "#2A2A2E", PanelOpacity = 0.86,
            CornerRadius = 0, BarHeight = 30, Margin = 0, IconSize = 18, IconSpacing = 3,
            FontFamily = "Segoe UI Variable Text, Segoe UI", FontSize = 12.5
        },
        new()
        {
            Name = "Transparent", Backdrop = BackdropMode.Transparent, BackgroundColor = "#000000", Opacity = 0,
            ForegroundColor = "#FFFFFF", AccentColor = "#FFFFFF", PanelColor = "#16171B", PanelOpacity = 0.9,
            CornerRadius = 0, BarHeight = 40, Margin = 0, IconSize = 22, IconSpacing = 4, FontSize = 12.5
        },
        new()
        {
            Name = "Dark", Backdrop = BackdropMode.Solid, BackgroundColor = "#121316", Opacity = 1,
            ForegroundColor = "#F2F3F5", AccentColor = "#8B7CFF", PanelColor = "#17181C", PanelOpacity = 1,
            CornerRadius = 0, BarHeight = 40, Margin = 0, IconSize = 22, IconSpacing = 4, FontSize = 12.5,
            BorderThickness = 0, BorderColor = "#26FFFFFF"
        },
        new()
        {
            Name = "Light", Backdrop = BackdropMode.Solid, BackgroundColor = "#F4F5F7", Opacity = 1,
            ForegroundColor = "#17181C", AccentColor = "#2563EB", PanelColor = "#FBFBFC", PanelOpacity = 1,
            CornerRadius = 0, BarHeight = 40, Margin = 0, IconSize = 22, IconSpacing = 4, FontSize = 12.5,
            BorderThickness = 0, BorderColor = "#22000000"
        },
        new()
        {
            // Stile "barra da tiling window manager": fondo scuro violaceo, cornice verde, accento viola, font monospazio.
            Name = "Terminal", Backdrop = BackdropMode.Solid, BackgroundColor = "#1C1B26", Opacity = 1,
            ForegroundColor = "#D9D6E6", AccentColor = "#9D7CF0", PanelColor = "#1C1B26", PanelOpacity = 1,
            CornerRadius = 5, BarHeight = 40, Margin = 6, IconSize = 20, IconSpacing = 8,
            FontFamily = "Cascadia Mono, Consolas", FontSize = 13,
            BorderThickness = 2, BorderColor = "#5ED162"
        },

        // ---- Temi "di stile" -------------------------------------------------------------------
        new()
        {
            // Neon magenta/ciano su nero, bordo sfumato e alone luminoso.
            Name = "Cyberpunk", Backdrop = BackdropMode.Solid, BackgroundColor = "#0B0716", BackgroundColor2 = "#1A0B2E", Opacity = 0.96,
            ForegroundColor = "#E8F9FF", AccentColor = "#FF2E97", PanelColor = "#120A22", PanelOpacity = 0.97,
            CornerRadius = 4, BarHeight = 40, Margin = 8, IconSize = 21, IconSpacing = 6,
            FontFamily = "Bahnschrift, Segoe UI", FontSize = 13,
            BorderThickness = 1.5, BorderColor = "#FF2E97", BorderColor2 = "#00F0FF",
            Shadow = true, ShadowGlow = true, ShadowColor = "#FF2E97", ShadowSize = 18,
            ClockColor = "#00F0FF", ClockFont = "Cascadia Mono, Consolas", ClockBold = true, DateColor = "#FF2E97",
            VolumeColor = "#F9F871", WidgetBackground = "#2600F0FF", WidgetRadius = 3
        },
        new()
        {
            // Toni caldi e polverosi da "lofi beats to relax": crema, pesca e malva.
            Name = "Lofi", Backdrop = BackdropMode.Acrylic, BackgroundColor = "#2B2433", Opacity = 0.78,
            ForegroundColor = "#F3E6D8", AccentColor = "#F2A97E", PanelColor = "#2E2736", PanelOpacity = 0.92,
            CornerRadius = 14, BarHeight = 40, Margin = 8, IconSize = 21, IconSpacing = 6,
            FontFamily = "Segoe UI Variable Display, Segoe UI", FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#33F3E6D8", Shadow = true, ShadowColor = "#000000", ShadowSize = 18,
            ClockColor = "#F2A97E", ClockBold = false, ClockFontSize = 14, DateColor = "#C9B6D9", VolumeColor = "#C9B6D9",
            WidgetBackground = "#1FF3E6D8", WidgetRadius = 10
        },
        new()
        {
            // Chiaro, pastello e arrotondato.
            Name = "Soft", Backdrop = BackdropMode.Acrylic, BackgroundColor = "#FFF7FA", Opacity = 0.80,
            ForegroundColor = "#4A4458", AccentColor = "#E58FB0", PanelColor = "#FFF7FA", PanelOpacity = 0.95,
            CornerRadius = 18, BarHeight = 42, Margin = 10, IconSize = 21, IconSpacing = 6,
            FontFamily = "Segoe UI Variable Display, Segoe UI", FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#22E58FB0", Shadow = true, ShadowColor = "#55B98BA6", ShadowSize = 20,
            ClockColor = "#8E7CC3", ClockBold = true, DateColor = "#B07A97", VolumeColor = "#8E7CC3",
            WidgetBackground = "#14E58FB0", WidgetRadius = 12
        },
        new()
        {
            // Nero carbone con accento rosso "RGB" e font squadrato.
            Name = "Gaming", Backdrop = BackdropMode.Solid, BackgroundColor = "#0D0E12", BackgroundColor2 = "#16181F", Opacity = 1,
            ForegroundColor = "#F2F4F8", AccentColor = "#FF1F3D", PanelColor = "#121318", PanelOpacity = 1,
            CornerRadius = 0, BarHeight = 42, Margin = 0, IconSize = 22, IconSpacing = 6,
            FontFamily = "Bahnschrift, Segoe UI", FontSize = 13,
            BorderThickness = 2, BorderColor = "#FF1F3D", BorderColor2 = "#7C3AED", BorderSides = BorderSides.DesktopSide,
            ClockColor = "#FFFFFF", ClockFont = "Bahnschrift, Segoe UI", ClockFontSize = 15, ClockBold = true, DateColor = "#FF1F3D",
            VolumeColor = "#FF1F3D", WidgetBackground = "#1AFF1F3D", WidgetRadius = 2
        },
        new()
        {
            // Tramonto anni '80: sfondo viola-blu sfumato, rosa e arancio.
            Name = "Synthwave", Backdrop = BackdropMode.Solid, BackgroundColor = "#2A0E45", BackgroundColor2 = "#0F1A4A", Opacity = 0.97,
            ForegroundColor = "#FFE9F7", AccentColor = "#FF6EC7", PanelColor = "#1E0B35", PanelOpacity = 0.97,
            CornerRadius = 10, BarHeight = 40, Margin = 8, IconSize = 21, IconSpacing = 6,
            FontFamily = "Bahnschrift, Segoe UI", FontSize = 13,
            BorderThickness = 1.5, BorderColor = "#FF8A3D", BorderColor2 = "#FF3DC8",
            Shadow = true, ShadowGlow = true, ShadowColor = "#FF3DC8", ShadowSize = 20,
            ClockColor = "#FFB86B", ClockBold = true, DateColor = "#FF6EC7", VolumeColor = "#7DF9FF", WidgetRadius = 8
        },
        new()
        {
            Name = "Vaporwave", Backdrop = BackdropMode.Glass, BackgroundColor = "#FF71CE", Opacity = 0.30,
            ForegroundColor = "#FFFFFF", AccentColor = "#01CDFE", PanelColor = "#3A2C5C", PanelOpacity = 0.9,
            CornerRadius = 16, BarHeight = 42, Margin = 8, IconSize = 22, IconSpacing = 6, FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#B967FF", BorderColor2 = "#05FFA1", Shadow = true, ShadowColor = "#66B967FF", ShadowSize = 18,
            ClockColor = "#FFFB96", DateColor = "#05FFA1", VolumeColor = "#01CDFE", WidgetBackground = "#26FFFFFF", WidgetRadius = 12
        },
        new()
        {
            // Pioggia di codice verde.
            Name = "Matrix", Backdrop = BackdropMode.Solid, BackgroundColor = "#020803", Opacity = 0.95,
            ForegroundColor = "#B6FFC4", AccentColor = "#00FF41", PanelColor = "#030D05", PanelOpacity = 1,
            CornerRadius = 0, BarHeight = 36, Margin = 0, IconSize = 20, IconSpacing = 6,
            FontFamily = "Cascadia Mono, Consolas", FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#00FF41", BorderSides = BorderSides.DesktopSide,
            ClockColor = "#00FF41", ClockFont = "Cascadia Mono, Consolas", ClockBold = true, DateColor = "#3DBE5A", VolumeColor = "#00FF41"
        },
        new()
        {
            Name = "Nord", Backdrop = BackdropMode.Acrylic, BackgroundColor = "#2E3440", Opacity = 0.85,
            ForegroundColor = "#ECEFF4", AccentColor = "#88C0D0", PanelColor = "#3B4252", PanelOpacity = 0.95,
            CornerRadius = 8, BarHeight = 40, Margin = 6, IconSize = 21, IconSpacing = 5, FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#4C566A",
            ClockColor = "#88C0D0", DateColor = "#81A1C1", VolumeColor = "#A3BE8C", WidgetBackground = "#3B4252", WidgetRadius = 6
        },
        new()
        {
            Name = "Dracula", Backdrop = BackdropMode.Solid, BackgroundColor = "#282A36", Opacity = 0.97,
            ForegroundColor = "#F8F8F2", AccentColor = "#BD93F9", PanelColor = "#282A36", PanelOpacity = 1,
            CornerRadius = 8, BarHeight = 40, Margin = 6, IconSize = 21, IconSpacing = 5,
            FontFamily = "Cascadia Code, Consolas", FontSize = 12.5,
            BorderThickness = 1.5, BorderColor = "#BD93F9", BorderColor2 = "#FF79C6",
            ClockColor = "#50FA7B", DateColor = "#FF79C6", VolumeColor = "#8BE9FD", WidgetBackground = "#44475A", WidgetRadius = 6
        },
        new()
        {
            Name = "Sakura", Backdrop = BackdropMode.Acrylic, BackgroundColor = "#3A2430", Opacity = 0.70,
            ForegroundColor = "#FFEAF1", AccentColor = "#FFB7C5", PanelColor = "#3A2430", PanelOpacity = 0.93,
            CornerRadius = 16, BarHeight = 40, Margin = 8, IconSize = 21, IconSpacing = 6,
            FontFamily = "Segoe UI Variable Display, Segoe UI", FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#55FFB7C5", Shadow = true, ShadowColor = "#66FF8FAB", ShadowSize = 16, ShadowGlow = true,
            ClockColor = "#FFD1DC", DateColor = "#FFB7C5", VolumeColor = "#FFD1DC", WidgetRadius = 10
        },
        new()
        {
            Name = "Ocean", Backdrop = BackdropMode.Blur, BackgroundColor = "#06283D", BackgroundColor2 = "#0B4F6C", Opacity = 0.70,
            ForegroundColor = "#E6FBFF", AccentColor = "#47B5FF", PanelColor = "#0A2A40", PanelOpacity = 0.93,
            CornerRadius = 12, BarHeight = 40, Margin = 6, IconSize = 21, IconSpacing = 5, FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#3347B5FF",
            ClockColor = "#9FE2FF", DateColor = "#47B5FF", VolumeColor = "#7FFFD4", WidgetRadius = 8
        },
        new()
        {
            Name = "Forest", Backdrop = BackdropMode.Solid, BackgroundColor = "#1B2A1F", BackgroundColor2 = "#243B2A", Opacity = 0.97,
            ForegroundColor = "#E8F1E4", AccentColor = "#8BC34A", PanelColor = "#1E2D22", PanelOpacity = 1,
            CornerRadius = 10, BarHeight = 40, Margin = 6, IconSize = 21, IconSpacing = 5, FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#3D5A44",
            ClockColor = "#C5E1A5", DateColor = "#A1887F", VolumeColor = "#8BC34A", WidgetBackground = "#26324A36", WidgetRadius = 8
        },
        new()
        {
            Name = "Sunset", Backdrop = BackdropMode.Solid, BackgroundColor = "#3D1E3F", BackgroundColor2 = "#8C3B2E", Opacity = 0.95,
            ForegroundColor = "#FFF1E6", AccentColor = "#FFB347", PanelColor = "#3A1E33", PanelOpacity = 0.97,
            CornerRadius = 14, BarHeight = 40, Margin = 8, IconSize = 21, IconSpacing = 6, FontSize = 12.5,
            Shadow = true, ShadowColor = "#000000", ShadowSize = 18,
            ClockColor = "#FFD39B", DateColor = "#FF9A76", VolumeColor = "#FFD39B", WidgetBackground = "#1FFFFFFF", WidgetRadius = 10
        },
        new()
        {
            // Monitor a fosfori ambra.
            Name = "Retro Amber", Backdrop = BackdropMode.Solid, BackgroundColor = "#140C02", Opacity = 1,
            ForegroundColor = "#FFB000", AccentColor = "#FFCC00", PanelColor = "#1A1003", PanelOpacity = 1,
            CornerRadius = 0, BarHeight = 34, Margin = 0, IconSize = 19, IconSpacing = 6,
            FontFamily = "Cascadia Mono, Consolas", FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#996A00", BorderSides = BorderSides.DesktopSide,
            ClockColor = "#FFCC00", ClockFont = "Cascadia Mono, Consolas", DateColor = "#CC8C00", VolumeColor = "#FFB000"
        },
        new()
        {
            Name = "Coffee", Backdrop = BackdropMode.Solid, BackgroundColor = "#2B1E16", Opacity = 0.97,
            ForegroundColor = "#F1E3D3", AccentColor = "#D4A373", PanelColor = "#2E2119", PanelOpacity = 1,
            CornerRadius = 10, BarHeight = 40, Margin = 6, IconSize = 21, IconSpacing = 5,
            FontFamily = "Georgia, Segoe UI", FontSize = 12.5, BorderThickness = 1, BorderColor = "#4A3729",
            ClockColor = "#E9C46A", ClockFont = "Georgia, Segoe UI", ClockBold = false, ClockFontSize = 14, DateColor = "#D4A373",
            VolumeColor = "#D4A373", WidgetBackground = "#3A2A20", WidgetRadius = 8
        },
        new()
        {
            Name = "Monochrome", Backdrop = BackdropMode.Solid, BackgroundColor = "#000000", Opacity = 1,
            ForegroundColor = "#FFFFFF", AccentColor = "#FFFFFF", PanelColor = "#0A0A0A", PanelOpacity = 1,
            CornerRadius = 0, BarHeight = 36, Margin = 0, IconSize = 20, IconSpacing = 6, FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#FFFFFF", BorderSides = BorderSides.DesktopSide,
            ClockBold = true, DateColor = "#9A9A9A"
        },
        new()
        {
            // Pillole flottanti chiare e ariose.
            Name = "Pastel Pop", Backdrop = BackdropMode.Solid, BackgroundColor = "#FDF6FF", BackgroundColor2 = "#EAF6FF", Opacity = 0.98,
            ForegroundColor = "#3B3355", AccentColor = "#7AA2F7", PanelColor = "#FBF7FF", PanelOpacity = 1,
            CornerRadius = 22, BarHeight = 42, Margin = 10, IconSize = 21, IconSpacing = 6,
            FontFamily = "Segoe UI Variable Display, Segoe UI", FontSize = 12.5,
            BorderThickness = 1.5, BorderColor = "#FFB3D9", BorderColor2 = "#A0E7E5", Shadow = true, ShadowColor = "#447AA2F7", ShadowSize = 18,
            ClockColor = "#7AA2F7", DateColor = "#E879A6", VolumeColor = "#5CC8A8", WidgetBackground = "#147AA2F7", WidgetRadius = 14
        },
        new()
        {
            // Ultra-luminoso per setup RGB: verde lime e viola.
            Name = "Neon Arcade", Backdrop = BackdropMode.Solid, BackgroundColor = "#07070D", Opacity = 0.97,
            ForegroundColor = "#F5F5FF", AccentColor = "#39FF14", PanelColor = "#0C0C16", PanelOpacity = 1,
            CornerRadius = 12, BarHeight = 42, Margin = 8, IconSize = 22, IconSpacing = 6,
            FontFamily = "Bahnschrift, Segoe UI", FontSize = 13,
            BorderThickness = 2, BorderColor = "#39FF14", BorderColor2 = "#BC13FE",
            Shadow = true, ShadowGlow = true, ShadowColor = "#BC13FE", ShadowSize = 22,
            ClockColor = "#39FF14", ClockFont = "Bahnschrift, Segoe UI", ClockFontSize = 15, DateColor = "#BC13FE",
            VolumeColor = "#FFE600", WidgetBackground = "#1F39FF14", WidgetRadius = 6
        },
    };

    // ---- Colori ---------------------------------------------------------------------------------

    public static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex.Trim()) is Color c) return c;
        }
        catch { }
        return fallback;
    }

    public static string ToHex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    private static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>Pubblica nelle risorse dell'applicazione i valori usati da barra, menu, flyout e menu Start.</summary>
    public static void ApplyResources(ThemeData t)
    {
        var res = Application.Current.Resources;
        Color fg = ParseColor(t.ForegroundColor, Colors.White);
        Color accent = ParseColor(t.AccentColor, Color.FromRgb(0x7C, 0x9C, 0xFF));
        Color panel = ParseColor(t.PanelColor, Color.FromRgb(0x1B, 0x1C, 0x22));
        // I pannelli restano sempre leggibili: mai meno del 55% di opacità.
        double panelAlpha = Math.Clamp(t.PanelOpacity, 0.55, 1);
        // Senza sfocatura dietro, un pannello troppo trasparente renderebbe il testo difficile da leggere.
        if (!BackdropHelper.BlurActive(t.Backdrop)) panelAlpha = Math.Max(panelAlpha, 0.95);
        bool panelIsDark = (0.299 * panel.R + 0.587 * panel.G + 0.114 * panel.B) < 140;
        Color panelFg = panelIsDark ? Color.FromRgb(0xF4, 0xF5, 0xF7) : Color.FromRgb(0x17, 0x18, 0x1C);

        res["FgBrush"] = Brush(fg);
        res["FgDimBrush"] = Brush(WithAlpha(fg, 0.62));
        res["HoverBrush"] = Brush(WithAlpha(fg, 0.12));
        res["PressedBrush"] = Brush(WithAlpha(fg, 0.20));
        res["AccentBrush"] = Brush(accent);
        res["AccentDimBrush"] = Brush(WithAlpha(accent, 0.28));

        res["PanelBrush"] = Brush(WithAlpha(panel, panelAlpha));
        res["PanelSolidBrush"] = Brush(panel);
        res["PanelFgBrush"] = Brush(panelFg);
        res["PanelFgDimBrush"] = Brush(WithAlpha(panelFg, 0.6));
        res["PanelHoverBrush"] = Brush(WithAlpha(panelFg, 0.09));
        res["PanelPressedBrush"] = Brush(WithAlpha(panelFg, 0.16));
        res["PanelBorderBrush"] = Brush(WithAlpha(panelFg, 0.12));

        FontFamily font;
        try { font = new FontFamily(string.IsNullOrWhiteSpace(t.FontFamily) ? "Segoe UI" : t.FontFamily); }
        catch { font = new FontFamily("Segoe UI"); }
        res["BarFont"] = font;
        res["BarFontSize"] = Math.Clamp(t.FontSize, 8, 24);
        res["BarIconSize"] = Math.Clamp(t.IconSize, 12, 64);
        res["BarIconSpacing"] = new Thickness(Math.Clamp(t.IconSpacing, 0, 24) / 2);
    }

    // ---- Temi dell'utente ---------------------------------------------------------------------

    public static List<ThemeData> LoadUserThemes()
    {
        var list = new List<ThemeData>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(AppPaths.ThemesDir, "*.json"))
            {
                var t = ReadThemeFile(file);
                if (t != null) list.Add(t);
            }
        }
        catch (Exception ex) { Log.Error("Lettura temi utente fallita", ex); }
        return list.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static ThemeData? ReadThemeFile(string path)
    {
        try
        {
            var t = JsonSerializer.Deserialize<ThemeData>(File.ReadAllText(path), JsonOptions.Default);
            if (t == null) return null;
            if (string.IsNullOrWhiteSpace(t.Name)) t.Name = Path.GetFileNameWithoutExtension(path);
            return t;
        }
        catch (Exception ex)
        {
            Log.Error($"Tema non valido: {path}", ex);
            return null;
        }
    }

    public static void WriteThemeFile(ThemeData theme, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(theme, JsonOptions.Default));

    public static string UserThemePath(string name)
    {
        string safe = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
        if (safe.Length == 0) safe = "tema";
        return Path.Combine(AppPaths.ThemesDir, safe + ".json");
    }

    public static void SaveUserTheme(ThemeData theme) => WriteThemeFile(theme, UserThemePath(theme.Name));

    public static void DeleteUserTheme(string name)
    {
        try { File.Delete(UserThemePath(name)); } catch (Exception ex) { Log.Error("Eliminazione tema fallita", ex); }
    }

    public static bool IsPreset(string name) => Presets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
