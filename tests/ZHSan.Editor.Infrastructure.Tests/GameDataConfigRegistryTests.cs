using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class GameDataConfigRegistryTests
{
    [Fact]
    public void Definitions_HaveUniqueKeysAndEntryNames()
    {
        var registry = new GameDataConfigRegistry();

        Assert.Equal(39, registry.Definitions.Count);
        Assert.Equal(39, registry.Definitions.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(39, registry.Definitions.Select(x => x.EntryName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
