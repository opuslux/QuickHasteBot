using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>"Einrückung ordnen" im Editor: rückt {if}-Blöcke ein, ohne zu ändern, was gesendet wird.</summary>
public class ScriptIndentTests
{
    /// <summary>Was gesendet würde, ohne Skripte auszuwerten: Text in «», Tasten als Tag.</summary>
    static string Sent(string text)
    {
        var form = new FormBuilder();
        var marked = KeyTags.MarkTags(text, form);
        return string.Concat(KeyTags.Split(marked, form).Select(s => s is TextStep t ? "«" + t.Text + "»" : KeyTags.Describe(s)));
    }

    static void AssertFormats(string text, string expected)
    {
        var formatted = ScriptIndent.Format(text);
        Assert.Equal(expected, formatted);
        Assert.Equal(Sent(text), Sent(formatted));
        Assert.Equal(formatted, ScriptIndent.Format(formatted)); // zweimal = einmal
    }

    [Fact]
    public void Indents_if_blocks() => AssertFormats(
        "{if,name=Senden}\n{ctrl+enter}\n{elseif,name=Antworten}\n{click,name=Antworten}\n{else}\nBitte prüfen\n{/if}",
        "{if,name=Senden}\n    {ctrl+enter}\n{elseif,name=Antworten}\n    {click,name=Antworten}\n{else}\n    Bitte prüfen\n{/if}");

    [Fact]
    public void Nested_blocks_and_crooked_indentation() => AssertFormats(
        "  {tab}\r\n{if,process=a}\r\n  A\r\n      {if,process=b}\r\n B\r\n  {/if}\r\n\t{/if}\r\nEnde",
        "{tab}\r\n{if,process=a}\r\n    A\r\n    {if,process=b}\r\n        B\r\n    {/if}\r\n{/if}\r\nEnde");

    [Fact]
    public void Text_keeps_its_relative_indentation() => AssertFormats(
        "{if,process=a}\n  Punkt\n    Unterpunkt\n\n  Ende\n{/if}",
        "{if,process=a}\n    Punkt\n      Unterpunkt\n\n    Ende\n{/if}");

    [Fact]
    public void Text_after_a_tag_on_the_same_line_stays() => AssertFormats(
        "{if,process=a}\n{focus,#a}Strasse 1\n  c/o Meier\n{/if}",
        "{if,process=a}\n    {focus,#a}Strasse 1\n  c/o Meier\n{/if}");

    [Fact]
    public void Comments_and_script_blocks_move_along() => AssertFormats(
        "{if,process=a}\n{# Notiz}\n{{ const x = `a\n  b` }}\nWert {{ x }}\n{/if}\n{# ganz unten}",
        "{if,process=a}\n    {# Notiz}\n    {{ const x = `a\n  b` }}\n    Wert {{ x }}\n{/if}\n{# ganz unten}");

    [Fact]
    public void Html_content_stays() => AssertFormats(
        "{if,process=a}\n{html}\n<p>\n  Hallo\n</p>\n{/html}\n{/if}",
        "{if,process=a}\n    {html}\n<p>\n  Hallo\n</p>\n    {/html}\n{/if}");

    [Theory]
    [InlineData("    Hallo\n  Welt")] // ohne Tags: Einrückung ist Text
    [InlineData("{# Notiz}\n    Hallo")]
    [InlineData("")]
    public void Plain_text_is_left_alone(string text) => Assert.Equal(text, ScriptIndent.Format(text));

    [Fact]
    public void Only_the_selected_lines()
    {
        const string text = "{if,process=a}\n{tab}\n{enter}\n{/if}";
        var start = text.IndexOf("{enter}", StringComparison.Ordinal);
        Assert.Equal("{if,process=a}\n{tab}\n    {enter}\n{/if}", ScriptIndent.Apply(text, ScriptIndent.Changes(text, start, start)));
    }

    [Fact]
    public void A_selected_text_line_takes_its_whole_text_along()
    {
        // Nur "B" markiert: A und C rücken mit, sonst änderte sich die gemeinsame Einrückung und damit der getippte Text
        const string text = "{if,process=a}\n  A\n  B\n  C\n{/if}";
        var start = text.IndexOf('B');
        var formatted = ScriptIndent.Apply(text, ScriptIndent.Changes(text, start, start + 1));
        Assert.Equal("{if,process=a}\n    A\n    B\n    C\n{/if}", formatted);
        Assert.Equal(Sent(text), Sent(formatted));
    }

    [Fact]
    public void A_selection_ending_at_a_line_start_leaves_that_line()
    {
        const string text = "{if,process=a}\n{tab}\n{enter}\n{/if}";
        var end = text.IndexOf("{enter}", StringComparison.Ordinal);
        Assert.Equal("{if,process=a}\n    {tab}\n{enter}\n{/if}", ScriptIndent.Apply(text, ScriptIndent.Changes(text, 0, end)));
    }

    [Theory]
    [InlineData("{if,name=Senden}", true)]
    [InlineData("    {elseif,process=x}  ", true)]
    [InlineData("{else}", true)]
    [InlineData("Text {if,js=\"a == 1\"}", true)]
    [InlineData("{if,process=x}{tab}", false)]
    [InlineData("{/if}", false)]
    [InlineData("{# {if,process=x}}", false)]
    [InlineData("Hallo", false)]
    public void Opens_block(string line, bool expected) => Assert.Equal(expected, ScriptIndent.OpensBlock(line));

    [Theory]
    [InlineData("    {/if}", true)]
    [InlineData("\t{else}", true)]
    [InlineData("{elseif,process=x}", true)]
    [InlineData("{if,process=x}", false)]
    [InlineData("{/if} Text", false)]
    [InlineData("", false)]
    public void Closes_block(string line, bool expected) => Assert.Equal(expected, ScriptIndent.ClosesBlock(line));

    /// <summary>Alle Beispiele: Einrücken ändert nie, was gesendet wird.</summary>
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void Formatting_the_examples_sends_the_same(string language)
    {
        foreach (var (path, text) in SampleStructure.Files(language).Where(f => f.Path.EndsWith(".txt", StringComparison.Ordinal)))
        {
            var formatted = ScriptIndent.Format(text);
            Assert.True(Sent(text) == Sent(formatted), path);
            Assert.True(formatted == ScriptIndent.Format(formatted), path);
        }
    }
}
