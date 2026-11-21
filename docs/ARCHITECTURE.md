# Architecture

WinPulse is a single-project WPF application (.NET 8) with a small, explicit layering:

```
src/WinPulse
├── App.xaml(.cs)          composition root, CLI entry points, global exception handling
├── MainWindow.xaml(.cs)   custom chrome, navigation shell, status bar, update overlay
├── Core/
│   ├── AppHost.cs         service composition (one instance per process)
│   ├── AppPaths.cs        %LOCALAPPDATA%\WinPulse layout, install-dir detection
│   ├── AppInfo.cs         product/version/GitHub constants
│   ├── FileKinds.cs       extension → category, screenshot heuristics, glyphs
│   ├── Formats.cs         byte/date/duration formatting
│   ├── DbTime.cs          ISO-8601 UTC timestamps for SQLite
│   ├── KnownFolders.cs    shell known folders (OneDrive-aware)
│   ├── ThemeManager.cs    dark/light palette switching at runtime
│   ├── SelfTest.cs        headless 26-check verification suite
│   ├── UpdateCli.cs       --check-updates / --update
│   └── Converters.cs      small XAML value converters
├── Models/                plain data used by the UI and services
├── Services/
│   ├── Database.cs        SQLite schema, batched upserts, queries, settings
│   ├── FileIndexer.cs     scan orchestration, incremental diffs, watcher debounce
│   ├── FileScanner.cs     fast recursive walker with skip lists
│   ├── FileOperations.cs  recycle-bin delete, rename, move, copy, open, reveal
│   ├── PreviewService.cs  decides preview kind, loads text safely
│   ├── SystemInfoService.cs  WMI hardware inventory (cached)
│   ├── PerformanceService.cs Win32 CPU/RAM/process counters
│   ├── ProcessService.cs  process list + CPU deltas
│   ├── InstalledAppsService.cs  uninstall registry reader
│   ├── CleanupService.cs  large/stale/duplicate candidates
│   ├── UpdateService.cs   GitHub releases check/download/staged apply
│   ├── SettingsService.cs typed preferences + HKCU startup entry
│   └── ShellIconService.cs shell icons per extension (cached)
├── ViewModels/            MainViewModel + one view model per page
├── Views/                 XAML pages + prompt dialog
├── Controls/Sparkline.cs  GPU-friendly area chart
└── Themes/                palette + control styles
```

## Data model

`files` is the core table:

| column | notes |
|---|---|
| `path` | `COLLATE NOCASE UNIQUE` — Windows paths are case-insensitive |
| `root`, `root_label` | source folder and its friendly name |
| `ext`, `category`, `is_screenshot` | derived at scan time |
| `size`, `created`, `modified` | from `FileInfo` |
| `first_seen`, `last_seen` | UTC strings; drive incremental diffs |
| `is_deleted`, `deleted_at` | soft-delete so "removed" events survive |
| `is_protected` | excluded from cleanup suggestions |

Queries are parameterised; search escapes `%`, `_` and `\` and uses `LIKE ... ESCAPE '\'`.

## Scanning

1. For each enabled, existing root: take a `scanStart` timestamp.
2. Walk the tree iteratively (no recursion depth risk), skipping reparse points, system directories and a noise list (`node_modules`, `.git`, `AppData`, …).
3. Upsert records in batches of 2,000 inside one transaction; `ON CONFLICT(path)` refreshes metadata and resurrects rows that reappeared.
4. Mark rows whose `last_seen < scanStart` as deleted (only when the root completed — cancellation skips this step).
5. Emit `added`/`removed` events for the activity feed.
6. `FileSystemWatcher` per root debounces 20 s and triggers an incremental scan.

## Safety rules

- No permanent deletes: `SHFileOperation(FO_DELETE | FOF_ALLOWUNDO)`.
- Every destructive action is confirmed in a custom dialog.
- Protected files are filtered out of cleanup queries.
- The updater refuses to run for portable builds and only writes inside the install folder.

## Updates

`UpdateService` calls `GET /repos/{owner}/{repo}/releases/latest` (ignores drafts/prereleases by definition of "latest"), picks a `*win-x64*.zip` asset, downloads it with byte progress, extracts to `%LOCALAPPDATA%\WinPulse\update\staging` and writes a temporary `.cmd` helper that waits for the current PID to exit, `robocopy /MIR`s the staging folder into the install folder, relaunches `WinPulse.exe` and deletes itself. Release artifacts are produced by `.github/workflows/ci.yml` on `v*` tags.
