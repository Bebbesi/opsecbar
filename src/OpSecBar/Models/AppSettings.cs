using System.Text.Json;
using System.Text.Json.Serialization;
using OpSecBar.Core;

namespace OpSecBar.Models;

public enum BarPosition { Top, Bottom, Left, Right }
public enum BarBehavior { AlwaysVisible, AutoHide }
public enum BackdropMode { Solid, Transparent, Blur, Acrylic, Glass }
/// <summary>Su quali lati disegnare il bordo della barra.</summary>
public enum BorderSides { All, DesktopSide, ScreenSide }
/// <summary>Dove compaiono le GIF sulla barra.</summary>
public enum GifPlacement { Left, Center, Right }
/// <summary>Elementi della barra che il layout avanzato permette di disporre a piacere.</summary>
public enum BarWidget { Apps, Media, Gifs, SysInfo, Volume, Clock, Tray, QuickSettings, Start }
/// <summary>Zone della barra: inizio (sinistra/alto), centro, fine (destra/basso).</summary>
public enum BarZone { Start, Center, End }

/// <summary>Disposizione degli elementi sulla barra: per ogni zona, gli elementi nell'ordine in cui compaiono.</summary>
public class BarLayout
{
    public List<BarWidget> Start { get; set; } = new();
    public List<BarWidget> Center { get; set; } = new();
    public List<BarWidget> End { get; set; } = new();

    public List<BarWidget> this[BarZone zone] => zone switch
    {
        BarZone.Start => Start,
        BarZone.Center => Center,
        _ => End
    };

    /// <summary>Disposizione classica: app a sinistra, player al centro, sistema a destra; le GIF dove indicato.</summary>
    public static BarLayout Default(GifPlacement gifs = GifPlacement.Center)
    {
        var layout = new BarLayout
        {
            Start = { BarWidget.Apps },
            Center = { BarWidget.Media },
            End = { BarWidget.SysInfo, BarWidget.Volume, BarWidget.Clock, BarWidget.Tray, BarWidget.QuickSettings, BarWidget.Start }
        };
        switch (gifs)
        {
            case GifPlacement.Left: layout.Start.Insert(0, BarWidget.Gifs); break;
            case GifPlacement.Right: layout.End.Insert(0, BarWidget.Gifs); break;
            default: layout.Center.Add(BarWidget.Gifs); break;
        }
        return layout;
    }

    /// <summary>Stile Windows 11: Start e app al centro, sistema a destra, player a sinistra.</summary>
    public static BarLayout Centered() => new()
    {
        Start = { BarWidget.Media, BarWidget.Gifs },
        Center = { BarWidget.Start, BarWidget.Apps },
        End = { BarWidget.SysInfo, BarWidget.Tray, BarWidget.Volume, BarWidget.QuickSettings, BarWidget.Clock }
    };

    /// <summary>
    /// Garantisce che ogni elemento compaia una e una sola volta (file modificato a mano, elementi aggiunti da una
    /// versione più recente): i doppioni spariscono, i mancanti tornano nella loro zona predefinita.
    /// </summary>
    public void Normalize()
    {
        Start ??= new();
        Center ??= new();
        End ??= new();
        var seen = new HashSet<BarWidget>();
        foreach (BarZone zone in Enum.GetValues<BarZone>())
            this[zone].RemoveAll(w => !Enum.IsDefined(w) || !seen.Add(w));

        BarLayout fallback = Default();
        foreach (BarZone zone in Enum.GetValues<BarZone>())
            foreach (BarWidget w in fallback[zone])
                if (seen.Add(w)) this[zone].Add(w);
    }
}

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
    private string _borderColor2 = "";
    private BorderSides _borderSides = BorderSides.All;
    private string _backgroundColor2 = "";
    private string _shadowColor = "#000000";
    private double _shadowSize = 16;
    private bool _shadowGlow;
    private string _clockColor = "", _clockFont = "", _dateColor = "", _volumeColor = "", _widgetBackground = "";
    private double _clockFontSize, _widgetRadius = 6;
    private bool _clockBold = true;

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
    /// <summary>Secondo colore del bordo: se impostato il bordo diventa una sfumatura (vuoto = tinta unita).</summary>
    public string BorderColor2 { get => _borderColor2; set => Set(ref _borderColor2, value); }
    public BorderSides BorderSides { get => _borderSides; set => Set(ref _borderSides, value); }
    /// <summary>Secondo colore dello sfondo: se impostato lo sfondo diventa una sfumatura (vuoto = tinta unita).</summary>
    public string BackgroundColor2 { get => _backgroundColor2; set => Set(ref _backgroundColor2, value); }
    public string ShadowColor { get => _shadowColor; set => Set(ref _shadowColor, value); }
    public double ShadowSize { get => _shadowSize; set => Set(ref _shadowSize, value); }
    /// <summary>Ombra centrata attorno alla barra (effetto "neon") invece che proiettata verso il basso.</summary>
    public bool ShadowGlow { get => _shadowGlow; set => Set(ref _shadowGlow, value); }

    // Widget: orologio, data (calendario), volume. Colori vuoti = colore del testo della barra.
    public string ClockColor { get => _clockColor; set => Set(ref _clockColor, value); }
    /// <summary>Font dell'orologio (vuoto = font della barra).</summary>
    public string ClockFont { get => _clockFont; set => Set(ref _clockFont, value); }
    /// <summary>Dimensione del testo dell'orologio (0 = come la barra).</summary>
    public double ClockFontSize { get => _clockFontSize; set => Set(ref _clockFontSize, value); }
    public bool ClockBold { get => _clockBold; set => Set(ref _clockBold, value); }
    public string DateColor { get => _dateColor; set => Set(ref _dateColor, value); }
    public string VolumeColor { get => _volumeColor; set => Set(ref _volumeColor, value); }
    /// <summary>Sfondo "a pillola" dietro orologio e volume (vuoto = nessuno).</summary>
    public string WidgetBackground { get => _widgetBackground; set => Set(ref _widgetBackground, value); }
    public double WidgetRadius { get => _widgetRadius; set => Set(ref _widgetRadius, value); }

    public ThemeData Clone() => JsonSerializer.Deserialize<ThemeData>(JsonSerializer.Serialize(this, JsonOptions.Default), JsonOptions.Default)!;
}

