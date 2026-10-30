using System.Text;
using Sts2CharForge.Core.Generation;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Plugins;

/// <summary>
/// 插件效果的「模板 → 代码」渲染，以及插件自带文件随 mod 打包（V0.2.315）。
///
/// <para>模板占位符（大小写不敏感）：</para>
/// <list type="bullet">
///   <item><c>{amount}</c> —— 这条效果的数值（状态那条链勾了「数值 = 本状态的层数」时是 <c>(int)base.Amount</c>）</item>
///   <item><c>{self}</c> —— 「自己」那个 Creature 表达式（卡牌/遗物/药水 = <c>base.Owner.Creature</c>，状态 = <c>base.Owner</c>）</item>
///   <item><c>{player}</c> —— 玩家表达式</item>
///   <item><c>{foe}</c> —— 「指定敌人」那个表达式（卡牌 = <c>cardPlay.Target</c>；没有选中目标时链上会兜底第一个敌人）</item>
///   <item><c>{choice}</c> —— <c>choiceContext</c></item>
///   <item><c>{cardSelf}</c> —— 这张牌（<c>this</c>），只在卡牌链有效</item>
///   <item><c>{power}</c> —— 这条效果「增益 / 减益」选的状态类名（要模板自己 <c>PowerCmd.Apply&lt;{power}&gt;</c> 那样用）</item>
///   <item><c>{card}</c> —— 「目标卡」选的那张牌的类名</item>
/// </list>
/// </summary>
public static class PluginGen
{
    /// <summary>渲染一个插件效果；<paramref name="e"/> 不是插件种类时返回 null。</summary>
    public static string? Render(EffectSpec e, PluginCtx ctx)
    {
        string kind = (e.Kind ?? "").Trim();
        var spec = PluginRegistry.EffectOf(kind);
        if (spec is null) return null;
        string body = spec.Emit ?? "";
        if (string.IsNullOrWhiteSpace(body)) return null;

        string amount = ctx.AmountOverride ?? Lit.Dec(e.Amount);
        string text = body
            .Replace("{amount}", amount, StringComparison.OrdinalIgnoreCase)
            .Replace("{self}", ctx.Self, StringComparison.OrdinalIgnoreCase)
            .Replace("{player}", ctx.Player, StringComparison.OrdinalIgnoreCase)
            .Replace("{foe}", ctx.Foe, StringComparison.OrdinalIgnoreCase)
            // V0.2.337：{target} = 「当前指向对象」（和 {foe} 同一个表达式；状态链上就是触发器的目标 / 兜底第一个敌人）
            .Replace("{target}", ctx.Foe, StringComparison.OrdinalIgnoreCase)
            .Replace("{targetorfirst}", ctx.Foe, StringComparison.OrdinalIgnoreCase)
            .Replace("{choice}", ctx.Choice, StringComparison.OrdinalIgnoreCase)
            .Replace("{cardself}", ctx.CardSelf, StringComparison.OrdinalIgnoreCase)
            .Replace("{power}", (e.PowerId ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
            .Replace("{card}", (e.SpawnCardId ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
            // ===== V0.2.338：把「上下文 / 对象 / 变化的卡牌」也交给插件 =====
            .Replace("{combatstate}", ctx.CombatState, StringComparison.OrdinalIgnoreCase)
            .Replace("{cardplay}", ctx.CardPlay, StringComparison.OrdinalIgnoreCase)
            .Replace("{state}", ctx.State, StringComparison.OrdinalIgnoreCase)
            .Replace("{spawncardtype}", ctx.SpawnCardType ?? ("ModelDb.Card<" + CSharpCodeGen.CardTypeExpr(null, (e.SpawnCardId ?? "").Trim()) + ">()"), StringComparison.OrdinalIgnoreCase)
            .Replace("{amountformula}", ctx.AmountOverride ?? amount, StringComparison.OrdinalIgnoreCase)
            .ReplaceLineEndings("\n");
        // 「变化这张牌」这种动作是本工具踩过坑的（打出的牌在 Play 摞里 → Transform 会丢牌），
        // 所以包里给一个**宏**，插件写 {transformSelf:类名} 就展开成验证过的写法（1 张换 1 张）。
        return TransformSelfMacro(text, ctx);
    }

    /// <summary>
    /// 展开 <c>{transformSelf:类名}</c>：把「这张牌变成另一张」做成宏（V0.2.338）。
    ///
    /// <para>为什么不直接让插件写 <c>CardCmd.Transform(this, …)</c>：打出的牌此刻躺在「打出」那一摞里，
    /// 本体 Transform 会把新牌塞回同一摞 → 战斗结束看不见（我们踩过）。这里展开成验证过的写法：
    /// 新牌进弃牌堆 + 把这张牌消耗掉（1 张换 1 张）。只替换占位符本身，不碰其它文本。</para>
    /// </summary>
    private static string TransformSelfMacro(string text, PluginCtx ctx)
    {
        const string key = "{transformSelf:";
        int at = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        int guard = 0;
        while (at >= 0 && guard++ < 16)
        {
            int close = text.IndexOf('}', at);
            if (close < 0) break;
            string cls = text.Substring(at + key.Length, close - at - key.Length).Trim();
            string made = $"__plugMade{guard}";
            string store = $"CardModel {made} = {ctx.CombatState}.CreateCard(ModelDb.Card<{cls}>(), {ctx.Player}); await CardPileCmd.Add({made}, PileType.Discard);";
            string body = string.Equals(ctx.CardSelf, "null", StringComparison.Ordinal)
                ? store   // 没有「这张牌」的链（状态 / 遗物 / 药水）：只把新牌放进弃牌堆
                : store + $" if ({ctx.CardSelf}.Pile is not null) await CardCmd.Exhaust({ctx.Choice}, {ctx.CardSelf});";
            text = text[..at] + body + text[(close + 1)..];
            at = text.IndexOf(key, at + body.Length, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    /// <summary>把渲染结果写进 <see cref="CodeWriter"/>（多行时每行都写）。返回是否写入了（false = 不是插件种类）。</summary>
    public static bool TryEmit(CodeWriter w, EffectSpec e, PluginCtx ctx)
    {
        string? text = Render(e, ctx);
        if (text is null) return false;
        foreach (string line in text.Split('\n'))
            w.Line(line);
        return true;
    }

    /// <summary>把渲染结果按缩进写进 <see cref="StringBuilder"/>（锻造节点图那条链用）。返回是否写入了。</summary>
    public static bool TryEmit(StringBuilder sb, string indent, EffectSpec e, PluginCtx ctx)
    {
        string? text = Render(e, ctx);
        if (text is null) return false;
        foreach (string line in text.Split('\n'))
            sb.AppendLine(indent + line);
        return true;
    }

    /// <summary>
    /// 把插件的自带文件拷进生成工程（需求③「插件内容可以正常与 mod 一同打包」）：
    /// <list type="bullet">
    ///   <item><c>CsFiles</c> → <c>cs/Plugins/&lt;插件 id&gt;/…</c>（会被一起编译进 mod 的 dll）</item>
    ///   <item><c>AssetFiles</c> → <c>plugin_assets/&lt;插件 id&gt;/…</c>（留在工程里，随打包一起走）</item>
    /// </list>
    /// 返回写进去的文件数。
    /// </summary>
    public static int CopyInto(string projectRoot)
    {
        int n = 0;
        foreach (var p in PluginRegistry.Plugins)
        {
            if (p.Problems.Count > 0) continue;
            foreach (string rel in p.CsFiles.Where(x => !string.IsNullOrWhiteSpace(x)))
                n += CopyOne(p, rel, Path.Combine(projectRoot, "cs", "Plugins", Safe(p.Id)));
            foreach (string rel in p.AssetFiles.Where(x => !string.IsNullOrWhiteSpace(x)))
                n += CopyOne(p, rel, Path.Combine(projectRoot, "plugin_assets", Safe(p.Id)));
        }
        return n;

        static string Safe(string id) => string.Concat(id.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
    }

    private static int CopyOne(PluginSpec p, string rel, string destDir)
    {
        try
        {
            string src = Path.GetFullPath(Path.Combine(p.Dir, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!src.StartsWith(Path.GetFullPath(p.Dir), StringComparison.OrdinalIgnoreCase) || !File.Exists(src)) return 0;
            Directory.CreateDirectory(destDir);
            File.Copy(src, Path.Combine(destDir, Path.GetFileName(src)), overwrite: true);
            return 1;
        }
        catch
        {
            return 0;   // 拷贝失败也不能挡住生成（插件是附加内容）
        }
    }

    // ==================== 自定义页面（V0.2.319）====================

    /// <summary>把一页的栏位值渲染成 <see cref="PluginPageSpec.Emit"/> 的文本（占位符 <c>{字段key}</c>）。</summary>
    public static string RenderPage(PluginSpec p, PluginPageSpec page)
    {
        string text = page.Emit ?? "";
        foreach (var f in page.Fields.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Key)))
            text = text.Replace("{" + f.Key.Trim() + "}", PluginRegistry.PageValueOf(p, page, f), StringComparison.OrdinalIgnoreCase);
        return text.ReplaceLineEndings("\n");
    }

    /// <summary>这页要写进工程的相对路径（<c>{id}</c> / <c>{字段key}</c> 都会替换）。</summary>
    public static string TargetOf(PluginSpec p, PluginPageSpec page)
    {
        string path = (page.Target ?? "").Replace("{id}", p.Id, StringComparison.OrdinalIgnoreCase);
        foreach (var f in page.Fields.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Key)))
            path = path.Replace("{" + f.Key.Trim() + "}", FileSafe(PluginRegistry.PageValueOf(p, page, f)), StringComparison.OrdinalIgnoreCase);
        return path.Replace('\\', '/').Trim();
    }

    /// <summary>路径里用的值：去掉非法字符（用户可能把事件名填成中文 / 带空格）。</summary>
    private static string FileSafe(string v)
    {
        var sb = new StringBuilder();
        foreach (char c in (v ?? "").Trim())
            sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c > 0x7F ? c : '_');
        return sb.ToString();
    }

    /// <summary>
    /// 把插件「自定义页」里填的值渲染成文件写进生成工程（只处理 <see cref="PluginPageSpec.Produces"/> 的页）。
    /// 生成时调一次 —— 于是这些内容随 mod 一起编译 / 打包。返回写进去的文件数。
    /// </summary>
    public static int WritePages(string projectRoot)
    {
        int n = 0;
        foreach (var pl in PluginRegistry.Plugins)
        {
            if (pl.Problems.Count > 0) continue;
            foreach (var page in pl.Pages.Where(x => x is not null && x.Produces))
            {
                string target = TargetOf(pl, page);
                if (target.Length == 0) continue;
                try
                {
                    string full = Path.GetFullPath(Path.Combine(projectRoot, target.Replace('/', Path.DirectorySeparatorChar)));
                    string rootFull = Path.GetFullPath(projectRoot);
                    if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;   // 越界，跳过
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllText(full, RenderPage(pl, page), new System.Text.UTF8Encoding(false));
                    n++;
                }
                catch { /* 插件是附加内容：写失败不能挡住生成 */ }
            }
        }
        return n;
    }

    /// <summary>生成时拼给模板用的上下文（每条链自己填）。</summary>
    /// <summary>
    /// 模板上下文（V0.2.338 扩充）：每条链填自己那份表达式，插件模板里的占位符就从这里换。
    /// 新加的几项都有默认值 —— 老调用点不改也能编译，只是那几个占位符在没传的链上等于默认写法。
    /// </summary>
    public readonly record struct PluginCtx(
        string Self, string Player, string Foe, string Choice,
        string CardSelf = "this", string? AmountOverride = null,
        // V0.2.338：把「上下文」也暴露给插件 —— 以前只有 amount / 目标，拿不到战斗状态、本次出牌、状态实例
        string CombatState = "base.Owner.Creature.CombatState",
        string CardPlay = "cardPlay",
        string State = "this",
        string? SpawnCardType = null);
}
