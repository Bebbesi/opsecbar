using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpSecBar.Core;
using Windows.Media.Control;
using AutoRepeat = Windows.Media.MediaPlaybackAutoRepeatMode;

namespace OpSecBar.Services;

/// <summary>
/// Player musicale basato sui controlli multimediali di sistema di Windows (SMTC):
/// funziona con Spotify, browser, Groove/Media Player, VLC e qualsiasi app compatibile.
/// </summary>
public sealed class MediaService : ObservableObject
{
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _tick;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _started;
    private int _propsVersion;

    private TimeSpan _basePosition;
    private DateTimeOffset _baseTime;
    private double _rate = 1;

    private bool _hasSession, _isPlaying, _canSeek, _canNext = true, _canPrevious = true;
    private bool _canShuffle, _canRepeat, _shuffle;
    private RepeatMode _repeat;
    private string _sourceAppId = "";
    private TimeSpan _startTime;
    // Dopo un seek alcune app inviano ancora per un attimo la vecchia posizione: la si ignora finché non si allinea.
    private TimeSpan _seekTarget;
    private DateTime _seekGuardUntil = DateTime.MinValue;
    private string _title = "", _artist = "";
    private ImageSource? _cover;
    private TimeSpan _position, _duration;

    public bool HasSession { get => _hasSession; private set => Set(ref _hasSession, value); }
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }
    public bool CanSeek { get => _canSeek; private set => Set(ref _canSeek, value); }
    public bool CanNext { get => _canNext; private set => Set(ref _canNext, value); }
    public bool CanPrevious { get => _canPrevious; private set => Set(ref _canPrevious, value); }
    public bool CanShuffle { get => _canShuffle; private set => Set(ref _canShuffle, value); }
    public bool CanRepeat { get => _canRepeat; private set => Set(ref _canRepeat, value); }
    public bool Shuffle { get => _shuffle; private set => Set(ref _shuffle, value); }
    public RepeatMode Repeat { get => _repeat; private set => Set(ref _repeat, value); }
    /// <summary>App che possiede la sessione (AUMID o nome dell'eseguibile, es. "Spotify.exe").</summary>
    public string SourceAppId { get => _sourceAppId; private set => Set(ref _sourceAppId, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Artist { get => _artist; private set => Set(ref _artist, value); }
    public ImageSource? Cover { get => _cover; private set => Set(ref _cover, value); }
    public TimeSpan Duration { get => _duration; private set { if (Set(ref _duration, value)) { Raise(nameof(HasTimeline)); Raise(nameof(Progress)); } } }
    public TimeSpan Position { get => _position; private set { if (Set(ref _position, value)) Raise(nameof(Progress)); } }
    public bool HasTimeline => _duration.TotalSeconds > 1;
    public double Progress => HasTimeline ? Math.Clamp(_position.TotalSeconds / _duration.TotalSeconds, 0, 1) : 0;

    public MediaService(Dispatcher ui)
    {
        _ui = ui;
        _tick = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => UpdateInterpolatedPosition();
    }

    public async void Start()
    {
        if (_started) return;
        _started = true;
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _ui.BeginInvoke(AttachCurrentSession);
            AttachCurrentSession();
        }
        catch (Exception ex)
        {
            Log.Error("Controlli multimediali di sistema non disponibili", ex);
            _started = false;
        }
    }

    public void Stop()
    {
        _started = false;
        DetachSession();
        _manager = null;
        Clear();
    }

    private void DetachSession()
    {
        if (_session == null) return;
        try
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        catch { }
        _session = null;
    }

    private void AttachCurrentSession()
    {
        if (!_started || _manager == null) return;
        DetachSession();
        try { _session = _manager.GetCurrentSession(); }
        catch (Exception ex) { Log.Error("GetCurrentSession", ex); }

        if (_session == null)
        {
            Clear();
            return;
        }

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged += OnTimelineChanged;

        try { SourceAppId = _session.SourceAppUserModelId ?? ""; } catch { SourceAppId = ""; }
        _seekGuardUntil = DateTime.MinValue;
        HasSession = true;
        RefreshProperties();
        RefreshPlayback();
        RefreshTimeline();
    }

    private void Clear()
    {
        _tick.Stop();
        HasSession = false;
        IsPlaying = false;
        Title = "";
        Artist = "";
        Cover = null;
        Duration = TimeSpan.Zero;
        Position = TimeSpan.Zero;
        SourceAppId = "";
        CanShuffle = CanRepeat = Shuffle = false;
        Repeat = RepeatMode.None;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ui.BeginInvoke(RefreshProperties);
    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ui.BeginInvoke(RefreshPlayback);
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => _ui.BeginInvoke(RefreshTimeline);

    private async void RefreshProperties()
    {
        var session = _session;
        if (session == null) return;
        int version = ++_propsVersion;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (version != _propsVersion || props == null) return;

            Title = props.Title ?? "";
            Artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist ?? "" : props.Artist;

            ImageSource? cover = null;
            if (props.Thumbnail != null)
            {
                try
                {
                    using var ras = await props.Thumbnail.OpenReadAsync();
                    using var stream = ras.AsStreamForRead();
                    var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 320;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    cover = bmp;
                }
                catch (Exception ex) { Log.Warn("Copertina non leggibile: " + ex.Message); }
            }
            if (version == _propsVersion) Cover = cover;
        }
        catch (Exception ex) { Log.Warn("RefreshProperties: " + ex.Message); }
    }

    private void RefreshPlayback()
    {
        var session = _session;
        if (session == null) return;
        try
        {
            var info = session.GetPlaybackInfo();
            if (info == null) return;
            IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _rate = info.PlaybackRate ?? 1;
            var c = info.Controls;
            CanNext = c?.IsNextEnabled ?? true;
            CanPrevious = c?.IsPreviousEnabled ?? true;
            CanSeek = c?.IsPlaybackPositionEnabled ?? false;
            CanShuffle = c?.IsShuffleEnabled ?? false;
            CanRepeat = c?.IsRepeatEnabled ?? false;
            Shuffle = info.IsShuffleActive ?? false;
            Repeat = info.AutoRepeatMode switch
            {
                AutoRepeat.List => RepeatMode.All,
                AutoRepeat.Track => RepeatMode.One,
                _ => RepeatMode.None
            };
            RefreshTimeline();
        }
        catch (Exception ex) { Log.Warn("RefreshPlayback: " + ex.Message); }
    }

    private void RefreshTimeline()
    {
        var session = _session;
        if (session == null) return;
        try
        {
            var t = session.GetTimelineProperties();
            if (t == null) return;
            Duration = t.EndTime > t.StartTime ? t.EndTime - t.StartTime : TimeSpan.Zero;
            _startTime = t.StartTime;
            TimeSpan reported = t.Position - t.StartTime;
            if (DateTime.UtcNow < _seekGuardUntil && Math.Abs((reported - _seekTarget).TotalSeconds) > 2.5)
            {
                // Posizione vecchia, precedente al seek: si continua a interpolare dal punto richiesto.
                if (IsPlaying && HasTimeline) _tick.Start(); else _tick.Stop();
                return;
            }
            _seekGuardUntil = DateTime.MinValue;
            _basePosition = reported;
            _baseTime = t.LastUpdatedTime;
            UpdateInterpolatedPosition();
            if (IsPlaying && HasTimeline) _tick.Start(); else _tick.Stop();
        }
        catch (Exception ex) { Log.Warn("RefreshTimeline: " + ex.Message); }
    }

    private void UpdateInterpolatedPosition()
    {
        TimeSpan pos = _basePosition;
        if (IsPlaying && _baseTime.Year > 2000)
        {
            TimeSpan elapsed = DateTimeOffset.Now - _baseTime;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(12)) pos += TimeSpan.FromSeconds(elapsed.TotalSeconds * _rate);
        }
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        if (HasTimeline && pos > Duration) pos = Duration;
        Position = pos;
    }

    // ---- Comandi --------------------------------------------------------------------------------

    public async void TogglePlayPause()
    {
        try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch (Exception ex) { Log.Warn("PlayPause: " + ex.Message); }
    }

    public async void Next()
    {
        try { if (_session != null) await _session.TrySkipNextAsync(); } catch (Exception ex) { Log.Warn("Next: " + ex.Message); }
    }

    public async void Previous()
    {
        try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch (Exception ex) { Log.Warn("Previous: " + ex.Message); }
    }

    /// <summary>Sposta la riproduzione (0..1 della durata).</summary>
    public async void Seek(double fraction)
    {
        try
        {
            if (_session == null || !HasTimeline) return;
            var target = TimeSpan.FromSeconds(Duration.TotalSeconds * Math.Clamp(fraction, 0, 1));
            _basePosition = target;
            _baseTime = DateTimeOffset.Now;
            _seekTarget = target;
            _seekGuardUntil = DateTime.UtcNow.AddSeconds(2);
            Position = target;
            // La posizione è assoluta: va sommato l'inizio della timeline (quasi sempre 0).
            await _session.TryChangePlaybackPositionAsync((_startTime + target).Ticks);
        }
        catch (Exception ex) { Log.Warn("Seek: " + ex.Message); }
    }

    public async void ToggleShuffle()
    {
        try
        {
            if (_session == null || !CanShuffle) return;
            bool next = !Shuffle;
            Shuffle = next; // risposta immediata nella UI; la conferma arriva da PlaybackInfoChanged
            if (!await _session.TryChangeShuffleActiveAsync(next)) RefreshPlayback();
        }
        catch (Exception ex) { Log.Warn("Shuffle: " + ex.Message); }
    }

    /// <summary>Ripetizione a ciclo: nessuna → tutta la playlist → brano singolo → nessuna.</summary>
    public async void CycleRepeat()
    {
        try
        {
            if (_session == null || !CanRepeat) return;
            RepeatMode next = Repeat switch { RepeatMode.None => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.None };
            Repeat = next;
            var mode = next switch
            {
                RepeatMode.All => AutoRepeat.List,
                RepeatMode.One => AutoRepeat.Track,
                _ => AutoRepeat.None
            };
            if (!await _session.TryChangeAutoRepeatModeAsync(mode)) RefreshPlayback();
        }
        catch (Exception ex) { Log.Warn("Repeat: " + ex.Message); }
    }
}

public enum RepeatMode { None, All, One }
