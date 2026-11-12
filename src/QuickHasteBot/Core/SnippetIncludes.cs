using System.Text;
using System.Text.RegularExpressions;

namespace QuickHasteBot.Core;

/// <summary>Warum ein {include,…} nicht ging.</summary>
public enum IncludeProblem
{
    /// <summary>Kein Textbaustein unter diesem Namen.</summary>
    NotFound,
    /// <summary>Ein Baustein bindet (über andere) sich selbst ein.</summary>
    Cycle,
    /// <summary>Mehr als <see cref="SnippetIncludes.MaxDepth"/> Ebenen.</summary>
    TooDeep,
    /// <summary>Die Datei liess sich nicht lesen.</summary>
    Unreadable,
}

/// <param name="Detail">Der Name aus dem Tag, beim Zyklus die Kette "A → B → A", bei Unreadable die Systemmeldung.</param>
public sealed class IncludeException(IncludeProblem problem, string detail) : Exception(detail)
{
    public IncludeProblem Problem { get; } = problem;

    public string Detail { get; } = detail;
}

/// <summary>
/// {include,Ordner/Name}: ersetzt das Tag durch den Text eines anderen Bausteins, bevor Tags und Skripte gelesen werden.
/// So gelten dessen Tags und Skripte wie im Baustein selbst, z.B. eine Signatur, die nur an einer Stelle gepflegt wird.
/// Kein Skript-Helfer, weil Skript-Ausgaben nie als Tags gelesen werden.
/// </summary>
public static class SnippetIncludes
{
    public const int MaxDepth = 10;

    /// <summary>Ein Baustein, wie ihn <see cref="Expand"/> braucht: sein Pfad (eindeutig, für Zyklen) und sein Text.</summary>
    public sealed record Included(string Key, string Text);

    /// <summary>Ob der Text {include,…} enthalten könnte. Schnell, ein falscher Treffer schadet nicht.</summary>
    public static bool HasIncludes(string text) => text.Contains("{include", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ersetzt jedes {include,…} ausserhalb von Skripten und {html}-Blöcken durch den Text des Bausteins, auch
    /// verschachtelt. Ein Umbruch am Ende des eingebundenen Texts fällt weg, wie am Ende eines Bausteins.
    /// </summary>
    /// <param name="resolve">Name aus dem Tag → Baustein, null wenn es keinen gibt.</param>
    /// <param name="map">Zeichnet die Ersetzungen auf der obersten Ebene auf (Herkunft im Schritt-für-Schritt-Lauf).</param>
    /// <param name="self">Schlüssel des Bausteins, der eingebunden wird (gegen Zyklen), null für ungespeicherten Text.</param>
    /// <exception cref="IncludeException"/>
    public static string Expand(string text, Func<string, Included?> resolve, FormBuilder? map = null, string? self = null) =>
        Expand(text, resolve, map, self is null ? [] : [self], []);

    static string Expand(string text, Func<string, Included?> resolve, FormBuilder? map, List<string> chain, List<string> names)
    {
        if (!HasIncludes(text)) return text;

        var html = KeyTags.HtmlPairs(text);
        var htmlClose = -1;
        var result = new StringBuilder(text.Length);
        var pos = 0;
        foreach (Match match in KeyTags.Candidate().Matches(text))
        {
            if (match.Groups["note"].Success || !match.Groups[1].Success) continue;
            if (htmlClose >= 0)
            {
                if (match.Index >= htmlClose) htmlClose = -1;
                continue;
            }
            if (html.TryGetValue(match.Index, out var close))
            {
                htmlClose = close.CloseStart;
                continue;
            }
            if (!KeyTags.TryParse(match.Groups[1].Value, null, out var steps, out _) || steps is not [IncludeStep include])
                continue;

            if (chain.Count + 1 > MaxDepth)
                throw new IncludeException(IncludeProblem.TooDeep, include.Path);
            var target = resolve(include.Path) ?? throw new IncludeException(IncludeProblem.NotFound, include.Path);
            if (chain.Contains(target.Key, StringComparer.OrdinalIgnoreCase))
                throw new IncludeException(IncludeProblem.Cycle, string.Join(" → ", names.Append(include.Path)));

            chain.Add(target.Key);
            names.Add(include.Path);
            var expanded = TrimFinalBreak(Expand(KeyTags.StripMarkers(target.Text), resolve, null, chain, names));
            chain.RemoveAt(chain.Count - 1);
            names.RemoveAt(names.Count - 1);

            result.Append(text, pos, match.Index - pos).Append(expanded);
            map?.Includes.Replaced(match.Index, match.Length, expanded.Length);
            pos = match.Index + match.Length;
        }
        return pos == 0 ? text : result.Append(text, pos, text.Length - pos).ToString();
    }

    static string TrimFinalBreak(string text) =>
        text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2]
        : text.EndsWith('\n') || text.EndsWith('\r') ? text[..^1]
        : text;
}
