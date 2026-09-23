using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Projects;

public sealed class SaveWorkspaceService(SaveArchiveService saveArchiveService)
{
    public async Task<WorkspaceSaveResult> SaveAllAsync(
        EditorWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var successes = new List<ArchiveSaveSuccess>();
        var failures = new List<ArchiveSaveFailure>();

        foreach (var scope in new[] { ConfigScope.Common, ConfigScope.Scenario })
        {
            var project = workspace.GetSlot(scope).Project;
            if (project?.HasUnsavedChanges != true)
            {
                continue;
            }

            try
            {
                var report = await saveArchiveService.SaveAllAsync(project, cancellationToken);
                successes.Add(new ArchiveSaveSuccess(scope, project, report));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(new ArchiveSaveFailure(
                    scope,
                    project,
                    exception.GetBaseException().Message,
                    exception is ArchiveConflictException));
            }
        }

        return new WorkspaceSaveResult(successes, failures);
    }
}

public sealed record ArchiveSaveSuccess(
    ConfigScope Scope,
    EditorProject Project,
    ValidationReport ValidationReport);

public sealed record ArchiveSaveFailure(
    ConfigScope Scope,
    EditorProject Project,
    string Message,
    bool IsConflict);

public sealed record WorkspaceSaveResult(
    IReadOnlyList<ArchiveSaveSuccess> Successes,
    IReadOnlyList<ArchiveSaveFailure> Failures)
{
    public bool IsSuccess => Failures.Count == 0;
}
