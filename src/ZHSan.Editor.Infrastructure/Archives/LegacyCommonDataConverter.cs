using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameDatas;
using GameDatas.Converts;
using Microsoft.Xna.Framework;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Infrastructure.Archives;

public sealed class LegacyCommonDataConverter(IConfigRegistry registry) : ILegacyCommonDataConverter
{
    private const int ExtraEntryCount = 1;

    private static readonly MethodInfo SaveMethod = typeof(LegacyCommonDataConverter)
        .GetMethod(nameof(Save), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo LoadMethod = typeof(LegacyCommonDataConverter)
        .GetMethod(nameof(Load), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly JsonSerializerOptions ProjectionOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new PointJsonConverter() }
    };

    private static readonly IReadOnlyDictionary<(Type Type, string Property), string> PropertyAliases =
        new Dictionary<(Type, string), string>
        {
            [(typeof(ConditionConfig), nameof(ConditionConfig.KindId))] = "Kind",
            [(typeof(ArchitectureEventEffectConfig), nameof(ArchitectureEventEffectConfig.KindId))] = "Kind",
            [(typeof(TroopEventEffectConfig), nameof(TroopEventEffectConfig.KindId))] = "Kind",
            [(typeof(InfluenceConfig), nameof(InfluenceConfig.KindId))] = "Kind",
            [(typeof(TitleConfig), nameof(TitleConfig.KindId))] = "Kind",
            [(typeof(FacilityKindConfig), nameof(FacilityKindConfig.IsDemolishable))] = "bukechaichu",
            [(typeof(FacilityKindLevelConfig), nameof(FacilityKindLevelConfig.ConcubineCapacity))] = "rongna",
            [(typeof(OfficialTitleKindConfig), nameof(OfficialTitleKindConfig.ReputationCap))] = "shengwangshangxian",
            [(typeof(OfficialTitleKindConfig), nameof(OfficialTitleKindConfig.RequiredContribution))] = "xuyaogongxiandu",
            [(typeof(OfficialTitleKindConfig), nameof(OfficialTitleKindConfig.RequiredArchitecture))] = "xuyaochengchi",
            [(typeof(DisasterKindConfig), nameof(DisasterKindConfig.MinDuration))] = "shijianxiaxian",
            [(typeof(DisasterKindConfig), nameof(DisasterKindConfig.MaxDuration))] = "shijianshangxian",
            [(typeof(DisasterKindConfig), nameof(DisasterKindConfig.PopulationDamage))] = "renkoushanghai",
            [(typeof(DisasterKindConfig), nameof(DisasterKindConfig.DominationDamage))] = "tongzhishanghai",
            [(typeof(DisasterKindConfig), nameof(DisasterKindConfig.EnduranceDamage))] = "naijiushanghai",
            [(typeof(DisasterKindConfig), nameof(DisasterKindConfig.AgricultureDamage))] = "nongyeshanghai",
            [(typeof(DisasterKindConfig), nameof(DisasterKindConfig.CommerceDamage))] = "shangyeshanghai",
            [(typeof(DisasterKindConfig), nameof(DisasterKindConfig.TechnologyDamage))] = "jishushanghai",
            [(typeof(DisasterKindConfig), nameof(DisasterKindConfig.MoraleDamage))] = "minxinshanghai"
        };

