using GameDatas;
using GameEnums;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class EventEditorViewModelTests
{
    private static ConfigDefinition Scenario(string key) =>
        new GameDataConfigRegistry().Find(new ConfigAddress(ConfigScope.Scenario, key))!;

    private static (ConfigDocumentViewModel Document, TEditor Editor) Create<TEditor>(
        string key, object record, IConfigEditorProvider provider)
    {
        var metadata = new ReflectionConfigMetadataProvider();
        var document = new ConfigDocument { Definition = Scenario(key), Items = [record] };
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([new EditorProject
        {
            Scope = ConfigScope.Scenario, ArchivePath = "scenario.dat", Documents =
            [
                document,
                new ConfigDocument
                {
                    Definition = Scenario("persons"),
                    Items = [new PersonConfig { Id = 6, SurName = "曹", GivenName = "操" },
                        new PersonConfig { Id = 16, SurName = "刘", GivenName = "备" },
                        new PersonConfig { Id = 17, SurName = "关", GivenName = "羽" }]
                }
            ]
        }]);
        var viewModel = new ConfigDocumentViewModel(document, metadata, _ => { }, referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([provider]));
        return (viewModel, Assert.IsType<TEditor>(viewModel.SpecializedEditor?.Content));
    }

    [Fact]
    public void Event_SlotsEditPersonsConditionsAndEffectsOfOneSlot()
    {
        var config = new EventConfig
        {
            Id = 1, Name = "桃园结义", HappenChance = 1, PersonString = "0 16 1 17 1 6 ",
            PersonCondString = "0 801 1 803 ", DialogString = "0 我等三人 1 愿同生共死 ", EffectString = "",
            NextScenario = "\r"
        };
        var (document, editor) = Create<EventEditorViewModel>("events", config, new EventEditorProvider());
        using var _ = document;

        Assert.Equal(["名称", "前置事件", "发动几率", "可以重复", "不重要", "全势力可见", "已发生过"],
            editor.BasicGroup.Fields.Select(field => field.Label));
        Assert.Equal("条件满足时必定发生", editor.BasicGroup.Note);
        Assert.Equal(["武将 %0", "武将 %1"], editor.Slots.Select(slot => slot.Title));
        Assert.Equal(["17 关羽", "6 曹操"], editor.Slots[1].Persons.Items.Select(item => item.Label));
        Assert.Equal(["%0 刘备", "%1 关羽／曹操"], editor.Dialogs[0].SpeakerOptions.Select(option => option.Label));

        // Removing 曹操 from slot 1 keeps slot 0 and the order of the other pairs.
        editor.Slots[1].Persons.Items.Single(item => item.Id == 6).RemoveCommand.Execute(null);
        Assert.Equal("0 16 1 17 ", config.PersonString);
        editor.Slots[0].Persons.Candidate = editor.Slots[0].Persons.Candidates.Single(option => option.Id == -1);
        editor.Slots[0].Persons.AddCommand.Execute(null);
        Assert.Equal("0 16 0 -1 1 17 ", config.PersonString);
        Assert.Equal("-1 任何武将", editor.Slots[0].Persons.Items[1].Label);

        editor.AddSlotCommand.Execute(null);
        Assert.Equal(3, editor.Slots.Count);
        Assert.Equal("0 16 0 -1 1 17 2 -1 ", config.PersonString);

        // Only the last slot can be removed; its dialogue goes with it in one step.
        Assert.False(editor.Slots[1].RemoveCommand.CanExecute(null));
        editor.Slots[2].Persons.Items[0].RemoveCommand.Execute(null);
        Assert.Equal(2, editor.Slots.Count);
        editor.Slots[1].RemoveCommand.Execute(null);
        Assert.Single(editor.Slots);
        Assert.Equal("0 16 0 -1 ", config.PersonString);
        Assert.Equal("0 801 ", config.PersonCondString);
        Assert.Equal("0 我等三人 ", config.DialogString);
        document.UndoCommand.Execute(null);
        Assert.Equal("0 我等三人 1 愿同生共死 ", config.DialogString);
        Assert.Equal(2, editor.Slots.Count);
        Assert.Equal("\r", config.NextScenario);
    }

    [Fact]
    public void Event_DialogueLinesAreEditedInPlaceWithoutSpaces()
    {
        var config = new EventConfig
        {
            Id = 1, Name = "桃园结义", HappenChance = 3, PersonString = "0 16 1 17 ",
            DialogString = "0 我等三人 1 愿同生共死 "
        };
        var (document, editor) = Create<EventEditorViewModel>("events", config, new EventEditorProvider());
        using var _ = document;
        var dialog = editor.Dialogs[0];
        var first = dialog.Lines[0];

        Assert.Equal("条件满足时每次检查有 1/3 的机会发生", editor.BasicGroup.Note);
        first.Text = "%0 我等 三人";
        Assert.Equal("0 %0我等三人 1 愿同生共死 ", config.DialogString);
        Assert.True(dialog.HasNotice);
        Assert.Same(first, dialog.Lines[0]);

        dialog.Lines[1].MoveUpCommand.Execute(null);
        Assert.Equal("1 愿同生共死 0 %0我等三人 ", config.DialogString);
        dialog.Lines[0].Speaker = dialog.SpeakerOptions[0];
        Assert.Equal("0 愿同生共死 0 %0我等三人 ", config.DialogString);

        dialog.NewSpeaker = dialog.SpeakerOptions[1];
        dialog.NewText = "誓扶汉室";
        dialog.AddCommand.Execute(null);
        Assert.Equal("0 愿同生共死 0 %0我等三人 1 誓扶汉室 ", config.DialogString);
        Assert.Equal(string.Empty, dialog.NewText);

        dialog.Lines[0].Text = "   ";
        Assert.Equal("0 %0我等三人 1 誓扶汉室 ", config.DialogString);
        Assert.Equal(2, dialog.Lines.Count);
    }

    [Fact]
    public void TroopEvent_PairListsAndSearchAreaUseChineseLabels()
    {
        var config = new TroopEventConfig
        {
            Id = 0, Name = "普通爆发", HappenChance = 30, LaunchPersonString = -1, ConditionsString = "",
            TargetPersonsString = "1 16 ", EffectAreasString = "", EffectPersonsString = "",
            DialogString = "-1 我是不会失败的！ ", CheckArea = EventCheckAreaKind.Vision
        };
        var (document, editor) = Create<TroopEventEditorViewModel>("troop-events", config, new TroopEventEditorProvider());
        using var _ = document;

        Assert.Equal("-1 任何部队", editor.BasicGroup.Fields.Single(field => field.Label == "发动人物").SelectedChoice?.Label);
        Assert.Equal("条件满足时有 30% 的机会发生", editor.BasicGroup.Note);
        Assert.Equal(("1 友好", "16 刘备"), (editor.TargetPersons.Rows[0].First, editor.TargetPersons.Rows[0].Second));
        Assert.Equal("-1 发动部队的主将", editor.Dialog.Lines[0].Speaker?.Label);
        Assert.Equal("0 视野内", editor.SelectedCheckArea?.Label);

        editor.TargetPersons.FirstCandidate = editor.TargetPersons.FirstOptions[0];
        editor.TargetPersons.SecondCandidate = editor.TargetPersons.SecondOptions.Single(option => option.Id == 6);
        editor.TargetPersons.AddCommand.Execute(null);
        Assert.Equal("1 16 0 6 ", config.TargetPersonsString);
        editor.TargetPersons.Rows[0].RemoveCommand.Execute(null);
        Assert.Equal("0 6 ", config.TargetPersonsString);

        editor.SelectedCheckArea = editor.CheckAreaOptions[2];
        Assert.Equal((EventCheckAreaKind)2, config.CheckArea);
        document.UndoCommand.Execute(null);
        Assert.Equal(EventCheckAreaKind.Vision, config.CheckArea);
        Assert.Equal("0 视野内", editor.SelectedCheckArea?.Label);
    }
}
