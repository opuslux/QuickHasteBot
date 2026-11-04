using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QuickHasteBot.Core;

/// <summary>Ein Schritt eines Formular-Bausteins, ausgeführt von PasteService.RunFormAsync.</summary>
public abstract record FormStep;

/// <summary>Text, der getippt wird. Ein Umbruch wird zu Shift+Enter, damit er ein Formular nicht abschickt.</summary>
public sealed record TextStep(string Text) : FormStep;

/// <param name="Name">So wie er im Tag stand, kleingeschrieben, z.B. "shift+tab". Für Vorschau und Log.</param>
/// <param name="SwitchesWindow">Die Taste wechselt typischerweise das Fenster (Startmenü, Alt+Tab …), danach ist der Fensterschutz aus.</param>
public sealed record KeyStep(string Name, ushort Vk, IReadOnlyList<ushort> Modifiers, int Repeat, bool SwitchesWindow) : FormStep;

/// <summary>{wait,ms}: einmalige Pause.</summary>
public sealed record WaitStep(int Milliseconds) : FormStep;

/// <summary>{delay,ms}: Pause nach jeder Taste und jedem Zeichen ab hier.</summary>
public sealed record DelayStep(int Milliseconds) : FormStep;

/// <summary>{guard,on|off}: Fensterschutz ab hier ein- oder ausschalten.</summary>
public sealed record GuardStep(bool On) : FormStep;

public enum ElementAction { Focus, Click, Set }

/// <summary>{focus,…}, {click,…}, {set,…,wert}: ein Element im Vordergrundfenster per UI Automation ansprechen.</summary>
public sealed record ElementStep(ElementAction Action, ElementSelector Selector, string? Value) : FormStep;

/// <summary>
/// Sammelt die Schritte eines Formular-Bausteins. Im Text stehen statt der Schritte unsichtbare Marker
/// (Zeichen aus der Private Use Area), damit Tags und key() aus Skripten in einer geordneten Liste landen,
/// ohne dass die Skript-Engine davon wissen muss.
/// </summary>
public sealed class FormBuilder
{
    readonly List<FormStep> _steps = [];

    public IReadOnlyList<FormStep> Steps => _steps;

    public string Marker(FormStep step)
    {
        _steps.Add(step);
        return KeyTags.MarkerStart + (_steps.Count - 1).ToString(CultureInfo.InvariantCulture) + KeyTags.MarkerEnd;
    }

    public string Markers(IEnumerable<FormStep> steps) => string.Concat(steps.Select(Marker));
}

/// <summary>
/// Tasten-Tags wie {tab}, {enter}, {shift+tab}, {wait,500} in Textbausteinen: machen aus einem Baustein eine
/// Abfolge von Text und Tasten, z.B. um Formulare auszufüllen. Nur bekannte Namen zählen, alles andere in
/// geschweiften Klammern (JSON, {name}) bleibt Text. Innerhalb von {{ … }} werden keine Tags erkannt.
/// </summary>
public static partial class KeyTags
{
    internal const char MarkerStart = '';
    internal const char MarkerEnd = '';

    public const int MaxWait = 10_000;
    public const int MaxDelay = 1_000;
    public const int MaxRepeat = 100;

    // Virtual-Key-Codes, hier statt in NativeMethods, damit der Core ohne Interop testbar bleibt
    const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B;
    const ushort VK_TAB = 0x09, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_BACK = 0x08;
    const ushort VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24;
    const ushort VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    const ushort VK_INSERT = 0x2D, VK_DELETE = 0x2E, VK_APPS = 0x5D, VK_F1 = 0x70, VK_F4 = 0x73, VK_F10 = 0x79;