    private static readonly IReadOnlyDictionary<string, LegacySource> Sources =
        new Dictionary<string, LegacySource>(StringComparer.OrdinalIgnoreCase)
        {
            ["TerrainDetails.json"] = new("AllTerrainDetails", "TerrainDetails", true),
            ["CombatMethods.json"] = new("AllCombatMethods", "CombatMethods", true),
            ["Stunts.json"] = new("AllStunts", "Stunts", true),
            ["Techniques.json"] = new("AllTechniques", "Techniques", true),
            ["Skills.json"] = new("AllSkills", "Skills", true),
            ["Stratagems.json"] = new("AllStratagems", "Stratagems", true),
            ["TitleKinds.json"] = new("AllTitleKinds", "TitleKinds", true),
            ["Titles.json"] = new("AllTitles", "Titles", true),
            ["InfluenceKinds.json"] = new("AllInfluenceKinds", "InfluenceKinds", true),
            ["Influences.json"] = new("AllInfluences", "Influences", true),
            ["ConditionKinds.json"] = new("AllConditionKinds", "ConditionKinds", true),
            ["Conditions.json"] = new("AllConditions", "Conditions", true),
            ["ArchitectureEventEffectKinds.json"] = new("AllEventEffectKinds", "EventEffectKinds", true),
            ["ArchitectureEventEffects.json"] = new("AllEventEffects", "EventEffects", true),
            ["TroopEventEffectKinds.json"] = new("AllTroopEventEffectKinds", "EventEffectKinds", true),
            ["TroopEventEffects.json"] = new("AllTroopEventEffects", "EventEffects", true),
            ["InformationKinds.json"] = new("AllInformationKinds", "GameObjects"),
            ["CharacterKinds.json"] = new("AllCharacterKinds"),
            ["FacilityKinds.json"] = new("AllFacilityKinds", "FacilityKinds", true),
            ["FacilityKindLevels.json"] = new("AllFacilityKinds", "FacilityKinds", true),
            ["DisasterKinds.json"] = new("suoyouzainanzhonglei", "zainanzhongleizidian", true),
            ["OfficialTitleKinds.json"] = new("suoyouguanjuezhonglei", "guanjuedezhongleizidian", true),
            ["SectionAIDetails.json"] = new("AllSectionAIDetails", "SectionAIDetails", true),
            ["IdealTendencyKinds.json"] = new("AllIdealTendencyKinds", "GameObjects", Optional: true),
            ["MilitaryKinds.json"] = new("AllMilitaryKinds", "MilitaryKinds", true),
            ["ArchitectureKinds.json"] = new("AllArchitectureKinds", "ArchitectureKinds", true),
            ["TileAnimations.json"] = new("AllTileAnimations", "Animations", true),
            ["TroopAnimations.json"] = new("AllTroopAnimations", "Animations", true),
            ["BiographyAdjectives.json"] = new("AllBiographyAdjectives"),
            ["PersonGeneratorTypes.json"] = new("AllPersonGeneratorTypes", "GameObjects"),
            ["TrainPolicies.json"] = new("AllTrainPolicies", "GameObjects", Optional: true),
            ["TreasureCreationSettings.json"] = new("AllTreasureCreationSettings", "GameObjects"),
            ["AttackDefaultKinds.json"] = new("AllAttackDefaultKinds", "GameObjects"),
            ["AttackTargetKinds.json"] = new("AllAttackTargetKinds", "GameObjects"),
            ["CastDefaultKinds.json"] = new("AllCastDefaultKinds", "GameObjects"),
            ["CastTargetKinds.json"] = new("AllCastTargetKinds", "GameObjects"),
            ["StatusEffects.json"] = new("allStatusEffects", SingleDictionaryValue: true)
        };

