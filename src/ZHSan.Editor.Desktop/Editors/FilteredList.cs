using System.Collections.ObjectModel;

namespace ZHSan.Editor.Desktop.Editors;

internal static class FilteredList
{
    /// <summary>
    /// Makes <paramref name="shown"/> hold <paramref name="matches"/>, in order, by removing and inserting only what
    /// changed: entries that still match stay in place, so the list keeps its selection while the search changes.
    /// Lookups go through sets, as a table can hold over ten thousand records.
    /// </summary>
    public static void Update<T>(ObservableCollection<T> shown, IReadOnlyList<T> matches) where T : class
    {
        var wanted = new HashSet<T>(matches, ReferenceEqualityComparer.Instance);
        for (var index = shown.Count - 1; index >= 0; index--)
            if (!wanted.Contains(shown[index])) shown.RemoveAt(index);

        var present = new HashSet<T>(shown, ReferenceEqualityComparer.Instance);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            if (index < shown.Count && ReferenceEquals(shown[index], match)) continue;
            if (present.Contains(match)) shown.Move(shown.IndexOf(match), index);
            else
            {
                shown.Insert(index, match);
                present.Add(match);
            }
        }
    }
}
