# V0.3.69 — 混色清除顺序：先清依赖条件的状态

## 日志里的实际顺序

```text
DARK_VINCI_MIX
DARK_VINCI_DYEING
DARK_VINCI_RED      → 染色：红 3
DARK_VINCI_TIDE     → 染色：蓝 2 + 间色：紫 5
```

这一局**没有黄色**，所以「混色：绿」本来就不会触发；触发的是红 + 蓝的「混色：紫」。

## 根因

`MixPurple` 的蓝分支生成顺序是：

```text
ClearPowerOne ColorRed
ClearPowerOne ColorBlue
```

但这两条效果的条件都是「拥有红色至少 1 层」。先清掉红色后，第二条的条件立刻变成 false，于是蓝色没有被清掉。

这不是清除实例找不到的问题，是**效果顺序把自己的条件拆掉了**。

## 修复

同一个触发时机里有多个 `ClearPowerOne` 时，按条件依赖排序：

- 如果清除 A 会移除「清除 B 的条件所依赖的状态」，则 **B 必须排在 A 前面**；
- 只调整 `ClearPowerOne` 在原有槽位里的相对顺序，非清除效果的位置完全不动；
- 有环形依赖时保持原顺序，不丢效果。

修复后 `MixPurple` 蓝分支变成：

```csharp
await PowerCmd.Remove<ColorBlue>(base.Owner);
await PowerCmd.Remove<ColorRed>(base.Owner);
```

两条的条件都还在，红蓝都会被清掉。

同类问题也修到：

- `MixOrange` 的「清黄 + 清红」；
- `MixGreen` 的「清蓝 + 清黄」。

## 验证

| 项 | 结果 |
|---|---|
| `MixPurple` 蓝分支 | 先清蓝、后清红 |
| `MixOrange` 红分支 | 先清红、后清黄 |
| 开发版 `--uicheck` | **4915 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4914 PASS / 0 FAIL** |

⚠ 必须重新生成工程。
