using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>run() und das gestaffelte Auswerten, ohne wirklich etwas zu starten (ein Fake statt ScriptRunner).</summary>
public class ScriptSessionTests
{
    readonly List<RunRequest> _runs = [];

    ScriptContext Context(FormBuilder? form = null, Func<RunRequest, RunResult>? run = null) =>
        SnippetScriptTests.Context with
        {
            Form = form,
            Run = run ?? (request =>
            {
                _runs.Add(request);
                return new RunResult(3, $"out:{request.File}\r\n", "err");
            }),
        };

    string Render(string text, Func<RunRequest, RunResult>? run = null)
    {
        using var session = new ScriptSession(Context(run: run));
        return session.Render(text);
    }

    [Fact]
    public void Run_returns_code_output_and_errors()
    {
        Assert.Equal("3|out:a.bat\r\n|err", Render("{{ const r = run('a.bat'); r.code + '|' + r.out + '|' + r.err }}"));
        Assert.Equal(new RunRequest("a.bat", null, SnippetScript.DefaultRunWait, false), Assert.Single(_runs));
    }

    [Fact]
    public void Run_as_text_is_its_output()
    {
        Assert.Equal("out:x.bat\r\n", Render("{{ run('x.bat') }}"));
        Assert.Equal("Nr. out:x.bat", Render("Nr. {{ String(run('x.bat')).trim() }}"));
    }

    [Theory]
    [InlineData("run('a.bat', '1 2')", "1 2", 60, false)]
    [InlineData("run('a.bat', { wait: 5, hidden: true })", null, 5, true)]
    [InlineData("run('a.bat', '-x', { wait: 0 })", "-x", 0, false)]
    [InlineData("run('a.bat', undefined, { wait: 2.6 })", null, 3, false)]
    public void Run_takes_arguments_and_options(string call, string? arguments, int wait, bool hidden)
    {
        Render("{{ " + call + "; '' }}");
        Assert.Equal(new RunRequest("a.bat", arguments, wait, hidden), Assert.Single(_runs));
    }

    [Fact]
    public void Not_waited_code_is_null() =>
        Assert.Equal("true", Render("{{ run('a.bat', { wait: 0 }).code === null }}", _ => new RunResult(null, "", "")));

    [Fact]
    public void Without_run_nothing_starts()
    {
        using var session = new ScriptSession(SnippetScriptTests.Context);
        Assert.Equal("0||", session.Render("{{ const r = run('format c:'); r.code + '|' + r.out + '|' + r.err }}"));
    }

    [Fact]
    public void Waiting_does_not_count_against_the_block_time_limit() =>
        Assert.Equal("ok", Render("{{ run('slow.bat'); let n = 0; for (let i = 0; i < 1000; i++) n += i; 'ok' }}", _ =>
        {
            Thread.Sleep(SnippetScript.Timeout * 2);
            return new RunResult(0, "", "");
        }));

    [Fact]
    public void Failed_run_names_the_block()
    {
        var ex = Assert.Throws<SnippetScriptException>(() => Render("{{ 1 }}{{ run('x.bat') }}", _ => throw new RunFailedException("x.bat didn't finish within 1 s")));
        Assert.Equal(2, ex.Block);
        Assert.Contains("didn't finish", ex.Message);
    }

    [Fact]
    public void Escape_while_waiting_is_not_a_script_error() =>
        Assert.Throws<OperationCanceledException>(() => Render("{{ run('x.bat') }}", _ => throw new OperationCanceledException()));

    [Fact]
    public void Run_needs_a_file() =>
        Assert.Contains("needs a file", Assert.Throws<SnippetScriptException>(() => Render("{{ run() }}")).Message);

    // --- gestaffelt ---

    [Theory]
    [InlineData("{tab}{{ run('a') }}", true)]
    [InlineData("{{ 1 }}{tab}{{ const r = run('a') }}{{ r }}", true)]
    [InlineData("{{ run('a') }}{tab}", false)] // vorher passiert nichts
    [InlineData("Text {{ run('a') }}{tab}", false)]
    [InlineData("{tab}{{ date() }}", false)] // kein run()
    [InlineData("{tab}{{ x.rerun(1) }}", false)]
    public void Staging_only_when_a_tag_comes_before_run(string text, bool staged)
    {
        var form = new FormBuilder();
        Assert.Equal(staged, ScriptSession.NeedsStaging(KeyTags.MarkTags(text, form)));
    }

