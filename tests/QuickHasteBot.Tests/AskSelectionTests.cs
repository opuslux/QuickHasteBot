using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>ask() und selection: was vor dem Einfügen erfragt bzw. aus dem Zielfenster kopiert wird.</summary>
public class AskSelectionTests
{
    static string Render(string text, ScriptContext? context = null) =>
        SnippetScript.Render(text, context ?? SnippetScriptTests.Context);

    [Fact]
    public void Questions_come_in_order_once_per_label()
    {
        var questions = SnippetScript.Questions("""
            Kunde: {{ ask('Kunde', 'Muster') }}
            {{ ask("Betreff") }} / {{ ask('Kunde') }}
            {{ const x = ask('Sag \'hallo\'') }}
            ask('nur Text') ausserhalb
            """);
        Assert.Equal([new Question("Kunde", "Muster"), new Question("Betreff", ""), new Question("Sag 'hallo'", "")], questions);
    }

    [Fact]
    public void Options_set_the_window_first_one_wins()
    {
        const string text = """
            {{ var f = ask('Frage', 'Soll ich?', { button: 'Orakel befragen', title: "Das Orakel" }) }}
            {{ ask('Name', { text: 'Wer fragt?', button: 'Später' }) }}
            {{ ask('Ort', {}) }}
            """;

        Assert.Equal(new AskWindowOptions("Orakel befragen", "Das Orakel", "Wer fragt?"), SnippetScript.AskWindow(text));
        Assert.Equal([new Question("Frage", "Soll ich?"), new Question("Name", ""), new Question("Ort", "")], SnippetScript.Questions(text));
        Assert.Equal(new AskWindowOptions(), SnippetScript.AskWindow("{{ ask('Kunde') }}"));
    }

    [Fact]
    public void Ask_with_options_gives_the_answer_or_default()
    {
        const string text = "{{ ask('Frage', 'Soll ich?', { button: 'Los' }) }} / {{ ask('Name', { title: 'Wer' }) }}";
        Assert.Equal("Soll ich? / [Name]", Render(text));
        var context = SnippetScriptTests.Context with { Answers = new Dictionary<string, string> { ["Frage"] = "Ja?", ["Name"] = "Max" } };
        Assert.Equal("Ja? / Max", Render(text, context));
    }

    [Theory]
    [InlineData("{{ ask('Frage', { icon: 'info' }) }}", "doesn't know the option 'icon'")]
    [InlineData("{{ ask('Frage', 'Vorgabe', 'Knopf') }}", "holds the options")]
    public void Wrong_options_fail(string text, string message)
    {
        var ex = Assert.Throws<SnippetScriptException>(() => Render(text));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Options_with_variables_are_not_asked_and_fail_when_pasting()
    {
        const string text = "{{ var b = 'Los'; ask('Frage', { button: b }) }}";
        Assert.Empty(SnippetScript.Questions(text));
        var context = SnippetScriptTests.Context with { Answers = new Dictionary<string, string>() };
        var ex = Assert.Throws<SnippetScriptException>(() => Render(text, context));
        Assert.Contains("fixed texts in quotes", ex.Message);
    }

    [Fact]
    public void Ask_gives_the_answer()
    {
        var context = SnippetScriptTests.Context with { Answers = new Dictionary<string, string> { ["Kunde"] = "Meier" } };
        Assert.Equal("Hallo Meier!", Render("Hallo {{ ask('Kunde', 'Muster') }}!", context));
    }

    [Fact]
    public void Without_answers_ask_shows_its_default_or_label() =>
        Assert.Equal("Muster / [Betreff]", Render("{{ ask('Kunde', 'Muster') }} / {{ ask('Betreff') }}"));

    [Fact]
    public void Ask_with_a_variable_label_fails_when_pasting()
    {
        var context = SnippetScriptTests.Context with { Answers = new Dictionary<string, string>() };
        var ex = Assert.Throws<SnippetScriptException>(() => Render("{{ const f = 'Kunde'; ask(f) }}", context));
        Assert.Contains("fixed text", ex.Message);
    }

    [Fact]
    public void Answers_never_press_keys()
    {
        var form = new FormBuilder();
        var context = SnippetScriptTests.Context with { Form = form, Answers = new Dictionary<string, string> { ["X"] = KeyTags.MarkerStart + "0" + KeyTags.MarkerEnd } };
        Assert.False(KeyTags.HasMarkers(Render("{{ ask('X') }}", context)));
    }

    [Fact]
    public void Selection_is_the_copied_text() =>
        Assert.Equal("> zitiert", Render("> {{ selection.trim() }}", SnippetScriptTests.Context with { Selection = "  zitiert\r\n" }));

    [Theory]
    [InlineData("{{ selection.toUpperCase() }}", true)]
    [InlineData("Ihre selection: {{ user }}", false)]
    [InlineData("{{ my.selection }}", false)]
    [InlineData("{{ selectionCount }}", false)]
    public void Only_scripts_using_selection_copy_it(string text, bool expected) =>
        Assert.Equal(expected, SnippetScript.UsesSelection(text));
}
