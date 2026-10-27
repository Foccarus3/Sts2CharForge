# V0.2.300 — 锻造页【条件】模块也能选意图了

## 上一版漏在哪

V0.2.299 我补的是**卡牌 / 状态效果条件**那份模板（`App.xaml`）；
锻造页的【条件】模块是**另一套东西** —— 它由 `ForgeCanvasView` 按字段表动态搭出来的，
字段定义在 `ForgeGraphCatalog` 里（`Cond` / `Amount` / `PowerId` / `Keyword` / `Scope` …），
里面压根没有「意图」这个字段，所以图上选不到。

## 修了三处

1. **字段定义**（`ForgeGraphCatalog`）：条件模块新增一栏
   `new ForgeNodeField("Intent", "意图（指定敌人的意图为…用）", ForgeFieldType.Pick, …, null, "Attack")`
   —— 默认 `Attack`。
2. **下拉候选**（`ForgeCanvasView`）：
   `"Intent" => EffectCatalog.Intents.Select(x => new ForgeFieldOption(x.Id, x.Display))`
   —— 就是本体 `IntentType` 那 15 个（攻击 / 增益 / 减益 / 强减益 / 防御 / 逃跑 / 治疗 / 隐藏 / 召唤 / 睡眠 / 眩晕 / 塞状态牌 / 卡牌减益 / 斩杀 / 未知）。
3. **生成端**（`ForgeGraphGen`）：把 `Intent = node.Get("Intent") ?? "Attack"` 带进 `ConditionSpec`，
   这样图上这块条件走的就是和卡牌/遗物完全同一条判据：
   `((…HittableEnemies?.FirstOrDefault())?.Monster?.NextMove?.Intents?.Any(i => i.IntentType == IntentType.X) ?? false)`

## 现在三处 + 锻造都能选

| 位置 | 状态 |
|---|---|
| 遗物页「整只遗物」条件 | ✅（V0.2.288） |
| 卡牌 / 状态效果的条件 | ✅（V0.2.299） |
| **锻造页【条件】模块** | ✅（本版） |

## 验收 / 发布

* 自检：**4454 PASS / 0 FAIL**（4682 行，无异常）
* 只出更新包（`-NoBundle`）
