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
/// <summary>Come disegnare le icone delle app: colori originali, velate dal colore delle icone, oppure in tinta unita.</summary>
public enum IconStyle { Original, Tinted, Monochrome }
/// <summary>Effetto animato sul bordo della barra: minimal (luce, tratteggio, arcobaleno) o divertente (personaggi).</summary>
public enum BorderFx { None, Pulse, Comet, TwinComets, MarchingAnts, Rainbow, Scanner, Duck, DuckFamily, Snake, Fireflies, Ufo }
/// <summary>Elementi della barra che il layout avanzato permette di disporre a piacere.</summary>
public enum BarWidget { Apps, Media, Gifs, SysInfo, Volume, Clock, Tray, QuickSettings, Start }
/// <summary>Zone della barra: inizio (sinistra/alto), centro, fine (destra/basso).</summary>
public enum BarZone { Start, Center, End }

/// <summary>
/// Da dove il menu Start prende l'elenco delle app: i collegamenti nella cartella "Start Menu\Programs" dell'utente,
/// oppure tutte le app installate (shell:AppsFolder, comprese quelle dello Store).
/// </summary>
public enum StartAppSource { StartMenuFolder, AllInstalled }

/// <summary>
/// Come il menu Start dispone le app di "All apps": elenco, elenco compatto, due colonne, griglia di tessere,
/// oppure elenco raggruppato per lettera iniziale. I risultati della ricerca restano sempre un elenco ordinato.
/// </summary>
public enum StartAppLayout { List, Compact, Columns, Grid, Grouped }

/// <summary>Widget che si possono aggiungere alla barra, anche più volte (es. più orologi del mondo).</summary>
public enum ExtraWidgetKind { Text, WorldClock, NetworkSpeed, Cpu, Memory, Battery, Disk, Uptime, Shortcut, Timer, Separator, Spacer }

/// <summary>
/// Un widget aggiunto alla barra. Ogni istanza ha le sue opzioni; quelle che non riguardano il suo tipo restano
/// ai valori predefiniti e vengono ignorate.
/// </summary>
public class ExtraWidget
{
    /// <summary>Identificativo usato nel layout. Non coincide mai con il nome di un elemento predefinito.</summary>
    public string Id { get; set; } = NewId();
    /// <summary>Un tipo sconosciuto (aggiunto da una versione più recente) non rende illeggibile il file: il widget viene scartato.</summary>
    [JsonConverter(typeof(TolerantEnumConverter<ExtraWidgetKind>))]
    public ExtraWidgetKind Kind { get; set; }
    /// <summary>Il testo del widget Testo; per gli altri, un'etichetta prima del valore (vuoto = nessuna).</summary>
    public string Label { get; set; } = "";
    /// <summary>Collegamento: app, file, cartella, indirizzo web o comando. Spazio su disco: l'unità (es. "C:\").</summary>
    public string Target { get; set; } = "";
    /// <summary>Orologio del mondo: id del fuso orario di Windows (es. "Tokyo Standard Time").</summary>
    public string TimeZone { get; set; } = "";
    /// <summary>Orologio del mondo: formato .NET dell'ora (vuoto = come l'orologio della barra, 24 o 12 ore).</summary>
    public string Format { get; set; } = "";
    /// <summary>Colore del testo e dell'icona (vuoto = colori della barra).</summary>
    public string Color { get; set; } = "";
    public bool ShowIcon { get; set; } = true;
    /// <summary>Spaziatore: larghezza in px (altezza sulle barre verticali).</summary>
    public double Width { get; set; } = 16;
    /// <summary>Timer: durata del conto alla rovescia in minuti (0 = cronometro).</summary>
    public double Minutes { get; set; } = 25;

