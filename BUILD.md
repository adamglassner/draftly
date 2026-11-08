# Building Draftly 1.0.0

Draftly is a C# / WPF app on .NET 8. The build produces one self-contained `Draftly.exe`, so people who install it do **not** need .NET, Python or Node. The build is followed by a normal Windows setup program made with Inno Setup.

## What you need (one-time, on the Windows PC that builds it)

Open **PowerShell** and run:

```powershell
winget install Microsoft.DotNet.SDK.8
winget install JRSoftware.InnoSetup
```

Close and reopen PowerShell afterwards so the new commands are found. Check with `dotnet --version` (it should print `8.0.x`).

## Build everything (exe + installer)

From the `Draftly` folder (the one with `build.ps1` in it):

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

When it finishes, the `dist` folder has:

| File | What it is |
|---|---|
| `dist\Draftly.exe` | The app. It runs on its own, with no install needed. |
| `dist\Draftly-1.0.0-win-x64.zip` | The portable download (just the exe, zipped) |
| `dist\Draftly-1.0.0-Setup.exe` | The installer to give to people |

## Or build the two steps by hand

**1. The exe**

```powershell
dotnet publish src\Draftly\Draftly.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist\publish
```

**2. The installer** (needs step 1 first)

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\Draftly.iss
```

If you installed Inno Setup for your user only, the compiler is at `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe` instead.

## Testing

`Draftly.exe --self-test result.txt screenshots` runs Draftly's built-in test. It creates and formats a document, inserts a picture, saves it as RTF, reopens it and checks every piece of formatting survived. It also checks Light, Dark and Follow System for readable contrast and that the choice persists, plus Find/Replace, Undo/Redo, keyboard shortcuts, the clipboard, the save prompt, plain text and Word import, Print Preview, view options and every dialog. It writes PASS/FAIL lines to `result.txt` and screenshots to `screenshots`. It uses a temporary settings folder, so your own settings aren't touched.

GitHub runs this on every push, along with an installer test: install to Program Files, check the Start Menu shortcut, uninstall entry, file types and icon, start the app, then uninstall and check nothing is left behind.

## Releasing a new version

1. Change `<Version>` (and `<InformationalVersion>`, `<AssemblyVersion>`, `<FileVersion>`) in `src\Draftly\Draftly.csproj`, and `MyAppVersion` in `installer\Draftly.iss`, e.g. to `1.0.1`.
2. Add a `## 1.0.1 - <date>` section at the top of `CHANGELOG.md`. The release notes and Settings › About › What's new come from it, and the release stops if it's missing.
3. Push to `main`.

GitHub then builds and tests it. If every test passes and there's no `v1.0.1` release yet, it publishes Release v1.0.1 with the Setup .exe and portable zip (pushing a tag like `v1.0.1` does the same). The Bytehawk Studio Launcher picks up the new version from there.

## What the installer does

- Installs to `C:\Program Files\Draftly`
- Adds a Start Menu shortcut, plus a Desktop shortcut if you tick the box
- Adds an uninstaller under **Settings › Apps › Installed apps** (named "Draftly 1.0.0")
- Has two optional checkboxes (off by default) for `.rtf` and `.txt` files. If you tick them, Draftly is added to those files' **Open with** list, and Setup then opens Windows' Default apps page for Draftly. Windows 10 and 11 don't let any installer silently change your default apps, so you confirm the change there with one click.
- Uninstalling removes the program and its registry entries. It doesn't touch your documents or your settings in `%APPDATA%\Draftly`.

## Icon files

`assets\draftly.ico` contains 16, 32, 48 and 256 px sizes. `assets\draftly-icon-source.png` is the 1024 px source. The icon is built into Draftly.exe, so the exe, the Start Menu shortcut and the installer all show the same icon.

## Building in the cloud instead

`.github/workflows/build.yml` builds and tests everything on GitHub's Windows servers on every push. Download the results from the run's **Artifacts**, or from **Releases** for tagged versions.

## Where Draftly keeps things

| What | Where |
|---|---|
| Settings | `%APPDATA%\Draftly\settings.json` |
| Auto-recovery copies | `%LOCALAPPDATA%\Draftly\Recovery` |
| Error log (only if something crashes) | `%APPDATA%\Draftly\error.log` |
