using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.UI;

/// <summary>
/// Eingebauter Editor für Textbausteine: Name, Text, Tag-Palette, Element-Picker und KI, eine Vorschau dessen, was das
/// Skript an der Schreibmarke ergibt, und ein Testlauf, der ihn ins nächste angeklickte Fenster schickt, mit dem ungespeicherten Text.
/// Speichern behält die Zeichenkodierung der Datei (ANSI bleibt ANSI). Ein Fenster pro Datei.
/// Strg+S speichert, F5 testet, Strg+F/Strg+H sucht bzw. ersetzt, Esc schliesst (fragt bei Änderungen).
/// </summary>
internal partial class SnippetEditorWindow : Window
{
    static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(300);
    static readonly TimeSpan TargetTimeout = TimeSpan.FromSeconds(30);
    const ushort VK_LBUTTON = 0x01;

    readonly AppController _app;
    // Der gewählte Ordner, beim Speichern landet die Datei dort. _savedFolder: wo sie gerade liegt.
    string _folder;
    string _savedFolder;
    readonly string _extension;
    readonly DispatcherTimer _previewTimer;
    string? _path;
    TextEncoding _encoding = TextEncoding.Utf8;
    string _savedName;
    string _savedText;
    // Der Hotkey aus dem Dateinamen: Das Namensfeld zeigt den Namen ohne Tag, Speichern hängt es wieder an
    HotkeyGesture? _hotkey;
    DateTime _savedWriteTime;
    bool _loading = true;

    internal SnippetHighlighting Highlighting { get; }
    CancellationTokenSource? _test;
    CursorHint? _hint;

    /// <summary>Haltepunkte für den Schritt-für-Schritt-Lauf, nur solange der Editor offen ist.</summary>
    internal BreakpointMargin Breakpoints { get; } = new();
    internal StepMarker StepMarker { get; }
    StepRun? _stepRun;
    RecordBar? _recordBar;

    SnippetEditorWindow(AppController app, string folder, string? path, string name, string text, TextEncoding encoding)
    {
        InitializeComponent();
        _app = app;
        _folder = _savedFolder = folder;
        _path = path;
        _extension = path is null ? app.NewSnippetExtension : Path.GetExtension(path);
        _encoding = encoding;
        (name, _hotkey) = FileHotkey.Split(name);
        _savedName = name;
        _savedText = text;
        _savedWriteTime = path is null ? default : File.GetLastWriteTimeUtc(path);
        _previewTimer = new DispatcherTimer(PreviewDelay, DispatcherPriority.Background, (_, _) => UpdatePreview(), Dispatcher) { IsEnabled = false };

        SetUpBody();
        WrapToggle.IsChecked = app.Settings.WrapEditor;
        ApplyWrap(app.Settings.WrapEditor);
        Highlighting = SnippetHighlighting.Attach(BodyInput, app.Settings.EvaluateKeyTags, app.Settings.EvaluateScripts,
            path => app.Library.FindIncluded(path) is not null);
        BodyInput.TextArea.LeftMargins.Insert(0, Breakpoints);
        LineNumbersToggle.IsChecked = app.Settings.ShowLineNumbers;
        ApplyLineNumbers(app.Settings.ShowLineNumbers);
        StepMarker = new StepMarker(BodyInput.TextArea.TextView);
        BodyInput.TextArea.TextView.BackgroundRenderers.Add(StepMarker);
        FindBar.Attach(BodyInput);
        FindBar.ReplacedAll += count => ShowStatus(string.Format(Strings.FindReplacedFormat, count), StatusKind.Success);
        NameInput.Text = name;
        BodyInput.Text = text;
        ExtensionText.Text = _extension;
        UpdateFolder();
        _loading = false;
        UpdateTitle();
        UpdatePreview();

        if (app.Settings.EditorBounds is { IsUsable: true } bounds)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Ist schon ein Editor offen, etwas versetzt, sonst läge der neue genau darauf
            SourceInitialized += (_, _) => WindowPlacement.Apply(this, bounds,
                CascadeStep * Application.Current.Windows.OfType<SnippetEditorWindow>().Count(w => w != this && w.IsVisible));
        }

