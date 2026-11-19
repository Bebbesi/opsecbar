using System.Numerics;
using System.Runtime.InteropServices;
using OpSecBar.Core;
using OpSecBar.Models;
using Windows.Foundation;
using Windows.Graphics.Effects;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using Color = System.Windows.Media.Color;

namespace OpSecBar.Services;

/// <summary>
/// Finestra nativa minima che fa da "lastra sfocata" sotto la barra (o sotto i pannelli flyout).
/// Usa Windows.UI.Composition (CreateHostBackdropBrush + GaussianBlur + clip arrotondato hardware) su una finestra
/// WS_EX_NOREDIRECTIONBITMAP: il ritaglio arrotondato Ã¨ anti-aliased e non lascia mai angoli rettangolari dietro la barra
/// su nessuna versione di Windows 10/11. In caso di errore fa fallback al vecchio percorso SetWindowCompositionAttribute.
/// </summary>
public sealed class BackdropWindow : IDisposable
{
    private const uint WS_POPUP = 0x80000000, SS_BLACKRECT = 0x4;
    private const uint WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
    private const uint WS_EX_NOACTIVATE = 0x08000000, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    private IntPtr _hwnd;
    private bool _useComposition;
    private DesktopWindowTarget? _target;
    private SpriteVisual? _visual;
    private CompositionRoundedRectangleGeometry? _clipGeometry;
    private CompositionGeometricClip? _clip;

    private (int X, int Y, int W, int H, int R) _lastShape;
    private (BackdropMode Mode, Color Tint, double Opacity, bool Rounded) _lastAccent;
    private BackdropMode _lastCompMode;
    private bool _visible;

    /// <summary>Mostra/aggiorna la lastra subito sotto la finestra <paramref name="above"/>. Coordinate in pixel fisici.</summary>
    public void Show(IntPtr above, int x, int y, int width, int height, int radius, BackdropMode mode, Color tint, double opacity)
    {
        if (width <= 0 || height <= 0 || above == IntPtr.Zero)
        {
            Hide();
            return;
        }

        if (_hwnd == IntPtr.Zero && !CreateBackdropWindow(x, y, width, height)) return;

        if (_useComposition) UpdateComposition(x, y, width, height, radius, mode);
        else UpdateLegacy(x, y, width, height, radius, mode, tint, opacity);

        // Inserita subito dopo (= sotto) la finestra della barra nello z-order.
        Native.SetWindowPos(_hwnd, above, x, y, width, height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        _visible = true;
    }

    private bool CreateBackdropWindow(int x, int y, int width, int height)
    {
        _lastShape = default;
        _lastAccent = default;
        _lastCompMode = (BackdropMode)(-1);

        if (BackdropHelper.CompositionBackdropAvailable && CompositionEngine.TryEnsure(out Compositor? compositor))
        {
            IntPtr hwnd = Native.CreateWindowEx(
                WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT,
                "Static", "", WS_POPUP, x, y, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hwnd != IntPtr.Zero)
            {
                try
                {
                    _target = CompositionEngine.CreateTarget(compositor!, hwnd);
                    _visual = compositor!.CreateSpriteVisual();
                    _clipGeometry = compositor.CreateRoundedRectangleGeometry();
                    _clip = compositor.CreateGeometricClip(_clipGeometry);
                    _target.Root = _visual;
                    _hwnd = hwnd;
                    _useComposition = true;
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Warn("BackdropWindow (Composition) non disponibile, uso fallback: " + ex.Message);
                    DisposeCompositionResources();
                    Native.DestroyWindow(hwnd);
                    BackdropHelper.CompositionBackdropAvailable = false;
                }
            }
        }

        // Fallback: finestra nativa "Static" con SS_BLACKRECT (sfondo nero = trasparente per l'accent).
        _hwnd = Native.CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TRANSPARENT,
            "Static", "", WS_POPUP | SS_BLACKRECT, x, y, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            Log.Warn("BackdropWindow: creazione fallita");
            return false;
        }
        _useComposition = false;
        return true;
    }

