using System.Text.Json;
using System.Text.Json.Serialization;
using OpSecBar.Core;

namespace OpSecBar.Models;

public enum BarPosition { Top, Bottom, Left, Right }
public enum BarBehavior { AlwaysVisible, AutoHide }
public enum BackdropMode { Solid, Transparent, Blur, Acrylic, Glass }

/// <summary>Tutti i parametri grafici della barra. Un "tema" è semplicemente un'istanza di questa classe.</summary>
public class ThemeData : ObservableObject
{
    private string _name = "Custom";
    private BackdropMode _backdrop = BackdropMode.Acrylic;
    private string _backgroundColor = "#1B1C22";
    private double _opacity = 0.72;
    private string _foregroundColor = "#FFFFFF";
    private string _accentColor = "#7C9CFF";
    private string _panelColor = "#1B1C22";
    private double _panelOpacity = 0.9;
    private double _cornerRadius;
    private double _barHeight = 40;
    private double _margin;
    private double _iconSize = 22;
    private double _iconSpacing = 4;
    private string _fontFamily = "Segoe UI Variable Text, Segoe UI";
    private double _fontSize = 12.5;
    private bool _shadow;
    private double _borderThickness;
    private string _borderColor = "#33FFFFFF";

    public string Name { get => _name; set => Set(ref _name, value); }
    public BackdropMode Backdrop { get => _backdrop; set => Set(ref _backdrop, value); }
    public string BackgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, value); }
    public string ForegroundColor { get => _foregroundColor; set => Set(ref _foregroundColor, value); }
    public string AccentColor { get => _accentColor; set => Set(ref _accentColor, value); }
    /// <summary>Colore di menu, flyout e menu Start.</summary>
    public string PanelColor { get => _panelColor; set => Set(ref _panelColor, value); }
    public double PanelOpacity { get => _panelOpacity; set => Set(ref _panelOpacity, value); }
    public double CornerRadius { get => _cornerRadius; set => Set(ref _cornerRadius, value); }
    public double BarHeight { get => _barHeight; set => Set(ref _barHeight, value); }
    /// <summary>Distanza dai bordi dello schermo (0 = barra a tutta larghezza, &gt;0 = barra "flottante").</summary>
    public double Margin { get => _margin; set => Set(ref _margin, value); }
    public double IconSize { get => _iconSize; set => Set(ref _iconSize, value); }
    public double IconSpacing { get => _iconSpacing; set => Set(ref _iconSpacing, value); }
    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; set => Set(ref _fontSize, value); }
    public bool Shadow { get => _shadow; set => Set(ref _shadow, value); }
    public double BorderThickness { get => _borderThickness; set => Set(ref _borderThickness, value); }
    public string BorderColor { get => _borderColor; set => Set(ref _borderColor, value); }

    public ThemeData Clone() => JsonSerializer.Deserialize<ThemeData>(JsonSerializer.Serialize(this, JsonOptions.Default), JsonOptions.Default)!;
}

/// <summary>Un'app fissata sulla barra o tra i preferiti del menu Start.</summary>
public class PinnedApp
{
    public string Name { get; set; } = "";
    /// <summary>Identificativo in shell:AppsFolder (AUMID). Null per un semplice eseguibile.</summary>
    public string? AppId { get; set; }
    /// <summary>Percorso dell'eseguibile (può essere null per le app UWP).</summary>
    public string? TargetPath { get; set; }

    [JsonIgnore] public string IconKey => !string.IsNullOrEmpty(AppId) ? "shell:AppsFolder\\" + AppId : TargetPath ?? "";
    [JsonIgnore] public string Key => (AppId ?? TargetPath ?? Name).ToLowerInvariant();
}

public class AppSettings : ObservableObject
{
    private ThemeData _theme = new();
    private BarPosition _position = BarPosition.Top;
    private BarBehavior _behavior = BarBehavior.AlwaysVisible;
    private bool _hideOnFullscreen = true, _allMonitors = true;
    private bool _showMediaPlayer = true, _showMediaTitle = true, _showClock = true, _showDate = true, _showSeconds;
    private bool _clock24h = true, _showTray = true, _showQuickSettings = true, _showStartButton = true, _showSysInfo;
    private bool _showRunningApps = true, _showPinnedApps = true, _groupWindows = true;
    private bool _autoStart, _hideWindowsTaskbar = true;
    private double _startMenuWidth = 420, _startMenuHeight = 600;
    private bool _startShowFavorites = true, _useWinKey = true;

    public ThemeData Theme { get => _theme; set => Set(ref _theme, value); }

    // Posizione e comportamento
    public BarPosition Position { get => _position; set => Set(ref _position, value); }
    public BarBehavior Behavior { get => _behavior; set => Set(ref _behavior, value); }
    public bool HideOnFullscreen { get => _hideOnFullscreen; set => Set(ref _hideOnFullscreen, value); }
    public bool AllMonitors { get => _allMonitors; set => Set(ref _allMonitors, value); }

    // Elementi
    public bool ShowMediaPlayer { get => _showMediaPlayer; set => Set(ref _showMediaPlayer, value); }
    public bool ShowMediaTitle { get => _showMediaTitle; set => Set(ref _showMediaTitle, value); }
    public bool ShowClock { get => _showClock; set => Set(ref _showClock, value); }
    public bool ShowDate { get => _showDate; set => Set(ref _showDate, value); }
    public bool ShowSeconds { get => _showSeconds; set => Set(ref _showSeconds, value); }
    public bool Clock24h { get => _clock24h; set => Set(ref _clock24h, value); }
    public bool ShowTray { get => _showTray; set => Set(ref _showTray, value); }
    public bool ShowQuickSettings { get => _showQuickSettings; set => Set(ref _showQuickSettings, value); }
    public bool ShowStartButton { get => _showStartButton; set => Set(ref _showStartButton, value); }
    public bool ShowSysInfo { get => _showSysInfo; set => Set(ref _showSysInfo, value); }
    public bool ShowRunningApps { get => _showRunningApps; set => Set(ref _showRunningApps, value); }
    public bool ShowPinnedApps { get => _showPinnedApps; set => Set(ref _showPinnedApps, value); }
    public bool GroupWindows { get => _groupWindows; set => Set(ref _groupWindows, value); }

    // Sistema
    public bool AutoStart { get => _autoStart; set => Set(ref _autoStart, value); }
    public bool HideWindowsTaskbar { get => _hideWindowsTaskbar; set => Set(ref _hideWindowsTaskbar, value); }

    // Menu Start
    public double StartMenuWidth { get => _startMenuWidth; set => Set(ref _startMenuWidth, value); }
    public double StartMenuHeight { get => _startMenuHeight; set => Set(ref _startMenuHeight, value); }
    public bool StartShowFavorites { get => _startShowFavorites; set => Set(ref _startShowFavorites, value); }
    /// <summary>Il tasto Windows apre il menu Start di OpSecBar invece di quello di Windows.</summary>
    public bool UseWinKey { get => _useWinKey; set => Set(ref _useWinKey, value); }

    // Elenchi (salvati esplicitamente quando cambiano)
    public List<PinnedApp> PinnedApps { get; set; } = new();
    public List<PinnedApp> StartFavorites { get; set; } = new();
    /// <summary>Icone del tray sempre visibili. Null = predefinite di Windows (rete, audio, batteria...).</summary>
    public string[]? PinnedTrayIcons { get; set; }
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
