# WowStopwatcher

WowStopwatcher is a desktop app for Windows, macOS and Linux that shows how you actually spend your day. You make a stopwatch for each activity, such as "English" with a 5-hour daily goal, "YouTube" or "Walking", and start the one you are doing. I built it to see where my time goes across several activities and to check my daily goals at a glance.

## How it works

- You have one regular stopwatch running at a time. Start another and the previous one stops and its session is saved, so your day adds up to one honest timeline.
- A stopwatch can be marked **Independent**. It runs alongside the regular one (for example "Walking" while you listen to English) and starting or stopping it doesn't affect the others. Up to 3 stopwatches can run at once.
- The day's total is wall-clock time. When several stopwatches run together, that time counts once in the total, while each stopwatch still shows its own full time.
- Everything is stored as sessions (date, stopwatch, start, stop), and all statistics are calculated from them.

## What you can do

- **See your day on a timeline.** Today shows a colored bar of your sessions. Overlapping time is drawn as stacked stripes. After the logged time, a striped section shows the plan to reach your unfinished goals, nearest goal first, and a star marks the moment each goal was reached.
- **Set daily goals.** Give any stopwatch an optional goal and watch its progress fill up.
- **Get told when you reach a goal.** A small popup appears near the taskbar corner and closes after 8 seconds. You can turn it off with **Notify when a goal is reached** in Settings.
- **Browse statistics.** One timeline per day, newest first. Open a day for per-stopwatch totals, goals met and session count, and select days to clear them.
- **Keep it in the tray.** Closing the window leaves the app running in the tray. The tray menu lists your stopwatches with live times, and a click switches to one. Double-click the tray icon to move to the next regular stopwatch.
- **Use global shortcuts.** Each stopwatch can have a shortcut that starts or stops it from any app.
- **Mix regular and independent stopwatches.** See "How it works" above.
- **Keep temporary stopwatches tidy.** New stopwatches are temporary: they hide themselves after a full day without use. Turn on **Permanent** to keep one. Hidden stopwatches keep their statistics, and you can bring one back in Statistics by hovering its row and clicking **Restore**.
- **Start with your computer.** Optionally launch in the tray with **Run with computer start**. No stopwatch starts by itself.
- **Update from inside the app.** See "Updating" below.
- **Keep your data to yourself.** Everything is stored locally in a SQLite file. Nothing is sent anywhere, except the optional "Check for updates" request to GitHub.

## Installation

You don't need to install .NET; the runtime is included. The app isn't code-signed, so Windows and macOS will warn you on first start (details below).

### Quick install with a script

