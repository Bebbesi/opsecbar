using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ManagedShell.AppBar;
using OpSecBar.Core;
using OpSecBar.Models;
using OpSecBar.Services;

namespace OpSecBar.Windows;

/// <summary>
/// Base dei pannelli che si aprono dalla barra (menu Start, calendario, impostazioni rapide): finestra trasparente con
/// margine per l'ombra, posizionata accanto all'elemento che l'ha aperta, con dissolvenza, breve scorrimento e sfocatura
/// dello sfondo. Si chiude da sola quando perde il primo piano.
/// </summary>
public class FlyoutWindow : Window
{
    /// <summary>Margine trasparente attorno al pannello (in DIP): il contenuto va messo in un Grid con questo margine.</summary>
    protected const double ShadowMargin = 14;
    private const double PanelRadius = 14;

    protected App App { get; }
    protected TaskbarWindow? Bar { get; private set; }

    private FrameworkElement? _anchor;
    private IntPtr _hwnd;
    private bool _forceClose, _revealed;
    private BackdropWindow? _backdrop;
    private DateTime _hiddenAt = DateTime.MinValue;
    private int _openSeq;

    public FlyoutWindow(App app)
    {
        App = app;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SnapsToDevicePixels = true;
        FontSize = 13;
        SetResourceReference(FontFamilyProperty, "BarFont");
        SetResourceReference(ForegroundProperty, "PanelFgBrush");

        SourceInitialized += (_, _) => _hwnd = new WindowInteropHelper(this).Handle;
        Deactivated += (_, _) => HideFlyout();
        // Un pannello alto quanto il contenuto può cambiare altezza mentre è aperto: resta agganciato alla barra.
        SizeChanged += (_, _) =>
        {
            if (!IsVisible) return;
            PositionNearAnchor();
            if (_revealed) ApplyBackdrop();
        };
    }

    // ---- Punti di estensione ---------------------------------------------------------------------

    /// <summary>Prima che il pannello compaia: contenuto da aggiornare.</summary>
    protected virtual void OnOpening() { }

    /// <summary>Il pannello è visibile e in primo piano: fuoco iniziale, caricamenti.</summary>
    protected virtual void OnOpened() { }

    /// <summary>Il pannello è stato nascosto.</summary>
    protected virtual void OnHidden() { }

