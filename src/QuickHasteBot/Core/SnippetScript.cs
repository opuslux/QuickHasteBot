using System.Globalization;
using System.Text.RegularExpressions;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace QuickHasteBot.Core;

/// <summary>
/// Was ein Skript über die Umgebung wissen darf. Von aussen übergeben, damit Tests einen festen Zeitpunkt haben:
/// <paramref name="Now"/> ist, was new Date() und Date.now() liefern.
/// </summary>
/// <param name="TimeZone">Zeitzone für die Datumsmethoden, ohne Angabe die von Windows.</param>
/// <param name="Form">Nimmt die Tasten aus key() und wait() auf. Ohne: Tasten-Tags sind ausgeschaltet, key() meldet einen Fehler.</param>
/// <param name="Run">
/// Führt run() wirklich aus (blockiert bis zum Ende oder zur Wartezeit). Ohne, z.B. in der Vorschau, startet run() nichts
/// und liefert ein leeres Ergebnis.
/// </param>
/// <param name="Selection">Der Text, der im Zielfenster markiert war (selection), leer ohne.</param>
/// <param name="Answers">
/// Was im Fenster von ask() eingegeben wurde, nach Beschriftung. Ohne, z.B. in der Vorschau, gibt ask() die Vorgabe oder
/// "[Beschriftung]".
/// </param>
/// <param name="Folder">Der Ordner des Bausteins: run() sucht relative Pfade zuerst dort.</param>
/// <param name="Message">
/// Zeigt msg() und confirm() an und wartet darauf: true für OK oder Ja, false für Nein, null für Esc oder das X bei
/// confirm(). Ohne, z.B. in der Vorschau, zeigt msg() nichts und confirm() gibt true.
/// </param>
public sealed record ScriptContext(DateTimeOffset Now, CultureInfo Culture, string Clipboard, TimeZoneInfo? TimeZone = null,
    FormBuilder? Form = null, Func<RunRequest, RunResult>? Run = null, string Selection = "",
    IReadOnlyDictionary<string, string>? Answers = null, string? Folder = null, Func<MessageRequest, bool?>? Message = null);

/// <summary>Das Symbol einer Meldung aus msg() oder confirm().</summary>
public enum MessageIcon { None, Info, Question, Warning, Error }

/// <summary>Eine Meldung aus msg() oder confirm().</summary>
/// <param name="Title">Ohne Angabe "QuickHasteBot".</param>
/// <param name="CloseSeconds">Nur msg(): schliesst sich nach so vielen Sekunden von selbst, 0 nie.</param>
/// <param name="Confirm">confirm(): Ja und Nein statt OK.</param>
/// <param name="AfterSeconds">Nur msg(): erscheint erst nach so vielen Sekunden, im Hintergrund; der Baustein wartet nicht
/// darauf. 0 sofort.</param>
public sealed record MessageRequest(string Text, string? Title, MessageIcon Icon, int CloseSeconds, bool Confirm, int AfterSeconds = 0);

/// <summary>Eine Frage aus ask(): die Beschriftung und die Vorgabe, wie sie im Baustein stehen.</summary>
public sealed record Question(string Label, string Default);

/// <summary>Das Fenster von ask() mit den Optionen der Aufrufe: null heisst Standard.</summary>
/// <param name="Button">Statt "Einfügen".</param>
/// <param name="Title">Statt des Namens des Bausteins in der Titelleiste.</param>
/// <param name="Text">Statt "«Name» braucht vor dem Einfügen noch ein paar Angaben:".</param>
public sealed record AskWindowOptions(string? Button = null, string? Title = null, string? Text = null);

/// <summary>Was run() starten soll. <paramref name="WaitSeconds"/> 0: nur starten, nicht warten.</summary>
/// <param name="Directory">Der Arbeitsordner („Ausführen in“), ohne Angabe der Ordner des Skripts.</param>
/// <param name="SnippetFolder">Der Ordner des Bausteins: ein relativer Pfad, der dort liegt, wird dort gestartet.</param>
public sealed record RunRequest(string File, string? Arguments, int WaitSeconds, bool Hidden, string? Directory = null,
    string? SnippetFolder = null);

