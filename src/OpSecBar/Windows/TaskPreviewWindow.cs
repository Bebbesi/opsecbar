using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ManagedShell.WindowsTasks;
using OpSecBar.Core;
using OpSecBar.Services;

namespace OpSecBar.Windows;

/// <summary>
/// Anteprime dal vivo delle finestre di un'app, mostrate sopra la sua icona (come la taskbar di Windows).
/// Le miniature sono "thumbnail" DWM: le disegna il compositore, aggiornate in tempo reale e senza catture.
/// Tenendo il mouse su un'anteprima si "sbircia" la finestra a grandezza reale (<see cref="PeekWindow"/>).
/// </summary>
internal sealed class TaskPreviewWindow : Window
{
    private const double MaxThumbWidth = 220, MaxThumbHeight = 124;
    private static readonly TimeSpan PeekDelay = TimeSpan.FromMilliseconds(550);

    private sealed class Card
    {
        public required ApplicationWindow Window;
        public required Border Root;
        public required Border Host;
        public IntPtr Thumb;
    }

    private readonly List<Card> _cards = new();
    private readonly StackPanel _panel;
    private readonly FrameworkElement _anchor;
    private readonly IntPtr _barHwnd;
    private readonly PlacementMode _placement;
    private readonly DispatcherTimer _peekTimer;
    private IntPtr _hwnd;
    private Card? _peekCandidate;
    private PeekWindow? _peek;

    // Trascinamento: soffermandosi su un'anteprima la sua finestra viene in primo piano, per rilasciarci dentro.
    private static readonly TimeSpan DragActivateDelay = TimeSpan.FromMilliseconds(400);
    private readonly DispatcherTimer _dragTimer;
    private Card? _dragCandidate;
    private int _dragMoves;

    /// <summary>L'utente ha cliccato o chiuso una finestra: la barra deve chiudere l'anteprima.</summary>
    public event Action? Dismissed;

    public TaskPreviewWindow(IEnumerable<ApplicationWindow> windows, FrameworkElement anchor, IntPtr barHwnd, PlacementMode placement)
    {
        _anchor = anchor;
        _barHwnd = barHwnd;
        _placement = placement;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = -32000;
        Top = -32000;
        Background = (Brush)Application.Current.Resources["PanelSolidBrush"];
        Foreground = (Brush)Application.Current.Resources["PanelFgBrush"];
        FontFamily = (FontFamily)Application.Current.Resources["BarFont"];
        FontSize = 12;

        bool vertical = placement is PlacementMode.Left or PlacementMode.Right;
        _panel = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Margin = new Thickness(6) };
        Content = _panel;

        _peekTimer = new DispatcherTimer { Interval = PeekDelay };
        _peekTimer.Tick += (_, _) =>
        {
            _peekTimer.Stop();
            if (_peekCandidate != null) ShowPeek(_peekCandidate);
        };

        // Qui non si rilascia nulla: l'anteprima serve solo a scegliere la finestra in cui proseguire il trascinamento.
        // Gli eventi non vengono marcati come gestiti, perché anche la barra li ascolta per tenere aperta l'anteprima.
        AllowDrop = true;
        DragEnter += (_, e) => e.Effects = DragDropEffects.None;
        DragOver += (_, e) => e.Effects = DragDropEffects.None;
        Drop += (_, e) => e.Effects = DragDropEffects.None;
        _dragTimer = new DispatcherTimer { Interval = DragActivateDelay };
        _dragTimer.Tick += (_, _) =>
        {
            _dragTimer.Stop();
            if (_dragCandidate is not { } card) return;
            Dismiss();
            TaskListService.ActivateDuringDrag(card.Window);
        };

        // L'handle serve prima di costruire le schede: le miniature DWM vanno registrate sulla nostra finestra.
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        int ex = GetWindowLong(_hwnd, -20);
        SetWindowLong(_hwnd, -20, ex | 0x08000000 /* NOACTIVATE */ | 0x80 /* TOOLWINDOW */);
        int round = 2; // DWMWCP_ROUND
        Native.DwmSetWindowAttribute(_hwnd, 33, ref round, sizeof(int));
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        foreach (var w in windows) AddCard(w);

