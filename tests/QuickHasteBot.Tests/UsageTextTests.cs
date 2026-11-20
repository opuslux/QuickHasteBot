using QuickHasteBot.Core;
using QuickHasteBot.UI;

namespace QuickHasteBot.Tests;

/// <summary>Texte zur Nutzung, gegen die Vorlagen aus Strings geprüft: Die Sprache hängt davon ab, was vorher lief.</summary>
public class UsageTextTests
{
    // Mittag in Ortszeit, damit keine Zeitzone den Tag verschiebt
    static readonly DateTime Now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "yesterday")]
    [InlineData(5, "days:5")]
    [InlineData(13, "days:13")]
    [InlineData(14, "weeks:2")]
    [InlineData(59, "weeks:8")]
    [InlineData(60, "months:2")]
    [InlineData(400, "months:13")]
    public void Ago_counts_calendar_days(int days, string expected)
    {
        var text = expected.Split(':') switch
        {
            ["today"] => Strings.AgoToday,
            ["yesterday"] => Strings.AgoYesterday,
            ["days", var n] => string.Format(Strings.AgoDaysFormat, n),
            ["weeks", var n] => string.Format(Strings.AgoWeeksFormat, n),
            [_, var n] => string.Format(Strings.AgoMonthsFormat, n),
            _ => throw new ArgumentException(expected),
        };
        Assert.Equal(text, UsageText.Ago(Now.AddDays(-days), Now));
    }

    [Fact]
    public void Late_yesterday_is_still_yesterday()
    {
        var lateYesterday = new DateTime(2026, 11, 19, 23, 30, 0, DateTimeKind.Local).ToUniversalTime();
        Assert.Equal(Strings.AgoYesterday, UsageText.Ago(lateYesterday, Now));
    }

    [Fact]
    public void Never_used_has_no_tooltip() => Assert.Null(UsageText.Tooltip(null, "mf", Now));

    [Fact]
    public void Tooltip_shows_counts_last_use_and_learned_texts()
    {
        var stats = new UsageStats(8, 1, 3, Now.AddDays(-30), Now.AddDays(-2),
            [new LearnedQuery("mf", 4, Now), new LearnedQuery("gruss", 2, Now)]);

        var lines = UsageText.Tooltip(stats, null, Now)!.Split(Environment.NewLine);

        Assert.Equal([
            string.Format(Strings.UsageTooltipFormat, 12, 8, 1, 3),
            string.Format(Strings.UsageTooltipLastFormat, string.Format(Strings.AgoDaysFormat, 2)),
            string.Format(Strings.UsageTooltipLearnedFormat,
                string.Format(Strings.LearnedQueryFormat, "mf", 4) + ", " + string.Format(Strings.LearnedQueryFormat, "gruss", 2)),
        ], lines);
    }

    [Fact]
    public void Tooltip_says_why_the_entry_moved_up()
    {
        var stats = new UsageStats(6, 0, 0, Now, Now,
            [new LearnedQuery("mfg", 4, Now), new LearnedQuery("m", 1, Now), new LearnedQuery("gruss", 2, Now)]);

        // Der gleiche Suchtext zählt vor einem, zu dem man hintippt; nichts Passendes, keine Zeile
        Assert.EndsWith(string.Format(Strings.UsageTooltipBoostFormat, "m", 1), UsageText.Tooltip(stats, " M ", Now));
        Assert.EndsWith(string.Format(Strings.UsageTooltipBoostFormat, "mfg", 4), UsageText.Tooltip(stats, "mf", Now));
        Assert.Equal(3, UsageText.Tooltip(stats, "xyz", Now)!.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void Learned_list_is_capped()
    {
        var queries = Enumerable.Range(1, 7).Select(i => new LearnedQuery($"q{i}", 1, null)).ToList();
        var text = UsageText.Learned(queries, max: 5);
        Assert.EndsWith(", …", text);
        Assert.Contains("q5", text);
        Assert.DoesNotContain("q6", text);
    }
}
