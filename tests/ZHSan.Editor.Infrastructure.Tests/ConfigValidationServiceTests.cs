using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Validation;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ConfigValidationServiceTests
{
    [Fact]
    public void Validate_RunsAllLayersAndBuildsLocatedContexts()
    {
        var fieldRule = new CapturingFieldRule();
        var tableRule = new CapturingTableRule();
        var crossTableRule = new CapturingCrossTableRule();
        var service = new ConfigValidationService(
            new ReflectionConfigMetadataProvider(),
            [fieldRule],
            [tableRule],
            [crossTableRule]);

        var report = service.Validate(CreateProject());

        Assert.Equal(3, report.Issues.Count);
        Assert.True(report.HasErrors);
        Assert.Equal(1, report.ErrorCount);
        Assert.Equal(1, report.WarningCount);
        Assert.Equal(1, report.InformationCount);

        Assert.Equal(2, fieldRule.Contexts.Count);
        var nameContext = Assert.Single(fieldRule.Contexts, context => context.Property.Name == "Name");
        Assert.Equal("test", nameContext.Document.Definition.Key);
        Assert.Equal(0, nameContext.Item.Index);
        Assert.Equal(42, nameContext.Item.Id);
        Assert.Equal("Alpha", nameContext.Value);

        var tableContext = Assert.Single(tableRule.Contexts);
        Assert.Equal(42, Assert.Single(tableContext.Items).Id);

        var crossTableContext = Assert.Single(crossTableRule.Contexts);
        Assert.Same(tableContext, crossTableContext.Tables["TEST"]);
    }

    [Fact]
    public void Validate_WithSelectedScope_OnlyRunsThatLayer()
    {
        var fieldRule = new CapturingFieldRule();
        var tableRule = new CapturingTableRule();
        var crossTableRule = new CapturingCrossTableRule();
        var service = new ConfigValidationService(
            new ReflectionConfigMetadataProvider(),
            [fieldRule],
            [tableRule],
            [crossTableRule]);

        var report = service.Validate(CreateProject(), ValidationScope.Table);

        Assert.Single(report.Issues);
        Assert.Empty(fieldRule.Contexts);
        Assert.Single(tableRule.Contexts);
        Assert.Empty(crossTableRule.Contexts);
    }

    [Fact]
    public void Validate_WithCancelledToken_StopsBeforeRulesRun()
    {
        var tableRule = new CapturingTableRule();
        var service = new ConfigValidationService(
            new ReflectionConfigMetadataProvider(),
            tableRules: [tableRule]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            service.Validate(CreateProject(), cancellationToken: cancellation.Token));
        Assert.Empty(tableRule.Contexts);
    }

    private static EditorProject CreateProject()
    {
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition("test", "测试", "测试", "Test.json", typeof(TestItem)),
            Items = [new TestItem { Id = 42, Name = "Alpha" }],
        };

        return new EditorProject
        {
            ArchivePath = "test.dat",
            Documents = [document],
            ActiveDocument = document,
        };
    }

    private sealed class CapturingFieldRule : IFieldValidationRule
    {
        public List<FieldValidationContext> Contexts { get; } = [];

        public IEnumerable<ValidationIssue> Validate(FieldValidationContext context)
        {
            Contexts.Add(context);
            if (context.Property.Name == "Name")
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Warning,
                    context.Document.Definition.Key,
                    context.Item.Id,
                    context.Property.Name,
                    "字段问题");
            }
        }
    }

    private sealed class CapturingTableRule : ITableValidationRule
    {
        public List<TableValidationContext> Contexts { get; } = [];

        public IEnumerable<ValidationIssue> Validate(TableValidationContext context)
        {
            Contexts.Add(context);
            yield return new ValidationIssue(
                ValidationSeverity.Information,
                context.Document.Definition.Key,
                null,
                null,
                "单表问题");
        }
    }

    private sealed class CapturingCrossTableRule : ICrossTableValidationRule
    {
        public List<CrossTableValidationContext> Contexts { get; } = [];

        public IEnumerable<ValidationIssue> Validate(CrossTableValidationContext context)
        {
            Contexts.Add(context);
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                "test",
                42,
                "Id",
                "跨表问题");
        }
    }

    private sealed class TestItem
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }
}
