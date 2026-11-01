using System.Windows.Input;

namespace QuickHasteBot.Core;

/// <summary>Werte entsprechen den Win32 MOD_*-Flags von RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, Key Key)
{
    public static readonly HotkeyGesture Default = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, Key.Y);

    // Reihenfolge für ToString(), wie Windows sie anzeigt
    static readonly (HotkeyModifiers Flag, string Name)[] ModifierNames =
    [
        (HotkeyModifiers.Ctrl, "Ctrl"),
        (HotkeyModifiers.Shift, "Shift"),
        (HotkeyModifiers.Alt, "Alt"),
        (HotkeyModifiers.Win, "Win"),
    ];

    /// <summary>Ohne Modifier sind nur Funktionstasten erlaubt, sonst wäre z.B. "Y" global blockiert.</summary>
    public bool IsValid =>
        Key != Key.None && !IsModifierKey(Key) &&
        (Modifiers != HotkeyModifiers.None || Key is >= Key.F1 and <= Key.F24);

    public int VirtualKey => KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString()
    {
        var modifiers = FormatModifiers(Modifiers);
        return modifiers.Length == 0 ? FormatKey(Key) : modifiers + "+" + FormatKey(Key);
    }

    /// <summary>Nur die Modifier, z.B. "Ctrl+Shift".</summary>
    public static string FormatModifiers(HotkeyModifiers modifiers) =>
        string.Join("+", ModifierNames.Where(m => modifiers.HasFlag(m.Flag)).Select(m => m.Name));

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = HotkeyModifiers.None;
        Key? key = null;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseModifier(raw, out var modifier))
            {
                modifiers |= modifier;
            }
            else if (key is null && TryParseKey(raw, out var parsed))
            {
                key = parsed;
            }
            else
            {
                return false;
            }
        }

        if (key is null) return false;

        gesture = new HotkeyGesture(modifiers, key.Value);
        return gesture.IsValid;
    }

    public static bool TryParseModifier(string text, out HotkeyModifiers modifier)
    {
        modifier = text.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" or "STRG" or "MOD_CONTROL" => HotkeyModifiers.Ctrl,
            "SHIFT" or "MOD_SHIFT" => HotkeyModifiers.Shift,
            "ALT" or "MOD_ALT" => HotkeyModifiers.Alt,
            "WIN" or "WINDOWS" or "MOD_WIN" => HotkeyModifiers.Win,
            _ => HotkeyModifiers.None,
        };
        return modifier != HotkeyModifiers.None;
    }

    static bool TryParseKey(string text, out Key key)
    {
        // "1" ist lesbarer als "D1"
        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            key = Key.D0 + (text[0] - '0');
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && !int.TryParse(text, out _);
    }

    static string FormatKey(Key key) => key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString();

    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;
}
