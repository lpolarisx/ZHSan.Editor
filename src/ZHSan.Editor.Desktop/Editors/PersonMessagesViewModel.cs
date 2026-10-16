using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using GameDatas;
using GameEnums;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// The selected person's lines in CommonData's PersonMessages.json: one record per message kind, each with
/// the lines the game picks from at random. The game keys them by (person, kind), so a kind is offered only
/// once per person. Groups and lines are updated in place, so committing one line never replaces the box the
/// user moved on to.
/// </summary>
public sealed class PersonMessagesViewModel : ObservableObject
{
    public const string DocumentKey = "person-messages";

    private readonly ConfigEditorContext _context;
    private readonly Func<PersonConfig?> _person;
    private IReadOnlyList<PersonChoiceOption> _availableKinds = [];
    private PersonChoiceOption? _newKind;
    private int? _personId;

    public PersonMessagesViewModel(ConfigEditorContext context, Func<PersonConfig?> person)
    {
        _context = context;
        _person = person;
        AddKindCommand = new RelayCommand(AddKind, () => CanAddKind);
    }

    public ObservableCollection<PersonMessageGroupViewModel> Groups { get; } = [];
    public IReadOnlyList<PersonChoiceOption> AvailableKinds => _availableKinds;
    public ICommand AddKindCommand { get; }
    public bool CanEdit => _person() is not null && Document is { HasNullRecords: false };
    public bool IsEmpty => CanEdit && Groups.Count == 0;
    public string Status => GetStatus();

    public PersonChoiceOption? NewKind
    {
        get => _newKind;
        set { if (SetProperty(ref _newKind, value)) ((RelayCommand)AddKindCommand).RaiseCanExecuteChanged(); }
    }

    private bool CanAddKind => CanEdit && NewKind is not null;
    private ConfigDocumentViewModel? Document => _context.FindDocument(DocumentKey);

    public void Refresh()
    {
        var person = _person();
        if (person?.Id != _personId)
        {
            Groups.Clear();
            _personId = person?.Id;
            NewKind = null;
        }

        var records = person is null || Document is not { } document
            ? []
            : document.Records
                .Where(record => record.Item is PersonMessageConfig message && message.PersonId == person.Id)
                .OrderBy(record => (int)((PersonMessageConfig)record.Item).Kind)
                .ToArray();
        for (var index = Groups.Count - 1; index >= 0; index--)
            if (!records.Contains(Groups[index].Record)) Groups.RemoveAt(index);
        for (var index = 0; index < records.Length; index++)
        {
            var existing = Groups.FirstOrDefault(group => ReferenceEquals(group.Record, records[index]));
            if (existing is null) Groups.Insert(index, new PersonMessageGroupViewModel(records[index], this));
            else if (Groups.IndexOf(existing) != index) Groups.Move(Groups.IndexOf(existing), index);
        }
        foreach (var group in Groups) group.Refresh();

        var used = records.Select(record => (int)((PersonMessageConfig)record.Item).Kind).ToHashSet();
        var available = TextMessageKindLabels.Values
            .Where(kind => kind != (int)TextMessageKind.None && !used.Contains(kind))
            .Select(kind => new PersonChoiceOption(kind, KindLabel(kind)))
            .ToArray();
        // Keep the same list when nothing changed so the ComboBox does not re-select and write back.
        if (!available.SequenceEqual(_availableKinds))
        {
            _availableKinds = available;
            OnPropertyChanged(nameof(AvailableKinds));
        }
        if (NewKind is not null && !available.Contains(NewKind)) NewKind = null;

        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Status));
        ((RelayCommand)AddKindCommand).RaiseCanExecuteChanged();
    }

    internal static string KindLabel(int kind) =>
        $"{kind.ToString(CultureInfo.InvariantCulture)} {TextMessageKindLabels.Get(kind)}";

    internal void SetMessages(ConfigRecordViewModel record, List<string> messages, string description)
    {
        if (!CanEdit || Document is not { } document || !document.Records.Contains(record)) return;
        document.SetPropertyValue(record, nameof(PersonMessageConfig.Messages), messages, description);
        Refresh();
    }

    internal void RemoveGroup(PersonMessageGroupViewModel group)
    {
        if (!CanEdit || Document is not { } document) return;
        document.RemoveRecordsWithHistory([group.Record], $"删除人物对话“{TextMessageKindLabels.Get(group.Kind)}”");
        Refresh();
    }

    private void AddKind()
    {
        if (!CanAddKind || _person() is not { } person || Document is not { } document || NewKind is not { } kind) return;
        if (document.CanInitializeEntry) document.InitializeEntryCommand.Execute(null);
        document.AddInitializedRecord(new Dictionary<string, object?>
        {
            [nameof(PersonMessageConfig.PersonId)] = person.Id,
            [nameof(PersonMessageConfig.Kind)] = (TextMessageKind)kind.Id,
            [nameof(PersonMessageConfig.Messages)] = new List<string>()
        }, $"新增人物对话“{TextMessageKindLabels.Get(kind.Id)}”");
        NewKind = null;
        Refresh();
    }

    private string GetStatus()
    {
        if (_person() is null) return "请先选择人物。";
        if (Document is not { } document)
            return "人物对话保存在 CommonData 的 PersonMessages.json，请同时打开 CommonData 后编辑。";
        if (document.HasNullRecords) return "人物消息包含 null 记录，请先在 CommonData 的人物消息配置中处理。";
        var notes = new List<string>
        {
            "人物对话保存在 CommonData 的 PersonMessages.json，所有剧本共用同一人物 ID 的对话；修改后需保存 CommonData（可用“全部保存”）。",
            "游戏会从同类台词中随机选一句，没有台词时使用默认文本。"
        };
        var duplicated = Groups.GroupBy(group => group.Kind).Where(group => group.Count() > 1)
            .Select(group => TextMessageKindLabels.Get(group.Key)).ToArray();
        if (duplicated.Length > 0)
            notes.Add($"“{string.Join("”“", duplicated)}”有重复记录，游戏读取时会出错，请删除多余的一组。");
        return string.Join(" ", notes);
    }
}

