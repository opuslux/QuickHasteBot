using System.Collections.Concurrent;

namespace QuickHasteBot.Core;

/// <summary>
/// Liest den structure-Ordner als Baum ein und hält ihn per FileSystemWatcher aktuell. Was der Watcher verpasst,
/// holt <see cref="VerifyAsync"/> beim Öffnen des Popups nach.
/// <see cref="Changed"/> kommt auf einem Threadpool-Thread.
/// </summary>
public sealed class SnippetLibrary : IDisposable
{
    const int DebounceMilliseconds = 300;

    /// <summary>Größere Dateien werden nicht durchsucht (aber natürlich trotzdem eingefügt).</summary>
    const long MaxSearchableSize = 256 * 1024;

    static readonly HashSet<string> IgnoredFiles = new(StringComparer.OrdinalIgnoreCase) { "desktop.ini", "thumbs.db" };

    readonly HashSet<string> _textExtensions;
    readonly Func<string, HotkeyGesture?>? _hotkeyFor;
    readonly ConcurrentDictionary<string, (DateTime Stamp, string? Text)> _contentCache = new(StringComparer.OrdinalIgnoreCase);
    // Vom Watcher gemeldete Umbenennungen (relativ), angewendet beim nächsten Einlesen, siehe ApplyRenames.
    // Verschieben in einen anderen Ordner meldet Windows als gelöscht + neu, darum auch diese.
    readonly ConcurrentQueue<(string Old, string New)> _renames = new();
    readonly ConcurrentQueue<string> _deleted = new();
    readonly ConcurrentQueue<string> _created = new();
    readonly Lock _reloadGate = new();
    readonly Lock _watchGate = new();
    FileSystemWatcher? _watcher;
    Timer? _debounce;
    bool _watchRequested;
    bool _disposed;
    // Der Watcher hängt am Ordner, nicht am Pfad: so lässt sich erkennen, ob structure inzwischen ein anderer ist
    DateTime _watchedCreationUtc;

    /// <param name="hotkeyFor">Der Hotkey zu einem relativen Pfad, siehe <see cref="SnippetData.HotkeyFor"/>. Null: keine Hotkeys.</param>
    public SnippetLibrary(string rootPath, IEnumerable<string> textExtensions, Func<string, HotkeyGesture?>? hotkeyFor = null)
    {
        RootPath = rootPath;
        _textExtensions = new HashSet<string>(textExtensions.Select(NormalizeExtension), StringComparer.OrdinalIgnoreCase);
        _hotkeyFor = hotkeyFor;
        Root = Scan();
        All = Flatten(Root);
    }

    public event EventHandler? Changed;

    /// <summary>
    /// Im Explorer umbenannt oder innerhalb des structure-Ordners verschoben, solange das Programm läuft (alter und neuer
    /// Pfad, relativ). Kommt beim Einlesen vor dem neuen Baum, damit dort schon die umgezogenen Hotkeys stehen.
    /// </summary>
    public event Action<string, string>? Renamed;

    public string RootPath { get; }

    public Snippet Root { get; private set; }

    /// <summary>Alle Einträge ohne den Wurzelordner, in Baum-Reihenfolge.</summary>
    public IReadOnlyList<Snippet> All { get; private set; }

    /// <summary>Neu einlesen und immer melden, z.B. weil sich der Inhalt einer Datei geändert hat (Vorschau).</summary>
    public void Reload() => Reload(onlyIfChanged: false);

