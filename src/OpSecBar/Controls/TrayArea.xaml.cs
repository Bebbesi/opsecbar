using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Interop;
using ManagedShell.WindowsTray;
using OpSecBar.Core;

namespace OpSecBar.Controls;

/// <summary>
/// Area di notifica (system tray). Le icone arrivano dal servizio tray di ManagedShell, che riceve le stesse
/// notifiche Shell_NotifyIcon della taskbar di Windows: click sinistro/destro e menu contestuali funzionano
/// come nella barra originale perché gli eventi del mouse vengono inoltrati all'app proprietaria dell'icona.
/// </summary>
public partial class TrayArea : UserControl
{
    private NotificationArea? _area;
    private AppBarEdge _edge = AppBarEdge.Top;
    private DateTime _overflowClosedAt = DateTime.MinValue;

    /// <summary>True quando il pannello delle icone nascoste è aperto (serve all'auto-hide della barra).</summary>
    public event Action<bool>? OverflowOpenChanged;

    public TrayArea()
    {
        InitializeComponent();
    }

    public void Attach(NotificationArea area, AppBarEdge edge)
    {
        _area = area;
        _edge = edge;
        PinnedItems.ItemsSource = area.PinnedIcons;
        OverflowItems.ItemsSource = area.UnpinnedIcons;

        bool vertical = edge is AppBarEdge.Left or AppBarEdge.Right;
        HostPanel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        ChevronGlyph.Text = edge switch
        {
            AppBarEdge.Bottom => "",
            AppBarEdge.Left => "",
            AppBarEdge.Right => "",
            _ => ""
        };
        Overflow.Placement = edge switch
        {
            AppBarEdge.Bottom => PlacementMode.Top,
            AppBarEdge.Left => PlacementMode.Right,
            AppBarEdge.Right => PlacementMode.Left,
            _ => PlacementMode.Bottom
        };
        if (vertical)
        {
            Chevron.Padding = new Thickness(0, 4, 0, 4);
            Chevron.Margin = new Thickness(3, 1, 3, 1);
        }
    }

    private void Chevron_Click(object sender, RoutedEventArgs e)
    {
        // Il popup si chiude da solo al click esterno (anche sul chevron): evita di riaprirlo subito.
        if ((DateTime.Now - _overflowClosedAt).TotalMilliseconds < 250) return;
        EmptyText.Visibility = OverflowItems.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Overflow.IsOpen = true;
    }

    private void Overflow_Opened(object? sender, EventArgs e) => OverflowOpenChanged?.Invoke(true);

    private void Overflow_Closed(object? sender, EventArgs e)
    {
        _overflowClosedAt = DateTime.Now;
        OverflowOpenChanged?.Invoke(false);
    }

    // ---- Inoltro degli eventi del mouse all'icona -------------------------------------------------

    private static NotifyIcon? IconOf(object sender) => (sender as FrameworkElement)?.DataContext as NotifyIcon;

    private void UpdatePlacement(FrameworkElement element, NotifyIcon icon)
    {
        try
        {
            var source = PresentationSource.FromVisual(element);
            if (source?.CompositionTarget == null) return;
            double scale = source.CompositionTarget.TransformToDevice.M11;
            Point p = element.PointToScreen(new Point(0, 0));
            // Per Shell_NotifyIconGetRect: Left/Top = posizione, Right/Bottom = dimensioni (in pixel).
            icon.Placement = new NativeMethods.Rect
            {
                Left = (int)p.X, Top = (int)p.Y,
                Right = (int)(element.ActualWidth * scale), Bottom = (int)(element.ActualHeight * scale)
            };

            // Posizione dell'host del tray: le app la usano per decidere dove aprire i propri flyout.
            if (_area != null && Window.GetWindow(this) is Window w)
            {
                Point hp = PointToScreen(new Point(0, 0));
                _area.SetTrayHostSizeData(new TrayHostSizeData
                {
                    edge = (NativeMethods.ABEdge)(int)_edge,
                    rc = new NativeMethods.Rect
                    {
                        Left = (int)hp.X, Top = (int)hp.Y,
                        Right = (int)(hp.X + ActualWidth * scale), Bottom = (int)(hp.Y + ActualHeight * scale)
                    }
                });
            }
        }
        catch (Exception ex) { Log.Warn("Tray placement: " + ex.Message); }
    }

