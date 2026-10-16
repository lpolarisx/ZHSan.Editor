using GameDatas;
using GameEnums;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class PersonMessagesViewModelTests
{
    private static (ConfigDocumentViewModel People, ConfigDocumentViewModel Messages, PersonBasicEditorViewModel Editor)
        Create(params PersonMessageConfig[] messages)
    {
        var metadata = new ReflectionConfigMetadataProvider();
        var messageDocument = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new GameDataConfigRegistry().Find("person-messages")!,
            Items = [..messages]
        }, metadata, _ => { });
        var people = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [new PersonConfig { Id = 6, SurName = "曹", GivenName = "操" },
                new PersonConfig { Id = 16, SurName = "刘", GivenName = "备" }]
        }, metadata, _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]),
            findRelatedDocument: key => key == "person-messages" ? messageDocument : null);
        var editor = Assert.IsType<PersonBasicEditorViewModel>(people.SpecializedEditor?.Content);
        editor.SelectedTabIndex = PersonBasicEditorViewModel.MessageTab;
        return (people, messageDocument, editor);
    }

    [Fact]
    public void Messages_ShowTheSelectedPersonsKindsWithChineseLabels()
    {
        var (people, messages, editor) = Create(
            new PersonMessageConfig { PersonId = 6, Kind = TextMessageKind.Rout, Messages = ["破敌！"] },
            new PersonMessageConfig { PersonId = 6, Kind = TextMessageKind.Critical, Messages = ["看招！", "受死吧！"] },
            new PersonMessageConfig { PersonId = 16, Kind = TextMessageKind.Critical, Messages = ["汉室当兴！"] });
        using var _ = people;
        using var __ = messages;

        Assert.Equal(["1 暴击", "5 击破"], editor.Messages.Groups.Select(group => group.KindLabel));
        Assert.Equal(["看招！", "受死吧！"], editor.Messages.Groups[0].Lines.Select(line => line.Text));
        Assert.DoesNotContain(editor.Messages.AvailableKinds, option => option.Id is 0 or 1 or 5);
        Assert.Contains(editor.Messages.AvailableKinds, option => option.Label == "87 武将发现怀孕／父亲子女出生");

        editor.SelectedPerson = editor.People[1];
        Assert.Equal(["汉室当兴！"], Assert.Single(editor.Messages.Groups).Lines.Select(line => line.Text));
    }

    [Fact]
    public void Messages_EditAddAndRemoveLinesAndKindsThroughCommonDocument()
    {
        var critical = new PersonMessageConfig { PersonId = 6, Kind = TextMessageKind.Critical, Messages = ["看招！", "受死吧！"] };
        var (people, messages, editor) = Create(critical);
        using var _ = people;
        using var __ = messages;
        var group = Assert.Single(editor.Messages.Groups);
        var firstLine = group.Lines[0];

        firstLine.Text = "吃我一剑！";
        Assert.Equal(["吃我一剑！", "受死吧！"], critical.Messages);
        Assert.True(messages.IsDirty);
        Assert.Same(firstLine, group.Lines[0]);

        group.NewLine = "  还有谁？ ";
        group.AddLineCommand.Execute(null);
        Assert.Equal(["吃我一剑！", "受死吧！", "还有谁？"], critical.Messages);
        Assert.Equal(string.Empty, group.NewLine);

        group.Lines[1].Text = "   ";
        Assert.Equal(["吃我一剑！", "还有谁？"], critical.Messages);
        group.Lines[0].RemoveCommand.Execute(null);
        Assert.Equal(["还有谁？"], critical.Messages);

        editor.Messages.NewKind = editor.Messages.AvailableKinds.Single(option => option.Id == (int)TextMessageKind.Rout);
        editor.Messages.AddKindCommand.Execute(null);
        var rout = Assert.IsType<PersonMessageConfig>(messages.Records[^1].Item);
        Assert.Equal((6, TextMessageKind.Rout, 0), (rout.PersonId, rout.Kind, rout.Messages.Count));
        Assert.Equal(["1 暴击", "5 击破"], editor.Messages.Groups.Select(item => item.KindLabel));
        Assert.DoesNotContain(editor.Messages.AvailableKinds, option => option.Id == (int)TextMessageKind.Rout);

        editor.Messages.Groups[0].RemoveCommand.Execute(null);
        Assert.Same(rout, Assert.Single(messages.Records).Item);
        Assert.Equal(["5 击破"], editor.Messages.Groups.Select(item => item.KindLabel));

        messages.UndoCommand.Execute(null);
        messages.RefreshReferenceOptions();
        people.RefreshReferenceOptions();
        Assert.Equal(["1 暴击", "5 击破"], editor.Messages.Groups.Select(item => item.KindLabel));
        Assert.Equal(["还有谁？"], editor.Messages.Groups[0].Lines.Select(line => line.Text));
    }

    [Fact]
    public void Messages_WithoutCommonDataOrWithDuplicates_ExplainWhy()
    {
        var (people, messages, editor) = Create(
            new PersonMessageConfig { PersonId = 6, Kind = TextMessageKind.Critical, Messages = ["一"] },
            new PersonMessageConfig { PersonId = 6, Kind = TextMessageKind.Critical, Messages = ["二"] });
        using var _ = people;
        using var __ = messages;
        Assert.Contains("暴击”有重复记录", editor.Messages.Status);

        using var alone = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [new PersonConfig { Id = 6, SurName = "曹", GivenName = "操" }]
        }, new ReflectionConfigMetadataProvider(), _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]));
        var lonely = Assert.IsType<PersonBasicEditorViewModel>(alone.SpecializedEditor?.Content);
        lonely.SelectedTabIndex = PersonBasicEditorViewModel.MessageTab;
        Assert.False(lonely.Messages.CanEdit);
        Assert.Contains("请同时打开 CommonData", lonely.Messages.Status);
    }
}
