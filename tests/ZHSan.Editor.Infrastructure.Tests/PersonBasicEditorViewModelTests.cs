using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.Services;
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
        var strength = editor.AllFields
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
        var sex = editor.AllFields
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
    public void Associations_ToggleSkillsPickTitlesPerKindEditExclusiveListsAndExperienceWithUndo()
    {
        var person = new PersonConfig
        {
            Id = 7,
            SkillsString = "11 99",
            StuntsString = "21",
            RealTitlesString = "31 77",
            PersonalTitleString = 31,
            StudyingTitleString = -1,
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
                    Items = [new TitleConfig { Id = 31, Name = "名将", KindId = 1, Level = 3 },
                        new TitleConfig { Id = 32, Name = "猛将", KindId = 1, Level = 2 },
                        new TitleConfig { Id = 33, Name = "军师", KindId = 2, Level = 1 },
                        new TitleConfig { Id = 34, Name = "王佐", KindId = 3, Level = 5 }]
                },
                new ConfigDocument
                {
                    Definition = new ConfigDefinition("title-kinds", "称号类型", "称号", "TitleKinds.json",
                        typeof(TitleKindConfig), ConfigScope.Common),
                    Items = [new TitleKindConfig { Id = 1, Name = "武将" },
                        new TitleKindConfig { Id = 2, Name = "合称" },
                        new TitleKindConfig { Id = 3, Name = "合称" }]
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
        editor.SelectedTabIndex = PersonBasicEditorViewModel.SkillTab;
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

        Assert.Equal(["武将", "合称 2", "合称 3"], editor.TitleSlots.Select(slot => slot.KindName));
        var general = editor.TitleSlots[0];
        Assert.Equal(["无", "Lv2 · 猛将", "Lv3 · 名将"], general.Options.Select(option => option.Label));
        Assert.Equal(31, general.SelectedOption?.Id);
        Assert.Equal("无", editor.TitleSlots[1].SelectedOption?.Label);
        Assert.Contains("77", editor.TitleStatus);
        general.SelectedOption = general.Options.Single(option => option.Id == 32);
        Assert.Equal("32 77", person.RealTitlesString);
        editor.TitleSlots[2].SelectedOption = editor.TitleSlots[2].Options.Single(option => option.Id == 34);
        Assert.Equal("32 77 34", person.RealTitlesString);
        general.SelectedOption = general.Options[0];
        Assert.Equal("77 34", person.RealTitlesString);
        document.UndoCommand.Execute(null);
        document.UndoCommand.Execute(null);
        document.UndoCommand.Execute(null);
        Assert.Equal("31 77", person.RealTitlesString);
        Assert.Equal(31, general.SelectedOption?.Id);

        var studying = editor.StudyingTitleField;
        Assert.Equal("无", studying.SelectedChoice?.Label);
        studying.SelectedChoice = studying.ChoiceOptions.Single(option => option.Id == 34);
        Assert.Equal(34, person.StudyingTitleString);
        Assert.Equal("合称 3 · Lv5 · 王佐", studying.SelectedChoice?.Label);
        document.UndoCommand.Execute(null);
        Assert.Equal(-1, person.StudyingTitleString);

        var exclusiveKinds = editor.ExclusiveLists.Single(list => list.Label == "专属兵种");
        Assert.Equal(["41 骑兵"], exclusiveKinds.Items.Select(item => item.Label));
        var exclusiveTitles = editor.ExclusiveLists.Single(list => list.Label == "专属称号");
        Assert.True(exclusiveTitles.IsEmpty);
        exclusiveTitles.Candidate = exclusiveTitles.Candidates.Single(option => option.Id == 33);
        exclusiveTitles.AddCommand.Execute(null);
        Assert.Equal("33", person.UniqueTitlesString);
        Assert.Equal(["军师（合称 2 Lv1）"], exclusiveTitles.Items.Select(item => item.Label));
        exclusiveKinds.Items[0].RemoveCommand.Execute(null);
        Assert.Equal("", person.UniqueMilitaryKindsString);
        document.UndoCommand.Execute(null);
        Assert.Equal("41", person.UniqueMilitaryKindsString);
        Assert.Single(exclusiveKinds.Items);

        var tactics = editor.ExperienceGroup.Fields.Single(field => field.Label == "战法");
        Assert.Equal(14, tactics.NumericValue);
        tactics.NumericValue = 25;
        Assert.Equal(25, person.TacticsExperience);
        document.UndoCommand.Execute(null);
        Assert.Equal(14, person.TacticsExperience);
        Assert.Equal(31, person.PersonalTitleString);
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
    public void NewPerson_TakesNextIdAndCreatesBiographyInSameUndoStep()
    {
        var metadata = new ReflectionConfigMetadataProvider();
        using var biographies = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("biographies", "列传", "人物", "Biographies.json",
                typeof(BiographyConfig), ConfigScope.Scenario),
            // A leftover biography with no person still reserves its ID.
            Items = [new BiographyConfig { Id = 3 }, new BiographyConfig { Id = 12, Brief = "遗留" }]
        }, metadata, _ => { });
        using var people = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [new PersonConfig { Id = 3, SurName = "关", GivenName = "羽" },
                new PersonConfig { Id = 10, SurName = "张", GivenName = "飞" }]
        }, metadata, _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]),
            findRelatedDocument: key => key == "biographies" ? biographies : null);
        var editor = Assert.IsType<PersonBasicEditorViewModel>(people.SpecializedEditor?.Content);

        people.AddCommand.Execute(null);

        var added = Assert.IsType<PersonConfig>(people.Records[^1].Item);
        Assert.Equal(13, added.Id);
        var biography = Assert.IsType<BiographyConfig>(biographies.Records.Single(
            record => record.Item is BiographyConfig item && item.Id == 13).Item);
        Assert.Equal(string.Empty, biography.Brief);
        Assert.True(biographies.IsDirty);
        Assert.Same(people.Records[^1], people.SelectedRecord);
        Assert.True(editor.HasBiography);

        people.CopyCommand.Execute(null);
        Assert.Equal(14, Assert.IsType<PersonConfig>(people.Records[^1].Item).Id);
        Assert.Equal(4, biographies.Records.Count);

        people.UndoCommand.Execute(null);
        people.UndoCommand.Execute(null);
        Assert.Equal(2, people.Records.Count);
        Assert.Equal(2, biographies.Records.Count);
        Assert.False(biographies.IsDirty);

        people.RedoCommand.Execute(null);
        Assert.Contains(biographies.Records, record => record.Item is BiographyConfig { Id: 13 });
        Assert.True(biographies.IsDirty);
    }

    [Fact]
    public void DeletePerson_RemovesBiographyFamilyAndRelationsInSameUndoStep()
    {
        var metadata = new ReflectionConfigMetadataProvider();
        using var biographies = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("biographies", "列传", "人物", "Biographies.json",
                typeof(BiographyConfig), ConfigScope.Scenario),
            Items = [new BiographyConfig { Id = 1, Brief = "关羽" }, new BiographyConfig { Id = 2, Brief = "关平" }]
        }, metadata, _ => { });
        var sonFamily = new PersonFamilyConfig { Id = 2, FatherId = 1, CloseIdsString = "1 3" };
        using var families = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("person-families", "人物亲属", "人物", "GameScenarios.json",
                typeof(PersonFamilyConfig), ConfigScope.Scenario),
            Items = [new PersonFamilyConfig { Id = 1, SpouseId = 3 }, sonFamily]
        }, metadata, _ => { });
        using var relations = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("person-relations", "人物关系", "人物", "PersonRelations.json",
                typeof(PersonRelationConfig), ConfigScope.Scenario),
            Items = [new PersonRelationConfig { PersonID1 = 1, PersonID2 = 2, Relation = 50 },
                new PersonRelationConfig { PersonID1 = 2, PersonID2 = 3, Relation = 10 }]
        }, metadata, _ => { });
        using var people = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [new PersonConfig { Id = 1, SurName = "关", GivenName = "羽" },
                new PersonConfig { Id = 2, SurName = "关", GivenName = "平" }]
        }, metadata, _ => { }, findRelatedDocument: key => key switch
        {
            "biographies" => biographies,
            "person-families" => families,
            "person-relations" => relations,
            _ => null
        });

        people.SelectedRecord = people.Records[0];
        people.DeleteCommand.Execute(null);

        Assert.Equal([2], people.Records.Select(record => ((PersonConfig)record.Item).Id));
        Assert.Equal([2], biographies.Records.Select(record => ((BiographyConfig)record.Item).Id));
        Assert.Same(sonFamily, Assert.Single(families.Records).Item);
        Assert.Equal(-1, sonFamily.FatherId);
        Assert.Equal("3", sonFamily.CloseIdsString);
        Assert.Equal(3, ((PersonRelationConfig)Assert.Single(relations.Records).Item).PersonID2);
        Assert.True(biographies.IsDirty);
        Assert.True(families.IsDirty);
        Assert.True(relations.IsDirty);

        people.UndoCommand.Execute(null);
        Assert.Equal(2, people.Records.Count);
        Assert.Equal([1, 2], biographies.Records.Select(record => ((BiographyConfig)record.Item).Id));
        Assert.Equal(2, families.Records.Count);
        Assert.Equal(1, sonFamily.FatherId);
        Assert.Equal("1 3", sonFamily.CloseIdsString);
        Assert.Equal(2, relations.Records.Count);
        Assert.False(biographies.IsDirty);
        Assert.False(families.IsDirty);
        Assert.False(relations.IsDirty);

        people.RedoCommand.Execute(null);
        Assert.Single(biographies.Records);
        Assert.Equal(-1, sonFamily.FatherId);
        Assert.Single(relations.Records);
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
    public void Biography_FactionColorPicksFromCommonColorTableThroughBiographyDocument()
    {
        var metadata = new ReflectionConfigMetadataProvider();
        var biography = new BiographyConfig { Id = 7, FactionColor = 52 };
        var colorData = new ConfigDocument
        {
            Definition = new GameDataConfigRegistry().Find("colors")!,
            Items = [new FactionColorConfig { Id = 0, R = 160 }, new FactionColorConfig { Id = 1, R = 192, G = 128, B = 255 }]
        };
        var biographyData = new ConfigDocument
        {
            Definition = new ConfigDefinition("biographies", "列传", "人物", "Biographies.json",
                typeof(BiographyConfig), ConfigScope.Scenario),
            Items = [biography]
        };
        var personData = new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [new PersonConfig { Id = 7, SurName = "关", GivenName = "羽" }]
        };
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([
            new EditorProject { Scope = ConfigScope.Common, ArchivePath = "common.dat", Documents = [colorData] },
            new EditorProject { Scope = ConfigScope.Scenario, ArchivePath = "scenario.dat", Documents = [biographyData, personData] }
        ]);
        using var biographies = new ConfigDocumentViewModel(biographyData, metadata, _ => { });
        using var people = new ConfigDocumentViewModel(personData, metadata, _ => { }, referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]),
            findRelatedDocument: key => key == "biographies" ? biographies : null);
        var editor = Assert.IsType<PersonBasicEditorViewModel>(people.SpecializedEditor?.Content);

        Assert.Equal(52, editor.FactionColor);
        Assert.Equal(["52 （未收录）", "0  #A00000", "1  #C080FF"], editor.FactionColorOptions.Select(option => option.Label));
        Assert.Null(editor.SelectedFactionColor?.Swatch);
        editor.SelectedFactionColor = editor.FactionColorOptions.Single(option => option.Id == 1);
        Assert.Equal(1, biography.FactionColor);
        Assert.NotNull(editor.SelectedFactionColor?.Swatch);
        Assert.Equal(["0  #A00000", "1  #C080FF"], editor.FactionColorOptions.Select(option => option.Label));
        Assert.True(biographies.IsDirty);
        biographies.UndoCommand.Execute(null);
        Assert.Equal(52, editor.FactionColor);
        Assert.Equal("52 （未收录）", editor.SelectedFactionColor?.Label);
    }

    [Fact]
    public void BasicFields_UseReferenceFormLabelsAndChoices()
    {
        var person = new PersonConfig
        {
            Id = 7, SurName = "关", GivenName = "羽", Ideal = 74, PersonalLoyalty = 4, Ambition = 1,
            PCharacter = 3, Strain = 98, JoinFactionID = []
        };
        var commonData = new ConfigDocument
        {
            Definition = new ConfigDefinition("character-kinds", "性格类型", "人物", "CharacterKinds.json",
                typeof(CharacterKindConfig)),
            Items = [new CharacterKindConfig { Id = 3, Name = "高傲" }]
        };
        var personData = new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [person]
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([
            new EditorProject { Scope = ConfigScope.Common, ArchivePath = "common.dat", Documents = [commonData] },
            new EditorProject { Scope = ConfigScope.Scenario, ArchivePath = "scenario.dat", Documents = [personData] }
        ]);
        using var document = new ConfigDocumentViewModel(personData, metadata, _ => { },
            referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]));
        var editor = Assert.IsType<PersonBasicEditorViewModel>(document.SpecializedEditor?.Content);
        PersonBasicFieldViewModel Field(string label) =>
            editor.AllFields.Single(field => field.Label == label);

        Assert.Equal(74, Field("相性").NumericValue);
        Assert.Equal(98, Field("血缘").NumericValue);
        Assert.Equal("很高", Field("义理").SelectedChoice?.Label);
        Assert.Equal("低", Field("野心").SelectedChoice?.Label);
        Assert.Equal("3 高傲", Field("性格").SelectedChoice?.Label);

        Field("义理").SelectedChoice = Field("义理").ChoiceOptions.Single(option => option.Label == "普通");
        Assert.Equal(2, person.PersonalLoyalty);
        Field("仕官势力").IdListText = "3 5";
        Assert.Equal([3, 5], person.JoinFactionID);
        Field("仕官势力").IdListText = "3 x";
        Assert.Equal([3, 5], person.JoinFactionID);
        document.UndoCommand.Execute(null);
        Assert.Empty(person.JoinFactionID);
        Assert.Equal("", Field("仕官势力").IdListText);
    }

    [Fact]
    public void Layout_GroupsAbilitiesAndSummarizesLifeYears()
    {
        var person = new PersonConfig
        {
            Id = 7, SurName = "于", GivenName = "禁", YearBorn = 159, YearAvailable = 184, YearDead = 221,
            BaseStrength = 85, StrengthExperience = 5802, IdealTendencyIDString = 2
        };
        var idealTendencies = new ConfigDocument
        {
            Definition = new ConfigDefinition("ideal-tendency-kinds", "理想倾向", "人物", "IdealTendencyKinds.json",
                typeof(IdealTendencyKindConfig)),
            Items = [new IdealTendencyKindConfig { Id = 2, Name = "2-比较随意", Offset = 6 }]
        };
        var personData = new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [person]
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([
            new EditorProject { Scope = ConfigScope.Common, ArchivePath = "common.dat", Documents = [idealTendencies] },
            new EditorProject { Scope = ConfigScope.Scenario, ArchivePath = "scenario.dat", Documents = [personData] }
        ]);
        using var document = new ConfigDocumentViewModel(personData, metadata, _ => { },
            referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]));
        var editor = Assert.IsType<PersonBasicEditorViewModel>(document.SpecializedEditor?.Content);

        Assert.Equal("身份", editor.IdentityGroup.Title);
        Assert.Equal(["生平"], editor.LeadingGroups.Select(group => group.Title));
        Assert.DoesNotContain(editor.Groups, group => group.Title.StartsWith("能力", StringComparison.Ordinal));
        var strength = Assert.Single(editor.AbilityColumns, column => column.Label == "武勇");
        Assert.Equal(85, strength.Ability.NumericValue);
        Assert.Equal(5802, strength.Experience.NumericValue);
        Assert.Equal("2-比较随意 （偏差 6）",
            editor.AllFields.Single(field => field.Label == "仕官倾向").SelectedChoice?.Label);

        var life = editor.Groups.Single(group => group.Title == "生平");
        Assert.Equal("出场时 25 岁 · 享年 62 岁", life.Note);
        Assert.False(life.IsWarning);
        editor.AllFields.Single(field => field.Label == "死亡年").NumericValue = 150;
        Assert.True(life.IsWarning);
        Assert.Contains("死亡年早于出生年", life.Note);
        document.UndoCommand.Execute(null);
        Assert.False(life.IsWarning);
    }

    [Fact]
    public void Ranges_FlagOutOfRangeValuesWithoutClamping()
    {
        var person = new PersonConfig { Id = 7, BaseStrength = 160, Ideal = 74, BaseBraveness = 0, PictureIndex = -5 };
        using var document = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [person]
        }, new ReflectionConfigMetadataProvider(), _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]));
        var editor = Assert.IsType<PersonBasicEditorViewModel>(document.SpecializedEditor?.Content);
        PersonBasicFieldViewModel Field(string label) => editor.AllFields.Single(field => field.Label == label);

        Assert.True(Field("武勇").IsOutOfRange);
        Assert.Contains("超出", Field("武勇").RangeTip);
        Assert.Equal(160, person.BaseStrength);
        Assert.False(Field("勇猛度").IsOutOfRange);
        Assert.False(Field("头像序号").IsOutOfRange);
        Assert.Null(Field("头像序号").RangeTip);

        Field("相性").NumericValue = 151;
        Assert.True(Field("相性").IsOutOfRange);
        Field("相性").NumericValue = 150;
        Assert.False(Field("相性").IsOutOfRange);
        Assert.Equal(150, person.Ideal);
        Field("武勇").NumericValue = 95;
        Assert.False(Field("武勇").IsOutOfRange);
    }

    [Fact]
    public void Family_EditsParentsAndListsThroughFamilyDocumentWithUndo()
    {
        var metadata = new ReflectionConfigMetadataProvider();
        var family = new PersonFamilyConfig { Id = 7, BrotherIdsString = "8" };
        using var families = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("person-families", "人物亲属", "人物", "GameScenarios.json",
                typeof(PersonFamilyConfig), ConfigScope.Scenario),
            Items = [family]
        }, metadata, _ => { });
        using var people = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items =
            [
                new PersonConfig { Id = 7, SurName = "关", GivenName = "羽" },
                new PersonConfig { Id = 8, SurName = "张", GivenName = "飞" },
                new PersonConfig { Id = 9, SurName = "刘", GivenName = "备" }
            ]
        }, metadata, _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new PersonBasicEditorProvider()]),
            findRelatedDocument: key => key == "person-families" ? families : null);
        var editor = Assert.IsType<PersonBasicEditorViewModel>(people.SpecializedEditor?.Content);
        var father = editor.FamilyReferenceFields.Single(field => field.Label == "父亲");
        var brothers = editor.FamilyListFields.Single(field => field.Label == "义兄");
        var close = editor.FamilyListFields.Single(field => field.Label == "亲近人物");

        Assert.True(editor.CanEditFamily);
        Assert.Equal(-1, father.SelectedOption?.Id);
        Assert.Equal(["8 张飞"], brothers.Items.Select(item => item.Label));
        father.SelectedOption = father.Options.Single(option => option.Id == 9);
        Assert.Equal(9, family.FatherId);
        close.Candidate = close.Candidates.Single(option => option.Id == 9);
        close.AddCommand.Execute(null);
        Assert.Equal("9", family.CloseIdsString);
        brothers.SelectedItem = brothers.Items.Single();
        brothers.RemoveCommand.Execute(null);
        Assert.Equal("", family.BrotherIdsString);
        Assert.True(families.IsDirty);
        Assert.False(people.IsDirty);
        families.UndoCommand.Execute(null);
        Assert.Equal("8", family.BrotherIdsString);
        Assert.Single(brothers.Items);

        editor.SelectedPerson = editor.People[1];
        Assert.Equal(-1, father.SelectedOption?.Id);
        father.SelectedOption = father.Options.Single(option => option.Id == 9);
        var added = Assert.IsType<PersonFamilyConfig>(families.Records.Single(
            record => record.Item is PersonFamilyConfig item && item.Id == 8).Item);
        Assert.Equal(9, added.FatherId);
        Assert.Equal(-1, added.MotherId);
        families.UndoCommand.Execute(null);
        Assert.DoesNotContain(families.Records, record => record.Item is PersonFamilyConfig { Id: 8 });
        Assert.Equal(-1, father.SelectedOption?.Id);
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

    [Fact]
    public void NewPerson_StartsStudyingReferencesAtGameNoneValue()
    {
        var personData = new ConfigDocument
        {
            Definition = new GameDataConfigRegistry().Find(new ConfigAddress(ConfigScope.Scenario, "persons"))!,
            Items = []
        };
        using var document = new ConfigDocumentViewModel(personData, new ReflectionConfigMetadataProvider(), _ => { });

        document.AddCommand.Execute(null);

        var person = Assert.IsType<PersonConfig>(Assert.Single(personData.Items));
        Assert.Equal(-1, person.StudyingTitleString);
        Assert.Equal(-1, person.StudyingStuntString);
        Assert.Equal(0, person.PersonalTitleString);
    }

    [Fact]
    public void Portrait_BuildsLargeImagePathFromPictureIndexAndRemembersFolder()
    {
        var directory = Directory.CreateTempSubdirectory("zhsan-portraits-").FullName;
        try
        {
            var personData = new ConfigDocument
            {
                Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                    typeof(PersonConfig), ConfigScope.Scenario),
                Items = [new PersonConfig { Id = 7, SurName = "关", GivenName = "羽", PictureIndex = 2 }]
            };
            var store = PersonPortraitDirectoryStore.InMemory();
            using var document = new ConfigDocumentViewModel(personData, new ReflectionConfigMetadataProvider(),
                _ => { }, editorProviderRegistry: new ConfigEditorProviderRegistry([new PersonBasicEditorProvider(store)]));
            var editor = Assert.IsType<PersonBasicEditorViewModel>(document.SpecializedEditor?.Content);

            Assert.Null(editor.PortraitPath);
            Assert.Equal("点击选择头像文件夹", editor.PortraitHint);
            Assert.Equal("2.jpg", editor.PortraitFileName);

            editor.PortraitDirectory = directory;
            Assert.Equal(directory, store.Directory);
            Assert.Equal(Path.Combine(directory, "2.jpg"), editor.PortraitPath);
            Assert.False(editor.HasPortrait);
            Assert.Contains("未找到 2.jpg", editor.PortraitHint);

            editor.AllFields.Single(field => field.Label == "头像序号").NumericValue = 15;
            Assert.Equal(Path.Combine(directory, "15.jpg"), editor.PortraitPath);
            document.UndoCommand.Execute(null);
            Assert.Equal("2.jpg", editor.PortraitFileName);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
