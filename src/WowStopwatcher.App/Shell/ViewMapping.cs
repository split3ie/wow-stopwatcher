using Avalonia.Controls;

namespace WowStopwatcher.App.Shell;

/// <summary>Tells the <see cref="ViewLocator"/> which view shows a view model. Registered in DI by the owner of the view.</summary>
/// <param name="ViewModelType">Exact type of the view model.</param>
/// <param name="CreateView">Creates a new view each time it is called.</param>
public sealed record ViewMapping(Type ViewModelType, Func<Control> CreateView);
