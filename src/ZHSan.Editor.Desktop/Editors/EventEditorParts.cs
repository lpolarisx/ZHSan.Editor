using System.Collections.ObjectModel;
using System.Windows.Input;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// The pair lists used by events: "a b a b ", split on any whitespace the way the game reads them and written
/// back with a trailing space the way the game saves them.
/// </summary>
internal static class EventPairs
{
    private static string[] Split(string? text) =>
        (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static bool TryParse(string? text, out List<(int First, int Second)> pairs)
    {
        pairs = [];
        var tokens = Split(text);
        if (tokens.Length % 2 != 0) return false;
        for (var index = 0; index < tokens.Length; index += 2)
        {
            if (!int.TryParse(tokens[index], out var first) || !int.TryParse(tokens[index + 1], out var second))
                return false;
            pairs.Add((first, second));
        }
        return true;
    }

    public static bool TryParseDialog(string? text, out List<(int Speaker, string Text)> lines)
    {
        lines = [];
        var tokens = Split(text);
        if (tokens.Length % 2 != 0) return false;
        for (var index = 0; index < tokens.Length; index += 2)
        {
            if (!int.TryParse(tokens[index], out var speaker)) return false;
            lines.Add((speaker, tokens[index + 1]));
        }
        return true;
    }

    public static string Format(IEnumerable<(int First, int Second)> pairs) =>
        string.Concat(pairs.Select(pair => $"{pair.First} {pair.Second} "));

    public static string FormatDialog(IEnumerable<(int Speaker, string Text)> lines) =>
        string.Concat(lines.Select(line => $"{line.Speaker} {line.Text} "));

    // The game splits dialogue on whitespace, so a line cannot contain any.
    public static string RemoveWhitespace(string? text) =>
        new((text ?? "").Where(character => !char.IsWhiteSpace(character)).ToArray());
}

/// <summary>
/// Presents the values one key holds in a "key value key value" string as a plain ID list, so the usual chip
/// list can edit, for example, the persons or conditions of person slot %k.
/// </summary>
internal sealed class EventSlotOwner(IIdChipListOwner editor, int key) : IIdChipListOwner
{
    public bool HasSelection => editor.HasSelection;

    // An unreadable pair list is shown as an unreadable ID list, which disables editing with a message.
    public object? GetValue(string propertyName) =>
        EventPairs.TryParse(editor.GetValue(propertyName) as string, out var pairs)
            ? ConfigStructuredStringCodec.FormatIds(pairs.Where(pair => pair.First == key).Select(pair => pair.Second))
            : "格式有误";

    public void SetValue(string propertyName, object value, string description)
    {
        if (!EventPairs.TryParse(editor.GetValue(propertyName) as string, out var pairs) ||
            ConfigStructuredStringCodec.ParseIds(value as string) is not { IsValid: true } ids) return;
        var position = pairs.FindIndex(pair => pair.First == key);
        var result = pairs.Where(pair => pair.First != key).ToList();
        var insertAt = position < 0 ? result.Count : pairs.Take(position).Count(pair => pair.First != key);
        result.InsertRange(insertAt, ids.Items.Select(id => (key, id)));
        editor.SetValue(propertyName, EventPairs.Format(result), description);
    }

    public IReadOnlyList<PersonChoiceOption> GetChipCandidates(string targetKey) => editor.GetChipCandidates(targetKey);
    public string GetChipLabel(string targetKey, int id) => editor.GetChipLabel(targetKey, id);
}

/// <summary>Person slot %k of an event: who can fill it, what they must satisfy, and what happens to them.</summary>
public sealed class EventSlotViewModel : ObservableObject
{
    public EventSlotViewModel(int key, IIdChipListOwner editor, ICommand removeCommand)
    {
        Key = key;
        RemoveCommand = removeCommand;
        var owner = new EventSlotOwner(editor, key);
        Persons = new PersonIdChipListViewModel("候选武将", "PersonString", EventEditorViewModel.SlotPersonsKey, owner);
        Conditions = new PersonIdChipListViewModel("武将条件", "PersonCondString", "conditions", owner);
        Effects = new PersonIdChipListViewModel("效果", "EffectString", "architecture-effects", owner);
        YesEffects = new PersonIdChipListViewModel("选是效果", "YesEffectString", "architecture-effects", owner);
        NoEffects = new PersonIdChipListViewModel("选否效果", "NoEffectString", "architecture-effects", owner);
    }

