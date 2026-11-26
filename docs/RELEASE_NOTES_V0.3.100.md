# V0.3.100 —— 遗物「每累计花掉 N 点」字段写错位置（CS1519）+ 插件单独页改列表不落盘

> 版本号延续 0.3.x（之前的 V0.4.0 / V0.4.1 按用户要求并回 **V0.3.100**，内容完全一致）。

## ① 遗物「每累计花掉 N 点能量」把字段写进了 `CanonicalVars` 数组里（CS1519/CS1031）

> 用户转来的构建日志：`cs\Relics\LuoRelic4.cs(16,33)`，遗物「事务所多功能电宝」

```csharp
protected override IEnumerable<DynamicVar> CanonicalVars =>
[
    private int __spentBaseTurn = -1;    // ← 数组里不允许声明字段
    private int __spentBaseCombat = -1;

    new PowerVar<VigorPower>(3m),
];
```

数组初始化器里只允许表达式 → 从那一行开始 `CS1519 / CS1031 / CS8124 / CS1026` 一整串。

- **触发条件**：遗物用「每累计花掉 N 点能量 / 额外资源量」（`EnergySpent` / `StarsSpent`）**并且**带动态变量的效果（如 `VigorPower`）。
- **修法**：两个计数基准字段的声明挪到 `CanonicalVars` 属性**之前**（类体里）。

## ② 插件「单独页」的列表：加行 / 改选牌后生成出来的工程不变

> 用户原话：「这个插件单独页里的列表，里面我添加一行或改选牌，底下工程不会变」（插件：`召唤物布局`）

**结论：工具的问题，插件没问题。** 把他的 `plugin.json` 当插件加载后跑生成，加行 / 改值都能正确反映；
问题在「界面 → 磁盘 → 生成」这条链上：

1. **值编辑从不落盘**：子栏位控件（选牌下拉 / 数字 / 勾选 / 文本 / 子表）只调 `onChanged()`（刷新预览），没有 SaveRows；
2. **`SaveRows` 写的是缓存表，不是界面上那份列表**：打开存档会 `ClearPageCache()`，界面手里的 `rows` 就变成旧对象
   → 界面改旧列表、磁盘写新缓存 → **加行也丢**；
3. **切存档只重建「当前选中的那个插件」的页**，其它页仍绑旧存档的列表。

修法：

- 新增 `PluginRegistry.SetRows(p, page, listField, rows)`：落盘前把**界面正在编辑的那份列表**接管进缓存；
- 列表编辑器加 `Persist()`（SetRows + SaveRows），「加 / 删 / ↑↓」都走它；
- 子栏位编辑统一走 `Touch()` → **350ms 防抖**后 SetRows + SaveRows；
- `SyncPluginProfileKey` 记住 `_lastShownPluginSpec`，切存档时一定重建那一页。

## 验收

- 探针 `_probe/relicspent`：`EnergySpent` / `StarsSpent` 各 **4/4 PASS**（数组里没有 `private` 字段 + 字段在类体里）
- 探针 `_probe/plugrows` **5/5 PASS**（含“不接管就落盘 → 磁盘没有新行”的复现断言）
- `dotnet build` 全解决方案 **0 error**
- `--uicheck`：**4949 PASS / 0 FAIL**

## 版本

- `V0.3.100`
