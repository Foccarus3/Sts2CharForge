# V0.3.34 — 插件：条件「本回合失去过生命值」

## 插件位置

`plugins\本回合失去过生命值\plugin.json`

条件 Id：`HpLostThisTurn`

## 判据

```csharp
base.CombatState is not null
&& CombatManager.Instance.History.Entries
    .OfType<DamageReceivedEntry>()
    .Any(e => e.Receiver == {self}
           && e.Result.UnblockedDamage > 0
           && e.HappenedThisTurn(base.CombatState))
```

- 只看**自己**：`{self}` 按所在链替换成 `base.Owner.Creature`（卡牌 / 遗物）或 `base.Owner`（状态）；
- 只看**真正掉到血上**的部分：`UnblockedDamage > 0`；
- 包含「失去生命」/ 中毒这类穿盾掉血（它们也走 `CreatureCmd.Damage` 并记进 `DamageReceivedEntry`）；
- 敌人掉血不算；
- 全被格挡 / 0 伤害的那一下不算。

## 出现位置

- 卡牌页 → 效果 → 条件选项；
- 遗物页 → 条件选项；
- 自定义状态 → 触发时机下面的条件；
- 锻造页 → 【条件】模块。

## 验证

| 项 | 结果 |
|---|---|
| 插件被加载 | PASS（`本回合失去过生命值`） |
| 卡牌条件选它 → 生成 `DamageReceivedEntry` + `UnblockedDamage > 0` + `HappenedThisTurn` | PASS |
| 开发版 `--uicheck` | **4884 PASS / 0 FAIL**（+2 条 V0.3.34 断言） |
| 部署版 `--uicheck` | **4883 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里没有这个插件条件。
