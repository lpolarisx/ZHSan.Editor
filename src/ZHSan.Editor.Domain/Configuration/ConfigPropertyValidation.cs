namespace ZHSan.Editor.Domain.Configuration;

public sealed record ConfigPropertyValidation
{
    public static ConfigPropertyValidation None { get; } = new();

    public ConfigPropertyValidation(
        bool isRequired = false,
        decimal? minimum = null,
        decimal? maximum = null)
    {
        if (minimum > maximum)
        {
            throw new ArgumentException("最小值不能大于最大值。", nameof(minimum));
        }

        IsRequired = isRequired;
        Minimum = minimum;
        Maximum = maximum;
    }

    public bool IsRequired { get; }

    public decimal? Minimum { get; }

    public decimal? Maximum { get; }

    public bool HasNumericRange => Minimum.HasValue || Maximum.HasValue;
}
