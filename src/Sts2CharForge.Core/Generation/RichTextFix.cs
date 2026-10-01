using System.Text;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 卡面 / 遗物 / 状态描述里的富文本标签（BBCode）**自动纠正 + 体检**。
///
/// 为什么必须做这件事（用户实测踩过）：
/// 本体的 <c>MegaLabelHelper.ParseBbcode</c> 是**严格**的 —— 它把每个 <c>[...]</c> 压栈 / 弹栈，
/// 一旦遇到下面任何一种情况就抛 <see cref="InvalidOperationException"/>：
///   · 标签拼错：<c>[god]中文[/gold]</c> → 「Found end tag gold, expected god」
///   · 没闭合：<c>[gold]文字</c> → 「In tag at end of string」
///   · 多出来的闭合：<c>[/gold]</c> → 「Found end tag gold with no tag on the stack」
///   · 交叉嵌套：<c>[gold][blue]x[/gold][/blue]</c> → 同上
/// 抛异常的地方是 <c>MegaRichTextLabel.AdjustFontSize()</c>（自动字号），
/// 后果是：那一次自动字号计算直接中断，而卡面上那串方括号会被**原样印出来**
/// （用户看到的「金色富文本用不了」就是这个：他写成了 <c>[god]</c>，少了一个 l）。
///
/// 所以生成前统一过一遍：把明显拼错的标签改成最接近的那个（只差一个字母才算），
/// 把没闭合的补上、多余的去掉、交叉的理顺 —— 保证进游戏的那份文本永远能被本体解析。
/// 改了什么会通过 <see cref="Result.Notes"/> 报给用户（生成日志 / 校验器里都能看到）。
/// </summary>
public static class RichTextFix
{
    /// <summary>修正结果：改好的文本 + 改了什么（给校验器 / 生成日志用）。</summary>
    public sealed record Result(string Text, IReadOnlyList<string> Notes);

    /// <summary>本体 <c>MegaRichTextLabel</c> 装上去的那 14 个效果标签（颜色 + 动效）。</summary>
    public static readonly IReadOnlyList<string> EffectTags = new[]
    {
        "gold", "blue", "red", "green", "purple", "orange", "pink", "aqua",
        "sine", "jitter", "scramble", "thinky_dots", "fly_in", "fade_in", "ancient_banner",
    };

    /// <summary>Godot 原生的富文本标签（不需要本体的效果，<c>bbcode_enabled</c> 打开就能用）。</summary>
    public static readonly IReadOnlyList<string> NativeTags = new[]
    {
        "b", "i", "u", "s", "center", "left", "right", "fill", "indent", "code",
        "table", "cell", "color", "font", "font_size", "url", "img",
    };

    /// <summary>自己就结束的标签（不需要配对的闭合标签）。</summary>
    private static readonly string[] SelfClosing = { "lb", "rb", "br", "p", "hr" };

    /// <summary>所有「能配对」的标签（拼错时只往这些里面找最接近的）。</summary>
    public static IEnumerable<string> AllPairedTags => EffectTags.Concat(NativeTags);

