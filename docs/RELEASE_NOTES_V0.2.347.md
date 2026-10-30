# V0.2.347 — 动画预览的参照物换成「静态立绘」：撤掉「血条中心」那套错的

你那三个判断都对：**蓝点不是血条中心**、**蓝点应该就是动画瞄点**、**瞄点和游戏里的血条中心对不上**。
查了本体源码，是我上一版（V0.2.334/335）拿旧注释推错了一个参照物。

## 真相（有源码依据）

**血条**是本体自己摆的，跟我们的偏移**毫无关系**：

```csharp
// NHealthBar.UpdateLayoutForCreatureBounds
float pad = 24f - _creature.Monster?.HpBarSizeReduction;
HpBarContainer.GlobalPosition = new Vector2(bounds.GlobalPosition.X - pad * 0.5f, HpBarContainer.GlobalPosition.Y); // Y 不改
SetHpBarContainerSizeWithOffsets(new Vector2(bounds.Size.X + pad, ...));
```

* 水平：居中于角色视觉场景里那个 `%Bounds` 控件（`NCreature.cs`：`Hitbox ← boundsContainer.GetNode<Control>("%Bounds")`）；
* 垂直：`HpBarContainer.GlobalPosition.Y` **这一步根本不改** —— 来自 `NCreature` 场景里 `NCreatureStateDisplay` 节点的位置，场景写死。
* 所以「血条中心」既不是瞄点，也**算不出来**（要读角色自己的立绘场景）。之前那个「脚底下方 ≈5.4% 内容高」是编的。

**瞄点 = 静态立绘那个 `Sprite2D` 的 `Position`**：生成的帧动画节点是复制它、再叠偏移：

```csharp
Position = s2d.Position + new Vector2(def.XOffset * s2d.Scale.X, def.YOffset * s2d.Scale.Y);
Scale    = new Vector2(s2d.Scale.X * def.Scale, s2d.Scale.Y * def.Scale);
```

`ArtGenerator` 写的立绘节点是 `position = (0, -spriteH/2)` + Sprite2D 默认 `Centered=true` ⇒ **立绘中心就是瞄点**，
**偏移 0 = 帧动画与立绘完全重合**（这才是「瞄点」的确定含义）。

## 改了什么

* 预览改成：**半透明静态立绘当底图** + 蓝点/十字 = 瞄点（立绘中心）+ 灰虚线 = 地面线（立绘底边，
  在瞄点下方半个立绘高）+ 当前帧按生成代码算出来的位置画 + 红虚线 = 偏移矢量；
* **血条不画了**，也不再当参照（ToolTip / 提示文字里「血条中心」的字样全清掉）；
* 读数字样改成：`相对瞄点：X +12（向右） · Y -97（向上）（立绘像素） —— 瞄点 = 静态立绘的 Sprite 原点（立绘中心），偏移 0 = 帧动画与立绘完全重合`；
* 预览的所有数字都来自**生成的运行时同一套式子**（`AnimFramesGen.PreviewGeometryOf`）：
  自动裁剪框（所有帧内容并集 +8px）、自动缩放（立绘高 ÷ 待机内容高）、自动 Y（`AutoYOffset`）、
  立绘缩放（`ArtGenerator.SpriteFit`：高于 400px 缩到 ~340）。

## ⚠ 一个如实提醒（重要）

按这套式子，**Y = 自动 时帧内内容底边落在「瞄点」上**，而地面线在瞄点下方半个立绘高 ——
也就是算出来会让动画比地面线高半个身位。你截图里游戏内看着人是站在地上的，说明这里可能还有一处我没对上
（立绘节点的 `Centered` / `position`，或者裁剪框底部那点留白）。

现在预览显示的就是**生成代码算出来的位置**（不再是我编的参照）。如果你打开一看预览里也明显偏高，
那就是真 bug；下一版修法有两条路，得你定一条：

1. 改 Y 的自动式子（把「对齐瞄点」改成「对齐地面线」，即 + SpriteH/2）；
2. 改立绘节点的摆位（`position` 改成让 Sprite 原点落在脚上）。

## 自检

**4625 PASS / 0 FAIL**（+8 条：读数不提血条 / 页面里没有「血条中心」/ 地面线 = 瞄点下方半立绘高 /
X=0 时帧图中心与瞄点重合 / 自动缩放 1.95 与裁剪框 (22,42)-(78,158) / 自动 Y = −97.5 / 显式值全按填的算）。
