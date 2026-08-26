using ZHSan.Editor.Application.Settings;

namespace ZHSan.Editor.Application.Abstractions;

public interface IEditorSettingsStore
{
    EditorSettings Load();

    void Save(EditorSettings settings);
}
