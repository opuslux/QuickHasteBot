using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace QuickHasteBot.Core;

/// <summary>Die Version dieser exe, wie sie der Release-Workflow aus dem Tag stempelt.</summary>
public static class AppVersion
{
    /// <summary>"2.15.0", "2.15.0-dev.3" oder "2.15.0-dev.3.dirty", dahinter das SDK "+commit".</summary>
    public static string Informational =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>Zum Anzeigen: "2.15.0 (abc1234)", der Commit gekürzt.</summary>
    public static string Display()
    {
        var version = Informational;
        var plus = version.IndexOf('+');
        return plus < 0 ? version : $"{version[..plus]} ({version[(plus + 1)..Math.Min(version.Length, plus + 8)]})";
    }

    /// <summary>
    /// Die Zahlen davon, ohne "-dev…" und "+commit": Ein Zwischenstand von 2.15.0 gilt als 2.15.0. Null ohne Version
    /// (Build ohne git, "0.0.0").
    /// </summary>
    public static Version? Numeric(string informational)
    {
        var text = informational.Split('+')[0].Split('-')[0].Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var version) && version > new Version(0, 0, 0) ? version : null;
    }
}

/// <summary>
/// Fragt GitHub nach dem neuesten Release, höchstens einmal am Tag. Geschickt wird nur die Anfrage selbst, keine Daten
/// über den Benutzer; abschalten lässt es sich in den Einstellungen (CheckForUpdates).
/// </summary>
public static class UpdateCheck
{
    public const string LatestReleaseApi = "https://api.github.com/repos/opuslux/QuickHasteBot/releases/latest";

    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <param name="Tag">Wie auf GitHub, z.B. "v2.16.0".</param>
    /// <param name="Url">Die Seite des Releases mit den Downloads.</param>
    public sealed record Release(Version Version, string Tag, string Url);

    /// <summary>Ob wieder gefragt werden soll: noch nie, vor einem Tag oder mehr, oder die Uhr ging zurück.</summary>
    public static bool Due(DateTimeOffset? last, DateTimeOffset now) => last is not { } l || now - l >= Interval || l > now;

    /// <summary>Das Release aus der Antwort von GitHub, null wenn sie nicht passt.</summary>
    public static Release? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { Length: > 0 } tagName
                || AppVersion.Numeric(tagName) is not { } version)
                return null;
            var url = root.TryGetProperty("html_url", out var html) && html.GetString() is { Length: > 0 } u ? u : null;
            return new(version, tagName, url ?? ReleasePage(tagName));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Die Release-Seite zu einem Tag, wenn nur der Tag bekannt ist (aus settings.json).</summary>
    public static string ReleasePage(string tag) => $"https://github.com/opuslux/QuickHasteBot/releases/tag/{Uri.EscapeDataString(tag)}";

    /// <summary>Ob <paramref name="tag"/> neuer ist als diese Version. Ohne eigene Version (Build ohne git) nie.</summary>
    public static bool IsNewer(string? tag, string informational) =>
        tag is not null && AppVersion.Numeric(tag) is { } latest && AppVersion.Numeric(informational) is { } current && latest > current;

    /// <exception cref="HttpRequestException">Kein Netz, GitHub nicht erreichbar oder eine Fehlerantwort.</exception>
    /// <exception cref="TaskCanceledException">Zeitüberschreitung.</exception>
    public static async Task<Release?> FetchAsync(HttpClient client, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("QuickHasteBot", AppVersion.Numeric(AppVersion.Informational)?.ToString() ?? "0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancel));
    }
}
