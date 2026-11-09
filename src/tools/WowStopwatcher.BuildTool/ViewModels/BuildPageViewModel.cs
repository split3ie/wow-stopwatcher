using System.ComponentModel;

namespace WowStopwatcher.BuildTool.ViewModels;

/// <summary>The "Build" navigation item (targets, output, version, log).</summary>
public sealed class BuildPageViewModel : NavPageViewModel
{
    private readonly MainViewModel _main;

    public BuildPageViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += OnMainPropertyChanged;
    }

    public override string Title => "Build";

    public override bool HasError => _main.HasVersionError;

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasVersionError))
        {
            OnPropertyChanged(nameof(HasError));
        }
    }
}
