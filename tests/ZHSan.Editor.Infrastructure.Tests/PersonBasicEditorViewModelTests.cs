using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class PersonBasicEditorViewModelTests
{
    [Fact]
    public void BasicEditor_SelectsPersonEditsAndUndoesThroughDocument()
    {
        var first = new PersonConfig { Id = 1, SurName = "关", GivenName = "羽", BaseStrength = 97 };
        var second = new PersonConfig { Id = 2, SurName = "张", GivenName = "飞", BaseStrength = 98 };
        var data = new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [first, second]
        };
        using var document = new ConfigDocumentViewModel(data, new ReflectionConfigMetadataProvider(),
            _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]));
        var editor = Assert.IsType<PersonBasicEditorViewModel>(document.SpecializedEditor?.Content);

        Assert.Same(document.Records[0], document.SelectedRecord);
        Assert.Equal("关羽", editor.SelectedTitle);
        var strength = editor.Groups.SelectMany(group => group.Fields)
            .Single(field => field.Label == "武勇");
        strength.NumericValue = 99;
        Assert.Equal(99, first.BaseStrength);
        Assert.True(document.IsDirty);

        document.UndoCommand.Execute(null);
        Assert.Equal(97, first.BaseStrength);
        Assert.Equal(97, strength.NumericValue);

        editor.SelectedPerson = editor.People[1];
        Assert.Same(document.Records[1], document.SelectedRecord);
        Assert.Equal(98, strength.NumericValue);
        editor.SearchText = "张";
        Assert.Single(editor.FilteredPeople);
        Assert.Same(editor.People[1], editor.SelectedPerson);
    }

    [Fact]
    public void Sex_UsesFemaleForTrueAndMaleForFalse()
    {
        var female = new PersonConfig { Id = 1, Sex = true };
        var male = new PersonConfig { Id = 2, Sex = false };
        using var document = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [female, male]
        }, new ReflectionConfigMetadataProvider(), _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]));
        var editor = Assert.IsType<PersonBasicEditorViewModel>(document.SpecializedEditor?.Content);
        var sex = editor.Groups.SelectMany(group => group.Fields)
            .Single(field => field.Label == "性别");

        Assert.True(sex.IsSex);
        Assert.False(sex.IsBoolean);
        Assert.Equal("女", sex.SelectedSex);
        var sexColumn = document.Properties.ToList().FindIndex(property => property.Name == nameof(PersonConfig.Sex));
        Assert.Equal("女", document.Records[0].Cells[sexColumn].DisplayValue);
        Assert.Equal("男", document.Records[1].Cells[sexColumn].DisplayValue);
        var genericSex = document.PropertyEditors.Single(field => field.Definition.Name == nameof(PersonConfig.Sex));
        Assert.True(genericSex.ShowSex);
        Assert.False(genericSex.ShowBoolean);
        Assert.Equal("女", genericSex.SelectedSex);
        editor.SelectedPerson = editor.People[1];
        Assert.Equal("男", sex.SelectedSex);
        genericSex = document.PropertyEditors.Single(field => field.Definition.Name == nameof(PersonConfig.Sex));
        Assert.Equal("男", genericSex.SelectedSex);

        sex.SelectedSex = "女";
        Assert.True(male.Sex);
        document.UndoCommand.Execute(null);
        Assert.False(male.Sex);
        Assert.Equal("男", sex.SelectedSex);

        genericSex = document.PropertyEditors.Single(field => field.Definition.Name == nameof(PersonConfig.Sex));
        genericSex.SelectedSex = "女";
        Assert.True(male.Sex);
        document.UndoCommand.Execute(null);
        Assert.False(male.Sex);
    }
    [Fact]
    public void Associations_ToggleNamedSkillsAndEditTitleAndCombatExperienceWithUndo()
    {
        var person = new PersonConfig
        {
            Id = 7,
            SkillsString = "11 99",
            StuntsString = "21",
            RealTitlesString = "31",
            PersonalTitleString = 31,
            StudyingStuntString = 21,
            UniqueMilitaryKindsString = "41",
            TacticsExperience = 14
        };
        var common = new EditorProject
        {
            Scope = ConfigScope.Common,
            ArchivePath = "common.dat",
            Documents =
            [
                new ConfigDocument
                {
                    Definition = new ConfigDefinition("skills", "技能", "能力", "Skills.json",
                        typeof(SkillConfig), ConfigScope.Common),
                    Items = [new SkillConfig { Id = 11, Name = "骑术" },
                        new SkillConfig { Id = 12, Name = "弓术" }]
                },
                new ConfigDocument
                {
                    Definition = new ConfigDefinition("stunts", "特技", "战斗", "Stunts.json",
                        typeof(StuntConfig), ConfigScope.Common),
                    Items = [new StuntConfig { Id = 21, Name = "神速" }]
                },
                new ConfigDocument
                {
                    Definition = new ConfigDefinition("titles", "称号", "称号", "Titles.json",
                        typeof(TitleConfig), ConfigScope.Common),
                    Items = [new TitleConfig { Id = 31, Name = "名将" },
                        new TitleConfig { Id = 32, Name = "军师" }]
                },
                new ConfigDocument
                {
                    Definition = new ConfigDefinition("military-kinds", "兵种", "战斗", "MilitaryKinds.json",
                        typeof(MilitaryKindConfig), ConfigScope.Common),
                    Items = [new MilitaryKindConfig { Id = 41, Name = "骑兵" }]
                }
            ]
        };
        var personData = new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [person]
        };
        var scenario = new EditorProject
        {
            Scope = ConfigScope.Scenario,
            ArchivePath = "scenario.dat",
            Documents = [personData]
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([common, scenario]);
        using var document = new ConfigDocumentViewModel(personData, metadata, _ => { },
            referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]));
        var editor = Assert.IsType<PersonBasicEditorViewModel>(document.SpecializedEditor?.Content);
        PersonAssociationFieldViewModel Field(string label) => editor.AssociationGroups
            .SelectMany(group => group.Fields).Single(field => field.Label == label);

        Assert.DoesNotContain(editor.AssociationGroups.SelectMany(group => group.Fields),
            field => field.Label.Contains("特技", StringComparison.Ordinal));
        var riding = Assert.Single(editor.SkillButtons, button => button.Name == "骑术");
        var archery = Assert.Single(editor.SkillButtons, button => button.Name == "弓术");
        Assert.True(riding.IsSelected);
        Assert.False(archery.IsSelected);
        Assert.Contains("99", editor.SkillStatus);
        archery.IsSelected = true;
        Assert.Equal("11 99 12", person.SkillsString);
        Assert.True(archery.IsSelected);
        document.UndoCommand.Execute(null);
        Assert.Equal("11 99", person.SkillsString);
        Assert.False(archery.IsSelected);
        Assert.Contains("99", editor.SkillStatus);
        riding.IsSelected = false;
        Assert.Equal("99", person.SkillsString);
        document.UndoCommand.Execute(null);
        Assert.Equal("11 99", person.SkillsString);
        Assert.True(riding.IsSelected);

        Assert.NotNull(Field("已有称号").Editor?.StructuredStringEditor);
        Assert.NotNull(Field("专属称号").Editor?.StructuredStringEditor);
        Assert.NotNull(Field("专属兵种").Editor?.StructuredStringEditor);
        Assert.NotNull(Field("战斗称号").Editor?.ReferencePicker);
        Assert.NotNull(Field("正在学习的称号").Editor?.ReferencePicker);
        var titlePicker = Assert.IsType<ReferencePickerViewModel>(Field("个人称号").Editor?.ReferencePicker);
        Assert.Contains("名将", titlePicker.SelectedOption?.Label);
        titlePicker.SelectedOption = Assert.Single(titlePicker.FilteredOptions, option => option.Id == 32);
        Assert.Equal(32, person.PersonalTitleString);
        document.UndoCommand.Execute(null);
        Assert.Equal(31, person.PersonalTitleString);

        var tactics = Assert.IsType<PropertyEditorViewModel>(Field("战法经验").Editor);
        tactics.NumericValue = 25;
        Assert.Equal(25, person.TacticsExperience);
        document.UndoCommand.Execute(null);
        Assert.Equal(14, person.TacticsExperience);
    }
    [Fact]
    public void Biography_TracksSelectedPersonAndUsesBiographyUndoHistory()
    {
        var first = new PersonConfig { Id = 7, SurName = "关", GivenName = "羽" };
        var second = new PersonConfig { Id = 8, SurName = "张", GivenName = "飞" };
        var biography = new BiographyConfig { Id = 7, Brief = "旧简介", History = "旧历史" };
        var metadata = new ReflectionConfigMetadataProvider();
        using var biographies = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("biographies", "列传", "人物", "Biographies.json",
                typeof(BiographyConfig), ConfigScope.Scenario),
            Items = [biography]
        }, metadata, _ => { });
        using var people = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [first, second]
        }, metadata, _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]),
            findRelatedDocument: key => key == "biographies" ? biographies : null);
        var editor = Assert.IsType<PersonBasicEditorViewModel>(people.SpecializedEditor?.Content);

        Assert.True(editor.HasBiography);
        var brief = editor.BiographyFields.Single(field => field.Label == "简介");
        brief.TextValue = "新简介";
        Assert.Equal("新简介", biography.Brief);
        Assert.True(biographies.IsDirty);
        Assert.False(people.IsDirty);
        biographies.UndoCommand.Execute(null);
        Assert.Equal("旧简介", brief.TextValue);

        editor.SelectedPerson = editor.People[1];
        Assert.False(editor.HasBiography);
        Assert.True(editor.CanAddBiography);
        editor.AddBiographyCommand.Execute(null);
        var added = Assert.IsType<BiographyConfig>(biographies.Records.Single(
            record => record.Item is BiographyConfig item && item.Id == 8).Item);
        Assert.Equal(string.Empty, added.Brief);
        Assert.True(editor.HasBiography);
        biographies.UndoCommand.Execute(null);
        Assert.False(editor.HasBiography);
    }

    [Fact]
    public void Biography_MissingEntryIsInitializedWhenCreatingFirstRecord()
    {
        var metadata = new ReflectionConfigMetadataProvider();
        var biographyData = new ConfigDocument
        {
            Definition = new ConfigDefinition("biographies", "列传", "人物", "Biographies.json",
                typeof(BiographyConfig), ConfigScope.Scenario),
            Items = [],
            EntryState = ArchiveEntryState.Missing
        };
        using var biographies = new ConfigDocumentViewModel(biographyData, metadata, _ => { });
        using var people = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [new PersonConfig { Id = 7, SurName = "关", GivenName = "羽" }]
        }, metadata, _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]),
            findRelatedDocument: key => key == "biographies" ? biographies : null);
        var editor = Assert.IsType<PersonBasicEditorViewModel>(people.SpecializedEditor?.Content);

        editor.AddBiographyCommand.Execute(null);

        Assert.False(biographies.CanInitializeEntry);
        Assert.True(editor.HasBiography);
        Assert.Equal(7, Assert.IsType<BiographyConfig>(Assert.Single(biographies.Records).Item).Id);
    }
    [Fact]
    public void Biography_DuplicateIdsAreNotEditedOrCreated()
    {
        var metadata = new ReflectionConfigMetadataProvider();
        using var biographies = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("biographies", "列传", "人物", "Biographies.json",
                typeof(BiographyConfig), ConfigScope.Scenario),
            Items = [new BiographyConfig { Id = 7 }, new BiographyConfig { Id = 7 }]
        }, metadata, _ => { });
        using var people = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [new PersonConfig { Id = 7, SurName = "关", GivenName = "羽" }]
        }, metadata, _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]),
            findRelatedDocument: key => key == "biographies" ? biographies : null);
        var editor = Assert.IsType<PersonBasicEditorViewModel>(people.SpecializedEditor?.Content);

        Assert.False(editor.HasBiography);
        Assert.False(editor.CanAddBiography);
        Assert.Contains("重复", editor.BiographyStatus);
    }
    [Fact]
    public void Provider_MatchesOnlyPersons()
    {
        var provider = new PersonBasicEditorProvider();
        Assert.True(provider.CanEdit(new ConfigDefinition("persons", "人物", "人物",
            "Persons.json", typeof(PersonConfig), ConfigScope.Scenario)));
        Assert.False(provider.CanEdit(new ConfigDefinition("biographies", "列传", "人物",
            "Biographies.json", typeof(BiographyConfig), ConfigScope.Scenario)));
    }
}