    public int Key { get; }
    public string Title => $"武将 %{Key}";
    public ICommand RemoveCommand { get; }
    public PersonIdChipListViewModel Persons { get; }
    public PersonIdChipListViewModel Conditions { get; }
    public PersonIdChipListViewModel Effects { get; }
    public PersonIdChipListViewModel YesEffects { get; }
    public PersonIdChipListViewModel NoEffects { get; }
    public IReadOnlyList<PersonIdChipListViewModel> Lists => [Persons, Conditions, Effects, YesEffects, NoEffects];

    public void Refresh()
    {
        foreach (var list in Lists) list.Refresh();
    }
}

/// <summary>An ordered list of "speaker text" dialogue lines, edited in place so typing is not interrupted.</summary>
public sealed class EventDialogListViewModel : ObservableObject
{
    private readonly IIdChipListOwner _owner;
    private readonly string _propertyName;
    private readonly Func<IReadOnlyList<PersonChoiceOption>> _speakers;
    private List<(int Speaker, string Text)> _lines = [];
    private IReadOnlyList<PersonChoiceOption> _speakerOptions = [];
    private PersonChoiceOption? _newSpeaker;
    private string _newText = string.Empty;
    private string _notice = string.Empty;
    private bool _canEdit;

    public EventDialogListViewModel(IIdChipListOwner owner, string propertyName, string label, string hint,
        Func<IReadOnlyList<PersonChoiceOption>> speakers)
    {
        _owner = owner;
        _propertyName = propertyName;
        _speakers = speakers;
        Label = label;
        Hint = hint;
        AddCommand = new RelayCommand(Add, () => CanEdit && NewSpeaker is not null &&
            EventPairs.RemoveWhitespace(NewText).Length > 0);
    }

    public string Label { get; }
    public string Hint { get; }
    public ObservableCollection<EventDialogLineViewModel> Lines { get; } = [];
    public bool IsEmpty => Lines.Count == 0;
    public IReadOnlyList<PersonChoiceOption> SpeakerOptions => _speakerOptions;
    public ICommand AddCommand { get; }
    internal bool Refreshing { get; private set; }
    public bool CanEdit => _canEdit;
    public string Status => _owner.HasSelection && !_canEdit
        ? "格式有误：应为“说话人 台词”成对排列，请先在通用编辑器中修正。"
        : "";
    public string Notice
    {
        get => _notice;
        private set { if (SetProperty(ref _notice, value)) OnPropertyChanged(nameof(HasNotice)); }
    }
    public bool HasNotice => Notice.Length > 0;

    public PersonChoiceOption? NewSpeaker
    {
        get => _newSpeaker;
        set { if (SetProperty(ref _newSpeaker, value)) ((RelayCommand)AddCommand).RaiseCanExecuteChanged(); }
    }

    public string NewText
    {
        get => _newText;
        set { if (SetProperty(ref _newText, value ?? string.Empty)) ((RelayCommand)AddCommand).RaiseCanExecuteChanged(); }
    }

    internal PersonChoiceOption SpeakerOption(int id) =>
        _speakerOptions.FirstOrDefault(option => option.Id == id) ?? new PersonChoiceOption(id, $"{id} （未定义）");

    private void Add()
    {
        if (NewSpeaker is not { } speaker) return;
        var text = Clean(NewText);
        if (text.Length == 0) return;
        Write([.._lines, (speaker.Id, text)], $"添加{Label}");
        NewText = string.Empty;
    }

    internal void SetSpeaker(int index, int speaker)
    {
        if (Refreshing || !_canEdit || index >= _lines.Count || _lines[index].Speaker == speaker) return;
        var lines = _lines.ToList();
        lines[index] = (speaker, lines[index].Text);
        Write(lines, $"修改{Label}说话人");
    }

    // Clearing a line removes it.
    internal void SetText(int index, string? value)
    {
        if (Refreshing || !_canEdit || index >= _lines.Count) return;
        var text = Clean(value);
        if (text == _lines[index].Text) return;
        var lines = _lines.ToList();
        if (text.Length == 0) lines.RemoveAt(index);
        else lines[index] = (lines[index].Speaker, text);
        Write(lines, text.Length == 0 ? $"删除{Label}" : $"修改{Label}");
    }

    internal void Move(int index, int offset)
    {
        var target = index + offset;
        if (!_canEdit || index >= _lines.Count || target < 0 || target >= _lines.Count) return;
        var lines = _lines.ToList();
        (lines[index], lines[target]) = (lines[target], lines[index]);
        Write(lines, $"调整{Label}顺序");
    }

