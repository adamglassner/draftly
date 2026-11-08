# Changelog

All notable changes to Draftly. Newest first.

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
