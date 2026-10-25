# V0.2.278 —— 三处生成端 bug：锻造图「仅本回合升级」CS0103、图里 `ThisCardDamage` 属性没生成、`CardPile` 当 `PileType` 传

> 用户原话（三条分别来自三份别人的构建日志 / 存档）：
> 1. `DarkVinciSwingPoint.cs(40,17): error CS0103: 当前上下文中不存在名称"__fgTempUpda676"`
> 2. `CarmenCobaltScar.cs(128,52): error CS1061: 未包含 ForgeThisCardDamage 的定义`
> 3. `CarmenCrimsonScar.cs(49,75): error CS1503: 参数 2: 无法从 CardPile 转换为 PileType`

## 1. 锻造图「仅本回合升级」→ CS0103

锻造图里勾了「仅本回合升级」（`SpawnUpgradedThisTurn`）时，会生成一句「挂上临时升级状态」再把变量交给后面的附加处理。变量名两侧来源不同：

- 引用侧：`GraphTempUpVar(node, spec)` → `UniqueVar(node, "__fgTempUp")` = `__fgTempUp` + 6 位节点哈希
  （同一张图上多处「仅本回合升级」才不会互相覆盖）；
- 声明侧：`EmitGraphTempUpgradeApply` 里**写死**了 `"__fgTempUp"`。

于是 `var __fgTempUp = …;` 声明出来了，后面却引用 `__fgTempUpda676` → CS0103。

**修法**：`EmitGraphTempUpgradeApply` 增加 `tempUpVar` 参数，用**调用方算好的名字**声明；5 个调用点分别传 `cvTempUp` / `genTempUp`。
「生成卡牌 / 复制卡牌 / 变化卡牌」三条路都中，没勾这个选项时生成文本一个字符不变。

## 2. 图里【效果】/【条件】的数值栏直接写 `ThisCardDamage` → CS1061

卡牌类上要不要生成 `public decimal ForgeThisCardDamage`，判据是「这张牌用没用到这个读数」：
`CardUsesReading` = 卡牌页的条件 / 各条效果公式 + **两块锻造图**。而扫图那一份
（`GraphMentionsReading`）**只扫【数值】模块的公式栏**。

可图上【效果】/【条件】/【触发时机】的「数值 / 生效次数 / 命中次数 / 概率」这些栏
**也能直接写公式**（和【数值】模块同一套语法，见 `ForgeNumbers.NumberFields`）——直接写 `ThisCardDamage` 时，
代码里用了 `((decimal)this.ForgeThisCardDamage)`，属性却没生成 → CS1061。

**修法**：`GraphMentionsReading` 除了【数值】的 `Formula`，再按 `ForgeNumbers.NumberFields`
把每种模块的那几栏也扫一遍。没写这些读数的图/卡生成文本不变。

## 3. `CardPile` 被当成 `PileType` 传 → CS1503

图上「取几张 = 全部 / 随机」这两档，从某一摞取牌时生成：

```csharp
foreach (CardModel __fgCard in (CardPile.GetCards(__fgPlayer, PileType.Draw.GetPile(__fgPlayer))
    .Where(c => …)).ToList()) await CardCmd.Discard(choiceContext, __fgCard);
```

`CardPile.GetCards(玩家, PileType)` 第二参要的是**枚举**，而 `GraphAllPickExpr` / `GraphRandomPickExpr`
塞进去的是 `GraphPileExpr()`（返回 `PileType.X.GetPile(玩家)`，一个 `CardPile`）→ CS1503。

**修法**：新增 `GraphPileTypeExpr(s)`（返回 `PileType.Hand/Draw/Discard/Exhaust`），那两处改用它；
`FromCombatPile` 那条要的本来就是 `CardPile`，继续用 `GraphPileExpr`。

## 验收

- 探针 `_probe/tempupvar` **6/6**（三种效果「声明名 == 引用名」+ 没勾就不生成）；
  同一份探针挂到 **V0.2.277 的 Core.dll** 上跑 → **6 条 FAIL**，复现 `__fgTempUp / __fgTempUpd6de64` 那对。
- 探针 `_probe/graphreading` **6/6**（图【效果】数值栏 / 图【条件】数值 N 栏 / 老路【数值】模块 / 两条红线）。
- **用他们发来的 `CarmenMod.json` 真跑一遍**：修之前三张 Scar 卡共 12 处
  `CardPile.GetCards(__fgPlayer, PileType.X.GetPile(__fgPlayer))`，修之后 **0 处**；
  这份存档的全部 **81 份生成源**（卡 49 / 遗物 5 / 药水 1 / 状态 26）扫一遍：
  没有「CardPile 当 PileType」、没有「引用了 `this.ForgeXxx` 却没声明」，`CarmenCobaltScar` 现在会生成该属性。
- `dotnet build` 全解决方案 **0 error**；`--uicheck` 见下（预计 4448 PASS / 0 FAIL）。

## 版本

- `V0.2.278`


---

## 一并包含（另一会话 · 本版同批）：效果新增「即死（直接杀死目标）」

效果种类下拉里多了一项 `即死（直接杀死目标）`：

* 「作用对象」可选：自己 / 指定敌人 / 全体敌人 / 随机敌人 / 全体单位 / 召唤物…；
* 无「数值」可填；走本体 `CreatureCmd.Kill(creature)`（`CreatureCmd.cs:446`）——
  不吃格挡、不看层数，但「瓶中仙女」这类免死效果照样能挡（`force: false`，和本体一致）；
* 接在三条链上：**卡牌 / 遗物 / 药水**。生成结果（探针原文）：

```csharp
await CreatureCmd.Kill(cardPlay.Target);   // 卡牌 · 指定敌人
foreach (Creature foe in base.Owner.Creature.CombatState.HittableEnemies) await CreatureCmd.Kill(foe);   // 遗物 · 全体敌人
await CreatureCmd.Kill(base.Owner.Creature);   // 选「自己」
```

* 限制（不静默）：锻造页【效果】模块还没接这个种类 → 校验器给「警告：这条链暂时没有它」。
* 自检：本批一并跑到 **4453 PASS / 0 FAIL**（含 4 条即死断言 + 修了两条「作用对象显隐」旧断言）。