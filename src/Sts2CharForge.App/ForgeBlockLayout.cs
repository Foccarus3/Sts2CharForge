using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.App;

/// <summary>
/// 一块「堆叠积木」（类 Scratch 的只读视图）。
/// Indent = 缩进层级（根 0）；Branch = 属于哪个分支（「是」/「否」/「每个」…，根为空）；
/// IsContainer = C 形块（下面缩进的那些是它的内容）；IsReport = 报告块（椭圆的数值块）。
/// Summary = 这一块**实际配了什么**（选中项 / 数值），没有就空串。
/// </summary>
public sealed record ForgeBlock(string NodeId, string Kind, string Text, string Summary, int Indent,
    string Branch = "", bool IsContainer = false, bool IsReport = false);

/// <summary>
/// 把「锻造」页那张节点图**按执行顺序**排成一摞积木（只读，不改任何数据、不做拖拽）。
///
/// 为什么能这么排：生成端本来就是**从【触发时机】的输出口往下走**的（结构决定顺序），
/// 所以「谁接在谁后面、谁在谁里面」在数据里已经确定了 —— 排出来不用用户摆位置，
/// 也不会出现「线断了 / 接错了」（那是节点图特有的问题）。
///
/// 这是**观感验证版**：图上怎么连，这里就怎么排。要改成能拖的编辑器是下一步的事。
/// </summary>
public static class ForgeBlockLayout
{
    /// <summary>把一块布排成积木列表。空图返回空列表（界面据此显示一句提示）。</summary>
    public static IReadOnlyList<ForgeBlock> Build(ForgeGraphSpec? g)
    {
        var list = new List<ForgeBlock>();
        if (g is null || g.Nodes.Count == 0) return list;

        var byId = new Dictionary<string, ForgeNodeSpec>(StringComparer.Ordinal);
        foreach (var n in g.Nodes)
            if (n is not null && n.Id.Length > 0) byId[n.Id] = n;

        // 连线分两类，判据和节点图 / 积木树**一模一样**（看终点那个下标是「收来源的槽」还是「执行链口」）：
        //   · 收来源的槽（【if】的「条件」/【状态】的「归属」/【效果】的数值与对象…）= 内联表达式，不占一行；
        //   · 执行链 / 分支（【if】的「是 / 否」）= 一条线一个后继。
        // ⚠ 判据是**类型配不配得上**，不是「同下标有没有出口」：
        //   【if】的「条件」是**入口 1**、而**出口 1** 是「否」（【状态】的「归属」是入口 0、出口 0 是「数值」），
        //   老规矩（同下标有出口就不算插槽）会把它们误判成执行链 —— 那正是「如果判断为否」那个老 bug 的一半。
        var next = new Dictionary<(string, int), ForgeNodeSpec>();
        var inline = new Dictionary<string, List<(string Slot, ForgeNodeSpec From)>>(StringComparer.Ordinal);

        foreach (var l in g.Links)
        {
            if (l is null) continue;
            if (!byId.TryGetValue(l.From, out var from) || !byId.TryGetValue(l.To, out var to)) continue;
            if (!ForgeBlockTree.IsSlotLink(from, l.FromPort, to, l.ToPort))
            {
                next[(from.Id, l.FromPort)] = to;
                continue;
            }
            var inPort = to.KindDef?.InputPort(l.ToPort);
            var outPort = to.KindDef?.OutputPort(l.ToPort);
            string slotLabel = inPort is { HasLabel: true } p ? p.Label
                : outPort is { HasLabel: true } q ? q.Label : "接入";
            if (!inline.TryGetValue(to.Id, out var slots)) inline[to.Id] = slots = new List<(string, ForgeNodeSpec)>();
            slots.Add((slotLabel, from));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        // 已经被「内联」进上一块的（条件 / 数值 / 对象）不再单占一行 —— 它们在 Scratch 里就是块里的一个槽
        var inlined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slots in inline.Values)
            foreach (var (_, src) in slots) inlined.Add(src.Id);

        // 根：触发时机那一块（执行链的起点）
        foreach (var t in g.Nodes.Where(n => n.Kind == "Trigger"))
        {
            list.Add(Make(t, 0, inline: inline));
            seen.Add(t.Id);
            Walk(t, 0, "", list, next, seen, inline);
        }
        // 剩下的：真悬空的块（线断了 / 只在内联槽里用过的那种不再列）
        foreach (var n in g.Nodes)
        {
            if (seen.Contains(n.Id) || n.Kind == "Trigger" || inlined.Contains(n.Id)) continue;
            list.Add(Make(n, 0, orphan: true, inline: inline));
            seen.Add(n.Id);
            Walk(n, 0, "", list, next, seen, inline);
        }
        return list;
    }

