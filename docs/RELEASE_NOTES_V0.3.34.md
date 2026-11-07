# V0.3.34 — 条件插件「本回合失去过生命值」（已从生成器剥离）

> 按用户要求：这个插件**不并入生成器**，只作为独立插件提供。
> 生成器版本仍是 **V0.3.34**（未升级版本）；更新包已重新打包，里面**不含**这个插件。

## 独立插件

文件：`本回合失去过生命值_插件_v1.0.zip`

解压后是：

```
本回合失去过生命值/
  plugin.json
  README.md
```

把它放进生成器的 `plugins\` 目录，点「重新加载插件」，卡牌条件里就会出现「本回合失去过生命值」。

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

## 使用提醒

卡牌效果上选这条后，如果只想限制**这一条效果**，把「不满足时」设为「跳过这一条」；
选「整张牌打不出去」时，本回合没掉过血这张牌会不可打。

## 验证

| 项 | 结果 |
|---|---|
| 独立插件在宿主里加载 | PASS（`本回合失去过生命值`） |
| 卡牌条件选它 → 生成 `DamageReceivedEntry` + `UnblockedDamage > 0` + `HappenedThisTurn` | PASS |
| 剥离后开发版 `--uicheck` | **4882 PASS / 0 FAIL** |
| 剥离后部署版 `--uicheck` | **4881 PASS / 0 FAIL** |

⚠ 生成器更新包本身不含这个插件；需要它请单独解压插件 zip 到 `plugins\`。
