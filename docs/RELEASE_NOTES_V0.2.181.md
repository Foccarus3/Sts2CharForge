# V0.2.181 更新说明

> 你报的：「**锻造卡的生成卡牌多选1不正常生效**」。真因很直接：节点上选的档位，**桥根本没读**。

---

## 一、现象与真因

【效果】= 「生成卡牌」+ 取卡方式「按范围随机」+「多选1」= 3（或 5）→ 进游戏**没有选牌界面**，
而是直接生成了 N 张**指定卡**（= 「目标卡」那一栏里的那张）。

真因在 `ForgeEffectBridge.FromFields`（把【效果】模块的**老字段**建成 `EffectSpec` 的那一步）：
它读了 `SpawnTo` / `SpawnCardId` / `PotionPick` / `CardPick`… 但**整块漏了生成卡牌的那几个档位**：

| 界面上的栏位 | 存进节点的键 | 桥以前读了吗 |
|---|---|---|
| 取卡方式（指定卡 / 按范围随机） | `SpawnPick` | ❌ 没读 → spec 里是 `""` |
| 多选1（候选张数） | `SpawnChoice` | ❌ 没读 → spec 里是默认 `1` |
| 范围限定 | `SpawnFilter` | ❌ 没读 |
| 从范围里自己挑 | `SpawnChosen` | ❌ 没读 |
| 附加处理（升级 / 免费 / 加关键词…） | `SpawnUpgraded` / `SpawnFree` / … | ❌ 没读 |

于是：

* `EffectSpec.IsSpawnRandom`（= `SpawnPick == "Random"`）为 **false** ⇒ 生成端走「指定卡」那条老路
  （`spawnCardClass` 是那张卡 → 循环 N 次生成同一张），**选牌界面那段代码一行都没生成**；
* 顺带：`SpawnChoice` 永远是 1 ⇒ 就算走对了分支也只会是「不选，随机给」。

**探针实测（修之前）**：

```
Ensure: Kind=GenerateCard Pick=Fixed IsRandom=False Choice=1 Filter=      ← SpawnPick / Choice / Filter 全丢
```

## 二、改法

`ForgeEffectBridge` 里把这九个键**读进来 + 回写出去**（和卡牌页同一套字段名，`EffectFieldUsage` 里那一份）：

```csharp
SpawnPick = Canonical(node.Get("SpawnPick"), "Fixed", "Fixed", "Random"),
SpawnChosen = node.GetFlag("SpawnChosen"),
SpawnFilter = (node.Get("SpawnFilter") ?? "").Trim(),
SpawnUpgraded / SpawnFree / SpawnFreeThisTurn / SpawnUpgradedThisTurn = node.GetFlag(...),
SpawnKeyword = (node.Get("SpawnKeyword") ?? "").Trim(),
// 多选1 的候选张数（纯数字）
if (decimal.TryParse(node.Get("SpawnChoice").Trim(), …)) e.SpawnChoice = sc;
```

`MirrorToFields`（spec → 老字段）也一起补上这九个 —— 不回写的话，界面上改过这些栏位之后老字段还是旧值，
回读 / 其它读老字段的路径会看到过期数据。

**修之后（同一个探针）**：

```
Ensure: Kind=GenerateCard Pick=Random IsRandom=True Choice=3 Filter=Attack
```

生成出来的就是卡牌页那份实现（同一个发射器，V0.2.170 就接好了，只是以前拿不到档位）：

```csharp
// 多选1 = 3（≤3 → 本体那张「选一张」界面）
List<CardModel> __candidates = MegaCrit.Sts2.Core.Factories.CardFactory.GetDistinctForCombat(__fgPlayer, __genPool, 3, …);
if (__candidates.Count > 0) {
    CardModel? __picked = await CardSelectCmd.FromChooseACardScreen(choiceContext, __candidates, __fgPlayer, canSkip: false);
    …
    await CardPileCmd.AddGeneratedCardToCombat(__picked, PileType.Hand, __fgPlayer);
}
// 多选1 = 5（>3 → 本体「抉择悖论」用的简单网格，本体那个 API 候选 > 3 会直接抛异常）
CardModel? __picked = (await CardSelectCmd.FromSimpleGrid(choiceContext, __candidates, __fgPlayer, new CardSelectorPrefs(base.SelectionScreenPrompt, 1))).FirstOrDefault();
```

选牌提示语也照旧自动生成（`SPAWN_PICK_PROBE.selectionScreenPrompt = "从 3 张中选一张"`）。

## 三、验证

* 探针两张锻造卡：多选1 = 3 → `FromChooseACardScreen`；多选1 = 5 → `FromSimpleGrid`
  → 生成工程 `dotnet build` **0 错误**；
* `--uicheck` **3799 PASS / 0 FAIL**；
* `--samplegen` 对 V0.2.143 基线仍是那 3 个已知差异（`.sln` / `ModEntry.cs`（V0.2.179 有意改的）/
  `SevenGuard.cs`），**没有扩大** —— 没在节点图上用生成卡牌的存档逐字节不变。

## 四、你要做的

更新到 V0.2.181 → **重新生成 + 一键构建·安装**。锻造页那张卡的「生成卡牌」现在会真的弹出选牌界面。

> 附加一句：这次只修了「取卡方式」这一族在节点图上**读不到**的问题。`TransformCard`（变化卡牌）用的是
> **同一份桥字段**（`ForgeGraphGen.cs:2429` 就是读 `Ensure(...).IsSpawnRandom`），所以它一起修好了。
> 「复制卡牌」的附加处理是**另一套字段**（`Copies` / `CopiesFormula`，见 `EffectFieldUsage`），
> 这次顺手核了一遍：桥里也没有读它 —— 你要是也发现复制卡牌的「按范围随机 / 附加处理」不生效，
> 发我一条日志，我照同一套过一遍（不一起改是因为我没有复现用例，不想凭猜改生成端）。
