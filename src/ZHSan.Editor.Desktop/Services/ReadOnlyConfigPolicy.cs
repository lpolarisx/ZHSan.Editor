using System.Text.Json;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Services;

/// <summary>
/// The configurations a build only lets people view. A build published with <c>-p:ReadOnlyConfigs=true</c>
/// reads the list from the embedded <c>ReadOnlyConfigs.json</c>; every other build edits everything.
/// </summary>
public sealed class ReadOnlyConfigPolicy
{
    private const string ResourceName = "ZHSan.Editor.Desktop.ReadOnlyConfigs.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyList<string> _entries;

    public ReadOnlyConfigPolicy(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.Where(entry => !string.IsNullOrWhiteSpace(entry)).Select(entry => entry.Trim()).ToArray();
    }

    public static ReadOnlyConfigPolicy None { get; } = new([]);

    public IReadOnlyList<string> Entries => _entries;

    // An entry names a configuration by key or display name ("influence-kinds", "影响类型"),
    // optionally limited to one scope ("Common:influence-kinds", "Scenario:人物").
    public bool IsReadOnly(ConfigDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return _entries.Any(entry =>
        {
            var separator = entry.IndexOf(':');
            var name = entry;
            if (separator > 0)
            {
                if (!Enum.TryParse<ConfigScope>(entry[..separator], ignoreCase: true, out var scope))
                {
                    return false;
                }

                if (scope != definition.Scope)
                {
                    return false;
                }

                name = entry[(separator + 1)..].Trim();
            }

            return string.Equals(name, definition.Key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, definition.DisplayName, StringComparison.Ordinal);
        });
    }

    public static ReadOnlyConfigPolicy Parse(string json)
    {
        var file = JsonSerializer.Deserialize<ReadOnlyConfigFile>(json, JsonOptions);
        return new ReadOnlyConfigPolicy(file?.ReadOnly ?? []);
    }

    public static ReadOnlyConfigPolicy LoadForBuild()
    {
#if READONLY_CONFIGS
        return LoadEmbedded();
#else
        return None;
#endif
    }

    // The list in ReadOnlyConfigs.json, which every build embeds whether or not it applies it.
    public static ReadOnlyConfigPolicy LoadEmbedded()
    {
        using var stream = typeof(ReadOnlyConfigPolicy).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"缺少嵌入的只读配置表 {ResourceName}。");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private sealed class ReadOnlyConfigFile
    {
        public List<string>? ReadOnly { get; set; }
    }
}
