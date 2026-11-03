using System.Diagnostics;
using System.Globalization;
using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class SnippetScriptTests
{
    // Dienstag, 3. November 2026, 14:05:09
    static readonly ScriptContext Context = new(
        new DateTimeOffset(2026, 11, 3, 14, 5, 9, TimeSpan.FromHours(1)), CultureInfo.GetCultureInfo("de-CH"), "aus der Zwischenablage");

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

    [Theory]
    [InlineData("{{ date() }}", "03.11.2026")]
    [InlineData("{{ date('yyyy-MM-dd', 1) }}", "2026-11-04")]
    [InlineData("{{ date('dddd, d. MMMM yyyy') }}", "Dienstag, 3. November 2026")]
    [InlineData("{{ date(undefined, -3) }}", "31.10.2026")]
    [InlineData("{{ time() }}", "14:05")]
    [InlineData("{{ time('HH:mm:ss') }}", "14:05:09")]
    public void Date_and_time_use_the_given_moment_and_culture(string text, string expected) => Assert.Equal(expected, Render(text));

    [Fact]
    public void Clipboard_is_available() =>
        Assert.Equal("AUS DER ZWISCHENABLAGE", Render("{{ clipboard.toUpperCase() }}"));

    [Fact]
    public void Guid_gives_a_new_guid_each_time()
    {
        var parts = Render("{{ guid() }} {{ guid() }}").Split(' ');
        Assert.All(parts, p => Assert.True(Guid.TryParse(p, out _)));
        Assert.NotEqual(parts[0], parts[1]);
    }

    [Fact]
    public void Literal_braces_can_be_written_as_string() => Assert.Equal("{{name}}", Render("{{ '{{' }}name{{ '}' + '}' }}"));

    [Fact]
    public void Clipboard_is_only_needed_when_mentioned()
    {
        Assert.True(SnippetScript.UsesClipboard("{{ clipboard }}"));
        Assert.False(SnippetScript.UsesClipboard("{{ date() }}"));
    }

    [Theory]
    [InlineData("ok {{ 1 }} {{ 1 + }}", 2)]
    [InlineData("{{ throw new Error('kaputt') }}", 1)]
    [InlineData("{{ gibtsNicht() }}", 1)]
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
