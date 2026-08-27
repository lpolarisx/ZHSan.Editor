using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public sealed class StructuredStringValidationRule : IFieldValidationRule
{
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

        if (definition.Kind == ConfigStructuredStringKind.ConditionIds)
        {
            var expression = ConfigStructuredStringCodec.ParseConditionExpression(value);
            foreach (var error in expression.Errors)
            {
                yield return CreateIssue(context, ValidationSeverity.Error, error);
            }

            foreach (var duplicate in expression.Items
                         .SelectMany(group => group.Terms)
                         .GroupBy(term => term.ConditionId)
                         .Where(group => group.Count() > 1))
            {
                yield return CreateIssue(
                    context,
                    ValidationSeverity.Warning,
                    $"条件 ID {duplicate.Key} 重复；游戏加载时只保留第一次出现的位置。");
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
