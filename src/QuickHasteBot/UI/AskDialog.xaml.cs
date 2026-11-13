using System.Windows;
using System.Windows.Controls;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Die Fragen aus ask() eines Bausteins, alle in einem Fenster, bevor er eingefügt wird. Vorschlag ist die Vorgabe aus
/// ask('Kunde', 'Muster'), sonst die letzte Antwort; sie ist markiert, Tippen ersetzt sie. Enter fügt ein, Esc bricht ab.
/// Knopf, Titel und die Zeile oben kommen aus den Optionen der ask(), z.B. { button: 'Orakel befragen' }.
/// </summary>
internal partial class AskDialog : Window
{
    readonly List<(Question Question, TextBox Input)> _inputs = [];

    internal AskDialog(string snippetName, IReadOnlyList<Question> questions, AskWindowOptions options,
        IReadOnlyDictionary<string, string> lastAnswers)
    {
        InitializeComponent();
        Title = options.Title ?? string.Format(Strings.AskTitleFormat, snippetName);
        Prompt.Text = options.Text ?? string.Format(Strings.AskPromptFormat, snippetName);
        ConfirmButton.Content = options.Button ?? Strings.AskConfirm;
        foreach (var question in questions)
        {
            var label = new Label { Content = question.Label, Margin = new Thickness(0, 10, 0, 4), Padding = new Thickness(0) };
            var input = new TextBox
            {
                Text = question.Default.Length > 0 ? question.Default : lastAnswers.GetValueOrDefault(question.Label, ""),
            };
            input.GotKeyboardFocus += (_, _) => input.SelectAll();
            label.Target = input;
            Fields.Items.Add(label);
            Fields.Items.Add(input);
            _inputs.Add((question, input));
        }
        Loaded += (_, _) =>
        {
            Activate();
            _inputs[0].Input.Focus();
        };
    }

    /// <summary>Was eingegeben wurde, nach Beschriftung.</summary>
    internal IReadOnlyDictionary<string, string> Answers => _inputs.ToDictionary(i => i.Question.Label, i => i.Input.Text);

    /// <summary>Die Felder, für Tests.</summary>
    internal IReadOnlyList<TextBox> Inputs => _inputs.Select(i => i.Input).ToList();

    /// <summary>Fragt und gibt die Antworten zurück, null bei Abbrechen.</summary>
    internal static IReadOnlyDictionary<string, string>? Ask(string snippetName, IReadOnlyList<Question> questions,
        AskWindowOptions options, IReadOnlyDictionary<string, string> lastAnswers)
    {
        var dialog = new AskDialog(snippetName, questions, options, lastAnswers);
        return dialog.ShowDialog() == true ? dialog.Answers : null;
    }

    void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
