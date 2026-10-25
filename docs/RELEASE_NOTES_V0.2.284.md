# V0.2.284 — 新增：回合结束自动打出这张牌

## 用法

卡牌页新增一个勾选框（就在「加入卡池」下面）：

```
☑ 回合结束时自动打出这张牌（只在自己回合结束、且它还在手牌里时）
```

勾上之后，**你的回合结束时**，只要这张牌还在手牌里，就自动把它打出一次。

## 生成出来的代码（探针实测）

```csharp
// ===== 回合结束自动打出这张牌（卡页勾了「回合结束自动打出自己」）=====
public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants) {
    if (participants is null || !participants.Contains(base.Owner.Creature)) return;   // 只在自己这一侧的回合结束时
    if (base.Pile is null || base.Pile.Type != PileType.Hand) return;                  // 不在这张牌手牌里就不打（避免自己循环）
    Creature? __autoTarget = base.Owner.Creature.CombatState?.HittableEnemies?.FirstOrDefault();
    await CardCmd.AutoPlay(choiceContext, this, __autoTarget);   // 本体 CardCmd.AutoPlay（CardCmd.cs:51）
}
```

要点：
* 走本体自己的 `CardCmd.AutoPlay`，所以「打出」的判定（费用免除、触发其它钩子、消耗等）和本体一致；
* **只在自己手牌里时才打**（打出去就进弃牌堆 / 被消耗掉的不在手上），所以不会自己无限循环；
* 目标取「可打的第一个敌人」；不需要目标的技能牌传 null 也安全；
* 不勾这个选项的存档：生成结果**逐字不变**（这段代码只在勾了时出现）。

## 已知限制

如果这张牌同时配了「卡牌时机 = 回合结束时」的效果，两者都会抢同一个 `AfterSideTurnEnd` 钩子 ——
这种情况下自动打出那段**先不生成**（避免同一个类里两个同名 override）。这种组合目前请二选一：
要么用「卡牌时机·回合结束时」写效果，要么用这个勾。
