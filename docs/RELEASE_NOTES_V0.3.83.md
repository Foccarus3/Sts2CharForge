# V0.3.83 — 多份初始卡自动拆成独立 CardModel

## 问题

多人实测（SerenMod）：

```text
Strike  ×4
Defend  ×4
```

生成出来是同一个 CardModel 类重复引用：

```csharp
ModelDb.Card<SerenStrike>(),
ModelDb.Card<SerenStrike>(),
...
```

主机 / 客户端对同一张卡的多份实例解析不一致，第 4 回合 checksum 分叉：

```text
主机：Block 5 / Energy 1 / 手牌 3
客户端：Block 10 / Energy 0 / 手牌 2
```

给每份复制品起不同类名后正常。

## 修复

新增 `StarterCopyGen`：

- 一张卡 `StartingCopies > 1`、且在初始卡组；
- 生成时把它展开成：

```text
原卡（1 份）
+ N-1 个独立副本 CardModel
```

例如：

```csharp
ModelDb.Card<SerenStrike>(),
ModelDb.Card<SerenStrikeCopy2>(),
ModelDb.Card<SerenStrikeCopy3>(),
ModelDb.Card<SerenStrikeCopy4>(),
```

副本：

- 显示名 / 描述 / 效果和原卡相同；
- 类名唯一；
- `StartingCopies = 1`；
- 不进奖励池（`InCardPool = false`）；
- 只改**本次生成的临时 profile**，用户存档里的 `StartingCopies` 不变。

## 从工程恢复

副本代码里写：

```csharp
// CET:StarterCopyOf=SerenStrike
```

恢复工程时不会把它们当成新卡，而是把份数并回原卡：

```text
10 张牌仍是 10 张，不会变成 18 张
```

## 验证

| 项 | 结果 |
|---|---|
| 一张卡 ×3 → 3 个不同 CardModel | 通过 |
| StartingDeck 原卡 + 副本 | 通过 |
| 副本本地化 | 通过 |
| 生成 → 回读张数一致 | 通过 |
| SparkleMod 真工程编译 | **通过** |
| 开发版 `--uicheck` | **4927 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4926 PASS / 0 FAIL** |
