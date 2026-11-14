using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using OpSecBar.Core;
using OpSecBar.Services;
using Windows.Devices.Radios;

namespace OpSecBar.Windows;

/// <summary>
/// Impostazioni rapide: Wi-Fi, Bluetooth, modalità aereo, accessibilità, risparmio energia, sottotitoli in tempo reale,
/// volume e dispositivo di uscita. Le tessere che Windows non lascia comandare da un'app aprono la pagina giusta delle
/// Impostazioni (e lo dicono con la piccola freccia nell'angolo).
/// </summary>
public partial class QuickSettingsWindow : FlyoutWindow
{
    private sealed class Tile
    {
        public required FrameworkElement Root { get; init; }
        public required Button Main { get; init; }
        public Button? More { get; init; }
        public required TextBlock Glyph { get; init; }
        public required TextBlock Label { get; init; }
    }

    private static readonly string CaptionsExe = System.IO.Path.Combine(Environment.SystemDirectory, "LiveCaptions.exe");
    private static RadioSnapshot _radios = new() { HasWifi = true, HasBluetooth = true }; // ultimo stato letto: riaprendo è subito giusto

    private readonly Tile _wifi, _bluetooth, _airplane, _energy;
    private readonly Tile? _captions;
    private readonly DispatcherTimer _timer;
    private bool _syncingVolume, _reading, _readAgain;

    public QuickSettingsWindow(App app) : base(app)
    {
        InitializeComponent();

        _wifi = AddTile("", "Wi-Fi", ToggleWifi, more: ShowWifiPage, moreTip: "Show available networks");
        _bluetooth = AddTile("", "Bluetooth", () => ToggleRadio(RadioKind.Bluetooth, !_radios.BluetoothOn),
            more: () => ShowPage(Page.Bluetooth), moreTip: "Bluetooth devices");
        _airplane = AddTile("", "Airplane mode", ToggleAirplane);
        Tile accessibility = AddTile("", "Accessibility", () => { }, corner: "");
        accessibility.Main.Click += (_, _) => ShowAccessibilityMenu(accessibility.Main);
        _energy = AddTile("", "Energy saver", () => OpenShell("ms-settings:batterysaver"), corner: "",
            tip: "Open the energy saver settings");
        if (System.IO.File.Exists(CaptionsExe)) _captions = AddTile("", "Live captions", ToggleCaptions);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => RefreshAll();
        ApplyRadios();
    }

    /// <summary>Legge lo stato delle radio prima che il pannello venga aperto, così la prima apertura è già giusta.</summary>
    public static async void Preload()
    {
        try { _radios = await RadioService.ReadAsync(); }
        catch (Exception ex) { Log.Warn("Stato iniziale delle radio: " + ex.Message); }
    }

    // ---- Apertura --------------------------------------------------------------------------------

    protected override void OnOpening()
    {
        ShowPage(Page.Main);
        App.Audio.Changed -= UpdateVolume;
        App.Audio.Changed += UpdateVolume;
        RefreshAll();
    }

    protected override void OnOpened() => _timer.Start();

    protected override void OnHidden()
    {
        _timer.Stop();
        App.Audio.Changed -= UpdateVolume;
        LeaveSubPage();
    }

    private void RefreshAll()
    {
        App.Audio.PollNow();
        UpdateVolume();
        UpdatePower();
        if (_captions != null) SetOn(_captions, CaptionsRunning());
        ReadRadios();
        RefreshSubPage();
    }

    // ---- Tessere ---------------------------------------------------------------------------------

    private TextBlock GlyphBlock(string glyph, double size) =>
        new() { Style = (Style)FindResource("Glyph"), Text = glyph, FontSize = size };

