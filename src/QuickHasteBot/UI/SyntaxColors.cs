using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Text mit denselben Farben wie im Editor (<see cref="SnippetHighlighting"/>) in einem TextBlock, für die Textvorschau
/// im Popup. Statt TextBlock.Text: <c>ui:SyntaxColors.Text</c> und <c>ui:SyntaxColors.Spans</c> (aus
/// <see cref="SnippetSyntax.Tokenize"/>). Hell oder dunkel nach der Textfarbe des TextBlocks, wie im Editor.
/// </summary>
internal static class SyntaxColors
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(SyntaxColors), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty SpansProperty = DependencyProperty.RegisterAttached(
        "Spans", typeof(IReadOnlyList<SyntaxSpan>), typeof(SyntaxColors), new PropertyMetadata(null, OnChanged));

    public static string GetText(TextBlock element) => (string)element.GetValue(TextProperty);
    public static void SetText(TextBlock element, string value) => element.SetValue(TextProperty, value);
    public static IReadOnlyList<SyntaxSpan>? GetSpans(TextBlock element) => (IReadOnlyList<SyntaxSpan>?)element.GetValue(SpansProperty);
    public static void SetSpans(TextBlock element, IReadOnlyList<SyntaxSpan>? value) => element.SetValue(SpansProperty, value);

    static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block) return;
        // Das Theme wechselt, während das Popup versteckt ist: beim nächsten Zeigen neu einfärben. Nur einmal anhängen.
        block.IsVisibleChanged -= OnVisibleChanged;
        block.IsVisibleChanged += OnVisibleChanged;
        Render(block);
    }

    static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            Render((TextBlock)sender);
    }

    static void Render(TextBlock block)
    {
        var text = GetText(block) ?? "";
        var spans = GetSpans(block);
        if (spans is not { Count: > 0 })
        {
            block.Text = text;
            return;
        }

        var dark = SnippetHighlighting.IsDark(block.Foreground);
        block.Inlines.Clear();
        var position = 0;
        foreach (var span in spans)
        {
            // Text und Spans kommen nacheinander an, kurz passen sie nicht zusammen
            if (span.Start < position || span.End > text.Length) continue;
            if (span.Start > position)
                block.Inlines.Add(new Run(text[position..span.Start]));
            var run = new Run(text[span.Start..span.End]) { Foreground = SnippetHighlighting.BrushFor(span.Kind, dark) };
            if (span.Kind == SyntaxKind.ScriptDelimiter)
                run.FontWeight = FontWeights.Bold;
            if (span.Kind == SyntaxKind.Error)
                run.TextDecorations = SnippetHighlighting.ErrorUnderline;
            block.Inlines.Add(run);
            position = span.End;
        }
        if (position < text.Length)
            block.Inlines.Add(new Run(text[position..]));
    }
}
