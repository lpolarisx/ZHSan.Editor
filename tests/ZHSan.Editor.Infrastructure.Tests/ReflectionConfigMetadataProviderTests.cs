using GameDatas;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ReflectionConfigMetadataProviderTests
{
    [Fact]
    public void GetProperties_PutsBaseIdentityFieldsFirst()
    {
        var provider = new ReflectionConfigMetadataProvider();

        var properties = provider.GetProperties(typeof(TechniqueConfig));

        Assert.Equal("Id", properties[0].Name);
        Assert.Equal("Name", properties[1].Name);
        Assert.Same(properties, provider.GetProperties(typeof(TechniqueConfig)));
    }

    [Fact]
    public void GetProperties_IncludesEditorOwnedValidationMetadata()
    {
        var provider = new ReflectionConfigMetadataProvider();

        var name = Assert.Single(
            provider.GetProperties(typeof(TechniqueConfig)),
            property => property.Name == "Name");
        var chance = Assert.Single(
            provider.GetProperties(typeof(StratagemConfig)),
            property => property.Name == "Chance");
        var combativity = Assert.Single(
            provider.GetProperties(typeof(StratagemConfig)),
            property => property.Name == "Combativity");

        Assert.True(name.Validation.IsRequired);
        Assert.Equal(0, chance.Validation.Minimum);
        Assert.Equal(100, chance.Validation.Maximum);
        Assert.False(combativity.Validation.HasNumericRange);
    }

    [Fact]
    public void GetProperties_IncludesExplicitCrossTableReferenceMetadata()
    {
        var provider = new ReflectionConfigMetadataProvider();

        var preId = Assert.Single(
            provider.GetProperties(typeof(TechniqueConfig)),
            property => property.Name == nameof(TechniqueConfig.PreID));
        var kindId = Assert.Single(
            provider.GetProperties(typeof(TitleConfig)),
            property => property.Name == nameof(TitleConfig.KindId));
        var levelUps = Assert.Single(
            provider.GetProperties(typeof(MilitaryKindConfig)),
            property => property.Name == nameof(MilitaryKindConfig.LevelUpKindID));
        var ordinaryField = Assert.Single(
            provider.GetProperties(typeof(TechniqueConfig)),
            property => property.Name == nameof(TechniqueConfig.Kind));

        Assert.Equal("techniques", preId.Reference?.TargetConfigKey);
        Assert.Equal(0, preId.Reference?.EmptyValue);
        Assert.Equal("title-kinds", kindId.Reference?.TargetConfigKey);
        Assert.False(kindId.Reference?.IsOptional);
        Assert.Equal("military-kinds", levelUps.Reference?.TargetConfigKey);
        Assert.Null(ordinaryField.Reference);
    }

    [Fact]
    public void GetProperties_IncludesExplicitStructuredRuleStringMetadata()
    {
        var provider = new ReflectionConfigMetadataProvider();

        var influences = Assert.Single(
            provider.GetProperties(typeof(TechniqueConfig)),
            property => property.Name == nameof(TechniqueConfig.InfluencesString));
        var conditions = Assert.Single(
            provider.GetProperties(typeof(TechniqueConfig)),
            property => property.Name == nameof(TechniqueConfig.ConditionTableString));
        var weights = Assert.Single(
            provider.GetProperties(typeof(TechniqueConfig)),
            property => property.Name == nameof(TechniqueConfig.AIConditionWeightString));

        Assert.Equal(ConfigStructuredStringKind.InfluenceIds, influences.StructuredString?.Kind);
        Assert.Equal("influences", influences.StructuredString?.TargetConfigKey);
        Assert.Equal(ConfigStructuredStringKind.ConditionIds, conditions.StructuredString?.Kind);
        Assert.Equal(ConfigStructuredStringKind.WeightedConditionPairs, weights.StructuredString?.Kind);
    }

    [Theory]
    [InlineData(typeof(CharacterKindConfig))]
    [InlineData(typeof(SkillConfig))]
    [InlineData(typeof(StuntConfig))]
    [InlineData(typeof(TitleConfig))]
    public void GetProperties_IncludesFixedGenerationChanceLength(Type itemType)
    {
        var provider = new ReflectionConfigMetadataProvider();

        var generationChance = Assert.Single(
            provider.GetProperties(itemType),
            property => property.Name == "GenerationChance");

        Assert.Equal(10, generationChance.Validation.ExpectedCollectionLength);
    }
}
