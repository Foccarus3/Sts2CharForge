# V0.2.230 —— 地图上画线的「蜡笔」颜色可以自定义了

> 来源：你问「能不能自定义在地图上的蜡笔的颜色」—— 能，引擎那边就是一个虚属性。

## 1. 它是什么：`CharacterModel.MapDrawingColor`

联机时在地图上画路线的那支「蜡笔 / 羽毛笔」，画出来的每一条线都是一个 `Line2D`，颜色取自身：

```csharp
// Core/Nodes/Screens/Map/NMapDrawings.cs:321
line2D.DefaultColor = player.Character.MapDrawingColor;
```

- `CharacterModel.MapDrawingColor`（`Core/Models/CharacterModel.cs:187`）默认是 **`Colors.Black`**；
- 本体每个角色各自覆盖成一个专属色：

| 角色 | 蜡笔颜色 |
| --- | --- |
| 铁甲战士 | `CB282B` |
| 静默猎手 | `2F6729` |
| 故障机器人 | `0D638C` |
| 亡灵缚者 | `AC0486` |
| 摄政王 | `935206` |

- 它是**虚属性**，所以在生成的角色类里写一句 override 就够了 —— **不需要任何补丁**；
- 联机时队友看到的就是**你**这个颜色（每条线的颜色跟着「画线的那个人」的角色走）。

## 2. 工具里怎么改

「角色」页新增一行 **「地图画线颜色（蜡笔）」**（在「角色最大能量」下面）：

- 框里填 `RRGGBB`（也能填 8 位 `RRGGBBAA` 带透明度），右边那个小方块就是实时色块；
- 填了之后生成的角色类里会多这一句：

```csharp
// 在地图上画线（蜡笔 / 羽毛笔）的颜色：本体 NMapDrawings 用 Character.MapDrawingColor 给 Line2D 上色
public override Color MapDrawingColor => new Color("3FA9F5");
```

- **留空 = 一个字都不写**（沿用本体默认黑色）；老存档 / 基线生成结果逐字不变；
- 校验器：
  - 非法值（例如 `zzz`）→ **错误**（不会悄悄退回黑色）；
  - 8 位颜色但透明度是 `00`（画出来根本看不见）→ **警告**。

## 3. 验收

- 探针 `sacprobe mapcolor`：生成的角色类里有 `public override Color MapDrawingColor => new Color("3FA9F5");`，
  整份工程 `dotnet build` **0 error**（`Color(string)` 这个构造和本体 `new Color("CB282B")` 同款）；
- 自检新增 6 条（`--uicheck` **4186 PASS / 0 FAIL**）：输入框绑的是 `Profile.MapDrawingColor`、
  留空不写 override（红线）、`#3fa9f5` → `3FA9F5`、8 位也支持、校验器「错误 / 警告 / 留空一条都不出」、存档往返；
- 老存档对拍（新 Core ↔ 已发布 V0.2.229 的 Core）：示例 **113** / WhiteCloak **412** 个文件**只差 `.sln` 的随机 GUID**。
