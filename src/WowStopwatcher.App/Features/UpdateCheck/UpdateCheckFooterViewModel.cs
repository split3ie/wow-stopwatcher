using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.UpdateCheck;

/// <summary>The "Check for updates" button and its result message in the Settings footer.</summary>
public sealed partial class UpdateCheckFooterViewModel : ObservableObject, ISettingsFooterItem
{
    private readonly IUpdateChecker _updateChecker;
    private readonly IAppVersion _version;
    private readonly IUpdateInstaller _installer;
    private readonly IDialogService _dialog;
    private readonly IAppLifetime _lifetime;
    private UpdateCheckResult? _available;

    public UpdateCheckFooterViewModel(
        IUpdateChecker updateChecker,
        IAppVersion version,
        IUpdateInstaller installer,
        IDialogService dialog,
        IAppLifetime lifetime)
    {
        _updateChecker = updateChecker;
        _version = version;
        _installer = installer;
        _dialog = dialog;
        _lifetime = lifetime;
    }

    public int Order => 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    /// <summary>True while an update is downloaded and installed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallButtonText))]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand), nameof(InstallUpdateCommand))]
    private bool _isInstalling;

    /// <summary>True when the available update can be installed in place (shows the "Install update" button).</summary>
    [ObservableProperty]
    private bool _canInstallUpdate;

    /// <summary>Release page shown as a fallback link when the install fails.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailedReleaseUrl))]
    private string? _failedReleaseUrl;

    public bool HasFailedReleaseUrl => FailedReleaseUrl is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallButtonText))]
    private int _installPercent;

    public string InstallButtonText => IsInstalling
        ? (InstallPercent > 0 ? Loc.T("update.installingPercent", ("percent", InstallPercent)) : Loc.T("update.installing"))
        : Loc.T("update.install");

    /// <summary>Result text of the last update check (empty before the first check).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateMessage))]
    private string _updateMessage = string.Empty;

    /// <summary>Release page of an available update; when set the message is shown as a link.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateMessage), nameof(HasUpdateLink))]
    private string? _updateUrl;

    [ObservableProperty]
    private bool _isUpdateOk;

    [ObservableProperty]
    private bool _isUpdateError;

    private bool CanCheck() => !IsBusy && !IsInstalling;

    public bool HasUpdateLink => UpdateUrl is not null;

    /// <summary>True when the message is plain text (not a link).</summary>
    public bool HasUpdateMessage => UpdateMessage.Length > 0 && UpdateUrl is null;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        CanInstallUpdate = false;
        FailedReleaseUrl = null;
        _available = null;
        IsUpdateOk = false;
        IsUpdateError = false;
        UpdateUrl = null;
        UpdateMessage = string.Empty;
        try
        {
            var result = await _updateChecker.CheckAsync(_version.Version);
            switch (result.Status)
            {
                case UpdateStatus.UpToDate:
                    UpdateMessage = Loc.T("update.upToDate");
                    IsUpdateOk = true;
                    break;
                case UpdateStatus.Available when IsHttpsUrl(result.Url):
                    UpdateMessage = Loc.T("update.available", ("version", result.Version));
                    UpdateUrl = result.Url;
                    _available = result;
                    CanInstallUpdate = _installer.CanInstall(result);
                    break;
                case UpdateStatus.NoReleases:
                    UpdateMessage = Loc.T("update.noReleases");
                    break;
                default:
                    UpdateMessage = Loc.T("update.checkFailed");
                    IsUpdateError = true;
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private void InstallUpdate()
    {
        if (_available is not { } update)
        {
            return;
        }

        _dialog.Show(
            Loc.T("update.install"),
            Loc.T("update.confirmBody", ("version", update.Version)),
            Loc.T("update.confirmInstall"),
            () => _ = InstallAsync(update));
    }

    private async Task InstallAsync(UpdateCheckResult update)
    {
        IsInstalling = true;
        InstallPercent = 0;
        IsUpdateError = false;
        FailedReleaseUrl = null;
        UpdateUrl = null;
        UpdateMessage = Loc.T("update.installing");
        try
        {
            var progress = new Progress<double>(p => InstallPercent = (int)Math.Round(p * 100));
            await _installer.InstallAsync(update, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update install failed: {ex}");
            UpdateMessage = Loc.T("update.installFailed");
            IsUpdateError = true;
            FailedReleaseUrl = update.Url;
            IsInstalling = false;
            return;
        }

        try
        {
            _installer.StartNewInstance();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not start the updated app: {ex}");
            UpdateMessage = Loc.T("update.installedRestart");
            IsUpdateOk = true;
            IsInstalling = false;
            return;
        }

        _lifetime.QuitKeepingRunningSessions();
    }

    private static bool IsHttpsUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
