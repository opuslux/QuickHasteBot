using System.Text.RegularExpressions;

namespace QuickHasteBot.Core;

public enum SyntaxKind
{
    /// <summary>Tasten- und Ablauf-Tags: {tab}, {shift+tab}, {wait,500}, {guard,off}.</summary>
    Key,
    /// <summary>
    /// Klammern, Name und Kommas von {focus,…}, {click,…}, {set,…}, {dblclick,…}, {rclick,…}, {window,…}, {start,…},
    /// dazu {html}…{/html} und {if,…} {elseif,…} {else} {/if}.
    /// </summary>
    Element,
    /// <summary>Selektor bzw. Programm in einem Element-Tag, z.B. #email oder name=Senden.</summary>
    Argument,
    /// <summary>"…" in Tags und Skripten.</summary>
    String,
    /// <summary>Ein bekanntes Tag mit falschen Argumenten, z.B. {tab,x}. Wird so nicht ausgeführt, sondern als Text eingefügt.</summary>
    Error,
    /// <summary>{{ und }}.</summary>
    ScriptDelimiter,
    Keyword,
    /// <summary>Die Helfer aus <see cref="SnippetScript.Helpers"/>: date, key, focus …</summary>
    Helper,
    Number,
    Comment,
}

/// <summary>Was die Test-Taste im Editor laufen lässt.</summary>
public enum TestScope
{
    /// <summary>Den ganzen Baustein.</summary>
    All,
    /// <summary>Die Markierung, auf angeschnittene Tags und Skriptblöcke erweitert.</summary>
    Selection,
    /// <summary>Das Tag, in dem die Schreibmarke steht.</summary>
    Tag,
    /// <summary>Den {{ … }}-Block, in dem die Schreibmarke steht.</summary>
    Script,
}

/// <param name="Error">Bei <see cref="SyntaxKind.Error"/>: warum das Tag nicht gilt.</param>
public readonly record struct SyntaxSpan(int Start, int Length, SyntaxKind Kind, string? Error = null)
{
    public int End => Start + Length;
}

/// <summary>
/// Zerlegt einen Textbaustein für die Farben im Editor. Folgt denselben Regeln wie das Ausführen
/// (<see cref="KeyTags.MarkTags"/>, <see cref="SnippetScript"/>): was hier farbig ist, wird zur Taste bzw. ausgewertet,
/// alles andere ist Text.
/// </summary>
public static partial class SnippetSyntax
{
    static readonly HashSet<string> Keywords =
    [
        "async", "await", "break", "case", "catch", "class", "const", "continue", "default", "delete", "do", "else",
        "false", "finally", "for", "function", "if", "in", "instanceof", "let", "new", "null", "of", "return", "switch",
        "this", "throw", "true", "try", "typeof", "undefined", "var", "void", "while",
    ];

    internal static readonly HashSet<string> Helpers =
        SnippetScript.Helpers.Select(h => h.Signature.Split('(')[0]).ToHashSet();

    // Kommentar (auch unvollständig), Zeichenkette (auch unvollständig), Zahl, Name
    [GeneratedRegex("""(?<c>//[^\r\n]*|/\*.*?(?:\*/|$))|(?<s>"(?:\\.|[^"\\\r\n])*"?|'(?:\\.|[^'\\\r\n])*'?|`(?:\\.|[^`\\])*`?)|(?<n>\b\d+(?:\.\d+)?\b)|(?<i>[A-Za-z_$][\w$]*)""", RegexOptions.Singleline)]
    internal static partial Regex ScriptToken();

