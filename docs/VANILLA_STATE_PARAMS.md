# 《杀戮尖塔 2》本体可读参数总表（V0.2.162 挖掘结果）

> 用户要求：「挖掘一下游戏本体的所有参数，如本回合消耗的手牌，拥有的状态牌数之类」。
>
> 这份文档就是那次挖掘的**全部结果**：本体在运行时**读得到**的状态，一条一条列出来，附
> ① 中文名 ② 生成代码里能直接用的 C# 表达式 ③ 出处（解包工程里的文件:行号）④ 粒度（本回合 / 本场战斗 / 本局 / 实时）
> ⑤ 是否已经做成「内置只读量」（✅ = 公式里直接写名字就能用；○ = 本体读得到、但工具还没做成内置量）。
>
> **基准**：解包工程 `D:\download\slay\1`（`src/Core/**`）+ 真 `sts2.dll` 的元数据（9761 个类型）双向核对过。
> 表达式**每一条都拿真 `sts2.dll` 编译过**（探针 `_probe\formulaprobe`，见文末「怎么复现」）。
>
> ⚠ 表里的表达式一律用两个占位符写：`{p}` = **玩家**表达式、`{c}` = **自己那个生物**表达式。
> 生成端在卡牌 / 遗物 / 药水 / 锻造链里填 `__fgPlayer` / `__fgSelf`（= `base.Owner` / `base.Owner.Creature`），
> 在条件（`IsPlayable` / 描金）里内联成 `base.Owner` / `base.Owner.Creature`。

---

## 0. 先记三条口径（踩过的坑都在这儿）

1. **`base.Owner` 是谁**：卡牌（`CardModel.Owner`）、遗物（`RelicModel.Owner`）、药水都是 **`Player`**；
   只有**自定义状态**（`PowerModel.Owner`）是 **`Creature`**。
   所以卡牌里写 `base.Owner.Creature.X` 读生物、`base.Owner.X` 读玩家；状态里要写 `base.Owner.Player.X`（可能为 null）。
2. **战斗牌堆只有战斗里才有**：`PileType.GetPile` 走 `player.PlayerCombatState?.…`，**战斗外（遗物「获得时」那种触发时机）
   本体直接抛 `InvalidOperationException`**。所以凡是读手牌 / 抽牌堆 / 弃牌堆 / 消耗堆 / 出牌堆的表达式，
   一律写成 `{p}?.PlayerCombatState?.Hand?.Cards.Count ?? 0`（读成 0，不抛）。
   **牌组（`Player.Deck`）不是战斗牌堆**，战斗外也在，别套那层保护。
3. **集合的 `Count(谓词)` 编译不过**：`Creature.Powers` / `CardPile.Cards` / `CombatState.Enemies` 都是
   `IReadOnlyList<T>`，**自带 `Count` 属性** —— 成员查找先命中属性，`x.Count(lambda)` 会报
   `CS1955 不可调用的成员 Count`。一律写 **`.Where(谓词).Count()`**。

---

## 1. 回合 / 轮 / 阶段

| 中文 | 表达式 | 出处 | 粒度 | 内置 |
|---|---|---|---|---|
| 自己这一侧的第几回合（额外回合 +1） | `{p}?.PlayerCombatState?.TurnNumber ?? 0` | `Entities/Players/PlayerCombatState.cs:37` | 实时 | ✅ `Turn` |
| 当前战斗轮数（双方各走一次算一轮） | `{p}?.Creature?.CombatState?.RoundNumber ?? 0` | `Combat/CombatState.cs:103` | 实时 | ✅ `Round` |
| 当前行动方 | `{p}?.Creature?.CombatState?.CurrentSide`（`CombatSide.None/Player/Enemy`） | `Combat/CombatState.cs:111` | 实时 | ○ |
| 回合阶段 | `{p}?.PlayerCombatState?.Phase`（`PlayerTurnPhase.None/Start/AutoPrePlay/Play/AutoPostPlay/End`） | `Combat/PlayerTurnPhase.cs:11`；`PlayerCombatState.cs:44` | 实时 | ○ |
| 现在是不是我的回合 | `CombatManager.Instance.IsPartOfPlayerTurn({p})` | `Combat/CombatManager.cs:1919` | 实时 | ○ |
| 战斗进行中 / 收尾 / 即将败北 | `CombatManager.Instance.IsInProgress` / `.IsOverOrEnding` / `.IsAboutToLose` | `Combat/CombatManager.cs:167/222/192` | 实时 | ○ |
| 敌人回合已开始 | `CombatManager.Instance.IsEnemyTurnStarted` | `Combat/CombatManager.cs:143` | 实时 | ○ |
| 正在结算卡 / 药水 | `CombatManager.Instance.IsExecutingCardOrPotionEffect({p})` | `Combat/CombatManager.cs:368` | 实时 | ○ |

