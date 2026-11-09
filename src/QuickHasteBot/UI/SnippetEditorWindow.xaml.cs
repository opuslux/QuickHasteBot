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
/// Eingebauter Editor für Textbausteine: Name, Text, Tag-Palette, Element-Picker und KI, eine Vorschau dessen, was der
/// Baustein ergibt, und ein Testlauf, der ihn ins nächste angeklickte Fenster schickt, mit dem ungespeicherten Text.
/// Speichern behält die Zeichenkodierung der Datei (ANSI bleibt ANSI). Ein Fenster pro Datei.
/// Strg+S speichert, F5 testet, Esc schliesst (fragt bei Änderungen).
/// </summary>
internal partial class SnippetEditorWindow : Window
{
    static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(400);
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
        Highlighting = SnippetHighlighting.Attach(BodyInput, app.Settings.EvaluateKeyTags, app.Settings.EvaluateScripts);
        BodyInput.TextArea.LeftMargins.Insert(0, Breakpoints);
        StepMarker = new StepMarker(BodyInput.TextArea.TextView);
        BodyInput.TextArea.TextView.BackgroundRenderers.Add(StepMarker);
        NameInput.Text = name;
        BodyInput.Text = text;
        ExtensionText.Text = _extension;
        UpdateFolder();
        _loading = false;
        UpdateTitle();
        UpdatePreview();

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

        // Die Test-Taste sagt, was gleich läuft: alles, die Auswahl oder das Tag an der Schreibmarke
        area.SelectionChanged += (_, _) => UpdateTestButton();
        area.Caret.PositionChanged += (_, _) => UpdateTestButton();
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

    internal void UpdatePreview()
    {
        _previewTimer.Stop();
        var preview = _app.Preview(BodyInput.Text);
        if (preview.Error is not null)
        {
            PreviewLabel.Text = Strings.EditorPreviewError;
            PreviewText.Text = preview.Error;
            PreviewText.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
            return;
        }
        PreviewLabel.Text = preview.IsForm ? Strings.EditorPreviewForm : Strings.EditorPreviewPaste;
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
        var start = BodyInput.SelectionStart;
        var end = start + BodyInput.SelectionLength;
        var startLine = document.GetLineByOffset(start);
        var endLine = document.GetLineByOffset(end);
        var before = !string.IsNullOrWhiteSpace(document.GetText(startLine.Offset, start - startLine.Offset));
        var after = !string.IsNullOrWhiteSpace(document.GetText(end, endLine.EndOffset - end));
        if (before && after)
        {
            Insert(tag, select);
            return;
        }
        var newLine = ICSharpCode.AvalonEdit.Document.TextUtilities.GetNewLineFromDocument(document, startLine.LineNumber);
        Insert((before ? newLine : "") + tag + (after || endLine.NextLine is null ? newLine : ""), select);
    }

