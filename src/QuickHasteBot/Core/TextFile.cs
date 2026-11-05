using System.Text;

namespace QuickHasteBot.Core;

public enum TextEncoding { Utf8, Utf8Bom, Utf16, Utf16BigEndian, Windows1252 }

public sealed record TextFileContent(string Text, TextEncoding Encoding);

/// <summary>
/// Textbausteine lesen und so zurückschreiben, wie sie waren: ANSI (Windows-1252) bleibt ANSI, UTF-8 mit/ohne BOM und
/// UTF-16 bleiben es auch. Erkennung: BOM → gültiges UTF-8 → Windows-1252.
/// </summary>
public static class TextFile
{
    static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    static readonly Encoding Windows1252;
    static readonly Encoding StrictWindows1252;

    static TextFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
        StrictWindows1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
    }

    public static TextFileContent Read(string path)
    {
        // FileShare.ReadWrite: Datei darf gleichzeitig im Editor offen sein
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    public static TextFileContent Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            return new(Encoding.UTF8.GetString(bytes[3..]), TextEncoding.Utf8Bom);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            return new(Encoding.Unicode.GetString(bytes[2..]), TextEncoding.Utf16);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            return new(Encoding.BigEndianUnicode.GetString(bytes[2..]), TextEncoding.Utf16BigEndian);

        try
        {
            return new(StrictUtf8.GetString(bytes), TextEncoding.Utf8);
        }
        catch (DecoderFallbackException)
        {
            return new(Windows1252.GetString(bytes), TextEncoding.Windows1252);
        }
    }

    /// <summary>
    /// Schreibt im gewünschten Format. Passt der Text nicht mehr in Windows-1252 (z.B. ein Emoji), wird UTF-8 mit BOM
    /// daraus, statt Zeichen zu verlieren; das BOM macht die Erkennung beim nächsten Lesen eindeutig.
    /// </summary>
    /// <returns>Das Format, in dem tatsächlich geschrieben wurde.</returns>
    public static TextEncoding Write(string path, string text, TextEncoding encoding)
    {
        var (bytes, used) = Encode(text, encoding);
        File.WriteAllBytes(path, bytes);
        return used;
    }

    public static (byte[] Bytes, TextEncoding Used) Encode(string text, TextEncoding encoding)
    {
        switch (encoding)
        {
            case TextEncoding.Windows1252:
                try
                {
                    return (StrictWindows1252.GetBytes(text), TextEncoding.Windows1252);
                }
                catch (EncoderFallbackException)
                {
                    return Encode(text, TextEncoding.Utf8Bom);
                }
            case TextEncoding.Utf8Bom:
                return ([0xEF, 0xBB, 0xBF, .. StrictUtf8.GetBytes(text)], encoding);
            case TextEncoding.Utf16:
                return ([0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)], encoding);
            case TextEncoding.Utf16BigEndian:
                return ([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)], encoding);
            default:
                return (StrictUtf8.GetBytes(text), TextEncoding.Utf8);
        }
    }
}
