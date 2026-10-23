using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 【效果】模块（锻造节点图）与卡牌那套 <see cref="EffectSpec"/> 编辑器之间的**桥**。
///
/// 背景（用户拍板 Q1=A）：锻造页【效果】模块直接复用「卡牌」页那套 EffectSpec 编辑器 ——
/// 一份模板、一份数据，两边的可填写项自然一致。但生成端 / 回读 / 校验 / 积木视图读的都是
/// 模块上的老字段（<see cref="ForgeNodeSpec.Fields"/>，键是 <c>Effect</c> / <c>Amount</c> /
/// <c>Times</c> / <c>Hits</c> / <c>Chance</c> …，V0.2.128 及以前就这个形状）。
///
/// 所以这里做两件**方向相反**的事，规矩只有一条 —— **老存档生成出来的东西一个字节都不许变**：
///
///   ① <see cref="NormalizeGraph"/>（打开存档时跑一次）：老字段 → EffectSpec。
///      只迁一次（<see cref="ForgeNodeSpec.EffectSpec"/> 已经有值就整段跳过 = 幂等），
///      迁完把 <see cref="ForgeNodeSpec.EffectFromLegacy"/> 置 true；
///   ② <see cref="MirrorToFields"/>（用户改过之后 / 生成前跑）：EffectSpec → 老字段。
///      只对 <c>FromLegacy == false</c>（新编辑器说了算）的那些模块跑 ——
///      **迁移出来的老模块永远不回写**，生成端照旧读它们原样的老字段。
///
/// 于是：
///   · 老存档（只有老字段）→ 生成文本逐字不变（连 Fields 都不动一个键）；
///   · 老存档 + 用户用新编辑器改了效果 → 改动先落进老字段，再走同一条生成路径；
///   · 新建 / 探针造的模块 → 直接就是 spec 说了算（<see cref="Author"/>）。
/// </summary>
public static class ForgeEffectBridge
{
    /// <summary>这个模块是不是【效果】模块。</summary>
    public static bool IsEffectNode(ForgeNodeSpec? node) =>
        node is not null && string.Equals(node.Kind, "Effect", StringComparison.Ordinal);

    /// <summary>一份配置里所有的锻造节点图（卡牌升级前 / 升级后、药水、遗物、自定义状态）。</summary>
    public static IEnumerable<ForgeGraphSpec> GraphsOf(CharacterProfile? p)
    {
        if (p is null) yield break;
        foreach (var c in p.Cards ?? new System.Collections.ObjectModel.ObservableCollection<CardSpec>())
        {
            if (c?.ForgeGraph is not null) yield return c.ForgeGraph;
            if (c?.UpgradedForgeGraph is not null) yield return c.UpgradedForgeGraph;
        }
        foreach (var s in p.Potions ?? new System.Collections.ObjectModel.ObservableCollection<PotionSpec>())
            if (s?.ForgeGraph is not null) yield return s.ForgeGraph;
        foreach (var r in p.Relics ?? new System.Collections.ObjectModel.ObservableCollection<RelicSpec>())
            if (r?.ForgeGraph is not null) yield return r.ForgeGraph;
        foreach (var cp in p.CustomPowers ?? new System.Collections.ObjectModel.ObservableCollection<CustomPowerSpec>())
            if (cp?.ForgeGraph is not null) yield return cp.ForgeGraph;
    }

    /// <summary>把全份配置里的【效果】模块都迁一遍（打开存档时调用；幂等）。</summary>
    public static void Normalize(CharacterProfile? p)
    {
        foreach (var g in GraphsOf(p)) NormalizeGraph(g);
    }

    /// <summary>把一张图里的【效果】模块迁一遍（幂等：已经有 EffectSpec 的跳过）。</summary>
    public static void NormalizeGraph(ForgeGraphSpec? g)
    {
        if (g?.Nodes is null) return;
        foreach (var n in g.Nodes)
        {
            if (n is null || !IsEffectNode(n)) continue;
            if (n.EffectSpec is not null) continue;             // ← 幂等的关键（第二次跑一个字节都不改）
            n.EffectSpec = FromFields(n);
            n.EffectFromLegacy = true;                          // 老字段是事实来源（生成端不回写、只读 Fields）
        }
    }

