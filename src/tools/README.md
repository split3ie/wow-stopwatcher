# Tools

## WowStopwatcher.BuildTool

A small Avalonia desktop tool to choose which optional features are compiled in and to publish the app binaries.

The source is in `src/tools/WowStopwatcher.BuildTool/`; the top-level `tools/` folder holds only the published exe.
The tool uses an orange variant of the app icon, at `Assets/app.ico` in the tool's source folder.
Run the exe (needs the .NET 10 runtime), or run from the source at the repository root:

```
tools/WowStopwatcher.BuildTool.exe
dotnet run --project src/tools/WowStopwatcher.BuildTool
```

Regenerate the exe after changing the tool (PowerShell, from the repository root):

```
dotnet publish src/tools/WowStopwatcher.BuildTool -c Release -o artifacts/tool-publish; Copy-Item artifacts/tool-publish/WowStopwatcher.BuildTool.exe tools/ -Force; Remove-Item artifacts/tool-publish -Recurse
```

The publish settings (win-x64, framework-dependent, single file, no pdb) are in the tool csproj under `_IsPublishing`.
The tool finds the repository root by walking up from the exe's folder to the folder containing `src/WowStopwatcher.slnx`.
It is not part of `src/WowStopwatcher.slnx`.

What it does:

- Lists the feature flags from `src/WowStopwatcher.App/Features.props` (all on by default); unchecked ones are passed as `-p:FeatureX=false`.
- Publishes each checked target (`win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`) one after another with
  `dotnet publish src/WowStopwatcher.App -c Release -r <rid> -o <output>/<rid>`. The default output folder is `artifacts/publish`
  (git-ignored). The optional version is passed as `-p:Version`. Builds are local (no `OfficialBuild`), so the app shows "local build".
- The Build page has one editable command line per checked target (under "Command", above the log), for example
  `dotnet publish src/WowStopwatcher.App -c Release -r win-x64 -o <output>/win-x64 -p:FeatureTray=false`, each with a **Run** button that
  builds only that target; **Build** runs every checked target with its own line. Whitespace separates arguments; double quotes group an
  argument with spaces. Changing features, the version, the output folder or the checked targets regenerates all lines (typed edits are lost);
  "Reset" regenerates them too. An empty line for a checked target disables Build and Run.
- Before publishing a target, closes a running `WowStopwatcher` started from that exact output file (close request, then kill after 5 s; open sessions resume on the next start) and renames the old file to `WowStopwatcher.old.exe` (`WowStopwatcher.old` on Linux/macOS), replacing an earlier one; if the rename fails, the target fails without publishing.
- Shows the command line and the live process output in the log, a status per target, and a Cancel button that kills the running process tree.

macOS limitation: the single-file binary for `osx-*` is produced on any host, but the `WowStopwatcher.app` bundle and its ad-hoc
signing (`codesign`) are created only when the tool runs on macOS. On other hosts the log says so.

UI: the tool uses the FluentAvalonia package (`FluentAvaloniaUI` 3.1.0) for a Windows 11 look: Mica window (solid background where Mica is not
available), title bar extended into the window, system light/dark theme and accent color, a left navigation pane, settings cards and info bars.

Navigation pages: the pane has one page per feature group (Pages, Stopwatches, System, ...) with the number of enabled members
("2/3") and a red badge when the group rule is violated, and a **Build** page pinned at the bottom of the pane.
A group page starts with the card "All features in this group" (a three-state checkbox that checks or unchecks all available members; the
card text says all, none or some enabled), followed by one card per feature with a toggle. The Build page has the targets (one toggle each,
with the status of the last build), the output folder (Browse...), the version (with inline validation), the command line and the log (monospace, scrolls to the end,
"Copy log"). The bottom bar is on every page: the problems list, the build status, Cancel and Build.

Feature groups and dependencies: some features depend on others (Goal notifications need Goals; Invoices PIN needs Invoicing and Encryption; Autostart and
Update check need Settings): when the requirement is off, the dependent one is off, its card is disabled and shows "Requires X",
and its previous choice returns when the requirement is on again. Both off and disabled features are passed as `=false`.
The Pages group needs at least one page; otherwise the group is marked, the problem is listed in the bottom bar and Build is disabled.

Saved setting: the output folder is remembered between runs in `%AppData%\WowStopwatcher.BuildTool\settings.json`
(`Environment.SpecialFolder.ApplicationData`; a separate folder, not the app's `WowStopwatcher` data folder). If the file is missing,
unreadable or has no value, the default `artifacts/publish` is used.

New features: the feature list is hard-coded in `src/tools/WowStopwatcher.BuildTool/Build/FeatureCatalog.cs`. When a flag is added to
`Features.props`, add an entry there too, with its group and the flags it requires. A new group in the catalog appears as a new navigation page automatically. On startup the tool shows a warning bar
if the two lists differ.
