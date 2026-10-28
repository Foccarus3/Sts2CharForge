# V0.2.313 — 「删除卡牌（全局）数值 0」只删一张，不再把同名副本全删光

用户原话：

> 删除卡牌（全局）会把牌组中的所有同名牌删除

## 真因

「删除卡牌（全局）」在**数值 = 0**（= 「这张牌自己」）那一档，生成的是：

```csharp
foreach (CardModel c in PileType.Deck.GetPile(...).Cards
             .Where(c => c.IsRemovable && c.Id == this.Id).ToList())   // ← Id 是「卡牌模板」的 id
    await CardPileCmd.RemoveFromDeck(c);
```

`c.Id` 认的是**卡牌模板**（同名 / 升级前后都算同一张），而 `Where(...).ToList()` 把**所有匹配的副本**
都收进列表逐个删 —— 所以牌组里 3 张「打击」会在一次效果里全没。

（本体 `CardPileCmd.RemoveFromDeck(card)` 本身是**删一张实例**，问题完全出在我们那个循环上。）

## 改法

数值 0 那一档只删**一张**副本：

```csharp
foreach (CardModel c in PileType.Deck.GetPile(...).Cards
             .Where(c => c.IsRemovable && c.Id == this.Id).Take(1).ToList())
    await CardPileCmd.RemoveFromDeck(c);
```

* 「只删一张」= 数值 0 的自然语义（删掉这张牌）；
* 想一次删光仍然可以：把那条效果的「取几张」切成 **「全部」**（生成的是
  `Where(c => c.IsRemovable).ToList()`，整批删 —— 那一档没动）。

## 验收

* 自检：**4509 PASS / 0 FAIL**（+2 条 V0.2.313 断言：数值 0 生成 `.Take(1)`；对照「勾了全部才整批删」）。
* 逐字节比对：Sample 113 个文件不变；WhiteCloak 只有 `WhiteCloakTensionMax.cs` 变了 ——
  正是那张「数值 0 删自己」的牌（`Take(1)` 进去），其余文件全同。
* 只出更新包（`-NoBundle`）。

## 还没改的（有问题再说）

「升级牌组（全局）/ 变化牌组（全局）」的**数值 0** 仍然是「同名的副本一起处理」
（`Where(c => c.Id == this.Id)` 整批升级 / 整批变化）。这两档这次没动 ——
如果它们的「一起处理」也不是你要的（想只动一张），说一声我下一版一起改成只动一张。
