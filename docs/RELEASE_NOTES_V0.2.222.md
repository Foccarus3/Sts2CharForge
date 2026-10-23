# V0.2.222 —— 锻造图的「随机敌人」不再变成「全体敌人 × 命中次数」

> 来源：用户实测报的「对群数和命中次数在锻造里写在一起，导致随机多段效果出现异常」。

## 1. 真因：图上「随机敌人」的候选列表给的是**全体敌人**

`ForgeGraphGen` 里【对象】=「随机敌人」解析出来的 `Foes` 一直是
`ForgeFormulaRuntime.EnemyList(__fgPlayer)`（**全体敌人列表**），于是：

```csharp
// 以前（【效果】= 造成伤害 + 【对象】= 随机敌人 + 命中次数 4）：
foreach (Creature __fgFoe in ForgeFormulaRuntime.EnemyList(__fgPlayer))
    await DamageCmd.Attack(3m).FromCard(this, cardPlay).Targeting(__fgFoe).WithHitCount(4).Execute(choiceContext);
// ↑ 每个敌人各挨 4 下 —— 「对群数（全体）」和「命中次数（4）」被乘在一起
```

「随机敌人 + 命中 1」也一样是**全体各挨 1 下**（根本不是随机一个）；「随机敌人 + 施加状态」
也是给**全体**上（应该是随机一个 / 随机 N 个）。而「卡牌」页那条链一直是对的
（走本体的 `TargetingRandomOpponents`），所以只有锻造图这半边出问题。

## 2. 现在的语义（和「卡牌」页同一口径）

| 图上【效果】 + 【对象 = 随机敌人】 | 生成 |
| --- | --- |
| **造成伤害**（卡牌布） | `DamageCmd.Attack(N).FromCard(…).TargetingRandomOpponents(combatState, allowDuplicates: …)`（+ `.WithHitCount(hits)`）= **随机打 hits 下，每一下随机挑一个敌人**（本体「随机多段」的语义） |
| 造成伤害（遗物 / 药水 / 状态布） | 那边没有 `Attacker`（本体 `TargetingRandomOpponents` 要求先设 Attacker，否则当场抛异常），所以走「**随机挑 N 个**敌人，每个打 hits 下」的循环 |
| 施加状态 / 格挡 / 回血 … 这类按单位施放的 | **随机挑 N 个敌人**（N = 命中次数；不勾「随机目标允许重复选中同一个敌人」= 挑不重复的 N 个） |

也就是说：

- 「命中次数」在**伤害**上 = **多段攻击的段数**（随机敌人时 = 随机打几下）；
- 「命中次数」在**其它按单位施放的效果**上 = **随机挑几个敌人**（和「卡牌」页把这一栏
  同时当「对群数」用的口径一致）；
- 【对象】模块那个「随机目标允许重复选中同一个敌人」现在**真的会传进去**（伤害走
  `allowDuplicates:`，其它效果走「允许重复 = 同一个敌人可以被挑中多次 / 不允许 = 洗牌取前 N 个」）。

【效果】模块上「命中次数」那一栏的 ToolTip 也把这两条写清楚了。

## 3. 验收

- 自检新增 6 条（并**改掉一条锁旧行为的断言** —— 那条原来断言「随机敌人 + 命中 4 = 逐个动手」，
  那正是这次的 bug）：
  - 随机 + 伤害 + 命中 1 → `TargetingRandomOpponents(…).Execute(…)`（不再对全体 foreach）；
  - 随机 + 伤害 + 命中 4 → `TargetingRandomOpponents(…).WithHitCount(4)`；
  - 不勾「允许重复」→ `allowDuplicates: false` 真的传进去了；
  - 随机 + 施加状态 + 命中 3 → `Enumerable.Range(0, 3).Select(_ => RandomEnemy(…))`（随机 3 个）；
  - 不勾「允许重复」→ `.OrderBy(…).Take(2)`（挑 2 个不同的）；
  - 遗物布 + 随机 + 伤害 + 命中 2 → 随机挑 2 个、每个打 2 下，且**不调** `TargetingRandomOpponents`；
  - 顺带把 V0.2.167⑨ 那条「逐个敌人 + 调试日志不越作用域」的用例换成「随机 + 施加状态」
    （随机 + 伤害现在是单次命令、不再是循环了）。
- 探针 `sacprobe randhit`：上面 5 种组合生成出来的工程 `dotnet build --no-incremental` **0 error**；
- 老存档逐文件对拍（新 Core ↔ 已发布 V0.2.221 的 Core，同一份输入各生成一遍）：
  示例 **113** / WhiteCloak **412** 个文件**只差 `.sln` 的 GUID**（这两份存档的图上没配「随机敌人」）；
- `--uicheck`：**4122 PASS / 0 FAIL**。

## 4. 版本

- `V0.2.222`（用这一版重新生成一次工程即可；图上【对象】= 随机敌人 的伤害现在是「随机打 N 下」，
  不再是「每个敌人各挨 N 下」）
