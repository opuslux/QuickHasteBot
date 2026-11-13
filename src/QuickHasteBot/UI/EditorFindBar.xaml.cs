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
/// Suchen (Strg+F) und Ersetzen (Strg+H) im Editor, schlank: ein Streifen oben im Textrahmen, alle Treffer
/// eingefärbt, der aktuelle ausgewählt. Enter/F3 weiter, Shift zurück, Aa für Gross-/Kleinschreibung. „Alle ersetzen“
/// ist ein einziger Schritt für Strg+Z. Esc schliesst die Leiste (das fängt der Editor ab).
/// </summary>
internal partial class EditorFindBar : UserControl
{
    TextEditor? _editor;
    MatchHighlighter? _highlighter;
    IReadOnlyList<int> _matches = [];

    public EditorFindBar() => InitializeComponent();

    /// <summary>Wie viele ersetzt wurden, nach „Alle ersetzen“. Der Editor meldet es unten.</summary>
    public event Action<int>? ReplacedAll;

    public bool IsOpen => Visibility == Visibility.Visible;

    public bool ShowsReplace => ReplaceRow.Visibility == Visibility.Visible;

    public string Query
    {
        get => FindInput.Text;
        set => FindInput.Text = value;
    }

    public string Replacement
    {
        get => ReplaceInput.Text;
        set => ReplaceInput.Text = value;
    }

    public bool MatchCase
    {
        get => MatchCaseToggle.IsChecked == true;
        set
        {
            MatchCaseToggle.IsChecked = value;
            Refresh(select: true);
        }
    }

    /// <summary>Die Treffer gerade, für die Färbung und die Tests.</summary>
    public IReadOnlyList<int> Matches => _matches;

    public string CountDisplay => CountText.Text;

    public void Attach(TextEditor editor)
    {
        _editor = editor;
        _highlighter = new MatchHighlighter(this);
        editor.TextArea.TextView.BackgroundRenderers.Add(_highlighter);
        editor.TextChanged += (_, _) =>
        {
            if (IsOpen) Refresh(select: false);
        };
        editor.TextArea.SelectionChanged += (_, _) =>
        {
            if (IsOpen) UpdateCount();
        };
        // Während eines Schrittlaufs oder einer Aufnahme ist der Text gesperrt, dann gibt es nichts zu ersetzen
        DependencyPropertyDescriptor.FromProperty(TextEditor.IsReadOnlyProperty, typeof(TextEditor))
            .AddValueChanged(editor, (_, _) => UpdateReplaceButtons());
        UpdatePlaceholders();
    }

    TextEditor Editor => _editor ?? throw new InvalidOperationException("Not attached");

    /// <summary>
    /// Öffnet die Leiste, mit Ersetzen bei Strg+H. Eine einzeilige Auswahl wird zum Suchtext, sonst bleibt der letzte.
    /// Strg+H mit Suchtext setzt den Fokus gleich ins Ersetzen-Feld.
    /// </summary>
    public void Open(bool replace)
    {
        // Zuerst sichtbar, damit der neue Suchtext gleich Treffer findet. Die Auswahl selbst ist dann der erste.
        Visibility = Visibility.Visible;
        var selected = Editor.SelectedText;
        if (selected.Length > 0 && selected.IndexOfAny(['\r', '\n']) < 0)
            FindInput.Text = selected;
        ShowReplace(replace);
        Refresh(select: false);

        var input = replace && FindInput.Text.Length > 0 ? ReplaceInput : FindInput;
        input.Focus();
        input.SelectAll();
    }

