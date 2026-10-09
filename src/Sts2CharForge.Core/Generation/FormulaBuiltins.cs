using System;
using System.Collections.Generic;
using System.Linq;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 「数值公式」里能直接写的**只读量**（用户要求：参数监听 —— 回合数、本回合 / 本场战斗丢了多少张牌、
/// 消耗了多少张、生成了多少张、花了多少能量、拿了多少额外资源量、身上有几个状态）。
///
/// 为什么不做成「自定义参数」：参数要在「自定义参数 / 组」页先定义再引用，而这些量是**运行时的实时读数**，
/// 每次求值都不一样，定义一次没有意义 —— 所以它们直接写名字就能用（和 <c>X</c> / <c>Y</c> 一样）。
///
/// 数据来源**几乎**都是本体现成的 API（不需要额外补丁 / 记录器）：
///   · 回合数 / 轮数：<c>PlayerCombatState.TurnNumber</c> / <c>ICombatState.RoundNumber</c>；
///   · 牌堆张数：<c>CardPile.GetCards(player, PileType.Hand)</c> / <c>PileType.X.GetPile(player).Cards</c>；
///   · 丢弃 / 消耗 / 生成：<c>CombatManager.Instance.History</c> 里的
///     <c>CardDiscardedEntry</c> / <c>CardExhaustedEntry</c> / <c>CardGeneratedEntry</c>
///     （本体在每个动作发生时都会记一条，见 <c>CombatHistory</c>）；
///   · 花掉的能量 / 拿到的额外资源量：同上的 <c>EnergySpentEntry</c> / <c>StarsModifiedEntry</c>（只数正数）；
///   · 身上的状态数量：<c>Creature.Powers</c>（只数**会对玩家显示**的那些，本体内部状态不算）。
/// 例外是「获得的能量」：本体压根没记，只能自己打补丁记（用到才生成 <c>ForgeEnergyGainTracker</c>，
/// 见 <c>CSharpCodeGen.EnergyGainTrackerSource</c>）。
///
/// <c>{p}</c> / <c>{c}</c> 是占位符：生成端分别填「玩家表达式」和「自己那个生物表达式」
/// （卡牌那边是 <c>__fgPlayer</c> / <c>__fgSelf</c>）。
///
/// ⚠ 「复制出来的牌」在本体的历史里**也算「生成」**（复制走的是同一个
/// <c>CardPileCmd.AddGeneratedCardToCombat</c> → <c>History.CardGenerated</c>），
/// 所以没有单独的「复制张数」——界面上和文档里都写明了这一点。
/// </summary>
public sealed record FormulaBuiltin(string Id, string Display, string CSharp)
{
    /// <summary>把表达式里的占位符换成真正的 C# 片段。</summary>
    public string Expr(string player, string creature) =>
        CSharp.Replace("{p}", player).Replace("{c}", creature);
}

public static class FormulaBuiltins
{
    /// <summary>历史记录里那些条目的完整类型名（生成的工程没有 using 那个命名空间，所以写全名）。</summary>
    private const string Hist = "MegaCrit.Sts2.Core.Combat.History.Entries.";

