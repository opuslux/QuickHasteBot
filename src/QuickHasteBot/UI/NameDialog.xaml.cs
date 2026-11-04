using System.Windows;
using System.Windows.Controls;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Fragt nach einem Namen, zum Klonen oder Umbenennen. Bei Dateien ist der Vorschlag bis auf die Endung markiert,
/// Tippen ersetzt also nur den Namen; bei Ordnern alles.
/// </summary>
internal partial class NameDialog : Window
{
    readonly string _folder;
    readonly string? _currentName;

    NameDialog(Snippet snippet, string title, string promptFormat, string confirm, string suggestion, bool rename)
    {
        InitializeComponent();
        var currentName = Path.GetFileName(snippet.FullPath);
        _folder = Path.GetDirectoryName(snippet.FullPath) ?? "";
        _currentName = rename ? currentName : null;
        Title = title;
        Prompt.Text = string.Format(promptFormat, currentName);
        ConfirmButton.Content = confirm;
        NameInput.Text = suggestion;
        Loaded += (_, _) =>
        {
            NameInput.Focus();
            NameInput.Select(0, snippet.IsFolder ? suggestion.Length : Path.GetFileNameWithoutExtension(suggestion).Length);
        };
    }

    public static NameDialog ForClone(Snippet snippet, string suggestion) =>
        new(snippet, Strings.CloneTitle, Strings.ClonePromptFormat, Strings.CloneConfirm, suggestion, rename: false);

    public static NameDialog ForRename(Snippet snippet) =>
        new(snippet, Strings.RenameTitle, Strings.RenamePromptFormat, Strings.RenameConfirm, Path.GetFileName(snippet.FullPath), rename: true);

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
