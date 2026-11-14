using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// v0.113 把一些插件常用的静态 API 挪走了（Rng.Shuffle / CardSelectCmd.PushSelector）。
/// 插件作者不会跟着游戏每次更新，所以这里只在**生成出来的工程**里做兼容：
///   · 把旧调用改写成兼容助手；
///   · 额外生成一个 cs/Sts2LegacyPluginCompat.cs。
/// 不修改用户的插件源文件，也不影响插件自己的工程。
/// </summary>
public static class PluginCompatGen
{
    public const string File = "Sts2LegacyPluginCompat";

    public static void Apply(string projectRoot)
    {
        try
        {
            string pluginDir = Path.Combine(projectRoot, "cs", "Plugins");
            if (!Directory.Exists(pluginDir)) return;

            foreach (string file in Directory.GetFiles(pluginDir, "*.cs", SearchOption.AllDirectories))
            {
                string text = System.IO.File.ReadAllText(file);
                string rewritten = Rewrite(text);
                if (rewritten == text) continue;
                System.IO.File.WriteAllText(file, rewritten, new System.Text.UTF8Encoding(false));
            }

            string outFile = Path.Combine(projectRoot, "cs", File + ".cs");
            System.IO.File.WriteAllText(outFile, Source, new System.Text.UTF8Encoding(false));
            WrapPluginNamespaces(projectRoot);
        }
        catch
        {
            // 插件是附加内容：兼容层写失败不能挡住整个生成
        }
    }

