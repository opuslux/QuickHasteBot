using System.Globalization;
using System.Text;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>Texte zur Nutzung für den Tooltip am Icon und den Tab "Statistik".</summary>
internal static class UsageText
{
    const int MaxLearnedInTooltip = 5;

    /// <summary>"heute", "gestern", "vor 5 Tagen", "vor 3 Wochen", "vor 4 Monaten", nach Kalendertagen in Ortszeit.</summary>
    public static string Ago(DateTime last, DateTime now)
    {
        var days = (now.ToLocalTime().Date - last.ToLocalTime().Date).Days;
        return days switch
        {
            <= 0 => Strings.AgoToday,
            1 => Strings.AgoYesterday,
            < 14 => string.Format(Strings.AgoDaysFormat, days),
            < 60 => string.Format(Strings.AgoWeeksFormat, days / 7),
            _ => string.Format(Strings.AgoMonthsFormat, days / 30),
        };
    }

    /// <summary>Datum in Ortszeit im Format der Windows-Region, z.B. 20.11.2026.</summary>
    public static string Date(DateTime utc) => utc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    /// <summary>"„mf“ (4×), „gruss“ (2×)", höchstens <paramref name="max"/>, dahinter "…".</summary>
    public static string Learned(IReadOnlyList<LearnedQuery> queries, int max = int.MaxValue)
    {
        var shown = queries.Take(max).Select(Learned);
        var text = string.Join(", ", shown);
        return queries.Count > max ? text + ", …" : text;
    }

    public static string Learned(LearnedQuery query) => string.Format(Strings.LearnedQueryFormat, query.Query, query.Count);

    /// <summary>
    /// Der Tooltip am Icon in der Trefferliste, null bei nie Benutztem (dann keiner). Mit <paramref name="query"/> zusätzlich,
    /// ob der Eintrag wegen eines gelernten Suchtexts weiter oben steht.
    /// </summary>
    public static string? Tooltip(UsageStats? stats, string? query, DateTime now)
    {
        if (stats is null) return null;

        var text = new StringBuilder();
        text.Append(string.Format(Strings.UsageTooltipFormat, stats.Total, stats.Search, stats.Browse, stats.Hotkey));
        if (stats.Last is { } last)
            text.AppendLine().Append(string.Format(Strings.UsageTooltipLastFormat, Ago(last, now)));
        if (stats.Queries.Count > 0)
            text.AppendLine().Append(string.Format(Strings.UsageTooltipLearnedFormat, Learned(stats.Queries, MaxLearnedInTooltip)));

        var normalized = query is null ? "" : UsageRanking.NormalizeQuery(query);
        if (normalized.Length > 0)
        {
            // Der gleiche Suchtext vor einem, zu dem man gerade hintippt
            var boost = stats.Queries
                .Select(q => (Query: q, Weight: UsageRanking.QueryWeight(q.Query, normalized)))
                .Where(q => q.Weight > 0)
                .OrderByDescending(q => q.Weight).ThenByDescending(q => q.Query.Count)
                .Select(q => q.Query)
                .FirstOrDefault();
            if (boost is not null)
                text.AppendLine().Append(string.Format(Strings.UsageTooltipBoostFormat, boost.Query, boost.Count));
        }
        return text.ToString();
    }
}
