using System.Windows;
using System.Windows.Controls;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Fragt nach einem Namen, zum Klonen, Umbenennen oder für einen neuen Ordner. Bei Dateien ist der Vorschlag bis auf
/// die Endung markiert, Tippen ersetzt also nur den Namen; bei Ordnern alles.
/// </summary>
internal partial class NameDialog : Window
{
    readonly string _folder;
    readonly string? _currentName;
    readonly Func<string, string?>? _hotkeyProblem;

    NameDialog(Snippet snippet, string title, string promptFormat, string confirm, string suggestion, bool rename,
        Func<string, string?>? hotkeyProblem)
        : this(Path.GetDirectoryName(snippet.FullPath) ?? "", rename ? Path.GetFileName(snippet.FullPath) : null, title,
            string.Format(promptFormat, Path.GetFileName(snippet.FullPath)), confirm, suggestion,
            snippet.IsFolder ? suggestion.Length : Path.GetFileNameWithoutExtension(suggestion).Length, hint: null, hotkeyProblem)
    {
    }

    /// <param name="folder">Ordner, in dem der Name gelten soll.</param>
    /// <param name="currentName">Beim Umbenennen der bisherige Name, sonst null.</param>
    /// <param name="selectLength">So viel vom Vorschlag ist markiert, Tippen ersetzt es.</param>
    /// <param name="hint">Zusätzliche Erklärung unter der Frage, null für keine.</param>
    NameDialog(string folder, string? currentName, string title, string prompt, string confirm, string suggestion, int selectLength,
        string? hint, Func<string, string?>? hotkeyProblem)
    {
        _hotkeyProblem = hotkeyProblem;
        _folder = folder;
        _currentName = currentName;
        InitializeComponent();
        Title = title;
        Prompt.Text = prompt;
        if (hint is not null)
        {
            Hint.Text = hint;
            Hint.Visibility = Visibility.Visible;
        }
        ConfirmButton.Content = confirm;
        NameInput.Text = suggestion;
        Loaded += (_, _) =>
        {
            NameInput.Focus();
            NameInput.Select(0, selectLength);
        };
    }

    /// <summary>Neuer Ordner, immer im Ordner, der im Popup offen ist. Der Hinweis sagt das und nennt ihn.</summary>
    /// <param name="parent">Vollständiger Pfad des Ordners, in dem der neue entsteht.</param>
    /// <param name="parentDisplay">Derselbe Ordner zum Anzeigen, z.B. "Bausteine › 3-SQL".</param>
    /// <param name="hotkeyProblem">Prüft einen Hotkey im Namen ("SQL [Ctrl+Alt+S]"), null wenn er frei ist.</param>
    public static NameDialog ForNewFolder(string parent, string parentDisplay, Func<string, string?>? hotkeyProblem = null)
    {
        var suggestion = SnippetFiles.SuggestNewFolderName(parent, Strings.NewFolderName);
        return new(parent, currentName: null, Strings.NewFolderTitle, Strings.NewFolderPrompt, Strings.NewFolderConfirm, suggestion,
            suggestion.Length, string.Format(Strings.NewFolderHintFormat, parentDisplay), hotkeyProblem);
    }

    /// <param name="hotkeyProblem">Prüft den Hotkey im neuen Namen ("Gruss [Ctrl+Alt+G].txt"), null wenn er frei ist.</param>
    public static NameDialog ForClone(Snippet snippet, string suggestion, Func<string, string?>? hotkeyProblem = null) =>
        new(snippet, Strings.CloneTitle, Strings.ClonePromptFormat, Strings.CloneConfirm, suggestion, rename: false, hotkeyProblem);

    /// <param name="hotkeyProblem">Prüft den Hotkey im neuen Namen ("Gruss [Ctrl+Alt+G].txt"), null wenn er frei ist.</param>
    public static NameDialog ForRename(Snippet snippet, Func<string, string?>? hotkeyProblem = null) =>
        new(snippet, Strings.RenameTitle, Strings.RenamePromptFormat, Strings.RenameConfirm, Path.GetFileName(snippet.FullPath), rename: true,
            hotkeyProblem);

    public string FileName => NameInput.Text.Trim();

    void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        var problem = SnippetFiles.Check(_folder, FileName, _currentName);
        var hotkey = problem == NameProblem.None ? _hotkeyProblem?.Invoke(FileName) : null;
        Problem.Text = problem switch
        {
            NameProblem.Invalid => Strings.NameInvalid,
            NameProblem.Exists => Strings.NameExists,
            _ => hotkey ?? "",
        };
        ConfirmButton.IsEnabled = problem == NameProblem.None && hotkey is null;
    }

    void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
