using System.Globalization;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public sealed class PropertyConstraintValidationRule : IFieldValidationRule
{
    public IEnumerable<ValidationIssue> Validate(FieldValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var validation = context.Property.Validation;
        if (validation.IsRequired && IsMissing(context.Value))
        {
            yield return CreateIssue(context, $"{context.Property.DisplayName} 为必填字段。");
        }

        if (!validation.HasNumericRange || context.Value is null)
        {
            yield break;
        }

        if (!TryGetNumber(context.Value, out var number))
        {
            yield return CreateIssue(context, $"{context.Property.DisplayName} 不是有效数值。");
            yield break;
        }

        if (validation.Minimum is { } minimum && number < minimum)
        {
            yield return CreateIssue(
                context,
                $"{context.Property.DisplayName} 不能小于 {Format(minimum)}。");
        }

        if (validation.Maximum is { } maximum && number > maximum)
        {
            yield return CreateIssue(
                context,
                $"{context.Property.DisplayName} 不能大于 {Format(maximum)}。");
        }
    }

    private static ValidationIssue CreateIssue(FieldValidationContext context, string message) =>
        new(
            ValidationSeverity.Error,
            context.Document.Definition.Key,
            context.Item.Id,
            context.Property.Name,
            message);

    private static bool IsMissing(object? value) =>
        value is null || value is string text && string.IsNullOrWhiteSpace(text);

    private static bool TryGetNumber(object value, out decimal number)
    {
        try
        {
            number = value switch
            {
                byte or sbyte or short or ushort or int or uint or long or ulong or decimal =>
                    Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                float single when !float.IsNaN(single) && !float.IsInfinity(single) =>
                    Convert.ToDecimal(single, CultureInfo.InvariantCulture),
                double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue) =>
                    Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture),
                _ => default,
            };

            return value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal
                || value is float singleValue && !float.IsNaN(singleValue) && !float.IsInfinity(singleValue)
                || value is double finiteDouble && !double.IsNaN(finiteDouble) && !double.IsInfinity(finiteDouble);
        }
        catch (OverflowException)
        {
            number = default;
            return false;
        }
    }

    private static string Format(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
