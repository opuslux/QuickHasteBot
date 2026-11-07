using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>Jeder Schritt kennt seine Stelle im Bausteintext, damit der Editor beim Schritt-für-Schritt-Lauf markieren kann.</summary>
public class StepSourceTests
{
    /// <summary>Wie AppController: Tags markieren, Skripte rendern, zerlegen. Je Schritt der Text an seiner Stelle.</summary>
    static string[] Sources(string text)
    {
        var form = new FormBuilder();
        var marked = KeyTags.MarkTags(text, form);
        marked = SnippetScript.Render(marked, SnippetScriptTests.Context with { Form = form });
        return KeyTags.SplitWithSources(marked, form).Select(s => text.Substring(s.Source.Start, s.Source.Length)).ToArray();
    }

    [Fact]
    public void Tags_know_their_exact_place() =>
        Assert.Equal(["Max", "{tab}", "Müller", "{shift+tab,2}", "{enter}"], Sources("Max{tab}Müller{shift+tab,2}{enter}"));

    [Fact]
    public void Text_between_tags_is_one_step() =>
        Assert.Equal(["Grüsse\r\nMax ", "{tab}", "Ende"], Sources("Grüsse\r\nMax {tab}Ende"));

    [Fact]
    public void The_dropped_line_break_at_the_end_is_not_part_of_the_last_step() =>
        Assert.Equal(["MfG", "{tab}", "Ende"], Sources("MfG{tab}Ende\r\n"));

    [Fact]
    public void Layout_breaks_at_tags_are_not_part_of_the_text_step() =>
        Assert.Equal(["{tab}", "Max", "{enter}"], Sources("{tab}\r\nMax\r\n{enter}"));

    [Fact]
    public void Tags_with_quoted_arguments_cover_the_whole_tag() =>
        Assert.Equal(["{set,#x,\"a, b}\"}", "{click,name=Senden}"], Sources("{set,#x,\"a, b}\"}{click,name=Senden}"));

    [Fact]
    public void A_tag_with_several_steps_marks_the_tag_for_each() =>
        Assert.Equal(["{clear}", "{clear}", "neu"], Sources("{clear}neu"));

    [Fact]
    public void Script_output_counts_as_text_with_its_block() =>
        Assert.Equal(["Hallo {{ 'Max' }}", "{tab}", "x"], Sources("Hallo {{ 'Max' }}{tab}x"));

    [Fact]
    public void Keys_from_a_script_get_the_whole_block()
    {
        const string block = "{{ ['a', 'b'].join(key('tab')) + key('enter') }}";
        Assert.Equal([block, block, block, block], Sources(block));
    }

    [Fact]
    public void Text_before_a_block_and_its_output_are_one_step() =>
        Assert.Equal(["x{{ 'a' + key('tab') }}", "{{ 'a' + key('tab') }}", "y"], Sources("x{{ 'a' + key('tab') }}y"));

    [Fact]
    public void Places_after_tags_and_blocks_stay_right()
    {
        // Tags werden kürzer (Marker), Blöcke länger oder leer: alles danach muss trotzdem stimmen
        Assert.Equal(["{tab}", "{{ 'lang lang lang' }}", "{enter}", "a{{ '' }}b", "{wait,100}", "c"],
            Sources("{tab}{{ 'lang lang lang' }}{enter}a{{ '' }}b{wait,100}c"));
    }

    [Fact]
    public void Tags_inside_a_block_stay_text_of_the_block() =>
        Assert.Equal(["{{ '{tab}' }}", "{enter}"], Sources("{{ '{tab}' }}{enter}"));

    [Fact]
    public void Places_map_back_to_text_with_marker_characters()
    {
        // Der Editor zeigt den Text mit den Marker-Zeichen, die Stellen gelten im Text ohne
        const string original = "ab{tab}";
        Assert.Equal(0, KeyTags.UnstrippedOffset(original, 0));
        Assert.Equal(3, KeyTags.UnstrippedOffset(original, 2));
        Assert.Equal(original.Length, KeyTags.UnstrippedOffset(original, 7));
        Assert.Equal(4, KeyTags.UnstrippedOffset("text", 4));
        Assert.Equal(4, KeyTags.UnstrippedOffset("text", 10));
    }

    [Fact]
    public void Split_still_gives_the_same_steps()
    {
        var form = new FormBuilder();
        var text = KeyTags.MarkTags("a{tab}b\n", form);
        Assert.Equal(KeyTags.SplitWithSources(text, form).Select(s => s.Step), KeyTags.Split(text, form));
    }
}
