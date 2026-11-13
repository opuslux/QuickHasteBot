namespace QuickHasteBot.Core;

/// <summary>
/// Eine eigene Prompt-Vorlage neben der exe, z.B. ai-prompt.txt. Fehlt die Datei, gilt <paramref name="Default"/>;
/// "Vorlage bearbeiten" legt sie damit an.
/// </summary>
public sealed record PromptTemplate(string Path, string Default)
{
    public bool Exists => File.Exists(Path);

    public string FileName => System.IO.Path.GetFileName(Path);
}
