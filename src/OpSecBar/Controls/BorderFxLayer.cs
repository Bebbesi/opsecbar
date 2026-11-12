using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using OpSecBar.Models;

namespace OpSecBar.Controls;

/// <summary>
/// Bordo animato della barra, disegnato sopra di essa senza intercettare il mouse.
/// Gli effetti "minimal" (impulso, cometa, tratteggio, arcobaleno, scanner) sono animazioni WPF pure; quelli
/// "divertenti" (paperella, famiglia di papere, serpente, lucciole, UFO) sono personaggi che percorrono il perimetro,
/// aggiornati da un timer a ~40 fps che gira solo quando l'effetto è attivo e la barra è visibile.
/// </summary>
public sealed class BorderFxLayer : Canvas
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly Stopwatch _clock = new();
    private readonly List<Action<double>> _updaters = new();

    private BorderFx _fx = BorderFx.None;
    private Color _color = Colors.White;
    private double _speed = 1, _radius, _border;
    private Dock _desktopSide = Dock.Bottom;
    private bool _paused;
    private string _signature = "";

    public BorderFxLayer()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        _timer.Tick += (_, _) => Tick();
        SizeChanged += (_, _) => Rebuild();
        IsVisibleChanged += (_, _) => Rebuild();
        Unloaded += (_, _) => Stop();
    }

    /// <param name="desktopSide">Lato della barra rivolto verso il desktop (lo scanner corre lungo quel lato).</param>
    public void Configure(BorderFx fx, Color color, double speed, double cornerRadius, double borderThickness, Dock desktopSide)
    {
        string signature = $"{fx}|{color}|{speed:0.00}|{cornerRadius:0.0}|{borderThickness:0.0}|{desktopSide}";
        if (signature == _signature) return;
        _signature = signature;
        _fx = fx;
        _color = color;
        _speed = Math.Clamp(speed, 0.2, 4);
        _radius = Math.Max(0, cornerRadius);
        _border = Math.Max(0, borderThickness);
        _desktopSide = desktopSide;
        Rebuild();
    }

    /// <summary>Sospende l'effetto (barra nascosta in auto-hide o dietro un'app a schermo intero).</summary>
    public void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        _paused = paused;
        Rebuild();
    }

    private void Stop()
    {
        _timer.Stop();
        _clock.Reset();
        _updaters.Clear();
        Children.Clear();
    }

    private void Rebuild()
    {
        Stop();
        double w = ActualWidth, h = ActualHeight;
        if (_fx == BorderFx.None || _paused || !IsVisible || w < 20 || h < 12) return;
        try
        {
            switch (_fx)
            {
                case BorderFx.Pulse: BuildPulse(w, h); break;
                case BorderFx.Comet: BuildComets(w, h, 1); break;
                case BorderFx.TwinComets: BuildComets(w, h, 2); break;
                case BorderFx.MarchingAnts: BuildAnts(w, h); break;
                case BorderFx.Rainbow: BuildRainbow(w, h); break;
                case BorderFx.Scanner: BuildScanner(w, h); break;
                case BorderFx.Duck: BuildDucks(w, h, 0); break;
                case BorderFx.DuckFamily: BuildDucks(w, h, 3); break;
                case BorderFx.Snake: BuildSnake(w, h); break;
                case BorderFx.Fireflies: BuildFireflies(w, h); break;
                case BorderFx.Ufo: BuildUfo(w, h); break;
            }
        }
        catch (Exception ex)
        {
            // Puramente decorativo: in caso di problemi si spegne senza disturbare la barra.
            OpSecBar.Core.Log.Warn("Bordo animato: " + ex.Message);
            Stop();
            return;
        }
        if (_updaters.Count > 0)
        {
            _clock.Restart();
            Tick();
            _timer.Start();
        }
    }

    private void Tick()
    {
        double t = _clock.Elapsed.TotalSeconds * _speed;
        foreach (var update in _updaters) update(t);
    }

    // =============================================================================================
    //  Effetti minimal
    // =============================================================================================

    private double StrokeWidth => Math.Max(1.5, Math.Min(_border, 4));

    /// <summary>Rettangolo arrotondato che ricalca il bordo della barra (tratto interamente all'interno).</summary>
    private Rectangle Outline(double w, double h, Brush stroke, double thickness)
    {
        var r = new Rectangle
        {
            Width = Math.Max(0, w - thickness), Height = Math.Max(0, h - thickness), Stroke = stroke, StrokeThickness = thickness,
            RadiusX = Math.Max(0, _radius - thickness / 2), RadiusY = Math.Max(0, _radius - thickness / 2)
        };
        SetLeft(r, thickness / 2);
        SetTop(r, thickness / 2);
        Children.Add(r);
        return r;
    }

    private static double Perimeter(double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        return 2 * (w + h) - 8 * r + 2 * Math.PI * r;
    }

    private DropShadowEffect Glow(double blur = 8, double opacity = 0.9) =>
        new() { Color = _color, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity };

    private static Timeline Forever(Timeline t)
    {
        t.RepeatBehavior = RepeatBehavior.Forever;
        return t;
    }

    private void BuildPulse(double w, double h)
    {
        var r = Outline(w, h, new SolidColorBrush(_color), StrokeWidth);
        r.Effect = Glow(10);
        var anim = new DoubleAnimation(0.15, 1, TimeSpan.FromSeconds(1.3 / _speed))
        {
            AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        r.BeginAnimation(OpacityProperty, Forever(anim) as DoubleAnimation);
    }

    /// <summary>Una o più comete che corrono lungo il bordo, con una coda che sfuma.</summary>
    private void BuildComets(double w, double h, int count)
    {
        double th = StrokeWidth;
        double perimeter = Perimeter(w - th, h - th, Math.Max(0, _radius - th / 2));
        double period = perimeter / count;
        double length = Math.Min(period * 0.35, 240);
        var duration = TimeSpan.FromSeconds(period / (520 * _speed));

        foreach (var (fraction, opacity, glow) in new[] { (1.0, 0.22, false), (0.55, 0.5, false), (0.16, 1.0, true) })
        {
            double len = Math.Max(4, length * fraction);
            var r = Outline(w, h, new SolidColorBrush(_color), th);
            r.Opacity = opacity;
            r.StrokeDashArray = new DoubleCollection { len / th, (period - len) / th };
            r.StrokeDashCap = PenLineCap.Round;
            if (glow) r.Effect = Glow(10, 1);
            // Tutti gli strati condividono il fronte della cometa: ognuno parte "indietro" della propria lunghezza.
            r.BeginAnimation(Shape.StrokeDashOffsetProperty,
                (DoubleAnimation)Forever(new DoubleAnimation(len / th, (len - period) / th, duration)));
        }
    }

    private void BuildAnts(double w, double h)
    {
        double th = StrokeWidth;
        var r = Outline(w, h, new SolidColorBrush(_color), th);
        r.StrokeDashArray = new DoubleCollection { 3, 3 };
        r.Opacity = 0.9;
        r.BeginAnimation(Shape.StrokeDashOffsetProperty,
            (DoubleAnimation)Forever(new DoubleAnimation(0, -6, TimeSpan.FromSeconds(0.45 / _speed))));
    }

    private void BuildRainbow(double w, double h)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(w >= h ? 0.3 : 0, w >= h ? 0 : 0.3), SpreadMethod = GradientSpreadMethod.Repeat
        };
        string[] stops = { "#FF3B3B", "#FF9F1C", "#FFE53B", "#3BFF6E", "#2EC5FF", "#7A5CFF", "#FF3BD4", "#FF3B3B" };
        for (int i = 0; i < stops.Length; i++)
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops[i]), i / (double)(stops.Length - 1)));
        var move = new TranslateTransform();
        brush.RelativeTransform = move;
        Outline(w, h, brush, Math.Max(2, StrokeWidth));
        var anim = (DoubleAnimation)Forever(new DoubleAnimation(0, 0.3, TimeSpan.FromSeconds(2.2 / _speed)));
        move.BeginAnimation(w >= h ? TranslateTransform.XProperty : TranslateTransform.YProperty, anim);
    }

    /// <summary>Una luce che scorre avanti e indietro lungo il lato verso il desktop.</summary>
    private void BuildScanner(double w, double h)
    {
        bool horizontal = _desktopSide is Dock.Top or Dock.Bottom;
        double along = horizontal ? w : h;
        double length = Math.Clamp(along * 0.14, 50, 180);
        double th = Math.Max(2, StrokeWidth + 0.5);
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = horizontal ? new Point(1, 0) : new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, _color.R, _color.G, _color.B), 0));
        fade.GradientStops.Add(new GradientStop(_color, 0.5));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, _color.R, _color.G, _color.B), 1));
        var bar = new Rectangle { Fill = fade, Width = horizontal ? length : th, Height = horizontal ? th : length, RadiusX = th / 2, RadiusY = th / 2, Effect = Glow(12, 1) };
        double inset = Math.Max(_radius * 0.6, 2);
        switch (_desktopSide)
        {
            case Dock.Top: SetTop(bar, 0); break;
            case Dock.Left: SetLeft(bar, 0); break;
            case Dock.Right: SetLeft(bar, w - th); break;
            default: SetTop(bar, h - th); break;
        }
        Children.Add(bar);
        var anim = new DoubleAnimation(inset, along - length - inset, TimeSpan.FromSeconds(Math.Max(0.6, along / (700 * _speed))))
        {
            AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        bar.BeginAnimation(horizontal ? LeftProperty : TopProperty, (DoubleAnimation)Forever(anim));
    }

    // =============================================================================================
    //  Effetti divertenti: personaggi che percorrono il perimetro
    // =============================================================================================

    /// <summary>Percorso chiuso (rettangolo arrotondato, senso orario) rientrato di <c>inset</c> dal bordo della barra.</summary>
    private sealed class Track
    {
        private readonly double _x, _y, _w, _h, _r;
        public double Length { get; }

        public Track(double width, double height, double inset, double radius)
        {
            inset = Math.Min(inset, Math.Min(width, height) / 2 - 0.5);
            _x = inset;
            _y = inset;
            _w = Math.Max(1, width - inset * 2);
            _h = Math.Max(1, height - inset * 2);
            _r = Math.Clamp(radius - inset, 0, Math.Min(_w, _h) / 2);
            Length = 2 * (_w + _h) - 8 * _r + 2 * Math.PI * _r;
        }

        /// <summary>Punto e direzione di marcia alla distanza indicata dall'inizio (angolo in alto a sinistra).</summary>
        public (Point P, Vector Dir) At(double d)
        {
            d %= Length;
            if (d < 0) d += Length;
            double r = _r, top = _w - 2 * r, side = _h - 2 * r, arc = Math.PI * r / 2;

            if (d < top) return (new Point(_x + r + d, _y), new Vector(1, 0));
            d -= top;
            if (d < arc) return Arc(_x + _w - r, _y + r, -90, d);
            d -= arc;
            if (d < side) return (new Point(_x + _w, _y + r + d), new Vector(0, 1));
            d -= side;
            if (d < arc) return Arc(_x + _w - r, _y + _h - r, 0, d);
            d -= arc;
            if (d < top) return (new Point(_x + _w - r - d, _y + _h), new Vector(-1, 0));
            d -= top;
            if (d < arc) return Arc(_x + r, _y + _h - r, 90, d);
            d -= arc;
            if (d < side) return (new Point(_x, _y + _h - r - d), new Vector(0, -1));
            d -= side;
            return Arc(_x + r, _y + r, 180, Math.Min(d, arc));

            (Point, Vector) Arc(double cx, double cy, double startDeg, double along)
            {
                double a = (startDeg + (r > 0 ? along / r * 180 / Math.PI : 0)) * Math.PI / 180;
                return (new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a)), new Vector(-Math.Sin(a), Math.Cos(a)));
            }
        }
    }

    /// <summary>Dimensione dei personaggi, proporzionata allo spessore della barra.</summary>
    private static double SpriteSize(double w, double h) => Math.Clamp(Math.Min(w, h) * 0.42, 11, 24);

    /// <summary>Un disegno vettoriale (in coordinate proprie, rivolto a destra) con una trasformazione aggiornabile.</summary>
    private sealed class Sprite
    {
        public Canvas Root { get; } = new() { IsHitTestVisible = false };
        private readonly MatrixTransform _transform = new();
        private readonly double _cx, _cy;
        public bool FacingLeft;

        public Sprite(double designWidth, double designHeight)
        {
            _cx = designWidth / 2;
            _cy = designHeight / 2;
            Root.RenderTransform = _transform;
        }

        public void Place(Point p, double scale, double rotationDeg = 0, double lift = 0)
        {
            var m = Matrix.Identity;
            m.Translate(-_cx, -_cy);
            m.Scale(FacingLeft ? -scale : scale, scale);
            m.Rotate(rotationDeg);
            m.Translate(p.X, p.Y - lift);
            _transform.Matrix = m;
        }

        public void Face(Vector dir)
        {
            if (dir.X > 0.35) FacingLeft = false;
            else if (dir.X < -0.35) FacingLeft = true;
        }
    }

    private static SolidColorBrush Solid(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private static T At<T>(T element, double left, double top) where T : UIElement
    {
        SetLeft(element, left);
        SetTop(element, top);
        return element;
    }

    /// <summary>Paperella vista di lato, rivolta a destra, in un riquadro 28×22.</summary>
    private static Sprite MakeDuck(bool baby)
    {
        var s = new Sprite(28, 22);
        Brush body = Solid(baby ? "#FFE680" : "#FFD93D"), wing = Solid(baby ? "#F7D35C" : "#F2B90F"), beak = Solid("#FF8C1A");
        var c = s.Root.Children;
        c.Add(new Polygon { Points = new PointCollection { new(1, 10), new(6, 7), new(7, 13) }, Fill = body }); // coda
        c.Add(At(new Ellipse { Width = 19, Height = 11, Fill = body }, 2, 9));                                  // corpo
        c.Add(At(new Ellipse { Width = 9, Height = 5, Fill = wing }, 6, 11.5));                                // ala
        c.Add(At(new Ellipse { Width = 10, Height = 10, Fill = body }, 13, 1));                                // testa
        c.Add(new Polygon { Points = new PointCollection { new(22, 5), new(27.5, 7), new(22, 8.6) }, Fill = beak }); // becco
        c.Add(At(new Ellipse { Width = 2.2, Height = 2.2, Fill = Brushes.Black }, 18.5, 3.6));                 // occhio
        c.Add(At(new Ellipse { Width = 0.9, Height = 0.9, Fill = Brushes.White }, 19.4, 3.9));
        c.Add(At(new Ellipse { Width = 4, Height = 1.8, Fill = beak }, 8, 19.4));                              // zampe
        c.Add(At(new Ellipse { Width = 4, Height = 1.8, Fill = beak }, 13, 19.4));
        return s;
    }

    private void BuildDucks(double w, double h, int ducklings)
    {
        double size = SpriteSize(w, h);
        var track = new Track(w, h, size / 2 + 1, _radius);
        double k = size / 22;
        var mom = MakeDuck(false);
        Children.Add(mom.Root);
        var family = new List<(Sprite Sprite, double Back, double Scale, double Phase)>();
        for (int i = 0; i < ducklings; i++)
        {
            var baby = MakeDuck(true);
            Children.Add(baby.Root);
            family.Add((baby, size * (1.05 + i * 0.75), k * 0.62, i * 1.7 + 0.8));
        }

        double speed = 70; // px/s, moltiplicati dalla velocità scelta (già inclusa in t)
        _updaters.Add(t =>
        {
            double d = t * speed;
            var (p, dir) = track.At(d);
            mom.Face(dir);
            double step = t * 9;
            mom.Place(p, k, Math.Sin(step) * 7, Math.Abs(Math.Sin(step)) * 1.2);
            foreach (var (sprite, back, scale, phase) in family)
            {
                var (bp, bdir) = track.At(d - back);
                sprite.Face(bdir);
                double bs = t * 12 + phase;
                // I piccoli si appoggiano al binario (più bassi della mamma) e zampettano più in fretta.
                sprite.Place(new Point(bp.X, bp.Y + (size - size * 0.62) * 0.3), scale, Math.Sin(bs) * 10, Math.Abs(Math.Sin(bs)) * 1.5);
            }
        });
    }

    private void BuildSnake(double w, double h)
    {
        double size = SpriteSize(w, h);
        double radius = size * 0.3;
        var track = new Track(w, h, radius + 2.5, _radius);
        int segments = 16;
        double spacing = radius * 1.25;
        Color dark = Color.FromRgb((byte)(_color.R * 0.7), (byte)(_color.G * 0.7), (byte)(_color.B * 0.7));
        var parts = new List<(Ellipse E, double Back, double R)>();
        for (int i = segments - 1; i >= 0; i--) // dalla coda alla testa: la testa resta sopra
        {
            double rr = i == 0 ? radius * 1.15 : radius * (1 - i / (segments * 1.6));
            var e = new Ellipse
            {
                Width = rr * 2, Height = rr * 2, Fill = new SolidColorBrush(i % 2 == 0 ? _color : dark),
                Opacity = 1 - i / (double)segments * 0.55
            };
            if (i == 0) e.Effect = Glow(6, 0.7);
            Children.Add(e);
            parts.Add((e, i * spacing, rr));
        }
        // Occhi della testa
        var eyeL = new Ellipse { Width = radius * 0.55, Height = radius * 0.55, Fill = Brushes.White };
        var eyeR = new Ellipse { Width = radius * 0.55, Height = radius * 0.55, Fill = Brushes.White };
        var pupilL = new Ellipse { Width = radius * 0.28, Height = radius * 0.28, Fill = Brushes.Black };
        var pupilR = new Ellipse { Width = radius * 0.28, Height = radius * 0.28, Fill = Brushes.Black };
        foreach (var e in new[] { eyeL, eyeR, pupilL, pupilR }) Children.Add(e);

        const double speed = 110;
        double wiggle = radius * 0.7;
        _updaters.Add(t =>
        {
            double head = t * speed;
            Vector headDir = new(1, 0);
            Point headPos = default;
            foreach (var (e, back, rr) in parts)
            {
                double d = head - back;
                var (p, dir) = track.At(d);
                var normal = new Vector(-dir.Y, dir.X);
                // L'ondulazione è legata al punto del percorso: il corpo "scivola" dentro la sua stessa curva.
                p += normal * Math.Sin(d * 0.085) * wiggle;
                SetLeft(e, p.X - rr);
                SetTop(e, p.Y - rr);
                if (back == 0) { headPos = p; headDir = dir; }
            }
            var side = new Vector(-headDir.Y, headDir.X);
            void Eye(Ellipse eye, Ellipse pupil, double sign)
            {
                Point c = headPos + headDir * radius * 0.35 + side * sign * radius * 0.45;
                SetLeft(eye, c.X - eye.Width / 2); SetTop(eye, c.Y - eye.Height / 2);
                Point pc = c + headDir * radius * 0.12;
                SetLeft(pupil, pc.X - pupil.Width / 2); SetTop(pupil, pc.Y - pupil.Height / 2);
            }
            Eye(eyeL, pupilL, -1);
            Eye(eyeR, pupilR, 1);
        });
    }

    private void BuildFireflies(double w, double h)
    {
        double thickness = Math.Min(w, h);
        var track = new Track(w, h, 2, _radius);
        int count = (int)Math.Clamp(track.Length / 110, 8, 28);
        var rnd = new Random(7);
        var flies = new List<(Ellipse E, double Base, double V, double Amp, double F, double Phase, double Tw)>();
        for (int i = 0; i < count; i++)
        {
            double size = 5 + rnd.NextDouble() * 4;
            var glow = new RadialGradientBrush(_color, Color.FromArgb(0, _color.R, _color.G, _color.B));
            glow.GradientStops.Insert(1, new GradientStop(Color.FromArgb(160, _color.R, _color.G, _color.B), 0.35));
            var e = new Ellipse { Width = size, Height = size, Fill = glow };
            Children.Add(e);
            flies.Add((e, i * track.Length / count + rnd.NextDouble() * 40, (rnd.NextDouble() < 0.5 ? -1 : 1) * (8 + rnd.NextDouble() * 22),
                thickness * (0.12 + rnd.NextDouble() * 0.3), 0.6 + rnd.NextDouble() * 1.4, rnd.NextDouble() * 6.28, 1 + rnd.NextDouble() * 2.5));
        }
        _updaters.Add(t =>
        {
            foreach (var (e, b, v, amp, f, phase, tw) in flies)
            {
                var (p, dir) = track.At(b + v * t);
                var inward = new Vector(-dir.Y, dir.X);
                p += inward * amp * (0.5 + 0.5 * Math.Sin(t * f + phase));
                SetLeft(e, p.X - e.Width / 2);
                SetTop(e, p.Y - e.Height / 2);
                e.Opacity = 0.25 + 0.75 * Math.Pow(0.5 + 0.5 * Math.Sin(t * tw + phase * 2), 2);
            }
        });
    }

    /// <summary>Disco volante con cupola nel colore dell'effetto e luci che lampeggiano in sequenza.</summary>
    private void BuildUfo(double w, double h)
    {
        double size = SpriteSize(w, h);
        var track = new Track(w, h, size / 2 + 1, _radius);
        var ufo = new Sprite(30, 18);
        var c = ufo.Root.Children;
        c.Add(At(new Ellipse { Width = 14, Height = 12, Fill = new SolidColorBrush(Color.FromArgb(200, _color.R, _color.G, _color.B)), Stroke = Solid("#CCFFFFFF"), StrokeThickness = 0.8 }, 8, 1)); // cupola
        var hull = new LinearGradientBrush(Color.FromRgb(0xE8, 0xEC, 0xF2), Color.FromRgb(0x7A, 0x82, 0x90), 90);
        c.Add(At(new Ellipse { Width = 30, Height = 9, Fill = hull }, 0, 7)); // scafo
        var lights = new List<Ellipse>();
        for (int i = 0; i < 4; i++)
        {
            var light = At(new Ellipse { Width = 2.6, Height = 2.6, Fill = new SolidColorBrush(_color) }, 4.5 + i * 6, 10.2);
            lights.Add(light);
            c.Add(light);
        }
        var beam = new Polygon
        {
            Points = new PointCollection { new(11, 15), new(19, 15), new(23, 26), new(7, 26) },
            Fill = new LinearGradientBrush(Color.FromArgb(120, _color.R, _color.G, _color.B), Color.FromArgb(0, _color.R, _color.G, _color.B), 90)
        };
        c.Insert(0, beam);
        Children.Add(ufo.Root);
        double k = size / 18;

        _updaters.Add(t =>
        {
            // Avanza a scatti: accelera, rallenta e ogni tanto si ferma ad "analizzare" con il raggio traente.
            double d = (t * 95) + Math.Sin(t * 0.9) * 60;
            var (p, dir) = track.At(d);
            ufo.Face(dir);
            ufo.Place(p, k, Math.Sin(t * 1.8) * 8 * (ufo.FacingLeft ? -1 : 1), Math.Sin(t * 3.1) * 1.5);
            beam.Opacity = Math.Max(0, Math.Sin(t * 0.7)) > 0.85 ? 1 : 0;
            int lit = (int)(t * 6) % lights.Count;
            for (int i = 0; i < lights.Count; i++) lights[i].Opacity = i == lit ? 1 : 0.35;
        });
    }
}
