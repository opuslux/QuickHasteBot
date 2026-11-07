using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickHasteBot.UI;

/// <summary>
/// Shift+Mausrad scrollt waagrecht, wie in Browsern und den meisten Editoren. WPF kann das von sich aus nicht.
/// Gilt für einen ScrollViewer oder für das erste ScrollViewer darin (z.B. im AvalonEdit-Editor).
/// </summary>
internal static class ShiftWheel
{
    /// <summary>Pixel pro Raste, etwa drei Zeichen in der Monospace-Schrift mehr als eine Zeile hoch.</summary>
    const double Step = 48;

    public static readonly DependencyProperty HorizontalProperty = DependencyProperty.RegisterAttached(
        "Horizontal", typeof(bool), typeof(ShiftWheel), new PropertyMetadata(false, OnHorizontalChanged));

    public static bool GetHorizontal(DependencyObject element) => (bool)element.GetValue(HorizontalProperty);

    public static void SetHorizontal(DependencyObject element, bool value) => element.SetValue(HorizontalProperty, value);

    static void OnHorizontalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseWheel -= OnWheel;
        if ((bool)e.NewValue)
            element.PreviewMouseWheel += OnWheel;
    }

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Viewer((DependencyObject)sender) is { } viewer && Scroll(viewer, e.Delta, Keyboard.Modifiers))
            e.Handled = true;
    }

    /// <summary>Mit Shift waagrecht scrollen. False, wenn nicht Shift oder es nichts zu scrollen gibt: dann normal senkrecht.</summary>
    internal static bool Scroll(ScrollViewer viewer, int delta, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Shift || viewer.ScrollableWidth <= 0) return false;
        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - delta / 120.0 * Step);
        return true;
    }

    /// <summary>Das Element selbst, wenn es ein ScrollViewer ist, sonst das erste darin.</summary>
    internal static ScrollViewer? Viewer(DependencyObject element)
    {
        if (element is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            if (Viewer(VisualTreeHelper.GetChild(element, i)) is { } found)
                return found;
        }
        return null;
    }
}
