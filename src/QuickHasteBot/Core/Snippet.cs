namespace QuickHasteBot.Core;

public enum SnippetKind
{
    Folder,

    /// <summary>Wird eingefügt.</summary>
    Text,

    /// <summary>Wird mit der zugeordneten Anwendung geöffnet (z.B. .url).</summary>
    File,
}

/// <summary>Ein Eintrag im structure-Ordner: Ordner, Textbaustein oder sonstige Datei.</summary>
public sealed class Snippet
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    /// <summary>Pfad relativ zum structure-Ordner, inklusive Extension. Leer beim Wurzelordner.</summary>
    public required string RelativePath { get; init; }

    public required SnippetKind Kind { get; init; }

    /// <summary>Globaler Hotkey aus dem Tag im Namen, z.B. "Gruss [Ctrl+Alt+G].txt". <see cref="Name"/> ist ohne Tag.</summary>
    public HotkeyGesture? Hotkey { get; init; }

    public Snippet? Parent { get; init; }

    public List<Snippet> Children { get; } = [];

    public bool IsFolder => Kind == SnippetKind.Folder;

    /// <summary>Anzeige des Elternordners, z.B. "5-SQL › Archiv". Leer bei Einträgen direkt im Wurzelordner.</summary>
    public string Location
    {
        get
        {
            var parent = Path.GetDirectoryName(RelativePath);
            // Ohne die Hotkey-Tags der Ordner, wie in der Liste
            return string.IsNullOrEmpty(parent)
                ? ""
                : string.Join(" › ", parent.Split(Path.DirectorySeparatorChar).Select(folder => FileHotkey.Split(folder).Name));
        }
    }

    public override string ToString() => RelativePath;
}
