using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameDatas;
using GameDatas.Converts;
using Microsoft.Xna.Framework;
using ZHSan.Editor.Application.Abstractions;

namespace ZHSan.Editor.Infrastructure.Archives;

public sealed class LegacyScenarioConverter : ILegacyScenarioConverter
{
    private const int ExpectedEntryCount = 23;

    private static readonly JsonSerializerOptions ProjectionOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new PointJsonConverter() }
    };

    private static readonly IReadOnlyDictionary<(Type Type, string Property), string> PropertyAliases =
        new Dictionary<(Type, string), string>
        {
            [(typeof(ArchitectureConfig), nameof(ArchitectureConfig.ConcubineString))] = "feiziliebiaoString",
            [(typeof(ArchitectureConfig), nameof(ArchitectureConfig.HasDisaster))] = "youzainan",
            [(typeof(ArchitectureConfig), nameof(ArchitectureConfig.HasEmperor))] = "huangdisuozai",
            [(typeof(FactionConfig), nameof(FactionConfig.CourtContribution))] = "chaotinggongxiandu",
            [(typeof(FactionConfig), nameof(FactionConfig.OfficialRank))] = "guanjue",
            [(typeof(FactionConfig), nameof(FactionConfig.RecruitmentFailureCount))] = "ZhaoxianFailureCount",
            [(typeof(TroopConfig), nameof(TroopConfig.LeaderId))] = "LeaderIDString",
            [(typeof(TroopConfig), nameof(TroopConfig.Fund))] = "zijin",
            [(typeof(TroopConfig), nameof(TroopConfig.Order))] = "mingling",
            [(typeof(TroopConfig), nameof(TroopConfig.OrderPosition))] = "minglingweizhi",
            [(typeof(TroopConfig), nameof(TroopConfig.CaptureChance))] = "captureChance",
            [(typeof(TroopConfig), nameof(TroopConfig.IsTargetPositionReset))] = "chongshemubiaoweizhibiaoji"
        };

    public Task<LegacyScenarioConversionResult> ConvertAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Convert(sourcePath, destinationPath, cancellationToken),
            cancellationToken);

    private static LegacyScenarioConversionResult Convert(
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
            throw new ArgumentException("新格式档案不能覆盖旧版 JSON 文件。", nameof(destinationPath));
        }

        JsonObject root;
        try
        {
            var legacyJson = File.ReadAllText(fullSourcePath);
            root = JsonNode.Parse(SanitizeLegacyJson(legacyJson)) as JsonObject
                ?? throw new InvalidDataException("旧版剧本的 JSON 根节点必须是对象。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"旧版剧本 JSON 无法解析：{exception.Message}", exception);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var data = new ScenarioArchiveData(
            ProjectList<ArchitectureConfig>(GameObjects(root, "Architectures")),
            ProjectValueList<BiographyConfig>(RequiredArray(RequiredObject(root, "AllBiographies"), "Biographys")),
            ProjectList<CaptiveConfig>(GameObjects(root, "captiveData")),
            ProjectValueList<DiplomaticRelationConfig>(RequiredArray(RequiredObject(root, "DiplomaticRelations"), "DiplomaticRelations")),
            ProjectList<EventConfig>(GameObjects(root, "AllEvents")),
            ProjectFacilities(GameObjects(root, "Facilities")),
            ProjectList<FactionConfig>(GameObjects(root, "Factions")),
            ProjectList<Point>(RequiredArray(RequiredObject(root, "FireTable"), "Positions")),
            Project<GameScenarioConfig>(root),
            ProjectList<InformationConfig>(GameObjects(root, "Informations")),
            ProjectList<LegionConfig>(GameObjects(root, "Legions")),
            ProjectList<MilitaryConfig>(GameObjects(root, "Militaries")),
            ProjectNoFoodPositions(RequiredArray(RequiredObject(root, "NoFoodDictionary"), "Positions")),
            ProjectList<PersonRelationConfig>(RequiredArray(root, "PersonRelationIds")),
            ProjectPeople(GameObjects(root, "Persons")),
            ProjectList<RegionConfig>(GameObjects(root, "Regions")),
            ProjectList<RoutewayConfig>(GameObjects(root, "Routeways")),
            ProjectList<SectionConfig>(GameObjects(root, "Sections")),
            ProjectList<StateConfig>(GameObjects(root, "States")),
            ProjectList<TreasureConfig>(GameObjects(root, "Treasures")),
            ProjectList<TroopEventConfig>(GameObjects(root, "TroopEvents")),
            ProjectList<TroopConfig>(GameObjects(root, "Troops")),
            ProjectList<YearTableConfig>(GameObjects(root, "YearTable")));

        cancellationToken.ThrowIfCancellationRequested();
        WriteAndVerify(data, fullDestinationPath, cancellationToken);

        return new LegacyScenarioConversionResult(
            fullDestinationPath,
            ExpectedEntryCount,
            data.ItemCount);
    }

    private static void WriteAndVerify(
        ScenarioArchiveData data,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("新格式剧本的输出路径无效。");
        Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = destinationPath + ".tmp";
        var backupPath = destinationPath + ".bak";
        DeleteIfExists(temporaryPath);

        try
        {
            using (var archive = GameDataArchive.Open(temporaryPath))
            {
                SaveAll(archive, data, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            using (var archive = GameDataArchive.Open(temporaryPath))
            {
                VerifyAll(archive, data);
            }

            cancellationToken.ThrowIfCancellationRequested();
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

    private static void SaveAll(
        GameDataArchive archive,
        ScenarioArchiveData data,
        CancellationToken cancellationToken)
    {
        Save(archive, "Architectures.json", data.Architectures, cancellationToken);
        Save(archive, "Biographies.json", data.Biographies, cancellationToken);
        Save(archive, "Captives.json", data.Captives, cancellationToken);
        Save(archive, "DiplomaticRelations.json", data.DiplomaticRelations, cancellationToken);
        Save(archive, "Events.json", data.Events, cancellationToken);
        Save(archive, "Facilities.json", data.Facilities, cancellationToken);
        Save(archive, "Factions.json", data.Factions, cancellationToken);
        Save(archive, "FirePositions.json", data.FirePositions, cancellationToken);
        Save(archive, "GameScenarios.json", data.GameScenarios, cancellationToken);
        Save(archive, "Informations.json", data.Informations, cancellationToken);
        Save(archive, "Legions.json", data.Legions, cancellationToken);
        Save(archive, "Militaries.json", data.Militaries, cancellationToken);
        Save(archive, "NoFoodPositions.json", data.NoFoodPositions, cancellationToken);
        Save(archive, "PersonRelations.json", data.PersonRelations, cancellationToken);
        Save(archive, "Persons.json", data.Persons, cancellationToken);
        Save(archive, "Regions.json", data.Regions, cancellationToken);
        Save(archive, "Routeways.json", data.Routeways, cancellationToken);
        Save(archive, "Sections.json", data.Sections, cancellationToken);
        Save(archive, "States.json", data.States, cancellationToken);
        Save(archive, "Treasures.json", data.Treasures, cancellationToken);
        Save(archive, "TroopEvents.json", data.TroopEvents, cancellationToken);
        Save(archive, "Troops.json", data.Troops, cancellationToken);
        Save(archive, "YearTables.json", data.YearTables, cancellationToken);
    }

    private static void VerifyAll(GameDataArchive archive, ScenarioArchiveData data)
    {
        Verify<ArchitectureConfig>(archive, "Architectures.json", data.Architectures.Count);
        Verify<BiographyConfig>(archive, "Biographies.json", data.Biographies.Count);
        Verify<CaptiveConfig>(archive, "Captives.json", data.Captives.Count);
        Verify<DiplomaticRelationConfig>(archive, "DiplomaticRelations.json", data.DiplomaticRelations.Count);
        Verify<EventConfig>(archive, "Events.json", data.Events.Count);
        Verify<FacilityConfig>(archive, "Facilities.json", data.Facilities.Count);
        Verify<FactionConfig>(archive, "Factions.json", data.Factions.Count);
        Verify<Point>(archive, "FirePositions.json", data.FirePositions.Count);
        VerifyObject<GameScenarioConfig>(archive, "GameScenarios.json");
        Verify<InformationConfig>(archive, "Informations.json", data.Informations.Count);
        Verify<LegionConfig>(archive, "Legions.json", data.Legions.Count);
        Verify<MilitaryConfig>(archive, "Militaries.json", data.Militaries.Count);
        Verify<NoFoodConfig>(archive, "NoFoodPositions.json", data.NoFoodPositions.Count);
        Verify<PersonRelationConfig>(archive, "PersonRelations.json", data.PersonRelations.Count);
        Verify<PersonConfig>(archive, "Persons.json", data.Persons.Count);
        Verify<RegionConfig>(archive, "Regions.json", data.Regions.Count);
        Verify<RoutewayConfig>(archive, "Routeways.json", data.Routeways.Count);
        Verify<SectionConfig>(archive, "Sections.json", data.Sections.Count);
        Verify<StateConfig>(archive, "States.json", data.States.Count);
        Verify<TreasureConfig>(archive, "Treasures.json", data.Treasures.Count);
        Verify<TroopEventConfig>(archive, "TroopEvents.json", data.TroopEvents.Count);
        Verify<TroopConfig>(archive, "Troops.json", data.Troops.Count);
        Verify<YearTableConfig>(archive, "YearTables.json", data.YearTables.Count);
    }

    private static void Save<T>(
        GameDataArchive archive,
        string entryName,
        T data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        archive.Save(entryName, data);
    }

    private static void Verify<T>(GameDataArchive archive, string entryName, int expectedCount)
    {
        var items = archive.Load<List<T>>(entryName)
            ?? throw new InvalidDataException($"新格式剧本缺少条目 {entryName}。");
        if (items.Count != expectedCount)
        {
            throw new InvalidDataException(
                $"新格式剧本条目 {entryName} 验证失败：预期 {expectedCount} 条，实际 {items.Count} 条。");
        }
    }

    private static void VerifyObject<T>(GameDataArchive archive, string entryName)
    {
        if (archive.Load<T>(entryName) is null)
        {
            throw new InvalidDataException($"新格式剧本缺少条目 {entryName}。");
        }
    }

    private static List<FacilityConfig> ProjectFacilities(JsonArray source) =>
        source.Select(node =>
        {
            var item = RequiredItemObject(node, "Facilities");
            var projected = Project<FacilityConfig>(item);
            if (!TryGetProperty(item, "KindID", out var kindNode) || kindNode is null)
            {
                throw new InvalidDataException("旧版设施记录缺少 KindID，无法转换为新版 LevelId。");
            }

            projected.LevelId = kindNode.GetValue<int>() * 10;
            return projected;
        }).ToList();

    private static List<PersonConfig> ProjectPeople(JsonArray source) =>
        source.Select(node =>
        {
            var projected = Project<PersonConfig>(RequiredItemObject(node, "Persons"));
            projected.StatusEffects ??= [];
            return projected;
        }).ToList();

    private static List<NoFoodConfig> ProjectNoFoodPositions(JsonArray source) =>
        source.Select(node =>
        {
            var item = RequiredItemObject(node, "NoFoodDictionary.Positions");
            var value = TryGetProperty(item, "Value", out var valueNode) && valueNode is not null
                ? valueNode
                : item;
            return Project<NoFoodConfig>(value);
        }).ToList();

    private static List<T> ProjectValueList<T>(JsonArray source) =>
        source.Select(node =>
        {
            var item = RequiredItemObject(node, typeof(T).Name);
            if (!TryGetProperty(item, "Value", out var valueNode) || valueNode is null)
            {
                throw new InvalidDataException($"旧版 {typeof(T).Name} 字典项缺少 Value。");
            }

            return Project<T>(valueNode);
        }).ToList();

    private static List<T> ProjectList<T>(JsonArray source) =>
        source.Select(node => Project<T>(RequiredItemNode(node, typeof(T).Name))).ToList();

    private static T Project<T>(JsonNode source)
    {
        var normalized = NormalizeNode(source, typeof(T), typeof(T).Name);
        try
        {
            return normalized.Deserialize<T>(ProjectionOptions)
                ?? throw new InvalidDataException($"旧版数据无法转换为 {typeof(T).Name}。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"旧版数据无法转换为 {typeof(T).Name}：{exception.Message}",
                exception);
        }
    }

    private static JsonNode? NormalizeNode(JsonNode? source, Type targetType, string dataPath)
    {
        if (source is null)
        {
            return null;
        }

        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType is not null)
        {
            return NormalizeNode(source, nullableType, dataPath);
        }

        if (targetType == typeof(string) || targetType.IsPrimitive || targetType.IsEnum ||
            targetType == typeof(decimal) || targetType == typeof(Point))
        {
            return source.DeepClone();
        }

        if (TryGetDictionaryTypes(targetType, out _, out var valueType))
        {
            var result = new JsonObject();
            if (source is JsonArray entries)
            {
                foreach (var entryNode in entries)
                {
                    var entry = RequiredItemObject(entryNode, targetType.Name);
                    if (!TryGetProperty(entry, "Key", out var keyNode) || keyNode is null ||
                        !TryGetProperty(entry, "Value", out var valueNode))
                    {
                        throw new InvalidDataException($"旧版数据 {dataPath} 的字典存在无效的 Key/Value 项。");
                    }

                    var key = GetDictionaryKeyText(keyNode, dataPath);
                    if (result.ContainsKey(key))
                    {
                        if (ShouldDeduplicateLegacyDictionary(dataPath))
                        {
                            continue;
                        }

                        throw new InvalidDataException($"旧版数据 {dataPath} 的字典包含重复键 {key}。");
                    }

                    result[key] = NormalizeNode(valueNode, valueType, $"{dataPath}[{key}]");
                }
            }
            else if (source is JsonObject sourceObject)
            {
                foreach (var property in sourceObject)
                {
                    result[property.Key] = NormalizeNode(
                        property.Value,
                        valueType,
                        $"{dataPath}[{property.Key}]");
                }
            }
            else
            {
                throw new InvalidDataException($"旧版数据 {dataPath} 的字典 JSON 结构无效。");
            }

            return result;
        }

        if (TryGetCollectionElementType(targetType, out var elementType))
        {
            if (source is not JsonArray sourceArray)
            {
                throw new InvalidDataException($"旧版数据 {dataPath} 的集合 JSON 结构无效。");
            }

            var result = new JsonArray();
            for (var index = 0; index < sourceArray.Count; index++)
            {
                result.Add(NormalizeNode(sourceArray[index], elementType, $"{dataPath}[{index}]"));
            }

            return result;
        }

        if (source is not JsonObject sourceObjectNode)
        {
            return source.DeepClone();
        }

        var targetObject = new JsonObject();
        foreach (var property in targetType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            var sourceName = PropertyAliases.GetValueOrDefault((targetType, property.Name), property.Name);
            if (!TryGetProperty(sourceObjectNode, sourceName, out var propertyNode) || propertyNode is null)
            {
                continue;
            }

            targetObject[property.Name] = NormalizeNode(
                propertyNode,
                property.PropertyType,
                $"{dataPath}.{property.Name}");
        }

        return targetObject;
    }

    private static bool ShouldDeduplicateLegacyDictionary(string dataPath) =>
        string.Equals(
            dataPath,
            $"{nameof(ArchitectureConfig)}.{nameof(ArchitectureConfig.CaptiveLoyaltyFall)}",
            StringComparison.Ordinal);

    private static string GetDictionaryKeyText(JsonNode keyNode, string dataPath)
    {
        if (keyNode is JsonValue value)
        {
            if (value.TryGetValue<int>(out var integer))
            {
                return integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        throw new InvalidDataException(
            $"旧版数据 {dataPath} 的字典包含无法转换的键。新格式场景只支持标量字典键。");
    }

    private static bool TryGetDictionaryTypes(Type type, out Type keyType, out Type valueType)
    {
        var dictionaryType = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            ? type
            : type.GetInterfaces().FirstOrDefault(candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictionaryType is null)
        {
            keyType = typeof(object);
            valueType = typeof(object);
            return false;
        }

        var arguments = dictionaryType.GetGenericArguments();
        keyType = arguments[0];
        valueType = arguments[1];
        return true;
    }

    private static bool TryGetCollectionElementType(Type type, out Type elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        var enumerableType = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerableType is null || type == typeof(string))
        {
            elementType = typeof(object);
            return false;
        }

        elementType = enumerableType.GetGenericArguments()[0];
        return true;
    }

    private static JsonArray GameObjects(JsonObject root, string propertyName)
    {
        var container = RequiredObject(root, propertyName);
        return RequiredArray(container, "GameObjects");
    }

    private static JsonObject RequiredObject(JsonObject parent, string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var node) || node is not JsonObject result)
        {
            throw new InvalidDataException($"旧版剧本缺少对象 {propertyName}。");
        }

        return result;
    }

    private static JsonArray RequiredArray(JsonObject parent, string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var node) || node is not JsonArray result)
        {
            throw new InvalidDataException($"旧版剧本缺少数组 {propertyName}。");
        }

        return result;
    }

    private static JsonObject RequiredItemObject(JsonNode? node, string context) =>
        node as JsonObject
        ?? throw new InvalidDataException($"旧版剧本的 {context} 中包含非对象记录。");

    private static JsonNode RequiredItemNode(JsonNode? node, string context) =>
        node ?? throw new InvalidDataException($"旧版剧本的 {context} 中包含 null 记录。");

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

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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
                if (character == '"')
                {
                    insideString = true;
                }

                continue;
            }

            if (escaped)
            {
                result.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                result.Append(character);
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                result.Append(character);
                insideString = false;
                continue;
            }

            switch (character)
            {
                case '\r':
                    result.Append("\\r");
                    break;
                case '\n':
                    result.Append("\\n");
                    break;
                case '\t':
                    result.Append("\\t");
                    break;
                case '\b':
                    result.Append("\\b");
                    break;
                case '\f':
                    result.Append("\\f");
                    break;
                default:
                    if (character < ' ')
                    {
                        result.Append("\\u");
                        result.Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        result.Append(character);
                    }

                    break;
            }
        }

        return result.ToString();
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record ScenarioArchiveData(
        List<ArchitectureConfig> Architectures,
        List<BiographyConfig> Biographies,
        List<CaptiveConfig> Captives,
        List<DiplomaticRelationConfig> DiplomaticRelations,
        List<EventConfig> Events,
        List<FacilityConfig> Facilities,
        List<FactionConfig> Factions,
        List<Point> FirePositions,
        GameScenarioConfig GameScenarios,
        List<InformationConfig> Informations,
        List<LegionConfig> Legions,
        List<MilitaryConfig> Militaries,
        List<NoFoodConfig> NoFoodPositions,
        List<PersonRelationConfig> PersonRelations,
        List<PersonConfig> Persons,
        List<RegionConfig> Regions,
        List<RoutewayConfig> Routeways,
        List<SectionConfig> Sections,
        List<StateConfig> States,
        List<TreasureConfig> Treasures,
        List<TroopEventConfig> TroopEvents,
        List<TroopConfig> Troops,
        List<YearTableConfig> YearTables)
    {
        public int ItemCount =>
            Architectures.Count + Biographies.Count + Captives.Count + DiplomaticRelations.Count +
            Events.Count + Facilities.Count + Factions.Count + FirePositions.Count + 1 +
            Informations.Count + Legions.Count + Militaries.Count + NoFoodPositions.Count +
            PersonRelations.Count + Persons.Count + Regions.Count + Routeways.Count + Sections.Count +
            States.Count + Treasures.Count + TroopEvents.Count + Troops.Count + YearTables.Count;
    }
}
