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
    public void Comment_lines_between_tags_add_no_steps() =>
        Assert.Equal("{focus,#name}«Max»{tab}«Müller»{enter}",
            Show("{# Erst der Name}\r\n{focus,#name}Max\r\n  {# dann Nachname }  \r\n{tab}Müller\r\n{# und ab\r\nüber zwei Zeilen}\r\n{enter}"));

    [Theory]
    [InlineData("Hallo\r\n{# Notiz}\r\nWelt", "Hallo\r\nWelt")]
    [InlineData("Hallo\n\t{# eingerückt}  \nWelt", "Hallo\nWelt")]
    [InlineData("{# ganz oben}\r\nHallo", "Hallo")]
    [InlineData("Hallo\r\n{# ganz unten}", "Hallo")]
    [InlineData("Hallo\r\n{# ganz unten}\r\n", "Hallo")]
    [InlineData("Hallo\r\n{# über\r\nzwei Zeilen}\r\nWelt", "Hallo\r\nWelt")]
    [InlineData("Hallo\r\n{# eins}\r\n{# zwei}\r\nWelt", "Hallo\r\nWelt")]
    [InlineData("{# nur das}", "")]
    [InlineData("Hallo {# Notiz}Welt", "Hallo Welt")]
    [InlineData("Hallo {# Notiz}\r\nWelt", "Hallo \r\nWelt")]
    public void Comments_are_left_out_with_their_line(string text, string expected)
    {
        var form = new FormBuilder();
        Assert.Equal(expected, KeyTags.MarkTags(text, form));
        Assert.Empty(form.Steps);
    }

    [Theory]
    [InlineData("Farbe {#ff0000")] // ohne } kein Kommentar
    [InlineData("{{ '{# kein Kommentar}' }}")] // im Skript ist es Text
    [InlineData("Hallo {name} {\"#a\": 1}")]
    public void Not_a_comment_stays(string text) => Assert.Equal(text, KeyTags.MarkTags(text, new FormBuilder()));

    [Fact]
    public void Steps_after_a_comment_keep_their_place_in_the_text()
    {
        const string text = "{# Notiz}\r\nMax{tab}";
        var form = new FormBuilder();
        var steps = KeyTags.SplitWithSources(KeyTags.MarkTags(text, form), form);
        Assert.Equal(["Max", "{tab}"], steps.Select(s => text.Substring(s.Source.Start, s.Source.Length)));
    }

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
    public void Each_tag_on_its_own_line_sends_no_line_breaks() =>
        Assert.Equal("{window,process=chrome}{set,#personen,\"5\"}{click,name=Zeit;type=button}",
            Show("{window,process=chrome}\r\n{set,#personen,\"5\"}\r\n{click,name=Zeit;type=button}\r\n"));

    [Fact]
    public void Text_on_its_own_line_between_tags_loses_only_the_layout_breaks()
    {
        Assert.Equal("{focus,#name}«Max Muster»{tab}«Müller»", Show("{focus,#name}\r\nMax Muster\r\n{tab}\r\nMüller"));
        Assert.Equal("{tab}«Max»{enter}", Show("{tab}\nMax\n{enter}"));
    }

    [Fact]
    public void Breaks_inside_the_text_are_still_sent() =>
        Assert.Equal("{focus,#text}«Grüsse\r\n\r\nMax»{tab}", Show("{focus,#text}\r\nGrüsse\r\n\r\nMax\r\n{tab}"));

    [Fact]
    public void Blank_lines_and_indentation_between_tags_are_layout_too() =>
        Assert.Equal("{tab}{enter}{tab}", Show("{tab}\r\n\r\n    {enter}\r\n\t{tab}"));

    [Fact]
    public void Spaces_at_a_tag_without_a_break_are_still_sent()
    {
        Assert.Equal("{tab}« »{tab}", Show("{tab} {tab}"));
        Assert.Equal("«Max »{tab}« Müller»", Show("Max {tab} Müller"));
    }

    [Fact]
    public void A_real_new_line_at_a_tag_is_a_key() =>
        Assert.Equal("{tab}{shift+enter}«Max»", Show("{tab}{shift+enter}\r\nMax"));

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

    // --- Fenster ---

    [Fact]
    public void Window_tag_brings_a_program_to_the_front()
    {
        var step = Assert.IsType<WindowStep>(Assert.Single(Steps("{window,crm}")));
        Assert.Equal("crm", step.Window.Any);
        Assert.Equal("kunden*", Assert.IsType<WindowStep>(Assert.Single(Steps("{window,title=kunden*}"))).Window.Title);
    }

    [Fact]
    public void Window_tag_combines_with_elements_and_text() =>
        Assert.Equal("{window,process=crm}{set,name=Suchen;type=combobox,\"crm\"}{enter}",
            Show("{window,process=crm}{set,name=Suchen;type=combobox,\"crm\"}{enter}"));

    [Fact]
    public void Element_tags_can_carry_their_window()
    {
        var step = Element("{set,name=Suchen;type=combobox;window=crm,\"x\"}");
        Assert.Equal("crm", step.Selector.Window?.Any);
    }

    [Theory]
    [InlineData("{window}")]
    [InlineData("{window,a,b}")]
    [InlineData("{window,class=x}")]
    public void Incomplete_window_tags_stay_text(string text) => Assert.Equal("«" + text + "»", Show(text));

    [Fact]
    public void Scripts_bring_windows_to_the_front() =>
        Assert.Equal("{window,crm}{focus,#suche}«x»", Show("{{ window('crm') + focus('#suche') + 'x' }}"));

    // --- Programme starten ---

    static StartStep Start(string tag) => Assert.IsType<StartStep>(Assert.Single(Steps(tag)));

    [Theory]
    [InlineData("{start,notepad}", "notepad.exe", "notepad")]
    [InlineData("{start,Notepad.EXE}", "Notepad.EXE", "Notepad")]
    [InlineData("{start,\"C:\\Program Files\\Notepad++\\notepad++.exe\"}", "C:\\Program Files\\Notepad++\\notepad++.exe", "notepad++")]
    [InlineData("{start,C:\\Tools\\crm}", "C:\\Tools\\crm.exe", "crm")]
    public void Start_tag_names_the_program_and_its_window(string tag, string program, string process)
    {
        var step = Start(tag);
        Assert.Equal(program, step.Program);
        Assert.Null(step.Arguments);
        Assert.Equal(process, step.Window.Process);
    }

    [Fact]
    public void Start_tag_passes_arguments() =>
        Assert.Equal("C:\\temp\\a, b.txt", Start("{start,notepad,\"C:\\temp\\a, b.txt\"}").Arguments);

    [Theory]
    [InlineData("{start,notepad}")]
    [InlineData("{start,notepad,\"C:\\temp\\log.txt\"}")]
    [InlineData("{start,\"C:\\Program Files\\Notepad++\\notepad++.exe\"}")]
    public void Start_tag_reads_back_as_written(string tag) => Assert.Equal(tag, Show(tag));

    [Fact]
    public void Start_tag_combines_with_elements_and_text() =>
        Assert.Equal("{start,notepad}«Hallo»{enter}«Welt»", Show("{start,notepad}Hallo{enter}Welt"));

    [Fact]
    public void Start_with_or_without_exe_is_the_same_program() =>
        Assert.Equal(Start("{start,msedge}").Window, Start("{start,msedge.exe}").Window);

    [Theory]
    [InlineData("{start,https://example.com/formular}", "https://example.com/formular")]
    [InlineData("{start, http://intranet/app?x=1 }", "http://intranet/app?x=1")]
    [InlineData("{start,\"https://example.com/?a=1,2\"}", "https://example.com/?a=1,2")]
    public void Start_with_a_web_address_opens_it(string tag, string url) =>
        Assert.Equal(new OpenUrlStep(url), Assert.Single(Steps(tag)));

    [Theory]
    [InlineData("{start,https://example.com}")]
    [InlineData("{start,\"https://example.com/?a=1,2\"}")]
    public void Start_with_a_web_address_reads_back_as_written(string tag) => Assert.Equal(tag, Show(tag));

    [Fact]
    public void Web_address_takes_no_arguments() =>
        Assert.Equal("«{start,https://example.com,\"x\"}»", Show("{start,https://example.com,\"x\"}"));

    [Theory]
    [InlineData("{start,ftp://example.com}")]
    [InlineData("{start,file://C:/temp/a.txt}")]
    public void Other_addresses_are_not_web_addresses(string text) =>
        Assert.IsNotType<OpenUrlStep>(Steps(text).FirstOrDefault());

    [Theory]
    [InlineData("{start}")]
    [InlineData("{start,notepad,a,b}")]
    [InlineData("{start,run.bat}")]
    [InlineData("{start,\"C:\\Users\\Me\\Desktop\\Crm.lnk\"}")]
    [InlineData("{start,script.ps1}")]
    [InlineData("{start,C:\\Tools\\}")]
    public void Start_only_takes_programs(string text) => Assert.Equal("«" + text + "»", Show(text));

    [Fact]
    public void Start_tag_errors_explain_why()
    {
        Assert.False(KeyTags.TryParse("start,run.bat", null, out _, out var error));
        Assert.Contains(".exe", error);
        Assert.False(KeyTags.TryParse("start", null, out _, out error));
        Assert.Contains("{start,notepad}", error);
    }

    [Fact]
    public void Scripts_and_clipboard_never_start_programs()
    {
        // Kein Helfer start() …
        Assert.Throws<SnippetScriptException>(() => Steps("{{ start('notepad') }}"));
        // … auch nicht über key() …
        Assert.Throws<SnippetScriptException>(() => Steps("{{ key('start,notepad') }}"));
        Assert.Throws<SnippetScriptException>(() => Steps("{{ key(clipboard) }}", clipboard: "start,notepad"));
        Assert.Throws<SnippetScriptException>(() => Steps("{{ key('start,https://example.com') }}"));
        // … und Ausgabe oder Zwischenablage mit {start,…} bleiben Text
        Assert.Equal([new TextStep("{start,notepad}{start,calc}")], Steps("{{ '{start,notepad}' }}{{ clipboard }}", clipboard: "{start,calc}"));
    }
}
