using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Application.Publishing;

public sealed class PublishArchiveService
{
    private readonly IGameDataArchiveRepository _repository;
    private readonly ValidationPreflightService _validationPreflightService;

    public PublishArchiveService(
        IGameDataArchiveRepository repository,
        ValidationPreflightService validationPreflightService)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(validationPreflightService);
        _repository = repository;
        _validationPreflightService = validationPreflightService;
    }

    public async Task<PublishArchiveResult> PublishAsync(
        EditorProject project,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullPath = Path.GetFullPath(destinationPath);
        var preflight = _validationPreflightService.Evaluate(
            project,
            ValidationOperation.Publish,
            cancellationToken);
        if (!preflight.CanProceed)
        {
            return new PublishArchiveResult(
                preflight.Report,
                false,
                fullPath,
                0,
                0);
        }

        await _repository.PublishAsync(project, fullPath, cancellationToken);
        return new PublishArchiveResult(
            preflight.Report,
            true,
            fullPath,
            project.Documents.Count,
            project.Documents.Sum(document => document.Items.Count));
    }
}
