using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Publishing;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Validation;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class PublishArchiveServiceTests
{
    [Fact]
    public async Task PublishAsync_WithValidationErrors_IsBlockedBeforeRepositoryWrite()
    {
        var project = CreateProject(string.Empty);
        var repository = new FakeRepository();
        var validation = new ConfigValidationService(
            new ReflectionConfigMetadataProvider(),
            fieldRules: [new RequiredNameRule()]);
        var service = new PublishArchiveService(
            repository,
            new ValidationPreflightService(validation));

        var result = await service.PublishAsync(project, "Release.dat");

        Assert.False(result.Published);
        Assert.True(result.ValidationReport.HasErrors);
        Assert.False(repository.WasPublished);
        Assert.Equal(0, result.ConfigCount);
    }

    [Fact]
    public async Task PublishAsync_ValidProject_PublishesAndReportsContents()
    {
        var project = CreateProject("有效");
        var repository = new FakeRepository();
        var validation = new ConfigValidationService(new ReflectionConfigMetadataProvider());
        var service = new PublishArchiveService(
            repository,
            new ValidationPreflightService(validation));

        var result = await service.PublishAsync(project, "Release.dat");

        Assert.True(result.Published);
        Assert.Empty(result.ValidationReport.Issues);
        Assert.True(repository.WasPublished);
        Assert.Equal(Path.GetFullPath("Release.dat"), repository.DestinationPath);
        Assert.Equal(1, result.ConfigCount);
        Assert.Equal(1, result.ItemCount);
    }

    private static EditorProject CreateProject(string name)
    {
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition("test", "测试", "测试", "Test.json", typeof(TestItem)),
            Items = [new TestItem { Id = 1, Name = name }],
        };
        return new EditorProject
        {
            ArchivePath = "Current.dat",
            Documents = [document],
            ActiveDocument = document,
        };
    }

    private sealed class FakeRepository : IGameDataArchiveRepository
    {
        public bool WasPublished { get; private set; }

        public string? DestinationPath { get; private set; }

        public Task PublishAsync(
            EditorProject project,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            WasPublished = true;
            DestinationPath = destinationPath;
            return Task.CompletedTask;
        }

        public Task<EditorProject> LoadAsync(
            string archivePath,
            IReadOnlyList<ConfigDefinition> definitions,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveAsync(EditorProject project, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveDocumentAsync(
            EditorProject project,
            ConfigDocument document,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveAsAsync(
            EditorProject project,
            string destinationPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveCopyAsync(
            EditorProject project,
            string destinationPath,
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