    private void UpdateComposition(int x, int y, int width, int height, int radius, BackdropMode mode)
    {
        if (_visual == null || _clipGeometry == null || _clip == null) return;

        if (mode != _lastCompMode)
        {
            _visual.Brush = CompositionEngine.GetBlurBrush(mode);
            _lastCompMode = mode;
        }

        if (width != _lastShape.W || height != _lastShape.H || radius != _lastShape.R)
        {
            _visual.Size = new Vector2(width, height);
            if (radius > 0)
            {
                // Rientro di 1 px fisico: compensa l'arrotondamento subpixel di posizione/dimensione e il fatto che
                // il bordo esterno del Border WPF ha raggio (CornerRadius + BorderThickness / 2), evitando sbavature agli angoli.
                const float inset = 1f;
                float w = MathF.Max(0, width - inset * 2);
                float h = MathF.Max(0, height - inset * 2);
                float r = MathF.Min(radius, MathF.Min(w, h) / 2f);
                _clipGeometry.Offset = new Vector2(inset, inset);
                _clipGeometry.Size = new Vector2(w, h);
                _clipGeometry.CornerRadius = new Vector2(r, r);
                _visual.Clip = _clip;
            }
            else
            {
                _visual.Clip = null;
            }
        }
        _lastShape = (x, y, width, height, radius);
    }

    private void UpdateLegacy(int x, int y, int width, int height, int radius, BackdropMode mode, Color tint, double opacity)
    {
        var accent = (mode, tint, opacity, radius > 0);
        if (accent != _lastAccent)
        {
            BackdropHelper.Apply(_hwnd, mode, tint, opacity, rounded: radius > 0);
            _lastAccent = accent;
        }

        var shape = (x, y, width, height, radius);
        if (shape != _lastShape)
        {
            if (radius > 0) BackdropHelper.SetRoundedRegion(_hwnd, 0, 0, width, height, radius);
            else BackdropHelper.ClearRegion(_hwnd);
            _lastShape = shape;
        }
    }

    /// <summary>Sposta la lastra senza cambiarne forma nÃ© z-order, per seguire una finestra che scorre.</summary>
    public void MoveTo(int x, int y)
    {
        if (_hwnd == IntPtr.Zero || !_visible) return;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        _lastShape = (x, y, _lastShape.W, _lastShape.H, _lastShape.R);
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

    private void DisposeCompositionResources()
    {
        if (_visual != null) _visual.Clip = null;
        if (_target != null) _target.Root = null;
        _clip?.Dispose();
        _clip = null;
        _clipGeometry?.Dispose();
        _clipGeometry = null;
        _visual?.Dispose();
        _visual = null;
        _target?.Dispose();
        _target = null;
    }

    public void Dispose()
    {
        DisposeCompositionResources();
        if (_hwnd != IntPtr.Zero) Native.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
        _visible = false;
        _useComposition = false;
    }

    // ---- Motore Windows.UI.Composition condiviso sul thread UI ----------------------------------

    private static class CompositionEngine
    {
        private static readonly Guid IidCompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");
        private static readonly Guid ClsidD2D1GaussianBlur = new("1FEB6D69-2FE6-4AC9-8C58-1D7F93E7A6A5");
        private static readonly Guid IidIPropertyValue = new("4BD682DD-7554-40E9-9A9B-82654EDE7E62");

        private static bool _initialized;
        private static IntPtr _dispatcherQueueController; // mantenuto vivo per tutta la durata del processo
        private static Compositor? _compositor;
        private static CompositionBackdropBrush? _hostBackdrop;
        private static readonly Dictionary<BackdropMode, CompositionBrush> _brushes = new();

        public static bool TryEnsure(out Compositor? compositor)
        {
            if (!_initialized)
            {
                _initialized = true;
                try
                {
                    if (DispatcherQueue.GetForCurrentThread() == null)
                    {
                        var options = new DispatcherQueueOptions
                        {
                            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                            threadType = 2,    // DQTYPE_THREAD_CURRENT
                            apartmentType = 2  // DQTAT_COM_STA
                        };
                        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _dispatcherQueueController));
                    }
                    _compositor = new Compositor();
                    _hostBackdrop = _compositor.CreateHostBackdropBrush();
                    // Verifica subito che la creazione del blur D2D funzioni prima di impegnarci su questo percorso.
                    _brushes[BackdropMode.Blur] = CreateBlurBrush(_compositor, _hostBackdrop, 18f);
                }
                catch (Exception ex)
                {
                    Log.Warn("CompositionEngine: inizializzazione fallita: " + ex.Message);
                    _hostBackdrop?.Dispose();
                    _hostBackdrop = null;
                    _compositor?.Dispose();
                    _compositor = null;
                    BackdropHelper.CompositionBackdropAvailable = false;
                }
            }

            compositor = _compositor;
            return compositor != null;
        }

