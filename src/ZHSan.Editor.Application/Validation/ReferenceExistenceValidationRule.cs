using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public sealed class ReferenceExistenceValidationRule : ICrossTableValidationRule
{
    public IEnumerable<ValidationIssue> Validate(CrossTableValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var reference in context.ReferenceIndex.References)
        {
            if (reference.Scope != context.Project.Scope ||
                !context.ReferenceIndex.IsScopeIndexed(reference.TargetScope) ||
                context.ReferenceIndex.ContainsTarget(reference.TargetAddress, reference.TargetId))
            {
                continue;
            }

            yield return new ValidationIssue(
                ValidationSeverity.Error,
                reference.ConfigKey,
                reference.RecordId,
                reference.Property.Name,
                $"{reference.Property.DisplayName} 引用的 {reference.TargetAddress} ID {reference.TargetId} 不存在。",
                reference.Scope);
        }
    }
}
