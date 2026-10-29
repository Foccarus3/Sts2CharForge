# V0.2.326 — 变化卡牌 0：消耗自己前先看有没有 pile（再稳一手）

这条是给 V0.2.325 的加固，**不改变行为**。

## 改了什么

```csharp
// V0.2.325
await CardCmd.Exhaust(choiceContext, this);

// V0.2.326
if (this.Pile is not null) await CardCmd.Exhaust(choiceContext, this);
```

为什么：`CardCmd.Exhaust` 内部会 `CardPileCmd.Add(card, PileType.Exhaust)`，而本体的 Add/Transform 都要求
「这张牌有 pile」。如果别的流程（消耗类效果、其它 mod 的 patch —— 你这台机器上就装了
`EgOMoDan` 的 `Hook.ModifyCardPlayResultLocation` 补丁）已经把它挪走了，没 pile 时 Add 会抛异常，
整段 OnPlay 中断 → 看起来就是「这条效果一点反应都没有」。加了这层判断后：变出来的新牌照样进弃牌堆，
消耗那一步在没 pile 时安静跳过，不会再把后面的效果一起带崩。

## 验收

* 自检：**4543 PASS / 0 FAIL**（断言同步改成新写法）。
* 只出更新包（`-NoBundle`）。

## 还请你确认一件事

你贴的日志是 **23:19** 那次运行（V0.2.324 之前），里面没有这张牌的任何记录，所以没法判断 325 之后的表现。
要定位「填 0 还是没生效」，请发**装了 V0.2.326 之后**那次运行的 `godot*.log`，同时：

1. 「构建/日志」页把「**生成调试日志**」打开，重新生成 → 构建；
2. 游戏里打出这张牌，再看日志里有没有我们打的 `[Carmenmod] 自定义状态 ...` / 效果行。

另外提醒：你那只 **猩红创痕** 的图里，变化卡牌是包在【如果】**「上一张打出的牌是创痕牌」**里头的 ——
如果前一张不是创痕牌、或那几张创痕牌已经被消耗掉了，这一段本来就不会跑（这是你自己配的条件，不是 bug）。
**郁蓝创痕** 用的是「生成卡牌」而不是「变化卡牌」，两者不要混。
