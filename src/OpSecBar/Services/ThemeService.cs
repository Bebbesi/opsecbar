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

        // ---- Temi "da terminale" ---------------------------------------------------------------
        // Palette dei temi più amati da terminale ed editor, con barre in stile i3/polybar/waybar.
        new()
        {
            // Polybar classica: barra piatta, linea sotto, ora tra parentesi quadre.
            Name = "Gruvbox", Backdrop = BackdropMode.Solid, BackgroundColor = "#282828", Opacity = 1,
            ForegroundColor = "#EBDBB2", AccentColor = "#FE8019", PanelColor = "#32302F", PanelOpacity = 1,
            CornerRadius = 0, BarHeight = 32, Margin = 0, IconSize = 18, IconSpacing = 6,
            FontFamily = "Cascadia Mono, Consolas", FontSize = 12.5,
            BorderThickness = 2, BorderColor = "#FE8019", BorderSides = BorderSides.DesktopSide,
            ClockColor = "#B8BB26", ClockFont = "Cascadia Mono, Consolas", ClockPrefix = "[ ", ClockSuffix = " ]",
            DateColor = "#FABD2F", VolumeColor = "#83A598", IconColor = "#EBDBB2", WidgetBackground = "#3C3836", WidgetRadius = 0
        },
        new()
        {
            // Isola flottante in stile waybar, con una cometa che gira sul bordo.
            Name = "Tokyo Night", Backdrop = BackdropMode.Solid, BackgroundColor = "#1A1B26", BackgroundColor2 = "#24283B", Opacity = 0.97,
            ForegroundColor = "#C0CAF5", AccentColor = "#7AA2F7", PanelColor = "#1F2335", PanelOpacity = 1,
            CornerRadius = 12, BarHeight = 38, Margin = 8, IconSize = 20, IconSpacing = 6,
            FontFamily = "Cascadia Code, Consolas", FontSize = 12.5,
            BorderThickness = 1.5, BorderColor = "#3B4261", Shadow = true, ShadowColor = "#000000", ShadowSize = 16,
            ClockColor = "#7DCFFF", ClockFont = "Cascadia Code, Consolas", DateColor = "#BB9AF7", VolumeColor = "#9ECE6A",
            WidgetBackground = "#292E42", WidgetRadius = 8, IconColor = "#7AA2F7",
            BorderFx = BorderFx.Comet, BorderFxColor = "#7DCFFF"
        },
        new()
        {
            Name = "Catppuccin Mocha", Backdrop = BackdropMode.Acrylic, BackgroundColor = "#1E1E2E", Opacity = 0.88,
            ForegroundColor = "#CDD6F4", AccentColor = "#CBA6F7", PanelColor = "#181825", PanelOpacity = 0.96,
            CornerRadius = 16, BarHeight = 40, Margin = 8, IconSize = 21, IconSpacing = 6,
            FontFamily = "Cascadia Code, Consolas", FontSize = 12.5,
            BorderThickness = 1.5, BorderColor = "#CBA6F7", BorderColor2 = "#F5C2E7", Shadow = true, ShadowGlow = true,
            ShadowColor = "#66CBA6F7", ShadowSize = 16,
            ClockColor = "#F5C2E7", DateColor = "#89B4FA", VolumeColor = "#A6E3A1", WidgetBackground = "#313244", WidgetRadius = 10,
            IconColor = "#CBA6F7", BorderFx = BorderFx.Pulse, BorderFxColor = "#F5C2E7", BorderFxSpeed = 0.6
        },
        new()
        {
            // Icone monocromatiche: tutta la barra parla la stessa palette.
            Name = "Solarized Dark", Backdrop = BackdropMode.Solid, BackgroundColor = "#002B36", Opacity = 1,
            ForegroundColor = "#93A1A1", AccentColor = "#268BD2", PanelColor = "#073642", PanelOpacity = 1,
            CornerRadius = 0, BarHeight = 34, Margin = 0, IconSize = 18, IconSpacing = 8,
            FontFamily = "Cascadia Mono, Consolas", FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#586E75", BorderSides = BorderSides.DesktopSide,
            ClockColor = "#B58900", ClockFont = "Cascadia Mono, Consolas", ClockPrefix = "~ ", DateColor = "#2AA198", VolumeColor = "#859900",
            IconColor = "#93A1A1", IconStyle = IconStyle.Monochrome
        },
        new()
        {
            Name = "Monokai", Backdrop = BackdropMode.Solid, BackgroundColor = "#272822", Opacity = 1,
            ForegroundColor = "#F8F8F2", AccentColor = "#A6E22E", PanelColor = "#2D2E27", PanelOpacity = 1,
            CornerRadius = 4, BarHeight = 36, Margin = 4, IconSize = 19, IconSpacing = 6,
            FontFamily = "Cascadia Mono, Consolas", FontSize = 12.5,
            BorderThickness = 1.5, BorderColor = "#49483E",
            ClockColor = "#E6DB74", ClockFont = "Cascadia Mono, Consolas", ClockPrefix = "» ", DateColor = "#66D9EF", VolumeColor = "#FD971F",
            IconColor = "#F92672", BorderFx = BorderFx.MarchingAnts, BorderFxColor = "#F92672", BorderFxSpeed = 0.7
        },
        new()
        {
            Name = "One Dark", Backdrop = BackdropMode.Solid, BackgroundColor = "#282C34", Opacity = 0.98,
            ForegroundColor = "#ABB2BF", AccentColor = "#61AFEF", PanelColor = "#21252B", PanelOpacity = 1,
            CornerRadius = 8, BarHeight = 38, Margin = 6, IconSize = 20, IconSpacing = 6,
            FontFamily = "Cascadia Code, Consolas", FontSize = 12.5, BorderThickness = 1, BorderColor = "#3E4451",
            ClockColor = "#98C379", DateColor = "#C678DD", VolumeColor = "#E5C07B", WidgetBackground = "#2C313A", WidgetRadius = 6,
            IconColor = "#61AFEF", IconStyle = IconStyle.Tinted
        },
        new()
        {
            Name = "Rosé Pine", Backdrop = BackdropMode.Acrylic, BackgroundColor = "#191724", Opacity = 0.85,
            ForegroundColor = "#E0DEF4", AccentColor = "#EBBCBA", PanelColor = "#1F1D2E", PanelOpacity = 0.96,
            CornerRadius = 18, BarHeight = 40, Margin = 10, IconSize = 21, IconSpacing = 6,
            FontFamily = "Segoe UI Variable Display, Segoe UI", FontSize = 12.5,
            BorderThickness = 1.5, BorderColor = "#C4A7E7", BorderColor2 = "#EBBCBA", Shadow = true, ShadowGlow = true,
            ShadowColor = "#88C4A7E7", ShadowSize = 18,
            ClockColor = "#EBBCBA", DateColor = "#C4A7E7", VolumeColor = "#9CCFD8", WidgetBackground = "#26233A", WidgetRadius = 12,
            BorderFx = BorderFx.Fireflies, BorderFxColor = "#F6C177", BorderFxSpeed = 0.8
        },
        new()
        {
            Name = "Kanagawa", Backdrop = BackdropMode.Solid, BackgroundColor = "#1F1F28", Opacity = 1,
            ForegroundColor = "#DCD7BA", AccentColor = "#7E9CD8", PanelColor = "#2A2A37", PanelOpacity = 1,
            CornerRadius = 6, BarHeight = 36, Margin = 6, IconSize = 19, IconSpacing = 6,
            FontFamily = "Cascadia Mono, Consolas", FontSize = 12.5,
            BorderThickness = 2, BorderColor = "#7E9CD8", BorderColor2 = "#957FB8",
            ClockColor = "#E6C384", ClockFont = "Cascadia Mono, Consolas", DateColor = "#957FB8", VolumeColor = "#98BB6C",
            WidgetBackground = "#363646", WidgetRadius = 4, IconColor = "#DCD7BA", IconStyle = IconStyle.Tinted
        },
        new()
        {
            Name = "Everforest", Backdrop = BackdropMode.Solid, BackgroundColor = "#2D353B", Opacity = 0.98,
            ForegroundColor = "#D3C6AA", AccentColor = "#A7C080", PanelColor = "#343F44", PanelOpacity = 1,
            CornerRadius = 10, BarHeight = 38, Margin = 6, IconSize = 20, IconSpacing = 6,
            FontFamily = "Cascadia Code, Consolas", FontSize = 12.5, BorderThickness = 1, BorderColor = "#475258",
            ClockColor = "#A7C080", DateColor = "#DBBC7F", VolumeColor = "#7FBBB3", WidgetBackground = "#3D484D", WidgetRadius = 8,
            BorderFx = BorderFx.Snake, BorderFxColor = "#A7C080", BorderFxSpeed = 0.7
        },
        new()
        {
            // Monitor a fosfori verdi: tutto in un solo colore, cursore finto dopo l'ora e uno scanner sul bordo.
            Name = "Phosphor CRT", Backdrop = BackdropMode.Solid, BackgroundColor = "#000F00", BackgroundColor2 = "#001A06", Opacity = 1,
            ForegroundColor = "#33FF66", AccentColor = "#33FF66", PanelColor = "#001405", PanelOpacity = 1,
            CornerRadius = 0, BarHeight = 32, Margin = 0, IconSize = 18, IconSpacing = 8,
            FontFamily = "Cascadia Mono, Consolas", FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#33FF66", BorderSides = BorderSides.DesktopSide,
            ClockColor = "#33FF66", ClockFont = "Cascadia Mono, Consolas", ClockPrefix = "> ", ClockSuffix = " _", DateColor = "#1FA344",
            VolumeColor = "#33FF66", IconColor = "#33FF66", IconStyle = IconStyle.Monochrome,
            BorderFx = BorderFx.Scanner, BorderFxColor = "#7DFF9C"
        },
        new()
        {
            // Blu del prompt di PowerShell.
            Name = "PowerShell", Backdrop = BackdropMode.Solid, BackgroundColor = "#012456", Opacity = 1,
            ForegroundColor = "#EEEDF0", AccentColor = "#F9F1A5", PanelColor = "#012456", PanelOpacity = 1,
            CornerRadius = 0, BarHeight = 34, Margin = 0, IconSize = 19, IconSpacing = 6,
            FontFamily = "Cascadia Mono, Consolas", FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#2A5CA8", BorderSides = BorderSides.DesktopSide,
            ClockColor = "#F9F1A5", ClockFont = "Cascadia Mono, Consolas", ClockPrefix = "PS> ", DateColor = "#9BB9E8", VolumeColor = "#F9F1A5"
        },
        new()
        {
            Name = "Ubuntu Terminal", Backdrop = BackdropMode.Solid, BackgroundColor = "#300A24", Opacity = 1,
            ForegroundColor = "#FFFFFF", AccentColor = "#E95420", PanelColor = "#2C001E", PanelOpacity = 1,
            CornerRadius = 8, BarHeight = 36, Margin = 6, IconSize = 20, IconSpacing = 6,
            FontFamily = "Ubuntu Mono, Cascadia Mono, Consolas", FontSize = 13,
            BorderThickness = 1, BorderColor = "#5E2750",
            ClockColor = "#8AE234", ClockFont = "Ubuntu Mono, Cascadia Mono, Consolas", ClockPrefix = "user@pc:~$ ", DateColor = "#729FCF",
            VolumeColor = "#E95420", WidgetBackground = "#4A1238", WidgetRadius = 6
        },
        new()
        {
            // Rosso su nero: "root shell", icone sagomate e due comete rosse.
            Name = "Root Shell", Backdrop = BackdropMode.Solid, BackgroundColor = "#0A0000", Opacity = 0.97,
            ForegroundColor = "#FF5F5F", AccentColor = "#FF2A2A", PanelColor = "#120202", PanelOpacity = 1,
            CornerRadius = 2, BarHeight = 34, Margin = 4, IconSize = 18, IconSpacing = 7,
            FontFamily = "Cascadia Mono, Consolas", FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#4D0000",
            ClockColor = "#FF2A2A", ClockFont = "Cascadia Mono, Consolas", ClockBold = true, ClockPrefix = "# ", DateColor = "#B33A3A",
            VolumeColor = "#FF5F5F", IconColor = "#FF5F5F", IconStyle = IconStyle.Monochrome,
            BorderFx = BorderFx.TwinComets, BorderFxColor = "#FF2A2A", BorderFxSpeed = 1.3
        },

        // ---- Temi divertenti -------------------------------------------------------------------
        new()
        {
            // Lo stagno: acqua verde-azzurra e una famiglia di papere in passeggiata.
            Name = "Duck Pond", Backdrop = BackdropMode.Solid, BackgroundColor = "#0E3B43", BackgroundColor2 = "#16606B", Opacity = 0.97,
            ForegroundColor = "#E8FBFF", AccentColor = "#FFD93D", PanelColor = "#0F3A42", PanelOpacity = 1,
            CornerRadius = 20, BarHeight = 44, Margin = 8, IconSize = 21, IconSpacing = 6,
            FontFamily = "Segoe UI Variable Display, Segoe UI", FontSize = 12.5,
            BorderThickness = 1.5, BorderColor = "#5FD3E0", Shadow = true, ShadowColor = "#000000", ShadowSize = 16,
            ClockColor = "#FFD93D", ClockBold = true, DateColor = "#A6F0F7", VolumeColor = "#FFD93D", WidgetBackground = "#1F7480", WidgetRadius = 12,
            IconColor = "#FFD93D", BorderFx = BorderFx.DuckFamily
        },
        new()
        {
            // Setup da gaming con il bordo arcobaleno che scorre.
            Name = "RGB Rainbow", Backdrop = BackdropMode.Solid, BackgroundColor = "#0B0B10", Opacity = 0.97,
            ForegroundColor = "#FFFFFF", AccentColor = "#2EC5FF", PanelColor = "#101018", PanelOpacity = 1,
            CornerRadius = 10, BarHeight = 40, Margin = 8, IconSize = 21, IconSpacing = 6,
            FontFamily = "Bahnschrift, Segoe UI", FontSize = 13, Shadow = true, ShadowGlow = true, ShadowColor = "#882EC5FF", ShadowSize = 18,
            ClockColor = "#FFFFFF", ClockFont = "Bahnschrift, Segoe UI", ClockFontSize = 14, DateColor = "#FF3BD4", VolumeColor = "#3BFF6E",
            WidgetBackground = "#1AFFFFFF", WidgetRadius = 6, BorderFx = BorderFx.Rainbow, BorderFxSpeed = 1.2
        },
        new()
        {
            Name = "Area 51", Backdrop = BackdropMode.Solid, BackgroundColor = "#05060F", BackgroundColor2 = "#101A33", Opacity = 0.97,
            ForegroundColor = "#D7FFE4", AccentColor = "#5CFF8F", PanelColor = "#0A0F1E", PanelOpacity = 1,
            CornerRadius = 14, BarHeight = 42, Margin = 8, IconSize = 21, IconSpacing = 6,
            FontFamily = "Cascadia Code, Consolas", FontSize = 12.5,
            BorderThickness = 1, BorderColor = "#2E4A7A", Shadow = true, ShadowGlow = true, ShadowColor = "#665CFF8F", ShadowSize = 16,
            ClockColor = "#5CFF8F", ClockFont = "Cascadia Code, Consolas", ClockPrefix = "◉ ", DateColor = "#8FA8FF", VolumeColor = "#5CFF8F",
            IconColor = "#5CFF8F", IconStyle = IconStyle.Tinted, BorderFx = BorderFx.Ufo, BorderFxColor = "#5CFF8F"
        },
        new()
        {
            Name = "Rubber Duck", Backdrop = BackdropMode.Solid, BackgroundColor = "#FFF8D6", Opacity = 0.98,
            ForegroundColor = "#4A3B00", AccentColor = "#FF8C1A", PanelColor = "#FFFBE8", PanelOpacity = 1,
            CornerRadius = 22, BarHeight = 42, Margin = 10, IconSize = 21, IconSpacing = 6,
            FontFamily = "Segoe UI Variable Display, Segoe UI", FontSize = 12.5,
            BorderThickness = 2, BorderColor = "#FFD93D", Shadow = true, ShadowColor = "#55C9A400", ShadowSize = 18,
            ClockColor = "#FF8C1A", ClockBold = true, DateColor = "#8A6D00", VolumeColor = "#FF8C1A", WidgetBackground = "#26FFD93D", WidgetRadius = 14,
            BorderFx = BorderFx.Duck, BorderFxSpeed = 1.2
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

        // Icone: i glifi della barra usano IconBrush; le icone delle app (bitmap) possono essere velate o ridotte a una
        // sagoma in tinta unita, disegnando sopra un rettangolo colorato che usa l'icona stessa come maschera.
        Color icon = string.IsNullOrWhiteSpace(t.IconColor) ? fg : ParseColor(t.IconColor, fg);
        res["IconBrush"] = Brush(icon);
        res["AppIconTintBrush"] = Brush(Color.FromRgb(icon.R, icon.G, icon.B));
        res["AppIconImageOpacity"] = t.IconStyle == IconStyle.Monochrome ? 0.0 : 1.0;
        res["AppIconTintOpacity"] = t.IconStyle switch { IconStyle.Monochrome => 1.0, IconStyle.Tinted => 0.55, _ => 0.0 };
        res["AppIconTintVisibility"] = t.IconStyle == IconStyle.Original ? Visibility.Collapsed : Visibility.Visible;

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