    internal void Remove(int index)
    {
        if (!_canEdit || index >= _lines.Count) return;
        var lines = _lines.ToList();
        lines.RemoveAt(index);
        Write(lines, $"删除{Label}");
    }

    internal int Count => _lines.Count;

    private string Clean(string? value)
    {
        var text = EventPairs.RemoveWhitespace(value);
        Notice = (value ?? "").Trim().Any(char.IsWhiteSpace)
            ? "游戏以空格分隔台词，台词中的空格已去掉。"
            : string.Empty;
        return text;
    }

    private void Write(List<(int Speaker, string Text)> lines, string description)
    {
        _owner.SetValue(_propertyName, EventPairs.FormatDialog(lines), description);
        Refresh();
    }

    public void Refresh()
    {
        Refreshing = true;
        try
        {
            _canEdit = _owner.HasSelection && EventPairs.TryParseDialog(_owner.GetValue(_propertyName) as string, out _lines);
            if (!_canEdit) _lines = [];
            var options = _speakers().ToList();
            foreach (var speaker in _lines.Select(line => line.Speaker).Distinct())
                if (options.All(option => option.Id != speaker))
                    options.Add(new PersonChoiceOption(speaker, $"{speaker} （未定义）"));
            if (!options.SequenceEqual(_speakerOptions))
            {
                _speakerOptions = options;
                OnPropertyChanged(nameof(SpeakerOptions));
            }
            if (Lines.Count != _lines.Count)
            {
                Lines.Clear();
                for (var index = 0; index < _lines.Count; index++) Lines.Add(new EventDialogLineViewModel(index, this));
            }
            foreach (var line in Lines) line.Refresh(_lines[line.Index].Speaker, _lines[line.Index].Text);
            if (NewSpeaker is { } chosen && options.All(option => option.Id != chosen.Id)) NewSpeaker = null;
        }
        finally
        {
            Refreshing = false;
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(Status));
        ((RelayCommand)AddCommand).RaiseCanExecuteChanged();
    }
}

public sealed class EventDialogLineViewModel : ObservableObject
{
    private readonly EventDialogListViewModel _owner;
    private int _speaker;
    private string _text = string.Empty;

    public EventDialogLineViewModel(int index, EventDialogListViewModel owner)
    {
        Index = index;
        _owner = owner;
        MoveUpCommand = new RelayCommand(() => owner.Move(Index, -1), () => Index > 0);
        MoveDownCommand = new RelayCommand(() => owner.Move(Index, 1), () => Index < owner.Count - 1);
        RemoveCommand = new RelayCommand(() => owner.Remove(Index));
    }

    public int Index { get; }
    public string Number => $"{Index + 1}.";
    public IReadOnlyList<PersonChoiceOption> SpeakerOptions => _owner.SpeakerOptions;
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand RemoveCommand { get; }

    public PersonChoiceOption? Speaker
    {
        get => _owner.SpeakerOption(_speaker);
        set { if (value is not null) _owner.SetSpeaker(Index, value.Id); }
    }

    public string Text
    {
        get => _text;
        set => _owner.SetText(Index, value);
    }

    internal void Refresh(int speaker, string text)
    {
        _speaker = speaker;
        _text = text;
        OnPropertyChanged(nameof(SpeakerOptions));
        OnPropertyChanged(nameof(Speaker));
        OnPropertyChanged(nameof(Text));
        ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();
    }
}

/// <summary>A "first second first second" list shown as rows, such as target persons or area effects.</summary>
public sealed class EventPairListViewModel : ObservableObject
{
    private readonly IIdChipListOwner _owner;
    private readonly string _propertyName;
    private readonly Func<IReadOnlyList<PersonChoiceOption>> _firstOptions;
    private readonly Func<IReadOnlyList<PersonChoiceOption>> _secondOptions;
    private IReadOnlyList<PersonChoiceOption> _firstChoices = [];
    private IReadOnlyList<PersonChoiceOption> _secondChoices = [];
    private List<(int First, int Second)> _pairs = [];
    private PersonChoiceOption? _firstCandidate;
    private PersonChoiceOption? _secondCandidate;
    private string _firstText = string.Empty;
    private string _secondText = string.Empty;
    private bool _canEdit;

