namespace QuickHasteBot.Core;

public enum FileHotkeyStatus
{
    /// <summary>Registriert, der Hotkey wirkt.</summary>
    Ok,

    /// <summary>Eine Datei oder ein Ordner weiter oben im Baum hat denselben Hotkey, siehe <see cref="FileHotkeyEntry.Owner"/>.</summary>
    Duplicate,

    /// <summary>Derselbe Hotkey öffnet das Popup.</summary>
    MainHotkey,

    /// <summary>Ein anderes Programm hat die Kombination schon registriert.</summary>
    Taken,
}

/// <param name="Owner">Bei <see cref="FileHotkeyStatus.Duplicate"/> der Eintrag, der den Hotkey bekommen hat.</param>
public sealed record FileHotkeyEntry(Snippet Snippet, HotkeyGesture Gesture, FileHotkeyStatus Status, Snippet? Owner = null);

/// <summary>Welche Einträge welchen Hotkey haben, und wo es sich beisst.</summary>
public static class FileHotkeyMap
{
    /// <summary>
    /// Alle Einträge mit Hotkey, in Baum-Reihenfolge. Bei doppelten gewinnt der erste: Er steht weiter oben im Popup,
    /// und die Reihenfolge bleibt über einen Neustart gleich. <see cref="FileHotkeyStatus.Taken"/> kann erst das
    /// Registrieren feststellen, siehe AppController.
    /// </summary>
    public static List<FileHotkeyEntry> Build(IEnumerable<Snippet> all, HotkeyGesture mainHotkey)
    {
        var result = new List<FileHotkeyEntry>();
        var owners = new Dictionary<HotkeyGesture, Snippet>();
        foreach (var snippet in all)
        {
            if (snippet.Hotkey is not { } gesture) continue;

            if (gesture == mainHotkey)
                result.Add(new(snippet, gesture, FileHotkeyStatus.MainHotkey));
            else if (owners.TryGetValue(gesture, out var owner))
                result.Add(new(snippet, gesture, FileHotkeyStatus.Duplicate, owner));
            else
            {
                owners[gesture] = snippet;
                result.Add(new(snippet, gesture, FileHotkeyStatus.Ok));
            }
        }
        return result;
    }

    /// <summary>Wer den Hotkey schon hat, ausser dem Eintrag selbst (beim Umbenennen oder Ändern).</summary>
    /// <param name="exceptPath">Vollständiger Pfad des Eintrags, der den Hotkey bekommen soll.</param>
    public static Snippet? FindOwner(IEnumerable<Snippet> all, HotkeyGesture gesture, string? exceptPath) =>
        all.FirstOrDefault(s => s.Hotkey == gesture && !string.Equals(s.FullPath, exceptPath, StringComparison.OrdinalIgnoreCase));
}
