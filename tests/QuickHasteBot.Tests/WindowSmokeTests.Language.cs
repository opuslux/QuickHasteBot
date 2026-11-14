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
using QuickHasteBot.Interop;
using QuickHasteBot.UI;

namespace QuickHasteBot.Tests;

/// <summary>Alle Fenster auf Deutsch und Englisch, Sprache und Theme umschalten, Dialoge.</summary>
public partial class WindowSmokeTests
{
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
        // Bei einem Text unten rechts in der Vorschau der Stift zum Bearbeiten, bei Ordnern und Weblinks nicht
        Assert.True(popup.EditPreviewButton.IsVisible);
        Assert.Equal("Diesen Baustein im Editor bearbeiten (Strg+O)", popup.EditPreviewButton.ToolTip);
        // Grau wie die übrigen Symbole: Akzentfarbe hiesse „eingeschaltet“ wie beim Umbruch
        Assert.Same(popup.FindResource("TextFillColorTertiaryBrush"), popup.EditPreviewButton.Foreground);
        // Shift+Mausrad scrollt die ungebrochene Vorschau waagrecht, ohne Shift bleibt es beim normalen Rad
        Assert.True(ShiftWheel.GetHorizontal(popup.DetailScroll));
        Assert.False(ShiftWheel.Scroll(popup.DetailScroll, -120, ModifierKeys.None));
        Assert.True(ShiftWheel.Scroll(popup.DetailScroll, -120, ModifierKeys.Shift));
        popup.UpdateLayout();
        Assert.True(popup.DetailScroll.HorizontalOffset > 0);
        popup.DetailScroll.ScrollToHorizontalOffset(0);

        // Aus der Suche heraus führt ein Ordner von rechts in den Blätter-Modus
        viewModel.Query = "grüsse";
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse");
        viewModel.EnterFolder(viewModel.DetailItems.Single(i => i.Snippet.Name == "Intern").Snippet);
        Assert.Equal("", viewModel.Query);
        Assert.Equal(["Bausteine", "4-Grüsse", "Intern"], viewModel.Breadcrumbs.Select(c => c.Name));
        viewModel.GoTo("");

