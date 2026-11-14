using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpSecBar.Core;
using OpSecBar.Services;

namespace OpSecBar.Windows;

// Pagina "Sound" delle impostazioni rapide: dispositivo di uscita e volume di ogni app (il mixer di Windows).
// L'elenco si ricostruisce solo quando cambiano dispositivi o app; negli aggiornamenti periodici i cursori si muovono
// sul posto, così un trascinamento in corso non viene interrotto.
public partial class QuickSettingsWindow
{
    private sealed class MixerRow
    {
        public required Slider Slider { get; init; }
        public required TextBlock Value { get; init; }
        public required TextBlock MuteGlyph { get; init; }
        public required Button Mute { get; init; }
        public bool Syncing { get; set; }
        public bool Muted { get; set; }
    }

    private readonly Dictionary<string, MixerRow> _mixerRows = new(StringComparer.OrdinalIgnoreCase);
    private string _soundSignature = "";

    private static string VolumeGlyph(int volume, bool muted) =>
        muted || volume == 0 ? "" : volume < 34 ? "" : volume < 67 ? "" : "";

    private void LoadSound()
    {
        if (_page != Page.Sound) return;
        List<AudioService.AudioDevice> outputs;
        List<AppAudioSession> apps;
        try
        {
            outputs = App.Audio.GetOutputDevices();
            apps = AppVolumeService.ListSessions();
        }
        catch (Exception ex)
        {
            Log.Error("Mixer", ex);
            return;
        }

        string signature = string.Join("|", outputs.Select(o => o.Id + (o.IsDefault ? "*" : ""))) + "#" +
                           string.Join("|", apps.Select(a => a.Key + ":" + a.Name));
        if (signature != _soundSignature)
        {
            _soundSignature = signature;
            BuildSound(outputs, apps);
        }
        foreach (AppAudioSession app in apps)
            if (_mixerRows.TryGetValue(app.Key, out MixerRow? row)) ShowLevel(row, app.Volume, app.Muted);
    }

    private void BuildSound(List<AudioService.AudioDevice> outputs, List<AppAudioSession> apps)
    {
        SubList.Children.Clear();
        _mixerRows.Clear();

        AddHeader("Volume mixer");
        if (apps.Count == 0) AddNote("No apps are using sound right now.");
        foreach (AppAudioSession app in apps) SubList.Children.Add(MixerRowFor(app));

        AddHeader("Output");
        if (outputs.Count == 0) AddNote("No sound output found.");
        foreach (AudioService.AudioDevice device in outputs)
        {
            SubList.Children.Add(Row("", device.Name, "", device.IsDefault,
                device.IsDefault ? "" : null, () => ChooseOutput(device), null));
        }
    }

    private void ChooseOutput(AudioService.AudioDevice device)
    {
        if (device.IsDefault) return;
        if (!App.Audio.SetDefaultOutput(device.Id)) AddNote("Windows didn't switch to that device.");
        UpdateVolume();
        _soundSignature = ""; // le sessioni delle app ora sono quelle del nuovo dispositivo
        LoadSound();
    }

    private FrameworkElement MixerRowFor(AppAudioSession app)
    {
        var grid = new Grid { Margin = new Thickness(10, 6, 6, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Icona dell'app (o glifo per i suoni di sistema)
        FrameworkElement icon;
        if (app.IconKey is { } iconKey)
        {
            var image = new Image { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);
            LoadIcon(image, iconKey);
            icon = image;
        }
        else
        {
            TextBlock glyph = GlyphBlock("", 18);
            glyph.HorizontalAlignment = HorizontalAlignment.Left;
            icon = glyph;
        }
        Grid.SetRowSpan(icon, 2);
        grid.Children.Add(icon);

        var name = new TextBlock
        {
            Text = app.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 2),
            FontWeight = app.Active ? FontWeights.SemiBold : FontWeights.Normal
        };
        Grid.SetColumn(name, 1);
        Grid.SetColumnSpan(name, 2);
        grid.Children.Add(name);

        var slider = new Slider
        {
            Style = (Style)FindResource("SeekSlider"), Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetRow(slider, 1);
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var value = new TextBlock { Width = 28, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        TextBlock muteGlyph = GlyphBlock(VolumeGlyph(app.Volume, app.Muted), 14);
        var mute = new Button { Style = (Style)FindResource("PanelButton"), Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), Content = muteGlyph };
        right.Children.Add(value);
        right.Children.Add(mute);
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var row = new MixerRow { Slider = slider, Value = value, MuteGlyph = muteGlyph, Mute = mute };
        _mixerRows[app.Key] = row;
        ShowLevel(row, app.Volume, app.Muted);

        slider.ValueChanged += (_, e) =>
        {
            int percent = (int)Math.Round(e.NewValue);
            value.Text = percent.ToString(CultureInfo.CurrentCulture);
            if (row.Syncing) return;
            AppVolumeService.SetSessionVolume(app.Key, percent);
            if (percent > 0) row.Muted = false;
            muteGlyph.Text = VolumeGlyph(percent, row.Muted);
        };
        slider.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? 2 : -2), 0, 100);
        };
        mute.Click += (_, _) =>
        {
            AppVolumeService.SetSessionMute(app.Key, !row.Muted);
            LoadSound();
        };

        return grid;
    }

    /// <summary>Mostra volume e mute letti da Windows, senza muovere un cursore che l'utente sta trascinando.</summary>
    private static void ShowLevel(MixerRow row, int volume, bool muted)
    {
        row.Muted = muted;
        row.MuteGlyph.Text = VolumeGlyph(volume, muted);
        row.Mute.ToolTip = muted ? "Unmute" : "Mute";
        row.Slider.Opacity = muted ? 0.5 : 1;
        if (row.Slider.IsMouseCaptureWithin) return;
        row.Syncing = true;
        row.Slider.Value = volume;
        row.Syncing = false;
        row.Value.Text = volume.ToString(CultureInfo.CurrentCulture);
    }

    private static async void LoadIcon(Image image, string key)
    {
        try { image.Source = await IconService.GetAsync(key, 32); }
        catch (Exception ex) { Log.Warn("Icona del mixer: " + ex.Message); }
    }
}
