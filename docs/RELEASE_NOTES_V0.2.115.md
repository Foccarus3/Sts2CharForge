# V0.2.115 更新说明

> 这批需求里的第 ⑧ 条：**选牌 / 取牌方式里增加一个「牌顶」**。

## 一、「从哪里选牌」多了「牌顶」这一档

「卡牌」页（遗物 / 药水 / 状态那边同样）→ 效果编辑器里那个「**从哪里选牌**」下拉，
原来只有 手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆，现在多了：

> **牌顶（抽牌堆最上面那张，不用挑、也不随机）**

选它的意思是：**直接从抽牌堆顶拿牌，不弹选牌界面、也不随机抓**；要 N 张就从上往下依次拿。
本体抽牌堆的 `Cards` 索引 0 就是顶（本体抽牌时取 `Cards.FirstOrDefault()`、「预见」看的也是前 N 个、
`MoveToTop` 是 `Insert(0)`），所以这就是本体口径的「牌顶」。

八种「选牌」效果全都支持：**消耗卡牌 / 丢弃卡牌 / 复制卡牌 / 升级卡牌 / 变化卡牌 / 重放卡牌 /
给予关键词 / 回合结束时自动打出**。

生成出来的取法（以消耗为例）：

```csharp
// 从牌顶（抽牌堆最上面那张）拿 1 张消耗（不用挑）
for (int __pickIdx = 0; __pickIdx < 1; __pickIdx++) {
    CardModel? pick = PileType.Draw.GetPile(base.Owner).Cards.FirstOrDefault();
    if (pick is null) break;
    await CardCmd.Exhaust(choiceContext, pick);
}
```

- **不弹选牌界面**（这一档不会生成 `CardSelectCmd.FromHand / FromCombatPile`）；
- **不随机**（不会生成 `Rng...NextItem(PileType.Draw…`）；
- 「重放 / 回合结束打出」这两条是「拿最多 N 张」，走的是 `…Cards.Take(N).ToList()`（同样从顶往下）；
- 老存档（手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆）生成的代码**逐字不变**（红线照旧）。

## 二、回读：不会被降级成「抽牌堆」

「牌顶」用的也是抽牌堆，光看 `PileType.Draw` 分不出来，所以：

- 那些自包含效果（复制 / 升级 / 重放 / 回合结束打出 / 给关键词…）的标记行里本来就有
  `CET:Pile=<选的那一摞>` → 现在写的是 `CET:Pile=Top`，回读直接认；
- 消耗 / 丢弃 / 变化这三条**没有标记行**，靠「代码里怎么取的」反推 —— 给回读加了一条规则：
  `PileType.Draw.GetPile` 和 `FirstOrDefault()`（或 `.Take(`）**出现在同一行**才算「牌顶」
  （`FirstOrDefault()` 到处都有，只看它会把别的效果认错）；
- 校验器那条「复制卡牌只能从战斗牌堆里复制」的白名单也把 `Top` 收进来了（否则一勾就被拦住）。

## 三、验证

- `--uicheck`：开发版与发布版都是 **2628 PASS / 0 FAIL**（新增 8 条：判据 / 八种效果都从牌顶拿 /
  不弹选牌界面 / 不随机 / 标记 `CET:Pile=Top` / 生成→回读八条全认回 `Top` / 0 没认出来；
  另有 4 条旧的「四摞」断言改成「四摞 + 牌顶」）；
- 探针新增 `RunPileTop`（八种效果各一条 `SelectPile = "Top"`）→ 生成：`从牌顶拿的行数 = 9`、
  `弹选牌界面 = False`、`随机抓 = False`；`recovered = ExhaustCard:Top/DiscardCard:Top/CopyCard:Top/
  UpgradeCard:Top/TransformCard:Top/ReplayCard:Top/GiveKeyword:Top/TurnEndPlay:Top`；
  工程 **真 `dotnet build` 0 错误**；整轮探针 **28 处 `HasUnparsed = False` / 0 处 True**；
- 打包：`Sts2CharForge_更新包_V0.2.115.zip`。

## 四、这批剩余的

| # | 需求 | 状态 |
|---|---|---|
| ① ② ④ ⑤ ⑥ ③ ⑦ | 见前几版 | ✅V0.2.108~114 |
| ⑧ | 选牌 / 取牌方式增加「牌顶」 | ✅ 本版 |
| ⑨ | 新效果「放置卡牌」（数值 0 = 自己；否则从五处挑一张放到五处）→ 会用上这一版的「牌顶」 | 下一版 |
| ⑩ | 同一状态的几条触发时机可勾「要和其他时机同时成立」+ 重置范围 | 待做（做法已确认） |
| ⑪ | 「只对哪类牌生效」增加选择范围 | 待做 |
