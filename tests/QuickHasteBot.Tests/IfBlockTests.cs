using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using static QuickHasteBot.Tests.TestSteps;

namespace QuickHasteBot.Tests;

/// <summary>
/// {if,…} {elseif,…} {else} {/if}: Tags lesen, Blöcke prüfen, und im Lauf den richtigen Zweig nehmen. Im Lauf nur mit
/// js-Bedingungen (ein Fake entscheidet) und Pausen, damit nichts getippt und kein Fenster gesucht wird.
/// </summary>
public class IfBlockTests
{
    static IReadOnlyList<FormStep> Steps(string text)
    {
        var form = new FormBuilder();
        text = KeyTags.MarkTags(text, form);
        return KeyTags.Split(text, form);
    }

    static string Show(string text) =>
        string.Concat(Steps(text).Select(s => s is TextStep t ? "«" + t.Text + "»" : KeyTags.Describe(s)));

    static IfCondition Condition(string tag) =>
        Assert.IsType<IfStep>(Assert.Single(Steps(tag))).Condition;

    // --- Tags lesen ---

    [Fact]
    public void Element_condition_uses_the_selectors_of_click()
    {
        var condition = Condition("{if,name=Senden}");
        Assert.Equal("Senden", condition.Element!.Name);
        Assert.Null(condition.Window);
        Assert.False(condition.Not);
        Assert.Equal(IfCondition.DefaultWait, condition.Wait);

        var inWindow = Condition("{if,window=outlook;name=Senden > name=OK;type=button}");
        Assert.Equal("outlook", inWindow.Element!.Window!.Any);
        Assert.Equal("Senden", inWindow.Element.Within!.Name);
        Assert.Equal("Senden", Condition("{if,Senden}").Element!.Name);
    }

    [Theory]
    [InlineData("{if,process=outlook}", "outlook", null, null)]
    [InlineData("{if,process=OUTLOOK.EXE}", "OUTLOOK", null, null)]
    [InlineData("{if,title=*Entwurf*}", null, "*Entwurf*", null)]
    [InlineData("{if,window=crm}", null, null, "crm")]
    [InlineData("{if,window=title=Kunden*}", null, "Kunden*", null)]
    public void Window_alone_checks_only_the_window(string tag, string? process, string? title, string? any)
    {
        var condition = Condition(tag);
        Assert.Null(condition.Element);
        Assert.Equal(new WindowSelector(process, title, any), condition.Window);
    }

    [Fact]
    public void Not_wait_and_script_are_read()
    {
        var condition = Condition("{elseif,not,#ergebnis,wait=3000}");
        Assert.True(condition.Not);
        Assert.Equal("ergebnis", condition.Element!.Id);
        Assert.Equal(3000, condition.Wait);
        Assert.Equal(KeyTags.MaxWait, Condition("{if,name=A,wait=99999}").Wait);
        Assert.Equal(0, Condition("{if,name=A,wait=0}").Wait);

        var script = Condition("{if,js=\"a == 'x, y' && b\"}");
        Assert.Equal("a == 'x, y' && b", script.Script);
        Assert.True(Condition("{if,not,js=\"ok\"}").Not);
    }

    [Theory]
    [InlineData("{if}", "needs a condition")]
    [InlineData("{if,not}", "needs a condition")]
    [InlineData("{if,name=A,name=B}", "checks one thing")]
    [InlineData("{if,name=A,wait=x}", "wait= needs milliseconds")]
    [InlineData("{if,js=}", "js= needs an expression")]
    [InlineData("{if,js=\"a\",wait=100}", "wait= only works")]
    [InlineData("{if,process=}", "window needs")]
    [InlineData("{if,name=A;type=nix}", "unknown element type")]
    [InlineData("{else,name=A}", "takes no condition")]
    public void Wrong_conditions_say_why(string tag, string error)
    {
        Assert.False(KeyTags.TryParse(tag[1..^1], null, out _, out var message));
        Assert.Contains(error, message);
    }

    [Theory]
    [InlineData("{if,name=Senden}")]
    [InlineData("{elseif,not,name=Senden,wait=2000}")]
    [InlineData("{if,process=outlook}")]
    [InlineData("{if,title=*Entwurf*}")]
    [InlineData("{if,window=crm}")]
    [InlineData("{if,js=\"r.code == 0\"}")]
    [InlineData("{if,\"name=Weiter, bitte\"}")]
    [InlineData("{else}")]
    [InlineData("{/if}")]
    public void Describe_writes_the_tag_as_it_was(string tag) =>
        Assert.Equal(tag, KeyTags.Describe(Assert.Single(Steps(tag))));

