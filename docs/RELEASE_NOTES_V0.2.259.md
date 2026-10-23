# V0.2.259 — 「临时护卫」加了没用还越来越多：单层标记类状态不能按层数加减

## 你报的

> 护卫这个 buff 加了临时没有用，而且越来越多。还有一个不知道怎么卡出来的 -2 层荆棘

## ① 临时护卫：为什么「越来越多」

`护卫` 是本体 `GuardedPower`，它的定义（`GuardedPower.cs`）是：

```csharp
public override PowerStackType StackType => PowerStackType.Single;      // 层数恒为 1，界面不显示
public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;  // 每次施加 = 一个新实例
```

* **Single** = 层数没有意义（永远是 1）；
* **Instanced** = 每施加一次就是**另一个实例**。

而工具以前对它做的是「临时 **+1**，回合结束 **-1**」—— 于是每回合都**新挂一个实例**、
撤的时候也撤不干净 ⇒ 你看到的「**越来越多**」。
它本身的效果是「受到**攻击**时伤害 ×0.5」（`ModifyDamageMultiplicative`），不给格挡，
所以看着也「**没用**」。

### 改法（生成端）

遇到 `PowerStackType.Single` 的状态（护卫、壁垒这类单层标记），「临时」改成**存在性语义**：

```csharp
// 挂上：没有才挂一个（不再 +1 叠层）
if (target.GetPower<GuardedPower>() is null)
    await PowerCmd.Apply<GuardedPower>(new ThrowingPlayerChoiceContext(), target, 1m, applier, cardSource, silent: true);

// 到点（回合结束 / 下回合开始）：把自己摘掉，并把这个状态**整个移除**
await PowerCmd.Remove(this);
PowerModel? __back = base.Owner.GetPower<GuardedPower>();
if (__back is not null) await PowerCmd.Remove(__back);
```

* 层数类状态（力量 / 敏捷 / 虚弱 / 荆棘…这些 `Counter`）**行为完全不变**（仍按层数加、到点按层数撤）；
* 进游戏后：每回合只会有一个「临时护卫」，回合结束它和「护卫」一起消失，不会再堆。

## ②「-2 层荆棘」

你这份存档（`CarmenMod.json`）里**一处都没有** `ThornsPower` / 荆棘相关配置（我全文搜过：`ThornsPower` 0 次、
`荆棘` 0 次），所以那一层负荆棘不可能来自这份存档的生成结果 —— 更像是**上一版 mod 的残留**：
以前「临时减益 -2 荆棘」会在回合结束 `Apply(-2)`，把本体状态推到负数（`SetAmount` 不夹 0）。

这一版顺带做了两件事：

* 「临时」的**还原**一律夹到 0（不允许负层数的状态永远不会被撤成 -2）；
* 校验器会提示：「临时减益 × 荆棘」这种组合 —— 该状态**不允许负层数**，目标 0 层时什么都不会发生，
  还原也只会夹到 0。
  （如果你还能复现负荆棘，把当时的 `godot*.log` 给我 —— 那说明是别的来源，得单独查。）

## 验收

* 自检：**4358 PASS / 0 FAIL**（新增 3 条：护卫按存在性挂一个 / 回合结束整个摘掉 / 力量仍按层数撤）。
* 真编译：探针工程里加了「临时护卫 + 临时力量」的卡 → `dotnet build` **0 个错误**。
* 对拍：Sample 113 / WhiteCloak 412 个文件**逐字节相同**（这两份没有单层标记类临时状态）。

## 一并包含（另一批次，第 150 批）

* **参数 = 最大生命 / 当前生命 拼进公式被 `??` 吃掉运算符**（`P*0.04` 变成 `P`，伤害等于满血）：
  `ForgeFormula` 拼标识符时给带 `??` 的片段加括号，`(x?.MaxHp ?? 0) * (0.04m)`。
