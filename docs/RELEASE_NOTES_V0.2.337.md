# V0.2.337 — 插件效果种类现在能落在「自定义状态」链上；新增 {target} 占位符

你贴的那段说明（插件只能落地卡牌 / 遗物 / 药水 / 锻造图四条链）—— 这一版把**状态链**接上了。

## 改了什么

1. **`CustomPowerGen.EmitEffect`**：在走白名单 switch 之前先试插件模板（和卡牌链同一套）：

   ```csharp
   if (Plugins.PluginGen.TryEmit(w, e, new PluginCtx(
           Self: "base.Owner", Player: "base.Owner.Player!",
           Foe: CSharpCodeGen.FirstEnemyOf("base.Owner.Player"),
           Choice: "choiceContext", CardSelf: "null", AmountOverride: amt)))
       return;
   ```

   * `{self}` = `base.Owner`（状态挂的 Creature）
   * `{player}` = `base.Owner.Player!`
   * `{foe}` / `{target}` = 可打的第一个敌人（状态链没有「玩家选中的目标」）
   * `{amount}` = 这条效果的数值（含「数值公式」「= 本状态层数」两种口径）
   * `{cardSelf}` = `null`（状态上没有「这张牌」）

2. **校验器放行**：状态链那条 `!PowerTriggers.Supports(e.Kind)` 现在对插件 kind 放行，
   不再报「生成端还没给自定义状态链实现这个效果种类」。

3. **新占位符 `{target}` / `{targetOrFirst}`**：等于当前「指向对象」的表达式（和 `{foe}` 同源）。
   写模板时用 `{target}` 比 `{foe}` 语义清楚，老的 `{foe}` 照旧可用。

## 验收

* 自检：**4552 PASS / 0 FAIL**。
* 只出更新包（`-NoBundle`）。

## 还没做（下一版）

* 占位符继续扩：`{combatState}` / `{cardPlay}` / `{state}` / `{amountFormula}` / `{spawnCardType}`；
* 内置命令 `{transformSelf:类名}`（把「变化卡牌」那套验证过的写法直接展开，插件不用重踩坑）；
* `插件AI提示词.md` 重写成分类长文档（逐链变量表 + 公式 + 对象 + 变化卡牌 + 打包 + 报错对照 + 示例）；
* 「扩展页」：让插件把自定义页挂到**已有的生成器页签**上（不只是「插件」页）。
