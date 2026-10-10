using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 「升级后的自定义描述」里**逐字段**控制升级差异高亮（<c>{X:diff()}</c>）的纯文本工具。
///
/// 为什么要有这一层：本体的升级差异高亮是写在描述正文里的
/// （<c>HighlightDifferencesFormatter</c>，键名 <c>diff</c> —— 真本体 <c>SlayTheSpire2.pck</c> 里
/// <c>"BASH.description": "Füge {Damage:diff()} Schaden zu."</c> 就是这么写的）。
/// 用户要求「把能造成高亮的做成按钮放在升级后自定义描述底下，用户可以选择具体字段高亮」，
/// 于是界面只需要把用户文本里**已经存在**的 <c>{X}</c> 改成 <c>{X:diff()}</c>（或反过来）。
///
/// 三条铁律（都为了「老存档 / 默认档逐字节不变」）：
/// 1. **只动占位符本身**，一个字都不加（文本里没有的字段绝不插入）；
/// 2. **幂等**：连点两次结果一样（已经是 <c>{X:diff()}</c> 的不会变成 <c>{X:diff():diff()}</c>）；
/// 3. 只认「变量名字符」的键（<c>[A-Za-z_][A-Za-z0-9_]*</c>），别的花括号原样留着。
///
/// 这里**不碰** <c>LocalizationGen.CardsJson</c>，也不加任何 <c>CET:</c> 标记 ——
/// <c>{X:diff()}</c> 就是用户正文的一部分，所以
/// <c>CET:UpgradedCustomDescription=</c> 会原样把它带回（回读天然幂等）。
/// </summary>
public static class HighlightDiffText
{
    /// <summary>
    /// 一个 <c>{X}</c> / <c>{X:diff()}</c> 占位符：<c>Index</c> / <c>Length</c> 是它在原文里的位置。
    /// </summary>
    public readonly record struct Placeholder(string Key, bool HasDiff, int Index, int Length);

    /// <summary>
    /// 占位符的匹配式子。刻意只用 <c>[A-Za-z_][A-Za-z0-9_]*</c> 当键：
    /// 本体的动态变量键就是这么个形状（<c>Damage</c> / <c>Block</c> / <c>PetHp</c>），
    /// 也避免把 <c>{IfUpgraded:show:…}</c> 这类本体自己的格式化器当成变量。
    /// </summary>
    private static readonly Regex PlaceholderRe = new(
        @"\{(?<key>[A-Za-z_][A-Za-z0-9_]*)(?<diff>:diff\(\))?\}", RegexOptions.Compiled);

    /// <summary>这段文字里所有 <c>{X}</c> / <c>{X:diff()}</c> 占位符（按出现顺序）。</summary>
    public static List<Placeholder> Scan(string? text)
    {
        var list = new List<Placeholder>();
        if (string.IsNullOrEmpty(text)) return list;
        foreach (Match m in PlaceholderRe.Matches(text))
            list.Add(new Placeholder(m.Groups["key"].Value, m.Groups["diff"].Success, m.Index, m.Length));
        return list;
    }

    /// <summary>
    /// 只匹配 <c>{X:diff()}</c>（校验器要单独认这一种「高亮写法」）。
    /// 键的形状和 <see cref="Scan"/> 完全一致 —— 两处不能各写一套，否则界面认得出、校验器认不出。
    /// </summary>
    private static readonly Regex DiffRe = new(
        @"\{(?<key>[A-Za-z_][A-Za-z0-9_]*):diff\(\)\}", RegexOptions.Compiled);

    /// <summary>去掉文本里所有的 <c>{X:diff()}</c>（剩下的就是「没有占位符」的原始文案）。</summary>
    public static string StripDiff(string? text) => text is null ? "" : DiffRe.Replace(text, "");

    /// <summary>
    /// 按勾选把文本里的占位符改写成高亮 / 普通两种写法。
    ///
    /// 参数 <paramref name="checkedKeys"/> = 用户勾上的字段键（只有勾上的会变 <c>{X:diff()}</c>）。
    /// 返回：改好之后的文本 + 真正改了几处 + 每个候选键在原文里被改了几处。
    /// **文本里没出现的字段一处都不会改**（也绝不插入）。
    /// </summary>
    public static (string Text, int Changed, Dictionary<string, int> PerKey) Apply(
        string? text, IEnumerable<string> checkedKeys)
    {
        string src = text ?? "";
        var wanted = new HashSet<string>(checkedKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        var perKey = new Dictionary<string, int>(StringComparer.Ordinal);
        int changed = 0;
        string result = PlaceholderRe.Replace(src, m =>
        {
            string key = m.Groups["key"].Value;
            bool hasDiff = m.Groups["diff"].Success;
            bool wantDiff = wanted.Contains(key);
            // 已经是想要的样子 → 一个字符都不动（幂等的关键）
            if (hasDiff == wantDiff) return m.Value;
            changed++;
            perKey[key] = perKey.TryGetValue(key, out int n) ? n + 1 : 1;
            return wantDiff ? "{" + key + ":diff()}" : "{" + key + "}";
        });
        return (result, changed, perKey);
    }

    /// <summary>这个文本里**现在就是高亮**（<c>{X:diff()}</c>）的字段键。</summary>
    public static List<string> HighlightedKeys(string? text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var p in Scan(text))
            if (p.HasDiff && seen.Add(p.Key)) list.Add(p.Key);
        return list;
    }

    /// <summary>这个文本里出现过的所有字段键（高亮 / 普通都算）。</summary>
    public static List<string> UsedKeys(string? text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var p in Scan(text))
            if (seen.Add(p.Key)) list.Add(p.Key);
        return list;
    }

    /// <summary>
    /// 「填入参数…」那种候选 token（<c>{Damage:diff()}</c> / <c>{Block}</c>）里抠出变量键。
    /// 校验器要拿它跟正文里的占位符比，所以单独放一个（两处算法必须一致）。
    /// </summary>
    public static string KeyOfToken(string? token)
    {
        var m = PlaceholderRe.Match(token ?? "");
        return m.Success ? m.Groups["key"].Value : "";
    }

    /// <summary>去掉 <c>:diff()</c> 的高亮写法（<c>{Damage:diff()}</c> → <c>{Damage}</c>）。</summary>
    public static string PlainToken(string key) => "{" + key + "}";

    /// <summary>加上 <c>:diff()</c> 的高亮写法（<c>{Damage}</c> → <c>{Damage:diff()}</c>）。</summary>
    public static string DiffToken(string key) => "{" + key + ":diff()}";
}