    /// <summary>Tema o dimensioni cambiati: le sottoclassi aggiornano la propria misura prima di chiamare la base.</summary>
    public virtual void ApplyTheme()
    {
        if (!IsVisible) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            PositionNearAnchor();
            ApplyBackdrop();
        });
    }

    // ---- Apertura / chiusura ---------------------------------------------------------------------

    public void Toggle(TaskbarWindow bar, FrameworkElement anchor)
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }
        // Il click sul pulsante della barra ha appena chiuso il pannello (per disattivazione): non riaprirlo.
        if ((DateTime.Now - _hiddenAt).TotalMilliseconds < 250) return;

        Bar = bar;
        _anchor = anchor;
        ApplyTheme();
        OnOpening();

        // L'animazione dell'apertura precedente tiene ancora Opacity a 1: va tolta, altrimenti "Opacity = 0" non ha effetto
        // e il pannello compare pieno per un fotogramma prima della dissolvenza (lo sfarfallio).
        BeginAnimation(OpacityProperty, null);
        StopSlide();
        _hasRest = false;
        Opacity = 0;

        // Una finestra trasparente, quando torna visibile, mostra per un istante l'ultimo fotogramma di prima (pannello pieno,
        // vecchia posizione). La si tiene "occultata" (DWM cloak) finché WPF non ha disegnato il nuovo fotogramma trasparente.
        if (_hwnd == IntPtr.Zero) _hwnd = new WindowInteropHelper(this).EnsureHandle();
        Cloak(true);
        Show();
        PositionNearAnchor();
        Activate();
        Native.ForceForeground(_hwnd); // necessario quando il pannello è aperto da tastiera
        OnOpened();
        bar.NotifyAutoHideStateChanged();

        // Dopo il primo layout (e l'eventuale cambio DPI del monitor) si riposiziona; un paio di fotogrammi dopo, quando
        // il contenuto trasparente è davvero sullo schermo, si scopre la finestra e parte la breve animazione.
        int opening = ++_openSeq;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (opening != _openSeq) return;
            PositionNearAnchor();
            AfterFrames(2, () =>
            {
                if (opening != _openSeq || !IsVisible) return;
                // Punto di partenza dello scorrimento: la lastra sfocata nasce già lì, sotto il pannello.
                AppBarEdge edge = Bar?.AppBarEdge ?? AppBarEdge.Top;
                MoveSlide((int)Math.Round((edge == AppBarEdge.Bottom ? 10 : -10) * Scale));
                _revealed = true;
                ApplyBackdrop();
                Cloak(false);
                AnimateIn();
            });
        });
    }

    /// <summary>Nasconde (o scopre) la finestra a schermo senza toglierle visibilità, attivazione o rendering.</summary>
    private void Cloak(bool on)
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            int value = on ? 1 : 0;
            Native.DwmSetWindowAttribute(_hwnd, 13 /* DWMWA_CLOAK */, ref value, sizeof(int));
        }
        catch { /* senza DWM resta comunque la dissolvenza da Opacity 0 */ }
    }

    private static void AfterFrames(int frames, Action action)
    {
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            if (--frames > 0) return;
            CompositionTarget.Rendering -= tick;
            action();
        };
        CompositionTarget.Rendering += tick;
    }

    public void HideFlyout()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.Now;
        _openSeq++; // annulla un'apertura ancora in corso
        _revealed = false;
        StopSlide();
        _backdrop?.Hide();
        Hide();
        Bar?.NotifyAutoHideStateChanged();
        OnHidden();
    }

    public void ForceClose()
    {
        _forceClose = true;
        _backdrop?.Dispose();
        _backdrop = null;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            HideFlyout();
        }
        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        e.Handled = true;
        HideFlyout();
    }

    /// <summary>Apre un indirizzo o un file con la shell (es. "ms-settings:sound") dopo aver chiuso il pannello.</summary>
    protected void OpenShell(string target)
    {
        HideFlyout();
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Log.Error("Apertura di " + target, ex); }
    }

    // ---- Animazione ------------------------------------------------------------------------------

    private double Scale => Bar?.DpiScale > 0 ? Bar!.DpiScale : 1;

    // Lo scorrimento sposta la finestra (e la lastra sfocata) di pixel interi, invece di traslare il contenuto: con una
    // traslazione WPF il pannello passa per posizioni frazionarie, testo e bordo da 1 px si sfumano e poi "scattano"
    // nitidi a fine animazione (uno sfarfallio). Spostando la finestra non si ridisegna nulla: resta sempre nitida.
    private (int X, int Y) _rest;   // posizione finale della finestra, in pixel fisici
    private (int W, int H) _size;   // dimensione della finestra usata per calcolare _rest, in pixel fisici
    private bool _hasRest;
    private int _slideOffset;       // scostamento verticale attuale rispetto a _rest
    private EventHandler? _slideTick;

    private void AnimateIn()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        int from = _slideOffset;
        if (from == 0) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _slideTick = (_, _) =>
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / 200);
            double left = Math.Pow(1 - t, 3); // decelerazione cubica
            MoveSlide((int)Math.Round(from * left));
            if (t >= 1) StopSlide();
        };
        CompositionTarget.Rendering += _slideTick;
    }

    private void MoveSlide(int offset)
    {
        if (!_hasRest || offset == _slideOffset) return;
        _slideOffset = offset;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, _rest.X, _rest.Y + offset, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        int pad = (int)Math.Round(ShadowMargin * Scale);
        _backdrop?.MoveTo(_rest.X + pad, _rest.Y + offset + pad);
    }

    private void StopSlide()
    {
        if (_slideTick != null) CompositionTarget.Rendering -= _slideTick;
        _slideTick = null;
        _slideOffset = 0;
    }

    // ---- Posizione e sfondo ----------------------------------------------------------------------

    /// <summary>Posiziona il pannello accanto all'elemento della barra, in pixel fisici (corretto con qualsiasi DPI e monitor).</summary>
    private void PositionNearAnchor()
    {
        if (Bar == null || _anchor == null || _hwnd == IntPtr.Zero) return;
        try
        {
            if (!Native.GetWindowRect(Bar.Handle, out Native.RECT barRect)) return;
            if (PanelSize() is not var (w, h)) return;
            var screen = Bar.Screen.Bounds;
            double scale = Scale;
            int pad = (int)Math.Round(ShadowMargin * scale);
            int gap = (int)Math.Round(6 * scale);

            // Se l'elemento non è più sulla barra (nascosto dalle impostazioni mentre il pannello è aperto) ci si allinea alla barra.
            FrameworkElement anchor = _anchor.IsVisible ? _anchor : Bar;
            Point a = anchor.PointToScreen(new Point(0, 0));
            int anchorLeft = (int)a.X, anchorTop = (int)a.Y;
            int anchorRight = (int)(a.X + anchor.ActualWidth * scale);
            int anchorBottom = (int)(a.Y + anchor.ActualHeight * scale);

            // Lungo la barra il pannello si allinea al lato in cui si trova il pulsante (che il layout avanzato può mettere
            // ovunque): all'inizio parte dal pulsante, al centro gli sta sopra centrato, alla fine termina con lui.
            static int Along(int from, int to, int barFrom, int barTo, int size, int pad)
            {
                int middle = (from + to) / 2, third = (barTo - barFrom) / 3;
                if (middle < barFrom + third) return from - pad;
                if (middle > barTo - third) return to - size + pad;
                return middle - size / 2;
            }

            int x, y;
            switch (Bar.AppBarEdge)
            {
                case AppBarEdge.Bottom:
                    x = Along(anchorLeft, anchorRight, barRect.Left, barRect.Right, w, pad);
                    y = barRect.Top - h + pad - gap;
                    break;
                case AppBarEdge.Left:
                    x = barRect.Right - pad + gap;
                    y = Along(anchorTop, anchorBottom, barRect.Top, barRect.Bottom, h, pad);
                    break;
                case AppBarEdge.Right:
                    x = barRect.Left - w + pad - gap;
                    y = Along(anchorTop, anchorBottom, barRect.Top, barRect.Bottom, h, pad);
                    break;
                default:
                    x = Along(anchorLeft, anchorRight, barRect.Left, barRect.Right, w, pad);
                    y = barRect.Bottom - pad + gap;
                    break;
            }

            // Il pannello visibile resta sempre dentro lo schermo.
            x = Math.Clamp(x, screen.Left - pad + gap, Math.Max(screen.Left - pad + gap, screen.Right - w + pad - gap));
            y = Math.Clamp(y, screen.Top - pad + gap, Math.Max(screen.Top - pad + gap, screen.Bottom - h + pad - gap));

            _rest = (x, y);
            _size = (w, h);
            _hasRest = true;
            Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y + _slideOffset, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
        catch (Exception ex) { Log.Error("Posizionamento pannello", ex); }
    }

    /// <summary>
    /// Dimensione della finestra in pixel fisici, presa dal layout WPF. Quando il contenuto cambia altezza (es. pagina
    /// Wi-Fi delle impostazioni rapide) SizeChanged arriva prima che WPF ridimensioni la finestra vera: in quel momento
    /// GetWindowRect restituisce ancora la misura vecchia, e pannello e sfocatura finirebbero nel posto sbagliato.
    /// </summary>
    private (int W, int H)? PanelSize()
    {
        if (ActualWidth > 0 && ActualHeight > 0)
            return ((int)Math.Round(ActualWidth * Scale), (int)Math.Round(ActualHeight * Scale));
        return Native.GetWindowRect(_hwnd, out Native.RECT r) ? (r.Width, r.Height) : null;
    }

    /// <summary>Con i temi blur/acrylic/glass anche i pannelli sfocano ciò che hanno dietro.</summary>
    private void ApplyBackdrop()
    {
        if (_hwnd == IntPtr.Zero) return;
        ThemeData t = App.Settings.Theme;
        bool blur = IsVisible && BackdropHelper.BlurActive(t.Backdrop) && t.PanelOpacity < 0.99;
        try
        {
            // La lastra segue la posizione e la misura calcolate per il pannello, non quelle (forse vecchie) della finestra.
            Native.RECT r = default;
            bool known = _hasRest || Native.GetWindowRect(_hwnd, out r);
            if (_hasRest) r = new Native.RECT { Left = _rest.X, Top = _rest.Y + _slideOffset, Right = _rest.X + _size.W, Bottom = _rest.Y + _slideOffset + _size.H };
            if (blur && known)
            {
                double scale = Scale;
                int pad = (int)Math.Round(ShadowMargin * scale);
                _backdrop ??= new BackdropWindow();
                _backdrop.Show(_hwnd, r.Left + pad, r.Top + pad, r.Width - pad * 2, r.Height - pad * 2, (int)Math.Round(PanelRadius * scale),
                    BackdropMode.Blur, ThemeService.ParseColor(t.PanelColor, Colors.Black), 0);
            }
            else _backdrop?.Hide();
        }
        catch (Exception ex) { Log.Error("Sfondo del pannello", ex); }
    }
}
