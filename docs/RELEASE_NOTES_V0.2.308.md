# V0.2.308 — 用自定义组「生成卡牌」打出去没反应：组里的牌被本体卡池过滤光了

用户原话：

> 使用自定义组进行生成卡牌的选择时，无法正常进行

## 查到的真因（用你的 `bzblMod.json` 复现）

组「均潮论刃」= 利刃（`bzblBlade`）+ 长槊（`bzblSpear`）；「规则书写者」再加一张篡改（`bzblChange`）。
这三张牌都是 **稀有度 = 基础（Basic）**（`InCardPool=False`、无色，就是令牌牌）。

生成出来的代码长这样：

```csharp
var __genPool = new List<CardModel> { ModelDb.Card<bzblBlade>(), ModelDb.Card<bzblSpear>() };
List<CardModel> __candidates = CardFactory.GetDistinctForCombat(base.Owner, __genPool, 2, …).ToList();
if (__candidates.Count > 0) { …弹选牌界面… }
```

而本体的 `CardFactory.FilterForCombat`（`CardFactory.cs:159`）第一句就是：

```csharp
c.CanBeGeneratedInCombat && c.Rarity != CardRarity.Basic && c.Rarity != CardRarity.Ancient && c.Rarity != CardRarity.Event
```

**整池被滤空** → `__candidates` 是 0 张 → 那个 `if` 整段跳过：牌打出去**什么都没发生**，选牌界面根本不出现。
（「自己挑一张」那一档不受影响，因为它本来就不走这个 API —— 所以你会觉得「有时候行、有时候不行」。）

## 改法

「范围」是**自定义组 / 自定义关键词**（用户明确点名要这几张牌），而且这几张里**有**会被上面那条过滤掉的
稀有度（基础 / 先古 / 事件）时，生成端改成**直接按这个列表抽**，不再过本体卡池：

```csharp
List<CardModel> __candidates = MegaCrit.Sts2.Core.Extensions.IEnumerableExtensions
    .TakeRandom(__genPool.Distinct(), 2, base.Owner.RunState.Rng.CombatCardGeneration)
    .Select(c => base.Owner.Creature.CombatState.CreateCard(c, base.Owner)).ToList();
// 不过本体卡池过滤（组里的牌可能是「基础」稀有度的令牌）
```

「多选1（候选 N 张里选 1 张）」和「按范围随机 N 张」两条路都改了（它们都会走那个 API）。
别的范围（攻击 / 技能 / 能力 / 诅咒 / 状态 / 任务 / 整个角色卡池）**一个字都没动**，照旧走本体卡池过滤。

## 验收

* 用你的存档复现：卡「均潮论刃」→ 现在生成 `TakeRandom(__genPool.Distinct(), 2, …)`；
  「规则书写者」→ 同上（3 张里随机 2 张当候选）。
* 探针 `sacprobe gengroup`（组里两张「基础」令牌 + 多选1 + 随机 2 张）：生成的工程 **`dotnet build` 0 错误**。
* 自检：**4496 PASS / 0 FAIL**（+6 条 V0.2.308 断言：判据、多选1 走新路、随机 N 走新路、
  对照「组里都是普通稀有度 → 照旧走本体那套」、以及真编译）。
* 红线：Sample 113 个文件 / WhiteCloak 412 个文件**逐字节相同**（只有 `.sln` 的 GUID 不同）。
* 只出更新包（`-NoBundle`）。

## 说明

「基础」稀有度的牌本来就不在卡池里（这是本体给令牌牌的分类），所以旧写法等于**永远抽不到**。
现在只有「你亲手选的组 / 关键词」会绕开这道过滤 —— 按「攻击 / 技能」这种宽范围生成时，
本体那套过滤仍然生效（不然会把基础牌、先古牌一起塞进奖励池那种地方）。
