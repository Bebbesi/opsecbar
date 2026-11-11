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
    private double _height;

    public GifImage()
    {
        Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        _timer = new DispatcherTimer(DispatcherPriority.Render);
        _timer.Tick += (_, _) => Advance();
        IsVisibleChanged += (_, _) => UpdateRunning();
        Unloaded += (_, _) => _timer.Stop();
        Loaded += (_, _) => UpdateRunning();
    }

    /// <summary>Carica la GIF mostrandola alta <paramref name="height"/> DIP.</summary>
    public async void Load(string path, double height)
    {
        if (path == _path && Math.Abs(height - _height) < 0.1) return;
        _path = path;
        _height = height;
        Height = height;
        _timer.Stop();
        try
        {
            // Si decodifica a ~2x (schermi ad alta densità) ma senza mai ingrandire l'originale.
            var animation = await GifService.LoadAsync(path, (int)Math.Ceiling(height * 2));
            if (path != _path) return; // nel frattempo è stata chiesta un'altra GIF
            _animation = animation;
            _index = 0;
            if (animation == null) { Source = null; Width = height; return; }
            Width = height * animation.Aspect;
            Source = animation.Frames[0];
            UpdateRunning();
        }
        catch (Exception ex) { Log.Warn("GIF: " + ex.Message); }
    }

    private void UpdateRunning()
    {
        if (_animation is { Frames.Length: > 1 } a && IsVisible && IsLoaded)
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