    /// <param name="more">Seconda metà della tessera (freccia): apre l'elenco o la pagina relativa.</param>
    /// <param name="corner">Piccolo glifo nell'angolo: la tessera apre un menu o una pagina invece di commutare.</param>
    private Tile AddTile(string glyph, string label, Action click, Action? more = null, string? moreTip = null,
        string? corner = null, string? tip = null)
    {
        var style = (Style)FindResource("TileButton");
        TextBlock icon = GlyphBlock(glyph, 17);
        var content = new Grid();
        content.Children.Add(icon);
        if (corner != null)
        {
            TextBlock mark = GlyphBlock(corner, 9);
            mark.HorizontalAlignment = HorizontalAlignment.Right;
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(0, 7, 8, 0);
            mark.Opacity = 0.65;
            content.Children.Add(mark);
        }

        var main = new Button { Style = style, Content = content, ToolTip = tip ?? label };
        main.Click += (_, _) => Safe(click);

        var surface = new Grid { Margin = new Thickness(4, 0, 4, 0) };
        Button? moreButton = null;
        if (more != null)
        {
            surface.ColumnDefinitions.Add(new ColumnDefinition());
            surface.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            main.Tag = new CornerRadius(8, 0, 0, 8);
            moreButton = new Button
            {
                Style = style, Tag = new CornerRadius(0, 8, 8, 0), Margin = new Thickness(1, 0, 0, 0),
                Content = GlyphBlock("", 10), ToolTip = moreTip
            };
            moreButton.Click += (_, _) => Safe(more);
            Grid.SetColumn(moreButton, 1);
            surface.Children.Add(moreButton);
        }
        surface.Children.Add(main);

        var text = new TextBlock
        {
            Text = label, FontSize = 12, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 6, 4, 0)
        };
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        root.Children.Add(surface);
        root.Children.Add(text);
        Tiles.Children.Add(root);
        return new Tile { Root = root, Main = main, More = moreButton, Glyph = icon, Label = text };
    }

    private static void SetOn(Tile tile, bool on)
    {
        foreach (Button? button in new[] { tile.Main, tile.More })
        {
            if (button == null) continue;
            if (on)
            {
                button.SetResourceReference(BackgroundProperty, "AccentBrush");
                button.SetResourceReference(ForegroundProperty, "AccentFgBrush");
            }
            else
            {
                button.ClearValue(BackgroundProperty);
                button.ClearValue(ForegroundProperty);
            }
        }
    }

    private static void Safe(Action action)
    {
        try { action(); } catch (Exception ex) { Log.Error("Impostazioni rapide", ex); }
    }

    // ---- Wi-Fi, Bluetooth, modalità aereo --------------------------------------------------------

    private async void ReadRadios()
    {
        if (_reading)
        {
            _readAgain = true;
            return;
        }
        _reading = true;
        try
        {
            do
            {
                _readAgain = false;
                _radios = await RadioService.ReadAsync();
                ApplyRadios();
            } while (_readAgain);
        }
        catch (Exception ex) { Log.Error("Lettura delle radio", ex); }
        finally { _reading = false; }
    }

    private void ApplyRadios()
    {
        RadioSnapshot r = _radios;
        if (r.HasWifi)
        {
            _wifi.Glyph.Text = "";
            _wifi.Label.Text = r.WifiNetwork ?? "Wi-Fi";
            _wifi.Main.ToolTip = r.WifiOn ? "Turn Wi-Fi off" : "Turn Wi-Fi on";
            SetOn(_wifi, r.WifiOn);
        }
        else
        {
            // Nessuna scheda Wi-Fi (PC collegato via cavo): la tessera mostra la rete e apre le sue impostazioni.
            _wifi.Glyph.Text = "";
            _wifi.Label.Text = r.Online ? r.WiredNetwork ?? "Network" : "Not connected";
            _wifi.Main.ToolTip = "Network settings";
            SetOn(_wifi, r.Online);
        }

        _bluetooth.Root.Visibility = r.HasBluetooth ? Visibility.Visible : Visibility.Collapsed;
        _bluetooth.Label.Text = !r.BluetoothOn ? "Bluetooth" : r.BluetoothDevice ?? "Not connected";
        _bluetooth.Main.ToolTip = r.BluetoothOn ? "Turn Bluetooth off" : "Turn Bluetooth on";
        SetOn(_bluetooth, r.BluetoothOn);

        SetOn(_airplane, r.AirplaneMode);
        OnRadiosApplied();
    }

    private void ToggleWifi()
    {
        if (_radios.HasWifi) ToggleRadio(RadioKind.WiFi, !_radios.WifiOn);
        else OpenShell("ms-settings:network");
    }

    private async void ToggleRadio(RadioKind kind, bool on)
    {
        try
        {
            // La tessera cambia subito; lo stato vero la conferma (o la riporta indietro) alla lettura successiva.
            _radios = kind == RadioKind.WiFi ? _radios with { WifiOn = on, WifiNetwork = null } : _radios with { BluetoothOn = on, BluetoothDevice = null };
            ApplyRadios();
            await RadioService.SetAsync(kind, on);
        }
        catch (Exception ex) { Log.Error($"Radio {kind}", ex); }
        ReadRadios();
    }

    private async void ToggleAirplane()
    {
        bool on = !_radios.AirplaneMode;
        try
        {
            _radios = _radios with { AirplaneMode = on };
            ApplyRadios();
            await RadioService.SetAirplaneModeAsync(on);
        }
        catch (Exception ex) { Log.Error("Modalità aereo", ex); }
        ReadRadios();
    }

    // ---- Accessibilità, energia, sottotitoli -----------------------------------------------------

    private void ShowAccessibilityMenu(FrameworkElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        menu.Items.Add(Ui.MenuEntry("Magnifier", "", () => OpenShell("magnify.exe")));
        menu.Items.Add(Ui.MenuEntry("Narrator", "", () => OpenShell("narrator.exe")));
        menu.Items.Add(Ui.MenuEntry("On-screen keyboard", "", () => OpenShell("osk.exe")));
        menu.Items.Add(Ui.MenuEntry("Color filters", "", () => OpenShell("ms-settings:easeofaccess-colorfilter")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.MenuEntry("Accessibility settings", "", () => OpenShell("ms-settings:easeofaccess")));
        menu.IsOpen = true;
    }

    private void UpdatePower()
    {
        if (!Native.GetSystemPowerStatus(out Native.SYSTEM_POWER_STATUS power)) return;
        SetOn(_energy, power.SystemStatusFlag == 1);

        bool hasBattery = (power.BatteryFlag & 128) == 0 && power.BatteryFlag != 255 && power.BatteryLifePercent <= 100;
        BatteryPanel.Visibility = hasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (!hasBattery) return;
        int level = power.BatteryLifePercent / 10; // 0..10
        bool charging = power.ACLineStatus == 1;
        // Glifi "Battery0..10" e "BatteryCharging0..10" di Segoe: i primi nove sono consecutivi, gli ultimi no.
        int code = charging
            ? level switch { 10 => 0xEA93, 9 => 0xE83E, _ => 0xE85A + level }
            : level == 10 ? 0xE83F : 0xE850 + level;
        BatteryGlyph.Text = char.ConvertFromUtf32(code);
        BatteryText.Text = power.BatteryLifePercent.ToString(CultureInfo.CurrentCulture) + "%" + (charging ? " · plugged in" : "");
    }

    private static bool CaptionsRunning()
    {
        Process[] processes = Process.GetProcessesByName("LiveCaptions");
        foreach (Process p in processes) p.Dispose();
        return processes.Length > 0;
    }

    private void ToggleCaptions()
    {
        if (_captions == null) return;
        Process[] running = Process.GetProcessesByName("LiveCaptions");
        if (running.Length == 0)
        {
            OpenShell(CaptionsExe);
            return;
        }

        bool closed = true;
        foreach (Process p in running)
        {
            try
            {
                if (!p.CloseMainWindow()) p.Kill();
            }
            catch { closed = false; } // Windows protegge il processo: lo chiude l'utente dalla sua finestra
            finally { p.Dispose(); }
        }
        if (closed) SetOn(_captions, false);
        else OpenShell(CaptionsExe); // lo porta in primo piano
    }

    // ---- Volume ----------------------------------------------------------------------------------

    private void UpdateVolume()
    {
        AudioService a = App.Audio;
        int v = Math.Max(0, a.Volume);
        MuteGlyph.Text = !a.Available || a.Muted || v == 0 ? "" : v < 34 ? "" : v < 67 ? "" : "";
        MuteButton.ToolTip = a.Muted ? "Unmute" : "Mute";
        VolumeValue.Text = v.ToString(CultureInfo.CurrentCulture);
        VolumeSlider.IsEnabled = MuteButton.IsEnabled = a.Available;
        _syncingVolume = true;
        VolumeSlider.Value = v;
        _syncingVolume = false;
    }

    private void Mute_Click(object sender, RoutedEventArgs e) => App.Audio.ToggleMute();

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        VolumeValue.Text = Math.Round(e.NewValue).ToString(CultureInfo.CurrentCulture);
        if (!_syncingVolume) App.Audio.SetVolume((int)Math.Round(e.NewValue));
    }

    private void Volume_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        App.Audio.SetVolume(Math.Max(0, App.Audio.Volume) + (e.Delta > 0 ? 2 : -2));
    }

    private void Output_Click(object sender, RoutedEventArgs e) => ShowPage(Page.Sound);

    /// <summary>Elenco rapido dei dispositivi di uscita (clic destro sulla freccia del volume).</summary>
    private void Output_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = OutputButton, Placement = PlacementMode.Bottom };
        foreach (AudioService.AudioDevice device in App.Audio.GetOutputDevices())
        {
            menu.Items.Add(Ui.MenuEntry(device.Name, device.IsDefault ? "" : "", () =>
            {
                if (!device.IsDefault) App.Audio.SetDefaultOutput(device.Id);
                UpdateVolume();
            }));
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(Ui.MenuEntry("Sound settings", "", () => OpenShell("ms-settings:sound")));
        menu.IsOpen = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenShell("ms-settings:");
}
