using System.Reflection;
using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Infrastructure.Configuration;

internal static class GameDataPropertyValidationMetadata
{
    private static readonly HashSet<string> PercentagePropertyNames = new(StringComparer.Ordinal)
    {
        "ChallengeChance",
        "ChildrenFemaleChance",
        "ControversyChance",
        "FemaleChance",
        "GenerationChance",
        "InheritChance",
        "InjuryChance",
        "ObtainProb",
        "SuccessRate",
        "TitleChance",
        "Chance",
    };

    private static readonly IReadOnlyDictionary<(Type ItemType, string PropertyName), int>
        FixedCollectionLengths = new Dictionary<(Type, string), int>
        {
            [(typeof(CharacterKindConfig), nameof(CharacterKindConfig.GenerationChance))] = 10,
            [(typeof(SkillConfig), nameof(SkillConfig.GenerationChance))] = 10,
            [(typeof(StuntConfig), nameof(StuntConfig.GenerationChance))] = 10,
            [(typeof(TitleConfig), nameof(TitleConfig.GenerationChance))] = 10,
        };

    public static ConfigPropertyValidation Get(PropertyInfo property)
    {
        var isRequired = property.PropertyType == typeof(string)
            && property.Name == "Name";
        var hasPercentageRange = IsNumeric(property.PropertyType)
            && PercentagePropertyNames.Contains(property.Name);
        var expectedCollectionLength = FixedCollectionLengths.GetValueOrDefault(
            (property.DeclaringType!, property.Name));
        var hasFixedCollectionLength = FixedCollectionLengths.ContainsKey(
            (property.DeclaringType!, property.Name));

        return isRequired || hasPercentageRange || hasFixedCollectionLength
            ? new ConfigPropertyValidation(
                isRequired,
                hasPercentageRange ? 0 : null,
                hasPercentageRange ? 100 : null,
                hasFixedCollectionLength ? expectedCollectionLength : null)
            : ConfigPropertyValidation.None;
    }

    private static bool IsNumeric(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return Type.GetTypeCode(type) is
            TypeCode.Byte or
            TypeCode.SByte or
            TypeCode.Int16 or
            TypeCode.UInt16 or
            TypeCode.Int32 or
            TypeCode.UInt32 or
            TypeCode.Int64 or
            TypeCode.UInt64 or
            TypeCode.Single or
            TypeCode.Double or
            TypeCode.Decimal;
    }
}
