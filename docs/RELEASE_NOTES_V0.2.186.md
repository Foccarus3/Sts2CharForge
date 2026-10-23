# V0.2.186 更新说明

> 这一版修你报的「**状态在血条上的覆盖色没显示出来**」，外加一件顺带的事。
>
> 老实说：**我没能在日志里查到它为什么没显示** —— 因为这条链**每一处失败都不出声**
> （`RefuseAndReturn` 全是光秃秃的 `return;`，一句日志都没有），你那份 `godot (13).log` 里
> 跟这个功能相关的**一个字都没有**。所以这一版先做两件事：
> ① **让它在日志里会说话**（加载时报「带了哪几条」、运行时「画上了 / 为什么没画」都报一次）；
> ② 把两处**确实会导致看不见**的地方直接修掉（宽度下限 + 多接一个入口）。
>
> 装上之后如果还是不显示，日志里现在一定会有线索 —— 把新日志发我即可。

---

## 一、为什么之前查不出来：这条链每一处失败都是静默的

「自定义状态覆盖血条」的补丁（`cs\ForgeHpBarOverlay.cs`）挂在**本体的私有方法**
`NHealthBar.RefreshForeground` 上，代码长这样：

```csharp
if (CreatureOf is null || __instance is null) return;   // 拿不到生物 → 直接 return
if (creature.HpDisplay != HpDisplay.Normal) return;      // 无限血条 → 直接 return
if (template is null || red is null) return;             // 找不到 %PoisonForeground → 直接 return
if (parentWidth <= 0f || ...) return;                    // 血条还没布局 → 直接 return
```

**一句日志都没有。** 于是下面四种情况（**修法完全不同**）在日志里长得一模一样：

| 实际情况 | 以前日志里 |
|---|---|
| 存档里根本没填「覆盖血条颜色」（或那条状态是停用的） | 什么都没有 |
| 补丁没打上（本体改了方法名 / 场景节点名） | 什么都没有 |
| 补丁打上了但钩子没被调用 | 什么都没有 |
| 钩子调用了、也算出来了，但那一段太窄看不见 | 什么都没有 |

## 二、现在两头都会记日志（每条只记一次，不刷屏）

* **模组加载时**（`ModEntry`）先报一行「带了哪几条」：

  ```
  [覆盖血条] 本模组带了 3 条覆盖血条：冰冻监听 #C08BFF 从右、霜之监视 #003CFF 从左、…
  ```

  这一行没有 = 生成时存档里**没填颜色**（或那条状态停用了）→ 去「自定义状态」页补颜色、重新生成。

* **打补丁时**两个入口各记一条（`RefreshForeground` / `RefreshValues`），失败给警告。

* **战斗里**（`ForgeHpBarOverlay.Postfix`）：

  ```
  [覆盖血条] 钩子跑起来了（血条宽 240），但这个生物身上没有配了覆盖色的状态，所以这次没画。
  [覆盖血条] 画上了：冰冻监听　层数=3　那一段=[222.9, 240]（血条宽 240，最大生命 70）
  [覆盖血条] 没画出来（这条只报一次）：找不到本体血条节点 %PoisonForeground / %HpForeground（本体改了血条场景的节点名）
  ```

  最后这种「拿不到就 return」的每一处都有**各自的理由**，并且带上当时的实际数值
  （Mask 宽 / MaxHp / Hp），不用再猜。

## 三、顺手修掉两处**确实会导致看不见**的地方

1. **宽度下限**：本体自己算血条宽度是有 **12px 下限**的
   （`NHealthBar.GetFgWidth`：`Math.Max(val, 12f)`），我们以前是纯线性
   （`层数 / 最大生命 × 血条宽`）—— 状态只有 1~3 层时算出来只有 **3~10px**，
   而那截条是**九宫格**（左右边距各 6px）：比 12px 还窄的九宫格基本就是**画不出来**。
   现在照抄本体的 12px 下限 —— 层数少也看得见。
2. **多接一个入口**：以前只打 `RefreshForeground`；现在**它的调用方 `RefreshValues` 也打一份**
   （public 方法，本体在「血量 / 格挡 / 战斗状态变了」时都会调它）。画法是幂等的（先藏再摆），
   重复调用不会画出两条。少接一个入口的表现就是「状态挂上了但血条上看不到」。
