using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameDatas;
using Microsoft.Xna.Framework;
using ZHSan.Editor.Infrastructure.Archives;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class LegacyCommonDataConverterTests
{
    [Fact]
    public async Task ConvertAsync_CreatesVerifiedArchiveWithAllCommonDataEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"zhsan-legacy-common-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "CommonData.json");
            var destinationPath = Path.Combine(directory, "CommonData.dat");
            await File.WriteAllTextAsync(sourcePath, CreateLegacyCommonDataJson());

            var converter = new LegacyCommonDataConverter(new GameDataConfigRegistry());
            var result = await converter.ConvertAsync(sourcePath, destinationPath);

            Assert.Equal(40, result.ConfigCount);
            Assert.Equal(41, result.EntryCount);
            Assert.Equal(9, result.ItemCount);
            Assert.Equal(destinationPath, result.DestinationPath);

            using (var zip = ZipFile.OpenRead(destinationPath))
            {
                Assert.Equal(41, zip.Entries.Count);
                Assert.Contains(zip.Entries, entry => entry.FullName == "Colors.json");
                Assert.Contains(zip.Entries, entry => entry.FullName == "FacilityKindLevels.json");
            }

            using var archive = GameDataArchive.Open(destinationPath);
            var condition = Assert.Single(archive.Load<List<ConditionConfig>>("Conditions.json")!);
            Assert.Equal(17, condition.Id);
            Assert.Equal(3, condition.KindId);

            var facilityKind = Assert.Single(archive.Load<List<FacilityKindConfig>>("FacilityKinds.json")!);
            Assert.True(facilityKind.IsDemolishable);
            var facilityLevel = Assert.Single(archive.Load<List<FacilityKindLevelConfig>>("FacilityKindLevels.json")!);
            Assert.Equal(120, facilityLevel.Id);
            Assert.Equal(12, facilityLevel.KindId);
            Assert.Equal(1, facilityLevel.Level);
            Assert.Equal(4, facilityLevel.ConcubineCapacity);

            var message = Assert.Single(archive.Load<List<PersonMessageConfig>>("PersonMessages.json")!);
            Assert.Equal(6, message.PersonId);
            Assert.Equal(["第一行", "第二行"], message.Messages);

            var officialTitle = Assert.Single(archive.Load<List<OfficialTitleKindConfig>>("OfficialTitleKinds.json")!);
            Assert.Equal(5000, officialTitle.ReputationCap);
            Assert.Equal(99, officialTitle.RequiredContribution);

            var settings = Assert.Single(archive.Load<List<PersonGeneratorSettingConfig>>("PersonGeneratorSettings.json")!);
            Assert.Equal(5, settings.FemaleChance);
            Assert.Empty(archive.Load<List<IdealTendencyKindConfig>>("IdealTendencyKinds.json")!);
            Assert.Empty(archive.Load<List<TrainPolicyConfig>>("TrainPolicies.json")!);

            var resourceKind = Assert.Single(archive.Load<List<ResourceKindConfig>>("ResourceKinds.json")!);
            Assert.Equal(2, resourceKind.Id);
            Assert.Equal("铁矿", resourceKind.Name);
            Assert.Equal("可锻造兵器", resourceKind.Description);

            var colors = archive.Load<List<Color>>("Colors.json");
            Assert.Equal((byte)30, Assert.Single(colors!).B);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ConvertAsync_DoesNotOverwriteSourceJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"zhsan-legacy-common-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "CommonData.json");
            await File.WriteAllTextAsync(sourcePath, CreateLegacyCommonDataJson());

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => new LegacyCommonDataConverter(new GameDataConfigRegistry())
                    .ConvertAsync(sourcePath, sourcePath));

            Assert.Contains("不能覆盖", exception.Message, StringComparison.Ordinal);
            Assert.StartsWith("{", await File.ReadAllTextAsync(sourcePath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("AllTreasureCreationSettings", null, "TreasureCreationSettings.json", false)]
    [InlineData("AllTreasureCreationSettings", null, "TreasureCreationSettings.json", true)]
    [InlineData("AllTreasureCreationSettings", "GameObjects", "TreasureCreationSettings.json", false)]
    [InlineData("AllTreasureCreationSettings", "GameObjects", "TreasureCreationSettings.json", true)]
    [InlineData("AllFacilityKinds", null, "FacilityKinds.json", false)]
    [InlineData("AllFacilityKinds", "FacilityKinds", "FacilityKindLevels.json", false)]
    [InlineData("AllTextMessages", null, "PersonMessages.json", false)]
    [InlineData("AllTextMessages", "textMessages", "PersonMessages.json", false)]
    [InlineData("PersonGeneratorSetting", null, "PersonGeneratorSettings.json", false)]
    [InlineData("AllColors", null, "Colors.json", false)]
    public async Task ConvertAsync_MissingDataWritesEmptyListAndReportsWarning(
        string rootProperty, string? collectionProperty, string entryName, bool useNull)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"zhsan-legacy-common-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var root = JsonNode.Parse(CreateLegacyCommonDataJson())!.AsObject();
            var parent = collectionProperty is null ? root : root[rootProperty]!.AsObject();
            var property = collectionProperty ?? rootProperty;
            if (useNull) parent[property] = null;
            else parent.Remove(property);

            var sourcePath = Path.Combine(directory, "CommonData.json");
            var destinationPath = Path.Combine(directory, "CommonData.dat");
            await File.WriteAllTextAsync(sourcePath, root.ToJsonString());
            var result = await new LegacyCommonDataConverter(new GameDataConfigRegistry())
                .ConvertAsync(sourcePath, destinationPath);

            var missingPath = collectionProperty is null ? rootProperty : $"{rootProperty}.{collectionProperty}";
            Assert.Single(result.Warnings, warning => warning == $"旧版 CommonData 缺少 {missingPath}。已用空列表保存。");
            using var zip = ZipFile.OpenRead(destinationPath);
            Assert.Equal(41, zip.Entries.Count);
            using var stream = zip.GetEntry(entryName)!.Open();
            using var data = JsonDocument.Parse(stream);
            Assert.Equal(0, data.RootElement.GetArrayLength());
            if (rootProperty == "AllFacilityKinds")
            {
                using var levelsStream = zip.GetEntry("FacilityKindLevels.json")!.Open();
                using var levels = JsonDocument.Parse(levelsStream);
                Assert.Equal(0, levels.RootElement.GetArrayLength());
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ConvertAsync_AllDataMissingWritesEveryEntryAsEmptyList()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"zhsan-legacy-common-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "CommonData.json");
            var destinationPath = Path.Combine(directory, "CommonData.dat");
            await File.WriteAllTextAsync(sourcePath, "{}");
            var result = await new LegacyCommonDataConverter(new GameDataConfigRegistry())
                .ConvertAsync(sourcePath, destinationPath);

            Assert.Equal(0, result.ItemCount);
            Assert.Equal(40, result.Warnings.Count);
            using var zip = ZipFile.OpenRead(destinationPath);
            Assert.Equal(41, zip.Entries.Count);
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var data = JsonDocument.Parse(stream);
                Assert.Equal(0, data.RootElement.GetArrayLength());
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ConvertAsync_InvalidExistingDataStillFailsWithoutReplacingDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"zhsan-legacy-common-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var root = JsonNode.Parse(CreateLegacyCommonDataJson())!.AsObject();
            root["AllTreasureCreationSettings"]!["GameObjects"] = "invalid";
            var sourcePath = Path.Combine(directory, "CommonData.json");
            var destinationPath = Path.Combine(directory, "CommonData.dat");
            await File.WriteAllTextAsync(sourcePath, root.ToJsonString());
            await File.WriteAllTextAsync(destinationPath, "original");

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LegacyCommonDataConverter(new GameDataConfigRegistry()).ConvertAsync(sourcePath, destinationPath));
            Assert.Equal("original", await File.ReadAllTextAsync(destinationPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateLegacyCommonDataJson() =>
        """
        {
          "AllArchitectureKinds":{"ArchitectureKinds":[]},
          "AllAttackDefaultKinds":{"GameObjects":[]},
          "AllAttackTargetKinds":{"GameObjects":[]},
          "AllBiographyAdjectives":[],
          "AllCastDefaultKinds":{"GameObjects":[]},
          "AllCastTargetKinds":{"GameObjects":[]},
          "AllCharacterKinds":[],
          "AllColors":[{"R":10,"G":20,"B":30,"A":255}],
          "AllCombatMethods":{"CombatMethods":[]},
          "AllConditionKinds":{"ConditionKinds":[]},
          "AllConditions":{"Conditions":[{"Key":17,"Value":{"ID":17,"Name":"测试条件","Kind":{"ID":3,"Name":"测试类型"},"Parameter":"1","Parameter2":""}}]},
          "AllEventEffectKinds":{"EventEffectKinds":[]},
          "AllEventEffects":{"EventEffects":[]},
          "AllFacilityKinds":{"FacilityKinds":[{"Key":12,"Value":{"ID":12,"Name":"测试设施","AILevel":2,"ArchitectureLimit":1,"FactionLimit":3,"PopulationRelated":true,"bukechaichu":true,"AIBuildConditionWeightString":"","PositionOccupied":2,"TechnologyNeeded":10,"PointCost":20,"FundCost":30,"MaintenanceCost":4,"Days":5,"Endurance":600,"InfluencesString":"7","ConditionTableString":"8","rongna":4}}]},
          "AllInfluenceKinds":{"InfluenceKinds":[]},
          "AllInfluences":{"Influences":[]},
          "AllInformationKinds":{"GameObjects":[]},
          "AllMilitaryKinds":{"MilitaryKinds":[]},
          "AllPersonGeneratorTypes":{"GameObjects":[]},
          "AllResourceKinds":[{"Key":2,"Value":{"ID":2,"Name":"铁矿","Description":"可锻造兵器"}}],
          "AllSectionAIDetails":{"SectionAIDetails":[]},
          "AllSkills":{"Skills":[]},
          "AllStratagems":{"Stratagems":[]},
          "AllStunts":{"Stunts":[]},
          "AllTechniques":{"Techniques":[]},
          "AllTerrainDetails":{"TerrainDetails":[]},
          "AllTextMessages":{"textMessages":[{"Key":{"key":6,"value":2},"Value":["第一行","第二行"]}]},
          "AllTileAnimations":{"Animations":[]},
          "AllTitleKinds":{"TitleKinds":[]},
          "AllTitles":{"Titles":[]},
          "AllTroopAnimations":{"Animations":[]},
          "AllTroopEventEffectKinds":{"EventEffectKinds":[]},
          "AllTroopEventEffects":{"EventEffects":[]},
          "PersonGeneratorSetting":{"ID":0,"femaleChance":5,"ChildrenFemaleChance":50,"bornLo":-15,"bornHi":-30,"debutLo":-5,"debutHi":10,"dieLo":30,"dieHi":99,"debutAtLeast":5},
          "suoyouguanjuezhonglei":{"guanjuedezhongleizidian":[{"Key":1,"Value":{"ID":1,"Name":"官职","shengwangshangxian":5000,"xuyaogongxiandu":99,"ShowDialog":true,"xuyaochengchi":2,"Loyalty":3}}]},
          "suoyouzainanzhonglei":{"zainanzhongleizidian":[]},
          "AllTreasureCreationSettings":{"GameObjects":[]},
          "allStatusEffects":[{"Key":1,"Value":{"ID":1,"Name":"状态","Duration":10,"StatusType":1,"TriggerConditions":"","Influences":""}}]
        }
        """;
}
