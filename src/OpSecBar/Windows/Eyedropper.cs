using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpSecBar.Core;

namespace OpSecBar.Windows;

/// <summary>
/// Contagocce: fotografa lo schermo, copre ogni monitor con una finestra quasi invisibile e mostra una lente
/// d'ingrandimento sotto il puntatore. Click = prende il colore del pixel, Esc o click destro = annulla.
/// Le frecce spostano il puntatore di un pixel per una scelta precisa.
/// </summary>
public static class Eyedropper
{
    private const int LoupePixels = 13;   // lato (in pixel dello schermo) dell'area ingrandita
    private const double LoupeZoom = 9;   // ingrandimento della lente

    public static Task<Color?> PickAsync()
    {
        var tcs = new TaskCompletionSource<Color?>();
        ScreenCapture capture;
        try { capture = ScreenCapture.Take(); }
        catch (Exception ex)
        {
            Log.Error("Contagocce: cattura dello schermo", ex);
            tcs.SetResult(null);
            return tcs.Task;
        }

        var overlays = new List<Window>();
        bool done = false;

        void Finish(Color? result)
        {
            if (done) return;
            done = true;
            foreach (Window w in overlays) { try { w.Close(); } catch { } }
            tcs.TrySetResult(result);
        }

        foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
        {
            var overlay = CreateOverlay(screen.Bounds, capture, Finish);
            overlays.Add(overlay);
            overlay.Show();
        }
        // La finestra sotto il puntatore prende il fuoco (serve per Esc e frecce).
        Native.GetCursorPos(out Native.POINT p);
        Window first = overlays.FirstOrDefault(w => w.Tag is System.Drawing.Rectangle r && r.Contains(p.X, p.Y)) ?? overlays[0];
        first.Activate();
        Native.ForceForeground(new WindowInteropHelper(first).Handle);
        return tcs.Task;
    }

