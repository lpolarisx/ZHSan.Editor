using GameDatas;
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
}
