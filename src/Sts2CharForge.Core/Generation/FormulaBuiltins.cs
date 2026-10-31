using System;
using System.Collections.Generic;
using System.Linq;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 「数值公式」里能直接写的**只读量**（用户要求：参数监听 —— 回合数、本回合 / 本场战斗打出了多少张牌、
/// 丢了多少张、消耗了多少张、生成了多少张、花了多少能量、拿了多少额外资源量、身上有几个状态…）。
///
/// 为什么不做成「自定义参数」：参数要在「自定义参数 / 组」页先定义再引用，而这些量是**运行时的实时读数**，
/// 每次求值都不一样，定义一次没有意义 —— 所以它们直接写名字就能用（和 <c>X</c> / <c>Y</c> 一样）。
///
/// 数据来源**全部**是本体现成的公开 API（不需要额外补丁 / 记录器，唯一的例外是「获得的能量」，见
/// <see cref="Tracker"/>）：
///   · 回合数 / 轮数：<c>PlayerCombatState.TurnNumber</c> / <c>ICombatState.RoundNumber</c>；
///   · 牌堆张数：<c>PlayerCombatState.Hand/DrawPile/…</c>（战斗堆）/ <c>Player.Deck</c>（跨战斗的牌组）；
///   · 打出 / 抽到 / 丢弃 / 消耗 / 生成 / 伤害 / 格挡 / 击杀 / 召唤 / 能量 / 额外资源量：
///     <c>CombatManager.Instance.History</c> 里的各类 Entry（本体在每个动作发生时都会记一条，
///     见 <c>CombatHistory</c>；「历史」那一节的 19 种记录就是这张表的来源）；
///   · 自己 / 敌人的生命、格挡、状态层数、遗物、药水、金币、跑图层数：本体模型上的公开属性。
///
/// <c>{p}</c> / <c>{c}</c> 是占位符：生成端分别填「玩家表达式」和「自己那个生物表达式」
/// （卡牌那边是 <c>__fgPlayer</c> / <c>__fgSelf</c>；条件那种要内联的场合是
/// <c>base.Owner</c> / <c>base.Owner.Creature</c>）。
///
/// ⚠ 三条口径（都写进了每条的 Display，界面上的「可用量」小框直接看得到）：
///   1. **战斗外读到 0，不抛异常**：战斗牌堆（手牌 / 抽牌堆 / 弃牌堆 / 消耗堆 / 出牌堆）只有战斗里才有，
///      本体 <c>PileType.GetPile</c> 在战斗外会直接抛 <c>InvalidOperationException</c> ——
///      所以带 <c>{p}?.PlayerCombatState?.</c> 的那几条在战斗外（遗物「获得时」那种触发时机）读成 0。
///   2. **「本回合」用本体判据** <c>HappenedThisTurn</c>（同一轮 + 同一侧 + 每个玩家的回合数都没变），
///      所以**敌人的回合里读到的是你上一个回合的数据**。
///   3. **打出牌数按「开始打出」记**（<c>CardPlaysStarted</c>，本体在 <c>OnPlay</c> **之前**记一条）——
///      正在打出的这一张**算在内**；<c>CardPlaysFinished</c> 要等结算完才记（不含这一张）。
///
/// ⚠ 「复制出来的牌」在本体的历史里**也算「生成」**（复制走的是同一个
/// <c>CardPileCmd.AddGeneratedCardToCombat</c> → <c>History.CardGenerated</c>），
/// 所以没有单独的「复制张数」——界面上和文档里都写明了这一点。
/// </summary>
/// <summary>
/// 一条内置读数（界面上「可用量」/ 参考窗 / 「填入参数…」弹窗里列的就是它）。
///
/// <para><b><paramref name="CardOnly"/></b> = 这条读数**只能在卡牌链上用**（表达式里带着
/// <c>base.…</c> 这种「这张牌自己」的成员）。遗物 / 药水 / 自定义状态的 Owner 不是
/// <c>CardModel</c>，同一条表达式在那三条链上要么编不过、要么语义错；而「自定义参数 / 组」的
/// 取值来源下拉（<c>ParamSources.All</c>）会把内置读数整张表铺进去 —— 卡牌专属的读数一旦被选成
/// 参数来源，参数表达式会在 **OnPlay 序幕那一刻**求值（那时候伤害还没打）→ 恒为 0、静默失效。
/// 所以卡牌专属的读数**不进参数来源下拉**，用它的校验器也会在别的链上报错拦住。</para>
/// </summary>
public sealed record FormulaBuiltin(string Id, string Display, string CSharp, IReadOnlyList<string>? LegacyCSharp = null,
    bool CardOnly = false)
{
    /// <summary>把表达式里的占位符换成真正的 C# 片段。</summary>
    public string Expr(string player, string creature) =>
        CSharp.Replace("{p}", player).Replace("{c}", creature);

    /// <summary>
    /// 这条读数**以前版本**生成过的表达式（改过表达式时列在这儿）。
    /// 从老工程回读（<see cref="ProjectRecovery"/>）时按原文逐字对，所以两个都要认 ——
    /// 只认新版会让老工程里那个参数变成「认不出来」。
    /// </summary>
    public IEnumerable<string> AllExprs(string player, string creature)
    {
        yield return Expr(player, creature);
        foreach (string legacy in LegacyCSharp ?? Array.Empty<string>())
            yield return legacy.Replace("{p}", player).Replace("{c}", creature);
    }
}

