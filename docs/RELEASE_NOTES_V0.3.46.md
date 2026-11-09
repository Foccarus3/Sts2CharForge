# V0.3.46 — Y 费卡再加一层取值兜底 + 调试日志

## 为什么 V0.3.45 还不够

V0.3.45 把 Y 改成 `cardPlay.Resources.StarsSpent`，但你的实测仍然是：

- 命中次数填 Y → 只打 1 次；
- 生效次数填 Y → 0 次。

说明 **`cardPlay.Resources.StarsSpent` 在你这次联机 / 模组组合下也是 0**。
可能来源：卡牌复制 / 自动打出路径、BaseLib 额外资源费用、或 `CardPlay` 不是标准 `PlayCardAction` 构造的。

## V0.3.46

Y 的取值按顺序兜底：

```csharp
decimal __Y = base.HasStarCostX
    ? (decimal)(
        cardPlay.Resources.StarsSpent > 0 ? cardPlay.Resources.StarsSpent
        : cardPlay.Resources.StarValue > 0 ? cardPlay.Resources.StarValue
        : base.LastStarsSpent > 0 ? base.LastStarsSpent
        : (base.Owner.PlayerCombatState?.Stars ?? 0))
    : 0m;
```

同时，开了「调试日志」时生成一行：

```text
[Y调试] spent=.../... last=... stars=... Y=...
```

如果这层兜底后仍然 Y=0，日志里这四个数会直接指出：

- 是 `StarsSpent` 0；
- 还是 `LastStarsSpent` 0；
- 还是当前 `Stars` 0（= 费用根本不走星星，而是 BaseLib / 其他模组的自定义资源）。

## 验证

| 项 | 结果 |
|---|---|
| 四层 Y 兜底生成 | PASS |
| 开发版 `--uicheck` | **4899 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4898 PASS / 0 FAIL** |

⚠ 必须**重新生成工程 + 重新构建**；进游戏后打出「锻造卡 3」，把日志里的 `[Y调试] ...` 发我。
