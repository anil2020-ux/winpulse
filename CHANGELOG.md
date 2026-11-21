# Changelog

All notable changes to WinPulse are documented here. Versions follow `MAJOR.MINOR.PATCH`.

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
