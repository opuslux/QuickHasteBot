using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>Suchen im Editor: reiner Text, Gross-/Kleinschreibung wählbar, Weitersuchen mit Umlauf.</summary>
public class FindInTextTests
{
    [Fact]
    public void Finds_every_match_ignoring_case_by_default()
    {
        Assert.Equal([0, 6, 12], FindInText.FindAll("Hallo hallo HALLO", "hallo", matchCase: false));
        Assert.Equal([6], FindInText.FindAll("Hallo hallo HALLO", "hallo", matchCase: true));
    }

    [Fact]
    public void Matches_do_not_overlap() => Assert.Equal([0, 2], FindInText.FindAll("aaaa", "aa", matchCase: true));

    [Fact]
    public void Braces_and_dots_are_plain_text() =>
        Assert.Equal([5], FindInText.FindAll("Text {tab} . *", "{tab}", matchCase: false));

    [Fact]
    public void Empty_query_or_no_match_finds_nothing()
    {
        Assert.Empty(FindInText.FindAll("Text", "", matchCase: false));
        Assert.Empty(FindInText.FindAll("Text", "y", matchCase: false));
        Assert.Equal(-1, FindInText.Next([], 0, backwards: false));
    }

    [Theory]
    [InlineData(0, false, 0)]  // an einem Treffer: der selbst
    [InlineData(1, false, 1)]  // dahinter: der nächste
    [InlineData(11, false, 0)] // nach dem letzten: wieder vorne
    [InlineData(6, true, 1)]   // rückwärts: der davor
    [InlineData(5, true, 0)]   // rückwärts an einem Treffer: nicht der selbst
    [InlineData(0, true, 2)]   // vor dem ersten: der letzte
    public void Next_wraps_around(int offset, bool backwards, int expected) =>
        Assert.Equal(expected, FindInText.Next([0, 5, 10], offset, backwards));
}
