# V0.3.7 — 卡牌属性新增「多人卡」（只会在多人游戏中出现）

## 加了什么

卡牌属性页「加入卡池」下面多了一个勾选框：

> ☐ **多人卡（只会在多人游戏中出现）**

勾上之后，生成出来的卡牌类会多一行：

```csharp
public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;
```

这是**本体自己的机制**（`CardModel.MultiplayerConstraint`，枚举 `CardMultiplayerConstraint.MultiplayerOnly`）——
本体的 BeaconOfHope / BelieveInYou / BladeSymphony / Blaze / Cacophony 这些「多人专用牌」全是这么写的。

效果：这张牌只在**多人局**的卡池 / 战斗奖励 / 商店里出现；单人局里不会出现（已经拿到的牌照旧能用）。
不勾 = 一行都不写（老存档生成的源码**逐字不变**）。

## 自检

**4803 PASS / 0 FAIL**（+2 条）：

* 勾了 → 生成 `MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;`；不勾 → 源码里连 `CardMultiplayerConstraint` 都没有；
* 那行 override 必须出现在 `OnPlay` **之前**（类成员层），写进方法体就是语法错误 —— 自检用位置比较守住（override=447 < OnPlay=617）。

## 说明

* 多人卡**仍然照常进卡池**（要不要进「加入卡池」还是你说了算）；区别只是**本体会按这一局的玩家数筛掉它**。
* 如果你还想要「只在单人出现」，本体也有 `SingleplayerOnly` —— 需要的话告我，我加个三档下拉（默认 / 仅多人 / 仅单人）。