> **「第一回合」没有布尔标记**：本体自己一律写 `TurnNumber == 1`（额外回合时 `RoundNumber` 不变、`TurnNumber` +1，
> 所以「第一回合」只能读 `TurnNumber`）。

---

## 2. 牌堆与牌组

`PileType`（`Entities/Cards/PileType.cs:7`）：`None / Draw / Hand / Discard / Exhaust / Play / Deck`。
**战斗牌堆** = Draw / Hand / Discard / Exhaust / Play（`PileType.IsCombatPile()`，`:35`）；`Deck` 是跨战斗的牌组。

| 中文 | 表达式 | 内置 |
|---|---|---|
| 手牌张数 | `{p}?.PlayerCombatState?.Hand?.Cards.Count ?? 0` | ✅ `HandCount` |
| 抽牌堆张数 | `{p}?.PlayerCombatState?.DrawPile?.Cards.Count ?? 0` | ✅ `DrawCount` |
| 弃牌堆张数 | `{p}?.PlayerCombatState?.DiscardPile?.Cards.Count ?? 0` | ✅ `DiscardCount` |
| 消耗牌堆张数 | `{p}?.PlayerCombatState?.ExhaustPile?.Cards.Count ?? 0` | ✅ `ExhaustCount` |
| 出牌暂存堆张数（**别当已出牌数用**） | `{p}?.PlayerCombatState?.PlayPile?.Cards.Count ?? 0` | ○ |
| 战斗里的牌总数（五堆相加） | `{p}?.PlayerCombatState?.AllCards.Count() ?? 0` | ○ |
| **牌组张数**（跨战斗） | `{p}.Deck.Cards.Count` | ✅ `DeckCount` |
| 牌组里还能升级的牌数 | `PileType.Deck.GetPile({p}).UpgradableCardCount` | ✅ `DeckUpgradableCount` |
| 手牌 / 牌组里**某一类**牌的张数 | `…Hand?.Cards.Where(__fgC => __fgC.Type == CardType.X).Count() ?? 0`（X = `Attack`/`Skill`/`Power`/`Status`/`Curse`） | ✅ 五类 × 两份：`Hand*Count` / `Deck*Count` |
| **拥有多少张状态牌**（用户点名的） | `{p}.Deck.Cards.Where(c => c.Type == CardType.Status).Count()` | ✅ `DeckStatusCount` |
| 牌组里某稀有度的张数 | `{p}.Deck.Cards.Where(c => c.Rarity == CardRarity.Common).Count()`（`Basic/Common/Uncommon/Rare/Ancient/Event/Token/Status/Curse/Quest`） | ○ |
| 牌组里已升级的张数 | `{p}.Deck.Cards.Where(c => c.IsUpgraded).Count()` | ○ |
| 牌组里带某关键词的张数 | `{p}.Deck.Cards.Where(c => c.Keywords.Contains(CardKeyword.Ethereal)).Count()`（`Exhaust/Ethereal/Innate/Unplayable/Retain/Sly/Eternal`） | ○ |
| 牌组里带某标签的张数 | `{p}.Deck.Cards.Where(c => c.Tags.Contains(CardTag.Strike)).Count()`（`Strike/Defend/Minion/OstyAttack/Shiv`） | ○ |
| 手牌上限常量 | `CardPile.MaxCardsInHand`（= 10） | ○ |

出处：`Entities/Cards/CardPile.cs:23/25/27/29/31`、`CardModel.cs:289`（Type）、`:291`（Rarity）、`:526`（Keywords）、`:534`（Tags）、`:784`（IsUpgraded）。

---

## 3. 战斗历史（**最富的一口矿**）