        SizeChanged += (_, _) => { Reposition(); UpdateThumbnails(); };
        Loaded += (_, _) => { Reposition(); UpdateThumbnails(); };
        MouseLeave += (_, _) => SetPeekCandidate(null);
        Closed += (_, _) =>
        {
            _dragTimer.Stop();
            _peekTimer.Stop();
            HidePeek();
            foreach (var c in _cards) Unregister(c);
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0021 /* WM_MOUSEACTIVATE */)
        {
            handled = true;
            return new IntPtr(3); // MA_NOACTIVATE: cliccare un'anteprima non toglie il primo piano alle altre finestre
        }
        return IntPtr.Zero;
    }

    // ---- Schede ----------------------------------------------------------------------------------

    private void AddCard(ApplicationWindow w)
    {
        Native.DwmRegisterThumbnail(_hwnd, w.Handle, out IntPtr thumb);
        double tw = MaxThumbWidth, th = MaxThumbHeight;
        if (thumb != IntPtr.Zero && Native.DwmQueryThumbnailSourceSize(thumb, out Native.SIZE size) == 0 && size.cx > 0 && size.cy > 0)
        {
            double aspect = (double)size.cx / size.cy;
            if (aspect >= MaxThumbWidth / MaxThumbHeight) th = MaxThumbWidth / aspect;
            else tw = MaxThumbHeight * aspect;
            tw = Math.Max(tw, 60);
            th = Math.Max(th, 40);
        }

        var host = new Border { Width = tw, Height = th, HorizontalAlignment = HorizontalAlignment.Center };
        if (thumb == IntPtr.Zero)
            host.Child = new Image { Source = w.Icon, Width = 32, Height = 32 }; // niente miniatura: almeno l'icona

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(w.Title) ? "(untitled)" : w.Title, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0)
        };
        var close = new Button
        {
            Content = "✕", Width = 22, Height = 22, Padding = new Thickness(0), FontSize = 10, Focusable = false,
            Style = (Style)Application.Current.Resources["PanelButton"], ToolTip = "Close window", Visibility = Visibility.Hidden
        };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var icon = new Image { Source = w.Icon, Width = 16, Height = 16 };
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        header.Children.Add(title);

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(host);

        var root = new Border
        {
            Child = body, Padding = new Thickness(8), Margin = new Thickness(2), CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent, Width = Math.Max(tw, 140) + 16, Cursor = Cursors.Hand
        };
        var card = new Card { Window = w, Root = root, Host = host, Thumb = thumb };

        root.MouseEnter += (_, _) =>
        {
            root.Background = (Brush)Application.Current.Resources["PanelHoverBrush"];
            close.Visibility = Visibility.Visible;
            SetPeekCandidate(card);
        };
        root.MouseLeave += (_, _) =>
        {
            root.Background = Brushes.Transparent;
            close.Visibility = Visibility.Hidden;
            if (_peekCandidate == card) SetPeekCandidate(null);
        };
        root.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Dismiss();
            try { w.BringToFront(); } catch (Exception ex) { Log.Error("Anteprima: attivazione", ex); }
        };
        root.MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            e.Handled = true;
            CloseWindow(card);
        };
        close.Click += (_, _) => CloseWindow(card);

        void DragWithin(object? sender, DragEventArgs e)
        {
            _dragMoves++; // annulla un'eventuale uscita in sospeso
            SetDragCandidate(card);
        }
        root.DragEnter += DragWithin;
        root.DragOver += DragWithin;
        root.DragLeave += (_, _) =>
        {
            // Arriva anche passando da un elemento interno della scheda all'altro (subito seguito da un DragEnter).
            int moves = _dragMoves;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (moves == _dragMoves && _dragCandidate == card) SetDragCandidate(null);
            });
        };
        root.Drop += (_, _) => SetDragCandidate(null);

        _cards.Add(card);
        _panel.Children.Add(root);
    }

    private void CloseWindow(Card card)
    {
        try { card.Window.Close(); } catch (Exception ex) { Log.Error("Anteprima: chiusura", ex); }
        if (_peekCandidate == card) SetPeekCandidate(null);
        if (_dragCandidate == card) SetDragCandidate(null);
        Unregister(card);
        _cards.Remove(card);
        _panel.Children.Remove(card.Root);
        if (_cards.Count == 0) Dismiss();
    }

    private void SetDragCandidate(Card? card)
    {
        if (_dragCandidate == card) return;
        // Durante un trascinamento il mouse non "passa sopra" le schede: l'evidenziazione va accesa a mano.
        if (_dragCandidate != null) _dragCandidate.Root.Background = Brushes.Transparent;
        _dragCandidate = card;
        _dragTimer.Stop();
        if (card == null) return;
        card.Root.Background = (Brush)Application.Current.Resources["PanelHoverBrush"];
        _dragTimer.Start();
    }

    private void Dismiss()
    {
        SetDragCandidate(null);
        SetPeekCandidate(null);
        Dismissed?.Invoke();
    }

    private static void Unregister(Card card)
    {
        if (card.Thumb == IntPtr.Zero) return;
        Native.DwmUnregisterThumbnail(card.Thumb);
        card.Thumb = IntPtr.Zero;
    }

    /// <summary>Allinea ogni miniatura DWM al suo riquadro (in pixel fisici, rispetto all'area client).</summary>
    private void UpdateThumbnails()
    {
        if (!IsLoaded) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        foreach (var c in _cards)
        {
            if (c.Thumb == IntPtr.Zero || !c.Host.IsLoaded) continue;
            Point p = c.Host.TransformToAncestor(this).Transform(new Point(0, 0));
            var props = new Native.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY | Native.DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new Native.RECT
                {
                    Left = (int)Math.Round(p.X * scale), Top = (int)Math.Round(p.Y * scale),
                    Right = (int)Math.Round((p.X + c.Host.ActualWidth) * scale), Bottom = (int)Math.Round((p.Y + c.Host.ActualHeight) * scale)
                },
                opacity = 255, fVisible = true, fSourceClientAreaOnly = false
            };
            Native.DwmUpdateThumbnailProperties(c.Thumb, ref props);
        }
    }

    // ---- Posizione -------------------------------------------------------------------------------

    /// <summary>Centra l'anteprima sull'icona, dal lato del desktop, restando dentro il monitor.</summary>
    public void Reposition()
    {
        if (_hwnd == IntPtr.Zero || !_anchor.IsLoaded || PresentationSource.FromVisual(_anchor) == null) return;
        if (!Native.GetWindowRect(_hwnd, out Native.RECT me)) return;

        Point a0 = _anchor.PointToScreen(new Point(0, 0));
        Point a1 = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight));
        const int gap = 10;
        double cx = (a0.X + a1.X) / 2, cy = (a0.Y + a1.Y) / 2;
        double x, y;
        switch (_placement)
        {
            case PlacementMode.Top: x = cx - me.Width / 2.0; y = a0.Y - me.Height - gap; break;
            case PlacementMode.Bottom: x = cx - me.Width / 2.0; y = a1.Y + gap; break;
            case PlacementMode.Left: x = a0.X - me.Width - gap; y = cy - me.Height / 2.0; break;
            default: x = a1.X + gap; y = cy - me.Height / 2.0; break;
        }

        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        IntPtr monitor = Native.MonitorFromPoint(new Native.POINT { X = (int)cx, Y = (int)cy }, Native.MONITOR_DEFAULTTONEAREST);
        if (Native.GetMonitorInfo(monitor, ref info))
        {
            Native.RECT m = info.rcMonitor;
            x = Math.Clamp(x, m.Left + 4, Math.Max(m.Left + 4, m.Right - me.Width - 4));
            y = Math.Clamp(y, m.Top + 4, Math.Max(m.Top + 4, m.Bottom - me.Height - 4));
        }

        Native.SetWindowPos(_hwnd, new IntPtr(-1), (int)Math.Round(x), (int)Math.Round(y), 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    // ---- Sbirciata -------------------------------------------------------------------------------

    private void SetPeekCandidate(Card? card)
    {
        if (_peekCandidate == card) return;
        _peekCandidate = card;
        _peekTimer.Stop();
        if (card == null)
        {
            HidePeek();
            return;
        }
        if (_peek != null) ShowPeek(card); // già in sbirciata: si passa subito all'altra finestra
        else _peekTimer.Start();
    }

    private void ShowPeek(Card card)
    {
        try
        {
            _peek ??= new PeekWindow();
            _peek.Show(card.Window.Handle, _barHwnd);
            // L'anteprima resta sopra la sbirciata.
            Native.SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            Log.Error("Sbirciata", ex);
            HidePeek();
        }
    }

    private void HidePeek()
    {
        _peek?.Close();
        _peek = null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}

/// <summary>
/// "Sbirciata": oscura lo schermo e mostra la finestra scelta a grandezza reale, nella sua posizione, sopra tutto il resto.
/// È una miniatura DWM a dimensione piena, quindi la finestra non viene davvero spostata né attivata.
/// </summary>
internal sealed class PeekWindow : Window
{
    private IntPtr _hwnd, _thumb;

    public PeekWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        int ex = GetWindowLong(_hwnd, -20);
        // Mai attivabile e trasparente al mouse: il puntatore resta "sull'anteprima" anche se la sbirciata copre lo schermo.
        SetWindowLong(_hwnd, -20, ex | 0x08000000 /* NOACTIVATE */ | 0x20 /* TRANSPARENT */ | 0x80 /* TOOLWINDOW */);
        HwndSource.FromHwnd(_hwnd)?.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == 0x02E0 /* WM_DPICHANGED */) handled = true; // dimensioni sempre in pixel fisici, decise da noi
            return IntPtr.Zero;
        });
        Closed += (_, _) => Unregister();
    }

    public void Show(IntPtr target, IntPtr barHwnd)
    {
        if (!TargetRect(target, out Native.RECT win, out Native.RECT m)) return;

        Unregister();
        if (Native.DwmRegisterThumbnail(_hwnd, target, out _thumb) != 0) _thumb = IntPtr.Zero;

        if (!IsVisible) Show();
        // Sotto la barra (che resta visibile, come in Windows) ma sopra tutte le altre finestre.
        Native.SetWindowPos(_hwnd, barHwnd != IntPtr.Zero ? barHwnd : new IntPtr(-1), m.Left, m.Top, m.Width, m.Height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        if (_thumb == IntPtr.Zero) return;
        var props = new Native.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY | Native.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new Native.RECT { Left = win.Left - m.Left, Top = win.Top - m.Top, Right = win.Right - m.Left, Bottom = win.Bottom - m.Top },
            opacity = 255, fVisible = true, fSourceClientAreaOnly = false
        };
        Native.DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    /// <summary>
    /// Dove si trova la finestra (pixel fisici) e il monitor che la contiene. Se è ridotta a icona si usa la posizione
    /// in cui tornerebbe ripristinandola, come fa la sbirciata di Windows.
    /// </summary>
    private static bool TargetRect(IntPtr target, out Native.RECT win, out Native.RECT monitor)
    {
        monitor = default;
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.IsIconic(target))
        {
            if (!Native.GetWindowRect(target, out win)) return false;
            if (!Native.GetMonitorInfo(Native.MonitorFromWindow(target, Native.MONITOR_DEFAULTTONEAREST), ref info)) return false;
            monitor = info.rcMonitor;
            return true;
        }

        var placement = new Native.WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
        win = default;
        if (!Native.GetWindowPlacement(target, ref placement)) return false;
        Native.RECT normal = placement.rcNormalPosition;
        if (!Native.GetMonitorInfo(Native.MonitorFromRect(ref normal, Native.MONITOR_DEFAULTTONEAREST), ref info)) return false;
        monitor = info.rcMonitor;
        Native.RECT work = info.rcWork;
        if ((placement.flags & Native.WPF_RESTORETOMAXIMIZED) != 0)
        {
            win = work; // tornerebbe massimizzata: occupa l'area di lavoro
            return true;
        }
        // rcNormalPosition è in coordinate dell'area di lavoro: si riportano a coordinate dello schermo.
        int dx = work.Left - monitor.Left, dy = work.Top - monitor.Top;
        win = new Native.RECT { Left = normal.Left + dx, Top = normal.Top + dy, Right = normal.Right + dx, Bottom = normal.Bottom + dy };
        return true;
    }

    private void Unregister()
    {
        if (_thumb != IntPtr.Zero) Native.DwmUnregisterThumbnail(_thumb);
        _thumb = IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
