using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace WowStopwatcher.App.Shell;

/// <summary>
/// Builds the view for a view model from the registered <see cref="ViewMapping"/>s (exact type match, no conventions).
/// Added to <c>Application.DataTemplates</c>, so it applies everywhere.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> _factories;

    public ViewLocator(IEnumerable<ViewMapping> mappings) =>
        _factories = mappings.ToDictionary(m => m.ViewModelType, m => m.CreateView);

    public bool Match(object? data) => data is not null && _factories.ContainsKey(data.GetType());

    /// <summary>Returns a new view each time.</summary>
    public Control? Build(object? param) =>
        param is not null && _factories.TryGetValue(param.GetType(), out var create) ? create() : null;
}