入口：`CombatManager.Instance.History`（`Combat/CombatManager.cs:157`）。
历史里 19 种条目（`Combat/History/Entries/*.cs`），每条都带 `Actor`（谁干的）、`HappenedThisTurn(combatState)`、
`HappenedLastPlayerTurn(player)`（`CombatHistoryEntry.cs:20/62/99`）。
「本回合」用的就是 `HappenedThisTurn` 这个判据（同一轮 + 同一侧 + 每个玩家回合数都没变）——
**敌人回合里读到的是你上一个回合的数据**。

| 条目 | 关键成员 | 能用它数什么 | 内置 |
|---|---|---|---|
| `CardPlayStartedEntry` | `CardPlay` | **打出的牌数**（OnPlay **之前**记 → 含正在打的这张） | ✅ `PlayedThisTurn/Combat`、`Attacks/Skills/PowersPlayed*` |
| `CardPlayFinishedEntry` | `CardPlay`、`WasEthereal` | 已结算完的出牌数（不含正在打的这张） | ○ |
| `CardDrawnEntry` | `Card`、`FromHandDraw` | **抽到的牌数** | ✅ `DrawnThisTurn/Combat` |
| `CardDiscardedEntry` | `Card` | 丢弃的牌数 | ✅ `DiscardedThisTurn/Combat` |
| `CardExhaustedEntry` | `Card` | **消耗的牌数**（用户点名的「本回合消耗的手牌」） | ✅ `ExhaustedThisTurn/Combat` |
| `CardGeneratedEntry` | `Card`、`Creator` | 生成的牌数（**复制也算生成**） | ✅ `Generated*`、`Copied*` |
| `CardAfflictedEntry` | `Card`、`Affliction` | 被附加「折磨」的牌数 | ○ |
| `DamageReceivedEntry` | `Result`（`UnblockedDamage`/`TotalDamage`/`WasTargetKilled`）、`Dealer`、`CardSource`、`Receiver` | **造成的伤害 / 受到的伤害 / 击杀数** | ✅ `DamageDealt*`、`DamageTaken*`、`EnemiesKilled*` |
| `BlockGainedEntry` | `Amount`、`Receiver`、`Props`、`CardPlay` | **获得的格挡** | ✅ `BlockGained*` |
| `EnergySpentEntry` | `Amount` | 花掉的能量 | ✅ `EnergySpent*` |
| `StarsModifiedEntry` | `Amount`（正=获得、负=花掉） | 获得 / **花掉**的额外资源量 | ✅ `StarsGained*`、`StarsSpent*` |
| `SummonedEntry` | `Amount` | 召唤量 | ✅ `Summoned*` |
| `PotionUsedEntry` | `Potion`、`Target` | 用药水次数 | ○ |
| `OrbChanneledEntry` | `Orb` | 引导充能球次数 | ○ |
| `PowerReceivedEntry` | `Power`、`Amount`(decimal)、`Applier` | 施加状态的次数 / 层数 | ○ |
| `MonsterPerformedMoveEntry` | `Monster`、`Move`、`Targets` | 敌人这回合出了哪招 / 出了几次招 | ○ |
| `CreatureAttackedEntry` | `DamageResults` | 攻击次数（含每一下的 DamageResult） | ○ |

> **「打出的牌数」用 `CardPlaysStarted` 还是 `CardPlaysFinished`？** 本体 `CardModel.cs:1926-1961`：
> `Hook.BeforeCardPlayed` → `History.CardPlayStarted` → **`OnPlay`** → `History.CardPlayFinished` → `Hook.AfterCardPlayed`。
> 所以效果里读到的是：`CardPlaysStarted` **含**正在打出的这一张，`CardPlaysFinished` **不含**。
> 内置量用的是前者（Display 里写明了）。
> 重放（Replay N）每一次都记一条 `CardPlay`（`PlayIndex` 递增），所以「打出的牌数」会把重放各算一次。
> **`PlayPile.Cards.Count` 不是已出牌数**（那只是出牌动画的暂存堆）。

---

## 4. 生物（Creature）—— 自己 / 敌人 / 伙伴通用

