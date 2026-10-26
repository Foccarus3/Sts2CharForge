# V0.2.296 — 新效果「升级本场战斗中临时增加的牌」

## 用法

效果种类里多了一项：

```
升级本场战斗中临时增加的牌     （单位：张）
```

数值 = **最多升几张**（0 / 留空 = 不限），卡牌 / 遗物 / 药水三条链都能用。

## 判据（用本体现成的流水，不自己记状态）

本体 `CombatHistory` 里就有一条「本场战斗生成了哪张牌」的记录：

```csharp
public class CardGeneratedEntry : CombatHistoryEntry { public CardModel Card { get; } public Player? Creator { get; } }
// CombatHistory.CardGenerated(combatState, card, creator) 时写入
```

生成出来的代码（探针实测）：

```csharp
// 升级本场战斗中临时增加的牌（本体 History.CardGenerated 的流水）
int __upgGen = 0;
foreach (var __gen in MegaCrit.Sts2.Core.Combat.CombatManager.Instance.History.Entries
             .OfType<MegaCrit.Sts2.Core.Combat.History.Entries.CardGeneratedEntry>().ToList())
{
    CardModel? __genCard = __gen.Card;
    if (__genCard is null || __genCard.IsUpgraded || !__genCard.IsUpgradable) continue;   // 升过的、不能升的跳过
    if (__upgGen >= 3) break;   // 最多升 3 张
    CardCmd.Upgrade(__genCard);
    __upgGen++;
}
```

要点：
* 用 `IsUpgradable` / `IsUpgraded` 过滤（本体 `CardModel.cs:784/786`），**不会**对已升级的牌重复动手；
* `.ToList()` 先固化一遍，避免升级过程中修改历史集合；
* 已经打出 / 消耗掉的临时牌仍在流水里，但它们的 `IsUpgradable` 一般还是 true —— 升级不影响当前战斗里已经用掉的牌（只是牌组里那张会变）。**如果你要「只升手牌里的」，说一声，我加一个勾选项。**

## 关于第二件事（公式数值实时显示）

这版**还没做**。可行性已经确认：工具里现成有 `ForgeFormula.TryEvalConst(root, constOf, out value, out error)`
（`ForgeFormula.cs:475`），可以把「只含固定参数」的公式直接算出一个数字，含运行时变量（X / 敌人层数 / 手牌数）的则标注出来。

但要真正落到界面上，得改五处输入框的绑定（卡牌 / 遗物 / 药水 / 自定义状态 / 锻造三处「数值 / 次数」栏），
并且要给 `EffectSpec` 加一个能读到存档参数的求值入口 —— 这是一次成规模的界面改动，
我不想在同一个包里塞半截（上次「意图下拉」就是这样只做了一半，让你白等一版）。

建议下一版专门做这件事，我会一次性把五处都接上，并让「写错公式」当场标红。

## 验收 / 发布

* 自检：**4454 PASS / 0 FAIL**（4680 行，无异常）
* 探针：效果生成出上面那段（含「最多升 N 张」与可升级过滤）
* 只出更新包（`-NoBundle`）
