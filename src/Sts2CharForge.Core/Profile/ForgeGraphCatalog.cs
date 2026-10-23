using System;
using System.Collections.Generic;
using System.Linq;
using Sts2CharForge.Core.Effects;

namespace Sts2CharForge.Core.Profile;

/// <summary>
/// 「触发时机」模块里那些时机的候选（需求第 1 条 + 后面补充的遗物两条）。
///
/// 每个时机记三件事：存在配置里的 <see cref="Id"/>、界面上的中文名、以及**它挂在哪个钩子上**
/// （生成端 <c>ForgeGraphGen</c> 按 Scope + Id 决定写进 OnPlay / 哪个 override）。
/// 可用范围：Card = 卡牌（被打出 / 被自动打出 / 卡牌钩子），Potion = 药水，Relic = 遗物。
/// </summary>
public static class ForgeTimings
{
    public const string CardPlayed = "CardPlayed";
    /// <summary>V0.2.157：「一张牌结算完毕时」（旧版的「打出一张牌后」；本体 AfterCardPlayedLate）。</summary>
    public const string CardResolved = "CardResolved";
    public const string CardAutoPlayed = "CardAutoPlayed";
    public const string CardExhausted = "CardExhausted";
    public const string CardDiscarded = "CardDiscarded";
    public const string CardDrawn = "CardDrawn";
    /// <summary>卡牌（用户要求 V0.2.128）：这张牌被保留时 —— 和「卡牌」页「卡牌时机」那一档同名同语义。</summary>
    public const string CardRetained = "Retained";
    public const string PotionUsed = "PotionUsed";
    public const string Obtained = "Obtained";
    public const string CombatStart = "CombatStart";
    public const string PlayerTurnStart = "PlayerTurnStart";
    /// <summary>V0.2.179（用户要求「触发时机加一个回合开始后」）：回合开始那个派发点的第一轮（抽牌**之前**）。</summary>
    public const string PlayerTurnStartEarly = "PlayerTurnStartEarly";
    /// <summary>V0.2.179：回合开始那个派发点的最后一轮（本体「血瓶 BloodVial」用的钩子）。</summary>
    public const string PlayerTurnStartLate = "PlayerTurnStartLate";
    public const string PlayerTurnEnd = "PlayerTurnEnd";
    public const string CombatVictory = "CombatVictory";
    public const string DamageReceived = "DamageReceived";
    public const string Attacked = "Attacked";
    public const string GoldGained = "GoldGained";
    public const string Shuffle = "Shuffle";
    // ===== 第 9 批 ④：和遗物页那份菜单（EffectCatalog.RelicTriggers）补齐的共享时机 =====
    // （Id / Display 逐字照抄遗物页那条；Hook 是本体钩子名，生成端 Signature 走 RelicTriggerSignature）
    public const string CombatEnd = "CombatEnd";
    public const string DamageDealt = "DamageDealt";
    public const string EnergySpent = "EnergySpent";
    public const string StarsSpent = "StarsSpent";
    public const string CardGenerated = "CardGenerated";
    public const string PowerReached = "PowerReached";
    public const string HpLost = "HpLost";
    public const string BlockGained = "BlockGained";
    public const string EnemyDeath = "EnemyDeath";
    public const string EnemyTurnStart = "EnemyTurnStart";
    public const string EnemyTurnEnd = "EnemyTurnEnd";
    public const string EnemyDamaged = "EnemyDamaged";
    public const string EnemyDealtDamage = "EnemyDealtDamage";
    public const string EnemyBlockGained = "EnemyBlockGained";
    public const string PowerChanged = "PowerChanged";
    public const string PowerRemoved = "PowerRemoved";
    /// <summary>遗物专用（用户要求）：当遗物被点击时。</summary>
    public const string RelicClicked = "RelicClicked";
    /// <summary>遗物专用（用户要求）：血量低于 N（N &lt; 1 时按百分比）。</summary>
    public const string HpBelow = "HpBelow";

    /// <summary>
    /// 「状态列表」（自定义状态）那一类布：V0.2.194 起锻造页也能用节点图编状态。
    /// 见 <see cref="BuildAll"/>：Power 档的时机**不是手抄的**，是从「自定义状态」页那份
    /// <c>PowerTriggers.All</c> 直接生成的（同一份菜单、同一份钩子分组）。
    /// </summary>
    public const string PowerScope = "Power";

    /// <summary>一条时机选项。Scope = Card / Potion / Relic / Power（决定它出现在哪一类物品的布上）。</summary>
    public sealed record TimingOption(string Id, string Display, string Scope, string Hook, string Hint, bool NeedCount = false, bool NeedHp = false,
        // 「盯哪个状态 / 到达 N 层」这两栏要不要显示（Power 档由 PowerTriggers 的 NeedPower 决定）
        bool NeedPower = false)
    {
        public override string ToString() => Display;
    }