public sealed class PersonMessageGroupViewModel : ObservableObject
{
    private readonly PersonMessagesViewModel _owner;
    private string _newLine = string.Empty;

    public PersonMessageGroupViewModel(ConfigRecordViewModel record, PersonMessagesViewModel owner)
    {
        Record = record;
        _owner = owner;
        AddLineCommand = new RelayCommand(AddLine, () => !string.IsNullOrWhiteSpace(NewLine));
        RemoveCommand = new RelayCommand(() => _owner.RemoveGroup(this));
    }

    public ConfigRecordViewModel Record { get; }
    private PersonMessageConfig Message => (PersonMessageConfig)Record.Item;
    public int Kind => (int)Message.Kind;
    public string KindLabel => PersonMessagesViewModel.KindLabel(Kind);
    public ObservableCollection<PersonMessageLineViewModel> Lines { get; } = [];
    public bool IsEmpty => Lines.Count == 0;
    public ICommand AddLineCommand { get; }
    public ICommand RemoveCommand { get; }

    public string NewLine
    {
        get => _newLine;
        set { if (SetProperty(ref _newLine, value ?? string.Empty)) ((RelayCommand)AddLineCommand).RaiseCanExecuteChanged(); }
    }

    internal IReadOnlyList<string> Messages => Message.Messages ?? [];

    // Clearing a line removes it, so the game never picks an empty line.
    internal void SetLine(int index, string text)
    {
        var messages = Messages.ToList();
        if (index < 0 || index >= messages.Count || messages[index] == text) return;
        if (string.IsNullOrWhiteSpace(text))
        {
            messages.RemoveAt(index);
            _owner.SetMessages(Record, messages, $"删除{TextMessageKindLabels.Get(Kind)}台词");
        }
        else
        {
            messages[index] = text;
            _owner.SetMessages(Record, messages, $"修改{TextMessageKindLabels.Get(Kind)}台词");
        }
    }

    internal void RemoveLine(int index)
    {
        var messages = Messages.ToList();
        if (index < 0 || index >= messages.Count) return;
        messages.RemoveAt(index);
        _owner.SetMessages(Record, messages, $"删除{TextMessageKindLabels.Get(Kind)}台词");
    }

    private void AddLine()
    {
        if (string.IsNullOrWhiteSpace(NewLine)) return;
        var text = NewLine.Trim();
        NewLine = string.Empty;
        _owner.SetMessages(Record, [..Messages, text], $"添加{TextMessageKindLabels.Get(Kind)}台词");
    }

    public void Refresh()
    {
        var messages = Messages;
        while (Lines.Count > messages.Count) Lines.RemoveAt(Lines.Count - 1);
        while (Lines.Count < messages.Count) Lines.Add(new PersonMessageLineViewModel(this, Lines.Count));
        foreach (var line in Lines) line.Refresh();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(KindLabel));
    }
}

public sealed class PersonMessageLineViewModel : ObservableObject
{
    private readonly PersonMessageGroupViewModel _group;

    public PersonMessageLineViewModel(PersonMessageGroupViewModel group, int index)
    {
        _group = group;
        Index = index;
        RemoveCommand = new RelayCommand(() => _group.RemoveLine(Index));
    }

    public int Index { get; }
    public ICommand RemoveCommand { get; }

    public string Text
    {
        get => Index < _group.Messages.Count ? _group.Messages[Index] : string.Empty;
        set => _group.SetLine(Index, value ?? string.Empty);
    }

    public void Refresh() => OnPropertyChanged(nameof(Text));
}