        // Beim Suchen führt der Pfeil nach rechts in den Ordnerinhalt rechts, aber nur am Ende des Suchtexts
        void Press(Key key) => popup.SearchBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(popup.SearchBox)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        popup.SearchBox.Text = "grüsse";
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse");
        popup.SearchBox.Select(2, 0);
        Press(Key.Right);
        Assert.False(viewModel.InDetail);
        // Ganz markiert wie nach dem Öffnen mit gemerktem Suchtext: gleich nach rechts, der Text bleibt zum Weitertippen
        popup.SearchBox.SelectAll();
        Press(Key.Right);
        Assert.Equal("Intern", viewModel.DetailSelected?.Snippet.Name);
        Assert.Equal((6, 0), (popup.SearchBox.SelectionStart, popup.SearchBox.SelectionLength));
        Press(Key.Down);
        Press(Key.Down);
        Press(Key.Down);
        Assert.Equal("03-MfG-EN", viewModel.DetailSelected?.Snippet.Name);
        Assert.Same(viewModel.DetailSelected, popup.DetailList.SelectedItem);
        Assert.Equal("03-MfG-EN", viewModel.CurrentSnippet?.Name);
        Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);
        Press(Key.Up);
        Assert.Equal("01-MfG-DE", viewModel.DetailSelected?.Snippet.Name);
        // Pfeil nach links: zurück in die linke Liste, dort ist noch derselbe Ordner markiert
        Press(Key.Left);
        Assert.False(viewModel.InDetail);
        Assert.Null(popup.DetailList.SelectedItem);
        Assert.Equal("4-Grüsse", viewModel.CurrentSnippet?.Name);
        // Alt+Ziffer bzw. ein Klick links, auch auf denselben Eintrag, holt die Tastatur zurück nach links
        Press(Key.Right);
        Assert.True(viewModel.InDetail);
        viewModel.Selected = viewModel.Selected;
        Assert.False(viewModel.InDetail);
        // Ein Ordner rechts lässt die Spalten weiterrücken, aus der Suche heraus wird geblättert
        Press(Key.Right);
        Press(Key.Right);
        Assert.Equal("", viewModel.Query);
        Assert.Equal(["Bausteine", "4-Grüsse"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal("Intern", viewModel.Selected?.Snippet.Name);
        Assert.Equal("01-Gruss", viewModel.DetailSelected?.Snippet.Name);
        // Zurück: in die linke Liste, dann eine Ebene hoch, der Ordner von vorher bleibt markiert
        Press(Key.Left);
        Press(Key.Left);
        Assert.Equal(["Bausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);
        // Ohne Suchtext genauso: Der Pfeil nach rechts öffnet links keinen Ordner, er geht nur in die rechte Liste
        Press(Key.Right);
        Assert.Equal(["Bausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);
        Assert.Equal("Intern", viewModel.DetailSelected?.Snippet.Name);
        Press(Key.Down);
        Assert.Equal("01-MfG-DE", viewModel.DetailSelected?.Snippet.Name);
        Press(Key.Right);
        Assert.Equal("01-MfG-DE", viewModel.DetailSelected?.Snippet.Name);
        Press(Key.Left);
        Assert.False(viewModel.InDetail);
        Assert.Equal(["Bausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        // Weitertippen holt die Tastatur zurück nach links
        popup.SearchBox.Text = "grüsse";
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse");
        popup.SearchBox.Select(popup.SearchBox.Text.Length, 0);
        Press(Key.Right);
        Assert.True(viewModel.InDetail);
        popup.SearchBox.Text = "grüss";
        Assert.False(viewModel.InDetail);
        // Bei einem Text rechts gibt es nichts auszuwählen, der Pfeil bleibt beim Suchfeld
        popup.SearchBox.Text = "mfg";
        popup.SearchBox.Select(3, 0);
        Press(Key.Right);
        Assert.False(viewModel.InDetail);

        viewModel.Query = "";
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "6-Weblinks").Snippet);
        Assert.Equal(["Bausteine", "6-Weblinks"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal(["", null], viewModel.Breadcrumbs.Select(c => c.Target));
        Assert.Equal("Öffnet https://www.google.com", viewModel.DetailText);
        Assert.False(viewModel.DetailIsText);
        popup.UpdateLayout();
        Assert.False(popup.EditPreviewButton.IsVisible);
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
        // Vorschau nur für Skripte: reiner Text steht im Editor schon so da
        Assert.Equal(Visibility.Collapsed, editor.PreviewBox.Visibility);

        editor.BodyInput.Text = "Max{tab}";
        editor.BodyInput.Select(editor.BodyInput.Text.Length, 0);
        editor.Insert("{focus,#id}", "id");
        Assert.Equal("Max{tab}{focus,#id}", editor.BodyInput.Text);
        Assert.Equal("id", editor.BodyInput.SelectedText);
        editor.BodyInput.SelectedText = "email";
        editor.BodyInput.Text += "{{ 6 * 7 }}";
        editor.BodyInput.Select(editor.BodyInput.Text.Length - 4, 0);
        editor.UpdatePreview();
        Assert.Equal(Visibility.Visible, editor.PreviewBox.Visibility);
        Assert.Equal("Dieser Block ergibt", editor.PreviewLabel.Text);
        Assert.Equal("42", editor.PreviewText.Text);
        Assert.Equal(Visibility.Collapsed, editor.PreviewNote.Visibility);
        // Die Auswahl: Tasten als lesbare Tags, das Skript ausgewertet
        editor.BodyInput.Select(0, editor.BodyInput.Text.Length);
        editor.UpdatePreview();
        Assert.Equal("Die Auswahl ergibt", editor.PreviewLabel.Text);
        Assert.Equal("Max{tab}{focus,#email}42", editor.PreviewText.Text);
        // Schreibmarke im Text, nicht im Skript: keine Vorschau
        editor.BodyInput.Select(1, 0);
        editor.UpdatePreview();
        Assert.Equal(Visibility.Collapsed, editor.PreviewBox.Visibility);
        editor.BodyInput.Select(editor.BodyInput.Text.Length - 4, 0);
        editor.UpdatePreview();
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

        // Kommentar: auf eine eigene Zeile, die Notiz markiert zum Überschreiben
        // Jeder Tag hat eine Beschreibung (in der Sprache der Oberfläche, nicht der Name des Texts)
        var tags = menu.Items.OfType<System.Windows.Controls.MenuItem>()
            .Where(i => i.Header is string header && header.StartsWith('{')).ToList();
        Assert.All(tags, t => Assert.False(string.IsNullOrEmpty(t.InputGestureText) || t.InputGestureText.StartsWith("Tag"), (string)t.Header));
        Assert.Contains(tags, t => t.InputGestureText == "Pfeil hoch");

        // Der Filter findet auch die englischen Texte
        filter.Text = "comment";
        Assert.Equal(["{# Notiz}"], Visible());
        filter.Text = "arrow";
        Assert.Equal(["{up}", "{down}", "{left}", "{right}"], Visible());
        filter.Text = "keys reload";
        Assert.Equal(["{f5}"], Visible());

        // Jeder Tag erklärt sich im Tooltip. Findet der Filter sonst nichts, sucht er in diesen Hilfetexten.
        Assert.All(tags, t => Assert.False(string.IsNullOrEmpty(((System.Windows.Controls.TextBlock)((System.Windows.Controls.ToolTip)t.ToolTip).Content).Text), (string)t.Header));
        filter.Text = "arbeitsordner";
        Assert.Equal(["{{ const r = run('d:/scripts/file.bat', { args: '', dir: 'd:/data' }) }}"], Visible());
        filter.Text = "standardbrowser";
        Assert.Equal(["{start,https://example.com}"], Visible());

        filter.Text = "kommentar";
        Assert.Equal(["{# Notiz}"], Visible());
        editor.BodyInput.Text = "Hallo";
        editor.BodyInput.Select(5, 0);
        filter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(filter)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.Equal("Hallo\r\n{# Notiz}\r\n", editor.BodyInput.Text);
        Assert.Equal("Notiz", editor.BodyInput.SelectedText);
        editor.UpdatePreview();
        Assert.Equal(Visibility.Collapsed, editor.PreviewBox.Visibility);

        // run(): auf eine eigene Zeile, der Pfad markiert. Die Vorschau startet nichts, run() liefert dort ein leeres Ergebnis.
        filter.Text = "ausführen";
        Assert.Equal(["{{ const r = run('d:/scripts/file.bat') }}"], Visible());
        editor.BodyInput.Text = "{tab}";
        editor.BodyInput.Select(5, 0);
        filter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(filter)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.Equal("{tab}\r\n{{ const r = run('d:/scripts/file.bat') }}\r\n", editor.BodyInput.Text);
        Assert.Equal("d:/scripts/file.bat", editor.BodyInput.SelectedText);
        editor.BodyInput.Text = "{tab}{{ const r = run('x.bat') }}{{ r.out }}";
        editor.BodyInput.Select(editor.BodyInput.Text.Length - 4, 0);
        editor.UpdatePreview();
        Assert.Equal("(nichts)", editor.PreviewText.Text);
        Assert.Equal(Visibility.Visible, editor.PreviewNote.Visibility);
        Assert.StartsWith("run() startet hier nichts", editor.PreviewNote.Text);
        // Variablen aus den Blöcken davor gelten
        editor.BodyInput.Text = "{{ var n = 3; '' }}Wert: {{ n * 2 }}";
        editor.BodyInput.Select(editor.BodyInput.Text.Length - 4, 0);
        editor.UpdatePreview();
        Assert.Equal("6", editor.PreviewText.Text);
        Assert.Equal(Visibility.Collapsed, editor.PreviewNote.Visibility);
        filter.Text = "";
        Assert.Equal(["Tasten", "Ablauf", "Elemente und Fenster", "Skript"], Visible());

        // Fehlerhaftes Tag: Tooltip-Text an der Stelle, kein Tooltip daneben
        editor.BodyInput.Text = "Max{tab,x}";
        var view = editor.BodyInput.TextArea.TextView;
        view.EnsureVisualLines();
        var at = view.GetVisualPosition(new TextViewPosition(1, 7), VisualYPosition.LineMiddle) - view.ScrollOffset;
        Assert.Contains("{tab,3}", editor.Highlighting.ErrorAt(at));
        Assert.Null(editor.Highlighting.ErrorAt(view.GetVisualPosition(new TextViewPosition(1, 2), VisualYPosition.LineMiddle) - view.ScrollOffset));

        // Hover-Hilfe: erst der Fehler, dann Kurzbeschreibung und Hilfetext auf Deutsch; eine andere Taste nennt ihren Namen
        var help = SnippetHighlighting.HelpContent("bad", TagHelp.At("{tab,x}", 2));
        Assert.Equal(["bad", "Nächstes Feld"], help.Children.OfType<System.Windows.Controls.TextBlock>().Take(2).Select(t => t.Text));
        Assert.StartsWith("Drückt Tab", help.Children.OfType<System.Windows.Controls.TextBlock>().Last().Text);
        var other = TagHelp.At("{ctrl+s}", 2)!;
        Assert.Equal("Taste ctrl+s", SnippetHighlighting.HelpTitle(other));
        Assert.StartsWith("Drückt ctrl+s. Tasten verbinden sich mit +, z.B. {ctrl+s}", SnippetHighlighting.HelpText(other));
        Assert.EndsWith("dreimal Pfeil runter. Geht mit jedem Tasten-Tag, z.B. {tab,2}.", SnippetHighlighting.HelpText(TagHelp.At("{down,3}", 2)!));

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
        editor.BodyInput.Select(3, 0);
        editor.UpdatePreview();
        Assert.Equal("Skriptfehler", editor.PreviewLabel.Text);
        Assert.StartsWith("Block 1: ", editor.PreviewText.Text);
        // Die Nummer zählt im ganzen Text, auch wenn ein Block davor fehlschlägt
        editor.BodyInput.Text = "{{ x( }}{{ 1 + }}";
        editor.BodyInput.Select(12, 0);
        editor.UpdatePreview();
        Assert.StartsWith("Block 2: ", editor.PreviewText.Text);

        editor.BodyInput.Text = "Grüsse aus Zürich";
        Assert.True(editor.Save());
        Assert.Equal(TextEncoding.Windows1252, TextFile.Read(ansi).Encoding);
        Assert.Equal("Grüsse aus Zürich", TextFile.Read(ansi).Text);
        Assert.False(editor.IsDirty);
        Assert.Equal("Gespeichert.", editor.StatusText.Text);
        Snapshot(editor, "editor-saved.png");
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

        // Der Tag steht über die ganze Breite über den Knöpfen, ein langer umbricht statt abgeschnitten zu werden
        picker.ResultPanel.Visibility = Visibility.Visible;
        picker.TagOutput.Text = "{window,process=chrome}{click,name=Zeit;type=button;window=process=chrome}";
        picker.UpdateLayout();
        Snapshot(picker, "element-picker-result.png");
        Assert.Equal(picker.ResultPanel.ActualWidth, picker.TagOutput.ActualWidth, 1);
        Assert.True(picker.CopyButton.TranslatePoint(new Point(), picker.TagOutput).Y >= picker.TagOutput.ActualHeight);
        Assert.True(picker.TagOutput.ActualHeight > 2 * picker.TagOutput.FontSize * 1.2, "langer Tag umbrochen");

        // Fünf Aktionen in einer Reihe, auch die langen deutschen Wörter
        System.Windows.Controls.RadioButton[] actions =
            [picker.ActionFocus, picker.ActionClick, picker.ActionDoubleClick, picker.ActionRightClick, picker.ActionSet];
        Assert.Equal(["Fokus", "Klick", "Doppelklick", "Rechtsklick", "Setzen"], actions.Select(a => (string)a.Content));
        Assert.All(actions, a => Assert.Equal(0, a.TranslatePoint(new Point(), picker.ActionFocus).Y, 1));
        Assert.True(picker.ActionSet.TranslatePoint(new Point(picker.ActionSet.ActualWidth, 0), picker.ResultPanel).X <= picker.ResultPanel.ActualWidth);
        picker.ActionDoubleClick.IsChecked = true;
        Assert.Equal(ElementAction.DoubleClick, picker.SelectedAction);
        picker.ActionRightClick.IsChecked = true;
        Assert.Equal(ElementAction.RightClick, picker.SelectedAction);

        // Ohne Editor ist Kopieren der blaue Knopf; mit Editor links blau „In Editor einfügen“, rechts Kopieren und Testen
        picker.UpdateInsertButton();
        Assert.Equal(Visibility.Collapsed, picker.InsertButton.Visibility);
        Assert.Same(picker.FindResource("AccentButtonStyle"), picker.CopyButton.Style);
        picker.InsertButton.Visibility = Visibility.Visible;
        picker.UpdateLayout();
        double X(FrameworkElement button) => button.TranslatePoint(new Point(), picker.ResultPanel).X;
        Assert.True(X(picker.InsertButton) < X(picker.CopyButton) && X(picker.CopyButton) < X(picker.TestButton));
        Assert.Same(picker.FindResource("AccentButtonStyle"), picker.InsertButton.Style);
        Snapshot(picker, "element-picker-actions.png");
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
        var template = PromptTemplates.Create(controller.AiTemplate);
        Assert.Equal(ScriptPrompt.DefaultTemplate, File.ReadAllText(template));
        File.WriteAllText(template, "Mach mir JS: {task}");
        Assert.Equal(template, PromptTemplates.Create(controller.AiTemplate));
        Assert.Equal("Mach mir JS: {task}", File.ReadAllText(template));
        var own = new AiPromptWindow(controller);
        Assert.Equal("Mach mir JS: Datum in 14 Tagen mit Wochentag", own.PromptPreview.Text);
        // Zurücksetzen gibt es nur mit eigener Vorlage. Ohne Datei gilt wieder der eingebaute Prompt.
        Assert.Equal(Visibility.Visible, own.ResetTemplateLink.Visibility);
        File.Delete(template); // im Programm über den Papierkorb, siehe PromptTemplates.Reset
        own.RefreshTemplate();
        Assert.Equal(Visibility.Collapsed, own.ResetTemplateLink.Visibility);
        Assert.StartsWith("Write JavaScript code for this task: Datum in 14 Tagen mit Wochentag", own.PromptPreview.Text);
        PromptTemplates.Reset(controller.AiTemplate, 0); // ohne Datei nichts zu tun
        Assert.False(controller.AiTemplate.Exists);
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

        // Zu schmal gespeichert: so breit wie die Zeile mit den Tastenkürzeln, sonst die gespeicherte Breite
        popup.ApplyWidth();
        Assert.Equal(popup.MinWidth, popup.Width);
        // Die Aufteilung von früher (Anteil) wird einmal in eine feste Breite umgerechnet und sieht gleich aus
        Assert.Null(controller.Settings.ListColumnShare);
        Assert.DoesNotContain("ListColumnShare", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        Snapshot(popup, "popup-narrow.png");
        Assert.Equal(popup.ListColumn.ActualWidth, popup.DetailColumn.ActualWidth, tolerance: 1.0);
        var listWidth = popup.ListColumn.ActualWidth;
        Assert.Equal(listWidth, controller.Settings.ListColumnWidth, 1);
        Assert.InRange(popup.MinWidth, 300, 600);
        // Die ganze Zeile hat Platz, auch "Ctrl , Settings" am Ende
        Assert.True(popup.HintBar.ActualWidth >= popup.HintBar.Children.OfType<FrameworkElement>().Sum(c => c.ActualWidth + c.Margin.Left + c.Margin.Right) - 0.5);
        controller.Settings.PopupWidth = 900;
        popup.ApplyWidth();
        Assert.Equal(900, popup.Width);
        Snapshot(popup, "popup-en.png");
        // Breiter: die Liste links bleibt, der Platz geht nach rechts
        Assert.Equal(listWidth, popup.ListColumn.ActualWidth, 1);
        Assert.True(popup.DetailColumn.ActualWidth > listWidth + 300);

        // Zu schmal für die gespeicherte Liste: nur in der Anzeige schmaler, rechts bleibt die Mindestbreite
        controller.Settings.ListColumnWidth = 860;
        popup.ApplyListWidth();
        popup.UpdateLayout();
        Assert.Equal(popup.DetailColumn.MinWidth, popup.DetailColumn.ActualWidth, 1);
        Assert.Equal(860, controller.Settings.ListColumnWidth);
        controller.Settings.ListColumnWidth = listWidth;
        popup.ApplyListWidth();

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

        // Erster Start: structure mit den Beispielen in der Sprache der Oberfläche, alle im Ordner Demo
        var structure = Path.Combine(dir.Path, "structure");
        var demo = Path.Combine(structure, "Demo");
        Assert.Equal(demo, controller.DemoRoot);
        Assert.Equal([demo], Directory.GetFileSystemEntries(structure));
        Assert.True(File.Exists(Path.Combine(demo, "Willkommen.txt")));
        Assert.True(File.Exists(Path.Combine(demo, "6 - Skripte", "JavaScript-Blöcke {{ }}.txt")));
        // {include} im Beispiel findet seinen Baustein auch unter Demo
        Assert.Equal(Path.Combine(demo, "1 - Einfacher Text", "Freundliche Grüsse.txt"),
            controller.Library.FindIncluded("Einfacher Text/Freundliche Grüsse")?.FullPath);

        // Neu aufbauen (Einstellungen): ohne Demo gibt es nichts für den Papierkorb, der Ordner entsteht und wird gleich eingelesen
        Directory.Delete(demo, recursive: true);
        File.WriteAllText(Path.Combine(structure, "Eigener.txt"), "eigen");
        Assert.Equal(SampleStructure.Files("de").Count, controller.ExportSamples(demo, rebuild: true, 0));
        Assert.NotNull(controller.Library.FindIncluded("Willkommen"));
        Assert.True(File.Exists(Path.Combine(structure, "Eigener.txt")));

        // Anderer Ordner: die Beispiele direkt hinein, eigene Dateien bleiben
        var other = Path.Combine(dir.Path, "Anderswo");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "Eigener.txt"), "eigen");
        controller.ExportSamples(other, rebuild: false, 0);
        Assert.True(File.Exists(Path.Combine(other, "Willkommen.txt")));
        Assert.True(File.Exists(Path.Combine(other, "Eigener.txt")));

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
}
