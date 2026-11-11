using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpSecBar.Core;

/// <summary>Piccoli aiuti condivisi per costruire la UI da codice.</summary>
public static class Ui
{
    public static FontFamily GlyphFont => (FontFamily)Application.Current.Resources["GlyphFont"];

    /// <summary>Voce di menu con icona (glifo Segoe Fluent/MDL2) e azione protetta da eccezioni.</summary>
    public static MenuItem MenuEntry(string text, string glyph, Action action)
    {
        var item = new MenuItem
        {
            Header = text,
            Icon = new TextBlock { Text = glyph, FontSize = 13, FontFamily = GlyphFont, VerticalAlignment = VerticalAlignment.Center }
        };
        item.Click += (_, _) =>
        {
            try { action(); } catch (Exception ex) { Log.Error("Comando di menu: " + text, ex); }
        };
        return item;
    }
}
