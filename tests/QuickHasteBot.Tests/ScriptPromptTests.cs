using System.Text.RegularExpressions;
using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class ScriptPromptTests
{
    static string Default(string task) => ScriptPrompt.Build(task, template: null);

    [Fact]
    public void Default_asks_for_a_code_block_and_has_the_rules_that_break_blocks()
    {
        var prompt = Default("Datum");
        Assert.Contains("exactly one markdown code block and nothing else. The code inside starts with {{ and ends with }}.", prompt);
        Assert.Contains("\"} }\"", prompt);
        Assert.Contains("No backticks", prompt);
        Assert.Contains("a.at(0)", prompt);
        // Alle Helfer ausser run(): Die KI soll keine Programme starten
        Assert.All(SnippetScript.Helpers.Where(h => !h.Signature.StartsWith("run(")),
            helper => Assert.Contains("  " + helper.Signature + ": " + helper.Description + "\n", prompt));
        Assert.DoesNotContain("run(", prompt);
        Assert.DoesNotContain("{helpers}", prompt);
        Assert.DoesNotContain("{task}", prompt);
    }

    // Die Beispiele im Prompt müssen in unserer Engine laufen, sonst lernt die KI Falsches
    [Fact]
    public void Examples_in_the_default_run()
    {
        var examples = Regex.Matches(Default("x"), @"^Task ""[^""]+"": (\{\{.*\}\})$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(2, examples.Count);
        Assert.Equal("Dienstag, 03. November 2026", SnippetScript.Render(examples[0], SnippetScriptTests.Context));
        Assert.Equal("03.11.2026 14:05", SnippetScript.Render(examples[1], SnippetScriptTests.Context));
    }

    // Google AI Mode sucht sonst nach diesen Namen, statt Code zu schreiben
    [Theory]
    [InlineData("Jint")]
    [InlineData("QuickHasteBot")]
    [InlineData(".NET ECMAScript")]
    public void Prompt_avoids_names_that_trigger_a_search(string name) =>
        Assert.DoesNotContain(name, Default("Datum"));

    [Fact]
    public void Task_comes_first_and_trimmed() =>
        Assert.StartsWith("Write JavaScript code for this task: Datum in 14 Tagen\n\n", Default("  Datum in 14 Tagen \r\n"));

    [Fact]
    public void Own_template_replaces_the_default() =>
        Assert.Equal("Schreib JS für: Wochentag. Nur Code.", ScriptPrompt.Build("Wochentag", "Schreib JS für: {task}. Nur Code.\r\n"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ohne Platzhalter")]
    public void Unusable_template_falls_back_to_the_default(string? template) =>
        Assert.Equal(Default("x"), ScriptPrompt.Build("x", template));

    // Kopieren-Knopf, markierter Codeblock mit Zäunen oder mit Sprachbeschriftung: es kommt derselbe Block heraus
    [Theory]
    [InlineData("{{ 1 }}", "{{ 1 }}")]
    [InlineData("```\n{{ 1 }}\n```", "{{ 1 }}")]
    [InlineData("```javascript\r\n{{ (() => { return 1; })() }}\r\n```\r\n", "{{ (() => { return 1; })() }}")]
    [InlineData("  ```js\n{{ 1 }}\n{{ 2 }}\n```  ", "{{ 1 }}\r\n{{ 2 }}")]
    [InlineData("javascript\n{{ 1 }}", "{{ 1 }}")]
    [InlineData("JavaScript\r\n{{ 1 }}", "{{ 1 }}")]
    public void Code_fences_and_language_label_are_stripped(string text, string expected)
    {
        Assert.Equal(expected.ReplaceLineEndings(), ScriptPrompt.ToBlock(text, out var wrapped));
        Assert.False(wrapped);
    }

    // Hält sich die KI nicht ans Format, wird {{ }} ergänzt
    [Theory]
    [InlineData("6 * 7", "{{ 6 * 7 }}")]
    [InlineData("```js\nconst a = 2;\na * 3\n```", "{{ const a = 2;\r\na * 3 }}")]
    [InlineData("javascript\nnew Date().getFullYear()", "{{ new Date().getFullYear() }}")]
    public void Missing_braces_are_added(string text, string expected)
    {
        Assert.Equal(expected.ReplaceLineEndings(), ScriptPrompt.ToBlock(text, out var wrapped));
        Assert.True(wrapped);
    }

    [Fact]
    public void Empty_answer_gives_no_block()
    {
        Assert.Equal("", ScriptPrompt.ToBlock("  \r\n```\n```", out var wrapped));
        Assert.False(wrapped);
    }

    [Fact]
    public void Google_ai_mode_gets_language_and_encoded_prompt()
    {
        var google = ScriptPrompt.Services.Single(s => s.Name == "Google AI Mode");
        var url = ScriptPrompt.Url(google.Url, "Grüsse & {{ 1 + f(2) }} #1", "de");

        // Wie ein Formular: Leerzeichen als +, ein echtes + als %2B, Klammern bleiben
        Assert.StartsWith("https://www.google.com/search?udm=50&hl=de&q=", url);
        Assert.EndsWith("Gr%C3%BCsse+%26+%7B%7B+1+%2B+f(2)+%7D%7D+%231", url);
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/ohne-platzhalter")]
    public void Unusable_url_falls_back_to_chatgpt(string? template) =>
        Assert.Equal("https://chatgpt.com/?q=x", ScriptPrompt.Url(template, "x", "de"));

    [Fact]
    public void Custom_url_works() =>
        Assert.Equal("https://claude.ai/new?q=a+b", ScriptPrompt.Url("https://claude.ai/new?q={prompt}", "a b", "de"));

    [Fact]
    public void Chatgpt_is_the_default_and_every_service_takes_the_prompt()
    {
        Assert.Equal("ChatGPT", ScriptPrompt.ServiceFor(ScriptPrompt.DefaultUrl)?.Name);
        Assert.Equal(["ChatGPT", "Google AI Mode", "Perplexity"], ScriptPrompt.Services.Select(s => s.Name));
        Assert.All(ScriptPrompt.Services, s => Assert.Contains("{prompt}", s.Url));
        Assert.Null(ScriptPrompt.ServiceFor("https://claude.ai/new?q={prompt}"));
    }

    // Google kappt die Anfrage um 2048 Zeichen. Der Regelteil lässt Platz für eine typische Aufgabe.
    [Fact]
    public void Url_stays_short_enough_for_google()
    {
        Assert.InRange(ScriptPrompt.Url(ScriptPrompt.DefaultUrl, Default(""), "de").Length, 0, 1700);
        Assert.InRange(ScriptPrompt.Url(ScriptPrompt.DefaultUrl, Default("das aktuelle Datum im Format \"Wochentag, dd. Monat yyyy\""), "de").Length, 0, 2048);
    }
}
