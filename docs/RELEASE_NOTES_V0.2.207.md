# V0.2.207 —— 「打出一张带某关键词的牌」这个触发时机，现在认得战斗中刚给上去的关键词

> 用户原话：「在战斗中给予卡牌关键词后打出这张卡不能触发打出某关键词的卡这个时机」
> （`godot (22).log` 里没有和这条相关的报错 —— 是「没反应」，不是崩溃。）

## 1. 为什么以前不触发

「触发时机 → 只对哪类牌生效 = 关键词：XXX」这一档，生成端用的是**生成期白名单**：

```csharp
// 以前（关键词 / 组 都走这一套，见 ScopeCondForFilter）
if (!((cardPlay.Card.Id == ModelDb.Card<MyCardA>().Id) || (cardPlay.Card.Id == ModelDb.Card<MyCardB>().Id))) return;
```

也就是「**存档里设计时**就勾了这个关键词的那几张牌」。而战斗中「给予卡牌关键词」是**运行时**写进
`ForgeGivenKeywords`（`ConditionalWeakTable<CardModel, List<string>>`，就是 `AddGivenCustomKeyword` 那份标记）——
被临时给上关键词的牌根本不在白名单里，于是这个时机永远不会响。

## 2. 现在：白名单 **或** 运行时标记

新增 `CSharpCodeGen.TriggerCardFilterCond`：关键词那一档生成

```csharp
if (!((cardPlay.Card.Id == ModelDb.Card<MyCardA>().Id) || (cardPlay.Card.Id == ModelDb.Card<MyCardB>().Id)
      || ForgeGivenKeywords.Get(cardPlay.Card).Contains("CONTRACT"))) return;   // 只对「关键词：契约」生效
```

- **设计期**带这个关键词的牌（老行为，白名单）照旧算；
- **战斗中刚被给予**这个关键词的牌，也算了 —— 这就是「给关键词 → 打出它 → 触发」这条链路；
- 临时关键词（`TempKeywordPower` 回合末摘掉）在移除时会 `RemoveGivenCustomKeyword`，所以摘掉之后就不再触发。
- 覆盖两条链：**遗物**的「打出一张牌后」、**自定义状态**的「打出一张牌时 / 一张牌结算完毕时」
  （锻造图上的【触发时机】也走同一个发射器，自动一起修好）。
- 别的档位（攻击 / 技能 / 能力 / 诅咒 / 状态 / 组）判据**一字未变**。

顺带修了一个会挡编译的问题：**只配了「关键词筛选的触发时机」、没配「给予关键词」效果**时，
以前不会生成 `GivenKeywords.cs`（`ForgeGivenKeywords` 那个类），生成出来的工程会 CS0103。
现在 `UsesGivenCustomKeyword` 把「触发时机的关键词筛选」也算进去了。

## 3. 验收

- 探针（`sacprobe gen`）：自定义关键词 + 设计期带它的卡 + 状态触发（CardFilter = Keyword:…）+
  遗物触发（CardFilter = Keyword:…）→ 生成工程 `dotnet build` **0 error**；
  生成出来的两条守卫分别是
  `if ((cardPlay.Card.Id == ModelDb.Card<SacKwCard>().Id) || ForgeGivenKeywords.Get(cardPlay.Card).Contains("KEYWORD_1")) { … }`（状态）
  和 `if (!(… || ForgeGivenKeywords.Get(cardPlay.Card).Contains("KEYWORD_1"))) return;`（遗物）。
- 老存档逐文件对拍：示例 **113** / WhiteCloak **412** 个文件 **0 差异**（这两份没有关键词筛选的触发时机）。
- `--uicheck`：**3983 PASS / 0 FAIL**（新增 4 条：遗物侧白名单+运行时标记、状态侧同样带上、
  光有触发筛选也会生成 `GivenKeywords`、非关键词档一字未变）。

## 4. 版本

- `V0.2.207`（重新生成一次工程即可，存档不用改）
