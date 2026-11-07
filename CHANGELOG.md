# Changelog

Release notes from v2.8.2 on are on the [GitHub releases page](https://github.com/opuslux/QuickHasteBot/releases). This file covers the versions before the public release.

## 2.8.1 (2026-11-06)

- Restructured README, the popup hotkey is clearly configurable

## 2.8.0 (2026-11-06)

- Icons in the popup and all right-click menus, and the shortcut bar shows every key as a keycap
- Document icon right of the pin in the popup opens an empty snippet in the editor
- Filter line in the editor's "Insert tag" menu: type to list matching tags, Enter inserts the first
- `{start,…}` starts a program if it isn't running, waits for its window and continues the snippet there
- Filter toggle next to the pin keeps the search text between popup openings, Enter starts the same entry again
- Folder picker in the editor creates new snippets in another folder or moves existing ones when saving
- Fix: Search box no longer jumps while typing with a vertically centered popup position

## 2.7.0 (2026-11-05)

- Dark mode for all dialogs, a theme setting (System, Light, Dark) and "Show in Explorer" in the right-click menu
- Element picker and AI prompt move from the popup to the snippet editor, and the AI prompt gets a tray entry
- Global hotkeys for files and folders, stored in the name like "Kind regards [Ctrl+Alt+G].txt"

## 2.6.0 (2026-11-05)

- Re-run button and examples in the AI prompt window, example structure on first start
- Rearranging the structure folder in Explorer no longer leaves a stale popup
- Key tags like `{tab}` and `{enter}`, also via `key()` in scripts, let snippets fill in forms in browsers and Windows apps
- Element tags `{focus}`, `{click}`, `{set}` and `{window}` fill in form fields by id, label or class via UI Automation, in any program, with a crosshair picker that copies the tag
- Built-in snippet editor with syntax highlighting, tag palette, element picker, live preview and a test run that sends the unsaved text to the next clicked field

## 2.5.0 (2026-11-04)

- Alt+1 to Alt+9 open the first nine entries, numbered in the list
- Choose where the popup opens relative to the mouse, settings in two tabs
- Snippets can contain JavaScript in `{{ … }}`, evaluated when pasting
- Right-click menu in the popup to edit, rename, clone and delete snippets
- Sparkle icon in the popup lets an AI write script blocks, with a test field and editable prompt

## 2.4.0 (2026-11-03)

- Second column with folder contents and text preview, selection follows the mouse

## 2.3.0 (2026-11-03)

- User interface in English and German

## 2.2.0 (2026-11-03)

- Settings with a robot video and a two-column layout
- Edit, Explorer and Settings can be clicked in the popup
- New robot head icon, also left of the search box in the popup
- Fix: The version shown comes from the latest git tag, also for local builds

## 2.1.0 (2026-11-03)

- More compact popup that adapts to the list and can be moved
- Standalone variant with the .NET runtime built in, attached to every release

## 2.0.0 (2026-11-02)

- QuickHasteBot: rebuild of QuickPasteBot with .NET and WPF
- The popup can be used with the mouse, single or double click selectable
