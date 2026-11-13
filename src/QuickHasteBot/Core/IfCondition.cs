using System.Globalization;

namespace QuickHasteBot.Core;

/// <summary>
/// Was {if,…} und {elseif,…} prüfen, erst wenn der Lauf dort ankommt: ob ein Element da und sichtbar ist
/// ({if,name=Senden}), ob ein Fenster offen ist ({if,process=outlook}, {if,window=title=*Entwurf*}) oder ob ein
/// JavaScript-Ausdruck stimmt ({if,js="r.code == 0"}). "not" davor kehrt die Bedingung um.
/// </summary>
/// <param name="Not">{if,not,…}: gilt, wenn das Element oder Fenster nicht da ist bzw. der Ausdruck nicht stimmt.</param>
/// <param name="Element">Wie bei {click}, auch mit window= (dann in diesem Fenster).</param>
/// <param name="Window">Nur das Fenster, ohne Element.</param>
/// <param name="Script">Der Ausdruck aus js="…", ausgewertet in der Engine des Bausteins.</param>
/// <param name="Wait">So lange wird auf Element oder Fenster gewartet, bevor die Bedingung als nicht erfüllt gilt (ms).</param>
public sealed record IfCondition(bool Not, ElementSelector? Element, WindowSelector? Window, string? Script, int Wait)
{
    public const int DefaultWait = 1_000;

    /// <param name="spec">"if" oder "elseif", für die Fehlermeldung.</param>
    public static bool TryParse(string spec, List<string> args, out IfCondition condition, out string error)
    {
        condition = new IfCondition(false, null, null, null, DefaultWait);
        error = $"\"{spec}\" needs a condition, e.g. {{{spec},name=Send}}, {{{spec},process=outlook}} or {{{spec},js=\"x == 1\"}}";

        var rest = args.Select(a => a.Trim()).ToList();
        var not = rest.Count > 0 && rest[0].Equals("not", StringComparison.OrdinalIgnoreCase);
        if (not) rest.RemoveAt(0);

        int? wait = null;
        if (rest.Count > 1 && rest[^1].StartsWith("wait=", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(rest[^1].AsSpan(5).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
            {
                error = "wait= needs milliseconds, e.g. wait=2000";
                return false;
            }
            wait = Math.Min(ms, KeyTags.MaxWait);
            rest.RemoveAt(rest.Count - 1);
        }
        if (rest.Count != 1 || rest[0].Length == 0)
        {
            if (rest.Count > 1)
                error = $"\"{spec}\" checks one thing, put text with commas in \"…\"";
            return false;
        }

        var arg = rest[0];
        if (arg.StartsWith("js=", StringComparison.OrdinalIgnoreCase))
        {
            var script = arg[3..].Trim();
            if (script.Length == 0)
            {
                error = "js= needs an expression, e.g. js=\"x == 1\"";
                return false;
            }
            if (wait is not null)
            {
                error = "wait= only works with elements and windows, js= is checked once";
                return false;
            }
            condition = new IfCondition(not, null, null, script, 0);
            return true;
        }
        if (WindowOnly(arg) is { } windowText)
        {
            if (!WindowSelector.TryParse(windowText, out var window, out error))
                return false;
            condition = new IfCondition(not, null, window, null, wait ?? DefaultWait);
            return true;
        }
        if (!ElementSelector.TryParse(arg, out var element, out error))
            return false;
        condition = new IfCondition(not, element, null, null, wait ?? DefaultWait);
        return true;
    }

    /// <summary>
    /// "process=…", "title=…" oder "window=…" allein (ohne ; und " > "): nur das Fenster. Der Text dafür, wie ihn
    /// <see cref="WindowSelector.TryParse"/> nimmt. Sonst null, dann ist es ein Element.
    /// </summary>
    static string? WindowOnly(string arg)
    {
        if (arg.Contains(';') || arg.Contains(ElementSelector.ContainerSeparator, StringComparison.Ordinal)) return null;
        var eq = arg.IndexOf('=');
        if (eq < 0) return null;
        return arg[..eq].Trim().ToLowerInvariant() switch
        {
            "process" or "title" => arg,
            "window" => arg[(eq + 1)..],
            _ => null,
        };
    }

    /// <summary>Die Argumente, wie sie im Tag stehen: "not,name=Senden,wait=2000".</summary>
    public override string ToString()
    {
        var what = Script is not null ? "js=\"" + Script + "\""
            : Window is not null ? (Window.Any is not null ? "window=" + Window : Window.ToString())
            : Element!.ToString();
        if (Script is null && what.IndexOfAny([',', '{', '}', '"']) >= 0)
            what = "\"" + what.Replace("\"", "") + "\"";
        return (Not ? "not," : "") + what + (Script is null && Wait != DefaultWait ? ",wait=" + Wait.ToString(CultureInfo.InvariantCulture) : "");
    }
}
