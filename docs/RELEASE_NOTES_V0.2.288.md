# V0.2.288 — 新条件「指定敌人的意图为（）」

## 用在哪

**遗物页 → 「条件选项（整只遗物）」**：条件下拉里选「指定敌人的意图为（）」，下面会多出一行 **「意图」** 下拉，15 个选项照本体 `IntentType`：

```
攻击 / 增益 / 减益 / 强减益 / 防御 / 逃跑 / 治疗
隐藏 / 召唤 / 睡眠 / 眩晕 / 塞状态牌 / 卡牌减益 / 斩杀 / 未知
```

## 判据照本体「眼部攻击」

本体 `GoForTheEyes.cs` 只判「是否攻击」：

```csharp
if (cardPlay.Target.Monster.IntendsToAttack) { …上虚弱… }
// 背后是 MonsterModel.IntendsToAttack => NextMove.Intents.Any(i => i.IntentType is Attack or DeathBlow)
```

这里扩成任意意图，生成出来是：

```csharp
((base.Owner.Creature.CombatState?.HittableEnemies?.FirstOrDefault())?.Monster?.NextMove?.Intents?
    .Any(i => i.IntentType == MegaCrit.Sts2.Core.MonsterMoves.Intents.IntentType.Defend) ?? false)
```

要点：
* `NextMove` 可能是 null（沉睡 / 刚出生）→ 全用 `?.` + `?? false`，不会空引用；
* 意图名只认白名单那 15 个，**非法值退回 Attack**（不把脏字符串写进代码，否则 CS0117 整个模组编译不过）；
* 目标取「可打的第一个敌人」（技能 / 遗物没有「选中的目标」这个概念，和其它条件同一口径）。

## 还没做（说清楚，不糊弄）

**卡牌 / 状态页里「每条效果自己的条件」那个编辑器是代码生成的**，这次只接上了**遗物页**那只下拉。
那条路径要加同样的「意图」下拉，还得改它那个动态构建的地方 —— 下次一起补（效果条件本身的判据已经通了，只差界面那一行）。

## 验收

* 自检：**4453 PASS / 0 FAIL**
* 探针：`Attack` / `Defend` / `Buff` / 非法值 四种都生成过，非法值确实退回 `Attack`
* 只出更新包（`-NoBundle`）
