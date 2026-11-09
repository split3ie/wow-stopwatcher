# AGENTS.md

Guidance for AI coding agents working in this repository.

## What this is

WowStopwatcher is a personal desktop app for Windows, macOS and Linux that tracks time across multiple stopwatches with optional daily goals
(for example "English, goal 5h", plus "YouTube" and "Walking" without goals).

- **Only one regular stopwatch runs at a time.** Starting another stops the running one and logs its session.
- A stopwatch can be marked **independent**: it runs alongside the regular one and other independent ones,
  and starting or stopping it does not affect the others. At most 3 stopwatches run at once (`StopwatchEngine.MaxConcurrent`).
  Independent rows on Today have a bottom line in the stopwatch's color.
- Every run is stored as a session: `Date | Stopwatch Name | Start | Stop`. All statistics are derived from sessions.
- Totals (Today and each Statistics day) are wall-clock time when any stopwatch ran: overlapping time counts once.
  Per-stopwatch totals are each stopwatch's full time. In the timeline bars, overlapping time is drawn as
  stacked stripes, one per running stopwatch (`SessionSlicer`).
- **Temporary vs permanent stopwatches:** a stopwatch is temporary by default and is hidden automatically after a full local day
  without use (`TemporaryStopwatchExpiry`); a permanent one is kept. Hidden stopwatches (expired or deleted) keep their statistics
  and can be restored from Statistics (`StopwatchEngine.Restore`).
- Screens: **Today** (timeline, stopwatch rows, total, Stop all, goals), **Statistics** (one timeline per day,
  expandable per-stopwatch totals (share bars; time past the goal is drawn darker) with a hover **Restore** button on hidden stopwatches, select/clear days), **Settings** (autostart, goal notification toggle, stopwatches: name, color
  (16 presets or a custom color), goal, independent, permanent, shortcut, order, delete; version and update check).
- **System tray**: icon with a menu listing stopwatches with live times, running ones highlighted, switch on click,
  Stop all (stops every running stopwatch), Open, Quit. Double-clicking the tray icon stops the current regular
  stopwatch and starts the next regular one in order (independent ones are skipped).
- **Goal-reached popup**: a small non-activating window in the taskbar-side corner of the primary screen (auto-closes after 8 s, click opens the app).
  Shown only when a stopwatch crosses its daily goal while the app runs (`GoalTracker`), not for goals already reached at start.
  Settings switch "Notify when a goal is reached" (setting `notify_goal_reached`, on by default).
- Global keyboard shortcuts (for example `Ctrl+F1`) start or stop a stopwatch while the app is in the background.
  A new stopwatch gets the first free default shortcut (`Ctrl+F1`..`F12`, then `Ctrl+Shift+`, `Ctrl+Alt+`, `Ctrl+Alt+Shift+`; `DefaultShortcuts`).
  On Linux they work on X11 only (not pure Wayland); the tray needs a StatusNotifierItem host (GNOME: AppIndicator extension).
- Autostart: Windows HKCU Run key; macOS `~/Library/LaunchAgents/com.split3ie.wowstopwatcher.plist`;
  Linux `~/.config/autostart/wowstopwatcher.desktop`.

Sources of truth:

