using System.Diagnostics;
using System.Windows.Input;
using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class SnippetDataTests
{
    static readonly HotkeyGesture CtrlAltG = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.G);
    static readonly DateTime Start = new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);

    sealed class Clock
    {
        public DateTime Now { get; set; } = Start;
    }

    static SnippetData Load(TempDirectory dir, Clock? clock = null)
    {
        clock ??= new Clock();
        return SnippetData.Load(Path.Combine(dir.Path, "snippets.json"), () => clock.Now);
    }

    static Snippet Snippet(string relativePath) => new()
    {
        Name = Path.GetFileNameWithoutExtension(relativePath),
        FullPath = @"C:\s\" + relativePath,
        RelativePath = relativePath,
        Kind = SnippetKind.Text,
    };

    [Fact]
    public void Survives_a_restart()
    {
        using var dir = new TempDirectory();
        using (var data = Load(dir))
        {
            data.SetHotkey(@"1-Grüsse\Gruss.txt", CtrlAltG);
            data.RecordUse(@"1-Grüsse\Gruss.txt", UseSource.Search, "Grüsse");
            data.RecordUse("Notiz.txt", UseSource.Hotkey);
        }

        using var reloaded = Load(dir);
        Assert.Equal(CtrlAltG, reloaded.HotkeyFor(@"1-grüsse\GRUSS.txt"));
        Assert.Equal(Start, reloaded.Since);
        Assert.NotNull(reloaded.BonusFor("grusse"));
        // Von Hand lesbar
        var json = File.ReadAllText(Path.Combine(dir.Path, "snippets.json"));
        Assert.Contains("\"Hotkey\": \"Ctrl+Alt+G\"", json);
        Assert.Contains("Grüsse", json);
    }

    [Fact]
    public void Broken_file_starts_empty_and_is_kept_as_backup()
    {
        using var dir = new TempDirectory();
        var path = dir.File("snippets.json", "{ kaputt");

        using var data = Load(dir);

        Assert.Empty(data.Hotkeys());
        Assert.Equal("{ kaputt", File.ReadAllText(path + ".bad"));
    }

    [Fact]
    public void Unreadable_hotkey_is_ignored()
    {
        using var dir = new TempDirectory();
        dir.File("snippets.json", """{ "Entries": { "a.txt": { "Hotkey": "Ctrl+Blabla" }, "b.txt": null } }""");

        using var data = Load(dir);

        Assert.Null(data.HotkeyFor("a.txt"));
        Assert.Empty(data.Hotkeys());
    }

    [Fact]
    public void Removing_the_last_thing_removes_the_entry()
    {
        using var dir = new TempDirectory();
        using var data = Load(dir);
        data.SetHotkey("a.txt", CtrlAltG);
        data.SetHotkey("a.txt", null);

        Assert.DoesNotContain("a.txt", File.ReadAllText(data.FilePath));
    }

    [Fact]
    public void Rename_moves_file_data_and_everything_in_a_folder()
    {
        using var dir = new TempDirectory();
        using var data = Load(dir);
        data.SetHotkey("SQL", CtrlAltG);
        data.RecordUse(@"SQL\Audit.txt", UseSource.Search, "audit");
        data.RecordUse(@"SQL-Alt\Audit.txt", UseSource.Search, "audit"); // gleicher Anfang, anderer Ordner

        Assert.True(data.Rename("SQL", "3 - SQL"));

        Assert.Null(data.HotkeyFor("SQL"));
        Assert.Equal(CtrlAltG, data.HotkeyFor("3 - SQL"));
        var bonus = data.BonusFor("audit")!;
        Assert.True(bonus(Snippet(@"3 - SQL\Audit.txt")) > 0);
        Assert.Equal(0, bonus(Snippet(@"SQL\Audit.txt")));
        Assert.True(bonus(Snippet(@"SQL-Alt\Audit.txt")) > 0);
    }

    [Fact]
    public void Rename_only_in_case_keeps_the_data()
    {
        using var dir = new TempDirectory();
        using var data = Load(dir);
        data.SetHotkey("gruss.txt", CtrlAltG);

        data.Rename("gruss.txt", "Gruss.txt");

        Assert.Equal(CtrlAltG, data.HotkeyFor("Gruss.txt"));
        Assert.Equal("Gruss.txt", Assert.Single(data.Hotkeys()).RelativePath);
    }

    [Fact]
    public void Rename_without_data_changes_nothing()
    {
        using var dir = new TempDirectory();
        using var data = Load(dir);
        data.SetHotkey("b.txt", CtrlAltG);

        Assert.False(data.Rename("a.txt", "b.txt"));
        Assert.Equal(CtrlAltG, data.HotkeyFor("b.txt"));
    }

    [Fact]
    public void Remove_takes_the_folder_contents_and_learned_queries()
    {
        using var dir = new TempDirectory();
        using var data = Load(dir);
        data.SetHotkey(@"SQL\Audit.txt", CtrlAltG);
        data.RecordUse(@"SQL\Audit.txt", UseSource.Search, "audit");

        data.Remove("SQL");

        Assert.Empty(data.Hotkeys());
        Assert.Null(data.BonusFor("audit"));
    }

    [Fact]
    public void Clear_usage_keeps_hotkeys_and_restarts_counting()
    {
        using var dir = new TempDirectory();
        var clock = new Clock();
        using var data = Load(dir, clock);
        data.SetHotkey("a.txt", CtrlAltG);
        data.RecordUse("a.txt", UseSource.Browse, "a");
        data.RecordUse("b.txt", UseSource.Browse);

        clock.Now = Start.AddDays(3);
        data.ClearUsage();

        Assert.Null(data.BonusFor("a"));
        Assert.Equal(CtrlAltG, data.HotkeyFor("a.txt"));
        Assert.Equal(Start.AddDays(3), data.Since);
        Assert.DoesNotContain("b.txt", File.ReadAllText(data.FilePath));
    }

    [Fact]
    public void Prune_drops_old_usage_of_missing_entries_but_keeps_hotkeys()
    {
        using var dir = new TempDirectory();
        var clock = new Clock();
        using var data = Load(dir, clock);
        data.RecordUse("weg-alt.txt", UseSource.Search, "weg");
        data.RecordUse("da.txt", UseSource.Search, "da");
        data.SetHotkey("weg-mit-hotkey.txt", CtrlAltG);
        clock.Now = Start.AddDays(200);
        data.RecordUse("weg-neu.txt", UseSource.Search, "neu");

        data.Prune(new HashSet<string>(["da.txt"], StringComparer.OrdinalIgnoreCase));
        data.Flush();

        var json = File.ReadAllText(data.FilePath);
        Assert.DoesNotContain("weg-alt.txt", json);
        Assert.Contains("da.txt", json);
        Assert.Contains("weg-neu.txt", json);
        Assert.Equal(CtrlAltG, data.HotkeyFor("weg-mit-hotkey.txt"));
    }

    [Fact]
    public void Lists_stay_short()
    {
        using var dir = new TempDirectory();
        var clock = new Clock();
        using var data = Load(dir, clock);
        for (var i = 0; i < 400; i++)
        {
            clock.Now = Start.AddMinutes(i);
            data.RecordUse("a.txt", UseSource.Search, $"suche {i}");
        }
        data.Flush();

        var json = File.ReadAllText(data.FilePath);
        // Die letzten 300 Suchtexte, die ältesten sind weg
        Assert.DoesNotContain("\"suche 99\"", json);
        Assert.Contains("\"suche 100\"", json);
        Assert.Contains("\"suche 399\"", json);
        Assert.Contains("\"Search\": 400", json);
        // 10 Zeitpunkte je Eintrag
        var recent = json[json.IndexOf("\"Recent\"", StringComparison.Ordinal)..];
        Assert.Equal(10, recent[..recent.IndexOf(']')].Count(c => c == 'Z'));
    }

    [Fact]
    public void Usage_bonus_is_moderate_and_query_bonus_strong()
    {
        using var dir = new TempDirectory();
        using var data = Load(dir);
        for (var i = 0; i < 20; i++)
            data.RecordUse("oft.txt", UseSource.Browse);
        data.RecordUse("gewaehlt.txt", UseSource.Search, "MfG");

        var bonus = data.BonusFor("mfg")!;
        Assert.Equal(UsageRanking.MaxUsageBonus, bonus(Snippet("oft.txt")));
        // Einmal gewählt: halber Suchtext-Bonus plus ein wenig allgemein
        Assert.Equal(30 + UsageRanking.UsageBonus(100), bonus(Snippet("gewaehlt.txt")));
        // Angefangen zu tippen: halb so viel
        Assert.Equal(15 + UsageRanking.UsageBonus(100), data.BonusFor("mf")!(Snippet("gewaehlt.txt")));
        Assert.Equal(0, bonus(Snippet("nie.txt")));
    }

    [Fact]
    public void Old_uses_count_less()
    {
        using var dir = new TempDirectory();
        var clock = new Clock();
        using var data = Load(dir, clock);
        data.RecordUse("a.txt", UseSource.Search, "a");
        data.RecordUse("a.txt", UseSource.Search, "a");

        Assert.Equal(UsageRanking.MaxQueryBonus, data.BonusFor("a")!(Snippet("a.txt")) - UsageRanking.UsageBonus(200));
        clock.Now = Start.AddDays(100);
        Assert.Equal(UsageRanking.QueryBonus(20) + UsageRanking.UsageBonus(20), data.BonusFor("a")!(Snippet("a.txt")));
    }

    [Fact]
    public void Saves_in_the_background_after_a_use()
    {
        using var dir = new TempDirectory();
        using var data = Load(dir);
        data.RecordUse("a.txt", UseSource.Browse);

        var path = Path.Combine(dir.Path, "snippets.json");
        var watch = Stopwatch.StartNew();
        while (!File.Exists(path) && watch.Elapsed < TimeSpan.FromSeconds(10))
            Thread.Sleep(50);
        Assert.Contains("a.txt", File.ReadAllText(path));
    }

    [Fact]
    public void Stats_show_counts_by_source_and_learned_queries()
    {
        using var dir = new TempDirectory();
        var clock = new Clock();
        using var data = Load(dir, clock);
        data.RecordUse("a.txt", UseSource.Search, "Gruss");
        clock.Now = Start.AddHours(1);
        data.RecordUse("a.txt", UseSource.Search, "mf");
        data.RecordUse("a.txt", UseSource.Search, "MF");
        data.RecordUse("a.txt", UseSource.Browse);
        data.RecordUse("a.txt", UseSource.Hotkey);
        data.SetHotkey("b.txt", CtrlAltG);

        var stats = data.StatsFor("A.TXT")!;
        Assert.Equal((3, 1, 1, 5), (stats.Search, stats.Browse, stats.Hotkey, stats.Total));
        Assert.Equal((Start, Start.AddHours(1)), (stats.First!.Value, stats.Last!.Value));
        // Die häufigsten zuerst, normalisiert
        Assert.Equal([("mf", 2), ("gruss", 1)], stats.Queries.Select(q => (q.Query, q.Count)));
        // Nur ein Hotkey ist keine Nutzung
        Assert.Null(data.StatsFor("b.txt"));
        Assert.Null(data.StatsFor("c.txt"));

        var all = data.AllStats();
        Assert.Equal(["a.txt"], all.Keys);
        Assert.Equal(stats, all["a.txt"] with { Queries = stats.Queries });
        Assert.Equal(stats.Queries, all["A.txt"].Queries);
    }

    [Fact]
    public void Forgetting_a_query_drops_only_its_bonus()
    {
        using var dir = new TempDirectory();
        using (var data = Load(dir))
        {
            data.RecordUse("a.txt", UseSource.Search, "mf");
            data.RecordUse("a.txt", UseSource.Search, "gruss");
            data.RecordUse("b.txt", UseSource.Search, "mf");
            var before = data.BonusFor("mf")!(Snippet("a.txt"));

            data.ForgetQuery("MF", "a.txt");

            Assert.True(data.BonusFor("mf")!(Snippet("a.txt")) < before);
            Assert.Equal(["gruss"], data.StatsFor("a.txt")!.Queries.Select(q => q.Query));
            Assert.Equal(["mf"], data.StatsFor("b.txt")!.Queries.Select(q => q.Query));
            Assert.Equal(2, data.StatsFor("a.txt")!.Search);
        }

        // Sofort gespeichert
        using var reloaded = Load(dir);
        Assert.Equal(["gruss"], reloaded.StatsFor("a.txt")!.Queries.Select(q => q.Query));
    }

    [Fact]
    public void Resetting_an_entry_keeps_its_hotkey_and_the_others()
    {
        using var dir = new TempDirectory();
        using var data = Load(dir);
        data.SetHotkey("a.txt", CtrlAltG);
        data.RecordUse("a.txt", UseSource.Search, "mf");
        data.RecordUse("b.txt", UseSource.Search, "mf");
        data.RecordUse("c.txt", UseSource.Browse);

        data.ResetUsage("a.txt");
        data.ResetUsage("c.txt");

        Assert.Null(data.StatsFor("a.txt"));
        Assert.Null(data.StatsFor("c.txt"));
        Assert.Equal(CtrlAltG, data.HotkeyFor("a.txt"));
        Assert.Equal(1, data.StatsFor("b.txt")!.Total);
        Assert.Equal(["b.txt"], data.AllStats().Keys);
        Assert.Equal(0, data.BonusFor("mf")!(Snippet("a.txt")));
    }
}
