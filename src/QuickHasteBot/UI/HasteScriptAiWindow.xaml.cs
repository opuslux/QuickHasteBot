using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// "HasteScript AI": Fragt, was eine KI am Skript im Editor tun soll, und legt einen vollständigen Prompt in die
/// Zwischenablage, mit Anweisungen, der Referenz aus dem README und dem Skript (<see cref="HasteScriptPrompt"/>).
/// Für eine Adresse wie beim Skriptblock-Fenster ist er viel zu lang, darum geht er in eine beliebige KI per Einfügen.
/// Das Skript wird beim Aktivieren und beim Kopieren frisch gelesen: Änderungen im Editor zählen sofort.
/// Strg+Enter kopiert, Esc schliesst.
/// </summary>
internal partial class HasteScriptAiWindow : Window
{
    readonly AppController _app;
    readonly SnippetEditorWindow _editor;
    readonly string _readme;
    HasteScriptRequest? _request;
    string? _template;

    public HasteScriptAiWindow(AppController app, SnippetEditorWindow editor, string task)
    {
        InitializeComponent();
        _app = app;
        _editor = editor;
        _readme = HelpWindow.ReadReadme();
        Refresh();
        TaskInput.Text = task;

        // Zurück aus dem Editor oder aus hastescript-prompt.txt: Skript und Vorlage neu lesen
        Activated += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            TaskInput.Focus();
            TaskInput.SelectAll();
        };
    }

    /// <summary>Der Prompt, wie ihn "Prompt kopieren" gerade in die Zwischenablage legen würde.</summary>
    internal string Prompt { get; private set; } = "";

    /// <summary>Skript, Markierung und Probleme aus dem Editor und die Vorlage neu lesen.</summary>
    internal void Refresh()
    {
        Title = string.Format(Strings.HasteScriptAiTitleFormat, _editor.DisplayName);
        _request = _editor.HasteScriptAiRequest(TaskInput.Text);
        _template = PromptTemplates.Read(_app.HasteScriptTemplate);
        ResetTemplateLink.Visibility = _app.HasteScriptTemplate.Exists ? Visibility.Visible : Visibility.Collapsed;
        IncludesText.Text = string.Format(Strings.HasteScriptAiIncludesFormat, string.Join(", ", Includes(_request)));
        UpdatePrompt();
    }

    /// <summary>Was mitkommt, für die Zeile unter dem Feld: das Skript, die Probleme, die Markierung.</summary>
    internal static IEnumerable<string> Includes(HasteScriptRequest request)
    {
        var script = request.Script.TrimEnd();
        if (script.Trim().Length == 0)
        {
            yield return Strings.HasteScriptAiScriptEmpty;
        }
        else
        {
            yield return string.Format(Strings.HasteScriptAiScriptFormat, HasteScriptPrompt.LineAt(script, script.Length));
            if (request.SelectionLength > 0 && request.SelectionStart + request.SelectionLength <= request.Script.Length
                && request.Script.Substring(request.SelectionStart, request.SelectionLength).Trim().Length > 0)
            {
                var first = HasteScriptPrompt.LineAt(request.Script, request.SelectionStart);
                var last = HasteScriptPrompt.LineAt(request.Script, request.SelectionStart + request.SelectionLength - 1);
                yield return first == last
                    ? string.Format(Strings.HasteScriptAiSelectionLineFormat, first)
                    : string.Format(Strings.HasteScriptAiSelectionLinesFormat, first, last);
            }
        }
        if (request.Problems.Count > 0)
            yield return string.Format(Strings.HasteScriptAiProblemsFormat, request.Problems.Count);
    }

    void UpdatePrompt()
    {
        if (_request is null) return;
        Prompt = HasteScriptPrompt.Build(_request with { Task = TaskInput.Text }, _template, _readme);
        PromptPreview.Text = Prompt;
        SizeText.Text = string.Format(Strings.HasteScriptAiSizeFormat, Prompt.Length.ToString("N0"));
    }

    void OnTaskChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = TaskInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = "";
        // Das Skript bleibt, wie es beim Aktivieren war: Die Vorschau im Editor würde sonst bei jedem Zeichen laufen
        UpdatePrompt();
    }

    /// <summary>Liest das Skript frisch und legt den Prompt in die Zwischenablage.</summary>
    internal bool Copy()
    {
        Refresh();
        try
        {
            Clipboard.SetText(Prompt);
            ShowStatus(Strings.HasteScriptAiCopied, "TextFillColorSecondaryBrush");
            return true;
        }
        catch (ExternalException ex)
        {
            Log.Error("HasteScript-Prompt konnte nicht in die Zwischenablage gelegt werden", ex);
            ShowStatus(Strings.HasteScriptAiCopyFailed, "SystemFillColorCriticalBrush");
            return false;
        }
    }

    void ShowStatus(string text, string brushKey)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
    }

    void OnCopy(object sender, RoutedEventArgs e) => Copy();

    void OnEditTemplate(object sender, MouseButtonEventArgs e)
    {
        PromptTemplates.Edit(_app.HasteScriptTemplate);
        Refresh();
    }

    void OnResetTemplate(object sender, MouseButtonEventArgs e)
    {
        var template = _app.HasteScriptTemplate;
        if (MessageDialog.Show(this, string.Format(Strings.AiPromptResetConfirm, template.FileName), MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        PromptTemplates.Reset(template, new WindowInteropHelper(this).Handle);
        Refresh();
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Copy();
            e.Handled = true;
        }
    }

    void OnClose(object sender, RoutedEventArgs e) => Close();
}
