# V0.2.350 — 修「构建失败 CS0101 + CS0111」：延迟状态类名撞车、同一个类写了两遍

你贴的报错：

```
cs\DelayedPowers.cs(41,21): error CS0101: 命名空间"LunaMod"已经包含"LunaForgeDelayedTempPowerSowSowPowerSelf"的定义
cs\DelayedPowers.cs(46,32): error CS0111: 类型"LunaForgeDelayedTempPowerSowSowPowerSelf"已定义了一个名为"AfterPlayerTurnStart"的成员
```

## 原因

`cs\DelayedPowers.cs` 是「下回合生效」的那一堆延迟状态类。它会按**去重键**把不同的效果各生成一个类，
而类名由 `Naming.DelayedPowerSuffix` 决定 —— 这两处的「判据」**不是同一套**：

| | 去重键里有没有 | 类名里有没有 |
|---|---|---|
| 状态（`PowerId`） | ✅ | ✅ |
| 作用对象（`TargetSide`） | ✅ | ✅ |
| 数值**正负**（`Amount < 0`） | ✅ | ❌ |
| 「临时增益什么时候撤」（`TempPowerReverse`） | ✅ | ❌ |

所以**两条只差「正负」或「什么时候撤」的「下回合生效的临时增益」**，去重键不同（会被当成两条去生成），
类名却算成同一个 ⇒ 同一个类在 `DelayedPowers.cs` 里写两遍 ⇒ `CS0101` + `CS0111`，整个构建失败。

## 改法（两层）

1. **把这两个维度编进类名**（`Naming.DelayedPowerSuffix`）：`Amount < 0` 加 `Abs`，
   「下回合才开始撤」加 `NextTurn`。
   **默认值一个字都不加**（正数 / 本回合撤）→ 你现有存档的类名一个字节都没变。
2. **发射时按类名兜底去重**（`CSharpCodeGen.DelayedPowersSource`）：万一以后又冒出一条
   「去重键不同但名字相同」的组合，只会生成 **一个类 + 一行注释**（写明被跳过的效果），不会再炸构建。

## 拿你的存档验过

用你这份 `LunaMod.json` 跑了一遍延迟状态的生成，现在输出 **3 个互不相同的类**：

```
LunaForgeDelayedSoarPower
LunaForgeDelayedTempPowerSoarPowerSelf
LunaForgeDelayedTempPowerSoarPowerSelfNextTurn
```

（改之前后两个会算成同一个名字 `…SoarPowerSelf`，就是你踩到的那个坑。报错里那个 `SowSowPower` 是你更早一版的存档，
机制完全一样。）

## 自检

**4636 PASS / 0 FAIL**（+3 条：只差「什么时候撤」的两条效果现在类名不同 / 两个类各只出现一次 /
万一还是撞名也只生成一个类 + 注释）。
