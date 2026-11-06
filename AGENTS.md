# AGENTS.md

Guidance for AI coding agents working in this repository.

## What this is

WowStopwatcher is a personal Windows desktop app that tracks time across multiple stopwatches with optional daily goals
(for example "English, goal 5h", plus "YouTube" and "Walking" without goals).

- **Only one stopwatch runs at a time.** Starting another stops the running one and logs its session.
- Every run is stored as a session: `Date | Stopwatch Name | Start | Stop`. All statistics are derived from sessions.
- Screens: **Today** (timeline, stopwatch rows, total, Stop all, goals), **Statistics** (one timeline per day,
  expandable per-stopwatch totals, select/clear days), **Settings** (autostart, stopwatches: name, color, goal,
  shortcut, order, delete; version and update check).
- **System tray**: icon with a menu listing stopwatches with live times, the active one highlighted, switch on click,
  Stop all, Open, Quit. Double-clicking the tray icon stops the current stopwatch and starts the next one in order.
- Global keyboard shortcuts (for example `Ctrl+F1`) start or stop a stopwatch while the app is in the background.

Sources of truth:

- Requirements: `D:\Personal\obsidian\01-Projects\OwnStopwatchTracker\Summary.md` (outside the repo, owner's notes).
- Visual design: claude.ai Design canvas "WowStopwatcher" (artboards `Main`, `Settings`, `Statistics`, `Tray`).
  Colors, spacing, radii and copy in the app follow that design.

## Stack

- .NET 10, C# (nullable enabled, implicit usings).
- UI: Avalonia 12 with the Fluent theme, MVVM via CommunityToolkit.Mvvm (source generators).
- Storage: SQLite through `Microsoft.Data.Sqlite` (no ORM). Database file: `%AppData%\WowStopwatcher\wowstopwatcher.db`.
- Tests: xUnit.

## Layout

```
src/
  WowStopwatcher.slnx
  Directory.Build.props    Shared build settings and <Version>.
  WowStopwatcher.Core/     Domain models, stopwatch engine, time formatting, timeline and statistics math. No UI, no I/O.
  WowStopwatcher.Data/     SQLite repositories and schema migrations. Depends on Core.
  WowStopwatcher.App/      Avalonia app: views, view models, theme, tray, global hotkeys, autostart. Depends on Core and Data.
  tests/
    WowStopwatcher.Core.Tests/
    WowStopwatcher.Data.Tests/
```

## Commands

```
dotnet build src/WowStopwatcher.slnx
dotnet test src/WowStopwatcher.slnx
dotnet run --project src/WowStopwatcher.App
```

The build must finish with no errors and no new warnings. Run the tests after any change to Core or Data.

## Releases

- Version lives in `src/Directory.Build.props` (`<Version>`); the release workflow overrides it from the tag.
- `dotnet publish src/WowStopwatcher.App -c Release` produces a self-contained single-file `WowStopwatcher.exe` (win-x64).
  The publish-only settings are in the App csproj under `_IsPublishing`; `build` and `run` are unaffected.
- `.github/workflows/ci.yml` builds and tests on push/PR to `develop` and `main`.
- `.github/workflows/release.yml` runs on a `vX.Y.Z` tag: tests, publishes, zips `WowStopwatcher-X.Y.Z-win-x64.zip`
  (exe + LICENSE) with a `.sha256`, and creates the GitHub Release. A tag with a hyphen becomes a pre-release.
- Agents never create tags or releases; the owner does.

## Conventions

- Keep logic in Core so it can be unit tested. View models call Core; views only bind.
- Time comes from `TimeProvider` (injected), never `DateTime.Now` directly, so tests can control the clock.
- Store timestamps as UTC; group sessions into days by **local** date of the session start.
- A running stopwatch is a session with no stop time. On normal quit, running sessions are stopped and saved.
  After a crash, an open session is resumed on the next start.
- Deleting a stopwatch is a soft delete: its past sessions stay in statistics with their name and color.
- Design tokens (colors, radii, fonts) live in one resource dictionary in the App project. Do not hard-code
  colors in views; reference the tokens.
- Fonts: Inter (via `Avalonia.Fonts.Inter`) for text; `Cascadia Mono, Consolas` for times and numbers.
- One public type per file, file name matches the type. Follow existing patterns before adding new ones.
- Do not add packages unless needed; prefer the BCL.

## Rules for agents

- Make the smallest change that does the task. Do not refactor unrelated code.
- Do not guess APIs. Avalonia 12 changed several APIs from 11; verify against the package or compile before relying on one.
- Do not commit, push, or create branches unless the owner asks.
- Never delete or overwrite the owner's data (the SQLite database, settings) during development or tests;
  tests use temporary databases.
- Report what you changed, how you verified it, and anything left open.
