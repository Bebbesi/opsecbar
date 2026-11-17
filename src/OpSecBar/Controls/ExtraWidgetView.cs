using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using ManagedShell.Common.Helpers;
using OpSecBar.Core;
using OpSecBar.Models;
using OpSecBar.Services;

namespace OpSecBar.Controls;

/// <summary>Nome, icona e descrizione di ogni tipo di widget aggiuntivo, per le Impostazioni e i menu.</summary>
public static class ExtraWidgetCatalog
{
    public static readonly (ExtraWidgetKind Kind, string Name, string Glyph, string Description)[] All =
    {
        (ExtraWidgetKind.WorldClock, "World clock", "\uE774", "The time in another time zone."),
        (ExtraWidgetKind.NetworkSpeed, "Network speed", "\uE968", "Live download and upload speed."),
        (ExtraWidgetKind.Cpu, "CPU", "\uE950", "Processor usage."),
        (ExtraWidgetKind.Memory, "Memory", "\uE964", "How much RAM is in use."),
        (ExtraWidgetKind.Battery, "Battery", "\uE83F", "Charge level and charging state."),
        (ExtraWidgetKind.Disk, "Disk space", "\uEDA2", "Free space on a drive."),
        (ExtraWidgetKind.Uptime, "Uptime", "\uE7E8", "Time since Windows started."),
        (ExtraWidgetKind.Timer, "Timer", "\uE916", "A countdown or a stopwatch."),
        (ExtraWidgetKind.Shortcut, "Shortcut", "\uE8A7", "Opens an app, file, folder or website."),
        (ExtraWidgetKind.Text, "Text", "\uE8D2", "Any text you like."),
        (ExtraWidgetKind.Separator, "Separator", "\uE784", "A thin line between items."),
        (ExtraWidgetKind.Spacer, "Spacer", "\uE8AB", "Empty space between items."),
    };

    public static string NameOf(ExtraWidgetKind kind) => All.FirstOrDefault(w => w.Kind == kind).Name ?? kind.ToString();
    public static string GlyphOf(ExtraWidgetKind kind) => All.FirstOrDefault(w => w.Kind == kind).Glyph ?? "\uE71D";

    /// <summary>Il dettaglio che distingue un widget dagli altri dello stesso tipo (fuso orario, unità, durata...).</summary>
    public static string Detail(ExtraWidget w) => w.Kind switch
    {
        ExtraWidgetKind.WorldClock => w.Label.Length > 0 ? w.Label : ExtraWidgetView.CityOf(ExtraWidgetView.FindZone(w.TimeZone)),
        ExtraWidgetKind.Disk => ExtraWidgetView.DriveLabel(w.Target),
        ExtraWidgetKind.Shortcut => w.Label.Length > 0 ? w.Label : w.Target.Trim().Length > 0 ? ExtraWidgetView.TargetName(w.Target) : "Not set yet",
        ExtraWidgetKind.Text => w.Label.Length > 32 ? w.Label[..32] + "…" : w.Label,
        ExtraWidgetKind.Timer => w.Minutes <= 0 ? "Stopwatch" : $"{w.Minutes:0} min",
        ExtraWidgetKind.Spacer => $"{w.Width:0} px",
        _ => w.Label
    };

    /// <summary>Nome completo, es. "World clock · Tokyo".</summary>
    public static string Describe(ExtraWidget w)
    {
        string name = NameOf(w.Kind), detail = Detail(w).Trim();
        // "CPU · CPU" non dice nulla in più.
        return detail.Length > 0 && !string.Equals(detail, name, StringComparison.CurrentCultureIgnoreCase) ? $"{name} · {detail}" : name;
    }
}

/// <summary>
/// Stato dei widget Timer, condiviso da tutte le barre: su più monitor si vede lo stesso timer. Usa un orologio
/// monotono, quindi un cambio dell'ora di sistema non lo sposta.
/// </summary>
public static class WidgetTimers
{
    private sealed class State
    {
        public long StartedAt = -1, BankedMs;
        public bool Finished;
    }

