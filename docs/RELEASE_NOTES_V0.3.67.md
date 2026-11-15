# V0.3.67 — 延迟状态里的「放置卡牌」不再生成 `CardPileCmd.Add(null, ...)`

## 问题

`DelayedPowers.cs` 里出现：

```text
error CS0121
CardPileCmd.Add(CardModel, ...)
和
CardPileCmd.Add(IEnumerable<CardModel>, ...)
二义性
```

## 根因

「下回合生效」的延迟状态里没有「这张牌自己」，但生成器仍把这条效果按卡牌链发射：

```csharp
// 卡牌链：selfExpr = "this"
await CardPileCmd.Add(this, PileType.Draw, CardPilePosition.Top);
```

进入延迟状态后，`this` 被统一替换成 `null`，于是变成：

```csharp
await CardPileCmd.Add(null, PileType.Draw, CardPilePosition.Top);
```

新 API 同时有 `Add(CardModel, ...)` 和 `Add(IEnumerable<CardModel>, ...)`，`null` 两个都能匹配 → CS0121。

## 修改

- `EmitCardEffectOnce` 在 `inDelayedBody` 时给 `PlaceCard` 传 `selfExpr: null`；
- 数值 0（= 这张牌自己）在延迟状态里没有意义，生成一行说明并跳过，不再生成 `Add(null, ...)`；
- `EmitPlaceCard` 也把字符串 `"null"` 当成「没有这张牌自己」处理（兜底其他入口）。

## 验证

| 项 | 结果 |
|---|---|
| 延迟 `PlaceCard` 数值 0 | 不再生成 `CardPileCmd.Add(null, ...)` |
| 延迟 `PlaceCard` 数值 ≥ 1 | 仍走 `__placeCard` / `__place` 列表 |
| 开发版 `--uicheck` | **4915 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4914 PASS / 0 FAIL** |

⚠ 旧工程必须重新生成；旧 `DelayedPowers.cs` 不会自己变。
