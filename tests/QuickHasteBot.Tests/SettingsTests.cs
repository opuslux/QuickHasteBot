using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class SettingsTests
{
    [Fact]
    public void Roundtrips_settings()
    {
        using var dir = new TempDirectory();
        var store = new SettingsStore(Path.Combine(dir.Path, "settings.json"));

        store.Save(new AppSettings { Hotkey = "Ctrl+Alt+Q", PasteMode = PasteMode.Typing, ShowTrayIcon = false, SingleClick = false, StructurePath = @"D:\Bausteine", ListColumnShare = 0.45, PopupWidth = 850 });
        var loaded = store.Load();

        Assert.Equal("Ctrl+Alt+Q", loaded.Hotkey);
        Assert.Equal(0.45, loaded.ListColumnShare);
        Assert.Equal(850, loaded.PopupWidth);
        Assert.Equal(PasteMode.Typing, loaded.PasteMode);
        Assert.False(loaded.ShowTrayIcon);
        Assert.False(loaded.SingleClick);
        Assert.Equal(@"D:\Bausteine", loaded.StructurePath);
        // Von Hand lesbar: Enum als Text, "+" und Backslash nicht als \u-Escape
        var json = File.ReadAllText(store.FilePath);
        Assert.Contains("\"Typing\"", json);
        Assert.Contains("\"Ctrl+Alt+Q\"", json);
        Assert.Contains(@"""D:\\Bausteine""", json);
    }

    [Fact]
    public void Broken_json_gives_defaults()
    {
        using var dir = new TempDirectory();
        var store = new SettingsStore(dir.File("settings.json", "{ kaputt"));

        var loaded = store.Load();

        Assert.Equal(HotkeyGesture.Default.ToString(), loaded.Hotkey);
        Assert.Equal(PasteMode.Clipboard, loaded.PasteMode);
        Assert.True(loaded.SingleClick);
        Assert.Equal(1.0 / 3, loaded.ListColumnShare);
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(0.0, 0.15)]
    [InlineData(2.0, 0.85)]
    [InlineData(double.NaN, 1.0 / 3)]
    public void Column_share_keeps_both_columns_visible(double share, double expected) =>
        Assert.Equal(expected, AppSettings.ClampColumnShare(share));

    [Fact]
    public void Structure_path_is_relative_to_exe_folder()
    {
        var paths = new AppPaths(@"D:\Apps\QuickHasteBot\");

        Assert.Equal(@"D:\Apps\QuickHasteBot\structure", paths.ResolveStructure("structure"));
        Assert.Equal(@"D:\Apps\QuickHasteBot\structure", paths.ResolveStructure(""));
        Assert.Equal(@"D:\Apps\Shared\structure", paths.ResolveStructure(@"..\Shared\structure"));
        Assert.Equal(@"E:\Bausteine", paths.ResolveStructure(@"E:\Bausteine"));
    }

    [Fact]
    public void Structure_path_expands_environment_variables()
    {
        var paths = new AppPaths(@"D:\Apps\QuickHasteBot\");
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(profile, "Bausteine"), paths.ResolveStructure(@"%USERPROFILE%\Bausteine"));
    }
}