    public void Close()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        _matches = [];
        Redraw();
        Editor.TextArea.Focus();
    }

    void ShowReplace(bool show)
    {
        ReplaceRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ReplaceToggle.IsChecked = show;
        ReplaceToggle.Content = show ? "" : ""; // ˅ offen, › zu
        UpdateReplaceButtons();
    }

    /// <summary>
    /// Zum nächsten bzw. vorherigen Treffer ab der Auswahl, am Ende geht es vorne weiter. Auch bei geschlossener Leiste
    /// (F3), dann mit dem letzten Suchtext. Ohne Suchtext öffnet F3 die Leiste.
    /// </summary>
    public void FindNext(bool backwards)
    {
        if (FindInput.Text.Length == 0)
        {
            Open(replace: false);
            return;
        }
        var matches = IsOpen ? _matches : FindInText.FindAll(Editor.Text, FindInput.Text, MatchCase);
        var offset = backwards ? Editor.SelectionStart : Editor.SelectionStart + Editor.SelectionLength;
        var index = FindInText.Next(matches, offset, backwards);
        if (index >= 0) Select(matches[index]);
        UpdateCount();
    }

    /// <summary>
    /// Ersetzt den ausgewählten Treffer und wählt den nächsten. Ist gerade kein Treffer ausgewählt, wird zuerst nur der
    /// nächste ausgewählt, damit man sieht, was ersetzt wird.
    /// </summary>
    public void ReplaceCurrent()
    {
        if (Editor.IsReadOnly || FindInput.Text.Length == 0) return;
        if (CurrentIndex() < 0)
        {
            FindNext(backwards: false);
            return;
        }
        var start = Editor.SelectionStart;
        Editor.Document.Replace(start, Editor.SelectionLength, ReplaceInput.Text);
        Editor.Select(start + ReplaceInput.Text.Length, 0);
        FindNext(backwards: false);
    }

    /// <summary>Ersetzt alle Treffer in einem Schritt (ein Strg+Z macht alles rückgängig), von hinten nach vorn.</summary>
    public int ReplaceAll()
    {
        if (Editor.IsReadOnly || FindInput.Text.Length == 0) return 0;
        var matches = FindInText.FindAll(Editor.Text, FindInput.Text, MatchCase);
        if (matches.Count == 0) return 0;
        var document = Editor.Document;
        using (document.RunUpdate())
        {
            for (var i = matches.Count - 1; i >= 0; i--)
                document.Replace(matches[i], FindInput.Text.Length, ReplaceInput.Text);
        }
        ReplacedAll?.Invoke(matches.Count);
        return matches.Count;
    }

    void Refresh(bool select)
    {
        if (_editor is null) return;
        _matches = IsOpen ? FindInText.FindAll(_editor.Text, FindInput.Text, MatchCase) : [];
        if (select && _matches.Count > 0)
            Select(_matches[FindInText.Next(_matches, _editor.SelectionStart, backwards: false)]);
        Redraw();
        UpdateCount();
    }

    void Select(int start)
    {
        Editor.Select(start, FindInput.Text.Length);
        var location = Editor.Document.GetLocation(start);
        Editor.ScrollTo(location.Line, location.Column);
    }

    /// <summary>Welcher Treffer gerade ausgewählt ist, -1 wenn die Auswahl keiner ist.</summary>
    int CurrentIndex()
    {
        if (Editor.SelectionLength != FindInput.Text.Length) return -1;
        var matches = IsOpen ? _matches : FindInText.FindAll(Editor.Text, FindInput.Text, MatchCase);
        for (var i = 0; i < matches.Count; i++)
            if (matches[i] == Editor.SelectionStart) return i;
        return -1;
    }

    void UpdateCount()
    {
        if (FindInput.Text.Length == 0)
            CountText.Text = "";
        else if (_matches.Count == 0)
            CountText.Text = Strings.FindNoResults;
        else
        {
            var current = CurrentIndex();
            CountText.Text = string.Format(Strings.FindCountFormat, current < 0 ? "?" : (current + 1).ToString(), _matches.Count);
        }
        CountText.SetResourceReference(TextBlock.ForegroundProperty,
            FindInput.Text.Length > 0 && _matches.Count == 0 ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
    }

    void UpdateReplaceButtons() =>
        ReplaceButton.IsEnabled = ReplaceAllButton.IsEnabled = _editor is { IsReadOnly: false };

    void UpdatePlaceholders()
    {
        FindPlaceholder.Visibility = FindInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReplacePlaceholder.Visibility = ReplaceInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Redraw() => _editor?.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

    // --- Bedienung ---

    void OnFindTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholders();
        Refresh(select: true);
    }

    void OnReplaceTextChanged(object sender, TextChangedEventArgs e) => UpdatePlaceholders();

    void OnFindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        FindNext(backwards: Keyboard.Modifiers == ModifierKeys.Shift);
        e.Handled = true;
    }

    void OnReplaceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ReplaceCurrent();
        e.Handled = true;
    }

    void OnMatchCaseClick(object sender, RoutedEventArgs e) => Refresh(select: true);

    void OnPreviousClick(object sender, RoutedEventArgs e) => FindNext(backwards: true);

    void OnNextClick(object sender, RoutedEventArgs e) => FindNext(backwards: false);

    void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    void OnReplaceToggleClick(object sender, RoutedEventArgs e)
    {
        ShowReplace(ReplaceToggle.IsChecked == true);
        (ShowsReplace ? ReplaceInput : FindInput).Focus();
    }

    void OnReplaceClick(object sender, RoutedEventArgs e) => ReplaceCurrent();

    void OnReplaceAllClick(object sender, RoutedEventArgs e) => ReplaceAll();

    /// <summary>Färbt alle Treffer im sichtbaren Teil des Texts, unter der Auswahl, solange die Leiste offen ist.</summary>
    sealed class MatchHighlighter(EditorFindBar bar) : IBackgroundRenderer
    {
        static readonly Brush Fill = Frozen(Color.FromArgb(0x60, 0xF5, 0xB7, 0x1E));

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var matches = bar._matches;
            var length = bar.FindInput.Text.Length;
            if (matches.Count == 0 || length == 0 || textView.Document is not { } document || !textView.VisualLinesValid
                || textView.VisualLines.Count == 0)
                return;
            var from = textView.VisualLines[0].FirstDocumentLine.Offset;
            var to = textView.VisualLines[^1].LastDocumentLine.EndOffset;
            var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2 };
            foreach (var start in matches)
            {
                if (start + length < from) continue;
                if (start > to || start + length > document.TextLength) break;
                builder.AddSegment(textView, new TextSegment { StartOffset = start, Length = length });
            }
            if (builder.CreateGeometry() is { } geometry)
                drawingContext.DrawGeometry(Fill, null, geometry);
        }

        static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
