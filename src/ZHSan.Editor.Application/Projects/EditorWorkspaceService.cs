using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Application.Projects;

public sealed class EditorWorkspaceService
{
    private readonly OpenArchiveService _openArchiveService;

    public EditorWorkspaceService(
        OpenArchiveService openArchiveService,
        EditorWorkspace? workspace = null)
    {
        _openArchiveService = openArchiveService;
        Workspace = workspace ?? new EditorWorkspace();
    }

    public EditorWorkspace Workspace { get; }

    public async Task<EditorProject> OpenAsync(
        ConfigScope scope,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        var slot = Workspace.GetSlot(scope);
        slot.BeginOpen();
        try
        {
            var project = await _openArchiveService.OpenAsync(archivePath, scope, cancellationToken);
            slot.CompleteOpen(project);
            return project;
        }
        catch
        {
            slot.CancelOpen();
            throw;
        }
    }

    public void Close(ConfigScope scope)
    {
        var slot = Workspace.GetSlot(scope);
        slot.BeginClose();
        slot.CompleteClose();
    }

    public void SwitchScope(ConfigScope scope) => Workspace.SwitchScope(scope);
}
