# V0.3.84 — 初始卡副本继承卡图

## 问题

V0.3.83 把一张卡 + 多份初始复制拆成独立 CardModel 后：

```text
原卡有卡图
Copy2 / Copy3 / Copy4 没有卡图
```

## 原因

卡图是按**类名**查的：

```csharp
p.Art.CardPortraits[cls]
```

副本换了类名（`SerenStrikeCopy2`），但 `CardPortraits` 里只有原卡的键 `SerenStrike`，所以副本回退成占位图。

## 修复

`StarterCopyGen` 生成副本时，把原卡的卡图映射同时复制给副本：

```csharp
if (copy.Art.CardPortraits.TryGetValue(baseName, out var portrait))
    copy.Art.CardPortraits[candidate] = portrait;
```

然后 `ArtGenerator` 按副本类名查时就能拿到同一张图。

## 验证

本机 SparkleMod 生成的：

```text
sparkle_strike.png
sparkle_strike_copy2.png
```

SHA256 完全一致。

| 项 | 结果 |
|---|---|
| 副本卡图继承 | 通过 |
| SparkleMod 真工程编译 | **通过** |
| 开发版 `--uicheck` | **4928 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4927 PASS / 0 FAIL** |