/// <param name="Code">Der Rückgabewert, null, wenn nicht gewartet wurde.</param>
public sealed record RunResult(int? Code, string Out, string Err);

/// <summary>run() konnte nicht starten, oder das Skript war nach der Wartezeit nicht fertig: Der Baustein bricht ab.</summary>
public sealed class RunFailedException(string message) : Exception(message);

/// <param name="block">Nummer des fehlerhaften {{ … }}-Blocks, ab 1 gezählt.</param>
public sealed class SnippetScriptException(int block, string message, Exception inner) : Exception(message, inner)
{
    public int Block { get; } = block;
}

/// <summary>
/// Wertet {{ … }} in Textbausteinen als JavaScript aus (Jint, ohne Zugriff auf .NET). Standard-JavaScript plus
/// ein paar Helfer für das, was JavaScript hier nicht selbst kann (siehe <see cref="Helpers"/>).
/// Alle Blöcke eines Bausteins laufen in derselben Engine, Variablen aus einem Block gelten in den folgenden.
/// Ein "}}" im Code beendet den Block, ein wörtliches "{{" schreibt man als {{ "{{" }}.
/// </summary>
public static partial class SnippetScript
{
    /// <summary>Gilt pro Block, damit eine Endlosschleife das Einfügen nicht blockiert.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    const long MemoryLimit = 32 * 1024 * 1024;
    const int RecursionLimit = 64;

    /// <summary>
    /// Die Helfer aus <see cref="RegisterHelpers"/>, so auch im KI-Prompt (<see cref="ScriptPrompt"/>).
    /// Ein Test prüft, dass jeder davon in der Engine existiert.
    /// </summary>
    public static readonly IReadOnlyList<(string Signature, string Description)> Helpers =
    [
        ("date(format?, days?)", "today, shifted by days (negative = past); .NET date format, default short date"),
        ("time(format?)", ".NET time format, default HH:mm"),
        ("isoweek(days?)", "ISO week number"),
        ("clipboard", "clipboard text"),
        ("selection", "the text selected in the target window before pasting, '' if none"),
        ("ask(label, default?, {button, title, text}?)", "asks the user before pasting, all ask() of a snippet in one window; label and options must be fixed texts"),
        ("user", "Windows user name"),
        ("guid()", "new GUID"),
        ("key(name, count?)", "presses a key, e.g. key('tab'), key('down', 3); put it and the next ones in the output"),
        ("wait(ms)", "pause"),
        ("focus(el)", "focus a field: '#id', 'name=Label', '.class'"),
        ("click(el)", "click a button or link"),
        ("set(el, value)", "fill a field or dropdown, checkbox 'on'/'off'"),
        ("dblclick(el)", "double-click an element, e.g. a list entry"),
        ("rclick(el)", "right-click an element; click('name=…') then picks from its context menu"),
        ("window(app)", "bring a program to the front"),
        ("run(file, args?, {wait, hidden, dir, args}?)", "runs a script or program right here and waits (wait in seconds, default 60; dir = working folder, default the script's); returns {code, out, err}, as text its output"),
        ("msg(text, {title, icon, close, after}?)", "shows a message box and waits for OK; icon 'info', 'warning', 'error' or 'none'; close = closes itself after that many seconds; after = shows it only after that many seconds in the background, the snippet doesn't wait (e.g. a timer)"),
        ("confirm(text, {title, icon}?)", "asks Yes or No in a message box, true for Yes; Esc stops the snippet"),
    ];

    /// <summary>So lange schliesst sich eine Meldung aus msg() höchstens von selbst, in Sekunden.</summary>
    public const int MaxMessageClose = 600;

    /// <summary>So lange wartet eine Meldung aus msg() mit after höchstens, in Sekunden: ein Tag.</summary>
    public const int MaxMessageAfter = 86_400;

    /// <summary>So lange wartet run() ohne Angabe, in Sekunden.</summary>
    public const int DefaultRunWait = 60;

    [GeneratedRegex(@"\{\{(.*?)\}\}", RegexOptions.Singleline)]
    internal static partial Regex Block();