    /// <summary>这个标签名是不是本体 / Godot 认识的（<c>lb</c> / <c>rb</c> 这种也算）。</summary>
    public static bool IsKnownTag(string name) =>
        EffectTags.Contains(name, StringComparer.Ordinal) || NativeTags.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// 把一段文本里的富文本标签修成「本体一定解析得动」的形状。
    /// 不碰 <c>[lb]</c> / <c>[rb]</c>（那是本体用来显示方括号本身的转义），也不碰没有 <c>]</c> 的裸 <c>[</c>。
    /// </summary>
    public static Result Repair(string? text)
    {
        var notes = new List<string>();
        if (string.IsNullOrEmpty(text) || text.IndexOf('[') < 0) return new Result(text ?? "", notes);

        var sb = new StringBuilder(text.Length + 16);
        var open = new List<string>();          // 还没闭合的标签（按出现顺序）
        var noteSeen = new HashSet<string>(StringComparer.Ordinal);
        void Note(string msg) { if (noteSeen.Add(msg)) notes.Add(msg); }

        int i = 0;
        while (i < text.Length)
        {
            if (text[i] != '[') { sb.Append(text[i++]); continue; }
            int close = text.IndexOf(']', i + 1);
            if (close < 0) { sb.Append(text, i, text.Length - i); break; }   // 没有闭合方括号：原样留着

            string inner = text.Substring(i + 1, close - i - 1);
            bool isEnd = inner.StartsWith('/');
            string raw = isEnd ? inner[1..] : inner;
            string name = NameOf(raw);
            string suffix = raw.Length > name.Length ? raw[name.Length..] : "";   // =参数 那部分

            // [lb] / [rb] 是转义：本体把它当普通文字，不参与配对
            if (!isEnd && SelfClosing.Contains(name, StringComparer.Ordinal))
            {
                sb.Append(text, i, close - i + 1);
                i = close + 1;
                continue;
            }

            // 拼错的标签（只差一个字母）→ 换成最接近的那个
            if (!IsKnownTag(name) && LooksLikeTagName(name))
            {
                string? near = AllPairedTags.FirstOrDefault(t => NearOne(name, t));
                if (near is not null)
                {
                    Note($"「[{(isEnd ? "/" : "")}{name}]」不是游戏认识的标签，已改成「[{(isEnd ? "/" : "")}{near}]」"
                        + (near == "gold" ? "（金色）" : ""));
                    name = near;
                }
            }

            if (isEnd)
            {
                int at = open.LastIndexOf(name);
                if (at < 0)
                {
                    Note($"「[/{name}]」没有对应的开始标签，已删掉");
                }
                else if (at == open.Count - 1)
                {
                    open.RemoveAt(at);
                    sb.Append("[/").Append(name).Append(']');
                }
                else
                {
                    // 交叉嵌套：先把中间那几层关掉，再把它们按原样重新打开（内容不变、只把顺序理顺）
                    var reopened = new List<string>();
                    for (int k = open.Count - 1; k > at; k--)
                    {
                        sb.Append("[/").Append(open[k]).Append(']');
                        reopened.Add(open[k]);
                        open.RemoveAt(k);
                    }
                    open.RemoveAt(at);
                    sb.Append("[/").Append(name).Append(']');
                    for (int k = reopened.Count - 1; k >= 0; k--)
                    {
                        sb.Append('[').Append(reopened[k]).Append(']');
                        open.Add(reopened[k]);
                    }
                    Note("富文本标签交叉了（例如 [gold][blue]…[/gold][/blue]），已改成正确的嵌套顺序");
                }
            }
            else
            {
                open.Add(name);
                sb.Append('[').Append(name).Append(suffix).Append(']');
                if (!IsKnownTag(name))
                    Note($"「[{name}]」不是游戏认识的标签：游戏里会把这对括号原样印出来"
                        + "（想显示一个方括号请写 [lb] / [rb]）");
            }
            i = close + 1;
        }

        // 收尾：还有没关掉的，按出现的相反顺序补上闭合标签
        for (int k = open.Count - 1; k >= 0; k--)
        {
            sb.Append("[/").Append(open[k]).Append(']');
            Note($"「[{open[k]}]」没有闭合，已在结尾补上「[/{open[k]}]」");
        }
        return new Result(DropEmptyPairs(sb.ToString()), notes);
    }

    /// <summary>
    /// 删掉「开了又立刻关」的空标签对（<c>[blue][/blue]</c>）—— 理顺交叉嵌套时可能留下这种空壳，
    /// 对显示没有任何影响，但留在卡面文本里很难看。
    /// </summary>
    private static string DropEmptyPairs(string text)
    {
        while (true)
        {
            string? next = null;
            for (int i = 0; i + 2 < text.Length; i++)
            {
                if (text[i] != '[') continue;
                int close = text.IndexOf(']', i + 1);
                if (close < 0) break;
                string name = text.Substring(i + 1, close - i - 1);
                if (name.Length == 0 || name[0] == '/') continue;
                string end = "[/" + name + "]";
                if (string.CompareOrdinal(text, close + 1, end, 0, end.Length) == 0)
                {
                    next = text.Remove(i, close + 1 - i + end.Length);
                    break;
                }
            }
            if (next is null) return text;
            text = next;
        }
    }

    /// <summary>标签名（去掉 <c>=参数</c> 和空格，统一小写）；<c>[color=#fff]</c> → <c>color</c>。</summary>
    private static string NameOf(string raw)
    {
        int cut = raw.IndexOfAny(new[] { '=', ' ' });
        string n = cut >= 0 ? raw[..cut] : raw;
        return n.Trim().ToLowerInvariant();
    }

    /// <summary>像不像一个标签名（避免把 <c>[3]</c> 这种正文里的方括号当标签处理）。</summary>
    private static bool LooksLikeTagName(string name) =>
        name.Length >= 2 && name.All(c => char.IsAsciiLetter(c) || c == '_');

    /// <summary>两个名字是不是「只差一个字母」（替换 / 多一个 / 少一个）。</summary>
    internal static bool NearOne(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return false;
        if (Math.Abs(a.Length - b.Length) > 1) return false;
        if (a.Length == b.Length)
        {
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i] && ++diff > 1) return false;
            return diff == 1;
        }
        string shortOne = a.Length < b.Length ? a : b;
        string longOne = a.Length < b.Length ? b : a;
        int si = 0, li = 0;
        bool skipped = false;
        while (si < shortOne.Length && li < longOne.Length)
        {
            if (shortOne[si] == longOne[li]) { si++; li++; continue; }
            if (skipped) return false;
            skipped = true;
            li++;
        }
        return true;
    }
}
