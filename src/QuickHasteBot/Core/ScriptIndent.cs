using System.Text;

namespace QuickHasteBot.Core;

/// <summary>Eine neue Einrückung für eine Zeile: ab <see cref="Start"/> werden <see cref="Length"/> Zeichen zu <see cref="Indent"/>.</summary>
public readonly record struct IndentChange(int Start, int Length, string Indent);

/// <summary>
/// "Einrückung ordnen" im Editor: rückt {if}-Blöcke ein, vier Leerzeichen pro Ebene. Ändert nur, was ohnehin Layout ist
/// (<see cref="KeyTags.IndentRemovals"/>), was gesendet wird, bleibt also gleich: Tag-Zeilen bekommen die Einrückung
/// ihrer Ebene, Textstücke auf eigenen Zeilen ebenso, mit ihrer relativen Einrückung. Text, der hinter einem Tag beginnt,
/// Skriptblöcke über mehrere Zeilen und der Inhalt von {html}-Blöcken bleiben, wie sie sind.
/// </summary>
public static class ScriptIndent
{
    /// <summary>Eine Ebene.</summary>
    public const string Unit = "    ";

    /// <summary>Das ganze Skript eingerückt.</summary>
    public static string Format(string text) => Apply(text, Changes(text, 0, text.Length));

    public static string Apply(string text, IReadOnlyList<IndentChange> changes)
    {
        var result = new StringBuilder(text.Length);
        var pos = 0;
        foreach (var change in changes.OrderBy(c => c.Start))
        {
            result.Append(text, pos, change.Start - pos).Append(change.Indent);
            pos = change.Start + change.Length;
        }
        return result.Append(text, pos, text.Length - pos).ToString();
    }

    /// <summary>
    /// Die Änderungen für die Zeilen von <paramref name="start"/> bis <paramref name="end"/> (ganze Zeilen; eine Auswahl,
    /// die am Anfang einer Zeile endet, zählt diese nicht mehr). Ein Textstück wird immer ganz eingerückt, sonst änderte
    /// sich seine gemeinsame Einrückung und damit, was getippt wird. Nur Zeilen, die sich ändern, in Textreihenfolge.
    /// </summary>
    public static IReadOnlyList<IndentChange> Changes(string text, int start, int end)
    {
        var scan = KeyTags.Scan(text, new FormBuilder());
        if (!scan.IsForm) return [];
        var first = LineStart(text, Math.Clamp(start, 0, text.Length));
        end = Math.Clamp(end, first, text.Length);
        bool Selected(int lineStart) => lineStart >= first && (lineStart < end || lineStart == first);

        var changes = new Dictionary<int, IndentChange>();
        void Set(int lineStart, int length, string indent)
        {
            if (length != indent.Length || string.CompareOrdinal(text, lineStart, indent, 0, length) != 0)
                changes[lineStart] = new(lineStart, length, indent);
        }

        // Tag-Zeilen: die erste Sache auf der Zeile ist ein Tag (auch {cursor} nicht) oder ein Kommentar allein auf der Zeile
        var ifs = scan.Edits.Where(e => e.Steps is [IfStep or ElseStep or EndIfStep]).ToList();
        for (var p = 0; p <= text.Length; p = NextLine(text, p))
        {
            if (Selected(p) && TagLine(p) is { } tag)
                Set(p, tag.Indent, Indent(Depth(ifs, p) - (tag.Closes ? 1 : 0)));
            if (p == text.Length) break;
        }

        // Textstücke auf eigenen Zeilen: ganz oder gar nicht
        foreach (var (from, to) in KeyTags.IndentableSegments(text, scan))
        {
            var lines = KeyTags.IndentLines(text, scan, from, to).Where(l => TagLine(l.Start) is null).ToList();
            if (!lines.Any(l => Selected(l.Start))) continue;
            var common = KeyTags.CommonIndent(text, lines);
            var indent = Indent(Depth(ifs, from));
            foreach (var line in lines)
                Set(line.Start, line.Indent, line.Blank ? "" : indent + text.Substring(line.Start + common, line.Indent - common));
        }
        return changes.Values.OrderBy(c => c.Start).ToList();

        (int Indent, bool Closes)? TagLine(int p)
        {
            if (p > 0 && text[p - 1] != '\n') return null;
            var q = p;
            while (q < text.Length && text[q] is ' ' or '\t') q++;
            // Ein Kommentar allein auf der letzten Zeile nimmt den Umbruch davor mit (KeyTags.WholeLine)
            if (scan.Edits.Any(e => e.Kind == KeyTags.EditKind.Note && e.Start < p && e.End == text.Length && text[e.Start] is '\r' or '\n'
                    && e.Start >= p - 2))
                return (q - p, false);
            if (scan.Blocks.Any(b => b.Start < p && p < b.End) || scan.Edits.Any(e => e.Start < p && p < e.End)) return null;
            if (scan.Edits.FirstOrDefault(e => e.Start == p && e.Kind == KeyTags.EditKind.Note) is { End: > 0 } note
                && (note.End == text.Length || text[note.End - 1] is '\n' or '\r'))
                return (q - p, false);
            if (scan.Edits.FirstOrDefault(e => e.Start == q && e.End > q) is { IsBoundary: true } edit)
                return (q - p, edit.Steps is [ElseStep or EndIfStep or IfStep { ElseIf: true }]);
            return null;
        }
    }

    /// <summary>Wie viele {if} vor <paramref name="pos"/> offen sind.</summary>
    static int Depth(IReadOnlyList<KeyTags.TagEdit> ifs, int pos)
    {
        var depth = 0;
        foreach (var edit in ifs.TakeWhile(e => e.End <= pos))
        {
            if (edit.Steps is [IfStep { ElseIf: false }]) depth++;
            else if (edit.Steps is [EndIfStep]) depth = Math.Max(0, depth - 1);
        }
        return depth;
    }

    static string Indent(int depth) => string.Concat(Enumerable.Repeat(Unit, Math.Max(0, depth)));

    static int LineStart(string text, int pos)
    {
        var i = pos;
        while (i > 0 && text[i - 1] != '\n') i--;
        return i;
    }

    static int NextLine(string text, int p)
    {
        var next = text.IndexOf('\n', p);
        return next < 0 ? text.Length : next + 1;
    }

    /// <summary>
    /// Für das Einrücken beim Tippen: Endet die Zeile mit {if,…}, {elseif,…} oder {else}, beginnt danach eine neue Ebene.
    /// </summary>
    public static bool OpensBlock(string line)
    {
        var trimmed = line.TrimEnd();
        var last = KeyTags.Candidate().Matches(trimmed).LastOrDefault();
        return last is { Success: true } && last.Index + last.Length == trimmed.Length && last.Groups[1].Success
            && !last.Groups["note"].Success
            && KeyTags.TryParse(last.Groups[1].Value, null, out var steps, out _)
            && steps is [IfStep or ElseStep];
    }

    /// <summary>Die Zeile besteht nur aus {else}, {elseif,…} oder {/if}: Beim Tippen rückt sie eine Ebene zurück.</summary>
    public static bool ClosesBlock(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0 && KeyTags.Candidate().Match(trimmed) is { Success: true } match && match.Length == trimmed.Length
            && match.Groups[1].Success && !match.Groups["note"].Success
            && KeyTags.TryParse(match.Groups[1].Value, null, out var steps, out _)
            && steps is [ElseStep or EndIfStep or IfStep { ElseIf: true }];
    }
}