    /// <summary>
    /// 拿到【效果】模块的编辑器模型（没有就按老字段建一个；**不会**回写老字段）。
    /// <paramref name="authored"/> = true 时表示「这是新造 / 已用新编辑器改过的模块」，
    /// 老字段以后由 spec 回写（见 <see cref="MirrorToFields"/>）。
    /// </summary>
    public static EffectSpec Ensure(ForgeNodeSpec node, bool authored = false)
    {
        if (node.EffectSpec is null)
        {
            node.EffectSpec = FromFields(node);
            // 老存档的模块一定有老字段（界面至少写过 "Effect" 那一格）；老字段全空 = 新建的模块
            node.EffectFromLegacy = !authored && node.Fields.Count > 0;
        }
        else if (authored)
        {
            node.EffectFromLegacy = false;
        }
        return node.EffectSpec;
    }

    /// <summary>
    /// 新造 / 已改过的【效果】模块：拿到 spec、挂上变更通知、并标记「spec 说了算」。
    /// 探针 / 界面新建模块都走它。**迁移出来的老模块不要走它**（会破「老存档逐字不变」）。
    /// </summary>
    public static EffectSpec Author(ForgeNodeSpec node)
    {
        var spec = Ensure(node, authored: true);
        Bind(node);
        return spec;
    }

    /// <summary>
    /// 挂上「spec 一改就回写老字段」的通知（**幂等**：同一个模块只挂一次；界面每次 Rebuild 都会调）。
    /// ⚠ 挂之前先把界面那些不写存档的标记（`IsForgeChain` 之类）设好 —— 挂上之后任何一次 set
    /// 都会触发回写，把老字段里的写法（比如大小写 / 缺省键）规范化掉。
    /// </summary>
    public static void Bind(ForgeNodeSpec node)
    {
        if (node.EffectSpec is null || node.EffectBridgeBound) return;
        node.EffectBridgeBound = true;
        node.EffectSpec.PropertyChanged += (_, e) => OnSpecChanged(node, e);
    }

    private static void OnSpecChanged(ForgeNodeSpec node, PropertyChangedEventArgs e)
    {
        var spec = node.EffectSpec;
        if (spec is null) return;
        // 「作用对象」被真的动过：从此这一栏接管目标（老存档没动过 = false，生成端照老规矩走）
        if (string.Equals(e.PropertyName, nameof(EffectSpec.TargetSide), StringComparison.Ordinal))
            spec.TargetSideCustom = true;
        // 改了任何一格 = 这份 spec 不再是「原封不动的老存档」→ 从此由它回写老字段
        node.EffectFromLegacy = false;
        MirrorToFields(node);
    }

    /// <summary>
    /// 生成 / 校验之前把「spec 说了算」的那些模块回写进老字段（老模块原样不动）。
    /// 生成端 / 校验器 / 卡面文案读的都是老字段，所以必须在它们之前跑。
    /// </summary>
    public static void SyncGraph(ForgeGraphSpec? g)
    {
        if (g?.Nodes is null) return;
        foreach (var n in g.Nodes)
        {
            if (n is null || !IsEffectNode(n)) continue;
            var spec = Ensure(n);
            if (spec is null || n.EffectFromLegacy) continue;
            MirrorToFields(n);
        }
    }

