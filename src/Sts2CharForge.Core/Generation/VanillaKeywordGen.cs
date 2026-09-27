using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 「本体关键词改名」的公共逻辑：把本体那 7 个关键词的显示名（和说明）换成用户自己写的。
///
/// 和「自定义关键词」（<see cref="KeywordGen"/>）的区别：那个是**新增**关键词，
/// 这个是**覆盖本体已有的 7 个**。两者写的是同一个文件、同一套键 —— 本体
/// <c>localization/&lt;语言&gt;/card_keywords.json</c>，靠 <c>LocTable.MergeWith</c> 逐键盖掉本体那条，
/// 全程不碰本体、不需要 Harmony 补丁。
///
/// 产物两部分：
///   ① <see cref="LocEntries"/>：写进我们的 card_keywords.json（<c>EXHAUST.title</c> / <c>EXHAUST.description</c>）
///      —— 卡面上的金色词和悬停提示都会跟着变；
///   ② <see cref="KeywordTextReplacements"/>：把本体卡牌 / 遗物 / 药水**描述正文**里写着的旧中文名
///      （比如「消耗」两个字）一起换掉 —— 那属于纯文本，本地化表不会自动跟着改。
/// </summary>
public static class VanillaKeywordGen
{
    /// <summary>这条改名到底改了什么（名字或说明填了才算）。</summary>
    public static bool ChangesAnything(VanillaKeywordRenameSpec r) =>
        r is not null && VanillaKeywordCatalog.IsKnown(r.KeywordId) && r.ChangesAnything;

    /// <summary>所有「真的改了」的改名条目（生成时只处理这些）。</summary>
    public static List<VanillaKeywordRenameSpec> Active(CharacterProfile p) =>
        (p.KeywordRenames ?? new()).Where(ChangesAnything).ToList();

    /// <summary>这份配置要不要写 card_keywords.json（有自定义关键词 **或** 有关键词改名）。</summary>
    public static bool HasAny(CharacterProfile p) =>
        KeywordGen.All(p).Count > 0 || Active(p).Count > 0;

    /// <summary>本体原文（标题）。本机读到了本体表就用本机的，读不到退回内置兜底文本。</summary>
    public static string VanillaTitleOf(string? keywordId)
    {
        var entry = VanillaKeywordCatalog.ById(keywordId);
        if (entry is null) return "";
        return (EffectCatalog.VanillaKeywordText(entry.Id + ".title") ?? entry.VanillaName).Trim();
    }

    /// <summary>本体原文（说明）。</summary>
    public static string VanillaDescriptionOf(string? keywordId)
    {
        var entry = VanillaKeywordCatalog.ById(keywordId);
        if (entry is null) return "";
        return (EffectCatalog.VanillaKeywordText(entry.Id + ".description") ?? entry.VanillaDescription).Trim();
    }

    /// <summary>
    /// 这个关键词在游戏里最后会显示成什么名字（改了就用新名字）。校验器查撞车、
    /// 旧名全文替换都按它算 —— 必须和生成时写进本地化表的值完全一致。
    /// </summary>
    public static string EffectiveNameOf(CharacterProfile p, string? keywordId)
    {
        string id = (keywordId ?? "").Trim();
        var hit = (p.KeywordRenames ?? new()).FirstOrDefault(r =>
            r is not null && string.Equals((r.KeywordId ?? "").Trim(), id, StringComparison.OrdinalIgnoreCase));
        string mine = (hit?.Name ?? "").Trim();
        if (mine.Length > 0) return mine;
        return VanillaTitleOf(id);
    }

    /// <summary>
    /// 要写进我们那份 <c>card_keywords.json</c> 的键（只写用户**真的改了**的那些）。
    ///
    /// 三个「别写」的边界（都是踩过/想清楚才这么定的）：
    ///   · 留空 = 不改 —— 写了空串会把本体那条覆盖成空文本（卡面上就变成空白词）；
    ///   · 和本体原文一模一样 = 等于没改 —— 不用白覆盖一次本体表；
    ///   · 说明留空时也不写 —— 本体的「虚无 / 保留」等说明键本身就有内容，
    ///     凭空写空串会让悬停说明变空（用户会以为我们弄坏了本体）。
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> LocEntries(CharacterProfile p)
    {
        foreach (var r in Active(p))
        {
            var entry = VanillaKeywordCatalog.ById(r.KeywordId);
            if (entry is null) continue;                       // 非法 id 由校验器报错，这里不能炸

            string name = (r.Name ?? "").Trim();
            if (name.Length > 0 && !string.Equals(name, VanillaTitleOf(entry.Id), StringComparison.Ordinal))
                yield return new(entry.Id + ".title", name);

            string desc = (r.Description ?? "").Trim();
            if (desc.Length == 0) continue;
            if (string.Equals(desc, VanillaDescriptionOf(entry.Id), StringComparison.Ordinal)) continue;
            yield return new(entry.Id + ".description", desc);
        }
    }

