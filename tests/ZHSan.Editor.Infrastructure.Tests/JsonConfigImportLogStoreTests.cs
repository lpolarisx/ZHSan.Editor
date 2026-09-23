using ZHSan.Editor.Application.Transfers;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Infrastructure.Settings;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class JsonConfigTransferLogStoreTests
{
    [Fact]
    public void Append_PersistsNewestFirstAcrossStoreInstances()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ZHSan.Editor.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "import-log.json");
        try
        {
            var store = new JsonConfigTransferLogStore(path);
            store.Append(new ConfigTransferLogEntry(
                DateTimeOffset.Parse("2026-08-27T09:00:00+08:00"),
                "First.dat",
                "技术",
                "成功",
                "第一条"));
            store.Append(new ConfigTransferLogEntry(
                DateTimeOffset.Parse("2026-08-27T10:00:00+08:00"),
                "Second.dat",
                "人物",
                "失败",
                "第二条",
                "导出",
                ConfigScope.Scenario));

            var entries = new JsonConfigTransferLogStore(path).Load();

            Assert.Equal(2, entries.Count);
            Assert.Equal("第二条", entries[0].Message);
            Assert.Equal("导出", entries[0].Operation);
            Assert.Equal(ConfigScope.Scenario, entries[0].Scope);
            Assert.Equal("第一条", entries[1].Message);
            Assert.Equal(ConfigScope.Common, entries[1].Scope);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ZHSan.Editor.ImportLog.{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "not json");

            var entries = new JsonConfigTransferLogStore(path).Load();

            Assert.Empty(entries);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
