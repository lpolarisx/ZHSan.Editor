using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Infrastructure.Configuration;

public sealed class ReflectionConfigMetadataProvider : IConfigMetadataProvider
{
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
                property.CanWrite,
                property.GetCustomAttribute<JsonPropertyOrderAttribute>()?.Order ?? index))
            .OrderBy(property => property.Order)
            .ThenBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
}