/// <summary>Una GIF caricata dall'utente e mostrata sulla barra.</summary>
public class GifItem
{
    /// <summary>Nome del file dentro la cartella "gifs" dei dati dell'app.</summary>
    public string File { get; set; } = "";
    public bool Enabled { get; set; } = true;
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
    private bool _autoStart, _hideWindowsTaskbar = true, _autoUpdate = true, _preReleaseUpdates;
    private double _startMenuWidth = 420, _startMenuHeight = 600;
    private bool _startShowFavorites = true, _useWinKey = true;
    private string _clockFormat = "", _dateFormat = "";
    private bool _dateBelowTime, _showVolume = true, _showVolumePercent;
    private double _gifSize;
    private GifPlacement _gifPlacement = GifPlacement.Center;
    private bool _advancedLayout;

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

    // Widget
    /// <summary>Formato .NET personalizzato dell'ora (vuoto = automatico da 24h/secondi).</summary>
    public string ClockFormat { get => _clockFormat; set => Set(ref _clockFormat, value); }
    /// <summary>Formato .NET personalizzato della data (vuoto = "ddd d MMM").</summary>
    public string DateFormat { get => _dateFormat; set => Set(ref _dateFormat, value); }
    /// <summary>Data sotto l'ora (su due righe) invece che accanto.</summary>
    public bool DateBelowTime { get => _dateBelowTime; set => Set(ref _dateBelowTime, value); }
    public bool ShowVolume { get => _showVolume; set => Set(ref _showVolume, value); }
    public bool ShowVolumePercent { get => _showVolumePercent; set => Set(ref _showVolumePercent, value); }
    /// <summary>Altezza delle GIF in px (0 = automatica, in base all'altezza della barra).</summary>
    public double GifSize { get => _gifSize; set => Set(ref _gifSize, value); }
    public GifPlacement GifPlacement { get => _gifPlacement; set => Set(ref _gifPlacement, value); }

    // Layout avanzato
    /// <summary>Se attivo, gli elementi della barra seguono <see cref="Layout"/> invece della disposizione classica.</summary>
    public bool AdvancedLayout { get => _advancedLayout; set => Set(ref _advancedLayout, value); }
    /// <summary>Disposizione personalizzata. Null finché il layout avanzato non viene attivato la prima volta.</summary>
    public BarLayout? Layout { get; set; }
    /// <summary>La disposizione da usare adesso: quella personalizzata, oppure quella classica.</summary>
    [JsonIgnore] public BarLayout EffectiveLayout => AdvancedLayout && Layout != null ? Layout : BarLayout.Default(GifPlacement);

    // Sistema
    public bool AutoStart { get => _autoStart; set => Set(ref _autoStart, value); }
    public bool HideWindowsTaskbar { get => _hideWindowsTaskbar; set => Set(ref _hideWindowsTaskbar, value); }
    /// <summary>All'avvio scarica e installa da sola le nuove release; se spento, le segnala soltanto.</summary>
    public bool AutoUpdate { get => _autoUpdate; set => Set(ref _autoUpdate, value); }
    /// <summary>Aggiorna alla versione più recente anche se è una pre-release; se spento si resta (o si torna) sull'ultima release.</summary>
    public bool PreReleaseUpdates { get => _preReleaseUpdates; set => Set(ref _preReleaseUpdates, value); }

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
    /// <summary>Ultimi colori scelti nel selettore (il più recente per primo).</summary>
    public List<string> RecentColors { get; set; } = new();
    public List<GifItem> Gifs { get; set; } = new();
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
