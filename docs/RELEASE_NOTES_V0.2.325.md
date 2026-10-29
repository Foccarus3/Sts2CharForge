# V0.2.325 — 变化卡牌「数值 0」现在真的替换掉这张牌（新牌进弃牌堆）

用户原话：

> 变化卡牌数值为0时怎么不是变化自己

## 真因：不是没生成，是**变到了看不见的地方**

V0.2.320 起「变化卡牌 = 0」生成的确实是「变自己」：

```csharp
CardModel __madeSelf = base.CombatState.CreateCard(ModelDb.Card<目标卡>(), base.Owner);
await CardCmd.Transform(this, __madeSelf);
```

但本体 `CardCmd.Transform` 的实现是「把旧牌从它**当前所在的那一摞**里拿掉，把新牌放进**同一摞**」。
这张牌是在 `OnPlay` 里、此刻躺在「**打出**」那一摞（`PileType.Play`）—— 于是变出来的新牌被塞回「打出」摞，
战斗结算完就跟着消失了 → 你看到的就是「根本没变」。

（顺带：`Transform` 还要求旧牌有 pile、且 `IsTransformable`，否则直接抛异常。）

## 改法：按「真的替换这张牌」来实现

```csharp
// 数值 0 = 这张牌自己：变出来的新牌进弃牌堆，再把这张牌自己消耗掉（= 替换）
CardModel __madeSelf = base.CombatState.CreateCard(ModelDb.Card<CarmenCrimsonScarAlteration>(), base.Owner);
await CardPileCmd.Add(__madeSelf, PileType.Discard);
await CardCmd.Exhaust(choiceContext, this);
```

* **1 张换 1 张**：不会变成「原牌 + 变体」两张叠在弃牌堆里；
* 这正好对上你卡面写的「变化成 1 张猩红创痕-变调，**放入弃牌堆**」；
* 「升级 / 仅本回合升级 / 加关键词」这些附加处理照旧；
* 「按范围随机」那档同理（新牌进弃牌堆 + 消耗自己）；
* 想要「额外多一张」（不替换原牌）就用**生成卡牌**，别用变化卡牌。

## 验收

* 你存档里的 **猩红创痕 / 郁蓝创痕** 现在生成：
  `CardPileCmd.Add(__madeSelf, PileType.Discard)` + `CardCmd.Exhaust(choiceContext, this)`。
* 探针 `sacprobe transform0`（列表链 + 图链各一张，数值 0 + 指定卡）：**`dotnet build` 0 错误**。
* 自检：**4543 PASS / 0 FAIL**（断言同步改成新写法）。
* 只出更新包（`-NoBundle`）。

## 提醒

「消耗掉自己」会走本体的消耗流程 —— 如果你那只状态/遗物有「每消耗一张牌触发」，它也算一次。
不想要这个副作用就把数值填成 ≥ 1 并用「生成卡牌」。
