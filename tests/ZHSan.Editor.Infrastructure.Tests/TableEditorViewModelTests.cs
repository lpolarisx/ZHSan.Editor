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
        // Each document goes to the archive of its own scope, as when a scenario is opened over CommonData.
        ConfigDocument[] all = [document, ..others];
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([
            new EditorProject
            {
                Scope = ConfigScope.Common, ArchivePath = "common.dat",
                Documents = [..all.Where(item => item.Definition.Scope == ConfigScope.Common)]
            },
            new EditorProject
            {
                Scope = ConfigScope.Scenario, ArchivePath = "scenario.dat",
                Documents = [..all.Where(item => item.Definition.Scope == ConfigScope.Scenario)]
            }
        ]);
        var viewModel = new ConfigDocumentViewModel(document, metadata, _ => { }, referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new TableEditorProvider(TableEditorSpecs.All)]));
        return (viewModel, Assert.IsType<TableEditorViewModel>(viewModel.SpecializedEditor?.Content));
    }

    private static ConfigDocument Common<T>(string key, params T[] items) where T : notnull =>
        new() { Definition = Registry.Find(key)!, Items = [..items.Cast<object>()] };

    private static ConfigDocument Scenario<T>(string key, params T[] items) where T : notnull =>
        new()
        {
            Definition = Registry.Find(new ConfigAddress(ConfigScope.Scenario, key))!, Items = [..items.Cast<object>()]
        };

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
    public void InformationKind_IsNamedByLevelAndRangeWithoutANameField()
    {
        var kind = new InformationKindConfig { Id = 2, Name = "", Level = InformationLevel.High, Radius = 6, CostFund = 800 };
        var (document, editor) = Open("information-kinds", [kind]);
        using var _ = document;

        Assert.DoesNotContain(editor.AllFields, field => field.Label == "名称");
        Assert.Equal("高 · 半径 6 · 800 资金", editor.SelectedTitle);
        editor.Sections[0].Group.Fields.Single(field => field.Label == "斜向").BooleanValue = true;
        Assert.Equal("高 · 半径 6 斜向 · 800 资金", editor.SelectedTitle);
    }

    [Fact]
    public void FactionColor_AppliesThePickedColorInOneStep()
    {
        var color = new FactionColorConfig { Id = 3, R = 0x10, G = 0x20, B = 0x30 };
        var metadata = new ReflectionConfigMetadataProvider();
        var colors = new ConfigDocument { Definition = Registry.Find("colors")!, Items = [color] };
        using var document = new ConfigDocumentViewModel(colors, metadata, _ => { },
            editorProviderRegistry: new ConfigEditorProviderRegistry(
                [new FactionColorEditorProvider(), new TableEditorProvider(TableEditorSpecs.All)]));
        var editor = Assert.IsType<FactionColorEditorViewModel>(document.SpecializedEditor?.Content);

        Assert.Equal("#102030", editor.SelectedTitle);
        Assert.False(editor.HasChanges);
        editor.DraftColor = Avalonia.Media.Color.FromArgb(200, 0xFF, 0x80, 0x30);
        Assert.True(editor.HasChanges);
        Assert.Equal(0x10, color.R);
        editor.ApplyCommand.Execute(null);
        Assert.Equal((0xFF, 0x80, 0x30, 200), (color.R, color.G, color.B, color.A));
        Assert.Equal("#FF8030", editor.SelectedTitle);
        Assert.False(editor.HasChanges);

        document.UndoCommand.Execute(null);
        Assert.Equal((0x10, 0x20, 0x30, 255), (color.R, color.G, color.B, color.A));
        Assert.Equal(Avalonia.Media.Color.FromRgb(0x10, 0x20, 0x30), editor.DraftColor);

        editor.DraftColor = Avalonia.Media.Colors.Red;
        editor.ResetCommand.Execute(null);
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void CommonRecords_AddedCopiedOrPastedContinueAfterTheHighestId()
    {
        var (document, _) = Open("condition-kinds",
            [new ConditionKindConfig { Id = 0, Name = "统率" }, new ConditionKindConfig { Id = 5, Name = "武力" }]);
        using var __ = document;
        int LastId() => ((ConditionKindConfig)document.Records[^1].Item).Id;

        document.AddCommand.Execute(null);
        Assert.Equal(6, LastId());
        document.SelectedRecord = document.Records[1];
        document.CopyCommand.Execute(null);
        Assert.Equal(7, LastId());
        Assert.Equal("武力", ((ConditionKindConfig)document.Records[^1].Item).Name);
        document.SelectedRecord = document.Records[0];
        document.CopyToClipboardCommand.Execute(null);
        document.PasteCommand.Execute(null);
        Assert.Equal(8, LastId());
        Assert.Equal([0, 5, 6, 7, 8], document.Records.Select(record => ((ConditionKindConfig)record.Item).Id));
        document.UndoCommand.Execute(null);
        document.AddCommand.Execute(null);
        Assert.Equal(8, LastId());

        // Colours are looked up by position, so a new one must take the next position to be saved.
        var colors = new ConfigDocument
        {
            Definition = Registry.Find("colors")!,
            Items = [new FactionColorConfig { Id = 0 }, new FactionColorConfig { Id = 1 }]
        };
        using var colorDocument = new ConfigDocumentViewModel(colors, new ReflectionConfigMetadataProvider(), _ => { });
        colorDocument.AddCommand.Execute(null);
        Assert.Equal([0, 1, 2], colors.Items.Cast<FactionColorConfig>().Select(color => color.Id));
    }

    [Fact]
    public void ScenarioRecords_AreNamedAfterWhatTheyReference()
    {
        var persons = Scenario("persons", new PersonConfig { Id = 16, SurName = "刘", GivenName = "备" },
            new PersonConfig { Id = 17, SurName = "关", GivenName = "羽" });
        var factions = Scenario("factions", new FactionConfig { Id = 2, Name = "刘备" });

        var captive = new CaptiveConfig { Id = 1, CaptivePersonId = 17, CaptiveFactionId = 2, RansomArchitectureId = -1 };
        var (captives, captiveEditor) = Open("captives", [captive], persons, factions);
        using (captives)
        {
            Assert.Equal("关羽 · 原属刘备", captiveEditor.SelectedTitle);
            var faction = captiveEditor.Sections[0].Group.Fields.Single(field => field.Label == "原属势力");
            faction.SelectedChoice = faction.ChoiceOptions.Single(option => option.Id == -1);
            Assert.Equal("关羽", captiveEditor.SelectedTitle);
            Assert.Equal("-1 无", captiveEditor.Sections[1].Group.Fields.Single(field => field.Label == "目标建筑")
                .SelectedChoice?.Label);
        }

        var relation = new PersonRelationConfig { PersonID1 = 16, PersonID2 = 17, Relation = 80 };
        var (relations, relationEditor) = Open("person-relations", [relation], persons);
        using (relations) Assert.Equal("刘备 ↔ 关羽", relationEditor.SelectedTitle);

        var facility = new FacilityConfig { Id = 5, LevelId = 2, Endurance = 100 };
        var (facilities, facilityEditor) = Open("facilities", [facility],
            Common("facility-kind-levels", new FacilityKindLevelConfig { Id = 2, KindId = 9, Level = 2 }),
            Common("facility-kinds", new FacilityKindConfig { Id = 9, Name = "农田" }));
        using (facilities)
        {
            Assert.Equal("农田 Lv2", facilityEditor.SelectedTitle);
            Assert.Equal("2 农田 Lv2", facilityEditor.Sections[0].Group.Fields.Single(field => field.Label == "设施等级")
                .SelectedChoice?.Label);
        }

        var military = new MilitaryConfig
        {
            Id = 3, Name = "青州兵", LeaderID = 16, FollowedLeaderID = -1, RecruitmentPersonID = -1,
            StartingArchitectureID = -1, TargetArchitectureID = -1, ShelledMilitaryID = -1
        };
        var (militaries, militaryEditor) = Open("militaries", [military], persons);
        using (militaries)
        {
            var leaders = militaryEditor.Sections.Single(section => section.Title == "将领").Group.Fields;
            Assert.Equal("16 刘备", leaders.Single(field => field.Label == "队长").SelectedChoice?.Label);
            Assert.Equal("-1 无", leaders.Single(field => field.Label == "追随将领").SelectedChoice?.Label);
        }
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
