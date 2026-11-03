# QuickHasteBot

Find and paste text snippets with a hotkey. The successor to QuickPasteBot (2012, WinForms), rebuilt with .NET 10 and WPF.

**Ctrl+Shift+Y** opens a popup with all your snippets at the mouse cursor. Type to filter, press **Enter** to paste the text into the window you were just working in.

The user interface is available in English and German. By default it follows the Windows display language (German on a German Windows, English everywhere else); the settings let you pick a language explicitly, which applies immediately.

## Download

Every release comes with two ZIPs containing the same app:

- `QuickHasteBot-x.y.z-win-x64.zip` (about 3 MB) requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
- `QuickHasteBot-x.y.z-win-x64-standalone.zip` (about 60 MB) includes the runtime and also works on machines that only have an older .NET installed.

## Snippets

Snippets are plain files in the `structure` folder next to the exe. Subfolders become folders in the popup.

```
structure\
  1-Addresses\
    1-Office address.txt
    2-Shipping address.txt
    3-Bank details.txt
  2-Email\
    1-Thanks for your message.txt
    2-Meeting confirmation.txt
    3-Out of office.txt
  3-Sign-offs\
    1-Kind regards (EN).txt
    2-Freundliche Grüsse (DE).txt
    3-Meilleures salutations (FR).txt
  4-Support\
    Replies\
      Password reset.txt
      Ticket received.txt
    Onboarding checklist.xlsx
  5-Links\
    Intranet.url
    Ticket system.url
  Phone numbers.txt
```

In the popup this shows up as five folders followed by `Phone numbers`. Opening `4-Support` shows the `Replies` folder and the checklist; typing `ticket` finds `Ticket received` and `Ticket system` regardless of which folder they are in.

- **`.txt`** files are pasted. The encoding (ANSI, UTF-8 or UTF-16) is detected automatically. More extensions can be added under `TextExtensions` in `settings.json`.
- **All other files** (e.g. `.url`, `.xlsx`, `.pdf`) open with their associated application, which makes the popup a quick launcher as well.
- **The file name** without its extension is the name shown in the popup, so name files after what they contain.
- **Sorting** is by name, folders first, so number prefixes like `1-`, `2-` set the order.
- **Hidden files** as well as `desktop.ini` and `thumbs.db` are ignored.
- **Changes** in the folder show up immediately, no restart needed.

## Using the popup

| Key | Action |
|---|---|
| Typing | From the second character on, searches all folders (a single character keeps the current folder). With several words, all of them must match. Case and accents don't matter (`grusse` finds `Grüsse`). Folder names and, optionally, the text inside the snippets are searched as well. |
| ↑ ↓ / Page Up, Page Down | Move the selection |
| Ctrl+Home / Ctrl+End | First / last entry |
| Enter | Paste (or open the folder) |
| Shift+Enter | Paste with the other method (typing instead of clipboard or vice versa) |
| Alt+1 … Alt+9 | Like a click on the entry with that number (the first nine entries in the list are numbered on the right); with Shift, the other paste method |
| → / ← / Backspace | With an empty search box: open folder / go up one level |
| Ctrl+O | Open the snippet in its editor |
| Ctrl+E | Show it in Explorer |
| Ctrl+, | Settings |
| Esc | Close |

The column on the right shows what is inside the selected entry: the contents of a folder, the text of a snippet (scrollable, lines are not wrapped), or the address a link opens. The ↵ button in the text preview switches line wrapping on and off. The divider between the columns can be dragged, and the left and right edges of the popup change its width (at least as wide as the shortcut row at the bottom). Wrapping, split and width are remembered in `settings.json` (`WrapPreview`, `ListColumnShare`, `PopupWidth`).

By default the popup opens to the bottom left of the mouse pointer, with the pointer at its top right corner. In the settings under "When opening", a small 3×3 grid laid out like the numpad chooses where the pointer sits in the popup instead: any corner, the middle of an edge, or the center (click a field or press 1–9). If there is no room on one side, the popup opens to the other. The choice is stored as `PopupPosition` in `settings.json`.

