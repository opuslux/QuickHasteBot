using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Farben für Tags und Skripte im Editor, nach <see cref="SnippetSyntax"/>. Hell und dunkel je nach Fluent-Theme:
/// erkannt an der Textfarbe des Editors, die per DynamicResource mitwechselt. Fehlerhafte Tags sind rot unterstrichen,
/// darüber zeigt ein Tooltip den Grund.
/// </summary>
internal sealed class SnippetHighlighting : DocumentColorizingTransformer, IDisposable
{
    sealed record Palette(Brush Key, Brush Element, Brush Argument, Brush String, Brush Error, Brush Delimiter,
        Brush Keyword, Brush Helper, Brush Number, Brush Comment)
    {
        public Brush For(SyntaxKind kind) => kind switch
        {
            SyntaxKind.Key => Key,
            SyntaxKind.Element => Element,
            SyntaxKind.Argument => Argument,
            SyntaxKind.String => String,
            SyntaxKind.Error => Error,
            SyntaxKind.ScriptDelimiter => Delimiter,
            SyntaxKind.Keyword => Keyword,
            SyntaxKind.Helper => Helper,
            SyntaxKind.Number => Number,
            _ => Comment,
        };
    }

    // An die Farben von VS Code angelehnt, auf dem jeweiligen Fluent-Hintergrund gut lesbar
    static readonly Palette Light = new(Brush("#0060B8"), Brush("#9A1FB8"), Brush("#1A7F8A"), Brush("#A31515"), Brush("#D13438"),
        Brush("#B35900"), Brush("#0000E0"), Brush("#795E26"), Brush("#098658"), Brush("#5F7F3F"));
    static readonly Palette Dark = new(Brush("#5CB8FF"), Brush("#D49BE0"), Brush("#4EC9B0"), Brush("#CE9178"), Brush("#FF8A80"),
        Brush("#E0B060"), Brush("#6FA8E8"), Brush("#DCDCAA"), Brush("#B5CEA8"), Brush("#7FA866"));

    /// <summary>Rot gepunktet unter fehlerhaften Tags.</summary>
    internal static readonly TextDecorationCollection ErrorUnderline = CreateUnderline();

    /// <summary>Die Farbe für <paramref name="kind"/>, auch für die Textvorschau im Popup (<see cref="SyntaxColors"/>).</summary>
    internal static Brush BrushFor(SyntaxKind kind, bool dark) => (dark ? Dark : Light).For(kind);

    /// <summary>Heller Text = dunkles Theme.</summary>
    internal static bool IsDark(Brush? foreground) =>
        foreground is SolidColorBrush { Color: var c } && (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 128;

    readonly TextEditor _editor;
    readonly bool _keyTags;
    readonly bool _scripts;
    readonly Func<string, bool>? _includeExists;
    readonly DependencyPropertyDescriptor _foreground = DependencyPropertyDescriptor.FromProperty(Control.ForegroundProperty, typeof(TextEditor));
    readonly ToolTip _toolTip = new();
    IReadOnlyList<SyntaxSpan> _spans = [];
    ITextSourceVersion? _version;
    Palette _palette = Light;

    SnippetHighlighting(TextEditor editor, bool keyTags, bool scripts, Func<string, bool>? includeExists)
    {
        _editor = editor;
        _keyTags = keyTags;
        _scripts = scripts;
        _includeExists = includeExists;
    }

    /// <summary>Hängt die Farben an <paramref name="editor"/>. Beim Schliessen Dispose aufrufen.</summary>
    /// <param name="includeExists">Ob es den Baustein aus {include,…} gibt, sonst ist das Tag rot.</param>
    public static SnippetHighlighting Attach(TextEditor editor, bool keyTags, bool scripts, Func<string, bool>? includeExists = null)
    {
        var highlighting = new SnippetHighlighting(editor, keyTags, scripts, includeExists);
        highlighting.UpdatePalette();
        editor.TextArea.TextView.LineTransformers.Add(highlighting);
        // Eine Änderung kann andere Zeilen umfärben ("{{" ohne Ende, dann "}}" drei Zeilen weiter)
        editor.TextChanged += highlighting.OnTextChanged;
        highlighting._foreground.AddValueChanged(editor, highlighting.OnThemeChanged);
        editor.TextArea.TextView.MouseHover += highlighting.OnMouseHover;
        editor.TextArea.TextView.MouseHoverStopped += highlighting.OnMouseHoverStopped;
        return highlighting;
    }

    public void Dispose()
    {
        // Der Descriptor hält den Editor sonst fest
        _foreground.RemoveValueChanged(_editor, OnThemeChanged);
        _toolTip.IsOpen = false;
    }

    void OnTextChanged(object? sender, EventArgs e) => _editor.TextArea.TextView.Redraw();

    void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdatePalette();
        _editor.TextArea.TextView.Redraw();
    }

