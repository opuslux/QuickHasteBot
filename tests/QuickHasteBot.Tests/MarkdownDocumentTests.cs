using QuickHasteBot.UI;

namespace QuickHasteBot.Tests;

/// <summary>
/// Wohin Links aus dem README im Hilfe-Fenster führen. Aufbau und Anker prüft <see cref="WindowSmokeTests"/>:
/// Ein FlowDocument braucht die Application mit dem Fluent-Theme, und die gibt es nur einmal, auf dem Thread jenes Tests.
/// </summary>
public class MarkdownDocumentTests
{
    [Theory]
    [InlineData("#scripts", null)]
    [InlineData("LICENSE", "https://github.com/opuslux/QuickHasteBot/blob/main/LICENSE")]
    [InlineData(".github/workflows/release.yml", "https://github.com/opuslux/QuickHasteBot/blob/main/.github/workflows/release.yml")]
    [InlineData("./THIRD-PARTY-NOTICES.md", "https://github.com/opuslux/QuickHasteBot/blob/main/THIRD-PARTY-NOTICES.md")]
    [InlineData("https://dotnet.microsoft.com/download/dotnet/10.0", "https://dotnet.microsoft.com/download/dotnet/10.0")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("file:///C:/Windows/notepad.exe", null)]
    public void Link_targets(string url, string? expected) => Assert.Equal(expected, HelpWindow.ExternalTarget(url));
}
