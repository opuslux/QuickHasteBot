using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class SnippetSearchTests
{
    // Nachgebaut aus dem echten structure-Ordner
    static readonly string[] Files =
    [
        @"2-Adressen\1-max.muster@example.com.txt",
        @"3-Anreden\Sehr geehrte Frau.txt",
        @"4-Grüsse\01-MfG-DE.txt",
        @"4-Grüsse\03-MfG-EN.txt",
        @"4-Grüsse\04-Bestätigung.txt",
        @"4-Grüsse\MfG-FR.txt",
        @"5-SQL\03-Suche nach Feld-Namen.txt",
        @"6-Weblinks\www.google.com.url",
    ];

    static List<Snippet> Library()
    {
        var result = new List<Snippet>();
        foreach (var folder in Files.Select(f => Path.GetDirectoryName(f)!).Distinct())
            result.Add(new Snippet { Name = folder, FullPath = folder, RelativePath = folder, Kind = SnippetKind.Folder });
        foreach (var file in Files)
            result.Add(new Snippet { Name = Path.GetFileNameWithoutExtension(file), FullPath = file, RelativePath = file, Kind = SnippetKind.Text });
        return result;
    }

    static List<string> Names(string query, Func<Snippet, string?>? content = null) =>
        SnippetSearch.Search(Library(), query, content).Select(h => h.Snippet.Name).ToList();

    [Theory]
    [InlineData("{tab} nächstes Feld Tasten", "feld", true)]
    [InlineData("{tab} nächstes Feld Tasten", "NACHSTES", true)]
    [InlineData("{tab} nächstes Feld Tasten", "tab feld", true)]
    [InlineData("{tab} nächstes Feld Tasten", "tab pause", false)]
    [InlineData("{tab} nächstes Feld Tasten", "  ", true)]
    public void Filter_needs_every_word_somewhere(string text, string query, bool expected) =>
        Assert.Equal(expected, SnippetSearch.MatchesAll(text, query));

    [Fact]
    public void Ignores_case_and_umlauts()
    {
        var names = Names("grusse");
        Assert.Equal("4-Grüsse", names[0]); // Ordner selbst zuerst (Treffer im Namen)
        Assert.Contains("01-MfG-DE", names);  // dann alles darin (Treffer im Ort)
    }

    [Fact]
    public void Equal_scores_keep_tree_order()
    {
        var names = Names("grusse");
        Assert.Equal(["4-Grüsse", "01-MfG-DE", "03-MfG-EN", "04-Bestätigung", "MfG-FR"], names);
    }

    [Fact]
    public void All_tokens_must_match()
    {
        var names = Names("mfg en");
        Assert.Equal(["03-MfG-EN"], names);
    }

    [Fact]
    public void Prefers_name_start_over_word_start_over_contains()
    {
        // "sehr" am Anfang, "suche" am Anfang eines Wortes, "uche" mitten im Wort
        Assert.Equal("Sehr geehrte Frau", Names("sehr")[0]);
        Assert.Equal("03-Suche nach Feld-Namen", Names("suche")[0]);

        var hits = SnippetSearch.Search(Library(), "suche");
        var containsHits = SnippetSearch.Search(Library(), "uche");
        Assert.True(hits[0].Score > containsHits[0].Score);
    }

    [Fact]
    public void Highlights_match_in_name()
    {
        var hit = SnippetSearch.Search(Library(), "bestatigung").Single();
        Assert.Equal("04-Bestätigung", hit.Snippet.Name);
        Assert.Equal(3, hit.MatchStart);
        Assert.Equal("Bestätigung".Length, hit.MatchLength);
    }

    [Fact]
    public void Highlights_later_word_start()
    {
        var hit = SnippetSearch.Search(Library(), "de").First(h => h.Snippet.Name == "01-MfG-DE");
        Assert.Equal("01-MfG-".Length, hit.MatchStart);
    }

    [Fact]
    public void Searches_content_only_with_provider()
    {
        string? Content(Snippet s) => s.Name == "MfG-FR" ? "Meilleures salutations" : null;

        Assert.Empty(Names("salutations"));
        Assert.Equal(["MfG-FR"], Names("salutations", Content));
    }

    [Fact]
    public void Content_hits_rank_below_name_hits()
    {
        string? Content(Snippet s) => s.Name == "MfG-FR" ? "Sehr geehrte Damen und Herren" : null;

        var names = Names("sehr", Content);
        Assert.Equal(["Sehr geehrte Frau", "MfG-FR"], names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_query_returns_nothing(string query) => Assert.Empty(Names(query));

    [Fact]
    public void Respects_max_results()
    {
        Assert.Equal(2, SnippetSearch.Search(Library(), "-", maxResults: 2).Count);
    }
}
