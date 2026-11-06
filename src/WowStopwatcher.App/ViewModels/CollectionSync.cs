using System.Collections.ObjectModel;

namespace WowStopwatcher.App.ViewModels;

/// <summary>Helpers to refresh view-model collections in place so existing controls are reused.</summary>
internal static class CollectionSync
{
    /// <summary>Adds or removes items at the end until the list has <paramref name="count"/> items.</summary>
    public static void Resize<T>(ObservableCollection<T> list, int count, Func<T> create)
    {
        while (list.Count > count)
        {
            list.RemoveAt(list.Count - 1);
        }

        while (list.Count < count)
        {
            list.Add(create());
        }
    }
}
