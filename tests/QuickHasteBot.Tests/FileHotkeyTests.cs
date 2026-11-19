using System.Windows.Input;
using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>Datei-Hotkeys kommen aus snippets.json, nicht mehr aus dem Namen.</summary>
public class FileHotkeyTests
{
    static readonly HotkeyGesture CtrlAltG = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.G);
    static readonly HotkeyGesture CtrlAltS = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.S);

    [Fact]
    public void Library_reads_hotkeys_from_the_data()
    {
        using var dir = new TempDirectory();
        using var dataDir = new TempDirectory();
        dir.File(@"3-SQL\Audit.txt", "insert");
        dir.File("Notiz.txt", "x");
        using var data = SnippetData.Load(Path.Combine(dataDir.Path, "snippets.json"));
        data.SetHotkey("3-SQL", CtrlAltS);
        data.SetHotkey(@"3-sql\audit.txt", CtrlAltG); // Gross/Klein egal

        using var library = new SnippetLibrary(dir.Path, [".txt"], data.HotkeyFor);

        Assert.Equal(CtrlAltS, library.All.Single(s => s.Name == "3-SQL").Hotkey);
        Assert.Equal(CtrlAltG, library.All.Single(s => s.Name == "Audit").Hotkey);
        Assert.Null(library.All.Single(s => s.Name == "Notiz").Hotkey);
    }

    /// <summary>Ein altes Tag im Namen ist nur noch Teil des Namens: kein Hotkey, und die Suche findet es wie jeden Text.</summary>
    [Fact]
    public void Old_tag_in_the_name_is_just_part_of_the_name()
    {
        using var dir = new TempDirectory();
        dir.File(@"3-SQL [Ctrl+Alt+S]\Audit [Ctrl+Alt+A].txt", "insert");

        using var library = new SnippetLibrary(dir.Path, [".txt"]);

        var audit = library.All.Single(s => !s.IsFolder);
        Assert.Equal("Audit [Ctrl+Alt+A]", audit.Name);
        Assert.Equal("3-SQL [Ctrl+Alt+S]", audit.Location);
        Assert.All(library.All, s => Assert.Null(s.Hotkey));
        Assert.Single(SnippetSearch.Search(library.All, "audit ctrl", null));
    }

    [Fact]
    public void Copy_name_keeps_the_name()
    {
        using var dir = new TempDirectory();
        var source = dir.File("Gruss [Entwurf].txt", "Gruss");
        Assert.Equal("Gruss [Entwurf] - Kopie.txt", SnippetFiles.SuggestCopyName(source, " - Kopie"));
    }
}

public class FileHotkeyMapTests
{
    static readonly HotkeyGesture Main = HotkeyGesture.Default;
    static readonly HotkeyGesture CtrlAltG = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.G);
    static readonly HotkeyGesture CtrlAltI = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.I);

    static SnippetLibrary Library(TempDirectory dir, SnippetData data) => new(dir.Path, [".txt"], data.HotkeyFor);

    static SnippetData Data(TempDirectory dataDir) => SnippetData.Load(Path.Combine(dataDir.Path, "snippets.json"));

    [Fact]
    public void First_in_tree_order_wins_a_duplicate()
    {
        using var dir = new TempDirectory();
        using var dataDir = new TempDirectory();
        dir.File(@"1-Grüsse\Gruss.txt");
        dir.File("Gutschein.txt");
        dir.File("Intranet.url");
        using var data = Data(dataDir);
        data.SetHotkey(@"1-Grüsse\Gruss.txt", CtrlAltG);
        data.SetHotkey("Gutschein.txt", CtrlAltG);
        data.SetHotkey("Intranet.url", CtrlAltI);

        using var library = Library(dir, data);
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
        using var dataDir = new TempDirectory();
        dir.File("Gruss.txt");
        using var data = Data(dataDir);
        data.SetHotkey("Gruss.txt", Main);

        using var library = Library(dir, data);

        Assert.Equal(FileHotkeyStatus.MainHotkey, FileHotkeyMap.Build(library.All, Main).Single().Status);
    }

    [Fact]
    public void Owner_ignores_the_entry_itself()
    {
        using var dir = new TempDirectory();
        using var dataDir = new TempDirectory();
        var path = dir.File("Gruss.txt");
        using var data = Data(dataDir);
        data.SetHotkey("Gruss.txt", CtrlAltG);

        using var library = Library(dir, data);

        Assert.Null(FileHotkeyMap.FindOwner(library.All, CtrlAltG, path));
        Assert.Same(library.All.Single(), FileHotkeyMap.FindOwner(library.All, CtrlAltG, exceptPath: null));
    }

    [Fact]
    public void Hotkeys_without_entry_are_orphans()
    {
        using var dir = new TempDirectory();
        using var dataDir = new TempDirectory();
        dir.File("Gruss.txt");
        using var data = Data(dataDir);
        data.SetHotkey("Gruss.txt", CtrlAltG);
        data.SetHotkey(@"Alt\Weg.txt", CtrlAltI);

        using var library = Library(dir, data);

        var orphan = Assert.Single(FileHotkeyMap.Orphans(data.Hotkeys(), library.All));
        Assert.Equal(@"Alt\Weg.txt", orphan.RelativePath);
        Assert.Equal(CtrlAltI, orphan.Gesture);
        // Verwaiste wirken nicht und blockieren nichts
        Assert.Single(FileHotkeyMap.Build(library.All, Main));
    }
}
