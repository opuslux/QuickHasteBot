using System.Windows.Input;
using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class FileHotkeyTests
{
    static readonly HotkeyGesture CtrlAltG = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.G);

    [Theory]
    [InlineData("Gruss [Ctrl+Alt+G]", "Gruss")]
    [InlineData("Gruss[Ctrl+Alt+G]", "Gruss")]
    [InlineData("Gruss [ctrl+alt+g]", "Gruss")]
    [InlineData("Gruss [Strg+Alt+G]", "Gruss")]
    [InlineData("Gruss [Alt+Ctrl+G]", "Gruss")]
    public void Tag_at_the_end_is_the_hotkey(string stem, string name)
    {
        var (parsed, hotkey) = FileHotkey.Split(stem);
        Assert.Equal(name, parsed);
        Assert.Equal(CtrlAltG, hotkey);
    }

    [Theory]
    [InlineData("Bericht [Entwurf]")]
    [InlineData("Bericht [G]")] // ohne Modifier nur Funktionstasten
    [InlineData("[Ctrl+Alt+G] Gruss")] // nur am Ende
    [InlineData("Gruss [Ctrl+Alt+G] alt")]
    [InlineData("Gruss (Ctrl+Alt+G)")]
    [InlineData("Gruss")]
    public void Anything_else_stays_part_of_the_name(string stem)
    {
        var (name, hotkey) = FileHotkey.Split(stem);
        Assert.Equal(stem, name);
        Assert.Null(hotkey);
    }

    [Fact]
    public void Function_key_alone_is_allowed()
    {
        Assert.Equal(new HotkeyGesture(HotkeyModifiers.None, Key.F9), FileHotkey.Split("Abwesenheit [F9]").Hotkey);
    }

    [Fact]
    public void Name_that_is_only_the_tag_keeps_the_tag_as_name()
    {
        var (name, hotkey) = FileHotkey.Split("[Ctrl+Alt+G]");
        Assert.Equal("[Ctrl+Alt+G]", name);
        Assert.Equal(CtrlAltG, hotkey);
    }

    [Theory]
    [InlineData("Gruss.txt", false, "Gruss [Ctrl+Alt+G].txt")]
    [InlineData("Gruss [Ctrl+Shift+F5].txt", false, "Gruss [Ctrl+Alt+G].txt")]
    [InlineData("Bericht [Entwurf].txt", false, "Bericht [Entwurf] [Ctrl+Alt+G].txt")]
    [InlineData("v1.2 Notizen", true, "v1.2 Notizen [Ctrl+Alt+G]")] // Ordner: der Punkt ist keine Endung
    [InlineData("Intranet.url", false, "Intranet [Ctrl+Alt+G].url")]
    [InlineData("Notiz", false, "Notiz [Ctrl+Alt+G]")]
    [InlineData("[Ctrl+Shift+F5].txt", false, "[Ctrl+Alt+G].txt")]
    public void Sets_or_replaces_the_tag(string fileName, bool isFolder, string expected)
    {
        Assert.Equal(expected, FileHotkey.WithHotkey(fileName, CtrlAltG, isFolder));
    }

    [Theory]
    [InlineData("Gruss [Ctrl+Alt+G].txt", false, "Gruss.txt")]
    [InlineData("3-SQL [Ctrl+Alt+G]", true, "3-SQL")]
    [InlineData("Gruss.txt", false, "Gruss.txt")]
    [InlineData("[Ctrl+Alt+G].txt", false, "Ctrl+Alt+G.txt")] // nicht ".txt"
    public void Removes_the_tag(string fileName, bool isFolder, string expected)
    {
        Assert.Equal(expected, FileHotkey.WithoutHotkey(fileName, isFolder));
    }

    [Fact]
    public void Written_tag_parses_back()
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Win, Key.D7);
        var name = FileHotkey.WithHotkey("Gruss.txt", gesture, isFolder: false);
        Assert.Equal("Gruss [Ctrl+Shift+Win+7].txt", name);
        Assert.Equal(gesture, FileHotkey.FromFileName(name, isFolder: false));
        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public void Library_shows_names_without_tags_and_reads_hotkeys()
    {
        using var dir = new TempDirectory();
        dir.File(@"3-SQL [Ctrl+Alt+S]\Audit [Ctrl+Alt+A].txt", "insert");
        dir.File("Bericht [Entwurf].txt", "x");

        using var library = new SnippetLibrary(dir.Path, [".txt"]);

        var folder = library.All.Single(s => s.IsFolder);
        var audit = library.All.Single(s => s.Kind == SnippetKind.Text && s.Hotkey is not null);
        Assert.Equal("3-SQL", folder.Name);
        Assert.Equal(new HotkeyGesture(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.S), folder.Hotkey);
        Assert.Equal("Audit", audit.Name);
        Assert.Equal("3-SQL", audit.Location);
        Assert.Null(library.All.Single(s => s.Name == "Bericht [Entwurf]").Hotkey);
    }

    [Fact]
    public void Search_does_not_find_the_tag()
    {
        using var dir = new TempDirectory();
        dir.File("Gruss [Ctrl+Alt+G].txt", "Hallo");

        using var library = new SnippetLibrary(dir.Path, [".txt"]);

        Assert.Single(SnippetSearch.Search(library.All, "gruss", null));
        Assert.Empty(SnippetSearch.Search(library.All, "ctrl", null));
    }

    [Fact]
    public void Copy_name_drops_the_hotkey()
    {
        using var dir = new TempDirectory();
        var source = dir.File("Gruss [Ctrl+Alt+G].txt", "Gruss");
        Assert.Equal("Gruss - Kopie.txt", SnippetFiles.SuggestCopyName(source, " - Kopie"));
    }
}

