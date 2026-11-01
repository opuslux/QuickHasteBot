using System.Collections.Concurrent;

namespace QuickHasteBot.Core;

/// <summary>
/// Liest den structure-Ordner als Baum ein und hält ihn per FileSystemWatcher aktuell.
/// <see cref="Changed"/> kommt auf einem Threadpool-Thread.
/// </summary>
public sealed class SnippetLibrary : IDisposable
{
    const int DebounceMilliseconds = 300;

    /// <summary>Größere Dateien werden nicht durchsucht (aber natürlich trotzdem eingefügt).</summary>
    const long MaxSearchableSize = 256 * 1024;

    static readonly HashSet<string> IgnoredFiles = new(StringComparer.OrdinalIgnoreCase) { "desktop.ini", "thumbs.db" };

    readonly HashSet<string> _textExtensions;
    readonly ConcurrentDictionary<string, (DateTime Stamp, string? Text)> _contentCache = new(StringComparer.OrdinalIgnoreCase);
    readonly Lock _reloadGate = new();
    FileSystemWatcher? _watcher;
    Timer? _debounce;

    public SnippetLibrary(string rootPath, IEnumerable<string> textExtensions)
    {
        RootPath = rootPath;
        _textExtensions = new HashSet<string>(textExtensions.Select(NormalizeExtension), StringComparer.OrdinalIgnoreCase);
        Root = Scan();
        All = Flatten(Root);
    }

    public event EventHandler? Changed;

    public string RootPath { get; }

    public Snippet Root { get; private set; }

    /// <summary>Alle Einträge ohne den Wurzelordner, in Baum-Reihenfolge.</summary>
    public IReadOnlyList<Snippet> All { get; private set; }

    public void Reload()
    {
        lock (_reloadGate)
        {
            var root = Scan();
            var all = Flatten(root);

            // Baum und Liste zusammen tauschen, Leser sehen immer einen konsistenten Stand
            (Root, All) = (root, all);

            var alive = all.Select(s => s.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in _contentCache.Keys.Where(k => !alive.Contains(k)))
                _contentCache.TryRemove(path, out _);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Snippet? Find(string relativePath) =>
        relativePath.Length == 0 ? Root : All.FirstOrDefault(s => string.Equals(s.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

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

    public void StartWatching()
    {
        if (_watcher is not null || !Directory.Exists(RootPath)) return;

        _debounce = new Timer(_ => Reload());
        _watcher = new FileSystemWatcher(RootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += OnFileSystemChanged;
        _watcher.Deleted += OnFileSystemChanged;
        _watcher.Renamed += OnFileSystemChanged;
        _watcher.Changed += OnFileSystemChanged;
        _watcher.Error += (_, e) =>
        {
            Log.Error("FileSystemWatcher-Fehler, lade neu", e.GetException());
            ScheduleReload();
        };
        _watcher.EnableRaisingEvents = true;
    }

    void OnFileSystemChanged(object sender, FileSystemEventArgs e) => ScheduleReload();

    // Ein Speichern im Editor löst mehrere Events aus, darum gesammelt neu laden
    void ScheduleReload() => _debounce?.Change(DebounceMilliseconds, Timeout.Infinite);

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
        _watcher = null;
        _debounce = null;
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
            var folder = new Snippet
            {
                Name = sub.Name,
                FullPath = sub.FullName,
                RelativePath = Path.GetRelativePath(RootPath, sub.FullName),
                Kind = SnippetKind.Folder,
                Parent = parent,
            };
            parent.Children.Add(folder);
            ScanInto(sub, folder);
        }

        foreach (var file in files.Where(f => IsVisible(f) && !IgnoredFiles.Contains(f.Name)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            parent.Children.Add(new Snippet
            {
                Name = Path.GetFileNameWithoutExtension(file.Name),
                FullPath = file.FullName,
                RelativePath = Path.GetRelativePath(RootPath, file.FullName),
                Kind = _textExtensions.Contains(file.Extension) ? SnippetKind.Text : SnippetKind.File,
                Parent = parent,
            });
        }
    }

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
