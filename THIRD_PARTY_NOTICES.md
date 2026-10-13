# Third-party notices

## madeyexz/louisa-wifi

- Source: https://github.com/madeyexz/louisa-wifi
- Examined commit: `ca8f59e3708f8f8208084a0b63013be6d41cc04d` (upstream HEAD checked 2026-10-14, Asia/Taipei).
- Copyright (c) 2026 Ian Hsiao. MIT. Full original text is preserved in `LICENSE`.
- `data/stores.json`: adapted from upstream data; wrapped in a versioned envelope and assigned stable store IDs. Original store names, addresses, phone numbers, coordinates and password candidates retained.
- `scripts/fetch_stores.py`: adapted scraper/parser/telephone rules. Adds identity retention, deduplication by name plus address, preservation of branches without phone numbers, validation, disappearance guard, atomic replacement, offline validation and tests.
- Behavior and architecture references: `Joiner.swift`, `Locator.swift`, `MenuController.swift`, `Tracker.swift`, `StatsView.swift`, `Updater.swift`. C# Windows implementation written separately; no Swift binary or macOS API is shipped.
- Upstream images and application icons are not redistributed.

## Microsoft .NET 10

Self-contained packages redistribute the .NET runtime, WPF, Windows Forms and Windows SDK .NET projection components. The exact SDK's .NET and Windows Desktop license/notice files are copied to the published directory as `DOTNET-LICENSE.txt`, `DOTNET-THIRD-PARTY-NOTICES.txt`, `WINDOWSDESKTOP-LICENSE.txt` and `WINDOWSDESKTOP-NOTICES.txt`. Source and terms: https://github.com/dotnet/runtime, https://github.com/dotnet/wpf, https://github.com/dotnet/winforms, https://github.com/microsoft/CsWinRT.

## Inno Setup

The installer is compiled using Inno Setup 6.7.3 (Jordan Russell / Martijn Laan). Compiler license and download: https://jrsoftware.org/isinfo.php. Inno Setup itself is not included in the source repository or ZIP. The setup executable contains its installer engine. Non-commercial open-source build.