    /// <summary>
    /// 按老字段建一份 <see cref="EffectSpec"/>（迁移用）。
    ///
    /// 映射口径（**逐条对齐 <see cref="ForgeGraphGen"/> 读老字段的方式**，所以迁完再回写也不会变语义）：
    ///   · 数值：纯数字 → <c>Amount</c>；公式原文 → <c>ForgeAmountText</c>（锻造链所有种类都能写公式）；
    ///   · 生效次数 / 命中次数 / 概率：纯数字 → 固定值；公式 → 对应的「次数公式」；
    ///   · 作用对象：留默认 "Enemy" 且 <see cref="EffectSpec.TargetSideCustom"/> = false（老规矩：由【对象】模块决定）。
    /// </summary>
    public static EffectSpec FromFields(ForgeNodeSpec node)
    {
        var e = new EffectSpec
        {
            Kind = KindOf(node),
            PowerId = NullIfEmpty(node.Get("PowerId")),
            SpawnCardId = NullIfEmpty(node.Get("SpawnCardId")),
            PotionId = NullIfEmpty(node.Get("PotionId")),
            PotionPick = Canonical(node.Get("PotionPick"), "Fixed", "Fixed", "Random"),
            SpawnTo = Canonical(node.Get("SpawnTo"), "Hand", "Hand", "Draw", "Discard", "Exhaust"),
            // ⚠ V0.2.181：这几个「生成卡牌 / 变化卡牌」的档位以前**压根没读**（用户实测报的
            // 「锻造卡的生成卡牌多选1不正常生效」：节点上明明选了「按范围随机」+「多选1 = 3」，
            // 桥这边建出来的 spec 却是 SpawnPick=""（⇒ IsSpawnRandom=false）、SpawnChoice=1，
            // 于是生成端走「指定卡」那条老路 —— 生成 N 张同一个卡，选牌界面根本不出现）。
            // 键名 = EffectFieldUsage 里那一行（界面绑的就是 SpawnPick / SpawnChoice / SpawnFilter…）。
            SpawnPick = Canonical(node.Get("SpawnPick"), "Fixed", "Fixed", "Random"),
            SpawnChosen = node.GetFlag("SpawnChosen"),
            SpawnFilter = (node.Get("SpawnFilter") ?? "").Trim(),
            SpawnUpgraded = node.GetFlag("SpawnUpgraded"),
            SpawnFree = node.GetFlag("SpawnFree"),
            SpawnFreeThisTurn = node.GetFlag("SpawnFreeThisTurn"),
            SpawnUpgradedThisTurn = node.GetFlag("SpawnUpgradedThisTurn"),
            SpawnKeyword = (node.Get("SpawnKeyword") ?? "").Trim(),
            // V0.2.220：图上【效果】新加的「为抽到的牌加关键词 / 是不是临时」两栏
            //（抽牌 / 从三摞拿牌 / 填满手牌用得到）。没有这两个键的老图 → 空串 / false，
            // 和 EffectSpec 的默认值一模一样（生成文本一个字都不变）。
            DrawnKeyword = (node.Get("DrawnKeyword") ?? "").Trim(),
            DrawnKeywordTemp = node.GetFlag("DrawnKeywordTemp"),
            // ⚠ 老图的「取牌方式」默认是「自己选」；卡牌那栏的默认是 Random，所以必须显式按老默认给
            CardPick = string.Equals(node.Get("Pick", "").Trim(), "Random", StringComparison.OrdinalIgnoreCase) ? "Random" : "Chosen",
            // V0.2.212：图上【效果】模块新加的「从哪里选牌 / 放到哪一摞」两栏。
            // 没有这两个键的老图 → 都落回 "Hand"，和 EffectSpec 自己的默认值一模一样（生成文本一个字都不变）。
            SelectPile = Canonical(node.Get("SelectPile"), "Hand", "Hand", "Draw", "Discard", "Exhaust", "Top"),
            PlaceTo = Canonical(node.Get("PlaceTo"), "Hand", "Hand", "Draw", "Discard", "Exhaust", "Top"),
            TempPowerReverse = node.Get("Reverse").Trim().Length == 0 ? TempPowerReverses.TurnEnd : node.Get("Reverse").Trim(),
            NextTurn = node.GetFlag("NextTurn"),
            // V0.2.250：图上【效果】新加的「只持续本回合」（减少卡牌费用用）。
            // 没有这个键的老图 → false，和 EffectSpec 默认值一样（老存档生成文本一个字都不变）。
            CostDownThisTurn = node.GetFlag("CostDownThisTurn"),
            AllowDuplicates = node.GetFlag("AllowDuplicate", true),
            TargetSideCustom = false,
        };

        // ---- 数值（老图的默认值是 6，见 ForgeGraphGen.EmitEffect 里的 Resolve(..., "Amount", 6)）----
        string amount = node.Get("Amount").Trim();
        e.ForgeAmountText = amount;
        if (amount.Length == 0) e.Amount = 6m;
        else if (decimal.TryParse(amount, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal av)) e.Amount = av;
        else if (EffectSpec.FormulaSupports(e.Kind)) e.AmountFormula = amount;

        // ---- 生效次数 / 命中次数（默认都是 1）----
        ApplyCount(node.Get("Times"), "1", v => e.Times = v, f => e.TimesFormula = f);
        ApplyCount(node.Get("Hits"), "1", v => e.RepeatCount = v, f => e.RepeatFormula = f);
        // ---- 多选1 的候选张数（默认 1 = 不是多选1；只认纯数字，公式这一栏不支持）----
        if (decimal.TryParse(node.Get("SpawnChoice").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal sc))
            e.SpawnChoice = sc;

        // ---- 概率生效（老图是一个数字：100 = 必定；也能写公式）----
        string chance = node.Get("Chance").Trim();
        if (chance.Length == 0)
        {
            e.ChanceEnabled = false;                 // 老默认 = 100 = 必定
            e.ChancePercent = 100m;
        }
        else if (decimal.TryParse(chance, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal cv))
        {
            e.ChancePercent = cv;
            e.ChanceEnabled = cv != 100m;
        }
        else
        {
            e.ChanceFormula = chance;
            e.ChanceEnabled = true;
        }

        // V0.2.272（用户报「限制了仅状态牌还是可以拿其他的牌」）：
        // 侧栏效果编辑器改的「范围界定 / 只对这张卡」如果还没回写到布上的老字段（node 的 SpawnFilter 是空的），
        // 生成端读的却是这里 `FromFields` 建出来的 spec —— 于是过滤谓词整段丢掉（选牌界面里什么牌都能选）。
        // 兜底：老字段空、而模块上绑着的 spec 有 → 用 spec 的。只补空值，老图（两边都空）生成结果逐字不变。
        if (node.EffectSpec is { } bound)
        {
            // UseCardScopeFilter 是按效果种类算出来的（只读），只要 SpawnFilter 非空且这个种类支持，就自动生效
            if ((e.SpawnFilter ?? "").Trim().Length == 0
                && bound.HasCardScope
                && !string.IsNullOrWhiteSpace(bound.SpawnFilter))
                e.SpawnFilter = bound.SpawnFilter;
            if ((e.ScopeCardId ?? "").Trim().Length == 0 && bound.HasScopeCardId)
                e.ScopeCardId = bound.ScopeCardId;
        }
        return e;
    }

