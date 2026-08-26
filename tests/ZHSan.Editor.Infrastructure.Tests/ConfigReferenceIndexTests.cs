using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ConfigReferenceIndexTests
{
    [Fact]
    public void Rebuild_IndexesTargetsScalarAndCollectionReferences()
    {
        var firstTechnique = new TechniqueConfig { Id = 1, Name = "基础技术" };
        var secondTechnique = new TechniqueConfig
        {
            Id = 2,
            Name = "进阶技术",
            PreID = 1,
            PostID = 0,
        };
        var military = new MilitaryKindConfig
        {
            Id = 10,
            Name = "进阶兵种",
            CreateTechnology = 1,
            LevelUpKindID = [11, 12],
            MorphToKindId = 0,
        };
        var project = CreateProject(
            CreateDocument("techniques", typeof(TechniqueConfig), firstTechnique, secondTechnique),
            CreateDocument("military-kinds", typeof(MilitaryKindConfig), military));
        var index = new ConfigReferenceIndex(new ReflectionConfigMetadataProvider());

        index.Rebuild(project);

        Assert.Equal(2, index.GetTargets("TECHNIQUES").Count);
        Assert.Equal("基础技术", index.GetTargets("techniques")[0].DisplayName);
        Assert.True(index.ContainsTarget("techniques", 1));
        Assert.False(index.ContainsTarget("techniques", 99));
        Assert.Contains(index.References, reference =>
            reference.ConfigKey == "techniques" &&
            reference.Property.Name == nameof(TechniqueConfig.PreID) &&
            reference.TargetId == 1);
        Assert.Equal(2, index.References.Count(reference =>
            reference.Property.Name == nameof(MilitaryKindConfig.LevelUpKindID)));
        Assert.DoesNotContain(index.References, reference => reference.TargetId == 0);
        Assert.Equal(2, index.GetReferencesTo("techniques", 1).Count);
    }

    [Fact]
    public void GetDeletionImpacts_GroupsIncomingReferencesAndExcludesSelectedSources()
    {
        var first = new TechniqueConfig { Id = 1, Name = "基础技术" };
        var second = new TechniqueConfig
        {
            Id = 2,
            Name = "进阶技术",
            PreID = 1,
        };
        var document = CreateDocument("techniques", typeof(TechniqueConfig), first, second);
        var index = new ConfigReferenceIndex(new ReflectionConfigMetadataProvider());
        index.Rebuild(CreateProject(document));

        var impact = Assert.Single(index.GetDeletionImpacts("techniques", [first]));

        Assert.Equal(1, impact.Target.Id);
        var reference = Assert.Single(impact.References);
        Assert.Equal(2, reference.RecordId);
        Assert.Equal("进阶技术", reference.RecordDisplayName);
        Assert.Equal(nameof(TechniqueConfig.PreID), reference.Property.Name);

        Assert.Empty(index.GetDeletionImpacts("techniques", [first, second]));
    }

    private static ConfigDocument CreateDocument(string key, Type itemType, params object[] items) =>
        new()
        {
            Definition = new ConfigDefinition(key, key, "测试", $"{key}.json", itemType),
            Items = items,
        };

    private static EditorProject CreateProject(params ConfigDocument[] documents) =>
        new()
        {
            ArchivePath = "test.dat",
            Documents = documents,
            ActiveDocument = documents.FirstOrDefault(),
        };
}
