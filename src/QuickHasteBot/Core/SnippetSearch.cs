using System.Globalization;

namespace QuickHasteBot.Core;

/// <param name="MatchStart">Treffer des ersten Suchworts im Namen, für die Hervorhebung. -1 ohne Treffer im Namen.</param>
public sealed record SearchHit(Snippet Snippet, int Score, int MatchStart, int MatchLength);

/// <summary>
/// Alle Suchwörter müssen passen (UND). Groß/Klein und Akzente werden ignoriert, "grusse" findet "Grüsse".
/// Der Ordnerpfad grenzt nur ein: Mindestens ein Suchwort muss Name oder Inhalt treffen. "grusse" findet den Ordner
/// "Grüsse", aber nicht alles darin; "grusse mfg" findet "MfG-DE" in diesem Ordner.
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
            // Hat ein Suchwort Name oder Inhalt getroffen? Sonst passt nur der Ordner, und das zählt allein nicht.
            var anchored = false;
            List<string>? locationOnly = null;

            foreach (var token in tokens)
            {
                var score = ScoreName(snippet.Name, token, out var start, out var length);
                if (score > 0)
                    anchored = true;
                else if (Contains(snippet.Location, token))
                {
                    score = LocationScore;
                    (locationOnly ??= []).Add(token);
                }
                else if (contentProvider is not null && ContentContains(token))
                {
                    score = ContentScore;
                    anchored = true;
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

            // Nur Ordnertreffer: zählt doch, wenn eines dieser Wörter auch im Inhalt steht
            if (total > 0 && !anchored && contentProvider is not null)
                anchored = locationOnly!.Any(ContentContains);

            if (total > 0 && anchored)
                hits.Add(new SearchHit(snippet, total, matchStart, matchLength));

            // Inhalt erst laden, wenn Name und Ort nicht gereicht haben
            bool ContentContains(string token)
            {
                if (!contentLoaded)
                {
                    content = contentProvider!(snippet);
                    contentLoaded = true;
                }
                return content is not null && Contains(content, token);
            }
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

    /// <summary>Für einfache Filter (Tag-Menü im Editor): kommt jedes Suchwort irgendwo in <paramref name="text"/> vor?</summary>
    public static bool MatchesAll(string text, string query) =>
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(token => Contains(text, token));

    static bool Contains(string text, string token) => text.Length > 0 && Compare.IndexOf(text, token, Options) >= 0;
}
