# 剧本与存档编辑

## 使用

1. 先备份游戏数据。在顶部 **Common** Tab 打开 `CommonData.dat`，在 **剧本/存档** Tab 打开剧本或存档 `.dat` 文件。两个档案独立，可只打开其中一个。
2. 在左侧按分类选择列表，编辑记录；切换 Tab 会恢复各自的活动列表和搜索条件。属性面板中的 `null` 与空集合含义不同，使用显式初始化或设为 `null` 操作。
3. **保存当前配置**只写入当前列表；**保存当前档案**写入当前槽位的修改；**保存全部档案**依次保存两个槽位，并分别报告失败。需要试验修改时使用**保存副本**，原档案和未保存状态不会因此改变。
4. 外部程序修改档案后，编辑器会提示冲突并阻止覆盖；可保存副本以保留当前编辑结果。

第一阶段只编辑 22 个剧本/存档列表。`GameScenarios.json` 是单对象，包含地图及全局状态，不在列表导航中；保存列表或副本时保留该条目。不要通过列表编辑器修改它。

## 190FDLM.dat 兼容验证

2026-09-23 在本机游戏样例 `190FDLM.dat` 的临时副本上验证。样例包含以下 22 个列表及未管理的 `GameScenarios.json`，真实档案未加入仓库。

| 列表 | 记录数 | 列表 | 记录数 |
|---|---:|---|---:|
| Architectures | 216 | Biographies | 1375 |
| Captives | 0 | DiplomaticRelations | 1326 |
| Events | 51 | Facilities | 13 |
| Factions | 53 | FirePositions | 0 |
| Informations | 0 | Legions | 0 |
| Militaries | 108 | NoFoodPositions | 0 |
| PersonRelations | 0 | Persons | 1375 |
| Regions | 9 | Routeways | 0 |
| Sections | 52 | States | 19 |
| Treasures | 319 | TroopEvents | 130 |
| Troops | 0 | YearTables | 0 |

兼容测试加载全部列表，修改副本中的人物名称，保存并重开，核对 22 个记录数、修改值、游戏 `GameDataArchive` 的读取结果、其余条目的原始文本及原档案 SHA-256。提交到仓库的生成夹具模拟人物 1375 条、建筑 216 条，并覆盖顶层 `null`、属性 `null`、空列表和列表内 `null` 的往返。

本机有游戏样例时，可运行：

```powershell
$env:ZHSAN_SCENARIO_SAMPLE = '完整路径\190FDLM.dat'
dotnet test tests/ZHSan.Editor.Infrastructure.Tests/ZHSan.Editor.Infrastructure.Tests.csproj --filter FullyQualifiedName~ScenarioArchiveCompatibilityTests
```

未设置环境变量时，真实样例测试不执行，生成夹具测试仍会运行。未来支持 `GameScenarios.json` 时，应为单对象建立独立文档类型和编辑 UI，补充地图与全局状态的校验及往返测试；不能把它当作列表注册。
