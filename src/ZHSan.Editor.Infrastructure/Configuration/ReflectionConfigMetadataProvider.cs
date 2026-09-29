using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using GameDatas;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Infrastructure.Configuration;

public sealed class ReflectionConfigMetadataProvider : IConfigMetadataProvider
{
    private static readonly IReadOnlySet<Type> ScenarioTypes =
        new GameDataConfigRegistry().GetDefinitions(ConfigScope.Scenario)
            .Select(definition => definition.ItemType)
            .ToHashSet();

    private readonly ConcurrentDictionary<Type, IReadOnlyList<ConfigPropertyDefinition>> _cache = new();

    public IReadOnlyList<ConfigPropertyDefinition> GetProperties(Type itemType) =>
        _cache.GetOrAdd(itemType, CreateProperties);

    private static IReadOnlyList<ConfigPropertyDefinition> CreateProperties(Type itemType) =>
        itemType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Select((property, index) => new ConfigPropertyDefinition(
                property.Name,
                property.Name,
                property.PropertyType,
                property.CanWrite && !(itemType == typeof(PersonConfig) && property.Name == nameof(PersonConfig.Name)),
                property.GetCustomAttribute<JsonPropertyOrderAttribute>()?.Order ?? index)
            {
                ValueGetter = itemType == typeof(PersonConfig) && property.Name == nameof(PersonConfig.Name)
                    ? item => GetPersonName((PersonConfig)item)
                    : null,
                IsMultilineText = IsBiographyText(itemType, property.Name),
                AllowNullEditing = property.PropertyType != typeof(string) ||
                    !ScenarioTypes.Contains(itemType),
                Validation = GameDataPropertyValidationMetadata.Get(property),
                Reference = GameDataPropertyReferenceMetadata.Get(itemType, property),
                StructuredString = GameDataStructuredStringMetadata.Get(itemType, property),
            })
            .OrderBy(property => property.Order)
            .ThenBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    private static string? GetPersonName(PersonConfig person)
    {
        var fullName = person.SurName + person.GivenName;
        return fullName.Length > 0 ? fullName : person.Name;
    }

    private static bool IsBiographyText(Type itemType, string propertyName) =>
        itemType == typeof(BiographyConfig) &&
        propertyName is nameof(BiographyConfig.Brief) or nameof(BiographyConfig.History) or
            nameof(BiographyConfig.Romance) or nameof(BiographyConfig.InGame);
}