| 中文 | 表达式 | 出处 | 内置 |
|---|---|---|---|
| 当前生命 | `{c}.CurrentHp` | `Creatures/Creature.cs:60` | ✅ `MyHp` |
| 最大生命 | `{c}.MaxHp` | `:81` | ✅ `MyMaxHp` |
| 已损失生命 | `{c}.MaxHp - {c}.CurrentHp` | — | ✅ `MyHpMissing` |
| 当前格挡 | `{c}.Block` | `:39` | ✅ `MyBlock` |
| 生命百分比（0~1，double） | `{c}.GetHpPercentRemaining()` | `:744` | ○ |
| 存活 / 死亡 | `{c}.IsAlive` / `{c}.IsDead` | `:207/209` | ○ |
| 可被命中 | `{c}.IsHittable` | `:286` | ○ |
| 阵营 | `{c}.Side`（`CombatSide`）、`{c}.IsEnemy` / `{c}.IsPlayer` / `{c}.IsMonster` | `:118/244/168/166` | ○ |
| 是否被击晕 | `{c}.IsStunned` | `:325` | ○ |
| 状态列表 / 数量 | `{c}.Powers`；数量 = `{c}.Powers.Where(p => p.IsVisible).Count()` | `:327` | ✅ `PowersOnMe`（只数可见的） |
| **增益 / 减益数量**（按当前层数判定） | `{c}.Powers.Where(p => p.TypeForCurrentAmount == PowerType.Buff/Debuff).Count()` | `PowerModel.cs:240` | ✅ `BuffsOnMe` / `DebuffsOnMe` |
| 某个状态的层数 | `{c}.GetPowerAmount<T>()`（无则 0）/ `{c}.GetPower<T>()?.Amount ?? 0` | `Creature.cs:596/571` | ○（参数页有「增益/减益（状态层数）」那一栏，可指定目标） |
| 某个状态在不在 | `{c}.HasPower<T>()` / `{c}.HasPower(id)` | `:561/566` | ○ |
| 伙伴（召唤物）数量 | `{p}?.PlayerCombatState?.Pets.Count ?? 0` | `PlayerCombatState.cs:27` | ✅ `PetCount` |
| 战斗内唯一 id | `{c}.CombatId` | `:100` | ○ |
| 多人血量缩放前的最大生命 | `{c}.MonsterMaxHpBeforeModification` | `:98` | ○ |

`PowerModel` 可读：`Amount`（层数，`Models/PowerModel.cs:187`）、`AmountOnTurnStart`（`:205`）、
`Type` / `TypeForCurrentAmount`（`:142/240`）、`IsVisible`（`:150`）、`Owner`（`:264`）、`Applier`（`:289`）、
`Target`（`:306`）、`AllowNegative`（`:238`）。**没有 `IsBuff`/`IsDebuff`**，要用 `Type`/`TypeForCurrentAmount` 比。

---

## 5. 玩家（Player / PlayerCombatState）

| 中文 | 表达式 | 出处 | 内置 |
|---|---|---|---|
| 当前能量（扣完费之后） | `{p}?.PlayerCombatState?.Energy ?? 0` | `PlayerCombatState.cs:84` | ✅ `MyEnergy` |
| 当前能量上限（含战斗内加成） | `{p}?.PlayerCombatState?.MaxEnergy ?? 0` | `:101` | ✅ `MyEnergyMax` |
| 基础能量上限（不含加成） | `{p}?.MaxEnergy ?? 0` | `Player.cs:204` | ○（参数页「最大能量数」） |
| 当前额外资源量 | `{p}?.PlayerCombatState?.Stars ?? 0` | `PlayerCombatState.cs:103` | ✅ `MyStars` |
| 金币 | `{p}?.Gold ?? 0` | `Player.cs:144` | ✅ `MyGold` |
| 遗物数量 | `{p}?.Relics.Count ?? 0` | `:118` | ✅ `RelicCount` |
| 药水数量 / 槽位上限 / 是否有空槽 | `{p}?.Potions.Count() ?? 0` / `{p}?.MaxPotionCount` / `{p}?.HasOpenPotionSlots` | `:122/42/166` | ✅ `PotionCount`（后两个 ○） |
| 充能球数量 / 容量 | `{p}?.PlayerCombatState?.OrbQueue.Orbs.Count ?? 0` / `.Capacity` | `PlayerCombatState.cs:121`；`Orbs/OrbQueue.cs:21/23` | ✅ `OrbCount`（容量 ○） |
| 是否有某遗物 | `{p}.GetRelic<T>() is not null` / `GetRelicById(id)` | `Player.cs:532/537` | ○ |
| 是否 Osty 在场 | `{p}.IsOstyAlive` / `{p}.Osty?.CurrentHp` | `:137/132` | ○ |
| 角色 / 玩家 id | `{p}.Character.Id.Entry` / `{p}.NetId` | `:44/48` | ○ |
| 是否参与 Hook（死了就是 false） | `{p}.IsActiveForHooks` | `:112` | ○ |

