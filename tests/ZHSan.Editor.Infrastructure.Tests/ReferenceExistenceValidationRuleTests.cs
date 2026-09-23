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

    [Fact]
    public void Validate_ScenarioReference_UsesExplicitTargetScope()
    {
        var common = CreateProject(
            ConfigScope.Common,
            CreateDocument(
                "military-kinds",
                typeof(MilitaryKindConfig),
                ConfigScope.Common,
                new MilitaryKindConfig { Id = 7, Name = "骑兵" }));
        var scenario = CreateProject(
            ConfigScope.Scenario,
            CreateDocument(
                "persons",
                typeof(PersonConfig),
                ConfigScope.Scenario,
                new PersonConfig { Id = 7, Name = "关羽" }),
            CreateDocument(
                "militaries",
                typeof(MilitaryConfig),
                ConfigScope.Scenario,
                new MilitaryConfig { Id = 1, KindId = 404, LeaderID = 7 }));
        var service = new ConfigValidationService(
            new ReflectionConfigMetadataProvider(),
            crossTableRules: [new ReferenceExistenceValidationRule()]);

        var report = service.Validate(
            scenario,
            [common, scenario],
            ValidationScope.CrossTable);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(ConfigScope.Scenario, issue.Scope);
        Assert.Equal("militaries", issue.ConfigKey);
        Assert.Equal(nameof(MilitaryConfig.KindId), issue.PropertyName);
        Assert.Contains("Common:military-kinds", issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(report.Issues, candidate =>
            candidate.PropertyName == nameof(MilitaryConfig.LeaderID));
    }

    private static ConfigDocument CreateDocument(string key, Type itemType, params object[] items) =>
        CreateDocument(key, itemType, ConfigScope.Common, items);

    private static ConfigDocument CreateDocument(
        string key,
        Type itemType,
        ConfigScope scope,
        params object[] items) =>
        new()
        {
            Definition = new ConfigDefinition(key, key, "测试", $"{key}.json", itemType, scope),
            Items = items,
        };

    private static EditorProject CreateProject(params ConfigDocument[] documents) =>
        CreateProject(ConfigScope.Common, documents);

    private static EditorProject CreateProject(ConfigScope scope, params ConfigDocument[] documents) =>
        new()
        {
            Scope = scope,
            ArchivePath = "test.dat",
            Documents = documents,
            ActiveDocument = documents.FirstOrDefault(),
        };
}
