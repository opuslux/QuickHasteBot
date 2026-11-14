using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>Baut das Menü „Tag einfügen ▾“ des Editors aus dem <see cref="TagCatalog"/>.</summary>
internal static class TagMenuBuilder
{
    /// <summary>Ein Text in der Sprache der Oberfläche.</summary>
    static string Local(string key) => Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;

    /// <summary>
    /// Was der Filter im Tag-Menü durchsucht: der Tag und die Texte in der Sprache der Oberfläche und auf Englisch, damit
    /// z.B. «comment» auch im deutschen Menü den Kommentar findet.
    /// </summary>
    static string SearchText(string? tag, params string?[] keys) =>
        string.Join(' ', new[] { tag }.Concat(keys.OfType<string>().SelectMany(key => new[]
        {
            Local(key), Strings.ResourceManager.GetString(key, System.Globalization.CultureInfo.InvariantCulture),
        })).OfType<string>().Distinct());

    /// <summary>
    /// "Tag einfügen ▾": oben eine Filterzeile, darunter die Gruppen. Sobald gefiltert wird, stehen statt der Gruppen alle
    /// passenden Tags in einer Liste (gesucht in Tag, Beschreibung und Gruppe, wie die Suche im Popup, ohne Treffer in den
    /// Hilfetexten). Enter fügt den ersten Treffer ein, ↓ geht in die Liste. Jeder Tag erklärt sich im Tooltip.
    /// </summary>
    internal static ContextMenu Create(SnippetEditorWindow editor, Action showPicker)
    {
        // Gruppen und Beschreibungen als Namen der Texte, siehe SearchText
        var groups = TagCatalog.Groups().Select(g => (g.Header, Icon: g.Header switch
        {
            nameof(Strings.EditorTagsKeys) => MenuIcons.Glyph(MenuIcons.Keys),
            nameof(Strings.EditorTagsFlow) => MenuIcons.Glyph(MenuIcons.Flow),
            nameof(Strings.EditorTagsElements) => MenuIcons.Crosshair(),
            _ => MenuIcons.Glyph(MenuIcons.Script),
        }, g.Entries)).ToArray();

        var menu = new ContextMenu();
        var filter = new TextBox { MinWidth = 220, VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(filter, Strings.EditorTagsFilter);
        var placeholder = new TextBlock
        {
            Text = Strings.EditorTagsFilter, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0),
            Foreground = (Brush)editor.FindResource("TextFillColorTertiaryBrush"),
        };
        // Klicks ins Feld sollen das Menü nicht schliessen, und die Zeile selbst ist kein Ziel für die Pfeiltasten
        menu.Items.Add(new MenuItem
        {
            Header = new Grid { Children = { filter, placeholder } }, Icon = MenuIcons.Glyph(MenuIcons.Search),
            StaysOpenOnClick = true, Focusable = false,
        });
        menu.Items.Add(new Separator());

        var groupItems = new List<MenuItem>();
        foreach (var (header, icon, entries) in groups)
        {
            var group = new MenuItem { Header = Local(header), Icon = icon };
            foreach (var entry in entries)
                group.Items.Add(entry is null ? new Separator() : Tag(entry));
            if (header == nameof(Strings.EditorTagsElements))
            {
                group.Items.Add(new Separator());
                group.Items.Add(MenuIcons.Item(Strings.EditorPickerMenu, MenuIcons.Crosshair(), showPicker));
            }
            groupItems.Add(group);
            menu.Items.Add(group);
        }

        // Die flache Trefferliste, versteckt bis gefiltert wird. Die Hilfetexte zählen nur, wenn Tag, Beschreibung und
        // Gruppe nichts finden: Sie erwähnen viele Wörter («Feld»), und die Liste soll kurz bleiben.
        var results = new List<(MenuItem Item, string Text, string Help)>();
        foreach (var (header, _, entries) in groups)
            foreach (var entry in entries.OfType<TagEntry>())
                results.Add((Tag(entry), SearchText(entry.Tag, entry.Description, header), SearchText(null, entry.Help)));
        results.Add((MenuIcons.Item(Strings.EditorPickerMenu, MenuIcons.Crosshair(), showPicker),
            SearchText(null, nameof(Strings.EditorPickerMenu), nameof(Strings.EditorTagsElements)), ""));
        var noMatch = new MenuItem { Header = Strings.EditorTagsNoMatch, IsEnabled = false };
        foreach (var (item, _, _) in results)
        {
            item.Visibility = Visibility.Collapsed;
            menu.Items.Add(item);
        }
        noMatch.Visibility = Visibility.Collapsed;
        menu.Items.Add(noMatch);

        filter.TextChanged += (_, _) =>
        {
            var query = filter.Text.Trim();
            var filtering = query.Length > 0;
            placeholder.Visibility = filter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var group in groupItems)
                group.Visibility = filtering ? Visibility.Collapsed : Visibility.Visible;
            var any = filtering && results.Any(r => SnippetSearch.MatchesAll(r.Text, query));
            var inHelp = filtering && !any;
            foreach (var (item, text, help) in results)
            {
                var match = filtering && SnippetSearch.MatchesAll(inHelp ? help : text, query);
                item.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                any |= match;
            }
            noMatch.Visibility = filtering && !any ? Visibility.Visible : Visibility.Collapsed;
        };
        filter.PreviewKeyDown += (_, e) =>
        {
            var first = menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Focusable && i.IsEnabled && i.Visibility == Visibility.Visible);
            switch (e.Key)
            {
                case Key.Down when first is not null:
                    first.Focus();
                    e.Handled = true;
                    break;
                case Key.Enter when filter.Text.Trim().Length > 0:
                    // Wie ein Klick auf den ersten Treffer
                    first?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, first));
                    menu.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        };
        // Das Menü holt sich beim Öffnen selbst den Fokus, danach gleich ins Filterfeld
        menu.Opened += (_, _) => editor.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(filter));
        return menu;

        MenuItem Tag(TagEntry entry)
        {
            // Der Hilfetext im Tooltip, umbrochen und in der normalen Schrift statt der des Editors
            var item = new MenuItem
            {
                Header = entry.Tag, InputGestureText = Local(entry.Description), FontFamily = editor.BodyInput.FontFamily,
                ToolTip = new ToolTip
                {
                    Content = new TextBlock { Text = Local(entry.Help), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
                    FontFamily = editor.FontFamily,
                },
            };
            ToolTipService.SetInitialShowDelay(item, 300);
            ToolTipService.SetShowDuration(item, 30000);
            item.Click += (_, _) =>
            {
                if (entry.FromClipboard) editor.PasteFormatted();
                else if (entry.OwnLine) editor.InsertTag(entry.Text ?? entry.Tag, entry.Select);
                else editor.Insert(entry.Text ?? entry.Tag, entry.Select);
            };
            return item;
        }
    }
}