        Loaded += (_, _) =>
        {
            if (path is null) NameInput.Focus();
            else BodyInput.Focus();
        };
        Closed += (_, _) =>
        {
            _previewTimer.Stop();
            _test?.Cancel();
            _stepRun?.Cancel();
            _recordBar?.End(keep: false);
            Highlighting.Dispose();
        };
    }

    /// <summary>Der Umschalter unten rechts im Text: gilt sofort und für alle Editoren, die danach aufgehen.</summary>
    void OnWrapClick(object sender, RoutedEventArgs e)
    {
        var wrap = WrapToggle.IsChecked == true;
        ApplyWrap(wrap);
        _app.Settings.WrapEditor = wrap;
        _app.SaveSettings();
    }

    /// <summary>Der Zeilennummern-Schalter links vom Umbruch: gilt sofort und für alle Editoren, die danach aufgehen.</summary>
    void OnLineNumbersClick(object sender, RoutedEventArgs e)
    {
        var show = LineNumbersToggle.IsChecked == true;
        ApplyLineNumbers(show);
        _app.Settings.ShowLineNumbers = show;
        _app.SaveSettings();
    }

    /// <summary>
    /// Zeilennummern ein oder aus. AvalonEdit setzt sie ganz links hin, mit einer gepunkteten Linie dahinter. Hier
    /// bleiben die Haltepunkte ganz links wie in Visual Studio, die Linie fällt weg, die Nummern sind dezent grau.
    /// </summary>
    void ApplyLineNumbers(bool show)
    {
        BodyInput.ShowLineNumbers = show;
        if (!show) return;
        BodyInput.SetResourceReference(ICSharpCode.AvalonEdit.TextEditor.LineNumbersForegroundProperty, "TextFillColorTertiaryBrush");
        var margins = BodyInput.TextArea.LeftMargins;
        foreach (var dotted in margins.Where(ICSharpCode.AvalonEdit.Editing.DottedLineMargin.IsDottedLineMargin).ToList())
            margins.Remove(dotted);
        if (margins.OfType<ICSharpCode.AvalonEdit.Editing.LineNumberMargin>().FirstOrDefault() is { } numbers)
            numbers.Margin = new Thickness(2, 0, 12, 0);
        margins.Remove(Breakpoints);
        margins.Insert(0, Breakpoints);
    }

    /// <summary>Lange Zeilen umbrechen, oder ungebrochen lassen und waagrecht scrollen.</summary>
    void ApplyWrap(bool wrap)
    {
        BodyInput.WordWrap = wrap;
        BodyInput.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    /// <summary>Verhalten und Farben des Textfelds an die Fluent-Textfelder angleichen.</summary>
    void SetUpBody()
    {
        var options = BodyInput.Options;
        options.EnableHyperlinks = false;
        options.EnableEmailHyperlinks = false;
        options.AllowScrollBelowDocument = false;
        options.EnableRectangularSelection = false;
        options.InheritWordWrapIndentation = false;

        var area = BodyInput.TextArea;
        var accent = TryFindResource("SystemAccentColor") is Color color ? color : SystemColors.HighlightColor;
        var selection = new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B));
        selection.Freeze();
        area.SelectionBrush = selection;
        area.SelectionForeground = null; // Farben der Tags bleiben auch markiert sichtbar
        area.SelectionBorder = null;
        area.SelectionCornerRadius = 0;
        area.Caret.CaretBrush = null;

        // Die Test-Taste sagt, was gleich läuft: alles, die Auswahl oder das Tag an der Schreibmarke.
        // Die Vorschau zeigt, was das Skript dort ergibt.
        area.SelectionChanged += (_, _) => OnCaretMoved();
        area.Caret.PositionChanged += (_, _) => OnCaretMoved();

        // {if}-Blöcke rücken beim Tippen ein, Einrückung ist nur Layout
        area.IndentationStrategy = new ScriptIndentation();
        area.TextEntered += (_, e) => ScriptIndentation.OnTextEntered(area, e.Text);

        // Rechtsklick: Bearbeiten, Suchen und Einrückung auch per Maus. Ausserhalb der Auswahl setzt er die Schreibmarke.
        BodyInput.ContextMenu = new ContextMenu();
        BodyInput.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (BodyInput.GetPositionFromPoint(e.GetPosition(BodyInput)) is not { } position) return;
            var offset = BodyInput.Document.GetOffset(position.Location);
            if (offset < BodyInput.SelectionStart || offset > BodyInput.SelectionStart + BodyInput.SelectionLength
                || BodyInput.SelectionLength == 0)
            {
                BodyInput.Select(offset, 0);
                area.Caret.Offset = offset;
            }
        };
        // Frei geöffnet wie die Menüs im Popup: als ContextMenu des Textfelds erbte es dessen Monospace-Schrift
        BodyInput.ContextMenuOpening += (_, e) =>
        {
            e.Handled = true;
            var menu = EditMenu();
            if (e.CursorLeft >= 0)
            {
                menu.PlacementTarget = BodyInput;
                menu.Placement = PlacementMode.MousePoint;
            }
            else
            {
                // Kontextmenü-Taste oder Umschalt+F10: unter der Schreibmarke
                var view = area.TextView;
                var caret = area.Caret.CalculateCaretRectangle();
                caret.Offset(-view.ScrollOffset.X, -view.ScrollOffset.Y);
                menu.PlacementTarget = view;
                menu.PlacementRectangle = caret;
                menu.Placement = PlacementMode.Bottom;
            }
            menu.IsOpen = true;
        };
    }

    /// <summary>
    /// Die Symbolleiste oben im Rechtsklick-Menü: ohne die Spalte für Symbole und Kürzel eines Eintrags, bündig mit den
    /// Symbolen darunter.
    /// </summary>
    static readonly ControlTemplate IconRowTemplate = CreateIconRowTemplate();

    static ControlTemplate CreateIconRowTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = presenter };
        template.Seal();
        return template;
    }

    /// <summary>
    /// Das Rechtsklick-Menü im Text: oben die einfachen Befehle als Symbole wie im Kontextmenü von Windows 11, darunter
    /// formatiert einfügen, alles markieren, Suchen/Ersetzen und Einrückung ordnen, mit ihren Tastenkürzeln.
    /// </summary>
    internal ContextMenu EditMenu()
    {
        // Die Schrift des Fensters wie im Popup: Sonst erbt das Menü über PlacementTarget die Monospace-Schrift des Texts
        var menu = new ContextMenu { FontFamily = FontFamily, FontSize = FontSize };
        var editable = !BodyInput.IsReadOnly;
        var selected = BodyInput.SelectionLength > 0;
        bool HasClipboard(string format)
        {
            try { return Clipboard.ContainsData(format); }
            catch (System.Runtime.InteropServices.ExternalException) { return false; } // gerade von einem anderen Programm belegt
        }

        // Links Kopieren, Ausschneiden, Einfügen, rechtsbündig Rückgängig und Wiederholen
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(right, Dock.Right);
        var icons = new DockPanel { LastChildFill = false, Children = { right, left } };
        void Icon(StackPanel icons, string glyph, string name, bool enabled, Action action)
        {
            var button = new Button
            {
                Content = glyph, Style = (Style)FindResource("MenuIconButton"), IsEnabled = enabled, ToolTip = name,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Click += (_, _) =>
            {
                menu.IsOpen = false;
                action();
                BodyInput.Focus();
            };
            icons.Children.Add(button);
        }
        Icon(left, MenuIcons.Copy, Strings.EditorMenuCopy, selected, BodyInput.Copy);
        Icon(left, MenuIcons.Cut, Strings.EditorMenuCut, editable && selected, BodyInput.Cut);
        Icon(left, MenuIcons.Paste, Strings.EditorMenuPaste, editable && HasClipboard(DataFormats.UnicodeText), BodyInput.Paste);
        Icon(right, MenuIcons.Undo, Strings.EditorMenuUndo, editable && BodyInput.CanUndo, () => BodyInput.Undo());
        Icon(right, MenuIcons.Redo, Strings.EditorMenuRedo, editable && BodyInput.CanRedo, () => BodyInput.Redo());
        icons.Margin = new Thickness(6, 2, 6, 4);
        menu.Items.Add(new MenuItem { Header = icons, StaysOpenOnClick = true, Focusable = false, Template = IconRowTemplate });
        menu.Items.Add(new Separator());

        menu.Items.Add(Entry(Strings.EditorMenuPasteFormatted, MenuIcons.PasteFormatted, Strings.EditorMenuPasteFormattedGesture,
            editable && HasClipboard(DataFormats.Html), PasteFormatted));
        menu.Items.Add(Entry(Strings.EditorMenuSelectAll, MenuIcons.SelectAll, Strings.EditorMenuSelectAllGesture, true, BodyInput.SelectAll));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Strings.EditorMenuFind, MenuIcons.Search, Strings.EditorMenuFindGesture, true, () => FindBar.Open(replace: false)));
        menu.Items.Add(Entry(Strings.EditorMenuReplace, MenuIcons.Replace, Strings.EditorMenuReplaceGesture, editable, () => FindBar.Open(replace: true)));
        menu.Items.Add(new Separator());
        var indent = Entry(selected ? Strings.EditorMenuIndentSelection : Strings.EditorMenuIndent, MenuIcons.Indent,
            Strings.EditorMenuIndentGesture, editable, TidyIndentation);
        indent.ToolTip = Strings.EditorMenuIndentToolTip;
        menu.Items.Add(indent);
        return menu;

        static MenuItem Entry(string header, string glyph, string gesture, bool enabled, Action action)
        {
            var item = MenuIcons.Item(header, MenuIcons.Glyph(glyph), action);
            item.InputGestureText = gesture;
            item.IsEnabled = enabled;
            return item;
        }
    }

    /// <summary>
    /// Strg+Umschalt+F: rückt {if}-Blöcke in der Auswahl ein, ohne Auswahl im ganzen Skript (<see cref="ScriptIndent"/>).
    /// Ein Schritt für Rückgängig, danach sind die geänderten Zeilen markiert, wenn etwas markiert war.
    /// </summary>
    internal void TidyIndentation()
    {
        if (BodyInput.IsReadOnly) return;
        var document = BodyInput.Document;
        var text = document.Text;
        var selected = BodyInput.SelectionLength > 0;
        var (start, end) = selected ? (BodyInput.SelectionStart, BodyInput.SelectionStart + BodyInput.SelectionLength) : (0, text.Length);
        var changes = ScriptIndent.Changes(text, start, end);
        if (changes.Count == 0)
        {
            var form = KeyTags.Scan(text, new FormBuilder()).IsForm;
            ShowStatus(form ? Strings.EditorIndentTidy : Strings.EditorIndentNoForm, StatusKind.Info);
            return;
        }

        var first = document.GetLineByOffset(Math.Min(start, changes[0].Start));
        var last = document.GetLineByOffset(Math.Max(Math.Max(start, end - (selected ? 1 : 0)), changes[^1].Start));
        document.BeginUpdate();
        try
        {
            foreach (var change in changes.Reverse())
                document.Replace(change.Start, change.Length, change.Indent);
        }
        finally
        {
            document.EndUpdate();
        }
        if (selected)
            BodyInput.Select(first.Offset, last.EndOffset - first.Offset);
        BodyInput.Focus();
    }

    void OnCaretMoved()
    {
        UpdateTestButton();
        if (_loading) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    (int Start, int Length, TestScope Scope) TestRange() =>
        SnippetSyntax.TestRange(BodyInput.Text, BodyInput.SelectionStart, BodyInput.SelectionLength);

    void UpdateTestButton()
    {
        if (_test is not null) return; // zeigt gerade "Abbrechen"
        TestButton.Content = TestRange().Scope switch
        {
            TestScope.Selection => Strings.EditorTestSelection,
            TestScope.Tag => Strings.EditorTestTag,
            TestScope.Script => Strings.EditorTestScript,
            _ => Strings.EditorTest,
        };
    }

    /// <exception cref="IOException">Datei nicht lesbar.</exception>
    /// <exception cref="UnauthorizedAccessException">Kein Zugriff.</exception>
    public static SnippetEditorWindow Open(AppController app, string path)
    {
        var content = TextFile.Read(path);
        return new SnippetEditorWindow(app, Path.GetDirectoryName(path) ?? app.StructureRoot, path,
            Path.GetFileNameWithoutExtension(path), content.Text, content.Encoding);
    }

    public static SnippetEditorWindow New(AppController app, string folder) =>
        new(app, folder, null, "", "", TextEncoding.Utf8);

    /// <summary>Pfad der Datei, null bei einem neuen, noch nicht gespeicherten Baustein.</summary>
    public string? FilePath => _path;

    public string DisplayName => NameInput.Text.Trim().Length > 0 ? NameInput.Text.Trim() : Strings.EditorNewSnippet;

    public bool IsDirty => NameInput.Text.Trim() != _savedName || BodyInput.Text != _savedText || FolderChanged
        || _path is null && BodyInput.Text.Length > 0;

    bool FolderChanged => !SamePath(_folder, _savedFolder);

    static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Der gewählte Ordner, z.B. "in Bausteine › 5-SQL", ohne die Hotkey-Tags der Ordner.</summary>
    void UpdateFolder()
    {
        FolderText.Text = string.Format(Strings.EditorFolderFormat, FolderDisplay(_folder));
        FolderButton.ToolTip = _folder + Environment.NewLine + Strings.EditorFolderToolTip;
    }

    string FolderDisplay(string folder)
    {
        var relative = Path.GetRelativePath(_app.StructureRoot, folder);
        IEnumerable<string> folders = relative == "." ? [] : relative.Split(Path.DirectorySeparatorChar).Select(f => FileHotkey.Split(f).Name);
        return string.Join(" › ", folders.Prepend(Strings.BreadcrumbRoot));
    }

    /// <summary>Anderer Ordner für den Baustein. Verschoben bzw. angelegt wird erst beim Speichern.</summary>
    internal void ChooseFolder(string folder)
    {
        _folder = folder;
        UpdateFolder();
        UpdateTitle();
    }

    void OnFolderClick(object sender, RoutedEventArgs e)
    {
        var menu = FolderMenu();
        menu.PlacementTarget = FolderButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Die Ordner wie im Explorer zum Aufklappen: zuoberst der Wurzelordner, darunter die Ordner der ersten Ebene. Wer
    /// Unterordner hat, klappt auf, zuoberst steht dort "Diesen Ordner wählen". Der gewählte Ordner hat ein Häkchen, der
    /// Weg dorthin ist fett.
    /// </summary>
    internal ContextMenu FolderMenu()
    {
        var menu = new ContextMenu();
        var root = _app.Library.Root;
        menu.Items.Add(Choice(root, Strings.BreadcrumbRoot));
        var children = Subfolders(root);
        if (children.Count > 0)
            menu.Items.Add(new Separator());
        foreach (var child in children)
            menu.Items.Add(Entry(child));
        return menu;

        static List<Snippet> Subfolders(Snippet folder) => folder.Children.Where(c => c.IsFolder).ToList();

        // Ohne Unterordner gleich wählbar, sonst ein Untermenü
        MenuItem Entry(Snippet folder)
        {
            var subfolders = Subfolders(folder);
            if (subfolders.Count == 0)
                return Choice(folder, folder.Name);

            var item = new MenuItem
            {
                Header = folder.Name,
                Icon = MenuIcons.Glyph(MenuIcons.Folder),
                FontWeight = OnCurrentPath(folder) ? FontWeights.SemiBold : FontWeights.Normal,
                Tag = folder.FullPath,
            };
            item.Items.Add(Choice(folder, Strings.EditorFolderChooseThis));
            item.Items.Add(new Separator());
            foreach (var child in subfolders)
                item.Items.Add(Entry(child));
            return item;
        }

        MenuItem Choice(Snippet folder, string header)
        {
            var current = SamePath(folder.FullPath, _folder);
            var item = new MenuItem
            {
                Header = header,
                Icon = MenuIcons.Glyph(current ? MenuIcons.Check : MenuIcons.Folder),
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                Tag = folder.FullPath,
            };
            item.Click += (_, _) => ChooseFolder(folder.FullPath);
            return item;
        }

        // Der gewählte Ordner selbst oder einer darüber
        bool OnCurrentPath(Snippet folder)
        {
            var relative = Path.GetRelativePath(folder.FullPath, _folder);
            return relative == "." || !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        }
    }

    void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) UpdateTitle();
    }

    void OnBodyChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        UpdateTitle();
        // Ein roter Schritt vom letzten Lauf passt nach einer Änderung nicht mehr
        StepMarker.Clear();
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    void UpdateTitle() => Title = (IsDirty ? "● " : "") + DisplayName + " – " + Strings.EditorTitle;

    /// <summary>
    /// Was das Skript an der Schreibmarke ergibt, bzw. die Auswahl, wenn darin ein Skript steht. Sonst keine Vorschau:
    /// Text und Tasten-Tags stehen im Editor schon so da, wie sie laufen.
    /// </summary>
    internal void UpdatePreview()
    {
        _previewTimer.Stop();
        var text = BodyInput.Text;
        var (start, length, scope) = TestRange();
        var part = text.Substring(start, length);
        if (!_app.Settings.EvaluateScripts || scope is not (TestScope.Script or TestScope.Selection) || !SnippetScript.HasBlocks(part))
        {
            PreviewBox.Visibility = Visibility.Collapsed;
            return;
        }

        PreviewBox.Visibility = Visibility.Visible;
        var preview = _app.Preview(text, start, length);
        // Auch ein run() davor: seine Variable ist hier leer. ask() und selection gibt es erst beim Einfügen.
        var upTo = text[..(start + length)];
        string[] notes =
        [
            .. SnippetScript.UsesRun(upTo) ? [Strings.EditorPreviewRunNote] : Array.Empty<string>(),
            .. SnippetScript.UsesMessages(upTo) ? [Strings.EditorPreviewMessageNote] : Array.Empty<string>(),
            .. SnippetScript.UsesAsk(upTo) ? [Strings.EditorPreviewAskNote] : Array.Empty<string>(),
            .. SnippetScript.UsesSelection(upTo) ? [Strings.EditorPreviewSelectionNote] : Array.Empty<string>(),
        ];
        PreviewNote.Text = string.Join(" ", notes);
        PreviewNote.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (preview.Error is not null)
        {
            PreviewLabel.Text = Strings.EditorPreviewError;
            PreviewIcon.Text = "\uEA39";
            PreviewIcon.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
            PreviewText.Text = preview.Error;
            PreviewText.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
            return;
        }
        PreviewLabel.Text = scope == TestScope.Script ? Strings.EditorPreviewScript : Strings.EditorPreviewSelection;
        PreviewIcon.Text = "\uE943";
        PreviewIcon.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        PreviewText.Text = preview.Text.Length == 0 ? Strings.EditorPreviewEmpty : TabStops.Expand(preview.Text);
        PreviewText.SetResourceReference(TextBlock.ForegroundProperty,
            preview.Text.Length == 0 ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush");
    }

    // --- Einfügen: Palette, Picker, KI ---

    /// <summary>
    /// Fügt an der Schreibmarke ein (ersetzt eine Markierung). <paramref name="select"/> wird danach markiert,
    /// z.B. "id" in {focus,#id}, damit man es gleich überschreiben kann.
    /// </summary>
    internal void Insert(string text, string? select = null)
    {
        var start = BodyInput.SelectionStart;
        BodyInput.SelectedText = text;
        var index = select is null ? -1 : text.IndexOf(select, StringComparison.Ordinal);
        if (index >= 0)
            BodyInput.Select(start + index, select!.Length);
        else
            BodyInput.Select(start + text.Length, 0);
        BodyInput.Focus();
    }

    /// <summary>
    /// Ein Tag aus dem Picker auf eine eigene Zeile: Umbruch davor, wenn die Zeile schon etwas enthält, und danach, wenn
    /// noch etwas folgt oder es die letzte Zeile ist. Diese Umbrüche sind nur Layout und werden nicht gesendet
    /// (<see cref="KeyTags.SplitWithSources"/>). Mitten im Text wird der Tag ohne Umbruch eingefügt.
    /// </summary>
    internal void InsertTag(string tag, string? select = null)
    {
        var document = BodyInput.Document;
        var indent = LineIndent();
        var start = BodyInput.SelectionStart;
        var end = start + BodyInput.SelectionLength;
        var startLine = document.GetLineByOffset(start);
        var endLine = document.GetLineByOffset(end);
        var before = !string.IsNullOrWhiteSpace(document.GetText(startLine.Offset, start - startLine.Offset));
        var after = !string.IsNullOrWhiteSpace(document.GetText(end, endLine.EndOffset - end));
        var newLine = ICSharpCode.AvalonEdit.Document.TextUtilities.GetNewLineFromDocument(document, startLine.LineNumber);
        var lines = tag.ReplaceLineEndings(newLine + indent);
        if (before && after)
        {
            Insert(lines, select);
            return;
        }
        Insert((before ? newLine + indent : "") + lines + (after || endLine.NextLine is null ? newLine + indent : ""), select);
    }

    /// <summary>
    /// Die Einrückung der Zeile an der Schreibmarke, für eingefügte Zeilen in einem eingerückten {if}-Block. Steht die
    /// Schreibmarke noch in dieser Einrückung, rückt sie ans Ende davon, damit das Eingefügte dort beginnt.
    /// </summary>
    string LineIndent()
    {
        var document = BodyInput.Document;
        var line = document.GetLineByOffset(BodyInput.SelectionStart);
        var whitespace = ICSharpCode.AvalonEdit.Document.TextUtilities.GetWhitespaceAfter(document, line.Offset);
        if (BodyInput.SelectionLength == 0 && BodyInput.SelectionStart < whitespace.EndOffset)
            BodyInput.Select(whitespace.EndOffset, 0);
        return document.GetText(whitespace);
    }

    /// <summary>
    /// Strg+Umschalt+V: den formatierten Text aus der Zwischenablage (z.B. eine Signatur aus Outlook) als {html}-Block auf
    /// eigenen Zeilen. Ohne formatierten Text sagt die Statuszeile das.
    /// </summary>
    internal void PasteFormatted()
    {
        if (PasteService.TryGetClipboardHtml() is not { } html || HtmlText.CleanFragment(html) is not { Length: > 0 } fragment)
        {
            ShowStatus(Strings.EditorNoFormattedText, StatusKind.Info);
            return;
        }
        var newLine = ICSharpCode.AvalonEdit.Document.TextUtilities.GetNewLineFromDocument(BodyInput.Document,
            BodyInput.Document.GetLineByOffset(BodyInput.CaretOffset).LineNumber);
        InsertTag("{html}" + newLine + fragment.ReplaceLineEndings(newLine) + newLine + "{/html}");
    }

    void OnTagsClick(object sender, RoutedEventArgs e)
    {
        var menu = TagMenu();
        menu.PlacementTarget = TagsButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Das Menü „Tag einfügen ▾“, siehe <see cref="TagMenuBuilder"/>.</summary>
    internal ContextMenu TagMenu() => TagMenuBuilder.Create(this, _app.ShowElementPicker);

    void OnPickerClick(object sender, RoutedEventArgs e) => _app.ShowElementPicker();

    void OnAiClick(object sender, RoutedEventArgs e) => _app.ShowAiPrompt();

    void OnHasteScriptAiClick(object sender, RoutedEventArgs e) => ShowHasteScriptAi();

    /// <summary>Ein Fenster pro Editor, gehört ihm und geht mit ihm zu. Ein weiterer Klick holt es nach vorne.</summary>
    internal HasteScriptAiWindow ShowHasteScriptAi()
    {
        if (_hasteScriptAi is null)
        {
            var window = new HasteScriptAiWindow(_app, this, _hasteScriptAiTask) { Owner = this };
            window.Closed += (_, _) =>
            {
                // Die Aufgabe bleibt, solange der Editor offen ist
                _hasteScriptAiTask = window.TaskInput.Text;
                if (_hasteScriptAi == window)
                    _hasteScriptAi = null;
            };
            _hasteScriptAi = window;
            window.Show();
        }

        if (_hasteScriptAi.WindowState == WindowState.Minimized)
            _hasteScriptAi.WindowState = WindowState.Normal;
        _hasteScriptAi.Activate();
        return _hasteScriptAi;
    }

    HasteScriptAiWindow? _hasteScriptAi;
    string _hasteScriptAiTask = "";

    /// <summary>
    /// Was "HasteScript AI" über den Text weiss: Markierung, rot unterstrichene Tags mit Grund und den Fehler, an dem das
    /// Skript scheitert. Die Vorschau sendet nichts, und run() startet nichts.
    /// </summary>
    internal HasteScriptRequest HasteScriptAiRequest(string task)
    {
        var text = BodyInput.Text;
        var problems = Highlighting.Spans()
            .Where(s => s.Kind == SyntaxKind.Error)
            .Select(s => $"Line {HasteScriptPrompt.LineAt(text, s.Start)}: {text.Substring(s.Start, s.Length)} – {s.Error}")
            .ToList();
        if (_app.Settings.EvaluateScripts && SnippetScript.HasBlocks(text) && _app.Preview(text).Error is { } error)
            problems.Add("Script: " + error);

        var buttons = new Dictionary<string, string>
        {
            ["Pick element"] = Strings.EditorPicker,
            ["Insert into editor"] = Strings.PickerInsert,
            ["Test"] = Strings.EditorTest,
            ["Step by step"] = Strings.EditorStepRun,
            ["Record"] = Strings.EditorRecord,
        };
        return new HasteScriptRequest(task, text, BodyInput.SelectionStart, BodyInput.SelectionLength, NameInput.Text,
            _extension, AppVersion.Display(), CultureInfo.CurrentCulture.Name, problems, buttons);
    }

    void OnHelpClick(object sender, RoutedEventArgs e) => _app.ShowHelp(HelpWindow.EditorAnchor);

    // --- Speichern ---

    void OnSave(object sender, RoutedEventArgs e) => Save();

    /// <returns>False, wenn nicht gespeichert wurde (Name ungültig, Fehler, abgebrochen).</returns>
    internal bool Save()
    {
        // Wer ein Tag von Hand ins Namensfeld schreibt, bekommt diesen Hotkey, sonst bleibt der bisherige
        var (name, typedHotkey) = FileHotkey.Split(NameInput.Text.Trim());
        var hotkey = typedHotkey ?? _hotkey;
        var fileName = FileHotkey.Tagged(name, hotkey) + _extension;
        var currentName = _path is null ? null : Path.GetFileName(_path);
        // In einen anderen Ordner: Dort zählt jeder gleichnamige Eintrag, auch einer, der sich nur in Gross/Klein unterscheidet
        var moving = _path is not null && !SamePath(_folder, Path.GetDirectoryName(_path) ?? "");
        if (_app.HotkeyConflictForName(fileName, isFolder: false, _path, _hotkey) is { } conflict)
        {
            ShowStatus(string.Format(Strings.EditorHotkeyConflictFormat, hotkey, conflict), error: true);
            NameInput.Focus();
            return false;
        }
        // Den Namen prüfen, nicht name + Endung: ".txt" allein wäre sonst ein gültiger Dateiname
        var problem = name.Length == 0 ? NameProblem.Empty : SnippetFiles.Check(_folder, fileName, moving ? null : currentName);
        if (problem is not (NameProblem.None or NameProblem.Unchanged))
        {
            ShowStatus(problem switch
            {
                NameProblem.Empty => Strings.EditorNameEmpty,
                NameProblem.Exists => Strings.NameExists,
                _ => Strings.NameInvalid,
            }, error: true);
            NameInput.Focus();
            return false;
        }

        // Inzwischen in einem anderen Programm geändert? Nicht still überschreiben.
        if (_path is not null && File.Exists(_path) && File.GetLastWriteTimeUtc(_path) != _savedWriteTime)
        {
            var answer = MessageDialog.Show(this, string.Format(Strings.EditorChangedOnDiskFormat, DisplayName),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return false;
        }

        try
        {
            var path = _path;
            if (path is not null && moving)
                path = SnippetFiles.Move(path, _folder, fileName);
            else if (path is not null && currentName != fileName)
                path = SnippetFiles.Rename(path, fileName);
            path ??= Path.Combine(_folder, fileName);

            var used = TextFile.Write(path, BodyInput.Text, _encoding);
            if (used != _encoding)
                Log.Info($"Baustein enthält Zeichen, die {_encoding} nicht kann, gespeichert als {used}: {path}");

            (_path, _encoding, _savedName, _savedText, _hotkey, _savedFolder) = (path, used, name, BodyInput.Text, hotkey, _folder);
            // Ein getipptes Tag steht jetzt im Dateinamen, im Feld wieder nur der Name
            if (typedHotkey is not null)
                NameInput.Text = name;
            _savedWriteTime = File.GetLastWriteTimeUtc(path);
            UpdateTitle();
            ShowStatus(Strings.EditorSaved, StatusKind.Success);
            _app.SnippetSaved(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Baustein konnte nicht gespeichert werden: {_path ?? Path.Combine(_folder, fileName)}", ex);
            ShowStatus(string.Format(Strings.EditorSaveErrorFormat, ex.Message), error: true);
            return false;
        }
    }

    enum StatusKind { Info, Success, Error, Busy }

    void ShowStatus(string text, bool error = false) => ShowStatus(text, error ? StatusKind.Error : StatusKind.Info);

    /// <summary>Die Meldung links neben den Knöpfen: Symbol und getönte Fläche nach Art, wie eine InfoBar.</summary>
    void ShowStatus(string text, StatusKind kind)
    {
        var (icon, color, background) = kind switch
        {
            StatusKind.Success => ("\uE73E", "SystemFillColorSuccessBrush", "SystemFillColorSuccessBackgroundBrush"),
            StatusKind.Error => ("\uEA39", "SystemFillColorCriticalBrush", "SystemFillColorCriticalBackgroundBrush"),
            StatusKind.Busy => ("\uE768", "AccentTextFillColorPrimaryBrush", "SystemFillColorAttentionBackgroundBrush"),
            _ => ("\uE946", "TextFillColorSecondaryBrush", "SubtleFillColorSecondaryBrush"),
        };
        StatusText.Text = text;
        StatusIcon.Text = icon;
        StatusIcon.SetResourceReference(TextBlock.ForegroundProperty, color);
        StatusBox.SetResourceReference(Border.BackgroundProperty, background);
        if (TestBanner.Visibility != Visibility.Visible)
            StatusBox.Visibility = Visibility.Visible;
    }

    // --- Testlauf ---

    /// <summary>
    /// Wartet, bis ein anderes Programm vorne ist (man klickt ins Zielfeld), dann läuft der Baustein dort wie beim
    /// Einfügen, mit dem Text, wie er gerade im Editor steht. Nochmals drücken bricht das Warten ab. Beginnt der Text
    /// mit {window,…} oder {start,…}, läuft er sofort, er holt sein Fenster selbst.
    /// </summary>
    async void OnTest(object sender, RoutedEventArgs e)
    {
        if (_test is not null)
        {
            _test.Cancel();
            return;
        }
        if (_stepRun is not null || _recordBar is not null) return;

        // Was läuft, wird festgehalten, bevor man wegklickt. Ein einzelnes Tag wird markiert, damit man es sieht.
        var (start, length, scope) = TestRange();
        var text = BodyInput.Text.Substring(start, length);
        if (scope is TestScope.Tag or TestScope.Script)
            BodyInput.Select(start, length);

        using var test = _test = new CancellationTokenSource(TargetTimeout);
        TestButton.Content = Strings.EditorTestCancel;
        var startTag = _app.StartWindowTag(text);
        if (startTag is null)
            ShowWaiting(scope == TestScope.All ? null : Shorten(text));
        try
        {
            var target = startTag is null ? await WaitForTargetAsync(test.Token) : Handle;
            HideWaiting();
            if (target == 0)
            {
                ShowStatus(Strings.EditorTestCancelled);
                return;
            }
            ShowStatus(Running(startTag, Strings.EditorTestRunning), StatusKind.Busy);
            var problem = await _app.RunTextAsync(text, DisplayName, alternate: false, target, folder: _folder);
            ShowStatus(problem?.Message ?? Strings.EditorTestDone, problem is null ? StatusKind.Success : StatusKind.Error);
        }
        finally
        {
            HideWaiting();
            _test = null;
            UpdateTestButton();
        }
    }

    /// <summary>
    /// Schritt für Schritt: wie „Testen“ erst ins Zielfeld klicken (ausser der Text beginnt mit {window,…} oder
    /// {start,…}), dann läuft der ganze Baustein dort, angehalten vor jedem Schritt. Die Leiste am Zielfenster und
    /// F10/F5 steuern, der Editor markiert den Schritt. Der Text ist solange gesperrt. Nochmals drücken beendet den Lauf.
    /// </summary>
    async void OnStepRun(object sender, RoutedEventArgs e)
    {
        if (_stepRun is not null)
        {
            _stepRun.Cancel();
            return;
        }
        if (_test is not null)
        {
            _test.Cancel();
            return;
        }

        var text = BodyInput.Text;
        var startTag = _app.StartWindowTag(text);
        var target = Handle;
        if (startTag is null)
        {
            using var test = _test = new CancellationTokenSource(TargetTimeout);
            StepButton.Content = Strings.EditorTestCancel;
            ShowWaiting(null);
            try
            {
                target = await WaitForTargetAsync(test.Token);
            }
            finally
            {
                HideWaiting();
                _test = null;
                StepButton.Content = Strings.EditorStepRun;
                UpdateTestButton();
            }
        }
        if (target == 0)
        {
            ShowStatus(Strings.EditorTestCancelled);
            return;
        }

        var run = _stepRun = new StepRun(_app, text, DisplayName, target, span => BreakpointLine(text, span), _folder);
        run.Changed += (_, view) => ShowStep(text, view);
        StepMarker.Clear();
        LockForStepRun(true);
        ShowStatus(Running(startTag, Strings.EditorStepRunning), StatusKind.Busy);
        try
        {
            await run.RunAsync();
            ShowStatus(StepMarker.Mark == StepMark.Failed ? Strings.EditorStepRunFailed : Strings.EditorStepRunEnded,
                StepMarker.Mark == StepMark.Failed ? StatusKind.Error : StatusKind.Success);
        }
        finally
        {
            _stepRun = null;
            LockForStepRun(false);
            // Rot bleibt stehen, bis man den Text ändert
            if (StepMarker.Mark != StepMark.Failed)
                StepMarker.Clear();
        }
    }

    /// <summary>Der Editor selbst: Zielfenster, wenn der Text sein Fenster mit {window,…} selbst holt.</summary>
    nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Statuszeile während eines Laufs, mit dem Grund, falls er ohne Klick ins Zielfeld begann.</summary>
    static string Running(string? startTag, string running) =>
        startTag is null ? running : string.Format(Strings.EditorStartsWithWindowFormat, startTag) + " " + running;

    /// <summary>Während des Laufs: Text gesperrt, „Testen“ aus, der Knopf beendet den Lauf.</summary>
    internal void LockForStepRun(bool running)
    {
        BodyInput.IsReadOnly = running;
        TestButton.IsEnabled = !running;
        RecordButton.IsEnabled = !running;
        StepButton.Content = running ? Strings.EditorStepRunStop : Strings.EditorStepRun;
    }

    // --- Aufnahme ---

    /// <summary>
    /// Aufnehmen: wie „Testen“ erst ins Zielfenster klicken, dann schaut der Recorder zu, während man das Formular selbst
    /// ausfüllt. Die Leiste am Zielfenster zeigt den Stand, „Fertig“ setzt die Tags an die Schreibmarke. Der Text ist
    /// solange gesperrt. Nochmals drücken (oder Esc im Editor) ist wie „Fertig“.
    /// </summary>
    async void OnRecord(object sender, RoutedEventArgs e)
    {
        if (_recordBar is not null)
        {
            _recordBar.End(keep: true);
            return;
        }
        if (_test is not null)
        {
            _test.Cancel();
            return;
        }
        if (_stepRun is not null) return;

        nint target;
        using (var wait = _test = new CancellationTokenSource(TargetTimeout))
        {
            RecordButtonText.Text = Strings.EditorTestCancel;
            ShowWaiting(null, record: true);
            try
            {
                target = await WaitForTargetAsync(wait.Token);
            }
            finally
            {
                HideWaiting();
                _test = null;
                RecordButtonText.Text = Strings.EditorRecord;
            }
        }
        if (target == 0)
        {
            ShowStatus(Strings.EditorRecordCancelled);
            return;
        }

        var bar = _recordBar = new RecordBar(_app.Settings.RecordMode);
        using var recorder = new ActionRecorder();
        void Update() => bar.Show(Recording.Lines(recorder.Snapshot(), bar.Mode), recorder.Note);
        recorder.Changed += (_, _) => Dispatcher.BeginInvoke(Update);
        recorder.ForegroundChanged += (_, window) => Dispatcher.BeginInvoke(() => bar.Place(window));
        bar.ModeChanged += (_, _) =>
        {
            _app.Settings.RecordMode = bar.Mode;
            _app.SaveSettings();
            Update();
        };

        LockForRecording(true);
        ShowStatus(Strings.EditorRecording, StatusKind.Busy);
        Log.Info($"Aufnahme: {DisplayName}");
        try
        {
            recorder.Start();
            bar.ShowAt(target);
            var keep = await bar.WaitForEndAsync();
            var actions = await recorder.StopAsync();
            var newLine = ICSharpCode.AvalonEdit.Document.TextUtilities.GetNewLineFromDocument(BodyInput.Document, 1);
            var text = Recording.Build(actions, bar.Mode, newLine);
            if (!keep)
                ShowStatus(Strings.EditorRecordDiscarded);
            else if (text.Length == 0)
                ShowStatus(Strings.EditorRecordedNothing);
            else
            {
                LockForRecording(false);
                InsertRecorded(text);
                ShowStatus(string.Format(Strings.EditorRecordedFormat, Recording.Lines(actions, bar.Mode).Count), StatusKind.Success);
            }
        }
        finally
        {
            _recordBar = null;
            bar.Close();
            LockForRecording(false);
        }
        if (IsVisible) Activate(); // nicht, wenn der Editor während der Aufnahme geschlossen wurde
    }

    /// <summary>Während der Aufnahme: Text gesperrt, Testen aus, der Knopf beendet die Aufnahme.</summary>
    internal void LockForRecording(bool recording)
    {
        BodyInput.IsReadOnly = recording;
        TestButton.IsEnabled = StepButton.IsEnabled = !recording;
        RecordButtonText.Text = recording ? Strings.EditorRecordStop : Strings.EditorRecord;
    }

    /// <summary>
    /// Die aufgenommenen Zeilen an die Schreibmarke (ersetzt eine Markierung), auf eigenen Zeilen: Umbruch davor, wenn
    /// die Zeile schon etwas enthält. Der Text endet mit einem Umbruch, was danach steht, rückt auf die nächste Zeile.
    /// </summary>
    internal void InsertRecorded(string text)
    {
        var document = BodyInput.Document;
        var indent = LineIndent(); // in einem eingerückten Zweig: jede aufgenommene Zeile ebenso
        var start = BodyInput.SelectionStart;
        var line = document.GetLineByOffset(start);
        var before = !string.IsNullOrWhiteSpace(document.GetText(line.Offset, start - line.Offset));
        var newLine = ICSharpCode.AvalonEdit.Document.TextUtilities.GetNewLineFromDocument(document, line.LineNumber);
        Insert((before ? newLine + indent : "") + text.ReplaceLineEndings(newLine + indent));
    }

    /// <summary>Den Schritt im Text markieren und hinscrollen. <paramref name="text"/>: der Text, mit dem der Lauf begann.</summary>
    internal void ShowStep(string text, StepView view)
    {
        if (view.Mark == StepMark.None)
        {
            StepMarker.Clear();
            return;
        }
        var length = BodyInput.Document.TextLength;
        var start = Math.Min(KeyTags.UnstrippedOffset(text, view.Source.Start), length);
        var end = Math.Min(KeyTags.UnstrippedOffset(text, view.Source.End), length);
        StepMarker.Show(start, end - start, view.Mark);
        var location = BodyInput.Document.GetLocation(start);
        BodyInput.ScrollTo(location.Line, location.Column);
    }

    /// <summary>Die erste Zeile mit Haltepunkt, die der Schritt an <paramref name="span"/> berührt, sonst null.</summary>
    internal int? BreakpointLine(string text, SourceSpan span)
    {
        var document = BodyInput.Document;
        var start = Math.Min(KeyTags.UnstrippedOffset(text, span.Start), document.TextLength);
        var end = Math.Min(KeyTags.UnstrippedOffset(text, Math.Max(span.Start, span.End - 1)), document.TextLength);
        for (var line = document.GetLineByOffset(start).LineNumber; line <= document.GetLineByOffset(end).LineNumber; line++)
        {
            if (Breakpoints.Has(line))
                return line;
        }
        return null;
    }

    /// <summary>
    /// Wartet auf den Klick ins Zielfeld: Banner in der Akzentfarbe statt der Statuszeile, und ein Kärtchen am
    /// Mauszeiger, weil man beim Hinüberfahren dorthin schaut. <paramref name="part"/>: was läuft, null für alles.
    /// <paramref name="record"/>: für die Aufnahme statt für einen Lauf.
    /// </summary>
    void ShowWaiting(string? part, bool record = false)
    {
        var inlines = TestBannerText.Inlines;
        inlines.Clear();
        inlines.Add(new Run(record ? Strings.EditorRecordClickTarget : Strings.EditorTestClickTarget) { FontWeight = FontWeights.SemiBold, FontSize = 13 });
        inlines.Add(new LineBreak());
        if (record)
            inlines.Add(new Run(Strings.EditorRecordWaiting));
        else if (part is null)
            inlines.Add(new Run(string.Format(Strings.EditorTestRunsAllFormat, DisplayName)));
        else
        {
            inlines.Add(new Run(Strings.EditorTestRunsPart));
            inlines.Add(new Run(" " + part + " ")
            {
                FontFamily = BodyInput.FontFamily,
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
            });
            inlines.Add(new Run(" "));
        }
        inlines.Add(new Run(Strings.EditorTestStops) { FontStyle = FontStyles.Italic });
        StatusBox.Visibility = Visibility.Collapsed;
        TestBanner.Visibility = Visibility.Visible;

        _hint?.Close();
        _hint = record
            ? new CursorHint(Strings.CursorHintRecordTitle, null, Strings.CursorHintRecord)
            : new CursorHint(Strings.CursorHintTitle, part,
                part is null ? string.Format(Strings.CursorHintAllFormat, DisplayName) : Strings.CursorHintEsc);
        _hint.ShowAtCursor();
    }

    void HideWaiting()
    {
        _hint?.Close();
        _hint = null;
        TestBanner.Visibility = Visibility.Collapsed;
        StatusBox.Visibility = StatusText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Für die Statuszeile: eine Zeile, höchstens 60 Zeichen.</summary>
    static string Shorten(string text)
    {
        var line = text.ReplaceLineEndings("⏎");
        return line.Length <= 60 ? line : line[..59] + "…";
    }

    /// <summary>Das Fenster, das als Nächstes vorne ist und nicht zu QuickHasteBot gehört, sobald die Maustaste los ist.</summary>
    static async Task<nint> WaitForTargetAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            var window = GetForegroundWindow();
            GetWindowThreadProcessId(window, out var pid);
            if (window != 0 && pid != Environment.ProcessId)
            {
                // Erst wenn der Klick vorbei ist, sonst landet der Text mitten in einer Markierung per Maus
                while ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 && !cancel.IsCancellationRequested)
                    await Task.Delay(30);
                await Task.Delay(150);
                return cancel.IsCancellationRequested ? 0 : window;
            }
            try
            {
                await Task.Delay(50, cancel);
            }
            catch (OperationCanceledException)
            {
            }
        }
        return 0;
    }

    // --- Tasten, Schliessen ---

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.S when ctrl:
                Save();
                break;
            case Key.V when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                PasteFormatted();
                break;
            case Key.F when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                TidyIndentation();
                break;
            case Key.F when ctrl:
                FindBar.Open(replace: false);
                break;
            case Key.H when ctrl:
                FindBar.Open(replace: true);
                break;
            case Key.F3 when Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift:
                FindBar.FindNext(backwards: Keyboard.Modifiers == ModifierKeys.Shift);
                break;
            case Key.F1:
                _app.ShowHelp(HelpWindow.EditorAnchor);
                break;
            case Key.F5:
                OnTest(this, e);
                break;
            case Key.F9:
                Breakpoints.Toggle(BodyInput.Document.GetLineByOffset(BodyInput.CaretOffset));
                break;
            case Key.Escape when _test is not null:
                _test.Cancel();
                break;
            case Key.Escape when _stepRun is not null:
                _stepRun.Cancel();
                break;
            case Key.Escape when _recordBar is not null:
                _recordBar.End(keep: true);
                break;
            case Key.Escape when FindBar.IsOpen:
                FindBar.Close();
                break;
            case Key.Escape:
                Close();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (IsDirty)
        {
            var answer = MessageDialog.Show(this, string.Format(Strings.EditorSaveChangesFormat, DisplayName),
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Cancel || answer == MessageBoxResult.Yes && !Save())
            {
                e.Cancel = true;
                return;
            }
        }
        RememberBounds();
    }

    /// <summary>Um so viele Pixel steht jeder weitere offene Editor weiter rechts unten.</summary>
    const int CascadeStep = 32;

    /// <summary>Grösse und Stelle für den nächsten Editor, beim Schliessen.</summary>
    void RememberBounds()
    {
        if (WindowPlacement.Read(this) is not { IsUsable: true } bounds || bounds == _app.Settings.EditorBounds) return;
        _app.Settings.EditorBounds = bounds;
        _app.SaveSettings();
    }
}
