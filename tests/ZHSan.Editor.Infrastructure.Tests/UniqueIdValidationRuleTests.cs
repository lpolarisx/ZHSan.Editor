using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class UniqueIdValidationRuleTests
{
    [Fact]
    public void Validate_ReportsEveryRecordWithDuplicateId()
    {
        var rule = new UniqueIdValidationRule();
        var context = CreateContext(1, 2, 1, 1);

        var issues = rule.Validate(context).ToArray();

        Assert.Equal(3, issues.Length);
        Assert.All(issues, issue =>
        {
            Assert.Equal("test", issue.ConfigKey);
            Assert.Equal(1, issue.ItemId);
            Assert.Equal("Id", issue.PropertyName);
        });
    }

    [Fact]
    public void Validate_AcceptsUniqueAndMissingIds()
    {
        var rule = new UniqueIdValidationRule();
        var context = CreateContext(1, null, 2);

        Assert.Empty(rule.Validate(context));
    }

    private static TableValidationContext CreateContext(params int?[] ids)
    {
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition("test", "测试", "测试", "Test.json", typeof(object)),
            Items = [],
        };
        var project = new EditorProject
        {
            ArchivePath = "test.dat",
            Documents = [document],
        };
        var items = ids
            .Select((id, index) => new ValidationItem(new object(), index, id))
            .ToArray();

        return new TableValidationContext(project, document, items);
    }
}
