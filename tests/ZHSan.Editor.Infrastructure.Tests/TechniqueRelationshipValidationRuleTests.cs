using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class TechniqueRelationshipValidationRuleTests
{
    [Fact]
    public void Validate_AcceptsConsistentAcyclicChain()
    {
        var context = CreateContext(
            CreateTechnique(1, 0, 2),
            CreateTechnique(2, 1, 3),
            CreateTechnique(3, 2, 0));

        Assert.Empty(new TechniqueRelationshipValidationRule().Validate(context));
    }

    [Fact]
    public void Validate_ReportsInconsistentPredecessorAndSuccessor()
    {
        var context = CreateContext(
            CreateTechnique(1, 0, 2),
            CreateTechnique(2, 0, 0),
            CreateTechnique(3, 4, 0),
            CreateTechnique(4, 0, 0));

        var issues = new TechniqueRelationshipValidationRule().Validate(context).ToArray();

        var postIssue = Assert.Single(issues, issue =>
            issue.ItemId == 1 && issue.PropertyName == nameof(TechniqueConfig.PostID));
        var preIssue = Assert.Single(issues, issue =>
            issue.ItemId == 3 && issue.PropertyName == nameof(TechniqueConfig.PreID));
        Assert.Contains(nameof(TechniqueConfig.PreID), postIssue.Message);
        Assert.Contains(nameof(TechniqueConfig.PostID), preIssue.Message);
    }

    [Fact]
    public void Validate_ReportsSelfReferencesWithoutDuplicateCycleIssues()
    {
        var context = CreateContext(CreateTechnique(7, 7, 7));

        var issues = new TechniqueRelationshipValidationRule().Validate(context).ToArray();

        Assert.Equal(2, issues.Length);
        Assert.Contains(issues, issue => issue.PropertyName == nameof(TechniqueConfig.PreID));
        Assert.Contains(issues, issue => issue.PropertyName == nameof(TechniqueConfig.PostID));
        Assert.All(issues, issue => Assert.Contains("自身", issue.Message));
    }

    [Fact]
    public void Validate_ReportsEveryRelationshipInCycle()
    {
        var context = CreateContext(
            CreateTechnique(1, 3, 2),
            CreateTechnique(2, 1, 3),
            CreateTechnique(3, 2, 1));

        var issues = new TechniqueRelationshipValidationRule().Validate(context).ToArray();

        Assert.Equal(3, issues.Length);
        Assert.All(issues, issue =>
        {
            Assert.Equal(nameof(TechniqueConfig.PreID), issue.PropertyName);
            Assert.Contains("循环依赖", issue.Message);
            Assert.Contains("1、2、3", issue.Message);
        });
        Assert.Equal([1, 2, 3], issues.Select(issue => issue.ItemId).Order().ToArray());
    }

    [Fact]
    public void Validate_IgnoresMissingTargetsHandledByReferenceRule()
    {
        var context = CreateContext(CreateTechnique(1, 404, 405));

        Assert.Empty(new TechniqueRelationshipValidationRule().Validate(context));
    }

    private static CrossTableValidationContext CreateContext(params TechniqueConfig[] techniques)
    {
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "techniques",
                "技术",
                "技术与能力",
                "Techniques.json",
                typeof(TechniqueConfig)),
            Items = techniques.Cast<object>().ToArray(),
        };
        var project = new EditorProject
        {
            ArchivePath = "test.dat",
            Documents = [document],
            ActiveDocument = document,
        };
        var table = new TableValidationContext(
            project,
            document,
            techniques
                .Select((technique, index) =>
                    new ValidationItem(technique, index, technique.Id))
                .ToArray());
        var index = new ConfigReferenceIndex(new ReflectionConfigMetadataProvider());
        index.Rebuild(project);

        return new CrossTableValidationContext(
            project,
            new Dictionary<string, TableValidationContext>(StringComparer.OrdinalIgnoreCase)
            {
                ["techniques"] = table,
            },
            index);
    }

    private static TechniqueConfig CreateTechnique(int id, int preId, int postId) =>
        new()
        {
            Id = id,
            Name = $"技术 {id}",
            PreID = preId,
            PostID = postId,
        };
}
