using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class FixedLengthCollectionValidationRuleTests
{
    [Fact]
    public void Validate_AcceptsCollectionWithExpectedLength()
    {
        var rule = new FixedLengthCollectionValidationRule();

        Assert.Empty(rule.Validate(CreateContext(new int[10], 10)));
    }

    [Fact]
    public void Validate_ReportsWrongOrMissingCollectionLength()
    {
        var rule = new FixedLengthCollectionValidationRule();

        var shortIssue = Assert.Single(rule.Validate(CreateContext(new int[9], 10)));
        var nullIssue = Assert.Single(rule.Validate(CreateContext(null, 10)));

        Assert.Equal("test", shortIssue.ConfigKey);
        Assert.Equal(7, shortIssue.ItemId);
        Assert.Equal("GenerationChance", shortIssue.PropertyName);
        Assert.Contains("10", shortIssue.Message);
        Assert.Contains("9", shortIssue.Message);
        Assert.Contains("空", nullIssue.Message);
    }

    [Fact]
    public void ValidationMetadata_RejectsNegativeExpectedLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ConfigPropertyValidation(expectedCollectionLength: -1));
    }

    private static FieldValidationContext CreateContext(object? value, int expectedLength)
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
        var property = new ConfigPropertyDefinition(
            "GenerationChance",
            "GenerationChance",
            typeof(int[]),
            true,
            0)
        {
            Validation = new ConfigPropertyValidation(expectedCollectionLength: expectedLength),
        };

        return new FieldValidationContext(
            project,
            document,
            new ValidationItem(new object(), 0, 7),
            property,
            value);
    }
}
