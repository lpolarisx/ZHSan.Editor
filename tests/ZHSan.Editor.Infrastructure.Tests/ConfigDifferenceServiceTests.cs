using GameDatas;
using ZHSan.Editor.Application.Differences;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Differences;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ConfigDifferenceServiceTests
{
    private readonly ConfigDifferenceService _service = new(new ReflectionConfigMetadataProvider());

    [Fact]
    public void Compare_ById_ReportsAddedModifiedDeletedAndChangedProperties()
    {
        var unchanged = new TechniqueConfig { Id = 1, Name = "不变" };
        var changed = new TechniqueConfig { Id = 2, Name = "旧名称", PreID = 1 };
        var deleted = new TechniqueConfig { Id = 3, Name = "删除" };
        var document = CreateDocument<TechniqueConfig>(unchanged, changed, deleted);
        object[] incoming =
        [
            new TechniqueConfig { Id = 1, Name = "不变" },
            new TechniqueConfig { Id = 2, Name = "新名称", PreID = 1 },
            new TechniqueConfig { Id = 4, Name = "新增" },
        ];

        var difference = _service.Compare(document, incoming);

        Assert.Equal("test", difference.ConfigKey);
        Assert.Equal(1, difference.AddedCount);
        Assert.Equal(1, difference.ModifiedCount);
        Assert.Equal(1, difference.DeletedCount);
        Assert.Equal(0, difference.ConflictCount);
        Assert.True(difference.HasChanges);
        Assert.False(difference.HasConflicts);

        var modified = Assert.Single(
            difference.Records,
            record => record.Kind == ConfigDifferenceKind.Modified);
        Assert.Equal(2, modified.ItemId);
        Assert.Same(changed, modified.CurrentItem);
        Assert.Same(incoming[1], modified.IncomingItem);
        var property = Assert.Single(modified.PropertyDifferences);
        Assert.Equal("Name", property.PropertyName);
        Assert.Equal("旧名称", property.CurrentValue);
        Assert.Equal("新名称", property.IncomingValue);
    }

    [Fact]
    public void Compare_DuplicateIds_ReportsOneExplicitConflict()
    {
        var document = CreateDocument<TechniqueConfig>(
            new() { Id = 7, Name = "当前一" },
            new() { Id = 7, Name = "当前二" });
        object[] incoming = [new TechniqueConfig { Id = 7, Name = "导入" }];

        var difference = _service.Compare(document, incoming);

        var conflict = Assert.Single(difference.Records);
        Assert.Equal(ConfigDifferenceKind.Conflict, conflict.Kind);
        Assert.Equal(7, conflict.ItemId);
        Assert.Equal(2, conflict.CurrentItems.Count);
        Assert.Single(conflict.IncomingItems);
        Assert.Contains("无法唯一匹配", conflict.ConflictReason);
        Assert.True(difference.HasConflicts);
    }

    [Fact]
    public void Compare_CollectionsByContents_DoesNotReportEquivalentInstances()
    {
        var document = CreateDocument<TreasureCreationSettingConfig>(
            new TreasureCreationSettingConfig { Id = 1, EligibleInfluenceIDs = [3, 5, 8] });
        object[] incoming =
        [
            new TreasureCreationSettingConfig { Id = 1, EligibleInfluenceIDs = [3, 5, 8] },
        ];

        var difference = _service.Compare(document, incoming);

        Assert.Empty(difference.Records);
        Assert.False(difference.HasChanges);
    }

    [Fact]
    public void Compare_WithoutId_MatchesRecordsByIndex()
    {
        var document = CreateDocument<PersonMessageConfig>(
            new PersonMessageConfig { PersonId = 1, Messages = ["旧消息"] });
        object[] incoming =
        [
            new PersonMessageConfig { PersonId = 1, Messages = ["新消息"] },
            new PersonMessageConfig { PersonId = 2, Messages = ["新增消息"] },
        ];

        var difference = _service.Compare(document, incoming);

        Assert.Equal(1, difference.ModifiedCount);
        Assert.Equal(1, difference.AddedCount);
        var modified = Assert.Single(
            difference.Records,
            record => record.Kind == ConfigDifferenceKind.Modified);
        Assert.Null(modified.ItemId);
        Assert.Equal(0, Assert.Single(modified.CurrentItems).Index);
        Assert.Equal("Messages", Assert.Single(modified.PropertyDifferences).PropertyName);
    }

    [Fact]
    public void Compare_RejectsItemsOfAnotherConfigurationType()
    {
        var document = CreateDocument<TechniqueConfig>();
        object[] incoming = [new SkillConfig { Id = 1, Name = "错误类型" }];

        var exception = Assert.Throws<ArgumentException>(() => _service.Compare(document, incoming));

        Assert.Equal("incomingItems", exception.ParamName);
    }

    [Fact]
    public void Compare_WhenCancelled_StopsBeforeComparing()
    {
        var document = CreateDocument<TechniqueConfig>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => _service.Compare(document, [], cancellation.Token));
    }

    private static ConfigDocument CreateDocument<T>(params T[] items)
        where T : class =>
        new()
        {
            Definition = new ConfigDefinition("test", "测试", "测试", "Test.json", typeof(T)),
            Items = items.Cast<object>().ToList(),
        };
}