    public static string NewId() => "w" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Un nuovo widget del tipo indicato, con opzioni iniziali sensate.</summary>
    public static ExtraWidget Create(ExtraWidgetKind kind)
    {
        var widget = new ExtraWidget { Kind = kind };
        switch (kind)
        {
            case ExtraWidgetKind.Text:
                widget.Label = "Hello";
                widget.ShowIcon = false;
                break;
            case ExtraWidgetKind.WorldClock:
                widget.TimeZone = "UTC";
                break;
            case ExtraWidgetKind.NetworkSpeed:
                widget.ShowIcon = false; // le frecce di download e upload bastano
                break;
            case ExtraWidgetKind.Disk:
                widget.Target = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                break;
        }
        return widget;
    }
}

/// <summary>
/// Disposizione degli elementi sulla barra: per ogni zona, gli elementi nell'ordine in cui compaiono. Ogni elemento è
/// una chiave: il nome di un elemento predefinito (<see cref="BarWidget"/>) oppure l'Id di un <see cref="ExtraWidget"/>.
/// </summary>
public class BarLayout
{
    public List<string> StartItems { get; set; } = new();
    public List<string> CenterItems { get; set; } = new();
    public List<string> EndItems { get; set; } = new();

    public List<string> this[BarZone zone] => zone switch
    {
        BarZone.Start => StartItems,
        BarZone.Center => CenterItems,
        _ => EndItems
    };

    // Formato delle versioni fino alla 1.1.6: le stesse zone con i soli elementi predefiniti. Lo si legge dai file
    // salvati da quelle versioni e lo si scrive ancora: tornando a una di esse (es. lasciando le pre-release) il file
    // resta leggibile e la disposizione degli elementi predefiniti non si perde.
    // Gli elementi sconosciuti (aggiunti da versioni più recenti) si saltano invece di rendere illeggibile il file.
    private readonly List<BarWidget>?[] _legacy = new List<BarWidget>?[3];
    [JsonPropertyName("Start"), JsonConverter(typeof(TolerantEnumListConverter<BarWidget>))]
    public List<BarWidget> LegacyStart { get => BuiltIns(StartItems); set => _legacy[0] = value; }
    [JsonPropertyName("Center"), JsonConverter(typeof(TolerantEnumListConverter<BarWidget>))]
    public List<BarWidget> LegacyCenter { get => BuiltIns(CenterItems); set => _legacy[1] = value; }
    [JsonPropertyName("End"), JsonConverter(typeof(TolerantEnumListConverter<BarWidget>))]
    public List<BarWidget> LegacyEnd { get => BuiltIns(EndItems); set => _legacy[2] = value; }

    private static List<BarWidget> BuiltIns(List<string> keys)
    {
        var list = new List<BarWidget>();
        foreach (string key in keys)
            if (TryBuiltIn(key, out BarWidget widget)) list.Add(widget);
        return list;
    }

    public static string Key(BarWidget widget) => widget.ToString();

    /// <summary>Vero se la chiave è un elemento predefinito (e non l'Id di un widget aggiunto).</summary>
    public static bool TryBuiltIn(string? key, out BarWidget widget)
    {
        widget = default;
        return key != null && Enum.TryParse(key, false, out widget) && Enum.IsDefined(widget) && widget.ToString() == key;
    }

    public static bool IsBuiltIn(string? key) => TryBuiltIn(key, out _);

    private static BarLayout Make(BarWidget[] start, BarWidget[] center, BarWidget[] end)
    {
        var layout = new BarLayout();
        layout.StartItems.AddRange(start.Select(Key));
        layout.CenterItems.AddRange(center.Select(Key));
        layout.EndItems.AddRange(end.Select(Key));
        return layout;
    }

    /// <summary>Disposizione classica: app a sinistra, player al centro, sistema a destra; le GIF dove indicato.</summary>
    public static BarLayout Default(GifPlacement gifs = GifPlacement.Center)
    {
        var layout = Make(
            new[] { BarWidget.Apps },
            new[] { BarWidget.Media },
            new[] { BarWidget.SysInfo, BarWidget.Volume, BarWidget.Clock, BarWidget.Tray, BarWidget.QuickSettings, BarWidget.Start });
        string gif = Key(BarWidget.Gifs);
        switch (gifs)
        {
            case GifPlacement.Left: layout.StartItems.Insert(0, gif); break;
            case GifPlacement.Right: layout.EndItems.Insert(0, gif); break;
            default: layout.CenterItems.Add(gif); break;
        }
        return layout;
    }

