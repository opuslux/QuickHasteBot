using System.Text;

namespace QuickHasteBot.Core;

/// <summary>Welche Tags eine Aufnahme für Felder schreibt.</summary>
public enum RecordMode
{
    /// <summary>Textfelder tippen wie ein Mensch, Auswahlfelder und Checkboxen per {set} auf den Zielzustand.</summary>
    Mixed,

    /// <summary>Jedes Feld per {set}, so kurz wie möglich.</summary>
    AlwaysSet,

    /// <summary>Jedes Feld fokussieren und tippen bzw. anklicken, so wie man es selbst gemacht hat.</summary>
    AlwaysType,
}

/// <summary>Wie ein aufgenommenes Feld bedient wird.</summary>
public enum FieldKind
{
    /// <summary>Textfeld: der Text wird getippt oder gesetzt.</summary>
    Text,

    /// <summary>Dropdown oder ComboBox: eine Option wählen.</summary>
    Choice,

    /// <summary>Checkbox oder Schalter: "on" oder "off".</summary>
    Toggle,
}

/// <summary>Was der Recorder gesehen hat, in der Reihenfolge, in der es passiert ist.</summary>
public abstract record RecordedAction;

/// <summary>Ein anderes Programm kam nach vorne.</summary>
public sealed record RecordedWindow(string Process) : RecordedAction;

/// <summary>
/// Ein Feld, in dem man war. <paramref name="Before"/> beim Betreten, <paramref name="After"/> beim Verlassen; null,
/// wenn das Feld keinen Wert verrät. Steht an der Stelle, an der man das Feld betreten hat.
/// </summary>
/// <param name="Password">Ein Passwortfeld: der Wert bleibt draussen, nur das Feld wird fokussiert.</param>
public sealed record RecordedField(ElementSelector Selector, FieldKind Kind, string? Before, string? After, bool Password = false) : RecordedAction;

/// <summary>Ein Klick auf einen Button, Link, Tab, Listeneintrag …</summary>
/// <param name="Name">Die Beschriftung, um Klicks auf die Optionen eines aufgeklappten Dropdowns zu erkennen.</param>
public sealed record RecordedClick(ElementSelector Selector, string Name) : RecordedAction;

/// <summary>
/// Macht aus einer Aufnahme Bausteintext: ein Schritt pro Zeile, die Umbrüche an den Tags sind nur Layout
/// (<see cref="KeyTags.SplitWithSources"/>). Räumt vorher auf, was beim Vormachen nebenbei passiert, aber im Baustein
/// nichts zu suchen hat.
/// </summary>
public static class Recording
{
    /// <summary>Buttons und Links klickt man, Checkboxen und Dropdowns setzt man, alles andere fokussiert man.</summary>
    public static ElementAction DefaultAction(string type) => type switch
    {
        "Button" or "Hyperlink" or "MenuItem" or "TabItem" or "ListItem" or "TreeItem" or "RadioButton" or "SplitButton" or "DataItem" => ElementAction.Click,
        "CheckBox" or "ComboBox" => ElementAction.Set,
        _ => ElementAction.Focus,
    };

    /// <summary>Der Bausteintext, mit einem Umbruch am Ende, damit die Schreibmarke danach auf einer neuen Zeile steht.</summary>
    public static string Build(IReadOnlyList<RecordedAction> actions, RecordMode mode, string newLine = "\r\n")
    {
        var lines = Lines(actions, mode);
        return lines.Count == 0 ? "" : string.Join(newLine, lines) + newLine;
    }

    /// <summary>Ein Schritt pro Eintrag, z.B. "{focus,#ort}{clear}Bern".</summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<RecordedAction> actions, RecordMode mode) =>
        Clean(actions).Select(action => Line(action, mode)).ToList();

