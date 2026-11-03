# QuickHasteBot

Textbausteine per Hotkey suchen und einfügen. Der Nachfolger von QuickPasteBot (2012, WinForms), neu gebaut mit .NET 10 und WPF.

**Ctrl+Shift+Y** öffnet am Mauszeiger ein Popup mit allen Textbausteinen. Tippen filtert, **Enter** fügt den Text in das Fenster ein, in dem du gerade warst.

## Download

Jedes Release hat zwei ZIPs mit derselben App:

- `QuickHasteBot-x.y.z-win-x64.zip` (ca. 1,5 MB) braucht die [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
- `QuickHasteBot-x.y.z-win-x64-standalone.zip` (ca. 60 MB) bringt die Runtime mit und läuft auch auf Rechnern, auf denen nur ein älteres .NET installiert ist.

## Textbausteine

Die Textbausteine sind normale Dateien im Ordner `structure` neben der exe. Unterordner werden zu Ordnern im Popup.

```
structure\
  2-Adressen\
    1-max.muster@example.com.txt
  4-Grüsse\
    01-MfG-DE.txt
  6-Weblinks\
    www.google.com.url
```

- **`.txt`** wird eingefügt. Die Kodierung (ANSI, UTF-8 oder UTF-16) wird automatisch erkannt.
- **Alle anderen Dateien** (z.B. `.url`, `.xlsx`) öffnen sich mit der zugeordneten Anwendung.
- **Sortierung** nach Name, darum funktionieren Nummern-Präfixe wie `1-`, `2-`.
- **Änderungen** im Ordner erscheinen sofort, ohne Neustart.

## Bedienung im Popup

| Taste | Aktion |
|---|---|
| Tippen | Suche über alle Ordner. Mehrere Wörter müssen alle passen, Groß/Klein und Umlaute sind egal (`grusse` findet `Grüsse`) |
| ↑ ↓ / Bild↑ Bild↓ | Auswahl |
| Enter | Einfügen (oder Ordner öffnen) |
| Shift+Enter | Mit der anderen Methode einfügen (Tippen statt Zwischenablage oder umgekehrt) |
| → / ← / Backspace | Bei leerem Suchfeld: in Ordner hinein / eine Ebene hoch |
| Strg+O | Textbaustein im Editor öffnen |
| Strg+E | Im Explorer zeigen |
| Strg+, | Einstellungen |
| Esc | Schliessen |

Mit der Maus fügt ein Klick ein oder öffnet den Ordner, mit Shift die andere Methode. In den Einstellungen lässt sich auf „Klick wählt aus, Doppelklick fügt ein“ umstellen.

## Einfügen: zwei Methoden

- **Zwischenablage** (Standard): Der Text kommt kurz in die Zwischenablage, dann wird Strg+V gesendet. Danach wird der alte Inhalt der Zwischenablage wiederhergestellt. Der Clipboard-Verlauf (Win+V) wird dabei nicht vollgemüllt.
- **Tippen**: Der Text wird Zeichen für Zeichen als Unicode-Eingabe gesendet. Das funktioniert auch dort, wo Einfügen gesperrt ist, ist bei langen Texten aber langsamer.

## Portable

Alles liegt neben `QuickHasteBot.exe`:

- `structure\` mit den Textbausteinen. Der Pfad lässt sich in den Einstellungen ändern (relativ, absolut oder mit `%USERPROFILE%`).
- `settings.json` mit den Einstellungen. Sie wird beim Ändern sofort gespeichert und kann auch von Hand bearbeitet werden.
- `QuickHasteBot.log` mit Fehlern und Startinfos.

Ein zweiter Start öffnet die Einstellungen der laufenden Instanz. `QuickHasteBot.exe --search` öffnet das Popup, zum Beispiel aus einem anderen Launcher heraus.

## Umstieg von QuickPasteBot

1. `structure\` und `settings.xml` aus dem alten Ordner neben die neue exe kopieren. Der Hotkey wird beim ersten Start übernommen.
2. QuickPasteBot beenden, sonst ist der Hotkey doppelt belegt. QuickHasteBot meldet das.
3. In den Einstellungen „Mit Windows starten“ aktivieren und den alten Autostart-Eintrag mit „Eintrag entfernen“ löschen.

## Entwicklung

Voraussetzung ist das .NET 10 SDK.

```
dotnet build
dotnet test
dotnet publish src/QuickHasteBot/QuickHasteBot.csproj -c Release -r win-x64 -o publish
```

`publish\QuickHasteBot.exe` ist eine einzelne Datei (ca. 1,5 MB) und braucht die .NET 10 Desktop Runtime. Die Standalone-Variante mit eingebauter Runtime entsteht mit:

```
dotnet publish src/QuickHasteBot/QuickHasteBot.csproj -c Release -r win-x64 -o publish/standalone -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`QHB_SNAPSHOTS=<Ordner> dotnet test` speichert zusätzlich Bilder des Popups und der Einstellungen. Das ist praktisch, um UI-Änderungen ohne Starten anzuschauen.

Ein Release entsteht, wenn ein Tag `vX.Y.Z` gepusht wird. Der Workflow testet, baut beide Varianten und hängt die ZIPs an das GitHub-Release.

```
src/QuickHasteBot/
  Core/      Einstellungen, Bibliothek, Suche, Kodierung – ohne UI, getestet
  Interop/   Win32: Hotkey, Einfügen (SendInput/Clipboard), Icons, Fenster-Platzierung
  UI/        Such-Popup, Einstellungen, Tray-Icon
tests/QuickHasteBot.Tests/
```
