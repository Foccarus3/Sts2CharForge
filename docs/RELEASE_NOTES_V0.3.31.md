# V0.3.31 — 锻造图的「触发时机」补上「这张牌因预见被丢弃时」

先回答版本问题：**V0.3.27 加的不是锻造图触发时机**。

V0.3.27 加的是**卡牌页 → 效果 → 条件选项 → 卡牌时机**里的
「因预见而被丢弃时」（`EffectCatalog.CardTimings`）。锻造页的【触发时机】是另一张候选表
（`ForgeTimings`），V0.3.27 没动它，所以锻造里看不到。

## V0.3.31 新增

锻造页卡牌布的【触发时机】下拉新增：

```
这张牌因预见被丢弃时
```

它和「这张牌被丢弃时」共用本体的 `AfterCardDiscarded` 钩子，靠工具自己生成的
`__inScryDiscard` 标记区分：

```csharp
if (card != this || base.CombatState is null || !ForgeXxxCombatCounters.__inScryDiscard)
    return;
```

## 同一张图同时有「被丢弃」和「因预见被丢弃」

生成端仍只写**一个** `AfterCardDiscarded`，第二块单独带标记守卫：

```csharp
// ---- 同一个时刻（这张牌因预见被丢弃时）的第 2 块 ----
{
    if (!ForgeXxxCombatCounters.__inScryDiscard) return;
    ...
}
```

不会出现两个同名 override 的 CS0111。

## 生成端配套

- `ForgeTimings` 新增 `CardDiscardedByScry`（Scope=Card，Hook=AfterCardDiscarded）；
- `ForgeGraphGen.GraphHasTiming` 让「只用了这个锻造时机」的存档也生成 `__inScryDiscard`
  （以前只有卡牌时机 / 条件才生成计数器）；
- 嵌进卡牌自己的 `AfterCardDiscarded` 钩子时（没有 card 参数）也会单独写
  `if (!__inScryDiscard) return;`，普通丢弃不会误跑这一块。

## 验证

| 项 | 结果 |
|---|---|
| 锻造图只选新时机 → `AfterCardDiscarded` + `__inScryDiscard` 守卫 | PASS |
| 只选新时机时 `CombatCountersSource` 生成 `__inScryDiscard` | PASS |
| 同一张图同时选两种丢弃 → 仍只有一个 `AfterCardDiscarded`，第二块带守卫 | PASS |
| 开发版 `--uicheck` | **4877 PASS / 0 FAIL**（+3 条 V0.3.31 断言） |
| 真编译探针（带锻造图新时机） | **0 错误** |
| 部署版 `--uicheck` | **4876 PASS / 0 FAIL** |

⚠ 需要重新生成工程才会出现这个新时机。
