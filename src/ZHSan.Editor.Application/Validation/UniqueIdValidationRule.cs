using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public sealed class UniqueIdValidationRule : ITableValidationRule
{
    public IEnumerable<ValidationIssue> Validate(TableValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var group in context.Items
                     .Where(item => item.Id.HasValue)
                     .GroupBy(item => item.Id!.Value)
                     .Where(group => group.Count() > 1))
        {
            var count = group.Count();
            foreach (var item in group)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error,
                    context.Document.Definition.Key,
                    item.Id,
                    "Id",
                    $"ID {item.Id} 在当前配置表中重复，共出现 {count} 次。");
            }
        }
    }
}
