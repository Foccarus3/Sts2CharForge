using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 把锻造页那张**节点图**翻成生成出来的模组里的 C# 代码。
///
/// 生成的基本形状（以卡牌为例）：
/// <code>
/// private async Task ForgeGraphOnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
/// {
///     Player __fgPlayer = base.Owner;                              // 执行点先算好「自己」
///     Creature __fgSelf = __fgPlayer.Creature;
///     Creature? __fgTarget = cardPlay.Target;                      // 以及「这一支链当前的目标」
///     decimal __pM = (decimal)__fgSelf.MaxHp;                      // 公式里用到的自定义参数（运行时现算）
///     decimal __X = ForgeFormulaRuntime.Energy(__fgPlayer) - 0m;   // 当前所有能量（减掉打出消耗）
///     decimal __Y = ForgeFormulaRuntime.Stars(__fgPlayer) - 0m;    // 当前所有额外资源量
///     decimal __fgXXXXXX = ((__pM) * (3m));                        // 【数值】模块
///     await DamageCmd.Attack((__fgXXXXXX)).FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(choiceContext);
/// }
/// </code>
///
/// 关键约定：
///   · <c>__fgPlayer</c> / <c>__fgSelf</c> / <c>__fgTarget</c> / <c>__p&lt;字母&gt;</c> 是**每个执行点先算一次的局部变量**，
///     公式和参数表达式都只引用它们，不会到处散落「再取一次」的写法；
///   · 每个模块的代码写在一个 <c>// ---- fg:&lt;节点Id&gt; ----</c> 注释下面，方便排查；
///   · **图是空的 = 这段代码一个字符都不生成**（默认档逐字不变的红线）。
/// </summary>
public static class ForgeGraphGen
{
    /// <summary>运行时辅助类的文件/类名（只在「有锻造图」时才写进工程）。</summary>
    public const string RuntimeFile = "ForgeFormulaRuntime";

    // ⚠ V0.2.130 用户要求删掉整块「卡面显示牌堆张数（{DrawPile}/{DiscardPile}/{ExhaustPile}）」：
    //   原来这里有 PileCountFile / NeedsPileCountVars / PileCountSource / ForgePileCountVar
    //   和 CardSpec.ShowPileCounts，全部一起删了（生成端那三行 CanonicalVars 声明、
    //   「填入参数…」菜单里那三个占位符、--uicheck 里那 9 条断言同批删掉）。
    //   老存档里勾过这个开关的：那个字段读进来会被 System.Text.Json 直接忽略（不报错），
    //   再次保存时自然消失 —— 见 docs\QUEUE.md 第 17 批的口径说明。

    /// <summary>「当遗物被点击时」那个 Harmony 补丁的文件/类名。</summary>
    public const string RelicClickPatchFile = "ForgeRelicClickPatch";
    public const string RelicClickPatchClass = "ForgeRelicClickPatch";

    // ==================================================================================
    //  图 → 执行点
    // ==================================================================================

    /// <summary>一个执行点的计划：一个触发时机模块 + 它后面那条执行链的入口。</summary>
    private sealed record HookPlan(ForgeNodeSpec Trigger, ForgeTimings.TimingOption Timing);

    /// <summary>一个执行点的生成上下文。</summary>
    /// <param name="CtxFrom">
    /// 这个执行点里的 <c>choiceContext</c> 从哪来（第 9 批 ④ 合并触发时机后新增）：
    ///   · <c>Param</c>    —— 本体的钩子签名里就有那个参数（卡牌 / 药水 / 遗物里带 context 的那些）；
    ///   · <c>Blocking</c> —— 签名里没有，但按本体的做法能造一个**阻塞式**的（「获得时」AfterObtained）；
    ///   · <c>Hook</c>     —— 签名里没有，但在战斗里 → 照本体 Hook.cs 造 <c>HookPlayerChoiceContext</c>
    ///                       （每消耗能量 / 每消耗额外资源量 / 生成卡牌 / 失去生命 / 获得格挡 /
    ///                        敌人回合开始 / 敌人获得格挡 / 状态被移除，以及**嵌进遗物自己钩子**的那种图）；
    ///   · <c>None</c>     —— 造不出来（战斗胜利 / 战斗结束 / 获得金币 / 遗物被点击）：
    ///                       需要 context 的效果会传 <c>null!</c>，校验器对这种组合**报错**拦住（不静默生成坏代码）。
    /// </param>
    /// <param name="NoHookParams">
    /// V0.2.169：这一块【触发时机】**嵌进卡牌自己的钩子**里（和遗物那条链同一个坑）—— 生成的是
    /// <c>public async Task ForgeGraphOnXxx(PlayerChoiceContext choiceContext)</c> 这个**普通方法**，
    /// 拿不到目标钩子那套参数（<c>card</c> / <c>player</c> / <c>retainedCards</c>…），
    /// 所以：进门守卫不写（外层那个钩子里已经判过同一条）、<c>cardPlay</c> 用占位 null。
    /// </param>
    private sealed record EmitCtx(string Scope, string CardPlayExpr, string CardSelfExpr, bool Upgraded = false,
        string CtxFrom = "Param", bool AllowCardReward = false, bool HasRoom = false, int PlanIdx = 0,
        bool NoHookParams = false)
    {
        /// <summary>效果代码里要传的那个选择上下文：没有上下文（None）时写 <c>null!</c>（运行时本体自己会拦）。</summary>
        public string ChoiceVar => CtxFrom == "None" ? "null!" : "choiceContext";

        /// <summary>要不要在这个方法体里自己造一个 choiceContext（Blocking / Hook）。</summary>
        public bool NeedsCtxBootstrap => CtxFrom is "Blocking" or "Hook";

        /// <summary>
        /// 「仅生效 N 次」那个计数字段名。V0.2.168：一块布上有**多块**【触发时机】时，每块各有一个计数器
        /// （名字加 <c>P2 / P3…</c> 后缀），第一块保持老名字 —— 老图（只有一块）生成的文本一个字符都不变。
        /// </summary>
        public string UsesVar => "__fgUses" + (PlanIdx > 0 ? "P" + (PlanIdx + 1) : "") + (Upgraded ? "Up" : "");

        /// <summary>
        /// V0.2.170：这个执行点真的拿得到 <c>cardPlay</c> 参数吗（= <see cref="ForgeGraphGen.EmitHook"/>
        /// 里算出来的 <c>hookHasPlay</c>）。「斩杀」条件的赋值要用「打出时选中的那个目标」，
        /// 拿不到 cardPlay 的时机只能写 false（占位 <c>CardPlay? cardPlay = null;</c> 上取 Target 会 CS8602，
        /// 而且斩杀会误成立）。
        /// </summary>
        public bool RealCardPlay { get; init; } = true;

        /// <summary>
        /// V0.2.194：这个执行点就是**「卡牌被打出时」（OnPlay）**本身。
        /// 「本张牌这一次打出造成的伤害」（ThisCardDamage）只在这里记账 —— 别的卡牌时机
        /// （被消耗 / 被丢弃 / 被自动打出…）跑的时候 OnPlay 那次清零早就过去了，记进去会累加到上一次打出上。
        /// </summary>
        public bool IsCardPlay { get; init; }
    }

    /// <summary>这个执行点的钩子签名里是不是带着 <c>CombatRoom room</c>（只有「战斗胜利时」）。</summary>
    private static bool HasRoomHook(EmitCtx ctx) => ctx.HasRoom;

    /// <summary>一次代码生成的状态（递归时共用：局部变量别重复声明、别顺着环无限递归）。</summary>
    private sealed class GenState
    {
        /// <summary>
        /// 已经算过的【数值】/【状态】/【能量】：键 = 模块 Id + 出口序号，值 = **声明时所在的作用域**。
        /// 为什么要记作用域（用户实测报的 CS0103）：
        /// 声明是**就地写进当前这段语句流**的，而 <c>if</c> 真支 / 假支是两个平级的大括号块。
        /// 同一个模块先被真支里的效果取用（<c>decimal __fgXXXX = …;</c> 落在真支里），
        /// 假支再取用时就只能拿到缓存里那个**引用**——而真支那个局部变量出了大括号就没了 →
        /// 生成 <c>else { …Attack(__fgXXXX)… }</c> 直接 CS0103「当前上下文中不存在名称」。
        /// 所以：缓存命中时还要看「声明处是不是当前作用域的祖先」；不是就在这一段里**重新声明一次**
        /// （同名落在兄弟分支 / 外层是合法 C#：兄弟块互不可见，外层那个也已经出作用域了）。
        /// </summary>
        public readonly Dictionary<string, string> ValueScope = new(StringComparer.Ordinal);
        public readonly HashSet<string> NumEmitted = new(StringComparer.Ordinal);     // 已经声明过的「数字栏公式」局部变量
        public readonly HashSet<string> Busy = new(StringComparer.Ordinal);           // 正在算的（防环）
        public readonly HashSet<string> Chained = new(StringComparer.Ordinal);        // 已经作为执行链走过的模块
        public int Depth;

        /// <summary>当前嵌套的大括号块（每进一层 <c>{ }</c> 压一个 Id，出块时弹掉）。</summary>
        public readonly List<string> ScopeStack = new();

        /// <summary>当前作用域（空串 = 方法体顶层）。</summary>
        public string ScopeKey => ScopeStack.Count == 0 ? "" : string.Join("\u001f", ScopeStack);

        /// <summary>作用域 <paramref name="outer"/> 是不是 <paramref name="inner"/> 的祖先（含相同）。</summary>
        public static bool ScopeCovers(string outer, string inner) =>
            outer.Length == 0                                                       // 顶层罩住一切
            || (inner.Length == outer.Length ? string.Equals(inner, outer, StringComparison.Ordinal)
                                             : inner.StartsWith(outer + "\u001f", StringComparison.Ordinal));

        /// <summary>
        /// 这条链上最近一个【指向对象】声明的目标（用户要求：那个模块说的是「这张牌选中谁」）。
        /// 它是一个**执行点**里的共享状态：链上没自己接【对象】的效果默认打在它身上。
        /// 没有【指向对象】的老图 → 保持 <c>null</c>，效果还是按老规矩默认「打出时选中的目标」。
        /// </summary>
        public UnitRef? CurrentUnit;
        public bool CurrentUnitSet;
    }

    /// <summary>读某一条目的图（升级前 / 升级后）。</summary>
    private static ForgeGraphSpec? GraphOf(object owner, bool upgraded) => owner switch
    {
        CardSpec c => upgraded ? (c.UpgradedForgeGraph ?? c.ForgeGraph) : c.ForgeGraph,
        RelicSpec r => r.ForgeGraph,
        PotionSpec x => x.ForgeGraph,
        // V0.2.194：自定义状态（锻造页「状态列表」那一类）也能有一块布
        CustomPowerSpec cp => cp.ForgeGraph,
        _ => null,
    };

    /// <summary>新版端口号（见 <see cref="CharacterProfile.ForgeGraphPortVersion"/>）。</summary>
    public const int PortVersion = 2;

    /// <summary>
    /// 把老存档的图**迁移到新版【if】端口**（用户要求：条件挪到左边入口）。
    ///
    /// 老版【if】：入口 0 = 接入；**出口 0 = 条件插槽、1 = 是、2 = 否**。
    /// 新版【if】：入口 0 = 接入、**入口 1 = 条件**；出口 0 = 是、1 = 否。
    ///
    /// 必须迁移的原因：端口挪位改变了**下标**的含义，不迁移会把「是 / 否」两支对调
    /// （老图 `if.1` 是「是」、新布局里 1 是「否」）。规则：
    ///   · `if.0 → X`（老版「从条件插槽往外拖」的线 = 用户画的**条件链的起点**）
    ///     → 顺着这条链找到**末尾那个出布尔的模块**（例：【对象】→【状态】→**【关系符】**），
    ///       把它的「结果」接成 `链尾 → if.条件`；链里没有布尔产出就只删（老版它也不起作用）；
    ///   · `if.1 → X` → 改成 `if.0`（是）；
    ///   · `if.2 → X` → 改成 `if.1`（否）；
    ///   · `Y → if.0` 且 Y 是**能出布尔**的模块（老版里这是一条「条件来源」线）→ 改成 `Y → if.条件`；
    ///   · 其余不动（`Y → if.0` 就是执行链的接入，新旧一样）。
    ///
    /// 返回改过几张图（0 = 不用迁移 / 本来就是新版）。
    /// </summary>
    public static int MigrateIfPorts(CharacterProfile p)
    {
        if (p.ForgeGraphPortVersion >= PortVersion) return 0;
        int changed = 0;
        foreach (var g in AllGraphs(p))
        {
            if (g is null || g.IsEmpty) continue;
            var ifs = g.Nodes.Where(n => n is not null && n.Kind == "If").ToList();
            if (ifs.Count == 0) continue;
            var byId = ifs.ToDictionary(n => n.Id, n => n, StringComparer.Ordinal);
            // 1) 先把「从条件口往外拖」的线**扶正**：`if.0 → X` ⇒ `链尾 → if.条件`。
            //    为什么会有这种线：老版的条件口是【if】右边的一个**出口**，用户只能从那一头往条件链上拖
            //    （关系符的「结果」那边根本连不回来 —— 用户原话：「关系符的结果连不到 if 的条件」）。
            //    所以 X 就是**他画的那条条件链的起点**（例：X = 【对象】，链是 对象→状态→关系符）。
            //    真正该接到条件口上的是**链尾那个出布尔的模块**，所以这里得顺着链走到底，不能只看 X 自己。
            bool anyFix = false;
            foreach (var l in g.Links.ToList())
            {
                if (l is null || !byId.ContainsKey(l.From) || l.FromPort != 0) continue;
                var head = g.Node(l.To);
                if (head is null) continue;
                var dest = ResolveConditionTail(g, head);
                g.Links.Remove(l);
                anyFix = true;
                if (dest is null) continue;               // 这条链里没有能出布尔的模块（老版它本来也不起作用）
                bool already = g.Links.Any(x => x is not null
                    && string.Equals(x.From, dest.Id, StringComparison.Ordinal) && x.To == l.From
                    && x.FromPort == 0 && x.ToPort == ForgeBlockTree.IfCondPort);
                // 那个条件口上已经有一条正着的布尔来源线了 → 别再加第二条（老版同一口上可能挂着好几条）
                bool occupied = g.Links.Any(x => x is not null && x.To == l.From
                    && x.ToPort == ForgeBlockTree.IfCondPort && IsBoolProducer(g.Node(x.From)));
                if (already || occupied) continue;
                g.Links.Add(new ForgeLinkSpec { From = dest.Id, To = l.From, FromPort = 0, ToPort = ForgeBlockTree.IfCondPort });
            }
            // 2) 剩下的 `if.0 → X`（链里没有任何布尔产出的残留线）删掉
            if (g.Links.RemoveAll(l => l is not null && byId.ContainsKey(l.From) && l.FromPort == 0) > 0) anyFix = true;
            foreach (var l in g.Links)
            {
                if (l is null) continue;
                if (byId.ContainsKey(l.From))
                {
                    if (l.FromPort == 1) { l.FromPort = 0; anyFix = true; }
                    else if (l.FromPort == 2) { l.FromPort = 1; anyFix = true; }
                }
                else if (byId.ContainsKey(l.To) && l.ToPort == 0)
                {
                    // 老版里「接到 if 的那个 0 号口」可能是执行链（来源没类型），也可能是条件来源（来源出布尔）
                    if (IsBoolProducer(g.Node(l.From))) { l.ToPort = 1; anyFix = true; }
                }
            }
            if (anyFix) changed++;
        }
        p.ForgeGraphPortVersion = PortVersion;
        return changed;
    }

    /// <summary>顺着「取值 / 条件」链往下找那个**出布尔**的模块（迁移老版条件线用）。</summary>
    /// <remarks>
    /// 老版条件线是「从【if】的条件口往外拖」的，用户画的链长这样：`【对象】→【状态】→【关系符】`，
    /// 真正该接回条件口的是**链尾的【关系符】**（中间那两块只是取值）。所以这里从链头开始按层找：
    /// 只顺「接在**带类型的入口**上的线」（【状态】的「归属」/【关系符】的「左右值」/【条件】的「N」…），
    /// 不顺着执行链的「接入」跑（不然会从【对象】跑到【效果】里去）。
    /// </remarks>
    private static ForgeNodeSpec? ResolveConditionTail(ForgeGraphSpec g, ForgeNodeSpec head)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { head.Id };
        var queue = new Queue<ForgeNodeSpec>();
        queue.Enqueue(head);
        for (int guard = 0; queue.Count > 0 && guard < 32; guard++)
        {
            var cur = queue.Dequeue();
            if (IsBoolProducer(cur)) return cur;
            foreach (var l in g.Links)
            {
                if (l is null || !string.Equals(l.From, cur.Id, StringComparison.Ordinal)) continue;
                var next = g.Node(l.To);
                if (next is null || !seen.Add(next.Id)) continue;
                if (next.KindDef?.InputPort(l.ToPort)?.AcceptsKind is null) continue;   // 执行链的「接入」不算
                queue.Enqueue(next);
            }
        }
        return null;
    }

    /// <summary>存档里所有节点图（卡牌升级前 / 升级后、药水、遗物）。</summary>
    public static IEnumerable<ForgeGraphSpec?> AllGraphs(CharacterProfile p)
    {
        foreach (var c in p.Cards)
        {
            if (c is null) continue;
            yield return c.ForgeGraph;
            yield return c.UpgradedForgeGraph;
        }
        foreach (var x in p.Potions) if (x is not null) yield return x.ForgeGraph;
        foreach (var r in p.Relics) if (r is not null) yield return r.ForgeGraph;
    }

    /// <summary>
    /// 同 <see cref="AllGraphs"/>，再带上**自定义状态**的图（它也能挂节点图触发器）。
    /// 「这个存档里有没有用到某个节点图功能」这类扫描要走这一份，不然状态上的图会被漏掉。
    /// </summary>
    public static IEnumerable<ForgeGraphSpec?> AllGraphsWithPowers(CharacterProfile p)
    {
        foreach (var g in AllGraphs(p)) yield return g;
        foreach (var cp in p.CustomPowers) if (cp is not null) yield return cp.ForgeGraph;
    }

    /// <summary>
    /// 图里【效果】模块选了「临时增益 / 临时减益」时，合成一条对应的效果。
    ///
    /// **两份代码必须共用这一个函数**：临时 Power 的**类名**（<c>…ForgeTemp力量</c> /
    /// <c>…Abs</c> / <c>…NextTurn</c>）就是按这条效果算出来的 ——
    /// <c>CSharpCodeGen.CollectTempPowerEffects</c> 用它决定要生成哪些类，
    /// 这里（<see cref="EmitEffect"/> 的 TempPower 分支）用它拼调用点。
    /// 各写一套的话，两边名字一旦不一致就是 CS0246「找不到类型」。
    /// </summary>
    public static EffectSpec TempPowerEffectOf(ForgeNodeSpec node) => new()
    {
        Kind = "TempPower",
        PowerId = node.Get("PowerId"),
        // 没接【数值】模块时用的那个数字（和 EmitEffect 里读的默认值一致）
        Amount = (decimal)node.GetNumber("Amount", 6),
        // 「什么时候撤」：空 = 本回合结束时撤（默认），NextTurn = 下回合开始时才撤。
        // 用的是「效果列表」那边同一份候选（TempPowerReverses），所以类名后缀也对得上。
        TempPowerReverse = node.Get("Reverse", TempPowerReverses.TurnEnd),
    };

    /// <summary>
    /// 【效果】= 施加增益/减益 那个模块按**生成端读到的那几个字段**还原成一个 <see cref="EffectSpec"/>。
    ///
    /// 为什么要它（V0.2.139）：负数分档（直接施加负层数 / 先取实例再扣）由
    /// <see cref="CSharpCodeGen.ApplyPowerLine"/> 一处判，判据是 <c>EffectSpec</c> 上的
    /// <c>PowerId</c> / <c>Amount</c>；而锻造链这边手上只有 <see cref="ForgeNodeSpec.Fields"/>
    /// （老存档只有 Fields，<c>node.EffectSpec</c> 是 null）—— 不还原的话「负数分档」会按一个空 spec 判错。
    /// <c>Amount</c> 的取法和 <see cref="EmitEffect"/> 里那句 <c>ForgeNumbers.Resolve(p, node, "Amount", 6)</c>
    /// 完全一致（写公式 → 走运行时变量 → 这里拿到的是 fallback，正负由运行时决定，
    /// 和改动前那条内联分支的口径一样：只有**写死的数**才判得出正负）。
    ///
    /// ⚠ **不要**在这里填 <c>AmountFormula</c>：<c>UsesAmountFormula</c> 只看那一栏非空，
    /// 填了字面量 "-2" 会被 <c>IsNegativeAmount</c> 当成「正负要到运行时才知道」→ 负数分档直接失效。
    /// </summary>
    public static EffectSpec ApplyPowerEffectOf(ForgeNodeSpec node) => new()
    {
        Kind = "ApplyPower",
        PowerId = node.Get("PowerId"),
        Amount = (decimal)node.GetNumber("Amount", 0),
    };

    /// <summary>这条目的图会不会接管生成（只有「图非空」才接管，空图 / null 一切照旧）。</summary>
    public static bool UsesGraph(CardSpec c) => c.ForgeGraph is { IsEmpty: false } || c.UpgradedForgeGraph is { IsEmpty: false };

    /// <summary>
    /// 这块布左上角那个【能量】模块填的**能量费用**（0 = 没填）。
    ///
    /// 用户报的坑：以前这个数值只在「打出时」用 `PlayerCmd.LoseEnergy` 扣一次，
    /// **没有**声明成卡牌自己的费用 —— 于是卡面 / 游戏里显示的都是 0 费（卡牌页那边费用栏是藏起来的，
    /// 只有 `CardSpec.Cost` 那一个 0），实际却扣了能量。现在改成本体正规的费用：
    /// 生成时把这个数写进卡牌构造函数的 `base(cost, …)`（见 <c>CSharpCodeGen</c>），
    /// 本体会在打出前自己扣（`PlayCardAction` → `SpendResources` → `OnPlay`），
    /// 不够能量时也会像本体卡一样**打不出去**。
    /// </summary>
    public static int EnergyCostOf(ForgeGraphSpec? g) => NodeCost(g, "EnergyCost");

    /// <summary>【能量】模块里填的**额外资源量费用**（0 = 没填）→ 生成时声明成本体的星星费用（`CanonicalStarCost`）。</summary>
    public static int ResourceCostOf(ForgeGraphSpec? g) => NodeCost(g, "ResourceCost");

    private static int NodeCost(ForgeGraphSpec? g, string key)
    {
        if (g is null || g.IsEmpty) return 0;
        double raw = g.Nodes.FirstOrDefault(n => n is not null && n.Kind == "Energy")?.GetNumber(key) ?? 0.0;
        return raw <= 0.0 ? 0 : (int)Math.Round(raw);
    }

    /// <summary>
    /// 【能量】模块的「打出时消耗的能量」写的是不是 <c>X</c>（= 消耗全部能量，本体 <c>HasEnergyCostX</c> 那套）。
    ///
    /// <para>V0.2.215（用户实测报的）：这一栏的提示一直写着「X = 消耗掉的能量」，但校验器把它当**运行时值**拦住了
    /// （「必须是编译期常量」）—— 于是「虚化」这种 X 费卡在建的时候直接生成失败。
    /// 其实 X 本来就该支持：本体就是 `HasEnergyCostX => true` + 构造函数费用写 0 + 打出时
    /// `ResolveEnergyXValue()` 取到「消耗掉的能量」。现在这一档真的接上了。</para>
    /// </summary>
    public static bool IsEnergyCostX(ForgeGraphSpec? g) => IsCostXOf(g, "EnergyCost", "X", allowPlus: true);

    /// <summary>【能量】模块的「打出时消耗的额外资源量」写的是不是 <c>Y</c>（本体 <c>HasStarCostX</c> 那套）。</summary>
    public static bool IsResourceCostX(ForgeGraphSpec? g) => IsCostXOf(g, "ResourceCost", "Y", allowPlus: false);

    /// <summary>「升级后 X + N」里那个 N（只有卡牌页的「升级后费用」框 / 图的 X+N 才有；0 = 不加）。</summary>
    public static int EnergyCostXPlus(ForgeGraphSpec? g)
    {
        if (g is null || g.IsEmpty) return 0;
        var r = ForgeNumbers.ResolveCost(null, NodeText(g, "EnergyCost"), "X", allowPlus: true, "打出时消耗的能量");
        return r.IsX ? r.Plus : 0;
    }

    private static string NodeText(ForgeGraphSpec g, string key) =>
        g.Nodes.FirstOrDefault(n => n is not null && n.Kind == "Energy")?.Get(key) ?? "";

    private static bool IsCostXOf(ForgeGraphSpec? g, string key, string symbol, bool allowPlus)
    {
        if (g is null || g.IsEmpty) return false;
        var r = ForgeNumbers.ResolveCost(null, NodeText(g, key), symbol, allowPlus, key == "ResourceCost" ? "打出时消耗的额外资源量" : "打出时消耗的能量");
        return r.IsX;
    }

    /// <summary>
    /// 图里那个【指向对象】模块声明的**卡牌目标类型**（用户要求：「这个『指向对象』的意思是卡牌在游戏中将选中谁」）。
    ///
    /// 返回 null = 这张图里没有【指向对象】（老图 / 还没接上）→ 调用方按老规矩「只看效果里的作用对象」推。
    /// 映射：【指定单体敌人】= 会选一个敌人（AnyEnemy）；【全部敌人 / 随机敌人】同理；其余（自己 / 友方 / 召唤物）都不选目标。
    /// 优先读**升级后**那块布（有的话），再读升级前 —— 和「升级后走哪块布」的口径一致。
    /// </summary>
    public static string? DeclaredTargetType(CardSpec card)
    {
        foreach (var g in new[] { card.UpgradedForgeGraph, card.ForgeGraph })
        {
            if (g is null || g.IsEmpty) continue;
            var t = g.Nodes.FirstOrDefault(n => n is not null && n.Kind == "Target");
            if (t is null) continue;
            return UnitPickToTargetType(t.Get("Pick", "Self"));
        }
        return null;
    }

    /// <summary>【对象】/【指向对象】里的那一档 → 卡牌的目标类型。</summary>
    public static string UnitPickToTargetType(string? pick) => (pick ?? "Self") switch
    {
        "ChosenEnemy" => "AnyEnemy",
        "AllEnemies" => "AllEnemies",
        "RandomEnemy" => "RandomEnemy",
        _ => "Self",
    };

    /// <summary>这一档是不是「要让玩家选一个敌人」（决定了 cardPlay.Target 有没有值）。</summary>
    public static bool PickNeedsChosenEnemy(string? pick) => string.Equals(pick, "ChosenEnemy", StringComparison.Ordinal);
    public static bool UsesGraph(PotionSpec p) => p.ForgeGraph is { IsEmpty: false };
    public static bool UsesGraph(RelicSpec r) => r.ForgeGraph is { IsEmpty: false };

    /// <summary>配置里有没有任何一张锻造图（决定要不要写运行时辅助类 / 点击补丁）。</summary>
    public static bool AnyGraphs(CharacterProfile p) =>
        p.Cards.Any(c => c is not null && UsesGraph(c))
        || p.Potions.Any(x => x is not null && UsesGraph(x))
        || p.Relics.Any(x => x is not null && UsesGraph(x))
        || p.CustomPowers.Any(cp => cp is not null && UsesGraph(cp));

    /// <summary>
    /// V0.2.194：这只**自定义状态**是不是由锻造页那块布说了算。
    ///
    /// 判据和卡牌 / 药水 / 遗物一模一样：**图非空才算**（null / 空图 = 老路 ——「自定义状态」页那套
    /// 触发时机 + 效果列表，生成文本一个字符都不变）。
    /// </summary>
    public static bool UsesGraph(CustomPowerSpec cp) => cp.ForgeGraph is { IsEmpty: false };

    /// <summary>
    /// 把一张图整理成「触发时机 + 它的执行链入口」。返回 null = 这张图是空的（什么都不生成）。
    ///
    /// <paramref name="scope"/> = Card / Potion / Relic：**必须传**，因为合并菜单之后
    /// <c>CardPlayed / CardExhausted / CardDrawn / CardDiscarded</c> 四条在卡牌档和遗物档里
    /// 同名（这是「同一套菜单」的本意），不按 scope 查就会把卡牌那条（Hook = OnPlay）当成遗物图的时机。
    ///
    /// V0.2.168 起一块布可以有多块【触发时机】，这里只取**第一块**（<see cref="Plans"/> 才是全部）。
    /// </summary>
    private static HookPlan? Plan(ForgeGraphSpec? g, string scope) => Plans(g, scope).FirstOrDefault();

    /// <summary>
    /// 把一张图整理成**所有**执行点（V0.2.168：一块布可以有**多块**【触发时机】，每块各带自己后面那一条链）。
    /// 空列表 = 这张图是空的 / 一块【触发时机】都没有。
    ///
    /// <paramref name="scope"/> 的含义同 <see cref="Plan"/>。
    /// </summary>
    private static List<HookPlan> Plans(ForgeGraphSpec? g, string scope)
    {
        var list = new List<HookPlan>();
        if (g is null || g.IsEmpty) return list;
        foreach (var trigger in g.TriggerNodes)
        {
            var timing = ForgeTimings.Find(trigger.Get("Timing", ForgeTimings.CardPlayed), scope)
                         ?? ForgeTimings.Find(ForgeTimings.CardPlayed, scope)
                         ?? ForgeTimings.Find(ForgeTimings.CardPlayed)!;
            list.Add(new HookPlan(trigger, timing));
        }
        return list;
    }

    /// <summary>
    /// 多块【触发时机】里，**落在同一个本体钩子上**的那几块只留第一块（其余进 <c>Skipped</c>）。
    ///
    /// 为什么必须留：两块都生成会在同一个类里写出两个签名一样的 override —— CS0111，整个模组编译不过。
    /// 校验器（<see cref="Issues"/>）对这种情况**报错**拦住，这里是兜底（老存档 / 命令行绕过界面时也不出坏代码）。
    /// </summary>
    private static (List<HookPlan> Keep, List<HookPlan> Skipped) SplitByHook(List<HookPlan> plans)
    {
        // V0.2.279（用户报「两块【触发时机】落在同一个钩子上，第 2 块白配了」）：一个都不丢 ——
        // 同一个钩子上的多块由 EmitHook 合并进同一个方法体（每块各包一层 { }）。
        return (plans, new List<HookPlan>());
    }

    /// <summary>
    /// 按**本体钩子**分组（同钩子的多块合成一个方法；组之间保持图上出现的先后顺序）。
    /// V0.2.279：两块都落在同一个钩子上时，正确语义就是「那一刻两块都跑」，所以是合并不是二选一。
    /// </summary>
    private static List<List<HookPlan>> GroupByHook(List<HookPlan> plans)
    {
        var groups = new List<List<HookPlan>>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pl in plans)
        {
            if (index.TryGetValue(pl.Timing.Hook, out int at)) groups[at].Add(pl);
            else { index[pl.Timing.Hook] = groups.Count; groups.Add(new List<HookPlan> { pl }); }
        }
        return groups;
    }

    /// <summary>被跳过的那些【触发时机】写一段注释（说清为什么没生成 + 怎么办）。</summary>
    private static void AppendSkippedNotes(StringBuilder sb, List<HookPlan> skipped)
    {
        foreach (var pl in skipped)
        {
            sb.AppendLine();
            sb.AppendLine($"    // ⚠ 这块布上还有一块【触发时机】=「{pl.Timing.Display}」：它和前面那块落在**同一个本体钩子**上，");
            sb.AppendLine("    //    这一版只生成前面那一块（两块都生成 = 同一个类里两个同名 override，整个模组编译不过）。");
            sb.AppendLine("    //    校验器会拦住这条：请把两块合成一块（要区分用【条件】模块），或者把这一块换成别的时机。");
        }
    }

    /// <summary>一条锻造条目属于哪一类布（查时机要用它）。</summary>
    private static string ScopeOf(object owner) => owner switch
    {
        CardSpec => "Card",
        RelicSpec => "Relic",
        PotionSpec => "Potion",
        // V0.2.194：自定义状态那一类布（时机菜单 = PowerTriggers，见 ForgeGraphCatalog.BuildAll）
        CustomPowerSpec => ForgeTimings.PowerScope,
        _ => "Card",
    };

    // ==================================================================================
    //  对外入口：卡牌 / 药水 / 遗物的钩子代码
    // ==================================================================================

    /// <summary>卡牌的钩子代码（OnPlay 那一支会生成 <c>ForgeGraphOnPlay</c>，由 OnPlay 末尾调用）。</summary>
    public static string CardHooks(CharacterProfile p, CardSpec card, bool isUpgraded)
    {
        PercentPowers.Current = p;   // V0.2.301：锻造图上用到的「百分比状态」也要能拼出类名
        var g = GraphOf(card, isUpgraded);
        var all = Plans(g, "Card");
        if (all.Count == 0 || g is null) return "";
        var (plans0, skipped) = SplitByHook(all);
        var plans = new List<HookPlan>();
        var groups = GroupByHook(plans0);
        foreach (var grp in groups) plans.Add(grp[0]);   // 每个钩子只发一块（其余走 extra 合并）
        // ⚠ V0.2.180：这里是「这张牌的图上，**被嵌进卡自己钩子**的那几块」。
        // 词用的是 CardOwnHookNames（卡自己会生成哪些钩子）**而不是**「图里用了哪些钩子」——
        // 单块布时图里那一块**自己写 override**（老规矩，输出逐字不变）。
        // 「两块布撞同一个钩子」那一种由 CSharpCodeGen.EmitGraphOnlyHookContainers 补一个壳 override 来兜，
        // 那它的两块也必须变成私有方法 —— 这里把它并进来。
        var nested = NestedCardHooks(card);
        foreach (string overlap in CardGraphOverlapHooks(card))
            if (!nested.Contains(overlap)) nested.Add(overlap);
        var sb = new StringBuilder();
        for (int i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            // 【触发时机】= 卡牌被打出时：不能自己 override OnPlay（会盖掉用户手填的那套效果），
            // 改成生成一个私有方法，由 OnPlay 末尾调用它。
            // **升级前 / 升级后两块布各用一个方法名** —— 两块布都设成「打出时」时，
            // 用同一个名字会生成两个签名一样的方法（CS0111，整个模组编译不过）。
            //
            // V0.2.169：图里这块【触发时机】和**这张牌自己的钩子**撞上时（被消耗 / 被丢弃 / 被保留 /
            // 被抽到）同理 —— 也不能自己 override（那一个类里就有两个同名 override 了，CS0111，
            // 用户实测报过 AinardiCard8.cs 那种）。改成私有方法，由那个钩子调用它
            //（遗物那条链早就是这么做的，见 RelicGraphNestsInTrigger）。
            string? method = plan.Timing.Hook == "OnPlay" ? PlayMethodName(isUpgraded)
                : nested.Contains(plan.Timing.Hook) ? CardNestedMethod(plan.Timing.Hook, isUpgraded)
                : null;

            sb.AppendLine();
            sb.AppendLine(plans.Count == 1
                ? $"    // ===== 锻造节点图（{(isUpgraded ? "升级后" : "升级前")}）：{plan.Timing.Display} ====="
                : $"    // ===== 锻造节点图（{(isUpgraded ? "升级后" : "升级前")}）第 {i + 1} 块【触发时机】：{plan.Timing.Display} =====");
            var __extras = groups.Count > i ? groups[i].Skip(1).ToList() : new List<HookPlan>();
            EmitHook(sb, p, g, plan,
                new EmitCtx("Card", "cardPlay", "this", isUpgraded, PlanIdx: i,
                    NoHookParams: method is not null && plan.Timing.Hook != "OnPlay"),
                method, __extras);
        }
        AppendSkippedNotes(sb, skipped);
        return sb.ToString();
    }

    /// <summary>
    /// 图上这几块【触发时机】会和**这张牌自己的钩子**撞同一个方法名，必须嵌进去（不能自己 override）。
    ///
    /// 两种来源：
    ///   · 普通效果的「卡牌时机」（被消耗 / 被丢弃 / 被保留 → <c>CSharpCodeGen.EmitCardTimingHooks</c> 那几个 override）；
    ///   · 卡牌自带的「抽到这张牌时本场战斗费用 -N」（→ <c>CSharpCodeGen.EmitSelfDrawHook</c> 的 AfterCardDrawn）。
    ///
    /// 判据里的钩子名**都从生成端那一份推**（不在这里另抄一张表）—— 抄一份就会漂，
    /// 加了新子时机忘了同步就又是一个类里两个同名 override（CS0111）。
    /// </summary>
    public static List<string> NestedCardHooks(CardSpec card)
    {
        var mine = CardOwnHookNames(card);
        var found = new List<string>();
        foreach (bool up in new[] { false, true })
        {
            var g = GraphOf(card, up);
            if (g is null) continue;
            var (plans, _) = SplitByHook(Plans(g, "Card"));
            foreach (var pl in plans)
                if (pl.Timing.Hook != "OnPlay" && mine.Contains(pl.Timing.Hook) && !found.Contains(pl.Timing.Hook))
                    found.Add(pl.Timing.Hook);
        }
        return found;
    }

    /// <summary>
    /// 这张牌上**两块布都用了**的卡牌钩子（升级前那块 + 升级后那块落在同一个本体钩子上）。
    ///
    /// <para>为什么需要：两块布各写一个 <c>override</c> 就是**同一个类里两个同名同参的方法** → CS0111，
    /// 整个模组编译不过（用户实测报的正是这个：两块布都设成「这张牌被抽到时」→ 两个 <c>AfterCardDrawn</c>）。
    /// 单块布时照旧走它自己的 <c>override</c>（老输出逐字不变）—— 所以这里只认「两块都撞上」。</para>
    ///
    /// <para>⚠ 判据里的图一律用 <c>GraphOf(card, true)</c>（= 没有「升级后」那块布时**退回「升级前」那块**）——
    /// 这正是两条路都撞上的原因：只有一张布时它同时当「升级前」和「升级后」用。</para>
    /// </summary>
    public static List<string> CardGraphOverlapHooks(CardSpec card)
    {
        var found = new List<string>();
        // ⚠ 「升级后那块布没设」**不算**两块都撞上：那种情况下 GraphOf(card,true) 会**退回**升级前那块布
        //（本体语义：没单独配升级后 = 升级前后用同一套），而**只有一块布时它自己写一个 override 就够了** ——
        // 那不是 CS0111，只有「真的有两块布、两块都用了同一个钩子」才是。
        // 判错这一句的后果实测过：卡上只有一块布（时机 = 被保留）时会凭空多出一个 AfterFlush 壳 override，
        // 自检那条「不许生成 AfterFlush(PlayerChoiceContext) 这种不存在的签名」直接 FAIL。
        if (card.UpgradedForgeGraph is not { IsEmpty: false }) return found;
        var baseG = GraphOf(card, false);
        var upG = GraphOf(card, true);
        if (baseG is null || upG is null) return found;
        var (basePlans, _) = SplitByHook(Plans(baseG, "Card"));
        var (upPlans, _) = SplitByHook(Plans(upG, "Card"));
        var upHooks = upPlans.Select(p => p.Timing.Hook).ToHashSet(StringComparer.Ordinal);
        foreach (var pl in basePlans)
            if (pl.Timing.Hook != "OnPlay" && upHooks.Contains(pl.Timing.Hook) && !found.Contains(pl.Timing.Hook))
                found.Add(pl.Timing.Hook);
        return found;
    }

    /// <summary>
    /// 图里所有非 <c>OnPlay</c> 的卡牌钩子（两块布都算，去重）。「图上这些块是不是得靠一个壳 override 调用」
    /// 由调用方拿它来判断（见 <c>CSharpCodeGen.EmitGraphOnlyHookContainers</c>）。
    /// </summary>
    public static List<string> AllCardGraphHooks(CardSpec card)
    {
        var found = new List<string>();
        foreach (bool up in new[] { false, true })
        {
            var g = GraphOf(card, up);
            if (g is null) continue;
            var (plans, _) = SplitByHook(Plans(g, "Card"));
            foreach (var pl in plans)
                if (pl.Timing.Hook != "OnPlay" && !found.Contains(pl.Timing.Hook)) found.Add(pl.Timing.Hook);
        }
        return found;
    }

    /// <summary>这张牌自己会生成哪些钩子方法（图里那块撞上它就得嵌进去）。</summary>
    private static HashSet<string> CardOwnHookNames(CardSpec card)
    {
        var hooks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in card.Effects)
            if (CSharpCodeGen.IsCardTiming(e.Condition))
                hooks.Add(CSharpCodeGen.CardTimingMethodName(e.Condition!.Timing ?? ""));
        // 「抽到这张牌时它本场战斗的费用 -N」：卡自己的 AfterCardDrawn（本体 KinglyKick 那种）
        if (CSharpCodeGen.HasSelfDrawHook(card.Effects)) hooks.Add("AfterCardDrawn");
        return hooks;
    }

    /// <summary>这张牌自己会不会生成 <paramref name="hook"/> 那个钩子（给「要不要补一个壳 override」用）。</summary>
    public static bool CardOwnsHook(CardSpec card, string hook) => CardOwnHookNames(card).Contains(hook);

    /// <summary>钩子名 → 界面上的中文时机名（生成注释用；认不出来就退回钩子名本身）。</summary>
    public static string HookZh(string hook) =>
        ForgeTimings.For("Card").FirstOrDefault(t => string.Equals(t.Hook, hook, StringComparison.Ordinal))?.Display ?? hook;

    /// <summary>这张牌的图里有没有被嵌进来的这一支（生成端据此决定写不写那句调用）。</summary>
    public static bool CardGraphNestsInHook(CardSpec card, string hook) =>
        NestedCardHooks(card).Contains(hook);

    /// <summary>某一块布上有没有这个钩子的【触发时机】（决定调用处要不要按升级与否二选一）。</summary>
    public static bool GraphHasHook(ForgeGraphSpec? g, string hook) =>
        Plans(g, "Card").Any(pl => string.Equals(pl.Timing.Hook, hook, StringComparison.Ordinal));

    /// <summary>
    /// 图嵌进卡牌钩子时那个私有方法的名字（调用方 = <c>CSharpCodeGen.EmitCardTimingHooks</c> /
    /// <c>EmitSelfDrawHook</c>）。照 <see cref="PlayMethodName"/> 那个规矩：升级前 / 升级后两块布
    /// 各一个名字，否则两块布都撞同一个钩子时又变成 CS0111。
    /// </summary>
    public static string CardNestedMethod(string hook, bool upgraded) =>
        "ForgeGraphOn" + hook + (upgraded ? "Upgraded" : "");

    /// <summary>
    /// 卡牌「打出时」那一支生成的方法名。
    /// 升级前 = <c>ForgeGraphOnPlay</c>、升级后 = <c>ForgeGraphOnPlayUpgraded</c> ——
    /// 两块布都可能设成「卡牌被打出时」，同名会 CS0111（一个类里两个签名一样的方法）。
    /// 生成端（<c>CSharpCodeGen</c>）按 <c>card.UpgradedForgeGraph</c> 是否非空决定调用哪一个。
    /// </summary>
    public static string PlayMethodName(bool upgraded) => upgraded ? "ForgeGraphOnPlayUpgraded" : "ForgeGraphOnPlay";

    /// <summary>这张牌的节点图里有没有「卡牌被打出时」这一支。</summary>
    public static bool HasPlayTiming(CardSpec card) =>
        HasPlayTimingIn(card.ForgeGraph)
        || (card.UpgradedForgeGraph is { IsEmpty: false } && HasPlayTimingIn(card.UpgradedForgeGraph));

    /// <summary>某一块布上有没有「卡牌被打出时」这一支（V0.2.168：这块布上**任何**一块【触发时机】都算）。</summary>
    public static bool HasPlayTimingIn(ForgeGraphSpec? g) =>
        Plans(g, "Card").Any(pl => string.Equals(pl.Timing.Hook, "OnPlay", StringComparison.Ordinal));

    /// <summary>
    /// 这瓶药的节点图里有没有「药水被使用时」这一支。
    ///
    /// 有的话生成端**不能再写那个空的 <c>OnUse</c> 骨架** —— 两边会生成两个签名一样的 override，
    /// 直接 CS0111（用户实测的构建日志：<c>cs\Potions\SparklePotion8.cs(21,35): error CS0111</c>）。
    /// 和卡牌那边 <see cref="HasPlayTiming"/> 是同一个坑、同一套处理。
    /// </summary>
    public static bool HasUseTiming(PotionSpec potion) => HasUseTimingIn(potion.ForgeGraph);

    /// <summary>某一块布上有没有「药水被使用时」这一支（V0.2.168：这块布上**任何**一块【触发时机】都算）。</summary>
    public static bool HasUseTimingIn(ForgeGraphSpec? g) =>
        Plans(g, "Potion").Any(pl => string.Equals(pl.Timing.Hook, "Use", StringComparison.Ordinal));

    /// <summary>药水的钩子代码。</summary>
    public static string PotionHooks(CharacterProfile p, PotionSpec potion)
    {
        var g = potion.ForgeGraph;
        var all = Plans(g, "Potion");
        if (all.Count == 0 || g is null) return "";
        var (plans0, skipped) = SplitByHook(all);
        var plans = new List<HookPlan>();
        var groups = GroupByHook(plans0);
        foreach (var grp in groups) plans.Add(grp[0]);   // 每个钩子只发一块（其余走 extra 合并）
        var sb = new StringBuilder();
        for (int i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            sb.AppendLine();
            sb.AppendLine(plans.Count == 1
                ? $"    // ===== 锻造节点图：{plan.Timing.Display} ====="
                : $"    // ===== 锻造节点图第 {i + 1} 块【触发时机】：{plan.Timing.Display} =====");
            EmitHook(sb, p, g, plan, new EmitCtx("Potion", "null", "null", PlanIdx: i));
        }
        AppendSkippedNotes(sb, skipped);
        return sb.ToString();
    }

    /// <summary>
    /// 这只遗物的节点图会不会被**嵌进它自己的触发钩子**里（而不是另加一个 override）。
    /// 判据：图上**任何一块**【触发时机】和遗物的某条「触发时机」是同一个钩子 —— 那时两边会生成两个签名
    /// 完全一样的 override，编译直接 CS0111。
    /// </summary>
    public static bool RelicGraphNestsInTrigger(RelicSpec relic, string relicTriggerId) =>
        NestedRelicTriggerIds(relic).Any(id => string.Equals(id, relicTriggerId, StringComparison.Ordinal));

    /// <summary>
    /// 图嵌进遗物触发钩子时那个私有方法的名字（调用方 = <c>CSharpCodeGen.RelicSource</c>）。
    ///
    /// 只有一块嵌入时就是老的 <c>ForgeGraphOnTrigger</c>（老存档生成的文本一个字都不变）；
    /// 一块布上有**多块**嵌入的【触发时机】时按顺序编号 <c>ForgeGraphOnTrigger2 / 3…</c> ——
    /// 都叫同一个名字会 CS0111。
    /// </summary>
    public static string RelicGraphNestedMethod(RelicSpec relic, string relicTriggerId)
    {
        var ids = NestedRelicTriggerIds(relic);
        int idx = ids.FindIndex(x => string.Equals(x, relicTriggerId, StringComparison.Ordinal));
        return idx <= 0 ? CSharpCodeGen.ForgeRelicGraphMethod : CSharpCodeGen.ForgeRelicGraphMethod + (idx + 1);
    }

    /// <summary>
    /// 图上「会被嵌进遗物自己的触发钩子」的那几块【触发时机】对应的**遗物时机 id**（按图的顺序）。
    /// </summary>
    private static List<string> NestedRelicTriggerIds(RelicSpec relic)
    {
        var ids = new List<string>();
        var (plans, _) = SplitByHook(Plans(relic.ForgeGraph, "Relic"));
        foreach (var pl in plans)
        {
            string? hit = NestedRelicTriggerId(relic, pl);
            if (hit is not null) ids.Add(hit);
        }
        return ids;
    }

    /// <summary>这块【触发时机】嵌进了遗物的哪一条触发时机（没有 = null，那时图另加一个 override）。</summary>
    private static string? NestedRelicTriggerId(RelicSpec relic, HookPlan plan)
    {
        foreach (var t in relic.Triggers)
        {
            if (t is null || string.IsNullOrWhiteSpace(t.Kind)) continue;
            // 遗物触发时机的钩子名从**签名那一份**推（CSharpCodeGen.RelicTriggerSignature 是唯一事实来源）——
            // 免得这里再抄一张「时机 → 钩子」的表，加了新时机忘了同步就会一个类里出两个同名 override（CS0111）。
            if (string.Equals(CSharpCodeGen.RelicHookNameOf(t.Kind), plan.Timing.Hook, StringComparison.Ordinal))
                return t.Kind;
        }
        return null;
    }

    /// <summary>
    /// 遗物图这个执行点的 choiceContext 从哪来（<see cref="EmitCtx.CtxFrom"/> 的那几个档）。
    ///
    /// 判据和遗物页（<see cref="CSharpCodeGen.RelicSource"/>）**同一套**：
    ///   本体钩子带 → 直接用；「获得时」→ 造阻塞式；战斗里那几条没带的 → 造 HookPlayerChoiceContext。
    /// 特别的两处（都是锻造页自己的情况）：
    ///   · <c>nested</c>（图嵌进遗物自己的触发钩子里）→ 那个普通方法拿不到钩子参数，所以**自己造一个**
    ///     （造不出来时也照样 None：校验器会拦住需要 context 的效果）；
    ///   · 「血量低于 N」用的是本体 AfterCurrentHpChanged（没带 context）→ 按战斗里的做法造一个，
    ///     以前这里被当成「有 context」，生成出来引用一个不存在的变量（CS0103）。
    /// </summary>
    private static string RelicCtxFrom(HookPlan plan, bool nested)
    {
        string id = plan.Timing.Id;
        // 「当遗物被点击时」：不是本体钩子（是界面补丁转发过来的），**不造 context**（保持原样）
        if (string.Equals(plan.Timing.Hook, "ForgeOnRelicClicked", StringComparison.Ordinal)) return "None";
        if (string.Equals(id, ForgeTimings.HpBelow, StringComparison.Ordinal)) return "Hook";
        if (CSharpCodeGen.HasContext(id)) return nested ? "Hook" : "Param";
        if (CSharpCodeGen.CanBootstrapContext(id)) return "Blocking";
        if (CSharpCodeGen.BootstrapInCombat(id)) return "Hook";
        return "None";
    }

    /// <summary>
    /// 这张图里有没有「需要 choiceContext 的效果」（校验用：造不出 context 的时机上必须报错拦住）。
    ///
    /// 判据直接复用遗物页那份 <see cref="CSharpCodeGen.RelicNeedsChoiceContext"/>（**拿不准就 true** 的那份）：
    /// 它宁可多算一个（多造一个用不上的局部变量），也不肯漏 —— 所以这里对「图里真有需要 context 的效果」
    /// 是敏感、不会漏报的。
    /// </summary>
    private static bool GraphNeedsChoiceContext(ForgeGraphSpec g)
    {
        foreach (var n in g.Nodes)
        {
            if (n is null || !string.Equals(n.Kind, "Effect", StringComparison.Ordinal)) continue;
            var probe = new EffectSpec
            {
                Kind = n.Get("Effect", "Damage"),
                NextTurn = n.GetFlag("NextTurn"),
                Amount = (decimal)n.GetNumber("Amount", 6),
            };
            if (CSharpCodeGen.RelicNeedsChoiceContext(probe)) return true;
        }
        return false;
    }

    /// <summary>遗物的钩子代码。</summary>
    public static string RelicHooks(CharacterProfile p, RelicSpec relic)
    {
        PercentPowers.Current = p;   // V0.2.301：锻造图上用到的「百分比状态」也要能拼出类名
        var g = relic.ForgeGraph;
        var all = Plans(g, "Relic");
        if (all.Count == 0 || g is null) return "";
        var (plans0, skipped) = SplitByHook(all);
        var plans = new List<HookPlan>();
        var groups = GroupByHook(plans0);
        foreach (var grp in groups) plans.Add(grp[0]);   // 每个钩子只发一块（其余走 extra 合并）
        var sb = new StringBuilder();
        for (int i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            // 「当遗物被点击时」不是本体的虚方法：生成一个普通方法，由补丁按类型转发；
            // 图里的时机和遗物触发时机是同一个钩子时，也生成一个普通方法（由那个钩子调用）。
            string? nestTrigger = NestedRelicTriggerId(relic, plan);
            bool nest = nestTrigger is not null;
            string? method = nest ? RelicGraphNestedMethod(relic, nestTrigger!)
                : string.Equals(plan.Timing.Hook, "ForgeOnRelicClicked", StringComparison.Ordinal) ? "ForgeOnRelicClicked"
                : null;
            sb.AppendLine();
            sb.AppendLine(plans.Count == 1
                ? $"    // ===== 锻造节点图：{plan.Timing.Display} ====="
                : $"    // ===== 锻造节点图第 {i + 1} 块【触发时机】：{plan.Timing.Display} =====");
            var __extrasR = groups.Count > i ? groups[i].Skip(1).ToList() : new List<HookPlan>();
            EmitHook(sb, p, g, plan, new EmitCtx("Relic", "null", "null", CtxFrom: RelicCtxFrom(plan, nest), PlanIdx: i), method, __extrasR);
        }
        AppendSkippedNotes(sb, skipped);
        return sb.ToString();
    }

    // ==================================================================================
    //  V0.2.194：自定义状态（锻造页「状态列表」那一类布）
    // ==================================================================================

    /// <summary>
    /// 状态那块布上某个【触发时机】模块对应的时机（scope = Power）。
    /// 认不出来（老存档 / 手改坏了 / 这块布上没有这一档）时退回「玩家回合开始时」——
    /// 校验器会给一条错误说清，生成端至少不会崩。
    /// </summary>
    private static ForgeTimings.TimingOption PowerTimingOf(ForgeNodeSpec trigger)
    {
        string id = trigger.Get("Timing", PlayerTurnStartTimingFallback);
        return ForgeTimings.Find(id, ForgeTimings.PowerScope)
            ?? ForgeTimings.Find(PlayerTurnStartTimingFallback, ForgeTimings.PowerScope)
            ?? ForgeTimings.All[0];
    }

    private const string PlayerTurnStartTimingFallback = "PlayerTurnStart";

    /// <summary>状态那块布上「只对哪类牌生效」那一栏（留空 / Any = 任意牌；只对打牌类时机有意义）。</summary>
    private static string CardFilterOf(ForgeNodeSpec trigger)
    {
        string raw = (trigger.Get("CardFilter") ?? "").Trim();
        return raw.Length == 0 ? "Any" : raw;
    }

    /// <summary>
    /// V0.2.270：图上【触发时机】的「层数变化方向」—— 只认 Up / Down，别的一律当「都算」（空串）。
    /// 老图没有这一栏 → <c>Get</c> 返回默认空串 → 生成结果逐字不变。
    /// </summary>
    private static string NormalizeDirection(string? raw) => (raw ?? "").Trim() switch
    {
        "Up" => "Up",
        "Down" => "Down",
        _ => "",
    };

    /// <summary>状态那块布的 choiceContext 从哪来（见 EmitCtx.CtxFrom）。</summary>
    private static string PowerCtxFrom(ForgeTimings.TimingOption timing) =>
        CustomPowerGen.TriggerHasChoiceContext(timing.Id) ? "Param" : "Hook";

    /// <summary>
    /// V0.2.194：**状态那一档**渲染正文时，【状态】/【状态列表】没接【对象】时的默认「归属」。
    ///
    /// 卡牌 / 遗物 / 药水是「打出时选中的那个目标」（<c>__fgTarget</c>）；状态没有这个目标，
    /// 用户想数的当然是**自己身上**的状态（要盯别人拖一块【对象】接上就行）。
    /// 用生成期字段而不是给 <see cref="ValueVar"/> 加参数：它的调用点有二十几个（条件 / 关系符 / 效果…），
    /// 一路加参数容易漏；这里一处判断就够，而且非状态档（null）逐字不变。
    /// </summary>
    [ThreadStatic] private static string? _valueDefaultUnit;

    /// <summary>【状态】/【状态列表】没接【对象】时的默认归属表达式。</summary>
    private static string ValueDefaultUnit => _valueDefaultUnit ?? "__fgTarget";

    /// <summary>
    /// 这个状态是不是「只叠一层」（<c>CustomPowerSpec.Single</c>）的自定义状态。
    ///
    /// <para>V0.2.246：读它的时候要**按语义夹成 0 / 1** —— 本体只是把层数藏起来（<c>PowerStackType.Single</c>
    /// 的注释写的是 “Amount is hidden, and is always 1”），<c>PowerCmd.Apply</c> 第二次照样走
    /// <c>ModifyAmount(+1)</c> → <c>SetAmount(2)</c>，内部层数真的会变 2。用户图上写「等于 1 层」时，
    /// 这个状态只要被施加过两次就静默失效（图标还在 ⇒ 看起来「形态生效但效果没生效」）。</para>
    /// </summary>
    private static bool IsSingleLayerCustomPower(CharacterProfile p, string powerId)
    {
        string id = (powerId ?? "").Trim();
        if (id.Length == 0) return false;
        for (int i = 0; i < p.CustomPowers.Count; i++)
        {
            var cp = p.CustomPowers[i];
            if (cp is null || !cp.Single) continue;
            if (string.Equals(CustomPowerGen.ClassNameOf(p, cp, i), id, StringComparison.Ordinal)) return true;
        }
        return EffectCatalog.IsSingleLayerCustomPower(id);
    }

    /// <summary>
    /// 把状态那块布上的【触发时机】整理成「自定义状态」页那套 <see cref="PowerTriggerSpec"/>。
    ///
    /// 用途：<c>CustomPowerGen.Source</c> 的方法壳（签名 / 归属守卫 / choiceContext / 衰减移除 / 数值闸门）
    /// 全部照老路走 —— 只有「跑效果」那一步换成 <see cref="PowerHookBodies"/> 里图上渲染好的正文。
    /// 返回 <c>null</c> = 图空 / 没有图（**老路，逐字不变**）。
    /// </summary>
    public static List<PowerTriggerSpec>? PowerTriggersOf(CharacterProfile p, CustomPowerSpec cp)
    {
        if (!UsesGraph(cp)) return null;
        var g = cp.ForgeGraph!;
        var list = new List<PowerTriggerSpec>();
        var seenHooks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trigger in g.TriggerNodes)
        {
            var timing = PowerTimingOf(trigger);
            // ⚠ 同一个本体钩子上只留**第一块**【触发时机】：两块都发就是同一个类里两个同名 override
            //（CS0111）—— 校验器对这种情况报错拦住（和卡牌 / 遗物一个口径，见 ForgeGraphGen.Issues）。
            // 这里必须去重：不去重的话方法壳里的效果体会被写两遍（正文是整段 Raw 进去的）。
            if (!seenHooks.Add(timing.Hook)) continue;
            var spec = new PowerTriggerSpec
            {
                Kind = PowerTriggers.Canonical(timing.Id),
                PowerId = (trigger.Get("PowerId") ?? "").Trim(),
                // 「只对哪类牌生效」（只有打牌类时机用得到）：图上那一栏留空 = 任意牌
                CardFilter = CardFilterOf(trigger),
                // 图上没有「每累计 N 次触发一次」那一栏 → 0 = 不过滤（老路那个「数值闸门」就套不上）；
                // 只有「某个状态层数到达 N 层时」用得到图上的「到达 N 层」（PowerAmount）。
                TriggerAmount = string.Equals(timing.Id, ForgeTimings.PowerReached, StringComparison.Ordinal)
                    ? (decimal)trigger.GetNumber("PowerAmount", 1)
                    : 0m,
                TriggerScope = trigger.Get("CountScope", "Turn"),
                // V0.2.270：图上【触发时机】新加的「层数变化方向」（只在「某个状态层数变化后」有意义）
                PowerDirection = NormalizeDirection(trigger.Get("PowerDirection")),
            };
            // 效果列表：只给「这个执行点里有没有需要 choiceContext 的效果」这条判据用
            //（方法壳里要不要自己造一个 context，见 CustomPowerGen.EmitChoiceContextBootstrap）。
            var (chainEffects, _) = WalkChain(g, trigger);
            foreach (var node in chainEffects)
            {
                var spec2 = ForgeEffectBridge.Ensure(node);
                if (spec2 is not null) spec.Effects.Add(spec2);
            }
            list.Add(spec);
        }
        return list;
    }

    /// <summary>
    /// 每个**本体钩子**上要写进方法体的正文（图上渲染好的，缩进按「方法体第 2 层」写死）。
    ///
    /// 为什么按钩子给而不是按触发时机给：两块【触发时机】落在同一个钩子上时只生成第一块
    ///（两块都生成 = 同一个类里两个同名 override，CS0111）—— 校验器对这种情况**报错**拦住。
    /// </summary>
    public static Dictionary<string, string> PowerHookBodies(CharacterProfile p, CustomPowerSpec cp)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!UsesGraph(cp)) return map;
        var g = cp.ForgeGraph!;
        int idx = 0;
        // 【状态】/【状态列表】没接【对象】时默认数**自己身上**的（见 ValueDefaultUnit 的说明）。
        var prevUnit = _valueDefaultUnit;
        _valueDefaultUnit = "__fgSelf";
        try
        {
        foreach (var trigger in g.TriggerNodes)
        {
            var timing = PowerTimingOf(trigger);
            // 「获得时」是遗物专属的：CustomPowerGen 那一档**不写方法**（只留一句注释），
            // 所以这里也不能产出正文（校验器会给一条错误拦住这个组合）。
            if (string.Equals(timing.Id, "Obtained", StringComparison.Ordinal)) continue;
            // V0.2.279（用户报「两块【触发时机】落在同一个钩子上，第 2 块白配」）：不再丢块 ——
            // 同一个钩子上的多块**合并进同一个方法体**（每块各包一层 { }，局部变量同名也不冲突）。
            var plan = new HookPlan(trigger, timing);
            var (_, hasReward) = WalkChain(g, trigger);
            var sb = new StringBuilder();
            var ctx = new EmitCtx(ForgeTimings.PowerScope, "null", "null",
                CtxFrom: PowerCtxFrom(timing),
                // 【卡牌奖励】在状态这块布上**不支持**（那几句要 base.Owner.Character.CardPool，状态上没有），
                // 这里保持 false → 图上那块会写一句「只在战斗胜利时那条链上才会生成」的注释，
                // 校验器另外给一条错误说清（见 ProfileValidator）。
                AllowCardReward: false,
                HasRoom: string.Equals(timing.Hook, "CombatVictory", StringComparison.Ordinal),
                PlanIdx: idx);
            EmitHookBody(sb, p, g, plan, ctx, hookHasPlay: false, deferGate: false, hasCardReward: hasReward);
            string body2 = sb.ToString();
            map[timing.Hook] = map.TryGetValue(timing.Hook, out string? prevBody)
                ? prevBody + "        // ---- 同一个时刻的第 " + (idx + 1) + " 块 ----\n" + "        {\n" + body2 + "        }\n"
                : body2;
            idx++;
        }
        }
        finally
        {
            _valueDefaultUnit = prevUnit;
        }
        return map;
    }

    // ==================================================================================
    //  V0.2.134：把整张图写进生成产物（唯一的「从工程恢复」依据）
    // ==================================================================================

    /// <summary>
    /// 图标记的**格式版本**。改一次格式就 +1；回读端遇到不认识的版本**必须报错说清**，不许静默丢图。
    /// v1 = <c>// CET:ForgeGraph=1:&lt;JSON 的 Base64&gt;</c>
    /// </summary>
    public const int MarkerVersion = 1;

    /// <summary>
    /// 图标记的写法（单行注释，放在**类级**、紧贴在「===== 锻造节点图」那一段前面）：
    /// <code>// CET:ForgeGraph=1:&lt;图的紧凑 JSON 的 Base64（UTF-8）&gt;</code>
    ///
    /// 为什么先写 JSON 再 Base64（而不是把 JSON 直接塞进注释）：
    ///   · 图上任何一栏（公式原文 / 状态名 / 类名 / 备注）都可能带引号、反斜杠、换行、中文，
    ///     直接塞进去要自己定义一套转义；Base64 之后**保证**只有 <c>A-Za-z0-9+/=</c>，
    ///     单行、无歧义、现成的 <c>Convert.FromBase64String</c> 就能读回来；
    ///   · 长度上 Base64 只多 33%，而这段标记**只在图非空时才写**（见 <see cref="ApplyGraphMarker"/>），
    ///     没有图的存档一个字节都不多。
    /// </summary>
    public const string MarkerPrefix = "// CET:ForgeGraph=";

    /// <summary>把一块布编成一行标记（图是空的 / null 返回 <c>null</c> = 一个字符都不写）。</summary>
    public static string? MarkerOf(ForgeGraphSpec? g)
    {
        if (g is null || g.IsEmpty) return null;
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(g, ProfileFactory.JsonOpts);
        return $"{MarkerPrefix}{MarkerVersion}:{Convert.ToBase64String(json)}";
    }

    /// <summary>
    /// 给一个条目（卡牌 / 遗物 / 药水）把它的图标记追加进 <paramref name="sb"/>。
    /// <paramref name="upgraded"/> 只对卡牌有意义（升级后那块布）。
    ///
    /// 返回是否真的写了（false = 图空 / null，一个字符都没加）。
    /// </summary>
    public static bool ApplyGraphMarker(StringBuilder sb, object owner, bool upgraded = false)
    {
        string? line = MarkerOf(GraphOf(owner, upgraded));
        if (line is null) return false;
        sb.AppendLine();
        sb.AppendLine("    " + line);
        return true;
    }

    /// <summary>
    /// 从一段生成出来的源码里把图标记读回来（没有标记 → <c>null</c>，老工程就是这一档，**不是错误**）。
    ///
    /// 返回 null 的三种情况分得很清楚：
    ///   · 压根没有 <c>CET:ForgeGraph=</c>：老工程 / 没有图的条目 → 照旧（图空、<c>IsForged=false</c>）；
    ///   · 有标记但读不出来（版本不认识 / Base64 坏了 / JSON 坏了）→ <paramref name="problem"/> 里说清原因，
    ///     调用方必须把它报给用户（**不许静默丢图**）。
    /// </summary>
    /// <param name="skip">
    /// 跳过前几个标记（**卡牌有两块布**：源码里第 1 个标记是「升级前」、第 2 个是「升级后」，
    /// 生成顺序见 <c>CSharpCodeGen.CardSource</c> —— 所以读升级后那一块要 skip=1）。
    /// </param>
    public static ForgeGraphSpec? ReadMarker(string source, out string? problem, int skip = 0)
    {
        problem = null;
        Match m = null;
        foreach (Match hit in Regex.Matches(source ?? "", Regex.Escape(MarkerPrefix) + @"(?<ver>\d+):(?<b64>[A-Za-z0-9+/=]*)"))
        {
            if (skip > 0) { skip--; continue; }
            m = hit;
            break;
        }
        if (m is null) return null;
        int ver = int.Parse(m.Groups["ver"].Value, CultureInfo.InvariantCulture);
        if (ver != MarkerVersion)
        {
            problem = $"锻造节点图的标记版本不认识（产物里写的是 v{ver}，这个版本只认 v{MarkerVersion}）—— "
                + "这个工程是**更新的版本**生成的，请用生成它的那个版本去「从工程恢复」，或者直接用手上的存档。";
            return null;
        }
        string b64 = m.Groups["b64"].Value;
        try
        {
            byte[] json = Convert.FromBase64String(b64);
            return JsonSerializer.Deserialize<ForgeGraphSpec>(json, ProfileFactory.JsonOpts);
        }
        catch (Exception ex)
        {
            problem = "锻造节点图的标记读不出来（Base64 / JSON 坏了）：" + ex.Message
                + " —— 这一段大概是被手改坏了；不改它的话「从工程恢复」拿不回这张图"
                + "（图上其它字段不受影响，照旧能恢复）。";
            return null;
        }
    }

    /// <summary>
    /// 把 <paramref name="file"/> 里的图标记读出来贴到 <paramref name="owner"/> 上（认到了返回 true）。
    ///
    /// 认不出来**不抛异常、也不报「没认出来」**：老工程（没有标记）必须照旧跑完，只是图仍然为空。
    /// 只有「有标记但坏了 / 版本不认识」才写进 <see cref="RecoveryResult.Unparsed"/>（**不许静默丢图**）。
    /// </summary>
    /// <param name="portVersionSink">
    /// 认到标记时被置成 <see cref="PortVersion"/>。**这一条是 V0.2.134 的关键**：
    /// 标记里存的是**已经迁到新版端口**的图，而恢复出来的存档
    /// （<see cref="CharacterProfile.ForgeGraphPortVersion"/> 默认 0）一 Load 就会被
    /// <see cref="MigrateIfPorts"/> 再迁一次 —— 那会**按老下标重接【if】的线**，
    /// 把「是 / 否」两支挪位、甚至删掉线，图就不是原来那张了（第二次生成的逐文件对拍会破）。
    /// 所以认到标记的同时必须把这个版本号抬到当前值，让迁移函数直接返回（幂等）。
    /// </param>
    public static bool AttachMarker(string file, object owner, RecoveryResult result, string what,
        bool upgraded = false, Action? portVersionSink = null)
    {
        string? problem;
        var g = ReadMarker(File.ReadAllText(file, Encoding.UTF8), out problem, skip: upgraded ? 1 : 0);
        if (problem is not null)
        {
            result.Unparsed.Add($"{what}：{problem}");
            return false;
        }
        if (g is null) return false;
        portVersionSink?.Invoke();
        switch (owner)
        {
            case CardSpec c when upgraded: c.UpgradedForgeGraph = g; break;
            case CardSpec c: c.ForgeGraph = g; c.IsForged = true; break;
            case RelicSpec r: r.ForgeGraph = g; r.IsForged = true; break;
            case PotionSpec s: s.ForgeGraph = g; s.IsForged = true; break;
            // V0.2.194：自定义状态的布（「从工程恢复」认它 —— 状态没有 IsForged 那一套标记，
            // 图非空本身就说明这只状态由锻造页那块布说了算，见 <see cref="UsesGraph(CustomPowerSpec)"/>）
            case CustomPowerSpec cp: cp.ForgeGraph = g; break;
            default: return false;
        }
        return true;
    }

    /// <summary>
    /// 「生效次数」要用的字段 / 复位钩子（只在这个执行点真的需要时才生成）。
    /// 返回空串 = 不需要（默认档一个字符都不多）。
    ///
    /// V0.2.134：本函数**同时负责写图标记** —— 三个调用点（卡牌 / 遗物 / 药水）都在
    /// 「图非空」的 if 里、而且每个执行点各调一次，所以在这里写标记刚好做到
    /// 「每个条目的每块布各一行、位置固定在锻造那一段的开头」，不用去改三处调用点。
    /// </summary>
    public static string TimingFields(CharacterProfile p, object owner, bool upgraded = false)
    {
        var sb = new StringBuilder();
        ApplyGraphMarker(sb, owner, upgraded);
        sb.Append(TimingFieldsCore(p, owner, upgraded));
        return sb.ToString();
    }

    /// <summary>「生效次数」那一段本身（图标记由 <see cref="TimingFields"/> 负责，见那里的说明）。</summary>
    private static string TimingFieldsCore(CharacterProfile p, object owner, bool upgraded = false)
    {
        var g = GraphOf(owner, upgraded);
        var (plans, _) = SplitByHook(Plans(g, ScopeOf(owner)));
        if (plans.Count == 0) return "";

        var sb = new StringBuilder();
        // 复位钩子要按「本回合 / 本场战斗」**合起来写一个**（每块时机各写一个 = 同一个类里两个同名
        // override，CS0111）—— 所以先把每个作用域要清零的变量收集起来，最后统一生成。
        var resets = new List<(string Hook, string Var)>();
        var allHooks = new HashSet<string>(plans.Select(x => x.Timing.Hook), StringComparer.Ordinal);

        for (int i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            var trig = plan.Trigger;
            var lim = ForgeNumbers.Resolve(p, trig, "CountLimit", 0);
            // 「仅生效 N 次」那一栏现在也能填公式（V0.2.128）：
            //   · 填 0 = 无限次（老行为）：计数字段 / 复位钩子都不生成；
            //   · 常数（含 2*3 这种折叠成常数的公式）：老行为；
            //   · 运行时公式：**按「有限次」生成**（算出来是多少就是多少；算出来是 0 / 负数时
            //     那一支一次都不会跑 —— 校验器会给一条提示）。
            bool runtimeLimit = lim.Runtime;
            int limit = runtimeLimit ? 0 : (int)Math.Round((double)lim.Value);
            if (!runtimeLimit && limit <= 0) continue;

            // 多块【触发时机】时每块各有一个计数器（第一块保持老名字，老图生成文本一个字符不变）
            string uses = "__fgUses" + (i > 0 ? "P" + (i + 1) : "") + (upgraded ? "Up" : "");
            string scope = trig.Get("CountScope", "Combat");
            string comment = runtimeLimit
                ? $"生效次数按公式算（{lim.Text}）"
                : ForgeTimings.CountZh(limit, scope);

            sb.AppendLine();
            // 用 plan 里那条时机自己的中文名（同名时机在卡牌档 / 遗物档显示名不一样，别用全局 Find 猜）
            sb.AppendLine($"    // ===== 锻造节点图【触发时机】{plan.Timing.Display}：{comment} =====");
            sb.AppendLine($"    private int {uses};");

            // 作用域复位：本回合 / 本场战斗开始时清零（「本次游玩」不清零 —— 那正是它的意思）
            if (string.Equals(scope, "Run", StringComparison.Ordinal)) continue;
            bool turn = string.Equals(scope, "Turn", StringComparison.Ordinal);
            // 「本回合」用 AfterPlayerTurnStart 清零；「本场战斗」用 AfterCombatVictory 清零
            // （这一场打完之后重置，下一场就是满次数；比用 BeforeSideTurnStart 干净 ——
            //   那个钩子会被「战斗开始时」这个触发时机用掉，同一个类里写两个会 CS0111 重复定义）。
            string hook = turn ? "AfterPlayerTurnStart" : "AfterCombatVictory";
            resets.Add((hook, uses));
        }
        if (resets.Count == 0) return sb.ToString();

        foreach (string hook in resets.Select(x => x.Hook).Distinct(StringComparer.Ordinal))
        {
            var vars = resets.Where(x => string.Equals(x.Hook, hook, StringComparison.Ordinal))
                             .Select(x => x.Var).ToList();
            bool turn = string.Equals(hook, "AfterPlayerTurnStart", StringComparison.Ordinal);
            // 复位钩子如果正好是**某一支的触发时机**（比如有块【触发时机】就是「每回合开始时」），
            // 就不再另写一遍（同一个类里两个同名 override = CS0111）—— 那一支的计数不会清零，
            // 校验器（Issues）会给一条提示，不静默。
            if (allHooks.Contains(hook)) continue;
            string resetSig = turn
                ? "public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)"
                : "public override async Task AfterCombatVictory(CombatRoom room)";
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>「{(turn ? "本回合" : "本场战斗")}仅生效 N 次」：{(turn ? "每回合" : "每场战斗结束后")}清零。</summary>");
            sb.AppendLine($"    {resetSig}");
            sb.AppendLine("    {");
            foreach (string v in vars) sb.AppendLine($"        {v} = 0;");
            sb.AppendLine("        await Task.CompletedTask;");
            sb.AppendLine("    }");
        }
        return sb.ToString();
    }

    // ==================================================================================
    //  钩子主体
    // ==================================================================================

    /// <summary>
    /// 这个执行点用的方法签名（override 哪个本体钩子）。
    /// <paramref name="methodName"/> 非空 = 不 override，生成一个私有方法（【触发时机】= 卡牌被打出时时用）。
    ///
    /// 第 9 批 ④：**遗物档的签名只留一份** —— 直接走 <see cref="CSharpCodeGen.RelicTriggerSignature"/>，
    /// 和遗物页那份逐字相同（以前这里自己抄了一套，两套会漂：例如 Attacked 那条抄漏了
    /// <c>MegaCrit.Sts2.Core.Commands.Builders.</c> 前缀，生成的工程 CS0246）。
    /// 「当遗物被点击时」/「血量低于 N」是锻造页专属的两条，签名照旧留在下面那个 switch 里。
    /// </summary>
    private static string Signature(ForgeTimings.TimingOption t, EmitCtx ctx, string? methodName = null)
    {
        if (methodName is not null)
        {
            // V0.2.169：嵌进卡牌自己钩子的那种 —— 参数照**调用方**来（只有一个 choiceContext，
            // 钩子那套参数在这儿不存在）。见 EmitCtx.NoHookParams 的说明。
            if (ctx.Scope == "Card" && ctx.NoHookParams)
                return $"public async Task {methodName}(PlayerChoiceContext choiceContext)";
            return ctx.Scope == "Card"
                ? $"public async Task {methodName}(PlayerChoiceContext choiceContext, CardPlay cardPlay)"
                : HasRoomHook(ctx) ? $"public async Task {methodName}(CombatRoom room)"
                : $"public async Task {methodName}()";
        }
        if (string.Equals(ctx.Scope, "Relic", StringComparison.Ordinal)
            && !string.Equals(t.Id, ForgeTimings.RelicClicked, StringComparison.Ordinal)
            && !string.Equals(t.Id, ForgeTimings.HpBelow, StringComparison.Ordinal))
            return CSharpCodeGen.RelicTriggerSignature(t.Id);
        return t.Hook switch
        {
            "OnPlay" => "public override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)",
            "Use" => "protected override async Task OnUse(PlayerChoiceContext choiceContext, Creature? target)",
            // 下面这些签名都对着本体源码 / 已生成代码核过（AfterCardExhausted / AfterCardDrawn 的最后一个参数别忘）
            "AfterCardExhausted" => "public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)",
            "AfterCardDiscarded" => "public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)",
            "AfterCardDrawn" => "public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)",
            // 卡牌时机「被保留」（用户要求 V0.2.128）：本体**没有** AfterCardRetained 之类的钩子，
            // 走 AbstractModel.AfterFlush —— 签名和 CSharpCodeGen.EmitCardTimingHooks 的 case "Retained" 逐字相同
            //（不漏这一条的话会落到最后那个默认分支，生成 `AfterFlush(PlayerChoiceContext)` 这个**不存在的签名**，
            //  整个工程 CS0114 / CS0115）。
            "AfterFlush" => "public override async Task AfterFlush(PlayerChoiceContext choiceContext, Player player, IReadOnlyCollection<CardModel> flushedCards, IReadOnlyCollection<CardModel> retainedCards)",
            "AfterCardPlayed" => "public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)",
            "AfterObtained" => "public override async Task AfterObtained()",
            "BeforeSideTurnStart" => "public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)",
            "AfterPlayerTurnStart" => "public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)",
            // V0.2.179（用户要求「触发时机加一个回合开始后」）：回合开始那三个时刻是**三个不同**的钩子方法
            //（本体 Hook.AfterPlayerTurnStart 里连着派发三次，见 Hook.cs:894-917）—— 遗物档会走上面
            // RelicTriggerSignature 那条（三条都在那份 switch 里），这里补的是**没走遗物档**时的兜底。
            "AfterPlayerTurnStartEarly" => "public override async Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)",
            "AfterPlayerTurnStartLate" => "public override async Task AfterPlayerTurnStartLate(PlayerChoiceContext choiceContext, Player player)",
            "AfterSideTurnEnd" => "public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)",
            "AfterCombatVictory" => "public override async Task AfterCombatVictory(CombatRoom room)",
            "AfterDamageReceived" => "public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)",
            // ⚠ AttackCommand 必须写**全名**：它在 MegaCrit.Sts2.Core.Commands.Builders，
            // 而生成的工程 GlobalUsings 里没有这个命名空间 → 写 `AttackCommand` 会 CS0246
            //（用户实测报过：PlatonicGirlRelic2.cs(19,79) 找不到类型 AttackCommand）。
            // 卡牌那条链（CSharpCodeGen 的 RelicTriggerSignature）和状态那条链（CustomPowerGen:503）都是全名。
            "AfterAttack" => "public override async Task AfterAttack(PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Commands.Builders.AttackCommand command)",
            "AfterGoldGained" => "public override async Task AfterGoldGained(Player player)",
            "AfterShuffle" => "public override async Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler)",
            "AfterCurrentHpChanged" => "public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)",
            // 遗物点击：RelicModel 上没有这个方法（本体只给界面节点留了回调），
            // 所以生成一个普通方法，由非常窄的 Harmony 补丁按类名转发过来。
            "ForgeOnRelicClicked" => "public async Task ForgeOnRelicClicked()",
            _ => "public override async Task " + t.Hook + "(PlayerChoiceContext choiceContext)",
        };
    }

    private static void EmitHook(StringBuilder sb, CharacterProfile p, ForgeGraphSpec g, HookPlan plan,
        EmitCtx ctx, string? methodName = null, IReadOnlyList<HookPlan>? extra = null)
    {
        // 【卡牌奖励】只能挂在「战斗胜利时」那一支上（那一支的钩子签名里带 CombatRoom room）
        var (chainEffects, hasCardReward) = WalkChain(g, plan.Trigger);
        // V0.2.176：「转化为先古卡」要在**这个方法末尾**弹一次本体那个预览界面 ——
        // 所以先看这条链上有没有它（收集列表的声明也按这个判）。
        bool ancientPreview = chainEffects.Any(n => n?.EffectSpec?.Kind == "AncientTranscend"
            || (n is not null && string.Equals(n.Get("Effect"), "AncientTranscend", StringComparison.Ordinal)));
        bool canReward = hasCardReward && string.Equals(plan.Timing.Hook, "AfterCombatVictory", StringComparison.Ordinal);
        // 「战斗结束时（房间结算之前）」的钩子签名里同样带 CombatRoom room（本体 AfterCombatEnd(room)）——
        // 第 9 批 ④ 把这条补进遗物档菜单后，图嵌进那个钩子时也必须带上 room（不然调用处传 room 会 CS1501）。
        ctx = ctx with
        {
            HasRoom = plan.Timing.Hook is "AfterCombatVictory" or "AfterCombatEnd",
        };
        if (methodName is null && !ctx.HasRoom) ctx = ctx with { AllowCardReward = false };
        else if (canReward) ctx = ctx with { AllowCardReward = true };

        sb.AppendLine($"    {Signature(plan.Timing, ctx, methodName)}");
        sb.AppendLine("    {");
        // V0.2.176：「转化为先古卡」的预览收集列表（这条链上用到它才声明）
        if (ancientPreview)
        {
            var decl = new CodeWriter();
            CSharpCodeGen.EmitAncientPreviewDecl(decl);
            foreach (string line in decl.ToString().Replace("\r\n", "\n").Split('\n'))
                if (line.Trim().Length > 0) sb.AppendLine("        " + line);
        }
        // 方法体起点：卡牌档挂在**没有 cardPlay 参数**的钩子上时（被消耗 / 被丢弃 / 被抽到 / 被保留），
        // 最后要按「生成出来的文本有没有引用 cardPlay」决定往这里插一句 `CardPlay? cardPlay = null;`
        //（和 CSharpCodeGen.EmitCardPlayPlaceholder 同一套做法，见那里的说明）。插回去而不是重写一遍，
        // 是为了让默认档（有 cardPlay 的那些时机）的输出**一个字符都不变**。
        int bodyMark = sb.Length;
        // 嵌进卡牌自己钩子里的那种（NoHookParams）拿不到 cardPlay 参数 → 和「钩子本来就没有 cardPlay」
        // 那几档走**同一条**路（占位 `CardPlay? cardPlay = null;` + `cardPlay?.Target`）。
        bool hookHasPlay = HookHasCardPlay(plan.Timing) && !ctx.NoHookParams;
        // V0.2.170：「斩杀」条件的赋值要用「打出时选中的那个目标」——只有这个执行点真的拿得到
        // cardPlay 参数时才那么写；拿不到的那几档（被消耗 / 被丢弃 / 嵌进卡牌钩子）只能是 false，
        // 不然 `cardPlay.Target` 会落在占位 `CardPlay? cardPlay = null;` 上（CS8602，而且斩杀会误成立）。
        ctx = ctx with
        {
            RealCardPlay = hookHasPlay,
            // V0.2.194：「本张牌这一次打出造成的伤害」只在**打出这张牌**那个执行点上记账
            IsCardPlay = string.Equals(ctx.Scope, "Card", StringComparison.Ordinal)
                         && string.Equals(plan.Timing.Hook, "OnPlay", StringComparison.Ordinal),
        };

        // ---- 0.0) 归属守卫（第 9 批 ④）：遗物档的图走**和遗物页同一套**判据 ----
        // 为什么必须补：锻造页以前一条守卫都没有 —— 敌人打我 / 敌人打牌 / 别人掉血
        // 也会触发「你自己的」锻造遗物。行文本和 CSharpCodeGen.RelicSource 里那个 switch **等价**
        //（见 CSharpCodeGen.ForgeTriggerGuardLines），缩进 8 空格对齐这个方法体。
        //
        // ⚠ 嵌进遗物自己触发钩子的那种（methodName = ForgeGraphOnTrigger）**不写**：
        //   · 外面那个钩子里 RelicSource 的守卫 switch 已经判过同一条（嵌进来的条件就是「同一个钩子」）；
        //   · 那个普通方法**没有**钩子参数（cardPlay / creature / card…），写了会 CS0103。
        if (methodName is null && string.Equals(ctx.Scope, "Relic", StringComparison.Ordinal))
        {
            // 「到达 N 层」那一栏现在也能写公式：写成运行时表达式就内联展开（这一句在序幕之前，
            // 只能依赖 base.Owner —— 遗物档的 __fgPlayer / __fgSelf / __fgTarget 正好就是它，
            // 所以内联写法和平常那套**等价**，见 ForgeNumbers.Place.Inline）。
            var amountField = ForgeNumbers.Resolve(p, plan.Trigger, "PowerAmount", 1,
                ForgeNumbers.Place.Inline, playTargetExpr: "null");
            foreach (string line in CSharpCodeGen.ForgeTriggerGuardLines(plan.Timing.Id,
                         plan.Trigger.Get("PowerId"), (decimal)plan.Trigger.GetNumber("PowerAmount", 1),
                         amountField.Runtime ? amountField.Expr : null,
                         NormalizeDirection(plan.Trigger.Get("PowerDirection"))))
                sb.AppendLine("        " + line);
        }

        // ---- 0.05) 时机自己的进门守卫（V0.2.128 新增「这张牌被保留时」）----
        // 「被保留」不是本体的虚方法（本体没有 AfterCardRetained），挂在 AfterFlush 上，
        // 所以方法体最前面要先判「这张牌真的被保留了」——判据和卡牌页那份逐字相同
        //（CSharpCodeGen.EmitCardTimingHooks 的 case "Retained"：retainedCards.Contains(this)）。
        // 嵌进卡牌自己的钩子时**不写**：外层那个钩子（CSharpCodeGen.EmitCardTimingHooks /
        // EmitSelfDrawHook）里已经判过同一条，而且这里根本没有 card / player / retainedCards 那几个参数，
        // 写了就是 CS0103（和上面遗物嵌进触发钩子那一段同一个理由）。
        if (!ctx.NoHookParams)
            foreach (string line in TimingEntryGuardLines(plan.Timing, ctx.Scope))
                sb.AppendLine("        " + line);

        // ---- 0) 血量阈值 / 生效次数（遗物的「当遗物被点击时」「血量低于 N」用得到）----
        // 注意：这一段在「自己 / 目标」那两个变量之前，所以里面只用 base.Owner 推算，
        // 不能引用 __fgSelf（会「在使用之前声明」编译不过）。
        //
        // V0.2.128：这两栏现在也能写公式。写了**运行时**公式时，它要用到序幕里算好的
        // __X / __Y / __pM / __fgTarget（用户要的「和【数值】模块同一套可用量」），
        // 所以把它挪到序幕之后再写 —— 序幕只是一串纯赋值（没有任何副作用），顺序换了语义不变；
        // 没写公式（默认档）时**还是老位置老文本**，一个字符都不动（红线）。
        bool deferGate = GateUsesFormula(p, plan);
        if (!deferGate) EmitTimingGate(sb, plan, ctx, p);

        // ---- 0.5) 需要 choiceContext、但钩子签名里没有的时机：按本体的做法自己造一个 ----
        // （见 EmitCtx.CtxFrom / RelicCtxFrom；「获得时」用阻塞式，战斗里那几条用 HookPlayerChoiceContext。
        //   不造的话效果里会引用一个不存在的变量（CS0103），或者传 null! 在运行时炸。）
        if (ctx.NeedsCtxBootstrap)
        {
            if (string.Equals(ctx.CtxFrom, "Blocking", StringComparison.Ordinal))
            {
                sb.AppendLine("        // 这个钩子（AfterObtained）本体没给 choiceContext：按本体的做法自己造一个阻塞式的");
                sb.AppendLine("        // （本体的 BlockingPlayerChoiceContext 注释里点名的第一个场景就是「遗物 AfterObtained 回调」）");
                sb.AppendLine("        var choiceContext = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();");
            }
            else
            {
                sb.AppendLine("        // 这个钩子本体没给 choiceContext（战斗里那几条）：按本体 Hook.cs 的做法造一个 HookPlayerChoiceContext");
                sb.AppendLine("        // —— 战斗里**不能**用 BlockingPlayerChoiceContext（那是局外的阻塞式上下文，会卡住）");
                sb.AppendLine("        // 遗物的 Owner 就是 Player，所以用「(source, owner, netId, type)」这个重载（不用传 CombatState）。");
                sb.AppendLine("        ulong __netId = MegaCrit.Sts2.Core.Context.LocalContext.NetId ?? 0UL;");
                sb.AppendLine("        var choiceContext = new MegaCrit.Sts2.Core.GameActions.Multiplayer.HookPlayerChoiceContext(this, base.Owner, __netId, MegaCrit.Sts2.Core.Entities.Multiplayer.GameActionType.Combat);");
            }
        }

        // ---- 1) 序幕 + 2) 执行链：V0.2.194 抽成 <see cref="EmitHookBody"/>（「状态列表」那一类布
        //     复用同一份正文；方法壳那条链由 CustomPowerGen 写，见 ForgePowerGraph.PowerHookBodies）。
        //      对卡牌 / 药水 / 遗物来说，这一句 = 原来那整段，文本一个字符都不变。
        EmitHookBody(sb, p, g, plan, ctx, hookHasPlay, deferGate, hasCardReward);

        // V0.2.279（用户报「两块【触发时机】落在同一个钩子上，第 2 块白配了」）：同一个本体钩子上的
        // 其余几块**合并进这一个方法体**，每块各包一层 { }（局部变量同名也不冲突）。两块都会执行 ——
        // 「每回合开始时」这种时刻，正确语义本来就是那一刻两块都跑。
        if (extra is not null)
        {
            for (int __ex = 0; __ex < extra.Count; __ex++)
            {
                var exPlan = extra[__ex];
                var (_, exReward) = WalkChain(g, exPlan.Trigger);
                bool exPlay = HookHasCardPlay(exPlan.Timing) && !ctx.NoHookParams;
                sb.AppendLine($"        // ---- 同一个时刻（{exPlan.Timing.Display}）的第 {__ex + 2} 块 ----");
                sb.AppendLine("        {");
                EmitHookBody(sb, p, g, exPlan, ctx with { PlanIdx = __ex + 1 }, exPlay, false,
                    exReward && string.Equals(exPlan.Timing.Hook, "AfterCombatVictory", StringComparison.Ordinal));
                sb.AppendLine("        }");
            }
        }

        // 卡牌档挂在**没有 cardPlay 参数**的钩子上、而且生成出来的代码真的用到了它（DamageCmd 的 FromCard /
        // CreatureCmd.GainBlock 的来源卡参数…）：补一句 `CardPlay? cardPlay = null;`
        //（本体那几条 API 允许传 null；真会解引用 cardPlay.Target 的用法校验器会拦，见 ProfileValidator）。
        // 判据是**生成出来的文本**，不是「按效果种类猜」——猜错会平白多一个用不上的局部变量（CS0219）。
        if ((methodName is null || ctx.NoHookParams) && string.Equals(ctx.Scope, "Card", StringComparison.Ordinal) && !hookHasPlay
            && sb.ToString(bodyMark, sb.Length - bodyMark).Contains("cardPlay", StringComparison.Ordinal))
        {
            var ins = new StringBuilder();
            ins.AppendLine("        // 这些钩子里没有「打出记录」（cardPlay）：下面把它传 null（那几条本体 API 允许传 null），");
            ins.AppendLine("        // 用 ?. 取「打出的目标」；真会解引用 cardPlay.Target 的用法由校验器拦住。");
            ins.AppendLine("        CardPlay? cardPlay = null;");
            sb.Insert(bodyMark, ins.ToString());
        }

        // V0.2.176：「转化为先古卡」的预览界面 —— 这条链的效果都跑完了才弹（收集列表在上面声明过）
        if (ancientPreview)
        {
            var show = new CodeWriter();
            CSharpCodeGen.EmitAncientPreviewShow(show);
            foreach (string line in show.ToString().Replace("\r\n", "\n").Split('\n'))
                if (line.Trim().Length > 0) sb.AppendLine("        " + line);
        }

        sb.AppendLine("    }");
    }

    /// <summary>
    /// 执行点的**正文**：序幕（自己 / 目标 / 参数 / X / Y）+ 「执行链」那一段。
    ///
    /// V0.2.194 从 <see cref="EmitHook"/> 里**原样抽出来**（一个字符都没改），因为「状态列表」那一类布
    /// （锻造页用节点图编自定义状态）要用**同一份正文** —— 它的方法壳（签名 / 归属守卫 / choiceContext /
    /// 衰减移除）由「自定义状态」页那条链写（见 <c>CustomPowerGen.Source</c> + <c>ForgePowerGraph</c>），
    /// 正文沿用这里，免得两份慢慢漂。
    /// </summary>
    private static void EmitHookBody(StringBuilder sb, CharacterProfile p, ForgeGraphSpec g, HookPlan plan,
        EmitCtx ctx, bool hookHasPlay, bool deferGate, bool hasCardReward)
    {
        // ---- 1) 先算「自己 / 目标 / 参数 / X / Y」----
        sb.AppendLine("        // ---- 锻造节点图：执行点先算好这几个值（后面的公式只引用它们）----");
        // V0.2.194：状态（PowerModel）这一档的 base.Owner 是 **Creature**（卡牌 / 遗物 / 药水那边是 Player）——
        // 所以「玩家」要多走一步 base.Owner.Player，「自己」就是 base.Owner 本身。
        if (string.Equals(ctx.Scope, ForgeTimings.PowerScope, StringComparison.Ordinal))
        {
            sb.AppendLine("        // 状态挂在生物身上（base.Owner 是 Creature）：玩家那一侧取 base.Owner.Player");
            sb.AppendLine("        Player __fgPlayer = base.Owner.Player!;");
            sb.AppendLine("        Creature __fgSelf = base.Owner;");
            // 「打谁」的默认档：状态没有「打出时选中的目标」，所以默认取第一个可打的敌人
            //（和「自定义状态」页那条老链同一个口径：EmitFoeBlock 的兜底就是它）。
            // V0.2.312（用户报「打帝王蟹，打右钳结果给的左钳」）：「突破格挡时」这个钩子自带
            // `target`（= 被打穿格挡的那个单位），图上没接【对象】时就该打它，而不是第一个敌人。
            sb.AppendLine(plan.Timing.Id == "BlockBroken"
                ? "        Creature? __fgTarget = target;   // 突破格挡的那个单位（钩子参数），不是「第一个敌人」"
                : "        Creature? __fgTarget = base.Owner.CombatState?.HittableEnemies.FirstOrDefault();");
        }
        else
        {
            sb.AppendLine("        Player __fgPlayer = base.Owner;");
            sb.AppendLine("        Creature __fgSelf = __fgPlayer.Creature;");
            sb.AppendLine(ctx.Scope == "Card"
                // 卡牌那几个**没有 cardPlay 参数**的钩子（被消耗 / 被丢弃 / 被抽到 / 被保留）：
                // 用 ?. 取目标（那一档下面会插一句 `CardPlay? cardPlay = null;`），
                // 不然生成出来是 CS8602（对可能为 null 的引用解引用）。
                ? (hookHasPlay ? "        Creature? __fgTarget = cardPlay.Target;" : "        Creature? __fgTarget = cardPlay?.Target;")
                : "        Creature? __fgTarget = null;");
        }
        // 没有 choiceContext 的时机（「获得时」「遗物被点击」以及造不出来的那几条）：效果里不引用那个名字，
        // 需要它的那几种效果会传 null!（本体自己在运行时会拦下并报错，比默默不做事清楚）——
        // 校验器对「造不出来 + 图里有需要 context 的效果」这一组合会**报错**拦住，不静默生成坏代码。
        if (string.Equals(ctx.CtxFrom, "None", StringComparison.Ordinal))
        {
            sb.AppendLine("        // 注意：这个执行点的钩子本体没给 choiceContext，也造不出来 —— 需要它的效果会传 null!");
        }

        // V0.2.170：「斩杀」条件（用户点的第 3 条）—— 图里用到它时，在这个执行点的序幕里声明
        // 「上一条攻击」那两个局部变量（和卡牌页 OnPlay 开头那份逐字相同），
        // 赋值在「造成伤害」那一条效果上（见 EmitEffect 的 fatalCap）。
        // 只有卡牌档 + 拿得到 cardPlay 的时机才声明得了；别的组合由校验器报错拦住。
        if (ctx.Scope == "Card" && GraphUsesFatal(g))
        {
            sb.AppendLine("        // 斩杀（本体 Fatal）：__lastAttack = 上一条攻击的结果；__fatalOk = 打之前记下的「目标允许触发斩杀」");
            sb.AppendLine("        MegaCrit.Sts2.Core.Commands.Builders.AttackCommand? __lastAttack = null;");
            sb.AppendLine("        bool __fatalOk = false;");
        }

        foreach (var param in UsedParams(p, g))
        {
            string name = ParamNames.Normalize(param.Name);
            sb.AppendLine($"        decimal {ForgeFormula.ParamVar(name)} = {ForgeFormula.ParamExpr(param, p)};   // {name}= {param.Summary}");
        }

        // X = 当前所有能量 / Y = 当前所有额外资源量。
        //
        // ⚠ 这里**不再**自己扣费用：左上角那个【能量】模块填的就是**这张牌的费用**，
        // 生成时会把它声明成卡牌自己的费用（能量 → 构造函数 / 额外资源量 → `CanonicalStarCost`），
        // 本体会在打出前扣掉（`PlayCardAction.ExecuteAction` = 先 `SpendResources()` 再 `OnPlay`）——
        // 所以到这里 `Energy` / `Stars` 已经是「扣完这张牌费用」之后的值，正好还是以前那个数
        //（以前是「没扣 − 费用」，值一样，只是卡面显示成 0 费、也不拦「能量不够」）。
        var energyNode = g.Nodes.FirstOrDefault(n => n is not null && n.Kind == "Energy");
        int energyCost = (int)Math.Round(energyNode?.GetNumber("EnergyCost") ?? 0);
        int resourceCost = (int)Math.Round(energyNode?.GetNumber("ResourceCost") ?? 0);
        // V0.2.215：费用那一栏写 X / Y（= 消耗全部能量 / 全部额外资源量）时，卡牌本身已经按 X 费生成
        // （HasEnergyCostX + 构造函数写 0），打出时能量被扣光 —— 所以这里的 X / Y 必须是
        // 「**消耗掉**了多少」，走本体 ResolveEnergyXValue() / ResolveStarXValue()，不能读剩余（那时是 0）。
        bool costIsX = string.Equals(ctx.Scope, "Card", StringComparison.Ordinal) && IsEnergyCostX(g);
        bool starCostIsX = string.Equals(ctx.Scope, "Card", StringComparison.Ordinal) && IsResourceCostX(g);
        int costXPlus = costIsX ? EnergyCostXPlus(g) : 0;
        if (costIsX || starCostIsX)
        {
            sb.AppendLine("        // X / Y：需求里点名的两栏 —— X = 当前所有能量，Y = 当前所有额外资源量");
            if (costIsX)
            {
                string plus = costXPlus > 0 ? $" + (base.IsUpgraded ? {costXPlus} : 0)" : "";
                sb.AppendLine($"        // 这张牌的费用是 X（【能量】模块里写的）→ X = 打出时**消耗掉的**能量，本体 ResolveEnergyXValue 就是它");
                sb.AppendLine($"        decimal __X = base.ResolveEnergyXValue(){plus};");
            }
            if (starCostIsX)
            {
                sb.AppendLine("        // 这张牌的额外资源量费用是 Y → Y = 打出时**消耗掉的**额外资源量");
                sb.AppendLine("        decimal __Y = base.HasStarCostX ? (decimal)base.ResolveStarXValue() : 0m;");
            }
            if (!costIsX) sb.AppendLine("        decimal __X = ForgeFormulaRuntime.Energy(__fgPlayer);");
            if (!starCostIsX) sb.AppendLine("        decimal __Y = ForgeFormulaRuntime.Stars(__fgPlayer);");
        }
        else if (energyCost <= 0 && resourceCost <= 0)
        {
            // 没填费用（老存档 / 还没动过那块）：**逐字保留老写法**（红线：默认配置一个字都不变）
            sb.AppendLine("        // X / Y：需求里点名的两栏 —— X = 当前所有能量，Y = 当前所有额外资源量");
            sb.AppendLine("        decimal __X = ForgeFormulaRuntime.Energy(__fgPlayer) - 0m;");
            sb.AppendLine("        decimal __Y = ForgeFormulaRuntime.Stars(__fgPlayer) - 0m;");
        }
        else
        {
            sb.AppendLine("        // X / Y：需求里点名的两栏 —— X = 当前所有能量，Y = 当前所有额外资源量");
            sb.AppendLine($"        // 【能量】模块费用：{energyCost} 能量 + {resourceCost} 额外资源量 —— 已声明成**卡牌自己的费用**")
             .AppendLine("        // （能量写在卡牌构造函数里，额外资源量是 CanonicalStarCost），本体打出前就扣掉了，这里不再扣第二次");
            sb.AppendLine("        decimal __X = ForgeFormulaRuntime.Energy(__fgPlayer);");
            sb.AppendLine("        decimal __Y = ForgeFormulaRuntime.Stars(__fgPlayer);");
        }

        // ---- 1.5) 战斗胜利时：房间那个变量要留着给【卡牌奖励】用 ----
        if (hasCardReward && ctx.AllowCardReward && !string.Equals(ctx.Scope, "Card", StringComparison.Ordinal))
            sb.AppendLine("        // 「战斗胜利时」的钩子签名里带着这个房间 —— 【卡牌奖励】要往它身上加一条奖励");

        // ---- 1.9) 血量阈值 / 生效次数（写了运行时公式的那一档，见上面 deferGate 的说明）----
        // 放在这里 = 序幕之后，所以 X / Y / 自定义参数 / 内置只读量 / __fgTarget 全都能用。
        if (deferGate) EmitTimingGate(sb, plan, ctx, p);

        // ---- 2) 执行链：从【触发时机】的出口往后走，if / 且 / 或 真正接管分支 ----
        var state = new GenState();
        var triggerOut = g.Outgoing(plan.Trigger.Id, 0).FirstOrDefault();
        if (triggerOut is null)
        {
            sb.AppendLine();
            sb.AppendLine("        // 【触发时机】右边还没接线：这张图现在是空的（接到【效果】上才会做事）。");
        }
        else
        {
            var next = g.Node(triggerOut.To);
            if (next is not null) EmitChain(sb, p, g, next, ctx, state, "        ");
        }
        // V0.2.216：图上有「会做事」的模块根本没接到这条链上 → 写一段注释说清（不静默失效）
        EmitUnreachableNote(sb, g, "        ");
    }

    /// <summary>
    /// 这个卡牌档时机的钩子签名里有没有 <c>CardPlay cardPlay</c> 参数。
    /// 只有「卡牌被打出时」（OnPlay）和「卡牌被自动打出时」（AfterCardPlayed）有；
    /// 被消耗 / 被丢弃 / 被抽到 / **被保留（AfterFlush）** 都没有 —— 那几档要自己补一句占位（见 EmitHook 末尾）。
    /// </summary>
    private static bool HookHasCardPlay(ForgeTimings.TimingOption t) =>
        t.Hook is "OnPlay" or "AfterCardPlayed";

    /// <summary>
    /// 【触发时机】自己的进门守卫（V0.2.128：「卡牌被保留时」用得到）。
    /// 默认（没有守卫的时机）返回空 = 生成文本一个字符都不变。
    ///
    /// V0.2.168 补：卡牌档那几条**本体广播给场上所有模型**的钩子（被消耗 / 被丢弃 / 被抽到）也要先判
    /// 「广播过来的这张牌就是**我**」—— 以前只有遗物档有归属守卫，卡牌档没有，于是「这张牌被消耗时」
    /// 会在**任何**一张牌被消耗时触发（用户配了「被消耗时抽 1 张」就会变成每消耗一张牌都抽）。
    /// 判据和卡牌页「卡牌时机」那一份（<c>CSharpCodeGen.EmitCardTimingHooks</c>）**逐字相同**。
    /// </summary>
    private static IReadOnlyList<string> TimingEntryGuardLines(ForgeTimings.TimingOption t, string scope) => t.Hook switch
    {
        "AfterCardExhausted" or "AfterCardDiscarded" when string.Equals(scope, "Card", StringComparison.Ordinal) => new[]
        {
            "// 本体这个钩子会广播给场上**所有**模型（不只是被消耗 / 被丢弃的那张牌），所以先判就是我",
            "if (card != this || base.CombatState is null) return;",
        },
        "AfterCardDrawn" when string.Equals(scope, "Card", StringComparison.Ordinal) => new[]
        {
            "// 抽牌同样广播给所有模型：只认被抽到的那张就是我",
            "if (card != this || base.CombatState is null) return;",
        },
        "AfterFlush" => new[]
        {
            "// 「这张牌被保留时」：本体没有 AfterCardRetained 钩子，用 AfterFlush 交出来的 retainedCards 判自己",
            "//（判据 = 本体 CombatManager.FlushPlayerHand 把这张牌归进 retainedCards：有保留关键词 / 这回合被单回合保留 / 整手牌都不丢）",
            "if (player != base.Owner || base.CombatState is null) return;",
            "if (!retainedCards.Contains(this)) return;   // 这张牌这回合没被保留（被丢进弃牌堆了）",
        },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// 「血量低于 N」/「仅生效 N 次」这两栏是不是写了**运行时**公式
    /// （写了的话进门条件要挪到执行点序幕之后才写，见 EmitHook）。
    /// </summary>
    private static bool GateUsesFormula(CharacterProfile p, HookPlan plan)
        => (plan.Timing.NeedHp && ForgeNumbers.Resolve(p, plan.Trigger, "HpThreshold", 0.5).Runtime)
        || ForgeNumbers.Resolve(p, plan.Trigger, "CountLimit", 0).Runtime;

    /// <summary>
    /// 顺着执行链走：一个模块之后所有出口接到的模块，**按顺序依次执行**。
    ///
    /// 各类模块在链里的角色：
    ///   · 【效果】 = 做事（走完继续往后）；
    ///   · 【if】   = 分叉：先按「否 / 是」两个出口各自成一条 if / else 分支，再往后走；
    ///   · 【且】【或】= 条件成立才继续（不成立就停在这一支）；
    ///   · 【条件】 = 判断成立才继续（可以当作「只在这时干活」的门）；
    ///   · 【对象】【状态】【数值】【能量】= 不是执行模块，只在被【效果】/【if】取用时才算。
    /// </summary>
    private static void EmitChain(StringBuilder sb, CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node,
        EmitCtx ctx, GenState st, string indent)
    {
        if (st.Depth > 48)
        {
            sb.AppendLine(indent + "// ⚠ 这条链太深 / 可能绕成环了，生成到这里就停了（把线理顺一下）。");
            return;
        }
        if (!st.Chained.Add(node.Id))
        {
            sb.AppendLine($"{indent}// 提示：模块 fg:{node.Id} 在链上被指到两次，第二次不再重复执行。");
            return;
        }

        switch (node.Kind)
        {
            // 【指向对象】（用户要求：紧接【触发时机】）：它声明**这张牌在游戏里选中谁**，
            // 于是它成了这条链上「没自己接【对象】的效果」的默认目标。
            // 卡牌那个 TargetType（= cardPlay.Target 有没有值）由 DeclaredTargetType 单独读它，不在这里生成代码。
            case "Target":
            {
                bool already = st.CurrentUnitSet;
                st.CurrentUnit = ResolveUnit(g, node);
                st.CurrentUnitSet = true;
                if (already)
                    sb.AppendLine($"{indent}// 提示：这一支上出现了第二个【指向对象】，后面没自己接【对象】的效果按**这一个**算。");
                sb.AppendLine($"{indent}// fg:{node.Id}【指向对象】{ForgeGraphCatalog.UnitZh(node.Get("Pick", "Self"))}：这条链上没自己接【对象】的效果默认打在它身上");
                break;
            }

            case "Effect":
                EmitEffect(sb, p, g, node, ctx, st, indent);
                break;

            case "If":
            {
                string cond = BoolExpr(p, g, node, "Cond", st, sb, indent, "false", ctx.Scope);
                bool hasTrue = HasOut(g, node, "True");
                bool hasFalse = HasOut(g, node, "False");
                if (!hasTrue && !hasFalse)
                {
                    sb.AppendLine($"{indent}// 【if】两个出口都没接线：这一支什么都不做");
                    return;
                }
                if (hasTrue && hasFalse) sb.AppendLine(indent + $"if ({cond})");
                else if (hasTrue) sb.AppendLine(indent + $"if ({cond})");
                else sb.AppendLine(indent + $"if (!({cond}))");
                sb.AppendLine(indent + "{");
                EmitInScope(sb, p, g, node, hasTrue ? "True" : "False", ctx, st, indent + "    ");
                sb.AppendLine(indent + "}");
                if (hasTrue && hasFalse)
                {
                    sb.AppendLine(indent + "else");
                    sb.AppendLine(indent + "{");
                    EmitInScope(sb, p, g, node, "False", ctx, st, indent + "    ");
                    sb.AppendLine(indent + "}");
                }
                return;      // 两个分支已经各自走完，别再顺出口走一遍
            }

            // V0.2.285（用户要求）：【While】= 条件成立就一直重复「循环」那条子链；条件每轮重新求值。
            // 上限 50 轮（玩家条件很容易永远成立 → 必须防卡死）。「结束」出口接循环之后的流程。
            case "While":
            {
                string cond = BoolExpr(p, g, node, "Cond", st, sb, indent, "false", ctx.Scope);
                if (!HasOut(g, node, "Loop"))
                {
                    sb.AppendLine($"{indent}// 【while】「循环」出口没接线：这一块什么都不做");
                    EmitInScope(sb, p, g, node, "Done", ctx, st, indent);
                    return;
                }
                sb.AppendLine(indent + "// 【while】条件成立就一直跑「循环」那条子链（最多 50 轮，防卡死）");
                sb.AppendLine(indent + "int __fgLoopGuard = 0;");
                sb.AppendLine(indent + $"while ({cond})");
                sb.AppendLine(indent + "{");
                sb.AppendLine(indent + "    if (++__fgLoopGuard > 50) { MegaCrit.Sts2.Core.Logging.Log.Warn(\"[模组调试] 【while】跑了 50 轮还没结束，强制跳出（检查你的条件）\"); break; }");
                EmitInScope(sb, p, g, node, "Loop", ctx, st, indent + "    ");
                sb.AppendLine(indent + "}");
                // 循环之后：「结束」出口那条链继续（没接就到此为止）
                if (HasOut(g, node, "Done")) EmitInScope(sb, p, g, node, "Done", ctx, st, indent);
                return;
            }

            case "And" or "Or":
            {
                // ⚠ V0.2.216（用户实测报的「处于二形态时触发不了多效卡效果只能打24的基础效果」）：
                // 以前这里写的是 BoolExpr(node, "Out") —— 那取的是**「Out」这个口上接进来的那一块**，
                // 也就是【且】的第一个输入，而不是【且】自己的条件。于是「(A && B) → 再串一个【且】C」这种
                // 图会生成 `if (A) { if ((A && B)) { … } }` —— **C 被整个丢掉**（且的第二个输入消失）。
                // 现在改成查这个模块**自己**的条件（BoolVar），三层「且」也一个不落。
                string cond = BoolVar(p, g, node, st, sb, indent, "true", ctx.Scope);
                if (!HasOut(g, node, "Out"))
                {
                    sb.AppendLine($"{indent}// 【{(node.Kind == "And" ? "且" : "或")}】右边没接线：这一支什么都不做");
                    return;
                }
                sb.AppendLine(indent + $"if ({cond})");
                sb.AppendLine(indent + "{");
                EmitInScope(sb, p, g, node, "Out", ctx, st, indent + "    ");
                sb.AppendLine(indent + "}");
                return;      // 已经由 EmitPortChain 走过出口，别再走一遍
            }

            case "Condition":
            {
                // 同上：查这一块【条件】自己的内容，而不是它 Out 口上接进来的那一块。
                string cond = BoolVar(p, g, node, st, sb, indent, "true", ctx.Scope);
                if (!HasOut(g, node, "Out"))
                {
                    sb.AppendLine($"{indent}// 【条件】右边没接线：这一支什么都不做");
                    return;
                }
                sb.AppendLine(indent + $"if ({cond})");
                sb.AppendLine(indent + "{");
                EmitInScope(sb, p, g, node, "Out", ctx, st, indent + "    ");
                sb.AppendLine(indent + "}");
                return;
            }

            default:
                sb.AppendLine($"{indent}// fg:{node.Id}【{node.KindDef?.Display ?? node.Kind}】不是执行模块，跳过（它只在被取用时算值）");
                break;
        }

        // 走完往后（所有出口按顺序，接了几个就走几个）
        for (int port = 0; port < (node.KindDef?.Outputs.Count ?? 0); port++)
            EmitPortChain(sb, p, g, node, port, ctx, st, indent);
    }

    /// <summary>从某个模块第 <paramref name="port"/> 个出口往后走（可能接了好几块，按顺序都走）。</summary>
    private static void EmitPortChain(StringBuilder sb, CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node,
        int port, EmitCtx ctx, GenState st, string indent)
    {
        foreach (var l in g.Outgoing(node.Id, port))
        {
            var next = g.Node(l.To);
            if (next is null) continue;
            EmitChain(sb, p, g, next, ctx, st, indent);
        }
    }

    private static void EmitPortChain(StringBuilder sb, CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node,
        string portKey, EmitCtx ctx, GenState st, string indent)
    {
        int port = PortIndexOf(node, portKey);
        if (port >= 0) EmitPortChain(sb, p, g, node, port, ctx, st, indent);
    }

    /// <summary>
    /// 走一个**大括号块**里的出口（if 的真支 / 假支、【且】/【或】、【条件】）：
    /// 进去之前把作用域压一层、出来弹掉 —— 见 <see cref="GenState.ValueScope"/>。
    /// 【数值】模块的 <c>decimal __fgXXXX = …;</c> 是就地写进当前语句流的，落在分支里就出不了那个大括号；
    /// 另一个分支再取同一个模块时必须**在自己这一段里重新声明**，否则 CS0103。
    /// </summary>
    private static void EmitInScope(StringBuilder sb, CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node,
        string portKey, EmitCtx ctx, GenState st, string indent)
    {
        st.ScopeStack.Add(node.Id + ":" + portKey);
        try { EmitPortChain(sb, p, g, node, portKey, ctx, st, indent); }
        finally { st.ScopeStack.RemoveAt(st.ScopeStack.Count - 1); }
    }

    /// <summary>这个出口上有没有接线。</summary>
    private static bool HasOut(ForgeGraphSpec g, ForgeNodeSpec node, string portKey)
    {
        int port = PortIndexOf(node, portKey);
        return port >= 0 && g.Outgoing(node.Id, port).Count > 0;
    }

    /// <summary>
    /// 「会做事」的模块种类：接到链上才会执行（【效果】/【if】/【且】/【或】/【条件】）。
    /// 【对象】【状态】【数值】【能量】【关系符】【指向对象】不算 —— 它们只在被取用时算值，
    /// 孤零零放着是正常的（用户经常在布上留几个备用模块）。
    /// </summary>
    private static bool IsExecModule(ForgeNodeSpec? n) =>
        n is not null
        // 【条件】勾了「不满足时 = 整张牌打不出去」时，它**不是**执行链上的一环（是卡牌级的门）——
        // 用户完全可能只在布上放一个【条件】当「打不出去」的判定、不接进链里，那不算漏接线。
        && !IsUnplayableGate(n)
        && n.Kind is "Effect" or "If" or "And" or "Or" or "Condition";

    /// <summary>这个模块是不是「不满足时整张牌打不出去」的那个【条件】。</summary>
    private static bool IsUnplayableGate(ForgeNodeSpec n) =>
        string.Equals(n.Kind, "Condition", StringComparison.Ordinal)
        && string.Equals((n.Get("WhenUnmet") ?? "").Trim(), "Unplayable", StringComparison.Ordinal);

    /// <summary>
    /// 图里**没有接到【触发时机】这条链上**的「会做事」模块。
    ///
    /// <para>V0.2.216（用户实测：「处于二形态时触发不了多效卡效果只能打24的基础效果」）：
    /// 他那张「自适应攻击-毁灭」里有一条「长枪形态 == 1 → 27 伤害 + 锋利」的分支，
    /// 但那个【关系符】的**左值（对象）没接线** → 整条分支从【触发时机】走不到 →
    /// 生成时**一个字都不输出**，游戏里就只剩兜底的 24。生成器当时一句提示都没有（静默失效）。</para>
    ///
    /// <para>所以这里把它算出来：生成端在方法里写一段「没接上」的注释，校验器给一条**警告**
    /// （不是错误：留着备用模块是正常用法，不该拦住生成）。</para>
    /// </summary>
    public static IReadOnlyList<ForgeNodeSpec> UnreachableModules(ForgeGraphSpec? g)
    {
        var list = new List<ForgeNodeSpec>();
        if (g is null || g.IsEmpty) return list;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<ForgeNodeSpec>();
        foreach (var t in g.Nodes)
            if (t is not null && string.Equals(t.Kind, "Trigger", StringComparison.Ordinal))
                if (seen.Add(t.Id)) queue.Enqueue(t);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var port in Enumerable.Range(0, Math.Max(1, n.KindDef?.Outputs.Count ?? 1)))
                foreach (var l in g.Outgoing(n.Id, port))
                {
                    var next = g.Node(l.To);
                    if (next is not null && seen.Add(next.Id)) queue.Enqueue(next);
                }
            // 只看「执行」方向：条件类的输入（A/B/Cond）也算接上 —— 它靠被 if/且/或 取用而执行。
            foreach (var l in g.Links)
            {
                if (l is null || !string.Equals(l.To, n.Id, StringComparison.Ordinal)) continue;
                var up = g.Node(l.From);
                if (up is not null && seen.Add(up.Id)) queue.Enqueue(up);
            }
        }
        foreach (var n in g.Nodes)
            if (IsExecModule(n) && !seen.Contains(n.Id)) list.Add(n!);
        return list;
    }

    /// <summary>
    /// 图里勾了「不满足时 = **整张牌打不出去**」的那些【条件】模块 → 它们的 C# 条件表达式。
    ///
    /// <para>V0.2.218（用户要求「锻造无法『处于某种条件下时，该牌无法被打出』」）：以前锻造图只能表达
    /// 「条件不满足就跳过这一支」（【条件】/【if】都是「执行时」判断），做不出卡牌页那种
    /// 「整张牌打不出去」（本体 <c>IsPlayable</c> + 描金边）。现在【条件】模块多一栏「不满足时」，
    /// 选「整张牌打不出去」就汇进卡牌的 <c>IsPlayable</c>。</para>
    ///
    /// <para>⚠ 这一档的条件表达式要写进 <c>IsPlayable</c> / <c>ShouldGlowGoldInternal</c> 那两个**表达式体属性**里，
    /// 那两处**不能声明局部变量** —— 所以「数值 N / M」必须是生成期常量（纯数字或能折叠的式子）。
    /// 写了运行时公式 / 接了【数值】模块的那几条会**跳过**，由校验器报一条警告说清怎么改。</para>
    /// </summary>
    public static IReadOnlyList<string> UnplayableGates(ForgeGraphSpec? g, CharacterProfile? p = null)
    {
        var list = new List<string>();
        if (g is null || g.IsEmpty) return list;
        foreach (var n in g.Nodes)
        {
            if (n is null || !string.Equals(n.Kind, "Condition", StringComparison.Ordinal)) continue;
            if (!string.Equals((n.Get("WhenUnmet") ?? "").Trim(), "Unplayable", StringComparison.Ordinal)) continue;
            var amt = ForgeNumbers.Resolve(p, n, "Amount", 1);
            var amt2 = ForgeNumbers.Resolve(p, n, "Amount2", 0);
            if (!amt.Ok || amt.Runtime || !amt2.Ok || amt2.Runtime) continue;      // 见上面：那两处放不下局部变量
            var spec = new ConditionSpec
            {
                Kind = n.Get("Cond", "None"),
                Amount = (decimal)amt.Value,
                Amount2 = (decimal)amt2.Value,
                PowerId = string.IsNullOrWhiteSpace(n.Get("PowerId")) ? null : n.Get("PowerId"),
            };
            string expr = CSharpCodeGen.ConditionExpr(spec, CSharpCodeGen.CondCtx.Card, inOnPlay: false);
            if (!string.IsNullOrWhiteSpace(expr)) list.Add(expr);
        }
        return list;
    }

    /// <summary>
    /// 「这一档写不出打不出去」的【条件】模块（勾了「整张牌打不出去」但数值写了运行时公式的那几条）——
    /// 校验器据此给警告；生成端跳过它们（那两处不能声明局部变量）。
    /// </summary>
    public static IReadOnlyList<ForgeNodeSpec> UnplayableGatesSkipped(ForgeGraphSpec? g, CharacterProfile? p = null)
    {
        var list = new List<ForgeNodeSpec>();
        if (g is null || g.IsEmpty) return list;
        foreach (var n in g.Nodes)
        {
            if (n is null || !string.Equals(n.Kind, "Condition", StringComparison.Ordinal)) continue;
            if (!string.Equals((n.Get("WhenUnmet") ?? "").Trim(), "Unplayable", StringComparison.Ordinal)) continue;
            var a1 = ForgeNumbers.Resolve(p, n, "Amount", 1);
            var a2 = ForgeNumbers.Resolve(p, n, "Amount2", 0);
            if (!a1.Ok || a1.Runtime || !a2.Ok || a2.Runtime) list.Add(n);
        }
        return list;
    }

    /// <summary>「没接上触发时机」那段注释（生成代码里写一段，让用户一眼看到）。</summary>
    internal static void EmitUnreachableNote(StringBuilder sb, ForgeGraphSpec g, string indent)
    {
        var missed = UnreachableModules(g);
        if (missed.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"{indent}// ⚠ 图上有 {missed.Count} 块模块**没接到【触发时机】这条链上**，它们不会执行（大概率是漏画了一条线）：");
        foreach (var n in missed.Take(20))
        {
            string what = n.Kind switch
            {
                "Effect" => EffectCatalog.FindKind(n.Get("Effect", "Damage")).Display
                    + (n.Get("Amount").Trim().Length > 0 ? " " + n.Get("Amount").Trim() : ""),
                "If" => "如果",
                "And" => "且",
                "Or" => "或",
                "Condition" => "条件",
                _ => n.Kind,
            };
            sb.AppendLine($"{indent}//    · fg:{n.Id}【{what}】");
        }
        if (missed.Count > 20) sb.AppendLine($"{indent}//    · …另有 {missed.Count - 20} 块");
        sb.AppendLine($"{indent}//    常见原因：【关系符】的「对象 / 左值」没接线，或者【是 / 否】那一支忘记接线。");
    }

    /// <summary>
    /// 端口 Key（"True" / "False" / "Out" / "In" / "Cond"…）→ 第几个口。
    /// **入口和出口都要找**：【且】/【或】的条件口是左边那两个「A / B」（在 Inputs 里），
    /// 【if】的「条件」也在 Inputs 里（新版 = 入口 1）；
    /// 而「是 / 否」两个分支口在 Outputs 里 —— 只找一边的话另一边会被当成「没接」，
    /// 条件就恒为 false（老版踩过的坑，见 MigrateIfPorts）。
    /// </summary>
    private static int PortIndexOf(ForgeNodeSpec node, string portKey)
    {
        var ins = node.KindDef?.Inputs;
        if (ins is not null)
            for (int i = 0; i < ins.Count; i++)
                if (string.Equals(ins[i].Key, portKey, StringComparison.Ordinal)) return i;
        var outs = node.KindDef?.Outputs;
        if (outs is not null)
            for (int i = 0; i < outs.Count; i++)
                if (string.Equals(outs[i].Key, portKey, StringComparison.Ordinal)) return i;
        return -1;
    }

    // ==================================================================================
    //  布尔表达式（if / 且 / 或 / 条件）
    // ==================================================================================

    /// <summary>
    /// 把「某个入口上接的那一块」翻成一句 C# 布尔表达式。
    /// 没接线时用 <paramref name="fallback"/>（【if】默认 false = 什么都不走；【且】【或】默认 true）。
    /// </summary>
    private static string BoolExpr(CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node, string inPortKey,
        GenState st, StringBuilder sb, string indent, string fallback, string ctxScope)
    {
        int port = PortIndexOf(node, inPortKey);
        if (port < 0) return fallback;
        // 一个入口上理论上只该有一条线（界面会替换旧的），但旧存档 / 手写 JSON 可能留着多余的 ——
        // 所以这里**按来源种类挑**：只要那一条是能出布尔的（【条件】/【且】/【或】），而不是碰巧排在前面的一条。
        var link = g.Links.FirstOrDefault(l => l is not null
            && string.Equals(l.To, node.Id, StringComparison.Ordinal) && l.ToPort == port
            && IsBoolProducer(g.Node(l.From)));
        var up = link is null ? null : g.Node(link.From);
        if (up is null) return fallback;
        return BoolVar(p, g, up, st, sb, indent, fallback, ctxScope);
    }

    /// <summary>把一个模块翻成 C# 布尔表达式（【条件】/【且】/【或】；别的一律当"不限"）。</summary>
    private static string BoolVar(CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node, GenState st,
        StringBuilder sb, string indent, string fallback, string ctxScope)
    {
        if (st.Depth > 48 || !st.Busy.Add(node.Id)) return fallback;
        st.Depth++;
        try
        {
            switch (node.Kind)
            {
                case "Condition":
                {
                    // 卡牌写在 OnPlay 里（能用「指定敌人」= cardPlay.Target）；
                    // 遗物 / 药水没有 cardPlay，用 Relic 那个上下文（CombatState 从 Owner.Creature 取）；
                    // V0.2.194：状态那一档用 Power 上下文（Owner 就是 Creature，玩家从 Owner.Player 取）。
                    var ctxKind = string.Equals(ctxScope, "Card", StringComparison.Ordinal) ? CSharpCodeGen.CondCtx.Card
                        : string.Equals(ctxScope, ForgeTimings.PowerScope, StringComparison.Ordinal) ? CSharpCodeGen.CondCtx.Power
                        : CSharpCodeGen.CondCtx.Relic;
                    // 「数值 N」那一栏接了【数值】模块**或者自己写了公式** → N 是运行时才算出来的，
                    // 那几种最常用的判据（手牌数 / 生命百分比 / 状态层数 / 额外资源量 / 生命区间）
                    // 直接写成运行时比较；其余条件种类仍然用模块上填的常数（本体那套判据照旧复用）。
                    var vnode = IncomingNode(g, node, 0);
                    if (vnode is not null && IsValueLike(vnode))
                    {
                        string? nExpr = ValueVar(p, g, vnode, st, sb, indent, IncomingPortAt(g, node, 0));
                        if (nExpr is not null)
                        {
                            string? runtime = RuntimeCondition(node.Get("Cond", "None"), nExpr, node, ctxKind);
                            if (runtime is not null) return runtime;
                        }
                    }
                    // 数值 N / 数值 M 自己写了**运行时**公式（V0.2.128）：同样走运行时判据
                    var condAmount = ForgeNumbers.Resolve(p, node, "Amount", 1);
                    var condAmount2 = ForgeNumbers.Resolve(p, node, "Amount2", 0);
                    if (condAmount.Runtime || condAmount2.Runtime)
                    {
                        string nExpr = DeclareNumberLocal(sb, indent, st, node, "Amount", condAmount, "数值 N");
                        string? runtime;
                        if (condAmount2.Runtime)
                        {
                            string mExpr = DeclareNumberLocal(sb, indent, st, node, "Amount2", condAmount2, "数值 M");
                            runtime = RuntimeCondition(node.Get("Cond", "None"), nExpr, node, ctxKind, mExpr);
                        }
                        else runtime = RuntimeCondition(node.Get("Cond", "None"), nExpr, node, ctxKind);
                        if (runtime is not null) return runtime;
                        // 这个条件种类这一阶段还不支持运行时 N：**不静默**（校验器会报一条错误说明）
                        sb.AppendLine(indent + $"// ⚠【条件】的数值写了公式，但这个条件种类还不支持运行时判断 —— 这一条恒为「不成立」"
                            + "（去「配置检查」看那条错误）。");
                        return "false";
                    }
                    var spec = new ConditionSpec
                    {
                        Kind = node.Get("Cond", "None"),
                        Amount = (decimal)condAmount.Value,
                        Amount2 = (decimal)condAmount2.Value,
                        PowerId = string.IsNullOrWhiteSpace(node.Get("PowerId")) ? null : node.Get("PowerId"),
                        // V0.2.247（用户要求）：「上一张打出的牌带有某关键词 / 是某范围（可按组）」
                        Keyword = node.Get("Keyword") ?? "",
                        Scope = node.Get("Scope") ?? "",
            // V0.2.300（用户报「锻造的条件模块怎么没修」）：条件「指定敌人的意图为（）」在图上也要有「意图」下拉
            Intent = node.Get("Intent") ?? "Attack",
                    };
                    return CSharpCodeGen.ConditionExpr(spec, ctxKind, inOnPlay: ctxKind == CSharpCodeGen.CondCtx.Card);
                }
                case "And":
                    return "(" + BoolExpr(p, g, node, "A", st, sb, indent, "true", ctxScope) + " && " + BoolExpr(p, g, node, "B", st, sb, indent, "true", ctxScope) + ")";
                case "Or":
                    return "(" + BoolExpr(p, g, node, "A", st, sb, indent, "true", ctxScope) + " || " + BoolExpr(p, g, node, "B", st, sb, indent, "true", ctxScope) + ")";
                case "Relate":
                    return RelateExpr(p, g, node, st, sb, indent, ctxScope);
                case "If":
                    // 【if】的条件来自它自己那个「条件」插槽（用 PortIndexOf 找，入口 / 出口都认）
                    return BoolExpr(p, g, node, "Cond", st, sb, indent, fallback, ctxScope);
                default:
                    return fallback;
            }
        }
        finally
        {
            st.Depth--;
            st.Busy.Remove(node.Id);
        }
    }

    /// <summary>这个模块会不会产出一个布尔（能接到【if】/【且】/【或】/【关系符】的条件入口上）。</summary>
    /// <summary>
    /// V0.2.286：这个「张数」表达式是不是**字面 0**（锻造图那条路的写法是 <c>(0m)</c> / <c>(int)((0m))</c>）。
    /// 是 → 这个效果模块整块跳过（本体选 0 张的界面会一直等 → 卡死）。公式 / 变量一律不算。
    /// </summary>
    private static bool IsZeroGraphCount(string n)
    {
        string s = n.Replace("(int)", "").Replace("(decimal)", "").Replace("(", "").Replace(")", "")
            .Replace("m", "").Replace(".", "").Replace(" ", "").Trim();
        return s.Length > 0 && s.All(ch => ch == '0');
    }

    private static bool IsBoolProducer(ForgeNodeSpec? node) =>
        node is not null && node.Kind is "Condition" or "And" or "Or" or "If" or "Relate";

    /// <summary>
    /// 【关系符】：把左值 / 右值按运算符比一下，产出一句 C# 布尔表达式。
    ///
    ///   大于 / 小于 / 等于 / 大于等于 / 小于等于 → 两边都是**数值**（【状态】/【数值】/【能量】）；
    ///   非 / 且 / 或 → 两边都是**条件**（【条件】/【且】/【或】/另一个【关系符】）。
    ///
    /// 右值没接线 = 用模块上填的那个数字（比较类才有意义）。
    /// 接错了类型（该接数值的地方接了条件、反之）时**不生成会编译不过的代码**：退成 false 并写一句说明，
    /// 同时校验器会给一条错误（见 ForgeGraphGen.Issues）。
    /// </summary>
    private static string RelateExpr(CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node, GenState st,
        StringBuilder sb, string indent, string ctxScope)
    {
        string op = node.Get("Op", "Gt");
        bool needsBool = ForgeGraphCatalog.RelateNeedsBool(op);
        bool unary = ForgeGraphCatalog.RelateIsUnary(op);

        // 左值：按**运算符要什么**去要求上游（数值类运算符要数值，逻辑类要条件）。
        // 没接线 / 接错类型都要说清楚，别生成出 `(某个bool) > 3m` 这种编译不过的东西。
        var (ln, lp) = IncomingNodeAt(g, node, 1);
        string left;
        if (ln is null)
        {
            sb.AppendLine($"{indent}// ⚠【关系符】{ForgeGraphCatalog.RelateOpZh(op)}：左边「左值」没接线，这一条恒为不成立。");
            return "false";
        }
        bool leftIsBool = IsBoolProducer(ln);
        if (leftIsBool != needsBool)
        {
            sb.AppendLine($"{indent}// ⚠【关系符】{ForgeGraphCatalog.RelateOpZh(op)}：左值接的是「{(leftIsBool ? "条件" : "数值")}」，"
                + $"这个运算符要的是「{(needsBool ? "条件" : "数值")}」—— 这一条恒为不成立（去「配置检查」看那条错误）。");
            return "false";
        }
        if (needsBool) left = BoolVar(p, g, ln, st, sb, indent, "true", ctxScope);
        else left = ValueVar(p, g, ln, st, sb, indent, lp) ?? "0m";

        if (unary) return $"(!({left}))";

        var (rn, rp) = IncomingNodeAt(g, node, 2);
        string right;
        if (rn is null)
        {
            // 比较类：右值没接线就用模块上填的数字（V0.2.128：那一栏也能写公式/参数）；
            // 且 / 或：没接线当"成立"
            if (needsBool) right = "true";
            else
            {
                var amt = ForgeNumbers.Resolve(p, node, "Amount", 3);
                right = amt.Runtime
                    ? DeclareNumberLocal(sb, indent, st, node, "Amount", amt, "右值")
                    : Lit((double)amt.Value);
            }
        }
        else if (IsBoolProducer(rn) != needsBool)
        {
            sb.AppendLine($"{indent}// ⚠【关系符】{ForgeGraphCatalog.RelateOpZh(op)}：右值接的是「{(IsBoolProducer(rn) ? "条件" : "数值")}」，"
                + $"这个运算符要的是「{(needsBool ? "条件" : "数值")}」—— 这一条恒为不成立（去「配置检查」看那条错误）。");
            return "false";
        }
        else if (needsBool) right = BoolVar(p, g, rn, st, sb, indent, "true", ctxScope);
        else right = ValueVar(p, g, rn, st, sb, indent, rp) ?? "0m";

        return op switch
        {
            "Lt" => $"(({left}) < ({right}))",
            "Eq" => $"(({left}) == ({right}))",
            "Ge" => $"(({left}) >= ({right}))",
            "Le" => $"(({left}) <= ({right}))",
            "And" => $"(({left}) && ({right}))",
            "Or" => $"(({left}) || ({right}))",
            _ => $"(({left}) > ({right}))",
        };
    }

    /// <summary>这个模块会不会产出一个数值（【数值】/【状态】/【状态列表】/【能量】）。</summary>
    private static bool IsValueLike(ForgeNodeSpec node) =>
        node.Kind is "Value" or "Power" or ForgeGraphCatalog.PowerListKind or "Energy";

    /// <summary>
    /// 「数值 N」接了【数值】模块、或者那一栏自己写了运行时公式时（N 要运行时才算），
    /// 最常用的那几种条件直接写成运行时比较。
    /// 返回 null = 这个条件种类这一阶段还不支持运行时 N（调用方会写一行说明 + 校验器报错，不静默）。
    /// <paramref name="mExpr">「数值 M」也是运行时才算的那种（只有「生命值在 N%~M% 之间」用得到）。</param>
    /// </summary>
    private static string? RuntimeCondition(string kind, string nExpr, ForgeNodeSpec node, CSharpCodeGen.CondCtx ctxKind,
        string? mExpr = null)
    {
        string player = ctxKind == CSharpCodeGen.CondCtx.Power ? "base.Owner.Player" : "base.Owner";
        string ownerCreature = ctxKind == CSharpCodeGen.CondCtx.Power ? "base.Owner" : "base.Owner.Creature";
        // V0.2.194：状态那一档的 CombatState 在 Creature 上（base.Owner.CombatState）——
        // 以前这里只有「卡牌 = base.CombatState，其余 = base.Owner.Creature.CombatState」两档，
        // Power 会写成 `Creature.Creature`（编译不过），所以补上这一档。
        string st = ctxKind == CSharpCodeGen.CondCtx.Card ? "base.CombatState"
            : ctxKind == CSharpCodeGen.CondCtx.Power ? "base.Owner.CombatState"
            : "base.Owner.Creature.CombatState";
        string hand = $"CardPile.GetCards({player}, PileType.Hand)";
        // 生命区间：下界 / 上界（都按百分比）。M 也是运行时才算时用两个算出来的数比。
        string lo = nExpr, hi = mExpr ?? nExpr;
        return kind switch
        {
            "HandAtLeast" => $"{player} is not null && {hand}.Count() >= (int)({nExpr})",
            "HandAtMost" => $"{player} is not null && {hand}.Count() <= (int)({nExpr})",
            "PlayedAtLeast" =>
                $"{st} is not null && CombatManager.Instance.History.CardPlaysFinished.Count(e => e.Actor == {ownerCreature} && e.HappenedThisTurn({st})) >= (int)({nExpr})",
            "ExtraResourceAtLeast" => $"(({player}?.PlayerCombatState?.Stars) ?? 0) >= (decimal)({nExpr})",
            "HpBelowPercent" =>
                $"({ownerCreature}.MaxHp <= 0 || {ownerCreature}.CurrentHp * 100m <= {ownerCreature}.MaxHp * (decimal)({nExpr}))",
            "HpInRange" =>
                // 两头都是现算的：用 Min/Max 保证区间是「小的一头 → 大的一头」（和常数那条路「顺序写反了自动排好」一致）
                $"({ownerCreature}.MaxHp <= 0 || ({ownerCreature}.CurrentHp * 100m"
                + $" >= {ownerCreature}.MaxHp * (decimal)System.Math.Min(({lo}), ({hi}))"
                + $" && {ownerCreature}.CurrentHp * 100m <= {ownerCreature}.MaxHp * (decimal)System.Math.Max(({lo}), ({hi}))))",
            "HasPowerAtLeast" when !string.IsNullOrWhiteSpace(node.Get("PowerId")) =>
                $"{ownerCreature}.GetPowerAmount<{Sts2CharForge.Core.Generation.Lit.Identifier(node.Get("PowerId"), "VulnerablePower")}>() >= (decimal)({nExpr})",
            _ => null,
        };
    }

    /// <summary>
    /// 把一个**运行时公式**的数字栏位算成一个局部变量（同一个模块的同一栏只声明一次：
    /// 同一个【条件】块可能被两条链各走一遍，直接各声明一次会 CS0128 重复定义）。
    /// 返回变量名。
    /// </summary>
    private static string DeclareNumberLocal(StringBuilder sb, string indent, GenState st, ForgeNodeSpec node,
        string key, ForgeNumbers.Resolved r, string label)
    {
        string varName = UniqueVar(node, "__fgN" + key);
        if (!st.NumEmitted.Add(node.Id + "#" + key)) return varName;     // 已经声明过了
        sb.AppendLine(indent + $"decimal {varName} = {r.Expr};   // fg:{node.Id}{label}（公式：{r.Text}）");
        return varName;
    }

    /// <summary>「血量低于 N」「仅生效 N 次」这些**进门条件**。</summary>
    /// <param name="p">整份配置（这两栏现在也能写公式 / 用自定义参数，见 <see cref="ForgeNumbers"/>）。</param>
    private static void EmitTimingGate(StringBuilder sb, HookPlan plan, EmitCtx ctx, CharacterProfile p)
    {
        var trig = plan.Trigger;
        // 计数字段名在 EmitCtx.UsesVar 那一份里（多块【触发时机】时每块各一个，末尾按块编号）
        string uses = ctx.UsesVar;

        if (string.Equals(plan.Timing.Id, ForgeTimings.CardAutoPlayed, StringComparison.Ordinal))
        {
            sb.AppendLine("        // 说明：本体没给「自动打出」单独一个钩子，这一支挂在 AfterCardPlayed 上；");
            sb.AppendLine("        // 手动打出时也会走到这里（本体分不出来），要严格区分请换别的触发时机。");
        }

        if (plan.Timing.NeedHp)
        {
            // ⚠ 这一栏也能写公式（V0.2.128）：写了运行时公式时**两档都按运行时判**
            //（公式算出来小于 1 = 按百分比、算出 ≥ 1 = 按点数 ——和那一栏的文字说明同一套口径，
            //  生成期分不出来是哪一档，所以生成一句自适应的判断）。
            var hp = ForgeNumbers.Resolve(p, trig, "HpThreshold", 0.5);
            if (hp.Runtime)
            {
                sb.AppendLine($"        // 「血量低于 {hp.Text}（按公式算：小于 1 = 百分比，≥ 1 = 点数）」");
                sb.AppendLine("        {");
                sb.AppendLine("            Creature __fgWatch = base.Owner?.Creature;");
                sb.AppendLine($"            decimal __fgHpTh = {hp.Expr};");
                sb.AppendLine("            if (__fgWatch is null || __fgWatch.MaxHp <= 0) return;");
                sb.AppendLine("            if (__fgHpTh < 1m)");
                sb.AppendLine("            {");
                sb.AppendLine("                if ((decimal)__fgWatch.CurrentHp > (decimal)__fgWatch.MaxHp * __fgHpTh) return;");
                sb.AppendLine("            }");
                sb.AppendLine("            else if ((decimal)__fgWatch.CurrentHp >= __fgHpTh) return;");
                sb.AppendLine("        }");
            }
            else
            {
                double raw = (double)hp.Value;
                bool percent = raw < 1;
                sb.AppendLine($"        // 「血量低于 {Lit(raw)}{(percent ? "（按百分比）" : " 点")}」");
                sb.AppendLine("        {");
                sb.AppendLine("            Creature __fgWatch = base.Owner?.Creature;");
                sb.AppendLine(percent
                    ? $"            if (__fgWatch is null || __fgWatch.MaxHp <= 0 || (decimal)__fgWatch.CurrentHp > (decimal)__fgWatch.MaxHp * {Lit(raw)}m) return;"
                    : $"            if (__fgWatch is null || (decimal)__fgWatch.CurrentHp >= {Lit(raw)}m) return;");
                sb.AppendLine("        }");
            }
        }

        var lim = ForgeNumbers.Resolve(p, trig, "CountLimit", 0);
        int limit = lim.Runtime ? 0 : (int)Math.Round((double)lim.Value);
        if (plan.Timing.NeedCount || lim.Runtime || limit > 0)
        {
            string scope = trig.Get("CountScope", "Combat");
            // 运行时公式：比的是算出来的那个数（算出来 0 / 负数 = 这一支一次都不跑 —— 校验器有提示）
            string cmp = lim.Runtime ? $"(int)({lim.Expr})" : Lit(limit);
            sb.AppendLine($"        // 生效次数：{(lim.Runtime ? "按公式算（" + lim.Text + "）" : ForgeTimings.CountZh(limit, scope))}（填 0 = 无限次）");
            // ⚠ 填 0 = 无限次时**不能**写「__fgUses >= 0 就 return」：那个条件恒成立，
            // 整张图一次都不会跑 —— 正是「菜单/栏位里有、生成出来什么都不做」那种坑
            //（自检里「血量低于 N」「当遗物被点击时」两条都造了图验它真的会做事）。
            if (lim.Runtime || limit > 0)
            {
                sb.AppendLine($"        if ({uses} >= {cmp}) return;");
                sb.AppendLine($"        {uses}++;");
                sb.AppendLine($"        if ({uses} >= {cmp})");
                sb.AppendLine("        {");
                sb.AppendLine("            Status = RelicStatus.Disabled;   // 用尽之后和本体一样变灰");
                sb.AppendLine("            InvokeDisplayAmountChanged();");
                sb.AppendLine("        }");
            }
        }
    }

    // ==================================================================================
    //  值 / 效果
    // ==================================================================================

    /// <summary>把图里用到的自定义参数收集出来（去重、保持配置里的顺序）。</summary>
    private static List<ParamSpec> UsedParams(CharacterProfile p, ForgeGraphSpec g)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in g.Nodes)
        {
            if (n is null || n.Kind != "Value") continue;
            var root = ForgeFormula.TryParse(n.Get("Formula"), out _);
            foreach (string id in ForgeFormula.Identifiers(root)) names.Add(id);
        }
        // V0.2.128：那 12 个「数字框」里也能写公式/参数 —— 用到哪个参数就要把 `__pM` 声明出来
        //（只有**运行时**的那些才要：生成期能折叠成常数的公式里那个参数一个变量都不需要）。
        foreach (var (node, key, _) in ForgeNumbers.FormulaFields(p, g))
        {
            var root = ForgeFormula.TryParse(node.Get(key), out _);
            foreach (string id in ForgeFormula.Identifiers(root)) names.Add(id);
        }
        return p.CustomParams.Where(x => x is not null && names.Contains(ParamNames.Normalize(x.Name))).ToList();
    }

    // ==================================================================================
    //  「数值公式」那一栏（需求原文：「另加一栏『数值公式』」）
    //
    //  和锻造页【数值】模块共用同一套语法与求值器（ForgeFormula / ForgeFormulaRuntime），
    //  区别只有上下文：那边每个执行点先算 __fgPlayer / __fgSelf / __fgTarget / __p<字母> / __X / __Y，
    //  这边是「卡牌被打出的那一刻」，同样的变量在 OnPlay 开头算一次。
    //  只有那 5 种「打出时才读数值」的效果用得到（见 EffectSpec.UsesAmountFormula）。
    // ==================================================================================

    /// <summary>这个存档里有没有卡牌用了「数值公式」/ 次数类公式 / 条件数值公式
    /// （用到才把 ForgeFormulaRuntime.cs 写进工程）。</summary>
    public static bool AnyCardFormula(CharacterProfile p) =>
        p.AllCards.Any(c => c is not null && !c.IsCurseCard
            && (c.Effects.Any(e => e is not null && (e.UsesAmountFormula || e.UsesAnyCountFormula || e.Condition?.UsesAnyFormula == true))
                || c.Condition?.UsesAnyFormula == true));

    /// <summary>
    /// V0.2.164：「数值公式」铺到遗物 / 药水 / 自定义状态之后，这三条链也要用到同一个
    /// <c>ForgeFormulaRuntime.cs</c>（内联表达式里有 Div / PowerAmount / FirstEnemy 这些辅助）。
    /// 判据只看**这一条效果会不会被生成成公式**（<c>UsesAmountFormula</c> 与链无关）。
    /// </summary>
    public static bool AnyNonCardFormula(CharacterProfile p) =>
        p.Relics.Any(r => r is not null && r.Effects.Any(IsFormulaEffect))
        || p.Potions.Any(s => s is not null && s.Effects.Any(IsFormulaEffect))
        || p.CustomPowers.Any(cp => cp is not null && cp.Triggers.Any(t => t is not null && t.Effects.Any(IsFormulaEffect)));

    private static bool IsFormulaEffect(EffectSpec? e) => e is not null && e.UsesAmountFormula;

    /// <summary>
    /// 一条卡牌效果的「数值公式」对应的 C# 表达式（null = 这条效果没用公式，调用方照旧用固定数值）。
    ///
    /// 返回的表达式里会引用 <c>__X</c> / <c>__Y</c> / <c>__p&lt;字母&gt;</c> 这些局部变量，
    /// 所以调用方**必须**先把 <see cref="EmitCardFormulaPrologue"/> 的声明吐出来（见 OnPlay / 卡牌时机钩子）。
    /// </summary>
    public static string? CardFormulaExpr(CharacterProfile p, EffectSpec e, out string error)
    {
        error = "";
        if (e is null || !e.UsesAmountFormula) return null;
        var root = ForgeFormula.TryParse(e.AmountFormula, out error);
        if (root is null) return null;
        // X / Y + 内置只读量（Turn、DiscardedThisTurn…）+ 自定义参数，统一走 ForgeFormula.CardResolver
        string? expr = ForgeFormula.Emit(root, ForgeFormula.CardResolver(p), out error);
        return expr;
    }

    /// <summary>
    /// 把这一组效果里「数值公式」用到的东西先算成局部变量（每个方法体开头调一次）。
    ///
    /// 只声明**真的用到**的那几个 —— C# 对「赋值了但没用」的局部变量会给一条警告（CS0219），
    /// 而生成出来的工程是要能干净编译的。
    /// <paramref name="targetExpr"/> 是「玩家选中的那个目标」在这条链里的写法：
    /// 卡牌是 <c>cardPlay?.Target</c>，卡牌时机钩子里也是它（那里会补一句 <c>CardPlay? cardPlay = null;</c>）。
    /// <paramref name="xPlus"/> 是这张牌的「升级后 X + N」（0 = 不加）—— <c>__X</c> 也要跟着加：
    /// 公式里的 <c>X</c> 和「= X」走的是**同一个 X**（打出时消耗掉的能量），
    /// 不给 <c>__X</c> 加就等于「升级后费用填 X+1」对那些用公式的效果静默失效。
    /// </summary>
    public static void EmitCardFormulaPrologue(CodeWriter w, CharacterProfile p, IEnumerable<EffectSpec> effects,
        string targetExpr = "cardPlay?.Target", int xPlus = 0, bool costIsX = false, bool starCostIsX = false)
    {
        // 「数值公式」+「次数类公式」（用户要求⑦）都要在这里先把值算出来
        var list = effects.Where(e => e is not null && (e.UsesAmountFormula || e.UsesAnyCountFormula)).ToList();
        if (list.Count == 0) return;

        // 1) 公式本体
        var formulaExprs = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in list)
        {
            if (e.UsesAmountFormula)
            {
                string? expr = CardFormulaExpr(p, e, out _);
                if (expr is not null)
                {
                    formulaExprs.Add(expr);
                    var root = ForgeFormula.TryParse(e.AmountFormula, out _);
                    foreach (string id in ForgeFormula.Identifiers(root)) names.Add(id);
                }
            }
            foreach (string key in EffectSpec.CountFormulaKeys)
            {
                string? ce = CSharpCodeGen.CountFormulaExpr(p, e, key, out _);
                if (ce is null) continue;
                formulaExprs.Add(ce);
                var root = ForgeFormula.TryParse(e.CountFormula(key), out _);
                foreach (string id in ForgeFormula.Identifiers(root)) names.Add(id);
            }
        }
        if (formulaExprs.Count == 0) return;

        // 2) 参数（按配置里的顺序）
        var usedParams = p.CustomParams
            .Where(x => x is not null && names.Contains(ParamNames.Normalize(x.Name))).ToList();
        var paramDecls = usedParams
            .Select(param =>
            {
                string name = ParamNames.Normalize(param.Name);
                return $"decimal {ForgeFormula.ParamVar(name)} = {ForgeFormula.ParamExpr(param, p)};   // {name}= {param.Summary}";
            })
            .ToList();

        // 3) 用到哪些变量：公式本体 + 参数表达式一起看（参数表达式自己会引用 __fgSelf / __fgTarget / __fgPlayer）
        string all = string.Join("\n", formulaExprs) + "\n" + string.Join("\n", paramDecls);
        bool needPlayer = all.Contains("__fgPlayer", StringComparison.Ordinal);
        bool needSelf = all.Contains("__fgSelf", StringComparison.Ordinal);
        bool needTarget = all.Contains("__fgTarget", StringComparison.Ordinal);
        bool needX = all.Contains("__X", StringComparison.Ordinal);
        bool needY = all.Contains("__Y", StringComparison.Ordinal);

        w.Line("// ---- 「数值公式」用到的值：打出这张牌时先算一次（和锻造页【数值】模块同一套语法）----");
        if (needPlayer) w.Line("Player __fgPlayer = base.Owner;");
        if (needSelf) w.Line("Creature __fgSelf = base.Owner.Creature;");
        if (needTarget) w.Line($"Creature? __fgTarget = {targetExpr};");
        // ⚠ V0.2.168：这张牌自己是**这张资源费的 X 档**（费用框里写 X / Y）时，__X / __Y 直接取
        // **这张牌花掉的那部分**，而不是「当前还剩多少」——
        // 本体是**先扣费、再跑效果**（PlayCardAction：`await _card.SpendResources()` → OnPlay），
        // 所以效果跑的那一刻「当前额外资源量」已经是扣完剩下的：一张「Y 费」的牌读到 **0**
        //（用户实测：「次数填 Y 只生效一次」—— 0 被 Math.Clamp(…, 1, 20) 夹成 1）。
        // 本体本来就为 X 费牌准备了这两个方法：ResolveEnergyXValue() / ResolveStarXValue()（= 这次花掉的）。
        // 用法的口径和「= X」那一条效果（见 CSharpCodeGen 里那个 `int x = Resolve…XValue() + …`）**一致**。
        string plus = xPlus > 0 ? $" + (base.IsUpgraded ? {xPlus} : 0)" : "";
        if (needX) w.Line((costIsX
            ? $"decimal __X = (decimal)base.ResolveEnergyXValue(){plus};   // X = 这张牌花掉的能量（X 费牌：本体先扣费再跑效果，读「当前能量」会是扣完剩下的）"
            : $"decimal __X = ForgeFormulaRuntime.Energy(base.Owner){plus};   // X = 打出这张牌那一刻的当前能量")
            + (xPlus > 0 ? $"（升级后 X + {xPlus}）" : ""));
        if (needY) w.Line(starCostIsX
            ? "decimal __Y = (decimal)base.ResolveStarXValue();   // Y = 这张牌花掉的额外资源量（Y 费牌：本体先扣费再跑效果，读「当前值」会是 0）"
            : "decimal __Y = ForgeFormulaRuntime.Stars(base.Owner);   // Y = 当前额外资源量");
        foreach (string decl in paramDecls) w.Line(decl);
        w.Line();
    }

    private static ForgeNodeSpec? IncomingNode(ForgeGraphSpec g, ForgeNodeSpec node, int port)
    {
        var l = g.Incoming(node.Id, port);
        return l is null ? null : g.Node(l.From);
    }

    // ==================================================================================
    //  卡面文案要用得到的几个小导航（只读，不改任何数据）
    //  ——「从节点图写一句卡面描述」在 LocalizationGen 里，用这几个 helper 读图。
    // ==================================================================================

    /// <summary>接在某个入口上的模块（没有 = null）。文案生成用。</summary>
    public static ForgeNodeSpec? NodeAtIncoming(ForgeGraphSpec g, ForgeNodeSpec node, int port)
        => IncomingNode(g, node, port);

    /// <summary>某个出口接的下一个模块（没有 = null）。文案生成用。</summary>
    public static ForgeNodeSpec? NodeAtOutgoing(ForgeGraphSpec g, ForgeNodeSpec node, int port)
    {
        foreach (var l in g.Outgoing(node.Id, port))
            if (l is not null && g.Node(l.To) is { } n) return n;
        return null;
    }

    /// <summary>
    /// 这条效果**打谁**（取值那一档的名字，不是表达式）：【效果】自己那个「指向对象」口优先，
    /// 没有就用链上最近的【对象】/【指向对象】，再没有就是「指定敌人」（和生成端一个口径）。
    /// 文案生成用它把「打谁」写成中文（召唤物那几档生成端有专门的表达式，文案这边直接读名字）。
    /// </summary>
    public static (string Pick, string SummonId) ResolvedPickForText(ForgeGraphSpec g, ForgeNodeSpec effect, string fallbackPick)
    {
        var direct = IncomingNode(g, effect, 2);
        if (direct is { Kind: "Unit" or "Target" })
            return (direct.Get("Pick", "Self"), direct.Get("SummonId", ""));
        // V0.2.129：没接线、而用户在新编辑器里**改过「作用对象」**那一栏 → 文案按那一档写
        //（和生成端 ResolveEffectUnit 是同一套优先级，否则会出现「代码打全体、卡面写指定敌人」）。
        if (effect.EffectSpec is { TargetSideCustom: true } spec)
            return (TargetSideToPick(spec.TargetSide), "");
        // 没用【对象】直接接在效果上：顺着执行链往上找最近的一个【对象】/【指向对象】
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cur = effect;
        for (int guard = 0; guard < 32; guard++)
        {
            if (!seen.Add(cur.Id)) break;
            var up = IncomingNode(g, cur, 0);
            if (up is null) break;
            if (up.Kind is "Unit" or "Target") return (up.Get("Pick", "Self"), up.Get("SummonId", ""));
            cur = up;
        }
        return (fallbackPick, "");
    }

    /// <summary>
    /// 「作用对象」那一栏（<see cref="EffectSpec.TargetSide"/>）→【对象】模块那套 Pick 值
    /// （文案里要用同一份中文名，见 <see cref="ForgeGraphCatalog.UnitZh"/>）。
    /// 和 <see cref="TargetSideUnit"/> 一一对应（那边是生成代码、这里是文案）。
    /// </summary>
    public static string TargetSideToPick(string? targetSide) => targetSide switch
    {
        "Self" => "Self",
        "Ally" => "ChosenAlly",
        "AllAllies" => "AllAllies",
        "AllEnemies" => "AllEnemies",
        "RandomEnemies" => "RandomEnemy",
        // V0.2.132：卡牌那栏新加的「全体单位」在节点图里**本来就有**同一档（【对象】的 AllUnits）
        "AllUnits" => "AllUnits",
        _ => "ChosenEnemy",          // Enemy（默认）= 打出这张牌时选中的那个目标
    };

    /// <summary>【状态】模块盯的是谁（文案里要写成「指定敌人身上的虚弱层数」）。</summary>
    public static string PowerOwnerPhraseForText(ForgeGraphSpec g, ForgeNodeSpec power, CharacterProfile p)
    {
        var unit = IncomingNode(g, power, 0);
        string pick = unit is { Kind: "Unit" or "Target" } ? unit.Get("Pick", "Self") : "Self";
        return ForgeGraphCatalog.UnitZh(pick);
    }

    /// <summary>
    /// 同上，但把「上游是从哪个出口接过来的」也带回来。
    /// 【能量】右边是两个口（能量 / 额外资源量），取值时必须知道是哪一个，否则两个口都会算成 __X。
    /// </summary>
    private static (ForgeNodeSpec? Node, int OutPort) IncomingNodeAt(ForgeGraphSpec g, ForgeNodeSpec node, int port)
    {
        var l = g.Incoming(node.Id, port);
        if (l is null) return (null, 0);
        return (g.Node(l.From), l.FromPort);
    }

    /// <summary>接在某个入口上的那条线是从哪个出口来的（没有线 = 0）。</summary>
    private static int IncomingPortAt(ForgeGraphSpec g, ForgeNodeSpec node, int port)
        => g.Incoming(node.Id, port)?.FromPort ?? 0;

    /// <summary>
    /// 从【触发时机】顺着执行链走一遍，收集这一支上的效果模块，并判断这一支里有没有「卡牌奖励」。
    /// 「卡牌奖励」只能挂在「战斗胜利时」那一支上（本体没有战斗中直接弹奖励的入口），
    /// 所以生成时要按**这一支自己**来判断，而不是看整张图。
    /// </summary>
    internal static (List<ForgeNodeSpec> Effects, bool HasCardReward) WalkChain(ForgeGraphSpec g, ForgeNodeSpec trigger)
    {
        var effects = new List<ForgeNodeSpec>();
        bool hasReward = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var l in g.Outgoing(trigger.Id, 0)) queue.Enqueue(l.To);
        while (queue.Count > 0)
        {
            string id = queue.Dequeue();
            if (!seen.Add(id)) continue;
            var node = g.Node(id);
            if (node is null) continue;
            if (node.Kind == "Effect")
            {
                effects.Add(node);
                if (string.Equals(node.Get("Effect"), "CardReward", StringComparison.Ordinal)) hasReward = true;
                continue;
            }
            for (int port = 0; port < (node.KindDef?.Outputs.Count ?? 0); port++)
                foreach (var l in g.Outgoing(node.Id, port)) queue.Enqueue(l.To);
        }
        return (effects, hasReward);
    }

    /// <summary>
    /// 把一个「数值来源」模块算成 C# 局部变量，返回变量名（算不出来返回 null）。
    /// 生成的 <c>decimal __fgXXXXXX = …;</c> 会**直接写进当前这段语句流**（<paramref name="sb"/> + <paramref name="indent"/>），
    /// 所以它一定出现在用到它的那一句之前；同一个模块在**同一个作用域里**只算一次
    /// （<see cref="GenState.ValueScope"/>；跨分支作用域会各算一份，原因见那个字段的说明）。
    /// </summary>
    private static string? ValueVar(CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node,
        GenState st, StringBuilder sb, string indent, int outPort = 0)
    {
        if (st.Depth > 40) return null;
        // 【能量】右边有两个出口（能量 / 额外资源量），得按**接的是哪个口**分别取 __X / __Y；
        // 同一个模块的两个口各出一个变量，所以变量名也要带上口序号。
        string varName = outPort == 0 ? node.VarName : node.VarName + "p" + outPort.ToString(CultureInfo.InvariantCulture);
        string key = node.Id + "#" + outPort.ToString(CultureInfo.InvariantCulture);
        // 已经算过、而且声明处**罩得住**当前作用域 → 直接引用（这条路上生成文本一个字符都不变）。
        // 罩不住（声明落在另一个平级分支里）→ 不能引用，下面在**这一段**里重新声明一次，
        // 否则就是 CS0103「当前上下文中不存在名称 __fgXXXXXX」（用户实测报的 MimikyuCard52）。
        if (st.ValueScope.TryGetValue(key, out string? declaredIn) && GenState.ScopeCovers(declaredIn, st.ScopeKey))
            return varName;
        // ⚠ 以前这里返回 node.VarName：缓存键带了口序号，回收时却没带 →
        // 出口 1 复用时拿到的名字少了 pN 后缀（CS0103），现在统一按上面的 varName 返回。
        st.ValueScope[key] = st.ScopeKey;

        switch (node.Kind)
        {
            case "Value":
            {
                string formula = node.Get("Formula").Trim();
                if (formula.Length > 0)
                {
                    var root = ForgeFormula.TryParse(formula, out _);
                    if (root is null) return null;
                    // X / Y + 内置只读量 + 自定义参数（和卡牌那条公式同一套解析）
                    string? expr = ForgeFormula.Emit(root, ForgeFormula.CardResolver(p), out _);
                    if (expr is null) return null;
                    sb.AppendLine(indent + $"decimal {varName} = {expr};   // fg:{node.Id}【数值】{formula}");
                    return varName;
                }
                // 没写公式：用接进来的那一块（它接在哪个口上就按哪个口取）
                var (upA, upAPort) = IncomingNodeAt(g, node, 0);
                string? inner = upA is null ? null : ValueVar(p, g, upA, st, sb, indent, upAPort);
                if (inner is null) return null;
                sb.AppendLine(indent + $"decimal {varName} = {inner};   // fg:{node.Id}【数值】（转发接进来的那一块）");
                return varName;
            }
            case "Energy":
                // 出口 0 =「能量」→ X；出口 1 =「额外资源量」→ Y
                if (outPort == 1)
                {
                    sb.AppendLine(indent + $"decimal {varName} = __Y;   // fg:{node.Id}【能量】额外资源量");
                    return varName;
                }
                sb.AppendLine(indent + $"decimal {varName} = __X;   // fg:{node.Id}【能量】");
                return varName;
            case "Power":
            {
                string powerId = node.Get("PowerId");
                if (string.IsNullOrWhiteSpace(powerId)) return null;
                var inNode = IncomingNode(g, node, 0);
                string who = (inNode is null ? null : UnitExpr(g, inNode)) ?? ValueDefaultUnit;
                string amountCall = $"ForgeFormulaRuntime.PowerAmount({who}, \"{powerId}\")";
                // V0.2.246（用户报「形态生效、效果没生效」）：**只叠一层（Single）**的自定义状态，
                // 本体的 <c>PowerStackType.Single</c> 只是「层数不显示」，<c>PowerCmd.Apply</c> 第二次照样
                // <c>SetAmount(2)</c>（内部层数变 2）。而图上「等于 1 层」是按语义写的最自然写法 ——
                // 一旦这个状态被施加过两次，条件不成立、整段效果被跳过（图标还在，看起来像「效果没生效」）。
                // 所以读「只叠一层」的状态时**按语义夹成 0 / 1**（有它就是 1 层、没有就是 0），
                // 让「等于 1 / 小于 1 / 大于等于 1」全都对得上；不碰模型本身（衰减 / 移除都不受影响）。
                if (IsSingleLayerCustomPower(p, powerId))
                    amountCall = $"({amountCall} <= 0m ? 0m : 1m)";
                sb.AppendLine(indent + $"decimal {varName} = {amountCall};   // fg:{node.Id}【状态】");
                return varName;
            }
            // V0.2.192（用户要求「在锻造增加状态列表」）：【状态列表】—— 一群状态的「个数 / 层数合计」。
            // 由它 + 【关系符】就能写「身上有任意减益」（个数 > 0）、「状态数 ≥ N」这类判断。
            case ForgeGraphCatalog.PowerListKind:
            {
                string filter = ForgeGraphCatalog.PowerListFilterOf(node);
                string powerId = string.Equals(filter, ForgeGraphCatalog.PowerListFilterOne, StringComparison.Ordinal)
                    ? node.Get("PowerId").Trim()
                    : "";
                if (string.Equals(filter, ForgeGraphCatalog.PowerListFilterOne, StringComparison.Ordinal)
                    && powerId.Length == 0) return null;      // 没选状态：什么都不生成（校验器会报错）
                bool layers = string.Equals(ForgeGraphCatalog.PowerListWhatOf(node),
                    ForgeGraphCatalog.PowerListWhatLayers, StringComparison.Ordinal);
                var inNode = IncomingNode(g, node, 0);
                string who = (inNode is null ? null : UnitExpr(g, inNode)) ?? ValueDefaultUnit;
                sb.AppendLine(indent + $"decimal {varName} = ForgeFormulaRuntime.{(layers ? "PowerLayers" : "PowerCount")}"
                    + $"({who}, \"{filter}\", \"{powerId}\");   // fg:{node.Id}【状态列表】"
                    + $"{ForgeGraphCatalog.PowerListFilterZh(filter)}{(powerId.Length > 0 ? "（" + powerId + "）" : "")}"
                    + $"·{ForgeGraphCatalog.PowerListWhatZh(ForgeGraphCatalog.PowerListWhatOf(node))}");
                return varName;
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// 一个【对象】在生成代码里的两种形态：
    ///   · <see cref="Single"/> = 单个单位表达式（可能为 null，但**永远不会抛出**——挑不到目标时上面套着守卫）；
    ///   · <see cref="Foes"/>   = 「全部 / 随机」那种要用循环走的列表表达式。
    /// 为什么两种都给：本体的 API 里「打单体」和「打全体」是两套写法（Targeting / foreach），
    /// 生成时必须按对象的语义分派，不能一律塞一个表达式进去。
    ///
    /// <paramref name="PerFoe"/> = 这个对象是「**逐个动手**」还是「**一次动全体**」：
    ///   · true（默认）= 对 <see cref="Foes"/> 里每一个各做一次（给状态 / 回血 / 失去生命这类
    ///     「本来就没有群体版本」的效果）；
    ///   · false = 这个对象本身就有**群体版本的一次性命令**（【全体敌人】+ 造成伤害 →
    ///     <c>DamageCmd.Attack(...).TargetingAllOpponents(...)</c>）。
    /// 为什么要分开（用户实测报的）：【造成伤害】+【全体敌人】+「命中次数 4」写成
    /// <c>foreach (foe) Attack(…).WithHitCount(4)</c> = **每个敌人都打 4 下**，
    /// 而用户要的是「一次打全体、命中 4 次」。只有群体那个一次性命令才表达得了后者。
    /// </summary>
    /// <summary>
    /// 一个【对象】/【作用对象】解析出来的「打谁」。
    /// <para><b><paramref name="RandomEnemy"/></b>（V0.2.222，用户报的「对群数和命中次数在锻造里写在一起、
    /// 随机多段效果异常」）：这一档是「随机敌人」，**不能**拿 <paramref name="Foes"/>（那是全体敌人列表）
    /// 直接 <c>foreach</c> —— 以前就是这样，于是「随机敌人 + 命中次数 4」生成成
    /// <c>foreach (全体敌人) Attack(…).WithHitCount(4)</c> = **每个敌人各挨 4 下**（离谱地强）。
    /// 现在：伤害走本体的 <c>TargetingRandomOpponents</c>（每一下随机挑一个，就是本体「随机多段」的语义），
    /// 其余按单位施放的效果按「随机挑 N 个」的列表来（N = 命中次数，和「卡牌」页同一口径）。</para>
    /// </summary>
    private sealed record UnitRef(string? Single, string Foes, bool PerFoe = true, bool RandomEnemy = false, bool Dup = true)
    {
        public static readonly UnitRef Self = new("__fgSelf", "ForgeFormulaRuntime.AllAllies(__fgPlayer)");
        /// <summary>打出这张牌时玩家选中的那个目标（卡牌上这就是「指定单体敌人」的含义）。</summary>
        public static readonly UnitRef Target = new("__fgTarget", "ForgeFormulaRuntime.TargetOrFirst(__fgTarget, __fgPlayer)");
    }

    /// <summary>
    /// 「随机敌人」这一档的**候选列表**：从敌方列表里随机挑 <paramref name="countExpr"/> 个。
    /// <paramref name="dup"/> = 勾了「随机目标允许重复选中同一个敌人」：允许同一个敌人被挑中多次。
    /// </summary>
    private static string RandomFoeList(bool dup, string countExpr) =>
        dup
            ? $"Enumerable.Range(0, {countExpr}).Select(_ => ForgeFormulaRuntime.RandomEnemy(__fgPlayer, allowDuplicates: true)).OfType<Creature>().ToList()"
            : $"ForgeFormulaRuntime.EnemyList(__fgPlayer).OrderBy(_ => __fgPlayer.RunState.Rng.CombatTargets.NextInt(int.MaxValue)).Take({countExpr}).ToList()";

    /// <summary>自检用：【对象】模块解析出来的「单个单位」表达式（看「指定召唤物」有没有按名称取）。</summary>
    public static string UnitExprForCheck(CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec unit)
        => ResolveUnit(g, unit).Single ?? "（null）";

    /// <summary>
    /// 把一个【对象】模块解成「打谁」。
    /// 没有对象模块 / 认不出来 → 用 <paramref name="fallback"/>（卡牌默认是打出时选的目标）。
    /// </summary>
    private static UnitRef ResolveUnit(ForgeGraphSpec g, ForgeNodeSpec? node, UnitRef? fallback = null, int depth = 0)
    {
        var fb = fallback ?? UnitRef.Target;
        if (node is null || depth > 16) return fb;
        // 【对象】和【指向对象】的取值方式完全一样（同一份 Pick 候选）——
        // 区别只在语义：【对象】是「这条效果打谁」，【指向对象】是「这张牌选中谁」（见 EmitChain 的 Target 分支）。
        if (node.Kind is not ("Unit" or "Target"))
        {
            // 不是对象模块：顺着它的入口继续往上找（条件 / if 之类没有对象语义）
            var up = IncomingNode(g, node, 0);
            return up is null ? fb : ResolveUnit(g, up, fb, depth + 1);
        }
        bool dup = node.GetFlag("AllowDuplicate", true);
        return node.Get("Pick", "Self") switch
        {
            "Self" => UnitRef.Self,
            "ChosenAlly" => UnitRef.Self,
            "AllAllies" => new UnitRef("__fgSelf", "ForgeFormulaRuntime.AllAllies(__fgPlayer)"),
            "ChosenEnemy" => UnitRef.Target,
            "RandomEnemy" => new UnitRef("ForgeFormulaRuntime.RandomEnemy(__fgPlayer, allowDuplicates: " + (dup ? "true" : "false") + ")",
                                          "ForgeFormulaRuntime.EnemyList(__fgPlayer)", RandomEnemy: true, Dup: dup),
            // 随机敌人 + 不勾「允许重复选中」= 全体里随机挑一个：列表那条按「随机一个」走
            // ⚠「全体敌人」标记成 PerFoe: false —— 它自己就有群体版本的一次性命令
            //（【造成伤害】走 TargetingAllOpponents），不能退化成「逐个动手」（那样「命中次数 4」
            //  会变成「每个敌人各打 4 下」，见 UnitRef 的说明）。
            "AllEnemies" => new UnitRef("ForgeFormulaRuntime.FirstEnemy(__fgPlayer)", "ForgeFormulaRuntime.EnemyList(__fgPlayer)", PerFoe: false),
            // 需求：选「指定召唤物」时按下面那栏选的名称找那一只（以前这个选项和「随机召唤物」一样都是第一只）
            "ChosenSummon" => new UnitRef($"ForgeFormulaRuntime.NamedSummon(__fgPlayer, \"{node.Get("SummonId")}\")", "ForgeFormulaRuntime.SummonList(__fgPlayer)"),
            "RandomSummon" => new UnitRef("ForgeFormulaRuntime.FirstSummon(__fgPlayer)", "ForgeFormulaRuntime.SummonList(__fgPlayer)"),
            "AllSummons" => new UnitRef("ForgeFormulaRuntime.FirstSummon(__fgPlayer)", "ForgeFormulaRuntime.SummonList(__fgPlayer)"),
            // 全体单位 = 自己 + 全部友方 + 全部敌人（本体的攻击链已经会打「可打的敌人」，
            // 所以这条只对「给状态 / 回血」这类按列表逐个动手的效果有意义）
            "AllUnits" => new UnitRef("__fgSelf", "ForgeFormulaRuntime.AllUnits(__fgPlayer)"),
            _ => fb,
        };
    }

    /// <summary>[旧签名] 兼容内层调用：只要单个表达式。</summary>
    private static string? UnitExpr(ForgeGraphSpec g, ForgeNodeSpec node, int depth = 0) =>
        ResolveUnit(g, node, null, depth).Single;

    /// <summary>
    /// 【效果】模块「打谁」的解析（V0.2.129 起多了一档，优先级从高到低）：
    ///   ① 显式接在【效果】左边「指向对象」口上的【对象】/【指向对象】模块 —— **最高**（老图就是这样）；
    ///   ② 新编辑器里「作用对象」那一栏**被用户改过**（<see cref="EffectSpec.TargetSideCustom"/>）
    ///      → 按那一栏算（和【对象】模块的同一套档位：自己 / 指定队友 / 全体友方 / 指定敌人 /
    ///      全体敌人 / 随机敌人）；
    ///   ③ 老规矩：这条链上最近一个【指向对象】声明的目标（<c>st.CurrentUnit</c>）。
    /// ⚠ 老存档走的是 ①/③，和 V0.2.128 逐字同一条路（TargetSideCustom 在迁移时一律置 false）。
    /// </summary>
    private static UnitRef ResolveEffectUnit(ForgeGraphSpec g, ForgeNodeSpec node, GenState st, string scope = "Card")
    {
        var wired = IncomingNode(g, node, 2);
        if (wired is not null) return ResolveUnit(g, wired, st.CurrentUnit);
        // V0.2.194：**状态那一档**（「状态列表」）的默认目标**不是「打出时选中的那个敌人」** ——
        // 状态没有「打出这张牌的目标」，而「自定义状态」页那条老链对大多数效果一律写在自己身上
        //（获得格挡 / 回复生命 / 失去生命 / 最大生命 / 抽牌 / 能量 / 金币…全是 base.Owner），
        // 只有「打谁有意义」的那几种才看效果自己的「作用对象」那一栏（见 PowerUnit）。
        if (string.Equals(scope, ForgeTimings.PowerScope, StringComparison.Ordinal))
            return st.CurrentUnit ?? PowerUnit(node);
        var spec = node.EffectSpec;
        if (spec is not null && spec.TargetSideCustom) return TargetSideUnit(spec);
        // ⚠ 链上没有【指向对象】时 st.CurrentUnit 可能是 null —— 这正是 ResolveUnit 里
        //   `fallback ?? UnitRef.Target` 兜的那一档，这里必须跟着兜（以前是走 ResolveUnit 兜的）。
        return st.CurrentUnit ?? UnitRef.Target;
    }

    /// <summary>状态自己（只有它一个，**不带召唤物**）—— 「自定义状态」页那条链对这类效果的写法就是 base.Owner。</summary>
    private static readonly UnitRef PowerSelf = new("__fgSelf", "new List<Creature> { __fgSelf }");

    /// <summary>
    /// 状态那一档里「打谁有意义」的效果种类（<c>ApplyPower</c> 那一族 + 造成伤害 / 击晕 / 禁止格挡 / 暴露）：
    /// 只有它们才看 <see cref="EffectSpec.TargetSide"/>。别的种类老链一律写在自己身上，
    /// 走「作用对象」的话会**打错人**（实测：从「自定义状态」页搬过来的「失去 6 点格挡」会被打到第一个敌人身上）。
    /// </summary>
    private static readonly HashSet<string> PowerEnemyTargetKinds = new(StringComparer.Ordinal)
    {
        "Damage", "ApplyPower", "TempPower", "DoublePower", "ClearPowerOne", "PetApplyPower", "Expose", "Stun", "ForbidBlock",
    };

    /// <summary>
    /// 状态那块布上「这个效果打谁」的默认档（没有接【对象】模块、链上也没有【对象】时）：
    ///   · 「打谁有意义」的种类 → 看效果自己的「作用对象」，判据和「自定义状态」页那条链**逐条对应**
    ///     （那边也是 <c>switch (e.TargetSide)</c>，默认档 = 打第一个可打的敌人）；
    ///   · 其余 → 只给自己（<see cref="PowerSelf"/>）。
    /// </summary>
    private static UnitRef PowerUnit(ForgeNodeSpec node)
    {
        if (!PowerEnemyTargetKinds.Contains(node.Get("Effect", "Damage"))) return PowerSelf;
        var spec = node.EffectSpec ?? ForgeEffectBridge.Ensure(node);
        if (spec is null) return PowerSelf;
        var unit = TargetSideUnit(spec);
        // 「自己 / 指定队友」那一档：状态链上只给自己（TargetSideUnit 给的是「全体友方」= 自己 + 召唤物）
        return ReferenceEquals(unit, UnitRef.Self) ? PowerSelf : unit;
    }

    /// <summary>「作用对象」这一栏（<see cref="EffectSpec.TargetSide"/>）→ 打谁（和 <see cref="ResolveUnit"/> 的档位一一对应）。</summary>
    private static UnitRef TargetSideUnit(EffectSpec e)
    {
        bool dup = e.AllowDuplicates;
        return e.TargetSide switch
        {
            "Self" => UnitRef.Self,
            // 「指定队友」：锻造图里和【对象】的「指定队友」同一档（那一档也是自己 —— 见 ResolveUnit）
            "Ally" => UnitRef.Self,
            // 「全部友方」：节点图里的 AllAllies 档 = 自己 + 召唤物；勾了「不包括召唤物」时只剩自己
            //（V0.2.132 —— 不新增运行时助手，免得动到已生成工程的 ForgeFormulaRuntime 文本）。
            "AllAllies" => e.AllAlliesExcludePets
                ? new UnitRef("__fgSelf", "new List<Creature> { __fgSelf }")
                : new UnitRef("__fgSelf", "ForgeFormulaRuntime.AllAllies(__fgPlayer)"),
            "AllEnemies" => new UnitRef("ForgeFormulaRuntime.FirstEnemy(__fgPlayer)", "ForgeFormulaRuntime.EnemyList(__fgPlayer)", PerFoe: false),
            "RandomEnemies" => new UnitRef("ForgeFormulaRuntime.RandomEnemy(__fgPlayer, allowDuplicates: " + (dup ? "true" : "false") + ")",
                                          "ForgeFormulaRuntime.EnemyList(__fgPlayer)", RandomEnemy: true, Dup: dup),
            // 「全体单位」（V0.2.132 卡牌那栏新增的第 7 档）：节点图【对象】本来就有同一档，
            // 用同一个运行时助手（自己 + 召唤物 + 敌人）。
            "AllUnits" => new UnitRef("__fgSelf", "ForgeFormulaRuntime.AllUnits(__fgPlayer)"),
            // Enemy（默认）/ 认不出来 = 打出这张牌时选中的那个目标（老图的默认档）
            _ => UnitRef.Target,
        };
    }

    /// <summary>
    /// 【效果】模块 → 本体 API 调用。
    /// </summary>
    private static void EmitEffect(StringBuilder sb, CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node,
        EmitCtx ctx, GenState st, string indent)
    {
        // V0.2.129：【效果】模块和卡牌共用一套 EffectSpec 编辑器 —— 「用新编辑器改过」的模块
        // 先把 spec 回写成老字段（老模块不动），下面照旧全部读老字段，生成文本对老存档逐字不变。
        ForgeEffectBridge.SyncGraph(g);
        string kind = node.Get("Effect", "Damage");
        var def = EffectCatalog.FindKind(kind);
        sb.AppendLine();
        sb.AppendLine($"{indent}// ---- fg:{node.Id}【效果】{def.Display} ----");

        // 数值：优先「数值」入口（下标 1）接上来的那一块，其次模块自带的数字
        var (valueNode, valueNodePort) = IncomingNodeAt(g, node, 1);
        string? varName = valueNode is null ? null : ValueVar(p, g, valueNode, st, sb, indent, valueNodePort);
        // 数值（V0.2.128：那一栏也能写公式/参数）
        var amountN = ForgeNumbers.Resolve(p, node, "Amount", 6);
        string amount;
        if (varName is not null) amount = varName;
        else if (amountN.Runtime)
        {
            string amtVar = UniqueVar(node, "__fgAmt");
            sb.AppendLine(indent + $"decimal {amtVar} = {amountN.Expr};   // fg:{node.Id}【效果】数值（公式：{amountN.Text}）");
            amount = amtVar;
        }
        else amount = "(" + Lit((double)amountN.Value) + "m)";

        // V0.2.194：**状态那一档**支持「= 本状态层数」那几个开关（EffectSpec.AmountIsStack /
        // TimesIsStack / RepeatIsStack）—— 那是「自定义状态」页效果编辑器上的开关（用户 31 只状态里
        // 有 16 只用到它，是最常用的一栏）。卡牌 / 遗物 / 药水那三条链上做不了（老规矩照旧报错拦住，
        // 见 UnsupportedEffectSettings），状态上做得到：**base.Amount 就是这个状态自己的层数**。
        // 为什么不走【状态】模块 + 【数值】：那要用户自己填本状态的类名，而类名是自动生成的
        //（换个名字就断），迁移 / 从「自定义状态」页搬过来的配置也会平白报错。
        bool powerScope = string.Equals(ctx.Scope, ForgeTimings.PowerScope, StringComparison.Ordinal);
        var stackSpec = powerScope ? EffectFromNode(node) : null;
        if (stackSpec?.AmountIsStack == true)
        {
            amount = "((decimal)base.Amount)";
            sb.AppendLine(indent + $"// 数值 = **本状态层数**（「自定义状态」页那个开关；base.Amount 就是这只状态自己的层数）");
        }

        // 指向对象：「指向对象」入口（下标 2）接的对象模块；
        // 没接就用**这条链上最近一个【指向对象】声明的那一只**（用户要求：那是「这张牌选中谁」），
        // 链上没有【指向对象】（老图）才退回老规矩「打出时选中的目标」。
        // V0.2.129：新编辑器里把卡牌那栏「作用对象」也摆出来了 —— 只有用户**真的动过它**
        // （TargetSideCustom）才让这一栏接管；老存档没动过 = 老规矩，生成文本一个字符都不变。
        var unit = ResolveEffectUnit(g, node, st, ctx.Scope);
        string single = unit.Single ?? "__fgTarget";
        string foes = unit.Foes;
        // 「战斗状态」在这几个类上写法不一样（本体：CombatState 是 CardModel 上的属性）：
        //   卡牌 → base.CombatState（和卡牌那条老链一致）；
        //   遗物 / 药水 → base.Owner.Creature.CombatState（那两条老链就是这么写的）；
        //   V0.2.194 状态（「状态列表」）→ base.Owner.CombatState（PowerModel 的 Owner 就是 Creature）。
        // ⚠ 以前这里一律写 base.CombatState —— 锻造遗物上用「造成伤害（全体敌人）」/「生成卡牌」
        //   生成出来是 CS0117（RelicModel 未包含 CombatState），整个工程编译不过（探针真编译踩到）。
        string combatState = string.Equals(ctx.Scope, "Card", StringComparison.Ordinal)
            ? "base.CombatState"
            : string.Equals(ctx.Scope, ForgeTimings.PowerScope, StringComparison.Ordinal)
                ? "base.Owner.CombatState"
                : "base.Owner.Creature.CombatState";

        // 施加 / 扣除 / 翻倍状态这类效果要的那个状态（本体 Power 类名；global using 已经把
        // MegaCrit.Sts2.Core.Models.Powers 引进来，所以短名直接用，和「卡牌」页那套生成一致）
        string powerId = node.Get("PowerId");

        // 【生成卡牌】/【变化卡牌】要哪张卡：模块上选的是「已有的卡」——**本体卡或你自己的卡都行**。
        // ⚠ V0.2.187（用户实测：挖肉补疮「自己选 3 张手牌**变化为昆虫防御**、生成 3 张感染」，
        //   实际是「随机变化」+ 第二条效果整条消失）：以前这里**只在 p.AllCards（自己存档的卡）里找**，
        //   用户选的是**本体卡**（MinionSacrifice / Infection）→ 找不到 → spawnCardClass 是 null →
        //     · 【变化卡牌】退化成 `CardCmd.TransformToRandom`（随机变化，用户看到「变出来的不是我要的那张」）；
        //     · 【生成卡牌】被当成「缺必填的东西」整条丢掉（生成的代码里只留一行警告注释）。
        //   本体卡就按用户选的那个类名原样用，交给 CSharpCodeGen.CardTypeExpr 写全名
        //   （它对我们自己的卡写短名、对本体卡写 MegaCrit.Sts2.Core.Models.Cards.<X>，而且已经是全名时原样返回）。
        string? spawnCardClass = null;
        string spawnCardId = node.Get("SpawnCardId");
        if (spawnCardId.Length > 0)
        {
            var targetCard = p.AllCards.FirstOrDefault(c => c is not null && string.Equals(c.ClassName, spawnCardId, StringComparison.Ordinal));
            spawnCardClass = targetCard is not null
                ? Naming.From(p).CardClassName(p, targetCard)
                : spawnCardId.Trim();
        }

        // 生效次数 / 命中次数 / 概率（V0.2.128：这三栏也能写公式/参数）
        //   · 纯数字 / 折叠成常数 → 走老路（生成文本一个字都不变）；
        //   · 运行时公式 → 先算成一个局部变量，再照原样用（循环上界 / WithHitCount / 概率判定）。
        var timesN = ForgeNumbers.Resolve(p, node, "Times", 1);
        int times = timesN.Runtime ? 1 : Math.Max(1, (int)Math.Round((double)timesN.Value));
        string timesExpr = Lit(times);
        if (timesN.Runtime)
        {
            timesExpr = UniqueVar(node, "__fgTimes");
            sb.AppendLine(indent + $"int {timesExpr} = Math.Max(1, (int)({timesN.Expr}));   // fg:{node.Id}【效果】生效次数（公式：{timesN.Text}）");
        }

        var hitsN = ForgeNumbers.Resolve(p, node, "Hits", 1);
        int hits = hitsN.Runtime ? 1 : Math.Max(1, (int)Math.Round((double)hitsN.Value));
        // 写了公式的命中次数**一律按多段攻击写**（和卡牌链的 CSharpCodeGen.RepeatIsMulti 同一个道理：
        // 生成期不知道公式算出来是几，不能因为「固定值那条算出来是 1」就省掉 .WithHitCount）；
        // 公式算出 1 时 WithHitCount(1) 与不写它**语义相同**。
        bool hitsMulti = hitsN.Runtime || hits > 1;
        string hitsExpr = Lit(hits);
        if (hitsN.Runtime)
        {
            hitsExpr = UniqueVar(node, "__fgHits");
            sb.AppendLine(indent + $"int {hitsExpr} = Math.Max(1, (int)({hitsN.Expr}));   // fg:{node.Id}【效果】命中次数（公式：{hitsN.Text}）");
        }

        // V0.2.194：状态那一档还有「生效次数 / 命中次数 = 本状态层数」两个开关（同 AmountIsStack 的说明）
        if (stackSpec?.TimesIsStack == true)
        {
            timesExpr = "((int)base.Amount)";
            sb.AppendLine(indent + "// 生效次数 = **本状态层数**（循环上界用 base.Amount）");
        }
        if (stackSpec?.RepeatIsStack == true)
        {
            hitsExpr = "((int)base.Amount)";
            hitsMulti = true;
            sb.AppendLine(indent + "// 命中次数 = **本状态层数**（多段攻击的段数用 base.Amount）");
        }

        // V0.2.222：「随机敌人」的候选列表 —— 以前直接用 EnemyList（**全体敌人**）去 foreach，
        // 于是「随机敌人 + 命中次数 4」= 每个敌人各挨 4 下（用户报的「随机多段效果异常」）。
        // 现在：按「命中次数」随机挑 N 个（不勾「允许重复」= 不重复），伤害那条路另走本体
        // 的 TargetingRandomOpponents（见下面 "Damage" when unit.RandomEnemy）。
        if (unit.RandomEnemy) foes = RandomFoeList(unit.Dup, hitsExpr);

        var chanceN = ForgeNumbers.Resolve(p, node, "Chance", 100);
        int chance = chanceN.Runtime ? 100 : (int)Math.Round((double)chanceN.Value);
        bool chanceRoll = chanceN.Runtime || chance < 100;
        string chanceExpr = Lit(chance);
        if (chanceN.Runtime)
        {
            chanceExpr = UniqueVar(node, "__fgChance");
            sb.AppendLine(indent + $"int {chanceExpr} = (int)({chanceN.Expr});   // fg:{node.Id}【效果】概率生效（公式：{chanceN.Text}）");
        }
        bool nextTurn = node.GetFlag("NextTurn");
        if (nextTurn && !EffectCatalog.HonorsNextTurn(kind))
        {
            sb.AppendLine(indent + "// ⚠「下回合生效」这一档在节点图里做不了（本体没有对应的延迟落点），这一条按立即生效生成。");
            if (kind == "TempPower")
                sb.AppendLine(indent + "//（「临时增益」想要『撑过这一回合、下回合开始时才撤』请用下面那栏「什么时候撤掉」选「下回合开始时才撤掉」，"
                    + "不要勾这个「下回合生效」。）");
            nextTurn = false;
        }
        // V0.2.170：「下回合生效」铺到**全部做得了的种类**（用户点的第 2 条）。
        // 以前这里只放「获得格挡 / 抽牌 / 施加状态」三种（别的种类一律退成立即生效）——
        // 现在除「获得格挡 / 抽牌」（这两条本来就有本体现成的 BlockNextTurnPower / DrawCardsNextTurnPower）之外，
        // 其余**所有** EffectCatalog.HonorsNextTurn 的种类一律复用卡牌页那套「挂一个延迟状态，
        // 下回合开始时跑同一套发射器」（见 CSharpCodeGen.EmitDelayedPowerApply / DelayedPowersSource）。
        // ⚠ 那些延迟状态类是**用到才生成**的（CSharpCodeGen.CollectDelayedEffects 已把节点图的效果也收进去）。
        if (nextTurn && kind is not ("Block" or "Draw"))
        {
            // 「获得能量 / 额外资源量」这两条在卡牌页用的是**本体现成的** EnergyNextTurnPower /
            // StarNextTurnPower（不是通用延迟状态），节点图照同一份口径走（见 CSharpCodeGen 的
            // case "Energy" / "ExtraResource"）—— 不然会去引用一个根本不会生成的延迟类（CS0246，探针实测）。
            bool negAmount = !amountN.Runtime && amountN.Value < 0m;
            if (kind == "Energy")
            {
                ViaCodeWriterAppend(sb, indent, w => w.Line(negAmount
                    ? $"await PowerCmd.Apply<{Naming.From(p).EnergyNextTurnDebtPowerClass}>({ctx.ChoiceVar}, __fgSelf, {Lit((double)Math.Abs(amountN.Value))}m, __fgSelf, {ctx.CardSelfExpr});   // 下回合少 {Math.Abs(amountN.Value):0.##} 点能量"
                    : $"await PowerCmd.Apply<EnergyNextTurnPower>({ctx.ChoiceVar}, __fgSelf, {amount}, __fgSelf, {ctx.CardSelfExpr});"));
                return;
            }
            if (kind == "ExtraResource" && !negAmount)
            {
                ViaCodeWriterAppend(sb, indent, w => w.Line(
                    $"await PowerCmd.Apply<StarNextTurnPower>({ctx.ChoiceVar}, __fgSelf, {amount}, __fgSelf, {ctx.CardSelfExpr});   // 下回合开始时获得额外资源量"));
                return;
            }
            if (kind == "ExtraResource")
            {
                sb.AppendLine(indent + "// ⚠「下回合生效」+ 负数的额外资源量：本体 StarNextTurnPower 只会加不会减，这一条按立即生效生成。");
            }
            else
            {
                ViaCodeWriterAppend(sb, indent, w =>
                    CSharpCodeGen.EmitDelayedPowerApply(w, p, EffectFromNode(node), "__fgSelf", "__fgSelf", ctx.ChoiceVar));
                return;
            }
        }

        // V0.2.170：「斩杀」条件（用户点的第 3 条）—— 它判的是「上一条攻击」的结果，生成出来引用
        // __lastAttack / __fatalOk 两个局部变量（和卡牌页同一份写法，见 CSharpCodeGen 的 usesFatal 那一段）。
        // 所以：图里用了这个条件时，在**这个执行点的序幕**里声明它们，「造成伤害」那一条把结果记进去。
        // 只有卡牌档 + 拿得到 cardPlay 的时机才做得了（遗物 / 药水 / 嵌进钩子的那几条没有攻击上下文），
        // 做不了的组合由校验器报错拦住（不静默生成 CS0103）。
        bool fatalDecl = ctx.Scope == "Card" && GraphUsesFatal(g);
        bool fatalCap = fatalDecl && ctx.RealCardPlay;
        string fatalPre = fatalCap
            ? "__fatalOk = cardPlay.Target is null || cardPlay.Target.Powers.All(pm => pm.ShouldOwnerDeathTriggerFatal()); "
            // 拿不到「打出时选中的目标」的时机（被消耗 / 被丢弃 / 嵌进卡牌钩子）：判据只能是 false
            //（校验器会给一条警告说清「要挪到『打出一张牌时』那一段」）。
            : fatalDecl
                ? "__fatalOk = false;   // 这个时机拿不到「打出时选中的目标」，斩杀条件不会成立 "
                : "";
        string fatalAsg = fatalDecl ? "__lastAttack = " : "";

        // V0.2.194：「本张牌这一次打出造成的伤害 / 获得的格挡」—— 图上也照卡牌页那套记账，
        // 记的是**同一张卡的那个属性**（声明 / 打出时清零在 CSharpCodeGen.CardSourceCore 里做）。
        //   · 只在「卡牌被打出时」（ctx.IsCardPlay）那个执行点上记：别的卡牌时机跑的时候
        //     打出那次清零早过去了，记进去就变成「累加到上一次打出」；
        //   · 伤害用**这个模块自己的**临时变量接攻击结果（和卡牌页同一个理由：有些分支可能没真的打，
        //     去读 __lastAttack 会把上一条攻击的结果再算一遍）；
        //   · 口径同本体：伤害 = Sum(r => r.TotalDamage)（含被格挡的部分）；
        //     格挡 = CreatureCmd.GainBlock 的返回值（实际拿到多少，含敏捷等加成）。
        bool thisCardCap = ctx.IsCardPlay && GraphUsesThisCardDamage(g);
        bool thisBlockCap = ctx.IsCardPlay && GraphUsesThisCardBlock(g);
        string atkVar = thisCardCap ? UniqueVar(node, "__fgAtk") : "";
        string dmgAsg = thisCardCap
            ? $"MegaCrit.Sts2.Core.Commands.Builders.AttackCommand? {atkVar} = "
            : fatalAsg;
        string dmgTail = thisCardCap
            ? $" if ({atkVar} is not null) {CSharpCodeGen.ThisCardDamageField} += (decimal){atkVar}.Results.SelectMany(hits => hits).Sum(r => r.TotalDamage);"
              + (fatalDecl ? $" __lastAttack = {atkVar};" : "")
            : "";
        string blockAcc = thisBlockCap ? $"{CSharpCodeGen.ThisCardBlockField} += " : "";

        // 选牌类效果（消耗 / 丢弃 / 升级 / 从某一摞拿牌）要弹本体的选牌界面，提示语是 LocString，
        // 只能内联生成（静态辅助类里没有 base）。这几句自己写进 sb，写完直接收工。
        if (kind is "ExhaustCard" or "DiscardCard" or "UpgradeCard")
        {
            EmitCardPick(sb, ctx, node, kind, amount, indent, p);
            return;
        }
        if (kind is "TakeFromDraw" or "TakeFromDiscard" or "TakeFromExhaust")
        {
            string pile = kind switch
            {
                "TakeFromDraw" => "PileType.Draw",
                "TakeFromDiscard" => "PileType.Discard",
                _ => "PileType.Exhaust",
            };
            // V0.2.170：「取几张」四档（用户点的第 2 条）。「全部」不弹界面，直接那一摞全拿。
            var tfE = ForgeEffectBridge.Ensure(node);
            // V0.2.221：「范围界定 / 只对这张卡（指定）」在这个钩子上也要真的筛
            //（用户报的「消耗丢弃升级指定卡没生效」是同一类：图上以前一个谓词都没传）。
            string? tfPred = CSharpCodeGen.CardScopePredicate(tfE, p);
            CountModeMarker(sb, indent, tfE);
            // V0.2.220：「为抽到的牌加关键词」—— 多这一段（拿到手之后再给它们加关键词）。
            // 没选关键词时下面两档一个字都不变（老图逐字不变）。
            if (tfE.HasDrawnKeyword)
            {
                if (tfE.CountIsAll)
                {
                    EmitRepeatLoop(sb, indent, timesExpr, __ind =>
                    {
                        sb.AppendLine(__ind + $"var __fgAllTaken = CardPile.GetCards(__fgPlayer, {pile}){GraphWhere(tfPred)}.ToList();");
                        sb.AppendLine(__ind + "if (__fgAllTaken.Count > 0) await CardPileCmd.Add(__fgAllTaken, PileType.Hand);");
                        ViaCodeWriterAppend(sb, __ind, w => CSharpCodeGen.EmitDrawnKeyword(w, p, tfE, "__fgAllTaken", "__fgSelf", ctx.CardSelfExpr));
                    });
                    return;
                }
                string kwPrefs = CSharpCodeGen.SelectPrefsExpr(tfE, "base.SelectionScreenPrompt", $"(int)({amount})");
                EmitRepeatLoop(sb, indent, timesExpr, __ind =>
                {
                    sb.AppendLine(__ind + "{");   // 这段自己的局部变量（__fgTake）包一层块作用域，和上面那档同名不冲突
                    sb.AppendLine(__ind + $"    var __fgTake = (await CardSelectCmd.FromCombatPile({ctx.ChoiceVar}, {pile}.GetPile(__fgPlayer), __fgPlayer, {kwPrefs}{GraphFilterArg(tfPred)})).ToList();");
                    sb.AppendLine(__ind + "    if (__fgTake.Count > 0) await CardPileCmd.Add(__fgTake, PileType.Hand);");
                    ViaCodeWriterAppend(sb, __ind + "    ", w => CSharpCodeGen.EmitDrawnKeyword(w, p, tfE, "__fgTake", "__fgSelf", ctx.CardSelfExpr));
                    sb.AppendLine(__ind + "}");
                });
                return;
            }
            if (tfE.CountIsAll)
            {
                EmitRepeatLoop(sb, indent, timesExpr, __ind =>
                {
                    sb.AppendLine(__ind + $"foreach (CardModel __fgTake in CardPile.GetCards(__fgPlayer, {pile}){GraphWhere(tfPred)}.ToList())");
                    sb.AppendLine(__ind + "    await CardPileCmd.Add(__fgTake, PileType.Hand);");
                });
                return;
            }
            string tfPrefs = CSharpCodeGen.SelectPrefsExpr(tfE, "base.SelectionScreenPrompt", $"(int)({amount})");
            EmitRepeatLoop(sb, indent, timesExpr, __ind =>
            {
                sb.AppendLine(__ind + $"foreach (CardModel __fgTake in (await CardSelectCmd.FromCombatPile({ctx.ChoiceVar}, {pile}.GetPile(__fgPlayer), __fgPlayer, {tfPrefs}{GraphFilterArg(tfPred)})).ToList())");
                sb.AppendLine(__ind + "    await CardPileCmd.Add(__fgTake, PileType.Hand);");
            });
            return;
        }
        // 变化 / 复制（本体「下去 / 二刀流」那两套写法）
        if (kind is "TransformCard" or "CopyCard")
        {
            var cvSpec = GraphSpec(node);
            // V0.2.170：「取卡方式」四档（用户点的第 1 条）—— 选了「按范围随机 / 多选1 / 从范围里自己挑一张」
            // 时「变成什么」由这一栏决定，而卡牌页那份实现（CSharpCodeGen.EmitTransformCard）已经把这四档
            // 全写全了（含「从哪里选牌」「取几张」「附加处理」「加关键词」），所以整段复用，不再抄第二遍。
            if (kind == "TransformCard" && cvSpec is { IsSpawnRandom: true })
            {
                ViaCodeWriterAppend(sb, indent, w => CSharpCodeGen.EmitTransformCardPublic(w, cvSpec!, p,
                    cardSource: ctx.CardSelfExpr, countExpr: $"(int)({amount})", playerExpr: "__fgPlayer",
                    creatureExpr: "__fgSelf", sourceExpr: ctx.CardSelfExpr, combatStateExpr: combatState,
                    choiceExpr: ctx.ChoiceVar));
                return;
            }
            bool chosen = !string.Equals(node.Get("Pick", "Chosen"), "Random", StringComparison.OrdinalIgnoreCase);
            // 变量名按模块 Id 取，**每条效果一套** —— 同一条链上有两个同种效果时不会撞名（编译期就报 CS0128）
        // V0.2.286（用户报「猩红创痕：变化效果数值给 0 会卡死」）：这条链上的「取几张」是**字面 0** 时，
        // 整个效果模块跳过 —— 本体 CardSelectorPrefs(提示, 0) 的选择界面会一直等一个不可能成立的确认。
        // ⚠ 只对「必须弹选择框」的这几档生效：复制卡牌 / 生成卡牌 的 0 有专门含义（= 这张牌自己），不能跳过。
        if (kind is "TransformCard" or "TransformCardGlobal" or "UpgradeCard" or "Scry" && IsZeroGraphCount(amount))
        {
            sb.AppendLine($"{indent}// fg:{node.Id}【{def.Display}】取 0 张：这条效果什么都不做（选 0 张会让本体的选择框一直等待 → 卡死）");
            return;
        }
            string selVar = UniqueVar(node, "__fgSel");
            string? cvTempUp = GraphTempUpVar(node, cvSpec);
            int copies = kind == "CopyCard" ? Math.Max(1, cvSpec?.Copies ?? 1) : 1;
            bool cvMods = NeedsGraphSpawnModifiers(cvSpec) || copies > 1;
            // V0.2.170：「取几张」四档（用户点的第 2 条）—— 「全部」= 那一摞每一张都来一遍，不弹界面。
            var cvE = ForgeEffectBridge.Ensure(node);
            // V0.2.221：「只变化这张卡 / 只对这张卡 + 范围界定」在图上也要真的筛（以前 filter 一律写 null）。
            string? cvPred = kind == "TransformCard"
                ? CSharpCodeGen.TransformSourcePredicate(cvSpec ?? cvE, p)
                : CSharpCodeGen.CardScopePredicate(cvE, p);
            CountModeMarker(sb, indent, cvE);
            bool cvAll = cvE.CountIsAll;
            string cvPrefs = CSharpCodeGen.SelectPrefsExpr(cvE, "base.SelectionScreenPrompt", $"(int)({amount})");
            // V0.2.226：「复制卡牌」的「复制到哪一摞」和「数值 0 = 复制这张牌自己」（卡牌页那份同一套）
            var (copyDest, copyDestPos, copyDestZh) = CSharpCodeGen.CopyDest(cvE);
            string copyDestArg = copyDest + copyDestPos;
            // 数值 0 = 这张牌自己：整段选牌都跳过，直接把这张牌本身克隆 N 份（只有卡牌布上有「这张牌」）
            if (kind == "CopyCard" && CSharpCodeGen.AmountZeroIsSelf(cvE))
            {
                if (string.Equals(ctx.CardSelfExpr, "null", StringComparison.Ordinal))
                {
                    sb.AppendLine(indent + "// ⚠【效果】复制卡牌：数值 0 = 这张牌自己，但这块布上没有「这张牌」（只有卡牌 / 卡牌的锻造图才有）"
                        + " —— 这一块什么都不做，请把数值改成 ≥ 1 张。");
                    return;
                }
                // 「仅本回合升级」要先挂那个临时状态（和下面非 0 那条路同一个调用）
                EmitGraphTempUpgradeApply(sb, p, cvSpec, ctx, indent, cvTempUp);
                ViaCodeWriterAppend(sb, indent, w => CSharpCodeGen.EmitCopySelfPublic(w, p, cvE,
                    ctx.CardSelfExpr, "__fgPlayer", "__fgSelf", cvTempUp));
                return;
            }
            EmitRepeatLoop(sb, indent, timesExpr, __ind =>
            {
                if (cvAll)
                    sb.AppendLine(__ind + $"var {selVar} = ({GraphAllPickExpr(cvSpec)}{GraphWhere(cvPred)}).ToList();");
                else if (chosen)
                    sb.AppendLine(__ind + (IsHandPick(cvSpec)
                        ? $"var {selVar} = (await CardSelectCmd.FromHand(context: {ctx.ChoiceVar}, player: __fgPlayer, prefs: {cvPrefs}, filter: {cvPred ?? "null"}, source: {ctx.CardSelfExpr})).ToList();"
                        : $"var {selVar} = (await CardSelectCmd.FromCombatPile({ctx.ChoiceVar}, {GraphPileExpr(cvSpec)}, __fgPlayer, {cvPrefs}{GraphFilterArg(cvPred)})).ToList();"));
                else
                    sb.AppendLine(__ind + $"var {selVar} = ({ctx.ChoiceVar} is null ? new List<CardModel>() : {GraphRandomPickExpr(cvSpec, CSharpCodeGen.RangeN(cvE, $"(int)({amount})"))}{GraphWhere(cvPred)});");
                if (kind == "CopyCard")
                {
                    // V0.2.167：勾了「复制的份数」（> 1）/ 附加处理 / 加关键词时，要拿得到复制出来的那张实例
                    // V0.2.226：复制到哪一摞由【效果】的「放到哪一摞」决定（默认手牌 = 老文本一个字不变）
                    if (!cvMods)
                    {
                        sb.AppendLine(__ind + $"foreach (CardModel {selVar}C in {selVar}) await CardPileCmd.AddGeneratedCardToCombat({selVar}C.CreateClone(), {copyDestArg}, __fgPlayer);");
                    }
                    else
                    {
                        EmitGraphTempUpgradeApply(sb, p, cvSpec, ctx, __ind, cvTempUp);
                        sb.AppendLine(__ind + $"foreach (CardModel {selVar}C in {selVar})");
                        sb.AppendLine(__ind + "{");
                        sb.AppendLine(__ind + $"    for (int __fgCopy = 0; __fgCopy < {copies}; __fgCopy++)");
                        sb.AppendLine(__ind + "    {");
                        sb.AppendLine(__ind + $"        CardModel __clone = {selVar}C.CreateClone();");
                        EmitGraphSpawnModifiers(sb, p, cvSpec, __ind + "        ", "__clone", cvTempUp);
                        sb.AppendLine(__ind + $"        await CardPileCmd.AddGeneratedCardToCombat(__clone, {copyDestArg}, __fgPlayer);");
                        sb.AppendLine(__ind + "    }");
                        sb.AppendLine(__ind + "}");
                    }
                }
                else if (spawnCardClass is null)
                {
                    if (!cvMods)
                        sb.AppendLine(__ind + $"foreach (CardModel {selVar}C in {selVar}) await CardCmd.TransformToRandom({selVar}C, __fgPlayer.RunState.Rng.CombatCardSelection);");
                    else
                    {
                        // 变化是**新建一张再把原卡移掉**，只有返回值里的 cardAdded 才是变出来的那张（卡牌那条链同款写法）
                        EmitGraphTempUpgradeApply(sb, p, cvSpec, ctx, __ind, cvTempUp);
                        sb.AppendLine(__ind + $"foreach (CardModel {selVar}C in {selVar})");
                        sb.AppendLine(__ind + "{");
                        sb.AppendLine(__ind + $"    var {selVar}Tr = await CardCmd.TransformToRandom({selVar}C, __fgPlayer.RunState.Rng.CombatCardSelection);");
                        sb.AppendLine(__ind + $"    CardModel? {selVar}New = {selVar}Tr.cardAdded;");
                        sb.AppendLine(__ind + $"    if ({selVar}New is not null)");
                        sb.AppendLine(__ind + "    {");
                        EmitGraphSpawnModifiers(sb, p, cvSpec, __ind + "        ", $"{selVar}New", cvTempUp);
                        sb.AppendLine(__ind + "    }");
                        sb.AppendLine(__ind + "}");
                    }
                }
                else
                {
                    if (!cvMods)
                        sb.AppendLine(__ind + $"foreach (CardModel {selVar}C in {selVar}) await CardCmd.Transform({selVar}C, __fgPlayer.RunState.CreateCard(ModelDb.Card<{CSharpCodeGen.CardTypeExpr(p, spawnCardClass)}>(), __fgPlayer));");
                    else
                    {
                        EmitGraphTempUpgradeApply(sb, p, cvSpec, ctx, __ind, cvTempUp);
                        sb.AppendLine(__ind + $"foreach (CardModel {selVar}C in {selVar})");
                        sb.AppendLine(__ind + "{");
                        sb.AppendLine(__ind + $"    var {selVar}Tr = await CardCmd.Transform({selVar}C, __fgPlayer.RunState.CreateCard(ModelDb.Card<{CSharpCodeGen.CardTypeExpr(p, spawnCardClass)}>(), __fgPlayer));");
                        sb.AppendLine(__ind + $"    CardModel? {selVar}New = {selVar}Tr?.cardAdded;");
                        sb.AppendLine(__ind + $"    if ({selVar}New is not null)");
                        sb.AppendLine(__ind + "    {");
                        EmitGraphSpawnModifiers(sb, p, cvSpec, __ind + "        ", $"{selVar}New", cvTempUp);
                        sb.AppendLine(__ind + "    }");
                        sb.AppendLine(__ind + "}");
                    }
                }
            });
            return;
        }
        // 预见（看抽牌堆顶 N 张，想丢的丢进弃牌堆 —— 本体没有这个机制，照「卡牌」页那套拼）
        if (kind == "Scry")
        {
            string topVar = UniqueVar(node, "__fgTop");
            string dropVar = UniqueVar(node, "__fgDrop");
            // V0.2.221：「范围界定 / 只对这张卡」照样筛（图上以前没传）—— 只在这几栏填了时才多一段
            string? scryPred = CSharpCodeGen.CardScopePredicate(ForgeEffectBridge.Ensure(node), p);
            EmitRepeatLoop(sb, indent, timesExpr, __ind =>
            {
                // V0.2.225：用到「本回合预见过 / 预见次数」那两条条件时，每一轮 +1（和卡牌页那条「预见」同一口径）
                if (CSharpCodeGen.UsesScryCounter(p))
                    sb.AppendLine(__ind + $"{CSharpCodeGen.CombatCountersClass(p)}.BumpScry(__fgPlayer);   // 记一次「预见」");
                sb.AppendLine(__ind + $"var {topVar} = PileType.Draw.GetPile(__fgPlayer).Cards.Take((int)({amount})){GraphWhere(scryPred)}.ToList();");
                sb.AppendLine(__ind + $"if ({topVar}.Count > 0)");
                sb.AppendLine(__ind + "{");
                sb.AppendLine(__ind + $"    var {dropVar} = (await CardSelectCmd.FromSimpleGrid({ctx.ChoiceVar}, {topVar}, __fgPlayer, new CardSelectorPrefs(base.SelectionScreenPrompt, 0, {topVar}.Count))).ToList();");
                sb.AppendLine(__ind + $"    if ({dropVar}.Count > 0) await CardCmd.Discard(choiceContext, {dropVar});");
                sb.AppendLine(__ind + "}");
            });
            return;
        }
        // 获得药水（指定 / 随机）—— 药水栏满时本体 TryToProcure 什么都不做
        if (kind == "GainPotion")
        {
            string? potionClass = null;
            string potionId = node.Get("PotionId");
            if (potionId.Length > 0)
            {
                foreach (var pot in EffectCatalog.Potions)
                    if (string.Equals(pot.Id, potionId, StringComparison.Ordinal)) { potionClass = pot.Id; break; }
            }
            bool fixedPotion = !string.Equals(node.Get("PotionPick", "Fixed"), "Random", StringComparison.OrdinalIgnoreCase);
            EmitRepeatLoop(sb, indent, timesExpr, __ind =>
            {
                if (fixedPotion && potionClass is not null)
                    sb.AppendLine(__ind + $"await PotionCmd.TryToProcure<MegaCrit.Sts2.Core.Models.Potions.{potionClass}>(__fgPlayer);");
                else
                    sb.AppendLine(__ind + $"await PotionCmd.TryToProcure(MegaCrit.Sts2.Core.Factories.PotionFactory.CreateRandomPotionInCombat(__fgPlayer, __fgPlayer.RunState.Rng.CombatPotionGeneration), __fgPlayer);");
            });
            return;
        }
        // 卡牌奖励：**只能挂在「战斗胜利时」那一支上**（本体没有"战斗中直接弹奖励"的入口）
        if (kind == "CardReward")
        {
            if (!ctx.AllowCardReward)
            {
                sb.AppendLine(indent + "// ⚠【效果】获得卡牌奖励：本体只能在**战斗结算**里加一条奖励，");
                sb.AppendLine(indent + "//   所以这个效果只有在【触发时机】= 「战斗胜利时」的那一支上才会生成；");
                sb.AppendLine(indent + "//   挂在别的时机上不会生效 —— 请把这一块挪到「战斗胜利时」那条链上。");
                return;
            }
            sb.AppendLine(indent + "// 获得卡牌奖励：像本体「王国资产」那样加进本场战斗的结算奖励里（打赢后多一条「选一张卡」）");
            sb.AppendLine(indent + "if (room is not null)");
            sb.AppendLine(indent + "{");
            sb.AppendLine(indent + "    // 事件房本体 ForRoom 会直接抛异常，那里手造一份同样不带 flag 的（带 flag 的存档会抛 NotImplementedException）");
            sb.AppendLine(indent + "    MegaCrit.Sts2.Core.Runs.CardCreationOptions __fgRewardOptions = room.RoomType == MegaCrit.Sts2.Core.Rooms.RoomType.Event");
            sb.AppendLine(indent + "        ? new MegaCrit.Sts2.Core.Runs.CardCreationOptions(new CardPoolModel[] { base.Owner.Character.CardPool }, MegaCrit.Sts2.Core.Runs.CardCreationSource.Other, MegaCrit.Sts2.Core.Runs.CardRarityOddsType.RegularEncounter)");
            sb.AppendLine(indent + "        : MegaCrit.Sts2.Core.Runs.CardCreationOptions.ForRoom(base.Owner, room.RoomType);");
            sb.AppendLine(indent + $"    room.AddExtraReward(__fgPlayer, new MegaCrit.Sts2.Core.Rewards.CardReward(__fgRewardOptions, (int)({amount}), __fgPlayer));");
            sb.AppendLine(indent + "}");
            return;
        }

        // V0.2.170：「取卡方式」四档（用户点的第 1 条）—— 只要选了「按范围随机 / 多选1 / 从范围里自己挑一张」
        //（这三档在数据里都是 IsSpawnRandom；见 EffectFieldUsage），就**复用卡牌页那份实现**
        //（CSharpCodeGen.EmitGenerateCard）：范围池、候选、选牌界面、附加处理、加关键词它全写全了，
        // 只是「上下文表达式」不同（playerExpr / creatureExpr / playerArg / choiceExpr 都开成了参数）。
        if (kind == "GenerateCard" && GraphSpec(node) is { IsSpawnRandom: true } genRandom)
        {
            ViaCodeWriterAppend(sb, indent, w => CSharpCodeGen.EmitGenerateCard(w, p, genRandom,
                countExpr: $"(int)({amount})", tempUpSource: ctx.CardSelfExpr,
                playerExpr: "__fgPlayer", creatureExpr: "__fgSelf", playerArg: "__fgPlayer", choiceExpr: ctx.ChoiceVar));
            return;
        }

        // V0.2.167：「生成卡牌」勾了「附加处理 / 加关键词」时要**自己 CreateCard**（拿得到实例才改得动），
        // 所以单独在 switch 之前处理；没勾时走 switch 里那条一句写完的老路径（老图生成文本逐字不变）。
        var genSpec = GraphSpec(node);
        if (kind == "GenerateCard" && spawnCardClass is not null && NeedsGraphSpawnModifiers(genSpec))
        {
            string genPile = node.Get("SpawnTo", "Hand");
            string? genTempUp = GraphTempUpVar(node, genSpec);
            EmitRepeatLoop(sb, indent, timesExpr, __ind2 =>
            {
                EmitGraphTempUpgradeApply(sb, p, genSpec, ctx, __ind2, genTempUp);
                sb.AppendLine(__ind2 + $"for (int __fgN = 0; __fgN < (int)({amount}); __fgN++)");
                sb.AppendLine(__ind2 + "{");
                sb.AppendLine(__ind2 + $"    CardModel __gen = {combatState}.CreateCard<{CSharpCodeGen.CardTypeExpr(p, spawnCardClass)}>(__fgPlayer);");
                EmitGraphSpawnModifiers(sb, p, genSpec, __ind2 + "    ", "__gen", genTempUp);
                sb.AppendLine(__ind2 + $"    await CardPileCmd.AddGeneratedCardToCombat(__gen, PileType.{genPile}, __fgPlayer);");
                sb.AppendLine(__ind2 + "}");
            });
            return;
        }

        // 按「逐个动手」写的那几种（对每个目标循环一次）
        // 【给伙伴施加状态】：目标是**召唤物**，所以单独算一份（见下面 switch 里的 PetApplyPower）。
        // 空 = 没选状态（那就什么都不生成，校验器会报一条「没选状态」）。
        string? petBody = null;
        if (kind == "PetApplyPower" && powerId.Length > 0)
        {
            var unitNode = IncomingNode(g, node, 2);
            string pick = unitNode is { Kind: "Unit" } ? unitNode.Get("Pick", "Self") : "";
            string one = pick == "ChosenSummon"
                ? $"ForgeFormulaRuntime.NamedSummon(__fgPlayer, \"{unitNode!.Get("SummonId")}\")"
                : "ForgeFormulaRuntime.FirstSummon(__fgPlayer)";
            string call = DebugLogGen.PowerBefore(ApplyPowerEffectOf(node), "__fgPet", powerId, amount)
                + $"await PowerCmd.Apply<{powerId}>({ctx.ChoiceVar}, __fgPet, {amount}, __fgSelf, {ctx.CardSelfExpr});"
                + DebugLogGen.PowerAfter(ApplyPowerEffectOf(node), "__fgPet", powerId);
            petBody = pick == "AllSummons"
                ? $"foreach (Creature __fgPet in ForgeFormulaRuntime.SummonList(__fgPlayer)) {{ {call} }}"
                : $"{{ Creature? __fgPet = {one}; if (__fgPet is not null) {{ {call} }} }}";
        }
        // 「生成调试日志」（V0.2.142，默认关）：这一段的效果属于「这张布上的这一个【效果】节点」。
        // 关着时这个 using 什么都不做、下面那几个内联日志助手也一律返回空串 —— 生成文本逐字节不变（红线①）。
        string body;
        // ⚠ V0.2.200（用户实测「敌人回合开始时卡死」）：只有**卡牌档**才有「这张牌」（CardSelfExpr = this）
        // 和打出记录（cardPlay）；状态 / 遗物 / 药水档的 ctx 里这两个都是 "null"。
        // 而本体 `AttackCommand.FromCard(CardModel card, CardPlay? cardPlay)` 的第一句就是
        // `Player owner = card.Owner;` —— 传 null 会在**运行时** NRE；它又是在钩子里抛的，
        // 会把战斗的回合循环直接打断（栈：AttackCommand.FromCard ← Gokuen.AfterSideTurnStart
        // ← Hook.AfterSideTurnStart ← CombatManager.StartTurn ⇒「不进入下个回合」）。
        // 所以非卡牌档**不调 FromCard**：源卡留空即可（Attacker 本来就靠反射设，不要求先调 FromCard，
        // 见 CSharpCodeGen 里「伙伴攻击」那段注释）。
        string fromCard = string.Equals(ctx.CardSelfExpr, "null", StringComparison.Ordinal)
            ? ""
            : $".FromCard({ctx.CardSelfExpr}, {ctx.CardPlayExpr})";
        // V0.2.306（用户要求「让回能和获得金币的对象能指定队友」）：这两档作用在**玩家**身上，
        // 按这个【效果】节点的「作用对象」分三路 —— 自己 / 指定队友（打出时选中的那个队友）/
        // 全部友方（同一边的**玩家本体**逐个来；召唤物 / 怪物的 Player 是 null，必须跳过，不然空引用）。
        // <paramref name="call"/> 里用 {0} 占位「那个玩家的表达式」；「自己」那一档生成文本与旧版逐字节相同。
        string PlayerPass(string call)
        {
            var pe = EffectFromNode(node);
            string With(string player) => string.Format(call, player);
            switch (pe.TargetSide)
            {
                case "Ally" when !string.Equals(ctx.CardPlayExpr, "null", StringComparison.Ordinal):
                    // 和 CSharpCodeGen.EmitPerPlayer 同一套守卫：不是自己、和我同一边、真的是个玩家
                    return $"if (({ctx.CardPlayExpr})?.Target is {{ }} __fgAllyUnit && __fgAllyUnit != __fgSelf"
                        + " && __fgAllyUnit.Side == __fgSelf.Side && __fgAllyUnit.Player is { } __fgAllyP)"
                        + $" {{ {With("__fgAllyP")} }}";
                case "AllAllies":
                    return $"if (__fgSelf.CombatState is not null) {{ foreach (Creature __fgAllyUnit in __fgSelf.CombatState.Allies.Where({CSharpCodeGen.AllyLoopWhere(pe, "__fgSelf")} && c.IsPlayer).ToList())"
                        + $" {{ if (__fgAllyUnit.Player is {{ }} __fgAllyP) {{ {With("__fgAllyP")} }} }} }}";
                default:
                    return With("__fgPlayer");
            }
        }
        using (DebugLogGen.Begin(p, ctx.Scope switch { "Relic" => "锻造遗物图", "Potion" => "锻造药水图", ForgeTimings.PowerScope => "锻造状态图", _ => "锻造卡牌图" },
                   $"{DebugLogMainName(ctx)}【效果】{def.Display}(fg:{node.Id})", DebugLogMainClass(ctx), "__fgSelf"))
        body = kind switch
        {
            // ---------- 单体 / 群体的基础效果 ----------
            // 【造成伤害】加【全体敌人】：走群体版本的一次性命令（TargetingAllOpponents），
            // 「命中次数 N」= 全体各挨 N 下。**不能**写成 foreach + WithHitCount(N)
            //（那是「每个敌人各打 N 下」，用户实测报过）。
            // 【随机敌人】仍然逐个动手：随机挑一个 → 挑中的那个打 N 下（那是「随机 N 下」的本体语义）。
            // 【随机敌人】+ 造成伤害（V0.2.222）：走本体的 <c>TargetingRandomOpponents</c> ——
            // 那个 API 是「**每一下**随机挑一个敌人」（AttackCommand.TargetingRandomOpponents +
            // WithHitCount 的循环里 `singleTarget = combatTargets.NextItem(validTargets)`），
            // 正是本体「随机 N 段」的语义。以前这里退化成「对全体敌人 foreach + WithHitCount(N)」，
            // 变成「每个敌人各挨 N 下」（用户报的「对群数和命中次数写在一起、随机多段异常」）。
            // ⚠ 它要求 Attacker 已经设好（本体在 builder 里就抛 InvalidOperationException），
            //   所以只有**卡牌档**（有 .FromCard）走这一支；遗物 / 药水 / 状态档照旧按
            //   「随机挑 N 个、每个打 N 下」的循环（那边没有 Attacker，不能调这个 API）。
            "Damage" when unit.RandomEnemy && fromCard.Length > 0 =>
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node),
                    "ForgeFormulaRuntime.RandomEnemy(__fgPlayer, allowDuplicates: " + (unit.Dup ? "true" : "false") + ")", amount)
                + fatalPre
                + (hitsMulti
                    ? $"{dmgAsg}await DamageCmd.Attack({amount}){fromCard}.TargetingRandomOpponents({combatState}, allowDuplicates: {(unit.Dup ? "true" : "false")}).WithHitCount({hitsExpr}).Execute({ctx.ChoiceVar});{dmgTail}"
                    : $"{dmgAsg}await DamageCmd.Attack({amount}){fromCard}.TargetingRandomOpponents({combatState}, allowDuplicates: {(unit.Dup ? "true" : "false")}).Execute({ctx.ChoiceVar});{dmgTail}"),
            "Damage" when !unit.PerFoe =>
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node), $"{foes}.FirstOrDefault()", amount)
                + fatalPre
                + (hitsMulti
                    ? $"{dmgAsg}await DamageCmd.Attack({amount}){fromCard}.TargetingAllOpponents({combatState}).WithHitCount({hitsExpr}).Execute({ctx.ChoiceVar});{dmgTail}"
                    : $"{dmgAsg}await DamageCmd.Attack({amount}){fromCard}.TargetingAllOpponents({combatState}).Execute({ctx.ChoiceVar});{dmgTail}"),
            "Damage" => FoeLoop(foes,
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true) + fatalPre,
                hitsMulti
                    ? $"{dmgAsg}await DamageCmd.Attack({amount}){fromCard}.Targeting(__fgFoe).WithHitCount({hitsExpr}).Execute({ctx.ChoiceVar});{dmgTail}"
                    : $"{dmgAsg}await DamageCmd.Attack({amount}){fromCard}.Targeting(__fgFoe).Execute({ctx.ChoiceVar});{dmgTail}",
                alwaysBraces: true),
            // ⚠ V0.2.174（用户实测报的「格挡怎么不能失去」）：**负数 = 失去格挡**这一档以前在节点图上漏了。
            // 卡牌页那条链一直是对的（CSharpCodeGen 的 case "Block"：负数走 `CreatureCmd.LoseBlock`），
            // 但节点图这里直接写 `CreatureCmd.GainBlock(__fgFoe, (‑5m), …)` —— 而本体 `GainBlock` 对
            // **非正数什么都不做**（直接返回），所以图上填负数额挡「一点都没少」，而且没有任何报错。
            // 判据和卡牌页同一套：**数值是常数且 < 0** 才当「失去格挡」（运行时公式算出来的正负生成期不知道，
            // 照旧按 GainBlock 生成 —— 和卡牌页 `IsNegativeAmount` 对 X / 公式的处理口径一致）。
            // 「失去格挡」时忽略「下回合生效」（本体没有"下回合失去格挡"的落点；卡牌页那条负数额也是立即生效）。
            "Block" when !amountN.Runtime && amountN.Value < 0m => FoeLoop(foes,
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true),
                $"await CreatureCmd.LoseBlock({ctx.ChoiceVar}, __fgFoe, {Lit(Math.Abs((double)amountN.Value))}m, __fgSelf);   // 负数 = 失去格挡（本体 GainBlock 对非正数什么都不做）",
                alwaysBraces: false),
            "Block" => FoeLoop(foes,
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true),
                nextTurn
                    ? $"await PowerCmd.Apply<BlockNextTurnPower>({ctx.ChoiceVar}, __fgFoe, {amount}, __fgSelf, {ctx.CardSelfExpr});"
                    : $"{blockAcc}await CreatureCmd.GainBlock(__fgFoe, {amount}, ValueProp.Move, {ctx.CardPlayExpr});",
                alwaysBraces: false),
            // ⚠ V0.2.191（用户问「失去金币能生效了吗」）：**负数 = 反向**这一族在节点图上原来只剩这几种没做，
            // 后果和 V0.2.174 修的「失去格挡」一模一样（本体 Gain* 对非正数**直接返回**，静默无效）；
            // 其中「额外资源量」更狠：本体 `PlayerCmd.GainStars` 对负数**直接抛 ArgumentException**。
            // 判据与上面 Block 那一档完全一致：**常数且 < 0** 才当反向（运行时公式 / X 的正负生成期不知道，
            // 照旧按「获得」生成 —— 和卡牌 / 遗物 / 药水 / 状态四条链同一口径）。
            "Heal" when !amountN.Runtime && amountN.Value < 0m => FoeLoop(foes,
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true),
                $"await CreatureCmd.Damage({ctx.ChoiceVar}, __fgFoe, {Lit(Math.Abs((double)amountN.Value))}m, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, {ctx.CardSelfExpr}, {ctx.CardPlayExpr});   // 负数 = 失去生命（本体 Heal 对非正数什么都不做）",
                alwaysBraces: false),
            "Heal" => FoeLoop(foes,
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true),
                $"await CreatureCmd.Heal(__fgFoe, {amount});",
                alwaysBraces: false),
            "HpLoss" => FoeLoop(foes,
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true),
                $"await CreatureCmd.Damage({ctx.ChoiceVar}, __fgFoe, {amount}, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, {ctx.CardSelfExpr}, {ctx.CardPlayExpr});",
                alwaysBraces: false),
            "MaxHp" when !amountN.Runtime && amountN.Value < 0m => FoeLoop(foes,
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true),
                $"await CreatureCmd.LoseMaxHp({ctx.ChoiceVar}, __fgFoe, {Lit(Math.Abs((double)amountN.Value))}m, isFromCard: true);   // 负数 = 失去最大生命",
                alwaysBraces: false),
            "MaxHp" => FoeLoop(foes,
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true),
                $"await CreatureCmd.GainMaxHp(__fgFoe, {amount});",
                alwaysBraces: false),
            "ApplyPower" => powerId.Length == 0
                ? null
                // 负数**不再自己写一套**（V0.2.139）：以前这里内联的是
                // `if (amount < 0m) { GetPower<T>() → ModifyAmount(amount) }` —— 两处都不对：
                //   ① 传进去的 amount 本身就是负的（ModifyAmount(-2) = **加** 2 层，方向反了）；
                //   ② 力量这类本体 AllowNegative 的状态被白白降级成「只扣已有层数」。
                // 现在统一走 CSharpCodeGen.ApplyPowerLine（唯一事实来源，和卡牌/遗物/药水/状态四条链同一份），
                // 正负两档与「允许负层数的 5 个状态」名单都由它判；上下文变量名传这一档自己的
                // （ctx.ChoiceVar：造不出来时是 null!，写死 choiceContext 会 CS0103）。
                : FoeLoopApplyPower(foes, ApplyPowerEffectOf(node), powerId, amount,
                    CSharpCodeGen.ApplyPowerLine(
                        ApplyPowerEffectOf(node), useX: false, powerId, "__fgFoe", amount, "__fgSelf",
                        ctx.CardSelfExpr, ctxVar: ctx.ChoiceVar)),
            // 临时增益 / 临时减益：**不能**直接施加真状态（那就是永久的了）——
            // 施加生成端那套「临时 Power」（<c>TempPowers.cs</c> 里的类：挂上时把层数加进真状态、
            // 到点再撤掉、自己消失）。类名由 TempPowerEffectOf 算，和 CollectTempPowerEffects 是同一份。
            // 数值是正还是负由那个类自己处理（负数档扣层数 / 施加负层数，见 TempPowersSource）。
            "TempPower" => powerId.Length == 0
                ? null
                : FoeLoop(foes,
                    DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true),
                    $"await PowerCmd.Apply<{CSharpCodeGen.TempPowerClassName(TempPowerEffectOf(node), p)}>({ctx.ChoiceVar}, __fgFoe, {amount}, __fgSelf, {ctx.CardSelfExpr});",
                    alwaysBraces: false),
            "DoublePower" => powerId.Length == 0
                ? null
                : $"foreach (Creature __fgFoe in {foes}) {{ PowerModel? __dbl = __fgFoe.GetPower<{powerId}>(); if (__dbl is not null) await PowerCmd.ModifyAmount({ctx.ChoiceVar}, __dbl, __dbl.Amount * (({amount}) - 1m), __fgSelf, {ctx.CardSelfExpr}); }}",
            "ClearPowerOne" => powerId.Length == 0
                ? null
                : $"foreach (Creature __fgFoe in {foes}) {{ PowerModel? __clr = __fgFoe.GetPower<{powerId}>(); if (__clr is not null) await PowerCmd.Remove(__clr); }}",
            "ClearPowerDebuffs" => $"foreach (Creature __fgFoe in {foes}) await ForgeFormulaRuntime.ClearDebuffs(__fgFoe);",
            "Stun" => $"foreach (Creature __fgFoe in {foes}) CreatureCmd.Stun(__fgFoe);",
            // V0.2.309（用户报「卡牌「监视」的即死【节点图】里还没接上」）：即死 = 本体 CreatureCmd.Kill
            //（和卡牌 / 遗物 / 药水链同一句；那几条链早就支持，只有节点图漏了这一档）。
            "InstantKill" => FoeLoop(foes,
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true),
                "await CreatureCmd.Kill(__fgFoe);", alwaysBraces: false),
            // V0.2.310：「升级本场战斗中临时增加的牌」—— 复用卡牌 / 状态那条链的同一份实现
            //（`CSharpCodeGen.EmitUpgradeGenerated`；它声明 `__upgGen`，所以包一层块作用域，
            //  同一张图上放两块也不会 CS0128）。
            "UpgradeGenerated" => ViaCodeWriterScoped(w =>
                CSharpCodeGen.EmitUpgradeGenerated(w, EffectFromNode(node), useX: false)),
            "ForbidBlock" => $"foreach (Creature __fgFoe in {foes}) await PowerCmd.Apply<NoBlockPower>({ctx.ChoiceVar}, __fgFoe, {amount}, __fgSelf, {ctx.CardSelfExpr});",
            // ---------- 对**召唤物**动手 ----------
            // 目标是召唤物，**不能**用 unit.Foes（那默认是「指定敌人」）。哪一只由【对象】模块决定：
            //   指定召唤物 → 按名称找那一只；全部召唤物 → 全都要；其余（含没接【对象】）→ 第一只。
            // 需求原文（别人报的）：「给召唤物 buff 不生效」—— 以前这一档在节点图里**什么都没生成**
            //（只有一行注释），所以现在既补上实现，也由校验器把「选了但没接的种类」拦下来。
            "PetApplyPower" => petBody,
            // ---------- 只作用于「自己 / 玩家」的 ----------
            // V0.2.220：「为抽到的牌加关键词」—— 多行（接住抽到的牌 + foreach 加关键词）包成一个块，
            // 让外面的「生效次数 / 概率」照常套上；没选关键词时走下面那一行（老文本逐字不变）。
            "Draw" when ForgeEffectBridge.Ensure(node) is { HasDrawnKeyword: true } && !nextTurn =>
                ViaCodeWriterScoped(w => CSharpCodeGen.EmitDraw(w, p, ForgeEffectBridge.Ensure(node)!, amount,
                    "__fgPlayer", "__fgSelf", ctx.CardSelfExpr)),
            "Draw" => nextTurn
                ? $"await PowerCmd.Apply<DrawCardsNextTurnPower>({ctx.ChoiceVar}, __fgSelf, {amount}, __fgSelf, {ctx.CardSelfExpr});"
                // ⚠ 本体这个 API 叫 `CardPileCmd.Draw(choiceContext, count, player)` ——
                //   以前这里写的是 `DrawCards(count, choiceContext, player)`（方法名和参数顺序都不对），
                //   生成的工程直接 CS0117（探针真编译时踩到）。和卡牌 / 遗物那条老链同一个写法（CSharpCodeGen）。
                : $"await CardPileCmd.Draw({ctx.ChoiceVar}, {amount}, __fgPlayer);",
            "Gold" when !amountN.Runtime && amountN.Value < 0m =>
                PlayerPass($"await PlayerCmd.LoseGold({Lit(Math.Abs((double)amountN.Value))}m, {{0}});   // 负数 = 失去金币（本体 GainGold 对非正数直接返回）"),
            "Gold" => PlayerPass($"await PlayerCmd.GainGold({amount}, {{0}});"),
            "Energy" when !amountN.Runtime && amountN.Value < 0m =>
                PlayerPass($"await PlayerCmd.LoseEnergy({Lit(Math.Abs((double)amountN.Value))}m, {{0}});   // 负数 = 失去能量（本体 GainEnergy 对非正数直接返回）"),
            "Energy" => PlayerPass($"await PlayerCmd.GainEnergy({amount}, {{0}});"),
            "ExtraResource" when !amountN.Runtime && amountN.Value < 0m =>
                $"await PlayerCmd.LoseStars({Lit(Math.Abs((double)amountN.Value))}m, __fgPlayer);   // 负数 = 失去额外资源量（本体 GainStars 对负数直接抛异常）",
            "ExtraResource" => $"await PlayerCmd.GainStars({amount}, __fgPlayer);",
            "EndTurn" => $"PlayerCmd.EndTurn(__fgPlayer, false);",
            // ---------- 牌堆类（做一次，不是对每个目标）----------
            // 「消耗 / 丢弃 / 升级 / 从某一摞拿牌」已经在上面内联生成了（提示语要 base.*），这里只管剩下的
            "GenerateCard" => spawnCardClass is null
                ? null
                : $"for (int __fgN = 0; __fgN < (int)({amount}); __fgN++) {{ CardModel __gen = {combatState}.CreateCard<{CSharpCodeGen.CardTypeExpr(p, spawnCardClass)}>(__fgPlayer); await CardPileCmd.AddGeneratedCardToCombat(__gen, PileType.{node.Get("SpawnTo", "Hand")}, __fgPlayer); }}",
            // ---------- V0.2.168：卡牌页 / 状态页早就支持、节点图以前没接的这几种 ----------
            // 用户截图报的「删除卡牌（全局：从牌组删牌）」就是第一组里的一条。
            // 它们全都**不需要「打出这张牌」的上下文**，只要一个玩家（+ 选牌时一个 choiceContext）——
            // 而这些发射器卡牌页 / 状态页已经有一份（见 CSharpCodeGen.EmitGlobalCardEffectPublic /
            // EmitPlaySfx / EmitPlaySoundFile / EmitOutbreak），所以这里走 ViaCodeWriter **复用同一份**，
            // 不再抄第二遍（抄的第二遍必然慢慢长歪）。
            "AddCardGlobal" or "TransformCardGlobal" or "RemoveCardGlobal" or "UpgradeCardGlobal" =>
                ViaCodeWriterScoped(w => CSharpCodeGen.EmitGlobalCardEffectPublic(w, EffectFromNode(node), "__fgPlayer",
                    EffectFromNode(node).AmountIsStack, p, ctx.ChoiceVar,
                    // V0.2.205：只有「卡牌」这张布上有「这张牌」（数值 0 = 这张牌自己用得到）
                    cardSelfExpr: ctx.Scope == "Card" ? ctx.CardSelfExpr : null)),
            // ---------- V0.2.170：要「打出这张牌」那一刻上下文的几种（用户点的第 3 条）----------
            // 「重放卡牌」：给牌堆里的牌加本场战斗的重放次数。它只需要「玩家 / 哪一摞 / 选牌上下文」，
            // 所以三种布都能生成（和卡牌页共用同一份实现，见 CSharpCodeGen.EmitReplayCardPublic）。
            "ReplayCard" => ViaCodeWriterScoped(w => CSharpCodeGen.EmitReplayCardPublic(w, EffectFromNode(node), p,
                $"(int)({amount})", "__fgPlayer", ctx.ChoiceVar, ctx.CardSelfExpr)),
            // 「回合结束时自动打出」：把选好的牌交给生成的 ForgeTurnEndPlayPower。
            // 数值 0 = 「这张牌自己」——只有卡牌档才有「自己」，别的布传 null（发射器自己会写一行说明）。
            "TurnEndPlay" => ViaCodeWriterScoped(w => CSharpCodeGen.EmitTurnEndPlayPublic(w, EffectFromNode(node), p,
                selfExpr: ctx.Scope == "Card" && !ctx.NoHookParams ? ctx.CardSelfExpr : null,
                cardSource: ctx.CardSelfExpr, playerExpr: "__fgPlayer", creatureExpr: "__fgSelf",
                choiceExpr: ctx.ChoiceVar, sourceExpr: ctx.CardSelfExpr, countExpr: $"(int)({amount})")),
            // 「将此牌放回手牌」：**inline 写**（不是钩子）——所以必须挂在「这张牌结算完毕时」那种
            // 「牌已经不在手牌里」的时机上才有效；挂在 OnPlay（打出时）上时这张牌还在手牌里，什么都不会发生
            //（本体是打完牌之后才把它放进弃牌堆：PlayCardAction）。校验器对别的时机会给一条警告说清。
            "ReturnSelfToHand" => ctx.Scope == "Card" && !ctx.NoHookParams
                ? $"if (base.Pile is not null && base.Pile.Type != PileType.Hand) await CardPileCmd.Add({ctx.CardSelfExpr}, PileType.Hand);"
                : null,
            // 本回合这张牌自己费用变 0（V0.2.203，用户截图报的「处决」选了这一块被拦下）：
            // 这是**这张牌自己的行为**，只有「卡牌」那张布上有「这张牌」（ctx.Scope == Card）；
            // 生成的语句和卡牌页那条链**完全一样**：本体 CardModel.SetToFreeThisTurn()
            // = EnergyCost.SetThisTurnOrUntilPlayed(0) + SetStarCostThisTurn(0)（能量费 + 额外资源量费一起变 0）。
            // 状态 / 遗物 / 药水的布上没有「这张牌」→ 返回 null（下面写一行说明），校验器也会报错拦住。
            "CheapenSelf" => ctx.Scope == "Card"
                ? "this.SetToFreeThisTurn();   // 本回合这张牌自己费用变 0（本体 CardModel.SetToFreeThisTurn：能量费 + 额外资源量费一起）"
                : null,
            "PlaySfx" => ViaCodeWriter(w => CSharpCodeGen.EmitPlaySfx(w, EffectFromNode(node))),
            // V0.2.231：「会被下一个自定义音频顶替」在锻造图上同样生效（把 p 传下去才知道要不要走小助手）
            "PlaySoundFile" => ViaCodeWriter(w => CSharpCodeGen.EmitPlaySoundFile(w, EffectFromNode(node), p)),
            "Outbreak" => ViaCodeWriter(w => CSharpCodeGen.EmitOutbreak(w, EffectFromNode(node), amount,
                enemies: $"{combatState}.HittableEnemies", applier: "__fgSelf", cardSource: ctx.CardSelfExpr)),
            // 「强化指定卡牌」（V0.2.168，用户问「强化指定卡牌怎么不能做」）：给**这张牌的主人**挂那张强化 Power，
            // 和状态页那份写法逐字一致（见 CustomPowerGen 的 case "BoostCard"），只是「自己」= __fgSelf。
            // ⚠ 那张 Power 类由 CSharpCodeGen.BoostPowersSource 生成 —— 它的「用到才生成」判断
            //（CollectBoostEffects）也一起补上了节点图的效果，不然会 CS0246。
            "BoostCard" => ViaCodeWriter(w =>
            {
                w.Line(CSharpCodeGen.BoostMarker(EffectFromNode(node)));
                w.Line($"await PowerCmd.Apply<{Naming.AmbientBoostPowerClass(EffectFromNode(node))}>({ctx.ChoiceVar}, __fgSelf, {amount}, __fgSelf, {ctx.CardSelfExpr});");
            }),

            // ========== V0.2.176：把「卡牌页支持、节点图以前没接」的那批补上 ==========
            // 共同口径：能用图上的上下文（__fgPlayer / __fgSelf / __fgTarget / foes / ctx.ChoiceVar）表达的，
            // 就**复用卡牌页那份发射器**（把上下文表达式当参数传进去）；只有卡牌页那份写死了 `cardPlay` /
            // `base.DynamicVars` 的少数几条，才在图上内联一份等价写法。
            //
            // 「放置卡牌」：数值 0 = 这张牌自己（只有卡牌档有「自己」）→ 和 TurnEndPlay 同款，
            // 非卡牌档传 null（发射器自己会写一行说明，不静默）。
            "PlaceCard" => ViaCodeWriterScoped(w => CSharpCodeGen.EmitPlaceCardPublic(w, EffectFromNode(node), p,
                useX: false, countExpr: $"(int)({amount})",
                selfExpr: ctx.Scope == "Card" && !ctx.NoHookParams ? ctx.CardSelfExpr : null,
                playerExpr: "__fgPlayer", cardSource: ctx.CardSelfExpr)),

            // 「给予卡牌关键词」：数值 0 = 这张牌自己（同上）；「取几张 / 从哪里选牌 / 自己选还是随机」
            // 全在卡牌页那份实现里，直接复用。⚠ 它自己选那一档会读提示语（见 NeedsSelectPrompt）。
            // ⚠ 用 ViaCodeWriter（不是 Scoped）：这个发射器会自己 Warn（把说明写成注释）——
            //   包在 `{ … }` 里的话那个 `}` 会被注释吞掉 → CS1513（真编译实测抓到过 6 处）。
            "GiveKeyword" => ViaCodeWriter(w => CSharpCodeGen.EmitGiveKeyword(w, p, EffectFromNode(node),
                useX: false, playerExpr: "__fgPlayer", creatureExpr: "__fgSelf",
                sourceExpr: ctx.CardSelfExpr, selfAllowed: ctx.Scope == "Card" && !ctx.NoHookParams,
                promptExpr: "base.SelectionScreenPrompt")),

            // 「刮削」：抽 N 张 → 对其中符合条件的牌执行选定的操作。只需要「玩家 + 选牌上下文」。
            "ScrapeDraw" => ViaCodeWriter(w => CSharpCodeGen.EmitScrapeDrawPublic(w, p, EffectFromNode(node),
                $"(int)({amount})", playerExpr: "__fgPlayer", choiceExpr: ctx.ChoiceVar)),

            // 「用目标卡填满手牌」（本体迫降）：只需要「玩家 + 战斗状态」。
            "FillHand" => ViaCodeWriter(w => CSharpCodeGen.EmitFillHandPublic(w, EffectFromNode(node),
                playerExpr: "__fgPlayer", combatStateExpr: combatState)),

            // 「禁止目标回复生命」：给每个目标挂本模组生成的 NoHealPower（层数 = 回合数）。
            // ⚠ 用 ViaCodeWriter（不是 Scoped）：这些发射器的行尾都带 `//` 注释，
            //   包进 `{ … }` 里那个 `}` 会被注释吞掉 → CS1513（真编译实测抓到 5 处）。
            "ForbidHeal" => ViaCodeWriter(w => w.Line(FoeLoop(foes,
                DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true),
                $"await PowerCmd.Apply<{Naming.From(p).NoHealPowerClass}>({ctx.ChoiceVar}, __fgFoe, System.Math.Abs({amount}), __fgSelf, {ctx.CardSelfExpr});   // 禁止生命回复",
                alwaysBraces: false))),

            // 「暴露」（本体 Expose）：清空格挡 + 移除人工制品 + 再施加 N 层状态（状态在「增益 / 减益」栏选）。
            "Expose" => ViaCodeWriter(w => w.Line(powerId.Length == 0
                ? $"// ⚠【效果】暴露：没选「增益 / 减益」—— 这一条什么都不做（到上面那栏选一个状态）"
                : FoeLoop(foes,
                    DebugLogGen.TargetInline(ApplyPowerEffectOf(node), "__fgFoe", amount, inLoop: true),
                    $"{{ Creature __fgExp = __fgFoe; await CreatureCmd.LoseBlock({ctx.ChoiceVar}, __fgExp, __fgExp.Block, __fgSelf);"
                    + $" if (__fgExp.HasPower<ArtifactPower>()) await PowerCmd.Remove<ArtifactPower>(__fgExp);"
                    + $" await PowerCmd.Apply<{powerId}>({ctx.ChoiceVar}, __fgExp, {amount}, __fgSelf, {ctx.CardSelfExpr}); }}   // 暴露：清格挡 + 去人工制品 + 施加状态",
                    alwaysBraces: false))),

            // ---- V0.2.176 第二批：消耗「本场战斗级」辅助类的几种 ----
            // 「额外获得一回合」：施加本模组生成的 ExtraTurnPower（和卡牌页同一个类）。
            "ExtraTurn" => ViaCodeWriterScoped(w =>
                w.Line($"await PowerCmd.Apply<{Naming.From(p).ExtraTurnPowerClass}>({ctx.ChoiceVar}, __fgSelf, 1m, __fgSelf, {ctx.CardSelfExpr});")),

            // 「抽到就减费」：给自己挂本模组生成的 DrawCostPower（层数 = 持续回合数）。
            // ⚠ 用 ViaCodeWriter（行尾带 `//` 注释，包 `{ }` 会 CS1513 —— 见上面 ForbidHeal 那段说明）。
            "CheapenDraw" => ViaCodeWriter(w =>
            {
                w.Line(CSharpCodeGen.SelfContainedMarker(EffectFromNode(node)));
                w.Line($"await PowerCmd.Apply<{Naming.From(p).DrawCostPowerClass}>({ctx.ChoiceVar}, __fgSelf, System.Math.Abs({amount}), __fgSelf, {ctx.CardSelfExpr});");   // 抽到就减费（层数 = 持续回合数）
            }),

            // 「减少指定卡牌类别的费用」（V0.2.250）：图上【效果】的「范围」栏就是「哪一类牌」，复用卡牌页那份发射器。
            // ⚠ 用 ViaCodeWriter（行尾带 `//` 注释，包 `{ }` 会 CS1513 —— 见上面 ForbidHeal 那段说明）。
            "CostDown" => ViaCodeWriter(w => CSharpCodeGen.EmitCostDownPublic(w, p, EffectFromNode(node),
                playerExpr: "__fgPlayer", sourceExpr: ctx.CardSelfExpr, choiceExpr: ctx.ChoiceVar)),

            // 「增加额外资源量上限值」：给「上限」那个隐藏状态加层数（上限 = 基础上限 + 层数）。
            "RaiseResourceCap" => ViaCodeWriter(w =>
                w.Line($"await PowerCmd.Apply<{CSharpCodeGen.StarCapPowerClass(p)}>({ctx.ChoiceVar}, __fgSelf, {amount}, __fgSelf, {ctx.CardSelfExpr});")),

            // 「透支能量（下回合少）」：施加本模组生成的负债 Power（它施加时立刻给能量、下回合扣回）。
            "OverdraftEnergy" => ViaCodeWriter(w =>
                w.Line($"await PowerCmd.Apply<{Naming.From(p).EnergyDebtPowerClass}>({ctx.ChoiceVar}, __fgSelf, {amount}, __fgSelf, {ctx.CardSelfExpr});")),

            // ========== V0.2.176 第三批：伙伴组（用户点的最高优先级）==========
            // 卡牌页那批宠物发射器已经全部参数化（V0.2.176），这里只负责：
            //   ① 图上查「这只伙伴」的局部变量（照抄延迟状态那条链的写法）；
            //   ② 包一层 `if (变量 is not null)`（宠物不在场 → 整条静默跳过，本体也是这个语义）；
            //   ③ 把图上的上下文表达式（__fgPlayer / __fgSelf / ctx.ChoiceVar / ctx.CardPlayExpr）传进去。
            "SummonPet" => ViaCodeWriter(w =>
            {
                var sumSpec = EffectFromNode(node);
                if (PetGen.IsAll(sumSpec.PetSummon))
                {
                    var sumDefs = PetGen.ResolveMany(p, sumSpec.PetSummon);
                    if (sumDefs.Count == 0) { CSharpCodeGen.EmitSummonPetPublic(w, sumSpec, p, amount, "__fgPlayer"); return; }
                    w.Line($"// 【全部召唤物】这一条对 {sumDefs.Count} 只各来一遍：" + string.Join("、", sumDefs.Select(d => d.DisplayName)));
                    foreach (var d in sumDefs)
                        CSharpCodeGen.EmitSummonPetPublic(w, sumSpec, p, amount, "__fgPlayer");
                    return;
                }
                CSharpCodeGen.EmitSummonPetPublic(w, sumSpec, p, amount, "__fgPlayer");
            }),
            "PetAttack" or "PetDamageByMaxHp" or "PetDamageByCurHp" or "PetDamageByMissingHp"
                or "PetHeal" or "PetLoseHp" or "PetGainMaxHp" or "PetSacrifice"
                or "PetApplyPower" or "PetGuardOn" or "PetGuardOff" =>
                ViaCodeWriter(w => EmitGraphPetEffect(w, p, node, kind, amount, combatState, ctx)),

            // 「大限已至」：伤害 = 目标身上的灾厄层数 × 这条效果填的数值（本体 Time's Up 的语义）。
            // 图上没有卡牌的 CalculatedDamage 计算变量 → 内联算（同口径，只是不走卡面变量）。
            // 只能打在「单个指定敌人」上（本体也是 AnyEnemy）；没有目标就跳过（不抛异常把链打断）。
            "TimesUp" => ViaCodeWriter(w =>
            {
                // `single` = 这条链上「指向对象」解析出来的单个目标（卡牌档就是打出时选的那个敌人）
                w.Open($"if ({single} is not null)");
                CSharpCodeGen.EmitTimesUp(w, EffectFromNode(node),
                    targetExpr: single,
                    inlineDamageExpr: $"((decimal){single}.GetPowerAmount<DoomPower>() * {amount})",
                    cardExpr: ctx.CardSelfExpr, cardPlayExpr: ctx.CardPlayExpr, choiceExpr: ctx.ChoiceVar);
                w.Close();
            }),

            // ---- V0.2.176 第四批：先古转化「转化为先古卡」----
            // 按效果里的「配对表」把牌组里的牌换成先古卡，各不相同；
            // 变出来的牌收进预览列表，方法末尾由 EmitHook 统一弹本体那个预览界面
            //（列表的声明也在 EmitHook 里，用同一个常量名 AncientPreviewVar）。
            "AncientTranscend" => ViaCodeWriter(w => CSharpCodeGen.EmitAncientTranscend(
                w, p, EffectFromNode(node), 0, playerExpr: "__fgPlayer")),

            _ => null,
        };

        // 上面的 switch 里被内联成循环的那几种，只有在「是单个目标」时才需要外层守卫；
        // 现在一律走 foreach（列表表达式对单体也会返回一个只含它的列表），所以不用再套守卫。
        if (body is null)
        {
            // 两种原因分开写：**这个种类没接**（选了等于白选）vs **必填的东西没填**（状态 / 目标卡）。
            // 以前一律写「还没接上这个种类」—— 用户填漏了字段时会被这句话带偏。
            // ⚠ V0.2.203：先看「卡牌专属」——「本回合这张牌自己费用变 0」现在**进了支持名单**（卡牌图能生成），
            // 但状态 / 遗物 / 药水的布上没有「这张牌」→ 这里必须还是写「卡牌自己的行为」那句，
            // 不能掉进上面那条「缺必填的东西」（那句话是骗人的）。
            sb.AppendLine(CardOnlyEffectKinds.Contains(kind)
                ? $"{indent}// ⚠【效果】{def.Display}：这一档是**卡牌自己的行为**（挂在这张牌自己身上），"
                  + "这块布（状态 / 遗物 / 药水）没有「牌自己」可以挂 —— 请改用「卡牌」页的「效果」列表。"
                : SupportedEffectKinds.Contains(kind)
                    ? $"{indent}// ⚠【效果】{def.Display}：这条效果**缺必填的东西**（状态 / 目标卡…），生成时被跳过了 —— 见「配置检查」里那条错误。"
                    : $"{indent}// ⚠【效果】{def.Display}：节点图这一阶段还没接上这个种类（下一阶段补上）—— 请改用「卡牌」页的「效果」列表，那边支持全部种类。");
            return;
        }

        EmitRepeatLoop(sb, indent, timesExpr, __ind =>
        {
            if (chanceRoll)
            {
                sb.AppendLine(__ind + $"if (ForgeFormulaRuntime.Roll(__fgPlayer, {chanceExpr}))");
                sb.AppendLine(__ind + "{");
                sb.AppendLine(__ind + "    " + body);
                sb.AppendLine(__ind + "}");
            }
            else
            {
                sb.AppendLine(__ind + body);
            }
        });
    }

    /// <summary>
    /// 「生成调试日志」（V0.2.142）：锻造图里的「施加状态」——套上「执行前 / 执行后层数」两行
    /// （和另外五条链同一份写法，见 <see cref="DebugLogGen.PowerBefore"/>）。
    /// 关着时返回 <paramref name="core"/> 原文，一个字符都不变（红线①）。
    /// </summary>
    private static string DebugLogApplyPower(EffectSpec e, string targetExpr, string powerType, string amount, string core)
    {
        if (!DebugLogGen.On) return core;
        return DebugLogGen.PowerBefore(e, targetExpr, powerType, amount)
            + core
            + DebugLogGen.PowerAfter(e, targetExpr, powerType);
    }

    /// <summary>
    /// V0.2.176：节点图上的**伙伴效果**（伙伴攻击 ×4 / 治疗 / 失去生命 / 最大生命 / 牺牲 /
    /// 给伙伴施加状态 / 替主人承伤开关）。
    ///
    /// 形状（和卡牌页一致，只是变量与上下文换成图上的写法）：
    /// <code>
    /// Creature? __zouPet1 = __fgPlayer.PlayerCombatState?.Pets.FirstOrDefault(c =&gt; c.Monster is ZouPet1);
    /// if (__zouPet1 is not null)
    /// {
    ///     … 把卡牌页那份发射器生成的代码原样放进来 …
    /// }
    /// </code>
    ///
    /// 为什么变量名按宠物类名取（<c>__zouPet1</c>）：同一张图上两条「伙伴攻击」引用**不同**的伙伴时，
    /// 用固定的 <c>__pet</c> 会撞名 → CS0128；按类名取名天然互不干扰（和卡牌页 <c>PetAttackVarName</c> 同一规矩）。
    ///
    /// 「伙伴不在场」时整条**静默跳过**：本体自己的宠物效果就是这个语义（卡牌页也包同一层判空）。
    /// </summary>
    private static void EmitGraphPetEffect(CodeWriter w, CharacterProfile p, ForgeNodeSpec node, string kind,
        string amount, string combatState, EmitCtx ctx)
    {
        var spec = EffectFromNode(node);
        var def = PetGen.Resolve(p, spec.PetSummon);
        if (def is null)
        {
            w.Line($"// ⚠【效果】{EffectCatalog.FindKind(kind).Display}：没有可用的召唤物"
                + (string.IsNullOrWhiteSpace(spec.PetSummon) ? "（还没选召唤物）" : $"（找不到召唤物「{spec.PetSummon!.Trim()}」）")
                + "：到「召唤物」页添加一只并勾上「启用」");
            return;
        }

        string petVar = CSharpCodeGen.PetAttackVarName(def.ClassName) + UniqueVar(node, "");
        // 图上的「选中的目标」= 这张牌 / 这条链打出时的目标（卡牌档就是玩家选的那个敌人）；
        // 没有它（遗物 / 药水 / 状态档）时 TargetOrFirst 会退回「可打的第一个敌人」，不会 NRE。
        string targetExpr = $"ForgeFormulaRuntime.TargetOrFirst({ctx.CardPlayExpr}?.Target, __fgPlayer).FirstOrDefault()";

        // ⚠ `w.Open` 自己会在行尾补 ` {` 并把缩进 +1 —— 这里**不要**再写一个 `{`（写了会多出一个空块）。
        w.Open($"Creature? {petVar} = __fgPlayer.PlayerCombatState?.Pets.FirstOrDefault(c => c.Monster is {def.ClassName});");
        // ⚠ 一定要用 `if (… is not null) { … }` 包起来，**不能**写 `if (… is null) return;` ——
        // 这个发射器的输出是**内联**在打出方法体里的，那个 return 会把这条链**后面的效果整段吞掉**
        //（卡牌页是在 OnPlay 的循环里包 if，那边没有这个问题）。
        w.Open($"if ({petVar} is not null)");
        switch (kind)
        {
            case "PetAttack":
                CSharpCodeGen.EmitPetAttack(w, spec, p, petVar, amount, useX: false, def,
                    combatState, ctx.ChoiceVar, ctx.CardSelfExpr, ctx.CardPlayExpr);
                break;
            case "PetDamageByMaxHp":
            case "PetDamageByCurHp":
            case "PetDamageByMissingHp":
                // 图上没有卡牌的 CalculatedDamage 计算变量 → 强制走「按这只自己的生命值算」那一支
                CSharpCodeGen.EmitPetCalculatedAttack(w, spec, p, petVar, useX: false, def,
                    forceInlineDamage: true, combatState, ctx.ChoiceVar, ctx.CardSelfExpr, ctx.CardPlayExpr);
                break;
            case "PetHeal":
                CSharpCodeGen.EmitPetHeal(w, spec, petVar, amount, amount);
                break;
            case "PetLoseHp":
                CSharpCodeGen.EmitPetLoseHp(w, spec, petVar, amount, amount, "__fgSelf", ctx.ChoiceVar, ctx.CardSelfExpr, ctx.CardPlayExpr);
                break;
            case "PetGainMaxHp":
                CSharpCodeGen.EmitPetGainMaxHp(w, spec, petVar, amount, amount);
                break;
            case "PetSacrifice":
                CSharpCodeGen.EmitPetSacrificePublic(w, spec, p, petVar, petDef: def,
                    ownerCreatureExpr: "__fgSelf", fixedGainExpr: amount, choiceExpr: ctx.ChoiceVar,
                    cardExpr: ctx.CardSelfExpr, cardPlayExpr: ctx.CardPlayExpr, targetExpr: targetExpr, inlineCalc: true);
                break;
            case "PetApplyPower":
                CSharpCodeGen.EmitPetApplyPower(w, p, spec, petVar, amount, null, amount,
                    "__fgSelf", ctx.ChoiceVar, ctx.CardSelfExpr);
                break;
            case "PetGuardOn":
                CSharpCodeGen.EmitPetGuard(w, p, petVar, on: true, ctx.ChoiceVar);
                break;
            case "PetGuardOff":
                CSharpCodeGen.EmitPetGuard(w, p, petVar, on: false, ctx.ChoiceVar);
                break;
        }
        w.Close();   // if (pet is not null)
        w.Close();   // 宠物变量声明那个块
    }

    /// <summary>
    /// 「逐个敌人动手」那几条效果的外壳（V0.2.167 修 CS0103）。
    ///
    /// <para>为什么要它：这几档的调试日志（<see cref="DebugLogGen.TargetInline"/> 传 <c>inLoop: true</c>）
    /// 会把**目标表达式 `__fgFoe` 拼进日志原文**，而日志行原来拼在
    /// <c>foreach (Creature __fgFoe in …)</c> 的**前面** —— 那是在声明它的作用域之外引用循环变量，
    /// 编译器直接 <c>CS0103 当前上下文中不存在名称"__fgFoe"</c>（用户实测报过：<c>Fulan.cs</c> 里 4 处，
    /// 前提是存档开着「生成调试日志」）。现在统一把日志写进循环体：日志关着时
    /// <paramref name="dbgPrefix"/> 是空串、输出与以前**逐字节相同**（红线①）。</para>
    /// </summary>
    /// <param name="alwaysBraces">原来就带花括号的那一档（【造成伤害】）传 true，保证关日志时文本不变。</param>
    private static string FoeLoop(string foes, string dbgPrefix, string body, bool alwaysBraces)
    {
        string head = $"foreach (Creature __fgFoe in {foes})";
        if (dbgPrefix.Length == 0) return alwaysBraces ? $"{head} {{ {body} }}" : $"{head} {body}";
        return $"{head} {{ {dbgPrefix}{body} }}";
    }

    /// <summary>
    /// 同上，但这一档的日志是「执行前 / 执行后各一行」（<see cref="DebugLogApplyPower"/>）——
    /// 那两行也引用 `__fgFoe`，所以整段要一起放进循环体（日志关着时输出与以前逐字节相同）。
    /// </summary>
    private static string FoeLoopApplyPower(string foes, EffectSpec e, string powerType, string amount, string core)
    {
        string head = $"foreach (Creature __fgFoe in {foes})";
        if (!DebugLogGen.On) return $"{head} {core}";
        return $"{head} {{ {DebugLogGen.PowerBefore(e, "__fgFoe", powerType, amount)}{core}{DebugLogGen.PowerAfter(e, "__fgFoe", powerType)} }}";
    }

    /// <summary>
    /// 日志里「这段锻造图挂在什么上」——节点图生成时拿不到 CardSpec / RelicSpec / PotionSpec 本身
    /// （<see cref="EmitCtx"/> 里只有上下文表达式），所以只写「范围 + 节点号」（节点号在后面拼）：
    /// 排查时够定位到是哪张图的哪一块布。
    /// </summary>
    private static string DebugLogMainName(EmitCtx ctx) => ctx.Scope switch
    {
        "Relic" => "遗物（锻造图）",
        "Potion" => "药水（锻造图）",
        // V0.2.194：状态那一类布（锻造 →「状态列表」）
        ForgeTimings.PowerScope => "状态（锻造图）",
        _ => "卡牌（锻造图）",
    };

    /// <summary>同上：类名也拿不到，写范围名（真正定位靠日志里的【效果】节点号）。</summary>
    private static string DebugLogMainClass(EmitCtx ctx) => ctx.Scope switch
    {
        "Relic" => "RelicForgeGraph",
        "Potion" => "PotionForgeGraph",
        ForgeTimings.PowerScope => "PowerForgeGraph",
        _ => "CardForgeGraph",
    };

    /// <summary>
    /// 「这几条代码重复 N 遍」——生效次数那一栏的两种档位：
    ///   · <paramref name="bound"/> 是**数字**（纯数字 / 生成期折叠成常数的公式）→ 照老写法**在生成期展开**
    ///     （生成出来的文本和 V0.2.127 逐字相同，红线）；
    ///   · <paramref name="bound"/> 是**运行时变量名**（那一栏写了公式）→ 生成一个真的
    ///     <c>for (int t = 0; t &lt; 那个变量; t++)</c> 循环把它包起来。
    /// </summary>
    private static void EmitRepeatLoop(StringBuilder sb, string indent, string bound, Action<string> body)
    {
        if (int.TryParse(bound, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
        {
            for (int t = 0; t < n; t++) body(indent);
            return;
        }
        sb.AppendLine(indent + $"for (int t = 0; t < {bound}; t++)");
        sb.AppendLine(indent + "{");
        body(indent + "    ");
        sb.AppendLine(indent + "}");
    }

    private static string Lit(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Lit(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 一个**每个模块唯一**的局部变量名。
    /// 为什么必须唯一：同一条执行链上可以有两个「消耗卡牌」/「复制卡牌」，各写一次
    /// <c>var __fgSel = …</c> 会在同一个方法里重复声明同名局部变量 → 生成出来的工程编译不过（CS0128）。
    /// 用模块内部 Id 的哈希做后缀：同一个模块每次生成的名字都一样（可重复生成、逐字稳定）。
    /// </summary>
    private static string UniqueVar(ForgeNodeSpec node, string prefix) =>
        prefix + ForgeNodeSpec.StableSuffix(node.Id);

    /// <summary>
    /// V0.2.167：节点图里【效果】那条效果自己的 <see cref="EffectSpec"/>（和卡牌页共用同一份编辑器）。
    /// 生成端以前只读老字段（`node.Get(...)`），而「生成 / 变化出来的卡」的附加处理（升级 / 免费 /
    /// 仅本回合免费 / 仅本回合升级）、「加关键词」、「复制的份数」**没有老字段**
    /// （见 <see cref="ForgeEffectBridge.MirrorToFields"/> 只回写一部分）—— 所以以前只能报错拦住。
    /// 现在这几项直接读 spec（<see cref="ForgeEffectBridge.Ensure"/> 会把老模块按老字段迁出来，
    /// 所以老图照样有 spec）。
    /// </summary>
    private static EffectSpec? GraphSpec(ForgeNodeSpec node) => ForgeEffectBridge.Ensure(node);

    /// <summary>
    /// 把【效果】模块读成一份 <see cref="EffectSpec"/>（V0.2.168）。
    ///
    /// 为什么要有它：新接的那几种效果（改牌组的四种全局效果 / 声音 / 毒性爆发 / 强化卡牌）复用的是
    /// 卡牌页 / 状态页**同一份**发射器，而那些发射器收的是 <see cref="EffectSpec"/> ——
    /// <see cref="GraphSpec"/> 正常都会给出一份（<c>Ensure</c> 会把老模块迁出来），拿不到时兜一个空壳。
    /// </summary>
    private static EffectSpec EffectFromNode(ForgeNodeSpec node) =>
        GraphSpec(node) ?? new EffectSpec { Kind = node.Get("Effect", "Damage") };

    /// <summary>
    /// 用 <see cref="CodeWriter"/> 写一段、拿回文本（V0.2.168）。
    /// 复用的那几份发射器（改牌组 / 声音 / 毒性爆发）写的都是 CodeWriter，而节点图这边拼的是
    /// <see cref="StringBuilder"/> —— 这个桥让两边共用同一份实现，不用抄第二遍。
    /// </summary>
    private static string ViaCodeWriter(Action<CodeWriter> emit)
    {
        var w = new CodeWriter();
        emit(w);
        return w.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>
    /// 同上，但**逐行**写进 <paramref name="sb"/> 并把缩进对齐到 <paramref name="indent"/>（V0.2.170）。
    /// 上面那个 <see cref="ViaCodeWriter"/> 是当「一行 switch 结果」用的（多行时只有第一行有缩进，能编译但难看）；
    /// 「取卡方式 / 下回合生效」这几档是**提前 return** 的多行代码，所以单开这一个把每行都缩进对齐。
    /// </summary>
    private static void ViaCodeWriterAppend(StringBuilder sb, string indent, Action<CodeWriter> emit)
    {
        // ⚠ 整段包一层 `{ }`（V0.2.170）：卡牌页那几份发射器用的是**固定的**局部变量名
        //（`__transformPool` / `__genPool` / `__replayPool` / `__tePicks`…），同一条链上放两个同类效果
        //（例：一个「变化卡牌」+ 一个「变化卡牌（全局）」）会在同一个方法里重复声明 → CS0128（探针实测）。
        // 包一层块作用域，各效果自己的局部变量互不干扰，每个效果仍是同一份实现、文本只多两行花括号。
        sb.AppendLine(indent + "{");
        foreach (string line in ViaCodeWriter(emit).Replace("\r\n", "\n").Split('\n'))
            sb.AppendLine(line.Length == 0 ? "" : indent + "    " + line);
        sb.AppendLine(indent + "}");
    }

    /// <summary>
    /// 同上，但拿回**一段包在块里的文本**（给「一行 switch 结果」那种用；见 <see cref="ViaCodeWriterAppend"/> 里
    /// 为什么必须包块）。
    /// </summary>
    private static string ViaCodeWriterScoped(Action<CodeWriter> emit) => "{ " + ViaCodeWriter(emit) + " }";

    /// <summary>
    /// 这张图里有没有用「斩杀」条件（【条件】模块选「斩杀」）—— V0.2.170（用户点的第 3 条）。
    /// 用到了就要在序幕里声明 <c>__lastAttack</c> / <c>__fatalOk</c>、并在「造成伤害」那一条上赋值，
    /// 否则生成出来的条件判据引用两个不存在的名字（CS0103，整个模组编译不过）。
    /// </summary>
    public static bool GraphUsesFatal(ForgeGraphSpec? g) =>
        g?.Nodes is not null && g.Nodes.Any(n => n is not null && n.Kind == "Condition"
            && string.Equals(n.Get("Cond", "None"), "Fatal", StringComparison.Ordinal));

    /// <summary>
    /// 这张图里有没有用某条「这一次打出」的卡牌专属读数（<c>ThisCardDamage</c> / <c>ThisCardBlock</c>，V0.2.194）——
    /// 图上这些读数写在【数值】模块的公式里（再接到【效果】/【关系符】上）。
    ///
    /// <para>用到就要在对应的效果上把结果记进卡片那个属性
    /// （见 <see cref="EmitEffect"/> 的 thisCardCap / thisBlockCap），否则读到的恒是 0；
    /// 属性声明与打出时的清零在 <c>CSharpCodeGen.CardSourceCore</c> 里统一做
    /// （那边判据也走这里 —— 两块布都算）。</para>
    /// </summary>
    public static bool GraphMentionsReading(ForgeGraphSpec? g, string id)
    {
        if (g?.Nodes is null) return false;
        foreach (var n in g.Nodes)
        {
            if (n is null) continue;
            // ① 【数值】模块自己的公式栏
            if (ForgeFormula.MentionsIdentifier(n.Get("Formula", ""), id)) return true;
            // ② 【效果】/【条件】/【触发时机】/【关系符】上那些「数值 / 生效次数 / 命中次数 / 概率」栏
            //    也能**直接写公式**（和【数值】模块同一套语法，见 ForgeNumbers.NumberFields）。
            //    以前这里只扫 ① —— 于是「在【效果】的数值栏直接写 ThisCardDamage」时，
            //    卡牌类上那个 `ForgeThisCardDamage` 属性**不会生成**，可生成代码又引用了它 → CS1061
            //（用户实测报的 `CarmenCobaltScar`：`未包含 ForgeThisCardDamage 的定义`）。
            foreach (var (kind, key, _) in ForgeNumbers.NumberFields)
                if (string.Equals(n.Kind, kind, StringComparison.Ordinal)
                    && ForgeFormula.MentionsIdentifier(n.Get(key, ""), id)) return true;
        }
        return false;
    }

    /// <summary>这张图里有没有用「本张牌这一次打出造成的伤害」。</summary>
    public static bool GraphUsesThisCardDamage(ForgeGraphSpec? g) =>
        GraphMentionsReading(g, CSharpCodeGen.ThisCardDamageId);

    /// <summary>这张图里有没有用「本张牌这一次打出获得的格挡」。</summary>
    public static bool GraphUsesThisCardBlock(ForgeGraphSpec? g) =>
        GraphMentionsReading(g, CSharpCodeGen.ThisCardBlockId);

    /// <summary>这条【效果】要不要「先 CreateCard 再改」（勾了附加处理 / 加关键词时）。</summary>
    private static bool NeedsGraphSpawnModifiers(EffectSpec? s) =>
        s is not null && (s.HasSpawnModifier || s.HasSpawnKeyword);

    /// <summary>「仅本回合升级」那个临时 Power 的变量名（勾了才生成那一行 Apply）。</summary>
    private static string? GraphTempUpVar(ForgeNodeSpec node, EffectSpec? s) =>
        s is not null && s.SpawnUpgradedThisTurn ? UniqueVar(node, "__fgTempUp") : null;

    /// <summary>
    /// 把「生成 / 变化出来的卡」的附加处理写进 <paramref name="sb"/>：
    /// 直接复用卡牌那条链的 <see cref="CSharpCodeGen.EmitSpawnModifiers"/>（它已经把链的
    /// 「玩家 / 自己」参数化了），所以两条链的语义和文案不会漂。
    /// </summary>
    private static void EmitGraphSpawnModifiers(StringBuilder sb, CharacterProfile p, EffectSpec? spec,
        string indent, string cardVar, string? tempUpVar)
    {
        if (!NeedsGraphSpawnModifiers(spec)) return;
        var w = new CodeWriter();
        CSharpCodeGen.EmitSpawnModifiers(w, spec!, cardVar, tempUpVar, p, "__fgPlayer", "__fgSelf");
        foreach (string line in w.ToString().Replace("\r\n", "\n").Split('\n'))
        {
            string t = line.Trim();
            if (t.Length > 0) sb.AppendLine(indent + t);
        }
    }

    /// <summary>
    /// 「仅本回合升级」：先挂一个「回合结束把它们降回来」的状态（和卡牌那条链同一份实现）。
    /// <paramref name="ctx"/> 的 <c>CardSelfExpr</c> = 这张牌自己（遗物 / 药水图上是 <c>null</c>）——
    /// 和 <see cref="CSharpCodeGen.EmitGenerateCard"/> 的 <c>tempUpSource</c> 是同一个约定。
    /// </summary>
    private static void EmitGraphTempUpgradeApply(StringBuilder sb, CharacterProfile p, EffectSpec? spec,
        EmitCtx ctx, string indent, string? tempUpVar)
    {
        if (spec is not { SpawnUpgradedThisTurn: true }) return;
        // ⚠ 变量名必须和**用它的那一句**一致：`GraphTempUpVar` 给的是 `__fgTempUp` + 节点哈希
        //（同一张图上多处「仅本回合升级」才不会互相覆盖），而这里以前**写死了** `__fgTempUp` ——
        // 于是生成 `var __fgTempUp = …;` 但后面引用 `__fgTempUp<hash>` → CS0103
        //（用户实测报的 `DarkVinciSwingPoint.cs(40,17) 当前上下文中不存在名称 __fgTempUpda676`）。
        if (string.IsNullOrEmpty(tempUpVar)) return;
        var w = new CodeWriter();
        CSharpCodeGen.EmitTempUpgradePowerApply(w, tempUpVar, ctx.CardSelfExpr, "__fgSelf");
        foreach (string line in w.ToString().Replace("\r\n", "\n").Split('\n'))
        {
            string t = line.Trim();
            if (t.Length > 0) sb.AppendLine(indent + t);
        }
    }

    /// <summary>
    /// 「消耗 / 丢弃 / 升级手牌里的 N 张」——选牌类效果。
    /// 提示语是本体 <c>CardSelectorPrefs</c> 上的 LocString 常量，所以这几句内联生成
    /// （静态辅助类里没有 base），和「卡牌」页那套写法一致。
    /// </summary>
    /// <summary>
    /// 【效果】模块的「从哪里选牌」在节点图里对应的那一摞牌（本体写法 <c>PileType.X.GetPile(player)</c>）。
    ///
    /// V0.2.168：以前节点图的选牌类效果**只从手牌里挑**，别的三摞一律报错拦住
    ///（别人实测截图报的：「变化卡牌」的「从哪里选牌」在锻造节点图里不生效）。现在四摞都支持：
    /// 手牌走 <c>CardSelectCmd.FromHand</c>（老文本一个字不变），别的三摞走 <c>CardSelectCmd.FromCombatPile</c>
    /// ——和卡牌页那条链（<see cref="CSharpCodeGen"/> 的 PileExpr）同一套写法。
    /// 「牌顶」= 抽牌堆最上面那张（本体抽牌堆的 Cards 是「顶在前」）。
    /// </summary>
    private static string GraphPileExpr(EffectSpec? s, string player = "__fgPlayer") => s?.SelectPile switch
    {
        "Draw" or "Top" => $"PileType.Draw.GetPile({player})",
        "Discard" => $"PileType.Discard.GetPile({player})",
        "Exhaust" => $"PileType.Exhaust.GetPile({player})",
        _ => $"PileType.Hand.GetPile({player})",
    };

    /// <summary>
    /// 同一摞牌的**枚举**写法（<c>PileType.Hand</c> / <c>PileType.Draw</c> …），给
    /// <c>CardPile.GetCards(player, PileType)</c> 这类**第二参要 PileType** 的 API 用。
    ///
    /// <para>⚠ 别把 <see cref="GraphPileExpr"/> 塞进这种地方：那个返回的是 <c>CardPile</c>
    /// （`PileType.X.GetPile(player)`），第二参要枚举 → `CS1503 参数 2: 无法从 CardPile 转换为 PileType`
    /// （用户实测报的 `CarmenCrimsonScar` / `CarmenNihil` / `CarmenCobaltScar`，111 条）。</para>
    /// </summary>
    private static string GraphPileTypeExpr(EffectSpec? s) => s?.SelectPile switch
    {
        "Draw" or "Top" => "PileType.Draw",
        "Discard" => "PileType.Discard",
        "Exhaust" => "PileType.Exhaust",
        _ => "PileType.Hand",
    };

    /// <summary>这个效果是不是「从手牌里挑」（没设 / 手牌 → 老写法；别的三摞 → FromCombatPile）。</summary>
    private static bool IsHandPick(EffectSpec? s) =>
        s is null || string.IsNullOrWhiteSpace(s.SelectPile) || string.Equals(s.SelectPile, "Hand", StringComparison.OrdinalIgnoreCase);

    private static void EmitCardPick(StringBuilder sb, EmitCtx ctx, ForgeNodeSpec node, string kind, string amount, string indent,
        CharacterProfile? p = null)
    {
        var spec = GraphSpec(node);
        bool chosen = !string.Equals(node.Get("Pick", "Chosen"), "Random", StringComparison.OrdinalIgnoreCase);
        string var = UniqueVar(node, "__fgPick");
        // V0.2.221（用户报的「消耗丢弃升级指定卡没生效」）：图上这一档以前**根本没有**把
        // 「范围界定 / 只对这张卡（指定）」的谓词传给选牌 / 随机挑牌 —— 界面上那两栏能填、生成端却没读，
        // 填了等于没填（静默失效）。现在和「卡牌」页那几档走**同一份**判据
        //（CSharpCodeGen.CardScopePredicate：范围界定 + 只对这张卡，两者是「与」的关系）。
        // ⚠ 没设范围时输出和以前**逐字一致**（老图生成文本不变，红线）。
        var e = ForgeEffectBridge.Ensure(node);
        // V0.2.223（用户实测报的「状态牌局内升级好像没生效」）：图上「升级卡牌」以前**不筛能升级的牌**
        //（卡牌页那份 EmitUpgradeCard 一直筛 `c.IsUpgradable`）—— 于是选牌界面里连状态牌 / 诅咒 /
        // 已经升满的牌都能选，选中后 `CardCmd.Upgrade` 自己会跳过（`IsUpgradable` 为假）→ 什么都不发生。
        // 现在和卡牌页同一口径：升级 → `(c.IsUpgradable) && (范围界定…)`。
        string? scopeCond = CSharpCodeGen.CardScopeCond(e, p);
        string? updCond = kind == "UpgradeCard"
            ? (scopeCond is null ? "c.IsUpgradable" : $"(c.IsUpgradable) && ({scopeCond})")
            : scopeCond;
        string? pred = updCond is null ? null : "c => " + updCond;
        string act = kind switch
        {
            "ExhaustCard" => "await CardCmd.Exhaust(choiceContext, __fgCard);",
            "DiscardCard" => "await CardCmd.Discard(choiceContext, __fgCard);",
            _ => "CardCmd.Upgrade(__fgCard);",
        };
        string prompt = kind switch
        {
            "ExhaustCard" => "CardSelectorPrefs.ExhaustSelectionPrompt",
            "DiscardCard" => "CardSelectorPrefs.DiscardSelectionPrompt",
            _ => "CardSelectorPrefs.UpgradeSelectionPrompt",
        };
        // V0.2.170：「取几张」四档（用户点的第 2 条）。以前这一栏在节点图里被报错拦住。
        //   · 恰好 N 张 → `new CardSelectorPrefs(prompt, N)`（老文本逐字不变）；
        //   · 最多 N 张 / 任意张 → 本体的 `(prompt, 0, N)` 写法（见 CSharpCodeGen.SelectPrefsExpr）；
        //   · 全部 → 不弹选牌界面，那一摞里符合条件的每一张都动手（本体「恶魔之焰」那种）。
        CountModeMarker(sb, indent, e);
        if (e.CountIsAll)
        {
            sb.AppendLine(indent + $"foreach (CardModel __fgCard in ({GraphAllPickExpr(spec)}{GraphWhere(pred)}).ToList()) {act}");
            return;
        }
        string prefs = CSharpCodeGen.SelectPrefsExpr(e, prompt, $"(int)({amount})");
        if (chosen)
            sb.AppendLine(indent + (IsHandPick(spec)
                ? $"var {var} = (await CardSelectCmd.FromHand(context: {ctx.ChoiceVar}, player: __fgPlayer, prefs: {prefs}, filter: {pred ?? "null"}, source: {ctx.CardSelfExpr})).ToList();"
                : $"var {var} = (await CardSelectCmd.FromCombatPile({ctx.ChoiceVar}, {GraphPileExpr(spec)}, __fgPlayer, {prefs}{GraphFilterArg(pred)})).ToList();"));
        else
            sb.AppendLine(indent + $"var {var} = ({ctx.ChoiceVar} is null ? new List<CardModel>() : {GraphRandomPickExpr(spec, CSharpCodeGen.RangeN(e, $"(int)({amount})"))}{GraphWhere(pred)});");
        sb.AppendLine(indent + $"foreach (CardModel __fgCard in {var}) {act}");
    }

    /// <summary>
    /// 「范围界定 / 只对这张卡」的谓词拼到候选表达式后面（V0.2.221）。
    /// 没设范围（<paramref name="pred"/> = null）→ **空串**（老文本逐字不变）。
    /// </summary>
    private static string GraphWhere(string? pred) => pred is null ? "" : $".Where({pred})";

    /// <summary>同上，但拼成 <c>CardSelectCmd</c> 的 filter 实参（null → 空串 = 不传那个参数）。</summary>
    private static string GraphFilterArg(string? pred) => pred is null ? "" : ", " + pred;

    /// <summary>「取几张 = 全部」那一摞的全部牌（不弹选牌界面；手牌 / 别的三摞都是这个写法）。</summary>
    private static string GraphAllPickExpr(EffectSpec? s) =>
        $"CardPile.GetCards(__fgPlayer, {GraphPileTypeExpr(s)})";

    /// <summary>
    /// 「取几张」不是默认档（最多 N / 任意张 / 全部）时写一行标记 —— 回读靠它还原
    /// （和卡牌链的 <c>CSharpCodeGen.CountModeMarker</c> 同一个标记；默认档一个字符都不写）。
    /// </summary>
    private static void CountModeMarker(StringBuilder sb, string indent, EffectSpec e)
    {
        if (e.CountMode != CountModes.Exact) sb.AppendLine(indent + $"// CET:CountMode={e.CountMode}");
    }

    /// <summary>
    /// 「从某一摞随机拿 N 张」的表达式（节点图用）。
    /// 手牌 → 老写法（<c>CardPile.GetCards(...Hand)</c> + 战斗随机 + Take）；别的三摞同理只是换那一摞；
    /// 「牌顶」→ 不随机、直接拿最上面那张（本体抽牌堆顶在前）。
    /// </summary>
    private static string GraphRandomPickExpr(EffectSpec? s, string amount)
    {
        if (s is not null && string.Equals(s.SelectPile, "Top", StringComparison.OrdinalIgnoreCase))
            return $"{GraphPileExpr(s)}.Cards.Take((int)({amount})).ToList()";
        return $"CardPile.GetCards(__fgPlayer, {GraphPileTypeExpr(s)})"
             + $".OrderBy(_ => __fgPlayer.RunState.Rng.CombatCardSelection.NextInt(int.MaxValue)).Take((int)({amount})).ToList()";
    }

    // ==================================================================================
    //  校验（界面 / 校验器共用这一份措辞）
    // ==================================================================================

    /// <summary>图里的问题（返回空列表 = 没问题）。<paramref name="scope"/> = Card / Potion / Relic。</summary>
    public static List<string> Problems(ForgeGraphSpec? g, CharacterProfile p, string scope) =>
        Issues(g, p, scope).Select(i => i.Message).ToList();

    /// <summary>
    /// 【效果】模块**真的接上了**的那些效果种类（<see cref="EmitEffect"/> 里的大 switch 有实现）。
    ///
    /// ⚠ 这份名单和那段 switch 是**一对**，加/改实现时必须同步改这里。
    /// 为什么要单独列一份：校验器要拿它拦住「在节点图里选了、但生成端什么都没做」的组合 ——
    /// 别人实测报过「给召唤物 buff 不生效」，根因就是【效果】下拉列的是**全量候选**，
    /// 而「给伙伴施加状态」这一档在节点图里只写了一行注释、一行代码都没生成。
    /// 探针 graphkindprobe 会把这份名单和**实际生成结果**逐个对一遍，防止它漂移。
    /// </summary>
    public static IReadOnlySet<string> SupportedEffectKinds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // ---------- 基础（单体 / 群体都走 foreach 或群体命令）----------
        "Damage", "Block", "Draw", "Energy", "Heal", "HpLoss", "MaxHp", "Gold", "ExtraResource",
        "ApplyPower", "Stun", "DoublePower", "ClearPowerOne", "ClearPowerDebuffs", "ForbidBlock",
        // V0.2.309：即死（用户报「卡牌「监视」的即死节点图里还没接上」）—— 生成 CreatureCmd.Kill
        "InstantKill",
        // V0.2.310：「升级本场战斗中临时增加的牌」（状态链 / 卡牌链都有，图上也补上；包一层块作用域避免重复声明 __upgGen）
        "UpgradeGenerated",
        "TempPower", "EndTurn",
        // ---------- 牌堆 / 选牌类 ----------
        "GenerateCard", "GainPotion", "ExhaustCard", "DiscardCard", "TransformCard", "UpgradeCard",
        // V0.2.168：卡牌页 / 状态页早就支持、节点图以前没接的这几档（用户截图报的「删除卡牌（全局：从牌组删牌）」）
        "AddCardGlobal", "TransformCardGlobal", "RemoveCardGlobal", "UpgradeCardGlobal",
        "Outbreak", "PlaySfx", "PlaySoundFile", "BoostCard",
        "CopyCard", "Scry", "TakeFromDraw", "TakeFromDiscard", "TakeFromExhaust",
        // V0.2.170：要「打出这张牌」那一刻上下文的几种（用户点的第 3 条）——
        // 重放卡牌 / 回合结束时自动打出 / 将此牌放回手牌（和卡牌页共用同一份发射器）。
        "ReplayCard", "TurnEndPlay", "ReturnSelfToHand",
        // V0.2.203：本回合这张牌自己费用变 0（用户截图报的「处决」）——
        // **只有卡牌**那张布有「这张牌」；状态 / 遗物 / 药水图里选它由校验器单独报错（见 Issues）。
        "CheapenSelf",
        // ---------- 召唤物 ----------
        "PetApplyPower",
        // V0.2.176：把「卡牌页支持、节点图以前没接」的那批补上（用户要求「全做」）——
        // 这一批是「图上就能表达」的：放置卡牌 / 给予关键词 / 刮削 / 填满手牌 / 禁止回复生命 / 暴露。
        "PlaceCard", "GiveKeyword", "ScrapeDraw", "FillHand", "ForbidHeal", "Expose",
        // V0.2.176 第二批：消耗「本场战斗级」辅助类的几种（辅助类的「用到才生成」已由
        // CSharpCodeGen.AllEffects 统一收上节点图的效果，不会 CS0246）。
        "ExtraTurn", "CheapenDraw", "RaiseResourceCap", "OverdraftEnergy",
        // V0.2.250（用户问「这个范围怎么不能选组或者特定关键词」）：节点图上的「减少卡牌费用」——
        // 「哪一类牌」由【效果】模块新加的「范围」栏给（SpawnFilter，含自定义关键词组 / 每个组）。
        "CostDown",
        // V0.2.176 第三批：伙伴组（召唤 + 攻击 ×4 + 治疗 / 失去生命 / 最大生命 / 牺牲 / 施加状态 / 承伤开关）
        "SummonPet",
        "PetAttack", "PetDamageByMaxHp", "PetDamageByCurHp", "PetDamageByMissingHp",
        "PetHeal", "PetLoseHp", "PetGainMaxHp", "PetSacrifice", "PetGuardOn", "PetGuardOff",
        // V0.2.176：大限已至（伤害 = 目标的灾厄层数 × 数值）
        "TimesUp",
        // V0.2.176 第四批：先古转化「转化为先古卡」（卡牌链 + 节点图都接上了）
        // ⚠「转化为先古遗物」**故意不在节点图上做**：它要选「换成哪只遗物」，
        // 而节点的「效果」模块是按卡牌页那套字段存的（没有遗物下拉）→ 用户选不到正确的值，
        // 生成出来会引用一张卡当遗物类（CS0311）。它仍然只在**遗物 / 药水**的效果列表里可用。
        "AncientTranscend",
        // 卡牌奖励：只在【触发时机】=「战斗胜利时」那一支上才生成（见 EmitEffect 里那段），
        // 所以它在别的时机上「没有代码」是**故意的**，那一条由它自己的提示负责，不算「没接」。
        "CardReward",
    };

    /// <summary>
    /// 【效果】模块上「只有**卡牌**才有意义」的那几种。
    ///
    /// 它们在本体里不是「一条效果」，而是**这张牌自己的行为**：
    ///   · <c>CheapenSelf</c>（本回合这张牌自己费用变 0）→ 生成的是 <c>this.SetToFreeThisTurn()</c>，
    ///     <c>this</c> 必须是那张 CardModel。<b>V0.2.203 起「卡牌」那张布上真的能生成了</b>
    ///     （它在 <see cref="SupportedEffectKinds"/> 里了）；这一份名单现在管的是**别的布**
    ///     （状态 / 遗物 / 药水没有「这张牌」）→ 写一行说清楚的注释，校验器那边另有一条错误。
    ///   · <c>CheapenSelfOnDraw</c>（抽到这张牌时它本场战斗费用 -N）→ 生成的是卡自己的
    ///     <c>AfterCardDrawn</c> 钩子（见 <c>CSharpCodeGen.EmitSelfDrawHook</c>），
    ///     节点图的【效果】模块**没有「牌自己」这个概念**可以挂（卡牌图上也一样）。
    /// </summary>
    private static readonly HashSet<string> CardOnlyEffectKinds = new(StringComparer.Ordinal)
    {
        "CheapenSelf", "CheapenSelfOnDraw",
    };

    /// <summary>
    /// 【效果】模块上「必须选一个状态」的种类（没选就一行代码都不生成，不能让用户猜）。</summary>
    private static readonly string[] NeedsPowerField =
        { "ApplyPower", "TempPower", "DoublePower", "ClearPowerOne", "PetApplyPower", "Expose" };

    /// <summary>
    /// 【效果】模块上「编辑器里能填、但节点图这一版**不生成**」的栏位（返回中文说明，空 = 都支持）。
    ///
    /// 为什么需要它（V0.2.129，用户拍板 Q1=A）：【效果】模块现在和「卡牌」页是**同一套编辑器**
    /// （同一份 <see cref="EffectSpec"/>、同一份 `EffectCommon` 模板）—— 卡牌页有的栏位它都有。
    /// 但节点图的发射器只覆盖其中一部分（见 <see cref="SupportedEffectKinds"/> 与 <c>EmitEffect</c>
    /// 里每个栏位到底被谁读）。**不许静默**：填了这些栏位就报错，说清「不生效 + 怎么改」。
    ///
    /// 这份清单**只对「节点图支持的种类」跑**（不支持的种类第一条错误已经拦住了，不必再刷一屏）。
    /// </summary>
    public static List<(string Level, string Message)> UnsupportedEffectSettings(ForgeGraphSpec? g, ForgeNodeSpec node,
        string scope = "Card")
    {
        var list = new List<(string, string)>();
        var e = ForgeEffectBridge.Ensure(node);
        void Add(string what, string how) => list.Add(("错误", $"「{what}」在锻造节点图里不生效 —— {how}"));
        // 「警告」档：这一栏**老存档里就可能有**（V0.2.128 的【效果】模块对**所有**种类都摆着
        // 「下回合生效」那个勾选框，勾了也只是生成一行注释）。报错的话老存档会突然生成不出来 ——
        // 所以这一档只**警告**（配置检查里看得见、不挡住生成），别的一律报错。
        void Warn(string what, string how) => list.Add(("警告", $"「{what}」在锻造节点图里不生效 —— {how}"));

        if (e.UpgradeAmount != 0m)
            Add("升级增量", "节点图没有「升级增量」这个概念；要给升级改数值请在【数值】模块里写公式。");
        if (e.AmountIsXEffective || e.TimesIsX || e.RepeatIsX)
            Add("数值 / 生效次数 / 命中次数 = X", "节点图里的 X / Y 是【能量】模块送出来的：把它的出口接到【效果】左边的「数值」口，"
                + "再在【数值】模块里写 X / Y。");
        // V0.2.194：状态那一档**支持这三个开关**（生成端把「本状态层数」写成 base.Amount，
        // 见 EmitEffect 里 AmountIsStack / TimesIsStack / RepeatIsStack 那两处）——
        // 所以状态这块布上不再报错（用户 31 只状态里有 16 只用到「数值 = 本状态层数」，
        // 一报错就整份配置生成不出来）。卡牌 / 遗物 / 药水照旧报错。
        if ((e.AmountIsStack || e.TimesIsStack || e.RepeatIsStack)
            && !string.Equals(scope, ForgeTimings.PowerScope, StringComparison.Ordinal))
            Add("「= 本状态层数」那几个开关", "那是「自定义状态」触发器的开关；节点图里用【状态】模块 + 【数值】模块取值。");
        if (e.Condition is not null && !e.Condition.IsNone)
            Add("条件选项", "节点图里用【条件】模块 + 【if】/【且】/【或】/【关系符】表达（【效果】模块自己不带条件）。");
        if (e.SlowPercent > 0m)
            Add("直接把「缓慢」设成 N%", "节点图只按层数施加状态（要用那一档请到「卡牌 / 遗物 / 药水」页的效果列表里配）。");
        // V0.2.170：「取卡方式」四档（用户点的第 1 条）**已经支持了** —— 生成端走
        // CSharpCodeGen.EmitGenerateCard / EmitTransformCardPublic / EmitGlobalCardEffectPublic
        // （范围池 / 候选 / 选牌界面 / 附加处理全都复用卡牌页那一份），所以不再报错。
        // 只剩一个理论上点不出来的组合（「自己挑 / 多选1」却不带「按范围随机」——界面上那两个勾选框
        // 只在「按范围随机」档才显示）照样报错，说清怎么改。
        if (!e.IsSpawnRandom && (e.SpawnChosen || e.HasSpawnChoice))
            Add("取卡方式 = 多选1 / 从范围里自己挑一张（但「取卡方式」那一栏不是「按范围随机」）",
                "这两档要先有「范围」：请把「取卡方式」改成「按范围随机」，再勾「多选1」或「从范围里自己挑一张」。");
        // V0.2.167：下面这三项**已经支持了**（生成端直接读 spec，见 EmitGraphSpawnModifiers /
        // EmitGraphTempUpgradeApply / 复制那一段），所以不再报错：
        //   · 生成 / 变化出来的卡的附加处理（升级 / 免费 / 仅本回合免费 / 仅本回合升级）；
        //   · 生成 / 变化出来的卡：加上关键词；
        //   · 复制的份数（节点图的「复制卡牌」现在按份数复制）。
        // V0.2.168：「从哪里选牌」**已经支持了**（手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆 / 牌顶，
        // 见 EmitCardPick / GraphPileExpr / GraphRandomPickExpr）—— 以前这一栏在节点图里会被报错拦住
        //（别人实测截图报的：「变化卡牌」的「从哪里选牌」在锻造节点图里不生效）。
        // V0.2.170：「取几张」四档（恰好 / 最多 N / 任意张 / 全部）**已经支持了** ——
        // 生成端读 e.CountMode / e.CountIsAll，见 EmitCardPick / TakeFrom* / 变化·复制那三段，
        // 并写 `// CET:CountMode=` 标记供回读（用户点的第 2 条），所以不再报错。
        // V0.2.176：「召唤物（哪一只）」**节点图现在支持了** —— 伙伴组的发射器（见 EmitGraphPetEffect）
        // 就是按 `spec.PetSummon` 查那只伙伴的（变量名也按它的类名取），和卡牌页同一份数据。
        // 以前这里报错说「由【对象】模块决定哪一只」，但发射器并没有读对象模块那一档 —— 报错与实际不符，
        // 而且会把「伙伴攻击 / 治疗伙伴」这些**刚接上**的种类整条拦下来（生成前就中止）。
        // 所以这一栏不再报错（下面 `e.IsPetEffect` 那一套检查照旧管别的栏位）。
        // V0.2.170：「下回合生效」已经铺到**全部做得了的种类**（用户点的第 2 条，见 EmitEffect 里
        // 那个 EmitDelayedPowerApply 分支）—— 只有本体没有延迟落点的那 8 种（EffectCatalog.NextTurnMeaningless）
        // 才警告；警告（不报错）是因为老存档里勾过这个勾选框、当时也只是生成一行注释，报错会突然生不出来。
        if (e.NextTurn && !EffectCatalog.HonorsNextTurn(e.Kind))
            Warn("下回合生效", "这个种类本体没有「下回合再生效」的落点（声音 / 结束回合 / 自己变便宜之类），"
                + "节点图会按**立即生效**生成。");
        // V0.2.170：「将此牌放回手牌」在节点图里是**内联**的（不是卡牌页那种钩子），所以要挂在
        // 「牌已经不在手牌里」的时机上（被消耗 / 被丢弃）—— 挂在「打出一张牌时」上时这张牌还在手牌里，
        // 而且本体是打完牌之后才把它放进弃牌堆的（PlayCardAction），搬回去会被那一步覆盖。
        if (string.Equals(e.Kind, "ReturnSelfToHand", StringComparison.Ordinal) && g is not null)
        {
            bool lateEnough = g.TriggerNodes.Any(t => ForgeTimings.Find(t.Get("Timing", ForgeTimings.CardPlayed), "Card")?.Hook
                is "AfterCardExhausted" or "AfterCardDiscarded");
            if (!lateEnough)
                Warn("将此牌放回手牌", "节点图里它是**内联**生成的：要挂在【触发时机】=「这张牌被消耗时 / 这张牌被丢弃时」这类"
                    + "「牌已经不在手牌里」的时机上才生效 —— 挂在「打出一张牌时」上时这张牌还在手牌里"
                    + "（本体是打完牌之后才把它放进弃牌堆的），这一步会被随后的入堆动作覆盖（看不出效果）。");
        }
        // 「作用对象」这一栏是支持的（TargetSideCustom），但**显式接线的【对象】优先**：
        // 用户可能在两边都设了，说清谁说了算（只是提示，不挡住生成）。
        if (e.TargetSideCustom && g is not null && IncomingNode(g, node, 2) is not null)
            list.Add(("提示", "「作用对象」这一栏被【效果】左边那个「指向对象」口上接的【对象】模块盖住了 —— "
                + "生成时以接线为准（要按「作用对象」走就把那条线删掉）。"));
        return list;
    }

    /// <summary>
    /// 图里的问题（给校验器用）：<c>(级别, 说明)</c>，级别 = 错误 / 警告 / 提示。
    /// <paramref name="scope"/> = Card / Potion / Relic。
    /// </summary>
    public static List<(string Level, string Message)> Issues(ForgeGraphSpec? g, CharacterProfile p, string scope,
        string? cardTargetType = null)
    {
        var list = new List<(string, string)>();
        if (g is null || g.IsEmpty) return list;
        // V0.2.129：【效果】模块和卡牌共用一套编辑器 —— 「新编辑器改过」的模块先把 spec 回写成老字段，
        // 下面全部按老字段校验（老模块原样不动）。界面 / CLI 都走这里，所以两边看到的问题一致。
        ForgeEffectBridge.SyncGraph(g);
        var trigger = g.TriggerNode;
        if (trigger is null)
        {
            list.Add(("警告", "这张布上没有【触发时机】模块 —— 整张图不会执行（新建一条锻造条目时会自动放一个）。"));
            return list;
        }
        // V0.2.168：一块布可以有**多块**【触发时机】—— 每块都按同一套规则核一遍（时机认不认得、是不是这类物品的、
        // 遗物档这个钩子造不造得出 choiceContext）。
        var triggerNodes = g.TriggerNodes.ToList();
        // V0.2.170：「斩杀」条件（用户点的第 3 条）—— 它判的是「这张牌**打出时**那一次攻击」的结果，
        // 只有卡牌档做得了；遗物 / 药水档没有「打出这张牌」这件事（生成出来引用不到 __lastAttack / __fatalOk）。
        // 挂在拿不到 cardPlay 的时机上（被消耗 / 被丢弃 / 被抽到…）只**警告**：代码能编译，但条件不会成立。
        if (GraphUsesFatal(g))
        {
            if (!string.Equals(scope, "Card", StringComparison.Ordinal))
                list.Add(("错误", "这张图里用了「斩杀」条件 —— 它判的是「这张牌刚打出的那一次攻击」的结果，"
                    + $"只有**卡牌**档做得了（这块布是{ScopeZh(scope)}的，没有「打出这张牌」这件事）⇒ "
                    + "生成出来会引用不存在的变量（CS0103，整个模组编译不过）。请把「斩杀」挪到卡牌上，或者把条件换成别的。"));
            else if (!Plans(g, "Card").Any(pl => string.Equals(pl.Timing.Hook, "OnPlay", StringComparison.Ordinal)))
                list.Add(("警告", "这张图里用了「斩杀」条件，但它的【触发时机】不是「打出一张牌时」—— "
                    + "那个条件判的是「这张牌刚打出的那一次攻击」的结果，别的时机上拿不到「打出时选中的目标」，"
                    + "生成出来的条件**不会成立**（代码能编译）。请把带「斩杀」的那条链挪到「打出一张牌时」这块【触发时机】上。"));
        }        // V0.2.194：两条「这一次打出」的读数只在**「卡牌被打出时」**那一块布上记账
        // （打出时清零、每条对应效果之后累加）。挂在别的卡牌时机上时，生成出来代码能编译，
        // 但读到的恒是 0（我们不记账）—— 说清一句，别让用户以为配好了。
        if (string.Equals(scope, "Card", StringComparison.Ordinal)
            && !Plans(g, "Card").Any(pl => string.Equals(pl.Timing.Hook, "OnPlay", StringComparison.Ordinal)))
            foreach (var r in CSharpCodeGen.CardReadingsAll)
                if (GraphMentionsReading(g, r.Id))
                    list.Add(("警告", $"这张图里用了「{r.Id}」（{r.Zh}），但它的【触发时机】不是「卡牌被打出时」——"
                        + "这条读数只在**打出这张牌**那一刻记账（别的时机跑的时候，打出那一刻早就过去了），"
                        + "所以生成出来读到的恒为 0。请把用到它的那条链挪到「卡牌被打出时」那块【触发时机】上。"));
        // V0.2.251（用户问「条件增加『上一张打出的牌是某种范围的牌』，范围可以填组」）：
        // 这两条「上一张打出的牌」的条件在图上同样要**把没填完的说出来** ——
        // 图上的【条件】模块走的也是 ConditionSpec，但以前只经过 ConditionExpr，
        // 从来没人校验过它填全了没：空关键词 = 恒假条件（这条链永远不执行）；
        // 空范围 = 恒真条件（等于没判）；空组 = 谁也匹配不到。
        foreach (var n in g.Nodes)
        {
            if (n is null || !string.Equals(n.Kind, "Condition", StringComparison.Ordinal)) continue;
            string condKind = n.Get("Cond", "None");
            bool isKw = string.Equals(condKind, "LastCardKeyword", StringComparison.Ordinal);
            bool isScope = string.Equals(condKind, "LastCardScope", StringComparison.Ordinal);
            if (!isKw && !isScope) continue;
            string kw = (n.Get("Keyword") ?? "").Trim();
            string sc = (n.Get("Scope") ?? "").Trim();
            if (isKw && EffectCatalog.NormalizeVanillaKeyword(kw) is null)
                list.Add(("错误", "「上一张打出的牌带有某关键词」那块【条件】还没选**关键词** —— "
                    + "生成出来是「永远不成立」，那条链一次都不会执行。在模块上的「关键词」栏选一个本体关键词。"));
            if (isScope && sc.Length == 0)
                list.Add(("提示", "「上一张打出的牌是某种范围的牌」那块【条件】**范围留空** = 任意牌，"
                    + "这条条件等于没判（永远成立）。在模块上的「范围」栏选一类牌 / 一个组 / 一个自定义关键词。"));
            else if (isScope && EffectCatalog.IsCardGroupFilter(sc) && EffectCatalog.CardGroupCards(p, sc).Count == 0)
                list.Add(("警告", "「上一张打出的牌是某种范围的牌」那块【条件】选的**组是空的** —— "
                    + "组里一张牌都没有，这条条件永远不成立。到「组」页给它勾几张牌。"));
        }
        var triggerOptions = new List<ForgeTimings.TimingOption?>();
        // V0.2.216（用户实测：「处于二形态时触发不了多效卡效果只能打24的基础效果」）：
        // 图上「会做事」的模块（效果 / if / 且 / 或 / 条件）如果没接到【触发时机】这条链上，
        // 生成时**一个字都不会输出** —— 以前完全静默。现在给一条警告把模块点出来。
        // 是警告不是错误：在布上留几个备用模块是正常用法，不该拦住生成。
        {
            var missed = UnreachableModules(g);
            if (missed.Count > 0)
            {
                string names = string.Join("、", missed.Take(6).Select(n => n.Kind switch
                {
                    "Effect" => $"「{EffectCatalog.FindKind(n.Get("Effect", "Damage")).Display}」",
                    "If" => "「如果」",
                    "And" => "「且」",
                    "Or" => "「或」",
                    "Condition" => "「条件」",
                    _ => $"「{n.Kind}」",
                }));
                list.Add(("警告", $"这张图上有 {missed.Count} 块模块**没接到【触发时机】这条链上**（{names}"
                    + (missed.Count > 6 ? "…" : "") + $"，fg:{missed[0].Id} 为首）—— 它们**不会执行**，"
                    + "生成出来的代码里也会留一段「没接上」的注释。常见原因：【关系符】的「对象 / 左值」没接线、"
                    + "【是 / 否】那一支忘记接线。想让它们生效就把线补上。"));
            }
        }
        // V0.2.218：「不满足时 = 整张牌打不出去」这一档只有卡牌布做得了（遗物 / 药水 / 状态没有「打出」），
        // 而且那两条判据（IsPlayable / 描金边）是**表达式体属性**、放不下局部变量 →
        // 条件里的 N / M 必须是生成期常量（写了运行时公式就跳过它，别静默）。
        {
            int gates = UnplayableGates(g, p).Count;
            if (gates > 0 && !string.Equals(scope, "Card", StringComparison.Ordinal))
                list.Add(("错误", $"这张图里有 {gates} 个【条件】勾了「不满足时整张牌打不出去」，"
                    + $"但这块布是{ScopeZh(scope)}的 —— 遗物 / 药水 / 状态没有「这张牌打不出去」这回事。"
                    + "请把那一栏改回「跳过这一支」，或者把这条条件放到卡牌上。"));
            var skipped = UnplayableGatesSkipped(g, p);
            if (skipped.Count > 0)
                list.Add(("警告", $"有 {skipped.Count} 个【条件】勾了「不满足时整张牌打不出去」，但它的「数值 N / M」写了"
                    + $"运行时才算的公式（例：{skipped[0].Get("Amount")}）—— 「打不出去」那个判定是"
                    + "`IsPlayable` 的表达式体属性，里面**不能声明局部变量**，所以这一条**不会生效**（生成时被跳过）。"
                    + "请把数值改成固定数字 / 生成期算得出来的式子（例：3、A+2），或者把这一栏改回「跳过这一支」。"));
        }
        foreach (var trg in triggerNodes)
        {
            var o = ForgeTimings.Find(trg.Get("Timing", ForgeTimings.CardPlayed), scope);
            string where = triggerNodes.Count > 1
                ? $"第 {triggerNodes.IndexOf(trg) + 1} 块【触发时机】" : "【触发时机】";
            if (o is null)
                list.Add(("错误", $"{where}选的「{trg.Get("Timing")}」认不出来，请重新选一次。"));
            else if (!string.Equals(o.Scope, scope, StringComparison.Ordinal))
                list.Add(("错误", $"{where}选的「{o.Display}」是给{ScopeZh(o.Scope)}用的，这块布是{ScopeZh(scope)}的。"));
            else if (string.Equals(scope, "Relic", StringComparison.Ordinal) && GraphNeedsChoiceContext(g))
            {
                // 第 9 批 ④：遗物档那几条时机里，本体的钩子**没有** choiceContext、也造不出来的
                //（战斗胜利 / 战斗结束 / 获得金币 / 遗物被点击）—— 图里真要 context 时只能传 null!，
                // 运行时会炸。宁可在这里报错说清，也不静默生成坏代码。
                string ctxFrom = RelicCtxFrom(new HookPlan(trg, o), nested: false);
                if (string.Equals(ctxFrom, "None", StringComparison.Ordinal))
                    list.Add(("错误", $"{where}选的「{o.Display}」：本体的这个钩子**没有** choiceContext，"
                        + "而且造不出来（战斗胜利 / 战斗结束 / 获得金币 / 遗物被点击这几个时刻没有可用的选择上下文）——"
                        + "图里「抽牌 / 造成伤害 / 施加状态 / 选牌」这类**需要玩家选择**的效果会传 null! 在运行时炸。"
                        + "请把这些效果挪到别的触发时机上（战斗中的那些时机都行）。"));
            }
            triggerOptions.Add(o);
        }
        // 两块【触发时机】落在**同一个本体钩子**上：生成出来是同一个类里两个签名一样的 override
        //（CS0111，整个模组编译不过）—— 生成端只写第一块（见 SplitByHook），这里必须报错说清怎么办。
        {
            var byHook = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < triggerOptions.Count; i++)
            {
                var o = triggerOptions[i];
                if (o is null) continue;
                if (byHook.TryGetValue(o.Hook, out int first))
                {
                    string hookZh = ForgeTimings.For(scope).FirstOrDefault(x => string.Equals(x.Hook, o.Hook, StringComparison.Ordinal))?.Display ?? o.Display;
                    list.Add(("提示", $"这块布上第 {first + 1} 块和第 {i + 1} 块【触发时机】落在**同一个本体钩子**上"
                        + $"（「{hookZh}」和「{o.Display}」其实是同一个时刻）—— V0.2.279 起这两块会**合并进同一个方法**，"
                        + "各自包一层 { } 各跑各的，不需要再手动合成一块（想区分它们的话，用【条件】/【if】把两条链分开即可）。"));


                }
                else byHook[o.Hook] = i;
            }
        }
        // 「仅生效 N 次（本回合 / 本场战斗）」的复位钩子正好被某一块【触发时机】占用了：
        // 复位方法不能再写一遍（CS0111），那一支的计数这一次不会清零 —— 说清，不静默。
        for (int i = 0; i < triggerNodes.Count; i++)
        {
            var trg = triggerNodes[i];
            var o = triggerOptions[i];
            if (o is null) continue;
            string cntScope = trg.Get("CountScope", "Combat");
            var lim = ForgeNumbers.Resolve(p, trg, "CountLimit", 0);
            if (!lim.Runtime && (int)Math.Round((double)lim.Value) <= 0) continue;
            if (string.Equals(cntScope, "Run", StringComparison.Ordinal)) continue;
            string resetHook = string.Equals(cntScope, "Turn", StringComparison.Ordinal)
                ? "AfterPlayerTurnStart" : "AfterCombatVictory";
            bool clash = triggerNodes.Where((_, j) => j != i)
                .Any(x => string.Equals(ForgeTimings.Find(x.Get("Timing", ForgeTimings.CardPlayed), scope)?.Hook, resetHook, StringComparison.Ordinal));
            if (clash)
                list.Add(("提示", $"【触发时机】「{o.Display}」填了「仅生效 N 次（{(string.Equals(cntScope, "Turn", StringComparison.Ordinal) ? "本回合" : "本场战斗")}）」："
                    + "清零用的那个钩子（"
                    + (string.Equals(cntScope, "Turn", StringComparison.Ordinal) ? "每回合开始时" : "战斗胜利时")
                    + "）已经被这块布上**另一块【触发时机】**占用了 —— 同一个方法不能写两遍（编译不过），"
                    + "所以这一支的计数**不会清零**（用满 N 次之后就不再触发）。要清零请把那一块换成别的时机。"));
        }

        foreach (var n in g.Nodes)
        {
            if (n is null) continue;
            // ===== V0.2.128：每个模块里那些「能填数值的地方」现在也能填公式/参数 =====
            // 逐栏核一遍（清单 = ForgeNumbers.NumberFields，生成端 / 界面提示 / 这里共用同一份）：
            //   · 解析不出来 → **错误**（说清是哪个模块哪一栏）——绝不再静默回落默认值；
            //   · 用了不存在的参数 → 错误；
            //   · 卡牌费用那两栏要编译期常量 → 写了运行时公式就报错（本体 base(cost, …) 收不了运行时值）。
            string kindId = n.KindDef?.Id ?? n.Kind;
            string kindZh = n.KindDef?.Display ?? n.Kind;
            foreach (var (fieldKind, key, label) in ForgeNumbers.NumberFields)
            {
                if (!string.Equals(kindId, fieldKind, StringComparison.Ordinal)) continue;
                string text = ForgeNumbers.TextOf(n, key);
                if (text.Length == 0) continue;
                var num = ForgeNumbers.Resolve(p, n, key, 0);
                if (!num.Ok)
                {
                    string why = num.Error.Length > 0 ? num.Error : "解析不出来";
                    list.Add(("错误", $"【{kindZh}】的「{label}」填的「{text}」有问题：{why}"
                        + "（这一栏可以填数字，也可以填公式：X = 当前能量，Y = 当前额外资源量，一个字母的自定义参数。"
                        + $"内置只读量：{FormulaBuiltins.Names}）"));
                    continue;
                }
                var root = ForgeFormula.TryParse(text, out _);
                foreach (string id in ForgeFormula.Identifiers(root))
                {
                    if (id is "X" or "Y") continue;
                    // 卡牌专属的读数（ThisCardDamage）：读的是**卡片派生类**上的属性 —— 遗物 / 药水 / 状态的图上
                    // 编不过（`this` 不是卡），拦下来并说清（V0.2.194）。
                    if (FormulaBuiltins.Find(id) is { CardOnly: true } cardOnly)
                    {
                        if (!string.Equals(scope, "Card", StringComparison.Ordinal))
                            list.Add(("错误", $"【{kindZh}】的「{label}」用了「{cardOnly.Id}」（{cardOnly.Display}）——"
                                + "它读的是**这张牌自己**的字段，只有**卡牌**的图上能用（遗物 / 药水 / 状态的图上 `this` 不是卡，生成出来编译不过）。"
                                + "想用「本回合 / 本场战斗造成的伤害」请改内置读数 DamageDealtThisTurn / DamageDealtThisCombat。"));
                        continue;
                    }
                    if (FormulaBuiltins.Find(id) is not null) continue;
                    if (!p.CustomParams.Any(x => x is not null && string.Equals(ParamNames.Normalize(x.Name), id, StringComparison.Ordinal)))
                        list.Add(("错误", $"【{kindZh}】的「{label}」用了参数 {id}=，但「自定义参数 / 组」页里没有这个参数"
                            + $"（内置只读量可以直接写：{FormulaBuiltins.Names}）。"));
                }
                if (num.Runtime && ForgeNumbers.NeedsCompileTimeConst(fieldKind, key))
                {
                    // V0.2.215（用户实测报的「虚化」生成失败）：这一栏写 **X / Y** 是**合法**的 ——
                    // 那正是本体「消耗全部能量 / 全部额外资源量」（HasEnergyCostX / HasStarCostX）那一套，
                    // 生成端已经接上了（构造函数写 0 + 打出时 ResolveEnergyXValue）。
                    // 以前这里一律当「运行时值」拦住，而这一栏的提示里明明写着「X = 消耗掉的能量」——
                    // 自相矛盾，用户被自己的提示卡死。现在只拦真正做不到的（读血量 / 读状态 / 实时读数…）。
                    bool isCostSymbol = ForgeNumbers.ResolveCost(p, text,
                        key == "ResourceCost" ? "Y" : "X",
                        allowPlus: key != "ResourceCost",
                        label).IsX;
                    if (!isCostSymbol)
                        list.Add(("错误", $"【{kindZh}】的「{label}」只能填**生成期就算得出来的数**："
                            + "这一栏会写进卡牌自己的费用（本体 base(cost, …) / CanonicalStarCost），**必须**是编译期常量。"
                            + $"「{text}」里用了运行时才算的量 —— 改成固定数字、用「固定数值」来源的参数写 A+3，"
                            + "或者写 X（消耗全部能量）/ Y（消耗全部额外资源量，只有额外资源量那一栏认）。"));
                }
                if (num.Runtime && string.Equals(kindId, "Effect", StringComparison.Ordinal)
                    && string.Equals(key, "Amount", StringComparison.Ordinal)
                    && string.Equals(n.Get("Effect", "Damage"), "TempPower", StringComparison.Ordinal))
                    list.Add(("提示", $"【{kindZh}】的「{label}」写了公式：「临时增益 / 临时减益」是按**数值是正还是负**"
                        + "分两条路生成的（负数 = 扣层那条），生成期看不出公式算出正还是负 —— 所以按「正数那条路」生成。"
                        + "要用扣层那条路，请把数值改成固定的负数。"));
                if (num.Runtime && string.Equals(kindId, "Trigger", StringComparison.Ordinal)
                    && string.Equals(key, "CountLimit", StringComparison.Ordinal))
                    list.Add(("提示", $"【{kindZh}】的「{label}」写了公式（{text}）：按「有限次」生成 ——"
                        + "公式算出来是 0 或负数时这一支**一次都不会跑**。"));
                if (num.Runtime && string.Equals(kindId, "Condition", StringComparison.Ordinal)
                    && string.Equals(n.Get("Cond", "None"), "HasPowerAtLeast", StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(n.Get("PowerId")))
                    list.Add(("错误", $"【{kindZh}】选了「拥有某状态至少 N 层」、数值写了公式，但**没选状态** ——"
                        + "生成出来那一支恒为「不成立」。")); 
            }
            if (n.Kind == ForgeGraphCatalog.PowerListKind)
            {
                // V0.2.192：【状态列表】的两条自检 —— ①选了「只看指定状态」却没选状态（生成端整块不生成）；
                // ②出口没接到任何地方（搭了一块但没人用，等于白搭）。
                string listFilter = ForgeGraphCatalog.PowerListFilterOf(n);
                if (string.Equals(listFilter, ForgeGraphCatalog.PowerListFilterOne, StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(n.Get("PowerId")))
                    list.Add(("错误", "【状态列表】选了「只看指定状态」，但**没选状态** —— 这一块不会生成任何代码；"
                        + "请在下面「指定的那个状态」里选一个（可以打字搜索）。"));
                if (g.Outgoing(n.Id, 0).Count == 0)
                    list.Add(("警告", $"【状态列表】（{ForgeGraphCatalog.PowerListFilterZh(listFilter)}·"
                        + $"{ForgeGraphCatalog.PowerListWhatZh(ForgeGraphCatalog.PowerListWhatOf(n))}）的「数值」出口**没接线** —— "
                        + "它不参与生成。要让它起作用：把出口接到【关系符】的「左值」（再比较 0 / N），"
                        + "或者接到【效果】的「数值」口上。"));
                continue;
            }
            if (n.Kind == "Value")
            {
                string f = n.Get("Formula").Trim();
                if (f.Length == 0) continue;
                var root = ForgeFormula.TryParse(f, out string err);
                if (root is null) { list.Add(("错误", $"【数值】的公式「{f}」有问题：{err}")); continue; }
                foreach (string id in ForgeFormula.Identifiers(root))
                {
                    if (id is "X" or "Y") continue;
                    // 卡牌专属的读数（ThisCardDamage）：只能在**卡牌**的图上用（理由同上一条）
                    if (FormulaBuiltins.Find(id) is { CardOnly: true } cardOnly)
                    {
                        if (!string.Equals(scope, "Card", StringComparison.Ordinal))
                            list.Add(("错误", $"【数值】的公式「{f}」用了「{cardOnly.Id}」（{cardOnly.Display}）——"
                                + "它读的是**这张牌自己**的字段，只有**卡牌**的图上能用（遗物 / 药水 / 状态的图上 `this` 不是卡，生成出来编译不过）。"
                                + "想用「本回合 / 本场战斗造成的伤害」请改内置读数 DamageDealtThisTurn / DamageDealtThisCombat。"));
                        continue;
                    }
                    // 内置只读量（Turn / DiscardedThisTurn…）不用定义，直接可用
                    if (FormulaBuiltins.Find(id) is not null) continue;
                    if (!p.CustomParams.Any(x => x is not null && string.Equals(ParamNames.Normalize(x.Name), id, StringComparison.Ordinal)))
                        list.Add(("错误", $"【数值】的公式「{f}」用了参数 {id}=，但「自定义参数 / 组」页里没有这个参数"
                            + $"（内置只读量可以直接写：{FormulaBuiltins.Names}）。"));
                }
                continue;
            }
            if (n.Kind == "Relate")
            {
                // 【关系符】：运算符决定左右两边该接什么（比较类要数值，非 / 且 / 或要条件）。
                // 接错了生成端会退成「恒不成立」（不生成编译不过的代码），但用户得知道为什么，
                // 所以这里必须**报错**说清该接什么。
                string op = n.Get("Op", "Gt");
                string opZh = ForgeGraphCatalog.RelateOpZh(op);
                bool needsBool = ForgeGraphCatalog.RelateNeedsBool(op);
                string want = needsBool ? "条件（【条件】/【且】/【或】/另一个【关系符】）" : "数值（【状态】/【数值】/【能量】）";
                var ln = IncomingNode(g, n, 1);
                if (ln is null)
                    list.Add(("错误", $"【关系符】「{opZh}」左边的「左值」还没接线 —— 没接的话这一条恒为「不成立」。"));
                else if (IsBoolProducer(ln) != needsBool)
                    list.Add(("错误", $"【关系符】「{opZh}」的「左值」接的是「{(IsBoolProducer(ln) ? "条件" : "数值")}」，但这个运算符要的是{want}。"));
                if (!ForgeGraphCatalog.RelateIsUnary(op))
                {
                    var rn = IncomingNode(g, n, 2);
                    if (rn is not null && IsBoolProducer(rn) != needsBool)
                        list.Add(("错误", $"【关系符】「{opZh}」的「右值」接的是「{(IsBoolProducer(rn) ? "条件" : "数值")}」，但这个运算符要的是{want}。"));
                }
                // 结果没接到任何地方 = 这一块白算（用户实测踩过：只连了左边那条链、忘了把结果接回【if】）
                if (g.Outgoing(n.Id, 0).Count == 0)
                    list.Add(("警告", $"【关系符】「{opZh}」的「结果」右边**没接线** —— 它不影响任何判断。"
                        + "要让它起作用，把它的「结果」出口拖到【if】的「条件」口（或另一个【关系符】的左右值）上。"));
                continue;
            }
            if (n.Kind == "If")
            {
                // 【if】的「条件」口必须接一个能出布尔的模块。【判据和生成端一致】：BoolExpr 找不到 Bool 来源时
                // 取 fallback = false —— 也就是**永远走「否」那一支**，而且一声不吭。
                // 用户实测报过：「敌人身上明明有 4 层虚弱，打出去还是走了『我失去生命』」，
                // 根因就是条件口上挂的是【对象】（不是 Bool），生成出来是 `if (false)`。
                // 判据和生成端（BoolExpr）**完全一致**：在那个口上找「**来源是 Bool 产出者**」的那条线。
                int condPort = PortIndexOf(n, "Cond");
                var condLinks = g.Links.Where(l => l is not null
                    && string.Equals(l.To, n.Id, StringComparison.Ordinal) && l.ToPort == condPort).ToList();
                var condSrc = condLinks.Select(l => g.Node(l.From)).FirstOrDefault(IsBoolProducer);
                if (condSrc is null)
                {
                    bool wired = HasOut(g, n, "True") || HasOut(g, n, "False");
                    var other = condLinks.Select(l => g.Node(l.From)).FirstOrDefault(x => x is not null);
                    string got = other is null
                        ? "**没有接线**"
                        : $"接的是【{other.KindDef?.Display ?? other.Kind}】—— 它出的不是「成立 / 不成立」";
                    string fix = "把【条件】/【且】/【或】/【关系符】的**结果**出口拖到【if】左边的「条件」口上。"
                        + "要判断「某个敌人身上的某个状态大于 N」：①【状态】(那个状态) 的「数值」出口 → 【关系符】的「左值」；"
                        + "②【数值】(N) 的出口 → 【关系符】的「右值」；③【关系符】的「结果」出口 → 【if】的「条件」口；"
                        + "（【状态】左边的「归属」接【对象】= 那个敌人）";
                    list.Add((wired ? "错误" : "警告", $"【if】的「条件」口{got} —— 生成出来的条件是**恒为「不成立」**"
                        + (wired ? "，所以永远只会走「否」那一支。" : "（这一块的两个出口也都没接线，整块不做事）。") + fix));
                }
                continue;
            }
            if (n.Kind != "Effect") continue;
            string kind = n.Get("Effect", "Damage");
            string display = EffectCatalog.FindKind(kind).Display;

            // ① 这个种类在节点图里**根本没接**（选了等于白选，生成出来只有一行注释）——
            //    这是最难看懂的一类：生成、编译、进游戏全都不报错，就是没反应。
            if (!SupportedEffectKinds.Contains(kind))
            {
                list.Add(("错误", $"【效果】选的「{display}」**节点图里还没接上**（选了不会做任何事，只在生成的代码里留一行注释）。"
                    + "请把这一块换成节点图支持的种类，或者把这一项从锻造页移出去（不勾「锻造」之后）"
                    + "再到「卡牌 / 药水 / 遗物」页的「效果」列表里配 —— 那边支持全部 65 种。"
                    + $"节点图现在支持：{string.Join(" / ", SupportedEffectKinds.OrderBy(x => x, StringComparer.Ordinal).Select(k => EffectCatalog.FindKind(k).Display))}。"));
                continue;
            }
            // ② 这个种类只在**卡牌**那张布上有意义（V0.2.203）：
            //    「本回合这张牌自己费用变 0」= 这张牌自己的行为（SetToFreeThisTurn），
            //    状态 / 遗物 / 药水的布上没有「这张牌」→ 生成端会整块跳过，必须在这里说清。
            if (string.Equals(kind, "CheapenSelf", StringComparison.Ordinal)
                && !string.Equals(scope, "Card", StringComparison.Ordinal))
            {
                list.Add(("错误", $"【效果】选的「{display}」是**这张牌自己**的行为（把这张牌本回合的费用变成 0），"
                    + "只有「卡牌」的锻造图里有「这张牌」——状态 / 遗物 / 药水的布上没有，这一块不会生成任何代码。"
                    + "请把这一块删掉，或者到「卡牌」页的那张牌上配（那边的「效果」列表里有同一项）。"));
                continue;
            }
            // ③ 必填的东西没填（状态 / 目标卡）：也是「一行代码都不生成」，但原因完全不同
            if (NeedsPowerField.Contains(kind, StringComparer.Ordinal) && string.IsNullOrWhiteSpace(n.Get("PowerId")))
                list.Add(("错误", $"【效果】「{display}」还没选「状态」（模块上的「状态」那一栏）—— 没选的话这一块不会生成任何代码。"));
            // 「目标卡」只在「取卡方式 = 指定卡」那一档才是必填的：V0.2.170 起「按范围随机 / 多选1 /
            // 从范围里自己挑一张」用的是「范围限定」，不需要「目标卡」（以前这里一律要求，选随机也报错）。
            if (kind == "GenerateCard" && string.IsNullOrWhiteSpace(n.Get("SpawnCardId"))
                && !ForgeEffectBridge.Ensure(n).IsSpawnRandom)
                list.Add(("错误", $"【效果】「{display}」还没选「目标卡」（模块上的「目标卡」那一栏）—— 没选的话这一块不会生成任何代码；"
                    + "想按范围随机生成请把「取卡方式」改成「按范围随机」并选一个「范围限定」。"));
            // ④ 目标卡填了就必须**认得出**（我们自己的卡或本体卡）—— V0.2.188 起「指定卡」那一档
            //    生成的是具体类型名（`ModelDb.Card<…>()` / `CreateCard<…>()`），名字不存在 = 编译期 CS0246。
            //    ⚠ 这一条以前不需要：那时只在自己存档的卡里找目标卡，找不到就**静默**退化成
            //    「随机变化 / 整条效果丢掉」—— 用户实测报的「挖肉补疮：选的是本体卡『昆虫防御』，
            //    实际变成随机卡、『生成感染』整条没生成」就是这个静默退化。
            if (kind is "GenerateCard" or "TransformCard"
                && ProfileValidator.CardTargetProblem(p, $"【效果】「{display}」", n.Get("SpawnCardId")) is { } targetProblem)
                list.Add((targetProblem.Level, targetProblem.Message));
            // V0.2.221：「只对这张卡（指定）」也是卡牌类名（生成 `c.Id == ModelDb.Card<类名>().Id`），
            // 填了就认得出才行 —— 认不出 = 编译期 CS0234，整个模组装不上（用户报的
            // 「消耗丢弃升级指定卡没生效」顺着查出来的：图上这一栏以前连生成端都没读）。
            if (ForgeEffectBridge.Ensure(n).HasScopeCardId
                && ProfileValidator.CardTargetProblem(p, $"【效果】「{display}」",
                    ForgeEffectBridge.Ensure(n).ScopeCardId, fieldZh: "只对这张卡（指定）") is { } scopeCardProblem)
                list.Add((scopeCardProblem.Level, scopeCardProblem.Message));
            // ③ V0.2.129：编辑器合并之后，卡牌页有的栏位【效果】模块上都有 ——
            //    但节点图的发射器只覆盖其中一部分。**填了不生效的栏位一律报错**（不许静默生成一半）。
            foreach (var (level, msg) in UnsupportedEffectSettings(g, n, scope))
                list.Add((level, $"【效果】「{display}」的{msg}"));
        }
        // 连线指向的模块必须还在（删模块之后残留的线）
        foreach (var l in g.Links)        {
            if (l is null) continue;
            if (g.Node(l.From) is null || g.Node(l.To) is null)
                list.Add(("提示", "有一条连线指向已经被删掉的模块（保存时会自动清掉）。"));
        }

        // 「指定单体敌人」必须真的有「选中的那个敌人」：
        //   图里用到它（【对象】/【指向对象】选了「指定单体敌人」），但这张牌的目标类型不是 AnyEnemy
        //   → 打出这张牌时玩家根本没选过敌人，cardPlay.Target 是 null，那一支拿到的是空目标（读状态得 0）。
        // 这是「静默算成 0」那一类里最难查的（和「数值公式」在全体牌上用『指定敌人』参数是同一个坑），
        // 所以直接**报错**并告诉用户「加一个【指向对象】选『指定单体敌人』」。
        if (string.Equals(scope, "Card", StringComparison.Ordinal) && cardTargetType is not null
            && !string.Equals(cardTargetType, "AnyEnemy", StringComparison.Ordinal))
        {
            var picker = g.Nodes.FirstOrDefault(x => x is not null
                && x.Kind is "Unit" or "Target" && PickNeedsChosenEnemy(x.Get("Pick", "Self")));
            if (picker is not null)
                list.Add(("错误", $"图里用了「指定单体敌人」（{(picker.Kind == "Target" ? "【指向对象】" : "【对象】")}那一块），"
                    + $"但这张牌的目标类型是「{cardTargetType}」—— 打出这张牌时**不会让玩家选敌人**，"
                    + "所以「指定单体敌人」拿到的是**空目标**（读它的状态得 0、打它等于没打）。"
                    + "请加一个【指向对象】模块（就在【触发时机】后面）并把「选中谁」选成「指定单体敌人」；"
                    + "或者把那一块改成「全部敌人 / 随机敌人」这种不需要选目标的档。"));
        }
        return list;
    }

    private static string ScopeZh(string scope) => scope switch
    {
        "Card" => "卡牌",
        "Potion" => "药水",
        "Relic" => "遗物",
        // V0.2.194：锻造页「状态列表」那一类布
        ForgeTimings.PowerScope => "状态",
        _ => scope,
    };

    /// <summary>
    /// 「升级前 vs 升级后」两块布的差异说明（界面上那一行提示用）。
    ///
    /// 需求：升级后那张布非空时，升级后的卡走它、空着就用升级前那张 —— 但用户看不出**哪几块不一样**，
    /// 所以这里按「模块的种类 + 它的设置」比一遍，列出只在某一边出现的块，以及设置不同的块。
    /// 位置（X/Y）**不算差异** —— 挪一下位置不该被说成「升级后改了」。
    /// </summary>
    public static string UpgradeDiff(CardSpec card)
    {
        var baseGraph = card.ForgeGraph;
        var upGraph = card.UpgradedForgeGraph;
        if (upGraph is null || upGraph.IsEmpty)
            return "「升级后」那块布是空的 → 升级后的卡**跟升级前一模一样**（走升级前那张布）。";
        if (baseGraph is null || baseGraph.IsEmpty)
            return "「升级前」是空的、「升级后」有内容 → 只有升级后的卡会做事。";

        // 每一块按「种类 + 设置」算一个可比对的签名（位置不比）
        static string Sig(ForgeNodeSpec n)
        {
            var keys = n.Fields.Keys.OrderBy(k => k, StringComparer.Ordinal);
            return n.Kind + "{" + string.Join(",", keys.Select(k => k + "=" + n.Get(k))) + "}";
        }
        var baseSigs = baseGraph.Nodes.Where(n => n is not null).Select(Sig).ToList();
        var upSigs = upGraph.Nodes.Where(n => n is not null).Select(Sig).ToList();

        int same = baseSigs.Count(s => upSigs.Contains(s));
        int onlyBase = baseSigs.Count - same;
        int onlyUp = upSigs.Count - same;
        int changed = Math.Min(onlyBase, onlyUp);          // 大致「改了设置的块」数
        int added = Math.Max(0, onlyUp - changed);
        int removed = Math.Max(0, onlyBase - changed);

        var parts = new List<string>();
        parts.Add($"升级前 {baseSigs.Count} 块 / 升级后 {upSigs.Count} 块");
        if (changed > 0) parts.Add($"约 {changed} 块改了设置");
        if (added > 0) parts.Add($"升级后多了 {added} 块");
        if (removed > 0) parts.Add($"升级后少了 {removed} 块");
        if (changed == 0 && added == 0 && removed == 0) parts.Add("两块布内容一致（只是位置可能不同）");
        return string.Join("，", parts) + " —— 升级后的卡走「升级后」那张布。";
    }

    /// <summary>
    /// 新建条目时把那块布建好（卡牌：正中间一个【触发时机】+ 左上角一个【能量】）。
    /// 另外按用户要求：**【触发时机】后面固定连着一个【指向对象】**（声明这张牌在游戏里选中谁），
    /// 于是新布的接线从「触发时机 → (空)」变成「触发时机 → 指向对象 → (空)」。
    /// 老存档的图不动（没有那个模块 = 行为和以前逐字一致，直到用户自己加上）。
    /// </summary>
    public static void EnsureGraph(CardSpec card)
    {
        card.ForgeGraph ??= ForgeGraphSpec.NewCardGraph();
        if (card.ForgeGraph.Nodes.All(n => n is null || n.Kind != "Target"))
            AddTargetAfterTrigger(card.ForgeGraph);
    }

    public static void EnsureGraph(PotionSpec potion)
    {
        // 药水不自动加【指向对象】：药水的目标由「药水」页那个「作用目标」决定（不是卡牌那套 TargetType），
        // 加了反而多一块没用的模块（需要的话可以从调色板里自己拖一个当【对象】用）。
        potion.ForgeGraph ??= ForgeGraphSpec.NewWithTiming(ForgeTimings.PotionUsed);
    }

    public static void EnsureGraph(RelicSpec relic)
    {
        relic.ForgeGraph ??= ForgeGraphSpec.NewWithTiming(
            relic.Trigger == "PlayerTurnStart" ? ForgeTimings.PlayerTurnStart : ForgeTimings.CombatStart);
    }

    /// <summary>
    /// 在【触发时机】后面补一个【指向对象】，并接好「触发时机 → 指向对象 → 原来接在触发时机上的那一块」。
    /// 位置放在触发时机右边一点；原来的下游整个接在它后面（顺序不变）。
    ///
    /// 默认值是「**指定单体敌人**」：这样「效果没自己接【对象】」时的默认目标 = <c>__fgTarget</c>
    /// （老图的老规矩就是这个），一个字都不变；同时它把卡牌目标类型声明成「会选一个敌人」，
    /// 于是「指定单体敌人」这类用法第一次真的拿得到玩家选中的那个敌人。
    /// 做的是**自增益牌**（打给自己）的话，把它改成「自己」就行（下面那些效果也就默认打自己了）。
    /// </summary>
    public static ForgeNodeSpec AddTargetAfterTrigger(ForgeGraphSpec g)
    {
        var node = new ForgeNodeSpec
        {
            Kind = "Target",
            X = ForgeGraphCatalog.CanvasCenterX + ForgeGraphCatalog.NodeWidth + 40,
            Y = ForgeGraphCatalog.CanvasCenterY,
            Fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["Pick"] = "ChosenEnemy" },
        };
        g.Nodes.Add(node);

        var trigger = g.TriggerNode;
        if (trigger is null) return node;
        // 触发时机原来接的那条线：改成从【指向对象】出去（顺序不变），触发时机改接【指向对象】
        foreach (var l in g.Outgoing(trigger.Id, 0).ToList())
        {
            g.Links.Remove(l);
            g.Links.Add(new ForgeLinkSpec { From = node.Id, To = l.To, FromPort = 0, ToPort = l.ToPort });
        }
        g.Links.Add(new ForgeLinkSpec { From = trigger.Id, To = node.Id, FromPort = 0, ToPort = 0 });
        return node;
    }

    // ==================================================================================
    //  生成出来的模组里的运行时辅助
    // ==================================================================================

    /// <summary>把生成出来的图里那些「运行时才知道的值」收在一个小类里（公式只调它）。</summary>
    public static string RuntimeSource(CharacterProfile p)
    {
        string n = Naming.From(p).Namespace;
        // $$""" 里 {{...}} 是插值；减益清单是生成期从效果库写死的（运行期不反射）
        string debuffs = DebuffNameLines();
        return $$"""
// <auto-generated>由 Sts2CharForge 生成：锻造节点图的运行时辅助。</auto-generated>
using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace {{n}};

/// <summary>
/// 锻造节点图的运行时辅助。
///
/// 为什么单独一个类：公式（=M*3、X、Y…）要能在**任何执行点**求值
/// （卡牌被打出 / 遗物被点击 / 血量低于 N…），而那些执行点的上下文各不相同；
/// 统一收在这里，生成的代码就只剩一句调用。
/// </summary>
public static class ForgeFormulaRuntime
{
    /// <summary>除法：分母为 0 时返回 0（生成器不会生成会崩的代码）。</summary>
    public static decimal Div(decimal a, decimal b) => b == 0m ? 0m : a / b;

    /// <summary>取余：同上，分母为 0 时返回 0。</summary>
    public static decimal Mod(decimal a, decimal b) => b == 0m ? 0m : a % b;

    /// <summary>当前能量。</summary>
    public static decimal Energy(Player? player) => player?.PlayerCombatState?.Energy ?? 0;

    /// <summary>当前额外资源量（本体的星星计数器）。</summary>
    public static decimal Stars(Player? player) => player?.PlayerCombatState?.Stars ?? 0;

    /// <summary>某个状态在目标身上的层数（目标为 null 时算 0）。</summary>
    public static decimal PowerAmount(Creature? creature, string powerId)
    {
        if (creature is null || string.IsNullOrWhiteSpace(powerId)) return 0m;
        foreach (var power in creature.Powers)
        {
            if (power is null) continue;
            if (string.Equals(power.GetType().Name, powerId, StringComparison.Ordinal)) return power.Amount;
        }
        return 0m;
    }

    /// <summary>
    /// V0.2.192：【状态列表】模块 —— 某个单位身上**符合条件**的状态有几个（<paramref name="filter"/> 见下面 MatchesPower）。
    /// 用 `p.GetType().Name` 和状态类名比（和 <see cref="PowerAmount"/> 同一套口径，本体的 Id.Entry 是 slug 化的）。
    /// </summary>
    public static decimal PowerCount(Creature? creature, string filter, string? powerId)
    {
        if (creature is null) return 0m;
        decimal n = 0m;
        foreach (var power in creature.Powers)
            if (power is not null && MatchesPower(power, filter, powerId)) n += 1m;
        return n;
    }

    /// <summary>V0.2.192：【状态列表】模块 —— 符合条件那些状态的**层数合计**。</summary>
    public static decimal PowerLayers(Creature? creature, string filter, string? powerId)
    {
        if (creature is null) return 0m;
        decimal sum = 0m;
        foreach (var power in creature.Powers)
            if (power is not null && MatchesPower(power, filter, powerId)) sum += power.Amount;
        return sum;
    }

    /// <summary>【状态列表】的筛选：Any = 全部 / Buff = 只看增益 / Debuff = 只看减益 / One = 只看指定状态（按类名）。</summary>
    private static bool MatchesPower(PowerModel power, string filter, string? powerId)
    {
        if (string.Equals(filter, "Buff", StringComparison.Ordinal))
            return power.TypeForCurrentAmount == PowerType.Buff;
        if (string.Equals(filter, "Debuff", StringComparison.Ordinal))
            return power.TypeForCurrentAmount == PowerType.Debuff;
        if (string.Equals(filter, "One", StringComparison.Ordinal))
            return !string.IsNullOrWhiteSpace(powerId)
                && string.Equals(power.GetType().Name, powerId, StringComparison.Ordinal);
        return true;
    }

    /// <summary>活着的一个敌人（没有就返回 null）。</summary>
    public static Creature? FirstEnemy(Player? player)
    {
        var st = player?.Creature?.CombatState;
        return st?.HittableEnemies?.FirstOrDefault(c => c is not null && c.IsAlive);
    }

    /// <summary>随机一个活着的敌人（allowDuplicates 只在多段命中时有意义，这里总是随机取一个）。</summary>
    public static Creature? RandomEnemy(Player? player, bool allowDuplicates)
    {
        var st = player?.Creature?.CombatState;
        var list = st?.HittableEnemies?.Where(c => c is not null && c.IsAlive).ToList();
        if (list is null || list.Count == 0) return null;
        var rng = player?.RunState?.Rng?.CombatCardSelection;
        return rng is null ? list[0] : rng.NextItem(list);
    }

    /// <summary>第一只召唤物（没有就返回 null）。</summary>
    public static Creature? FirstSummon(Player? player) =>
        player?.PlayerCombatState?.Pets?.FirstOrDefault(c => c is not null && c.IsAlive);

    /// <summary>
    /// 指定名称的那只召唤物（需求：【对象】选「指定召唤物」后按下面选的名称取那一只）。
    /// 名字用的是生成出来的**英文类名**（<c>SummonSpec.ClassName</c>）；拿模型 ID 比，
    /// 比对不上就退回第一只（和「随机召唤物」一个兜底，免得整支效果什么都不做）。
    /// </summary>
    public static Creature? NamedSummon(Player? player, string? className)
    {
        string want = (className ?? "").Trim();
        if (want.Length == 0) return FirstSummon(player);
        var pets = player?.PlayerCombatState?.Pets;
        if (pets is null) return null;
        foreach (var c in pets)
            if (c is not null && c.IsAlive && string.Equals(c.ModelId.Entry, want, StringComparison.Ordinal)) return c;
        return FirstSummon(player);
    }

    /// <summary>全部召唤物（活的）。</summary>
    public static List<Creature> SummonList(Player? player) =>
        player?.PlayerCombatState?.Pets?.Where(c => c is not null && c.IsAlive).ToList() ?? new List<Creature>();

    /// <summary>全部友方（自己 + 召唤物）。</summary>
    public static List<Creature> AllAllies(Player? player)
    {
        var list = new List<Creature>();
        if (player?.Creature is not null) list.Add(player.Creature);
        list.AddRange(SummonList(player));
        return list;
    }

    /// <summary>全部可以打的敌人。</summary>
    public static List<Creature> EnemyList(Player? player) =>
        player?.Creature?.CombatState?.HittableEnemies?.Where(c => c is not null && c.IsAlive).ToList()
        ?? new List<Creature>();

    /// <summary>
    /// 「打出时选中的那个目标」优先，没选 / 已经死了就退回首個能打的敌人。
    /// 卡牌上「指定单体敌人」指的就是前者（就是玩家打这张牌时点的那个人）。
    /// </summary>
    public static List<Creature> TargetOrFirst(Creature? target, Player? player)
    {
        var list = new List<Creature>();
        if (target is not null && target.IsAlive) { list.Add(target); return list; }
        var first = FirstEnemy(player);
        if (first is not null) list.Add(first);
        return list;
    }

    /// <summary>全体单位（自己 + 召唤物 + 敌人）。</summary>
    public static List<Creature> AllUnits(Player? player)
    {
        var list = AllAllies(player);
        list.AddRange(EnemyList(player));
        return list;
    }

    /// <summary>「概率生效」用的判定：percent ≥ 100 一律成立。</summary>
    public static bool Roll(Player? player, int percent)
    {
        if (percent >= 100) return true;
        if (percent <= 0) return false;
        var rng = player?.RunState?.Rng?.CombatCardSelection;
        return rng is null || rng.NextInt(100) < percent;
    }

    /// <summary>
    /// 清掉目标身上所有减益。
    /// 本体没有公开的「哪些 Power 算减益」查询（工具自己的效果库里有 <c>Type = Debuff</c>），
    /// 所以这里用**生成期就知道的那份清单**逐个判断类名 —— 不用反射、也不用猜。
    /// </summary>
    public static async Task ClearDebuffs(Creature? creature)
    {
        if (creature is null) return;
        foreach (var power in creature.Powers.ToList())
        {
            if (power is null) continue;
            if (DebuffPowerNames.Contains(power.GetType().Name)) await PowerCmd.Remove(power);
        }
    }

    /// <summary>工具的效果库里标了「减益」的那批状态类名（生成期写死，运行期只查名字）。</summary>
    public static readonly HashSet<string> DebuffPowerNames = new(StringComparer.Ordinal)
    {
{{debuffs}}
    };
}
""";
    }

    /// <summary>DebuffPowerNames 的内容（生成期按效果库写死，不用运行时反射）。</summary>
    private static string DebuffNameLines() =>
        string.Join("\n", EffectCatalog.Debuffs.Select(d => "        \"" + d.Id + "\","));

    /// <summary>
    /// 「当遗物被点击时」的 Harmony 补丁：点击是**界面节点**（<c>NRelic</c>）上的回调
    /// <c>OnRelicClicked</c>，<c>RelicModel</c> 上没有对应虚方法，所以只能补在节点上。
    /// 只认带这条时机的锻造遗物，其它遗物原样放行（补丁非常窄）。
    /// </summary>
    public static string RelicClickPatchSource(CharacterProfile p)
    {
        var n = Naming.From(p);
        var hits = new List<string>();
        for (int i = 0; i < p.Relics.Count; i++)
        {
            var r = p.Relics[i];
            var plan = Plan(r?.ForgeGraph, "Relic");
            if (plan is null || !string.Equals(plan.Timing.Id, ForgeTimings.RelicClicked, StringComparison.Ordinal)) continue;
            hits.Add(n.RelicClassName(r!, i));
        }
        if (hits.Count == 0) return "";

        var w = new CodeWriter();
        w.Line("// <auto-generated> 锻造节点图：「当遗物被点击时」的补丁 </auto-generated>");
        w.Line("using System.Threading.Tasks;")
         .Line("using MegaCrit.Sts2.Core.Models;")
         .Line("using MegaCrit.Sts2.Core.Nodes.Relics;")
         .Line("using HarmonyLib;")
         .Line();
        w.Line($"namespace {n.Namespace};")
         .Line();
        w.Line("/// <summary>");
        w.Line("/// 把「遗物图标被点一下」转发给遗物自己的 ForgeOnRelicClicked()。");
        w.Line("/// 本体只给界面节点留了点击回调（RelicModel 上没有对应的虚方法），所以只能补在这里；");
        w.Line("/// 补丁只认下面这几个我们自己生成的遗物类，其它遗物原样放行。");
        w.Line("/// </summary>");
        // 遗物栏（战斗中）
        EmitClickPatch(w, "NRelicInventory", hits);
        w.Line();
        // 遗物栏（其它界面用的持有者节点）
        EmitClickPatch(w, "NRelicBasicHolder", hits);
        return w.ToString();
    }

    private static void EmitClickPatch(CodeWriter w, string nodeType, List<string> hits)
    {
        w.Line($"[HarmonyPatch(typeof({nodeType}), \"OnRelicClicked\")]")
         .Open($"public static class {RelicClickPatchClass}_{nodeType}")
         .Line("/// <summary>点击之后：如果被点的是我们的锻造遗物，就把它的节点图跑一遍。</summary>")
         .Line("private static void Postfix(RelicModel __0)")
         .Line("{")
         .Indent();
        // ⚠ 变量名必须每一行都不同：两个「当遗物被点击时」的遗物会生成两行
        //   `if (__0 is A __fgRelic) … if (__0 is B __fgRelic) …` → CS0128（同一个作用域里重复定义），
        //   整个模组编译不过（探针真编译时踩到）。
        for (int i = 0; i < hits.Count; i++)
            w.Line($"if (__0 is {hits[i]} __fgRelic{i}) _ = __fgRelic{i}.ForgeOnRelicClicked();");
        w.Dedent().Line("}");
        w.Close();
    }
}

