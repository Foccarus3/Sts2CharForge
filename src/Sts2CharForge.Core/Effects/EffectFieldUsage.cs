using Sts2CharForge.Core.Generation;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Effects;

/// <summary>一个「栏位探针」：把这条效果的这个栏位改成特征值，看生成出来的代码变不变。</summary>
/// <param name="Name">字段名（自检 / 探针报告里用）。</param>
/// <param name="Display">中文名（给人看的）。</param>
/// <param name="Set">把特征值写进去。</param>
public sealed record EffectFieldProbe(string Name, string Display, Action<EffectSpec> Set);

/// <summary>
/// 【机械测量器】「某一种效果，生成端到底读不读某一栏」—— 全部由**实测**决定，不靠人读代码。
///
/// <para>它替 <see cref="EffectFieldPolicy"/> 提供依据，也是自检里「显示的都被用到、藏起来的都没用到」
/// 那条断言的测量端；命令行 <c>forge --fieldmatrix</c> 打的就是它。</para>
///
/// <para><b>做法</b>：同一条效果造两份 —— 基线（所有可选栏位都不填 / 填一个「有效但不特殊」的值）
/// 和变体（把要测的那一栏改成特征值），各自生成一次源码，**逐字符比对**。
/// 两份不一样 ⇒ 生成端读了这一栏。</para>
///
/// <para><b>三个坑（第一版探针全踩过，改这里务必保住）</b>：
/// ① CET 标记行（<c>// CET:...</c>，给 <c>--recover</c> 用的记录）必须**整行删掉** —— 它把栏位值原样写进去，
///    不删的话每个栏位都「读」；而且「多出一行标记」也会被当成差异，所以是整行删除而不是留空行。
/// ② 基线必须**进得了正常分支**：<c>给予关键词</c> 没选关键词、<c>获得药水</c> 没选药水时会走
///    「这条效果什么都不做」的早退分支，于是**别的栏位全都测不出来**（基线里给上有效值才对）。
/// ③ 特征值要真的能区分：测「作用对象」用 <c>Self</c> 而不是 <c>AllEnemies</c> ——
///    遗物链上 <c>Enemy</c> 和 <c>AllEnemies</c> 生成的是同一句（都是全体敌人），拿它测会误判成「没读」。</para>
///
/// <para>已知的、故意留在清单外的几栏（测出来恒为「没读」，但那是测量口径问题，不是生成端不读）：
/// <c>数值（公式）</c> 在非公式种类上由 <c>ProfileFactory.FoldPlainConstantAmountFormulas</c>
/// 在**读档时**折进固定数值（生成那一刻已经是空的了）；<c>缓慢百分比</c> / <c>临时增益撤销时机</c>
/// 的显隐跟的是「增益 / 减益 选的是不是那个状态」，不是效果种类。</para>
/// </summary>
public static class EffectFieldUsage
{
    public const string CardChain = "card";
    public const string RelicChain = "relic";
    public const string PotionChain = "potion";