    private static readonly TimingOption[] CoreTimings = new[]
    {
        // ===== 卡牌 =====
        new TimingOption(CardPlayed, "卡牌被打出时", "Card", "OnPlay",
            "本体卡牌就是 OnPlay()（打出这张牌时执行）。", false),
        new TimingOption(CardAutoPlayed, "卡牌被自动打出时", "Card", "AfterCardPlayed",
            "别人（效果）替你自动打出这张牌时走这一支；手动打出时不走。生成的是 AfterCardPlayed 钩子里判 card == this。"),
        new TimingOption(CardExhausted, "这张牌被消耗时", "Card", "AfterCardExhausted",
            "本体「战鼓」的做法：override AfterCardExhausted 里判 card == this。"),
        new TimingOption(CardDiscarded, "这张牌被丢弃时", "Card", "AfterCardDiscarded", "override AfterCardDiscarded 里判 card == this。"),
        new TimingOption(CardDrawn, "这张牌被抽到时", "Card", "AfterCardDrawn", "override AfterCardDrawn 里判 card == this。"),
        // 用户要求（V0.2.128）：新增「卡牌被保留时」。
        //   · Id = 卡牌页「卡牌时机」那一档的 Retained（同一套语义，回读 / 文案对得上）；
        //   · 本体**没有** AfterCardRetained 之类的钩子，走 AbstractModel.AfterFlush
        //     （本体丢手牌那一步会把「这回合要留下的牌」单独交出来）+ retainedCards.Contains(this)，
        //     和 CSharpCodeGen.EmitCardTimingHooks 的 case "Retained" 完全同一套判据；
        //   · ⚠ 查时机一律用 ForgeTimings.Find(id, scope)：Retained 在卡牌档只有这一条，
        //     但 CardPlayed / CardExhausted 等 id 卡牌档和遗物档都有，不带 scope 会命中错的那条。
        new TimingOption(CardRetained, "这张牌被保留时", "Card", "AfterFlush",
            "本体没有「被保留」这个钩子，它丢手牌时会把「这回合留下的牌」交出来（AfterFlush）——"
            + "判据 = 有「保留」关键词 / 这回合被单回合保留 / 整手牌都不丢。"),

        // ===== 药水 =====
        new TimingOption(PotionUsed, "药水被使用时", "Potion", "Use", "本体药水就是 Use()（喝下这瓶药水时执行）。"),

        // ===== 遗物 =====
        new TimingOption(Obtained, "获得时", "Relic", "AfterObtained", "拾取这只遗物的那一刻（本体 YummyCookie / Whetstone 那种）。"),
        new TimingOption(CombatStart, "战斗开始时", "Relic", "BeforeSideTurnStart", "每一场战斗开场。"),
        new TimingOption(PlayerTurnStart, "每回合开始时", "Relic", "AfterPlayerTurnStart", "你自己的每个回合开始。"),
        // V0.2.179（用户要求「触发时机加一个回合开始后」）：本体 Hook.AfterPlayerTurnStart 一个函数里
        // **连着派发三次**（Hook.cs:894-917）：Early → After → Late，而它是 SetupPlayerTurn 的**最后一步**
        //（回能量 → 抽牌 → 然后才调它，见 CombatManager.cs:880-926）。三条落在**三个不同的钩子方法**上，
        // 所以同一只遗物上可以各配一块【触发时机】，不会撞签名（不是「同一个时刻」那条校验拦的对象）。
        // Id / Display 逐字照抄遗物页 EffectCatalog.RelicTriggers 里的同名条目（自检有双向相等 + 逐字对照）。
        new TimingOption(PlayerTurnStartEarly, "回合开始后（抽牌前）", "Relic", "AfterPlayerTurnStartEarly",
            "和「回合开始后（抽牌后）」同一个派发点的**第一轮** —— 这时这回合的牌**还没抽**"
            + "（要改抽几张 / 抽牌前先上状态就用它）。"),
        new TimingOption(PlayerTurnStartLate, "回合开始后（最晚）", "Relic", "AfterPlayerTurnStartLate",
            "同一个派发点的**最后一轮**（本体「血瓶 BloodVial」用的就是它）—— 所有「回合开始时」的事都跑完了。"),
        new TimingOption(PlayerTurnEnd, "每回合结束时", "Relic", "AfterSideTurnEnd", "你自己的每个回合结束。"),
        new TimingOption(CombatVictory, "战斗胜利时", "Relic", "AfterCombatVictory", "打赢这一场才走。"),
        new TimingOption(DamageReceived, "受到伤害时（每一下）", "Relic", "AfterDamageReceived", "多次连击每一下都会触发一次。"),
        new TimingOption(Attacked, "受到攻击后（连击只算一次）", "Relic", "AfterAttack", "一次攻击的所有命中结束后只跑一次。"),
        new TimingOption(GoldGained, "获得金币时", "Relic", "AfterGoldGained", "拿到金币那一刻。"),
        new TimingOption(Shuffle, "抽牌堆打乱洗牌时", "Relic", "AfterShuffle", "战斗中途洗牌（开局那次不算，本体「大～抱抱」同款）。"),
        // ===== 第 9 批 ④（后半段）：和遗物页那份菜单合并成同一套 =====
        // 下面这些的 Id / Display **逐字**照抄 EffectCatalog.RelicTriggers 里那条，Hook 也照抄它的钩子名；
        // 生成端（ForgeGraphGen.Signature）在遗物档上直接 return CSharpCodeGen.RelicTriggerSignature(Id) ——
        // 签名和遗物页同一份，不会再各写一遍漂掉；归属守卫也走同一份（CSharpCodeGen.ForgeTriggerGuardLines）。
        // ⚠ 其中 CardPlayed / CardExhausted / CardDrawn / CardDiscarded 四条**和卡牌档同名**：
        //   所以查时机一律用 ForgeTimings.Find(id, scope)（按 scope 精确匹配），别用不带 scope 的那个重载。
        new TimingOption(CombatEnd, "战斗结束时（房间结算之前）", "Relic", "AfterCombatEnd",
            "本体 AfterCombatEnd：跑在房间结算（发奖励 / 存档）之前；和「战斗胜利时」一样只在打赢那条路上触发。"),
        new TimingOption(DamageDealt, "自己造成伤害后", "Relic", "AfterDamageGiven",
            "本体 AfterDamageGiven：每一下命中都触发一次（多段攻击 = 多次）；只认自己造成的伤害。"),
        new TimingOption(EnergySpent, "花掉能量后", "Relic", "AfterEnergySpent",
            "本体 AfterEnergySpent(card, amount)：只认自己花掉的能量；这个钩子没有 choiceContext，需要时生成端自己造一个。"),
        new TimingOption(StarsSpent, "花掉额外资源量后", "Relic", "AfterStarsSpent",
            "本体 AfterStarsSpent(amount, spender)：只认自己花掉的额外资源量；同样没有 choiceContext。"),
        new TimingOption(CardGenerated, "生成卡牌时", "Relic", "AfterCardGeneratedForCombat",
            "本体 AfterCardGeneratedForCombat：只算玩家自己生成的牌（敌人塞给你的状态牌不算）。"),
        new TimingOption(PowerReached, "某个状态层数到达 N 层时", "Relic", "AfterPowerAmountChanged",
            "本体 AfterPowerAmountChanged：只看自己身上的状态；「盯哪个状态 / 到达 N 层」在下面那两栏填。"),
        new TimingOption(CardPlayed, "打出一张牌时（这张牌还没生效）", "Relic", "BeforeCardPlayed",
            "本体 BeforeCardPlayed：只认自己打出的牌（敌人打牌不算）。这时这张牌**还没结算**，所以「打出它才施加的状态」不会被它自己触发。"),
        new TimingOption(CardResolved, "一张牌结算完毕时", "Relic", "AfterCardPlayedLate",
            "本体 AfterCardPlayedLate：这张牌**已经生效完了**（旧版的「打出一张牌后」就是这一档）。只认自己打出的牌。"),
        new TimingOption(CardExhausted, "消耗一张牌后", "Relic", "AfterCardExhausted",
            "本体 AfterCardExhausted：只认自己被消耗的牌。"),
        new TimingOption(CardDrawn, "抽一张牌后", "Relic", "AfterCardDrawn",
            "本体 AfterCardDrawn：只认自己抽到的牌。"),
        new TimingOption(CardDiscarded, "弃掉一张牌后", "Relic", "AfterCardDiscarded",
            "本体 AfterCardDiscarded：只认自己被弃掉的牌。"),
        new TimingOption(HpLost, "自己失去生命时（每一下）", "Relic", "AfterCurrentHpChanged",
            "本体 AfterCurrentHpChanged：delta 为正是回血，只有掉血那一下才算。"),
        new TimingOption(BlockGained, "自己获得格挡后", "Relic", "AfterBlockGained",
            "本体 AfterBlockGained：只认自己拿到格挡。"),
        new TimingOption(EnemyDeath, "一个敌人死亡后", "Relic", "AfterDeath",
            "本体 AfterDeath：只认敌人死亡（自己人死了不算）。"),
        new TimingOption(EnemyTurnStart, "敌人回合开始时", "Relic", "AfterSideTurnStart",
            "本体 AfterSideTurnStart：只认敌人那一侧。"),
        new TimingOption(EnemyTurnEnd, "敌人回合结束时", "Relic", "BeforeSideTurnEnd",
            "本体 BeforeSideTurnEnd：只认敌人那一侧。"),
        new TimingOption(EnemyDamaged, "敌人受到伤害后", "Relic", "AfterDamageReceived",
            "本体 AfterDamageReceived：受伤的是敌人（自己不算）。"),
        new TimingOption(EnemyDealtDamage, "敌人造成伤害后", "Relic", "AfterDamageGiven",
            "本体 AfterDamageGiven：打人的是敌人（自己不算）。"),
        new TimingOption(EnemyBlockGained, "敌人获得格挡后", "Relic", "AfterBlockGained",
            "本体 AfterBlockGained：拿格挡的是敌人（自己不算）。"),
        new TimingOption(PowerChanged, "某个状态层数变化后", "Relic", "AfterPowerAmountChanged",
            "本体 AfterPowerAmountChanged：升 / 降都触发；「盯哪个状态」在下面那栏填。"),
        // 「某个状态被移除时」：不是本体的 override，而是工具给 PowerModel.RemoveInternal 打补丁后
        // 调过来的一个普通方法（见 CSharpCodeGen.PowerRemovalSource）——Hook 填方法名。
        new TimingOption(PowerRemoved, "某个状态被移除时", "Relic", "OnForgePowerRemovedAsync",
            "本体没有全局的「状态被移除」钩子，这条由工具自己打补丁（用到时才多生成一个 cs/PowerRemoval.cs）。"),
        // 用户要求新增的两条：生效次数（0 = 无限）+ 用尽后变灰（生成端会挂一个计数的状态）
        new TimingOption(RelicClicked, "当遗物被点击时", "Relic", "ForgeOnRelicClicked",
            "本体的遗物点击回调（RelicModel 上没有虚方法，所以生成端会给界面节点挂一个很窄的 Harmony 补丁转发过来）。"
            + "可以填「本场战斗 / 本回合 / 本次游玩仅生效 N 次」，填 0 = 无限次。", true),
        new TimingOption(HpBelow, "血量低于 N", "Relic", "AfterCurrentHpChanged",
            "填小于 1 的值 = 按百分比算（填 0.5 就是血量低于 50%），填 ≥ 1 = 具体点数。可以填生效次数，填 0 = 无限次。", true, true),
    };

