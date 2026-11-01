using System.Globalization;

namespace QuickHasteBot.Core;

/// <param name="MatchStart">Treffer des ersten Suchworts im Namen, für die Hervorhebung. -1 ohne Treffer im Namen.</param>
public sealed record SearchHit(Snippet Snippet, int Score, int MatchStart, int MatchLength);

/// <summary>
/// Alle Suchwörter müssen passen (UND). Groß/Klein und Akzente werden ignoriert, "grusse" findet "Grüsse".
/// </summary>
public static class SnippetSearch
{
    const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    const int NameStartScore = 100;
    const int WordStartScore = 70;
    const int NameContainsScore = 50;
    const int LocationScore = 25;
    const int ContentScore = 10;

    public static IReadOnlyList<SearchHit> Search(
        IEnumerable<Snippet> snippets,
        string query,
        Func<Snippet, string?>? contentProvider = null,
        int maxResults = 100)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return [];

        var hits = new List<SearchHit>();
        foreach (var snippet in snippets)
        {
            var total = 0;
            int matchStart = -1, matchLength = 0;
            string? content = null;
            var contentLoaded = false;

            foreach (var token in tokens)
            {
                var score = ScoreName(snippet.Name, token, out var start, out var length);
                if (score == 0 && Contains(snippet.Location, token))
                    score = LocationScore;
                if (score == 0 && contentProvider is not null)
                {
                    // Inhalt erst laden, wenn Name und Ort nicht gereicht haben
                    if (!contentLoaded)
                    {
                        content = contentProvider(snippet);
                        contentLoaded = true;
                    }
                    if (content is not null && Contains(content, token))
                        score = ContentScore;
                }

                if (score == 0)
                {
                    total = 0;
                    break;
                }

                if (matchStart < 0 && start >= 0)
                    (matchStart, matchLength) = (start, length);
                total += score;
            }

            if (total > 0)
                hits.Add(new SearchHit(snippet, total, matchStart, matchLength));
        }

        // Stabil sortiert: bei gleichem Score bleibt die Baum-Reihenfolge (Nummern-Präfixe) erhalten
        return hits
            .OrderByDescending(h => h.Score)
            .Take(maxResults)
            .ToList();
    }

    static int ScoreName(string name, string token, out int start, out int length)
    {
        start = Compare.IndexOf(name, token, Options, out length);
        if (start < 0) return 0;
        if (start == 0) return NameStartScore;

        // Alle Vorkommen prüfen: "de" in "Bestätigung-DE" ist ein Wortanfang, aber erst beim zweiten Treffer
        var position = start;
        var positionLength = length;
        while (true)
        {
            if (!char.IsLetterOrDigit(name[position - 1]))
            {
                (start, length) = (position, positionLength);
                return WordStartScore;
            }

            var next = Compare.IndexOf(name.AsSpan(position + 1), token, Options, out positionLength);
            if (next < 0) return NameContainsScore;
            position += 1 + next;
        }
    }

    static bool Contains(string text, string token) => text.Length > 0 && Compare.IndexOf(text, token, Options) >= 0;
}
