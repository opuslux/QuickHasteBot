using System.Text;
using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>{cursor} und {html}…{/html}: Schritte, Einfüge-Inhalt, Klartext und das Format der Zwischenablage.</summary>
public class FormattedTextTests
{
    static IReadOnlyList<FormStep> Steps(string text)
    {
        var form = new FormBuilder();
        text = KeyTags.MarkTags(text, form);
        text = SnippetScript.Render(text, SnippetScriptTests.Context with { Form = form });
        return KeyTags.Split(text, form);
    }

    static string Show(string text) =>
        string.Concat(Steps(text).Select(s => s is TextStep t ? "«" + t.Text + "»" : KeyTags.Describe(s)));

    [Fact]
    public void Html_block_is_one_step_with_its_content() =>
        Assert.Equal("«Guten Tag»{html}<b>Max</b>{/html}", Show("Guten Tag\r\n{html}\r\n<b>Max</b>\r\n{/html}\r\n"));

    [Fact]
    public void Tags_inside_html_are_text_scripts_run() =>
        Assert.Equal("{html}<p>{tab} 42</p>{/html}", Show("{html}<p>{tab} {{ 6 * 7 }}</p>{/html}"));

    [Theory]
    [InlineData("{html}<b>x</b>")]
    [InlineData("<b>x</b>{/html}")]
    public void Html_without_its_partner_is_text(string text) =>
        Assert.Equal(text, Assert.IsType<TextStep>(Assert.Single(Steps(text))).Text);

    [Fact]
    public void Cursor_keeps_the_line_breaks_around_it() =>
        Assert.Equal("«Guten Tag »{cursor}«,\r\nMax»", Show("Guten Tag {cursor},\r\nMax"));

    [Fact]
    public void Plain_text_with_cursor_moves_back_over_what_follows()
    {
        var content = PasteContent.From(Steps("Guten Tag {cursor},\r\nMax 😀\r\n"));
        Assert.Equal("Guten Tag ,\r\nMax 😀", content.Text);
        Assert.Null(content.Html);
        // ",", Umbruch, "Max", " ", Emoji
        Assert.Equal(7, content.CaretBack);
    }

    [Fact]
    public void Only_text_cursor_and_html_fit_a_paste()
    {
        Assert.True(PasteContent.Fits(Steps("A{cursor}B{html}<i>C</i>{/html}")));
        Assert.False(PasteContent.Fits(Steps("A{tab}B")));
    }

    [Fact]
    public void Html_paste_escapes_the_text_around_the_block()
    {
        var content = PasteContent.From(Steps("A & B\r\n{html}<b>fett</b>{/html}"));
        Assert.Equal("A &amp; B<b>fett</b>", content.Html);
        Assert.Equal("A & Bfett", content.Text);
    }

    [Fact]
    public void Clean_fragment_takes_the_copied_part_without_office_extras()
    {
        const string clipboard = "Version:0.9\r\nStartHTML:0000000105\r\n<html><body>\r\n<!--StartFragment--><p class=MsoNormal>Gr&uuml;sse<o:p></o:p></p>" +
            "<!--[if gte vml 1]><v:shape>x</v:shape><![endif]--><![if !vml]><img src=\"https://example.com/logo.png\"><![endif]>" +
            "<!-- Kommentar --><!--EndFragment-->\r\n</body></html>";
        Assert.Equal("<p class=MsoNormal>Gr&uuml;sse</p><img src=\"https://example.com/logo.png\">", HtmlText.CleanFragment(clipboard));
    }

    [Fact]
    public void Plain_text_of_html_keeps_lines_lists_and_cells()
    {
        const string html = "<p>Freundliche\r\n  Grüsse</p><p>Max&nbsp;Muster<br>Bern</p><ul><li>eins</li><li>zwei</li></ul>" +
            "<table><tr><td>A</td><td>B</td></tr></table><style>p { color: red }</style>";
        Assert.Equal("Freundliche Grüsse\nMax Muster\nBern\n• eins\n• zwei\nA\tB", HtmlText.ToPlain(html));
    }

    [Fact]
    public void Escape_makes_text_safe_html() =>
        Assert.Equal("a &lt;b&gt; &amp; &quot;c&quot;<br>d", HtmlText.Escape("a <b> & \"c\"\r\nd"));

    [Fact]
    public void Clipboard_format_offsets_count_utf8_bytes()
    {
        const string fragment = "<b>Grüsse 😀</b>";
        var bytes = HtmlText.ClipboardFormat(fragment);
        var text = Encoding.UTF8.GetString(bytes);
        int Offset(string name) => int.Parse(text.Split(name + ":")[1][..10]);

        Assert.StartsWith("Version:0.9\r\n", text);
        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes, Offset("StartFragment"), Offset("EndFragment") - Offset("StartFragment")));
        Assert.Equal(bytes.Length, Offset("EndHTML"));
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(bytes, Offset("StartHTML"), 6));
    }
}