public class FileHotkeyMapTests
{
    static readonly HotkeyGesture Main = HotkeyGesture.Default;

    [Fact]
    public void First_in_tree_order_wins_a_duplicate()
    {
        using var dir = new TempDirectory();
        dir.File(@"1-Grüsse\Gruss [Ctrl+Alt+G].txt");
        dir.File("Gutschein [Ctrl+Alt+G].txt");
        dir.File("Intranet [Ctrl+Alt+I].url");

        using var library = new SnippetLibrary(dir.Path, [".txt"]);
        var entries = FileHotkeyMap.Build(library.All, Main);

        Assert.Equal(3, entries.Count);
        var gruss = entries.Single(e => e.Snippet.Name == "Gruss");
        var gutschein = entries.Single(e => e.Snippet.Name == "Gutschein");
        Assert.Equal(FileHotkeyStatus.Ok, gruss.Status);
        Assert.Equal(FileHotkeyStatus.Duplicate, gutschein.Status);
        Assert.Same(gruss.Snippet, gutschein.Owner);
        Assert.Equal(FileHotkeyStatus.Ok, entries.Single(e => e.Snippet.Name == "Intranet").Status);
    }

    [Fact]
    public void Popup_hotkey_is_a_conflict()
    {
        using var dir = new TempDirectory();
        dir.File("Gruss [Ctrl+Shift+Y].txt");

        using var library = new SnippetLibrary(dir.Path, [".txt"]);

        Assert.Equal(FileHotkeyStatus.MainHotkey, FileHotkeyMap.Build(library.All, Main).Single().Status);
    }

    [Fact]
    public void Owner_ignores_the_entry_itself()
    {
        using var dir = new TempDirectory();
        var path = dir.File("Gruss [Ctrl+Alt+G].txt");

        using var library = new SnippetLibrary(dir.Path, [".txt"]);
        var gesture = library.All.Single().Hotkey!.Value;

        Assert.Null(FileHotkeyMap.FindOwner(library.All, gesture, path));
        Assert.Same(library.All.Single(), FileHotkeyMap.FindOwner(library.All, gesture, exceptPath: null));
    }
}
