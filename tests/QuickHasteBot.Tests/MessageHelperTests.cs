using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>msg() und confirm(): Meldungen aus Skripten, statt der Message Box von PowerShell.</summary>
public class MessageHelperTests
{
    readonly List<MessageRequest> _shown = [];

    string Render(string text, bool? answer = true, int delay = 0) =>
        SnippetScript.Render(text, SnippetScriptTests.Context with
        {
            Message = request =>
            {
                _shown.Add(request);
                if (delay > 0) Thread.Sleep(delay);
                return answer;
            },
        });

    [Fact]
    public void Msg_shows_the_text_and_types_nothing()
    {
        Assert.Equal("vorher nachher", Render("vorher {{ msg('Fertig') }}nachher"));
        var request = Assert.Single(_shown);
        Assert.Equal(new MessageRequest("Fertig", null, MessageIcon.Info, 0, Confirm: false), request);
    }

    [Fact]
    public void Msg_takes_title_icon_and_close()
    {
        Render("{{ msg('Achtung', { title: 'Bestellung', icon: 'warning', close: 5 }) }}");
        Render("{{ msg('Kaputt', { icon: 'Error', close: 9999 }) }}");
        Render("{{ msg(42, { icon: 'none' }) }}");
        Assert.Equal(
        [
            new MessageRequest("Achtung", "Bestellung", MessageIcon.Warning, 5, false),
            new MessageRequest("Kaputt", null, MessageIcon.Error, SnippetScript.MaxMessageClose, false),
            new MessageRequest("42", null, MessageIcon.None, 0, false),
        ], _shown);
    }

    [Theory]
    [InlineData(true, "dringend")]
    [InlineData(false, "normal")]
    public void Confirm_gives_the_answer(bool answer, string expected)
    {
        Assert.Equal(expected, Render("{{ confirm('Dringend?', { title: 'Betreff' }) ? 'dringend' : 'normal' }}", answer));
        Assert.Equal(new MessageRequest("Dringend?", "Betreff", MessageIcon.Question, 0, Confirm: true), Assert.Single(_shown));
    }

    // Esc oder das X: wie Esc beim Warten auf run(), der Baustein hört ohne Fehlermeldung auf
    [Fact]
    public void Esc_in_confirm_stops_the_snippet() =>
        Assert.Throws<OperationCanceledException>(() => Render("{{ confirm('Weiter?') }}", answer: null));

    [Fact]
    public void Without_a_window_msg_shows_nothing_and_confirm_says_yes()
    {
        Assert.Equal("ja", SnippetScript.Render("{{ msg('x') }}{{ confirm('y') ? 'ja' : 'nein' }}", SnippetScriptTests.Context));
    }

    // Auf die Antwort zu warten zählt nicht zur Zeitgrenze von 250 ms, wie bei run()
    [Fact]
    public void Waiting_for_the_answer_is_not_a_timeout() =>
        Assert.Equal("ok", Render("{{ msg('a'); confirm('b') ? 'ok' : '' }}", delay: 400));

    [Theory]
    [InlineData("{{ msg() }}", "msg needs a text")]
    [InlineData("{{ confirm('  ') }}", "confirm needs a text")]
    [InlineData("{{ msg('x', { icon: 'smile' }) }}", "doesn't know the icon 'smile'")]
    [InlineData("{{ confirm('x', { close: 3 }) }}", "close only works with msg()")]
    [InlineData("{{ msg('x', 'Titel') }}", "the second argument of msg holds the options")]
    public void Wrong_arguments_say_what_is_wrong(string text, string error)
    {
        var ex = Assert.Throws<SnippetScriptException>(() => Render(text));
        Assert.Contains(error, ex.Message);
        Assert.Empty(_shown);
    }

    [Fact]
    public void Markers_never_reach_the_message()
    {
        var form = new FormBuilder();
        var marked = KeyTags.MarkTags("{{ msg(clipboard) }}", form);
        SnippetScript.Render(marked, SnippetScriptTests.Context with
        {
            Clipboard = KeyTags.MarkTags("a{tab}b", new FormBuilder()),
            Form = form,
            Message = request => { _shown.Add(request); return true; },
        });
        Assert.DoesNotContain(Assert.Single(_shown).Text, c => c is >= '' and <= '');
    }

    [Theory]
    [InlineData("msg('x')", true)]
    [InlineData("confirm ('x')", true)]
    [InlineData("run('a.bat')", true)]
    [InlineData("message('x')", false)]
    [InlineData("o.msg('x')", false)]
    [InlineData("const msgText = 'x'", false)]
    public void Msg_and_confirm_wait_like_run(string code, bool waits) =>
        Assert.Equal(waits, SnippetScript.Waits(code));

    [Fact]
    public void A_message_after_a_tag_shows_when_the_run_gets_there()
    {
        var form = new FormBuilder();
        Assert.True(ScriptSession.NeedsStaging(KeyTags.MarkTags("Hallo{enter}{{ msg('Fertig') }}", form), form));
        form = new FormBuilder();
        Assert.False(ScriptSession.NeedsStaging(KeyTags.MarkTags("{{ msg('Los') }}Hallo{enter}", form), form));
    }

    [Fact]
    public void Preview_notes_tell_run_and_messages_apart()
    {
        Assert.True(SnippetScript.UsesRun("{{ run('a') }}"));
        Assert.False(SnippetScript.UsesRun("{{ msg('a') }}"));
        Assert.True(SnippetScript.UsesMessages("{{ confirm('a') }}"));
        Assert.False(SnippetScript.UsesMessages("msg('nur Text') {{ 1 }}"));
    }

    [Theory]
    [InlineData("{{ ms|g('x') }}", "TagMessage")]
    [InlineData("{{ con|firm('x') }}", "TagConfirm")]
    public void Hover_explains_msg_and_confirm(string marked, string key) =>
        Assert.Equal(key, TagHelp.At(marked.Replace("|", ""), marked.IndexOf('|'))?.Key);

    // Die Klammern der Optionen in js="…" beenden das Tag nicht
    [Fact]
    public void Confirm_with_options_in_an_if_is_one_tag()
    {
        var text = "{if,js=\"confirm('Senden?', { title: 'Antwort' })\"}A{else}B{/if}";
        Assert.DoesNotContain(SnippetSyntax.Tokenize(text), s => s.Kind == SyntaxKind.Error);
        var form = new FormBuilder();
        var steps = KeyTags.Split(KeyTags.MarkTags(text, form), form);
        var condition = Assert.IsType<IfStep>(steps[0]).Condition;
        Assert.Equal("confirm('Senden?', { title: 'Antwort' })", condition.Script);
    }
}
