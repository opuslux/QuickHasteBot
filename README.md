# QuickHasteBot

Find and paste text snippets with a hotkey. The successor to QuickPasteBot (2012, WinForms), rebuilt with .NET 10 and WPF.

Press the hotkey (**Ctrl+Shift+Y** unless you change it) and a popup with all your snippets opens at the mouse pointer. Type to filter, press **Enter**, and the text is pasted into the window you were just working in.

Beyond plain text, snippets can contain [JavaScript](#scripts) (dates, the clipboard, calculations) and [key tags](#forms-key-tags) that fill in forms in browsers and Windows programs. Snippets can [ask for details](#asking-before-pasting) before pasting, [include other snippets](#snippets-in-snippets) and paste [formatted text](#formatted-text) such as an email signature. Every snippet can also get a [global hotkey of its own](#hotkeys-for-files-and-folders).

The user interface is available in English and German.

## Contents

- [Getting started](#getting-started)
- [Snippets](#snippets)
- [The popup](#the-popup)
- [Pasting: two methods](#pasting-two-methods)
- [Formatted text](#formatted-text)
- [Hotkeys for files and folders](#hotkeys-for-files-and-folders)
- [Editor](#editor)
- [Scripts](#scripts)
- [Forms: key tags](#forms-key-tags)
- [Settings and files](#settings-and-files)
- [Switching from QuickPasteBot](#switching-from-quickpastebot)
- [Development](#development)
- [License](#license)

## Getting started

Every release comes with two ZIPs containing the same app:

- `QuickHasteBot-x.y.z-win-x64.zip` (about 4 MB) requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
- `QuickHasteBot-x.y.z-win-x64-standalone.zip` (about 60 MB) includes the runtime and also works on machines that only have an older .NET installed.

Unpack the ZIP into any folder and start `QuickHasteBot.exe`. No installation and no administrator rights are needed. QuickHasteBot runs in the tray; on first start it creates a `structure` folder with [example snippets](#snippets) in its `Demo` folder.

The exe is not code-signed, so Windows SmartScreen may warn the first time you start it. Click **More info → Run anyway**. The ZIPs are built from this repository by the [release workflow](.github/workflows/release.yml).

**The hotkey** that opens the popup is **Ctrl+Shift+Y** by default. To change it, open the settings (double-click the tray icon, or Ctrl+, in the popup), click the hotkey field under **General → Hotkey** and press the new combination. It must include Ctrl, Alt, Shift or Win; if another program already uses it, the settings say so right away. The hotkey is stored as `Hotkey` in `settings.json` and applies immediately.

The tray menu also opens the snippets folder, the [element picker](#elements-focus-click-set), the [AI prompt for scripts](#writing-a-block-with-ai) and this manual as **Help**, which the settings (next to the version) and the editor (**Help** button or F1) open as well. To start with Windows, enable "Start with Windows" in the settings.

## Snippets

Snippets are plain files in the `structure` folder next to the exe. Subfolders become folders in the popup.

```
structure\
  1 - Addresses\
    1 - Office address.txt
    2 - Shipping address.txt
    3 - Bank details.txt
  2 - Email\
    1 - Thanks for your message.txt
    2 - Meeting confirmation.txt
    3 - Out of office.txt
  3 - Sign-offs\
    1 - Kind regards (EN).txt
    2 - Freundliche Grüsse (DE).txt
    3 - Meilleures salutations (FR).txt
  4 - Support\
    Replies\
      Password reset.txt
      Ticket received.txt
    Onboarding checklist.xlsx
  5 - Links\
    Intranet.url
    Ticket system.url
  Phone numbers.txt
```

In the popup this shows up as five folders followed by `Phone numbers`. Opening `4 - Support` shows the `Replies` folder and the checklist; typing `ticket` finds `Ticket received` and `Ticket system` regardless of which folder they are in.

- **`.txt` files** are pasted. The encoding (ANSI, UTF-8 or UTF-16) is detected automatically. More extensions can be added under `TextExtensions` in `settings.json`.
- **All other files** (e.g. `.url`, `.xlsx`, `.pdf`) open with their associated application, which makes the popup a quick launcher as well.
- **The file name** without its extension is the name shown in the popup, so name files after what they contain.
- **Sorting** is by name, folders first, so number prefixes like `1 - `, `2 - ` set the order.
- **Hidden files** as well as `desktop.ini` and `thumbs.db` are ignored.
- **Changes** show up right away, no restart needed. The folder can be rearranged in Explorer while QuickHasteBot is running, and every hotkey checks the folder again, so even replacing or renaming the whole `structure` folder is picked up.

**Examples:** on first start, QuickHasteBot puts examples in the language of the interface into `structure\Demo`, in eight folders by topic: simple texts (greetings, emails with a date that moves along, SQL), text and selection (asking before pasting, `{cursor}`, `{include}`), formatting, forms (key tags and the element tags with a test form to try them on), programs and windows (media keys, one to try **Step by step** on), scripts (calculating, `run()`, and `JavaScript blocks {{ }}` with 16 script blocks: dates, the clipboard, random values and more), [conditions](#conditions-if-elseif-else) and [messages](#messages-msg-and-confirm) (with dice and an oracle for yes-or-no questions). Each example has a [comment](#forms-key-tags) at the top that explains it. They are only examples: keep your own snippets next to `Demo`, not in it, and delete `Demo` when you no longer need it. Since they are built into the exe, unpacking an update over an existing installation never overwrites your snippets. To get them back, or to try the ones a newer version brings, click **Export examples…** under the snippets folder in the settings: it proposes `structure\Demo` and, if that already exists, moves it to the Recycle Bin and builds it anew. **Other folder…** puts the examples straight into a folder of your choice instead, replacing only example files with the same name.

Snippets can be created, renamed, cloned and deleted right in the popup (see [Right-click menu](#right-click-menu)) and edited in the built-in [editor](#editor).

### Snippets in snippets

`{include,Folder/Name}` puts the text of another snippet at this point, e.g. a signature that is kept in one place and used in many emails:

```
Thank you for your order.
{include,Sign-offs/Kind regards (EN)}
```

- **The name** is the folder and name as in the popup, with `/` or `\`. The number in front (`3-`) and the extension can be left out, and case doesn't matter. A name or path that isn't at the top is also looked for in subfolders, so `{include,Simple text/Kind regards}` finds `Demo\1 - Simple text\Kind regards.txt`; if several match, the first in the popup wins.
- **Tags and scripts** in the included snippet work as if they were written here, and its script blocks share their variables with the others. A line break at its very end is left out.
- Included snippets may include others, up to 10 levels deep. If a snippet would include itself, or the name doesn't exist, nothing is pasted and a message says why. The editor underlines an unknown name in red.
- `{include}` is one of the [key tags](#forms-key-tags): with them turned off in the settings, it stays text. Inside `{{ … }}` and [`{html}` blocks](#formatted-text) it is text too.

## The popup

### Keys

| Key | Action |
|---|---|
| Typing | From the second character on, searches all folders (a single character keeps the current folder). With several words, all of them must match. Case and accents don't matter (`grusse` finds `Grüsse`). Optionally the text inside the snippets is searched as well. A folder name only narrows the search: `grusse` finds the folder `Grüsse`, not everything in it, while `grusse mfg` finds the `MfG` snippets in that folder. |
| ↑ ↓ / Page Up, Page Down | Move the selection |
| Ctrl+Home / Ctrl+End | First / last entry |
| Enter | Paste (or open the folder) |
| Shift+Enter | Paste with the [other method](#pasting-two-methods) |
| Alt+1 … Alt+9 | Like a click on the entry with that number (the first nine entries are numbered on the right); with Shift, the other paste method |
| → | Into the folder contents on the right: ↑ ↓ and Enter then work there. On a folder there, → moves the columns one level on, like a column view. While searching, only at the end of the search text, and typing goes on searching. |
| ← | Back from the right to the list on the left. On the left with an empty search box: up one level |
| Backspace | With an empty search box: up one level |
| Ctrl+O | Edit the snippet in the [editor](#editor) |
| Ctrl+N | New snippet in the current folder |
| Ctrl+E | Show it in Explorer |
| F2 | Rename the selected file or folder |
| Ctrl+, | Settings |
| Ctrl+T | [Keep the popup open](#keeping-it-open) to paste several snippets one after another |
| Esc | Close |

### Mouse

- The selection follows the pointer as soon as it rests on an entry for a moment, like in a menu.
- A click pastes the snippet or opens the folder; hold Shift for the other paste method. The settings can switch this to "Click selects, double-click pastes".
- Entries in the right column can be clicked directly: a snippet is pasted, a folder opens. So hovering over a folder and clicking a snippet next to it pastes with a single click.
- The folder path above the list and the edit, Explorer and settings commands at the bottom are clickable.
- The popup can be dragged anywhere outside the search box.

### Right-click menu

Right-clicking an entry, in either column, offers:

| Command | Does |
|---|---|
| **Edit** | Opens a text snippet in the [editor](#editor), like Ctrl+O |
| **Open with default program** | Opens it with the program Windows uses for the file type, e.g. Notepad++ |
| **Show in Explorer** | Like Ctrl+E for the entry under the pointer: a file is selected in its folder, a folder opens |
| **New snippet…** | In that folder, or next to that file |
| **New folder…** | Always in the folder that is open in the popup, whichever entry was right-clicked; the dialog says where |
| **Rename…** | Also for folders; F2 does the same for the selected entry |
| **Set hotkey…** | See [Hotkeys for files and folders](#hotkeys-for-files-and-folders) |
| **Clone…** | A copy in the same folder, with a name suggested like Explorer does (`Name - Copy.txt`) |
| **Delete…** | Asks first, then moves the file to the Recycle Bin. Where there is none, e.g. on a network drive, Windows asks whether to delete it permanently |

Right-clicking the empty space next to the entries offers **New snippet…** and **New folder…** for the open folder, which also works in an empty folder.

### Preview and layout

The right column shows what is inside the selected entry: the contents of a folder, the text of a snippet (scrollable, lines are not wrapped, tags and scripts in the same [colors](#editor) as in the editor), or the address a link opens. The ↵ button in the text preview switches line wrapping on and off, the pencil next to it opens the snippet in the editor (like Ctrl+O). Shift+mouse wheel scrolls long lines sideways.

The divider between the columns sets the width of the list on the left; a wider popup gives the extra space to the right column. The left and right edges of the popup change its width (at least as wide as the shortcut row at the bottom).

By default the popup is as tall as the list. Drag its top or bottom edge to give it a fixed height instead: it then stays the same for every folder and search, and long lists scroll. A double-click on the top or bottom edge goes back to following the list. The height never goes past the edge of the screen.

Wrapping, list width, popup width and height are remembered (`WrapPreview`, `ListColumnWidth`, `PopupWidth`, `PopupHeight` in `settings.json`; an empty `PopupHeight` means "as tall as the list").

### Where it opens

By default the popup opens to the bottom left of the mouse pointer, with the pointer at its top right corner. In the settings under **Popup → When opening**, a 3×3 grid laid out like the numpad chooses where the pointer sits in the popup instead: any corner, the middle of an edge, or the center (click a field or press 1–9). If there is no room on one side, the popup opens to the other. The choice is stored as `PopupPosition`.

Three buttons next to the folder path change how the popup starts:

- **Pin:** the popup opens where it was closed last time, in the same folder with the same entry selected, even after a restart (`RememberFolder`).
- **Filter:** the popup keeps the last search text, with the same hit selected, so Enter starts the same entry again. The text is selected, so typing replaces it (`RememberQuery`).
- **Document icon:** opens an empty snippet in the [editor](#editor), like Ctrl+N.

Pin and filter are also in the settings under **When opening**.

### Keeping it open

To paste several snippets one after another, e.g. while filling in a form, the lock at the right end of the shortcut row at the bottom (or Ctrl+T) keeps the popup from closing when it loses the focus. That is all it does: the popup is then an ordinary window that goes behind other windows when you click them and comes to the front when you click it.

- After pasting, the popup comes back to the front at the same place and in the same folder, without taking the focus away from the program you pasted into. Click the next snippet, or press the hotkey to get back into the search box. A left click on the tray icon brings it to the front too.
- Clicking into another program makes it the new target: the next snippet goes there. Windows of QuickHasteBot itself, like the editor, never become the target, and neither does a window a snippet brings to the front.
- Editing a snippet, Explorer and the settings leave the popup open too.
- Esc closes the popup, the lock stays on. The setting is saved (`KeepPopupOpen`) and applies until you switch it off again.

## Pasting: two methods

- **Clipboard** (default): the text is placed on the clipboard briefly, then Ctrl+V is sent. Afterwards the previous clipboard content is restored, and the clipboard history (Win+V) is not cluttered.
- **Typing**: the text is sent character by character as Unicode input. This also works where pasting is blocked, but is slower for long texts.

The default is set in the settings under "Paste with Enter"; Shift+Enter or Shift+click uses the other one. Snippets with [key tags](#forms-key-tags) are always typed, whatever the setting, so text and keys arrive in the right order. `{cursor}`, `{include}` and `{html}` alone don't count: such snippets are pasted with the method set.

## Formatted text

Text between `{html}` and `{/html}` is pasted as formatted text, with bold, links, lists, tables and images from the web, into Outlook, Word, Teams, web mail and other programs that take formatted text. The snippet itself stays a normal `.txt` file:

```
Hello {{ ask('Name') }},

thank you for your message.

{html}
<p>Kind regards<br><b>Max Muster</b><br><a href="https://example.com">example.com</a></p>
{/html}
```

- **Without writing HTML:** copy formatted text, e.g. your signature in Outlook or a table in Word, and press **Ctrl+Shift+V** in the [editor](#editor) (or **Insert tag ▾** → Flow → "formatted text from the clipboard"). It is inserted as an `{html}` block, without the extras Office adds for itself.
- **Pasting:** a snippet with `{html}` blocks and no other key tags goes to the clipboard in one piece, formatted, with plain text alongside for programs that only take text. Text outside the blocks keeps its line breaks. With the other paste method (typing, Shift+Enter) the plain text is typed. In a form with key tags, each block is pasted via the clipboard at its place.
- **Inside the block** everything is HTML: key tags are text, scripts run, and what they print goes in as HTML. Line breaks right after `{html}` and before `{/html}` don't count.
- **Images** need a web address (`https://…`). Pictures from your own disk, as in a signature copied from Outlook, show up on your computer but not for the recipient.

## Hotkeys for files and folders

Besides the popup hotkey, every snippet, file or folder can have a global hotkey of its own. It is part of the name, in square brackets at the end:

```
structure\
  3 - SQL [Ctrl+Alt+S]\
    Audit insert [Ctrl+Alt+A].txt
  Kind regards [Ctrl+Alt+G].txt
  Intranet [Ctrl+Alt+I].url
```

- **A text snippet** is pasted right away into the window in front, without the popup, just like Enter in the popup (key tags and scripts included).
- **Any other file** opens with its associated application.
- **A folder** opens the popup in that folder.

Since the hotkey lives in the name, there is nothing else to maintain: it moves along when a file is copied, zipped, synced or put under version control, and renaming the file changes it. The popup shows the name without the brackets, with the hotkey as a small badge on the right, and the search ignores it.

Only a valid combination in brackets counts: `Report [draft].txt` stays a name. The key names are the same as for the popup hotkey (`Ctrl`, `Shift`, `Alt`, `Win`, also `Strg`). A single function key like `[F9]` works too, but is then missing in every other program.

**Setting a hotkey:** right-click an entry, choose **Set hotkey…** and press the combination. The dialog says right away if it is already used by another entry, is the popup hotkey, or belongs to another program, and only applies a free one. "Remove hotkey" takes it out of the name again. Renaming (F2, the name field in the editor, or Explorer) works as well; QuickHasteBot checks the hotkey there too, and the editor keeps it when the name changes. A clone gets no hotkey.

**Conflicts** that come in from outside, e.g. a file renamed in Explorer, are reported once with a message. The entry then shows a warning sign next to its badge, with the reason in the tooltip. If two entries have the same hotkey, the one further up in the popup gets it. The settings list every hotkey under **Hotkeys**, conflicts in red; a double-click changes one. If there are conflicts at startup, the settings open on that tab.

## Editor

"Edit" and Ctrl+O open text snippets in a built-in editor. "New snippet…", Ctrl+N and the document icon in the popup open an empty one in the folder that is open, e.g. to try something out with Test (F5) and keep it as a file only if it is worth it. One window per file; several can be open.

- **Name** at the top: changing it renames the file when saving. The extension stays (`.txt` for new snippets).
- **Folder** next to the name ("in Snippets › 2 - Email"): click it to choose another folder from the list of all folders, with collapsible subfolders. A new snippet is created there, an existing one moves there when saving.
- **Insert tag ▾** offers keys, pauses, `{clear}`, the window guard, element, window and start tags, and script helpers like `{{ date() }}`, each with a short explanation. The filter line at the top has the focus right away: typing, e.g. `field` or `wait`, lists every tag whose name, explanation or group matches, in the language of the interface or in English (all words, case and accents don't matter), Enter inserts the first one, ↓ moves into the list. Placeholders are selected after inserting, e.g. `id` in `{focus,#id}`, so you can type over them.
- **Formatted text:** Ctrl+Shift+V inserts formatted text from the clipboard as an [`{html}` block](#formatted-text); Ctrl+V stays plain text.
- **Record** writes the tags for you while you fill in a form once yourself, see [Recording a form](#recording-a-form).
- **Pick element** opens the [crosshair picker](#elements-focus-click-set); its "Insert into editor" puts the tag at the cursor in the editor used last. The **JS-AI** button opens the [script prompt window](#writing-a-block-with-ai), which can insert its block the same way.
- **Colors** show what will happen: key tags like `{tab}` in blue, element, window and start tags in purple with the selector in teal, `{{ … }}` scripts with keywords, strings, numbers and helpers colored. Plain text and anything in braces that isn't a tag, like `{name}` or JSON, stays uncolored. A known tag with wrong arguments, like `{tab,x}` or `{wait}`, is underlined in red and would be pasted as text; hovering it tells why. The colors follow the light or dark theme.
- **Hovering** a tag, a comment or a script helper like `date` or `run` explains what it does, with the same text as in **Insert tag ▾**; on a red tag, the reason comes first.
- **Line wrapping:** the ↵ button at the bottom right of the text switches it off and on, e.g. for snippets with aligned columns (`WrapEditor` in `settings.json`, on by default). Shift+mouse wheel scrolls long lines sideways.
- **Size and position** are remembered when the editor closes, also maximized, and the next one opens there (`EditorBounds` in `settings.json`, without it in the middle of the screen). A second editor open at the same time sits slightly offset. If that spot is on a monitor that is no longer connected, Windows brings the editor back onto the screen.
- **Preview:** with the cursor inside a `{{ … }}` block, a line below the text shows what that block gives, e.g. your user name for `{{ user }}`, or its error in red. With a selection that contains a script, it shows what the selection gives, keys as readable tags. Blocks further up run first, so their variables count. Nothing is sent, and `run()` starts nothing there. Plain text and tags need no preview, so the line only appears for scripts.
- **Test** (F5): click into the target field afterwards, and the snippet runs there exactly as if pasted, with the text as it is in the editor, saved or not. While it waits for that click, a card next to the mouse pointer and a banner in the editor say what will run. Cancel or Esc stops the wait.
  - To debug one step at a time, select part of the snippet and only the selection runs. Tags and scripts cut off by the selection are included whole. Or put the cursor inside a tag or `{{ … }}` block and only that one runs.
  - The button says which: "Test", "Test selection", "Test tag" or "Test script".
  - A part that uses variables from an earlier script block fails, because that block doesn't run.
  - Holding Esc stops a running snippet.
- **Save** (Ctrl+S) keeps the file's encoding: ANSI (Windows-1252) stays ANSI, UTF-8 and UTF-16 stay as they were. Only text that ANSI can't hold (e.g. an emoji) turns an ANSI file into UTF-8 with BOM, so that nothing is lost. If the file was changed in another program in the meantime, the editor asks before overwriting. Esc or the window's close button closes the editor; with unsaved changes it asks first.

To keep using another editor, turn off "Edit snippets in the built-in editor" (`UseBuiltInEditor`). The right-click menu offers both either way.

## Scripts

Everything between `{{` and `}}` is evaluated as JavaScript when pasting and replaced by its result:

```
Zurich, {{ date('dddd, dd. MMMM yyyy') }}
Please reply by {{ date('dd.MM.yyyy', 14) }} (week {{ isoweek(14) }}).
Order number: {{ clipboard.trim().toUpperCase() }}
Total incl. VAT: {{ (19.90 * 1.081).toFixed(2) }}
Kind regards, {{ user }}
```

Besides standard JavaScript (`Date`, `Math`, `JSON`, string and array methods, template strings, arrow functions), these helpers cover what JavaScript can't do well or at all here:

| Helper | Result |
|---|---|
| `date(format, days)` | Today's date, or shifted by `days` (negative for the past). `format` is a [.NET date format](https://learn.microsoft.com/dotnet/standard/base-types/custom-date-and-time-format-strings) in the Windows language and region, e.g. `'dddd, dd. MMMM yyyy'` → `Dienstag, 03. November 2026`; without it, the short date (`03.11.2026`). |
| `time(format)` | The current time, without a format `HH:mm` (`14:05`); `'HH:mm:ss'` with seconds. |
| `isoweek(days)` | The ISO calendar week (`45`), optionally shifted by `days` like `date()`. |
| `clipboard` | The text that was on the clipboard before pasting. |
| `selection` | The text that was selected in the target window before pasting, `''` if none. QuickHasteBot copies it with Ctrl+C and puts the clipboard back afterwards. |
| `ask(label, default)` | Asks before pasting and gives what was typed, see [Asking before pasting](#asking-before-pasting). |
| `user` | The Windows user name. |
| `guid()` | A new GUID, e.g. for reference numbers. |
| `key(name, count)` | Presses a key instead of typing text, with the same names as the [key tags](#forms-key-tags): `key('tab')`, `key('shift+tab')`, `key('down', 3)`. The result has to end up in the output. |
| `wait(ms)` | A pause before the next key or text, like `{wait,ms}`. |
| `focus(el)`, `click(el)`, `set(el, value)`, `dblclick(el)`, `rclick(el)` | Like the [element tags](#elements-focus-click-set): `focus('#email')`, `click('name=Send;type=button')`, `set('#country', 'Austria')`, `dblclick('name=Report.docx')`. |
| `window(app)` | Like [`{window,…}`](#other-programs-window-and-start): `window('crm')`. |
| `run(file, args, options)` | Runs a script or program right where it stands and waits for it: `run('d:/scripts/number.bat')`, with options such as `{ wait: 30, dir: 'd:/data' }`. The result has `code`, `out` and `err`; as text it is its output. See [Running scripts](#running-scripts). |
| `msg(text, options)` | Shows a message box and waits for OK: `msg('Done')`, `msg('Check the amount', { icon: 'warning' })`. Types nothing. See [Messages](#messages-msg-and-confirm). |
| `confirm(text, options)` | Asks Yes or No, `true` for Yes: `{if,js="confirm('Send now?')"}`. See [Messages](#messages-msg-and-confirm). |

- **`selection`** works on what is selected where you paste. Pasting replaces the selection, so `«{{ selection.trim() }}»` puts quotes around it and `{{ selection.toUpperCase() }}` turns it into capitals. Ctrl+C is only pressed for snippets whose scripts mention `selection`.
- All blocks of a snippet share their variables: `{{ var n = 3; '' }}` defines `n` for later blocks without printing anything. `undefined` and `null` print nothing either.
- **A line of blocks that prints nothing goes away**, line break included, like a comment line. So assignments and checks can stand on lines of their own, and a line can depend on a condition: `{{ vip ? 'Priority handling' : '' }}` on its own line appears only for VIPs. If the blocks print something, the line stays as written. Empty lines you type yourself stay.
- There is no browser or Node.js: `alert`, `fetch`, `require` and the like don't exist (for a message box there is [`msg()`](#messages-msg-and-confirm)), and `Intl` is only partly supported (hence `date()`). Scripts have no access to files or the network.
- `}}` always ends the block. A literal `{{` is written as `{{ '{{' }}`.
- If a block fails (syntax error, runs longer than 250 ms or uses too much memory), nothing is pasted and a message names the block. The text preview in the popup always shows the snippet as written.
- If your snippets contain `{{` that should be pasted as is (e.g. Mustache or Jinja templates), turn off "Run {{ … }} in snippets as JavaScript" in the settings (`EvaluateScripts`).

### Asking before pasting

`ask()` asks for a detail when the snippet is pasted, and puts in what was typed:

```
Dear {{ ask('Name') }},

your order {{ ask('Order number') }} ships on {{ date('dd.MM.yyyy', Number(ask('Days', '3'))) }}.
```

- All `ask()` of a snippet come in one window before anything is pasted, one field per label in the order they appear. The same label is asked once and gives the same answer everywhere, also in [included snippets](#snippets-in-snippets).
- The second argument is a default, e.g. `ask('Days', '3')`. Without one, the field offers what was typed last time for that label (until QuickHasteBot is closed). The suggestion is selected, so typing replaces it.
- Enter pastes, Esc cancels, and nothing is pasted. Test (F5) and Step by step ask on every run.
- The label has to be written in quotes: `ask(label)` with a variable can't be asked beforehand and stops with a message.
- The [preview](#editor) in the editor shows the default or `[label]` instead of asking.

### Running scripts

`run()` starts a script or program at its place in the snippet, waits until it is done, and hands over what it printed and its exit code:

```
{focus,#customer}Muster{tab}
{{ const r = run('d:/scripts/number.bat', '2026', { wait: 30, hidden: true }) }}
{{ if (r.code != 0) throw new Error('number.bat says ' + r.code + ': ' + r.err) }}
{focus,#number}{{ r.out.trim() }}
```

- **What runs** is what a double-click would run: programs (`.exe`) directly, `.ps1` with PowerShell (its execution policy applies), everything else through Windows' file types (`.bat`, `.cmd`, `.py`, `.vbs`, …). A name without a folder, like `run('ping', '-n 1 example.com')`, is looked up like in a command prompt. Slashes work in paths, so there are no `\` to double in JavaScript.
- **Scripts next to the snippet:** a path without a drive, like `run('number.bat')` or `run('scripts/number.bat')`, is looked for in the snippet's folder first. So a script can sit right next to its snippet and still be found when the `structure` folder moves or is synced to another computer. The example `Number via batch file` in `Demo/6 - Scripts` works this way.
- **Arguments** are a text, passed on as written. Quote paths with spaces: `run('tool.exe', '"C:/my files/a.txt"')`. They can also go in the options as `args`: `run('tool.exe', { args: '-v', wait: 10 })`, but not both ways at once.
- **Working folder** ("Start in"): `dir` sets it, e.g. `run('export.bat', { dir: 'd:/data' })`; environment variables like `%TEMP%` work. A script without a folder in its name is then looked for there too. Without `dir`, the script runs in its own folder. If the folder doesn't exist, the snippet stops with a message.
- **Options:** `wait` is how long to wait in seconds (default 60). If the script isn't done by then, the snippet stops with a message; the script itself keeps running. `wait: 0` only starts it and goes on, `code` is then `null`. `hidden: true` starts it without a window. A visible console window stays empty, since the output goes to `out`. If the script starts another program (`start app.exe`), `run()` doesn't wait for that program; it takes what was printed up to a second after the script ended.
- **The result** `r` has `code` (exit code), `out` (what it printed) and `err` (error output), read in the code page of the command prompt. As text it is `out`, so `{{ run('date.bat') }}` types the output right away. Assigned with `const`, it prints nothing and stays available to every later block.
- **Checking the result:** `throw new Error('…')` stops the snippet with that message, as in any block.
- **At its place:** blocks are normally all evaluated before the first key is sent. If a tag comes before the first `run()`, that block and all later ones are evaluated only when the snippet gets there, so the script runs after what comes before it and later blocks see its result. In step-by-step runs each block (or line of blocks) is a step of its own; what it prints appears as steps behind it.
- **Focus:** if the script's window comes to the front, the window that was in front before is brought back afterwards. Holding Esc stops the wait.
- The preview, Test in the element picker and the AI window never start anything; there `run()` returns an empty result. With the [log](#portable) switched on, every start and exit code is written to it.

#### Messages: msg and confirm

Scripts don't run in the browser, so there is no `alert()`. `msg()` shows a message box (popup) instead, e.g. when a form is filled in, and `confirm()` asks Yes or No:

```
{click,name=Send;type=button}
{{ msg('The order is sent.') }}
```

```
{if,js="confirm('Send the reply now?')"}
{ctrl+enter}
{else}
{{ msg('Not sent, the reply stays open.', { icon: 'warning' }) }}
{/if}
```

- **At its place:** like `run()`, a block with `msg()` or `confirm()` runs when the snippet gets there, so the message shows after what comes before it. The snippet waits for the answer and then brings the window it was working in back to the front.
- **`msg()`** types nothing and goes on after OK or Enter.
- **`confirm()`** gives `true` for Yes and `false` for No. In `{if,js="…"}` it picks the branch, in a block it works like any value: `{{ confirm('Urgent?') ? 'URGENT: ' : '' }}`.
- **Esc** or the close button stops the snippet, for both.
- **Options** go in braces after the text:
  - `title: 'Order'` sets the title bar, otherwise it says QuickHasteBot.
  - `icon: 'warning'` picks the symbol and sound of Windows: `'info'` (default for `msg()`), `'warning'`, `'error'` or `'none'`. `confirm()` shows a question mark unless you pick one.
  - `close: 5` (only `msg()`) closes the message after 5 seconds by itself, the OK button counts down. So a hint doesn't hold up a snippet that runs on its own.
  ```
  {{ msg('Copying the order, don\'t touch the mouse.', { title: 'Order', close: 3 }) }}
  ```
- The message is the same window as QuickHasteBot's own messages, in light or dark like the app, and comes up on top of the window in front.
- The preview and the AI window show no message; there `confirm()` gives `true`.
- The examples in `Demo/8 - Messages` show all of this.

#### PowerShell

Any PowerShell command runs with `run()`, and its output can be typed:


```
Computer: {{ run('powershell', '-NoProfile -Command "$env:COMPUTERNAME"', { hidden: true }).out.trim() }}
```

- `.trim()` removes the line break PowerShell prints at the end.
- The command goes in double quotes. Single quotes inside it are written as `\'`, because the JavaScript text around it is in single quotes.
- `-NoProfile` skips your PowerShell profile, so it starts faster and the same everywhere. Longer scripts are better off in a `.ps1` file: `run('d:/scripts/report.ps1')`.

### Writing a block with AI

The **JS-AI** button in the editor or "Write a script block with AI…" in the tray menu opens a small prompt window:

1. **Describe** what the block should insert, in any language, e.g. "the date in 14 days with the weekday". As long as there is no text of your own, the field starts with one of ten ready-made examples, selected so typing replaces it; the light bulb goes on to the next one.
2. **Send** with "Open in ChatGPT" or Ctrl+Enter. QuickHasteBot adds the rules of the script engine, the helpers listed above and two examples, and opens the AI with the full prompt (it can be expanded in the window).
3. **Test the answer:** copy the code block with its copy button and paste it under "Test the answer". It runs right away and shows the result or the error. The circular arrow next to the heading (or F5) runs it again, handy for blocks with the time, random values or the clipboard. While the field is empty it shows an example, a decision die, and the arrow takes it over.
4. **Use it:** "Copy block" puts the finished block on the clipboard; while an editor is open, "Insert into editor" puts it at the cursor there. Code fences and a leading language label ("javascript") are removed, and missing `{{ }}` are added.

Selecting the answer by hand instead of using the copy button can lose characters that chat apps display as formatting or citations, such as backticks or `[1]`.

The window stays open after sending, so the prompt can be adjusted and sent again; Esc or "Close" closes it. Your text is kept for next time, even after a restart (`AiPromptText`); an unchanged example is never saved.

**Service:** chosen at the bottom left of the prompt window or in the settings under "AI for scripts", and remembered: ChatGPT (default, free without an account), Google AI Mode or Perplexity. Google AI Mode tends to show search results instead of code. Any other service can be entered by hand as `AiPromptUrl` in `settings.json`, with `{prompt}` for the prompt and `{lang}` for the interface language, e.g. `https://claude.ai/new?q={prompt}`; the settings then show it as "Custom address".

**Template:** "Edit template" creates `ai-prompt.txt` next to the exe with the built-in prompt and opens it. `{task}` stands for what you typed; changes apply the next time you send. "Reset template" (shown once the file exists) moves it to the Recycle Bin, and the built-in prompt applies again.

## Forms: key tags

Key tags turn a snippet into a sequence of text and key presses, to fill in web forms or dialogs in Windows programs:

```
Max{tab}Müller{tab}max.mueller@example.com{tab}{space}{tab}{enter}
```

types the first name, jumps to the next field with Tab, ticks a checkbox with Space and submits. Tags are not case sensitive; an argument follows a comma (`{tab,3}`) or a space (`{tab 3}`). Only the names below count: anything else in braces, such as `{name}` or JSON, is pasted as is.

| Tags | Keys |
|---|---|
| `{tab}` `{enter}` `{esc}` `{space}` | Next field, submit, cancel, tick a checkbox |
| `{up}` `{down}` `{left}` `{right}` `{home}` `{end}` `{pgup}` `{pgdn}` | Arrows and navigation, e.g. `{down,3}` in a dropdown |
| `{backspace}`/`{bs}` `{delete}`/`{del}` `{insert}`/`{ins}` | Editing |
| `{f1}` … `{f24}` | Function keys, e.g. `{f5}` reloads a page |
| `{numplus}` `{numminus}` `{nummul}`/`{numstar}` `{numdiv}` `{numdot}` `{numenter}` `{num0}` … `{num9}` | Numpad keys. Trees such as the folder list in Outlook or Explorer only react to these: `{numplus}` expands the selected folder, `{numminus}` collapses it, `{nummul}` expands everything below. A typed `+` doesn't do that |
| `{plus}` `{minus}` `{comma}` `{period}`/`{dot}` | The punctuation keys as keys, for combinations: `{ctrl+plus}` and `{ctrl+minus}` zoom in browsers |
| `{key,a}` `{key,A}` `{key,5,3}` | A letter or digit as a real key press instead of a typed character, for Remote Desktop, Citrix and single-key shortcuts such as j/k in webmail. A capital letter adds Shift |
| `{prtsc}`/`{printscreen}` `{pause}` `{capslock}` `{numlock}` `{scrolllock}` | Print Screen and the lock keys. Caps Lock and Num Lock stay switched until pressed again |
| `{volumeup}` `{volumedown}` `{volumemute}`/`{mute}` `{playpause}` `{nexttrack}` `{prevtrack}` `{mediastop}` | Media keys |
| `{browserback}` `{browserforward}` `{browserrefresh}` `{browserhome}` | Browser keys |
| `{win}` `{alt}` `{ctrl}` `{shift}` `{apps}`/`{menu}` | Modifiers alone: `{win}` opens the Start menu, `{alt}` the menu bar, `{apps}` the context menu |
| `{ctrl+a}` `{shift+tab}` `{ctrl+shift+end}` `{ctrl+numplus}` | Combinations with `ctrl`, `alt`, `shift`, `win`, plus any key above or a single letter or digit |
| `{tab,3}` | Repeats a key |
| `{wait,500}` | Pauses 500 ms, e.g. until a dropdown opens or a page has loaded (at most 10 s) |
| `{delay,50}` | From here on, pauses 50 ms after every key and character, for slow forms and programs that lose or mix up characters, such as Notepad in Windows 11 (`{delay,30}`) (default: 30 ms after keys, none between characters) |
| `{clear}` | `{ctrl+a}{del}`: empties a prefilled field before typing |
| `{cursor}` | Where the cursor ends up after pasting: `Dear {cursor},` puts it before the comma. Only text may follow it, no keys. Works in plain snippets too, which are then still pasted with the method set |
| `{include,Folder/Name}` | Another snippet at this point, see [Snippets in snippets](#snippets-in-snippets) |
| `{html}`…`{/html}` | Formatted text, see [Formatted text](#formatted-text) |
| `{if,…}`…`{else}`…`{/if}` | Only if an element is there, a program is open or a script expression is true, see [Conditions](#conditions-if-elseif-else) |
| `{guard,off}` `{guard,on}` | Turns the [window guard](#window-guard) off or on from here |

Handy combinations: `{shift+tab}` goes back a field, `{alt+down}` opens a dropdown, `{ctrl+enter}` sends in Outlook, Teams and many web editors, `{ctrl+l}` jumps to the browser's address bar, `{alt+space}` opens the window menu, and [`{start,notepad}`](#other-programs-window-and-start) starts a program. Ctrl+Alt+Del and Win+L can't be sent (Windows protects them).

- **Line breaks** in the text are sent as Shift+Enter: a new line in a multi-line field or chat box that doesn't submit the form. Only `{enter}` submits. A single line break at the very end of the file is ignored.
- **Line breaks at a tag are only layout** and are not sent: right after a tag, right before a tag (spaces in between don't matter), and blank lines or indentation between two tags. So every tag can go on a line of its own:

  ```
  {window,process=chrome}
  {set,name=Guests,"5"}
  {focus,#name}
  Max Muster
  {tab}
  ```

  types only "Max Muster" between the tags. Line breaks inside the text stay. For a real new line at a tag, write `{shift+enter}` or `{enter}`. "Insert into editor" in the element picker puts each tag on a line of its own.
- **With scripts:** tags and `{{ … }}` blocks can be mixed freely, e.g. `{{ user }}{tab}{{ date() }}{enter}`, and variables carry over as usual. Inside a script, `key()` and `wait()` press keys, e.g. `{{ ['Max', 'Müller'].join(key('tab')) + key('enter') }}`. What a script returns or what comes from the clipboard is never read as a tag, so a copied `{enter}` can't submit anything. A literal tag is written as `{{ '{tab}' }}`.
- **Comments:** `{# note}` is a note to yourself and is never pasted, in forms and in plain text. It may span several lines and ends at the first `}`. On a line of its own, the whole line goes, so comments leave no empty lines. **Insert tag ▾** → Flow → the first entry puts one on its own line with the note selected. Comments belong to the tags: with key tags turned off (below), they are pasted as is.
- **Stopping:** holding Esc stops a snippet between two steps.
- **Turning off:** with "Press key tags like {tab} and {enter}" off in the settings (`EvaluateKeyTags`), tags are pasted as is.

### Window guard

If another window comes to the front while a snippet runs (e.g. you click elsewhere during a `{wait}`), the snippet stops, so keys don't end up in the wrong program. Dialogs of the target window don't count.

- After a key that switches windows on purpose (`{win}` and `win+…`, `{alt}`, `alt+tab`, `alt+esc`, `alt+f4`, `alt+space`, `{apps}`, `shift+f10`, `{prtsc}`), the guard is off for the rest of the snippet.
- `{guard,off}` turns it off explicitly, e.g. for `{guard,off}{win}{wait,500}Notepad{wait,300}{enter}`; `{guard,on}` turns it back on for the window that is in front at that moment.
- The setting "Stop when another window comes to the front" (`KeyTagsWindowGuard`) turns it off for all snippets.

### Elements: focus, click, set

Instead of counting `{tab}`s, a snippet can address a field directly, in the window that is in front:

```
{focus,#firstname}Max{set,#lastname,"Müller"}{set,#country,"Austria"}{set,#terms,on}{click,name=Send;type=button}
```

| Tag | Does |
|---|---|
| `{focus,element}` | Puts the cursor into the field, typing continues there |
| `{click,element}` | Clicks a button, link, tab or checkbox; without anything to click, a real mouse click on it |
| `{dblclick,element}` | Double-clicks the element with the mouse, e.g. a list entry, a file or a table row |
| `{rclick,element}` | Right-clicks the element with the mouse, e.g. to open its context menu |
| `{set,element,"value"}` | Fills a field directly, picks a dropdown option by its text, sets a checkbox to `on`/`off`. Dropdowns built by web libraries, whose list opens elsewhere on the page, are opened and the option is clicked; if it isn't there, the snippet stops with a message. Where none of this is possible (e.g. dropdowns in Firefox, autocomplete fields), it focuses the element and types the value instead |

`{dblclick}` and `{rclick}` are always real mouse clicks, because UI Automation has no other way to do them: the element has to be visible on the screen, and the mouse pointer jumps back afterwards. A context menu that opens is searched before the window itself, so the next tag picks from it, even if the window has a button with the same name:

```
{rclick,name=Report.docx}{click,name=Copy}
```

The element is described like this, parts combined with `;`:

| Selector | Finds | In a browser |
|---|---|---|
| `#email` or `id=email` | the id | `id="email"` |
| `name=E-Mail` or just `E-Mail` | the label or text, not case sensitive; `*` stands for any text, see below. Without `type=`, a field comes before a plain text with the same name, so `name=E-Mail` finds the field, not its label | `<label>`, `aria-label`, a button's text |
| `.btn.primary` | all these classes | `class="btn primary large"` |
| `class=btn primary` | exactly this class attribute (in Windows programs: the window class) | |
| `help=Print` | the tooltip, not case sensitive; `*` works as in names | `title`, a field's placeholder |
| `type=edit` | the kind: `edit`, `button`, `checkbox`, `radio`, `combobox`/`dropdown`, `link`, `listitem`, `menuitem`, `tab`, … | |
| `index=2` | the second match | |
| `window=crm` | first bring this program to the front and search there, see [below](#other-programs-window-and-start) | |

Some labels change: Outlook calls its inbox "Inbox" or "Inbox - 2 unread items", depending on the mail. With `*` in the name, any text may stand there, also none: `name=Inbox*` begins with, `name=*unread*` contains, `name=*.docx` ends with, and `name=*lorem*ipsum*` contains both words in this order. The pattern covers the whole name, so `Inbox*` doesn't find "My Inbox". Without `*`, the name has to match exactly. `*` is the only wildcard; all other characters, like `.`, `?` or `(`, count as written, and a `*` in the label itself is matched by the pattern's `*` as well. If several elements fit, the first one counts (or the one from `index=`), so make the pattern as precise as you can and add `type=`, which also makes the search faster:

```
{click,name=Inbox*;type=button}
```

When the same element appears more than once, e.g. "Street" in the billing and in the delivery address, or "Inbox" in two mailboxes, put the part of the window it lies in first, followed by ` > ` (with spaces):

```
{set,name=Delivery address > name=Street,"Main Street 1"}
{click,name=john@example.com* > name=Inbox*;type=button}
```

The part on the left is a selector like any other, and it may contain the element anywhere inside, not only directly. If several parts match, they are searched one after the other; `index=2` on the left takes only the second one, `index=2` on the right the second element across all of them. Steps can be chained: `#orders > name=Delivery address > name=Street`. The picker suggests this form by itself when no other selector finds exactly the element.

QuickHasteBot uses UI Automation, Windows' accessibility interface, which Chrome, Edge and Firefox fill with the page's form, and Windows programs with their controls (in WinForms, `#name` is the control's name). Each element tag waits up to 5 seconds for the element to appear, e.g. while a page loads, and for a disabled field to be enabled, e.g. a time dropdown that a form only enables once the date is filled in. So `{wait}` is rarely needed; on large pages the very first search takes a moment longer. If nothing turns up, the snippet stops with a message. Holding Esc stops the wait.

**Element picker:** "Pick element" in the editor or "Pick element…" in the tray menu opens a small window at the top right of the screen that stays on top.

1. Hold the mouse button on its crosshair and drag it onto a field, button or link. A red frame shows what is under the pointer, with its type, id, name, class, tooltip and value.
2. Let go. The picker checks which selectors find exactly that element, the most stable first (a real id before the label; ids that web apps generate anew on every load, like `:r3:`, `mui-12345` or `mantine-2hi08ejzc`, last), suggests focus, click or set depending on the element, and copies the tag to the clipboard, e.g. `{focus,#email}`.
3. Optionally pick another selector or action (focus, click, double-click, right-click or set; their tooltips explain each tag) and edit the tag; it is copied again.
4. **Test** runs the tag right away in the program the element was picked in, like a snippet would (so a click really clicks), and shows whether it worked or why not.

With "Include the window" (remembered), the tag starts with `{window,process=…}` for the program it was picked in.

### Recording a form

**Record** in the editor watches while you fill in a form once yourself and turns that into tags. Click it, click into the target window, and work as usual: type into fields, pick from dropdowns, tick checkboxes, click buttons. A bar at the bottom of the target window shows how many steps there are and the last one as a tag; like the step-by-step bar, it never takes the focus away. **Done** puts the tags at the cursor in the editor, one step per line; **Discard**, or closing the editor, throws them away. While recording, the text is locked, and pressing the button again or Esc in the editor works like Done.

```
{window,process=crm}
{focus,#name}Muster
{focus,#city}{clear}Bern
{set,#country,"Switzerland"}
{set,#newsletter,"on"}
{click,name=Save}
```

The three buttons in the bar choose what fields become, and apply to the whole recording, also afterwards (`RecordMode` in `settings.json`):

| | Mixed (default) | Always set | Always type |
|---|---|---|---|
| Text field | `{focus,f}` and the text typed | `{set,f,"text"}` | `{focus,f}` and the text typed |
| Dropdown | `{set,f,"option"}` | `{set,f,"option"}` | `{focus,f}` and the option typed |
| Checkbox | `{set,f,"on"}` | `{set,f,"on"}` | `{click,f}` |
| Date or time field (browser) | `{focus,f}` and the digits typed | `{focus,f}` and the digits typed | `{focus,f}` and the digits typed |
| Button, link, tab | `{click,f}` | `{click,f}` | `{click,f}` |

Typing is what a person does, so web forms that check or complete input as you type react to it; `{set}` is shorter, but some web forms don't notice a value that was set directly. `{clear}` comes before typed text only when the field wasn't empty. Text that would be read as a tag, like a typed `{tab}`, is written as `{{ '…' }}` so that it is typed exactly as it was.

Date and time fields in Chrome and Edge are always typed, because they ignore a value that is set directly. It doesn't matter how you fill them in, by typing, in the calendar or with Alt+↓: the recording takes the date that is in the field when you leave it and writes its digits in the order the field shows them, e.g. `{focus,#res-date}20112026` for 20.11.2026 or `1930` for 19:30. The browser moves to the next part by itself, so there are no dots or colons. Clicks on the calendar icon and in the calendar are left out.

What the recording leaves out:

- **Keys** like Tab, Enter or shortcuts. There is no keyboard or mouse hook; the recording reads fields, values and clicks through the same UI Automation the element tags use. So it records what a key did (another field, a new value), not the key itself. Add `{enter}` and the like where needed.
- **Fields that didn't change**, and clicks on the option of an open dropdown (the `{set}` picks it).
- **Passwords:** a password field only gets `{focus,f}`, the password itself is not recorded.
- **Programs without UI Automation**: where the element picker sees nothing, the recording doesn't either. For [programs running as administrator](#programs-running-as-administrator), the bar says so.

A recording starts with `{window,process=…}` for the program it was made in, so Test and Step by step run it without clicking into the target first.

### Other programs: window and start

`{window,…}` brings a program to the front, and the rest of the snippet goes there. So a snippet works wherever you start it from:

```
{window,crm}{set,name=Search;type=combobox,"Müller"}{enter}
```

- `{window,crm}`: a program called `crm(.exe)`, or else a window whose title contains "crm". `{window,process=crm}` only matches the program, `{window,title=Customers*}` only the title (`*` stands for any text; without `*`, part of the title is enough). Case doesn't matter.
- If the program has several windows, the one used last wins. A minimized window is restored. If none turns up within 5 seconds (e.g. while the program starts), the snippet stops with a message. Holding Esc stops the wait.
- For a single tag, `window=` in the element does the same: `{set,name=Search;type=combobox;window=crm,"Müller"}`.
- The window guard follows: it now watches that window instead of turning off. `{guard,off}` still keeps it off.

`{start,…}` does the same, but starts the program first if it isn't running, and then waits for it. It also opens web addresses, [see below](#web-addresses):

```
{start,crm}{set,name=Search;type=combobox,"Müller"}{enter}
{start,"C:\Program Files\Notepad++\notepad++.exe","C:\temp\log.txt"}{ctrl+end}
```

- `{start,notepad}`: a program name that Windows knows (as in Win+R, e.g. `notepad`, `excel`, `chrome`) or a full path, `.exe` can be left out. A second argument in quotes is passed to the program, e.g. a file to open.
- If a window of the program is already open, it's brought to the front, and no second copy starts. Otherwise QuickHasteBot starts it the way Explorer does with a double-click, waits up to 30 seconds for its window and until it accepts input, then the snippet goes on. Holding Esc stops the wait.
- The window is recognized by the program's name, like `{window,process=…}`. Programs that open their window under another name (launchers, Store apps such as `calc`) aren't found this way: start them by hand and use `{window,title=…}`.
- Only programs (`.exe`) and web addresses start, no scripts (`.bat`, `.ps1`, …) or shortcuts; for those, use [`run()`](#running-scripts), which runs a script, waits for it and reads its output, but doesn't bring a window to the front. Only a `{start,…}` written in the snippet itself starts anything: `{start,…}` in a script's result or on the clipboard stays text. With the [log](#portable) switched on, every start is written to it.
- A program that asks for administrator rights shows Windows' prompt. After that, Windows doesn't let QuickHasteBot send it keys (see below).

#### Web addresses

With a web address, `{start,…}` opens it in the default browser, and the rest of the snippet goes to that page:

```
{start,https://example.com/order}
{set,#email,"max@example.com"}
{click,name=Send;type=button}
```

- Only `http://` and `https://` addresses, without further arguments. An address with a comma goes in quotes: `{start,"https://example.com/?a=1,2"}`.
- The address always opens, also when the browser is already running; then in a new tab. QuickHasteBot waits until a browser window shows the new page (up to 30 seconds while the browser starts) and brings it to the front.
- Which browser that is, Windows decides (Settings › Apps › Default apps). To use a particular browser instead, start it and type the address: `{start,chrome}{ctrl+t}{ctrl+l}https://example.com{enter}`.
- The element tags wait up to 5 seconds for their field while the page loads. For slower pages, put a `{wait,2000}` after the `{start,…}`.

### Conditions: if, elseif, else

`{if,…}` decides while the snippet runs what comes next: if the condition holds, what follows it runs, otherwise what follows `{else}`. Each tag can go on a line of its own like any other tag, and `{/if}` ends the block:

```
{window,process=outlook}
{if,name=Send}
{ctrl+enter}
{else}
{alt+s}
{/if}
```

- **Element:** `{if,name=Send}` holds if the element is there and visible, with the same selectors as `{click,…}`: `#id`, `name=`, `help=`, `class=`, `type=`, `*` and containers with ` > `. Hidden or collapsed elements don't count; a field further down a web page does. It searches the window in front, or with `window=` in the selector that window, which then comes to the front: `{if,window=crm;name=Save}`.
- **Window:** `{if,process=outlook}`, `{if,title=*Draft*}` or `{if,window=crm}` (as in [`{window,…}`](#other-programs-window-and-start)) hold if such a window is open. It then comes to the front like with `{window,…}`, so the keys after it go there.
- **Script:** `{if,js="r.code == 0"}` holds if the JavaScript expression is true. It's checked when the run gets there, with the variables of the blocks before it, so it sees the answers of [`ask()`](#asking-before-pasting) and the result of [`run()`](#running-scripts). `{if,js="confirm('Send now?')"}` asks Yes or No right there, see [Messages](#messages-msg-and-confirm). Text inside the quotes is written with `'`: `{if,js="answer == 'yes'"}`.
- **Waiting:** elements and windows get up to 1 second to turn up, `wait=3000` gives them 3 (at most 10). If they don't, the condition doesn't hold. Unlike `{click,…}`, that's no error, the snippet just goes on after `{else}` (or after `{/if}`).
- **Not:** `{if,not,name=Send}` holds if the element is not there.
- **More cases:** `{elseif,…}` checks the next condition if the ones before didn't hold, `{else}` comes last and is optional:

  ```
  {if,name=Send}{ctrl+enter}
  {elseif,name=Reply}{click,name=Reply}
  {else}{esc}
  {/if}
  ```

- **Nesting:** blocks may sit inside each other, every `{if}` needs its `{/if}`. A `{else}` without `{if}` or a missing `{/if}` shows red in the editor, and the snippet doesn't start.
- **Scripts in a branch** only run when their branch does. So `{{ … }}` blocks after an `{if}` are evaluated when the run gets there, like blocks with `run()`.
- **Step by step** stops at `{if}` and frames the element it checks. The next step shown is the branch it takes.
- **Insert tag ▾** → Flow has a block for each kind of condition. The examples in `Demo/7 - Conditions` show all of them.

### Programs running as administrator


Windows doesn't let normal programs send keys to programs running as administrator or read their fields, and drops everything silently. QuickHasteBot doesn't need administrator rights and doesn't ask for them. Instead, it detects such a program before typing or pasting and says so, and the element picker shows it while hovering. The fix is to start that program without administrator rights.

## Settings and files

The settings open with a double-click on the tray icon, "Settings" in the tray menu, or Ctrl+, in the popup. Every change is saved immediately to `settings.json` and applies right away.

| Tab | Contains |
|---|---|
| **General** | The popup hotkey; "Start with Windows", tray icon, log file, the hint on new versions, language and theme; the `structure` folder, content search, the built-in editor, script and key tag options, the AI service |
| **Popup** | Paste method, mouse behavior, and "When opening": pin, filter and where the popup opens |
| **Hotkeys** | Every [file and folder hotkey](#hotkeys-for-files-and-folders), with conflicts |

**New versions:** once a day QuickHasteBot asks GitHub for the newest release. If there is a newer one, the tray menu and the settings (next to the version) offer "Download version x.y.z", and Windows shows a notification once. Only the request itself is sent, nothing about you. To turn it off, untick "Tell me about new versions" under **General** (`CheckForUpdates`).

**Language:** English and German. By default the interface follows the Windows display language (German on a German Windows, English everywhere else); picking a language in the settings applies immediately.

**Theme:** System (follows the Windows app setting, also when it changes), Light or Dark.

### Portable

Everything lives next to `QuickHasteBot.exe`:

- `structure\` with your snippets. The path can be changed in the settings (relative, absolute, or with `%USERPROFILE%`).
- `settings.json` with the settings. It can also be edited by hand, e.g. `"Hotkey": "Ctrl+Alt+Space"`, `"Language": "en"`, `"de"` or empty for the Windows language, `"Theme": "System"`, `"Light"` or `"Dark"`.
- `ai-prompt.txt` (optional) with your own [prompt template for the AI](#writing-a-block-with-ai).
- `QuickHasteBot.log` with errors, startup information and every program and script a snippet starts. Only written when "Write log file" is on in the settings (`"WriteLog": true`), off by default. Over 1 MB it is renamed to `QuickHasteBot.log.old`.

The only thing written elsewhere is the short animation in the settings window, which is extracted to `%TEMP%\QuickHasteBot` the first time the settings open.

### Command line

Starting the exe a second time opens the settings of the running instance. `QuickHasteBot.exe --search` opens the popup, for example from another launcher.

## Switching from QuickPasteBot

1. Copy `structure\` and `settings.xml` from the old folder next to the new exe. The old hotkey is taken over on first start.
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

```
src/QuickHasteBot/
  Core/      Settings, library, search, encoding – no UI, covered by tests
  Interop/   Win32: hotkey, pasting (SendInput/clipboard), icons, window placement
  UI/        Search popup, settings, tray icon, texts (Strings.resx, Strings.de.resx)
tests/QuickHasteBot.Tests/
```

- **Version:** comes from the latest `vX.Y.Z` git tag. A build exactly on the tag is `2.2.0`, three commits later it is `2.2.0-dev.3`, and uncommitted changes add `.dirty`. The settings window shows it together with the commit.
- **Texts:** the user interface texts live in `src/QuickHasteBot/UI/Strings.resx` (English, also the fallback for every other language) and `Strings.de.resx` (German). The build generates the `Strings` class from them, used in code (`Strings.HotkeyActive`) and XAML (`{x:Static ui:Strings.HotkeyActive}`). A test makes sure both files contain the same keys and placeholders. Adding a language means adding another `Strings.<code>.resx` and an entry in `UiLanguage.Choices`.
- **Snapshots:** `QHB_SNAPSHOTS=<folder> dotnet test` additionally saves images of the popup and the settings window, which is handy for checking UI changes without starting the app.
- **Release:** pushing a `vX.Y.Z` tag runs the tests, builds both variants and attaches the ZIPs to the GitHub release.

## License

QuickHasteBot is free software under the [GNU General Public License v3.0 or later](LICENSE). It uses AvalonEdit, H.NotifyIcon, Jint and Acornima; their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The changes up to version 2.8.1 are in [CHANGELOG.md](CHANGELOG.md).
