namespace Sts2CharForge.Core.Effects;

/// <summary>
/// 「某一种效果该显示哪些栏位」的**唯一一份**允许清单。
///
/// <para><b>这份清单是怎么来的（不许拍脑袋改）</b>：机械测量出来的 —— 见
/// <see cref="EffectFieldUsage"/>（也见命令行 <c>forge --fieldmatrix</c>）。
/// 做法：给每一种效果各造一条效果，把某一栏改成特征值、重新生成这条效果的代码，
/// 和「不填」的基线逐字符比对（CET 标记行不算）；**变了 = 生成端真的读这一栏**。
/// 三条链（卡牌 / 遗物 / 药水）各测一遍。</para>
///
/// <para><b>规矩</b>：界面上显示的栏位，生成端一定读；藏起来的，生成端一定不读。
/// 自检（<c>--uicheck</c>）会把这条规矩对 65 种效果 × 每条链全量验一遍，
/// 以后加了新效果种类却忘了更新这里，会当场 FAIL 而不是悄悄漂掉。</para>
///
/// <para><b>为什么只写「允许的」而不是「排除的」</b>：新加的效果种类默认什么都不显示，
/// 逼着人先量一遍（漏了会被自检抓住），而不是默认全显示、又把用户淹一遍。</para>
///
/// <para><b>作用范围</b>：锻造链（<c>IsForgeChain</c>）**不受这份清单影响** ——
/// 节点图那边栏位的显隐由锻造自己那套（<c>ForgeCanvasView</c>）决定，
/// 免得改卡牌页把锻造页一起改了。</para>
/// </summary>
public static class EffectFieldPolicy
{
    /// <summary>「作用对象」那一栏（本条效果对谁生效）。</summary>
    public static bool UsesTargetSide(string? kind) => kind is
        "Damage" or "Block" or "Heal" or "HpLoss" or "MaxHp" or "ApplyPower"
        or "Stun" or "DoublePower" or "Expose" or "ClearPowerOne" or "ClearPowerDebuffs"
        or "ForbidBlock" or "ForbidHeal" or "TempPower" or "TimesUp"
        or "PetAttack" or "PetDamageByMaxHp" or "PetDamageByCurHp" or "PetDamageByMissingHp"
        or "InstantKill"   // V0.2.278：即死也要选「对谁生效」（自己 / 指定敌人 / 全体敌人…）
        // V0.2.306（用户要求「让回能和获得金币的对象能指定队友」）：这两种作用在**玩家**身上，
        // 只给「自己 / 指定队友 / 全部友方」三档（候选见 EffectCatalog.TargetSidesFor）。
        or "Energy" or "Gold";

    /// <summary>
    /// 「牺牲伙伴」的「作用对象」：**只有收益 = 造成伤害**时才有得选（V0.2.200，用户要求）。
    ///
    /// <para>收益是格挡 / 获得增益·减益时，「对谁生效」根本没有第二种可能（格挡给自己、
    /// 层数也挂在自己身上），摆出这一栏只会让人以为能改。所以判据挂在<b>收益类型</b>上，
    /// 而不是挂在效果种类上 —— <see cref="UsesTargetSide"/> 那种「只看 Kind」的写法表达不了。</para>
    /// </summary>
    public static bool UsesTargetSideForSacrifice(string? gain) =>
        string.Equals(gain, "Damage", StringComparison.Ordinal);

    /// <summary>「增益 / 减益」那一栏（要施加 / 翻倍 / 清除 / 暴露哪个状态）。</summary>
    public static bool UsesPower(string? kind) => kind is
        "ApplyPower" or "DoublePower" or "Expose" or "ClearPowerOne" or "TempPower" or "PetApplyPower";

    /// <summary>
    /// 「目标卡」那一栏。
    ///
    /// <para><b>为什么带一个 <paramref name="plainChain"/></b>：遗物链上「转化为先古遗物」的
    /// **目标遗物**也走这一栏（那一页有自己的编辑器，见 <c>AncientRelicSwapEditor</c>）。
    /// V0.2.176 起卡牌链也能用「转化为先古遗物」了（生成端读同一个 <c>SpawnCardId</c>），
    /// 所以两条链都显示这一栏。</para>
    /// </summary>
    public static bool UsesTargetCard(string? kind, bool plainChain = false) =>
        kind is "BoostCard" or "GenerateCard" or "TransformCard" or "FillHand"
            or "AddCardGlobal" or "TransformCardGlobal"
        // 「转化为先古遗物」的**目标遗物**也走这一栏 —— 只有遗物 / 药水链有它自己的编辑器
        //（V0.2.176：卡牌页没法选「换成品体哪只遗物」，所以这一档**只做遗物 / 药水链**）
        || (plainChain && kind is "AncientRelicSwap");

