using System.Text;
using System.Text.Json;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>生成本地化文件（characters / cards / relics / potions / ancients / powers）。</summary>
public static class LocalizationGen
{
    /// <summary>
    /// 角色「说明」留空时生成端填的那句**兜底文案**（V0.2.134 抽成常量）。
    ///
    /// 为什么要有这个常量：这一句是**生成端造的默认值**，不是用户写的。从工程恢复时如果不认得它，
    /// 就会把它当成用户填的说明读回来 —— 于是「空说明」生成出来的工程，恢复后说明变成了这一句，
    /// 再生成一次 mod_manifest.json 就跟第一次不一样了（实测：这一版自检里抓到）。
    /// 生成 / 回读两边共用这一个字符串，判据就是「说明 == 这句 && 清单里 description 是空的」→ 当没填。
    /// </summary>
    public const string DefaultCharacterDescription = "一个自定义角色。";

    /// <summary>
    /// 联机 ping（右下角那个 <b>Ping</b> 按钮按下去冒出的那句「催促」台词）的**生成端默认值**。
    /// 这一句从 V0.0.x 起就写死在生成端（V0.2.173 之前没有任何配置项），所以：
    ///   · 生成时用户没填 → 写这一句（老存档 / <c>--samplegen</c> 结果逐字不变）；
    ///   · 从工程恢复时读到这一句 → 当作「没填」（否则恢复出来的存档会无中生有多出一句，
    ///     再生成一次 characters.json 就跟第一次不一样 —— 和
    ///     <see cref="DefaultCharacterDescription"/> 同一个坑）。
    /// </summary>
    public const string DefaultEndTurnPingText = "……继续。";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 本地化表 → JSON。写盘前统一过一遍 <see cref="RichTextFix"/>：标签拼错（[god]）、
    /// 没闭合（[gold]文字）、交叉嵌套都会让本体的 BBCode 解析**抛异常**，
    /// 后果是自动字号中断 + 卡面上那串方括号被原样印出来（用户实测报过）。
    /// 修好的文本进本地化表；改了什么由校验器/生成日志报给用户（原始文本仍留在 CET 标记里，回读不受影响）。
    /// </summary>
    internal static string LocJson(Dictionary<string, string> dict)
    {
        foreach (string key in dict.Keys.ToList())
            dict[key] = RichTextFix.Repair(dict[key]).Text;
        return JsonSerializer.Serialize(dict, JsonOpts);
    }

    public static string CharactersJson(CharacterProfile p)
    {
        var n = Naming.From(p);
        var (obj, poss, possPro, subj) = p.Gender switch
        {
            "Feminine" => ("她", "她的", "她的", "她"),
            "Neutral" => ("TA", "TA的", "TA的", "TA"),
            _ => ("他", "他的", "他的", "他"),
        };

        var dict = new Dictionary<string, string>
        {
            [$"{n.CharEntry}.title"] = p.DisplayName,
            [$"{n.CharEntry}.titleObject"] = p.DisplayName,
            [$"{n.CharEntry}.description"] = string.IsNullOrWhiteSpace(p.Description) ? DefaultCharacterDescription : p.Description,
            [$"{n.CharEntry}.pronounObject"] = obj,
            [$"{n.CharEntry}.possessiveAdjective"] = poss,
            [$"{n.CharEntry}.pronounPossessive"] = possPro,
            [$"{n.CharEntry}.pronounSubject"] = subj,
            [$"{n.CharEntry}.cardsModifierTitle"] = p.DisplayName + "卡牌",
            [$"{n.CharEntry}.cardsModifierDescription"] = p.DisplayName + "的卡牌现在会出现在奖励和商店中。",
            [$"{n.CharEntry}.eventDeathPrevention"] = string.IsNullOrWhiteSpace(p.DeathText) ? "……还不到时候。" : p.DeathText,
            [$"{n.CharEntry}.bestiaryQuote"] = "……记住这个数字就够了。",
            [$"{n.CharEntry}.goldMonologue"] = "钱？也只是数字。",
            // V0.2.173：这一句以前**写死**成「……继续。」—— 现在由角色页那栏「多人催促文本」决定。
            // 留空时仍然写「……继续。」：老存档 / --samplegen 生成的结果逐字不变（硬红线）。
            [$"{n.CharEntry}.banter.alive.endTurnPing"] =
                string.IsNullOrWhiteSpace(p.EndTurnPingText) ? DefaultEndTurnPingText : p.EndTurnPingText.Trim(),
            [$"{n.CharEntry}.banter.dead.endTurnPing"] = string.IsNullOrWhiteSpace(p.DeadBanterText) ? "……" : p.DeadBanterText,
            [$"{n.CharEntry}.unlockText"] = "完成 {Prerequisite} 的条件后解锁。",
        };
        return LocJson(dict);
    }

    public static string CardsJson(CharacterProfile p)
    {
        var n = Naming.From(p);
        var dict = new Dictionary<string, string>();
        foreach (var c in p.AllCards)
        {
            // 本体卡引用（打击 / 防御）：名字和描述都用本体自己的那份，不要覆盖
            if (c.IsVanillaCard) continue;
            string entry = Naming.EntryOf(n.CardClassName(p, c));
            dict[$"{entry}.title"] = c.Name;
            // 「从牌堆拿牌到手牌 / 预见 / 多选1」的选牌界面提示语（生成代码里用 base.SelectionScreenPrompt 读它）
            // V0.2.145：**锻造卡的效果在节点图上**（`c.Effects` 是空的）→ 两块布上的效果也要一起看，
            // 否则图上那种「从某一摞拿牌到手牌」会让本体在打出这张牌时抛异常（游戏卡死）。
            var cardPromptEffects = c.Effects.Where(e => e is not null)
                .Concat(GraphEffects(c.ForgeGraph))
                .Concat(GraphEffects(c.UpgradedForgeGraph))
                .ToList();
            if (NeedsSelectPrompt(cardPromptEffects))
                dict[$"{entry}.selectionScreenPrompt"] = SelectPromptText(cardPromptEffects);
            // 自定义关键词：和本体关键词一样拼在描述最前面（本体是「[gold]消耗[/gold]。」+ 换行 + 效果描述）
            // V0.2.136：「升级后获得 / 失去的自定义关键词」让这两段可以**不一样** ——
            // 获得 = 升级后那段才有那一行，失去 = 没升级那段才有（见 KeywordGen.CardTextBoth）。
            // ⚠ 没填那两栏时两段逐字节相同，下面走的还是**老路径**（keywordText + 老分支），产物一个字符都不变。
            var (keywordBase, keywordUp) = KeywordGen.CardTextBoth(p, c);
            string keywordText = keywordBase;
            bool keywordDiffers = !string.Equals(keywordBase, keywordUp, StringComparison.Ordinal);
            // 卡牌自定义描述：默认**追加**在自动描述后面；勾了「替换」就整段换掉（关键词那行仍保留在最前面）
            string customDesc = (c.CustomDescription ?? "").Trim();
            string customUp = (c.UpgradedCustomDescription ?? "").Trim();
            // 自动描述：普通卡 = 效果栏自动写的那段；锻造卡 = 从**节点图**写出来的（以前这种卡是空的）。
            // 升级前 / 升级后各算一次：两段不一样（或你给升级后单独写了自定义描述）时，
            // 用本体的 {IfUpgraded:show:升级后|没升级} 包起来，游戏里按有没有升级自动显示对应那一版。
            string autoBase = CardAutoText(p, c, upgraded: false);
            string autoUp = CardAutoText(p, c, upgraded: true);
            // ⚠「升级后」那一栏**留空 = 用同一段自定义描述**（老存档只有一个框，升级前后都显示它，
            //   不能因为「升级后没单独写」就把自定义那段换成自动描述）
            string customUpWanted = customUp.Length > 0 ? customUp : customDesc;
            string bodyBase = MergeAutoAndCustom(autoBase, customDesc, c.CustomDescriptionReplaces);
            string bodyUp = MergeAutoAndCustom(autoUp, customUpWanted, c.CustomDescriptionReplaces);
            // 正文那段（不含关键词行）：升级前后一样就直接用，不一样才包 {IfUpgraded}
            string cardBody = string.Equals(bodyBase, bodyUp, StringComparison.Ordinal)
                ? bodyBase
                : IfUpgradedText(bodyUp, bodyBase);
            if (keywordDiffers)
            {
                // 关键词那行也要按升级前后分叉 → 整段（关键词 + 正文）一起包进 {IfUpgraded:show:升级后|没升级}。
                // 顺序固定 = 关键词行在前、正文在后（和上面老路径的拼接顺序一致）。
                string fullBase = keywordBase.Length == 0
                    ? bodyBase
                    : (bodyBase.Length == 0 ? keywordBase : keywordBase + "\n" + bodyBase);
                string fullUp = keywordUp.Length == 0
                    ? bodyUp
                    : (bodyUp.Length == 0 ? keywordUp : keywordUp + "\n" + bodyUp);
                dict[$"{entry}.description"] = string.Equals(fullBase, fullUp, StringComparison.Ordinal)
                    ? fullBase
                    : IfUpgradedText(fullUp, fullBase);
            }
            else
            {
                dict[$"{entry}.description"] = keywordText.Length == 0
                    ? cardBody
                    : (cardBody.Length == 0 ? keywordText : keywordText + "\n" + cardBody);
            }
        }
        return LocJson(dict);
    }

    /// <summary>
    /// 这张牌**自动生成的那段描述**（升级前 / 升级后各一次调用）。
    /// 卡牌页那两个「自动填入」按钮用的就是它，生成存档时也是同一段。
    /// </summary>
    public static string CardAutoText(CharacterProfile p, CardSpec c, bool upgraded)
    {
        if (ForgeGraphGen.UsesGraph(c))
        {
            var g = upgraded && c.UpgradedForgeGraph is { IsEmpty: false } up ? up : c.ForgeGraph;
            if (g is { IsEmpty: false }) return GraphCardText(p, g);
        }
        // 效果列表那条路：升级前后是**同一段**（数字靠本体的 {…:diff()} 由游戏自己按升没升级显示）
        string bodyText = c.IsCurseCard
            ? CurseBodyText(p, c)
            : Describe(c.Effects, p, isCard: true, starCostIsX: c.StarCostIsXEffective) + ConditionSuffix(c.Condition, isCard: true, p);
        // V0.2.131：「额外资源量费用」框里显式写了费用时，卡面开头补一句（那条负数效果不一定还在）
        string starLead = StarCostLeadText(p, c);
        return starLead.Length == 0 ? bodyText : (bodyText.Length == 0 ? starLead : starLead + "\n" + bodyText);
    }

    /// <summary>
    /// 「额外资源量费用」框里写的费用在卡面上的那一句（空 = 不写）。
    ///
    /// 为什么不写在效果那一栏：新语义下这张费用是**框**里定的，效果列表里可以一条负数效果都没有
    /// （老存档才是「负数效果 = 费用」）。有负数效果时不重复写 —— 那一句本来就由效果生成，
    /// 写两句会变成「需要 2 点星尘。…需要 3 点星尘。」（校验器会提示这种冲突）。
    /// </summary>
    private static string StarCostLeadText(CharacterProfile p, CardSpec c)
    {
        if (string.IsNullOrWhiteSpace(c.StarCostFormula)) return "";
        if (c.Effects.Any(e => e is not null && e.Kind == "ExtraResource" && e.Amount < 0)) return "";
        var r = Generation.ForgeNumbers.ResolveCost(p, c.StarCostFormula, "Y", allowPlus: false, "额外资源量费用");
        if (!r.Ok) return "";   // 解析不出来由校验器报错，这里不写半句话
        if (r.IsX) return $"需要 X 点{ResourceName(p)}（打出时消耗全部）。";
        return r.Value > 0 ? $"需要 {r.Value} 点{ResourceName(p)}。" : "";
    }

    /// <summary>自动描述 + 自定义描述（勾了「替换」就只用自定义那段）。</summary>
    private static string MergeAutoAndCustom(string auto, string custom, bool replaces)
    {
        if (custom.Length == 0) return auto;
        if (replaces) return custom;
        return auto.Length == 0 ? custom : auto + "\n" + custom;
    }

    /// <summary>
    /// 本体那套「按有没有升级显示不同文字」的语法（<c>ShowIfUpgradedFormatter</c>）：
    /// 第一个选项 = **升级后**，第二个 = 没升级（和本体卡牌描述里的用法一致）。
    /// ⚠ 两个选项里的**字面量 <c>|</c>** 会把选项截断（生成出来的卡面会解析失败），所以这里换成全角 `｜`。
    /// </summary>
    private static string IfUpgradedText(string upgraded, string normal)
        => "{IfUpgraded:show:" + EscapePipe(upgraded) + "|" + EscapePipe(normal) + "}";

    private static string EscapePipe(string s) => s.Replace("|", "｜");

    // ==================================================================================
    //  节点图 → 卡面描述（锻造卡用；以前这种卡的 .description 是**空的**）
    // ==================================================================================

