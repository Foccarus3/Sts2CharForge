# V0.2.342 — 「打出的牌浮在空中」的兜底：状态互相触发成环时不再把牌卡死

你报的：**破解这张牌打出后浮在空中**。查过了：牌自己的代码没问题，是它打出后拉起的那条**状态连锁**没跑完。

## 为什么会浮空（机制）

本体 `CardModel.Play` 是「**先等 `OnPlay` 返回，再把这牌放进结果堆**」：

```csharp
await branchingPlayerChoiceContext.AssignTaskAndWaitForPauseOrCompletion(task);   // = OnPlay 跑完
…
await CardPileCmd.Add(this, resultLocation.pileType, resultLocation.position, null, skipCardPileVisuals);
```

所以「牌悬在出牌位」= `OnPlay` 还没返回（在等某个 `await`），或者中途抛了异常。
（V0.2.217 / V0.2.243 那两次是「多目标牌读了空 `cardPlay.Target` 抛异常」；破解的生成代码里没有 `cardPlay.Target`，不是那个洞。）

破解会**一次给所有敌人**上「骇入」，于是每个敌人身上那些「给予骇入时」的状态都要各跑一遍；
你存档里有几个还会再绕回来（`自动攻击程序` 造成伤害 → `BeatDown`「造成伤害时给所有敌人骇入」→ 又给骇入），
还可能弹出成串的选牌框（`程序杀毒` 每次上骇入都弹手牌/抽牌堆/弃牌堆三个框）。

## 这一版做了什么

### ① 运行时兜底：触发链 12 层闸门

有启用的自定义状态时，生成一个 `cs/ForgeRecursionGuard.cs`：

```csharp
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.AfterPowerAmountChanged))]
internal static class ForgeRecursionGuardPatch
{
    private const int Limit = 12;
    [System.ThreadStatic] private static int _depth;

    private static bool Prefix()          // 嵌套太深：跳过这一次派发
    {
        if (_depth >= Limit) { MegaCrit.Sts2.Core.Logging.Log.Warn("[ForgeRecursionGuard] 状态层数变化的连锁已经嵌套 …"); return false; }
        _depth++;
        return true;
    }
    private static void Postfix(ref System.Threading.Tasks.Task __result)   // 跑完再把深度减回去
    {
        if (__result is null) return;
        __result = AwaitAndRelease(__result);
    }
}
```

* 所有「状态层数变了」的连锁都从 `Hook.AfterPowerAmountChanged` 走，所以只加这一个闸门就够，生成的业务代码一个字都没动。
* 正常配置的嵌套深度是 2~4 层，12 层足够宽；环一旦形成每轮 +1，最多多跑 12 轮就断掉。
* **怎么验证日志**：打一次破解，日志里如果出现 `[ForgeRecursionGuard] 状态层数变化的连锁已经嵌套 12 层…`，
  就证明确实有环 —— 顺着那行去检查那几个「每当…时」的状态（去掉一边，或给一边加条件）。
* 只有「启用的自定义状态 ≥ 1」才生成这个文件（没有状态时不可能成环，老工程一个文件都不多）。

### ② 校验器：生成前就提示「状态互相触发成环」

新增一条检查（`ProfileValidator.ValidateTriggerCycles`）：A 的触发效果给 B 叠层数、B 的又给 A 叠层数（或自己给自己）
→ **警告**，并写清后果「打出的牌会停在出牌位浮空、回合卡住」。

* **只算会「加」层数的那一边**：回合结束「层数减半 / 衰减」是拿 `-B/2` 这类负数给自己叠，属于正常递减，
  不误报（你存档里 `骇入` / `狱炎` / `少女的秘密` 那三条就是这种，实测不报了）。
* 只覆盖「状态 ↔ 状态」；「伤害 → 给状态 → 又伤害」这种跨事件的环由 ① 兜住。

## 自检

**4588 PASS / 0 FAIL**（+5 条 V0.2.342：环要报 / 负数衰减不误报 / 递归保护源码形状 / 没有状态时不生成 /
真生成一次工程里带着 `cs/ForgeRecursionGuard.cs`）。

## 还是那句

这张牌浮空的**根**是你那套「给骇入 → 造成伤害 → 造成伤害时给骇入」的配置在互相咬；这一版让它在 12 层截断、
并提前告诉你。真要舒服，建议：**把那几个「每当…时」的状态挑一个去掉，或者改成 `数值 > 0` 之类的条件**。
