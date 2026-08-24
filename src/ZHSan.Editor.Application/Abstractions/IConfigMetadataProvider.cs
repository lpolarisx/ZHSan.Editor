using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Application.Abstractions;

public interface IConfigMetadataProvider
{
    IReadOnlyList<ConfigPropertyDefinition> GetProperties(Type itemType);
}
