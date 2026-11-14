using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>In einem Formular ist Einrückung am Zeilenanfang nur Layout: {if}-Blöcke dürfen wie Code eingerückt sein.</summary>
public class IndentationTests
{
    /// <summary>Wie AppController: Tags markieren, Skripte rendern, zerlegen. Text in «», Tasten als Tag.</summary>
    static string Show(string text)
    {
        var form = new FormBuilder();
        var marked = SnippetScript.Render(KeyTags.MarkTags(text, form), SnippetScriptTests.Context with { Form = form });
        return string.Concat(KeyTags.Split(marked, form).Select(s => s is TextStep t ? "«" + t.Text + "»" : KeyTags.Describe(s)));
    }

    [Theory]
    // Text in einem Zweig, eingerückt wie Code
    [InlineData("{if,process=x}\r\n    Hallo\r\n{/if}", "{if,process=x}«Hallo»{/if}")]
    [InlineData("{if,process=x}\n\tHallo\n{else}\n\tTschüss\n{/if}", "{if,process=x}«Hallo»{else}«Tschüss»{/if}")]
    // Mehrere Zeilen: die gemeinsame Einrückung fällt weg, die relative bleibt
    [InlineData("{tab}\r\n    Punkt\r\n      Unterpunkt\r\n    Ende\r\n{enter}", "{tab}«Punkt\r\n  Unterpunkt\r\nEnde»{enter}")]
    // Leerzeilen zählen nicht, ihre Leerzeichen fallen weg
    [InlineData("{tab}\n    A\n  \n    B\n{tab}", "{tab}«A\n\nB»{tab}")]
    // Verschachtelt, mit eingerückten Tags
    [InlineData("{if,process=x}\n    A\n    {if,process=y}\n        B\n    {/if}\n{/if}", "{if,process=x}«A»{if,process=y}«B»{/if}{/if}")]
    // Text vor dem ersten Tag und ganz am Ende
    [InlineData("    Hallo\n{tab}\n    Welt", "«Hallo»{tab}«Welt»")]
    // Einrückung vor dem ersten Tag ganz am Anfang
    [InlineData("    {tab}Hallo", "{tab}«Hallo»")]
    // Das Beispiel im README
    [InlineData("{if,name=Send}\r\n    {ctrl+enter}\r\n{else}\r\n    Please check:\r\n      - the address\r\n{/if}",
        "{if,name=Send}{ctrl+enter}{else}«Please check:\r\n  - the address»{/if}")]
    public void Indentation_is_layout(string text, string expected) => Assert.Equal(expected, Show(text));

    [Theory]
    // Text, der mitten in der Zeile hinter einem Tag beginnt, bleibt ganz, wie er dasteht (z.B. ein aufgenommener Wert)
    [InlineData("{focus,#a}Strasse 1\n  c/o Meier\n{tab}", "{focus,#a}«Strasse 1\n  c/o Meier»{tab}")]
    // Ohne gemeinsame Einrückung ändert sich nichts
    [InlineData("{tab}\nA\n    B\n{tab}", "{tab}«A\n    B»{tab}")]
    // Echte Leerzeichen am Anfang: {space}
    [InlineData("{if,process=x}\n    {space,2}Hallo\n{/if}", "{if,process=x}{space,2}«Hallo»{/if}")]
    // Leerzeichen mitten im Text bleiben
    [InlineData("{tab}\n    A    B\n{tab}", "{tab}«A    B»{tab}")]
    public void Other_spaces_stay(string text, string expected) => Assert.Equal(expected, Show(text));

    [Theory]
    // Ohne Tags ist es ein normaler Text: Einrückung bleibt
    [InlineData("    Hallo\n    Welt")]
    [InlineData("{# Notiz}\n    Hallo")]
    // {cursor} und {html} allein machen kein Formular
    [InlineData("    Hallo {cursor}\n    Welt")]
    public void Plain_text_keeps_its_indentation(string text)
    {
        var form = new FormBuilder();
        var marked = KeyTags.MarkTags(text, form);
        Assert.StartsWith("    Hallo", KeyTags.StripMarkers(marked));
    }

    [Fact]
    public void Script_blocks_and_their_output_keep_their_indentation()
    {
        // Ein Template-String über mehrere Zeilen bleibt, wie er ist, und seine Ausgabe auch
        Assert.Equal("{tab}«a\n    b»", Show("{tab}\n    {{ `a\n    b` }}"));
        // Eingerückte Zeile nur aus Blöcken ohne Ausgabe: fällt samt Zeile weg
        Assert.Equal("{if,process=x}«Wert 1»{/if}", Show("{if,process=x}\n    {{ const v = 1 }}\n    Wert {{ v }}\n{/if}"));
    }

    [Fact]
    public void Html_blocks_are_left_alone()
    {
        var form = new FormBuilder();
        var marked = KeyTags.MarkTags("{tab}\n{html}\n    <b>A</b>\n{/html}", form);
        Assert.Contains("\n    <b>A</b>\n", marked);
    }

    [Fact]
    public void Comments_in_an_indented_block_still_vanish() =>
        Assert.Equal("{if,process=x}«A\nB»{/if}", Show("{if,process=x}\n    A\n    {# Notiz}\n    B\n{/if}"));

    [Fact]
    public void Steps_know_their_place_after_the_indentation_is_removed()
    {
        const string text = "{if,process=x}\r\n    Hallo\r\n    {tab}\r\n{/if}";
        var form = new FormBuilder();
        var steps = KeyTags.SplitWithSources(KeyTags.MarkTags(text, form), form);
        Assert.Equal(["{if,process=x}", "Hallo", "{tab}", "{/if}"], steps.Select(s => text.Substring(s.Source.Start, s.Source.Length)));
    }
}
