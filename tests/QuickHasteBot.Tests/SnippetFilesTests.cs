using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class SnippetFilesTests
{
    [Fact]
    public void Copy_name_counts_up_like_explorer()
    {
        using var dir = new TempDirectory();
        var source = dir.File("Gruss.txt", "Gruss");

        Assert.Equal("Gruss - Kopie.txt", SnippetFiles.SuggestCopyName(source, " - Kopie"));
        dir.File("Gruss - Kopie.txt", "");
        Assert.Equal("Gruss - Kopie (2).txt", SnippetFiles.SuggestCopyName(source, " - Kopie"));
        // Auch ein Ordner mit dem Namen zählt als belegt
        Directory.CreateDirectory(Path.Combine(dir.Path, "Gruss - Kopie (2).txt"));
        Assert.Equal("Gruss - Kopie (3).txt", SnippetFiles.SuggestCopyName(source, " - Kopie"));
    }

    [Fact]
    public void Copy_name_without_extension()
    {
        using var dir = new TempDirectory();
        Assert.Equal("Notiz - Copy", SnippetFiles.SuggestCopyName(dir.File("Notiz", ""), " - Copy"));
    }

    [Theory]
    [InlineData("Neu.txt", NameProblem.None)]
    [InlineData("", NameProblem.Empty)]
    [InlineData("   ", NameProblem.Empty)]
    [InlineData("a:b.txt", NameProblem.Invalid)]
    [InlineData(@"Unter\Ordner.txt", NameProblem.Invalid)]
    [InlineData("Frage?.txt", NameProblem.Invalid)]
    [InlineData("Punkt.", NameProblem.Invalid)]
    [InlineData("Gruss.txt", NameProblem.Exists)]
    [InlineData("GRUSS.TXT", NameProblem.Exists)]
    public void Checks_new_names(string name, NameProblem expected)
    {
        using var dir = new TempDirectory();
        dir.File("Gruss.txt", "");
        Assert.Equal(expected, SnippetFiles.Check(dir.Path, name));
    }

    [Theory]
    [InlineData("Gruss.txt", NameProblem.Unchanged)]
    [InlineData("gruss.TXT", NameProblem.None)]
    [InlineData("Gruss CH.txt", NameProblem.None)]
    [InlineData("Andere.txt", NameProblem.Exists)]
    public void Renaming_may_change_only_the_case(string name, NameProblem expected)
    {
        using var dir = new TempDirectory();
        dir.File("Gruss.txt", "");
        dir.File("Andere.txt", "");
        Assert.Equal(expected, SnippetFiles.Check(dir.Path, name, currentName: "Gruss.txt"));
    }

    [Theory]
    [InlineData("Gruss CH.txt")]
    [InlineData("gruss.txt")]
    public void Rename_file(string name)
    {
        using var dir = new TempDirectory();
        var source = dir.File(@"Mails\Gruss.txt", "Freundliche Grüsse");

        var target = SnippetFiles.Rename(source, name);

        Assert.Equal(Path.Combine(dir.Path, "Mails", name), target);
        Assert.Equal([name], Directory.GetFiles(Path.Combine(dir.Path, "Mails")).Select(Path.GetFileName));
        Assert.Equal("Freundliche Grüsse", File.ReadAllText(target));
    }

    [Theory]
    [InlineData("2-Mails alt")]
    [InlineData("1-MAILS")]
    public void Rename_folder_with_contents(string name)
    {
        using var dir = new TempDirectory();
        var file = dir.File(@"1-Mails\Gruss.txt", "Gruss");

        var target = SnippetFiles.Rename(Path.GetDirectoryName(file)!, name);

        Assert.Equal([name], Directory.GetDirectories(dir.Path).Select(Path.GetFileName));
        Assert.Equal("Gruss", File.ReadAllText(Path.Combine(target, "Gruss.txt")));
    }

    [Fact]
    public void Rename_never_overwrites()
    {
        using var dir = new TempDirectory();
        var source = dir.File("Gruss.txt", "neu");
        var existing = dir.File("Alt.txt", "alt");

        Assert.Throws<IOException>(() => SnippetFiles.Rename(source, "Alt.txt"));
        Assert.Equal("alt", File.ReadAllText(existing));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Clone_copies_into_the_same_folder()
    {
        using var dir = new TempDirectory();
        var source = dir.File(@"Mails\Gruss.txt", "Freundliche Grüsse");

        var copy = SnippetFiles.Clone(source, "Gruss CH.txt");

        Assert.Equal(Path.Combine(dir.Path, "Mails", "Gruss CH.txt"), copy);
        Assert.Equal("Freundliche Grüsse", File.ReadAllText(copy));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Clone_never_overwrites()
    {
        using var dir = new TempDirectory();
        var source = dir.File("Gruss.txt", "neu");
        var existing = dir.File("Alt.txt", "alt");

        Assert.Throws<IOException>(() => SnippetFiles.Clone(source, "Alt.txt"));
        Assert.Equal("alt", File.ReadAllText(existing));
    }
}
