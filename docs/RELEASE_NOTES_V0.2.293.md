# V0.2.293 — 百分比状态：标题是一串英文 / 效果没生效

## 你报的两件事，是两个独立 bug

### ① 状态描述是一串英文

那四个百分比状态是**生成端内置写的类**，不走「自定义状态」那条本地化生成，
所以 `localization/eng/powers.json` 里没有它们的 `title / description / smartDescription` ——
游戏取不到就显示成原始键名（`SPARKLE_PERCENT_DAMAGE_UP_POWER`，看起来像一串英文）。

修法：`LocalizationGen.PowersJson` 里按「这个存档用到哪个就补哪个」写入三条：

```json
"SPARKLE_PERCENT_DAMAGE_UP_POWER.title": "百分比增伤",
"SPARKLE_PERCENT_DAMAGE_UP_POWER.description": "百分比增伤（造成伤害 +N%）：层数就是百分比（50 = 50%），只作用于有来源的攻击。",
"SPARKLE_PERCENT_DAMAGE_UP_POWER.smartDescription": "百分比增伤（造成伤害 +{Amount}%）"
```

（实测输出就是上面这三行。）

### ② 状态挂上了但效果没生效

上一版我在施加那一句里把数值**强行改成字面量**：

```csharp
amt = e.AmountIsXEffective ? "x" : Lit.Dec(e.Amount);   // ← 问题在这
```

你那张牌用的是「数值公式 = 100」，而 `Amount` 字段本身只是 **1**（公式才是真正的值）。
于是生成出来是 `Apply<…>(…, 1m, …)` → 只叠 **1 层 = +1%**，肉眼当然看不出效果。

修法：**不再改数值**，只换类名；数值仍旧走原来的路径（公式 / `DynamicVars` / X）。

> 之所以当初会那么写，是怕 `base.DynamicVars["…"]` 的键对不上（`PowerVar<T>` 用类名推导）。
> V0.2.291 起读档会把 `PowerId` 解析成类名，键名两边已经一致了 —— 所以那个补丁现在既没必要、还有害。

## 验收

* 自检：**4453 PASS / 0 FAIL**（4679 行，无异常）
* 探针（你的 `SparkleMod.json`）：`PowersJson` 输出上面三条中文；施加语句恢复成公式算出的值
* 只出更新包（`-NoBundle`）

## 你要做的

替换到 V0.2.293 → 重新「一键构建 + 安装」。
进游戏后状态栏应显示「百分比增伤」+ 中文说明；打出那张牌后，敌人受到的伤害应按你公式填的百分比变化。