    /// <summary>
    /// 把一张锻造卡的节点图写成卡面中文。例：
    /// 「如果 指定单体敌人身上的虚弱 大于 3：对所有敌人造成 A+3 点伤害。否则：自己失去 5 点生命。」
    ///
    /// 口径和生成端一致：从【触发时机】顺出口走；【对象】/【指向对象】改「这一支打谁」；
    /// 【if】两支分别是「是 / 否」；【条件】/【且】/【或】当成一道门（成立才继续）。
    /// 数字一律写成**字面量或公式本身**（锻造卡不声明动态变量，用不了 {…:diff()}）。
    /// </summary>
    public static string GraphCardText(CharacterProfile p, ForgeGraphSpec g)
    {
        // V0.2.168：一块布上可以有多块【触发时机】—— 每一块各写一段（各带自己的时机抬头），
        // 一块都没有时才算空图。每块用自己的 seen 集合（两块共用一个模块时两段都要写出来）。
        var parts = new List<string>();
        foreach (var trig in g.TriggerNodes)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string body = ChainText(p, g, ForgeGraphGen.NodeAtOutgoing(g, trig, 0), "ChosenEnemy", seen, 0);
            if (body.Length == 0) continue;
            string head = TriggerText(trig);
            parts.Add(head.Length == 0 ? body : head + "\n" + body);
        }
        return string.Join("\n", parts);
    }

    /// <summary>触发时机的抬头：默认的「卡牌被打出时」不写（和效果列表那条路一样）；别的时机写出来。</summary>
    private static string TriggerText(ForgeNodeSpec trigger)
    {
        string timing = trigger.Get("Timing", ForgeTimings.CardPlayed);
        if (string.Equals(timing, ForgeTimings.CardPlayed, StringComparison.Ordinal)) return "";
        // 认不出来的时机（手写 JSON / 老存档）**不写**抬头 —— 别把 id 原样印到卡面上
        string zh = ForgeTimings.Find(timing)?.Display ?? "";
        return zh.Length == 0 ? "" : zh + "：";
    }

    private static string JoinText(List<string> parts)
    {
        var keep = parts.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        return string.Join("\n", keep);
    }

    /// <summary>顺着执行链把这一支写成文字（【if】两支各自递归）。</summary>
    private static string ChainText(CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec? first, string unitPick,
        HashSet<string> seen, int depth)
    {
        var parts = new List<string>();
        var cur = first;
        string unit = unitPick;
        int guard = 0;
        while (cur is not null && depth < 24 && guard++ < 64 && seen.Add(cur.Id))
        {
            switch (cur.Kind)
            {
                // 【对象】/【指向对象】：改「这一支打谁」（后面的效果没自己接【对象】就按它算）
                case "Unit":
                case "Target":
                    unit = cur.Get("Pick", unit);
                    break;
                case "Effect":
                {
                    string one = EffectText(p, g, cur, unit);
                    if (one.Length > 0) parts.Add(one);
                    break;
                }
                case "If":
                {
                    string cond = CondText(p, g, ForgeGraphGen.NodeAtIncoming(g, cur, 1));
                    string yes = ChainText(p, g, ForgeGraphGen.NodeAtOutgoing(g, cur, 0), unit, seen, depth + 1);
                    string no = ChainText(p, g, ForgeGraphGen.NodeAtOutgoing(g, cur, 1), unit, seen, depth + 1);
                    parts.Add(BranchText(cond, yes, no));
                    return JoinText(parts);
                }
                // 【条件】/【且】/【或】当成一道门：成立才继续（生成端写的就是 if (cond) { 后面的链 }）
                case "Condition":
                case "And":
                case "Or":
                {
                    string cond = CondText(p, g, cur);
                    string rest = ChainText(p, g, ForgeGraphGen.NodeAtOutgoing(g, cur, 0), unit, seen, depth + 1);
                    parts.Add(BranchText(cond, rest, ""));
                    return JoinText(parts);
                }
            }
            cur = ForgeGraphGen.NodeAtOutgoing(g, cur, 0);
        }
        return JoinText(parts);
    }

    /// <summary>一支写成一句：两份都有就「如果…：…；否则：…。」，只有一份就不写「否则」。</summary>
    private static string BranchText(string cond, string yes, string no)
    {
        // 条件没接时写成「如果（还没接条件）：…」——不加空格，读起来不像多了一个词
        bool noCond = cond.Length == 0;
        string c = noCond ? "如果（还没接条件）" : $"如果 {cond}";
        string yesOne = OneLine(yes), noOne = OneLine(no);
        if (yesOne.Length == 0 && noOne.Length == 0) return $"{c}：什么都不做。";
        if (noOne.Length == 0) return $"{c}：{yesOne}";
        if (yesOne.Length == 0) return $"{c}：什么都不做；否则：{noOne}";
        // 「…伤害。；否则…」中间那个句号太碍眼，去掉（「否则」那句自己的句号保留）
        return $"{c}：{TrimEndDot(yesOne)}；否则：{noOne}";
    }

    private static string TrimEndDot(string s) => s.EndsWith("。", StringComparison.Ordinal) ? s.Substring(0, s.Length - 1) : s;

    /// <summary>分支里嵌了分支就压成一行（卡面上换行太多看着乱）。</summary>
    private static string OneLine(string s) => s.Replace("\n", " ").Trim();

    /// <summary>一块【效果】写成一句（数字走它「数值」口接的【数值】公式，没接就用模块上填的数）。</summary>
    private static string EffectText(CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node, string unitPick)
    {
        var (pick, summonId) = ForgeGraphGen.ResolvedPickForText(g, node, unitPick);
        var e = EffectSpecOfNode(p, g, node);
        var (side, targetPhrase, whoPhrase) = UnitPhrases(pick);
        e.TargetSide = side;
        if (summonId.Length > 0) e.PetSummon = summonId;
        string amountText = AmountTextOf(g, node);
        if (decimal.TryParse(amountText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal parsed))
            e.Amount = parsed;
        return DescribeEffect(e, p, potionTarget: null, isCard: true, starCostIsX: false, varMap: null,
            targetOverride: targetPhrase, whoOverride: whoPhrase, varOverride: amountText.Length == 0 ? "0" : amountText);
    }

    /// <summary>
    /// 「全部友方」在卡面 / 药水描述里的措辞（V0.2.201）。
    ///
    /// <para>生成端遍历的是 <c>CombatState.Allies</c>（玩家这一边：玩家本体 + 召唤物），
    /// 两个子选项各改一处：勾「包括自己」把自己算进去、勾「不包括召唤物」把召唤物去掉。
    /// 默认（都不勾）= 队友 + 召唤物，不含自己。</para>
    /// </summary>
    private static string AllAlliesZh(EffectSpec e)
    {
        if (e.AllAlliesIncludeSelf)
            return e.AllAlliesExcludePets ? "你和所有队友" : "你、队友和所有召唤物";
        return e.AllAlliesExcludePets ? "所有队友" : "队友和所有召唤物";
    }

    /// <summary>「打谁」那一档 → 效果描述里用的三样（TargetSide / 「对谁」/「让谁」）。</summary>
    private static (string Side, string? Target, string? Who) UnitPhrases(string pick) => pick switch
    {
        "Self" or "ChosenAlly" => ("Self", null, null),
        "ChosenEnemy" => ("Enemy", null, null),
        "AllEnemies" => ("AllEnemies", null, null),
        "RandomEnemy" => ("RandomEnemies", null, null),
        "AllAllies" => ("Enemy", "对你和所有召唤物", "你和所有召唤物"),
        "ChosenSummon" => ("Enemy", "对指定召唤物", "指定召唤物"),
        "RandomSummon" => ("Enemy", "对随机召唤物", "随机召唤物"),
        "AllSummons" => ("Enemy", "对全部召唤物", "全部召唤物"),
        "AllUnits" => ("Enemy", "对所有单位", "所有单位"),
        _ => ("Self", null, null),
    };

    /// <summary>这一块【效果】的数值文字：接了【数值】就用它的公式，没接就用模块上填的数字。</summary>
    private static string AmountTextOf(ForgeGraphSpec g, ForgeNodeSpec node)
    {
        var val = ForgeGraphGen.NodeAtIncoming(g, node, 1);
        if (val is { Kind: "Value" }) return ValueText(val);
        return node.Get("Amount", "").Trim();
    }

    /// <summary>【数值】模块的文字：公式本身（留空就用备注，再留空写 0）。</summary>
    private static string ValueText(ForgeNodeSpec value)
    {
        string f = ForgeFormula.DisplayText(value.Get("Formula", ""));
        if (f.Length > 0) return f;
        string label = value.Get("Label", "").Trim();
        return label.Length > 0 ? label : "0";
    }

    /// <summary>数字写成卡面文字（3 → 「3」、3.5 → 「3.5」；整数不带小数点）。</summary>
    private static string NumberText(double d)
        => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>把图里那块【效果】的字段搬进一个 EffectSpec（数值/公式由调用方另外覆盖）。</summary>
    /// <param name="p">整份配置（V0.2.128：生效次数 / 命中次数 / 概率那几栏也能写公式，卡面文案要印公式）。</param>
    private static EffectSpec EffectSpecOfNode(CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node)
    {
        // ⚠ 这里**只做文案**（见 EffectText）：不参与生成，所以可以放心把公式塞进
        // EffectSpec 的次数公式字段（CSharpCodeGen 的文案路径本来就认它们）。
        var timesN = ForgeNumbers.Resolve(p, node, "Times", 1);
        var hitsN = ForgeNumbers.Resolve(p, node, "Hits", 1);
        var chanceN = ForgeNumbers.Resolve(p, node, "Chance", 100);
        var spec = new EffectSpec
        {
            Kind = node.Get("Effect", "Damage"),
            PowerId = node.Get("PowerId", ""),
            SpawnCardId = node.Get("SpawnCardId", ""),
            SpawnTo = node.Get("SpawnTo", "Hand"),
            CardPick = node.Get("Pick", "Random"),
            RepeatCount = hitsN.Runtime ? 1 : Math.Max(1, (int)Math.Round((double)hitsN.Value)),
            Times = timesN.Runtime ? 1 : Math.Max(1, (int)Math.Round((double)timesN.Value)),
            NextTurn = node.GetFlag("NextTurn", false),
            ChanceEnabled = chanceN.Runtime || (int)Math.Round((double)chanceN.Value) < 100,
            ChancePercent = chanceN.Runtime ? 100m : (decimal)chanceN.Value,
        };
        if (hitsN.Runtime) spec.RepeatFormula = hitsN.Text;
        if (timesN.Runtime) spec.TimesFormula = timesN.Text;
        if (chanceN.Runtime) spec.ChanceFormula = chanceN.Text;
        return spec;
    }

    /// <summary>条件那一块写成一句中文（【条件】/【关系符】/【且】/【或】）。</summary>
    private static string CondText(CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec? node)
    {
        if (node is null) return "";
        switch (node.Kind)
        {
            case "Condition":
            {
                // 数值 N / M 写了**运行时**公式时，卡面文案里把公式印出来（和「数值公式」同一套观感）
                var cn = ForgeNumbers.Resolve(p, node, "Amount", 1);
                var cm = ForgeNumbers.Resolve(p, node, "Amount2", 0);
                var spec = new ConditionSpec
                {
                    Kind = node.Get("Cond", "None"),
                    Amount = (decimal)cn.Value,
                    Amount2 = (decimal)cm.Value,
                    PowerId = string.IsNullOrWhiteSpace(node.Get("PowerId")) ? null : node.Get("PowerId"),
                };
                return CSharpCodeGen.ConditionText(spec, ResourceName(p),
                    cn.Runtime ? cn.Text : null, cm.Runtime ? cm.Text : null);
            }
            case "Relate":
            {
                string op = node.Get("Op", "Gt");
                bool needsBool = ForgeGraphCatalog.RelateNeedsBool(op);
                string left = RelateOperandText(p, g, ForgeGraphGen.NodeAtIncoming(g, node, 1), "");
                if (left.Length == 0) left = "（左值没接）";
                // 右值没接线时：比较类用模块上那个数（生成端也是 ForgeNumbers.Resolve("Amount", 3)）；
                // 「且 / 或 / 非」比的是条件，没接就不写右边。写了运行时公式就把公式印出来。
                string rightFallback;
                {
                    var ra = ForgeNumbers.Resolve(p, node, "Amount", 3);
                    rightFallback = needsBool ? "" : (ra.Runtime ? ra.Text : NumberText((double)ra.Value));
                }
                string right = RelateOperandText(p, g, ForgeGraphGen.NodeAtIncoming(g, node, 2), rightFallback);
                string opZh = op switch
                {
                    "Lt" => "小于", "Eq" => "等于", "Ge" => "大于等于", "Le" => "小于等于",
                    "Not" => "不成立", "And" => "且", "Or" => "或", _ => "大于",
                };
                if (op == "Not") return $"{left} {opZh}";
                return right.Length == 0 ? $"{left} {opZh}" : $"{left} {opZh} {right}";
            }
            case "And":
            case "Or":
            {
                string a = RelateOperandText(p, g, ForgeGraphGen.NodeAtIncoming(g, node, 0), "");
                string b = RelateOperandText(p, g, ForgeGraphGen.NodeAtIncoming(g, node, 1), "");
                string glue = node.Kind == "And" ? "且" : "或";
                if (a.Length == 0) return b;
                if (b.Length == 0) return a;
                return $"{a} {glue} {b}";
            }
            case "If":
                return CondText(p, g, ForgeGraphGen.NodeAtIncoming(g, node, 1));
            default:
                return "";
        }
    }

    /// <summary>关系符左右两侧的文字：一块【状态】=「谁身上的某个状态层数」；【数值】= 公式；没接 = 模块上那个数。</summary>
    private static string RelateOperandText(CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec? node, string fallback)
    {
        if (node is null) return fallback;
        switch (node.Kind)
        {
            case "Power":
            {
                string who = ForgeGraphGen.PowerOwnerPhraseForText(g, node, p);
                string power = PowerNameFor(p, node.Get("PowerId", "")).Trim();
                return $"{who}身上的{power}层数";
            }
            // V0.2.192：【状态列表】模块（「身上有任意减益」「状态数 ≥ N」这类）
            case ForgeGraphCatalog.PowerListKind:
            {
                string who = ForgeGraphGen.PowerOwnerPhraseForText(g, node, p);
                string filter = ForgeGraphCatalog.PowerListFilterOf(node);
                string scope = filter switch
                {
                    ForgeGraphCatalog.PowerListFilterBuff => "增益",
                    ForgeGraphCatalog.PowerListFilterDebuff => "减益",
                    ForgeGraphCatalog.PowerListFilterOne => PowerNameFor(p, node.Get("PowerId", "")).Trim(),
                    _ => "状态",
                };
                if (scope.Length == 0) scope = "状态";
                return string.Equals(ForgeGraphCatalog.PowerListWhatOf(node),
                        ForgeGraphCatalog.PowerListWhatLayers, StringComparison.Ordinal)
                    ? $"{who}身上的{scope}层数合计"
                    : $"{who}身上的{scope}数量";
            }
            case "Value":
                return ValueText(node);
            case "Energy":
                return "当前能量";
            case "Condition":
            case "Relate":
            case "And":
            case "Or":
            case "If":
                return CondText(p, g, node);
            default:
                return fallback;
        }
    }

    /// <summary>
    /// 诅咒的卡面描述：本体那句「在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中，…」+ 效果。
    /// 「不能被打出」不写在这段里 —— 它是本体的关键字（<c>CardKeyword.Unplayable</c>），
    /// 卡面会自己把关键字那一行印在最前面（和「消耗 / 虚无」一样）。
    /// 「战斗结束时删掉自己」那条也写出来，否则玩家看不懂这张诅咒为什么会自己消失。
    /// </summary>
    private static string CurseBodyText(CharacterProfile p, CardSpec c)
    {
        var sb = new StringBuilder();
        if (c.Effects.Count > 0)
        {
            string body = Describe(c.Effects, p, isCard: true);
            sb.Append("在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中：").Append('\n').Append(body);
        }
        if (c.CurseRemoveAfterCombat)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("战斗结束时，如果这张牌在你的[gold]牌组[/gold]中，它会消失。");
        }
        return sb.ToString();
    }

    public static string RelicsJson(CharacterProfile p)
    {
        var n = Naming.From(p);
        var dict = new Dictionary<string, string>();
        for (int i = 0; i < p.Relics.Count; i++)
        {
            var r = p.Relics[i];
            string entry = Naming.EntryOf(n.RelicClassName(r, i));
            dict[$"{entry}.title"] = r.Name;
            // V0.2.126：效果是**整只遗物唯一那一份** → 选牌提示文案只看它（不再遍历各条时机的效果）
            // V0.2.145：锻造遗物的效果在节点图上 → 图上的效果也要一起看（同一个坑，见 GraphEffects）。
            var relicAllEffects = r.Effects.Where(e => e is not null)
                .Concat(GraphEffects(r.ForgeGraph))
                .ToList();
            if (NeedsSelectPrompt(relicAllEffects))
                dict[$"{entry}.selectionScreenPrompt"] = SelectPromptText(relicAllEffects);
            // 遗物自定义描述（用户要求）：留空 = 自动那段；写了就追加（勾「替换」就整段换掉）
            // V0.2.126：触发时机只说明「什么时候触发」、效果是遗物那一份 —— 所以描述 =
            // 「触发时机名（、触发时机名…）：效果那一段」。**只有 1 条时机、关系 = 满足其中一个就触发
            // （= 老存档）时逐字保留老写法**（老存档的遗物描述一个字符都不动）。
            var relicTriggerList = r.Triggers.Where(t => t is not null).ToList();
            // V0.2.209：「锻造图说了算」的遗物**不声明动态变量**（源码里 CanonicalVars 是空的）——
            // 描述里不能写 {…:diff()}，一律写数字字面量（否则游戏里抛 Localization formatting error）。
            bool relicLiteral = r.ForgeGraph is { IsEmpty: false };
            string relicBody;
            if (relicTriggerList.Count > 1)
            {
                string when = string.Join("、", relicTriggerList.Select(RelicTriggerHeading));
                // 「全部满足才触发」必须写出来：不然玩家会以为随便哪条都触发
                if (r.IsAllTriggers) when += "（全部满足才触发）";
                relicBody = when + "：" + Describe(relicAllEffects, p, literalAmount: relicLiteral);
            }
            else
            {
                // 单条时机：**逐字**用老写法（筛选那句「打出一张牌后（只对X牌生效）：」拼在最前面）
                relicBody = RelicSingleTriggerBody(r.First, relicAllEffects, p, literalAmount: relicLiteral);
            }
            relicBody += ConditionSuffix(r.Condition, isCard: false, p);
            string relicCustom = (r.CustomDescription ?? "").Trim();
            if (relicCustom.Length > 0)
                relicBody = r.CustomDescriptionReplaces
                    ? relicCustom
                    : (relicBody.Length == 0 ? relicCustom : relicBody + "\n" + relicCustom);
            dict[$"{entry}.description"] = relicBody;
            dict[$"{entry}.flavor"] = "由 Sts2CharForge 生成的自定义遗物。";
        }
        // 「转化为先古卡」的预览界面顶部那行说明（本体 NSimpleCardsViewScreen 的 infoText）。
        // 所有用到这个效果的遗物共用一条键 —— 文案是固定的，没必要每只遗物各写一份。
        if (p.Relics.Any(r => r is not null && r.Effects.Any(e => e is not null && e.Kind == "AncientTranscend")))
            dict[CSharpCodeGen.AncientPreviewLocKey] = "这些牌被转化成了先古版本。";
        // 额外资源量的载体遗物也要有名字，否则遗物栏里会显示成缺键
        if (p.ExtraResource.Enabled)
        {
            var x = p.ExtraResource;
            string entry = Naming.EntryOf(Naming.From(p).ExtraResourceRelicClass);
            string res = ResourceName(p);
            dict[$"{entry}.title"] = res;
            dict[$"{entry}.description"] = x.CarryOver
                ? $"每场战斗开始时获得 {x.Initial} 点{res}，上一场战斗没用完的会保留到下一场。"
                : $"每场战斗开始时获得 {x.Initial} 点{res}（战斗结束清空）。";
            dict[$"{entry}.flavor"] = RenameResource("由 Sts2CharForge 生成的隐藏起始遗物，用来承载额外资源量。", p);
        }
        return LocJson(dict);
    }

    /// <summary>
    /// **单条触发时机**的遗物描述（= 老写法，逐字保留）。
    ///
    /// 「只对哪类牌生效」（第 9 批 ④）的说明就拼在这一段里 —— 筛选不是「任意」时把
    /// 「打出一张牌后（只对X牌生效）：」写在效果最前面（不写的话玩家在游戏里只看得到效果，
    /// 看不出它只对某类牌生效）。默认（任意）**一个字都不加**：老存档的遗物描述逐字不变。
    /// </summary>
    private static string RelicSingleTriggerBody(RelicTriggerSpec t, List<EffectSpec> effects, CharacterProfile p, bool literalAmount = false)
    {
        string body = Describe(effects, p, literalAmount: literalAmount);
        bool filtered = string.Equals(t.Kind, "CardPlayed", StringComparison.Ordinal)
            && !string.Equals(t.CardFilter, "Any", StringComparison.Ordinal);
        if (!filtered) return body;
        string when = $"打出一张牌后（只对{PowerTriggers.CardFilterZh(t.CardFilter)}牌生效）：";
        return body.Length > 0 ? when + "\n" + body : when;
    }

    /// <summary>
    /// 多条时机时那句「哪几条时机」（用触发时机的显示名，顿号连接）。
    /// 「打出一张牌后」还带筛选时，把「只对X牌生效」并进这一句里（不另起一句，见 RelicSingleTriggerBody）。
    /// </summary>
    private static string RelicTriggerHeading(RelicTriggerSpec t)
    {
        string when = EffectCatalog.RelicTriggers.FirstOrDefault(x => x.Id == t.Kind)?.Display ?? t.Kind;
        if (string.Equals(t.Kind, "CardPlayed", StringComparison.Ordinal)
            && !string.Equals(t.CardFilter, "Any", StringComparison.Ordinal))
            when += $"（只对{PowerTriggers.CardFilterZh(t.CardFilter)}牌生效）";
        return when;
    }

    /// <summary>
    /// 额外资源量在界面上的名字（悬停提示 / 卡面描述 / 那个隐藏遗物都用它）。
    /// 用户没填就用「额外资源量」这个通用说法。
    /// </summary>
    public static string ResourceName(CharacterProfile p) =>
        string.IsNullOrWhiteSpace(p.ExtraResource.Name) ? "额外资源量" : p.ExtraResource.Name.Trim();

    /// <summary>
    /// 把文本里的「额外资源量」换成用户自己填的名字（没填就原样返回）。
    /// 触发时机名（「花掉额外资源量后」）、条件说明（「拥有额外资源量至少 N 点」）里都带着这个词。
    /// </summary>
    public static string RenameResource(string text, CharacterProfile p)
    {
        string name = ResourceName(p);
        return name == "额外资源量" || text.Length == 0 ? text : text.Replace("额外资源量", name);
    }

    /// <summary>
    /// 一份锻造节点图里所有【效果】模块的 <see cref="EffectSpec"/>（V0.2.145）。
    ///
    /// <para>为什么要它：锻造卡 / 锻造药水 / 锻造遗物的效果都在**节点图上**，`XxxSpec.Effects` 是空的 ——
    /// 只按 `Effects` 判断「要不要选牌提示语」时，图上那种「从某一摞拿牌到手牌 / 预见」就被漏掉，
    /// 而生成代码照样读 <c>base.SelectionScreenPrompt</c> → 本体直接抛异常、游戏卡死
    /// （和 V0.2.145 修的自定义状态那条是同一个坑）。</para>
    ///
    /// <para>节点上的 spec 平时由 <see cref="ForgeEffectBridge.SyncGraph"/> 保证存在；这里再兜一次
    /// <see cref="ForgeEffectBridge.Ensure"/>（幂等：只按老字段建 spec，不回写老字段）——
    /// 这样单独调 <c>CardsJson</c> 的自检也拿得到。</para>
    /// </summary>
    public static IEnumerable<EffectSpec> GraphEffects(ForgeGraphSpec? graph)
    {
        if (graph?.Nodes is null) yield break;
        foreach (var node in graph.Nodes)
        {
            if (!ForgeEffectBridge.IsEffectNode(node)) continue;
            yield return node!.EffectSpec ?? ForgeEffectBridge.Ensure(node!);
        }
    }

    /// <summary>
    /// 这组效果里有没有会弹出**自己的**选牌界面的（那种必须有 <c>&lt;ENTRY&gt;.selectionScreenPrompt</c> 这一条本地化）。
    ///
    /// 为什么要盯死这件事：本体的 <c>CardModel.SelectionScreenPrompt</c> 在键不存在时**直接抛异常**
    /// （<c>throw new InvalidOperationException($"No selection screen prompt for {Id}.")</c>），
    /// 而生成的代码是在打出那张牌的时候读它的 —— 抛在效果中间 = 这张牌永远打不完，游戏卡死。
    /// 用户实测：预见的卡「无法丢弃、游戏卡死」就是这么来的（预见当时漏在这条名单外）。
    /// 所以凡是生成代码里出现 <c>base.SelectionScreenPrompt</c> 的效果种类，都必须列在这里。
    ///
    /// <para>⚠ 同一条规矩对**四条链**都成立（卡牌 / 遗物 / 药水 / 自定义状态）：
    /// 本体 <c>PowerModel.SelectionScreenPrompt</c> 是同一份实现（同样的键名、同样"没有就抛"）。
    /// V0.2.145 修的就是「自定义状态上「生成卡牌 + 多选1」漏了这一条」——
    /// 游戏日志里 <c>InvalidOperationException: No selection screen prompt for POWER.XXX</c>
    /// 抛在战斗回合钩子里 → 本轮 turn loop 直接死掉 → **战斗卡住**（用户实测的卡死）。</para>
    /// </summary>
    public static bool NeedsSelectPrompt(IEnumerable<EffectSpec> effects) =>
        effects.Any(e => e.Kind is "TakeFromDraw" or "TakeFromDiscard" or "TakeFromExhaust" or "Scry"
            || (e.Kind == "GiveKeyword" && e.Amount > 0 && !e.AmountIsXEffective && e.CardPick == "Chosen")
            // 「生成卡牌 + 按范围随机 + 多选1（候选 > 3 张时走网格选牌界面）」会读 base.SelectionScreenPrompt；
            // V0.2.145 补上**「从范围里自己挑一张」**：它的候选 > 3 时走的也是那条网格选牌界面
            //（生成端 `EmitGenerateCard` 里 `if (e.SpawnChosen)` 那一支，条件写的是同一个 base.SelectionScreenPrompt）
            // —— 以前漏了它，正好是「自定义状态选生成卡牌会卡死」的另一半。
            || (e.Kind == "GenerateCard" && e.IsSpawnRandom && (e.SpawnChoice > 1m || e.SpawnChosen))
            // 「放置卡牌 + 取牌方式 = 自己选」（且不是「牌顶」那一档）：生成代码读的也是 base.SelectionScreenPrompt
            //（CSharpCodeGen 里 `SelectPrefsExpr(e, "base.SelectionScreenPrompt", n)` 那条）。
            // ⚠ V0.2.145 第二遍「机械扫描」抓出来的漏网之鱼（见 --uicheck 里那条按种类扫关键字的断言）——
            //   漏了它就是同一种「打出这张牌 = 游戏卡死」。
            || (e.Kind == "PlaceCard" && e.CardPick == "Chosen" && !CSharpCodeGen.IsPileTop(e))
            // 另外「复制卡牌」自己选牌也要（CardSelectCmd.FromHand 那条）。
            || (e.Kind == "CopyCard" && e.CardPick == "Chosen")
            // ⚠ V0.2.173（用户实测：锻造卡「你去！」打出后抛 `No selection screen prompt for CARD.XXX`
            // ⇒ 那张牌的效果**一条都不跑**、牌「挂在手上」）：**「变化卡牌 + 取牌方式 = 自己选」**这一档
            // 还有一条**节点图专用**的读取点，上面那几条都盖不到它：
            //   ForgeGraphGen.EmitEffect 里 `chosen` 是按**图上字段**判的
            //   （`bool chosen = !string.Equals(node.Get("Pick", "Chosen"), "Random")`），
            //   然后走 `CardSelectCmd.FromHand / FromCombatPile(… new CardSelectorPrefs(base.SelectionScreenPrompt, …))`；
            //   而 `TransformCard` 的**卡牌页**那条链只在 `IsSpawnRandom && (SpawnChoice > 1 || SpawnChosen)` 时读
            //   —— 图上这个节点是「固定卡 + Pick=Chosen」，`IsSpawnRandom` 为 false、`SpawnChosen` 也是 false，
            //   于是判据说「不用写键」，代码却在打出时读了 → 本体抛异常。
            // 这是同一类坑第三次复发（V0.2.145 自定义状态 / V0.2.170 四档 / 这次图上 Pick），所以一并算进来：
            // 多写一条没人读的键无害，漏一条就是「打出这张牌 = 卡住」。
            || (e.Kind is "TransformCard" or "CopyCard" && e.CardPick == "Chosen")
            // 「重放」自己选牌也一样（走 CardSelectCmd.FromHand / FromCombatPile）
            || (e.Kind == "ReplayCard" && e.CardPick == "Chosen")
            // 「回合结束时自动打出」填了 1 以上 + 自己选：选牌是**打出这张牌时**做的，读的是它的提示语
            || (e.Kind == "TurnEndPlay" && e.Amount > 0 && !e.AmountIsXEffective && e.CardPick == "Chosen")
            // 「给予卡牌关键词」+ 取牌方式 = 自己选（数值 ≥ 1）：生成代码读的也是 base.SelectionScreenPrompt
            //（CSharpCodeGen.EmitGiveKeyword 里 `new CardSelectorPrefs(base.SelectionScreenPrompt, n)` 那两句）。
            // V0.2.176：把「数值是公式 / X」也算进来（那种也走自己选那一支、也读提示语），
            // 但「全部」（CountIsAll 走 foreach、不弹界面）和「数值 0 = 这张牌自己」（直接对 this 动手）
            // 这两档**不读**，所以排除掉 —— 判据和发射器逐条对齐：多写一条键无害，漏一条就是牌卡住。
            || (e.Kind == "GiveKeyword" && e.CardPick == "Chosen" && !e.CountIsAll && !(e.Amount <= 0m && !e.AmountIsXEffective))
            // V0.2.170：**「取卡方式 = 多选1 / 从范围里自己挑一张」**这一档，四条链（生成 / 变化 /
            // 全局加牌 / 全局变化）走的都是 `CardSelectCmd.FromSimpleGrid(..., new CardSelectorPrefs(base.SelectionScreenPrompt, 1))`
            //（候选 > 3 时；见 CSharpCodeGen.EmitGenerateCard / EmitTransformCard / EmitGlobalCardEffect 里
            //  EmitSpawnPickChoose 那段）—— 漏一个就是「打出这张牌 = 本体抛 No selection screen prompt ⇒ 游戏卡死」。
            || (((e.Kind is "GenerateCard" or "TransformCard" or "AddCardGlobal" or "TransformCardGlobal")
                 && e.IsSpawnRandom && (e.SpawnChoice > 1m || e.SpawnChosen))));

    /// <summary>选牌界面上那句提示（本体的 <c>&lt;ENTRY&gt;.selectionScreenPrompt</c>，生成代码里读 base.SelectionScreenPrompt）。</summary>
    public static string SelectPromptText(IEnumerable<EffectSpec> effects)
    {
        var list = effects as IList<EffectSpec> ?? effects.ToList();
        if (list.Any(e => e.Kind == "Scry")) return "选择要丢进弃牌堆的牌";
        if (list.Any(e => e.Kind == "CopyCard" && e.CardPick == "Chosen")) return "选择要复制的牌";
        // V0.2.173：图上「变化卡牌 + 自己选」也要一句（这段话用在哪条效果上都读得通）
        if (list.Any(e => e.Kind == "TransformCard" && e.CardPick == "Chosen")) return "选择要变化的牌";
        if (list.Any(e => e.Kind == "ReplayCard" && e.CardPick == "Chosen")) return "选择要重放的牌";
        // V0.2.176：图上「给予关键词 + 自己选」也要一句
        if (list.Any(e => e.Kind == "GiveKeyword" && e.CardPick == "Chosen" && !e.CountIsAll && !(e.Amount <= 0m && !e.AmountIsXEffective))) return "选择要给予关键词的牌";
        if (list.Any(e => e.Kind == "TurnEndPlay" && e.Amount > 0 && e.CardPick == "Chosen")) return "选择回合结束时自动打出的牌";
        // 「从范围里自己挑一张」：候选就是这一组范围里的全部卡（V0.2.145 补上，以前会落到最后那句
        // 「选择要拿到手牌的牌」—— 措辞对不上这条效果）
        if (list.Any(e => e.Kind == "GenerateCard" && e.IsSpawnRandom && e.SpawnChosen)) return "选择要生成的牌";
        // V0.2.170：「变化 / 加进牌组 / 牌组变化」的「多选1 / 自己挑一张」也读同一份提示语
        if (list.Any(e => e.Kind == "TransformCard" && e.IsSpawnRandom && (e.SpawnChosen || e.SpawnChoice > 1m))) return "选择要变化成的牌";
        if (list.Any(e => e.Kind == "AddCardGlobal" && e.IsSpawnRandom && (e.SpawnChosen || e.SpawnChoice > 1m))) return "选择要加进牌组的牌";
        if (list.Any(e => e.Kind == "TransformCardGlobal" && e.IsSpawnRandom && (e.SpawnChosen || e.SpawnChoice > 1m))) return "选择要变化成的牌";
        // 「放置卡牌 + 自己选」：从这一摞挑 N 张放到另一摞（V0.2.145 补上，同一个道理）
        if (list.Any(e => e.Kind == "PlaceCard" && e.CardPick == "Chosen" && !CSharpCodeGen.IsPileTop(e))) return "选择要移动的牌";
        // 多选1 的提示要跟着候选张数走（「在 3 张中选一张」）
        var choice = list.FirstOrDefault(e => e.Kind == "GenerateCard" && e.IsSpawnRandom && e.SpawnChoice > 1m);
        if (choice is not null)
        {
            int m = Math.Max(2, (int)choice.SpawnChoice);
            return $"从 {m} 张中选一张";
        }
        var e = list.FirstOrDefault(x => x.Kind is "TakeFromDraw" or "TakeFromDiscard" or "TakeFromExhaust");
        if (e is null) return "选择要拿到手牌的牌";
        string pile = e.Kind switch
        {
            "TakeFromDiscard" => "弃牌堆",
            "TakeFromExhaust" => "消耗牌堆",
            _ => "抽牌堆",
        };
        return $"从{pile}选择要拿到手牌的牌";
    }

    /// <summary>
    /// 「范围界定」（需求 ③：选牌类效果只对某一类牌 / 某一组牌动手）在卡面描述里的那一句。
    /// 没设范围就返回空串 —— 卡面描述与旧版逐字一致。
    /// 例：<c>（只限攻击牌）</c> / <c>（只限「命运」那一组牌）</c>。
    /// </summary>
    private static string CardScopeNote(CharacterProfile p, EffectSpec e)
    {
        // V0.2.214：「只对这张卡（指定）」也写进卡面（和第 ③ 项的范围界定是「与」的关系）
        string card = e.HasScopeCardId ? $"（只对{CardNameOf(p, e.ScopeCardId)}）" : "";
        if (!e.HasCardScope) return card;
        string name = EffectCatalog.SpawnFilterZhFor(p, e.SpawnFilter);
        return ((EffectCatalog.IsKeywordFilter(e.SpawnFilter) || EffectCatalog.IsCardGroupFilter(e.SpawnFilter))
            ? $"（只限「{name}」那一组牌{(e.HasScopeCardId ? $"，只对{CardNameOf(p, e.ScopeCardId)}" : "")}）"
            : $"（只限{name}牌{(e.HasScopeCardId ? $"，只对{CardNameOf(p, e.ScopeCardId)}" : "")}）");
    }

    /// <summary>
    /// 「刮削」卡面描述的后半句（条件和操作都是可选的，各档的说法不一样）。
    /// **默认那一档（非 0 费 + 丢弃）返回与旧版逐字一致的「丢弃其中所有非 0 费牌」** ——
    /// 老存档的卡面描述一个字符都不能变（用户要求），由 --uicheck 钉死。
    /// 例：<c>丢弃其中所有非 0 费牌</c> / <c>把其中「攻击」的牌消耗掉</c> / <c>把其中每一张牌升级</c>。
    /// </summary>
    private static string ScrapeTailZh(CharacterProfile p, EffectSpec e)
    {
        if (EffectCatalog.IsDefaultScrape(e.ScrapeCond, e.ScrapeOp)) return "丢弃其中所有非 0 费牌";
        string cond = EffectCatalog.ScrapeCondZhFor(p, e.ScrapeCond);
        string op = EffectCatalog.ScrapeOpZh(e.ScrapeOp);
        // 「不限」时不说「符合条件的」（那样读起来像还有筛选）
        return e.ScrapeCond == EffectCatalog.ScrapeCondAny
            ? $"把其中每一张牌{op}"
            : $"把其中「{cond}」的牌{op}";
    }

    /// <summary>
    /// 需求 ⑦：「生成 / 变化出来的卡带上关键词」在卡面描述里的那一句。
    /// 没选关键词就返回空串 —— 卡面描述与旧版逐字一致（老存档一个字符都不变）。
    /// 例：<c>（生成出来的卡会带上[gold]虚无[/gold]）</c>。
    /// </summary>
    private static string SpawnKeywordNote(CharacterProfile p, EffectSpec e)
    {
        if (!e.HasSpawnKeyword) return "";
        return $"（生成出来的卡会带上[gold]{KeywordNameOf(p, e.SpawnKeyword)}[/gold]）";
    }

    /// <summary>「给予卡牌关键词」的一句话描述。</summary>
    private static string GiveKeywordText(CharacterProfile p, EffectSpec e, string repeat, string when)
    {
        string kw = KeywordNameFor(p, e);
        string temp = e.TempKeyword ? "（本回合）" : "";
        if (e.Amount <= 0) return $"{when}这张牌获得[gold]{kw}[/gold]{temp}。";
        string n = e.AmountIsXEffective ? "X" : Math.Max(1, (int)e.Amount).ToString();
        string pile = e.SelectPile switch { "Draw" => "抽牌堆", "Discard" => "弃牌堆", "Exhaust" => "消耗牌堆", _ => "手牌" };
        // 「取几张 = 全部」（V0.2.163）：不弹选牌界面、也不是随机 —— 不写「自己选 / 随机」，也不写张数
        if (e.CountIsAll)
            return e.SelectPile is "Draw" or "Discard" or "Exhaust"
                ? $"{repeat}{pile}里的所有牌获得[gold]{kw}[/gold]{temp}{CardScopeNote(p, e)}。"
                : $"{repeat}所有手牌获得[gold]{kw}[/gold]{temp}{CardScopeNote(p, e)}。";
        string pick = e.CardPick == "Chosen" ? "自己选" : "随机";
        return $"{repeat}{pick} {n} 张{pile}里的牌获得[gold]{kw}[/gold]{temp}{CardScopeNote(p, e)}。";
    }

    /// <summary>
    /// 「强化指定卡牌」（像本体「精准」）的一句话描述。
    /// 数值 = 加多少（本场战斗内一直有效），负数 = 削弱那张卡。
    /// 负数时写死绝对值（和「回复 / 失去生命」那几条一个写法）：卡面上印「额外造成 -4 点伤害」很别扭。
    /// </summary>
    private static string BoostCardText(CharacterProfile p, EffectSpec e, string var)
    {
        string card = CardNameOf(p, e.SpawnCardId);
        bool block = e.BoostStat == "Block";
        if (e.Amount < 0 && var != "X")
            return block
                ? $"你打出的{card}获得的格挡减少 {-e.Amount} 点（本场战斗内持续）。"
                : $"你打出的{card}造成的伤害减少 {-e.Amount} 点（本场战斗内持续）。";
        return block
            ? $"你打出的{card}额外获得 {var} 点格挡（本场战斗内持续）。"
            : $"你打出的{card}额外造成 {var} 点伤害（本场战斗内持续）。";
    }

    /// <summary>「给予卡牌关键词」在卡面上的名字：本体关键词用中文枚举名，自定义关键词用它的显示名。</summary>
    private static string KeywordNameFor(CharacterProfile p, EffectSpec e) => KeywordNameOf(p, e.GivenKeyword);

    /// <summary>
    /// 关键词引用（本体枚举名 / 自定义关键词的键或名字）在卡面上的名字。
    /// 「给予卡牌关键词」和需求 ⑦ 的「生成出来的卡带的关键词」共用这一份。
    /// </summary>
    private static string KeywordNameOf(CharacterProfile p, string? reference)
    {
        string raw = (reference ?? "").Trim();
        if (raw.Length == 0) return "?";
        string? vanilla = EffectCatalog.NormalizeVanillaKeyword(raw);
        if (vanilla is not null)
        {
            // 去掉括号里的解释（「保留（回合末不弃）」→「保留」）
            string display = EffectCatalog.VanillaKeywordChoices.First(k => k.Id == vanilla).Display;
            int at = display.IndexOf('（');
            return at > 0 ? display[..at] : display;
        }
        var hit = KeywordGen.Find(p, raw);
        return hit is null ? raw : KeywordGen.DisplayName(hit.Value.Spec, hit.Value.Key);
    }

    /// <summary>
    /// 覆盖本体的「辉星」悬停提示：只要启用了额外资源量，就把标题和描述都换成我们自己的
    /// —— 名字用你填的显示名，规则按你配的写（开场多少点 / 是否跨战斗继承 / 消耗规则）。
    ///
    /// 为什么描述也要覆盖（用户报过「额外资源量的描述有误」）：
    /// 以前只有「上传了自定义图标」时才写描述，于是没传图标时鼠标悬停会显示**本体的**那条
    /// 「你当前的辉星…储君的部分卡牌可能会需要辉星才能打出」——说的完全是别的角色的资源。
    /// 本体加载本地化时会把模组的同名表并进来、覆盖同名键，所以这个文件能生效。
    /// </summary>
    /// <summary>
    /// 额外资源量的悬停提示（名字 + 说明）。
    ///
    /// ⚠ **不覆盖本体的 <c>STAR_COUNT.title</c> / <c>STAR_COUNT.description</c>**（以前是覆盖的）。
    /// 那两个键是**全局**的：多人联机时「影响玩法的模组」必须在每个客户端都装着（本体的握手规则），
    /// 于是同一台机器上会同时装好几个角色模组 —— 都去改同一个本体键的话，
    /// **谁后加载谁赢**，而且两个客户端的加载顺序还可能不同（你看到「戏法」、队友看到「星尘」）。
    /// 现在改成写我们**自己的键**，由生成的计数器补丁在「这个计数器归我方角色」时换上去
    /// （见生成的 <c>ExtraResourceUi.Apply</c>）—— 每个角色显示自己那份，互不干扰，
    /// 本体 / 别的角色 / 百科里的辉星文案一个字都没动。
    ///
    /// 为什么要写描述（用户报过「额外资源量的描述有误」）：
    /// 只写标题时，鼠标悬停会显示**本体的**那条「你当前的辉星…储君的部分卡牌可能会需要辉星才能打出」——
    /// 说的完全是别的角色的资源。所以名字和说明都要有一份我们自己的。
    /// </summary>
    public static string StaticHoverTipsJson(CharacterProfile p)
    {
        var dict = new Dictionary<string, string>();
        if (!p.ExtraResource.Enabled) return LocJson(dict);

        var n = Naming.From(p);
        string name = ResourceName(p);
        dict[$"{n.CharEntry}_EXTRA_RESOURCE.title"] = name;

        // 描述：用户填了「自定义描述」就原样用它（用户要求）；没填才用工具自动写的那段。
        // 注意用「内联小图」（24×24，见 ArtGenerator.ExtraResourceIconInlineResPath）：
        // 内联 [img] 是按原图尺寸画的，塞计数器那张大图会把整段提示文字挤到字号极小。
        string? icon = ArtGenerator.ExtraResourceIconInlineResPath(p);
        string iconPrefix = icon is null ? "" : $"[img]{icon}[/img]";
        string? custom = (p.ExtraResource.Description ?? "").Trim();
        if (custom.Length > 0)
        {
            // 用户自己写了 [img] 就不再加前缀（避免出现两个图标）
            dict[$"{n.CharEntry}_EXTRA_RESOURCE.description"] =
                custom.Contains("[img]", StringComparison.OrdinalIgnoreCase) ? custom : iconPrefix + custom;
            return LocJson(dict);
        }

        dict[$"{n.CharEntry}_EXTRA_RESOURCE.description"] = ExtraResourceAutoDescription(p);
        return LocJson(dict);
    }

    /// <summary>
    /// 额外资源量**自动写**的那段描述（用户没填「自定义描述」时用的）。
    ///
    /// <para>为什么单独抽成一个 public 方法：<c>ProjectRecovery.ParseExtraResource</c> 要判断
    /// 「工程里那段描述是用户自定义的、还是工具自动写的」—— 自动写的那份**不该**回读成
    /// 「用户填了自定义描述」（回读一次就多一段自己长出来的文字）。两边共用这一份文本，
    /// 将来改措辞也不会对不上。</para>
    ///
    /// <para>⚠ 这段文本**带**图标前缀（<c>[img]…[/img]</c>，按「有没有自定义图标」自动补）——
    /// 回读端要先把前缀剥掉再和这里比。</para>
    /// </summary>
    public static string ExtraResourceAutoDescription(CharacterProfile p)
    {
        var n = Naming.From(p);
        string name = ResourceName(p);
        string? icon = ArtGenerator.ExtraResourceIconInlineResPath(p);
        string iconPrefix = icon is null ? "" : $"[img]{icon}[/img]";
        var bits = new List<string>();
        if (p.ExtraResource.Initial > 0)
            bits.Add(p.ExtraResource.CarryOver
                ? $"每场战斗开始时获得 {p.ExtraResource.Initial} 点（上一场剩下的会带过来）"
                : $"每场战斗开始时获得 {p.ExtraResource.Initial} 点（战斗结束清零）");
        else
            bits.Add("开场不发放，靠卡牌 / 遗物 / 药水获得");

        bool spends = CSharpCodeGen.AllEffects(p).Any(e => e.Kind == "ExtraResource" && e.Amount < 0);
        if (spends)
            bits.Add("打出需要消耗它的牌时会扣除，数量不足时无法打出");

        return $"{iconPrefix}{name}：{string.Join("；", bits)}。";
    }

    /// <summary>
    /// 「资源不够、这张牌打不出去」那句提示（本体键 <c>combat_messages.NOT_ENOUGH_STARS</c> =
    /// 「我没有足够的[red]辉星[/red]。」）——我们写**自己的键**，由生成的补丁只在玩我方角色时换上去。
    ///
    /// 为什么要专门写这个文件：那句话是本体全局键、里面写死了「辉星」两个字，
    /// 换了资源名之后一付不起星费就露馅（用户实测报过）。覆盖全局键会连带影响别的角色和联机另一边，
    /// 所以只加我们自己的键（同名文件逐键合并，见 LocManager/MergeWith）。
    /// 名字直接写进文本里 —— 本体那句本来就没有占位符，这里也不给玩家留「填错变量」的机会。
    /// </summary>
    public static string CombatMessagesJson(CharacterProfile p)
    {
        var dict = new Dictionary<string, string>();
        if (!p.ExtraResource.Enabled) return LocJson(dict);
        var n = Naming.From(p);
        string name = ResourceName(p);
        // 和本体那句保持同一套富文本/换行（本体是「我没有\n足够的[red]辉星[/red]。」）
        dict[$"{n.CharEntry}_EXTRA_RESOURCE.notEnough"] = $"我没有\n足够的[red]{name}[/red]。";
        return LocJson(dict);
    }

    /// <summary>
    /// 自定义关键词的文案，写进本体的 <c>card_keywords</c> 表（同名文件逐键合并 → 只加自己的键，
    /// 不影响本体那 8 个关键词）。键形如 <c>命定 → FATE.title / FATE.description</c>。
    ///
    /// 这个文件同时也是「本体关键词改名」的落点：<c>EXHAUST.title = 你的名字</c> 会盖掉本体那条，
    /// 于是本体卡面上的金色词和悬停提示都变成你写的（本体 LocTable.MergeWith 的行为，不需要补丁）。
    /// 两拨键合在一个文件里 —— 少了这个文件，改名就不生效。
    /// </summary>
    public static string KeywordsJson(CharacterProfile p)
    {
        var dict = KeywordGen.LocEntries(p);
        // 本体关键词改名：只写用户真的改了的键（留空 / 和本体一样都不写，见 VanillaKeywordGen.LocEntries）
        foreach (var kv in VanillaKeywordGen.LocEntries(p))
            dict[kv.Key] = kv.Value;
        return LocJson(dict);
    }

    public static string PotionsJson(CharacterProfile p)
    {
        var n = Naming.From(p);
        var dict = new Dictionary<string, string>();
        for (int i = 0; i < p.Potions.Count; i++)
        {
            var s = p.Potions[i];
            string entry = Naming.EntryOf(n.PotionClassName(s, i));
            dict[$"{entry}.title"] = s.Name;
            // 药水的实际作用目标是「药水属性」里的 TargetType（生成的代码按它执行），
            // 所以描述也必须按它写，否则会出现「描述说打单体、实际打了全体」这种不一致。
            // V0.2.145：锻造药水的效果在节点图上 → 图上的效果也要一起看（同一个坑，见 GraphEffects）。
            var potionPromptEffects = s.Effects.Where(e => e is not null)
                .Concat(GraphEffects(s.ForgeGraph))
                .ToList();
            if (NeedsSelectPrompt(potionPromptEffects))
                dict[$"{entry}.selectionScreenPrompt"] = SelectPromptText(potionPromptEffects);
            // 药水自定义描述（用户要求）：留空 = 自动那段；写了就追加（勾「替换」就整段换掉）
            string potionBody = Describe(s.Effects, p, PotionTargetPhrase(s.TargetType));
            string potionCustom = (s.CustomDescription ?? "").Trim();
            dict[$"{entry}.description"] = potionCustom.Length == 0
                ? potionBody
                : (s.CustomDescriptionReplaces
                    ? potionCustom
                    : (potionBody.Length == 0 ? potionCustom : potionBody + "\n" + potionCustom));
        }
        return LocJson(dict);
    }

    /// <summary>
    /// 先古之民对话：<c>&lt;先古之民&gt;.talk.&lt;我们的角色&gt;.&lt;X&gt;-&lt;Y&gt;[r].ancient|char</c>，
    /// 非最后一句还要给 <c>…next</c>（按钮上的字）。
    /// 这些键由模组里的补丁（AncientDialoguePatch）在运行时把我们的角色塞进本体的对话表之后才会被读到。
    /// </summary>
    public static string AncientsJson(CharacterProfile p)
    {
        var n = Naming.From(p);
        var dict = new Dictionary<string, string>();

        foreach (var talk in p.Ancients)
        {
            if (string.IsNullOrWhiteSpace(talk.AncientId)) continue;
            if (EffectCatalog.FindAncient(talk.AncientId) is null) continue;

            for (int x = 0; x < talk.Dialogues.Count; x++)
            {
                var dlg = talk.Dialogues[x];
                string r = dlg.Repeating ? "r" : "";
                for (int y = 0; y < dlg.Lines.Count; y++)
                {
                    var line = dlg.Lines[y];
                    if (string.IsNullOrWhiteSpace(line.Text)) continue;
                    string baseKey = $"{talk.AncientId}.talk.{n.CharEntry}.{x}-{y}{r}";
                    dict[baseKey + (line.AncientSpeaks ? ".ancient" : ".char")] = line.Text;
                    if (y < dlg.Lines.Count - 1)
                        dict[baseKey + ".next"] = string.IsNullOrWhiteSpace(line.NextText) ? "继续" : line.NextText;
                }
            }
        }

        return LocJson(dict);
    }
    /// <summary>
    /// 把「本体状态改名」要一起换掉的本体条目合进某张表（键相同就覆盖）。
    /// 例：把中毒改名成剧毒后，本体卡面描述里那句「施加 2 层中毒」也要变成「剧毒」。
    /// </summary>
    public static string MergeVanillaText(string json, IEnumerable<(string Table, string Key, string Text)> replacements, string table)
    {
        var hit = replacements.Where(r => string.Equals(r.Table, table, StringComparison.Ordinal)).ToList();
        if (hit.Count == 0) return json;

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        foreach (var r in hit) dict[r.Key] = r.Text;
        return LocJson(dict);
    }

    /// <summary>延迟 Power 的名称与描述；外加「本体状态改写」要覆盖的本体键（改名/改描述）。</summary>
    public static string PowersJson(CharacterProfile p)
    {
        var dict = new Dictionary<string, string>();
        foreach (var e in CSharpCodeGen.CollectDelayedEffects(p))
        {
            string entry = Naming.EntryOf(CSharpCodeGen.DelayedPowerClassName(e, p));
            string powerName = EffectCatalog.PowerName(e.PowerId);
            bool toSelf = e.TargetSide == "Self";
            dict[$"{entry}.title"] = $"下回合：{powerName}";
            dict[$"{entry}.description"] = toSelf ? $"下回合开始时获得{powerName}。" : $"下回合开始时对敌人施加{powerName}。";
            dict[$"{entry}.smartDescription"] = toSelf
                ? $"下回合开始时获得 {{Amount}} 层{powerName}。"
                : $"下回合开始时对所有敌人施加 {{Amount}} 层{powerName}。";
        }
        // 本体状态改写：键和本体 powers 表同名，本体加载 mod 本地化表时会 MergeWith 覆盖掉本体的值
        foreach (var kv in VanillaPowerGen.LocEntries(p))
            dict[kv.Key] = kv.Value;
        // 「临时增益」的临时 Power：状态栏里显示成「临时 X」，说明写清什么时候撤。
        // ⚠ 两档撤法的措辞必须分开写 —— 以前这一段的文案是写死的「回合结束时消失」，
        //    「下回合开始时才撤」那一档的悬停提示就会骗人（状态栏上写着回合结束就没了，实际还在）。
        foreach (var e in CSharpCodeGen.CollectTempPowerEffects(p))
        {
            bool nextTurn = e.TempPowerReverse == TempPowerReverses.NextTurnStart;
            bool negative = e.Amount < 0;
            string entry = Naming.EntryOf(CSharpCodeGen.TempPowerClassName(e, p));
            string powerName = EffectCatalog.PowerName(e.PowerId);
            // 负数档是「临时扣层」，措辞要写「-N，到点加回来」，不能写「+{Amount}」
            string body = negative
                ? $"{powerName} -{-e.Amount}"
                : $"{powerName} +{{Amount}}";
            string tail = negative
                ? (nextTurn ? "撑过这一回合和敌人的回合，下回合开始时加回来。" : "回合结束时加回来。")
                : (nextTurn ? "撑过这一回合和敌人的回合，下回合开始时消失。" : "回合结束时消失。");
            dict[$"{entry}.title"] = $"临时{powerName}";
            dict[$"{entry}.description"] = $"本回合内{body}，{tail}";
            dict[$"{entry}.smartDescription"] = $"本回合内{body}，{tail}";
        }
        // 「透支能量」的负债 Power：它会挂在玩家身上（状态栏里有个图标，数量 = 下回合要少几点能量）。
        // 少了这段，游戏在 powers 表里查不到 title/description，就会把**原始键名**原样印在悬停提示上
        // （用户实测截图：powers.SPARKLE_FORGE_ENERGY_DEBT_POWER.title / …description）。
        if (CSharpCodeGen.UsesEnergyDebt(p))
        {
            string entry = Naming.EntryOf(Naming.From(p).EnergyDebtPowerClass);
            dict[$"{entry}.title"] = "能量透支";
            dict[$"{entry}.description"] = "下回合少 {Amount} 点能量。";
            dict[$"{entry}.smartDescription"] = "下回合少 {Amount} 点能量。";
        }
        // 「额外获得一回合」的 Power 同理（它会一直挂在状态栏里，直到把额外回合用掉）
        if (CSharpCodeGen.UsesExtraTurn(p))
        {
            string entry = Naming.EntryOf(Naming.From(p).ExtraTurnPowerClass);
            dict[$"{entry}.title"] = "额外回合";
            dict[$"{entry}.description"] = "本回合结束后额外获得一个回合。";
            dict[$"{entry}.smartDescription"] = "本回合结束后额外获得一个回合。";
        }
        // 「替主人承伤」的守卫 Power：它挂在**伙伴**身上（IsVisibleInternal => false，不显示图标），
        // 但游戏 / BaseLib 仍会去 powers 表查它的 title（日志里会刷
        // `GetRawText: Key '<角色>FORGE_PET_GUARDIAN_POWER.title' not found in table 'powers'`），
        // 所以照样补一条，别让日志里出现找不到键的警告。
        if (PetGen.UsesGuardianEffect(p) || PetGen.Enabled(p).Any(s => s.TakesDamageForOwner))
        {
            string entry = Naming.EntryOf(Naming.From(p).GuardianPowerClass);
            dict[$"{entry}.title"] = "替主人承伤";
            dict[$"{entry}.description"] = "这只伙伴会替你承受可格挡的攻击伤害（中毒 / 失去生命这类穿盾伤害照旧打在你身上）。";
            dict[$"{entry}.smartDescription"] = dict[$"{entry}.description"];
        }
        // 「强化指定卡牌」的强化 Power：它会挂在状态栏里（层数 = 加多少），
        // 少了这段游戏查不到 title/description，悬停提示就会把原始键名原样印出来。
        foreach (var e in CSharpCodeGen.CollectBoostEffects(p))
        {
            string entry = Naming.EntryOf(CSharpCodeGen.BoostPowerClassName(e, p));
            string card = CardNameOf(p, e.SpawnCardId);
            bool block = e.BoostStat == "Block";
            dict[$"{entry}.title"] = $"强化：{card}";
            dict[$"{entry}.description"] = block
                ? $"你打出的{card}额外获得 {{Amount}} 点格挡，本场战斗内持续。"
                : $"你打出的{card}额外造成 {{Amount}} 点伤害，本场战斗内持续。";
            dict[$"{entry}.smartDescription"] = dict[$"{entry}.description"];
        }
        // 自定义状态（能力牌用）：键就是本体的规则 Id.Entry + ".title"（PowerModel.Title 默认就这么取）
        // ===== V0.2.209：几个「辅助 Power」的 title / description =====
        // 它们挂在玩家身上、界面上不显示图标，但游戏 / BaseLib 仍会去 powers 表查 title ——
        // 少了就刷 `GetRawText: Key '<角色>FORGE_TURN_END_PLAY_POWER.title' not found in table 'powers'`
        //（用户日志里那条）。**用到哪个才写哪个**：没用到的老存档生成文本一个字符都不变。
        {
            var hn = Naming.From(p);
            void HelperPower(string cls, string title, string desc)
            {
                string en = Naming.EntryOf(cls);
                dict[$"{en}.title"] = title;
                dict[$"{en}.description"] = desc;
                dict[$"{en}.smartDescription"] = desc;
            }
            if (CSharpCodeGen.UsesTurnEndPlay(p))
                HelperPower(hn.TurnEndPlayPowerClass, "回合结束自动打出", "这张牌会在回合结束时自动打出。");
            if (CSharpCodeGen.UsesTempKeywordPower(p))
                HelperPower(hn.TempKeywordPowerClass, "临时关键词", "这张牌暂时带着这个关键词，到点自动摘掉。");
            if (CSharpCodeGen.UsesTempUpgrade(p))
                HelperPower(hn.TempUpgradePowerClass, "临时升级", "这张牌仅在本回合是升级状态，到点还原。");
            if (CSharpCodeGen.NeedsNoHealPower(p))
                HelperPower(hn.NoHealPowerClass, "禁止回复生命", "拥有者无法回复生命（层数 = 剩余回合）。");
            if (CSharpCodeGen.NeedsDrawCostPower(p))
                HelperPower(hn.DrawCostPowerClass, "抽到就减费", "这期间每抽到一张牌，那张牌的费用 -1（层数 = 剩余回合）。");
            if (CSharpCodeGen.UsesRaiseResourceCap(p))
                HelperPower(CSharpCodeGen.StarCapPowerClass(p), "额外资源量上限", "额外资源量的上限提高了（层数 = 提高多少）。");
            if (CSharpCodeGen.UsesEnergyNextTurnDebt(p))
                HelperPower(hn.EnergyNextTurnDebtPowerClass, "透支能量", "下回合少 {Amount} 点能量。");
        }
        for (int i = 0; i < p.CustomPowers.Count; i++)
        {
            var cp = p.CustomPowers[i];
            if (!CustomPowerGen.IsActive(cp)) continue;
            string entry = CustomPowerGen.EntryOf(p, cp, i);
            // 描述里的「额外资源量」也换成用户自己填的名字（触发时机名里就带着这个词：
            // 「花掉额外资源量后」—— 不换的话状态描述和计数器上是两个名字）
            string desc = RenameResource(CustomPowerGen.DescriptionOf(cp), p);
            dict[$"{entry}.title"] = cp.Name.Trim();
            dict[$"{entry}.description"] = desc;
            dict[$"{entry}.smartDescription"] = desc;   // 战斗里的悬停提示优先用它
            // V0.2.145（用户实测的「自定义状态选生成卡牌会卡死」）：
            // 状态的效果里如果有「要弹选牌界面」的（生成卡牌 + 按范围随机 + 多选1 > 3 / 从范围里自己挑、
            // 从某一摞拿牌到手牌、复制卡牌自己选、重放自己选…），就必须补这一条本地化 ——
            // 本体 **PowerModel.SelectionScreenPrompt 与 CardModel 是同一份实现**：键不存在时
            // 直接抛 `InvalidOperationException: No selection screen prompt for POWER.XXX.`；
            // 抛在战斗回合钩子（例：玩家回合开始时）里 = 本轮 turn loop 直接死掉 → **战斗卡住**。
            // 卡牌 / 遗物 / 药水三条链早就有这一条，只有自定义状态这条漏了（V0.2.145 补上）。
            // ⚠ 判据用的是**通用那一份** NeedsSelectPrompt —— 它把「从某一摞拿牌到手牌」也算进来，
            // 而状态链上那几种走的是**静态**提示（CardSelectorPrefs.DiscardSelectionPrompt 之类）、用不到这条键
            // → 会多写一条没人读的本地化。这是**故意**的：多一条空键毫无副作用，漏一条就是「本体抛异常、战斗卡住」。
            var powerEffects = cp.Triggers.Where(t => t is not null).SelectMany(t => t.Effects)
                .Where(e => e is not null)
                .Concat(GraphEffects(cp.ForgeGraph))
                .ToList();
            if (NeedsSelectPrompt(powerEffects))
                dict[$"{entry}.selectionScreenPrompt"] = SelectPromptText(powerEffects);
        }
        return LocJson(dict);
    }

    // ---------- 描述自动生成 ----------
    private static string Describe(IEnumerable<EffectSpec> effects, CharacterProfile p, string? potionTarget = null, bool isCard = false, bool starCostIsX = false,
        bool literalAmount = false)
    {
        var sb = new StringBuilder();
        var list = effects as IList<EffectSpec> ?? effects.ToList();
        // 同名变量会带别名（Damage2 这种），描述里的 {占位符} 必须和生成代码一致
        var varMap = CSharpCodeGen.VarNamesOf(list);
        foreach (var e in list)
        {
            // 数值 0 且升级也不加数值：连描述都不写（用户要求：游戏里不显示这条效果）
            if (CSharpCodeGen.IsInertZero(e)) continue;
            var one = new StringBuilder();
            string text = DescribeEffect(e, p, potionTarget, isCard, starCostIsX, varMap, literalAmount: literalAmount);
            // 斩杀条件写在**这句效果的最前面**（本体 FEED「造成…伤害。\n[gold]斩杀[/gold]时，永久获得…最大生命值。」），
            // 不跟别的条件一样单独占一行 —— 那样读起来像「另一条效果」。
            // 卡牌时机（需求⑩）同理：写成「当这张牌被消耗时，…」贴在效果前面（读起来才像一句条件句）。
            if (text.Length > 0 && e.Condition is { Kind: "Fatal" or "CardTiming" })
                text = EffectConditionInline(e.Condition, p) + text;
            if (text.Length > 0) one.Append(text).Append('\n');
            // 条件选项是「每条效果各自一份」的，所以条件说明紧跟在它管的那条效果后面
            // （药水不支持条件，所以药水不写；斩杀 / 卡牌时机那两句已经在上面拼到最前面了）
            if (potionTarget is null && e.Condition is not { Kind: "Fatal" or "CardTiming" })
            {
                string cond = EffectConditionInline(e.Condition, p);
                if (cond.Length > 0) one.Append(cond).Append('\n');
            }
            if (one.Length == 0) continue;
            string block = one.ToString().TrimEnd('\n');
            // 数值 0、靠升级增量才有数值：用本体的 {IfUpgraded:show:…} 包起来 ——
            // 没升级时整条不显示（升级预览里本体自己会用绿色写出来）。逐行包：格式串里跨行不好读也不好查。
            if (CSharpCodeGen.IsUpgradeOnlyZero(e))
                block = string.Join("\n", block.Split('\n').Select(line => "{IfUpgraded:show:" + line + "}"));
            sb.Append(block).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// 条件选项写进卡面 / 遗物描述：让玩家在游戏里就能看到「什么条件下才生效」。
    /// 本体「取回 / 死亡之门 / 怀表」这类卡牌也都会把条件写在描述里。
    /// </summary>
    private static string ConditionSuffix(ConditionSpec? condition, bool isCard, CharacterProfile? p = null) =>
        ConditionInline(condition, isCard ? (condition?.UnplayableWhenUnmet == true ? "（不满足时无法打出）" : "（不满足时这张牌的效果不生效）") : "（不满足时不触发）", p);

    /// <summary>每条效果自己的条件：紧跟在那条效果的描述后面。</summary>
    private static string EffectConditionInline(ConditionSpec? condition, CharacterProfile? p = null)
    {
        // 斩杀（本体 Fatal）：照本体那几张牌的写法写成「[gold]斩杀[/gold]时，…」
        // （本体 FEED「[gold]斩杀[/gold]时，永久获得…」/ HAND_OF_GREED「[gold]斩杀[/gold]时，获得…金币」），
        // 而不是通用的「条件：…（不满足时这条效果不生效）」——那句话在攻击牌上读起来很怪。
        if (condition is { Kind: "Fatal" }) return "[gold]斩杀[/gold]时，";
        // 卡牌时机（需求⑩）：写成「当这张牌被消耗时，…」—— 它描述的也是「什么时候发生」，不是「打出的条件」。
        if (condition is { Kind: "CardTiming" })
            return EffectCatalog.CardTimingPhrase(condition.Timing) + "，";
        return ConditionInline(condition, "（不满足时这条效果不生效）", p);
    }

    private static string ConditionInline(ConditionSpec? condition, string tail, CharacterProfile? p = null)
    {
        if (condition is null || condition.IsNone) return "";
        // 条件里的「额外资源量」要用用户自己填的名字（计数器上显示的就是那个名字）
        string text = CSharpCodeGen.ConditionText(condition, p is null ? null : ResourceName(p));
        if (text.Length == 0) return "";
        return "条件：" + text + tail;
    }

    /// <summary>
    /// 状态在描述里的名字：用户在本体状态改写里给它改过名就用新名字
    /// （游戏里显示的是新名字，描述里不该还是旧名字）。
    /// </summary>
    private static string PowerNameFor(CharacterProfile p, string? powerId)
    {
        string vanilla = EffectCatalog.PowerName(powerId);
        if (string.IsNullOrWhiteSpace(powerId)) return vanilla;
        foreach (var o in VanillaPowerGen.Active(p))
        {
            if (!string.Equals(o.PowerId?.Trim(), powerId.Trim(), StringComparison.Ordinal)) continue;
            string name = (o.Name ?? "").Trim();
            if (name.Length > 0) return name;
        }
        return vanilla;
    }

    /// <summary>
    /// 卡牌类名 → 卡面描述里显示的名字。
    /// 本模组自己的卡用你填的中文名（如 SevenCard9 → 「自己卡」）；本体卡用本体中文名。
    /// </summary>
    private static string CardNameOf(CharacterProfile p, string? cardId)    {
        if (string.IsNullOrWhiteSpace(cardId)) return "卡牌";
        string cls = cardId.Trim();
        var n = Naming.From(p);
        for (int i = 0; i < p.Cards.Count; i++)
            if (string.Equals(n.CardClassName(p, p.Cards[i]), cls, StringComparison.Ordinal))
                return string.IsNullOrWhiteSpace(p.Cards[i].Name) ? cls : p.Cards[i].Name;
        return EffectCatalog.FindCardZh(cls);
    }

    /// <summary>药水作用目标 → 描述里的措辞（必须和生成的代码一致）。</summary>
    private static string PotionTargetPhrase(string? targetType) => targetType switch
    {
        "AnyEnemy" => "对指定敌人",
        "AllEnemies" => "对所有敌人",
        _ => "对自己",
    };

    /// <summary>
    /// 「召唤伙伴 / 伙伴攻击」在卡面描述里的那只召唤物的名字。
    /// 现在是列表：按效果上的 <see cref="EffectSpec.PetSummon"/>（稳定标识 = 宠物类名）挑那一只，
    /// 留空（老存档）时退回第一只启用的召唤物（和上一版单只召唤物的行为一致）。
    /// 一只都没有（或选的那只被删了）时返回「伙伴」这种读得通的词 —— 校验器会另外报错拦住。
    /// </summary>
    private static string SummonName(CharacterProfile p, EffectSpec e)
    {
        var def = PetGen.Resolve(p, e.PetSummon);
        if (def is not null) return def.DisplayName;
        string want = (e.PetSummon ?? "").Trim();
        return want.Length > 0 ? want : "伙伴";
    }

    /// <summary>
    /// 卡面描述里那只召唤物的称呼：选了「全部召唤物」时是「全部 N 只召唤物」，
    /// 否则就是那一只的名字（<see cref="SummonName"/>）。
    /// </summary>
    private static string SummonNameOrAll(CharacterProfile p, EffectSpec e) =>
        PetGen.IsAll(e.PetSummon) ? $"全部 {PetGen.All(p).Count} 只召唤物" : SummonName(p, e);

    /// <summary>
    /// 「伙伴攻击（按生命值算）」的卡面描述。
    /// **绝不能写死数字** —— 伤害是打出时按那只宠物当时的最大/当前/已损失生命算出来的，
    /// 所以只写 <c>{变量名:diff()}</c> 让本体去算（升级增量也会跟着显示）。
    /// 后半句写明伤害等于宠物的哪个生命值（照本体「重压 Crush / 榨取」那类牌的写法）。
    /// </summary>
    private static string PetCalcDamageText(CharacterProfile p, EffectSpec e, string var, string repeat,
        string when, string target, string hitSuffix, string which)
    {
        string who = SummonNameOrAll(p, e);
        // 「全部召唤物」：每只各自按自己的生命值算，数字个个不同 —— 卡面没法只显示一个数，
        // 所以写清「各自算、卡面不显示具体数值」，而不是硬塞一个会误导人的数字。
        if (PetGen.IsAll(e.PetSummon))
            return $"{repeat}{when}让[gold]{who}[/gold]{target}造成伤害{hitSuffix}。\n"
                 + $"每只的伤害各自按它自己的{which}算（每只数字都不同，卡面不显示具体数值）。";
        return $"{repeat}{when}让[gold]{who}[/gold]{target}造成{var}点伤害{hitSuffix}。\n"
             + $"此伤害等于[gold]{who}[/gold]的{which}。";
    }

    /// <summary>
    /// 「牺牲伙伴」的卡面描述：先写「若伙伴存活」，再写收益（格挡 / 伤害 / 获得状态）。
    ///
    /// 收益选「获得增益 / 减益」时写「获得 N 层某个状态」—— 和另外两档同一个句式，
    /// 数字一律用 <c>{变量:diff()}</c>（层数是打出时按伙伴生命值算出来的，写死就是错的）。
    /// </summary>
    private static string PetSacrificeText(CharacterProfile p, EffectSpec e, string var)
    {
        string who = SummonName(p, e);
        bool block = e.PetSacrificeGain != "Damage";
        bool power = e.PetSacrificeGain == "Power";
        // 状态名（改过名 / 自定义状态都走同一个渲染，和「给伙伴施加状态」那条的措辞一致）
        string powerZh = PowerNameFor(p, e.PowerId);
        // V0.2.200（用户要求「召唤物即死：收益的造成伤害能不能加个选择对象」）：
        // 收益 = 伤害时打谁跟着「作用对象」那一栏走，描述里也得说对 —— 以前一律印「指定敌人」，
        // 玩家把作用对象改成「全体敌人」后卡面还在说「指定敌人」，等于在骗人。
        string foeZh = e.TargetSide switch
        {
            "AllEnemies" => "所有敌人",
            "RandomEnemies" => e.RepeatIsX ? "随机 X 个敌人" : $"随机 {Math.Max(1, (int)e.RepeatCount)} 个敌人",
            "AllUnits" => "场上所有单位",
            "Ally" => "指定队友",
            "AllAllies" => AllAlliesZh(e),
            _ => "指定敌人",
        };
        if (PetGen.IsAll(e.PetSummon))
        {
            // 「全部召唤物」：每只各自牺牲一次，收益也各自算一遍（有几只存活就算几次）
            // 注意：三种收益里「获得增益 / 减益」必须**先判**——它同时满足 block（非伤害就是给自己），
            // 所以 block 放前面会把状态那一档吃掉（写成「获得 N 点格挡」）。
            string per = e.PetSacrificeFormula switch
            {
                "Fixed" => power
                    ? $"你就获得 {var} 层{powerZh}"
                    : block ? $"你就获得 {var} 点[gold]格挡[/gold]" : $"它就对{foeZh}造成 {var} 点伤害",
                "CurHp" => power
                    ? $"你就获得等同于它当前生命值的{powerZh}层数"
                    : block ? "你就获得等同于它当前生命值的[gold]格挡[/gold]"
                            : $"它就对{foeZh}造成等同于它当前生命值的伤害",
                _ => power
                    ? $"你就获得等同于它最大生命 × {e.PetSacrificeMultiplier:0.##} 的{powerZh}层数"
                    : block ? $"你就获得等同于它最大生命 × {e.PetSacrificeMultiplier:0.##} 的[gold]格挡[/gold]"
                            : $"它就对{foeZh}造成等同于它最大生命 × {e.PetSacrificeMultiplier:0.##} 的伤害",
            };
            return $"[gold]{SummonNameOrAll(p, e)}[/gold]各自牺牲：每死去一只，{per}。";
        }
        string pay = power
            ? $"然后你获得{var}层{powerZh}。"
            : block ? $"然后你获得{var}点[gold]格挡[/gold]。" : $"然后它对{foeZh}造成{var}点伤害。";
        return $"若[gold]{who}[/gold]存活：它死去，{pay}";
    }

    /// <summary>
    /// 一条效果写成卡面中文。
    ///
    /// <paramref name="targetOverride"/> / <paramref name="whoOverride"/> / <paramref name="varOverride"/> 是给
    /// 「节点图 → 卡面描述」那条路用的（见 <see cref="GraphCardText"/>）：
    ///   · 锻造卡的目标可以是「全部召唤物 / 全体单位」这种效果列表没有的档 → 直接给它整句措辞；
    ///   · 锻造卡**不声明动态变量**，所以 {} 占位符没法用 → 数字/公式由调用方算好整串传进来。
    /// 三个都为 null 时行为和以前**逐字一致**（效果列表那条路一个字都不改）。
    /// </summary>
    internal static string DescribeEffect(EffectSpec e, CharacterProfile p, string? potionTarget = null, bool isCard = false, bool starCostIsX = false,
        Dictionary<EffectSpec, string>? varMap = null, string? targetOverride = null, string? whoOverride = null, string? varOverride = null,
        // V0.2.209：主人是「锻造图说了算」的遗物 / 药水 / 状态时，源码里**不声明动态变量** →
        // 描述必须写数字字面量，否则游戏格式化时会抛
        // `Localization formatting error! No source extension could handle the selector named "VulnerablePower"`。
        bool literalAmount = false)
    {
        // 「数值 = X」的卡牌：描述里写 X，而不是那个用不上的固定数值。
        // 变量名必须用 DisplayVarNameOf（= CanonicalVars 里真实声明的那个键）：三个「按生命值算」的
        // 伙伴攻击 / 牺牲伙伴声明的是本体的 CalculatedDamage / CalculatedBlock，直接用 VarNameOf 的
        // 内部名字会让卡面原样印出 {PetMissingHpDamage:diff()}（用户实测截图报过）。
        //
        // 「数值公式」（需求：「另加一栏『数值公式』」）：卡面上**直接写公式本身**（例：「造成 M*3 点伤害」）。
        // 为什么不用动态变量那个 {…:diff()}：那个变量的值只是占位（打出时才现算），写它会骗人 ——
        // 填了 M*3 却在卡面上写「造成 6 点伤害」。玩家想知道具体数字可以自己写「自定义描述」。
        // 这里也不能带 :diff()：升级增量对公式那条不生效（公式取代了数值），带了反而显示一个假的对比值。
        // 「数值公式」的卡面数字（用户要求：卡面要看到**算出来的数字**，不是「M/2」这种公式文本）：
        // 生成端给公式的 5 种效果各声明了一个「现算预览值」的动态变量
        //（见 CSharpCodeGen.FormulaVarsSource），所以这里照旧用 {…:diff()} 占位符 ——
        // 游戏在卡面上会填上「按当前属性算出来」的数字（力量 / 敏捷那些加成也照常叠）。
        // 以前的写法是直接印公式本身（「造成 M/2 点伤害」），玩家看不懂，用户报过。
        string? formulaText = e.UsesAmountFormula && isCard ? ForgeFormula.DisplayText(e.AmountFormula) : null;
        string var = varOverride
            ?? (e.AmountIsXEffective && isCard ? "X"
                : literalAmount ? e.Amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                : "{" + CSharpCodeGen.DisplayVarNameOf(e, varMap) + ":diff()}");
        // 「取任意张 / 最多 N 张 / 全部」（需求：「消耗任意张手牌」+「选择所有手牌」）：
        // 任意 = 不写数字；最多 = 前面加「最多」；全部 = 写「所有」（**不弹选牌界面**，本体「恶魔之焰」那种）。
        if (e.CountIsAll) var = "所有";
        else if (e.CountIsAny) var = "任意";
        else if (e.CountMode == "UpTo") var = "最多 " + var;
        // 「任意」= 任意张；「全部」= 所有（不补量词，「所有 张」不通）；数字才补「张」
        string amountPhrase = e.CountIsAll ? "所有" : e.CountIsAny ? "任意张" : var + " 张";
        // 「全部」那一档不写「自己选 / 随机 / 牌顶」——那一档没有选牌界面、也不是随机（和 V0.2.161 同一条规矩：
        // 不生效 / 藏起来的栏位，文案里也不许写）。
        string pickWord = e.CountIsAll ? "" : EffectCatalog.CardPickZh(e.CardPick);
        string when = e.NextTurn ? "下回合开始时，" : "";
        int times = Math.Max(1, e.Times);
        // 「生效次数」写了公式（用户要求⑦）：卡面印公式本身（和「数值公式」同一套观感），
        // 不然玩家看到的是一个和实际不符的固定次数。
        string timesText = !string.IsNullOrWhiteSpace(e.TimesFormula) ? ForgeFormula.DisplayText(e.TimesFormula) : times.ToString();
        string repeat = e.TimesIsX && isCard ? "重复 X 次：" : (times > 1 || !string.IsNullOrWhiteSpace(e.TimesFormula)) ? $"重复 {timesText} 次：" : "";
        // 「命中次数 / 对群数」同理：写了公式就印公式
        string repeatText = !string.IsNullOrWhiteSpace(e.RepeatFormula)
            ? ForgeFormula.DisplayText(e.RepeatFormula)
            : Math.Max(1, e.RepeatCount).ToString();
        // 药水：统一用「药水作用目标」的措辞（和实际执行一致）
        string target = targetOverride ?? potionTarget ?? e.TargetSide switch
        {
            "Enemy" => "对指定敌人",
            "Ally" => "对指定队友",
            "AllEnemies" => "对所有敌人",
            // V0.2.132：「全部友方」勾了「不包括召唤物」时卡面写清（默认档一个字都不加）
            // V0.2.201：生成端改成遍历 CombatState.Allies（玩家这一边：玩家本体 + 召唤物），
            //   而且多了「包括自己」这个子选项 —— 措辞统一由 AllAlliesZh 出，别在这里各写一份。
            "AllAllies" => "对" + AllAlliesZh(e),
            // V0.2.132 新档「全体单位」= 两边所有存活单位
            "AllUnits" => "对所有单位",
            "RandomEnemies" => e.RepeatIsX && isCard ? "对随机敌人 X 次" : $"对随机敌人 {repeatText} 次",
            _ => "对自己",
        };

        // 伤害的命中次数：随机目标已经在 target 里写了次数，其余情况补充在「造成…伤害」后面
        string hitSuffix = e.TargetSide == "RandomEnemies"
            ? ""
            : e.RepeatIsX && isCard ? " X 次"
            : (e.RepeatCount > 1 || !string.IsNullOrWhiteSpace(e.RepeatFormula)) ? $" {repeatText} 次"
            : "";

        // 能打敌人的效果（格挡 / 回复生命 / 失去生命 / 最大生命）：作用对象不是自己时要写清「给谁」
        string who = whoOverride ?? (potionTarget is not null
            ? potionTarget.TrimStart('对')                      // 「对所有敌人」→「所有敌人」
            : e.TargetSide switch
            {
                "Enemy" => "指定敌人",
                "Ally" => "指定队友",
                "AllEnemies" => "所有敌人",
                "AllAllies" => AllAlliesZh(e),
                "AllUnits" => "所有单位",
                "RandomEnemies" => e.RepeatIsX && isCard ? "随机敌人 X 次" : $"随机 {repeatText} 个敌人",
                _ => "",
            });
        string forWho = who.Length == 0 ? "" : $"让{who}";

        string text = e.Kind switch
        {
            // 伤害：命中次数 > 1（或 = X）要写出来，本体「旋风斩 / 天际钻头」的描述就是「造成 X 点伤害 X 次」
            // V0.2.194：「等同于本牌获得格挡」那种写法（先获得格挡、再按格挡打伤害，像本体「全身撞击」）
            // 卡面印不出数字（这条读数没有现算预览值），改印一句说人话的。
            "Damage" when CSharpCodeGen.IsExactThisCardBlock(e.AmountFormula) =>
                $"{repeat}{when}{target}造成等同于本牌获得格挡的伤害{hitSuffix}。",
            "Damage" => e.TargetSide == "Self" ? $"受到 {var} 点伤害。" : $"{repeat}{when}{target}造成 {var} 点伤害{hitSuffix}。",
            // 格挡 / 回复生命 / 失去生命 / 最大生命：自己以外要写清对象（能给敌人加格挡 / 回血了）
            // 负数 = 失去格挡（生成的是 CreatureCmd.LoseBlock；本体 GainBlock 对非正数什么都不做）
            "Block" => e.Amount < 0 && var != "X" && formulaText is null
                ? (who.Length == 0
                    ? $"{repeat}{when}失去 {-e.Amount} 点格挡。"
                    : $"{repeat}{when}{forWho}失去 {-e.Amount} 点格挡。")
                : (who.Length == 0
                    ? $"{repeat}{when}获得 {var} 点格挡。"
                    : $"{repeat}{when}{forWho}获得 {var} 点格挡。"),
            "Draw" => $"{repeat}{when}抽 {var} 张牌。",
            // 刮削（本体卡「刮削 Scrape」）：抽 N 张，然后对其中符合条件的牌做选定的操作。
            // 默认那一档必须与旧版逐字一致（「丢弃其中所有非 0 费牌」）—— 卡面描述的回归由 --uicheck 钉死。
            "ScrapeDraw" => $"{repeat}{when}抽 {var} 张牌，{ScrapeTailZh(p, e)}。",
            // V0.2.154：播放音效（数值那一栏 = 音量%；默认音量时卡面不写音量）
            "PlaySfx" => $"{repeat}{when}播放音效：{EffectCatalog.SfxDisplay(e.Sfx)}"
                + (e.SfxVolumePercent == 100m ? "" : $"（音量 {e.SfxVolumePercent:0.##}%）") + "。",
            // V0.2.155：播放上传的音频文件（音效名 = 它在你「美术资源」页里的名字）
            "PlaySoundFile" => $"{repeat}{when}播放音频：{EffectCatalog.AudioAssetName(p?.AudioAssets, e.Sfx)}"
                + (e.SfxVolumePercent == 100m ? "" : $"（音量 {e.SfxVolumePercent:0.##}%）") + "。",
            // 透支：现在拿 N 点，下回合少 N 点（走生成的负债 Power —— 本体 GainEnergy 会忽略负数）
            "OverdraftEnergy" => $"{repeat}{when}获得 {var} 点能量，下回合少 {var} 点能量。",
            // 负数 = 失去（生成的是 LoseEnergy：本体 GainEnergy 对非正数直接返回）
            "Energy" => e.Amount < 0 && var != "X"
                ? $"{repeat}{when}失去 {-e.Amount} 点能量。"
                : $"{repeat}{when}获得 {var} 点能量。",
            "Heal" => e.Amount < 0 && var != "X" && formulaText is null
                ? (who.Length == 0 ? $"失去 {-e.Amount} 点生命。" : $"{forWho}失去 {-e.Amount} 点生命。")
                : (who.Length == 0 ? $"{repeat}回复 {var} 点生命。" : $"{repeat}{forWho}回复 {var} 点生命。"),
            "HpLoss" => who.Length == 0 ? $"失去 {var} 点生命。" : $"{forWho}失去 {var} 点生命。",
            "MaxHp" => e.Amount < 0 && var != "X"
                ? (who.Length == 0 ? $"失去 {-e.Amount} 点最大生命。" : $"{forWho}失去 {-e.Amount} 点最大生命。")
                : (who.Length == 0 ? $"{repeat}获得 {var} 点最大生命。" : $"{repeat}{forWho}获得 {var} 点最大生命。"),
            // 负数 = 扣除（生成的是 LoseGold：本体 GainGold 对非正数直接返回）
            "Gold" => e.Amount < 0
                ? $"失去 {-e.Amount} 枚金币。"
                : $"获得 {var} 枚金币。",
            // 增加额外资源量上限值：数值 = 抬多少（卡面用 {ResourceCap:diff()}，升级后会自动显示升级值）
            "RaiseResourceCap" => $"{ResourceName(p)}上限 +{(e.AmountIsXEffective && isCard ? "X" : "{ResourceCap:diff()}")}。",
            // 卡牌上的负数 = 这张牌的费用（费用数字显示在卡面星级费用处），遗物/药水上的负数 = 直接扣；
            // 「下回合生效」（正数）走本体 StarNextTurnPower，描述里要写清是下回合开始时发
            "ExtraResource" => starCostIsX && e.Amount < 0 && isCard
                ? $"需要 X 点{ResourceName(p)}（打出时消耗全部）。"
                : e.AmountIsXEffective && isCard
                ? $"{(e.Amount < 0 ? "需要" : "获得")} X 点{ResourceName(p)}。"
                : e.Amount < 0
                    ? (isCard ? $"需要 {-e.Amount * Math.Max(1, e.Times)} 点{ResourceName(p)}。" : $"失去 {-e.Amount * Math.Max(1, e.Times)} 点{ResourceName(p)}。")
                    : $"{repeat}{when}获得 {var} 点{ResourceName(p)}。",
            "EndTurn" => "结束你的回合。",
            // 将此牌放回手牌（值 = 1 时才生效；值 0 的那条会被 IsInertZero 整条丢掉，卡面上一个字都不写）
            "ReturnSelfToHand" => "打出后，将此牌放回手牌。",
            // 抽到这张牌时它自己本场战斗的费用 -N（值 0 = 不生效，由 IsInertZero 丢掉）
            "CheapenSelfOnDraw" => $"抽到时：这张牌本场战斗的费用 -{Math.Max(1, (int)Math.Round(e.Amount, MidpointRounding.AwayFromZero))}。",            "ExtraTurn" => "本回合结束后，额外获得一个回合。",
            // 生成卡牌：三种取卡方式（指定卡 / 按范围随机 N 张 / 多选1）分别有各自的说法。
            // 多选1 的「数值」是**候选张数**，生成出来永远只有 1 张，所以这里不能写「生成 3 张」。
            "GenerateCard" => e.IsSpawnRandom && e.SpawnChosen
                ? $"从「{EffectCatalog.SpawnFilterZhFor(p, e.SpawnFilter)}」中自己挑一张生成，放入{EffectCatalog.SpawnTargetZh(e.SpawnToPile)}。"
                : e.IsSpawnRandom && e.SpawnChoice > 1m
                ? $"从 {Math.Max(2, (int)e.SpawnChoice)} 张「{EffectCatalog.SpawnFilterZhFor(p, e.SpawnFilter)}」中选一张生成，放入{EffectCatalog.SpawnTargetZh(e.SpawnToPile)}。"
                : e.IsSpawnRandom
                ? $"生成 {amountPhrase}「{EffectCatalog.SpawnFilterZhFor(p, e.SpawnFilter)}」，放入{EffectCatalog.SpawnTargetZh(e.SpawnToPile)}。"
                : $"生成 {amountPhrase}{CardNameOf(p, e.SpawnCardId)}，放入{EffectCatalog.SpawnTargetZh(e.SpawnToPile)}。",
            // 获得药水（需求 ⑨）：指定档写清是哪一瓶（药水名从「目标药水」目录里取中文名），
            // 随机档写「随机一瓶药水」。句尾**必须**写明药水栏满时的行为（用户要求：不要静默）——
            // 本体 TryToProcure 在栏满时返回 success=false，我们什么都不做。
            "GainPotion" => e.IsPotionRandom
                ? $"{repeat}{when}获得 {(e.AmountIsXEffective && isCard ? "X" : ((int)e.Amount).ToString())} 瓶随机药水"
                    + "（药水栏满时不会发生任何事）。"
                : $"{repeat}{when}获得 {(e.AmountIsXEffective && isCard ? "X" : ((int)e.Amount).ToString())} 瓶"
                    + $"[gold]{EffectCatalog.FindPotionZh(e.PotionId)}[/gold]（药水栏满时不会发生任何事）。",
            // 「从哪里选牌」不是手牌时不再写「手牌」（以前写死「N 张手牌」，选了弃牌堆就描述不对了）
            // 范围界定（需求 ③）：范围非空时在句尾补一句「（只限攻击牌）」（空则不加，描述与旧版一致）
            // 消耗卡牌：数值 0 = 这张牌自己（用户实测报的：「卡牌时机 = 被保留」时想「消耗自身」）——
            // 和「丢弃卡牌」那一档同款：写「消耗这张牌自己」，不写张数（这一档没有数字）。
            "ExhaustCard" => CSharpCodeGen.AmountZeroIsSelf(e)
                ? "消耗这张牌自己。"
                : e.SelectPile == "Hand"
                    ? $"{pickWord}消耗 {amountPhrase}手牌{CardScopeNote(p, e)}。"
                    : $"{pickWord}从{EffectCatalog.SelectPileZh(e.SelectPile)}消耗 {amountPhrase}牌{CardScopeNote(p, e)}。",
            // 丢弃卡牌：丢进弃牌堆（不是消耗，洗牌后会回来）。丢哪一摞写在描述里：
            // 手牌 = 「丢弃 N 张手牌」；抽牌堆 = 「从抽牌堆里丢弃 N 张牌」。
            // 数值 0 = 这张牌自己（用户要求）：写「丢弃这张牌自己」，不写张数（这一档没有数字）。
            "DiscardCard" => CSharpCodeGen.AmountZeroIsSelf(e)
                ? "丢弃这张牌自己。"
                : e.SelectPile == "Hand"
                    ? $"{pickWord}丢弃 {amountPhrase}手牌{CardScopeNote(p, e)}。"
                    : $"{pickWord}从{EffectCatalog.SelectPileZh(e.SelectPile)}里丢弃 {amountPhrase}牌{CardScopeNote(p, e)}。",
            // 升级卡牌：从哪一摞选、自己选还是随机（本体「武装」那种）
            "UpgradeCard" => e.SelectPile == "Hand"
                ? $"{pickWord}升级 {amountPhrase}手牌（升级不了的牌不会出现在选择里）{CardScopeNote(p, e)}。"
                : $"{pickWord}从{EffectCatalog.SelectPileZh(e.SelectPile)}里升级 {amountPhrase}牌{CardScopeNote(p, e)}。",
            // 预见（一代观者的 Scry）：看抽牌堆顶 N 张，想丢的丢进弃牌堆（可以一张都不丢）
            // 范围界定作用于「看的那几张」：只把符合范围的牌列进选择里
            "Scry" => $"预见 {var}：看抽牌堆顶的 {var} 张牌，把其中任意张丢进弃牌堆（也可以一张都不丢）{CardScopeNote(p, e)}。",
            // 用「目标卡」填满手牌（本体「迫降 CrashLanding」）：张数由手牌上限决定，没有数值
            "FillHand" => $"{repeat}{when}用{CardNameOf(p, e.SpawnCardId)}填满你的[gold]手牌[/gold]。",
            // 抽到就减费：N 回合内每次抽到的牌费用 -1（负数 = 反向扣层数）
            "CheapenDraw" => e.Amount < 0
                ? $"{repeat}{when}解除「抽到就减费」{-e.Amount} 层。"
                : $"{repeat}{when}在 {(int)e.Amount} 回合内，每抽到一张牌，那张牌本场战斗的费用 -1。",
            // 复制卡牌（本体「二刀流 DualWield」那种）：选 / 随机拿 N 张，每张复制 M 份到手牌。
            // 份数 = 1 时按本体的说法写「复制一份」而不是「复制 1 份」（卡面更顺）。
            // 「全部」那一档不写数字 / 不写「自己选」—— 直接把那一摞符合条件的全复制（V0.2.163）。
            // V0.2.226（用户要求）：数值 0 = **这张牌自己**；「复制出来的放到哪一摞」也能选（默认手牌，老文案一字不变）。
            "CopyCard" => e.Amount <= 0m && !e.AmountIsXEffective
                ? "复制这张牌本身"
                  + (!string.IsNullOrWhiteSpace(e.CopiesFormula)
                      ? $" {ForgeFormula.DisplayText(e.CopiesFormula)} 份"
                      : Math.Max(1, e.Copies) == 1 ? "一份" : $" {Math.Max(1, e.Copies)} 份")
                  + $"到{CSharpCodeGen.CopyDest(e).Zh}{CardScopeNote(p, e)}。"
                : (e.CountIsAll
                    ? (e.SelectPile == "Hand" ? "复制所有手牌" : $"复制{EffectCatalog.SelectPileZh(e.SelectPile)}里的所有牌")
                    : e.SelectPile == "Hand"
                        ? $"{(e.CardPick == "Chosen" ? "自己选" : "随机")} {var} 张手牌复制"
                        : $"{(e.CardPick == "Chosen" ? "自己选" : "随机")}{EffectCatalog.SelectPileZh(e.SelectPile)}里的 {var} 张牌复制")
                + (!string.IsNullOrWhiteSpace(e.CopiesFormula)
                    ? $"（每张复制 {ForgeFormula.DisplayText(e.CopiesFormula)} 份到{CSharpCodeGen.CopyDest(e).Zh}）{CardScopeNote(p, e)}。"
                    : Math.Max(1, e.Copies) == 1 ? $"（每张复制一份到{CSharpCodeGen.CopyDest(e).Zh}）{CardScopeNote(p, e)}。" : $"（每张复制 {Math.Max(1, e.Copies)} 份到{CSharpCodeGen.CopyDest(e).Zh}）{CardScopeNote(p, e)}。"),
            // 重放卡牌：本体 CardModel.BaseReplayCount（重放 1 = 打出去时连着打 2 次）
            "ReplayCard" => (e.CountIsAll
                    ? (e.SelectPile == "Hand" ? "让手牌里的所有牌" : $"让{EffectCatalog.SelectPileZh(e.SelectPile)}里的所有牌")
                    : e.SelectPile == "Hand"
                        ? $"让{(e.CardPick == "Chosen" ? "你选" : "随机")}的 {var} 张手牌"
                        : $"让{(e.CardPick == "Chosen" ? "你选" : "随机")}的{EffectCatalog.SelectPileZh(e.SelectPile)}里的 {var} 张牌")
                + (!string.IsNullOrWhiteSpace(e.ReplayFormula)
                    ? $"本场战斗内额外打出 {ForgeFormula.DisplayText(e.ReplayFormula)} 次{CardScopeNote(p, e)}。"
                    : Math.Max(1, e.ReplayTimes) == 1
                        ? $"本场战斗内额外打出一次{CardScopeNote(p, e)}。"
                        : $"本场战斗内额外打出 {Math.Max(1, e.ReplayTimes)} 次{CardScopeNote(p, e)}。"),
            // 回合结束时自动打出：0 = 这张牌自己；≥ 1 = 先选好 N 张，回合结束时打出去。
            // 这里**写数字字面量**（不用 {Cards:diff()}）：这一条效果不声明动态变量
            // （数值 0 有特殊含义，不能当成「0 = 不生效」），写成占位符的话游戏里替换不出来。
            "TurnEndPlay" => e.Amount <= 0m && !e.AmountIsXEffective
                ? "回合结束时自动打出这张牌。"
                : e.CountIsAll
                    ? (e.SelectPile == "Hand"
                        ? $"手牌里的所有牌，回合结束时自动打出它们{CardScopeNote(p, e)}。"
                        : $"{EffectCatalog.SelectPileZh(e.SelectPile)}里的所有牌，回合结束时自动打出它们{CardScopeNote(p, e)}。")
                : (e.SelectPile == "Hand"
                    ? $"{(e.CardPick == "Chosen" ? "自己选" : "随机")} {(e.AmountIsXEffective ? "X" : Math.Max(1, (int)e.Amount).ToString())} 张手牌，回合结束时自动打出它们{CardScopeNote(p, e)}。"
                    : $"{(e.CardPick == "Chosen" ? "自己选" : "随机")}{EffectCatalog.SelectPileZh(e.SelectPile)}里的 {(e.AmountIsXEffective ? "X" : Math.Max(1, (int)e.Amount).ToString())} 张牌，回合结束时自动打出它们{CardScopeNote(p, e)}。"),
            // 毒性爆发 / 大限已至：照本体两张牌的原文写
            "Outbreak" => $"给予所有敌人 {var} 层[gold]中毒[/gold]，并立即触发[gold]中毒[/gold]。",
            "TimesUp" => $"造成等于该敌人身上[gold]灾厄[/gold]层数的伤害。",
            "TransformCard" => (e.CountIsAll
                    ? $"把所有{(e.SelectPile == "Hand" ? "手牌" : EffectCatalog.SelectPileZh(e.SelectPile) + "里的牌")}变化为"
                    : e.SelectPile == "Hand"
                        ? $"{pickWord}将 {amountPhrase}手牌变化为"
                        : $"{pickWord}将{EffectCatalog.SelectPileZh(e.SelectPile)}里的 {amountPhrase}牌变化为")
                + (string.IsNullOrWhiteSpace(e.SpawnCardId) ? "随机卡牌。" : CardNameOf(p, e.SpawnCardId) + "。")
                // V0.2.213：限定「只变化哪些牌」时把范围写进卡面（不然玩家看不出它只动某一类牌）
                + (e.HasScopeCardId ? $"（只变化{CardNameOf(p, e.ScopeCardId)}）"
                    : e.HasTransformPick ? $"（只变化：{e.TransformPickZh}）"
                    : ""),
            // 本体「搜寻 / 全息影像 / 挖掘」那种：从牌堆里挑牌拿到手牌
            // 「全部」那一档没有选牌界面 —— 直接写「把那一摞里符合条件的牌全拿到手牌」。
            "TakeFromDraw" => e.CountIsAll
                ? $"把抽牌堆里符合条件的牌全部拿到手牌{CardScopeNote(p, e)}。"
                : $"从抽牌堆里选 {amountPhrase}牌拿到手牌{CardScopeNote(p, e)}。",
            "TakeFromDiscard" => e.CountIsAll
                ? $"把弃牌堆里符合条件的牌全部拿到手牌{CardScopeNote(p, e)}。"
                : $"从弃牌堆里选 {amountPhrase}牌拿到手牌{CardScopeNote(p, e)}。",
            "TakeFromExhaust" => e.CountIsAll
                ? $"把消耗牌堆里符合条件的牌全部拿到手牌{CardScopeNote(p, e)}。"
                : $"从消耗牌堆里选 {amountPhrase}牌拿到手牌{CardScopeNote(p, e)}。",
            // 放置卡牌（V0.2.116）：数值 0 = 这张牌自己；≥ 1 = 从某一摞（含牌顶）挑 N 张放到「放到哪一摞」
            "PlaceCard" => (e.Amount <= 0m && !e.AmountIsXEffective
                    ? $"把这张牌放到{e.PlaceToZh}。"
                    : e.CountIsAll
                        ? $"把{EffectCatalog.SelectPileZh(e.SelectPile)}里符合条件的牌全部放到{e.PlaceToZh}{CardScopeNote(p, e)}。"
                        : $"{EffectCatalog.CardPickZh(e.CardPick)}从{EffectCatalog.SelectPileZh(e.SelectPile)}拿 {amountPhrase}牌放到{e.PlaceToZh}{CardScopeNote(p, e)}。"),
            // 「直接把「缓慢」设成 N%」时层数没有意义（生成时就按本体做法施加 1 层），所以只写百分比
            // 负数 = 扣除层数（生成的是 PowerCmd.ModifyAmount(-N)：本体 Apply 对负数会新建一个负层数的状态）
            // 暴露 / 清除状态（没有动态变量，数值直接写死）
            "ForbidBlock" => e.Amount < 0
                ? $"{repeat}{when}解除{target}身上「禁止获得格挡」的效果。"
                : $"{repeat}{when}{target}在 {(int)e.Amount} 回合内无法获得格挡。",
            // 禁止生命回复：N 回合内该目标回复量恒为 0（负数 = 反向解除）
            "ForbidHeal" => e.Amount < 0
                ? $"{repeat}{when}解除{target}身上「禁止回复生命」的效果。"
                : $"{repeat}{when}{target}在 {(int)e.Amount} 回合内无法回复生命。",
            "Expose" => $"{repeat}{when}{target}清空格挡与人工制品，并施加 {(int)e.Amount} 层{PowerNameFor(p, e.PowerId).Trim()}。",
            "ClearPowerOne" => $"{repeat}{when}{target}清除{PowerNameFor(p, e.PowerId).Trim()}。",
            "ClearPowerDebuffs" => $"{repeat}{when}{target}清除所有减益状态。",            // 翻倍状态层数（数值 = 倍数，2 = 翻倍；没有动态变量，所以直接写死数字）
            "DoublePower" => (e.TargetSide == "Self"
                    ? $"{repeat}{when}让自己身上 {PowerNameFor(p, e.PowerId).Trim()} 的层数变为 {Math.Clamp((int)e.Amount, 2, 5)} 倍。"
                    : $"{repeat}{when}{target}身上 {PowerNameFor(p, e.PowerId).Trim()} 的层数变为 {Math.Clamp((int)e.Amount, 2, 5)} 倍。"),            "ApplyPower" when CSharpCodeGen.IsExactThisCardDamage(e.AmountFormula) =>
                // 「本张牌这一次打出造成的伤害」（V0.2.194，用户问的「荒疫打击」）：层数是**运行期**从
                // 这一下打出的伤害来的，而卡面上那个「现算预览值」读到的是上一次打出留下的数
                //（见 CSharpCodeGen.ThisCardDamageId 的注释）—— 所以照本体 BlightStrike 的写法印成
                // 「给予等量于所造成伤害的灾厄」，**不印数字**。
                $"{repeat}{when}{target}施加等量于所造成伤害的{PowerNameFor(p, e.PowerId)}。",
            "ApplyPower" when CSharpCodeGen.IsExactThisCardBlock(e.AmountFormula) =>
                // 「本张牌这一次打出获得的格挡」（V0.2.194 追加）：同一条道理，不印数字，
                // 印成「施加等同于此牌获得格挡的 X」。
                $"{repeat}{when}{target}施加等同于本牌获得格挡的{PowerNameFor(p, e.PowerId)}。",
            // V0.2.241：四个「张数」读数（消耗 / 丢弃 / 变化 / 生成）—— 同样不印数字，改成说人话的句子
            "ApplyPower" when CSharpCodeGen.IsExactReading(e.AmountFormula, CSharpCodeGen.ThisCardExhaustId) =>
                $"{repeat}{when}{target}施加等同于本牌消耗张数的{PowerNameFor(p, e.PowerId)}。",
            "ApplyPower" when CSharpCodeGen.IsExactReading(e.AmountFormula, CSharpCodeGen.ThisCardDiscardId) =>
                $"{repeat}{when}{target}施加等同于本牌丢弃张数的{PowerNameFor(p, e.PowerId)}。",
            "ApplyPower" when CSharpCodeGen.IsExactReading(e.AmountFormula, CSharpCodeGen.ThisCardTransformId) =>
                $"{repeat}{when}{target}施加等同于本牌变化张数的{PowerNameFor(p, e.PowerId)}。",
            "ApplyPower" when CSharpCodeGen.IsExactReading(e.AmountFormula, CSharpCodeGen.ThisCardGenerateId) =>
                $"{repeat}{when}{target}施加等同于本牌生成张数的{PowerNameFor(p, e.PowerId)}。",
            "ApplyPower" => CSharpCodeGen.IsSlowPercentEffect(e)
                ? $"{repeat}{when}{target}施加{PowerNameFor(p, e.PowerId)}（受到伤害 +{e.SlowPercentEffective}%）。"
                : (e.Amount < 0 && var != "X" && formulaText is null
                    ? (e.TargetSide == "Self"
                        ? $"{repeat}{when}失去 {-e.Amount} 层{PowerNameFor(p, e.PowerId)}。"
                        : $"{repeat}{when}{target}扣除 {-e.Amount} 层{PowerNameFor(p, e.PowerId)}。")
                    : $"{repeat}{when}{target}施加 {var} 层{PowerNameFor(p, e.PowerId)}。"),
            // 临时增益：两档撤法各写一句（生成的是 <角色>ForgeTemp<状态>[NextTurn] Power）
            // 「获得 / 失去」按数值正负走；「自己 / 敌人」的措辞分开（同一句话套在敌人身上会很别扭）。
            // ⚠ 负数档要写「失去 5 层」而不是「获得 -5 层」（卡面上印负号很难看）。
            "TempPower" => (e.Amount < 0
                    ? (e.TargetSide == "Self"
                        ? $"{repeat}{when}本回合内失去 {-e.Amount} 层{PowerNameFor(p, e.PowerId)}"
                        : $"{repeat}{when}{target}本回合内失去 {-e.Amount} 层{PowerNameFor(p, e.PowerId)}")
                    : (e.TargetSide == "Self"
                        ? $"{repeat}{when}本回合内获得 {var} 层{PowerNameFor(p, e.PowerId)}"
                        : $"{repeat}{when}{target}本回合内施加 {var} 层{PowerNameFor(p, e.PowerId)}"))
                + (e.TempPowerReverse == TempPowerReverses.NextTurnStart
                    ? (e.Amount < 0 ? "（下回合开始时加回来）。" : "（下回合开始时消失）。")
                    : (e.Amount < 0 ? "（回合结束时加回来）。" : "（回合结束时消失）。")),
            // 给予卡牌关键词：数值 = 选几张牌（0 = 这张牌自己）
            "GiveKeyword" => GiveKeywordText(p, e, repeat, when),
            // 强化指定卡牌（像本体「精准」对「小刀」）：本场战斗内，你打出的**那一张卡**伤害 / 格挡 +N
            "BoostCard" => BoostCardText(p, e, var),
            // 击晕：本体里它不是状态（Power）而是「怪物意图」，所以单列一条（本体卡「口哨」那种）
            "Stun" => e.TargetSide == "Self"
                ? $"{repeat}{when}自己被打晕（本回合不行动）。"
                : $"{repeat}{when}击晕{(who.Length == 0 ? "指定敌人" : who)}（本回合不行动）。",
            // ===== 全局（直接改牌组）=====
            "AddCardGlobal" => $"获得 {(e.AmountIsXEffective && isCard ? "X" : ((int)e.Amount).ToString())} 张{CardNameOf(p, e.SpawnCardId)}（加入牌组）。",
            // 挂「战斗胜利后」时是结算界面多一条奖励；挂在战斗中就是当场弹选牌界面（见 CSharpCodeGen.EmitCardReward）
            "CardReward" => $"获得卡牌奖励（{(e.AmountIsXEffective && isCard ? "X" : ((int)e.Amount).ToString())} 选一，选中的加入牌组）。",
            // V0.2.205：数值 0 = 这张牌自己（卡牌 / 卡牌的锻造图） —— 描述要说出来，不然卡面写「删除 0 张」等于骗人
            "RemoveCardGlobal" => e.Amount <= 0m && !e.AmountIsXEffective
                ? "把[gold]这张牌[/gold]从牌组里删掉（永久）。"
                : e.CountIsAll
                ? "从牌组中把能删的牌全部删除。"
                : e.CardPick == "Chosen"
                    ? $"从牌组中自己选 {(e.AmountIsXEffective && isCard ? "X" : ((int)e.Amount).ToString())} 张牌删除。"
                    : $"从牌组中随机删除 {(e.AmountIsXEffective && isCard ? "X" : ((int)e.Amount).ToString())} 张牌。",
            "TransformCardGlobal" => e.Amount <= 0m && !e.AmountIsXEffective
                ? "将牌组里的[gold]这张牌[/gold]变化为" + (string.IsNullOrWhiteSpace(e.SpawnCardId) ? "随机卡牌。" : CardNameOf(p, e.SpawnCardId) + "。")
                : (e.CountIsAll ? "将牌组中的所有牌变化为"
                    : (e.CardPick == "Chosen" ? "将牌组中自己选的 " : "将牌组中随机 ")
                        + $"{(e.AmountIsXEffective && isCard ? "X" : ((int)e.Amount).ToString())} 张牌变化为")
                + (string.IsNullOrWhiteSpace(e.SpawnCardId) ? "随机卡牌。" : CardNameOf(p, e.SpawnCardId) + "。"),
            // 升级牌组里的牌（永久、跨战斗）：本体「香盒 Pomander」那种
            "UpgradeCardGlobal" => e.Amount <= 0m && !e.AmountIsXEffective
                ? "升级牌组里的[gold]这张牌[/gold]（永久）。"
                : e.CountIsAll
                ? "从牌组中把能升级的牌全部升级。"
                : e.CardPick == "Chosen"
                    ? $"从牌组中自己选 {(e.AmountIsXEffective && isCard ? "X" : ((int)e.Amount).ToString())} 张牌升级。"
                    : $"从牌组中随机升级 {(e.AmountIsXEffective && isCard ? "X" : ((int)e.Amount).ToString())} 张牌。",
            // ===== 召唤伙伴（本体的通用宠物 API，不需要补丁）=====
            // 数值 0 = 用「召唤物」页里配置的血量，这时不写具体数字（避免卡面写「召唤伙伴 0 点生命」误导人）
            "SummonPet" => (e.AmountIsXEffective && isCard)
                ? $"召唤{SummonNameOrAll(p, e)}（{var} 点生命）。"
                : e.Amount <= 0
                    ? (PetGen.IsAll(e.PetSummon)
                        ? $"召唤{SummonNameOrAll(p, e)}（各自按「召唤物」页里配置的血量）。"
                        : $"召唤{SummonName(p, e)}。")
                    : $"召唤{SummonNameOrAll(p, e)}（{var} 点生命）。",
            // 伙伴攻击：attacker 是宠物，不是自己 —— 描述里必须写清楚是哪只在打
            "PetAttack" => e.TargetSide == "Self"
                ? $"{SummonNameOrAll(p, e)}攻击自己，造成 {var} 点伤害。"
                : $"{repeat}{when}让{SummonNameOrAll(p, e)}{target}造成 {var} 点伤害{hitSuffix}。",
            // ===== 新增的那批宠物效果 =====
            // 三个「按生命值算的伙伴攻击」：**绝不能写死数字**（伤害是打出时按宠物当时的最大/当前/已损失
            // 生命算出来的），只写 {CalculatedDamage:diff()} 让本体去算，升级增量也会跟着显示。
            "PetDamageByMaxHp" => PetCalcDamageText(p, e, var, repeat, when, target, hitSuffix, "最大生命值"),
            "PetDamageByCurHp" => PetCalcDamageText(p, e, var, repeat, when, target, hitSuffix, "当前生命值"),
            "PetDamageByMissingHp" => PetCalcDamageText(p, e, var, repeat, when, target, hitSuffix, "已损失的生命值"),
            "PetHeal" => $"{repeat}让[gold]{SummonNameOrAll(p, e)}[/gold]回复 {var} 点生命。",
            "PetLoseHp" => $"让[gold]{SummonNameOrAll(p, e)}[/gold]失去 {var} 点生命。",
            "PetGainMaxHp" => $"[gold]{SummonNameOrAll(p, e)}[/gold]的最大生命值增加 {var} 点（同时回复等量生命）。",
            "PetApplyPower" => $"[gold]{SummonNameOrAll(p, e)}[/gold]获得 {var} 层{PowerNameFor(p, e.PowerId)}。",
            "PetGuardOn" => $"[gold]{SummonNameOrAll(p, e)}[/gold]开始替主人承伤（主人受到可格挡的攻击伤害时，改由它承担）。",
            "PetGuardOff" => $"[gold]{SummonNameOrAll(p, e)}[/gold]不再替主人承伤。",
            "PetSacrifice" => PetSacrificeText(p, e, var),
            _ => "",
        };
        // 需求 ⑦：「生成 / 变化出来的卡带上关键词」—— 句尾补一句「（生成出来的卡会带上『X』）」。
        // 没选关键词时一个字都不加（卡面描述与旧版逐字一致）。
        if (text.Length > 0 && e.HasSpawnKeyword)
            text = text.TrimEnd('。') + SpawnKeywordNote(p, e) + "。";
        // 概率生效：写在这条效果后面（例：「造成 6 点伤害（50% 概率）。」）
        if (text.Length > 0 && e.ChanceEnabled)
            text = text.TrimEnd('。') + e.ChanceText + "。";
        return text;
    }
}
