# Changelog

All notable changes to WinPulse are documented here. Versions follow `MAJOR.MINOR.PATCH`.

## [1.1.0] - 2026-11-21

The "pro" update: background mode, instant navigation and visual analytics.

### Added
- **System tray mode**: a tray icon with a context menu (open, scan, pages, exit), close-to-tray behaviour and `--minimized` support. Closing the window now keeps WinPulse tracking.
- **Tray notifications** for new large files (>100 MB), low disk space, and background mode.
- **Ctrl+K command palette**: fuzzy search pages, actions and indexed files; arrow keys + Enter, or double-click. Also opens with `--palette`.
- **Screenshots gallery** page with thumbnail grid, time filters, full preview, per-file actions, and "delete older than 30 days".
- **Largest folders** analytics card on the dashboard.
- **CSV export** of the currently filtered file list.
- **Bulk duplicate resolution** in Cleanup: keep the newest copy of each group, recycle the rest.

### Changed
- Status bar advertises Ctrl+K and shows the new navigation entry.
- Settings gained a "keep running in the tray" switch.

### Fixed
- `--view=screenshots` and other short aliases for the launch flag.

## [1.0.2] - 2026-11-21

Cleanup reaches feature parity with the File activity explorer.

### Added
- Shared preview pane in **Cleanup**: images, text/code, PDFs (WebView2), audio and video, plus file metadata.
- Per-file actions in Cleanup: open, reveal, rename, copy to folder, move to folder, protect and recycle-bin delete.
- Double-click a cleanup suggestion to open it.
- Preview toggle in the Cleanup toolbar.

### Changed
- Delete/protect in Cleanup now act on the current selection (or the highlighted row) instead of silently using the whole list.

### Fixed
- WebView2 PDF preview no longer throws when the source changes to null (shared preview control navigates from code-behind).

## [1.0.1] - 2026-11-21

Maintenance release that validates the in-app update pipeline end to end.

### Fixed
- Installer console output is now ASCII-safe on non-UTF-8 terminals.
- Cleanup header now shows reclaimable space instead of repeating the suggestion count.
- The updater no longer purges extra files (such as `uninstall.ps1`) from the install folder.
- PDF preview no longer throws when switching between files with no PDF selected.

### Verified
- WinPulse 1.0.0 detects this release, downloads it in-app and replaces itself using the staged updater.

## [1.0.0] - 2026-11-21

First public release.

### Added
- **Dashboard** with live CPU/RAM/disk gauges, sparklines, hardware inventory (CPU, GPU, RAM modules, motherboard, BIOS, OS, drives), storage-by-category and recent activity.
- **File activity explorer** backed by a SQLite index: background scanning of Downloads, Desktop, Documents, Pictures and Videos, plus user-added folders.
- Filters for category, date, size, source folder, screenshots and installers; free-text search; five sort orders; incremental scans with new/removed file events.
- **In-app previews** for images, text/code, PDFs (WebView2), audio and video, with metadata and open-with-default fallback.
- File operations everywhere: open, reveal, copy path, rename, copy to folder, move to folder, recycle-bin delete and protect.
- **Processes** view with live CPU/memory, publisher, start time, end task and reveal.
- **Apps** view reading installed programs from the registry plus downloaded installers in tracked folders, with uninstall and installer cleanup.
- **Cleanup** view that finds large files, stale downloads, old screenshots and likely duplicates (size + SHA-256 head hash), with bulk recycle-bin delete and protect.
- **In-app GitHub updates**: release check, changelog, download with progress, staged install and restart.
- Dark and light themes, custom window chrome, keyboard shortcuts (`Ctrl+F`, `Ctrl+R`, `F2`, `Delete`, `Enter`).
- `--selftest` headless verification suite, `--check-updates` and `--update` CLI hooks.
- Self-contained single-file Windows x64 packaging, per-user installer, uninstall entry and GitHub Actions CI/release automation.

### Notes
- Portable builds run anywhere but only installed builds support self-update.
- Everything stays local; the only outbound requests are GitHub release checks/downloads.