    /// <summary>Stile Windows 11: Start e app al centro, sistema a destra, player a sinistra.</summary>
    public static BarLayout Centered() => Make(
        new[] { BarWidget.Media, BarWidget.Gifs },
        new[] { BarWidget.Start, BarWidget.Apps },
        new[] { BarWidget.SysInfo, BarWidget.Tray, BarWidget.Volume, BarWidget.QuickSettings, BarWidget.Clock });

    /// <summary>
    /// Garantisce che ogni elemento compaia una e una sola volta (file modificato a mano, elementi aggiunti da una
    /// versione più recente): i doppioni e le chiavi sconosciute spariscono, gli elementi predefiniti mancanti tornano
    /// nella loro zona predefinita. <paramref name="extraIds"/> sono gli Id dei widget aggiunti che esistono.
    /// </summary>
    public void Normalize(ICollection<string> extraIds)
    {
        StartItems ??= new();
        CenterItems ??= new();
        EndItems ??= new();
        // File salvato da una versione precedente: si parte dai suoi elenchi.
        if (StartItems.Count + CenterItems.Count + EndItems.Count == 0)
            foreach (BarZone zone in Enum.GetValues<BarZone>())
                if (_legacy[(int)zone] is { } old) this[zone].AddRange(old.Select(Key));
        Array.Clear(_legacy);

        var seen = new HashSet<string>();
        foreach (BarZone zone in Enum.GetValues<BarZone>())
            this[zone].RemoveAll(k => k == null || !(IsBuiltIn(k) || extraIds.Contains(k)) || !seen.Add(k));

        BarLayout fallback = Default();
        foreach (BarZone zone in Enum.GetValues<BarZone>())
            foreach (string key in fallback[zone])
                if (seen.Add(key)) this[zone].Add(key);
    }

