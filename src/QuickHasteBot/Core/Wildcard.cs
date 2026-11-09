using System.Text.RegularExpressions;

namespace QuickHasteBot.Core;

/// <summary>
/// Muster mit * für beliebigen Text (auch keinen), ohne Rücksicht auf Gross-/Kleinschreibung: "Posteingang*" beginnt
/// mit, "*ungelesen*" enthält, "*.pdf" endet mit. Für window=title= und name= in Element-Selektoren.
/// </summary>
public static class Wildcard
{
    public static bool IsPattern(string text) => text.Contains('*');

    /// <summary>Ob <paramref name="text"/> ganz zum Muster passt. Ohne * heisst das: gleich.</summary>
    public static bool IsMatch(string pattern, string text)
    {
        if (!IsPattern(pattern)) return string.Equals(pattern, text, StringComparison.OrdinalIgnoreCase);
        var regex = "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$";
        return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    }
}