    [Fact]
    public void Tags_on_their_own_lines_add_no_line_breaks() =>
        Assert.Equal("{if,name=Senden}{ctrl+enter}{elseif,process=outlook}«Hallo»{else}{esc}{/if}«Ende»",
            Show("{if,name=Senden}\r\n{ctrl+enter}\r\n{elseif,process=outlook}\r\nHallo\r\n{else}\r\n  {esc}\r\n{/if}\r\nEnde"));

    [Fact]
    public void Text_like_if_without_a_known_form_stays_text() =>
        Assert.Equal("«{iffy} {else if} {/iff}»", Show("{iffy} {else if} {/iff}"));

    [Fact]
    public void Scripts_cant_make_if_tags()
    {
        var form = new FormBuilder();
        var ex = Assert.Throws<SnippetScriptException>(() =>
            SnippetScript.Render("{{ key('if,name=A') }}", SnippetScriptTests.Context with { Form = form }));
        Assert.Contains("only work in the snippet text", ex.Message);
    }

    // --- Blöcke prüfen ---

    [Theory]
    [InlineData("{if,name=A}a{/if}")]
    [InlineData("{if,name=A}a{else}b{/if}")]
    [InlineData("{if,name=A}a{elseif,name=B}b{elseif,name=C}c{else}d{/if}")]
    [InlineData("{if,name=A}{if,name=B}ab{else}a{/if}{else}{if,name=C}c{/if}{/if}")]
    [InlineData("Nur Text {tab}")]
    public void Matching_blocks_pass(string text) =>
        Assert.Null(KeyTags.CheckBlocks(Steps(text)));

    [Theory]
    [InlineData("{else}b", 0, "{else} without {if,…}")]
    [InlineData("a{elseif,name=B}b{/if}", 1, "{elseif,…} without {if,…}")]
    [InlineData("{/if}", 0, "{/if} without {if,…}")]
    [InlineData("{if,name=A}a{else}b{else}c{/if}", 4, "a second {else}")]
    [InlineData("{if,name=A}a{else}b{elseif,name=B}c{/if}", 4, "{elseif,…} after {else}")]
    [InlineData("{if,name=A}a", 0, "without {/if}")]
    [InlineData("{if,name=A}{if,name=B}b{/if}", 0, "without {/if}")]
    [InlineData("{if,name=A}{if,name=B}b{/if}{/if}{/if}", 5, "{/if} without {if,…}")]
    public void Broken_blocks_name_the_step_and_why(string text, int step, string error)
    {
        var broken = KeyTags.CheckBlocks(Steps(text));
        Assert.NotNull(broken);
        Assert.Equal(step, broken.Value.Step);
        Assert.Contains(error, broken.Value.Error);
    }

    // --- Lauf ---

    /// <summary>
    /// Läuft die Schritte mit Bedingungen, die <paramref name="truth"/> beantwortet (js="Name"). Zurück: die Pausen, die
    /// liefen (ihre Millisekunden als Kennung), und welche Bedingungen gefragt wurden.
    /// </summary>
    static async Task<(string Ran, string Asked, FormResult Result, StepGateTests.RecordingGate Gate)> Run(
        FormStep[] steps, params string[] truth)
    {
        var asked = new List<string>();
        var gate = new StepGateTests.RecordingGate();
        var result = await new PasteService().RunFormAsync(Sourced(steps), targetWindow: 0, windowGuard: false, gate,
            test: script =>
            {
                asked.Add(script);
                return Task.FromResult(new ScriptTest(truth.Contains(script)));
            });
        var ran = gate.Seen.Select(i => gate.Steps![i].Step).OfType<WaitStep>().Select(w => w.Milliseconds.ToString());
        return (string.Join(",", ran), string.Join(",", asked), result, gate);
    }

    static IfStep If(string name, bool not = false) => new(new IfCondition(not, null, null, name, 0), ElseIf: false);
    static IfStep ElseIf(string name) => new(new IfCondition(false, null, null, name, 0), ElseIf: true);
    static WaitStep Mark(int id) => new(id);

    [Theory]
    [InlineData(new[] { "a" }, "1,2,4", "a")]
    [InlineData(new string[0], "1,3,4", "a")]
    public async Task If_else_takes_one_branch(string[] truth, string ran, string asked)
    {
        var run = await Run([Mark(1), If("a"), Mark(2), new ElseStep(), Mark(3), new EndIfStep(), Mark(4)], truth);
        Assert.Equal(FormOutcome.Completed, run.Result.Outcome);
        Assert.Equal(ran, run.Ran);
        Assert.Equal(asked, run.Asked);
    }

