using System.Diagnostics;
using System.Globalization;
using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class SnippetScriptTests
{
    // Dienstag, 3. November 2026, 14:05:09 in einer festen Zone UTC+1, damit es auch auf einem UTC-Rechner (CI) gleich läuft
    static readonly DateTimeOffset Now = new(2026, 11, 3, 14, 5, 9, TimeSpan.FromHours(1));
    static readonly TimeZoneInfo Zurich = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    internal static readonly ScriptContext Context = new(Now, CultureInfo.GetCultureInfo("de-CH"), "aus der Zwischenablage", Zurich);

    static string Render(string text) => SnippetScript.Render(text, Context);

    [Theory]
    [InlineData("Freundliche Grüsse")]
    [InlineData("Nur eine { Klammer } und }} am Ende")]
    [InlineData("")]
    public void Text_without_blocks_stays_as_is(string text) => Assert.Equal(text, Render(text));

    [Fact]
    public void Text_around_blocks_stays_including_line_breaks() =>
        Assert.Equal("Summe:\r\n52 Fr.\r\n", Render("Summe:\r\n{{ 10 * 5 + 2 }} Fr.\r\n"));

    [Theory]
    [InlineData("{{ undefined }}")]
    [InlineData("{{ null }}")]
    [InlineData("{{ }}")]
    [InlineData("{{}}")]
    public void Empty_results_vanish(string text) => Assert.Equal("", Render(text));

    [Theory]
    [InlineData("{{ new Date(2026, 0, 2).getDate() }}", "2")]
    [InlineData("{{ Math.max(1, 5) }}", "5")]
    [InlineData("{{ JSON.stringify({ a: 1 }) }}", "{\"a\":1}")]
    [InlineData("{{ 'ab'.toUpperCase().padStart(4, '-') }}", "--AB")]
    [InlineData("{{ `x=${1 + 1}` }}", "x=2")]
    [InlineData("{{ [1, 2, 3].map(n => n * 2).join(', ') }}", "2, 4, 6")]
    [InlineData("{{ [3, 1, 2].sort().join() }}", "1,2,3")]
    [InlineData("{{ 0.1 + 0.2 }}", "0.30000000000000004")]
    [InlineData("{{ true }}", "true")]
    public void Standard_javascript_works_without_helpers(string text, string expected) => Assert.Equal(expected, Render(text));

    [Fact]
    public void Variables_carry_over_to_later_blocks() =>
        Assert.Equal("Anzahl: 6", Render("{{ var n = 2; '' }}Anzahl: {{ n * 3 }}"));

    // new Date() und Date.now() liefern den übergebenen Zeitpunkt, alle Blöcke denselben
    [Theory]
    [InlineData("{{ new Date().getFullYear() }}", "2026")]
    [InlineData("{{ new Date().toLocaleDateString('de-CH') }}", "3.11.2026")]
    [InlineData("{{ new Date().getDate() + '.' + (new Date().getMonth() + 1) }}", "3.11")]
    [InlineData("{{ new Date().getDay() }}", "2")]
    [InlineData("{{ new Date().getHours() + ':' + new Date().getMinutes() }}", "14:5")]
    [InlineData("{{ (() => { const d = new Date(); d.setDate(d.getDate() + 14); return d.getDate() + '.' + (d.getMonth() + 1); })() }}", "17.11")]
    public void Date_uses_the_given_moment(string text, string expected) => Assert.Equal(expected, Render(text));

    [Fact]
    public void Date_now_is_the_given_moment() =>
        Assert.Equal(Now.ToUnixTimeMilliseconds().ToString(), Render("{{ Date.now() }}"));

    [Theory]
    [InlineData("{{ date() }}", "03.11.2026")]
    [InlineData("{{ date('yyyy-MM-dd', 1) }}", "2026-11-04")]
    [InlineData("{{ date('dddd, dd. MMMM yyyy') }}", "Dienstag, 03. November 2026")]
    [InlineData("{{ date(undefined, -3) }}", "31.10.2026")]
    [InlineData("{{ date('dd.MM.yyyy') + ' ' + time('HH:mm') }}", "03.11.2026 14:05")]
    [InlineData("{{ time() }}", "14:05")]
    [InlineData("{{ time('HH:mm:ss') }}", "14:05:09")]
    [InlineData("{{ isoweek() }}", "45")]
    [InlineData("{{ isoweek(7) }}", "46")]
    [InlineData("{{ 'KW ' + isoweek() }}", "KW 45")]
    [InlineData("Please reply by {{ date('dd.MM.yyyy', 14) }} (week {{ isoweek(14) }}).", "Please reply by 17.11.2026 (week 47).")]
    public void Date_helpers_use_the_given_moment_and_culture(string text, string expected) => Assert.Equal(expected, Render(text));

    [Fact]
    public void User_is_the_windows_user() => Assert.Equal(Environment.UserName, Render("{{ user }}"));

    [Fact]
    public void Guid_gives_a_new_guid_each_time()
    {
        var parts = Render("{{ guid() }} {{ guid() }}").Split(' ');
        Assert.All(parts, p => Assert.True(Guid.TryParse(p, out _)));
        Assert.NotEqual(parts[0], parts[1]);
    }

    // Prompt und README beschreiben genau diese Helfer, sie müssen in der Engine existieren
    [Fact]
    public void Every_described_helper_exists()
    {
        foreach (var (signature, _) in SnippetScript.Helpers)
            Assert.NotEqual("undefined", Render("{{ typeof " + signature.Split('(')[0] + " }}"));
    }

    [Fact]
    public void Clipboard_is_available() =>
        Assert.Equal("AUS DER ZWISCHENABLAGE", Render("{{ clipboard.toUpperCase() }}"));

    [Fact]
    public void Literal_braces_can_be_written_as_string() => Assert.Equal("{{name}}", Render("{{ '{{' }}name{{ '}' + '}' }}"));

    // Eine typische KI-Antwort: IIFE mit Arrays, Arrow-Function und Umlauten, Strings mit + verbunden
    [Fact]
    public void Typical_ai_answer_runs()
    {
        var sentence = Render("""
            {{ (() => { const subj=['Der Hund','Eine Katze']; const verb=['sieht','begrüßt']; const obj=['einen alten Baum','das große Haus'];
            const r=a=>a[Math.floor(Math.random()*a.length)]; const s=r(subj)+' '+r(verb)+' '+r(obj);
            const w=s.split(' '); return w.length<=7?s:w.slice(0,7).join(' '); })() }}
            """);
        Assert.InRange(sentence.Split(' ').Length, 5, 7);
    }

    // Der Prompt verlangt .at() statt a[0], weil Perplexity [0] beim Kopieren als Quellenverweis weglässt
    [Fact]
    public void At_replaces_index_brackets() =>
        Assert.Equal("Dienstag | 03. November 2026 | 2026 | D",
            Render("{{ (() => { const parts = 'Dienstag, 03. November 2026'.split(', '); const words = parts.at(1).split(' '); return parts.at(0) + ' | ' + parts.at(1) + ' | ' + words.at(-1) + ' | ' + parts.at(0).at(0); })() }}"));

    // Was beim Kopieren aus einer Chat-Oberfläche passiert, wenn die Backticks fehlen
    [Fact]
    public void Template_literal_without_backticks_is_a_syntax_error() =>
        Assert.Throws<SnippetScriptException>(() => Render("{{ (() => { const a='x'; const s=${a} ${a}; return s; })() }}"));

    [Fact]
    public void Clipboard_is_only_needed_when_mentioned()
    {
        Assert.True(SnippetScript.UsesClipboard("{{ clipboard }}"));
        Assert.False(SnippetScript.UsesClipboard("{{ new Date() }}"));
    }

    [Theory]
    [InlineData("ok {{ 1 }} {{ 1 + }}", 2)]
    [InlineData("{{ throw new Error('kaputt') }}", 1)]
    [InlineData("{{ gibtsNicht() }}", 1)]
    [InlineData("{{ null.x }}", 1)]
    [InlineData("{{ date('%') }}", 1)]
    [InlineData("{{ 1 }} {{ (function f() { return f(); })() }}", 2)]
    public void Errors_name_the_block(string text, int block)
    {
        var ex = Assert.Throws<SnippetScriptException>(() => Render(text));
        Assert.Equal(block, ex.Block);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Endless_loop_is_stopped()
    {
        var watch = Stopwatch.StartNew();
        var ex = Assert.Throws<SnippetScriptException>(() => Render("{{ while (true) {} }}"));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"dauerte {watch.Elapsed}");
        Assert.Contains("Timeout", ex.Message);
    }

    [Fact]
    public void Growing_memory_is_stopped() =>
        Assert.Throws<SnippetScriptException>(() => Render("{{ const a = []; while (true) a.push('x'.repeat(1000) + a.length); }}"));
}
