using System.Globalization;
using System.Text.RegularExpressions;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;

namespace QuickHasteBot.Tests;

/// <summary>Die Beispiele für den ersten Start: in beiden Sprachen gleich aufgebaut, jeder Skriptblock läuft.</summary>
public class SampleStructureTests
{
    // Dienstag, 3. November 2026, 14:05:09, wie in den SnippetScriptTests
    static readonly DateTimeOffset Now = new(2026, 11, 3, 14, 5, 9, TimeSpan.FromHours(1));
    static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");

    static string Render(string text, string culture, string clipboard = "") =>
        SnippetScript.Render(text, new ScriptContext(Now, CultureInfo.GetCultureInfo(culture), clipboard, Zone));

    static string Sample(string language, string label) =>
        SampleStructure.Files(language).Single(f => f.Path == "Script-Samples.txt").Text
            .Split("\r\n").Single(l => l.StartsWith(label + ":", StringComparison.Ordinal))[(label.Length + 1)..].TrimStart('\t');

    [Theory]
    [InlineData("de", "Willkommen.txt", @"1-Grüsse\Freundliche Grüsse.txt", @"2-E-Mail\Abwesenheit.txt", @"3-SQL\Audit-Insert.txt",
        @"4-Beispiele\01-Antwort mit Rückfragen.txt", @"4-Beispiele\11-Computer-Infos.txt")]
    [InlineData("en", "Welcome.txt", @"1-Greetings\Kind regards.txt", @"2-Email\Out of office.txt", @"3-SQL\Audit insert.txt",
        @"4-Examples\01-Reply with questions.txt", @"4-Examples\11-Computer info.txt")]
    public void Both_languages_have_the_same_small_structure(string language, params string[] some)
    {
        var paths = SampleStructure.Files(language).Select(f => f.Path).ToList();
        Assert.Equal(29, paths.Count);
        Assert.Contains("Script-Samples.txt", paths);
        Assert.Subset(paths.ToHashSet(), some.ToHashSet());
        Assert.All(SampleStructure.Files(language), f => Assert.DoesNotContain("\n", f.Text.Replace("\r\n", "")));
    }

    [Fact]
    public void Unknown_language_falls_back_to_english() =>
        Assert.Equal(SampleStructure.Files("en").Select(f => f.Path), SampleStructure.Files("fr").Select(f => f.Path));

    [Theory]
    [InlineData("de", "de-CH")]
    [InlineData("en", "en-US")]
    public void Every_sample_runs_without_errors(string language, string culture)
    {
        Assert.Equal(16, SampleStructure.Files(language).Single(f => f.Path == "Script-Samples.txt").Text.Trim().Split("\r\n").Length);
        foreach (var (path, text) in SampleStructure.Files(language))
        foreach (var clipboard in new[] { "", "Hallo Welt", "{\"a\": 1}", "Zeile 1\r\nZeile 2" })
        {
            // Die Excel-Zeile bricht mit leerer Zwischenablage absichtlich ab
            var context = new ScriptContext(Now, CultureInfo.GetCultureInfo(culture), clipboard, Zone, new FormBuilder());
            if (clipboard.Length == 0 && text.Contains("throw") && text.Contains("clipboard"))
            {
                Assert.Throws<SnippetScriptException>(() => SnippetScript.Render(text, context));
                continue;
            }
            var result = SnippetScript.Render(text, context);
            Assert.DoesNotContain("{{", result);
            Assert.DoesNotContain("[native code]", result);
            Assert.False(string.IsNullOrWhiteSpace(result), path);
        }
    }

    [Theory]
    [InlineData("de", "de-CH", "Datum lang", "Dienstag, 3. November 2026")]
    [InlineData("de", "de-CH", "Datum Uhrzeit", "03.11.2026 14:05")]
    [InlineData("de", "de-CH", "Git Commit Template", "feat: [KW45] Implementiert von {0} am 2026.11.03 14:05")]
    [InlineData("de", "de-CH", "Fälligkeitsdatum (+14 Tage)", "17.11.2026")]
    [InlineData("de", "de-CH", "Feierabend Countdown", "Noch 3h 54m bis Feierabend (18:00).")]
    [InlineData("de", "de-CH", "Cäsar-Verschlüsselung", "zhofkhu wdj lvw khxwh")]
    [InlineData("de", "de-CH", "Zeitstempel (Dateiname)", "2026-11-03_1405")]
    [InlineData("en", "en-US", "Long date", "Tuesday, November 3, 2026")]
    [InlineData("en", "en-US", "Greeting (time of day)", "Good afternoon")]
    [InlineData("en", "en-US", "Quitting time countdown", "3h 54m left until quitting time (6 pm).")]
    [InlineData("en", "en-US", "Caesar cipher", "zkdw gdb lv lw wrgdb")]
    public void Samples_give_the_expected_text(string language, string culture, string label, string expected) =>
        Assert.Equal(string.Format(expected, Environment.UserName), Render(Sample(language, label), culture));

