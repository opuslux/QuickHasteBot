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

/// <summary>Dateien und Ordner im structure-Ordner kopieren, verschieben und umbenennen. Löschen geht über den Papierkorb, siehe Interop.RecycleBin.</summary>
public static class SnippetFiles
{
    /// <summary>
    /// Wie im Explorer: "Gruss.txt" wird zu "Gruss - Kopie.txt", gibt es die schon, zu "Gruss - Kopie (2).txt" usw.
    /// Bei Ordnern zählt der ganze Name, auch mit Punkt darin.
    /// </summary>
    /// <param name="copySuffix">" - Kopie" bzw. " - Copy", je nach Sprache.</param>
    /// <param name="folder">Wo die Kopie hinkommt, ohne Angabe neben das Original.</param>
    public static string SuggestCopyName(string path, string copySuffix, string? folder = null)
    {
        folder ??= Path.GetDirectoryName(path) ?? "";
        var isFolder = Directory.Exists(path);
        var stem = isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        var extension = isFolder ? "" : Path.GetExtension(path);

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

    /// <summary>Datei oder Ordner kommt in einen anderen Ordner, auf Wunsch gleich mit neuem Namen. Überschreibt nie etwas.</summary>
    /// <returns>Vollständiger neuer Pfad.</returns>
    public static string Move(string path, string folder, string name)
    {
        var target = Path.Combine(folder, name);
        if (Directory.Exists(path))
        {
            // Directory.Move legt einen Ordner nicht in einen gleichnamigen, meldet aber nur einen unklaren Fehler
            if (Exists(target))
                throw new IOException($"Existiert schon: {target}");
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target, overwrite: false);
        }
        return target;
    }

    /// <summary>Wie im Explorer: "Neuer Ordner", gibt es den schon, "Neuer Ordner (2)" usw.</summary>
    /// <param name="baseName">"Neuer Ordner" bzw. "New folder", je nach Sprache.</param>
    public static string SuggestNewFolderName(string parent, string baseName)
    {
        var name = baseName;
        for (var n = 2; Exists(Path.Combine(parent, name)); n++)
            name = $"{baseName} ({n})";
        return name;
    }

    /// <summary>Neuer, leerer Ordner. Gibt es den Namen schon (auch als Datei), schlägt es fehl, statt ihn mitzubenutzen.</summary>
    /// <returns>Vollständiger Pfad des neuen Ordners.</returns>
    public static string CreateFolder(string parent, string name)
    {
        var target = Path.Combine(parent, name);
        if (Exists(target))
            throw new IOException($"Existiert schon: {target}");
        Directory.CreateDirectory(target);
        return target;
    }

    /// <summary>Kopie einer Datei oder eines Ordners mit allem darin, auch in denselben Ordner. Überschreibt nie etwas.</summary>
    /// <returns>Vollständiger Pfad der Kopie.</returns>
    public static string Copy(string path, string folder, string name)
    {
        var target = Path.Combine(folder, name);
        if (!Directory.Exists(path))
        {
            File.Copy(path, target, overwrite: false);
            return target;
        }

        if (Exists(target))
            throw new IOException($"Existiert schon: {target}");
        // In sich selbst kopiert, würde es sich endlos wiederholen
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(target).StartsWith(source, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Ein Ordner lässt sich nicht in sich selbst kopieren: {target}");
        CopyFolder(new DirectoryInfo(path), target);
        return target;
    }

    static void CopyFolder(DirectoryInfo source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in source.GetFiles())
            file.CopyTo(Path.Combine(target, file.Name), overwrite: false);
        foreach (var sub in source.GetDirectories())
            CopyFolder(sub, Path.Combine(target, sub.Name));
    }

    static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
