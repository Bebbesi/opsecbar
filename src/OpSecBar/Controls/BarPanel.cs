using System.Windows;
using System.Windows.Controls;
using OpSecBar.Models;

namespace OpSecBar.Controls;

/// <summary>
/// Pannello della barra: dispone gli elementi in tre zone (inizio, centro, fine) lungo la barra.
/// La zona centrale è centrata sulla barra finché c'è posto, altrimenti scivola di lato senza sovrapporsi alle altre.
/// Un elemento "flessibile" (l'elenco delle app) riceve lo spazio che avanza e, se non basta, scorre.
/// Zona e ordine sono proprietà associate: per spostare un elemento non serve toglierlo dall'albero visuale.
/// </summary>
public sealed class BarPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(BarPanel),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ZoneProperty = DependencyProperty.RegisterAttached(
        "Zone", typeof(BarZone), typeof(BarPanel),
        new FrameworkPropertyMetadata(BarZone.Start, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty OrderProperty = DependencyProperty.RegisterAttached(
        "Order", typeof(int), typeof(BarPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty FlexProperty = DependencyProperty.RegisterAttached(
        "Flex", typeof(bool), typeof(BarPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static BarZone GetZone(DependencyObject d) => (BarZone)d.GetValue(ZoneProperty);
    public static void SetZone(DependencyObject d, BarZone value) => d.SetValue(ZoneProperty, value);
    public static int GetOrder(DependencyObject d) => (int)d.GetValue(OrderProperty);
    public static void SetOrder(DependencyObject d, int value) => d.SetValue(OrderProperty, value);
    public static bool GetFlex(DependencyObject d) => (bool)d.GetValue(FlexProperty);
    public static void SetFlex(DependencyObject d, bool value) => d.SetValue(FlexProperty, value);

    private bool Horizontal => Orientation == Orientation.Horizontal;
    private double Main(Size s) => Horizontal ? s.Width : s.Height;
    private double Cross(Size s) => Horizontal ? s.Height : s.Width;
    private Size Make(double main, double cross) => Horizontal ? new Size(main, cross) : new Size(cross, main);

    protected override Size MeasureOverride(Size availableSize)
    {
        double main = Main(availableSize), cross = Cross(availableSize);
        bool bounded = !double.IsInfinity(main);
        double used = 0, thickness = 0;
        int flexible = 0;

        // Prima gli elementi a dimensione propria...
        foreach (UIElement child in InternalChildren)
        {
            if (bounded && GetFlex(child))
            {
                flexible++;
                continue;
            }
            child.Measure(Make(double.PositiveInfinity, cross));
            used += Main(child.DesiredSize);
            thickness = Math.Max(thickness, Cross(child.DesiredSize));
        }

        // ...poi quelli flessibili, che si dividono lo spazio rimasto.
        if (flexible > 0)
        {
            double share = Math.Max(0, main - used) / flexible;
            foreach (UIElement child in InternalChildren)
            {
                if (!GetFlex(child)) continue;
                child.Measure(Make(share, cross));
                used += Main(child.DesiredSize);
                thickness = Math.Max(thickness, Cross(child.DesiredSize));
            }
        }

        return Make(bounded ? Math.Min(used, main) : used, thickness);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double main = Main(finalSize), cross = Cross(finalSize);

        var children = InternalChildren.Cast<UIElement>().OrderBy(GetOrder).ToList(); // ordinamento stabile
        double start = 0, center = 0, end = 0;
        foreach (UIElement child in children)
        {
            double length = Main(child.DesiredSize);
            switch (GetZone(child))
            {
                case BarZone.Start: start += length; break;
                case BarZone.Center: center += length; break;
                default: end += length; break;
            }
        }

        // La fine resta attaccata al bordo; il centro sta in mezzo alla barra, ma mai sopra l'inizio o la fine.
        double endAt = Math.Max(0, main - end);
        double centerAt = Math.Round((main - center) / 2);
        centerAt = Math.Max(start, Math.Min(centerAt, endAt - center));

        double startPos = 0, centerPos = centerAt, endPos = endAt;
        foreach (UIElement child in children)
        {
            double length = Main(child.DesiredSize);
            double at, limit;
            switch (GetZone(child))
            {
                case BarZone.Start: at = startPos; startPos += length; limit = endAt; break;
                case BarZone.Center: at = centerPos; centerPos += length; limit = endAt; break;
                default: at = endPos; endPos += length; limit = Math.Max(main, endAt + end); break;
            }
            // Se lo spazio non basta, ciò che sborda viene tagliato invece di finire sopra la zona accanto.
            double visible = Math.Max(0, Math.Min(length, limit - at));
            child.Arrange(Horizontal ? new Rect(at, 0, visible, cross) : new Rect(0, at, cross, visible));
        }
        return finalSize;
    }
}