### 5.1 「本局累计」统计（`Player.ExtraFields`，跨战斗，本体自己就在记）

| 中文 | 表达式 | 出处 | 内置 |
|---|---|---|---|
| 本局累计造成的伤害 | `{p}?.ExtraFields.DamageDealt ?? 0` | `ExtraPlayerFields.cs:33`（累加处 `Commands/CreatureCmd.cs:408`） | ✅ `DamageDealtTotal` |
| 本局累计施加的减益次数 | `{p}?.ExtraFields.DebuffsApplied ?? 0` | `:38` | ✅ `DebuffsAppliedTotal` |
| 本局在商店删过几张牌 | `{p}?.ExtraFields.CardShopRemovalsUsed ?? 0` | `:14` | ○ |
| Wongo 事件积分 / CCCCombo 徽章 | `{p}?.ExtraFields.WongoPoints` / `.CccomboBadgeUnlocked` | `:23/28` | ○ |

> ⚠ 这一组是**整局**粒度，本体**没有**「本回合 / 本场战斗的伤害累计」这种字段 —— 那两个要用
> 战斗历史的 `DamageReceivedEntry` 现算（见第 3 节，已内置成 `DamageDealtThisTurn/Combat`）。

---

## 6. 跑图（`{p}.RunState`，类型 `IRunState`）

| 中文 | 表达式 | 出处 | 内置 |
|---|---|---|---|
| 当前总层数 | `{p}?.RunState?.TotalFloor ?? 0` | `Runs/IRunState.cs:81`（`RunState.cs:158`） | ✅ `Floor` |
| 本幕层数 | `{p}?.RunState?.ActFloor ?? 0` | `IRunState.cs:75` | ✅ `ActFloor` |
| 第几幕（0 起） | `{p}?.RunState?.CurrentActIndex ?? 0` | `:31` | ✅ `ActNumber`（内置量做了 +1，从 1 数） |
| 升华（进阶）等级 | `{p}?.RunState?.AscensionLevel ?? 0` | `:126` | ✅ `Ascension` |
| 当前房间类型 | `{p}?.RunState?.CurrentRoom?.RoomType`（`RoomType.Shop/RestSite/...`） | `IRunState.cs:106`；`Rooms/AbstractRoom.cs:12` | ○ |
| 已走过的地图点坐标数 | `{p}?.RunState?.VisitedMapCoords.Count ?? 0` | `RunState.cs:107` | ○ |
| 本局见过的事件数 | `{p}?.RunState?.VisitedEventIds.Count ?? 0` | `:240` | ○ |
| 修正器 / 徽章数量 | `{p}?.RunState?.Modifiers.Count` / `.BadgeModels.Count` | `:245/250` | ○ |
| 是否已结束 | `{p}?.RunState?.IsGameOver` | `:190` | ○ |
| 种子 | `{p}?.RunState?.Rng.Seed`（`ulong`）/ `.StringSeed` | `RunRngSet.cs:26/21` | ○ |
| 本局所有地图点历史 | `{p}?.RunState?.MapPointHistory` | `RunState.cs:163` | ○（见下） |

### 6.1 每个地图点的统计（`PlayerMapPointHistoryEntry` —— 另有一整片金矿）

取法：`{p}.RunState.MapPointHistory.SelectMany(act => act).Select(h => h.GetEntry({p}.NetId))`
（`Runs/History/MapPointHistoryEntry.cs:47`），每个点能读：

**金币** `GoldGained` / `GoldSpent` / `GoldLost` / `GoldStolen` / `CurrentGold`；
**生命** `CurrentHp` / `MaxHp` / `DamageTaken` / `HpHealed` / `MaxHpGained` / `MaxHpLost`；
**牌** `CardsGained` / `CardsRemoved` / `CardsTransformed` / `CardsEnchanted` / `UpgradedCards` / `DowngradedCards`；
**商店** `BoughtRelics` / `BoughtPotions` / `BoughtColorless`；
**药水** `PotionUsed` / `PotionDiscarded`；
**其它** `RestSiteChoices` / `EventChoices` / `CompletedQuests`。
（全部 public，`Runs/PlayerMapPointHistoryEntry.cs:20~135`；`TurnsTaken` 在 `MapPointRoomHistoryEntry.cs:39`。）

