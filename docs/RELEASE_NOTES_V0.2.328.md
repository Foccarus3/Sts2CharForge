# V0.2.328 — 变出来的牌对了，但原来的卡还悬浮在空中（视觉残留）

用户原话：

> 卡是变出来了，但是原来的卡怎么还悬浮在空中

## 真因：两个「跳过视觉」叠在一起

V0.2.327 生成的是：

```csharp
if (this.Pile is null || this.Pile.Type != PileType.Discard)
    await CardPileCmd.Add(this, PileType.Discard, CardPilePosition.Bottom, null, skipVisuals: true);  // ← ①
await CardCmd.Transform(this, __madeSelf);                                                              // ← ②
```

* **① `skipVisuals: true`**：挪牌时不播动画 —— 但那张牌的 UI 节点就是靠这个动画被「飞进弃牌堆、然后收走」的。
  跳过动画 → 节点留在原来的位置（= 悬浮在空中），模型却已经换掉了。
* **② `CardCmd.Transform` 默认的 `CardPreviewStyle.HorizontalLayout`**：会弹一个「变化预览」的浮层，
  战斗界面里叠着一张预览牌，观感同样是「有东西飘着」（本体 `PandorasBox` 就是显式传 `None` 躲开它）。

## 改法

```csharp
if (this.Pile is null || this.Pile.Type != PileType.Discard)
    await CardPileCmd.Add(this, PileType.Discard);   // 照常播「飞进弃牌堆」，节点才收得走
await CardCmd.Transform(this, __madeSelf, MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle.None);   // 不弹预览浮层
```

## 验收

* 探针 `sacprobe transform0`（列表链 + 图链各一张）：生成上面两句，整份工程 **`dotnet build` 0 错误**。
* 自检：**4543 PASS / 0 FAIL**（断言同步）。
* 只出更新包（`-NoBundle`）。

## 如果还残留

那是「打出流程」自己那一步又把牌挪了一次（你这台机器上装了 `EgOMoDan` 的
`Hook.ModifyCardPlayResultLocation` 补丁，它会改「打出的牌去哪个牌堆」）。
真还遇到就把 `godot*.log` 发我 —— 我改成「让打出流程自己把牌放进弃牌堆、我们只负责在原地变」的写法。
