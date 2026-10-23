# V0.2.227 —— 修「篝火会卡死」（联机）；篝火立绘的命中区 / 悬停 / 翻面恢复

> 来源：用户实测报的「篝火会卡死」，附了 Godot 日志。
> 日志里就一条关键错误：
>
> ```
> ERROR: System.NullReferenceException
>    at MegaCrit.Sts2.Core.Nodes.RestSite.NRestSiteCharacter.FlipX()
>    at MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom._Ready_Patch1(NRestSiteRoom this)
> ```

## 1. 真因：篝火补丁把本体 `_Ready` 整段跳过了

逐帧动画（`cs/FrameAnimPatches.cs`）里那张篝火补丁，以前是**前缀补丁直接 `return false`**：
理由是「本体 `_Ready` 会把第一个子节点当 Spine 包成 `MegaSprite`，非 Spine 场景会抛异常」。

实际上本体（4.5.1）篝火那个 `_Ready` 包 Spine 的一段是**按 `GetClass() == "SpineSprite"` 过滤**的
（`GetChildSpineNodes()`，非 Spine 场景里是个空循环，根本不会抛），而它同时还要：

- 初始化 `_controlRoot`（`GetNode<Control>("ControlRoot")`）；
- 初始化 `Hitbox`（`%Hitbox`）并连上 `FocusEntered / FocusExited / MouseEntered / MouseExited`
  → **命中区 / 悬停 / 点击都靠它**；
- 初始化 `%SelectionReticle`（选择圈）与 `%ThoughtBubbleLeft` / `%ThoughtBubbleRight`（气泡锚点）；
- 按章节播骨架动画（我们没骨架，这一步本来就空转）。

跳过之后这些字段全是 `null`。单机只有一个角色、永远不会翻面，所以只是「点不到、没悬停」，看不出来；
**联机两人以上**时本体这么写：

```csharp
for (int i = 0; i < _runState.Players.Count; i++) {
    NRestSiteCharacter c = NRestSiteCharacter.Create(_runState.Players[i], i);
    _characterContainers[i].AddChildSafely(c);
    if (i % 2 == 1) c.FlipX();      // ← 2 号位角色翻面
}
```

`FlipX()` 第一行就是 `_controlRoot.Scale` → 空引用。异常从 `NRestSiteRoom._Ready` 里冒出来，
**把它后面那一整段全打断了**：`UpdateRestSiteOptions()`（生成选项）、继续按钮接线、同步器事件注册……
于是篝火没有选项、也点不了继续 = **卡死**。

## 2. 改法

1. **篝火 `_Ready` 补丁改成「先挂帧动画，再放行本体」**：挂完帧动画检查场景结构（`ControlRoot` /
   `%Hitbox` / `%SelectionReticle` 都在）→ `return true`，本体照常初始化。
   只有自己换过篝火场景、结构不齐时才仍然跳过本体（不然本体炸在 `GetNode` 上、同样打断房间 `_Ready`），
   并在日志里写明原因。
2. **新增 `FlipX` 兜底补丁**：`_controlRoot` 还是 `null` 时直接跳过翻面（只对本工具的角色生效，
   别的模组的角色不碰），日志里 `[<角色>_anim] 篝火角色还没初始化完（_controlRoot = null）…`。
3. **帧动画立绘补一次水平镜像**：本体翻面只翻 `SpineSprite` 子节点，非 Spine 的 `Visuals` /
   `__FrameAnimSprite` 不会跟着翻 → 2 号位玩家的篝火立绘原本会朝反方向，现在 `FlipX` 后补一次
   `Scale.X` / `Position.X` 取反。
4. **商店补丁不动**：`NMerchantCharacter._Ready` 只有「把第一个子节点包成 Spine」这一件事，
   跳过它是对的（多商店共处时也一样）。

## 3. 顺带恢复的东西

- 篝火立绘的**命中区**（`%Hitbox`）现在真的连了信号：鼠标悬停会出选择圈、点角色能选；
- 联机时，别的玩家的篝火立绘**高亮 / 取消高亮**（`MultiplayerPlayerContainer`）恢复；
- `Deselect()` / 思考气泡这类用到 `_selectionReticle` / 锚点的地方不再有潜在空引用。

## 4. 验收

- 自检新增 4 条（`--uicheck` **4162 PASS / 0 FAIL**）：篝火补丁不再整段跳过（结构齐 → 放行、
  不齐才跳过）、`FlipX` 兜底补丁在、非 Spine 立绘补镜像、商店补丁保持跳过；
- 探针 `sacprobe anim` 用一张 2 帧 GIF 生成整份带逐帧动画的工程，`dotnet build` **0 error**
  （`AccessTools.FieldRefAccess` / `nameof(NRestSiteCharacter.FlipX)` / `MirrorX` 都对得上本体 DLL）；
- 老存档对拍（新 Core ↔ 已发布 V0.2.226 的 Core）：示例 **113** / WhiteCloak **412** 个文件**只差 `.sln` 的随机 GUID**
  （逐帧动画没勾时那份补丁根本不生成，所以老存档一字不变）。
