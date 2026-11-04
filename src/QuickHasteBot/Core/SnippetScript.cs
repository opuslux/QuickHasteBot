using System.Globalization;
using System.Text.RegularExpressions;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace QuickHasteBot.Core;

/// <summary>
/// Was ein Skript über die Umgebung wissen darf. Von aussen übergeben, damit Tests einen festen Zeitpunkt haben:
/// <paramref name="Now"/> ist, was new Date() und Date.now() liefern.
/// </summary>
/// <param name="TimeZone">Zeitzone für die Datumsmethoden, ohne Angabe die von Windows.</param>
public sealed record ScriptContext(DateTimeOffset Now, CultureInfo Culture, string Clipboard, TimeZoneInfo? TimeZone = null);

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
        ("user", "Windows user name"),
        ("guid()", "new GUID"),
    ];

    [GeneratedRegex(@"\{\{(.*?)\}\}", RegexOptions.Singleline)]
    private static partial Regex Block();

    public static bool HasBlocks(string text) => text.Contains("{{");

    /// <summary>Die Zwischenablage wird nur gelesen, wenn ein Skript sie überhaupt erwähnt.</summary>
    public static bool UsesClipboard(string text) => text.Contains("clipboard");

    /// <exception cref="SnippetScriptException">Syntax- oder Laufzeitfehler, Zeit- oder Speicherlimit in einem Block.</exception>
    public static string Render(string text, ScriptContext context)
    {
        if (!HasBlocks(text)) return text;

        using var engine = new Engine(options =>
        {
            options.TimeoutInterval(Timeout);
            options.LimitMemory(MemoryLimit);
            options.LimitRecursion(RecursionLimit);
            options.Culture = context.Culture;
            options.TimeSystem = new FixedClock(context.Now, new DefaultTimeSystem(context.TimeZone ?? TimeZoneInfo.Local, context.Culture));
        });
        RegisterHelpers(engine, context);

        var block = 0;
        return Block().Replace(text, match =>
        {
            block++;
            var code = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(code)) return "";

            try
            {
                var result = engine.Evaluate(code);
                return result.IsUndefined() || result.IsNull() ? "" : TypeConverter.ToString(result);
            }
            catch (TimeoutException ex)
            {
                throw new SnippetScriptException(block, $"Timeout after {Timeout.TotalMilliseconds:0} ms", ex);
            }
            catch (Exception ex)
            {
                throw new SnippetScriptException(block, ex.Message, ex);
            }
        });
    }

    static void RegisterHelpers(Engine engine, ScriptContext context)
    {
        // Dieselbe Ortszeit wie new Date(), damit date() und Date nicht auseinanderlaufen
        var now = TimeZoneInfo.ConvertTime(context.Now, context.TimeZone ?? TimeZoneInfo.Local);

        engine.SetValue("clipboard", context.Clipboard);
        engine.SetValue("user", Environment.UserName);

        // date(format, tage): .NET-Datumsformat in der Kultur von Windows, ohne Format das kurze Datum ("03.11.2026")
        Function("date", 2, args => now.AddDays(Number(args, 1)).ToString(Format(args, 0) ?? "d", context.Culture));

        // time(format): ohne Format Stunden und Minuten ("14:05")
        Function("time", 1, args => now.ToString(Format(args, 0) ?? "HH:mm", context.Culture));

        // isoweek(tage): Kalenderwoche nach ISO 8601, wie in Europa üblich
        Function("isoweek", 1, args => ISOWeek.GetWeekOfYear(now.AddDays(Number(args, 0)).DateTime).ToString(CultureInfo.InvariantCulture));

        Function("guid", 0, _ => Guid.NewGuid().ToString());

        void Function(string name, int length, Func<JsValue[], string> body) =>
            engine.SetValue(name, new ClrFunction(engine, name, (_, args) => body(args), length));
    }

    static string? Format(JsValue[] args, int index) =>
        Arguments.At(args, index) is { } value && !value.IsUndefined() && !value.IsNull() ? TypeConverter.ToString(value) : null;

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
