# V0.3.65 — 「全体友方」不再默认包含自己，并支持多人队友

## 问题

多人卡「月光-第一乐章」的锻造图【对象】= **全部友方**。效果实际给**所有友方**（包括自己）施加了力量 / 敏捷，没有勾「包括自己」也给自己加了状态。

## 原因

两处语义不一致：

1. 卡牌页的「全部友方」默认是**不含自己**（`Allies.Where(c => c != self)`）；
2. 锻造节点图的 `AllAllies` 用的是 `ForgeFormulaRuntime.AllAllies(player)`，旧实现只返回：
   ```text
   自己 + 自己的召唤物
   ```
   既包含自己，又**不包含多人队友**。

## 修改

### 节点图【对象】= 全部友方

生成表达式改成：

```csharp
ForgeFormulaRuntime.AllAllies(__fgPlayer)
    .Where(c => c.IsAlive && c != __fgSelf)
    .ToList()
```

- 默认不含自己；
- 卡牌效果上勾了「包括自己」才会保留自己；
- 勾「不包括召唤物」时再加 `c.PetOwner is null`。

### 运行时 `AllAllies`

从「自己 + 自己的召唤物」改成：

```csharp
combat.Allies.Where(c => c is not null && c.IsAlive).ToList()
```

即同一侧所有存活单位：

- 多人：其他玩家 + 他们的召唤物 + 自己的召唤物；
- 单人：自己 + 召唤物（再按上面的 `c != __fgSelf` 排除自己）。

## 验证

| 项 | 结果 |
|---|---|
| `CarmenMoonlightTheFirstMovement` 生成源码 | 力量 / 敏捷 TempPower 的目标为 `AllAllies(...).Where(c => c.IsAlive && c != __fgSelf)` |
| 多人队友 | `CombatState.Allies` 覆盖同侧其他玩家 |
| 开发版 `--uicheck` | **4915 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4914 PASS / 0 FAIL** |

⚠ 必须重新生成工程；旧代码里还是 `AllAllies(__fgPlayer)` 全量。