    [GeneratedRegex(@"(?<![\w$.])(?:run|msg|confirm)\s*\(")]
    private static partial Regex WaitingCall();

    /// <summary>
    /// Ob der Code eines Blocks run(), msg() oder confirm() aufruft, also auf etwas ausserhalb wartet: Solche Blöcke laufen
    /// an ihrer Stelle. Ein falscher Treffer schadet nicht, der Block läuft dann nur später.
    /// </summary>
    public static bool Waits(string code) => WaitingCall().IsMatch(code);

    [GeneratedRegex(@"(?<![\w$.])run\s*\(")]
    private static partial Regex RunCall();

    [GeneratedRegex(@"(?<![\w$.])(?:msg|confirm)\s*\(")]
    private static partial Regex MessageCall();

    /// <summary>Ob ein Block im Text run() aufruft: Die Vorschau startet es nicht.</summary>
    public static bool UsesRun(string text) => Block().Matches(text).Any(m => RunCall().IsMatch(m.Groups[1].Value));

    /// <summary>Ob ein Block im Text msg() oder confirm() aufruft: Die Vorschau zeigt sie nicht.</summary>
    public static bool UsesMessages(string text) => Block().Matches(text).Any(m => MessageCall().IsMatch(m.Groups[1].Value));


    public static bool HasBlocks(string text) => text.Contains("{{");

    /// <summary>Die Zwischenablage wird nur gelesen, wenn ein Skript sie überhaupt erwähnt.</summary>
    public static bool UsesClipboard(string text) => text.Contains("clipboard");

    [GeneratedRegex(@"(?<![\w$.])selection\b")]
    private static partial Regex SelectionWord();

    /// <summary>
    /// Ob ein Skript selection erwähnt: Nur dann wird vor dem Einfügen Strg+C im Zielfenster gedrückt. Text ausserhalb
    /// der Blöcke zählt nicht, das Wort kommt in Mails vor.
    /// </summary>
    public static bool UsesSelection(string text) =>
        text.Contains("selection") && Block().Matches(text).Any(m => SelectionWord().IsMatch(m.Groups[1].Value));

    // ask('Kunde'), ask("Kunde", 'Muster'), ask('Kunde', 'Muster', { button: 'Weiter' }) oder ask('Kunde', { title: '…' }):
    // nur feste Texte, damit alle Fragen und das Fenster vor dem ersten Block feststehen
    [GeneratedRegex("""(?<![\w$.])ask\s*\(\s*(?:'(?<l>(?:\\.|[^'\\\r\n])*)'|"(?<l>(?:\\.|[^"\\\r\n])*)")\s*(?:,\s*(?:'(?<d>(?:\\.|[^'\\\r\n])*)'|"(?<d>(?:\\.|[^"\\\r\n])*)")\s*)?(?:,\s*\{(?<o>\s*(?:['"]?\w+['"]?\s*:\s*(?:'(?:\\.|[^'\\\r\n])*'|"(?:\\.|[^"\\\r\n])*")\s*(?:,\s*['"]?\w+['"]?\s*:\s*(?:'(?:\\.|[^'\\\r\n])*'|"(?:\\.|[^"\\\r\n])*")\s*)*,?\s*)?)\}\s*)?\)""")]
    private static partial Regex AskCall();

    // Eine Option im Fenster von ask(): button: 'Weiter'
    [GeneratedRegex("""['"]?(?<k>\w+)['"]?\s*:\s*(?:'(?<v>(?:\\.|[^'\\\r\n])*)'|"(?<v>(?:\\.|[^"\\\r\n])*)")""")]
    private static partial Regex AskOption();

    /// <summary>Die Optionen, die das Fenster von ask() kennt.</summary>
    static readonly string[] AskOptionNames = ["button", "title", "text"];