`ScoreUtility`（`Runs/ScoreUtility.cs`）还直接给了几个聚合读数：
`GetElitesKilledCount(history)`（`:73`）、`GetBossesSlainCount(history, won)`（`:96`）、
`CalculateScore(run, won)`（`:29`）—— 都是 `static public`，可以直接在公式里调（内置量暂未收录）。

---

## 7. 这张牌自己（`CardModel` / `CardPlay` / `ResourceInfo`）

| 中文 | 表达式 | 出处 | 内置 |
|---|---|---|---|
| 这张牌花掉的能量（X 费牌的 X） | `base.EnergyCost.CostsX ? (decimal)base.ResolveEnergyXValue() : 0m` | `CardModel.cs:1117`；`CardEnergyCost.cs:38` | ✅ `XSpent` |
| 这张牌花掉的额外资源量（Y 费牌的 Y） | `base.HasStarCostX ? (decimal)base.ResolveStarXValue() : 0m` | `CardModel.cs:468/1139` | ✅ `YSpent` |
| 当前实际费用（含修正） | `this.EnergyCost.GetWithModifiers(CostModifiers.All)` | `CardEnergyCost.cs:94` | ○ |
| 纸面费用 / 是否 X 费 | `this.EnergyCost.Canonical` / `.CostsX` | `:31/38` | ○ |
| 这张牌的类型 / 稀有度 | `this.Type` / `this.Rarity` | `CardModel.cs:289/291` | ○ |
| 是否已升级 / 升级层数 | `this.IsUpgraded` / `this.CurrentUpgradeLevel` | `:784/765` | ○ |
| 关键词 / 标签 | `this.Keywords.Contains(CardKeyword.Exhaust)` / `this.Tags.Contains(CardTag.Strike)` | `:526/534` | ○ |
| 这张牌现在在哪一摞 | `this.Pile?.Type` | `:356` | ○ |
| 动态数值的基础值 | `this.DynamicVars.Damage.BaseValue` / `this.DynamicVars["Xxx"].IntValue` | `:538`；`Localization/DynamicVars/DynamicVar.cs:33/95` | ○ |
| 附魔 / 折磨层数 | `this.Enchantment?.Amount` / `this.Affliction?.Amount` | `:632/634` | ○ |
| 加入牌组时的层数 | `this.FloorAddedToDeck` | `:887` | ○ |
| 打出的目标 / 是否自动打出 / 是第几次 | `cardPlay.Target` / `.IsAutoPlay` / `.PlayIndex` / `.PlayCount` | `Entities/Cards/CardPlay.cs:29/46/52/59` | ○ |
| 这一次实际花掉的资源 | `cardPlay.Resources.EnergySpent` / `.StarsSpent` / `.EnergyValue` / `.StarValue` | `Entities/Cards/ResourceInfo.cs:8/20/15/27` | ○ |

---

## 8. 敌人 / 全场

| 中文 | 表达式 | 出处 | 内置 |
|---|---|---|---|
| 活着的敌人数 | `{c}.CombatState.Enemies.Where(e => e.IsAlive).Count()` | `Combat/CombatState.cs:66` | ✅ `EnemyCount` |
| 可被攻击的敌人数 | `{c}.CombatState.HittableEnemies.Count` | `:143` | ✅ `HittableEnemyCount` |
| 敌人的总生命 | `{c}.CombatState.Enemies.Where(e => e.IsAlive).Sum(e => e.CurrentHp)` | `:66` | ✅ `EnemyTotalHp` |
| 全部生物 / 我方 / 玩家人数 | `CombatState.Creatures` / `.Allies` / `.Players`（各有 `.Count`） | `:71/61/81` | ○ |
| 当前行动方的生物数 | `CombatState.CreaturesOnCurrentSide.Count` | `:134` | ○ |
| 逃跑的敌人数 | `CombatState.EscapedCreatures.Count` | `:129` | ○ |
| 某怪物在不在场 | `CombatState.ContainsMonster<T>()` | `:316` | ○ |
| 敌人的意图 | `{c}.Monster?.NextMove` / `.IntendsToAttack` / `{c}.IsStunned` | `Models/MonsterModel.cs:240/242`；`Creature.cs:325` | ○ |

