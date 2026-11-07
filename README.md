# QuickHasteBot

Find and paste text snippets with a hotkey. The successor to QuickPasteBot (2012, WinForms), rebuilt with .NET 10 and WPF.

Press the hotkey (**Ctrl+Shift+Y** unless you change it) and a popup with all your snippets opens at the mouse pointer. Type to filter, press **Enter**, and the text is pasted into the window you were just working in.

Beyond plain text, snippets can contain [JavaScript](#scripts) (dates, the clipboard, calculations) and [key tags](#forms-key-tags) that fill in forms in browsers and Windows programs. Every snippet can also get a [global hotkey of its own](#hotkeys-for-files-and-folders).

The user interface is available in English and German.

## Contents

- [Getting started](#getting-started)
- [Snippets](#snippets)
- [The popup](#the-popup)
- [Pasting: two methods](#pasting-two-methods)
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

Unpack the ZIP into any folder and start `QuickHasteBot.exe`. No installation and no administrator rights are needed. QuickHasteBot runs in the tray; on first start it creates a `structure` folder with a few [example snippets](#snippets).

The exe is not code-signed, so Windows SmartScreen may warn the first time you start it. Click **More info → Run anyway**. The ZIPs are built from this repository by the [release workflow](.github/workflows/release.yml).

**The hotkey** that opens the popup is **Ctrl+Shift+Y** by default. To change it, open the settings (double-click the tray icon, or Ctrl+, in the popup), click the hotkey field under **General → Hotkey** and press the new combination. It must include Ctrl, Alt, Shift or Win; if another program already uses it, the settings say so right away. The hotkey is stored as `Hotkey` in `settings.json` and applies immediately.

The tray menu also opens the snippets folder, the [element picker](#elements-focus-click-set) and the [AI prompt for scripts](#writing-a-block-with-ai). To start with Windows, enable "Start with Windows" in the settings.

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

- **`.txt` files** are pasted. The encoding (ANSI, UTF-8 or UTF-16) is detected automatically. More extensions can be added under `TextExtensions` in `settings.json`.
- **All other files** (e.g. `.url`, `.xlsx`, `.pdf`) open with their associated application, which makes the popup a quick launcher as well.
- **The file name** without its extension is the name shown in the popup, so name files after what they contain.
- **Sorting** is by name, folders first, so number prefixes like `1-`, `2-` set the order.
- **Hidden files** as well as `desktop.ini` and `thumbs.db` are ignored.
- **Changes** show up right away, no restart needed. The folder can be rearranged in Explorer while QuickHasteBot is running, and every hotkey checks the folder again, so even replacing or renaming the whole `structure` folder is picked up.

**Examples:** on first start, QuickHasteBot fills the folder in the language of the interface: a welcome note, greetings, two emails with a date that moves along, two SQL snippets, and `Script-Samples` with 16 script blocks (dates, the clipboard, random values and more). They are only examples and can be deleted. Since they are built into the exe, unpacking an update over an existing installation never overwrites your snippets.

Snippets can be created, renamed, cloned and deleted right in the popup (see [Right-click menu](#right-click-menu)) and edited in the built-in [editor](#editor).

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
| → / ← / Backspace | With an empty search box: open folder / go up one level |
| Ctrl+O | Edit the snippet in the [editor](#editor) |
| Ctrl+N | New snippet in the current folder |
| Ctrl+E | Show it in Explorer |
| F2 | Rename the selected file or folder |
| Ctrl+, | Settings |
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

The right column shows what is inside the selected entry: the contents of a folder, the text of a snippet (scrollable, lines are not wrapped), or the address a link opens. The ↵ button in the text preview switches line wrapping on and off, the pencil next to it opens the snippet in the editor (like Ctrl+O). Shift+mouse wheel scrolls long lines sideways.

The divider between the columns can be dragged, and the left and right edges of the popup change its width (at least as wide as the shortcut row at the bottom). Wrapping, split and width are remembered (`WrapPreview`, `ListColumnShare`, `PopupWidth` in `settings.json`).

### Where it opens

By default the popup opens to the bottom left of the mouse pointer, with the pointer at its top right corner. In the settings under **Popup → When opening**, a 3×3 grid laid out like the numpad chooses where the pointer sits in the popup instead: any corner, the middle of an edge, or the center (click a field or press 1–9). If there is no room on one side, the popup opens to the other. The choice is stored as `PopupPosition`.

Three buttons next to the folder path change how the popup starts:

- **Pin:** the popup opens where it was closed last time, in the same folder with the same entry selected, even after a restart (`RememberFolder`).
- **Filter:** the popup keeps the last search text, with the same hit selected, so Enter starts the same entry again. The text is selected, so typing replaces it (`RememberQuery`).
- **Document icon:** opens an empty snippet in the [editor](#editor), like Ctrl+N.

Pin and filter are also in the settings under **When opening**.

## Pasting: two methods

- **Clipboard** (default): the text is placed on the clipboard briefly, then Ctrl+V is sent. Afterwards the previous clipboard content is restored, and the clipboard history (Win+V) is not cluttered.
- **Typing**: the text is sent character by character as Unicode input. This also works where pasting is blocked, but is slower for long texts.

The default is set in the settings under "Paste with Enter"; Shift+Enter or Shift+click uses the other one. Snippets with [key tags](#forms-key-tags) are always typed, whatever the setting, so text and keys arrive in the right order.

## Hotkeys for files and folders

Besides the popup hotkey, every snippet, file or folder can have a global hotkey of its own. It is part of the name, in square brackets at the end:

```
structure\
  3-SQL [Ctrl+Alt+S]\
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
- **Folder** next to the name ("in Snippets › 2-Email"): click it to choose another folder from the list of all folders, with collapsible subfolders. A new snippet is created there, an existing one moves there when saving.
- **Insert tag ▾** offers keys, pauses, `{clear}`, the window guard, element, window and start tags, and script helpers like `{{ date() }}`, each with a short explanation. The filter line at the top has the focus right away: typing, e.g. `field` or `wait`, lists every tag whose name, explanation or group matches, in the language of the interface or in English (all words, case and accents don't matter), Enter inserts the first one, ↓ moves into the list. Placeholders are selected after inserting, e.g. `id` in `{focus,#id}`, so you can type over them.
- **Record** writes the tags for you while you fill in a form once yourself, see [Recording a form](#recording-a-form).
- **Pick element** opens the [crosshair picker](#elements-focus-click-set); its "Insert into editor" puts the tag at the cursor in the editor used last. The **AI** button opens the [script prompt window](#writing-a-block-with-ai), which can insert its block the same way.
- **Colors** show what will happen: key tags like `{tab}` in blue, element, window and start tags in purple with the selector in teal, `{{ … }}` scripts with keywords, strings, numbers and helpers colored. Plain text and anything in braces that isn't a tag, like `{name}` or JSON, stays uncolored. A known tag with wrong arguments, like `{tab,x}` or `{wait}`, is underlined in red and would be pasted as text; hovering it tells why. The colors follow the light or dark theme.
- **Line wrapping:** the ↵ button at the bottom right of the text switches it off and on, e.g. for snippets with aligned columns (`WrapEditor` in `settings.json`, on by default). Shift+mouse wheel scrolls long lines sideways.
- **Preview** below the text shows what the snippet turns into while you type: scripts evaluated, keys as readable tags, and script errors in red. Nothing is sent.
- **Test** (F5): click into the target field afterwards, and the snippet runs there exactly as if pasted, with the text as it is in the editor, saved or not. While it waits for that click, a card next to the mouse pointer and a banner in the editor say what will run. Cancel or Esc stops the wait.
  - To debug one step at a time, select part of the snippet and only the selection runs. Tags and scripts cut off by the selection are included whole. Or put the cursor inside a tag or `{{ … }}` block and only that one runs.
  - The button says which: "Test", "Test selection", "Test tag" or "Test script".
  - A part that uses variables from an earlier script block fails, because that block doesn't run.
  - Holding Esc stops a running snippet.
- **Save** (Ctrl+S) keeps the file's encoding: ANSI (Windows-1252) stays ANSI, UTF-8 and UTF-16 stay as they were. Only text that ANSI can't hold (e.g. an emoji) turns an ANSI file into UTF-8 with BOM, so that nothing is lost. If the file was changed in another program in the meantime, the editor asks before overwriting. Closing with unsaved changes asks too.

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
| `user` | The Windows user name. |
| `guid()` | A new GUID, e.g. for reference numbers. |
| `key(name, count)` | Presses a key instead of typing text, with the same names as the [key tags](#forms-key-tags): `key('tab')`, `key('shift+tab')`, `key('down', 3)`. The result has to end up in the output. |
| `wait(ms)` | A pause before the next key or text, like `{wait,ms}`. |
| `focus(el)`, `click(el)`, `set(el, value)` | Like the [element tags](#elements-focus-click-set): `focus('#email')`, `click('name=Send;type=button')`, `set('#country', 'Austria')`. |
| `window(app)` | Like [`{window,…}`](#other-programs-window-and-start): `window('crm')`. |

- All blocks of a snippet share their variables: `{{ var n = 3; '' }}` defines `n` for later blocks without printing anything. `undefined` and `null` print nothing either.
- There is no browser or Node.js: `alert`, `fetch`, `require` and the like don't exist, and `Intl` is only partly supported (hence `date()`). Scripts have no access to files or the network.
- `}}` always ends the block. A literal `{{` is written as `{{ '{{' }}`.
- If a block fails (syntax error, runs longer than 250 ms or uses too much memory), nothing is pasted and a message names the block. The text preview in the popup always shows the snippet as written.
- If your snippets contain `{{` that should be pasted as is (e.g. Mustache or Jinja templates), turn off "Run {{ … }} in snippets as JavaScript" in the settings (`EvaluateScripts`).

### Writing a block with AI

The **AI** button in the editor or "Write a script block with AI…" in the tray menu opens a small prompt window:

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
| `{f1}` … `{f12}` | Function keys, e.g. `{f5}` reloads a page |
| `{win}` `{alt}` `{ctrl}` `{shift}` `{apps}`/`{menu}` | Modifiers alone: `{win}` opens the Start menu, `{alt}` the menu bar, `{apps}` the context menu |
| `{ctrl+a}` `{shift+tab}` `{ctrl+shift+end}` | Combinations with `ctrl`, `alt`, `shift`, `win`, plus any key above or a single letter or digit |
| `{tab,3}` | Repeats a key |
| `{wait,500}` | Pauses 500 ms, e.g. until a dropdown opens or a page has loaded (at most 10 s) |
| `{delay,50}` | From here on, pauses 50 ms after every key and character, for slow forms that lose characters (default: 30 ms after keys, none between characters) |
| `{clear}` | `{ctrl+a}{del}`: empties a prefilled field before typing |
| `{guard,off}` `{guard,on}` | Turns the [window guard](#window-guard) off or on from here |

Handy combinations: `{shift+tab}` goes back a field, `{alt+down}` opens a dropdown, `{ctrl+enter}` sends in Outlook, Teams and many web editors, `{ctrl+l}` jumps to the browser's address bar, `{alt+space}` opens the window menu, and [`{start,notepad}`](#other-programs-window-and-start) starts a program. Ctrl+Alt+Del and Win+L can't be sent (Windows protects them), and Caps Lock and Num Lock are left out on purpose, since they would stay switched.

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

- After a key that switches windows on purpose (`{win}` and `win+…`, `{alt}`, `alt+tab`, `alt+esc`, `alt+f4`, `alt+space`, `{apps}`, `shift+f10`), the guard is off for the rest of the snippet.
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
| `{set,element,"value"}` | Fills a field directly, picks a dropdown option by its text, sets a checkbox to `on`/`off`. Dropdowns built by web libraries, whose list opens elsewhere on the page, are opened and the option is clicked; if it isn't there, the snippet stops with a message. Where none of this is possible (e.g. dropdowns in Firefox, autocomplete fields), it focuses the element and types the value instead |

The element is described like this, parts combined with `;`:

| Selector | Finds | In a browser |
|---|---|---|
| `#email` or `id=email` | the id | `id="email"` |
| `name=E-Mail` or just `E-Mail` | the label or text, not case sensitive | `<label>`, `aria-label`, a button's text |
| `.btn.primary` | all these classes | `class="btn primary large"` |
| `class=btn primary` | exactly this class attribute (in Windows programs: the window class) | |
| `type=edit` | the kind: `edit`, `button`, `checkbox`, `radio`, `combobox`/`dropdown`, `link`, `listitem`, `menuitem`, `tab`, … | |
| `index=2` | the second match | |
| `window=crm` | first bring this program to the front and search there, see [below](#other-programs-window-and-start) | |

QuickHasteBot uses UI Automation, Windows' accessibility interface, which Chrome, Edge and Firefox fill with the page's form, and Windows programs with their controls (in WinForms, `#name` is the control's name). Each element tag waits up to 5 seconds for the element to appear, e.g. while a page loads, so `{wait}` is rarely needed; on large pages the very first search takes a moment longer. If nothing turns up, the snippet stops with a message. Holding Esc stops the wait.

**Element picker:** "Pick element" in the editor or "Pick element…" in the tray menu opens a small window that stays on top.

1. Hold the mouse button on its crosshair and drag it onto a field, button or link. A red frame shows what is under the pointer, with its type, id, name, class and value.
2. Let go. The picker checks which selectors find exactly that element, the most stable first (a real id before the label; ids that web apps generate anew on every load, like `:r3:`, `mui-12345` or `mantine-2hi08ejzc`, last), suggests focus, click or set depending on the element, and copies the tag to the clipboard, e.g. `{focus,#email}`.
3. Optionally pick another selector or action (their tooltips explain each tag) and edit the tag; it is copied again.
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
| Button, link, tab | `{click,f}` | `{click,f}` | `{click,f}` |

Typing is what a person does, so web forms that check or complete input as you type react to it; `{set}` is shorter, but some web forms don't notice a value that was set directly. `{clear}` comes before typed text only when the field wasn't empty. Text that would be read as a tag, like a typed `{tab}`, is written as `{{ '…' }}` so that it is typed exactly as it was.

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

`{start,…}` does the same, but starts the program first if it isn't running, and then waits for it:

```
{start,crm}{set,name=Search;type=combobox,"Müller"}{enter}
{start,"C:\Program Files\Notepad++\notepad++.exe","C:\temp\log.txt"}{ctrl+end}
```

- `{start,notepad}`: a program name that Windows knows (as in Win+R, e.g. `notepad`, `excel`, `chrome`) or a full path, `.exe` can be left out. A second argument in quotes is passed to the program, e.g. a file to open.
- If a window of the program is already open, it's brought to the front, and no second copy starts. Otherwise QuickHasteBot starts it the way Explorer does with a double-click, waits up to 30 seconds for its window and until it accepts input, then the snippet goes on. Holding Esc stops the wait.
- The window is recognized by the program's name, like `{window,process=…}`. Programs that open their window under another name (launchers, Store apps such as `calc`) aren't found this way: start them by hand and use `{window,title=…}`.
- Only programs (`.exe`) start, no scripts (`.bat`, `.ps1`, …) or shortcuts. And only a `{start,…}` written in the snippet itself starts anything: there is no script helper for it, and `{start,…}` in a script's result or on the clipboard stays text. Every start is written to the log.
- A program that asks for administrator rights shows Windows' prompt. After that, Windows doesn't let QuickHasteBot send it keys (see below).

### Programs running as administrator

Windows doesn't let normal programs send keys to programs running as administrator or read their fields, and drops everything silently. QuickHasteBot doesn't need administrator rights and doesn't ask for them. Instead, it detects such a program before typing or pasting and says so, and the element picker shows it while hovering. The fix is to start that program without administrator rights.

## Settings and files

The settings open with a double-click on the tray icon, "Settings" in the tray menu, or Ctrl+, in the popup. Every change is saved immediately to `settings.json` and applies right away.

| Tab | Contains |
|---|---|
| **General** | The popup hotkey; "Start with Windows", tray icon, language and theme; the `structure` folder, content search, the built-in editor, script and key tag options, the AI service |
| **Popup** | Paste method, mouse behavior, and "When opening": pin, filter and where the popup opens |
| **Hotkeys** | Every [file and folder hotkey](#hotkeys-for-files-and-folders), with conflicts |

**Language:** English and German. By default the interface follows the Windows display language (German on a German Windows, English everywhere else); picking a language in the settings applies immediately.

**Theme:** System (follows the Windows app setting, also when it changes), Light or Dark.

### Portable

Everything lives next to `QuickHasteBot.exe`:

- `structure\` with your snippets. The path can be changed in the settings (relative, absolute, or with `%USERPROFILE%`).
- `settings.json` with the settings. It can also be edited by hand, e.g. `"Hotkey": "Ctrl+Alt+Space"`, `"Language": "en"`, `"de"` or empty for the Windows language, `"Theme": "System"`, `"Light"` or `"Dark"`.
- `ai-prompt.txt` (optional) with your own [prompt template for the AI](#writing-a-block-with-ai).
- `QuickHasteBot.log` with errors and startup information.

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
