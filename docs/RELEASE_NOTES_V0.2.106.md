# V0.2.106 更新说明

> 你说「做吧」——这一版把上版留着的那条**「获得的能量」**做完了（本回合 / 本场战斗），
> 顺手修掉一个回读时**悄悄丢配置**的问题（「整张牌」的条件写了公式 → 从工程恢复时公式丢了）。

## 一、「获得的能量」现在能读了（本回合 / 本场战斗）

公式内置量（卡牌效果编辑器最下面那块「可用量」小框）和「自定义参数 / 组」页的
**取值来源**里都多出两条：

| 名字 | 是什么 |
|---|---|
| `EnergyGainedThisTurn` | 本回合**获得的能量**（回合开始把能量**补满**的那部分**不算**） |
| `EnergyGainedThisCombat` | 本场战斗**获得的能量**（同上） |

**为什么要自己记**：本体只在 `PlayerCombatState.Energy` 上放一个当前值（外加一个 `EnergyChanged`
事件），战斗历史（`CombatHistory`）里**只有「花掉能量」**（`EnergySpentEntry`），**没有任何「获得」记录**，
也没有现成计数器 —— 所以在**本体的入口**上打一个后缀补丁：

```csharp
// 生成的 cs/EnergyGainTracker.cs（只在这份存档真的用到时才写这个文件）
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState), "GainEnergy")]
internal static class ForgeEnergyGainTrackerPatch {
    [HarmonyLib.HarmonyPostfix]
    private static void After(PlayerCombatState __instance, decimal amount) {
        try { ForgeEnergyGainTracker.Note(__instance, amount); }
        catch (Exception e) { Log.Warn("能量记录失败：" + e.Message); }
    }
}
```

- **打在同步方法上**：本体所有「获得能量」最后都走 `PlayerCmd.GainEnergy` →
  `player.PlayerCombatState.GainEnergy(finalAmount)` 这个**同步**方法（`PlayerCmd.GainEnergy`
  本身是 `async`，异步状态机上的补丁时机很坑，所以不打它）；
- **补满不算「获得」**：回合开始把能量补满走的是 `ResetEnergy()`、
  「获得等同于最大能量」走 `AddMaxEnergyToCurrent()`，这两个**都不经过** `GainEnergy` ——
  正好就是我们要的语义（不然这两个读数每回合都会莫名多出 3 点）；
- **不用任何重置钩子**：本体**每场战斗都会 `new` 一个 `PlayerCombatState`**
  （`Player.ResetCombatState`），所以按 `PlayerCombatState` 实例记 —— 换了战斗自然从头算；
  「本回合」按 `PlayerCombatState.TurnNumber` 认，换回合自动清零；
- 键用的是 `ConditionalWeakTable`（弱引用键），打完的战斗状态不会被这份记录拖住；
- **没用到就不生成这个文件**（默认存档的输出和以前一模一样，红线照旧守着）。

用法和别的内置量一样，直接写进公式（大小写不敏感），例如「本回合每获得 1 点能量就打 2 点伤害」
就写 `EnergyGainedThisTurn*2`；也可以在参数页把它指定成一个参数（`M = 本回合获得的能量`）再引用。

## 二、修掉一个「悄悄丢配置」：整张牌的条件写了公式，回读时丢了

「条件里的数值写公式」是 V0.2.97 加的功能，但**只有挂在「某条效果」上的条件**写了回读标记
（`// CET:CondFull=…`），挂在**整张牌**上的条件（效果编辑器上面那一栏「条件选项」）没写 ——
于是「生成 → 从工程恢复」时那一格公式**悄悄变成空**（不报错、用户还以为是自己没填），
而且反过来还会把「某条效果的条件」认成「整张牌的条件」。现在：

- 整张牌的条件写了公式 → 写它**专用**的一行标记 `// CET:CardCondFull=…`（回读按它整条重建，
  种类 / 固定数值 / 状态 / 指向对象 / 两个公式全带上）；
- 回读时两种标记各认各的：效果的条件归那条效果，整张牌的条件归整张牌，
  **没有「// 条件：…」注释的牌就是没有整张牌条件**（不再把效果的条件顶上去、也不再误报「条件没认出来」）；
- 没写公式的条件照旧不写标记（老样子）。

## 三、验证

- `--uicheck`：开发版 **2545 PASS / 0 FAIL**（新增 24 条：读数清单 / 记录器生成与内容 / 红线 /
  整张牌条件标记 / 回读不串门 / 0 没认出来）；
- 探针新增 `RunEnergyGain`（数值公式 + 参数来源 + 条件公式三条路径各来一次）→
  生成写 `cs/EnergyGainTracker.cs` → 真 `dotnet build` **0 错误** → 回读
  `EnergyGainedThisCombat + M` / 参数来源 `EnergyGainedThisTurn` / 条件公式 `EnergyGainedThisTurn`；
  整轮探针 **23 处 `HasUnparsed = False` / 0 处 True**；另外抽查 `condformula`、`formulabuiltins`
  两个探针工程也**各 0 错误**；
- 发布版（`4\程序文件`）同样 `--uicheck` **0 FAIL**。
