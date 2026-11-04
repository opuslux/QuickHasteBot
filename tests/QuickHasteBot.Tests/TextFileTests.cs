using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class TextFileTests
{
    static readonly byte[] AnsiGruesse = [0x47, 0x72, 0xFC, 0x73, 0x73, 0x65]; // "Grüsse" in Windows-1252

    [Fact]
    public void Encodings_are_recognized()
    {
        Assert.Equal(new TextFileContent("Grüsse", TextEncoding.Windows1252), TextFile.Decode(AnsiGruesse));
        Assert.Equal(TextEncoding.Utf8, TextFile.Decode("Grüsse"u8).Encoding);
        Assert.Equal(TextEncoding.Utf8Bom, TextFile.Decode([0xEF, 0xBB, 0xBF, .. "Grüsse"u8]).Encoding);
        Assert.Equal(TextEncoding.Utf16, TextFile.Decode([0xFF, 0xFE, (byte)'A', 0]).Encoding);
    }

    [Theory]
    [InlineData(TextEncoding.Windows1252)]
    [InlineData(TextEncoding.Utf8)]
    [InlineData(TextEncoding.Utf8Bom)]
    [InlineData(TextEncoding.Utf16)]
    [InlineData(TextEncoding.Utf16BigEndian)]
    public void Saving_keeps_the_encoding(TextEncoding encoding)
    {
        var (bytes, used) = TextFile.Encode("Grüsse\r\nZeile 2 – €", encoding);
        Assert.Equal(encoding, used);
        Assert.Equal(new TextFileContent("Grüsse\r\nZeile 2 – €", encoding), TextFile.Decode(bytes));
    }

    [Fact]
    public void Ansi_is_kept_byte_for_byte() => Assert.Equal(AnsiGruesse, TextFile.Encode("Grüsse", TextEncoding.Windows1252).Bytes);

    [Fact]
    public void Ansi_becomes_utf8_with_bom_rather_than_losing_characters()
    {
        var (bytes, used) = TextFile.Encode("Grüsse 😀", TextEncoding.Windows1252);
        Assert.Equal(TextEncoding.Utf8Bom, used);
        Assert.Equal("Grüsse 😀", TextFile.Decode(bytes).Text);
    }
}
