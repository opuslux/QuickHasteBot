using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class KeyTagsTests
{
    const ushort Tab = 0x09, Enter = 0x0D, Shift = 0x10, Ctrl = 0x11, Alt = 0x12, Win = 0x5B, Down = 0x28;

    /// <summary>Wie AppController: Tags markieren, Skripte rendern, an den Markern zerlegen.</summary>
    static IReadOnlyList<FormStep> Steps(string text, string clipboard = "")
    {
        var form = new FormBuilder();
        text = KeyTags.MarkTags(KeyTags.StripMarkers(text), form);
        text = SnippetScript.Render(text, SnippetScriptTests.Context with { Clipboard = clipboard, Form = form });
        return KeyTags.Split(text, form);
    }

    /// <summary>Text in «», Tasten als Tag, damit wörtlicher Text und echte Tasten unterscheidbar sind.</summary>
    static string Show(string text) =>
        string.Concat(Steps(text).Select(s => s is TextStep t ? "«" + t.Text + "»" : KeyTags.Describe(s)));

    static KeyStep Key(string tag) => Assert.IsType<KeyStep>(Assert.Single(Steps(tag)));

    [Fact]
    public void Text_and_keys_become_steps_in_order() =>
        Assert.Equal("«Max»{tab}«Müller»{tab}{tab}{enter}", Show("Max{tab}Müller{tab}{tab}{enter}"));

    [Theory]
    [InlineData("{TAB}")]
    [InlineData("{Tab}")]
    public void Case_does_not_matter(string tag) => Assert.Equal(Tab, Key(tag).Vk);

    [Theory]
    [InlineData("{tab,3}")]
    [InlineData("{tab 3}")]
    [InlineData("{tab, 3}")]
    public void Count_repeats_the_key(string tag) => Assert.Equal(3, Key(tag).Repeat);

    [Fact]
    public void Repeat_is_capped() => Assert.Equal(KeyTags.MaxRepeat, Key("{down,5000}").Repeat);

    [Fact]
    public void Combinations_hold_modifiers()
    {
        var key = Key("{ctrl+shift+end}");
        Assert.Equal(0x23, key.Vk);
        Assert.Equal(new[] { Ctrl, Shift }, key.Modifiers);

        Assert.Equal(Tab, Key("{shift+tab}").Vk);
        Assert.Equal((ushort)'A', Key("{ctrl+a}").Vk);
        Assert.Equal((ushort)'5', Key("{alt+5}").Vk);
    }

    [Theory]
    [InlineData("{win}", Win)]
    [InlineData("{alt}", Alt)]
    [InlineData("{ctrl}", Ctrl)]
    public void Modifiers_can_be_pressed_alone(string tag, ushort vk)
    {
        var key = Key(tag);
        Assert.Equal(vk, key.Vk);
        Assert.Empty(key.Modifiers);
    }

    [Theory]
    [InlineData("{win}", true)]
    [InlineData("{win+r}", true)]
    [InlineData("{alt+tab}", true)]
    [InlineData("{alt+f4}", true)]
    [InlineData("{alt+space}", true)]
    [InlineData("{alt}", true)]
    [InlineData("{apps}", true)]
    [InlineData("{shift+f10}", true)]
    [InlineData("{tab}", false)]
    [InlineData("{enter}", false)]
    [InlineData("{alt+down}", false)]
    [InlineData("{ctrl+a}", false)]
    public void Window_switching_keys_are_marked(string tag, bool switches) => Assert.Equal(switches, Key(tag).SwitchesWindow);

    [Fact]
    public void Arrows_and_navigation_are_extended_keys()
    {
        Assert.True(KeyTags.IsExtended(Down));
        Assert.True(KeyTags.IsExtended(Key("{del}").Vk));
        Assert.True(KeyTags.IsExtended(Win));
        Assert.False(KeyTags.IsExtended(Tab));
        Assert.False(KeyTags.IsExtended(Enter));
    }

    [Fact]
    public void Wait_delay_and_guard_take_arguments()
    {
        Assert.Equal(new WaitStep(100), Assert.Single(Steps("{wait,100}")));
        Assert.Equal(new WaitStep(250), Assert.Single(Steps("{wait 250}")));
        Assert.Equal(new WaitStep(KeyTags.MaxWait), Assert.Single(Steps("{wait,999999}")));
        Assert.Equal(new DelayStep(50), Assert.Single(Steps("{delay,50}")));
        Assert.Equal(new GuardStep(false), Assert.Single(Steps("{guard,off}")));
        Assert.Equal(new GuardStep(true), Assert.Single(Steps("{GUARD,On}")));
    }

    [Fact]
    public void Clear_selects_all_and_deletes() => Assert.Equal("{ctrl+a}{del}", Show("{clear}"));

    [Theory]
    [InlineData("{name}")]
    [InlineData("{a,b}")]
    [InlineData("{\"a\":1}")]
    [InlineData("{ tab }")]
    [InlineData("{tab,x}")]
    [InlineData("{wait}")]
    [InlineData("{wait,abc}")]
    [InlineData("{guard,maybe}")]
    [InlineData("{tab,0}")]
    [InlineData("{a}")]
    [InlineData("{foo+tab}")]
    [InlineData("function() { return 1; }")]
    public void Unknown_or_invalid_tags_stay_text(string text) => Assert.Equal("«" + text + "»", Show(text));

    [Fact]
    public void Line_breaks_stay_in_the_text_but_the_one_at_the_end_is_dropped() =>
        Assert.Equal("«Zeile 1\r\nZeile 2»{tab}«Ende»", Show("Zeile 1\r\nZeile 2{tab}Ende\r\n"));

    [Fact]
    public void A_final_line_break_after_a_key_disappears() =>
        Assert.Equal("«Max»{enter}", Show("Max{enter}\n"));

    [Fact]
    public void Text_without_tags_has_no_markers()
    {
        var form = new FormBuilder();
        var text = KeyTags.MarkTags("Hallo {name}, {{ 1 + 1 }}", form);
        Assert.False(KeyTags.HasMarkers(text));
        Assert.Empty(form.Steps);
    }

    // --- Zusammenspiel mit Skripten ---

    [Fact]
    public void Tags_between_script_blocks_keep_variables() =>
        Assert.Equal("«Max»{tab}«MAX»", Show("{{ const n = 'Max'; n }}{tab}{{ n.toUpperCase() }}"));

    [Fact]
    public void Tags_inside_script_blocks_are_text() =>
        Assert.Equal("«{tab}»", Show("{{ \"{tab}\" }}"));

    [Fact]
    public void Scripts_press_keys_with_key()
    {
        Assert.Equal("«Max»{tab}«Müller»{tab}«m@example.com»{enter}", Show("{{ ['Max', 'Müller', 'm@example.com'].join(key('tab')) + key('enter') }}"));
        Assert.Equal("{down,3}{shift+tab}{wait,500}", Show("{{ key('down', 3) + key('shift+tab') + wait(500) }}"));
        Assert.Equal("{guard,off}{wait,200}", Show("{{ key('guard,off') + key('wait,200') }}"));
    }

    [Fact]
    public void Script_output_and_clipboard_never_become_keys()
    {
        var steps = Steps("{{ clipboard }}{tab}{{ '{enter}' }}", clipboard: "{enter}0");
        Assert.Equal(3, steps.Count);
        Assert.Equal(new TextStep("{enter}0"), steps[0]); // Marker-Zeichen entfernt, die Ziffer ist nur noch Text
        Assert.IsType<KeyStep>(steps[1]);
        Assert.Equal(new TextStep("{enter}"), steps[2]);
    }

    [Fact]
    public void Forged_markers_in_the_snippet_are_removed() =>
        Assert.Equal("«a0b»{tab}", Show("a0b{tab}"));

    [Fact]
    public void Unknown_key_in_a_script_is_an_error()
    {
        var ex = Assert.Throws<SnippetScriptException>(() => Steps("{{ key('tabb') }}"));
        Assert.Contains("tabb", ex.Message);
    }

    [Fact]
    public void Key_without_key_tags_says_they_are_disabled()
    {
        var ex = Assert.Throws<SnippetScriptException>(() => SnippetScript.Render("{{ key('tab') }}", SnippetScriptTests.Context));
        Assert.Contains("disabled", ex.Message);
    }

    [Fact]
    public void Preview_shows_readable_tags()
    {
        var form = new FormBuilder();
        var text = SnippetScript.Render("{{ 'a' + key('tab') + 'b' + key('ctrl+a', 2) }}", SnippetScriptTests.Context with { Form = form });
        Assert.Equal("a{tab}b{ctrl+a,2}", KeyTags.Preview(text, form));
    }

    // --- Element-Tags ---

    static ElementStep Element(string tag) => Assert.IsType<ElementStep>(Assert.Single(Steps(tag)));

    [Fact]
    public void Element_tags_focus_click_and_set()
    {
        var focus = Element("{focus,#email}");
        Assert.Equal(ElementAction.Focus, focus.Action);
        Assert.Equal("email", focus.Selector.Id);

        var click = Element("{click,name=Absenden;type=button}");
        Assert.Equal(ElementAction.Click, click.Action);
        Assert.Equal("Absenden", click.Selector.Name);
        Assert.Equal("Button", click.Selector.Type);

        var set = Element("{set,#land,\"Österreich\"}");
        Assert.Equal(ElementAction.Set, set.Action);
        Assert.Equal("Österreich", set.Value);

        Assert.Equal("a, b}", Element("{set,#x,\"a, b}\"}").Value);
        Assert.Equal("Absenden", Element("{click Absenden}").Selector.Name);
    }

    [Theory]
    [InlineData("{focus}")]
    [InlineData("{set,#email}")]
    [InlineData("{click,#a,#b}")]
    [InlineData("{focus,type=banana}")]
    public void Incomplete_element_tags_stay_text(string text) => Assert.Equal("«" + text + "»", Show(text));

    [Fact]
    public void Element_tags_mix_with_text_and_keys() =>
        Assert.Equal("{focus,#vorname}«Max»{tab}«Müller»{set,#land,\"Schweiz\"}{click,name=Senden;type=button}",
            Show("{focus,#vorname}Max{tab}Müller{set,#land,Schweiz}{click,name=Senden;type=button}"));

    [Fact]
    public void Scripts_use_focus_click_and_set() =>
        Assert.Equal("{focus,#email}«m@example.com»{set,#ok,\"on\"}{click,name=Senden}",
            Show("{{ focus('#email') + 'm@example.com' + set('#ok', 'on') + click('Senden') }}"));

    [Fact]
    public void Bad_selector_in_a_script_is_an_error()
    {
        var ex = Assert.Throws<SnippetScriptException>(() => Steps("{{ focus('type=banana') }}"));
        Assert.Contains("banana", ex.Message);
    }

    [Theory]
    [InlineData(ElementAction.Focus, "#email", null, "{focus,#email}")]
    [InlineData(ElementAction.Set, "#email", "a,b", "{set,#email,\"a,b\"}")]
    [InlineData(ElementAction.Set, "#ok", "on", "{set,#ok,\"on\"}")]
    [InlineData(ElementAction.Click, "name=Weiter, bitte;type=button", null, "{click,\"name=Weiter, bitte;type=button\"}")]
    [InlineData(ElementAction.Set, "#q", "Zitat \"x\"", "{set,#q,\"Zitat x\"}")]
    public void Picker_tags_parse_back_to_the_same_step(ElementAction action, string selectorText, string? value, string expected)
    {
        Assert.True(ElementSelector.TryParse(selectorText, out var selector, out _));
        var tag = KeyTags.ElementTag(action, selector, value);
        Assert.Equal(expected, tag);

        var step = Element(tag);
        Assert.Equal(action, step.Action);
        Assert.Equal(selector.ToString(), step.Selector.ToString());
    }
}
