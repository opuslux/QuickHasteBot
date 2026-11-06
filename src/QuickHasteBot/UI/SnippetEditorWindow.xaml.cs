using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
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
        Highlighting = SnippetHighlighting.Attach(BodyInput, app.Settings.EvaluateKeyTags, app.Settings.EvaluateScripts);
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
            Highlighting.Dispose();
        };
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
        PreviewText.Text = preview.Text.Length == 0 ? Strings.EditorPreviewEmpty : preview.Text;
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

    void OnTagsClick(object sender, RoutedEventArgs e)
    {
        var menu = TagMenu();
        menu.PlacementTarget = TagsButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Ein Eintrag im Tag-Menü. <see cref="Select"/>: was nach dem Einfügen markiert wird, zum Überschreiben.</summary>
    sealed record TagEntry(string Tag, string? Description = null, string? Select = null);

    /// <summary>
    /// "Tag einfügen ▾": oben eine Filterzeile, darunter die Gruppen. Sobald gefiltert wird, stehen statt der Gruppen alle
    /// passenden Tags in einer Liste (gesucht in Tag, Beschreibung und Gruppe, wie die Suche im Popup). Enter fügt den
    /// ersten Treffer ein, ↓ geht in die Liste.
    /// </summary>
    internal ContextMenu TagMenu()
    {
        (string Header, UIElement Icon, TagEntry?[] Entries)[] groups =
        [
            (Strings.EditorTagsKeys, MenuIcons.Glyph(MenuIcons.Keys),
            [
                new("{tab}", Strings.TagNextField), new("{shift+tab}", Strings.TagPreviousField), new("{enter}", Strings.TagSubmit),
                new("{space}", Strings.TagCheckbox), new("{esc}"), null,
                new("{up}"), new("{down}"), new("{down,3}", Strings.TagRepeat), new("{left}"), new("{right}"), new("{home}"), new("{end}"), null,
                new("{bs}"), new("{del}"), new("{ctrl+a}", Strings.TagSelectAll), new("{alt+down}", Strings.TagOpenDropdown),
                new("{ctrl+enter}", Strings.TagSendChat), new("{f5}"),
            ]),
            (Strings.EditorTagsFlow, MenuIcons.Glyph(MenuIcons.Flow),
            [
                new("{wait,500}", Strings.TagWait, "500"), new("{delay,50}", Strings.TagDelay, "50"), new("{clear}", Strings.TagClear), null,
                new("{guard,off}", Strings.TagGuardOff), new("{guard,on}", Strings.TagGuardOn),
            ]),
            (Strings.EditorTagsElements, MenuIcons.Crosshair(),
            [
                new("{focus,#id}", Strings.TagFocus, "id"), new("{click,name=Label}", Strings.TagClick, "Label"),
                new("{set,#id,\"\"}", Strings.TagSet, "id"), null,
                new("{window,program}", Strings.TagWindow, "program"), new("{start,program}", Strings.TagStart, "program"),
            ]),
            (Strings.EditorTagsScripts, MenuIcons.Glyph(MenuIcons.Script),
            [
                new("{{ date() }}", Strings.TagDate), new("{{ date('dd.MM.yyyy', 14) }}", Strings.TagDateIn, "14"), new("{{ time() }}"),
                new("{{ isoweek() }}", Strings.TagWeek), new("{{ clipboard }}", Strings.TagClipboard), new("{{ user }}", Strings.TagUser),
                new("{{ guid() }}"),
            ]),
        ];

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
            var group = new MenuItem { Header = header, Icon = icon };
            foreach (var entry in entries)
                group.Items.Add(entry is null ? new Separator() : Tag(entry));
            if (header == Strings.EditorTagsElements)
            {
                group.Items.Add(new Separator());
                group.Items.Add(MenuIcons.Item(Strings.EditorPickerMenu, MenuIcons.Crosshair(), _app.ShowElementPicker));
            }
            groupItems.Add(group);
            menu.Items.Add(group);
        }

        // Die flache Trefferliste, versteckt bis gefiltert wird
        var results = new List<(MenuItem Item, string Text)>();
        foreach (var (header, _, entries) in groups)
            foreach (var entry in entries.OfType<TagEntry>())
                results.Add((Tag(entry), $"{entry.Tag} {entry.Description} {header}"));
        results.Add((MenuIcons.Item(Strings.EditorPickerMenu, MenuIcons.Crosshair(), _app.ShowElementPicker),
            $"{Strings.EditorPickerMenu} {Strings.EditorTagsElements}"));
        var noMatch = new MenuItem { Header = Strings.EditorTagsNoMatch, IsEnabled = false };
        foreach (var (item, _) in results)
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
            var any = false;
            foreach (var (item, text) in results)
            {
                var match = filtering && SnippetSearch.MatchesAll(text, query);
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
            var item = new MenuItem { Header = entry.Tag, InputGestureText = entry.Description ?? "", FontFamily = BodyInput.FontFamily };
            item.Click += (_, _) => Insert(entry.Tag, entry.Select);
            return item;
        }
    }

    void OnPickerClick(object sender, RoutedEventArgs e) => _app.ShowElementPicker();

    void OnAiClick(object sender, RoutedEventArgs e) => _app.ShowAiPrompt();

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
    /// Einfügen, mit dem Text, wie er gerade im Editor steht. Nochmals drücken bricht das Warten ab.
    /// </summary>
    async void OnTest(object sender, RoutedEventArgs e)
    {
        if (_test is not null)
        {
            _test.Cancel();
            return;
        }

        // Was läuft, wird festgehalten, bevor man wegklickt. Ein einzelnes Tag wird markiert, damit man es sieht.
        var (start, length, scope) = TestRange();
        var text = BodyInput.Text.Substring(start, length);
        if (scope is TestScope.Tag or TestScope.Script)
            BodyInput.Select(start, length);

        using var test = _test = new CancellationTokenSource(TargetTimeout);
        TestButton.Content = Strings.EditorTestCancel;
        ShowWaiting(scope == TestScope.All ? null : Shorten(text));
        try
        {
            var target = await WaitForTargetAsync(test.Token);
            HideWaiting();
            if (target == 0)
            {
                ShowStatus(Strings.EditorTestCancelled);
                return;
            }
            ShowStatus(Strings.EditorTestRunning);
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
    /// Wartet auf den Klick ins Zielfeld: Banner in der Akzentfarbe statt der Statuszeile, und ein Kärtchen am
    /// Mauszeiger, weil man beim Hinüberfahren dorthin schaut. <paramref name="part"/>: was läuft, null für alles.
    /// </summary>
    void ShowWaiting(string? part)
    {
        var inlines = TestBannerText.Inlines;
        inlines.Clear();
        inlines.Add(new Run(Strings.EditorTestClickTarget) { FontWeight = FontWeights.SemiBold, FontSize = 13 });
        inlines.Add(new LineBreak());
        if (part is null)
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
        _hint = new CursorHint(Strings.CursorHintTitle, part,
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
            case Key.F5:
                OnTest(this, e);
                break;
            case Key.Escape when _test is not null:
                _test.Cancel();
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
