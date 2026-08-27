using GameDatas;
using ZHSan.Editor.Application.Differences;
using ZHSan.Editor.Application.Importing;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Importing;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ConfigImportMergeServiceTests
{
    private readonly ConfigImportMergeService _service = new(
        new ConfigDifferenceService(new ReflectionConfigMetadataProvider()));

    [Fact]
    public void CreatePlan_ReplaceAll_UsesIncomingOrderAndSupportsRecordsWithoutId()
    {
        var current = new PersonMessageConfig { PersonId = 1, Messages = ["旧"] };
        var first = new PersonMessageConfig { PersonId = 3, Messages = ["三"] };
        var second = new PersonMessageConfig { PersonId = 2, Messages = ["二"] };
        var document = CreateDocument(current);

        var plan = _service.CreatePlan(
            document,
            [first, second],
            ConfigImportStrategy.ReplaceAll);

        Assert.Equal(ConfigImportStrategy.ReplaceAll, plan.Strategy);
        Assert.Equal([first, second], plan.MergedItems);
        Assert.True(plan.HasChanges);
        Assert.Same(current, document.Items[0]);
    }

    [Fact]
    public void CreatePlan_MergeById_ReplacesChangesPreservesExistingOrderAndAppendsNewItems()
    {
        var unchanged = new TechniqueConfig { Id = 1, Name = "不变" };
        var changed = new TechniqueConfig { Id = 2, Name = "旧" };
        var retained = new TechniqueConfig { Id = 3, Name = "保留" };
        var replacement = new TechniqueConfig { Id = 2, Name = "新" };
        var addedLast = new TechniqueConfig { Id = 5, Name = "新增五" };
        var addedFirst = new TechniqueConfig { Id = 4, Name = "新增四" };
        var document = CreateDocument(unchanged, changed, retained);

        var plan = _service.CreatePlan(
            document,
            [replacement, new TechniqueConfig { Id = 1, Name = "不变" }, addedLast, addedFirst],
            ConfigImportStrategy.MergeById);

        Assert.Equal([unchanged, replacement, retained, addedLast, addedFirst], plan.MergedItems);
        Assert.True(plan.HasChanges);
        Assert.Equal(2, plan.Difference.AddedCount);
        Assert.Equal(1, plan.Difference.ModifiedCount);
        Assert.Equal(1, plan.Difference.DeletedCount);
    }

    [Fact]
    public void CreatePlan_AddNewOnly_IgnoresChangesAndMissingIncomingItems()
    {
        var existing = new TechniqueConfig { Id = 1, Name = "当前" };
        var retained = new TechniqueConfig { Id = 2, Name = "仍保留" };
        var ignoredReplacement = new TechniqueConfig { Id = 1, Name = "导入修改" };
        var added = new TechniqueConfig { Id = 3, Name = "新增" };
        var document = CreateDocument(existing, retained);

        var plan = _service.CreatePlan(
            document,
            [ignoredReplacement, added],
            ConfigImportStrategy.AddNewOnly);

        Assert.Equal([existing, retained, added], plan.MergedItems);
        Assert.True(plan.HasChanges);
    }

    [Theory]
    [InlineData(ConfigImportStrategy.MergeById)]
    [InlineData(ConfigImportStrategy.AddNewOnly)]
    public void CreatePlan_IdStrategyWithoutId_ReportsUnsupported(ConfigImportStrategy strategy)
    {
        var document = CreateDocument(
            new PersonMessageConfig { PersonId = 1, Messages = ["消息"] });

        var exception = Assert.Throws<NotSupportedException>(
            () => _service.CreatePlan(document, [], strategy));

        Assert.Contains("没有 int Id", exception.Message);
    }

    [Theory]
    [InlineData(ConfigImportStrategy.ReplaceAll)]
    [InlineData(ConfigImportStrategy.MergeById)]
    [InlineData(ConfigImportStrategy.AddNewOnly)]
    public void CreatePlan_DuplicateIncomingIds_ReportsConflict(ConfigImportStrategy strategy)
    {
        var document = CreateDocument<TechniqueConfig>();
        object[] incoming =
        [
            new TechniqueConfig { Id = 7, Name = "一" },
            new TechniqueConfig { Id = 7, Name = "二" },
        ];

        var exception = Assert.Throws<ConfigMergeConflictException>(
            () => _service.CreatePlan(document, incoming, strategy));

        var conflict = Assert.Single(exception.Conflicts);
        Assert.Equal(7, conflict.ItemId);
        Assert.Equal(2, conflict.IncomingItems.Count);
    }

    [Fact]
    public void CreatePlan_ReplaceAll_AllowsIncomingDataToRepairCurrentDuplicateIds()
    {
        var first = new TechniqueConfig { Id = 7, Name = "当前一" };
        var second = new TechniqueConfig { Id = 7, Name = "当前二" };
        var replacement = new TechniqueConfig { Id = 7, Name = "修复" };
        var document = CreateDocument(first, second);

        var plan = _service.CreatePlan(
            document,
            [replacement],
            ConfigImportStrategy.ReplaceAll);

        Assert.Equal([replacement], plan.MergedItems);
        Assert.True(plan.HasChanges);
        Assert.True(plan.Difference.HasConflicts);
    }

    [Theory]
    [InlineData(ConfigImportStrategy.MergeById)]
    [InlineData(ConfigImportStrategy.AddNewOnly)]
    public void CreatePlan_IdStrategiesRejectCurrentDuplicateIds(ConfigImportStrategy strategy)
    {
        var document = CreateDocument(
            new TechniqueConfig { Id = 7, Name = "当前一" },
            new TechniqueConfig { Id = 7, Name = "当前二" });

        Assert.Throws<ConfigMergeConflictException>(
            () => _service.CreatePlan(
                document,
                [new TechniqueConfig { Id = 8, Name = "新增" }],
                strategy));
    }

    [Theory]
    [InlineData(ConfigImportStrategy.ReplaceAll)]
    [InlineData(ConfigImportStrategy.MergeById)]
    [InlineData(ConfigImportStrategy.AddNewOnly)]
    public void CreatePlan_EquivalentData_HasNoChanges(ConfigImportStrategy strategy)
    {
        var current = new TechniqueConfig { Id = 1, Name = "相同" };
        var document = CreateDocument(current);

        var plan = _service.CreatePlan(
            document,
            [new TechniqueConfig { Id = 1, Name = "相同" }],
            strategy);

        Assert.False(plan.HasChanges);
        Assert.Same(current, Assert.Single(plan.MergedItems));
    }

    [Fact]
    public void CreatePlan_UnknownStrategy_ReportsArgumentError()
    {
        var document = CreateDocument<TechniqueConfig>();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _service.CreatePlan(document, [], (ConfigImportStrategy)99));

        Assert.Equal("strategy", exception.ParamName);
    }

    private static ConfigDocument CreateDocument<T>(params T[] items)
        where T : class =>
        new()
        {
            Definition = new ConfigDefinition("test", "测试", "测试", "Test.json", typeof(T)),
            Items = items.Cast<object>().ToList(),
        };
}