    public EventPairListViewModel(IIdChipListOwner owner, string propertyName, string label, string hint,
        string firstHeader, Func<IReadOnlyList<PersonChoiceOption>> firstOptions, bool firstSearchable,
        string secondHeader, Func<IReadOnlyList<PersonChoiceOption>> secondOptions, bool secondSearchable)
    {
        _owner = owner;
        _propertyName = propertyName;
        _firstOptions = firstOptions;
        _secondOptions = secondOptions;
        Label = label;
        Hint = hint;
        FirstHeader = firstHeader;
        SecondHeader = secondHeader;
        FirstSearchable = firstSearchable;
        SecondSearchable = secondSearchable;
        AddCommand = new RelayCommand(Add, () => _canEdit && FirstCandidate is not null && SecondCandidate is not null);
    }

    public string Label { get; }
    public string Hint { get; }
    public string FirstHeader { get; }
    public string SecondHeader { get; }
    public bool FirstSearchable { get; }
    public bool SecondSearchable { get; }
    public string FirstPlaceholder => $"选择{FirstHeader}";
    public string SecondPlaceholder => $"选择{SecondHeader}";
    public ObservableCollection<EventPairRowViewModel> Rows { get; } = [];
    public bool IsEmpty => Rows.Count == 0;
    public IReadOnlyList<PersonChoiceOption> FirstOptions => _firstChoices;
    public IReadOnlyList<PersonChoiceOption> SecondOptions => _secondChoices;
    public ICommand AddCommand { get; }
    public bool CanEdit => _canEdit;
    public string Status => _owner.HasSelection && !_canEdit
        ? $"格式有误：应为“{FirstHeader} {SecondHeader}”成对排列，请先在通用编辑器中修正。"
        : "";

    public PersonChoiceOption? FirstCandidate
    {
        get => _firstCandidate;
        set { if (SetProperty(ref _firstCandidate, value)) ((RelayCommand)AddCommand).RaiseCanExecuteChanged(); }
    }

    public PersonChoiceOption? SecondCandidate
    {
        get => _secondCandidate;
        set { if (SetProperty(ref _secondCandidate, value)) ((RelayCommand)AddCommand).RaiseCanExecuteChanged(); }
    }

    public string FirstText
    {
        get => _firstText;
        set => SetProperty(ref _firstText, value ?? string.Empty);
    }

    public string SecondText
    {
        get => _secondText;
        set => SetProperty(ref _secondText, value ?? string.Empty);
    }

    private static string OptionLabel(IReadOnlyList<PersonChoiceOption> options, int id) =>
        options.FirstOrDefault(option => option.Id == id)?.Label ?? $"{id} （未收录）";

    private void Add()
    {
        if (FirstCandidate is not { } first || SecondCandidate is not { } second) return;
        Write([.._pairs, (first.Id, second.Id)], $"添加{Label}");
        FirstCandidate = null;
        SecondCandidate = null;
        FirstText = string.Empty;
        SecondText = string.Empty;
    }

    private void Remove(int index)
    {
        if (!_canEdit || index >= _pairs.Count) return;
        var pairs = _pairs.ToList();
        pairs.RemoveAt(index);
        Write(pairs, $"移除{Label}");
    }

    private void Write(List<(int First, int Second)> pairs, string description)
    {
        _owner.SetValue(_propertyName, EventPairs.Format(pairs), description);
        Refresh();
    }

    public void Refresh()
    {
        _canEdit = _owner.HasSelection && EventPairs.TryParse(_owner.GetValue(_propertyName) as string, out _pairs);
        if (!_canEdit) _pairs = [];
        var first = _firstOptions();
        if (!first.SequenceEqual(_firstChoices))
        {
            _firstChoices = first;
            OnPropertyChanged(nameof(FirstOptions));
        }
        var second = _secondOptions();
        if (!second.SequenceEqual(_secondChoices))
        {
            _secondChoices = second;
            OnPropertyChanged(nameof(SecondOptions));
        }
        Rows.Clear();
        for (var index = 0; index < _pairs.Count; index++)
        {
            var row = index;
            Rows.Add(new EventPairRowViewModel(OptionLabel(_firstChoices, _pairs[index].First),
                OptionLabel(_secondChoices, _pairs[index].Second), new RelayCommand(() => Remove(row), () => _canEdit)));
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(Status));
        ((RelayCommand)AddCommand).RaiseCanExecuteChanged();
    }
}

public sealed record EventPairRowViewModel(string First, string Second, ICommand RemoveCommand);
