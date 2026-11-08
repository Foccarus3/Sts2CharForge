# V0.3.40 — 自定义状态「全体友方」施加状态错误地给了敌人

## 现象

宠物挂上 A 状态后，A 状态的效果：

1. 自己 ≥3 层 A → 减少自身 3 层 A；
2. 自己 ≥3 层 A → 给**所有队友**发活力（`VigorPower`）。

实际第 2 条把活力发给了**敌人**。

## 真因

`CustomPowerGen.EmitEffect` 的 `ApplyPower` 分支自己写了一个 `switch (applySide)`：

- 有 `Self` / `AllEnemies` / `AllUnits` / `RandomEnemies`；
- **没有 `AllAllies`**；
- 于是「全体友方」掉进 `default` → `EmitFoeBlock` → `HittableEnemies.FirstOrDefault()`，也就是第一个敌人。

生成出来的 Doris 代码就是：

```csharp
Creature? other = base.Owner.CombatState?.HittableEnemies.FirstOrDefault();
await PowerCmd.Apply<VigorPower>(choiceContext, other, 5m, base.Owner, null);
```

## 修法

给 `ApplyPower` 补上和 `TempPower` / `EmitPowerTargets` 同一份友方口径：

```csharp
foreach (Creature other in base.Owner.CombatState.Allies
    .Where(c => c.IsAlive && c != base.Owner).ToList())
    await PowerCmd.Apply<VigorPower>(choiceContext, other, 5m, base.Owner, null);
```

同时补 `Ally` / `AnyAlly`：状态链没有「玩家选中的队友」上下文，按自己处理（和临时增益同一口径）。

## 你这份 Doris 配置还有第二处要对

V0.3.40 修完后，目标会变成友方；但你的 `DorisPower12`（增幅纳米储备）里：

- 条件用的是 `DorisPower11`（挂机）；
- 扣层用的也是 `DorisPower11`。

如果 A 状态就是「拥有这些效果的那个状态」，要把：

- 「拥有某状态至少 3 层」的条件目标；
- 「清除指定状态」的 `PowerId`；

都改成 **A 状态自己**。否则即使 A 到 3 层，也会因为「挂机」不到 3 层而不触发 / 扣错状态。

## 验证

| 项 | 结果 |
|---|---|
| 自定义状态 ApplyPower → 全体友方生成 `CombatState.Allies` | PASS |
| 不再生成 `HittableEnemies.FirstOrDefault()` | PASS |
| 开发版 `--uicheck` | **4891 PASS / 0 FAIL**（+1 条 V0.3.40 断言） |
| 部署版 `--uicheck` | **4890 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里 DorisPower12 还是「第一个敌人」。