    /// <summary>
    /// 全部时机：卡牌 / 药水 / 遗物那三档（手写，逐字不动）**加上**「状态列表」那一档
    /// （由 <c>PowerTriggers.All</c> 生成 —— 用户要求「所有触发时机下拉菜单内容相同」，
    /// 状态那边本来就和遗物那份对齐，这里直接照抄它的 Id / Display / Hint，钩子分组走
    /// <c>CustomPowerGen.PowerHookOf</c>，一个字符都不重复写）。
    ///
    /// ⚠ 顺序 = 菜单顺序：Power 档接在最后，三档老菜单的顺序一个位置都不动（默认档输出不变）。
    /// ⚠⚠ 这个字段必须**声明在 CoreTimings 之后**：静态字段初始化按**文本顺序**跑，
    /// 放在前面时 CoreTimings 还是 null（实测：老存档一生成就 TypeInitializationException）。
    /// </summary>
    public static IReadOnlyList<TimingOption> All { get; } = BuildAll();

    private static IReadOnlyList<TimingOption> BuildAll()
    {
        var list = new List<TimingOption>(CoreTimings);
        foreach (var t in Effects.PowerTriggers.All)
            list.Add(new TimingOption(t.Kind, t.Display, PowerScope,
                Generation.CustomPowerGen.PowerHookOf(t.Kind), t.Hint,
                NeedCount: false, NeedHp: false, NeedPower: t.NeedPower));
        return list;
    }

    /// <summary>某一类物品能用的时机（卡牌 / 药水 / 遗物 / 状态）。</summary>
    public static IReadOnlyList<TimingOption> For(string scope) =>
        All.Where(t => string.Equals(t.Scope, scope, StringComparison.Ordinal)).ToList();

    public static TimingOption? Find(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id ?? "", StringComparison.Ordinal));

    /// <summary>
    /// 按「时机 id + 这块布的类别」查时机。
    ///
    /// 为什么需要它：第 9 批 ④ 把遗物档和遗物页那份菜单合并之后，
    /// <c>CardPlayed / CardExhausted / CardDrawn / CardDiscarded</c> 四条**两边都有、id 完全一样**
    /// （这正是「同一套菜单」的意思）。不带 scope 的 <see cref="Find(string?)"/> 只会命中先声明的那条
    /// （卡牌档），拿去做遗物图就会把「卡牌被打出时（OnPlay）」当成遗物图的时机 ——
    /// 所以生成端 / 校验器一律走这个重载。<paramref name="scope"/> 为 null = 老行为（第一条命中）。
    /// </summary>
    public static TimingOption? Find(string? id, string? scope)
    {
        var hits = All.Where(t => string.Equals(t.Id, id ?? "", StringComparison.Ordinal)).ToList();
        if (hits.Count == 0) return null;
        if (scope is null) return hits[0];
        return hits.FirstOrDefault(t => string.Equals(t.Scope, scope, StringComparison.Ordinal)) ?? hits[0];
    }

    /// <summary>
    /// 这个时机要不要显示「盯哪个状态 / 到达 N 层」那两栏（【触发时机】模块上的字段）。
    /// 只有状态层数那三条用得到（遗物页那边是 RelicSpec.TriggerPowerId / TriggerAmount）。
    /// </summary>
    public static bool NeedsPowerWatch(string? id) =>
        id is PowerReached or PowerChanged or PowerRemoved;

    /// <summary>时机的中文名（认不出来时退回 Id，界面 / 代码注释里不会出现空白）。</summary>
    public static string Zh(string? id) => Find(id)?.Display ?? (id ?? "").Trim();

    /// <summary>模块标题上用的短名（去掉「卡牌被…时」里的前缀，布上位置有限）。</summary>
    public static string ShortZh(string? id)
    {
        var opt = Find(id);
        if (opt is null) return (id ?? "").Trim();
        return opt.Display switch
        {
            "卡牌被打出时" => "打出时",
            "卡牌被自动打出时" => "自动打出时",
            "这张牌被消耗时" => "被消耗时",
            "这张牌被丢弃时" => "被丢弃时",
            "这张牌被抽到时" => "被抽到时",
            "这张牌被保留时" => "被保留时",
            "药水被使用时" => "被使用时",
            "受到伤害时（每一下）" => "受伤时",
            "受到攻击后（连击只算一次）" => "受击后",
            // V0.2.179：回合开始那三档在布上的标题短一点（完整说法在下拉里）
            "每回合开始时" => "回合开始时",
            "回合开始后（抽牌前）" => "回合开始·抽牌前",
            "回合开始后（最晚）" => "回合开始·最晚",
            _ => opt.Display,
        };
    }

    /// <summary>这个时机要不要显示「生效次数」那几个栏位。</summary>
    public static bool NeedsCount(string? id) => Find(id)?.NeedCount == true;

    /// <summary>
    /// V0.2.194：这个时机要不要显示「只对哪类牌生效」（<see cref="ForgeNodeSpec"/> 的 CardFilter 栏）。
    /// 只有「打出一张牌时 / 一张牌结算完毕时」用得到（和「自定义状态」页那份 <c>PowerTriggerSpec</c> 一个口径）。
    /// </summary>
    public static bool NeedsCardFilter(string? id) =>
        id is "CardPlayed" or "CardResolved";

    /// <summary>这个时机要不要显示「血量低于 N」那个数值栏。</summary>
    public static bool NeedsHp(string? id) => Find(id)?.NeedHp == true;

    /// <summary>生效次数的作用域候选（用户要求：本场战斗 / 本回合 / 本次游玩）。</summary>
    public static IReadOnlyList<ForgeFieldOption> CountScopes { get; } = new[]
    {
        new ForgeFieldOption("Combat", "本场战斗"),
        new ForgeFieldOption("Turn", "本回合"),
        new ForgeFieldOption("Run", "本次游玩"),
    };

    public static string CountScopeZh(string? id) => CountScopes.FirstOrDefault(o => o.Id == (id ?? "Combat"))?.Display ?? "本场战斗";

    /// <summary>把「生效次数」翻成生成代码 / 描述里的一句话（0 = 无限）。</summary>
    public static string CountZh(int count, string scope) =>
        count <= 0 ? "不限次数" : $"{CountScopeZh(scope)}仅生效 {count} 次";
}

/// <summary>
/// 锻造页能用的**全部模块**（需求里列的那 11 种）。
///
/// 端口约定（生成端和界面共用这一份定义，所以两边绝不会对不上）：
///   · 输入口只画在左边，输出口只画在右边；
///   · <see cref="ForgePort.Key"/> 在同一个模块里唯一，生成端按 Key 找上游（比按下标稳）。
/// </summary>
public static class ForgeGraphCatalog
{
    /// <summary>需求：能量模块「放置于左上角固定」。</summary>
    public const double EnergyX = 24;
    public const double EnergyY = 24;

    /// <summary>新布上【触发时机】的位置（画布正中）。</summary>
    public const double CanvasCenterX = 300;
    public const double CanvasCenterY = 170;

    /// <summary>模块宽度（界面绘制和连线锚点都用它，改一处就够）。</summary>
    public const double NodeWidth = 210;

    /// <summary>
    /// 「数值栏里也能填公式」那句话（V0.2.128 需求 1）——界面提示和 ToolTip 共用这一份，
    /// 免得界面上写一套、校验器里写另一套。
    /// 注意是**属性**不是常量了：V0.2.162 起它把内置只读量的**条数**写进去（那一批从 23 条涨到 80 条，
    /// 写死的数字会过期），常量表达式里不能读 <c>FormulaBuiltins.All.Count</c>。
    /// </summary>
    public static string NumberFormulaHint => Generation.ForgeNumbers.FormulaHint;

    /// <summary>这一栏的值是不是「必须编译期常量」（费用那两栏：本体 <c>base(cost, …)</c> 要常量）。</summary>
    public static bool NumberFieldNeedsConst(string? kindId, string fieldKey) =>
        Generation.ForgeNumbers.NeedsCompileTimeConst(kindId ?? "", fieldKey);

    /// <summary>模块标题栏高度。</summary>
    public const double HeaderHeight = 26;

    /// <summary>一行端口的高度（圆点按这个往下排）。</summary>
    public const double PortRowHeight = 22;

    // ==================== 【状态列表】模块的取值（V0.2.192） ====================

    /// <summary>「看哪些状态」= 全部 / 只看增益 / 只看减益 / 只看指定状态。</summary>
    public const string PowerListFilterAny = "Any";
    public const string PowerListFilterBuff = "Buff";
    public const string PowerListFilterDebuff = "Debuff";
    public const string PowerListFilterOne = "One";