    /// <summary>
    /// Wie das Fenster von ask() aussehen soll: Text des Knopfs, Titel und Zeile oben aus den Optionen der ask() eines
    /// Bausteins, je die erste Angabe. Ohne Angabe null, dann gilt der Standard.
    /// </summary>
    public static AskWindowOptions AskWindow(string text)
    {
        var options = new Dictionary<string, string>();
        if (text.Contains("ask"))
        {
            foreach (Match block in Block().Matches(text))
            {
                foreach (Match call in AskCall().Matches(block.Groups[1].Value))
                {
                    foreach (Match option in AskOption().Matches(call.Groups["o"].Value))
                    {
                        var value = Unescape(option.Groups["v"].Value);
                        if (value.Trim().Length > 0)
                            options.TryAdd(option.Groups["k"].Value, value);
                    }
                }
            }
        }
        return new(options.GetValueOrDefault("button"), options.GetValueOrDefault("title"), options.GetValueOrDefault("text"));
    }

    /// <summary>Ob ein Skript ask() aufruft: Nur dann kommt vor dem Einfügen das Fenster mit den Fragen.</summary>
    public static bool UsesAsk(string text) => text.Contains("ask") && Questions(text).Count > 0;

    /// <summary>
    /// Die Fragen aus ask() in den Blöcken, in ihrer Reihenfolge, jede Beschriftung einmal (die erste Vorgabe gilt). Ein
    /// ask() mit einer Variablen statt eines festen Texts fehlt hier und meldet beim Auswerten einen Fehler.
    /// </summary>
    public static IReadOnlyList<Question> Questions(string text)
    {
        if (!text.Contains("ask")) return [];
        var questions = new List<Question>();
        foreach (Match block in Block().Matches(text))
        {
            foreach (Match call in AskCall().Matches(block.Groups[1].Value))
            {
                var label = Unescape(call.Groups["l"].Value);
                if (label.Length > 0 && !questions.Any(q => q.Label == label))
                    questions.Add(new(label, Unescape(call.Groups["d"].Value)));
            }
        }
        return questions;
    }

    /// <summary>Die üblichen Escapes in einer JavaScript-Zeichenkette: \' \" \\ \n \t.</summary>
    static string Unescape(string text) =>
        !text.Contains('\\') ? text : Regex.Replace(text, @"\\(.)", m => m.Groups[1].Value switch { "n" => "\n", "t" => "\t", var c => c });

    /// <exception cref="SnippetScriptException">Syntax- oder Laufzeitfehler, Zeit- oder Speicherlimit in einem Block.</exception>
    public static string Render(string text, ScriptContext context)
    {
        if (!HasBlocks(text)) return text;
        using var session = new ScriptSession(context);
        return session.Render(text);
    }

    internal static Engine CreateEngine(ScriptContext context)
    {
        var engine = new Engine(options =>
        {
            options.TimeoutInterval(Timeout);
            options.LimitMemory(MemoryLimit);
            options.LimitRecursion(RecursionLimit);
            options.Culture = context.Culture;
            options.TimeSystem = new FixedClock(context.Now, new DefaultTimeSystem(context.TimeZone ?? TimeZoneInfo.Local, context.Culture));
        });
        RegisterHelpers(engine, context);
        return engine;
    }

