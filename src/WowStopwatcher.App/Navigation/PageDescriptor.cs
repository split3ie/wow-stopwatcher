namespace WowStopwatcher.App.Navigation;

/// <summary>A page reachable from the header navigation. Registered as a singleton by the owner of the page.</summary>
/// <param name="Key">Unique key used to navigate to the page.</param>
/// <param name="TitleKey">Translation key of the navigation button text. It is translated when the navigation is built, after the language is set.</param>
/// <param name="IconKey">Resource key of an icon geometry, or null for a text-only button.</param>
/// <param name="Order">Position in the navigation; lower comes first.</param>
/// <param name="ViewModelType">Type of the page view model, resolved from the container on each visit.</param>
/// <param name="IsPreferredStart">True for the page shown at startup when it is registered; otherwise the first page by <paramref name="Order"/> is shown.</param>
public sealed record PageDescriptor(string Key, string TitleKey, string? IconKey, int Order, Type ViewModelType, bool IsPreferredStart = false)
{
    /// <summary>Picks the page shown at startup: the preferred start page if registered, otherwise the page with the lowest order.</summary>
    public static PageDescriptor PickStartPage(IEnumerable<PageDescriptor> pages) =>
        pages.Where(p => p.IsPreferredStart).OrderBy(p => p.Order).FirstOrDefault()
        ?? pages.OrderBy(p => p.Order).First();
}
