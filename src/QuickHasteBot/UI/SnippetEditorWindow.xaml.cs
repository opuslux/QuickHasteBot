using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    readonly string _folder;
    readonly string _extension;
    readonly DispatcherTimer _previewTimer;
    string? _path;
    TextEncoding _encoding = TextEncoding.Utf8;
    string _savedName;
    string _savedText;
    DateTime _savedWriteTime;
    bool _loading = true;

    internal SnippetHighlighting Highlighting { get; }
    CancellationTokenSource? _test;

    SnippetEditorWindow(AppController app, string folder, string? path, string name, string text, TextEncoding encoding)
    {
        InitializeComponent();
        _app = app;
        _folder = folder;
        _path = path;
        _extension = path is null ? app.NewSnippetExtension : Path.GetExtension(path);
        _encoding = encoding;
        _savedName = name;
        _savedText = text;
        _savedWriteTime = path is null ? default : File.GetLastWriteTimeUtc(path);
        _previewTimer = new DispatcherTimer(PreviewDelay, DispatcherPriority.Background, (_, _) => UpdatePreview(), Dispatcher) { IsEnabled = false };

        SetUpBody();
        Highlighting = SnippetHighlighting.Attach(BodyInput, app.Settings.EvaluateKeyTags, app.Settings.EvaluateScripts);
        NameInput.Text = name;
        BodyInput.Text = text;
        ExtensionText.Text = _extension;
        var relative = Path.GetRelativePath(app.StructureRoot, folder);
        FolderText.Text = relative == "." ? "" : "📁 " + relative.Replace(Path.DirectorySeparatorChar.ToString(), " › ");
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

    public bool IsDirty => NameInput.Text.Trim() != _savedName || BodyInput.Text != _savedText || _path is null && BodyInput.Text.Length > 0;

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

    ContextMenu TagMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Group(Strings.EditorTagsKeys,
            Tag("{tab}", Strings.TagNextField), Tag("{shift+tab}", Strings.TagPreviousField), Tag("{enter}", Strings.TagSubmit),
            Tag("{space}", Strings.TagCheckbox), Tag("{esc}"), null,
            Tag("{up}"), Tag("{down}"), Tag("{down,3}", Strings.TagRepeat), Tag("{left}"), Tag("{right}"), Tag("{home}"), Tag("{end}"), null,
            Tag("{bs}"), Tag("{del}"), Tag("{ctrl+a}", Strings.TagSelectAll), Tag("{alt+down}", Strings.TagOpenDropdown),
            Tag("{ctrl+enter}", Strings.TagSendChat), Tag("{f5}")));
        menu.Items.Add(Group(Strings.EditorTagsFlow,
            Tag("{wait,500}", Strings.TagWait, "500"), Tag("{delay,50}", Strings.TagDelay, "50"), Tag("{clear}", Strings.TagClear), null,
            Tag("{guard,off}", Strings.TagGuardOff), Tag("{guard,on}", Strings.TagGuardOn)));
        var elements = Group(Strings.EditorTagsElements,
            Tag("{focus,#id}", Strings.TagFocus, "id"), Tag("{click,name=Label}", Strings.TagClick, "Label"),
            Tag("{set,#id,\"\"}", Strings.TagSet, "id"), null,
            Tag("{window,program}", Strings.TagWindow, "program"));
        elements.Items.Add(new Separator());
        var pick = new MenuItem { Header = Strings.EditorPickerMenu };
        pick.Click += (_, _) => _app.ShowElementPicker();
        elements.Items.Add(pick);
        menu.Items.Add(elements);
        menu.Items.Add(Group(Strings.EditorTagsScripts,
            Tag("{{ date() }}", Strings.TagDate), Tag("{{ date('dd.MM.yyyy', 14) }}", Strings.TagDateIn, "14"), Tag("{{ time() }}"),
            Tag("{{ isoweek() }}", Strings.TagWeek), Tag("{{ clipboard }}", Strings.TagClipboard), Tag("{{ user }}", Strings.TagUser),
            Tag("{{ guid() }}")));
        return menu;

        MenuItem Group(string header, params MenuItem?[] items)
        {
            var group = new MenuItem { Header = header };
            foreach (var item in items)
                group.Items.Add(item is null ? new Separator() : item);
            return group;
        }

        MenuItem Tag(string tag, string? description = null, string? select = null)
        {
            var item = new MenuItem { Header = tag, InputGestureText = description ?? "", FontFamily = BodyInput.FontFamily };
            item.Click += (_, _) => Insert(tag, select);
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
        var name = NameInput.Text.Trim();
        var fileName = name + _extension;
        var currentName = _path is null ? null : Path.GetFileName(_path);
        // Den Namen prüfen, nicht name + Endung: ".txt" allein wäre sonst ein gültiger Dateiname
        var problem = name.Length == 0 ? NameProblem.Empty : SnippetFiles.Check(_folder, fileName, currentName);
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
            var answer = MessageBox.Show(this, string.Format(Strings.EditorChangedOnDiskFormat, DisplayName), "QuickHasteBot",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return false;
        }

        try
        {
            var path = _path;
            if (path is not null && currentName != fileName)
                path = SnippetFiles.Rename(path, fileName);
            path ??= Path.Combine(_folder, fileName);

            var used = TextFile.Write(path, BodyInput.Text, _encoding);
            if (used != _encoding)
                Log.Info($"Baustein enthält Zeichen, die {_encoding} nicht kann, gespeichert als {used}: {path}");

            (_path, _encoding, _savedName, _savedText) = (path, used, name, BodyInput.Text);
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

        using var test = _test = new CancellationTokenSource(TargetTimeout);
        TestButton.Content = Strings.EditorTestCancel;
        ShowStatus(Strings.EditorTestClickTarget);
        try
        {
            var target = await WaitForTargetAsync(test.Token);
            if (target == 0)
            {
                ShowStatus(Strings.EditorTestCancelled);
                return;
            }
            ShowStatus(Strings.EditorTestRunning);
            var problem = await _app.RunTextAsync(BodyInput.Text, DisplayName, alternate: false, target);
            ShowStatus(problem?.Message ?? Strings.EditorTestDone, error: problem is not null);
        }
        finally
        {
            _test = null;
            TestButton.Content = Strings.EditorTest;
        }
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
        var answer = MessageBox.Show(this, string.Format(Strings.EditorSaveChangesFormat, DisplayName), "QuickHasteBot",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Cancel || answer == MessageBoxResult.Yes && !Save())
            e.Cancel = true;
    }
}