    /// <summary>Ein Block, wie ihn der Baustein ausgibt: undefined und null geben nichts aus.</summary>
    internal static string Evaluate(Engine engine, string code, int block)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        try
        {
            var result = engine.Evaluate(code);
            return result.IsUndefined() || result.IsNull() ? "" : TypeConverter.ToString(result);
        }
        catch (OperationCanceledException)
        {
            throw; // Esc beim Warten auf run(): wie Esc sonst, kein Fehler
        }
        catch (TimeoutException ex)
        {
            throw new SnippetScriptException(block, $"Timeout after {Timeout.TotalMilliseconds:0} ms", ex);
        }
        catch (Exception ex)
        {
            throw new SnippetScriptException(block, ex.Message, ex);
        }
    }

    static void RegisterHelpers(Engine engine, ScriptContext context)
    {
        // Dieselbe Ortszeit wie new Date(), damit date() und Date nicht auseinanderlaufen
        var now = TimeZoneInfo.ConvertTime(context.Now, context.TimeZone ?? TimeZoneInfo.Local);

        // Marker aus der Zwischenablage weg, sonst könnte kopierter Text Tasten auslösen
        engine.SetValue("clipboard", KeyTags.StripMarkers(context.Clipboard));
        engine.SetValue("selection", KeyTags.StripMarkers(context.Selection));
        engine.SetValue("user", Environment.UserName);

        // date(format, tage): .NET-Datumsformat in der Kultur von Windows, ohne Format das kurze Datum ("03.11.2026")
        Function("date", 2, args => now.AddDays(Number(args, 1)).ToString(Format(args, 0) ?? "d", context.Culture));

        // time(format): ohne Format Stunden und Minuten ("14:05")
        Function("time", 1, args => now.ToString(Format(args, 0) ?? "HH:mm", context.Culture));

        // isoweek(tage): Kalenderwoche nach ISO 8601, wie in Europa üblich
        Function("isoweek", 1, args => ISOWeek.GetWeekOfYear(now.AddDays(Number(args, 0)).DateTime).ToString(CultureInfo.InvariantCulture));

        Function("guid", 0, _ => Guid.NewGuid().ToString());

        // ask("Kunde", "Muster"): was im Fenster vor dem Einfügen eingegeben wurde. Ohne Fenster (Vorschau) die Vorgabe.
        Function("ask", 3, args => Ask(context, args));

        // key("tab"), key("shift+tab"), key("down", 3), key("wait,500"): dieselbe Syntax wie die Tags im Text.
        // Liefert einen unsichtbaren Marker, der im Ergebnis stehen muss, damit die Taste gedrückt wird.
        Function("key", 2, args => FormMarker(context, Format(args, 0) ?? "", Arguments.At(args, 1) is { } n && !n.IsUndefined() ? (int)TypeConverter.ToNumber(n) : null));

        // wait(500): Pause in Millisekunden
        Function("wait", 1, args => FormMarker(context, "wait", (int)Number(args, 0)));

        // focus("#email"), click("name=Senden"), set("#land", "Österreich"): Elemente per UI Automation
        Function("focus", 1, args => ElementMarker(context, "focus", [Format(args, 0) ?? ""]));
        Function("click", 1, args => ElementMarker(context, "click", [Format(args, 0) ?? ""]));
        Function("set", 2, args => ElementMarker(context, "set", [Format(args, 0) ?? "", Format(args, 1) ?? ""]));
        Function("dblclick", 1, args => ElementMarker(context, "dblclick", [Format(args, 0) ?? ""]));
        Function("rclick", 1, args => ElementMarker(context, "rclick", [Format(args, 0) ?? ""]));
        Function("window", 1, args => ElementMarker(context, "window", [Format(args, 0) ?? ""]));

        // run(datei, argumente, { wait, hidden, dir, args }): startet ein Skript oder Programm und wartet darauf. Das Ergebnis
        // { code, out, err } gibt als Text seine Ausgabe her, so tippt {{ run('nummer.bat') }} direkt die Ausgabe.
        engine.SetValue("__run", new ClrFunction(engine, "__run", (_, args) => Run(engine, context, args), 3));
        engine.Execute("function run(file, args, options) { const r = __run(file, args, options); r.toString = function () { return this.out; }; return r; }");

        // msg("Fertig", { title, icon, close }): Meldung, wartet auf OK. Gibt nichts aus, getippt wird also nichts.
        engine.SetValue("msg", new ClrFunction(engine, "msg", (_, args) =>
        {
            Message(engine, context, args, confirm: false);
            return JsValue.Undefined;
        }, 2));

        // confirm("Senden?", { title, icon }): Ja oder Nein, true bei Ja. Esc bricht den Baustein ab.
        engine.SetValue("confirm", new ClrFunction(engine, "confirm", (_, args) =>
            Message(engine, context, args, confirm: true) ? JsBoolean.True : JsBoolean.False, 2));

        void Function(string name, int length, Func<JsValue[], string> body) =>
            engine.SetValue(name, new ClrFunction(engine, name, (_, args) => body(args), length));
    }

    static string Ask(ScriptContext context, JsValue[] args)
    {
        var label = Format(args, 0);
        if (string.IsNullOrEmpty(label))
            throw new ArgumentException("ask needs a label, e.g. ask('Customer')");
        // Die Optionen als zweites oder drittes Argument: ask('Kunde', { … }) oder ask('Kunde', 'Muster', { … })
        var second = Arguments.At(args, 1);
        var fallback = second is ObjectInstance ? null : Format(args, 1);
        var options = second is ObjectInstance ? second : Arguments.At(args, 2);
        if (options is ObjectInstance o)
        {
            foreach (var key in o.GetOwnPropertyKeys())
            {
                if (!AskOptionNames.Contains(key.ToString()))
                    throw new ArgumentException($"ask doesn't know the option '{key}', use {string.Join(", ", AskOptionNames)}");
            }
        }
        else if (!options.IsUndefined())
        {
            throw new ArgumentException("the last argument of ask holds the options, e.g. ask('Question', { button: 'Ask' })");
        }

        if (context.Answers is null)
            return KeyTags.StripMarkers(string.IsNullOrEmpty(fallback) ? "[" + label + "]" : fallback);
        if (!context.Answers.TryGetValue(label, out var answer))
            throw new ArgumentException($"ask() needs fixed texts in quotes, e.g. ask('{label}') or ask('{label}', {{ button: 'OK' }}), not variables");
        // Marker weg: Eingetipptes soll keine Tasten auslösen
        return KeyTags.StripMarkers(answer);
    }

    static bool Message(Engine engine, ScriptContext context, JsValue[] args, bool confirm)
    {
        var name = confirm ? "confirm" : "msg";
        var text = Format(args, 0);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException($"{name} needs a text, e.g. {name}('{(confirm ? "Send now?" : "Done")}')");
        string? title = null;
        var icon = confirm ? MessageIcon.Question : MessageIcon.Info;
        var close = 0;
        var after = 0;
        if (Arguments.At(args, 1) is ObjectInstance o)
        {
            title = Text(o.Get("title"));
            if (Text(o.Get("icon")) is { } named)
            {
                icon = named.ToLowerInvariant() switch
                {
                    "info" or "information" => MessageIcon.Info,
                    "question" => MessageIcon.Question,
                    "warning" => MessageIcon.Warning,
                    "error" => MessageIcon.Error,
                    "none" => MessageIcon.None,
                    _ => throw new ArgumentException($"{name} doesn't know the icon '{named}', use 'info', 'warning', 'error' or 'none'"),
                };
            }
            if (o.Get("close") is { } c && !c.IsUndefined())
            {
                if (confirm)
                    throw new ArgumentException("confirm waits for an answer, close only works with msg()");
                close = Math.Clamp((int)Math.Round(TypeConverter.ToNumber(c)), 0, MaxMessageClose);
            }
            if (o.Get("after") is { } a && !a.IsUndefined())
            {
                if (confirm)
                    throw new ArgumentException("confirm waits for an answer, after only works with msg()");
                var seconds = TypeConverter.ToNumber(a);
                after = double.IsNaN(seconds) ? 0 : (int)Math.Clamp(Math.Round(seconds), 0, MaxMessageAfter);
            }
        }
        else if (!Arguments.At(args, 1).IsUndefined())
        {
            throw new ArgumentException($"the second argument of {name} holds the options, e.g. {name}('…', {{ title: 'Order' }})");
        }

        if (context.Message is null) return true;
        var answer = context.Message(new MessageRequest(KeyTags.StripMarkers(text), title is null ? null : KeyTags.StripMarkers(title),
            icon, close, confirm, after));
        // Das Warten zählt nicht zur Zeitgrenze eines Blocks, wie bei run()
        engine.Constraints.Reset();
        // Esc oder das X bei confirm(): wie Esc sonst, der Baustein hört auf
        return answer ?? throw new OperationCanceledException();
    }

    static JsValue Run(Engine engine, ScriptContext context, JsValue[] args)

    {
        var file = Format(args, 0);
        if (string.IsNullOrWhiteSpace(file))
            throw new ArgumentException("run needs a file, e.g. run('d:/scripts/number.bat')");
        // Zweites Argument: die Argumente als Text, oder gleich die Optionen
        var second = Arguments.At(args, 1);
        var options = second is ObjectInstance ? second : Arguments.At(args, 2);
        var arguments = second is ObjectInstance ? null : Format(args, 1);
        var wait = DefaultRunWait;
        var hidden = false;
        string? directory = null;
        if (options is ObjectInstance o)
        {
            if (o.Get("wait") is { } w && !w.IsUndefined())
                wait = Math.Max(0, (int)Math.Round(TypeConverter.ToNumber(w)));
            hidden = TypeConverter.ToBoolean(o.Get("hidden"));
            if (Text(o.Get("args")) is { } named)
            {
                if (!string.IsNullOrEmpty(arguments))
                    throw new ArgumentException("run got its arguments twice, as text and as args; use one of them");
                arguments = named;
            }
            directory = Text(o.Get("dir"));
        }

        var result = context.Run?.Invoke(new RunRequest(file, arguments, wait, hidden, directory, context.Folder)) ?? new RunResult(0, "", "");
        // Das Warten zählt nicht zur Zeitgrenze eines Blocks, die gilt nur für JavaScript
        engine.Constraints.Reset();
        return JsObject.CreateFromEntries(engine,
        [
            new("code", result.Code is { } code ? JsNumber.Create(code) : JsValue.Null),
            new("out", new JsString(result.Out)),
            new("err", new JsString(result.Err)),
        ]);
    }

    static string FormMarker(ScriptContext context, string spec, int? count)
    {
        if (context.Form is null)
            throw new InvalidOperationException("key tags are disabled in the settings");
        if (!KeyTags.TryParse(spec, count, out var steps, out var error))
            throw new ArgumentException(error);
        // Programme startet nur, was wörtlich im Baustein steht, nie Skript-Ausgabe oder die Zwischenablage
        if (steps.Any(s => s is StartStep or OpenUrlStep))
            throw new ArgumentException("scripts can't start programs, write {start,…} in the snippet text");
        if (steps.Any(s => s is IncludeStep or HtmlMarkStep))
            throw new ArgumentException("{include} and {html} only work in the snippet text, not in key()");
        // Ein Block aus Skript-Ausgabe könnte die {if}-Blöcke des Bausteins durcheinanderbringen, und im Skript gibt es if
        if (steps.Any(s => s is IfStep or ElseStep or EndIfStep))
            throw new ArgumentException("{if}, {else} and {/if} only work in the snippet text, in scripts use if (…)");
        return context.Form.Markers(steps);
    }

    static string ElementMarker(ScriptContext context, string spec, List<string> args)
    {
        if (context.Form is null)
            throw new InvalidOperationException("key tags are disabled in the settings");
        var ok = spec == "window"
            ? KeyTags.TryParseWindow(args, out var steps, out var error)
            : KeyTags.TryParseElement(spec, args, out steps, out error);
        if (!ok)
            throw new ArgumentException(error);
        return context.Form.Markers(steps);
    }

    static string? Format(JsValue[] args, int index) =>
        Arguments.At(args, index) is { } value && !value.IsUndefined() && !value.IsNull() ? TypeConverter.ToString(value) : null;

    /// <summary>Eine Option als Text, null wenn sie fehlt oder leer ist.</summary>
    static string? Text(JsValue value) =>
        value.IsUndefined() || value.IsNull() || TypeConverter.ToString(value) is not { Length: > 0 } text ? null : text;

    static double Number(JsValue[] args, int index) =>
        Arguments.At(args, index) is { } value && !value.IsUndefined() ? TypeConverter.ToNumber(value) : 0;

    /// <summary>Jints Uhr, aber mit festem "jetzt": alle Blöcke eines Bausteins sehen denselben Zeitpunkt.</summary>
    sealed class FixedClock(DateTimeOffset now, ITimeSystem inner) : ITimeSystem
    {
        public DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public TimeZoneInfo DefaultTimeZone => inner.DefaultTimeZone;

        public bool TryParse(string date, out long epochMilliseconds) => inner.TryParse(date, out epochMilliseconds);

        public TimeSpan GetUtcOffset(long epochMilliseconds) => inner.GetUtcOffset(epochMilliseconds);
    }
}
