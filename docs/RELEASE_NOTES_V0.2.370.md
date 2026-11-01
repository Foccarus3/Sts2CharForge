# V0.2.370 — 修「扣血时从弃牌堆拿牌」把**正在打出的那张牌卡在屏幕中央**

## 原因（和 V0.2.363 是同一类问题）

你的状态是「扣血（受到伤害）时 → 从弃牌堆把某张卡拿回手牌」，**选牌方式 = 自己选**。
生成出来的钩子体里，选牌用的是钩子传进来的 `choiceContext`：

```csharp
public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, ...) {
    …
    var picked = (await CardSelectCmd.FromCombatPile(choiceContext, PileType.Discard.GetPile(...), …)).ToList();
}
```

`choiceContext` 是 `HookPlayerChoiceContext` —— 它发起选择时会**把当前正在跑的那个 action 暂停**，
再往队列里排一个新的 hook action。而你此刻正在跑的 action **就是「打出那张扣血牌」**：
那张牌被暂停在结算到一半的地方，等一个永远等不到（因为在它自己后面排队）的选牌 action
→ 牌就停在画面中央，回合也推不动。

V0.2.363 修的是「卡牌时机 = 被保留」那个时机，这次是**状态（能力）钩子**里的同一类死锁。

## 改法

`CustomPowerGen.EmitItemsWithAmountGate` 开头加一句判断：这条触发时机里**真的有选牌**
（`CardPick == "Chosen"`，且效果种类属于「自己选」那一族）时，生成：

```csharp
choiceContext = new BlockingPlayerChoiceContext();   // 钩子里选牌：别让正在结算的牌卡住
```

* `BlockingPlayerChoiceContext` 是本体自带的（它注释里写明专供「敌人回合 / 阻塞也无所谓」的场合）：
  选牌照弹、当场等玩家选完，**不再往队列里排 action**，所以不会再把出牌卡住；
* 只有真有选牌的钩子才写这一句 —— 其它钩子的生成文本**一个字都不变**（自检里有反向断言）。

## 自检

**4703 PASS / 0 FAIL**（+2 条：有选牌的状态钩子必须生成 BlockingPlayerChoiceContext /
没有选牌的钩子不许平白多这一行）。

## 你那边验证

1. 更新到 **V0.2.370** → 生成 → 构建安装；
2. 打那张扣血牌：应该**先弹弃牌堆选牌**，选完牌正常进手牌，出牌动画正常结束（不再卡在中央）。

如果还是卡，说明问题不在选牌而在「把牌塞回手牌」那一步 —— 那就把日志发我，
我在「从弃牌堆拿牌」那条效果上再补一条诊断（同 V0.2.364 的清除状态调试日志那种）。
