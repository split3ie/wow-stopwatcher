using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.UpdateCheck;

/// <summary>The "Check for updates" button and its result message in the Settings footer.</summary>
public sealed partial class UpdateCheckFooterViewModel : ObservableObject, ISettingsFooterItem
{
    private readonly IUpdateChecker _updateChecker;
    private readonly IAppVersion _version;

    public UpdateCheckFooterViewModel(IUpdateChecker updateChecker, IAppVersion version)
    {
        _updateChecker = updateChecker;
        _version = version;
    }

    public int Order => 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

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

    public bool HasUpdateLink => UpdateUrl is not null;

    /// <summary>True when the message is plain text (not a link).</summary>
    public bool HasUpdateMessage => UpdateMessage.Length > 0 && UpdateUrl is null;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
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
                    UpdateMessage = "You’re on the latest version";
                    IsUpdateOk = true;
                    break;
                case UpdateStatus.Available when IsHttpsUrl(result.Url):
                    UpdateMessage = $"Version {result.Version} is available";
                    UpdateUrl = result.Url;
                    break;
                case UpdateStatus.NoReleases:
                    UpdateMessage = "No releases published yet";
                    break;
                default:
                    UpdateMessage = "Couldn’t check for updates";
                    IsUpdateError = true;
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsHttpsUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
