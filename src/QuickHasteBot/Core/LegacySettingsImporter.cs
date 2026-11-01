using System.Xml;
using System.Xml.Linq;

namespace QuickHasteBot.Core;

/// <summary>
/// Übernimmt die settings.xml von QuickPasteBot:
/// &lt;root&gt;&lt;setting name="HotKey1"&gt;&lt;value&gt;MOD_CONTROL&lt;/value&gt;&lt;/setting&gt;...&lt;/root&gt;
/// HotKey1/HotKey2 sind Modifier, HotKey3 ist ein WinForms-Keys-Name, ShowInTray gab es erst ab 2021.
/// </summary>
public static class LegacySettingsImporter
{
    public static AppSettings? TryImport(string xmlPath)
    {
        if (!File.Exists(xmlPath)) return null;

        Dictionary<string, string> values;
        try
        {
            values = XDocument.Load(xmlPath).Root?
                .Elements("setting")
                .Select(e => (Name: (string?)e.Attribute("name"), Value: (string?)e.Element("value")))
                .Where(e => e.Name is not null && e.Value is not null)
                .GroupBy(e => e.Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Value!.Trim(), StringComparer.OrdinalIgnoreCase)
                ?? [];
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"Alte Einstellungen konnten nicht gelesen werden: {xmlPath}", ex);
            return null;
        }

        var settings = new AppSettings();

        var parts = new[] { "HotKey1", "HotKey2", "HotKey3" }
            .Select(name => values.GetValueOrDefault(name))
            .Where(v => !string.IsNullOrEmpty(v));

        if (HotkeyGesture.TryParse(string.Join("+", parts), out var gesture))
            settings.Hotkey = gesture.ToString();

        if (bool.TryParse(values.GetValueOrDefault("ShowInTray"), out var showInTray))
            settings.ShowTrayIcon = showInTray;

        return settings;
    }
}