    /// <returns>Ob <see cref="Changed"/> ausgelöst wurde.</returns>
    bool Reload(bool onlyIfChanged)
    {
        lock (_reloadGate)
        {
            ApplyRenames();
            var root = Scan();
            var all = Flatten(root);
            if (onlyIfChanged && SameTree(All, all)) return false;

            // Baum und Liste zusammen tauschen, Leser sehen immer einen konsistenten Stand
            (Root, All) = (root, all);

            var alive = all.Select(s => s.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in _contentCache.Keys.Where(k => !alive.Contains(k)))
                _contentCache.TryRemove(path, out _);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Beim Hotkey: den Ordner neu einlesen, gemeldet wird nur ein geänderter Baum (sonst flackert das Popup).
    /// Fängt ab, was der Watcher nicht mitbekommt: structure im Explorer gelöscht, umbenannt oder durch einen
    /// anderen Ordner ersetzt (der Watcher beobachtet dann ins Leere), ein Netzlaufwerk ist wieder da, verlorene Events.
    /// </summary>
    public Task VerifyAsync() => Task.Run(Verify);

    /// <returns>Ob sich der Baum geändert hat.</returns>
    internal bool Verify()
    {
        lock (_watchGate)
        {
            if (_disposed) return false;
            if (_watchRequested && !WatcherFits())
            {
                Log.Info($"structure-Ordner wurde ersetzt oder war weg, beobachte neu: {RootPath}");
                StopWatcher();
                StartWatcher();
            }
        }

        return Reload(onlyIfChanged: true);
    }

    bool WatcherFits()
    {
        try
        {
            return _watcher is not null && Directory.Exists(RootPath) && Directory.GetCreationTimeUtc(RootPath) == _watchedCreationUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Meldet die gesammelten Umbenennungen, aber nur, wo der alte Pfad inzwischen fehlt und der neue da ist. Manche
    /// Editoren benennen beim Speichern das Original in ein Backup um und schreiben es dann neu: Dann ist der alte Pfad
    /// wieder da, und der Hotkey bleibt beim Original statt mit dem Backup zu wandern.
    /// </summary>
    void ApplyRenames()
    {
        var pending = new List<(string Old, string New)>();
        while (_renames.TryDequeue(out var rename))
            pending.Add(rename);
        pending.AddRange(Moves(Drain(_deleted), Drain(_created)));
        if (pending.Count == 0) return;

        for (var i = 0; i < pending.Count; i++)
        {
            var (oldPath, newPath) = pending[i];
            if (Exists(oldPath)) continue;
            // a → b → c: b gibt es nicht mehr, wurde aber gleich weiter umbenannt
            if (!Exists(newPath) && !pending.Skip(i + 1).Any(p => string.Equals(p.Old, newPath, StringComparison.OrdinalIgnoreCase))) continue;
            Renamed?.Invoke(oldPath, newPath);
        }

        bool Exists(string relative)
        {
            var full = Path.Combine(RootPath, relative);
            return File.Exists(full) || Directory.Exists(full);
        }
    }

    static List<string> Drain(ConcurrentQueue<string> queue)
    {
        var items = new List<string>();
        while (queue.TryDequeue(out var item))
            items.Add(item);
        return items;
    }

    /// <summary>
    /// Verschoben: hier gelöscht und in einem anderen Ordner gleichnamig neu. Nur eindeutige Paare, bei zwei gleichen
    /// Namen wäre es geraten. Ob es den alten Pfad wirklich nicht mehr gibt, prüft <see cref="ApplyRenames"/>.
    /// </summary>
    internal static IEnumerable<(string Old, string New)> Moves(IReadOnlyCollection<string> deleted, IReadOnlyCollection<string> created)
    {
        if (deleted.Count == 0 || created.Count == 0) yield break;

        var comparer = StringComparer.OrdinalIgnoreCase;
        foreach (var group in deleted.Distinct(comparer).GroupBy(Path.GetFileName, comparer))
        {
            if (group.Count() != 1) continue;
            var oldPath = group.Single();
            var targets = created.Distinct(comparer)
                .Where(c => comparer.Equals(Path.GetFileName(c), group.Key) && !comparer.Equals(Path.GetDirectoryName(c), Path.GetDirectoryName(oldPath)))
                .ToList();
            if (targets.Count == 1)
                yield return (oldPath, targets[0]);
        }
    }

    /// <summary>Der Pfad relativ zum structure-Ordner wie <see cref="Snippet.RelativePath"/>, null wenn er nicht darin liegt.</summary>
    public string? RelativeOf(string fullPath)
    {
        var root = Path.TrimEndingDirectorySeparator(RootPath);
        if (fullPath.Length <= root.Length + 1 || fullPath[root.Length] != Path.DirectorySeparatorChar
            || !fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return null;
        return Path.TrimEndingDirectorySeparator(fullPath[(root.Length + 1)..]);
    }

    static bool SameTree(IReadOnlyList<Snippet> before, List<Snippet> after) =>
        before.Count == after.Count &&
        before.Zip(after).All(p => p.First.Kind == p.Second.Kind && string.Equals(p.First.FullPath, p.Second.FullPath, StringComparison.Ordinal));

    public Snippet? Find(string relativePath) =>
        relativePath.Length == 0 ? Root : All.FirstOrDefault(s => string.Equals(s.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Der Textbaustein zu einem Namen in {include,…}: "Ordner/Name" wie im Popup, mit / oder \, Gross/Klein egal.
    /// Jeder Teil darf mit oder ohne Nummer davor ("3-Grüsse" oder "Grüsse") und die Datei mit oder ohne Endung stehen.
    /// Ein Name oder Pfad, den es oben nicht gibt, wird auch in Unterordnern gesucht ("Grüsse/Freundliche Grüsse" findet
    /// "Demo\1 - Grüsse\Freundliche Grüsse.txt"). Bei mehreren gilt der erste in der Liste.
    /// </summary>
    public Snippet? FindIncluded(string path)
    {
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return null;
        var all = All;
        return all.FirstOrDefault(s => s.Kind == SnippetKind.Text && PathMatches(s, segments, fromRoot: true))
            ?? all.FirstOrDefault(s => s.Kind == SnippetKind.Text && PathMatches(s, segments, fromRoot: false));

        static bool PathMatches(Snippet snippet, string[] segments, bool fromRoot)
        {
            var node = snippet;
            for (var i = segments.Length - 1; i >= 0; i--)
            {
                if (node is null || node.RelativePath.Length == 0 || !NameMatches(node, segments[i])) return false;
                node = node.Parent;
            }
            return !fromRoot || node is { RelativePath.Length: 0 };
        }

        static bool NameMatches(Snippet node, string segment) =>
            Same(node.Name, segment) || Same(WithoutNumber(node.Name), segment)
            || !node.IsFolder && (Same(node.Name + Path.GetExtension(node.FullPath), segment)
                                  || Same(WithoutNumber(node.Name) + Path.GetExtension(node.FullPath), segment));

        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // "3-Grüsse", "3 - Grüsse" → "Grüsse", "10 Notizen" → "Notizen"
        static string WithoutNumber(string name)
        {
            var i = 0;
            while (i < name.Length && char.IsAsciiDigit(name[i])) i++;
            if (i == 0 || i >= name.Length - 1 || name[i] is not ('-' or '_' or ' ' or '.')) return name;
            var rest = name[(i + 1)..].TrimStart();
            // "3 - Grüsse": nach dem Leerschlag noch der Strich
            if (name[i] == ' ' && rest.Length > 1 && rest[0] is '-' or '_' or '.') rest = rest[1..].TrimStart();
            return rest.Length > 0 ? rest : name;
        }
    }

    /// <summary>Liest den Text frisch von der Platte, wie die alte Version auch.</summary>
    public static string ReadText(Snippet snippet) => TextFileReader.Read(snippet.FullPath);

    /// <summary>Inhalt für die Suche, gecacht bis sich die Datei ändert. Null bei Nicht-Text oder zu großen Dateien.</summary>
    public string? GetSearchableText(Snippet snippet)
    {
        if (snippet.Kind != SnippetKind.Text) return null;

        try
        {
            var info = new FileInfo(snippet.FullPath);
            if (!info.Exists || info.Length > MaxSearchableSize) return null;

            var stamp = info.LastWriteTimeUtc;
            if (_contentCache.TryGetValue(snippet.FullPath, out var cached) && cached.Stamp == stamp)
                return cached.Text;

            var text = TextFileReader.Read(snippet.FullPath);
            _contentCache[snippet.FullPath] = (stamp, text);
            return text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Füllt den Such-Cache im Hintergrund, damit die erste Suche nicht auf die Platte wartet.</summary>
    public Task WarmUpAsync() => Task.Run(() =>
    {
        foreach (var snippet in All)
            GetSearchableText(snippet);
    });

    /// <summary>Gibt es den Ordner (noch) nicht, beginnt das Beobachten bei der nächsten <see cref="Verify"/>, sobald er da ist.</summary>
    public void StartWatching()
    {
        lock (_watchGate)
        {
            if (_disposed) return;
            _watchRequested = true;
            StartWatcher();
        }
    }

    void StartWatcher()
    {
        if (_watcher is not null || !Directory.Exists(RootPath)) return;

        try
        {
            _debounce ??= new Timer(_ => Reload());
            _watchedCreationUtc = Directory.GetCreationTimeUtc(RootPath);
            var watcher = new FileSystemWatcher(RootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Created += (_, e) => Remember(_created, e.FullPath);
            watcher.Deleted += (_, e) => Remember(_deleted, e.FullPath);
            watcher.Renamed += OnRenamed;
            watcher.Changed += OnFileSystemChanged;
            watcher.Error += (_, e) =>
            {
                Log.Error("FileSystemWatcher-Fehler, lade neu", e.GetException());
                ScheduleReload();
            };
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Z.B. gerade wieder gelöscht: beim nächsten Hotkey noch einmal
            Log.Error($"structure-Ordner kann nicht beobachtet werden: {RootPath}", ex);
        }
    }

    void StopWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    void OnFileSystemChanged(object sender, FileSystemEventArgs e) => ScheduleReload();

    void Remember(ConcurrentQueue<string> queue, string fullPath)
    {
        if (RelativeOf(fullPath) is { } relative)
            queue.Enqueue(relative);
        ScheduleReload();
    }

    void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (RelativeOf(e.OldFullPath) is { } oldPath && RelativeOf(e.FullPath) is { } newPath)
            _renames.Enqueue((oldPath, newPath));
        ScheduleReload();
    }

    // Ein Speichern im Editor löst mehrere Events aus, darum gesammelt neu laden
    void ScheduleReload()
    {
        try
        {
            _debounce?.Change(DebounceMilliseconds, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // Event kam noch während Dispose
        }
    }

    public void Dispose()
    {
        lock (_watchGate)
        {
            _disposed = true;
            StopWatcher();
            _debounce?.Dispose();
            _debounce = null;
        }
    }

    Snippet Scan()
    {
        var root = new Snippet { Name = Path.GetFileName(RootPath), FullPath = RootPath, RelativePath = "", Kind = SnippetKind.Folder };
        if (Directory.Exists(RootPath))
            ScanInto(new DirectoryInfo(RootPath), root);
        return root;
    }

    void ScanInto(DirectoryInfo directory, Snippet parent)
    {
        DirectoryInfo[] subDirectories;
        FileInfo[] files;
        try
        {
            subDirectories = directory.GetDirectories();
            files = directory.GetFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Ordner konnte nicht gelesen werden: {directory.FullName}", ex);
            return;
        }

        // Ordinal sortieren: die Nummern-Präfixe ("1-Temp", "2-Adressen") bestimmen die Reihenfolge
        foreach (var sub in subDirectories.Where(IsVisible).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Relative(parent, sub.Name);
            var folder = new Snippet
            {
                Name = sub.Name,
                FullPath = sub.FullName,
                RelativePath = relative,
                Kind = SnippetKind.Folder,
                Hotkey = _hotkeyFor?.Invoke(relative),
                Parent = parent,
            };
            parent.Children.Add(folder);
            ScanInto(sub, folder);
        }

        foreach (var file in files.Where(f => IsVisible(f) && !IgnoredFiles.Contains(f.Name)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Relative(parent, file.Name);
            parent.Children.Add(new Snippet
            {
                Name = Path.GetFileNameWithoutExtension(file.Name),
                FullPath = file.FullName,
                RelativePath = relative,
                Kind = _textExtensions.Contains(file.Extension) ? SnippetKind.Text : SnippetKind.File,
                Hotkey = _hotkeyFor?.Invoke(relative),
                Parent = parent,
            });
        }
    }

    // Aus dem Elternpfad statt mit Path.GetRelativePath: das fragt bei Kurzpfaden ("C:\Users\MAXMUS~1\…")
    // für jeden Eintrag das Dateisystem nach dem langen Namen, bei 1000 Einträgen 30-mal langsamer
    static string Relative(Snippet parent, string name) =>
        parent.RelativePath.Length == 0 ? name : parent.RelativePath + Path.DirectorySeparatorChar + name;

    static bool IsVisible(FileSystemInfo info) => (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

    static List<Snippet> Flatten(Snippet root)
    {
        var result = new List<Snippet>();
        void Walk(Snippet node)
        {
            foreach (var child in node.Children)
            {
                result.Add(child);
                Walk(child);
            }
        }
        Walk(root);
        return result;
    }

    static string NormalizeExtension(string extension) => extension.StartsWith('.') ? extension : "." + extension;
}
