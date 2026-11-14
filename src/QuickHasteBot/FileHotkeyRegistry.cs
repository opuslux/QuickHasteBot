using System.Windows;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using QuickHasteBot.UI;

namespace QuickHasteBot;

/// <summary>
/// Die Hotkeys aus Datei- und Ordnernamen (z.B. „Gruss [Ctrl+Alt+G].txt“): registriert sie beim globalen Hotkey, merkt sich
/// Konflikte und beantwortet, ob ein Hotkey noch frei ist. Ohne <see cref="Attach"/> (vor dem Start, in Tests) gibt es nur
/// die Liste.
/// </summary>
/// <param name="library">Die aktuelle Bibliothek, sie wird beim Wechsel des structure-Ordners ersetzt.</param>
/// <param name="mainHotkey">Der Hotkey des Popups, den keine Datei bekommen darf.</param>
internal sealed class FileHotkeyRegistry(Func<SnippetLibrary> library, Func<HotkeyGesture> mainHotkey)
{
    // Was registriert ist, nach vollständigem Pfad, und was beim letzten Abgleich herauskam
    readonly Dictionary<string, (int Id, HotkeyGesture Gesture)> _registrations = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, FileHotkeyEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    HashSet<string> _knownConflicts = [];
    bool _suspended;
    GlobalHotkey? _hotkey;

    /// <summary>Alle Einträge mit Hotkey aus dem letzten Abgleich, in Baum-Reihenfolge.</summary>
    public IReadOnlyList<FileHotkeyEntry> Entries { get; private set; } = [];

    /// <summary>Nach jedem Abgleich, für die Liste in den Einstellungen.</summary>
    public event EventHandler? Changed;

    /// <summary>Ab jetzt registrieren (beim Start), null beim Beenden.</summary>
    internal void Attach(GlobalHotkey? hotkey) => _hotkey = hotkey;

    /// <summary>Solange eine Tastenkombination aufgenommen wird: alle frei geben, bis <see cref="Resume"/>.</summary>
    internal void Suspend()
    {
        _suspended = true;
        foreach (var (id, _) in _registrations.Values)
            _hotkey?.Unregister(id);
        _registrations.Clear();
    }

    internal void Resume()
    {
        _suspended = false;
        Sync();
    }

    /// <summary>Der Eintrag zu einem ausgelösten Hotkey, null wenn er nicht (mehr) registriert ist.</summary>
    internal string? PathFor(int id) => _registrations.FirstOrDefault(r => r.Value.Id == id).Key;

    /// <summary>
    /// Registriert die Hotkeys aus den Dateinamen neu: nach dem Einlesen, nach einer Änderung im Ordner und wenn sich
    /// der Popup-Hotkey ändert. Nur was sich geändert hat, wird frei gegeben oder registriert.
    /// </summary>
    /// <param name="announce">Neu entstandene Konflikte melden, z.B. nach Umbenennen im Explorer.</param>
    internal void Sync(bool announce = true)
    {
        var entries = FileHotkeyMap.Build(library().All, mainHotkey());
        // Ohne Hotkey-Fenster (vor Start, in Tests) nur die Liste
        if (_hotkey is not null && !_suspended)
        {
            var wanted = entries.Where(e => e.Status == FileHotkeyStatus.Ok)
                .ToDictionary(e => e.Snippet.FullPath, e => e.Gesture, StringComparer.OrdinalIgnoreCase);

            // Erst frei geben, damit ein Hotkey von einer Datei zur nächsten wandern kann
            foreach (var (path, registration) in _registrations.ToList())
            {
                if (wanted.TryGetValue(path, out var gesture) && gesture == registration.Gesture) continue;
                _hotkey.Unregister(registration.Id);
                _registrations.Remove(path);
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.Status != FileHotkeyStatus.Ok || _registrations.ContainsKey(entry.Snippet.FullPath)) continue;

                var id = FreeId();
                if (_hotkey.Register(id, entry.Gesture))
                    _registrations[entry.Snippet.FullPath] = (id, entry.Gesture);
                else
                    entries[i] = entry with { Status = FileHotkeyStatus.Taken };
            }
        }

        Entries = entries;
        _byPath = entries.ToDictionary(e => e.Snippet.FullPath, StringComparer.OrdinalIgnoreCase);

