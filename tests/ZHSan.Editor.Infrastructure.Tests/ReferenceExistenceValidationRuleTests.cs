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

    [Theory]
    [InlineData(-1, 7, false)]
    [InlineData(7, 7, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 7, true)]
    [InlineData(404, 7, true)]
    [InlineData(-2, 7, true)]
    public void Validate_MayorId_OnlyMinusOneMeansNoMayor(int mayorId, int personId, bool expectedError)
    {
        var scenario = CreateProject(ConfigScope.Scenario,
            CreateDocument("persons", typeof(PersonConfig), ConfigScope.Scenario,
                new PersonConfig { Id = personId, SurName = "关", GivenName = "羽" }),
            CreateDocument("architectures", typeof(ArchitectureConfig), ConfigScope.Scenario,
                new ArchitectureConfig { Id = 1, MayorId = mayorId }));
        var service = new ConfigValidationService(new ReflectionConfigMetadataProvider(),
            crossTableRules: [new ReferenceExistenceValidationRule()]);

        var report = service.Validate(scenario, ValidationScope.CrossTable);

        Assert.Equal(expectedError, report.Issues.Any(issue =>
            issue.PropertyName == nameof(ArchitectureConfig.MayorId)));
    }

    [Theory]
    [InlineData(-1, 7, false)]
    [InlineData(7, 7, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 7, true)]
    [InlineData(404, 7, true)]
    [InlineData(-2, 7, true)]
    public void Validate_UpgradingTechnique_UsesCommonTechnologyAndMinusOneForNone(
        int upgradingTechnique, int techniqueId, bool expectedError)
    {
        var common = CreateProject(CreateDocument("techniques", typeof(TechniqueConfig),
            new TechniqueConfig { Id = techniqueId, Name = "科技" }));
        var scenario = CreateProject(ConfigScope.Scenario,
            CreateDocument("factions", typeof(FactionConfig), ConfigScope.Scenario,
                new FactionConfig { Id = 1, UpgradingTechnique = upgradingTechnique }));
        var service = new ConfigValidationService(new ReflectionConfigMetadataProvider(),
            crossTableRules: [new ReferenceExistenceValidationRule()]);

        var report = service.Validate(scenario, [common, scenario], ValidationScope.CrossTable);
        var issues = report.Issues.Where(issue =>
            issue.PropertyName == nameof(FactionConfig.UpgradingTechnique)).ToArray();

        Assert.Equal(expectedError, issues.Length > 0);
        if (expectedError)
        {
            Assert.Contains("Common:techniques", Assert.Single(issues).Message);
        }
    }

    [Theory]
    [InlineData(-1, 7, false)]
    [InlineData(7, 7, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 7, true)]
    [InlineData(404, 7, true)]
    [InlineData(-2, 7, true)]
    public void Validate_MilitaryLeader_OnlyMinusOneMeansNoCommander(int leaderId, int personId, bool expectedError)
    {
        var scenario = CreateProject(ConfigScope.Scenario,
            CreateDocument("persons", typeof(PersonConfig), ConfigScope.Scenario,
                new PersonConfig { Id = personId, SurName = "关", GivenName = "羽" }),
            CreateDocument("militaries", typeof(MilitaryConfig), ConfigScope.Scenario,
                new MilitaryConfig { Id = 1, LeaderID = leaderId }));
        var service = new ConfigValidationService(new ReflectionConfigMetadataProvider(),
            crossTableRules: [new ReferenceExistenceValidationRule()]);

        var report = service.Validate(scenario, ValidationScope.CrossTable);

        Assert.Equal(expectedError, report.Issues.Any(issue =>
            issue.PropertyName == nameof(MilitaryConfig.LeaderID)));
    }

    [Theory]
    [InlineData(-1, 7, false)]
    [InlineData(7, 7, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 7, true)]
    [InlineData(404, 7, true)]
    [InlineData(-2, 7, true)]
    public void Validate_SectionTargets_OnlyMinusOneMeansUnset(int targetId, int existingId, bool expectedError)
    {
        var section = new SectionConfig
        {
            Id = 99, OrientationFactionID = targetId, OrientationSectionID = targetId,
            OrientationStateID = targetId, OrientationArchitectureID = targetId
        };
        var scenario = CreateProject(ConfigScope.Scenario,
            CreateDocument("sections", typeof(SectionConfig), ConfigScope.Scenario,
                section, new SectionConfig { Id = existingId }),
            CreateDocument("factions", typeof(FactionConfig), ConfigScope.Scenario,
                new FactionConfig { Id = existingId }),
            CreateDocument("states", typeof(StateConfig), ConfigScope.Scenario,
                new StateConfig { Id = existingId }),
            CreateDocument("architectures", typeof(ArchitectureConfig), ConfigScope.Scenario,
                new ArchitectureConfig { Id = existingId }));
        var service = new ConfigValidationService(new ReflectionConfigMetadataProvider(),
            crossTableRules: [new ReferenceExistenceValidationRule()]);

        var report = service.Validate(scenario, ValidationScope.CrossTable);

        foreach (var property in new[] { nameof(SectionConfig.OrientationFactionID), nameof(SectionConfig.OrientationSectionID),
                     nameof(SectionConfig.OrientationStateID), nameof(SectionConfig.OrientationArchitectureID) })
        {
            Assert.Equal(expectedError, report.Issues.Any(issue =>
                issue.ConfigKey == "sections" && issue.ItemId == 99 && issue.PropertyName == property));
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("2", false)]
    [InlineData("2 5", false)]
    [InlineData("2 404", true)]
    [InlineData("2 bad", true)]
    public void Validate_ArchitectureResourceKinds_ReferenceCommonResourceKinds(string resourceKinds, bool expectedError)
    {
        var common = CreateProject(ConfigScope.Common,
            CreateDocument("resource-kinds", typeof(ResourceKindConfig), ConfigScope.Common,
                new ResourceKindConfig { Id = 2, Name = "铁矿" }, new ResourceKindConfig { Id = 5, Name = "马场" }));
        var scenario = CreateProject(ConfigScope.Scenario,
            CreateDocument("architectures", typeof(ArchitectureConfig), ConfigScope.Scenario,
                new ArchitectureConfig { Id = 1, MayorId = -1, ResourceKindsString = resourceKinds }));
        var service = new ConfigValidationService(new ReflectionConfigMetadataProvider(),
            [new StructuredStringValidationRule()], crossTableRules: [new ReferenceExistenceValidationRule()]);

        var report = service.Validate(scenario, [common, scenario]);

        Assert.Equal(expectedError, report.Issues.Any(issue =>
            issue.PropertyName == nameof(ArchitectureConfig.ResourceKindsString)));
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
