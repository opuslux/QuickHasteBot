using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickHasteBot.Core;

public sealed class SettingsStore(string path)
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        // Die Datei wird auch von Hand bearbeitet: "Ctrl+Shift+Y" statt "Ctrl+Shift+Y"
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string FilePath { get; } = path;

    public bool Exists => File.Exists(FilePath);

    /// <summary>Lädt die Einstellungen. Fehlt die Datei oder ist sie kaputt, gibt es die Standardwerte.</summary>
    public AppSettings Load()
    {
        if (!Exists) return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"Einstellungen konnten nicht gelesen werden: {FilePath}", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            // Erst in eine Temp-Datei schreiben, damit ein Absturz keine halbe settings.json hinterlässt
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Einstellungen konnten nicht gespeichert werden: {FilePath}", ex);
        }
    }
}
