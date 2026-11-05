namespace QuickHasteBot.Core;

/// <summary>
/// Die alten Textbausteine sind bunt gemischt: ANSI (Windows-1252), UTF-8 mit/ohne BOM und UTF-16.
/// Reihenfolge: BOM → gültiges UTF-8 → Windows-1252. Details und Zurückschreiben in <see cref="TextFile"/>.
/// </summary>
public static class TextFileReader
{
    public static string Read(string path) => TextFile.Read(path).Text;

    public static string Decode(ReadOnlySpan<byte> bytes) => TextFile.Decode(bytes).Text;
}
