# V0.2.190 更新说明

> 你报的：「**状态失去格挡还是没有正常生效**」——对，这一条我上一轮只修了**锻造节点图**那条链
> （V0.2.174），**状态链**漏了。
>
> 用你这份 `WhiteCloakMod.json` 真生成了一遍，你说的状态就在里面：
>
> ```
> 状态「疲劳」：触发「玩家回合结束时」→【获得格挡】数值 = -6
> ```
>
> **旧版生成出来的代码**（就是这个 bug 的样子）：
>
> ```csharp
> await CreatureCmd.GainBlock(base.Owner, -6m, ValueProp.Unpowered, null);
> ```
>
> 本体 `CreatureCmd.GainBlock` 对**非正数直接返回**（什么都不做）⇒ 游戏里一点格挡都不掉。
> **改后**：
>
> ```csharp
> await CreatureCmd.LoseBlock(choiceContext, base.Owner, System.Math.Abs(-6m), base.Owner);   // 负数 = 失去格挡
> ```

---

## 一、顺带查出同一族的另外四处（其中一条**会抛异常**，不是静默）

你这份存档里还有一条：

```
状态「黑暗圣所（减益）」：触发「打出一张牌时」→【额外资源量】数值 = -1
```

旧版生成的是 `PlayerCmd.GainStars(-1m, base.Owner.Player)` —— 而本体 `GainStars` 对负数**直接抛
ArgumentException**（不是忽略！）⇒ **每打出一张牌就抛一次异常**（抛在状态钩子里，钩子链断在那里）。
改后：`PlayerCmd.LoseStars(-1, …)` ✓。

顺手把同一族剩下的三条也对齐（卡牌 / 遗物 / 药水 / 节点图四条链早就是「负数 = 反向」，只有状态链漏）：

| 效果种类 | 填负数时以前 | 现在 |
|---|---|---|
| **获得格挡** | `GainBlock(-N)` → 什么都不做 | `CreatureCmd.LoseBlock(…, N, …)` ✓ |
| **回复生命** | `Heal(-N)` → 什么都不做 | 改走**失去生命**（`Damage(…, Unblockable \| Unpowered, …)`）✓ |
| **获得能量** | `GainEnergy(-N)` → 什么都不做 | `PlayerCmd.LoseEnergy(N, player)` ✓ |
| **获得金币** | `GainGold(-N)` → 什么都不做 | `PlayerCmd.LoseGold(N, player)` ✓ |
| **额外资源量** | `GainStars(-N)` → **抛异常** | `PlayerCmd.LoseStars(N, player)` ✓ |

（「施加增益 / 减益」那种本来就分正负两档，一直是对的。）

## 二、回读也跟着补了一条

「从工程恢复」以前**认不出** `PlayerCmd.LoseStars(...)` 这一行（会当成「没认出来」丢掉那条效果）。
这一版补上：恢复出来的存档里是**额外资源量 = -1** ✓（用你这份存档验过：40 处没认出来 → 38 处）。

## 三、已经验过

| 项 | 结果 |
|---|---|
| 你这份存档**真生成 + 真编译** | `WhiteCloakMod` 工程 `dotnet build` **0 错误**（29 个警告）；`WhiteCloakPowerFatigue.cs` 里是 `LoseBlock`、`WhiteCloakPowerDarkSanctuaryDebuff.cs` 里是 `LoseStars` |
| 从工程恢复 | 「疲劳」→ 格挡 **-6** ✓、「黑暗圣所（减益）」→ 额外资源量 **-1** ✓（都原样认回来） |
| `--uicheck` | **3825 PASS / 0 FAIL**（新增 5 条：状态链上这五种填负数分别生成 LoseBlock / 失去生命 / LoseEnergy / LoseGold / LoseStars） |
| `--samplegen` | 113 → 114，**5 处既有差异**（新增 `cs/ForgeCardLibrary.cs`(V0.2.187) ＋ 4 处旧差异）—— 默认档零影响 |
| 前几版 | 同一份包：节点图目标卡选本体卡（V0.2.188）、「取几张 = 最多」在五条链上不生效（V0.2.189）、点卡牌图鉴卡死（V0.2.187）、覆盖血条（V0.2.186） |

## 四、要做的

**重新生成 + 一键构建 + 替换 mod**（必须），然后进战斗看：
「疲劳」挂上之后，每到你的回合结束**格挡会真的掉 6 点**（有格挡才掉，没格挡就是无事发生）；
「黑暗圣所（减益）」每打一张牌，额外资源量会真的少 1 点（而不是后台抛异常）。
