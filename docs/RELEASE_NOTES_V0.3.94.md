# V0.3.94 — 修复锻造图 `ThisCardExhaust` 一直读到 0

## 日志

```text
[模组调试] 卡牌「锻造卡 5」(SparkleCard46) → 打出第 1/1 次
[模组调试] ...【效果】施加增益/减益...；施加量=0
```

同一张图里：

```text
【效果】消耗卡牌：Amount=6，Pick=All
【效果】施加状态：AmountFormula=ThisCardExhaust
```

## 根因

`ThisCardExhaust` 的定义是：

> 本张牌这一次打出**消耗的卡牌数**（只算「消耗卡牌」这条效果）

但锻造图生成器里的 `EmitCardPick` 只生成了：

```csharp
foreach (CardModel __fgCard in ...) await CardCmd.Exhaust(choiceContext, __fgCard);
```

**没有给 `ForgeThisCardExhaust` 累加**。所以后面的：

```csharp
decimal __fgAmt = ((decimal)this.ForgeThisCardExhaust);
```

永远是 0。

## 修复

锻造图里只要这张图引用了 `ThisCardExhaust`：

- 「消耗卡牌」数值 0（消耗自己）→ 消耗后 `+= 1m`；
- 「取几张 = 全部」→ 每消耗一张 `+= 1m`；
- 指定 / 随机 N 张 → 每张 `+= 1m`。

生成结果：

```csharp
foreach (CardModel __fgCard in (CardPile.GetCards(__fgPlayer, PileType.Hand)).ToList())
{
    await CardCmd.Exhaust(choiceContext, __fgCard);
    ForgeThisCardExhaust += 1m;
}
```

## 重要语义

`ThisCardExhaust` **不是**“这张牌是否消耗”。

它表示：

```text
这一次打出消耗了几张牌
```

- 手牌空 / 消耗 0 张 → 0；
- 消耗 6 张 → 6；
- 如果你要“这张牌自己消耗”，请勾卡牌的 **消耗** 关键词（`Exhaust`），不要用这个读数。

## 验证

| 项 | 结果 |
|---|---|
| SparkleMod 重新生成 | 通过 |
| 生成代码出现 `ForgeThisCardExhaust += 1m` | 通过 |
| SparkleMod 真工程编译 | 通过 |
| 开发版 `--uicheck` | **4937 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4936 PASS / 0 FAIL** |
