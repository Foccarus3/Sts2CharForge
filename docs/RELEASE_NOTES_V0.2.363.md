# V0.2.363 — 修「卡牌时机 = 被保留」里选牌会把**回合结束卡死**

你报的现象是「点结束回合后无法正常结束回合」+「什么效果都没触发」——这两条是同一个 bug 的两面。

## 原因（本体源码里能直接看到）

「被保留」挂的是本体的 `AbstractModel.AfterFlush`。而它在哪跑？

`CombatManager.FlushPlayerHand`：

```csharp
await CardPileCmd.Add(cardsToFlush, PileType.Discard);                  // ① 先丢手牌
await Hook.AfterFlush(state, player, playerChoiceContext, …);          // ② 才派发「被保留」
```

关键是这个 `playerChoiceContext` 是 `new HookPlayerChoiceContext(item, …, GameActionType.CombatPlayPhaseOnly)`
—— **只能在出牌阶段执行**的那种。于是：

1. 你的效果里要「选 1 张手牌」→ `CardSelectCmd.FromHand(choiceContext, …)`；
2. 它调 `SignalPlayerChoiceBegun` → 给上下文**排一个新 action**（类型还是 `CombatPlayPhaseOnly`）；
3. 但此刻是回合结束流程，那个 action 永远等不到执行 → `FromHand` 一直 await；
4. `AfterFlush` 不返回 → 丢手牌流程不结束 → **结束回合卡死**，而效果还没跑到（所以"什么都没触发"）。

## 改法

那三个会生成 `AfterFlush` 的地方，在守卫之后各加一句（本体自己遇到同类情况的写法）：

```csharp
// 这一档在回合结束的丢牌流程里跑，传进来的 context 只能在出牌阶段执行 →
// 在里面发起选牌会永远等不到。换成 BlockingPlayerChoiceContext（选牌照弹、当场等玩家选完，不再排 action）。
choiceContext = new BlockingPlayerChoiceContext();
```

三处都改了：

| 路径 | 什么时候走 |
|---|---|
| `CSharpCodeGen.EmitGraphOnlyHookContainers` 的 `AfterFlush` 分支 | 升级前 / 升级后**两块布都**设了「被保留」（你这张资本怪物就是这条） |
| `ForgeGraphGen.TimingEntryGuardLines` 的 `AfterFlush` 守卫 | 图只有**一块**布（图自己写 override） |
| `CSharpCodeGen.EmitCardTimingHooks` 的 `case "Retained"` | 不用锻造图、走**效果列表**的「卡牌时机 = 被保留」 |

`BlockingPlayerChoiceContext` 是本体自己带的类（`MegaCrit.Sts2.Core.GameActions.Multiplayer`），
它注释里写得很清楚：*"For use when we don't care if player choice blocks the task… Enemy turn…"* ——
正是我们这种「在回合结束/敌人回合里要玩家做选择」的场景。

## 自检

**4681 PASS / 0 FAIL**（+3 条：两块布的壳 / 单块布的直接 override / 效果列表那条，三条路径都生成了
`choiceContext = new BlockingPlayerChoiceContext();`）。

## 你那边验证步骤

1. 更新到 **V0.2.363** → 「生成工程」→「构建 / 安装」；
2. 进游戏，把「资本怪物」留在手上，点结束回合：
   - 应该**先弹选牌**（选 1 张手牌变成资本怪物），选完回合正常结束；
   - 金币 +3。

如果还有问题，把新日志发我；同类风险我已经记在待办里：「失去生命时」这个时机也可能在敌人回合里跑，
如果它里面也配了选牌，我会用同一句修掉。
