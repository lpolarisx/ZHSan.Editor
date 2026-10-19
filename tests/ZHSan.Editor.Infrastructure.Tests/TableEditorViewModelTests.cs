using GameDatas;
using GameEnums;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class TableEditorViewModelTests
{
    private static readonly GameDataConfigRegistry Registry = new();

    public static TheoryData<string> SpecKeys() => [..TableEditorSpecs.All.Select(spec => spec.Key)];

    private static ConfigDefinition Definition(TableEditorSpec spec) =>
        Registry.GetDefinitions(ConfigScope.Common).Concat(Registry.GetDefinitions(ConfigScope.Scenario))
            .Single(definition => definition.Key == spec.Key && definition.ItemType == spec.ItemType);

    private static (ConfigDocumentViewModel Document, TableEditorViewModel Editor) Open(
        string key, IEnumerable<object> items, params ConfigDocument[] others)
    {
        var spec = TableEditorSpecs.All.Single(spec => spec.Key == key);
        var metadata = new ReflectionConfigMetadataProvider();
        var document = new ConfigDocument { Definition = Definition(spec), Items = [..items] };
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([new EditorProject
        {
            Scope = ConfigScope.Common, ArchivePath = "common.dat", Documents = [document, ..others]
        }]);
        var viewModel = new ConfigDocumentViewModel(document, metadata, _ => { }, referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new TableEditorProvider(TableEditorSpecs.All)]));
        return (viewModel, Assert.IsType<TableEditorViewModel>(viewModel.SpecializedEditor?.Content));
    }

    private static ConfigDocument Common<T>(string key, params T[] items) where T : notnull =>
        new() { Definition = Registry.Find(key)!, Items = [..items.Cast<object>()] };

    [Theory]
    [MemberData(nameof(SpecKeys))]
    public void Spec_OpensAndCoversEveryWritableField(string key)
    {
        var spec = TableEditorSpecs.All.Single(spec => spec.Key == key);
        var (document, editor) = Open(key, [Activator.CreateInstance(spec.ItemType)!]);
        using var _ = document;

        Assert.True(editor.HasSelection);
        var listed = spec.Fields.Select(field => field.Property).Concat(spec.Untouched).ToArray();
        Assert.Equal(listed.Length, listed.Distinct().Count());
        var writable = spec.ItemType.GetProperties().Where(property => property.CanWrite && property.Name != "Id")
            .Select(property => property.Name);
        Assert.Empty(writable.Except(listed));
        Assert.DoesNotContain(spec.Fields, field => field.Label.Any(char.IsAsciiLetterLower));
    }

    [Fact]
    public void Specs_AreUniqueAndLeaveDedicatedEditorsAlone()
    {
        Assert.Equal(TableEditorSpecs.All.Count, TableEditorSpecs.All.Select(spec => spec.Key).Distinct().Count());
        Assert.DoesNotContain(TableEditorSpecs.All, spec =>
            spec.Key is "techniques" or "facility-kind-levels" or "persons" or "factions" or "treasures");
    }

    [Fact]
    public void Condition_PicksItsKindByName()
    {
        var condition = new ConditionConfig { Id = 1, Name = "统率80以上", KindId = 7, Parameter = "80" };
        var (document, editor) = Open("conditions", [condition],
            Common("condition-kinds", new ConditionKindConfig { Id = 7, Name = "统率大于等于" },
                new ConditionKindConfig { Id = 8, Name = "武力大于等于" }));
        using var _ = document;
        var kind = editor.Sections[0].Group.Fields.Single(field => field.Label == "条件类型");

        Assert.Equal("统率80以上", editor.SelectedTitle);
        Assert.Equal("7 统率大于等于", kind.SelectedChoice?.Label);
        kind.SelectedChoice = kind.ChoiceOptions.Single(option => option.Id == 8);
        Assert.Equal(8, condition.KindId);
        document.UndoCommand.Execute(null);
        Assert.Equal(7, condition.KindId);
    }

    [Fact]
    public void InfluenceKind_ShowsEnumsInChineseAndEditsDecimals()
    {
        var kind = new InfluenceKindConfig { Id = 1, Name = "统率", Type = InfluenceType.Person, AIPersonValue = 1.5f };
        var (document, editor) = Open("influence-kinds", [kind]);
        using var _ = document;
        var type = editor.Sections[0].Group.Fields.Single(field => field.Label == "种类");
        var value = editor.Sections[1].Group.Fields.Single(field => field.Label == "武将AI值");

        Assert.Equal(["前提", "多选一", "个人", "战斗", "建筑战斗", "建筑", "势力"], type.Options);
        Assert.Equal("个人", type.SelectedOption);
        type.SelectedOption = "建筑";
        Assert.Equal(InfluenceType.Architecture, kind.Type);
        Assert.True(value.IsDecimal);
        Assert.Equal(1.5m, value.NumericValue);
        value.NumericValue = 0.25m;
        Assert.Equal(0.25f, kind.AIPersonValue);
    }

    [Fact]
    public void ListsAndRules_UseChipsAndTheRuleEditor()
    {
        var setting = new TreasureCreationSettingConfig { Id = 3, Name = "名剑", EligibleInfluenceIDs = [10], PicIDs = [1, 2] };
        var (document, editor) = Open("treasure-creation-settings", [setting],
            Common("influences", new InfluenceConfig { Id = 10, Name = "武力+5" }, new InfluenceConfig { Id = 11, Name = "统率+5" }));
        using var _ = document;
        var influences = Assert.IsType<PersonIdChipListViewModel>(editor.Sections[1].WideItems[0]);

        Assert.Equal(["10 武力+5"], influences.Items.Select(item => item.Label));
        influences.Candidate = influences.Candidates.Single(option => option.Id == 11);
        influences.AddCommand.Execute(null);
        Assert.Equal([10, 11], setting.EligibleInfluenceIDs);
        var pictures = editor.Sections[1].Group.Fields.Single(field => field.Label == "图片");
        pictures.IdListText = "1 2 5";
        Assert.Equal([1, 2, 5], setting.PicIDs);
        document.UndoCommand.Execute(null);
        document.UndoCommand.Execute(null);
        Assert.Equal([10], setting.EligibleInfluenceIDs);

        var status = new StatusEffectConfig { Id = 1, Name = "醉酒", StatusType = 1, Influences = "10 " };
        var (statusDocument, statusEditor) = Open("status-effects", [status],
            Common("influences", new InfluenceConfig { Id = 10, Name = "武力+5" }, new InfluenceConfig { Id = 11, Name = "统率+5" }),
            Common("conditions", new ConditionConfig { Id = 4, Name = "白天" }));
        using var __ = statusDocument;
        Assert.Equal("1 喝酒", statusEditor.Sections[0].Group.Fields.Single(field => field.Label == "状态类型")
            .SelectedChoice?.Label);
        var rule = Assert.IsType<TableRuleFieldViewModel>(statusEditor.Sections[1].WideItems[1]);
        Assert.Equal("1 个影响", rule.Editor.Summary);
        rule.Editor.AddPicker.SelectedOption = rule.Editor.AddPicker.FilteredOptions.Single(option => option.Id == 11);
        Assert.Equal("10 11", status.Influences.Trim());
        Assert.Equal("2 个影响", rule.Editor.Summary);
        statusDocument.UndoCommand.Execute(null);
        Assert.Equal("10", status.Influences.Trim());
        Assert.Equal("1 个影响", rule.Editor.Summary);
    }

    [Fact]
    public void BiographyAdjective_EditsTextListsOnePerLine()
    {
        var adjective = new BiographyAdjectiveConfig { Id = 1, Strength = 90, Text = ["勇冠三军"], SuffixText = [] };
        var (document, editor) = Open("biography-adjectives", [adjective]);
        using var _ = document;
        var text = Assert.IsType<TableWideTextViewModel>(editor.Sections[1].WideItems[0]).Field;

        Assert.Equal("#1", editor.SelectedTitle);
        Assert.Equal("勇冠三军", text.TextListValue);
        text.TextListValue = "勇冠三军\r\n\r\n 万人敌 ";
        Assert.Equal(["勇冠三军", "万人敌"], adjective.Text);
        document.UndoCommand.Execute(null);
        Assert.Equal(["勇冠三军"], adjective.Text);
    }

    [Fact]
    public void BattleActions_NameTheirAttackAndCastKinds()
    {
        var method = new CombatMethodConfig { Id = 1, Name = "齐射", AttackDefaultString = 2, AttackTargetString = 7 };
        var (document, editor) = Open("combat-methods", [method],
            Common("attack-default-kinds", new AttackDefaultKindConfig { Id = 2, Name = "攻击最弱敌军" }),
            Common("attack-target-kinds", new AttackTargetKindConfig { Id = 7, Name = "单体" }));
        using var _ = document;
        Assert.Equal("2 攻击最弱敌军", editor.Sections[0].Group.Fields.Single(field => field.Label == "攻击默认").SelectedChoice?.Label);
        Assert.Equal("7 单体", editor.Sections[0].Group.Fields.Single(field => field.Label == "攻击目标").SelectedChoice?.Label);

        var stratagem = new StratagemConfig { Id = 1, Name = "伪报", CastDefaultString = -1, CastTargetString = 3 };
        var (stratagemDocument, stratagemEditor) = Open("stratagems", [stratagem],
            Common("cast-target-kinds", new CastTargetKindConfig { Id = 3, Name = "敌军部队" }));
        using var __ = stratagemDocument;
        var castDefault = stratagemEditor.Sections[0].Group.Fields.Single(field => field.Label == "施放默认");
        Assert.Equal("-1 无", castDefault.SelectedChoice?.Label);
        Assert.Equal("3 敌军部队", stratagemEditor.Sections[0].Group.Fields.Single(field => field.Label == "施放目标")
            .SelectedChoice?.Label);
    }

    [Fact]
    public void SectionAIDetail_NamesOrientationKindsInChinese()
    {
        var detail = new SectionAIDetailConfig { Id = 0, Name = "全权委任", OrientationKind = SectionOrientationKind.Section };
        var (document, editor) = Open("section-ai-details", [detail]);
        using var _ = document;
        var kind = editor.Sections[0].Group.Fields.Single(field => field.Label == "目标种类");

        Assert.Equal(["无", "军团", "势力", "州域", "建筑"], kind.Options);
        Assert.Equal("军团", kind.SelectedOption);
        kind.SelectedOption = "州域";
        Assert.Equal(SectionOrientationKind.State, detail.OrientationKind);
    }

    [Fact]
    public void MilitaryKind_ListsKindsAsChipsAndNamesTerrainAndBehaviour()
    {
        var spear = new MilitaryKindConfig
        {
            Id = 1, Name = "长枪兵", AttackDefaultKind = TroopAttackDefaultKind.WeakestAttack, OneAdaptabilityKind = 6,
            LevelUpKindID = [2], SuccessorString = "2 "
        };
        var (document, editor) = Open("military-kinds",
            [spear, new MilitaryKindConfig { Id = 2, Name = "精锐长枪兵" }, new MilitaryKindConfig { Id = 3, Name = "重骑兵" }]);
        using var _ = document;
        var behaviour = editor.Sections.Single(section => section.Title == "默认行为").Group.Fields[0];
        var terrain = editor.Sections.Single(section => section.Title == "地形");
        var upgrade = editor.Sections.Single(section => section.Title == "升级与变换");

        Assert.Equal("攻最弱", behaviour.SelectedOption);
        Assert.Equal("6 水域", terrain.Group.Fields.Single(field => field.Label == "单一适性").SelectedChoice?.Label);
        Assert.Equal(4 * TableSectionViewModel.CellWidth, terrain.FieldsMaxWidth);
        var levelUp = upgrade.WideItems.OfType<PersonIdChipListViewModel>().Single(list => list.Label == "升级成");
        var successors = upgrade.WideItems.OfType<PersonIdChipListViewModel>().Single(list => list.Label == "较强兵种");
        Assert.Equal(["2 精锐长枪兵"], levelUp.Items.Select(item => item.Label));
        successors.Candidate = successors.Candidates.Single(option => option.Id == 3);
        successors.AddCommand.Execute(null);
        Assert.Equal("2 3", spear.SuccessorString.Trim());
    }

    [Fact]
    public void NumberArray_IsEditedAsText()
    {
        var kind = new CharacterKindConfig { Id = 0, Name = "冷静", GenerationChance = new int[10] };
        var (document, editor) = Open("character-kinds", [kind]);
        using var _ = document;
        var chance = editor.Sections[1].Group.Fields.Single();

        chance.IdListText = "1 2 3 4 5 6 7 8 9 10";
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], kind.GenerationChance);
    }
}
