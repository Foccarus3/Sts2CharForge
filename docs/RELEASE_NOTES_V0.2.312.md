# V0.2.312 — 「突破格挡时」的对象：打右钳就给右钳，不再落到左钳

用户原话：

> 突破格挡时的对象有点问题，打帝王蟹，打右钳结果给的左钳，应该是给予有指向的破盾对象

## 真因

本体那个钩子是：

```csharp
AfterBlockBroken(PlayerChoiceContext choiceContext, Creature target, Creature? breaker)
```

`target` = **被打穿格挡的那个单位**（帝王蟹的右钳），本体自己的「手钻」HandDrill 就只对 `target` 动手。

而我们的三条链里，「指定敌人」这一档以前一律退回「可打的第一个敌人」：

```csharp
Creature? other = base.Owner.CombatState.HittableEnemies.FirstOrDefault();   // ← 正好是左钳
```

于是破盾 / 施加状态 / 伤害全都落在**列表里第一个敌人**身上，跟你打的是哪一个无关。

## 改法（三条链都改）

| 链 | 以前 | 现在（突破格挡时） |
|---|---|---|
| 自定义状态 | `Creature? other = base.Owner.CombatState.HittableEnemies.FirstOrDefault();` | `Creature? other = target;` |
| 遗物 | `Creature? foe = base.Owner.Creature.CombatState.HittableEnemies.FirstOrDefault();` | `Creature? foe = target;` |
| 锻造节点图 | `Creature? __fgTarget = base.Owner.CombatState?.HittableEnemies.FirstOrDefault();` | `Creature? __fgTarget = target;` |

实现要点：

* 状态链：`BlockBroken` 那个钩子生成效果时把「当前钩子的目标」传成 `target`（其它时机不变）；
* 遗物链：加了一层「当前钩子自带目标」的作用域（`WithHookFoe` / `EmitRelicFoeDecl`）——
  `EmitPerCreature` 的「指定敌人」和遗物里那两处 `Creature? foe = …` 都会先用它；
* 节点图：`__fgTarget` 的默认值按触发时机定（`plan.Timing.Id == "BlockBroken"` 时换成 `target`）。

**只有「突破格挡时」这一条时机受影响**，别的时机生成的文本逐字不变（拿 Sample / WhiteCloak 两份存档
逐字节比对过：只有 `.sln` 的 GUID 不同）。

## 验收

* 探针（三条链各一份）：
  * `sacprobe blockbroken2`（状态 + 遗物：「突破格挡时」+ 施加虚弱）：分别生成
    `Creature? other = target;` / `Creature? foe = target;`，整份工程 **`dotnet build` 0 错误**；
  * `sacprobe blockbroken3`（锻造图：「突破格挡时」→ 施加虚弱）：生成
    `Creature? __fgTarget = target;` + `TargetOrFirst(__fgTarget, …)`，**`dotnet build` 0 错误**。
* 自检：**4507 PASS / 0 FAIL**（+4 条 V0.2.312 断言：状态链用 target / 遗物链用 target /
  对照「别的时机照旧取第一个敌人」/ 图上默认目标也换成 target）。
* 只出更新包（`-NoBundle`）。

## 说明

「突破格挡时」的**作用对象**填「全体敌人 / 随机敌人」时仍然按字面意思打全体 / 随机——
那次只有 `target` 的格挡被打穿，但你要的是「打全体」就照打。只有默认的「指定敌人」这一档会锁定 `target`。
