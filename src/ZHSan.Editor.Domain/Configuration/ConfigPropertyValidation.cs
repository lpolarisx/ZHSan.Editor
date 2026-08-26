namespace ZHSan.Editor.Domain.Configuration;

public sealed record ConfigPropertyValidation
{
    public static ConfigPropertyValidation None { get; } = new();

    public ConfigPropertyValidation(
        bool isRequired = false,
        decimal? minimum = null,
        decimal? maximum = null,
        int? expectedCollectionLength = null)
    {
        if (minimum > maximum)
        {
            throw new ArgumentException("最小值不能大于最大值。", nameof(minimum));
        }

        if (expectedCollectionLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedCollectionLength),
                expectedCollectionLength,
                "集合固定长度不能为负数。");
        }

        IsRequired = isRequired;
        Minimum = minimum;
        Maximum = maximum;
        ExpectedCollectionLength = expectedCollectionLength;
    }

    public bool IsRequired { get; }

    public decimal? Minimum { get; }

    public decimal? Maximum { get; }

    public int? ExpectedCollectionLength { get; }

    public bool HasNumericRange => Minimum.HasValue || Maximum.HasValue;
}
