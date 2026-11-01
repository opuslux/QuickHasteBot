using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class SnippetLibraryTests
{
    static SnippetLibrary Create(TempDirectory dir) => new(dir.Path, [".txt"]);

    [Fact]
    public void Folders_first_then_files_sorted_by_name()
    {
        using var dir = new TempDirectory();
        dir.File(@"2-Adressen\b.txt", "b");
        dir.File(@"1-Temp\a.txt", "a");
        dir.File("ascii-table.txt");
        dir.File("Aaa.txt");

        using var library = Create(dir);

        Assert.Equal(["1-Temp", "2-Adressen", "Aaa", "ascii-table"], library.Root.Children.Select(c => c.Name));
    }

    [Fact]
    public void Classifies_text_and_other_files()
    {
        using var dir = new TempDirectory();
        dir.File(@"6-Weblinks\www.google.com.url", "[InternetShortcut]\r\nURL=https://www.google.com");
        dir.File(@"5-SQL\Show-Schemas.TXT", "select 1");

        using var library = Create(dir);

        var url = library.All.Single(s => s.Name == "www.google.com");
        var sql = library.All.Single(s => s.Name == "Show-Schemas");
        Assert.Equal(SnippetKind.File, url.Kind);
        Assert.Equal(SnippetKind.Text, sql.Kind);
        Assert.Equal("6-Weblinks", url.Location);
        Assert.Equal(@"5-SQL\Show-Schemas.TXT", sql.RelativePath);
    }

    [Fact]
    public void Nested_location_uses_separators()
    {
        using var dir = new TempDirectory();
        dir.File(@"1-Temp\Neuer Ordner\x.txt");

        using var library = Create(dir);

        Assert.Equal("1-Temp › Neuer Ordner", library.All.Single(s => s.Name == "x").Location);
        Assert.Same(library.All.Single(s => s.Name == "Neuer Ordner"), library.Find(@"1-Temp\Neuer Ordner"));
        Assert.Same(library.Root, library.Find(""));
    }

    [Fact]
    public void Skips_hidden_and_system_files()
    {
        using var dir = new TempDirectory();
        dir.File("visible.txt");
        dir.File("desktop.ini");
        var hidden = dir.File("hidden.txt");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        using var library = Create(dir);

        Assert.Equal(["visible"], library.All.Select(s => s.Name));
    }

    [Fact]
    public void Missing_root_gives_empty_library()
    {
        using var library = new SnippetLibrary(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), [".txt"]);
        Assert.Empty(library.All);
    }

    [Fact]
    public void Searchable_text_is_refreshed_after_change()
    {
        using var dir = new TempDirectory();
        var path = dir.File("a.txt", "alt");
        using var library = Create(dir);
        var snippet = library.All.Single();

        Assert.Equal("alt", library.GetSearchableText(snippet));

        File.WriteAllText(path, "neu");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal("neu", library.GetSearchableText(snippet));
    }

    [Fact]
    public void Watcher_picks_up_new_files()
    {
        using var dir = new TempDirectory();
        dir.File("a.txt");
        using var library = Create(dir);
        using var changed = new ManualResetEventSlim();
        library.Changed += (_, _) => changed.Set();
        library.StartWatching();

        dir.File(@"Neu\b.txt", "b");

        Assert.True(changed.Wait(TimeSpan.FromSeconds(5)), "Changed wurde nicht ausgelöst");
        Assert.Contains(library.All, s => s.Name == "b");
    }
}
