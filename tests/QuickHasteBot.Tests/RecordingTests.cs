using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class RecordingTests
{
    static ElementSelector Sel(string text) =>
        ElementSelector.TryParse(text, out var selector, out var error) ? selector : throw new ArgumentException(error);

    static readonly RecordedAction[] Form =
    [
        new RecordedWindow("crm"),
        new RecordedField(Sel("#name"), FieldKind.Text, "", "Muster"),
        new RecordedField(Sel("#ort"), FieldKind.Text, "Zürich", "Bern"),
        new RecordedField(Sel("#land"), FieldKind.Choice, "Deutschland", "Schweiz"),
        new RecordedField(Sel("#newsletter"), FieldKind.Toggle, "off", "on"),
        new RecordedClick(Sel("Speichern"), "Speichern"),
    ];

    static string Build(RecordMode mode, params RecordedAction[] actions) => Recording.Build(actions, mode, "\n");

    /// <summary>Wie AppController: Tags markieren, Skripte rendern, an den Markern zerlegen.</summary>
    static IReadOnlyList<FormStep> Steps(string text)
    {
        var form = new FormBuilder();
        text = KeyTags.MarkTags(text, form);
        text = SnippetScript.Render(text, SnippetScriptTests.Context with { Form = form });
        return KeyTags.Split(text, form);
    }

    [Fact]
    public void Mixed_types_text_and_sets_choices() => Assert.Equal(
        """
        {window,process=crm}
        {focus,#name}Muster
        {focus,#ort}{clear}Bern
        {set,#land,"Schweiz"}
        {set,#newsletter,"on"}
        {click,name=Speichern}

        """.ReplaceLineEndings("\n"), Build(RecordMode.Mixed, Form));

    [Fact]
    public void Always_set_sets_every_field() => Assert.Equal(
        """
        {window,process=crm}
        {set,#name,"Muster"}
        {set,#ort,"Bern"}
        {set,#land,"Schweiz"}
        {set,#newsletter,"on"}
        {click,name=Speichern}

        """.ReplaceLineEndings("\n"), Build(RecordMode.AlwaysSet, Form));

    [Fact]
    public void Always_type_types_choices_and_clicks_checkboxes() => Assert.Equal(
        """
        {window,process=crm}
        {focus,#name}Muster
        {focus,#ort}{clear}Bern
        {focus,#land}Schweiz
        {click,#newsletter}
        {click,name=Speichern}

        """.ReplaceLineEndings("\n"), Build(RecordMode.AlwaysType, Form));

    [Fact]
    public void Clear_only_when_the_field_had_text_before()
    {
        Assert.Equal("{focus,#a}x\n", Build(RecordMode.Mixed, new RecordedField(Sel("#a"), FieldKind.Text, "", "x")));
        Assert.Equal("{focus,#a}x\n", Build(RecordMode.Mixed, new RecordedField(Sel("#a"), FieldKind.Text, null, "x")));
        Assert.Equal("{focus,#a}{clear}x\n", Build(RecordMode.Mixed, new RecordedField(Sel("#a"), FieldKind.Text, "y", "x")));
    }

    [Fact]
    public void Emptied_field_is_cleared() =>
        Assert.Equal("{focus,#a}{clear}\n", Build(RecordMode.Mixed, new RecordedField(Sel("#a"), FieldKind.Text, "alt", "")));

    [Fact]
    public void Unchanged_and_unreadable_fields_are_dropped() => Assert.Equal("", Build(RecordMode.Mixed,
        new RecordedField(Sel("#a"), FieldKind.Text, "x", "x"),
        new RecordedField(Sel("#b"), FieldKind.Text, null, null),
        new RecordedField(Sel("#c"), FieldKind.Text, null, "")));

    [Fact]
    public void Entering_the_same_field_twice_keeps_first_and_last_value() => Assert.Equal(
        "{focus,#a}{clear}drei\n", Build(RecordMode.Mixed,
            new RecordedField(Sel("#a"), FieldKind.Text, "eins", "zwei"),
            new RecordedField(Sel("#a"), FieldKind.Text, "zwei", "drei")));

    [Fact]
    public void Checkbox_toggled_back_is_dropped() => Assert.Equal("", Build(RecordMode.Mixed,
        new RecordedField(Sel("#cb"), FieldKind.Toggle, "off", "on"),
        new RecordedField(Sel("#cb"), FieldKind.Toggle, "on", "off")));

    [Fact]
    public void Click_on_the_chosen_option_of_a_dropdown_is_dropped() => Assert.Equal(
        "{set,#land,\"Schweiz\"}\n{click,name=OK}\n", Build(RecordMode.Mixed,
            new RecordedField(Sel("#land"), FieldKind.Choice, "", "Schweiz"),
            new RecordedClick(Sel("Schweiz"), "Schweiz"),
            new RecordedClick(Sel("OK"), "OK")));

    [Fact]
    public void Password_field_is_only_focused() => Assert.Equal(
        "{focus,#pw}\n", Build(RecordMode.AlwaysSet, new RecordedField(Sel("#pw"), FieldKind.Text, null, null, Password: true)));

    [Fact]
    public void Window_switch_counts_only_with_something_done_there() => Assert.Equal(
        "{window,process=crm}\n{click,name=OK}\n{window,process=excel}\n{click,name=Neu}\n", Build(RecordMode.Mixed,
            new RecordedWindow("explorer"),
            new RecordedWindow("crm"),
            new RecordedClick(Sel("OK"), "OK"),
            new RecordedWindow("crm"),
            new RecordedWindow("excel"),
            new RecordedClick(Sel("Neu"), "Neu"),
            new RecordedWindow("outlook")));

    [Fact]
    public void Same_program_again_after_actions_needs_no_second_window_tag() => Assert.Equal(
        "{window,process=crm}\n{click,name=A}\n{click,name=B}\n", Build(RecordMode.Mixed,
            new RecordedWindow("crm"),
            new RecordedClick(Sel("A"), "A"),
            new RecordedWindow("crm"),
            new RecordedClick(Sel("B"), "B")));

    [Fact]
    public void Always_set_types_values_that_set_would_change() => Assert.Equal(
        "{focus,#a}Er sagte \"Ja\"\n", Build(RecordMode.AlwaysSet, new RecordedField(Sel("#a"), FieldKind.Text, "", "Er sagte \"Ja\"")));

    [Theory]
    [InlineData("Bern")]
    [InlineData("{name} bleibt Text")]
    [InlineData("Taste {tab} wörtlich")]
    [InlineData("Skript {{ 1+1 }} wörtlich")]
    [InlineData("Kommentar {# bleibt} wörtlich")]
    [InlineData("{# ganze Zeile}")]
    [InlineData("Klammern }} und {{ und '\\ gemischt {enter}")]
    [InlineData("{\"json\": {\"a\": 1}}")]
    public void Typed_text_arrives_exactly_as_recorded(string value)
    {
        var text = Build(RecordMode.Mixed, new RecordedField(Sel("#a"), FieldKind.Text, "", value));
        var steps = Steps(text);
        Assert.IsType<ElementStep>(steps[0]);
        Assert.Equal(value, Assert.IsType<TextStep>(Assert.Single(steps.Skip(1))).Text);
    }

    [Fact]
    public void Recorded_snippet_runs_as_the_expected_steps()
    {
        var steps = Steps(Build(RecordMode.Mixed, Form));
        Assert.Equal(
            ["{window,process=crm}", "{focus,#name}", "Muster", "{focus,#ort}", "{ctrl+a}", "{del}", "Bern",
             "{set,#land,\"Schweiz\"}", "{set,#newsletter,\"on\"}", "{click,name=Speichern}"],
            steps.Select(KeyTags.Describe));
    }

    [Fact]
    public void Recorded_snippet_has_no_syntax_errors()
    {
        foreach (var mode in Enum.GetValues<RecordMode>())
            Assert.DoesNotContain(SnippetSyntax.Tokenize(Build(mode, Form)), span => span.Error is not null);
    }

    [Fact]
    public void Nothing_recorded_is_empty_text() => Assert.Equal("", Build(RecordMode.Mixed));
}