    [Theory]
    [InlineData(new[] { "a", "b" }, "1,9", "a")] // die erste, die gilt; die übrigen werden nicht mehr gefragt
    [InlineData(new[] { "b", "c" }, "2,9", "a,b")]
    [InlineData(new[] { "c" }, "3,9", "a,b,c")]
    [InlineData(new string[0], "4,9", "a,b,c")]
    public async Task Elseif_chain_checks_until_one_holds(string[] truth, string ran, string asked)
    {
        var run = await Run([If("a"), Mark(1), ElseIf("b"), Mark(2), ElseIf("c"), Mark(3), new ElseStep(), Mark(4), new EndIfStep(), Mark(9)],
            truth);
        Assert.Equal(ran, run.Ran);
        Assert.Equal(asked, run.Asked);
    }

    [Fact]
    public async Task Without_else_nothing_runs_when_the_condition_fails()
    {
        var run = await Run([If("a"), Mark(1), new EndIfStep(), Mark(2)]);
        Assert.Equal("2", run.Ran);
    }

    [Fact]
    public async Task Not_turns_the_condition_around()
    {
        Assert.Equal("1", (await Run([If("a", not: true), Mark(1), new ElseStep(), Mark(2), new EndIfStep()])).Ran);
        Assert.Equal("2", (await Run([If("a", not: true), Mark(1), new ElseStep(), Mark(2), new EndIfStep()], "a")).Ran);
    }

    [Theory]
    [InlineData(new[] { "a", "b" }, "1,9")]
    [InlineData(new[] { "a" }, "2,9")]
    [InlineData(new[] { "c" }, "3,9")]
    [InlineData(new string[0], "9")]
    public async Task Nested_blocks_skip_as_a_whole(string[] truth, string ran)
    {
        FormStep[] steps =
        [
            If("a"),
                If("b"), Mark(1), new ElseStep(), Mark(2), new EndIfStep(),
            new ElseStep(),
                If("c"), Mark(3), new ElseStep(), new EndIfStep(),
            new EndIfStep(),
            Mark(9),
        ];
        Assert.Equal(ran, (await Run(steps, truth)).Ran);
    }

    [Fact]
    public async Task The_gate_sees_the_branch_it_lands_on_but_not_the_skipped_steps()
    {
        var run = await Run([If("a"), Mark(1), ElseIf("b"), Mark(2), new ElseStep(), Mark(3), new EndIfStep(), Mark(4)], "b");
        // {if} 0, angesprungen {elseif} 2, sein Zweig 3, dann von oben {else} 4 → weiter nach {/if} 6
        Assert.Equal([0, 2, 3, 4, 7], run.Gate.Seen);
    }

    [Fact]
    public async Task Skipped_branch_never_runs_its_deferred_block()
    {
        var expanded = new List<int>();
        var result = await new PasteService().RunFormAsync(
            Sourced(If("a"), new DeferredStep(1, "{{ ja }}"), new ElseStep(), new DeferredStep(2, "{{ nein }}"), new EndIfStep()),
            targetWindow: 0, windowGuard: false,
            expand: (step, source) =>
            {
                expanded.Add(step.Block);
                // Der Block bringt Schritte mit, die Sprünge danach stimmen trotzdem
                return Task.FromResult(new Expansion([new SourcedStep(new WaitStep(1), source), new SourcedStep(new WaitStep(2), source)]));
            },
            test: _ => Task.FromResult(new ScriptTest(true)));
        Assert.Equal(FormOutcome.Completed, result.Outcome);
        Assert.Equal([1], expanded);
    }

    [Fact]
    public async Task Failed_condition_stops_with_its_message()
    {
        var result = await new PasteService().RunFormAsync(Sourced(Mark(1), If("x.y"), Mark(2), new EndIfStep()), 0, false,
            test: _ => Task.FromResult(new ScriptTest(false, "x is not defined")));
        Assert.Equal(FormOutcome.ScriptFailed, result.Outcome);
        Assert.Equal("x is not defined", result.Error);
        Assert.Equal(1, result.StepIndex);
    }

    [Fact]
    public async Task Script_condition_without_scripts_stops()
    {
        var result = await new PasteService().RunFormAsync(Sourced(If("a"), new EndIfStep()), 0, false);
        Assert.Equal(FormOutcome.ScriptFailed, result.Outcome);
    }

    // --- Skripte ---

