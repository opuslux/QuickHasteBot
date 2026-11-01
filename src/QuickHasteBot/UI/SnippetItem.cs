using System.Windows.Media;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;

namespace QuickHasteBot.UI;

/// <summary>Eine Zeile im Such-Popup. Der Name ist für die Hervorhebung des Treffers dreigeteilt.</summary>
internal sealed class SnippetItem
{
    ImageSource? _icon;
    bool _iconLoaded;

    SnippetItem(Snippet snippet, bool showLocation, int matchStart, int matchLength)
    {
        Snippet = snippet;
        ShowLocation = showLocation && snippet.Location.Length > 0;

        var name = snippet.Name;
        if (matchStart >= 0 && matchStart + matchLength <= name.Length)
        {
            NameBefore = name[..matchStart];
            NameMatch = name.Substring(matchStart, matchLength);
            NameAfter = name[(matchStart + matchLength)..];
        }
        else
        {
            NameBefore = name;
        }
    }

    public Snippet Snippet { get; }

    public string NameBefore { get; } = "";

    public string NameMatch { get; } = "";

    public string NameAfter { get; } = "";

    public string Location => Snippet.Location;

    public bool ShowLocation { get; }

    public bool IsFolder => Snippet.IsFolder;

    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _icon = ShellIcons.For(Snippet);
                _iconLoaded = true;
            }
            return _icon;
        }
    }

    public static SnippetItem ForBrowsing(Snippet snippet) => new(snippet, showLocation: false, -1, 0);

    public static SnippetItem ForHit(SearchHit hit) => new(hit.Snippet, showLocation: true, hit.MatchStart, hit.MatchLength);
}
