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

    private void Icon_MouseMove(object sender, MouseEventArgs e) =>
        IconOf(sender)?.IconMouseMove(MouseHelper.GetCursorPositionParam());

    private void Icon_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IconOf(sender) is not { } icon || sender is not FrameworkElement fe) return;
        e.Handled = true;
        UpdatePlacement(fe, icon);
        icon.IconMouseDown(e.ChangedButton, MouseHelper.GetCursorPositionParam(), (int)Native.GetDoubleClickTime());
    }

    private void Icon_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (IconOf(sender) is not { } icon) return;
        e.Handled = true;
        icon.IconMouseUp(e.ChangedButton, MouseHelper.GetCursorPositionParam(), (int)Native.GetDoubleClickTime());
    }
}