    /// <summary>
    /// Was im Baustein landet: Felder ohne Änderung fallen weg, mehrfaches Betreten desselben Felds hintereinander wird
    /// eins, Klicks auf die gewählte Option eines Dropdowns fallen weg (das {set} wählt sie), und ein Programmwechsel
    /// zählt nur, wenn danach dort etwas passiert und es nicht schon das Programm davor war.
    /// </summary>
    public static IReadOnlyList<RecordedAction> Clean(IReadOnlyList<RecordedAction> actions)
    {
        // Dasselbe Feld direkt nochmals betreten (z.B. Fokus kurz auf eine Option und zurück): ein Feld, erster und letzter Wert
        var merged = new List<RecordedAction>();
        foreach (var action in actions)
        {
            if (action is RecordedField field && merged.Count > 0 && merged[^1] is RecordedField last && SameField(last, field))
                merged[^1] = last with { After = field.After ?? last.After, Password = last.Password || field.Password };
            else
                merged.Add(action);
        }

        var kept = new List<RecordedAction>();
        RecordedField? lastChoice = null;
        foreach (var action in merged)
        {
            switch (action)
            {
                case RecordedField { Password: true } field:
                    kept.Add(field);
                    lastChoice = null;
                    break;
                case RecordedField field when field.After is null || field.After == (field.Before ?? ""):
                    break; // nichts geändert, oder der Wert ist nicht lesbar
                case RecordedField field:
                    kept.Add(field);
                    lastChoice = field.Kind == FieldKind.Choice ? field : null;
                    break;
                case RecordedClick click when lastChoice is not null && click.Name.Equals(lastChoice.After, StringComparison.OrdinalIgnoreCase):
                    break; // die Option im aufgeklappten Dropdown
                default:
                    kept.Add(action);
                    lastChoice = null;
                    break;
            }
        }

        // Programmwechsel: nur, wenn danach dort etwas passiert, und nicht zweimal dasselbe Programm hintereinander
        var result = new List<RecordedAction>();
        string? process = null;
        for (var i = 0; i < kept.Count; i++)
        {
            if (kept[i] is not RecordedWindow window)
            {
                result.Add(kept[i]);
                continue;
            }
            var followedByAction = i + 1 < kept.Count && kept[i + 1] is not RecordedWindow;
            if (!followedByAction || window.Process.Equals(process, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(window);
            process = window.Process;
        }
        return result;
    }

    static bool SameField(RecordedField a, RecordedField b) => a.Kind == b.Kind && a.Selector.ToString() == b.Selector.ToString();

    static string Line(RecordedAction action, RecordMode mode) => action switch
    {
        RecordedWindow window => KeyTags.WindowTag(new WindowSelector(window.Process, null, null)),
        RecordedClick click => KeyTags.ElementTag(ElementAction.Click, click.Selector),
        RecordedField { Password: true } field => KeyTags.ElementTag(ElementAction.Focus, field.Selector),
        RecordedField field => FieldLine(field, mode),
        _ => "",
    };

    static string FieldLine(RecordedField field, RecordMode mode)
    {
        var value = field.After ?? "";
        switch (field.Kind)
        {
            case FieldKind.Toggle:
                return mode == RecordMode.AlwaysType
                    ? KeyTags.ElementTag(ElementAction.Click, field.Selector)
                    : KeyTags.ElementTag(ElementAction.Set, field.Selector, value);
            case FieldKind.Choice:
                return mode == RecordMode.AlwaysType
                    ? KeyTags.ElementTag(ElementAction.Focus, field.Selector) + Literal(value)
                    : KeyTags.ElementTag(ElementAction.Set, field.Selector, value);
        }

        // {set} verliert Anführungszeichen und Umbrüche (KeyTags.ElementTag), solche Werte werden lieber getippt
        if (mode == RecordMode.AlwaysSet && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return KeyTags.ElementTag(ElementAction.Set, field.Selector, value);
        var clear = string.IsNullOrEmpty(field.Before) ? "" : "{clear}";
        return KeyTags.ElementTag(ElementAction.Focus, field.Selector) + clear + Literal(value);
    }

    /// <summary>
    /// Text, der genau so getippt wird. Enthält er etwas, das als Tag oder Skript gelesen würde ({tab}, {{, ein Kommentar {# … }), steht er
    /// als JavaScript-Text in {{ '…' }}: Was ein Skript ausgibt, wird nie als Tag gelesen.
    /// </summary>
    internal static string Literal(string text)
    {
        if (!text.Contains('{') || !text.Contains("{{") && !HasTag(text)) return text;

        var js = new StringBuilder("{{ '");
        foreach (var c in text)
        {
            js.Append(c switch
            {
                '\\' => @"\\",
                '\'' => @"\'",
                '\n' => @"\n",
                '\r' => @"\r",
                '}' => @"\x7d", // als JavaScript-Escape, "}}" würde den Block beenden
                _ => c.ToString(),
            });
        }
        return js.Append("' }}").ToString();
    }

    /// <summary>Ein Tag oder ein Kommentar {# … }, der beim Einfügen wegfiele.</summary>
    static bool HasTag(string text) =>
        KeyTags.Candidate().Matches(text).Any(m => m.Groups["note"].Success
            || m.Groups[1].Success && KeyTags.TryParse(m.Groups[1].Value, null, out _, out _));
}