3. **不再依赖「从 Mask 上再查一次 `%PoisonForeground`」**：模板直接用 `__instance` 上已经拿到的那个节点，
   少一处可能失败的唯一名查找。

## 四、顺带：没上传图标的自定义状态不再显示紫色问号

本体 `PowerModel.Icon` 是 `ResourceLoader.Load<Texture2D>(PackedIconPath, ...)`，**没有兜底**：
图集里没有那个精灵 → 回退 `images/powers/<状态>.png` → 这个文件也没有 → 用本体的
`missing_power.png`（紫色问号）。你的日志里一次刷了 8 条：

```
[WARN] AtlasResourceLoader: Missing sprite 'white_cloak_power_frozen_monitor' in power_atlas
[WARN] AtlasResourceLoader: Missing sprite 'white_cloak_power_shadow_mantle' in power_atlas
…（frozen_monitor / shadow_mantle / grave_awaken / ice_star / determination / will_stronger /
   more_tension_a / fatigue 共 8 条）
```

**这一版会给每个「没上传图标」的自定义状态写一张中性占位图**（和卡面 / 遗物一个口径），
`images/powers/<状态>.png` + 图集占位 `power_atlas.sprites/<状态>.tres` 都写；
**上传过图标的照旧用你的图**（文件存在就跳过，不会被覆盖）。

## 已经验过

| 项 | 结果 |
|---|---|
| 血条覆盖：**本体那边的事实核对** | 用反射读本机 `sts2.dll`（v0.111.0，和你日志里那份**同版本**）：`NHealthBar` 确实有私有字段 `_creature`、`RefreshForeground` 是私有方法且**由 `RefreshValues` 调用**、`%PoisonForeground` / `%HpForeground` / `%DoomForeground` 三个节点名都还在 → 补丁的三个假设全都成立 |
| 血条覆盖：**Godot 语义验证** | 用同一个 Godot 4.5.1 造一个和本体 `health_bar.tscn` **同构**的场景，跑 `Postfix` 的那套动作：从 **Mask** 上查 `%PoisonForeground` 拿得到、`Duplicate()` + `AddChild` 正常、算出来的那一段落在 Mask 里（`[216.9, 240]`、宽 23px）、第二次调用复用同一个节点 |
| 血条覆盖：**生成物真编译** | 用一份带 3 条覆盖色（含「从左」那一档）的存档生成工程 → `dotnet build` **0 错误**（这一次真抓出一个我改出来的参数错误，已修） |
| 图标占位 | 同一份存档：8 个自定义状态各生成一张 `images/powers/<状态>.png` + `power_atlas.sprites/<状态>.tres`，工具输出里会写「N 个自定义状态用中性占位」 |
| `dotnet build Sts2CharForge.sln` | 0 错误 |
| `--uicheck` | **3811 PASS / 0 FAIL**（覆盖血条新增 4 条：加载日志 / 两向运行时日志 / 12px 下限 / 两个入口） |
| 输出影响面 | 样例工程逐文件对拍（对齐 V0.2.143 基线）：**113 个文件里 4 个不同** —— `cs\Patches.cs`（V0.2.185）＋ `cs\ModEntry.cs`（V0.2.179 跨局保存）＋ `cs\Cards\SevenGuard.cs`（V0.2.170 既有差异）＋ `ExampleMod.sln`（随机 GUID）。**样例存档里没有自定义状态，所以这一版对样例工程一个文件都不影响** |

## 装上之后怎么看有没有生效

1. 进游戏、打一场战斗，让那个带颜色状态的状态挂到自己身上；
2. 打开 `%APPDATA%\SlayTheSpire2\logs\godot*.log`，搜 `覆盖血条`：
   * 搜不到 → 这个 mod 里没带覆盖血条（回去看「自定义状态」页的颜色填了没、状态是不是停用的）；
   * 有「钩子跑起来了…没有配了覆盖色的状态」→ 钩子正常，只是这个生物身上没有那条状态；
   * 有「画上了」→ 已经画上去了（带上层数 / 那一段的坐标 / 血条宽度），对照游戏里看那一段是不是太靠边；
   * 有「没画出来」→ 括号里就是原因，直接发我。
