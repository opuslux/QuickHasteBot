using System.Windows;
using System.Windows.Controls;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Fragt nach einem Namen, zum Umbenennen oder für einen neuen Ordner. Bei Dateien ist der Vorschlag bis auf
/// die Endung markiert, Tippen ersetzt also nur den Namen; bei Ordnern alles.
/// </summary>
internal partial class NameDialog : Window
{
    readonly string _folder;
    readonly string? _currentName;

    NameDialog(Snippet snippet, string title, string promptFormat, string confirm, string suggestion)
        : this(Path.GetDirectoryName(snippet.FullPath) ?? "", Path.GetFileName(snippet.FullPath), title,
            string.Format(promptFormat, Path.GetFileName(snippet.FullPath)), confirm, suggestion,
            snippet.IsFolder ? suggestion.Length : Path.GetFileNameWithoutExtension(suggestion).Length, hint: null)
    {
    }

    /// <param name="folder">Ordner, in dem der Name gelten soll.</param>
    /// <param name="currentName">Beim Umbenennen der bisherige Name, sonst null.</param>
    /// <param name="selectLength">So viel vom Vorschlag ist markiert, Tippen ersetzt es.</param>
    /// <param name="hint">Zusätzliche Erklärung unter der Frage, null für keine.</param>
    NameDialog(string folder, string? currentName, string title, string prompt, string confirm, string suggestion, int selectLength,
        string? hint)
    {
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
    public static NameDialog ForNewFolder(string parent, string parentDisplay)
    {
        var suggestion = SnippetFiles.SuggestNewFolderName(parent, Strings.NewFolderName);
        return new(parent, currentName: null, Strings.NewFolderTitle, Strings.NewFolderPrompt, Strings.NewFolderConfirm, suggestion,
            suggestion.Length, string.Format(Strings.NewFolderHintFormat, parentDisplay));
    }

    public static NameDialog ForRename(Snippet snippet) =>
        new(snippet, Strings.RenameTitle, Strings.RenamePromptFormat, Strings.RenameConfirm, Path.GetFileName(snippet.FullPath));

    public string FileName => NameInput.Text.Trim();

    void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        var problem = SnippetFiles.Check(_folder, FileName, _currentName);
        Problem.Text = problem switch
        {
            NameProblem.Invalid => Strings.NameInvalid,
            NameProblem.Exists => Strings.NameExists,
            _ => "",
        };
        ConfirmButton.IsEnabled = problem == NameProblem.None;
    }

    void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
