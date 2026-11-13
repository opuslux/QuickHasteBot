namespace QuickHasteBot.Core;

/// <summary>
/// Portable Ablage: Einstellungen, Log und Textbausteine liegen neben der exe.
/// </summary>
public sealed class AppPaths(string baseDirectory)
{
    public static AppPaths ForCurrentProcess() => new(AppContext.BaseDirectory);

    public string BaseDirectory { get; } = baseDirectory;

    public string SettingsFile => Path.Combine(BaseDirectory, "settings.json");

    /// <summary>settings.xml der alten QuickPasteBot-Version, wird beim ersten Start übernommen.</summary>
    public string LegacySettingsFile => Path.Combine(BaseDirectory, "settings.xml");

    public string LogFile => Path.Combine(BaseDirectory, "QuickHasteBot.log");

    /// <summary>Eigene Vorlage für den KI-Prompt mit {task}. Fehlt sie, gilt der eingebaute Prompt.</summary>
    public string AiPromptFile => Path.Combine(BaseDirectory, "ai-prompt.txt");

    /// <summary>Eigene Vorlage für den Prompt von "HasteScript AI". Fehlt sie, gilt der eingebaute Prompt.</summary>
    public string HasteScriptPromptFile => Path.Combine(BaseDirectory, "hastescript-prompt.txt");

    /// <summary>Relative Pfade gelten ab dem exe-Ordner, Umgebungsvariablen werden aufgelöst.</summary>
    public string ResolveStructure(string configured)
    {
        var expanded = Environment.ExpandEnvironmentVariables(string.IsNullOrWhiteSpace(configured) ? AppSettings.DefaultStructurePath : configured);
        return Path.GetFullPath(Path.Combine(BaseDirectory, expanded));
    }
}
