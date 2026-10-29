# V0.2.331 — 动画页「缩放 / Y 偏移」以前压根没生效；X 偏移是新增的

你让我先**检测**那三个功能，检测结果（代码层面，都有出处）：

| 功能 | 结论 | 出处 |
|---|---|---|
| 缩放 | ❌ **不生效** | `FrameAnimGen.Attach()` 里，视觉节点是 `Sprite2D`（正常情况）时，新建动画精灵直接抄静态立绘：`Position = s2d.Position, Scale = s2d.Scale` —— `def.Scale` 只在「body 不是 Sprite2D」的兜底分支里用 |
| Y 偏移 | ❌ **不生效** | 同上，`def.YOffset` 同一条路径被忽略 |
| X 偏移 | ❌ **根本不存在** | 全仓库搜不到 `XOffset`；`AnimationSpec` / `Plan` / 运行时注册表里都没有这个量 |
| 播放速度系数 | ✅ 生效 | `NormalizeFrames()` 把每帧时长按系数除（`durs[i] / speedScale`），日志有「速度系数 X 已应用」 |

## 这一版改了什么

1. **修好缩放 / Y 偏移**（`Sprite2D` 分支）：

   ```csharp
   Position = s2d.Position + new Vector2(def.XOffset * s2d.Scale.X, def.YOffset * s2d.Scale.Y),
   Scale    = new Vector2(s2d.Scale.X * def.Scale, s2d.Scale.Y * def.Scale),
   ```

   偏移量按静态立绘的缩放换算（`AutoYOffset` 本来就是「纹理像素 × scale」的口径）。

2. **新增 X 偏移**：`AnimationSpec.XOffset` → `Plan` → 运行时 `Register(..., xOffset)` → `CharDef.XOffset` → 生成 `__animXOffset` 常量，并写进 manifest（`"xOffset"`）。动画页里也加了「X 偏移（像素，正数往右；0 = 水平居中）」那一栏。

3. 缩放的语义（不改以前的口径）：`0` 仍自动算（静态立绘高 ÷ 待机内容高），非 0 = 用你填的值；显示大小 = 静态立绘缩放 × 这个值。

## 验收

* 自检：**4553 PASS / 0 FAIL**（+3 条 V0.2.331 断言：`XOffset` 存在 / 计划里带着 X 偏移 / 运行时确实是「位置 + 偏移、缩放相乘」）。
* 只出更新包（`-NoBundle`）。

## 还没做的（下一版）

你要的另外两块我**没有**在这一版里塞进去，避免一次改太多说不清：

1. **「动画设置」只保留这四项**（把 手动裁剪框 / 抠背景+容差 / 剥描边+亮度 / 最近邻 / 每帧时长 / 篝火·商店保持 从界面和模型里删掉，生成固定用默认值）；
2. **预览图**：循环播放 GIF（带播放/暂停按钮，按速度系数播）+ 数值变化时预览跟着缩放/平移 + 图下画「血条中心瞄点」。

关于瞄点我先说清一个**数据缺口**：本体的血条位置是 `NHealthBar.UpdateLayoutForCreatureBounds()` 算的 ——
横向 = 角色包围盒（`%Bounds`）的**中心** ✔（这个我能算准）；纵向 = 血条容器在**角色视觉场景模板**里的固定 Y，
这个值不在生成器的数据里（每个角色的场景模板自带）。所以下一版我会把瞄点画成「横向中心线 + 顶部血条占位框」，
纵向先按本体那个 24px 的量摆；你进游戏截一张「血条相对角色头顶的实际距离」给我，我就能校准成精确值。
