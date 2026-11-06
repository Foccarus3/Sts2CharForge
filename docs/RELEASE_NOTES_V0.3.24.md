# V0.3.24 — X 费卡在 0 能量时仍执行一次

用 `DarkVinci (1).json` 的「积墨（DarkVinciAccumulatedInk）」/「燎原（DarkVinciWildfire）」复现。

## 真因

这两张卡的锻造图里，【效果】的「生效次数」写的是公式 `X`。生成出来的循环上界是：

```csharp
int __fgTimes05224e = Math.Max(1, (int)(__X));
for (int t = 0; t < __fgTimes05224e; t++) { ... }
```

`Math.Max(1, ...)` 把 X=0 抬成了 1 —— 所以 0 能量打出 X 费卡，仍然会：
- 给随机敌人挂一次 `SecondaryPurple`（积墨）；
- 或造成一次 5 点伤害（燎原）。

这不是费用没扣，是**循环上界被夹到了 1**。

## 修法

`ForgeGraphGen` 里「生效次数 = 运行时公式」的局部变量改成：

```csharp
int __fgTimes = Math.Max(0, (int)(expr));
```

X=0 → 循环 0 次，整条效果不执行。
`Hits`（命中次数）的公式仍按至少 1 处理（攻击命中次数 0 没有意义，且本体 `WithHitCount(0)` 的行为不可靠），
本次只改「生效次数」这一栏。

## 实测

用 `DarkVinci (1).json` 重新生成：

```csharp
int __fgTimes05224e = Math.Max(0, (int)(__X));
for (int t = 0; t < __fgTimes05224e; t++)
```

积墨 / 燎原的未升级、升级后两张布都已确认是 `Math.Max(0, ...)`。

## 验证

| 项 | 结果 |
|---|---|
| `dotnet build Sts2CharForge.sln -c Debug` | **0 错误** |
| `--uicheck`（开发版） | **4860 PASS / 0 FAIL**（+1 条 V0.3.24 断言） |
| 部署版 `--uicheck` | **4859 PASS / 0 FAIL** |
| 真编译探针 `sacprobe vars` | 锻造卡「生效次数 = X」真编译 **0 错误** |

⚠ 需要重新生成 DarkVinci 工程才会生效。
