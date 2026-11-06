# V0.3.28 — 打第二层 Boss「凯撒螃蟹」时 GIF 不转向

日志 `godot.log` 里能找到这场战斗：

```text
Creating NCombatRoom with mode=ActiveCombat encounter=KAISER_CRAB_BOSS.
[sparkle_anim] 挂载: 根=SparkleVisuals body=Visuals(Sprite2D)
```

## 真因

凯撒螃蟹（`KAISER_CRAB_BOSS`）会给自己/玩家挂 `SurroundedPower`。它的 `FaceDirection` 会翻：

```csharp
Node2D body = NCombatRoom.Instance.GetCreatureNode(c).Body;   // = NCreatureVisuals.%Visuals
body.Scale *= new Vector2(-1f, 1f);
```

也就是翻 **`%Visuals` 这个 Sprite2D** 的 `Scale.X`。

而旧版生成的 `AnimatedSprite2D` 是 `%Visuals` 的**兄弟节点**（为了 `Visible=false` 收静态图时不把动画一起隐藏）。
兄弟节点不继承 `%Visuals.Scale.X`，所以螃蟹一转向，GIF 仍然朝原来的方向 —— 表现就是「不会转向」。

## 修法

把 `AnimatedSprite2D` 挂成 `%Visuals` 的**子节点**，让它自然继承 `Scale.X`（以及缩放 / 位置）：

```csharp
staticTexture = s2d.Texture;
asp = new AnimatedSprite2D {
    Position = new Vector2((float)def.XOffset, (float)def.YOffset),
    Scale = new Vector2((float)def.Scale, (float)def.Scale),
    ...
};
s2d.AddChild(asp);
```

不能再用 `s2d.Visible = false` 收静态图（会把子节点一起隐藏），改成：

```csharp
_staticTexture ??= st.Texture;
st.Texture = null;      // 只隐藏静态图自己，子动画照常画
```

回滚时再把 `Texture` 还回去。

篝火补丁里原来还会单独翻一次 `__FrameAnimSprite`；现在它是 `%Visuals` 的子节点，会跟着翻，
再翻一次会抵消，所以删掉那一句。

## 验证

| 项 | 结果 |
|---|---|
| 生成运行时里有 `s2d.AddChild(asp)`，没有旧的 `sp.AddChild(asp)` | PASS |
| 收静态图是 `st.Texture = null`，不是 `st.Visible = false` | PASS |
| 篝火补丁不再单独镜像 `__FrameAnimSprite` | PASS |
| `dotnet build Sts2CharForge.sln -c Debug` | **0 错误** |
| `--uicheck`（开发版） | **4872 PASS / 0 FAIL** |
| 真编译探针（带 idle/attack GIF） | 生成的帧动画运行时 **0 错误** |
| 部署版 `--uicheck` | **4871 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里的 `FrameAnimRuntime.cs` 仍是兄弟节点写法。
