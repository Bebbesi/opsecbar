using System.Windows.Media;
using OpSecBar.Core;
using OpSecBar.Models;

namespace OpSecBar.Services;

/// <summary>
/// Finestra nativa minima che fa da "lastra sfocata" sotto la barra (o sotto il menu Start).
/// Perché esiste: sulle finestre WPF trasparenti (layered) Windows ignora la regione quando disegna blur/acrylic,
/// quindi l'effetto riempirebbe tutto il rettangolo della finestra. Una finestra normale invece viene ritagliata
/// correttamente: la si posiziona esattamente dietro la barra, con gli stessi angoli arrotondati.
/// </summary>
public sealed class BackdropWindow : IDisposable
{
    private const uint WS_POPUP = 0x80000000, SS_BLACKRECT = 0x4;
    private const uint WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;

    private IntPtr _hwnd;
    private (int X, int Y, int W, int H, int R) _lastShape;
    private (BackdropMode Mode, Color Tint, double Opacity) _lastAccent;
    private bool _visible;

    /// <summary>Mostra/aggiorna la lastra subito sotto la finestra <paramref name="above"/>. Coordinate in pixel fisici.</summary>
    public void Show(IntPtr above, int x, int y, int width, int height, int radius, BackdropMode mode, Color tint, double opacity)
    {
        if (width <= 0 || height <= 0 || above == IntPtr.Zero)
        {
            Hide();
            return;
        }

        if (_hwnd == IntPtr.Zero)
        {
            // Classe di sistema "Static" con SS_BLACKRECT: nessuna classe da registrare, sfondo nero = trasparente per l'accent.
            _hwnd = Native.CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST, "Static", "",
                WS_POPUP | SS_BLACKRECT, x, y, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Log.Warn("BackdropWindow: creazione fallita");
                return;
            }
            _lastShape = default;
            _lastAccent = default;
        }

        var accent = (mode, tint, opacity);
        if (accent != _lastAccent)
        {
            BackdropHelper.Apply(_hwnd, mode, tint, opacity);
            _lastAccent = accent;
        }

        var shape = (x, y, width, height, radius);
        if (shape != _lastShape)
        {
            if (radius > 0) BackdropHelper.SetRoundedRegion(_hwnd, 0, 0, width, height, radius);
            else BackdropHelper.ClearRegion(_hwnd);
            _lastShape = shape;
        }

        // Inserita subito dopo (= sotto) la finestra della barra nello z-order.
        Native.SetWindowPos(_hwnd, above, x, y, width, height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        _visible = true;
    }

    /// <summary>Riporta la lastra subito sotto la barra dopo un cambio di z-order.</summary>
    public void Restack(IntPtr above)
    {
        if (_hwnd != IntPtr.Zero && _visible)
            Native.SetWindowPos(_hwnd, above, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void Hide()
    {
        if (_hwnd != IntPtr.Zero && _visible) Native.ShowWindow(_hwnd, Native.SW_HIDE);
        _visible = false;
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero) Native.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
        _visible = false;
    }
}
