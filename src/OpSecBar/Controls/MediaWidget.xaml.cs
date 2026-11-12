using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ManagedShell.AppBar;
using OpSecBar.Services;

namespace OpSecBar.Controls;

/// <summary>Player musicale: vista compatta nella barra e vista espansa al click.</summary>
public partial class MediaWidget : UserControl
{
    private MediaService? _media;
    private AudioService? _audio;
    private readonly DispatcherTimer _volumeTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(800) };
    private bool _syncingVolume;
    private Storyboard? _eq;
    private bool _eqRunning, _seeking, _enabled = true, _showTitle = true, _vertical;
    private DateTime _flyoutClosedAt = DateTime.MinValue;

    public event Action<bool>? FlyoutOpenChanged;

    public MediaWidget()
    {
        InitializeComponent();
        Unloaded += (_, _) => Detach();
        // Lo Slider con IsMoveToPointEnabled "consuma" il click sulla traccia (e.Handled = true) prima dei gestori normali:
        // senza handledEventsToo il primo click spostava solo il cursore e serviva un secondo click per il seek.
        Seek.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Seek_Down), true);
        Seek.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(Seek_Up), true);
        _volumeTimer.Tick += (_, _) => RefreshVolume();
    }

    public void Attach(MediaService media, AppBarEdge edge, AudioService? audio = null)
    {
        Detach();
        _media = media;
        _audio = audio;
        _media.PropertyChanged += OnMediaChanged;
        _eq = (Storyboard)Resources["EqStoryboard"];

        _vertical = edge is AppBarEdge.Left or AppBarEdge.Right;
        Compact.Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal;
        Flyout.Placement = edge switch
        {
            AppBarEdge.Bottom => PlacementMode.Top,
            AppBarEdge.Left => PlacementMode.Right,
            AppBarEdge.Right => PlacementMode.Left,
            _ => PlacementMode.Bottom
        };
        if (_vertical)
        {
            foreach (Button b in new[] { InfoButton, PlayButton })
            {
                b.Padding = new Thickness(0, 5, 0, 5);
                b.Margin = new Thickness(3, 1, 3, 1);
            }
        }
        RefreshAll();
    }

    private void Detach()
    {
        if (_media != null) _media.PropertyChanged -= OnMediaChanged;
        _media = null;
        StopEq();
    }

    /// <summary>Attiva/disattiva il widget e la visualizzazione del titolo (dalle impostazioni).</summary>
    public void Configure(bool enabled, bool showTitle)
    {
        _enabled = enabled;
        _showTitle = showTitle;
        RefreshAll();
    }

    private void OnMediaChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MediaService.Position):
            case nameof(MediaService.Progress):
                if (Flyout.IsOpen) RefreshTimeline();
                break;
            case nameof(MediaService.Duration):
            case nameof(MediaService.HasTimeline):
                RefreshTimeline();
                break;
            default:
                RefreshAll();
                break;
        }
    }

    private void RefreshAll()
    {
        var m = _media;
        bool visible = _enabled && m is { HasSession: true } && (!string.IsNullOrEmpty(m.Title) || m.IsPlaying);
        RootGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible || m == null)
        {
            if (Flyout.IsOpen) Flyout.IsOpen = false;
            StopEq();
            return;
        }

        string title = string.IsNullOrWhiteSpace(m.Title) ? "Now playing" : m.Title;
        CompactTitle.Text = string.IsNullOrWhiteSpace(m.Artist) ? title : $"{title}  ·  {m.Artist}";
        FlyoutTitle.Text = title;
        FlyoutArtist.Text = m.Artist;
        FlyoutArtist.Visibility = string.IsNullOrWhiteSpace(m.Artist) ? Visibility.Collapsed : Visibility.Visible;

        Brush? cover = m.Cover == null ? null : new ImageBrush(m.Cover) { Stretch = Stretch.UniformToFill };
        CoverSmall.Background = cover;
        CoverLarge.Background = cover;

        string glyph = m.IsPlaying ? "" : "";
        PlayGlyph.Text = glyph;
        FlyPlayGlyph.Text = glyph;

        PrevButton.IsEnabled = FlyPrev.IsEnabled = m.CanPrevious;
        NextButton.IsEnabled = FlyNext.IsEnabled = m.CanNext;

        CompactTitle.Visibility = _showTitle && !_vertical ? Visibility.Visible : Visibility.Collapsed;
        PrevButton.Visibility = NextButton.Visibility = Equalizer.Visibility = _vertical ? Visibility.Collapsed : Visibility.Visible;

        RefreshModes();
        if (m.IsPlaying && !_vertical) StartEq(); else StopEq();
        RefreshTimeline();
    }

    private void RefreshTimeline()
    {
        var m = _media;
        if (m == null) return;
        TimelinePanel.Visibility = m.HasTimeline ? Visibility.Visible : Visibility.Collapsed;
        if (!m.HasTimeline) return;
        Seek.IsEnabled = m.CanSeek;
        if (!_seeking) Seek.Value = m.Progress;
        PositionText.Text = Format(_seeking ? TimeSpan.FromSeconds(m.Duration.TotalSeconds * Seek.Value) : m.Position);
        DurationText.Text = Format(m.Duration);
    }

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void StartEq()
    {
        if (_eqRunning || _eq == null) return;
        try
        {
            _eq.Begin(this, true);
            _eqRunning = true;
            Equalizer.Opacity = 1;
        }
        catch (Exception ex)
        {
            // L'equalizzatore è solo decorativo: se l'animazione non parte lo si disattiva, senza altri effetti.
            OpSecBar.Core.Log.Warn("Equalizzatore: " + ex.Message);
            _eq = null;
        }
    }

    private void StopEq()
    {
        if (!_eqRunning || _eq == null) return;
        _eqRunning = false;
        try { _eq.Stop(this); } catch { }
        Equalizer.Opacity = 0.35;
    }

    // ---- Interazione ------------------------------------------------------------------------------

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        if ((DateTime.Now - _flyoutClosedAt).TotalMilliseconds < 250) return;
        RefreshAll();
        Flyout.IsOpen = true;
    }

    private void Flyout_Opened(object? sender, EventArgs e)
    {
        RefreshVolume();
        _volumeTimer.Start(); // segue anche le modifiche fatte dal mixer di Windows o dall'app stessa
        FlyoutOpenChanged?.Invoke(true);
    }

    private void Flyout_Closed(object? sender, EventArgs e)
    {
        _flyoutClosedAt = DateTime.Now;
        _seeking = false;
        _volumeTimer.Stop();
        FlyoutOpenChanged?.Invoke(false);
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => _media?.Previous();
    private void Play_Click(object sender, RoutedEventArgs e) => _media?.TogglePlayPause();
    private void Next_Click(object sender, RoutedEventArgs e) => _media?.Next();
    private void Shuffle_Click(object sender, RoutedEventArgs e) => _media?.ToggleShuffle();
    private void Repeat_Click(object sender, RoutedEventArgs e) => _media?.CycleRepeat();

    // ---- Shuffle e ripetizione --------------------------------------------------------------------

    private void RefreshModes()
    {
        var m = _media;
        if (m == null) return;
        ShuffleButton.IsEnabled = m.CanShuffle;
        RepeatButton.IsEnabled = m.CanRepeat;
        ShuffleButton.ToolTip = !m.CanShuffle ? "Shuffle (not supported by this app)" : m.Shuffle ? "Shuffle: on" : "Shuffle: off";
        RepeatButton.ToolTip = !m.CanRepeat ? "Repeat (not supported by this app)" : m.Repeat switch
        {
            RepeatMode.All => "Repeat: all",
            RepeatMode.One => "Repeat: this track",
            _ => "Repeat: off"
        };
        SetActive(ShuffleGlyph, ShuffleDot, m.Shuffle);
        SetActive(RepeatGlyph, RepeatDot, m.Repeat != RepeatMode.None);
        RepeatGlyph.Text = m.Repeat == RepeatMode.One ? "" : ""; // RepeatOne / RepeatAll
    }

    private static void SetActive(TextBlock glyph, FrameworkElement dot, bool on)
    {
        if (on) glyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        else glyph.ClearValue(TextBlock.ForegroundProperty);
        dot.Visibility = on ? Visibility.Visible : Visibility.Hidden;
    }

    // ---- Volume dell'app ---------------------------------------------------------------------------

    /// <summary>True se il volume mostrato è quello dell'app del player, false se è quello di sistema.</summary>
    private bool _isAppVolume;

    private void RefreshVolume()
    {
        var m = _media;
        if (m == null || !Flyout.IsOpen) return;
        int volume;
        bool muted;
        if (AppVolumeService.Get(m.SourceAppId) is { } app)
        {
            _isAppVolume = true;
            (volume, muted) = app;
        }
        else
        {
            _isAppVolume = false;
            if (_audio == null) { VolumeRow.Visibility = Visibility.Collapsed; return; }
            _audio.PollNow();
            if (!_audio.Available) { VolumeRow.Visibility = Visibility.Collapsed; return; }
            volume = Math.Max(0, _audio.Volume);
            muted = _audio.Muted;
        }
        VolumeRow.Visibility = Visibility.Visible;
        string source = _isAppVolume ? AppName(m.SourceAppId) + " volume" : "System volume";
        MediaVolume.ToolTip = source;
        MediaMuteButton.ToolTip = (muted ? "Unmute " : "Mute ") + source.Replace(" volume", "");
        MediaMuteGlyph.Text = muted || volume == 0 ? "" : volume < 34 ? "" : volume < 67 ? "" : "";
        MediaVolumeText.Text = muted ? "Mute" : volume.ToString();
        if (Math.Abs(MediaVolume.Value - volume) >= 1)
        {
            _syncingVolume = true;
            MediaVolume.Value = volume;
            _syncingVolume = false;
        }
    }

    /// <summary>Nome leggibile dall'AUMID: "Spotify.exe" → Spotify, "SpotifyAB.SpotifyMusic_x!Spotify" → Spotify.</summary>
    private static string AppName(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "App";
        int bang = id.IndexOf('!');
        string name = bang >= 0 ? id[(bang + 1)..] : System.IO.Path.GetFileNameWithoutExtension(id.Replace('/', '\\'));
        if (name.Equals("App", StringComparison.OrdinalIgnoreCase) && bang > 0)
        {
            string family = id[..bang];
            name = family.Split('_')[0].Split('.').Last();
        }
        return name.Length == 0 ? "App" : char.ToUpper(name[0]) + name[1..];
    }

    private void SetMediaVolume(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var m = _media;
        if (m == null) return;
        if (_isAppVolume && AppVolumeService.SetVolume(m.SourceAppId, percent)) { }
        else _audio?.SetVolume(percent);
        MediaVolumeText.Text = percent.ToString();
        RefreshVolume();
    }

    private void MediaVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingVolume || !IsLoaded) return;
        SetMediaVolume((int)Math.Round(e.NewValue));
    }

    private void MediaVolume_Wheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        SetMediaVolume((int)Math.Round(MediaVolume.Value) + (e.Delta > 0 ? 2 : -2));
    }

    private void MediaMute_Click(object sender, RoutedEventArgs e)
    {
        var m = _media;
        if (m == null) return;
        if (_isAppVolume && AppVolumeService.Get(m.SourceAppId) is { } app) AppVolumeService.SetMute(m.SourceAppId, !app.Muted);
        else _audio?.ToggleMute();
        RefreshVolume();
    }

    // ---- Barra di avanzamento ----------------------------------------------------------------------

    private void Seek_Down(object sender, MouseButtonEventArgs e)
    {
        if (Seek.IsEnabled) _seeking = true;
    }

    private void Seek_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        // Se si stava trascinando il cursore, il valore definitivo arriva subito dopo questo evento: si legge al giro successivo.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => CommitSeek());
    }

    private void Seek_LostCapture(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed) return;
        CommitSeek();
    }

    private void CommitSeek()
    {
        if (!_seeking) return;
        _seeking = false;
        _media?.Seek(Seek.Value);
    }

    private void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Durante il trascinamento il tempo mostrato segue il cursore.
        if (_seeking && _media is { HasTimeline: true } m)
            PositionText.Text = Format(TimeSpan.FromSeconds(m.Duration.TotalSeconds * e.NewValue));
    }
}