        public static CompositionBrush GetBlurBrush(BackdropMode mode)
        {
            if (_brushes.TryGetValue(mode, out CompositionBrush? existing)) return existing;
            float sigma = mode switch
            {
                BackdropMode.Acrylic => 28f,
                BackdropMode.Glass => 16f,
                _ => 18f
            };
            CompositionBrush brush = CreateBlurBrush(_compositor!, _hostBackdrop!, sigma);
            _brushes[mode] = brush;
            return brush;
        }

        public static DesktopWindowTarget CreateTarget(Compositor compositor, IntPtr hwnd)
        {
            IntPtr unk = WinRT.MarshalInspectable<Compositor>.FromManaged(compositor);
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unk, in IidCompositorDesktopInterop, out IntPtr interopPtr));
                try
                {
                    var interop = (ICompositorDesktopInterop)Marshal.GetObjectForIUnknown(interopPtr);
                    Marshal.ThrowExceptionForHR(interop.CreateDesktopWindowTarget(hwnd, false, out IntPtr targetPtr));
                    try { return DesktopWindowTarget.FromAbi(targetPtr); }
                    finally { Marshal.Release(targetPtr); }
                }
                finally { Marshal.Release(interopPtr); }
            }
            finally { Marshal.Release(unk); }
        }

        private static CompositionEffectBrush CreateBlurBrush(Compositor compositor, CompositionBackdropBrush backdrop, float sigma)
        {
            var effect = new GaussianBlurEffect(sigma);
            IntPtr effectPtr = Marshal.GetComInterfaceForObject(effect, typeof(IGraphicsEffectRaw));
            try
            {
                IGraphicsEffect wrapped = WinRT.MarshalInterface<IGraphicsEffect>.FromAbi(effectPtr);
                using CompositionEffectFactory factory = compositor.CreateEffectFactory(wrapped);
                CompositionEffectBrush brush = factory.CreateBrush();
                brush.SetSourceParameter("source", backdrop);
                return brush;
            }
            finally
            {
                Marshal.Release(effectPtr);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }

        [DllImport("CoreMessaging.dll")]
        private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

        [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICompositorDesktopInterop
        {
            [PreserveSig]
            int CreateDesktopWindowTarget(IntPtr hwndTarget, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out IntPtr result);
        }

        [ComImport, Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IInspectableRaw
        {
            [PreserveSig] int GetIids(out uint iidCount, out IntPtr iids);
            [PreserveSig] int GetRuntimeClassName(out IntPtr className);
            [PreserveSig] int GetTrustLevel(out int trustLevel);
        }

        [ComImport, Guid("2D8F9DDC-4339-4EB9-9216-F9DEB75658A2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsEffectSourceRaw
        {
            [PreserveSig] int GetIids(out uint iidCount, out IntPtr iids);
            [PreserveSig] int GetRuntimeClassName(out IntPtr className);
            [PreserveSig] int GetTrustLevel(out int trustLevel);
        }

        [ComImport, Guid("CB51C0CE-8FE6-4636-B202-861FAA07D8F3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsEffectRaw
        {
            [PreserveSig] int GetIids(out uint iidCount, out IntPtr iids);
            [PreserveSig] int GetRuntimeClassName(out IntPtr className);
            [PreserveSig] int GetTrustLevel(out int trustLevel);
            [PreserveSig] int get_Name(out IntPtr name);
            [PreserveSig] int put_Name(IntPtr name);
        }

        [ComImport, Guid("2FC57384-A068-44D7-A331-30982FCF7177"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsEffectD2D1InteropRaw
        {
            [PreserveSig] int GetEffectId(out Guid id);
            [PreserveSig] int GetNamedPropertyMapping(IntPtr name, out uint index, out uint mapping);
            [PreserveSig] int GetPropertyCount(out uint count);
            [PreserveSig] int GetProperty(uint index, out IntPtr value);
            [PreserveSig] int GetSource(uint index, out IntPtr source);
            [PreserveSig] int GetSourceCount(out uint count);
        }

        /// <summary>Definizione minimale di D2D1 GaussianBlur per Compositor.CreateEffectFactory (nessuna dipendenza da Win2D).</summary>
        private sealed class GaussianBlurEffect : IInspectableRaw, IGraphicsEffectRaw, IGraphicsEffectSourceRaw, IGraphicsEffectD2D1InteropRaw
        {
            private readonly float _sigma;
            private readonly CompositionEffectSourceParameter _source = new("source");

            public GaussianBlurEffect(float sigma) => _sigma = sigma;

            public int GetIids(out uint iidCount, out IntPtr iids) { iidCount = 0; iids = IntPtr.Zero; return 0; }
            public int GetRuntimeClassName(out IntPtr className) { className = IntPtr.Zero; return 0; }
            public int GetTrustLevel(out int trustLevel) { trustLevel = 0; return 0; }
            public int get_Name(out IntPtr name) { name = IntPtr.Zero; return 0; }
            public int put_Name(IntPtr name) => 0;

            public int GetEffectId(out Guid id) { id = ClsidD2D1GaussianBlur; return 0; }
            public int GetNamedPropertyMapping(IntPtr name, out uint index, out uint mapping)
            {
                index = 0;
                mapping = 0;
                return unchecked((int)0x80070057); // E_INVALIDARG
            }

            public int GetPropertyCount(out uint count) { count = 3; return 0; }

            public int GetProperty(uint index, out IntPtr value)
            {
                object? prop = index switch
                {
                    0 => PropertyValue.CreateSingle(_sigma), // D2D1_GAUSSIANBLUR_PROP_STANDARD_DEVIATION
                    1 => PropertyValue.CreateUInt32(1u),     // D2D1_GAUSSIANBLUR_PROP_OPTIMIZATION = BALANCED
                    2 => PropertyValue.CreateUInt32(1u),     // D2D1_GAUSSIANBLUR_PROP_BORDER_MODE = HARD
                    _ => null
                };
                if (prop == null) { value = IntPtr.Zero; return unchecked((int)0x80070057); }
                IntPtr insp = WinRT.MarshalInspectable<object>.FromManaged(prop);
                try { return Marshal.QueryInterface(insp, in IidIPropertyValue, out value); }
                finally { Marshal.Release(insp); }
            }

            public int GetSource(uint index, out IntPtr source)
            {
                if (index != 0) { source = IntPtr.Zero; return unchecked((int)0x80070057); }
                source = WinRT.MarshalInterface<IGraphicsEffectSource>.FromManaged(_source);
                return 0;
            }

            public int GetSourceCount(out uint count) { count = 1; return 0; }
        }
    }
}
