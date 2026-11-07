namespace QuickHasteBot.Tests;

/// <summary>Beginnt ein Baustein mit {window,…} oder {start,…}, startet der Editor ihn ohne Klick ins Zielfeld.</summary>
public class StartWindowTagTests
{
    static string? Tag(string text, bool keyTags = true, bool scripts = true) => AppController.StartWindowTag(text, keyTags, scripts);

    [Fact]
    public void Window_at_the_start_is_found() =>
        Assert.Equal("{window,process=notepad}", Tag("{window,process=notepad}Grüsse{tab}MfG"));

    [Fact]
    public void Start_at_the_start_is_found() =>
        Assert.StartsWith("{start,", Tag("{start,notepad.exe}Hallo"));

    [Fact]
    public void Pauses_and_guard_before_it_do_not_count() =>
        Assert.Equal("{window,process=notepad}", Tag("{delay,200}{guard,off}{window,process=notepad}x"));

    [Theory]
    [InlineData("Grüsse{window,process=notepad}")]
    [InlineData("{tab}{window,process=notepad}")]
    [InlineData("{wait,100}{window,process=notepad}")]
    [InlineData("Nur Text, keine Tags")]
    [InlineData("")]
    public void Anything_else_first_needs_the_click(string text) => Assert.Null(Tag(text));

    [Fact]
    public void Without_key_tags_nothing_is_a_window() =>
        Assert.Null(Tag("{window,process=notepad}x", keyTags: false));

    [Fact]
    public void Scripts_count_with_what_they_output()
    {
        Assert.Equal("{window,process=notepad}", Tag("{{ '' }}{window,process=notepad}x"));
        Assert.Null(Tag("{{ 'Hallo' }}{window,process=notepad}x"));
        Assert.Null(Tag("{{ key('tab') }}{window,process=notepad}x"));
    }

    [Fact]
    public void A_broken_script_needs_the_click() =>
        Assert.Null(Tag("{{ nicht( }}{window,process=notepad}x"));
}
