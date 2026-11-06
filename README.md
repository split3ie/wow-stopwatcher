# WowStopwatcher

A Windows desktop app for tracking your day with several stopwatches and daily goals.

Create a stopwatch per activity: "English" with a 5-hour goal, "YouTube", "Walking" with a 1-hour goal. Start one and the
time counts. Switch to another and the first one stops. Only one stopwatch runs at a time, so your day adds up to one honest
timeline.

## Features

- **Today timeline.** A colored bar of today's sessions in order. Hover a section to see its time. A striped grey section shows
  how much time is left to reach your goals, and a star marks the moment each goal was reached.
- **Stopwatch rows.** One big start/stop button per stopwatch with the current session time, today's total, goal progress and
  the session list (collapsible).
- **Goals.** Optional daily goal per stopwatch, with a progress fill, a "Goals" panel and a star once it's reached.
- **Total today and Stop all.** The combined time of all stopwatches, plus a single button to stop the running one or start the
  last used one.
- **Statistics.** One timeline per day, newest first. Click a day for totals per stopwatch, goals met and session count. Select
  days to clear them.
- **System tray.** The app keeps running in the tray when you close the window. Click the tray icon for a menu with every
  stopwatch and its live time, the running one highlighted. Click a stopwatch to switch to it. Double-click the tray icon to
  stop the current stopwatch and start the next one in order.
- **Global shortcuts.** Give each stopwatch a shortcut (for example `Ctrl+F1`) to start or stop it from any app.
- **Run with Windows.** Optionally start in the tray when you sign in. No stopwatch starts by itself.
- **Local and private.** All data stays on your computer in a SQLite file. Nothing is sent anywhere, except the optional
  "Check for updates" request to GitHub.

## Download

1. Open [Releases](https://github.com/split3ie/wow-stopwatcher/releases) and download `WowStopwatcher-<version>-win-x64.zip`.
2. Unzip it to a folder of your choice, for example `%LocalAppData%\Programs\WowStopwatcher`.
3. Run `WowStopwatcher.exe`.

Requirements: Windows 10 or 11, 64-bit. No .NET install is needed; the runtime is included.

The executable isn't code-signed, so Windows SmartScreen may warn on first start. Choose **More info → Run anyway**. Each
release includes a `.sha256` file to check the download.

If you turn on **Run with computer start** and later move the folder, turn the setting off and on again so it points to the
new location.

## Usage

1. Open **Settings** and add your stopwatches: name, color, optional daily goal (`hh:mm:ss`) and optional shortcut.
2. Go to **Today** and click a stopwatch to start it. Click it again, press its shortcut, or use **Stop** to stop it.
3. Close the window when you like; the app stays in the tray. Use **Quit** in the tray menu to exit. A running stopwatch is
   stopped and saved on quit.

Good to know:

- Starting a stopwatch stops the one that was running.
- A session running past midnight is split, so each day's statistics stay correct.
- **Reset** on Today deletes that stopwatch's sessions for today. **Reset all** deletes all of today's sessions.
- Deleting a stopwatch in Settings keeps its past sessions in Statistics.
- Shortcuts need `Ctrl`, `Alt` or `Shift` unless they use a function key. If another app already owns a combination, that
  shortcut won't work until you pick a different one.

## Your data

Sessions and stopwatches are stored in `%AppData%\WowStopwatcher\wowstopwatcher.db` (SQLite). Back up that file to keep your
history. Deleting it resets the app.

## Build from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```
dotnet build src/WowStopwatcher.slnx
dotnet test src/WowStopwatcher.slnx
dotnet run --project src/WowStopwatcher.App
```

Start hidden in the tray with `dotnet run --project src/WowStopwatcher.App -- --minimized`.

Built with Avalonia 12, CommunityToolkit.Mvvm and Microsoft.Data.Sqlite. Project layout:

| Folder | Contents |
| --- | --- |
| `src/WowStopwatcher.Core` | Stopwatch engine, timeline and statistics calculations, time formatting. No UI or I/O. |
| `src/WowStopwatcher.Data` | SQLite storage and schema migrations. |
| `src/WowStopwatcher.App` | Avalonia app: pages, tray, global shortcuts, autostart. |
| `src/tests/` | xUnit tests for Core and Data. |

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

The workflow runs the tests, builds the single-file executable and creates a GitHub Release with the zip, its checksum and
generated release notes. Tags with a suffix, such as `v0.2.0-beta.1`, are published as pre-releases. The in-app
**Check for updates** compares the running version with the latest release.

## License

Source-available under the [PolyForm Perimeter License 1.0.0](LICENSE). You may use, copy, modify and share it for any
purpose except providing others a product that competes with it. See the license for the exact terms.
