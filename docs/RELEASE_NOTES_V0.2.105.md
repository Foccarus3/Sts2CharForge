# V0.2.105 更新说明

> 你说「一起做吧」——这一版先把**「一个敌人死亡后」的数值**做了（遗物 + 状态两边都有）。
> 「**获得的能量**」那条要打一个本体没有的补丁，还在做，见文末（这一版不含它）。

## 一、「一个敌人死亡后」现在能填「每几个敌人死亡触发一次」

遗物页 / 状态页选「一个敌人死亡后」，下面会多出一栏「**每几个敌人死亡触发（本场战斗内计数）**」：
留空 / 0 = 每死一个就触发（老行为）；填 N = 每 N 个敌人死亡触发一次。

**为什么这一条要单独做**：本体在战斗历史（`CombatHistory`）里**没有记录死亡**，
所以前几批那种「读历史、无状态算」的办法用不了 —— 这一条由工具**自己记一个计数器**：

```csharp
// 生成的 cs/CombatCounters.cs（只在这份存档真的用到时才写这个文件）
public static class <角色>ForgeCombatCounters
{
    private static ICombatState? __state;
    private static int __deaths;
    public static int BumpEnemyDeath(ICombatState? state)
    {
        if (!ReferenceEquals(__state, state)) { __state = state; __deaths = 0; }   // 换一场战斗自动归零
        return ++__deaths;
    }
    public static int EnemyDeaths(ICombatState? state) => ReferenceEquals(__state, state) ? __deaths : 0;
}
```

钩子里就是：

```csharp
if (!creature.IsEnemy) return;                                                       // 只认敌人
if (__deaths % 2 != 0) return;   // 每 2 个敌人死亡触发一次（本场战斗内计数）        ← 遗物侧
int __deaths = <角色>ForgeCombatCounters.BumpEnemyDeath(base.Owner.CombatState);    ← 状态侧（多条时机共用钩子）
```

- **不用在战斗开始时做任何重置**：计数器按 `ICombatState` 认「这是哪一场」，换了战斗自动从 0 重新数；
- 类名带角色前缀（`<角色>ForgeCombatCounters`），两个模组同时装也不会撞类名；
- 回读照旧靠 `// CET:TriggerAmount=Nm CET:TriggerScope=…` 那行标记。

## 二、还没做的最后一条：获得的能量

「**获得的能量**」（本回合 / 本场战斗）本体在战斗历史里**只记「花掉」**（`EnergySpentEntry`），
没有任何「获得」的记录，也没有现成计数器 —— 所以它和上面这条一样得由工具自己记一份，
但**能量是在很多地方涨的**，必须在**本体的入口**上打补丁：

- 已经确认本体有一个统一入口：`PlayerCmd.GainEnergy(decimal amount, Player player)`
  （内部 `Hook.ModifyEnergyGain` → `PlayerCombatState.GainEnergy(finalAmount)`），
  所以补丁打在它（或 `PlayerCombatState.GainEnergy`）的 postfix 上就能把「获得」记全，
  包括每回合回满（`ResetEnergy`）和「获得最大能量」（`AddMaxEnergyToCurrent`）这两条旁路；
- 记下来之后就是普通读数：`EnergyGainedThisTurn` / `EnergyGainedThisCombat`，
  加进公式内置量 + 参数页「从哪来」候选（和 V0.2.103 那批一样，一处定义四处用）。

⚠ 这条会往用户的工程里**多生成一个补丁文件**（和「抽牌堆洗过牌」那个记录器同样的做法），
所以我想先确认一句再动手：**你要的是「本回合/本场战斗获得了几点能量」（补丁记一份），
还是只想要「现在有几能量 / 这回合花了多少」这种现成的**？后者现在就能用
（`Energy`、`EnergySpentThisTurn`、`EnergySpentThisCombat`）。

## 三、验证

- 探针 `RunTriggerAmounts` 新增遗物「每 2 个敌人死亡」→ 生成（写了 `cs/CombatCounters.cs`）→
  真 `dotnet build` **0 错误** → 回读 `amount=2`；整轮探针 **22 处 `HasUnparsed = False` / 0 处 True**；
- `--uicheck`：开发版与发布版都是 **2521 PASS / 0 FAIL**。