public static class FormulaBuiltins
{
    /// <summary>历史记录里那些条目的完整类型名（生成的工程没有 using 那个命名空间，所以写全名）。</summary>
    private const string Hist = "MegaCrit.Sts2.Core.Combat.History.Entries.";

    /// <summary>
    /// 内置变量表（**不含**插件加的那些）。名字叫 Core 是因为 <see cref="All"/> 现在是「内置 + 插件」的现算结果：
    /// 插件是启动时扫描的，把插件那条并进静态字段就会在「插件还没加载」时算一次然后永远少那几条
    /// （V0.2.367 的 ForgeTimings 踩过同一个坑）。
    /// </summary>
    private static IReadOnlyList<FormulaBuiltin> Core { get; } = new[]
    {
        // ===== ① 回合 / 轮 =====
        new FormulaBuiltin("Turn",  "自己这一侧的第几回合", "({p}?.PlayerCombatState?.TurnNumber ?? 0)"),
        new FormulaBuiltin("Round", "当前战斗轮数（所有单位各走一次算一轮）", "({p}?.Creature?.CombatState?.RoundNumber ?? 0)"),
        // ===== ② 牌堆张数（战斗里的五摞 + 跨战斗的牌组）=====
        // 「战斗外读成 0」：战斗牌堆只有战斗里才有，本体 PileType.GetPile 在战斗外会抛异常。
        // 老版本生成的是不带保护的那几句（PileType.X.GetPile(...)），所以旧表达式列进 LegacyCSharp。
        new FormulaBuiltin("HandCount",    "手牌张数",     "(decimal)({p}?.PlayerCombatState?.Hand?.Cards.Count ?? 0)",
            new[] { "(CardPile.GetCards({p}, PileType.Hand).Count())" }),
        new FormulaBuiltin("DrawCount",    "抽牌堆张数",   "(decimal)({p}?.PlayerCombatState?.DrawPile?.Cards.Count ?? 0)",
            new[] { "(PileType.Draw.GetPile({p}).Cards.Count())" }),
        new FormulaBuiltin("DiscardCount", "弃牌堆张数",   "(decimal)({p}?.PlayerCombatState?.DiscardPile?.Cards.Count ?? 0)",
            new[] { "(PileType.Discard.GetPile({p}).Cards.Count())" }),
        new FormulaBuiltin("ExhaustCount", "消耗牌堆张数", "(decimal)({p}?.PlayerCombatState?.ExhaustPile?.Cards.Count ?? 0)",
            new[] { "(PileType.Exhaust.GetPile({p}).Cards.Count())" }),
        new FormulaBuiltin("DeckCount",    "牌组张数（跨战斗的整副牌，不是战斗里的牌堆）", Deck()),
        // ===== ③ 手牌 / 牌组里各类型牌的张数（「拥有的状态牌数」在这两条：DeckStatusCount / HandStatusCount）=====
        new FormulaBuiltin("HandAttackCount", "手牌里的攻击牌数", Hand("Attack")),
        new FormulaBuiltin("HandSkillCount",  "手牌里的技能牌数", Hand("Skill")),
        new FormulaBuiltin("HandPowerCount",  "手牌里的能力牌数", Hand("Power")),
        new FormulaBuiltin("HandStatusCount", "手牌里的状态牌数", Hand("Status")),
        new FormulaBuiltin("HandCurseCount",  "手牌里的诅咒牌数", Hand("Curse")),
        new FormulaBuiltin("DeckAttackCount", "牌组里的攻击牌数", Deck("Attack")),
        new FormulaBuiltin("DeckSkillCount",  "牌组里的技能牌数", Deck("Skill")),
        new FormulaBuiltin("DeckPowerCount",  "牌组里的能力牌数", Deck("Power")),
        new FormulaBuiltin("DeckStatusCount", "牌组里的状态牌数", Deck("Status")),
        new FormulaBuiltin("DeckCurseCount",  "牌组里的诅咒牌数", Deck("Curse")),
        new FormulaBuiltin("DeckUpgradableCount", "牌组里还能升级的牌数", "(decimal)(PileType.Deck.GetPile({p}).UpgradableCardCount)"),
        // V0.2.368（用户要求「抽/弃/耗三摞没有按牌型」）：补上三摞 × 五种牌型。
        // 这样「按弃牌堆里的攻击牌数造成伤害」这类不用自己写 C#，插件也能通过 formulaVariables 再加。
        new FormulaBuiltin("DrawAttackCount",    "抽牌堆里的攻击牌数", Pile("DrawPile", "Attack")),
        new FormulaBuiltin("DrawSkillCount",     "抽牌堆里的技能牌数", Pile("DrawPile", "Skill")),
        new FormulaBuiltin("DrawPowerCount",     "抽牌堆里的能力牌数", Pile("DrawPile", "Power")),
        new FormulaBuiltin("DrawStatusCount",    "抽牌堆里的状态牌数", Pile("DrawPile", "Status")),
        new FormulaBuiltin("DrawCurseCount",     "抽牌堆里的诅咒牌数", Pile("DrawPile", "Curse")),
        new FormulaBuiltin("DiscardAttackCount", "弃牌堆里的攻击牌数", Pile("DiscardPile", "Attack")),
        new FormulaBuiltin("DiscardSkillCount",  "弃牌堆里的技能牌数", Pile("DiscardPile", "Skill")),
        new FormulaBuiltin("DiscardPowerCount",  "弃牌堆里的能力牌数", Pile("DiscardPile", "Power")),
        new FormulaBuiltin("DiscardStatusCount", "弃牌堆里的状态牌数", Pile("DiscardPile", "Status")),
        new FormulaBuiltin("DiscardCurseCount",  "弃牌堆里的诅咒牌数", Pile("DiscardPile", "Curse")),
        new FormulaBuiltin("ExhaustAttackCount", "消耗牌堆里的攻击牌数", Pile("ExhaustPile", "Attack")),
        new FormulaBuiltin("ExhaustSkillCount",  "消耗牌堆里的技能牌数", Pile("ExhaustPile", "Skill")),
        new FormulaBuiltin("ExhaustPowerCount",  "消耗牌堆里的能力牌数", Pile("ExhaustPile", "Power")),
        new FormulaBuiltin("ExhaustStatusCount", "消耗牌堆里的状态牌数", Pile("ExhaustPile", "Status")),
        new FormulaBuiltin("ExhaustCurseCount",  "消耗牌堆里的诅咒牌数", Pile("ExhaustPile", "Curse")),
        // ===== ④ 自己的生命 / 格挡 / 资源 / 身上东西 =====
        // ⚠ 这一组带 `My` 前缀：参数页「取值来源」里已经有 `CurrentHp` / `MaxHp` / `Gold` / `Energy` /
        //   `MaxEnergy`（那几个**可以指定从谁身上取**，例如「指定敌人的最大生命」），名字撞了的话
        //   `ParamSources` 那个下拉会出现两个同名项、而且按 Id 取值永远只会命中前面那个。
        new FormulaBuiltin("MyHp",        "自己当前生命",       "((decimal){c}.CurrentHp)"),
        new FormulaBuiltin("MyMaxHp",     "自己最大生命",       "((decimal){c}.MaxHp)"),
        new FormulaBuiltin("MyHpMissing", "自己已损失的生命（最大生命 - 当前生命）",
            "((decimal)({c}.MaxHp - {c}.CurrentHp))"),
        new FormulaBuiltin("MyBlock",     "自己当前格挡",       "((decimal){c}.Block)"),
        new FormulaBuiltin("MyEnergy",    "当前能量（打出这张牌时扣完费之后的余量）",
            "((decimal)({p}?.PlayerCombatState?.Energy ?? 0))"),
        new FormulaBuiltin("MyEnergyMax", "当前能量上限（含战斗内的加成 / 修正）",
            "((decimal)({p}?.PlayerCombatState?.MaxEnergy ?? 0))"),
        new FormulaBuiltin("MyStars",     "当前额外资源量",     "((decimal)({p}?.PlayerCombatState?.Stars ?? 0))"),
        new FormulaBuiltin("MyGold",      "金币数",             "((decimal)({p}?.Gold ?? 0))"),
        new FormulaBuiltin("RelicCount",  "遗物数量",       "((decimal)({p}?.Relics.Count ?? 0))"),
        new FormulaBuiltin("PotionCount", "药水数量",       "((decimal)({p}?.Potions.Count() ?? 0))"),
        new FormulaBuiltin("PetCount",    "自己的伙伴（召唤物）数量",
            "((decimal)({p}?.PlayerCombatState?.Pets.Count ?? 0))"),
        new FormulaBuiltin("OrbCount",    "自己的充能球数量",
            "((decimal)({p}?.PlayerCombatState?.OrbQueue.Orbs.Count ?? 0))"),
        // ===== ⑤ 身上状态的数量（只数会对玩家显示的；本体内部状态不算）=====
        // ⚠ 这里必须 `.Where(...).Count()`，**不能**写 `.Powers.Count(x => ...)`：
        //   Creature.Powers 是 IReadOnlyList，它自己有一个 **Count 属性** —— 成员查找先命中那个属性，
        //   `Count(lambda)` 会直接编译不过（CS1955「不可调用的成员 Count」）。
        //   V0.2.95 起 PowersOnMe 就是这么写的（一直编不过，探针 `_probe/formulaprobe` 真编译抓出来的）。
        new FormulaBuiltin("PowersOnMe", "自己身上的状态数量（不含内部状态）",
            "((decimal){c}.Powers.Where(__fgP => __fgP.IsVisible).Count())",
            new[] { "({c}.Powers.Count(__fgP => __fgP.IsVisible))" }),
        new FormulaBuiltin("BuffsOnMe", "自己身上的增益数量（按当前层数判定）",
            "((decimal){c}.Powers.Where(__fgP => __fgP.TypeForCurrentAmount == PowerType.Buff).Count())"),
        new FormulaBuiltin("DebuffsOnMe", "自己身上的减益数量（按当前层数判定）",
            "((decimal){c}.Powers.Where(__fgP => __fgP.TypeForCurrentAmount == PowerType.Debuff).Count())"),
        // ===== ⑥ 敌人 =====
        new FormulaBuiltin("EnemyCount", "活着的敌人数",
            "({c}.CombatState is null ? 0m : (decimal){c}.CombatState.Enemies.Where(__fgX => __fgX.IsAlive).Count())"),
        new FormulaBuiltin("HittableEnemyCount", "可被攻击的敌人数（本体 HittableEnemies）",
            "({c}.CombatState is null ? 0m : (decimal){c}.CombatState.HittableEnemies.Count)"),
        new FormulaBuiltin("EnemyTotalHp", "活着的敌人的总生命",
            "({c}.CombatState is null ? 0m : (decimal){c}.CombatState.Enemies.Where(__fgX => __fgX.IsAlive).Sum(__fgX => __fgX.CurrentHp))"),
        // ===== ⑦ 跑图进度 / 跨战斗累计 =====
        new FormulaBuiltin("Floor",     "当前总层数（跑图进度）", "((decimal)({p}?.RunState?.TotalFloor ?? 0))"),
        new FormulaBuiltin("ActFloor",  "当前这一幕的层数",       "((decimal)({p}?.RunState?.ActFloor ?? 0))"),
        new FormulaBuiltin("ActNumber", "当前是第几幕（从 1 数）",
            "((decimal)(({p}?.RunState?.CurrentActIndex ?? 0) + 1))"),
        new FormulaBuiltin("Ascension", "升华（进阶）等级",       "((decimal)({p}?.RunState?.AscensionLevel ?? 0))"),
        new FormulaBuiltin("DamageDealtTotal", "本局累计造成的伤害（跨战斗）",
            "((decimal)({p}?.ExtraFields.DamageDealt ?? 0))"),
        new FormulaBuiltin("DebuffsAppliedTotal", "本局累计施加减益的次数（跨战斗）",
            "((decimal)({p}?.ExtraFields.DebuffsApplied ?? 0))"),
        // ===== ⑧ 本回合 / 本场战斗的计数（本体战斗历史）=====
        // 打出：CardPlaysStarted（本体在 OnPlay **之前**记）→ 正在打出的这一张也算在内。
        new FormulaBuiltin("PlayedThisTurn",   "本回合打出的牌数（含正在打出的这一张）", Plays(thisTurn: true,  null)),
        new FormulaBuiltin("PlayedThisCombat", "本场战斗打出的牌数",                     Plays(thisTurn: false, null)),
        new FormulaBuiltin("AttacksPlayedThisTurn",   "本回合打出的攻击牌数", Plays(thisTurn: true,  "Attack")),
        new FormulaBuiltin("AttacksPlayedThisCombat", "本场战斗打出的攻击牌数", Plays(thisTurn: false, "Attack")),
        new FormulaBuiltin("SkillsPlayedThisTurn",    "本回合打出的技能牌数", Plays(thisTurn: true,  "Skill")),
        new FormulaBuiltin("SkillsPlayedThisCombat",  "本场战斗打出的技能牌数", Plays(thisTurn: false, "Skill")),
        new FormulaBuiltin("PowersPlayedThisTurn",    "本回合打出的能力牌数", Plays(thisTurn: true,  "Power")),
        new FormulaBuiltin("PowersPlayedThisCombat",  "本场战斗打出的能力牌数", Plays(thisTurn: false, "Power")),
        new FormulaBuiltin("DrawnThisTurn",   "本回合抽到的牌数",   CountWhere("CardDrawnEntry", thisTurn: true)),
        new FormulaBuiltin("DrawnThisCombat", "本场战斗抽到的牌数", CountWhere("CardDrawnEntry", thisTurn: false)),
        new FormulaBuiltin("DiscardedThisTurn",     "本回合丢弃的牌数",   Count("CardDiscardedEntry", thisTurn: true)),
        new FormulaBuiltin("DiscardedThisCombat",   "本场战斗丢弃的牌数", Count("CardDiscardedEntry", thisTurn: false)),
        new FormulaBuiltin("ExhaustedThisTurn",     "本回合消耗的牌数",   Count("CardExhaustedEntry", thisTurn: true)),
        new FormulaBuiltin("ExhaustedThisCombat",   "本场战斗消耗的牌数", Count("CardExhaustedEntry", thisTurn: false)),
        // 用户要求「参数页也能监听复制了几张」：本体把复制记成**同一个** CardGenerated 记录，
        // 所以这两个读数和「生成」是同一个表达式 —— 列出来是为了让「复制的牌数」在参数页 / 公式清单里找得到。
        new FormulaBuiltin("GeneratedThisTurn",     "本回合生成的牌数（复制出来的也算）",   Generated(thisTurn: true)),
        new FormulaBuiltin("GeneratedThisCombat",   "本场战斗生成的牌数（复制出来的也算）", Generated(thisTurn: false)),
        new FormulaBuiltin("CopiedThisTurn",     "本回合复制的牌数（本体把复制记成「生成」，所以和上面同值）",   Generated(thisTurn: true)),
        new FormulaBuiltin("CopiedThisCombat",   "本场战斗复制的牌数（同上，和「生成」同值）", Generated(thisTurn: false)),
        // 伤害 / 格挡 / 击杀：同一条历史记录（DamageReceivedEntry / BlockGainedEntry）里就有数。
        // 「造成的伤害」只算**没被格挡**的那部分（Result.UnblockedDamage），而且排除打自己那一下
        //（自伤也是 Dealer = 自己，不该算进「你造成的伤害」）。
        new FormulaBuiltin("DamageDealtThisTurn", "本回合造成的伤害（只算没被格挡的，不含自伤）",
            SumWhere("DamageReceivedEntry", thisTurn: true,  "__fgE.Dealer == {c} && __fgE.Receiver != {c}", "__fgE.Result.UnblockedDamage")),
        new FormulaBuiltin("DamageDealtThisCombat", "本场战斗造成的伤害（只算没被格挡的，不含自伤）",
            SumWhere("DamageReceivedEntry", thisTurn: false, "__fgE.Dealer == {c} && __fgE.Receiver != {c}", "__fgE.Result.UnblockedDamage")),
        new FormulaBuiltin("DamageTakenThisTurn", "本回合受到的伤害（只算没被格挡的）",
            SumWhere("DamageReceivedEntry", thisTurn: true,  "__fgE.Receiver == {c}", "__fgE.Result.UnblockedDamage")),
        new FormulaBuiltin("DamageTakenThisCombat", "本场战斗受到的伤害（只算没被格挡的）",
            SumWhere("DamageReceivedEntry", thisTurn: false, "__fgE.Receiver == {c}", "__fgE.Result.UnblockedDamage")),
        new FormulaBuiltin("BlockGainedThisTurn",   "本回合获得的格挡",   SumWhere("BlockGainedEntry", thisTurn: true,  "__fgE.Receiver == {c}", "__fgE.Amount")),
        new FormulaBuiltin("BlockGainedThisCombat", "本场战斗获得的格挡", SumWhere("BlockGainedEntry", thisTurn: false, "__fgE.Receiver == {c}", "__fgE.Amount")),
        new FormulaBuiltin("EnemiesKilledThisTurn",   "本回合被自己打死的敌人数（按致死的那一下记）",
            CountWhere("DamageReceivedEntry", thisTurn: true,  "__fgE.Dealer == {c} && __fgE.Result.WasTargetKilled")),
        new FormulaBuiltin("EnemiesKilledThisCombat", "本场战斗被自己打死的敌人数（按致死的那一下记）",
            CountWhere("DamageReceivedEntry", thisTurn: false, "__fgE.Dealer == {c} && __fgE.Result.WasTargetKilled")),
        // 资源：能量花掉 / 拿到的本体现在就记（EnergySpentEntry）；「获得的能量」本体没有，只能自己打补丁记。
        new FormulaBuiltin("EnergySpentThisTurn",   "本回合花掉的能量",   Summed("EnergySpentEntry", thisTurn: true)),
        new FormulaBuiltin("EnergySpentThisCombat", "本场战斗花掉的能量", Summed("EnergySpentEntry", thisTurn: false)),
        // 额外资源量：一条记录里正数是获得、负数是花掉。
        new FormulaBuiltin("StarsGainedThisTurn",   "本回合获得的额外资源量",   Stars(thisTurn: true)),
        new FormulaBuiltin("StarsGainedThisCombat", "本场战斗获得的额外资源量", Stars(thisTurn: false)),
        new FormulaBuiltin("StarsSpentThisTurn",    "本回合花掉的额外资源量",
            SumWhere("StarsModifiedEntry", thisTurn: true,  "__fgE.Actor == {c} && __fgE.Amount < 0", "-__fgE.Amount")),
        new FormulaBuiltin("StarsSpentThisCombat",  "本场战斗花掉的额外资源量",
            SumWhere("StarsModifiedEntry", thisTurn: false, "__fgE.Actor == {c} && __fgE.Amount < 0", "-__fgE.Amount")),
        new FormulaBuiltin("SummonedThisTurn",   "本回合的召唤量（本体 SummonedEntry 里记的数）",
            SumWhere("SummonedEntry", thisTurn: true,  "__fgE.Actor == {c}", "__fgE.Amount")),
        new FormulaBuiltin("SummonedThisCombat", "本场战斗的召唤量（本体 SummonedEntry 里记的数）",
            SumWhere("SummonedEntry", thisTurn: false, "__fgE.Actor == {c}", "__fgE.Amount")),
        // 获得的能量：本体只把能量记成一个数（PlayerCombatState.Energy + EnergyChanged 事件），
        // **没有**「这回合 / 这场战斗获得了多少能量」这种读数，所以只能由工具自己打补丁来记
        // （见 CSharpCodeGen.EnergyGainTrackerSource）。
        // 语义：只数**真正额外获得**的能量 —— 回合开始把能量补满（ResetEnergy）、
        // AddMaxEnergyToCurrent 那种「回满」不算「获得」，否则这个读数每回合都会莫名多出 3 点。
        new FormulaBuiltin("EnergyGainedThisTurn",   "本回合获得的能量（回合开始补满的不算）",   Tracker("GainedThisTurn")),
        new FormulaBuiltin("EnergyGainedThisCombat", "本场战斗获得的能量（回合开始补满的不算）", Tracker("GainedThisCombat")),
        // ===== ⑨ 这张牌自己花掉的（V0.2.158）=====
        // 为什么需要（用户实测：「次数填 Y（额外资源量）时只会触发一次」）：
        // 本体是**先扣费、再跑效果**（PlayCardAction：`await _card.SpendResources()` → `OnPlayWrapper(...)`），
        // 所以「当前能量 / 当前额外资源量」在效果跑的那一刻读到的是**扣完剩下的** ——
        // 一张「Y 费」的牌读到 0（次数被夹到 1）；「X 费」的牌读能量同理。
        // 想用「这张牌花掉的那部分」就用这两个读数 —— 本体本来就为 X 费牌准备了这两个方法
        //（ResolveEnergyXValue() = 这次花掉的能量、ResolveStarXValue() = 这次花掉的额外资源量）。
        // ⚠ **不是 X 费的牌上它们会抛异常**（本体第一句就是
        //   `if (!EnergyCost.CostsX) throw new InvalidOperationException(...)` /
        //   `if (!HasStarCostX) throw ...`）—— 所以生成出来的表达式必须**先判再调**（三元），
        //   不然用户在普通牌上用一次 YSpent 就会在战斗中抛 InvalidOperationException（踩过）。
        new FormulaBuiltin("XSpent", "这张牌花掉的能量（X 费牌就是那个 X；不是 X 费 = 0）",
            "(base.EnergyCost.CostsX ? (decimal)base.ResolveEnergyXValue() : 0m)"),
        new FormulaBuiltin("YSpent", "这张牌花掉的额外资源量（Y 费牌就是那个 Y；不是 Y 费 = 0）",
            "(base.HasStarCostX ? (decimal)base.ResolveStarXValue() : 0m)"),
        // ===== ⑩ 这张牌**这一次打出**造成的伤害（V0.2.194，用户问的「荒疫打击」）=====
        // 由来：本体 BlightStrike（荒疫打击）是「造成 8 点伤害。给予等量于所造成伤害的灾厄。」，
        // 灾厄层数取的是 `attackCommand.Results.SelectMany(r => r).Sum(r => r.TotalDamage)`
        // —— 也就是**这一下打出的伤害**（含被格挡的部分：TotalDamage = BlockedDamage + UnblockedDamage）。
        //
        // 我们读的是卡片身上的一个字段 `ForgeThisCardDamage`：生成端在 OnPlay 开头把它清零、
        // 每条「造成伤害」之后累加（见 CSharpCodeGen 的 usesThisCardDamage / EmitAttack）。于是：
        //   · 每次打出各算一次（回手再打 / 重放 / 复制出来的第二张互不影响）；
        //   · 跟回合、历史、中毒跳伤、遗物、别的牌打的伤害**完全无关**（不去查战斗历史）；
        //   · 只算走 `DamageCmd.Attack` 的普通攻击（伙伴攻击 / 对自己造成伤害 / 中毒灾厄跳伤不算）。
        //
        // ⚠ CardOnly: 这条表达式里的 `this` 必须是卡片（CardModel 的子类）—— 遗物 / 药水 / 自定义状态上
        //   编不过，校验器会拦住，也不进「自定义参数 / 组」的取值来源下拉（理由见 FormulaBuiltin 的注释）。
        // ⚠ 表达式里用 **`this.`**（不是 `base.`）：这条读数读的是**派生卡片类**上的属性
        //   （`ForgeThisCardDamage`），而 `base` 在卡片类里指的是本体 CardModel —— 写 `base.…`
        //   直接 CS0117「CardModel 未包含该定义」（探针真编译抓出来的；XSpent / YSpent 能用 `base.`
        //   是因为 EnergyCost / ResolveEnergyXValue 本来就是 CardModel 的成员）。
        //   代价：卡面那个「现算预览值」的静态 lambda 里没有 `this`（它只有 `card` 参数，类型是 CardModel），
        //   所以**含这条读数的公式不声明预览变量**（见 CSharpCodeGen.VarDeclarationCore），
        //   卡面文案对「施加状态」那一档另做特判（印「等量于所造成伤害」、不印数字）。
        new FormulaBuiltin("ThisCardDamage",
            "本张牌这一次打出造成的伤害（含被格挡的；只算普通攻击，和本体「荒疫打击」同口径）",
            "((decimal)this.ForgeThisCardDamage)",
            CardOnly: true),
        // ===== ⑪ 这张牌**这一次打出**获得的格挡（V0.2.194，用户问的「能不能加一个这张牌获得的格挡」）=====
        // 和上面那条是一对（同一个属性机制：打出时清零、每条格挡效果之后累加）。口径取本体
        // `CreatureCmd.GainBlock(...)` 的**返回值** —— 它是走完 `ModifyBlock` 之后**实际拿到**的格挡
        //（CreatureCmd.cs:680-700），所以敏捷 / 遗物 / 状态那些格挡加成也算在内，跟卡面写的数字不一定相等。
        //   · 只算「获得格挡」这一条效果真拿到的那部分（给队友 / 敌人的格挡也算）；
        //   · 「失去格挡」（负数那种走 LoseBlock）、「下回合生效」（挂 BlockNextTurnPower）、
        //     牺牲伙伴换来的格挡、状态 / 遗物给的格挡 —— 一律不算；
        //   · 每次打出各算一次。
        new FormulaBuiltin("ThisCardBlock",
            "本张牌这一次打出获得的格挡（实际拿到多少；含敏捷等加成，只算「获得格挡」效果）",
            "((decimal)this.ForgeThisCardBlock)",
            CardOnly: true),
        // ===== ⑫ 四个「张数」读数（V0.2.241，用户问的「这张卡丢弃 / 消耗 / 变化 / 生成的卡牌数」）=====
        // 和上面两条同一套属性机制（打出时清零、每条对应效果之后累加），只是记的是**张数**。
        // 为什么不用战斗历史：本体那几张记录（CardExhaustedEntry / CardDiscardedEntry / CardGeneratedEntry）
        //   **只带卡、不带「哪一次打出」**，根本分不出是谁干的；「变化」更是压根没有历史记录 ——
        //   所以只能在生成出来的代码里逐次记账（每个操作点 +1 / +列表张数）。
        // 口径：只算「这张牌这一次打出」自己那几条效果；「下回合生效」、状态 / 遗物 / 别的牌做的都不算。
        new FormulaBuiltin("ThisCardExhaust",
            "本张牌这一次打出消耗的卡牌数（只算「消耗卡牌」这条效果；数值 0 = 消耗这张牌自己也算 1）",
            "((decimal)this.ForgeThisCardExhaust)",
            CardOnly: true),
        new FormulaBuiltin("ThisCardDiscard",
            "本张牌这一次打出丢弃的卡牌数（只算「丢弃卡牌」这条效果；数值 0 = 丢弃这张牌自己也算 1）",
            "((decimal)this.ForgeThisCardDiscard)",
            CardOnly: true),
        new FormulaBuiltin("ThisCardTransform",
            "本张牌这一次打出变化的卡牌数（只算「变化卡牌」这条效果）",
            "((decimal)this.ForgeThisCardTransform)",
            CardOnly: true),
        new FormulaBuiltin("ThisCardGenerate",
            "本张牌这一次打出生成的卡牌数（只算「生成卡牌」这条效果；「复制卡牌」不算）",
            "((decimal)this.ForgeThisCardGenerate)",
            CardOnly: true),
    };