---

## 9. 本体读得到、但**需要 Harmony 补丁**的（原生 `private`）

这些**不在**内置量里，也**不建议**做（本体每次改版都可能变）；列出来是为了回答「有没有」：

| 想读的 | 本体里的私有字段 | 出处 |
|---|---|---|
| 本回合已打出的牌数（遗物「天鹅绒项圈」自己数的那份） | `VelvetChoker._cardsPlayedThisTurn`、`BrilliantScarf._cardsPlayedThisTurn`、`TenderPower._cardsPlayedThisTurn`、`SlothPower._cardsPlayedThisTurn` | `Models/Relics/VelvetChoker.cs:18` 等 |
| 本回合已打出的攻击 / 技能 / 能力数 | `Shuriken._attacksPlayedThisTurn`、`RainbowRing._attacksPlayedThisTurn/_skillsPlayedThisTurn/_powersPlayedThisTurn` | `Models/Relics/Shuriken.cs:22`、`RainbowRing.cs:19-23` |
| 回合状态本体 | `CombatTurnState`（**整类是 `internal sealed`**）、`CombatManager._turnState` | `Combat/CombatTurnState.cs:21`、`CombatManager.cs:49` |
| 历史条目的轮数 / 行动方 | `CombatHistoryEntry.RoundNumber` / `.CurrentSide`（private）—— **用 `HappenedThisTurn` 代替** | `History/CombatHistoryEntry.cs:27/36` |
| 卡牌本体的（不含战场临时赋予的）关键词 | `CardModel.LocalKeywords`（private）—— **用 `Keywords` 代替** | `CardModel.cs:507` |
| 遗物袋剩余 / RNG 调用次数 | `RelicGrabBag._deques`（private；`ToSerializable().RelicIdLists` 可间接读）、`Rng._counter`（private；`ToSerializable().counter` 可读） | `Runs/RelicGrabBag.cs:28`、`Random/Rng.cs:16` |

---

## 10. 已内置的 80 条（`FormulaBuiltins.All`，公式里直接写名字）

**回合**：`Turn` `Round`
**牌堆 / 牌组**：`HandCount` `DrawCount` `DiscardCount` `ExhaustCount` `DeckCount` `DeckUpgradableCount`
**手里 / 牌组各类型**：`HandAttackCount` `HandSkillCount` `HandPowerCount` `HandStatusCount` `HandCurseCount` `DeckAttackCount` `DeckSkillCount` `DeckPowerCount` `DeckStatusCount` `DeckCurseCount`
**自己**：`MyHp` `MyMaxHp` `MyHpMissing` `MyBlock` `MyEnergy` `MyEnergyMax` `MyStars` `MyGold` `RelicCount` `PotionCount` `PetCount` `OrbCount`
**身上的状态**：`PowersOnMe` `BuffsOnMe` `DebuffsOnMe`
**敌人**：`EnemyCount` `HittableEnemyCount` `EnemyTotalHp`
**跑图**：`Floor` `ActFloor` `ActNumber` `Ascension` `DamageDealtTotal` `DebuffsAppliedTotal`
**本回合 / 本场战斗**：`PlayedThisTurn` `PlayedThisCombat` `AttacksPlayedThisTurn` `AttacksPlayedThisCombat` `SkillsPlayedThisTurn` `SkillsPlayedThisCombat` `PowersPlayedThisTurn` `PowersPlayedThisCombat` `DrawnThisTurn` `DrawnThisCombat` `DiscardedThisTurn` `DiscardedThisCombat` `ExhaustedThisTurn` `ExhaustedThisCombat` `GeneratedThisTurn` `GeneratedThisCombat` `CopiedThisTurn` `CopiedThisCombat` `DamageDealtThisTurn` `DamageDealtThisCombat` `DamageTakenThisTurn` `DamageTakenThisCombat` `BlockGainedThisTurn` `BlockGainedThisCombat` `EnemiesKilledThisTurn` `EnemiesKilledThisCombat` `EnergySpentThisTurn` `EnergySpentThisCombat` `EnergyGainedThisTurn` `EnergyGainedThisCombat` `StarsGainedThisTurn` `StarsGainedThisCombat` `StarsSpentThisTurn` `StarsSpentThisCombat` `SummonedThisTurn` `SummonedThisCombat`
**这张牌自己**：`XSpent` `YSpent`

