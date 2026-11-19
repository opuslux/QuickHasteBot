using System.Windows;
using System.Windows.Controls;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Legt den Hotkey einer Datei oder eines Ordners fest. Der Dialog liefert nur die Kombination, gespeichert wird sie
/// danach in snippets.json (<see cref="AppController.SetFileHotkey"/>), der Name bleibt. Ein Hotkey, der schon an
/// einen anderen Eintrag vergeben ist, das Popup öffnet oder einem anderen Programm gehört, lässt sich nicht übernehmen.
/// </summary>
internal partial class HotkeyDialog : Window
{
    readonly AppController _app;
    readonly Snippet _snippet;
    HotkeyGesture? _gesture;
    bool _suspended;

    public HotkeyDialog(AppController app, Snippet snippet)
    {
        InitializeComponent();
        _app = app;
        _snippet = snippet;
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

    /// <summary>Nach "Übernehmen" der neue Hotkey, nach "Hotkey entfernen" null.</summary>
    public HotkeyGesture? NewGesture { get; private set; }

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
        string? problem = null;
        if (_gesture is { } gesture && gesture != _snippet.Hotkey)
        {
            problem = _app.FileHotkeys.Conflict(gesture, _snippet.FullPath);
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

    void OnConfirm(object sender, RoutedEventArgs e)
    {
        NewGesture = _gesture;
        DialogResult = true;
    }

    void OnRemove(object sender, RoutedEventArgs e)
    {
        NewGesture = null;
        DialogResult = true;
    }
}
