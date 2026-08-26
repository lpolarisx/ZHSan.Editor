using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public sealed class ReferenceExistenceValidationRule : ICrossTableValidationRule
{
    public IEnumerable<ValidationIssue> Validate(CrossTableValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var reference in context.ReferenceIndex.References)
        {
            if (context.ReferenceIndex.ContainsTarget(reference.TargetConfigKey, reference.TargetId))
            {
                continue;
            }

            yield return new ValidationIssue(
                ValidationSeverity.Error,
                reference.ConfigKey,
                reference.RecordId,
                reference.Property.Name,
                $"{reference.Property.DisplayName} 引用的 {reference.TargetConfigKey} ID {reference.TargetId} 不存在。");
        }
    }
}
