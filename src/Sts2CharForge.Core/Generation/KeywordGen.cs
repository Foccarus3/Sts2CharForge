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

    /// <summary>某条关键词的本地化键前缀（全大写下划线；空或非法时按序号兜底）。</summary>
    public static string KeyOf(CustomKeywordSpec spec, int index)
    {
        string raw = (spec.Key ?? "").Trim();
        if (raw.Length == 0) return FallbackKey(index);
        string slug = Naming.Slug(raw);
        var cleaned = new string(slug.Where(ch => (char.IsLetterOrDigit(ch) && ch < 128) || ch == '_').ToArray());
        return cleaned.Length == 0 ? FallbackKey(index) : cleaned;
    }

    /// <summary>是不是本体的保留键（界面校验用）。</summary>
    public static bool IsReservedKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && ReservedKeys.Contains(Naming.Slug(key!.Trim()));

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
