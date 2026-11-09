namespace WowStopwatcher.App.Navigation;

/// <summary>Implemented by page view models that need to know when their page is shown or left.</summary>
public interface INavigationAware
{
    void OnNavigatedTo();

    void OnNavigatedFrom();
}
