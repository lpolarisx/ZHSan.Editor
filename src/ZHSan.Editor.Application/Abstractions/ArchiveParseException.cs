namespace ZHSan.Editor.Application.Abstractions;

public sealed class ArchiveParseException : Exception
{
    public ArchiveParseException(
        string archivePath,
        string fileName,
        long lineNumber,
        long fieldPosition,
        string? fieldPath,
        string details,
        Exception innerException)
        : base(CreateMessage(archivePath, fileName, lineNumber, fieldPosition, fieldPath, details), innerException)
    {
        ArchivePath = archivePath;
        FileName = fileName;
        LineNumber = lineNumber;
        FieldPosition = fieldPosition;
        FieldPath = fieldPath;
    }

    public string ArchivePath { get; }
    public string FileName { get; }
    public long LineNumber { get; }
    public long FieldPosition { get; }
    public string? FieldPath { get; }

    private static string CreateMessage(
        string archivePath,
        string fileName,
        long lineNumber,
        long fieldPosition,
        string? fieldPath,
        string details)
    {
        var location = string.IsNullOrWhiteSpace(fieldPath)
            ? $"\u7b2c {lineNumber} \u884c\uff0c\u7b2c {fieldPosition} \u5217"
            : $"\u7b2c {lineNumber} \u884c\uff0c\u7b2c {fieldPosition} \u5217\uff0c\u5b57\u6bb5\u201c{fieldPath}\u201d";
        return $"\u65e0\u6cd5\u89e3\u6790\u6863\u6848\u201c{archivePath}\u201d\u4e2d\u7684\u6587\u4ef6\u201c{fileName}\u201d\uff08{location}\uff09\uff1a{details}";
    }
}
