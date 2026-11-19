using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickHasteBot.Core;

/// <summary>Woher eine Verwendung kam, für die Statistik.</summary>
public enum UseSource
{
    /// <summary>Aus den Suchergebnissen gewählt.</summary>
    Search,

    /// <summary>Beim Durchblättern der Ordner gewählt.</summary>
    Browse,

    /// <summary>Über den Hotkey der Datei oder des Ordners.</summary>
    Hotkey,
}

/// <summary>Was zu einem Eintrag gespeichert ist, eine Zeile in snippets.json.</summary>
public sealed class SnippetRecord
{
    /// <summary>Der globale Hotkey, z.B. "Ctrl+Alt+G".</summary>
    public string? Hotkey { get; set; }

    public int Search { get; set; }

    public int Browse { get; set; }

    public int HotkeyUses { get; set; }

    public DateTime? First { get; set; }

    public DateTime? Last { get; set; }

    /// <summary>Die letzten Verwendungen, neueste am Ende, für die Reihenfolge in der Suche.</summary>
    public List<DateTime>? Recent { get; set; }

    [JsonIgnore]
    public int Uses => Search + Browse + HotkeyUses;

    [JsonIgnore]
    internal bool IsEmpty => Hotkey is null && Uses == 0;
}

/// <summary>Wie oft ein Eintrag nach einem bestimmten Suchtext gewählt wurde.</summary>
public sealed class QueryPick
{
    public int Count { get; set; }

    /// <summary>Die letzten Male, neuestes am Ende.</summary>
    public List<DateTime> Recent { get; set; } = [];
}

/// <summary>
/// Was QuickHasteBot zu den Einträgen im structure-Ordner speichert: Hotkeys, wie oft und wann etwas benutzt wurde und
/// was nach welchem Suchtext gewählt wurde. Liegt als snippets.json neben settings.json, gehört also dem Benutzer und
/// nicht dem (vielleicht geteilten oder synchronisierten) structure-Ordner. Schlüssel ist der Pfad relativ zum
/// structure-Ordner wie <see cref="Snippet.RelativePath"/>, Gross/Klein egal.
/// <para>
/// Thread-sicher: Der Watcher meldet Umbenennungen auf dem Threadpool, eingelesen wird auch dort. Gespeichert wird
/// gesammelt einige Sekunden nach der letzten Änderung im Hintergrund, Hotkeys sofort, der Rest spätestens bei
/// <see cref="Dispose"/>.
/// </para>
/// </summary>
public sealed class SnippetData : IDisposable
{
    const int MaxRecent = 10;
    const int MaxQueryRecent = 5;
    const int MaxQueries = 300;
    const int SaveDelayMilliseconds = 2000;

    /// <summary>Nutzung von Einträgen, die es nicht mehr gibt, fliegt nach dieser Zeit raus. Hotkeys bleiben.</summary>
    static readonly TimeSpan PruneAge = TimeSpan.FromDays(180);

    static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        // Auch von Hand lesbar: "Grüsse" statt "Grüsse"
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    readonly Func<DateTime> _clock;
    readonly Lock _gate = new();
    // Hält Serialisieren und Schreiben zusammen: sonst könnte ein älterer Stand einen neueren überschreiben
    readonly Lock _writeGate = new();
    DateTime? _since;
    Dictionary<string, SnippetRecord> _entries = new(PathComparer);
    // Normalisierter Suchtext → Eintrag → wie oft gewählt
    Dictionary<string, Dictionary<string, QueryPick>> _queries = new(StringComparer.Ordinal);
    // Allgemeiner Bonus je Eintrag, wird bei jeder Änderung neu gebaut (nie verändert, Suchen halten ihn fest)
    Dictionary<string, int>? _usageBonus;
    DateTime _usageBonusDay;
    Timer? _saveTimer;
    bool _dirty;
    bool _disposed;

    SnippetData(string path, Func<DateTime> clock)
    {
        FilePath = path;
        _clock = clock;
    }

    public string FilePath { get; }

    /// <summary>Seit wann gezählt wird: erster Start mit dieser Funktion oder "Verlauf löschen".</summary>
    public DateTime Since
    {
        get
        {
            lock (_gate)
                return _since ?? Now;
        }
    }

    /// <summary>Fehlt die Datei, ist alles leer. Eine kaputte Datei wird als .bad daneben gesichert, statt sie zu überschreiben.</summary>
    /// <param name="clock">Für Tests, sonst die aktuelle Zeit in UTC.</param>
    public static SnippetData Load(string path, Func<DateTime>? clock = null)
    {
        var data = new SnippetData(path, clock ?? (() => DateTime.UtcNow));
        data.Read();
        return data;
    }

