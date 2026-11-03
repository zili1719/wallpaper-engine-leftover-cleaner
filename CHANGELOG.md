# Changelog

All notable changes to this project. Format follows [Keep a Changelog](https://keepachangelog.com/),
versioning follows [Semantic Versioning](https://semver.org/).

## [1.0.0] — 2026-11-03

First public release.

### Added

- Orphan classification across four signals: `WorkshopItemsInstalled`,
  `WorkshopItemDetails.subscribedby`, the filesystem, and Wallpaper Engine's own `config.json`
  (`wproperties` + `explore.favorites` + `config_backups`).
- Nine status levels — Subscribed / Protected / In favorites / Backup hint / Unsubscribed /
  No owner / Suspicious / Download fragment / Index only.
- Automatic discovery of every Steam library on the machine (registry + `libraryfolders.vdf`,
  multi-drive), plus `--lib` for manual roots.
- Quarantine-first deletion with a per-batch `manifest.json`, one-command restore, and verified
  cross-volume copies (`.partial` sibling, file-count and byte-size comparison before the source
  is removed).
- Keep-list (`keep add`) that outranks every heuristic.
- GUI: sortable result grid, colour-coded status, preview pane, full ACF signal breakdown,
  context menu (keep / reveal / workshop page / copy ID / copy path).
- Tray watcher: monitors every library's `AppWorkshop` ACF and rescans on change.
- CLI: `scan` (table + `--json`), `quarantine`, `batches`, `restore`, `purge`, `keep`, `paths`,
  `tray`, `shellopen`.
- Recycle-bin-first permanent deletion, `--hard` to bypass.
- `make-fixture.ps1` regression fixture covering six classification cases; CI runs the full
  quarantine → restore → purge cycle on every push.

### Fixed during development

- **GDI+ `Generic Error` when selecting rows with GIF previews.** `Image.FromStream` returns an
  image that still references the stream; the `using` block closed it, and the picture box's
  animator then failed in `SelectActiveFrame`. Previews are now decoded into an independent
  `Bitmap`.
- **Dock z-order overlapped the content area.** The content panel was docked before the toolbar and
  status bar were laid out, so the top of the results pane was hidden underneath the toolbar.
- **Data directory resolution was racy.** A fixed-name writability probe made concurrent processes
  disagree about where `settings.json` lived. Probes now use unique names.
- **`explorer.exe - 0xc0000142` on file reveal.** Spawning a new explorer process per reveal could
  fail during loader initialisation. Reveal now tries `SHOpenFolderAndSelectItems` →
  `ShellExecute("open")` → `explorer.exe /select` (opt-in), and never fails silently.
- **`SplitterDistance` crash on startup** when minimum panel sizes exceeded the initial control
  size.

[1.0.0]: https://github.com/zili1719/wallpaper-engine-leftover-cleaner/releases/tag/v1.0.0
