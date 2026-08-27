using System.Globalization;

namespace ZHSan.Editor.Domain.Configuration;

public sealed record ConfigStructuredStringParseResult<T>(
    IReadOnlyList<T> Items,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed record WeightedConditionValue(int ConditionId, float Weight);

/// <summary>
/// Implements the whitespace-delimited formats consumed by the game runtime.
/// Only ASCII space, CR, LF and tab are separators because those are the exact
/// characters used by the game's LoadFromString implementations.
/// </summary>
public static class ConfigStructuredStringCodec
{
    private static readonly char[] Separators = [' ', '\n', '\r', '\t'];

    public static ConfigStructuredStringParseResult<int> ParseIds(string? value)
    {
        var items = new List<int>();
        var errors = new List<string>();
        var tokens = Split(value);
        for (var index = 0; index < tokens.Length; index++)
        {
            if (int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                items.Add(id);
            }
            else
            {
                errors.Add($"第 {index + 1} 项“{tokens[index]}”不是有效的 32 位整数 ID。");
            }
        }

        return new ConfigStructuredStringParseResult<int>(items, errors);
    }

    public static ConfigStructuredStringParseResult<WeightedConditionValue> ParseWeightedConditions(
        string? value)
    {
        var items = new List<WeightedConditionValue>();
        var errors = new List<string>();
        var tokens = Split(value);
        if (tokens.Length % 2 != 0)
        {
            errors.Add("条件权重必须按“条件 ID 权重”成对排列。");
        }

        for (var index = 0; index + 1 < tokens.Length; index += 2)
        {
            var hasId = int.TryParse(
                tokens[index],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var id);
            var hasWeight = float.TryParse(
                tokens[index + 1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var weight) && float.IsFinite(weight);
            if (!hasId)
            {
                errors.Add($"第 {index + 1} 项“{tokens[index]}”不是有效的 32 位条件 ID。");
            }

            if (!hasWeight)
            {
                errors.Add($"第 {index + 2} 项“{tokens[index + 1]}”不是有效的有限权重。");
            }

            if (hasId && hasWeight)
            {
                items.Add(new WeightedConditionValue(id, weight));
            }
        }

        return new ConfigStructuredStringParseResult<WeightedConditionValue>(items, errors);
    }

    public static string FormatIds(IEnumerable<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return string.Join(' ', ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
    }

    public static string FormatWeightedConditions(IEnumerable<WeightedConditionValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return string.Join(' ', values.SelectMany(value => new[]
        {
            value.ConditionId.ToString(CultureInfo.InvariantCulture),
            value.Weight.ToString("R", CultureInfo.InvariantCulture),
        }));
    }

    private static string[] Split(string? value) =>
        (value ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
}