- Requirements: `D:\Personal\obsidian\01-Projects\OwnStopwatchTracker\Summary.md` (outside the repo, owner's notes).
- Visual design: claude.ai Design canvas "WowStopwatcher" (artboards `Main`, `Settings`, `Statistics`, `Tray`).
  Colors, spacing, radii and copy in the app follow that design.

## Stack

- .NET 10, C# (nullable enabled, implicit usings).
- UI: Avalonia 12 with the Fluent theme, MVVM via CommunityToolkit.Mvvm (source generators).
- Storage: SQLite through `Microsoft.Data.Sqlite` (no ORM). Database file: `wowstopwatcher.db` in `WowStopwatcher` under `Environment.SpecialFolder.ApplicationData`
  (Windows: `%AppData%\WowStopwatcher`).
- Tests: xUnit.
- Platforms: Windows, macOS and Linux. Platform-specific code stays behind small services in the App project.

## Layout

```
src/
  WowStopwatcher.slnx
  Directory.Build.props    Shared build settings and <Version>.
  Features.props (App)     Build flags `FeatureX` (see Architecture).
  WowStopwatcher.Core/     Domain models, stopwatch engine (`StopwatchEngine` behind `IStopwatchState`, `IStopwatchControl`,
                           `IStopwatchMaintenance`; expiry strategy `IStopwatchExpiryPolicy`), goal tracking, time formatting,
                           timeline and statistics math. No UI, no I/O.
  WowStopwatcher.Data/     SQLite repositories and schema migrations. Depends on Core.
  WowStopwatcher.App/      Avalonia app. Depends on Core and Data.
    Composition/           Dependency injection setup: `AppComposition`, `IFeatureModule`, `FeatureModules`, `IAppService`.
    Shell/                 Shell abstractions (window, lifetime, dispatcher, dialogs, version, `ShellOptions`, view locator,
                           settings contribution interfaces, `IShortcutService`) and their default implementations.
    Navigation/            `PageDescriptor`, `PageKeys`, `INavigationService`, `INavigationAware`.
    Features/<Name>/       Feature modules: Autostart, GoalNotifications, Hotkeys, Settings, Statistics, Today, Tray, UpdateCheck.
    ViewModels/, Views/, Controls/, Layout/, Theme/, Services/   Always-present UI (main window, timeline, dialogs) and helpers.
  tests/
    WowStopwatcher.Core.Tests/
    WowStopwatcher.Data.Tests/
    WowStopwatcher.App.Tests/   Composition and navigation tests.
  tools/
    WowStopwatcher.BuildTool/  Source of the GUI to pick features and publish win/linux/macOS binaries (not in the solution; see src/tools/README.md).
tools/                         Only the published `WowStopwatcher.BuildTool.exe` (framework-dependent, needs .NET 10).
```

## Architecture

- The App uses `Microsoft.Extensions.DependencyInjection`. `AppComposition.Build` registers Core, storage and the shell,
  then calls `Register` on every module in `FeatureModules.All`, and builds the provider with `ValidateOnBuild`.
- A **feature** is a folder `src/WowStopwatcher.App/Features/<Name>/` with a `<Name>Module : IFeatureModule`. The module registers
  everything the feature contributes: pages (`PageDescriptor` + `ViewMapping`), settings items (`ISettingsGeneralItem`,
  `ISettingsFooterItem` + their `ViewMapping`), and background services (`IAppService`).
- `IAppService.Start` is called after the main window is created, in registration order; services are disposed in reverse order.
- **Build flags** are in `src/WowStopwatcher.App/Features.props`; all default to true and a flag that is true defines `FEATURE_<NAME>`.
  They come in three groups:
  - Pages: `FeatureToday`, `FeatureStatistics`, `FeatureSettings`. **At least one page must be on**: a target fails the build with
    "At least one page must be enabled: FeatureToday, FeatureStatistics or FeatureSettings." The app opens on the page with the lowest `Order`.
  - Stopwatches (capability flags): `FeatureIndependent`, `FeatureGoals`, `FeaturePermanent`.
  - System: `FeatureTray`, `FeatureHotkeys`, `FeatureGoalNotifications`, `FeatureAutostart`, `FeatureUpdateCheck`.
- **Folder flags vs capability flags:** a page or system flag that is false removes the feature folder `Features/<Name>/` from the build
  (Compile, AvaloniaXaml, AvaloniaResource). A capability flag has no folder: it only defines `FEATURE_<NAME>`, and `FeatureModules.Capabilities`
  (a `StopwatchCapabilities`) is built from the three defines and registered as a singleton.
- **Flag dependencies** (forced in `Features.props` before the defines): `FeatureGoals=false` forces `FeatureGoalNotifications=false`;
  `FeatureSettings=false` forces `FeatureAutostart=false` and `FeatureUpdateCheck=false` (their UI lives on the Settings page).
- **Capability decorator:** `AddStorage` registers `SqliteStopwatchRepository` wrapped in `CapabilityFilteredStopwatchRepository`; the engine and
  all other code get the filtered one. Reads return disabled fields neutral (no goal, not independent, not permanent); updates keep the stored values,
  so turning a flag back on restores them. UI for a disabled capability is hidden by injecting `StopwatchCapabilities`, never with `#if`.
- **Adding a feature:** create the folder and module; add the flag (in its group), the define and, for a folder flag, the folder removal to `Features.props`;
  add one `#if FEATURE_<NAME>` entry (and its `using`) in `Composition/FeatureModules.cs`; register its pages, settings items,
  views and background services in the module. Check the minimal CI steps (see Commands), and add a checkbox for it to the
  build tool: one entry in `src/tools/WowStopwatcher.BuildTool/Build/FeatureCatalog.cs` (the tool warns when `Features.props` and the catalog differ).
- **Rules:** `FeatureModules.cs` is the only file with `#if FEATURE_*`. Nothing outside a feature folder references a type
  inside it. Features talk to the rest of the app through Core and Shell/Navigation interfaces.
- **Optional capabilities use a null object:** the shell registers a default that does nothing, and the feature replaces it.
  Example: `IShortcutService` defaults to `NullShortcutService` (`IsAvailable` is false, so Settings hides the shortcut fields);
  `HotkeysModule` registers the real one.
- **Behavior with a feature off:** Today off: no Today page, the app opens on the first remaining page; Settings off: no Settings page,
  no link to it from Today, and no Autostart or UpdateCheck; Goals off: no goal field on Settings cards, no Goals card on Today, no goal pins,
  shading or notifications; Independent off: no independent toggle, every stopwatch is a normal one; Permanent off: no permanent toggle,
  every stopwatch is temporary and auto-hides after a full unused day (the stored flag is kept). Tray off: `ShellOptions.CloseHidesWindow` stays false, so closing the main window quits the app
  (the Tray module sets it to true); Hotkeys off: no shortcut fields in Settings; Statistics off: no Statistics page in the navigation;
  GoalNotifications, Autostart, UpdateCheck off: their Settings card/button and background work are absent.
  Autostart also registers nothing on an unsupported system or when the process path is unknown.

## Commands

```
dotnet build src/WowStopwatcher.slnx
dotnet test src/WowStopwatcher.slnx
dotnet run --project src/WowStopwatcher.App
dotnet build src/WowStopwatcher.slnx -p:FeatureTray=false      # one feature off; same for dotnet test / publish
dotnet build src/WowStopwatcher.slnx <every flag false except FeatureToday>   # minimal build; at least one page must stay on
```

CI builds and tests on Windows, Linux and macOS, then builds and tests again with every flag off except `FeatureToday`, and once more with every flag off except `FeatureSettings`.

The build must finish with no errors and no new warnings. Run the tests after any change to Core or Data.

## Releases

- Version lives in `src/Directory.Build.props` (`<Version>`); the release workflow overrides it from the tag.
- `dotnet publish src/WowStopwatcher.App -c Release` produces a self-contained single-file `WowStopwatcher.exe` (win-x64 by default);
  pass `-r linux-x64`, `-r osx-arm64` or `-r osx-x64` for other platforms (single file `WowStopwatcher`).
  The publish-only settings are in the App csproj under `_IsPublishing`; `build` and `run` are unaffected.
- `.github/workflows/ci.yml` builds and tests on windows/ubuntu/macos on push/PR to `develop` and `main`.
- `.github/workflows/release.yml` runs on a `vX.Y.Z` tag push, or manually (Run workflow) with a patch/minor/major
  bump or an exact version, in which case it creates the tag itself. Either way it: resolves the version, tests, builds packages in a matrix, and creates the GitHub Release once.
  Assets, each with a `.sha256`: `WowStopwatcher-X.Y.Z-win-x64.zip` (exe + LICENSE), `-linux-x64.tar.gz` (binary + LICENSE),
  `-osx-arm64.zip` and `-osx-x64.zip` (ad-hoc signed `WowStopwatcher.app`, Info.plist template in `build/macos/`; icon `build/macos/WowStopwatcher.icns`, generated from `app.ico`).
  A tag with a hyphen becomes a pre-release.
- The release workflow passes `-p:OfficialBuild=true`; builds without it show "local build" instead of the version in Settings.
- Agents never create tags or releases; the owner does.

## Conventions

- Keep logic in Core so it can be unit tested. View models call Core; views only bind.
- Dependencies come through the container (constructor injection); depend on interfaces from Core or Shell. Do not add `#if` outside `FeatureModules.cs`.
- Time comes from `TimeProvider` (injected), never `DateTime.Now` directly, so tests can control the clock.
- Store timestamps as UTC; group sessions into days by **local** date of the session start.
- A running stopwatch is a session with no stop time. On normal quit, running sessions are stopped and saved.
  After a crash, an open session is resumed on the next start.
- Deleting a stopwatch is a soft delete (also used when a temporary stopwatch expires): its past sessions stay in statistics with their name and color.
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
