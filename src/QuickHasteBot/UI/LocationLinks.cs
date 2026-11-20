using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace QuickHasteBot.UI;

/// <summary>
/// Füllt einen TextBlock mit dem Ordnerpfad eines Treffers, jeder Ordner ein eigener Run mit seinem Pfad im Tag.
/// Ein TextBlock statt einzelner Elemente, damit ein langer Pfad weiterhin mit "…" gekürzt wird.
/// </summary>
internal static class LocationLinks
{
    public static readonly DependencyProperty CrumbsProperty = DependencyProperty.RegisterAttached(
        "Crumbs", typeof(IReadOnlyList<Breadcrumb>), typeof(LocationLinks), new PropertyMetadata(null, OnCrumbsChanged));

    public static IReadOnlyList<Breadcrumb>? GetCrumbs(DependencyObject element) => (IReadOnlyList<Breadcrumb>?)element.GetValue(CrumbsProperty);

    public static void SetCrumbs(DependencyObject element, IReadOnlyList<Breadcrumb>? value) => element.SetValue(CrumbsProperty, value);

    /// <summary>Der Ordnerpfad, wenn die Maus auf einem der Ordner steht, sonst null.</summary>
    public static string? TargetAt(object source) => source is Run { Tag: string target } run && GetIsLink(run) ? target : null;

    static readonly DependencyProperty IsLinkProperty = DependencyProperty.RegisterAttached(
        "IsLink", typeof(bool), typeof(LocationLinks), new PropertyMetadata(false));

    static bool GetIsLink(DependencyObject element) => (bool)element.GetValue(IsLinkProperty);

    static void OnCrumbsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text) return;

        text.Inlines.Clear();
        foreach (var crumb in (IReadOnlyList<Breadcrumb>?)e.NewValue ?? [])
        {
            if (crumb.ShowSeparator)
                text.Inlines.Add(new Run(" › "));

            // Unterstrichen beim Darüberfahren statt in Akzentfarbe: bleibt auch auf der markierten Zeile lesbar
            var link = new Run(crumb.Name) { Tag = crumb.Target, Cursor = Cursors.Hand };
            link.SetValue(IsLinkProperty, true);
            link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (_, _) => link.TextDecorations = null;
            text.Inlines.Add(link);
        }
    }
}
