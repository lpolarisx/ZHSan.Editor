using System.Reflection;
using GameDatas;

namespace ZHSan.Editor.Infrastructure.Configuration;

/// <summary>
/// The game splits an architecture's list strings (persons, links, resources, packs...) without a null check,
/// so those fields are kept as "" instead of null.
/// </summary>
public static class GameDataStringDefaults
{
    private static readonly PropertyInfo[] ArchitectureListStrings = typeof(ArchitectureConfig)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.PropertyType == typeof(string) && property.Name.EndsWith("String", StringComparison.Ordinal) &&
            property.CanRead && property.CanWrite)
        .ToArray();

    public static void Apply(object? item)
    {
        if (item is not ArchitectureConfig architecture) return;
        foreach (var property in ArchitectureListStrings)
        {
            if (property.GetValue(architecture) is null) property.SetValue(architecture, "");
        }
    }
}