    /// <summary>
    /// V0.3.64：插件自带的 .cs 以前全在**全局命名空间**里，两个插件都有
    /// <c>RandBuffPool</c> / <c>CardGifSwapConfig</c> 这种裸类名时就是 CS0101。
    /// 这里把每个插件的副本包进 <c>Sts2CharForge.Generated.Plugin_&lt;id&gt;</c>，
    /// 再在 GlobalUsings 里 global using 这些命名空间，主模组 / 插件模板里
    /// 仍然可以写裸类名（例如 <c>PluginDemo.Half(...)</c>）。
    ///
    /// <para>只包**没有 namespace 声明**、且没有 global using / extern alias 的文件；
    /// 已经自己写了 namespace 的插件不强行改（作者自己负责它的命名空间）。</para>
    /// </summary>
    public static void WrapPluginNamespaces(string projectRoot)
    {
        try
        {
            string pluginRoot = Path.Combine(projectRoot, "cs", "Plugins");
            if (!Directory.Exists(pluginRoot)) return;
            string globalUsings = Path.Combine(projectRoot, "cs", "GlobalUsings.cs");
            string usingText = System.IO.File.Exists(globalUsings) ? System.IO.File.ReadAllText(globalUsings) : "";
            var addUsings = new List<string>();

            foreach (string dir in Directory.GetDirectories(pluginRoot))
            {
                string id = new string(Path.GetFileName(dir)
                    .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
                if (id.Length == 0) continue;
                string ns = "Sts2CharForge.Generated.Plugin_" + id;
                bool wrappedAny = false;

                string[] files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
                // 同一个插件里只要有一个文件自己写了 namespace（或 global using / extern alias），
                // 就整只插件都不包：否则同一插件的类会被拆到两个命名空间里，互相引用会断。
                bool mixed = files.Any(file =>
                {
                    string text = System.IO.File.ReadAllText(file);
                    return Regex.IsMatch(text, @"(?m)^\s*namespace\s", RegexOptions.CultureInvariant)
                        || text.Contains("global using", StringComparison.Ordinal)
                        || text.Contains("extern alias", StringComparison.Ordinal);
                });
                if (mixed) continue;

                foreach (string file in files)
                {
                    string text = System.IO.File.ReadAllText(file);
                    string wrapped = "namespace " + ns + "\n{\n" + text + "\n}\n";
                    System.IO.File.WriteAllText(file, wrapped, new System.Text.UTF8Encoding(false));
                    wrappedAny = true;
                }

                if (wrappedAny && !usingText.Contains("global using " + ns + ";", StringComparison.Ordinal))
                {
                    addUsings.Add("global using " + ns + ";");
                }
            }

            if (addUsings.Count > 0)
            {
                usingText = usingText.TrimEnd() + "\n" + string.Join("\n", addUsings) + "\n";
                System.IO.File.WriteAllText(globalUsings, usingText, new System.Text.UTF8Encoding(false));
            }
        }
        catch
        {
            // 同上：插件是附加内容，失败不能挡住整个生成
        }
    }

    /// <summary>只替换插件里确实存在的旧静态调用；普通 CardSelectCmd.FromHand 等一概不碰。</summary>
    public static string Rewrite(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = text.Replace("MegaCrit.Sts2.Core.Commands.CardSelectCmd.PushSelector(",
            "global::Sts2CharForge.PluginCompat.LegacyPluginCompat.PushSelector(");
        text = text.Replace("MegaCrit.Sts2.Core.Commands.CardSelectCmd.UseSelector(",
            "global::Sts2CharForge.PluginCompat.LegacyPluginCompat.UseSelector(");
        text = text.Replace("MegaCrit.Sts2.Core.Commands.CardSelectCmd.SetTestSelector(",
            "global::Sts2CharForge.PluginCompat.LegacyPluginCompat.SetTestSelector(");
        text = text.Replace("CardSelectCmd.PushSelector(",
            "global::Sts2CharForge.PluginCompat.LegacyPluginCompat.PushSelector(");
        text = text.Replace("CardSelectCmd.UseSelector(",
            "global::Sts2CharForge.PluginCompat.LegacyPluginCompat.UseSelector(");
        text = text.Replace("CardSelectCmd.SetTestSelector(",
            "global::Sts2CharForge.PluginCompat.LegacyPluginCompat.SetTestSelector(");
        return EnsurePlayerChoiceContextGameActionType(text);
    }

    /// <summary>
    /// v0.113 给 PlayerChoiceContext 加了抽象属性 GameActionType。
    /// 插件里自己继承 PlayerChoiceContext 的包装器（ShuffleHelper.NullSafeChoiceContext 等）
    /// 如果没实现它，会 CS0534。这里在**生成工程的插件副本**里补一句 override。
    /// 只补继承 PlayerChoiceContext 且正文里还没有 GameActionType 的类。
    /// </summary>
    public static string EnsurePlayerChoiceContextGameActionType(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("PlayerChoiceContext", StringComparison.Ordinal)) return text;

        var rx = new Regex(@"\bclass\s+[A-Za-z_]\w*\s*(?:<[^>{]*>)?\s*:\s*([^{]+?)\{",
            RegexOptions.Singleline);
        MatchCollection matches = rx.Matches(text);
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            Match m = matches[i];
            if (!Regex.IsMatch(m.Groups[1].Value, @"\bPlayerChoiceContext\b")) continue;

            int open = text.IndexOf('{', m.Index);
            if (open < 0) continue;
            int close = FindMatchingBrace(text, open);
            if (close < 0) continue;

            string body = text.Substring(open + 1, close - open - 1);
            if (Regex.IsMatch(body, @"\bGameActionType\b")) continue;

            // 类声明所在行的缩进；成员缩进 +4 空格
            int lineStart = text.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
            string line = text.Substring(lineStart, m.Index - lineStart);
            string indent = new string(line.TakeWhile(ch => ch == ' ' || ch == '\t').ToArray());
            string memberIndent = indent + "    ";
            string insert = "\n" + memberIndent
                + "public override global::MegaCrit.Sts2.Core.Entities.Multiplayer.GameActionType? GameActionType => null;"
                + "   // v0.113 PlayerChoiceContext 新增抽象成员\n" + indent;
            text = text.Insert(close, insert);
        }
        return text;
    }

    /// <summary>从 class 的 { 开始找配对的 }，跳过字符串 / 字符 / 行注释 / 块注释。</summary>
    private static int FindMatchingBrace(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                i = s.IndexOf('\n', i);
                if (i < 0) return -1;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (i < 0) return -1;
                i++;
                continue;
            }
            // 普通 / 逐字 / 插值字符串：整个跳过（插值里的括号不影响类体配对）
            if (c == '"')
            {
                i = SkipString(s, i, verbatim: false);
                if (i < 0) return -1;
                continue;
            }
            if (c == '@' && i + 1 < s.Length && s[i + 1] == '"')
            {
                i = SkipString(s, i + 1, verbatim: true);
                if (i < 0) return -1;
                continue;
            }
            if (c == '$' && i + 1 < s.Length && s[i + 1] == '"')
            {
                i = SkipString(s, i + 1, verbatim: false);
                if (i < 0) return -1;
                continue;
            }
            if (c == '$' && i + 2 < s.Length && s[i + 1] == '@' && s[i + 2] == '"')
            {
                i = SkipString(s, i + 2, verbatim: true);
                if (i < 0) return -1;
                continue;
            }
            if (c == '\'')
            {
                i = SkipChar(s, i);
                if (i < 0) return -1;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static int SkipString(string s, int quote, bool verbatim)
    {
        for (int i = quote + 1; i < s.Length; i++)
        {
            if (verbatim && s[i] == '"' && i + 1 < s.Length && s[i + 1] == '"') { i++; continue; }
            if (!verbatim && s[i] == '\\') { i++; continue; }
            if (s[i] == '"') return i;
        }
        return -1;
    }

    private static int SkipChar(string s, int quote)
    {
        for (int i = quote + 1; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '\'') return i;
        }
        return -1;
    }

    // v0.113：Rng.Shuffle<T>(IList<T>) 被删了，改成 ListExtensions.UnstableShuffle/StableShuffle。
    // 插件普遍写的是 rng.Shuffle(list)，这里用同名扩展方法兜住（放在 Rng 自己的命名空间里，
    // 插件只要 using 了它原本就有的 MegaCrit.Sts2.Core.Random 就能找到）。
    public static string Source => """
// <auto-generated> 旧插件 API 兼容层（v0.113） </auto-generated>
#nullable enable annotations
#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace MegaCrit.Sts2.Core.Random
{
    /// <summary>v0.113 删除的 Rng.Shuffle&lt;T&gt;(IList&lt;T&gt;) 兼容扩展。</summary>
    public static class Sts2LegacyRngShuffleExtensions
    {
        public static void Shuffle<T>(this Rng rng, IList<T> list)
        {
            if (list is null || list.Count < 2) return;
            List<T> copy = list.ToList();
            MegaCrit.Sts2.Core.Extensions.ListExtensions.UnstableShuffle(copy, rng);
            for (int i = 0; i < list.Count; i++) list[i] = copy[i];
        }
    }
}

namespace Sts2CharForge.PluginCompat
{
    /// <summary>v0.113 把 CardSelectCmd 上的选择器入口挪到了 RunManager.Instance.CardSelectorStack。</summary>
    public static class LegacyPluginCompat
    {
        public static IDisposable PushSelector(ICardSelector selector, bool localOnly = false)
        {
            CardSelectorStack stack = RunManager.Instance.CardSelectorStack;
            if (stack is null) throw new InvalidOperationException("CardSelectorStack is not available.");
            if (localOnly) return stack.UseLocalSelector(selector);

            Player? player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
            if (player is null) throw new InvalidOperationException("Local player not found.");
            return stack.PushSelector(player, selector);
        }

        public static IDisposable UseSelector(ICardSelector selector, bool localOnly = false)
        {
            CardSelectorStack stack = RunManager.Instance.CardSelectorStack;
            if (stack is null) throw new InvalidOperationException("CardSelectorStack is not available.");
            if (localOnly) return stack.UseLocalSelector(selector);

            Player? player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
            if (player is null) throw new InvalidOperationException("Local player not found.");
            return stack.UseSelector(player, selector);
        }

        public static IDisposable SetTestSelector(ICardSelector selector, bool localOnly = false)
        {
            // 旧 API 的 localOnly 在测试选择器上没有对应物：仍安装全局测试选择器，保证插件能运行。
            return CardSelectorStack.SetTestSelector(selector);
        }
    }
}
""";
}
