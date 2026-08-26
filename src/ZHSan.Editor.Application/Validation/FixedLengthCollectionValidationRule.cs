using System.Collections;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public sealed class FixedLengthCollectionValidationRule : IFieldValidationRule
{
    public IEnumerable<ValidationIssue> Validate(FieldValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Property.Validation.ExpectedCollectionLength is not { } expectedLength)
        {
            yield break;
        }

        if (context.Value is ICollection collection && collection.Count == expectedLength)
        {
            yield break;
        }

        var actualLength = context.Value is ICollection actualCollection
            ? actualCollection.Count.ToString()
            : context.Value is null ? "空" : "非集合值";
        yield return new ValidationIssue(
            ValidationSeverity.Error,
            context.Document.Definition.Key,
            context.Item.Id,
            context.Property.Name,
            $"{context.Property.DisplayName} 必须包含 {expectedLength} 个元素，当前为 {actualLength}。");
    }
}
