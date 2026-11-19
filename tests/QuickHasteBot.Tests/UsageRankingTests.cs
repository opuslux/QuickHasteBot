using System.Diagnostics;
using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class UsageRankingTests
{
    static readonly DateTime Now = new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 100)]
    [InlineData(4, 100)]
    [InlineData(5, 70)]
    [InlineData(20, 50)]
    [InlineData(60, 30)]
    [InlineData(365, 10)]
    public void Points_fall_with_age(int days, int points)
    {
        Assert.Equal(points, UsageRanking.Points(Now.AddDays(-days), Now));
    }

    [Fact]
    public void Bonuses_are_capped()
    {
        Assert.Equal(0, UsageRanking.UsageBonus(0));
        Assert.Equal(UsageRanking.MaxUsageBonus, UsageRanking.UsageBonus(5000));
        Assert.Equal(30, UsageRanking.QueryBonus(100));
        Assert.Equal(UsageRanking.MaxQueryBonus, UsageRanking.QueryBonus(200));
        Assert.Equal(UsageRanking.MaxQueryBonus, UsageRanking.QueryBonus(5000));
    }

    [Theory]
    [InlineData("mfg", "mfg", 1)]
    [InlineData("mfg", "mf", 0.5)]
    [InlineData("mf", "mfg", 0)]
    [InlineData("gruss", "mfg", 0)]
    public void Query_weight(string stored, string current, double weight)
    {
        Assert.Equal(weight, UsageRanking.QueryWeight(stored, current));
    }

    [Theory]
    [InlineData("  Grüsse  MfG ", "grusse mfg")]
    [InlineData("Café", "cafe")]
    [InlineData("   ", "")]
    public void Query_is_normalized(string query, string normalized)
    {
        Assert.Equal(normalized, UsageRanking.NormalizeQuery(query));
    }
}

/// <summary>Die Nutzung in der Suche: wie weit sie Treffer verschiebt, und was sie kostet.</summary>
public class SnippetSearchBonusTests
{
    static Snippet Snippet(string name, string? location = null) => new()
    {
        Name = name,
        FullPath = @"C:\s\" + name + ".txt",
        RelativePath = (location is null ? "" : location + @"\") + name + ".txt",
        Kind = SnippetKind.Text,
    };

    static Func<Snippet, int> Bonus(string name, int bonus) => s => s.Name == name ? bonus : 0;

    [Fact]
    public void Used_entry_comes_first_among_equally_good_hits()
    {
        Snippet[] snippets = [Snippet("MfG-EN"), Snippet("MfG-DE")];
        var hits = SnippetSearch.Search(snippets, "mfg", bonus: Bonus("MfG-DE", 4));
        Assert.Equal(["MfG-DE", "MfG-EN"], hits.Select(h => h.Snippet.Name));
    }

    [Fact]
    public void Usage_alone_does_not_pass_a_name_start()
    {
        // "Gruss MfG" Wortanfang (70) + 20 bleibt unter "MfG-DE" Namensanfang (100)
        Snippet[] snippets = [Snippet("Gruss MfG"), Snippet("MfG-DE")];
        var hits = SnippetSearch.Search(snippets, "mfg", bonus: Bonus("Gruss MfG", UsageRanking.MaxUsageBonus));
        Assert.Equal(["MfG-DE", "Gruss MfG"], hits.Select(h => h.Snippet.Name));
    }

    [Fact]
    public void Content_hit_never_passes_a_name_hit_by_usage_alone()
    {
        Snippet[] snippets = [Snippet("Notiz"), Snippet("Bericht")];
        var hits = SnippetSearch.Search(snippets, "bericht", s => s.Name == "Notiz" ? "siehe Bericht" : null,
            bonus: Bonus("Notiz", UsageRanking.MaxUsageBonus));

        Assert.Equal(["Bericht", "Notiz"], hits.Select(h => h.Snippet.Name));
    }

    [Fact]
    public void Learned_query_comes_first()
    {
        Snippet[] snippets = [Snippet("MfG-DE"), Snippet("MfG-EN"), Snippet("Gruss MfG")];
        var hits = SnippetSearch.Search(snippets, "mfg", bonus: Bonus("Gruss MfG", UsageRanking.MaxQueryBonus));
        Assert.Equal("Gruss MfG", hits[0].Snippet.Name);
        Assert.Equal(UsageRanking.MaxQueryBonus, hits[0].Bonus);
    }

    [Fact]
    public void Bonus_counts_before_the_cut()
    {
        var snippets = Enumerable.Range(0, 150).Select(i => Snippet($"Eintrag {i:000}")).ToList();
        var hits = SnippetSearch.Search(snippets, "eintrag", maxResults: 100, bonus: Bonus("Eintrag 149", 10));
        Assert.Equal("Eintrag 149", hits[0].Snippet.Name);
        Assert.Equal(100, hits.Count);
    }

    [Fact]
    public void Without_bonus_order_is_unchanged()
    {
        Snippet[] snippets = [Snippet("B-Gruss"), Snippet("A-Gruss")];
        Assert.Equal(["B-Gruss", "A-Gruss"], SnippetSearch.Search(snippets, "gruss").Select(h => h.Snippet.Name));
    }

    /// <summary>
    /// Vorgabe: praktisch keine Einbusse. 2000 Einträge, alle mit voller Nutzung, 300 gelernte Suchtexte: Bonus bauen
    /// und suchen kostet höchstens wenig mehr als ohne. Gemessen als Median, mit grosszügigem Rand für langsame Runner.
    /// </summary>
    [Fact]
    public void Ranking_by_usage_costs_next_to_nothing()
    {
        using var dir = new TempDirectory();
        var snippets = Enumerable.Range(0, 2000).Select(i => Snippet($"Baustein {i:0000} Gruss", $"Ordner {i % 40}")).ToList();
        var clock = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        using var data = SnippetData.Load(Path.Combine(dir.Path, "snippets.json"), () => clock);
        for (var i = 0; i < 2000; i++)
        {
            for (var n = 0; n < 10; n++)
                data.RecordUse(snippets[i].RelativePath, UseSource.Search, $"gruss {i % 300}");
        }

        var load = Stopwatch.StartNew();
        data.Flush();
        using (SnippetData.Load(data.FilePath)) { }
        load.Stop();
        Assert.True(load.ElapsedMilliseconds < 500, $"Speichern und Laden dauerte {load.ElapsedMilliseconds} ms");

        string[] queries = ["g", "gr", "gru", "grus", "gruss", "gruss 1", "baustein 0", "ordner 3"];
        // Aufwärmen (JIT)
        foreach (var query in queries)
        {
            SnippetSearch.Search(snippets, query);
            SnippetSearch.Search(snippets, query, bonus: data.BonusFor(query));
        }

        var without = Median(() => { foreach (var q in queries) SnippetSearch.Search(snippets, q); });
        var with = Median(() => { foreach (var q in queries) SnippetSearch.Search(snippets, q, bonus: data.BonusFor(q)); });

        // Pro Tastendruck (eine Suche) höchstens ein paar Millisekunden mehr
        var extraPerSearch = (with - without).TotalMilliseconds / queries.Length;
        Assert.True(extraPerSearch < 3, $"ohne {without.TotalMilliseconds:0.0} ms, mit {with.TotalMilliseconds:0.0} ms für {queries.Length} Suchen");
    }

    static TimeSpan Median(Action run)
    {
        var times = new List<TimeSpan>();
        for (var i = 0; i < 9; i++)
        {
            var watch = Stopwatch.StartNew();
            run();
            times.Add(watch.Elapsed);
        }
        times.Sort();
        return times[times.Count / 2];
    }
}
