using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>{include,…}: ein anderer Baustein an dieser Stelle, mit seinen Tags und Skripten.</summary>
public class SnippetIncludesTests
{
    static readonly Dictionary<string, string> Snippets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Signatur"] = "Freundliche Grüsse\r\nMax Muster\r\n",
        ["Formular"] = "{focus,#name}Max{tab}",
        ["Skript"] = "{{ 6 * 7 }}",
        ["Aussen"] = "A {include,Innen} C",
        ["Innen"] = "B",
        ["Ping"] = "{include,Pong}",
        ["Pong"] = "{include,Ping}",
        ["Selbst"] = "x {include,Selbst}",
    };

    static SnippetIncludes.Included? Resolve(string path) =>
        Snippets.TryGetValue(path, out var text) ? new(path, text) : null;

    static string Expand(string text, FormBuilder? form = null) => SnippetIncludes.Expand(text, Resolve, form);

    [Fact]
    public void Replaces_the_tag_without_the_final_line_break() =>
        Assert.Equal("Danke!\r\nFreundliche Grüsse\r\nMax Muster", Expand("Danke!\r\n{include,Signatur}"));

    [Fact]
    public void Nested_includes_are_expanded() =>
        Assert.Equal("A B C", Expand("{include,Aussen}"));

    [Fact]
    public void Text_without_includes_stays_the_same()
    {
        const string text = "Hallo {tab} {{ include }} {name}";
        Assert.Same(text, Expand(text));
    }

    [Theory]
    [InlineData("{{ '{include,Signatur}' }}")]
    [InlineData("{html}{include,Signatur}{/html}")]
    [InlineData("{# {include,Signatur} }")]
    public void Scripts_html_blocks_and_comments_are_left_alone(string text) =>
        Assert.Equal(text, Expand(text));

    [Fact]
    public void Unknown_snippet_is_an_error()
    {
        var ex = Assert.Throws<IncludeException>(() => Expand("{include,Gibts/nicht}"));
        Assert.Equal(IncludeProblem.NotFound, ex.Problem);
        Assert.Equal("Gibts/nicht", ex.Detail);
    }

    [Fact]
    public void A_cycle_is_an_error_with_the_chain()
    {
        var ex = Assert.Throws<IncludeException>(() => Expand("{include,Ping}"));
        Assert.Equal(IncludeProblem.Cycle, ex.Problem);
        Assert.Equal("Ping → Pong → Ping", ex.Detail);
    }

    [Fact]
    public void A_snippet_including_itself_is_a_cycle() =>
        Assert.Equal(IncludeProblem.Cycle,
            Assert.Throws<IncludeException>(() => SnippetIncludes.Expand("{include,Selbst}", Resolve, self: "Selbst")).Problem);

    [Fact]
    public void Too_deep_is_an_error()
    {
        var deep = Enumerable.Range(0, SnippetIncludes.MaxDepth + 2).ToDictionary(i => "T" + i, i => "{include,T" + (i + 1) + "}");
        var ex = Assert.Throws<IncludeException>(() =>
            SnippetIncludes.Expand("{include,T0}", path => deep.TryGetValue(path, out var t) ? new(path, t) : new(path, "Ende"), null));
        Assert.Equal(IncludeProblem.TooDeep, ex.Problem);
    }

    [Fact]
    public void Tags_and_scripts_of_the_included_snippet_run()
    {
        var form = new FormBuilder();
        var text = KeyTags.MarkTags(Expand("{include,Formular}{include,Skript}", form), form);
        text = SnippetScript.Render(text, SnippetScriptTests.Context with { Form = form });
        Assert.Equal(["{focus,#name}", "Max", "{tab}", "42"], KeyTags.Split(text, form).Select(KeyTags.Describe));
    }

    [Fact]
    public void Steps_from_an_included_snippet_come_from_its_tag()
    {
        const string source = "Hallo {include,Formular} Ende";
        var form = new FormBuilder();
        var marked = KeyTags.MarkTags(Expand(source, form), form);
        var steps = KeyTags.SplitWithSources(marked, form);
        Assert.Equal(["Hallo ", "{include,Formular}", "{include,Formular}", "{include,Formular}", " Ende"],
            steps.Select(s => source.Substring(s.Source.Start, s.Source.Length)));
    }

    [Fact]
    public void Library_finds_snippets_by_folder_and_name()
    {
        using var dir = new TempDirectory();
        dir.File(@"3-Grüsse [Ctrl+Alt+G]\1-Freundliche Grüsse.txt", "MfG");
        dir.File(@"3-Grüsse [Ctrl+Alt+G]\Archiv\Alt.txt", "alt");
        dir.File(@"Notiz.txt", "notiz");
        dir.File(@"Bild.png", "");
        using var library = new SnippetLibrary(dir.Path, [".txt"]);

        Assert.Equal("1-Freundliche Grüsse", library.FindIncluded("3-Grüsse/1-Freundliche Grüsse")?.Name);
        Assert.Equal("1-Freundliche Grüsse", library.FindIncluded(@"grüsse\freundliche grüsse.txt")?.Name);
        Assert.Equal("Alt", library.FindIncluded("Grüsse/Archiv/Alt")?.Name);
        // Ohne Ordner: erst oben, dann überall
        Assert.Equal("Notiz", library.FindIncluded("Notiz")?.Name);
        Assert.Equal("Alt", library.FindIncluded("Alt")?.Name);
        Assert.Null(library.FindIncluded("Archiv/Alt"));
        Assert.Null(library.FindIncluded("Bild"));
        Assert.Null(library.FindIncluded("3-Grüsse"));
    }

    [Fact]
    public void Numbers_with_spaced_dash_may_be_left_out()
    {
        using var dir = new TempDirectory();
        dir.File(@"1 - Grüsse\01 - Freundliche Grüsse.txt", "MfG");
        dir.File(@"2 - E-Mail\Abwesenheit.txt", "weg");
        using var library = new SnippetLibrary(dir.Path, [".txt"]);

        Assert.Equal("01 - Freundliche Grüsse", library.FindIncluded("Grüsse/Freundliche Grüsse")?.Name);
        Assert.Equal("01 - Freundliche Grüsse", library.FindIncluded("1 - Grüsse/01 - Freundliche Grüsse.txt")?.Name);
        // Nur der Strich nach der Nummer fällt weg, der in "E-Mail" bleibt
        Assert.Equal("Abwesenheit", library.FindIncluded("E-Mail/Abwesenheit")?.Name);
    }
}