The pin next to the folder path makes the popup open where it was closed last time, in the same folder with the same entry selected, even after a restart. The same option is in the settings under "When opening".

With the mouse, the selection follows the pointer as soon as it rests on an entry for a moment, like in a menu. A click pastes the snippet or opens the folder; hold Shift for the other paste method. Entries in the right column can be clicked directly: a snippet is pasted right away, a folder opens. This way, hovering over a folder and clicking a snippet next to it pastes with a single click. The settings can switch clicking to "Click selects, double-click pastes". The folder path above the list and the edit, Explorer and settings commands at the bottom are clickable too, and the popup can be dragged anywhere outside the search box.

## Pasting: two methods

- **Clipboard** (default): the text is placed on the clipboard briefly, then Ctrl+V is sent. Afterwards the previous clipboard content is restored, and the clipboard history (Win+V) is not cluttered.
- **Typing**: the text is sent character by character as Unicode input. This also works where pasting is blocked, but is slower for long texts.

## Portable

Everything lives next to `QuickHasteBot.exe`:

- `structure\` with your snippets. The path can be changed in the settings (relative, absolute, or with `%USERPROFILE%`).
- `settings.json` with the settings. It is saved immediately on every change and can also be edited by hand (`"Language": "en"`, `"de"`, or empty for the Windows language).
- `QuickHasteBot.log` with errors and startup information.

The only thing written elsewhere is the short animation in the settings window, which is extracted to `%TEMP%\QuickHasteBot` the first time the settings open.

Starting the exe a second time opens the settings of the running instance. `QuickHasteBot.exe --search` opens the popup, for example from another launcher.

## Switching from QuickPasteBot

1. Copy `structure\` and `settings.xml` from the old folder next to the new exe. The hotkey is taken over on first start.
2. Quit QuickPasteBot, otherwise the hotkey is registered twice. QuickHasteBot will tell you if that happens.
3. In the settings, enable "Start with Windows" and remove the old autostart entry with "Remove entry".

## Development

Requires the .NET 10 SDK.

```
dotnet build
dotnet test
dotnet publish src/QuickHasteBot/QuickHasteBot.csproj -c Release -r win-x64 -o publish
```

`publish\QuickHasteBot.exe` is a single file and requires the .NET 10 Desktop Runtime. The standalone variant with the runtime built in is created with:

```
dotnet publish src/QuickHasteBot/QuickHasteBot.csproj -c Release -r win-x64 -o publish/standalone -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The version comes from the latest `vX.Y.Z` git tag: a build exactly on the tag is `2.2.0`, three commits later it is `2.2.0-dev.3`, and uncommitted changes add `.dirty`. The settings window shows it together with the commit.

User interface texts live in `src/QuickHasteBot/UI/Strings.resx` (English, also the fallback for every other language) and `Strings.de.resx` (German). The build generates the `Strings` class from them, used in code (`Strings.HotkeyActive`) and XAML (`{x:Static ui:Strings.HotkeyActive}`). A test makes sure both files contain the same keys and placeholders; adding a language means adding another `Strings.<code>.resx` and an entry in `UiLanguage.Choices`.

`QHB_SNAPSHOTS=<folder> dotnet test` additionally saves images of the popup and the settings window, which is handy for checking UI changes without starting the app.

A release is created by pushing a `vX.Y.Z` tag. The workflow runs the tests, builds both variants and attaches the ZIPs to the GitHub release.

```
src/QuickHasteBot/
  Core/      Settings, library, search, encoding – no UI, covered by tests
  Interop/   Win32: hotkey, pasting (SendInput/clipboard), icons, window placement
  UI/        Search popup, settings, tray icon, texts (Strings.resx, Strings.de.resx)
tests/QuickHasteBot.Tests/
```
