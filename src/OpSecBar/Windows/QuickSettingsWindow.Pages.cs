using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OpSecBar.Core;
using OpSecBar.Services;
using Windows.Devices.Radios;

namespace OpSecBar.Windows;

// Pagine secondarie delle impostazioni rapide: scelta della rete Wi-Fi e dei dispositivi Bluetooth, senza passare dai
// pannelli di Windows. Le righe si ricostruiscono dallo stato (rete o dispositivo aperto, messaggi, operazione in corso).
public partial class QuickSettingsWindow
{
    private enum Page { Main, Wifi, Bluetooth, Sound }

    private Page _page = Page.Main;
    private bool _subWasOn;

    // Wi-Fi
    private static List<WifiNetwork> _wifiNetworks = new(); // ultimo elenco: riaprendo la pagina compare subito
    private static WifiListState _wifiState = WifiListState.Ok;
    private readonly HashSet<string> _wifiNeedsPassword = new(StringComparer.Ordinal);
    private string? _wifiOpen, _wifiMessage;
    private string _wifiPassword = "";
    private bool _wifiBusy, _wifiScanning;

    // Bluetooth
    private List<BluetoothItem> _btPaired = new();
    private BluetoothService.Scanner? _btScanner;
    private string? _btOpen, _btMessage;
    private bool _btBusy, _btConfirmRemove, _btRenderQueued, _btLoading;
    private PairingPrompt? _btPrompt;
    private TaskCompletionSource<string?>? _btAnswer;
    private string _btPin = "";

    // ---- Navigazione -----------------------------------------------------------------------------

    private void ShowWifiPage()
    {
        if (_radios.HasWifi) ShowPage(Page.Wifi);
        else OpenShell("ms-settings:network");
    }

    private void ShowPage(Page page)
    {
        if (_page != page) LeaveSubPage();
        _page = page;
        MainPage.Visibility = page == Page.Main ? Visibility.Visible : Visibility.Collapsed;
        SubPage.Visibility = page == Page.Main ? Visibility.Collapsed : Visibility.Visible;
        if (page == Page.Main) return;

        SubTitle.Text = page switch { Page.Wifi => "Wi-Fi", Page.Bluetooth => "Bluetooth", _ => "Sound" };
        SubMoreText.Text = page switch
        {
            Page.Wifi => "More Wi-Fi settings", Page.Bluetooth => "More Bluetooth settings", _ => "Volume mixer settings"
        };
        SubRefresh.Visibility = page == Page.Wifi ? Visibility.Visible : Visibility.Collapsed;
        SubSwitch.Visibility = page == Page.Sound ? Visibility.Collapsed : Visibility.Visible;
        _subWasOn = SubIsOn;
        UpdateSubSwitch();

        if (page == Page.Sound) LoadSound();
        else if (page == Page.Wifi)
        {
            RenderWifi();
            LoadWifi(scan: true);
        }
        else
        {
            RenderBluetooth();
            LoadBluetooth();
            StartBluetoothScan();
        }
    }

    /// <summary>Chiude ciò che la pagina secondaria teneva aperto (ricerca Bluetooth, richieste in sospeso).</summary>
    private void LeaveSubPage()
    {
        StopBluetoothScan();
        _btAnswer?.TrySetResult(null);
        _btAnswer = null;
        _btPrompt = null;
        _wifiOpen = _btOpen = _wifiMessage = _btMessage = null;
        _wifiPassword = _btPin = "";
        _btConfirmRemove = false;
        _mixerRows.Clear();
        _soundSignature = "";
    }

    private bool SubIsOn => _page == Page.Wifi ? _radios.WifiOn : _radios.BluetoothOn;

