using System.Windows;
using System.Windows.Controls;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Legt den Hotkey einer Datei oder eines Ordners fest. Gespeichert wird er im Namen ("Gruss [Ctrl+Alt+G].txt"),
/// der Dialog liefert nur den neuen Namen, umbenannt wird danach wie beim Umbenennen. Ein Hotkey, der schon an
/// einen anderen Eintrag vergeben ist, das Popup öffnet oder einem anderen Programm gehört, lässt sich nicht übernehmen.
/// </summary>
internal partial class HotkeyDialog : Window
{
    readonly AppController _app;
    readonly Snippet _snippet;
    readonly string _currentName;
    readonly string _folder;
    HotkeyGesture? _gesture;
    bool _suspended;

    public HotkeyDialog(AppController app, Snippet snippet)
    {
        InitializeComponent();
        _app = app;
        _snippet = snippet;
        _currentName = Path.GetFileName(snippet.FullPath);
        _folder = Path.GetDirectoryName(snippet.FullPath) ?? "";
        _gesture = snippet.Hotkey;

        Prompt.Text = string.Format(snippet.IsFolder ? Strings.FileHotkeyFolderPromptFormat : Strings.FileHotkeyPromptFormat, snippet.Name);
        if (snippet.Hotkey is { } current)
            HotkeyInput.Gesture = current;
        RemoveButton.IsEnabled = snippet.Hotkey is not null;

        // Solange aufgenommen wird, darf kein Hotkey auslösen, auch nicht der, den man gerade drückt
        HotkeyInput.GotKeyboardFocus += (_, _) => Suspend();
        HotkeyInput.LostKeyboardFocus += (_, _) => Resume();
        Closed += (_, _) => Resume();
        Loaded += (_, _) => HotkeyInput.Focus();

        Validate();
    }

    /// <summary>Nach "Übernehmen" oder "Hotkey entfernen": der Name, den der Eintrag bekommen soll.</summary>
    public string NewFileName { get; private set; } = "";

    void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        _app.SuspendHotkey();
    }

    void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        _app.ResumeHotkey();
    }

    void OnGestureCaptured(object? sender, HotkeyGesture gesture)
    {
        _gesture = gesture;
        Validate();
    }

    void Validate()
    {
        var name = NameFor(_gesture);
        FileNameText.Text = _gesture is null ? "" : string.Format(Strings.FileHotkeyFileNameFormat, name);

        string? problem = null;
        if (_gesture is { } gesture && gesture != _snippet.Hotkey)
        {
            problem = _app.HotkeyConflict(gesture, _snippet.FullPath)
                ?? NameProblemText(SnippetFiles.Check(_folder, name, _currentName));
            if (problem is null && !gesture.Modifiers.HasFlag(HotkeyModifiers.Ctrl) && !gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)
                && !gesture.Modifiers.HasFlag(HotkeyModifiers.Win))
            {
                // Erlaubt, aber F5 oder Shift+F1 fehlen dann in allen Programmen
                ShowHint(Strings.FileHotkeyGlobalHint, warning: false);
                ConfirmButton.IsEnabled = true;
                return;
            }
        }

        ShowHint(problem ?? "", warning: problem is not null);
        ConfirmButton.IsEnabled = problem is null && _gesture is not null && _gesture != _snippet.Hotkey;
    }

    void ShowHint(string text, bool warning)
    {
        Problem.Text = text;
        Problem.SetResourceReference(TextBlock.ForegroundProperty, warning ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
    }

    string NameFor(HotkeyGesture? gesture) => FileHotkey.WithHotkey(_currentName, gesture, _snippet.IsFolder);

    static string? NameProblemText(NameProblem problem) => problem switch
    {
        NameProblem.Exists => Strings.NameExists,
        NameProblem.Invalid => Strings.NameInvalid,
        _ => null,
    };

    void OnConfirm(object sender, RoutedEventArgs e)
    {
        NewFileName = NameFor(_gesture);
        DialogResult = true;
    }

    void OnRemove(object sender, RoutedEventArgs e)
    {
        var name = NameFor(null);
        if (NameProblemText(SnippetFiles.Check(_folder, name, _currentName)) is { } problem)
        {
            ShowHint(problem, warning: true);
            return;
        }
        NewFileName = name;
        DialogResult = true;
    }
}
