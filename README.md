# ZHSan 游戏配置编辑器

基于 Avalonia UI 与 C#/.NET 9 的桌面游戏配置编辑器，用于查看、修改、校验、导入和导出 `ZHSan.Data` 定义的游戏数据档案。

## 当前状态

基础设施和初始页面已经建立，可以通过 `GameDataArchive` 打开 `CommonData.dat`，识别其中 39 项配置并显示分类、记录数量和字段结构。后续开发以 [docs/TASKS.md](docs/TASKS.md) 为唯一任务台账。

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