    /// <summary>
    /// Die Beispiele mit Tags: im Editor nichts Rotes ({include} findet sein Ziel unter den Beispielen), die Beschreibung
    /// oben fällt samt Zeile weg, und Tags, Skripte und Blöcke ergeben Schritte, mit und ohne Markierung.
    /// </summary>
    [Theory]
    [InlineData("de", "de-CH")]
    [InlineData("en", "en-US")]
    public void Examples_have_no_errors_and_their_description_is_not_pasted(string language, string culture)
    {
        var files = SampleStructure.Files(language);
        var examples = files.Where(f => f.Path.StartsWith("4-", StringComparison.Ordinal) && f.Path.EndsWith(".txt", StringComparison.Ordinal)).ToList();
        Assert.Equal(18, examples.Count);
        foreach (var (path, text) in examples)
        {
            Assert.StartsWith("{#", text);
            Assert.Empty(SnippetSyntax.Tokenize(text, includeExists: name => files.Any(f => SamePath(f.Path, name)))
                .Where(s => s.Kind == SyntaxKind.Error).Select(s => $"{path}: {s.Error}"));
            foreach (var selection in new[] { "", "Erste Zeile\r\n\r\n  Zweite Zeile " })
            {
                var form = new FormBuilder();
                var marked = KeyTags.MarkTags(text, form);
                var rendered = SnippetScript.Render(marked, new ScriptContext(Now, CultureInfo.GetCultureInfo(culture), "Max\tMuster", Zone, form, Selection: selection));
                var steps = KeyTags.Split(rendered, form);
                var shown = string.Concat(steps.Select(s => s is TextStep t ? t.Text : KeyTags.Describe(s)));
                Assert.False(string.IsNullOrWhiteSpace(shown), path);
                Assert.DoesNotContain("{#", shown);
                Assert.False(char.IsWhiteSpace(shown[0]), path);
            }
        }

        // "Grüsse/Freundliche Grüsse" für "1-Grüsse\Freundliche Grüsse.txt": Nummer und Endung dürfen fehlen
        static bool SamePath(string path, string name)
        {
            var have = path.Split('\\');
            var want = name.Split('/', '\\');
            return have.Length == want.Length && have.Zip(want).All(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p.First).TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-'), p.Second, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Examples_give_the_expected_text()
    {
        string Example(string name) => SampleStructure.Files("de").Single(f => f.Path.EndsWith(name, StringComparison.Ordinal)).Text;
        string Paste(string name, string selection, string clipboard = "", Dictionary<string, string>? answers = null)
        {
            var form = new FormBuilder();
            var rendered = SnippetScript.Render(KeyTags.MarkTags(Example(name), form),
                new ScriptContext(Now, CultureInfo.GetCultureInfo("de-CH"), clipboard, Zone, form, Selection: selection, Answers: answers));
            return string.Concat(KeyTags.Split(rendered, form).Select(s => s is TextStep t ? t.Text : KeyTags.Describe(s)));
        }

        Assert.Equal("«Hallo Welt{cursor}»", Paste("02-Markierung in Anführungszeichen.txt", " Hallo Welt\r\n"));
        Assert.Equal("• Äpfel\n• Birnen", Paste("03-Markierung als Punkte.txt", "Äpfel\r\n\r\n  Birnen "));
        Assert.Equal("• ", Paste("03-Markierung als Punkte.txt", ""));
        Assert.Equal("1. Äpfel\n2. Birnen\n3. 1.5 kg Mehl", Paste("04-Markierung als Aufzählung.txt", "Äpfel\r\n\r\n  Birnen \r\n1.5 kg Mehl"));
        Assert.Equal("1. Äpfel\n2. Birnen\n3. Mehl", Paste("04-Markierung als Aufzählung.txt", "• Äpfel\n- Birnen\n7) Mehl"));
        Assert.Equal("1. ", Paste("04-Markierung als Aufzählung.txt", ""));
        Assert.Equal("Max{tab}Muster{tab}Zürich", Paste("12-Excel-Zeile ins Formular.txt", "", "Max\tMuster\tZürich\r\nZweite\tZeile\r\n"));
        Assert.Equal("Betrag netto:   CHF 1250.50\r\nMwSt. 8.1 %:     CHF 101.29\r\nTotal:          CHF 1351.79\r\nAuf Wunsch zahlbar in zwei Raten.",
            Paste("13-Offerte mit MwSt.txt", "", answers: new() { ["Betrag netto"] = "1'250.50", ["MwSt. in %"] = "8,1" }));
        Assert.DoesNotContain("Raten", Paste("13-Offerte mit MwSt.txt", ""));
    }

    [Theory]
    [InlineData("", "Die Zwischenablage ist leer")]
    [InlineData("Hallo Welt, das ist ein Test.", "29 Zeichen, 6 Wörter, 1 Zeile – Text")]
    [InlineData("https://www.example.com/pfad?x=1", "32 Zeichen, 1 Wort, 1 Zeile – Link")]
    [InlineData("maxmuster@example.com", "21 Zeichen, 1 Wort, 1 Zeile – E-Mail-Adresse")]
    [InlineData("1'234.50", "8 Zeichen, 1 Wort, 1 Zeile – Zahl")]
    [InlineData("04.11.2026", "10 Zeichen, 1 Wort, 1 Zeile – Datum")]
    [InlineData("3f2504e0-4f89-41d3-9a0c-0305e82c3301", "36 Zeichen, 1 Wort, 1 Zeile – GUID")]
    [InlineData(@"C:\Users\Public", "15 Zeichen, 1 Wort, 1 Zeile – Dateipfad")]
    [InlineData("{\"name\": \"Max\"}", "15 Zeichen, 2 Wörter, 1 Zeile – JSON")]
    [InlineData("eins\r\nzwei", "10 Zeichen, 2 Wörter, 2 Zeilen – mehrzeiliger Text")]
    public void Clipboard_info_counts_and_recognizes_the_type(string clipboard, string expected) =>
        Assert.Equal(expected, Render(Sample("de", "Zwischenablage-Info"), "de-CH", clipboard));

    [Fact]
    public void Clipboard_on_one_line_joins_lines_and_hyphenated_words() =>
        Assert.Equal("Text aus einem PDF mit Umbrüchen",
            Render(Sample("de", "Zwischenablage einzeilig"), "de-CH", "Text aus ei-\r\nnem PDF\r\nmit   Umbrüchen\r\n"));

    /// <summary>Die Batch-Datei neben dem Beispiel: run() findet sie im Ordner des Bausteins, mit Kunde eine Nummer, ohne einen Fehler.</summary>
    [Theory]
    [InlineData("de", @"4-Beispiele\20-Nummer per Batch-Datei", "Kein Kunde angegeben")]
    [InlineData("en", @"4-Examples\20-Number via batch file", "No customer given")]
    public void Batch_example_runs_next_to_its_snippet(string language, string example, string error)
    {
        using var dir = new TempDirectory();
        SampleStructure.Extract(dir.Path, language);
        var snippet = Path.Combine(dir.Path, example + ".txt");
        var text = File.ReadAllText(snippet);
        var file = Regex.Match(text, @"run\('([^']+)'").Groups[1].Value;
        Assert.Equal(Path.GetFileName(example) + ".bat", file);

        RunResult Run(string arguments) =>
            ScriptRunner.Run(new RunRequest(file, arguments, 10, Hidden: true, SnippetFolder: Path.GetDirectoryName(snippet)), () => false);
        var ok = Run("\"Muster AG\"");
        Assert.Equal(0, ok.Code);
        Assert.Matches(@"^Muster AG-\d+$", ok.Out.Trim());
        var failed = Run("\"\"");
        Assert.Equal(2, failed.Code);
        Assert.Equal(error, failed.Err.Trim());
    }

    [Fact]
    public void Extract_writes_the_files_as_utf8_without_bom()
    {
        using var dir = new TempDirectory();
        SampleStructure.Extract(dir.Path, "de");
        var file = Path.Combine(dir.Path, "1-Grüsse", "Freundliche Grüsse.txt");
        Assert.Equal("Freundliche Grüsse\r\n\r\nVorname Nachname", File.ReadAllText(file));
        Assert.NotEqual(0xEF, File.ReadAllBytes(file)[0]);
    }
}
