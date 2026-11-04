namespace QuickHasteBot.Core;

public enum NameProblem
{
    None,
    Empty,

    /// <summary>Zeichen wie \ / : * ? " &lt; &gt; | oder ein Punkt am Ende, was Windows nicht zulässt.</summary>
    Invalid,

    /// <summary>Im Ordner gibt es schon eine Datei oder einen Unterordner mit dem Namen.</summary>
    Exists,

    /// <summary>Beim Umbenennen: genau der bisherige Name.</summary>
    Unchanged,
}

/// <summary>Dateien im structure-Ordner klonen und umbenennen. Löschen geht über den Papierkorb, siehe Interop.RecycleBin.</summary>
public static class SnippetFiles
{
    /// <summary>
    /// Wie im Explorer: "Gruss.txt" wird zu "Gruss - Kopie.txt", gibt es die schon, zu "Gruss - Kopie (2).txt" usw.
    /// </summary>
    /// <param name="copySuffix">" - Kopie" bzw. " - Copy", je nach Sprache.</param>
    public static string SuggestCopyName(string path, string copySuffix)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        var name = stem + copySuffix + extension;
        for (var n = 2; Exists(Path.Combine(folder, name)); n++)
            name = $"{stem}{copySuffix} ({n}){extension}";
        return name;
    }

    /// <param name="currentName">Beim Umbenennen der bisherige Dateiname: Nur Gross/Klein zu ändern ist dann erlaubt.</param>
    public static NameProblem Check(string folder, string fileName, string? currentName = null)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return NameProblem.Empty;
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName.EndsWith('.') || fileName.EndsWith(' '))
            return NameProblem.Invalid;
        if (currentName is not null && string.Equals(fileName, currentName, StringComparison.OrdinalIgnoreCase))
            return fileName == currentName ? NameProblem.Unchanged : NameProblem.None;
        return Exists(Path.Combine(folder, fileName)) ? NameProblem.Exists : NameProblem.None;
    }

    /// <summary>Datei oder Ordner bekommt einen neuen Namen am selben Ort, überschreibt nie etwas.</summary>
    /// <returns>Vollständiger neuer Pfad.</returns>
    public static string Rename(string path, string name)
    {
        var target = Path.Combine(Path.GetDirectoryName(path) ?? "", name);
        if (Directory.Exists(path))
            Directory.Move(path, target);
        else
            File.Move(path, target, overwrite: false);
        return target;
    }

    /// <summary>Kopie im selben Ordner, überschreibt nie etwas.</summary>
    /// <returns>Vollständiger Pfad der Kopie.</returns>
    public static string Clone(string path, string fileName)
    {
        var target = Path.Combine(Path.GetDirectoryName(path) ?? "", fileName);
        File.Copy(path, target, overwrite: false);
        return target;
    }

    static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