    private void Icon_MouseEnter(object sender, MouseEventArgs e)
    {
        if (IconOf(sender) is not { } icon || sender is not FrameworkElement fe) return;
        fe.ToolTip = string.IsNullOrWhiteSpace(icon.Title) ? null : icon.Title;
        UpdatePlacement(fe, icon);
        icon.IconMouseEnter(MouseHelper.GetCursorPositionParam());
    }

    private void Icon_MouseLeave(object sender, MouseEventArgs e) =>
        IconOf(sender)?.IconMouseLeave(MouseHelper.GetCursorPositionParam());

    private void Icon_MouseMove(object sender, MouseEventArgs e)
    {
        if (_press != null && ReferenceEquals(_press.Element, sender))
        {
            Native.GetCursorPos(out Native.POINT p);
            if (!_press.Dragging && (Math.Abs(p.X - _press.Start.X) > DragThreshold || Math.Abs(p.Y - _press.Start.Y) > DragThreshold))
                BeginDrag();
            if (_press.Dragging)
            {
                _ghost?.MoveTo(p);
                return;
            }
        }
        IconOf(sender)?.IconMouseMove(MouseHelper.GetCursorPositionParam());
    }

    private void Icon_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IconOf(sender) is not { } icon || sender is not FrameworkElement fe) return;
        e.Handled = true;
        UpdatePlacement(fe, icon);

        if (e.ChangedButton == MouseButton.Left)
        {
            // Il click sinistro viene inoltrato al rilascio: se nel frattempo il puntatore si sposta, diventa un trascinamento.
            Native.GetCursorPos(out Native.POINT p);
            _press = new PressState(fe, icon, p);
            fe.CaptureMouse();
            return;
        }
        icon.IconMouseDown(e.ChangedButton, MouseHelper.GetCursorPositionParam(), (int)Native.GetDoubleClickTime());
    }

    private void Icon_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (IconOf(sender) is not { } icon) return;
        e.Handled = true;

        if (e.ChangedButton == MouseButton.Left && _press != null)
        {
            PressState press = _press;
            _press = null;
            press.Element.ReleaseMouseCapture();
            if (press.Dragging)
            {
                EndDrag(press, true);
                return;
            }
            uint pos = MouseHelper.GetCursorPositionParam();
            int dbl = (int)Native.GetDoubleClickTime();
            press.Icon.IconMouseDown(MouseButton.Left, pos, dbl);
            press.Icon.IconMouseUp(MouseButton.Left, pos, dbl);
            return;
        }
        icon.IconMouseUp(e.ChangedButton, MouseHelper.GetCursorPositionParam(), (int)Native.GetDoubleClickTime());
    }

    private void Icon_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // Cattura persa (Alt+Tab, finestra di sistema...): il trascinamento si annulla.
        if (_press != null && ReferenceEquals(_press.Element, sender) && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            PressState press = _press;
            _press = null;
            if (press.Dragging) EndDrag(press, false);
        }
    }

    // ---- Trascinamento: dal pannello delle icone nascoste alla barra (e viceversa) -----------------

    private const int DragThreshold = 6;
    private PressState? _press;
    private DragGhost? _ghost;

    private sealed class PressState(FrameworkElement element, NotifyIcon icon, Native.POINT start)
    {
        public FrameworkElement Element { get; } = element;
        public NotifyIcon Icon { get; } = icon;
        public Native.POINT Start { get; } = start;
        public bool Dragging { get; set; }
        /// <summary>True se l'icona parte dal pannello delle icone nascoste.</summary>
        public bool FromOverflow { get; } = !icon.IsPinned;
    }

    private void BeginDrag()
    {
        if (_press == null) return;
        _press.Dragging = true;
        _press.Element.Opacity = 0.35;
        _ghost = new DragGhost(_press.Icon.Icon);
        _ghost.Show();
        Native.GetCursorPos(out Native.POINT p);
        _ghost.MoveTo(p);
    }

    private void EndDrag(PressState press, bool commit)
    {
        press.Element.Opacity = 1;
        _ghost?.Close();
        _ghost = null;
        if (!commit || _area == null) return;

        try
        {
            Native.GetCursorPos(out Native.POINT p);
            TrayArea? target = TrayUnder(p, out bool overChevron);
            NotifyIcon icon = press.Icon;

            if (target == null || overChevron)
            {
                // Trascinata fuori dalla barra (o sul pulsante delle icone nascoste): torna tra le nascoste.
                if (!press.FromOverflow) icon.Unpin();
            }
            else
            {
                target.PinAt(icon, p);
            }
            if (press.FromOverflow)
            {
                Overflow.IsOpen = false;
                // Il pannello (StaysOpen=False) riprende la cattura del mouse quando la perde: senza questo
                // il primo click successivo sulla barra andrebbe perso.
                Dispatcher.BeginInvoke(() => { if (Mouse.Captured != null) Mouse.Capture(null); },
                    System.Windows.Threading.DispatcherPriority.Input);
            }

            App.Instance.Settings.PinnedTrayIcons = _area.PinnedNotifyIcons;
            App.Instance.SettingsService.Notify(nameof(Models.AppSettings.PinnedTrayIcons));
        }
        catch (Exception ex) { Log.Error("Trascinamento icona del tray", ex); }
    }

    /// <summary>L'area tray della barra sotto il punto (pixel fisici), se il punto cade su una barra.</summary>
    private static TrayArea? TrayUnder(Native.POINT p, out bool overChevron)
    {
        overChevron = false;
        foreach (var bar in App.Instance.Bars)
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(bar).Handle;
            if (hwnd == IntPtr.Zero || !bar.IsVisible || !Native.GetWindowRect(hwnd, out Native.RECT r)) continue;
            if (p.X < r.Left || p.X >= r.Right || p.Y < r.Top || p.Y >= r.Bottom) continue;
            TrayArea tray = bar.Tray;
            if (tray.Chevron.IsVisible)
            {
                Point c = tray.Chevron.PointToScreen(new Point(0, 0));
                Point c2 = tray.Chevron.PointToScreen(new Point(tray.Chevron.ActualWidth, tray.Chevron.ActualHeight));
                overChevron = p.X >= c.X && p.X <= c2.X && p.Y >= c.Y && p.Y <= c2.Y;
            }
            return tray.IsVisible ? tray : null;
        }
        return null;
    }

    /// <summary>Rende l'icona sempre visibile, inserendola tra le icone della barra nella posizione del puntatore.</summary>
    private void PinAt(NotifyIcon icon, Native.POINT p)
    {
        if (_area == null) return;
        bool vertical = HostPanel.Orientation == Orientation.Vertical;
        var order = _area.PinnedNotifyIcons.ToList();
        order.RemoveAll(icon.IsEqualByIdentifier);

        NotifyIcon? before = null, last = null;
        foreach (object item in PinnedItems.Items)
        {
            if (item is not NotifyIcon other || ReferenceEquals(other, icon)) continue;
            if (PinnedItems.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement fe || !fe.IsVisible) continue;
            Point center = fe.PointToScreen(new Point(fe.ActualWidth / 2, fe.ActualHeight / 2));
            if ((vertical ? p.Y < center.Y : p.X < center.X)) { before = other; break; }
            last = other;
        }

        int index = order.Count;
        if (before != null)
        {
            int i = order.FindIndex(before.IsEqualByIdentifier);
            if (i >= 0) index = i;
        }
        else if (last != null)
        {
            int i = order.FindIndex(last.IsEqualByIdentifier);
            if (i >= 0) index = i + 1;
        }
        order.Insert(index, icon.Identifier);
        _area.SetPinnedIcons(order.ToArray());
        try { _area.PinnedIcons.Refresh(); _area.UnpinnedIcons.Refresh(); } catch { }
    }
}

/// <summary>Piccola finestra con l'icona trascinata che segue il puntatore.</summary>
internal sealed class DragGhost : Window
{
    public DragGhost(System.Windows.Media.ImageSource? icon)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = -32000;
        Top = -32000;
        Content = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(7),
            Background = (System.Windows.Media.Brush)Application.Current.Resources["PanelSolidBrush"],
            BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["AccentBrush"], BorderThickness = new Thickness(1.5),
            Child = new Image { Source = icon, Width = 16, Height = 16 },
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.4, Direction = 270 }
        };
        Margin = new Thickness(0);
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            // Mai attivabile e "trasparente" al mouse: il drop si calcola dalla posizione del puntatore.
            int ex = GetWindowLong(hwnd, -20);
            SetWindowLong(hwnd, -20, ex | 0x08000000 /* NOACTIVATE */ | 0x20 /* TRANSPARENT */ | 0x80 /* TOOLWINDOW */);
            System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == 0x02E0 /* WM_DPICHANGED */) handled = true;
                return IntPtr.Zero;
            });
        };
    }

    public void MoveTo(Native.POINT p)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) { Left = p.X; Top = p.Y; return; }
        Native.SetWindowPos(hwnd, new IntPtr(-1), p.X - 15, p.Y - 15, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
