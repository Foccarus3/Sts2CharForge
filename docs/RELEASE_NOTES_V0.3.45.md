# V0.3.45 — Y 费锻造卡：Y 改用本次实际消耗的额外资源量

## 现象

「锻造卡 3」（`SparkleCard44`，额外资源量费用 = Y，伤害节点生效次数 = Y）：

- 打出时确实扣掉了 Y 点额外资源量；
- 但一次伤害都没有。

## 真因

生成代码：

```csharp
decimal __Y = base.HasStarCostX ? (decimal)base.ResolveStarXValue() : 0m;
int times = Math.Max(0, (int)(__Y));
for (int t = 0; t < times; t++) { await DamageCmd.Attack(1m)...; }
```

`ResolveStarXValue()` 在某些卡 / 某些模组补丁的组合下读回 0，于是循环 0 次；
费用照扣，但伤害一次都不打。

## 修法

Y 直接用本体的 `cardPlay.Resources.StarsSpent`：

```csharp
decimal __Y = base.HasStarCostX ? (decimal)cardPlay.Resources.StarsSpent : 0m;
```

`PlayCardAction` 在 `OnPlay` 之前已经把这次实际扣掉的资源量放进 `ResourceInfo.StarsSpent`，
不会受 X 值修改 / 读取时机影响。

没有 `cardPlay` 的、嵌进卡牌钩子的图仍退回 `ResolveStarXValue()`。

## 验证

用你的 `SparkleMod.json` 真跑 `CSharpCodeGen.CardSource`：

```text
decimal __Y = base.HasStarCostX ? (decimal)cardPlay.Resources.StarsSpent : 0m;
int __fgTimes... = Math.Max(0, (int)(__Y));
for (int t = 0; t < __fgTimes...; t++)
```

| 项 | 结果 |
|---|---|
| Y 费卡 Y = `Resources.StarsSpent` | PASS |
| 次数循环仍按 Y | PASS |
| 开发版 `--uicheck` | **4899 PASS / 0 FAIL**（+1 条 V0.3.45 断言） |
| 部署版 `--uicheck` | **4898 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程的 `SparkleCard44` 仍用 `ResolveStarXValue()`。