    void OnTagsClick(object sender, RoutedEventArgs e)
    {
        var menu = TagMenu();
        menu.PlacementTarget = TagsButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Ein Eintrag im Tag-Menü. <see cref="Select"/>: was nach dem Einfügen markiert wird, zum Überschreiben.
    /// <see cref="OwnLine"/>: auf eine eigene Zeile wie die Tags aus dem Picker (Kommentare).
    /// </summary>
    /// <param name="Description">
    /// Name des Texts in <see cref="Strings"/>, z.B. nameof(Strings.TagNextField): angezeigt in der Sprache der Oberfläche,
    /// der Filter findet ihn auch auf Englisch.
    /// </param>
    /// <param name="Description">Name des Texts mit der Kurzbeschreibung rechts im Menü. Der Hilfetext im Tooltip heisst
    /// gleich mit "Help" dahinter (<see cref="Help"/>).</param>
    internal sealed record TagEntry(string Tag, string Description, string? Select = null, bool OwnLine = false)
    {
        public string Help => Description + "Help";
    }

    /// <summary>
    /// Die Einträge von "Tag einfügen ▾", nach Gruppen (Namen der Texte), null ist ein Trenner. Ein Test prüft, dass
    /// jeder Eintrag Kurzbeschreibung und Hilfetext in allen Sprachen hat.
    /// </summary>
    internal static (string Header, TagEntry?[] Entries)[] TagGroups() =>
    [
        (nameof(Strings.EditorTagsKeys),
        [
            new("{tab}", nameof(Strings.TagNextField)), new("{shift+tab}", nameof(Strings.TagPreviousField)),
            new("{enter}", nameof(Strings.TagSubmit)), new("{space}", nameof(Strings.TagCheckbox)), new("{esc}", nameof(Strings.TagCancel)), null,
            new("{up}", nameof(Strings.TagUp)), new("{down}", nameof(Strings.TagDown)), new("{down,3}", nameof(Strings.TagRepeat)),
            new("{left}", nameof(Strings.TagLeft)), new("{right}", nameof(Strings.TagRight)), new("{home}", nameof(Strings.TagHome)),
            new("{end}", nameof(Strings.TagEnd)), null,
            new("{bs}", nameof(Strings.TagBackspace)), new("{del}", nameof(Strings.TagDelete)), new("{ctrl+a}", nameof(Strings.TagSelectAll)),
            new("{alt+down}", nameof(Strings.TagOpenDropdown)), new("{ctrl+enter}", nameof(Strings.TagSendChat)), new("{f5}", nameof(Strings.TagReload)),
        ]),
        (nameof(Strings.EditorTagsFlow),
        [
            new("{# " + Strings.TagCommentText + "}", nameof(Strings.TagComment), Strings.TagCommentText, OwnLine: true), null,
            new("{wait,500}", nameof(Strings.TagWait), "500"), new("{delay,50}", nameof(Strings.TagDelay), "50"),
            new("{clear}", nameof(Strings.TagClear)), null,
            new("{guard,off}", nameof(Strings.TagGuardOff)), new("{guard,on}", nameof(Strings.TagGuardOn)),
        ]),
        (nameof(Strings.EditorTagsElements),
        [
            new("{focus,#id}", nameof(Strings.TagFocus), "id"), new("{click,name=Label}", nameof(Strings.TagClick), "Label"),
            new("{set,#id,\"\"}", nameof(Strings.TagSet), "id"), null,
            new("{window,program}", nameof(Strings.TagWindow), "program"), new("{start,program}", nameof(Strings.TagStart), "program"),
            new("{start,https://example.com}", nameof(Strings.TagStartUrl), "https://example.com"),
        ]),
        (nameof(Strings.EditorTagsScripts),
        [
            new("{{ date() }}", nameof(Strings.TagDate)), new("{{ date('dd.MM.yyyy', 14) }}", nameof(Strings.TagDateIn), "14"),
            new("{{ time() }}", nameof(Strings.TagTime)), new("{{ isoweek() }}", nameof(Strings.TagWeek)),
            new("{{ clipboard }}", nameof(Strings.TagClipboard)), new("{{ user }}", nameof(Strings.TagUser)),
            new("{{ guid() }}", nameof(Strings.TagGuid)), null,
            new("{{ const r = run('d:/scripts/file.bat') }}", nameof(Strings.TagRun), "d:/scripts/file.bat", OwnLine: true),
            new("{{ const r = run('d:/scripts/file.bat', { args: '', dir: 'd:/data' }) }}", nameof(Strings.TagRunOptions), "d:/scripts/file.bat", OwnLine: true),
        ]),
    ];

    /// <summary>Ein Text in der Sprache der Oberfläche.</summary>
    static string Local(string key) => Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;

    /// <summary>
    /// Was der Filter im Tag-Menü durchsucht: der Tag und die Texte in der Sprache der Oberfläche und auf Englisch, damit
    /// z.B. «comment» auch im deutschen Menü den Kommentar findet.
    /// </summary>
    static string SearchText(string? tag, params string?[] keys) =>
        string.Join(' ', new[] { tag }.Concat(keys.OfType<string>().SelectMany(key => new[]
        {
            Local(key), Strings.ResourceManager.GetString(key, System.Globalization.CultureInfo.InvariantCulture),
        })).OfType<string>().Distinct());

    /// <summary>
    /// "Tag einfügen ▾": oben eine Filterzeile, darunter die Gruppen. Sobald gefiltert wird, stehen statt der Gruppen alle
    /// passenden Tags in einer Liste (gesucht in Tag, Beschreibung und Gruppe, wie die Suche im Popup, ohne Treffer in den
    /// Hilfetexten). Enter fügt den ersten Treffer ein, ↓ geht in die Liste. Jeder Tag erklärt sich im Tooltip.
    /// </summary>
    internal ContextMenu TagMenu()
    {
        // Gruppen und Beschreibungen als Namen der Texte, siehe SearchText
        var groups = TagGroups().Select(g => (g.Header, Icon: g.Header switch
        {
            nameof(Strings.EditorTagsKeys) => MenuIcons.Glyph(MenuIcons.Keys),
            nameof(Strings.EditorTagsFlow) => MenuIcons.Glyph(MenuIcons.Flow),
            nameof(Strings.EditorTagsElements) => MenuIcons.Crosshair(),
            _ => MenuIcons.Glyph(MenuIcons.Script),
        }, g.Entries)).ToArray();

        var menu = new ContextMenu();
        var filter = new TextBox { MinWidth = 220, VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(filter, Strings.EditorTagsFilter);
        var placeholder = new TextBlock
        {
            Text = Strings.EditorTagsFilter, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0),
            Foreground = (Brush)FindResource("TextFillColorTertiaryBrush"),
        };
        // Klicks ins Feld sollen das Menü nicht schliessen, und die Zeile selbst ist kein Ziel für die Pfeiltasten
        menu.Items.Add(new MenuItem
        {
            Header = new Grid { Children = { filter, placeholder } }, Icon = MenuIcons.Glyph(MenuIcons.Search),
            StaysOpenOnClick = true, Focusable = false,
        });
        menu.Items.Add(new Separator());

        var groupItems = new List<MenuItem>();
        foreach (var (header, icon, entries) in groups)
        {
            var group = new MenuItem { Header = Local(header), Icon = icon };
            foreach (var entry in entries)
                group.Items.Add(entry is null ? new Separator() : Tag(entry));
            if (header == nameof(Strings.EditorTagsElements))
            {
                group.Items.Add(new Separator());
                group.Items.Add(MenuIcons.Item(Strings.EditorPickerMenu, MenuIcons.Crosshair(), _app.ShowElementPicker));
            }
            groupItems.Add(group);
            menu.Items.Add(group);
        }

        // Die flache Trefferliste, versteckt bis gefiltert wird. Die Hilfetexte zählen nur, wenn Tag, Beschreibung und
        // Gruppe nichts finden: Sie erwähnen viele Wörter («Feld»), und die Liste soll kurz bleiben.
        var results = new List<(MenuItem Item, string Text, string Help)>();
        foreach (var (header, _, entries) in groups)
            foreach (var entry in entries.OfType<TagEntry>())
                results.Add((Tag(entry), SearchText(entry.Tag, entry.Description, header), SearchText(null, entry.Help)));
        results.Add((MenuIcons.Item(Strings.EditorPickerMenu, MenuIcons.Crosshair(), _app.ShowElementPicker),
            SearchText(null, nameof(Strings.EditorPickerMenu), nameof(Strings.EditorTagsElements)), ""));
        var noMatch = new MenuItem { Header = Strings.EditorTagsNoMatch, IsEnabled = false };
        foreach (var (item, _, _) in results)
        {
            item.Visibility = Visibility.Collapsed;
            menu.Items.Add(item);
        }
        noMatch.Visibility = Visibility.Collapsed;
        menu.Items.Add(noMatch);

        filter.TextChanged += (_, _) =>
        {
            var query = filter.Text.Trim();
            var filtering = query.Length > 0;
            placeholder.Visibility = filter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var group in groupItems)
                group.Visibility = filtering ? Visibility.Collapsed : Visibility.Visible;
            var any = filtering && results.Any(r => SnippetSearch.MatchesAll(r.Text, query));
            var inHelp = filtering && !any;
            foreach (var (item, text, help) in results)
            {
                var match = filtering && SnippetSearch.MatchesAll(inHelp ? help : text, query);
                item.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                any |= match;
            }
            noMatch.Visibility = filtering && !any ? Visibility.Visible : Visibility.Collapsed;
        };
        filter.PreviewKeyDown += (_, e) =>
        {
            var first = menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Focusable && i.IsEnabled && i.Visibility == Visibility.Visible);
            switch (e.Key)
            {
                case Key.Down when first is not null:
                    first.Focus();
                    e.Handled = true;
                    break;
                case Key.Enter when filter.Text.Trim().Length > 0:
                    // Wie ein Klick auf den ersten Treffer
                    first?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, first));
                    menu.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        };
        // Das Menü holt sich beim Öffnen selbst den Fokus, danach gleich ins Filterfeld
        menu.Opened += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(filter));
        return menu;

        MenuItem Tag(TagEntry entry)
        {
            // Der Hilfetext im Tooltip, umbrochen und in der normalen Schrift statt der des Editors
            var item = new MenuItem
            {
                Header = entry.Tag, InputGestureText = Local(entry.Description), FontFamily = BodyInput.FontFamily,
                ToolTip = new ToolTip
                {
                    Content = new TextBlock { Text = Local(entry.Help), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
                    FontFamily = FontFamily,
                },
            };
            ToolTipService.SetInitialShowDelay(item, 300);
            ToolTipService.SetShowDuration(item, 30000);
            item.Click += (_, _) =>
            {
                if (entry.OwnLine) InsertTag(entry.Tag, entry.Select);
                else Insert(entry.Tag, entry.Select);
            };
            return item;
        }
    }

    void OnPickerClick(object sender, RoutedEventArgs e) => _app.ShowElementPicker();

    void OnAiClick(object sender, RoutedEventArgs e) => _app.ShowAiPrompt();

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
            ShowStatus(Strings.EditorSaved);
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

    void ShowStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
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
            ShowStatus(Running(startTag, Strings.EditorTestRunning));
            var problem = await _app.RunTextAsync(text, DisplayName, alternate: false, target);
            ShowStatus(problem?.Message ?? Strings.EditorTestDone, error: problem is not null);
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

        var run = _stepRun = new StepRun(_app, text, DisplayName, target, span => BreakpointLine(text, span));
        run.Changed += (_, view) => ShowStep(text, view);
        StepMarker.Clear();
        LockForStepRun(true);
        ShowStatus(Running(startTag, Strings.EditorStepRunning));
        try
        {
            await run.RunAsync();
            ShowStatus(StepMarker.Mark == StepMark.Failed ? Strings.EditorStepRunFailed : Strings.EditorStepRunEnded,
                error: StepMarker.Mark == StepMark.Failed);
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
        ShowStatus(Strings.EditorRecording);
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
                ShowStatus(string.Format(Strings.EditorRecordedFormat, Recording.Lines(actions, bar.Mode).Count));
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
        var start = BodyInput.SelectionStart;
        var line = document.GetLineByOffset(start);
        var before = !string.IsNullOrWhiteSpace(document.GetText(line.Offset, start - line.Offset));
        var newLine = ICSharpCode.AvalonEdit.Document.TextUtilities.GetNewLineFromDocument(document, line.LineNumber);
        Insert((before ? newLine : "") + text.ReplaceLineEndings(newLine));
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
        StatusText.Visibility = Visibility.Collapsed;
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
        StatusText.Visibility = Visibility.Visible;
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
            case Key.Escape:
                Close();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!IsDirty) return;
        var answer = MessageDialog.Show(this, string.Format(Strings.EditorSaveChangesFormat, DisplayName),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Cancel || answer == MessageBoxResult.Yes && !Save())
            e.Cancel = true;
    }
}
