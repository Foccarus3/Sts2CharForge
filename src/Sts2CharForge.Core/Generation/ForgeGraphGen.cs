using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
    private sealed record EmitCtx(string Scope, string CardPlayExpr, string CardSelfExpr, bool Upgraded = false,
        string CtxFrom = "Param", bool AllowCardReward = false, bool HasRoom = false)
    {
        /// <summary>效果代码里要传的那个选择上下文：没有上下文（None）时写 <c>null!</c>（运行时本体自己会拦）。</summary>
        public string ChoiceVar => CtxFrom == "None" ? "null!" : "choiceContext";

        /// <summary>要不要在这个方法体里自己造一个 choiceContext（Blocking / Hook）。</summary>
        public bool NeedsCtxBootstrap => CtxFrom is "Blocking" or "Hook";
    }

    /// <summary>这个执行点的钩子签名里是不是带着 <c>CombatRoom room</c>（只有「战斗胜利时」）。</summary>
    private static bool HasRoomHook(EmitCtx ctx) => ctx.HasRoom;

    /// <summary>一次代码生成的状态（递归时共用：局部变量别重复声明、别顺着环无限递归）。</summary>
    private sealed class GenState
    {
        public readonly HashSet<string> ValueEmitted = new(StringComparer.Ordinal);   // 已经算过的【数值】/【状态】/【能量】
        public readonly HashSet<string> NumEmitted = new(StringComparer.Ordinal);     // 已经声明过的「数字栏公式」局部变量
        public readonly HashSet<string> Busy = new(StringComparer.Ordinal);           // 正在算的（防环）
        public readonly HashSet<string> Chained = new(StringComparer.Ordinal);        // 已经作为执行链走过的模块
        public int Depth;

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
        || p.Relics.Any(x => x is not null && UsesGraph(x));

    /// <summary>
    /// 把一张图整理成「触发时机 + 它的执行链入口」。返回 null = 这张图是空的（什么都不生成）。
    ///
    /// <paramref name="scope"/> = Card / Potion / Relic：**必须传**，因为合并菜单之后
    /// <c>CardPlayed / CardExhausted / CardDrawn / CardDiscarded</c> 四条在卡牌档和遗物档里
    /// 同名（这是「同一套菜单」的本意），不按 scope 查就会把卡牌那条（Hook = OnPlay）当成遗物图的时机。
    /// </summary>
    private static HookPlan? Plan(ForgeGraphSpec? g, string scope)
    {
        if (g is null || g.IsEmpty) return null;
        var trigger = g.TriggerNode;
        if (trigger is null) return null;
        var timing = ForgeTimings.Find(trigger.Get("Timing", ForgeTimings.CardPlayed), scope)
                     ?? ForgeTimings.Find(ForgeTimings.CardPlayed, scope)
                     ?? ForgeTimings.Find(ForgeTimings.CardPlayed)!;
        return new HookPlan(trigger, timing);
    }

    /// <summary>一条锻造条目属于哪一类布（查时机要用它）。</summary>
    private static string ScopeOf(object owner) => owner switch
    {
        CardSpec => "Card",
        RelicSpec => "Relic",
        PotionSpec => "Potion",
        _ => "Card",
    };

    // ==================================================================================
    //  对外入口：卡牌 / 药水 / 遗物的钩子代码
    // ==================================================================================

    /// <summary>卡牌的钩子代码（OnPlay 那一支会生成 <c>ForgeGraphOnPlay</c>，由 OnPlay 末尾调用）。</summary>
    public static string CardHooks(CharacterProfile p, CardSpec card, bool isUpgraded)
    {
        var g = GraphOf(card, isUpgraded);
        var plan = Plan(g, "Card");
        if (plan is null || g is null) return "";
        // 【触发时机】= 卡牌被打出时：不能自己 override OnPlay（会盖掉用户手填的那套效果），
        // 改成生成一个私有方法，由 OnPlay 末尾调用它。
        // **升级前 / 升级后两块布各用一个方法名** —— 两块布都设成「打出时」时，
        // 用同一个名字会生成两个签名一样的方法（CS0111，整个模组编译不过）。
        string? method = plan.Timing.Hook == "OnPlay" ? PlayMethodName(isUpgraded) : null;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"    // ===== 锻造节点图（{(isUpgraded ? "升级后" : "升级前")}）：{plan.Timing.Display} =====");
        EmitHook(sb, p, g, plan, new EmitCtx("Card", "cardPlay", "this", isUpgraded), method);        return sb.ToString();
    }

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

    /// <summary>某一块布上有没有「卡牌被打出时」这一支。</summary>
    public static bool HasPlayTimingIn(ForgeGraphSpec? g)
    {
        var plan = Plan(g, "Card");
        return plan is not null && plan.Timing.Hook == "OnPlay";
    }

    /// <summary>
    /// 这瓶药的节点图里有没有「药水被使用时」这一支。
    ///
    /// 有的话生成端**不能再写那个空的 <c>OnUse</c> 骨架** —— 两边会生成两个签名一样的 override，
    /// 直接 CS0111（用户实测的构建日志：<c>cs\Potions\SparklePotion8.cs(21,35): error CS0111</c>）。
    /// 和卡牌那边 <see cref="HasPlayTiming"/> 是同一个坑、同一套处理。
    /// </summary>
    public static bool HasUseTiming(PotionSpec potion) => HasUseTimingIn(potion.ForgeGraph);

    /// <summary>某一块布上有没有「药水被使用时」这一支。</summary>
    public static bool HasUseTimingIn(ForgeGraphSpec? g)
    {
        var plan = Plan(g, "Potion");
        return plan is not null && plan.Timing.Hook == "Use";
    }

    /// <summary>药水的钩子代码。</summary>
    public static string PotionHooks(CharacterProfile p, PotionSpec potion)
    {
        var g = potion.ForgeGraph;
        var plan = Plan(g, "Potion");
        if (plan is null || g is null) return "";
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"    // ===== 锻造节点图：{plan.Timing.Display} =====");
        EmitHook(sb, p, g, plan, new EmitCtx("Potion", "null", "null"));
        return sb.ToString();
    }

    /// <summary>
    /// 这只遗物的节点图会不会被**嵌进它自己的触发钩子**里（而不是另加一个 override）。
    /// 判据：图的【触发时机】和遗物的「触发时机」下拉是同一个钩子 —— 那时两边会生成两个签名
    /// 完全一样的 override，编译直接 CS0111。
    /// </summary>
    public static bool RelicGraphNestsInTrigger(RelicSpec relic, string relicTriggerId)
    {
        var plan = Plan(relic.ForgeGraph, "Relic");
        if (plan is null) return false;
        string graphHook = plan.Timing.Hook;
        // 遗物触发时机的钩子名从**签名那一份**推（CSharpCodeGen.RelicTriggerSignature 是唯一事实来源）——
        // 免得这里再抄一张「时机 → 钩子」的表，加了新时机忘了同步就会一个类里出两个同名 override（CS0111）。
        string relicHook = CSharpCodeGen.RelicHookNameOf(relicTriggerId);
        return relicHook.Length > 0 && string.Equals(graphHook, relicHook, StringComparison.Ordinal);
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
        var g = relic.ForgeGraph;
        var plan = Plan(g, "Relic");
        if (plan is null || g is null) return "";
        // 「当遗物被点击时」不是本体的虚方法：生成一个普通方法，由补丁按类型转发；
        // 图里的时机和遗物触发时机是同一个钩子时，也生成一个普通方法（由那个钩子调用）。
        bool nest = RelicGraphNestsInTrigger(relic, relic.Trigger);
        string? method = nest ? CSharpCodeGen.ForgeRelicGraphMethod
            : string.Equals(plan.Timing.Hook, "ForgeOnRelicClicked", StringComparison.Ordinal) ? "ForgeOnRelicClicked"
            : null;
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"    // ===== 锻造节点图：{plan.Timing.Display} =====");
        EmitHook(sb, p, g, plan, new EmitCtx("Relic", "null", "null", CtxFrom: RelicCtxFrom(plan, nest)), method);
        return sb.ToString();
    }

    /// <summary>
    /// 「生效次数」要用的字段 / 复位钩子（只在这个执行点真的需要时才生成）。
    /// 返回空串 = 不需要（默认档一个字符都不多）。
    /// </summary>
    public static string TimingFields(CharacterProfile p, object owner, bool upgraded = false)
    {
        var g = GraphOf(owner, upgraded);
        var plan = Plan(g, ScopeOf(owner));
        if (plan is null) return "";

        var trig = plan.Trigger;
        var lim = ForgeNumbers.Resolve(p, trig, "CountLimit", 0);
        // 「仅生效 N 次」那一栏现在也能填公式（V0.2.128）：
        //   · 填 0 = 无限次（老行为）：计数字段 / 复位钩子都不生成；
        //   · 常数（含 2*3 这种折叠成常数的公式）：老行为；
        //   · 运行时公式：**按「有限次」生成**（算出来是多少就是多少；算出来是 0 / 负数时
        //     那一支一次都不会跑 —— 校验器会给一条提示）。
        bool runtimeLimit = lim.Runtime;
        int limit = runtimeLimit ? 0 : (int)Math.Round((double)lim.Value);
        if (!runtimeLimit && limit <= 0) return "";

        string uses = "__fgUses" + (upgraded ? "Up" : "");
        var sb = new StringBuilder();
        string scope = trig.Get("CountScope", "Combat");
        string comment = runtimeLimit
            ? $"生效次数按公式算（{lim.Text}）"
            : ForgeTimings.CountZh(limit, scope);

        sb.AppendLine();
        // 用 plan 里那条时机自己的中文名（同名时机在卡牌档 / 遗物档显示名不一样，别用全局 Find 猜）
        sb.AppendLine($"    // ===== 锻造节点图【触发时机】{plan.Timing.Display}：{comment} =====");
        sb.AppendLine($"    private int {uses};");

        // 作用域复位：本回合 / 本场战斗开始时清零（「本次游玩」不清零 —— 那正是它的意思）
        if (!string.Equals(scope, "Run", StringComparison.Ordinal))
        {
            bool turn = string.Equals(scope, "Turn", StringComparison.Ordinal);
            // 「本回合」用 AfterPlayerTurnStart 清零；「本场战斗」用 AfterCombatVictory 清零
            // （这一场打完之后重置，下一场就是满次数；比用 BeforeSideTurnStart 干净 ——
            //   那个钩子会被「战斗开始时」这个触发时机用掉，同一个类里写两个会 CS0111 重复定义）。
            string hook = turn ? "AfterPlayerTurnStart" : "AfterCombatVictory";
            string resetSig = turn
                ? "public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)"
                : "public override async Task AfterCombatVictory(CombatRoom room)";
            // 主钩子如果正好是同一个（比如触发时机就是「战斗胜利时」），就不再写一遍
            bool clash = plan.Timing.Hook == hook;
            if (!clash)
            {
                sb.AppendLine();
                sb.AppendLine($"    /// <summary>「{(turn ? "本回合" : "本场战斗")}仅生效 N 次」：{(turn ? "每回合" : "每场战斗结束后")}清零。</summary>");
                sb.AppendLine($"    {resetSig}");
                sb.AppendLine("    {");
                sb.AppendLine($"        {uses} = 0;");
                sb.AppendLine("        await Task.CompletedTask;");
                sb.AppendLine("    }");
            }
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
            return ctx.Scope == "Card"
                ? $"public async Task {methodName}(PlayerChoiceContext choiceContext, CardPlay cardPlay)"
                : HasRoomHook(ctx) ? $"public async Task {methodName}(CombatRoom room)"
                : $"public async Task {methodName}()";
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
            "AfterSideTurnEnd" => "public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)",
            "AfterCombatVictory" => "public override async Task AfterCombatVictory(CombatRoom room)",
            "AfterDamageReceived" => "public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)",
            "AfterAttack" => "public override async Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command)",
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
        EmitCtx ctx, string? methodName = null)
    {
        // 【卡牌奖励】只能挂在「战斗胜利时」那一支上（那一支的钩子签名里带 CombatRoom room）
        var (_, hasCardReward) = WalkChain(g, plan.Trigger);
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
        // 方法体起点：卡牌档挂在**没有 cardPlay 参数**的钩子上时（被消耗 / 被丢弃 / 被抽到 / 被保留），
        // 最后要按「生成出来的文本有没有引用 cardPlay」决定往这里插一句 `CardPlay? cardPlay = null;`
        //（和 CSharpCodeGen.EmitCardPlayPlaceholder 同一套做法，见那里的说明）。插回去而不是重写一遍，
        // 是为了让默认档（有 cardPlay 的那些时机）的输出**一个字符都不变**。
        int bodyMark = sb.Length;
        bool hookHasPlay = HookHasCardPlay(plan.Timing);

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
                         amountField.Runtime ? amountField.Expr : null))
                sb.AppendLine("        " + line);
        }

        // ---- 0.05) 时机自己的进门守卫（V0.2.128 新增「这张牌被保留时」）----
        // 「被保留」不是本体的虚方法（本体没有 AfterCardRetained），挂在 AfterFlush 上，
        // 所以方法体最前面要先判「这张牌真的被保留了」——判据和卡牌页那份逐字相同
        //（CSharpCodeGen.EmitCardTimingHooks 的 case "Retained"：retainedCards.Contains(this)）。
        foreach (string line in TimingEntryGuardLines(plan.Timing))
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

        // ---- 1) 先算「自己 / 目标 / 参数 / X / Y」----
        sb.AppendLine("        // ---- 锻造节点图：执行点先算好这几个值（后面的公式只引用它们）----");
        sb.AppendLine("        Player __fgPlayer = base.Owner;");
        sb.AppendLine("        Creature __fgSelf = __fgPlayer.Creature;");
        sb.AppendLine(ctx.Scope == "Card"
            // 卡牌那几个**没有 cardPlay 参数**的钩子（被消耗 / 被丢弃 / 被抽到 / 被保留）：
            // 用 ?. 取目标（那一档下面会插一句 `CardPlay? cardPlay = null;`），
            // 不然生成出来是 CS8602（对可能为 null 的引用解引用）。
            ? (hookHasPlay ? "        Creature? __fgTarget = cardPlay.Target;" : "        Creature? __fgTarget = cardPlay?.Target;")
            : "        Creature? __fgTarget = null;");
        // 没有 choiceContext 的时机（「获得时」「遗物被点击」以及造不出来的那几条）：效果里不引用那个名字，
        // 需要它的那几种效果会传 null!（本体自己在运行时会拦下并报错，比默默不做事清楚）——
        // 校验器对「造不出来 + 图里有需要 context 的效果」这一组合会**报错**拦住，不静默生成坏代码。
        if (string.Equals(ctx.CtxFrom, "None", StringComparison.Ordinal))
        {
            sb.AppendLine("        // 注意：这个执行点的钩子本体没给 choiceContext，也造不出来 —— 需要它的效果会传 null!");
        }

        foreach (var param in UsedParams(p, g))
        {
            string name = ParamNames.Normalize(param.Name);
            sb.AppendLine($"        decimal {ForgeFormula.ParamVar(name)} = {ForgeFormula.ParamExpr(param)};   // {name}= {param.Summary}");
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
        if (energyCost <= 0 && resourceCost <= 0)
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

        // 卡牌档挂在**没有 cardPlay 参数**的钩子上、而且生成出来的代码真的用到了它（DamageCmd 的 FromCard /
        // CreatureCmd.GainBlock 的来源卡参数…）：补一句 `CardPlay? cardPlay = null;`
        //（本体那几条 API 允许传 null；真会解引用 cardPlay.Target 的用法校验器会拦，见 ProfileValidator）。
        // 判据是**生成出来的文本**，不是「按效果种类猜」——猜错会平白多一个用不上的局部变量（CS0219）。
        if (methodName is null && string.Equals(ctx.Scope, "Card", StringComparison.Ordinal) && !hookHasPlay
            && sb.ToString(bodyMark, sb.Length - bodyMark).Contains("cardPlay", StringComparison.Ordinal))
        {
            var ins = new StringBuilder();
            ins.AppendLine("        // 这些钩子里没有「打出记录」（cardPlay）：下面把它传 null（那几条本体 API 允许传 null），");
            ins.AppendLine("        // 用 ?. 取「打出的目标」；真会解引用 cardPlay.Target 的用法由校验器拦住。");
            ins.AppendLine("        CardPlay? cardPlay = null;");
            sb.Insert(bodyMark, ins.ToString());
        }

        sb.AppendLine("    }");
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
    /// </summary>
    private static IReadOnlyList<string> TimingEntryGuardLines(ForgeTimings.TimingOption t) => t.Hook switch
    {
        "AfterFlush" => new[]
        {
            "// 「这张牌被保留时」：本体没有 AfterCardRetained 钩子，用 AfterFlush 交出来的 retainedCards 判自己",
            "//（判据 = 本体 CombatManager.FlushPlayerHand 把这张牌归进 retainedCards：有保留关键词 / 这回合被单回合保留 / 整手牌都不丢）",
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
                EmitPortChain(sb, p, g, node, hasTrue ? "True" : "False", ctx, st, indent + "    ");
                sb.AppendLine(indent + "}");
                if (hasTrue && hasFalse)
                {
                    sb.AppendLine(indent + "else");
                    sb.AppendLine(indent + "{");
                    EmitPortChain(sb, p, g, node, "False", ctx, st, indent + "    ");
                    sb.AppendLine(indent + "}");
                }
                return;      // 两个分支已经各自走完，别再顺出口走一遍
            }

            case "And" or "Or":
            {
                string cond = BoolExpr(p, g, node, "Out", st, sb, indent, "true", ctx.Scope);
                if (!HasOut(g, node, "Out"))
                {
                    sb.AppendLine($"{indent}// 【{(node.Kind == "And" ? "且" : "或")}】右边没接线：这一支什么都不做");
                    return;
                }
                sb.AppendLine(indent + $"if ({cond})");
                sb.AppendLine(indent + "{");
                EmitPortChain(sb, p, g, node, "Out", ctx, st, indent + "    ");
                sb.AppendLine(indent + "}");
                return;      // 已经由 EmitPortChain 走过出口，别再走一遍
            }

            case "Condition":
            {
                string cond = BoolExpr(p, g, node, "Out", st, sb, indent, "true", ctx.Scope);
                if (!HasOut(g, node, "Out"))
                {
                    sb.AppendLine($"{indent}// 【条件】右边没接线：这一支什么都不做");
                    return;
                }
                sb.AppendLine(indent + $"if ({cond})");
                sb.AppendLine(indent + "{");
                EmitPortChain(sb, p, g, node, "Out", ctx, st, indent + "    ");
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

    /// <summary>这个出口上有没有接线。</summary>
    private static bool HasOut(ForgeGraphSpec g, ForgeNodeSpec node, string portKey)
    {
        int port = PortIndexOf(node, portKey);
        return port >= 0 && g.Outgoing(node.Id, port).Count > 0;
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
                    // 遗物 / 药水没有 cardPlay，用 Relic 那个上下文（CombatState 从 Owner.Creature 取）。
                    var ctxKind = string.Equals(ctxScope, "Card", StringComparison.Ordinal) ? CSharpCodeGen.CondCtx.Card : CSharpCodeGen.CondCtx.Relic;
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

    /// <summary>这个模块会不会产出一个数值（【数值】/【状态】/【能量】）。</summary>
    private static bool IsValueLike(ForgeNodeSpec node) =>
        node.Kind is "Value" or "Power" or "Energy";

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
        string st = ctxKind == CSharpCodeGen.CondCtx.Card ? "base.CombatState" : "base.Owner.Creature.CombatState";
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
        string uses = "__fgUses" + (ctx.Upgraded ? "Up" : "");

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
        string targetExpr = "cardPlay?.Target", int xPlus = 0)
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
                return $"decimal {ForgeFormula.ParamVar(name)} = {ForgeFormula.ParamExpr(param)};   // {name}= {param.Summary}";
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
        if (needX) w.Line("decimal __X = ForgeFormulaRuntime.Energy(base.Owner)"
            + (xPlus > 0 ? $" + (base.IsUpgraded ? {xPlus} : 0)" : "")
            + ";   // X = 打出这张牌那一刻的当前能量" + (xPlus > 0 ? $"（升级后 X + {xPlus}）" : ""));
        if (needY) w.Line("decimal __Y = ForgeFormulaRuntime.Stars(base.Owner);   // Y = 当前额外资源量");
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
    private static (List<ForgeNodeSpec> Effects, bool HasCardReward) WalkChain(ForgeGraphSpec g, ForgeNodeSpec trigger)
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
    /// 所以它一定出现在用到它的那一句之前；同一个模块只算一次（<see cref="GenState.ValueEmitted"/>）。
    /// </summary>
    private static string? ValueVar(CharacterProfile p, ForgeGraphSpec g, ForgeNodeSpec node,
        GenState st, StringBuilder sb, string indent, int outPort = 0)
    {
        if (st.Depth > 40) return null;
        if (!st.ValueEmitted.Add(node.Id + "#" + outPort.ToString(CultureInfo.InvariantCulture))) return node.VarName;
        // 【能量】右边有两个出口（能量 / 额外资源量），得按**接的是哪个口**分别取 __X / __Y；
        // 同一个模块的两个口各出一个变量，所以变量名也要带上口序号。
        string varName = outPort == 0 ? node.VarName : node.VarName + "p" + outPort.ToString(CultureInfo.InvariantCulture);

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
                string who = (inNode is null ? null : UnitExpr(g, inNode)) ?? "__fgTarget";
                sb.AppendLine(indent + $"decimal {varName} = ForgeFormulaRuntime.PowerAmount({who}, \"{powerId}\");   // fg:{node.Id}【状态】");
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
    private sealed record UnitRef(string? Single, string Foes, bool PerFoe = true)
    {
        public static readonly UnitRef Self = new("__fgSelf", "ForgeFormulaRuntime.AllAllies(__fgPlayer)");
        /// <summary>打出这张牌时玩家选中的那个目标（卡牌上这就是「指定单体敌人」的含义）。</summary>
        public static readonly UnitRef Target = new("__fgTarget", "ForgeFormulaRuntime.TargetOrFirst(__fgTarget, __fgPlayer)");
    }

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
                                          "ForgeFormulaRuntime.EnemyList(__fgPlayer)"),
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
    private static UnitRef ResolveEffectUnit(ForgeGraphSpec g, ForgeNodeSpec node, GenState st)
    {
        var wired = IncomingNode(g, node, 2);
        if (wired is not null) return ResolveUnit(g, wired, st.CurrentUnit);
        var spec = node.EffectSpec;
        if (spec is not null && spec.TargetSideCustom) return TargetSideUnit(spec);
        // ⚠ 链上没有【指向对象】时 st.CurrentUnit 可能是 null —— 这正是 ResolveUnit 里
        //   `fallback ?? UnitRef.Target` 兜的那一档，这里必须跟着兜（以前是走 ResolveUnit 兜的）。
        return st.CurrentUnit ?? UnitRef.Target;
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
                                          "ForgeFormulaRuntime.EnemyList(__fgPlayer)"),
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

        // 指向对象：「指向对象」入口（下标 2）接的对象模块；
        // 没接就用**这条链上最近一个【指向对象】声明的那一只**（用户要求：那是「这张牌选中谁」），
        // 链上没有【指向对象】（老图）才退回老规矩「打出时选中的目标」。
        // V0.2.129：新编辑器里把卡牌那栏「作用对象」也摆出来了 —— 只有用户**真的动过它**
        // （TargetSideCustom）才让这一栏接管；老存档没动过 = 老规矩，生成文本一个字符都不变。
        var unit = ResolveEffectUnit(g, node, st);
        string single = unit.Single ?? "__fgTarget";
        string foes = unit.Foes;
        // 「战斗状态」在这三个类上写法不一样（本体：CombatState 是 CardModel 上的属性）：
        //   卡牌 → base.CombatState（和卡牌那条老链一致）；
        //   遗物 / 药水 → base.Owner.Creature.CombatState（那两条老链就是这么写的）。
        // ⚠ 以前这里一律写 base.CombatState —— 锻造遗物上用「造成伤害（全体敌人）」/「生成卡牌」
        //   生成出来是 CS0117（RelicModel 未包含 CombatState），整个工程编译不过（探针真编译踩到）。
        string combatState = string.Equals(ctx.Scope, "Card", StringComparison.Ordinal)
            ? "base.CombatState"
            : "base.Owner.Creature.CombatState";

        // 施加 / 扣除 / 翻倍状态这类效果要的那个状态（本体 Power 类名；global using 已经把
        // MegaCrit.Sts2.Core.Models.Powers 引进来，所以短名直接用，和「卡牌」页那套生成一致）
        string powerId = node.Get("PowerId");

        // 【生成卡牌】要哪张卡：模块上选的是「已有的卡」（本体卡或你自己的卡），生成的是它的类名
        string? spawnCardClass = null;
        string spawnCardId = node.Get("SpawnCardId");
        if (spawnCardId.Length > 0)
        {
            var targetCard = p.AllCards.FirstOrDefault(c => c is not null && string.Equals(c.ClassName, spawnCardId, StringComparison.Ordinal));
            if (targetCard is not null) spawnCardClass = Naming.From(p).CardClassName(p, targetCard);
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
        if (nextTurn && kind is not ("Block" or "Draw" or "ApplyPower"))
        {
            sb.AppendLine(indent + "// ⚠「下回合生效」在节点图里暂时只支持「获得格挡 / 抽牌」，这一条按立即生效生成。");
            if (kind == "TempPower")
                sb.AppendLine(indent + "//（「临时增益」想要『撑过这一回合、下回合开始时才撤』请用下面那栏「什么时候撤掉」选「下回合开始时才撤掉」，"
                    + "不要勾这个「下回合生效」。）");
            nextTurn = false;
        }

        // 选牌类效果（消耗 / 丢弃 / 升级 / 从某一摞拿牌）要弹本体的选牌界面，提示语是 LocString，
        // 只能内联生成（静态辅助类里没有 base）。这几句自己写进 sb，写完直接收工。
        if (kind is "ExhaustCard" or "DiscardCard" or "UpgradeCard")
        {
            EmitCardPick(sb, ctx, node, kind, amount, indent);
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
            EmitRepeatLoop(sb, indent, timesExpr, __ind =>
            {
                sb.AppendLine(__ind + $"foreach (CardModel __fgTake in (await CardSelectCmd.FromCombatPile({ctx.ChoiceVar}, {pile}.GetPile(__fgPlayer), __fgPlayer, new CardSelectorPrefs(base.SelectionScreenPrompt, (int)({amount})))).ToList())");
                sb.AppendLine(__ind + "    await CardPileCmd.Add(__fgTake, PileType.Hand);");
            });
            return;
        }
        // 变化 / 复制（本体「下去 / 二刀流」那两套写法）
        if (kind is "TransformCard" or "CopyCard")
        {
            bool chosen = !string.Equals(node.Get("Pick", "Chosen"), "Random", StringComparison.OrdinalIgnoreCase);
            // 变量名按模块 Id 取，**每条效果一套** —— 同一条链上有两个同种效果时不会撞名（编译期就报 CS0128）
            string selVar = UniqueVar(node, "__fgSel");
            EmitRepeatLoop(sb, indent, timesExpr, __ind =>
            {
                if (chosen)
                    sb.AppendLine(__ind + $"var {selVar} = (await CardSelectCmd.FromHand(context: {ctx.ChoiceVar}, player: __fgPlayer, prefs: new CardSelectorPrefs(base.SelectionScreenPrompt, (int)({amount})), filter: null, source: {ctx.CardSelfExpr})).ToList();");
                else
                    sb.AppendLine(__ind + $"var {selVar} = ({ctx.ChoiceVar} is null ? new List<CardModel>() : CardPile.GetCards(__fgPlayer, PileType.Hand).OrderBy(_ => __fgPlayer.RunState.Rng.CombatCardSelection.NextInt(int.MaxValue)).Take((int)({amount})).ToList());");
                if (kind == "CopyCard")
                {
                    sb.AppendLine(__ind + $"foreach (CardModel {selVar}C in {selVar}) await CardPileCmd.AddGeneratedCardToCombat({selVar}C.CreateClone(), PileType.Hand, __fgPlayer);");
                }
                else if (spawnCardClass is null)
                {
                    sb.AppendLine(__ind + $"foreach (CardModel {selVar}C in {selVar}) await CardCmd.TransformToRandom({selVar}C, __fgPlayer.RunState.Rng.CombatCardSelection);");
                }
                else
                {
                    sb.AppendLine(__ind + $"foreach (CardModel {selVar}C in {selVar}) await CardCmd.Transform({selVar}C, __fgPlayer.RunState.CreateCard(ModelDb.Card<{spawnCardClass}>(), __fgPlayer));");
                }
            });
            return;
        }
        // 预见（看抽牌堆顶 N 张，想丢的丢进弃牌堆 —— 本体没有这个机制，照「卡牌」页那套拼）
        if (kind == "Scry")
        {
            string topVar = UniqueVar(node, "__fgTop");
            string dropVar = UniqueVar(node, "__fgDrop");
            EmitRepeatLoop(sb, indent, timesExpr, __ind =>
            {
                sb.AppendLine(__ind + $"var {topVar} = PileType.Draw.GetPile(__fgPlayer).Cards.Take((int)({amount})).ToList();");
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
            string call = $"await PowerCmd.Apply<{powerId}>({ctx.ChoiceVar}, __fgPet, {amount}, __fgSelf, {ctx.CardSelfExpr});";
            petBody = pick == "AllSummons"
                ? $"foreach (Creature __fgPet in ForgeFormulaRuntime.SummonList(__fgPlayer)) {{ {call} }}"
                : $"{{ Creature? __fgPet = {one}; if (__fgPet is not null) {{ {call} }} }}";
        }
        string body = kind switch
        {
            // ---------- 单体 / 群体的基础效果 ----------
            // 【造成伤害】加【全体敌人】：走群体版本的一次性命令（TargetingAllOpponents），
            // 「命中次数 N」= 全体各挨 N 下。**不能**写成 foreach + WithHitCount(N)
            //（那是「每个敌人各打 N 下」，用户实测报过）。
            // 【随机敌人】仍然逐个动手：随机挑一个 → 挑中的那个打 N 下（那是「随机 N 下」的本体语义）。
            "Damage" when !unit.PerFoe =>
                hitsMulti
                    ? $"await DamageCmd.Attack({amount}).FromCard({ctx.CardSelfExpr}, {ctx.CardPlayExpr}).TargetingAllOpponents({combatState}).WithHitCount({hitsExpr}).Execute({ctx.ChoiceVar});"
                    : $"await DamageCmd.Attack({amount}).FromCard({ctx.CardSelfExpr}, {ctx.CardPlayExpr}).TargetingAllOpponents({combatState}).Execute({ctx.ChoiceVar});",
            "Damage" => hitsMulti
                ? $"foreach (Creature __fgFoe in {foes}) {{ await DamageCmd.Attack({amount}).FromCard({ctx.CardSelfExpr}, {ctx.CardPlayExpr}).Targeting(__fgFoe).WithHitCount({hitsExpr}).Execute({ctx.ChoiceVar}); }}"
                : $"foreach (Creature __fgFoe in {foes}) {{ await DamageCmd.Attack({amount}).FromCard({ctx.CardSelfExpr}, {ctx.CardPlayExpr}).Targeting(__fgFoe).Execute({ctx.ChoiceVar}); }}",
            "Block" => nextTurn
                ? $"foreach (Creature __fgFoe in {foes}) await PowerCmd.Apply<BlockNextTurnPower>({ctx.ChoiceVar}, __fgFoe, {amount}, __fgSelf, {ctx.CardSelfExpr});"
                : $"foreach (Creature __fgFoe in {foes}) await CreatureCmd.GainBlock(__fgFoe, {amount}, ValueProp.Move, {ctx.CardPlayExpr});",
            "Heal" => $"foreach (Creature __fgFoe in {foes}) await CreatureCmd.Heal(__fgFoe, {amount});",
            "HpLoss" => $"foreach (Creature __fgFoe in {foes}) await CreatureCmd.Damage({ctx.ChoiceVar}, __fgFoe, {amount}, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, {ctx.CardSelfExpr}, {ctx.CardPlayExpr});",
            "MaxHp" => $"foreach (Creature __fgFoe in {foes}) await CreatureCmd.GainMaxHp(__fgFoe, {amount});",
            "ApplyPower" => powerId.Length == 0
                ? null
                : $"foreach (Creature __fgFoe in {foes}) {{ if ({amount} < 0m) {{ PowerModel? __negPower = __fgFoe.GetPower<{powerId}>(); if (__negPower is not null) await PowerCmd.ModifyAmount({ctx.ChoiceVar}, __negPower, {amount}, __fgSelf, {ctx.CardSelfExpr}); }} else await PowerCmd.Apply<{powerId}>({ctx.ChoiceVar}, __fgFoe, {amount}, __fgSelf, {ctx.CardSelfExpr}); }}",
            // 临时增益 / 临时减益：**不能**直接施加真状态（那就是永久的了）——
            // 施加生成端那套「临时 Power」（<c>TempPowers.cs</c> 里的类：挂上时把层数加进真状态、
            // 到点再撤掉、自己消失）。类名由 TempPowerEffectOf 算，和 CollectTempPowerEffects 是同一份。
            // 数值是正还是负由那个类自己处理（负数档扣层数 / 施加负层数，见 TempPowersSource）。
            "TempPower" => powerId.Length == 0
                ? null
                : $"foreach (Creature __fgFoe in {foes}) await PowerCmd.Apply<{CSharpCodeGen.TempPowerClassName(TempPowerEffectOf(node), p)}>({ctx.ChoiceVar}, __fgFoe, {amount}, __fgSelf, {ctx.CardSelfExpr});",
            "DoublePower" => powerId.Length == 0
                ? null
                : $"foreach (Creature __fgFoe in {foes}) {{ PowerModel? __dbl = __fgFoe.GetPower<{powerId}>(); if (__dbl is not null) await PowerCmd.ModifyAmount({ctx.ChoiceVar}, __dbl, __dbl.Amount * (({amount}) - 1m), __fgSelf, {ctx.CardSelfExpr}); }}",
            "ClearPowerOne" => powerId.Length == 0
                ? null
                : $"foreach (Creature __fgFoe in {foes}) {{ PowerModel? __clr = __fgFoe.GetPower<{powerId}>(); if (__clr is not null) await PowerCmd.Remove(__clr); }}",
            "ClearPowerDebuffs" => $"foreach (Creature __fgFoe in {foes}) await ForgeFormulaRuntime.ClearDebuffs(__fgFoe);",
            "Stun" => $"foreach (Creature __fgFoe in {foes}) CreatureCmd.Stun(__fgFoe);",
            "ForbidBlock" => $"foreach (Creature __fgFoe in {foes}) await PowerCmd.Apply<NoBlockPower>({ctx.ChoiceVar}, __fgFoe, {amount}, __fgSelf, {ctx.CardSelfExpr});",
            // ---------- 对**召唤物**动手 ----------
            // 目标是召唤物，**不能**用 unit.Foes（那默认是「指定敌人」）。哪一只由【对象】模块决定：
            //   指定召唤物 → 按名称找那一只；全部召唤物 → 全都要；其余（含没接【对象】）→ 第一只。
            // 需求原文（别人报的）：「给召唤物 buff 不生效」—— 以前这一档在节点图里**什么都没生成**
            //（只有一行注释），所以现在既补上实现，也由校验器把「选了但没接的种类」拦下来。
            "PetApplyPower" => petBody,
            // ---------- 只作用于「自己 / 玩家」的 ----------
            "Draw" => nextTurn
                ? $"await PowerCmd.Apply<DrawCardsNextTurnPower>({ctx.ChoiceVar}, __fgSelf, {amount}, __fgSelf, {ctx.CardSelfExpr});"
                // ⚠ 本体这个 API 叫 `CardPileCmd.Draw(choiceContext, count, player)` ——
                //   以前这里写的是 `DrawCards(count, choiceContext, player)`（方法名和参数顺序都不对），
                //   生成的工程直接 CS0117（探针真编译时踩到）。和卡牌 / 遗物那条老链同一个写法（CSharpCodeGen）。
                : $"await CardPileCmd.Draw({ctx.ChoiceVar}, {amount}, __fgPlayer);",
            "Energy" => $"await PlayerCmd.GainEnergy({amount}, __fgPlayer);",
            "Gold" => $"await PlayerCmd.GainGold({amount}, __fgPlayer);",
            "ExtraResource" => $"await PlayerCmd.GainStars({amount}, __fgPlayer);",
            "EndTurn" => $"PlayerCmd.EndTurn(__fgPlayer, false);",
            // ---------- 牌堆类（做一次，不是对每个目标）----------
            // 「消耗 / 丢弃 / 升级 / 从某一摞拿牌」已经在上面内联生成了（提示语要 base.*），这里只管剩下的
            "GenerateCard" => spawnCardClass is null
                ? null
                : $"for (int __fgN = 0; __fgN < (int)({amount}); __fgN++) {{ CardModel __gen = {combatState}.CreateCard<{spawnCardClass}>(__fgPlayer); await CardPileCmd.AddGeneratedCardToCombat(__gen, PileType.{node.Get("SpawnTo", "Hand")}, __fgPlayer); }}",
            _ => null,
        };

        // 上面的 switch 里被内联成循环的那几种，只有在「是单个目标」时才需要外层守卫；
        // 现在一律走 foreach（列表表达式对单体也会返回一个只含它的列表），所以不用再套守卫。
        if (body is null)
        {
            // 两种原因分开写：**这个种类没接**（选了等于白选）vs **必填的东西没填**（状态 / 目标卡）。
            // 以前一律写「还没接上这个种类」—— 用户填漏了字段时会被这句话带偏。
            sb.AppendLine(SupportedEffectKinds.Contains(kind)
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
    /// 「消耗 / 丢弃 / 升级手牌里的 N 张」——选牌类效果。
    /// 提示语是本体 <c>CardSelectorPrefs</c> 上的 LocString 常量，所以这几句内联生成
    /// （静态辅助类里没有 base），和「卡牌」页那套写法一致。
    /// </summary>
    private static void EmitCardPick(StringBuilder sb, EmitCtx ctx, ForgeNodeSpec node, string kind, string amount, string indent)
    {
        bool chosen = !string.Equals(node.Get("Pick", "Chosen"), "Random", StringComparison.OrdinalIgnoreCase);
        string var = UniqueVar(node, "__fgPick");
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
        if (chosen)
            sb.AppendLine(indent + $"var {var} = (await CardSelectCmd.FromHand(context: {ctx.ChoiceVar}, player: __fgPlayer, prefs: new CardSelectorPrefs({prompt}, (int)({amount})), filter: null, source: {ctx.CardSelfExpr})).ToList();");
        else
            sb.AppendLine(indent + $"var {var} = ({ctx.ChoiceVar} is null ? new List<CardModel>() : CardPile.GetCards(__fgPlayer, PileType.Hand).OrderBy(_ => __fgPlayer.RunState.Rng.CombatCardSelection.NextInt(int.MaxValue)).Take((int)({amount})).ToList());");
        sb.AppendLine(indent + $"foreach (CardModel __fgCard in {var}) {act}");
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
        "TempPower", "EndTurn",
        // ---------- 牌堆 / 选牌类 ----------
        "GenerateCard", "GainPotion", "ExhaustCard", "DiscardCard", "TransformCard", "UpgradeCard",
        "CopyCard", "Scry", "TakeFromDraw", "TakeFromDiscard", "TakeFromExhaust",
        // ---------- 召唤物 ----------
        "PetApplyPower",
        // 卡牌奖励：只在【触发时机】=「战斗胜利时」那一支上才生成（见 EmitEffect 里那段），
        // 所以它在别的时机上「没有代码」是**故意的**，那一条由它自己的提示负责，不算「没接」。
        "CardReward",
    };

    /// <summary>【效果】模块上「必须选一个状态」的种类（没选就一行代码都不生成，不能让用户猜）。</summary>
    private static readonly string[] NeedsPowerField =
        { "ApplyPower", "TempPower", "DoublePower", "ClearPowerOne", "PetApplyPower" };

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
    public static List<(string Level, string Message)> UnsupportedEffectSettings(ForgeGraphSpec? g, ForgeNodeSpec node)
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
        if (e.AmountIsStack || e.TimesIsStack || e.RepeatIsStack)
            Add("「= 本状态层数」那几个开关", "那是「自定义状态」触发器的开关；节点图里用【状态】模块 + 【数值】模块取值。");
        if (e.Condition is not null && !e.Condition.IsNone)
            Add("条件选项", "节点图里用【条件】模块 + 【if】/【且】/【或】/【关系符】表达（【效果】模块自己不带条件）。");
        if (e.SlowPercent > 0m)
            Add("直接把「缓慢」设成 N%", "节点图只按层数施加状态（要用那一档请到「卡牌 / 遗物 / 药水」页的效果列表里配）。");
        if (e.IsSpawnRandom || e.HasSpawnChoice || e.SpawnChosen)
            Add("取卡方式 = 按范围随机 / 范围限定 / 多选1 / 从范围里自己挑一张",
                "节点图的「生成卡牌 / 变化卡牌」只能生成（变化成）「目标卡」那一栏指定的那张卡。");
        if (e.SpawnUpgraded || e.SpawnFree || e.SpawnFreeThisTurn || e.SpawnUpgradedThisTurn)
            Add("生成 / 变化出来的卡的附加处理（直接升级 / 免费 / 仅本回合免费 / 仅本回合升级）",
                "节点图把生成出来的卡按原样放进牌堆。");
        if (e.HasSpawnKeyword)
            Add("生成 / 变化出来的卡：加上关键词", "节点图不支持给生成出来的卡挂关键词。");
        if (e.ShowCopies && e.Copies != 1)
            Add("复制的份数", "节点图的「复制卡牌」每张复制 1 份（「数值」= 选几张牌）。");
        if (e.UsesCountMode && e.CountMode.Length > 0)
            Add("取几张（最多 N / 任意张）", "节点图按「恰好 N 张」选牌。");
        if (e.UsesSelectPile && !string.Equals(e.SelectPile, "Hand", StringComparison.OrdinalIgnoreCase))
            Add("从哪里选牌", "节点图的选牌类效果**只从手牌里挑**（要从抽牌堆 / 弃牌堆 / 消耗牌堆拿牌请到「卡牌」页的效果列表里配）。");
        if (e.IsPetEffect && !string.IsNullOrWhiteSpace(e.PetSummon))
            Add("召唤物（哪一只）", "节点图里由【对象】模块决定哪一只召唤物（选「指定召唤物」+ 填召唤物名称）。");
        if (e.NextTurn && !string.Equals(e.Kind, "Block", StringComparison.Ordinal)
            && !string.Equals(e.Kind, "Draw", StringComparison.Ordinal)
            && !string.Equals(e.Kind, "ApplyPower", StringComparison.Ordinal))
            Warn("下回合生效", "节点图这一版只有「获得格挡 / 抽牌 / 施加增益减益」有延迟分支（别的种类会被当成立即生效生成）。");
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
        var opt = ForgeTimings.Find(trigger.Get("Timing", ForgeTimings.CardPlayed), scope);
        if (opt is null)
            list.Add(("错误", $"【触发时机】选的「{trigger.Get("Timing")}」认不出来，请重新选一次。"));
        else if (!string.Equals(opt.Scope, scope, StringComparison.Ordinal))
            list.Add(("错误", $"【触发时机】选的「{opt.Display}」是给{ScopeZh(opt.Scope)}用的，这块布是{ScopeZh(scope)}的。"));
        else if (string.Equals(scope, "Relic", StringComparison.Ordinal) && GraphNeedsChoiceContext(g))
        {
            // 第 9 批 ④：遗物档那几条时机里，本体的钩子**没有** choiceContext、也造不出来的
            //（战斗胜利 / 战斗结束 / 获得金币 / 遗物被点击）—— 图里真要 context 时只能传 null!，
            // 运行时会炸。宁可在这里报错说清，也不静默生成坏代码。
            string ctxFrom = RelicCtxFrom(new HookPlan(trigger, opt), nested: false);
            if (string.Equals(ctxFrom, "None", StringComparison.Ordinal))
                list.Add(("错误", $"【触发时机】选的「{opt.Display}」：本体的这个钩子**没有** choiceContext，"
                    + "而且造不出来（战斗胜利 / 战斗结束 / 获得金币 / 遗物被点击这几个时刻没有可用的选择上下文）——"
                    + "图里「抽牌 / 造成伤害 / 施加状态 / 选牌」这类**需要玩家选择**的效果会传 null! 在运行时炸。"
                    + "请把这些效果挪到别的触发时机上（战斗中的那些时机都行）。"));
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
                    if (FormulaBuiltins.Find(id) is not null) continue;
                    if (!p.CustomParams.Any(x => x is not null && string.Equals(ParamNames.Normalize(x.Name), id, StringComparison.Ordinal)))
                        list.Add(("错误", $"【{kindZh}】的「{label}」用了参数 {id}=，但「自定义参数 / 组」页里没有这个参数"
                            + $"（内置只读量可以直接写：{FormulaBuiltins.Names}）。"));
                }
                if (num.Runtime && ForgeNumbers.NeedsCompileTimeConst(fieldKind, key))
                    list.Add(("错误", $"【{kindZh}】的「{label}」只能填**生成期就算得出来的数**："
                        + "这一栏会写进卡牌自己的费用（本体 base(cost, …) / CanonicalStarCost），**必须**是编译期常量。"
                        + $"「{text}」里用了运行时才算的量 —— 改成固定数字，或者用「固定数值」来源的参数写 A+3。"));
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
            if (n.Kind == "Value")
            {
                string f = n.Get("Formula").Trim();
                if (f.Length == 0) continue;
                var root = ForgeFormula.TryParse(f, out string err);
                if (root is null) { list.Add(("错误", $"【数值】的公式「{f}」有问题：{err}")); continue; }
                foreach (string id in ForgeFormula.Identifiers(root))
                {
                    if (id is "X" or "Y") continue;
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
            // ② 必填的东西没填（状态 / 目标卡）：也是「一行代码都不生成」，但原因完全不同
            if (NeedsPowerField.Contains(kind, StringComparer.Ordinal) && string.IsNullOrWhiteSpace(n.Get("PowerId")))
                list.Add(("错误", $"【效果】「{display}」还没选「状态」（模块上的「状态」那一栏）—— 没选的话这一块不会生成任何代码。"));
            if (kind == "GenerateCard" && string.IsNullOrWhiteSpace(n.Get("SpawnCardId")))
                list.Add(("错误", $"【效果】「{display}」还没选「目标卡」（模块上的「目标卡」那一栏）—— 没选的话这一块不会生成任何代码。"));
            // ③ V0.2.129：编辑器合并之后，卡牌页有的栏位【效果】模块上都有 ——
            //    但节点图的发射器只覆盖其中一部分。**填了不生效的栏位一律报错**（不许静默生成一半）。
            foreach (var (level, msg) in UnsupportedEffectSettings(g, n))
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