    /// <summary>
    /// 把 <see cref="EffectSpec"/> 回写成老字段（**只写生成端真的会读的那些键**，别的键一个都不动）。
    /// 空值也照写（空串 = 那一栏没填，和以前没这个键等价 —— 生成端读的 fallback 是同一份）。
    /// </summary>
    public static void MirrorToFields(ForgeNodeSpec node)
    {
        var e = node.EffectSpec;
        if (e is null) return;
        node.SetField("Effect", e.Kind);
        node.SetField("PowerId", (e.PowerId ?? "").Trim());
        node.SetField("Amount", AmountTextOf(e));
        node.SetField("Times", CountText(e.TimesFormula, e.Times));
        node.SetField("Hits", CountText(e.RepeatFormula, e.RepeatCount));
        node.SetField("Chance", ChanceTextOf(e));
        node.SetField("Pick", string.Equals(e.CardPick, "Random", StringComparison.OrdinalIgnoreCase) ? "Random" : "Chosen");
        node.SetField("SpawnCardId", (e.SpawnCardId ?? "").Trim());
        node.SetField("SpawnTo", e.SpawnTo);
        // V0.2.181：把「取卡方式 / 多选1 / 范围限定 / 从范围里自己挑 / 附加处理」也回写 ——
        // 不回写的话，界面上改过这些栏位之后老字段还是旧的（生成端虽然读 spec，但回读 / 别的读老字段的
        // 路径会看到过期值）。
        node.SetField("SpawnPick", string.Equals(e.SpawnPick, "Random", StringComparison.OrdinalIgnoreCase) ? "Random" : "Fixed");
        node.SetField("SpawnChoice", e.SpawnChoice.ToString("0.####", CultureInfo.InvariantCulture));
        node.SetField("SpawnFilter", (e.SpawnFilter ?? "").Trim());
        node.SetField("SpawnChosen", e.SpawnChosen ? "1" : "0");
        node.SetField("SpawnUpgraded", e.SpawnUpgraded ? "1" : "0");
        node.SetField("SpawnFree", e.SpawnFree ? "1" : "0");
        node.SetField("SpawnFreeThisTurn", e.SpawnFreeThisTurn ? "1" : "0");
        node.SetField("SpawnUpgradedThisTurn", e.SpawnUpgradedThisTurn ? "1" : "0");
        node.SetField("SpawnKeyword", (e.SpawnKeyword ?? "").Trim());
        // V0.2.220：「为抽到的牌加关键词 / 是不是临时」—— 同样只在这几种效果用得到时才写
        //（积木视图会把非空栏位拼成摘要，无条件写「关键词 = 不加」是噪声）。
        if (Sts2CharForge.Core.Effects.EffectFieldPolicy.UsesDrawnKeyword(e.Kind))
        {
            node.SetField("DrawnKeyword", (e.DrawnKeyword ?? "").Trim());
            node.SetField("DrawnKeywordTemp", e.DrawnKeywordTemp ? "1" : "0");
        }
        node.SetField("PotionPick", e.PotionPick);
        node.SetField("PotionId", (e.PotionId ?? "").Trim());
        // V0.2.212：「从哪里选牌 / 放到哪一摞」（放置卡牌、消耗 / 丢弃 / 升级 / 变化 / 复制类效果用得到）。
        // ⚠ 只在这两种效果**用得到的那一档**才写：积木视图（ForgeBlockView）会把非空的栏位拼成一句摘要，
        // 无条件写「手牌」会让每个效果模块都多一句「放到哪一摞：手牌」的噪声。
        if (EffectSpec.KindUsesSelectPile(e.Kind)) node.SetField("SelectPile", e.SelectPile);
        if (EffectSpec.KindUsesPlaceTo(e.Kind)) node.SetField("PlaceTo", e.PlaceTo);
        // V0.2.250：「只持续本回合」只在「减少卡牌费用」上用得到时才回写（积木摘要不噪声）
        if (string.Equals(e.Kind, "CostDown", StringComparison.Ordinal))
            node.SetField("CostDownThisTurn", e.CostDownThisTurn ? "1" : "0");
        node.SetField("Reverse", e.TempPowerReverse);
        node.SetField("NextTurn", e.NextTurn ? "1" : "0");
    }

