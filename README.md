# ZHSan 游戏配置编辑器

基于 Avalonia UI 与 C#/.NET 9 的桌面游戏配置编辑器，用于查看、修改、校验、导入和导出 `ZHSan.Data` 定义的游戏数据档案。

## 当前状态

M1 配置发现与项目工作区、M2 档案加载与安全保存、M3 通用编辑器和 M4 编辑历史与效率功能已经完成。编辑器可以通过 `GameDataArchive` 打开 `CommonData.dat`，识别其中 39 项配置，在动态表格中管理记录，并通过属性面板编辑常用标量、枚举、集合和已声明的跨表引用；属性修改、记录增删复制、剪切粘贴和批量修改均可撤销和重做。保存链路支持临时文件、备份、内容指纹和外部变更冲突保护，并已验证现有 JSON 与游戏 API 的双向兼容。现已支持最近项目、关闭与退出前的未保存确认、跨配置全文搜索与结果跳转、窗口和表格筛选状态恢复、常用键盘快捷键，以及必填、数值范围、ID 唯一性和引用目标存在性校验；删除或剪切仍被引用的记录前会展示完整影响范围并要求确认。后续开发以 [docs/TASKS.md](docs/TASKS.md) 为唯一任务台账。

## 环境

- .NET SDK 9.0+
- Avalonia 12.1.1
- Windows、Linux 或 macOS（首要发布目标为 Windows x64）

## 构建与运行

```powershell
dotnet restore ZHSan.Editor.sln
dotnet build ZHSan.Editor.sln
dotnet run --project src/ZHSan.Editor.Desktop/ZHSan.Editor.Desktop.csproj
```

> `ZHSan.Data.dll` 的 `GameDataArchive` 使用 MonoGame 的 `PointJsonConverter`，因此 Infrastructure 显式引用与游戏一致的 `MonoGame.Framework.DesktopGL 3.8.5.1`。

## 文档

- [项目架构](docs/ARCHITECTURE.md)
- [开发任务](docs/TASKS.md)

## 目录

```text
src/
  ZHSan.Editor.Desktop/        Avalonia 视图、ViewModel、控件和程序入口
  ZHSan.Editor.Application/    打开、保存、导入、导出等应用用例
  ZHSan.Editor.Domain/         编辑器领域模型、配置元数据和校验概念
  ZHSan.Editor.Infrastructure/ 数据档案、文件系统、备份及 ZHSan.Data 集成
libs/
  ZHSan.Data.dll               游戏配置类型程序集
docs/
  ARCHITECTURE.md              架构设计与技术决策
  TASKS.md                     可持续维护的任务台账
```