    [Theory]
    [InlineData("{if,name=A}{{ x = 1 }}{/if}", true)]
    [InlineData("{{ x = 1 }}{if,name=A}a{/if}", false)] // vor dem {if}: kann vorher laufen
    [InlineData("{tab}{{ x = 1 }}", false)]
    [InlineData("{{ const r = run('a') }}{if,js=\"r.code == 0\"}{{ r.out }}{/if}", true)] // run() vorne entscheidet nicht allein
    [InlineData("{{ const r = run('a') }}{tab}{{ r.out }}", false)]
    public void Blocks_after_an_if_are_staged(string text, bool staged)
    {
        var form = new FormBuilder();
        Assert.Equal(staged, ScriptSession.NeedsStaging(KeyTags.MarkTags(text, form), form));
    }

    [Fact]
    public void Block_in_a_branch_runs_only_when_the_branch_does()
    {
        const string text = "{{ var a = 'vorher' }}{if,name=A}{{ a = 'ja' }}{else}{{ a = 'nein' }}{/if}{{ a }}";
        var form = new FormBuilder();
        using var session = new ScriptSession(SnippetScriptTests.Context with { Form = form });
        var marked = KeyTags.MarkTags(text, form);
        Assert.True(ScriptSession.NeedsStaging(marked, form));
        var steps = KeyTags.SplitWithSources(session.RenderStaged(marked), form);
        Assert.Equal(["{if,name=A}", "{{ a = 'ja' }}", "{else}", "{{ a = 'nein' }}", "{/if}", "{{ a }}"],
            steps.Select(s => KeyTags.Describe(s.Step)));
        Assert.True(session.Test("a == 'vorher'")); // keiner der beiden Zweige ist schon gelaufen

        // Der Lauf nimmt {else}: nur dessen Block und der danach
        session.Evaluate((DeferredStep)steps[3].Step, steps[3].Source);
        var output = session.Evaluate((DeferredStep)steps[5].Step, steps[5].Source);
        Assert.Equal("nein", Assert.IsType<TextStep>(Assert.Single(output).Step).Text);
    }

    [Fact]
    public void Test_sees_the_variables_and_says_true_or_false()
    {
        using var session = new ScriptSession(SnippetScriptTests.Context);
        session.Render("{{ var answer = 'ja'; const r = { code: 0 } }}");
        Assert.True(session.Test("answer == 'ja'"));
        Assert.False(session.Test("r.code != 0"));
        Assert.False(session.Test("''"));
        Assert.True(session.Test("r // ein Objekt gilt"));
        Assert.Throws<SnippetScriptException>(() => session.Test("gibtsnicht.x"));
    }

    // --- Editor ---

    [Fact]
    public void Editor_colors_the_block_tags_like_element_tags()
    {
        const string text = "{if,name=A}x{else}y{/if}";
        var spans = SnippetSyntax.Tokenize(text).Select(s => (text.Substring(s.Start, s.Length), s.Kind)).ToArray();
        Assert.Equal(
        [
            ("{if,", SyntaxKind.Element), ("name=A", SyntaxKind.Argument), ("}", SyntaxKind.Element),
            ("{else}", SyntaxKind.Element), ("{/if}", SyntaxKind.Element),
        ], spans);
    }

    [Theory]
    [InlineData("{if,name=A}x", "{if,name=A}", "without {/if}")]
    [InlineData("x{else}y", "{else}", "without {if,…}")]
    [InlineData("{if,js=\"a\"}x{else}y{else}z{/if}", "{else}z", "a second {else}")]
    public void Editor_marks_the_tag_where_the_blocks_break(string text, string at, string error)
    {
        var span = Assert.Single(SnippetSyntax.Tokenize(text), s => s.Kind == SyntaxKind.Error);
        Assert.Equal(text.IndexOf(at, StringComparison.Ordinal), span.Start);
        Assert.Contains(error, span.Error);
    }

    [Theory]
    [InlineData("{if,name=A}", "TagIf")]
    [InlineData("{elseif,process=outlook}", "TagIfWindow")]
    [InlineData("{if,js=\"a\"}", "TagIfScript")]
    [InlineData("{else}", "TagIf")]
    [InlineData("{/if}", "TagIf")]
    public void Hover_help_fits_the_kind_of_condition(string tag, string key) =>
        Assert.Equal(key, TagHelp.At(tag, 2)?.Key);

    [Fact]
    public void Test_button_on_an_if_tag_runs_everything() =>
        Assert.Equal(TestScope.All, SnippetSyntax.TestRange("{if,name=A}a{/if}", 3, 0).Scope);

    [Fact]
    public async Task Missing_window_is_no_error_just_false()
    {
        var missing = new IfStep(new IfCondition(false, null, new WindowSelector("qhb-gibt-es-nicht", null, null), null, 0), false);
        var run = await Run([missing, Mark(1), new ElseStep(), Mark(2), new EndIfStep()]);
        Assert.Equal(FormOutcome.Completed, run.Result.Outcome);
        Assert.Equal("2", run.Ran);
    }
}
