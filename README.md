# Sts2CharForge — 杀戮尖塔 2 自定义角色生成器

一个 Windows 桌面工具（WPF + .NET 9）：填表配置 → 一键生成 C# 工程 → 编译 → 导出 PCK → 安装到游戏 `mods`。

> **免责声明（务必先读）**
> 本项目是**非官方**的第三方模组制作工具，与 Mega Crit Games 没有任何关联，也未获其背书。
> 使用前请阅读 [Mega Crit 内容政策](https://megacrit.com/content-policy/)：
>
> - **可以**自由制作模组、接受玩家自愿捐赠。
> - **禁止**任何形式的模组变现：付费墙、订阅、付费下载、目标式众筹（含慈善）都不行。
> - 不要声称或暗示你的模组与官方有关联。
>
> **本仓库不包含任何游戏素材。** 工具在生成模组时，会读取**你自己电脑上已购买安装的游戏**
> （游戏安装目录 + 你自行解包的原版工程目录）里的资源，复制进你生成的模组工程。
> 因此：**你自己本地玩没问题；但把生成出来的模组发布出去，等于在分发游戏美术资源**——
> 请自行确认你有权这么做（本工具默认使用中性占位图，见下文「美术资源」）。

## 需要自备的东西

| 用途 | 说明 |
|---|---|
| .NET SDK 9 | 编译工具与生成的模组 —— 用整合包「环境包」时不用自己装 |
| Godot 4.5.1 **mono** 版 | 导出模组的 PCK（用 `*_console.exe` 才能拿到构建日志）—— 同上 |
| 游戏安装目录 | 提供 `data_sts2_windows_x86_64\sts2.dll`、`0Harmony.dll` 作为编译引用 |
| 解包后的原版工程目录 | 效果库（增益/减益列表）与占位素材的来源 —— **唯一需要手动指定的一项** |

前两项（Godot、.NET）和游戏安装目录都由程序自动识别；界面上只需要在
「构建 / 日志」页指定「解包后的原版工程目录」，其余路径已从界面隐藏。

## 效果种类

| 效果 | 说明 |
|---|---|
| 造成伤害 / 获得格挡 / 抽牌 / 获得能量 / 回复生命 / 失去生命 / 最大生命 / 获得金币 | 基础效果；其中**格挡 / 回复生命 / 失去生命 / 最大生命**也能作用在敌人身上（见下）。⚠ **「造成伤害」和「失去生命」不一样**：「造成伤害 + 自己」= 本体的普通伤害（`DamageProps.cardUnpowered`）—— **格挡能挡**、不被力量/易伤放大（卡面写「受到 N 点伤害」）；「失去生命 + 自己」= `DamageProps.cardHpLoss`（`Unblockable`）—— **无视格挡**（卡面写「失去 N 点生命」）。本体判据在 `Creature.cs`：`props.HasFlag(Unblockable) ? 0 : Math.Min(Block, amount)` |
| 施加增益 / 减益 | 目标取自本体 Power 列表（运行时从你本机的解包工程读取） |
| 结束回合 / 额外获得一回合 | `PlayerCmd.EndTurn` / 自动生成 `ForgeExtraTurnPower` |
| 生成卡牌 | 参考静默猎手的剑舞·小刀：`CombatState.CreateCard<T>` + `CardPileCmd.AddGeneratedCardsToCombat`；可选生成的卡（本体卡或本模组的卡）与去处（手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆） |
| 消耗卡牌 | 参考铁甲战士的坚毅：可选「自己选」（`CardSelectCmd.FromHand` + `CardCmd.Exhaust`）或「随机」（`Rng.CombatCardSelection.NextItem`）；「从哪里选牌」可选 手牌 / 抽牌堆 / 弃牌堆 |
| 丢弃卡牌 | 丢进弃牌堆（洗牌后会回来）：`CardCmd.Discard` + `CardSelectCmd.FromHandForDiscard` / `FromCombatPile`；「从哪里丢弃」可选 手牌 / 抽牌堆（见下文「丢弃卡牌」） |
| 升级卡牌 | 从手牌 / 抽牌堆 / 弃牌堆挑 N 张升级（`CardCmd.Upgrade` + `UpgradeSelectionPrompt`，只列能升级的牌）；见下文「升级卡牌」 |
| 升级卡牌（全局） | 升级**牌组**里的 N 张（永久、写进存档）：本体「香盒 Pomander」的写法 `CardSelectCmd.FromDeckForUpgrade` |
| 毒性爆发 | 本体 Outbreak：给所有敌人上 N 层中毒，然后立刻把中毒触发一次（`PoisonPower.Trigger()`） |
| 大限已至 | 本体 Time's Up：造成等于**目标身上灾厄层数**的伤害（`CalculatedDamage` + `GetPowerAmount<DoomPower>()`），只能用在卡牌上、目标必须选「指定敌人」 |
| 预见 | 一代观者的 Scry（本体没有这个机制）：看抽牌堆顶 N 张，把其中任意张丢进弃牌堆（可以一张都不丢） |
| 变化卡牌 | 参考储君的下去：填了目标卡 → `CardCmd.Transform`；留空 → `CardCmd.TransformToRandom`；同样支持「从哪里选牌」 |
| 从抽牌堆 / 弃牌堆拿牌到手牌 | 参考本体的「搜寻 / 全息影像 / 挖掘」：`CardSelectCmd.FromCombatPile` + `CardPileCmd.Add(选中, PileType.Hand)`，弹自己的选牌界面（提示语 `<ENTRY>.selectionScreenPrompt` 会一起生成） |
| 获得卡牌 / 变化卡牌 / 删除卡牌（全局） | 直接改**牌组**（跨战斗永久生效）：`RunState.CreateCard` + `CardPileCmd.Add(card, PileType.Deck)` / `CardCmd.Transform` / `CardSelectCmd.FromDeckForRemoval` + `CardPileCmd.RemoveFromDeck` |
| 获得卡牌奖励 | N 选一进牌组。挂「战斗胜利后」（状态 / 遗物）走本体战斗奖励（`room.AddExtraReward(new CardReward(...))`，结算界面多一条）；挂战斗中则当场弹选牌界面 |
| 强化指定卡牌 | 像本体「精准」对「小刀」那样，只给**你指定的那一张卡**加伤害 / 格挡（见下文「强化指定卡牌」） |

### 「获得能量 / 获得金币」填负数 = 扣除

数值填负数就是扣：`-2` = 失去 2 点能量、`-25` = 失去 25 枚金币（范围 -10~10 点 / -999~999 枚）。
为什么不能直接把这个负数交给本体：

- `PlayerCmd.GainEnergy` 第一句是 `if (!(amount > 0m)) return;` —— **非正数什么都不做**；
- `PlayerCmd.GainGold` 改完数值后同样是 `if (!(amount > 0m)) return;`。

所以生成器把负数改写成 `PlayerCmd.LoseEnergy(System.Math.Abs(...), owner)` /
`PlayerCmd.LoseGold(System.Math.Abs(...), owner)`（`LoseGold` 默认 `GoldLossType.Lost`）——
这两个内部会 `Math.Max(0, …)`，**不够时只扣到 0，不会扣成负数**。卡面描述也跟着写成
「失去 2 点能量。」而不是「获得 -2 点能量。」（自定义状态的触发器描述同理）。

- `Energy` + 勾了「下回合生效」+ 负数 → 本体 `EnergyNextTurnPower` 的 `AfterEnergyReset` 走的还是
  `GainEnergy`（负数被忽略），所以改用一个生成的负债状态 `<角色>ForgeEnergyNextTurnDebtPower`
  （`AfterEnergyReset` 里 `LoseEnergy(base.Amount)` 再自毁），描述写「下回合开始时，失去 3 点能量。」；
- 想「现在拿、下回合还」（本体的透支）用「透支能量」那条效果，不用填负数；
- 「数值 = X」的卡牌上填负数无效：X 要到打出时才知道，正负判不出来，只能按「获得」生成（校验会给提示）。
- `--recover` 认这两种写法：`LoseEnergy` / `LoseGold` 回读成负数，生成的那个负债状态回读成
  「获得能量 -N + 下回合生效」。

### 「下回合生效」都支持哪些效果

勾上那条「下回合生效」以后，效果不是立刻发生，而是**下回合开始时**才发生（本体的做法都是挂一个
「回合开始时发放并自毁」的状态）：

| 效果 | 生成的东西 | 说明 |
|---|---|---|
| 获得格挡 | 本体 `BlockNextTurnPower` | 下回合开始时给这么多格挡 |
| 抽牌 | 本体 `DrawCardsNextTurnPower` | 下回合开始时抽这么多张 |
| 获得能量 | 本体 `EnergyNextTurnPower`（正数）/ 生成的负债状态（负数） | 负数 = 下回合开始时少这么多点 |
| **获得额外资源量** | 本体 `StarNextTurnPower` | 下回合开始时按数量发放（只支持正数：本体的 `GainStars` 拒绝负数） |
| 施加增益 / 减益 | 生成的延迟状态类 `<角色>ForgeDelayed…` | 下回合开始时才挂上那个状态 |
| 其它（伤害 / 回复生命 / 生成卡牌…） | —— | 不支持，界面勾了会**报错拦住**（不是静默忽略） |

额外资源量那条要注意：**负数 = 卡牌费用「需要 N 点」**，费用跟「下回合生效」没有关系，
所以负数 + 勾了下回合生效时会给一句**警告**说明这一勾会被忽略（想要「下回合再扣」请改用别的写法）。


### 「从哪里选牌」：手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆 / 牌顶

「消耗卡牌」「变化卡牌」「丢弃卡牌」下面有一个 **「从哪里选牌」** 下拉（手牌 = 本体默认）：

| 选哪一摞 | 生成的东西 | 本体的同款 |
|---|---|---|
| 手牌 | `CardSelectCmd.FromHand(context, player, prefs, filter, source)`（丢弃走 `FromHandForDiscard`，奇巧牌会描金） | 坚毅 / 下去 / 杂技 / 幸存者 |
| 抽牌堆 | `CardSelectCmd.FromCombatPile(choiceContext, PileType.Draw.GetPile(base.Owner), base.Owner, prefs)` | 充能 Charge / 净化 Cleanse / 降灵 Seance |
| 弃牌堆 | 同上，换成 `PileType.Discard.GetPile(base.Owner)` | 全息影像 Hologram / 头槌 Headbutt / 挖掘 Dredge / 宇宙冷漠 |
| 消耗牌堆 | 同上，换成 `PileType.Exhaust.GetPile(base.Owner)` | （本场战斗已被消耗的牌） |
| **牌顶**（V0.2.115 新增） | **不弹选牌界面、也不随机**：`PileType.Draw.GetPile(base.Owner).Cards.FirstOrDefault()`（要 N 张就是 `…Cards.Take(N)`） | 抽牌堆最上面那张（本体抽牌就是取 `Cards.FirstOrDefault()`，「预见」也是看前 N 个） |

「随机」模式下就是从那一摞里随机抓：`Rng.CombatCardSelection.NextItem(PileType.Draw.GetPile(base.Owner).Cards)`。
描述也会跟着写成「自己选从弃牌堆消耗 1 张牌。」，不会写死「手牌」。
**「丢弃卡牌」只有手牌 / 抽牌堆 / 牌顶三个选项**（弃牌堆里的牌本来就已经被丢了，再丢一次没有意义）。

「从抽牌堆 / 弃牌堆拿牌到手牌」这两种效果是**只有「自己选」的**（弹选牌界面，挑 N 张进手牌），
那一摞里没牌时什么都不做。这两类效果只能用在卡牌 / 遗物 / 药水上（状态触发器里没有这条 UI 流程，校验会拦住）。

### 放置卡牌（V0.2.116）：把牌放到某一摞（含牌顶）

效果种类「**放置卡牌（数值 0 = 这张牌自己）**」，配两个下拉：

| 栏 | 候选 |
|---|---|
| **放到哪一摞** | 手牌 / 抽牌堆（牌堆底）/ 弃牌堆 / 消耗牌堆 / **牌顶**（抽牌堆最上面） |
| **从哪里选牌** | 手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆 / **牌顶**（数值 ≥ 1 时才有意义） |

- **数值 0 = 把这张牌自己放过去**（`await CardPileCmd.Add(this, PileType.X, CardPilePosition.Y)`）——
  只有**卡牌**上有「这张牌」，遗物 / 药水 / 状态上填 0 会被校验器**报错拦住**；
- **数值 ≥ 1** = 从「从哪里选牌」挑 N 张（自己选 / 随机 / 牌顶）再放过去，走
  `CardSelectCmd` + `CardPileCmd.Add(cards, PileType.X, CardPilePosition.Y)`；
- 「抽牌堆」那一档 = 本体默认位置（**牌堆底**）；「牌顶」= `CardPilePosition.Top`；
- 「范围界定」（攻击 / 技能 / 自定义关键词那一组）照旧能和它一起用。

### 将此牌放回手牌

效果种类里多了一条「**将此牌放回手牌（值为 1 时生效）**」：

- **值 0 = 这条效果不存在**（卡面不写、代码也不生成，别的效果照常）；**值 1 = 打出之后把这张牌从弃牌堆搬回手牌**。
  它是**开关型**的：没有可升级的数值，所以填了「升级增量」也不生效（校验器会提醒，不当静默忽略）。
- 生成的是卡自己的 `AfterCardPlayedLate` 钩子 —— 本体「回响 MakeItSo」用的就是它：
  `if (cardPlay.Card == this && base.Pile is not null && base.Pile.Type != PileType.Hand) await CardPileCmd.Add(this, PileType.Hand);`
- **为什么不写在 `OnPlay` 里**：本体 `PlayCardAction` 是**打完这张牌之后**才把它放进弃牌堆的，
  在 OnPlay 里搬会被那一步覆盖（这是这个效果唯一容易踩空的地方）。
- 条件和概率照别的效果同一套（这个钩子里拿得到 `cardPlay`，所以「指定敌人」类条件读到的是这张牌打出去时选的目标）。
- 只做在卡牌上：遗物 / 药水没有「这张牌」（校验器拦住并说明）；自定义状态的触发器里也不行
  （那里没有「刚打出的那张牌」）。

### 像「致死性」这种效果怎么做

本体「**致死性**」（`Lethality`）是亡灵缚者的一张 Power 牌：给自己挂 50 层
`LethalityPower`（升级 75），效果是「**本回合打出的第一张攻击牌 ×(1 + 层数/100)**」——
它**不是**「斩杀 / 处决」，那是另一个关键词（`FATAL`，工具里对应条件「斩杀」）。

它**不需要新效果种类**：`LethalityPower` 本来就在效果库的状态列表里，所以直接用「施加增益/减益」：

| 要填的 | 值 |
|---|---|
| 效果种类 | 施加增益/减益（负数 = 扣除层数） |
| 增益 / 减益 | `LethalityPower`（列表里按名字搜「致死性」） |
| 数值 | 50（升级增量 25 → 升完 75） |
| 作用对象 | 自己 |
| 卡牌类型 / 稀有度 | 类型选「能力」、稀有度选「罕见」（下拉里显示的是中文；存进存档 / 生成代码的仍是 `Power` / `Uncommon` —— 本体就是这样，卡框用能力牌那套） |

同理，凡是「本体现成的状态实现了某个机制」的效果（致死性 / 混乱 / 人工制品…），
都优先用「施加增益/减益」引用它，而不是自己拼 —— 工具里没有通用的「伤害倍率」效果，
`BoostCard`（像「精准」）是**加法**而且只对某一张卡生效。

### 「数值填 0」的例外名单（哪些 0 有别的含义）

「数值 0 = 这条效果不显示也不执行」是通用规矩，但**下面这些种类的 0 有别的含义，不在此列**
（工具里是显式排除的，不会被当成「没填」丢掉）：

| 效果种类 | 数值 0 的含义 |
|---|---|
| 召唤伙伴 | 用「召唤物」页里配置的血量 |
| 给予卡牌关键词 | 这张牌自己 |
| 回合结束时自动打出 | 这张牌自己 |
| 按生命值算的三条伙伴攻击 / 牺牲伙伴 | 0 是常态（力度来自「按最大生命 / 当前生命」那个公式） |
| 将此牌放回手牌 / 抽到这张牌时费用 -N（本批新增） | 0 = 关，但**升级增量对它也没意义**（校验器会提醒） |
| 丢弃卡牌（本批新增） | 0 = **丢弃这张牌自己**（不挑牌、也没有张数） |

「**消耗卡牌**」**没有**「填 0 = 消耗自己」这一档 —— 它一直是「数值 = 张数，0 = 不生效」；
想「消耗这张牌自己」用卡牌页的「**消耗**」关键词（本体 `CardKeyword.Exhaust`）。
「丢弃卡牌」的 0 档是这一版按你的要求加的（生成 `await CardCmd.Discard(choiceContext, this);`），
那一档界面上会把「选牌方式 / 从哪里丢弃 / 范围界定」那几行藏起来（它根本不挑牌）。

### 「取几张」四档：恰好 N / 最多 N / 任意张 / **全部**（不弹选牌界面）

选牌类效果（消耗 / 丢弃 / 升级 / 复制 / 重放 / 变化 / 回合结束自动打出 / 放置 / 给予关键词 /
从三摞拿牌 / 三种全局牌组 —— 共 15 种）的效果编辑器里都有一栏 **「取几张」**：

| 取几张 | 生成的东西 | 表现 |
|---|---|---|
| 恰好 N 张（默认，老存档行为） | `new CardSelectorPrefs(prompt, N)` | 必须选满 N 张 |
| 最多 N 张（本体「净化 Purity」那种） | `new CardSelectorPrefs(prompt, minCount: 0, maxCount: N)` | 可以少选（界面上要确认） |
| 任意张 | `new CardSelectorPrefs(prompt, 0, 999999999)` | 想选几张都行（含全选 / 一张不选） |
| **全部** | **不弹选牌界面**：`foreach (CardModel c in <那一摞>.Cards.Where(范围).ToList()) <动手>` | 直接对那一摞里符合条件的**每一张**动手 —— 本体「恶魔之焰 FiendFire」消耗所有手牌那种 |

- **「全部」是唯一不弹界面的那一档**：本体恶魔之焰的写法就是 `PileType.Hand.GetPile(base.Owner).Cards.ToList()`
  然后逐张 `CardCmd.Exhaust`（先拍快照再动手，遍历中改集合会炸）；卡面写「消耗 **所有**手牌。」。
  弃掉整只手牌（本体「计算下注」那种）就用「丢弃卡牌 + 全部」。
- 「全部」这一档**选牌方式（自己选 / 随机 / 牌顶）不生效**（既没有界面、也不是随机）—— 界面上那一栏会提示，
  校验器也会说一句，不会静默忽略；同理**升级增量**对它没有落点（没有张数可显示），也有一条提示。
- 「最多 / 任意 / 全部」不声明 `Cards` 变量（卡面不写数字），生成时会写一行 `// CET:CountMode=…` 标记 ——
  「全部」那一档代码里根本没有选牌语句，回读（从已有工程恢复存档）只能靠这行标记还原。
- 卡面描述跟着档位走：「消耗 {Cards:diff()} 张手牌。」「消耗 最多 … 张手牌。」「消耗 任意张手牌。」
  「消耗 所有手牌。」；选择不生效的词（自己选 / 随机）在「全部」档里一个字都不写。

### 丢弃卡牌（丢进弃牌堆，不是消耗）


数值 = 丢几张（1~9），「选牌方式」= 自己选 / 随机，「从哪里丢弃」= 手牌 / 抽牌堆。
生成的是本体的 `CardCmd.Discard`（内部 `CardPileCmd.Add(card, 弃牌堆)` + `Hook.AfterCardDiscarded`）：

> **数值 0 = 丢弃这张牌自己**（本批按你的要求加的）：生成 `await CardCmd.Discard(choiceContext, this);` ——
> 不挑牌、也没有张数（所以不声明 `Cards` 变量，界面上「选牌方式 / 从哪里丢弃 / 范围界定」那几行会藏起来）。
> 典型用法：配「卡牌时机 = 被保留时 / 在你的回合结束时若在手牌里」那种，让这张牌自己进弃牌堆。

| 怎么配 | 生成的东西 | 表现 |
|---|---|---|
| 自己选 + 手牌 | `CardSelectCmd.FromHandForDiscard(…)` → `CardCmd.Discard(choiceContext, toDiscard)` | 弹选牌界面挑 N 张手牌丢掉 |
| 自己选 + 抽牌堆 | `CardSelectCmd.FromCombatPile(… PileType.Draw …)` → `CardCmd.Discard(…)` | 从抽牌堆里挑 N 张丢掉 |
| 随机（任意一摞） | `for (…) { NextItem(<那一摞>.Cards) → CardCmd.Discard(choiceContext, pick) }` | 随机丢 N 张，那一摞不够就有多少丢多少 |

- 丢掉的牌**洗牌后会回到抽牌堆**（和「消耗卡牌」的区别就在这里：消耗是这一场不再回来）；
- 走本体 `CardCmd.Discard` 所以「被丢弃时」的钩子照常触发 —— 手牌版本的奇巧牌在选牌界面会描金；
  从**抽牌堆**丢的那条不经手牌，所以「奇巧」这类「从手牌被丢弃时」的效果不会触发（校验器会给提示）；- 解包工程里能回读：`CardCmd.Discard` + `DiscardSelectionPrompt` 认「自己选 / 随机」，
  选牌那句里的 `FromHandForDiscard` / `PileType.X` 认「从哪一摞」（顺手把「消耗 / 变化」以前**根本没回读**
  「从哪里选牌」、以及 `CardCmd.TransformToRandom` 被记成「认不出来」两个老问题一起修了）。

### 升级卡牌（战斗内 / 全局）

「升级卡牌」和「升级卡牌（全局）」**不是一回事**：

| | 升级卡牌 | 升级卡牌（全局） |
|---|---|---|
| 改的是哪里的牌 | 战斗里的**手牌 / 抽牌堆 / 弃牌堆**（「从哪里升级」三摞都能选） | 玩家的**牌组**（永久，写进存档、跨战斗） |
| 生成的东西 | `CardSelectCmd.FromHand` / `FromCombatPile(… UpgradeSelectionPrompt, N)…` + `CardCmd.Upgrade(c)`（选牌时带上 `c => c.IsUpgradable`，只列能升级的） | `CardSelectCmd.FromDeckForUpgrade(owner, new CardSelectorPrefs(UpgradeSelectionPrompt, N))` + `CardCmd.Upgrade(c)`（本体「香盒 Pomander / 混沌之香」的写法） |
| 持续多久 | 只本场战斗 —— 战斗里的卡是牌组卡的**克隆**，所以不会污染牌组 | 永久（改的就是牌组本身） |
| 用在哪 | 卡牌 / 遗物 / 药水（要选牌界面，状态触发器里用不了） | 卡牌 / 遗物 / 药水 / 自定义状态触发器都能用（只要拿到玩家就行） |

### 生成 / 变化卡牌：范围限定 + 生成出来的卡怎么处理

「生成卡牌」「变化卡牌」「变化卡牌（全局）」「获得卡牌（全局）」多了两组设置：

- **取卡方式**：「指定卡」= 用「目标卡」那一栏选定的那张（老存档就是它）；
  **「按范围随机」** = 从卡池里按「范围限定」抽（本体「发现 Discovery / 攻击药水 AttackPotion」那种）。
- **范围限定**（只在「按范围随机」时出现）：不限 / 攻击 / 技能 / 能力 / **诅咒** / **状态**，
  再加上**每个自定义关键词一组**。
  攻击 / 技能 / 能力按 `CardType` 过滤；诅咒 / 状态会**把本体的诅咒池、状态池一起算进来**
  （`ModelDb.CardPool<CurseCardPool>()` / `StatusCardPool` + 你自己的卡池），所以你自己做的诅咒 / 状态牌也在候选里。
  随机取卡走 `CardFactory.GetDistinctForCombat(player, 池, N, Rng.CombatCardGeneration)`。
- **自定义关键词 = 一套可复用的「卡的组」**：在「自定义关键词」页加的关键词，每个都会在「范围限定」里多出一条
  「自定义关键词：××（带这个关键词的那一组牌）」。选它 = 只从**带这个关键词的那批牌**里随机出。
  卡牌页勾关键词就能改这一组里有哪些牌。
  实现上生成的是**显式牌表**（`new List<CardModel> { ModelDb.Card<A>(), ModelDb.Card<B>() }`）——
  关键词只是本工具自己的标注（卡面文字 + 悬停说明），游戏的 `CardKeyword` 是封闭枚举、模组加不了，
  运行时过滤不出来，所以只能在生成期按配置把这些牌列出来。
  两个注意：**这一组是空的**（没有任何牌带这个关键词）校验器会**报错**拦住；
  本体的候选过滤会排除「基础 / 先古 / 事件」稀有度，所以起始的打击 / 防御不会出现在这一组里。
- **多选1（候选张数）**：「生成卡牌 + 按范围随机」时效果底下多一栏，**默认 1 = 直接生成**（原来的行为）；
  填 3 就是「先随机抽 3 张候选（互不重复），玩家挑 1 张，只生成选中的那一张」——
  这时「数值」里的张数不生效（它就是候选张数），校验器会给一句提示。
  候选 ≤ 3 走本体「发现 Discovery」那个界面（`CardSelectCmd.FromChooseACardScreen`，
  本体**超过 3 张会直接抛 `ArgumentException`**）；候选 > 3 走简单网格选牌
  （`FromSimpleGrid` + `new CardSelectorPrefs(base.SelectionScreenPrompt, 1)`，本体「抉择悖论 ChoicesParadox」那种），
  所以这条效果所在的**卡片 / 遗物 / 药水 / 自定义状态**都必须一起生成 `<ENTRY>.selectionScreenPrompt`
  （`NeedsSelectPrompt` 管这件事；V0.2.145 起**四条链**都写，含「从范围里自己挑一张」那一档与锻造图上的效果）。
  候选一张都没有时整条效果**安全跳过**（本体在这一步是 `ReportSoftlock()` = 卡死）。
- **复制卡牌**（效果种类里那一条）：**数值 = 从牌堆里选几张**，效果底下的**「复制的份数」= 每一张复制几份**，
  复制出来的进**手牌**。自己选 / 随机、手牌 / 抽牌堆 / 弃牌堆都能选。
  实现就是本体「二刀流 DualWield」的做法：`CardSelectCmd.FromHand / FromCombatPile` 选出原牌 →
  `card.CreateClone()` → `CardPileCmd.AddGeneratedCardToCombat(clone, PileType.Hand, owner)`。
  注意 `CreateClone` 只允许**战斗牌堆**里的牌（本体源码里非战斗牌堆直接抛异常），所以牌组不能选；
  复制出来的牌跟着原牌的升级 / 附魔走。遗物 / 自定义状态上用不了（那里没有「战斗里选牌」的上下文），生成时会留一行说明。
  **复制出来的卡也能选「免费打出 / 升级 / 仅本回合免费 / 仅本回合升级 / 加上关键词」**（用户要求）——
  和「生成卡牌 / 变化卡牌」用的是同一套「生成出来的卡怎么处理」，生成顺序是
  `CreateClone()` → 升级 / 免费 → 再 `AddGeneratedCardToCombat` 进手牌（顺序反了就等于没改）。
- **生成 / 变化出来的卡怎么处理**（可多选）：
  - **直接升级** → `CardCmd.Upgrade(card)`（本场战斗内有效）；
  - **免费打出** → `card.SetToFreeThisCombat()`（本场战斗内 0 费）；
  - **仅本回合免费** → `card.SetToFreeThisTurn()`（回合结束恢复原费用）；
  - **仅本回合升级** → 本体**没有**「临时升级」这种 API（只有视觉预览 `CardUpgradePreviewType`），
    所以工具生成一个 `<角色>ForgeTempUpgradePower`：把这张牌交给它（`Track` 里升级并记住），
    回合结束时把**这次真的升上去的那几张**降回来（本来就是升级过的牌不碰）。
  - **加上关键词** → 本体关键词走 `card.AddKeyword(CardKeyword.X)`；选自定义关键词时走「给予卡牌关键词」
    那**同一套实现**（`AddGivenCustomKeyword` + `card_keywords` 文案 + 卡面 / 悬停补丁），
    所以卡面上的金字和悬停说明都跟着来。下拉第一项「（不加关键词）」是默认档 —— 不选时生成的代码 /
    标记行 / 卡面描述与以前的版本逐字一致。「变化卡牌」还要接住 `CardCmd.TransformToRandom / Transform`
    返回的 `cardAdded`（新卡是本体新建的，原卡已经被移掉），所以那一支会多写一行。

两点注意：
- 「按范围随机」和这几个附加处理在**卡牌 / 遗物 / 药水 / 自定义状态触发器**上都能用 ——
  自定义状态的触发器里 `base.Owner` 是 `Creature`，那条链复用的是同一份 `EmitGenerateCard`，
  只是把「玩家表达式」换成 `base.Owner.Player`、并把角色配置一起传进去（自定义关键词那一组要在生成期查）。
  默认档（指定卡、不加任何附加处理）生成出来的文本与旧版逐字一致（仍是一行 `AddToCombatAndPreview`，标记行也不写）。
- 带附加处理时生成器会自己 `CreateCard` 再逐张处理（`AddToCombatAndPreview` 内部拿不到那些卡实例），
  最后仍然走 `CardPileCmd.AddGeneratedCardToCombat` 进牌堆。

### 播放音效（V0.2.154，新效果种类）

效果种类里多了一条「**播放音效（本体音效库）**」：

- **实现**：本体所有声音都走 FMOD —— `SfxCmd.Play(string sfx, float volume = 1f)`
  （`CreatureCmd` 里的 `block_gain` / `heal` / `PlayDamage`… 全是这么放的），所以生成的就一句
  `SfxCmd.Play("event:/sfx/block_gain", 1f);`。**四条链共用**（卡牌 / 遗物 / 药水 / 自定义状态触发时机）——
  `SfxCmd` 是静态的，不需要 choiceContext、也不需要玩家选目标，所以哪里有「效果」哪里就能放。
- **「音效」栏**：`EffectCatalog.SfxChoices` —— 本体源码里**真实调用过**的 362 个非模板事件里挑出的
  55 条常用项（攻击 / 格挡 / 治疗 / 增益 / 减益 / 能量 / 金币 / 卡牌 / 遗物 / 附魔 / 宝箱 / 地图 /
  界面 / 伙伴 / 各角色招牌音 / 4 首音乐），都配中文名；界面是 `SearchComboBox`，
  候选外**可直接手填任意 `event:/…`**。
  ⚠ 为什么不是「全部 362 条」：本体 `banks/desktop/*.bank` 里存的是事件 **GUID**
  （实测把 42MB 的 bank 全文扫了一遍，一个 `event:/` 字符串都没有），完整清单只能从源码里拿。
- **音量**：**「数值」那一栏在这条效果上 = 音量%**（留空 / 0 = 100%）。
  它是**唯一一个「0 有意义」**的种类，所以：**不列进** `AmountIsStrength`
  （列进去「数值 0 = 不生效」会把整条丢掉、用户永远放不出默认音量），
  但**要列进** `NoDynamicVarKind`（不声明动态变量，否则兜底的 `new DynamicVar("Value", …)`
  会和别的效果撞名 → `DynamicVarSet contains duplicate key` 开局崩）和 `NextTurnMeaningless`
  （没有「延迟放声音」这回事）。
- **文案**：卡面 / 状态描述 / 列表摘要都写「播放音效：中文名（音量 N%）」；
  顺手修掉状态描述里跟着印出来的「（对指定敌人）」（那是从别的效果借来的尾巴）。
- **校验**：没选 = 错误；不像 `event:/…` = 错误；不在候选表里 = **提示**（不拦 —— 候选只是
  「本体用过的一批」，路径不存在时游戏里只是不放声音）；音量 > 200% = 警告。
- **回读**：生成时写 `// CET:Sfx=路径 CET:SfxVolume=音量`，`ProjectRecovery` 按它认回来
  （`SfxCmd.Play(` 那一句归到「整段跳过」白名单）；自检里跑了一遍完整「生成 → 回读」。
- ⏸ **没做**：放**你自己上传的音频文件**。本体声音必须走 FMOD，而自建 FMOD 事件需要 bank
  （模组注册不了新事件），只能绕开 FMOD 用 Godot `AudioStreamPlayer` 加载 `res://` 音频 ——
  那要新加「上传音频素材 → 打进模组 → Godot 导入」一整套链路。用户要就做。

### 播放音频文件（我上传的）（V0.2.155）

用户拍板要做「放自己的音频」。做法**绕开 FMOD** —— 本体自己的声音全是 FMOD 事件，而模组**注册不了
新的 FMOD 事件**（事件编译进 `banks/desktop/*.bank`，bank 里存的还是事件 GUID：把 42MB 的 bank
全文扫过一遍，一个 `event:/` 字符串都没有）。所以：

- **素材**：`AudioAssetSpec`（`Name` = 显示名兼生成用的 Key、`File` = 用户本机绝对路径、
  `Key` = 名字去非法字符 + 原扩展名）+ `CharacterProfile.AudioAssets`；
  「美术资源」页底部新增「**音效 / 音频**」区（列表 + 上传 / 删除），上传只是**记住本机路径**，
  生成时由 `ArtGenerator.Generate` 复制到 `<工程>/audio/<Key>`（文件不在本机 / 扩展名不认都只警告并跳过）。
  导出预设本来就是 `export_filter="all_resources"`，所以 `audio/` 下的东西会一起打进 PCK。
- **效果种类**：`PlaySoundFile`「播放音频文件（我上传的）」（数值 = 音量%），
  `CSharpCodeGen.EmitPlaySoundFile` 生成（整段包在 `{ }` 里 → 同一方法里出现两次不撞名）：

  ```csharp
  {
      var __sfx = new AudioStreamPlayer();
      __sfx.Stream = GD.Load<AudioStream>("res://audio/my_voice.ogg");
      __sfx.VolumeDb = Mathf.LinearToDb(1.0f);
      ((SceneTree)Engine.GetMainLoop()).Root.AddChild(__sfx);   // 模型不是 Node，挂场景根上最稳
      __sfx.Finished += __sfx.QueueFree;                        // 放完自己回收
      __sfx.Play();
  }
  ```

  四条链（卡牌 / 遗物 / 药水 / 状态）共用；`NoDynamicVarKind` / `NextTurnMeaningless` /
  `EffectFieldPolicy.UsesUpgradeAmount` 三份清单同步。
- **校验**：没选 / 条目被删 / 文件不在本机 / 扩展名不认（`.ogg .oga .mp3 .wav` 才认，
  别的按 `.ogg` 命名并报错）/ 同名两条互相覆盖（警告）/ 音量 > 200%（警告）。
- **回读**：认 `// CET:SfxFile=<文件名> CET:SfxVolume=<音量>` 标记，
  **并且从工程 `audio/` 目录把音频清单也认回来**（否则恢复出来的存档下次生成会丢文件）。
- ⚠ 顺手修掉 V0.2.154 的一个漏洞：`PowerTriggers.SupportedEffectKinds`（状态触发器白名单）
  漏了 `PlaySfx` → 「状态里放本体音效」被校验器拦着**根本用不了**（生成端其实已实现）。
  这一版把 `PlaySfx` / `PlaySoundFile` 都补进去了。

### 获得指定 / 随机药水

效果种类里多了一条「**获得药水（指定 / 随机一瓶）**」，取药方式两档：

- **指定药水**（默认档）：效果底下多一栏「**目标药水**」，是个**可搜索**的下拉（和「目标卡」同构的 `SearchComboBox`，
  按**中文名**打字就能搜，例：打「格挡」只剩「格挡药水 (BlockPotion)」，点它或回车补全）。
  候选来自本机的本体药水目录：扫解包工程的 `src/Core/Models/Potions/*.cs` 拿到**类名**，
  再用本体那套 `StringHelper.Slugify` 反算出本地化键 `BLOCK_POTION.title` 取中文名
  （读不到解包工程时从游戏 `sts2.dll` 反射 `PotionModel` 子类）。
  生成的是 `await PotionCmd.TryToProcure<MegaCrit.Sts2.Core.Models.Potions.BlockPotion>(玩家)` ——
  本体的泛型版内部就是 `ModelDb.Potion<T>().ToMutable()`（带实例那条重载会 `AssertMutable()`，
  所以不能直接把池子里的规范模型传进去）。数值 = 瓶数（> 1 时套一层 `for`，写死在代码里、不声明动态变量）。
- **随机药水**：本体卡「炼制药水 Alchemize」的原样写法 ——
  `PotionFactory.CreateRandomPotionInCombat(player, RunState.Rng.CombatPotionGeneration).ToMutable()` +
  `TryToProcure(...)`；**不在战斗里**的触发时机（遗物「获得时」那种）自动换成 `CreateRandomPotionOutOfCombat`，
  否则治疗类药水会被 `CanBeGeneratedInCombat` 挡掉。数值 = 瓶数。
- **药水栏满 = 什么都不发生**：本体 `TryToProcure` 在栏满时返回 `success = false`
  （源码注释 *Can fail if the potions bar is full*），我们**不丢弃、不替换**已有药水 ——
  但**不静默**：卡面 / 药水 / 状态描述里一律写明「（药水栏满时不会发生任何事）」。
- 四个上下文（**卡牌 / 遗物触发 / 药水 / 自定义状态的触发器**）都能用，也和别的效果一样支持
  「生效次数 / 概率生效 / 每条效果自己的条件」。数值 0 = 这条效果不显示也不执行（和其他种类同一套规矩）。

### 选牌类效果：范围界定（只对哪一类牌 / 哪一组牌动手）

上面那套「范围限定」现在**所有会挑牌 / 对牌动手的效果**都能用了（和「生成 / 变化卡牌」共用同一份选项表）：

| 效果种类 | 「范围界定」管什么 |
|---|---|
| 消耗卡牌 / 丢弃卡牌 / 升级卡牌 / 复制卡牌 / 重放卡牌 / 回合结束时自动打出 / 给予卡牌关键词 | 只从那一摞里挑**符合范围**的牌来动手（自己选 + 随机两条路都过同一份过滤） |
| 从抽牌堆 / 弃牌堆 / 消耗牌堆拿牌到手牌 | 选牌界面里只列符合范围的牌 |
| 预见 | 先看抽牌堆顶 N 张，再把其中**符合范围**的列进可以丢掉的那一批 |

- 界面那一行叫「**范围界定（哪些牌能选）**」，候选和「生成 / 变化」**完全同一份**：
  不限 / 攻击 / 技能 / 能力 / 诅咒 / 状态 + 每个自定义关键词一组（把关键词当**卡的组**用）。
  它**一直在**（不像「范围限定」藏在「按范围随机」那一档后面），因为「自己选」那一档同样要筛候选。
- 生成的是在候选池上套一层 `Where`：
  `PileType.Hand.GetPile(base.Owner).Cards.Where(c => c.Type == CardType.Attack)`；
  「自己选」那一档把谓词直接当 `CardSelectCmd` 的 `filter` 传进去
  （本体自己会在候选为空 / 不够时直接返回，**不会弹一个空选牌界面**）。
  自定义关键词那一组在牌堆里是按 `c.Id == ModelDb.Card<X>().Id` 认的（战斗里的牌是规范模型的克隆，Id 一样）。
- **范围为空 = 和以前逐字一样**：不写标记行、不套 `Where`、描述里也不多一个字（自检里把生成文本钉死了）。
- 卡面描述会写清范围，例：「自己选消耗 1 张手牌**（只限攻击牌）**。」「随机从抽牌堆里丢弃 1 张牌**（只限技能牌）**。」
- 遗物触发 / 药水两种上下文一样支持（「从消耗牌堆拿牌到手牌」以前只有卡牌有，这一轮把遗物 / 药水也补上了）。
- 「范围界定」只对**会挑牌**的效果种类有意义：消耗 / 丢弃 / 升级 / 给关键词 / 复制 / 重放 /
  回合结束时自动打出 / 从三摞牌拿牌 / 预见 —— 其中「复制 / 重放 / 回合结束自动打出」这三样在自定义状态的
  触发器里放不进去（状态钩子没有「打出这张牌」那一刻的上下文，校验器会按**效果种类**拦住并说明，和范围无关）。
- 范围界定选了自定义关键词、但那一组一张牌都没有 → 校验器**报错**（和「生成 / 变化」那条一样）。

### 自定义状态的触发器里能用哪些效果（以及为什么）

判据只有一条：**这条效果要不要「打出这张牌」那一刻的上下文**（这张牌本身 / 此刻弹一个选牌界面）。
要的那些（复制卡牌 / 重放卡牌 / 回合结束自动打出）生成不出来，校验器在生成前就拦住。

> ⚠ 以前校验失败时**一律**说「这种效果需要卡牌上下文（选牌 / 结束回合）」—— 这个说法误导过用户：
> 清单里混进了几种**根本不需要**那个上下文、只是生成端还没实现的种类。现在文案按真实原因分档，
> 那几种也已经补齐实现（见下）。

| 效果 | 状态触发器里 | 说明 |
|---|---|---|
| 清除目标的指定状态 | ✅ | 本体只有 `foe.GetPower<T>()` + `PowerCmd.Remove(p)`，**不传 choiceContext**；目标是「自己 / 全体敌人 / 随机敌人 / 可打的第一个敌人」 |
| 清除目标的所有减益状态 | ✅ | 同上：按 `p.TypeForCurrentAmount == PowerType.Debuff` 过滤后 `Remove` |
| 牺牲伙伴换收益 | ✅ | 三档收益都行。伙伴不在场 → 整条**静默跳过**（`if (__pet is not null)`，和卡牌一致） |
| 复制卡牌 / 重放卡牌 / 回合结束自动打出 | ❌ | 真需要「打出这张牌」那一刻的上下文 |

「牺牲伙伴」在状态钩子里的三档收益怎么生成（和卡牌那条链的区别）：

| 收益 | 卡牌上 | 状态触发器里 |
|---|---|---|
| 获得增益 / 减益 | `PowerCmd.Apply<T>(ctx, base.Owner.Creature, layers, …, this)` | `PowerCmd.Apply<T>(ctx, base.Owner, layers, base.Owner, null)` —— 状态里 `base.Owner` **本身就是 Creature**，而且没有 CardModel 可当 cardSource（传 `this` 会选错重载、整个模组编译不过） |
| 获得格挡 | `CreatureCmd.GainBlock(base.Owner.Creature, gain, …, cardPlay)` | `CreatureCmd.GainBlock(base.Owner, gain, …, null)` —— 没有 cardPlay 可当来源 |
| 造成伤害 | `.FromCard(this, cardPlay).Targeting(cardPlay.Target)` | `DamageCmd.Attack(dmg).Targeting(foe).FromPetAttacker(pet)` —— 跳过「来源」那一步（`FromCard` 的第一个形参是 `CardModel`，传 Creature 直接 CS1503；`FromMonster` 内部顺手 `TargetingAllOpponents`，之后再 `Targeting` 会抛「Targets already set」），目标用状态那条链的目标解析 |

> 顺带一个会漏生成的坑：「有没有伙伴攻击效果」这份判断以前**只看卡牌** —— 状态里「牺牲伙伴 + 收益 = 伤害」
> 也走 `.FromPetAttacker(...)`，漏了就不会生成 `cs/PetAttackExtensions.cs`，生成出来的状态源码引用一个
> 不存在的扩展方法 → CS1061、整个模组编译不过。现在两个来源都看。

> 说明：**「变化卡牌」没有另加这一行** —— 它的「范围限定」早就有含义了（那是「变出来的新卡从哪个范围抽」），
> 同一个字段再拿去限定「挑哪几张牌去变」会变成一句话两个意思，还会让老存档静默改行为。

### 「生成到」：手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆

「生成卡牌」那一条效果编辑器里有「**生成到（生成卡牌用）**」下拉，四摞都是本体的**战斗内牌堆**
（`PileType.IsCombatPile()` 认 Hand / Draw / Discard / Exhaust / Play），所以走同一套本体 API：

| 生成到 | 生成的代码 | 游戏里的表现 |
|---|---|---|
| 手牌（默认） | `CardPileCmd.AddToCombatAndPreview<T>(…, PileType.Hand, N, …)` | 直接进手牌（本体会等一下，让你看清） |
| 抽牌堆 | 同上，`PileType.Draw` | 进抽牌堆底，之后抽到 |
| 弃牌堆 | 同上，`PileType.Discard` | 进弃牌堆，洗牌后会回到抽牌堆 |
| **消耗牌堆** | 同上，`PileType.Exhaust` | **直接进消耗堆**：不经过手牌 / 抽牌堆（抽不到它），本场战斗结束也不会回来 —— 适合「生成出来就立刻消耗掉」、或者只是拿它触发「消耗时 / 消耗堆里的牌」这类效果、做个计数 |

「生成到」的值是 `Hand / Draw / Discard / Exhaust`，界面上显示中文；回读（从工程恢复存档）四种都认得。

### 「造成伤害」和「失去生命」的区别（自伤代价用哪个）

两者都能把「作用对象」选成自己，但生成的是**两套本体常量**：

| 效果 | 生成 | 护盾（格挡） | 力量 / 易伤 | 卡面 |
|---|---|---|---|---|
| 造成伤害（自己） | `DamageProps.cardUnpowered`（`Unpowered \| Move`） | **能挡** | **不放大**（就是卡面那个数） | 受到 N 点伤害 |
| 失去生命（自己） | `DamageProps.cardHpLoss`（`Unblockable \| Unpowered \| Move`） | **无视** | 不放大 | 失去 N 点生命 |

- 判据在本体 `Creature.cs`：`props.HasFlag(Unblockable) ? 0 : Math.Min(Block, amount)` —— 只要带
  `Unblockable`，格挡一点都吃不到。遗物 / 自定义状态这类**非卡牌来源**用 `DamageProps.nonCardUnpowered`（`Unpowered`）。
- 想「付出生命换爆发」（本体祭品 / 放血那种）用**失去生命**；想「打自己一下、能被护盾吸收」用**造成伤害**。
- 节点图那条路同理：【效果】「造成伤害」+【对象】「自己」走的是本体的攻击链（`DamageCmd.Attack`，可格挡），
  而【效果】「失去生命」才是 `Unblockable`。
- ⚠ 药水的目标如果是「自己」，里面那条「造成伤害」**不会执行**（药水没有「自己挨打」这条路）——
  配置检查会提醒你改目标（以前只在生成的代码里留一行注释，静默）。

### 自定义描述（卡牌 / 遗物 / 药水）

三处的「自定义描述」是同一套规则：**留空 = 用自动生成的那段**；写了以后默认**追加**在自动描述后面，
勾上「替换掉自动生成的描述」就只用你写的那段。可以写本体占位符：
`{Damage}` 显示当前数值、`{Damage:diff()}` 显示升级后的值（卡牌才有升级增量）、`[gold]…[/gold]` 是金色富文本。

描述框下面的 **「填入参数…」** 按钮会列出这条模型**真的声明过**的动态变量（只列能用的，抄错键会显示不出来）：
卡牌给 `{Damage:diff()}` 这种（**带 `:diff()` 就是高亮写法**，升级后按差异变绿），
遗物 / 药水给 `{Damage}`（它们不能升级）。一个候选直接插入，多个就弹菜单让你挑，
插到光标处（选中一段就替换那段）。

#### 遗物 / 药水 / 额外资源量右栏的**效果栏**：和卡牌页同一个版式（V0.2.144）

用户原话：「遗物药水右侧的效果栏能不能同步卡牌的」→ 拍板 A：**三页右侧照卡牌页版式重做**。

- **上面 = 一行摘要列表**（固定高度 140px）：每一行就是这条效果的「效果种类 + 数值 / 升级增量 /
  作用对象 / 条件…」（`EffectSpec.Display`，和卡牌页同一个写法）；
- **下面 = 一整块共享编辑区**：点上面任意一条 → 下面立刻变成它的完整编辑区（用的还是原来那套
  编辑器模板，一个字都没重写）；**单条效果自己的「条件」就在这块编辑区里**（以前是右下角单独一块，
  现在撤掉了 —— 和卡牌页一样，条件跟着每条效果走）；
- **加 / 删效果**：点「添加效果」加出来的那条会**自动选中**，下面那块编辑区直接就是它，可以马上改；
- ⚠ **V0.2.137 那套「每条效果折叠成一行摘要、点开才展开」已经撤掉**（`EffectSpec.IsExpanded`
  这个纯界面标记也一并从模型里删了）：它虽然精简，但版式和卡牌页不一样，而且「整行是一颗 Button」
  会把 `ListBoxItem` 的点击吃掉（V0.2.143 那个「条件选完存不住」的真因）—— 改成现在这个版式之后，
  行的点击是原生行为，那个坑**从结构上没有了**；
- ⚠ 卡牌页 / 诅咒页 / 先古卡页本来就是「一行摘要列表 + 一块共享编辑区」，这次**一个字都没动**；
- 版式改动**不碰**存档 / 生成端 / 回读：摘要是 `EffectSpec.Display`（一直就有），点的还是同一条效果。

#### 卡牌：**「自动填入」** 和 **「升级后的自定义描述」**

- **「自动填入」**：把这张牌**自动生成的那段描述**直接填进框里，你接着改就行（不用自己从头写）。
  填完会顺手勾上「替换掉自动生成的描述」—— 不勾的话卡面上会「自动那段 + 你填的这段」印两遍。
- **「升级后的自定义描述」**（卡牌页第二个框）：写**升级之后**卡面上显示的文字，留空 = 和上面那段一样。
  生成出来是本体那套语法 `{IfUpgraded:show:升级后那段|没升级那段}`，游戏里按有没有升级自动显示对应那一版。
  锻造卡最用得上它：「升级前 / 升级后」两块布干的事不一样时，一句话说不清（例：升级前打 6、升级后抽 2 张）。
  普通卡也能填（升级后换一句风味文字）。
  ⚠ 描述里自己写的字面量 `|`（竖线）会把 `IfUpgraded` 那句话**截断**（本体是按 `|` 切分选项的），
  生成时会换成全角「｜」并给一条警告。
- **升级差异高亮：直接在「升级后的自定义描述」里自己写**（V0.2.137 起）。
  游戏里「升级后」那段文字会整段变绿（本体对「升级前 → 升级后」的字面量差异做染色）；
  想**逐字段**决定哪些数值变绿，就在这段文字里直接写本体那套高亮写法：
  - `{Damage:diff()}` = 高亮（升级后按「升级前 → 升级后」的差异变绿）；
  - `{Damage}` = 普通颜色（照旧显示当前数值）。
  - 不用记语法：点上面的「**填入参数…**」按钮，卡牌那一栏给的就是**带 `:diff()` 的高亮写法**
    （遗物 / 药水不能升级，那边的「填入参数…」给的是不带 `:diff()` 的普通写法）。
  - ⚠ **不要自己手写变量名**：候选里的键就是生成代码真的声明过的那些。抄一个不存在的键
    （例如手写 `{Value:diff()}`）游戏里取不到变量，卡面上会**原样印出** `{Value:diff()}`。
  - ⚠ 写错的情况生成前的检查会报出来：填了 `{X:diff()}` 但那个变量这张牌**没声明过**
    （例如那条效果后来被删了）→ **错误**并点名是哪张牌；写了 `:diff()` 却整段一个 `{}` 占位符都没有
    （纯中文 + 直接写死的数字）→ **警告**提醒先用「填入参数…」把数字换成占位符。
  - ⚠ V0.2.135 那个「高亮字段…」多选弹窗**已经在 V0.2.137 按用户要求撤掉**了
    （用户原话：「把游戏本体中显示高亮的字符直接显示在生成器的升级后自定义描述中，让用户可以自己填」）。
    高亮就是**正文的一部分**，`CET:UpgradedCustomDescription=` 原样带回，所以生成 → 回读 → 再生成**幂等**。
- **锻造卡的自动描述是从节点图写出来的**（例：「如果 指定单体敌人身上的虚弱层数 大于 3：对所有敌人造成 A+3 点伤害；否则：失去 5 点生命。」）。
  以前这种卡的卡面描述是**空的**（效果栏里没有效果、节点图又不写文案），现在不写自定义描述也有字了。

回读靠生成代码里的 `// CET:CustomDescReplace=0/1 CET:CustomDescription=…`
和 `// CET:UpgradedCustomDescription=…` 两个标记（本地化表里是拼起来的，认不出来）。

#### 描述里的富文本 / 占位符怎么写

本体描述走的是 Godot 的 BBCode（`RichTextLabel`）+ 游戏自己注册的那几个效果标签
（`src/Core/RichTextTags/`），所以我们生成的自动描述和你的自定义描述是同一套语法：

| 写法 | 效果 |
|---|---|
| `[gold]文字[/gold]` | 游戏的金色（`StsColors.gold` = `#EFC851`）—— 本体写关键词 / 关键数字用的就是它 |
| `[blue]` `[red]` `[green]` `[purple]` `[orange]` `[pink]` `[aqua]` | 本体的另外几种颜色（数字用 `[blue]`、警告用 `[red]` 最常见） |
| `[b]粗[/b]` `[i]斜[/i]` `[center]…[/center]` | 标准 BBCode（加粗 / 斜体 / 居中） |
| `[sine]` `[jitter]` `[scramble]` `[thinky_dots]` | 本体的动效标签（本体主要用在事件文本里；卡面上也能用，但比较吵） |
| `{Damage}` / `{Damage:diff()}` / `{PoisonPower}` | 数值占位符：动态变量的**键**。「填入参数…」按钮直接插好给你 |
| 直接回车 | 换行（生成时写进本地化表，游戏里就是换行） |

几点注意：
- 标签必须**成对闭合**、区分大小写（`[gold]…[/gold]`）；写错（例如 `[gol]`、`[god]`）游戏里会把那串方括号**原样印出来**。
- **工具会自动体检 + 自动修**：只差一个字母的拼写错误（`[god]` → `[gold]`）会改成最接近的那个，
  没闭合的会在结尾补上 `[/…]`，多余的闭合会删掉，交叉嵌套（`[gold][blue]x[/gold][/blue]`）会理顺成正确顺序 ——
  改了什么会以**警告**写在生成日志 / 校验结果里，建议还是自己回去改对。
  为什么必须修：本体 `MegaLabelHelper.ParseBbcode` 对标签是**严格**的，上面任何一种都会让它**抛异常**
  （游戏日志里能看到 `Found end tag gold, expected god`），那一次自动字号计算中断，卡面上那串方括号就原样显示了
  （用户实测报的「金色富文本用不了」其实就是把 `[gold]` 写成了 `[god]`）。
- 想真的显示一个方括号，写 `[lb]` / `[rb]`（本体自己的转义写法）。
- 颜色和占位符可以套着用：`[gold]{Damage:diff()}[/gold]` = 「升级后的伤害，金色」。
- 「填入参数…」列出的是这条模型**真的声明过**的变量键；抄一个不存在的键，游戏里那个占位符不会变成数字。
- **升级差异高亮就写在正文里**：卡牌页「升级后的自定义描述」那一栏（以及普通那一栏）里
  直接写 `{Damage:diff()}` 就是高亮（升级后按差异变绿）、写 `{Damage}` 就是普通颜色 ——
  V0.2.137 起不再有「高亮字段…」按钮 / 弹窗（用户要求：让用户自己填）。写错时生成前的检查会给错误 / 警告。
- 关键词那一行（`[gold]命定[/gold]。`）是工具自动拼在描述最前面的，不用自己写。

### 自定义状态造成的伤害不算「你造成的伤害」

本体的伤害归因只看 `CreatureCmd.Damage` 的 **`dealer`** 参数
（`if (dealer != null && dealer.Player != null && target.Player == null) dealer.Player.ExtraFields.DamageDealt += …`，
也就是「伤害最高」那类统计用的）。所以自定义状态的触发器里造成的伤害一律传 **`dealer: null`**：
不计入「你造成的伤害」、战斗记录写成「XX 受到了 N 点伤害」，同时保留 `ValueProp.Unpowered`
（力量 / 虚弱只对 `IsPoweredAttack` 生效）—— 和本体「中毒跳伤 / 灭亡 / 窒息 / 闹鬼」那些状态的做法一致。

## 卡牌关键字

「卡牌」页可勾选本体 `CardKeyword`：消耗 / 虚无 / 固有 / 保留 / 不能被打出 / 奇巧 / 永恒
（`Exhaust / Ethereal / Innate / Retain / Unplayable / Sly / Eternal`，生成 `CanonicalKeywords` 覆写）。
其中「永恒」的效果是**无法从你的牌组中移除或变化**（本体就是 `IsRemovable` / `IsTransformable` 两个开关）。
「诅咒 / 先古 / 状态」页里也有一套同样的勾选框（诅咒默认固定带「不能被打出」；费用填 ≥ 0 时不再强制）。

除这 7 个之外，还可以用**效果**给牌加关键词 —— 见下面的「给予卡牌关键词」。
（卡牌页不再有「临时保留 / 临时奇巧」两个勾选框，那两个需求现在由「给予卡牌关键词」+
「数值 = 0（这张牌自己）」+「是否为临时关键词」实现。）

## 击晕敌人（效果种类里的一项）

效果种类里的 **「击晕（敌人本回合不行动）」**：作用对象选 指定敌人 / 所有敌人 / 随机 N 个敌人，
被打晕的敌人**这一回合什么都不做**。

它**不在**「增益 / 减益」那个下拉里，因为本体里「击晕」不是一个状态（`PowerModel`），
而是**怪物意图**（`StunIntent`）；本体卡「口哨 Whistle」就是用公开 API 做的：

```csharp
await CreatureCmd.Stun(cardPlay.Target);   // 本体 Whistle.OnPlay 的原样写法
```

我们照它生成（`CreatureCmd.Stun` 不需要 choiceContext，所以卡牌 / 遗物 / 药水 / 自定义状态触发器都能用），
卡面描述写「击晕指定敌人（本回合不行动）。」，悬停用本体那条静态提示（`StunIntent.GetStaticHoverTip()`）。

⚠ **「击晕」在自定义状态里的两个坑**（V0.2.166，用户实测「晕眩值到 100 却不击晕敌人」）：

1. **状态里的「敌人」= 玩家视角的敌方那一侧**（本体 `CombatState.HittableEnemies => Enemies.Where(IsHittable)`，
   `Enemies` 是整个战斗的敌方那一侧，不是「持有者的敌人」）。所以一个**贴在敌人身上**的减益（例：
   自己造的「击晕值」）里写「作用对象 = 指定敌人」，取的是**那一侧里第一个可打的敌人** ——
   **不一定是身上带着这个状态的那只**（多只敌人时会晕错）。想「击晕带着这个状态的那只」，
   把作用对象改成 **自己**（状态挂在敌人身上时「自己」就是它）→ 生成 `CreatureCmd.Stun(base.Owner)`。
   ⚠ 状态链的 `base.Owner` 本身就是 `Creature`，所以**不能**写 `base.Owner.Creature`（那是 CS1061 —— V0.2.166 修掉的老 bug，
   以前你在状态里选「击晕自己」生成出来的工程编译不过）。
2. **「某个状态层数到达 N 层时」的「盯」别选错**：状态里的「自己」= **挂着这个状态的那个单位**。
   - 「盯：**自己**」→ 守卫 `power.Owner == base.Owner`：这个状态**自己**涨到 N 就触发 ✓
     （「击晕值满了就晕那只怪」要的就是这个）；
   - 「盯：**任意敌人**」→ 守卫 `power.Owner != base.Owner && …HittableEnemies.Contains(power.Owner)`：
     要求被盯的那份状态在**别的**生物身上。所以「盯任意敌人 + 盯的就是这个状态自己」时，
     **它自己涨到 N 那一下永远不会触发**（只有别的敌人身上那份同名状态跨过 N 时才触发）。
     配置检查会为这个组合给**警告**并指路「盯：自己」（V0.2.166 新增）。

## 给予卡牌关键词（效果种类里的一项）

效果种类选 **「给予卡牌关键词」**：**数值 = 选几张牌**（**0 = 这张牌自己**），
然后在「选牌方式」（自己选 / 随机）和「从哪里选牌」（手牌 / 抽牌堆 / 弃牌堆）里选，
再在它下面新增的 **「给予关键词」** 下拉里选要给的关键词 —— 候选是本体那 7 个
（消耗 / 虚无 / 固有 / 保留 / 不能被打出 / 奇巧 / 永恒）**加上你自己的自定义关键词**；
最下面的 **「是否为临时关键词」** 勾上就只这一回合生效（回合结束自动去掉）。

生成出来的代码分三种情况：

| 给的是什么 | 生成的东西 | 临时（勾了「是否为临时关键词」） |
|---|---|---|
| 本体的**保留 / 奇巧** | `<牌>.AddKeyword(CardKeyword.Retain)` | 走本体的单回合标记 `GiveSingleTurnRetain()` / `GiveSingleTurnSly()`（`EndOfTurnCleanup` 自动复位） |
| 本体的其它关键词 | 同上 | 先 `AddKeyword`，再挂生成的 `<角色>ForgeTempKeywordPower`，回合结束 `RemoveKeyword` |
| **自定义关键词** | `<牌>.AddGivenCustomKeyword("键")` | 同上，回合结束 `RemoveGivenCustomKeyword` |

- 本体关键词走的是 `CardModel.AddKeyword` / `RemoveKeyword`（本体 API，会触发 `KeywordsChanged`，
  所以**卡面上那行金色字会立刻出现 / 消失**；本体卡、别的模组的卡也能给）。
- 自定义关键词在生成期是**写死在卡面描述文本里**的（`cards.json`），运行时加不了文本，
  所以给它们配了 `<角色>GivenKeywords.cs`：一个 `CardModel → 关键词键` 的注册表
  （`ConditionalWeakTable`，牌没了自动清）+ 两个 Harmony Postfix ——
  一个补 `CardModel.GetDescriptionForPile`（把 `[gold]名字[/gold]。` 拼在描述最前面）、
  一个补 `CardModel.get_ExtraHoverTips`（让悬停也有说明）。这样**任何卡**（含本体卡）都能被给自定义关键词。
- 数值 0（这张牌自己）只有卡牌上有意义；遗物 / 药水 / 自定义状态触发器请填 ≥ 1（校验会拦住 0）。

## 临时增益（本回合 +X，回合结束消失）

「卡牌 / 遗物 / 药水 / 自定义状态」的效果种类里有一项 **「临时增益（本回合 +X，回合结束消失）」**：
数值 = 层数，**增益 / 减益** 里选那个状态，作用对象和其他效果一样（自己 / 指定敌人 / 全体敌人 / 随机敌人）。

生成的是本体的 `TemporaryStrengthPower` 那套写法（本体的「临时力量 / 临时敏捷」、`FlexPotion` 的
`FlexPotionPower` 都是这么做的）：

- 挂上时（`BeforeApplied`）把 X 层加进**真正的状态**；
- 叠加时（`AfterPowerAmountChanged`）按增量补进去；
- 这一回合结束（`AfterSideTurnEnd`）时把这次加的 X 层撤掉、临时 Power 自己消失。

所以状态栏上会看到「临时气势 +3」这样的条目，回合结束自动掉光；**别的来源加的层数不受影响**
（撤掉的只是这次临时加的 X 层）。生成的类名是 `<角色类名>ForgeTemp<状态>`，多模组共存不撞车。

## 强化指定卡牌（像本体「精准」对「小刀」）

「卡牌 / 遗物 / 药水 / 自定义状态」的效果种类里有一项
**「强化指定卡牌（像「精准」，+N 伤害/格挡）」**：

- **目标卡** 下拉里选要强化的那张牌（自己的牌和本体卡都能选，**必填**）；
- 它下面的 **强化什么** 里选 伤害（像「精准」）或 格挡（像「敏捷」）；
- **数值** = 加多少，**填负数就是削弱**那张牌。

生成的是挂在自己身上的一张状态（`<角色>ForgeBoost<目标卡><Damage|Block>Power`），
钩子和本体那两个状态一模一样：

| 强化什么 | 钩子 | 判定 |
|---|---|---|
| 伤害 | `ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)` | 只强化自己打出的、且 `props.IsPoweredAttack()` 的**那一张卡** |
| 格挡 | `ModifyBlockAdditive(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)` | 只强化那一张卡的 `IsPoweredCardOrMonsterMoveBlock()` |

- 本体「精准」是用 `card.Tags.Contains(CardTag.Shiv)` 认小刀的，我们直接认**卡牌类型**
  （`cardSource is not <目标卡>`），比按标签认更精确、也不要求那张牌带什么标签。
- **层数 = 加多少**，本场战斗内一直有效（战斗结束状态自己消失，**不会改到存档里的卡牌**）。
  状态栏里显示「强化：某某」和层数；`AllowNegative => true` 是为了让负数（削弱）也能挂上
  （本体状态默认把负数夹到 0）。
- 同一个「目标卡 + 伤害/格挡」只会生成**一个** Power（多张牌强化同一张卡时共用）。

## 卡牌「类型 / 稀有度」显示中文（V0.2.136）

需求原文：《卡牌页优化.md》第 2 条 ①「**将类型和稀有度的英文替换成中文**」。

「卡牌」页三处下拉（锻造卡那一栏、中间那一栏、诅咒 / 先古 / 状态页那一栏）
和卡牌列表那一行，类型 / 稀有度都**显示中文**了；做法照抄遗物 / 药水页那一套
（`EffectCatalog.CardTypeOptions` / `CardRarityOptions`：`Id` 保持英文、`Display` 是中文）。

| | 下拉里显示 | 存进存档 / 生成代码的 |
|---|---|---|
| 类型 | 攻击 / 技能 / 能力 / 状态 / 诅咒 / 任务 | `Attack` / `Skill` / `Power` / `Status` / `Curse` / `Quest` |
| 稀有度 | 基础 / 普通 / 罕见 / 稀有 / 先古 / 状态 / 诅咒 | `Basic` / `Common` / `Uncommon` / `Rare` / `Ancient` / `Status` / `Curse` |

- 中文**照抄游戏本体**（`localization\zhs\gameplay_ui.json` 的 `CARD_TYPE.*` / `CARD_RARITY.*`，
  与本体 `CardTypeExtensions.ToLocString()` / `CardRarityExtensions.ToLocString()` 一一对应）；
  **档位一个都没加、一个都没减**（本体 `CardRarity` 里还有 `Event` / `Token`，那是本体内部档，
  工具从来没给过 —— 和药水页的处理一样）。
- **存档面零变化**：`CardSpec.CardType` / `Rarity` 存的仍是英文 id，生成端写的还是
  `CardType.Power` / `CardRarity.Uncommon` 这种**本体枚举名**，校验器 / 回读也照旧按英文判
  （把中文写进模型会被校验器报「类型非法 / 稀有度非法」）。
- ⚠ 措辞与遗物 / 药水页**故意不同**：那两页是「常见 / 罕见 / 稀有」（遗物还有「初始」），
  这里按本体的 `CARD_RARITY.*` 走的是「**普通** / 罕见 / 稀有」（`Basic` = 「基础」）——
  两处看的是**不同枚举**（`CardRarity` vs `RelicRarity` / `PotionRarity`），不强行统一。

## 卡牌自定义描述（「卡牌」页 → 卡面预览下面）

自动生成的卡面描述不合意时，可以在 **自定义描述** 里自己写一段（支持多行）：

- 默认**追加**在自动描述后面（自动那句话还在，你在下面补一句）；
- 勾上 **「替换掉自动生成的描述」** 就整段换成你写的（自动那句不生成）；
- 给这张牌勾了自定义关键词时，关键词那一行仍然排在最前面（那是关键词机制在管，不属于效果描述）；
- 「从工程恢复存档」也会把你写的这段读回来（多行、追不追加都能还原 ——
  生成时写在卡牌 `.cs` 里的一行 `// CET:CustomDescReplace=… CET:CustomDescription=…` 标记，
  换行转义成 `\n`；本地化表里认不出来，因为那张表里是「自动描述 + 你写的」拼起来的）。

## 自定义诅咒 / 先古卡 / 状态卡（「诅咒 / 先古 / 状态」页）

这一页放三种「不进普通卡牌池」的牌。它们是**同一套配置对象**（效果编辑器、卡面、自定义描述全都一样），
只是生成出来的模板不同 —— 所以这一页就是三个列表 + 各自合适的详情面板：

| 列表 | 存在哪 | 稀有度 | 详情面板 |
|---|---|---|---|
| 自定义诅咒 | `Profile.Curses` | 写死 `Curse` | 专属（效果写在「回合结束还在手牌里」） |
| 自定义先古卡 | `Profile.AncientCards` | 写死 `Ancient` | 和状态卡**共用**一块（都是普通牌） |
| 自定义状态卡 | **就是 `Profile.Cards` 里稀有度为「状态」的那些** | 写死 `Status` | 同上 |

> 状态卡**不复制数据**：这一栏是 `Profile.Cards` 的镜像。在「卡牌」页把稀有度改成「状态」它也出现在这里，
> 在这一栏点「添加状态卡」加的也是同一份（所以两边永远一致，删一边另一边也没了）。

### 一、自定义诅咒（默认打不出去，留在手里坑你）

生成出来的类完全照本体那几个诅咒写（受伤 Decay / 疑虑 Doubt / 苦恼 Writhe / 厄运 BadLuck / 罪恶 Guilty）：

| 项目 | 生成成什么 |
|---|---|
| 费用 / 类型 / 稀有度 | `base(<费用>, CardType.Curse, CardRarity.Curse, TargetType.None)` —— 费用**默认 -1** = 打不出去，填 ≥ 0 就用你填的值 |
| 升级 | `MaxUpgradeLevel => 0`（本体所有诅咒都是这样） |
| 关键字 | 费用 -1 时一律带 `CardKeyword.Unplayable`（不能被打出）；费用 ≥ 0 时**不再强制**（想让它照样打不出去就自己勾）；「固有 / 永恒」由你勾（本体「苦恼」= 固有、「厄运」= 永恒） |
| 触发时机 | `HasTurnEndInHandEffect => true` + `OnTurnEndInHand(...)` —— **在你的回合结束时，如果这张牌还在你的手牌中** |
| 卡框 | `VisualCardPool => ModelDb.CardPool<CurseCardPool>()`：显示成本体的灰色诅咒卡框，但**模型注册在你自己的卡池里** |
| 可选 | 「战斗结束时，如果它还在牌组里就消失」（本体「罪恶」那套：`AfterCombatEnd` → `CardPileCmd.RemoveFromDeck(this)`） |

- **费用可调**：默认 -1（打不出去）。填 0 或正数 → 这张牌**能打出去**（生成时不再强制「不能被打出」），
  但它的效果仍然写在「你的回合结束时，如果它还在你的手牌里」，所以**打出去本身不做事** ——
  只是能把它从手里丢掉（0 费「能打出去的诅咒」就是这个用法）。校验器会给一句提示说清楚。
  想让效果在打出时生效，请到「卡牌」页做一张普通牌。
- 效果语句用的是**遗物**那一套：诅咒没有「打出的目标」（没有 `CardPlay`），所以「指定敌人」按遗物同样的约定取可打的第一个敌人。
- 卡面描述照本体行文写：「在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中：…」。
  「不能被打出」不写进描述 —— 那是本体的关键字，卡面会自己把关键字那一行印在最前面。
- **怎么拿到它**：诅咒不会出现在战斗奖励 / 商店里（本体的奖励只从 Common / Uncommon / Rare 里抽），要你自己给：
  卡牌效果里用「生成卡牌」（放上手牌 / 抽牌堆 / 弃牌堆）、「获得卡牌（全局）」加进牌组，或者「获得卡牌奖励」。
  这三处的「目标卡」下拉里都能搜到你的诅咒（和自己的卡一样）。
- 注意：诅咒和普通卡**共用一个命名空间**，英文类名别撞（留空会自动编号，三张列表一起数号）。

### 二、自定义先古卡（稀有度 Ancient 的强力牌）

先古卡就是**稀有度为「先古」的普通牌**（本体丰饶 Abundance / 神化 Apotheosis / 幽灵形态 WraithForm 那种）：
能打出、能升级、能有自定义关键词和升级后关键字，只是**不进战斗奖励 / 随机生成**
（生成时写 `CanBeGeneratedInCombat => false` + `CanBeGeneratedByModifiers => false`，
和本体 `CardFactory` 里显式排除 `CardRarity.Ancient` 一致）。

- **怎么拿到它**：
  1. 先古之民给的遗物 —— 本体「尘封的书 DustyTome」会从**你的卡池**里随机挑一张先古卡（你做的先古卡也在候选里）；
  2. 或者你自己的效果：「生成卡牌 / 获得卡牌（全局）/ 获得卡牌奖励」里选它。
- 详情面板里**没有**「稀有度」下拉：这一页的牌写死是 Ancient（诅咒那栏写死 Curse），省得配错。
- **卡面是竖的**：本体先古卡在卡面节点里显示的不是普通 `%Portrait`（250×190 → 1000×760 横图），
  而是 `%AncientPortrait`（598×842，**直接拉满不保持比例**），本体所有先古卡素材都是 **606×852**。
  所以给先古卡传卡面请用 **606×852 竖图**：选到先古卡时工具里的预览框会换成 606×852 的竖框、
  按「拉满」显示（比例不对一眼就能看到变形），上传按钮与比例说明也会按这个尺寸给；
  没传图时生成的占位图同样是竖的（勾「用本体素材占位」用本体的 `ancient_beta.png`）。
  诅咒和普通卡不受影响，仍是 1000×760 横图。

### 三、自定义状态卡（本体「伤口 / 灼伤 / 眩晕 / 粘液」那种）

状态卡就是**稀有度 + 类型都是「状态」的普通牌**：

| 项目 | 生成成什么 |
|---|---|
| 费用 / 类型 / 稀有度 | `base(<费用>, CardType.Status, CardRarity.Status, TargetType.<按效果算>)` —— **费用可调**（本体那些状态牌是 0~1 费） |
| 升级 | 默认 `MaxUpgradeLevel => 0`（本体「粘液 Slimed」就是这样）；勾了「可被无限升级」才生成能升级的状态卡 |
| 关键字 | 和普通卡一样随便勾（本体「粘液」带消耗、 「伤口 / 灼伤」什么都不带） |
| 效果 | 和普通卡完全一样（`OnPlay` 里的那套，能用的种类也一样） |

- **数据只有一份**：状态卡住在 `Profile.Cards` 里（稀有度选了「状态」的那些），这一页的第三栏是它的镜像 ——
  所以在「卡牌」页加 / 删 / 改都会同步过来，反过来也一样。
- **怎么拿到它**：状态卡不会出现在战斗奖励 / 商店里（本体的奖励只从 Common / Uncommon / Rare 里抽，
  商店也不给状态牌摆货架），要你自己给：用「生成卡牌」（放上手牌 / 抽牌堆 / 弃牌堆）或「获得卡牌（全局）」。
- 校验器会给三条提醒：类型不是 `Status`（本体状态牌的类型都是 Status）、
  勾了「加入卡池」（它不会出现在奖励里）、放进初始卡组（本体只有诅咒才这么干）。

### 四、外观：用 RRGGBB 给诅咒 / 先古卡换卡框颜色

「诅咒 / 先古 / 状态」页诅咒和先古卡那两栏各有一行 **「诅咒卡框 / 先古卡框」**：

- 默认 **「跟角色配色（默认）」** —— 和以前完全一样（诅咒用本体的灰色诅咒框，先古卡用角色的卡框）；
- 也可以直接选**本体的 8 种框色**（铁甲红 / 无色灰 / 诅咒紫灰 / 任务深蓝…）；
- **先古卡**还能选 **「自定义」**，在下面的 **RRGGBB** 输入框里填 6 位十六进制（右边有色块预览）。
- **诅咒没有「自定义颜色」这一项**（本体的诅咒外观就是固定的灰色卡池，染色没有意义）：
  下拉里只有「跟角色配色」+ 本体框色名；老存档 / 老工程里存过的 `Frame = custom` 一律忽略
  （`SpecialCardStyleSpec.AllowsCustomColor = false` → `Any` / `PreviewHex` 都当没设过，
  生成时退回 `CurseCardPool`，校验器会给一句「不支持、会被忽略」的提示）。

生成出来的东西：

| 填的东西 | 生成什么 |
|---|---|
| 自定义颜色 `8A5CF6`（**只有先古卡能选**） | `materials/cards/frames/<角色>_ancient_frame_mat.tres`（按颜色的 HSV 写一份 `hsv.gdshader` 材质）+ 一个**外观池** `<角色>AncientStylePool`，先古卡的 `VisualCardPool` 指过去；**先古卡**额外生成 `<角色>StyledCardTint.cs`（染先古边框 / 横幅的 modulate，见下） |
| 本体框色 `card_frame_blue` | 只生成外观池，`CardFrameMaterialPath` 直接用那个本体框色（不用写材质）；先古卡的染色补丁按这个框色对应的种子色染 |
| 跟角色配色 | 什么都不生成（诅咒仍用本体 `CurseCardPool`，先古卡用角色卡池） |

**为什么是「外观池」**：本体的边框材质只能从**池**上取 ——
`CardModel.FrameMaterial => VisualCardPool.FrameMaterial` → `CardPoolModel.FrameMaterialPath` →
`res://materials/cards/frames/<名字>_mat.tres`，卡本身没有可覆写的口子。
本体自己也是这么干的（Clash / DualWield 覆写 `VisualCardPool` 借铁甲战士的卡框）。
这个外观池**不装任何卡**（`GenerateAllCards() => []`），也不进 `ModelDb.AllCardPools` ——
所以它不会在卡牌图鉴里多出一个空分类。

**先古卡还要额外一个染色补丁**（诅咒不用）：先古卡在卡面节点里**根本不显示普通的 Frame** ——
`NCard.Reload()` 里写死了 `_frame.Visible = (稀有度 != Ancient)`，先古卡显示的是
`AncientBorder` / `AncientTextBg` / `AncientBanner` 三张**先古专用贴图**，它们没有材质、只吃节点的
`modulate`（本体自己就是用 modulate 给它们上色的：场景里 `AncientBorder` 的默认 modulate 是
`(1, 0.978, 0.906, 0.502)`）。所以「只换卡框材质」对先古卡一点效果都没有 ——
生成器会额外产出 `<角色>StyledCardTint.cs`：在 `NCard.Reload` 之后，把属于这个外观池的先古卡的
`AncientBorder` / `AncientBanner` 的 modulate **乘上**你要的颜色（记住本体原色再乘，避免 Reload
反复调用越乘越暗；选的是本体框色名时用那个框色对应的种子色，不会生成 `Color("#")` 这种解析不出来的值）。
这个补丁在模组入口里**手动打**并包在 try/catch 里：万一本体改了方法名，
最多是没有染色，不会连累角色注册。

**改不了的那两项**：牌堆底色（`card.Pool.DeckEntryCardColor`）和能量图标描边
（`NCard` 里的 `Model.Pool.EnergyOutlineColor`）取的都是**真实卡池**，而真实卡池必须装这张卡 ——
所以按「类」改不了，外观池里照抄角色的那份（工具里也就不给这两个填框，免得填了没反应）。

### 五、回读（从工程恢复存档）

- 诅咒：构造函数里的费用（默认 `-1`）+ `Curse` + `OnTurnEndInHand` 里的语句会把它还原回「诅咒」列表
  （费用填 ≥ 0 的老工程也能读回那个数字）；「战斗结束自删」按 `CardPileCmd.RemoveFromDeck(this)` 认出来。
- 先古卡：按构造函数里的 `CardRarity.Ancient` 还原回「先古卡」列表。
- 状态卡：稀有度 `CardRarity.Status` 的普通卡 —— 回读后仍然落在 `Profile.Cards` 里（这一页那一栏会自动列出它）。
- 三类都按**稀有度**分回各自的列表（不是按它原来在哪个列表），所以老存档里手改过稀有度的卡也对得上。
- 卡框颜色：从外观池的 `CardFrameMaterialPath` 认（自定义色再去材质文件里按 h/s/v 反算回 RRGGBB）。
  顺带修了一个老 bug：以前那个反算函数的路径少了两层、还按 `shader_parameter/color` 找颜色，
  所以**角色自己的自定义边框颜色从来没读回来过**（静默退回默认红）—— 现在角色 / 诅咒 / 先古卡三处都能读回来了。

## 无色卡（「卡牌」页 → 「无色卡」）

卡牌页勾上「**无色卡**」，这张牌就**进本体的无色卡池**，和本体那些无色牌（`ColorlessCardPool`）放在一起：

- **卡框 / 能量图标用本体无色那套**（`card_frame_colorless`，"colorless" 能量前缀）——
  这张牌的卡框颜色、牌堆底色都由**本体无色卡池**决定，「卡牌」页的卡框颜色对它不生效（设计如此）；
- **不进你角色的卡池**：战斗奖励 / 商店抽的是**你角色的卡池**，所以无色卡不会自己冒出来；
  它只会从「无色卡池」相关的来源给到玩家（本体那些按无色卡池发牌的遗物 / 事件 / 药水）。
  「加入卡池」那个勾对无色卡没作用（校验器会提示一句，不静默）；
- 它照样能在**你自己的**卡组 / 效果里被引用（生成卡牌的范围里选中无色卡池、指定卡、升级卡牌都行）。
- **卡面素材要写在 `colorless/` 目录下**（V0.2.114 修的坑）：本体是按**卡所在那个池**的
  `Title` 去找卡面的（`CardModel.PortraitPngPath` = `packed/card_portraits/{Pool.Title}/{卡ID}.png`，
  `.tres` 同理），而无色卡登记进的是**本体的无色卡池**（`ColorlessCardPool.Title = "colorless"`）——
  所以生成器现在把它的卡面写进 `images/packed/card_portraits/colorless/` 与
  `images/atlases/card_atlas.sprites/colorless/`。
  以前一律写进你角色自己的池目录里（`card_portraits/<你的池>/…`），游戏按 `colorless/` 去找 → 找不到，
  表现就是**这张牌在游戏里没有卡面**（用户报的「无色牌卡面丢失」）。普通卡 / 诅咒 / 先古卡照旧写在你自己的池目录里。

**怎么做到的**：本体的 `ColorlessCardPool` 是 `sealed` 的、成员表硬编码在类里，卡池成员只能靠
`ModHelper.AddModelToPool(池类型, 卡类)` 在**游戏初始化之前**登记（晚了本体直接抛
`it's too late! You must add content before the game is initialized.`）。所以生成器会在
`cs/ModEntry.cs` 的 `Initialize()` 里为每张无色卡写一句：

```csharp
var __colorlessPool = typeof(MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool);
MegaCrit.Sts2.Core.Modding.ModHelper.AddModelToPool(__colorlessPool, __t你的卡类);
```

（包在 try/catch 里、按类名反射查找，找不到只记一行警告，不会连累角色注册。
这条是「卡池成员一律在自己池类的 `GenerateAllXxx()` 里给出」那条规矩的**唯一例外**，
`cs/ModEntry.cs` 里也写了原因。）

**稀有度只能是 Common / Uncommon / Rare**：本体的 `CardFactory` 对 Ancient / Event / Quest / Token
这几档有特殊处理（会把它们当成「无色」用在别的用途上），放进无色卡池会出怪事 —— 配置检查会**报错拦住**。

**回读**：`--recover` 扫 `cs/ModEntry.cs` 里那些 `AddModelToPool(__colorlessPool, …)` 语句，
把对应的卡还原成「无色卡」；没勾无色卡的存档，生成结果一个字符都不变。

## 自定义关键词（「自定义关键词」页）

这一页做两件事：**改本体关键词的名字**、**加自己的新关键词**。两件都不需要 Harmony 补丁。

### 一、本体关键词改名（消耗 / 虚无 / 固有 / 不能被抽出 / 保留 / 奇巧 / 永恒）

页面最上面那 7 行就是本体的 7 个关键词：填「新显示名」（**留空 = 不改**），想连悬停里的说明一起改就再填「新说明」。

- **机制**：本体加载本地化时会把模组的 `localization/zhs/card_keywords.json` **逐键合并**进本体那张表
  （模组的键盖本体的），所以只写 `EXHAUST.title = 你的名字` 就生效 ——
  卡面那个金色词是本体读本地化标题拼出来的，自动跟着变。
- **连带替换**：本体卡牌 / 遗物 / 药水**描述正文**里出现的旧中文名（比如「消耗」两个字）也会一起换成新名字
  （和「本体状态改写」改 Power 名字的做法一致），否则会出现「卡面上写新名、描述里还是旧名」这种不一致。
  实测把「消耗」改成「献祭」时，会连带覆盖 40 条本体文案（卡牌 29 / 遗物 8 / 药水 3）。
- **注意**：这是**全局**改动 —— 本体其他角色的牌上出现的同一个关键词也会跟着改名。

### 二、新增自己的关键词

本体那 8 个关键字是**封闭枚举**，模组既加不了枚举成员、也没有注册 API。所以自定义关键词走的是
**等价机制**（全程只用本体的公开 API，不改本体、不需要 Harmony 补丁）：

| 环节 | 做法 |
|---|---|
| 文案 | 生成时写进模组工程的 `localization/zhs/card_keywords.json`。本体加载本地化时会把模组的同名表**逐键合并**，所以只加自己的键、不影响本体那 8 个关键词 |
| 悬停说明 | 引用它的卡牌会生成 `ExtraHoverTips`（`CardModel` 的虚属性），鼠标悬停在卡上就弹出「名字 + 说明」 |
| 卡面文字 | 描述**最前面**自动拼 `[gold]名字[/gold]。`，和本体 `消耗 / 虚无` 那类关键字一样的观感 |

用法：

1. 「自定义关键词」页 → 「添加关键词」，填**名字**（卡面上显示的那个词）、**英文标识**（本地化键，
   留空自动用 `KEYWORD_1`、`KEYWORD_2`…）、**说明**（悬停时弹出的正文）。
2. 到「卡牌」页勾选哪张牌用这个词（勾选存在卡牌上，一张牌可以用多个）。
3. 生成时自动产出上面三样东西；「卡牌」页的勾选也会随工程回读一起找回来。

几条约定与限制：

- 英文标识不能和本体占用的键撞名（`NONE / EXHAUST / ETHEREAL / INNATE / UNPLAYABLE / RETAIN / SLY / ETERNAL`），
  撞了生成前校验会报错；同名/同标识的重复关键词也会被拦住。
- 删除关键词时，卡牌上对它的引用会一并清掉（否则校验会报「引用了不存在的关键词」）；
  但「撤回删除」只恢复关键词本身，卡牌上的勾选要重新点。
- **升级后获得 / 失去自定义关键词**（V0.2.136）在「卡牌」页「升级后的关键字」**底下**那两栏里配，
  见下面「升级后获得 / 失去自定义关键词」。
- 关键词**没有图标**（本体关键字也没有），也不参与本体的关键字筛选/被别的模型按关键字引用。

## 升级后获得 / 失去自定义关键词（V0.2.136）

需求原文：《卡牌页优化.md》第 2 条 ④「**升级后的关键字底下增加对自定义关键词的处理**」。

「卡牌」页「升级后的关键字」那块**底下**多了两栏：**升级后获得（留空 = 不变）** / **升级后失去（留空 = 不变）**，
候选就是你在「自定义关键词」页里加的那些词。留空 = 一个字都不改（老存档就是空，产物逐字节不变）。

> ⚠ **这两栏只影响显示，不是机制变化** —— 界面上也是这么写的。
> 本体的 `CardKeyword` 是**封闭枚举**（模组加不了新值），所以自定义关键词本来就是
> 「卡面那行 `[gold]名字[/gold]。` + `ExtraHoverTips` 悬停说明 + `card_keywords` 本地化键」拼出来的**显示等价物**，
> 游戏侧**没有任何按它判定的逻辑**。对比上面那 6 个**本体**关键字：它们走 `AddKeyword` / `RemoveKeyword`，
> 会**真的改变卡牌状态**，别的按关键词判定的逻辑看得见。

生成成什么：

| 环节 | 做法 |
|---|---|
| 卡面描述 | 关键词那行按升级前后分叉，整段用本体的 `{IfUpgraded:show:升级后那段\|没升级那段}` 包起来（⚠ **顺序固定是「升级后\|没升级」**，描述里自己写的字面量 `\|` 会被本体当成选项分隔符，生成端会换成全角 `｜`） |
| 悬停说明 | 生成的 `ExtraHoverTips` 按 `base.IsUpgraded` 条件加 / 不加那条（升级后才有的词，没升级时悬停也不会冒出来） |
| 回读 | 卡牌源码里写 `// CET:UpgradeKeywordAdd=` / `// CET:UpgradeKeywordRemove=`（**只在真填了才写**，空的那一栏不写；两个标记各自一行 —— 同一行上「空值 + 下一个标记」会被贪婪匹配吃串） |

校验器：

- 那两栏引用了**不存在的**自定义关键词 → **错误**，并点名是哪张牌的哪一栏（生成被拦住，不许静默）；
- 同一张牌把同一个词同时填进「获得」和「失去」 → 警告（互相抵消，等于升级前后都不显示）；
- 填了但那张牌**升不了级**（诅咒 / 状态牌，`MaxUpgradeLevel = 0`）→ 提示说清这两栏永远不会生效；
- 诅咒 / 状态牌上也**照写**回读标记（不写的话，「生成 → 从工程恢复」会把那个词搬到「一直都有」那一栏）。


## 召唤伙伴（「角色」页 → 「召唤伙伴」）

给角色配一个**独立的伙伴单位**（类似亡灵缚者的奥斯提、静默猎手的伯德皮普）：有自己的血量、能被指挥攻击。
「角色」页那一组里勾上「启用召唤伙伴」，填名字 / 英文类名 / 生命，可选上传一张 PNG。

它走的是**本体的通用宠物 API**，所以**不需要任何 Harmony 补丁**：

| 环节 | 做法 |
|---|---|
| 兽体 | 生成一个 `MonsterModel` 子类（`cs/Pet.cs`）。本体 `ModelDb` 会自动扫模组程序集里的子类，**不需要注册**；类名不能和本体怪物重名（会抛 `DuplicateModelException`） |
| 召唤 | 走 `PlayerCmd.AddPet<T>(player)`（本体 Byrdpip / Pael's Legion 就是这么做的），再用 `SetMaxHp` + `Heal` 把血量设成你配的值 |
| 名字 | 写进 `localization/zhs/monsters.json` 的 `<ENTRY>.name` |
| 视觉 | 上传了图 → 生成最小 `scenes/creature_visuals/<entry>.tscn`（静态 Sprite2D + 本体要求的四个唯一名节点）；没上传 → 用本体的 `creature_visuals/fallback` 占位图，**能上场 / 能打 / 能死**，只是不好看 |

两种新的「效果」用来驱动它：

- **召唤伙伴**（卡牌或遗物触发都行）：数值 = 召唤时的生命，**填 0 = 用配置里的血量**。
  已经有一只活着的时候**不会召出第二只**，而是给它加最大生命并回满（照本体 Osty 的做法）。
  放在遗物的「战斗开始时」就是「开局自动召唤」。
- **伙伴攻击**（只能放在卡牌上）：数值 = 伤害，目标跟卡牌的目标选择一致。生成的代码是
  `DamageCmd.Attack(n).Targeting(...).FromMonster(pet.Monster)`；**宠物不在场时会安全跳过**，不会让整张牌报错。

宠物类的效果一共 12 个（召唤伙伴 / 伙伴攻击 / 伙伴攻击·按最大·当前·已损失生命值 / 治疗伙伴 /
伙伴失去生命 / 伙伴最大生命 +N / 牺牲伙伴换收益 / 给伙伴施加状态 / 伙伴替主人承伤 开·关），
它们都在**「卡牌」页的效果种类下拉**里，直接选就行；选到宠物效果时效果栏里会多出
**「召唤物（哪一只）」**下拉（留空 = 用「召唤物」页里第一只启用的）。下拉里第一条是
**「★ 全部召唤物」**：这条效果对**每一只启用的**召唤物各算一次（生成时逐只展开，每只自己判在不在场，
不在场的自动跳过）—— 例：一张牌把三只全召出来、或让每一只都替你承伤。

「牺牲伙伴」的收益有三档：**格挡**（给自己）/ **伤害**（打敌人，按「作用对象」）/ **获得增益·减益**
（层数同样按「固定 N / 最大生命 × 倍率 / 当前生命」算，状态挂在**你自己**身上，选的是哪个状态复用的就是
上面「增益 / 减益」那一栏）。三档的生成顺序都是**先算收益（或层数）、再杀宠物**——
宠物死了就取不到它的生命值；收益是状态时最后再 `PowerCmd.Apply` 把状态挂上，生成的代码里写了这个顺序的原因。

已知限制（都是本体机制，不是工具的问题）：

- 宠物**不跨战斗**：战斗结束时本体 `PlayerCombatState.AfterCombatEnd()` 会清空宠物列表，每场战斗都要重新召唤
  （所以「伙伴成长」这种设计要靠遗物/状态记录，不能靠宠物自己）。
- **敌人默认打不到宠物**（玩家侧的兽体不在敌人的目标集合里）。勾上某只召唤物的「替主人承伤」会为它挂一个
  伤害重定向守卫（照本体 `DieForYouPower` 写），主人受到的**可格挡攻击伤害**改由它承担；
  中毒 / 失去生命这类穿盾伤害照旧打在主人身上。
  **可以勾多只**：承伤的是召唤物列表里**第一只活着的**，它死后自动换下一只。
  本体的重定向是链式的（`Hook.ModifyUnblockedDamageTarget` 依次把 target 传给每个监听者），
  照本体的写法会变成「谁先监听谁生效」、不可预期，所以由我们自己的守卫仲裁（校验器不再拦，只给一句提示）。
- 宠物**没有自主回合**，攻击完全靠玩家的「伙伴攻击」卡驱动；它头顶不会有意图图标。
- 不要用本体的 `OstyCmd.Summon` / `AttackCommand.FromOsty`（它们硬编码奥斯提类型，换别的伙伴会直接抛异常）。



## 卡牌配色（「卡牌」页左上角）

本体里卡牌颜色是**卡池级**的三个字段，所以这里也是整个角色统一生效：

| 字段 | 含义 | 可取值 |
|---|---|---|
| 卡牌边框 | `materials/cards/frames/<框色>_mat.tres` | `card_frame_red` 铁甲红 / `card_frame_green` 静默绿 / `card_frame_blue` 缺陷蓝 / `card_frame_pink` 亡灵粉 / `card_frame_orange` 摄政橙 / `card_frame_colorless` 无色灰 / `card_frame_curse` 诅咒 / `card_frame_quest` 任务 |
| **卡牌边框颜色** | 任意颜色：按该色值的 HSV 生成一份自己的边框材质（本体边框材质就是 `hsv.gdshader` 的 h/s/v 三个参数），`CardFrameMaterialPath` 指向它 | `RRGGBB`；**留空 = 用上面的官方框色** |
| 牌堆底色 | 见下方说明 | `RRGGBB` 或 `RRGGBBAA`（可带 `#`，会自动规范成大写） |
| 能量图标描边 | 卡面上能量数字的描边色 + **左侧能量计数器数字的描边色** + **左侧能量球配色**（5 层贴图 + 2 个粒子光效按该色相重新着色） | 同上 |

界面上有 8 个**配色预设**（就是把各角色官方原值一键套用），两个下拉的**最后一项都是「自定义」**：

- 「配色预设 → 自定义（手动填写下面各项）」：只要手动改了下面任意一项，就会自动切到这一项；
  选了某个预设就套用它的三个值（并清掉自定义边框色）。
- 「卡牌边框 → 自定义（用下面的边框颜色）」：切到它会给一个相近的种子颜色（例如从铁甲红切过来是 `D62000`），
  你再改成任意色值即可；选了官方框色则自动清掉自定义颜色、回到官方材质。

右侧有色块实时预览。填错格式会在生成前的校验里直接报错拦住。

**「牌堆底色」到底改的是哪里？** 本体里它叫 `CardPoolModel.DeckEntryCardColor`，唯一的使用点是
`NTinyCard._cardBack.Modulate`——也就是**查看牌组/牌堆界面里那些小卡背面的染色**（配合小卡背用的
`FrameMaterial`）。它不是卡面本身的颜色；卡面颜色由上面的边框材质决定。

## 自定义参数 / 组（「自定义参数 / 组」页）

上半部分是**参数**（一个字母 = 一个中间变量），下半部分是**组**（只用来分类查看）。

### 参数

- **新建参数**会自动挑一个还没被占用的字母（M、N、P…）；`X` / `Y` 是保留的
  （X = 当前所有能量，Y = 当前所有额外资源量），不能拿来当自定义参数。
- 每个参数的**取值来源**可选：数值（固定数字）/ 当前血量 / 最大生命 / 金币数 / 能量数 / 最大能量数 /
  增益减益（状态层数）。选「增益 / 减益」时再选是哪个状态；「从谁身上取」可选自己 / 指定敌人 /
  随机敌人 / 全部敌人 / 全部友方 / 召唤物 —— 例如「自己 + 最大生命」就是 `M=自己最大生命`，
  「指定敌人 + 易伤」就是 `M=被指定敌人的易伤层数`。
- 这些值**打出牌的那一刻才知道**，所以在生成出来的模组里**运行时现算**（血量、层数变了结果就跟着变），
  不是在生成时算成固定数字。
- 取值来源的清单里除了上面那几种，还有一批「**实时读数**」（就是下面「内置只读量」那张表，
  回合数 / 丢弃·消耗·生成·复制的张数 / 花掉的能量 / 获得的能量 / 获得的额外资源量 / 身上的状态数…）——
  选它们就等于把这个读数当成参数用，每次取值现算。其中
  `EnergyGainedThisTurn` / `EnergyGainedThisCombat`（本回合 / 本场战斗**获得的能量**）本体没记，
  用到时才会生成 `cs/EnergyGainTracker.cs`（见「内置只读量」那一节）。
- 参数页右边有「给予能量 / 给予额外资源量」两个指定：把某个参数指定成「战斗开始时给多少能量 /
  多少额外资源量」。默认那一套就是 X 与 Y（= 把当前能量 / 额外资源量全部给出去）。
- 卡面自定义描述里的公式可以照常引用这些字母（和数值模块里写 `=M*3` 是同一套写法）。

### 组

- **新建组**可以命名；**卡牌属性**标题右边会多出一排勾选框（也可以在本页右边对整个组全选 / 全不选），
  勾上就表示这张牌属于这个组。
- 组存的是**内部标识**而不是组名：改组名**不会**让已经勾好的卡掉出去。
- 组只影响**生成器里的查看与筛选**（「范围」里会多出「组：XXX」这一档），不写进生成出来的模组代码。

## 锻造（「锻造」页）

「锻造」页用**另一套逻辑**来做卡牌 / 药水 / 遗物（没有那四个费用框、也不会去动「升级后费用」）：

- 最左侧上方选**卡牌列表 / 药水列表 / 遗物列表**；一类都不选时右侧整块空白，只提示
  「请选择卡牌、药水、遗物」。选了之后左侧下方列的就是这一类里**用锻造页做出来的**那些。
- 点**添加**会生成一项**名字前面带 `*`** 的条目；这些条目在原来的「卡牌 / 遗物 / 药水」页里
  被选中时右侧不显示编辑器，只提示「请在锻造页查看」。
- 锻造页中间那一栏还是照常改**名称 / 类型 / 稀有度 / 英文类名 / 卡牌关键字**这些；
  **没有**费用 / 额外资源量费用 / 升级后费用那四栏（V0.2.131 起整块藏掉）——
  它们归最右侧那块「布」（节点图）里的【触发时机】/【能量】模块管。
- 最右侧的节点图（节点编辑器）**已经接上**，见下一节。

## 节点图（锻造页最右侧那块「布」）

锻造页的右侧就是节点编辑器：**中间是【触发时机】模块**（默认「卡牌被打出时」），左上角固定一个【能量】模块。

### 怎么用

### 编辑器操作

- **加模块**：上面那个下拉选种类 → 「添加」。同类物品能用哪些触发时机是筛过的（卡牌 / 药水 / 遗物各一套）。
- **移动**：拖模块的标题栏。**多选之后拖任意一个，选中的会一起移动**（【能量】固定不动）。
- **多选**：在空白处**按住左键拖出一个框**（框选）；`Shift` 点模块加选 / 取消；「全选」按钮或 `Ctrl+A` 全选。
- **删除**：「删除选中」（或模块标题栏右边的 `×`、或按 `Delete`）——连着它的线一起清掉。
- ⚠ **【能量】是固定模块**（左上角那一块）：**不能选中、不能拖动、不能删除**（点它只会提示一句；
  「全选」/ 框选也会跳过它；它的标题栏上没有 `×`）。它只负责把「当前能量 / 当前额外资源量」送出去，
  要改就在它自己那两栏里改（打出时消耗多少）。积木视图的调色板里也没有它 —— 布上本来就有一块，不用再加。
- ⚠ **【触发时机】第一块是固定模块**（V0.2.128 起）：**删不掉、也不会被「全选」带走**
  （整张图没有它就不生成）—— 它的标题栏上同样没有 `×`，按 `Delete` 只会提示一句。
  它**照常可以选中、拖动**（位置还是你自己摆的）；要换时机就在它自己那个下拉里换。
- **一块布上可以有多块【触发时机】**（V0.2.168 起）：再从上面那个下拉里选一次【触发时机】就能加第二块、第三块，
  **每块配自己右边那一条链**（例：一块「卡牌被打出时」→ 造成伤害，另一块「这张牌被消耗时」→ 抽 1 张牌）。
  - **第一块**仍是固定模块（删不掉、不被全选带走）；**第二块起是普通模块**（能删能选能拖，
    积木视图的调色板里也能加）。
  - 新加的那一块默认挑「这块布上**还没用过**的时机」，不会一加出来就和第一块落在同一个时刻。
  - ⚠ 两块落在**同一个本体钩子**上时（比如两块都是「卡牌被打出时」）—— 那会是同一个类里两个同名方法
    （整个模组编译不过），所以**只生成第一块**，配置检查会**报错**说清（合成一块，或换别的时机）。
- **连线**：先点右边一个**出口**圆点，再点左边一个**入口**圆点就接上了；点空白处取消。
  - 一个入口只接一条线（再连会替换掉旧的）；不能自己连自己；
  - **连错了会画成红色虚线，下面一句写清为什么**（比如「这里要接【条件】，不能接数值」）；
  - 圆点有字时以小字显示，没字就只有一个圆点。
- **填入参数… / 数值栏里的公式**（V0.2.128）：
  - 【数值】模块那个多行框和**模块里那些单行数字框**（见下面「数值栏也能填公式 / 参数」）都能写公式；
  - 【数值】模块那个「填入参数…」按钮、以及每个数字框右边那个「**…**」按钮，
    点一下就把 `X` / `Y` / 你定义的参数 / 内置只读量插到光标处；
  - 布顶部那行灰字就是**可用量清单**（鼠标悬停有每一条的中文说明）。

### 左上角的「升级前 / 升级后」

卡牌有两块布：**升级前**（默认）和**升级后**。「升级后」那一页的左上角有一个悬浮按钮 **「一键复制『升级前』的内容」**，
把升级前的模块、位置、设置、连线整份复制过来（复制出来是**两块独立的布**，改一边不会影响另一边）。

两块布的**差异**会写在编辑器下面那一行（绿字）：升级前几块 / 升级后几块、约几块改了设置、多了几块、少了几块
（位置移动不算差异）；升级后那块布空着时会直接写「跟升级前一模一样」。

生成时的规则：

| 两块布的状态 | 生成出来 |
|---|---|
| 升级后空着 | 升级前后都走「升级前」那张布 |
| 两块都设了【触发时机】=「卡牌被打出时」 | 生成两个方法（`ForgeGraphOnPlay` / `ForgeGraphOnPlayUpgraded`），运行时按 `base.IsUpgraded` **二选一**（不是两套都跑） |
| 只有升级后那块布有「打出时」 | 直接调升级后那个方法 |

节点图（含升级后那块布）会**存进存档**，下次打开还在。

**V0.2.134 起，节点图也能「从工程恢复」了**：生成时会把整张图写成一行标记
`// CET:ForgeGraph=<版本>:<图的紧凑 JSON 的 Base64>`（放在生成出来的类里、紧贴在「===== 锻造节点图」那一段前面）。
`--recover` 按这行标记重建 `ForgeGraph`（节点、每一格字段、连线、升级后那块布、【效果】模块的编辑器内容全都逐字段还原），
并把条目置成 `IsForged = true`。

- **只有图非空时才写这行标记**，所以**没有锻造图的存档生成结果一个字节都不变**（示例存档 / `--samplegen` 实测逐字节一致）。
- 代价：**含锻造图的存档**生成出来的那几个 `.cs` 会多出这行标记（每个条目每块布一行，Base64 之后一行大约 10~40 KB）。
  V0.2.134 的实测：`DarkVinci` 6 张卡各多 2 行、`SparkleMod` 2 张卡（其中一张只有升级前那块布 = 1 行），
  这些文件里**除了这行标记一个字都没改**（逐行对拍核过）。
- 老工程（V0.2.133 及以前生成、没有这行标记）**照旧**：图仍然是空的、`IsForged` 仍然是 false，
  也**不会**多出一条「没认出来」——只是图恢复不回来（要拿回图请用生成它的那一版重新生成一次）。
- 标记坏了 / 版本不认识**一定说清**，绝不静默丢图：版本不对会说「这个工程是更新的版本生成的」，
  Base64 / JSON 坏了会说「这一段被手改坏了」；两者都会进「从工程恢复」的「没认出来」清单。
- 生成端自己也做一道自检：每个带标记的文件写完后立刻解开验一遍，解不开就直接报错中止生成
  （不然要等到存档丢了才发现没得恢复）。

V0.2.128 起，回读还会把**生成出来的「锻造节点图」那几段方法整个跳过**（以前它们会被当成
「卡牌时机」的效果 / 遗物的一条触发时机去解析，平白留下几行「没认出来」）—— 图现在由上面那行标记负责恢复，
方法体照旧跳过，回读不会有那些假报警。

### 数值栏也能填公式 / 参数（V0.2.128）

用户要求：「锻造页中所有模块里能填入数值的地方，都能填入参数或者公式」。**数字框保持原样（单行）**，
只是里面可以直接填公式文本了 —— 一共 12 处：

| 模块 | 栏位 |
|---|---|
| 【触发时机】 | 血量低于 / 到达 N 层 / 仅生效 N 次 |
| 【能量】 | 打出时消耗的能量 / 打出时消耗的额外资源量 |
| 【效果】 | 数值 / 生效次数 / 命中次数 / 概率生效 |
| 【条件】 | 数值 N / 数值 M |
| 【关系符】 | 右值 |

可用量和【数值】模块**完全同一套**（`X` = 当前所有能量、`Y` = 当前所有额外资源量、一个字母的自定义参数、
80 条内置只读量），语法也一样（`+ - * / % ( )` 和 `min/max/floor/ceil/round/abs`）。三种写法：

| 你填的 | 生成出来 |
|---|---|
| 纯数字（`6`、`0.5`） | **和以前一模一样**（默认档生成的文本一个字符都没变） |
| 生成期就能算出来的公式（`2*3`、`A+3`，A 是「固定数值」来源的参数） | 生成期折叠成那个数，生成出来还是老样子 |
| 用了运行时才知道的量（`X`、`Y`、`Turn`、`M`…） | 生成一段运行时表达式：数值 → `decimal __fgAmt… = <表达式>;`、生效次数 → 真的 `for` 循环、命中次数 → `.WithHitCount(<表达式>)`、概率 → 按算出来的百分比判定 |

⚠ 两条硬边界（都是本体的限制，界面 / 配置检查里都写明了）：

- **【能量】那两栏只收「生成期就算得出来的数」**：它们会写进卡牌自己的费用（本体 `base(cost, …)` /
  `CanonicalStarCost` 要**编译期常量**）—— 写 `X` 会由配置检查**报错**拦住，写 `1+1` / `A+3` 可以；
- 「**当遗物被点击时 / 血量低于 N**」的进门条件（血量阈值 / 生效次数）写在执行点最前面，写了运行时公式时
  会自动挪到「先算好 X / Y / 参数」那几句之后（语义不变），所以这几个位置的自定义参数、内置只读量都能用。

界面上：填对了是灰框；**填了运行时公式的框标蓝**（ToolTip 里写出它会变成什么表达式）；
**填错了当场描红**（ToolTip 写出原因），配置检查里也会报一条「**哪个模块 · 哪一栏**」的错误 ——
不再像以前那样悄悄退回默认值。

> V0.2.129 起【效果】模块换成了「卡牌」页那套效果编辑器（见下一节）—— 那一块里的
> 「数值 / 生效次数 / 命中次数 / 概率」是**模板里的文本框**（不是上面这种带「…」按钮的数字框），
> 公式直接在框里写就行（同一套 `ForgeNumbers` 语法、同一份可用量）；其余模块的数字框照旧。

### 【效果】模块 = 卡牌那套编辑器（V0.2.129）

用户要求（《锻造优化.md》第 3 条）：「【效果】模块里面每一条效果的选择出现的可填写项与卡牌页处的效果保持一致。」
用户已拍板（见 `docs\需求\已拍板决定.md` Q1=A）：**直接复用卡牌那套 `EffectSpec` 编辑器**，
不再给 `ForgeNodeField` 一栏一栏补（那样会永远漂）。

怎么做成的（对用户可见的三件事）：

1. **选一个效果种类，底下出现的可填写项和「卡牌」页一模一样** —— 同一份模板（`EffectCommon`）、
   同一份显隐判据，所以「取卡方式 / 范围限定」只在生成类效果上出现、「目标药水」只在获得药水上出现、
   「复制的份数」只在复制卡牌上出现…两边永远同步。
2. **数值 / 生效次数 / 命中次数 / 概率这四栏在锻造图里能直接写公式或参数**（V0.2.128 的能力原样保留，
   见上一节）：写法、可用量、三档语义都和别处同一套。
3. **「作用对象」这一栏也摆出来了**，但它的优先级是：**显式接在【效果】左边「指向对象」口上的【对象】模块
   最优先**；没接线而你又改过「作用对象」→ 按你改的那一档（自己 / 指定队友 / 全体友方 / 全体敌人 /
   随机敌人）；都没动过 → 照老规矩（这条链上最近一个【指向对象】声明的那张牌选中的目标）。
   系统会提示「接线盖住了作用对象」这种情况，不会闷着不说。

**老存档一个字节都没变**（这是本版最硬的一条）：存档里【效果】模块原来存的是 `ForgeNodeField` 那一串老字段
（`Effect` / `Amount` / `Times` / `Hits` / `Chance`…），打开存档时会**幂等**迁出一份共用的 `EffectSpec`
给编辑器用，但**不回写老字段** —— 生成端读的还是那份老字段，所以老存档生成出来的代码 / 卡面文案逐字不变；
你**改过**之后才把编辑器里的内容回写进老字段（此后编辑器说了算）。再打开一次存档、再生成一次都不会改动一个字节。

**没接上的种类 / 栏位一律被「配置检查」报错拦住**（不许静默生成一半）：

- ⚠ **V0.2.170 起，「栏位」这一类缺口基本清零了**（以前报错拦住的都真的能用了）：
  · **「取卡方式」四档**（指定卡 / 按范围随机 / 多选 1 / 从范围里自己挑一张）—— 生成卡牌 / 变化卡牌 /
    加牌进牌组（全局）/ 变化卡牌（全局）四种都能用；
  · **「取几张」四档**（恰好 N / 最多 N / 任意张 / 全部）；
  · **「下回合生效」铺到全部做得了的种类**（只有本体没有延迟落点的 8 种仍按立即生效生成并警告）；
  · 要「打出这张牌」那一刻上下文的 **重放卡牌 / 回合结束时自动打出 / 将此牌放回手牌 / 斩杀条件**。
  做法是把「卡牌」页那几份发射器的上下文（`base.Owner` / `choiceContext` / `this`）开成参数，
  节点图传 `__fgPlayer` / `__fgSelf` / `ctx.ChoiceVar` —— 一份实现两条链共用。
  两个注意点（校验器会说清）：**「将此牌放回手牌」要挂在「这张牌被消耗时 / 被丢弃时」**这类
  「牌已经不在手牌里」的时机上；**「斩杀」条件只有卡牌档 + 拿得到 `cardPlay` 的时机**才成立。

- 节点图这一版实现的是 **41 / 65 种**（清单 = `ForgeGraphGen.SupportedEffectKinds`）。
  另外 **35 种**（`OverdraftEnergy` 透支能量 / `Expose` 暴露 / `CheapenDraw` 抽到就减费 / `CheapenSelf` /
  `ReturnSelfToHand` / `CheapenSelfOnDraw` / `RaiseResourceCap` / `AncientTranscend` / `AncientRelicSwap` /
  `ForbidHeal` / `BoostCard` / `ExtraTurn` / `ReplayCard` / `TurnEndPlay` / `ScrapeDraw` / `PlaceCard` 放置卡牌 /
  `Outbreak` / `TimesUp` / `FillHand` / `GiveKeyword` / `AddCardGlobal` / `TransformCardGlobal` /
  `RemoveCardGlobal` / `UpgradeCardGlobal` / `SummonPet` / `PetAttack` / `PetDamageByMaxHp` /
  `PetDamageByCurHp` / `PetDamageByMissingHp` / `PetHeal` / `PetLoseHp` / `PetGainMaxHp` / `PetSacrifice` /
  `PetGuardOn` / `PetGuardOff`）**在节点图里选了会被报错拦住**，报错里写清「换成节点图支持的种类」或者
  「把这一项从锻造页移出去（不勾「锻造」）之后到「卡牌 / 药水 / 遗物」页的效果列表里配（那边支持全部 65 种）」。
- 编辑器上有、但节点图这一版**不生成**的栏位也报错说清（V0.2.170 之后**只剩**：「直接把缓慢设成 N%」、
  以及卡牌专属的「升级增量 / = X / = 本状态层数 / 条件选项」；以前列在这里的
  「按范围随机 / 取几张 / 从哪里选牌 / 下回合生效」几项都已经真的支持了）。
  —— 也就是说「选了却生成不出来」这一类静默坑，现在一定会在「配置检查」里看到一条错误。
- ⚠ **V0.2.167 起这几项节点图也支持了**（以前报错拦住，用户实测「生成被拦住了」就是它）：
  **「生成 / 变化出来的卡的附加处理」**（直接升级 / 免费 / 仅本回合免费 / 仅本回合升级）、
  **「生成 / 变化出来的卡：加上关键词」**、**「复制的份数」**。
  实现方式：这几栏**只存在新编辑器那份 `EffectSpec` 上**（`ForgeEffectBridge.MirrorToFields` 不回写它们），
  所以生成端改成直接读 spec，并复用卡牌那条链的 `CSharpCodeGen.EmitSpawnModifiers` /
  `EmitTempUpgradePowerApply`（链参数化过）——两条链的语义和文案不会漂。
  没勾这几项的老图，生成文本**逐字不变**（还是那一句 `CreateCard + AddGeneratedCardToCombat`）。

## 积木视图（同一块布，可拖的编辑器）

节点图那块布右上角有 **「视图：节点图 / 积木（可拖）」** 两个单选（默认还是节点图）。
切到「积木」= **同一块布的另一种看法**：按**执行顺序**从上往下一摞类 Scratch 的积木，
缩进 = 在谁里面；【if】把「条件」那条链显示在它下面（标着「条件：」），再是「是：」「否：」两个分支标题
和一个「（if 到这里结束）」收口。
颜色沿用节点图那套（帽子块橙色 / C 形块蓝色 / 报告块淡蓝圆角 / 堆叠块浅灰 + 左侧色条）。

**它是能改的**：

- **拖块换位置**：拖到别的块**中间** = 放进它里面（【if】的两支，或【效果】/【状态】那种**取值口**）；
  拖到上 / 下边缘 = 插在它前面 / 后面。
  拖的时候有半透明预览块，目标会变色（橙色 = 插在旁边，绿色 = 放进里面）；
- **✚** = 在那一块后面加一块（弹菜单挑种类）；**✕** = 删掉那一块和它的内容（同一支里后面的块会接回来）；
- 空布上直接点上面的调色板也能从零搭，会自动补一个【触发时机】当起点。

改完**立刻写回节点图的连线**（节点对象还是同一批，位置 / 字段都不动），所以切回「节点图」看到的就是改完的样子，
生成器读到的也是改完的图。存档格式没变（还是原来那张节点图），老存档照旧能开。

写回时保证一条关键性质：**生成端走出来的执行顺序 = 积木视图从上往下读的顺序**。
做法是「图里一个出口只能接一条线」，所以同一条链上的块必须**一个一个嵌着**接
（第一块接在上一块上，后面的接在前一块的出口 0 上），不能并排躺在同一个出口上 ——
并排的话第二块起会被生成端当成「重复出口」丢掉（表现就是「加了块但游戏里不执行」）。

**取值槽那条线的方向**（【数值】→【效果】的「数值」口、【对象】→【状态】的「归属」…）是
「**子块的出口 → 父块那个入口**」。这一版修掉了一个老 bug：以前一律按「父块的出口」写，
于是【数值】→【效果】那条线会被写成反的（`效果.1 → 数值`）—— 生成端按「谁接到数值口上」找来源，
读不到就**静默退回模块上填的常数**（表现就是「数值块连了但不算」）。

两个已知限制：**（1）** 【且】/【或】门那两条 A / B 线积木视图不接管 —— 写回时**原样留着**，
要改请在节点图里改；**（2）** 把一整个容器（【if】/【效果】）拖进另一个容器里，复杂图上偶尔会有一支要在节点图里补一下线。
放不进去的情况（比如拖进没有「取值口」的【对象】里）会**当场说一句**，不会悄悄写出一条无效的线。

### 模块

| 模块 | 端口 | 说明 |
|---|---|---|
| 【触发时机】 | 右侧 1 个出口 | 整张图的起点。默认「卡牌被打出时」；卡的另有时机：被自动打出 / 被消耗 / 被丢弃 / 被抽到 / **被保留**（V0.2.128 新增，见下）；药水：被使用时；遗物：**和遗物页那份完全同一套**（V0.2.120 起 29 条：获得时 / 战斗开始 / 每回合开始 / 每回合结束 / 战斗胜利 / 战斗结束（房间结算之前）/ 受到伤害（每一下）/ 受到攻击后 / 获得金币 / 洗牌 / 造成伤害（每一下）/ 花掉能量 / 花掉额外资源量 / 生成卡牌时 / 某个状态层数到达 N 层 / 打出一张牌后 / 消耗一张牌后 / 抽一张牌后 / 弃掉一张牌后 / 自己失去生命时 / 自己获得格挡后 / 一个敌人死亡后 / 敌人回合开始 / 敌人回合结束 / 敌人受到伤害后 / 敌人造成伤害后 / 敌人获得格挡后 / 某个状态层数变化后 / 某个状态被移除时），外加锻造页专属的 **当遗物被点击时** / **血量低于 N**。选了「到达 N 层 / 层数变化 / 被移除」时，下面会多出「盯哪个状态」和「到达 N 层」两栏。**第一块是固定模块：删不掉、也不会被全选带走**（照常可以拖动）；**V0.2.168 起可以放多块**（每块配自己那条链，见上面「编辑器操作」） |
| 【指向对象】 | 左 1 个「接入」；右 1 个出口 + 1 个「指向对象」（Object） | **这张牌在游戏里选中谁**（紧接【触发时机】，新建的布上自动接好）。决定卡牌的目标类型（要不要让玩家选一个敌人），也是这条链上「效果没自己接【对象】时」默认打谁。默认「指定单体敌人」 |
| 【if】 | 左「接入」+ 左「条件」（Bool）；右「是」+「否」两个出口 | 条件判断：上面那个「接入」接前置，下面那个「条件」接**能出「成立 / 不成立」的模块**（【条件】/【且】/【或】/【关系符】的「结果」）；右边两个出口各走一条分支 |
| 【对象】 | 左接入点；右出口 | 自己 / 指定队友 / 全部友方 / 指定单体敌人 / 全部敌人 / 随机敌人（带「允许重复选中」勾选，默认勾选）/ 指定召唤物 / 随机召唤物 / 全部召唤物 / 全体单位 |
| 【状态】 | 左「归属」接入点；右「数值」出口 | 本体的状态 + 你自己的状态（**可搜索**下拉），输出它的层数 |
| 【数值】 | 左接入点；右「数值」出口 | 填数字或公式：`M*3`、`X`、`Y`、`min(2, X)`…；支持 `+ - * / % ( )` 和 `min/max/floor/ceil/round/abs` |
| 【能量】 | 右侧两个出口（能量 / 额外资源量） | 左上角固定。**这两栏就是这张牌的费用**：填了 = 卡面 / 游戏里显示这个费用，能量 / 资源量不够时本体不允许打出（能量 → 卡牌构造函数费用，额外资源量 → 本体的星星费用 `CanonicalStarCost`）。两个出口取到的值就是「扣完这张牌费用之后」的当前能量 / 额外资源量。**这块不能选中、不能拖动、也删不掉**（布上固定一块） |
| 【效果】 | 左接入点；右「指向对象」+「数值」两个出口 | **和「卡牌」页的效果编辑器是同一套**（V0.2.129，用户拍板 Q1=A）：同一个 `EffectSpec`、同一份模板，所以**效果种类底下出现的可填写项两边完全一致**（数值 / 数值公式 / 作用对象 / 命中次数 / 生效次数 / 增益减益 / 目标卡 / 生成到 / 取牌方式 / 从哪里选牌 / 取几张 / 范围界定 / 多选1 / 附加处理 / 目标药水 / 复制的份数 / 刮削 / 下回合生效 / 概率 / 缓慢 / 牺牲伙伴…）。差别只有三处，都是锻造图自己的规矩，见下面「【效果】模块 = 卡牌那套编辑器」 |
| 【关系符】 | 左「左值」+「右值」；右「结果」（Bool） | 判断条件是否成立：大于 / 小于 / 等于 / 大于等于 / 小于等于 / 非 / 且 / 或。左右值**既能接数值也能接条件**（选非 / 且 / 或时接条件），于是可以**叠起来**用 |
| 【且】【或】 | 两个条件入口 A、B；一个「结果」出口 | 与门 / 或门（和【关系符】里的「且 / 或」等价，老图用的就是这一对） |
| 【条件】 | 左「N」（可选，接【数值】）；右「结果」出口 | 本体现成的那些条件（和卡牌页条件栏同一份候选） |

### 【指向对象】：牌选中谁 ≠ 效果打谁

用户要求新增的一块，**紧接【触发时机】**（新建的布上自动放好并接好）。它读作「**这张牌在游戏里选中谁**」：

- 决定**卡牌的目标类型**：「指定单体敌人」→ 打出时让玩家选一个敌人（`TargetType.AnyEnemy`，
  于是 `cardPlay.Target` 真的有值）；「全部敌人 / 随机敌人」同理；「自己 / 召唤物…」→ 不需要选目标；
- 同时它是这条链上**「效果没自己接【对象】时」的默认目标**（老图的老规矩就是这个：打出时选中的那个目标）；
- 和【效果】左边那个「指向对象」口**不是一回事**：那个说的是「这条效果打谁」，可以不一样 ——
  「牌选中一个敌人、效果对全体造成伤害」是能同时成立的；
- **老图行为一个字不变**（没有这一块 = 按老规矩推），但图里用到「指定单体敌人」时
  配置检查会**报错**并让你补一个【指向对象】—— 不补的话那张牌根本不会让玩家选敌人，
  那一支拿到的是**空目标**（读它的状态得 0、打它等于没打）。

### 【关系符】：把「哪个状态大于多少」写成一条条件

用户要求新增的一块。原来的条件只有本体那些现成判据（手牌数 / 生命百分比…），
比不了「**某个敌人身上的某个状态** > N」这种。现在：

```
【if】的「条件」口 ← 【关系符】(大于) ← 左值：【状态】(虚弱) ← 【对象】(指定单体敌人)
                                    ← 右值：【数值】(3)
```

- 运算符：`大于 / 小于 / 等于 / 大于等于 / 小于等于 / 非 / 且 / 或`；
- 左值 / 右值两个口是**联合类型**（`NumberOrBool`）：比较类接数值（【状态】/【数值】/【能量】），
  非 / 且 / 或接条件（【条件】/【且】/【或】/另一个【关系符】）；
- 右值不接线就用模块上填的那个数字；
- **可叠加**：「虚弱 > 3」且「易伤 > 0」= 两个【关系符】各比一条，再用一个【关系符】选「且」把两个结果合起来；
- 接错类型（该接数值的地方接了条件）→ 配置检查**报错**说清该接什么，
  生成端也不会写出编译不过的代码（退成「恒不成立」并在代码里写一句说明）。

### 【if】的两个口要分清（接入 vs 条件）

【if】**左边**有两个入口：上面是「接入」、下面是「条件」，别落错：

- **「接入」**（左边上面那个）= 执行链：谁领到这一块（【触发时机】/【指向对象】/【效果】/别的分支）；
- **「条件」**（左边下面那个）= 往里插一个能出「成立 / 不成立」的模块：把【条件】/【且】/【或】/**【关系符】的「结果」**
  拖到它上面（**左 ← 右**：来源的出口 → 这个入口）。

> 这一版把「条件」从右边那个出口**挪到了左边的入口**（用户要求：「关系符的结果连不到 if 的条件」——
> 老版条件口是个**出口**，只能从 if 那一头往条件链上拖，结果存下来的线方向是反的）。
> 现在的接法和别的模块一样：**从来源的出口拖到目标的入口**。

**老存档会自动迁移**（打开就改好，不用重连）：老版里那条「从条件口往外拖」的线会被**扶正** ——
顺着它找到条件链末尾那个能出布尔的模块（例：【对象】→【状态】→**【关系符】**），把它的「结果」接进新的「条件」入口；
同时「是 / 否」两个出口的下标从 1 / 2 归到 0 / 1（**不会对调**）。迁移是幂等的，只做一次（存档里记了版本号）。

**条件口没接**（或接的不是能出布尔的模块）时，生成出来的条件会**恒为「不成立」**（永远走「否」），
所以「配置检查」直接**报错**，并把三步接线写出来：

```
①【状态】(那个状态) 的「数值」出口 → 【关系符】的「左值」
②【数值】(N) 的出口                → 【关系符】的「右值」
③【关系符】的「结果」出口          → 【if】的「条件」口（左边下面那个）
（【状态】左边的「归属」接【对象】= 那个敌人）
```

> 用户实测报过：「敌人身上明明有 4 层虚弱，打出去还是走了『我失去生命』那一支」——
> 就是这里连反了（从条件口往外拖），生成出来是 `if (false)`，一声不吭。
> 现在画布也**不让**从「条件」口往外拖（它是入口，不是出口），落错口会当场拦下来说清。

### 照着连一遍（一个完整例子）

目标：「当单体敌人虚弱层数大于 3 时，对所有敌人造成『我身上易伤层数 + 3』的伤害，否则我失去 5 点生命」。
先在「自定义参数 / 组」页加 `A` = 增益减益 → 易伤，从谁身上取 = 自己。

| 步 | 连什么 |
|---|---|
| 1 | 新建锻造卡 → 布上已有【触发时机】→【指向对象】（默认「指定单体敌人」），不用动 |
| 2 | 【指向对象】右边接【if】 |
| 3 | 【if】左边下面的「条件」口接【关系符】的「结果」，运算符选「大于」 |
| 4 | 【关系符】「左值」← 【状态】（虚弱）←「归属」← 【对象】（指定单体敌人） |
| 5 | 【关系符】「右值」← 【数值】（`3`） |
| 6 | 【if】「是」→【效果】造成伤害：「数值」←【数值】`A+3`，「指向对象」←【对象】（全部敌人） |
| 7 | 【if】「否」→【效果】失去生命：「数值」填 `5`，「指向对象」←【对象】（自己） |

生成出来就是这一句：

```csharp
Creature? __fgTarget = cardPlay.Target;                                    // 【指向对象】= 指定单体敌人 → 有值
decimal __pA  = ForgeFormulaRuntime.PowerAmount(__fgSelf, "VulnerablePower");   // A = 我身上的易伤
decimal __fg1 = ForgeFormulaRuntime.PowerAmount(__fgTarget, "WeakPower");       // 那个敌人的虚弱
decimal __fg2 = (3m);
if (((__fg1) > (__fg2)))
{
    decimal __fg3 = (__pA + (3m));
    await DamageCmd.Attack(__fg3).FromCard(this, cardPlay).TargetingAllOpponents(base.CombatState).Execute(choiceContext);
}
else
{
    foreach (Creature __fgFoe in ForgeFormulaRuntime.AllAllies(__fgPlayer))
        await CreatureCmd.Damage(choiceContext, __fgFoe, (5m), ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, this, cardPlay);
}
```

> 新建的布上【指向对象】默认是「指定单体敌人」。做**自增益牌**（打给自己）就把它改成「自己」，
> 下面那些没自己接【对象】的效果也就默认打自己了。


### 公式里的字母

- `X` = **当前所有能量**，`Y` = **当前所有额外资源量**（需求里点名的两栏，不用定义）；
- 其它字母是你在「自定义参数 / 组」页定义的参数（`M`、`N`…），**打出牌的那一刻现算**
  —— 「自己最大生命」会随血量上限变，「指定敌人的易伤层数」会随层数变。

### 执行顺序怎么走（`if` /【且】【或】/【条件】真的会分支）

从【触发时机】的出口顺着线走，**接了几个出口就按顺序走几个**：

- 【效果】= 做事，做完继续往后走；
- 【if】= 分叉：左边下面的「条件」入口接【条件】/【且】/【或】/【关系符】的「结果」，右边「是 / 否」两个出口各自成为 `if` / `else` 一条分支；
- 【且】【或】= 两个条件入口 A / B，右边成立才继续（不成立就停在这一支）；
- 【条件】= 判断成立才继续（可以当成「只在这时干活」的门）；
- 【对象】【状态】【数值】【能量】= 不是执行模块，只在被【效果】/【条件】取用时才算值。

**执行顺序是结构决定的，不是一条线** —— 【if】左边上面那个「接入」才是执行链的口，
下面那个「条件」是**收来源**的口（往里插一块能出「成立 / 不成立」的模块），两个口不抢同一个下标。

【条件】左边那个 `N` 入口可以接【数值】：接上之后 N 就是运行时算出来的（手牌数 / 生命值百分比 /
状态层数 / 额外资源量这几种会写成运行时比较）。

链接类型对不上时**当场拦住并说明原因**：条件只能接到 if / 且 / 或，数值只能接到要数值的入口。

### 【对象】的十种目标

| 选的是 | 生成成什么 |
|---|---|
| 自己 | `__fgSelf`（就是自己那个 Creature，**不会**扫到召唤物） |
| 指定队友 | 卡牌效果里的「作用对象 = 指定队友」：本体 `TargetType.AnyAlly`（**多人联机**里才会弹目标选择），选中的那个队友就在 `cardPlay.Target` 里 —— 和「指定敌人」是同一个字段 |
| 全部友方 | `ForgeFormulaRuntime.AllAllies(...)` = 自己 + 全部召唤物 |
| 指定单体敌人 | `TargetOrFirst(__fgTarget, ...)` = 打出这张牌时选中的那个目标（拿不到才退回第一个能打的敌人） |
| 全部敌人 | `ForgeFormulaRuntime.EnemyList(...)` = 全部能打的敌人 |
| 随机敌人 | `RandomEnemy(...)`（`allowDuplicates` 由那个勾选框决定） |
| 指定 / 随机 / 全部召唤物 | `ForgeFormulaRuntime.SummonList(...)` |
| 全体单位 | `ForgeFormulaRuntime.AllUnits(...)` = 自己 + 召唤物 + 敌人 |

**这几种是真不一样的**：写伤害 / 上状态 / 回血 / 加格挡时，会按上面那条表达式 `foreach` 逐个动手
（不是"打第一个"就完事）。「随机敌人」不勾「允许重复选中」时，同一个敌人不会被重复点两次。

### 现在支持到哪一步（下一阶段要补的）

**已经能真正跑起来**：触发时机 → 执行链（含 `if` / 且 / 或 / 条件 分支）→【效果】→【数值】→【对象】这一整套，
卡牌 / 药水 / 遗物都行。效果种类接了 **造成伤害 / 获得格挡 / 抽牌 / 获得能量 / 回复生命 / 失去生命 /
获得最大生命 / 获得金币 / 获得额外资源量 / 施加·扣除状态 / 翻倍状态层数 / 清除指定状态 / 清除全部减益 /
禁止获得格挡 / 击晕 / 结束回合 / 生成卡牌 / 消耗卡牌 / 丢弃卡牌 / 升级卡牌 / 变化卡牌 / 复制卡牌 /
从三摞各拿牌到手牌 / 预见 / 获得药水 / 获得卡牌奖励 / **给伙伴施加状态****。
「消耗 / 丢弃 / 升级 / 变化 / 复制」都有**自己选**（弹本体选牌界面）与**随机**两档。

> **【效果】下拉里列的是「卡牌页效果栏」的全量候选 —— 不是每一档节点图都接上了。**
> 这一版把这条差异**变成看得见的**：选了节点图没接上的种类，**配置检查会报错拦住**，
> 并在错误里列出「节点图现在支持哪些」，同时告诉你可以把它放到「卡牌 / 遗物 / 药水」页的
> 「效果」列表里（那边支持**全部**种类）。以前这一档只在生成的代码里留一行注释，
> 生成、编译、进游戏全都不报错，就是没反应（别人实测报的「给召唤物 buff 不生效」就是这个）。
>
> 节点图现在支持的就是上面那串（判断依据是 `ForgeGraphGen.SupportedEffectKinds`）。
> **【给伙伴施加状态】** 是这一版补上的：哪一只由【对象】模块决定 ——
> 指定召唤物 = 按名称找那一只；全部召唤物 = 逐只都加；没接【对象】= 第一只。

两条要留意的限制（生成时都会在代码里写一句说明）：

- **【获得卡牌奖励】** 只能挂在【触发时机】= **「战斗胜利时」** 那一支上 ——
  本体没有"战斗中直接弹奖励"的入口，挂在别的时机上不会生效（生成器会跳过并写明原因）。
  挂对了的时候，它会像本体「王国资产」那样往 `room.AddExtraReward` 里加一条奖励。
- 「指定队友」目前等同于「自己」（本体单人是自己；多人细分还没做）。

另外：**自定义状态**上那块节点图生成端**还没接**（只有卡牌 / 药水 / 遗物的布会真的生成）——
在布上画了东西的话，配置检查会给一条警告，并提示改用「触发时机」列表。

还没做的：多人（队友）细分（**本体没有可指定的队友对象**，见上表，所以这一条实际上做不了）、
以及上面那份名单之外的其它效果种类（「伙伴攻击 / 按生命值算的伙伴攻击 / 牺牲伙伴 / 召唤伙伴 /
强化指定卡牌 / 重放卡牌 / 回合结束时自动打出 / 刮削 / 毒性爆发 / 大限已至 / 填充手牌 /
给予关键词 / 牌组类效果 / 额外回合 / 抽到就减费 / 禁止回复生命 / 提高资源上限…）——
这些**卡牌页的「效果」列表里全都能用**。

### 关于「指定队友」

本体是**单人**游戏：`sts2.dll` 里没有「队友 / AI 同伴」这种可以被指定为目标的类型，
也没有多人模式的目标选择接口。所以【对象】里的「指定队友」**只能等同于自己** ——
召唤物（亡灵契约师那只、以及你自己做的那些）走「指定 / 随机 / 全部召唤物」那三档。
这一条不是"没做"，是本体没有对应的东西可指。

### 遗物的「当遗物被点击时」和「血量低于 N」

- **当遗物被点击时**：本体没给 `RelicModel` 留点击虚方法，所以生成端会给界面节点挂一个**很窄的 Harmony 补丁**
  （只认你自己生成的这只遗物，其它遗物原样放行）转发过来。
- **血量低于 N**：填 **小于 1** 的值按百分比算（`0.5` = 血量低于 50%），填 ≥ 1 就是具体点数。
- 两者都能填 **「本场战斗 / 本回合 / 本次游玩仅生效 N 次」，填 0 = 无限次**；
  用完 N 次之后遗物会像本体一样**变灰**（生成代码里把它置成 `RelicStatus.Disabled`）。
  「本回合」每回合开始清零、「本场战斗」每场战斗开始清零、「本次游玩」不清零。

## 先古之民（对话 + 替换原本的遗物）

「先古之民」页左边是本体那几位（达弗 / 妮欧 / 建筑师…，目录从解包工程读取），右边两件事：

1. **对话**：一段一段写自己角色专属的台词（补丁把我们的角色塞进 `CharacterDialogues`，
   键为 `<先古之民>.talk.<角色>.X-Y[r].ancient|char`）。
2. **替换原本的遗物**：本体每位先古之民会从他自己那张候选表（`_validRelicSets`，私有字段，外部改不了）
   里筛选 → 洗牌 → 给你 3 个选项（有时第 3 个是「尘封古书」）。
   界面上把**他原本会给的遗物全部罗列出来**（读 `Core/Models/Events/<他>.cs` 得到的候选表），
   点一个 → 「替换掉选中的这个」→ 在右边的「替换记录」里选「换成哪个遗物」（默认会挑你自己做的那个遗物）。
   生成时在他跑完 `GenerateInitialOptions()` 之后改结果：
   - 这次选项里正好有那个「原本的遗物」→ 直接换掉它；
   - **这次没抽到它（他每次是随机取 3 个）→ 什么都不做**（不会去顶掉别的选项，免得把本来该出现的遗物挤掉）；
   - 要换成的遗物已经在选项里 → 跳过（不会出现两个一样的）；
   - 选项数还是 3 个，其它逻辑一概不动；造选项调用本体自己的 protected
     `RelicOption(RelicModel, pageName, customDonePage)`（反射），所以「选中 → `RelicCmd.Obtain` → 事件结束」
     的行为与本体完全一致；整个补丁包在 try/catch 里（改选项失败就原样返回本体结果）。

> 匹配「原本的遗物」时会把**类名**和**本体的本地化表键**都归一化再比（`SneckoEye` == `SNECKO_EYE`）：
> 界面里配的是类名，而运行时 `RelicModel.Id.Entry` 是本地化表的键，两边都归一化（去掉下划线/连字符/空格、
> 忽略大小写），另外还会比一遍类型名。
> 每次替换都会往游戏日志里写一行「替换前 → 替换后（换了哪个、为什么）」，出问题看日志就知道原因。
> 界面上每条替换记录下面还有一句状态：`✅ 会替换…` / `⚠ 还没选要换成什么遗物 —— 这样生成出来不会有任何变化`，
> 配得不全时一眼就能看出来。

## 初始卡组里的打击 / 防御（Strike / Defend）

初始卡组的「打击 ×5 + 防御 ×5」不是「角色」页的勾选框，而是「卡牌」页列表里默认就有的**两张你自己的卡**，
**固定排在列表最上面**（打击在前、防御在后）：

- **英文类名固定成 `Strike` / `Defend`**（其它字段随便改）：本体的 `ModelDb` 只按**类名**
  （`StringHelper.Slugify(type.Name)`，忽略命名空间）算模型 ID，自己定义 `StrikeIronclad` 会和本体撞 ID →
  注册时抛 `DuplicateModelException`（"conflict in mod content names"），模组加载当场失败；
  而 `Strike` / `Defend` 这两个裸类名本体没有（只有 `StrikeIronclad` / `StrikeSilent`… 这些带角色后缀的），所以安全。
  校验器会在卡牌类名和本体卡重名时直接报错。
- **其余全都能改**：费用、类型、稀有度、数值、升级增量、效果、关键字、卡面立绘、名称与描述。
  出厂值照抄本体（打击 1 费 6 伤害、防御 1 费 5 格挡，升级都 +3），稀有度 `Basic`，初始 5 张，不进奖励 / 商店。
- **标了本体卡标签 `CardTag.Strike` / `CardTag.Defend`**（卡牌页「本体卡标签」那一行）——
  本体那些「升级你的初始打击 / 防御」「只对打击牌生效」的遗物、事件、卡牌都是按 `CardTag` 查牌的
  （`NeowsTalisman` 妮欧的护符、`LeafyPoultice` 叶敷剂、`LargeCapsule` 大胶囊、`GhostSeed` 幽灵种子、
  `PerfectedStrike` 完美打击…），有些还会再加「稀有度 = Basic」这一条；不标标签它们就找不到你的牌。
  防御牌还会自动生成 `GainsBlock => true`（和本体防御牌一致）。
- 生成的是 `cs/Cards/Strike.cs` / `Defend.cs` + `cards.json` 里的 `STRIKE.*` / `DEFEND.*` 条目，
  在角色卡池里（那些按标签查牌的遗物是从卡池里找牌的），但被奖励过滤排除。

## 自定义状态的触发时机：附加条件

自定义状态（能力）的每条「触发时机」在选中之后，会按需要多出一个附加条件下拉：

- **「只对哪类牌生效」**（选「打出一张牌后」时出现）：任意 / 攻击牌 / 技能牌 / 能力牌 /
  **诅咒 / 状态 / 关键词：XXX / 组：XXX**（V0.2.118 扩充；后两类是你自己配的「自定义关键词」与「组」那一组牌，
  生成时展开成显式 `ModelId` 判据，和卡牌那边的「范围界定」共用同一份选项表与算法）；
  选了「关键词组 / 组」但**那一组里一张牌都没有** → 校验器**报错拦住**（生成出来会是 `if (false)`，
  这条时机永远不会触发）；一个状态上可以放**多条**「打出一张牌后」（各带不同筛选），
  它们生成在同一个钩子方法里，回读会**按筛选切成几条**分别认回来；
- **「盯哪个状态」**（选「某个状态层数变化后」时出现）：**指定是哪一个状态的层数变化**。
  留空 = 除自己以外任意状态的层数一变就触发；选了某个状态（本体的 `StrengthPower`，或你自己造的状态类名）
  就只在它的层数变化时触发 —— 生成出来是 `if (power is StrengthPower) { …效果… }`，
  界面说明会写成「触发时机：某个状态层数变化后（盯：力量）→ …」。

> 对应本体钩子 `AfterPowerAmountChanged(choiceContext, power, amount, applier, cardSource)`，
> 生成时一律先排除自己（`if (power == this …) return;`），只看挂在自己身上的状态。

### 遗物侧的「只对哪类牌生效」（V0.2.120：和状态侧合并成同一套）

遗物页的【触发时机】选「**打出一张牌后**」时，下面同样会出现「**只对哪类牌生效**」——
候选和状态页**同一份**（任意 / 攻击 / 技能 / 能力 / **诅咒 / 状态 / 关键词：XXX / 组：XXX**）：

- 生成的是和状态侧**同一套判据**：老四档是 `if (cardPlay.Card.Type != CardType.Attack) return;`，
  诅咒 / 状态 / 关键词组 / 组走卡牌那边同一份 `ScopeCondForFilter` 谓词（那份算法只有一份，不会漂移）；
- 新档会多写一行 `// CET:CardFilter=<筛选值>` 供回读（判据是展开出来的，从代码反推不回来）；
  **老四档没有标记行**，回读靠 `cardPlay.Card.Type != CardType.Attack` 那句守卫反推（和状态侧同一个口径）；
- 遗物的**描述**里也会写出来（例：「打出一张牌后（只对攻击牌生效）：获得 3 点格挡。」），
  游戏里玩家能看出这只遗物挑牌；**选「任意」时一个字都不加**（老存档的生成结果逐字不变）；
- 「关键词组 / 组」里**一张牌都没有** → 校验器**报错拦住**（生成出来会是 `if (false)`，这条时机永远不触发）；
  时机不是「打出一张牌后」却填了筛选 → 提示「这个值不生效」。

### 「要和其他时机同时成立」（V0.2.117）：几条时机都成立才触发

同一个状态上，把**参与的那几条触发时机都勾上**「要和其他时机同时成立」，
它们就变成「**都在同一个窗口里成立过**才触发」；下面还会多一行「**重置范围**」：
**本回合**（默认，每回合重新凑）/ **本场战斗**（整场凑齐一次就够）。

- 每条时机各自照常判断自己那件事；每成立一条打个标记；
- **最后凑齐的那一条**会跑**它自己**的效果，然后把这一轮的标记清掉 —— 所以一个窗口**只触发一次**，
  「两条上都写同样的效果」也**不会**触发两次；想「凑齐后做一件事」，效果写在其中一条上最省事；
- 生成出来：`private bool __andHit0, __andHit1;`，「本回合」时按回合号自动清零
  （`__andPcs.TurnNumber != __andTurn` → 清标记），不需要额外的重置钩子；
- **只有 1 条勾了** → 校验器提示「没有别的时机和它凑，这条设置不生效」（照旧每次都触发）；
- 回读靠那一行 `// CET:RequireAll=1 CET:AndScope=<范围>`；
- 没勾这个设置的状态生成结果**一个字符都不变**。

## 「战斗胜利后」的卡牌奖励（能力牌 → 自定义状态）

「自定义状态」里有个触发时机叫**「战斗胜利后」**，「获得卡牌奖励」挂在它下面时是这样的：

- **钩子用 `AfterCombatEnd(CombatRoom room)`**，不是 `AfterCombatVictory`。
  本体的「王国资产」（`RoyaltiesPower.AfterCombatEnd` 发金币）就是这条路。
- **奖励走本体的战斗奖励**：`room.AddExtraReward(player, new CardReward(CardCreationOptions.ForRoom(player, room.RoomType), N, player))`
  —— 也就是打赢之后，**结算界面会多出一条「选一张卡」**，N 选一，选中的直接进牌组；打输了不给（`AfterCombatEnd` 只在胜利流程里派发）。
  这条路不需要 `choiceContext`，也不在结算时弹界面。
- 选项**不能带 `CardCreationFlags`**（例如 `ForNonCombatWithDefaultOdds` 会带 `NoUpgradeRoll`）：
  战斗结束时会存档，`CardReward.ToSerializable()` 遇到带 flag 的选项会抛 `NotImplementedException`。
  `ForRoom` 不带 flag，是安全的；事件房（`RoomType.Event`）本体 `ForRoom` 会直接抛异常，生成时会退回一份同样不带 flag 的选项。
- 想让「打了多少层就给几条奖励」就勾「生效次数 = 本状态的层数」，状态有几层就多几条奖励（每条都是 N 选一）。
- 挂在**其它触发时机**（战斗中）的「获得卡牌奖励」是当场弹选牌界面，选中即进牌组 —— 两种写法都在。

## 死亡描述文本 / 阵亡后台词

两个字段都会真的出现在游戏里（生成 `cs/DeathFlavor.cs`，两句都留空则不生成）：

- **死亡描述文本** → **死亡结算画面**上角色立绘下面那句带引号的话。本体那句取自 `run_history` 表的通用文案
  （`NRunHistory.GetDeathQuote`：被谁杀死 / 放弃 / 假胜利…再套上 `game_over_screen.ENCOUNTER_QUOTE_LEFT/RIGHT` 引号），
  表里**没有**「每个角色一句」这种键，所以这里给 `GetDeathQuote` 挂 postfix：`characterId` 是我们的角色时换成配置的文本。
- **阵亡后台词** → 自己阵亡的那一刻冒一个 3 秒的对话气泡（`NSpeechBubbleVfx.Create`，颜色用角色的
  `SpeechBubbleColor`），挂在 `CreatureCmd.Kill(IReadOnlyCollection<Creature>, bool)` 上（本体所有死亡都经过它）。
  本体里对应的键是 `characters`:<角色>.banter.dead.endTurnPing，它只在**联机**「回合结束 ping」时才会显示
  （`FlavorSynchronizer`），单人游戏看不到；所以这个键照旧写，另外再加这个阵亡气泡。

> 初始遗物只有一个来源：**「遗物」页里勾了「起始遗物」的遗物**（`StartWithBurningBlood` 只是老存档字段，生成时不读取）。

## 触发时机：「受到伤害时（每一下）」vs「受到攻击后（连击只算一次）」

遗物页和自定义状态页的触发时机里都有这两条，差别很重要：

| 触发时机 | 本体钩子 | 触发次数 | 什么时候用 |
|---|---|---|---|
| 受到伤害时（每一下） | `AfterDamageReceived` | **每一下伤害都触发一次** | 原版的「受到伤害后」语义（本体原体黏土 `SelfFormingClay`、残存之心 `BeatingRemnant` 都是它） |
| 受到攻击后（连击只算一次） | `AfterAttack` | **一次攻击的所有命中结束后只触发一次** | 想「一次攻击只算一次」时用它 |

- 本体的敌人攻击很多是**多次连击**（`AttackCommand.WithHitCount` / `MultiAttackIntent`，
  意图上写着「1 点 × 5 下」这种），所以一次「造成 5 点伤害」的攻击会让「每一下」那条触发 5 次 ——
  这是本体语义，不是生成器重复执行（本体用这个钩子的遗物也是这个行为）。
  用户实测报过「敌人造成 5 点伤害，这个触发时机会生效五次」，就是这种情况。
- 生成代码里「每一下」那条会判 **`if (result.UnblockedDamage <= 0) return;`**（全被格挡 / 0 伤害的那一下不算「受到伤害」），
  和本体原体黏土的判法一致。
- **「失去生命」不算「受到伤害」**（V0.2.123 起）：生成代码里还有一句
  `if (props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered)) return;`，
  排掉的就是本体 `DamageProps.cardHpLoss`（`Unblockable | Unpowered`）那种掉血 ——
  **血祭 / 献祭代价 / 放血 / 中毒**这类「穿盾而且没有来源加成」的掉血都走它，
  请用「**自己失去生命时**」那条时机去接（那条才是为它准备的）。
  为什么以前会串：本体 `CreatureCmd.Damage` 里 `Hook.AfterDamageReceived` 是**无条件**广播的（不看 props），
  所以「失去生命」以前也会触发「受到伤害后」。
  ⚠ **「对自己造成伤害」照旧算「受到伤害」**：它生成的是 `Unpowered | Move`（**格挡能挡**），
  不含 `Unblockable`，不会被这一句挡掉。想「无视格挡的自伤代价」请用效果种类「失去生命」。
  敌人那两条（敌人受到伤害后 / 敌人造成伤害后）**故意不加**这一句：敌人侧没有「敌人失去生命」
  这条时机可以替代，排掉就等于功能丢了。
- 「受到攻击后（连击只算一次）」生成的是
  `public override async Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command)`，
  判据是**这一次攻击真的打到了我身上**：
  `command.Results.SelectMany(hits => hits).Where(r => r.Receiver == 我的生物).Sum(r => r.UnblockedDamage)` > 0。
  本体 `AbstractModel.AfterAttack` 的注释写明了：multi-attack 时它在**所有命中结束之后只跑一次**
  （而 `AfterDamageGiven` / `AfterDamageReceived` 是每一下都跑）。它只认「攻击」——
  中毒、事件掉血这类不是攻击的伤害不会触发它（那种用「每一下」那条）。
- 自定义状态里「每一下」× 「生效次数 = 本状态的层数」会**相乘**（敌人的一次 5 连击 × 5 层 = 一次攻击执行 25 次），
  校验器会把算式写出来提醒。

### 触发时机：遗物和状态现在是**同一套**

遗物页和自定义状态页的触发时机**合并成了一套**（同一个 id、同一个显示名）：
状态那边有的，遗物这边都有；遗物早就有的「获得金币时 / 抽牌堆打乱洗牌时」也补到了状态那边。

遗物侧是**按遗物的归属**写守卫的（遗物的 `base.Owner` 是 `Player`，状态的是 `Creature`）：

| 触发时机 | 本体钩子 | 遗物侧守卫 |
|---|---|---|
| 打出一张牌时（这张牌还没生效） | `BeforeCardPlayed`（**V0.2.157 改的**，见下） | `cardPlay.Player != base.Owner` → 返回 |
| 一张牌结算完毕时 | `AfterCardPlayedLate`（旧版的「打出一张牌后」） | 同上 |
| 消耗 / 抽到 / 弃掉一张牌后 | `AfterCardExhausted` / `AfterCardDrawn` / `AfterCardDiscarded` | `card?.Owner?.Creature != base.Owner.Creature` → 返回 |
| 自己失去生命时（每一下） | `AfterCurrentHpChanged` | 只认自己 + `delta < 0`（回血不算）；钩子没给 context → 自己造 |
| 自己获得格挡后 / 敌人获得格挡后 | `AfterBlockGained` | 只认自己 / 只认敌人；自己造 context |
| 一个敌人死亡后 | `AfterDeath` | `!creature.IsEnemy` → 返回 |
| 敌人回合开始时 / 结束时 | `AfterSideTurnStart` / `BeforeSideTurnEnd` | `side != CombatSide.Enemy` → 返回 |
| 敌人受到伤害后 / 造成伤害后 | `AfterDamageReceived` / `AfterDamageGiven` | 受伤的是敌人 / 打人的是敌人（**故意不排「失去生命」**：敌人侧没有「敌人失去生命」这条时机替代） |
| 某个状态层数变化后 | `AfterPowerAmountChanged` | 只看自己身上的状态（可选「盯哪个状态」） |

> ⚠ 遗物侧 / 状态侧的「**自己受到伤害后**」和「**自己造成伤害后**」都带一句
> `if (props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered)) return;` ——
> **「失去生命」不再算「受到伤害」**（V0.2.123 起，「戴罪者的镣铐 / 血祭 / 献祭代价 / 中毒」那种
> 穿盾且无来源加成的掉血用「**自己失去生命时**」那条时机接）。
> 「对自己造成伤害」是 `Unpowered | Move`（可格挡、不含 `Unblockable`）→ **照旧算「受到伤害」**，没被误伤。
>
> ⚠ **致死的那一下不再触发**（V0.2.139 起）：这三条时机（受到伤害后 / 失去生命时 / 受到攻击后）的守卫里
> 都加了「自己已经死了就直接返回」——
> 状态链是 `if (base.Owner.IsDead) return;`（`base.Owner` 本身就是 `Creature`），
> 遗物链 / 锻造链是 `if (base.Owner.Creature.IsDead) return;`（那两条链的 `base.Owner` 是 `Player`）。
>
> 为什么必须加：`AfterDamageReceived` / `AfterCurrentHpChanged` 这两个钩子本体是**无条件广播**的，
> 「被敌人打死的那一下」照样会进来；`AfterAttack` 则是在**一次攻击的所有命中结束之后**才跑一次 ——
> 致死的那一击也是在这时点触发。不挡的话，效果里只要有「回复生命」，就会把自己从 0 血拉回正数：
> 多人模式下队友还活着，看起来就是「被打死了还能回血复活」（用户报的 bug）。单人模式同样生效
> （致死那一下不再触发这只遗物 / 这个状态）—— 这是预期语义。

**同一个时机在两边的叫法完全一样**（以前一边叫「受到伤害时（每一下）」、另一边叫「自己受到伤害后（每一下）」，
现在统一成后者）；老工程里写的老名字也认得回来（`// 触发时机：<老显示名>` 兜底），回读不会丢触发时机。

仍然只在遗物侧的三条（都是有原因的）：**获得时**（遗物才有「拿到的那一刻」）、
**战斗结束时（房间结算之前）**（状态那边「战斗胜利后」生成的就是同一个 `AfterCombatEnd` 钩子）、
**某个状态层数到达 N 层时**（状态那边用「层数变化后」+ 条件「刚好达到 N 层」实现，能力等价）。

> 卡牌**没有**跟着改：卡牌不是挂在模型上的钩子，它的时机是「打出的那一刻」+「卡牌时机」条件
> （被消耗 / 被生成 / 被保留 / 被丢弃 / 被变化 / 被给关键词）。要让卡牌也能选「战斗开始时」这类时机是另一件事。

## 触发时机：「造成伤害时（每一下）」「每消耗一个能量 / 一个额外资源量」「生成卡牌时」

这一版给遗物页补了四条触发时机，自定义状态页补了一条（都对着本体 `AbstractModel` 的钩子签名核过）：

| 触发时机 | 本体钩子 | 谁有 | 触发次数 |
|---|---|---|---|
| 造成伤害时（每一下） | `AfterDamageGiven(choiceContext, dealer, result, props, target, cardSource)` | 遗物 | **每一下命中都触发**（连击 = 多次） |
| 每消耗一个能量 | `AfterEnergySpent(CardModel card, int amount)` | 遗物 | 按**点数**各触发一次 |
| 每消耗一个额外资源量 | `AfterStarsSpent(int amount, Player spender)` | 遗物 | 按**点数**各触发一次 |
| 生成卡牌时 | `AfterCardGeneratedForCombat(CardModel card, Player? creator)` | 遗物 + 自定义状态 | 每生成一张触发一次 |
| 某个状态层数到达 N 层时 | `AfterPowerAmountChanged(choiceContext, power, amount, applier, cardSource)` | 遗物 | 层数**跨过 N** 那一下（见下） |

### ⚠ V0.2.157：「打出一张牌时」必须是**结算前**（`BeforeCardPlayed`）

用户报：「状态监听『打出一张牌时』会监听**施加这个状态的牌** —— 那一档实际是『打出一张牌后』，
和『一张牌结算完毕时』几乎一样」。

本体打一张牌的顺序（`CardModel`）：

```
Hook.BeforeCardPlayed(cardPlay)   ← 打出的那一刻，这张牌还没结算
   ↓ OnPlay(...)                  ← 卡自己的效果（「施加状态」就在这一步）
   ↓ Hook.AfterCardPlayed(...)    ← 结算完才回调：内部先 AfterCardPlayed 再 AfterCardPlayedLate
```

工具那一档原来生成的是 `AfterCardPlayed` ⇒ 同一张牌施加的状态在它自己身上立刻触发了一次。
**改法**：`CardPlayed` → **`BeforeCardPlayed`**（结算前 ⇒ 它自己施加的状态还不存在 ⇒ 不会自己触发自己），
老语义留成新时机 **`CardResolved`** → `AfterCardPlayedLate`（旧工程回读也落到这一档，语义不变）。

- ⚠ `BeforeCardPlayed(CardPlay)` 本体**不给** `choiceContext` → 生成端按本体做法自己造
  `HookPlayerChoiceContext`（复用 `BootstrapInCombat` / `TriggerHasChoiceContext`，和「花掉能量 /
  自己失去生命」同一套）；只有该时机的效果**真的需要** context 时才造。
- 状态侧 / 遗物侧 / 锻造页遗物档**三处菜单、签名、归属守卫**同步；回读：
  `BeforeCardPlayed → CardPlayed`、`AfterCardPlayed`/`AfterCardPlayedLate → CardResolved`
  （并加进 `RelicHookIsTrigger`、纳入 CardFilter 与分段回读）。
- 卡牌链两条时机共用抽出来的 `EmitCardPlayedItems`（免得两处漂移）。
- 自检 5 条钉住（生成的是 `BeforeCardPlayed` 且没有 `AfterCardPlayed` / 需要 context 时造了 bootstrap /
  `CardResolved` 生成 `AfterCardPlayedLate` / 三处菜单显示名一致 / 遗物侧签名 + `HasContext` +
  `BootstrapInCombat` 口径）。

### 「某个状态层数到达 N 层时」

选了这条时机，遗物详情里会多出两栏：「**盯哪个状态**」和「**到达 N 层**」。

- **判据**（生成出来的守卫）：
  ```csharp
  if (power.Owner is null || power.Owner != base.Owner.Creature) return;   // 只看自己身上的状态
  if (power is not WeakPower) return;                                      // 只盯「虚弱」
  if (!(power.Amount >= 5m && power.Amount - amount < 5m)) return;         // 只有「层数跨过 5」那一下才触发
  ```
  也就是「**之前不到 N、变化之后到了 N 或更多**」那一下 —— 已经是 5 层再加 1 层**不会**重复触发；
  一次上 3 层（3→6）也只算跨过一次。
- **「盯哪个状态」留空** = 自己身上**任何**状态的层数跨过 N 层都触发（可以再用效果自己的条件去筛）。
- 本体这条钩子**任何单位**的状态层数一变都会调（敌人的、队友的），所以生成端一定先判「是不是自己身上的」。
- 参数存在 `RelicSpec.TriggerPowerId` / `TriggerAmount`，回读靠类里那行
  `// CET:PowerWatch=<状态类名> CET:PowerReach=<N>m`（守卫语句以后改写法也不会丢配置）。
- 校验器：N ≤ 0 报错；没选状态给提示（说清「任何状态都触发」）；**没选这个时机却填了这两个值**也会提示
  「不生效」（不静默）。
- ⚠ **V0.2.152 修的两个坑**（用户实测报的，两件事同一个病根）：
  1. `CSharpCodeGen.TriggerSupportsAmount` 漏了 `PowerReached` → **界面上根本没有 N 输入框**
     （`ShowTriggerAmountField` / relic 的 `ShowAmountField` 全 false）→ N 永远停在默认值 **1**，
     生成出来是「跨过 **1** 层那一下触发」：用户按 1→2→3 涨上去只在第一层触发过一次，**到 N 层时不再触发**
     （「我状态层数达到了却没生成卡牌」）；状态描述里印的是下拉菜单那个**字母 N**
     （「在游戏中怎么也是 N，而不是填的数」）。现在这一档补进名单（**不算**计数族 ——
     `TriggerAmountNeedsScope` 用的是 `CountExpr`，所以不会误显示「本回合 / 本场战斗」），
     两侧标题都写「到达 N 层（层数跨过这一层那一下触发）」。
     ⚠ 这一改会让遗物那条路为 `PowerReached` 多写一行 `// CET:TriggerAmount=` →
     **多时机（全部满足 / 任一满足）的段落切分会多切出一条时机**（自检实测 2 条变 3 条），
     所以两处标记条件都加了 `&& id != "PowerReached"`（它的 N 由 `CET:PowerReach=` 记着，不会丢）。
  2. 生成守卫以前**无条件**写 `power != this`（排除这只状态自己）→ 自定义状态「盯**自己**这只状态」
     永远不成立。现在**显式盯到自己**时去掉它（`CustomPowerGen.SelfClassName` 按生成期的 `_powerOwner`
     反查类名）；留空 = 任意状态 / 盯别人时照旧排除自己。自己触发自己不会死循环：
     「跨过 N 那一下」这个判据本身就挡住（4 层时 `4-1=3` 不 `< 3`）。
- `PowerTriggers.TriggerLine` 现在把 N 直接写进文字（「某个状态层数到达 3 层时（盯：自己的××状态）」），
  界面那一行与游戏里的状态描述共用它；开发探针 `forge --powerprobe D:\out.cs` 可以直接看生成源码 + 自动描述。

### 「某个状态被移除时」（遗物 + 状态两边）—— 这条要自己打补丁

选了它，下面同样有「**盯哪个状态**」：留空 = 自己身上任意状态被移除都触发，填了就只认那一个。
「层数掉到 0 自然消失」和「被『清除状态』直接清掉」**都算**。

**为什么必须自己打补丁**：本体**没有**任何「状态被移除」的全局钩子 ——

- `PowerModel.AfterRemoved(Creature oldOwner)` 是**那个状态自己**的实例钩子（遗物 / 别的状态都收不到）；
- `AfterPowerAmountChanged` 只覆盖「层数被扣到 0」那一半；被 `PowerCmd.Remove` **直接清除**的根本不走那条路。

所以这份存档只要用到这条时机，工具就多写一个 `cs/PowerRemoval.cs`：后缀补丁打在
**同步**的 `PowerModel.RemoveInternal()` 上（它是本体**所有**移除的唯一出口：`PowerCmd.Remove` /
「清除全部减益」/ 战斗结束清状态全都走它），把「哪个状态从谁身上被移除了」转发给：

1. 该生物所属**玩家身上的遗物**里配了这条时机的那些；
2. 该生物身上**还留着**的自定义状态里配了这条时机的那些；
3. **被移除的那个状态自己**（它已经从 `Powers` 里摘掉了，所以单独判一次 —— 「我这个状态被移除时」也能用）。

> ⚠ **通知范围 = 这一场战斗里的所有生物**（V0.2.164 修，用户实测报的「状态里监听某状态被移除时无法触发」）：
> 老写法只遍历 `power.Owner` **那一只**身上的状态 / 它所属玩家的遗物 ⇒
> 「挂在你自己身上、盯**敌人**身上的状态被移除」的配置**永远收不到通知** ——
> 而 UI 允许「盯：任意敌人」，生成出来的守卫里也真写着
> `power.Owner != base.Owner && …HittableEnemies.Contains(power.Owner)`，那一支就是**死代码**。
> 现在遍历 `CombatState.Creatures`（拿不到战斗状态就退回它自己），每条时机**自己的守卫**照旧筛
> （每只生物只发一次，不会重复触发；「被移除的那个就是自己」那一段留在循环**外面**）。
> 另外：当时还用真引擎 dll 实测过 Harmony **能**打上这个补丁（`[HarmonyPatch(typeof(PowerModel), "RemoveInternal")]`
> 打完后 `GetPatchInfo` 非空）—— 所以问题不在补丁本身，而在通知范围。
> 排查用：打开「生成调试日志」后，补丁会往游戏日志写一行
> `[模组调试] 状态被移除：XxxPower（通知了 N 个盯这条时机的遗物 / 状态）` ——
> `0` = 一个都没通知到（存档里没配 / 补丁文件没生成），`≥1` = 通知到了、问题在那条时机自己的守卫里。

转发的方法是个普通方法（不是 `override`）：

```csharp
// 遗物侧（base.Owner 是 Player）
public async Task OnForgePowerRemovedAsync(PowerModel power) {
    if (power.Owner is null || power.Owner != base.Owner.Creature) return;   // 只看自己身上的状态
    if (power is not VulnerablePower) return;                                 // 只盯「易伤」
    ...
}
```

⚠ 补丁是**同步**的、方法是 async 的，所以转发用本体自带的 `TaskHelper.RunSafely(...)`
（fire-and-forget，本体自己也是这么处理的）。**没用到这条时机的存档不会生成这个文件**（输出逐字不变）。

#### ⚠ 状态页「盯某个状态」的守卫：「自己那份」还是「任意敌人」（V0.2.168 修，用户实测）

用户报的「某个状态被移除时，好像无法正常生效」（**状态页**、盯**敌人身上的状态**）真因就在这条守卫里：
「盯谁 = 任意敌人」那一支以前写着 `power.Owner != base.Owner`，等于要求「发生事的那份状态在**别的**生物身上」，
而「盯哪个状态」填的正是**这只状态自己**（比如贴在敌人身上的「击晕值」）时，用户要的是
「**这份状态挂在谁身上，它自己那份出事就在谁那里触发**」—— 于是这份自己那份（`power.Owner == base.Owner`）
被这条挡死：生成 / 编译 / 进游戏全不报错，就是什么都不做。

现在这三条「盯某个状态」的时机（**层数变化后 / 层数到达 N 层时 / 被移除时**）共用一份守卫：

| 你配的 | 生成出来的守卫 |
|---|---|
| 盯的就是**它自己这个状态** | `power.Owner == base.Owner`（**不管「盯谁」选自己还是任意敌人**）—— 挂谁身上就盯谁，包括贴在敌人身上的那份自己 |
| 盯**别的**状态 + 「盯自己」 | `power != this && power.Owner == base.Owner`（不变） |
| 盯**别的**状态 + 「任意敌人」 | 要求那份状态挂在别的生物身上（不变，这才是「盯敌人身上的易伤」的意思） |

> ⚠ 修的是**生成端**：老工程要重新点一次「生成」才生效。校验器里原来那条
> 「自己涨到 N 那一下不会触发」的警告也随之改成提示（说清按「这份状态自己」生成）。

- **「造成伤害时（每一下）」**：判据是 `dealer == 自己`（和自定义状态那条「自己造成伤害后」口径一致）。
  想「一次攻击只算一次」用「受到攻击后（连击只算一次）」那种思路 —— 本体 `AfterAttack` 是所有命中结束后跑一次。
- **「每消耗一个能量 / 一个额外资源量」**：本体那两个钩子的 `amount` 是**这一张牌一共花的点数**
  （X 费牌 = 当前全部能量），所以生成的效果按点数各跑一次（本体 `OrbitPower` 也是按点数累加）。
  守卫两条：`amount <= 0` 直接返回（本体这条钩子**0 费也会调**）、以及「是我方角色花的」
  （`card.Owner.Creature` / `spender`，不判的话联机时别人打牌也会触发你的遗物）。
  掷骰留在循环**外面**：一次触发只掷一次。
- **「生成卡牌时」**：本体注释写明「只算**玩家自己生成**的牌」（敌人塞给你的状态牌不算），
  判据 `creator != null && creator == 自己`（遗物）/ `creator.Creature == base.Owner`（状态）。
- **上下文**：这三个钩子本体都**没给 `choiceContext`**。需要它的效果（造成伤害 / 抽牌 / 施加状态…）
  生成时会按本体 `Hook.cs` 的做法**自己造一个**：

  ```csharp
  ulong __netId = MegaCrit.Sts2.Core.Context.LocalContext.NetId ?? 0UL;
  var choiceContext = new HookPlayerChoiceContext(this, base.Owner, __netId, GameActionType.Combat);
  ```

  遗物用「(source, owner, netId, type)」那个重载（遗物的 `Owner` 就是 `Player`）；
  **战斗里不能用 `BlockingPlayerChoiceContext`** —— 那是局外的阻塞式上下文（「获得时」用的才是它），
  在战斗里用会卡住。
- **回读**：这四个时机都按钩子签名认（`AfterDamageGiven` / `AfterEnergySpent` / `AfterStarsSpent` /
  `AfterCardGeneratedForCombat`），不需要额外标记。

## 触发时机上的数值（每 N 次 / 每 N 点 / 数值 ≥ N）**填在哪**

> 📍 **位置**：选了触发时机之后，**「触发时机」那个下拉的正下面**就是它（遗物页在「遗物属性」里，
> 状态页在「触发时机」那一块里、每条时机的下方）。这栏**只在有数值可填的时机上出现** ——
> 选了战斗开始/结束、回合开始/结束、获得遗物时这种没有数值的时机，它整栏都不显示
> （底下那句灰色说明会写「这条触发时机暂时没有数值可填」）。

标题会跟着触发时机换说法：

| 触发时机 | 那一栏的标题 | 还带什么 |
|---|---|---|
| 打出一张牌后 / 消耗一张牌后 / 丢弃一张牌后 / 抽到一张牌后 / 生成卡牌时 | **每累计几次触发** | 再加一行「**统计范围**」：本回合 / 本场战斗 |
| 一个敌人死亡后 | **每累计几次触发** | 本场战斗内计数（工具自己数的，见更新说明） |
| 花掉能量后 / 花掉额外资源量后 | **花掉多少点触发** | 再加一行「统计范围」 |
| 受到伤害后 / 造成伤害后 | **伤害至少多少才触发** | —（⚠ 这里的「伤害」**不含**「失去生命」那种穿盾掉血，见「受到伤害时（每一下）」那一节） |
| 自己失去生命时 | **失去生命至少多少才触发** | — |
| 获得格挡后 | **获得格挡至少多少才触发** | — |
| 某个状态层数到达 N 层时 | （不在这栏）**到达 N 层**，在它自己的那一行 | 配合「盯哪个状态」 |

- **留空 / 填 0 = 老行为**（每次都触发）；填 N 就是上面表里的意思。输入框下面那行灰色小字随时告诉你
  现在是什么状态（例：`现在每累计 3 次触发一次，范围 = 本回合。`）。
- 这栏（**触发时机上的数值**）只能填**数字** —— 它和效果里的「数值公式」是两回事；
  遗物和状态两边是**同一套**（V0.2.164 起效果里的「数值公式」三条链都能写，见「数值公式」那一节）。
- **状态页**可以给同一个状态配好几条时机，**每条各有自己的一份数值**。
- 界面上的坑（V0.2.107 修）：以前在编辑器里把触发时机换掉之后，这一栏**不会当场出现**，
  得重新在左边列表里点一次遗物 / 状态才看得到 —— 现在换完时机它立刻出现（自检里有真的去动那个下拉的用例守着）。

## 条件「某状态层数刚好达到 N 层」

自定义状态的触发时机「**某个状态层数变化后**」上多了一条条件：判断「这次变化**跨过**了 N 层」。

- 判据用的是那条钩子给的两个参数：`power.Amount`（变化**之后**的层数）和 `amount`（这一次的增量）→
  `power is XPower && power.Amount >= N && power.Amount - amount < N`；
- 例：盯「虚弱」+ 条件「刚好达到 5 层」→ 只有从 4 层涨到 5 层（或更多）那一次才触发，
  已经是 5 层再加也不会重复触发；
- 它**只能**挂在「某个状态层数变化后」上 —— 别的触发时机没有这两个参数，
  生成出来永远不成立，所以校验器会**报错拦住**并告诉你改用「拥有某状态至少 N 层」；
- 「只要现在有 N 层就生效」是另一条条件（**拥有某状态至少 N 层**，随时可查，卡牌 / 遗物 / 状态都能用）。
- 界面上状态触发器的条件候选是**单独一份**（比遗物那份多出这一条），所以不会出现「遗物上选得到、选完才被拦」。

## 遗物触发时机「获得时」

遗物页多了一个触发时机「**获得时**」：拿到这只遗物的那一刻触发一次，
对应本体钩子 `AfterObtained()`（本体的「好吃饼干 YummyCookie / 磨刀石 Whetstone / 爪子 Claws」
这些「拾取时生效」的遗物都是它），生成时同时声明 `public override bool HasUponPickupEffect => true;`。

- 这个钩子本体**没给 `choiceContext`**，但本体允许在获得遗物时做需要它的操作
  （本体「爪子 Claws」就在获得时让玩家选牌变化）。所以需要上下文的那些效果
  （生成卡牌 / 抽牌 / 卡牌奖励 / 施加状态…）生成时**自己造一个**
  `new BlockingPlayerChoiceContext()` —— 本体 `BlockingPlayerChoiceContext` 的注释里点名的
  第一个场景就是 "Relic AfterObtained callbacks"；不需要的效果不会多造那个变量。
- 「获得卡牌奖励」放在这里（或其它非「战斗胜利后」的时机）是**当场弹 N 选一**、选中即进牌组；
  挂在「战斗胜利后」才是「结算界面多一条奖励」。

## 遗物触发时机「战斗结束时（房间结算之前）」

遗物页多了一个触发时机「**战斗结束时（房间结算之前）**」，对应本体钩子
`AfterCombatEnd(CombatRoom room)`。它比「战斗胜利时」（`AfterCombatVictory`）**更早**：

```
CombatManager.EndCombatInternal 的顺序：
  Hook.AfterCombatEnd        ← 这一条
  room.OnCombatEnded()
  Hook.AfterCombatVictory    ← 「战斗胜利时」
  发奖励 / 写存档 / 结算
```

所以「必须在房间结算之前做完」的事（清掉本场留下的临时状态、结算前补一笔金币…）挂这一条；
「要跟结算／奖励一起做」的事挂「战斗胜利时」（卡牌奖励那条路径就在那里）。

> ⚠ **和「战斗胜利时」一样，它只在打赢那条路上触发**：打输走的是本体的
> `ProcessPendingLoss`（那里只发一个 `CombatEnded` 事件，**不调任何模型钩子**），
> 所以「无论输赢都触发」在遗物上做不到（要那样得再加一条补丁）。
> 这个时机也没有 `choiceContext`，「召唤伙伴」这类需要上下文的效果会被配置检查拦住并说明。

回读（`--recover`）说明：这个时机和「每场战斗只触发一次」的清理钩子用的是**同一个**
`AfterCombatEnd`，所以生成时会写一句 `// 触发时机：战斗结束时（房间结算之前）`，
回读先按钩子签名认、认不出来时按这句注释兜底（不会误认成「战斗胜利时」）。

### 作用对象：指定队友 / 全部队友（多人联机）

卡牌效果的「作用对象」多了两档（用户要求），用的是**本体现成的目标类型**：

| 作用对象 | 卡牌 `TargetType` | 生成的效果 | 本体现在谁在用 |
|---|---|---|---|
| 指定队友 | `AnyAlly`（任意队友、**不含自己**） | `cardPlay.Target`（和「指定敌人」同一个字段，只是那张牌的 TargetType 不同） | 本体卡 `BelieveInYou`（0 费技能） |
| 全部队友 | `AllAllies`（不弹选择） | `foreach (Creature ally in base.Owner.Creature.CombatState.PlayerCreatures.Where(c => c.IsAlive && c != base.Owner.Creature))` | 本体卡 `BladeSymphony`（2 费技能） |

支持的**效果种类**（其余种类会**被配置检查拦住**，绝不静默成「对自己」）：
造成伤害 / 施加增益减益 / 临时增益 / 获得格挡 / 回复生命 / 失去生命 / 最大生命。

- **「指定队友」只能做在卡牌上**：遗物 / 药水没有「玩家选中的目标」（本体只有卡牌会在打出时弹目标选择）。
  「全部队友」在遗物 / 药水上也能用（它不挑目标，直接遍历）。
- 一张牌**只有一个** `TargetType`：同一张牌里既有「指定队友」又有打敌人的效果 → 配置检查**报错**并让你拆成两张牌。
- ⚠ **单人游戏里 `AnyAlly` 的牌是打不出去的**（本体规则：场上活着的玩家单位 ≤ 1 → `CardModel.cs:1742` 直接判为不可打出）。
  配置检查会给一条提示；单人调试时把那一档临时改成「自己」。
- 回读：这两档生成出来的语句和「指定敌人」**一模一样**（都是 `cardPlay.Target`），
  所以生成时会多写一行 `// CET:TargetSide=Ally|AllAllies`，`--recover` 靠它还原。

> 注：锻造页节点图【对象】模块里的「指定队友」目前仍然等同「自己」（那一套走的是 `ForgeFormulaRuntime`，
> 还没有队友档）——卡牌效果这一侧的「作用对象」是真的队友目标。

## 遗物稀有度「先古」

遗物页的「稀有度」多了「**先古**」（本体 `RelicRarity.Ancient`）——本体的先古遗物
（古老牙齿 `ArchaicTooth` / 星盘 `Astrolabe` / 黑星 `BlackStar`…）都是这一档。

- 这一档**不会从普通奖励 / 商店里抽出来**，是**先古之民给的**。所以还勾着「加入遗物池」时，
  配置检查会给一条提示：想让它在奖励池里出现就把稀有度改成 Common / Uncommon / Rare / Shop；
  真要做先古遗物，就取消那个勾，用「先古之民」页安排它（或用自己的效果发放）。
- 生成的就是 `public override RelicRarity Rarity => RelicRarity.Ancient;`，其余（触发时机 / 效果 / 图标）照旧。

### 「下回合生效」支持哪些效果

勾上「下回合生效」之后，这条效果不会立刻发生，而是**下回合开始时**才发生。判据只有一个：
`EffectCatalog.HonorsNextTurn(kind)`（界面显隐和配置检查都读它）——**只有真正实现了延迟分支的才给勾**，
不会再出现「勾了没反应」。

| 支持 | 挂的东西 |
|---|---|
| 获得格挡 | 本体 `BlockNextTurnPower` |
| 抽牌 | 本体 `DrawCardsNextTurnPower` |
| 获得能量（可负数） | 本体 `EnergyNextTurnPower`；负数是生成的「下回合少 N 点」欠费状态 |
| 获得额外资源量 | 本体 `StarNextTurnPower` |
| 施加 / 扣除状态 | 生成的 `<角色>ForgeDelayed<状态>`（回合开始施加层数再自毁） |
| 生成卡牌 / 复制卡牌 / 获得·变化·删除·升级牌组里的牌（全局） | 生成的 `<角色>ForgeDelayed<Gen|Copy|AddGlobal|TransGlobal|RemoveGlobal|UpGlobal><卡类名>`：**层数 = 张数**，回合开始时调用和「立刻执行」**同一套发射器** |

**还没做**：变化卡牌（战斗内）（它的 `SpawnPoolExpr` / `EmitTransformBody` 里 `base.Owner` 写死是 Player，要先参数化）；
生成类的那几种目前只做在**卡牌**上，遗物 / 药水 / 自定义状态勾了会被配置检查拦住（不静默改成「立刻生成」）。

### 生成卡牌：从范围里自己挑一张

「生成卡牌 + 按范围随机」下面多了一个勾选框「**从范围里自己挑一张（候选 = 这一组里的全部卡，不再随机抽）**」：

- 勾上 = 选牌界面直接列出这一组范围里的**所有卡**（最多 60 张），玩家想拿哪张拿哪张；
  候选 ≤ 3 张走本体的 `FromChooseACardScreen`、> 3 张走 `FromSimpleGrid`（和「多选1」同一条切换规则）；
- 不勾 = 老样子（随机出 N 张；「多选1」填了就是先随机抽 M 张候选再挑 1 张）；
- 选完照样吃「生成出来的卡怎么处理」（升级 / 免费 / 仅本回合 / 加关键词 / 放入哪一摞）；
- 范围里一张都没有 → 整条安全跳过（不弹空界面、不卡死）；回读按 `CET:Chosen=1` 认回来。

## 牌堆：四种都能选（含消耗牌堆）

「从哪里选牌」有**四摞**：手牌 / 抽牌堆 / 弃牌堆 / **消耗牌堆**。
四摞都是本体的**战斗牌堆**（`PileType.IsCombatPile()` 认 Hand / Draw / Discard / Exhaust / Play），
所以走同一套 API：手牌 = `CardSelectCmd.FromHand`，其余三摞 = `CardSelectCmd.FromCombatPile(context, PileType.X.GetPile(player), …)`。

| 效果 | 从消耗牌堆选会怎样 |
|---|---|
| 消耗卡牌 | 没有变化（那摞里的牌本来就已经消耗了，校验器会给一句提示） |
| 变化卡牌 / 升级卡牌 | 把消耗掉的牌变成别的牌 / 升级（升级只影响本场战斗的这一份） |
| 丢弃卡牌 | 把消耗掉的牌挪回**弃牌堆**（洗牌后会回到抽牌堆） |
| 复制卡牌 | 克隆一张已经消耗的牌（本体 `CreateClone` 只要求原牌在战斗牌堆里，消耗堆算） |
| 重放卡牌 | 让消耗掉的牌获得「重放 N 次」（它下次被打出时才体现） |
| 给予卡牌关键词 | 给消耗掉的牌加关键词 |
| **从消耗牌堆拿牌到手牌** | 专门的一条效果（和「从抽牌堆 / 弃牌堆拿牌到手牌」并列）：把消耗掉的牌**拿回来** |

## 数值填 0 = 这条效果「不存在」

所有效果种类的数值都允许填 **0**，填 0 就是**这条效果不生效**：卡面描述里不写这一行、
生成的代码里也整条不执行（不会被力量 / 敏捷 / 状态加成）。以前有些种类（生成卡牌 / 消耗卡牌 /
施加状态…）的下限是 1，现在全部放宽到 0。

三个例外 —— 它们的 0 有别的含义，照样生效：
- **召唤伙伴**：0 = 用「召唤物」页里配置的血量；
- **给予卡牌关键词**：0 = 这张牌自己；
- **回合结束时自动打出**：0 = 这张牌自己。

## 「数值公式」：数值按打出的那一刻算

用户要求（V0.2.112）：「效果那边只要保留公式就行，去掉数值框，反正数值的功能公式都有并且更多」——
所以在**卡牌**上，那 5 种支持公式的效果（伤害 / 格挡 / 失去生命 / 回复生命 / 施加状态）
**只剩「数值（公式）」这一栏**，原来那个固定数值框收起来了：

- **留空 = 0**：这条效果**不显示、也不执行**（写公式才会有数值）；
- **打开老存档时**，这些效果原来的固定数值会**自动转成公式**（例：伤害 7 → 公式 `"7"`），
  数值一个都不变，也不会出现「看不见却还在生效」的数字；
- **不支持公式的种类照旧有「数值」框**（抽几张 / 生成几张 / 金币 / 能量 / 消耗…这些的数值
  必须在生成时就定下来，没有公式可用）；
- **遗物 / 药水 / 自定义状态上的效果也照旧有「数值」框** —— 公式目前只做在卡牌上
  （那几条链没有「打出这张牌」那一刻的上下文，填了会被校验器拦住），收起来就没法填了；
- 写法：数字、参数名（`M` / `N`…，到「自定义参数 / 组」页定义它「从哪来」）、
  `X` = 打出这张牌那一刻的当前能量、`Y` = 当前额外资源量、**内置实时只读量**（见下表）、
  `+ - * / %` 与括号、`min / max / floor / ceil / round / abs`；最前面可以写一个 `=`。
- 例：`M*3`（自己最大生命 ×3）、`max(1, N/2)`（N = 指定敌人身上某个状态的层数）、`X`（当前能量）、`Turn*2`（这一场第几回合 ×2）。
- **升级增量照旧生效**（V0.2.121 修）：两者的配合是
  「公式算出的值」**再加上**「升级增量」—— 生成出来是本体习惯的写法
  `(base.IsUpgraded ? 升级后 : 升级前)`（例：伤害 14、升级 +6 → `DamageCmd.Attack((base.IsUpgraded ? 20m : 14m))`），
  现算变量也一起带上增量，所以**卡面数字和实际打出来的数一致**。
  （V0.2.112 ~ V0.2.120 期间这两者不搭界：打出时用的是公式字面量、升级增量改的是动态变量，
  于是在那几版里「打了公式的卡」升级增量是白填的 —— 现已修好。）

### 内置只读量（不用定义，直接写）

> 📍 **在界面哪里找**：「卡牌」页 → 选中一张牌 → 右边效果栏选中一条效果 →
> 效果编辑器**最下面那块蓝色小框「可用量」**里就是全部内置名字（带中文说明），一直显示、不用先填错。
> 三个公式框都能用它：**数值公式**、**次数公式**、**条件选项 → 数值 N / M 的公式**。
> （只做在卡牌上 —— 遗物 / 药水 / 诅咒 / 自定义状态的编辑器里不显示这块清单，填了也会被校验器拦住。）

这些是**运行时的实时读数**（每次求值都不一样），所以不像参数那样要先在「自定义参数 / 组」页定义 ——
直接把它们当变量写进公式就行（大小写不敏感）。数据来源**几乎全**是本体现成的 API，**只有「获得的能量」那两条需要工具自己打补丁**。
完整清单（每条带中文说明）以界面上那个「可用量」小框为准（= `FormulaBuiltins.All`，V0.2.162 起共 **80 条**），
**逐条出处 / 表达式 / 口径**列在 `docs/VANILLA_STATE_PARAMS.md`（那次「挖掘本体所有参数」的结果表）。

| 名字 | 是什么 | 底层 |
|---|---|---|
| `Turn` | 自己这一侧的第几回合（额外回合 +1；「第一回合」只能判它） | `PlayerCombatState.TurnNumber` |
| `Round` | 当前战斗轮数（所有单位各走一次算一轮） | `ICombatState.RoundNumber` |
| `HandCount` / `DrawCount` / `DiscardCount` / `ExhaustCount` | 手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆的张数（**战斗外读成 0**） | `PlayerCombatState.Hand/DrawPile/DiscardPile/ExhaustPile` |
| `DeckCount` / `DeckUpgradableCount` | **牌组**张数 / 牌组里还能升级的张数（跨战斗，不是战斗里的牌堆） | `Player.Deck` |
| `Hand*Count` / `Deck*Count` | 手牌 / 牌组里**某一类**牌的张数（`Attack` / `Skill` / `Power` / **`Status`** / `Curse` 五种写法，例：`DeckStatusCount` = 拥有的状态牌数） | `…Cards.Where(c => c.Type == CardType.X).Count()` |
| `MyHp` / `MyMaxHp` / `MyHpMissing` / `MyBlock` | 自己当前生命 / 最大生命 / 已损失的生命 / 当前格挡（`My` 前缀是为了不和参数页那几个**可指定目标**的来源重名） | `Creature.CurrentHp/MaxHp/Block` |
| `MyEnergy` / `MyEnergyMax` / `MyStars` / `MyGold` | 当前能量 / 能量上限（含加成） / 额外资源量 / 金币 | `PlayerCombatState.Energy/MaxEnergy/Stars`、`Player.Gold` |
| `RelicCount` / `PotionCount` / `PetCount` / `OrbCount` | 遗物 / 药水 / 伙伴 / 充能球的数量 | `Player.Relics`、`Player.Potions`、`PlayerCombatState.Pets`、`OrbQueue.Orbs` |
| `PowersOnMe` / `BuffsOnMe` / `DebuffsOnMe` | 自己身上状态 / 增益 / 减益的数量（只数会对玩家显示的；增益减益按**当前层数**判定） | `Creature.Powers` + `IsVisible` / `TypeForCurrentAmount` |
| `EnemyCount` / `HittableEnemyCount` / `EnemyTotalHp` | 活着的敌人数 / 可被攻击的敌人数 / 敌人总生命 | `CombatState.Enemies/HittableEnemies` |
| `Floor` / `ActFloor` / `ActNumber` / `Ascension` | 总层数 / 本幕层数 / 第几幕（从 1 数） / 升华等级 | `IRunState.TotalFloor/ActFloor/CurrentActIndex/AscensionLevel` |
| `DamageDealtTotal` / `DebuffsAppliedTotal` | **本局累计**造成的伤害 / 施加的减益次数（跨战斗） | `Player.ExtraFields` |
| `PlayedThisTurn` / `PlayedThisCombat` | 本回合 / 本场战斗**打出的牌数**（`CardPlaysStarted` 口径：**含正在打出的这一张**；重放各算一次） | `History.CardPlaysStarted` |
| `Attacks/Skills/PowersPlayedThisTurn`·`ThisCombat` | 打出的攻击 / 技能 / 能力牌数 | 同上 + `CardType` 过滤 |
| `DrawnThisTurn` / `DrawnThisCombat` | 抽到的牌数 | `CardDrawnEntry` |
| `DiscardedThisTurn` / `DiscardedThisCombat` | **丢弃**的牌数 | `CardDiscardedEntry` |
| `ExhaustedThisTurn` / `ExhaustedThisCombat` | **消耗**的牌数（= 你说的「本回合消耗的手牌」） | `CardExhaustedEntry` |
| `GeneratedThisTurn` / `GeneratedThisCombat` | **生成**的牌数（**复制出来的也算**） | `CardGeneratedEntry` |
| `CopiedThisTurn` / `CopiedThisCombat` | 复制的牌数（本体把复制记成「生成」，所以和上面同值） | 同上 |
| `DamageDealtThisTurn` / `DamageDealtThisCombat` | 造成的伤害（**只算没被格挡的**，排除自伤） | `DamageReceivedEntry.Result.UnblockedDamage` |
| `DamageTakenThisTurn` / `DamageTakenThisCombat` | 受到的伤害（只算没被格挡的） | 同上（按 `Receiver` 数） |
| `BlockGainedThisTurn` / `BlockGainedThisCombat` | 获得的格挡 | `BlockGainedEntry` |
| `EnemiesKilledThisTurn` / `EnemiesKilledThisCombat` | 被自己打死的敌人数（按**致死的那一下**记） | `DamageReceivedEntry.Result.WasTargetKilled` |
| `EnergySpentThisTurn` / `EnergySpentThisCombat` | 花掉的能量 | `EnergySpentEntry` |
| `StarsGainedThisTurn` / `StarsGainedThisCombat` | 获得的额外资源量 | `StarsModifiedEntry`（只数正数） |
| `StarsSpentThisTurn` / `StarsSpentThisCombat` | 花掉的额外资源量 | 同上（只数负数） |
| `SummonedThisTurn` / `SummonedThisCombat` | 召唤量 | `SummonedEntry` |
| `EnergyGainedThisTurn` / `EnergyGainedThisCombat` | 获得的能量（回合开始把能量补满的那部分**不算**） | 工具自己打补丁记（见下） |
| `XSpent` / `YSpent` | 这张牌**花掉的**能量 / 额外资源量（V0.2.158；**不是那种费用 = 0**，V0.2.159 加的先判再调）—— 和 `X` / `Y`（当前值）不是一回事，见下 | `CardModel.ResolveEnergyXValue` / `ResolveStarXValue`（⚠ 本体这两个方法**不是 X 费就抛异常**，所以生成时必须先判 `EnergyCost.CostsX` / `HasStarCostX`） |

> ⚠ `X`（当前能量）/ `Y`（当前额外资源量）读的是**打出这张牌那一刻剩下的** —— 本体**先扣费再跑效果**
> （`PlayCardAction`：`await SpendResources()` → `OnPlay`），所以「X 费 / Y 费」的牌在效果里读 `X` / `Y`
> 会读到 **0**（次数被 `Math.Clamp(…, 1, 20)` 夹成 1）。要「这张牌花掉的那个数」必须用 `XSpent` / `YSpent`；
> 写错时校验器会给警告（详细来龙去脉见「次数类数值也能写公式（卡牌上）」那一节）。

- **「获得的能量」为什么要自己记**：本体只在 `PlayerCombatState.Energy` 上放一个当前值（外加一个
  `EnergyChanged` 事件），战斗历史里也只有「花掉能量」（`EnergySpentEntry`），**没有「获得了多少」**。
  所以这份存档只要用到这两个读数，工具就会多写一个 `cs/EnergyGainTracker.cs`：静态记录器 +
  打在本体**同步**方法 `PlayerCombatState.GainEnergy(decimal)` 上的后缀补丁
  （所有「获得能量」最后都走它，而回合开始的 `ResetEnergy()` / `AddMaxEnergyToCurrent()` 不走它 ——
  正好符合「补满不算获得」的语义）；按 `PlayerCombatState` 实例记（本体每场战斗都会 `new` 一个 → 换战斗自动从头算），
  「本回合」按 `TurnNumber` 认（换回合自动清零），不需要额外的重置钩子。
  **没用到的存档不会生成这个文件**（输出和以前一模一样）。

- 「本回合」用的是本体那套判据（`HappenedThisTurn`：同一轮 + 同一侧 + 每个玩家的回合数都没变），
  所以敌人的回合里读到的还是你**上一个**回合的数据；
- **没有单独的「复制张数」**：本体把复制出来的牌也记成「生成」（复制走的是同一个
  `CardPileCmd.AddGeneratedCardToCombat` → `History.CardGenerated`），所以 `Generated*` 里包含复制；
- 名字写错了会**报错拦住**并把完整名单列出来（不会当成 0 静默失效）。

### 次数类数值也能写公式（卡牌上）

「数值」之外，**四个次数类字段**也能写公式（在「卡牌」页的效果编辑器里，是一块「次数公式」）：

| 字段 | 用在哪 | 生成出来是什么 |
|---|---|---|
| 生效次数 | 整条效果重复几遍 | `for (int i = 0; i < (int)Math.Clamp(<公式>, 1m, 20m); i++)` |
| 命中次数 / 对群数 | 多段攻击的段数 / 随机挑几个敌人 | `.WithHitCount((int)Math.Clamp(<公式>, 1m, 20m))` |
| 复制的份数 | 「复制卡牌」每张复制几份 | `for (int __copyIdx = 0; __copyIdx < (int)Math.Clamp(<公式>, 1m, 20m); __copyIdx++)` |
| 重放次数 | 「重放卡牌」每张额外打出几次 | `__replayCard.BaseReplayCount += (int)Math.Clamp(<公式>, 1m, 20m);` |
| 概率（%） | 「概率生效」那条效果生效的百分比 | `__chanceRoll < (double)(<公式>)` |

**概率那一格和前四个不太一样**：本体原本是「同一张牌里勾了概率的效果**共用一次掷骰**、各占一段区间」
（区间是生成时按固定百分比算好的）。写了公式的那条**退出这个区间划分、改成自己独立判一次**
（`__chanceRoll < 现算的百分比`）—— 所以「一条写公式 + 一条固定值」时两条不再互斥，
校验器会给一句提示说清这一点。

- 写法、参数、内置只读量和「数值公式」**完全一样**（`M`/`N`、`X`/`Y`、`Turn`/`HandCount`/`DiscardedThisTurn`…）；
- ⚠ **「这张牌花掉的」要用 `XSpent` / `YSpent`，不能用 `X` / `Y`**（V0.2.158）：本体打一张牌是
  **先扣费、再跑效果**（`PlayCardAction`：`await _card.SpendResources()` → `OnPlayWrapper`），
  所以效果跑的那一刻 `X` / `Y` 读到的是**扣完剩下的** —— 「Y 费」的牌读 `Y` = 0，
  `Math.Clamp(…, 1, 20)` 把它夹成 **1**（用户实测原话：「在次数填 Y（额外资源量）时，只会触发一次」）。
  想用「这张牌**花掉**的」就填 `XSpent`（= 本体 `ResolveEnergyXValue()`）/ `YSpent`（= `ResolveStarXValue()`），
  例：Y 费牌「生效次数 = `YSpent`」= 花掉几点额外资源量就重复几遍。
  ⚠ 生成的是**先判再调**的三元（`base.HasStarCostX ? … : 0m` / `base.EnergyCost.CostsX ? … : 0m`）：
  本体这两个方法**不是 X 费就抛异常**，不判就会在战斗中崩（V0.2.159 修）；不是那种费用的牌上读出来是 0。
  校验器对「X 费 / Y 费 + 公式里真的用到 `X` / `Y`」的牌给**警告**（判据按标识符边界，
  `YSpent` 里的 `Y` 不算 —— 用现成的标识符表会误判，踩过）；对「用了 `XSpent` / `YSpent` 但牌不是那种费用」
  也给警告（读数恒为 0）；
- 算出来的次数一律**夹到 1 ~ 20**（和固定值那条同样的上限，免得公式算出 999 把一局卡死）；
- 卡面印的是**公式本身**（「重复 Turn 次」「对随机敌人 2 + HandCount 次」）；
- **命中次数公式在三种作用对象上都会生效**（V0.2.110 修的坑）：
  - 指定敌人 → `.WithHitCount(现算)`（打同一个目标 N 下，本体「旋风斩 / 天际钻头」那种）；
  - 全体敌人 → `.TargetingAllOpponents(...).WithHitCount(现算)`（**每个**敌人各挨 N 下）；
  - 随机敌人 → 逐个挑目标，挑 N 次。

  以前「指定敌人 / 全体敌人」那两条分支只认**固定**次数 > 1（写公式时固定值通常还是 1）→
  整条 `WithHitCount` 都不生成，**只打一下**，可卡面却印着「2+A 次」（两处对不上）。
  现在只要写了公式就一定写 `WithHitCount`；固定 1 次且没写公式时照旧一个字符都不多。

> **V0.2.131**：原来那三个「= X」勾选框（数值 / 生效次数 / 命中次数·对群数）从界面上撤掉了
> （用户要求《卡牌页优化.md》右侧③）。X 这个能力**没丢**，改在这几个公式框里写：
> 「数值（公式）」写 `X`、「次数公式 · 生效次数」写 `X`、「次数公式 · 命中次数 / 对群数」写 `X`。
> 老存档里勾过那三个开关（`AmountIsX` / `TimesIsX` / `RepeatIsX`）的字段一个都没删，照旧读得回来、照旧生成。
> 对不是那 5 种公式效果的种类（抽牌 / 生成 / 消耗…），「数值（公式）」栏**只认两种写法**：
> ① 一个**纯数字 / 能折成常数的式子**（例：`1`、`2+3`、`=1`）—— 它就是左边的固定数值，
> 工具在**打开存档 / 校验 / 生成之前自动折好**（V0.2.146 起；以前这种写法被直接报错拦住，
> 用户实测报过「数值公式就不能填纯数值吗」）；
> ② 光秃秃一个 `X`（= 原来的「数值 = X」，生成出来的代码与老勾选框**逐字相同**）。
> 写别的式子（`M*3` / `HandCount`…）「配置检查」会报错拦住。
- 和「= X / = 层数」那几个开关**不能同时用**（都是「改用算出来的值」），校验器会报错；
- **只做在卡牌上**：遗物 / 药水 / 诅咒 / 自定义状态的触发时机里，代码链上没有那两个「自己 / 玩家」的局部变量，
  所以填了会**报错拦住**（和「数值公式」同一条规矩）；
- **不能写公式的地方**（必须在生成时就定下来，本体构造函数要的是编译期常量）：卡牌**费用**、
  升级增量、页面常量（初始血量 / 初始金币 / 额外资源量的初始值与上限…）。
- 回读：生成时写一行 `// CET:CountFormulas=Times=Turn|Repeat=2 + HandCount`，
  `--recover` 按它把公式原样认回来。

### 条件里的数值也能写公式（卡牌上）

「条件选项 → 数值 N（/ 数值 M）」旁边多了一个公式框（**只有卡牌**的效果编辑器里有）：

- 留空 = 用左边那个固定数字；写了 = **打出这张牌时现算**，写法与「数值公式」完全一样
  （`M`/`N`、`X`/`Y`、内置只读量 `Turn`/`HandCount`/`DiscardedThisTurn`/`PowersOnMe`…）；
- 例：「手牌数不少于 N 张」写 `Turn` = 第几回合就要求几张；「生命值在 N%~M% 之间」下限写 `25`、上限写 `HandCount * 10`；
- 生成出来的是**内联表达式**（`IsPlayable` / 描金边那两个表达式体属性里没法先声明局部变量）：

  ```csharp
  // 手牌数不少于 Turn 张
  CardPile.GetCards(base.Owner, PileType.Hand).Count() >= (int)System.Math.Max(0m, (base.Owner?.PlayerCombatState?.TurnNumber ?? 0))
  ```

- ⚠ **条件里的公式**写了之后「填负数 = 反向 / 区间外」不生效：条件公式算出来的正负生成时不知道，
  这一版一律按「正面」判断（校验器会给一句提示）。要反向请把负号写进公式本身（例 `0 - Turn`），
  或者清空公式用固定负值；（**效果**那 5 种支持公式的数值不受这一条限制：公式是纯数字时会按常数判正负，
  见「『获得类』数值填负数 = 反向效果」那一节。）
- 公式里引用了「指定敌人」来源的参数时，这条条件**不写进「打不出去」/ 描金边**，只包住那条效果
  （那两处还没有 `cardPlay`）—— 和「指定敌人」那类条件的处理一致；
- 回读：生成时写一行 `// CET:CondFull=Kind=…|Amount=…|AmountFormula=…`（整条条件都带上，
  因为守卫那一行已经变成算完的表达式、反推不回来），`--recover` 按它重建。
- **只做在卡牌上**：遗物 / 药水 / 诅咒 / 自定义状态上填了会被**报错拦住**。

**只有这 5 种效果能写**：造成伤害 / 获得格挡 / 失去生命 / 回复生命 / 施加增益减益 ——它们的数字是**打出时才读**的。其它种类（抽几张 / 生成几张 / 消耗几张）的数字必须在**生成时**定下来
（要提前分配卡面变量、算好选牌上限），所以那些种类上这一栏根本不出现；老配置里若留着值，
生成前的检查会报错说清原因，**不会静默忽略**。

这一版公式**只做在卡牌上**：遗物 / 药水 / 自定义状态的触发时机、诅咒牌上填了会报错拦住 ——
那几条链没有「打出这张牌」这一刻的上下文（拿不到「打出的那张牌 / 玩家选中的目标」）。

几个要知道的行为：

- 卡面上显示的是**算出来的数字**（V0.2.111 起）：生成端给公式的 5 种效果各声明了一个「现算预览值」的
  动态变量（`cs/FormulaVars.cs`），卡面的 `{Damage:diff()}` 这类占位符会填上按当前属性算出来的值，
  力量 / 敏捷 / 易伤那些加成也照旧叠上去。
  （以前是直接把「M*3」这种公式文本印在卡面上 —— 玩家看不懂，用户报过。）
- **升级增量对公式那条不生效**（旧文档的说法，指的是「真·现算公式」`M*2` 那种：公式取代了数值，
  升级增量只改动态变量、不改公式算出来的值，校验器会给一句提示）；
  **纯常数公式**是例外（V0.2.141 起界面上会把它说清）：生成端对「常数公式 + 升级增量」写的是
  `(base.IsUpgraded ? 常数+增量 : 常数)`，升级**确实**会改打出时的数值 —— 效果列表摘要里会写出
  「（升级后 N）」。⚠ `常数 + 增量 == 0`（升级增量把数值抵平了，例：公式 `-2` + 升级增量 `2`）
  会让**升级过的那张牌打出去没有任何效果**，校验器会给一条**警告**并把「怎么改」写清楚；
- 公式算出来的**正负要到打出时才知道**，所以生成端一律按「正面效果」写
  （伤害 / 格挡 / 回复 / 施加状态都不会反向）。要「扣层数」那种语义请用「施加增益 / 减益」填负的固定数值；
- 效果列表里那条会显示成「数值公式 M*3」，一眼能看出它不是固定数字；
- **引用了「指定敌人」参数的公式必须写在单体牌上**。这张牌如果是「对所有敌人 / 对自己」那种，
  打出时**不会选中某个敌人**，`cardPlay.Target` 是 `null`，那个参数就**永远是 0** ——
  用户实测踩过：A = 敌人的虚弱、伤害写 `A+3`、作用对象选「全体敌人」，生成出来是
  `PowerAmount(cardPlay?.Target, "WeakPower")`，实际伤害恒为 3（公式等于白写）。
  校验器现在把这一条**报错拦住**，并给出两种改法（把作用对象改成「指定敌人」，
  或把这个参数的「从谁身上取」改成「全部敌人 / 随机敌人」）。
  另外公式是**整张牌只算一次**的，所以「每个敌人各自的状态层数」做不到 —— 自动的话所有敌人同一个数字；
- 「从工程恢复存档」会把公式、以及公式用到的自定义参数一起认回来（参数靠生成代码里
  `decimal __pM = …` 那一行还原）—— 只有参数的「备注」那一栏恢复不了，因为它不参与生成。

代码里长这样（生成出来的，不用自己写）：

```csharp
// ---- 「数值公式」用到的值：打出这张牌时先算一次（和锻造页【数值】模块同一套语法）----
Creature __fgSelf = base.Owner.Creature;
decimal __pM = __fgSelf?.MaxHp ?? 0;   // M= 自己的最大生命
// CET:AmountFormula=M*3
await DamageCmd.Attack((__pM * (3m))).FromCard(this, cardPlay).Targeting(cardPlay.Target)…
```

## 公式里的 `X` / `Y` 读的是「剩下的」，要「花掉的」用 `XSpent` / `YSpent`（V0.2.158）

用户实测报的：「**在次数填 Y（额外资源量）时，只会触发一次**」。

- 本体打一张牌的顺序（`PlayCardAction`）是 **`await _card.SpendResources()` → `OnPlayWrapper(...)`** ——
  也就是**先扣费、再跑效果**。公式是在效果里现算的，所以那一刻：
  - `Y`（当前额外资源量）= **扣完剩下的**，「Y 费」的牌就是 **0**；
  - `X`（当前能量）= 同理（「X 费」的牌是 0）。
- 次数公式算出来 0 之后会被生成端的 `Math.Clamp(__Y, 1m, 20m)` **夹到 1** ⇒ 表现就是「只触发一次」。
  **不是生成端算错**，是本体时序 —— 所以「让 `Y` 自己变成花掉的那个数」做不到。
- **修法**：新增两个内置量（「可用量」蓝框里能看到，三个公式框都能用）：

  | 写法 | 意思 | 生成出来是 |
  |---|---|---|
  | `XSpent` | 这张牌**花掉的能量** | `(base.EnergyCost.CostsX ? (decimal)base.ResolveEnergyXValue() : 0m)` |
  | `YSpent` | 这张牌**花掉的额外资源量** | `(base.HasStarCostX ? (decimal)base.ResolveStarXValue() : 0m)` |

  这两个读的是本体**自己缓存的**「这张牌花掉了多少」（扣费时记下的），扣费之后再读也不会变 0。
  例：Y 费牌「生效次数（公式）」填 `YSpent` ⇒ 花掉几点额外资源量就重复几遍。
  ⚠ **必须先判再调**（表达式里那个三元）：本体这两个方法**不是 X 费就直接抛异常**
  （`CardModel` 第一句就是 `if (!EnergyCost.CostsX) throw new InvalidOperationException(...)` /
  `if (!HasStarCostX) throw ...`）—— 不判的话，普通牌上写一次 `YSpent` 就会在战斗中抛
  `InvalidOperationException`（V0.2.159 修的；同一个坑 V0.2.131 在「费用填 X」那条路上踩过一次）。
  所以在**不是那种费用**的牌上，它们读出来是 **0**（不是崩），校验器也会给一句警告说清「这条公式等于没写」。
- **写错不再静默**：卡牌是「X 费 / Y 费」（费用框里写了 `X`）**且**效果公式里真的用了 `X` / `Y` →
  校验器给**警告**，写清「本体先扣费再跑效果 ⇒ 那一刻读到 0（次数被夹到 1）」并指向 `XSpent` / `YSpent`；
  反过来，用了 `XSpent` / `YSpent` 但这张牌**不是** X 费 / Y 费 → 也给警告（读数恒为 0）。
  判据用**标识符边界**（`YSpent` 里的 `Y` 不算用到 `Y`）：
  一开始直接用现成的标识符表，结果 `YSpent` 也被当成「用到 Y」，自己写的正例仍然报警告（踩过）。

## 遗物 / 药水 / 自定义状态上也能写「数值公式」（V0.2.164）

用户问的「状态和遗物不能使用公式吗」——以前不能（校验器直接拦住），现在能了。

**为什么以前不能**：公式求值要「自己 / 玩家 / 目标」。卡牌链的做法是「打出这张牌之前先算一批局部变量」
（`__pM` / `__X` / `__Y` / `__fgPlayer` / `__fgSelf`，见 `ForgeGraphGen.EmitCardFormulaPrologue`），
而遗物 / 药水 / 自定义状态的执行点（触发器钩子 / `OnUse`）**没有**这套前置。
但**锻造节点图**（遗物 / 药水那一档）一直能用公式 —— 它走的是**全内联求值**
（`ForgeFormula.ConditionResolver`：参数就地展开成 `base.Owner…`，不需要任何局部变量）。

于是这一版把三条链接上同一个内联求值（`ForgeFormula.InlineResolver` + `CSharpCodeGen.InlineAmountFormula`）：

| 链 | 「玩家」 | 「自己」 | 「指定敌人」类参数取谁 |
|---|---|---|---|
| 遗物 / 药水（`base.Owner` 是 Player） | `base.Owner` | `base.Owner.Creature` | **可打的第一个敌人** |
| 自定义状态（`base.Owner` 是 Creature） | `base.Owner.Player` | `base.Owner` | **可打的第一个敌人** |
| 卡牌（不变） | `__fgPlayer` | `__fgSelf` | 玩家选中的那个目标 |

- 支持的种类还是那 5 种（造成伤害 / 获得格挡 / 失去生命 / 回复生命 / 施加增益减益）；
  其余种类在这三条链上照旧**报错拦住**（数值必须在生成时就定下来）。
- **显示**：遗物 / 药水的描述是 `{Heal:diff()}`，读的是动态变量的 `PreviewValue`，而它们**没有**
  卡牌那种「显示前回调」⇒ 生成一个 `__RefreshFormulaPreview()` 挂在 `ExtraHoverTips` 上
  （本体取悬停说明时一定会读它）现算一遍，悬停看到的就是真实数字；自定义状态的说明是**生成期写死的纯文本**，
  现算不了 ⇒ 那里**直接印公式本身**（例：「玩家回合开始时：失去 `M/10` 点生命。」）。
- **回读**：三条链都写一行 `// CET:AmountFormula=<公式>`，`--recover` 原样认回来。
- **「可用量」小框**：这三条链的效果编辑器里也摆出来了（判据同一个 `ShowAmountFormula`），
  说明文字按链分口径（`X` / `Y` = **当前**能量 / 额外资源量；「指定敌人」= 可打的第一个敌人）。
- 坑（真编译抓出来的两个）：① 属性**不能**直接写方法体（`P { 语句; }` 不是合法 C#，报 CS8124），
  必须写成 `get` 访问器；② 这两条链也要用到 `cs/FormulaVars.cs`（那 5 种的现算变量类）与
  `cs/ForgeFormulaRuntime.cs`（`Div` / `PowerAmount` / `FirstEnemy` 这些辅助）——
  漏了就是 CS0246（`UsesFormulaVars` 已扩到遗物 / 药水，另加 `ForgeGraphGen.AnyNonCardFormula`）。

## 回合结束时自动打出

效果种类里的「**回合结束时自动打出**」：

- **数值 0 = 这张牌自己**：打出它之后挂一个内置状态，本回合结束时**再自动打出它一次**；
- **数值 ≥ 1**：打出它时先让你（或随机）从「从哪里选牌」那一摞里挑好 N 张，
  本回合结束时把这 N 张逐张自动打出去（**不弹选牌界面** —— 选择发生在打出这张牌的时候）；
- 实现：生成 `<角色>ForgeTurnEndPlayPower`（不可见、每次挂一个实例、记住那几张牌），
  它在 `BeforeSideTurnEnd` 里逐张 `CardCmd.AutoPlay(choiceContext, card, null)` 然后自毁。
  挑这个钩子是因为本体的回合结束流程是
  `Hook.BeforeSideTurnEnd`（CombatManager.cs:1559）→ `DoTurnEnd`（:1570，处理手牌里的
  `OnTurnEndInHand` 卡 + 弃手牌）—— 跑在前面，自动打出的牌会正常离开手牌，不会和弃牌流程抢同一张牌。
- 只能用在卡牌 / 遗物 / 药水上；数值 0（这张牌自己）只有**卡牌**能用（遗物 / 药水没有「自己这张牌」）。

## 重放卡牌

效果种类里的「**重放卡牌**」：让选中的牌获得「重放 N 次」——本体机制是 `CardModel.BaseReplayCount`
（打出时 `GeneratePlayCount = GetEnchantedReplayCount() + 1`，所以**重放 1 次 = 打出去时连着打 2 次**）。
本体「转化 Transfigure / 隐藏宝石 HiddenGem / 剑圣 SwordSagePower / 士兵炖菜 SoldiersStew」
做的都是同一句 `card.BaseReplayCount += N`（HiddenGem 还会 `CardCmd.Preview` 一下）。

- 数值 = 从牌堆里选几张（四摞都能选），效果底下的「**重放次数**」= 每一张额外打出几次；
- 只能给**别的**牌加 —— 自己这张牌的重放次数在本体里是 `OnPlay` **之前**就算好的
  （`GeneratePlayCount` 在 `OnPlayWrapper` 里、进 `OnPlay` 循环之前调用），改它没有作用（校验器会提示）。

## 条件选项「斩杀」（本体 Fatal）

本体那几张「[gold]斩杀[/gold]时，…」的牌（「进食 FEED / 贪婪之手 HAND_OF_GREED / 狩猎 THE_HUNT」）
用的判据是这样的：

```csharp
bool shouldTriggerFatal = cardPlay.Target.Powers.All(p => p.ShouldOwnerDeathTriggerFatal());   // 打之前算
AttackCommand attackCommand = await DamageCmd.Attack(...).FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(choiceContext);
if (shouldTriggerFatal && attackCommand.Results.SelectMany(r => r).Any(r => r.WasTargetKilled)) { … }
```

条件里选了「**斩杀（这一次攻击杀死了目标）**」就生成这套：
打伤害那一条会把 `AttackCommand` 记进 `__lastAttack`（并先算好 `__fatalOk`），
条件那一条判 `__lastAttack.Results…Any(r => r.WasTargetKilled)`。

- 只能用在**卡牌**上，而且要写在「造成伤害」那一条效果**后面**（它判的是上一条攻击的结果；校验器会拦住）；
- 卡面描述照本体写成「[gold]斩杀[/gold]时，…」，并自动挂上本体的「斩杀」悬停说明（`StaticHoverTip.Fatal`）；
- 小怪 / 会复活的目标打不死（本体 `MinionPower` / `ReattachPower` 让 `ShouldOwnerDeathTriggerFatal()` 返回 false），
  这一点也照着本体判了。

## 英文类名 / 关键词键：每次构建自动「固定 + 修引用」

留空的英文类名（卡牌 / 遗物 / 药水 / 召唤物 / 自定义状态）和留空的自定义关键词「英文标识」
过去是**按位置自动编号**的（`<角色>Card9`、`<角色>Pet2`、`KEYWORD_3`…）。
也就是说：在中间**插入 / 删除 / 拖动**一条，后面那些自动编号的模型就**集体改名** ——
而效果里存的是**类名 / 键的字符串**（目标卡 `SpawnCardId`、召唤物 `PetSummon`、自定义状态 `PowerId`、
卡牌上勾的关键词、`Keyword:XXX` 范围限定、给予关键词），于是引用要么直接失效
（校验器报「找不到 / 引用了不存在的自定义关键词」，构建失败），要么**静默指到别的模型上**。
用户实测就是「每次构建都得手工去重排英文类名，不然老是构建失败」。

现在每次构建（`ModGenerator.Generate` 开头、**校验之前**）都会自动做两件事：

1. **修引用**：先读上一次构建留下的「类名表」
   `<输出目录>/<ModId>.forge_classmap.json`（写在工程目录的**上一级**，不会被导出进 pck），
   里面有「类名 → 当时的显示名」。失效或错位的引用按**名字**认回来：
   - 卡牌 / 召唤物 / 自定义状态：`SpawnCardId` / `PetSummon` / `PowerId`；
   - 自定义关键词：卡牌上勾的 `KeywordIds`、`GivenKeyword`、`SpawnFilter = "Keyword:XXX"`；
   - 认不回来时还有两条兜底：`<角色>Pet2` / `KEYWORD_3` 这种**自动编号按序号**认
     （第 2 只召唤物 / 第 3 个关键词），以及「只剩一只召唤物就是它」（老存档的行为）。
   每修一处都会在日志里写一行「引用的 ×× 已自动改成 ○○」，**不会静默改错**。
2. **固定类名**：所有留空的类名 / 关键词键一次性写实并存进存档（界面上「① 生成工程」/
   「② 一键构建 + 安装」之后都会静默保存一次；自检模式不写存档）——
   以后再加 / 删 / 拖动别的条目，已经固定的那些名字都不会再变。

> 想手工看一眼「上次构建时这些名字都是谁」，直接打开那个 `.forge_classmap.json` 即可。

### ⚠ V0.2.153：改号和引用必须**在同一拍**里搬（用户报「点构建后目标卡里的东西会变」）

自动类名是**按列表行号**算的（`<角色>Card3` = 普通卡列表第 3 张；诅咒 / 先古各自一套前缀各自数），
而效果里那些「指着某张卡」的地方存的都是**这个字符串**（`SpawnCardId` 目标卡、
`AncientRows.AncientCards` 先古卡清单、`Art.CardPortraits` 上传的卡面、锻造图节点的 `SpawnCardId`）。

于是：**先选目标卡 → 之后增删 / 拖动过卡牌 → 行号变、自动名集体改号 → 引用没搬 → 那个字符串
落到了「同名的另一张卡」身上**。构建时才 `Freeze`（把留空类名按当前行号写实）已经太晚：
引用里存的旧名字早就不指向原来那张卡了 —— 用户看到的就是「目标卡里的东西变了」。

三处接上（都用生成端同一套 `NameStabilizer.RenumberCardNames` → `RepairCardClassReferences`）：

1. **界面每次重算候选表**（`RefreshCardChoices` → `SyncCardClassReferences`：增删 / 拖动 / 复制 /
   撤回 / 改名之后都会走）先搬一次，所以界面上**不用等到构建**，目标卡始终是同一张卡；
2. **构建**（`NameStabilizer.Stabilize`）里顺序固定为 `RepairReferences`（按类名表认名字）→
   **`RenumberCardNames`（搬引用）** → `Freeze`（写实类名）——`RenumberCardNames` 自己的注释
   就写着「必须排在 Freeze 之前」，以前漏了；
3. `Naming.RenumberCardClassNames` 补上**「没填类名」那一支**的旧名映射：以前只更新
   `LastAutoOrdinal`（第几号）不产出 `旧名 → 新名`，而这一类卡最常见 —— 一改号就搬不动。
   现在按 `LastAutoOrdinal` 拼出旧名写进 `renamedOut`（`<= 0` 的新卡跳过，不产生 `…Card0` 假映射）。
   `LastAutoOrdinal` 是 `[JsonIgnore]`，界面每次刷新候选表（含载入存档）都会被写实，所以开着存档改列表也准。

自检 3 条钉住（含反向）：插一张卡后引用搬到同一张卡的新编号；**反向**断言老名字现在属于另一张卡
（把用户看到的现象钉死）；构建那条路冻出来的类名与引用一致。
反向验证：临时关掉第 3 处 → 3 条 FAIL（引用停在 `SevenCard3`，主人变成「挪卡2」），打开即 0 FAIL。

### ⚠ V0.2.156：**拖动**卡牌这条路上，卡面（按类名存）会跟错人 —— 三层 bug

用户报「在卡牌列表拖动卡牌后，卡面会变成其他卡」。卡面存在 `Art.CardPortraits[类名]`，
而自动类名按**行号**算 ⇒ 名字没跟卡走 = 图跟错人。三层叠在一起：

1. **时机**：拖动的落点是 `Cards.Move → RefreshCardOrdinals() → SyncDetail()`，而
   `RefreshCardOrdinals` 会把 `LastAutoOrdinal`（= 上次的号）就地改成**新**行号 ——
   可「旧名 → 新名」映射正是靠它算的（见上一节第 3 点）⇒ 先覆盖就永远算不出映射。
   V0.2.153 把同步点接在 `RefreshCardChoices`，**拖动这条路不走它**。
   → 现在 `RefreshCardOrdinals()` 的**第一件事**就是 `SyncCardClassReferences()`。
2. **重入**：搬迁会改 `ClassName` / `LastAutoOrdinal` / `SpawnCardId` → `PropertyChanged` /
   `CollectionChanged` → `OnCardChanged` / `OnCardsChanged` **重入**刷新 → 又搬一次；
   重入时号只改了一半 → 算出**部分映射**又搬一遍（实测量到：一次拖动搬了 **4 遍、每次一张**）。
   → `SyncCardClassReferences` 加 `_syncingCardRefs` 重入保护（搬迁期间重入直接返回），
   并且 `Naming.RenumberCardClassNames` 改成**两趟**（先算完整张映射表、再动手改卡）——
   即便有重入也算不出半张表。
3. **环**：把牌往上拖会让名字成**环**（`[A,B,C]→[C,A,B]`：12→13、13→14、14→1）。
   「顺序搬 + 目标已有图就跳过」在环上会把刚搬进去的那张图**删掉**（原 12 的图整个丢）。
   → 搬卡面改成**从零重建新表**（`key → renamed[key] ?? key`），对环天然正确。

自检 3 条走**真实**的 `RefreshCardOrdinals` + `Cards.Move`（不是另写模拟）：拖动后三张卡面各归各卡、
反向说明「旧名字现在属于另一张卡」、以及**名字成环**时也对。反向验证：关掉重入保护 → 2 条 FAIL。

另外修掉了一个会让「一键构建」**必然失败**的老 bug：宠物效果里的 `<宠物>Cmd` 在生成时是个占位符，
以前只有普通卡那条路会替换它，**诅咒 / 先古卡 / 遗物 / 药水**的 `CanonicalVars` 会把
`__PETCMD__` 原样写进代码 → `CS0103`（用户日志里 `DaogeCard9.cs(21,172)` 那两条就是）。
现在统一在 `VarDeclaration` 里替换（找不到那只召唤物时退成「固定 0」的普通变量），
占位符不会再进生成代码。

## 原版状态改名：只在你自己的角色上生效

「本体状态改写」（把易伤改叫破绽、把中毒改叫剧毒…）过去是**覆盖本体的本地化键**实现的，
而本地化表是**全局**的 —— 一改，别的角色、百科、以及任何提到这个状态的地方都会跟着变
（用户实测：sparkle 把易伤改成破绽后，百科里铁甲战士的「战栗」描述也成了破绽）。

现在改成两条互不干扰的路子：

- **新名字 / 新描述写进我们自己的键** `<SLUG>_FORGE.title / .description / .smartDescription`，
  本体的键**一个字都不动**；本体卡牌 / 遗物 / 药水文本也**默认不再改写**
  （要改写的话得自己在改写条目上勾「本体文本里的旧名字也一起换掉」，那一条是全角色范围的）。
- 生成一个 `ForgePowerRename` 补丁（Postfix `PowerModel.Title / Description / SmartDescription`），
  **只在「这一局玩的是我方角色」时**才返回我们的键。判定用的是本体自己的公开链：
  `RunManager.Instance.IsInProgress` → `DebugOnlyGetState()` →
  `LocalContext.GetMe(state)?.Character is <你的角色类>`（本体的百科页也是这么判的）；
  主菜单、别的角色一律原名。图标 / 大图标 / 层数颜色 / 血条颜色四个补丁**同样**加了这把开关。

于是：你自己的角色里显示破绽（含战斗中的状态图标与悬停提示），
百科里铁甲战士的「战栗」、别的角色的任何文本，永远还是「易伤」。

## 「重放卡牌」按本体的防御写法来

本体「隐藏宝石 HiddenGem」给别的牌加重放时是**先筛、再判空、只取一张**。生成的代码现在照它写：

- 先算**候选池**：去掉「打不出去」的牌（Unplayable / 状态 / 诅咒）与**已经有重放**的牌
  （本体是 `c.GetEnchantedReplayCount() < 1`）—— 不然重放会一层层累乘；
- 池子空了就**整条跳过**（本体是 `if (list.Count == 0) return;`），不会拿着空选牌界面等玩家；
- 要的张数**永不大于池子**（`Math.Min(N, pool.Count)`），选牌界面永远能满足；
- 预览用 `CardCmd.Preview(列表)` **整批一次**（本体自带的重载），不再一张一张弹。

## 「获得类」数值填负数 = 反向效果

生成器里凡是「获得 / 施加」的数值都可以填**负数**，生成的就是反向动作（本体对非正数基本都直接 return，
所以每一条都换成了对应的反向命令）：

| 效果 | 填 -N 时生成的 | 依据 |
|---|---|---|
| 获得格挡 | `CreatureCmd.LoseBlock(...)` | 本体 `GainBlock` 对非正数什么都不做 |
| 施加增益/减益<br>（**本体允许负层数**的 5 个状态：力量 / 敏捷 / 专注 / 尖啸 / 萎缩） | 直接 `PowerCmd.Apply<T>(目标, -N)` | 本体「尖啸 PiercingWail」扣敌人力量就是这么写的（`StrengthPower.AllowNegative => true`）—— 目标身上**本来 0 层**也能扣成 -N |
| 施加增益/减益<br>（其余状态：虚弱 / 易伤 / 中毒…） | 先 `GetPower<T>()` 取目标身上那份实例，再 `PowerCmd.ModifyAmount(-N)` | 这些状态层数不能为负，只能从已有层数里扣（直接 `Apply(-N)` 会挂出一个负层数、什么也不做的状态）。目标身上没有这个状态时什么都不做 |
| 获得能量 | `PlayerCmd.LoseEnergy(N)` | 本体 `GainEnergy` 第一句就是 `if (!(amount > 0m)) return;` |
| 获得金币 | `PlayerCmd.LoseGold(N)` | 同上（`GainGold` 对非正数直接返回） |
| 获得最大生命 | `CreatureCmd.LoseMaxHp(...)` | — |
| 回复生命 | 走「失去生命」 | — |

卡面描述也跟着写成反向的话：「失去 5 点格挡。」「失去 3 层力量。」（给自己）/「扣除 5 层中毒。」（给敌人），
而不是「获得 -5 点格挡」。

> ⚠ 老版本（V0.2.138 及以前）**一律**走 `ModifyAmount`，于是「力量 -2」在目标身上本来没有力量时被静默跳过
> （用户报的「在生成器填负值的力量不生效」）。V0.2.139 起按上面那张表分两档 —— 力量 / 敏捷 / 专注 / 尖啸 / 萎缩
> 这 5 个现在会真的扣成负层数。

两个组合做不到，校验器会直接拦住并说清原因：
- **施加状态 + 负数 + 下回合生效**：下回合那套是给目标挂一个延迟状态（机制是**加**层数）；
- **临时增益 + 负数**：它的机制是「本回合 +X、回合结束撤掉」，负数的意思会变成「先扣、回合结束加回来」。

从工程回读（`--recover`）也认这些负数写法，负数层数 / 格挡会原样带回来（两种写法都认，符号不会读反）。

### 数值公式那一栏也判得出负数

卡牌上那 5 种支持公式的效果（伤害 / 格挡 / 失去生命 / 回复生命 / 施加状态）只要数值不是 0，
生成器会把固定数值**搬进「数值公式」那一栏**（界面从此只编辑公式）。所以负数分档看的是
「公式折出来的常数是不是负数」，不只是那个固定数值框 —— 否则从存档载入的卡（固定数值已经搬进公式了）
会退化成「按正数施加」。公式里有运行时量（X / 内置只读量）时正负生成期判不出来，照旧按「获得 / 施加正的」生成
（校验器有提示）。

## 翻倍目标身上的状态层数

效果种类里的「**翻倍目标身上的状态层数**」：数值 = **倍数**（2 = 翻倍，3 = 三倍…；**0 = 不生效**），
要翻倍哪个状态就在「增益 / 减益」那一栏选（这一栏一直可见），作用对象按「作用对象」栏来（自己 / 单体敌人 / 全体 / 随机）。

生成的是：先 `GetPower<T>()` 取目标身上**那一份实例**，再
`PowerCmd.ModifyAmount(choiceContext, 实例, 实例.Amount * (倍数 - 1), 施加者, this)` ——
本体没有「翻倍」这个 API，加「(倍数 − 1) 倍现有层数」就是同一个意思，而且走的是本体自己的层数变更
（会正常触发状态的层数钩子）。目标身上没有这个状态时什么都不做。

只做在**卡牌**上（它需要「选中的目标」，遗物 / 药水没有；生成前的检查会拦住）。

## 暴露 / 清除状态

三个「对目标身上的状态动手」的效果（都只做在**卡牌**上，需要「选中的目标」；生成前的检查会拦住遗物 / 药水）：

| 效果 | 生成的 | 说明 |
|---|---|---|
| **暴露**（数值 = 层数，0 = 不生效） | `CreatureCmd.LoseBlock(目标, 目标.Block)` → 有 `ArtifactPower` 就 `PowerCmd.Remove<ArtifactPower>` → `PowerCmd.Apply<X>(N)` | 照本体卡「暴露 Expose」写；施加哪个状态在「增益 / 减益」栏选 |
| **清除指定状态** | `目标.GetPower<X>()` 取到实例才 `PowerCmd.Remove(实例)` | 目标身上没有这个状态时什么都不做 |
| **清除所有减益** | 遍历 `目标.Powers.ToList()`，`TypeForCurrentAmount == PowerType.Debuff` 的逐个 `PowerCmd.Remove` | 先 `ToList` 再清，免得边遍历边改集合 |

顺带修掉一个隐患：这几条（以及「翻倍状态层数」）以前没被算进「这张牌要不要选目标」，
所以**只带这类效果**的牌会生成成 `TargetType.Self`，打出去时 `cardPlay.Target` 是空的。
现在它们都会让牌变成「要选目标」（单体 / 全体 / 随机按「作用对象」栏来）。

## 禁止目标获得格挡

效果种类里的「**禁止目标获得格挡**」：数值 = 层数（本体的层数即持续回合数），
生成的就是 `PowerCmd.Apply<NoBlockPower>(目标, N, …)` —— 本体的 `NoBlockPower` 现成可用，
不用自己造状态。**填负数 = 反向**（取目标身上那份实例再 `PowerCmd.ModifyAmount` 扣掉），
卡面写「解除…的效果」。作用对象按「作用对象」栏来；只带这个效果的牌也会变成「要选目标」。

## 可被无限升级

「卡牌」页多了「**可被无限升级**」这个勾选框（默认不勾）。勾上以后生成的卡牌类里会多一行：

```csharp
public override int MaxUpgradeLevel => int.MaxValue;
```

本体默认每张牌**只能升 1 级**（`MaxUpgradeLevel => 1`；诅咒固定 `0` —— 所有诅咒都不能升级）。
覆写成 `int.MaxValue` 之后就能反复升级，而且**每升一级都会再跑一遍 `OnUpgrade()`**：
我们生成的升级增量（效果里的「升级增量」、卡牌上的「升级后费用」、「升级后关键词」）全写在那个方法里，
所以是「越升越强」而不是只换一次数值。卡面标题本体自己会显示成「名字+N」（本体 `CardModel.Title`
在 `MaxUpgradeLevel > 1` 时的写法）。

- 一处升级增量都没填就勾上：「配置检查」会提醒你 —— 那样反复升级只是白花钱。
- 从工程反推存档（`--recover`）时按 `MaxUpgradeLevel => int.MaxValue` 这一行认回来，不用另写标记行。

### 「升级增量」哪些效果有用

升级增量写在生成的 `OnUpgrade()` 里（`base.DynamicVars[…].UpgradeValueBy(增量)`），所以**只有声明了动态变量的效果**才有用。
⚠ 而且**光写 UpgradeValueBy 不够**：真正决定「选几张 / 生成几张 / 升级几张」的那一行也必须读这个变量，
否则卡面写 `{Cards:diff()}`（升级后显示 3）而代码还是写死 1 —— V0.2.79 修的就是这个（「生成卡牌的升级增量没反应」）。

| | 效果 |
|---|---|
| **有用** | 造成伤害 / 获得格挡 / 抽牌 / 回复生命 / 失去生命 / 最大生命 / 能量 / 金币 / 额外资源量 / 消耗·丢弃·**升级**·变化·复制·重放卡牌 / 预见 / 从三摞拿牌 / **生成卡牌** / 状态层数…（卡面写 `{…:diff()}`，升级后数字跟着变，**实际张数也会变**） |
| **没用**（填了会被配置检查警告） | 数字只是「循环几次」或压根没数字的那些：**升级卡牌（全局）/ 获得卡牌（全局）/ 变化卡牌（全局）/ 删除卡牌（全局）**、获得卡牌奖励、给予卡牌关键词、获得药水、结束回合、额外获得一回合、回合结束时自动打出 |

⚠ 注意「升级卡牌」和「升级卡牌（全局）」是两种：**战斗内**那个（从手牌 / 牌堆挑 N 张升级）声明了 `Cards` 变量，
升级增量**有用**（升级后多升级一张）；**全局**那个（改牌组）张数是写死的循环次数，升级增量没用。
锻造卡（节点图）的【效果】模块**没有「升级增量」栏** —— 「升级后变强」靠把「升级后」那块布单独画一遍。

## 状态挂在谁身上：「自己」就是挂着它的那个单位（V0.2.147）

用户问的：「当状态挂在敌人身上时，状态的自己是不是敌人自己」→ **是**。

| 概念 | 实际是谁（生成的代码里） |
|---|---|
| 状态效果里的 **「自己」** | **挂着这个状态的那个单位**（`base.Owner`）。贴在敌人身上时 = **那个敌人** |
| 状态效果里的 **「单体敌人 / 全体敌人」** | 本体 `CombatState.HittableEnemies` = **玩家视角的敌方那一侧**（绝对的一侧，不随持有者变）。所以贴在敌人身上时，它打的是**它自己和它的同伴** |
| 「自己受到伤害后 / 自己获得格挡后 / 自己失去生命时」这类时机 | 盯的是**持有者**（挂在敌人身上就盯那个敌人）✓ 能用 |
| 「玩家回合开始时 / 打出牌后 / 抽到牌后 / 花能量 / 获得金币 / 洗牌…」这类时机 | 钩子传进来的是**玩家**，护栏是 `player != base.Owner.Player` —— 挂在敌人身上时（`Player` 为 null）**永远不触发** |
| 「敌人回合开始时 / 敌人受伤后 / 敌人死亡后…」这类时机 | 盯的是**敌方那一侧** ✓ 贴在敌人身上能用 |

### 挂在敌人身上会「抛异常、战斗卡住」的效果（校验器会给警告）

自定义状态里凡是**要拿玩家对象**的效果，生成代码都会直接读 `base.Owner.Player`，而本体
`Creature.Player` **对怪物就是 null**（只有玩家单位那个构造函数会赋值）→ null 传进本体接口 =
运行时异常 → 战斗卡住（和「没有选牌提示语」同一类问题）。目前是这些（**机械扫生成源码扫出来的**，
见 `ProfileValidator.PlayerSidePowerEffectKinds`；自检里有一条断言钉着这份清单与生成端一致）：

- 抽牌 / 获得能量 / 获得金币 / 获得额外资源量 / **生成卡牌** / 获得药水；
- 获得卡牌（全局）/ 变化卡牌（全局）/ 删除卡牌（全局）/ 升级卡牌（全局）；
- 升级卡牌 / 消耗卡牌 / 丢弃卡牌 / **给予卡牌关键词**；
- 从抽牌堆 / 弃牌堆 / 消耗牌堆拿牌到手牌、获得卡牌奖励；
- ⚠ **任何效果只要勾了「概率生效」**也会读 `base.Owner.Player`（掷骰子）→ 同样会抛异常。

**能做「贴在敌人身上的减益 / 标记」的效果**（只跟持有者 / 敌方那一侧有关）：
造成伤害、失去生命、获得格挡、施加或清除状态、击晕、翻倍层数、暴露、毒性爆发、禁止格挡 / 禁止回复生命、
临时增益、强化指定卡牌、结束回合、额外回合、召唤伙伴那几种…

要做「为自己附魔」这类状态，请把**施加它的那个效果**的「作用对象」改成「自己」（让状态挂在自己身上）。

## 自定义状态上「生成卡牌」会让战斗卡死（V0.2.145）

**用户原话**：「自定义状态选择生成卡牌会卡死，看看日志和调试日志」。

**日志里的原文**（`%APPDATA%\SlayTheSpire2\logs\godot.log`）：

```
[ERROR] Combat #1 turn loop died while its combat is in progress; the combat is stuck
        until the room is restarted:
        System.InvalidOperationException: No selection screen prompt for POWER.SPARKLE_POWER6.
   at MegaCrit.Sts2.Core.Models.PowerModel.get_SelectionScreenPrompt()
   at SparkleMod.SparklePower6.AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
   ...
```

**真因（生成端漏了一条本地化）**：状态的「生成卡牌 + 按范围随机 + 多选1（候选 > 3）/ 从范围里自己挑一张」
生成出来的代码走的是本体那张**网格选牌界面**：

```csharp
CardModel? __picked = (await CardSelectCmd.FromSimpleGrid(choiceContext, __candidates, base.Owner.Player,
        new CardSelectorPrefs(base.SelectionScreenPrompt, 1))).FirstOrDefault();
```

而本体的 `PowerModel.SelectionScreenPrompt` 和 `CardModel` 是**同一份实现** ——
读 `powers` 表里的 `<Id.Entry>.selectionScreenPrompt`，**键不存在就直接抛异常**
（`throw new InvalidOperationException($"No selection screen prompt for {Id}.")`）。
工具给**卡牌 / 遗物 / 药水**三条链都写了这一条（`LocalizationGen` 里那三处 `NeedsSelectPrompt`），
**只有自定义状态这条链从来没写**（`PowersJson` 只写 title / description / smartDescription）→
抛在战斗回合钩子里 → 本轮 turn loop 直接死掉 → **战斗卡住**（只有重开房间才能恢复）。

**修法**（生成端，两处）：

1. `LocalizationGen.PowersJson`：自定义状态的效果里只要有「要弹选牌界面」的，就补一条
   `<状态>.selectionScreenPrompt`（提示语按效果种类给：多选1 → 「从 N 张中选一张」、
   自己挑 → 「选择要生成的牌」）；
2. `NeedsSelectPrompt` 补上「生成卡牌 + **从范围里自己挑一张**」这一档 ——
   它的候选 > 3 时走的也是那条网格界面，以前连名单都没进（这是同一个卡死的另一半）；
   同一条规矩对**「放置卡牌 + 取牌方式 = 自己选」**也成立（`SelectPrefsExpr(e, "base.SelectionScreenPrompt", …)`），
   所以一起补上 —— **卡牌上用这一档同样会卡死**（V0.2.145 第二遍才扫出来）；
3. 顺带补上**锻造图**：锻造卡 / 锻造药水 / 锻造遗物的效果在**节点图上**（`XxxSpec.Effects` 是空的），
   以前判断「要不要选牌提示语」时看不到图上的效果 —— 图上放「从某一摞拿牌到手牌 / 预见」同样会卡死。
   新增 `LocalizationGen.GraphEffects(graph)` 把图上的效果一起算进来。

**自检加了一条「机械扫描」**（`--uicheck` 里那条「凡是生成代码里会读 base.SelectionScreenPrompt 的效果种类…」）：
把**每一种**效果都按「最想弹选牌界面」的参数（自己选 / 手牌 / 多选1=4 / 从范围自己挑）配一遍，
分别塞进**卡牌链**和**自定义状态链**、生成源码，只要源码里出现 `base.SelectionScreenPrompt`，
`NeedsSelectPrompt` 就必须认得它 —— 认不出来当场 FAIL。
实测扫到 12 处（`GenerateCard`×2 条链、`CopyCard`、`ReplayCard`、`TurnEndPlay`、`Scry`、
`TakeFromDraw/Discard/Exhaust`、`PlaceCard`、`GiveKeyword`×2 条链），
写完这条断言它当场抓出漏网的 `PlaceCard(卡牌链)` —— 所以以后谁再加一条这种效果，漏了会立刻红。

**红线①（默认输出不变）**：判据只在真的需要时才加键 —— `--samplegen` 与 V0.2.143 的基线对拍
**112 个文件逐字节一致**（只差 `ExampleMod.sln` 的随机 GUID）。

**反向验证**：把 `PowersJson` 那两行临时注释掉重跑 `--uicheck` → **3 条 FAIL**
（就是「状态写出了 selectionScreenPrompt」那三条），改回即 0 FAIL。

**发版（V0.2.145，本地打包，没发 GitHub）**：用户「还是卡死」的原因是**他手上跑的还是 V0.2.143 那版程序** ——
上一次我只改了仓库、没有打包，所以他用旧程序重新生成的模组依旧缺那条键
（`D:\ds\s\4\程序文件\Sts2CharForge.Core.dll` 的时间戳停在 10/10 23:30）。
这一版用 `tools\release_all.ps1 -Version V0.2.145 -NoRelease -NoCommit -Yes` 走了正式打包：
`4\程序文件` 已更新成 V0.2.145（用户数据 1020 个文件原样放回、旧启动器删掉）、
更新包 `Sts2CharForge_更新包_V0.2.145.zip`（60.6 MB）已产出，
并且核对过「部署进去的 `Sts2CharForge.Core.dll` 与本机 Release 构建 **SHA256 完全相同**」——
所以新程序里确实带着这次的修复。

**探针真编译**（`_tmp` 自造存档、只 `--build` 不 `--install`）：三个状态
（多选1=4 / 从范围自己挑 / 从某一摞拿牌）生成出来的工程 `dotnet build` **0 错误**，
`localization/zhs/powers.json` 里三条 `selectionScreenPrompt` 都在，状态源码里读的正是 `base.SelectionScreenPrompt`。

> ⚠ 日志里另外两条（`Missing sprite 'sparkle_power6' in power_atlas`、
> `Asset not cached: res://images/powers/missing_power.png`）是**另一件事**：这个状态没上传图标。
> 生成器只在「上传了图标」时才写图集 `.tres` + `images/powers/<状态>.png` 回退图
> （见「状态 / 遗物图标的『本体回退路径』」那一节），所以没上传就是本体那张 `missing_power` 紫色占位 ——
> 不影响战斗，想换掉就上传一张状态图标（建议 256×256 方图）。

## 遗物 / 药水 / 额外资源量右侧效果栏：改成和卡牌页同一个版式（V0.2.144）

**用户原话**：「遗物药水右侧的效果栏能不能同步卡牌的」→ 拍板 **A：照卡牌页版式重做**（三页一起改）。

**改成了什么**（现在这四页右侧**形状完全一致**）：

| | 以前（V0.2.137~143） | 现在（V0.2.144） |
|---|---|---|
| 列表 | 每条效果一行摘要（▶ + 摘要 + 「展开 / 收起」），点开才在**行内**展开编辑器 | **一行摘要列表**（固定 140px）：每行就是 `EffectSpec.DisplayPlain`（遗物 / 药水不能升级，不显示「（升级 +N）」） |
| 编辑器 | 摊在列表行里（折叠 / 展开） | 列表**下面一整块共享编辑区**，绑「列表当前选中的那条效果」（和卡牌页同一套：卡牌 `Display` / 这三页 `DisplayPlain`） |
| 单条效果的条件 | **右下角单独一块**（绑 `SelectedItem`，和列表选择联动） | **跟着这条效果长在编辑区里**（`EffectConditionPlain`，候选表还是 `PlainEffectConditions` 那一份） |
| 没选中效果时 | 折叠着看不见 | 编辑区里显示一句「点上面一条效果 → 就在这块里改它」（不会凭空冒出一堆开关） |
| 折叠标记 | `EffectSpec.IsExpanded`（`[JsonIgnore]` 的界面状态） | **已从模型删掉**（这套交互不存在了） |

**为什么这次是「改版式」而不是再修一次补丁**：V0.2.143 修的「条件选完存不住」真因是
**整行是一颗 `Button`**（点开 / 收起），Button 在冒泡阶段吃掉 `MouseLeftButtonDown` →
外层 `ListBoxItem` 永远收不到点击 → `SelectedItem` 恒为 null → 靠它取数据的条件框写进空对象。
现在行换回**普通 `ListBoxItem`**（点一条 = 原生选中），那个结构性原因**从根上没有了** ——
`OnToggleEffectRow` / `SelectEffectRowOf` / `ListBoxOwningEffect` / `ToggleEffectRow` 整块删掉。

**顺手修掉两处被这次改版暴露出来的老毛病**（都不是这次新引入的，只是以前藏起来了）：

1. **编辑器里的长说明文字横向跑出面板**：`多选1 / 复制的份数 / 重放次数 / 回合结束时 / 缓慢` 那几段
   提示原本是「一行不换行的超长文字」（卡牌页一直如此，只是没人量过）——现在三页编辑器常显，
   自检当场抓到（实测宽 788~1027px）。给它们 `MaxWidth="320"` + `TextWrapping="Wrap"` 折行；
2. **`ContentTemplate` 遇上 `Content=null` 也会把模板实例化**：没选中效果时那些行内 `Visibility`
   绑定解不出来、停在默认值 `Visible` → 界面上凭空冒出几行开关（自检抓到「遗物页看见了概率独立」，
   它的 `DataContext` 是 null）。现在三页的编辑区内容用 `NotEmptyToVisibility` 收起，
   空白位置换成一句指路文字。

**版式改动不碰存档 / 生成端 / 回读**：摘要是 `Display` / `DisplayPlain`（本来就有），
点的还是同一条效果，`--samplegen` 逐字节不变。

**自检**：`V0.2.144` 相关断言 **44 条**（三页各一组「点行 → 选中 → 编辑区跟着换 → 在编辑区改值真的写回模型 →
改条件写回模型 → 切走切回还在」+ 「行里没有 Button」+「`IsExpanded` 已删」+ 卡牌页回归），
Debug `--uicheck` **3334 PASS / 0 FAIL**、发布版（正式 publish 的 exe）**3334 PASS / 0 FAIL**。

> 卡牌页 / 诅咒页 / 先古卡页**没有动**：它们本来就是「一行摘要列表 + 一块共享编辑区」，
> 这次只是让另外三页跟上来（卡牌页那一条列表用的还是 `DisplayMemberPath="Display"`，因为它能升级）。

## 遗物 / 药水 / 额外资源量页右下角「单条效果的触发条件」修好了（V0.2.143）

**用户原话**：「遗物的条件选项怎么用不了了」→ 精确化之后是：中间那块「条件选项（整只遗物）」正常，
**右下角那块「单条效果的触发条件」用不了** —— 在下拉里选了条件**存不住**（切到别的遗物再回来 / 重开又变回「无条件」）。
用户给的验收口径是**「做的和卡牌一样就行了」**（卡牌页同一功能一直是好的）。

**真因（在界面事件层，不是数据层）**：卡牌 / 遗物 / 药水 / 额外资源量四页共用同一个「折叠效果行」模板
`EffectEditorCollapsible`（`MainWindow.xaml`），而**整行就是一颗 `Button`**（`Click="OnToggleEffectRow"`）。
WPF 里 `Button` 在**冒泡阶段**先处理 `MouseLeftButtonDown` 并置 `e.Handled = true` →
外层 `ListBoxItem` **收不到这次点击** → **那一行永远不会被选中**。于是：

```xml
<Border x:Name="RelicEffectConditionBox"
        DataContext="{Binding SelectedItem, ElementName=RelicEffectList}">   <!-- 一直是 null -->
```

右下角那块绑的正是 `SelectedItem` → **一直是 null** → 在里面选条件 = **写进空对象、静默丢失** →
界面一重读就回到「无条件」。**卡牌页为什么没事**：卡牌页没有这块独立条件框，它的条件编辑器在**展开区里面**
（绑那一行**自己**的效果对象），不依赖 `SelectedItem`。

**修法**：点效果行那个展开按钮时，**顺手把这一行选中**（`OnToggleEffectRow` 里新增一句 `SelectEffectRowOf(...)`）：
从被点的元素往上找 `ListBoxItem` → 反查它属于哪个 `ListBox` → 把 `SelectedItem` 设成这一条效果；
找不到 `ListBoxItem`（虚拟化 / 拿不到父链）时退化成「在窗口可视树里找那个 `ItemsSource` 正好装着这条效果的列表」
（找到多个 / 找不到就安静返回 —— 宁可不选中，也不许把别的页的列表改坏）。四页共用这一个函数，所以三页一起修好，
顺带修好「点了行但列表里没高亮」这个既有小毛病。

**两处写的其实是同一个条件**：右下角那块和展开区里那份「单条效果的条件」都绑**同一个** `EffectSpec.Condition`
（自检里有一条断言专门钉「两处 DataContext 是同一个对象」）—— 改哪边都一样，**不是两份数据**。

**自检补上了这次漏掉的方向**：以前只测「模型 → 界面」（把 `Condition.Kind` 设成 `HpInRange` → 断言 M 框显示出来），
**从没测过「在下列表里选 → 写回存档」**。现在 V0.2.143 新增 25 条断言：真的点那一行那颗按钮 →
断言 `SelectedItem` **就是那一条效果**（修好之前是 null）→ 在右下角下拉里真的选一个条件 → 断言写回
`EffectSpec.Condition.Kind` → 切走再切回**条件还在**（遗物 / 药水 / 额外资源量各一组），
外加卡牌页回归（卡牌页不误改）与「两处同一个对象」。
**反向验证**：把新加的那句「选中」注释掉 → `--uicheck` **11 条 FAIL**（3309 → PASS 3328 / FAIL 11）；改回即 PASS 3339 / FAIL 0。

> **V0.2.144 起这一段已经过期**：三页右侧改成了卡牌页那种版式（行不再是 Button、右下角那块条件框也撤了），
> 所以 `OnToggleEffectRow` / `SelectEffectRowOf` 连同折叠行一起删掉了 —— 同一个毛病从结构上不会再出现。
> 下面这段留作历史记录（真因分析与当时的修法）。详见上面「V0.2.144」那一节。

## 生成调试日志（V0.2.142，排查运行期问题用）

**在哪**：「角色」页倒数第二行 —— 一个勾选框，文案是
**「打开『生成调试日志』（排查用：只影响日志，不影响游戏行为；排查完请关掉）」**。

**它是干什么的**：游戏里的日志本来只记异常，**不记「数值前后」也不记你打了哪张牌** ——
所以像「填了 -2 层力量，游戏里却没反应」这种**运行期**问题根本查不下去（日志里没有异常，
也看不出「目标身上本来有没有这个状态」）。勾上这个开关再重新生成，模组就会在**每条效果真正执行的地方**
往游戏日志里多打几行 `[模组调试] …`（游戏日志里看到的是 `[INFO] [模组调试] …`），写明：

- **谁在跑**：哪张牌 / 哪只遗物 / 哪瓶药水 / 哪个自定义状态 + 英文类名，以及效果种类；
  （负数「施加状态」还会说明实际走的是哪一档：「施加负层数」还是「扣除已有层数」。）
- **目标是谁**：目标的 `Creature.Name` + **是不是自己**；
- **数值前后**：
  - **施加状态**：**执行前**层数与**执行后**层数**各打一行** ——
    目标身上**本来没有**这个状态时记 `-` 并标「本来没有这个状态」，已经有就记真实层数并标「已经有了这个状态」；
    同时打出这次实际施加的层数（`施加量=…`）。**这两种情形正是要区分的**，也是这个开关存在的主要理由；
  - **造成伤害 / 获得格挡 / 回复生命 / 失去生命 …**：打出实际传进去的那个数值与目标；
  - 其它种类至少打一行「谁 + 种类 + 目标」。

例（游戏日志里的原文）：

```
[INFO] [模组调试] 卡牌「调试尖啸」(DbgPiercingWail) → 施加增益/减益（负数 = 扣除层数）（状态 StrengthPower）（实际按「施加负层数」执行：这个状态本体允许负层数，本体「尖啸」就是这么写的）；目标=[铁甲战士]（不是自己）；执行前层数=2（已经有了这个状态）；施加量=-2
[INFO] [模组调试] 卡牌「调试尖啸」(DbgPiercingWail) → 施加增益/减益（负数 = 扣除层数）（状态 StrengthPower）（实际按「施加负层数」执行：这个状态本体允许负层数，本体「尖啸」就是这么写的）；目标=[铁甲战士]（不是自己）；执行后层数=0（现在有了）
[INFO] [模组调试] 卡牌「调试打击」(DbgStrike) → 造成伤害；数值=(base.IsUpgraded ? 10m : 7m)；目标=[铁甲战士]（不是自己）
[INFO] [模组调试] 自定义状态「调试回血状态」(DbgHealOnHit) → 回复生命；数值=3m；目标=[铁甲战士]（自己）
```

**三条保证**：

1. **默认关着**（老存档没有这个字段 → 读进来就是 `false`）：关着时生成出来的代码与 V0.2.141
   **逐字节一致**（`--samplegen` 112 个文件的 SHA256 逐个对过，只有 `ExampleMod.sln` 的随机 GUID 不同）。
2. **只影响日志，不影响游戏行为**：加的全是「读一下、打一行」的语句，不碰任何状态与数值；
   原本那几条语句**一个字符都没改**（日志是另加的独立行）。
3. 覆盖了**五条链**：卡牌 / 遗物 / 药水 / 自定义状态 / 锻造节点图（含「给伙伴施加状态」）。
   召唤物守卫类效果（`PetGuardOn` / `PetGuardOff`）没接（那两条本来就不产生层数变化）。

⚠ 日志会变多，**排查完请把勾去掉再重新生成一次**。

### V0.2.141 修：「数值公式 + 升级增量」回读时增量被算重一倍（用户报的「填负值的力量不生效」）

**现象**：在生成器里填「力量 -2 / 升级增量 0」，生成 → 从工程恢复存档 → 再生成，升级过的那张牌打出去
**一点效果都没有**（没升级的那张才是 -2 层）。

**真因（两件事叠在一起）**：

1. **回读把同一个增量加了两遍**（真因，在回读端，生成端一直是对的）：对「数值公式 + 升级增量」的 5 种效果
   （造成伤害 / 获得格挡 / 失去生命 / 回复生命 / 施加增益减益），生成端会把**同一个增量写三处** ——
   ① 变量构造第二个参数 `new ForgeFormulaPowerVar<StrengthPower>(static (card, target) => (1m), 1m)`、
   ② 打出时那个三元式 `(base.IsUpgraded ? 2m : 1m)`、
   ③ `OnUpgrade()` 里的 `base.DynamicVars["StrengthPower"].UpgradeValueBy(1m)`。
   回读时 ①② 已经算出增量（`A - B` = 1），③ 是**同一个数**，而旧代码在 `UpgradeValueBy` 那一支
   **无条件 `+=`** → 1 变 2。再生成出来就成了 `(base.IsUpgraded ? 3m : 1m)`：
   公式 -2 的卡升级后 = `-2 + 2 = 0` 层 = **升级过的牌打出去什么都不发生**。
2. **没有任何提示**：就算增量是对的（用户自己填「升级增量 2」配公式 -2），升级后那一档确实是 0，
   而界面上一个字都不说，用户只能看到「填了负值没反应」。

**修法**：

- **回读**：给效果加一个「这一条的增量是从那个三元式拿到的」标记（**不用「值是不是 0」猜**）。
  `OnUpgrade` 里的 `UpgradeValueBy` 遇到带标记的效果**不再相加** ——
  两边**相等**（生成端就是这么写的）→ **跳过**，保留三元式算出来的值；
  两边**不等**（老产物 / 手改过的工程）→ 以 `UpgradeValueBy` 为准**覆盖**
  （它是本体真正生效的那一份：`OnUpgrade` 改的是变量，打出时读的也是那个变量）。
  **非公式那条路没动**：`new DamageVar(6)` + `UpgradeValueBy(3m)`（没有三元式）照旧累加到 3；
  真·现算公式（`M*2` + 3m，走不到三元式那一支）照旧是 3。
- **校验器加一条警告**（**不是错误**，绝不拦住老存档生成）：纯常数公式 + 升级增量
  算出来升级后是 0 → 「升级增量 +2 把数值公式里的 -2 抵成了 **0**：升级后的这张牌打出去不会有任何效果
  （没升级的那张才是 -2）。想让升级后照样有 2 层，请把「升级增量」清成 0。」
  遗物 / 药水 / 状态那几条链上「数值 + 升级增量 == 0」同理（它们用固定数值）。
- **效果列表的摘要行**现在会把**升级后的最终值**写出来（常数公式那一档）：
  `增益/减益 StrengthPower 数值公式 -2（升级后 0） → 自己` —— 不用点开逐条看就能发现。
  真·现算公式折不出常数，照旧只显示公式本身（`伤害 数值公式 M*2 → 单体敌人`），不写假数字。

> 验：`--uicheck` 里新增 20 条断言（回读 `UpgradeAmount==1`、再生成是 `? 2m : 1m` 且**不出现** `3m`、
> 老写法照旧 `Upg==3`、真·现算公式照旧 `Upg==3` 且公式原文 `M*2`、那条警告「有 / 无」两档 +
> 遗物链那条固定数值的同一判据 + 摘要行三种形态）；把核心修复**临时改回旧写法**重跑，那两条核心断言确实 **FAIL**
> （`UpgradeAmount=2（期望 1）` / `出现了 3m（增量算重了）`），改回来即 PASS（Debug 与发布版均 **3297 PASS / 0 FAIL**）。

## 触发时机「自己失去生命时」

自定义状态（能力）的触发时机里多了「**自己失去生命时（每一下）**」，走的是本体的
`AfterCurrentHpChanged`（本体「肉骨头 MeatOnTheBone」与亡灵契约师的 `NecroMasteryPower` 用的就是它）。
`delta < 0` 表示生命值真的少了，生成的是：

```csharp
public override async Task AfterCurrentHpChanged(Creature creature, decimal delta) {
    // 这个钩子本体不给 choiceContext：按本体做法自己造一个（不然造成伤害 / 抽牌会 CS0103）
    if (creature != base.Owner) return;   // 只认自己掉血
    if (delta >= 0m) return;              // delta 为正是回血，不算「失去生命」
    ...
}
```

和已有的「自己受到伤害后（每一下）」的关系：

| 触发时机 | 本体钩子 | 什么时候触发 |
|---|---|---|
| 自己受到伤害后（每一下） | `AfterDamageReceived` | 走**伤害管线**的那一下（判据 `UnblockedDamage > 0`：全被格挡 / 0 伤害不算）。⚠ **「失去生命」不算**（`Unblockable \| Unpowered` = 血祭 / 献祭代价 / 中毒那种穿盾且无来源加成的掉血已被排除，见上）—— 那种掉血用这条「自己失去生命时」 |
| **自己失去生命时（每一下）** | `AfterCurrentHpChanged` | 只要**生命值真的少了**：伤害、自己「失去生命」、直接扣血（致死那套）都算；回血不算 |

所以后者把前者包含在内，**但前者不包含「失去生命」**（V0.2.123 起）：「受到伤害后」只管伤害管线那一下，
「失去生命」这一类掉血只走「失去生命时」那条（本体 `Hook.AfterDamageReceived` 是无条件广播的，
以前两边都会触发，用户实测报过「自己受到伤害时怎么还是被失去生命触发」）。
两个都是**每一下都会触发一次**（敌人「1 点 × 5 下」的连击就是 5 次）。
配置检查会把这条差别写成「提示」；从工程反推存档时按 `AfterCurrentHpChanged` 认回这条触发时机。

顺带修掉一个回读漏洞：自定义状态的触发器**没有动态变量**（数值是生成期字面量），
原来「获得格挡 / 失去格挡」这两条只按变量回读，反推存档时会把数值静默变成 0 ——
现在按参数里的字面量兜底（探针实测「获得 3 点格挡」能原样带回）。

## 刮削（抽 N 张 → 对其中符合条件的牌执行选定的操作）

效果种类里的「**刮削**」（本体卡「刮削 Scrape」那套做法）：抽 N 张牌，然后对其中**符合条件的牌**
执行**选定的操作**。默认那一档就是本体的刮削 —— 抽 N 张，把其中**所有非 0 费**的牌丢进弃牌堆
（0 费牌留在手上），生成的就是本体那两句：

```csharp
IEnumerable<CardModel> __scraped = (await CardPileCmd.Draw(choiceContext, base.DynamicVars.Cards.BaseValue, base.Owner))
    .Where(c => c.EnergyCost.GetWithModifiers(CostModifiers.All) != 0 || c.EnergyCost.CostsX);
await CardCmd.Discard(choiceContext, __scraped);
```

- 数值 = 抽几张（0 = 不生效，守「所有种类都能填 0」那条规矩），用本体的 `CardsVar`：
  卡面写 `{Cards:diff()}`，升级增量也加在它上面。
- 界面上多两行：**「其中符合条件的牌」（条件）**和**「执行」（操作）**。
  - 条件：**非 0 费**（默认，本体「刮削」的判据）/ **不限**（抽到的每一张都动手）/
    攻击 / 技能 / 能力（按牌的类型筛）/ 诅咒 / 状态（按稀有度筛）/
    「自定义关键词：××」那一组（把自定义关键词当成一套**卡的组**用，和「范围限定 / 范围界定」是同一份选项表；
    关键词只是我们自己的标注，游戏里没有对应枚举，所以**生成时**会查成显式判据
    `c.Id == ModelDb.Card<那张卡>().Id`）。
  - 操作：**丢弃**（`CardCmd.Discard`，进弃牌堆、洗牌后会回来，奇巧这类跟弃牌有关的钩子照常触发）/
    **消耗掉**（`CardCmd.Exhaust`，本场战斗不再回来）/ **升级**（`CardCmd.Upgrade`，
    和「升级卡牌」同一个命令；战斗里的升级只影响本场战斗）。
- 条件选「不限」时那一句里**不会**出现 `.Where(...)`（不筛，抽到的每一张都动手）；
  操作换成消耗 / 升级时改走逐张的 `foreach`。
- 条件选自定义关键词那一组、但那一组一张牌都没有 → 校验器**报错**（和「范围限定 / 范围界定」同一条规矩）；
  条件 / 操作的 Id 认不出来也会报错（不然效果会静静地什么都不做）。
- 卡牌 / 遗物 / 药水三处都能用；触发时机本身没有 choiceContext 时会给出警告（不会生成坏代码）。
- 从工程反推存档时按 `CET:Effect=ScrapeDraw` 标记整段认回来（数值、是否 X 都在标记里）；
  条件和操作只在**不是默认值**时才追加 `CET:ScrapeCond=` / `CET:ScrapeOp=`，
  所以默认档的生成文本与以前逐字一致（老存档回读回来仍是默认值）。

## 用「目标卡」填满手牌（本体「迫降」那种）

效果种类里的「**用「目标卡」填满手牌**」：算出手牌还差几张，然后用你在「目标卡」那一栏选的那张卡补齐。
照本体卡「迫降 CrashLanding」的写法生成：

```csharp
int __fillTo = CardPile.MaxCardsInHand - CardPile.GetCards(base.Owner, PileType.Hand).Count();
if (__fillTo > 0) {
    List<CardModel> __filled = new List<CardModel>();
    for (int i = 0; i < __fillTo; i++) __filled.Add(base.CombatState.CreateCard<目标卡>(base.Owner));
    await CardPileCmd.AddGeneratedCardsToCombat(__filled, PileType.Hand, base.Owner);
}
```

- 数值没有意义（手牌上限 - 当前手牌数就是张数，手牌满了就什么都不发生），所以这一条的数值栏是 0~0；
  也**不会**声明动态变量（卡面不出现用不上的占位符）。
- 目标卡没填时退成小刀（`Shiv`），配置检查会给一条警告说清楚。
- 卡牌 / 遗物 / 药水三处都能用；触发时机不在战斗里时给警告（不会生成坏代码）。
- 从工程反推存档按 `CET:Effect=FillHand` + `CET:Card=<卡类名>` 认回来。

## 抽到就减费（N 回合内，每次抽到的牌费用 -1）

效果种类里的「**抽到就减费**」：给自己挂一个生成的 `<角色>ForgeDrawCostPower`，层数 = 还能持续几个回合。
本体没有现成的状态，用公开 API 自己写（不需要 Harmony）：

```csharp
public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw) {
    if (card.Owner != base.Owner.Player) return;
    if (card.EnergyCost.CostsX || card.EnergyCost.Canonical < 0) return;   // X 费 / 不能打出的牌不动
    int __now = card.EnergyCost.GetWithModifiers(CostModifiers.All);
    if (__now > 0) card.EnergyCost.SetThisCombat(__now - 1);               // 本体 ConfusedPower 就是这么改费用的
}
// 每回合掉 1 层，掉光自毁
```

- 数值 = 持续回合数；**填负数 = 反向**（取实例后 `PowerCmd.ModifyAmount` 扣层数），卡面写「解除「抽到就减费」N 层」。
- 只有存档里真的用到这个效果才会生成那个 Power 类（不用就不生成，免得留下没用的代码）。
- 卡牌 / 遗物 / 药水三处都能用。
- 从工程反推存档按 `CET:Effect=CheapenDraw` 认回来。

> ⚠ **它减的是「这期间抽到的每一张牌」，不是「这张牌自己」**（用户问过「怎么是别的牌减费」）：
> 它是一个挂在自己身上的状态，抽到谁就给谁 -1。想要「抽到**这张牌**时、**这张牌**费用 -1」请用下面那条新效果。

## 抽到这张牌时它本场战斗的费用 -N（不要回合数限制的那种）

效果种类里的「**抽到这张牌时它本场战斗的费用 -N（可叠加；0 = 不生效）**」——
就是「抽到这张牌后，本场战斗内这张牌的费用 -1」这件事，**只作用在这张牌自己身上、没有回合数限制**。

- 数值 = 每次抽到减多少（默认 1；**值 0 = 这条效果不存在**：卡面不写、代码也不生成）。
- 生成的是**卡自己**的钩子（照抄本体卡「王者一击 KinglyKick」）：

  ```csharp
  public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw) {
      if (card == this) base.EnergyCost.AddThisCombat(-1);   // 抽到几次叠几次
      return Task.CompletedTask;
  }
  ```

- 用 `AddThisCombat(-N)` 而不是「读出当前费用再 `SetThisCombat(当前-N)`」：本体的费用是
  「基础费 + 本场加成」，`AddThisCombat` 直接记一笔「本场 -N」，抽到几次叠几次，也不会被别的改费效果冲掉，
  而且本体自己会夹住下限（费用不会变负）。
- 只做在卡牌上（遗物 / 药水 / 自定义状态都没有「这张牌」；想要状态版的用上面那条「抽到就减费」）。
- 它没有可升级的数值，所以「升级增量」填了不生效（校验器会提醒）；从工程反推按 `CET:Effect=CheapenSelfOnDraw` 认回来。

顺带修掉一个回读漏洞：「禁止获得格挡」的**正数**分支只有一句 `PowerCmd.Apply<NoBlockPower>`
（没有 `__negPower` 那种记号），以前会被再当成一条「施加状态」多回读一条 → 下次生成会多加一次。
现在这类「没有独特记号的实现行」都按标记行里的种类整段跳过（探针实测：`ForbidBlock(2)` / `ForbidBlock(-1)`
不再多出一条 `ApplyPower(NoBlockPower)`）。

## 禁止目标回复生命

效果种类里的「**禁止目标回复生命**」：给目标挂上本模组自己生成的状态，N 回合内该目标的**回复量恒为 0**。

本体**没有**任何「治疗量修改」的钩子（全库只有营地的 `ModifyRestSiteHealAmount`），所以配一条 Harmony 前缀：

```csharp
[HarmonyLib.HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal))]
internal static class <角色>ForgeNoHealPowerPatch {
    // ref 改参数，而不是「跳过原方法」：原方法是 async Task，跳过会让调用方 await 到 null
    private static void Prefix(Creature creature, ref decimal amount) {
        if (creature is not null && creature.HasPower<<角色>ForgeNoHealPower>()) amount = 0m;
    }
}
```

- 数值 = 回合数；**填负数 = 反向**（取实例后 `PowerCmd.ModifyAmount` 扣层数），卡面写「解除…的效果」。
- 状态自己会掉层（挂在自己身上 = 玩家回合末掉、挂在敌人身上 = 敌人回合末掉），掉光自毁。
- 只做在**卡牌**上（需要「选中的目标」；配到遗物 / 药水上时配置检查会直接报错），只带它的牌也会变成「要选目标」。
- 只有存档里真的用到这个效果才会生成那个状态 + 补丁。
- 从工程反推存档按 `CET:Effect=ForbidHeal` 认回来。

## 存档安全：自动备份 + 从工程恢复

**每次保存前，旧内容都会自动留一份**：

- 同目录的 `<存档名>.json.bak`（上一次的内容）；
- 存档目录下的 `_备份\<存档名>.<时间戳>.json`（最多留 30 份，旧的自动清）。

**万一存档被覆盖 / 改坏 / 整个删掉**：工程里带着全部信息，可以从工程反推回存档 ——

- 界面：「配置存档」页 → **「从工程恢复存档…」**，选一个以前生成出来的模组工程目录，
  反推出卡牌（费用 / 稀有度 / 关键字 / 每条效果的数值与升级增量 / 条件 / 自定义描述 / **是不是无色卡**）、遗物、药水、自定义状态、
  角色属性、本地化名字、上传过的图，另存为 `<工程名>_恢复.json`；
- 命令行：`forge --recover "工程目录" --out "恢复.json"`（加 `--profile 旧.json` 可补上环境路径）。

### 命令行（`forge.exe`）—— 安装必须**显式**给 `--install`（V0.2.137 起）

同一套核心，界面能做的事命令行基本都有；**唯一要特别记住的是「安装」**：

| 命令 | 做什么 |
|---|---|
| `forge --profile x.json` | 只生成工程 |
| `forge --profile x.json --out "D:\out"` | 生成到指定输出目录 |
| `forge --profile x.json --build` | 生成 + 编译 + 导出 PCK —— **不会安装到任何地方** |
| `forge --profile x.json --build --install "D:\...\mods"` | 上面那套 + **安装**到那个目录 |
| `forge --buildcheck`（可加 `--godot "<Godot_..._console.exe>"`） | 自检：**不给 `--install` 时绝不安装**（构建前后各数一遍「存档里写着的 InstallDir」那个陷阱目录） |
| `forge --samplegen "D:\out"` | 把示例配置生成一遍并打出每个文件的 SHA256（回归对照） |
| `forge --recover "工程目录" --out "恢复.json"` | 从工程反推回存档 |

⚠ **存档里的 `Paths.InstallDir` 只给图形界面用**：命令行**不再**拿它当兜底（V0.2.136 及以前会，
于是「只想编译一下」的探针会把模组真的装进存档里写着的游戏 mods 目录 —— V0.2.135 / V0.2.137 各发生过一次）。
图形界面里「一键构建」装不装也是**用户点的那个勾**决定的，不是靠这个字段硬装。

实现见 `src/Sts2CharForge.Core/Generation/ProjectRecovery.cs`：它是 `CSharpCodeGen` 的逆运算
（`CanonicalVars` + 构造函数 + `OnPlay` + `OnUpgrade` + `IsPlayable` + 本地化表 + 卡池/遗物池顺序）。
自检里有**往返测试**：示例配置 → 生成工程 → 反推 → 逐项比对（卡名 / 费用 / 效果 / 目标 / 升级 / 条件 / 池子归属
/ 强化指定卡牌 / 自定义描述 / 丢弃卡牌 / 「从哪里选牌」）。

这一版顺手补回来的几处回读（以前会静默丢效果或刷「没认出来」）：
「从哪里选牌」（消耗 / 变化以前根本不回读，一律退回手牌）、留空目标卡的 `CardCmd.TransformToRandom`、
「从抽牌堆 / 弃牌堆拿牌到手牌」（`__taken` 那两行）、「拥有额外资源量至少 N 点」这个条件。

### V0.2.120 补回来的：「数值公式」那种变量（`new ForgeFormula…Var(…)`）

V0.2.112 起，卡牌上的固定数值在打开存档时会自动**迁成公式**，声明也跟着变成
`new ForgeFormulaDamageVar(static (card, target) => (7m))`（施加状态那个是泛型
`ForgeFormulaPowerVar<VulnerablePower>(…)`）。变量解析的正则要求 `(` 后面紧跟**数字字面量**，
于是这类变量**整条都没被认出来**，后果有两层：

- 卡牌的**基础数值全部丢成 0**（回读出来的存档数值是 0，下一次生成就真的变成 0 了）；
- `OnUpgrade` 里的升级增量对不上效果 → 回读报告里刷一片「升级增量找不到对应效果（变量 Damage）」。

现在两种声明按**声明顺序**合并进同一张变量表（顺序就是生成代码里的顺序，红线），
公式里的常数（`(7m)`）当作数值；真·现算公式取不到常数时记 0 —— 数值本身由生成端的
`// CET:AmountFormula=` 标记负责还原，这里只要「种类 + 名字 + 顺序」对。
自检里加了三断言：迁成公式的固定数值回读成 7（不是 0）、升级增量 +3 认得回来、这一轮 0 条「没认出来」。
另外做过**不动点验证**：「生成 → 回读 → 再生成 → 再回读」两个来回，回读出来的存档**逐字节一致**
（同一个 SHA256）、全程 0 条「没认出来」—— 这套往返是无损的。

⚠ **V0.2.141 又修了这一条路上的一个反向毛病**：常数公式（`"1"` / `"-2"` 这种）的卡上，生成端把升级增量
写了**三处**（变量构造第二参 / 打出时的三元式 / `OnUpgrade` 的 `UpgradeValueBy`），而回读以前在
`UpgradeValueBy` 那一支又 `+=` 了一次 —— 于是「数值 + 升级增量」的卡回读出来增量**翻倍**，
再生成出来升级后那一档就变了（公式 `-2` 的卡升级后变成 `0` 层 = 打出去什么都不发生，
用户报的「填负值的力量不生效」）。现在回读带一个「增量是三元式给的」标记：两边相等就**跳过**、
不等就**以 `UpgradeValueBy` 覆盖**（绝不相加）；非公式那条路（`new DamageVar(6)` + `UpgradeValueBy(3m)`）
与真·现算公式照旧。详见「『升级增量』哪些效果有用 → V0.2.141 修」那一节。

静默保存（选解包工程目录、生成前）会先核对「内存里的配置」和「当前存档文件名」是否一致，
不一致就不写，避免把别的配置写进这份存档。

## 条件选项（每条效果各自一份）

条件选项挂在**每一条效果**上（不是整张牌），位置在「效果栏 → 选中一条效果 → 面板最下面」：

- **条件 / 数值 N / 状态 / 指向对象**：按选中的条件决定显示哪几栏；
- **指向对象**（`自己 / 指定敌人 / 任意一个敌人 / 全部敌人`）用于「拥有某状态至少 N 层」「生命值低于 N%」这类条件：
  「指定敌人」= 这张牌打出去时玩家选中的那个目标（`cardPlay.Target`）；
  「任意一个敌人」= 只要有一个活着的敌人满足就算满足，「全部敌人」= 每个活着的敌人都得满足（且至少有一个敌人活着）；
- 「指定敌人」**只能用在效果条件**里（那时玩家已经选好目标）。勾了「条件不满足时这张牌打不出去」时用不了它 ——
  判断能不能打出的时候玩家还没选目标，校验结果会给提示，这种情况请改用「任意一个敌人」；
- **卡牌**：勾上「条件不满足时这张牌打不出去」→ 条件会进本体的 `IsPlayable`（Clash / GrandFinale 的做法），
  卡面上也会描金边提示；不勾 → 只有这一条效果不生效，同一张牌的其它效果照常执行；
- **遗物**：页面下方那个「条件选项（整只遗物）」是这只遗物**整体**的触发条件（不满足时整个遗物不触发），
  效果栏里每条效果还能再有各自的条件（不满足时只有那一条不生效）；
- **药水**不支持条件（药水效果上没有这个入口）。

支持的条件里，「拥有额外资源量至少 N 点」读的是自定义资源（本体星星计数器 `PlayerCombatState.Stars`）；
「每 N 回合触发一次」「每场战斗只触发一次」只有遗物能用（卡牌请用「本回合已打出至少 N 张牌」这类）。

### 条件「卡牌时机」（被消耗 / 被生成 / 被保留 / 被丢弃 / 被变化 / 被给予自定义关键词）

条件里选「**卡牌时机（这张牌被消耗 / 被丢弃 / 被保留…时）**」后，下面会多一个「**卡牌时机**」下拉，
六项：**被消耗 / 被生成 / 被保留 / 被丢弃 / 被变化 / 被给予自定义关键词**。

这一条和别的条件**不一样**：别的条件都是「打出这张牌时算一个判断」，而「这张牌被消耗了」这件事
打出的时候可能还没发生（也许是别的牌把你丢掉了、也许是回合结束时虚无、也许永远不发生）。
所以带这个条件的效果**不会在打出时执行**，而是挂到本体对应时机的钩子上（本体「战鼓」就是这么做的：
它覆写 `AfterCardExhausted` 再判 `card == this`）：

| 子时机 | 挂在哪儿 |
| --- | --- |
| 被消耗 | `AbstractModel.AfterCardExhausted(choiceContext, card, causedByEthereal)` |
| 被丢弃 | `AbstractModel.AfterCardDiscarded(choiceContext, card)` |
| 被生成 | `AbstractModel.AfterCardGeneratedForCombat(card, creator)`（战斗里生成卡牌时本体广播的就是它） |
| 被保留 | `AbstractModel.AfterFlush(choiceContext, player, flushedCards, retainedCards)` —— 判据是「这张牌被本体归进了 `retainedCards`」 |
| 被变化 | `CardModel.AfterTransformedFrom()`（这张牌被变成了别的牌） |
| 被给予自定义关键词 | 我们自己生成的「给予卡牌关键词」那一步直接调这张牌的方法 |

同一张牌在同一个时机上有好几条效果时，它们会合并进**同一个** override、按列表顺序依次执行。

- 只能用在**普通卡牌**上（遗物 / 药水 / 自定义状态没有「这张牌被消耗」这一说；诅咒的效果写在
  「回合结束时在手上」，也挂不上这些钩子），校验器会拦；
- 这些钩子里**没有打出记录**，所以「卡牌时机」的效果里不能用「打出的目标」（请改用自己 / 全体敌人 / 随机敌人）、
  不能用 X 值、也不能用「伙伴已经在场」类效果（纯「召唤伙伴」可以），校验器都会报错说清楚；
- 勾了「条件不满足时这张牌打不出去」对它没用（它不是打出的条件），效果只会在那个时机触发，牌本身照常能打出；
- 卡面描述写成「当这张牌被消耗时，…」这种条件句；生成代码里每个时机写一行 `CET:CardTiming=` 标记，
  「从工程恢复存档」按它原样认回子时机。

### 条件填负数 = 判断反过来

「至少 / 至多 N」这类条件，**数值填负数就是反过来判断**（数值本身按绝对值用）：

| 填的 | 实际判断 |
| --- | --- |
| 生命值低于 `-20` | 生命值**高于** 20% |
| 拥有某状态至少 `-2` 层 | 拥有某状态**少于** 2 层 |
| 拥有额外资源量至少 `-2` 点 | 拥有的额外资源量**少于** 2 点 |
| 本回合已打出至少 `-4` 张牌 | 本回合打出的牌**少于** 4 张 |
| 手牌数不少于 `-3` 张 | 手牌数**少于** 3 张 |
| 手牌数不多于 `-3` 张 | 手牌数**多于** 3 张 |

- 卡面描述、生成代码里的条件注释、界面上的悬停提示，全都按**反向后的说法**写（不会出现「生命值低于 -20%」这种东西）；
- 校验器会给一条**提示**（「填了负数 → 判断反过来：实际生效的是『生命值高于 20%』」），免得过几天忘了自己填过负数；
- 只有上面这几条支持反向。其它带数值的条件填负数会**警告**并按绝对值处理（不会悄悄反向）；
- 数值 `0` 既不是正数也不是负数，会生成「不低于 0」这种恒真条件，校验器也会提醒一句。

### 生命区间（生命值在 N%~M% 之间）

条件里选「生命值在 N%~M% 之间」后，**数值 N** 与**~ 数值 M（%）**两栏会一起出现（别的条件只显示数值 N）：

- 两个数都填百分比（含端点），顺序写反也没关系（`60` 和 `30` 会自动排成 `30~60`）；
- **任意一个填负数 = 判断成「区间外」**：`25` / `75` → 生命在 25%~75% 之间；`-30` / `60` → 生命低于 30% **或**高于 60%；
- 「指向对象」照常可用（自己 / 指定敌人 / 任意一个敌人 / 全部敌人）；
- 描述写「生命值在 30%~60% 之外（区间外）」，校验器也会提示一句；第二个数填 0 时按「下界 0%」处理。

### 抽牌堆被打乱洗过牌

本体**没有**任何「洗过牌」的可查询状态（没有事件、没有战斗历史、没有计数器）——
先古遗物「大～抱抱」自己是靠 `override AfterShuffle(choiceContext, shuffler)` 在洗牌那一刻反应的。
所以这里分两种给：

- **条件**「抽牌堆本回合被打乱洗过牌」/「抽牌堆本场战斗被打乱洗过牌」：工具会生成一个
  `<角色>ForgeShuffleTracker` 记录器 + 打在 `Hook.AfterShuffle` 上的 Harmony 后缀补丁，
  记下「谁、哪一回合、洗了几次」，于是任何地方都能查询（卡牌打不出去判定、遗物 / 状态效果门控……）；
- **遗物触发时机**「抽牌堆打乱洗牌时」：直接覆写 `AfterShuffle`，**在洗牌那一刻**触发
  （和大～抱抱一模一样；生成的效果体里会先判 `if (shuffler != base.Owner) return;`，只看自己洗的牌）。

两个共同注意点（都是本体机制）：**开局的洗牌不算**（开战那次走的是 `ModifyShuffleOrder`，不触发 `AfterShuffle`），
战斗结束 / 收尾阶段也不算。「本场战斗洗过」按这一场战斗的 `ICombatState` 记，换一场战斗自动重新算。
只有真的用到这两条条件时才会生成那个记录器文件。

## 数值填 0 的效果：不显示、也不执行

「造成伤害 / 获得格挡 / 回复生命 / 失去生命 / 获得最大生命 / 抽牌 / 获得能量 / 获得金币 /
额外资源量 / 施加增益减益 / 升级卡牌 / 预见」这些效果，**数值填 0 就是「这条效果不存在」**：

- 卡面描述里**不写这一条**；
- 生成的代码里**整条不执行** —— 不能只写个 0 摆着，因为本体算伤害 / 格挡时要过力量 / 敏捷
  （0 点伤害 + 3 点力量 = 3 点伤害、0 点格挡 + 2 点敏捷 = 2 点格挡）；
- 想让这条效果**升级后**才出现：数值填 `0`、**升级增量**填上（例如 `6`）——
  没升级时照样不显示也不执行，升级之后才出现（描述用本体 `{IfUpgraded:show:…}` 的写法，
  升级预览里显示成绿色；代码里包一层 `if (base.IsUpgraded)`）；
- 数值 0 的效果也**不会**让这张牌变成「需要选目标」的攻击牌（不然会出现一张打出去什么都不做的攻击牌）；
- 不想用这条效果就直接删掉。真正需要「0」的判定（例如「本回合还没受到伤害」）请用**条件选项**。

生成前的校验会给一条提示说清这条效果是「整条不生效」还是「升级后才生效」。

（注意：**召唤伙伴**填 0 是「用『召唤物』页配置的血量」、**大限已至**的数值来自目标的灾厄层数、
三个「按生命值算」的伙伴攻击 / 牺牲伙伴填 0 只是计算式里的固定加值 —— 这些的 0 都有意义，不受这条规则影响。）

## 升级后的关键字

「卡牌」页中间**「升级后的关键字」**那一块：6 个关键字各一行，三态可选
（`不变 / 升级后获得 / 升级后失去`），生成到 `OnUpgrade()` 里的 `AddKeyword` / `RemoveKeyword`
（本体「残影」升级获得固有、「幻影 / 寒冰」升级失去虚无 / 消耗，就是这么写的）。
升级预览和实际升级后的关键字都由本体自己显示，不需要写本地化文本。

这一块**底下**还有「**自定义关键词：不变 / 升级后获得 / 升级后失去**」两栏（V0.2.136）——
那两栏**只影响显示**（自定义关键词是封闭枚举之外的显示等价物，游戏侧没有按它判定的逻辑），
详见上面「升级后获得 / 失去自定义关键词」。

> ⚠ V0.2.131 起，「升级后费用 / 升级后额外资源量费用」**搬到了上面费用栏下面那一行**
> （原来在右边的效果编辑器顶部，用户说和中间重复了）—— 见下面「卡牌费用 / 额外资源量费用」。

## 卡牌费用 / 额外资源量费用 / 升级后那两栏（V0.2.131）

用户要求（《卡牌页优化.md》中间②）：去掉「X 费用 / 额外资源量费用为 X / 升级后 X+1」三个勾选框，
改成**在费用框里写式子**，并在费用框同侧右边加「额外资源量费用」框、在它们下方加
「升级后费用 / 升级后额外资源量费用」。用户拍板 `Q3 = A`：**两者都要** ——
`A+3` 这类**生成期折叠成常量**（`A` 必须来自「固定数值」参数）＋ `X` **沿用本体那套**。

| 栏位 | 写什么 | 生成成什么 |
|---|---|---|
| **费用**（消耗能量） | `2` / `1+1` / `A+3`（A 是「数值（固定数字）」参数）/ `X` | `base(N, …)`；写 `X` → `HasEnergyCostX => true` + 构造函数写 0 + `ResolveEnergyXValue()` |
| **额外资源量费用**（费用框右边） | `2` / `1+1` / `Y`；**留空 = 老规矩**（按效果里的负数「花费额外资源量」算） | `CanonicalStarCost => N`；写 `Y` → `HasStarCostX => true` |
| **升级后费用**（下一行） | 留空 / `0` / `1` / `2`（绝对费用）/ `1+1` 这类常量式子 / `X`（= 升级后还是 X，等于不改）/ `X+1` | `OnUpgrade()` 里的 `base.EnergyCost.UpgradeBy(差值)`；写 `X+N` → `x = ResolveEnergyXValue() + (base.IsUpgraded ? N : 0)`（原来「升级后 X +1」勾选框的等价写法） |
| **升级后额外资源量费用** | 留空 / 绝对数（或常量式子）/ `Y`（= 升级后还是 Y，等于不改） | 填数字 → `OnUpgrade()` 里的 `UpgradeStarCostBy(差值)`（本体依据：`sts2.dll` 里 `CardModel` 有 `protected void UpgradeStarCostBy(int addend)`（和 `OnUpgrade()` 同一个可见性），另有 `BaseStarCost` / `CurrentStarCost` / `WasStarCostJustUpgraded` 那套机制）；填 `Y` → 什么都不生成（本体改不了 X 那一档）；**`Y+N` 做不了**：本体 `UpgradeStarCostBy` 碰到 `HasStarCostX` 的牌会**直接抛异常**，所以那一档由配置检查拦下来说清 |

> **V0.2.148**（用户报的「升级后费用里面填不了 Y 和 X，但是填过之后什么都没有也会报错」）：
> ① 「升级后额外资源量费用」那一栏以前**只显示绝对数**（填了 `Y` / 填错时绝对数是 null → 框子显示成空，
> 看着像「填不进去」），而且**清空时没清掉留存的原文**（`UpgradeStarCostFormula`）→ 校验器那句「看不懂」
> 永远消不掉。现在 getter 认原文、清空/填数字都把原文一起清掉。
> ② `Y`（= 升级后还是 Y）从「报错」改成**合法**（等价于留空，生成端本来就不写任何东西）；
> `Y+N` 仍然是错误但换成了说清原因的文案（本体 `UpgradeStarCostBy` 对资源量 X 的牌直接抛异常）。
> ③ 把 `Y` 填进「升级后费用」（能量那一栏）时，报错里会提示「这一栏是能量费用，Y 请填右边那一栏」。

- **本体 `base(cost, …)` 要的是编译期常量**，所以费用栏只收「数字 / 生成期算得出来的式子 / `X`」；
  读血量、读状态那种**运行时表达式会被「配置检查」明确报错拦住**（说清是哪一张牌的哪一栏）——
  不是静默忽略。
- **老字段一个都没删**：`CostIsX` / `StarCostIsX` / `XPlusOnUpgrade` 照旧读得回来、照旧生成，
  所以老存档的生成文本**逐字节不变**（红线①，实测见 `docs\QUEUE.md` 第 18 批）。
  界面上那三个勾选框没了，改由 `CostText` / `StarCostText` / `UpgradeCostText` 三个文本属性代理
  （写 `X` / `Y` / `X+1` 就落回老字段；写纯数字落回 `Cost` / `UpgradeCost`）。
- **回读（`--recover`）**：`HasEnergyCostX` / `HasStarCostX` 照旧认；「升级后 X + N」按
  `ResolveEnergyXValue() + (base.IsUpgraded ? N : 0)` 或公式序幕那句注释
  `// X = …（升级后 X + N）`认；「额外资源量费用」框写过的原文写成 `// CET:StarCostFormula=`，
  回读按它还原（不然旧口径会凭空补一条负数效果，生成结果就变了）。
- 顺带修正：以前「升级后 X +1」只对**老写法**（勾「数值 = X」）生效，
  对「数值（公式）」里写的 `X` 不生效（`__X` 没加那个 +1）——现在两边一起加。

## 效果的作用对象（自己 / 敌人）

**获得格挡、回复生命、失去生命、获得最大生命** 这四种效果也能作用在敌人身上（卡牌 / 遗物 / 药水都支持）：

- 作用对象选 `自己 / 指定敌人 / 所有敌人 / 随机 N 个敌人`，卡牌里「指定敌人」= 玩家选中的目标，
  遗物 / 药水没有「这张牌打的目标」，会退回「可打的第一个敌人」；
- 卡面描述自动写清给谁：`让所有敌人获得 6 点格挡。` / `让指定敌人回复 5 点生命。`；
- 这类效果会让整张牌变成需要选目标的牌（本体 `TargetType.AnyEnemy`），选不到目标时不报错、直接跳过。

## 额外资源量的升级增量

卡牌上「获得 N 点额外资源量」的数值支持**升级增量**：生成时用本体的 `StarsVar` 声明变量
（`new StarsVar(2)`），打出时读 `base.DynamicVars.Stars.BaseValue`，升级增量写进 `OnUpgrade()` 的
`UpgradeValueBy`，卡面描述用本体的 `{Stars:diff()}` 占位符 —— 升级后由本体自己算，
所以卡面会显示升级后的数字（本体「星尘」这类牌就是这么写的）。

### 那只载体遗物：在「额外资源量」页能看见它，也能勾「在游戏中隐藏」

「额外资源量」启用之后，工具会自动生成一只**载体遗物**（`<角色>ExtraResourceRelic`，
稀有度 `Starter`）——它只负责「每场战斗开始时发资源量」和「记录跨战斗继承」，不是藏品。
它必须在遗物池里（本体 `RelicModel.Pool` 是 `First(...)` 查找，找不到会在选人界面直接报错），
但它是 `Starter` 稀有度，奖励只会抽 Common / Uncommon / Rare / Shop，所以**不会被当成奖励发出来**。

- **在「额外资源量」页看得见**（V0.2.127 起；V0.2.124 搬过来之前它在遗物页左上角）：
  那一页左栏底部那块「额外资源量遗物（自动生成的隐藏起始遗物）」里会写明它的类名 + 稀有度，
  并注明它**不在遗物列表里**（那张表只列你自己配的遗物）；
- **它挂的效果在那一页右栏的「效果栏」**（V0.2.127 新增，用户要求「在右侧增加效果栏」；
  V0.2.144 起**版式和卡牌页一样**：上面一行摘要列表，下面一整块共享编辑区点一条就编辑它，
  这一条效果自己的「条件」也在编辑区里），
  触发时机固定是「战斗开始时」。**只有这一份效果数据**（`Profile.ExtraResource.Effects`），
  里面没有第二份镜像；
- **「在游戏中隐藏（不显示在遗物栏里）」勾选框**（V0.2.113 新增，V0.2.112 及以前它一直占着遗物栏一格）：
  勾上之后工具会多生成一个 `cs/HideCarrierRelic.cs` —— 给本体的
  `NRelicInventory.Add`（遗物栏就是遍历 `Player.Relics` 逐个 `Add` 建的，本体没有可见性判断，
  `RelicModel` 上也没有「别显示我」的虚属性可以覆写）打一个**后缀补丁**：
  **原方法一个字都不改地照跑**（节点照建、照插进遗物栏的节点列表 `_relicNodes`、照进 Godot 树），
  跑完之后才把这个节点设成 `Visible = false`（**纯视觉隐藏**）。**文件里只有这一个补丁类。**
  「新获得遗物」的飞入动画**不用**单独挡：它只 tween 节点里 icon 的透明度 / 位置 / 缩放，**不碰 `Visible`**，
  父节点不可见 ⇒ 整棵子树都不渲染，动画自己在不可见节点上跑完、没有任何可见效果
  （挂在 `AnimateRelic` 上的**后缀**里 `return` 只是结束它自己 —— 原方法早跑完了，拦不住也不用拦）。
  遗物本身照旧挂在角色身上 → **发放资源量 / 跨战斗继承一个字都不变**，存档也兼容；
- ⚠ **V0.2.113~V0.2.137 这里打的是「前缀补丁」**（遇到这只遗物就 `return false` **跳过原方法**）：
  那会让遗物栏的节点列表比 `Player.Relics` **少一项**，之后再获得任何遗物时，
  本体 `OnRelicObtained` 按模型算出来的 `index` 插进这个短列表就**越界**
  （`List.Insert` 抛 `ArgumentOutOfRangeException`，异步任务直接死掉 —— 从玩家视角就是**卡死**，
  实测命中的是「先古事件选遗物」）。**V0.2.138 起改成后缀 + 纯视觉隐藏，绝不再跳过原方法**；
  这条不变量由「生成端 + `--uicheck` 回归钉 + 校验器」三处一起保：任何情况下遗物栏节点列表的
  长度与顺序都必须和 `Player.Relics` 一致。
- 默认**不勾**（和以前一样看得见），改完要重新「生成」才生效。
- 页头名字：V0.2.127 起叫「**额外资源量**」（以前叫「额外资源量/状态」）。
- **已知老坑（V0.2.127 修掉）**：这只遗物的效果链以前生成出来的 `cs/ExtraResource.cs`
  **是编译不过的** —— `CanonicalVars =>` 后面多开了一层 `{` 而且从没关上，
  `BeforeSideTurnStart` 被塞进了 `CanonicalVars` 的块里；另外勾「每场战斗只触发一次」时
  用到的 `_condUsedThisCombat` 字段压根没声明。两处都只在「启用额外资源量 + 有至少 1 条效果」
  时触发（示例存档的额外资源量是关的，所以一直没暴露）。

### 「显示名」会用在哪些地方

在「额外资源量」页填的**显示名**（例：`冰附魔`）会出现在这些文本里，不会再有第二个叫法：

| 位置 | 例子 |
|---|---|
| 计数器下方 / 鼠标悬停提示 | `冰附魔`（覆盖本体的「辉星」文案） |
| 悬停提示里的**描述** | 留空 = 工具自动写（`冰附魔：每场战斗开始时获得 3 点…`）；「额外资源量」页新增了**自定义描述**输入框，填了就原样用它（上传了图标会在前面自动补 24×24 内联小图，除非你自己写了 `[img]`） |
| 系统提示「我没有足够的**辉星**。」 | 本体那句来自**全局**键 `combat_messages.NOT_ENOUGH_STARS`（写死了「辉星」）。本版起改成写**我们自己的键** `<角色>_EXTRA_RESOURCE.notEnough`（内容 = 「我没有足够的[red]你的资源名[/red]。」）+ 一个只在玩我方角色时替换它的补丁（目标是本体 `internal` 的 `UnplayableReasonExtensions.GetPlayerDialogueLine`，**手动打**、打不上只丢这一句提示），所以付不起资源费时的思考气泡里也是你的资源名 |
| 卡面效果描述 | `获得 {Stars:diff()} 点冰附魔。` / `需要 1 点冰附魔。` |
| **条件说明** | `拥有冰附魔至少 4 点` |
| 状态触发时机的说明 | `花掉冰附魔后：…` |
| 那个隐藏起始遗物的名字 / flavor | `冰附魔` |

没填显示名时全部退回通用说法「额外资源量」。界面里的条件短名（效果列表那一行）也跟着显示名走。
只要启用了额外资源量，计数器与卡面上悬停提示的标题和描述都会替换成我们自己的（不会露出本体别的角色的文案）。

## 遗物页 / 药水页 / 卡牌三页的左侧序号、$5$ 个一块的空槽、拖动换位置、自动类名跟序号走

V0.2.124/125 在遗物页做了这一套，V0.2.127 按用户要求（《遗物、药水优化.md》第 6 条
「药水方面参考遗物的优化方案」）**照抄到药水页**，V0.2.140 又照抄到**三个卡牌列表页**
（卡牌 / 诅咒卡 / 先古卡，见下一节）—— 现在是同一套版式：

| 做什么 | 口径 |
|---|---|
| 最左边那一列序号 | 是这一条**现在排第几位**（1..N 连续），**不跟着条目走**；拖动 / 增删之后重排 |
| 一次多留几个序号 | 显示的**行数** = `5 × ceil(max(N,1)/5)`：3 条 → 1~5（4、5 是灰空槽）；6 条 → 1~10；11 条 → 1~15 |
| 空槽 | 只显示灰色序号（`#AAAAAA`，真条目是 `#FF1F4E79`），**选不中**（不污染选中 / 多选 / 全选 / 删除 / 生成），但**可以当拖放落点** |
| 拖动换位置 | 按住行里的文字上下拖过系统阈值才起拖（不然点一下选中会被当成拖动）；松手插到鼠标下面那一行的位置 |
| 唯一事实来源 | `Profile.Relics` / `Profile.Potions` 里**永远只有真条目** —— 空槽只活在界面镜像集合（`MainWindow.RelicRows` / `MainWindow.PotionRows`）里，生成 / 存档 / 回读 / 校验都看不到它 |
| 自动类名跟序号走 | 生成前跑 `Naming.RenumberRelicClassNames` / `RenumberPotionClassNames`：**手填的一个字不动**；写着「本工具以前自动生成过的名字」（`<角色类名>Relic<N>` / `<角色类名>Potion<N>`，按**名字形状**认）的按当前序号改名；**留空的保持留空**只记序号（一写实就再也分不出手打的和工具生成的）。**幂等** |
| 稀有度 | 界面显示中文（初始 / 常见 / 罕见 / 稀有 / 先古 / 商店；药水只有 常见 / 罕见 / 稀有 —— 本体 `PotionRarity` 里还有 `Event` / `Token`，那是本体内部的档，工具从来没给过），**存进存档的仍是英文 id**，老存档读得回来 |

两个实测踩到的坑（代码里都有注释）：

1. 界面镜像集合必须是 **`public`** —— WPF 的 `PropertyPath` 走反射读不到 `internal` 成员，
   绑 internal 字段时绑定状态直接是 `PathError`、`ItemsSource` 永远是 null；
2. `Rows.Clear()` 会触发 `SelectionChanged → SyncDetail → 重新铺集合` 的**重入** ——
   镜像铺两遍会各自按自己那份快照写，差一行；所以重入只置 dirty，由最外层循环重算到稳定。
   另外药水列表的 `SelectionChanged` 在 `_inSyncPotionRows` 期间**不再递归进 `SyncDetail`**
   （那一刻遗物页的镜像还没铺完，递归会把效果列表的数据源绑到上一份存档的遗物上）。

> 换存档（`Raise("Profile")`）会在通知里把两个镜像重铺一遍 —— 有几处历史自检是直接给
> `_profile` 字段赋值再 `Raise("Profile")` 的（不走属性 setter），只靠 setter 里的重铺铺不到。

## 药水页照抄遗物页：抄了什么、什么没抄

- **抄了**：左侧序号 + 每 5 个预留空槽 + 拖动换位置 + 自动英文类名跟序号走 +
  稀有度英文改中文 + 右侧**效果栏**（每条效果直接在列表行里编辑，右下角选「单条效果自己的条件」）+
  自定义描述（这一栏本来就有）。
- **没抄（药水没有这个概念，硬塞会误导）**：遗物的「触发时机列表 / 关系开关（满足其中一个 /
  全部满足）/ 重置范围」一律没有；「范围界定 / 关键词」这类药水用不到的效果模块也不会无中生有。
- **顺手补的两处老缺口（都不许静默）**：
  - 药水效果的**条件**以前是「界面没有入口 + 校验器一律报错（『药水不支持条件选项』）+ 生成端不生成」。
    V0.2.127 起真的支持了：新增 `CSharpCodeGen.CondCtx.Potion`（表达式和遗物同形：
    `base.Owner` 是 Player，模型上没有 `CombatState`，走 `Owner.Creature.CombatState`），
    `EmitPotionEffect` 会给带条件的那条效果包一层 `if`，并写一行
    `// 条件（只对「X」这条效果）：…` 供回读（回读靠块栈的既有规则，**没有新标记**）。
    ⚠ 老存档不可能带药水条件（校验器以前直接拦死），所以这条不会改到任何老存档的生成结果。
  - 药水的「**概率生效**」和「**命中次数**」是老缺口：界面一直有这两栏，但 `EmitPotionEffect`
    从来没为药水生成过掷骰 / 多段伤害 —— 勾了等于没勾、而且一句提示都没有。
    这一版**刻意不补生成**（补了会改掉老存档的生成结果，而「老存档生成结果逐字不变」是红线），
    改成**校验器明确报出来**（警告级）。想要「重复执行几遍」请用「生效次数」。

## 卡牌页的左侧序号、每 5 个一块的空槽、拖动换位置、自动类名按序号（V0.2.130；序号口径 V0.2.137；**空槽版式 V0.2.140**）

用户要求（《卡牌页优化.md》第 1 条：「左侧序号、可拖动位置、对应序号英文类名」），
拍板口径见 `docs\需求\已拍板决定.md` **Q5 = A**；
V0.2.137 用户又提「卡牌的那个排序有点问题，可以做的和遗物左侧的排序一样」→ **拍板 A**：
**每页列表各自 1..N**（和遗物 / 药水页完全一样），三页各用一套类名前缀。

**V0.2.140** 用户对前一版**再更正一次**（原话）：
> 「不是说卡牌左侧的序号做的像遗物一样，五个五个列，直接显示出来，比如只有一张卡时，有1-5序号，
> 当有6张卡时，出现6到10序号」

即**连版式都要和遗物 / 药水页一样**：序号直接显示、**每 5 个一块、不够一块的补灰空槽**。
范围（用户 2026-10-10 已确认「要」）：**三个卡牌列表页全做** —— 卡牌 / 诅咒卡 / 先古卡。

| 做什么 | 口径 |
|---|---|
| 最左边那一列序号 | 卡牌名最左侧从上到下；是这一张卡**现在排第几位**，**不跟着卡牌走**；拖动 / 增删之后重排 |
| 一次多留几个序号 | 显示的**行数** = `5 × ceil(max(N,1)/5)`（N = 该页真卡数）：1 张 → 1~5；5 张 → 1~5；**6 张 → 1~10**；11 张 → 1~15 |
| 空槽 | 只显示灰色序号（`#AAAAAA`，真卡是 `#FF1F4E79` **加粗**），卡名那一列整块收起来、ToolTip 为空；**选不中**（不污染选中 / 多选 / 全选 / 删除 / 生成），但**可以当拖放落点** |
| 序号按哪些行编号 | **每页列表各自 1..N，空槽不占号**：普通卡列表（含本体卡 / 初始的打击与防御，它们就在这个列表里、就是第 1、2 号）、诅咒卡列表、先古卡列表**各自**从 1 数。用户原话的验收场景：「面具虚影」排第三张 → 删掉英文类名后生成 `SparkleCard3` |
| 三套自动类名前缀 | 普通卡 `<角色类名>Card<N>`、诅咒卡 `<角色类名>Curse<N>`、先古卡 `<角色类名>Ancient<N>`（N = **它自己那一页**列表里的位置）。**必须分三套前缀**：三页各自从 1 开始，同一个前缀会撞类名 → 本体注册时 `DuplicateModelException`。状态卡住在「卡牌」列表里，用 `Card` 前缀 |
| 唯一事实来源 | `Profile.Cards` / `Curses` / `AncientCards` 各自仍是唯一事实来源 —— 空槽只活在**界面镜像集合**（`MainWindow.CardRows` / `CurseRows` / `AncientRows`）里，生成 / 存档 / 回读 / 校验都看不到它 |
| 三页共用一份实现 | 三个列表页只有「读哪个真集合」不同，所以镜像逻辑抽成 `SlotRowMirror<T>`（`src\Sts2CharForge.App\SlotRowMirror.cs`），三页各 new 一个实例；**不复制三份**（复制三份以后必然漂）。遗物 / 药水两页那一份**没动**（避免改既有行为） |
| 拖动换位置 | 按住行里的文字上下拖过系统阈值才起拖（不然点一下选中会被当成拖动）；松手插到鼠标下面那一行的位置（**拖到空槽 = 落到那一行的位置，超出末尾就夹到末尾**）；`MoveCardTo` 是纯逻辑，自检直接调它 |
| 自动类名跟序号走 | 生成前跑 `NameStabilizer.RenumberCardNames`（内部对**三个列表分别**跑 `Naming.RenumberCardClassNames`）：**手填的一个字不动**；写着「本工具以前自动生成过的名字」（`<角色类名>` + `Card` / `Curse` / `Ancient` + `<N>`，按**名字形状**认）的按当前位置改名；**留空的保持留空**只记序号（一写实就再也分不出手打的和工具生成的）。**幂等** |
| 界面提示 | 「英文类名」下面那行小字写清现在这张是「自动（跟序号走）」还是「你手填的」（**卡牌页 / 诅咒页 / 状态卡页都有这一行**）；卡牌页在留空时还会写出「普通卡 / 诅咒卡 / 先古卡各自的前缀」；列表每行的 ToolTip 写「第 N 位 ｜ 自动 / 手填 ｜ 可以拖动」 |

列表模板（三页同形，序号列 34px + 卡名列 `*`）：

```xml
<TextBlock x:Name="CardRowOrdinal" Grid.Column="0" Text="{Binding OrdinalText}" FontWeight="Bold"
           Foreground="#1F4E79" VerticalAlignment="Center" Margin="0,0,4,0"/>
<TextBlock x:Name="CardRowName" Grid.Column="1" Text="{Binding Display}" .../>
<DataTemplate.Triggers>
  <DataTrigger Binding="{Binding IsPlaceholder}" Value="True">
    <Setter TargetName="CardRowRoot" Property="ToolTip" Value="{x:Null}"/>
    <Setter TargetName="CardRowOrdinal" Property="Foreground" Value="#AAAAAA"/>
    <Setter TargetName="CardRowOrdinal" Property="FontWeight" Value="Normal"/>
    <Setter TargetName="CardRowName" Property="Visibility" Value="Collapsed"/>
  </DataTrigger>
</DataTemplate.Triggers>
```

**空槽绝不能被当成卡选中**，所以详情栏 / 显隐都不再绑 `SelectedItem`，改绑窗口上的三组属性
（照遗物页 V0.2.125 / 药水页 V0.2.127 的写法）：

| 页 | 详情栏 DataContext | 详情栏显隐（也管「请选择…」那句提示） |
|---|---|---|
| 卡牌 | `CardUiSelection` | `HasCardSelection` |
| 诅咒卡 | `CurseUiSelection` | `HasCurseSelection` |
| 先古卡 | `AncientUiSelection` | `HasAncientSelection` |

语义：点中空槽行时 **`UiSelection` 保持上一张真卡**（右侧详情不清空），但**显隐判据看的是「列表里有没有选中一行」**
—— 真的取消选中（`SelectedIndex = -1` / 点列表空白）才回到「请选择卡牌」。
`UiSelection` 只可能是真卡（空槽被 `is CardSpec` 天然滤掉），三个镜像属性都**必须 public**
（WPF 的 `PropertyPath` 走反射读不到 `internal` 成员；遗物页实测踩过这个坑）。

同源的一条副作用口径（`CardList.SelectedItem` 现在可能是占位项，只读它算出来的东西会错）：

| 属性 | 旧写法（点空槽时会错） | 现写法 |
|---|---|---|
| `CardEffectsAreForged` / `CardCoreFieldsVisible` | `(CardList?.SelectedItem as CardSpec)?.IsForged` | `CardUiSelection?.IsForged == true` |
| `ForgeEnergyCostText` | `CardList?.SelectedItem is not CardSpec c` | `CardUiSelection is not CardSpec c` |
| `SpecialCard`（先古卡 / 状态卡共用详情） | `AncientCardList?.SelectedItem as CardSpec` | `AncientUiSelection ?? (StatusCardList?.SelectedItem as CardSpec)` |
| `SpecialCardOf()`（三栏「当前那张牌」） | 三个 `SelectedItem as CardSpec` | `CurseUiSelection ?? AncientUiSelection ?? (StatusCardList.SelectedItem as CardSpec)` |

> 不修的话后果是**用户可见的错乱**：点一下空槽 → 先古卡那栏右侧整块详情塌掉；
> 或者看着是锻造卡、右侧效果编辑器却被解锁了。

两个实测踩到的坑（和遗物 / 药水页同一对，代码里都有注释）：

1. 镜像集合必须能经 **`public`** 路径绑到（`CardRows` / `CurseRows` / `AncientRows`）——
   绑 `internal` 字段时绑定状态直接是 `PathError`、`ItemsSource` 永远是 null；
2. `Rows.Clear()` 会触发 `SelectionChanged → SyncDetail → 重新铺集合` 的**重入** ——
   镜像铺两遍会各自按自己那份快照写，差一行；所以重入只置 dirty，由最外层循环重算到稳定。
   并且卡牌页的 `SelectionChanged` 在镜像**正在铺**的时候**不再递归进 `SyncDetail`**。
   换存档还有一条自愈：有几处历史代码直接给 `Profile` 字段赋值（不走属性 setter），
   `CollectionChanged` 会留在上一份存档上 —— 镜像发现「挂的存档不是当前这份」就重挂一次。

**生成端零改动**：`SlotRowMirror` / `SlotPlaceholder` 只被界面用到，`Generation\*.cs` 一处都没引用；
`CardSpec` 新增的 `IsPlaceholder => false` 是 `[JsonIgnore]`，存档面零变化。

⚠ **V0.2.130 那一版**改的是「只数自有卡 → 全部行」（打击 / 防御也开始占号）；
⚠ **V0.2.137 这一版**改的是「全部卡一条连续编号 → 每页各自 1..N + 三套前缀」——
两版都**会改掉既有存档里「工具自动生成的」卡牌类名**（手填的绝不动、没填的仍按新序号拼）。
改号会连带改：

- `.cs` 文件名 / 类名声明 / 卡池引用 / 本地化键 / 卡面素材文件名（`images/packed/card_portraits/...`）；
- **别的模型里按类名存的卡牌引用**：效果里的「目标卡」（`EffectSpec.SpawnCardId`）、
  「转化为先古卡」的类名清单、锻造图【效果】模块的「目标卡」字段。
- 用户**上传的卡面**（`ArtSpec.CardPortraits` 是按类名存的键）也会跟着搬，否则会静悄悄退回占位图。

最后那一类是**卡牌独有**的坑（遗物 / 药水没人按类名引用它们）：改号之后那些引用会指向
**同名的新主人**或干脆不存在。所以 `NameStabilizer.RenumberCardNames` 会在同一次里把引用一起改过来。
**不能**只靠「上一次构建的类名表」（`NameStabilizer.RepairReferences`）：CLI（`--build`）连着跑两次时
存档一个字节都没变（用户没保存），而那张表已经翻到新一轮了 —— 实测会出现「第一次生成对、
第二次生成引用跑到别的卡身上」（不幂等）。改成同一次改掉之后，「加载 → 生成」连跑多少次结果都一样。

**老存档里勾过「卡面显示牌堆张数」的**：那个功能 V0.2.130 按用户要求**整块删掉**了
（勾选框 + 生成端 3 个 `ForgePileCountVar` 声明 + 辅助类 `cs/ForgePileCountVar.cs` + 占位符表
`PileCountHints` + 「填入参数…」菜单里那三个占位符与提示 + `--uicheck` 里那 9 条断言）。
老存档里那个字段（`"ShowPileCounts": true`）读进来会被 `System.Text.Json` **直接忽略**：
不报错、不会拦住加载，再次保存时字段自然消失。老存档自定义描述里已经写下的
`{DrawPile}` / `{DiscardPile}` / `{ExhaustPile}` 会原样留在文本里（本体运行时替换不了它们，
卡面上就按字面显示）—— 要清理请到「自定义描述」里手动删掉那三个占位符。

## 从工程恢复（`--recover`）补上的四项

V0.2.127 的探针发现这四项以前**完全没回读**，从工程恢复出来的存档会静默丢（或变回默认值）：

| 配置项 | 回读依据（既有规则，**没有新标记**） |
|---|---|
| 「在游戏中隐藏」（额外资源量载体遗物） | `cs/HideCarrierRelic.cs` **文件存在 = 勾着**（那个补丁只有这一个用途） |
| 「始终显示计数器」 | 角色类（`cs/<角色类名>.cs`）里有 `ShouldAlwaysShowStarCounter => true` |
| 「数量上限」 | `cs/ExtraResourceUi.cs` 里的 `internal const int MaxAmount = N;`（填了上限就一定会生成这个文件） |
| 药水的「加入药水池」 | `<角色>PotionPool.GetUnlockedPotions()` 体里出现 `ModelDb.Potion<X>()` = 这瓶**没勾**（生成端把全部药水列进 `GenerateAllPotions()`，没勾的在 `GetUnlockedPotions` 里 `RemoveAll` 掉） |

另外「自定义描述」也补了回读：它写在静态悬停文案表的
`<CharEntry>_EXTRA_RESOURCE.description` 里，回读时先剥掉生成时补的 `[img]…[/img]`
再和 `LocalizationGen.ExtraResourceAutoDescription`（自动写的那段）比 —— **一样就是「没填」**，
不然每恢复一次就长出一段自己写的文字。

## 从工程恢复（`--recover`）补上的三处（V0.2.134）

做「锻造节点图能从工程恢复」那一版时，端到端往返（生成 → 恢复 → 再生成 → 逐文件对拍）把这三处
**回读不到、又不报错**的地方暴露出来了。它们都不是这一版引入的，但都会让「恢复出来的存档再生成一次」
跟第一次不一样，所以一并补上：

| 配置项 | 回读依据 | 不补会怎样 |
|---|---|---|
| 作者 / 版本 / 模组说明 | `mod_manifest.json` 里的 `author` / `version` / `description`（本来就写在里面，只是没读） | 恢复后这三样变成空串，再生成的 `mod_manifest.json` 跟第一次不同 |
| 角色「说明」（那一栏留空时） | 本地化表里那**句兜底文案** `LocalizationGen.DefaultCharacterDescription`（= 「一个自定义角色。」）**且**清单里 `description` 是空的 → 当「没填」 | 空说明会**无中生有**变成那句话，`mod_manifest.json` 跟着变 |
| 锻造图的**端口版本** `ForgeGraphPortVersion` | 认到 `// CET:ForgeGraph=` 标记时置成本版的端口版本（`ForgeGraphGen.PortVersion`）；用旧存档当模板时也照抄模板那份 | 恢复出来的存档版本是 0，**下一次一打开就被 `MigrateIfPorts` 当成老存档再迁一次** —— 【if】的「是 / 否」两支会挪位（实测：6 张图各丢一条线、换一条线） |

> 「组」（`CardGroups` / 卡牌的 `GroupIds`）**仍然不能从工程恢复**：工程里只有「范围限定 = 组：`<id>`」
> 那一处痕迹，组名和「哪张牌属于哪个组」只存在存档里。后果是恢复出来的存档里那个组是空的，
> 如果还有效果引用它，**下次生成会被校验器拦下并说清**（这是有意的：与其按猜出来的成员生成，不如让你去补一下）。
> 用旧存档当模板（界面上的「从工程恢复存档…」会自动带当前存档当模板）时组名会跟着带过来。

## 状态的「数值 = 本状态的层数」和本体那些「显示数字不是层数」的状态

两件很容易踩的事，工具都会在生成前的校验里直接说清楚：

1. **勾了「数值 = 本状态的层数」，「数值」里填的数字就不生效了** ——
   生成的是 `base.Amount`（= 这个状态当前的层数）。所以「数值」填 30、勾上那个勾，
   游戏里得到的是「层数」而不是 30。想要固定数值就把勾去掉。
2. **本体有些状态栏显示的数字根本不是层数**（它们自己 override 了 `PowerModel.DisplayAmount`）。
   最典型的是**「缓慢」**：它显示的是 `SlowAmount × 10`，而 `SlowAmount` 是本回合「每打出一张牌 +1」
   （每张牌让该敌人本回合受到的攻击伤害 +10%），**跟施加的层数毫无关系**；
   本体自己施加时也只施加 1 层（`BygoneEffigy`：`PowerCmd.Apply<SlowPower>(…, 1m, …)`）。
   所以「施加 30 层缓慢」在游戏里永远不会显示 30 —— 层数照旧记着，只是状态栏那个数字由它自己算。

   同一类（会提示的）状态还有：`SlothPower`、`TenderPower`、`PaleBlueDotPower`、`AutomationPower`、
   `CacophonyPower`、`PanachePower`、`WitheringPresencePower`、`MonologuePower`、`FeralPower`、
   `HardenedShellPower`、`OrbitPower`、`TagTeamPower`（清单是把本机解包工程里
   `override int DisplayAmount` 的 Power 全扫一遍得来的）。
3. 状态的自动描述里会写出条件（`条件：拥有冰附魔至少 4 点，不满足时这条效果不生效`）。

### 想直接给敌人上「受到伤害 +30%」？用「直接把「缓慢」设成 N%」

「缓慢」这条效果没法靠层数做到，但可以直接改它的内部数值。做法：效果选「施加增益/减益」→ 状态选**缓慢**，
下面会多出一行 **「直接把「缓慢」设成 ___ %」**（填 30 就是「敌人当场受到伤害 +30%」，0 = 不设置）。

- 生成出来是这样的（和本体 `BygoneEffigy` 施加缓慢的写法一致：只施加 1 层，再写内部数值）：

  ```csharp
  await PowerCmd.Apply<SlowPower>(choiceContext, cardPlay.Target, 1m, base.Owner.Creature, this);
  SlowPowerHelper.SetPercent(cardPlay.Target, 30);   // 本体「缓慢」：直接设成 30%
  ```

- `cs/SlowPowerHelper.cs`（只在用到时才生成）里把 `SlowAmount` 写成 3（= 30%）、
  `DisplayAmount` 写成 30，再用反射调一下本体 protected 的 `InvokeDisplayAmountChanged()` 让状态栏立刻刷新；
  拿不到状态、或本体将来改了字段名时只记一条日志，不会把战斗搞崩。
- 描述会自动写成「施加冻伤（受到伤害 +30%）」（状态名跟着你在「本体状态改写」里改的名字走），
  层数不再出现在描述里 —— 反正它对「缓慢」没有作用，生成时按本体的做法只施加 1 层。
- 按 10% 一档折算（本体内部是整数档位）：填 25% 会按 30% 处理，校验里会提示。
- 仍然是本体机制：**每次敌人回合开始会清零**。
- 多人联机下不保证同步（本体的缓慢是靠「每打出一张牌 +10%」算出来的，这里是直接把内部数值写进去）。

## 自定义状态「覆盖血条」（每条状态各一条、颜色独立、可选左/右）

「自定义状态」页那个状态有**两栏**：

- **覆盖血条颜色**（六位 RRGGBB，留空 = 不覆盖）；
- **从哪端开始覆盖**（填了颜色才出现）：`从右边开始`（默认，像本体中毒）/ `从左边开始`。

填了之后，身上有这个状态的生物（自己 / 敌人）血条上就会出现对应颜色的一截。
**每个状态各占一条、颜色各自独立** —— 身上同时有状态 A 和状态 B 时，两条**都能看见**。

- **几何**（两端都对应本体自己的画法，所以观感和本体一致）：
  - 从**右边**开始（默认）：从「当前血量的右端」往左量 **层数 / 最大生命 × 血条宽** 那么多，
    红条右端跟着往左退（= 本体中毒那一截的画法）；
  - 从**左边**开始：从血条最左端往右量同样宽度，红条左端跟着往右退；
  - 同一边有几条就**依次往里堆**（互不重叠），**中间剩下那一段就是红条**；
  - 两边加起来超过当前血量时，后面放不下的那几条**不画**（宁可少画，也不叠在一起糊成一片）；
  - 和本体中毒 / 灾厄共存：本体那两截先在，我们从它们内侧接着排（从左边的那条会让开灾厄）；
  - 无限血条（本体那两档）不插手。
- **为什么每条状态要各自一个节点**：本体血条上只有**一截**绿色（`%PoisonForeground`），
  直接改它的颜色的话两条状态只能显示一条（谁后写谁赢）。所以生成出来的补丁拿那一截当**模板**
  `Duplicate()` 出 N 份（每个状态一份、按状态类名命名、挂在同一个 Mask 里），各染各的色、各占各的位置。
  复制品会 `UniqueNameInOwner = false`，不会去抢 `%PoisonForeground` 那个名字的查找。
- **为什么颜色能随便填**：本体血条上红条 / 中毒 / 灾厄用的是**同一张纯白九宫格**
  （`images/ui/combat/health_bar_fill.png`），颜色全部来自节点自己的 `self_modulate`
  （红 `CC0C0C` / 中毒 `79C03C` / 灾厄是着色器渐变）—— 所以换色就是换一个数字，不用改贴图。
- 颜色写坏了（不是六位十六进制）生成前的检查会**报错拦住**；
  不填颜色 = **一个文件都不多、一行补丁都不打**（老存档生成的模组一个字符都不变）。

生成出来是这样（每个状态一段，各自的颜色和左右）：

```csharp
// cs/ForgeHpBarOverlay.cs —— Postfix 在本体 RefreshForeground 之后跑
HideOurs(mask);                                   // 上一次画的先全藏起来（状态没了就在这一步收尾）
float leftEnd = 0f, rightEnd = parentWidth + red.OffsetRight;
if (creature.GetPower<BurnPower>() is { } __hp0 && __hp0.Amount > 0m)
    Place(mask, …, "BurnPower", new Color("FF8800"), left: false, amount: (int)__hp0.Amount, ref leftEnd, ref rightEnd, ref anyLeft);
if (creature.GetPower<RotPower>() is { } __hp1 && __hp1.Amount > 0m)
    Place(mask, …, "RotPower",  new Color("3CB371"), left: true,  amount: (int)__hp1.Amount, ref leftEnd, ref rightEnd, ref anyLeft);
if (anyLeft) red.OffsetLeft = leftEnd;            // 红条 = 两边让完之后中间剩下的那一段
red.OffsetRight = rightEnd - parentWidth;
```

补丁是**手动**打的（`RefreshForeground` 是私有方法）：本体哪天改了这个方法名，
只丢这些覆盖条、记一条警告，**不会连累整个模组加载**。「从工程恢复存档」会把颜色和左右一起认回来
（状态类里有 `// CET:HpBarColor=RRGGBB CET:HpBarSide=Left` 标记）。

## 「全体敌人」+「对群数 / 命中次数」

卡牌页那条效果的「对群数 / 命中次数」在「**全体敌人**」这一档是「**一次打全体、每个敌人各挨 N 下**」：

```csharp
await DamageCmd.Attack(6m)
    .FromCard(this, cardPlay)
    .TargetingAllOpponents(base.CombatState)   // 一次打全体（本体的一次性群体命令）
    .WithHitCount(4)                           // 每个敌人挨 4 下
    .WithHitFx("vfx/vfx_attack_slash").Execute(choiceContext);
```

- 「**随机敌人**」那一档仍然是「挑中的那些敌人各挨 N 下」（`ForEach` + `Targeting(foe)` + `WithHitCount(N)`）；
- 想「打完一轮再来一轮」请用同一行的「**生效次数**」（整条效果重复 N 遍）；
- 填 **1** 时**不写** `WithHitCount`（生成的代码和以前逐字一致）。
- 以前卡牌页的「全体敌人」会把那个数字**整个忽略**（只打 1 下），现在照你填的打 ——
  这一处和锻造图【效果】上那栏的语义对齐了（见「修掉『全体敌人 + 命中次数』」那一节）。

## 作用对象 7 档 + 概率「是否独立」+ 卡牌效果编辑器重排（V0.2.132）

这一版做的是《卡牌页优化.md》**右侧④**里「作用对象扩档 / 概率独立 / UI 重排」这三块
（「下回合生效适配全部效果」按用户排期留给下一版）。

### 一、作用对象从 6 档扩到 7 档（**全生成器共用一张表**）

卡牌 / 遗物 / 药水 / 自定义状态 / 锻造【效果】模块五处的「作用对象」下拉**都是同一张表**，
现在按需求给的顺序排 7 档：

| 档位 | 中文 | 生成出来的目标 | 谁真的实现了 |
|---|---|---|---|
| `Self` | 自己 | `base.Owner.Creature` | 全部链 |
| `Ally` | 指定队友 | `cardPlay.Target`（本体 `TargetType.AnyAlly`） | **只有卡牌**（遗物 / 药水没有「玩家选中的目标」，校验器报错拦住） |
| `AllAllies` | 全部友方 | `CombatState.PlayerCreatures.Where(c => c.IsAlive && c != base.Owner.Creature)` | 全部链 |
| `Enemy` | 单体敌人 | 卡牌 = 选中的目标；遗物 / 药水 = 可打的第一个敌人 | 全部链 |
| `RandomEnemies` | 随机敌人 | 随机挑 N 个（N = 命中次数） | 全部链 |
| `AllEnemies` | 全部敌人 | `CombatState.HittableEnemies` | 全部链 |
| `AllUnits` | **全体单位（新）** | `CombatState.Creatures.Where(c => c.IsAlive)`（自己 + 队友 + 召唤物 + 敌人） | 伤害 / 格挡 / 回复生命 / 失去生命 / 最大生命 / 施加增益减益 / 临时增益 / 击晕 |

- 「**全体单位**」用的是本体 `CombatState.Creatures`（`CombatState.cs:71` = `_allies.Concat(_enemies)`）；
- **没实现这一档的效果种类**（抽牌 / 生成卡牌 / 消耗…）选了它「配置检查」会**报错拦住**并列出支持名单 ——
  不拦的话生成端会把它当成「对自己」（生成、编译、进游戏都不报错，最难查的一类问题）；
- 「**全部友方**」多了一个子选项「**不包括召唤物（只算玩家本体）**」：
  - **不勾（默认）= 老行为**：同一边的玩家单位全算（含召唤物），生成出来的过滤条件与 V0.2.131 **逐字节相同**；
  - 勾上 → 尾部补一句 `&& c.PetOwner is null`（本体 `Creature.PetOwner` 非空 = 那是召唤物）；
- **锻造节点图**：这一栏（新编辑器里改过的）映射到【对象】模块本来就有档位 ——
  「全体单位」= `ForgeFormulaRuntime.AllUnits(__fgPlayer)`，「全部友方（不含召唤物）」= 只剩自己；
- **回读**：生成时写一行 `// CET:TargetSide=AllUnits`（`AllAllies` 勾了不含召唤物时同一行再补 ` CET:AllyNoPets=1`），
  `--recover` 按它还原。老存档（6 档）不写新内容，生成文本一个字节不变。

### 二、概率：「不同效果的概率之间是否独立」（新开关，默认 = 老行为）

- **不勾（默认）**：同一张牌里勾了「概率生效」的效果**共用一次掷骰、各占一段区间**（互斥）——
  第一条 40% + 第二条 60% = 掷出 0~39.999 只跑第一条、40~99.999 只跑第二条。这是老版本的行为，一个字符都没变；
- **勾上**：这一条**自己独立掷一次**（`NextDouble() * 100.0 < 你的百分比`），不再参与区间划分 ——
  两条 50% 可能都中、也可能都不中；
- 开关长在**卡牌链**「概率」那一行里（需求原文就是「再其下为概率…并且可以选择不同效果的概率之间是否独立」）；
- 生成时写一行 `// CET:ChanceIndependent=1 CET:ChancePct=70`（**只在勾了非默认档时才写**），
  `--recover` 按它把「独立 + 百分比」认回来。⚠ 概率本身（`ChanceEnabled` / `ChancePercent`）以前就**不回读**，
  所以「独立档」的百分比是靠这一行标记带回来的；
- 写了「概率公式」的那一条**本来就是自己独立判一次**（V0.2.98 起的老规矩，不变）。

### 三、卡牌效果编辑器重排（只有卡牌链，其余四处外观与绑定一个字不变）

顺序改成需求给的那样：
**升级增量 → 效果种类 → 数值（可写 `A+3` / `X`）→ 作用对象 → 命中次数（可写公式）→ 生效次数（可写公式）→
概率（可写公式 + 「是否独立」）→ 下回合生效 → 其余按种类出现**。

- 「命中次数」「生效次数」「概率」这三格现在是**固定值框 + 公式框并排**（以前公式都在下面那个「次数公式」组里）；
- 下面那个组现在只剩「复制的份数 / 重放次数」两格（它们属于「其余按种类只留用得到的」那一档）；
- **遗物 / 药水 / 自定义状态 / 锻造链的编辑器外观与绑定没有变**：新摆的那几行对它们恒为收起
  （Auto 行高度 0），它们仍然用原来那两行控件 —— 对拍实测生成文本与 V0.2.131 逐字节一致。

### 三点五、按种类裁剪栏位（V0.2.150）

用户原文：「卡牌页右边的效果栏做的太差了…**造成伤害只需要**数值（公式）/ 升级增量 / 作用对象 /
命中次数（公式）/ 生效次数（公式）/ 概率（公式）/ 下回合生效 / 随机目标允许攻击同个敌人，
**其他的都可以隐藏掉**」。

以前有 5 行是**无条件显示**的（增益 / 减益、目标卡、生成到、选牌方式、随机目标允许重复），
加上「作用对象」对所有种类都摆着 —— 这就是「太差」的来源。现在**每一行都按种类决定**，
判据是**机械测量**出来的：

| 东西 | 在哪 | 干什么 |
|---|---|---|
| 允许清单 | `Core/Effects/EffectFieldPolicy.cs` | 唯一一份「哪种效果显示哪些栏」；**只写允许的**，新效果默认什么都不显示（漏更新会被自检抓住） |
| 测量器 | `Core/Effects/EffectFieldUsage.cs` | 65 种 × 3 条链 × 47 栏：把某一栏改成特征值 → 重新生成这条效果的代码 → 与基线逐字符比对；`// CET:` 标记行整行删掉（它只是回读记录，不算「读了」） |
| 命令行 | `forge --fieldmatrix "D:\out.tsv"` | 自己复现这张「哪一栏被读了」的矩阵 |

规矩是**显示的栏位生成端一定读、藏起来的生成端一定不读**，`--uicheck` 对 65 种 × 13 栏
（卡牌链逐条相等、遗物 / 药水链只许更宽）全量验一遍。这次测量还顺带发现三处
「生成端在读、界面却藏起来」（等于做不出来），一并摆出来了：「取几张」多 9 种、
「多选1」多 3 种、「选牌方式」多 3 种（三种全局牌组）。**锻造节点图不受影响**（那边显隐是它自己那套）。

⚠ **同一条规矩的下一半（V0.2.161）**：**藏起来的栏位，文案里也不许写它**。
用户实测报的「为什么抽卡、获得能量之类的效果种类的对象没了，但是显示的是对敌人」就是漏了这一半：
`EffectSpec._targetSide` 的**默认值就是 `"Enemy"`**，V0.2.150 把「作用对象」那一行按种类藏了，
可**列表行行尾**还印着「抽牌 1 **→ 单体敌人**」、**自定义状态描述**里还印着「抽 N 张牌**（对指定敌人）**」——
显示在说假话，用户自然以为坏了。现在两处都走**同一份判据** `EffectFieldPolicy.UsesTargetSide`：
- `EffectSpec.Display` 只在该判据为真（且不是宠物效果）时追加 `→ 谁`；
- `PowerTriggers.EffectText`（状态描述）同理，不再用「`TargetSide == "Self"` 就省略」那种局部写法。
自检是**全量双条件**：67 种效果逐个建一条 `TargetSide = "Enemy"` 的探针，断言
「行尾出现 `→ 单体敌人`」**当且仅当**「那一行会显示」。反向验证：把判据退回原样 → 3 条 FAIL
（`Draw(显示=True/应=False)`、`Energy`、`OverdraftEnergy`、`Gold`… 一长串种类又印出「→ 单体敌人」）。

⚠ **版式上也有一条铁律**（V0.2.151 血的教训）：效果栏第一列是 `Width="Auto"` 的**标签列**、
第二列才是 `*` 的输入列 —— **标签文字有多长，输入列就被挤掉多少**。
「从哪里选牌（消耗 / 变化 / 复制…；选「牌顶」= 抽牌堆最上面那张，不用挑）」这 34 个字曾经把标签列撑到
**570px、输入列剩 0px**（整列下拉变成点不动的细条，「生效次数（公式）」被挤出可视区）。
所以：**标签一律短**，长说明进 ToolTip；自检里实测那个两列 Grid（标签列 ≤ 360、输入列 ≥ 140）+
所有标签长度 ≤ 8 字，两条守卫钉着。
想肉眼核对版式：`Sts2CharForge.exe --uishot D:\out`（把 6 种代表性效果栏 + 两个先古编辑器渲染成 PNG，
不参与生成、不写存档）；要看整页：`--forgeshot`（锻造页）、`--roleshot`（角色页，V0.2.160 加的 ——
查「角色显示名被挤到窗口外面」就是靠它），都写到 exe 旁边。

⚠ **另一条版式铁律**（V0.2.160 的教训）：**Grid 里「自动换行的长文字」不能跨列**。
角色页那张 4 列表单（`Auto / 320 / Auto / 320`）里有 4 块跨越 3~4 列的长说明（调试日志那一大段、
初始卡组 / 召唤物的两段），WPF 会把它们的「想要的宽度」摊到被跨越的列上 ⇒ 两个固定 320 的输入列
被撑成**各 1366px**，表单右边跑到 3125（可视区右边才 1395），第 4 列「角色显示名」直接被裁掉
（外层 `ScrollViewer` 的横向滚动**默认是关的** ⇒ 看不见也点不到）。修法两条一起上：
① 输入列改成 `Width="*"` + `MinWidth`（按窗口伸缩，不再固定宽）；
② 长说明**搬出网格**（放到网格下面单独的 StackPanel 里），再怎么长也挤不到表单列。
自检里量三样：表单右边 ≤ 视口右边、「角色显示名」输入框右边 ≤ 视口右边、输入框宽 ≥ 140
（修之前实测 表单右边=3125 / 可视区右边=1395，两条 FAIL）。

### 四、「命中次数」和「对群数」

- 界面上**没有**单独的「对群数」栏位：从前就只有 `RepeatCount` 一个字段，
  它在「造成伤害」上是**命中次数**（多段攻击打几下）、在「随机敌人」上是**挑几个目标**；
- V0.2.131 已经把三个「= X」勾选框（数值 / 生效次数 / 命中次数·对群数）从界面撤掉，这一版核对：
  **界面上一处 `AmountIsX` / `TimesIsX` / `RepeatIsX` 的入口都没有了**（字段还在，老存档照旧读得回来、照旧生成）；
- 卡牌链上那一行的标题现在叫「**命中次数（公式）**」；其余四条链上的老标题「对群数/命中次数」
  **保持原样没动**（免得动到那四处的外观 —— 要不要一起改名等用户定）；
- 能力等价性：含「随机敌人 + 命中次数 2」的老存档，改动前后生成的 `cs/Cards/LegacyRandomEnemies.cs`
  **SHA256 完全相同**（`.TargetingRandomOpponents(...).WithHitCount(2)`）。

## 「下回合生效」适配全部效果（V0.2.133）

用户原文（`docs\需求\卡牌页优化.md` 右侧④）：「其下为是否下回合生效，**这个很重要，让这个适配所有的效果**」。
用户拍板（`docs\需求\已拍板决定.md` Q4=B + 第四节）：一次做齐，**那 8 种「以前勾了没生效」的从此真的生效**。

### 结论：65 种效果里 **57 种**支持「下回合生效」

| 分类 | 走哪条路 | 种类 |
|---|---|---|
| 本体现成机制（5 种） | 本体 `BlockNextTurnPower` / `DrawCardsNextTurnPower` / `EnergyNextTurnPower` / `StarNextTurnPower` ＋ 生成的 `&lt;角色&gt;ForgeDelayed&lt;状态&gt;` | 格挡 / 抽牌 / 能量 / 额外资源量 / 施加·扣除状态 |
| 生成类（7 种，V0.2.88~90 就有） | 延迟状态里调同一套发射器 | 生成卡牌 / 复制卡牌 / 变化卡牌 / 获得·变化·删除·升级牌组（全局） |
| **通用延迟分支（45 种，V0.2.133 新增）** | `<角色>ForgeDelayed<种类><状态/关键词><作用对象>`：下回合开始时跑**同一套发射器** | 造成伤害 / 回复生命 / 失去生命 / 最大生命 / 金币 / 击晕 / 翻倍层数 / 暴露 / 清除指定状态 / 清除全部减益 / 禁止格挡 / 禁止回复生命 / 强化卡牌 / 获得药水 / 消耗·丢弃·升级卡牌 / 放置卡牌 / 毒性爆发 / 大限已至 / 填满手牌 / 给予关键词 / 卡牌奖励 / 预见 / 三处拿牌 / 临时增益 / 透支能量 / 重放卡牌 / 增加额外资源量上限 / 先古转化两种 / 召唤伙伴与 11 种伙伴效果 |

**做不了的 8 种**（`EffectCatalog.NextTurnMeaningless`）—— 语义绑死在「打出的那一瞬间」，
界面上**不显示**勾选框，校验器会明确说清（不许静默）：

`CheapenSelf`（本回合这张牌自己费用变 0）、`CheapenSelfOnDraw`（抽到这张牌时本场费用 -N）、
`ReturnSelfToHand`（把这张牌放回手牌）、`CheapenDraw`（抽到就减费本身是持续状态）、
`EndTurn`（下回合开始时结束回合 = 跳过下回合，语义要用户定）、`ExtraTurn`、
`ScrapeDraw`（刮削是「抽 N 张 + 就地处理」）、`TurnEndPlay`（本身就是「稍后执行」，再套一层是双重延迟）。

### 老存档口径（用户已定）

历史上界面上能被勾上「下回合生效」的只有 13 种，其中 8 种**当年勾了没生效**（生成端没有延迟分支）：

**透支能量 / 翻倍状态层数 / 暴露 / 清除指定状态 / 清除全部减益 / 禁止获得格挡 / 禁止回复生命 / 伙伴攻击**

这 8 种**从 V0.2.133 起真的生效**（老存档里勾过的会开始真的延迟生效 —— 这是用户明确接受的
「老存档输出会变」）。其余任何种类都**不许改变老存档的生成输出**（红线①）：它们在 V0.2.132 及以前
界面上根本没有勾选框，老存档里必然 `NextTurn=false`，所以走的还是原来那条路。

### 已知边界（不静默，写在这里）

- 延迟状态里没有「这张牌」，所以**延迟执行的数值一律取生成期折好的常量**
  （`Amount + UpgradeAmount`，把升级增量也保住；「数值 = X」在延迟里退成基础数值）；
- 「打到打出时选中的那个目标」靠 `PowerCmd.Apply` 的 target 参数带走，延迟状态里用 `base.Target` 取回；
  **目标已经不在了就退成第一个可打的敌人**（不会静默什么都不做）；
- **遗物 / 药水 / 自定义状态链上的「生成类」七种**（生成 / 复制 / 变化 / 全局加牌）还没有延迟分支
  （那七种需要「这张牌」）—— 只接上了**通用延迟分支**的那 45 种；
- 「刮削」这类延迟语义待定的效果，等用户拍板。

## 安装目录 = 游戏的 mods 文件夹


「构建 / 日志」页的安装目录要填**游戏目录下的 `mods` 文件夹**：

- 点「浏览…」选中游戏根目录（或 `data_sts2_windows_x86_64` 目录）时，工具会自动改成它下面的 `mods`；
- 启动时会按注册表里的 Steam 路径自动找一遍游戏并填好（`mods` 目录本体第一次装模组时会自己建）；
- 校验结果里会检查这一项：填成游戏根目录或随便一个目录时会给出警告和推荐路径。

## 存档之间互不影响

- 生成出来的工程目录**跟着存档文件名走**（存档 `七.json` → 工程目录 `七`），不是跟着模组 ID 走；
- 每次生成会先清掉该目录里上一次生成的工程文件，不会残留旧 `.cs` 导致「改了配置反而编译不过」；
- 左下角问题列表会提醒：**同一个模组 ID 的另一个存档**装进游戏会互相覆盖（dll / pck 名字一样），
  建议把其中一个的模组 ID 改掉（「角色」页最上面）。

## 多人联机：两份角色模组一起玩会不会冲突

先看本体的硬规则（`Core/Multiplayer/Connection/HandshakeManager.cs` + `PeerVersionInfo`），它决定了**能不能连上**：

1. **游戏版本要一致** → 不一致是 `VersionMismatch`；
2. **「影响玩法的模组」列表要完全一致**（`manifest.id` + 版本，双向都不许缺） → 不一致是 `ModMismatch`；
3. **模型 ID 库的哈希要一致**（`ModelIdSerializationCache.Hash` = 所有注册模型 ID 的集合） → 不一致还是 `VersionMismatch`。

所以「你只装你的角色、我只装我的角色」**连不上**：两个角色模组都是玩法模组，都会往模型库里加东西。
**要一起玩，所有人都得装同一整套模组**（A 的和 B 的都装上）。
非玩法模组（manifest 里 `affects_gameplay: false`）可以不一样 —— 本体只记一条警告。

装齐之后，每个客户端会同时加载好几份角色模组，「各看各的」靠的是**互不覆盖**：

| 东西 | 怎么做到 | 一起玩安全吗 |
|---|---|---|
| 本体状态改名 / 换描述 / 换图标 / 血条颜色 | 运行时补丁，**只在「这一局本地玩家玩的正是我方角色」时生效**（`LocalContext.GetMe().Character`） | ✅ 各看各的 |
| 额外资源量的**计数器**（图标 + 名字条） | 同一套：补丁按「这个计数器归谁」判断（`NStarCounter.Initialize(player)`） | ✅ 各看各的 |
| 额外资源量的**悬停提示**（名字 + 说明） | 写**我们自己的键** `<角色>_EXTRA_RESOURCE.title/.description`，计数器补丁在「归我方角色」时换上去 | ✅ 自己的键，不碰本体 |
| 卡面上的资源图标 / 提示 | 同上（自己的键 + 自己的图） | ✅ |
| 卡牌 / 遗物 / 药水 / 状态 / 图标 / 场景 | 类名与资源路径都带**角色类名前缀**（`<角色>Strike`、`<角色>Card40`…） | ✅ 只要两个角色的类名不一样 |
| 能量球 / 图集精灵 | 用模组自己的图集页（不动本体 `ui_atlas.tpsheet`） | ✅ |
| 联机宝箱房的**手势** | 前四张（选择 / 剪刀 / 石头 / 布）换的是本体按固定路径读的那套图，**每个角色各一套**，互不覆盖 | ✅ 各看各的 |
| 联机宝箱房的**「拿取」手势** + 那句**催促文本** | 运行时补丁，**只在「这一局本地玩家玩的正是我方角色」时生效**：手势看 `LocalContext.IsMe(某只手)` + 私有状态 `_state == GrabbingRelic`，文本看 `LocalContext.GetMe().Character` | ✅ 各看各的 |

⚠ **唯一还要自己注意的一件事**：本体的**模型 ID 是用类名算的**
（`ModelDb.GetId` → `Slugify(类型名)`，**不含命名空间**）。两份模组只要有一个**同名类**，
游戏在初始化模型库时就会抛 `DuplicateModelException`（表现是**游戏起不来**，不是战斗中出错）。

- 工具自动生成的类名都带角色前缀（`SparkleStrike` / `SparkleCard40` / `SparklePower1`…）✅；
- **你自己手填的类名**（卡牌 / 遗物 / 药水 / 自定义状态 / 角色类名）要保证全局唯一 ——
  两个人都把角色类名起成 `Seven`、或者都留默认的 `MyCharacter`，就会撞。
  左下角「配置检查」现在会提示「类名没带角色前缀」（一起玩的话建议带上）。
- 「模组 ID」同理：两个模组的 `manifest.id` 一样时 dll / pck 会互相覆盖。

**本体全局性的东西**（做不到「各看各的」，因为键是全局的，两个模组都改就是谁后加载谁赢）：

- **本体卡牌 / 遗物 / 药水描述里的旧名字替换**、**本体关键词改名**（写 `EXHAUST.title` 这种键）；
- 一起玩时建议**所有人用同一份配置**，或者干脆别用这两个功能（本体的状态改写不受影响，它是运行时按角色判断的）。

## 左下角的卡牌数量

状态栏右下角实时显示「卡牌：攻击 N ｜ 技能 N ｜ 能力 N ｜ 共 N 张」，按 `Attack / Skill / Power` 分类统计。

## 美术资源（在「美术资源」页上传）

未上传的槽位默认生成**中性占位图**（纯色带边框，不含任何游戏素材）。
如果只为本地随手测试，可以勾选「未上传的槽位用本体（铁甲战士）素材占位」——
**发布模组前请取消勾选并提供自己的美术**。

勾选后除了各槽位用本体图，**四个角色场景直接借用本体铁甲战士的场景**：
战斗立绘 `scenes/creature_visuals/ironclad.tscn`、商店 `.../ironclad_merchant.tscn`、
篝火 `.../ironclad_rest_site.tscn`、选人背景 `scenes/screens/char_select/char_select_bg_ironclad.tscn`
（复制时去掉 uid、把根节点名换成我们自己的角色名；Spine 数据 / 图集 / VFX 都在游戏自己的 pck 里，不用我们带）。
取消勾选则回到工具自带的占位图（zwt.png）。

> 注意：`animations\characters\ironclad\ironclad.png`、`animations\character_select\ironclad\*.png` 那几张是
> **Spine 图集**（例如战斗图集是 1329×269 的图集页），**不是**能直接当立绘用的单张图 —— 别把它们当静态立绘传进去。

**预览会按「游戏里的显示区域」画一块黑底**（普通卡 / 诅咒的卡面 1000×760、各槽位按上表的本体原图尺寸）：
上传图比例和它不一致时，游戏里是按比例缩放居中显示的，多出来的那一圈就是黑边 ——
预览里的黑底就是它，下面还会写清「图偏宽 → 上下留黑边 / 图偏窄 → 左右留黑边」以及大概占多少。
想不留黑边就把图裁成那个比例（或直接用推荐尺寸）。

**先古卡的卡面是竖的**：本体的先古卡在卡面节点里用的不是 `%Portrait` 而是 `%AncientPortrait`
（598×842，而且**直接拉满、不保持比例**），本体所有先古卡素材都是 **606×852**。所以：
选到先古卡时，卡面预览框会变成 606×852 的**竖框**、而且按「拉满」显示（比例不对能直接看到变形），
上传按钮写「建议 606×852 竖图」，比例说明也改说「会被拉变形」而不是「留黑边」；
生成占位图时先古卡也走竖图（勾「用本体素材占位」用本体的 `ancient_beta.png`，
不勾则生成 606×852 的中性占位图）。普通卡 / 诅咒不受影响，还是 1000×760 横图。

> ⚠ **切页时预览要跟着换**（V0.2.165 修，用户实测：「点卡牌页查看卡牌，再点先古页查看先古牌，
> 先古牌的卡面会消失，比例还变成卡牌页的横图比例」）：
> 三个卡牌列表页（卡牌 / 诅咒 / 先古卡）**共用同一套卡面预览属性**，取「当前这张」的判据是
> `CardUiSelection ?? SpecialCardOf()`；而卡牌页那个列表镜像的语义是「点空槽行时**保持上一张**」
> （`SlotRowMirror.UiSelection`）—— 于是三栏互斥时只把 `CardList.SelectedItem = null` **不够**，
> 镜像不会松手，切到先古页后预览拿到的还是卡牌页那张：**框还是 1000×760 横图、图也按那张卡的 key 去取
> （先古卡的卡面存在另一个 key 上，取不到 = 看起来「卡面消失」）**。
> 修法：互斥的两个方向都连**镜像**一起清 —— `ClearCardPageSelectionForPortrait()`（选到诅咒 / 先古 / 状态卡时
> 清卡牌页）+ `ClearSpecialCardSelections()`（在卡牌页选中真卡时清那三栏）。自检钉住 4 条
> （先量 1000×760 → 切先古页必须变 606×852 竖框 + Fill + 预览取的是先古卡那张 → 再切回来变回 1000×760）。

上传文件按**原尺寸直接使用**（程序不做缩放），请按下列尺寸准备：

| 槽位 | 格式 | 尺寸（本体原图实测） | 说明 |
|---|---|---|---|
| 静态立绘（战斗/商店/篝火） | PNG-32（RGBA） | **281×235** | **替代本体的 Spine 骨骼动画**：战斗立绘、商店立绘、篝火立绘三处都用这张静态图。留空则用工具自带的 `Assets\placeholder_character.png` |
| 顶部头像 | PNG-32（RGBA） | **88×88** | 游戏内左上角头像，方图，可用 176/512 等比 |
| 头像描边 | **十六进制颜色，不用传图** | — | 填 `2A2A2A` 这类色值，工具会按颜色自动生成 88×88 描边图（取头像的透明通道做剪影） |
| 选人界面立绘 | PNG-32 | **132×195** | 竖版 ≈0.677:1，建议 396×585 等比 |
| 选人界面背景大图 | PNG | **横图，建议 1920×1080** | 选人界面里角色背后那张图，**整屏铺满（keep-aspect 居中）**，所以要横图（游戏画面就是 1920×1080）；传竖图会左右留黑边。留空则用静态立绘那张图 |
| 地图标记 | PNG-32 | **49×64** | 地图上的角色标记 |
| 能量图标 | PNG-32 | **74×74** | 会写进 `ui_atlas.tpsheet`；文字里的能量小图标另外按 24×24 生成，左侧能量球也会换成这张图 |
| 选人过场贴图 | 灰度/RGBA | **2560×1200** | 白=显示、黑=透明（转场遮罩） |
| 卡面（每张卡） | PNG（RGBA 或 24bit 均可） | **1000×760**（普通卡 / 诅咒）；**606×852**（先古卡，竖图） | 横版，本体铁甲卡面即 24bit 无 alpha；先古卡的卡面框是竖的（本体先古卡素材就是 606×852） |
| 遗物图标（每个遗物） | PNG-32 | **256×256** | 方图 |
| 多人手势（选择 / 剪刀 / 石头 / 布 / 拿取） | PNG-32 | **422×1200** | 联机宝箱房那只手。前四张是本体按固定路径读的（`images/ui/hands/multiplayer_hand_<角色>_<手势>.png`）；「拿取」是本体没有的第五张，传了才会多写 `<角色>_grab.png` + 一个补丁 |

> 「选人界面立绘（未解锁）」不提供上传入口：未上传时按中性占位或本体占位生成。

### 联机宝箱房：那只手 + 那句「开抢！！！」

「美术资源」页最下面有五个槽位（手势：选择 / 剪刀 / 石头 / 布 / 拿取），再下面是一个「催促文本」输入框：

- **前四张**是本体按固定路径读的**一整套**（`CharacterModel.ArmPointingTexture / ArmRockTexture / ArmPaperTexture /
  ArmScissorsTexture` → `images/ui/hands/multiplayer_hand_<角色ID小写>_<手势>.png`），上传即替换文件。
  **只传一部分**的话，没传的那几张会是占位图，联机进宝箱房时那只手会一半是你的一半是占位 ——
  要么四张都传、要么都不传（配置检查会提醒）。单机看不到这处美术（宝箱房只在联机出现）。
- **「拿取」是本体没有的第五张**：本体拿遗物是「布（伸手）→ 石头（拳头攥住）」两步糊出来的
  （`NHandImage.GrabRelic`）。传了它，生成时会多写一张 `<角色>_grab.png` 和一个补丁 `cs/TreasureGesture.cs`：
  给私有的 `NHandImage.SetTextureToFightMove` 打 Postfix，只在**这只手是我方的**（`LocalContext.IsMe(Player)`）
  + **私有状态 `_state` 是 `GrabbingRelic`** + **正要显示「石头」** 时换成你的图。
  状态是反射读的（本体改布局就静默退回本体表现，绝不会把石头剪刀布亮牌时的拳头一起换掉）。
- **催促文本**：本体那句话来自全局键 `gameplay_ui.TREASURE_FIGHT_TEXT`（「开抢！！！」）。
  直接覆盖那个键 = 别的角色、联机另一边的玩家也跟着变，所以改成给 `NTreasureRoomRelicCollection._Ready`
  打 Postfix，**只在我方角色的这一局里**把 `%FightLabel` 那一行字换掉（本体的本地化键一个字都不改）。
  留空 = 沿用本体那句。字太长会按屏幕自动缩字号（本体最小 8 号）。

上传图片后会**立刻写进当前存档**（角色美术槽位、逐张卡面、遗物 / 药水 / 状态图标都一样），
状态栏会显示「已写进存档：xxx.json」；清除图片与撤回同样立刻落盘。

### 状态悬停提示（卡面 / 遗物 / 药水描述里的状态）

本体里「描述提到某个状态」时鼠标移上去能弹那个状态的说明，靠的是模型自己声明 `ExtraHoverTips`
（本体例：`Abrasive` 写 `protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<DexterityPower>()]`）。
生成的卡牌 / 遗物 / 药水也会按用到的状态自动补上这一段：

```csharp
// 鼠标悬停描述里的状态时弹出的说明（文本来自本体 powers 表；本体状态被改写后就是新名字 + 新描述）
protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips =>
[
    MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromPower<WeakPower>(2),
    MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromPower<SparkleMaskedFoolPower>(1)
];
```

- 收进来的状态：这条效果**施加**的状态（含「下回合生效」那种延迟状态类）+ **条件里盯**的状态（「拥有破绽至少 N 层」）；
- 药水那份是 `public override`（本体 `PotionModel.ExtraHoverTips` 是 public），卡牌 / 遗物是 `protected override`；
- 文本取自本体 `powers` 本地化表 → **本体状态改写后（改名 + 改描述）弹出来的就是新名字 + 新描述**；
  自定义状态也会弹（它的名字/描述本来就写在同一个表里）；
- 没提到任何状态就不生成这一段（不留多余代码）；
- 显示的数量是效果里填的那个数（填「= 层数 / = X」时数量说不准，就不写数字，跟本体一致）。

### 状态 / 遗物图标的「本体回退路径」

本体取图标是按 `Id.Entry` 拼资源路径的，图集里找不到精灵时会**回退到另一条路径**；
只往图集目录里放文件是不够的（游戏会报 `AtlasResourceLoader: Missing sprite 'xxx' in power_atlas` 并显示紫色占位图）。生成器两条都写：

| 对象 | 图集路径（本体先查这个） | 回退路径（**必须也有文件**） |
|---|---|---|
| 自定义状态 / 能力牌 | `images/atlases/power_atlas.sprites/<状态>.tres` | `images/powers/<状态>.png`（`BigIcon` 施加特效那张大图也读这里） |
| 额外资源量（隐藏起始遗物） | `images/atlases/relic_atlas.sprites/extra_resource_relic.tres` | `images/relics/extra_resource_relic.png`（不然遗物栏那格是紫色占位） |
| 自定义遗物 / 药水 | 各自的 `*_atlas.sprites/<id>.tres` | `images/relics/<id>.png` / `images/potions/<id>.png` |

「本体状态改写」的图标走的是 Harmony 补丁（`PowerModel.Icon` / `BigIcon`），覆盖的是**本体已有**的状态，不需要回退路径。

### 同时装多个自建角色：能量图标 / 药水图标为什么不会互相盖掉

**能量图标**（卡面费用、能量悬停提示）和**药水图标**在图集加载器里是「按精灵名字去图集的 `.tpsheet` 里查」的
（`AtlasResourceLoader` → `AtlasManager.GetSprite`）。而 `res://` 下**同一个路径只有一个文件能生效** ——
早期版本每个模组都往自己工程里塞一份完整的 `ui_atlas.tpsheet`（本体 102 条 + 自己那条能量图标），
于是同时装两个自建角色时，后加载的那个 pck 会把前一个盖掉，另一个角色的能量图标就变成
`Missing sprite 'card/energy_xxx' in ui_atlas`，**游戏里图标直接消失**
（`ui_atlas` 在图集加载器里**没有**回退路径，找不到就是没有；药水有回退图所以看着正常）。

现在生成器**不再写任何 `.tpsheet`**，改成生成一个补丁 `cs/AtlasSpritePatch.cs`：
运行时给本体的 `AtlasManager.HasSprite` / `GetSprite` 挂前缀，把「我们自己的那几个精灵」
直接返回我们自己的纹理（能量图标用 `<颜色>_energy_page_0.png`，药水用 `images/potions/large/<id>.png`）。
文件名每个模组都是独有的，所以装多少个自建角色都不会互相覆盖；本体的那几份图集表也保持原样。

另外：**没上传额外资源量图标**时，那个隐藏起始遗物的回退图也会写出来（用本体的星星图标），
不然遗物栏那一格会报 `Missing sprite` 并显示紫色占位。


### 关于动画

生成的角色**不含任何 Spine 骨骼动画**：插件的战斗立绘、商店立绘、篝火立绘、选人背景四个场景
都由工具生成的静态图场景替代（节点结构照本体的非 Spine 模板，`NCreatureVisuals` 等脚本能正常取到节点，
`SpineAnimationAccess` 在无骨架时是空操作，不会报错）。

仍然沿用本体资源的部分（运行时由游戏本体提供，不复制进模组）：能量球表面贴图、卡牌拖尾、
角色选择界面的图标场景、部分 VFX 贴图。

#### GIF 解码（修过一个「真实素材一律解不开」的硬伤）

GIF 的**全局 / 局部色表项数**按规范是 `2^(N+1)`（N = 打包字段低 3 位）。
以前的代码写成 `(1 << (N + 1)) & 31` —— N = 7（满 256 色，真实素材几乎都是）时算出来是
`256 & 31 = 0`，于是**色表一项都不读、后面整块数据全部错位**，满色表的 GIF 一律解不开
（实测 6 个真实 GIF 里 5 个挂在这一句）。现在按规范算，并且夹的是 N（非法值 8 → 7）而不是移位结果。

同一处还修了两个「静默变成解不开」的问题：

- LZW 解出的像素数**可能少于**画布像素数（截断 / 坏流）→ 以前会把数组缩到实际长度，
  调用方按 `fw×fh` 逐像素读时越界抛 `IndexOutOfRangeException`，又被 catch 吞成「解不开」。
  现在补足到期望长度（缺的当透明黑）。
- 解不开时记下**具体原因**（「文件头不是 GIF」「没有全局色表」「某一帧的像素数据解不出来」…），
  提示行后面会接上原因，不再是光秃秃一句「解不开」。

> 实测：本机 8 个真实 GIF（含隔行 / 4 帧动画 / 满 256 色）全部解开；另用真编码器生成的
> 300×200 满色表图逐像素比对，**0 个像素不同**。

#### 效果库读不到（空壳解包工程）

有的整合包里那个「解包工程」是**空壳**：`src\Core\Models\Powers\*.cs` 是几百个 1 字节的文件
（只留了文件名），`Cards` 那边也有上千个。以前只看「目录在不在」，于是：

1. 把它当成可用工程 → **不再去读 `sts2.dll`**；
2. 正则解析那些空文件一条都匹配不上 → 效果库为空 → 校验器**把生成整个拦死**（连不含动画的角色都生成不出来）。

现在两道保险：

- **空壳判定**：抽样最多 40 个 `*.cs`，全都不超过 2 字节就当目录不可用（有一个有真内容就不算空壳）；
- **读空回退**：解包工程读了个空时，再读一次 `sts2.dll`。

读 `sts2.dll` 的方式也换成了**只读元数据**（`System.Reflection.Metadata`，不加载程序集）：

- 以前用 `Assembly.LoadFrom(dll).GetTypes()`，在依赖不全的程序集上只能拿到 35/9760 个类型
  （`ReflectionTypeLoadException`），**.NET 9 上甚至直接把栈打爆** —— `StackOverflowException`
  是 CLR 致命错误、`catch` 不住，整个工具当场死。用户遇到的「效果库读不到」就是这个；
- 现在只读类型名 / 基类链（连 GodotSharp、0Harmony 都不需要），快且不会崩。
  实测：空壳工程 + 本机 `sts2.dll` → **268 个状态 / 596 张卡 / 65 瓶药水** 正常读回。

## 逐帧动画（把 GIF 拆成帧动画）

「动画」页可以把 **GIF 动图拆成一帧一帧**，让战斗 / 篝火 / 商店里的角色立绘动起来。
它只替换**这三处立绘**；其它美术槽位（头像、选人立绘、地图标记、能量图标、模组预览图…）完全不受影响。

**为什么在生成时拆帧、而不是游戏里直接放 GIF**：游戏引擎（Godot）读不了 GIF；
而且帧图在游戏里会被缩放，先把帧裁好、底抠干净，游戏里才不会出现黑框和糊边。
所以生成阶段就把动图解成 RGBA 帧、按同一个裁剪框裁好，写成 PNG + Godot 的 `SpriteFrames` 资源，
再由生成的运行时把本体的动画请求翻译成逐帧播放 —— **模组里不带 GIF，也不装任何解码器**。

### 7 个动作

| 槽位 | 本体动画 | 什么时候播 | 要传吗 |
|---|---|---|---|
| 待机（循环） | `idle_loop` | 战斗 / 篝火 / 商店的默认动作 | **必须有**（其它动作没素材都会退回它） |
| 攻击 | `attack` | 打出攻击牌时播一次 | 可选 |
| 施法 | `cast` | 打出技能 / 能力牌时播一次 | 可选 |
| 受伤 | `hurt` | 被打到时播一次 | 可选 |
| 死亡 | `die` | 死亡时播一次，播完**定格在最后一帧** | 可选 |
| 篝火 | `overgrowth_loop` | 篝火（营地）场景循环播放 | 可选 |
| 商店 | `relaxed_loop` | 商店场景循环播放 | 可选 |

没传的动作只会在日志里写一行 `[提示]`（例如「没死亡素材：死亡时会退回待机」），不会让生成失败；
**只有待机是硬要求**：没待机等于整条动画都不生效，生成前的检查会直接报错拦住。
不同动作的画布尺寸不一致时会写一行 `[警告] … 仍按同一裁剪框裁（可能错位）`。

### 设置（都在「动画」页底部，没勾总开关时不显示）

| 设置 | 作用 |
|---|---|
| 抠背景（+ 容差） | 把纯色底变透明：取头尾两个像素的均值当底色，RGB 每通道差不超过容差的像素 alpha 清 0。素材是白底 / 纯色底时开它 |
| 剥掉浅色描边（+ 亮度阈值） | 把轮廓外最外 1~2 层浅色像素也剥成透明（素材在轮廓外留了一圈不透明的浅色边时用）。**会吃掉不透明像素**；剥掉超过全图 2.5% 时整块还原，保护素材不被剥没 |
| 最近邻过滤（默认开） | 像素画必须开：帧图在游戏里会被缩放，线性过滤会混出源里不存在的颜色、边缘发糊 |
| 播放速度系数 | 1 = 动图原速，2 = 双倍速；游戏里每帧时长 = 计划里的每帧时长 ÷ 这个系数 |
| 缩放 | 0 = 按「静态立绘高 ÷ 待机内容高」自动算；填了就用手填的 |
| Y 偏移 | 0 = 自动：让帧内内容的底边落在静态立绘的底边（脚的位置不变）；要微调就填非 0 |
| 每帧时长秒 | 0 = 用动图自己的延时取平均；填了就所有动作都用这个值 |
| 手动裁剪框 `x0,y0,x1,y1` | 留空 = 自动（**所有动作所有帧的并集**，7 个动作共用同一个裁剪框，换动作时角色不会跳位置）。x1 / y1 是**开区间** |
| 篝火 / 商店里保持专属动画 | 不勾：篝火和商店会先播你传的专属动画，但本体过一会儿会发一次「回待机」，画面就切回待机动画了（本体默认行为）。勾上就一直保持专属动画 |

> 裁剪框和缩放**永远按原图算**：抠底 / 剥描边只改写出去的像素。这样反复调这两个开关时，
> 角色不会因为裁出来的大小变了而跳位置。

「预览动画计划」会**真解一遍动图**（不写任何文件），把裁剪框 / 每个动作的帧数与每帧时长 / 缩放 / Y 偏移 /
提示行列出来 —— 素材不对（待机解不开、几个动作的画布尺寸不一致…）在生成之前就能看见。

### 生成出来的东西

```
images/characters/anim/<角色>/<角色>_<动作>_<000..N-1>.png      # 裁好的帧（RGBA）
images/characters/anim/<角色>/<角色>_<动作>_<序号>.png.import   # Godot 导入设置
images/characters/anim/<角色>/<角色>_<动作>_frames.tres        # SpriteFrames（动画名 = 动作名）
images/characters/anim/<角色>/<角色>_<动作>_manifest.json      # 该动作的计划（裁剪框 / 尺寸 / 帧数 / 缩放 / 位置 / 触发名）
cs/FrameAnimRuntime.cs                                        # 帧资源注册表 + 播放器
cs/FrameAnimPatches.cs                                        # 把本体的动画请求翻成逐帧播放的补丁
```

`ModEntry.cs` 里会多出四个常量（缩放 / Y 偏移 / 速度系数 / 篝火商店开关）和每个动作一行的
`FrameAnimRegistry.Register(...)` 注册调用。

两个「为什么这么写」的点（改生成逻辑时别踩回去）：

- `SpriteFrames` 里 `speed` 恒为 `1.0`，**每帧秒数写在每个 frame 的 `duration` 上** ——
  Godot 的 `SpriteFrames.speed` 是**倍率不是 fps**，填 30 会额外加速 30 倍。
- `.png.import` 里 `path` / `dest_files` 用的 `ctex` 哈希是**源文件 `res://` 路径的 MD5**，不是文件内容的哈希。

### 两个容易踩的点

- **不勾「启用逐帧动画（动图拆帧）」时，生成结果和以前完全一样**：不写帧图、也不写那两个 `.cs`；
  槽位里留着的 GIF 路径不会生效（界面会给一条橙色提示 + 「一键启用逐帧动画」，免得你以为它在起作用）。
  老存档（JSON 里根本没有这一档）读出来就是「没启用」，走的就是这条路。
- **回读（`--recover`）不还原这一档**：产物里只有裁好的帧资源，GIF 的**源路径没有写进工程**，
  所以动画设置只存在存档 JSON 里 —— 想改动画请回「动画」页改，别指望从工程反推。

## 效果库（增益/减益列表）怎么来的

工具**不内嵌**游戏的 Power 文本。运行时按以下顺序加载：

1. 从你指定的「解包工程目录」读取 `src/Core/Models/Powers/*.cs`（类名 + `PowerType` 分类）
   与 `localization/{zhs,eng}/powers.json`（显示名）→ 完整列表（约 244 项）。
2. 没有解包工程时，直接从游戏 `data_sts2_windows_x86_64\sts2.dll` 反射读取 Power 类型
   （约 265 项），中文名从本体 `SlayTheSpire2.pck` 里的 `localization/zhs/powers.json` 取。
   这条路只需要**游戏本体**，不需要解包，换台电脑也能直接用。
3. 读不到时，退回开发机内嵌的 `Data/powers_catalog.json`（该文件已被 `.gitignore` 排除，
   发布版没有它）。仓库里可以用 `tools/build_power_catalog.ps1` 从你自己的解包工程重新生成。
4. 都没有时列表为空，此时可在「增益/减益」下拉里**手动输入 Power 类名**（如 `WeakPower`）。

## 双击时弹「打开文件 - 安全警告：无法验证发布者」

那不是程序坏了，也不是杀毒在拦：exe 用的是发布出来的自包含程序，**没有买代码签名证书**
（所以「发行商」显示「未知发布者」），而 Windows 又认为文件「**来自网络**」——
从浏览器下载的 zip 解压出来的文件，每个都带一份「来自网络」标记（NTFS 的 `Zone.Identifier`
备用数据流），这时双击 / `start` / `Start-Process`（走 ShellExecute）都会先问一次。

- 处理：右键那个 exe → 属性 → 最下面勾「**解除锁定**」→ 确定；**更彻底**的是
  **解压之前**右键 zip → 属性 → 解除锁定，再解压（里面的文件就都干净了）。
  已经解压出来的一整份可以批量解除：
  `Get-ChildItem -Path "解压出来的文件夹" -Recurse -File | Unblock-File`；
- 直接点「运行(R)」也是安全的（就是你刚解压出来的程序本身）；
- 发布侧也做了防护：`tools\release_all.ps1` 在 `dotnet publish` 之后会把「程序文件」里带
  这个标记的文件统一 `Unblock-File` 一遍（标记会跟着 `Copy-Item` 传播，比如某台机器上
  NuGet 缓存里的 apphost 带了标记就可能被复制进产物）——所以**打出来的包里不会有标记**；
  用户那边看到的标记来自「下载 + 解压」这一步，需要按上面那两条解除。

> 踩过的坑（2026-10-09）：后台自检脚本用 `Start-Process` 起刚发布出来的 exe，被这个框
> 卡住直到人工点掉 —— 自动化里请直接调用（`& $exe --uicheck`，走 CreateProcess 不弹框）。

## 改这个工具时：源码备份 + 编码体检（`tools\backup_src.ps1`）

2026-10-08 出过一次事故：某个工具把 `MainWindow.xaml.cs` 按错代码页重写了一遍
（UTF-8 字节被当 CP936 读出、再按 UTF-8 写回）—— 文件变成一坨乱码、还丢了字节；
而当时 git HEAD 还停在 V0.2.51（落后 68 个版本）、手边**一个干净副本都没有**，
最后只能拿已发布 DLL 里的字符串当「预言机」一点点反推，花掉整整一轮。
所以现在有两道闸：

- **快照**：`tools\backup_src.ps1` 把源码树（`src` / `tools` / `docs` / `README.md` / `.gitignore`，
  不含 `bin` / `obj` / `_tmp` / 输出 / 存档）整份复制到 `..\_备份\源码\源码_<时间戳>\`，
  并写一份 `清单.tsv`（每个文件的 SHA256）。默认保留最近 **30** 份（`-Keep N` 可改）。
  **每次发版 `release_all.ps1` 都会自动先跑一遍**（改动任何东西之前），也可以随时手跑：
  `powershell -ExecutionPolicy Bypass -File tools\backup_src.ps1`
- **体检**：同一个脚本会扫「被写坏」的三个特征 —— **非法 UTF-8 字节**、**U+FFFD**（替换字符）、
  **私用区 PUA 字符**（U+E000~U+F8FF，昨天那坨乱码就是这类），并和最近一次快照对比，
  列出「改了哪些 / 新增 / 少了」。**命中就返回退出码 1，发版脚本会直接中止**（`-SkipBackup` 可跳过）。
  平时可以随时 `... -Check` 只体检不复制，`-List` 看已有快照。
- 还原：把快照目录里的文件复制回 `t3` 对应位置即可（清单里的 SHA256 可以核对有没有还原干净）。
- **git 也自动了**：`release_all.ps1` **发布完默认会把源码提交到本地**（提交信息 `发版 V<版本>（版本号 + 发版说明）`），
  所以每个版本在 `git log` 里都有一条、随时能 `git restore --source=<commit> -- <文件>` 退回去。
  默认**不推送**：要推远端加 `-Push`（或者你自己 `git push`）；完全不想提交加 `-NoCommit`。
  —— 昨天之所以那么难救，根因就是「68 个版本没提交过」，现在这道闸是默认开的。

## 许可

工具源码：见仓库内 LICENSE（如有）。
《Slay the Spire》《Slay the Spire 2》及其全部素材版权归 Mega Crit Games 所有。

---

## V0.2.171（生成被"好像没填过"的栏位挡住 + 「播放音频文件」编译不过）

* **"我并没有填 Y 怎么还这样"**：「命中次数 / 对群数」是按**种类**收起的一栏，换效果种类**之前**留下的值
  会**看不见地**留着，校验器拿它挡住生成（用户实测：卡牌「新卡」上留着 RepeatFormula='Y'）。现在：
  ①里面还留着值时那一行**露出来**，可以直接清；②**换种类自动清掉**这一种类用不到的它；③这条从
  **错误**降成**警告**（不生效照样说清，但不再挡住整个模组）。
* **「播放音频文件（我上传的）」整张卡编译不过（CS1513）**：那句 w.Open("{") 会生成 { { ——
  开两个块只关一个，整张卡少一个 }。从 V0.2.155 就存在，一直被别的报错挡在前面。已修，
  用户那份 43 张卡的存档现在 --build **编译 OK**。

---

## V0.2.172（角色页底部也能填多人催促文本 + 新增「角色最大能量」）

* **多人催促文本**：「角色」页最底下多了一个输入框（绑的是**美术页那一个** Profile.Art.TreasureFightText，
  两边同步、存档格式不变，美术页那个框照旧留着）。
* **角色最大能量**（新字段，默认 3 = 本体默认）：本体 CharacterModel.MaxEnergy 是虚属性，角色类里一个
  public override int MaxEnergy => N; 就是每回合能量上限，**不需要补丁**。⚠ 等于 3 时**不写 override**
  （老存档 / 基线逐字不变）。1~99 之外校验报错；不是 3 时给一条「改了会明显改难度」的提示；回读认不到回退 3。
