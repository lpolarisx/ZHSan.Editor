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
        CancellationToken cancellationToken = default,
        string? commonArchivePath = null,
        Action<ConfigScope>? loadingScopeChanged = null)
    {
        if (scope == ConfigScope.Common)
        {
            EnsureCommonCanChange();
        }

        var common = Workspace.Common;
        var loadCommon = scope == ConfigScope.Scenario && (!common.IsOpen ||
            commonArchivePath is not null && !string.Equals(
                Path.GetFullPath(commonArchivePath), Path.GetFullPath(common.ArchivePath!),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        if (scope == ConfigScope.Scenario && common.IsTransitioning)
        {
            throw new InvalidOperationException("CommonData 正在加载或关闭，请稍后再打开剧本 / 存档。");
        }

        if (loadCommon && string.IsNullOrWhiteSpace(commonArchivePath))
        {
            throw new InvalidOperationException("打开剧本 / 存档前，请先选择并加载对应的 CommonData。");
        }

        var slot = Workspace.GetSlot(scope);
        slot.BeginOpen();
        try
        {
            EditorProject? commonProject = null;
            if (loadCommon)
            {
                common.BeginOpen();
                loadingScopeChanged?.Invoke(ConfigScope.Common);
                commonProject = await _openArchiveService.OpenAsync(
                    commonArchivePath!, ConfigScope.Common, cancellationToken);
            }

            loadingScopeChanged?.Invoke(scope);
            var project = await _openArchiveService.OpenAsync(archivePath, scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (commonProject is not null)
            {
                common.CompleteOpen(commonProject);
            }

            slot.CompleteOpen(project);
            return project;
        }
        catch
        {
            slot.CancelOpen();
            if (loadCommon && common.IsTransitioning)
            {
                common.CancelOpen();
            }

            throw;
        }
    }

    public void Close(ConfigScope scope)
    {
        if (scope == ConfigScope.Common)
        {
            EnsureCommonCanChange();
        }

        var slot = Workspace.GetSlot(scope);
        slot.BeginClose();
        slot.CompleteClose();
    }

    public void SwitchScope(ConfigScope scope) => Workspace.SwitchScope(scope);

    private void EnsureCommonCanChange()
    {
        if (Workspace.Scenario.IsOpen || Workspace.Scenario.IsTransitioning)
        {
            throw new InvalidOperationException("请先关闭剧本 / 存档，再关闭或更换 CommonData。");
        }
    }
}
