namespace QuickHasteBot.Tests;

public class AppControllerTests
{
    // Textbausteine mit diesen Endungen würden mit "open" ausgeführt statt im Editor geöffnet
    [Theory]
    [InlineData(@"C:\structure\Start.bat", true)]
    [InlineData(@"C:\structure\Start.CMD", true)]
    [InlineData(@"C:\structure\Gruss.txt", false)]
    [InlineData(@"C:\structure\Notiz.md", false)]
    [InlineData(@"C:\structure\Ohne Endung", false)]
    public void Knows_executable_extensions(string path, bool expected) =>
        Assert.Equal(expected, AppController.IsExecutable(path));
}
