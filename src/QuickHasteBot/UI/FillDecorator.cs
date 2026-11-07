using System.Windows;
using System.Windows.Controls;

namespace QuickHasteBot.UI;

/// <summary>
/// Füllt den Platz, den die Nachbarn vorgeben, ohne selbst Höhe zu verlangen. So wächst das Fenster
/// nicht mit der rechten Spalte mit, die rechte Spalte scrollt stattdessen.
/// Mit <see cref="Grow"/> verlangt es die Höhe des Inhalts, damit z.B. eine Ordnerliste ohne Scrollbalken auskommt.
/// Diese Höhe wird nur grösser, bis <see cref="ResetGrowth"/>: Sonst würde das Fenster beim Überfahren der Liste
/// mit der Maus wachsen und schrumpfen, und die Einträge rutschten unter dem Mauszeiger weg.
/// </summary>
internal sealed class FillDecorator : Decorator
{
    public static readonly DependencyProperty GrowProperty = DependencyProperty.Register(
        nameof(Grow), typeof(bool), typeof(FillDecorator),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public bool Grow
    {
        get => (bool)GetValue(GrowProperty);
        set => SetValue(GrowProperty, value);
    }

    double _grown;
    bool _measuringForArrange;

    /// <summary>Die gemerkte Höhe vergessen, z.B. wenn die linke Liste neu aufgebaut wird.</summary>
    public void ResetGrowth()
    {
        _grown = 0;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Grow && Child is { } child)
        {
            // Höchstens so hoch wie der Platz, den das Fenster hergibt, sonst baut eine lange Liste alle Zeilen auf
            var height = double.IsFinite(constraint.Height) ? constraint.Height : SystemParameters.VirtualScreenHeight;
            child.Measure(new Size(constraint.Width, height));
            _grown = Math.Max(_grown, child.DesiredSize.Height);
        }
        return new Size(0, _grown);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        // Erst hier ist die Grösse bekannt, mit ihr messen, damit die Scrollbalken stimmen
        _measuringForArrange = true;
        try
        {
            Child?.Measure(arrangeSize);
        }
        finally
        {
            _measuringForArrange = false;
        }
        Child?.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }

    // Das Messen in ArrangeOverride ändert die Wunschgrösse des Inhalts (andere Vorgabe als beim Messen für Grow).
    // Das darf kein neues Messen auslösen, sonst ginge das Layout im Kreis.
    protected override void OnChildDesiredSizeChanged(UIElement child)
    {
        if (!_measuringForArrange)
            base.OnChildDesiredSizeChanged(child);
    }
}
