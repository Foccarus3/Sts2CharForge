using System;
using System.Collections.Generic;
using System.Linq;


namespace Sts2CharForge.Core.Profile;

/// <summary>拖到目标块的哪个位置：上面（插在它前面）/ 中间（放进它里面）/ 下面（插在它后面）。</summary>
public enum DropWhere { Before, Into, After }

/// <summary>
/// 「锻造」页积木视图用的**执行树**：把 <see cref="ForgeGraphSpec"/> 那张节点图读成一棵有序树，
/// 用户拖完之后再 <see cref="WriteBack"/> 写回图（只动 Links，节点本身是同一批对象）。
///
/// 为什么要树：节点图是「一堆块 + 一堆线」，位置由用户摆、顺序由结构推；拖拽编辑器要的是
/// 「谁在谁里面、谁在谁后面」这种**有序结构**。两者可以互相转换：
///   · 读：从【触发时机】出发，出口 0 = 执行链的下一块（同层），其余出口 = 分支 / 内容（下一层）；
///   · 写：深度优先重新分配出口下标 —— 出口 0 永远给「执行链的下一块」，
///     其余按下标从小到大给「分支 / 内容」。
///
/// ⚠ 顺序要求：读的时候必须**先把执行链走完、再递归各分支**（和写回时的分配顺序一致），
/// 否则第一块的位置会漂。
/// </summary>
public sealed class ForgeBlockTree
{
    /// <summary>一棵文档 = 一条执行链（触发时机那块是它的 Entry）。</summary>
    public sealed class Doc
    {
        public ForgeNodeSpec Entry = null!;
        public List<TreeBlock> Children = new();

        /// <summary>Entry 的积木外壳（触发时机那块本身不在 Children 里，界面 / 自检要一个 TreeBlock 才能统一处理）。</summary>
        public TreeBlock EntryBlock => new() { Node = Entry, ParentPort = 0 };
    }

    /// <summary>树里的一块：Kind + 它自己的节点（字段都在节点上，拖拽不改字段）。</summary>
    public sealed class TreeBlock
    {
        public ForgeNodeSpec Node = null!;
        public string Kind => Node.Kind;
        /// <summary>归哪个出口（写回时按它分配出口下标；0 = 执行链 / 根）。</summary>
        public int ParentPort;
        /// <summary>
        /// 这块是「**插在父块某个取值口里的**块」（【效果】的数值 / 指向对象、【状态】的归属、【条件】的 N…）。
        /// ⚠ 这时 <see cref="ParentPort"/> 是**入口**下标（不是出口），写回时的方向也不同：
        ///   是「**本块的出口 → 父块那个入口**」。
        /// 以前两种情况共用 ParentPort、方向一律按「父块的出口」写，于是【数值】→【效果】那条线
        /// 会被写成反的（`效果.1 → 数值`）—— 生成端按「谁接到数值口上」找来源，读不到就静默退回填的常数。
        /// </summary>
        public bool IsSlotChild;
        /// <summary>插槽块用的是**自己哪个出口**（【能量】有两个：能量 / 额外资源量；【指向对象】也有两个）。</summary>
        public int SlotFromPort;
        public List<TreeBlock> Children = new();
    }

    public List<Doc> Docs { get; } = new();

    /// <summary>【if】的「条件」入口下标（新版布局：入口 0 = 接入、**入口 1 = 条件**）。</summary>
    public const int IfCondPort = 1;

    /// <summary>【if】的条件槽（槽里那一整条布尔链）单独存：它不是「执行链」，写回时挂到 Cond 出口。</summary>
    public readonly Dictionary<string, TreeBlock> Slots = new(StringComparer.Ordinal);

    /// <summary>没接上任何东西的模块（用户在节点图上堆着的）：本编辑器不搬它们，写回时连它们的线一起留着。</summary>
    public readonly List<ForgeNodeSpec> Strays = new();

    /// <summary>
    /// 【且】/【或】的入口（A / B）。它们在图上落在【条件】那类模块的**出口 0** 上（从终点认不出是「入口」），
    /// 积木编辑器**不重建**这种线：只把两块条件摆在门下面给用户看，写回时原样留着。
    /// </summary>
    public readonly Dictionary<string, List<ForgeNodeSpec>> GateInputs = new(StringComparer.Ordinal);

    // ==================================================================================
    //  读：图 → 树
    // ==================================================================================