The script downloads the latest release and installs it. The Linux/macOS script also verifies the SHA-256 checksum.

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/split3ie/wow-stopwatcher/main/scripts/Install-GhLatestRelease.ps1 | iex
```

Linux and macOS:

```bash
curl -fsSL https://raw.githubusercontent.com/split3ie/wow-stopwatcher/main/scripts/install.sh | bash
```

What it does:

- **Windows:** puts `WowStopwatcher.exe` in `~\bin` (your user folder, `bin`), adds that folder to your user PATH and creates a Start Menu shortcut named `WowStopwatcher`.
- **Linux:** puts `WowStopwatcher` in `~/.local/bin`. If that folder isn't in your PATH, the script prints the line to add to your shell profile.
- **macOS:** puts `WowStopwatcher.app` in `~/Applications` and removes the quarantine flag, so you can launch it with `open ~/Applications/WowStopwatcher.app`.

### Manual install

Open [Releases](https://github.com/split3ie/wow-stopwatcher/releases) and download the file for your system:

| System | File |
| --- | --- |
| Windows 10/11, 64-bit | `WowStopwatcher-<version>-win-x64.zip` |
| macOS, Apple silicon | `WowStopwatcher-<version>-osx-arm64.zip` |
| macOS, Intel | `WowStopwatcher-<version>-osx-x64.zip` |
| Linux, 64-bit | `WowStopwatcher-<version>-linux-x64.tar.gz` |

Each release includes a `.sha256` file you can use to check the download.

#### Windows

Unzip to a folder of your choice, for example `%LocalAppData%\Programs\WowStopwatcher`, and run `WowStopwatcher.exe`. SmartScreen may warn on first start: choose **More info → Run anyway**.

#### macOS

Unzip and move `WowStopwatcher.app` to Applications. The app has only an ad-hoc signature, so on first launch right-click it and choose **Open**, or remove the quarantine flag:

```bash
xattr -dr com.apple.quarantine WowStopwatcher.app
```

#### Linux

Extract the archive and run the binary:

```bash
tar -xzf WowStopwatcher-<version>-linux-x64.tar.gz
./WowStopwatcher
```

- The tray icon needs a StatusNotifierItem host. On GNOME, install the AppIndicator extension.
- Global shortcuts work on X11 only, not on pure Wayland.

### Updating

In the app, open **Settings** and click **Check for updates**. If a newer version exists, you can install it right there: the app downloads the file for your platform, verifies its SHA-256, replaces itself and restarts. Running stopwatches keep running.

You can also re-run the install script, or download the new release and replace the old files.

### Uninstalling

1. Open **Settings** and turn off **Run with computer start**, so no autostart entry is left behind. Quit the app from the tray menu.
2. Remove the files the install created:
   - **Windows (script install):** delete `WowStopwatcher.exe` from `~\bin` and the `WowStopwatcher` shortcut from your Start Menu programs folder. The script also added `~\bin` to your user PATH; remove that entry too if nothing else uses the folder.
   - **Linux (script install):** delete `~/.local/bin/WowStopwatcher`.
   - **macOS (script install):** delete `~/Applications/WowStopwatcher.app`.
   - **Manual install:** delete the folder or app you put it in.
3. Optional: delete the data folder to remove your history (see "Your data"). Back it up first if you might want it later.

### Run with computer start

If you turn on **Run with computer start** and later move the app, turn the setting off and on again so it points to the new location. Autostart is stored here:

- Windows: `HKCU\...\Run` registry key
- macOS: `~/Library/LaunchAgents/com.split3ie.wowstopwatcher.plist`
- Linux: `~/.config/autostart/wowstopwatcher.desktop`

## Getting started

1. Open **Settings** and add your stopwatches. For each one you can set the name, color, **Daily goal** (`hh:mm:ss`), **Independent**, **Permanent** and **Start / Stop shortcut** (a default one is filled in). Drag the handle on a stopwatch to change the order.
2. Go to **Today** and click a stopwatch to start it. Click it again, press its shortcut, or use **Stop** to stop it.
3. Close the window whenever you like; the app stays in the tray.
4. Choose **Quit** in the tray menu to exit. Running stopwatches are stopped and saved on quit.

Good to know:

- Starting a regular stopwatch stops the regular one that was running; independent ones keep running.
- New shortcuts start working after you leave the Settings page.
- A new stopwatch gets the first free shortcut from `Ctrl+F1`…`Ctrl+F12`, then `Ctrl+Shift+F1`…`F12`, `Ctrl+Alt+F1`…`F12` and `Ctrl+Alt+Shift+F1`…`F12`.
- Shortcuts need `Ctrl`, `Alt` or `Shift` unless they use a function key. If another app already owns a combination, that shortcut won't work until you pick a different one.
- A session running past midnight is split, so each day's statistics stay correct.
- **Reset** on Today deletes that stopwatch's sessions for today. **Reset all** deletes all of today's sessions.
- Deleting a stopwatch in Settings keeps its past sessions in Statistics.

## Your data

Sessions and stopwatches are stored in `wowstopwatcher.db` (SQLite) in a `WowStopwatcher` folder under the OS application-data folder (`Environment.SpecialFolder.ApplicationData`; on Windows `%AppData%\WowStopwatcher`). Back up that file to keep your history. Deleting it resets the app.

## For developers

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows, macOS or Linux. Built with Avalonia 12, CommunityToolkit.Mvvm and Microsoft.Data.Sqlite.

```bash
dotnet build src/WowStopwatcher.slnx
dotnet test src/WowStopwatcher.slnx
dotnet run --project src/WowStopwatcher.App
```

Start hidden in the tray with `dotnet run --project src/WowStopwatcher.App -- --minimized`.

### Feature flags

Optional features can be left out at build time, for example `dotnet build src/WowStopwatcher.slnx -p:FeatureTray=false`. The flags (all on by default) come in three groups:

- Pages: `FeatureToday`, `FeatureStatistics`, `FeatureSettings`. At least one must stay on, otherwise the build fails.
- Stopwatches: `FeatureIndependent`, `FeatureGoals`, `FeaturePermanent` (a stopwatch property is removed from the whole app; stored values are kept).
- System: `FeatureTray`, `FeatureHotkeys`, `FeatureGoalNotifications`, `FeatureAutostart`, `FeatureUpdateCheck`.

`FeatureGoals=false` also turns off `FeatureGoalNotifications`; `FeatureSettings=false` also turns off `FeatureAutostart` and `FeatureUpdateCheck`.

### Translating

To add or improve a language, see [src/WowStopwatcher.Core/Localization/Translations/README.md](src/WowStopwatcher.Core/Localization/Translations/README.md).

### Build tool

`tools/WowStopwatcher.BuildTool.exe` (Windows, needs the .NET 10 runtime) is a small GUI to pick features and publish the app for Windows, Linux and macOS in one go. Its source is in `src/tools/WowStopwatcher.BuildTool/`; see `src/tools/README.md`. It is kept in sync with the feature flags: when a feature is added or removed, its list is updated and the exe in `tools/` is rebuilt.

### Project layout

| Folder | Contents |
| --- | --- |
| `src/WowStopwatcher.Core` | Stopwatch engine, timeline and statistics calculations, time formatting. No UI or I/O. |
| `src/WowStopwatcher.Data` | SQLite storage and schema migrations. |
| `src/WowStopwatcher.App` | Avalonia app: pages, shell and the optional features (`Features/`: today, settings, tray, global shortcuts, autostart, statistics, goal notifications, update check). |
| `src/tests/` | xUnit tests for Core, Data and the App composition and navigation. |
| `src/tools/WowStopwatcher.BuildTool` | Source of the build tool (feature picker and multi-platform publish). |
| `tools/` | The published build tool exe only. |

### Releasing

Releases are built by GitHub Actions. The easiest way is a button:

1. Open **Actions → Release → Run workflow**.
2. Pick the branch to release (usually `main`).
3. Choose `patch`, `minor` or `major`, or type an exact version such as `1.2.0` or `1.2.0-beta.1`.

The bump starts from the latest stable `vX.Y.Z` tag (pre-release tags are ignored). The workflow creates the tag on the selected commit and fails if that tag already exists.

Pushing a tag yourself also works:

```bash
git tag vX.Y.Z
git push origin vX.Y.Z
```

The workflow runs the tests, builds single-file packages for Windows, macOS (arm64, x64) and Linux, and creates a GitHub Release with the packages, their checksums and generated release notes. A tag with a hyphen, such as `v0.2.0-beta.1`, is published as a pre-release. The in-app **Check for updates** compares the running version with the latest release.

## License

Source-available under the [PolyForm Perimeter License 1.0.0](LICENSE). You may use, copy, modify and share it for any purpose except providing others a product that competes with it. See the license for the exact terms.
