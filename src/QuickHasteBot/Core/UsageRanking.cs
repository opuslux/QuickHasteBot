using System.Globalization;
using System.Text;

namespace QuickHasteBot.Core;

/// <summary>
/// Wie viel die Nutzung in der Suche zählt. Jede Verwendung gibt Punkte nach ihrem Alter, die Summe wird zu einem
/// Bonus auf den Treffer-Score gekappt. Allgemein benutzt höchstens <see cref="MaxUsageBonus"/>: ein reiner
/// Inhaltstreffer (10) überholt so nie einen Namenstreffer (ab 50). Nach genau diesem Suchtext gewählt bis
/// <see cref="MaxQueryBonus"/>: zweimal kürzlich gewählt steht praktisch immer oben.
/// </summary>
public static class UsageRanking
{
    public const int MaxUsageBonus = 20;
    public const int MaxQueryBonus = 60;

    /// <summary>So viele Punkte ergeben den vollen allgemeinen Bonus, etwa fünf Verwendungen in den letzten Tagen.</summary>
    const double FullUsagePoints = 500;

    /// <summary>So viele Punkte ergeben den vollen Suchtext-Bonus, zweimal kürzlich gewählt.</summary>
    const double FullQueryPoints = 200;

    /// <summary>Punkte einer Verwendung nach ihrem Alter: Was lange nicht benutzt wurde, verliert seinen Vorsprung.</summary>
    public static int Points(DateTime use, DateTime now)
    {
        var days = (now - use).TotalDays;
        return days switch
        {
            <= 4 => 100,
            <= 14 => 70,
            <= 31 => 50,
            <= 90 => 30,
            _ => 10,
        };
    }

    public static int Points(IReadOnlyList<DateTime> uses, DateTime now)
    {
        var sum = 0;
        for (var i = 0; i < uses.Count; i++)
            sum += Points(uses[i], now);
        return sum;
    }

    public static int UsageBonus(double points) => Scale(points, FullUsagePoints, MaxUsageBonus);

    public static int QueryBonus(double points) => Scale(points, FullQueryPoints, MaxQueryBonus);

    static int Scale(double points, double full, int max) => points <= 0 ? 0 : (int)Math.Round(Math.Min(1.0, points / full) * max);

    /// <summary>
    /// Wie stark ein gespeicherter Suchtext für den aktuellen zählt: gleich voll, angefangen zu tippen (der gespeicherte
    /// beginnt mit dem aktuellen) halb, sonst nicht.
    /// </summary>
    public static double QueryWeight(string stored, string current) =>
        stored.Length == 0 || current.Length == 0 ? 0
        : string.Equals(stored, current, StringComparison.Ordinal) ? 1
        : stored.StartsWith(current, StringComparison.Ordinal) ? 0.5
        : 0;

    /// <summary>
    /// Suchtext zum Speichern und Vergleichen: ohne Rand, Leerzeichen zusammengefasst, klein, ohne Akzente.
    /// "  Grüsse  MfG " wird zu "grusse mfg", wie die Suche selbst Akzente ignoriert.
    /// </summary>
    public static string NormalizeQuery(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return "";

        var decomposed = string.Join(' ', tokens).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