界面上的「可用量」小框（效果编辑器里）列的就是这份表，逐条带中文说明；
「参数页 → 取值来源」那个下拉也复用同一份（可搜索，前 7 项是可指定目标的固定来源，后面全是这些实时读数）。

### 10.1 为什么这一组自读量带 `My` 前缀

参数页「取值来源」里已经有 `CurrentHp` / `MaxHp` / `Gold` / `Energy` / `MaxEnergy`（那几个**可以指定从谁身上取**，
例如「指定敌人的最大生命」）。内置量如果也叫 `MaxHp`，那个下拉就会出现**两个同名项**，
而且按 Id 取值永远只命中前面那个 —— 于是「指定敌人的最大生命」会被悄悄换成「自己的最大生命」（V0.2.162 当场踩到）。
所以自读量一律加 `My` 前缀，`ParamExpr` 也改成**先认那 7 个固定来源、再看是不是实时读数**。

### 10.2 已知口径 / 坑（都写进了每条的 Display）

- **战斗外读成 0**：桩读战斗牌堆的那几条（`HandCount` 等）带 `PlayerCombatState?.` 保护；
  老版本生成的是不带保护、战斗外会抛异常的写法，回读时**老表达式也认**（`FormulaBuiltin.LegacyCSharp`）。
- **`CardPlaysStarted` 含正在打出的这一张**；重放 N 会把每次重放各记一条。
- **「复制」= 「生成」**：本体把复制记成同一条 `CardGeneratedEntry`，所以没有独立的复制读数（`Copied*` 与 `Generated*` 同值）。
- **`DamageDealt*` 只算没被格挡的部分**（`Result.UnblockedDamage`），并排除自伤；
  想数「打出去的总量」用 `History` 里同一条记录的 `Result.TotalDamage`（暂未内置）。
- **`EnemiesKilled*` 按「致死的那一下」记**（`WasTargetKilled`），非伤害致死（例：灭亡）不算。
- **`EnergyGained*`** 本体没有这个读数，是工具打在 `PlayerCombatState.GainEnergy` 上的记录器；
  回合开始把能量补满**不算**「获得」。
- **`XSpent` / `YSpent`** 在**不是 X/Y 费的牌**上会抛异常，所以生成的是「先判再调」的三元；
  它们在**卡面预览变量**里会被换成 `(card).…`（那是 `static` 匿名函数，**不许出现 `base`** —— CS8821）。

---

## 11. 怎么复现（探针 + 红线）

```powershell
# ① 把 FormulaBuiltins 里**每一条**表达式吐成一个真 C# 工程，用真 sts2.dll 编译（0 错误才算过）
dotnet run --project D:\ds\s\_probe\formulaprobe -c Debug -- D:\ds\s\_tmp\builtinprobe
dotnet build D:\ds\s\_tmp\builtinprobe\BuiltinProbe.csproj -c Debug

# ② 真生成一次「用到全部读数」的模组（含参数页 能量数 / 最大能量数 / 实时读数 三个来源），
#    再用真 Godot SDK 编译一遍（探针会把工程路径打出来）
dotnet build D:\ds\s\_tmp\builtinprobe\gen\ExampleMod\ExampleMod.csproj -c Debug

# ③ 工具自己的红线
dotnet build D:\ds\s\t3\Sts2CharForge.sln -c Debug            # 0 错误
Start-Process D:\ds\s\t3\src\Sts2CharForge.App\bin\Debug\net9.0-windows\Sts2CharForge.exe -ArgumentList '--uicheck' -Wait
D:\ds\s\t3\src\Sts2CharForge.Cli\bin\Debug\net9.0\forge.exe --samplegen D:\ds\s\_tmp\gen   # 与 V0.2.143 基线逐字节一致（只差 .sln 的随机 GUID）
```

> 探针抓出过两个**一直在**、但没人发现的编译错误（V0.2.162 修）：
> ① `PowersOnMe` 用了 `.Powers.Count(谓词)`（`IReadOnlyList` 的 `Count` 是**属性** → CS1955）；
> ② 参数页「能量数」生成的是 `__fgPlayer?.Energy`，而 **`Player` 上根本没有 `Energy`**（在 `PlayerCombatState` 上）。
> 这两条只要用户用到就是「模组整个编不过」，而工具自己的自检只看生成文本、看不见它们 —— 所以这个探针必须留着。
