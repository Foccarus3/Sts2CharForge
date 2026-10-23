# V0.2.233 —— 修遗物「获得金币时」不生效（效果被静默丢掉）

> 来源：你报的「获得金币时，这个判定，无法触发」→「无法正常生效」。

## 1. 触发时机本身是对的，问题在「效果全被丢掉了」

先确认了生成出来的代码没问题 —— 遗物（单时机 / 多时机两条路）和锻造节点图都会生成：

```csharp
// 触发时机：获得金币时
public override async Task AfterGoldGained(Player player) {
    if (player != base.Owner) return;
    …
}
```

本体也确实会调它（`PlayerCmd.GainGold` 末尾那句 `await Hook.AfterGoldGained(runState, player)`，
`Core/Commands/PlayerCmd.cs:169`；战斗奖励 `GoldReward.cs:77`、联机奖励同步、事件、遗物全都走这条路）。

**真正的问题是这行签名里没有 `choiceContext`**（只有 `Player player`）。而下面这些效果**都要它**：
抽牌 / 消耗卡牌 / 丢弃 / 升级 / 变化 / 从三摞拿牌 / 预见 / 造成伤害 / 施加状态 / 生成卡牌 / 卡牌奖励 / 召唤伙伴…

以前生成端遇到这种情况只会写一行注释：

```csharp
{
    // 该触发时机不支持「抽牌」（该触发时机没有 choiceContext，抽牌无法实现），已忽略。
}
```

→ 触发时机其实**触发了**，但里面什么都没做，看起来就是「不生效」。

## 2. 改法：给「获得金币时」按运行时造一个 choiceContext

`AfterGoldGained` **战斗里 / 战斗外都可能触发**，所以不能死写一种上下文，生成的是：

```csharp
MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext;
if (base.Owner.Creature.CombatState is not null)
{
    // 战斗中（自己打出的「获得金币」）→ 和本体 Hook.cs 派发时造的一样，联机时序才对
    ulong __netId = MegaCrit.Sts2.Core.Context.LocalContext.NetId ?? 0UL;
    choiceContext = new HookPlayerChoiceContext(this, base.Owner, __netId, GameActionType.Combat);
}
else
{
    // 战斗外（战斗奖励 / 事件）→ 本体注释点名的那类场景；它本身是空实现，不会卡住
    choiceContext = new BlockingPlayerChoiceContext();
}
```

- **只有真的用得到时才写这几句**：比如「获得金币时 + 获得格挡」这种不需要上下文的效果，生成文本和以前**逐字一致**；
- 单时机、多时机（获得金币 + 回合开始…）两条生成路径都改了，且同一个方法里只造一次；
- 锻造节点图的「获得金币时」仍然会**报错拦住**需要上下文的效果（图里没有可传的上下文）—— 这是原来的行为，没变，
  但错误提示说了「挪到别的触发时机」；用**遗物效果栏**（而不是节点图）就没这个问题了。

## 3. 顺带补的两处

1. 校验器那条「缺 choiceContext → 生成时会被忽略」的提醒，以前只覆盖抽牌 / 伤害 / 施加状态 / 临时增益 / 扣血 这几种，
   现在**按生成端同一份判据**（`CSharpCodeGen.RelicNeedsChoiceContext`）逐条报，不再漏「消耗 / 丢弃 / 升级 / 变化 /
   拿牌 / 预见 / 生成卡牌 / 召唤伙伴…」；「获得金币时」不再误报。
2. 「召唤伙伴」在「获得金币时」上的警告改了理由：不是「钩子拿不到上下文」，而是「**获得金币也可能在战斗外**
   （战斗奖励 / 事件），战斗外没有伙伴可召唤」。

## 4. 验收

- 探针 `sacprobe goldctx`（遗物「获得金币时」+ 格挡 / 抽牌 / 消耗卡牌）：生成结果里三句命令都在
  （`CardPileCmd.Draw(choiceContext, …)`、`CardSelectCmd.FromHand(context: choiceContext, …)`），
  整份工程 `dotnet build` **0 error**；
- 探针 `sacprobe goldmulti`（多时机：获得金币 + 回合开始 + 抽牌）同样 0 error，上下文只造一次；
- 自检新增 7 条（`--uicheck` **4210 PASS / 0 FAIL**）：判据本身、抽牌/消耗真的生成出来、
  上下文按运行时两路、**红线**（只配格挡时不多写）、多时机只造一次、
  校验器「金币不再误报 / 战斗胜利时仍然报」；
- 老存档对拍（新 Core ↔ 已发布 V0.2.232 的 Core）：示例 **113** / WhiteCloak **412** 个文件**只差 `.sln` 的随机 GUID**。
