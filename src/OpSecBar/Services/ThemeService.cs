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
