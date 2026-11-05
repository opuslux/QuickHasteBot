using QuickHasteBot.Core;
using static QuickHasteBot.Core.SyntaxKind;

namespace QuickHasteBot.Tests;

public class SnippetSyntaxTests
{
    /// <summary>Die farbigen Stellen als (Text, Art), damit die Erwartungen lesbar bleiben.</summary>
    static (string, SyntaxKind)[] Spans(string text, bool keyTags = true, bool scripts = true) =>
        SnippetSyntax.Tokenize(text, keyTags, scripts).Select(s => (text.Substring(s.Start, s.Length), s.Kind)).ToArray();

    [Fact]
    public void Key_tags_are_colored_as_a_whole()
    {
        Assert.Equal([("{tab}", Key), ("{SHIFT+TAB}", Key), ("{wait,500}", Key), ("{down 3}", Key), ("{guard,off}", Key)],
            Spans("Max{tab}{SHIFT+TAB}Müller{wait,500}{down 3}{guard,off}"));
    }

    [Fact]
    public void Plain_braces_stay_text()
    {
        Assert.Empty(Spans("Hallo {name}, {\"a\":1} { end } {a,b}"));
    }

    [Fact]
    public void Known_tags_with_wrong_arguments_are_errors_with_a_reason()
    {
        var spans = SnippetSyntax.Tokenize("{tab,x}{wait}{focus}{clear,1}");
        Assert.Equal([Error, Error, Error, Error], spans.Select(s => s.Kind));
        Assert.Contains("{tab,3}", spans[0].Error);
        Assert.Contains("milliseconds", spans[1].Error);
        Assert.Contains("element", spans[2].Error);
        Assert.Contains("no arguments", spans[3].Error);
    }

    [Fact]
    public void Element_tags_split_into_name_selector_and_value()
    {
        Assert.Equal([("{set,", Element), ("#land", Argument), (",", Element), ("\"Öster, reich\"", SyntaxKind.String), ("}", Element)],
            Spans("{set,#land,\"Öster, reich\"}"));
        Assert.Equal([("{window,", Element), ("crm", Argument), ("}", Element)], Spans("{window,crm}"));
    }

    [Fact]
    public void Scripts_are_colored_inside_and_tags_in_them_are_not()
    {
        Assert.Equal(
        [
            ("{{", ScriptDelimiter), ("const", Keyword), ("\"{tab}\"", SyntaxKind.String), ("date", Helper), ("'d'", SyntaxKind.String), ("14", Number),
            ("// Kommentar", Comment), ("}}", ScriptDelimiter),
        ], Spans("{{ const t = \"{tab}\" + date('d', 14).trim // Kommentar\n}}"));
    }

    [Fact]
    public void Helper_names_after_a_dot_are_properties()
    {
        Assert.Equal([("{{", ScriptDelimiter), ("key", Helper), ("\"tab\"", SyntaxKind.String), ("}}", ScriptDelimiter)],
            Spans("{{ [a, b].join(key(\"tab\")).set }}"));
    }

    [Fact]
    public void Unclosed_script_and_switched_off_settings_stay_text()
    {
        Assert.Equal([("{tab}", Key)], Spans("{{ 1 + {tab}"));
        Assert.Equal([("{tab}", Key)], Spans("{{ 1 }}{tab}", scripts: false));
        Assert.Equal([("{{", ScriptDelimiter), ("1", Number), ("}}", ScriptDelimiter)], Spans("{{ 1 }}{tab}", keyTags: false));
    }
}
