# V0.3.85 — 初始卡副本不再出现在百科大全

## 问题

V0.3.83 把「一张卡 × N 初始份数」拆成独立 CardModel 后，百科大全里：

```text
打击
打击 Copy2
打击 Copy3
打击 Copy4
```

显示成好几张。

## 原因

副本是独立的 `CardModel` 类，默认：

```csharp
CardModel(..., bool shouldShowInCardLibrary = true)
```

所以百科会把每个副本也当成一张新卡列出来。

## 修复

副本的构造函数生成：

```csharp
public SparkleStrikeCopy2()
    : base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy,
           shouldShowInCardLibrary: false) { }
```

原卡不变：

```csharp
public SparkleStrike()
    : base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy) { }
```

效果：

- 副本仍在初始卡组里；
- 副本仍能正常打出 / 升级 / 显示卡图；
- 百科大全只列原卡一次；
- 奖励池仍然没有副本。

## 验证

| 项 | 结果 |
|---|---|
| 副本构造函数隐藏 | 通过 |
| 原卡仍显示 | 通过 |
| SparkleMod 真工程编译 | **通过** |
| 开发版 `--uicheck` | **4930 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4929 PASS / 0 FAIL** |
