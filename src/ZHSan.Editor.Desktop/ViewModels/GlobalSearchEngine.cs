using System.Collections;
using System.Globalization;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.ViewModels;

public static class GlobalSearchEngine
{
    public static IReadOnlyList<GlobalSearchMatch> Search(
        IEnumerable<ConfigDocumentViewModel> documents,
        string query,
        int limit = 500)
    {
        var text = query.Trim();
        if (text.Length == 0 || limit <= 0)
        {
            return [];
        }

        var results = new List<GlobalSearchMatch>();
        foreach (var document in documents)
        {
            foreach (var record in document.Records)
            {
                foreach (var property in document.Properties)
                {
                    var value = record.Item.GetType().GetProperty(property.Name)?.GetValue(record.Item);
                    if (!ContainsText(value, text))
                    {
                        continue;
                    }

                    results.Add(new GlobalSearchMatch(
                        document,
                        record,
                        property,
                        FormatValue(value)));
                    if (results.Count >= limit)
                    {
                        return results;
                    }
                }
            }
        }

        return results;
    }

    private static bool ContainsText(object? value, string query)
    {
        if (value is IEnumerable values and not string)
        {
            return values.Cast<object?>().Any(item => ContainsText(item, query));
        }

        return FormatValue(value).Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is IEnumerable values and not string)
        {
            return string.Join(", ", values.Cast<object?>().Select(FormatValue));
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.CurrentCulture) ?? string.Empty
            : value.ToString() ?? string.Empty;
    }
}

public sealed record GlobalSearchMatch(
    ConfigDocumentViewModel Document,
    ConfigRecordViewModel Record,
    ConfigPropertyDefinition Property,
    string ValuePreview);
