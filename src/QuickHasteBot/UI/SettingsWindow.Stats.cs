using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Der Tab "Statistik": wie oft jeder Eintrag benutzt wurde und nach welchen Suchtexten. Wird erst beim Anwählen gebaut
/// und beim Zurückkehren ins Fenster aufgefrischt, kostet also nichts, solange niemand hinschaut.
/// </summary>
internal partial class SettingsWindow
{
    /// <summary>Eine Zeile der Statistik. Zähler, die null sind, bleiben leer, damit die benutzten auffallen.</summary>
    internal sealed record UsageRow(Snippet Snippet, UsageStats? Stats, string LastText, string ToolTip)
    {
        public string Name => Snippet.Name;

        public string Location => Snippet.Location;

        public int Total => Stats?.Total ?? 0;

        public string TotalText => Total.ToString();

        public string SearchText => Count(Stats?.Search);

        public string BrowseText => Count(Stats?.Browse);

        public string HotkeyText => Count(Stats?.Hotkey);

        public string LearnedText => Stats is null ? "" : UsageText.Learned(Stats.Queries);

        static string Count(int? count) => count is > 0 ? count.Value.ToString() : "";
    }

    // Spalte → Sortierung; Zahlen und Datum standardmässig absteigend, Texte aufsteigend
    static readonly Dictionary<string, (Func<UsageRow, IComparable?> Key, bool Descending)> StatsColumns = new()
    {
        ["Name"] = (r => r.Name, false),
        ["Location"] = (r => r.Location, false),
        ["Total"] = (r => r.Total, true),
        ["Search"] = (r => r.Stats?.Search ?? 0, true),
        ["Browse"] = (r => r.Stats?.Browse ?? 0, true),
        ["Hotkey"] = (r => r.Stats?.Hotkey ?? 0, true),
        ["Last"] = (r => r.Stats?.Last, true),
        ["Learned"] = (r => r.Stats?.Queries.Count ?? 0, true),
    };

    List<UsageRow> _statsRows = [];
    string _statsSort = "Total";
    bool _statsDescending = true;
    bool _statsLoaded;