    /// <summary>每个栏位一条探针（顺序 = 报告里的顺序）。</summary>
    public static IReadOnlyList<EffectFieldProbe> Probes { get; } = new EffectFieldProbe[]
    {
        new("Amount", "数值", s => s.Amount = 111m),
        new("UpgradeAmount", "升级增量", s => s.UpgradeAmount = 112m),
        new("AmountFormula", "数值（公式）", s => s.AmountFormula = "113"),
        new("TargetSide", "作用对象", s => s.TargetSide = "Self"),
        new("RepeatCount", "命中次数（固定）", s => s.RepeatCount = 114),
        new("RepeatFormula", "命中次数（公式）", s => s.RepeatFormula = "115"),
        new("Times", "生效次数", s => s.Times = 116),
        new("TimesFormula", "生效次数（公式）", s => s.TimesFormula = "117"),
        new("ChanceEnabled", "概率生效（勾选）", s => s.ChanceEnabled = true),
        new("ChancePercent", "概率生效（百分比）", s => { s.ChanceEnabled = true; s.ChancePercent = 118m; }),
        new("ChanceIndependent", "概率各自独立", s => { s.ChanceEnabled = true; s.ChancePercent = 50m; s.ChanceIndependent = true; }),
        new("ChanceFormula", "概率（公式）", s => { s.ChanceEnabled = true; s.ChanceFormula = "120"; }),
        new("NextTurn", "下回合生效", s => s.NextTurn = true),
        new("PowerId", "增益 / 减益", s => s.PowerId = "VulnerablePower"),
        new("SpawnCardId", "目标卡", s => s.SpawnCardId = "<card>"),
        new("SpawnTo", "生成到", s => s.SpawnTo = "Exhaust"),
        new("CardPick", "选牌方式", s => s.CardPick = "Chosen"),
        new("SelectPile", "从哪里选牌", s => s.SelectPile = "Discard"),
        new("PlaceTo", "放到哪一摞", s => s.PlaceTo = "Exhaust"),
        new("CountMode", "取几张", s => { s.CardPick = "Chosen"; s.CountMode = "UpTo"; }),
        new("GivenKeyword", "给予关键词", s => s.GivenKeyword = "Retain"),
        new("TempKeyword", "临时关键词", s => { s.GivenKeyword = "Retain"; s.TempKeyword = true; }),
        new("BoostStat", "强化什么", s => { s.SpawnCardId = "<card>"; s.BoostStat = "Block"; }),
        new("SpawnPick", "取卡方式", s => { s.SpawnPick = "Random"; s.SpawnCardId = ""; }),
        new("SpawnFilter", "范围限定 / 范围界定", s => { s.SpawnPick = "Random"; s.SpawnFilter = "Attack"; }),
        new("SpawnChosen", "从范围里自己挑", s => { s.SpawnPick = "Random"; s.SpawnChosen = true; }),
        new("SpawnChoice", "多选1", s => { s.SpawnPick = "Random"; s.SpawnChoice = 121m; }),
        new("SpawnUpgraded", "附加处理：升级", s => { s.SpawnPick = "Random"; s.SpawnUpgraded = true; }),
        new("SpawnFree", "附加处理：本场免费", s => { s.SpawnPick = "Random"; s.SpawnFree = true; }),
        new("SpawnFreeThisTurn", "附加处理：本回合免费", s => { s.SpawnPick = "Random"; s.SpawnFreeThisTurn = true; }),
        new("SpawnUpgradedThisTurn", "附加处理：本回合升级", s => { s.SpawnPick = "Random"; s.SpawnUpgradedThisTurn = true; }),
        new("SpawnKeyword", "附加处理：加关键词", s => { s.SpawnPick = "Random"; s.SpawnKeyword = "Retain"; }),
        new("PotionPick", "取药方式", s => s.PotionPick = "Random"),
        new("PotionId", "目标药水", s => s.PotionId = "<potion>"),
        new("Copies", "复制的份数", s => s.Copies = 122),
        new("CopiesFormula", "复制的份数（公式）", s => { s.Copies = 1; s.CopiesFormula = "123"; }),
        new("ReplayTimes", "重放次数", s => s.ReplayTimes = 124),
        new("ReplayFormula", "重放次数（公式）", s => { s.ReplayTimes = 1; s.ReplayFormula = "125"; }),
        new("ScrapeCond", "刮削条件", s => s.ScrapeCond = "Attack"),
        new("ScrapeOp", "刮削执行", s => s.ScrapeOp = EffectCatalog.ScrapeOpUpgrade),
        new("SlowPercent", "缓慢（百分比）", s => { s.PowerId = "SlowPower"; s.SlowPercent = 126m; }),
        new("TempPowerReverse", "临时增益撤销时机", s => { s.PowerId = "StrengthPower"; s.TempPowerReverse = TempPowerReverses.NextTurnStart; }),
        new("PetSummon", "召唤物（哪一只）", s => s.PetSummon = "ProbePet"),
        new("PetSacrificeGain", "牺牲伙伴：收益", s => s.PetSacrificeGain = "Damage"),
        new("PetSacrificeFormula", "牺牲伙伴：收益公式", s => s.PetSacrificeFormula = "CurHp"),
        new("PetSacrificeMultiplier", "牺牲伙伴：倍率", s => { s.PetSacrificeFormula = "MaxHp"; s.PetSacrificeMultiplier = 127m; }),
        new("Condition", "条件选项", s => s.Condition = new ConditionSpec { Kind = "HandAtLeast", Amount = 130m, WhenUnmet = "Skip" }),
    };

    /// <summary>
    /// 造一个**只给测量用**的探针存档：卡 / 遗物 / 药水三种容器都先清空，测量时各自只放一条。
    /// 从 <see cref="ProfileFactory.Sample"/> 复制，环境路径（解包工程 / 游戏 data）沿用示例存档的自动探测。
    /// </summary>
    public static CharacterProfile CreateScratch()
    {
        CharacterProfile p = ProfileFactory.Sample();
        p.Cards.Clear();
        p.Relics.Clear();
        p.Potions.Clear();
        return p;
    }

