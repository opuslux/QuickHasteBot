using System.Windows;
using System.Windows.Controls;

namespace QuickHasteBot.UI;

/// <summary>
/// Füllt den Platz, den die Nachbarn vorgeben, ohne selbst Höhe zu verlangen. So wächst das Fenster
/// nicht mit der rechten Spalte mit, die rechte Spalte scrollt stattdessen.
/// </summary>
internal sealed class FillDecorator : Decorator
{
    protected override Size MeasureOverride(Size constraint) => default;

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        // Erst hier ist die Grösse bekannt, mit ihr messen, damit die Scrollbalken stimmen
        Child?.Measure(arrangeSize);
        Child?.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }
}
