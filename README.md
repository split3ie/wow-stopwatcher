# WowStopwatcher

A desktop app for Windows, macOS and Linux for tracking your day with several stopwatches and daily goals.

Create a stopwatch per activity: "English" with a 5-hour goal, "YouTube", "Walking" with a 1-hour goal. Start one and the
time counts. Switch to another and the first one stops. Only one regular stopwatch runs at a time, so your day adds up to one
honest timeline. Mark a stopwatch as independent (for example "Walking" while you listen to English) and it runs alongside
the others; time when several run at once counts once in the day's total.

## Features

- **Today timeline.** A colored bar of today's sessions in order. Hover a section to see its time. Time when several
  stopwatches ran together is drawn as stacked stripes, one per stopwatch. After the logged time, a striped section shows the
  plan to reach your goals: one piece per unfinished goal, tinted in that stopwatch's color, nearest goal first. Hover a piece
  to see which goal it is and how much is left. A star marks the moment each goal was reached.
- **Stopwatch rows.** One big start/stop button per stopwatch with the current session time, today's total, goal progress and
  the session list (collapsible).
- **Goals.** Optional daily goal per stopwatch, with a progress fill, a "Goals" panel and a star once it's reached.
- **Total today and Stop all.** The day's tracked time (overlapping time counted once), plus a single button to stop every
  running stopwatch or start the last used one.
- **Statistics.** One timeline per day, newest first. Click a day for totals per stopwatch, goals met and session count. Select
  days to clear them.
- **System tray.** The app keeps running in the tray when you close the window. Click the tray icon for a menu with every
  stopwatch and its live time, running ones highlighted. Click a stopwatch to switch to it. Double-click the tray icon to
  stop the current regular stopwatch and start the next regular one in order.
- **Independent stopwatches.** Up to 3 stopwatches can run at once: one regular plus independent ones. Starting or stopping
  an independent stopwatch doesn't affect the others.
- **Global shortcuts.** Each stopwatch can have a shortcut to start or stop it from any app. A new stopwatch gets the first
  free one of `Ctrl+F1`…`Ctrl+F12`, then `Ctrl+Shift+F1`…`F12`, `Ctrl+Alt+F1`…`F12` and `Ctrl+Alt+Shift+F1`…`F12`. Change
  or clear it in Settings.
- **Run on sign-in.** Optionally start in the tray when you sign in. No stopwatch starts by itself.
- **Local and private.** All data stays on your computer in a SQLite file. Nothing is sent anywhere, except the optional
  "Check for updates" request to GitHub.

## Download

### Install with a script

The script downloads the latest release and installs it (the Linux/macOS script also verifies the SHA-256).

Windows (PowerShell):

```
irm https://raw.githubusercontent.com/split3ie/wow-stopwatcher/main/scripts/Install-GhLatestRelease.ps1 | iex
```

Linux and macOS:

```
curl -fsSL https://raw.githubusercontent.com/split3ie/wow-stopwatcher/main/scripts/install.sh | bash
```

- Windows installs to `~\bin` and adds it to the user PATH.
- Linux installs to `~/.local/bin`.
- macOS installs to `~/Applications`.

### Manual download

