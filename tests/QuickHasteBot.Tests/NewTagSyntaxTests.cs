using QuickHasteBot.Core;
using static QuickHasteBot.Core.SyntaxKind;

namespace QuickHasteBot.Tests;

/// <summary>Farben und Fehler für {cursor}, {html}…{/html} und {include,…} im Editor.</summary>
public class NewTagSyntaxTests
{
    static (string, SyntaxKind)[] Spans(string text, Func<string, bool>? includeExists = null) =>
        SnippetSyntax.Tokenize(text, includeExists: includeExists).Select(s => (text.Substring(s.Start, s.Length), s.Kind)).ToArray();

    [Fact]
    public void Html_tags_are_colored_and_their_content_is_text() =>
        Assert.Equal([("{html}", Element), ("{{", ScriptDelimiter), ("user", Helper), ("}}", ScriptDelimiter), ("{/html}", Element), ("{tab}", Key)],
            Spans("{html}<b>{tab} {{ user }}</b>{/html}{tab}"));

    [Theory]
    [InlineData("{html}<b>x</b>", "{/html}")]
    [InlineData("<b>x</b>{/html}", "{html}")]
    public void Html_without_its_partner_is_an_error(string text, string hint)
    {
        var span = Assert.Single(SnippetSyntax.Tokenize(text));
        Assert.Equal(Error, span.Kind);
        Assert.Contains(hint, span.Error);
    }

    [Fact]
    public void Cursor_is_a_key_tag() =>
        Assert.Equal([("{cursor}", Key)], Spans("Guten Tag {cursor},"));

    [Fact]
    public void Keys_after_the_cursor_are_an_error_on_the_cursor()
    {
        var spans = SnippetSyntax.Tokenize("A{cursor}B{wait,100}C{tab}");
        Assert.Equal([Error, Key, Key], spans.Select(s => s.Kind));
        Assert.Contains("{tab}", spans[0].Error);
    }

    [Fact]
    public void Only_the_first_cursor_counts()
    {
        var spans = SnippetSyntax.Tokenize("A{cursor}B{cursor}");
        Assert.Equal([Key, Error], spans.Select(s => s.Kind));
    }

    [Fact]
    public void Include_is_colored_like_window_and_red_when_the_snippet_is_missing()
    {
        Assert.Equal([("{include,", Element), ("Grüsse/MfG", Argument), ("}", Element)], Spans("{include,Grüsse/MfG}", _ => true));
        var missing = Assert.Single(SnippetSyntax.Tokenize("{include,Gibts nicht}", includeExists: _ => false));
        Assert.Equal(Error, missing.Kind);
        Assert.Contains("Gibts nicht", missing.Error);
        Assert.Equal(Error, Assert.Single(SnippetSyntax.Tokenize("{include}")).Kind);
    }

    [Theory]
    [InlineData("{cursor}", "TagCursor")]
    [InlineData("{html}", "TagHtml")]
    [InlineData("{/html}", "TagHtml")]
    [InlineData("{include,a}", "TagInclude")]
    [InlineData("{{ selection }}", "TagSelection")]
    [InlineData("{{ ask('x') }}", "TagAsk")]
    public void New_tags_have_help(string text, string key) =>
        Assert.Equal(key, TagHelp.At(text, text.IndexOfAny(['c', 'h', 'i', 's', 'a', '/']) + 1)?.Key);

    [Fact]
    public void Scripts_cannot_include_or_open_html()
    {
        var context = SnippetScriptTests.Context with { Form = new FormBuilder() };
        Assert.Throws<SnippetScriptException>(() => SnippetScript.Render("{{ key('include,x') }}", context));
        Assert.Throws<SnippetScriptException>(() => SnippetScript.Render("{{ key('html') }}", context));
    }
}
