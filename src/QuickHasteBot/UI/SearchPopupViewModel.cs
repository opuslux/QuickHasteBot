using System.Collections.ObjectModel;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Leeres Suchfeld: Ordner durchblättern. Mit Suchtext: flache Trefferliste über alle Ordner.
/// </summary>
internal sealed class SearchPopupViewModel(AppController app) : ObservableObject
{
    string _query = "";
    string _folderPath = ""; // relativ zum structure-Ordner, überlebt ein Neuladen der Bibliothek
    SnippetItem? _selected;
    IReadOnlyList<Breadcrumb> _breadcrumbs = [];
    string _detailText = "";
    string _emptyText = "";

    public ObservableCollection<SnippetItem> Items { get; } = [];

    /// <summary>Rechte Spalte, wenn ein Ordner markiert ist: sein Inhalt, direkt anklickbar.</summary>
    public ObservableCollection<SnippetItem> DetailItems { get; } = [];

    public string Query
    {
        get => _query;
        set
        {
            if (!Set(ref _query, value)) return;
            OnPropertyChanged(nameof(ShowPlaceholder));
            Refresh();
        }
    }

    // Ein einzelner Buchstabe trifft fast alles, erst ab zwei wird gesucht. Bis dahin bleibt der Ordner stehen.
    const int MinSearchLength = 2;

    public bool IsSearching => _query.Trim().Length >= MinSearchLength;

    public bool ShowPlaceholder => _query.Length == 0;