        // Nur Konflikte melden, die es vorher nicht gab: sonst kommt bei jeder Änderung im Ordner dieselbe Meldung
        var conflicts = entries.Where(e => e.Status != FileHotkeyStatus.Ok).ToList();
        var fresh = conflicts.Where(e => !_knownConflicts.Contains(ConflictKey(e))).ToList();
        _knownConflicts = conflicts.Select(ConflictKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Changed?.Invoke(this, EventArgs.Empty);

        foreach (var entry in fresh)
            Log.Info($"Datei-Hotkey {entry.Gesture} nicht aktiv ({entry.Status}): {entry.Snippet.FullPath}");
        if (announce && fresh.Count > 0)
            MessageDialog.Show(Strings.FileHotkeyConflictsFound + "\n\n" + string.Join("\n", fresh.Select(e => "• " + DescribeConflict(e))),
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    static string ConflictKey(FileHotkeyEntry entry) => $"{entry.Snippet.FullPath}|{entry.Gesture}|{entry.Status}";

    int FreeId()
    {
        var used = _registrations.Values.Select(r => r.Id).ToHashSet();
        var id = GlobalHotkey.FirstFileId;
        while (used.Contains(id)) id++;
        return id;
    }

    /// <summary>"Ctrl+Alt+G für «Gruss»: Schon vergeben an «Grüsse» (1-Grüsse)."</summary>
    internal static string DescribeConflict(FileHotkeyEntry entry) =>
        string.Format(Strings.FileHotkeyConflictLineFormat, entry.Gesture, DescribeSnippet(entry.Snippet), ConflictReason(entry));

    /// <summary>Warum der Hotkey nicht wirkt, null wenn er wirkt.</summary>
    internal static string? ConflictReason(FileHotkeyEntry entry) => entry.Status switch
    {
        FileHotkeyStatus.Duplicate => string.Format(Strings.FileHotkeyUsedByFormat, DescribeSnippet(entry.Owner!)),
        FileHotkeyStatus.MainHotkey => Strings.FileHotkeyIsMain,
        FileHotkeyStatus.Taken => Strings.FileHotkeyTaken,
        _ => null,
    };

    /// <summary>Für das Warnzeichen im Popup: warum der Hotkey dieses Eintrags nicht wirkt, null ohne Problem.</summary>
    internal string? Problem(Snippet snippet) =>
        snippet.Hotkey is not null && _byPath.TryGetValue(snippet.FullPath, out var entry) ? ConflictReason(entry) : null;

    static string DescribeSnippet(Snippet snippet) => snippet.Location.Length == 0
        ? string.Format(Strings.QuotedNameFormat, snippet.Name)
        : string.Format(Strings.QuotedNameWithLocationFormat, snippet.Name, snippet.Location);

    /// <summary>
    /// Ob ein Eintrag diesen Hotkey bekommen darf, für den Hotkey-Dialog und beim Umbenennen. Null wenn ja,
    /// sonst der Grund.
    /// </summary>
    /// <param name="selfPath">Der Eintrag selbst, sein bisheriger Hotkey ist natürlich kein Konflikt.</param>
    internal string? Conflict(HotkeyGesture gesture, string? selfPath)
    {
        if (gesture == mainHotkey()) return Strings.FileHotkeyIsMain;
        if (FileHotkeyMap.FindOwner(library().All, gesture, selfPath) is { } owner)
            return string.Format(Strings.FileHotkeyUsedByFormat, DescribeSnippet(owner));
        // Was wir selbst registriert haben, hält kein anderes Programm
        if (_registrations.Values.Any(r => r.Gesture == gesture)) return null;
        return _hotkey is not null && !_hotkey.IsFree(gesture) ? Strings.FileHotkeyTaken : null;
    }

    /// <summary>
    /// Beim Umbenennen: ob der Hotkey im neuen Namen frei ist. Null ohne Tag, mit dem bisherigen Hotkey oder einem freien.
    /// </summary>
    /// <param name="selfPath">Bisheriger Pfad des Eintrags, null bei einem neuen.</param>
    internal string? ConflictForName(string fileName, bool isFolder, string? selfPath, HotkeyGesture? currentHotkey)
    {
        if (FileHotkey.FromFileName(fileName, isFolder) is not { } gesture || gesture == currentHotkey) return null;
        return Conflict(gesture, selfPath);
    }
}
