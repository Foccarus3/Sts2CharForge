# V0.2.174 更新说明

> 回答你报的：**「变化其他牌的卡会一直挂在这，什么效果都不会触发」**
> （游戏日志里是 `System.InvalidOperationException: No selection screen prompt for CARD.BUGKILLER_CARD35.`）

**V0.2.173 没修好这条** —— 你发我的存档（`BugkillerMod.json`）让我把真因钉死了。这一版才是真修。

---

## 一、真因：节点图那条链有自己的读取点，本地化键的「判据」没跟上

出问题的是「你去！」（`BugkillerCard35`）—— 节点图上只有一个效果：

```
节点 f01684b7  Kind=Effect  Fields=[Effect=TransformCard, Pick=Chosen, SpawnCardId=BugkillerCard20, SpawnTo=Draw …]
```

生成出来的代码（`ForgeGraphOnPlay`）：

```csharp
var __fgSel090c03 = (await CardSelectCmd.FromCombatPile(choiceContext, PileType.Draw.GetPile(__fgPlayer), __fgPlayer,
        new CardSelectorPrefs(base.SelectionScreenPrompt, (int)((1m))))).ToList();   // ← 这里读提示语
foreach (CardModel __fgSel090c03C in __fgSel090c03) await CardCmd.Transform(...);
```

而**写不写那条本地化键**是由 `LocalizationGen.NeedsSelectPrompt` 这张**手写名单**决定的，
它当时认的是「**生成卡牌** + 按范围随机 + 多选1/自己挑」这几档：

```csharp
|| (((e.Kind is "GenerateCard" or "TransformCard" or "AddCardGlobal" or "TransformCardGlobal")
     && e.IsSpawnRandom && (e.SpawnChoice > 1m || e.SpawnChosen)))
```

⚠ 关键在 `e.IsSpawnRandom`：**「变化卡牌 + 取牌方式 = 自己选」这一档 `IsSpawnRandom` 是 false**
（卡是「固定卡」→ 变成哪张已经指定，只有「挑哪一张来变」是玩家选），所以**判据说「不用写键」**。

但节点图发射器（`ForgeGraphGen.EmitEffect`）判的是**图上另一个字段**：

```csharp
bool chosen = !string.Equals(node.Get("Pick", "Chosen"), "Random", StringComparison.OrdinalIgnoreCase);
… CardSelectCmd.FromHand / FromCombatPile(… new CardSelectorPrefs(base.SelectionScreenPrompt, …))   // 读了
```

两张名单**不同步** → 键没写 → 打出那张牌时本体 `CardModel.get_SelectionScreenPrompt` 查不到
`BUGKILLER_CARD35.selectionScreenPrompt` → **抛异常**，异常抛在效果链中间 → **这张牌后面的效果一条都不跑**
（你看到的「一直挂在那儿」）。

> 同一类坑这是**第三次**：V0.2.145（自定义状态上「生成卡牌+多选1」）→ V0.2.170（四档一起补）→ 这次（图上 `Pick`）。
> 根子都是「手写名单」和「发射器」各判各的。

## 二、修法

1. `NeedsSelectPrompt` 补上这一档：
   ```csharp
   || (e.Kind is "TransformCard" or "CopyCard" && e.CardPick == "Chosen")
   ```
   （以前只有 `CopyCard` 那一半；现在「变化」也一起算）
2. `SelectPromptText` 相应加一句：变化自己选 → **「选择要变化的牌」**
   （以前会落到兜底的「选择要拿到手牌的牌」，措辞对不上）
3. 自检里**钉死两条**（防第四次）：
   * 用「变化卡牌 + 自己选」造一张探针卡 → **生成代码里出现 `base.SelectionScreenPrompt` 就必须要写出键**；
   * `NeedsSelectPrompt` 必须认这一档。

## 三、验证（用你发的那份存档真跑的）

- 全 54 张卡扫一遍「生成代码读不读提示语 / 本地化键写没写」：**会读的 3 张（卡片 13 / 23 / 35），缺键 0 张**；
  卡片 35 现在有 `"BUGKILLER_CARD35.selectionScreenPrompt": "选择要变化的牌"`；
- 把这份存档**真生成 + 真编译**：`0 错误`（只有 22 条本体的可空性警告）；
- 生成工程全量校验（不依赖任何名单）：读了提示语的 3 个源码文件，本地化里对应条目 **缺键 0 个**；
- 另外两张「多余」的键（卡片 47 / 48 也写了）是**故意**的：多写一条没人读的键无害，漏一条就是牌卡住；
- `dotnet build` → **0 错误**；`--uicheck` → **3773 PASS / 0 FAIL**。

## 四、你要做的

**对害虫杀手（BugkillerMod）重新生成 + 一键构建·安装**，然后打一次「你去！」验证：应当弹出选牌界面
（提示语「选择要变化的牌」，从抽牌堆挑 1 张变成指定的卡）—— 不再报 `No selection screen prompt`。

> 说明：V0.2.173 里修的是另外三条（每累计 N 次不触发、多人催促文本绑错属性、角色页那一段占位说明），
> 那些都还在；这一版在它们之上补了这条。

---

## 附：为什么其它卡没事

| 卡 | 效果 | 有没有踩到 |
|---|---|---|
| 「变脸」（`SparkleCardFaceChange`） | 变化**手牌**里的牌 | **没事** —— 它走的是本体自带的固定提示语 `CardSelectorPrefs.TransformSelectionPrompt`，压根不读 `base.SelectionScreenPrompt` |
| 「幕后黑手」（`SparkleCardMastermind`） | 从牌组删除一张 | **没事** —— 同上，用的是 `CardSelectorPrefs.RemoveSelectionPrompt` |
| 卡片 13 / 23 | 从抽牌堆拿牌到手牌 / 放置卡牌 | 本来就有键（它们的判据盖得住） |
| **「你去！」（卡片 35）** | **变化（自己选一张）** | **踩到了** —— 就是这条 |