    public SnippetItem? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
                UpdateDetail(value?.Snippet);
        }
    }

    /// <summary>Kopfzeile: Ordnerpfad beim Blättern, Trefferzahl beim Suchen.</summary>
    public IReadOnlyList<Breadcrumb> Breadcrumbs
    {
        get => _breadcrumbs;
        private set => Set(ref _breadcrumbs, value);
    }

    /// <summary>Rechte Spalte für alles ausser Ordnern mit Inhalt: der Text, die Adresse oder ein Hinweis.</summary>
    public string DetailText
    {
        get => _detailText;
        private set => Set(ref _detailText, value);
    }

    public bool ShowDetailItems => DetailItems.Count > 0;

    /// <summary>Der Inhalt eines Textbausteins, nicht umbrochen in Monospace. Hinweise werden umbrochen.</summary>
    public bool DetailIsText { get; private set; }

    /// <summary>Umschalter in der Textvorschau, gilt sofort und bleibt gespeichert.</summary>
    public bool WrapPreview
    {
        get => app.Settings.WrapPreview;
        set
        {
            if (app.Settings.WrapPreview == value) return;
            app.Settings.WrapPreview = value;
            app.SaveSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>Stecknadel neben dem Ordnerpfad, dieselbe Einstellung wie im Einstellungsfenster.</summary>
    public bool RememberFolder
    {
        get => app.Settings.RememberFolder;
        set
        {
            if (app.Settings.RememberFolder == value) return;
            app.Settings.RememberFolder = value;
            app.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string EmptyText
    {
        get => _emptyText;
        private set
        {
            if (Set(ref _emptyText, value))
                OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => _emptyText.Length > 0;

    /// <summary>Bearbeiten, Explorer und Einstellungen stehen daneben als Links im Popup.</summary>
    public string PasteHints => app.Settings.PasteMode == PasteMode.Clipboard
        ? Strings.HintsClipboard
        : Strings.HintsTyping;

    /// <summary>Der gerade angezeigte Ordner im Blätter-Modus.</summary>
    public Snippet CurrentFolder => app.Library.Find(_folderPath) ?? app.Library.Root;

    /// <summary>
    /// Jedes Öffnen beginnt wie das alte Menü oben im Wurzelordner, oder auf Wunsch dort, wo man aufgehört hat.
    /// Gibt es den Ordner nicht mehr, geht es oben los.
    /// </summary>
    /// <param name="startFolder">Ordner relativ zum structure-Ordner, z.B. vom Hotkey eines Ordners. Übersteuert das Merken.</param>
    public void Reset(string? startFolder = null)
    {
        var settings = app.Settings;
        _folderPath = startFolder ?? (settings.RememberFolder ? settings.LastFolder : "");
        _query = "";
        OnPropertyChanged(nameof(Query));
        OnPropertyChanged(nameof(ShowPlaceholder));
        // Kann sich inzwischen im Einstellungsfenster geändert haben
        OnPropertyChanged(nameof(PasteHints));
        OnPropertyChanged(nameof(RememberFolder));
        OnPropertyChanged(nameof(WrapPreview));
        Refresh(selectRelativePath: startFolder is null && settings.RememberFolder ? settings.LastSelected : null);
    }

    /// <summary>Beim Schliessen Ordner und markierten Eintrag für das nächste Öffnen speichern.</summary>
    public void RememberPosition()
    {
        var settings = app.Settings;
        if (!settings.RememberFolder) return;

        // Ein Suchtreffer liegt meist nicht im Ordner, dann beginnt es dort beim ersten Eintrag
        var selected = IsSearching ? "" : _selected?.Snippet.RelativePath ?? "";
        if (settings.LastFolder == _folderPath && settings.LastSelected == selected) return;

        settings.LastFolder = _folderPath;
        settings.LastSelected = selected;
        app.SaveSettings();
    }

    public void Refresh(string? selectRelativePath = null) => Refresh([selectRelativePath ?? _selected?.Snippet.RelativePath]);

    /// <summary>Nach dem Klonen: die Kopie markieren, falls sie in der Liste steht, sonst bleibt die Auswahl.</summary>
    public void RefreshSelecting(string relativePath) => Refresh([relativePath, _selected?.Snippet.RelativePath]);

    /// <summary>
    /// Was nach dem Löschen markiert sein soll: War es die Auswahl links, der nächste Eintrag (am Ende der vorige),
    /// damit die Liste nicht nach oben springt. Sonst, z.B. beim Löschen rechts, bleibt die Auswahl.
    /// </summary>
    public string? SelectionAfterRemoving(Snippet snippet)
    {
        if (_selected is null || !SamePath(_selected.Snippet.RelativePath, snippet.RelativePath))
            return _selected?.Snippet.RelativePath;

        var index = Items.IndexOf(_selected);
        var neighbor = index + 1 < Items.Count ? Items[index + 1] : index > 0 ? Items[index - 1] : null;
        return neighbor?.Snippet.RelativePath;
    }

    /// <summary>Der erste Pfad, der in der neuen Liste vorkommt, wird markiert, ohne Treffer der erste Eintrag.</summary>
    void Refresh(string?[] preferredSelection)
    {
        var library = app.Library;

        List<SnippetItem> items;
        if (IsSearching)
        {
            var contentProvider = app.Settings.SearchContent ? library.GetSearchableText : (Func<Snippet, string?>?)null;
            items = SnippetSearch.Search(library.All, _query, contentProvider).Select(hit => SnippetItem.ForHit(hit, app.HotkeyProblem(hit.Snippet))).ToList();
            Breadcrumbs = [new(items.Count == 1 ? Strings.MatchesOne : string.Format(Strings.MatchesFormat, items.Count), Target: null, ShowSeparator: false)];
            EmptyText = items.Count == 0 ? Strings.NoMatches : "";
        }
        else
        {
            var folder = library.Find(_folderPath);
            if (folder is null || !folder.IsFolder)
            {
                folder = library.Root;
                _folderPath = "";
            }

            items = folder.Children.Select(child => SnippetItem.ForBrowsing(child, app.HotkeyProblem(child))).ToList();
            Breadcrumbs = BuildBreadcrumbs(folder.RelativePath);
            EmptyText = items.Count > 0 ? ""
                : !Directory.Exists(library.RootPath) ? string.Format(Strings.FolderNotFoundFormat, library.RootPath)
                : Strings.FolderEmpty;
        }

        Items.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Shortcut = i < ShortcutCount ? (i + 1).ToString() : "";
            Items.Add(items[i]);
        }

        // Ohne Treffer der alten Auswahl immer den ersten Eintrag wählen, damit Enter sofort etwas tut.
        // Die rechte Spalte immer neu aufbauen, auch wenn nichts mehr ausgewählt ist oder sich der Ordner geändert hat.
        _selected = preferredSelection
                        .Select(path => items.FirstOrDefault(i => SamePath(i.Snippet.RelativePath, path)))
                        .FirstOrDefault(i => i is not null)
                    ?? items.FirstOrDefault();
        OnPropertyChanged(nameof(Selected));
        UpdateDetail(_selected?.Snippet);
    }

    /// <summary>Alt+1 bis Alt+9 erreichen die ersten neun Zeilen links.</summary>
    public const int ShortcutCount = 9;

    /// <summary>Wählt die Zeile mit der Nummer aus, falls es so viele gibt.</summary>
    public bool SelectNumber(int number)
    {
        if (number < 1 || number > Math.Min(ShortcutCount, Items.Count)) return false;
        Selected = Items[number - 1];
        return true;
    }

    public void MoveSelection(int delta)
    {
        if (Items.Count == 0) return;
        var index = _selected is null ? -1 : Items.IndexOf(_selected);
        Selected = Items[Math.Clamp(index + delta, 0, Items.Count - 1)];
    }

    public void SelectEdge(bool last)
    {
        if (Items.Count > 0)
            Selected = last ? Items[^1] : Items[0];
    }

    public void EnterFolder(Snippet folder)
    {
        _folderPath = folder.RelativePath;
        _query = "";
        OnPropertyChanged(nameof(Query));
        OnPropertyChanged(nameof(ShowPlaceholder));
        _selected = null;
        Refresh(selectRelativePath: "");
    }

    /// <summary>Eine Ebene hoch, der Ordner, aus dem man kommt, bleibt ausgewählt.</summary>
    public bool GoUp()
    {
        if (_folderPath.Length == 0) return false;
        GoTo(Path.GetDirectoryName(_folderPath) ?? "");
        return true;
    }

    /// <summary>Zurück in einen übergeordneten Ordner, der Unterordner auf dem Weg dorthin bleibt ausgewählt.</summary>
    public void GoTo(string ancestorPath)
    {
        var parts = SplitPath(_folderPath);
        var depth = SplitPath(ancestorPath).Length;
        if (depth >= parts.Length) return;

        _folderPath = ancestorPath;
        Refresh(selectRelativePath: Path.Combine(parts[..(depth + 1)]));
    }

    static IReadOnlyList<Breadcrumb> BuildBreadcrumbs(string folderPath)
    {
        var parts = SplitPath(folderPath);
        var crumbs = new List<Breadcrumb> { new(Strings.BreadcrumbRoot, parts.Length > 0 ? "" : null, ShowSeparator: false) };
        for (var i = 0; i < parts.Length; i++)
        {
            var isCurrent = i == parts.Length - 1;
            crumbs.Add(new(FileHotkey.Split(parts[i]).Name, isCurrent ? null : Path.Combine(parts[..(i + 1)]), ShowSeparator: true));
        }
        return crumbs;
    }

    static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static string[] SplitPath(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    void UpdateDetail(Snippet? snippet)
    {
        DetailItems.Clear();
        if (snippet is { IsFolder: true })
        {
            foreach (var child in snippet.Children)
                DetailItems.Add(SnippetItem.ForBrowsing(child, app.HotkeyProblem(child)));
        }
        OnPropertyChanged(nameof(ShowDetailItems));

        var text = snippet?.Kind == SnippetKind.Text ? app.Library.GetSearchableText(snippet) : null;
        DetailIsText = text is not null;
        OnPropertyChanged(nameof(DetailIsText));
        DetailText = text?.ReplaceLineEndings() ?? BuildHint(snippet);
    }

    string BuildHint(Snippet? snippet)
    {
        if (snippet is null) return "";

        switch (snippet.Kind)
        {
            case SnippetKind.Folder:
                return snippet.Children.Count == 0 ? Strings.FolderEmpty : "";

            case SnippetKind.Text:
                return Strings.PreviewTooLarge;

            default:
                if (Path.GetExtension(snippet.FullPath).Equals(".url", StringComparison.OrdinalIgnoreCase))
                {
                    var url = TryReadUrl(snippet.FullPath);
                    if (url is not null) return string.Format(Strings.OpensUrlFormat, url);
                }
                return Strings.OpensWithApp;
        }
    }

    static string? TryReadUrl(string path)
    {
        try
        {
            return TextFileReader.Read(path)
                .ReplaceLineEndings("\n").Split('\n')
                .FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
