# V0.3.104 — 插件对象可以进「作用对象」下拉

## 结论

可以，但不是只加下拉项 —— `TargetSide` 是生成契约，必须同时接上：

- 卡牌 / 遗物 / 自定义状态 / 锻造图；
- `TargetType` 与判空；
- 卡面文案 / 校验 / 回读标记。

V0.3.104 已完成。

## 插件写法

```jsonc
"units": [
  {
    "id": "ExceptTargetEnemies",
    "display": "指定敌人以外的所有敌人",
    "foes": "{combatState}.HittableEnemies.Where(c => c != {foe})",
    "single": "",
    "kinds": [ "Damage", "Block", "Heal", "HpLoss", "MaxHp",
               "ApplyPower", "TempPower", "Stun" ],
    "needsChosenTarget": true
  }
]
```

- `kinds` 空 = 老行为（只进锻造图【对象】）；
- `needsChosenTarget = true` → 卡牌 `TargetType.AnyEnemy` + 判空；
- 只能用 `{self}` / `{foe}` / `{combatState}`；
- `id` 不能和 7 个内置作用对象重名。

## 已接生成端

| 链 | 覆盖 |
|---|---|
| 卡牌 | `EmitPerCreature`（Block/Heal/HpLoss/MaxHp/Clear*/…）、`EmitAttackCore`、`EmitStun`、ApplyPower、TempPower |
| 遗物 | `EmitPerCreature`、Damage、ApplyPower、TempPower、Stun |
| 自定义状态 | `EmitPowerTargets`、Damage、ApplyPower、TempPower |
| 锻造图 | `TargetSideUnit` |
| 药水 | 只接回读标记 / 卡面文案；实际打谁仍由药水「作用目标」决定（校验器提示） |

## 回读

插件档位效果前会写：

```csharp
// CET:TargetSide=ExceptTargetEnemies
```

`ProjectRecovery` 原样认回，不会退成 Enemy / AllEnemies。

## 校验

- 插件未加载 / 禁用 → **错误**，不静默回退；
- `kinds` 不支持该效果种类 → **错误**；
- `needsChosenTarget` 用在遗物 / 状态 / 药水 → **警告**；
- 药水 → **提示**；
- 下回合生效 + 插件档位 → **错误**（MVP 不支持）。

## 验证

| 项 | 结果 |
|---|---|
| 插件 `kinds` 进 Damage 下拉 | 通过 |
| Damage 卡牌 `TargetType.AnyEnemy` | 通过 |
| `foreach __pluginUnit` / `.Targeting(__pluginUnit)` | 通过 |
| 遗物 / 状态链生成 | 通过（探针） |
| 开发版 `--uicheck` | **4951 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4950 PASS / 0 FAIL** |
