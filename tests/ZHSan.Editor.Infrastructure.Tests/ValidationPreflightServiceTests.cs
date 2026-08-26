using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Validation;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ValidationPreflightServiceTests
{
    [Fact]
    public void Evaluate_SaveAllowsErrorsButPublishRejectsThem()
    {
        var validationService = new ConfigValidationService(
            new ReflectionConfigMetadataProvider(),
            fieldRules: [new ErrorRule()]);
        var service = new ValidationPreflightService(validationService);
        var project = CreateProject();

        var save = service.Evaluate(project, ValidationOperation.Save);
        var publish = service.Evaluate(project, ValidationOperation.Publish);

        Assert.True(save.CanProceed);
        Assert.True(save.Report.HasErrors);
        Assert.False(publish.CanProceed);
        Assert.True(publish.Report.HasErrors);
    }

    [Fact]
    public void Evaluate_PublishAllowsProjectWithoutErrors()
    {
        var validationService = new ConfigValidationService(
            new ReflectionConfigMetadataProvider());
        var service = new ValidationPreflightService(validationService);

        var result = service.Evaluate(CreateProject(), ValidationOperation.Publish);

        Assert.True(result.CanProceed);
        Assert.Empty(result.Report.Issues);
    }

    private static EditorProject CreateProject()
    {
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition("test", "测试", "测试", "Test.json", typeof(TestItem)),
            Items = [new TestItem { Id = 1, Name = "测试" }],
        };
        return new EditorProject
        {
            ArchivePath = "test.dat",
            Documents = [document],
            ActiveDocument = document,
        };
    }

    private sealed class ErrorRule : IFieldValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(FieldValidationContext context)
        {
            if (context.Property.Name == nameof(TestItem.Name))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error,
                    context.Document.Definition.Key,
                    context.Item.Id,
                    context.Property.Name,
                    "测试错误");
            }
        }
    }

    private sealed class TestItem
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }
}
