using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Publishing;

public sealed record PublishArchiveResult(
    ValidationReport ValidationReport,
    bool Published,
    string DestinationPath,
    int ConfigCount,
    int ItemCount);