    private void UpdateSubSwitch()
    {
        bool on = SubIsOn;
        SubSwitch.Content = new TextBlock
        {
            Text = on ? "On" : "Off", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        SubSwitch.ToolTip = (on ? "Turn off " : "Turn on ") + (_page == Page.Wifi ? "Wi-Fi" : "Bluetooth");
        if (on)
        {
            SubSwitch.SetResourceReference(BackgroundProperty, "AccentBrush");
            SubSwitch.SetResourceReference(ForegroundProperty, "AccentFgBrush");
        }
        else
        {
            SubSwitch.ClearValue(BackgroundProperty);
            SubSwitch.ClearValue(ForegroundProperty);
        }
    }

    /// <summary>Lo stato delle radio è stato riletto: la pagina aperta segue l'accensione o lo spegnimento.</summary>
    private void OnRadiosApplied()
    {
        if (_page is Page.Main or Page.Sound) return;
        UpdateSubSwitch();
        bool on = SubIsOn;
        if (on == _subWasOn) return;
        _subWasOn = on;
        if (_page == Page.Wifi)
        {
            RenderWifi();
            if (on) LoadWifi(scan: true);
        }
        else
        {
            if (on) StartBluetoothScan(); else StopBluetoothScan();
            RenderBluetooth();
            LoadBluetooth();
        }
    }

    /// <summary>Aggiornamento periodico mentre il pannello è aperto.</summary>
    private void RefreshSubPage()
    {
        if (_page == Page.Bluetooth) LoadBluetooth();
        else if (_page == Page.Sound) LoadSound();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowPage(Page.Main);

    private void SubSwitch_Click(object sender, RoutedEventArgs e) =>
        ToggleRadio(_page == Page.Wifi ? RadioKind.WiFi : RadioKind.Bluetooth, !SubIsOn);

    private void SubRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_page == Page.Wifi) LoadWifi(scan: true);
    }

    private void SubMore_Click(object sender, RoutedEventArgs e) =>
        OpenShell(_page switch { Page.Wifi => "ms-settings:network-wifi", Page.Bluetooth => "ms-settings:bluetooth", _ => "ms-settings:apps-volume" });

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Esc torna prima alla pagina principale; da lì chiude il pannello (lo fa la base).
        if (e.Key == Key.Escape && _page != Page.Main)
        {
            ShowPage(Page.Main);
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    // ---- Mattoni delle righe ---------------------------------------------------------------------

    private void AddNote(string text) => SubList.Children.Add(Note(text, new Thickness(10, 10, 10, 6)));

    private static TextBlock Note(string text, Thickness margin) =>
        new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = margin }.Dim();

