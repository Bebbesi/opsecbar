using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using OpSecBar.Core;

namespace OpSecBar.Windows;

/// <summary>
/// Finestra trasparente e click-through che disegna l'ombra o l'alone esterno della barra flottante.
/// Perché esiste: l'ombra dentro TaskbarWindow verrebbe tagliata dal bordo della AppBar (pochi pixel verso il desktop)
/// oppure costringerebbe ad allargare l'area riservata spingendo via le finestre massimizzate. Una finestra separata
/// con WS_EX_TRANSPARENT si estende oltre la barra quanto serve all'ombra senza mai intercettare click né spostare finestre,
/// restando sempre entro i bordi del monitor (evita sia lo sbordo su monitor adiacenti sia il clamp di Windows sulla misura).
/// </summary>
public sealed class ShadowWindow : Window
{
    private readonly Grid _root = new() { IsHitTestVisible = false };
    private readonly Border _shape = new() { IsHitTestVisible = false };

    private IntPtr _hwnd;
    private bool _visible;
    private (int X, int Y, int W, int H, double BarW, double BarH, double R, double PadL, double PadT) _lastShape;
    private (Color Color, double Blur, bool Glow) _lastEffect;

    public ShadowWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
        Focusable = false;

        _root.Children.Add(_shape);
        Content = _root;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE,
                ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        };
    }

    /// <summary>
    /// Mostra/aggiorna l'ombra dietro la barra. <paramref name="originPx"/> e <paramref name="screenBoundsPx"/>
    /// sono in pixel fisici; dimensioni e raggio della barra in DIP.
    /// </summary>
    public void Update(IntPtr above, Point originPx, double barWidth, double barHeight, double radius, double scale,
        System.Drawing.Rectangle screenBoundsPx, Color shadowColor, double shadowSize, bool glow)
    {
        if (above == IntPtr.Zero || barWidth <= 0 || barHeight <= 0)
        {
            HideShadow();
            return;
        }

        double blur = Math.Clamp(shadowSize, 2, 40);
        int padPx = (int)Math.Ceiling((blur * 1.3 + 6) * scale);

        int barLeft = (int)Math.Round(originPx.X);
        int barTop = (int)Math.Round(originPx.Y);
        int barRight = barLeft + (int)Math.Round(barWidth * scale);
        int barBottom = barTop + (int)Math.Round(barHeight * scale);

        // Restiamo sempre nei limiti del monitor: la finestra non supera mai la risoluzione dello schermo
        // (niente clamp di Windows che taglierebbe l'ombra in fondo/a destra) e non sborda su altri monitor.
        int left = Math.Max(screenBoundsPx.Left, barLeft - padPx);
        int top = Math.Max(screenBoundsPx.Top, barTop - padPx);
        int right = Math.Min(screenBoundsPx.Right, barRight + padPx);
        int bottom = Math.Min(screenBoundsPx.Bottom, barBottom + padPx);
        int winW = right - left;
        int winH = bottom - top;
        if (winW <= 0 || winH <= 0)
        {
            HideShadow();
            return;
        }

        var effectKey = (shadowColor, blur, glow);
        if (effectKey != _lastEffect)
        {
            double strength = shadowColor.A / 255.0;
            Color rgb = Color.FromRgb(shadowColor.R, shadowColor.G, shadowColor.B);
            _shape.Background = new SolidColorBrush(rgb);
            _shape.Effect = new DropShadowEffect
            {
                Color = rgb,
                BlurRadius = blur,
                ShadowDepth = glow ? 0 : 2,
                Direction = 270,
                Opacity = (glow ? 0.95 : 0.5) * strength
            };
            _lastEffect = effectKey;
        }

        double padL = (barLeft - left) / scale;
        double padT = (barTop - top) / scale;
        double padR = Math.Max(0, (right - barRight) / scale);
        double padB = Math.Max(0, (bottom - barBottom) / scale);

        var shapeKey = (left, top, winW, winH, barWidth, barHeight, radius, padL, padT);
        if (shapeKey != _lastShape)
        {
            Width = winW / scale;
            Height = winH / scale;
            _shape.Margin = new Thickness(padL, padT, padR, padB);
            _shape.CornerRadius = new CornerRadius(radius);

            // Esclude l'interno della barra così il riempimento opaco di _shape non traspare dietro barre semitrasparenti.
            // Un rientro minimo (0.75 DIP) evita la fessura anti-aliased di ~1 px tra ombra e bordo della barra.
            const double seamInset = 0.75;
            var outer = new RectangleGeometry(new Rect(0, 0, Width, Height));
            var inner = new RectangleGeometry(
                new Rect(padL + seamInset, padT + seamInset, Math.Max(0, barWidth - seamInset * 2), Math.Max(0, barHeight - seamInset * 2)),
                Math.Max(0, radius - seamInset), Math.Max(0, radius - seamInset));
            _root.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            _lastShape = shapeKey;
        }

        if (!_visible)
        {
            Left = left / scale;
            Top = top / scale;
            Show();
            _visible = true;
        }

        if (_hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, above, left, top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    /// <summary>Riporta l'ombra subito sotto la barra dopo un cambio di z-order.</summary>
    public void Restack(IntPtr above)
    {
        if (_hwnd != IntPtr.Zero && _visible)
            Native.SetWindowPos(_hwnd, above, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void HideShadow()
    {
        if (!_visible) return;
        _visible = false;
        Hide();
    }
}