    void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged steigt auch aus den Auswahlfeldern in den Tabs auf
        if (e.OriginalSource == SettingsTabs && StatsTab.IsSelected)
            UpdateStats();
    }

    /// <summary>Daten neu holen und anzeigen. Nur wenn der Tab offen ist oder schon einmal gezeigt wurde.</summary>
    internal void UpdateStats()
    {
        _statsLoaded = true;
        var now = DateTime.UtcNow;
        var all = _app.SnippetData.AllStats();
        // Ordner nur, wenn sie benutzt wurden (per Hotkey oder aus der Suche geöffnet), sonst wäre die Liste voll davon
        _statsRows = _app.Library.All
            .Select(snippet => (Snippet: snippet, Stats: all.GetValueOrDefault(snippet.RelativePath)))
            .Where(s => !s.Snippet.IsFolder || s.Stats is not null)
            .Select(s => new UsageRow(s.Snippet, s.Stats,
                s.Stats?.Last is { } last ? UsageText.Ago(last, now) : Strings.StatsNever,
                RowToolTip(s.Snippet, s.Stats, now)))
            .ToList();

        var files = _statsRows.Where(r => !r.Snippet.IsFolder).ToList();
        StatsSummary.Text = string.Format(Strings.StatsSummaryFormat, UsageText.Date(_app.SnippetData.Since),
            _statsRows.Sum(r => r.Total), files.Count(r => r.Total > 0), files.Count);
        ShowStats();
    }

    static string RowToolTip(Snippet snippet, UsageStats? stats, DateTime now)
    {
        var lines = new List<string> { snippet.RelativePath };
        if (snippet.Hotkey is { } hotkey)
            lines.Add(string.Format(Strings.FileHotkeyBadgeTooltipFormat, hotkey));
        if (UsageText.Tooltip(stats, null, now) is { } usage)
            lines.Add(usage);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Filtern und sortieren, ohne die Daten neu zu holen.</summary>
    void ShowStats()
    {
        var filter = StatsFilterInput.Text.Trim();
        StatsFilterPlaceholder.Visibility = StatsFilterInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        IEnumerable<UsageRow> rows = _statsRows;
        if (StatsNeverUsedInput.IsChecked == true)
            rows = rows.Where(r => r.Total == 0 && r.Stats is null);
        if (filter.Length > 0)
            rows = rows.Where(r => SnippetSearch.MatchesAll(r.Name + " " + r.Location + " " + r.LearnedText, filter));

        var (key, _) = StatsColumns[_statsSort];
        // Gleichstand: nach Name, damit die Reihenfolge nicht springt
        rows = _statsDescending
            ? rows.OrderByDescending(key).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            : rows.OrderBy(key).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);

        var selected = (StatsList.SelectedItem as UsageRow)?.Snippet.RelativePath;
        var list = rows.ToList();
        StatsList.ItemsSource = list;
        StatsList.SelectedItem = list.FirstOrDefault(r => r.Snippet.RelativePath == selected);
        UpdateStatsHeader();
    }

    void UpdateStatsHeader()
    {
        foreach (var header in StatsHeader.Children.OfType<TextBlock>())
        {
            var column = (string)header.Tag;
            var text = column switch
            {
                "Name" => Strings.StatsColumnName,
                "Location" => Strings.StatsColumnFolder,
                "Total" => Strings.StatsColumnTotal,
                "Search" => Strings.StatsColumnSearch,
                "Browse" => Strings.StatsColumnBrowse,
                "Hotkey" => Strings.StatsColumnHotkey,
                "Last" => Strings.StatsColumnLast,
                _ => Strings.StatsColumnLearned,
            };
            header.Text = column == _statsSort ? text + (_statsDescending ? " ▾" : " ▴") : text;
        }
    }

    void OnStatsHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock { Tag: string column }) return;
        _statsDescending = column == _statsSort ? !_statsDescending : StatsColumns[column].Descending;
        _statsSort = column;
        ShowStats();
    }

    void OnStatsFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_statsLoaded)
            ShowStats();
    }

    void OnStatsDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StatsList.SelectedItem is UsageRow row)
            _app.RevealInExplorer(row.Snippet);
    }

    void OnStatsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || StatsList.SelectedItem is not UsageRow row) return;
        _app.RevealInExplorer(row.Snippet);
        e.Handled = true;
    }

    // Rechtsklick: die gelernten Suchtexte des Eintrags als Untermenü, je einer zum Vergessen
    void OnStatsContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (StatsList.SelectedItem is not UsageRow row)
        {
            e.Handled = true;
            return;
        }

        StatsForgetMenu.Items.Clear();
        foreach (var query in row.Stats?.Queries ?? [])
        {
            var item = new MenuItem { Header = UsageText.Learned(query) };
            item.Click += (_, _) => ForgetQuery(row, query);
            StatsForgetMenu.Items.Add(item);
        }
        StatsForgetMenu.IsEnabled = StatsForgetMenu.Items.Count > 0;
        StatsResetMenu.IsEnabled = row.Stats is not null;
    }

    void OnStatsRevealMenu(object sender, RoutedEventArgs e)
    {
        if (StatsList.SelectedItem is UsageRow row)
            _app.RevealInExplorer(row.Snippet);
    }

    void OnStatsEditMenu(object sender, RoutedEventArgs e)
    {
        if (StatsList.SelectedItem is UsageRow row)
            _app.EditSnippet(row.Snippet);
    }

    void OnStatsResetMenu(object sender, RoutedEventArgs e)
    {
        if (StatsList.SelectedItem is not UsageRow row) return;
        _app.ResetUsage(row.Snippet);
        UpdateStats();
    }

    internal void ForgetQuery(UsageRow row, LearnedQuery query)
    {
        _app.ForgetQuery(query.Query, row.Snippet);
        UpdateStats();
    }
}
