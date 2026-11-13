namespace QuickHasteBot.Core;

/// <summary>
/// Suchen und Ersetzen im Editor, bewusst schlank: reiner Text, wahlweise mit Gross-/Kleinschreibung, ohne Regex und
/// ohne „ganzes Wort“. Treffer überlappen nicht ("aa" in "aaa" ist ein Treffer), wie in Notepad.
/// </summary>
public static class FindInText
{
    /// <summary>Die Anfänge aller Treffer, aufsteigend. Leerer Suchtext ergibt keine.</summary>
    public static IReadOnlyList<int> FindAll(string text, string query, bool matchCase)
    {
        var matches = new List<int>();
        if (query.Length == 0) return matches;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var at = text.IndexOf(query, 0, comparison); at >= 0; at = text.IndexOf(query, at + query.Length, comparison))
            matches.Add(at);
        return matches;
    }

    /// <summary>
    /// Welcher Treffer als nächster dran ist: vorwärts der erste ab <paramref name="offset"/>, rückwärts der letzte, der
    /// vor <paramref name="offset"/> beginnt. Am Ende geht es vorne weiter und umgekehrt. -1 ohne Treffer.
    /// </summary>
    public static int Next(IReadOnlyList<int> matches, int offset, bool backwards)
    {
        if (matches.Count == 0) return -1;
        if (backwards)
        {
            for (var i = matches.Count - 1; i >= 0; i--)
                if (matches[i] < offset) return i;
            return matches.Count - 1;
        }
        for (var i = 0; i < matches.Count; i++)
            if (matches[i] >= offset) return i;
        return 0;
    }
}
