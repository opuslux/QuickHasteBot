using System.Collections.ObjectModel;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Leeres Suchfeld: Ordner durchblättern. Mit Suchtext: flache Trefferliste über alle Ordner.
/// </summary>
internal sealed class SearchPopupViewModel(AppController app) : ObservableObject
{
    const int PreviewLines = 8;

    string _query = "";
    string _folderPath = ""; // relativ zum structure-Ordner, überlebt ein Neuladen der Bibliothek
    SnippetItem? _selected;
    string _header = "";
    string _preview = "";
    string _emptyText = "";

    public ObservableCollection<SnippetItem> Items { get; } = [];

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

    public bool IsSearching => !string.IsNullOrWhiteSpace(_query);

    public bool ShowPlaceholder => _query.Length == 0;

    public SnippetItem? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
                Preview = BuildPreview(value?.Snippet);
        }
    }

    public string Header
    {
        get => _header;
        private set => Set(ref _header, value);
    }

    public string Preview
    {
        get => _preview;
        private set
        {
            if (Set(ref _preview, value))
                OnPropertyChanged(nameof(HasPreview));
        }
    }

    public bool HasPreview => _preview.Length > 0;

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

    public string Hints => app.Settings.PasteMode == PasteMode.Clipboard
        ? "↵ Einfügen   ⇧↵ Tippen   Strg+O Bearbeiten   Strg+E Explorer   Strg+, Einstellungen"
        : "↵ Tippen   ⇧↵ Einfügen   Strg+O Bearbeiten   Strg+E Explorer   Strg+, Einstellungen";

    /// <summary>Der gerade angezeigte Ordner im Blätter-Modus.</summary>
    public Snippet CurrentFolder => app.Library.Find(_folderPath) ?? app.Library.Root;

    /// <summary>Jedes Öffnen beginnt wie das alte Menü oben im Wurzelordner.</summary>
    public void Reset()
    {
        _folderPath = "";
        _query = "";
        OnPropertyChanged(nameof(Query));
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(Hints));
        Refresh();
    }

    public void Refresh(string? selectRelativePath = null)
    {
        selectRelativePath ??= _selected?.Snippet.RelativePath;
        var library = app.Library;

        List<SnippetItem> items;
        if (IsSearching)
        {
            var contentProvider = app.Settings.SearchContent ? library.GetSearchableText : (Func<Snippet, string?>?)null;
            items = SnippetSearch.Search(library.All, _query, contentProvider).Select(SnippetItem.ForHit).ToList();
            Header = items.Count == 1 ? "1 Treffer" : $"{items.Count} Treffer";
            EmptyText = items.Count == 0 ? "Keine Treffer" : "";
        }
        else
        {
            var folder = library.Find(_folderPath);
            if (folder is null || !folder.IsFolder)
            {
                folder = library.Root;
                _folderPath = "";
            }

            items = folder.Children.Select(SnippetItem.ForBrowsing).ToList();
            Header = "Textbausteine" + string.Concat(
                folder.RelativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Select(part => " › " + part));
            EmptyText = items.Count > 0 ? ""
                : !Directory.Exists(library.RootPath) ? $"Ordner nicht gefunden: {library.RootPath}"
                : "Dieser Ordner ist leer. Strg+E öffnet ihn im Explorer.";
        }

        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        // Ohne Treffer der alten Auswahl immer den ersten Eintrag wählen, damit Enter sofort etwas tut
        _selected = null;
        Selected = items.FirstOrDefault(i => string.Equals(i.Snippet.RelativePath, selectRelativePath, StringComparison.OrdinalIgnoreCase))
                   ?? items.FirstOrDefault();
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
        var child = _folderPath;
        _folderPath = Path.GetDirectoryName(_folderPath) ?? "";
        Refresh(selectRelativePath: child);
        return true;
    }

    string BuildPreview(Snippet? snippet)
    {
        if (snippet is null) return "";

        switch (snippet.Kind)
        {
            case SnippetKind.Folder:
                return snippet.Children.Count == 1 ? "1 Eintrag" : $"{snippet.Children.Count} Einträge";

            case SnippetKind.Text:
                var text = app.Library.GetSearchableText(snippet);
                if (text is null) return "(Datei zu gross für die Vorschau)";
                var lines = text.ReplaceLineEndings("\n").Split('\n');
                var preview = string.Join(Environment.NewLine, lines.Take(PreviewLines));
                return lines.Length > PreviewLines ? preview + Environment.NewLine + "…" : preview;

            default:
                if (Path.GetExtension(snippet.FullPath).Equals(".url", StringComparison.OrdinalIgnoreCase))
                {
                    var url = TryReadUrl(snippet.FullPath);
                    if (url is not null) return "Öffnet " + url;
                }
                return "Öffnet mit der zugeordneten Anwendung";
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
