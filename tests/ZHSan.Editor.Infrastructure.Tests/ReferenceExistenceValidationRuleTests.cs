using GameDatas;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Validation;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ReferenceExistenceValidationRuleTests
{
    [Fact]
    public void Validate_ReportsEachMissingDeclaredReferenceAndSkipsEmptyValues()
    {
        var technique = new TechniqueConfig
        {
            Id = 1,
            Name = "技术",
            PreID = 0,
            PostID = 404,
            InfluencesString = "7 409",
        };
        var treasure = new TreasureCreationSettingConfig
        {
            Id = 2,
            Name = "宝物",
            EligibleInfluenceIDs = [7, 408],
        };
        var project = CreateProject(
            CreateDocument("techniques", typeof(TechniqueConfig), technique),
            CreateDocument("treasure-creation-settings", typeof(TreasureCreationSettingConfig), treasure),
            CreateDocument("influences", typeof(InfluenceConfig), new InfluenceConfig
            {
                Id = 7,
                Name = "有效影响",
                KindId = 3,
            }),
            CreateDocument("influence-kinds", typeof(InfluenceKindConfig), new InfluenceKindConfig
            {
                Id = 3,
                Name = "影响类型",
            }));
        var service = new ConfigValidationService(
            new ReflectionConfigMetadataProvider(),
            crossTableRules: [new ReferenceExistenceValidationRule()]);

        var report = service.Validate(project, ValidationScope.CrossTable);

        Assert.Equal(3, report.ErrorCount);
        Assert.Contains(report.Issues, issue =>
            issue.ConfigKey == "techniques" &&
            issue.ItemId == 1 &&
            issue.PropertyName == nameof(TechniqueConfig.PostID) &&
            issue.Message.Contains("404"));
        Assert.Contains(report.Issues, issue =>
            issue.ConfigKey == "treasure-creation-settings" &&
            issue.ItemId == 2 &&
            issue.PropertyName == nameof(TreasureCreationSettingConfig.EligibleInfluenceIDs) &&
            issue.Message.Contains("408"));
        Assert.Contains(report.Issues, issue =>
            issue.ConfigKey == "techniques" &&
            issue.ItemId == 1 &&
            issue.PropertyName == nameof(TechniqueConfig.InfluencesString) &&
            issue.Message.Contains("409"));
        Assert.DoesNotContain(report.Issues, issue => issue.Message.Contains(" ID 0 "));
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
