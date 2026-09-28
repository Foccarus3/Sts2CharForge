using System.Text;
using System.Text.Json;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>生成本地化文件（characters / cards / relics / potions / ancients / powers）。</summary>
public static class LocalizationGen
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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
            [$"{n.CharEntry}.description"] = string.IsNullOrWhiteSpace(p.Description) ? "一个自定义角色。" : p.Description,
            [$"{n.CharEntry}.pronounObject"] = obj,
            [$"{n.CharEntry}.possessiveAdjective"] = poss,
            [$"{n.CharEntry}.pronounPossessive"] = possPro,
            [$"{n.CharEntry}.pronounSubject"] = subj,
            [$"{n.CharEntry}.cardsModifierTitle"] = p.DisplayName + "卡牌",
            [$"{n.CharEntry}.cardsModifierDescription"] = p.DisplayName + "的卡牌现在会出现在奖励和商店中。",
            [$"{n.CharEntry}.eventDeathPrevention"] = string.IsNullOrWhiteSpace(p.DeathText) ? "……还不到时候。" : p.DeathText,
            [$"{n.CharEntry}.bestiaryQuote"] = "……记住这个数字就够了。",
            [$"{n.CharEntry}.goldMonologue"] = "钱？也只是数字。",
            [$"{n.CharEntry}.banter.alive.endTurnPing"] = "……继续。",
            [$"{n.CharEntry}.banter.dead.endTurnPing"] = string.IsNullOrWhiteSpace(p.DeadBanterText) ? "……" : p.DeadBanterText,
            [$"{n.CharEntry}.unlockText"] = "完成 {Prerequisite} 的条件后解锁。",
        };
        return JsonSerializer.Serialize(dict, JsonOpts);
    }

    public static string CardsJson(CharacterProfile p)
    {
        var n = Naming.From(p);
        var dict = new Dictionary<string, string>();
        for (int i = 0; i < p.Cards.Count; i++)
        {
            var c = p.Cards[i];
            // 本体卡引用（打击 / 防御）：名字和描述都用本体自己的那份，不要覆盖
            if (c.IsVanillaCard) continue;
            string entry = Naming.EntryOf(n.CardClassName(p, c));
            dict[$"{entry}.title"] = c.Name;
            // 「从牌堆拿牌到手牌」的选牌界面提示语（生成代码里用 base.SelectionScreenPrompt 读它）
            if (NeedsSelectPrompt(c.Effects))
                dict[$"{entry}.selectionScreenPrompt"] = SelectPromptText(c.Effects);
            // 老存档是「整张牌一个条件」，那个条件写在最后；新存档的条件跟着各自的效果走（Describe 里处理）
            string legacy = ConditionSuffix(c.Condition, isCard: true, p);
            // 自定义关键词：和本体关键词一样拼在描述最前面（本体是「[gold]消耗[/gold]。」+ 换行 + 效果描述）
            string keywordText = KeywordGen.CardTextFor(p, c.CustomKeywordList);
            string cardBody = Describe(c.Effects, p, isCard: true, starCostIsX: c.StarCostIsX) + legacy;
            dict[$"{entry}.description"] = keywordText.Length == 0
                ? cardBody
                : (cardBody.Length == 0 ? keywordText : keywordText + "\n" + cardBody);
        }
        return JsonSerializer.Serialize(dict, JsonOpts);
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
            if (NeedsSelectPrompt(r.Effects))
                dict[$"{entry}.selectionScreenPrompt"] = SelectPromptText(r.Effects);
            dict[$"{entry}.description"] = Describe(r.Effects, p) + ConditionSuffix(r.Condition, isCard: false, p);
            dict[$"{entry}.flavor"] = "由 Sts2CharForge 生成的自定义遗物。";
        }
        // 额外资源量的载体遗物也要有名字，否则遗物栏里会显示成缺键
        if (p.ExtraResource.Enabled)
        {
            var x = p.ExtraResource;
            string entry = Naming.EntryOf("ExtraResourceRelic");
            string res = ResourceName(p);
            dict[$"{entry}.title"] = res;
            dict[$"{entry}.description"] = x.CarryOver
                ? $"每场战斗开始时获得 {x.Initial} 点{res}，上一场战斗没用完的会保留到下一场。"
                : $"每场战斗开始时获得 {x.Initial} 点{res}（战斗结束清空）。";
            dict[$"{entry}.flavor"] = RenameResource("由 Sts2CharForge 生成的隐藏起始遗物，用来承载额外资源量。", p);
        }
        return JsonSerializer.Serialize(dict, JsonOpts);
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

    /// <summary>这组效果里有没有「从牌堆拿牌到手牌」（那种会弹自己的选牌界面，需要一句提示语）。</summary>
    public static bool NeedsSelectPrompt(IEnumerable<EffectSpec> effects) =>
        effects.Any(e => e.Kind is "TakeFromDraw" or "TakeFromDiscard");

    /// <summary>
    /// 选牌界面上那句提示（本体的 <c>&lt;ENTRY&gt;.selectionScreenPrompt</c>，生成代码里读 base.SelectionScreenPrompt）。
    /// 本体每条卡牌 / 遗物 / 药水都有这么一条，所以这里自己写一句。
    /// </summary>
    public static string SelectPromptText(IEnumerable<EffectSpec> effects)
    {
        var e = effects.FirstOrDefault(x => x.Kind is "TakeFromDraw" or "TakeFromDiscard");
        if (e is null) return "选择要拿到手牌的牌";
        string pile = e.Kind == "TakeFromDiscard" ? "弃牌堆" : "抽牌堆";
        return $"从{pile}选择要拿到手牌的牌";
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
    public static string StaticHoverTipsJson(CharacterProfile p)
    {
        var dict = new Dictionary<string, string>();
        if (!p.ExtraResource.Enabled) return JsonSerializer.Serialize(dict, JsonOpts);

        string name = ResourceName(p);
        dict["STAR_COUNT.title"] = name;

        // 描述：本工具自己写，提到名字、开场数量、继承规则、消耗规则（图标可选，没传就不放图标）
        // 注意用「内联小图」（24×24，见 ArtGenerator.ExtraResourceIconInlineResPath）：
        // 内联 [img] 是按原图尺寸画的，塞计数器那张大图会把整段提示文字挤到字号极小。
        string? icon = ArtGenerator.ExtraResourceIconInlineResPath(p);
        string iconPrefix = icon is null ? "" : $"[img]{icon}[/img]";
        var bits = new List<string>();
        if (p.ExtraResource.Initial > 0)
            bits.Add(p.ExtraResource.CarryOver
                ? $"每场战斗开始时获得 {p.ExtraResource.Initial} 点（上一场剩下的会带过来）"
                : $"每场战斗开始时获得 {p.ExtraResource.Initial} 点（战斗结束清零）");
        else
            bits.Add("开场不发放，靠卡牌 / 遗物 / 药水获得");

        bool spends = p.Cards.SelectMany(c => c.Effects)
            .Concat(p.Relics.SelectMany(r => r.Effects))
            .Concat(p.Potions.SelectMany(s => s.Effects))
            .Any(e => e.Kind == "ExtraResource" && e.Amount < 0);
        if (spends)
            bits.Add("打出需要消耗它的牌时会扣除，数量不足时无法打出");

        dict["STAR_COUNT.description"] = $"{iconPrefix}{name}：{string.Join("；", bits)}。";
        return JsonSerializer.Serialize(dict, JsonOpts);
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
        return JsonSerializer.Serialize(dict, JsonOpts);
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
            if (NeedsSelectPrompt(s.Effects))
                dict[$"{entry}.selectionScreenPrompt"] = SelectPromptText(s.Effects);
            dict[$"{entry}.description"] = Describe(s.Effects, p, PotionTargetPhrase(s.TargetType));
        }
        return JsonSerializer.Serialize(dict, JsonOpts);
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

        return JsonSerializer.Serialize(dict, JsonOpts);
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
        return JsonSerializer.Serialize(dict, JsonOpts);
    }

    /// <summary>延迟 Power 的名称与描述；外加「本体状态改写」要覆盖的本体键（改名/改描述）。</summary>
    public static string PowersJson(CharacterProfile p)
    {
        var dict = new Dictionary<string, string>();
        foreach (var e in CSharpCodeGen.CollectDelayedEffects(p))
        {
            string entry = Naming.EntryOf(CSharpCodeGen.DelayedPowerClassName(e));
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
        // 自定义状态（能力牌用）：键就是本体的规则 Id.Entry + ".title"（PowerModel.Title 默认就这么取）
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
        }
        return JsonSerializer.Serialize(dict, JsonOpts);
    }

    // ---------- 描述自动生成 ----------
    private static string Describe(IEnumerable<EffectSpec> effects, CharacterProfile p, string? potionTarget = null, bool isCard = false, bool starCostIsX = false)
    {
        var sb = new StringBuilder();
        var list = effects as IList<EffectSpec> ?? effects.ToList();
        // 同名变量会带别名（Damage2 这种），描述里的 {占位符} 必须和生成代码一致
        var varMap = CSharpCodeGen.VarNamesOf(list);
        foreach (var e in list)
        {
            string text = DescribeEffect(e, p, potionTarget, isCard, starCostIsX, varMap);
            if (text.Length > 0) sb.Append(text).Append('\n');
            // 条件选项是「每条效果各自一份」的，所以条件说明紧跟在它管的那条效果后面
            // （药水不支持条件，所以药水不写）
            if (potionTarget is null)
            {
                string cond = EffectConditionInline(e.Condition, p);
                if (cond.Length > 0) sb.Append(cond).Append('\n');
            }
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
    private static string EffectConditionInline(ConditionSpec? condition, CharacterProfile? p = null) =>
        ConditionInline(condition, "（不满足时这条效果不生效）", p);

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

    /// <summary>「牺牲伙伴」的卡面描述：先写「若伙伴存活」，再写收益（格挡 / 伤害）。</summary>
    private static string PetSacrificeText(CharacterProfile p, EffectSpec e, string var)
    {
        string who = SummonName(p, e);
        bool block = e.PetSacrificeGain != "Damage";
        if (PetGen.IsAll(e.PetSummon))
        {
            // 「全部召唤物」：每只各自牺牲一次，收益也各自算一遍（有几只活着就算几次）
            string per = e.PetSacrificeFormula switch
            {
                "Fixed" => block ? $"你就获得 {var} 点[gold]格挡[/gold]" : $"它就对指定敌人造成 {var} 点伤害",
                "CurHp" => block
                    ? "你就获得等同于它当前生命值的[gold]格挡[/gold]"
                    : "它就对指定敌人造成等同于它当前生命值的伤害",
                _ => block
                    ? $"你就获得等同于它最大生命 × {e.PetSacrificeMultiplier:0.##} 的[gold]格挡[/gold]"
                    : $"它就对指定敌人造成等同于它最大生命 × {e.PetSacrificeMultiplier:0.##} 的伤害",
            };
            return $"[gold]{SummonNameOrAll(p, e)}[/gold]各自牺牲：每死去一只，{per}。";
        }
        string pay = block
            ? $"然后你获得{var}点[gold]格挡[/gold]。"
            : $"然后它对指定敌人造成{var}点伤害。";
        return $"若[gold]{who}[/gold]存活：它死去，{pay}";
    }

    private static string DescribeEffect(EffectSpec e, CharacterProfile p, string? potionTarget = null, bool isCard = false, bool starCostIsX = false,
        Dictionary<EffectSpec, string>? varMap = null)
    {
        // 「数值 = X」的卡牌：描述里写 X，而不是那个用不上的固定数值。
        // 变量名必须用 DisplayVarNameOf（= CanonicalVars 里真实声明的那个键）：三个「按生命值算」的
        // 伙伴攻击 / 牺牲伙伴声明的是本体的 CalculatedDamage / CalculatedBlock，直接用 VarNameOf 的
        // 内部名字会让卡面原样印出 {PetMissingHpDamage:diff()}（用户实测截图报过）。
        string var = e.AmountIsX && isCard ? "X" : "{" + CSharpCodeGen.DisplayVarNameOf(e, varMap) + ":diff()}";
        string when = e.NextTurn ? "下回合开始时，" : "";
        int times = Math.Max(1, e.Times);
        string repeat = e.TimesIsX && isCard ? "重复 X 次：" : times > 1 ? $"重复 {times} 次：" : "";
        // 药水：统一用「药水作用目标」的措辞（和实际执行一致）
        string target = potionTarget ?? e.TargetSide switch
        {
            "Enemy" => "对指定敌人",
            "AllEnemies" => "对所有敌人",
            "RandomEnemies" => e.RepeatIsX && isCard ? "对随机敌人 X 次" : $"对随机敌人 {Math.Max(1, e.RepeatCount)} 次",
            _ => "对自己",
        };

        // 伤害的命中次数：随机目标已经在 target 里写了次数，其余情况补充在「造成…伤害」后面
        string hitSuffix = e.TargetSide == "RandomEnemies"
            ? ""
            : e.RepeatIsX && isCard ? " X 次"
            : e.RepeatCount > 1 ? $" {e.RepeatCount} 次"
            : "";

        // 能打敌人的效果（格挡 / 回复生命 / 失去生命 / 最大生命）：作用对象不是自己时要写清「给谁」
        string who = potionTarget is not null
            ? potionTarget.TrimStart('对')                      // 「对所有敌人」→「所有敌人」
            : e.TargetSide switch
            {
                "Enemy" => "指定敌人",
                "AllEnemies" => "所有敌人",
                "RandomEnemies" => e.RepeatIsX && isCard ? "随机敌人 X 次" : $"随机 {Math.Max(1, e.RepeatCount)} 个敌人",
                _ => "",
            };
        string forWho = who.Length == 0 ? "" : $"让{who}";

        string text = e.Kind switch
        {
            // 伤害：命中次数 > 1（或 = X）要写出来，本体「旋风斩 / 天际钻头」的描述就是「造成 X 点伤害 X 次」
            "Damage" => e.TargetSide == "Self" ? $"受到 {var} 点伤害。" : $"{repeat}{when}{target}造成 {var} 点伤害{hitSuffix}。",
            // 格挡 / 回复生命 / 失去生命 / 最大生命：自己以外要写清对象（能给敌人加格挡 / 回血了）
            "Block" => who.Length == 0
                ? $"{repeat}{when}获得 {var} 点格挡。"
                : $"{repeat}{when}{forWho}获得 {var} 点格挡。",
            "Draw" => $"{repeat}{when}抽 {var} 张牌。",
            "Energy" => $"{repeat}{when}获得 {var} 点能量。",
            "Heal" => e.Amount < 0 && var != "X"
                ? (who.Length == 0 ? $"失去 {-e.Amount} 点生命。" : $"{forWho}失去 {-e.Amount} 点生命。")
                : (who.Length == 0 ? $"{repeat}回复 {var} 点生命。" : $"{repeat}{forWho}回复 {var} 点生命。"),
            "HpLoss" => who.Length == 0 ? $"失去 {var} 点生命。" : $"{forWho}失去 {var} 点生命。",
            "MaxHp" => e.Amount < 0 && var != "X"
                ? (who.Length == 0 ? $"失去 {-e.Amount} 点最大生命。" : $"{forWho}失去 {-e.Amount} 点最大生命。")
                : (who.Length == 0 ? $"{repeat}获得 {var} 点最大生命。" : $"{repeat}{forWho}获得 {var} 点最大生命。"),
            "Gold" => $"获得 {var} 枚金币。",
            // 额外资源量：正数获得用 {Stars:diff()} —— 卡面会在升级后自动显示升级值（用户报过升级后仍显示原值）；
            // 卡牌上的负数 = 这张牌的费用（费用数字显示在卡面星级费用处），遗物/药水上的负数 = 直接扣
            "ExtraResource" => starCostIsX && e.Amount < 0 && isCard
                ? $"需要 X 点{ResourceName(p)}（打出时消耗全部）。"
                : e.AmountIsX && isCard
                ? $"{(e.Amount < 0 ? "需要" : "获得")} X 点{ResourceName(p)}。"
                : e.Amount < 0
                    ? (isCard ? $"需要 {-e.Amount * Math.Max(1, e.Times)} 点{ResourceName(p)}。" : $"失去 {-e.Amount * Math.Max(1, e.Times)} 点{ResourceName(p)}。")
                    : $"{repeat}获得 {var} 点{ResourceName(p)}。",
            "EndTurn" => "结束你的回合。",
            "ExtraTurn" => "本回合结束后，额外获得一个回合。",
            "GenerateCard" => $"生成 {(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 张{CardNameOf(p, e.SpawnCardId)}，放入{EffectCatalog.SpawnTargetZh(e.SpawnToPile)}。",
            // 「从哪里选牌」不是手牌时不再写「手牌」（以前写死「N 张手牌」，选了弃牌堆就描述不对了）
            "ExhaustCard" => e.SelectPile == "Hand"
                ? $"{EffectCatalog.CardPickZh(e.CardPick)}消耗 {(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 张手牌。"
                : $"{EffectCatalog.CardPickZh(e.CardPick)}从{EffectCatalog.SelectPileZh(e.SelectPile)}消耗 {(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 张牌。",
            "TransformCard" => (e.SelectPile == "Hand"
                    ? $"{EffectCatalog.CardPickZh(e.CardPick)}将 {(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 张手牌变化为"
                    : $"{EffectCatalog.CardPickZh(e.CardPick)}将{EffectCatalog.SelectPileZh(e.SelectPile)}里的 {(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 张牌变化为")
                + (string.IsNullOrWhiteSpace(e.SpawnCardId) ? "随机卡牌。" : CardNameOf(p, e.SpawnCardId) + "。"),
            // 本体「搜寻 / 全息影像 / 挖掘」那种：从牌堆里挑牌拿到手牌
            "TakeFromDraw" => $"从抽牌堆里选 {(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 张牌拿到手牌。",
            "TakeFromDiscard" => $"从弃牌堆里选 {(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 张牌拿到手牌。",
            // 「直接把「缓慢」设成 N%」时层数没有意义（生成时就按本体做法施加 1 层），所以只写百分比
            "ApplyPower" => CSharpCodeGen.IsSlowPercentEffect(e)
                ? $"{repeat}{when}{target}施加{PowerNameFor(p, e.PowerId)}（受到伤害 +{e.SlowPercentEffective}%）。"
                : $"{repeat}{when}{target}施加 {var} 层{PowerNameFor(p, e.PowerId)}。",
            // ===== 全局（直接改牌组）=====
            "AddCardGlobal" => $"获得 {(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 张{CardNameOf(p, e.SpawnCardId)}（加入牌组）。",
            // 挂「战斗胜利后」时是结算界面多一条奖励；挂在战斗中就是当场弹选牌界面（见 CSharpCodeGen.EmitCardReward）
            "CardReward" => $"获得卡牌奖励（{(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 选一，选中的加入牌组）。",
            "RemoveCardGlobal" => e.CardPick == "Chosen"
                ? $"从牌组中自己选 {(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 张牌删除。"
                : $"从牌组中随机删除 {(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 张牌。",
            "TransformCardGlobal" => (e.CardPick == "Chosen" ? "将牌组中自己选的 " : "将牌组中随机 ")
                + $"{(e.AmountIsX && isCard ? "X" : ((int)e.Amount).ToString())} 张牌变化为"
                + (string.IsNullOrWhiteSpace(e.SpawnCardId) ? "随机卡牌。" : CardNameOf(p, e.SpawnCardId) + "。"),
            // ===== 召唤伙伴（本体的通用宠物 API，不需要补丁）=====
            // 数值 0 = 用「召唤物」页里配置的血量，这时不写具体数字（避免卡面写「召唤伙伴 0 点生命」误导人）
            "SummonPet" => (e.AmountIsX && isCard)
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
        // 概率生效：写在这条效果后面（例：「造成 6 点伤害（50% 概率）。」）
        if (text.Length > 0 && e.ChanceEnabled)
            text = text.TrimEnd('。') + e.ChanceText + "。";
        return text;
    }
}
