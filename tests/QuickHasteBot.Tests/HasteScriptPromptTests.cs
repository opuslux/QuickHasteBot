using QuickHasteBot.Core;
using QuickHasteBot.UI;

namespace QuickHasteBot.Tests;

public class HasteScriptPromptTests
{
    static readonly string Readme = HelpWindow.ReadReadme();

    static HasteScriptRequest Request(string script = "Hallo{tab}Welt", string task = "Auch die Telefonnummer ausfüllen") =>
        new(task, script, 0, 0, "Formular", ".txt", "2.17.0", "de-CH", []);

    [Fact]
    public void Docs_take_the_script_sections_of_the_readme()
    {
        var docs = HasteScriptPrompt.Docs(Readme);

        // Fehlt eine Überschrift, wurde sie im README umbenannt: dann DocSections anpassen
        Assert.All(HasteScriptPrompt.DocSections, heading => Assert.Contains(heading + "\n", docs));
        Assert.Contains("### Conditions: if, elseif, else", docs);
        Assert.Contains("### Elements: focus, click, set", docs);
        Assert.Contains("| `date(format, days)`", docs);
        // Bedienung und die KI-Fenster selbst nicht
        Assert.DoesNotContain("## The popup", docs);
        Assert.DoesNotContain("## Settings and files", docs);
        Assert.DoesNotContain("## Getting started", docs);
        Assert.All(HasteScriptPrompt.SkippedSections, heading => Assert.DoesNotContain(heading, docs));
        Assert.DoesNotContain("ai-prompt.txt", docs);
        Assert.InRange(docs.Length, 20_000, 80_000);
    }

    [Fact]
    public void Docs_ignore_headings_in_code_blocks_and_end_at_the_next_section()
    {
        const string readme = """
            # Title
            ## Scripts
            Text
              ```
            ## not a heading
              ```
            ### Writing a block with AI
            skipped
            ### Running scripts
            kept
            ## Settings and files
            gone
            """;

        Assert.Equal("## Scripts\nText\n  ```\n## not a heading\n  ```\n### Running scripts\nkept", HasteScriptPrompt.Docs(readme));
    }

    [Fact]
    public void Prompt_has_instructions_docs_snippet_and_task_in_that_order()
    {
        var prompt = HasteScriptPrompt.Build(Request(), null, Readme);

        Assert.StartsWith("You are an expert in HasteScript", prompt);
        var docs = prompt.IndexOf("## Forms: key tags", StringComparison.Ordinal);
        var script = prompt.IndexOf("```\nHallo{tab}Welt\n```", StringComparison.Ordinal);
        var task = prompt.LastIndexOf("Auch die Telefonnummer ausfüllen", StringComparison.Ordinal);
        Assert.True(docs > 0 && docs < script && script < task, prompt[..200]);
        Assert.Contains("Name: Formular.txt", prompt);
        Assert.Contains("QuickHasteBot 2.17.0, Windows language de-CH", prompt);
        Assert.DoesNotContain("Problems found by the editor", prompt);
        Assert.DoesNotContain("I selected", prompt);
        Assert.DoesNotContain("{docs}", prompt);
        Assert.DoesNotContain("{snippet}", prompt);
        Assert.EndsWith("Auch die Telefonnummer ausfüllen\n", prompt);
    }

    [Fact]
    public void Empty_task_and_empty_script_have_defaults()
    {
        var prompt = HasteScriptPrompt.Build(Request(script: " \r\n", task: "  "), null, Readme);

        Assert.EndsWith(HasteScriptPrompt.DefaultTask + "\n", prompt);
        Assert.Contains("The script is empty: write a new one.", prompt);
        Assert.DoesNotContain("The script:", prompt);
    }

    [Fact]
    public void Problems_selection_and_button_names_go_along()
    {
        const string script = "Zeile 1\r\n{tab,x}\r\nZeile 3\r\nZeile 4";
        var start = script.IndexOf("{tab,x}", StringComparison.Ordinal);
        var request = Request(script) with
        {
            SelectionStart = start,
            SelectionLength = script.IndexOf("Zeile 3", StringComparison.Ordinal) + 7 - start,
            Problems = ["Line 2: {tab,x} – keine Zahl"],
            Buttons = new Dictionary<string, string> { ["Pick element"] = "Element erfassen", ["Test"] = "Test" },
        };

        var snippet = HasteScriptPrompt.Snippet(request);

        Assert.Contains("Problems found by the editor:\n- Line 2: {tab,x} – keine Zahl\n", snippet);
        Assert.Contains("I selected this part (lines 2-3).", snippet);
        Assert.Contains("```\n{tab,x}\nZeile 3\n```", snippet);
        Assert.Contains("```\nZeile 1\n{tab,x}\nZeile 3\nZeile 4\n```", snippet);
        // Nur Knöpfe, die anders heissen als im README
        Assert.Contains("use these names: \"Element erfassen\" (Pick element)\n", snippet);
        Assert.DoesNotContain("(Test)", snippet);
    }

    [Fact]
    public void A_selection_of_spaces_only_is_ignored()
    {
        var snippet = HasteScriptPrompt.Snippet(Request("a  b") with { SelectionStart = 1, SelectionLength = 2 });

        Assert.DoesNotContain("I selected", snippet);
    }

    [Fact]
    public void The_fence_is_longer_than_backticks_in_the_script()
    {
        Assert.Equal("```\nx\n```\n", HasteScriptPrompt.Fenced("x\n"));
        Assert.Equal("````\nA ``` B\n````\n", HasteScriptPrompt.Fenced("A ``` B"));
    }

    [Fact]
    public void Own_template_keeps_what_it_leaves_out_and_placeholders_in_the_script_stay()
    {
        var prompt = HasteScriptPrompt.Build(Request(script: "Text {task} {docs}"), "Fix this: {task}", Readme);

        Assert.StartsWith("Fix this: Auch die Telefonnummer ausfüllen\n\n# HasteScript reference\n\n", prompt);
        Assert.Contains("# My snippet\n\n", prompt);
        Assert.Contains("```\nText {task} {docs}\n```", prompt);
        // Die Aufgabe steht nur einmal da: Ihr Platzhalter war in der Vorlage
        Assert.DoesNotContain("# My request", prompt);
    }

    [Fact]
    public void Default_template_has_all_placeholders_once()
    {
        foreach (var placeholder in new[] { HasteScriptPrompt.TaskPlaceholder, HasteScriptPrompt.DocsPlaceholder, HasteScriptPrompt.SnippetPlaceholder })
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(HasteScriptPrompt.DefaultTemplate, System.Text.RegularExpressions.Regex.Escape(placeholder)));
    }

    [Fact]
    public void Line_numbers_count_from_one()
    {
        Assert.Equal(1, HasteScriptPrompt.LineAt("abc", 0));
        Assert.Equal(2, HasteScriptPrompt.LineAt("a\r\nb", 3));
        Assert.Equal(3, HasteScriptPrompt.LineAt("a\nb\nc", 99));
    }
}
