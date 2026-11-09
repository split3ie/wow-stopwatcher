using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WowStopwatcher.BuildTool.ViewModels;

/// <summary>A target (runtime identifier) toggle with the status of its last build.</summary>
public sealed partial class TargetOptionViewModel(string rid, bool isSelected) : ObservableObject
{
    public string Rid { get; } = rid;

    /// <summary>Short name such as "Windows x64".</summary>
    public string Label { get; } = rid switch
    {
        "win-x64" => "Windows x64",
        "linux-x64" => "Linux x64",
        "osx-arm64" => "macOS Apple Silicon (arm64)",
        "osx-x64" => "macOS Intel (x64)",
        _ => rid,
    };

    [ObservableProperty]
    private bool _isSelected = isSelected;

    /// <summary>The editable publish command line for this target.</summary>
    [ObservableProperty]
    private string _command = "";

    /// <summary>Runs only this target; assigned by the main view model.</summary>
    public IRelayCommand? RunCommand { get; set; }

    /// <summary>Empty, "building...", "ok", "failed" or "cancelled".</summary>
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [ObservableProperty]
    private string _status = "";

    public bool HasStatus => Status.Length > 0;
}