    private void AddHeader(string text) =>
        SubList.Children.Add(new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 8, 10, 4) }.Dim());

    private Button Pill(string text, Action click, bool primary = false, bool enabled = true)
    {
        var button = new Button
        {
            Style = (Style)FindResource("TileButton"), Height = 30, Margin = new Thickness(0, 0, 6, 0), IsEnabled = enabled,
            Tag = new CornerRadius(6),
            Content = new TextBlock { Text = text, FontSize = 12.5, Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center }
        };
        if (primary)
        {
            button.SetResourceReference(BackgroundProperty, "AccentBrush");
            button.SetResourceReference(ForegroundProperty, "AccentFgBrush");
        }
        button.Click += (_, _) => Safe(click);
        return button;
    }

    /// <summary>Campo di testo (o password) arrotondato come gli altri controlli del pannello, con il suggerimento finché è vuoto.</summary>
    private Border Field(Control box, string hint)
    {
        box.Background = System.Windows.Media.Brushes.Transparent;
        box.BorderThickness = new Thickness(0);
        box.VerticalContentAlignment = VerticalAlignment.Center;
        box.SetResourceReference(ForegroundProperty, "PanelFgBrush");

        TextBlock placeholder = new TextBlock { Text = hint, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) }.Dim();
        void Sync() => placeholder.Visibility =
            (box is PasswordBox p ? p.Password.Length : ((TextBox)box).Text.Length) == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (box is PasswordBox password) password.PasswordChanged += (_, _) => Sync();
        else ((TextBox)box).TextChanged += (_, _) => Sync();
        Sync();

        var content = new Grid();
        content.Children.Add(placeholder);
        content.Children.Add(box);
        var field = new Border { CornerRadius = new CornerRadius(6), Height = 32, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 0, 8), Child = content };
        field.SetResourceReference(Border.BackgroundProperty, "PanelPressedBrush");
        return field;
    }

    /// <summary>
    /// Riga con icona, nome e stato. Se <paramref name="details"/> non è null la riga è "aperta": resta evidenziata e
    /// mostra sotto i suoi comandi.
    /// </summary>
    private FrameworkElement Row(string glyph, string title, string subtitle, bool strong, string? trailingGlyph, Action click, FrameworkElement? details)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock icon = GlyphBlock(glyph, 16);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal });
        if (subtitle.Length > 0)
            text.Children.Add(new TextBlock { Text = subtitle, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis }.Dim());
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (trailingGlyph != null)
        {
            TextBlock trailing = GlyphBlock(trailingGlyph, 11).Dim();
            trailing.Margin = new Thickness(8, 0, 2, 0);
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }

        var header = new Button { Style = (Style)FindResource("RowButton"), Content = grid };
        header.Click += (_, _) => Safe(click);
        if (details == null) return header;

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(details);
        var open = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 2, 0, 2), Child = panel };
        open.SetResourceReference(Border.BackgroundProperty, "PanelHoverBrush");
        return open;
    }

    private static StackPanel Details() => new() { Margin = new Thickness(40, 0, 10, 10) };

    private static StackPanel Buttons() => new() { Orientation = Orientation.Horizontal };

    // ---- Wi-Fi -----------------------------------------------------------------------------------

    private async void LoadWifi(bool scan)
    {
        if (_wifiScanning) return;
        _wifiScanning = true;
        try
        {
            // Prima l'elenco che Windows ha già (immediato), poi una nuova ricerca che lo aggiorna.
            (_wifiState, _wifiNetworks) = await WifiService.ListAsync(scan: false);
            if (_page == Page.Wifi) RenderWifi();
            if (scan && _radios.WifiOn && _wifiState == WifiListState.Ok)
            {
                (_wifiState, _wifiNetworks) = await WifiService.ListAsync(scan: true);
            }
        }
        catch (Exception ex) { Log.Error("Elenco reti Wi-Fi", ex); }
        finally
        {
            _wifiScanning = false;
            if (_page == Page.Wifi) RenderWifi();
        }
    }

    private static string SignalGlyph(int bars) => bars switch { <= 1 => "", 2 => "", 3 => "", _ => "" };

    private void RenderWifi()
    {
        if (_page != Page.Wifi) return;
        SubList.Children.Clear();
        if (!_radios.WifiOn)
        {
            AddNote("Wi-Fi is off.");
            return;
        }
        if (_wifiState == WifiListState.AccessDenied)
        {
            AddNote("Windows isn't letting apps see Wi-Fi networks. Turn on location access to list them here.");
            var buttons = Buttons();
            buttons.Margin = new Thickness(10, 4, 0, 0);
            buttons.Children.Add(Pill("Open location settings", () => OpenShell("ms-settings:privacy-location")));
            SubList.Children.Add(buttons);
            return;
        }
        if (_wifiNetworks.Count == 0)
        {
            AddNote(_wifiScanning ? "Searching for networks…" : "No networks found.");
            return;
        }

        PasswordBox? focus = null;
        foreach (WifiNetwork network in _wifiNetworks)
        {
            bool open = network.Ssid == _wifiOpen;
            string status = network.Connected ? "Connected" : network.Saved ? "Saved" : network.Enterprise ? "Needs sign-in" : network.Secured ? "Secured" : "Open";
            SubList.Children.Add(Row(SignalGlyph(network.Bars), network.Ssid, status, network.Connected, network.Secured ? "" : null,
                () => OpenWifi(open ? null : network.Ssid), open ? WifiDetails(network, out focus) : null));
        }
        if (_wifiScanning) AddNote("Searching…");
        if (focus != null) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { focus.Focus(); Keyboard.Focus(focus); });
    }

    private void OpenWifi(string? ssid)
    {
        if (_wifiBusy) return;
        _wifiOpen = ssid;
        _wifiMessage = null;
        _wifiPassword = "";
        RenderWifi();
    }

    private FrameworkElement WifiDetails(WifiNetwork network, out PasswordBox? focus)
    {
        focus = null;
        StackPanel details = Details();
        if (_wifiMessage != null) details.Children.Add(Note(_wifiMessage, new Thickness(0, 0, 0, 8)));
        if (_wifiBusy) return details;

        StackPanel buttons = Buttons();
        if (network.Connected)
        {
            buttons.Children.Add(Pill("Disconnect", () => DisconnectWifi()));
        }
        else if (network.Enterprise)
        {
            details.Children.Add(Note("This network asks for a work or school sign-in.", new Thickness(0, 0, 0, 8)));
            buttons.Children.Add(Pill("Connect in Windows", () => OpenShell("ms-availablenetworks:")));
        }
        else if (network.Secured && (!network.Saved || _wifiNeedsPassword.Contains(network.Ssid)))
        {
            var box = new PasswordBox { Password = _wifiPassword, ToolTip = "Network password" };
            Button connect = Pill("Connect", () => ConnectWifi(network, _wifiPassword), primary: true, enabled: _wifiPassword.Length > 0);
            box.PasswordChanged += (_, _) =>
            {
                _wifiPassword = box.Password;
                connect.IsEnabled = _wifiPassword.Length > 0;
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter || _wifiPassword.Length == 0) return;
                e.Handled = true;
                ConnectWifi(network, _wifiPassword);
            };
            details.Children.Add(Field(box, "Password"));
            buttons.Children.Add(connect);
            focus = box;
        }
        else
        {
            buttons.Children.Add(Pill("Connect", () => ConnectWifi(network, null), primary: true));
        }
        details.Children.Add(buttons);
        return details;
    }

    private async void ConnectWifi(WifiNetwork network, string? password)
    {
        if (_wifiBusy) return;
        _wifiBusy = true;
        _wifiMessage = "Connecting…";
        RenderWifi();

        WifiConnectOutcome outcome = await WifiService.ConnectAsync(network, password);
        _wifiBusy = false;
        switch (outcome)
        {
            case WifiConnectOutcome.Connected:
                _wifiNeedsPassword.Remove(network.Ssid);
                _wifiOpen = _wifiMessage = null;
                _wifiPassword = "";
                break;
            case WifiConnectOutcome.NeedsPassword:
                _wifiNeedsPassword.Add(network.Ssid);
                _wifiMessage = "Enter the network password.";
                break;
            case WifiConnectOutcome.WrongPassword:
                _wifiNeedsPassword.Add(network.Ssid);
                _wifiMessage = "That password didn't work.";
                break;
            case WifiConnectOutcome.TimedOut:
                _wifiMessage = "The network didn't answer in time.";
                break;
            default:
                _wifiMessage = "Couldn't connect to this network.";
                break;
        }
        RenderWifi();
        LoadWifi(scan: false);
        ReadRadios();
    }

    private async void DisconnectWifi()
    {
        if (_wifiBusy) return;
        _wifiBusy = true;
        _wifiMessage = "Disconnecting…";
        RenderWifi();
        await WifiService.DisconnectAsync();
        await Task.Delay(700); // il tempo che Windows aggiorni lo stato della scheda
        _wifiBusy = false;
        _wifiOpen = _wifiMessage = null;
        RenderWifi();
        LoadWifi(scan: false);
        ReadRadios();
    }

    // ---- Bluetooth -------------------------------------------------------------------------------

    private void StartBluetoothScan()
    {
        if (_btScanner != null || !_radios.BluetoothOn) return;
        try
        {
            _btScanner = new BluetoothService.Scanner();
            _btScanner.Changed += OnBluetoothFound;
        }
        catch (Exception ex) { Log.Warn("Ricerca Bluetooth: " + ex.Message); }
    }

    private void StopBluetoothScan()
    {
        if (_btScanner == null) return;
        _btScanner.Changed -= OnBluetoothFound;
        _btScanner.Dispose();
        _btScanner = null;
    }

    /// <summary>Arriva da un thread qualsiasi e anche molte volte al secondo: si ridisegna al massimo ogni mezzo secondo.</summary>
    private void OnBluetoothFound()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_btRenderQueued) return;
            _btRenderQueued = true;
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                _btRenderQueued = false;
                // Mentre si digita un PIN l'elenco resta fermo: ricostruirlo toglierebbe il fuoco al campo.
                if (_btPrompt?.Kind != PairingPromptKind.EnterPin) RenderBluetooth();
            };
            delay.Start();
        });
    }

    private async void LoadBluetooth()
    {
        if (_btLoading || _btBusy || _btPrompt != null) return;
        _btLoading = true;
        try
        {
            _btPaired = _radios.BluetoothOn ? await BluetoothService.PairedAsync() : new List<BluetoothItem>();
            if (!_btBusy && _btPrompt == null) RenderBluetooth();
        }
        catch (Exception ex) { Log.Error("Elenco dispositivi Bluetooth", ex); }
        finally { _btLoading = false; }
    }

    private void RenderBluetooth()
    {
        if (_page != Page.Bluetooth) return;
        SubList.Children.Clear();
        if (!_radios.BluetoothOn)
        {
            AddNote("Bluetooth is off.");
            return;
        }

        TextBox? focus = null;
        if (_btPaired.Count > 0) AddHeader("Your devices");
        foreach (BluetoothItem device in _btPaired)
        {
            bool open = device.Key == _btOpen;
            string status = device.Connected ? "Connected" : "Paired";
            SubList.Children.Add(Row(device.Audio ? "" : "", device.Name, status, device.Connected, null,
                () => OpenBluetooth(open ? null : device.Key), open ? PairedDetails(device) : null));
        }

        var known = new HashSet<string>(_btPaired.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);
        List<BluetoothItem> nearby = (_btScanner?.Found() ?? new List<BluetoothItem>()).Where(d => !known.Contains(d.Key)).ToList();
        AddHeader("Nearby devices");
        foreach (BluetoothItem device in nearby)
        {
            bool open = device.Key == _btOpen;
            SubList.Children.Add(Row("", device.Name, "Not paired", false, null,
                () => OpenBluetooth(open ? null : device.Key), open ? NearbyDetails(device, out focus) : null));
        }
        AddNote(nearby.Count == 0 ? "Searching… Put the device in pairing mode to see it here." : "Searching…");
        if (focus != null) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { focus.Focus(); Keyboard.Focus(focus); });
    }

    private void OpenBluetooth(string? key)
    {
        if (_btBusy || _btPrompt != null) return;
        _btOpen = key;
        _btMessage = null;
        _btConfirmRemove = false;
        RenderBluetooth();
    }

    private FrameworkElement PairedDetails(BluetoothItem device)
    {
        StackPanel details = Details();
        if (_btMessage != null) details.Children.Add(Note(_btMessage, new Thickness(0, 0, 0, 8)));
        if (_btBusy) return details;

        StackPanel buttons = Buttons();
        if (_btConfirmRemove)
        {
            details.Children.Add(Note($"Remove {device.Name}? You'll have to pair it again to use it.", new Thickness(0, 0, 0, 8)));
            buttons.Children.Add(Pill("Remove", () => RemoveBluetooth(device), primary: true));
            buttons.Children.Add(Pill("Cancel", () => { _btConfirmRemove = false; RenderBluetooth(); }));
        }
        else
        {
            if (device.Audio) buttons.Children.Add(Pill(device.Connected ? "Disconnect" : "Connect", () => ConnectBluetoothAudio(device, !device.Connected), primary: !device.Connected));
            else if (!device.Connected) details.Children.Add(Note("It connects by itself when it's switched on and in range.", new Thickness(0, 0, 0, 8)));
            buttons.Children.Add(Pill("Remove", () => { _btConfirmRemove = true; _btMessage = null; RenderBluetooth(); }));
        }
        details.Children.Add(buttons);
        return details;
    }

    private FrameworkElement NearbyDetails(BluetoothItem device, out TextBox? focus)
    {
        focus = null;
        StackPanel details = Details();
        StackPanel buttons = Buttons();

        if (_btPrompt is { } prompt)
        {
            switch (prompt.Kind)
            {
                case PairingPromptKind.ShowPin:
                    details.Children.Add(Note($"Type {prompt.Pin} on {prompt.DeviceName}, then press Enter on it.", new Thickness(0, 0, 0, 2)));
                    return details;
                case PairingPromptKind.ConfirmPin:
                    details.Children.Add(Note($"Does {prompt.DeviceName} show this PIN?  {prompt.Pin}", new Thickness(0, 0, 0, 8)));
                    buttons.Children.Add(Pill("Yes", () => AnswerPrompt("yes"), primary: true));
                    buttons.Children.Add(Pill("No", () => AnswerPrompt(null)));
                    break;
                default:
                    details.Children.Add(Note($"Enter the PIN for {prompt.DeviceName}.", new Thickness(0, 0, 0, 8)));
                    var box = new TextBox { Text = _btPin };
                    box.SetResourceReference(TextBox.CaretBrushProperty, "PanelFgBrush");
                    box.TextChanged += (_, _) => _btPin = box.Text;
                    box.KeyDown += (_, e) =>
                    {
                        if (e.Key != Key.Enter) return;
                        e.Handled = true;
                        AnswerPrompt(_btPin);
                    };
                    details.Children.Add(Field(box, "PIN"));
                    buttons.Children.Add(Pill("Pair", () => AnswerPrompt(_btPin), primary: true));
                    buttons.Children.Add(Pill("Cancel", () => AnswerPrompt(null)));
                    focus = box;
                    break;
            }
            details.Children.Add(buttons);
            return details;
        }

        if (_btMessage != null) details.Children.Add(Note(_btMessage, new Thickness(0, 0, 0, 8)));
        if (_btBusy) return details;
        buttons.Children.Add(Pill("Pair", () => PairBluetooth(device), primary: true));
        details.Children.Add(buttons);
        return details;
    }

    private void AnswerPrompt(string? answer)
    {
        TaskCompletionSource<string?>? pending = _btAnswer;
        _btAnswer = null;
        _btPrompt = null;
        _btPin = "";
        _btMessage = "Pairing…";
        RenderBluetooth();
        pending?.TrySetResult(answer);
    }

    /// <summary>Chiamata dal servizio (da un thread qualsiasi) quando l'associazione ha bisogno dell'utente.</summary>
    private Task<string?> AskPairing(PairingPrompt prompt) =>
        Dispatcher.InvokeAsync(() =>
        {
            _btPrompt = prompt;
            Task<string?> answer;
            if (prompt.Kind == PairingPromptKind.ShowPin) answer = Task.FromResult<string?>("shown");
            else
            {
                _btAnswer = new TaskCompletionSource<string?>();
                answer = _btAnswer.Task;
            }
            RenderBluetooth();
            return answer;
        }).Task.Unwrap();

    private async void PairBluetooth(BluetoothItem device)
    {
        if (_btBusy) return;
        _btBusy = true;
        _btMessage = "Pairing…";
        RenderBluetooth();

        (bool ok, string? error) = await BluetoothService.PairAsync(device, AskPairing);
        _btBusy = false;
        _btPrompt = null;
        _btAnswer = null;
        _btMessage = ok ? null : error + ".";
        if (ok) _btOpen = null;
        RenderBluetooth();
        LoadBluetooth();
        ReadRadios();
    }

    private async void ConnectBluetoothAudio(BluetoothItem device, bool connect)
    {
        if (_btBusy) return;
        _btBusy = true;
        _btMessage = connect ? "Connecting…" : "Disconnecting…";
        RenderBluetooth();

        bool sent = await BluetoothService.SetAudioConnectedAsync(device, connect);
        bool done = false;
        // Il comando parte subito, il collegamento vero richiede qualche secondo: si attende che lo stato cambi.
        for (int i = 0; sent && !done && i < 10 && _page == Page.Bluetooth; i++)
        {
            await Task.Delay(1000);
            List<BluetoothItem> paired = await BluetoothService.PairedAsync();
            _btPaired = paired;
            done = paired.FirstOrDefault(d => d.Key == device.Key)?.Connected == connect;
        }

        _btBusy = false;
        _btMessage = done ? null
            : connect ? "Couldn't connect. Make sure the device is on and in range."
            : "Couldn't disconnect the device.";
        RenderBluetooth();
        ReadRadios();
    }

    private async void RemoveBluetooth(BluetoothItem device)
    {
        if (_btBusy) return;
        _btBusy = true;
        _btConfirmRemove = false;
        _btMessage = "Removing…";
        RenderBluetooth();

        bool ok = await BluetoothService.RemoveAsync(device);
        _btBusy = false;
        _btMessage = ok ? null : "Couldn't remove the device.";
        if (ok) _btOpen = null;
        RenderBluetooth();
        LoadBluetooth();
    }
}
