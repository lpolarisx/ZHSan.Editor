using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Exporting;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Validation;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ConfigExportServiceTests
{
    [Fact]
    public async Task ExportDocumentAsync_RunsValidationAndAllowsWorkingDataWithErrors()
    {
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition("test", "测试", "测试", "Test.json", typeof(TestItem)),
            Items = [new TestItem { Id = 1, Name = string.Empty }],
        };
        var project = new EditorProject { ArchivePath = "Current.dat", Documents = [document] };
        var validation = new ConfigValidationService(
            new ReflectionConfigMetadataProvider(),
            fieldRules: [new RequiredNameRule()]);
        var writer = new FakeExportWriter();
        var service = new ConfigExportService(writer, new ValidationPreflightService(validation));

        var result = await service.ExportDocumentAsync(project, document, "Test.json");

        Assert.True(result.ValidationReport.HasErrors);
        Assert.True(writer.WasDocumentWritten);
        Assert.Single(result.WriteResult.Successes);
    }

    private sealed class FakeExportWriter : IConfigExportWriter
    {
        public bool WasDocumentWritten { get; private set; }

        public Task<ConfigExportSuccess> WriteDocumentAsync(
            string destinationPath,
            ConfigDocument document,
            CancellationToken cancellationToken = default)
        {
            WasDocumentWritten = true;
            return Task.FromResult(new ConfigExportSuccess(
                document.Definition.Key,
                document.Definition.DisplayName,
                document.Definition.EntryName,
                Path.GetFullPath(destinationPath),
                document.Items.Count));
        }

        public Task<ConfigExportWriteResult> WriteProjectDirectoryAsync(
            string destinationDirectory,
            IReadOnlyList<ConfigDocument> documents,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RequiredNameRule : IFieldValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(FieldValidationContext context)
        {
            if (context.Property.Name == nameof(TestItem.Name) &&
                string.IsNullOrWhiteSpace((string?)context.Value))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error,
                    context.Document.Definition.Key,
                    context.Item.Id,
                    context.Property.Name,
                    "名称不能为空。");
            }
        }
    }

    private sealed class TestItem
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }
}
