# V0.3.32 — 锻造【条件】能选插件条件；插件输入框的现状与做法

## 1. 锻造【条件】找不到插件条件（已修）

**根因**：`ForgeGraphCatalog.ConditionPicks` 读的是 `EffectCatalog.Conditions`（只有内置条件），
插件条件合并进的是 `EffectCatalog.AllConditions`。另外它是个静态缓存，插件重新加载后也不会更新。

**修法**：

- `ConditionPicks` 改成每次现取 `EffectCatalog.AllConditions.Where(ForCard)`；
- `ForgeNodeField` 增加 `OptionsFactory`，锻造【条件】的 `Cond` 栏每次展开都重新取候选
  （重新加载插件后不用重启生成器）。

实测：示例插件的 `PluginHasBlock` 已出现在锻造条件下拉里，选它生成的是：

```csharp
if ((base.Owner.Creature.Block > 0))
```

不是退回 `if (true)`。

## 2. 插件条件里的「数值 / 状态」输入框

V0.3.32 起，如果插件条件的 `expr` 里用了内置占位符：

- `{amount}` → 注册时自动 `NeedsAmount = true`，卡牌 / 遗物 / 锻造【条件】都会显示「数值 N」；
- `{power}` → 自动 `NeedsPower = true`，显示「状态」下拉。

```json
{
  "id": "PluginHasBlockAtLeast",
  "display": "插件示例：格挡至少 N",
  "expr": "{self}.Block >= {amount}",
  "note": "N 就是条件上的「数值 N」"
}
```

生成时会替换成 `c.Amount` / `c.PowerId` 的值。

## 3. 「编辑器里加不了输入框」——现状

截图里说 `PluginSpec` 没有「设置项」，这只对**旧版**成立。当前版本有三条正规做法：

### A. 插件自己的设置页（推荐做全局设置）

`pages[].fields` 已经支持输入框：

```json
"pages": [
  {
    "id": "settings",
    "title": "我的插件设置",
    "tab": "forge",
    "fields": [
      { "key": "BaseValue", "label": "基础数值", "type": "number", "default": "3" },
      { "key": "UseHardMode", "label": "困难模式", "type": "bool", "default": "0" },
      { "key": "Mode", "label": "模式", "type": "pick", "options": ["Normal|普通", "Hard|困难"] }
    ],
    "emit": "// 这些值可以在 emit 里用 {BaseValue} / {UseHardMode} / {Mode} 取到"
  }
]
```

支持的类型：`text / number / bool / pick / multiline / list / ref`。
值存在 `plugins/<id>/values.json`，生成时按 `emit` 写进工程。

### B. 锻造模块上的输入框（推荐做「每个节点不同参数」）

`forgeModules[].fields` 会直接长在锻造【效果】节点上：

```json
"forgeModules": [
  {
    "kind": "MyPluginEffect",
    "display": "我的插件效果",
    "fields": [
      { "key": "Times", "label": "重复几次", "type": "number", "default": "1" },
      { "key": "Mode", "label": "模式", "type": "pick", "options": ["A|模式 A", "B|模式 B"] }
    ],
    "emit": "for (int i = 0; i < {Times}; i++) { /* {Mode} */ }"
  }
]
```

### C. 编辑器扩展（需要自定义控件时）

`plugin.json` 声明 `editorExtension`，实现 `ICharForgeEditorExtension`，用
`IEditorHost.CreatePanel()` 拿到受控面板，再 `AddTextBox / AddNumber / AddCheck / AddCombo / AddList …`
加输入框。它只能加在**插件自己的页面**里，不能往生成器原有的卡牌 / 锻造节点里塞控件。

## 4. 还做不到的：插件条件上的「任意自定义输入框」

`PluginConditionSpec` 目前只有 `expr / note`，没有 `fields`。所以：

- 条件只需要「数值 N / 状态」→ 用 `{amount}` / `{power}`（V0.3.32 已支持）；
- 条件需要多个自定义参数 → 目前没有逐条件字段。可行绕法：
  1. 把逻辑做成 **forgeModules 效果**（有 `fields`），在效果内部写 `if` 分支；
  2. 用 `pages[].fields` 做全局设置，生成一个静态设置类，条件 `expr` 里直接引用
     `MyPluginSettings.BaseValue`（改值后要重新生成）；
  3. 需要真正的逐条件输入框，就得扩展 `PluginConditionSpec.Fields` + 条件编辑器动态栏位，
     这是下一版可以单独立项的功能，不是现在能靠配置绕过去的。

## 验证

| 项 | 结果 |
|---|---|
| 锻造条件候选包含插件条件 `PluginHasBlock` | PASS |
| 锻造条件选插件条件 → 生成 `base.Owner.Creature.Block > 0` | PASS |
| 开发版 `--uicheck` | **4879 PASS / 0 FAIL**（+2 条 V0.3.32 断言） |
| 部署版 `--uicheck` | **4878 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里没有插件条件节点。