    /// <summary>
    /// 从一块往下走。三种出口分开处理（按本体生成端的读法）：
    ///   · 出口 0 = 执行链的下一块（同层级）；
    ///   · 【if】的出口 **0 / 1** = 「是」/「否」两支 —— **先出分支标题，再缩进一层放内容**；
    ///   · 别的出口 = 内容（【对象】那种），缩进一层。
    /// 【if】那一支走完会插一句「（if 到这里结束）」收口 —— 不然「if 后面的块」会看起来像 if 的内容。
    /// </summary>
    private static void Walk(ForgeNodeSpec node, int indent, string branch, List<ForgeBlock> list,
        Dictionary<(string, int), ForgeNodeSpec> next, HashSet<string> seen,
        Dictionary<string, List<(string Slot, ForgeNodeSpec From)>> inline)
    {
        // ⚠【if】要**先**判：新版布局里它的出口 0 就是「是」那一支（不是执行链的下一块），
        //   按普通块那样顺着出口 0 走会把「是」当成同层级的下一块（两支也只剩「否」）。
        if (node.Kind == "If")
        {
            WalkBranch(node, 0, list, next, seen, inline);
            WalkBranch(node, 1, list, next, seen, inline);
            list.Add(new ForgeBlock(node.Id + "#end", "End", "（if 到这里结束）", "", indent, "",
                IsContainer: false, IsReport: false));
            return;
        }
        if (next.TryGetValue((node.Id, 0), out var follow) && seen.Add(follow.Id))
        {
            list.Add(Make(follow, indent, branch, inline: inline));
            Walk(follow, indent, branch, list, next, seen, inline);
        }
        var def = node.KindDef;
        int outs = def?.Outputs.Count ?? 0;
        for (int i = 1; i < outs; i++)
        {
            if (!next.TryGetValue((node.Id, i), out var child) || !seen.Add(child.Id)) continue;
            string label = def?.OutputPort(i) is { HasLabel: true } p ? p.Label : "";
            list.Add(Make(child, indent + 1, label, inline: inline));
            Walk(child, indent + 1, label, list, next, seen, inline);
        }
    }

    /// <summary>【if】的一支：先插一行分支标题（是 / 否），内容缩进一层；这一支里的块跟着缩进。</summary>
    private static void WalkBranch(ForgeNodeSpec iff, int port, List<ForgeBlock> list,
        Dictionary<(string, int), ForgeNodeSpec> next, HashSet<string> seen,
        Dictionary<string, List<(string Slot, ForgeNodeSpec From)>> inline)
    {
        if (!next.TryGetValue((iff.Id, port), out var head) || !seen.Add(head.Id)) return;
        string label = iff.KindDef?.OutputPort(port) is { HasLabel: true } p ? p.Label : (port == 0 ? "是" : "否");
        list.Add(new ForgeBlock(iff.Id + "#br" + port, "Branch", label + "：", "", 1, label,
            IsContainer: false, IsReport: false));
        list.Add(Make(head, 2, label, inline: inline));
        Walk(head, 2, label, list, next, seen, inline);
    }

    private static ForgeBlock Make(ForgeNodeSpec n, int indent, string branch = "", bool orphan = false,
        Dictionary<string, List<(string Slot, ForgeNodeSpec From)>>? inline = null)
    {
        string kind = n.Kind;
        // 报告块（Scratch 里那些椭圆的数值块）：数值 / 状态 / 能量 / 条件 / 且 / 或
        bool report = kind is "Value" or "Power" or "Energy" or "Condition" or "And" or "Or";
        // C 形块：里面有内容（if 的两支、对象的"每个"）
        bool container = !report && kind is "If" or "Unit";
        string text = orphan && !report && kind != "If" ? "（没接上）" + n.Title : n.Title;
        string summary = Summary(n);
        // 内联的「收来源」块（【if】的条件 / 【效果】的数值与对象）：显示在块后面，
        // 这样它们就不必单独占一行（Scratch 里这些就是块里面的一个槽）。
        if (inline is not null && inline.TryGetValue(n.Id, out var slots))
        {
            // 内联槽：条件读成一句问句「if 手牌数不少于 2 张 ?」，别的写成「数值＝M*3」
            var parts = slots.Where(s => s.Slot != "接入").Select(s => s.Slot == "条件"
                ? $"if {Expr(s.From)} ?"
                : $"{s.Slot}＝{Expr(s.From)}");
            string extra = string.Join("  ", parts);
            if (extra.Length > 0) summary = extra;   // 插槽内容就是这一块的意思，别和摘要混在一起
            // 【if】那一行别再写一遍「if」，也**别把左边的执行链来源（触发时机）当成条件** ——
            // 只有「条件槽」那一支才是它的判断依据。
            if (kind == "If")
            {
                var condOnly = slots.Where(s => s.Slot == "条件").Select(s => Expr(s.From));
                summary = string.Join("  ", condOnly);
            }
        }
        return new ForgeBlock(n.Id, kind, text, summary, indent, branch, container, report);
    }