    /// <summary>「算哪一个」= 状态数（有几个）/ 层数合计（加起来多少层）。</summary>
    public const string PowerListWhatCount = "Count";
    public const string PowerListWhatLayers = "Layers";

    /// <summary>这个模块的 Id（生成端 / 界面都按它认）。</summary>
    public const string PowerListKind = "PowerList";

    /// <summary>看哪些状态（默认「全部状态」）。</summary>
    public static string PowerListFilterOf(ForgeNodeSpec? node) =>
        (node?.Get("ListFilter", PowerListFilterAny) ?? PowerListFilterAny).Trim();

    /// <summary>算哪一个（默认「状态数」）。</summary>
    public static string PowerListWhatOf(ForgeNodeSpec? node) =>
        (node?.Get("ListWhat", PowerListWhatCount) ?? PowerListWhatCount).Trim();

    // ==================== 下拉候选 ====================

    /// <summary>「对象」模块的候选（需求第 3 条）。</summary>
    public static IReadOnlyList<ForgeFieldOption> UnitPicks { get; } = new[]
    {
        new ForgeFieldOption("Self", "自己"),
        new ForgeFieldOption("ChosenAlly", "指定队友"),
        new ForgeFieldOption("AllAllies", "全部友方"),
        new ForgeFieldOption("ChosenEnemy", "指定单体敌人"),
        new ForgeFieldOption("AllEnemies", "全部敌人"),
        new ForgeFieldOption("RandomEnemy", "随机敌人"),
        new ForgeFieldOption("ChosenSummon", "指定召唤物"),
        new ForgeFieldOption("RandomSummon", "随机召唤物"),
        new ForgeFieldOption("AllSummons", "全部召唤物"),
        new ForgeFieldOption("AllUnits", "全体单位"),
    };

    public static string UnitZh(string? id) =>
        UnitPicks.FirstOrDefault(o => o.Id == (id ?? "Self"))?.Display ?? "自己";

    // ---------- 4b.【状态列表】的两栏候选（V0.2.192：用户要求「在锻造增加状态列表」） ----------

    /// <summary>【状态列表】的「看哪些状态」四档。</summary>
    public static IReadOnlyList<ForgeFieldOption> PowerListFilters { get; } = new[]
    {
        new ForgeFieldOption(PowerListFilterAny, "全部状态"),
        new ForgeFieldOption(PowerListFilterBuff, "只看增益"),
        new ForgeFieldOption(PowerListFilterDebuff, "只看减益"),
        new ForgeFieldOption(PowerListFilterOne, "只看指定状态"),
    };

    /// <summary>【状态列表】的「算哪一个」两档。</summary>
    public static IReadOnlyList<ForgeFieldOption> PowerListWhats { get; } = new[]
    {
        new ForgeFieldOption(PowerListWhatCount, "状态数（有几个）"),
        new ForgeFieldOption(PowerListWhatLayers, "层数合计（加起来多少层）"),
    };

    public static string PowerListFilterZh(string? id) =>
        PowerListFilters.FirstOrDefault(o => o.Id == (id ?? PowerListFilterAny))?.Display ?? "全部状态";

    public static string PowerListWhatZh(string? id) =>
        PowerListWhats.FirstOrDefault(o => o.Id == (id ?? PowerListWhatCount))?.Display ?? "状态数";


    // ==================================================================================
    //  端口类型（连线时判断「能不能接」）
    // ==================================================================================
    //
    // 只有 4 种：Number（数值）/ Bool（条件）/ Object（对象）/ null（执行链）。
    // **联合类型**（NumberOrBool）是给【关系符】用的：它的左右两个口既能接数值（大于 / 小于…），
    // 也能接条件（非 / 且 / 或），所以「配不配得上」不能再拿字符串相等来判 —— 一律走 TypesCompatible。

    /// <summary>既能接数值、也能接条件（【关系符】的左右两个口）。</summary>
    public const string NumberOrBool = "NumberOrBool";

    /// <summary>
    /// 出口类型 <paramref name="source"/> 能不能接到入口类型 <paramref name="slot"/> 上。
    /// 支持联合类型（NumberOrBool 同时接受 Number 和 Bool）；null（执行链）**不**参与这里的匹配。
    /// </summary>
    public static bool TypesCompatible(string? source, string? slot)
    {
        if (source is null || slot is null) return false;
        if (string.Equals(source, slot, StringComparison.Ordinal)) return true;
        if (string.Equals(slot, NumberOrBool, StringComparison.Ordinal))
            return source is "Number" or "Bool" or NumberOrBool;
        if (string.Equals(source, NumberOrBool, StringComparison.Ordinal))
            return slot is "Number" or "Bool" or NumberOrBool;
        return false;
    }

    /// <summary>「关系符」的运算符（用户要求：大于 / 小于 / 等于 / 大于等于 / 非 / 且 / 或，可叠加）。</summary>
    public static IReadOnlyList<ForgeFieldOption> RelateOps { get; } = new[]
    {
        new ForgeFieldOption("Gt", "大于（左 &gt; 右）"),
        new ForgeFieldOption("Lt", "小于（左 &lt; 右）"),
        new ForgeFieldOption("Eq", "等于（左 = 右）"),
        new ForgeFieldOption("Ge", "大于等于（左 ≥ 右）"),
        new ForgeFieldOption("Le", "小于等于（左 ≤ 右）"),
        new ForgeFieldOption("Not", "非（左不成立）"),
        new ForgeFieldOption("And", "且（左右都成立）"),
        new ForgeFieldOption("Or", "或（任一成立）"),
    };

    /// <summary>这个运算符比的是「两个数值」还是「两个条件」。</summary>
    public static bool RelateNeedsBool(string? op) => op is "Not" or "And" or "Or";

    /// <summary>这个运算符只要一个左值（【非】）。</summary>
    public static bool RelateIsUnary(string? op) => op == "Not";

    public static string RelateOpZh(string? op) =>
        RelateOps.FirstOrDefault(o => o.Id == (op ?? "Gt"))?.Display ?? "大于";

    /// <summary>「效果」模块能选的效果种类（=「卡牌」页效果栏那一整份候选，不用另造一套）。</summary>
    public static IReadOnlyList<ForgeFieldOption> EffectPicks { get; } =
        EffectCatalog.EffectKinds.Select(k => new ForgeFieldOption(k.Kind, k.Display)).ToList();

    /// <summary>
    /// 取牌方式：「自己选」= 弹本体的选牌界面，让玩家挑；「随机」= 自动挑（节点图里两种都有用）。
    /// </summary>
    public static IReadOnlyList<ForgeFieldOption> PickModes { get; } = new[]
    {
        new ForgeFieldOption("Chosen", "自己选（弹选牌界面）"),
        new ForgeFieldOption("Random", "随机（自动挑）"),
    };

    /// <summary>「生成到」——生成出来的卡放哪一摞。</summary>
    public static IReadOnlyList<ForgeFieldOption> SpawnPiles { get; } = new[]
    {
        new ForgeFieldOption("Hand", "手牌"),
        new ForgeFieldOption("Draw", "抽牌堆"),
        new ForgeFieldOption("Discard", "弃牌堆"),
        new ForgeFieldOption("Exhaust", "消耗牌堆"),
    };

    /// <summary>
    /// 【效果】模块上「从哪里选牌」那一栏的候选 —— **直接复用「卡牌」页那份**
    ///（<see cref="EffectCatalog.SelectPiles"/>：手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆 / 牌顶），
    /// 所以两处的 id 与措辞永远一致，不会出现「锻造页选了牌顶、生成端不认」。
    /// </summary>
    public static IReadOnlyList<ForgeFieldOption> SelectPilePicks { get; } =
        EffectCatalog.SelectPiles.Select(o => new ForgeFieldOption(o.Id, o.Display)).ToList();

    /// <summary>【效果】模块上「放到哪一摞（放置卡牌用）」那一栏的候选（同上，复用「卡牌」页那份）。</summary>
    public static IReadOnlyList<ForgeFieldOption> PlaceToPicks { get; } =
        EffectCatalog.PlacePiles.Select(o => new ForgeFieldOption(o.Id, o.Display)).ToList();

