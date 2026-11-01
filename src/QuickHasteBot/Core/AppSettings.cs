namespace QuickHasteBot.Core;

public enum PasteMode
{
    /// <summary>Text ins Clipboard legen und Strg+V senden, danach das alte Clipboard wiederherstellen.</summary>
    Clipboard,

    /// <summary>Text Zeichen für Zeichen als Unicode-Tastatureingabe senden.</summary>
    Typing,
}

public sealed class AppSettings
{
    public const string DefaultStructurePath = "structure";

    public string Hotkey { get; set; } = HotkeyGesture.Default.ToString();

    public string StructurePath { get; set; } = DefaultStructurePath;

    public PasteMode PasteMode { get; set; } = PasteMode.Clipboard;

    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Ein Klick im Popup fügt ein bzw. öffnet den Ordner, sonst wählt er nur aus und es braucht einen Doppelklick.</summary>
    public bool SingleClick { get; set; } = true;

    /// <summary>Suche auch im Inhalt der Textbausteine, nicht nur im Namen.</summary>
    public bool SearchContent { get; set; } = true;

    /// <summary>Diese Dateien werden eingefügt, alle anderen mit der zugeordneten Anwendung geöffnet.</summary>
    public List<string> TextExtensions { get; set; } = [".txt"];

    public static PasteMode Other(PasteMode mode) => mode == PasteMode.Clipboard ? PasteMode.Typing : PasteMode.Clipboard;
}
