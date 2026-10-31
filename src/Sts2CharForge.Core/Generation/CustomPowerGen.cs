using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Plugins;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 「自定义状态（能力）」的生成：把配置里的每个状态变成一个 PowerModel 子类。
///
/// 每个状态 = 一个 <c>cs/Powers/&lt;类名&gt;.cs</c>，里面按「触发时机」重写本体的钩子
/// （钩子都在 AbstractModel 上、由 Hook.cs 派发，PowerModel.ShouldReceiveCombatHooks 为 true）。
/// 名字/描述写进 localization/zhs/powers.json；图标和层数数字颜色由状态外观补丁接管
/// （PowerModel.Icon 不是 virtual，没法在子类里重写，只能补丁）。
/// </summary>
public static class CustomPowerGen
{
    public static bool IsActive(CustomPowerSpec p) => p.Enabled && !string.IsNullOrWhiteSpace(p.Name);

    public static List<CustomPowerSpec> Active(CharacterProfile profile) =>
        profile.CustomPowers.Where(IsActive).ToList();

    public static bool HasAny(CharacterProfile p) => Active(p).Count > 0;

    /// <summary>类名（配置空着就用 角色类名 + Power + 序号）。</summary>
    public static string ClassNameOf(CharacterProfile p, CustomPowerSpec power, int index)
    {
        string cls = (power.ClassName ?? "").Trim();
        if (cls.Length > 0 && Naming.IsValidIdentifier(cls)) return cls;
        return Naming.From(p).CharClass + "Power" + (index + 1);
    }

    /// <summary>本体 Id.Entry（本地化键前缀）：类名按驼峰拆开大写，和本体一致。</summary>
    public static string EntryOf(CharacterProfile p, CustomPowerSpec power, int index) =>
        EffectCatalog.SlugFor(ClassNameOf(p, power, index));

    /// <summary>图标在工程里的相对路径。</summary>
    public static string IconRelPath(CharacterProfile p, CustomPowerSpec power, int index) =>
        $"images/powers_override/{EntryOf(p, power, index).ToLowerInvariant()}_icon.png";

    /// <summary>图标 res:// 路径（没上传返回 null）。</summary>
    public static string? IconResPath(CharacterProfile p, CustomPowerSpec power, int index) =>
        string.IsNullOrWhiteSpace(power.Icon) ? null : "res://" + IconRelPath(p, power, index);

    /// <summary>
    /// 描述（没填就按触发器自动生成一句）。
    ///
    /// ⚠ 多行描述**原样保留换行**（本地化表是 JSON，换行会被正确转义；游戏里的悬停提示本来就支持多行）。
    /// 需要「压成一行」的地方（C# 注释之类）请用 <see cref="OneLine"/>。
    /// </summary>
    public static string DescriptionOf(CustomPowerSpec power) =>
        string.IsNullOrWhiteSpace(power.Description)
            // V0.2.194：图说了算时按**图上的效果**自动写一句（老路照旧按触发时机列表 —— 逐字不变）
            ? (ForgeGraphGen.UsesGraph(power) ? GraphAutoDescription(power) : PowerTriggers.AutoDescription(power))
            : power.Description.Trim();

    /// <summary>
    /// 图说了算的自定义状态：按布上每条【触发时机】+ 它那条链上的【效果】自动写一句描述
    /// （用户没填「描述」栏时用；填了就用他写的）。
    ///
    /// 为什么不复用 <c>PowerTriggers.AutoDescription</c>：那份读的是 <c>power.Triggers</c>，
    /// 而图说了算时那个列表是空的 → 描述会变成空白（游戏里悬停什么都看不到）。
    /// </summary>
    private static string GraphAutoDescription(CustomPowerSpec power)
    {
        var g = power.ForgeGraph;
        if (g is null || g.IsEmpty) return "";
        var parts = new List<string>();
        foreach (var trigger in g.TriggerNodes)
        {
            var timing = ForgeTimings.Find(trigger.Get("Timing"), ForgeTimings.PowerScope);
            var (chain, _) = ForgeGraphGen.WalkChain(g, trigger);
            var names = chain
                .Select(n => EffectCatalog.FindKind(ForgeEffectBridge.KindOf(n))?.Display ?? ForgeEffectBridge.KindOf(n))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            parts.Add($"{timing?.Display ?? "触发时机"}：{(names.Count > 0 ? string.Join("、", names) : "（还没接效果）")}");
        }
        return string.Join("；", parts);
    }

