using System.Windows;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using QuickHasteBot.UI;

namespace QuickHasteBot;

/// <summary>
/// Die eigenen KI-Vorlagen (ai-prompt.txt, hastescript-prompt.txt): lesen, beim ersten Bearbeiten mit dem eingebauten
/// Prompt anlegen und zurücksetzen. Fehler landen im Log und, wo der User etwas tat, in einer Meldung.
/// </summary>
internal static class PromptTemplates
{
    /// <summary>Inhalt der Vorlage, null ohne Datei (dann gilt der eingebaute Prompt).</summary>
    internal static string? Read(PromptTemplate template)
    {
        if (!template.Exists) return null;
        try
        {
            return TextFileReader.Read(template.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"KI-Vorlage konnte nicht gelesen werden: {template.Path}", ex);
            return null;
        }
    }

    /// <summary>Legt die Vorlage beim ersten Mal mit dem eingebauten Prompt an und öffnet sie im Editor.</summary>
    internal static void Edit(PromptTemplate template)
    {
        try
        {
            Create(template);
            Shell.Open(template.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"KI-Vorlage konnte nicht angelegt werden: {template.Path}", ex);
            MessageDialog.Show(string.Format(Strings.AiPromptTemplateErrorFormat, template.FileName, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Zurück zum eingebauten Prompt: Die Vorlage kommt in den Papierkorb, eigene Änderungen bleiben so wiederherstellbar.</summary>
    internal static void Reset(PromptTemplate template, nint owner)
    {
        if (!template.Exists) return;
        try
        {
            RecycleBin.Delete(template.Path, owner);
        }
        catch (IOException ex)
        {
            Log.Error($"KI-Vorlage konnte nicht gelöscht werden: {template.Path}", ex);
            MessageDialog.Show(string.Format(Strings.AiPromptResetErrorFormat, template.FileName, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <returns>Pfad der Vorlage. Eine vorhandene bleibt, wie sie ist.</returns>
    internal static string Create(PromptTemplate template)
    {
        if (!template.Exists)
            File.WriteAllText(template.Path, template.Default);
        return template.Path;
    }
}