    /// <param name="keyTags">Wie AppSettings.EvaluateKeyTags: aus, dann sind Tags Text.</param>
    /// <param name="scripts">Wie AppSettings.EvaluateScripts: aus, dann sind {{ … }} Text.</param>
    /// <param name="includeExists">Ob es den Baustein aus {include,…} gibt; ohne Angabe wird das nicht geprüft.</param>
    public static IReadOnlyList<SyntaxSpan> Tokenize(string text, bool keyTags = true, bool scripts = true, Func<string, bool>? includeExists = null)
    {
        var spans = new List<SyntaxSpan>();
        var html = keyTags ? KeyTags.HtmlPairs(text) : [];
        var htmlClose = -1;
        int? cursor = null; // die Spanne des ersten {cursor}
        var blocks = new List<(int Start, int Length, FormStep Step)>(); // {if}, {elseif}, {else}, {/if}: Passen sie zusammen?
        foreach (Match match in KeyTags.Candidate().Matches(text))
        {
            if (match.Groups["note"].Success)
            {
                // Kommentare gehören zu den Tags: ohne Tags sind sie Text
                if (keyTags) spans.Add(new(match.Index, match.Length, SyntaxKind.Comment));
                continue;
            }
            if (!match.Groups[1].Success)
            {
                if (scripts) Script(spans, text, match.Index, match.Length);
                continue;
            }
            if (!keyTags) continue;

            // {html}…{/html}: die beiden Tags wie {window}, dazwischen ist alles HTML, Tags darin sind Text
            var content = match.Groups[1].Value;
            if (htmlClose >= 0 && match.Index < htmlClose)
                continue;
            if (match.Index == htmlClose)
            {
                spans.Add(new(match.Index, match.Length, SyntaxKind.Element));
                htmlClose = -1;
                continue;
            }
            if (html.TryGetValue(match.Index, out var close))
            {
                spans.Add(new(match.Index, match.Length, SyntaxKind.Element));
                htmlClose = close.CloseStart;
                continue;
            }
            if (KeyTags.IsHtmlStart(content) || KeyTags.IsHtmlEnd(content))
            {
                spans.Add(new(match.Index, match.Length, SyntaxKind.Error,
                    KeyTags.IsHtmlStart(content) ? "{html} needs a {/html} after the formatted text" : "{/html} needs an {html} before it"));
                continue;
            }

            if (KeyTags.IsIfEnd(content))
            {
                spans.Add(new(match.Index, match.Length, SyntaxKind.Element));
                blocks.Add((match.Index, match.Length, new EndIfStep()));
                continue;
            }

            var steps = Tag(spans, match.Index, content, includeExists);
            if (steps is [IfStep or ElseStep])
                blocks.Add((match.Index, match.Length, steps[0]));
            if (steps is [CursorStep])
            {
                if (cursor is null)
                    cursor = spans.Count - 1;
                else
                    spans[^1] = spans[^1] with { Kind = SyntaxKind.Error, Error = "only the first {cursor} counts" };
            }
            else if (cursor is { } c && steps.Any(s => s is KeyStep or ElementStep or WindowStep or StartStep or OpenUrlStep)
                     && spans[c].Kind != SyntaxKind.Error)
            {
                spans[c] = spans[c] with
                {
                    Kind = SyntaxKind.Error,
                    Error = $"only text may follow {{cursor}}, but {text.Substring(match.Index, match.Length)} comes after it",
                };
            }
        }

        // Ein {else} ohne {if}, ein {if} ohne {/if} …: das Tag, an dem es hapert, rot. Laufen würde der Baustein so nicht.
        if (KeyTags.CheckBlocks([.. blocks.Select(b => b.Step)]) is { } broken)
        {
            var (start, length, _) = blocks[broken.Step];
            var at = spans.FindIndex(s => s.Start >= start);
            spans.RemoveAll(s => s.Start >= start && s.End <= start + length);
            spans.Insert(at, new(start, length, SyntaxKind.Error, broken.Error));
        }
        return spans;
    }

    /// <summary>
    /// Welcher Teil von <paramref name="text"/> beim Testen läuft, zum Debuggen einzelner Schritte:
    /// eine Markierung (angeschnittene Tags und Skriptblöcke ganz), sonst das Tag oder der Skriptblock, in dem die
    /// Schreibmarke steht, sonst alles. Direkt vor "{" oder nach "}" zählt nicht als im Tag.
    /// </summary>
    public static (int Start, int Length, TestScope Scope) TestRange(string text, int selectionStart, int selectionLength)
    {
        if (selectionLength > 0)
        {
            var start = selectionStart;
            var end = selectionStart + selectionLength;
            foreach (Match match in KeyTags.Candidate().Matches(text))
            {
                if (match.Index >= end) break;
                if (match.Index + match.Length <= start) continue;
                start = Math.Min(start, match.Index);
                end = Math.Max(end, match.Index + match.Length);
            }
            // Genau ein Tag markiert (z.B. nach "Tag testen"): weiter ein Tag
            foreach (Match match in KeyTags.Candidate().Matches(text))
                if (match.Index == start && match.Length == end - start && Single(match) is { } single)
                    return (start, end - start, single);
            return (start, end - start, TestScope.Selection);
        }

        var caret = selectionStart;
        foreach (Match match in KeyTags.Candidate().Matches(text))
        {
            if (match.Index >= caret) break;
            if (caret >= match.Index + match.Length) continue;
            if (Single(match) is { } single)
                return (match.Index, match.Length, single);
            break; // {name}: Text, also alles
        }
        return (0, text.Length, TestScope.All);

        static TestScope? Single(Match match)
        {
            if (match.Groups["note"].Success) return null; // ein Kommentar läuft nicht, also alles
            if (!match.Groups[1].Success) return TestScope.Script;
            var name = KeyTags.NamePart().Match(match.Groups[1].Value);
            // {if,…} allein tut nichts und hätte kein {/if}: alles testen
            return name.Success && KeyTags.IsTagName(name.Groups[1].Value)
                && name.Groups[1].Value.ToLowerInvariant() is not ("if" or "elseif" or "else") ? TestScope.Tag : null;
        }
    }