    /// <summary>
    /// 「临时增益 / 临时减益」什么时候把这次变动撤掉（只有【效果】选了「临时增益」才用得到）。
    /// 直接用「卡牌 / 遗物」效果栏那份候选（<see cref="TempPowerReverses"/>），
    /// 两边的 id 一模一样 —— 生成端算类名后缀时用的是同一份（空 = 回合结束，NextTurn = 下回合开始）。
    /// </summary>
    public static IReadOnlyList<ForgeFieldOption> TempPowerReversePicks { get; } =
        TempPowerReverses.All.Select(o => new ForgeFieldOption(o.Id, o.Display)).ToList();

    /// <summary>【对象】模块的候选（需求第 3 条）：值 + 显示 + 「它会作用到几个单位」。</summary>
    public static IReadOnlyList<ForgeFieldOption> UnitPickList { get; } = UnitPicks;

    // ==================== 「目标卡 / 目标药水」的候选 ====================
    // 和「自定义关键词」那套一样用一张**静态登记表**：界面在卡牌 / 药水列表变化时灌进来，
    // 节点编辑器（它只拿得到一张图，拿不到整个存档）直接读这张表。
    // 显示名带上类名 —— 光看中文认不出是同一张牌的两个版本，光看类名认不出中文。
    private static readonly Dictionary<string, string> CardNames = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> PotionNames = new(StringComparer.Ordinal);

    /// <summary>卡牌 / 药水的候选（Id = 英文类名，Display = 「中文名（类名）」）。</summary>
    public static IReadOnlyList<ForgeFieldOption> CardChoices { get; private set; } = Array.Empty<ForgeFieldOption>();
    public static IReadOnlyList<ForgeFieldOption> PotionChoices { get; private set; } = Array.Empty<ForgeFieldOption>();

    /// <summary>界面把当前配置里的卡灌进来（改名字 / 加删卡之后要再灌一次）。</summary>
    public static void SetCardChoices(IEnumerable<(string ClassName, string Name, bool IsVanilla)> cards)
    {
        var list = new List<ForgeFieldOption>();
        CardNames.Clear();
        foreach (var (cls, name, isVanilla) in cards)
        {
            if (string.IsNullOrWhiteSpace(cls)) continue;
            string zh = string.IsNullOrWhiteSpace(name) ? cls : name.Trim();
            string disp = isVanilla ? $"【本体卡】{zh}（{cls}）" : $"{zh}（{cls}）";
            if (CardNames.ContainsKey(cls)) continue;
            CardNames[cls] = disp;
            list.Add(new ForgeFieldOption(cls, disp));
        }
        CardChoices = list;
    }

    /// <summary>界面把当前能用的药水灌进来（本体药水 + 你自己做的）。</summary>
    public static void SetPotionChoices(IEnumerable<(string Id, string Display)> potions)
    {
        var list = new List<ForgeFieldOption>();
        PotionNames.Clear();
        foreach (var (id, display) in potions)
        {
            if (string.IsNullOrWhiteSpace(id) || PotionNames.ContainsKey(id)) continue;
            string disp = string.IsNullOrWhiteSpace(display) ? id : display;
            PotionNames[id] = disp;
            list.Add(new ForgeFieldOption(id, disp));
        }
        PotionChoices = list;
    }

    /// <summary>「条件」模块能选的条件（= 卡牌页那份候选）。</summary>
    public static IReadOnlyList<ForgeFieldOption> ConditionPicks { get; } =
        EffectCatalog.Conditions.Where(c => c.ForCard).Select(c => new ForgeFieldOption(c.Id, c.Display)).ToList();

    /// <summary>「且 / 或」的门没有下拉，只把端口摆好。</summary>
    /// <remarks>
    /// ⚠ A / B 两个入口**必须带类型 "Bool"**（和【if】的「条件」那一口一样）：
    ///   积木视图靠「出口类型配不配得上入口类型」把「条件槽」和「执行链接入 / 出口」区分开
    ///   （这两类连线的出口下标都是 0，撞车）。
    /// </remarks>
    private static readonly IReadOnlyList<ForgePort> GateIn2 = new[]
    {
        new ForgePort("A", "A", "第一个输入（接一支分支）", true, "Bool"),
        new ForgePort("B", "B", "第二个输入（接另一支分支）", true, "Bool"),
    };

    private static readonly IReadOnlyList<ForgePort> GateOut1 = new[]
    {
        new ForgePort("Out", "", "两个输入都满足 / 任一满足时走这里", false, "Bool"),
    };

    // ==================== 模块表 ====================

