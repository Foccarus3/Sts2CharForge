# V0.2.322 — 敌人身上的「状态 + 节点图」整只失效（流血不扣血）

用户原话：

> 流血怎么不扣血了

## 真因：状态挂在**敌人**身上时，图上那几句序幕会抛空引用

「自定义状态」这块布上，图上效果体开头会先算几个公共变量（和卡牌那块布同一套）：

```csharp
Player __fgPlayer = base.Owner.Player!;                       // ← 以前
Creature __fgSelf = base.Owner;
decimal __X = ForgeFormulaRuntime.Energy(__fgPlayer) - 0m;    // ← 这一句会炸
decimal __Y = ForgeFormulaRuntime.Stars(__fgPlayer) - 0m;
```

`PowerModel.Owner` 是 **Creature**：挂在玩家身上时 `Owner.Player` 有值，
但你的**流血是挂在敌人身上的**（`血欲 / 和声 / 翠枝 / 热望 / 黄蜂 / 黑天鹅 / 欢愉 / 渴血症` 全都是
给敌人上流血）—— 敌人的 `Owner.Player` 是 **null**，于是 `Energy(null)` 抛 `NullReferenceException`，
**整个钩子在跑效果之前就中断了** → 看起来就是「流血不扣血」。

## 改法

生成时不再直接信 `Owner.Player`，退一层找：

```csharp
Player __fgPlayer = base.Owner.Player
    ?? base.Owner.PetOwner?.Player
    ?? base.Owner.CombatState!.Players.First();
```

* 自己身上的状态 → 还是 `Owner.Player`（行为不变）；
* 敌人身上的状态 → 找到**玩家侧**对象（战斗里一定存在），图上的公式 / X / Y 都能正常算；
* 找不到时才会炸（战斗中不可能）。

## 验收

* 你的存档里 `流血`（挂在敌人身上）现在生成：
  `Player __fgPlayer = base.Owner.Player ?? base.Owner.PetOwner?.Player ?? base.Owner.CombatState!.Players.First();`
  后面 `CreatureCmd.Damage(..., ((decimal)base.Amount), Unblockable|Unpowered|Move, ...)` 照旧能跑到。
* 自检：**4540 PASS / 0 FAIL**（把「状态那块布序幕」那条断言改成新写法）。
* 只出更新包（`-NoBundle`）。

## 顺便说明你的流血配置（不是 bug，但会影响观感）

你图里给「流血」加了两个东西：

1. **条件**：`血色欲望层数 < 1` —— 身上有血色欲望时，流血**故意**不触发；
2. **每触发一次自扣 `-ceil(B/3)` 层** —— 流血会把自己耗掉，层数归零后伤害自然是 0。

如果修好之后还觉得「偶尔不扣血」，先看这两条。
