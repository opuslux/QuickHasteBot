using System.Windows.Controls;
using System.Windows.Input;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Nimmt eine Tastenkombination auf: hineinklicken und die Kombination drücken. Esc bricht ab.
/// Ersetzt die drei Comboboxen der alten Version.
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    HotkeyGesture _gesture;

    public HotkeyBox()
    {
        // Nicht IsReadOnly: das Fluent-Theme zeigt schreibgeschützte Felder ohne Stil an.
        // Getippter Text kommt trotzdem nie an, siehe OnPreviewKeyDown/OnPreviewTextInput.
        IsUndoEnabled = false;
        AllowDrop = false;
        Cursor = Cursors.Hand;
        CaretBrush = System.Windows.Media.Brushes.Transparent;
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e) => e.Handled = true;

    // Kein Ausschneiden/Einfügen-Menü per Rechtsklick
    protected override void OnContextMenuOpening(ContextMenuEventArgs e) => e.Handled = true;

    /// <summary>Kommt nur für gültige Kombinationen.</summary>
    public event EventHandler<HotkeyGesture>? GestureCaptured;

    public HotkeyGesture Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value;
            Text = value.ToString();
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = Strings.HotkeyPrompt;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Text = _gesture.ToString();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;

        // Bei gedrückter Alt-Taste meldet WPF Key.System und die eigentliche Taste in SystemKey
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }
        if (key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            e.Handled = false;
            return;
        }

        var modifiers = CurrentModifiers();
        if (HotkeyGesture.IsModifierKey(key))
        {
            // Zwischenstand anzeigen, z.B. "Ctrl+Shift+…"
            Text = modifiers == HotkeyModifiers.None ? Strings.HotkeyPrompt : HotkeyGesture.FormatModifiers(modifiers) + "+…";
            return;
        }

        var gesture = new HotkeyGesture(modifiers, key);
        if (!gesture.IsValid)
        {
            Text = string.Format(Strings.HotkeyInvalidFormat, gesture);
            return;
        }

        Gesture = gesture;
        GestureCaptured?.Invoke(this, gesture);
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    static HotkeyModifiers CurrentModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) modifiers |= HotkeyModifiers.Win;
        return modifiers;
    }
}
