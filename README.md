# QuickHasteBot

Find and paste text snippets with a hotkey. The successor to QuickPasteBot (2012, WinForms), rebuilt with .NET 10 and WPF.

**Ctrl+Shift+Y** opens a popup with all your snippets at the mouse cursor. Type to filter, press **Enter** to paste the text into the window you were just working in.

The user interface is available in English and German. By default it follows the Windows display language (German on a German Windows, English everywhere else); the settings let you pick a language explicitly, which applies immediately.

## Download

Every release comes with two ZIPs containing the same app:

- `QuickHasteBot-x.y.z-win-x64.zip` (about 4 MB) requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
- `QuickHasteBot-x.y.z-win-x64-standalone.zip` (about 60 MB) includes the runtime and also works on machines that only have an older .NET installed.

## Snippets

Snippets are plain files in the `structure` folder next to the exe. Subfolders become folders in the popup. The folder can be rearranged in Explorer while QuickHasteBot is running: changes show up right away, and each hotkey checks the folder again, so even replacing or renaming the whole `structure` folder is picked up.

On first start, QuickHasteBot creates the folder with a few examples in the language of the interface (English or German): a welcome note, greetings, two emails with a date that moves along, two SQL snippets, and `Script-Samples` with 16 script blocks (dates, the clipboard, random values and more). They are only examples and can be deleted. The examples are built into the exe, so unpacking an update over an existing installation never overwrites your snippets.

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
| Ctrl+O | Edit the snippet (in the [built-in editor](#editor)) |
| Ctrl+N | New snippet in the current folder |
| Ctrl+E | Show it in Explorer |
| F2 | Rename the selected file or folder |
| Ctrl+, | Settings |
| Esc | Close |

The column on the right shows what is inside the selected entry: the contents of a folder, the text of a snippet (scrollable, lines are not wrapped), or the address a link opens. The ↵ button in the text preview switches line wrapping on and off. The divider between the columns can be dragged, and the left and right edges of the popup change its width (at least as wide as the shortcut row at the bottom). Wrapping, split and width are remembered in `settings.json` (`WrapPreview`, `ListColumnShare`, `PopupWidth`).

By default the popup opens to the bottom left of the mouse pointer, with the pointer at its top right corner. In the settings under "When opening", a small 3×3 grid laid out like the numpad chooses where the pointer sits in the popup instead: any corner, the middle of an edge, or the center (click a field or press 1–9). If there is no room on one side, the popup opens to the other. The choice is stored as `PopupPosition` in `settings.json`.

The pin next to the folder path makes the popup open where it was closed last time, in the same folder with the same entry selected, even after a restart. The same option is in the settings under "When opening".

With the mouse, the selection follows the pointer as soon as it rests on an entry for a moment, like in a menu. A click pastes the snippet or opens the folder; hold Shift for the other paste method. Entries in the right column can be clicked directly: a snippet is pasted right away, a folder opens. This way, hovering over a folder and clicking a snippet next to it pastes with a single click. The settings can switch clicking to "Click selects, double-click pastes". Right-clicking an entry, in either column, offers **Edit** (snippets only; like Ctrl+O, opens the [built-in editor](#editor)), **Open with default program** (the program Windows uses for the file type, e.g. Notepad++), **Show in Explorer** (like Ctrl+E for the entry under the pointer: a file is selected in its folder, a folder opens), **New snippet…** (in that folder, or next to that file), **Rename…** (also for folders, or F2 on the selected entry), **Clone…** (a copy in the same folder, with an editable name suggested like Explorer does: `Name - Copy.txt`) and **Delete…** (asks first, then moves the file to the Recycle Bin; where there is none, e.g. on a network drive, Windows asks whether to delete it permanently). The folder path above the list and the edit, Explorer and settings commands at the bottom are clickable too, and the popup can be dragged anywhere outside the search box.

## Pasting: two methods

- **Clipboard** (default): the text is placed on the clipboard briefly, then Ctrl+V is sent. Afterwards the previous clipboard content is restored, and the clipboard history (Win+V) is not cluttered.
- **Typing**: the text is sent character by character as Unicode input. This also works where pasting is blocked, but is slower for long texts.

Snippets with key tags (see [Forms](#forms-key-tags)) are always typed, whatever the setting, so text and keys arrive in the right order.

## Scripts

Everything between `{{` and `}}` is evaluated as JavaScript when pasting and replaced by its result. If your snippets contain `{{` that should be pasted as is (e.g. Mustache or Jinja templates), turn off "Run {{ … }} in snippets as JavaScript" in the settings (`EvaluateScripts` in `settings.json`).

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
| `window(app)` | Like [`{window,…}`](#other-programs-window): `window('crm')`. |

- All blocks of a snippet share their variables: `{{ var n = 3; '' }}` defines `n` for later blocks without printing anything. `undefined` and `null` print nothing either.
- There is no browser or Node.js: `alert`, `fetch`, `require` and the like don't exist, and `Intl` is only partly supported (hence `date()`). Scripts have no access to files or the network.
- `}}` always ends the block. A literal `{{` is written as `{{ '{{' }}`.
- If a block fails (syntax error, runs longer than 250 ms or uses too much memory), nothing is pasted and a message names the block. The text preview in the popup always shows the snippet as written.

**Writing a block with AI:** the **AI** button in the snippet editor or "Write a script block with AI…" in the tray menu opens a small prompt editor in its own window. It stays open after sending, so the prompt can be adjusted and sent again; Esc or "Close" closes it, and the text is kept for next time, even after a restart (`AiPromptText` in `settings.json`). As long as there is no text of your own, the field starts with one of ten ready-made examples, picked at random and selected, so typing replaces it; the light bulb next to the question goes on to the next one. An unchanged example is never saved, so your own text comes back the next time. Describe what the block should insert (in any language, e.g. "the date in 14 days with the weekday"), then click "Open in ChatGPT" or press Ctrl+Enter. QuickHasteBot adds the rules of the script engine, the helpers listed above and two examples, and opens the AI with the full prompt (it can be expanded in the editor). The answer comes as a code block: copy it with the code block's copy button and paste it under "Test the answer" in the prompt editor, where it runs right away and shows the result or the error. The circular arrow next to the heading (or F5) runs it again, handy for blocks with the time, random values or the clipboard. While the field is empty it shows an example, a decision die, and the arrow takes it over. "Copy block" then puts the finished block on the clipboard, ready to paste into a snippet (while an editor is open, "Insert into editor" puts it at the cursor there): code fences and a leading language label ("javascript") are removed, and if the AI left out the `{{ }}`, they are added. (Selecting text instead of using the copy button can lose characters that chat apps display as formatting or citations, such as backticks or `[1]`.)

"Edit template" creates `ai-prompt.txt` next to the exe with the built-in prompt and opens it in the editor. `{task}` stands for what you typed; changes apply the next time you send. "Reset template" (shown once the file exists) moves it to the Recycle Bin, and the built-in prompt applies again.

The service is chosen at the bottom left of the prompt editor or in the settings under "AI for scripts", and is remembered: ChatGPT (default, free without an account), Google AI Mode or Perplexity. Google AI Mode tends to show search results instead of code. Any other service can be entered by hand as `AiPromptUrl` in `settings.json`, with `{prompt}` for the prompt and `{lang}` for the interface language, e.g. `https://claude.ai/new?q={prompt}`; the settings then show it as "Custom address".

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
| `{guard,off}` `{guard,on}` | Turns the window guard (below) off or on from here |

Handy combinations: `{shift+tab}` goes back a field, `{alt+down}` opens a dropdown, `{ctrl+enter}` sends in Outlook, Teams and many web editors, `{ctrl+l}` jumps to the browser's address bar, `{alt+space}` opens the window menu, and `{win+r}{wait,300}notepad{enter}` starts a program. Ctrl+Alt+Del and Win+L can't be sent (Windows protects them), and Caps Lock and Num Lock are left out on purpose, since they would stay switched.

- **Line breaks** in the text are sent as Shift+Enter: a new line in a multi-line field or chat box that doesn't submit the form. Only `{enter}` submits. A single line break at the very end of the file is ignored.
- **With scripts:** tags and `{{ … }}` blocks can be mixed freely, e.g. `{{ user }}{tab}{{ date() }}{enter}`, and variables carry over as usual. Inside a script, `key()` and `wait()` press keys, e.g. `{{ ['Max', 'Müller'].join(key('tab')) + key('enter') }}`. What a script returns or what comes from the clipboard is never read as a tag, so a copied `{enter}` can't submit anything. A literal tag is written as `{{ '{tab}' }}`.
- **Stopping:** holding Esc stops a snippet between two steps.
- **Window guard:** if another window comes to the front while the snippet runs (e.g. you click elsewhere during a `{wait}`), it stops, so keys don't end up in the wrong program. Dialogs of the target window don't count. After a key that switches windows on purpose (`{win}` and `win+…`, `{alt}`, `alt+tab`, `alt+esc`, `alt+f4`, `alt+space`, `{apps}`, `shift+f10`), the guard is off for the rest of the snippet. `{guard,off}` turns it off explicitly, e.g. for `{guard,off}{win}{wait,500}Notepad{wait,300}{enter}`; `{guard,on}` turns it back on for the window that is in front at that moment. The setting "Stop when another window comes to the front" (`KeyTagsWindowGuard`) turns it off for all snippets.
- Turning off "Press key tags like {tab} and {enter}" in the settings (`EvaluateKeyTags` in `settings.json`) pastes the tags as is.

### Elements: focus, click, set

Instead of counting `{tab}`s, a snippet can address a field directly, in the window that is in front:

```
{focus,#firstname}Max{set,#lastname,"Müller"}{set,#country,"Austria"}{set,#terms,on}{click,name=Send;type=button}
```

| Tag | Does |
|---|---|
| `{focus,element}` | Puts the cursor into the field, typing continues there |
| `{click,element}` | Clicks a button, link, tab or checkbox; without anything to click, a real mouse click on it |
| `{set,element,"value"}` | Fills a field directly, picks a dropdown option by its text, sets a checkbox to `on`/`off`. Where that isn't possible (e.g. dropdowns in Firefox, autocomplete fields), it focuses the element and types the value instead |

The element is described like this, parts combined with `;`:

| Selector | Finds | In a browser |
|---|---|---|
| `#email` or `id=email` | the id | `id="email"` |
| `name=E-Mail` or just `E-Mail` | the label or text, not case sensitive | `<label>`, `aria-label`, a button's text |
| `.btn.primary` | all these classes | `class="btn primary large"` |
| `class=btn primary` | exactly this class attribute (in Windows programs: the window class) | |
| `type=edit` | the kind: `edit`, `button`, `checkbox`, `radio`, `combobox`/`dropdown`, `link`, `listitem`, `menuitem`, `tab`, … | |
| `index=2` | the second match | |
| `window=crm` | first bring this program to the front and search there, see below | |

QuickHasteBot uses UI Automation, Windows' accessibility interface, which Chrome, Edge and Firefox fill with the page's form, and Windows programs with their controls (in WinForms, `#name` is the control's name). Each element tag waits up to 5 seconds for the element to appear, e.g. while a page loads, so `{wait}` is rarely needed; on large pages the very first search takes a moment longer. If nothing turns up, the snippet stops with a message. Holding Esc stops the wait.

**Element picker:** "Pick element" in the snippet editor or "Pick element…" in the tray menu opens a small window that stays on top. Hold the mouse button on its crosshair, drag it onto a field, button or link (a red frame shows what is under the pointer, with its type, id, name, class and value) and let go. The picker then checks which selectors find exactly that element, the most stable first (a real id before the label, ids that web apps generate anew on every load, like `:r3:` or `mui-12345`, last), suggests focus, click or set depending on the element, and copies the tag to the clipboard, e.g. `{focus,#email}`. Another selector or action can be picked (their tooltips explain what each tag does), and the tag edited and copied again. **Test** runs the tag right away in the program the element was picked in, like a snippet would (so a click really clicks), and shows in the picker whether it worked or why not. With "Include the window" (remembered), the tag starts with `{window,process=…}` for the program it was picked in.

### Other programs: window

`{window,…}` brings a program to the front, and the rest of the snippet goes there. So a snippet works wherever you start it from:

```
{window,crm}{set,name=Search;type=combobox,"Müller"}{enter}
```

- `{window,crm}`: a program called `crm(.exe)`, or else a window whose title contains "crm". `{window,process=crm}` only matches the program, `{window,title=Customers*}` only the title (`*` stands for any text; without `*`, part of the title is enough). Case doesn't matter.
- If the program has several windows, the one used last wins. A minimized window is restored. If none turns up within 5 seconds (e.g. while the program starts), the snippet stops with a message. Holding Esc stops the wait.
- For a single tag, `window=` in the element does the same: `{set,name=Search;type=combobox;window=crm,"Müller"}`.
- The window guard follows: it now watches that window instead of turning off. `{guard,off}` still keeps it off.

**Programs running as administrator:** Windows doesn't let normal programs send keys to them or read their fields, and drops everything silently. QuickHasteBot doesn't need administrator rights and doesn't ask for them. Instead, it detects such a program before typing or pasting and says so, and the picker shows it while hovering. The fix is to start that program without administrator rights.

## Editor

"Edit" and Ctrl+O open text snippets in a built-in editor, "New snippet…" and Ctrl+N an empty one. One window per file; several can be open.

- **Name** at the top: changing it renames the file when saving. The extension stays (`.txt` for new snippets).
- **Insert tag ▾** offers keys, pauses, `{clear}`, the window guard, element and window tags, and script helpers like `{{ date() }}`, each with a short explanation. Placeholders are selected after inserting, e.g. `id` in `{focus,#id}`, so you can type over them.
- **Pick element** opens the [crosshair picker](#elements-focus-click-set). Its "Insert into editor" puts the tag at the cursor in the editor used last. The **AI** button opens the script prompt window, which can insert its block the same way.
- **Colors** show what will happen: key tags like `{tab}` in blue, element and window tags in purple with the selector in teal, `{{ … }}` scripts with keywords, strings, numbers and helpers colored. Plain text and anything in braces that isn't a tag, like `{name}` or JSON, stays uncolored. A known tag with wrong arguments, like `{tab,x}` or `{wait}`, is underlined in red and would be pasted as text; hovering it tells why. The colors follow the light or dark theme.
- **Preview** below the text shows what the snippet turns into while you type: scripts evaluated, keys as readable tags, and script errors in red. Nothing is sent.
- **Test** (F5): click into the target field afterwards, and the snippet runs there exactly as if pasted, with the text as it is in the editor, saved or not. While it waits for that click, a card next to the mouse pointer and a banner in the editor say what will run. Cancel or Esc stops the wait.
  - To debug one step at a time, select part of the snippet and only the selection runs. Tags and scripts cut off by the selection are included whole. Or put the cursor inside a tag or `{{ … }}` block and only that one runs.
  - The button says which: "Test", "Test selection", "Test tag" or "Test script".
  - A part that uses variables from an earlier script block fails, because that block doesn't run.
  - Holding Esc stops a running snippet.
- **Save** (Ctrl+S) keeps the file's encoding: ANSI (Windows-1252) stays ANSI, UTF-8 and UTF-16 stay as they were. Only text that ANSI can't hold (e.g. an emoji) turns an ANSI file into UTF-8 with BOM, so that nothing is lost. If the file was changed in another program in the meantime, the editor asks before overwriting. Closing with unsaved changes asks too.
- To keep using another editor, turn off "Edit snippets in the built-in editor" (`UseBuiltInEditor` in `settings.json`). The right-click menu offers both either way.

## Portable

Everything lives next to `QuickHasteBot.exe`:

- `structure\` with your snippets. The path can be changed in the settings (relative, absolute, or with `%USERPROFILE%`).
- `settings.json` with the settings. It is saved immediately on every change and can also be edited by hand (`"Language": "en"`, `"de"`, or empty for the Windows language; `"Theme": "System"`, `"Light"` or `"Dark"`, also under "Theme" in the settings and applied right away).
- `ai-prompt.txt` (optional) with your own prompt template for the AI, see [Scripts](#scripts).
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
