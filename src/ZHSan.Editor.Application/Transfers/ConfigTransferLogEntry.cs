namespace ZHSan.Editor.Application.Transfers;

public sealed record ConfigTransferLogEntry(
    DateTimeOffset Timestamp,
    string SourcePath,
    string TargetName,
    string Status,
    string Message,
    string Operation = "导入");
