using QuickHasteBot.UI;

namespace QuickHasteBot.Tests;

/// <summary>Die Textvorschauen richten Tabs aus wie der Editor: Tabstopp alle 4 Zeichen.</summary>
public class TabStopsTests
{
    [Theory]
    [InlineData("ohne Tabs", "ohne Tabs")]
    [InlineData("\tx", "    x")]
    [InlineData("ab\tx", "ab  x")]
    [InlineData("abcd\tx", "abcd    x")]
    [InlineData("a\t\tx", "a       x")]
    // Jede Zeile zählt neu, Umlaute sind ein Zeichen
    [InlineData("Datum lang:\tA\r\nGrüsse:\t\tB", "Datum lang: A\r\nGrüsse:     B")]
    public void Tabs_become_spaces_up_to_the_next_stop(string text, string expected) =>
        Assert.Equal(expected, TabStops.Expand(text));

    [Fact]
    public void Columns_line_up_like_in_the_editor()
    {
        var lines = TabStops.Expand("Datum lang:\t\t\t\t{{ a }}\nZufälliger Satz:\t\t{{ b }}\nAB:\t\t\t\t\t\t{{ c }}").Split('\n');
        Assert.All(lines, l => Assert.Equal(24, l.IndexOf("{{", StringComparison.Ordinal)));
    }
}
