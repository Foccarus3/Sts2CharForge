# V0.2.371 — 修「消耗 1 张状态」不生效（「消耗全部」却正常）

## 原因：先取后过滤，顺序反了

你在**锻造图**上配的是：【效果】消耗卡牌，数值 1，范围 = 状态牌，取牌方式 = 随机。
以前生成出来是这一句（用你的 `Carmenmod (3).json` 里那张 `Regret1` 复现的）：

```csharp
// 修之前
var picked = CardPile.GetCards(player, PileType.Hand)
    .OrderBy(_ => Rng.NextInt(int.MaxValue))   // ① 先从整手牌里随机
    .Take(1)                                   // ② 只拿 1 张（这时候还没看是不是状态牌）
    .ToList()
    .Where(c => c.Type == CardType.Status || c.Rarity == CardRarity.Status);   // ③ 再过滤
```

也就是说：它先随便抽 1 张手牌，**再**判断这张是不是状态牌 ——
抽到的八成是普通牌 → 过滤后为空 → 什么都不消耗。

而「消耗全部」走的是另一条分支（先 `Where` 再逐张消耗），所以它正常 ——
这正是你观察到的「1 张不行、全部可以」。

## 改法

`GraphRandomPickExpr`（节点图里「从某一摞随机拿 N 张」那个表达式）现在**先过滤再取**：

```csharp
// 修之后
var picked = (choiceContext is null ? new List<CardModel>() : CardPile.GetCards(player, PileType.Hand)
    .Where(c => c.Type == CardType.Status || c.Rarity == CardRarity.Status)   // ① 先筛
    .OrderBy(_ => Rng.NextInt(int.MaxValue))
    .Take(1)                                                                  // ② 在筛过的里面随机取
    .ToList());
```

两个调用点（「消耗 / 丢弃 / 升级 / 变化 / 复制…」那几种走同一份表达式）都改了；
**没写范围**（`SpawnFilter` 空）时生成的文本和以前一模一样（谓词为空 = 不拼 `.Where`）。

## 自检

**4704 PASS / 0 FAIL**（+1 条：断言 `.Where(` 必须出现在 `.OrderBy(` **之前**）。
用你的存档复跑同一条效果，现在生成的是 `…Where(状态).OrderBy(随机).Take(1)…`。
