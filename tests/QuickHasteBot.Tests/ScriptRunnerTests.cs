using System.Diagnostics;
using System.Globalization;
using System.Text;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;

namespace QuickHasteBot.Tests;

/// <summary>run() startet wirklich: Batch-Dateien im Temp-Ordner, versteckt, damit keine Fenster aufgehen.</summary>
public class ScriptRunnerTests
{
    static readonly Encoding Oem;

    static ScriptRunnerTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }

    static RunResult Run(string file, string? arguments = null, int wait = 10, Func<bool>? cancelled = null) =>
        ScriptRunner.Run(new RunRequest(file, arguments, wait, Hidden: true), cancelled ?? (() => false));

    /// <summary>Wie man eine .bat-Datei im Editor speichert: in der OEM-Codepage, damit cmd die Umlaute richtig ausgibt.</summary>
    static string Batch(TempDirectory dir, string name, string content)
    {
        var path = Path.Combine(dir.Path, name);
        File.WriteAllText(path, content.ReplaceLineEndings("\r\n"), Oem);
        return path;
    }

    [Fact]
    public void Batch_gives_output_errors_and_exit_code()
    {
        using var dir = new TempDirectory();
        var path = Batch(dir, "nummer.bat", "@echo off\necho Grüsse %1\necho kaputt 1>&2\nexit /b 3\n");
        var result = Run(path, "Max");
        Assert.Equal(3, result.Code);
        Assert.Equal("Grüsse Max", result.Out.Trim());
        Assert.Equal("kaputt", result.Err.Trim());
    }

    [Fact]
    public void Path_with_slashes_and_spaces_works()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "mit Leerzeichen"));
        var path = Batch(dir, Path.Combine("mit Leerzeichen", "a b.bat"), "@echo [%~1][%2]\n");
        Assert.Equal("[eins zwei][drei]", Run(path.Replace('\\', '/'), "\"eins zwei\" drei").Out.Trim());
    }

    [Fact]
    public void Script_runs_in_its_own_folder()
    {
        using var dir = new TempDirectory();
        var path = Batch(dir, "wo.bat", "@cd\n");
        Assert.Equal(Path.GetFullPath(dir.Path).TrimEnd('\\'), Run(path).Out.Trim().TrimEnd('\\'), ignoreCase: true);
    }

    [Fact]
    public void Script_runs_in_the_given_folder()
    {
        using var dir = new TempDirectory();
        using var work = new TempDirectory();
        var path = Batch(dir, "wo.bat", "@cd\n");
        var result = ScriptRunner.Run(new RunRequest(path, null, 10, Hidden: true, Directory: work.Path.Replace('\\', '/')), () => false);
        Assert.Equal(Path.GetFullPath(work.Path).TrimEnd('\\'), result.Out.Trim().TrimEnd('\\'), ignoreCase: true);
    }

    [Fact]
    public void Script_without_folder_is_found_in_the_given_folder()
    {
        using var dir = new TempDirectory();
        Batch(dir, "hier.bat", "@echo gefunden\n");
        var result = ScriptRunner.Run(new RunRequest("hier.bat", null, 10, Hidden: true, Directory: dir.Path), () => false);
        Assert.Equal("gefunden", result.Out.Trim());
    }

    [Fact]
    public void Missing_folder_is_reported() =>
        Assert.Contains("folder", Assert.Throws<RunFailedException>(() =>
            ScriptRunner.Run(new RunRequest("cmd.exe", "/d /c echo x", 10, Hidden: true, Directory: @"C:\gibts\nicht"), () => false)).Message);

    [Fact]
    public void Program_from_the_path() =>
        Assert.Equal("hallo", Run("cmd.exe", "/d /c echo hallo").Out.Trim());

    [Fact]
    public void Missing_file_is_reported() =>
        Assert.Contains("not found", Assert.Throws<RunFailedException>(() => Run(@"C:\gibts\nicht.bat")).Message);

    [Fact]
    public void Too_slow_is_reported_after_the_wait()
    {
        using var dir = new TempDirectory();
        var path = Batch(dir, "langsam.bat", "@ping -n 6 127.0.0.1 >nul\n");
        var watch = Stopwatch.StartNew();
        var ex = Assert.Throws<RunFailedException>(() => Run(path, wait: 1));
        Assert.Contains("langsam.bat didn't finish within 1 s", ex.Message);
        Assert.InRange(watch.Elapsed.TotalSeconds, 0.9, 4);
    }

    [Fact]
    public void Escape_stops_waiting()
    {
        using var dir = new TempDirectory();
        var path = Batch(dir, "langsam.bat", "@ping -n 6 127.0.0.1 >nul\n");
        var watch = Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() => Run(path, cancelled: () => watch.ElapsedMilliseconds > 300));
        Assert.True(watch.Elapsed.TotalSeconds < 3);
    }

    [Fact]
    public void Program_started_by_the_script_does_not_hold_it_up()
    {
        // ping erbt die Fehlerausgabe und hält sie 7 s offen, das Skript selbst ist sofort fertig
        using var dir = new TempDirectory();
        var path = Batch(dir, "starter.bat", "@echo hallo\n@start \"\" /b ping -n 8 127.0.0.1 >nul\n@exit /b 4\n");
        var watch = Stopwatch.StartNew();
        var result = Run(path, wait: 5);
        Assert.True(watch.Elapsed.TotalSeconds < 3, $"took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(4, result.Code);
        Assert.Equal("hallo", result.Out.Trim());
    }

    [Fact]
    public void Escape_works_while_a_started_program_holds_the_output()
    {
        using var dir = new TempDirectory();
        var path = Batch(dir, "starter.bat", "@start \"\" /b ping -n 8 127.0.0.1 >nul\n");
        var watch = Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() => Run(path, cancelled: () => watch.ElapsedMilliseconds > 300));
        Assert.True(watch.Elapsed.TotalSeconds < 1.5, $"took {watch.Elapsed.TotalSeconds:0.0} s");
    }

    [Fact]
    public void No_wait_only_starts()
    {
        using var dir = new TempDirectory();
        var marker = Path.Combine(dir.Path, "lief.txt");
        var path = Batch(dir, "anstossen.bat", $"@echo ja> \"{marker}\"\n");
        Assert.Equal(new RunResult(null, "", ""), Run(path, wait: 0));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(marker) && DateTime.UtcNow < deadline)
            Thread.Sleep(50);
        Assert.True(File.Exists(marker));
    }
}
