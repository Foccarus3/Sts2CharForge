using System;
using System.Collections.Generic;
using System.Linq;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 自定义关键词的公共逻辑：本地化键、悬停提示表达式、卡面文字。
///
/// 为什么集中在一处：这三样必须严格对齐 —— 卡面文字里的名字、悬停提示读的键、
/// 本地化表里写的键，只要有一处对不上，游戏里就会「弹出一个缺键异常」或者「悬停什么都没有」。
///
/// 走的是什么机制（都在本体的公开 API 上，不需要 Harmony）：
///   · 文案写进本体的 <c>card_keywords</c> 表（同名文件逐键合并，模组可以只加自己的键）；
///   · 卡牌用 <c>ExtraHoverTips</c>（<c>CardModel</c> 的虚属性）挂上「名称 + 说明」；
///   · 卡面描述开头拼 <c>[gold]名称[/gold]。</c>，与本体的 <c>CardKeywordExtensions.GetCardText()</c> 观感一致。
/// </summary>
public static class KeywordGen
{
    /// <summary>
    /// 本体 CardKeyword 的键（<c>StringHelper.Slugify(枚举名)</c>）+ <c>PERIOD</c>：
    /// 自定义键绝不能撞这些，否则会覆盖本体关键词的文案（「消耗」「虚无」…会变成你写的内容）。
    /// </summary>
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "NONE", "EXHAUST", "ETHEREAL", "INNATE", "UNPLAYABLE", "RETAIN", "SLY", "ETERNAL", "PERIOD",
    };

    /// <summary>空/非法/撞本体时兜底用的键。</summary>
    public static string FallbackKey(int index) => "KEYWORD_" + (index + 1).ToString();

    /// <summary>
    /// 把用户填的「英文标识」规范化成本地化键前缀：只保留 A–Z / 0–9，其余（空格、连字符、点…）换成 <c>_</c>，
    /// 统一大写。
    ///
    /// **为什么不用 <see cref="Naming.Slug"/>**：那个算法是给**驼峰类名**用的，与本体
    /// <c>StringHelper.Slugify</c> 一致 —— 它会在**连续大写之间也插下划线**，于是用户手填的 <c>FATE</c>
    /// 会变成 <c>F_A_T_E</c>。而卡牌上存的引用是用户填的原样文本（<c>FATE</c>），两边就对不上、
    /// 校验器会报「引用了不存在的自定义关键词」（自检抓到过）。标识本身就是键，不该再被拆字。
    /// </summary>
    public static string NormalizeKeyText(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char ch in key!.Trim())
        {
            if (ch < 128 && char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
            else if (ch == '_' || ch == '-' || ch == ' ' || ch == '.') sb.Append('_');
        }
        return sb.ToString();
    }

    /// <summary>某条关键词的本地化键前缀（全大写；空或非法时按序号兜底）。</summary>
    public static string KeyOf(CustomKeywordSpec spec, int index)
    {
        string cleaned = NormalizeKeyText(spec.Key);
        return cleaned.Length == 0 ? FallbackKey(index) : cleaned;
    }

    /// <summary>是不是本体的保留键（界面校验用）。</summary>
    public static bool IsReservedKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && ReservedKeys.Contains(NormalizeKeyText(key));

    /// <summary>全部关键词 + 它们的本地化键（顺序 = 列表顺序）。</summary>
    public static List<(CustomKeywordSpec Spec, string Key)> All(CharacterProfile p)
    {
        var list = new List<(CustomKeywordSpec, string)>();
        if (p.CustomKeywords is null) return list;
        for (int i = 0; i < p.CustomKeywords.Count; i++)
        {
            var spec = p.CustomKeywords[i];
            if (spec is null) continue;
            list.Add((spec, KeyOf(spec, i)));
        }
        return list;
    }

    /// <summary>
    /// 按引用找关键词：卡牌里存的可能是「键」也可能是「中文名」（用户手写 JSON / 回读工程时都可能），
    /// 两者都认，大小写不敏感。
    /// </summary>
    public static (CustomKeywordSpec Spec, string Key)? Find(CharacterProfile p, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        string want = reference!.Trim();
        foreach (var item in All(p))
        {
            if (string.Equals(item.Key, want, StringComparison.OrdinalIgnoreCase)) return item;
            // 也认「用户原样填的标识」与「规范化后的标识」两种写法（老存档 / 手写 JSON 都能对上）
            if (string.Equals(NormalizeKeyText(item.Spec.Key), NormalizeKeyText(want), StringComparison.OrdinalIgnoreCase)
                && NormalizeKeyText(want).Length > 0) return item;
            if (string.Equals((item.Spec.Name ?? "").Trim(), want, StringComparison.OrdinalIgnoreCase)) return item;
        }
        return null;
    }

    /// <summary>这条关键词在卡面上的名字（没填名字就退回键）。</summary>
    public static string DisplayName(CustomKeywordSpec spec, string key) =>
        string.IsNullOrWhiteSpace(spec.Name) ? key : spec.Name.Trim();

    /// <summary>
    /// 引用的关键词 → 悬停提示的 C# 表达式（写进生成的 <c>ExtraHoverTips</c>）。
    /// 找不到的引用直接跳过（校验器会另外报错，这里不能让生成炸掉）。
    /// </summary>
    public static List<string> TipsFor(CharacterProfile p, IEnumerable<string>? references)
    {
        var list = new List<string>();
        if (references is null) return list;
        foreach (var r in references)
        {
            var hit = Find(p, r);
            if (hit is null) continue;
            list.Add("new MegaCrit.Sts2.Core.HoverTips.HoverTip("
                   + $"new LocString(\"card_keywords\", \"{hit.Value.Key}.title\"), "
                   + $"new LocString(\"card_keywords\", \"{hit.Value.Key}.description\"))");
        }
        return list.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 引用的关键词 → 卡面描述开头那几行（本体关键词就是拼在描述最前面的，例如「消耗。造成 10 点伤害」）。
    /// 返回**不含末尾换行**的文本，多条之间用换行分隔；没有就返回空串。
    /// </summary>
    public static string CardTextFor(CharacterProfile p, IEnumerable<string>? references)
    {
        var lines = new List<string>();
        if (references is null) return "";
        foreach (var r in references)
        {
            var hit = Find(p, r);
            if (hit is null) continue;
            string name = DisplayName(hit.Value.Spec, hit.Value.Key);
            if (name.Length == 0) continue;
            string line = $"[gold]{name}[/gold]。";
            if (!lines.Contains(line)) lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    // ==================== 升级后获得 / 失去的自定义关键词（V0.2.136）====================
    //
    // 机制事实（预研已核实，别再重新论证）：本体的 CardKeyword 是**封闭枚举**，模组加不了新值 ——
    // 自定义关键词本来就是「卡面那行 [gold]名字[/gold]。 + 生成的 ExtraHoverTips 覆写 + card_keywords
    // 本地化键」三块拼起来的**显示等价物**，游戏侧没有任何按它判定的逻辑。
    // 所以「升级后获得 / 失去」只改**显示**两处：
    //   · 描述那行用本体的 {IfUpgraded:show:升级后|没升级} 包起来（见 BuildCardText）；
    //   · ExtraHoverTips 按 base.IsUpgraded 条件加/不加那条（见 CSharpCodeGen.CustomKeywordTipsConditional）。
    // 对比：本体关键词走 OnUpgrade() 里的 AddKeyword/RemoveKeyword，会真的改卡牌状态。

    /// <summary>
    /// 一组引用 → 当前配置下的**规范本地化键**（去掉空值 / 重复，认键也认中文名）。
    /// 界面勾选、生成标记、回读三处共用同一套规范化，回读出来的东西才和界面写的逐字一样。
    /// </summary>
    public static List<string> NormalizeReferences(CharacterProfile p, IEnumerable<string>? references)
    {
        var list = new List<string>();
        if (references is null) return list;
        foreach (var r in references)
        {
            string text = (r ?? "").Trim();
            if (text.Length == 0) continue;
            // 对不上的引用保留用户写的那份（生成端会跳过它，校验器会报错 —— 不许静默丢掉用户的输入）
            string key = Find(p, text)?.Key ?? text;
            if (!list.Contains(key, StringComparer.OrdinalIgnoreCase)) list.Add(key);
        }
        return list;
    }

    /// <summary>
    /// 这张牌的卡面描述要用的两段关键词文字（升级前 / 升级后各一段）。
    ///
    /// <para>普通自定义关键词（<see cref="CustomKeywordList"/>）升级前后都显示；
    /// 「升级后获得的」只在升级后那段、「升级后失去的」只在没升级那段。</para>
    ///
    /// <para>两边算出来**完全相同**时（没填这两栏就是这种情况）调用方直接按老路径走 ——
    /// 生成出来的 <c>cards.json</c> 一个字符都不变（红线①）。</para>
    /// </summary>
    public static (string Base, string Upgraded) CardTextBoth(CharacterProfile p, CardSpec c)
    {
        var now = c?.CustomKeywordList ?? new List<string>();
        var add = c?.UpgradeAddedCustomKeywords ?? new List<string>();
        var del = c?.UpgradeRemovedCustomKeywords ?? new List<string>();
        // 顺序 = 普通关键词（升级前后都在）→ 升级后获得的 → 升级后失去的（各自去重、保持用户填的顺序）
        string baseText = CardTextFor(p, Dedup(now.Concat(del)));
        string upText = CardTextFor(p, Dedup(now.Concat(add)));
        return (baseText, upText);
    }

    private static List<string> Dedup(IEnumerable<string> items)
    {
        var list = new List<string>();
        foreach (string x in items)
        {
            string t = (x ?? "").Trim();
            if (t.Length == 0) continue;
            if (!list.Contains(t, StringComparer.OrdinalIgnoreCase)) list.Add(t);
        }
        return list;
    }

    // ==================== 内置关键词：临时保留 / 临时奇巧 ====================
    //
    // 为什么是「关键词」而不是本体 CardKeyword：本体的 CardKeyword 是**封闭枚举**（模组加不了新值），
    // 而这两个是「只这一回合」的单回合版本 —— 卡牌代码里打的是本体 GiveSingleTurnRetain/Sly 标记，
    // 卡面上则用自定义关键词那套等价机制显示（[gold]名字[/gold]。+ 悬停说明）。
    // 键固定为 TEMP_RETAIN / TEMP_SLY，写了这两个键的 card_keywords.json 由 ModGenerator 生成。

    /// <summary>
    /// 一条自定义关键词引用的**本地化键**（「给予卡牌关键词」用的是键，卡牌上存的是键或名字）。
    /// 找不到时返回规范化后的文本（生成端不会因此炸；校验器会另外报错）。
    /// </summary>
    public static string KeyOfReference(CharacterProfile p, string? reference)
    {
        var hit = Find(p, reference);
        return hit?.Key ?? NormalizeKeyText(reference);
    }

    /// <summary>本地化表内容（键 = <c>&lt;KEY&gt;.title</c> / <c>&lt;KEY&gt;.description</c>）。</summary>
    public static Dictionary<string, string> LocEntries(CharacterProfile p)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (spec, key) in All(p))
        {
            dict[key + ".title"] = DisplayName(spec, key);
            dict[key + ".description"] = (spec.Description ?? "").Trim();
        }
        return dict;
    }

    /// <summary>
    /// 把卡牌上的引用规整成「当前的本地化键」。
    ///
    /// 为什么需要：用户先勾了关键词（当时键是自动生成的 <c>KEYWORD_3</c>），之后又把英文标识填成
    /// <c>FATE</c> —— 老引用就再也找不到对应关键词了，生成前的校验会报「引用了不存在的关键词」。
    /// 这里在载入存档时统一修正：能按键/名字对上就用当前键；对不上但形如 <c>KEYWORD_3</c> 的，
    /// 按序号认回第 3 条关键词。
    /// </summary>
    public static void Normalize(CharacterProfile p)
    {
        var all = All(p);
        if (all.Count == 0 || p.Cards is null) return;
        foreach (var card in p.Cards)
        {
            if (card is null) continue;
            var fixedList = new List<string>();
            foreach (string raw in card.KeywordIds ?? new List<string>())
            {
                string text = (raw ?? "").Trim();
                if (text.Length == 0) continue;
                var hit = FindIn(all, text);
                if (hit is null)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(text, @"^KEYWORD_(\d+)$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out int idx) && idx >= 1 && idx <= all.Count)
                        hit = all[idx - 1];
                }
                string key = hit?.Key ?? text;
                if (!fixedList.Contains(key, StringComparer.OrdinalIgnoreCase)) fixedList.Add(key);
            }
            if (!fixedList.SequenceEqual(card.KeywordIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase))
                card.KeywordIds = fixedList;
            // V0.2.136「升级后获得 / 失去的自定义关键词」同理**在循环外**（独立于「普通关键词引用」）：
            // 老引用也要跟着改过的英文标识走，否则用户改了标识之后这两栏就变成
            // 「引用了不存在的关键词」（校验器会报错）。没填时进出一模一样（列表保持空、不产生任何存档变化）。
            card.UpgradeAddedCustomKeywords = NormalizeLike(card.UpgradeAddedCustomKeywords, all);
            card.UpgradeRemovedCustomKeywords = NormalizeLike(card.UpgradeRemovedCustomKeywords, all);
        }
    }

    /// <summary>把一组引用按「当前配置的键」规整（键 / 中文名 / KEYWORD_N 三种写法都认）。</summary>
    private static List<string> NormalizeLike(List<string>? references, List<(CustomKeywordSpec Spec, string Key)> all)
    {
        var list = new List<string>();
        if (references is null) return list;
        foreach (string raw in references)
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0) continue;
            var hit = FindIn(all, text);
            if (hit is null)
            {
                var m = System.Text.RegularExpressions.Regex.Match(text, @"^KEYWORD_(\d+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int idx) && idx >= 1 && idx <= all.Count)
                    hit = all[idx - 1];
            }
            string key = hit?.Key ?? text;
            if (!list.Contains(key, StringComparer.OrdinalIgnoreCase)) list.Add(key);
        }
        return list;
    }

    /// <summary>在已取好的关键词表里按引用找（键 / 规范化标识 / 中文名都认，大小写不敏感）。</summary>
    private static (CustomKeywordSpec Spec, string Key)? FindIn(
        List<(CustomKeywordSpec Spec, string Key)> all, string want)
    {
        foreach (var item in all)
        {
            if (string.Equals(item.Key, want, StringComparison.OrdinalIgnoreCase)) return item;
            if (string.Equals(NormalizeKeyText(item.Spec.Key), NormalizeKeyText(want), StringComparison.OrdinalIgnoreCase)
                && NormalizeKeyText(want).Length > 0) return item;
            if (string.Equals((item.Spec.Name ?? "").Trim(), want, StringComparison.OrdinalIgnoreCase)) return item;
        }
        return null;
    }
}
