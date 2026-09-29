# 剧本与存档编辑

## 使用

1. 先备份游戏数据。CommonData 可以单独打开；剧本/存档依赖对应的 CommonData。在 **剧本/存档** Tab 点击打开，会显示一个面板，分别列出剧本文件和公共配置的路径及用途。CommonData 优先填入当前已打开的配置，其次填入所选剧本最近关联的配置；缺失时在面板中提示选择。文件选择器只会在点击“选择文件”或“更换”时弹出。确认两份文件后点击一次 **加载**，状态栏依次显示正在加载公共配置、剧本。两份档案都读取成功后才更新工作区。
2. 在左侧按分类选择列表，编辑记录；切换 Tab 会恢复各自的活动列表和搜索条件。属性面板中的 `null` 与空集合含义不同，使用显式初始化或设为 `null` 操作。
3. **保存当前配置**只写入当前列表；**保存当前档案**写入当前槽位的修改；**保存全部档案**依次保存两个槽位，并分别报告失败。需要试验修改时使用**保存副本**，原档案和未保存状态不会因此改变。
4. 外部程序修改档案后，编辑器会提示冲突并阻止覆盖；可保存副本以保留当前编辑结果。
5. 剧本/存档打开后自动校验，包括指向 CommonData 的 KindId 等引用。请选用同一游戏版本或模组的 CommonData；ID 存在不代表配置内容一定匹配。打开面板内可以更换 CommonData，加载前会统一处理被替换档案的未保存修改；取消或加载失败会保留原工作区。通过 Common Tab 单独关闭或更换 CommonData 时，仍须先关闭剧本。CommonData 的内容仍可编辑，引用选项会随修改刷新。

成功打开剧本时会在最近记录中保存对应的 CommonData 路径。另存为剧本或当前 CommonData 时同步更新当前剧本的关联；关联信息保存在编辑器设置中，不写入游戏档案。

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
