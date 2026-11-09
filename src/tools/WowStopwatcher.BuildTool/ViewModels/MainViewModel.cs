using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.BuildTool.Build;

namespace WowStopwatcher.BuildTool.ViewModels;

/// <summary>State and commands of the main window.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly string[] Rids = ["win-x64", "linux-x64", "osx-arm64", "osx-x64"];

    private readonly string? _repoRoot;
    private readonly FeatureSelection _selection;
    private CancellationTokenSource? _cancellation;
    private readonly string _settingsPath;
    private bool _settingsLoaded;

    public MainViewModel() : this(ToolSettingsStore.DefaultPath)
    {
    }

    public MainViewModel(string settingsPath)
    {
        _settingsPath = settingsPath;
        _selection = new FeatureSelection(FeatureCatalog.All);
        Groups = new ObservableCollection<FeatureGroupViewModel>(FeatureCatalog.Groups.Select(g =>
            new FeatureGroupViewModel(
                g,
                _selection,
                new ObservableCollection<FeatureOptionViewModel>(FeatureCatalog.All
                    .Where(f => f.Group == g)
                    .Select(f => new FeatureOptionViewModel(f, _selection, RefreshFeatures))),
                RefreshFeatures)));
        BuildPage = new BuildPageViewModel(this);
        FooterPages = [BuildPage];
        _selectedPage = Groups.FirstOrDefault() ?? (NavPageViewModel)BuildPage;
        var hostRid = HostRid();
        Targets = new ObservableCollection<TargetOptionViewModel>(
            Rids.Select(r => new TargetOptionViewModel(r, r == hostRid)));
        foreach (var target in Targets)
        {
            target.RunCommand = new AsyncRelayCommand(
                () => BuildTargetsAsync([target]), () => CanBuild() && !string.IsNullOrWhiteSpace(target.Command));
            target.PropertyChanged += OnTargetPropertyChanged;
        }

        _repoRoot = RepoLocator.FindRoot(AppContext.BaseDirectory);
        if (_repoRoot is null)
        {
            ErrorMessage = "Repository root not found (no folder with src/WowStopwatcher.slnx above "
                + AppContext.BaseDirectory + ").";
            ResetCommands();
            return;
        }

        var defaultOutput = Path.Combine(_repoRoot, "artifacts", "publish");
        var saved = ToolSettingsStore.Load(_settingsPath).OutputFolder;
        OutputFolder = string.IsNullOrWhiteSpace(saved) ? defaultOutput : saved;
        _settingsLoaded = true;
        var propsPath = Path.Combine(_repoRoot, "src", "WowStopwatcher.App", "Features.props");
        try
        {
            var flags = FeaturePropsReader.ParseFlags(File.ReadAllText(propsPath));
            WarningMessage = FeaturePropsReader.Compare(flags, FeatureCatalog.All);
        }
        catch (IOException ex)
        {
            ErrorMessage = "Cannot read Features.props: " + ex.Message;
        }

        ResetCommands();
    }

    public ObservableCollection<FeatureGroupViewModel> Groups { get; }

    public ObservableCollection<TargetOptionViewModel> Targets { get; }

    public BuildPageViewModel BuildPage { get; }

    /// <summary>Items pinned to the bottom of the navigation pane.</summary>
    public ObservableCollection<NavPageViewModel> FooterPages { get; }

    /// <summary>The page shown in the content area: a feature group or the build page.</summary>
    [NotifyPropertyChangedFor(nameof(SelectedGroup), nameof(IsBuildPageSelected))]
    [ObservableProperty]
    private NavPageViewModel? _selectedPage;

    public FeatureGroupViewModel? SelectedGroup => SelectedPage as FeatureGroupViewModel;

    public bool IsBuildPageSelected => SelectedPage is BuildPageViewModel;

    /// <summary>Status line shown in the bottom bar while building.</summary>
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [ObservableProperty]
    private string? _statusText;

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    [ObservableProperty]
    private string _outputFolder = "";

    [ObservableProperty]
    private string _version = "";

    [ObservableProperty]
    private string _log = "";

    [NotifyPropertyChangedFor(nameof(HasError))]
    [ObservableProperty]
    private string? _errorMessage;

    [NotifyPropertyChangedFor(nameof(HasWarning))]
    [ObservableProperty]
    private string? _warningMessage;

    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand), nameof(CancelCommand))]
    [ObservableProperty]
    private bool _isBuilding;

    public bool IsIdle => !IsBuilding;

    partial void OnIsBuildingChanged(bool value) => NotifyBuildStateChanged();

    partial void OnOutputFolderChanged(string value)
    {
        ResetCommands();
        if (_settingsLoaded)
        {
            ToolSettingsStore.Save(_settingsPath, new ToolSettings { OutputFolder = value });
        }
    }

    public bool HasError => ErrorMessage is not null;

    public bool HasWarning => WarningMessage is not null;

    private static string HostRid()
    {
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        if (OperatingSystem.IsWindows())
        {
            return "win-" + arch;
        }

        return (OperatingSystem.IsMacOS() ? "osx-" : "linux-") + arch;
    }

    /// <summary>Inline error under the Version box, or null when the version is valid or empty.</summary>
    public string? VersionError => VersionValidator.Validate(Version);

    public bool HasVersionError => VersionError is not null;

    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetOptionViewModel.IsSelected))
        {
            ResetCommands();
        }
        else if (e.PropertyName == nameof(TargetOptionViewModel.Command))
        {
            NotifyBuildStateChanged();
        }
    }

    /// <summary>Regenerates every target's command line from the current settings.</summary>
    [RelayCommand]
    private void ResetCommands()
    {
        var disabled = _selection.DisabledFlags().ToList();
        foreach (var target in Targets)
        {
            target.Command = PublishCommand.BuildCommandLine(
                target.Rid, TargetDir(target.Rid), disabled, Version);
        }

        NotifyBuildStateChanged();
    }

    private string TargetDir(string rid)
    {
        string root;
        try
        {
            root = Path.GetFullPath(OutputFolder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            root = OutputFolder;
        }

        return Path.Combine(root, rid);
    }

    private void NotifyBuildStateChanged()
    {
        OnPropertyChanged(nameof(ProblemsText));
        OnPropertyChanged(nameof(HasProblems));
        BuildCommand.NotifyCanExecuteChanged();
        foreach (var target in Targets)
        {
            target.RunCommand?.NotifyCanExecuteChanged();
        }
    }

    partial void OnVersionChanged(string value)
    {
        OnPropertyChanged(nameof(VersionError));
        OnPropertyChanged(nameof(HasVersionError));
        ResetCommands();
    }

    /// <summary>Blocking problems (group rules, version) joined for display next to Build, or null.</summary>
    public string? ProblemsText
    {
        get
        {
            var problems = _selection.GroupErrors().ToList();
            if (VersionError is not null)
            {
                problems.Add(VersionError);
            }

            problems.AddRange(Targets
                .Where(t => t.IsSelected && string.IsNullOrWhiteSpace(t.Command))
                .Select(t => $"Command for {t.Rid} is empty"));

            return problems.Count == 0 ? null : string.Join("; ", problems);
        }
    }

    public bool HasProblems => ProblemsText is not null;

    private bool CanBuild() =>
        !IsBuilding && _repoRoot is not null && _selection.GroupErrors().Count == 0 && !HasVersionError
        && !Targets.Any(t => t.IsSelected && string.IsNullOrWhiteSpace(t.Command));

    /// <summary>Refreshes all feature checkboxes, group states and the Build button after a change.</summary>
    private void RefreshFeatures()
    {
        foreach (var group in Groups)
        {
            group.Refresh();
            foreach (var option in group.Options)
            {
                option.Refresh();
            }
        }

        ResetCommands();
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private Task BuildAsync() => BuildTargetsAsync(Targets.Where(t => t.IsSelected).ToList());

    private async Task BuildTargetsAsync(IReadOnlyList<TargetOptionViewModel> selected)
    {
        // Show the Build page so the log and per-target status are visible.
        SelectedPage = BuildPage;

        if (selected.Count == 0 || _repoRoot is null)
        {
            Log = "No target selected." + Environment.NewLine;
            return;
        }

        IsBuilding = true;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        foreach (var target in Targets)
        {
            target.Status = "";
        }

        Log = "";
        try
        {
            foreach (var (target, index) in selected.Select((t, i) => (t, i)))
            {
                StatusText = $"Building {target.Label} ({index + 1} of {selected.Count})...";
                await BuildTargetAsync(target, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled.");
        }
        finally
        {
            _cancellation = null;
            StatusText = null;
            IsBuilding = false;
        }
    }

    private async Task BuildTargetAsync(
        TargetOptionViewModel target, CancellationToken token)
    {
        var repoRoot = _repoRoot!;
        var targetDir = TargetDir(target.Rid);
        var command = CommandLineSplitter.Split(target.Command);
        target.Status = "building...";
        AppendLog("");
        AppendLog("=== " + target.Rid + " ===");
        AppendLog(PublishCommand.Format(command));

        string? firstError = null;
        void OnOutput(string line)
        {
            firstError ??= BuildErrorFinder.IsErrorLine(line) ? line.Trim() : null;
            AppendLog(line);
        }

        try
        {
            var exit = await ProcessRunner.RunAsync(command[0], command.Skip(1), repoRoot, OnOutput, token);
            if (exit != 0)
            {
                target.Status = "failed";
                AppendLog($"The command exited with code {exit}.");
                AppendLog(firstError is null
                    ? "No error line found in the output; see the log above."
                    : "First error: " + firstError);
                return;
            }

            if (target.Rid.StartsWith("osx-", StringComparison.Ordinal))
            {
                if (!OperatingSystem.IsMacOS())
                {
                    AppendLog("The .app bundle and signing need macOS; only the single-file binary was produced.");
                }
                else
                {
                    var version = string.IsNullOrWhiteSpace(Version) ? "0.0.0" : Version.Trim();
                    if (!await MacAppBundler.CreateAsync(repoRoot, targetDir, version, AppendLog, token))
                    {
                        target.Status = "failed";
                        AppendLog("codesign failed.");
                        return;
                    }
                }
            }

            target.Status = "ok";
        }
        catch (OperationCanceledException)
        {
            target.Status = "cancelled";
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            target.Status = "failed";
            AppendLog("Error: " + ex.Message);
        }
    }

    private bool CanCancel() => IsBuilding;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cancellation?.Cancel();

    /// <summary>Appends a line to the log; process output arrives on thread-pool threads.</summary>
    private void AppendLog(string line)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Log += line + Environment.NewLine;
        }
        else
        {
            Dispatcher.UIThread.Post(() => Log += line + Environment.NewLine);
        }
    }
}
