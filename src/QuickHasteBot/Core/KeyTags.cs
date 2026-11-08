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

/// <summary>{window,…}: dieses Programmfenster nach vorne holen, die folgenden Schritte gehen dorthin.</summary>
public sealed record WindowStep(WindowSelector Window) : FormStep;

/// <summary>
/// {start,programm} und {start,programm,"argumente"}: läuft das Programm schon, wie {window,process=…}, sonst starten und
/// auf sein Fenster warten. Nur als Tag im Baustein, nicht aus Skripten, damit nie Skript-Ausgabe oder Zwischenablage
/// ein Programm startet.
/// </summary>
/// <param name="Program">Name oder Pfad, immer mit ".exe", z.B. "notepad.exe".</param>
public sealed record StartStep(string Program, string? Arguments, WindowSelector Window) : FormStep;

public enum ElementAction { Focus, Click, Set }

/// <summary>{focus,…}, {click,…}, {set,…,wert}: ein Element im Vordergrundfenster per UI Automation ansprechen.</summary>
public sealed record ElementStep(ElementAction Action, ElementSelector Selector, string? Value) : FormStep;

/// <summary>
/// Ein {{ … }}-Block, der erst ausgewertet wird, wenn der Lauf dort ankommt: der erste mit run() und alle danach
/// (<see cref="ScriptSession.RenderStaged"/>). Seine Ausgabe kommt dann als Schritte dazu.
/// </summary>
/// <param name="Block">Die Nummer des (ersten) Blocks, für Fehlermeldungen.</param>
/// <param name="Text">
/// Der Block samt {{ }}. Steht er mit anderen Blöcken allein auf einer Zeile, die ganze Zeile samt Umbruch: Sie fällt
/// weg, wenn ihre Blöcke nichts ausgeben.
/// </param>
public sealed record DeferredStep(int Block, string Text) : FormStep;

/// <summary>Eine Stelle im Bausteintext, so wie er an <see cref="KeyTags.MarkTags"/> ging.</summary>
public readonly record struct SourceSpan(int Start, int Length)
{
    public int End => Start + Length;

    public static SourceSpan FromBounds(int start, int end) => new(start, Math.Max(0, end - start));
}

/// <summary>Ein Schritt und die Stelle im Bausteintext, aus der er stammt: für den Schritt-für-Schritt-Lauf im Editor.</summary>
public sealed record SourcedStep(FormStep Step, SourceSpan Source);

/// <summary>
/// Sammelt die Schritte eines Formular-Bausteins. Im Text stehen statt der Schritte unsichtbare Marker
/// (Zeichen aus der Private Use Area), damit Tags und key() aus Skripten in einer geordneten Liste landen,
/// ohne dass die Skript-Engine davon wissen muss.
/// Merkt sich ausserdem, woher jeder Schritt im Bausteintext stammt: ein Tag von seiner genauen Stelle, key() und
/// Co. vom ganzen {{ … }}-Block. Dafür zeichnen <see cref="KeyTags.MarkTags"/> und <see cref="SnippetScript.Render"/>
/// auf, was sie ersetzt haben. Pro Builder je ein Durchgang.
/// </summary>
public sealed class FormBuilder
{
    readonly List<FormStep> _steps = [];
    readonly List<SourceSpan> _sources = [];
    SourceSpan? _block;

    /// <summary>Bausteintext → Text mit Markern statt Tags.</summary>
    internal TextMap Tags { get; } = new();

    /// <summary>Text mit Markern → Text mit Skript-Ausgaben statt {{ … }}.</summary>
    internal TextMap Scripts { get; } = new();

    public IReadOnlyList<FormStep> Steps => _steps;

    /// <param name="source">Wo der Tag im Bausteintext steht. Ohne Angabe der Skriptblock, der gerade läuft.</param>
    public string Marker(FormStep step, SourceSpan? source = null)
    {
        _steps.Add(step);
        _sources.Add(source ?? _block ?? default);
        return KeyTags.MarkerStart + (_steps.Count - 1).ToString(CultureInfo.InvariantCulture) + KeyTags.MarkerEnd;
    }

