using Sts2CharForge.Core.Generation;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Effects;

/// <summary>
/// 一条「触发时机」选项。
/// Kind     = 配置里存的 id
/// Display  = 界面上显示的中文
/// Hint     = 界面上那句补充说明（挂在本体哪个钩子上）
/// NeedCardFilter = 只有「打出牌后」需要选牌类型
/// NeedPower      = 只有「某个状态层数变化后」需要选「是哪个状态」
/// </summary>
public sealed record PowerTriggerOption(string Kind, string Display, string Hint, bool NeedCardFilter = false, bool NeedPower = false,
    // 「到达 N 层」那种需要填一个数值的时机（用户要求：两边菜单相同 → 状态这边也要能填 N）
    bool NeedAmount = false)
{
    public override string ToString() => Display;
}

public static class PowerTriggers
{
    /// <summary>
    /// 自定义状态能用的触发时机。
    /// 全部对着本体源码确认过：这些都是 AbstractModel 上的 virtual 钩子，由 Hook.cs 统一派发给
    /// 「ShouldReceiveCombatHooks == true」的模型（PowerModel 就是 true），所以自定义状态能吃它们。
    /// </summary>
    public static IReadOnlyList<PowerTriggerOption> All { get; } = new[]
    {
        new PowerTriggerOption("PlayerTurnStart", "玩家回合开始时",   "AfterPlayerTurnStart：本体恶魔形态就是挂这个（老存档里的 TurnStart 也认）"),
        new PowerTriggerOption("PlayerTurnEnd",  "玩家回合结束时",   "BeforeSideTurnEnd：本体缠身/战斗回合结算时用（老存档里的 TurnEnd 也认）"),
        // V0.2.157：这一档以前用的是 AfterCardPlayed（= **结算完**才回调），于是「打这张牌才施加的这个状态」
        // 会在**同一张牌**上立刻触发一次（用户实测报的：实际变成了「打出一张牌后」，和「一张牌结算完毕时」
        // 几乎一样）。现在改用本体的 **BeforeCardPlayed**（真正的「打出的那一刻」：这张牌的效果还没跑，
        // 所以它自己施加的状态还不存在 → 不会被自己触发）。
        // 「结算完」那一档单独留成 CardResolved（见下），语义不丢。
        new PowerTriggerOption("CardPlayed",    "打出一张牌时（这张牌还没生效）", "BeforeCardPlayed：这张牌**还没结算**，所以「打出它才施加的状态」不会被它自己触发。可以只对攻击/技能/能力牌生效", NeedCardFilter: true),
        new PowerTriggerOption("CardResolved",  "一张牌结算完毕时",     "AfterCardPlayedLate：这张牌**已经生效完了**（旧版的「打出一张牌后」就是这一档）。可以只对攻击/技能/能力牌生效", NeedCardFilter: true),
        new PowerTriggerOption("CardExhausted", "消耗一张牌后",       "AfterCardExhausted：本体无痛感（获得格挡）用这个"),
        new PowerTriggerOption("CardDrawn",     "抽一张牌后",         "AfterCardDrawn"),
        new PowerTriggerOption("CardDiscarded", "弃掉一张牌后",       "AfterCardDiscarded"),
        // 「自己受到伤害后」= 本体 AfterDamageReceived：**每一下伤害都会触发一次**。
        // 本体的敌人攻击很多是「1 点 × 5 下」这种多次连击（AttackCommand.WithHitCount /
        // MultiAttackIntent），所以一次「5 点伤害」的攻击会触发 5 次（本体的原体黏土 SelfFormingClay
        // 也是这个行为）。想「一次攻击只算一次」用下面那条「自己受到攻击后」。
        new PowerTriggerOption("DamageReceived", "自己受到伤害后（每一下）", "AfterDamageReceived：多次连击会触发多次（老存档里的 DamageTaken 也认）"),
        // 「自己失去生命时」= 本体 AfterCurrentHpChanged 里 delta < 0 的那一半（本体「肉骨头 MeatOnTheBone」
        // 与亡灵契约师的 NecroMasteryPower 用的就是这个钩子）。和上面那条的区别：
        //   · 「受到伤害后」走的是**伤害管线**（AfterDamageReceived，判 UnblockedDamage > 0）；
        //   · 这条认的是**生命值真的少了**这一件事 —— 伤害、自己「失去生命」、直接扣血（例：致死那套
        //     LoseHpInternal）都算，回血（delta > 0）不算。所以它把上一条完全包含在内。
        // 同样是**每一下都会触发一次**（「1 点 × 5 下」的连击就是 5 次）。
        new PowerTriggerOption("HpLost",        "自己失去生命时（每一下）", "AfterCurrentHpChanged：delta < 0 时触发（伤害 / 失去生命 / 直接扣血都算，回血不算）"),
        // 一次攻击只触发一次：本体的 AfterAttack 注释里写明了 —— multi-attack 时它在**所有命中结束之后**
        // 只跑一次（AfterDamageGiven / AfterDamageReceived 是每一下都跑）。
        new PowerTriggerOption("Attacked",      "自己受到攻击后（连击只算一次）", "AfterAttack：一次攻击的所有命中结束后只跑一次"),
        new PowerTriggerOption("DamageDealt",   "自己造成伤害后",     "AfterDamageGiven"),
        new PowerTriggerOption("BlockGained",   "自己获得格挡后",     "AfterBlockGained"),
        new PowerTriggerOption("EnemyDeath",    "一个敌人死亡后",     "AfterDeath"),
        new PowerTriggerOption("CombatStart",   "战斗开始时",         "BeforeCombatStart"),
        new PowerTriggerOption("EnergySpent",   "花掉能量后",         "AfterEnergySpent"),
        new PowerTriggerOption("StarsSpent",    "花掉额外资源量后",   "AfterStarsSpent"),
        // 生成卡牌时（用户要求）：本体 AfterCardGeneratedForCombat —— **玩家自己生成**的牌才算
        //（敌人塞给你的状态牌不算，本体注释写明了），每生成一张触发一次。钩子不带 choiceContext。
        new PowerTriggerOption("CardGenerated", "生成卡牌时",         "AfterCardGeneratedForCombat：玩家自己生成一张牌时触发（敌人塞的不算）"),
        // ===== 敌人侧（用户要求新增）=====
        new PowerTriggerOption("EnemyTurnStart",  "敌人回合开始时", "AfterSideTurnStart：side == Enemy 时触发"),
        new PowerTriggerOption("EnemyTurnEnd",    "敌人回合结束时", "BeforeSideTurnEnd：side == Enemy 时触发"),
        new PowerTriggerOption("EnemyDamaged",    "敌人受到伤害后", "AfterDamageReceived：受伤的是敌人时触发"),
        new PowerTriggerOption("EnemyDealtDamage","敌人造成伤害后", "AfterDamageGiven：打人的是敌人时触发"),
        new PowerTriggerOption("CombatVictory",   "战斗胜利后",   "AfterCombatEnd：打赢这一场才会走（打输了不走）。「获得卡牌奖励」会挂进本场战斗的结算奖励里，跟本体「王国资产」一样"),
        new PowerTriggerOption("EnemyBlockGained","敌人获得格挡后", "AfterBlockGained：拿格挡的是敌人时触发"),
        new PowerTriggerOption("PowerChanged",  "某个状态层数变化后", "AfterPowerAmountChanged：可以只盯某一个状态（留空 = 任意状态）", NeedPower: true),
        // 移除某状态时（用户要求）：本体没有全局的「状态被移除」钩子（`PowerModel.AfterRemoved` 是
        // 那个状态自己的实例钩子），所以这条由工具自己打补丁通知（见 CSharpCodeGen.PowerRemovalSource）。
        // 可以只盯某一个状态（留空 = 自己身上任意状态被移除都触发）。
        new PowerTriggerOption("PowerRemoved",  "某个状态被移除时", "工具补丁（PowerModel.RemoveInternal）：本体没有全局的「状态被移除」钩子，这条是工具自己补的 —— 可以只盯某一个状态（留空 = 任意状态）", NeedPower: true),
        // ===== 用户要求「遗物 / 状态 / 卡牌的触发时机合并成同一套」后补上的（遗物那边早就有的两条）=====
        // 获得金币时：本体 AfterGoldGained(player)——钩子**不带 choiceContext**（生成端自己造一个）。
        new PowerTriggerOption("GoldGained", "获得金币时", "AfterGoldGained：拿到金币时触发（结算 / 事件给的钱也算）"),
        // 抽牌堆打乱洗牌时：本体 AfterShuffle(choiceContext, shuffler)——只认自己洗的牌（本体「大～抱抱」那套）。
        new PowerTriggerOption("Shuffle", "抽牌堆打乱洗牌时", "AfterShuffle：战斗中途洗牌时触发（开局那次洗牌不算）"),
        // ===== 用户要求「所有触发时机下拉菜单包含的内容相同」：遗物侧还有的这三条也列进来 =====
        // 战斗结束时（房间结算之前）：和「战斗胜利后」是**同一个本体钩子**（AfterCombatEnd），
        // 所以生成时和它归到一组、只写一个方法（见 CustomPowerGen.HookOf）。
        new PowerTriggerOption("CombatEnd", "战斗结束时（房间结算之前）", "AfterCombatEnd：比「战斗胜利后」更早（房间结算之前）；本体钩子其实是同一个"),
        // 某个状态层数到达 N 层时：本体 AfterPowerAmountChanged（和「某个状态层数变化后」同一个钩子），
        // 但**多判一条**「这一次变化跨过了 N 层」——N 填在触发器那一栏（见 PowerTriggerSpec.TriggerAmount）。
        new PowerTriggerOption("PowerReached", "某个状态层数到达 N 层时", "AfterPowerAmountChanged：层数跨过 N 那一下才触发（N 填在下面那栏）", NeedPower: true, NeedAmount: true),
        // 「获得时」：状态是**被施加**的，没有「拾取」这一说——列出来是为了两边菜单一模一样；
        // 选中了校验器会明确报错说清（生成端也留一句注释，不会静默什么都不做）。
        new PowerTriggerOption("Obtained", "获得时", "AfterObtained：遗物才有「拿到的那一刻」；状态用不了（校验器会拦住）"),
    };

