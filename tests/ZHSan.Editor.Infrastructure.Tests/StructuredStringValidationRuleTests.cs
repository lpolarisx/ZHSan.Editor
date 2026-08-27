using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class StructuredStringValidationRuleTests
{
    private readonly StructuredStringValidationRule _rule = new();

    [Fact]
    public void Validate_ReportsMalformedAndUnsafeConditionOperators()
    {
        var malformed = Validate(ConfigStructuredStringKind.ConditionIds, "997 10 bad 996");

        Assert.Contains(malformed, issue => issue.Severity == ValidationSeverity.Error && issue.Message.Contains("bad"));
        Assert.Contains(malformed, issue => issue.Message.Contains("997"));
        Assert.Contains(malformed, issue => issue.Message.Contains("996"));
    }

    [Fact]
    public void Validate_ReportsDuplicatesAccordingToRuntimeBehavior()
    {
        var ids = Validate(ConfigStructuredStringKind.InfluenceIds, "10 10");
        var weighted = Validate(ConfigStructuredStringKind.WeightedConditionPairs, "20 1 20 2");

        Assert.Contains(ids, issue => issue.Severity == ValidationSeverity.Warning && issue.Message.Contains("只保留第一次"));
        Assert.Contains(weighted, issue => issue.Severity == ValidationSeverity.Error && issue.Message.Contains("加载失败"));
    }

    [Fact]
    public void Validate_AcceptsWellFormedConditionExpression()
    {
        Assert.Empty(Validate(ConfigStructuredStringKind.ConditionIds, "10 996 11 997 12"));
    }

    private IReadOnlyList<ValidationIssue> Validate(ConfigStructuredStringKind kind, string value)
    {
        var property = new ConfigPropertyDefinition("Rules", "规则", typeof(string), true, 0)
        {
            StructuredString = new ConfigStructuredStringDefinition(kind, "targets"),
        };
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition("source", "来源", "测试", "Source.json", typeof(object)),
            Items = [],
        };
        var project = new EditorProject { ArchivePath = "test.dat", Documents = [document] };
        var context = new FieldValidationContext(
            project,
            document,
            new ValidationItem(new object(), 0, 7),
            property,
            value);
        return _rule.Validate(context).ToArray();
    }
}
