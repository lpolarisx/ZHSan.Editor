namespace ZHSan.Editor.Application.Abstractions;

public sealed class ConfigImportParseException : Exception
{
    public ConfigImportParseException(
        string sourcePath,
        string entryName,
        long lineNumber,
        long fieldPosition,
        string? fieldPath,
        string detail,
        Exception innerException)
        : base(CreateMessage(sourcePath, entryName, lineNumber, fieldPosition, fieldPath, detail), innerException)
    {
        SourcePath = sourcePath;
        EntryName = entryName;
        LineNumber = lineNumber;
        FieldPosition = fieldPosition;
        FieldPath = fieldPath;
    }

    public string SourcePath { get; }

    public string EntryName { get; }

    public long LineNumber { get; }

    public long FieldPosition { get; }

    public string? FieldPath { get; }

    private static string CreateMessage(
        string sourcePath,
        string entryName,
        long lineNumber,
        long fieldPosition,
        string? fieldPath,
        string detail) =>
        $"导入文件 {Path.GetFileName(sourcePath)} 的 {entryName} 解析失败，" +
        $"位置：第 {lineNumber} 行，第 {fieldPosition} 列" +
        (string.IsNullOrWhiteSpace(fieldPath) ? string.Empty : $"，字段 {fieldPath}") +
        $"。{detail}";
}
