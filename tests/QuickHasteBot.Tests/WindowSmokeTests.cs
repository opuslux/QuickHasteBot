using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using QuickHasteBot.Core;
using QuickHasteBot.UI;

namespace QuickHasteBot.Tests;

/// <summary>
/// Lädt die Fenster offscreen mit Fluent-Theme: findet kaputtes XAML, Bindings und fehlende Theme-Ressourcen.
/// Mit QHB_SNAPSHOTS=&lt;Ordner&gt; werden die Fenster zusätzlich als PNG gespeichert.
/// </summary>
public class WindowSmokeTests
{
    // Eine Application pro Prozess: beide Sprachen laufen nacheinander im selben Test
    [Fact]
    public void Popup_and_settings_render_in_german_and_english() => RunOnSta(() =>
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.Light };
        app.Resources.Add("BoolToVisibility", new System.Windows.Controls.BooleanToVisibilityConverter());
        try
        {
            // Wie ein deutsches Windows, damit der Test überall gleich läuft. English() stellt per settings.json um.
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            German();
            English();
            SwitchLanguage();
            ThemeAndDialogs();
            FileHotkeys();
        }
        finally
        {
            app.Shutdown();
        }
    });

    /// <summary>Blättern, Suchen und Kopfzeile, dazu die deutschen Texte.</summary>
    static void German()
    {
        using var dir = new TempDirectory();
        dir.File(@"structure\1-Temp\00-Temp.txt", "temp");
        dir.File(@"structure\4-Grüsse\01-MfG-DE.txt", "Freundliche Grüsse\r\nMax Muster\r\nEine sehr lange Zeile, die in der Vorschau nur mit Umbruch ganz zu sehen ist, ohne muss man scrollen");
        dir.File(@"structure\4-Grüsse\03-MfG-EN.txt", "Kind regards\r\nMax Muster");
        dir.File(@"structure\4-Grüsse\Intern\01-Gruss.txt", "Gruss");
        dir.File(@"structure\6-Weblinks\www.google.com.url", "[InternetShortcut]\r\nURL=https://www.google.com");
        dir.File(@"structure\ascii-table.txt", "!\"#$%&'()*+,-./");

        using var controller = new AppController(new AppPaths(dir.Path));

        var popup = new SearchPopup(controller);
        var viewModel = (SearchPopupViewModel)popup.DataContext;
        viewModel.Reset();
        Assert.Equal(["1-Temp", "4-Grüsse", "6-Weblinks", "ascii-table"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal(["00-Temp"], viewModel.DetailItems.Select(i => i.Snippet.Name));

        // Links nummeriert für Alt+1 bis Alt+9, rechts nicht
        Assert.Equal(["1", "2", "3", "4"], viewModel.Items.Select(i => i.Shortcut));
        Assert.Equal([null], viewModel.DetailItems.Select(i => i.Shortcut));
        Assert.Equal("Alt+2 – wie ein Klick auf diesen Eintrag", viewModel.Items[1].ShortcutTooltip);
        Assert.Null(viewModel.DetailItems[0].ShortcutTooltip);
        Assert.True(viewModel.SelectNumber(3));
        Assert.Equal("6-Weblinks", viewModel.Selected?.Snippet.Name);
        Assert.False(viewModel.SelectNumber(5));
        Assert.Equal("6-Weblinks", viewModel.Selected?.Snippet.Name);

        // Rechts steht der Inhalt des markierten Ordners
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse");
        Assert.Equal(["Intern", "01-MfG-DE", "03-MfG-EN"], viewModel.DetailItems.Select(i => i.Snippet.Name));
        Assert.True(viewModel.ShowDetailItems);
        Snapshot(popup, "popup-browse.png");

        // Ein Ordner von rechts öffnet sich direkt
        viewModel.EnterFolder(viewModel.DetailItems.Single(i => i.Snippet.Name == "Intern").Snippet);
        Assert.Equal(["Bausteine", "4-Grüsse", "Intern"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal(["01-Gruss"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal("Gruss", viewModel.DetailText);
        viewModel.GoTo("");

        // Ein Buchstabe sucht noch nicht, der Ordner bleibt stehen
        viewModel.Query = "m";
        Assert.False(viewModel.IsSearching);
        Assert.Equal(["1-Temp", "4-Grüsse", "6-Weblinks", "ascii-table"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal("Bausteine", viewModel.Breadcrumbs[0].Name);

        viewModel.Query = "mfg";
        Assert.Equal(2, viewModel.Items.Count);
        Assert.False(viewModel.ShowDetailItems);
        Assert.True(viewModel.DetailIsText);
        Assert.StartsWith("Freundliche Grüsse" + Environment.NewLine + "Max Muster" + Environment.NewLine, viewModel.DetailText);
        Snapshot(popup, "popup-search.png");

        // Aus der Suche heraus führt ein Ordner von rechts in den Blätter-Modus
        viewModel.Query = "grüsse";
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse");
        viewModel.EnterFolder(viewModel.DetailItems.Single(i => i.Snippet.Name == "Intern").Snippet);
        Assert.Equal("", viewModel.Query);
        Assert.Equal(["Bausteine", "4-Grüsse", "Intern"], viewModel.Breadcrumbs.Select(c => c.Name));
        viewModel.GoTo("");

        viewModel.Query = "";
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "6-Weblinks").Snippet);
        Assert.Equal(["Bausteine", "6-Weblinks"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal(["", null], viewModel.Breadcrumbs.Select(c => c.Target));
        Assert.Equal("Öffnet https://www.google.com", viewModel.DetailText);
        Assert.False(viewModel.DetailIsText);
        Assert.True(viewModel.GoUp());
        Assert.Equal("6-Weblinks", viewModel.Selected?.Snippet.Name);

        // Über die Kopfzeile zurück, der Ordner auf dem Weg bleibt ausgewählt
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse").Snippet);
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "Intern").Snippet);
        Assert.Equal(["Bausteine", "4-Grüsse", "Intern"], viewModel.Breadcrumbs.Select(c => c.Name));
        Snapshot(popup, "popup-breadcrumbs.png");
        viewModel.GoTo(viewModel.Breadcrumbs[0].Target!);
        Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);

        // Klonen: Vorschlag wie im Explorer, ungültige oder vergebene Namen sperren den Knopf
        var mfg = controller.Library.Find(@"4-Grüsse\01-MfG-DE.txt")!;
        var dialog = NameDialog.ForClone(mfg, SnippetFiles.SuggestCopyName(mfg.FullPath, Strings.CopySuffix));
        Assert.Equal("01-MfG-DE - Kopie.txt", dialog.FileName);
        Assert.True(dialog.ConfirmButton.IsEnabled);
        Snapshot(dialog, "clone.png");
        dialog.NameInput.Text = "03-MfG-EN.txt";
        Assert.Equal(Strings.NameExists, dialog.Problem.Text);
        Assert.False(dialog.ConfirmButton.IsEnabled);
        dialog.NameInput.Text = "MfG: neu.txt";
        Assert.Equal(Strings.NameInvalid, dialog.Problem.Text);
        dialog.NameInput.Text = "  02-MfG-CH.txt ";
        Assert.Equal("", dialog.Problem.Text);
        Assert.Equal("02-MfG-CH.txt", dialog.FileName);
        dialog.Close();

        // Umbenennen: unverändert gesperrt, nur Gross/Klein zu ändern erlaubt
        var rename = NameDialog.ForRename(mfg);
        Assert.Equal("Umbenennen", rename.Title);
        Assert.Equal("01-MfG-DE.txt", rename.FileName);
        Assert.False(rename.ConfirmButton.IsEnabled);
        Assert.Equal("", rename.Problem.Text);
        Snapshot(rename, "rename.png");
        rename.NameInput.Text = "01-mfg-de.txt";
        Assert.True(rename.ConfirmButton.IsEnabled);
        rename.NameInput.Text = "03-MfG-EN.txt";
        Assert.Equal(Strings.NameExists, rename.Problem.Text);
        rename.Close();

        // Danach ist die Kopie markiert, nach dem Löschen der nächste Eintrag bzw. am Ende der vorige
        viewModel.EnterFolder(mfg.Parent!);
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "01-MfG-DE");
        var copy = SnippetFiles.Clone(mfg.FullPath, "02-MfG-CH.txt");
        controller.Library.Reload();
        viewModel.RefreshSelecting(Path.GetRelativePath(controller.Library.RootPath, copy));
        Assert.Equal(["Intern", "01-MfG-DE", "02-MfG-CH", "03-MfG-EN"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal("02-MfG-CH", viewModel.Selected?.Snippet.Name);

        var next = viewModel.SelectionAfterRemoving(viewModel.Selected!.Snippet);
        File.Delete(copy);
        controller.Library.Reload();
        viewModel.Refresh(next);
        Assert.Equal("03-MfG-EN", viewModel.Selected?.Snippet.Name);
        Assert.Equal(@"4-Grüsse\01-MfG-DE.txt", viewModel.SelectionAfterRemoving(viewModel.Selected!.Snippet));
        // Ein umbenannter Ordner bleibt markiert
        viewModel.GoTo("");
        var renamed = SnippetFiles.Rename(controller.Library.Find("1-Temp")!.FullPath, "2-Temp");
        controller.Library.Reload();
        viewModel.RefreshSelecting(Path.GetRelativePath(controller.Library.RootPath, renamed));
        Assert.Equal("2-Temp", viewModel.Selected?.Snippet.Name);
        Assert.Equal(["00-Temp"], viewModel.DetailItems.Select(i => i.Snippet.Name));
        SnippetFiles.Rename(renamed, "1-Temp");
        controller.Library.Reload();

        // Rechts gelöscht: die Auswahl links bleibt
        viewModel.Refresh("4-Grüsse");
        Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);
        Assert.Equal("4-Grüsse", viewModel.SelectionAfterRemoving(mfg));

        // Mit der Stecknadel beginnt das nächste Öffnen im Ordner und beim Eintrag vom Schliessen
        viewModel.RememberFolder = true;
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse").Snippet);
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "03-MfG-EN");
        // Das Popup schliesst sich selbst, sobald es nicht mehr aktiv ist. Je nach Desktop (ein anderes Fenster
        // holt sich den Fokus) ist es hier schon zu, dann würde Hide() nichts auslösen.
        popup.Show();
        popup.Hide();
        Assert.Contains(@"""LastFolder"": ""4-Grüsse""", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        viewModel.Reset();
        Assert.Equal(["Bausteine", "4-Grüsse"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal("03-MfG-EN", viewModel.Selected?.Snippet.Name);

        // Ohne Stecknadel geht es wieder oben los, ein verschwundener Ordner ebenso
        viewModel.RememberFolder = false;
        viewModel.Reset();
        Assert.Equal(["Bausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        controller.Settings.RememberFolder = true;
        controller.Settings.LastFolder = "Gibt-es-nicht";
        viewModel.Reset();
        Assert.Equal(["Bausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.True(viewModel.RememberFolder);
        controller.Settings.RememberFolder = false;

        // Mit dem Filter kommt der Suchtext samt markiertem Treffer wieder, Enter startet denselben Eintrag
        viewModel.RememberQuery = true;
        viewModel.Query = "mfg";
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "03-MfG-EN");
        popup.Show();
        popup.Hide();
        Assert.Contains(@"""LastQuery"": ""mfg""", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        viewModel.Reset();
        Assert.Equal("mfg", viewModel.Query);
        Assert.Equal("03-MfG-EN", viewModel.Selected?.Snippet.Name);
        // Der Hotkey eines Ordners öffnet dort ohne Suchtext
        viewModel.Reset("4-Grüsse");
        Assert.Equal("", viewModel.Query);
        // Ohne Filter beginnt es wieder leer
        viewModel.Query = "mfg";
        viewModel.RememberQuery = false;
        popup.Show();
        popup.Hide();
        viewModel.Reset();
        Assert.Equal("", viewModel.Query);

        // Umbruch der Textvorschau wird sofort gespeichert
        Assert.False(viewModel.WrapPreview);
        viewModel.WrapPreview = true;
        Assert.Contains(@"""WrapPreview"": true", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        viewModel.Query = "mfg";
        Snapshot(popup, "popup-wrap.png");
        viewModel.WrapPreview = false;
        viewModel.Query = "";

        // KI-Prompt: ohne eigenen Text steht ein zufälliges Beispiel da, gespeichert wird es dabei nicht
        Assert.Equal(10, AiPromptWindow.Examples.Count);
        Assert.Contains("Das Datum in 14 Tagen mit Wochentag", AiPromptWindow.Examples);
        var ai = new AiPromptWindow(controller);
        Assert.Contains(ai.RequirementInput.Text, AiPromptWindow.Examples);
        Assert.Equal("", controller.Settings.AiPromptText);
        Assert.True(ai.OpenButton.IsEnabled);
        Assert.Equal(Visibility.Collapsed, ai.Placeholder.Visibility);
        Assert.Equal("In ChatGPT öffnen", ai.OpenButton.Content);
        Assert.Equal("ChatGPT", ai.ServiceInput.SelectedItem?.ToString());
        Assert.Equal(Visibility.Collapsed, ai.ResetTemplateLink.Visibility);
        // Die Glühbirne geht reihum zum nächsten Beispiel, auch das wird nicht gespeichert
        var index = AiPromptWindow.Examples.ToList().IndexOf(ai.RequirementInput.Text);
        ai.NextExample();
        Assert.Equal(AiPromptWindow.Examples[(index + 1) % 10], ai.RequirementInput.Text);
        Assert.Equal("", controller.Settings.AiPromptText);
        // Ein eigener Text bleibt gespeichert, auch wenn danach ein Beispiel im Feld steht
        ai.RequirementInput.Text = "Mein eigener Text";
        ai.NextExample();
        Assert.Contains(ai.RequirementInput.Text, AiPromptWindow.Examples);
        Assert.Equal("Mein eigener Text", controller.Settings.AiPromptText);
        // Leer gelöscht: gesperrt, mit Platzhalter, die Vorschau zeigt den ganzen Prompt mit der Eingabe am Anfang
        ai.RequirementInput.Text = "";
        Assert.False(ai.OpenButton.IsEnabled);
        Assert.Equal(Visibility.Visible, ai.Placeholder.Visibility);
        ai.RequirementInput.Text = "Datum in 14 Tagen mit Wochentag";
        Assert.True(ai.OpenButton.IsEnabled);
        Assert.Equal(Visibility.Collapsed, ai.Placeholder.Visibility);
        Assert.StartsWith("Write JavaScript code for this task: Datum in 14 Tagen mit Wochentag", ai.PromptPreview.Text);

        // Antwort testen, leer: Platzhalter mit Beispiel, der Knopf übernimmt es, jeder weitere Klick würfelt neu
        Assert.Equal(Visibility.Collapsed, ai.TestResultBox.Visibility);
        Assert.Equal(Visibility.Visible, ai.AnswerPlaceholder.Visibility);
        Assert.Equal("Beispiel übernehmen (F5)", ai.RerunButton.ToolTip);
        Snapshot(ai, "ai-prompt-empty.png");
        string[] faces = ["Ja", "Nein", "Vielleicht", "Morgen"];
        ai.Rerun();
        Assert.StartsWith("{{ ['Ja', 'Nein'", ai.AnswerInput.Text);
        Assert.Equal(Visibility.Collapsed, ai.AnswerPlaceholder.Visibility);
        Assert.StartsWith("Erneut ausführen (F5)", (string)ai.RerunButton.ToolTip);
        Assert.Contains(ai.TestResult.Text, faces);
        var rolled = Enumerable.Range(0, 50).Select(_ => { ai.Rerun(); return ai.TestResult.Text; }).ToHashSet();
        Assert.Subset(faces.ToHashSet(), rolled);
        Assert.True(rolled.Count > 1);

        // Läuft wie beim Einfügen, Codeblock-Zäune stören nicht
        ai.AnswerInput.Text = "```js\n{{ (() => { const w = ['So', 'Mo']; return w.at(1) + ' ' + 6 * 7; })() }}\n```";
        ai.TestAnswer();
        Assert.Equal(Visibility.Visible, ai.TestResultBox.Visibility);
        Assert.Equal("Mo 42", ai.TestResult.Text);
        Assert.Equal(Visibility.Visible, ai.CopyRow.Visibility);
        Assert.Equal(Visibility.Hidden, ai.WrappedHint.Visibility);
        Assert.Equal("{{ (() => { const w = ['So', 'Mo']; return w.at(1) + ' ' + 6 * 7; })() }}", ai.Block);

        // Eine echte ChatGPT-Antwort, markiert statt mit dem Kopieren-Knopf: Sprachzeile oben, ohne {{ }}
        ai.AnswerInput.Text = """
            javascript
            const spider = [
              "        / _ \\        ",
              "      \\_\\(_)/_/      "
            ].join("\n");

            spider
            """;
        ai.TestAnswer();
        Assert.Equal("        / _ \\        \n      \\_\\(_)/_/      ".ReplaceLineEndings(), ai.TestResult.Text.ReplaceLineEndings());
        Assert.Equal(Visibility.Visible, ai.WrappedHint.Visibility);
        Assert.StartsWith("{{ const spider = [", ai.Block);
        Assert.EndsWith("spider }}", ai.Block);
        Snapshot(ai, "ai-prompt.png");

        // Editor: ANSI-Baustein öffnen, Vorschau, einfügen, speichern (bleibt ANSI), umbenennen
        var ansi = dir.Bytes(@"structure\Editor\Gruss.txt", [0x47, 0x72, 0xFC, 0x73, 0x73, 0x65]); // "Grüsse" in Windows-1252
        var editor = SnippetEditorWindow.Open(controller, ansi);
        Assert.Equal("Gruss", editor.NameInput.Text);
        Assert.Equal("Grüsse", editor.BodyInput.Text);
        Assert.Equal(".txt", editor.ExtensionText.Text);
        Assert.Equal("in Bausteine › Editor", editor.FolderText.Text);
        Assert.False(editor.IsDirty);
        Assert.Equal("Gruss – Baustein bearbeiten", editor.Title);
        Assert.Equal("Vorschau: wird so eingefügt", editor.PreviewLabel.Text);

        editor.BodyInput.Text = "Max{tab}";
        editor.BodyInput.Select(editor.BodyInput.Text.Length, 0);
        editor.Insert("{focus,#id}", "id");
        Assert.Equal("Max{tab}{focus,#id}", editor.BodyInput.Text);
        Assert.Equal("id", editor.BodyInput.SelectedText);
        editor.BodyInput.SelectedText = "email";
        editor.BodyInput.Text += "{{ 6 * 7 }}";
        editor.UpdatePreview();
        Assert.Equal("Vorschau: ein Formular-Baustein, wird mit diesen Tasten getippt", editor.PreviewLabel.Text);
        Assert.Equal("Max{tab}{focus,#email}«42»".Replace("«", "").Replace("»", ""), editor.PreviewText.Text);
        Assert.True(editor.IsDirty);
        Assert.StartsWith("● ", editor.Title);
        Snapshot(editor, "editor.png");

        // Tag-Menü: ohne Filter die Gruppen, mit Filter eine flache Trefferliste, Enter fügt den ersten Treffer ein
        var menu = editor.TagMenu();
        menu.PlacementTarget = editor.TagsButton;
        menu.IsOpen = true;
        menu.UpdateLayout();
        var filter = (System.Windows.Controls.TextBox)((System.Windows.Controls.Grid)((System.Windows.Controls.MenuItem)menu.Items[0]).Header).Children[0];
        List<string> Visible() => menu.Items.OfType<System.Windows.Controls.MenuItem>().Skip(1)
            .Where(i => i.Visibility == Visibility.Visible).Select(i => i.Header as string ?? "").ToList();
        Assert.Equal(["Tasten", "Ablauf", "Elemente und Fenster", "Skript"], Visible());
        SnapshotElement(menu, "editor-tag-menu.png");
        filter.Text = "feld";
        Assert.Equal(["{tab}", "{shift+tab}", "{clear}", "{focus,#id}", "{set,#id,\"\"}"], Visible());
        SnapshotElement(menu, "editor-tag-filter.png");
        filter.Text = "feld cursor";
        Assert.Equal(["{focus,#id}"], Visible());
        filter.Text = "gibtsnicht";
        Assert.Equal(["Kein passendes Tag"], Visible());
        filter.Text = "guid";
        editor.BodyInput.Text = "Ref ";
        editor.BodyInput.Select(4, 0);
        filter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(filter)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.Equal("Ref {{ guid() }}", editor.BodyInput.Text);
        Assert.False(menu.IsOpen);
        filter.Text = "";
        Assert.Equal(["Tasten", "Ablauf", "Elemente und Fenster", "Skript"], Visible());

        // Fehlerhaftes Tag: Tooltip-Text an der Stelle, kein Tooltip daneben
        editor.BodyInput.Text = "Max{tab,x}";
        var view = editor.BodyInput.TextArea.TextView;
        view.EnsureVisualLines();
        var at = view.GetVisualPosition(new TextViewPosition(1, 7), VisualYPosition.LineMiddle) - view.ScrollOffset;
        Assert.Contains("{tab,3}", editor.Highlighting.ErrorAt(at));
        Assert.Null(editor.Highlighting.ErrorAt(view.GetVisualPosition(new TextViewPosition(1, 2), VisualYPosition.LineMiddle) - view.ScrollOffset));

        // Die Test-Taste zeigt, was läuft: alles, die Auswahl, das Tag oder das Skript an der Schreibmarke
        editor.BodyInput.Text = "Max{tab}{{ 6 * 7 }}";
        editor.BodyInput.Select(0, 0);
        Assert.Equal("Testen", editor.TestButton.Content);
        editor.BodyInput.Select(5, 0);
        Assert.Equal("Tag testen", editor.TestButton.Content);
        editor.BodyInput.Select(12, 0);
        Assert.Equal("Skript testen", editor.TestButton.Content);
        editor.BodyInput.Select(0, 2);
        Assert.Equal("Auswahl testen", editor.TestButton.Content);

        editor.BodyInput.Text = "{{ 1 + }}";
        editor.UpdatePreview();
        Assert.Equal("Vorschau: Skriptfehler, es würde nichts eingefügt", editor.PreviewLabel.Text);
        Assert.StartsWith("Block 1: ", editor.PreviewText.Text);

        editor.BodyInput.Text = "Grüsse aus Zürich";
        Assert.True(editor.Save());
        Assert.Equal(TextEncoding.Windows1252, TextFile.Read(ansi).Encoding);
        Assert.Equal("Grüsse aus Zürich", TextFile.Read(ansi).Text);
        Assert.False(editor.IsDirty);
        Assert.Equal("Gespeichert.", editor.StatusText.Text);
        Assert.NotNull(controller.Library.Find(@"Editor\Gruss.txt"));

        editor.NameInput.Text = "Gruss Zürich";
        Assert.True(editor.Save());
        Assert.False(File.Exists(ansi));
        Assert.Equal("Grüsse aus Zürich", TextFile.Read(Path.Combine(dir.Path, @"structure\Editor\Gruss Zürich.txt")).Text);
        Assert.NotNull(controller.Library.Find(@"Editor\Gruss Zürich.txt"));

        // Anderer Ordner: steht im Menü, macht den Baustein geändert, verschoben wird beim Speichern
        var archive = Directory.CreateDirectory(Path.Combine(dir.Path, @"structure\Editor\Archiv")).FullName;
        dir.File(@"structure\Editor\Archiv\Belegt.txt", "schon da");
        controller.Library.Reload();
        // Aufklappbar: "Editor" hat Unterordner, also ein Untermenü mit "Diesen Ordner wählen" zuoberst
        var folders = editor.FolderMenu().Items.OfType<System.Windows.Controls.MenuItem>().ToList();
        Assert.Equal("Bausteine", folders[0].Header);
        var editorFolder = folders.Single(i => (string)i.Tag == Path.Combine(dir.Path, @"structure\Editor"));
        Assert.Equal(FontWeights.SemiBold, editorFolder.FontWeight);
        var inEditor = editorFolder.Items.OfType<System.Windows.Controls.MenuItem>().ToList();
        Assert.Equal(["Diesen Ordner wählen", "Archiv"], inEditor.Select(i => (string)i.Header));
        Assert.Equal(FontWeights.SemiBold, inEditor[0].FontWeight);
        Assert.Equal(FontWeights.Normal, inEditor[1].FontWeight);
        Assert.Equal(archive, inEditor[1].Tag);
        editor.ChooseFolder(archive);
        Assert.Equal("in Bausteine › Editor › Archiv", editor.FolderText.Text);
        Assert.True(editor.IsDirty);
        editor.NameInput.Text = "Belegt";
        Assert.False(editor.Save());
        Assert.Equal(Strings.NameExists, editor.StatusText.Text);
        editor.NameInput.Text = "Gruss Zürich";
        Assert.True(editor.Save());
        Assert.False(editor.IsDirty);
        Assert.False(File.Exists(Path.Combine(dir.Path, @"structure\Editor\Gruss Zürich.txt")));
        Assert.Equal(Path.Combine(archive, "Gruss Zürich.txt"), editor.FilePath);
        Assert.Equal("Grüsse aus Zürich", TextFile.Read(editor.FilePath!).Text);
        Assert.NotNull(controller.Library.Find(@"Editor\Archiv\Gruss Zürich.txt"));
        editor.ChooseFolder(Path.GetDirectoryName(archive)!);
        Assert.True(editor.Save());
        Assert.Equal(Path.Combine(dir.Path, @"structure\Editor\Gruss Zürich.txt"), editor.FilePath);
        editor.Close();

        // Neuer Baustein: ohne Namen nicht speicherbar, dann als UTF-8 im Ordner
        var created = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, @"structure\Editor"));
        Assert.Equal("Neuer Baustein – Baustein bearbeiten", created.Title);
        created.BodyInput.Text = "Neu 😀";
        Assert.False(created.Save());
        Assert.Equal("Der Baustein braucht einen Namen.", created.StatusText.Text);
        created.NameInput.Text = "Gruss Zürich";
        Assert.False(created.Save());
        Assert.Equal(Strings.NameExists, created.StatusText.Text);
        created.NameInput.Text = "Neu";
        Assert.True(created.Save());
        Assert.Equal(new TextFileContent("Neu 😀", TextEncoding.Utf8), TextFile.Read(Path.Combine(dir.Path, @"structure\Editor\Neu.txt")));
        Assert.Equal(created.FilePath, Path.Combine(dir.Path, @"structure\Editor\Neu.txt"));
        created.Close();

        // Neuer Baustein in einem anderen Ordner als dem, aus dem er angelegt wurde
        var elsewhere = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, @"structure\Editor"));
        elsewhere.NameInput.Text = "Abgelegt";
        elsewhere.BodyInput.Text = "Archiviert";
        elsewhere.ChooseFolder(archive);
        Assert.True(elsewhere.Save());
        Assert.Equal(Path.Combine(archive, "Abgelegt.txt"), elsewhere.FilePath);
        elsewhere.Close();

        // Element-Picker: vor dem ersten Erfassen nur Fadenkreuz und leere Felder
        var picker = new ElementPickerWindow(controller);
        Assert.Equal("QuickHasteBot – Element erfassen", picker.Title);
        Assert.Equal(Visibility.Collapsed, picker.ResultPanel.Visibility);
        Assert.Equal("–", picker.IdText.Text);
        Assert.Equal(ElementAction.Click, ElementPickerWindow.DefaultAction("Button"));
        Assert.Equal(ElementAction.Set, ElementPickerWindow.DefaultAction("ComboBox"));
        Assert.Equal(ElementAction.Focus, ElementPickerWindow.DefaultAction("Edit"));
        Snapshot(picker, "element-picker.png");
        picker.Close();

        // Fehler: rot, nichts zu kopieren
        ai.AnswerInput.Text = "{{ 1 + }}";
        ai.TestAnswer();
        Assert.StartsWith("Block 1: ", ai.TestResult.Text);
        Assert.Equal(Visibility.Collapsed, ai.CopyRow.Visibility);
        ai.AnswerInput.Text = "";
        ai.TestAnswer();
        Assert.Equal(Visibility.Collapsed, ai.TestResultBox.Visibility);
        Assert.Equal(Visibility.Visible, ai.AnswerPlaceholder.Visibility);
        // Der Dienst lässt sich hier wählen, gilt sofort und wird gespeichert
        ai.ServiceInput.SelectedItem = ScriptPrompt.Services.Single(s => s.Name == "Perplexity");
        Assert.Equal("In Perplexity öffnen", ai.OpenButton.Content);
        Assert.Contains(@"""AiPromptUrl"": ""https://www.perplexity.ai/search?q={prompt}""", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        ai.Close();

        // Vorlage: wird beim ersten Mal mit dem eingebauten Prompt angelegt, eine eigene gilt für die Vorschau
        var template = controller.CreateAiPromptTemplate();
        Assert.Equal(ScriptPrompt.DefaultTemplate, File.ReadAllText(template));
        File.WriteAllText(template, "Mach mir JS: {task}");
        Assert.Equal(template, controller.CreateAiPromptTemplate());
        Assert.Equal("Mach mir JS: {task}", File.ReadAllText(template));
        var own = new AiPromptWindow(controller);
        Assert.Equal("Mach mir JS: Datum in 14 Tagen mit Wochentag", own.PromptPreview.Text);
        // Zurücksetzen gibt es nur mit eigener Vorlage. Ohne Datei gilt wieder der eingebaute Prompt.
        Assert.Equal(Visibility.Visible, own.ResetTemplateLink.Visibility);
        File.Delete(template); // im Programm über den Papierkorb, siehe ResetAiPromptTemplate
        own.RefreshTemplate();
        Assert.Equal(Visibility.Collapsed, own.ResetTemplateLink.Visibility);
        Assert.StartsWith("Write JavaScript code for this task: Datum in 14 Tagen mit Wochentag", own.PromptPreview.Text);
        controller.ResetAiPromptTemplate(0); // ohne Datei nichts zu tun
        Assert.False(controller.HasAiPromptTemplate);
        own.Close();

        var settings = new SettingsWindow(controller);
        Assert.Equal(PopupPosition.TopRight, settings.PositionInput.Position);
        // KI-Dienst: zeigt die Wahl aus dem Prompt-Fenster, eine Auswahl hier gilt ebenso sofort
        Assert.Equal("Perplexity", settings.AiServiceInput.SelectedItem?.ToString());
        Assert.Equal(3, settings.AiServiceInput.Items.Count);
        settings.AiServiceInput.SelectedIndex = 0;
        Assert.Equal(ScriptPrompt.DefaultUrl, controller.Settings.AiPromptUrl);
        Snapshot(settings, "settings.png");
        settings.SettingsTabs.SelectedIndex = 1;
        Snapshot(settings, "settings-popup.png");
        // Eine Auswahl im Raster gilt sofort und wird gespeichert
        settings.PositionInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(settings)!, 0, Key.NumPad1) { RoutedEvent = Keyboard.KeyDownEvent });
        Assert.Equal(PopupPosition.BottomLeft, controller.Settings.PopupPosition);
        Assert.Contains(@"""PopupPosition"": ""BottomLeft""", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));

        // Zurück zum ersten Tab: Der Hotkey darf dabei nicht den Fokus bekommen, sonst wartet er auf eine neue Kombination
        settings.Activate();
        var general = (System.Windows.Controls.TabItem)settings.SettingsTabs.Items[0];
        var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        general.RaiseEvent(click);
        if (!click.Handled)
            general.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        settings.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });
        Assert.Equal(0, settings.SettingsTabs.SelectedIndex);
        Assert.False(settings.HotkeyInput.IsKeyboardFocused);

        settings.Close();
        popup.Close();
    }

    /// <summary>Dieselben Fenster mit englischen Texten, eingestellt über settings.json.</summary>
    static void English()
    {
        using var dir = new TempDirectory();
        dir.File(@"structure\5-Links\Intranet.url", "[InternetShortcut]\r\nURL=https://intranet.example.com");
        dir.File(@"structure\Phone numbers.txt", "Reception 100");
        Directory.CreateDirectory(Path.Combine(dir.Path, @"structure\Empty"));
        dir.File("settings.json", """{ "Language": "en", "ListColumnShare": 0.5, "PopupWidth": 100 }""");

        using var controller = new AppController(new AppPaths(dir.Path));

        var popup = new SearchPopup(controller);
        var viewModel = (SearchPopupViewModel)popup.DataContext;
        viewModel.Reset();
        Assert.Equal(["Intranet"], viewModel.DetailItems.Select(i => i.Snippet.Name));
        // Die gespeicherte Aufteilung der Spalten gilt
        Assert.Equal(popup.ListColumn.Width, popup.DetailColumn.Width);

        // Zu schmal gespeichert: so breit wie die Zeile mit den Tastenkürzeln, sonst die gespeicherte Breite
        popup.ApplyWidth();
        Assert.Equal(popup.MinWidth, popup.Width);
        Snapshot(popup, "popup-narrow.png");
        Assert.InRange(popup.MinWidth, 300, 600);
        // Die ganze Zeile hat Platz, auch "Ctrl , Settings" am Ende
        Assert.True(popup.HintBar.ActualWidth >= popup.HintBar.Children.OfType<FrameworkElement>().Sum(c => c.ActualWidth + c.Margin.Left + c.Margin.Right) - 0.5);
        controller.Settings.PopupWidth = 900;
        popup.ApplyWidth();
        Assert.Equal(900, popup.Width);
        Snapshot(popup, "popup-en.png");

        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "Empty");
        Assert.False(viewModel.ShowDetailItems);
        Assert.Equal("This folder is empty. Ctrl+E opens it in Explorer.", viewModel.DetailText);

        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "5-Links").Snippet);
        Assert.Equal(["Snippets", "5-Links"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal("Opens https://intranet.example.com", viewModel.DetailText);

        // Ohne Treffer bleibt rechts nichts vom letzten Eintrag stehen
        viewModel.Query = "xyz";
        Assert.Equal("0 matches", viewModel.Breadcrumbs.Single().Name);
        Assert.Empty(viewModel.DetailItems);
        Assert.Equal("", viewModel.DetailText);

        // Eine von Hand eingetragene Adresse erscheint als eigener Eintrag
        controller.Settings.AiPromptUrl = "https://claude.ai/new?q={prompt}";
        var settings = new SettingsWindow(controller);
        Assert.Equal("QuickHasteBot – Settings", settings.Title);
        Assert.Equal(4, settings.AiServiceInput.Items.Count);
        Assert.Equal("Custom address", settings.AiServiceInput.SelectedItem?.ToString());
        var customAi = new AiPromptWindow(controller);
        Assert.Equal("Open in browser", customAi.OpenButton.Content);
        customAi.Close();
        Snapshot(settings, "settings-en.png");
        settings.Close();
        popup.Close();
    }

    /// <summary>Sprachwechsel ohne Neustart: das Einstellungsfenster kommt an derselben Stelle in der neuen Sprache wieder.</summary>
    static void SwitchLanguage()
    {
        using var dir = new TempDirectory();
        using var controller = new AppController(new AppPaths(dir.Path));
        SettingsWindow Current() => Application.Current.Windows.OfType<SettingsWindow>().Single();

        // Erster Start: structure mit den Beispielen in der Sprache der Oberfläche
        Assert.True(File.Exists(Path.Combine(dir.Path, "structure", "Willkommen.txt")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "structure", "Script-Samples.txt")));
        Assert.True(Directory.Exists(Path.Combine(dir.Path, "structure", "1-Grüsse")));

        controller.ShowSettings(new Point(-20000, -20000));
        Assert.Equal("QuickHasteBot – Einstellungen", Current().Title);

        // Das KI-Fenster gibt es nur einmal, sein Text überlebt Schliessen und Sprachwechsel
        AiPromptWindow Prompt() => Application.Current.Windows.OfType<AiPromptWindow>().Single();
        controller.ShowAiPrompt(new Point(-20000, -20000));
        Prompt().RequirementInput.Text = "Zufälliger Satz";
        controller.ShowAiPrompt(new Point(-20000, -20000));
        Assert.Equal("Skriptblock mit KI", Prompt().Title);

        controller.ChangeLanguage("en");
        Assert.Equal("QuickHasteBot – Settings", Current().Title);
        Assert.Equal("Script block with AI", Prompt().Title);
        Assert.Equal("Zufälliger Satz", Prompt().RequirementInput.Text);
        Assert.Equal(-20000, Prompt().Left);
        Prompt().Close();
        controller.ShowAiPrompt(new Point(-20000, -20000));
        Assert.Equal("Zufälliger Satz", Prompt().RequirementInput.Text);
        Prompt().Close();
        // Gespeichert, steht also auch nach einem Neustart wieder da
        using (var restarted = new AppController(new AppPaths(dir.Path)))
            Assert.Equal("Zufälliger Satz", restarted.Settings.AiPromptText);
        Assert.Equal(-20000, Current().Left);
        Assert.Equal("Snippets", Strings.BreadcrumbRoot);
        Assert.Equal("en", controller.Settings.Language);

        controller.ChangeLanguage("de");
        Assert.Equal("QuickHasteBot – Einstellungen", Current().Title);
        Current().Close();
    }

    /// <summary>Hell/dunkel aus den Einstellungen gilt sofort, die eigenen Meldungen folgen dem Theme.</summary>
    static void ThemeAndDialogs()
    {
        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de", "Theme": "Dark" }""");
        using var controller = new AppController(new AppPaths(dir.Path));
        Assert.Equal(AppTheme.Dark, controller.Settings.Theme);
        try
        {
            var settings = new SettingsWindow(controller);
            Assert.Equal("Dunkel", settings.ThemeInput.SelectedItem?.ToString());
            Assert.Equal(["Automatisch (Windows)", "Hell", "Dunkel"], settings.ThemeInput.Items.Cast<object>().Select(i => i.ToString()));
            settings.ThemeInput.SelectedIndex = 1;
            Assert.Equal(ThemeMode.Light, Application.Current.ThemeMode);
            Assert.Contains(@"""Theme"": ""Light""", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
            settings.ThemeInput.SelectedIndex = 2;
            Assert.Equal(ThemeMode.Dark, Application.Current.ThemeMode);
            Snapshot(settings, "settings-dark.png");
            settings.Close();

            // Löschen fragt mit Ja/Nein, vorgewählt und mit Esc: Nein
            var delete = new MessageDialog(string.Format(Strings.DeleteConfirmFormat, "Gruss.txt"), MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No);
            Snapshot(delete, "dialog-dark.png");
            var buttons = delete.Buttons.Children.OfType<System.Windows.Controls.Button>().ToList();
            Assert.Equal(["Ja", "Nein"], buttons.Select(b => b.Content));
            Assert.True(buttons[1].IsDefault);
            Assert.True(buttons[1].IsCancel);
            Assert.Equal(MessageBoxResult.No, delete.Result);
            Assert.Equal(Visibility.Visible, delete.Symbol.Visibility);
            buttons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(MessageBoxResult.Yes, delete.Result);
            Assert.False(delete.IsVisible);

            // Nur OK, ohne Symbol: Esc ergibt OK
            controller.ChangeTheme(AppTheme.Light);
            var info = new MessageDialog("Hinweis", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
            Snapshot(info, "dialog-light.png");
            Assert.Equal("OK", info.Buttons.Children.OfType<System.Windows.Controls.Button>().Single().Content);
            Assert.Equal(MessageBoxResult.OK, info.Result);
            Assert.Equal(Visibility.Collapsed, info.Symbol.Visibility);
            info.Close();
        }
        finally
        {
            // Die übrigen Tests rechnen mit dem hellen Theme
            UiTheme.Apply(AppTheme.Light);
        }
    }

    /// <summary>Hotkeys im Dateinamen: Badge im Popup, Konflikte im Dialog, beim Umbenennen und in den Einstellungen.</summary>
    static void FileHotkeys()
    {
        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de" }""");
        dir.File(@"structure\3-SQL [Ctrl+Alt+S]\Audit [Ctrl+Alt+A].txt", "insert");
        dir.File(@"structure\Gruss [Ctrl+Alt+G].txt", "Gruss");
        dir.File(@"structure\Gutschein [Ctrl+Alt+G].txt", "Gutschein");
        dir.File(@"structure\Popup [Ctrl+Shift+Y].txt", "Popup");
        dir.File(@"structure\Notiz.txt", "Notiz");
        using var controller = new AppController(new AppPaths(dir.Path));
        controller.SyncFileHotkeys(announce: false);

        var popup = new SearchPopup(controller);
        var viewModel = (SearchPopupViewModel)popup.DataContext;
        viewModel.Reset();
        Assert.Equal(["3-SQL", "Gruss", "Gutschein", "Notiz", "Popup"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal(["Ctrl+Alt+S", "Ctrl+Alt+G", "Ctrl+Alt+G", null, "Ctrl+Shift+Y"], viewModel.Items.Select(i => i.HotkeyText));
        Assert.Equal([false, false, true, false, true], viewModel.Items.Select(i => i.HasHotkeyProblem));
        Assert.Equal("Schon vergeben an «Gruss».", viewModel.Items[2].HotkeyTooltip);
        Assert.Equal("Hotkey Ctrl+Alt+S, gilt in jedem Programm", viewModel.Items[0].HotkeyTooltip);
        Snapshot(popup, "popup-hotkeys.png");

        // Hotkey eines Ordners: das Popup beginnt dort, die Kopfzeile ohne Tag
        viewModel.Reset(controller.Library.All.Single(s => s.Name == "3-SQL").RelativePath);
        Assert.Equal(["Bausteine", "3-SQL"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal(["Audit"], viewModel.Items.Select(i => i.Snippet.Name));
        popup.Close();

        // Dialog: vergebene Kombinationen sperren, eine freie zeigt den neuen Dateinamen
        var notiz = controller.Library.All.Single(s => s.Name == "Notiz");
        var dialog = new HotkeyDialog(controller, notiz);
        Assert.False(dialog.RemoveButton.IsEnabled);
        Assert.False(dialog.ConfirmButton.IsEnabled);
        dialog.HotkeyInput.Gesture = new HotkeyGesture(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.A);
        dialog.HotkeyInput.RaiseCaptured();
        Assert.Equal("Schon vergeben an «Audit» (3-SQL).", dialog.Problem.Text);
        Assert.False(dialog.ConfirmButton.IsEnabled);
        dialog.HotkeyInput.Gesture = HotkeyGesture.Default;
        dialog.HotkeyInput.RaiseCaptured();
        Assert.Equal(Strings.FileHotkeyIsMain, dialog.Problem.Text);
        dialog.HotkeyInput.Gesture = new HotkeyGesture(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.N);
        dialog.HotkeyInput.RaiseCaptured();
        Assert.Equal("", dialog.Problem.Text);
        Assert.True(dialog.ConfirmButton.IsEnabled);
        Assert.Equal("Dateiname: Notiz [Ctrl+Alt+N].txt", dialog.FileNameText.Text);
        Snapshot(dialog, "hotkey-dialog.png");
        dialog.Close();

        // Umbenennen mit einem vergebenen Hotkey im Namen ist gesperrt, mit dem eigenen nicht
        var gruss = controller.Library.All.Single(s => s.Name == "Gruss");
        var rename = NameDialog.ForRename(gruss, name => controller.HotkeyConflictForName(name, false, gruss.FullPath, gruss.Hotkey));
        rename.NameInput.Text = "Grüsse [Ctrl+Alt+G].txt";
        Assert.Equal("", rename.Problem.Text);
        Assert.True(rename.ConfirmButton.IsEnabled);
        rename.NameInput.Text = "Gruss [Ctrl+Alt+S].txt";
        Assert.Equal("Schon vergeben an «3-SQL».", rename.Problem.Text);
        Assert.False(rename.ConfirmButton.IsEnabled);
        rename.Close();

        // Neuer Ordner: der Hinweis nennt den offenen Ordner, vergebene Namen und Hotkeys sperren
        var sql = controller.Library.All.Single(s => s.Name == "3-SQL");
        var newFolder = NameDialog.ForNewFolder(sql.FullPath, "Bausteine › 3-SQL",
            name => controller.HotkeyConflictForName(name, true, null, null));
        Assert.Equal("Neuer Ordner", newFolder.FileName);
        Assert.Equal("Neue Ordner entstehen immer im Ordner, der im Popup gerade offen ist: Bausteine › 3-SQL", newFolder.Hint.Text);
        Assert.Equal(Visibility.Visible, newFolder.Hint.Visibility);
        Assert.True(newFolder.ConfirmButton.IsEnabled);
        Snapshot(newFolder, "new-folder.png");
        newFolder.NameInput.Text = "Audit [Ctrl+Alt+A].txt";
        Assert.Equal(Strings.NameExists, newFolder.Problem.Text);
        newFolder.NameInput.Text = "Archiv [Ctrl+Alt+G]";
        Assert.Equal("Schon vergeben an «Gruss».", newFolder.Problem.Text);
        Assert.False(newFolder.ConfirmButton.IsEnabled);
        newFolder.Close();

        // Einstellungen: alle Hotkeys, die zwei Konflikte rot
        var settings = new SettingsWindow(controller);
        settings.ShowFileHotkeys();
        var rows = settings.FileHotkeysList.Items.Cast<SettingsWindow.FileHotkeyRow>().ToList();
        Assert.Equal(["Ctrl+Alt+S", "Ctrl+Alt+A", "Ctrl+Alt+G", "Ctrl+Alt+G", "Ctrl+Shift+Y"], rows.Select(r => r.Hotkey));
        Assert.Equal(2, rows.Count(r => r.IsProblem));
        Assert.Equal("3 aktiv, 2 wirken nicht", settings.FileHotkeysSummary.Text);
        Snapshot(settings, "settings-hotkeys.png");
        settings.Close();
    }

    static void Snapshot(Window window, string fileName)
    {
        // Offscreen anzeigen, damit Layout und Ressourcen wirklich aufgelöst werden
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.Show();
        window.UpdateLayout();

        var folder = Environment.GetEnvironmentVariable("QHB_SNAPSHOTS");
        if (string.IsNullOrEmpty(folder)) return;

        var content = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);

        // Hintergrund des Fensters mitzeichnen, der Inhalt selbst ist transparent
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // Fluent lässt den Hintergrund für Mica durchsichtig: dann den des Themes, sonst wäre auch das dunkle Theme weiss
            var background = window.Background is SolidColorBrush { Color.A: > 0 } own ? own
                : window.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
            context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var stream = File.Create(Path.Combine(folder, fileName));
        encoder.Save(stream);
    }

    /// <summary>Ein offenes Menü oder Popup-Inhalt, ohne eigenes Fenster.</summary>
    static void SnapshotElement(FrameworkElement element, string fileName)
    {
        var folder = Environment.GetEnvironmentVariable("QHB_SNAPSHOTS");
        if (string.IsNullOrEmpty(folder)) return;

        element.UpdateLayout();
        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var background = element.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
            context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        }
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var stream = File.Create(Path.Combine(folder, fileName));
        encoder.Save(stream);
    }

    static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