    /// <summary>「生成到」那一栏（生成出来的卡进哪一摞）。只有「生成卡牌」用得到。</summary>
    public static bool UsesSpawnTo(string? kind) => kind is "GenerateCard";

    /// <summary>「选牌方式（自己选 / 随机）」那一栏。</summary>
    public static bool UsesCardPick(string? kind) => kind is
        "ExhaustCard" or "DiscardCard" or "TransformCard" or "UpgradeCard"
        or "CopyCard" or "ReplayCard" or "TurnEndPlay" or "PlaceCard" or "GiveKeyword"
        or "TransformCardGlobal" or "RemoveCardGlobal" or "UpgradeCardGlobal";

    /// <summary>
    /// 「升级增量」那一栏（卡牌页专用）。
    /// 下面这 23 种生成端**根本不读**它（数值在生成时就定死 / 这个效果没有数值落点）：
    /// 击晕 / 翻倍 / 暴露 / 清除指定状态 / 清除全部减益 / 禁止格挡 / 禁止回血 / 抽到就减费 /
    /// 放回手牌 / 抽到就本场减费 / 结束回合 / 额外回合 / 获得药水 / 回合结束自动打出 / 填满手牌 /
    /// 给予关键词 / 四种全局牌组 / 卡牌奖励 / 伙伴承伤开关。
    /// </summary>
    public static bool UsesUpgradeAmount(string? kind) => kind is not (
        "Stun" or "DoublePower" or "Expose" or "ClearPowerOne" or "ClearPowerDebuffs"
        or "ForbidBlock" or "ForbidHeal" or "CheapenDraw" or "ReturnSelfToHand" or "CheapenSelfOnDraw"
        or "EndTurn" or "ExtraTurn" or "GainPotion" or "TurnEndPlay" or "FillHand" or "GiveKeyword"
        or "AddCardGlobal" or "TransformCardGlobal" or "RemoveCardGlobal" or "UpgradeCardGlobal"
        or "CardReward" or "PetGuardOn" or "PetGuardOff"
        // V0.2.154：播放音效 —— 它的「数值」是音量%（SfxCmd.Play 那一句里写死），没有可升级的数值
        or "PlaySfx"
        // V0.2.155：播放上传的音频文件同理（音量写死在 AudioStreamPlayer 那个块里）
        or "PlaySoundFile");

    /// <summary>
    /// 「下回合生效」那一栏要不要显示。
    ///
    /// <para>判据**就是** <see cref="EffectCatalog.HonorsNextTurn"/>（语义上做不了延迟分支的那 8 种不显示：
    /// 抽到就减费 / 本回合费用变 0 / 抽到就本场减费 / 放回手牌 / 结束回合 / 额外回合 /
    /// 回合结束自动打出 / 刮削）。V0.2.150 用机械测量复核过一遍：卡牌链上**正好**是这 8 种不读 NextTurn，
    /// 所以这里不再另写一份清单（两份判据迟早会漂）。</para>
    /// </summary>
    public static bool UsesNextTurn(string? kind) => EffectCatalog.HonorsNextTurn(kind);

    /// <summary>
    /// 「生效次数」那一栏要不要显示。
    ///
    /// <para>机械测量：卡牌链上 65 种里只有 2 种**不读** <c>Times</c> ——
    /// 「将此牌放回手牌」和「抽到这张牌时它本场费用 -N」。这两种的生成代码挂在卡自己的钩子上
    /// （<c>AfterCardPlayedLate</c> / <c>AfterCardDrawn</c>），没有「整条效果重复几遍」的落点。
    /// 遗物 / 药水链上这两种**会**读它（那边走的是通用 <c>EmitRepeated</c>），
    /// 所以这个判据只在卡牌链上用（<c>ShowCardTimesRow</c>）。</para>
    /// </summary>
    public static bool UsesTimes(string? kind) => kind is not ("ReturnSelfToHand" or "CheapenSelfOnDraw");

