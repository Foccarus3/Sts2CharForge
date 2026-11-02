# V0.2.402 — 修「扣血时从弃牌堆拿牌 → 打出的扣血卡卡死在画面中央」

## 根因（用你的 `MetallicSword.json` 复现）

状态 `MetalicSwordPowerxunqiu`「剑意昂扬」：触发 = **扣血时（HpLost）**，效果 = **从弃牌堆拿指定卡回手牌**，数值 = 10。
以前生成这样：

```csharp
MegaCrit...PlayerChoiceContext choiceContext = new ...HookPlayerChoiceContext(this, __netId, ...);
var __fgTake = await CardSelectCmd.FromCombatPile(choiceContext, Discard.GetPile(player), player,
                   new CardSelectorPrefs(DiscardSelectionPrompt, (int)(10m)), 过滤条件);
```

两个问题叠在一起：

1. `HookPlayerChoiceContext` 发起选牌时，会把**正在结算的那张牌**暂停、再排队等一个 hook action ——
   那张牌永远等不到恢复，于是**卡在画面中央**（和 V0.2.370 同一类死锁）；
2. 选牌要求「正好 10 张」，而过滤条件只匹配 1 张卡 → 界面永远确认不了 → 就算不卡死也走不下去。

## 改法

* **钩子里要弹选牌 → 用 `BlockingPlayerChoiceContext`**：选牌照弹、当场等玩家选完，不再往队列里排。
  * 判据有两种来源：① 触发时机的效果列表里有选牌（含「从三摞拿牌」）；
    ② **锻造图正文**里出现 `CardSelectCmd`（你这个状态就是这种，效果写在图纸上）。
  * 两者必须与下面那句 `choiceContext = new Blocking...` 判据**一致**——否则先 `var ...Hook...` 再赋 Blocking 会 CS0029。
* **选牌张数按「实际可拿的数量」夹取**：
  `new CardSelectorPrefs(prompt, (int)Math.Min(10m, 可选张数))` —— 只有 1 张就只要求选 1 张。
  图纸那条路同样处理。

现在你这个状态生成的是：

```csharp
MegaCrit...PlayerChoiceContext choiceContext = new ...BlockingPlayerChoiceContext();
...
var __fgTake = (await CardSelectCmd.FromCombatPile(choiceContext, Discard.GetPile(player), player,
    new CardSelectorPrefs(DiscardSelectionPrompt, (int)Math.Min((decimal)(10m), (decimal)(匹配张数))), 过滤条件)).ToList();
```

## 自检

**4789 PASS / 0 FAIL**（开发版）。
过程中有 3 条**老断言**因为行为变化而被更新（不是放宽，是事实变了）：

* 「没有选牌的钩子文本不变」→ 现在「从牌堆拿牌（含随机）」也算有选牌，改成正面断言；
* 「TakeFromDiscard / BoostCard 会自己造 choiceContext」→ 现在声明是 `PlayerChoiceContext choiceContext = new ...`，
  断言改成接受两种声明形式。

## 仍存在的一个边界（如实说）

如果**弃牌堆里一张符合过滤条件都没有**，`Math.Min(…, 0)` = 0，`CardSelectorPrefs(prompt, 0)` 仍会等一个不可能成立的确认。
这一版没修（要加「池子为空就整段跳过」的运行期守卫）。规避：把取牌方式改成「全部」，或保证那张卡在弃牌堆里。
下一步我会补 `if (池子.Count > 0) { … }` 守卫。
