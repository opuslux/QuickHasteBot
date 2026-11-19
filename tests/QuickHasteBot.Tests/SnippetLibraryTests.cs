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

    static readonly HotkeyGesture CtrlAltG = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, System.Windows.Input.Key.G);

    /// <summary>Bibliothek mit Hotkeys aus snippets.json (in einem eigenen Ordner), Umbenennungen ziehen sie mit.</summary>
    static (SnippetLibrary Library, SnippetData Data) CreateWithData(TempDirectory dir, TempDirectory dataDir)
    {
        var data = SnippetData.Load(Path.Combine(dataDir.Path, "snippets.json"));
        var library = new SnippetLibrary(dir.Path, [".txt"], data.HotkeyFor);
        library.Renamed += (oldPath, newPath) => data.Rename(oldPath, newPath);
        return (library, data);
    }

    /// <summary>Wartet, bis nach einer Änderung im Ordner der Baum <paramref name="done"/> erfüllt.</summary>
    static void WaitFor(SnippetLibrary library, Func<bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(5))
            Thread.Sleep(50);
        Assert.True(done(), "Der Baum hat sich nicht wie erwartet geändert");
    }

    [Fact]
    public void Hotkey_follows_a_rename_in_explorer()
    {
        using var dir = new TempDirectory();
        using var dataDir = new TempDirectory();
        var path = dir.File("Gruss.txt", "Hallo");
        var (library, data) = CreateWithData(dir, dataDir);
        using var _ = library;
        using var __ = data;
        data.SetHotkey("Gruss.txt", CtrlAltG);
        library.Reload();
        library.StartWatching();

        File.Move(path, Path.Combine(dir.Path, "Grüsse.txt"));

        WaitFor(library, () => library.All.Any(s => s.Name == "Grüsse" && s.Hotkey == CtrlAltG));
        Assert.Null(data.HotkeyFor("Gruss.txt"));
    }

    [Fact]
    public void Hotkey_follows_a_move_inside_the_folder()
    {
        using var dir = new TempDirectory();
        using var dataDir = new TempDirectory();
        dir.File(@"SQL\Audit.txt", "insert");
        Directory.CreateDirectory(Path.Combine(dir.Path, "Archiv"));
        var (library, data) = CreateWithData(dir, dataDir);
        using var _ = library;
        using var __ = data;
        data.SetHotkey(@"SQL\Audit.txt", CtrlAltG);
        library.StartWatching();

        // Ordner samt Inhalt verschoben, wie Ziehen im Explorer
        Directory.Move(Path.Combine(dir.Path, "SQL"), Path.Combine(dir.Path, @"Archiv\SQL"));

        WaitFor(library, () => library.All.Any(s => s.RelativePath == @"Archiv\SQL\Audit.txt" && s.Hotkey == CtrlAltG));
    }

    /// <summary>Manche Editoren benennen beim Speichern das Original in ein Backup um und schreiben es neu.</summary>
    [Fact]
    public void Hotkey_stays_with_the_original_when_an_editor_renames_it_to_a_backup()
    {
        using var dir = new TempDirectory();
        using var dataDir = new TempDirectory();
        var path = dir.File("Gruss.txt", "Hallo");
        var (library, data) = CreateWithData(dir, dataDir);
        using var _ = library;
        using var __ = data;
        data.SetHotkey("Gruss.txt", CtrlAltG);
        library.StartWatching();

        File.Move(path, path + ".bak");
        File.WriteAllText(path, "Hallo neu");

        WaitFor(library, () => library.All.Any(s => s.Name == "Gruss.txt"));
        Assert.Equal(CtrlAltG, library.All.Single(s => s.Name == "Gruss").Hotkey);
        Assert.Null(library.All.Single(s => s.Name == "Gruss.txt").Hotkey);
    }

    [Fact]
    public void Moves_pair_only_unique_names_in_another_folder()
    {
        var moves = SnippetLibrary.Moves(
            ["SQL", "a.txt", @"x\b.txt", @"y\b.txt"],
            [@"Archiv\SQL", "a.txt", @"z\b.txt"]).ToList();

        // a.txt: im selben Ordner neu (Editor), b.txt: zwei gelöschte, welcher wäre es?
        Assert.Equal([("SQL", @"Archiv\SQL")], moves);
    }

    [Fact]
    public void Relative_paths_only_inside_the_folder()
    {
        using var dir = new TempDirectory();
        using var library = Create(dir);

        Assert.Equal(@"SQL\Audit.txt", library.RelativeOf(Path.Combine(dir.Path, @"SQL\Audit.txt")));
        Assert.Null(library.RelativeOf(dir.Path));
        Assert.Null(library.RelativeOf(dir.Path + "-anders\\a.txt"));
    }

    [Fact]
    public void Verify_picks_up_what_the_watcher_missed_and_stays_quiet_otherwise()
    {
        using var dir = new TempDirectory();
        dir.File("a.txt");
        using var library = Create(dir);
        var changes = 0;
        library.Changed += (_, _) => changes++;

        // Ohne Watcher, wie bei verlorenen Events
        dir.File(@"Neu\b.txt", "b");
        Assert.True(library.Verify());
        Assert.Contains(library.All, s => s.Name == "b");
        Assert.Equal(1, changes);

        // Nichts geändert: kein Changed, sonst flackert das Popup bei jedem Hotkey
        Assert.False(library.Verify());
        Assert.Equal(1, changes);

        // Auch eine Umbenennung nur in der Gross-/Kleinschreibung zählt
        Directory.Move(Path.Combine(dir.Path, "Neu"), Path.Combine(dir.Path, "neu"));
        Assert.True(library.Verify());
        Assert.Contains(library.All, s => s.Name == "neu");
    }

    [Fact]
    public void Verify_watches_again_after_structure_was_replaced_in_explorer()
    {
        using var dir = new TempDirectory();
        var root = Path.Combine(dir.Path, "structure");
        dir.File(@"structure\alt.txt");
        using var library = new SnippetLibrary(root, [".txt"]);
        using var changed = new ManualResetEventSlim();
        library.Changed += (_, _) => changed.Set();
        library.StartWatching();

        // Im Explorer weggeschoben und durch einen anderen Ordner ersetzt: der Watcher hängt noch am alten
        Directory.Move(root, Path.Combine(dir.Path, "structure-alt"));
        dir.File(@"structure\1-Neu\neu.txt");
        Directory.SetCreationTimeUtc(root, DateTime.UtcNow.AddMinutes(1));

        Assert.True(library.Verify());
        Assert.Equal(["1-Neu", "neu"], library.All.Select(s => s.Name));

        // Und er beobachtet wieder den richtigen Ordner
        changed.Reset();
        dir.File(@"structure\danach.txt");
        Assert.True(changed.Wait(TimeSpan.FromSeconds(5)), "Changed wurde nicht ausgelöst");
        Assert.Contains(library.All, s => s.Name == "danach");
    }

    [Fact]
    public void Verify_starts_watching_once_a_missing_structure_appears()
    {
        using var dir = new TempDirectory();
        var root = Path.Combine(dir.Path, "structure");
        using var library = new SnippetLibrary(root, [".txt"]);
        using var changed = new ManualResetEventSlim();
        library.Changed += (_, _) => changed.Set();
        library.StartWatching();
        Assert.Empty(library.All);

        // Z.B. ein Netzlaufwerk, das beim Start noch nicht da war
        dir.File(@"structure\da.txt");
        Assert.True(library.Verify());

        changed.Reset();
        dir.File(@"structure\auch.txt");
        Assert.True(changed.Wait(TimeSpan.FromSeconds(5)), "Changed wurde nicht ausgelöst");
        Assert.Equal(["auch", "da"], library.All.Select(s => s.Name));
    }
}