    /// <summary>「数值」栏回写成什么文字（优先锻造链存的原文，其次卡牌那套公式 / 固定值）。</summary>
    public static string AmountTextOf(EffectSpec e)
    {
        string raw = (e.ForgeAmountText ?? "").Trim();
        if (raw.Length > 0) return raw;
        if (e.UsesAmountFormula) return e.AmountFormulaText;
        return e.Amount.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>次数类栏位回写成什么文字（写了公式就是公式本身）。</summary>
    private static string CountText(string formula, int fixedValue)
    {
        string f = EffectSpec.FormulaText(formula);
        return f.Length > 0 ? f : fixedValue.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>「概率生效」回写成什么文字（没勾「概率生效」= 老图的 100 = 必定）。</summary>
    private static string ChanceTextOf(EffectSpec e)
    {
        string f = EffectSpec.FormulaText(e.ChanceFormula);
        if (f.Length > 0) return f;
        return e.ChanceEnabled
            ? e.ChancePercent.ToString("0.##", CultureInfo.InvariantCulture)
            : "100";
    }

    /// <summary>老图的「效果种类」（认不出来就退回 Damage，和生成端同一份判据）。</summary>
    public static string KindOf(ForgeNodeSpec node)
    {
        string k = node.Get("Effect", "Damage").Trim();
        return k.Length == 0 ? "Damage" : k;
    }

    /// <summary>数字栏位：填得进一个数就当固定值，否则当公式原文（两条路的生成语义一致，见 ForgeNumbers）。</summary>
    private static void ApplyCount(string? text, string dflt, Action<int> setValue, Action<string> setFormula)
    {
        string s = (text ?? "").Trim();
        if (s.Length == 0) s = dflt;
        if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal v))
            setValue(Math.Max(1, (int)Math.Round(v)));     // 和 ForgeNumbers.Resolve 的 Math.Max(1, …) 一致
        else
            setFormula(s);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>下拉值规范化：认（忽略大小写）就给那一档的规范写法，认不出来给 <paramref name="dflt"/>。</summary>
    private static string Canonical(string? raw, string dflt, params string[] allowed)
    {
        string s = (raw ?? "").Trim();
        foreach (string a in allowed)
            if (string.Equals(s, a, StringComparison.OrdinalIgnoreCase)) return a;
        return dflt;
    }
}
