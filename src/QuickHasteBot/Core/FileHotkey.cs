using System.Text.RegularExpressions;

namespace QuickHasteBot.Core;

/// <summary>
/// Der Hotkey einer Datei oder eines Ordners steht im Namen, als Tag am Ende: "Gruss [Ctrl+Alt+G].txt",
/// "3-SQL [Ctrl+Alt+S]". So braucht es keine eigene Konfiguration, und der Hotkey reist beim Kopieren,
/// Zippen oder Synchronisieren mit. Nur ein gültiger Hotkey zählt, "Bericht [Entwurf].txt" bleibt ein Name.
/// </summary>
public static partial class FileHotkey
{
    [GeneratedRegex(@"^(?<name>.*?)\s*\[(?<hotkey>[^\[\]]+)\]$")]
    private static partial Regex TagPattern();

    /// <param name="stem">Dateiname ohne Endung, bei Ordnern der ganze Name.</param>
    /// <returns>Der Name ohne Tag und der Hotkey, ohne gültiges Tag der Name unverändert und null.</returns>
    public static (string Name, HotkeyGesture? Hotkey) Split(string stem)
    {
        var match = TagPattern().Match(stem);
        if (!match.Success || !HotkeyGesture.TryParse(match.Groups["hotkey"].Value, out var gesture))
            return (stem, null);

        // "[Ctrl+Alt+G].txt" ganz ohne Namen: dann eben das Tag als Name, damit die Zeile nicht leer ist
        var name = match.Groups["name"].Value;
        return (name.Length == 0 ? stem : name, gesture);
    }

    /// <summary>Teil des Namens, der das Tag trägt: bei Dateien ohne Endung, bei Ordnern alles.</summary>
    public static string Stem(string fileName, bool isFolder) => isFolder ? fileName : Path.GetFileNameWithoutExtension(fileName);

    /// <summary>Der Hotkey aus einem Datei- oder Ordnernamen, null ohne gültiges Tag.</summary>
    public static HotkeyGesture? FromFileName(string fileName, bool isFolder) => Split(Stem(fileName, isFolder)).Hotkey;

    /// <summary>Der Name ohne Tag, aber mit Endung: "Gruss [Ctrl+Alt+G].txt" wird zu "Gruss.txt".</summary>
    public static string WithoutHotkey(string fileName, bool isFolder) => WithHotkey(fileName, null, isFolder);

    /// <summary>Setzt, ersetzt oder entfernt (null) das Tag, die Endung bleibt.</summary>
    public static string WithHotkey(string fileName, HotkeyGesture? gesture, bool isFolder)
    {
        var stem = Stem(fileName, isFolder);
        var extension = isFolder ? "" : Path.GetExtension(fileName);
        var (name, hotkey) = Split(stem);
        // Ein Name, der nur aus dem Tag bestand, hat kein eigenes Wort davor. Fällt das Tag weg, bleibt die
        // Kombination ohne Klammern als Name ("Ctrl+Alt+G.txt"), statt einer Datei, die nur ".txt" heisst.
        if (hotkey is { } old && name == stem)
            name = gesture is null ? old.ToString() : "";
        return Tagged(name, gesture) + extension;
    }

    /// <summary>"Gruss" und Ctrl+Alt+G ergeben "Gruss [Ctrl+Alt+G]", ohne Hotkey nur den Namen.</summary>
    public static string Tagged(string name, HotkeyGesture? gesture) =>
        gesture is { } g ? (name.Length == 0 ? $"[{g}]" : $"{name} [{g}]") : name;
}
