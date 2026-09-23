# ZHSan 游戏配置编辑器

基于 Avalonia UI 与 C#/.NET 9 的桌面游戏配置编辑器，用于查看、修改、校验、导入和导出 `ZHSan.Data` 定义的游戏数据档案。

## 当前状态

M1 配置发现与项目工作区、M2 档案加载与安全保存、M3 通用编辑器、M4 编辑历史与效率功能、M5 校验与引用关系，以及 M6 导入、导出与发布已经完成；M7 已完成专用配置编辑器扩展机制、通用外键引用编辑器、科技树编辑器和设施种类/等级组合编辑器。M9 已支持同时打开 Common 与剧本/存档，分别导航、编辑、搜索、校验和保存。Common 注册 39 项配置，剧本/存档注册 22 个列表；`GameScenarios.json` 暂不编辑。科技树和设施等级提供专用编辑面，其余配置使用通用表格与属性面板。保存链路支持临时文件、备份、内容指纹和外部变更冲突保护。后续开发以 [docs/TASKS.md](docs/TASKS.md) 为唯一任务台账。

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
- [剧本与存档使用说明及兼容验证](docs/SCENARIO.md)
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
