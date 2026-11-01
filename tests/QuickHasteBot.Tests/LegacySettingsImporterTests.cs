using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class LegacySettingsImporterTests
{
    // So liegt sie in st_Apps\QuickPasteBot (Version 2012, ohne ShowInTray)
    const string Xml2012 = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <setting name="HotKey1"><value>MOD_CONTROL</value></setting>
          <setting name="HotKey2"><value>MOD_SHIFT</value></setting>
          <setting name="HotKey3"><value>Y</value></setting>
        </root>
        """;

    const string Xml2021 = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <setting name="HotKey1"><value>MOD_SHIFT</value></setting>
          <setting name="HotKey2"><value>MOD_ALT</value></setting>
          <setting name="HotKey3"><value>F5</value></setting>
          <setting name="ShowInTray"><value>False</value></setting>
        </root>
        """;

    [Fact]
    public void Imports_hotkey_from_2012_version()
    {
        using var dir = new TempDirectory();
        var settings = LegacySettingsImporter.TryImport(dir.File("settings.xml", Xml2012));

        Assert.NotNull(settings);
        Assert.Equal("Ctrl+Shift+Y", settings.Hotkey);
        Assert.True(settings.ShowTrayIcon);
    }

    [Fact]
    public void Imports_hotkey_and_tray_from_2021_version()
    {
        using var dir = new TempDirectory();
        var settings = LegacySettingsImporter.TryImport(dir.File("settings.xml", Xml2021));

        Assert.NotNull(settings);
        Assert.Equal("Shift+Alt+F5", settings.Hotkey);
        Assert.False(settings.ShowTrayIcon);
    }

    [Fact]
    public void Missing_file_returns_null()
    {
        using var dir = new TempDirectory();
        Assert.Null(LegacySettingsImporter.TryImport(Path.Combine(dir.Path, "settings.xml")));
    }

    [Fact]
    public void Broken_file_returns_null()
    {
        using var dir = new TempDirectory();
        Assert.Null(LegacySettingsImporter.TryImport(dir.File("settings.xml", "<root><setting")));
    }

    [Fact]
    public void Invalid_hotkey_keeps_default()
    {
        using var dir = new TempDirectory();
        var xml = Xml2012.Replace("<value>Y</value>", "<value></value>");
        var settings = LegacySettingsImporter.TryImport(dir.File("settings.xml", xml));

        Assert.Equal(HotkeyGesture.Default.ToString(), settings!.Hotkey);
    }
}
