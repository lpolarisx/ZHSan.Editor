using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public sealed class StructuredStringValidationRule : IFieldValidationRule
{
    private const int NegateNextConditionId = 996;
    private const int OrConditionId = 997;

    public IEnumerable<ValidationIssue> Validate(FieldValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Property.StructuredString is not { } definition)
        {
            yield break;
        }

        if (context.Value is not string value)
        {
            yield return CreateIssue(context, ValidationSeverity.Error, "结构化规则字段必须是文本。");
            yield break;
        }

        if (definition.Kind == ConfigStructuredStringKind.WeightedConditionPairs)
        {
            var result = ConfigStructuredStringCodec.ParseWeightedConditions(value);
            foreach (var error in result.Errors)
            {
                yield return CreateIssue(context, ValidationSeverity.Error, error);
            }

            foreach (var duplicate in result.Items.GroupBy(item => item.ConditionId).Where(group => group.Count() > 1))
            {
                yield return CreateIssue(
                    context,
                    ValidationSeverity.Error,
                    $"条件 ID {duplicate.Key} 重复；游戏使用字典加载权重，重复项会导致该字段加载失败。");
            }

            yield break;
        }

        var ids = ConfigStructuredStringCodec.ParseIds(value);
        foreach (var error in ids.Errors)
        {
            yield return CreateIssue(context, ValidationSeverity.Error, error);
        }

        foreach (var duplicate in ids.Items.GroupBy(id => id).Where(group => group.Count() > 1))
        {
            yield return CreateIssue(
                context,
                ValidationSeverity.Warning,
                $"ID {duplicate.Key} 重复；游戏加载时只保留第一次出现的位置。");
        }

        if (definition.Kind != ConfigStructuredStringKind.ConditionIds)
        {
            yield break;
        }

        for (var index = 0; index < ids.Items.Count; index++)
        {
            if (ids.Items[index] == OrConditionId &&
                (index == 0 || index == ids.Items.Count - 1 || ids.Items[index - 1] == OrConditionId))
            {
                yield return CreateIssue(
                    context,
                    ValidationSeverity.Error,
                    "条件 997（或以下条件）不能位于开头、结尾或紧邻另一个 997，否则会产生空的“或”分组。");
            }

            if (ids.Items[index] == NegateNextConditionId &&
                (index == ids.Items.Count - 1 || ids.Items[index + 1] == OrConditionId))
            {
                yield return CreateIssue(
                    context,
                    ValidationSeverity.Error,
                    "条件 996（否定下一项）后必须紧跟一个普通条件。");
            }
        }
    }

    private static ValidationIssue CreateIssue(
        FieldValidationContext context,
        ValidationSeverity severity,
        string message) =>
        new(
            severity,
            context.Document.Definition.Key,
            context.Item.Id,
            context.Property.Name,
            $"{context.Property.DisplayName}：{message}");
}
