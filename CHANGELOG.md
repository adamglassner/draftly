# Changelog

All notable changes to Draftly. Newest first.

## 1.2.1 - 2026-11-11

### Added
- **Automatically hide toolbar** (ribbon interface), like Windows 11 Paint. The new arrow button at the top right gives the choice between "Always show toolbar" and "Automatically hide toolbar".
  - When the toolbar is hidden, only the Home and View tab names stay at the top and the page gets the extra room.
  - Click a tab to drop the ribbon down over the page. It hides again after you use a command, click the page, press Esc or switch to another window.
  - Ctrl+F1 or double-clicking a tab also switches it, and there's a setting in Settings › Appearance.
- A self-test for the hidden toolbar.

## 1.2.0 - 2026-11-08

The features Windows 11 Notepad added, plus line numbers and default-app setup.

### Added
- **Tabs:** every document gets its own tab in one window, with its own undo history. Use Ctrl+T or Ctrl+N for a new tab, Ctrl+W to close one, Ctrl+Tab and Ctrl+Shift+Tab to switch, Alt+1…9 to jump to a tab and Ctrl+Shift+N for a new window. Middle-click a tab to close it. A dot marks tabs with unsaved changes. Tabs can be turned off in Settings to work one document at a time, like WordPad.
- **Opening files:** files opened from File Explorer join the Draftly window that's already open as new tabs. Opening a file that's already open switches to its tab. Open several files at once, or drag them onto the window. Dropping a picture onto the page inserts it.
- **Sessions:** Draftly reopens your tabs next time. Optionally, like Notepad, it can also keep unsaved changes when you close it and bring them back exactly as you left them.
- **Headings and styles:** Title, Heading 1–3, Quote and Code, from the new Styles button, the right-click menu or Format › Style. Shortcuts are Ctrl+Alt+1/2/3, Ctrl+Alt+T for Title and Ctrl+Alt+0 for normal text. Pressing Enter after a heading goes back to normal text. Styles are saved as ordinary formatting, so they look right in WordPad and Word too.
- **Markdown shortcuts while typing:** at the start of a line, type `#`, `##` or `###` for a heading, `-` or `*` for bullets, `1.` for numbering or `>` for a quote, then a space. You can turn this off in Settings.
- **Links:** insert or edit a link with Ctrl+K, Insert › Link or the right-click menu, and open one with Ctrl+click. The status bar shows where the link at the cursor goes. Links are saved in RTF.
- **Markdown files (.md):** headings, bold, italic, strikethrough, lists, quotes, code, links and tables open formatted. Switch to the source text and back with Ctrl+Shift+M or the status bar, and save back as Markdown. You're warned before anything Markdown can't store (pictures, colours, centred text) would be left out.
- **Table editing:** insert rows above or below, insert columns left or right, and delete rows, columns or the whole table, from the right-click menu or Insert › Table.
- **Line numbers** in the page's margin (View tab, View menu, Settings, or Ctrl+Alt+L). They're shown on screen only and aren't printed.
- **Default app:** Settings › Files and saving shows whether .rtf, .txt and .md files open in Draftly, and a "Make Draftly the default…" button. It adds Draftly to "Open with" and opens Windows Settings, where one click confirms it (Windows doesn't let apps make themselves the default). Draftly also offers this once in its info bar.
- A character count in the status bar.
- Line endings (Windows CRLF, Unix LF, old Mac CR) are detected, kept when you save plain text or Markdown, shown in the status bar and can be changed.
- F5 inserts the time and date, as in Notepad.
- Save all, for saving every tab with unsaved changes.
- The installer can also register Markdown (.md) files, and uninstalling removes the "Open with" entries Draftly added.
- Self-tests for everything above.

### Not included
- Notepad's Copilot and AI features. Draftly works entirely offline.

## 1.1.1 - 2026-11-08

### Fixed
- The large toolbar buttons now line up. Picture, Date and time and Object (and every other large button) keep their icons at the same height, even when a label wraps onto two lines.

### Added
- This changelog. It's shown in Settings › About ("What's new") and in each GitHub release.

## 1.1.0 - 2026-11-08

### Added
- **Classic interface:** a menu bar (File, Edit, View, Insert, Format, Help) with a standard toolbar and a formatting toolbar, like older WordPad and Word, as an alternative to the ribbon. Both have every command.
- Switch between them in Settings › Appearance › Interface, with **Classic menus** on the ribbon's View tab, or with View › Switch to the ribbon. Your choice is remembered.
- The built-in self-test now covers the Classic interface: its menus, toolbars, button states and switching back and forth.

### Changed
- The View tab and the Classic toolbars fit on a 1024-pixel-wide screen.

## 1.0.0 - 2026-11-08

First release.

- WordPad-style layout with a modern Windows 11 look: ribbon with Clipboard, Font, Paragraph, Insert and Editing groups, the page in the middle and a status bar with word count, page/line and a zoom slider.
- Rich Text (.rtf) as the native format, plus plain text (UTF-8, UTF-8 with BOM, ANSI, UTF-16). Word (.docx) files open through Microsoft's Open XML SDK and are never overwritten.
- Light, Dark and Follow System themes, including a dark page in Dark mode, seven accent colours, and the choice remembered.
- Fonts, sizes, bold/italic/underline/strikethrough, subscript/superscript, text colour, highlight, bullets and numbering, alignment, line spacing, indents (also draggable on the ruler) and a Paragraph dialog.
- Insert pictures, date and time, tables, symbols, lines, page breaks and text from another file.
- Find and Replace, Undo and Redo, Print, Print Preview and Page Setup.
- Asks to save before closing or opening another document, plus auto-recovery after a crash and crash-safe saving.
- RTF compatible with WordPad and Word, including subscript, superscript and line spacing.
- Settings beyond WordPad's: default font, units, zoom, word wrap, spell check, page defaults, auto-recovery, text encoding, backups, recent files and window memory.
- Installer: Program Files, Start Menu shortcut, optional desktop shortcut, optional .rtf/.txt file types and an uninstaller. Also a portable zip and the Bytehawk Studio Launcher.