    private static readonly Dictionary<string, State> States = new();

    /// <summary>Un timer è partito, si è fermato o è finito: le barre si aggiornano.</summary>
    public static event Action? Changed;

    private static long Now => Environment.TickCount64;
    private static State Get(string id) => States.TryGetValue(id, out State? s) ? s : States[id] = new State();

    public static bool IsRunning(string id) => States.TryGetValue(id, out State? s) && s.StartedAt >= 0;
    public static bool IsFinished(string id) => States.TryGetValue(id, out State? s) && s.Finished;

    public static TimeSpan Elapsed(string id)
    {
        if (!States.TryGetValue(id, out State? s)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(s.BankedMs + (s.StartedAt >= 0 ? Now - s.StartedAt : 0));
    }

    /// <summary>Avvia o mette in pausa; un timer finito riparte da capo.</summary>
    public static void Toggle(string id)
    {
        State s = Get(id);
        if (s.Finished)
        {
            s.Finished = false;
            s.BankedMs = 0;
            s.StartedAt = Now;
        }
        else if (s.StartedAt >= 0)
        {
            s.BankedMs += Now - s.StartedAt;
            s.StartedAt = -1;
        }
        else s.StartedAt = Now;
        Changed?.Invoke();
    }

    public static void Reset(string id)
    {
        if (States.Remove(id)) Changed?.Invoke();
    }

    /// <summary>Il conto alla rovescia è arrivato a zero: si ferma e suona, una volta sola anche con più barre.</summary>
    public static void Finish(string id, TimeSpan total)
    {
        State s = Get(id);
        if (s.Finished) return;
        s.StartedAt = -1;
        s.BankedMs = (long)total.TotalMilliseconds;
        s.Finished = true;
        try { SystemSounds.Exclamation.Play(); } catch { }
        Changed?.Invoke();
    }
}

/// <summary>
/// Un widget aggiunto dall'utente, disegnato sulla barra. Ogni barra ha la sua copia; i dati arrivano da fuori:
/// <see cref="RefreshSystemInfo"/> quando CPU, memoria, batteria o rete cambiano, <see cref="RefreshTimed"/> allo
/// scatto programmato secondo <see cref="DueInMs"/>.
/// </summary>
public sealed class ExtraWidgetView : Button
{
    private static ControlTemplate? _plainTemplate;
    private readonly bool _horizontal;
    private readonly TextBlock? _glyph, _text;
    private readonly Image? _image;
    private readonly TimeZoneInfo? _zone;
    private string _shown = "";
    private bool _alert;

    public ExtraWidget Model { get; }

    public bool UsesSystemInfo => Model.Kind is ExtraWidgetKind.Cpu or ExtraWidgetKind.Memory or ExtraWidgetKind.Battery or ExtraWidgetKind.NetworkSpeed;

    /// <summary>Riceve lo sfondo "a pillola" dei widget, come orologio e volume.</summary>
    public bool TakesPill => Model.Kind is not (ExtraWidgetKind.Separator or ExtraWidgetKind.Spacer);

    /// <summary>Un click fa qualcosa: gli altri widget non si evidenziano al passaggio del mouse.</summary>
    private bool Clickable => Model.Kind is ExtraWidgetKind.Shortcut or ExtraWidgetKind.Timer or ExtraWidgetKind.Cpu or
        ExtraWidgetKind.Memory or ExtraWidgetKind.NetworkSpeed or ExtraWidgetKind.Battery or ExtraWidgetKind.Disk;