    /// <summary>
    /// 「范围界定（哪些牌能选）」那一栏 —— 选牌类效果限定「只对哪一类牌 / 哪一组牌动手」。
    /// 和「范围限定（随机出什么）」共用同一个 <c>SpawnFilter</c> 字段，只是两个入口。
    /// </summary>
    public static bool UsesCardScopeFilter(string? kind) => kind is
        "ExhaustCard" or "DiscardCard" or "UpgradeCard" or "GiveKeyword"
        or "CopyCard" or "ReplayCard" or "TurnEndPlay"
        or "TakeFromDraw" or "TakeFromDiscard" or "TakeFromExhaust" or "Scry"
        // V0.2.249：「减少指定卡牌类别的费用」—— 这一栏的「范围」就是「哪一类牌」
        or "CostDown";

    /// <summary>「取几张（恰好 / 最多 / 任意）」那一栏 —— 选牌类效果真的会读它。</summary>
    public static bool UsesCountMode(string? kind) => kind is
        "ExhaustCard" or "DiscardCard" or "TransformCard" or "UpgradeCard"
        or "CopyCard" or "ReplayCard" or "TurnEndPlay" or "PlaceCard" or "GiveKeyword"
        or "TakeFromDraw" or "TakeFromDiscard" or "TakeFromExhaust"
        or "TransformCardGlobal" or "RemoveCardGlobal" or "UpgradeCardGlobal";

    /// <summary>
    /// 「取卡方式（指定卡 / 按范围随机）」+ 它下面那几行（范围限定 / 多选1 / 从范围里自己挑）。
    ///
    /// <para>注意和「生成 / 变化出来的卡的附加处理」（<c>UsesSpawnOptions</c>）**不是同一批**：
    /// 复制卡牌也支持附加处理（升级 / 免费 / 加关键词），但它**不读**「取卡方式」和「范围限定」
    /// （它挑哪张牌由「选牌方式 / 从哪里选牌 / 范围界定」决定）—— 所以这两批分开管。</para>
    /// </summary>
    public static bool UsesSpawnPick(string? kind) => kind is
        "GenerateCard" or "TransformCard" or "AddCardGlobal" or "TransformCardGlobal";

    /// <summary>
    /// 「为抽到的牌添加关键词」那一栏（V0.2.220，用户要求：抽牌 / 拿牌这些效果下多一条选项）。
    ///
    /// <para>只在这几档有意义 —— 它们是**真的把牌拿进手里**的那几种（<c>CardPileCmd.Draw</c> 的返回值 /
    /// 选牌结果 / 填满手牌那批新卡）：抽牌 / 刮削（先抽再挑）/ 从三摞拿牌 / 用目标卡填满手牌。
    /// 其它种类生成端**根本不读**这个字段，所以这里收起来（机械测量会钉住这一点）。</para>
    /// </summary>
    public static bool UsesDrawnKeyword(string? kind) => kind is
        "Draw" or "ScrapeDraw"
        or "TakeFromDraw" or "TakeFromDiscard" or "TakeFromExhaust"
        or "FillHand";

    /// <summary>「临时关键词」那个勾要不要显示：有「为抽到的牌加关键词」那一栏的种类才做得了。</summary>
    public static bool UsesDrawnKeywordTemp(string? kind) => UsesDrawnKeyword(kind);

    /// <summary>
    /// 「会被下一个自定义音频顶替」那个勾（V0.2.231）—— 只有「播放音频文件（我上传的）」读它。
    /// 机械测量会钉住这一点（其它种类勾了也不影响生成，所以界面上不摆）。
    /// </summary>
    public static bool UsesSfxReplace(string? kind) => string.Equals(kind, "PlaySoundFile", StringComparison.Ordinal);

    /// <summary>
    /// 「随机目标允许攻击同个敌人」那一栏要不要显示：
    /// 只有「作用对象 = 随机敌人」这一档有意义（其余档它不参与生成）。
    /// </summary>
    public static bool UsesAllowDuplicates(string? targetSide) =>
        string.Equals(targetSide, "RandomEnemies", StringComparison.Ordinal);
}
