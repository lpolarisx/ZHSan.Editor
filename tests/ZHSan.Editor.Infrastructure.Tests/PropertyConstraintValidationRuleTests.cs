using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class PropertyConstraintValidationRuleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ReportsMissingRequiredValue(string? value)
    {
        var rule = new PropertyConstraintValidationRule();
        var context = CreateContext(
            value,
            new ConfigPropertyValidation(isRequired: true));

        var issue = Assert.Single(rule.Validate(context));

        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal("test", issue.ConfigKey);
        Assert.Equal(7, issue.ItemId);
        Assert.Equal("Value", issue.PropertyName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_ReportsValueOutsideInclusiveRange(int value)
    {
        var rule = new PropertyConstraintValidationRule();
        var context = CreateContext(
            value,
            new ConfigPropertyValidation(minimum: 0, maximum: 100));

        Assert.Single(rule.Validate(context));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void Validate_AcceptsValueInsideInclusiveRange(int value)
    {
        var rule = new PropertyConstraintValidationRule();
        var context = CreateContext(
            value,
            new ConfigPropertyValidation(minimum: 0, maximum: 100));

        Assert.Empty(rule.Validate(context));
    }

    [Fact]
    public void ValidationMetadata_RejectsInvertedRange()
    {
        Assert.Throws<ArgumentException>(() =>
            new ConfigPropertyValidation(minimum: 2, maximum: 1));
    }

    private static FieldValidationContext CreateContext(
        object? value,
        ConfigPropertyValidation validation)
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
            "Value",
            "值",
            value?.GetType() ?? typeof(string),
            true,
            0)
        {
            Validation = validation,
        };

        return new FieldValidationContext(
            project,
            document,
            new ValidationItem(new object(), 0, 7),
            property,
            value);
    }
}