    DateTime Now
    {
        get
        {
            // Auf Sekunden: kürzer in der Datei, genauer braucht es niemand
            var now = _clock();
            return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        }
    }

    void Read()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                if (JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath), Options) is { } model)
                    Adopt(model);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Log.Error($"snippets.json konnte nicht gelesen werden: {FilePath}", ex);
                Backup();
            }
        }

        if (_since is null)
        {
            // Erst beim nächsten Speichern in die Datei, eine leere muss nicht entstehen
            _since = Now;
            _dirty = true;
        }
    }

    void Backup()
    {
        try
        {
            File.Copy(FilePath, FilePath + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Sicherung von {FilePath} fehlgeschlagen", ex);
        }
    }

    // Der Deserializer kennt die Vergleiche nicht: Gross/Klein bei Pfaden egal, Suchtexte sind schon normalisiert
    void Adopt(Model model)
    {
        _since = model.Since;
        foreach (var (path, record) in model.Entries ?? Enumerable.Empty<KeyValuePair<string, SnippetRecord>>())
        {
            if (record is null || path.Length == 0) continue;
            _entries[path] = record;
        }

        foreach (var (query, picks) in model.Queries ?? Enumerable.Empty<KeyValuePair<string, Dictionary<string, QueryPick>>>())
        {
            if (picks is null) continue;
            var normalized = UsageRanking.NormalizeQuery(query);
            if (normalized.Length == 0) continue;
            var target = _queries.TryGetValue(normalized, out var existing) ? existing : _queries[normalized] = new(PathComparer);
            foreach (var (path, pick) in picks)
            {
                if (pick is not null && path.Length > 0)
                {
                    pick.Recent ??= [];
                    target[path] = pick;
                }
            }
        }
    }

    // --- Hotkeys ---

    /// <summary>Der Hotkey eines Eintrags, null ohne oder mit einem nicht lesbaren (von Hand verschrieben).</summary>
    public HotkeyGesture? HotkeyFor(string relativePath)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(relativePath, out var record) && record.Hotkey is { } text
                   && HotkeyGesture.TryParse(text, out var gesture)
                ? gesture
                : null;
        }
    }

    /// <summary>Alle gespeicherten Hotkeys, auch die von Einträgen, die es gerade nicht gibt.</summary>
    public IReadOnlyList<(string RelativePath, HotkeyGesture Gesture)> Hotkeys()
    {
        lock (_gate)
        {
            var result = new List<(string, HotkeyGesture)>();
            foreach (var (path, record) in _entries)
            {
                if (record.Hotkey is { } text && HotkeyGesture.TryParse(text, out var gesture))
                    result.Add((path, gesture));
            }
            return result;
        }
    }

    /// <summary>Setzt oder entfernt (null) den Hotkey und speichert sofort.</summary>
    public void SetHotkey(string relativePath, HotkeyGesture? gesture)
    {
        lock (_gate)
        {
            if (gesture is { } g)
                GetOrAdd(relativePath).Hotkey = g.ToString();
            else if (_entries.TryGetValue(relativePath, out var record))
            {
                record.Hotkey = null;
                if (record.IsEmpty)
                    _entries.Remove(relativePath);
            }
            _dirty = true;
        }
        Flush();
    }

    // --- Nutzung ---

    /// <summary>Eine Verwendung: zählt für die Statistik und die Reihenfolge, mit Suchtext auch für diesen.</summary>
    /// <param name="query">Der Suchtext, aus dem gewählt wurde, null ohne Suche.</param>
    public void RecordUse(string relativePath, UseSource source, string? query = null)
    {
        lock (_gate)
        {
            var now = Now;
            var record = GetOrAdd(relativePath);
            switch (source)
            {
                case UseSource.Search:
                    record.Search++;
                    break;
                case UseSource.Browse:
                    record.Browse++;
                    break;
                default:
                    record.HotkeyUses++;
                    break;
            }
            record.First ??= now;
            record.Last = now;
            AddRecent(record.Recent ??= [], now, MaxRecent);

            var normalized = query is null ? "" : UsageRanking.NormalizeQuery(query);
            if (normalized.Length > 0)
            {
                if (!_queries.TryGetValue(normalized, out var picks))
                    _queries[normalized] = picks = new(PathComparer);
                if (!picks.TryGetValue(relativePath, out var pick))
                    picks[relativePath] = pick = new QueryPick();
                pick.Count++;
                AddRecent(pick.Recent, now, MaxQueryRecent);
                TrimQueries();
            }

            Changed();
        }
    }

    static void AddRecent(List<DateTime> recent, DateTime now, int max)
    {
        recent.Add(now);
        if (recent.Count > max)
            recent.RemoveRange(0, recent.Count - max);
    }

    // Nur die zuletzt benutzten Suchtexte behalten, sonst wächst die Datei mit jedem Tippfehler
    void TrimQueries()
    {
        if (_queries.Count <= MaxQueries) return;
        foreach (var stale in _queries.OrderBy(q => LastPick(q.Value)).Take(_queries.Count - MaxQueries).Select(q => q.Key).ToList())
            _queries.Remove(stale);
    }

    static DateTime LastPick(Dictionary<string, QueryPick> picks) =>
        picks.Values.Select(p => p.Recent.Count > 0 ? p.Recent[^1] : DateTime.MinValue).DefaultIfEmpty(DateTime.MinValue).Max();

    /// <summary>
    /// Für die Suche: der Bonus je Treffer zu diesem Suchtext, null wenn es nichts gibt (dann sucht die Suche wie ohne).
    /// Was teuer ist, passiert hier einmal pro Suche, die zurückgegebene Funktion ist nur noch ein Nachschlagen.
    /// </summary>
    public Func<Snippet, int>? BonusFor(string query)
    {
        lock (_gate)
        {
            var now = Now;
            var usage = UsageBonuses(now);
            var byQuery = QueryBonuses(UsageRanking.NormalizeQuery(query), now);
            if (usage.Count == 0 && byQuery is null) return null;
            if (byQuery is null) return snippet => usage.GetValueOrDefault(snippet.RelativePath);
            return snippet => usage.GetValueOrDefault(snippet.RelativePath) + byQuery.GetValueOrDefault(snippet.RelativePath);
        }
    }

    // Gilt einen Tag lang, die Altersstufen sind in Tagen
    Dictionary<string, int> UsageBonuses(DateTime now)
    {
        if (_usageBonus is not null && _usageBonusDay == now.Date) return _usageBonus;

        var bonuses = new Dictionary<string, int>(PathComparer);
        foreach (var (path, record) in _entries)
        {
            if (record.Recent is not { Count: > 0 } recent) continue;
            var bonus = UsageRanking.UsageBonus(UsageRanking.Points(recent, now));
            if (bonus > 0)
                bonuses[path] = bonus;
        }
        (_usageBonus, _usageBonusDay) = (bonuses, now.Date);
        return bonuses;
    }

    Dictionary<string, int>? QueryBonuses(string query, DateTime now)
    {
        if (query.Length == 0 || _queries.Count == 0) return null;

        Dictionary<string, double>? points = null;
        foreach (var (stored, picks) in _queries)
        {
            var weight = UsageRanking.QueryWeight(stored, query);
            if (weight == 0) continue;
            points ??= new(PathComparer);
            foreach (var (path, pick) in picks)
                points[path] = points.GetValueOrDefault(path) + weight * UsageRanking.Points(pick.Recent, now);
        }
        return points?.ToDictionary(p => p.Key, p => UsageRanking.QueryBonus(p.Value), PathComparer);
    }

    /// <summary>"Verlauf löschen": alle Zähler und gelernten Suchtexte weg, die Hotkeys bleiben. Zählt ab jetzt neu.</summary>
    public void ClearUsage()
    {
        lock (_gate)
        {
            foreach (var (path, record) in _entries.ToList())
            {
                if (record.Hotkey is null)
                {
                    _entries.Remove(path);
                    continue;
                }
                record.Search = record.Browse = record.HotkeyUses = 0;
                (record.First, record.Last, record.Recent) = (null, null, null);
            }
            _queries.Clear();
            _since = Now;
            _usageBonus = null;
            _dirty = true;
        }
        Flush();
    }

    // --- Umbenennen, Löschen, Aufräumen ---

    /// <summary>
    /// Eine Datei oder ein Ordner hat einen neuen Pfad: Hotkey und Nutzung wandern mit, bei Ordnern alles darin.
    /// Ohne Daten am alten Pfad passiert nichts.
    /// </summary>
    /// <returns>Ob etwas umgezogen ist.</returns>
    public bool Rename(string oldPath, string newPath)
    {
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) return false;

        lock (_gate)
        {
            var moved = Move(_entries, oldPath, newPath);
            foreach (var picks in _queries.Values)
                moved |= Move(picks, oldPath, newPath);
            if (moved)
                Changed();
            return moved;
        }
    }

    static bool Move<T>(Dictionary<string, T> map, string oldPath, string newPath)
    {
        var keys = map.Keys.Where(k => IsSameOrInside(k, oldPath)).ToList();
        // Erst alle heraus, dann hinein: bei "gruss.txt" → "Gruss.txt" ist es derselbe Schlüssel
        var values = keys.Select(k => (Key: newPath + k[oldPath.Length..], Value: map[k])).ToList();
        foreach (var key in keys)
            map.Remove(key);
        foreach (var (key, value) in values)
            map[key] = value;
        return keys.Count > 0;
    }

    /// <summary>Im Programm gelöscht: alles zu diesem Eintrag weg, bei Ordnern auch zu allem darin.</summary>
    public void Remove(string relativePath)
    {
        lock (_gate)
        {
            var removed = Remove(_entries, relativePath);
            foreach (var (query, picks) in _queries.ToList())
            {
                removed |= Remove(picks, relativePath);
                if (picks.Count == 0)
                    _queries.Remove(query);
            }
            if (removed)
                Changed();
        }
    }

    static bool Remove<T>(Dictionary<string, T> map, string path)
    {
        var keys = map.Keys.Where(k => IsSameOrInside(k, path)).ToList();
        foreach (var key in keys)
            map.Remove(key);
        return keys.Count > 0;
    }

    static bool IsSameOrInside(string key, string path) =>
        key.Length == path.Length
            ? PathComparer.Equals(key, path)
            : key.Length > path.Length && key[path.Length] == Path.DirectorySeparatorChar && key.StartsWith(path, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Beim Start, nur mit vorhandenem structure-Ordner: Nutzung von Einträgen, die es nicht mehr gibt und die lange
    /// nicht benutzt wurden, verwerfen. Hotkeys bleiben (ein Netzlaufwerk kann auch nur gerade fehlen).
    /// </summary>
    public void Prune(IReadOnlySet<string> existingPaths)
    {
        lock (_gate)
        {
            var cutoff = Now - PruneAge;
            var removed = false;
            foreach (var (path, record) in _entries.ToList())
            {
                if (record.Hotkey is not null || existingPaths.Contains(path) || record.Last >= cutoff) continue;
                _entries.Remove(path);
                removed = true;
            }

            foreach (var (query, picks) in _queries.ToList())
            {
                foreach (var (path, pick) in picks.ToList())
                {
                    if (existingPaths.Contains(path) || pick.Recent.Count > 0 && pick.Recent[^1] >= cutoff) continue;
                    picks.Remove(path);
                    removed = true;
                }
                if (picks.Count == 0)
                    _queries.Remove(query);
            }

            if (removed)
                Changed();
        }
    }

    SnippetRecord GetOrAdd(string path)
    {
        if (!_entries.TryGetValue(path, out var record))
            _entries[path] = record = new SnippetRecord();
        return record;
    }

    // Unter _gate: Bonus neu rechnen lassen und gesammelt speichern
    void Changed()
    {
        _usageBonus = null;
        _dirty = true;
        if (_disposed) return;
        _saveTimer ??= new Timer(_ => Flush());
        _saveTimer.Change(SaveDelayMilliseconds, Timeout.Infinite);
    }

    // --- Speichern ---

    /// <summary>Jetzt speichern, falls sich etwas geändert hat.</summary>
    public void Flush()
    {
        lock (_writeGate)
        {
            string json;
            lock (_gate)
            {
                if (!_dirty) return;
                json = JsonSerializer.Serialize(new Model { Since = _since, Entries = _entries, Queries = _queries }, Options);
                _dirty = false;
            }

            try
            {
                // Erst in eine Temp-Datei, damit ein Absturz keine halbe Datei hinterlässt
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"snippets.json konnte nicht gespeichert werden: {FilePath}", ex);
                lock (_gate)
                    _dirty = true;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _saveTimer?.Dispose();
            _saveTimer = null;
        }
        Flush();
    }

    sealed class Model
    {
        public DateTime? Since { get; set; }

        // Von Hand verschriebene Einträge können null sein, Adopt überspringt sie
        public Dictionary<string, SnippetRecord>? Entries { get; set; }

        public Dictionary<string, Dictionary<string, QueryPick>>? Queries { get; set; }
    }
}
