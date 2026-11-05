namespace QuickHasteBot.UI;

/// <summary>Ein Glied der Kopfzeile im Popup. Mit <see cref="Target"/> führt ein Klick in diesen Ordner zurück.</summary>
/// <param name="Target">Ordnerpfad relativ zum structure-Ordner, null wenn nicht anklickbar (aktueller Ordner, Trefferzahl).</param>
/// <param name="IsRoot">Der oberste Ordner, mit einem Häuschen davor.</param>
internal sealed record Breadcrumb(string Name, string? Target, bool ShowSeparator, bool IsRoot = false)
{
    public bool IsNavigable => Target is not null;
}
