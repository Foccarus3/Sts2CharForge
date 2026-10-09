# V0.2.116 更新说明

> 这批需求里的第 ⑨ 条：新效果「**放置卡牌**」（按你确认的做法：数值 0 = 自己；否则从五处挑一张放到五处）。

## 一、新效果：放置卡牌

「卡牌」页 / 遗物 / 药水的效果编辑器里，效果种类多了一条：

> **放置卡牌（数值 0 = 这张牌自己）**

它有两个下拉（用上一版刚做的「牌顶」）：

| 栏 | 意思 | 候选 |
|---|---|---|
| **放到哪一摞** | 放过去的目标 | 手牌 / 抽牌堆（牌堆底）/ 弃牌堆 / 消耗牌堆 / **牌顶**（抽牌堆最上面） |
| **从哪里选牌** | 数值 ≥ 1 时从哪一摞拿 | 手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆 / **牌顶** |

行为：

- **数值 0 = 把这张牌自己放到「放到哪一摞」**（只有**卡牌**上有「这张牌」；遗物 / 药水填 0 会被校验器**报错拦住**）：
  ```csharp
  // 把这张牌自己放到牌顶
  await CardPileCmd.Add(this, PileType.Draw, CardPilePosition.Top);
  ```
- **数值 ≥ 1** = 从「从哪里选牌」挑 N 张（自己选 / 随机 / 牌顶）再放过去：
  ```csharp
  // 自己选：从抽牌堆挑 2 张放到手牌
  var __place = (await CardSelectCmd.FromCombatPile(choiceContext, PileType.Draw.GetPile(base.Owner), base.Owner, new CardSelectorPrefs(base.SelectionScreenPrompt, 2))).ToList();
  if (__place.Count > 0) await CardPileCmd.Add(__place, PileType.Hand, CardPilePosition.Bottom);

  // 牌顶：直接拿抽牌堆最上面那张放到消耗牌堆（不用挑、也不随机）
  CardModel? __placeCard = PileType.Draw.GetPile(base.Owner).Cards.FirstOrDefault();
  if (__placeCard is null) break;
  await CardPileCmd.Add(__placeCard, PileType.Exhaust, CardPilePosition.Bottom);
  ```
- 「抽牌堆」那一档 = 本体默认位置（**牌堆底**）；「**牌顶**」= `CardPilePosition.Top`；
- 走的是本体同一个 API `CardPileCmd.Add(card|cards, PileType, CardPilePosition)`，所以「放回手牌 / 置顶 / 塞回抽牌堆」这些语义跟本体一致；
- **范围界定**（攻击 / 技能 / 自定义关键词那一组）照旧能和它一起用（候选池会按范围过滤）。

## 二、回读 / 校验 / 文案

- 这条效果实现有好几行（选牌 + 循环 + Add），所以生成时写一行标记
  `// CET:Effect=PlaceCard … CET:PlaceTo=<放到哪一摞>`，回读整段按标记重建（`CardPileCmd.Add` 到处都有，
  逐行猜会认错）；
- 回读的「跳过实现代码」名单补上了这一段的行（`__place` / `await CardPileCmd.Add(this, PileType.…, CardPilePosition.…)`），
  否则会把它们报成「没认出来的语句」；
- 校验器：**遗物 / 药水 / 状态上填 0 → 错误**（「0 = 这张牌自己」只有卡牌上成立）；
  卡牌上填 0 → 提示（说清「从哪里选牌」那一栏在这条效果上不生效）；
  数值 ≥ 1 → 提示一句「怎么拿、放哪里」；
- 卡面文案：数值 0 = 「把这张牌放到牌顶。」；数值 ≥ 1 = 「自己选从抽牌堆拿 2 张牌放到手牌。」。

## 三、验证

- `--uicheck`：开发版与发布版都是 **2641 PASS / 0 FAIL**（新增 10 条：效果种类在 /
  「放到哪一摞」的 5 个候选与显隐 / 「从哪里选牌」也显示 / 数值 0 → `Add(this, Draw, Top)` /
  自己选 → 选牌界面 + `Add(__place, Hand, Bottom)` / 牌顶 → `FirstOrDefault()` + `Add(…, Exhaust, …)` /
  卡面文案 / 标记 `CET:PlaceTo=Top` / 校验器两条 / 生成→回读三条全认回来 + 0 没认出来）；
- 探针新增 `RunPlaceCard`（卡上四条：0 放牌顶 / 自己选 2 张抽牌堆→手牌 / 牌顶 1 张→消耗牌堆 /
  随机 1 张弃牌堆→抽牌堆；遗物一条：随机 1 张弃牌堆→牌顶）→ 生成正确、回读
  `卡 = 0:Hand->Top / 2:Draw->Hand / 1:Top->Exhaust / 1:Discard->Draw`、`遗物 = PlaceCard:1:Top`；
  工程 **真 `dotnet build` 0 错误**；整轮探针 **29 处 `HasUnparsed = False` / 0 处 True**；
- 打包：`Sts2CharForge_更新包_V0.2.116.zip`。

## 四、这批剩余的

| # | 需求 | 状态 |
|---|---|---|
| ① ② ④ ⑤ ⑥ ③ ⑦ ⑧ | 见前几版 | ✅V0.2.108~115 |
| ⑨ | 新效果「放置卡牌」 | ✅ 本版 |
| ⑩ | 同一状态的几条触发时机可勾「要和其他时机同时成立」+ 重置范围 | 下一版 |
| ⑪ | 「只对哪类牌生效」增加选择范围 | 待做 |