    public static IReadOnlyList<FormulaBuiltin> All { get; } = new[]
    {
        // ===== 回合 / 轮 =====
        new FormulaBuiltin("Turn",  "自己这一侧的第几回合", "({p}?.PlayerCombatState?.TurnNumber ?? 0)"),
        new FormulaBuiltin("Round", "当前战斗轮数（所有单位各走一次算一轮）", "({p}?.Creature?.CombatState?.RoundNumber ?? 0)"),
        // ===== 牌堆张数 =====
        new FormulaBuiltin("HandCount",    "手牌张数",     "(CardPile.GetCards({p}, PileType.Hand).Count())"),
        new FormulaBuiltin("DrawCount",    "抽牌堆张数",   "(PileType.Draw.GetPile({p}).Cards.Count())"),
        new FormulaBuiltin("DiscardCount", "弃牌堆张数",   "(PileType.Discard.GetPile({p}).Cards.Count())"),
        new FormulaBuiltin("ExhaustCount", "消耗牌堆张数", "(PileType.Exhaust.GetPile({p}).Cards.Count())"),
        // ===== 本回合 / 本场战斗的计数（本体历史记录）=====
        new FormulaBuiltin("DiscardedThisTurn",     "本回合丢弃的牌数",   Count("CardDiscardedEntry", thisTurn: true)),
        new FormulaBuiltin("DiscardedThisCombat",   "本场战斗丢弃的牌数", Count("CardDiscardedEntry", thisTurn: false)),
        new FormulaBuiltin("ExhaustedThisTurn",     "本回合消耗的牌数",   Count("CardExhaustedEntry", thisTurn: true)),
        new FormulaBuiltin("ExhaustedThisCombat",   "本场战斗消耗的牌数", Count("CardExhaustedEntry", thisTurn: false)),
        new FormulaBuiltin("GeneratedThisTurn",     "本回合生成的牌数（复制出来的也算）",   Generated(thisTurn: true)),
        new FormulaBuiltin("GeneratedThisCombat",   "本场战斗生成的牌数（复制出来的也算）", Generated(thisTurn: false)),
        // 用户要求「参数页也能监听复制了几张」：本体把复制记成**同一个** CardGenerated 记录，
        // 所以这两个读数和上面两个是同一个表达式 —— 列出来是为了让「复制的牌数」在参数页 / 公式清单里找得到。
        new FormulaBuiltin("CopiedThisTurn",     "本回合复制的牌数（本体把复制记成「生成」，所以和上面同值）",   Generated(thisTurn: true)),
        new FormulaBuiltin("CopiedThisCombat",   "本场战斗复制的牌数（同上，和「生成」同值）", Generated(thisTurn: false)),
        new FormulaBuiltin("EnergySpentThisTurn",   "本回合花掉的能量",   Summed("EnergySpentEntry", thisTurn: true)),
        new FormulaBuiltin("EnergySpentThisCombat", "本场战斗花掉的能量", Summed("EnergySpentEntry", thisTurn: false)),
        // 额外资源量：一条记录里正数是获得、负数是花掉 —— 这里只数**获得**的（用户要的是「获得」）
        new FormulaBuiltin("StarsGainedThisTurn",   "本回合获得的额外资源量",   Stars(thisTurn: true)),
        new FormulaBuiltin("StarsGainedThisCombat", "本场战斗获得的额外资源量", Stars(thisTurn: false)),
        // 获得的能量：本体只把能量记成一个数（PlayerCombatState.Energy + EnergyChanged 事件），
        // **没有**「这回合 / 这场战斗获得了多少能量」这种读数，所以只能由工具自己打补丁来记
        // （见 CSharpCodeGen.EnergyGainTrackerSource）。
        // 语义：只数**真正额外获得**的能量 —— 回合开始把能量补满（ResetEnergy）、
        // AddMaxEnergyToCurrent 那种「回满」不算「获得」，否则这个读数每回合都会莫名多出 3 点。
        new FormulaBuiltin("EnergyGainedThisTurn",   "本回合获得的能量（回合开始补满的不算）",   Tracker("GainedThisTurn")),
        new FormulaBuiltin("EnergyGainedThisCombat", "本场战斗获得的能量（回合开始补满的不算）", Tracker("GainedThisCombat")),
        // ===== 身上的状态数量 =====
        // 只数**会对玩家显示**的那些（本体内部状态 IsVisible=false，不该算进「身上有几个状态」）。
        new FormulaBuiltin("PowersOnMe", "自己身上的状态数量（不含内部状态）",
            "({c}.Powers.Count(__fgP => __fgP.IsVisible))"),
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

    /// <summary>「数一下历史里某类条目有几条」（只数自己做的那些；thisTurn = 只看这一回合）。</summary>
    private static string Count(string entry, bool thisTurn)
    {
        string turn = thisTurn ? " && __fgE.HappenedThisTurn({p}?.Creature?.CombatState)" : "";
        return "(({p}?.Creature?.CombatState) is null ? 0m : (decimal)CombatManager.Instance.History.Entries"
             + ".OfType<" + Hist + entry + ">()"
             + ".Count(__fgE => __fgE.Actor == {c}" + turn + "))";
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
}