    public string Markers(IEnumerable<FormStep> steps, SourceSpan? source = null) =>
        string.Concat(steps.Select(step => Marker(step, source)));

    internal SourceSpan SourceOf(int step) => _sources[step];

    /// <summary>Ein {{ … }}-Block (Stelle im Text mit Markern) beginnt, key() und Co. stammen ab jetzt von dort.</summary>
    internal void BeginScript(int index, int length) => _block = Tags.Back(index, index + length);

    internal void EndScript() => _block = null;

    /// <summary>Ein zurückgestellter Block (<see cref="DeferredStep"/>) läuft, key() und Co. stammen von dort.</summary>
    internal void BeginDeferred(SourceSpan source) => _block = source;

    internal void EndDeferred() => _block = null;

    /// <summary>Woher ein Stück des fertigen Texts stammt. Text aus einem Skript zählt mit dem ganzen Block.</summary>
    internal SourceSpan SourceOfText(int start, int end)
    {
        var marked = Scripts.Back(start, end);
        return Tags.Back(marked.Start, marked.End);
    }
}

/// <summary>
/// Was ein Durchgang über einen Text ersetzt hat, in der Reihenfolge der Ersetzungen. Führt Stellen im Ergebnis auf
/// die Eingabe zurück: ausserhalb von Ersetzungen verschoben, innerhalb auf die ganze ersetzte Stelle.
/// </summary>
internal sealed class TextMap
{
    readonly List<(int OutStart, int OutEnd, int InStart, int InEnd)> _replaced = [];
    int _shift; // Ausgabe minus Eingabe nach der letzten Ersetzung

    public void Replaced(int inStart, int inLength, int outLength)
    {
        var outStart = inStart + _shift;
        _replaced.Add((outStart, outStart + outLength, inStart, inStart + inLength));
        _shift += outLength - inLength;
    }

    public SourceSpan Back(int start, int end)
    {
        var from = BackStart(start);
        return SourceSpan.FromBounds(from, Math.Max(from, BackEnd(end)));
    }

    int BackStart(int pos)
    {
        var shift = 0;
        foreach (var r in _replaced)
        {
            if (r.OutStart <= pos && pos < r.OutEnd) return r.InStart;
            if (r.OutEnd > pos) break;
            shift = r.OutEnd - r.InEnd;
        }
        return pos - shift;
    }

    int BackEnd(int pos)
    {
        var shift = 0;
        foreach (var r in _replaced)
        {
            if (r.OutStart < pos && pos <= r.OutEnd) return r.InEnd;
            if (r.OutEnd >= pos) break;
            shift = r.OutEnd - r.InEnd;
        }
        return pos - shift;
    }
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

    // Ein {{ … }}-Block (bleibt unangetastet), ein Kommentar {# … } (auch über mehrere Zeilen, endet beim ersten })
    // oder ein Tag-Kandidat. Argumente in "…" dürfen } und , enthalten.
    [GeneratedRegex("""\{\{.*?\}\}|(?<note>\{#[^}]*\})|\{((?:"[^"]*"|[^{}"\r\n])+)\}""", RegexOptions.Singleline)]
    internal static partial Regex Candidate();

    [GeneratedRegex(@"^((?:(?:ctrl|alt|shift|win)\+)*[a-z0-9]+)(?:\s*,\s*|\s+|$)", RegexOptions.IgnoreCase)]
    internal static partial Regex NamePart();

    static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
        { "wait", "delay", "guard", "clear", "focus", "click", "set", "window", "start" };

