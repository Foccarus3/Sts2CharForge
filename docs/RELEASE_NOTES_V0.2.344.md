# V0.2.344 — 卡牌引用：悬停就能看到「这条效果会生成/变化成的那张卡」的卡面

你要的效果：像本体「刀刃之舞」悬停时旁边冒出「小刀」的卡面那样。

## 怎么做的：和本体是同一个 API

本体 `BladeDance.cs` 里就一行：

```csharp
protected override IEnumerable<IHoverTip> ExtraHoverTips => new[]{ HoverTipFactory.FromCard<Shiv>() };
```

弹出来的 `CardHoverTip` 就是那张小卡面。我们这边不用新发明 —— 工具**早就有** `ExtraHoverTips` 这个出口
（`CSharpCodeGen.HoverTipsOf`，卡牌 / 诅咒 / 先古卡 / 状态卡 / 遗物 / 药水六条链都走它，原本只塞状态提示），
这一版在里面补了一段「这条效果提到了哪张牌」：

```csharp
protected override IEnumerable<IHoverTip> ExtraHoverTips =>
[
    MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromCard<MegaCrit.Sts2.Core.Models.Cards.Dazed>(),
    MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromPower<Hakking>(),
];
```

## 覆盖范围（按你选的 A + B′，卡面数量不设上限）

| 效果 | 悬停卡面 |
|---|---|
| 生成卡牌（`GenerateCard`） | `SpawnCardId` 填了 → 那张；**留空 → 小刀**（生成端本来就写死 `Shiv`） |
| 获得卡牌（全局）（`AddCardGlobal`） | 同上 |
| 用目标卡填满手牌（`FillHand`） | 同上（生成端留空也是 `Shiv`） |
| 变化卡牌 / 全局变化（`TransformCard` / `TransformCardGlobal`） | 填了就弹；**留空 = 随机卡，不弹**（不瞎猜） |
| 强化指定卡（`BoostCard`） | 填了就弹 |
| 勾了「生成的是升级过的」 | 卡面按**升级后**显示：`FromCard<X>(true)` |

* 同一张卡被几条效果提到 → 只弹一次（去重）。
* 自己的卡写短类名；**本体卡一律写全名**（本体那张 `Void` 会和 `System.Void` 撞，工具里 `CardTypeExpr` 早就是这个口径）。
* 遗物 / 药水 / 诅咒 / 先古卡 / 状态卡的「生成卡牌」也一样弹（走同一个 `HoverTipsOf`）。

## 证据（拿你的存档跑出来的）

你的月雪存档里，「银蜂」（`RukayukiCard20`）会生成「眩晕」。生成出来的源码里现在是：

```csharp
protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips =>
[
    MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromCard<MegaCrit.Sts2.Core.Models.Cards.Dazed>(),
];
```

## 自检

**4607 PASS / 0 FAIL**（+8 条：生成卡牌留空 = 小刀卡面 / 点名自己的卡用短类名 / 升级态 `FromCard<X>(true)` /
本体卡用全名 / 反向：变化类留空不弹 / 同卡去重 / 遗物链也弹 / 前面那条状态提示不受影响）。

## 还没做（你之前没选的那两档，留着）

* **C 档**：「随机范围 / 自定义组」也弹（最多 N 张 + 一句「随机」）—— 你现在选了「不限张数」但只做 A+B′，
  要做的话是另一个小版本；
* **D 档**：自定义描述里写「提到某张卡」的语法（`[[小刀]]` 这种）→ 自动挂卡面。
