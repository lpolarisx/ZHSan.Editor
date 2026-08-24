# 项目架构

## 1. 目标与边界

编辑器负责：

- 打开游戏数据档案并识别其中的 JSON 条目。
- 以表格和属性面板查看、编辑、新增、复制及删除记录。
- 在保存或导出前执行字段、单表和跨表校验。
- 从外部 JSON 导入数据，并在覆盖前展示差异。
- 将有效配置导出到目录或发布包。
- 提供撤销、重做、自动备份和未保存状态提示。

`ZHSan.Data.dll` 只定义游戏配置类型。编辑器的显示名称、控件选择、引用关系、校验和文件名映射均保留在编辑器内部，避免污染游戏基础库。

## 2. 已知数据模型特征

当前 `ZHSan.Data.dll` 面向 `net9.0`，其主要命名空间为 `GameDatas` 和 `GameEnums`。

- 多数配置继承 `BaseConfig`，包含 `Id` 和 `Name`。
- 部分配置不继承 `BaseConfig`，例如 `FacilityConfig` 和 `PersonMessageConfig`。
- 字段类型包括 `int`、`float`、`bool`、`string`、枚举、数组和字符串列表。
- `KindId`、`LevelId`、`PreID`、`PostID` 等字段可能构成跨表引用。
- `InfluencesString`、`ConditionTableString` 等字段实际承载更复杂的结构化内容。
- `GameDataArchive` 使用 ZIP 容器保存多个 JSON 条目，并提供泛型加载、保存、存在检查和删除能力。
- `GameDataArchive` 的 `PointJsonConverter` 引用了 MonoGame，因此编辑器必须携带兼容的 MonoGame 运行时程序集。

因此，第一阶段采用通用元数据驱动编辑器覆盖所有类型，后续再为科技树、动画、条件和影响等配置添加专用编辑器。

## 3. 分层与依赖

```text
Desktop ───────→ Application ───────→ Domain
   │                   ↑
   └──────────→ Infrastructure ──────┘
                         │
                         └──────────→ ZHSan.Data.dll
```

### Domain

不依赖 UI、文件系统或 `ZHSan.Data.dll`，保存编辑器的稳定核心概念：

- `ConfigDefinition`：配置键、显示名、分类、档案条目名和记录类型。
- `ConfigDocument`：当前文件、记录集合和未保存状态。
- `ValidationIssue`：错误级别、记录及字段定位。
- 后续加入属性元数据、引用定义、项目和差异模型。

### Application

组织用户用例，并通过接口访问外部能力：

- 打开、关闭配置项目。
- 加载、保存、全部保存。
- 导入、差异预览、导出和发布。
- 搜索、过滤、撤销与重做。
- 执行字段、单表与跨表校验。

UI 不直接读写 JSON。

### Infrastructure

实现 Application 定义的端口：

- 基于 `GameDataArchive` 的档案条目加载与保存。
- 原子写入、备份和文件恢复。
- `ZHSan.Data.dll` 类型发现与配置注册。
- 编辑器设置和最近项目。
- 文件系统监听及外部变更检测。

### Desktop

包含 Avalonia 的 Views、ViewModels、Controls、Themes 和 Assets。采用 MVVM，主界面布局为：

- 顶部：菜单和常用命令。
- 左侧：配置分类树。
- 中间：可搜索、排序和多选的配置表格。
- 右侧：选中记录的属性面板。
- 底部：校验错误、搜索结果、JSON 预览与日志。

## 4. 配置元数据

配置类型通过显式注册表进入编辑器，不直接把程序集中的所有公开类展示出来：

```csharp
registry.Register<TechniqueConfig>(
    key: "techniques",
    displayName: "技术",
    category: "技术与能力",
    fileName: "Techniques.json");
```

属性编辑器根据元数据选择默认控件：

| 数据类型 | 默认编辑控件 |
|---|---|
| `string` | 单行或多行文本框 |
| `int`、`float` | 数值输入框 |
| `bool` | 复选框 |
| 枚举 | 下拉选择框 |
| 数组、列表 | 集合编辑器 |
| 已声明的外键 ID | 可搜索的引用选择框 |

专用编辑器通过提供者接口覆盖通用控件，而不改变保存格式。

## 5. 数据生命周期

```text
打开 CommonData.dat
  → 识别注册配置
  → JSON 反序列化
  → 建立内存文档和引用索引
  → 编辑并记录撤销操作
  → 校验
  → 写入 .tmp
  → 备份原文件
  → 原子替换正式文件
```

导入策略包括整表替换、按 ID 合并和仅添加新 ID。导入提交前展示新增、修改、删除及冲突数量。

## 6. 校验策略

校验按三个层次执行：

1. 字段校验：必填、类型、数值范围、数组长度。
2. 单表校验：ID 唯一、业务组合规则。
3. 跨表校验：外键存在、删除引用检查、科技关系无环。

错误必须携带配置键、记录 ID 和属性名，供 UI 双击定位。存在错误时默认禁止发布，但允许用户保存尚未完成的工作文件；此策略后续可配置。

## 7. 保存与兼容性

- JSON 格式由 `GameDataArchive` 统一处理，编辑器不再调用 `JsonStore<T>`。
- 默认保留游戏当前使用的属性命名、枚举表示方式和 `PointJsonConverter`。
- 正式写入前必须完成临时文件序列化。
- 每个原文件保留最近一次 `.bak`，后续可扩展时间戳备份历史。
- 编辑器设置写入独立文件，不写入游戏配置目录的数据文件。

## 8. 技术决策

| 项目 | 决策 |
|---|---|
| 目标框架 | 全部项目统一为 `net9.0` |
| UI | Avalonia 12.1.1 |
| 架构 | 分层架构 + MVVM |
| 数据访问 | `ZHSan.Data.GameDataArchive` |
| 数据程序集 | 仓库 `libs/ZHSan.Data.dll` 固定引用 |
| 数据运行时依赖 | `MonoGame.Framework.DesktopGL 3.8.5.1` |
| 首要平台 | Windows x64，同时避免平台专属 UI 代码 |
| 包版本 | 使用 Central Package Management 集中管理 |

## 9. 当前限制与风险

- DLL 更新后需要同步替换 `libs/ZHSan.Data.dll`，并重新执行兼容性构建与序列化测试。
- 复杂的 `*String` 字段语法尚未形成正式规范，专用编辑器实现前需要先确认解析规则。
- 当前已按游戏 `CommonData` 的真实读取清单注册 39 个档案条目；`Colors.json` 的类型不属于 `ZHSan.Data`，暂不在本编辑器范围内。
- 当前已实现档案加载及基础保存设施，数据表格与记录编辑仍未实现。