    /// <summary>
    /// 改名的关键词，把本体卡牌 / 遗物 / 药水描述里的**旧中文名**也一起换掉
    /// （和 <see cref="VanillaPowerGen.VanillaTextReplacements"/> 同形状，由 ModGenerator 合并进三张表）。
    ///
    /// 用户报过的同类问题：状态改名之后卡面描述里还写着旧名，看着像没改 —— 关键词一模一样。
    /// 「消耗」这种词在本体卡面里到处都是（「消耗 1 张牌」），不换的话改完名字等于只改了一半。
    ///
    /// 两个安全阀：
    ///   · <c>map.ContainsKey(kv.Key)</c> —— **只覆盖本体表里本来就有的键**，
    ///     绝不把我们的自定义卡牌/遗物/药水文案（同一个 LocalizationGen 生成）误伤成新键；
    ///   · 新旧名字相同 / 名字为空时直接跳过，不产生无意义的覆盖项。
    /// </summary>
    public static IEnumerable<(string Table, string Key, string Text)> KeywordTextReplacements(CharacterProfile p)
    {
        // 旧名 → 新名（同一个关键词只处理一次；键和值都 trim 过，比较用序数比较）
        var renames = new List<(string Old, string New)>();
        foreach (var r in Active(p))
        {
            var entry = VanillaKeywordCatalog.ById(r.KeywordId);
            if (entry is null) continue;
            string oldName = VanillaTitleOf(entry.Id);
            string newName = (r.Name ?? "").Trim();
            if (oldName.Length == 0 || newName.Length == 0) continue;      // 没改名（只改了说明）就没什么可换
            if (string.Equals(oldName, newName, StringComparison.Ordinal)) continue;
            renames.Add((oldName, newName));
        }
        if (renames.Count == 0) yield break;

        foreach (var (table, map) in new (string, IReadOnlyDictionary<string, string>)[]
        {
            ("cards", EffectCatalog.ZhCardLoc),
            ("relics", EffectCatalog.ZhRelicLoc),
            ("potions", EffectCatalog.ZhPotionLoc),
        })
        {
            foreach (var kv in map)
            {
                if (!map.ContainsKey(kv.Key)) continue;                    // 双保险：只动本体表自己的键
                if (kv.Value.Length == 0) continue;
                string text = kv.Value;
                bool hit = false;
                foreach (var (oldName, newName) in renames)
                {
                    if (!text.Contains(oldName, StringComparison.Ordinal)) continue;
                    text = text.Replace(oldName, newName);
                    hit = true;
                }
                if (hit && !string.Equals(text, kv.Value, StringComparison.Ordinal))
                    yield return (table, kv.Key, text);
            }
        }
    }

    /// <summary>
    /// 本机到底读没读到本体的关键词文案（读不到时界面上要如实说：
    /// 改名照样生效 —— 键是我们按枚举名写死的 —— 但「原名」这一列只能用内置兜底文本，
    /// 而且「本体卡面描述里的旧名字」那一步换不了）。
    /// </summary>
    public static bool CanReadVanillaKeywordText => EffectCatalog.VanillaKeywordLoc.Count > 0;

    /// <summary>给校验器用：列出「和本体原文不一样」的新名字（键 = 枚举名）。</summary>
    public static Dictionary<string, string> RenamedNames(CharacterProfile p)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Active(p))
        {
            var entry = VanillaKeywordCatalog.ById(r.KeywordId);
            if (entry is null) continue;
            string name = (r.Name ?? "").Trim();
            if (name.Length == 0) continue;
            if (string.Equals(name, VanillaTitleOf(entry.Id), StringComparison.Ordinal)) continue;
            map[entry.Id] = name;
        }
        return map;
    }
}