Open [Releases](https://github.com/split3ie/wow-stopwatcher/releases) and download the file for your system:

| System | File |
| --- | --- |
| Windows 10/11, 64-bit | `WowStopwatcher-<version>-win-x64.zip` |
| macOS, Apple silicon | `WowStopwatcher-<version>-osx-arm64.zip` |
| macOS, Intel | `WowStopwatcher-<version>-osx-x64.zip` |
| Linux, 64-bit | `WowStopwatcher-<version>-linux-x64.tar.gz` |

No .NET install is needed; the runtime is included. Each release includes a `.sha256` file to check the download. The app
isn't code-signed.

**Windows.** Unzip to a folder of your choice, for example `%LocalAppData%\Programs\WowStopwatcher`, and run
`WowStopwatcher.exe`. SmartScreen may warn on first start: choose **More info → Run anyway**.

**macOS.** Unzip and move `WowStopwatcher.app` to Applications. The app is unsigned (ad-hoc signature only), so on first
launch right-click it and choose **Open**, or run `xattr -dr com.apple.quarantine WowStopwatcher.app`.

**Linux.** Extract with `tar -xzf WowStopwatcher-<version>-linux-x64.tar.gz` and run `./WowStopwatcher`.

- The tray icon needs a StatusNotifierItem host. On GNOME, install the AppIndicator extension.
- Global shortcuts work on X11 only, not on pure Wayland.

If you turn on **Run with computer start** and later move the app, turn the setting off and on again so it points to the
new location. Autostart is stored here:

- Windows: `HKCU\...\Run` registry key
- macOS: `~/Library/LaunchAgents/com.split3ie.wowstopwatcher.plist`
- Linux: `~/.config/autostart/wowstopwatcher.desktop`

## Usage

1. Open **Settings** and add your stopwatches: name, color, optional daily goal (`hh:mm:ss`), independent or not, and a
   shortcut (a default one is filled in).
2. Go to **Today** and click a stopwatch to start it. Click it again, press its shortcut, or use **Stop** to stop it.
3. Close the window when you like; the app stays in the tray. Use **Quit** in the tray menu to exit. A running stopwatch is
   stopped and saved on quit.

Good to know:

- Starting a regular stopwatch stops the regular one that was running; independent ones keep running.
- New shortcuts start working after you leave the Settings page.
- A session running past midnight is split, so each day's statistics stay correct.
- **Reset** on Today deletes that stopwatch's sessions for today. **Reset all** deletes all of today's sessions.
- Deleting a stopwatch in Settings keeps its past sessions in Statistics.
- Shortcuts need `Ctrl`, `Alt` or `Shift` unless they use a function key. If another app already owns a combination, that
  shortcut won't work until you pick a different one.

## Your data

Sessions and stopwatches are stored in `wowstopwatcher.db` (SQLite) in a `WowStopwatcher` folder under the OS application-data folder (`Environment.SpecialFolder.ApplicationData`; on Windows `%AppData%\WowStopwatcher`). Back up that file to keep your
history. Deleting it resets the app.

## Build from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows, macOS or Linux.

```
dotnet build src/WowStopwatcher.slnx
dotnet test src/WowStopwatcher.slnx
dotnet run --project src/WowStopwatcher.App
```

Optional features can be left out at build time, for example `dotnet build src/WowStopwatcher.slnx -p:FeatureTray=false`. The flags (all on by default) are in three groups:

- Pages: `FeatureToday`, `FeatureStatistics`, `FeatureSettings`. At least one must stay on, otherwise the build fails.
- Stopwatches: `FeatureIndependent`, `FeatureGoals`, `FeaturePermanent` (a stopwatch property is removed from the whole app; stored values are kept).
- System: `FeatureTray`, `FeatureHotkeys`, `FeatureGoalNotifications`, `FeatureAutostart`, `FeatureUpdateCheck`.

`FeatureGoals=false` also turns off `FeatureGoalNotifications`; `FeatureSettings=false` also turns off `FeatureAutostart` and `FeatureUpdateCheck`.

### Build tool

`tools/WowStopwatcher.BuildTool.exe` (Windows, needs the .NET 10 runtime) is a small GUI to pick the features and publish the app for
Windows, Linux and macOS in one go. Its source is in `src/tools/WowStopwatcher.BuildTool/`; see `src/tools/README.md`.
The tool is kept in sync with the feature flags: whenever a feature is added or removed, its list is updated and the exe in `tools/` is rebuilt.

Start hidden in the tray with `dotnet run --project src/WowStopwatcher.App -- --minimized`.

Built with Avalonia 12, CommunityToolkit.Mvvm and Microsoft.Data.Sqlite. Project layout:

| Folder | Contents |
| --- | --- |
| `src/WowStopwatcher.Core` | Stopwatch engine, timeline and statistics calculations, time formatting. No UI or I/O. |
| `src/WowStopwatcher.Data` | SQLite storage and schema migrations. |
| `src/WowStopwatcher.App` | Avalonia app: pages, shell and the optional features (`Features/`: today, settings, tray, global shortcuts, autostart, statistics, goal notifications, update check). |
| `src/tests/` | xUnit tests for Core, Data and the App composition and navigation. |
| `src/tools/WowStopwatcher.BuildTool` | Source of the build tool (feature picker and multi-platform publish). |
| `tools/` | The published build tool exe only. |

## Releasing

Releases are built by GitHub Actions. The easiest way is a button:

1. Open **Actions → Release → Run workflow**.
2. Pick the branch to release (usually `main`).
3. Choose `patch`, `minor` or `major`, or type an exact version such as `1.2.0` or `1.2.0-beta.1`.

The bump starts from the latest stable `vX.Y.Z` tag (pre-release tags are ignored). The workflow creates the tag on the
selected commit and fails if that tag already exists.

Pushing a tag yourself still works:

```
git tag vX.Y.Z
git push origin vX.Y.Z
```

The workflow runs the tests, builds single-file packages for Windows, macOS (arm64, x64) and Linux, and creates a GitHub Release with
the packages, their checksums and generated release notes. Tags with a suffix, such as `v0.2.0-beta.1`, are published as pre-releases. The in-app
**Check for updates** compares the running version with the latest release.

## License

Source-available under the [PolyForm Perimeter License 1.0.0](LICENSE). You may use, copy, modify and share it for any
purpose except providing others a product that competes with it. See the license for the exact terms.
