# V0.3.114 — 效果来回切换时「数值（公式）」栏卡住 / 消失

## 现象

遗物效果栏里按顺序点：

```text
抽牌 → 回复生命 → 抽牌
```

「数值（公式）」那一栏在非第一条效果之间来回切换时会消失，后面几条也一样。

## 原因

`EffectSpec` 上有一个界面标记：

```csharp
IsCardChain
```

它决定「数值（公式）」栏显不显示：

```csharp
ShowAmountFormula => FormulaSupports(Kind) || IsCardChain;
ShowAmountFormulaNormal => ShowAmountFormula && !IsForgeChain;
```

问题有两处：

1. `IsCardChain` 的 setter 只通知了 `ShowAmountBox`，**没有通知 `ShowAmountFormula` / `ShowAmountFormulaNormal`**；
2. 效果对象会在卡牌页 / 锻造页被临时标上 `IsCardChain` / `IsForgeChain`，切到遗物 / 药水 / 状态页时没有清掉。

于是 WPF 那一行的 `Visibility` 会停在上一档：Heal 上显示过的公式栏，切到 Draw 后不刷新；再切回来又反过来。

## 修复

1. `IsCardChain` setter 增加通知：

```csharp
Raise(nameof(ShowAmountFormula));
Raise(nameof(ShowAmountFormulaNormal));
Raise(nameof(AmountFormulaHint));
```

2. `Kind` setter 也补 `ShowAmountFormulaNormal` 通知（换效果种类时立刻刷新）。

3. 遗物 / 药水 / 自定义状态的效果列表加 `SelectionChanged`：

```csharp
fx.IsForgeChain = false;
fx.IsCardChain = false;
```

进入这些「普通链」时先把上一页的标记清掉，公式栏按当前效果种类重新判断。

## 现在的正确行为

- 遗物上的「抽牌」：不显示「数值（公式）」（抽牌张数必须在生成期定下来）；
- 遗物上的「回复生命 / 伤害 / 格挡 / 失去生命 / 施加状态」：显示「数值（公式）」；
- 来回切换不会卡在上一档。

## 注意

这修的是**界面显示**。存档里已经写进去的 `AmountFormula`（例如 `2X`）不会被自动清掉，仍然需要手动清空那条效果。

## 验证

| 项 | 结果 |
|---|---|
| `IsCardChain` 通知 `ShowAmountFormulaNormal` | 通过 |
| 普通链效果选择时重置标记 | 通过 |
| 开发版 `--uicheck` | **4956 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4955 PASS / 0 FAIL** |
