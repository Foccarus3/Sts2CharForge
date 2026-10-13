# V0.2.162 更新说明

> 回答你报的：「**挖掘一下游戏本体的所有参数**，如本回合消耗的手牌，拥有的状态牌数之类」。

## 一、结论：本体能读的状态一共挖出 5 大类，其中 57 条做成了内置只读量

原来「数值公式」里能直接写的实时读数只有 **23 条**（回合 / 轮 / 四摞张数 / 丢弃·消耗·生成·复制 /
花掉的能量 / 拿到的资源 / 身上状态数）。这一版把本体**所有**读得到的运行时状态过了一遍
（解包工程 `src/Core/**` 逐文件 + 真 `sts2.dll` 的 9761 个类型双向核对），补到 **80 条**。

新加的（按用途分组，公式里直接写名字）：

| 组 | 新增的内置量 |
|---|---|
| 牌组 | `DeckCount`（牌组张数）· `DeckUpgradableCount`（还能升级的牌数） |
| **某类牌的张数**（你点名的「拥有的状态牌数」在这条：`DeckStatusCount`） | 手牌五类 `HandAttackCount` `HandSkillCount` `HandPowerCount` `HandStatusCount` `HandCurseCount` + 牌组五类 `DeckAttackCount` `DeckSkillCount` `DeckPowerCount` `DeckStatusCount` `DeckCurseCount` |
| 自己 | `MyHp` `MyMaxHp` `MyHpMissing`（已损失生命）`MyBlock` `MyEnergy` `MyEnergyMax` `MyStars` `MyGold` `RelicCount` `PotionCount` `PetCount` `OrbCount` |
| 身上的状态 | `BuffsOnMe` `DebuffsOnMe`（按**当前层数**判定，力量这种可负的才算得对） |
| 敌人 | `EnemyCount`（活着的）`HittableEnemyCount`（可打的）`EnemyTotalHp` |
| 跑图 | `Floor` `ActFloor` `ActNumber` `Ascension` `DamageDealtTotal`（本局累计伤害）`DebuffsAppliedTotal` |
| **本回合 / 本场战斗**（你点名的「本回合消耗的手牌」在 `ExhaustedThisTurn`，另有打出的牌数 `PlayedThisTurn`） | `Played*`（打出的牌数·**含正在打出的这一张**）`Attacks/Skills/PowersPlayed*` `Drawn*`（抽到的）`DamageDealt*` `DamageTaken*` `BlockGained*` `EnemiesKilled*` `StarsSpent*`（花掉的资源）`Summoned*` |

完整清单、口径、以及**本体读得到但还没内置的**（按稀有度 / 关键词 / 标签统计、每个地图点的金币生命统计、
敌人意图、ScoreUtility 的精英 / Boss 击杀数…）全部写在 **`docs/VANILLA_STATE_PARAMS.md`**
——那张表每条都带 C# 表达式、出处（文件:行号）、粒度和例子，以后想加读数照着抄就行。

## 二、顺手挖出两个「一用就编不过」的老 bug（都修了）

挖掘的办法是：把 `FormulaBuiltins` 里**每一条表达式**吐成一个真 C# 工程、拿**游戏自己的 `sts2.dll` 编译一遍**
（探针 `_probe\formulaprobe`）。结果当场抓到两条**一直在**、但工具自己的自检看不见的错误
（自检只看生成文本，不看能不能编译）：

1. **`PowersOnMe`（V0.2.95 起就在）**：写成 `{c}.Powers.Count(p => p.IsVisible)` ——
   `Creature.Powers` 是 `IReadOnlyList`，它**自己带一个 `Count` 属性**，成员查找先命中属性，
   于是 `CS1955 不可调用的成员 Count`。改成 `.Powers.Where(p => p.IsVisible).Count()`。
   **凡是「集合 + 谓词」都改成 `.Where(谓词).Count()`**（这次新增的 57 条全部照这个写法，并加了自检守卫）。
2. **参数页「能量数」（`ParamSources.Energy`）**：生成的表达式是 `__fgPlayer?.Energy` ——
   而**本体的 `Player` 上根本没有 `Energy`**（当前能量在 `PlayerCombatState` 上，`Player` 只有 `MaxEnergy`）。
   只要有一个参数选「能量数」并被公式引用，**整个模组编不过**。改成
   `((decimal)(__fgPlayer?.PlayerCombatState?.Energy ?? 0))`（顺手加了战斗外兜底）。

另外修了一条同类问题：`XSpent` / `YSpent` 用在**伤害数值**上时，卡面预览那行是
`static (card, target) => …` 这个**静态匿名函数**，里面**不许出现 `base.`**（CS8821）；
预览表达式现在会把剩下的 `base.` 换成 `(card).`（打出时那条照旧用 `base.`）。

## 三、别的改动

- **战斗外不再抛异常**：手牌 / 抽牌堆 / 弃牌堆 / 消耗堆这四条老读数，以前战斗外（例：遗物「获得时」的触发时机）
  会让本体 `PileType.GetPile` 抛 `InvalidOperationException`；现在统一读成 **0**。
  老工程回读**两种表达式都认**（`FormulaBuiltin.LegacyCSharp`），不会变成「认不出来」。
- **自读量加 `My` 前缀**（`MyMaxHp` / `MyEnergy` / `MyGold`…）：参数页「取值来源」里已经有
  `MaxHp` / `Energy` / `Gold`（那几个**可以指定从谁身上取**，例如「指定敌人的最大生命」）——
  名字撞了会出现两个同名项、而且按 Id 取值永远只命中前面那个（实测会把「指定敌人的最大生命」换成自己的）。
  `ParamExpr` 也改成**先认那 7 个固定来源、再看是不是实时读数**。
- **参数页「取值来源」换成可搜索下拉**（和「增益 / 减益」同一个控件）：现在这里是 7 + 80 = 87 项，
  普通下拉点不过来 —— 打字（「状态牌」/「本回合打出」/「能量」）就能筛。
- 「可用量」小框、各种 ToolTip、错误提示里的名单和条数全部跟着名单自动走（不再写死「21 条」）。

## 四、验证（红线）

- `dotnet build Sts2CharForge.sln -c Debug` → **0 错误**；
- `--uicheck`：Debug **3600 PASS / 0 FAIL**（基线 3502，本批新增 98 条断言）；
- `--samplegen`：与 V0.2.143 基线 **113 个文件逐字节一致**（只差 `ExampleMod.sln` 的随机 GUID）；
- **探针（新增，以后每批都该跑）**：
  ① 80 条表达式逐个拿真 `sts2.dll` 编译 → **0 错误**；
  ② 真生成一个「用到全部 80 条读数 + 参数页 3 种来源」的模组，用真 Godot SDK 编译 → **0 错误**；
- 修好的两个 bug 都是「用户一用就编不过」，所以这次**没有**只改文案：生成端表达式真的变了
  （`PowersOnMe` / 四条牌堆读数），老工程回读有兼容分支兜着。
