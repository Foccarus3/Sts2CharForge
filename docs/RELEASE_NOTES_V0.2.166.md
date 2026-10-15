# V0.2.166 更新说明

> 查了你的存档（部署版存档目录里的 `WendaoMod.json`，自定义状态「击晕值」）：
> **不是游戏的问题，也不是生成器算错 —— 是这一条时机的「盯」和效果的「作用对象」都选反了**，
> 生成出来的守卫永远不成立，所以到 100 也没击晕。工具这一版把这两个坑做成**配置检查**（会直接告诉你）。

## 一、你现在的配置生成出来是什么（真因）

你配的是：

| 项 | 你填的 | 生成出来的守卫 / 语句 |
|---|---|---|
| 触发时机 | 某个状态层数到达 N 层时（N=100） | `AfterPowerAmountChanged(...)` |
| 盯 | **任意敌人** + 状态 = 击晕值（`WendaoPower1`） | `power.Owner != base.Owner && …HittableEnemies.Contains(power.Owner) && power is WendaoPower1` |
| 效果 | 击晕（作用对象 = 指定敌人） | `foe = base.Owner.CombatState.HittableEnemies.FirstOrDefault(); CreatureCmd.Stun(foe);` |

问题在**第二行**：状态里的「自己」= **挂着这个状态的那个单位**（`base.Owner`，对减益来说就是那只敌人），
而「盯：任意敌人」生成的要求是「被盯的那份状态在**别的**生物身上」（`power.Owner != base.Owner`）——
「击晕值」自己涨到 100 那一下，`power.Owner` 就是 `base.Owner`，**这个条件永远不成立**，
所以整条 `if` 一次都不会进（只有「别的敌人身上也有一份击晕值、它跨过 100」时才会触发）。

顺便说清第二条：状态里的「敌人」指的是**玩家视角的敌方那一侧** —— 「指定敌人」取的是那一侧里
**第一个可打的敌人**，不一定是身上带着这个状态的那只（场上多只敌人时会晕错）。

## 二、怎么改（改完就能用）

同一个状态上改两处：

1. **「盯」从「任意敌人」改成「自己」**（状态 = 击晕值不变；「自己」在状态链里 = 挂着这个状态的那个单位）；
2. 「击晕」的**作用对象从「指定敌人」改成「自己」**（状态挂在敌人身上时「自己」就是它 → 击晕带着这个状态的那只）。

改完生成出来的代码（实测，0 编译错误）：

```csharp
public override async Task AfterPowerAmountChanged(..., PowerModel power, decimal amount, ...) {
    if (power.Owner == base.Owner && power is WendaoPower1 && power.Amount >= 100m && power.Amount - amount < 100m) {
        // 层数跨过 100 那一下才触发
        if (base.Owner.GetPowerAmount<WendaoPower1>() >= 100) {
            await CreatureCmd.Stun(base.Owner);      // ← 击晕「带着这个击晕值」的那只
        }
    }
}
```

（原来那句是 `power.Owner != base.Owner`，所以永远进不去。）

## 三、这一版修的两个工具问题

1. **「作用对象 = 自己」在自定义状态链上生成的是 `base.Owner.Creature` → 编译不过（CS1061）**
   （`Creature` 上没有 `Creature` 属性；卡牌 / 遗物 / 药水的 `base.Owner` 才是 Player）。
   现在状态链生成 `CreatureCmd.Stun(base.Owner)`，卡牌 / 遗物 / 药水**逐字不变**（还是 `.Creature`）。
   也就是说：你要改的第二步，在旧版上是会直接编译失败的。
2. **配置检查新增两条**（就是你这个坑）：
   - **警告**：触发时机盯「任意敌人」、而盯的那个状态**就是这条时机所在的状态自己**
     → 说清「自己涨到 N 那一下不会触发」并提示改成「盯：自己」；
   - **提示**：**减益**状态里的「击晕」作用对象选了「指定敌人 / 全体敌人 / 随机敌人」
     → 说清那取的是「玩家视角敌方那一侧里第一个可打的敌人」，想击晕带着这个状态的那只请改成「自己」。

> 实测：拿你这份存档跑配置检查，上面两条**都报出来了**（第二条就是你现在这条效果）。

## 四、验证

- `dotnet build Sts2CharForge.sln -c Debug` → **0 错误**；
- **真存档验证**：把 `WendaoMod.json` 复制一份、只把那两处改成「自己」，`forge --profile … --out … --build`
  → **生成的模组工程 0 错误**，守卫与击晕语句就是上面那段；
- `--uicheck`：Debug **3670 PASS / 0 FAIL**（新增 5 条：两条校验 + 盯自己时的守卫生成 +
  状态链「击晕自己」=`base.Owner` + **对照**卡牌链照旧 `base.Owner.Creature`）；
- **默认输出不变（红线①）**：`--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**。

> 另外顺手看到你存档里两条旧警告（和这次无关，但会影响你想要的效果）：
> 「遗物『吃』的『放置卡牌』填了 0」—— 0 = 「这张牌自己」只在**卡牌**上成立，遗物上这条不做任何事；
> 「额外资源量图标不存在：`D:\Users\lenovo\Desktop\…png`」—— 那是**另一台机器**上的路径，
> 这台机器上找不到，重新选一张就行。
