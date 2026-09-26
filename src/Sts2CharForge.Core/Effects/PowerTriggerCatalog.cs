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
public sealed record PowerTriggerOption(string Kind, string Display, string Hint, bool NeedCardFilter = false, bool NeedPower = false)
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
        new PowerTriggerOption("TurnStart",     "玩家回合开始时",     "AfterPlayerTurnStart：本体恶魔形态就是挂这个"),
        new PowerTriggerOption("TurnEnd",       "玩家回合结束时",     "BeforeSideTurnEnd：本体缠身/战斗回合结算时用"),
        new PowerTriggerOption("CardPlayed",    "打出一张牌后",       "AfterCardPlayed：可以只对攻击/技能/能力牌生效", NeedCardFilter: true),
        new PowerTriggerOption("CardExhausted", "消耗一张牌后",       "AfterCardExhausted：本体无痛感（获得格挡）用这个"),
        new PowerTriggerOption("CardDrawn",     "抽一张牌后",         "AfterCardDrawn"),
        new PowerTriggerOption("CardDiscarded", "弃掉一张牌后",       "AfterCardDiscarded"),
        new PowerTriggerOption("DamageTaken",   "自己受到伤害后",     "AfterDamageReceived"),
        new PowerTriggerOption("DamageDealt",   "自己造成伤害后",     "AfterDamageGiven"),
        new PowerTriggerOption("BlockGained",   "自己获得格挡后",     "AfterBlockGained"),
        new PowerTriggerOption("EnemyDeath",    "一个敌人死亡后",     "AfterDeath"),
        new PowerTriggerOption("CombatStart",   "战斗开始时",         "BeforeCombatStart"),
        new PowerTriggerOption("EnergySpent",   "花掉能量后",         "AfterEnergySpent"),
        new PowerTriggerOption("StarsSpent",    "花掉额外资源量后",   "AfterStarsSpent"),
        // ===== 敌人侧（用户要求新增）=====
        new PowerTriggerOption("EnemyTurnStart",  "敌人回合开始时", "AfterSideTurnStart：side == Enemy 时触发"),
        new PowerTriggerOption("EnemyTurnEnd",    "敌人回合结束时", "BeforeSideTurnEnd：side == Enemy 时触发"),
        new PowerTriggerOption("EnemyDamaged",    "敌人受到伤害后", "AfterDamageReceived：受伤的是敌人时触发"),
        new PowerTriggerOption("EnemyDealtDamage","敌人造成伤害后", "AfterDamageGiven：打人的是敌人时触发"),
        new PowerTriggerOption("CombatVictory",   "战斗胜利后",   "AfterCombatEnd：打赢这一场才会走（打输了不走）。「获得卡牌奖励」会挂进本场战斗的结算奖励里，跟本体「王国资产」一样"),
        new PowerTriggerOption("EnemyBlockGained","敌人获得格挡后", "AfterBlockGained：拿格挡的是敌人时触发"),
        new PowerTriggerOption("PowerChanged",  "某个状态层数变化后", "AfterPowerAmountChanged：可以只盯某一个状态（留空 = 任意状态）", NeedPower: true),
    };

    public static PowerTriggerOption? Find(string? kind) =>
        kind is null ? null : All.FirstOrDefault(t => string.Equals(t.Kind, kind, StringComparison.Ordinal));

    /// <summary>「打出牌后」能过滤的牌类型。</summary>
    public static IReadOnlyList<string> CardFilters { get; } = new[] { "Any", "Attack", "Skill", "Power" };

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
        "ExtraResource", "ApplyPower", "GenerateCard", "ExtraTurn", "CardReward",
        "AddCardGlobal", "TransformCardGlobal", "RemoveCardGlobal",
    };

    /// <summary>这个效果种类能不能放在状态触发器里。</summary>
    public static bool Supports(string? kind) =>
        kind is not null && SupportedEffectKinds.Contains(kind, StringComparer.Ordinal);

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
        string when = Find(t.Kind)?.Display ?? t.Kind;
        if (t.Kind == "CardPlayed" && t.CardFilter != "Any")
            when += t.CardFilter switch { "Attack" => "（攻击牌）", "Skill" => "（技能牌）", "Power" => "（能力牌）", _ => "" };
        if (t.Kind == "PowerChanged")
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
        string amt = e.AmountIsStack ? "" : e.Amount.ToString("0.##");
        string n = e.AmountIsStack ? "自身层数" : amt;
        // 作用对象也要写出来（用户报过：改了「作用对象」左侧描述不跟着变）
        string foe = e.TargetSide switch
        {
            "Enemy" => "对指定敌人",
            "AllEnemies" => "对所有敌人",
            "RandomEnemies" => e.RepeatIsX ? "对随机敌人 X 次" : $"对随机 {Math.Max(1, e.RepeatCount)} 个敌人",
            _ => "",
        };
        string who = e.TargetSide == "Self" ? "" : foe;
        string suffix = who.Length == 0 ? "" : $"（{who}）";
        if (e.ChanceEnabled) suffix += $"（{e.ChancePercent.ToString("0.##")}% 概率）";
        string body = e.Kind switch
        {
            "Damage" => $"造成 [blue]{n}[/blue] 点伤害",
            "Block" => $"获得 [blue]{n}[/blue] 点格挡",
            "Draw" => $"抽 [blue]{n}[/blue] 张牌",
            "Energy" => $"获得 [blue]{n}[/blue] 点能量",
            "Heal" => $"回复 [blue]{n}[/blue] 点生命",
            "HpLoss" => $"失去 [blue]{n}[/blue] 点生命",
            "MaxHp" => $"最大生命 [blue]{n}[/blue]",
            "Gold" => $"获得 [blue]{n}[/blue] 金币",
            "ExtraResource" => $"获得 [blue]{n}[/blue] 点额外资源量",
            // 本体「缓慢」这类「显示数字不是层数」的状态：勾了「直接设成 N%」时层数没意义（按本体做法施加 1 层）
            "ApplyPower" => e.SlowPercentEffective > 0 && e.IsSlowPower
                ? $"施加{EffectCatalog.PowerName(e.PowerId, "状态")}（受到伤害 +{e.SlowPercentEffective}%）"
                : $"施加 [blue]{n}[/blue] 层{EffectCatalog.PowerName(e.PowerId, "状态")}",
            "GenerateCard" => $"生成 [blue]{n}[/blue] 张牌",
            "ExtraTurn" => "额外获得一个回合",
            _ => e.Kind,
        };
        return body + suffix;
    }
}