    void UpdatePalette() => _palette = IsDark(_editor.Foreground) ? Dark : Light;

    IReadOnlyList<SyntaxSpan> Spans()
    {
        var document = _editor.Document;
        if (_version is null || !_version.Equals(document.Version))
        {
            _spans = SnippetSyntax.Tokenize(document.Text, _keyTags, _scripts, _includeExists);
            _version = document.Version;
        }
        return _spans;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var spans = Spans();
        foreach (var span in spans)
        {
            if (span.End <= line.Offset) continue;
            if (span.Start >= line.EndOffset) break;
            var start = Math.Max(span.Start, line.Offset);
            var end = Math.Min(span.End, line.EndOffset);
            if (end <= start) continue;

            var brush = _palette.For(span.Kind);
            var bold = span.Kind == SyntaxKind.ScriptDelimiter;
            var error = span.Kind == SyntaxKind.Error;
            ChangeLinePart(start, end, element =>
            {
                element.TextRunProperties.SetForegroundBrush(brush);
                if (bold)
                {
                    var face = element.TextRunProperties.Typeface;
                    element.TextRunProperties.SetTypeface(new Typeface(face.FontFamily, face.Style, FontWeights.Bold, face.Stretch));
                }
                if (error)
                    element.TextRunProperties.SetTextDecorations(ErrorUnderline);
            });
        }
    }

    /// <summary>
    /// Über einem Tag oder Skript-Helfer erklärt ein Tooltip, was er tut (dieselben Texte wie im Menü "Tag einfügen"),
    /// über einem fehlerhaften Tag steht zuerst, was nicht stimmt.
    /// </summary>
    void OnMouseHover(object sender, MouseEventArgs e)
    {
        var view = _editor.TextArea.TextView;
        if (OffsetAt(e.GetPosition(view)) is not { } offset) return;
        var error = ErrorAt(offset);
        var topic = TagHelp.At(_editor.Document.Text, offset, _keyTags, _scripts);
        if (error is null && topic is null) return;
        _toolTip.PlacementTarget = view;
        _toolTip.Content = HelpContent(error, topic);
        _toolTip.IsOpen = true;
        e.Handled = true;
    }

    int? OffsetAt(Point point)
    {
        var view = _editor.TextArea.TextView;
        view.EnsureVisualLines();
        return view.GetPositionFloor(point + view.ScrollOffset) is { } position ? _editor.Document.GetOffset(position.Location) : null;
    }

    /// <summary>Warum das Tag an <paramref name="point"/> (relativ zur TextView) nicht gilt, null wenn dort keins ist.</summary>
    internal string? ErrorAt(Point point) => OffsetAt(point) is { } offset ? ErrorAt(offset) : null;

    string? ErrorAt(int offset) => Spans().FirstOrDefault(s => s.Kind == SyntaxKind.Error && offset >= s.Start && offset < s.End).Error;

    /// <summary>Der Fehler (falls einer), die Kurzbeschreibung fett und der Hilfetext, umbrochen.</summary>
    internal static StackPanel HelpContent(string? error, HelpTopic? topic)
    {
        var panel = new StackPanel { MaxWidth = 420 };
        if (error is not null)
        {
            var problem = new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap };
            problem.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
            panel.Children.Add(problem);
        }
        if (topic is not null)
        {
            if (error is not null)
                panel.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 6) });
            panel.Children.Add(new TextBlock { Text = HelpTitle(topic), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = HelpText(topic), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        }
        return panel;
    }

    internal static string HelpTitle(HelpTopic topic)
    {
        var title = Local(topic.Key, topic.KeyName);
        return title.Length > 0 ? char.ToUpper(title[0], Strings.Culture ?? System.Globalization.CultureInfo.CurrentUICulture) + title[1..] : title;
    }

    internal static string HelpText(HelpTopic topic)
    {
        var text = Local(topic.Key + "Help", topic.KeyName);
        return topic.Repeated && topic.Key != TagHelp.Repeat ? text + "\n" + Local(TagHelp.Repeat + "Help", null) : text;
    }

    static string Local(string key, string? keyName)
    {
        var text = Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;
        return keyName is null ? text : string.Format(text, keyName);
    }

    void OnMouseHoverStopped(object sender, MouseEventArgs e) => _toolTip.IsOpen = false;

    static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    static TextDecorationCollection CreateUnderline()
    {
        var pen = new Pen(Brush("#E81123"), 1) { DashStyle = DashStyles.Dot };
        pen.Freeze();
        var decorations = new TextDecorationCollection { new TextDecoration(TextDecorationLocation.Underline, pen, 1, TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended) };
        decorations.Freeze();
        return decorations;
    }
}
