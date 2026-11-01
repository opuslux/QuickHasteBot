using System.Text;
using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

/// <summary>Die echten Textbausteine enthalten alle diese Kodierungen.</summary>
public class TextFileReaderTests
{
    const string Sample = "Freundliche Grüsse\r\nMax Muster – Bestätigung";

    [Fact]
    public void Reads_windows_1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Assert.Equal(Sample, TextFileReader.Decode(Encoding.GetEncoding(1252).GetBytes(Sample)));
    }

    [Fact]
    public void Reads_utf8_without_bom() =>
        Assert.Equal(Sample, TextFileReader.Decode(Encoding.UTF8.GetBytes(Sample)));

    [Fact]
    public void Reads_utf8_with_bom() =>
        Assert.Equal(Sample, TextFileReader.Decode([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Sample)]));

    [Fact]
    public void Reads_utf16_le_with_bom() =>
        Assert.Equal(Sample, TextFileReader.Decode([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Sample)]));

    [Fact]
    public void Reads_utf16_be_with_bom() =>
        Assert.Equal(Sample, TextFileReader.Decode([.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(Sample)]));

    [Fact]
    public void Reads_plain_ascii() =>
        Assert.Equal("SELECT * FROM dual", TextFileReader.Decode(Encoding.ASCII.GetBytes("SELECT * FROM dual")));

    [Fact]
    public void Reads_file_from_disk()
    {
        using var dir = new TempDirectory();
        var path = dir.Bytes("a.txt", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Sample)]);
        Assert.Equal(Sample, TextFileReader.Read(path));
    }
}