    [Fact]
    public void Staged_blocks_run_when_reached_and_share_variables()
    {
        const string text = "{{ var a = 'vorher' }}{tab}{{ a }}\r\n{{ const r = run('n.bat') }}\r\n{focus,#nr}{{ r.out.trim() + ' ' + a }}{enter}";
        var form = new FormBuilder();
        using var session = new ScriptSession(Context(form));
        var marked = KeyTags.MarkTags(text, form);
        var steps = KeyTags.SplitWithSources(session.RenderStaged(marked), form);
        Assert.Empty(_runs); // noch nichts gestartet

        // "vorher" samt Umbruch, genau wie wenn alles vorher ausgewertet würde (der Block mit run() gibt nichts aus)
        Assert.Equal(["{tab}", "vorher\r\n", "{{ const r = run('n.bat') }}", "{focus,#nr}", "{{ r.out.trim() + ' ' + a }}", "{enter}"],
            steps.Select(s => s.Step is TextStep t ? t.Text : KeyTags.Describe(s.Step)));

        var deferred = steps.Where(s => s.Step is DeferredStep).ToList();
        Assert.Equal([3, 4], deferred.Select(s => ((DeferredStep)s.Step).Block));
        Assert.Equal("{{ const r = run('n.bat') }}", text.Substring(deferred[0].Source.Start, deferred[0].Source.Length));

        Assert.Empty(session.Evaluate((DeferredStep)deferred[0].Step, deferred[0].Source));
        Assert.Equal("n.bat", Assert.Single(_runs).File);
        var output = session.Evaluate((DeferredStep)deferred[1].Step, deferred[1].Source);
        Assert.Equal("out:n.bat vorher", Assert.IsType<TextStep>(Assert.Single(output).Step).Text);
        Assert.Equal(deferred[1].Source, output[0].Source);
    }

    [Fact]
    public void Keys_from_a_staged_block_come_from_the_block()
    {
        const string text = "{tab}{{ run('a'); 'x' + key('tab') + 'y' }}";
        var form = new FormBuilder();
        using var session = new ScriptSession(Context(form));
        var steps = KeyTags.SplitWithSources(session.RenderStaged(KeyTags.MarkTags(text, form)), form);
        var deferred = steps[1];
        var output = session.Evaluate((DeferredStep)deferred.Step, deferred.Source);
        Assert.Equal(["x", "{tab}", "y"], output.Select(s => s.Step is TextStep t ? t.Text : KeyTags.Describe(s.Step)));
        Assert.All(output, s => Assert.Equal(deferred.Source, s.Source));
    }

    [Fact]
    public void Line_breaks_next_to_a_staged_block_stay_like_text()
    {
        // Wie vorher ausgewertet: "Nr. 7\nWeiter" mit dem Umbruch, die Zeilen um die Tags bleiben Layout
        const string text = "{tab}\r\nNr. {{ run('a').out }}\r\nWeiter{enter}";
        var form = new FormBuilder();
        using var session = new ScriptSession(Context(form, _ => new RunResult(0, "7", "")));
        var steps = KeyTags.SplitWithSources(session.RenderStaged(KeyTags.MarkTags(text, form)), form);
        Assert.Equal(["{tab}", "Nr. ", "{{ run('a').out }}", "\r\nWeiter", "{enter}"],
            steps.Select(s => s.Step is TextStep t ? t.Text : KeyTags.Describe(s.Step)));
    }

    [Fact]
    public void Staged_error_names_the_block()
    {
        var form = new FormBuilder();
        using var session = new ScriptSession(Context(form));
        var steps = KeyTags.SplitWithSources(session.RenderStaged(KeyTags.MarkTags("{{ 1 }}{tab}{{ run('a'); throw new Error('Nummer fehlt') }}", form)), form);
        var deferred = steps.Single(s => s.Step is DeferredStep);
        var ex = Assert.Throws<SnippetScriptException>(() => session.Evaluate((DeferredStep)deferred.Step, deferred.Source));
        Assert.Equal(2, ex.Block);
        Assert.Contains("Nummer fehlt", ex.Message);
    }
}