    /// <summary>
    /// Ein Name, den es als Tag gibt ("tab", "shift+tab", "ctrl+a", "wait" …), egal ob die Argumente stimmen.
    /// So lässt sich {tab,x} als fehlerhaftes Tag erkennen, während {name} einfach Text ist.
    /// </summary>
    internal static bool IsTagName(string spec)
    {
        if (Commands.Contains(spec)) return true;
        var parts = spec.Split('+');
        var key = parts[^1];
        return Keys.ContainsKey(key) || parts.Length > 1 && key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]);
    }

    /// <summary>
    /// Ersetzt die Tags ausserhalb von {{ … }} durch Marker für <paramref name="form"/> und lässt Kommentare {# … } weg.
    /// </summary>
    public static string MarkTags(string text, FormBuilder form)
    {
        var result = new StringBuilder(text.Length);
        var pos = 0;
        foreach (Match match in Candidate().Matches(text))
        {
            string replacement;
            var (start, end) = (match.Index, match.Index + match.Length);
            if (match.Groups["note"].Success)
            {
                (start, end) = WholeLine(text, start, end);
                start = Math.Max(start, pos); // der Umbruch davor gehörte schon zum Kommentar davor
                replacement = "";
            }
            else if (!match.Groups[1].Success || !TryParse(match.Groups[1].Value, null, out var steps, out _))
                continue; // Skriptblock oder Text in Klammern
            else
                replacement = form.Markers(steps, new SourceSpan(match.Index, match.Length));

            result.Append(text, pos, start - pos).Append(replacement);
            form.Tags.Replaced(start, end - start, replacement.Length);
            pos = end;
        }
        return pos == 0 ? text : result.Append(text, pos, text.Length - pos).ToString();
    }

    /// <summary>
    /// Steht <paramref name="start"/> bis <paramref name="end"/> allein auf seiner Zeile (Leerzeichen zählen nicht), die
    /// ganze Zeile: samt Umbruch danach, auf der letzten Zeile samt dem Umbruch davor. Sonst die Stelle selbst. So
    /// hinterlassen Kommentare und Blöcke ohne Ausgabe keine Leerzeilen.
    /// </summary>
    internal static (int Start, int End) WholeLine(string text, int start, int end)
    {
        var lineStart = start;
        while (lineStart > 0 && text[lineStart - 1] is ' ' or '\t') lineStart--;
        if (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r')) return (start, end);
        var lineEnd = end;
        while (lineEnd < text.Length && text[lineEnd] is ' ' or '\t') lineEnd++;
        if (lineEnd < text.Length && text[lineEnd] is not ('\n' or '\r')) return (start, end);

        var afterBreak = BreakEnd(text, lineEnd);
        if (afterBreak < text.Length) return (lineStart, afterBreak);
        // Letzte Zeile (höchstens ein Umbruch folgt noch): den Umbruch davor mitnehmen
        var beforeBreak = lineStart >= 2 && text[lineStart - 2] == '\r' && text[lineStart - 1] == '\n' ? lineStart - 2
            : lineStart >= 1 ? lineStart - 1
            : lineStart;
        return (beforeBreak, afterBreak);

        static int BreakEnd(string text, int i) =>
            i < text.Length && text[i] == '\r' ? (i + 1 < text.Length && text[i + 1] == '\n' ? i + 2 : i + 1)
            : i < text.Length && text[i] == '\n' ? i + 1
            : i;
    }

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

        if (spec == "window")
            return TryParseWindow(args, out steps, out error);

        if (spec == "start")
            return TryParseStart(args, out steps, out error);

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
                if (arg is not null)
                {
                    error = "\"clear\" takes no arguments";
                    return false;
                }
                steps = [Key("ctrl+a", 0x41, [VK_CONTROL], 1), Key("del", VK_DELETE, [], 1)];
                return true;
        }

        var repeat = 1;
        if (count is { } c)
            repeat = c;
        else if (arg is not null && !int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out repeat))
            repeat = 0;
        if (repeat < 1)
        {
            error = $"the number after \"{spec}\" is how often to press it, e.g. {{{spec},3}}";
            return false;
        }
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

    internal static bool TryParseWindow(List<string> args, out IReadOnlyList<FormStep> steps, out string error)
    {
        steps = [];
        if (args.Count != 1)
        {
            error = "\"window\" needs a program or title, e.g. {window,crm}";
            return false;
        }
        if (!WindowSelector.TryParse(args[0], out var window, out error))
            return false;
        steps = [new WindowStep(window)];
        return true;
    }

    /// <summary>
    /// {start,notepad}, {start,"C:\Program Files\Notepad++\notepad++.exe","C:\temp\log.txt"}. Nur Programme (.exe, die
    /// Endung darf fehlen), keine Skripte oder Verknüpfungen. Das Fenster wird am Programmnamen erkannt.
    /// </summary>
    internal static bool TryParseStart(List<string> args, out IReadOnlyList<FormStep> steps, out string error)
    {
        steps = [];
        error = "";
        if (args.Count is < 1 or > 2 || args[0].Trim().Length == 0)
        {
            error = "\"start\" needs a program and optionally its arguments, e.g. {start,notepad} or {start,notepad,\"C:\\temp\\log.txt\"}";
            return false;
        }

        var program = args[0].Trim();
        if (program.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || program.EndsWith('\\') || program.EndsWith('/'))
        {
            error = $"\"{program}\" is not a program";
            return false;
        }
        var extension = Path.GetExtension(program);
        if (extension.Length == 0)
            program += ".exe";
        else if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            error = $"\"start\" only starts programs (.exe), not {extension} files";
            return false;
        }

        var arguments = args.Count == 2 && args[1].Length > 0 ? args[1] : null;
        steps = [new StartStep(program, arguments, new WindowSelector(Path.GetFileNameWithoutExtension(program), null, null))];
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

    public static string WindowTag(WindowSelector window)
    {
        var text = window.ToString().Replace("\"", "");
        return "{window," + (text.IndexOfAny([',', '{', '}']) >= 0 || text != text.Trim() ? "\"" + text + "\"" : text) + "}";
    }

    /// <summary>Wie der Tag geschrieben wurde, nur ".exe" beim reinen Namen weggelassen: {start,notepad}.</summary>
    public static string StartTag(StartStep start)
    {
        var program = start.Program.Contains('\\') || start.Program.Contains('/') ? start.Program : start.Program[..^4];
        var tag = "{start," + (program.IndexOfAny([',', '{', '}', ' ']) >= 0 ? "\"" + program + "\"" : program);
        if (start.Arguments is not null)
            tag += ",\"" + start.Arguments.Replace("\"", "") + "\"";
        return tag + "}";
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
    /// Eine Stelle im Text nach <see cref="StripMarkers"/> (dort gelten die <see cref="SourceSpan"/>s), zurück im Text
    /// davor, z.B. im Editor. Nur anders, wenn der Text Marker-Zeichen enthielt.
    /// </summary>
    public static int UnstrippedOffset(string original, int offset)
    {
        if (!original.Contains(MarkerStart) && !original.Contains(MarkerEnd))
            return Math.Min(offset, original.Length);
        var seen = 0;
        for (var i = 0; i < original.Length; i++)
        {
            if (original[i] is MarkerStart or MarkerEnd) continue;
            if (seen == offset) return i;
            seen++;
        }
        return original.Length;
    }

    /// <summary>
    /// Zerlegt den fertigen Text an den Markern in Schritte. Ein Umbruch ganz am Ende fällt weg,
    /// den hängen Editoren oft an und er würde nach dem letzten Feld noch eine Zeile einfügen.
    /// Umbrüche an einem Tag sind nur Layout und werden nicht gesendet: direkt nach einem Tag, direkt vor einem Tag
    /// (Leerzeichen dazwischen zählen nicht) und Leerzeilen zwischen zwei Tags. So darf jedes Tag auf einer eigenen
    /// Zeile stehen. Eine echte neue Zeile dort ist {enter} bzw. {shift+enter}.
    /// </summary>
    public static IReadOnlyList<FormStep> Split(string text, FormBuilder form) =>
        SplitWithSources(text, form).Select(s => s.Step).ToList();

    /// <summary>
    /// Wie <see cref="Split"/>, dazu woher jeder Schritt im Bausteintext stammt. Text zwischen zwei Tags ist ein
    /// Schritt, samt den Skriptblöcken, deren Ausgabe darin steht.
    /// Ein zurückgestellter Block (<see cref="DeferredStep"/>) zählt dabei nicht als Tag: Seine Ausgabe ist meist Text,
    /// Umbrüche daneben bleiben darum, wie wenn er vorher ausgewertet worden wäre.
    /// </summary>
    /// <param name="output">
    /// Die Ausgabe eines zurückgestellten Blocks statt eines ganzen Bausteins: Alle Schritte stammen von dieser Stelle,
    /// und ein Umbruch am Ende bleibt (er ist nicht das Ende der Datei).
    /// </param>
    public static IReadOnlyList<SourcedStep> SplitWithSources(string text, FormBuilder form, SourceSpan? output = null)
    {
        var steps = new List<SourcedStep>();
        var lastTextStart = 0;
        var pos = 0;
        var afterTag = false;
        while (pos < text.Length)
        {
            var start = text.IndexOf(MarkerStart, pos);
            var end = start < 0 ? -1 : text.IndexOf(MarkerEnd, start);
            if (start < 0 || end < 0) // keiner mehr, oder abgeschnitten (kann nur aus Skripten stammen)
            {
                AddText(pos, start < 0 ? text.Length : start, afterTag, beforeTag: false);
                break;
            }
            var step = int.TryParse(text.AsSpan(start + 1, end - start - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index < form.Steps.Count ? form.Steps[index] : null;
            var isTag = step is not DeferredStep;
            AddText(pos, start, afterTag, beforeTag: isTag);
            afterTag = isTag;
            if (step is not null)
                steps.Add(new(step, output ?? form.SourceOf(index)));
            pos = end + 1;
        }

        if (output is null && steps.Count > 0 && steps[^1].Step is TextStep last)
        {
            var trimmed = last.Text.EndsWith("\r\n", StringComparison.Ordinal) ? last.Text[..^2]
                : last.Text.EndsWith('\n') || last.Text.EndsWith('\r') ? last.Text[..^1]
                : last.Text;
            if (trimmed.Length == 0)
                steps.RemoveAt(steps.Count - 1);
            else if (trimmed.Length < last.Text.Length)
                steps[^1] = new(new TextStep(trimmed), form.SourceOfText(lastTextStart, lastTextStart + trimmed.Length));
        }
        return steps;

        void AddText(int from, int to, bool afterTag, bool beforeTag)
        {
            var layout = false;
            if (afterTag && LayoutBreakAfter(text, from, to) is var after && after > from)
                (from, layout) = (after, true);
            if (beforeTag && LayoutBreakBefore(text, from, to) is var before && before < to)
                (to, layout) = (before, true);
            if (to <= from) return;
            // Was zwischen zwei Tags nach dem Abziehen der Umbrüche bleibt, nur Leerzeilen oder Einrückung: auch Layout
            if (layout && text.AsSpan(from, to - from).IsWhiteSpace()) return;
            steps.Add(new(new TextStep(text[from..to]), output ?? form.SourceOfText(from, to)));
            lastTextStart = from;
        }
    }

    /// <summary>Hinter Leerzeichen und einem Umbruch am Anfang von text[from..to], sonst <paramref name="from"/>.</summary>
    static int LayoutBreakAfter(string text, int from, int to)
    {
        var i = from;
        while (i < to && text[i] is ' ' or '\t') i++;
        if (i < to && text[i] == '\r') return i + 1 < to && text[i + 1] == '\n' ? i + 2 : i + 1;
        return i < to && text[i] == '\n' ? i + 1 : from;
    }

    /// <summary>Wo ein Umbruch samt folgender Leerzeichen am Ende von text[from..to] beginnt, sonst <paramref name="to"/>.</summary>
    static int LayoutBreakBefore(string text, int from, int to)
    {
        var i = to;
        while (i > from && text[i - 1] is ' ' or '\t') i--;
        if (i > from && text[i - 1] == '\n') return i - 1 > from && text[i - 2] == '\r' ? i - 2 : i - 1;
        return i > from && text[i - 1] == '\r' ? i - 1 : to;
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
        WindowStep w => WindowTag(w.Window),
        StartStep s => StartTag(s),
        DeferredStep d => d.Text.Trim(),
        _ => "",
    };

    /// <summary>Der fertige Text mit lesbaren Tags statt Markern. Zurückgestellte Blöcke so, wie sie dastehen.</summary>
    public static string Preview(string text, FormBuilder form) =>
        HasMarkers(text) ? string.Concat(Split(text, form).Select(s => s is DeferredStep d ? d.Text : Describe(s))) : text;
}