    /// <returns>Die Schritte des Tags, leer bei Text oder einem Fehler.</returns>
    static IReadOnlyList<FormStep> Tag(List<SyntaxSpan> spans, int start, string content, Func<string, bool>? includeExists)
    {
        var name = KeyTags.NamePart().Match(content);
        if (!name.Success || !KeyTags.IsTagName(name.Groups[1].Value)) return []; // {name}, JSON: Text

        var length = content.Length + 2;
        if (!KeyTags.TryParse(content, null, out var steps, out var error))
        {
            spans.Add(new(start, length, SyntaxKind.Error, error));
            return [];
        }
        if (steps is [IncludeStep include] && includeExists is not null && !includeExists(include.Path))
        {
            spans.Add(new(start, length, SyntaxKind.Error, $"there is no text snippet \"{include.Path}\""));
            return [];
        }

        var spec = name.Groups[1].Value.ToLowerInvariant();
        if (spec == "else")
        {
            spans.Add(new(start, length, SyntaxKind.Element));
            return steps;
        }
        if (!KeyTags.ElementTags.ContainsKey(spec) && spec is not ("window" or "start" or "include" or "if" or "elseif"))
        {

            spans.Add(new(start, length, SyntaxKind.Key));
            return steps;
        }

        // {focus, #email}: Klammer, Name und Trenner, dann die Argumente, Anführungszeichen als Zeichenkette
        var pos = start + 1 + name.Length;
        Add(spans, start, pos - start, SyntaxKind.Element);
        var end = start + length - 1;
        var offset = pos - start - 1; // Position in content
        while (pos < end)
        {
            var c = content[offset];
            if (c == '"')
            {
                var close = content.IndexOf('"', offset + 1);
                var quoted = close - offset + 1;
                Add(spans, pos, quoted, SyntaxKind.String);
                pos += quoted;
                offset += quoted;
            }
            else
            {
                Add(spans, pos, 1, c == ',' ? SyntaxKind.Element : SyntaxKind.Argument);
                pos++;
                offset++;
            }
        }
        Add(spans, end, 1, SyntaxKind.Element);
        return steps;
    }

    static void Script(List<SyntaxSpan> spans, string text, int start, int length)
    {
        spans.Add(new(start, 2, SyntaxKind.ScriptDelimiter));
        var code = text.Substring(start + 2, length - 4);
        foreach (Match token in ScriptToken().Matches(code))
        {
            var kind = token.Groups["c"].Success ? SyntaxKind.Comment
                : token.Groups["s"].Success ? SyntaxKind.String
                : token.Groups["n"].Success ? SyntaxKind.Number
                : Keywords.Contains(token.Value) ? SyntaxKind.Keyword
                // date() ja, x.date nein
                : Helpers.Contains(token.Value) && !PrecededByDot(code, token.Index) ? SyntaxKind.Helper
                : (SyntaxKind?)null;
            if (kind is { } k)
                spans.Add(new(start + 2 + token.Index, token.Length, k));
        }
        spans.Add(new(start + length - 2, 2, SyntaxKind.ScriptDelimiter));
    }

    internal static bool PrecededByDot(string code, int index)
    {
        var i = index - 1;
        while (i >= 0 && char.IsWhiteSpace(code[i])) i--;
        return i >= 0 && code[i] == '.';
    }

    /// <summary>Hängt an oder verlängert die letzte Spanne gleicher Art, damit "#email" eine Spanne ist und nicht sechs.</summary>
    static void Add(List<SyntaxSpan> spans, int start, int length, SyntaxKind kind)
    {
        if (spans.Count > 0 && spans[^1] is { Kind: var last } previous && last == kind && previous.End == start)
            spans[^1] = previous with { Length = previous.Length + length };
        else
            spans.Add(new(start, length, kind));
    }
}
