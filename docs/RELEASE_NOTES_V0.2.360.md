# V0.2.360 — 插件效果能声明**动态变量**：卡面数字跟着实时值走

上一版把插件的 `text` 做出来了，但卡面是**字面数字**（不会跟力量 / 敏捷 / 升级变）。这一版补上真正的动态变量。

## 用法

```json
{ "kind": "PluginHealAndBlock", "var": "Heal",
  "text": "回复 {amount} 点生命并获得 {amount} 点格挡。",
  "emit": "await CreatureCmd.Heal({self}, {amount});" }
```

写一个 `var` 名字，生成端会做三件事：

1. **真的声明这个动态变量**：`CanonicalVars` 里出现 `new DynamicVar("Heal", 数值)`；
2. **`emit` 里的 `{amount}`** → `base.DynamicVars["Heal"].BaseValue`（代码和卡面同一个值）；
3. **`text` 里的 `{amount}`** → `{Heal:diff()}`（卡面显示算出来的数字，升级预览里还有对比值）。

约束：
* `var` 必须是合法标识符，**不能用 `X` / `Y`**（本体保留名）→ 写错在加载时报问题；
* 同一张牌两条效果用同一个 `var` → 自动加后缀（`Heal2`）；
* 不写 `var` = 老行为（不声明变量、字面数字）；遗物 / 药水同样适用。

## 自检

**4670 PASS / 0 FAIL**（+3 条：生成端真的声明了 `DynamicVar("Heal", 5m)` /
`emit` 里的 `{amount}` 变成 `base.DynamicVars["Heal"]` / 卡面写 `{Heal:diff()}`）。
示例插件里 `PluginHealAndBlock` 带 `var: "Heal"`，另加了一条 **不写 var** 的 `PluginHealNoVar` 做对照（卡面走字面数字）。

顺手改了一条老断言：V0.2.359 的「text 字面数字」和「hoverTip」原本都压在同一个示例效果上，
现在主效果声明了 var，就分别改用 `PluginHealNoVar` / `PluginHealAndBlock` 各验一条 —— 口径变了改断言，不改行为。

## B 组只剩

* **apiVersion 迁移机制**（保险性质：将来改格式不至于把老插件判死）；
* 触发时机只能挑本体已有钩子。
