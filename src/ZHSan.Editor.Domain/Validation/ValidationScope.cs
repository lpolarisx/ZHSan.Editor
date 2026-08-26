namespace ZHSan.Editor.Domain.Validation;

[Flags]
public enum ValidationScope
{
    None = 0,
    Field = 1,
    Table = 2,
    CrossTable = 4,
    All = Field | Table | CrossTable,
}