    /// <summary>Zona in cui si trova un elemento (null se non c'è).</summary>
    public BarZone? ZoneOf(string key)
    {
        foreach (BarZone zone in Enum.GetValues<BarZone>())
            if (this[zone].Contains(key)) return zone;
        return null;
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
    private string _iconColor = "", _borderFxColor = "", _clockPrefix = "", _clockSuffix = "";
    private IconStyle _iconStyle = IconStyle.Original;
    private BorderFx _borderFx = BorderFx.None;
    private double _borderFxSpeed = 1;

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
    /// <summary>Testo fisso prima/dopo l'ora, es. "[ " e " ]" o "$ " per i temi da terminale.</summary>
    public string ClockPrefix { get => _clockPrefix; set => Set(ref _clockPrefix, value); }
    public string ClockSuffix { get => _clockSuffix; set => Set(ref _clockSuffix, value); }

    // Icone
    /// <summary>Colore delle icone a glifo (Start, volume, player...) e della tinta delle icone delle app (vuoto = testo).</summary>
    public string IconColor { get => _iconColor; set => Set(ref _iconColor, value); }
    public IconStyle IconStyle { get => _iconStyle; set => Set(ref _iconStyle, value); }

    // Bordo animato
    public BorderFx BorderFx { get => _borderFx; set => Set(ref _borderFx, value); }
    /// <summary>Colore dell'effetto (vuoto = accento).</summary>
    public string BorderFxColor { get => _borderFxColor; set => Set(ref _borderFxColor, value); }
    /// <summary>Velocità relativa dell'effetto (1 = normale).</summary>
    public double BorderFxSpeed { get => _borderFxSpeed; set => Set(ref _borderFxSpeed, value); }

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
    private bool _showRunningApps = true, _showPinnedApps = true, _groupWindows = true, _showWindowPreviews = true;
    private bool _autoStart = true, _hideWindowsTaskbar = true, _autoUpdate = true, _preReleaseUpdates;
    private double _startMenuWidth = 420, _startMenuHeight = 600;
    private bool _startShowFavorites = true, _startShowRecent = true, _startHideUserName, _useWinKey = true;
    private StartAppSource _startAppSource = StartAppSource.AllInstalled;
    private StartAppLayout _startAppLayout = StartAppLayout.List;
    private bool _customCalendar = true, _customQuickSettings = true;
    private string _clockFormat = "", _dateFormat = "";
    private bool _dateBelowTime, _showVolume = true, _showVolumePercent;
    private double _gifSize;
    private GifPlacement _gifPlacement = GifPlacement.Center;
    private bool _advancedLayout, _showDesktopButton = true;

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
    /// <summary>Lineetta "mostra desktop" all'estremità della barra.</summary>
    public bool ShowDesktopButton { get => _showDesktopButton; set => Set(ref _showDesktopButton, value); }
    public bool ShowRunningApps { get => _showRunningApps; set => Set(ref _showRunningApps, value); }
    public bool ShowPinnedApps { get => _showPinnedApps; set => Set(ref _showPinnedApps, value); }
    public bool GroupWindows { get => _groupWindows; set => Set(ref _groupWindows, value); }
    /// <summary>Anteprime delle finestre al passaggio del mouse sulle icone (e "sbirciata" a tutto schermo).</summary>
    public bool ShowWindowPreviews { get => _showWindowPreviews; set => Set(ref _showWindowPreviews, value); }

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

    /// <summary>Widget aggiunti alla barra (orologi del mondo, misuratori, collegamenti...). La posizione la dà <see cref="Layout"/>.</summary>
    public List<ExtraWidget> ExtraWidgets { get; set; } = new();
    /// <summary>I widget aggiunti che si vedono adesso: solo con il layout avanzato (quello classico non li prevede).</summary>
    [JsonIgnore] public IReadOnlyList<ExtraWidget> ShownExtraWidgets =>
        AdvancedLayout && Layout != null ? ExtraWidgets : Array.Empty<ExtraWidget>();
    /// <summary>CPU, memoria, batteria o rete servono al monitor di sistema o a qualche widget aggiunto.</summary>
    [JsonIgnore] public bool NeedsSystemInfo => ShowSysInfo || ShownExtraWidgets.Any(w =>
        w.Kind is ExtraWidgetKind.Cpu or ExtraWidgetKind.Memory or ExtraWidgetKind.Battery or ExtraWidgetKind.NetworkSpeed);
    [JsonIgnore] public bool NeedsNetworkSpeed => ShownExtraWidgets.Any(w => w.Kind == ExtraWidgetKind.NetworkSpeed);

    public ExtraWidget? FindExtraWidget(string id) => ExtraWidgets.FirstOrDefault(w => w.Id == id);

    /// <summary>
    /// Aggiunge un widget in una zona, alla posizione indicata. Se il layout avanzato era spento lo accende, partendo
    /// dalla disposizione classica: gli altri elementi restano dove sono.
    /// </summary>
    public void AddExtraWidget(ExtraWidget widget, BarZone zone, int index)
    {
        Layout ??= BarLayout.Default(GifPlacement);
        ExtraWidgets.Add(widget);
        List<string> list = Layout[zone];
        list.Insert(Math.Clamp(index, 0, list.Count), widget.Id);
        AdvancedLayout = true;
    }

    public void RemoveExtraWidget(ExtraWidget widget)
    {
        ExtraWidgets.Remove(widget);
        if (Layout != null)
            foreach (BarZone zone in Enum.GetValues<BarZone>()) Layout[zone].Remove(widget.Id);
    }

    /// <summary>Dopo il caricamento: layout coerente e niente widget orfani, doppi o senza Id.</summary>
    public void NormalizeLayout()
    {
        ExtraWidgets ??= new();
        var ids = new HashSet<string>();
        ExtraWidgets.RemoveAll(w => w == null || !Enum.IsDefined(w.Kind) || string.IsNullOrWhiteSpace(w.Id) || BarLayout.IsBuiltIn(w.Id) || !ids.Add(w.Id));
        if (Layout == null)
        {
            ExtraWidgets.Clear();
            return;
        }
        Layout.Normalize(ids);
        ExtraWidgets.RemoveAll(w => Layout.ZoneOf(w.Id) == null);
    }

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
    /// <summary>Sezione "Recent" del menu Start: le ultime app aperte da OpSecBar.</summary>
    public bool StartShowRecent { get => _startShowRecent; set => Set(ref _startShowRecent, value); }
    /// <summary>Maschera con asterischi il nome dell'account Windows nel piede del menu Start.</summary>
    public bool StartHideUserName { get => _startHideUserName; set => Set(ref _startHideUserName, value); }
    /// <summary>Quali app elenca il menu Start (predefinito: tutte le app installate).</summary>
    public StartAppSource StartAppSource { get => _startAppSource; set => Set(ref _startAppSource, value); }
    /// <summary>Come sono disposte le app nell'elenco "All apps" del menu Start.</summary>
    public StartAppLayout StartAppLayout { get => _startAppLayout; set => Set(ref _startAppLayout, value); }
    /// <summary>Il tasto Windows apre il menu Start di OpSecBar invece di quello di Windows.</summary>
    public bool UseWinKey { get => _useWinKey; set => Set(ref _useWinKey, value); }

    // Pannelli
    /// <summary>Il click sull'orologio apre il calendario di Open TaskBar invece del pannello notifiche/calendario di Windows.</summary>
    public bool CustomCalendar { get => _customCalendar; set => Set(ref _customCalendar, value); }
    /// <summary>Il pulsante delle impostazioni rapide apre il pannello di Open TaskBar invece di quello di Windows.</summary>
    public bool CustomQuickSettings { get => _customQuickSettings; set => Set(ref _customQuickSettings, value); }

    // Elenchi (salvati esplicitamente quando cambiano)
    public List<PinnedApp> PinnedApps { get; set; } = new();
    /// <summary>Ordine scelto trascinando le icone delle app aperte ma non fissate (chiavi di AppItem).</summary>
    public List<string> TaskOrder { get; set; } = new();
    public List<PinnedApp> StartFavorites { get; set; } = new();
    /// <summary>Icone del tray sempre visibili. Null = predefinite di Windows (rete, audio, batteria...).</summary>
    public string[]? PinnedTrayIcons { get; set; }
    /// <summary>Ultimi colori scelti nel selettore (il più recente per primo).</summary>
    public List<string> RecentColors { get; set; } = new();
    public List<GifItem> Gifs { get; set; } = new();
}

/// <summary>
/// Enum scritto come testo che, in lettura, trasforma un valore sconosciuto in -1 invece di fallire: il chiamante
/// lo riconosce con <see cref="Enum.IsDefined{TEnum}(TEnum)"/> e lo scarta.
/// </summary>
public sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse(reader.GetString(), true, out T named) && Enum.IsDefined(named))
            return named;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int number) && Enum.IsDefined(typeof(T), number))
            return (T)Enum.ToObject(typeof(T), number);
        return (T)Enum.ToObject(typeof(T), -1);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

/// <summary>Elenco di enum scritti come testo; in lettura i valori sconosciuti vengono saltati.</summary>
public sealed class TolerantEnumListConverter<T> : JsonConverter<List<T>> where T : struct, Enum
{
    private static readonly TolerantEnumConverter<T> Item = new();

    public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException($"Expected a list of {typeof(T).Name}.");
        var list = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }
            T value = Item.Read(ref reader, typeof(T), options);
            if (Enum.IsDefined(value)) list.Add(value);
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (T item in value) writer.WriteStringValue(item.ToString());
        writer.WriteEndArray();
    }
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