    public ExtraWidgetView(ExtraWidget model, bool horizontal)
    {
        Model = model;
        _horizontal = horizontal;
        SetResourceReference(StyleProperty, "BarButton");
        Focusable = false;
        MinWidth = 0;
        ToolTipService.SetInitialShowDelay(this, 450);
        if (!Clickable) Template = PlainTemplate;
        if (!horizontal)
        {
            Padding = new Thickness(0, 7, 0, 7);
            Margin = new Thickness(3, 1, 3, 1);
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        switch (model.Kind)
        {
            case ExtraWidgetKind.Separator:
                Padding = new Thickness(0);
                Margin = new Thickness(0);
                HorizontalContentAlignment = HorizontalAlignment.Stretch;
                VerticalContentAlignment = VerticalAlignment.Stretch;
                var line = new Rectangle
                {
                    Width = horizontal ? 1 : double.NaN, Height = horizontal ? double.NaN : 1,
                    Margin = horizontal ? new Thickness(6, 9, 6, 9) : new Thickness(9, 6, 9, 6),
                    Opacity = string.IsNullOrWhiteSpace(model.Color) ? 0.35 : 1
                };
                SetBrush(line, Shape.FillProperty, model.Color, "FgBrush");
                Content = line;
                return;

            case ExtraWidgetKind.Spacer:
                Padding = new Thickness(0);
                Margin = new Thickness(0);
                double size = Math.Clamp(model.Width, 2, 400);
                if (horizontal) Width = size; else Height = size;
                return;
        }

        var panel = new StackPanel
        {
            Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        if (model.Kind == ExtraWidgetKind.Shortcut)
        {
            MinWidth = 30;
            _image = new Image { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            _image.SetResourceReference(WidthProperty, "BarIconSize");
            _image.SetResourceReference(HeightProperty, "BarIconSize");
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            panel.Children.Add(_image);
        }
        _glyph = new TextBlock
        {
            FontFamily = Ui.GlyphFont, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        TextOptions.SetTextFormattingMode(_glyph, TextFormattingMode.Ideal);
        _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
        Typography.SetNumeralAlignment(_text, FontNumeralAlignment.Tabular); // cifre a larghezza fissa: i valori non "ballano"
        if (!horizontal)
        {
            // Barra verticale stretta: testo piccolo, e le etichette lunghe vanno a capo invece di essere tagliate.
            _text.FontSize = 10;
            _text.TextWrapping = TextWrapping.Wrap;
        }
        else if (model.Kind != ExtraWidgetKind.Text)
        {
            // I valori che cambiano non restringono il widget: la barra non si sposta a ogni aggiornamento.
            _text.SizeChanged += (_, e) => { if (e.NewSize.Width > _text.MinWidth) _text.MinWidth = e.NewSize.Width; };
        }
        SetBrush(_glyph, TextBlock.ForegroundProperty, model.Color, "IconBrush");
        SetBrush(_text, TextBlock.ForegroundProperty, model.Color, "FgBrush");
        panel.Children.Add(_glyph);
        panel.Children.Add(_text);
        Content = panel;

        if (model.Kind == ExtraWidgetKind.WorldClock) _zone = FindZone(model.TimeZone);
        if (model.Kind == ExtraWidgetKind.Shortcut) LoadShortcutIcon();
    }

    /// <summary>Come il pulsante della barra, ma senza evidenziazione: per i widget che non si cliccano.</summary>
    private static ControlTemplate PlainTemplate
    {
        get
        {
            if (_plainTemplate != null) return _plainTemplate;
            var border = new FrameworkElementFactory(typeof(Border), "Bd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, new TemplateBindingExtension(HorizontalContentAlignmentProperty));
            presenter.SetValue(VerticalAlignmentProperty, new TemplateBindingExtension(VerticalContentAlignmentProperty));
            border.AppendChild(presenter);
            _plainTemplate = new ControlTemplate(typeof(Button)) { VisualTree = border };
            _plainTemplate.Seal();
            return _plainTemplate;
        }
    }

    private static void SetBrush(DependencyObject target, DependencyProperty property, string hex, string resource)
    {
        if (string.IsNullOrWhiteSpace(hex)) ((FrameworkElement)target).SetResourceReference(property, resource);
        else target.SetValue(property, new SolidColorBrush(ThemeService.ParseColor(hex, Colors.White)));
    }

    // ---- Aggiornamento -------------------------------------------------------------------------

    /// <summary>Tutto, alla creazione.</summary>
    public void RefreshAll(SystemInfoService sys, bool clock24h)
    {
        RefreshSystemInfo(sys);
        RefreshTimed(clock24h);
        if (Model.Kind == ExtraWidgetKind.Text) Show(null, "", Model.Label, null);
        if (Model.Kind == ExtraWidgetKind.Shortcut) ShowShortcut();
    }

    /// <summary>CPU, memoria, batteria, rete: chiamato quando arrivano nuovi valori.</summary>
    public void RefreshSystemInfo(SystemInfoService sys)
    {
        try
        {
            switch (Model.Kind)
            {
                case ExtraWidgetKind.Cpu:
                    Show("\uE950", Model.Label, $"{sys.Cpu}%", $"CPU usage: {sys.Cpu}%\nClick to open Task Manager");
                    break;
                case ExtraWidgetKind.Memory:
                    Show("\uE964", Model.Label, $"{sys.Ram}%",
                        $"Memory in use: {Bytes(sys.RamUsed)} of {Bytes(sys.RamTotal)} ({sys.Ram}%)\nClick to open Task Manager");
                    break;
                case ExtraWidgetKind.Battery:
                    Visibility = sys.Battery < 0 ? Visibility.Collapsed : Visibility.Visible;
                    if (sys.Battery < 0) return;
                    int level = Math.Clamp((int)Math.Round(sys.Battery / 10.0), 0, 10);
                    string glyph = sys.Charging
                        ? level switch { 10 => "\uEA93", 9 => "\uE83E", _ => ((char)(0xE85A + level)).ToString() }
                        : level == 10 ? "\uE83F" : ((char)(0xE850 + level)).ToString();
                    Show(glyph, Model.Label, $"{sys.Battery}%", $"Battery: {sys.Battery}%{(sys.Charging ? ", plugged in" : "")}");
                    break;
                case ExtraWidgetKind.NetworkSpeed:
                    string down = Rate(sys.DownloadSpeed, !_horizontal), up = Rate(sys.UploadSpeed, !_horizontal);
                    Show("\uE968", Model.Label, _horizontal ? $"↓ {down}   ↑ {up}" : $"↓{down}\n↑{up}",
                        $"Download: {Rate(sys.DownloadSpeed, false)}\nUpload: {Rate(sys.UploadSpeed, false)}\nClick to open Task Manager");
                    break;
            }
        }
        catch (Exception ex) { Log.Warn($"Widget {Model.Kind}: {ex.Message}"); }
    }

    /// <summary>Orologi, durate e spazio su disco: chiamato allo scatto programmato.</summary>
    public void RefreshTimed(bool clock24h)
    {
        try
        {
            switch (Model.Kind)
            {
                case ExtraWidgetKind.WorldClock: ShowWorldClock(clock24h); break;
                case ExtraWidgetKind.Uptime: ShowUptime(); break;
                case ExtraWidgetKind.Timer: ShowTimer(); break;
                case ExtraWidgetKind.Disk: ShowDisk(); break;
            }
        }
        catch (Exception ex) { Log.Warn($"Widget {Model.Kind}: {ex.Message}"); }
    }

    /// <summary>Millisecondi al prossimo cambio del testo; null se il widget non cambia da solo.</summary>
    public int? DueInMs(DateTime now)
    {
        int toMinute = (60 - now.Second) * 1000 - now.Millisecond;
        switch (Model.Kind)
        {
            case ExtraWidgetKind.WorldClock:
                return FormatHasSeconds(Model.Format) ? 1000 - now.Millisecond : toMinute;
            case ExtraWidgetKind.Uptime:
            case ExtraWidgetKind.Disk:
                return toMinute;
            case ExtraWidgetKind.Timer:
                if (!WidgetTimers.IsRunning(Model.Id)) return null;
                return 1000 - (int)(WidgetTimers.Elapsed(Model.Id).TotalMilliseconds % 1000);
            default:
                return null;
        }
    }

    /// <summary>Icona + etichetta + valore. Non tocca nulla se non è cambiato niente.</summary>
    private void Show(string? glyph, string label, string value, string? tooltip, bool forceGlyph = false)
    {
        if (_text == null || _glyph == null) return;
        label = label.Trim();
        string separator = _horizontal ? " " : "\n";
        string text = label.Length == 0 ? value : value.Length == 0 ? label : label + separator + value;
        bool showGlyph = (Model.ShowIcon || forceGlyph) && !string.IsNullOrEmpty(glyph) && _image?.Visibility != Visibility.Visible;

        string key = $"{text}\u0001{glyph}\u0001{showGlyph}";
        if (key != _shown)
        {
            _shown = key;
            _text.Text = text;
            _text.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _glyph.Text = glyph ?? "";
            _glyph.Visibility = showGlyph ? Visibility.Visible : Visibility.Collapsed;
            bool iconBefore = showGlyph || _image?.Visibility == Visibility.Visible;
            _text.Margin = !iconBefore || text.Length == 0 ? new Thickness(0)
                : _horizontal ? new Thickness(6, 0, 0, 0) : new Thickness(0, 3, 0, 0);
        }
        if (!Equals(ToolTip, tooltip)) ToolTip = tooltip;
    }

    private void ShowWorldClock(bool clock24h)
    {
        TimeZoneInfo zone = _zone ?? TimeZoneInfo.Utc;
        DateTime time = TimeZoneInfo.ConvertTime(DateTime.UtcNow, zone);
        string value = FormatTime(time, Model.Format, clock24h ? "HH:mm" : "h:mm tt");
        string label = Model.Label.Length > 0 ? Model.Label : CityOf(zone);
        if (!_horizontal && label.Length > 6) label = label[..6];

        TimeSpan offset = zone.GetUtcOffset(DateTime.UtcNow) - TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
        string difference = offset == TimeSpan.Zero ? "same time as here"
            : (offset > TimeSpan.Zero ? "+" : "−") + (offset.Duration().Minutes == 0 ? $"{offset.Duration().Hours} h" : $"{offset.Duration():h\\:mm} h") + " from here";
        Show("\uE774", label, value, $"{time.ToString("dddd d MMMM, HH:mm", CultureInfo.CurrentCulture)}\n{zone.DisplayName}\n{difference}");
    }

    private void ShowUptime()
    {
        TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        string value = up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h" : up.TotalHours >= 1 ? $"{up.Hours}h {up.Minutes}m" : $"{up.Minutes}m";
        DateTime since = DateTime.Now - up;
        Show("\uE7E8", Model.Label, value, $"Windows has been running for {value}\nsince {since.ToString("g", CultureInfo.CurrentCulture)}");
    }

    private void ShowTimer()
    {
        string id = Model.Id;
        TimeSpan elapsed = WidgetTimers.Elapsed(id);
        bool running = WidgetTimers.IsRunning(id);
        double minutes = Math.Clamp(Model.Minutes, 0, 1440);
        string value, tooltip;
        if (minutes > 0)
        {
            TimeSpan total = TimeSpan.FromMinutes(minutes);
            if (running && elapsed >= total)
            {
                WidgetTimers.Finish(id, total);
                running = false;
                elapsed = total;
            }
            // Arrotondato per eccesso: alla partenza si legge 25:00, non 24:59.
            TimeSpan remaining = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, (total - elapsed).TotalSeconds)));
            value = Duration(remaining);
            tooltip = WidgetTimers.IsFinished(id) ? "Time's up!\nClick to start again · Right-click to reset"
                : running ? $"Countdown: {value} left\nClick to pause · Right-click to reset"
                : elapsed > TimeSpan.Zero ? $"Paused: {value} left\nClick to resume · Right-click to reset"
                : $"Countdown of {Duration(total)}\nClick to start";
        }
        else
        {
            value = Duration(TimeSpan.FromSeconds(Math.Floor(elapsed.TotalSeconds)));
            tooltip = running ? "Stopwatch\nClick to pause · Right-click to reset"
                : elapsed > TimeSpan.Zero ? "Stopwatch (paused)\nClick to resume · Right-click to reset" : "Stopwatch\nClick to start";
        }
        Show("\uE916", Model.Label, value, tooltip);

        // Finito: testo nel colore d'accento finché non si riparte o si azzera. In pausa: attenuato.
        bool alert = WidgetTimers.IsFinished(id);
        if (_text != null && alert != _alert)
        {
            _alert = alert;
            if (alert) _text.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            else SetBrush(_text, TextBlock.ForegroundProperty, Model.Color, "FgBrush");
        }
        if (_text != null) _text.Opacity = !running && !alert && elapsed > TimeSpan.Zero ? 0.6 : 1;
    }

    private void ShowDisk()
    {
        string root = DriveRoot(Model.Target);
        var drive = new DriveInfo(root);
        string name = DriveLabel(root);
        if (!drive.IsReady)
        {
            Show("\uEDA2", Model.Label, $"{name} –", $"{name} is not available");
            return;
        }
        long free = drive.AvailableFreeSpace, total = drive.TotalSize;
        int used = total > 0 ? (int)Math.Round(100.0 * (total - free) / total) : 0;
        string volume = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Drive" : drive.VolumeLabel;
        Show("\uEDA2", Model.Label, _horizontal ? $"{name} {Bytes((ulong)free)}" : $"{name}\n{Bytes((ulong)free, compact: true)}",
            $"{volume} ({name})\n{Bytes((ulong)free)} free of {Bytes((ulong)total)} ({used}% used)\nClick to open");
    }

    private void ShowShortcut()
    {
        string target = Model.Target.Trim();
        string glyph = target.Length == 0 ? "\uE710" : IsWebAddress(target) ? "\uE774" : "\uE8A7";
        string tooltip = target.Length == 0 ? "Shortcut: choose what it opens in Settings"
            : (Model.Label.Length > 0 ? Model.Label : TargetName(target)) + "\n" + target;
        // Senza icona né etichetta il collegamento sparirebbe: il glifo resta comunque.
        Show(glyph, Model.Label, "", tooltip, forceGlyph: Model.Label.Trim().Length == 0);
    }

    private async void LoadShortcutIcon()
    {
        if (_image == null) return;
        try
        {
            string? key = IconKey(Model.Target);
            if (key == null) return;
            ImageSource? icon = await IconService.GetAsync(key);
            if (icon == null || !Model.ShowIcon) return;
            _image.Source = icon;
            _image.Visibility = Visibility.Visible;
            _shown = "";
            ShowShortcut();
        }
        catch (Exception ex) { Log.Warn("Icona del collegamento: " + ex.Message); }
    }

    // ---- Azioni --------------------------------------------------------------------------------

    protected override void OnClick()
    {
        base.OnClick();
        try
        {
            switch (Model.Kind)
            {
                case ExtraWidgetKind.Shortcut: Launch(Model.Target); break;
                case ExtraWidgetKind.Timer: WidgetTimers.Toggle(Model.Id); break;
                case ExtraWidgetKind.Cpu:
                case ExtraWidgetKind.Memory:
                case ExtraWidgetKind.NetworkSpeed:
                    ShellHelper.StartTaskManager();
                    break;
                case ExtraWidgetKind.Battery: OpenUri("ms-settings:batterysaver"); break;
                case ExtraWidgetKind.Disk: OpenUri(DriveRoot(Model.Target)); break;
            }
        }
        catch (Exception ex) { Log.Error($"Widget {Model.Kind}", ex); }
    }

    /// <summary>Voci del menu del tasto destro proprie di questo widget (le comuni le aggiunge la barra).</summary>
    public IEnumerable<(string Text, string Glyph, Action Action)> MenuActions()
    {
        switch (Model.Kind)
        {
            case ExtraWidgetKind.Timer:
                bool running = WidgetTimers.IsRunning(Model.Id);
                yield return (running ? "Pause" : WidgetTimers.Elapsed(Model.Id) > TimeSpan.Zero && !WidgetTimers.IsFinished(Model.Id) ? "Resume" : "Start",
                    running ? "\uE769" : "\uE768", () => WidgetTimers.Toggle(Model.Id));
                if (running || WidgetTimers.Elapsed(Model.Id) > TimeSpan.Zero) yield return ("Reset", "\uE72C", () => WidgetTimers.Reset(Model.Id));
                break;
            case ExtraWidgetKind.Shortcut:
                if (Model.Target.Trim().Length == 0) break;
                yield return ("Open", "\uE8A7", () => Launch(Model.Target));
                string path = Environment.ExpandEnvironmentVariables(Model.Target.Trim().Trim('"'));
                if (File.Exists(path) || Directory.Exists(path))
                    yield return ("Open file location", "\uE8B7", () =>
                        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose());
                break;
            case ExtraWidgetKind.Cpu:
            case ExtraWidgetKind.Memory:
            case ExtraWidgetKind.NetworkSpeed:
                yield return ("Task Manager", "\uE9D9", ShellHelper.StartTaskManager);
                break;
            case ExtraWidgetKind.Battery:
                yield return ("Power and battery settings", "\uE83F", () => OpenUri("ms-settings:batterysaver"));
                break;
            case ExtraWidgetKind.Disk:
                yield return ("Open " + DriveLabel(Model.Target), "\uE8B7", () => OpenUri(DriveRoot(Model.Target)));
                yield return ("Storage settings", "\uEDA2", () => OpenUri("ms-settings:storagesense"));
                break;
            case ExtraWidgetKind.WorldClock:
                yield return ("Date and time settings", "\uE775", () => OpenUri("ms-settings:dateandtime"));
                break;
        }
    }

    /// <summary>Apre ciò che indica un collegamento: file, cartella, app, indirizzo web o comando con argomenti.</summary>
    public static void Launch(string rawTarget)
    {
        string target = Environment.ExpandEnvironmentVariables(rawTarget.Trim());
        if (target.Length == 0) return;
        try
        {
            string unquoted = target.Trim('"');
            ProcessStartInfo info;
            if (File.Exists(unquoted) || Directory.Exists(unquoted))
                info = new ProcessStartInfo(unquoted) { UseShellExecute = true, WorkingDirectory = File.Exists(unquoted) ? System.IO.Path.GetDirectoryName(unquoted) ?? "" : "" };
            else if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
                info = new ProcessStartInfo(target) { UseShellExecute = true }; // https:, ms-settings:, steam:...
            else
            {
                (string file, string args) = SplitCommand(target);
                info = new ProcessStartInfo(file, args) { UseShellExecute = true };
            }
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex) { Log.Error("Collegamento: " + target, ex); }
    }

    /// <summary>"app.exe --arg" oppure "\"C:\percorso con spazi\app.exe\" --arg" → file e argomenti.</summary>
    private static (string File, string Args) SplitCommand(string command)
    {
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0) return (command[1..end], command[(end + 1)..].Trim());
        }
        int space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }

    private static void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Log.Error("Apertura di " + uri, ex); }
    }

    // ---- Aiuti ---------------------------------------------------------------------------------

    private static bool IsWebAddress(string target) =>
        Uri.TryCreate(target.Trim(), UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Da dove prendere l'icona vera del collegamento (null = si usa un glifo).</summary>
    private static string? IconKey(string rawTarget)
    {
        string target = Environment.ExpandEnvironmentVariables(rawTarget.Trim());
        if (target.Length == 0 || IsWebAddress(target)) return null;
        string unquoted = target.Trim('"');
        if (File.Exists(unquoted) || Directory.Exists(unquoted)) return unquoted;
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return target;
        string file = SplitCommand(target).File;
        if (File.Exists(file)) return file;
        // Un comando come "notepad" o "wt": si cerca l'eseguibile nel PATH.
        string exe = System.IO.Path.HasExtension(file) ? file : file + ".exe";
        if (exe.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return null;
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = System.IO.Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// <summary>Nome leggibile di ciò che apre un collegamento: nome del file, della cartella o del sito.</summary>
    public static string TargetName(string rawTarget)
    {
        string target = rawTarget.Trim();
        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
            return uri.Host.Length > 0 ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host) : target;
        string path = SplitCommand(target).File.TrimEnd('\\', '/');
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        return name.Length > 0 ? name : target;
    }

    public static TimeZoneInfo FindZone(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception) { return TimeZoneInfo.Utc; }
    }

    /// <summary>
    /// Nome breve di un fuso, dal nome di Windows: la città che dà il nome al fuso se è nell'elenco
    /// ("Tokyo Standard Time", "Osaka, Sapporo, Tokyo" → "Tokyo"), altrimenti la prima ("Eastern Time (US &amp; Canada)" → "Eastern Time").
    /// </summary>
    public static string CityOf(TimeZoneInfo zone)
    {
        if (zone.Id is "UTC" or "Etc/UTC") return "UTC";
        string name = zone.DisplayName;
        int close = name.IndexOf(')');
        if (close >= 0 && close + 1 < name.Length) name = name[(close + 1)..];
        var places = name.Split(',')
            .Select(p => (p.IndexOf('(') is int paren and > 0 ? p[..paren] : p).Trim())
            .Where(p => p.Length > 0)
            .ToList();
        string zoneName = zone.Id.Replace(" Standard Time", "", StringComparison.OrdinalIgnoreCase).Trim();
        string city = places.FirstOrDefault(p => string.Equals(p, zoneName, StringComparison.OrdinalIgnoreCase))
                      ?? places.FirstOrDefault() ?? zone.Id;
        return city.Length > 18 ? city[..18].TrimEnd() + "…" : city;
    }

    public static string DriveRoot(string target)
    {
        string t = target.Trim();
        if (t.Length == 0) return System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        try { return System.IO.Path.GetPathRoot(t.Length == 1 ? t + ":\\" : t.Length == 2 && t[1] == ':' ? t + "\\" : t) ?? t; }
        catch (ArgumentException) { return t; }
    }

    /// <summary>"C:\" → "C:".</summary>
    public static string DriveLabel(string target) => DriveRoot(target).TrimEnd('\\', '/');

    private static bool FormatHasSeconds(string? format) =>
        !string.IsNullOrWhiteSpace(format) && (format.Trim().Length == 1 || format.IndexOfAny(new[] { 's', 'f', 'F' }) >= 0);

    private static string FormatTime(DateTime time, string custom, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(custom))
        {
            try { return time.ToString(custom, CultureInfo.CurrentCulture); }
            catch (FormatException) { }
        }
        return time.ToString(fallback, CultureInfo.CurrentCulture);
    }

    private static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    /// <summary>Velocità: "1.2 MB/s", oppure in forma breve "1.2M" per le barre verticali.</summary>
    private static string Rate(double bytesPerSecond, bool compact)
    {
        string[] units = compact ? new[] { "B", "K", "M", "G" } : new[] { "B/s", "KB/s", "MB/s", "GB/s" };
        double value = Math.Max(0, bytesPerSecond);
        int unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        string number = value.ToString(unit > 0 && value < 10 ? "0.0" : "0", CultureInfo.CurrentCulture);
        return compact ? number + units[unit] : number + " " + units[unit];
    }

    private static string Bytes(ulong bytes, bool compact = false)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        string number = value.ToString(value < 10 && unit > 0 ? "0.0" : "0", CultureInfo.CurrentCulture);
        return compact ? number + units[unit][0] : number + " " + units[unit];
    }
}
