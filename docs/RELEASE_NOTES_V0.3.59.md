# V0.3.59 — 「减少卡牌费用」范围选「不限」不再变成空集

## 问题

效果「减少指定卡牌类别的费用」里，范围选**不限**（`SpawnFilter` 空）时：

```csharp
ScopeKind = ""
Ids = []
```

生成出来的 `Matches(card)` 先跳过所有 `case`，再遍历空 `Ids`，最后 `return false` ——
结果**一张牌都不减**。

## 修改

`CostDownPowerSource` 的 `Matches` 开头补：

```csharp
if (ScopeKind.Length == 0 && Ids.Length == 0) return true;
```

即：

- 不限（空）= 所有牌；
- 攻击 / 技能 / 能力 / 诅咒 / 状态 / 任务 = 按类型；
- 组 / 自定义关键词 = 按生成期展开的 ModelId 名单。

## 验证

- 自检新增断言：空范围 `Matches` 返回 `true`；
- 开发版 `--uicheck` **4912 PASS / 0 FAIL**；
- 部署版 `--uicheck` **4911 PASS / 0 FAIL**。

⚠ 旧工程需要重新生成；`CostDownPower.cs` 是生成时才写进去的。
