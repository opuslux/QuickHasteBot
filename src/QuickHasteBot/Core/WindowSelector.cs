using System.Text.RegularExpressions;

namespace QuickHasteBot.Core;

/// <summary>
/// Beschreibt ein Programmfenster für {window,…} und window= in Element-Selektoren.
/// "crm": Programmname crm(.exe) oder ein Titel, der "crm" enthält. "process=crm": nur der Programmname.
/// "title=Kunden*": der Titel, * steht für beliebigen Text, ohne * genügt ein Teil des Titels.
/// Gross-/Kleinschreibung zählt nie.
/// </summary>
public sealed record WindowSelector(string? Process, string? Title, string? Any)
{
    public static bool TryParse(string text, out WindowSelector selector, out string error)
    {
        selector = new WindowSelector(null, null, null);
        error = "";
        var trimmed = text.Trim();
        var eq = trimmed.IndexOf('=');
        var key = eq < 0 ? "" : trimmed[..eq].Trim().ToLowerInvariant();
        var value = eq < 0 ? trimmed : trimmed[(eq + 1)..].Trim();

        switch (key)
        {
            case "" when value.Length > 0:
                selector = new WindowSelector(null, null, StripExe(value));
                return true;
            case "process" when value.Length > 0:
                selector = new WindowSelector(StripExe(value), null, null);
                return true;
            case "title" when value.Length > 0:
                selector = new WindowSelector(null, value, null);
                return true;
            default:
                error = $"window needs a program or title, e.g. {{window,crm}}, {{window,process=crm}} or {{window,title=Kunden*}}, not \"{text}\"";
                return false;
        }
    }

    static string StripExe(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    public bool Matches(string processName, string title)
    {
        if (Process is not null) return processName.Equals(Process, StringComparison.OrdinalIgnoreCase);
        if (Title is not null) return TitleMatches(Title, title);
        return processName.Equals(Any, StringComparison.OrdinalIgnoreCase) || title.Contains(Any!, StringComparison.OrdinalIgnoreCase);
    }

    static bool TitleMatches(string pattern, string title)
    {
        if (!pattern.Contains('*')) return title.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        var regex = "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$";
        return Regex.IsMatch(title, regex, RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    public override string ToString() =>
        Process is not null ? "process=" + Process : Title is not null ? "title=" + Title : Any!;
}