    /// <summary>
    /// 老存档 / 老工程里的**旧 id → 现在的 id**。
    ///
    /// 用户要求「所有触发时机下拉菜单内容相同」之后，状态侧三条改了 id（改成和遗物侧同一个 id）：
    ///   TurnStart → PlayerTurnStart、TurnEnd → PlayerTurnEnd、DamageTaken → DamageReceived。
    /// 老存档里存的是旧 id，所以这里做一层别名：读取 / 生成 / 回读三处都先过 <see cref="Canonical"/>。
    /// </summary>
    private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal)
    {
        ["TurnStart"] = "PlayerTurnStart",
        ["TurnEnd"] = "PlayerTurnEnd",
        ["DamageTaken"] = "DamageReceived",
    };

    /// <summary>把一个（可能是老的）时机 id 规范化成现在的 id。</summary>
    public static string Canonical(string? kind) =>
        kind is not null && LegacyAliases.TryGetValue(kind, out string? now) ? now : (kind ?? "");

    public static PowerTriggerOption? Find(string? kind)
    {
        if (kind is null) return null;
        string canonical = Canonical(kind);
        return All.FirstOrDefault(t => string.Equals(t.Kind, kind, StringComparison.Ordinal))
            ?? All.FirstOrDefault(t => string.Equals(t.Kind, canonical, StringComparison.Ordinal));
    }

    /// <summary>「打出牌后」能过滤的牌类型（只有 id，界面上那份带中文的见 <see cref="CardFiltersFor"/>）。</summary>
    public static IReadOnlyList<string> CardFilters { get; } = new[] { "Any", "Attack", "Skill", "Power" };

    /// <summary>
    /// 「打出牌后」的「只对哪类牌生效」候选（用户要求：**增加选择范围**）。
    ///
    /// V0.2.118 起和卡牌那边的「范围界定」**共用同一份选项表**（<see cref="EffectCatalog.SpawnFiltersFor"/>），
    /// 于是这里除了原来的 任意 / 攻击 / 技能 / 能力，还多了：
    /// 诅咒 / 状态 / 每个「自定义关键词」一组 / 每个「组」一组。
    /// 「任意」沿用了老 id <c>Any</c>（老存档与老工程回读都不受影响）。
    /// </summary>
    public static IReadOnlyList<SpawnFilterOption> CardFiltersFor(CharacterProfile? p)
    {
        var list = new List<SpawnFilterOption> { new("Any", "任意牌（不过滤）", "任意") };
        foreach (var f in EffectCatalog.SpawnFiltersFor(p))
            if (f.Id.Length > 0) list.Add(f);   // "" = 「不限」，跟「任意」重复，跳过
        return list;
    }

    /// <summary>「只对哪类牌生效」的中文短名（列表 / 说明 / 回读用）。</summary>
    public static string CardFilterZh(string? v)
    {
        string f = (v ?? "").Trim();
        if (f.Length == 0 || f == "Any") return "任意";
        if (EffectCatalog.IsKeywordFilter(f)) return "关键词：" + EffectCatalog.SpawnFilterZh(f);
        if (EffectCatalog.IsCardGroupFilter(f)) return "组：" + EffectCatalog.SpawnFilterZh(f);
        return f switch { "Attack" => "攻击", "Skill" => "技能", "Power" => "能力", "Curse" => "诅咒", "Status" => "状态", _ => f };
    }

    /// <summary>
    /// 「某个状态层数变化后」盯的是谁身上的状态。
    /// 为什么只有两个：那个钩子（AfterPowerAmountChanged）没有「这张牌打的目标」，
    /// 所以「指定敌人」在它这儿没有对应物；「全部敌人」对「层数变化」这种逐次事件也没意义。
    /// </summary>
    public static IReadOnlyList<ConditionTargetOption> WatchTargets { get; } = new[]
    {
        new ConditionTargetOption("Self", "自己"),
        new ConditionTargetOption("AnyEnemy", "任意敌人"),
    };

    public static string WatchTargetZh(string? v) =>
        WatchTargets.FirstOrDefault(t => t.Id == (v ?? "Self"))?.Display ?? "自己";

    /// <summary>
    /// 自制状态的触发器里能用的效果种类（别的种类需要卡牌上下文，生成时会被校验拦住）。
    /// 全局（改牌组）那三种和「获得卡牌奖励」不需要卡牌上下文（只要拿到玩家就能做），
    /// 生成器一直在支持它们（CustomPowerGen.EmitEffect → EmitGlobalCardEffectPublic / EmitCardRewardPublic），
    /// 以前这张清单漏了它们、导致校验把用户拦下来 —— 现在补上。
    /// </summary>
    public static IReadOnlyList<string> SupportedEffectKinds { get; } = new[]
    {
        "Damage", "Block", "Draw", "Energy", "Heal", "HpLoss", "MaxHp", "Gold",
        "ExtraResource", "ApplyPower", "TempPower", "Stun", "GenerateCard", "ExtraTurn", "CardReward",
        "AddCardGlobal", "TransformCardGlobal", "RemoveCardGlobal",
        // 强化指定卡牌：只是给自己挂一张强化 Power（不需要卡牌上下文），状态触发器里也能用
        "BoostCard",
        // 毒性爆发：全是本体 API（PowerCmd.Apply<PoisonPower> + PoisonPower.Trigger()），状态里也能用。
        // 「大限已至 / 升级卡牌 / 预见」不在这个清单里：前两个要「玩家选中的目标」或选牌界面，
        // 状态触发器里没有这些上下文，校验会拦住。
        "Outbreak",
        // V0.2.154 / V0.2.155：播放音效 / 播放上传的音频文件 —— 两条都不需要任何上下文
        //（SfxCmd.Play 是静态的；AudioStreamPlayer 挂场景根），状态触发器里当然也能放。
        // ⚠ 以前漏了这一条：生成端（CustomPowerGen）其实**已经**实现了，但这份清单没写上 →
        // 校验器会把「状态里放声音」直接拦成错误（用户根本用不了）。
        "PlaySfx", "PlaySoundFile",
        // 「升级卡牌（全局）」= 直接改牌组（本体「香盒 Pomander」那种），不需要卡牌上下文，状态里也能用
        "UpgradeCardGlobal",
        // 获得药水（需求 ⑨）：本体 PotionCmd.TryToProcure 只收玩家（不收 choiceContext），
        // 状态触发器里 base.Owner.Player 就是玩家，所以也能用。
        "GainPotion",
        // 从某一摞拿牌到手牌（用户报的用例：追忆「玩家回合开始时 → 从弃牌堆拿 1 张」）：
        // 这一类只要 choiceContext + 玩家就能做，自定义状态的触发时机两个都有
        //（没有 choiceContext 的钩子由 EmitChoiceContextBootstrap 造一个），所以状态里也能用。
        "TakeFromDraw", "TakeFromDiscard", "TakeFromExhaust",
        // 升级卡牌（用户报的用例：自定义状态「玩家回合开始时 → 升级手牌里的 1 张」，
        // 参照本体储君的「暴政」TyrannyPower）。本体自己就在状态钩子里选牌：
        //   TyrannyPower.AfterPlayerTurnStart → CardSelectCmd.FromHand(choiceContext, player, prefs, null, this)
        //   EntropyPower / ToolsOfTheTradePower / StratagemPower / ForegoneConclusionPower 同理。
        // 以前这里没有它，是因为生成端只给「卡牌 / 遗物 / 药水」实现了 UpgradeCard，
        // 状态那条链没有对应实现 —— 现在补上了（CustomPowerGen.EmitEffect 的 UpgradeCard 分支）。
        "UpgradeCard",
        // 消耗卡牌 / 丢弃卡牌（用户要求：把状态里「仍然不支持」的这两条也做掉）。
        // 本体在状态钩子里就是这么做的：
        //   TyrannyPower.AfterPlayerTurnStart            → FromHand(..., ExhaustSelectionPrompt) → CardCmd.Exhaust
        //   ToolsOfTheTradePower.AfterPlayerTurnStart    → FromHandForDiscard(..., DiscardSelectionPrompt) → CardCmd.Discard
        // 从哪一摞（手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆）都能选，生成端照卡牌那条同一套写法。
        "ExhaustCard", "DiscardCard",
        // 清除目标身上的指定状态 / 所有减益（用户报的用例：自定义状态「玩家回合开始时 → 清除自己身上的虚弱」）。
        // 以前这里没有它们，理由是「需要卡牌上下文」—— **那个理由是错的**：
        // 生成的原文本体只有 `PowerModel? p = foe.GetPower<T>(); if (p is not null) await PowerCmd.Remove(p);`
        // 和 `foreach (p in foe.Powers.ToList()) if (p.TypeForCurrentAmount == Debuff) await PowerCmd.Remove(p);`
        // —— 一个 choiceContext 都不传，唯一需要的是「选哪个单位」，而状态那条链早就有目标解析了
        // （和 ApplyPower / Stun 用的是同一套 Self / AllEnemies / RandomEnemies / 第一个敌人）。
        "ClearPowerOne", "ClearPowerDebuffs",
        // 牺牲伙伴换收益（用户报的用例）。本体那一套里**只有两处在用卡牌上下文**：
        //   · 收益 = 造成伤害：`.FromCard(this, cardPlay)` + `.Targeting(cardPlay.Target)`；
        //   · 收益 = 获得格挡：`CreatureCmd.GainBlock(..., cardPlay)`。
        // 状态里没有「这张牌」，所以这两处按状态那条链的写法重写（伤害来源传 null、目标用同一套目标解析）；
        // 「获得增益 / 减益」那一档本来就只挂状态、不碰 cardPlay，原样可用。
        // 「找那只宠物」也不是问题：本体就是 `XxxCmd.Get(base.Owner)` 一行，状态钩子里 base.Owner 也是 Creature。
        "PetSacrifice",
    };

    /// <summary>这个效果种类能不能放在状态触发器里。</summary>
    public static bool Supports(string? kind) =>
        kind is not null && SupportedEffectKinds.Contains(kind, StringComparer.Ordinal);

    /// <summary>
    /// 这些种类**真的**需要「打出这张牌」那一刻的上下文（打出这张牌本身 / 此刻弹选牌界面），
    /// 所以自定义状态里做不了 —— 报错文案按这个分开说，别把「生成端还没实现」混进来。
    ///
    /// 为什么单独列出来：以前 <see cref="Supports"/> 失败时一律说「需要卡牌上下文」，
    /// 但那个清单里混了两种完全不同的原因（ClearPowerOne / ClearPowerDebuffs / PetSacrifice
    /// 其实只要目标或宠物，根本没用到那个上下文）—— 用户被误导过，问「为什么上下文用不了」。
    /// 现在：这三个已经补上实现（见 CustomPowerGen.EmitEffect），留在清单里的只有下面这几个。
    /// </summary>
    public static IReadOnlyList<string> NeedsCardPlayContext { get; } = new[]
    {
        // 重放要「现在就把重放次数写上去」，复制要「原牌还在战斗牌堆里 + 弹选牌界面」，
        // 回合结束自动打出要把牌挂到本回合结束那一刻的钩子上 —— 三个都要「打出的那张牌」。
        "CopyCard", "ReplayCard", "TurnEndPlay",
        // 「将此牌放回手牌」要的是「刚刚打出的这张牌」本身（打出之后才知道它在不在弃牌堆里），
        // 状态触发器里没有这张牌 —— 报错文案也因此走「需要打出这张牌的上下文」那一档。
        "ReturnSelfToHand",
        // 「抽到这张牌时它本场战斗费用 -N」同理：这里的「这张牌」是打出它的那张牌自己，
        // 状态触发器里没有这个上下文（想要「抽到就减费（N 回合）」那是另一条效果 CheapenDraw）。
        "CheapenSelfOnDraw",
    };

    /// <summary>一条触发时机「做什么」的一句话说明（界面上实时显示用）。</summary>
    public static string TriggerWhat(PowerTriggerSpec t)
    {
        if (t.Effects.Count == 0) return "还没加效果（什么都不做）";
        return string.Join("，", t.Effects.Select(EffectTextWithCondition));
    }

    /// <summary>
    /// 一条效果 + 它自己的条件选项。
    /// 条件选项是「每条效果各自一份」的，所以写在那条效果后面；
    /// 以前自动描述里完全不写条件，游戏里那个状态就只写「做什么」，玩家看不到「什么条件下才生效」。
    /// 文案里的「额外资源量」由 LocalizationGen 换成用户在「额外资源量/状态」页填的名字（如「冰附魔」）。
    /// </summary>
    private static string EffectTextWithCondition(EffectSpec e)
    {
        string text = EffectText(e);
        string cond = CSharpCodeGen.ConditionText(e.Condition);
        return cond.Length == 0 ? text : $"{text}（条件：{cond}，不满足时这条效果不生效）";
    }

    /// <summary>
    /// 一条触发时机的完整说明：什么时候 + 做什么（界面上那句提示就用它，改了就跟着变）。
    /// </summary>
    public static string TriggerLine(PowerTriggerSpec t)
    {
        // V0.2.152：「到达 N 层」那一档以前**原样印出下拉里那个字母 N**（「某个状态层数到达 N 层时」）——
        // 用户填了 3，界面上那行和游戏里的状态描述都只显示 N，看着就像「工具没用我填的数」。
        // 现在直接把填的层数写进文字里；顺带把「盯的是哪个状态」也写出来（和变化 / 被移除那两条一个口径）。
        string when = t.Kind == "PowerReached"
            ? $"某个状态层数到达 {Math.Max(1m, t.TriggerAmount):0.##} 层时"
            : Find(t.Kind)?.Display ?? t.Kind;
        // 「只对哪类牌生效」（V0.2.118 起还多了 诅咒 / 状态 / 关键词组 / 自定义组）
        if (t.Kind == "CardPlayed" && !string.IsNullOrWhiteSpace(t.CardFilter) && t.CardFilter != "Any")
            when += $"（{CardFilterZh(t.CardFilter)}牌）";
        if (t.Kind is "PowerChanged" or "PowerRemoved" or "PowerReached")
            when += string.IsNullOrWhiteSpace(t.PowerId)
                ? $"（盯：{WatchTargetZh(t.PowerTarget)}的任意状态）"
                : $"（盯：{WatchTargetZh(t.PowerTarget)}的{EffectCatalog.PowerName(t.PowerId, t.PowerId)}）";
        return $"{when}：{TriggerWhat(t)}。";
    }

    /// <summary>
    /// 说明书式的描述：界面上「描述留空自动生成」用。
    /// </summary>
    public static string AutoDescription(CustomPowerSpec p)
    {
        var lines = new List<string>();
        foreach (var t in p.Triggers) lines.Add(TriggerLine(t));
        if (p.RemoveAtTurnEnd) lines.Add("回合结束时移除。");
        else if (p.DecayPerTurn > 0) lines.Add($"每回合减少 [blue]{p.DecayPerTurn}[/blue] 层。");
        return string.Join("\n", lines);
    }

    /// <summary>把状态描述里的富文本标记去掉（界面预览用纯文本）。</summary>
    public static string Plain(string text) =>
        string.IsNullOrEmpty(text) ? "" : text.Replace("[blue]", "").Replace("[/blue]", "");

    private static string EffectText(EffectSpec e)
    {
        // V0.2.164：「数值公式」在状态链上也支持了（全内联求值）。
        // 状态描述是**生成期写死的纯文本**（本体的状态说明不是动态变量那一套），现算不了 ——
        // 所以写了公式就把**公式本身**印出来（例：「造成 A 点伤害」），固定数值那个占位不印（印了就是骗人）。
        string amt = e.AmountIsStack ? "" : (e.UsesAmountFormula
            ? Generation.ForgeFormula.DisplayText(e.AmountFormula)
            : e.Amount.ToString("0.##"));
        string n = e.AmountIsStack ? "自身层数" : amt;
        // 作用对象也要写出来（用户报过：改了「作用对象」左侧描述不跟着变）
        string foe = e.TargetSide switch
        {
            "Enemy" => "对指定敌人",
            "AllEnemies" => "对所有敌人",
            "RandomEnemies" => e.RepeatIsX ? "对随机敌人 X 次" : $"对随机 {Math.Max(1, e.RepeatCount)} 个敌人",
            _ => "",
        };
        // V0.2.161（用户实测：「抽卡、获得能量之类的效果种类的对象没了，但是显示的是对敌人」）：
        // 「抽牌 / 获得能量」这些种类**生成端不读作用对象**（界面上那一行也藏了）⇒ 描述里也不写「对指定敌人」——
        // 判据和显隐同一份（EffectFieldPolicy.UsesTargetSide），别各写一套。
        string who = e.TargetSide == "Self" || !EffectFieldPolicy.UsesTargetSide(e.Kind) ? "" : foe;
        string suffix = who.Length == 0 ? "" : $"（{who}）";
        if (e.ChanceEnabled) suffix += $"（{e.ChancePercent.ToString("0.##")}% 概率）";
        // 「获得能量 / 获得金币」填了负数 = 扣除（生成的是 LoseEnergy / LoseGold）：
        // 描述也得跟着写成「失去 N」，不然状态描述会印成「获得 -2 点能量」。
        bool negAmount = e.Amount < 0 && !e.AmountIsStack;
        string nAbs = negAmount ? Math.Abs(e.Amount).ToString("0.##") : n;
        string body = e.Kind switch
        {
            "Damage" => $"造成 [blue]{n}[/blue] 点伤害",
            "Block" => $"获得 [blue]{n}[/blue] 点格挡",
            "Draw" => $"抽 [blue]{n}[/blue] 张牌",
            "Energy" => $"{(negAmount ? "失去" : "获得")} [blue]{nAbs}[/blue] 点能量",
            "Heal" => $"回复 [blue]{n}[/blue] 点生命",
            "HpLoss" => $"失去 [blue]{n}[/blue] 点生命",
            "MaxHp" => $"最大生命 [blue]{n}[/blue]",
            "Gold" => $"{(negAmount ? "失去" : "获得")} [blue]{nAbs}[/blue] 金币",
            "ExtraResource" => $"获得 [blue]{n}[/blue] 点额外资源量",
            // 本体「缓慢」这类「显示数字不是层数」的状态：勾了「直接设成 N%」时层数没意义（按本体做法施加 1 层）
            "ApplyPower" => e.SlowPercentEffective > 0 && e.IsSlowPower
                ? $"施加{EffectCatalog.PowerName(e.PowerId, "状态")}（受到伤害 +{e.SlowPercentEffective}%）"
                : $"施加 [blue]{n}[/blue] 层{EffectCatalog.PowerName(e.PowerId, "状态")}",
            "GenerateCard" => $"生成 [blue]{n}[/blue] 张牌",
            // 播放音效（V0.2.154）：数值那一栏在这条上是音量%，描述里写音效名（不加音量时就不写音量）
            "PlaySfx" => $"播放音效：{EffectCatalog.SfxDisplay(e.Sfx)}"
                + (e.SfxVolumePercent == 100m ? "" : $"（音量 {e.SfxVolumePercent:0.##}%）"),
            // 播放上传的音频文件（V0.2.155）：这里拿不到配置（没有 profile 参数），直接用文件名
            "PlaySoundFile" => $"播放音频：{(string.IsNullOrWhiteSpace(e.Sfx) ? "（没选音频）" : e.Sfx.Trim())}"
                + (e.SfxVolumePercent == 100m ? "" : $"（音量 {e.SfxVolumePercent:0.##}%）"),
            // 获得药水（需求 ⑨）：指定档写清是哪一瓶（中文名从「目标药水」目录里取），随机档写「随机」。
            // 不写这一条的话状态描述里会原样印出「GainPotion」（界面 / 卡面都是给人看的）。
            "GainPotion" => e.IsPotionRandom
                ? $"获得 [blue]{n}[/blue] 瓶随机药水"
                : $"获得 [blue]{n}[/blue] 瓶{EffectCatalog.FindPotionZh(e.PotionId)}",
            // 丢弃卡牌（状态触发器里用不了，但描述里还是要写对，不然界面会印出 Kind 原文）
            "DiscardCard" => $"{EffectCatalog.CardPickZh(e.CardPick)}丢弃 [blue]{n}[/blue] 张"
                + (e.SelectPile == "Hand" ? "手牌" : $"（{EffectCatalog.SelectPileZh(e.SelectPile)}）牌"),
            // 毒性爆发 / 大限已至：本体那两张牌的说明（大限已至用不了，但描述也要对）
            "Outbreak" => $"给予所有敌人 [blue]{n}[/blue] 层中毒，并立即触发中毒",
            "TimesUp" => "造成等于目标身上灾厄层数的伤害",
            "UpgradeCardGlobal" => $"{EffectCatalog.CardPickZh(e.CardPick)}升级牌组里的 [blue]{n}[/blue] 张牌",
            "ExtraTurn" => "额外获得一个回合",
            // 将此牌放回手牌：状态触发器里用不了（见 NeedsCardPlayContext），但描述里不能印出英文种类名
            "ReturnSelfToHand" => "将此牌放回手牌",
            // 抽到这张牌时它自己本场战斗费用 -N：同样只在卡牌上（状态里没有「这张牌」）
            "CheapenSelfOnDraw" => "抽到时这张牌本场战斗的费用 -N",
            _ => e.Kind,
        };
        return body + suffix;
    }
}