    /// <summary>
    /// 把一段用户文本压成**一行**（CRLF / CR / LF 全换成空格）。
    ///
    /// 为什么要它：用户会直接在描述栏里**打回车换行**（V0.2.168 用户实测报的
    /// 「自定义状态的描述栏中 如果用回车换行会有报错」）—— WPF 的 TextBox 打出来的是 **CRLF**，
    /// 而生成端往 C# 注释里塞这段文本时只把 <c>\n</c> 换成了空格、**留下了 <c>\r</c>**，
    /// C# 把单独的 <c>\r</c> 也当换行 ⇒ 注释被截断，后半截（<c> 第二行</c> 之类）变成代码 → 整个工程编译不过。
    /// </summary>
    public static string OneLine(string? text) =>
        (text ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>生成一个自定义状态的 PowerModel 子类。</summary>
    public static string Source(CharacterProfile profile, CustomPowerSpec power, int index)
    {
        PercentPowers.Current = profile;   // V0.2.301：状态链里的「百分比状态」也要能拼出类名
        var n = Naming.From(profile);
        string cls = ClassNameOf(profile, power, index);
        bool debuff = power.Type == "Debuff";
        // 「生成调试日志」（V0.2.142）用的临时变量序号：每次生成一个源文件从头开始
        DebugLogGen.Reset();

        // ==================== V0.2.194：这块布说了算吗 ====================
        // 图非空（锻造页「状态列表」那一类）→ 触发时机来自图上的【触发时机】模块、
        // 方法体（效果链）来自图上渲染好的正文；**壳**（签名 / 归属守卫 / choiceContext / 数值闸门 /
        // 衰减移除）和「自定义状态」页那条链**完全是同一份代码**（见下面 EmitTriggerMethod 那几个调用点）。
        // 图空 / null → graphTriggers = null / bodies 为空 → 下面全部走老路，生成文本一个字符都不变。
        var graphTriggers = ForgeGraphGen.PowerTriggersOf(profile, power);
        var graphBodies = graphTriggers is null ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ForgeGraphGen.PowerHookBodies(profile, power);
        var triggers = graphTriggers ?? power.Triggers.Where(t => t is not null).ToList();

        var w = new CodeWriter();
        w.Line($"// <auto-generated> 自定义状态「{power.Name}」（能力牌用） </auto-generated>");
        // 「覆盖血条」的颜色 / 从哪端开始：生成出来的补丁文件里写着这些，回读时看不出它是从哪来的
        //（血条那几条是运行时算的，跟本类没有任何字符串联系）→ 在这一行标记里带上，供「从工程恢复存档」认回来。
        string hpBarHex = HpBarOverlayGen.NormalizeHex(power.HpBarColor);
        if (hpBarHex.Length > 0)
            w.Line($"// CET:HpBarColor={hpBarHex} CET:HpBarSide={HpBarSides.Normalize(power.HpBarSide)}");
        w.Line($"namespace {n.Namespace};");
        w.Line();
        w.Line($"/// <summary>{OneLine(power.Name)}：{OneLine(DescriptionOf(power))}</summary>");
        w.Open($"public sealed class {cls} : PowerModel");
        // V0.2.194：图标记（「从工程恢复」唯一的依据；图空时一个字符都不写 —— 老状态照旧）
        string? graphMarker = ForgeGraphGen.MarkerOf(power.ForgeGraph);
        if (graphMarker is not null) w.Line(graphMarker);
        w.Line($"public override PowerType Type => PowerType.{(debuff ? "Debuff" : "Buff")};");
        w.Line();
        w.Line($"public override PowerStackType StackType => PowerStackType.{(power.Single ? "Single" : "Counter")};");

        // V0.2.239（用户问「在自定义状态增加状态层数能否到负值」）：勾了「允许负层数」就照本体
        // 力量 / 敏捷 / 专注 那套覆写 AllowNegative —— 这样 `PowerModel.SetAmount` 不会把层数夹在 0 以上、
        // `ShouldRemoveDueToAmount` 也变成「正好 0 才移除」，于是「施加增益/减益」填负数时
        // 生成端会走「直接施加负值」那一档（见 CSharpCodeGen.ApplyPowerLine / EffectCatalog.AllowsNegativeLayers）。
        // 不勾时一行都不写 —— 老存档生成结果逐字不变（红线①）。
        if (power.AllowNegativeLayers)
        {
            w.Line();
            w.Line("// 允许负层数（用户勾了）：和本体 力量 / 敏捷 / 专注 一样，0 层也能被扣成负数");
            w.Line("//（负层数在游戏里按「减益」显示，正好回到 0 时本体会把这个状态移掉）");
            w.Line("public override bool AllowNegative => true;");
        }

        // 「生命真的少了」这条链的**重入闸**：见 NeedsHpLossGuard 的说明（不加会无限扣血扣到死）。
        bool hpLossGuard = NeedsHpLossGuard(triggers);
        if (hpLossGuard)
        {
            w.Line();
            w.Line("/// <summary>");
            w.Line("/// 「生命真的少了」那些钩子（AfterDamageReceived / AfterCurrentHpChanged）正在跑本状态的效果时置 true。");
            w.Line("/// 为什么必须有：本状态的「失去生命」效果走的是 CreatureCmd.Damage，而它**又会**触发那两个钩子 ——");
            w.Line("/// 不拦的话就是「受到伤害 → 失去 1 点生命 → 又是一次受到伤害 → …」无限循环，玩家直接被扣到死。");
            w.Line("/// 本体自己的「生命少了才触发」类状态都没有配「再造成伤害」，所以本体不会踩这个坑。");
            w.Line("/// </summary>");
            w.Line("private bool __inHpLoss;");
        }

        if (triggers.Count == 0)
        {
            w.Line();
            w.Line(graphTriggers is null
                ? "// 没配触发时机：这个状态只会挂着（加个触发时机才会真的做事）"
                : "// 这块布上还没有【触发时机】：去锻造页加一块（触发时机是整张图的起点）");
        }

        // 「每累计 N 次触发一次」的基准值（见 EmitItemsWithAmountGate）：**每个状态实例自己一份**，
        // -1 = 还没记过（记的时候 = 「刚上这个状态时本体历史里已经有多少次」）。
        {
            var countTriggers = triggers
                .Where(t => t is not null && t.TriggerAmount > 0m
                    && (CSharpCodeGen.TriggerAmountNeedsScope(PowerTriggers.Canonical(t.Kind))
                        || string.Equals(PowerTriggers.Canonical(t.Kind), "EnemyDeath", StringComparison.Ordinal)))
                .ToList();
            if (countTriggers.Count > 0)
            {
                var fields = countTriggers.Select(t => CountBaseField(t, power))
                    .Distinct(StringComparer.Ordinal).ToList();
                w.Line();
                w.Line("/// <summary>");
                w.Line("/// 「每累计 N 次触发一次」的基准值：**刚上这个状态时**本体历史里已经有多少次（int.MinValue = 还没记过）。");
                w.Line("/// 为什么要有它（V0.2.168 用户实测报的）：本体历史是整个回合 / 整场战斗的累计 ——");
                w.Line("/// 直接拿「累计 % N」判，会把**上状态之前**发生过的事也数进去（「要打 4 张牌」在上状态前");
                w.Line("/// 已经打了 3 张时，上状态后只要再打 1 张就触发了）。只数基准之后的事件才对。");
                w.Line("/// ⚠ V0.2.211：同一个状态上**两条筛选不同**的计数时机（用户报的「打出攻击牌 3 次 + 打出技能牌 3 次」）");
                w.Line("/// 必须各记一份 —— 共用一份时两条的累计值不同，后跑的那条会用自己的「累计 - 1」把先跑那条冲掉，");
                w.Line("/// 于是第一次只要 1 张就触发（后续又变回 3 张）。这两条的字段名带筛选（见 CountBaseField）。");
                w.Line("/// ⚠ V0.2.340：哨兵值从 -1 改成 int.MinValue —— -1 是**合法的基准值**（本回合第 1 张牌时");
                w.Line("/// 「历史累计」= 0，基准正好记成 -1），用 -1 当「没记过」会让换回合后的**第一张牌**被");
                w.Line("/// 当成「还没记过」再重置一次（那一刻的 0 被丢掉），于是每回合第一次要打 3 张才触发（用户报的");
                w.Line("/// 「两张牌就该给，但第三张才给」）。");
                w.Line("/// </summary>");
                w.Line("private int " + string.Join(", ", fields.Select(f => f + " = int.MinValue")) + ";");
            }
            // V0.2.336（用户报「花掉能量后」的计数从战斗开始算）：花能量 / 花额外资源量这两条时机以前
            // 直接用本体历史的总量 → 上状态之前花掉的也算进去。这里同样给每个状态实例记一份基准
            // （按范围各一份：本回合 / 本场战斗；-1 = 还没记过，见 EmitSpendTriggers）。
            var spendScopes = triggers
                .Where(t => t is not null && t.TriggerAmount >= 0m
                    && PowerTriggers.Canonical(t.Kind) is "EnergySpent" or "StarsSpent")
                .Select(t => (t.TriggerScope == "Combat") ? "Combat" : "Turn")
                .Distinct(StringComparer.Ordinal).ToList();
            if (spendScopes.Count > 0)
            {
                w.Line();
                w.Line("/// <summary>「每累计花掉 N 点触发一次」的基准值：这个状态被施加那一刻已经花掉多少（-1 = 还没记过）。</summary>");
                w.Line("private int " + string.Join(", ", spendScopes.Select(sc => "__spentBase" + sc + " = -1")) + ";");
            }
        }

        // 「要和其他时机同时成立」（用户要求）：≥ 2 条时机勾了它才有意义 —— 每条一个成立标记，
        // 「本回合」窗口还要记一下当前的回合数（换回合就把标记清掉）。
        // V0.2.194：图说了算时这一套不参与（图上用【if】/【且】/【或】表达「同时成立」）。
        var andTriggers = graphTriggers is null ? power.AndTriggers : new List<PowerTriggerSpec>();
        bool andOn = andTriggers.Count >= 2;
        if (andOn)
        {
            w.Line();
            w.Line("/// <summary>");
            w.Line("/// 【要和其他时机同时成立】这几条时机各自照常判断自己那件事，谁成立了就打个标记；");
            w.Line("/// **最后凑齐的那一条**才会跑它自己的效果，然后把这一轮的标记清掉（一个窗口只触发一次）。");
            w.Line("/// 窗口 = " + (power.AndScope == "Combat" ? "本场战斗（状态本来就是每场战斗一个新实例，天然重置）" : "本回合（换回合按回合号自动清零）") + "。");
            w.Line("/// </summary>");
            w.Line("private bool " + string.Join(", ", andTriggers.Select((_, i) => $"__andHit{i}")) + ";");
            if (power.AndScope != "Combat")
                w.Line("private int __andTurn;   // 上一次记下的回合数（变了就清标记 = 本回合窗口）");
        }

        // ==================== V0.2.248：「减少指定卡牌类别的费用」（腐化 / 刺破帷幕那种）====================
        // 用户原话：「减少指定卡牌类别的费用（类似腐化，刺破帷幕这些）」。
        // 本体同一个钩子（腐化 Corruption / 刺破帷幕 Veilpiercer / 免费攻击 FreeAttackPower 都用它）：
        //   AbstractModel.TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
        // 只在「范围」非空时生成（老存档 / 没配的状态一行都不写 → 逐字不变）。
        EmitCostModifier(w, power, profile);

        // 同一个**本体钩子方法**只能生成一个覆写（同名同参数写两个 → CS0111，整次构建失败）。
        // 所以按「钩子」分组，不是按「配置里的时机」分组：
        //   · 玩家回合结束时（TurnEnd）和敌人回合结束时（EnemyTurnEnd）都是 BeforeSideTurnEnd；
        //   · 衰减 / 移除也写在这个方法里。
        // 踩过的坑：只给 TurnEnd 做了合并，用户把时机改成「敌人回合结束时」＋「每回合衰减」时
        // 就会生成两个 BeforeSideTurnEnd → CS0111（实测报告）。
        var order = new List<string>();
        var byHook = new Dictionary<string, List<PowerTriggerSpec>>(StringComparer.Ordinal);
        foreach (var t in triggers)
        {
            // 老存档里可能是旧 id（TurnStart / TurnEnd / DamageTaken）→ 分组时按规范化后的 id 走
            //（**不改 profile 里的值**：界面上的下拉还用老值绑定，Find 认别名照样显示得出来）
            string hook = PowerHookOf(PowerTriggers.Canonical(t.Kind));
            if (!byHook.TryGetValue(hook, out var list)) { list = new List<PowerTriggerSpec>(); byHook[hook] = list; order.Add(hook); }
            list.Add(t);
        }

        // 「要和其他时机同时成立」那条链要知道「这个状态上还有哪几条时机勾了它」——
        // 钩子发射器埋在很多层下面（EmitTriggerMethod → EmitItemsWithAmountGate → EmitEffects），
        // 一层层加参数要改二十几个调用点，所以照 CSharpCodeGen._formulaOwner 那套放一个生成期的「当前状态」。
        var prevOwner = _powerOwner;
        var prevBody = _graphBody;
        _powerOwner = power;
        try
        {
            foreach (string hook in order)
            {
                var items = byHook[hook];
                // V0.2.194：图说了算时，「跑效果」这一步换成图上渲染好的正文（见 _graphBody）。
                // graphBodies 里只有「这个钩子**第一块**【触发时机】」那一条（同一个钩子上两块 →
                // 校验器报错拦住），所以这里取不到正文时就是空图 / 老路。
                _graphBody = graphBodies.TryGetValue(hook, out string? bodyText) ? bodyText : null;
                if (hook == "TurnEnd")
                {
                    // 回合结束这一类（玩家 / 敌人 / 都有）＋ 衰减 / 移除，合并成一个方法
                    EmitTurnEndMethod(w, items, power, profile);
                    continue;
                }
                EmitTriggerMethod(w, PowerTriggers.Canonical(items[0].Kind), items, profile);
            }

            // 只配了衰减 / 移除、没有回合结束类触发时机时，也要有那个方法
            if (!byHook.ContainsKey("TurnEnd") && (power.RemoveAtTurnEnd || power.DecayPerTurn > 0))
            {
                _graphBody = null;
                EmitTurnEndMethod(w, new List<PowerTriggerSpec>(), power, profile);
            }
        }
        finally
        {
            _powerOwner = prevOwner;
            _graphBody = prevBody;
        }

        w.Close();
        return w.ToString();
    }

    /// <summary>
    /// 一种触发时机最终落在哪个**本体钩子方法**上。同名 = 只能生成一个覆写（否则 CS0111）。
    ///
    /// ⚠ 这份清单是**手写**的，漏一对就会生成两个同名同参方法（用户实测报过两次：
    /// 最早只给「回合结束」做了合并 → TurnEnd + EnemyTurnEnd 撞 BeforeSideTurnEnd；
    /// V0.2.167 又补上「状态层数变化 / 到达」「受到伤害（自己/敌人）」「造成伤害（自己/敌人）」
    /// 「获得格挡（自己/敌人）」这 4 对）。**新增触发时机时，先看它的原型钩子签名和谁重复**。
    /// </summary>
    public static string PowerHookOf(string kind) => kind switch
    {
        "TurnEnd" or "EnemyTurnEnd" or "PlayerTurnEnd" => "TurnEnd",
        // 「战斗结束时（房间结算之前）」和「战斗胜利后」是**同一个本体钩子**（AfterCombatEnd）——
        // 归到同一组，只写一个方法（不然两个 override 同名同参数 = CS0111）。
        "CombatEnd" => "CombatVictory",
        // 下面 4 对都是「同一个钩子方法、两种时机」—— 合并成一组后由 TryEmitMergedHook
        // 写成一个方法 + 每条时机各自的守卫（items.Count >= 2 时才走那条路）。
        "PowerChanged" or "PowerReached" => "PowerChanged",              // AfterPowerAmountChanged
        "DamageReceived" or "EnemyDamaged" => "DamageReceived",          // AfterDamageReceived
        "DamageDealt" or "EnemyDealtDamage" => "DamageDealt",            // AfterDamageGiven
        "BlockGained" or "EnemyBlockGained" => "BlockGained",            // AfterBlockGained
        _ => kind,
    };

    /// <summary>
    /// 把「这个钩子分组里的每条触发时机」的各自效果发出来，并套上**触发时机上的数值**（用户要求）：
    ///   计数族（打牌 / 消耗 / 弃掉 / 抽到 / 生成）→ 每累计 N 次触发一次（本回合 / 本场战斗）；
    ///   阈值族（受到·造成伤害 / 失去生命 / 获得格挡）→ 数值 ≥ N 才触发。
    /// 没填数值的触发时机照旧**原样**发出（一个字符都不变）。
    /// </summary>
    /// <summary>「打出牌后」这一档是不是「不挑牌型」（老存档里可能存空串）。</summary>
    private static bool IsAnyCardFilter(string? v) => string.IsNullOrWhiteSpace(v) || v == "Any";

    /// <summary>
    /// 生成期的「当前自定义状态」（见 <c>CSharpCodeGen._formulaOwner</c> 的同款做法）：
    /// 「要和其他时机同时成立」要知道这个状态上还有哪几条时机勾了它，而钩子发射器埋得很深。
    /// </summary>
    [ThreadStatic] private static CustomPowerSpec? _powerOwner;

    /// <summary>
    /// V0.2.194：「状态列表」那一类布（锻造页用节点图编自定义状态）当前这一条钩子的**效果体正文**。
    ///
    /// 为什么用生成期字段而不是参数（和 <see cref="_powerOwner"/> 同一个理由）：
    /// 效果体埋在 <c>EmitTriggerMethod</c> → <c>EmitItemsWithAmountGate</c> / <c>EmitTurnEndMethod</c> /
    /// <c>EmitCardPlayedItems</c> 底下的三十几个调用点上，一路加参数要改三十几处；
    /// 而这些方法**只有「把效果发出来」这一步**要换 —— 换成一个字段，一处判断就够，
    /// 老路（null）逐字不变。用法见 <see cref="Source"/>（每个钩子发之前设，发完清）。
    /// </summary>
    [ThreadStatic] private static string? _graphBody;

    /// <summary>
    /// 「要和其他时机同时成立」（用户要求）：包一层「等所有参与时机都成立过才跑」。
    /// 返回 true = 已经包好了（调用方不用再包）。
    /// </summary>
    private static bool TryWrapRequireAll(CodeWriter w, PowerTriggerSpec t, Action body)
    {
        if (_powerOwner is not { } power || !t.RequireAllTriggers) return false;
        var all = power.AndTriggers;
        if (all.Count < 2)
        {
            // 只有这一条勾了 = 没有「别的时机」和它同时成立 → 按老行为跑（配置检查会给一句提示）
            body();
            return true;
        }
        int idx = -1;
        for (int i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], t)) { idx = i; break; }
        if (idx < 0) { body(); return true; }
        string flags = string.Join(" && ", all.Select((_, i) => $"__andHit{i}"));
        if (power.AndScope != "Combat")
        {
            // 本回合窗口：换回合先把标记清掉（按回合号认，不需要额外的重置钩子）
            w.Open("if (base.Owner.Player?.PlayerCombatState is { } __andPcs && __andPcs.TurnNumber != __andTurn)");
            w.Line($"__andTurn = __andPcs.TurnNumber;   // 新的一回合：把「同时成立」的标记清掉");
            w.Line(string.Join(" ", all.Select((_, i) => $"__andHit{i} = false;")));
            w.Close();
        }
        w.Line($"// 「要和其他时机同时成立」：这条已经成立（{idx + 1}/{all.Count}）");
        // 回读用的一行标记（放在这条时机的实现里：回读时这个钩子方法对应哪条时机，就把标记挂到它身上）
        w.Line($"// CET:RequireAll=1 CET:AndScope={power.AndScope}");
        w.Line($"__andHit{idx} = true;");
        w.Line("// 参与的那几条时机在同一个窗口里都成立过了");
        w.Open($"if ({flags})");
        body();
        w.Line(string.Join(" ", all.Select((_, i) => $"__andHit{i} = false;")) + "   // 这一轮用掉了，下个窗口重新凑");
        w.Close();
        return true;
    }

    /// <summary>插件触发时机要落的额外守卫（V0.2.316）：触发时机生成处设，效果体生成处消费一次。</summary>
    [ThreadStatic] private static string? _pendingPluginGuard;

    private static void EmitItemsWithAmountGate(CodeWriter w, IEnumerable<PowerTriggerSpec> items,
        CharacterProfile p, string? foeFrom = null, string? bodyText = null)
    {
        // 插件触发时机（别名）：先把清单里那句额外守卫落下来（只落一次，落完就清）
        if (_pendingPluginGuard is { } plugGuard)
        {
            w.Line(plugGuard + "   // 插件触发时机的额外守卫");
            _pendingPluginGuard = null;
        }
        // 先落到 List：下面要按「这个钩子里合并了几条触发时机」决定要不要给每条包裸块（≥2 条才包）。
        var list = items as List<PowerTriggerSpec> ?? items.ToList();
        bool ownScope = list.Count > 1;

        // V0.2.194（「状态列表」那一类布）：锻造节点图那条路**效果体是图上渲染好的正文** ——
        // 触发时机自己的数值闸门（每累计 N 次 / 数值 ≥ N）照旧由这里套上，只把「跑效果」那一步
        // 换成写这段正文（见 <see cref="Source"/> 的 graphBody）。bodyText = null 时逐字走老路。
        string? body = bodyText ?? _graphBody;
        // V0.2.318：图上的正文在整个钩子方法里**只落一次**（同一个钩子上合并多条触发时机时以前每条都
        // Raw 一遍 → 效果被执行两遍；EmitEffects 那次兜底也会再写一遍）。
        bool bodyWritten = false;
        void EmitOne(PowerTriggerSpec t, string? foe, bool scope)
        {
            if (body is not null)
            {
                if (!bodyWritten) { w.Raw(body); bodyWritten = true; }
                _graphBody = null;   // 交给这里了，别让 EmitEffects 再兜底写一遍
                return;
            }
            if (!TryWrapRequireAll(w, t, () => EmitEffects(w, t, foeFrom: foe, p: p, ownScope: scope)))
                EmitEffects(w, t, foeFrom: foe, p: p, ownScope: scope);
        }

        // 「每 N 个敌人死亡」的计数**由工具自己数**（本体战斗历史里没有死亡记录）：Bump 一次就是 +1。
        // 同一个钩子上有多条这种时机时，必须**只 bump 一次** —— 否则每次死亡会被数成 2 个 / 3 个，
        // 「每 2 个敌人死亡触发」就变成「每 1 个」（和 V0.2.211 修的那个「3+3 却只要 1+1」是同一类症状）。
        string? deathCountOnce = null;
        if (list.Count(x => x is not null && x.TriggerAmount > 0m
                && string.Equals(PowerTriggers.Canonical(x.Kind), "EnemyDeath", StringComparison.Ordinal)) > 1)
        {
            deathCountOnce = "__deathsNow";
            w.Line($"int {deathCountOnce} = {CSharpCodeGen.CombatCountersClass(p)}.BumpEnemyDeath(base.Owner.CombatState);"
                + "   // 这次死亡只数一次（同一个钩子上有多条「每 N 个敌人死亡」）");
        }

        foreach (var t in list)
        {
            string kind = PowerTriggers.Canonical(t.Kind);
            // ===== 「每累计 N 次触发一次」：**从这个状态被施加那一刻开始数** =====
            // 用户实测报的（V0.2.168）：「状态会直接统计整个回合（战斗）的计数，然后哪怕那时候还没上状态，
            // 也会计数……要打 4 张牌触发，但在上状态之前就打了 3 张，上状态之后只需要打 1 张就行了」。
            // 真因：本体历史（CombatManager.History）是整个回合 / 整场战斗的累计，老写法直接拿
            // `累计 % N == 0` 判 —— 上状态之前发生的事也算在里面了。
            // 修法：每个状态实例自己记一个**基准值**（刚上状态时历史里已经有多少次），只数基准之后的事件；
            // 「本回合」范围靠 `__cnt <= 基准` 自动重置（换回合后累计值会变小 → 重新记基准）。
            bool counting = t.TriggerAmount > 0m
                && (CSharpCodeGen.TriggerAmountNeedsScope(kind) || string.Equals(kind, "EnemyDeath", StringComparison.Ordinal));
            if (counting)
            {
                string field = CountBaseField(t, _powerOwner);
                string countExpr = string.Equals(kind, "EnemyDeath", StringComparison.Ordinal)
                    // 敌人死亡：本体历史里没有死亡记录 → 由工具自己记一个（见 CombatCountersSource）
                    ? (deathCountOnce ?? $"{CSharpCodeGen.CombatCountersClass(p)}.BumpEnemyDeath(base.Owner.CombatState)")
                    : CSharpCodeGen.CountExpr(kind, t.TriggerScope, "base.Owner", "base.Owner.CombatState", "base.Owner.Player",
                        t.CardFilter, currentCardExpr: "cardPlay.Card");   // V0.2.341：CardPlayed 要把正在打出的这一张补上
                string step = CSharpCodeGen.TriggerAmountForMarker(t.TriggerAmount).TrimEnd('m');
                w.Line($"// CET:TriggerAmount={CSharpCodeGen.TriggerAmountForMarker(t.TriggerAmount)} CET:TriggerScope={t.TriggerScope}");
                w.Open("");   // 裸块：__cnt 这个局部变量不和别条时机 / 外层撞名
                w.Line($"int __cnt = {countExpr};   // 本体历史里到现在为止一共有多少次（含正在打出的这一张）");
                w.Line($"if ({field} == int.MinValue || __cnt <= {field}) {field} = __cnt - 1;   // 本状态刚上 / 换了新回合：从现在开始数");
                // V0.2.173：外加 `__cnt > {field}` —— **施加这个状态的那一张牌不算**。
                // 本体现在是「先调 BeforeCardPlayed、再记 CardPlayStarted」（见 CardModel.Play），
                // 所以 CountExpr 里已经 +1 把正在打出的这一张补上了（V0.2.340 修正）；上状态的那一张
                // 早就在历史里了，于是它落在基准里、不参与计数 —— 不挡掉它的话「施加时正好凑够 N 张」会当场响一次。
                w.Open($"if (__cnt > {field} && (__cnt - {field}) % {step} == 0)");
                w.Line($"// 每累计 {t.TriggerAmount:0.##} 次触发一次（范围：{(t.TriggerScope == "Combat" ? "本场战斗" : "本回合")}，"
                     + "**从这个状态被施加那一刻**起数）");
                EmitOne(t, foeFrom, false);
                w.Close();
                w.Close();
                continue;
            }
            string? cond = CSharpCodeGen.TriggerAmountCond(kind, t.TriggerAmount, t.TriggerScope,
                "base.Owner", "base.Owner.CombatState", "base.Owner.Player", CSharpCodeGen.TriggerValueExpr(kind),
                t.CardFilter, currentCardExpr: "cardPlay.Card");
            if (cond is null)
            {
                // ⚠ 这一支**不套 if**：多条触发时机合并时，这里就是那个「帮助变量会撞车」的地方 → 必须各自包裸块。
                EmitOne(t, foeFrom, ownScope);
                continue;
            }
            w.Line($"// CET:TriggerAmount={CSharpCodeGen.TriggerAmountForMarker(t.TriggerAmount)} CET:TriggerScope={t.TriggerScope}");
            w.Open($"if ({cond})");
            EmitOne(t, foeFrom, ownScope);
            w.Close();
        }
    }

    /// <summary>
    /// 「花掉能量 / 花掉额外资源量」这两条时机的**数值闸门**（V0.2.219，用户实测报的
    /// 「每消耗 N 点能量 / 额外资源量，数值填 0 和填 1 的效果一模一样」）。
    ///
    /// <para>真因：以前「填 0」落进的是「每 1 点」那个分支（<c>SpendTickCond</c> 里 <c>Math.Max(1, 0)</c>），
    /// 于是 0 和 1 走同一条路 —— 一张 3 费牌触发 3 次。</para>
    ///
    /// <para>现在（用户要求的语义）：
    /// <list type="bullet">
    /// <item><b>填 0（留空）= 只要消耗就触发</b>，而且是**本回合 / 本场战斗里只触发第一次**
    ///   （「统计范围」那一栏决定窗口）。判据不需要任何计数器：本体在派发钩子**之前**就把这一笔记进战斗历史
    ///   （<c>CardModel.SpendEnergy</c>：先 <c>History.EnergySpent(...)</c> 再 <c>Hook.AfterEnergySpent(...)</c>），
    ///   所以「这一次之前花掉多少」= 历史累计 − 这一次的 amount；它 == 0 就说明这是本窗口的第一次消耗。
    ///   这比自己在状态里存标记更稳：清窗口 / 换回合 / 换战斗都由本体历史自己管，不会留脏标记。</item>
    /// <item><b>填 N = 每累计花掉 N 点触发一次</b>（按点数循环 + 余数判断）—— 老行为，输出逐字不变。</item>
    /// </list></para>
    /// </summary>
    private static void EmitSpendTriggers(CodeWriter w, string kind, string idxVar, List<PowerTriggerSpec> items,
        CharacterProfile? p)
    {
        var all = items.Where(x => x is not null).ToList();
        if (all.Count == 0) return;
        // 图说了算时（「状态列表」那一类布）效果体是图上渲染好的正文：闸门由图上自己的节点表达，
        // 这里照 EmitItemsWithAmountGate 的老路换成正文，只发一次。
        if (_graphBody is not null) { w.Raw(_graphBody); return; }
        var perPoint = all.Where(x => x.TriggerAmount > 0m).ToList();
        var plain = all.Where(x => x.TriggerAmount <= 0m).ToList();

        // 「这一次之前已经花掉多少」这个局部变量：只用一个范围时仍叫 __spentBefore（老存档逐字不变），
        // 同一个钩子上混了「本回合 / 本场战斗」两种范围时才按范围各起一个名字。
        var scopes = all.Select(SpendScopeOf).Distinct(StringComparer.Ordinal).ToList();
        string BeforeVar(string scope) => scopes.Count == 1
            ? "__spentBefore"
            : (scope == "Combat" ? "__spentBeforeCombat" : "__spentBeforeTurn");
        var declared = new HashSet<string>(StringComparer.Ordinal);
        // V0.2.336：把「这个状态被施加那一刻已经花掉多少」定成基准（第一次走到这里时记下），
        // 之后只数基准之后花掉的部分 —— 于是「每累计 N 点触发一次」不再把上状态之前的花销算进去。
        string BaseVar(string scope) => "__spentBase" + scope;
        void DeclareBefore(string scope)
        {
            if (!declared.Add(scope)) return;
            w.Line(CSharpCodeGen.SpendBeforeLine(kind, scope, "base.Owner", "base.Owner.CombatState", BeforeVar(scope)));
            w.Line($"int __spentNow{scope} = {BeforeVar(scope)} + amount;   // 含这一次一共花掉多少");
            w.Line($"if ({BaseVar(scope)} < 0 || __spentNow{scope} <= {BaseVar(scope)}) {BaseVar(scope)} = {BeforeVar(scope)};   // 施加时的基准（换回合会自己重记）");
        }
        // 跑一条时机自己的效果
        void Run(PowerTriggerSpec t) => EmitEffects(w, t, p: p);

        // ===== 0 档：只要消耗就触发，本回合 / 本场战斗只触发第一次 =====
        if (plain.Count > 0)
        {
            foreach (var t in plain)
            {
                string scope = SpendScopeOf(t);
                DeclareBefore(scope);
                w.Line($"// 数值填 0（留空）：只要消耗就触发一次（范围：{(scope == "Combat" ? "本场战斗" : "本回合")}"
                     + "，只触发第一次 —— 之后再消耗不重复）");
                w.Line($"// CET:TriggerAmount={CSharpCodeGen.TriggerAmountForMarker(t.TriggerAmount)} CET:TriggerScope={t.TriggerScope}");
                w.Open($"if ({BeforeVar(scope)} - {BaseVar(scope)} == 0 && amount > 0)");
                Run(t);
                w.Close();
            }
        }

        // ===== N 档：每累计花掉 N 点触发一次（按点数循环）=====
        if (perPoint.Count > 0)
        {
            w.Line(CSharpCodeGen.SpendThresholdComment(perPoint[0].TriggerAmount, perPoint[0].TriggerScope));
            foreach (var scope in perPoint.Select(SpendScopeOf).Distinct(StringComparer.Ordinal))
                DeclareBefore(scope);
            w.Open($"for (int {idxVar} = 0; {idxVar} < amount; {idxVar}++)");
            foreach (var t in perPoint)
            {
                string sc = SpendScopeOf(t);
                w.Open($"if (({BeforeVar(sc)} - {BaseVar(sc)} + {idxVar} + 1) % {CSharpCodeGen.TriggerAmountForMarker(t.TriggerAmount).TrimEnd('m')} == 0)");
                Run(t);
                w.Close();
            }
            w.Close();
        }
    }

    /// <summary>「花掉能量 / 花掉额外资源量」的统计范围归一一档（<c>Combat</c> = 本场战斗，其余 = 本回合）。</summary>
    private static string SpendScopeOf(PowerTriggerSpec t) =>
        string.Equals(t.TriggerScope, "Combat", StringComparison.Ordinal) ? "Combat" : "Turn";

    /// <summary>
    /// 「每累计 N 次触发一次」在本状态实例上的**基准值字段**名（-1 = 还没记过，见 <see cref="EmitItemsWithAmountGate"/>）。
    /// 名字里带时机 + 范围：同一只状态上两条不同时机 / 不同范围的计数互不影响。
    ///
    /// <para>V0.2.211（用户实测报的「触发次数填 3+3，但第一次只要 1+1，后面才是 3+3」）：
    /// 同一个状态上**两条「只对哪类牌生效」不同的计数时机**（例：打出攻击牌 3 次 + 打出技能牌 3 次）
    /// 以前共用同一个字段。两条的累计值本来就不一样，而两条各自都会执行
    /// <c>if (字段 &lt; 0 || 累计 &lt;= 字段) 字段 = 累计 - 1;</c> ——
    /// 后跑的那条会拿自己的「累计 - 1」把先跑那条记下的基准值**冲掉**（通常冲成更小的值），
    /// 于是先跑那条按错误的基准算「(累计 - 基准) % 3」，第一次只要 1 张就凑够了。
    /// 现在这种情况下字段名把筛选也编进去（<c>__cntCardPlayedTurnAttack</c> / <c>__cntCardPlayedTurnSkill</c>），
    /// 两条各记一份。**单一计数时机、或两条筛选相同**的存档字段名一个字都不变（老工程逐字一致）。</para>
    /// </summary>
    private static string CountBaseField(PowerTriggerSpec t, CustomPowerSpec? power)
    {
        string kind = PowerTriggers.Canonical(t.Kind);
        string name = "__cnt" + kind + (string.Equals(t.TriggerScope, "Combat", StringComparison.Ordinal) ? "Combat" : "Turn");
        return CountFieldNeedsFilter(power, kind, t.TriggerScope)
            ? name + Lit.Identifier(t.CardFilter ?? "", "")
            : name;
    }

    /// <summary>「只对哪类牌生效」归一一档：留空和 <c>Any</c> 是同一件事（都不筛）。</summary>
    private static string NormalizeCountFilter(string? v) =>
        string.IsNullOrWhiteSpace(v) || string.Equals(v.Trim(), "Any", StringComparison.Ordinal) ? "Any" : v.Trim();

    /// <summary>
    /// 这个状态上「同一条时机 + 同一个范围」的计数时机是不是**筛选不同**（是 → 字段名要带筛选）。
    /// 筛选相同 = 计数表达式也相同，共用一份基准值完全正确（两条同时到点、同时触发）。
    /// </summary>
    private static bool CountFieldNeedsFilter(CustomPowerSpec? power, string kind, string scope)
    {
        if (power is null) return false;
        var filters = power.Triggers
            .Where(x => x is not null && x.TriggerAmount > 0m
                && string.Equals(PowerTriggers.Canonical(x.Kind), kind, StringComparison.Ordinal)
                && string.Equals(x.TriggerScope, scope, StringComparison.Ordinal))
            .Select(x => NormalizeCountFilter(x.CardFilter))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return filters.Count > 1;
    }

    /// <summary>
    /// 这个自定义状态的触发时机要不要那个「生命真的少了」的**重入闸**。
    ///
    /// 什么时候要：它的触发时机里有「自己受到伤害后」（AfterDamageReceived）或「自己失去生命时」
    /// （AfterCurrentHpChanged），**而且**效果里有一条会去改生命（失去生命 / 造成伤害 / 最大生命降下来）。
    ///
    /// 为什么非加不可（用户实测报的）：本体 <c>CreatureCmd.Damage</c> 每打一下都会调
    /// <c>Hook.AfterDamageReceived</c> 和 <c>Hook.AfterCurrentHpChanged</c>，而状态自己的「失去生命」
    /// 效果正是走 <c>CreatureCmd.Damage</c> —— 于是：
    ///   敌人打你 1 下 → 状态触发「失去 1 点生命」→ 这 1 点生命**又是一次受到伤害** → 状态再触发 → …
    /// 一回合之内直接扣到死（用户原话「会循环到死」）。
    ///
    /// 为什么本体自己不踩这个坑：本体那些「生命少了才触发」的状态（肉骨头 MeatOnTheBone /
    /// NecroMasteryPower / 原体黏土 SelfFormingClay）效果里都**没有再造成伤害 / 失去生命**，
    /// 所以循环起不来 —— 这是「用户能配出本体配不出的组合」带来的新问题。
    ///
    /// 修法是**重入闸**而不是「过滤掉自己造成的伤害」：本体 <c>CreatureCmd.Damage</c> 的弹伤害
    /// 没有可供外部判别的「是我自己打的」标记（只有 dealer，而状态里 dealer 本来就可能为 null），
    /// 所以在钩子入口守一个 bool 最稳 —— 本状态自己的效果跑完就放掉，真实的下一次受伤照样触发。
    /// </summary>
    private static bool NeedsHpLossGuard(CustomPowerSpec power) => NeedsHpLossGuard(power.Triggers);

    /// <summary>同上，按一组触发时机判（生成单个钩子方法时用）。</summary>
    private static bool NeedsHpLossGuard(IEnumerable<PowerTriggerSpec> triggers)
    {
        var list = triggers.ToList();
        bool hurtTrigger = list.Any(t => t.Kind is "DamageTaken" or "HpLost");
        if (!hurtTrigger) return false;
        return list.Any(t => t.Effects.Any(e => e.Kind is "HpLoss" or "Damage" or "MaxHp" or "PetLoseHp"));
    }

    /// <summary>
    /// 打开重入闸（配 <see cref="EmitHpLossGuardClose"/> 用）：
    /// <code>
    /// if (__inHpLoss) return;
    /// __inHpLoss = true;
    /// try
    /// {
    ///     …效果…
    /// }
    /// finally { __inHpLoss = false; }
    /// </code>
    /// 为什么用 try/finally 而不是「跑完再置 false」：效果里可能 await 到一半被中断（战斗结束 / 取消），
    /// 那种情况也要把闸放掉，否则这只状态之后**再也不响应受伤**了（比循环更难查）。
    /// </summary>
    private static void EmitHpLossGuardOpen(CodeWriter w)
    {
        w.Line("if (__inHpLoss) return;   // 闸：本状态自己的「失去生命」又会触发这个钩子，不放行会扣血扣到死");
        w.Line("__inHpLoss = true;");
        w.Open("try");
    }

    /// <summary>关掉重入闸（见 <see cref="EmitHpLossGuardOpen"/>）。</summary>
    private static void EmitHpLossGuardClose(CodeWriter w)
    {
        w.Close();
        w.Line("finally { __inHpLoss = false; }");
    }

    /// <summary>回合结束这一类（玩家 / 敌人 / 都有）：触发器的效果先跑，然后才是衰减 / 移除。</summary>
    private static void EmitTurnEndMethod(CodeWriter w, List<PowerTriggerSpec> items, CustomPowerSpec power,
        CharacterProfile? p = null, string? bodyText = null)
    {
        // V0.2.194：锻造节点图那条路 —— 「跑效果」换成写图上渲染好的正文（bodyText），
        // 触发时机自己的守卫（哪一侧的回合结束）与衰减 / 移除照旧由这里写。null = 老路逐字不变。
        string? body = bodyText ?? _graphBody;
        void EmitOne(PowerTriggerSpec t, bool scope)
        {
            if (body is not null) { w.Raw(body); return; }
            EmitEffects(w, t, p: p, ownScope: scope);
        }

        w.Line();
        w.Line("// ===== 触发时机：回合结束时（触发器效果跑完再做衰减 / 移除）=====");

        // ⚠⚠ 玩家的「回合结束时」有**两个 id**：目录里现在叫 PlayerTurnEnd，老存档 / 老工程里叫 TurnEnd
        //（PowerTriggers.Canonical 会把后者规范化成前者，ProfileFactory.Load 读存档时也会改写成前者）。
        // 这里必须按**规范化后的 id** 认：以前只写 `t.Kind == "TurnEnd"`，于是**现在所有存档**
        //（存的是 PlayerTurnEnd）的这一条都被这个 Where 过滤掉 —— 方法照样生成、衰减 / 移除也在，
        // 就是**效果体整个是空的**（游戏里那条状态什么都不做，日志里也看不出问题）。V0.2.157 修。
        var playerSide = items.Where(t => PowerTriggers.Canonical(t.Kind) == "PlayerTurnEnd").ToList();
        var enemySide = items.Where(t => PowerTriggers.Canonical(t.Kind) == "EnemyTurnEnd").ToList();
        // 同一个钩子里合并了 ≥2 条触发时机时，每条各自包裸块（不然各条里的帮助变量会重复声明 → CS0128）
        bool multiPlayer = playerSide.Count > 1;
        bool multiEnemy = enemySide.Count > 1;

        if (power.RemoveAtTurnEnd)
        {
            w.Open("public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)");
            // 只配了「敌人回合结束时」时不能把玩家侧直接 return 掉（那样这一整个方法就白写了）
            w.Line(enemySide.Count > 0
                ? "if (side != CombatSide.Player && side != CombatSide.Enemy) return;"
                : "if (side != CombatSide.Player) return;");
            foreach (var t in playerSide) EmitOne(t, multiPlayer);
            if (enemySide.Count > 0)
            {
                w.Line();
                w.Line("// 敌人回合结束时配的那几条");
                w.Open("if (side == CombatSide.Enemy)");
                foreach (var t in enemySide) EmitOne(t, multiEnemy);
                w.Close();
            }
            w.Line();
            EmitDecay(w, power, hasChoiceContext: true);
            w.Close();
            return;
        }

        w.Open("public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)");
        if (enemySide.Count == 0)
        {
            w.Line("if (side != CombatSide.Player) return;");
            foreach (var t in playerSide) EmitOne(t, multiPlayer);
        }
        else
        {
            // 两种侧都配了：玩家侧跑玩家侧那几条，敌人侧跑敌人侧那几条（不能提前 return，否则敌人侧永远不执行）
            w.Open("if (side == CombatSide.Player)");
            foreach (var t in playerSide) EmitOne(t, multiPlayer);
            w.Close();
            w.Line("else if (side == CombatSide.Enemy)");
            w.Open("");
            foreach (var t in enemySide) EmitOne(t, multiEnemy);
            w.Close();
        }

        // 衰减 / 移除：走 PowerCmd（才会通知「层数变化后」那些钩子，见 EmitDecay 的注释）
        EmitDecay(w, power, hasChoiceContext: true);
        w.Close();
    }

    /// <summary>把一种触发时机（可能配了好几条）生成成一个钩子方法。</summary>
    /// <summary>
    /// 正在生成的那只自定义状态**自己**的类名（给「盯自己这个状态」这类判断用）。
    /// 拿不到（生成期没设 <see cref="_powerOwner"/> / 找不到序号）时返回空串 —— 调用方按「不是自己」处理。
    /// </summary>
    private static string SelfClassName(CharacterProfile? p)
    {
        if (p is null || _powerOwner is null) return "";
        int idx = p.CustomPowers.IndexOf(_powerOwner);
        return idx < 0 ? "" : ClassNameOf(p, _powerOwner, idx);
    }

    /// <summary>
    /// 同一个本体钩子上合并了 ≥2 条触发时机时用的发射器（V0.2.167 修 CS0111）。
    ///
    /// <para>为什么要它：本体一个类里**同名同参的方法只能有一个**，而这几对时机各自落在同一个签名上 ——
    /// 两条一起配时旧代码会写出两个 override →
    /// <c>CS0111 已定义了一个名为"X"的具有相同参数类型的成员</c>（用户实测报过：
    /// <c>Hakking.cs(47,32) AfterPowerAmountChanged</c>；`HookOf` 那份手写并组清单只并了
    /// 「回合结束三兄弟」和「战斗胜利/结束」两对，漏了这里这 4 对）。</para>
    ///
    /// <para>守卫写成**正向条件**（<c>if (…) { … }</c>）而不是原来的 <c>if (…) return;</c>：
    /// 一个方法里有多条时机时，`return` 会把排在后面的时机一起跳过。</para>
    ///
    /// <para>⚠ 单条触发时机**不走这里**（那份带 `return` 的守卫文本逐字不变）—— 那些守卫同时是
    /// 「从工程反推存档」认时机用的（<see cref="ProjectRecovery"/> 按钩子签名 + 守卫认），改了会破坏回读。
    /// 合并这条路以前生成出来**编译不过**（CS0111），所以不存在「老工程是这条路生成的」。</para>
    /// </summary>
    private static bool TryEmitMergedHook(CodeWriter w, string kind, List<PowerTriggerSpec> items,
        CharacterProfile? p, bool hpLossGuard)
    {
        if (items.Count < 2) return false;
        switch (PowerHookOf(kind))
        {
            // 「自己受到伤害后」+「敌人受到伤害后」→ AfterDamageReceived
            case "DamageReceived":
                w.Open("public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)");
                EmitChoiceContextBootstrap(w, kind, items);
                foreach (var t in items)
                {
                    if (t.Kind == "EnemyDamaged")
                    {
                        w.Line("// 触发时机：敌人受到伤害后 —— 受伤的是敌人（自己不算）");
                        w.Open("if (target != base.Owner && target.IsAlive)");
                        EmitItemsWithAmountGate(w, new[] { t }, p, foeFrom: "target");
                        w.Close();
                    }
                    else
                    {
                        // 三条正向条件 = 原来那三句 `return`：只认自己 / 「失去生命」不算受伤 / 全被格挡不算 / 自己没死
                        w.Line("// 触发时机：自己受到伤害后 —— 只认自己（「失去生命」不算受伤；全被格挡那一下也不算；自己死了不再跑）");
                        w.Open("if (target == base.Owner"
                             + " && !(props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered))"
                             + " && result.UnblockedDamage > 0 && !base.Owner.IsDead)");
                        if (hpLossGuard) EmitHpLossGuardOpen(w);
                        EmitItemsWithAmountGate(w, new[] { t }, p, foeFrom: "dealer");
                        if (hpLossGuard) EmitHpLossGuardClose(w);
                        w.Close();
                    }
                }
                w.Close();
                return true;

            // 「自己造成伤害后」+「敌人造成伤害后」→ AfterDamageGiven
            case "DamageDealt":
                w.Open("public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)");
                EmitChoiceContextBootstrap(w, kind, items);
                foreach (var t in items)
                {
                    if (t.Kind == "EnemyDealtDamage")
                    {
                        w.Line("// 触发时机：敌人造成伤害后 —— 打人的是敌人（自己不算）");
                        w.Open("if (dealer is not null && dealer != base.Owner)");
                        EmitItemsWithAmountGate(w, new[] { t }, p, foeFrom: "dealer");
                        w.Close();
                    }
                    else
                    {
                        w.Line("// 触发时机：自己造成伤害后 —— 「失去生命」不算造成伤害");
                        w.Open("if (dealer == base.Owner"
                             + " && !(props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered)))");
                        EmitItemsWithAmountGate(w, new[] { t }, p, foeFrom: "target");
                        w.Close();
                    }
                }
                w.Close();
                return true;

            // 「自己获得格挡后」+「敌人获得格挡后」→ AfterBlockGained
            case "BlockGained":
                w.Open("public override async Task AfterBlockGained(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)");
                EmitChoiceContextBootstrap(w, kind, items);
                foreach (var t in items)
                {
                    bool enemy = t.Kind == "EnemyBlockGained";
                    w.Line(enemy
                        ? "// 触发时机：敌人获得格挡后 —— 拿格挡的是敌人（自己不算）"
                        : "// 触发时机：自己获得格挡后 —— 只认自己");
                    w.Open(enemy ? "if (creature != base.Owner)" : "if (creature == base.Owner)");
                    if (enemy) EmitItemsWithAmountGate(w, new[] { t }, p, foeFrom: "creature");
                    else EmitItemsWithAmountGate(w, new[] { t }, p);
                    w.Close();
                }
                w.Close();
                return true;

            // 「某个状态层数变化后」+「某个状态层数到达 N 层时」→ AfterPowerAmountChanged
            case "PowerChanged":
                w.Open("public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)");
                EmitChoiceContextBootstrap(w, kind, items);
                foreach (var t in items)
                {
                    // 每条各自判断「盯谁 + 盯哪个状态」：不能写成函数开头的统一 return ——
                    // 那样两种对象（自己 / 任意敌人）会互相把对方挡掉（实测生成出来敌人那条永远不跑）。
                    string ownerGuard = PowerWatchGuard(t, p, excludeSelf: true);
                    string? want = string.IsNullOrWhiteSpace(t.PowerId) ? null : t.PowerId.Trim();
                    if (t.Kind == "PowerReached")
                    {
                        string reach = Lit.Dec(Math.Max(0m, t.TriggerAmount));
                        w.Open($"if ({ownerGuard}{(want is null ? "" : $" && power is {Lit.Identifier(want, "VulnerablePower")}")}"
                             + $" && power.Amount >= {reach} && power.Amount - amount < {reach})");
                        w.Line($"// 层数跨过 {Math.Max(0m, t.TriggerAmount):0.##} 那一下才触发");
                        EmitEffects(w, t, p: p);
                        w.Close();
                    }
                    else
                    {
                        string guard = want is null ? ownerGuard : $"({ownerGuard}) && (power is {want})";
                        // V0.2.266（用户要求）：细分「层数上升 / 下降」—— amount 是这次变化的增量
                        if (string.Equals(t.PowerDirection, "Up", StringComparison.Ordinal))
                            guard += " && amount > 0m";
                        else if (string.Equals(t.PowerDirection, "Down", StringComparison.Ordinal))
                            guard += " && amount < 0m";
                        string what = want is null ? "任意状态" : EffectCatalog.PowerName(want, want);
                        string dirZh = t.PowerDirection == "Up" ? "（只要上升）" : t.PowerDirection == "Down" ? "（只要下降）" : "";
                        w.Line($"// 盯：{PowerTriggers.WatchTargetZh(t.PowerTarget)}的{what}{dirZh}");
                        w.Open($"if ({guard})");
                        EmitEffects(w, t, p: p);
                        w.Close();
                    }
                }
                w.Close();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 「某个状态层数变化后 / 到达 N 层时 / 被移除时」这三条**盯某个状态**的时机，共用的「盯谁」守卫。
    ///
    /// <para>为什么要抽这一份：四个调用点（合并钩子里的变化 / 到达、单条的变化、单条的到达、单条的移除）
    /// 以前各写一遍，于是「盯任意敌人」那一支慢慢漂了 —— V0.2.168 用户实测报的
    /// 「状态页上配『某个状态被移除时』（盯任意敌人、盯的就是它自己）**完全没反应**」就是这个坑：
    /// 那一支写着 <c>power.Owner != base.Owner</c>，等于要求「发生事的那份状态在**别的**生物身上」，
    /// 而用户要的是「**这份状态自己**（挂在谁身上就盯谁）被移除时」——
    /// 状态自己那一份被移除时 <c>power.Owner == base.Owner</c>，被这条挡得死死的，
    /// 生成、编译、进游戏全都不报错，就是什么都不做。</para>
    ///
    /// <para>规则：<b>盯的就是它自己这个状态</b>（「盯哪个状态」填的正是这只状态自己的类名）时，
    /// 不管「盯谁」选的是「自己」还是「任意敌人」，都按「**这份状态自己**」算 ——
    /// 自己这一份发生的事就在自己这里触发（「任意敌人」＝「哪个敌人身上这份状态出事都算，
    /// 包括我自己挂着的这份」）。盯的是**别的**状态时，「任意敌人」照旧要求那份状态挂在别的生物身上
    /// （那才是「盯敌人身上的易伤」的意思）。</para>
    /// </summary>
    /// <param name="excludeSelf">不是「盯自己」时要不要排掉自己（「层数变化 / 到达」要排 —— 免得自己给自己叠层自己触发自己；
    /// 「被移除」不排 —— 「我这个状态自己被移除」正是那条时机要覆盖的场景）。</param>
    private static string PowerWatchGuard(PowerTriggerSpec t, CharacterProfile? p, bool excludeSelf)
    {
        string? want = string.IsNullOrWhiteSpace(t.PowerId) ? null : t.PowerId.Trim();
        string selfClass = SelfClassName(p);
        bool watchesSelf = want is not null && string.Equals(want, selfClass, StringComparison.Ordinal);
        // 盯的就是它自己这个状态：不挑「在谁身上」——这份状态挂在谁身上，就在谁那里触发
        if (watchesSelf) return "power.Owner == base.Owner";
        if (t.PowerTarget == "AnyEnemy")
            return "power.Owner is not null && power.Owner != base.Owner && power.Owner.IsAlive"
                + " && base.Owner.CombatState is not null && base.Owner.CombatState.HittableEnemies.Contains(power.Owner)";
        return excludeSelf ? "power != this && power.Owner == base.Owner" : "power.Owner == base.Owner";
    }

    /// <summary>
    /// V0.2.248（用户要求「减少指定卡牌类别的费用（类似腐化，刺破帷幕这些）」）：
    /// 给这只状态生成 <c>TryModifyEnergyCostInCombatLate</c>。「范围」留空 → 一行都不写。
    ///
    /// <para>判据和「范围界定」共用一份（<see cref="CSharpCodeGen.ScopeCondForFilter"/>：攻击 / 技能 / 能力 /
    /// 诅咒 / 状态 / 任务 / 自定义关键词那一组 / 每个组），变量名用 <c>card</c>（这个钩子给的就是那张牌）。</para>
    ///
    /// <para>⚠ 两个必须做的事（和本体 腐化/刺破帷幕 一致）：
    /// ① 只改**你自己的**牌 —— `card.Owner.Creature != base.Owner` 直接放行；
    /// ② 最低减到 0 —— `Math.Max(0m, 原价 - N)`（本体「腐化」干脆写死 0；
    /// 「刺破帷幕」也是 0）。填 0 表示「变成 0 费」，和腐化一模一样。</para>
    /// </summary>
    private static void EmitCostModifier(CodeWriter w, CustomPowerSpec power, CharacterProfile profile)
    {
        string scope = (power.CostReduceScope ?? "").Trim();
        if (scope.Length == 0) return;
        string? cond = CSharpCodeGen.ScopeCondForFilter(scope, profile, "card");
        if (cond is null) return;   // 认不出来的范围 Id：校验器会报错，这里不生成（不静默生成错判据）
        decimal amount = power.CostReduceAmount;
        string expr;
        // V0.2.272（用户报「开了这个效果会让状态牌变成 0 费」）：以前 **0 也算「变成 0 费」**，
        // 而界面上的数字框留空 / 留 0 是最常见的手势 —— 于是「只想减 1 费」直接变成「这类牌全部 0 费」。
        // 现在：0 = 不修正（什么都不生成）；**负数** = 变成 0 费（腐化 / 刺破帷幕那档）。
        if (amount == 0m) return;
        if (amount < 0m)
            expr = "0m";        // 变成 0 费（腐化 / 刺破帷幕那档）
        else
        {
            string n = amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "m";
            expr = power.CostReducePerStack
                ? $"System.Math.Max(0m, originalCost - base.Amount * {n})"
                : $"System.Math.Max(0m, originalCost - {n})";
        }
        string scopeZh = EffectCatalog.SpawnFilterZhFor(profile, scope);
        w.Line();
        w.Line("/// <summary>");
        w.Line($"/// 费用修正（V0.2.248）：「{scopeZh}」这类牌在你手上时 {(amount < 0m ? "费用变成 0" : $"费用 -{amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}")}"
            + (power.CostReducePerStack && amount > 0m ? " × 层数" : "") + "。");
        w.Line("/// 用的是本体的 TryModifyEnergyCostInCombatLate —— 腐化 Corruption / 刺破帷幕 Veilpiercer 同一个钩子。");
        w.Line("/// </summary>");
        w.Open("public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)");
        w.Line("modifiedCost = originalCost;");
        w.Line("if (card.Owner.Creature != base.Owner) return false;   // 只改你自己的牌（别人的牌不看这个状态）");
        w.Open($"if (!({cond}))");
        w.Line("return false;");
        w.Close();
        w.Line($"modifiedCost = {expr};   // 最低 0 费，不会变成负费");
        w.Line("return true;");
        w.Close();
    }

    /// <summary>
    /// V0.2.362：把插件声明的**自定义钩子**写成状态类里的一个 override。
    /// 签名由插件给（方法名 + 参数），这里只负责：补 choiceContext（签名里要了才补）、守卫、效果体。
    /// </summary>
    private static void EmitCustomHookMethod(CodeWriter w, string kind, List<PowerTriggerSpec> items,
        CharacterProfile? p, PluginTriggerSpec plug)
    {
        string sig = plug.Signature.Trim();
        string when = PowerTriggers.Find(kind)?.Display ?? kind;
        w.Line();
        w.Line($"// ===== 触发时机：{when}（插件自定义钩子 {plug.Hook}）=====");
        w.Open("public override " + sig);
        // 签名里声明了 choiceContext 才补它（否则会 CS0103 / 多余的局部变量）
        if (sig.Contains("PlayerChoiceContext choiceContext", StringComparison.Ordinal))
            EmitChoiceContextBootstrap(w, kind, items);
        if (!string.IsNullOrWhiteSpace(plug.Guard))
        {
            w.Line(plug.Guard.Trim());
        }
        EmitItemsWithAmountGate(w, items, p);
        w.Close();
    }
    private static void EmitTriggerMethod(CodeWriter w, string kind, List<PowerTriggerSpec> items,
        CharacterProfile? p = null)
    {
        // 「生命真的少了」那条链的重入闸（见 NeedsHpLossGuard）：字段是按**整只状态**配的，
        // 所以这里按「这只状态的所有触发时机」重算一次 —— 不能只看当前这一组 items
        //（闸是在 DamageTaken / HpLost 那两个方法里用的，而「要不要开闸」由整只状态决定）。
        bool hpLossGuard = NeedsHpLossGuard(items);
        string when = PowerTriggers.Find(kind)?.Display ?? kind;
        // V0.2.316：插件贡献的触发时机 = 「本体已有的某个钩子换名字 + 一句额外守卫」的别名。
        // 这样插件不用改编辑器的钩子生成器（也就不会因编辑器升级失效）；
        // 守卫由下面第一个效果体（EmitItemsWithAmountGate）落一行。
        _pendingPluginGuard = null;
        if (Plugins.PluginRegistry.TriggerOf(kind) is { } plugTr)
        {
            // V0.2.362：插件写了 signature = **自定义触发时机** —— 原样把它的 override 写进来，
            // 于是「本体有、编辑器没收录」的钩子也能用（写错是编译期报错，不会静默失效）。
            if (!string.IsNullOrWhiteSpace(plugTr.Signature))
            {
                EmitCustomHookMethod(w, kind, items, p, plugTr);
                return;
            }
            kind = plugTr.Hook.Trim();
            _pendingPluginGuard = string.IsNullOrWhiteSpace(plugTr.Guard) ? null : plugTr.Guard.Trim();
        }
        w.Line();
        w.Line($"// ===== 触发时机：{when} =====");

        // 同一个本体钩子上合并了 ≥2 条触发时机时，只写**一个** override（各条自己带正向守卫）——
        // 两条各写一个同名同参方法 = CS0111（见 TryEmitMergedHook）。单条时一个字符都不变。
        if (TryEmitMergedHook(w, kind, items, p, hpLossGuard)) return;

        switch (kind)
        {
            case "PlayerTurnStart":
                w.Open("public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)");
                w.Line("if (player != base.Owner.Player) return;");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            // V0.2.179（用户要求「触发时机加一个回合开始后」）：回合开始那三个时刻是**三个不同**的钩子方法
            //（本体 Hook.AfterPlayerTurnStart 里连着派发三次，见 Hook.cs:894-917），所以各写各的 override，
            // 不会撞签名。归属判据和上面那条一样：只有自己的回合开始了才跑。
            case "PlayerTurnStartEarly":
                w.Open("public override async Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)");
                w.Line("if (player != base.Owner.Player) return;");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            case "PlayerTurnStartLate":
                w.Open("public override async Task AfterPlayerTurnStartLate(PlayerChoiceContext choiceContext, Player player)");
                w.Line("if (player != base.Owner.Player) return;");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            // 「打出一张牌时」= **本体的 BeforeCardPlayed**（这张牌还没结算）。
            // ⚠ V0.2.157：以前这里用的是 AfterCardPlayed（结算完才回调）—— 于是「打出这张牌才施加的状态」
            // 会在**同一张牌**上立刻触发一次（用户实测报的）。BeforeCardPlayed 在 OnPlay 之前派发
            //（见 CardModel：Hook.BeforeCardPlayed → OnPlay → … → Hook.AfterCardPlayed），
            // 那时这张牌的效果还没跑，所以它自己施加的状态还不存在 → 不会被自己触发。
            // 「结算完」那一档是 CardResolved（AfterCardPlayedLate），语义不丢。
            case "CardPlayed":
                w.Open("public override async Task BeforeCardPlayed(CardPlay cardPlay)");
                // 这个钩子本体**没给 choiceContext**（签名只有 cardPlay）→ 需要时按本体做法自己造一个
                EmitChoiceContextBootstrap(w, "CardPlayed", items);
                EmitCardPlayedItems(w, items, p);
                w.Close();
                break;

            // 「一张牌结算完毕时」= 本体的 AfterCardPlayedLate（牌已经生效完）—— 旧版的「打出一张牌后」
            case "CardResolved":
                w.Open("public override async Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay)");
                EmitCardPlayedItems(w, items, p);
                w.Close();
                break;

            case "CardExhausted":
                w.Open("public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)");
                w.Line("if (card?.Owner?.Creature != base.Owner) return;   // V0.2.282: only my own cards (co-op teammates excluded)");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            case "CardDrawn":
                w.Open("public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)");
                w.Line("if (card?.Owner?.Creature != base.Owner) return;   // V0.2.282: only my own cards (co-op teammates excluded)");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            case "CardDiscarded":
                w.Open("public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)");
                w.Line("if (card?.Owner?.Creature != base.Owner) return;   // V0.2.282: only my own cards (co-op teammates excluded)");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            case "DamageReceived":
                w.Open("public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)");
                w.Line("if (target != base.Owner) return;");
                // 「失去生命」不算「受到伤害」：本体 DamageProps.cardHpLoss = Unblockable | Unpowered
                //（血祭 / 献祭 / 中毒这类穿盾且没有来源加成的掉血）—— 它由「自己失去生命时」那条时机负责，别两边都触发。
                // 注意别把它和「对自己造成伤害」搞混：那条生成的是 Unpowered | Move（**格挡能挡**），
                // 不含 Unblockable → 不会被这一句挡掉，照旧算「受到伤害」。
                w.Line("if (props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered)) return;");
                // 本体「原体黏土 SelfFormingClay」就是这么多判一句的：全被格挡 / 0 伤害的那一下不算「受到伤害」。
                // 注意这个钩子**每一下伤害都会进**：敌人一次「1 点 × 5 下」的连击会触发 5 次
                //（想「一次攻击只算一次」用下面的「自己受到攻击后」）。
                w.Line("if (result.UnblockedDamage <= 0) return;   // 这一下全被格挡 / 0 伤害：不算受到伤害");
                // V0.2.139：**自己已经死了就别再跑**。这个钩子在 CreatureCmd.Damage 里是**无条件**广播的
                //（扣血那一步之后才派发），所以「被敌人打死的那一下」照样会进来 —— 效果里只要有
                // 「回复生命」，就会把 HP 从 0 拉回正数：多人模式下队友还活着，于是看起来就是「复活」。
                // 单人模式一样生效（致死那一下不再触发本状态的效果）—— 这是预期语义。
                // 位置：在「只认自己」（上面那句）**之后**，因为 base.Owner 只有在确认「挨打的是自己」之后
                // 才保证是本人；也在效果之前。状态链的 base.Owner 本身就是 Creature。
                w.Line("if (base.Owner.IsDead) return;   // 自己已经死了：致死那一下不再跑（否则「回复生命」会把人拉回来）");
                if (hpLossGuard) EmitHpLossGuardOpen(w);
                EmitItemsWithAmountGate(w, items, p, foeFrom: "dealer");
                if (hpLossGuard) EmitHpLossGuardClose(w);
                w.Close();
                break;

            // 自己失去生命时（本体 AfterCurrentHpChanged：delta < 0 = 生命真的少了，回血是正数）。
            // 这个钩子**不带 choiceContext**，所以照 BlockGained 那套先 bootstrap 一个（不然配了
            // 造成伤害 / 抽牌 / 施加状态就会 CS0103）。本体也是这么写的：
            // NecroMasteryPower 判 `!(delta >= 0m)`，肉骨头 MeatOnTheBone 只拿它更新遗物状态。
            case "HpLost":
                w.Open("public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)");
                EmitChoiceContextBootstrap(w, "HpLost", items);
                w.Line("if (creature != base.Owner) return;   // 只认自己掉血");
                w.Line("if (delta >= 0m) return;   // delta 为正是回血，不算「失去生命」");
                // V0.2.139：同 DamageReceived —— 致死那一下这个钩子也会进来（delta 正好是负的），
                // 不挡的话「失去生命时回复生命」会在自己死后把血拉回来（多人模式下 = 复活）。
                w.Line("if (base.Owner.IsDead) return;   // 自己已经死了：致死那一下不再跑（否则「回复生命」会把人拉回来）");
                if (hpLossGuard) EmitHpLossGuardOpen(w);
                EmitItemsWithAmountGate(w, items, p);
                if (hpLossGuard) EmitHpLossGuardClose(w);
                w.Close();
                break;

            // 自己受到攻击后（连击只算一次）：本体 AfterAttack 在**一次攻击的所有命中都结束之后**才跑一次
            //（AbstractModel.AfterAttack 的注释就是这么写的），所以敌人的多次连击在这里只触发一次。
            case "Attacked":
                // AttackCommand 在 MegaCrit.Sts2.Core.Commands.Builders，生成的 GlobalUsings 里没有这个命名空间
                //（卡牌代码只用到 DamageCmd）—— 必须写全名，不然生成的工程 CS0246。
                w.Open("public override async Task AfterAttack(PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Commands.Builders.AttackCommand command)");
                w.Line("// 多次连击只算一次：本体的 AfterAttack 是所有命中跑完之后才调一次");
                w.Line("decimal __takenThisAttack = command.Results.SelectMany(hits => hits).Where(r => r.Receiver == base.Owner).Sum(r => r.UnblockedDamage);");
                w.Line("if (__takenThisAttack <= 0) return;   // 这一次攻击没真的打到我（全被格挡 / 打的是别人）");
                // V0.2.139：本体 AfterAttack 在**一次攻击的所有命中都结束之后**才跑一次 —— 那么
                //「这一击把我打死了」也是在这个时点才调到这里（__takenThisAttack 还是 > 0）。
                // 不挡的话「受到攻击后回复生命」会把自己从 0 血拉回来（多人模式下 = 复活）。
                w.Line("if (base.Owner.IsDead) return;   // 自己已经死了：致死那一下不再跑（否则「回复生命」会把人拉回来）");
                EmitItemsWithAmountGate(w, items, p, foeFrom: "command.Attacker");
                w.Close();
                break;

            case "DamageDealt":
                w.Open("public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)");
                w.Line("if (dealer != base.Owner) return;");
                // 「失去生命」不算「受到伤害」，也就不算「自己造成伤害」（同上：本体 DamageProps.cardHpLoss =
                // Unblockable | Unpowered；「对自己造成伤害」是 Unpowered | Move，不含 Unblockable → 照旧算）。
                w.Line("if (props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered)) return;");
                EmitItemsWithAmountGate(w, items, p, foeFrom: "target");
                w.Close();
                break;

            case "BlockGained":
                w.Open("public override async Task AfterBlockGained(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)");
                // 本体的 AfterBlockGained **不带 choiceContext**：这条分支以前漏了 bootstrap，
                // 于是「自己获得格挡后」只要配了「造成伤害 / 抽牌 / 施加状态 / 额外回合」，
                // 生成出来的代码就会 CS0103（当前上下文中不存在名称"choiceContext"）——用户实测报过。
                EmitChoiceContextBootstrap(w, "BlockGained", items);
                w.Line("if (creature != base.Owner) return;");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            case "EnemyDeath":
                w.Open("public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)");
                w.Line("if (!creature.IsEnemy) return;");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            case "CombatStart":
                w.Open("public override async Task BeforeCombatStart()");
                EmitChoiceContextBootstrap(w, "CombatStart", items);
                w.Line("await Task.CompletedTask;   // 钩子是 async 的，保持签名一致");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            case "EnergySpent":
                w.Open("public override async Task AfterEnergySpent(CardModel card, int amount)");
                EmitChoiceContextBootstrap(w, "EnergySpent", items);
                // 本体这条钩子（AbstractModel.AfterEnergySpent，本体卡 CardModel.SpendEnergy 里派发）：
                //   · **每打出一张牌都会调一次，0 费也会调**（本体源码里 `amount > 0` 只包住了历史记录和扣能量，
                //     钩子调用在外面）→ 必须自己挡掉 amount <= 0；
                //   · 参数的 amount 是**这一张牌一共花的能量**（X 费牌 = 当前全部能量），
                //     所以「每消耗一个能量」要按 amount 跑 amount 次（本体 OrbitPower 也是按点数累加的）。
                //   · 这条钩子**不带 Player 参数**，「是谁花的」只能从 card.Owner 认
                //     （本体 EnvenomPower / OrbitPower 都是 `card.Owner...== base.Owner` 这么判的）——
                //     以前这里漏了这个守卫：联机时别人打牌也会触发你的状态（实测就是这个问题）。
                w.Line("if (amount <= 0 || card?.Owner?.Creature != base.Owner) return;   // 只认「我方角色花的能量」");
                w.Line("await Task.CompletedTask;   // 钩子是 async 的：一个需要 await 的效果都没有时保持签名一致");
                EmitSpendTriggers(w, "EnergySpent", "__energySpentIdx", items, p);
                w.Close();
                break;

            case "StarsSpent":
                w.Open("public override async Task AfterStarsSpent(int amount, Player spender)");
                EmitChoiceContextBootstrap(w, "StarsSpent", items);
                w.Line("if (amount <= 0 || spender != base.Owner.Player) return;");
                w.Line("await Task.CompletedTask;   // 钩子是 async 的：一个需要 await 的效果都没有时保持签名一致");
                EmitSpendTriggers(w, "StarsSpent", "__starsSpentIdx", items, p);
                w.Close();
                break;

            case "CardGenerated":
                // 生成卡牌时（用户要求）：本体 AfterCardGeneratedForCombat(CardModel card, Player? creator) ——
                // 钩子**不带 choiceContext**（见 TriggerHasChoiceContext），需要时自己造一个；
                // 本体注释写明「只算玩家自己生成的牌」（敌人塞给你的状态牌不算），而且 creator 可能为 null。
                w.Open("public override async Task AfterCardGeneratedForCombat(CardModel card, Player? creator)");
                EmitChoiceContextBootstrap(w, "CardGenerated", items);
                w.Line("if (creator is null || creator.Creature != base.Owner) return;   // 只认「我方角色主动生成」的牌");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            case "CombatVictory":
            case "CombatEnd":      // 同一个钩子（HookOf 把 CombatEnd 归到这里），共用这个方法
                // 用 AfterCombatEnd 而不是 AfterCombatVictory：本体的「王国资产」（RoyaltiesPower）就是这么写的，
                // 而且卡牌奖励要挂在 room 的奖励集合上（AddExtraReward），这个钩子里正好有 CombatRoom。
                w.Open("public override async Task AfterCombatEnd(CombatRoom room)");
                EmitChoiceContextBootstrap(w, "CombatVictory", items, roomInScope: true);
                w.Line("// 战斗结算之前（本体 AfterCombatEnd）：打赢才会走到这里（打输走 ProcessPendingLoss，不调模型钩子）");
                // 「战斗结束时」那条效果也在这里跑（同一个钩子、同一个时机窗口）
                foreach (var tr in items)
                {
                    // V0.2.194：图说了算时，「跑效果」换成图上渲染好的正文
                    if (_graphBody is not null) { w.Raw(_graphBody); continue; }
                    if (tr.Kind == "CombatEnd")
                        w.Line($"// 触发时机：战斗结束时（房间结算之前）→「{string.Join("、", tr.Effects.Select(x => EffectCatalog.FindKind(x.Kind).Display))}」");
                    // 「战斗胜利时 / 战斗结束时」共用一个钩子 → ≥2 条时每条各自包裸块（避免帮助变量重复声明）
                    EmitEffects(w, tr, roomVar: "room", p: p, ownScope: items.Count > 1);
                }
                w.Close();
                break;

            // 「某个状态层数到达 N 层时」（用户要求：两边菜单相同）：和「某个状态层数变化后」同一个钩子，
            // 多判一条「这一次变化跨过了 N 层」（power.Amount = 变化后，amount = 这一次的增量）。
            case "PowerReached":
                w.Open("public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)");
                // V0.2.152：以前这里无条件写 `power != this` —— 于是「盯**自己这个状态**」永远不成立
                //（用户报「我状态层数达到了却没生成卡牌」，他盯的就是这只状态自己）。
                // 现在：**显式盯到自己这只状态时**才放行；留空（任意状态）/ 盯别的状态时照旧排除自己
                //（免得自己给自己叠层 → 自己触发自己）。
                // 自己触发自己会不会死循环：「跨过 N 那一下」这个判据本身就挡住了 ——
                // 触发里再加层只会越过去（4 层时 4-1=3 不 < 3），不会重复触发。
                // V0.2.168：「盯谁 + 盯哪个状态」那一段抽到 PowerWatchGuard（四个调用点共用一份，
                // 免得「盯任意敌人」那一支再漂掉）。
                foreach (var t in items)
                {
                    string ownerGuard = PowerWatchGuard(t, p, excludeSelf: true);
                    string? want = string.IsNullOrWhiteSpace(t.PowerId) ? null : t.PowerId.Trim();
                    string reach = Lit.Dec(Math.Max(0m, t.TriggerAmount));
                    w.Open($"if ({ownerGuard}{(want is null ? "" : $" && power is {Lit.Identifier(want, "VulnerablePower")}")}"
                         + $" && power.Amount >= {reach} && power.Amount - amount < {reach})");
                    w.Line($"// 层数跨过 {Math.Max(0m, t.TriggerAmount):0.##} 那一下才触发");
                    EmitEffects(w, t, p: p);
                    w.Close();
                }
                w.Close();
                break;

            // 「获得时」：状态没有「拾取」这一说（列在菜单里只是为了让两边一样）——说清楚，不静默
            case "Obtained":
                w.Line("// 触发时机「获得时」是遗物专属的：状态是被施加的，没有「拿到的那一刻」——");
                w.Line("// 校验器会拦住这个组合，这里再留一句注释，免得有人手改工程后一头雾水。");
                break;

            case "EnemyTurnStart":
                w.Open("public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)");
                EmitChoiceContextBootstrap(w, "EnemyTurnStart", items);
                w.Line("if (side != CombatSide.Enemy) return;   // 只认敌人那一侧");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            // ===== 用户要求「触发时机合并成同一套」后补上的两条（遗物那边早就有了）=====
            // V0.2.302（用户要求「突破格挡时」，参考本体「手钻」HandDrill.AfterBlockBroken）
            case "BlockBroken":
                w.Open("public override async Task AfterBlockBroken(PlayerChoiceContext choiceContext, Creature target, Creature? breaker)");
                EmitChoiceContextBootstrap(w, "BlockBroken", items);
                w.Line("if (target.IsPlayer) return;                                   // 只认敌人的格挡被打穿");
                w.Line("if (!(breaker == base.Owner || breaker?.PetOwner?.Creature == base.Owner)) return;   // 只认自己或自己的召唤物打的（照抄手钻）");
                // V0.2.312（用户报「打帝王蟹，打右钳结果给的左钳」）：钩子里的 `target` 就是**被打穿格挡的那个单位**，
                // 「指定敌人」那一档必须用它 —— 以前不传 foeFrom，于是退回「可打的第一个敌人」（正好是左钳）。
                EmitItemsWithAmountGate(w, items, p, foeFrom: "target");
                w.Close();
                break;

            case "GoldGained":
                w.Open("public override async Task AfterGoldGained(Player player)");
                EmitChoiceContextBootstrap(w, "GoldGained", items);
                w.Line("if (player != base.Owner.Player) return;   // 只认自己拿到的金币");
                w.Line("await Task.CompletedTask;   // 钩子是 async 的：一个需要 await 的效果都没有时保持签名一致");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            case "Shuffle":
                w.Open("public override async Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler)");
                w.Line("if (shuffler != base.Owner.Player) return;   // 只认自己的抽牌堆被打乱洗牌");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            case "EnemyTurnEnd":
                w.Open("public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)");
                EmitChoiceContextBootstrap(w, "EnemyTurnEnd", items);
                w.Line("if (side != CombatSide.Enemy) return;   // 只认敌人那一侧");
                EmitItemsWithAmountGate(w, items, p);
                w.Close();
                break;

            case "EnemyDamaged":
                w.Open("public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)");
                EmitChoiceContextBootstrap(w, "EnemyDamaged", items);
                // ⚠ 这里**故意不**加「失去生命不算受到伤害」那句守卫：敌人侧没有「敌人失去生命」这条触发时机
                //   可以替代，排掉就等于「敌人被血祭 / 中毒扣血」再也收不到通知 —— 功能直接丢了（自己那两条才有替代）。
                w.Line("if (target == base.Owner || !target.IsAlive) return;   // 受伤的是敌人（自己不算）");
                EmitItemsWithAmountGate(w, items, p, foeFrom: "target");
                w.Close();
                break;

            case "EnemyDealtDamage":
                w.Open("public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)");
                EmitChoiceContextBootstrap(w, "EnemyDealtDamage", items);
                // 同上：敌人造成的伤害也没有「敌人失去生命」这条时机兜底，守卫保持原样。
                w.Line("if (dealer is null || dealer == base.Owner) return;   // 打人的是敌人（自己不算）");
                EmitItemsWithAmountGate(w, items, p, foeFrom: "dealer");
                w.Close();
                break;

            case "EnemyBlockGained":
                w.Open("public override async Task AfterBlockGained(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)");
                EmitChoiceContextBootstrap(w, "EnemyBlockGained", items);
                w.Line("if (creature == base.Owner) return;   // 拿格挡的是敌人（自己不算）");
                EmitItemsWithAmountGate(w, items, p, foeFrom: "creature");
                w.Close();
                break;

            case "PowerChanged":
                w.Open("public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)");
                // 每条触发时机各自判断「盯谁 + 盯哪个状态」：不能写成函数开头的统一 return ——
                // 那样两种对象（自己 / 任意敌人）会互相把对方挡掉（实测生成出来敌人那条永远不跑）。
                foreach (var t in items)
                {
                    string ownerGuard = PowerWatchGuard(t, p, excludeSelf: true);
                    string? want = string.IsNullOrWhiteSpace(t.PowerId) ? null : t.PowerId.Trim();
                    string guard = want is null ? ownerGuard : $"({ownerGuard}) && (power is {want})";
                    // V0.2.266（用户要求）：细分「层数上升 / 下降」—— amount 是这次变化的增量
                    if (string.Equals(t.PowerDirection, "Up", StringComparison.Ordinal))
                        guard += " && amount > 0m";
                    else if (string.Equals(t.PowerDirection, "Down", StringComparison.Ordinal))
                        guard += " && amount < 0m";
                    string what = want is null ? "任意状态" : EffectCatalog.PowerName(want, want);
                    string dirZh2 = t.PowerDirection == "Up" ? "（只要上升）" : t.PowerDirection == "Down" ? "（只要下降）" : "";
                    w.Line($"// 盯：{PowerTriggers.WatchTargetZh(t.PowerTarget)}的{what}{dirZh2}");
                    w.Open($"if ({guard})");
                    EmitEffects(w, t, p: p);
                    w.Close();
                }
                w.Close();
                break;

            // 「某个状态被移除时」（用户要求）：本体没有全局的「状态被移除」钩子
            //（`PowerModel.AfterRemoved` 是那个状态自己的实例钩子；`PowerCmd.Remove` 也不走层数变化那条路），
            // 所以这里不是 override，而是等工具自己打的补丁（见 CSharpCodeGen.PowerRemovalSource）调过来。
            // 「盯哪个状态」留空 = 自己身上任意状态被移除都触发；也可以只盯一个（power is XxxPower）。
            // 注意：这条**不排除自己**（不像 PowerChanged 那样 `power != this`）——
            // 「我这个状态自己被移除时」正是这条时机要覆盖的场景之一。
            case "PowerRemoved":
                w.Open($"public async Task {CSharpCodeGen.PowerRemovedMethod}(PowerModel power)");
                EmitChoiceContextBootstrap(w, "PowerRemoved", items);
                foreach (var t in items)
                {
                    // V0.2.168：「盯任意敌人」+ 盯的就是它自己这个状态 → 按「**这份状态自己**」算
                    // （见 PowerWatchGuard 的说明）—— 修用户实测报的「配了完全没反应」。
                    // 「被移除」这一条**不排除自己**（不像「层数变化」那样 power != this）：
                    // 「我这个状态自己被移除」正是这条时机要覆盖的场景之一。
                    string ownerGuard = PowerWatchGuard(t, p, excludeSelf: false);
                    string? want = string.IsNullOrWhiteSpace(t.PowerId) ? null : t.PowerId.Trim();
                    string guard = want is null ? ownerGuard : $"({ownerGuard}) && (power is {want})";
                    string what = want is null ? "任意状态" : EffectCatalog.PowerName(want, want);
                    w.Line($"// 盯：{PowerTriggers.WatchTargetZh(t.PowerTarget)}的{what}被移除");
                    w.Open($"if ({guard})");
                    EmitEffects(w, t, p: p);
                    w.Close();
                }
                w.Close();
                break;

            default:
                w.Line("// （未知的触发时机，已跳过）");
                break;
        }
    }
    private static void EmitEffects(CodeWriter w, PowerTriggerSpec t, string? foeFrom = null, string? roomVar = null,
        CharacterProfile? p = null, bool ownScope = false)
    {
        // V0.2.318（用户报「锻造状态某个状态层数到达 N 层触发时机不生效」）：**锻造状态**这块布上，
        // 「跑效果」那一步要用图上渲染好的正文（_graphBody）。以前只有 EmitItemsWithAmountGate 那条路
        // 会换正文，而「到达 N 层」这些档是直接调 EmitEffects 的 → 图上的效果被丢掉，退回老链发射器，
        // TransformCard 之类在状态链上不支持 → 只剩一句「用不了」注释 = 完全不生效。
        if (_graphBody is { } graphBodyNow)
        {
            _graphBody = null;   // 每段正文只落一次
            w.Line();
            w.Raw(graphBodyNow);
            return;
        }
        if (t.Effects.Count == 0)
        {
            w.Line("// （这个触发时机下还没加效果）");
            return;
        }
        // 一个钩子方法里合并了好几条触发时机时，每条各自包一层**裸块**。
        // 为什么必须包：每条触发时机的效果里会就地声明一批「帮助变量」
        //（概率的 __chanceRoll、随机目标的 Creature? other、拿牌的 __fgTake / __fgUp / srcVar / __fgPick、牺牲伙伴的局部…），
        // 而「多条触发时机共用一个钩子」是支持的写法（见 TriggerAmountCond 的注释）——
        // 不包的话第二条起就会「已在此范围定义了名为 __chanceRoll 的局部变量」CS0128（用户实测：
        // LwenjunPower2.cs(37,16)：6 个错误全是同一个 __chanceRoll 重复声明）。
        // ⚠ 只在**真的合并了多条**时才包：单条触发时机的输出一个字符都不变（「默认档逐字不变」）。
        // ⚠ 裸块回读侧也认：ProjectRecovery 的 `line == "{"` 那一支会记一帧（不然它那个 `}` 会弹掉真正的循环 / 条件帧）。
        if (ownScope) w.Open("");
        w.Line();
        // 概率生效：这一个触发时机里所有勾了概率的效果共用一次掷骰
        var powerChanceSlices = CSharpCodeGen.ChanceSlices(t.Effects);
        if (powerChanceSlices.Count > 0) w.Line(CSharpCodeGen.ChanceRollLine("base.Owner.Player"));
        // 「生成调试日志」（V0.2.142，默认档一个字都不写）：这一段的效果都属于「这个自定义状态」
        // （状态钩子里 base.Owner 本身就是 Creature，所以「自己」= base.Owner）。
        // 状态名字直接取生成期那个「当前状态」（_powerOwner），不额外往二十几个调用点加参数。
        using (DebugLogGen.Begin(p, "自定义状态", _powerOwner is { } dbgPow ? dbgPow.Name : "", DebugLogClassNameOf(p), "base.Owner"))
        foreach (var e in t.Effects)
        {
            // 每条效果自己的条件：不满足时只跳过这一条
            string? cond = CSharpCodeGen.EffectConditionGuard(e, CSharpCodeGen.CondCtx.Power, chanceSlices: powerChanceSlices);
            if (cond is not null)
            {
                w.Line($"// 条件（只对「{EffectCatalog.FindKind(e.Kind).Display}」这条效果）：{CSharpCodeGen.ConditionText(e.Condition)}（不满足时这条效果不生效）");
                w.Open(cond);
            }
            EmitEffect(w, e, foeFrom, roomVar, p);
            if (cond is not null) w.Close();
        }
        if (ownScope) w.Close();   // 关掉这条触发时机自己的裸块（见方法开头）
    }

    /// <summary>
    /// 「生成调试日志」里写哪个类名：优先用配置里填的类名，没填就拼 <c>&lt;角色类名&gt;Power</c>
    /// （自动编号那个 <c>&lt;角色类名&gt;Power&lt;序号&gt;</c> 在这里拿不到序号 —— 序号只影响日志原文，
    /// 所以退回不带序号的名字，不影响生成结果）。
    /// </summary>
    private static string DebugLogClassNameOf(CharacterProfile? p)
    {
        string cls = (_powerOwner?.ClassName ?? "").Trim();
        if (cls.Length > 0) return cls;
        return p is null ? "" : Naming.From(p).CharClass + "Power";
    }

    /// <summary>
    /// 「打出一张牌时 / 一张牌结算完毕时」两条时机**共用的方法体**（V0.2.157 抽出来）：
    /// 只看自己打出的牌 + 按「只对哪类牌生效」分流，再跑各自的效果。
    /// 两条区别只在**钩子**（BeforeCardPlayed = 结算前 / AfterCardPlayedLate = 结算完），
    /// 体一模一样 —— 抽一份免得两处漂移。
    /// </summary>
    private static void EmitCardPlayedItems(CodeWriter w, List<PowerTriggerSpec> items, CharacterProfile? p)
    {
        w.Line("if (cardPlay.Player != base.Owner.Player) return;");
        // 不挑牌型的先无条件跑；挑牌型的各自包一层 if
        EmitItemsWithAmountGate(w, items.Where(x => IsAnyCardFilter(x.CardFilter)), p, foeFrom: "cardPlay.Target");
        foreach (var t in items.Where(x => !IsAnyCardFilter(x.CardFilter)))
        {
            // 老的四档（攻击 / 技能 / 能力）生成的判据**逐字不变**（老存档的回归红线）；
            // V0.2.118 新加的 诅咒 / 状态 / 关键词组 / 组 走卡牌那边同一套「范围界定」谓词。
            if (t.CardFilter is "Attack" or "Skill" or "Power")
            {
                w.Open($"if (cardPlay.Card.Type == CardType.{t.CardFilter})");
            }
            else
            {
                w.Line($"// CET:CardFilter={t.CardFilter}");
                // V0.2.207：自定义关键词要连**运行时**标记一起看（战斗中刚给上的关键词也算），
                // 见 CSharpCodeGen.TriggerCardFilterCond（用户实测：「给卡牌关键词后打出它，这个时机不响」）。
                string? cond = CSharpCodeGen.TriggerCardFilterCond(t.CardFilter, p, "cardPlay.Card");
                if (cond is null)
                {
                    // 认不出来的筛选值：不乱猜，照旧不筛（校验器会报错说清）
                    EmitItemsWithAmountGate(w, new[] { t }, p, foeFrom: "cardPlay.Target");
                    continue;
                }
                w.Open($"if ({cond})");
            }
            EmitItemsWithAmountGate(w, new[] { t }, p, foeFrom: "cardPlay.Target");
            w.Close();
        }
    }

    /// <summary>
    /// 这个触发时机的钩子有没有 choiceContext 参数？
    /// 本体有些钩子不带它：AfterSideTurnStart(side, participants, combatState)、
    /// AfterBlockGained(creature, amount, props, cardSource)、BeforeCombatStart()、
    /// AfterEnergySpent(int, Player)、AfterStarsSpent(int, Player)、
    /// AfterCardGeneratedForCombat(card, creator)。
    /// </summary>
    internal static bool TriggerHasChoiceContext(string? kind) =>
        kind is not ("EnemyTurnStart" or "EnemyBlockGained" or "BlockGained" or "EnergySpent" or "StarsSpent" or "CombatStart" or "CombatVictory" or "HpLost" or "CardGenerated" or "GoldGained"
            // 「某个状态被移除时」不是本体的钩子（是工具补丁调过来的普通方法）→ 同样没有 choiceContext
            or "PowerRemoved"
            // V0.2.157：「打出一张牌时」用的是 BeforeCardPlayed(CardPlay)——签名里**没有** choiceContext
            //（本体 AbstractModel.BeforeCardPlayed 就只有 cardPlay）。「一张牌结算完毕时」用的是
            // AfterCardPlayedLate(choiceContext, cardPlay)，**有** context，所以不列进来。
            or "CardPlayed");

    /// <summary>
    /// 这个钩子没有 choiceContext 时，自己造一个再往下跑。
    /// 本体给模型派发钩子时就是这么造的（Hook.cs：new HookPlayerChoiceContext(model, netId, combatState, GameActionType.Combat)），
    /// 所以「敌人回合开始时造成伤害 / 上状态」这类效果也能正常用（不造的话会 CS0103）。
    /// roomInScope = 钩子里有 CombatRoom 的那几条：那种地方卡牌奖励走 room.AddExtraReward，不需要 choiceContext。
    /// </summary>
    private static void EmitChoiceContextBootstrap(CodeWriter w, string kind, IEnumerable<PowerTriggerSpec> items, bool roomInScope = false)
    {
        if (TriggerHasChoiceContext(kind)) return;
        if (!items.Any(t => t.Effects.Any(x => NeedsChoiceContext(x) && !(roomInScope && x.Kind == "CardReward")))) return;
        EmitChoiceContextBootstrapRaw(w);
    }

    /// <summary>无条件造一个 choiceContext（调用方自己判断要不要）。衰减那一段（见 <see cref="EmitDecay"/>）也用它。</summary>
    private static void EmitChoiceContextBootstrapRaw(CodeWriter w)
    {
        w.Line("// 这个钩子本体没给 choiceContext：按本体的做法自己造一个（不然造成伤害 / 施加状态 / 抽牌用不了）");
        w.Line("ulong __netId = MegaCrit.Sts2.Core.Context.LocalContext.NetId ?? 0UL;");
        w.Line("var choiceContext = new MegaCrit.Sts2.Core.GameActions.Multiplayer.HookPlayerChoiceContext(this, __netId, base.CombatState, MegaCrit.Sts2.Core.Entities.Multiplayer.GameActionType.Combat);");
    }

    /// <summary>
    /// 「每回合衰减 / 回合结束移除」那一段。
    ///
    /// ⚠️ 必须走 <c>PowerCmd.ModifyAmount</c>，**不能直接写 <c>SetAmount</c>**：
    /// 本体只在 <c>PowerCmd.ModifyAmount</c> / <c>Apply</c> 里调 <c>Hook.AfterPowerAmountChanged</c>
    /// （见 PowerCmd.cs 第 249 行），直接 SetAmount 等于绕过钩子 ——
    /// 表现就是「自定义状态里盯『某个状态层数变化后』：上状态时能触发，之后每回合自然衰减却不触发」
    /// （用户实测报的）。本体的状态衰减清一色是 <c>await PowerCmd.Decrement(this)</c>，就是这个原因。
    /// 我们这里要衰减 N 层，所以用 ModifyAmount 传 -N（Decrement 只能 -1）。
    ///
    /// applier 传 <c>null</c>（和本体 Decrement 一致）：衰减不该被「施加者加成 / 减伤」那套影响。
    /// </summary>
    private static void EmitDecay(CodeWriter w, CustomPowerSpec power, bool roomInScope = false, bool hasChoiceContext = false)
    {
        bool removeAtEnd = power.RemoveAtTurnEnd;
        int decay = power.DecayPerTurn;
        if (!removeAtEnd && decay <= 0) return;
        // 方法签名里本来就有 choiceContext 的（BeforeSideTurnEnd）直接用那个；
        // 没有的（「只配了衰减」的兜底方法）自己造一个 —— 上状态那种效果要走 PowerCmd.Apply。
        if (!hasChoiceContext) EmitChoiceContextBootstrapRaw(w);
        w.Line();
        if (removeAtEnd)
        {
            w.Line("await PowerCmd.Remove(this);   // 临时状态：回合结束就没了");
            return;
        }
        w.Line($"// 每回合减 {decay} 层（减到 0 就移除）—— 走 PowerCmd.ModifyAmount 才会通知「层数变化后」那些钩子");
        w.Open($"if (base.Amount <= {decay})");
        w.Line("await PowerCmd.Remove(this);");
        w.Close();
        w.Line("else");
        w.Open("");
        w.Line($"await PowerCmd.ModifyAmount(choiceContext, this, -{decay}m, null, null);");
        w.Close();
    }

    /// <summary>
    /// 这些效果生成出来的代码里**会用** choiceContext，所以落在「本体钩子不带 choiceContext」的
    /// 触发时机里时必须先 bootstrap 一个（否则 CS0103）。
    ///
    /// 用户实测报过三次：第一次是漏了「自己获得格挡后」那条分支的 bootstrap，
    /// 第二次是这份清单本身漏了 HpLoss / MaxHp / 下回合生效的 ExtraResource ——
    /// 「敌人回合开始时：失去 3 点生命」正好是 HpLoss，于是照样 CS0103；
    /// 第三次是「生成卡牌」—— 它**分档**（只有「多选1 / 自己挑一张」那两条路会弹选牌界面、
    /// 才写 choiceContext），所以只能加在细化重载 <see cref="NeedsChoiceContext(EffectSpec)"/> 里，
    /// **不要**加进这份不看档位的 string 清单（加了会让「指定卡 / 纯按范围随机」的默认档也凭空
    /// 多出三行 bootstrap，破坏「默认档逐字不变」）。
    /// 以后新增任何在 EmitEffect 里写 choiceContext 的效果种类，都要加进这里（有自检兜底）。
    /// </summary>
    internal static bool NeedsChoiceContext(string? kind) =>
        kind is "Damage" or "Draw" or "ApplyPower" or "TempPower" or "ExtraTurn" or "CardReward"
            or "HpLoss" or "MaxHp" or "ExtraResource"
            // 升级卡牌也要（选牌界面走 choiceContext）—— 漏了的话「敌人回合开始时升级一张牌」会 CS0103
            or "UpgradeCard"
            // 消耗 / 丢弃卡牌同理（都要弹选牌界面）
            or "ExhaustCard" or "DiscardCard"
            // V0.2.167：「强化指定卡牌」也要 —— 状态链里它写的是
            // `PowerCmd.Apply<…Boost…Power>(choiceContext, …)`（见下面 case "BoostCard"），
            // 漏了这份清单 → 落在不带上下文的钩子上时「用了但没声明」→ CS0103（用户实测报过：
            // TenguPower1.cs(20,80) 等 5 处）。
            or "BoostCard"
            // 同理：「从某一摞挑 N 张拿到手牌」用的是 `CardSelectCmd.FromCombatPile(choiceContext, …)`
            //（见下面 case "TakeFromDraw" / "TakeFromDiscard" / "TakeFromExhaust"）。
            or "TakeFromDraw" or "TakeFromDiscard" or "TakeFromExhaust"
            // V0.2.168：「召唤伙伴」—— 生成的 `XxxCmd.Summon(choiceContext, player, hp)` 第一个参数就是它
            //（用户实测报的：状态「生成卡牌时 → 召唤伙伴」生成出来 CS0103 choiceContext）。
            or "SummonPet";

    /// <summary>同 <see cref="NeedsChoiceContext(string)"/>，但能按效果本身细化。</summary>
    public static bool NeedsChoiceContext(EffectSpec e) => e.Kind switch
    {
        // 「额外资源量」只有「下回合生效」才走 PowerCmd.Apply&lt;StarNextTurnPower&gt;(choiceContext, …)；
        // 普通获得走 PlayerCmd.GainStars（不需要），花费用负数额度也不占变量。
        "ExtraResource" => e.NextTurn && (e.Amount > 0 || e.AmountIsStack),
        // 「生成卡牌」只有**弹选牌界面**的两条路会写 choiceContext（见 CSharpCodeGen.EmitGenerateCard）：
        //   · 「多选1」（SpawnChoice &gt; 1）→ CardSelectCmd.FromChooseACardScreen / FromSimpleGrid
        //   · 「自己挑一张」（SpawnChosen）→ 同上
        // 「指定卡」和纯「按范围随机」都不写它 —— 所以只写在细化重载里（string 版不看档位，加了会污染默认档）。
        // 漏掉这条的后果：「敌人回合开始时：生成卡牌（多选1）」这种配置生成出来 CS0103（用户实测报过）。
        "GenerateCard" => e.IsSpawnRandom && (e.SpawnChosen || e.SpawnChoice > 1m),
        // V0.2.210：宠物效果里**有几档**也要 choiceContext（以前一个都没列，而且「牺牲伙伴」已经因此出过问题）：
        //   · 牺牲伙伴：收益是「造成伤害」→ DamageCmd.Attack(...).Execute(choiceContext)；
        //               收益是「获得增益 / 减益」→ PowerCmd.Apply(choiceContext, …)；
        //               收益是「获得格挡」→ CreatureCmd.GainBlock（不传）。
        //     所以只有前两种需要，按收益档位判（给格挡那档凭空多三行 bootstrap 会破坏默认档逐字一致）。
        "PetSacrifice" => e.PetSacrificeGain is "Damage" or "Power",
        //   · 伙伴失去生命 → CreatureCmd.Damage(choiceContext, …)
        //   · 给伙伴施加状态 → PowerCmd.Apply / 延迟状态（两个分支都传 choiceContext）
        //   · 承伤开关 → PowerCmd.Apply / PowerCmd.Remove<…Guardian…>(choiceContext, …)
        //   · 治疗伙伴 / 伙伴最大生命 +N → CreatureCmd.Heal / GainMaxHp（**不传** choiceContext）
        "PetLoseHp" or "PetApplyPower" or "PetGuardOn" or "PetGuardOff" => true,
        _ => NeedsChoiceContext(e.Kind),
    };

    /// <summary>
    /// 「随机挑一个敌人」那一行。
    ///
    /// 坑（用户实测编译失败）：状态钩子里 <c>base.Owner</c> 是 **Creature**，直接写
    /// <c>base.Owner.RunState</c> 报 CS1061（Creature 没有 RunState，整次构建失败）。
    /// 本体状态是这么写的（CacophonyPower / CountdownPower / HauntPower）：
    /// <c>base.Owner.Player.RunState.Rng.CombatTargets.NextItem(base.CombatState.HittableEnemies)</c>。
    /// 再兜一层 <c>base.CombatState?.RunState</c>：状态万一被挂在敌人身上时 Player 是 null（两边都取不到就返回 null，
    /// 后面那行 <c>if (other is null) break;</c> 会安全跳过，不会崩）。
    /// </summary>
    private const string RandomTargetLine =
        "Creature? other = (base.Owner.Player?.RunState ?? base.CombatState?.RunState)?.Rng.CombatTargets.NextItem(base.Owner.CombatState.HittableEnemies);";

    /// <summary>一条效果在状态钩子里的写法（上下文：choiceContext + base.Owner 是 Creature）。
    /// roomVar 非空 = 钩子里有 CombatRoom（战斗胜利后），卡牌奖励走本体的战斗奖励。</summary>
    private static void EmitEffect(CodeWriter w, EffectSpec e, string? foeFrom, string? roomVar = null,
        CharacterProfile? p = null)
    {
        // 数值 0 且升级也不加数值：整条丢掉（用户要求：不显示、也不执行 —— 0 点会被力量/敏捷加成）
        if (CSharpCodeGen.IsInertZero(e)) return;
        // V0.2.164：状态链上也能写「数值公式」了 —— 同样写一行标记，「从工程恢复存档」按它认回来
        if (e.UsesAmountFormula) w.Line($"// CET:AmountFormula={CSharpCodeGen.EscapeMarker(e.AmountFormula)}");
        // V0.2.238：「生效次数」的公式在状态链上也认了 —— 回读标记同一份口径
        CSharpCodeGen.EmitCountFormulaMarker(w, e);
        // V0.2.164：「数值公式」在状态链上也是**全内联**求值 —— 状态钩子里 base.Owner 本身就是 Creature，
        // 「自己」= base.Owner、「玩家」= base.Owner.Player（没有「玩家选中的目标」→「指定敌人」取可打的第一个敌人）。
        // 没写公式（或这种种类不支持公式）时退回固定数值 / 层数，输出与老版本逐字相同。
        string amt = e.AmountIsStack
            ? "base.Amount"
            : (CSharpCodeGen.InlineAmountFormula(p, e, "base.Owner.Player", "base.Owner",
                   CSharpCodeGen.FirstEnemyOf("base.Owner.Player")) ?? Lit.Dec(e.Amount));
        // V0.2.337（用户要求「插件别限制太死」）：插件贡献的效果种类在**状态链**上也能落地了
        // （以前只有卡牌 / 遗物 / 药水 / 锻造图四条链 —— 状态链的 switch 白名单会把插件 kind 打回「用不了」）。
        if (Plugins.PluginGen.TryEmit(w, e, new Plugins.PluginGen.PluginCtx(
                Self: "base.Owner", Player: "base.Owner.Player!", Foe: CSharpCodeGen.FirstEnemyOf("base.Owner.Player"),
                Choice: "choiceContext", CardSelf: "null", AmountOverride: amt,
                // V0.2.339：状态链上 base.Owner **本身就是 Creature**，不能写 base.Owner.Creature.CombatState
                //（V0.2.338 的默认值就是那个 → 模板里用 {combatState} 会 CS1061）
                CombatState: "base.Owner.CombatState", CardSelfIsCard: false)))
            return;
        // 「直接把缓慢设成 N%」：本体的做法就是只施加 1 层（层数对「缓慢」没有作用），
        // 而且 amount == 0 时 PowerCmd.Apply 直接 return（状态根本挂不上）
        if (CSharpCodeGen.IsSlowPercentEffect(e)) amt = "1m";
        // 生效次数：勾了「= 本状态的层数」就按层数循环，否则用填的数字（层数本身不会让效果多跑，这点用户问过）
        // V0.2.238（用户问「生效次数可以填写公式吗」）：状态链也能写了 —— 和这条链的「数值公式」同一套内联口径
        // （状态钩子里没有卡牌那套前置局部变量）：上界现算，夹到 1~20，免得公式算出 999 把一局卡死。
        int times = Math.Max(1, Math.Min(20, e.Times));
        string? timesFormula = e.TimesIsStack ? null
            : CSharpCodeGen.InlineCountFormula(p, e, "Times", "base.Owner.Player", "base.Owner",
                CSharpCodeGen.FirstEnemyOf("base.Owner.Player"));
        bool loop = e.TimesIsStack || timesFormula is not null || times > 1;
        // 每条效果外面永远包一层作用域：里面会声明 other / pick 这类临时变量，
        // 同一个触发时机里两条效果都声明同名变量时 C# 会 CS0136/CS0128 编译不过
        w.Open("");
        // V0.2.132：新档位在状态链上也要能回读 —— 只在**新档位**（全体单位 / 全部友方不含召唤物）
        // 出现时才写这一行；老存档（6 档 + 含召唤物）一个字符都不多（红线①）。
        if (string.Equals(e.TargetSide, "AllUnits", StringComparison.Ordinal))
            w.Line("// CET:TargetSide=AllUnits");
        else if (string.Equals(e.TargetSide, "AllAllies", StringComparison.Ordinal) && e.AllAlliesExcludePets)
            w.Line("// CET:TargetSide=AllAllies CET:AllyNoPets=1");
        // V0.2.306：「获得能量 / 金币」的队友那两档在状态链上也没有别的标记，
        // 不写这一行，回读会退回默认的「单体敌人」（生成时等同于「自己」）。
        else if (e.Kind is "Energy" or "Gold" && e.TargetSide is "Ally" or "AllAllies")
            w.Line($"// CET:TargetSide={e.TargetSide}");
        if (loop) w.Open($"for (int __rep = 0; __rep < {(e.TimesIsStack ? "(int)base.Amount" : timesFormula is not null ? $"(int)System.Math.Clamp({timesFormula}, 1m, 20m)" : times.ToString())}; __rep++)");

        // V0.2.315：插件效果种类（状态链：base.Owner 本身就是 Creature；「指定敌人」按触发器的目标兜底）
        if (PluginGen.TryEmit(w, e, new PluginGen.PluginCtx(
                Self: "base.Owner", Player: "base.Owner.Player",
                Foe: foeFrom ?? "base.Owner.CombatState?.HittableEnemies.FirstOrDefault()",
                Choice: "choiceContext",
                AmountOverride: e.AmountIsStack ? "(int)base.Amount" : Lit.Dec(e.Amount),
                // V0.2.339：同上 —— 状态链的战斗状态挂在 base.Owner 自己身上；状态的 this 也不是卡牌
                CombatState: "base.Owner.CombatState", CardSelfIsCard: false)))
            return;

        switch (e.Kind)
        {
            case "Block":
                // V0.2.241（用户报「触发时机增加回复生命值时……」）：这一条以前**不看「作用对象」**，
                // 永远 `base.Owner` 自己加减格挡 —— 选了「全体友方 / 全体敌人」也还是只动自己。
                // 现在和「造成伤害」同一套展开（见 EmitPowerTargets）；「自己」那一档的输出逐字不变。
                EmitPowerTargets(w, e, foeFrom, (x, tg) =>
                {
                    DebugLogGen.Target(x, e, tg,
                        CSharpCodeGen.IsNegativeAmount(e, useX: false) ? CSharpCodeGen.AbsExpr(amt) : amt,
                        inLoop: IsMultiTargetSide(e));
                    // ⚠ V0.2.190（用户实测报的「状态**失去格挡**还是没有正常生效」）：**负数 = 失去 N 点格挡**。
                    // 以前这里不看正负、一律写 `CreatureCmd.GainBlock`，而本体 `CreatureCmd.GainBlock`
                    // 对**非正数直接返回**（什么都不做）⇒ 状态里填 -6 生成的是
                    // `GainBlock(base.Owner, -6m, …)`，游戏里一点格挡都不掉。
                    // 卡牌 / 遗物 / 药水 / 锻造节点图四条链早就是「负数 → CreatureCmd.LoseBlock」，
                    // 只有状态链漏了（用户报「还是没生效」就是因为上一轮只修了节点图那条）。
                    x.Line(CSharpCodeGen.IsNegativeAmount(e, useX: false)
                        ? $"await CreatureCmd.LoseBlock(choiceContext, {tg}, {CSharpCodeGen.AbsExpr(amt)}, base.Owner);   // 负数 = 失去格挡（本体 GainBlock 对非正数什么都不做）"
                        : $"await CreatureCmd.GainBlock({tg}, {amt}, ValueProp.Unpowered, null);");
                });
                break;

            case "Damage":
                EmitDamage(w, e, amt, foeFrom);
                break;

            case "Draw":
                // V0.2.220：填了「为抽到的牌加关键词」时接住抽到的牌再加（没填还是老那一句）
                CSharpCodeGen.EmitDraw(w, p, e, amt, "base.Owner.Player", "base.Owner", "null");
                break;

            case "Energy":
                // 负数 = 失去能量（本体 GainEnergy 对非正数直接返回；和卡牌链 GainEnergyLine 同一口径）
                // V0.2.306：作用对象能选「全部友方」了（状态触发链没有「玩家选中的目标」→「指定队友」退回自己，
                // 校验器会报出来）；「自己」那一档的输出逐字不变。
                CSharpCodeGen.EmitPerPlayer(w, e, "base.Owner.Player", "base.Owner", null,
                    player => CSharpCodeGen.IsNegativeAmount(e, useX: false)
                        ? $"await PlayerCmd.LoseEnergy({CSharpCodeGen.AbsExpr(amt)}, {player});   // 负数 = 失去能量（本体 GainEnergy 对非正数直接返回）"
                        : $"await PlayerCmd.GainEnergy({amt}, {player});");
                break;

            case "Heal":
                // V0.2.241（用户报的就是这一条）：以前不管「作用对象」选谁都只有一句
                // `CreatureCmd.Heal(base.Owner, N)` —— 选了全体友方 / 单体敌人还是只给自己回血。
                // 现在按作用对象展开；「自己」那一档输出逐字不变。
                EmitPowerTargets(w, e, foeFrom, (x, tg) =>
                {
                    DebugLogGen.Target(x, e, tg,
                        CSharpCodeGen.IsNegativeAmount(e, useX: false) ? CSharpCodeGen.AbsExpr(amt) : amt,
                        inLoop: IsMultiTargetSide(e));
                    // 负数 = 反向：失去 N 点生命（和卡牌 / 遗物 / 药水链同一口径 —— 本体 Heal 对非正数什么都不做）
                    x.Line(CSharpCodeGen.IsNegativeAmount(e, useX: false)
                        ? $"await CreatureCmd.Damage(choiceContext, {tg}, {CSharpCodeGen.AbsExpr(amt)}, ValueProp.Unblockable | ValueProp.Unpowered, base.Owner);   // 负数 = 失去生命（本体 Heal 对非正数什么都不做）"
                        : $"await CreatureCmd.Heal({tg}, {amt});");
                });
                break;

            case "HpLoss":
                EmitPowerTargets(w, e, foeFrom, (x, tg) =>
                {
                    DebugLogGen.Target(x, e, tg, amt, inLoop: IsMultiTargetSide(e));
                    x.Line($"await CreatureCmd.Damage(choiceContext, {tg}, {amt}, ValueProp.Unblockable | ValueProp.Unpowered, base.Owner);");
                });
                break;

            case "MaxHp":
                // V0.2.241：顺带修掉两处（都是同一条「作用对象」的洞）：
                //   ① 以前永远只改 base.Owner 的最大生命；
                //   ② 以前按 **`base.Amount`（状态自己的层数）** 的正负决定「加 / 减」——
                //      层数基本恒为正 ⇒ 「最大生命 -N」实际上一直是**加** N。现在按效果数值的正负走
                //      （和卡牌 / 遗物 / 药水链一致）。
                EmitPowerTargets(w, e, foeFrom, (x, tg) =>
                {
                    DebugLogGen.Target(x, e, tg, amt, inLoop: IsMultiTargetSide(e));
                    x.Line(CSharpCodeGen.IsNegativeAmount(e, useX: false)
                        ? $"await CreatureCmd.LoseMaxHp(choiceContext, {tg}, {CSharpCodeGen.AbsExpr(amt)}, isFromCard: false);"
                        : $"await CreatureCmd.GainMaxHp({tg}, {amt});");
                });
                break;

            case "Gold":
                // 负数 = 失去金币（本体 GainGold 对非正数直接返回；和卡牌链 GainGoldLine 同一口径）
                // V0.2.306：作用对象能选「全部友方」了（「指定队友」在状态链上退回自己，校验器会报出来）
                CSharpCodeGen.EmitPerPlayer(w, e, "base.Owner.Player", "base.Owner", null,
                    player => CSharpCodeGen.IsNegativeAmount(e, useX: false)
                        ? $"await PlayerCmd.LoseGold({CSharpCodeGen.AbsExpr(amt)}, {player});   // 负数 = 失去金币（本体 GainGold 对非正数直接返回）"
                        : $"await PlayerCmd.GainGold({amt}, {player});");
                break;

            case "ExtraResource":
                // 下回合生效：本体的 StarNextTurnPower（它的 GainStars 拒绝负数，所以只有「获得」走这条）
                if (e.NextTurn && (e.Amount > 0 || e.AmountIsStack))
                    w.Line($"await PowerCmd.Apply<StarNextTurnPower>(choiceContext, base.Owner, {amt}, base.Owner, null);   // 下回合开始时获得 {amt} 点额外资源量");
                // 负数 = **失去**额外资源量：本体 `PlayerCmd.GainStars` 对负数**直接抛 ArgumentException**
                // （不是静默忽略！所以状态链里填 -1 以前是「每触发一次抛一次异常」），必须改走 LoseStars。
                else if (CSharpCodeGen.IsNegativeAmount(e, useX: false))
                    w.Line($"await PlayerCmd.LoseStars({CSharpCodeGen.AbsExpr(amt)}, base.Owner.Player);   // 负数 = 失去额外资源量（本体 GainStars 对负数直接抛异常）");
                else
                    w.Line($"await PlayerCmd.GainStars({amt}, base.Owner.Player);");
                break;

            case "ExtraTurn":
                w.Line($"await PowerCmd.Apply<{Naming.AmbientExtraTurnPowerClass}>(choiceContext, base.Owner, 1m, base.Owner, null);");
                break;

            // 播放音效（V0.2.154）：状态触发时也能放（和卡牌 / 遗物 / 药水共用同一个发射器）
            case "PlaySfx":
                CSharpCodeGen.EmitPlaySfx(w, e);
                break;

            // 播放上传的音频文件（V0.2.155）：Godot AudioStreamPlayer，状态触发时也能放
            //（V0.2.231：「会被下一个自定义音频顶替」那一档在状态链上同样生效）
            case "PlaySoundFile":
                CSharpCodeGen.EmitPlaySoundFile(w, e, p);
                break;

            case "GenerateCard":
                // 需求 ⑥：这里**不再**手写 AddToCombatAndPreview，而是把卡牌那条链的 EmitGenerateCard 拿来复用 ——
                // 「按范围随机（含自定义关键词那一组）」「生成出来的卡的附加处理（升级 / 免费 / 本回合升级 /
                // 本回合免费 / 需求⑦ 的带关键词）」全都只有一份实现，这里只把「上下文表达式」换掉。
                //
                // 为什么分两支：**默认档必须逐字一致**。老代码在「指定卡、不加任何附加处理」时写的是
                //   await CardPileCmd.AddToCombatAndPreview<卡>(base.Owner, PileType.X, N, base.Owner.Player);
                // 而 EmitGenerateCard 统一写 (玩家.Creature, …)。状态钩子里 base.Owner 是 Creature、本身就是「自己」，
                // 所以默认档照样传 base.Owner（文本与旧版一致、也不写 CET:SpawnPick 标记行、不写那句生成注释）；
                // 只有真的用上「按范围随机 / 附加处理 / 范围界定」时才换成 base.Owner.Player
                //（那些都要 Player 与 profile：卡池 / RunState.Rng / 自定义关键词分组）。
                {
                    bool plainSpawn = !e.IsSpawnRandom && !e.HasSpawnModifier && !e.HasCardScope;
                    CSharpCodeGen.EmitGenerateCard(w, p!, e,
                        // tempUpSource = 生成的那个「仅本回合升级」临时状态挂上去时的来源实参。
                        // 状态钩子里没有「这张牌」，所以来源只能是 null（卡牌那条链才是 this）。
                        tempUpSource: "null",
                        playerExpr: plainSpawn ? "base.Owner" : "base.Owner.Player",
                        withMarker: !plainSpawn,
                        creatureExpr: "base.Owner",
                        withComment: !plainSpawn,
                        playerArg: "base.Owner.Player");
                }
                break;

            // 获得药水（需求 ⑨）：本体 PotionCmd.TryToProcure 不收 choiceContext，状态触发器里照样能用。
            // 状态钩子里 base.Owner 是 **Creature**（不是 Player），所以玩家那一侧要取 base.Owner.Player；
            // 「瓶数 = 本状态的层数」那一档直接传 base.Amount（和别的效果同一套口径）。
            case "GainPotion":
                CSharpCodeGen.EmitGainPotion(w, e, useX: false, playerExpr: "base.Owner.Player", p: p,
                    countExpr: e.AmountIsStack ? "base.Amount" : null);
                break;

            // 毒性爆发：状态里也能用（都是本体 API）。注意这里的 base.Owner 是 Creature（不是 Player），
            // 所以敌人列表 / 施加者都用它自己的形式（见 CSharpCodeGen.EmitOutbreak 的参数说明）。
            case "Outbreak":
                CSharpCodeGen.EmitOutbreak(w, e, amt,
                    enemies: "base.Owner.CombatState.HittableEnemies",
                    applier: "base.Owner", cardSource: "null");
                break;

            // 全局（牌组）类效果：状态里也能用（直接改玩家的牌组，不受战斗牌堆限制）
            case "AddCardGlobal":
            case "TransformCardGlobal":
            case "RemoveCardGlobal":
            case "UpgradeCardGlobal":
                CSharpCodeGen.EmitGlobalCardEffectPublic(w, e, "base.Owner.Player", e.AmountIsStack, p);
                break;

            // ===== 清除状态 / 清除所有减益（用户报的用例）=====
            // 以前校验器把这两个拦下来，理由是「需要卡牌上下文（选牌 / 结束回合）」—— **那个理由是错的**：
            // 生成的原文本体只有
            //   `PowerModel? __clrPower = foe.GetPower<T>(); if (__clrPower is not null) await PowerCmd.Remove(__clrPower);`
            //   `foreach (PowerModel p in foe.Powers.ToList()) if (p.TypeForCurrentAmount == PowerType.Debuff) await PowerCmd.Remove(p);`
            // 一个 choiceContext 都不传。唯一需要的是「选哪个单位」，而这块的目标解析和 ApplyPower / Stun 一模一样，
            // 直接复用 CSharpCodeGen.EmitPerCreature（cardTargetExpr 传 null = 状态链没有「玩家选的目标」，
            // 它自己会退回「可打的第一个敌人」）。
            case "ClearPowerOne":
                CSharpCodeGen.EmitPerCreaturePublic(w, e, useX: false, "base.Owner", null,
                    "base.Owner.CombatState.HittableEnemies",
                    foe => $"{{ PowerModel? __clrPower = {foe}.GetPower<{e.PowerId}>(); if (__clrPower is not null) await PowerCmd.Remove(__clrPower); }}   // 清除指定状态");
                break;

            case "ClearPowerDebuffs":
                // 先 ToList 再遍历，免得边遍历边改集合（本体 ClearPowerDebuffs 就是这么写的）
                CSharpCodeGen.EmitPerCreaturePublic(w, e, useX: false, "base.Owner", null,
                    "base.Owner.CombatState.HittableEnemies",
                    foe => $"{{ foreach (PowerModel __clrP in {foe}.Powers.ToList()) {{ if (__clrP.TypeForCurrentAmount == PowerType.Debuff) await PowerCmd.Remove(__clrP); }} }}   // 清除所有减益");
                break;

            // ===== 牺牲伙伴换收益（用户报的用例）=====
            // 先按本体那套查出这只宠物（`XxxCmd.Get(base.Owner)`，和卡牌那半段一模一样），
            // 用 `if (__pet is not null)` 包住 —— 伙伴不在场时整条静默跳过（不会报错，和卡牌一致）。
            // 收益那三档的生成复用卡牌那份实现（CSharpCodeGen.EmitPetSacrificePublic），
            // 「获得格挡」「造成伤害」这两档在里面按状态上下文重写（没有 cardPlay）。
            case "PetSacrifice":
            {
                // V0.2.210：「全部召唤物」以前在这里走的是 PetGen.Resolve（它对 "*" 返回 null）→
                // 生成一句「⚠ 没有可用的召唤物」然后什么都不做，而校验器明确允许这个组合（静默失效）。
                // 现在和卡牌那条链一样按**已启用的那几只**逐只展开。
                var sacDefs = PetGen.ResolveMany(p!, e.PetSummon);
                if (sacDefs.Count == 0)
                {
                    w.Line("// ⚠ 这条「牺牲伙伴」没有可用的召唤物：到「召唤物」页添加一只并勾上「启用」");
                    break;
                }
                if (PetGen.IsAll(e.PetSummon))
                    w.Line($"// 【全部召唤物】这一条对 {sacDefs.Count} 只各来一遍："
                        + string.Join("、", sacDefs.Select(d => d.DisplayName)) + "（不在场的那只自动跳过）");
                foreach (var sacDef in sacDefs)
                {
                    string sacVar = CSharpCodeGen.PetAttackVarName(sacDef.ClassName);
                    w.Line($"// 查一次这只伙伴（可能还没召唤 / 上一场战斗结束时被本体清掉了）：不在场就整条跳过");
                    // ⚠ 必须传 base.Owner.Player：生成的 XxxCmd.Get 收的是 **Player**（卡牌那条链里 base.Owner 就是 Player，
                    //    状态钩子里它是 Creature）—— 传错编译不过：CS1503 Creature → Player。
                    w.Line($"Creature? {sacVar} = {sacDef.ClassName}Cmd.Get(base.Owner.Player);");
                    w.Open($"if ({sacVar} is not null)");
                    // V0.2.210：状态链**没有** base.DynamicVars（那是卡牌 / 遗物那套），
                    // 以前这里传的是 null → 生成 `base.DynamicVars["…"].BaseValue` / `base.DynamicVars.CalculatedBlock`
                    // → 状态类里 CS0103 / CS1061，整个模组编译不过。现在：
                    //   · 固定数值档：直接用状态链算好的数值表达式（= 填的数字 / 本状态层数）；
                    //   · 按生命值算的档：inlineCalc = true → 内联读这只宠物自己的生命值（不碰任何变量）。
                    CSharpCodeGen.EmitPetSacrificePublic(w, e, p!, sacVar, petDef: sacDef, petVarOverride: sacVar,
                        inStatus: true, fixedGainExpr: amt, inlineCalc: true);
                    w.Close();
                }
                break;
            }

            // 召唤伙伴（V0.2.168，用户报的用例：自定义状态「生成卡牌时 → 召唤伙伴」）：
            // 生成端照卡牌那条链同一份 EmitSummonPet（所以「数值 0 = 用召唤物页里配的血量」、
            // 「已经在场就加最大生命不重复召唤」这些行为完全一致），只有召唤者不同 ——
            // 状态钩子里 base.Owner 是 Creature，而生成的 XxxCmd.Summon 收的是 Player。
            case "SummonPet":
                // V0.2.262（用户报「召唤所有召唤物不生效」）：这条以前把 e.PetSummon 直接丢给「单只」那套，
                // 「全部召唤物」= "*" 在单只那边**认不出来** → 只生成一句「没有可用的召唤物」，一只都不召。
                // 现在和卡牌 / 遗物一样按 ResolveMany **逐只展开**（回读靠 CET:PetAll 标记合并回一条）。
                if (PetGen.IsAll(e.PetSummon))
                {
                    var sumDefs = PetGen.ResolveMany(p!, e.PetSummon);
                    if (sumDefs.Count == 0)
                    {
                        w.Line("// ⚠ 这条「召唤伙伴」选了「全部召唤物」，但「召唤物」页里一只都没启用"
                            + "：到「召唤物」页添加一只并勾上「启用」");
                        break;
                    }
                    w.Line($"// 【全部召唤物】这一条对 {sumDefs.Count} 只各来一遍："
                        + string.Join("、", sumDefs.Select(d => d.DisplayName)));
                    // 组号：同一份配置每次生成都一样（按内容算），回读靠它把这几行合并回一条效果
                    string petAllGrp = "p" + Math.Abs(StringComparer.Ordinal.GetHashCode(
                        $"{e.PetSummon}|{e.Amount}|{e.TargetSide}|{e.UpgradeAmount}")).ToString();
                    foreach (var sumDef in sumDefs)
                        CSharpCodeGen.EmitSummonPetPublic(w, e, p!, amt, petDef: sumDef,
                            extraComment: "// CET:PetAll=" + petAllGrp);
                    break;
                }
                CSharpCodeGen.EmitSummonPetPublic(w, e, p, amt);
                break;

            // ===== 其余宠物效果（V0.2.210，用户报的用例：自定义状态「打出一张牌时（这张牌还没生效）→
            //       给伙伴施加状态」被校验拦住，对话框里把支持清单原样列了出来）=====
            // 写法和上面的「牺牲伙伴」完全一样，只有两处差别：
            //   · 「找那只宠物」只查一次（`XxxCmd.Get(base.Owner.Player)`，拿不到就整条跳过 ——
            //     伙伴没上场 / 上一场战斗结束时被本体清掉了，这在卡牌那条链上也是同样的行为，不静默做错人）；
            //   · 具体那一句**复用卡牌链的发射器**，只把「上下文表达式」换成状态链的：
            //     施加者 / 主人 = base.Owner（状态钩子里它就是 Creature，卡牌链里写的是 base.Owner.Creature）、
            //     「这张牌」= null（状态里没有 CardModel，传 this 会 CS1503，详见 EmitPetApplyPower 的说明）。
            case "PetHeal":
            case "PetLoseHp":
            case "PetGainMaxHp":
            case "PetApplyPower":
            case "PetGuardOn":
            case "PetGuardOff":
            {
                // 「下回合生效」：**不查宠物**、走卡牌链同一个入口 EmitDelayedPowerApply ——
                // 它会写 `CET:Effect=… CET:NextTurn=1` 标记行（回读认得出这条是下回合生效），
                // 现在只挂延迟状态；那个类下回合自己找这只伙伴再施加（数值在生成期折成常量）。
                if (e.NextTurn)
                {
                    // 两个实参都是 base.Owner：生成的 `PowerCmd.Apply<延迟状态>(choiceContext, 挂给谁, 层数, 施加者, null)`
                    // 第 2 / 第 4 个形参都是 **Creature**（不是 Player）—— 卡牌链那边传的是 base.Owner.Creature，
                    // 状态链的 base.Owner 本身就是 Creature。挂在这只生物身上，延迟状态里才查得到它名下的伙伴。
                    CSharpCodeGen.EmitDelayedPowerApply(w, p!, e, "base.Owner", "base.Owner");
                    break;
                }
                var petDefs = PetGen.ResolveMany(p!, e.PetSummon);
                if (petDefs.Count == 0)
                {
                    w.Line($"// ⚠ 这条「{EffectCatalog.FindKind(e.Kind).Display}」没有可用的召唤物："
                        + "到「召唤物」页添加一只并勾上「启用」");
                    break;
                }
                if (PetGen.IsAll(e.PetSummon))
                    w.Line($"// 【全部召唤物】这一条对 {petDefs.Count} 只各来一遍："
                        + string.Join("、", petDefs.Select(d => d.DisplayName)) + "（不在场的那只自动跳过）");
                foreach (var petDef in petDefs)
                {
                    string petVar = CSharpCodeGen.PetAttackVarName(petDef.ClassName);
                    w.Line("// 查一次这只伙伴（可能还没召唤 / 上一场战斗结束时被本体清掉了）：不在场就整条跳过");
                    // ⚠ base.Owner.Player：生成的 XxxCmd.Get 收的是 Player（状态钩子里 base.Owner 是 Creature）
                    w.Line($"Creature? {petVar} = {petDef.ClassName}Cmd.Get(base.Owner.Player);");
                    w.Open($"if ({petVar} is not null)");
                    switch (e.Kind)
                    {
                        case "PetHeal":
                            // 「数值 = 本状态的层数」时 amt 就是 base.Amount；否则是填的数字
                            CSharpCodeGen.EmitPetHeal(w, e, petVar, amt, valueExpr: amt);
                            break;
                        case "PetLoseHp":
                            // 主人 / 来源都换状态链的写法（否则 CS1061：状态里没有 base.Owner.Creature）
                            CSharpCodeGen.EmitPetLoseHp(w, e, petVar, amt, valueExpr: amt,
                                ownerCreatureExpr: "base.Owner", cardExpr: "null", cardPlayExpr: "null");
                            break;
                        case "PetGainMaxHp":
                            CSharpCodeGen.EmitPetGainMaxHp(w, e, petVar, amt, valueExpr: amt);
                            break;
                        case "PetApplyPower":
                            // 施加者 = base.Owner（状态钩子里它就是 Creature）、「这张牌」= null
                            CSharpCodeGen.EmitPetApplyPower(w, p!, e, petVar, amt, map: null, valueExpr: amt,
                                ownerCreatureExpr: "base.Owner", cardExpr: "null");
                            break;
                        case "PetGuardOn":
                            CSharpCodeGen.EmitPetGuard(w, p!, petVar, on: true);
                            break;
                        default:
                            CSharpCodeGen.EmitPetGuard(w, p!, petVar, on: false);
                            break;
                    }
                    w.Close();
                }
                break;
            }

            // 复制卡牌 / 重放卡牌 / 回合结束时自动打出：状态里不支持
            //（这几个都要「打出这张牌时选牌」的上下文，本体的状态钩子里没有）
            case "CopyCard":
            case "ReplayCard":
            case "TurnEndPlay":
                w.Line($"// 「{EffectCatalog.FindKind(e.Kind).Display}」不支持放在自定义状态里 —— 请把它放到卡牌或药水上。已忽略。");
                break;

            // ===== 牌堆类：从某一摞挑 N 张拿到手牌 =====
            // 用户报过的用例：追忆（增益）「玩家回合开始时 → 从弃牌堆拿 1 张到手牌」。
            // 这一类**不需要卡牌上下文**，只要拿到 choiceContext + 玩家就能做 ——
            // 而自定义状态的每个触发时机都有 choiceContext（没有的那个由 EmitChoiceContextBootstrap 造一个），
            // 所以他们在状态里完全能用。以前这里没实现、校验器也把用户拦下来，现在补上。
            case "TakeFromDraw":
            case "TakeFromDiscard":
            case "TakeFromExhaust":
            {
                string pile = e.Kind switch
                {
                    "TakeFromDraw" => "PileType.Draw",
                    "TakeFromDiscard" => "PileType.Discard",
                    _ => "PileType.Exhaust",
                };                // 提示语：状态里**没有** base.SelectionScreenPrompt（那个在 CardModel / PotionModel 上，
                // 备注里也写过它在本地化键缺失时会直接抛异常），所以用本体的静态提示常量。
                // ⚠️ 只能挑**静态**属性：CardSelectorPrefs 里的 Prompt 是**实例**属性（构造函数里才赋值），
                // 写 CardSelectorPrefs.Prompt 会得到 CS0120「对象引用对于非静态的字段、方法或属性…是必需的」
                // —— 别人的构建日志里就是这么炸的（SerenPower2.cs: error CS0120）。
                // 拿的是「从哪一摞拿」这一档，所以用那一档语义最接近的静态提示。
                string prompt = e.Kind switch
                {
                    "TakeFromDiscard" => "CardSelectorPrefs.DiscardSelectionPrompt",
                    "TakeFromExhaust" => "CardSelectorPrefs.ExhaustSelectionPrompt",
                    _ => "CardSelectorPrefs.RemoveSelectionPrompt",
                };
                // V0.2.220：「为拿到的牌加关键词」的回读标记（要写在这条效果的识别行 `var __fgTake = …` 前面）
                string kwMark = CSharpCodeGen.DrawnKeywordMarker(e);
                if (kwMark.Length > 0) w.Line(kwMark);
                // V0.2.273（用户报「从消耗牌堆拿牌限制了仅状态牌，还是可以拿其他的牌」）：
                // 这里以前**一个谓词都没传** —— 范围界定 / 只对这张卡整段被丢掉，选牌界面里什么牌都能选。
                // 判据和别的链共用 `CardScopePredicate`（同一份实现，不会两边漂）。
                string? takePred = CSharpCodeGen.CardScopePredicate(e, p);
                w.Line($"// 从{EffectCatalog.SelectPileZh(e.SelectPile == "Hand" ? "Discard" : e.SelectPile)}拿 {(e.AmountIsStack ? "本状态层数" : Lit.Int(e.Amount))} 张到手牌"
                    + (takePred is null ? "" : "（只列符合范围界定的牌）"));
                w.Line($"var __fgTake = (await CardSelectCmd.FromCombatPile(choiceContext, {pile}.GetPile(base.Owner.Player), base.Owner.Player, new CardSelectorPrefs({prompt}, (int)({amt})){(takePred is null ? "" : ", " + takePred)})).ToList();");
                w.Open("foreach (CardModel __fgTook in __fgTake)");
                w.Line("await CardPileCmd.Add(__fgTook, PileType.Hand);");
                w.Close();
                // V0.2.220：填了「为拿到的牌加关键词」就把这批牌也加上（状态里 base.Owner 是 Creature）
                CSharpCodeGen.EmitDrawnKeyword(w, p, e, "__fgTake", "base.Owner", "null");
                break;
            }

            case "CardReward":
                CSharpCodeGen.EmitCardRewardPublic(w, e, "base.Owner.Player", e.AmountIsStack, roomVar);
                break;

            case "ApplyPower":
                // 施加状态：**复用**卡牌 / 遗物 / 药水那条同一份实现（CSharpCodeGen.ApplyPowerLine），
                // 不在这里自己手写 PowerCmd.Apply —— 以前就是手写的，于是填负数时语义和另外三个上下文不一致：
                //   本体 PowerCmd.Apply 收到负数会走 ApplyInternal(target, -N, …) **新建一个 -N 层的状态**
                //   （等于凭空挂个负数），而不是「扣掉目标身上已有的 N 层」。
                // 用户实测：同样填 -3，卡牌 / 遗物 / 药水都是扣 3 层，只有自定义状态触发器会挂出一个 -3 层的新状态。
                // ApplyPowerLine 内部按 IsNegativeAmount 分流：负数先 GetPower<T>() 取已有实例再 ModifyAmount 扣。
                //
                // useX: false —— 这条链**不支持「数值 = X」**，所以不能也不该传 true：
                //   · 界面：自定义状态触发器的效果编辑器是 App.xaml 的 EffectEditorPower，只有
                //     「数值 / 生效次数 / 命中次数 = 本状态的层数」（AmountIsStack 那三个），
                //     **没有**卡牌那套「= X」（AmountIsX / TimesIsX / RepeatIsX）；
                //   · 生成：这个方法的 amt 只有两种来源 —— 勾了「= 本状态的层数」用 base.Amount，否则用填的数字；
                //     全文件搜不到任何 AmountIsX 的读取。
                // IsNegativeAmount(e, useX) 里 useX 只用来豁免「X 卡牌」（数值要到运行时才知道正负，不能按负数
                // 生成扣除）。这里没有 X：传 false 意味着**任何**负数字面量都走扣除那条路，与卡牌 / 遗物 / 药水一致
                //（同一条链里的 EmitStunPublic / EmitGiveKeyword / EmitGainPotion 也都传 useX: false）。
                //
                // cardSource 传 "null"：PowerCmd.Apply / ModifyAmount 的最后一个实参类型是 CardModel?，
                // 而状态钩子里没有「这张牌」（遗物 / 药水同理，写 this 会选错重载、整个模组编译不过，
                // 详见 CSharpCodeGen.ApplyPowerLine 重载的说明）。
                // 目标 / 施加者都写 base.Owner：这条链里 base.Owner **本身就是 Creature**（不是 Player），
                // 别自作主张加上 .Creature。
                switch (e.TargetSide)
                {
                    case "Self":
                        w.Line(CSharpCodeGen.ApplyPowerInstr(e, useX: false, e.PowerId, "base.Owner", amt, "base.Owner", cardSourceExpr: "null"));
                        CSharpCodeGen.EmitSlowPercentFix(w, e, "base.Owner");
                        break;
                    case "AllEnemies":
                        w.Open("foreach (Creature other in base.Owner.CombatState.HittableEnemies)")
                         .Line(CSharpCodeGen.ApplyPowerInstr(e, useX: false, e.PowerId, "other", amt, "base.Owner", cardSourceExpr: "null"));
                        CSharpCodeGen.EmitSlowPercentFix(w, e, "other");
                        w.Close();
                        break;
                    case "AllUnits":
                        // 全体单位（V0.2.132）：状态触发器里 `base.Owner` 本身就是 Creature，
                        // 所以直接取它自己的 CombatState（不能用卡牌那套 base.Owner.Creature）。
                        w.Open("if (base.Owner.CombatState is not null)")
                         .Open("foreach (Creature other in base.Owner.CombatState.Creatures.Where(c => c.IsAlive).ToList())")
                         .Line(CSharpCodeGen.ApplyPowerInstr(e, useX: false, e.PowerId, "other", amt, "base.Owner", cardSourceExpr: "null"));
                        CSharpCodeGen.EmitSlowPercentFix(w, e, "other");
                        w.Close()
                         .Close();
                        break;
                    case "RandomEnemies":
                        {
                            string times2 = e.RepeatIsStack ? "(int)base.Amount" : Math.Max(1, e.RepeatCount).ToString();
                            w.Open($"for (int k = 0; k < {times2}; k++)");
                            w.Line(RandomTargetLine);
                            w.Line("if (other is null) break;");
                            w.Line(CSharpCodeGen.ApplyPowerInstr(e, useX: false, e.PowerId, "other", amt, "base.Owner", cardSourceExpr: "null"));
                            CSharpCodeGen.EmitSlowPercentFix(w, e, "other");
                            w.Close();
                            break;
                        }
                    default:
                        // EmitFoeBlock 生成的变量就叫 other；下面的缓慢修正是同一批目标
                        EmitFoeBlock(w, foeFrom, foe =>
                            CSharpCodeGen.ApplyPowerInstr(e, useX: false, e.PowerId, foe, amt, "base.Owner", cardSourceExpr: "null"));
                        CSharpCodeGen.EmitSlowPercentFix(w, e, "other");
                        break;
                }
                break;

            // 击晕：状态触发器里也能用（CreatureCmd.Stun 不需要 choiceContext）
            // ⚠ selfExpr 必须传 `base.Owner`（状态钩子里 Owner 本身就是 Creature）—— 传 .Creature 会 CS1061
            case "Stun":
                CSharpCodeGen.EmitStunPublic(w, e, useX: false, cardTargetExpr: null,
                    enemiesExpr: "base.Owner.CombatState.HittableEnemies", playerExpr: "base.Owner.Player",
                    selfExpr: "base.Owner");
                break;

            // 给予卡牌关键词：状态触发器里也能给（数值 0 = 「这张牌自己」在状态里没有意义）
            case "GiveKeyword":
                CSharpCodeGen.EmitGiveKeyword(w, null, e, useX: false,
                    playerExpr: "base.Owner.Player", creatureExpr: "base.Owner", sourceExpr: "null", selfAllowed: false);
                break;

            // 强化指定卡牌：状态触发器里也能挂（挂在自己身上，只给那一张卡加伤害 / 格挡）
            case "BoostCard":
                w.Line(CSharpCodeGen.BoostMarker(e));
                w.Line($"await PowerCmd.Apply<{Naming.AmbientBoostPowerClass(e)}>(choiceContext, base.Owner, {amt}, base.Owner, null);");
                break;

            // 升级卡牌（用户报的用例：自定义状态「玩家回合开始时 → 升级手牌里的 1 张」，
            // 参考本体储君的「暴政」TyrannyPower —— 它就是在状态钩子里
            // CardSelectCmd.FromHand(choiceContext, player, prefs, null, this) 让玩家选牌再动手）。
            //
            // 为什么以前用不了：这条效果一直需要「选牌界面」，而卡牌上下文里有 base.Owner 这个 Player、
            // 状态里 base.Owner 是 **Creature** —— 生成端只给卡牌实现了它，校验器就把状态拦下了。
            // 本体自己证明状态里完全能做（EntropyPower / ToolsOfTheTradePower / TyrannyPower 都这么写），
            // 差别只是玩家要从 base.Owner.Player 取（同文件里 TakeFromDraw 那条早就是这么取的）。
            // V0.2.310（用户构建日志：「自定义状态「操控现实」的『生成卡牌时』第 1 条效果是
            // 『升级本场战斗中临时增加的牌』，生成端还没给自定义状态那条链实现」）：
            // 这一段本来就不碰卡牌上下文（只读战斗历史 + CardCmd.Upgrade），复用卡牌链同一份实现。
            // 勾了「数值 = 本状态的层数」时上界换成 base.Amount（和别的种类同一套语义）。
            case "UpgradeGenerated":
                CSharpCodeGen.EmitUpgradeGenerated(w, e, useX: false,
                    capExpr: e.AmountIsStack ? "(int)base.Amount" : null);
                break;

            case "UpgradeCard":
            {
                string pile = e.SelectPile switch
                {
                    "Draw" => "PileType.Draw",
                    "Discard" => "PileType.Discard",
                    "Exhaust" => "PileType.Exhaust",
                    _ => "PileType.Hand",
                };
                string pileZh = EffectCatalog.SelectPileZh(e.SelectPile);
                // 只挑能升级的牌（和卡牌那条一致）；范围界定（攻击 / 技能 / 自定义关键词那一组）有就一起 && 上
                string? ownerScope = CSharpCodeGen.CardScopeCond(e, p);
                string upgradable = (ownerScope is null || ownerScope.Length == 0) ? "c.IsUpgradable" : $"c.IsUpgradable && ({ownerScope})";
                // V0.2.253：挂在**没有玩家**的生物身上时（状态被贴到敌人 / 召唤物身上），
                // `base.Owner.Player` 是 null —— 以前直接把 null 传给 `PileType.Hand.GetPile(...)`，
                // 运行时空引用、效果静默失败（日志里只有一条看不懂的异常）。这里先拦一句。
                w.Line("if (base.Owner.Player is null) return;   // 这个状态挂在没有玩家的生物身上：拿不到手牌，直接跳过（不会炸）");
                // V0.2.253（用户报「自定义状态里升级能无限升级的状态牌没效果」）：
                // ① 「全部」那一档以前在状态链里没实现（会当成随机挑 N 张）→ 补上，和卡牌页一致；
                // ② 开着「生成调试日志」时把「这一摞里到底有几张能升级的牌 / 实际升了几张」打出来 ——
                //    「没效果」九成是候选 0 张（手牌里没有状态牌 / 状态牌没勾『可被无限升级』），
                //    没有这行日志只能靠猜。
                if (DebugLogGen.Enabled(p))
                {
                    string ownerName = _powerOwner?.Name ?? "";
                    w.Line($"Godot.GD.Print(\"[模组调试] 状态\" + {Lit.Str(ownerName)} + \" → 升级卡牌（{pileZh}）：可升级的牌 \" + {pile}.GetPile(base.Owner.Player).Cards.Count(c => {upgradable}).ToString() + \" 张（状态牌 / 诅咒牌要勾『可被无限升级』才能升）\");   // CET:DebugLog");
                }
                if (e.CountIsAll)
                {
                    w.Line($"// 全部：把{pileZh}里所有能升级的牌**全部**升级（不弹选牌界面）");
                    w.Line($"foreach (CardModel c in {pile}.GetPile(base.Owner.Player).Cards.Where(c => {upgradable}).ToList()) CardCmd.Upgrade(c);");
                    break;
                }
                if (e.CardPick == "Chosen")
                {
                    // ⚠️ 提示语只能用**静态**属性：CardSelectorPrefs.Prompt 是实例属性（写了会 CS0120）
                    w.Line($"// 自己从{pileZh}选 {(e.AmountIsStack ? "本状态层数" : Lit.Int(e.Amount))} 张升级（只列能升级的牌）");
                    if (pile == "PileType.Hand")
                        w.Line($"var __fgUp = (await CardSelectCmd.FromHand(choiceContext, base.Owner.Player, new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, (int)({amt})), c => {upgradable}, this)).ToList();");
                    else
                        w.Line($"var __fgUp = (await CardSelectCmd.FromCombatPile(choiceContext, {pile}.GetPile(base.Owner.Player), base.Owner.Player, new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, (int)({amt})), c => {upgradable})).ToList();");
                    w.Open("foreach (CardModel __fgUpCard in __fgUp)");
                    w.Line("CardCmd.Upgrade(__fgUpCard);");
                    w.Close();
                }
                else
                {
                    w.Line($"// 随机升级{pileZh}里的 {(e.AmountIsStack ? "本状态层数" : Lit.Int(e.Amount))} 张（只挑能升级的）");
                    w.Open($"for (int __fgUpIdx = 0; __fgUpIdx < (int)({amt}); __fgUpIdx++)");
                    w.Line($"CardModel? __fgUpPick = (base.Owner.Player?.RunState ?? base.CombatState?.RunState)?.Rng.CombatCardSelection.NextItem({pile}.GetPile(base.Owner.Player).Cards.Where(c => {upgradable}));");
                    w.Line("if (__fgUpPick is null) break;");
                    w.Line("CardCmd.Upgrade(__fgUpPick);");
                    w.Close();
                }
                break;
            }

            // 消耗卡牌 / 丢弃卡牌（用户要求：把状态里「仍然不支持」的这两条也做掉）。
            //
            // 和「升级卡牌」同一个道理：本体自己在状态钩子里就做过这两件事 ——
            //   · TyrannyPower.AfterPlayerTurnStart → FromHand(..., ExhaustSelectionPrompt, ...) → CardCmd.Exhaust
            //   · ToolsOfTheTradePower.AfterPlayerTurnStart → FromHandForDiscard(..., DiscardSelectionPrompt, ...) → CardCmd.Discard
            // 差别只有「玩家从 base.Owner.Player 取」+「提示语只能用静态常量」。
            case "ExhaustCard":
            case "DiscardCard":
            {
                bool exhaust = e.Kind == "ExhaustCard";
                string pile = e.SelectPile switch
                {
                    "Draw" => "PileType.Draw",
                    "Discard" => "PileType.Discard",
                    "Exhaust" => "PileType.Exhaust",
                    _ => "PileType.Hand",
                };
                string pileZh = EffectCatalog.SelectPileZh(e.SelectPile);
                string verb = exhaust ? "消耗" : "丢弃";
                string prompt = exhaust ? "CardSelectorPrefs.ExhaustSelectionPrompt" : "CardSelectorPrefs.DiscardSelectionPrompt";
                string srcVar = exhaust ? "__fgEx" : "__fgDis";
                // 范围界定（攻击 / 技能 / 自定义关键词那一组）有就用它当筛选条件，没有就传 null（本体写法）
                string? pickPred = CSharpCodeGen.CardScopePredicate(e, p) ?? "null";
                if (e.CardPick == "Chosen")
                {
                    w.Line($"// 自己从{pileZh}选 {(e.AmountIsStack ? "本状态层数" : Lit.Int(e.Amount))} 张{verb}");
                    if (pile == "PileType.Hand")
                    {
                        // ⚠️ 丢手牌用本体那个专用入口（奇巧牌会描金）；消耗手牌走通用 FromHand。
                        // ⚠️ 提示语只能用**静态**属性：CardSelectorPrefs.Prompt 是实例属性（写了会 CS0120）
                        string call = exhaust ? "FromHand" : "FromHandForDiscard";
                        w.Line($"var {srcVar} = (await CardSelectCmd.{call}(choiceContext, base.Owner.Player, new CardSelectorPrefs({prompt}, (int)({amt})), {pickPred}, this)).ToList();");
                    }
                    else
                    {
                        w.Line($"var {srcVar} = (await CardSelectCmd.FromCombatPile(choiceContext, {pile}.GetPile(base.Owner.Player), base.Owner.Player, new CardSelectorPrefs({prompt}, (int)({amt})){(pickPred == "null" ? "" : ", " + pickPred)})).ToList();");
                    }
                    if (exhaust)
                    {
                        w.Open($"foreach (CardModel __fgExCard in {srcVar})");
                        w.Line("await CardCmd.Exhaust(choiceContext, __fgExCard);");
                        w.Close();
                    }
                    else
                    {
                        w.Line($"await CardCmd.Discard(choiceContext, {srcVar});");
                    }
                }
                else
                {
                    string pool = CSharpCodeGen.CardScopeFiltered(e, p, $"{pile}.GetPile(base.Owner.Player).Cards");
                    w.Line($"// 随机从{pileZh}{verb} {(e.AmountIsStack ? "本状态层数" : Lit.Int(e.Amount))} 张");
                    w.Open($"for (int __fgPickIdx = 0; __fgPickIdx < (int)({amt}); __fgPickIdx++)");
                    w.Line($"CardModel? __fgPick = (base.Owner.Player?.RunState ?? base.CombatState?.RunState)?.Rng.CombatCardSelection.NextItem({pool});");
                    w.Line("if (__fgPick is null) break;");
                    w.Line(exhaust
                        ? "await CardCmd.Exhaust(choiceContext, __fgPick);"
                        : "await CardCmd.Discard(choiceContext, __fgPick);");
                    w.Close();
                }
                break;
            }

            // 临时增益（本回合 +X，回合结束撤掉）：打的是生成的临时 Power
            case "TempPower":
                // 「生成调试日志」（V0.2.142，默认关）：目标 + 实际施加的层数
                // V0.2.235：全体那两档的日志打在 foreach **外面**，不能引用循环变量 `other`（否则 CS0103，
                // 和卡牌 / 遗物链那个 ally / unit 是同一个毛病）→ 改成一句固定说明。
                DebugLogGen.Target(w, e, "base.Owner", amt, groupZh: e.TargetSide switch
                {
                    "AllEnemies" => "全部敌人（逐个动手）",
                    "AllUnits" => "全体单位（逐个动手）",
                    _ => "",
                });
                switch (e.TargetSide)
                {
                    case "Self":
                        w.Line($"await PowerCmd.Apply<{Naming.AmbientTempPowerClass(e)}>(choiceContext, base.Owner, {amt}, base.Owner, null);");
                        break;
                    case "AllEnemies":
                        w.Open("foreach (Creature other in base.Owner.CombatState.HittableEnemies)")
                         .Line($"await PowerCmd.Apply<{Naming.AmbientTempPowerClass(e)}>(choiceContext, other, {amt}, base.Owner, null);")
                         .Close();
                        break;
                    case "AllUnits":
                        // 全体单位（V0.2.132）：两边所有存活单位，各打一份临时状态
                        w.Open("if (base.Owner.CombatState is not null)")
                         .Open("foreach (Creature other in base.Owner.CombatState.Creatures.Where(c => c.IsAlive).ToList())")
                         .Line($"await PowerCmd.Apply<{Naming.AmbientTempPowerClass(e)}>(choiceContext, other, {amt}, base.Owner, null);")
                         .Close()
                         .Close();
                        break;
                    default:
                        EmitFoeBlock(w, foeFrom, foe =>
                            $"await PowerCmd.Apply<{Naming.AmbientTempPowerClass(e)}>(choiceContext, {foe}, {amt}, base.Owner, null);");
                        break;
                }
                break;

            default:
                w.Line($"// 这个效果种类在状态触发器里用不了（{e.Kind}）");
                break;
        }

        if (loop) w.Close();
        w.Close();   // 这条效果自己的作用域
    }

    /// <summary>
    /// 这条效果的「作用对象」是不是**多目标**那一档（循环里逐个动手）。
    ///
    /// 用途：调试日志要不要打印「目标是谁」按它决定 —— 多目标时目标名只有运行时才知道
    /// （打印点在循环里，引用循环变量是安全的）；单目标那两档沿用老口径（日志里没有目标段）。
    /// </summary>
    private static bool IsMultiTargetSide(EffectSpec e) =>
        e.TargetSide is "AllEnemies" or "AllUnits" or "AllAllies" or "RandomEnemies";

    /// <summary>
    /// 「全部友方」在**状态触发链**上的过滤条件（和卡牌 / 遗物链那份
    /// <see cref="CSharpCodeGen.AllyLoopWhere"/> 同一个口径，只是这里的「自己」是 <c>base.Owner</c>）。
    /// </summary>
    private static string PowerAllyWhere(EffectSpec e) =>
        "c => c.IsAlive"
        + (e.AllAlliesIncludeSelf ? "" : " && c != base.Owner")
        + CSharpCodeGen.AllyFilterSuffix(e);

    /// <summary>
    /// 按「作用对象」把「对某个生物做一件事」展开 —— **状态触发链专用**（这里的 <c>base.Owner</c> 本身就是
    /// <c>Creature</c>，写 <c>base.Owner.Creature</c> 会 CS1061）。
    ///
    /// <para>V0.2.241（用户报「触发时机增加回复生命值时……」）：以前「回复生命 / 格挡 / 失去生命 / 最大生命」
    /// 这四种在状态触发链上**完全不看作用对象**，永远只对 <c>base.Owner</c> 动手 —— 用户选了
    /// 「全体友方 / 单体敌人 / 全体敌人 / 全体单位」照样只动自己，而且不报错、不提示，属于静默忽略配置。
    /// 现在统一走这里，档位口径和「造成伤害」（<see cref="EmitDamage"/>）完全一致。</para>
    /// </summary>
    private static void EmitPowerTargets(CodeWriter w, EffectSpec e, string? foeFrom, Action<CodeWriter, string> body)
    {
        switch (e.TargetSide)
        {
            case "AllEnemies":
                w.Open("foreach (Creature other in base.Owner.CombatState.HittableEnemies)");
                body(w, "other");
                w.Close();
                break;

            case "AllUnits":
                // 全体单位：两边所有存活单位（含自己 / 队友 / 召唤物 / 敌人）
                w.Open("if (base.Owner.CombatState is not null)")
                 .Open("foreach (Creature other in base.Owner.CombatState.Creatures.Where(c => c.IsAlive).ToList())");
                body(w, "other");
                w.Close()
                 .Close();
                break;

            case "AllAllies":
                // 全部友方 = 同一边的存活单位（默认不含自己、含召唤物；两个勾选框照卡牌链那份口径）
                w.Open("if (base.Owner.CombatState is not null)")
                 .Open($"foreach (Creature other in base.Owner.CombatState.Allies.Where({PowerAllyWhere(e)}).ToList())");
                body(w, "other");
                w.Close()
                 .Close();
                break;

            case "RandomEnemies":
                {
                    // 和 EmitDamage 那条随机目标同一份写法（循环变量带 __ 前缀，外面可能还套着生效次数的 for）
                    string times = e.RepeatIsStack ? "(int)base.Amount" : Math.Max(1, e.RepeatCount).ToString();
                    w.Open($"for (int k = 0; k < {times}; k++)");
                    w.Line(RandomTargetLine);
                    w.Line("if (other is null) break;");
                    body(w, "other");
                    w.Close();
                    break;
                }

            case "Ally":
                // 状态触发链没有「玩家选中的目标」→ 和遗物 / 药水一致：退回自己
                //（校验器会提醒「指定队友」这类只对有目标的效果有意义）
                body(w, "base.Owner");
                break;

            case "Self":
                body(w, "base.Owner");
                break;

            default:   // Enemy（含老档位的写法）
                EmitFoeOnly(w, foeFrom, body);
                break;
        }
    }

    /// <summary>
    /// 「需要一个敌方目标」的那一档：优先用触发器能提供的（打出牌后 = 这张牌的目标、受到伤害后 = 打你的那个），
    /// 拿不到就从可打的敌人里挑第一个。和 <see cref="EmitFoeBlock"/> 同口径，只是把「生成一行」换成
    /// 「把一段代码写进去」（<see cref="EmitPowerTargets"/> 的 body 是 Action）。
    /// </summary>
    private static void EmitFoeOnly(CodeWriter w, string? foeFrom, Action<CodeWriter, string> body)
    {
        if (foeFrom is not null)
        {
            w.Line($"Creature? other = {foeFrom};");
            w.Line("if (other is null || !other.IsEnemy || !other.IsAlive) other = base.Owner.CombatState.HittableEnemies.FirstOrDefault();");
        }
        else
        {
            w.Line("Creature? other = base.Owner.CombatState.HittableEnemies.FirstOrDefault();");
        }
        w.Open("if (other is not null)");
        body(w, "other");
        w.Close();
    }

    /// <summary>
    /// 自定义状态的触发时机里造成的伤害。
    ///
    /// 用户要求：「自定义状态造成的伤害不能判定为角色造成的伤害」——
    /// 本体是靠 <c>dealer</c> 归因的（<c>CreatureCmd.Damage</c> 里
    /// <c>if (dealer != null &amp;&amp; dealer.Player != null &amp;&amp; target.Player == null) dealer.Player.ExtraFields.DamageDealt += …</c>），
    /// 所以这里传 **dealer: null**：伤害不计入「你造成的伤害」（本体「伤害最高」徽章、相关条件/统计都按这个走），
    /// 战斗记录里也写成「XX 受到了 N 点伤害」而不是「你造成了 N 点伤害」。
    /// 注意 <c>dealer</c> 只收非空 Creature 的重载不能传 null，必须用 7 个参数那条（末尾三个 null）。
    ///
    /// 同时保留 <c>ValueProp.Unpowered</c>：本体的力量/虚弱只对「PoweredAttack」生效
    /// （<c>props.IsPoweredAttack()</c> = 带 Move 且不带 Unpowered），所以伤害不随状态浮动；
    /// <c>dealer == null</c> 还顺带让荆棘/火焰屏障这类「被打就反弹」不会触发（它们都判 <c>dealer != null</c>）。
    /// 本体的「中毒跳伤 / 灭亡 / 窒息 / 闹鬼」这些状态造成的伤害就是这么写的（PoisonPower.Trigger 里 dealer 传 null）。
    /// </summary>
    private static void EmitDamage(CodeWriter w, EffectSpec e, string amt, string? foeFrom)
    {
        const string props = "ValueProp.Unpowered";
        switch (e.TargetSide)
        {
            case "Self":
                // 「生成调试日志」（V0.2.142，默认关）：目标 + 实际数值（状态链的 base.Owner 就是 Creature）
                DebugLogGen.Target(w, e, "base.Owner", amt);
                w.Line($"await CreatureCmd.Damage(choiceContext, base.Owner, {amt}, {props}, null, null, null);");
                break;
            case "AllEnemies":
                w.Open("foreach (Creature other in base.Owner.CombatState.HittableEnemies)");
                DebugLogGen.Target(w, e, "other", amt, inLoop: true);
                w.Line($"await CreatureCmd.Damage(choiceContext, other, {amt}, {props}, null, null, null);")
                 .Close();
                break;
            case "AllUnits":
                // 全体单位（V0.2.132）：两边所有存活单位（含自己 / 队友 / 召唤物 / 敌人）各挨一下
                w.Open("if (base.Owner.CombatState is not null)")
                 .Open("foreach (Creature other in base.Owner.CombatState.Creatures.Where(c => c.IsAlive).ToList())");
                DebugLogGen.Target(w, e, "other", amt, inLoop: true);
                w.Line($"await CreatureCmd.Damage(choiceContext, other, {amt}, {props}, null, null, null);")
                 .Close()
                 .Close();
                break;
            case "RandomEnemies":
                {
                    string times = e.RepeatIsStack ? "(int)base.Amount" : Math.Max(1, e.RepeatCount).ToString();
                    w.Open($"for (int k = 0; k < {times}; k++)");
                    w.Line(RandomTargetLine);
                    w.Line("if (other is null) break;");
                    DebugLogGen.Target(w, e, "other", amt);
                    w.Line($"await CreatureCmd.Damage(choiceContext, other, {amt}, {props}, null, null, null);");
                    w.Close();
                    break;
                }
            default:
                EmitFoeBlock(w, foeFrom, foe =>
                    DebugLogGen.TargetInline(e, "other", amt)
                    + $"await CreatureCmd.Damage(choiceContext, {foe}, {amt}, {props}, null, null, null);");
                break;
        }
    }

    /// <summary>
    /// 需要一个「敌方目标」的效果：优先用触发器能提供的那个（比如「打出牌后」就是这张牌的目标、
    /// 「受到伤害后」就是打你的那个敌人），拿不到就从可打的敌人里挑第一个。
    /// </summary>
    private static void EmitFoeBlock(CodeWriter w, string? foeFrom, Func<string, string> body)
    {
        if (foeFrom is not null)
        {
            w.Line($"Creature? other = {foeFrom};");
            w.Line("if (other is null || !other.IsEnemy || !other.IsAlive) other = base.Owner.CombatState.HittableEnemies.FirstOrDefault();");
        }
        else
        {
            w.Line("Creature? other = base.Owner.CombatState.HittableEnemies.FirstOrDefault();");
        }
        w.Open("if (other is not null)");
        w.Line(body("other"));
        w.Close();
    }
}
