using Microsoft.Win32;

namespace QuickHasteBot.Core;

/// <summary>Autostart über HKCU\...\Run, braucht keine Adminrechte.</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "QuickHasteBot";

    /// <summary>Eintrag der alten Version, den man beim Umstieg entfernen möchte.</summary>
    public const string LegacyValueName = "QuickPasteBot";

    public static bool IsEnabled(string exePath) =>
        string.Equals(Unquote(Read(ValueName)), exePath, StringComparison.OrdinalIgnoreCase);

    public static void Enable(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{exePath}\"");
    }

    public static void Disable() => Delete(ValueName);

    public static string? LegacyEntry => Read(LegacyValueName);

    public static void RemoveLegacyEntry() => Delete(LegacyValueName);

    static string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) as string;
    }

    static void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    static string? Unquote(string? value) => value?.Trim().Trim('"');
}