    /// <summary>把一个「内联来源」块写成一句表达式（数值块直接给公式，别的给标题）。</summary>
    private static string Expr(ForgeNodeSpec n)
    {
        string kind = n.Kind;
        if (kind == "Value")
        {
            string f = n.Get("Formula").Trim();
            return f.Length == 0 ? "（空）" : f;
        }
        // 触发时机 / 对象：给它们的中文名，别把「触发时机：打出时」整句塞进条件问句里
        if (kind == "Trigger") return ForgeTimings.Zh(n.Get("Timing"));
        // 条件：给一句能读出来的中文（「手牌数不少于 N 张」），别把「条件：…」整串塞进问句
        if (kind == "Condition")
        {
            string condId = n.Get("Cond", "None");
            var copt = EffectCatalog.Conditions.FirstOrDefault(o => string.Equals(o.Id, condId, StringComparison.Ordinal));
            string baseText = copt?.Display ?? condId;
            if (baseText.StartsWith("无条件", StringComparison.Ordinal)) return "（还没选条件）";
            // 「手牌数不少于 N 张」这种把 N 直接换掉，读起来就是一句条件，而不是「条件：… 数值 N：2」
            string body = baseText;
            string amt = n.Get("Amount").Trim();
            if (amt.Length > 0 && baseText.Contains(" N ", StringComparison.Ordinal))
                body = baseText.Replace(" N ", " " + amt + " ");
            string pid = n.Get("PowerId").Trim();
            if (pid.Length > 0) body += "（" + EffectCatalog.PowerName(pid) + "）";
            return body;
        }
        if (kind == "Unit")
        {
            string pick = n.Get("Pick", "Self");
            var opt = ForgeGraphCatalog.UnitPicks.FirstOrDefault(o => string.Equals(o.Id, pick, StringComparison.Ordinal));
            return opt?.Display ?? pick;
        }
        string s = Summary(n);
        return s.Length == 0 ? n.Title : s;
    }

    /// <summary>这一块**实际配了什么**（选中项 / 数值）。认不出来的字段跳过，绝不抛异常。</summary>
    private static string Summary(ForgeNodeSpec n)
    {
        var def = n.KindDef;
        if (def is null) return "";
        var parts = new List<string>();
        bool isCondition = n.Kind == "Condition";
        foreach (var f in def.Fields)
        {
            // 条件名（「手牌数不少于 N 张」）会进块标题，摘要里只留它的参数（数值 N / 状态）
            if (isCondition && string.Equals(f.Key, "Cond", StringComparison.Ordinal)) continue;
            if (f.Type == ForgeFieldType.Check)
            {
                if (n.GetFlag(f.Key)) parts.Add(f.Label);
                continue;
            }
            string v = (n.Get(f.Key) ?? "").Trim();
            if (v.Length == 0) continue;
            // 【触发时机】上新加的「盯哪个状态 / 到达 N 层」两栏（第 9 批 ④）：只在状态层数那三条时机上
            // 才有意义 —— 别的时机上就算老存档里存过值也别写进块摘要（免得块上平白多一句看不懂的话）
            if (n.Kind == "Trigger" && f.Key is "PowerId" or "PowerAmount"
                && !ForgeTimings.NeedsPowerWatch(n.Get("Timing"))) continue;
            // 块标题里已经写了「效果：造成伤害」这种，摘要里就别再重复一遍
            if (string.Equals(f.Key, "Effect", StringComparison.Ordinal)) continue;
            if (f.Options is { Count: > 0 })
            {
                var opt = f.Options.FirstOrDefault(o => string.Equals(o.Id, v, StringComparison.Ordinal));
                string disp = opt?.Display ?? "";
                if (disp.Length == 0) continue;                       // 认不出来的选项名就别显示原始 Id
                // 「不限」这种默认档不显示（省地方）
                if (disp.StartsWith("不限", StringComparison.Ordinal)) continue;
                parts.Add($"{f.Label}：{disp}");
            }
            else if (f.Type == ForgeFieldType.Number
                && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal num))
            {
                if (num == 0m && f.Default == "0") continue;          // 默认 0 的栏不显示
                parts.Add($"{f.Label}：{num.ToString("0.##", CultureInfo.InvariantCulture)}");
            }
            else if (f.Type == ForgeFieldType.Text || f.Type == ForgeFieldType.Formula)
            {
                parts.Add($"{f.Label}：{v}");
            }
        }
        return string.Join("  ", parts);
    }
}
