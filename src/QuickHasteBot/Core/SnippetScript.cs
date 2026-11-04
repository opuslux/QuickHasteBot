using System.Globalization;
using System.Text.RegularExpressions;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace QuickHasteBot.Core;

/// <summary>Was ein Skript über die Umgebung wissen darf. Von aussen übergeben, damit Tests einen festen Zeitpunkt haben.</summary>
public sealed record ScriptContext(DateTimeOffset Now, CultureInfo Culture, string Clipboard);

/// <param name="block">Nummer des fehlerhaften {{ … }}-Blocks, ab 1 gezählt.</param>
public sealed class SnippetScriptException(int block, string message, Exception inner) : Exception(message, inner)
{
    public int Block { get; } = block;
}

/// <summary>
/// Wertet {{ … }} in Textbausteinen als JavaScript aus (Jint, ohne Zugriff auf .NET).
/// Alle Blöcke eines Bausteins laufen in derselben Engine, Variablen aus einem Block gelten in den folgenden.
/// Ein "}}" im Code beendet den Block, ein wörtliches "{{" schreibt man als {{ "{{" }}.
/// </summary>
public static partial class SnippetScript
{
    /// <summary>Gilt pro Block, damit eine Endlosschleife das Einfügen nicht blockiert.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    const long MemoryLimit = 32 * 1024 * 1024;
    const int RecursionLimit = 64;

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
        engine.SetValue("clipboard", context.Clipboard);
        engine.SetValue("user", Environment.UserName);

        // date(format, tage): .NET-Datumsformat in der Kultur von Windows, ohne Format das kurze Datum ("03.11.2026")
        Function("date", 2, args =>
            context.Now.AddDays(Number(args, 1)).ToString(Format(args, 0) ?? "d", context.Culture));

        // time(format): ohne Format die kurze Uhrzeit ("14:05")
        Function("time", 1, args => context.Now.ToString(Format(args, 0) ?? "t", context.Culture));

        Function("guid", 0, _ => Guid.NewGuid().ToString());

        void Function(string name, int length, Func<JsValue[], string> body) =>
            engine.SetValue(name, new ClrFunction(engine, name, (_, args) => body(args), length));
    }

    static string? Format(JsValue[] args, int index) =>
        Arguments.At(args, index) is { } value && !value.IsUndefined() && !value.IsNull() ? TypeConverter.ToString(value) : null;

    static double Number(JsValue[] args, int index) =>
        Arguments.At(args, index) is { } value && !value.IsUndefined() ? TypeConverter.ToNumber(value) : 0;
}
