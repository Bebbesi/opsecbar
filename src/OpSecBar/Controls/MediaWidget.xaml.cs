using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ManagedShell.AppBar;
using OpSecBar.Services;

namespace OpSecBar.Controls;

/// <summary>Player musicale: vista compatta nella barra e vista espansa al click.</summary>
public partial class MediaWidget : UserControl
{
    private MediaService? _media;
    private Storyboard? _eq;
    private bool _eqRunning, _seeking, _enabled = true, _showTitle = true, _vertical;
    private DateTime _flyoutClosedAt = DateTime.MinValue;

    public event Action<bool>? FlyoutOpenChanged;

    public MediaWidget()
    {
        InitializeComponent();
        Unloaded += (_, _) => Detach();
    }

    public void Attach(MediaService media, AppBarEdge edge)
    {
        Detach();
        _media = media;
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

    private void Flyout_Opened(object? sender, EventArgs e) => FlyoutOpenChanged?.Invoke(true);

    private void Flyout_Closed(object? sender, EventArgs e)
    {
        _flyoutClosedAt = DateTime.Now;
        _seeking = false;
        FlyoutOpenChanged?.Invoke(false);
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => _media?.Previous();
    private void Play_Click(object sender, RoutedEventArgs e) => _media?.TogglePlayPause();
    private void Next_Click(object sender, RoutedEventArgs e) => _media?.Next();

    private void Seek_Down(object sender, MouseButtonEventArgs e) => _seeking = true;

    private void Seek_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        // Il valore viene aggiornato dallo slider subito dopo questo evento: leggo al giro successivo.
        Dispatcher.BeginInvoke(() =>
        {
            _media?.Seek(Seek.Value);
            _seeking = false;
        });
    }

    private void Seek_LostCapture(object sender, MouseEventArgs e)
    {
        if (!_seeking || Mouse.LeftButton == MouseButtonState.Pressed) return;
        _media?.Seek(Seek.Value);
        _seeking = false;
    }
}