    public Task<LegacyCommonDataConversionResult> ConvertAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Convert(sourcePath, destinationPath, cancellationToken), cancellationToken);

    private LegacyCommonDataConversionResult Convert(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        if (PathsEqual(fullSourcePath, fullDestinationPath))
        {
            throw new ArgumentException("新格式档案不能覆盖旧版 CommonData JSON 文件。", nameof(destinationPath));
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(SanitizeLegacyJson(File.ReadAllText(fullSourcePath))) as JsonObject
                ?? throw new InvalidDataException("旧版 CommonData JSON 根节点必须是对象。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"旧版 CommonData JSON 无法解析：{exception.Message}", exception);
        }

        var entries = new List<ArchiveEntry>(registry.Definitions.Count + ExtraEntryCount);
        foreach (var definition in registry.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(ProjectEntry(root, definition));
        }

        entries.Add(ProjectColors(root));
        WriteAndVerify(entries, fullDestinationPath, cancellationToken);

        return new LegacyCommonDataConversionResult(
            fullDestinationPath,
            registry.Definitions.Count,
            entries.Count,
            entries.Sum(entry => entry.Count));
    }

    private static ArchiveEntry ProjectEntry(JsonObject root, ConfigDefinition definition)
    {
        if (definition.EntryName == "PersonMessages.json")
        {
            return ProjectPersonMessages(root, definition);
        }

        if (definition.EntryName == "PersonGeneratorSettings.json")
        {
            var value = RequiredObject(root, "PersonGeneratorSetting");
            return CreateEntry(definition.EntryName, definition.ItemType, new JsonArray(ProjectObject(value, definition.ItemType)));
        }

        if (!Sources.TryGetValue(definition.EntryName, out var source))
        {
            throw new InvalidOperationException($"没有为 {definition.EntryName} 定义旧版 CommonData 映射。");
        }

        var items = ExtractItems(root, source);
        var projected = new JsonArray();
        foreach (var item in items)
        {
            var result = ProjectObject(RequiredItemObject(item, source.RootProperty), definition.ItemType);
            if (definition.EntryName == "FacilityKindLevels.json")
            {
                var oldId = RequiredInteger(result, nameof(FacilityKindLevelConfig.Id));
                result[nameof(FacilityKindLevelConfig.Id)] = checked(oldId * 10);
                result[nameof(FacilityKindLevelConfig.KindId)] = oldId;
                result[nameof(FacilityKindLevelConfig.Level)] = 1;
            }

            projected.Add(result);
        }

        return CreateEntry(definition.EntryName, definition.ItemType, projected);
    }

    private static ArchiveEntry ProjectPersonMessages(JsonObject root, ConfigDefinition definition)
    {
        var entries = RequiredArray(RequiredObject(root, "AllTextMessages"), "textMessages");
        var projected = new JsonArray();
        foreach (var node in entries)
        {
            var entry = RequiredItemObject(node, "AllTextMessages.textMessages");
            var key = RequiredObject(entry, "Key");
            projected.Add(new JsonObject
            {
                [nameof(PersonMessageConfig.PersonId)] = RequiredNode(key, "key").DeepClone(),
                [nameof(PersonMessageConfig.Kind)] = RequiredNode(key, "value").DeepClone(),
                [nameof(PersonMessageConfig.Messages)] = RequiredNode(entry, "Value").DeepClone()
            });
        }

        return CreateEntry(definition.EntryName, definition.ItemType, projected);
    }

    private static ArchiveEntry ProjectColors(JsonObject root)
    {
        var source = RequiredArray(root, "AllColors");
        var projected = new JsonArray(source.Select(node => node?.DeepClone()).ToArray());
        return CreateEntry("Colors.json", typeof(Color), projected);
    }

    private static JsonArray ExtractItems(JsonObject root, LegacySource source)
    {
        if (!TryGetProperty(root, source.RootProperty, out var rootNode) || rootNode is null)
        {
            if (source.Optional)
            {
                return [];
            }

            throw new InvalidDataException($"旧版 CommonData 缺少 {source.RootProperty}。");
        }

        JsonNode container = rootNode;
        if (source.CollectionProperty is not null)
        {
            container = RequiredNode(RequiredItemObject(rootNode, source.RootProperty), source.CollectionProperty);
        }

        if (source.SingleDictionaryValue)
        {
            if (container is JsonArray dictionaryEntries)
            {
                var dictionaryValues = new JsonArray();
                foreach (var node in dictionaryEntries)
                {
                    dictionaryValues.Add(RequiredNode(
                        RequiredItemObject(node, source.RootProperty),
                        "Value").DeepClone());
                }

                return dictionaryValues;
            }

            var dictionary = RequiredItemObject(container, source.RootProperty);
            return new JsonArray(RequiredNode(dictionary, "Value").DeepClone());
        }

        var array = container as JsonArray
            ?? throw new InvalidDataException($"旧版 CommonData 的 {source.RootProperty} 不是有效数组。");
        if (!source.DictionaryValues)
        {
            return array;
        }

        var values = new JsonArray();
        foreach (var node in array)
        {
            values.Add(RequiredNode(RequiredItemObject(node, source.RootProperty), "Value").DeepClone());
        }

        return values;
    }

    private static JsonObject ProjectObject(JsonObject source, Type targetType)
    {
        var result = new JsonObject();
        foreach (var property in targetType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            var sourceName = PropertyAliases.GetValueOrDefault((targetType, property.Name), property.Name);
            if (TryGetProperty(source, sourceName, out var value) && value is not null)
            {
                result[property.Name] = property.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
                    value is JsonObject referencedObject
                        ? RequiredNode(referencedObject, "ID").DeepClone()
                        : value.DeepClone();
            }
        }

        return result;
    }

    private static ArchiveEntry CreateEntry(string entryName, Type itemType, JsonArray data)
    {
        var listType = typeof(List<>).MakeGenericType(itemType);
        object items;
        try
        {
            items = data.Deserialize(listType, ProjectionOptions)
                ?? throw new InvalidDataException($"{entryName} 转换结果为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"旧版 CommonData 无法转换 {entryName}：{exception.Message}", exception);
        }

        return new ArchiveEntry(entryName, itemType, items, ((ICollection)items).Count);
    }

    private static void WriteAndVerify(
        IReadOnlyList<ArchiveEntry> entries,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("CommonData 档案输出路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = destinationPath + ".tmp";
        var backupPath = destinationPath + ".bak";
        DeleteIfExists(temporaryPath);

        try
        {
            using (var archive = GameDataArchive.Open(temporaryPath))
            {
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SaveMethod.MakeGenericMethod(entry.Items.GetType())
                        .Invoke(null, [archive, entry.Name, entry.Items]);
                }
            }

            using (var archive = GameDataArchive.Open(temporaryPath))
            {
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var loaded = LoadMethod.MakeGenericMethod(entry.Items.GetType())
                        .Invoke(null, [archive, entry.Name]) as ICollection;
                    if (loaded?.Count != entry.Count)
                    {
                        throw new InvalidDataException(
                            $"转换档案验证失败：{entry.Name} 预期 {entry.Count} 条，实际 {loaded?.Count ?? -1} 条。");
                    }
                }
            }

            if (File.Exists(destinationPath))
            {
                File.Replace(temporaryPath, destinationPath, backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    private static void Save<T>(GameDataArchive archive, string entryName, T items) => archive.Save(entryName, items);

    private static T? Load<T>(GameDataArchive archive, string entryName) => archive.Load<T>(entryName);

    private static int RequiredInteger(JsonObject source, string propertyName)
    {
        var node = RequiredNode(source, propertyName);
        if (node is JsonValue value && value.TryGetValue<int>(out var result))
        {
            return result;
        }

        throw new InvalidDataException($"旧版 CommonData 的 {propertyName} 不是有效整数。");
    }

    private static JsonObject RequiredObject(JsonObject parent, string propertyName) =>
        RequiredNode(parent, propertyName) as JsonObject
        ?? throw new InvalidDataException($"旧版 CommonData 的 {propertyName} 不是有效对象。");

    private static JsonArray RequiredArray(JsonObject parent, string propertyName) =>
        RequiredNode(parent, propertyName) as JsonArray
        ?? throw new InvalidDataException($"旧版 CommonData 的 {propertyName} 不是有效数组。");

    private static JsonNode RequiredNode(JsonObject parent, string propertyName)
    {
        if (TryGetProperty(parent, propertyName, out var node) && node is not null)
        {
            return node;
        }

        throw new InvalidDataException($"旧版 CommonData 缺少 {propertyName}。");
    }

    private static JsonObject RequiredItemObject(JsonNode? node, string context) =>
        node as JsonObject
        ?? throw new InvalidDataException($"旧版 CommonData 的 {context} 中包含非对象记录。");

    private static bool TryGetProperty(JsonObject source, string name, out JsonNode? value)
    {
        foreach (var property in source)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string SanitizeLegacyJson(string json)
    {
        var result = new StringBuilder(json.Length);
        var insideString = false;
        var escaped = false;
        foreach (var character in json)
        {
            if (!insideString)
            {
                result.Append(character);
                insideString = character == '"';
                continue;
            }

            if (escaped)
            {
                result.Append(character);
                escaped = false;
            }
            else if (character == '\\')
            {
                result.Append(character);
                escaped = true;
            }
            else if (character == '"')
            {
                result.Append(character);
                insideString = false;
            }
            else if (character == '\r') result.Append("\\r");
            else if (character == '\n') result.Append("\\n");
            else if (character == '\t') result.Append("\\t");
            else if (character < ' ')
            {
                result.Append("\\u");
                result.Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            }
            else result.Append(character);
        }

        return result.ToString();
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record LegacySource(
        string RootProperty,
        string? CollectionProperty = null,
        bool DictionaryValues = false,
        bool Optional = false,
        bool SingleDictionaryValue = false);

    private sealed record ArchiveEntry(string Name, Type ItemType, object Items, int Count);
}
