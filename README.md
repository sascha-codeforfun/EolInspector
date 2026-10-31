# EOL Inspector

Shows the line-ending style and byte-order mark of every text file in a folder.

A small dark-mode Windows tool (WPF, .NET 10) for spotting LF vs CRLF mix-ups and stray BOMs across a source tree.

It only reads. Nothing is written anywhere: no settings file, no log, nothing in AppData. Every start begins from the defaults below.

## What it does

Pick a folder, click **Scan**, and every matching text file is listed with its byte-order mark (BOM), its line-ending style and the raw count of each terminator, so a `Mixed` file also tells you *how* mixed it is.

| Column    | Shows |
|-----------|-------|
| File      | Path relative to the chosen folder |
| BOM       | `UTF-8`, `UTF-16 LE/BE`, `UTF-32 LE/BE` or `None` (dimmed, so files that do carry a BOM stand out) |
| EOL style | `CRLF`, `LF`, `CR`, `Mixed` or `None`, colour-coded: Mixed in red, CR in amber, LF in the accent colour, None dimmed |
| CRLF / LF / CR | Number of each terminator; zeros are dimmed |
| Size      | Size in bytes, with the system's thousands separator |

Every column can be sorted by clicking its header (Size sorts numerically). Several rows can be selected at once.

**Double-click a row** to open that file through the Windows "Open with" picker, so you choose the app each time rather than launching the default.

## Options

- **Include subfolders** – off by default, which scans only the top level of the chosen folder. When on, the whole tree is walked; a subfolder that can't be read is skipped and the rest of the scan carries on.
- **Extensions** – comma-separated list; only files with these extensions are scanned. The leading dot is optional and matching ignores case. Leave the box empty to scan every file. **Reset** restores the default:
  `.cs, .xaml, .txt, .json, .js, .ts, .html, .css, .xml, .config, .md, .sh`
- **Exclude folders** – comma-separated folder *names* (not paths) to skip, matched case-insensitively. An excluded folder is skipped together with everything beneath it; the chosen folder itself is always scanned. Only active when *Include subfolders* is ticked. Leave empty to exclude nothing. **Reset** restores the default:
  `bin, obj, .git, .vs, node_modules, packages`

## Scanning

The scan runs in the background, so the window stays responsive. It first lists the matching files ("Enumerating files…"), then analyses them with a progress bar and a running count in the status bar of files shown and skipped.

While a scan is running the **Scan** button becomes **Cancel**; cancelling keeps the results gathered so far. When the scan ends the status bar reports how many files were shown, scanned and skipped.

Skipped files are:

- **binary** – any file containing a NUL byte, and
- **unreadable** – locked, access denied, and so on.

Each file is read whole into memory, so very large files take a moment.

## How detection works

The file is read as raw bytes, not as text, so nothing normalises the line endings before they are counted. Walking the bytes:

- `\r` followed by `\n` → one CRLF
- lone `\n` → one LF (Unix)
- lone `\r` → one CR (classic Mac)

A file using exactly one of these is labelled accordingly; more than one gives `Mixed`; none gives `None` (a single line with no terminator, or an empty file).

The BOM comes from the first bytes of the same read:

| Bytes         | BOM       |
|---------------|-----------|
| `EF BB BF`    | UTF-8     |
| `FF FE`       | UTF-16 LE |
| `FE FF`       | UTF-16 BE |
| `FF FE 00 00` | UTF-32 LE |
| `00 00 FE FF` | UTF-32 BE |
| anything else | None      |

Longer marks are checked before shorter ones, since `FF FE` (UTF-16 LE) is the start of `FF FE 00 00` (UTF-32 LE). In practice you'll only see `UTF-8` or `None`: UTF-16 and UTF-32 files are full of NUL bytes, so they trip the binary check and are skipped. The wider encodings are still recognised so the value stays correct if that rule is ever relaxed. BOM bytes contain no CR or LF, so they never affect the line-ending counts.

## Look

The app uses the HueTheme kit in its Cyan family (`Theme/HueTheme.xaml` and `Theme/HueThemeKit.cs`), shared with the other apps in the suite. Everything it draws is dark: the header, buttons, text boxes, checkboxes, the results grid, scrollbars, tooltips, context menus, the progress bar, the message dialog and the native title bar (Windows 10 1809 and later, Windows 11). It also follows Windows' high-contrast setting.

The header shows the app's version, the SDK it was built with and the .NET runtime it runs on. The status bar's dot shows the state at a glance: grey before a folder is picked, cyan while scanning, green when done, amber for a cancel or warning, red for an error.

Two dialogs are drawn by Windows itself and follow the system's own light/dark setting instead: the folder picker and the "Open with" picker.

## Building

Requires the .NET 10 SDK on Windows. No NuGet packages are used.

```
dotnet publish EolInspector.csproj
```

Output: `bin\Release\net10.0-windows\win-x64\publish\EolInspector.exe`

The project file already sets everything for a single self-contained exe, so no extra command-line switches are needed:

- `win-x64`, self-contained, single file
- native libraries bundled inside the exe
- single-file compression on
- no debug information (`DebugType` is `none`), so no `.pdb` is produced and the build is reproducible
- no commit hash appended to the product version (`IncludeSourceRevisionInInformationalVersion` is `false`), so a build from a git clone matches one from a zip
- a guard target (`RejectPathHashedTypeNames`) that fails the build if a type name derived from the build folder's path reaches the exe
- the version (`<Version>` in the csproj) and the SDK used are stamped into the exe and shown in the header

The target machine does not need .NET installed.

The win-x64 runtime packs (`Microsoft.NETCore.App.Runtime.win-x64` and `Microsoft.WindowsDesktop.App.Runtime.win-x64`) must be available to the SDK, either online or from a local package cache or feed on an offline builder.

To work on it interactively, open `EolInspector.csproj` in Visual Studio 2026 (or any IDE with the .NET 10 SDK) and press F5.

## Icon set

| File          | Used for |
|---------------|----------|
| `app.ico`     | Exe icon and window icon |
| `app_256.png` | Image in the header block, next to the app name and description |

Both sit in the project root and are embedded at build time. Replace them with new artwork whenever needed, keeping the same file names.

## Project layout

```
EolInspector/
├─ EolInspector.csproj
├─ App.xaml(.cs)                   Application entry; merges the theme and follows high contrast
├─ app.ico, app_256.png            Icon set
├─ LICENSE.txt                     MIT license
├─ Theme/
│  ├─ HueTheme.xaml                Palette and styles for every control (Cyan family)
│  └─ HueThemeKit.cs               Title bar, message dialog, version info, high contrast
├─ Views/
│  └─ MainWindow.xaml(.cs)         Header, folder and filter inputs, results grid, status bar
├─ Models/
│  ├─ FileEolResult.cs             One results row
│  ├─ ScanProgress.cs              Progress snapshot sent to the UI
│  └─ ScanOutcome.cs               Final scan result (rows, counts, cancelled flag)
├─ Services/
│  ├─ FileScanner.cs               Folder walk, extension and folder filters, progress, cancellation
│  └─ EolAnalyzer.cs               Per-file BOM and line-ending detection
└─ Interop/
   └─ ShellOpenWith.cs             Windows "Open with" dialog (SHOpenWithDialog)
```

## License

MIT, see `LICENSE.txt`.