    static readonly Dictionary<string, ushort> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = VK_CONTROL, ["alt"] = VK_MENU, ["shift"] = VK_SHIFT, ["win"] = VK_LWIN,
    };

    static readonly Dictionary<string, ushort> Keys = BuildKeys();

    static Dictionary<string, ushort> BuildKeys()
    {
        var keys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["tab"] = VK_TAB, ["enter"] = VK_RETURN, ["esc"] = VK_ESCAPE, ["escape"] = VK_ESCAPE, ["space"] = VK_SPACE,
            ["backspace"] = VK_BACK, ["bs"] = VK_BACK, ["delete"] = VK_DELETE, ["del"] = VK_DELETE,
            ["insert"] = VK_INSERT, ["ins"] = VK_INSERT, ["home"] = VK_HOME, ["end"] = VK_END,
            ["pgup"] = VK_PRIOR, ["pageup"] = VK_PRIOR, ["pgdn"] = VK_NEXT, ["pagedown"] = VK_NEXT,
            ["up"] = VK_UP, ["down"] = VK_DOWN, ["left"] = VK_LEFT, ["right"] = VK_RIGHT,
            ["apps"] = VK_APPS, ["menu"] = VK_APPS,
        };
        for (var i = 0; i < 12; i++)
            keys["f" + (i + 1)] = (ushort)(VK_F1 + i);
        foreach (var (name, vk) in Modifiers)
            keys[name] = vk;
        return keys;
    }

    /// <summary>Tasten, die SendInput mit KEYEVENTF_EXTENDEDKEY senden muss, sonst kommen z.B. Pfeile als Ziffernblock an.</summary>
    static readonly HashSet<ushort> ExtendedKeys =
        [VK_PRIOR, VK_NEXT, VK_END, VK_HOME, VK_LEFT, VK_UP, VK_RIGHT, VK_DOWN, VK_INSERT, VK_DELETE, VK_APPS, VK_LWIN];

    public static bool IsExtended(ushort vk) => ExtendedKeys.Contains(vk);

    // Ein {{ … }}-Block (bleibt unangetastet) oder ein Tag-Kandidat. Argumente in "…" dürfen } und , enthalten.
    [GeneratedRegex("""\{\{.*?\}\}|\{((?:"[^"]*"|[^{}"\r\n])+)\}""", RegexOptions.Singleline)]
    private static partial Regex Candidate();

    [GeneratedRegex(@"^((?:(?:ctrl|alt|shift|win)\+)*[a-z0-9]+)(?:\s*,\s*|\s+|$)", RegexOptions.IgnoreCase)]
    private static partial Regex NamePart();

    /// <summary>Ersetzt die Tags ausserhalb von {{ … }} durch Marker für <paramref name="form"/>.</summary>
    public static string MarkTags(string text, FormBuilder form) =>
        Candidate().Replace(text, match =>
        {
            if (!match.Groups[1].Success) return match.Value; // Skriptblock
            return TryParse(match.Groups[1].Value, null, out var steps, out _) ? form.Markers(steps) : match.Value;
        });

    /// <summary>
    /// Der Inhalt eines Tags ohne Klammern, z.B. "tab", "tab,3", "shift+tab", "wait,500", "guard,off".
    /// <paramref name="count"/> überschreibt die Wiederholung (für key("tab", 3)).
    /// </summary>
    public static bool TryParse(string content, int? count, out IReadOnlyList<FormStep> steps, out string error)
    {
        steps = [];
        error = $"unknown key \"{content}\"";

        // Bewusst streng: "{ end }" in Code-Bausteinen soll keine Taste sein
        var name = NamePart().Match(content);
        if (!name.Success) return false;

        var spec = name.Groups[1].Value.ToLowerInvariant();
        var args = SplitArgs(content[name.Length..]);
        if (args is null) return false;

        if (spec is "focus" or "click" or "set")
            return TryParseElement(spec, args, out steps, out error);

        if (args.Count > 1)
        {
            error = $"too many arguments in \"{content}\"";
            return false;
        }
        var arg = args.Count == 1 ? args[0] : null;

        switch (spec)
        {
            case "wait" or "delay":
            {
                var max = spec == "wait" ? MaxWait : MaxDelay;
                var value = count ?? (int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) ? ms : -1);
                if (value < 0)
                {
                    error = $"\"{spec}\" needs milliseconds, e.g. {{{spec},500}}";
                    return false;
                }
                value = Math.Min(value, max);
                steps = [spec == "wait" ? new WaitStep(value) : new DelayStep(value)];
                return true;
            }
            case "guard":
            {
                if (arg?.ToLowerInvariant() is not ("on" or "off"))
                {
                    error = "\"guard\" needs on or off, e.g. {guard,off}";
                    return false;
                }
                steps = [new GuardStep(arg.Equals("on", StringComparison.OrdinalIgnoreCase))];
                return true;
            }
            case "clear":
                if (arg is not null) return false;
                steps = [Key("ctrl+a", 0x41, [VK_CONTROL], 1), Key("del", VK_DELETE, [], 1)];
                return true;
        }

        var repeat = 1;
        if (count is { } c)
            repeat = c;
        else if (arg is not null && !int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out repeat))
            return false;
        if (repeat < 1) return false;
        repeat = Math.Min(repeat, MaxRepeat);

        var parts = spec.Split('+');
        var keyName = parts[^1];
        var modifiers = parts[..^1].Select(m => Modifiers[m]).Distinct().ToList();

        ushort vk;
        if (Keys.TryGetValue(keyName, out var known))
            vk = known;
        else if (modifiers.Count > 0 && keyName.Length == 1 && char.IsAsciiLetterOrDigit(keyName[0]))
            vk = char.ToUpperInvariant(keyName[0]); // VK von A-Z und 0-9 entspricht dem ASCII-Code
        else
            return false;

        steps = [Key(spec, vk, modifiers, repeat)];
        return true;
    }

    /// <summary>{focus,#email}, {click,Absenden}, {set,#land,"Österreich"}.</summary>
    internal static bool TryParseElement(string spec, List<string> args, out IReadOnlyList<FormStep> steps, out string error)
    {
        steps = [];
        var action = spec switch { "focus" => ElementAction.Focus, "click" => ElementAction.Click, _ => ElementAction.Set };
        var expected = action == ElementAction.Set ? 2 : 1;
        if (args.Count != expected)
        {
            error = action == ElementAction.Set
                ? "\"set\" needs an element and a value, e.g. {set,#email,\"max@example.com\"}"
                : $"\"{spec}\" needs an element, e.g. {{{spec},#email}} or {{{spec},name=Send}}";
            return false;
        }
        if (!ElementSelector.TryParse(args[0], out var selector, out error))
            return false;

        steps = [new ElementStep(action, selector, action == ElementAction.Set ? args[1] : null)];
        return true;
    }

    /// <summary>Ein Element-Tag, wie es der Picker in die Zwischenablage legt. Setzt Argumente nur in "…", wenn nötig.</summary>
    public static string ElementTag(ElementAction action, ElementSelector selector, string? value = null)
    {
        var tag = "{" + action.ToString().ToLowerInvariant() + "," + Quote(selector.ToString());
        if (action == ElementAction.Set)
            tag += "," + Quote(value ?? "", always: true);
        return tag + "}";

        static string Quote(string arg, bool always = false)
        {
            var clean = arg.Replace("\"", "").ReplaceLineEndings(" ");
            return always || clean.IndexOfAny([',', '{', '}']) >= 0 || clean != clean.Trim() ? "\"" + clean + "\"" : clean;
        }
    }

    static KeyStep Key(string name, ushort vk, IReadOnlyList<ushort> modifiers, int repeat) =>
        new(name, vk, modifiers, repeat, SwitchesWindow(vk, modifiers));

    /// <summary>Win-Taste und -Kombinationen, Alt allein, Alt+Tab/Esc/F4/Leertaste, Kontextmenü.</summary>
    static bool SwitchesWindow(ushort vk, IReadOnlyList<ushort> modifiers)
    {
        if (vk == VK_LWIN || modifiers.Contains(VK_LWIN) || vk == VK_APPS) return true;
        if (vk == VK_F10 && modifiers.Contains(VK_SHIFT)) return true; // Shift+F10 = Kontextmenü
        if (vk == VK_MENU && modifiers.Count == 0) return true;
        return modifiers.Contains(VK_MENU) && vk is VK_TAB or VK_ESCAPE or VK_F4 or VK_SPACE;
    }

    /// <summary>"3" → ["3"], "" → [], "\"a,b\", c" → ["a,b", "c"]. Null bei ungültigen Anführungszeichen.</summary>
    static List<string>? SplitArgs(string rest)
    {
        var args = new List<string>();
        if (rest.Length == 0) return args;

        var current = new StringBuilder();
        var quoted = false;
        var wasQuoted = false;
        foreach (var c in rest)
        {
            if (c == '"')
            {
                quoted = !quoted;
                wasQuoted = true;
            }
            else if (c == ',' && !quoted)
            {
                args.Add(wasQuoted ? current.ToString() : current.ToString().Trim());
                current.Clear();
                wasQuoted = false;
            }
            else
                current.Append(c);
        }
        if (quoted) return null;
        args.Add(wasQuoted ? current.ToString() : current.ToString().Trim());
        return args;
    }

    /// <summary>Entfernt Marker-Zeichen aus fremdem Text (Datei, Zwischenablage), damit nur Tags und key() Tasten erzeugen.</summary>
    public static string StripMarkers(string text) =>
        text.Contains(MarkerStart) || text.Contains(MarkerEnd) ? text.Replace(MarkerStart.ToString(), "").Replace(MarkerEnd.ToString(), "") : text;

    public static bool HasMarkers(string text) => text.Contains(MarkerStart);

    /// <summary>
    /// Zerlegt den fertigen Text an den Markern in Schritte. Ein Umbruch ganz am Ende fällt weg,
    /// den hängen Editoren oft an und er würde nach dem letzten Feld noch eine Zeile einfügen.
    /// </summary>
    public static IReadOnlyList<FormStep> Split(string text, FormBuilder form)
    {
        var steps = new List<FormStep>();
        var pos = 0;
        while (pos < text.Length)
        {
            var start = text.IndexOf(MarkerStart, pos);
            if (start < 0)
            {
                AddText(text[pos..]);
                break;
            }
            AddText(text[pos..start]);

            var end = text.IndexOf(MarkerEnd, start);
            if (end < 0) break; // abgeschnittener Marker, kann nur aus Skripten stammen
            if (int.TryParse(text.AsSpan(start + 1, end - start - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index < form.Steps.Count)
                steps.Add(form.Steps[index]);
            pos = end + 1;
        }

        if (steps.Count > 0 && steps[^1] is TextStep last)
        {
            var trimmed = last.Text.EndsWith("\r\n", StringComparison.Ordinal) ? last.Text[..^2]
                : last.Text.EndsWith('\n') || last.Text.EndsWith('\r') ? last.Text[..^1]
                : last.Text;
            if (trimmed.Length == 0)
                steps.RemoveAt(steps.Count - 1);
            else
                steps[^1] = new TextStep(trimmed);
        }
        return steps;

        void AddText(string part)
        {
            if (part.Length > 0)
                steps.Add(new TextStep(part));
        }
    }

    /// <summary>Wie der Schritt als Tag aussieht, für die Vorschau im KI-Fenster.</summary>
    public static string Describe(FormStep step) => step switch
    {
        TextStep t => t.Text,
        KeyStep { Repeat: > 1 } k => $"{{{k.Name},{k.Repeat}}}",
        KeyStep k => $"{{{k.Name}}}",
        WaitStep w => $"{{wait,{w.Milliseconds}}}",
        DelayStep d => $"{{delay,{d.Milliseconds}}}",
        GuardStep g => g.On ? "{guard,on}" : "{guard,off}",
        ElementStep e => ElementTag(e.Action, e.Selector, e.Value),
        _ => "",
    };

    /// <summary>Der fertige Text mit lesbaren Tags statt Markern.</summary>
    public static string Preview(string text, FormBuilder form) =>
        HasMarkers(text) ? string.Concat(Split(text, form).Select(Describe)) : text;
}