    /// <summary>按名字找一条内置量（大小写不敏感；<c>M</c>/<c>N</c> 这种单字母参数不会和它们撞）。</summary>
    public static FormulaBuiltin? Find(string? name)
    {
        string key = (name ?? "").Trim();
        return key.Length == 0 ? null : All.FirstOrDefault(b => string.Equals(b.Id, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>内置名字的清单（错误提示用）。</summary>
    public static string Names => string.Join(" / ", All.Select(b => b.Id));

    /// <summary>内置名字 + 中文说明（界面提示用）。</summary>
    public static string HintList => string.Join("，", All.Select(b => $"{b.Id}（{b.Display}）"));

    // ---- 下面几个是上面那张表用的表达式拼装（读起来比一长串内联字符串清楚）。
    //      注意这里**不能**用内插字符串：{p} / {c} 是要留给 FormulaBuiltin.Expr 替换的占位符。

    // ===== 牌堆 / 牌组 =====
    // ⚠ 按类型过滤一律写 `.Where(...).Count()`：Cards 是 IReadOnlyList，自带 Count **属性** ——
    //   写 `.Cards.Count(x => …)` 会先命中那个属性，直接 CS1955（探针 `_probe/formulaprobe` 真编译抓出来的）。

    /// <summary>跨战斗的**牌组**（不是战斗里的牌堆 —— 那个要 <c>PlayerCombatState</c>，战斗外是 null）。</summary>
    private static string Deck(string? cardType = null) => cardType is null
        ? "(decimal)({p}.Deck.Cards.Count)"
        : "(decimal)({p}.Deck.Cards.Where(__fgC => __fgC.Type == CardType." + cardType + ").Count())";

    /// <summary>
    /// 战斗里**手牌**的张数（可按类型过滤）。
    /// 战斗外手牌根本不存在（<c>PlayerCombatState</c> 是 null）→ 读成 0，**不抛异常**。
    /// </summary>
    private static string Hand(string? cardType) => cardType is null
        ? "(decimal)({p}?.PlayerCombatState?.Hand?.Cards.Count ?? 0)"
        : "(decimal)({p}?.PlayerCombatState?.Hand?.Cards.Where(__fgC => __fgC.Type == CardType." + cardType + ").Count() ?? 0)";

    /// <summary>
    /// V0.2.368（用户要求）：战斗里**抽牌堆 / 弃牌堆 / 消耗牌堆**的张数，也能按牌型过滤。
    /// 以前这三摞只有总张数（DrawCount / DiscardCount / ExhaustCount），
    /// 「按弃牌堆里的攻击牌数造成伤害」这种就得自己写整段 C#。
    /// <paramref name="pileProp"/> = PlayerCombatState 上的属性名（DrawPile / DiscardPile / ExhaustPile）。
    /// 战斗外读成 0（和 Hand 同一个口径，不抛异常）。
    /// </summary>
    private static string Pile(string pileProp, string? cardType) => cardType is null
        ? "(decimal)({p}?.PlayerCombatState?." + pileProp + "?.Cards.Count ?? 0)"
        : "(decimal)({p}?.PlayerCombatState?." + pileProp + "?.Cards.Where(__fgC => __fgC.Type == CardType." + cardType + ").Count() ?? 0)";

    // ===== 战斗历史 =====

    /// <summary>「本回合」那半句判据（本体口径就是 CombatHistoryEntry.HappenedThisTurn）。</summary>
    private static string Turn(bool thisTurn) =>
        thisTurn ? " && __fgE.HappenedThisTurn({p}?.Creature?.CombatState)" : "";

    /// <summary>历史的入口 + 这场战斗的兜底：不在战斗里（拿不到 CombatState）直接读成 0。</summary>
    private static string Guard(string body) =>
        "(({p}?.Creature?.CombatState) is null ? 0m : (decimal)(" + body + "))";

    /// <summary>「数一下历史里某类条目有几条」（只数自己做的那些；thisTurn = 只看这一回合）。</summary>
    private static string Count(string entry, bool thisTurn)
    {
        string turn = thisTurn ? " && __fgE.HappenedThisTurn({p}?.Creature?.CombatState)" : "";
        return "(({p}?.Creature?.CombatState) is null ? 0m : (decimal)CombatManager.Instance.History.Entries"
             + ".OfType<" + Hist + entry + ">()"
             + ".Count(__fgE => __fgE.Actor == {c}" + turn + "))";
    }

    /// <summary>「数一下历史里某类条目有几条」（自己写判据 —— 用 <c>__fgE</c> 引用那一条；默认「自己做的那些」）。</summary>
    private static string CountWhere(string entry, bool thisTurn, string where = "__fgE.Actor == {c}") =>
        Guard("CombatManager.Instance.History.Entries.OfType<" + Hist + entry + ">()"
            + ".Count(__fgE => " + where + Turn(thisTurn) + ")");

    /// <summary>
    /// 「打出」的计数：<c>CardPlaysStarted</c>（本体在 OnPlay **之前**记一条）——
    /// 所以正在打出的这一张**算在内**（用 <c>CardPlaysFinished</c> 会少算这一张）。
    /// </summary>
    private static string Plays(bool thisTurn, string? cardType)
    {
        string filter = cardType is null ? "" : " && __fgE.CardPlay.Card.Type == CardType." + cardType;
        return Guard("CombatManager.Instance.History.CardPlaysStarted"
            + ".Count(__fgE => __fgE.Actor == {c}" + filter + Turn(thisTurn) + ")");
    }

    /// <summary>「生成卡牌」：本体把玩家主动生成的和敌人塞的都记一条，靠 Creator 区分（复制出来的也算生成）。</summary>
    private static string Generated(bool thisTurn)
    {
        string turn = thisTurn ? " && __fgE.HappenedThisTurn({p}?.Creature?.CombatState)" : "";
        return "(({p}?.Creature?.CombatState) is null ? 0m : (decimal)CombatManager.Instance.History.Entries"
             + ".OfType<" + Hist + "CardGeneratedEntry>()"
             + ".Count(__fgE => __fgE.Creator == {p}" + turn + "))";
    }

    /// <summary>「花掉的能量」：一条记录里的 Amount 就是这一次花的点数，直接求和。</summary>
    private static string Summed(string entry, bool thisTurn)
    {
        string turn = thisTurn ? " && __fgE.HappenedThisTurn({p}?.Creature?.CombatState)" : "";
        return "(({p}?.Creature?.CombatState) is null ? 0m : (decimal)CombatManager.Instance.History.Entries"
             + ".OfType<" + Hist + entry + ">()"
             + ".Where(__fgE => __fgE.Actor == {c}" + turn + ")"
             + ".Sum(__fgE => __fgE.Amount))";
    }

    /// <summary>「把历史里某类条目的某个数加起来」（自己写判据与被加的那个数）。</summary>
    private static string SumWhere(string entry, bool thisTurn, string where, string amount) =>
        Guard("CombatManager.Instance.History.Entries.OfType<" + Hist + entry + ">()"
            + ".Where(__fgE => " + where + Turn(thisTurn) + ")"
            + ".Sum(__fgE => " + amount + ")");

    /// <summary>
    /// 「获得的能量」：本体没有这个读数，靠工具生成的 <c>ForgeEnergyGainTracker</c>（打在本体**同步**方法
    /// <c>PlayerCombatState.GainEnergy(decimal)</c> 上的后缀补丁）来记 —— 类名是**固定**的，
    /// 因为这个表达式要写进内置量表（每个角色都长一样），生成时按角色决定要不要写那个文件。
    /// </summary>
    private static string Tracker(string member) => $"((decimal)ForgeEnergyGainTracker.{member}({{p}}))";

    /// <summary>「获得的额外资源量」：同一条记录正数是获得、负数是花掉 —— 只数正数。</summary>
    private static string Stars(bool thisTurn)
    {
        string turn = thisTurn ? " && __fgE.HappenedThisTurn({p}?.Creature?.CombatState)" : "";
        return "(({p}?.Creature?.CombatState) is null ? 0m : (decimal)CombatManager.Instance.History.Entries"
             + ".OfType<" + Hist + "StarsModifiedEntry>()"
             + ".Where(__fgE => __fgE.Actor == {c} && __fgE.Amount > 0" + turn + ")"
             + ".Sum(__fgE => __fgE.Amount))";
    }
    /// <summary>
    /// 全部变量 = **内置** + **插件贡献的**（V0.2.368：插件清单里的 <c>formulaVariables[]</c>）。
    ///
    /// <para>为什么现算：插件是启动时扫描加载的，静态字段会在「插件还没加载」时算一次然后永远少那几条
    /// （V0.2.367 的 ForgeTimings 踩过同一个坑）。插件变量的 <c>{p}</c> / <c>{c}</c> 和内置同一套占位符，
    /// 生成端在 <see cref="AllExprs(string,string)"/> 里替换。</para>
    /// </summary>
    public static IReadOnlyList<FormulaBuiltin> All
    {
        get
        {
            var extra = PluginVariables();
            if (extra.Count == 0) return Core;
            var list = new List<FormulaBuiltin>(Core.Count + extra.Count);
            list.AddRange(Core);
            list.AddRange(extra);
            return list;
        }
    }

    /// <summary>插件贡献的变量（清单里 <c>formulaVariables</c> 那几条；重名的跳过，内置优先）。</summary>
    private static List<FormulaBuiltin> PluginVariables()
    {
        var list = new List<FormulaBuiltin>();
        foreach (var pl in Plugins.PluginRegistry.Plugins.Where(x => x.Enabled && x.Problems.Count == 0))
        {
            foreach (var v in pl.FormulaVariables.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Id)))
            {
                string id = v.Id.Trim();
                if (Core.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))) continue;
                if (list.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(new FormulaBuiltin(id,
                    string.IsNullOrWhiteSpace(v.Display) ? "插件变量：" + id : v.Display.Trim(),
                    (v.CSharp ?? "").Trim(),
                    // 旧写法也认这一条：从工程反推存档时同一条表达式要能认回来
                    new[] { (v.CSharp ?? "").Trim() }));
            }
        }
        return list;
    }
}