    private static readonly List<ForgeNodeKind> AllKinds = new()
    {
        // ---------- 1.【触发时机】 ----------
        new ForgeNodeKind(
            "Trigger", "触发时机", "触发", "#B8860B",
            // 用户要求（V0.2.128）：【触发时机】是**固定模块** —— 不能被删、也不会被「全选」带走
            //（一块布只有一个触发时机，删掉整张图就不生成了：见 ForgeGraphGen.Plan）。
            "整张图的起点：决定什么时候开始执行这条链。默认「卡牌被打出时」。"
            + "这一块是**固定模块**：不能删除、也不会被「全选」带走（一块布只能有一个触发时机），"
            + "但可以照常拖动位置。",
            Array.Empty<ForgePort>(),
            new[] { new ForgePort("Out", "", "接到下一块（对象 / if / 效果…）", false) },
            new[]
            {
                new ForgeNodeField("Timing", "触发时机", ForgeFieldType.Pick, "换成别的时机（卡牌 / 药水 / 遗物各自的候选不一样）", null, "", false, true),
                new ForgeNodeField("HpThreshold", "血量低于（小于 1 = 百分比，如 0.5 = 50%）", ForgeFieldType.Number, "填 0.5 就是血量低于 50%；填 30 就是低于 30 点", null, "0.5"),
                // 第 9 批 ④：盯状态那几条时机（到达 N 层 / 层数变化 / 被移除）要的两个参数。
                // 候选和别处的 PowerId 一样（可搜索下拉，本体 200+ 状态 + 自定义状态），
                // 只在选中那几条时机时才显示（见 ForgeCanvasView.BuildField / ForgeTimings.NeedsPowerWatch）。
                new ForgeNodeField("PowerId", "盯哪个状态（状态层数类时机用）", ForgeFieldType.Pick,
                    "只有「某个状态层数到达 N 层时 / 层数变化后 / 被移除时」用得到；留空 = 自己身上任何状态都算",
                    null, "", true, true),
                new ForgeNodeField("PowerAmount", "到达 N 层（状态层数到达时用）", ForgeFieldType.Number,
                    "层数从 0 往上跨过这个数的那一下才触发（默认 1）", null, "1"),
                // V0.2.194：**状态那一档**才显示的「只对哪类牌生效」（界面上按 scope 判，见 ForgeCanvasView）。
                // 为什么要有它：「自定义状态」页那套触发时机里，「打出一张牌时 / 一张牌结算完毕时」能限定
                // 牌型 / 关键词 / 组（PowerTriggerSpec.CardFilter）；从那一页搬到布上的状态如果丢掉这一栏，
                // 就会变成「任意一张牌都触发」—— 那是**静默的行为变化**，用户最烦这个。
                // 填法就是本体那套筛选值：Attack / Skill / Power / Curse / Status / Keyword:xxx / Group:xxx，
                // 留空或 Any = 任意牌。
                new ForgeNodeField("CardFilter", "只对哪类牌生效（打牌类时机用）", ForgeFieldType.Text,
                    "留空 / Any = 任意牌；也可以填 Attack / Skill / Power / Curse / Status / Keyword:关键词 / Group:组的Id",
                    null, ""),
                new ForgeNodeField("CountScope", "生效次数的作用域", ForgeFieldType.Pick, "本场战斗 / 本回合 / 本次游玩", null, "Combat"),
                new ForgeNodeField("CountLimit", "仅生效 N 次（填 0 = 无限次）", ForgeFieldType.Number, "用完这一只遗物会像本体那样变灰并显示次数耗尽", null, "0"),
            },
            false, null),

        // ---------- 2.【if】 ----------
        // 注意：执行顺序是**结构决定**的（从【触发时机】顺着出口走），不是一条线。
        // 用户要求（V0.2.76 改的）：**「条件」改成左边的入口点** —— 这样条件那条链就是从左边进来
        //（【对象】→【状态】→【关系符】→ 结果 接到「条件」上），不用「右→右」那种别扭的连法；
        // 顺带把「接入」和「条件」**下标撞车**这个老坑彻底消掉（以前两个口都是 0，靠类型猜）。
        new ForgeNodeKind(
            "If", "if", "流程", "#2A6FB5",
            "条件判断：左边上面那个「接入」接前置（【触发时机】/【指向对象】/别的分支）；" +
            "左边下面那个「条件」接一个能出「成立 / 不成立」的模块（【条件】/【且】/【或】/【关系符】的**结果**）；" +
            "右边「是 / 否」两个出口分别走两支。",
            new[]
            {
                new ForgePort("In", "接入", "谁领到这一块（接触发时机 / 指向对象 / 别的分支）", true),
                new ForgePort("Cond", "条件", "把【条件】/【且】/【或】/【关系符】的「结果」拖到这里", true, "Bool"),
            },
            new[]
            {
                new ForgePort("True", "是", "条件成立时走这条", false),
                new ForgePort("False", "否", "条件不成立时走这条", false),
            },
            Array.Empty<ForgeNodeField>()),

        // ---------- 3.【对象】 ----------
        new ForgeNodeKind(
            "Unit", "对象", "取值", "#7A5AA5",
            "这一支链作用在谁身上（自己 / 指定敌人 / 全部敌人 / 召唤物…）。",
            new[] { new ForgePort("In", "接入", "接触发时机 / 条件 / 对象", true) },
            new[] { new ForgePort("Out", "", "接到状态 / 效果 / 数值", false) },
            new[]
            {
                new ForgeNodeField("Pick", "对象", ForgeFieldType.Pick, "选「随机敌人」时下面会多一个勾选框；选「指定召唤物」时会多出召唤物名称", UnitPicks, "Self", false, true),
                // 「召唤物名称」只在 Pick = 指定召唤物 时显示（需求），候选由界面按当前配置的召唤物列表填
                new ForgeNodeField("SummonId", "召唤物名称（选「指定召唤物」时用）", ForgeFieldType.Pick,
                    "要指定哪一只召唤物（在「召唤物」页配的那些）", null, "", true, true),
                new ForgeNodeField("AllowDuplicate", "随机目标允许重复选中同一个敌人", ForgeFieldType.Check, "默认勾选（和用户要求一致）", null, "1"),
            }),

        // ---------- 4.【状态】 ----------
        new ForgeNodeKind(
            "Power", "状态", "取值", "#0A7A6A",
            "某一个增益 / 减益：左边接它的归属对象，右边输出它的层数（接数值模块）。",
            new[] { new ForgePort("In", "归属", "这个状态挂在谁身上（接对象）", true, "Object") },
            new[] { new ForgePort("Value", "数值", "状态的层数（接数值模块）", false, "Number") },
            new[]
            {
                new ForgeNodeField("PowerId", "增益 / 减益", ForgeFieldType.Pick, "可以打字搜索（本体 200+ 个状态）", null, "", true, true),
            }),

        // ---------- 4b.【状态列表】（V0.2.192：用户要求「在锻造增加状态列表」） ----------
        //
        // 和上面那块【状态】的区别：那块读**某一个**状态的层数；这块看**一群**状态 ——
        // 「身上一共有几个状态 / 一共多少层」，还能按「只看增益 / 只看减益 / 只看某个状态」筛。
        // 于是「身上有任意减益」「状态数 ≥ N」「身上有指定状态」这类判断都能表达：
        // 把它的出口接到【关系符】上比较就行（接【if】的条件口要中间过一道【关系符】）。
        new ForgeNodeKind(
            ForgeGraphCatalog.PowerListKind, "状态列表", "取值", "#0A7A6A",
            "某一群状态：左边接「看谁身上的」（不接 = 打出牌时选中的那个目标），" +
            "右边输出「有几个状态」或「层数合计」。接到【关系符】上就能写「身上有任意减益」「状态数 ≥ N」。",
            new[] { new ForgePort("In", "归属", "看谁身上的状态（接对象）", true, "Object") },
            new[] { new ForgePort("Value", "数值", "状态数（有几个）/ 层数合计（加起来几层）", false, "Number") },
            new[]
            {
                new ForgeNodeField("ListFilter", "看哪些状态", ForgeFieldType.Pick,
                    "全部状态 / 只看增益 / 只看减益 / 只看指定状态", PowerListFilters, PowerListFilterAny, false, true),
                new ForgeNodeField("PowerId", "指定的那个状态（选「只看指定状态」时用）", ForgeFieldType.Pick,
                    "可以打字搜索（本体 200+ 个状态 + 你自己的自定义状态）", null, "", true, true),
                new ForgeNodeField("ListWhat", "算哪一个", ForgeFieldType.Pick,
                    "状态数（有几个）/ 层数合计（加起来多少层）", PowerListWhats, PowerListWhatCount, false, true),
                new ForgeNodeField("Label", "备注", ForgeFieldType.Text, "只给你自己看（不参与生成）"),
            }),

        // ---------- 5.【数值】 ----------
        new ForgeNodeKind(
            "Value", "数值", "取值", "#0A7A2A",
            "一个数值：可以直接填数字，也可以写公式（M*3、X、Y、min(2,X)…）。",
            new[] { new ForgePort("In", "接入", "可接别的数值（填了公式就以公式为准）", true, "Number") },
            new[] { new ForgePort("Out", "数值", "这个数（接效果 / 状态 / 条件）", false, "Number") },
            new[]
            {
                new ForgeNodeField("Formula", "公式 / 数值", ForgeFieldType.Formula,
                    "参数用字母（M、N…）；X = 当前所有能量，Y = 当前所有额外资源量。支持 + - * / % ( )、比较 > < >= <= == !=、逻辑 && || !、三元 条件 ? 成立时 : 不成立时（布尔当数值用时真取 1、假取 0），以及 .NET 的 Math 函数（min/max/abs/sign/clamp/floor/ceiling/round/truncate 直接写，pow/sqrt/exp/log/三角/双曲写成 Math.xxx；常量 Math.PI / Math.E / Math.Tau）",
                    null, "", false, true),
                new ForgeNodeField("Label", "备注", ForgeFieldType.Text, "只给你自己看（不参与生成）"),
            }),

        // ---------- 6.【能量】 ----------
        new ForgeNodeKind(
            "Energy", "能量", "取值", "#B06000",
            "能量与额外资源量：左上角固定。右边两个出口分别把「能量」「额外资源量」当数值送出去。",
            Array.Empty<ForgePort>(),
            new[]
            {
                new ForgePort("Energy", "能量", "当前能量（接到数值模块后可以在公式里用 X）", false, "Number"),
                new ForgePort("Resource", "额外资源量", "当前额外资源量（公式里用 Y）", false, "Number"),
            },
            new[]
            {
                new ForgeNodeField("EnergyCost", "打出时消耗的能量（填 0 = 不消耗）", ForgeFieldType.Number,
                    "需求：自动打出时若接了数值就扣这么多；没接就是 0。X = 消耗掉的能量。", null, "0"),
                new ForgeNodeField("ResourceCost", "打出时消耗的额外资源量（填 0 = 不消耗）", ForgeFieldType.Number,
                    "同上，Y = 消耗掉的额外资源量。", null, "0"),
                new ForgeNodeField("Note", "备注", ForgeFieldType.Text, "只给你自己看"),
            },
            true),

        // ---------- 7.【效果】 ----------
        new ForgeNodeKind(
            "Effect", "效果", "作用", "#C0392B",
            "真正做事的那一块：造成伤害 / 获得格挡 / 抽牌…左边接「谁造成的」（执行链），" +
            "再往左两个口分别接「多少」（接数值）和「对谁」（接对象）。",
            new[]
            {
                new ForgePort("In", "接入", "谁造成的（接对象 / 触发时机）", true),
                // 两个「收来源」的口：数值（下标 1）、指向对象（下标 2）。
                // 生成端就是按这两个下标读的（ForgeGraphGen 里 IncomingNodeAt(node, 1) / IncomingNode(node, 2)），
                // 改这里的顺序就必须同步改那边 —— 早先这两个口被误写成「出口」，
                // 结果画到右边、左边根本点不到，用户没法把【数值】接给【效果】。
                new ForgePort("Value", "数值", "这一条效果打多少：把【数值】/【状态】/【能量】的出口拖到这里", true, "Number"),
                new ForgePort("Target", "指向对象", "对谁造成：把【对象】的出口拖到这里", true, "Object"),
            },
            new[] { new ForgePort("Out", "完成", "接下一块（对象 / if / 效果…）", false) },
            new[]
            {
                new ForgeNodeField("Effect", "效果种类", ForgeFieldType.Pick, "和「卡牌」页效果栏同一份候选", EffectPicks, "Damage", false, true),
                new ForgeNodeField("PowerId", "增益 / 减益（施加 / 扣除 / 翻倍类效果用）", ForgeFieldType.Pick,
                    "可以打字搜索（本体 200+ 个状态）。数值为负数 = 扣除层数", null, "", true, true),
                new ForgeNodeField("Amount", "数值（没接数值模块时用）", ForgeFieldType.Number, "接了数值模块就以那一块为准", null, "6"),
                new ForgeNodeField("SpawnCardId", "目标卡（生成类效果用）", ForgeFieldType.Pick,
                    "要生成哪张卡（先到「卡牌」页把牌建好，这里就能选到）", null, "", true, true),
                new ForgeNodeField("SpawnTo", "生成到", ForgeFieldType.Pick, "生成出来的卡放哪", SpawnPiles, "Hand"),
                new ForgeNodeField("PotionPick", "药水（指定 / 随机）", ForgeFieldType.Pick, "指定 = 下面那栏选哪瓶；随机 = 本体药水工厂随机给一瓶",
                    new[]
                    {
                        new ForgeFieldOption("Fixed", "指定药水"),
                        new ForgeFieldOption("Random", "随机药水"),
                    }, "Fixed"),
                new ForgeNodeField("PotionId", "目标药水（指定药水时用）", ForgeFieldType.Pick,
                    "可以打字搜索（本体 200+ 瓶药水；你自己的药水也在列表里）", null, "", true, true),
                new ForgeNodeField("Pick", "取牌方式（消耗 / 丢弃 / 升级类效果用）", ForgeFieldType.Pick,
                    "自己选 = 弹本体的选牌界面；随机 = 自动挑", PickModes, "Chosen"),
                // V0.2.250（用户问「这个范围怎么不能选组或者特定关键词」）：图上【效果】模块以前**压根没有
                // 「范围」这一栏** —— 卡牌页那一行「范围界定（哪些牌能选）」在布上没有对应栏位，
                // 于是「减少卡牌费用」这类效果在锻造卡上没法选「哪一类牌 / 哪个组」。
                // 候选在界面上从当前存档动态取（<c>EffectCatalog.SpawnFiltersFor</c>：含自定义关键词组 / 每个组）。
                new ForgeNodeField("SpawnFilter", "范围（哪一类牌 / 哪个组）", ForgeFieldType.Pick,
                    "限定「哪一类牌」：减少卡牌费用 / 消耗 / 丢弃 / 升级 / 给关键词 / 抽牌类效果都用它。"
                    + "攻击 / 技能 / 能力按类型，诅咒 / 状态按稀有度；「自定义关键词：…」= 把某个关键词当成一套卡的组；"
                    + "「组：…」= 你在「组」页建的那一组。留空 = 不限。", null, ""),
                new ForgeNodeField("CostDownThisTurn", "只持续本回合（减少卡牌费用用）", ForgeFieldType.Check,
                    "不勾 = 本场战斗一直有效（像本体「腐化」）；勾上 = 玩家回合结束时自动撤掉。", null, ""),
                // V0.2.212（用户问「放置卡牌怎么选择放在哪里」）：这两栏以前**只有卡牌 / 遗物 / 药水 / 状态页
                // 的效果栏有**，锻造页【效果】模块上根本没有 → 图上做「放置卡牌 / 消耗卡牌」时
                // 从哪一摞拿、放到哪一摞都只能吃默认值（都是手牌），用户找不到地方选。
                new ForgeNodeField("SelectPile", "从哪里选牌（消耗 / 丢弃 / 升级 / 变化 / 复制 / 放置类用）", ForgeFieldType.Pick,
                    "和「卡牌」页那一栏同一份候选。手牌 = 本体默认；抽牌堆 / 弃牌堆 / 消耗牌堆走本体的 FromCombatPile；"
                    + "「牌顶」= 抽牌堆最上面那张（不用挑、也不随机）。留空 = 手牌。", SelectPilePicks, "Hand"),
                new ForgeNodeField("PlaceTo", "放到哪一摞（放置 / 复制卡牌用）", ForgeFieldType.Pick,
                    "数值 0 = 把**这张牌自己**放到这里（复制卡牌 = 复制这张牌自己）；数值 ≥ 1 = 把从上面「从哪里选牌」挑出来的那几张放到这里（复制卡牌 = 复制出来的那几张）。"
                    + "「牌顶」= 抽牌堆最上面（本体 CardPilePosition.Top），「抽牌堆」那一档是牌堆底。留空 = 手牌。",
                    PlaceToPicks, "Hand"),
                new ForgeNodeField("Times", "生效次数", ForgeFieldType.Number, "默认 1", null, "1"),
                // V0.2.220（用户要求「在抽牌/拿牌等效果下额外填加一条『为抽到的牌添加永久/临时关键词』的选项」）：
                // 和「卡牌」页效果栏那两栏同一份口径（EffectFieldPolicy.UsesDrawnKeyword 决定在哪几档上出现）。
                new ForgeNodeField("DrawnKeyword", "为抽到的牌加关键词（抽牌 / 拿牌类用）", ForgeFieldType.Pick,
                    "这一条效果**真的抽到 / 拿到手**的那几张牌加上这个关键词（不是整个手牌）。"
                    + "「（不加关键词）」= 老行为（生成文本一个字都不变）。候选 = 本体那 7 个 + 你自己造的关键词。",
                    null, ""),
                new ForgeNodeField("DrawnKeywordTemp", "抽到的牌的关键词只是临时的", ForgeFieldType.Check,
                    "勾上 = 这些抽到的牌只这一回合带着它（保留 / 奇巧走本体的单回合标记，其它关键词含自定义的在回合结束时被摘掉）。",
                    null, "0"),                new ForgeNodeField("Hits", "命中次数", ForgeFieldType.Number,
                    "默认 1（伤害类会按多段攻击写）。\n"
                    + "· 「伤害」：多段攻击的**段数**；对象是「随机敌人」时 = **随机打 N 下**（每一下随机挑一个敌人，本体 TargetingRandomOpponents 的语义），"
                    + "不会变成「每个敌人各挨 N 下」。\n"
                    + "· 其它按单位施放的效果（施加状态 / 格挡 / 回血…）：对象是「随机敌人」时 = **随机挑 N 个敌人**（和「卡牌」页同一口径）。",
                    null, "1"),
                new ForgeNodeField("Chance", "概率生效（%，100 = 必定）", ForgeFieldType.Number, "低于 100 时生成一个随机判定", null, "100"),
                // 「临时增益」多这一栏：效果栏那边叫「临时增益什么时候撤」，这里同一份候选。
                // 空 = 本回合结束时撤（本体的灵活药剂那套）；「NextTurn」= 撑过这一回合和敌人的回合、下回合开始才撤。
                // 用户报过「临时增益不生效」：图里以前**没有**这一栏，而且选了「临时增益」也只会施加永久状态。
                new ForgeNodeField("Reverse", "什么时候撤掉（只有「临时增益」用得到）", ForgeFieldType.Pick,
                    "空 = 本回合结束时撤掉；选「下回合开始时才撤掉」= 这一回合 + 敌人的回合都保留", TempPowerReversePicks, TempPowerReverses.TurnEnd),
                new ForgeNodeField("NextTurn", "下回合生效", ForgeFieldType.Check, "勾上 = 下回合开始时才发生（「临时增益」不要勾这个，用上面那栏）", null, "0"),
            }),

        // ---------- 8.【且】 ----------
        new ForgeNodeKind("And", "且", "逻辑", "#444444",
            "与门：两支都满足时右边才成立（左边两个入口各接一个条件）。", GateIn2, GateOut1, Array.Empty<ForgeNodeField>()),

        // ---------- 9.【或】 ----------
        new ForgeNodeKind("Or", "或", "逻辑", "#444444",
            "或门：任一支满足时右边就成立。", GateIn2, GateOut1, Array.Empty<ForgeNodeField>()),

        // ---------- 10.【条件】 ----------
        new ForgeNodeKind(
            "Condition", "条件", "条件", "#8A6D00",
            "本体现成的那些条件（手牌数、生命值百分比、拥有某状态…）。右边输出「成立 / 不成立」，接到【if】/【且】/【或】。",
            new[] { new ForgePort("Value", "N", "可接【数值】：让条件里的 N 变成运行时算出来的（手牌数 / 生命百分比 / 状态层数 / 额外资源量）", true, "Number") },
            new[] { new ForgePort("Out", "结果", "满足 / 不满足", false, "Bool") },
            new[]
            {
                new ForgeNodeField("Cond", "条件", ForgeFieldType.Pick, "和「卡牌」页条件栏同一份候选", ConditionPicks, "None", false, true),
                new ForgeNodeField("Amount", "数值 N", ForgeFieldType.Number, "条件里那个 N（填负数 = 反向判断，见卡牌页说明）", null, "1"),
                new ForgeNodeField("Amount2", "数值 M（生命区间用）", ForgeFieldType.Number, "只有「生命值在 N%~M% 之间」用得到", null, "0"),
                new ForgeNodeField("PowerId", "状态（拥有某状态用）", ForgeFieldType.Pick, "只有「拥有某状态至少 N 层」用得到", null, "", true, true),
                // V0.2.247（用户要求）：【状态】旁边这两栏 —— 只有对应的条件才显示（见 ForgeCanvasView 的可见性）
                new ForgeNodeField("Keyword", "关键词（上一张打出的牌带有某关键词用）", ForgeFieldType.Pick,
                    "只有「上一张打出的牌带有某关键词」用得到：本体关键词（消耗 / 虚无 / 固有 / 保留 / 不能被打出 / 奇巧 / 永恒）。",
                    null, ""),
                new ForgeNodeField("Scope", "范围（上一张打出的牌是某种范围的牌用）", ForgeFieldType.Pick,
                    "只有「上一张打出的牌是某范围」用得到：攻击 / 技能 / 能力 / 诅咒 / 状态 / 任务 / 自定义关键词那一组 / 每个组。",
                    null, "", true),
                // V0.2.218（用户要求「锻造里没法做『处于某种条件下时这张牌不能打出』」）：
                // 和「卡牌」页那个「条件」的「不满足时：这条效果不生效 / 整张牌打不出去」是同一个口径。
                // 只有卡牌布上有意义（遗物 / 药水 / 状态没有「打不出去」这回事）。
                new ForgeNodeField("WhenUnmet", "不满足时", ForgeFieldType.Pick,
                    "「跳过这一支」（默认）= 条件不满足时这一支什么都不做；"
                    + "「整张牌打不出去」= 这张牌在条件不满足时**根本不能打出**（卡面描金边 = 现在可以打；"
                    + "和本体「冲突 / 死亡之门」那种一样）。只有卡牌布上有意义。",
                    new[]
                    {
                        new ForgeFieldOption("Pass", "跳过这一支（默认）"),
                        new ForgeFieldOption("Unplayable", "整张牌打不出去"),
                    }, "Pass"),
            }),

        // ---------- 11.【指向对象】（用户要求：紧接【触发时机】，声明这张牌在游戏里选中谁）----------
        // 语义（用户原话）：「这个『指向对象』的意思是卡牌在游戏中将选中谁」。
        // 所以它**不是**「这条效果打谁」（那是【效果】左边那个「指向对象」口接的【对象】），
        // 而是**整张牌的**目标类型：决定了 cardPlay.Target 有没有值（也就决定了「指定单体敌人」能不能用）。
        // 摆位：它就在执行链上（触发时机 → 它 → if / 效果…），同时它也是这条链上
        // 「没自己接【对象】的效果」的默认目标。
        new ForgeNodeKind(
            "Target", "指向对象", "触发", "#8B5A2B",
            "这张牌在游戏里**选中谁**：决定卡牌的目标类型（会选一个敌人 / 全体 / 不需要选）。" +
            "它就在【触发时机】后面，右边接【if】/【效果】；它也是这条链上「没自己接【对象】的效果」的默认目标。" +
            "注意和【效果】左边那个「指向对象」口区分：那个是「这条效果打谁」，可以不一样（例：牌选一个敌人，效果打全体）。",
            new[] { new ForgePort("In", "接入", "接【触发时机】（或上一块）", true) },
            new[]
            {
                new ForgePort("Out", "", "接下一块（if / 效果…）", false),
                new ForgePort("Unit", "指向对象", "把这一块当对象用：接到【效果】的「指向对象」口 / 【状态】的「归属」口", false, "Object"),
            },
            new[]
            {
                new ForgeNodeField("Pick", "选中谁", ForgeFieldType.Pick,
                    "「指定单体敌人」= 打出这张牌时会让玩家选一个敌人（卡牌目标类型 = 单体）；" +
                    "「全部敌人 / 随机敌人」同理；「自己 / 召唤物」= 不需要选目标",
                    UnitPicks, "Self", false, true),
                new ForgeNodeField("SummonId", "召唤物名称（选「指定召唤物」时用）", ForgeFieldType.Pick,
                    "要指定哪一只召唤物（在「召唤物」页配的那些）", null, "", true, true),
                new ForgeNodeField("AllowDuplicate", "随机目标允许重复选中同一个敌人", ForgeFieldType.Check, "默认勾选；只对「随机敌人」有意义", null, "1"),
            }),

        // ---------- 12.【关系符】（用户要求：判断条件是否成立，可叠加）----------
        // 用户要的接线：if 的「条件」口 ← 【关系符】 ← 左【状态】/【数值】、右【数值】。
        // 左值 / 右值都是**联合类型**（既能接数值，也能接条件）：因为「非 / 且 / 或」比的是两个条件。
        // 端口下标说明：左值放 1、右值放 2（和【且】【或】的 A / B 同一个道理）——
        // 下标 0 和出口 0 撞车，积木视图从下标认不出「这是插槽还是执行链」（见 ForgeBlockTree 的注释）。
        new ForgeNodeKind(
            "Relate", "关系符", "条件", "#7A4A8A",
            "判断「左值和右值的关系」：大于 / 小于 / 等于 / 大于等于 / 小于等于 / 非 / 且 / 或。" +
            "左边「左值」接【状态】/【数值】（或另一个【关系符】的结果），左边「右值」接【数值】（不接就用上面填的数字），" +
            "右边「结果」接【if】的「条件」口 —— 可以多个【关系符】叠起来用（且 / 或 / 非）。",
            new[]
            {
                new ForgePort("In", "接入", "这一块不用接链，留空即可", true),
                new ForgePort("L", "左值", "要比的那个值：接【状态】/【数值】；选「非 / 且 / 或」时接的是条件（【条件】/【且】/【或】/【关系符】）", true, NumberOrBool),
                new ForgePort("R", "右值", "拿什么比：接【数值】；选「且 / 或」时接的是另一个条件。留空 = 用上面填的数字", true, NumberOrBool),
            },
            new[] { new ForgePort("Out", "结果", "成立 / 不成立（接【if】的「条件」口，或另一个【关系符】）", false, "Bool") },
            new[]
            {
                new ForgeNodeField("Op", "运算符", ForgeFieldType.Pick, "大于 / 小于 / 等于 / 大于等于 / 小于等于 / 非 / 且 / 或", RelateOps, "Gt", false, true),
                new ForgeNodeField("Amount", "右值（没接【数值】时用这个数字）", ForgeFieldType.Number, "选「非 / 且 / 或」时这一栏没用（比的是条件）", null, "3"),
            }),
    };

    public static IReadOnlyList<ForgeNodeKind> All { get; } = AllKinds;

    public static ForgeNodeKind? Find(string? id) =>
        AllKinds.FirstOrDefault(k => string.Equals(k.Id, id ?? "", StringComparison.Ordinal));

    /// <summary>新建模块时的默认位置（按种类错开一点，别叠在一起）。</summary>
    public static (double X, double Y) DefaultPosition(string kind, int existingCount)
    {
        if (string.Equals(kind, "Energy", StringComparison.Ordinal)) return (EnergyX, EnergyY);
        int i = Math.Max(0, existingCount);
        return (80 + (i % 4) * 40, 60 + (i % 6) * 34);
    }
}
