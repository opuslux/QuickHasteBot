using System.Reflection;
using System.Text;

namespace QuickHasteBot.Core;

/// <summary>
/// Beispielordner für den ersten Start: Samples\de und Samples\en im Projekt, als Ressource in der exe.
/// So bleibt es bei einer einzigen exe, die Sprache passt zur Oberfläche, und ein über eine bestehende
/// Installation entpacktes Update überschreibt keine Textbausteine.
/// </summary>
public static class SampleStructure
{
    const string Prefix = "Samples/";
    const string Fallback = "en";

    static readonly Assembly Assembly = typeof(SampleStructure).Assembly;

    /// <summary>Alle Dateien einer Sprache, Pfad relativ zum structure-Ordner, Zeilenenden wie unter Windows üblich.</summary>
    public static IReadOnlyList<(string Path, string Text)> Files(string language)
    {
        var files = Read(language);
        return files.Count > 0 ? files : Read(Fallback);
    }

    /// <summary>
    /// Legt die Beispiele im Ordner an, UTF-8 ohne BOM wie die übrigen Textbausteine. Gleichnamige Dateien werden
    /// überschrieben, alle anderen bleiben, wie sie sind.
    /// </summary>
    /// <returns>Anzahl geschriebener Dateien.</returns>
    public static int Extract(string root, string language)
    {
        var files = Files(language);
        foreach (var (path, text) in files)
        {
            var target = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text, new UTF8Encoding(false));
        }
        return files.Count;
    }

    static List<(string, string)> Read(string language)
    {
        // LogicalName aus der csproj, z.B. "Samples/de\1-Grüsse\Freundliche Grüsse.txt"
        var prefix = $"{Prefix}{language}/";
        var files = new List<(string, string)>();
        foreach (var name in Assembly.GetManifestResourceNames())
        {
            var normalized = name.Replace('\\', '/');
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            using var stream = Assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var path = normalized[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            // Im Repo je nach Checkout LF oder CRLF, Notepad & Co. erwarten CRLF
            files.Add((path, reader.ReadToEnd().ReplaceLineEndings("\r\n")));
        }
        files.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return files;
    }
}
