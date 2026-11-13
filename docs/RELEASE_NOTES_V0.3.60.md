# V0.3.60 — 旧插件 API 兼容层（v0.113）

## 这次构建日志里的错误

```text
PilePlayRuntime.cs(72,29): CS1061 “Rng”未包含“Shuffle”的定义
TakeoverPower.cs(140,35): CS0117 “CardSelectCmd”未包含“PushSelector”的定义
NinefoursevenCard59.cs(48,81): CS1739 “LoseMaxHp”没有名为“isFromCard”的参数
```

## 处理

### 1. 生成端：`LoseMaxHp` 新签名

V0.3.58 已修：第四参数从 `bool isFromCard` 改成 `CardModel? cardSource`。

```text
卡牌 = this
状态 / 遗物 / 药水 = null
锻造图 = ctx.CardSelfExpr
```

### 2. 插件旧 API：自动兼容

只改**生成工程里的插件副本**，不改用户原插件目录：

| 旧插件写法 | 生成工程里改成 |
|---|---|
| `rng.Shuffle(list)` | 自动生成的 `Sts2LegacyRngShuffleExtensions.Shuffle(...)` |
| `CardSelectCmd.PushSelector(...)` | `LegacyPluginCompat.PushSelector(...)` |
| `CardSelectCmd.UseSelector(...)` | `LegacyPluginCompat.UseSelector(...)` |
| `CardSelectCmd.SetTestSelector(...)` | `LegacyPluginCompat.SetTestSelector(...)` |

`LegacyPluginCompat.PushSelector` 内部转调 v0.113 的：

```csharp
RunManager.Instance.CardSelectorStack.PushSelector(player, selector)
```

插件里旧的 `CardSelectCmd.FromHand` / `FromCombatPile` 等**不动**。

兼容文件只在工程里存在插件 C# 文件时生成：

```text
cs/Sts2LegacyPluginCompat.cs
```

### 3. 已一并检查/保留的 v0.113 适配

- `MonsterModel.IsInteractable`
- `NCreature.SetIsInteractable`
- `AfterDeath(..., CardModel? source, ...)`
- `AfterThisWasTransformedIntoInternal`
- 卡牌 / 状态 / 遗物 / 药水 / 锻造五条链统一 `LoseMaxHp`

## 验证

| 项 | 结果 |
|---|---|
| 带 `rng.Shuffle` + `CardSelectCmd.PushSelector` 的探针插件 | 真编译 **0 错误** |
| 带 `MaxHp -5` 的探针卡 | 真编译 **0 错误** |
| 开发版 `--uicheck` | **4913 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4912 PASS / 0 FAIL** |

⚠ 旧工程要重新生成；兼容层只在生成时写进新工程。
