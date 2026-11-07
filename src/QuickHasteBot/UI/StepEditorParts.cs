using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace QuickHasteBot.UI;

/// <summary>
/// Haltepunkte im linken Rand des Editors: ein Klick setzt oder entfernt einen roten Punkt. Sie hängen an der Zeile,
/// nicht an der Zeilennummer, und wandern beim Tippen mit. Gelten nur, solange der Editor offen ist.
/// </summary>
internal sealed class BreakpointMargin : AbstractMargin
{
    const double MarginWidth = 16;
    const double Radius = 4.5;

    static readonly Brush Dot = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)));

    readonly HashSet<DocumentLine> _lines = [];

    public BreakpointMargin()
    {
        Cursor = Cursors.Hand;
        ToolTip = Strings.EditorBreakpointToolTip;
    }

    /// <summary>Die Zeilennummern (ab 1) mit Haltepunkt, aufsteigend.</summary>
    public IReadOnlyList<int> Lines => _lines.Where(l => !l.IsDeleted).Select(l => l.LineNumber).Order().ToList();

    public bool Has(int lineNumber) => _lines.Any(l => !l.IsDeleted && l.LineNumber == lineNumber);

    public void Toggle(DocumentLine line)
    {
        _lines.RemoveWhere(l => l.IsDeleted);
        if (!_lines.Remove(line))
            _lines.Add(line);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null)
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext context)
    {
        // Durchsichtig gefüllt, damit Klicks überall im Rand ankommen
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var view = TextView;
        if (view is null || !view.VisualLinesValid) return;
        foreach (var line in view.VisualLines)
        {
            if (!_lines.Contains(line.FirstDocumentLine)) continue;
            var y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextMiddle) - view.VerticalOffset;
            context.DrawEllipse(Dot, null, new Point(MarginWidth / 2, y), Radius, Radius);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (LineAt(e.GetPosition(this).Y) is { } line)
            Toggle(line);
        e.Handled = true;
    }

    /// <summary>Die Zeile auf Höhe <paramref name="y"/> im Rand.</summary>
    internal DocumentLine? LineAt(double y)
    {
        var view = TextView;
        if (view is null) return null;
        view.EnsureVisualLines();
        return view.GetVisualLineFromVisualTop(y + view.VerticalOffset)?.FirstDocumentLine;
    }

    static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Markiert im Editor den Schritt, vor dem der Schritt-für-Schritt-Lauf steht (gelb), der gerade läuft (blau) oder an
/// dem er gescheitert ist (rot). Verschiebt sich nicht mit dem Text, der ist während des Laufs gesperrt.
/// </summary>
internal sealed class StepMarker(TextView view) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    public StepMark Mark { get; private set; } = StepMark.None;

    public TextSegment? Segment { get; private set; }

    public void Show(int offset, int length, StepMark mark)
    {
        (Segment, Mark) = mark == StepMark.None ? (null, mark) : (new TextSegment { StartOffset = offset, Length = Math.Max(1, length) }, mark);
        view.InvalidateLayer(Layer);
    }

    public void Clear() => Show(0, 0, StepMark.None);

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Segment is null || textView.Document is null || Segment.EndOffset > textView.Document.TextLength) return;
        var (fill, border) = StepColors.For(Mark);
        var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2, BorderThickness = border.Thickness };
        builder.AddSegment(textView, Segment);
        if (builder.CreateGeometry() is { } geometry)
            drawingContext.DrawGeometry(fill, border, geometry);
    }
}

/// <summary>Die Farben eines Schritts, gleich im Editor und in der Leiste: angehalten gelb, läuft blau, gescheitert rot.</summary>
internal static class StepColors
{
    static readonly (Brush Fill, Pen Border) Paused = Style(Color.FromArgb(0x55, 0xFF, 0xC8, 0x00), Color.FromRgb(0xC8, 0x96, 0x00));
    static readonly (Brush Fill, Pen Border) Running = Style(Color.FromArgb(0x33, 0x00, 0x78, 0xD4), Color.FromRgb(0x00, 0x78, 0xD4));
    static readonly (Brush Fill, Pen Border) Failed = Style(Color.FromArgb(0x40, 0xE8, 0x11, 0x23), Color.FromRgb(0xE8, 0x11, 0x23));

    public static (Brush Fill, Pen Border) For(StepMark mark) => mark switch
    {
        StepMark.Running => Running,
        StepMark.Failed => Failed,
        _ => Paused,
    };

    static (Brush, Pen) Style(Color fill, Color border)
    {
        var brush = new SolidColorBrush(fill);
        brush.Freeze();
        var pen = new Pen(new SolidColorBrush(border), 1);
        pen.Freeze();
        return (brush, pen);
    }
}
