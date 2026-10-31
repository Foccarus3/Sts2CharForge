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
            string body = !ctx.CardSelfIsCard
                ? store   // 没有「这张牌」的链（状态 / 遗物 / 药水 / 非卡牌上的锻造图）：只把新牌放进弃牌堆
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
            if (p.Problems.Count > 0 || !p.Enabled) continue;   // V0.2.358：禁用的插件不随 mod 打包
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

    /// <summary>
    /// 把一页的栏位值渲染成 <see cref="PluginPageSpec.Emit"/> 的文本（占位符 <c>{字段key}</c>）。
    ///
    /// <para>V0.2.349（框架）：<c>list</c> 栏位用**块**展开 ——</para>
    /// <code>
    /// {moves}
    ///     new Move("{name}", {damage}),
    /// {/moves}
    /// </code>
    /// <para>块里每行渲染一遍，行内可用该行的子栏位，以及 <c>{index}</c>（从 1 起）/ <c>{index0}</c>（从 0 起）。
    /// 没写块（或没配对）时这个 list 不产出任何文本（配对错误在加载时已经报进 <see cref="PluginPageSpec.Problems"/>）。</para>
    /// </summary>
    public static string RenderPage(PluginSpec p, PluginPageSpec page)
    {
        // V0.2.357：一页可以产出**多个文件**（outputs[]）；没写 outputs 就是老的「一个 target + 一段 emit」。
        // 预览里按文件分段显示（每段前一行「// ---- 文件：… ----」），一眼看出每个文件长什么样。
        if (page.Outputs.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var o in page.Outputs)
            {
                if (o is null || !o.Ok) continue;
                if (o.IsRowOutput)
                {
                    var lf = page.Fields.FirstOrDefault(x => x is not null && x.IsList
                        && string.Equals((x.Key ?? "").Trim(), (o.ForEach ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                    if (lf is null) continue;
                    var rows = PluginRegistry.PageRowsOf(p, page, lf);
                    for (int i = 0; i < rows.Count; i++)
                    {
                        sb.AppendLine($"// ---- 文件（按「{lf.Key}」第 {i + 1} 行）：{RenderTarget(o.Target, p, page, lf, rows[i], i)} ----");
                        sb.AppendLine(RenderOne(o.Emit, p, page, lf, rows[i], i));
                    }
                    continue;
                }
                sb.AppendLine($"// ---- 文件：{RenderTarget(o.Target, p, page, null, null, 0)} ----");
                sb.AppendLine(RenderOne(o.Emit, p, page, null, null, 0));
            }
            return sb.ToString().ReplaceLineEndings("\n");
        }
        return RenderOne(page.Emit ?? "", p, page, null, null, 0).ReplaceLineEndings("\n");
    }

    /// <summary>渲染一段模板：行内子栏位（按行产出时）→ list 块 → 页面级标量 → 条件宏。</summary>
    private static string RenderOne(string text, PluginSpec p, PluginPageSpec page,
        PluginFieldSpec? rowList, Dictionary<string, string>? row, int rowIndex)
    {
        text ??= "";
        if (rowList is not null && row is not null)
        {
            text = text
                .Replace("{index0}", rowIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{index}", (rowIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
            foreach (var sub in rowList.Fields.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Key)))
                text = text.Replace("{" + sub.Key.Trim() + "}", PluginRegistry.RowValueOf(rowList, row, sub), StringComparison.OrdinalIgnoreCase);
        }
        foreach (var lf in page.Fields.Where(x => x is not null && x.IsList))
            text = ExpandListBlock(text, p, page, lf);
        foreach (var f in page.Fields.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Key) && !x.IsList))
            text = text.Replace("{" + f.Key.Trim() + "}", PluginRegistry.PageValueOf(p, page, f), StringComparison.OrdinalIgnoreCase);
        return ExpandConditions(text, p, page, rowList, row);
    }

    /// <summary>把 target 模板渲染成实际路径（行内子栏位 + 页面级标量；值做文件名安全处理）。</summary>
    private static string RenderTarget(string target, PluginSpec p, PluginPageSpec page,
        PluginFieldSpec? rowList, Dictionary<string, string>? row, int rowIndex)
    {
        string path = (target ?? "").Replace("{id}", p.Id, StringComparison.OrdinalIgnoreCase);
        if (rowList is not null && row is not null)
        {
            path = path
                .Replace("{index0}", rowIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{index}", (rowIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
            foreach (var sub in rowList.Fields.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Key)))
                path = path.Replace("{" + sub.Key.Trim() + "}", FileSafe(PluginRegistry.RowValueOf(rowList, row, sub)), StringComparison.OrdinalIgnoreCase);
        }
        foreach (var f in page.Fields.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Key) && !x.IsList))
            path = path.Replace("{" + f.Key.Trim() + "}",
                f.IsRef ? PluginRegistry.PageValueOf(p, page, f) : FileSafe(PluginRegistry.PageValueOf(p, page, f)),
                StringComparison.OrdinalIgnoreCase);
        return path.Replace('\\', '/').Trim();
    }

    /// <summary>
    /// 条件宏（V0.2.357）：<c>{#key}…{/key}</c> = 该字段**有值**才渲染；<c>{^key}…{/^key}</c> = 为空才渲染。
    /// 「有值」= 非空、且不是 0 / false / 否。按行产出时行内子栏位也能当条件。
    /// </summary>
    private static string ExpandConditions(string text, PluginSpec p, PluginPageSpec page,
        PluginFieldSpec? rowList, Dictionary<string, string>? row)
    {
        if (string.IsNullOrEmpty(text)) return text;
        for (int guard = 0; guard < 200; guard++)
        {
            bool did = false;
            var keys = new List<(string Key, string Value)>();
            foreach (var f in page.Fields.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Key) && !x.IsList))
                keys.Add((f.Key.Trim(), PluginRegistry.PageValueOf(p, page, f)));
            if (rowList is not null && row is not null)
                foreach (var sub in rowList.Fields.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Key)))
                    keys.Add((sub.Key.Trim(), PluginRegistry.RowValueOf(rowList, row, sub)));
            foreach (var (ck, cv) in keys)
            {
                foreach (bool wantTruthy in new[] { true, false })
                {
                    string open = wantTruthy ? "{#" + ck + "}" : "{^" + ck + "}";
                    string close = wantTruthy ? "{/" + ck + "}" : "{/^" + ck + "}";
                    int at = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) continue;
                    int end = text.IndexOf(close, at + open.Length, StringComparison.OrdinalIgnoreCase);
                    if (end < 0) continue;      // 没配对：原样留着（加载时已报问题）
                    string inner = text.Substring(at + open.Length, end - at - open.Length);
                    bool keep = IsTruthy(cv) == wantTruthy;
                    text = text.Substring(0, at) + (keep ? inner : "") + text.Substring(end + close.Length);
                    did = true;
                    break;
                }
                if (did) break;
            }
            if (!did) break;
        }
        return text;
    }

    /// <summary>条件宏里的「有值」判据（空 / 0 / false / 否 都算没有）。</summary>
    private static bool IsTruthy(string? v)
    {
        string s = (v ?? "").Trim();
        if (s.Length == 0) return false;
        if (s is "0" || s == "否") return false;
        return !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>展开一个 list 栏位的块：<c>{key} … {/key}</c>（可能出现多次）。</summary>
    private static string ExpandListBlock(string text, PluginSpec p, PluginPageSpec page, PluginFieldSpec listField)
    {
        string key = (listField.Key ?? "").Trim();
        if (key.Length == 0 || string.IsNullOrEmpty(text)) return text;
        string open = "{" + key + "}";
        string close = "{/" + key + "}";
        int at = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        int guard = 0;
        while (at >= 0 && guard++ < 200)
        {
            int end = text.IndexOf(close, at + open.Length, StringComparison.OrdinalIgnoreCase);
            if (end < 0) break;      // 没配对：原样留着（加载时已报问题）
            string inner = text.Substring(at + open.Length, end - at - open.Length);
            var rows = PluginRegistry.PageRowsOf(p, page, listField);
            var sb = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                string one = inner
                    .Replace("{index}", (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                    .Replace("{index0}", i.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
                foreach (var sub in listField.Fields.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Key)))
                    one = one.Replace("{" + sub.Key.Trim() + "}", PluginRegistry.RowValueOf(listField, rows[i], sub), StringComparison.OrdinalIgnoreCase);
                sb.Append(one);
            }
            string replaced = sb.ToString();
            text = text.Substring(0, at) + replaced + text.Substring(end + close.Length);
            at = text.IndexOf(open, Math.Min(text.Length, at + replaced.Length), StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }
    /// <summary>这页要写进工程的相对路径（<c>{id}</c> / <c>{字段key}</c> 都会替换）。</summary>
    public static string TargetOf(PluginSpec p, PluginPageSpec page)
    {
        string path = (page.Target ?? "").Replace("{id}", p.Id, StringComparison.OrdinalIgnoreCase);
        foreach (var f in page.Fields.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Key)))
            path = path.Replace("{" + f.Key.Trim() + "}",
                f.IsList ? "" : FileSafe(PluginRegistry.PageValueOf(p, page, f)), StringComparison.OrdinalIgnoreCase);
        return path.Replace('\\', '/').Trim();
    }

    /// <summary>
    /// V0.2.357：一页要写的全部文件（<c>(相对路径, 内容)</c>）。没有 <see cref="PluginPageSpec.Outputs"/> 就是
    /// 老的一个文件；写了 outputs 就有多个 —— 其中 <c>forEach</c> 那条会按 list 的每一行各出一个文件。
    /// 生成端（<see cref="WritePages"/>）和界面的「生成到当前工程」都走这一份，避免两边口径不一致。
    /// </summary>
    public static List<(string Target, string Content)> Renders(PluginSpec p, PluginPageSpec page)
    {
        var list = new List<(string, string)>();
        if (page.Outputs.Count > 0)
        {
            foreach (var o in page.Outputs)
            {
                if (o is null || !o.Ok) continue;
                if (o.IsRowOutput)
                {
                    var lf = page.Fields.FirstOrDefault(x => x is not null && x.IsList
                        && string.Equals((x.Key ?? "").Trim(), (o.ForEach ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                    if (lf is null) continue;
                    var rows = PluginRegistry.PageRowsOf(p, page, lf);
                    for (int i = 0; i < rows.Count; i++)
                    {
                        string target = RenderTarget(o.Target, p, page, lf, rows[i], i);
                        if (target.Length == 0) continue;
                        list.Add((target, RenderOne(o.Emit, p, page, lf, rows[i], i).ReplaceLineEndings("\n")));
                    }
                    continue;
                }
                string one = RenderTarget(o.Target, p, page, null, null, 0);
                if (one.Length == 0) continue;
                list.Add((one, RenderOne(o.Emit, p, page, null, null, 0).ReplaceLineEndings("\n")));
            }
            return list;
        }
        string legacy = TargetOf(p, page);
        if (legacy.Length == 0) return list;
        list.Add((legacy, RenderPage(p, page)));
        return list;
    }

    /// <summary>把一页的全部文件写进工程目录；返回写成功的文件数（失败的单个文件跳过，不影响别的）。</summary>
    public static int WritePageTo(string projectRoot, PluginSpec p, PluginPageSpec page)
    {
        int n = 0;
        foreach (var (target, content) in Renders(p, page))
        {
            try
            {
                string full = Path.GetFullPath(Path.Combine(projectRoot, target.Replace('/', Path.DirectorySeparatorChar)));
                string rootFull = Path.GetFullPath(projectRoot);
                if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;   // 越界，跳过
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content, new System.Text.UTF8Encoding(false));
                n++;
            }
            catch { /* 插件是附加内容：写失败不能挡住生成 */ }
        }
        return n;
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
            if (pl.Problems.Count > 0 || !pl.Enabled) continue;   // V0.2.358：禁用的插件不产出
            foreach (var page in pl.Pages.Where(x => x is not null && x.Produces && x.Problems.Count == 0))
            {
                // V0.2.357：一页可能产出多个文件（outputs[] / 按行）—— 统一走 WritePageTo
                n += WritePageTo(projectRoot, pl, page);
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
        string? SpawnCardType = null,
        // V0.2.339：这一条链上「这张牌」到底是不是**真的卡牌**（卡牌链 / 卡牌上的锻造图 = true）。
        // {transformSelf:…} 宏只在这条为 true 时才「顺手把这张牌消耗掉」——
        // 遗物 / 药水 / 状态的 this 是遗物 / 药水 / 状态对象，没有 CardModel.Pile，写那句会 CS1061。
        bool CardSelfIsCard = true);
}
