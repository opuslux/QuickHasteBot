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

    /// <summary>Die Sammlung der Skriptblöcke, eine Zeile pro Block, oben die Beschreibung.</summary>
    static string JavaScriptBlocks(string language) =>
        language == "de" ? @"6 - Skripte\JavaScript-Blöcke {{ }}.txt" : @"6 - Scripts\JavaScript blocks {{ }}.txt";

    static string Sample(string language, string label) =>
        SampleStructure.Files(language).Single(f => f.Path == JavaScriptBlocks(language)).Text
            .Split("\r\n").Single(l => l.StartsWith(label + ":", StringComparison.Ordinal))[(label.Length + 1)..].TrimStart('\t');

    [Theory]
    [InlineData("de", "Willkommen.txt", @"1 - Einfacher Text\Freundliche Grüsse.txt", @"1 - Einfacher Text\Abwesenheit.txt",
        @"1 - Einfacher Text\Audit-Insert.txt", @"2 - Text und Markierung\Antwort mit Rückfragen.txt", @"6 - Skripte\Computer-Infos.txt",
        @"7 - Bedingungen\Zusage oder Absage.txt", @"8 - Meldungen\Ja oder Nein.txt")]
    [InlineData("en", "Welcome.txt", @"1 - Simple text\Kind regards.txt", @"1 - Simple text\Out of office.txt",
        @"1 - Simple text\Audit insert.txt", @"2 - Text and selection\Reply with questions.txt", @"6 - Scripts\Computer info.txt",
        @"7 - Conditions\Accept or decline.txt", @"8 - Messages\Yes or no.txt")]
    public void Both_languages_have_the_same_small_structure(string language, params string[] some)
    {
        var paths = SampleStructure.Files(language).Select(f => f.Path).ToList();
        Assert.Equal(41, paths.Count);
        Assert.Contains(JavaScriptBlocks(language), paths);
        Assert.Subset(paths.ToHashSet(), some.ToHashSet());
        Assert.All(SampleStructure.Files(language), f => Assert.DoesNotContain("\n", f.Text.Replace("\r\n", "")));
    }

    /// <summary>
    /// Die Beispiele liegen flach in nummerierten Themenordnern (im structure-Ordner unter Demo), die Bausteine darin
    /// ohne Nummer (alphabetisch). Lose liegt nur der Willkommenstext. In beiden Sprachen gleich viele Ordner, und in
    /// jedem gleich viele Dateien.
    /// </summary>
    [Fact]
    public void Examples_are_in_numbered_topic_folders_with_unnumbered_files()
    {
        static List<(string Folder, int Files)> Topics(string language) =>
        [
            .. SampleStructure.Files(language).Select(f => f.Path.Split('\\')).Where(p => p.Length > 1)
                .Select(p =>
                {
                    Assert.Equal(2, p.Length); // Thema\Datei
                    Assert.Matches(@"^\d - ", p[0]);
                    Assert.DoesNotMatch(@"^\d", p[1]);
                    return p[0];
                })
                .GroupBy(folder => folder).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (g.Key, g.Count())),
        ];
        Assert.Equal(["Willkommen.txt"], SampleStructure.Files("de").Select(f => f.Path).Where(p => !p.Contains('\\')));
        Assert.Equal(["Welcome.txt"], SampleStructure.Files("en").Select(f => f.Path).Where(p => !p.Contains('\\')));
        var de = Topics("de");
        var en = Topics("en");
        Assert.Equal(8, de.Count);
        Assert.Equal(de.Select(t => (t.Folder[..1], t.Files)), en.Select(t => (t.Folder[..1], t.Files)));
    }

    [Fact]
    public void Unknown_language_falls_back_to_english() =>
        Assert.Equal(SampleStructure.Files("en").Select(f => f.Path), SampleStructure.Files("fr").Select(f => f.Path));

    [Theory]
    [InlineData("de", "de-CH")]
    [InlineData("en", "en-US")]
    public void Every_sample_runs_without_errors(string language, string culture)
    {
        Assert.Equal(16, SampleStructure.Files(language).Single(f => f.Path == JavaScriptBlocks(language)).Text.Split("\r\n").Count(l => l.Contains(":\t")));
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
        // Ab Ordner 2 hat jeder Baustein oben eine Beschreibung, die einfachen Texte in 1 nicht
        var examples = files.Where(f => f.Path[0] is >= '2' and <= '8' && f.Path.EndsWith(".txt", StringComparison.Ordinal)).ToList();
        Assert.Equal(31, examples.Count);
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
                Assert.Null(KeyTags.CheckBlocks(steps)); // {if}-Blöcke passen zusammen
                var shown = string.Concat(steps.Select(s => s is TextStep t ? t.Text : KeyTags.Describe(s)));
                // Ein Baustein, der nur eine Meldung zeigt, tippt nichts
                if (shown.Length == 0 && SnippetScript.UsesMessages(text)) continue;
                Assert.False(string.IsNullOrWhiteSpace(shown), path);
                Assert.DoesNotContain("{#", shown);
                Assert.False(char.IsWhiteSpace(shown[0]), path);
            }
        }

        // "Einfacher Text/Freundliche Grüsse" für "1 - Einfacher Text\Freundliche Grüsse.txt": Nummer und Endung dürfen fehlen
        static bool SamePath(string path, string name)
        {
            var have = path.Split('\\');
            var want = name.Split('/', '\\');
            return have.Length == want.Length && have.Zip(want).All(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p.First).TrimStart("0123456789 -".ToCharArray()), p.Second, StringComparison.OrdinalIgnoreCase));
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

        Assert.Equal("«Hallo Welt{cursor}»", Paste("Markierung in Anführungszeichen.txt", " Hallo Welt\r\n"));
        Assert.Equal("• Äpfel\n• Birnen", Paste("Markierung als Punkte.txt", "Äpfel\r\n\r\n  Birnen "));
        Assert.Equal("• ", Paste("Markierung als Punkte.txt", ""));
        Assert.Equal("1. Äpfel\n2. Birnen\n3. 1.5 kg Mehl", Paste("Markierung als Aufzählung.txt", "Äpfel\r\n\r\n  Birnen \r\n1.5 kg Mehl"));
        Assert.Equal("1. Äpfel\n2. Birnen\n3. Mehl", Paste("Markierung als Aufzählung.txt", "• Äpfel\n- Birnen\n7) Mehl"));
        Assert.Equal("1. ", Paste("Markierung als Aufzählung.txt", ""));
        Assert.Equal("Max{tab}Muster{tab}Zürich", Paste("Excel-Zeile ins Formular.txt", "", "Max\tMuster\tZürich\r\nZweite\tZeile\r\n"));
        Assert.Equal("Betrag netto:   CHF 1250.50\r\nMwSt. 8.1 %:     CHF 101.29\r\nTotal:          CHF 1351.79\r\nAuf Wunsch zahlbar in zwei Raten.",
            Paste("Offerte mit MwSt.txt", "", answers: new() { ["Betrag netto"] = "1'250.50", ["MwSt. in %"] = "8,1" }));
        Assert.DoesNotContain("Raten", Paste("Offerte mit MwSt.txt", ""));
        // Jeder Tag auf einer eigenen Zeile, ohne dass Umbrüche getippt werden; die Zeile mit var fällt weg
        Assert.Equal("{start,notepad}{delay,30}{ctrl+n}{wait,300}Checkliste für Dienstag, 3. November{enter}"
            + "1. Mails beantworten\n2. Ablage aufräumen\n3. Wochenplan prüfen{enter}{wait,500}Erledigt: 0 von 3",
            Paste("Schritt für Schritt.txt", ""));
        // Die Blöcke stehen auf eigenen Zeilen, getippt werden nur die Leerzeilen um den Brief
        Assert.Equal("Hallo Max,\r\n\r\n{if,js=\"zusage == 'ja' || zusage == 'j'\"}Danke für die Einladung, ich bin gerne dabei."
            + "{if,js=\"termin != ''\"} Passt dir Montag?{/if}{else}Danke für die Einladung, leider kann ich nicht dabei sein.{/if}\r\n\r\nFreundliche Grüsse",
            Paste("Zusage oder Absage.txt", "", answers: new() { ["Zusage? (ja/nein)"] = "ja", ["Terminvorschlag (leer: keiner)"] = "Montag" }));
        // Ohne Fenster sagt confirm() Ja, msg() zeigt nichts
        Assert.Equal("DRINGEND: Ihre Bestellung{enter}{enter}{if,js=\"confirm('Ist die Bestellung schon unterwegs?', { title: 'Antwort' })\"}"
            + "Ihre Bestellung ist unterwegs und kommt in den nächsten Tagen an.{else}Ihre Bestellung ist in Arbeit. Wir melden uns, sobald sie unterwegs ist.{/if}",
            Paste("Ja oder Nein.txt", ""));
        Assert.Equal("Diese Zeile kam vor der Meldung.{enter}Und diese nach dem OK.{enter}", Paste("Meldung am Ende.txt", ""));
        Assert.Equal("", Paste("Computer-Infos als Meldung.txt", ""));
        Assert.Equal("", Paste("Würfel.txt", ""));
        Assert.Equal("", Paste("Entscheider.txt", ""));
        Assert.Equal("", Paste("Tee-Timer.txt", ""));
    }

    /// <summary>
    /// Der Tee-Timer: Minuten:Sekunden oder nur Minuten, Unsinn gibt 5:00. Erst ein kurzer Hinweis, dann die Meldung mit
    /// after, auf die der Baustein nicht wartet.
    /// </summary>
    [Theory]
    [InlineData("de", "Tee-Timer.txt", "Zeit (Minuten:Sekunden)", "2:30", 150, "2:30")]
    [InlineData("de", "Tee-Timer.txt", "Zeit (Minuten:Sekunden)", "3", 180, "3:00")]
    [InlineData("de", "Tee-Timer.txt", "Zeit (Minuten:Sekunden)", "1,5", 90, "1:30")]
    [InlineData("de", "Tee-Timer.txt", "Zeit (Minuten:Sekunden)", "0:45", 45, "0:45")]
    [InlineData("de", "Tee-Timer.txt", "Zeit (Minuten:Sekunden)", "Tee", 300, "5:00")]
    [InlineData("en", "Tea timer.txt", "Time (minutes:seconds)", "4:05", 245, "4:05")]
    [InlineData("en", "Tea timer.txt", "Time (minutes:seconds)", "", 300, "5:00")]
    public void Tea_timer_shows_a_message_after_the_time(string language, string name, string label, string answer, int seconds, string shown)
    {
        var text = SampleStructure.Files(language).Single(f => f.Path.EndsWith(name, StringComparison.Ordinal)).Text;
        var messages = new List<MessageRequest>();
        var context = new ScriptContext(Now, CultureInfo.InvariantCulture, "", Zone, new FormBuilder(),
            Answers: new Dictionary<string, string> { [label] = answer }, Message: request => { messages.Add(request); return true; });
        Assert.Equal("", SnippetScript.Render(KeyTags.MarkTags(text, context.Form!), context).Trim());

        Assert.Equal(2, messages.Count);
        Assert.Equal((3, 0), (messages[0].CloseSeconds, messages[0].AfterSeconds));
        Assert.Contains(shown, messages[0].Text);
        Assert.Equal((0, seconds), (messages[1].CloseSeconds, messages[1].AfterSeconds));
        Assert.Contains(shown, messages[1].Text);
    }

    /// <summary>Würfel und Orakel zeigen nur eine Meldung: die Würfe passen zu Seiten und Anzahl, das Orakel nennt die Frage.</summary>
    [Theory]
    [InlineData("de", "Würfel.txt", "Wie viele Seiten?", "Wie viele Würfel?", "W", "Gewürfelt: ")]
    [InlineData("en", "Dice.txt", "How many sides?", "How many dice?", "d", "You rolled: ")]
    public void Dice_rolls_in_range_and_adds_up(string language, string name, string sidesLabel, string countLabel, string letter, string single)
    {
        var text = SampleStructure.Files(language).Single(f => f.Path.EndsWith(name, StringComparison.Ordinal)).Text;
        List<MessageRequest> Roll(string sides, string count)
        {
            var shown = new List<MessageRequest>();
            var context = new ScriptContext(Now, CultureInfo.InvariantCulture, "", Zone, new FormBuilder(),
                Answers: new Dictionary<string, string> { [sidesLabel] = sides, [countLabel] = count },
                Message: request => { shown.Add(request); return true; });
            SnippetScript.Render(KeyTags.MarkTags(text, context.Form!), context);
            return shown;
        }

        for (var i = 0; i < 50; i++)
        {
            var three = Assert.Single(Roll("20", "3"));
            Assert.Equal("3" + letter + "20", three.Title);
            var match = Regex.Match(three.Text, @"^(\d+) \+ (\d+) \+ (\d+) = (\d+)$");
            Assert.True(match.Success, three.Text);
            var rolls = Enumerable.Range(1, 3).Select(g => int.Parse(match.Groups[g].Value)).ToList();
            Assert.All(rolls, r => Assert.InRange(r, 1, 20));
            Assert.Equal(rolls.Sum(), int.Parse(match.Groups[4].Value));

            var one = Assert.Single(Roll("", "x")); // Unsinn gibt einen normalen Würfel
            Assert.Equal("1" + letter + "6", one.Title);
            Assert.InRange(int.Parse(one.Text[single.Length..]), 1, 6);
        }
    }

    [Theory]
    [InlineData("de", "Entscheider.txt", "Deine Frage an das Orakel", "Orakel")]
    [InlineData("en", "Decider.txt", "Your question for the oracle", "Oracle")]
    public void Decider_answers_the_question_in_a_message(string language, string name, string label, string title)
    {
        var text = SampleStructure.Files(language).Single(f => f.Path.EndsWith(name, StringComparison.Ordinal)).Text;
        var shown = new List<MessageRequest>();
        var context = new ScriptContext(Now, CultureInfo.InvariantCulture, "", Zone, new FormBuilder(),
            Answers: new Dictionary<string, string> { [label] = "Pizza?" }, Message: request => { shown.Add(request); return true; });
        Assert.Equal("", SnippetScript.Render(KeyTags.MarkTags(text, context.Form!), context).Trim());
        var message = Assert.Single(shown);
        Assert.Equal(title, message.Title);
        Assert.Equal(MessageIcon.None, message.Icon);
        Assert.StartsWith("Pizza?\n\n", message.Text);
        Assert.True(message.Text.Length > "Pizza?\n\n".Length);
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
    [InlineData("de", @"6 - Skripte\Nummer per Batch-Datei", "Kein Kunde angegeben")]
    [InlineData("en", @"6 - Scripts\Number via batch file", "No customer given")]

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
        var file = Path.Combine(dir.Path, "1 - Einfacher Text", "Freundliche Grüsse.txt");
        Assert.Equal("Freundliche Grüsse\r\n\r\nVorname Nachname", File.ReadAllText(file));
        Assert.NotEqual(0xEF, File.ReadAllBytes(file)[0]);
    }

    /// <summary>Export in einen anderen Ordner (Einstellungen): geänderte Beispiele zurück, eigene Bausteine unberührt.</summary>
    [Fact]
    public void Extract_again_restores_the_samples_and_keeps_other_files()
    {
        using var dir = new TempDirectory();
        SampleStructure.Extract(dir.Path, "de");
        var sample = Path.Combine(dir.Path, "1 - Einfacher Text", "Freundliche Grüsse.txt");
        var own = Path.Combine(dir.Path, "1 - Einfacher Text", "Eigener Gruss.txt");
        File.WriteAllText(sample, "geändert");
        File.WriteAllText(own, "Mein Text");

        Assert.Equal(SampleStructure.Files("de").Count, SampleStructure.Extract(dir.Path, "de"));
        Assert.Equal("Freundliche Grüsse\r\n\r\nVorname Nachname", File.ReadAllText(sample));
        Assert.Equal("Mein Text", File.ReadAllText(own));
    }
}