    /// <summary>基线效果：所有可选栏位都用「有效但不特殊」的值（见类注释坑②）。</summary>
    public static EffectSpec Baseline(string kind, string probePotionId = "FirePotion") => new()
    {
        Kind = kind,
        Amount = 1m,
        TargetSide = "Enemy",
        PowerId = "WeakPower",
        Times = 1,
        RepeatCount = 1,
        Copies = 1,
        ReplayTimes = 1,
        SpawnChoice = 1m,
        PetSacrificeMultiplier = 3m,
        GivenKeyword = "Exhaust",     // 不填的话「给予关键词」整条走早退分支
        PotionId = probePotionId,     // 同上，「获得药水」
    };

    /// <summary>CET 标记行整行删掉（见类注释坑①）。</summary>
    public static string StripMarkers(string source) => string.Join("\n", source.Split('\n')
        .Select(line =>
        {
            int at = line.IndexOf("// CET:", StringComparison.Ordinal);
            if (at < 0) return line;
            return string.IsNullOrWhiteSpace(line[..at]) ? "\u0000DROP" : line[..at];
        })
        .Where(line => line != "\u0000DROP"));

    /// <summary>按链生成这一条效果的源码（探针卡 / 探针遗物 / 探针药水）。失败返回 <c>ERR:...</c>。</summary>
    public static string SourceFor(CharacterProfile scratch, string chain, string kind, EffectSpec effect, string probeCardId)
    {
        try
        {
            if (chain == CardChain)
            {
                var card = new CardSpec
                {
                    Name = "探针" + kind, ClassName = "Probe" + kind, CardType = "Skill", Rarity = "Common", Cost = 1,
                };
                card.Effects.Add(effect);
                scratch.Cards.Clear();
                scratch.Cards.Add(card);
                return StripMarkers(CSharpCodeGen.CardSource(scratch, card, 0));
            }
            if (chain == RelicChain)
            {
                var relic = new RelicSpec
                {
                    Name = "探针" + kind, ClassName = "ProbeR" + kind, Rarity = "Common", Trigger = "PlayerTurnStart",
                };
                relic.Effects.Add(effect);
                scratch.Relics.Clear();
                scratch.Relics.Add(relic);
                return StripMarkers(CSharpCodeGen.RelicSource(scratch, relic, 0));
            }
            var potion = new PotionSpec
            {
                Name = "探针" + kind, ClassName = "ProbeP" + kind, Rarity = "Common",
                Usage = "CombatOnly", TargetType = "AllEnemies",
            };
            potion.Effects.Add(effect);
            scratch.Potions.Clear();
            scratch.Potions.Add(potion);
            return StripMarkers(CSharpCodeGen.PotionSource(scratch, potion, 0));
        }
        catch (Exception ex)
        {
            return "ERR:" + ex.GetType().Name + ":" + ex.Message;
        }
    }

    /// <summary>
    /// 量一种效果在一条链上每一栏「读没读」。<paramref name="baselineError"/> 非空 = 基线本身生成失败
    /// （那这一轮全部栏位的结论都不可信，调用方应该报出来）。
    /// </summary>
    public static IReadOnlyList<(EffectFieldProbe Probe, bool Read)> Measure(
        CharacterProfile scratch, string chain, string kind, string probeCardId, string probePotionId,
        out string? baselineError)
    {
        string baseline = SourceFor(scratch, chain, kind, Baseline(kind, probePotionId), probeCardId);
        baselineError = baseline.StartsWith("ERR:", StringComparison.Ordinal) ? baseline : null;

        var result = new List<(EffectFieldProbe, bool)>(Probes.Count);
        foreach (EffectFieldProbe probe in Probes)
        {
            EffectSpec variant = Baseline(kind, probePotionId);
            probe.Set(variant);
            // 探针表里的 <card> / <potion> 占位符换成这个探针存档里真实存在的卡 / 药水
            //（写死不存在的类名时生成端认不出来，会把「读了这一栏」误判成「没读」）。
            if (variant.SpawnCardId == "<card>") variant.SpawnCardId = probeCardId;
            if (variant.PotionId == "<potion>") variant.PotionId = probePotionId;
            string variantSource = SourceFor(scratch, chain, kind, variant, probeCardId);
            result.Add((probe, !string.Equals(baseline, variantSource, StringComparison.Ordinal)));
        }
        return result;
    }
}