    private static Window CreateOverlay(System.Drawing.Rectangle bounds, ScreenCapture capture, Action<Color?> finish)
    {
        var loupeImage = new Image { Width = LoupePixels * LoupeZoom, Height = LoupePixels * LoupeZoom, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(loupeImage, BitmapScalingMode.NearestNeighbor);
        var center = new Border
        {
            Width = LoupeZoom + 2, Height = LoupeZoom + 2, BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(1) }
        };
        var lens = new Grid { ClipToBounds = true };
        lens.Children.Add(loupeImage);
        lens.Children.Add(center);

        var swatch = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255)), BorderThickness = new Thickness(1) };
        var hex = new TextBlock { Foreground = Brushes.White, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var info = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 2) };
        info.Children.Add(swatch);
        info.Children.Add(hex);
        var hint = new TextBlock
        {
            Text = "Click to pick · Esc to cancel", Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), FontSize = 11,
            Margin = new Thickness(8, 0, 8, 6)
        };

        var body = new StackPanel();
        body.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = lens, Margin = new Thickness(4, 4, 4, 0) });
        body.Children.Add(info);
        body.Children.Add(hint);
        var loupe = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x1E, 0x1F, 0x24)), CornerRadius = new CornerRadius(11),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = body, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.45, Direction = 270 }
        };

        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
        canvas.Children.Add(loupe);

        var window = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Topmost = true, Cursor = Cursors.Cross, Content = canvas, Tag = bounds,
            Left = -32000, Top = -32000, Width = 10, Height = 10, Title = "Eyedropper"
        };

        Color current = Colors.Black;

        void Update()
        {
            Native.GetCursorPos(out Native.POINT p);
            if (!bounds.Contains(p.X, p.Y)) { loupe.Visibility = Visibility.Collapsed; return; }
            current = capture.PixelAt(p.X, p.Y);
            loupeImage.Source = capture.Region(p.X - LoupePixels / 2, p.Y - LoupePixels / 2, LoupePixels, LoupePixels);
            swatch.Background = new SolidColorBrush(current);
            hex.Text = $"#{current.R:X2}{current.G:X2}{current.B:X2}";

            // Posizione della lente in DIP della finestra, spostata per non uscire dallo schermo.
            Point pos = Mouse.GetPosition(canvas);
            loupe.Visibility = Visibility.Visible;
            loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size size = loupe.DesiredSize;
            double x = pos.X + 22, y = pos.Y + 22;
            if (x + size.Width > canvas.ActualWidth) x = pos.X - 22 - size.Width;
            if (y + size.Height > canvas.ActualHeight) y = pos.Y - 22 - size.Height;
            Canvas.SetLeft(loupe, Math.Max(0, x));
            Canvas.SetTop(loupe, Math.Max(0, y));
        }

        void Cover()
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, new IntPtr(-1) /* HWND_TOPMOST */, bounds.X, bounds.Y, bounds.Width, bounds.Height, Native.SWP_SHOWWINDOW);
        }
        window.SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            // Ogni finestra copre esattamente un monitor: si ignora il cambio di DPI per non farla ridimensionare da WPF.
            HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == 0x02E0 /* WM_DPICHANGED */) handled = true;
                return IntPtr.Zero;
            });
            Cover();
        };
        // WPF può riapplicare la posizione iniziale durante la visualizzazione: la si reimposta a finestra caricata.
        window.Loaded += (_, _) => Cover();
        window.MouseMove += (_, _) => Update();
        window.MouseEnter += (_, _) => Update();
        window.MouseLeave += (_, _) => loupe.Visibility = Visibility.Collapsed;
        window.MouseLeftButtonUp += (_, _) =>
        {
            Native.GetCursorPos(out Native.POINT p);
            finish(capture.PixelAt(p.X, p.Y));
        };
        window.MouseRightButtonUp += (_, _) => finish(null);
        window.Closed += (_, _) => finish(null); // Alt+F4 o chiusura dall'esterno: si annulla
        window.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: finish(null); break;
                case Key.Enter: case Key.Space: finish(current); break;
                case Key.Left: case Key.Right: case Key.Up: case Key.Down:
                    Native.GetCursorPos(out Native.POINT p);
                    int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
                    int dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
                    SetCursorPos(p.X + dx, p.Y + dy);
                    Update();
                    e.Handled = true;
                    break;
            }
        };
        window.Deactivated += (_, _) =>
        {
            // Se un'altra app ruba il fuoco (Alt+Tab) si annulla, ma non quando il fuoco passa a un altro monitor.
            window.Dispatcher.BeginInvoke(() =>
            {
                IntPtr fg = Native.GetForegroundWindow();
                bool ours = Application.Current.Windows.OfType<Window>().Any(w => w.Title == "Eyedropper" && new WindowInteropHelper(w).Handle == fg);
                if (!ours) finish(null);
            });
        };
        return window;
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);

    /// <summary>Copia dei pixel dell'intero desktop virtuale (coordinate fisiche).</summary>
    private sealed class ScreenCapture
    {
        private byte[] _pixels = Array.Empty<byte>();
        private int _left, _top, _width, _height;

        public static ScreenCapture Take()
        {
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            using var bitmap = new System.Drawing.Bitmap(vs.Width, vs.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, System.Drawing.CopyPixelOperation.SourceCopy);
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, vs.Width, vs.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var c = new ScreenCapture { _left = vs.Left, _top = vs.Top, _width = vs.Width, _height = vs.Height };
                c._pixels = new byte[vs.Width * vs.Height * 4];
                for (int y = 0; y < vs.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, c._pixels, y * vs.Width * 4, vs.Width * 4);
                return c;
            }
            finally { bitmap.UnlockBits(data); }
        }

        public Color PixelAt(int x, int y)
        {
            x = Math.Clamp(x - _left, 0, _width - 1);
            y = Math.Clamp(y - _top, 0, _height - 1);
            int i = (y * _width + x) * 4;
            return Color.FromRgb(_pixels[i + 2], _pixels[i + 1], _pixels[i]);
        }

        public BitmapSource Region(int x, int y, int w, int h)
        {
            byte[] region = new byte[w * h * 4];
            for (int ry = 0; ry < h; ry++)
            for (int rx = 0; rx < w; rx++)
            {
                int sx = x + rx - _left, sy = y + ry - _top;
                int d = (ry * w + rx) * 4;
                if (sx < 0 || sy < 0 || sx >= _width || sy >= _height) continue; // fuori dallo schermo: trasparente
                int s = (sy * _width + sx) * 4;
                region[d] = _pixels[s]; region[d + 1] = _pixels[s + 1]; region[d + 2] = _pixels[s + 2]; region[d + 3] = 255;
            }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, region, w * 4);
            bmp.Freeze();
            return bmp;
        }
    }
}
