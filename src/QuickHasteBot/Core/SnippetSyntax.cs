using System.Text.RegularExpressions;

namespace QuickHasteBot.Core;

public enum SyntaxKind
{
    /// <summary>Tasten- und Ablauf-Tags: {tab}, {shift+tab}, {wait,500}, {guard,off}.</summary>
    Key,
    /// <summary>Klammern, Name und Kommas von {focus,…}, {click,…}, {set,…}, {window,…}, {start,…}.</summary>
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

    static readonly HashSet<string> Helpers =
        SnippetScript.Helpers.Select(h => h.Signature.Split('(')[0]).ToHashSet();

    // Kommentar (auch unvollständig), Zeichenkette (auch unvollständig), Zahl, Name
    [GeneratedRegex("""(?<c>//[^\r\n]*|/\*.*?(?:\*/|$))|(?<s>"(?:\\.|[^"\\\r\n])*"?|'(?:\\.|[^'\\\r\n])*'?|`(?:\\.|[^`\\])*`?)|(?<n>\b\d+(?:\.\d+)?\b)|(?<i>[A-Za-z_$][\w$]*)""", RegexOptions.Singleline)]
    private static partial Regex ScriptToken();

    /// <param name="keyTags">Wie AppSettings.EvaluateKeyTags: aus, dann sind Tags Text.</param>
    /// <param name="scripts">Wie AppSettings.EvaluateScripts: aus, dann sind {{ … }} Text.</param>
    public static IReadOnlyList<SyntaxSpan> Tokenize(string text, bool keyTags = true, bool scripts = true)
    {
        var spans = new List<SyntaxSpan>();
        foreach (Match match in KeyTags.Candidate().Matches(text))
        {
            if (match.Groups["note"].Success)
            {
                // Kommentare gehören zu den Tags: ohne Tags sind sie Text
                if (keyTags) spans.Add(new(match.Index, match.Length, SyntaxKind.Comment));
            }
            else if (!match.Groups[1].Success)
            {
                if (scripts) Script(spans, text, match.Index, match.Length);
            }
            else if (keyTags)
                Tag(spans, match.Index, match.Groups[1].Value);
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
            return name.Success && KeyTags.IsTagName(name.Groups[1].Value) ? TestScope.Tag : null;
        }
    }

    static void Tag(List<SyntaxSpan> spans, int start, string content)
    {
        var name = KeyTags.NamePart().Match(content);
        if (!name.Success || !KeyTags.IsTagName(name.Groups[1].Value)) return; // {name}, JSON: Text

        var length = content.Length + 2;
        if (!KeyTags.TryParse(content, null, out _, out var error))
        {
            spans.Add(new(start, length, SyntaxKind.Error, error));
            return;
        }

        var spec = name.Groups[1].Value.ToLowerInvariant();
        if (spec is not ("focus" or "click" or "set" or "window" or "start"))
        {
            spans.Add(new(start, length, SyntaxKind.Key));
            return;
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

    static bool PrecededByDot(string code, int index)
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
