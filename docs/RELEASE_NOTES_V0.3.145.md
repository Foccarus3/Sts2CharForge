# V0.3.145 — 「给予关键词 / 放置卡牌」选「全部牌堆」时只动了手牌

> 这一版在 V0.3.144（多触发遗物「每累计花掉 N 点」漏声明计数基准字段）之上，包含那条修复。

## 一、问题（用户报）

> 消耗上给牌堆里的牌不生效

即：「给予卡牌关键词 = 消耗」+「从哪里选牌 = **全部牌堆**」/「取几张 = 全部」，
打出去之后**抽牌堆 / 弃牌堆里的牌一张都没加上「消耗」**（只有手牌里的被加上了）。

## 二、真因

`EmitGiveKeyword` 里「全部」和「随机 N 张」这两条分支用的是：

```csharp
PileExpr(e).Cards        // ← PileExpr 只认 Hand / Draw / Discard / Exhaust / Top
```

而「全部牌堆」（`SelectPile = "All"`）**不在 PileExpr 的分支里**，于是走了默认档 **`PileType.Hand`**
—— 生成的注释写着「给全部牌堆里符合条件的牌全部加上关键词」，代码却只遍历手牌。

（其它选牌类效果早在 V0.2.365 就改用了 `PileCardsExpr` —— 它会把 `All` 翻成
`player.PlayerCombatState.AllCards`。只有「给予关键词」和「放置卡牌」漏了。）

**「放置卡牌」是同一个坑**：它也用 `PileExpr(e).Cards`，选「全部牌堆」时同样只搬手牌里的牌。

## 三、改法

两处都改用 `PileCardsExpr`（`All` → `AllCards`；其它档位生成的文本**一字不差**）：

```csharp
// 给予关键词（全部牌堆 + 全部）
foreach (CardModel __kwCard in base.Owner.PlayerCombatState.AllCards.ToList()) {
    __kwCard.AddKeyword(CardKeyword.Exhaust);
}

// 放置卡牌（全部牌堆 + 全部）
var __place = base.Owner.PlayerCombatState.AllCards.ToList();
```

## 四、验证

`--uicheck` 新增 3 条断言：

| 断言 | 结果 |
|---|---|
| 「给予关键词 + 全部牌堆 + 全部」真的扫四摞（`PlayerCombatState.AllCards`），不再退回手牌 | 通过 |
| 单摞（弃牌堆）那一档的生成文本不变（还是 `PileType.Discard.GetPile(...).Cards`） | 通过 |
| 「放置卡牌 + 全部牌堆」也扫四摞（同一个坑） | 通过 |

```text
V0.3.145 开发版 --uicheck：5031 PASS / 0 FAIL
```

## 五、用户这边

更新到 V0.3.145 → 重新「生成工程」。存档配置不用改：

- 「给予关键词 = 消耗」+「全部牌堆」→ 现在抽牌堆 / 弃牌堆 / 手牌 / 消耗堆里的牌**全都会**加上「消耗」；
- 「放置卡牌」+「全部牌堆」同理。

（单摞的选项 —— 抽牌堆 / 弃牌堆 / 消耗牌堆 / 牌顶 —— 本来就是对的，这次没动。）

## 版本

```text
V0.3.145
```
