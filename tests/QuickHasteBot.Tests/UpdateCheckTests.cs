using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("2.15.0", "2.15.0")]
    [InlineData("2.15.0+abc1234", "2.15.0")]
    [InlineData("2.15.0-dev.3.dirty+abc", "2.15.0")]
    [InlineData("v2.16.1", "2.16.1")]
    public void Version_numbers_without_suffixes(string informational, string expected) =>
        Assert.Equal(Version.Parse(expected), AppVersion.Numeric(informational));

    [Theory]
    [InlineData("0.0.0-dev")]
    [InlineData("?")]
    [InlineData("")]
    public void No_version_without_a_real_number(string informational) =>
        Assert.Null(AppVersion.Numeric(informational));

    [Theory]
    [InlineData("v2.16.0", "2.15.0+abc", true)]
    [InlineData("v2.15.0", "2.15.0-dev.4+abc", false)]
    [InlineData("v2.15.0", "2.16.0", false)]
    [InlineData("v2.16.0", "0.0.0-dev", false)]
    [InlineData(null, "2.15.0", false)]
    public void Newer_only_with_a_higher_number(string? tag, string informational, bool expected) =>
        Assert.Equal(expected, UpdateCheck.IsNewer(tag, informational));

    [Fact]
    public void Asks_at_most_once_a_day()
    {
        var now = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.FromHours(1));
        Assert.True(UpdateCheck.Due(null, now));
        Assert.False(UpdateCheck.Due(now.AddHours(-23), now));
        Assert.True(UpdateCheck.Due(now.AddDays(-1), now));
        Assert.True(UpdateCheck.Due(now.AddDays(3), now)); // Uhr zurückgestellt
    }

    [Fact]
    public void Reads_the_github_answer()
    {
        var release = UpdateCheck.Parse("""{ "tag_name": "v2.16.0", "html_url": "https://github.com/opuslux/QuickHasteBot/releases/tag/v2.16.0", "assets": [] }""");
        Assert.Equal(new UpdateCheck.Release(new Version(2, 16, 0), "v2.16.0", "https://github.com/opuslux/QuickHasteBot/releases/tag/v2.16.0"), release);
        Assert.Null(UpdateCheck.Parse("""{ "message": "Not Found" }"""));
        Assert.Null(UpdateCheck.Parse("kein json"));
        Assert.Null(UpdateCheck.Parse("""{ "tag_name": "nightly" }"""));
    }
}
