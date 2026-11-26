# V0.3.120 — 插件效果支持可配置「升级增量」

## 插件清单

插件效果现在可以声明：

```json
{
  "kind": "PluginHealAndBlock",
  "display": "治疗并格挡",
  "var": "Heal",
  "upgrade": true,
  "emit": "..."
}
```

`"upgrade": true` 后：

- 卡牌效果编辑器显示「升级增量」输入框；
- 玩家可以每条效果单独填；
- 不写 = 老行为，完全不变。

## emit 占位符

新增：

```text
{upgradeAmount}    → 这条效果的升级增量，例如 2m
{upgradedAmount}   → 升级后的实际数值
```

例：

```csharp
await CreatureCmd.Heal({self}, {upgradedAmount});
```

## var 的卡面显示

插件声明了 `var`：

```json
{ "kind": "PluginHeal", "var": "Heal", "upgrade": true }
```

生成端会：

1. 声明 `new DynamicVar("Heal", 数值)`；
2. 在 `OnUpgrade` 里生成 `base.DynamicVars["Heal"].UpgradeValueBy(增量)`；
3. 卡面 `{Heal:diff()}` 自动显示升级后的数字。

没有 `var` 时：

- `{upgradedAmount}` 仍按 `base.IsUpgraded` 改实际效果；
- 卡面没有动态变量，不会自动显示升级后数字。

## 范围

- 只对**卡牌链**生成 `base.IsUpgraded`；
- 遗物 / 药水 / 状态没有升级概念，`{upgradedAmount}` 原样使用基础数值；
- 非卡牌链填升级增量不会自动升级（没有 OnUpgrade 落点）。

## 验证

| 项 | 结果 |
|---|---|
| `upgrade: true` 显示升级增量栏 | 通过 |
| 有 var → `UpgradeValueBy(2)` | 通过 |
| `{upgradedAmount}` 生成 `base.IsUpgraded` | 通过 |
| 没声明 upgrade 的老插件行为不变 | 通过 |
| 开发版 `--uicheck` | **4968 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4967 PASS / 0 FAIL** |
