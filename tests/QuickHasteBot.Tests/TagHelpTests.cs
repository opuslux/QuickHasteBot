using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>Welche Hilfe der Tooltip im Editor über einer Stelle zeigt.</summary>
public class TagHelpTests
{
    /// <summary>Die Hilfe an der Stelle von "|" im Text (das Zeichen selbst wird entfernt).</summary>
    static HelpTopic? At(string marked, bool keyTags = true, bool scripts = true) =>
        TagHelp.At(marked.Replace("|", ""), marked.IndexOf('|'), keyTags, scripts);

    [Theory]
    [InlineData("Max{t|ab}", "TagNextField")]
    [InlineData("{|tab}", "TagNextField")]
    [InlineData("{tab|}", "TagNextField")]
    [InlineData("{Shift+Tab|}", "TagPreviousField")]
    [InlineData("{back|space}", "TagBackspace")]
    [InlineData("{wait,5|00}", "TagWait")]
    [InlineData("{guard,o|n}", "TagGuardOn")]
    [InlineData("{guard,o|ff}", "TagGuardOff")]
    [InlineData("{set,#land,\"Ö|sterreich\"}", "TagSet")]
    [InlineData("{start,no|tepad}", "TagStart")]
    [InlineData("{start,https://exa|mple.com}", "TagStartUrl")]
    [InlineData("{# eine |Notiz}", "TagComment")]
    [InlineData("{tab,|x}", "TagNextField")] // fehlerhaft, die Hilfe gibt es trotzdem
    public void Tags_have_their_help(string marked, string key) => Assert.Equal(key, At(marked)?.Key);

    [Fact]
    public void Topic_covers_the_whole_tag()
    {
        var topic = At("Max{sh|ift+tab}Muster")!;
        Assert.Equal((3, 11), (topic.Start, topic.Length));
    }

    [Fact]
    public void Other_keys_name_the_key()
    {
        Assert.Equal(new HelpTopic(0, 8, TagHelp.AnyKey, "ctrl+s"), At("{ctrl+|s}"));
        Assert.Equal(new HelpTopic(0, 8, TagHelp.AnyKey, "pgdn", Repeated: true), At("{pgdn,|2}"));
    }

    [Fact]
    public void Repeated_key_mentions_the_repeat() =>
        Assert.Equal(new HelpTopic(0, 8, "TagDown", Repeated: true), At("{down,3|}"));

    [Theory]
    [InlineData("M|ax{tab}")]
    [InlineData("Ma|x{tab}")] // das Zeichen davor
    [InlineData("{tab}|")] // das Zeichen danach
    [InlineData("{na|me}")] // kein Tag, Text in Klammern
    [InlineData("{\"a\": |1}")]
    public void Text_has_no_help(string marked) => Assert.Null(At(marked));

    [Theory]
    [InlineData("{{ da|te() }}", "TagDate", 3)]
    [InlineData("{{ const r = r|un('a.bat', { dir: 'd:/' }) }}", "TagRunOptions", 13)]
    [InlineData("{{ clip|board.trim() }}", "TagClipboard", 3)]
    [InlineData("{{ ['a', 'b'].join(k|ey('tab')) }}", "TagKeyHelper", 19)]
    public void Script_helpers_have_their_help(string marked, string key, int start)
    {
        var topic = At(marked)!;
        Assert.Equal(key, topic.Key);
        Assert.Equal(start, topic.Start);
    }

    [Theory]
    [InlineData("{{ x.da|te }}")] // Eigenschaft, kein Helfer
    [InlineData("{{ 'da|te' }}")] // Zeichenkette
    [InlineData("{{ // da|te\n1 }}")] // Kommentar
    [InlineData("{{ Ma|th.max(1, 2) }}")]
    public void Other_script_code_has_no_help(string marked) => Assert.Null(At(marked));

    [Fact]
    public void Turned_off_tags_and_scripts_are_text()
    {
        Assert.Null(At("{t|ab}", keyTags: false));
        Assert.Null(At("{# No|tiz}", keyTags: false));
        Assert.Null(At("{{ da|te() }}", scripts: false));
    }
}
