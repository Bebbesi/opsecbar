using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OpSecBar.Core;
using OpSecBar.Services;

namespace OpSecBar.Controls;

/// <summary>Immagine che riproduce in loop una GIF animata. Si ferma da sola quando non è visibile.</summary>
public sealed class GifImage : Image
{
    private readonly DispatcherTimer _timer;
    private GifAnimation? _animation;
    private int _index;
    private string? _path;
    private double _height, _dpi;
    private bool _paused;

    /// <summary>Ferma l'animazione mentre la barra non si vede (auto-hide, app a schermo intero).</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            UpdateRunning();
        }
    }

    public GifImage()
    {
        Stretch = Stretch.Uniform;
        // I fotogrammi sono già decodificati alla dimensione in pixel dello schermo: basta il filtro lineare (GPU).
        // Quello "HighQuality" riscalerebbe via software ogni fotogramma a ogni cambio.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        _timer = new DispatcherTimer(DispatcherPriority.Render);
        _timer.Tick += (_, _) => Advance();
        IsVisibleChanged += (_, _) => UpdateRunning();
        Unloaded += (_, _) => _timer.Stop();
        Loaded += (_, _) => UpdateRunning();
    }

    /// <summary>Carica la GIF mostrandola alta <paramref name="height"/> DIP.</summary>
    public async void Load(string path, double height)
    {
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
        if (path == _path && Math.Abs(height - _height) < 0.1 && Math.Abs(dpi - _dpi) < 0.01) return;
        _path = path;
        _height = height;
        _dpi = dpi;
        Height = height;
        _timer.Stop();
        try
        {
            // Si decodifica esattamente ai pixel dello schermo (mai ingrandendo l'originale): fotogrammi più piccoli
            // in memoria e nessun ridimensionamento a ogni cambio di fotogramma.
            var animation = await GifService.LoadAsync(path, (int)Math.Ceiling(height * dpi));
            if (path != _path || Math.Abs(dpi - _dpi) >= 0.01) return; // nel frattempo è stata chiesta un'altra GIF
            _animation = animation;
            _index = 0;
            if (animation == null) { Source = null; Width = height; return; }
            Width = height * animation.Aspect;
            Source = animation.Frames[0];
            UpdateRunning();
        }
        catch (Exception ex) { Log.Warn("GIF: " + ex.Message); }
    }

    /// <summary>Schermo con un'altra densità (o barra spostata su un altro monitor): si ridecodifica a quei pixel.</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_path != null && Math.Abs(newDpi.DpiScaleY - _dpi) >= 0.01) Load(_path, _height);
    }

    private void UpdateRunning()
    {
        if (_animation is { Frames.Length: > 1 } a && IsVisible && IsLoaded && !_paused)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(a.DelaysMs[_index]);
            _timer.Start();
        }
        else _timer.Stop();
    }

    private void Advance()
    {
        if (_animation is not { } a || a.Frames.Length == 0) { _timer.Stop(); return; }
        _index = (_index + 1) % a.Frames.Length;
        Source = a.Frames[_index];
        _timer.Interval = TimeSpan.FromMilliseconds(a.DelaysMs[_index]);
    }
}
