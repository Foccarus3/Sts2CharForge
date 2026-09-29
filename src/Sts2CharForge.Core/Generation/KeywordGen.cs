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
        // 生成器自带的两个内置关键词（「临时保留 / 临时奇巧」，见下面的 Temp* 成员）：
        // 用户的自定义关键词不能占用这两个键，否则会把卡面上那行字和悬停说明覆盖掉
        TempRetainKey, TempSlyKey,
    };

    /// <summary>「临时保留」的本地化键（生成器内置，不占用户的自定义关键词）。</summary>
    public const string TempRetainKey = "TEMP_RETAIN";

    /// <summary>「临时奇巧」的本地化键（生成器内置）。</summary>
    public const string TempSlyKey = "TEMP_SLY";

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

    // ==================== 内置关键词：临时保留 / 临时奇巧 ====================
    //
    // 为什么是「关键词」而不是本体 CardKeyword：本体的 CardKeyword 是**封闭枚举**（模组加不了新值），
    // 而这两个是「只这一回合」的单回合版本 —— 卡牌代码里打的是本体 GiveSingleTurnRetain/Sly 标记，
    // 卡面上则用自定义关键词那套等价机制显示（[gold]名字[/gold]。+ 悬停说明）。
    // 键固定为 TEMP_RETAIN / TEMP_SLY，写了这两个键的 card_keywords.json 由 ModGenerator 生成。

    /// <summary>内置临时关键词在卡面上的名字。</summary>
    public static string TempTitleOf(string key) =>
        string.Equals(key, TempRetainKey, StringComparison.OrdinalIgnoreCase) ? "临时保留" : "临时奇巧";

    /// <summary>内置临时关键词的悬停说明（鼠标悬停在卡上时弹出的那段）。</summary>
    public static string TempDescriptionOf(string key) =>
        string.Equals(key, TempRetainKey, StringComparison.OrdinalIgnoreCase)
            ? "临时保留：这张牌在回合结束时不会被弃掉 —— 只限这一回合。和「保留」的区别：「保留」是每回合都留，"
              + "临时保留只保这一次，下一回合就恢复正常（除非它还带着「保留」关键词）。"
            : "临时奇巧：这张牌只在这一回合算「奇巧」—— 在这一回合里被弃掉时可以免费打出。"
              + "和「奇巧」的区别：「奇巧」一直算，临时奇巧只算这一次。";

    /// <summary>这张牌要用到哪几个内置临时关键词的键（顺序固定：临时保留 → 临时奇巧）。</summary>
    public static List<string> TempKeysOf(CardSpec c)
    {
        var list = new List<string>();
        if (c is null) return list;
        if (c.TempRetain) list.Add(TempRetainKey);
        if (c.TempSly) list.Add(TempSlyKey);
        return list;
    }

    /// <summary>整个存档里有没有用到内置临时关键词（决定要不要写 card_keywords.json）。</summary>
    public static bool UsesTempKeywords(CharacterProfile p) =>
        p.Cards is not null && p.Cards.Any(c => c is not null && (c.TempRetain || c.TempSly));

    /// <summary>内置临时关键词 → 悬停提示的 C# 表达式（和 <see cref="TipsFor"/> 同一套写法）。</summary>
    public static List<string> TempTipsFor(CardSpec c) =>
        TempKeysOf(c).Select(k => "new MegaCrit.Sts2.Core.HoverTips.HoverTip("
                + $"new LocString(\"card_keywords\", \"{k}.title\"), "
                + $"new LocString(\"card_keywords\", \"{k}.description\"))")
            .ToList();

    /// <summary>内置临时关键词 → 卡面描述开头那几行（不含末尾换行；没有就返回空串）。</summary>
    public static string TempCardTextFor(CardSpec c) =>
        string.Join("\n", TempKeysOf(c).Select(k => $"[gold]{TempTitleOf(k)}[/gold]。"));

    /// <summary>
    /// 内置临时关键词的本地化条目 —— 只写**真的用到**的那几个键
    /// （没用到就不写，免得往本体表里塞用不上的东西）。
    /// </summary>
    public static Dictionary<string, string> TempLocEntries(CharacterProfile p)
    {
        var dict = new Dictionary<string, string>();
        if (p.Cards is null) return dict;
        foreach (string key in p.Cards.Where(c => c is not null).SelectMany(TempKeysOf)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            dict[key + ".title"] = TempTitleOf(key);
            dict[key + ".description"] = TempDescriptionOf(key);
        }
        return dict;
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
            if (card?.KeywordIds is null || card.KeywordIds.Count == 0) continue;
            var fixedList = new List<string>();
            foreach (string raw in card.KeywordIds)
            {
                string text = (raw ?? "").Trim();
                if (text.Length == 0) continue;
                var hit = Find(p, text);
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
            if (!fixedList.SequenceEqual(card.KeywordIds, StringComparer.OrdinalIgnoreCase))
                card.KeywordIds = fixedList;
        }
    }
}