    public static ForgeBlockTree Build(ForgeGraphSpec? g)
    {
        var tree = new ForgeBlockTree();
        if (g is null || g.Nodes.Count == 0) return tree;

        var byId = new Dictionary<string, ForgeNodeSpec>(StringComparer.Ordinal);
        foreach (var n in g.Nodes)
            if (n is not null && n.Id.Length > 0) byId[n.Id] = n;

        // 连线分三类（判据**和画布 / 积木视图 / 生成端同一套**）：
        //   · 执行链 = 来源出口 → 终点「接入」（终点那个下标不带类型）；
        //   · 取值槽 = 终点那个下标是**带类型的入口**（AcceptsKind != null）且来源出口类型配得上
        //     （【效果】的数值 / 指向对象、【状态】的归属、**新版【if】的「条件」**…）；
        //   · 老存档的 PullsSource 口（老版【if】的「条件」）也归到取值槽那一类，见 slotSources。
        var next = new Dictionary<(string, int), ForgeNodeSpec>();
        var slotSources = new Dictionary<string, List<ForgeNodeSpec>>(StringComparer.Ordinal);
        // 取值槽：键 = (父块, **入口**下标)，值 = 插在那里的块 + 用的是它自己的哪个出口
        //（方向必须一起记住：写回时要写「子块的出口 → 父块的入口」）
        var typedSlots = new Dictionary<(string, int), List<(ForgeNodeSpec Node, int FromPort)>>();
        // 门的 A / B 记在实例字段 GateInputs 里（写回时也要用）
        if (Environment.GetEnvironmentVariable("FORGE_BT_DEBUG") == "1" && g.Links.Any(l => l is not null && byId.TryGetValue(l.To, out var t) && t.Kind == "And"))
        {
            Console.WriteLine("[bt] 进 Build 的图里有 And，链接数 " + g.Links.Count);
        }
        foreach (var l in g.Links)
        {
            if (l is null) continue;
            if (!byId.TryGetValue(l.From, out var from) || !byId.TryGetValue(l.To, out var to)) continue;
            if (Environment.GetEnvironmentVariable("FORGE_BT_DEBUG") == "1" && (to.Kind == "And" || from.Kind == "And"))
                Console.WriteLine($"[bt] 看线 {from.Kind}#{from.Id[..4]}.{l.FromPort}→{to.Kind}#{to.Id[..4]}.{l.ToPort}");
            // 判据统一是「终点那个下标是什么口」，**顺序不能反**：
            //   · 那个下标既是输入口又带类型、而且**不是**这个模块的出口 → 「收来源的槽」；
            //   · 那个下标是出口、而且带 PullsSource → 【if】的「条件」口；
            //   · 其余 → 执行链。
            // ⚠ 必须**先判输入口**：有的模块同一个下标两头都有定义（【且】的 A 是 InputPort(1)、
            //   Out 是 OutputPort(1)，【if】的「条件」也正好是 OutputPort(1)），反着判会把【且】的 A 认成条件口。
            // 【且】/【或】的 A / B：终点是【条件】那类模块的**出口 0**，从终点认不出来，只能从来源认 ——
            // 门自己伸出来的、下标大于 0 的线就是它的入口。这种线积木编辑器不管（图上怎么连就怎么留着）。
            if (from.Kind is "And" or "Or" && l.FromPort > 0)
            {
                if (!tree.GateInputs.TryGetValue(from.Id, out var gi)) tree.GateInputs[from.Id] = gi = new List<ForgeNodeSpec>();
                gi.Add(to);
                continue;
            }
            var inPort = to.KindDef?.InputPort(l.ToPort);
            var outPort = to.KindDef?.OutputPort(l.ToPort);
            // 「带类型的入口」= 插槽线。判据是**类型配得上**，不是「同下标没有出口」：
            // 【if】的「条件」在入口 1、而出口 1 是「否」（【状态】的「归属」在入口 0、出口 0 是「数值」…），
            // 老规矩（同下标必须有出口就不算插槽）会把它们误判成执行链。
            if (inPort is { AcceptsKind: not null }
                && ForgeGraphCatalog.TypesCompatible(from.KindDef?.OutputPort(l.FromPort)?.AcceptsKind, inPort.AcceptsKind))
            {
                if (!typedSlots.TryGetValue((to.Id, l.ToPort), out var ts))
                    typedSlots[(to.Id, l.ToPort)] = ts = new List<(ForgeNodeSpec, int)>();
                ts.Add((from, l.FromPort));
                continue;
            }
            var srcOutPort = from.KindDef?.OutputPort(l.FromPort);
            if (outPort is { PullsSource: true } && srcOutPort is { AcceptsKind: not null }
                && string.Equals(srcOutPort.AcceptsKind, outPort.AcceptsKind, StringComparison.Ordinal))
            {
                if (!slotSources.TryGetValue(to.Id, out var ss)) slotSources[to.Id] = ss = new List<ForgeNodeSpec>();
                ss.Add(from);
                continue;
            }
            next.TryAdd((from.Id, l.FromPort), to);         // 一个出口只留**第一条**线，和生成端的走法一致
        }

        // 归属：只有「明确归到某个出口」的子块才需要记出口（【if】的「是/否」、【效果】的数值槽…）。
        // 「执行链上排在自己后面的那些块」不记 —— 它们一律嵌套到上一块的出口 0（见 EmitChildPorts），
        // 这么写回去的图出口下标可能和原来不一样（原来是平铺的会变成嵌套），但**执行顺序一模一样**。
        var parentPort = new Dictionary<string, int>(StringComparer.Ordinal);
        var slotChild = new HashSet<string>(StringComparer.Ordinal);          // 插在父块「取值口」里的
        var slotFromPort = new Dictionary<string, int>(StringComparer.Ordinal); // 它用的是自己哪个出口
        void Own(string id, int port, bool isSlot = false, int fromPort = 0)
        {
            if (!parentPort.ContainsKey(id)) parentPort[id] = port;
            if (!isSlot) return;
            slotChild.Add(id);
            if (!slotFromPort.ContainsKey(id)) slotFromPort[id] = fromPort;
        }
        foreach (var (key, to) in next) Own(to.Id, key.Item2);
        foreach (var ((toId, port), srcs) in typedSlots)
        {
            // ⚠【if】的「条件」是**入口 1**，而它的「否」分支是**出口 1**：同一个下标两头都有定义，
            // 所以条件链**不能**按 ParentPort 挂成子块（会和「否」撞车）—— 它进 Slots（见下）。
            if (byId.TryGetValue(toId, out var owner) && owner.Kind == "If" && port == IfCondPort) continue;
            foreach (var (s, fp) in srcs) Own(s.Id, port, isSlot: true, fromPort: fp);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var path = new HashSet<string>(StringComparer.Ordinal);      // 正在建的这一条链（防成环）
        var slotClaimed = new HashSet<(string, int)>();              // 已经用掉的「槽」连线（一条槽只挂一块）

        // 一个节点 → 一块。子块分两种：
        //   · 带类型的槽 / 【if】的「是、否」（ParentPort > 0）= 明确归到那个出口的子块；
        //   · 出口 0 的执行链 = **只有一个子块**，它在 Children 里的下标就是 0，再往下一路嵌进去
        //     （图里「一个出口只能接一条线」，所以「接在后面的那些块」读出来就是一层套一层的链）。
        // ⚠ 必须整棵一次建好：条件链（【条件】→【且】）里的块自己也有条件槽，分两趟建会把槽丢掉。
        // ⚠ 不能拿 seen 当「已经挂好了」用 —— 一个节点可以合法地被挂两次（【且】的 A 和 B 是同一块），
        //   真正要拦的只有「自己套自己」那种环，所以只用 path 拦截。
        TreeBlock Make(ForgeNodeSpec head)
        {
            var block = new TreeBlock
            {
                Node = head,
                ParentPort = parentPort.TryGetValue(head.Id, out int pp) ? pp : 0,
                IsSlotChild = slotChild.Contains(head.Id),
                SlotFromPort = slotFromPort.TryGetValue(head.Id, out int fp) ? fp : 0,
            };
            seen.Add(head.Id);
            if (!path.Add(head.Id)) return block;                    // 环：不再往下走
            // 1) 带类型的槽（【效果】的数值 / 对象口）：键是 head，值是要挂的块。
            //    ⚠【且】/【或】的 A / B 不在这里 —— 它们落在【条件】的出口 0 上（图上看不出是「入口」），
            //      积木编辑器**不管**那种线（当它不存在，见 GateInputs），免得把整棵条件链绕成一团。
            var slotPorts = new SortedDictionary<int, List<ForgeNodeSpec>>();
            var slotClaimed = new HashSet<(string, int)>();
            void Slot(int port, ForgeNodeSpec node)
            {
                if (!slotClaimed.Add((node.Id, port))) return;
                if (!slotPorts.TryGetValue(port, out var l)) slotPorts[port] = l = new List<ForgeNodeSpec>();
                l.Add(node);
            }
            foreach (var ((toId, port), srcs) in typedSlots.Where(kv => kv.Key.Item1 == head.Id))
            {
                // 【if】的「条件」（入口 1）不是子块，是 Slots 里的那条布尔链（下标和「否」出口撞车）
                if (head.Kind == "If" && port == IfCondPort) continue;
                foreach (var (s, _) in srcs) Slot(port, s);
            }
            foreach (var (port, nodes) in slotPorts)
                foreach (var n in nodes)
                {
                    if (seen.Contains(n.Id)) continue;                       // 已经挂过了：别再挂一次
                    var kid = Make(n);
                    // 【且】/【或】的 A / B：ParentPort 已经是**入口**下标（0/1），IsSlotChild 也标好了
                    //（写回时不重建那种线，原样留着 —— 见 WriteBack 里的 kept）
                    block.Children.Add(kid);
                }
            // 【且】/【或】的 A / B：只摆在积木里看得见，写回时**原样留着**（编辑器不管这条线）
            if (tree.GateInputs.TryGetValue(head.Id, out var gateKids))
                foreach (var g in gateKids)
                    if (!seen.Contains(g.Id)) block.Children.Add(Make(g));
            // 2) 出口 0 的执行链 = 直接子块，整条链**平铺成兄弟**（写回时一路接下去）。
            //    ⚠ 每一步都要问 seen：链上绕回来（已经挂过的节点）就停，不然同一个节点会被挂两次
            //      （写回时会写出重复的块）。
            //    ⚠【if】**没有**「出口 0 的执行链」—— 新版布局里它的出口 0 就是「是」那一支（见第 3 步），
            //      在这里先顺着出口 0 走会把「是」当成执行链的下一块（那样两支就只剩「否」了）。
            if (head.Kind != "If")
            {
                var kid = next.TryGetValue((head.Id, 0), out var first) && !seen.Contains(first.Id) ? Make(first) : null;
                if (kid is not null)
                {
                    kid.ParentPort = 0;
                    block.Children.Insert(0, kid);
                    var prev = kid;
                    while (next.TryGetValue((prev.Node.Id, 0), out var more) && !seen.Contains(more.Id))
                    {
                        var nxt = Make(more);
                        nxt.ParentPort = 0;
                        block.Children.Insert(block.Children.IndexOf(prev) + 1, nxt);
                        prev = nxt;
                    }
                }
            }
            // 3) 分支出口 = 直接子块，出口下标原样留着：
            //    别的块从出口 1 起（出口 0 是执行链，上面已经走过了）；
            //    【if】从出口 **0** 起（0 = 是、1 = 否）—— 它的两支都是分支，没有执行链出口。
            //    ⚠【且】/【或】没有分支出口（它们的 A / B 已由 GateInputs 挂好，而且不参与写回）
            var def = head.KindDef;
            int outs = def?.Outputs.Count ?? 0;
            int firstBranch = head.Kind == "If" ? 0 : 1;
            for (int i = firstBranch; i < outs && head.Kind is not ("And" or "Or"); i++)
                if (next.TryGetValue((head.Id, i), out var child) && !seen.Contains(child.Id))
                {
                    var kid = Make(child);
                    kid.ParentPort = i;
                    block.Children.Add(kid);
                }
            path.Remove(head.Id);
            if (Environment.GetEnvironmentVariable("FORGE_BT_DEBUG") == "1")
                Console.WriteLine($"[bt] Make {head.Kind}#{head.Id[..4]} → 子 {block.Children.Count}：" +
                    string.Join(",", block.Children.Select(c => c.Node.Kind + "#" + c.Node.Id[..4] + "p" + c.ParentPort)));
            return block;
        }

        // 1) 每个【触发时机】带一条执行链
        foreach (var t in g.Nodes.Where(n => n.Kind == "Trigger"))
        {
            var doc = new Doc { Entry = t };
            seen.Add(t.Id);
            if (next.TryGetValue((t.Id, 0), out var first)) doc.Children.Add(Make(first));
            tree.Docs.Add(doc);
        }

        // 2) 【if】的「条件」槽（那条布尔链：条件 / 且 / 或 / 关系符）。
        //    必须在「悬空」那一步之前做 —— 条件链上的块只连在槽里，别把它们当成悬空模块。
        //    新版布局里条件接在**入口 1**（带类型 Bool）→ 落在 typedSlots 里；
        //    老存档（还没迁移的 PullsSource 口）落在 slotSources 里，两条都认。
        foreach (var n in g.Nodes.Where(x => x.Kind == "If"))
        {
            var srcs = typedSlots.TryGetValue((n.Id, IfCondPort), out var t) ? t.Select(x => x.Node).ToList() : null;
            if (srcs is null && slotSources.TryGetValue(n.Id, out var s)) srcs = s;
            if (srcs is null) continue;
            var first = srcs.FirstOrDefault(s => !seen.Contains(s.Id));
            if (first is null) continue;
            tree.Slots[n.Id] = Make(first);
        }

        // 3) 真的没接上的模块（线断了 / 只堆在布上）：**原样留着**（不搬进 Docs，也不改它的连线），
        //    不然「切过去切回来」会把它们吃掉 —— WriteBack 会重建 Links，这些块的线不在树里。
        foreach (var n in g.Nodes)
        {
            if (seen.Contains(n.Id) || n.Kind == "Trigger") continue;
            tree.Strays.Add(n);
        }
        return tree;
    }

    /// <summary>
    /// 这条线是不是「插槽连线」（终点那个下标是收来源的槽）。
    /// 新版布局下判据是**类型配得上**（见下面的四参重载）；这个两参版本只认老存档那种
    /// 「从【if】的「条件」出口往外拖」的 PullsSource 口（老图 / 没迁移的图）。
    /// </summary>
    public static bool IsSlotLink(ForgeNodeSpec to, int toPort) =>
        to.KindDef?.OutputPort(toPort) is { PullsSource: true };

    /// <summary>终点的那个下标到底算「插槽口」还是「执行链口」（判据 = 来源出口类型配不配得上入口类型）。</summary>
    public static bool IsSlotLink(ForgeNodeSpec from, int fromPort, ForgeNodeSpec to, int toPort)
    {
        var src = from.KindDef?.OutputPort(fromPort);
        var inPort = to.KindDef?.InputPort(toPort);
        if (inPort is { AcceptsKind: not null }
            && ForgeGraphCatalog.TypesCompatible(src?.AcceptsKind, inPort.AcceptsKind)) return true;
        var slotPort = to.KindDef?.OutputPort(toPort);
        if (slotPort is not { PullsSource: true }) return false;
        return src is { AcceptsKind: not null }
            && string.Equals(src.AcceptsKind, slotPort.AcceptsKind, StringComparison.Ordinal);
    }

    // ==================================================================================
    //  写：树 → 图（只重建 Links；节点对象原样保留，字段 / 位置不动）
    // ==================================================================================

    /// <summary>把树写回图：重新分配所有连线。写完 <c>graph.Links</c> 就是这棵树的形状。</summary>
    public void WriteBack(ForgeGraphSpec g)
    {
        var strayIds = Strays.Where(n => n is not null).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        // 本编辑器不管的线，原样留着：
        //   · 悬空模块（用户堆在布上的）身上的线；
        //   · 【且】/【或】的 A / B —— 不管方向（`门.1 → 条件` 还是 `条件 → 门.A`）都留着不重建。
        var gateIds = g.Nodes.Where(n => n is not null && n.Kind is "And" or "Or")
            .Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var kept = g.Links.Where(l => l is not null
            && (strayIds.Contains(l.From) || strayIds.Contains(l.To)
                || (GateInputs.ContainsKey(l.From) && l.FromPort > 0)
                || gateIds.Contains(l.To))).ToList();
        // 同一个出口只留一条线（树里条件块可能被两个 if 共用，别写重复线）
        var used = new HashSet<(string, int, string, int)>();
        void Link(string from, int fromPort, string to, int toPort)
        {
            if (!used.Add((from, fromPort, to, toPort))) return;
            g.Links.Add(new ForgeLinkSpec { From = from, To = to, FromPort = fromPort, ToPort = toPort });
        }
        // 条件槽那条线是「条件口」（【if】的入口 1），和「是」出口（0 号口）**不同下标**了，
        // 但还是单独记账：条件块自己的出口 0 后面可能接着【且】/【或】，别按「(来源, 出口) 唯一」去重。
        var slotLinked = new HashSet<string>(StringComparer.Ordinal);
        void SlotLink(string from, string to)
        {
            if (!slotLinked.Add(from)) return;
            int toPort = g.Node(to)?.Kind == "If" ? IfCondPort : 0;
            g.Links.Add(new ForgeLinkSpec { From = from, To = to, FromPort = 0, ToPort = toPort });
        }
        g.Links.Clear();
        g.Links.AddRange(kept);
        foreach (var l in kept) used.Add((l.From, l.FromPort, l.To, l.ToPort));

        // 根链：从【触发时机】开始，接上它出口 0 的那一块，再顺着「上一块 → 出口 0 → 下一块」接下去。
        foreach (var doc in Docs)
        {
            ForgeNodeSpec? prev = null;
            foreach (var kid in doc.Children)
            {
                Link(prev?.Id ?? doc.Entry.Id, 0, kid.Node.Id, 0);
                EmitBlock(kid, Link);
                prev = kid.Node;
            }
        }
        // 【if】的条件槽：条件链的头接「条件」入口（新版 = 入口 1；条件块的出口是 Bool，
        // 接入口要的是执行块 ⇒ 读的时候不会被当成执行链）
        foreach (var (ifId, slot) in Slots)
        {
            var linkSlot = Link;
            SlotLink(slot.Node.Id, ifId);
            EmitBlock(slot, linkSlot);
        }
    }

    /// <summary>
    /// 把**这一块自己的**子块写回去（不含「谁接到它」那条线 —— 那条由它的父块 / 根链写）：
    ///   · 出口 0 的执行链：Children 里 ParentPort &lt;= 0 的那一块，接在出口 0 上，再递归；
    ///   · 分支出口（【if】的「是 / 否」）：`父块.出口 → 子块.0`；
    ///   · 取值槽（【效果】的数值 / 指向对象…）：**方向相反** —— `子块.出口 → 父块.那个入口`。
    /// </summary>
    private void EmitBlock(TreeBlock block, Action<string, int, string, int> link)
    {
        // 【且】/【或】的 A / B 不重建（见 WriteBack 里的 kept）—— 那些块只是摆在这里给用户看
        if (block.Kind is "And" or "Or") return;
        // 分支出口（【if】的「是、否」）：先写；它们占自己的出口号。
        var taken = new HashSet<int>();
        foreach (var kid in block.Children.Where(c => !c.IsSlotChild && c.ParentPort > 0))
        {
            if (!taken.Add(kid.ParentPort)) continue;
            link(block.Node.Id, kid.ParentPort, kid.Node.Id, 0);
            EmitBlock(kid, link);
        }
        // 取值槽：**从子块的出口接到父块这个入口上**（方向反了生成端就读不到 —— 老版就是这么写反的）
        foreach (var kid in block.Children.Where(c => c.IsSlotChild))
        {
            link(kid.Node.Id, kid.SlotFromPort, block.Node.Id, kid.ParentPort);
            EmitBlock(kid, link);
        }
        // 出口 0 的执行链：图里一个出口只能接一条线，所以链上的块要**一个接一个嵌套**着写：
        // 第一个接在本块的出口 0 上，后面每一块接在上一块的出口 0 上（下标要让开已经被占的）。
        ForgeNodeSpec? prev = null;
        foreach (var kid in block.Children.Where(c => !c.IsSlotChild && c.ParentPort <= 0))
        {
            int port = 0;
            while (taken.Contains(port)) port++;
            taken.Add(port);
            link(prev?.Id ?? block.Node.Id, port, kid.Node.Id, 0);
            EmitBlock(kid, link);
            prev = kid.Node;
        }
    }

    // ==================================================================================
    //  编辑操作（拖拽 / 加块 / 删块都走这几个）
    // ==================================================================================

    /// <summary>找一个块的父容器（父块 + 它在父容器里的位置）。根链的父是 null 且带上 Doc。</summary>
    public (TreeBlock? Parent, List<TreeBlock> List, int Index, Doc? Doc) Locate(TreeBlock block)
    {
        foreach (var doc in Docs)
        {
            int i = doc.Children.IndexOf(block);
            if (i >= 0) return (null, doc.Children, i, doc);
            var found = LocateIn(doc.Children, block);
            if (found is not null) return found.Value;
        }
        foreach (var slot in Slots.Values)
        {
            int i = slot.Children.IndexOf(block);
            if (i >= 0) return (slot, slot.Children, i, null);
            var found = LocateIn(slot.Children, block);
            if (found is not null) return found.Value;
        }
        return (null, new List<TreeBlock>(), -1, null);
    }

    private static (TreeBlock? Parent, List<TreeBlock> List, int Index, Doc? Doc)? LocateIn(List<TreeBlock> list, TreeBlock block)
    {
        foreach (var b in list)
        {
            int i = b.Children.IndexOf(block);
            if (i >= 0) return (b, b.Children, i, null);
            var found = LocateIn(b.Children, block);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>
    /// 这块能不能当容器（拖到它中间能「放进去」）：
    ///   · 【if】= 放进「是 / 否」两支；
    ///   · 有「取值口」的模块（【效果】的数值 / 指向对象、【状态】的归属、【数值】的接入…）= 放进那个槽。
    /// ⚠【且】/【或】不算 —— 它们的 A / B 线编辑器不重建（放进去会写不出来）；
    ///   【对象】/【指向对象】也没有「取值口」，所以按「放不进去」处理（以前会写出一条指向不存在出口的线）。
    /// </summary>
    public static bool CanHoldChildren(TreeBlock b) =>
        b.Kind == "If" || (b.Kind is not ("And" or "Or")
            && (b.Node.KindDef?.Inputs.Any(p => p.AcceptsKind is not null) ?? false));

    /// <summary>上一次拖动为什么没成（界面拿它给用户一句话）。</summary>
    public string? LastRefusal { get; private set; }

    /// <summary>把 <paramref name="moving"/>（连着它的子树）插到 <paramref name="target"/> 之前 / 之后 / 里面。</summary>
    public bool Move(TreeBlock moving, TreeBlock target, DropWhere where)
    {
        LastRefusal = null;
        bool dbg = Environment.GetEnvironmentVariable("FORGE_BT_DEBUG") == "1";
        if (ReferenceEquals(moving, target)) { LastRefusal = "不能拖到自己身上。"; if (dbg) Console.WriteLine("[mv] 同一个块"); return false; }
        // ⚠ 只拦「把父块拖进自己的子树」：那是真会绕成环的。
        //   「把一块拖进自己所在的那个容器」**不拦** —— 那正是「从 if 里面拖出来 / 换一支」的常用操作。
        if (IsAncestor(moving, target))
        {
            LastRefusal = $"【{moving.Node.Title}】里现在就装着【{target.Node.Title}】，不能放进自己里面（会绕成环）。";
            if (dbg) Console.WriteLine($"[mv] {moving.Kind} 是 {target.Kind} 的祖先 → 拒绝");
            return false;
        }
        var from = Locate(moving);
        if (from.Index < 0) { LastRefusal = "找不到这一块（界面和树不同步，切一下视图再试）。"; if (dbg) Console.WriteLine($"[mv] 定位不到 {moving.Kind}"); return false; }
        if (dbg) Console.WriteLine($"[mv] {moving.Kind} 从 {from.Index} 挪到 {target.Kind} 的 {where}");
        from.List.RemoveAt(from.Index);

        if (where == DropWhere.Into)
        {
            if (!CanHoldChildren(target))
            {
                from.List.Insert(Math.Min(from.Index, from.List.Count), moving);   // 放不进去 → 原样放回
                LastRefusal = $"【{target.Node.Title}】里面放不了别的模块 —— 它没有能插「取值块」的口"
                    + "（要接在它后面就拖到它的下边缘）。";
                return false;
            }
            if (!AddInto(target, moving))
            {
                from.List.Insert(Math.Min(from.Index, from.List.Count), moving);   // 类型对不上 → 原样放回
                return false;
            }
            return true;
        }
        // ⚠ 目标位置要**摘掉之后**再算：同一张列表里，被摘的块如果在目标前面，后面所有下标都会往前挪一格
        var to = Locate(target);
        if (to.Index < 0) { from.List.Insert(Math.Min(from.Index, from.List.Count), moving); return false; }
        if (where == DropWhere.Before) InsertBefore(to.List, to.Index, target, moving);
        else InsertAfter(target, moving);
        return true;
    }

    /// <summary>
    /// 把 <paramref name="moving"/> 插到 <paramref name="target"/> **后面**（同一条链的下一块）。
    /// 加块和拖动共用这一条规则，保证两边树形一致。
    /// </summary>
    public static void InsertAfter(TreeBlock target, TreeBlock moving)
    {
        moving.ParentPort = 0;
        LastOfBranch(target).Children.Insert(0, moving);
    }

    /// <summary>
    /// 把 <paramref name="moving"/> 插到 <paramref name="target"/> **前面**（同一条链的上一块）。
    /// 插在第一位就挂到父块上；否则接到「目标前一块」那条支的尾巴上（不能并排 —— 同一条链上的块是嵌着的）。
    /// </summary>
    private static void InsertBefore(List<TreeBlock> list, int index, TreeBlock target, TreeBlock moving)
    {
        if (index > 0)
        {
            moving.ParentPort = 0;
            LastOfBranch(list[index - 1]).Children.Insert(0, moving);
            return;
        }
        moving.ParentPort = target.ParentPort;
        list.Insert(0, moving);
    }

    /// <summary>
    /// 这一块所在这条支的**最后一块**：一路顺着出口 0 接下去（同一条链后面的块都是嵌着的）。
    /// 「插在某块后面」就接到这条支的尾巴上，不会和它抢出口。
    /// </summary>
    private static TreeBlock LastOfBranch(TreeBlock block)
    {
        var last = block;
        var next = last.Children.FirstOrDefault(c => !c.IsSlotChild && c.ParentPort <= 0);
        while (next is not null)
        {
            last = next;
            next = last.Children.FirstOrDefault(c => !c.IsSlotChild && c.ParentPort <= 0);
        }
        return last;
    }

    /// <summary>
    /// 把一块放进容器里：
    ///   · 【if】→ 占**下一个空的分支**（先「是」再「否」；两支都满了并到「是」支那条链的尾巴上）；
    ///   · 别的容器（【效果】/【状态】/【数值】…）→ 占它**下一个空的取值口**（还要类型配得上）；
    ///     没有能放的槽就**不放**（返回 false，界面会说一句）—— 以前会写出一条指向不存在出口的线，
    ///     那块看着在、实际什么都不做（静默失效）。
    /// </summary>
    private bool AddInto(TreeBlock container, TreeBlock moving)
    {
        if (container.Kind != "If")
        {
            var def = container.Node.KindDef;
            var used = container.Children.Where(c => c.IsSlotChild).Select(c => c.ParentPort).ToHashSet();
            for (int i = 0; def is not null && i < def.Inputs.Count; i++)
            {
                var inPort = def.Inputs[i];
                if (inPort.AcceptsKind is null || used.Contains(i)) continue;
                // 这块得**出得了**那个类型的东西（拿它自己的某个出口去配）
                int fromPort = -1;
                int outs = moving.Node.KindDef?.Outputs.Count ?? 0;
                for (int j = 0; j < outs; j++)
                {
                    if (!ForgeGraphCatalog.TypesCompatible(moving.Node.KindDef?.OutputPort(j)?.AcceptsKind, inPort.AcceptsKind)) continue;
                    fromPort = j;
                    break;
                }
                if (fromPort < 0) continue;
                moving.ParentPort = i;
                moving.IsSlotChild = true;
                moving.SlotFromPort = fromPort;
                container.Children.Add(moving);
                return true;
            }
            LastRefusal = $"【{container.Node.Title}】的取值口里放不下【{moving.Node.Title}】"
                + "（要么槽都占着，要么类型对不上：数值口收 【数值】/【状态】/【能量】，条件口收 【条件】/【且】/【或】/【关系符】）。";
            return false;
        }
        int port = NextFreeChildPort(container);
        if (port < 0)
        {
            // 【if】两支都满了：并到「是」支那条链的尾巴上（不留洞、也不和别的块抢出口）
            var yes = container.Children.FirstOrDefault(c => !c.IsSlotChild && c.ParentPort == 0);
            if (yes is not null)
            {
                moving.ParentPort = 0;
                moving.IsSlotChild = false;
                LastOfBranch(yes).Children.Insert(0, moving);
                return true;
            }
            port = 0;
        }
        moving.ParentPort = port;
        moving.IsSlotChild = false;
        container.Children.Add(moving);
        return true;
    }

    /// <summary>
    /// 自检 / 排错用：检查「同一张图里的每个节点最多在树上出现一次」。
    /// 出现两次就说明 Move / AddInto 把一块挂到了两处（写回时会写出重复的块）。
    /// 返回重复出现的节点 Id（空 = 干净）。
    /// </summary>
    public List<string> DuplicatedNodes()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dup = new List<string>();
        foreach (var b in All())
            if (!seen.Add(b.Node.Id)) dup.Add(b.Node.Id);
        return dup;
    }

    /// <summary>这块的子树里有没有 <paramref name="maybeChild"/>（拖拽时防止把父块塞进自己里面）。</summary>
    public static bool IsAncestor(TreeBlock maybeParent, TreeBlock maybeChild)
    {
        foreach (var c in maybeParent.Children)
        {
            if (ReferenceEquals(c, maybeChild)) return true;
            if (IsAncestor(c, maybeChild)) return true;
        }
        return false;
    }


    /// <summary>
    /// 【if】里下一个能用的**分支**出口：0（是）/ 1（否）；两个都占了返回 -1
    /// （调用方会把它并到「是」支那条链的尾巴上）。
    /// </summary>
    public static int NextFreeChildPort(TreeBlock container)
    {
        if (container.Kind == "If")
        {
            var usedIf = container.Children.Where(c => !c.IsSlotChild).Select(c => c.ParentPort).ToHashSet();
            if (!usedIf.Contains(0)) return 0;      // 新版【if】：出口 0 = 是
            if (!usedIf.Contains(1)) return 1;      // 出口 1 = 否
            return -1;                              // 两支都满了
        }
        return -1;                                  // 别的容器看「取值口」，在 AddInto 里算
    }

    /// <summary>
    /// 删掉一块：它「执行链上排在自己后面的」子块接回同一个位置（不留洞），
    /// 「归到某个出口 / 取值口」的子块（【if】的两支、【效果】的数值槽…）连同它们的子树一起被摘掉 ——
    /// 它们本来就是它的内容。
    /// 返回**所有被摘掉、需要从图里删掉的块**（含自己），界面据此清节点；接回去的那些不在里面。
    /// </summary>
    public List<TreeBlock> Remove(TreeBlock block)
    {
        var loc = Locate(block);
        if (loc.Index < 0) return new List<TreeBlock>();
        loc.List.RemoveAt(loc.Index);
        var dropped = new List<TreeBlock> { block };
        // 执行链子块（既不是插槽块、ParentPort 又是 0）接回同一个位置
        foreach (var c in block.Children.Where(c => !c.IsSlotChild && c.ParentPort <= 0).ToList())
        {
            c.ParentPort = block.ParentPort;
            loc.List.Insert(Math.Min(loc.Index, loc.List.Count), c);
            loc.Index++;
        }
        // 分支 / 取值口的子块跟着被删的父一起没
        foreach (var c in block.Children.Where(c => c.IsSlotChild || c.ParentPort > 0))
            dropped.AddRange(Flatten(c));
        return dropped;
    }

    private static IEnumerable<TreeBlock> Flatten(TreeBlock b)
    {
        yield return b;
        foreach (var c in b.Children) foreach (var x in Flatten(c)) yield return x;
    }

    /// <summary>所有块（深度优先，用来查重 / 命名 / 自检）。顺序 = 界面上从上往下看到的顺序。</summary>
    public IEnumerable<TreeBlock> All()
    {
        foreach (var doc in Docs)
        {
            yield return doc.EntryBlock;
            foreach (var b in Walk(doc.Children)) yield return b;
        }
        foreach (var slot in Slots.Values) foreach (var b in Walk(new[] { slot })) yield return b;
    }

    private static IEnumerable<TreeBlock> Walk(IEnumerable<TreeBlock> list)
    {
        foreach (var b in list)
        {
            yield return b;
            foreach (var c in Walk(b.Children)) yield return c;
        }
    }

    /// <summary>深拷贝一棵子树（拖拽失败要放回原处时用不到；这里给「复制」留的入口）。</summary>
    public static TreeBlock CloneSubtree(TreeBlock b) => new()
    {
        Node = b.Node,
        ParentPort = b.ParentPort,
        Children = b.Children.Select(CloneSubtree).ToList(),
    };
}
