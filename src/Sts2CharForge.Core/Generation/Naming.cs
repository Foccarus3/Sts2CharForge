using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>由 profile 推导出的全部命名。</summary>
public sealed record Naming(
    string Namespace,
    string ModId,
    string CharClass,
    string CharEntry,
    string CharSlug,
    string EnergyColor,
    string PoolTitle,
    string CardPoolClass,
    string RelicPoolClass,
    string PotionPoolClass)
{
    public static Naming From(CharacterProfile p)
    {
        string charClass = Pascal(string.IsNullOrWhiteSpace(p.CharacterClass) ? "MyCharacter" : p.CharacterClass);
        string charEntry = Slug(charClass);
        string charSlug = charEntry.ToLowerInvariant();
        string modId = string.IsNullOrWhiteSpace(p.ModId) ? charClass + "Mod" : p.ModId.Trim();
        _currentCharClass = charClass;      // 见 CurrentCharClass 的说明
        return new Naming(Pascal(modId), modId, charClass, charEntry, charSlug, charSlug, charSlug,
            charClass + "CardPool", charClass + "RelicPool", charClass + "PotionPool");
    }

    private static string _currentCharClass = "";

    /// <summary>
    /// 「当前正在生成哪个角色」—— <see cref="From"/> 每调用一次就更新一次。
    ///
    /// **只给极少数拿不到 <c>CharacterProfile</c> 的深层 emitter 当兜底**用
    /// （<see cref="CustomPowerGen"/> 的自定义状态钩子链里那条「额外获得一个回合」）：
    /// 那条链一路传的是 CodeWriter / EffectSpec，为它把 profile 穿 4 层不划算。
    /// 为什么可以这么用：生成是单线程的，而且**每个生成文件的入口第一句都是 <c>Naming.From(profile)</c>**
    /// （CharacterSource / CardSource / RelicSource / PotionSource / ExtraResourceSource / CustomPowerGen.Source …），
    /// 所以同一个文件内读到的永远是它自己那个角色。
    /// 能拿到 profile 的地方**一律显式传**，不要读这个兜底值。
    /// </summary>
    public static string CurrentCharClass => _currentCharClass;

    /// <summary>「额外获得一个回合」的 Power 类名（兜底用当前角色，见 <see cref="CurrentCharClass"/>）。</summary>
    public static string AmbientExtraTurnPowerClass => _currentCharClass + "ForgeExtraTurnPower";

    /// <summary>「下回合生效」的延迟 Power 类名（兜底用当前角色）。</summary>
    public static string AmbientDelayedPowerClass(EffectSpec e) => _currentCharClass + DelayedPowerSuffix(e);

    /// <summary>「临时增益（本回合 +X）」的临时 Power 类名（兜底用当前角色）。</summary>
    public static string AmbientTempPowerClass(EffectSpec e) => _currentCharClass + TempPowerSuffix(e);

    /// <summary>「强化指定卡牌」用的 Power 类名（兜底用当前角色）。</summary>
    public static string AmbientBoostPowerClass(EffectSpec e) => _currentCharClass + BoostPowerSuffix(e);

    /// <summary>「仅本回合升级」用的 Power 类名（兜底用当前角色）。</summary>
    public static string AmbientTempUpgradePowerClass => _currentCharClass + "ForgeTempUpgradePower";

    /// <summary>「抽牌堆洗过牌」记录器类名（兜底用当前角色）。</summary>
    public static string AmbientShuffleTrackerClass => _currentCharClass + "ForgeShuffleTracker";

    /// <summary>
    /// 工具自己记的战斗计数器类名（敌人死亡 / 预见次数…，兜底用当前角色）。
    /// 条件表达式（<c>CSharpCodeGen.ConditionExpr</c>）拿不到 <c>CharacterProfile</c>，所以走这个兜底。
    /// </summary>
    public static string AmbientCombatCountersClass => _currentCharClass + "ForgeCombatCounters";

    /// <summary>
    /// 需求⑩「卡牌时机 = 被给予自定义关键词」的转发器类名（兜底用当前角色）。
    /// 它就生成在「给予卡牌关键词」那份源码里（见 <see cref="CSharpCodeGen.GivenKeywordPatchSource"/>），
    /// 作用是把「这张牌刚被给了一个自定义关键词」转发给带那个时机的卡自己的方法。
    /// </summary>
    public static string AmbientGivenKeywordTimingClass => _currentCharClass + "ForgeGivenKeywordTiming";

    /// <summary>
    /// 老重载（只有序号、没有配置）：V0.2.137 起走**三套前缀**（按稀有度判），
    /// 但没有集合就数不出「它自己那一页的第几位」——界面 / 生成端一律用
    /// <see cref="CardClassName(CharacterProfile, CardSpec)"/> 或 <see cref="AutoCardClassName"/>。
    /// </summary>
    public string CardClassName(CardSpec c, int index) =>
        IsValidIdentifier(c.ClassName)
            ? EmittedCardClass(c.ClassName!.Trim())
            : CharClass + CardStemOf(c) + (index + 1).ToString();

    /// <summary>
    /// 卡牌实际**生成出来的类名**。
    ///
    /// 只有一处特殊处理：初始的「打击 / 防御」（配置里类名固定写成 Strike / Defend）要加**角色类名前缀**
    /// （<c>SparkleStrike</c> / <c>SparkleDefend</c>）—— 本体的 ModelDb 只用**类名**算模型 ID（忽略命名空间），
    /// 两个模组各自都定义一个 <c>class Strike</c> 就会在加载时抛
    /// <c>DuplicateModelException: conflict in mod content names</c>，装在一起游戏直接起不来
    /// （用户实测报过：「mods 里有不同存档构建的角色模组时游戏打不开」）。
    /// </summary>
    public string EmittedCardClass(string profileClassName) =>
        IsBasicCardName(profileClassName) ? CharClass + profileClassName.Trim() : profileClassName.Trim();

    /// <summary>配置里的类名是不是「初始打击 / 防御」（这两张牌的类名是固定约定）。</summary>
    public static bool IsBasicCardName(string? className) =>
        string.Equals(className?.Trim(), "Strike", StringComparison.OrdinalIgnoreCase)
        || string.Equals(className?.Trim(), "Defend", StringComparison.OrdinalIgnoreCase);

    // ===== 卡牌自动类名的**三套前缀**（V0.2.137，用户拍板口径 A）=====
    //
    // 用户原话：「卡牌的那个排序有点问题，可以做的和遗物左侧的排序一样。」→ 拍板 = A：
    //   · **每页列表各自 1..N**：普通卡列表 / 诅咒卡列表 / 先古卡列表**各自**从 1 数；
    //     序号 = 列表位置（拖动 / 增删之后重算，和遗物页 / 药水页完全一样）；
    //   · 自动英文类名跟着这个号走，但**三类各自一套前缀**（否则三张第 1 号会撞同一个类名 →
    //     本体注册时 DuplicateModelException、模组直接加载不了）。
    //
    // 老口径（V0.2.130 ~ V0.2.136）：**全部卡一条连续编号**（普通卡 → 诅咒 → 先古卡），
    // 只有一个前缀 Card。所以普通卡（打击 / 防御排最前）占号 —— 新口径下它们**仍然占号**
    // （它们就在「卡牌」列表里，就是第 1、2 号）；变的是诅咒 / 先古卡：老口径里它们接着普通卡
    // 往下数（第 40 多号），新口径里各自从 1 开始。
    //
    // ⚠ 判据仍然**按名字形状认**（不加存档字段 —— 理由见 IsAutoRelicClassNameFor 的注释）。

    /// <summary>普通卡的自动名前缀（<c>&lt;角色类名&gt;Card&lt;列表位置&gt;</c>）。</summary>
    public const string CardStem = "Card";

    /// <summary>诅咒卡的自动名前缀（<c>&lt;角色类名&gt;Curse&lt;诅咒列表位置&gt;</c>）。</summary>
    public const string CurseStem = "Curse";

    /// <summary>先古卡的自动名前缀（<c>&lt;角色类名&gt;Ancient&lt;先古卡列表位置&gt;</c>）。</summary>
    public const string AncientStem = "Ancient";

    /// <summary>
    /// 这张卡该用哪一套前缀（按**稀有度**判 —— 和 <c>CardSpec.IsCurseCard</c> /
    /// <c>IsAncientCard</c> 同一套口径，和生成端「诅咒 / 先古卡」的分流一致）。
    ///
    /// <para>⚠ 为什么按稀有度而不是按「它在哪个集合里」：状态卡也住在 <c>Profile.Cards</c> 里
    /// （稀有度 Status），它属于「卡牌」列表、用 Card 前缀；而诅咒 / 先古卡在生成端就是按稀有度分流
    /// 的（费用固定 -1、走 <c>OnTurnEndInHand</c> 那一套）。两边判据必须一致，否则会出现
    /// 「名字按 A 集合编号、生成按 B 集合分流」的错位。</para>
    /// </summary>
    public static string CardStemOf(CardSpec c) => c switch
    {
        null => CardStem,
        _ when c.IsCurseCard => CurseStem,
        _ when c.IsAncientCard => AncientStem,
        _ => CardStem,
    };

    /// <summary>这张卡该用哪一套**集合**去数位置（普通卡 / 状态卡 → <c>Cards</c>；诅咒 → <c>Curses</c>；先古 → <c>AncientCards</c>）。</summary>
    public static IReadOnlyList<CardSpec> CardListOf(CharacterProfile p, CardSpec c) => c switch
    {
        null => p.Cards,
        _ when c.IsCurseCard => p.Curses,
        _ when c.IsAncientCard => p.AncientCards,
        _ => p.Cards,
    };

    /// <summary>
    /// 这张卡在**它自己那一页列表**里排第几位（1 起算）。找不到（还没加进任何集合）返回 0
    /// —— 调用方一律用 <see cref="AutoCardClassName(CharacterProfile, CardSpec)"/>，别自己 +1。
    /// </summary>
    public static int CardRowOf(CharacterProfile p, CardSpec c)
    {
        if (p is null || c is null) return 0;
        var list = CardListOf(p, c);
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], c)) return i + 1;
        return 0;
    }

    /// <summary>
    /// 这张卡**留空类名时**会被生成出来的那个自动名：<c>&lt;角色类名&gt; + 前缀 + 列表位置</c>
    /// （前缀 = Card / Curse / Ancient，见 <see cref="CardStemOf"/>）。
    ///
    /// <para>这是**唯一的**口径来源：生成端 <see cref="CardClassName(CharacterProfile, CardSpec)"/>、
    /// 校验器那条「下次会改名」的提示、回读 / 界面里凡是需要「它现在应该叫什么」的地方都走它
    /// —— 以前这几处各自手拼 <c>CharClass + "Card" + 行号</c>，改口径时最容易漏。</para>
    /// </summary>
    public string AutoCardClassName(CharacterProfile p, CardSpec c)
    {
        int row = CardRowOf(p, c);
        return CharClass + CardStemOf(c) + (row >= 1 ? row : 1).ToString();
    }

    // ===== 生成器自己起的固定名字：一律带角色类名前缀，避免两个模组撞模型 ID（原因见 EmittedCardClass）=====
    /// <summary>「额外资源量」的隐藏承载遗物（每个模组必须有自己的一份）。</summary>
    public string ExtraResourceRelicClass => CharClass + "ExtraResourceRelic";

    /// <summary>
    /// 图集精灵补丁：能量图标 / 药水图标由本模组自己的图直接供给 AtlasManager
    /// （不写本体的 ui_atlas.tpsheet —— 那份表多个模组会互相覆盖，见 ArtGenerator.AtlasSpritePatchSource）。
    /// </summary>
    public string AtlasSpritePatchClass => CharClass + "ForgeAtlasSpritePatch";

    /// <summary>「额外获得一个回合」用的 Power。</summary>
    public string ExtraTurnPowerClass => CharClass + "ForgeExtraTurnPower";

    /// <summary>
    /// 「我上传的音频」播放器小助手（V0.2.231）：只在有用到「会被下一个自定义音频顶替」时才生成，
    /// 负责记住「还在放、且允许被顶替」的那几个播放器，下一段开始时先停掉它们。
    /// </summary>
    public string SfxRegistryClass => CharClass + "ForgeSfx";

    /// <summary>「透支能量」用的负债 Power（下回合少 N 点能量；本体的 GainEnergy 会忽略负数，只能自己扣）。</summary>
    public string EnergyDebtPowerClass => CharClass + "ForgeEnergyDebtPower";

    /// <summary>
    /// 「生成 / 变化出来的卡：仅在此回合升级」用的 Power：记住这次真的升上去的那几张，
    /// 回合结束时把它们降回来（本体没有「临时升级」API，只能自己记）。
    /// </summary>
    public string TempUpgradePowerClass => CharClass + "ForgeTempUpgradePower";

    /// <summary>「抽牌堆洗过牌」记录器（+ 一个打在 Hook.AfterShuffle 上的补丁）。</summary>
    public string ShuffleTrackerClass => CharClass + "ForgeShuffleTracker";

    /// <summary>
    /// 「获得能量」填了负数 + 勾了「下回合生效」时用的 Power：下回合能量重置后扣掉 N 点。
    /// 本体 <c>EnergyNextTurnPower</c> 的 AfterEnergyReset 走的是 GainEnergy（忽略非正数），
    /// 所以「下回合少 N 点」只能自己生成一个（和透支能量那个负债 Power 长得像，但施加时不加能量）。
    /// </summary>
    public string EnergyNextTurnDebtPowerClass => CharClass + "ForgeEnergyNextTurnDebtPower";

    /// <summary>
    /// 「给予卡牌关键词」勾了「临时关键词」时用的 Power：记住「哪张牌、哪个关键词」，
    /// 回合结束时把它摘掉（保留 / 奇巧走本体自己的单回合标记，不用这个）。
    /// </summary>
    public string TempKeywordPowerClass => CharClass + "ForgeTempKeywordPower";

    /// <summary>
    /// 「回合结束时自动打出」用的 Power：记住「本回合结束时要把哪几张牌自动打出去」
    /// （数值 0 = 这张牌自己），在 BeforeSideTurnEnd 里逐张 <c>CardCmd.AutoPlay</c> 之后自毁。
    /// </summary>
    public string TurnEndPlayPowerClass => CharClass + "ForgeTurnEndPlayPower";

    /// <summary>
    /// 「抽到就减费」用的 Power：挂在自己身上，N 层 = 还能持续几个回合；
    /// 每次抽到牌就把那张牌本场战斗的费用减 1（AfterCardDrawn + EnergyCost.SetThisCombat），
    /// 每回合掉 1 层、掉光自毁。
    /// </summary>
    public string DrawCostPowerClass => CharClass + "ForgeDrawCostPower";

    /// <summary>
    /// V0.2.249（用户要求「减少指定卡牌类别的费用（类似腐化，刺破帷幕），加在效果种类里」）：
    /// 「减少卡牌费用」这条效果施加的状态类名。带载荷（范围 + 减多少 + 是否只持续本回合），
    /// 覆写本体的 <c>TryModifyEnergyCostInCombatLate</c> —— 腐化 / 刺破帷幕同一个钩子。
    /// </summary>
    public string CostDownPowerClass => CharClass + "ForgeCostDownPower";

    /// <summary>
    /// 「禁止目标回复生命」用的状态：挂谁身上谁就回不了血（配一条 Harmony 前缀把
    /// <c>CreatureCmd.Heal</c> 的治疗量改成 0）。层数 = 还能持续几个回合，掉光自毁。
    /// </summary>
    public string NoHealPowerClass => CharClass + "ForgeNoHealPower";

    /// <summary>
    /// 「强化指定卡牌」（像本体「精准」）用的 Power：
    /// <c>&lt;角色&gt;ForgeBoost&lt;目标卡类名&gt;&lt;Damage|Block&gt;Power</c>。
    /// 它挂在自己身上，只给**那一张卡**的伤害 / 格挡加 N。
    /// </summary>
    public string BoostPowerClass(EffectSpec e) => CharClass + BoostPowerSuffix(e);

    /// <summary>强化 Power 的固定后半段（回读时按它认「强化指定卡牌」）。</summary>
    public static string BoostPowerSuffix(EffectSpec e) =>
        "ForgeBoost" + SafeName(e.SpawnCardId) + (e.BoostStat == "Block" ? "Block" : "Damage") + "Power";

    /// <summary>把用户填的卡牌类名收拾成能直接拼进标识符的样子（类名允许留空 / 填中文）。</summary>
    private static string SafeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Card";
        var chars = raw.Trim().Where(ch => char.IsLetterOrDigit(ch) && ch < 128).ToArray();
        return chars.Length == 0 ? "Card" : new string(chars);
    }

    /// <summary>「下回合生效」用的延迟 Power（<c>&lt;角色&gt;ForgeDelayed&lt;状态&gt;</c>）。</summary>
    public string DelayedPowerClass(EffectSpec e) => CharClass + DelayedPowerSuffix(e);

    /// <summary>延迟 Power 的固定后半段（回读时按它认「下回合生效」）。</summary>
    public static string DelayedPowerSuffix(EffectSpec e)
    {
        // 「下回合生成 / 变化（全局）/ 全局加牌」这几种**没有 PowerId**：按「种类 + 那张卡」各自起名，
        // 否则它们会全叫 ForgeDelayedPower —— 同一个类名生成两次（CS0102 / 后写覆盖前写）。
        if (CSharpCodeGen.IsDelayedSpawnKind(e.Kind))
        {
            string stem = e.Kind switch
            {
                "GenerateCard" => "Gen",
                "CopyCard" => "Copy",
                "TransformCard" => "Trans",
                "TransformCardGlobal" => "TransGlobal",
                "AddCardGlobal" => "AddGlobal",
                "RemoveCardGlobal" => "RemoveGlobal",
                "UpgradeCardGlobal" => "UpGlobal",
                _ => "Gen",
            };
            string card = string.IsNullOrWhiteSpace(e.SpawnCardId)
                ? (e.IsSpawnRandom ? "Rnd" + Lit.Identifier(e.SpawnFilter ?? "", "") : "")
                : e.SpawnCardId!.Trim();
            // ⚠ 只编进「种类 + 卡（或随机范围）」还不够：`CollectDelayedEffects` 的去重键里还带着
            //   生成到哪（手牌 / 抽牌堆 / 弃牌堆）、张数、几个附加处理（升级 / 免费 / 关键词）、数值 = X ——
            //   两条只在这些栏位上不同的效果会被当成两条去生成，类名却一模一样 ⇒
            //   `cs\DelayedPowers.cs` 里同一个类写两遍：**CS0101 + CS0111**（用户实测报的
            //   `DaqgeForgeDelayedGenRnd` 重复定义）。
            //   所以这里把**非默认**的那几栏编进类名（默认值一个字都不加 —— 默认存档的类名不变，守红线）。
            string extra = "";
            if (!string.Equals(e.SpawnTo, "Hand", StringComparison.Ordinal))
                extra += "To" + Lit.Identifier(e.SpawnTo ?? "", "");
            if (e.SpawnChoice > 1m) extra += "C" + ((int)Math.Round(e.SpawnChoice)).ToString();
            if (e.SpawnUpgraded) extra += "Up";
            if (e.SpawnFree) extra += "Free";
            if (e.SpawnFreeThisTurn) extra += "FreeTurn";
            if (e.SpawnUpgradedThisTurn) extra += "UpTurn";
            if (e.HasSpawnKeyword) extra += "Kw" + Lit.Identifier(e.SpawnKeyword ?? "", "");
            if (e.AmountIsXEffective) extra += "X";
            return "ForgeDelayed" + stem + Lit.Identifier(card, "") + extra;
        }
        // V0.2.133 的**通用**延迟分支：这些种类大多**没有 PowerId**，必须按「种类 + 那几个会分叉的栏位」
        // 各自起名 —— 否则「造成伤害 / 回复生命 / 击晕…」全叫 ForgeDelayedPower，
        // 同一个类名生成两次（CS0102，后写的覆盖前写的）。
        // ⚠ 只有**延迟造成的结果真的不一样**的栏位才编进类名（否则同一个存档里两条小差异的效果
        //   会各生成一个几乎一样的类）。判据与 CSharpCodeGen.DelayedEffectKey 保持一致。
        if (CSharpCodeGen.IsDelayedEffectKind(e.Kind))
        {
            string tail = e.Kind switch
            {
                "ApplyPower" or "TempPower" or "DoublePower" or "Expose" or "ClearPowerOne"
                    or "ForbidBlock" or "ForbidHeal" or "BoostCard" or "PetApplyPower" or "PetSacrifice" => e.PowerId,
                "GiveKeyword" => e.GivenKeyword,
                "PlaceCard" => e.PlaceTo,
                _ => null,
            };
            // 「作用对象」不同的同一条效果生成出来的**语句也不同**（自己 / 全体敌人 / 全部友方…），
            // 所以类名要带上这一档 —— 但默认档（Enemy）**不写**，保持短名好读（也避免老存档里
            // 已经存在的 ForgeDelayedDamage 突然改名）。
            string side = string.Equals(e.TargetSide, "Enemy", StringComparison.Ordinal) ? "" : e.TargetSide;
            // V0.2.210：宠物类效果**哪一只**也要编进类名 —— 延迟状态类体里「查哪只伙伴」那一行不同
            //（`DaYunCmd.Get(...)` / `XiaoBaCmd.Get(...)`），而种类 + 状态名可能完全一样。
            // 不编进类名就会：同一个类名生成两次 → CS0101（实测：两只伙伴各配一条「下回合生效：给伙伴施加中毒」）。
            // 「全部召唤物」不写（那里由延迟状态类自己逐只处理，类名保持稳定）。
            string pet = CSharpCodeGen.NeedsExistingPet(e) && !PetGen.IsAll(e.PetSummon)
                ? Lit.Identifier(e.PetSummon ?? "", "")
                : "";
            return "ForgeDelayed" + e.Kind + Lit.Identifier(tail ?? "", "") + Lit.Identifier(side, "") + pet;
        }
        return "ForgeDelayed" + (e.PowerId ?? "Power");
    }

    /// <summary>
    /// 「临时增益（本回合 +X）」用的临时 Power（<c>&lt;角色&gt;ForgeTemp&lt;状态&gt;</c>；
    /// 「下回合开始时才撤」那一档加 <c>NextTurn</c> 后缀，见 <see cref="TempPowerSuffix"/>）。
    /// 它自己不给任何加成，只是在施加时把 X 层加进真正的状态、到点再把 X 层撤掉
    /// （本体 FlexPotion 的 TemporaryStrengthPower 就是这套）。
    /// </summary>
    public string TempPowerClass(EffectSpec e) => CharClass + TempPowerSuffix(e);

    /// <summary>
    /// 临时 Power 的固定后半段（回读时按它认「临时增益」）。三样都要编进类名：
    ///   · <c>ForgeTemp&lt;状态&gt;</c>（默认：回合结束时撤）；
    ///   · <c>ForgeTemp&lt;状态&gt;NextTurn</c>（下回合开始时才撤）；
    ///   · <c>ForgeTemp&lt;状态&gt;Abs</c> / <c>ForgeTemp&lt;状态&gt;AbsNextTurn</c>（数值是负数 = 临时**减益**：先扣层，到点加回来）。
    ///     <c>Abs</c> = 「取绝对值那一条路」，因为减益版的生成代码和增益版完全不同（GetPower+ModifyAmount 扣层）。
    /// 为什么不做成一个类带参数：类名就是回读时唯一的判据（<see cref="ProjectRecovery"/> 只看类名），
    /// 名字里带上这些信息可以零成本地把「哪一档」也带回来；合成一个类反而要多记标记。
    /// ⚠ 尾巴必须接在状态名**后面**，这样「抠出真正的状态名」那段逻辑只要多剥一层尾巴就行。
    /// </summary>
    public static string TempPowerSuffix(EffectSpec e)
    {
        string tail = "";
        if (e.Amount < 0m) tail += "Abs";
        if (e.TempPowerReverse == TempPowerReverses.NextTurnStart) tail += "NextTurn";
        return "ForgeTemp" + (e.PowerId ?? "Power") + tail;
    }

    /// <summary>「替主人承伤」共用的守卫 Power（所有召唤物共用一个类，所以只能有一个）。</summary>
    public string GuardianPowerClass => CharClass + "ForgePetGuardianPower";

    /// <summary>
    /// 诅咒的「外观池」类名（只用来换卡牌边框材质，不装卡）。
    /// 注意**故意不叫** <c>…CardPool</c>：回读（从工程恢复存档）和卡池顺序解析都用
    /// <c>Directory.GetFiles(cs, "*CardPool.cs")</c> 找「角色卡池」，名字里再带 CardPool 会把它一起匹配到
    /// （自检里踩过：挑中的是外观池 → 卡池顺序 / 配色全读错）。
    /// </summary>
    public string CurseStylePoolClass => CharClass + "CurseStylePool";

    /// <summary>先古卡的「外观池」类名（同上，故意不带 CardPool）。</summary>
    public string AncientStylePoolClass => CharClass + "AncientStylePool";

    /// <summary>自定义边框颜色生成出来的材质名（<c>materials/cards/frames/&lt;它&gt;_mat.tres</c>）。</summary>
    public string SpecialFrameMaterial(string stem) => $"{CharSlug}_{stem}_frame";

    /// <summary>
    /// 卡牌类名（没填类名时，按它**在自己那一页列表里是第几行**自动编号，1 起算）。
    ///
    /// <para>编号口径（V0.2.137 用户拍板 <b>A</b>，改成「和遗物页一样」）：**每页列表各自 1..N** ——
    /// 普通卡列表（含本体卡 / 初始打击防御，它们就在这个列表里，仍然占第 1、2 号）、诅咒卡列表、
    /// 先古卡列表**各自**从 1 数；序号 = 列表位置，拖动 / 增删后跟着重算。
    /// 用户原话的验收场景：「sparkle 存档里『面具虚影』在卡牌中排第三张 → 对应左侧序列号 3 →
    /// 删掉英文类名后生成 SparkleCard3」。</para>
    ///
    /// <para>⚠ 类名前缀**三套**（<see cref="CardStemOf"/>）：普通卡 <c>&lt;角色&gt;Card&lt;n&gt;</c>、
    /// 诅咒卡 <c>&lt;角色&gt;Curse&lt;n&gt;</c>、先古卡 <c>&lt;角色&gt;Ancient&lt;n&gt;</c>。
    /// 三页各自从 1 开始就必须分前缀，否则三张「第 1 号」会撞同一个类名 →
    /// 本体注册时 <c>DuplicateModelException</c>、模组直接加载不了。</para>
    ///
    /// <para>⚠ 老口径（V0.2.130 ~ V0.2.136）是「普通卡 + 诅咒 + 先古卡**一条连续编号**，只有一个
    /// Card 前缀」—— 这会让既有存档里**工具自动生成的**类名集体改号（手填的一个字不动、
    /// 没填的仍按新序号拼），所以生成前必须先跑 <see cref="RenumberCardClassNames"/> 把自动名按新序号重排一遍。
    /// 见 <c>docs\QUEUE.md</c> 第 24 批里那张「改前 → 改后」映射表。</para>
    /// </summary>
    public string CardClassName(CharacterProfile p, CardSpec c)
    {
        if (IsValidIdentifier(c.ClassName)) return EmittedCardClass(c.ClassName!.Trim());
        return AutoCardClassName(p, c);
    }

    /// <summary>
    /// 这个名字是不是「本工具自动生成过的卡牌类名」：<c>&lt;角色类名&gt; + Card|Curse|Ancient + 数字</c>（1 起算）。
    ///
    /// <para>和 <see cref="IsAutoRelicClassName"/> / <see cref="IsAutoPotionClassName"/> 同一个理由
    /// （用户要求第 1 条）：重新生成 / 构建的时候，**没填英文类名**的卡要先清掉以前自动生成的那个类名，
    /// 再按它**现在的行号**重新生成；但**用户手填的类名绝不能被清掉**，所以必须能把两者区分开。</para>
    ///
    /// <para>⚠ 判据是**按名字形状认**（整串正好是 <c>角色类名 + 三套前缀之一 + 数字</c>），不加存档字段
    /// —— 理由见 <see cref="IsAutoRelicClassNameFor"/> 的注释（老存档一个字节都不用改）。
    /// 命中面窄：<c>SparkleCardMask</c> / <c>Abb2</c> / 别的角色前缀都不算。</para>
    /// </summary>
    public bool IsAutoCardClassName(string? className) => IsAutoCardClassNameFor(CharClass, className);

    /// <summary>
    /// 同上，但可以**不依赖一份配好的 <see cref="Naming"/>**（给界面 / 模型用：
    /// <see cref="Sts2CharForge.Core.Profile.CardSpec"/> 自己不知道角色类名，只想知道
    /// 「这串名字长得像不像自动编号」，所以把角色类名留空 = 只按**形状**判断
    /// —— 任意一串「英文标识 + Card|Curse|Ancient + 数字」都算）。
    /// </summary>
    public static bool IsAutoCardClassNameFor(string? charClass, string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return false;
        string s = className.Trim();
        // 三套前缀都认（V0.2.137）：Card / Curse / Ancient。
        // ⚠ 顺序无关（三个前缀互相不构成对方的后缀：Curse / Ancient 都不以 Card 结尾），
        //   但一定要**每个都试** —— 少试一个就会出现「诅咒卡的自动名被当成手填、永远不跟着列表走」。
        foreach (string stem in new[] { CardStem, CurseStem, AncientStem })
        {
            if (!s.EndsWith(stem + DigitsTail(s, stem), StringComparison.Ordinal)) continue;
            int at = s.LastIndexOf(stem, StringComparison.Ordinal);
            if (at < 0) continue;
            // 三个前缀互相不是对方的后缀，但 LastIndexOf 仍要**按这个 stem 找** ——
            // 用 LastIndexOf("Card") 去找 Curse 的位置会拿到 -1。
            string prefix = s.Substring(0, at + stem.Length);
            string digits = s.Substring(at + stem.Length);
            if (digits.Length == 0) continue;
            // 前缀必须是「角色类名」本身（给了 charClass 时）或一个合法英文标识
            if (!string.IsNullOrWhiteSpace(charClass))
            {
                if (!string.Equals(prefix, charClass!.Trim() + stem, StringComparison.Ordinal)) continue;
            }
            else if (!IsValidIdentifier(prefix)) continue;
            bool allDigits = true;
            for (int i = 0; i < digits.Length; i++)
                if (digits[i] < '0' || digits[i] > '9') { allDigits = false; break; }
            if (!allDigits) continue;
            // 编号得是合法正整数：`...Card0` 不是自动生成出来的（人手打的）
            if (int.TryParse(digits, out int n) && n >= 1) return true;
        }
        return false;
    }

    /// <summary>名字末尾那串数字（没有数字就是空串）。给 <see cref="IsAutoCardClassNameFor"/> 定位前缀用。</summary>
    private static string DigitsTail(string s, string stem)
    {
        int at = s.LastIndexOf(stem, StringComparison.Ordinal);
        if (at < 0) return "";
        int i = at + stem.Length;
        int end = i;
        while (end < s.Length && s[end] >= '0' && s[end] <= '9') end++;
        return s.Substring(i, end - i);
    }

    /// <summary>
    /// 生成前的「卡牌自动类名重新编号」（V0.2.137 口径）：语义和
    /// <see cref="RenumberRelicClassNames"/> / <see cref="RenumberPotionClassNames"/> **完全一致**，
    /// 只是对象换成 <see cref="CardSpec"/>：
    /// <list type="bullet">
    /// <item>没填英文类名的 → 记下它**当前在列表里的位置**对应的自动名
    ///       （<see cref="CardSpec.LastAutoOrdinal"/>，类名本身**留空** —— 生成端 / 校验器读
    ///       <see cref="CardClassName(CharacterProfile, CardSpec)"/> 时自己会拼出来）；</item>
    /// <item>写着本工具以前自动生成的那个名字的（<see cref="IsAutoCardClassName"/>）→ 改成**当前位置**对应的名字
    ///       （拖到哪就跟着哪）；</item>
    /// <item>用户手填的（认不出是自动名的）→ 一个字都不动。</item>
    /// </list>
    ///
    /// <para>⚠ V0.2.137：<paramref name="cards"/> 要传**它自己那一页列表**（生成端分别传
    /// <c>p.Cards</c> / <c>p.Curses</c> / <c>p.AncientCards</c>）—— 传 <c>p.AllCards</c> 会退回
    /// 老口径那种「一条连续编号」（而且前缀也不对）。</para>
    ///
    /// <para>⚠ 为什么「没填」的**不写实**：一写实就再也分不出「这串名字是工具生成的」还是「用户手打的」——
    /// 用户手打一个正好长得像自动编号的名字时，下一次生成会被当成自动名改写；
    /// 留空 + 单独记序号，两边都不会误判（生成端读
    /// <see cref="CardClassName(CharacterProfile, CardSpec)"/> 时自己会拼出来）。</para>
    ///
    /// <para>⚠ 为什么自动名也要「跟着拖」：这正是用户要的（「重新生成构建的时候，如果没有填写英文类名，
    /// 自动清除之前自动生成的英文类名，然后按照左侧序列号生成对应的英文类名」）。
    /// 这一步**必须排在 <c>NameStabilizer.Freeze</c> 之前** —— Freeze 会把留空的类名永久写实。</para>
    ///
    /// <para>返回「按新位置重算了几张」。**幂等**：编号正好对上的不会被动。</para>
    ///
    /// <para><paramref name="renamedOut"/> 非空时，把「这一次真的改了名的卡」记成
    /// <c>旧类名 → 新类名</c>（给 <see cref="NameStabilizer.RenumberCardNames"/> 用：
    /// 类名改掉以后，别的效果里按类名存的「目标卡 / 先古卡」引用**必须跟着改**，
    /// 否则它们会指向同名的新主人或者干脆不存在）—— 见那里的说明。</para>
    /// </summary>
    public int RenumberCardClassNames(IEnumerable<CardSpec> cards, IDictionary<string, string>? renamedOut = null)
    {
        // ⚠⚠ V0.2.156：**两趟** —— 先把整张「旧名 → 新名」表算完，再动手改卡。
        //
        // 为什么必须两趟（用户实测报「在卡牌列表拖动卡牌后，卡面会变成其他卡」的真因之一）：
        //   改 `ClassName` / `LastAutoOrdinal` 会触发 PropertyChanged，界面那边的
        //   `OnCardChanged` / `OnCardsChanged` 会**重入**刷新（`RefreshCardChoices` → 又一次
        //   `RenumberCardNames`）。一趟「边算边改」的写法在重入时**只算得出已经走过的那部分映射**
        //   （剩下的卡 LastAutoOrdinal 已被重入的 `RefreshCardOrdinals` 改成新号了）——
        //   于是引用 / 卡面被**分几次**搬，每一小次都以为自己拿到的是完整置换，结果互相覆盖。
        //   实测：把 3 张牌从 [A,B,C] 拖成 [C,A,B] 时，映射被拆成三次（13→14 / 12→13 / 14→1），
        //   三张卡面最后错位成「第 3 张拿了第 2 张的图、第 2 张没有图」。
        //   两趟之后，第一趟不碰任何字段（纯读），重入也只会在第二趟里发生（那时映射已经定死，
        //   重入的那次算出来是空映射，什么都不动）→ 结果永远一致。
        var list = new List<CardSpec>();
        foreach (var c in cards) if (c is not null) list.Add(c);

        var plannedRenames = new List<(CardSpec Card, string Want)>();
        var plannedOrdinals = new List<(CardSpec Card, int Row, bool Counted)>();
        int renumbered = 0;
        for (int i = 0; i < list.Count; i++)
        {
            CardSpec c = list[i];
            int row = i + 1;
            string want = CharClass + CardStemOf(c) + row.ToString();
            if (IsValidIdentifier(c.ClassName))
            {
                // ⚠ V0.2.346（用户要求「生成英文类名后就绑定」，原话：变换顺序后自动生成的英文类名会变、
                // 导致目标卡丢失）：**已经存下来的类名一律不动**，哪怕它长得像自动名。
                //
                // 以前这里「认得出是自动名就跟着行号走」（V0.2.140/153 的行为）：拖动换位置时名字会变，
                // 而效果里按类名存的引用（`SpawnCardId` 目标卡 / 先古转化清单 / 卡面 key）**只在生成时**才被搬，
                // 拖动那一刻就断链 —— 用户看到的就是「目标卡丢失」。
                // 现在只有**留空**的才按行号自动编号；名字一旦写实（添加 / 打开存档时写实）就固定。
                if (c.LastAutoOrdinal != row) plannedOrdinals.Add((c, row, false));
            }
            else
            {
                // 没填类名：只记「这个位置对应的自动名是第几号」，类名留空（生成端会自己拼）
                if (c.LastAutoOrdinal != row)
                {
                    // V0.2.153：**「旧名 → 新名」也要在这里产出**（以前这一支什么都不记）。
                    // 为什么必须记：没填类名的卡，它的名字是**算出来的**（<角色>Card + 行号），
                    // 而效果里「目标卡」存的是这个字符串 —— 用户先选了目标卡、之后又增删 / 拖动过卡牌，
                    // 改号那一刻不把引用一起搬，那个字符串就会落到**同名的另一张卡**身上（静默换卡；
                    // 用户实测报「点击构建后目标卡里的东西会变」）。
                    // 「上一轮的号」= <see cref="CardSpec.LastAutoOrdinal"/>（界面每次刷新候选表时会被这里写实）。
                    if (renamedOut is not null && c.LastAutoOrdinal >= 1)
                    {
                        string oldName = CharClass + CardStemOf(c) + c.LastAutoOrdinal.ToString();
                        if (!string.Equals(oldName, want, StringComparison.Ordinal)) renamedOut.TryAdd(oldName, want);
                    }
                    plannedOrdinals.Add((c, row, true));
                    renumbered++;
                }
            }
        }

        foreach (var (c, want) in plannedRenames) c.ClassName = want;
        foreach (var (c, row, _) in plannedOrdinals) if (c.LastAutoOrdinal != row) c.LastAutoOrdinal = row;
        return renumbered;
    }

    public string RelicClassName(RelicSpec r, int index) =>
        IsValidIdentifier(r.ClassName) ? r.ClassName.Trim() : CharClass + "Relic" + (index + 1).ToString();

    /// <summary>
    /// 这个名字是不是「本工具自动生成过的遗物类名」：<c>&lt;角色类名&gt;Relic&lt;数字&gt;</c>（1 起算）。
    ///
    /// 为什么需要它（用户要求第 2 条）：重新生成 / 构建的时候，**没填英文类名**的遗物要先清掉
    /// 以前自动生成的那个类名，再按它**现在的序号**重新生成 —— 这样拖动换位置后自动类名会跟着走。
    /// 但**用户手填的类名绝不能被清掉**，所以必须能把两者区分开。
    ///
    /// ⚠ 为什么不加一个 `ClassNameAuto` 开关存进存档：那会给每一只遗物都多写一个字段，
    /// 老存档读进来这个开关是「未设置」，分不出「老版本的自动名」和「老版本手填的名字」；
    /// 而它的默认值不管取哪一边都会猜错一半。这里改成**按名字形状认**：
    /// 只有「整串正好是 角色类名 + Relic + 数字」才算自动名（`SevenCharm` / `StelleRelic1x` /
    /// 别的角色前缀都不算），命中面窄、可预测，老存档一个字节都不用改。
    /// </summary>
    public bool IsAutoRelicClassName(string? className) => IsAutoRelicClassNameFor(CharClass, className);

    /// <summary>
    /// 同上，但可以**不依赖一份配好的 <see cref="Naming"/>**（给界面 / 模型用：
    /// <see cref="Sts2CharForge.Core.Profile.RelicSpec"/> 自己不知道角色类名，只想知道
    /// 「这串名字长得像不像自动编号」，所以把角色类名留空 = 只按**形状**判断
    /// —— 任意一串「英文标识 + Relic + 数字」都算）。
    /// </summary>
    public static bool IsAutoRelicClassNameFor(string? charClass, string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return false;
        string s = className.Trim();
        int at = s.LastIndexOf("Relic", StringComparison.Ordinal);
        if (at < 0) return false;
        string prefix = s.Substring(0, at + "Relic".Length);
        string digits = s.Substring(at + "Relic".Length);
        // 前缀必须是「角色类名」本身（给了 charClass 时）或一个合法英文标识
        if (!string.IsNullOrWhiteSpace(charClass))
        {
            if (!string.Equals(prefix, charClass!.Trim() + "Relic", StringComparison.Ordinal)) return false;
        }
        else if (!IsValidIdentifier(prefix)) return false;
        if (digits.Length == 0) return false;
        for (int i = 0; i < digits.Length; i++)
            if (digits[i] < '0' || digits[i] > '9') return false;
        // 编号得是合法正整数：`...Relic0` 不是自动生成出来的（人手打的）
        return int.TryParse(digits, out int n) && n >= 1;
    }

    /// <summary>
    /// 生成前的「自动类名重新编号」（用户要求）：从左到右遍历遗物列表，
    /// <list type="bullet">
    /// <item>没填英文类名的 → 记下它**当前序号**对应的自动名（<see cref="RelicSpec.LastAutoOrdinal"/>，
    ///       类名本身**留空** —— 生成端 / 校验器读 <see cref="RelicClassName"/> 时自己会拼出来）；</item>
    /// <item>写着本工具以前自动生成的那个名字的（<see cref="IsAutoRelicClassName"/>）→ 改成**当前序号**对应的名字
    ///       （拖到哪就跟着哪）；</item>
    /// <item>用户手填的（认不出是自动名的）→ 一个字都不动。</item>
    /// </list>
    ///
    /// ⚠ 为什么「没填」的**不写实**：一写实就再也分不出「这串名字是工具生成的」还是「用户手打的」——
    /// 用户手打一个正好长得像自动编号的名字时，下一次生成会被当成自动名改写；
    /// 留空 + 单独记序号，两边都不会误判（生成端读 <see cref="RelicClassName"/> 时自己会拼出来）。
    ///
    /// ⚠ 为什么自动名也要「跟着拖」：这正是用户要的（「重新生成的时候…按照左侧序列号生成对应的英文类名」）。
    ///
    /// 返回「按新序号重算了几只」。
    /// </summary>
    public int RenumberRelicClassNames(IEnumerable<RelicSpec> relics)
    {
        int renumbered = 0;
        int index = 0;
        foreach (var r in relics)
        {
            if (r is null) { index++; continue; }
            if (IsValidIdentifier(r.ClassName))
            {
                // ⚠ V0.2.346（用户要求「生成英文类名后就绑定」）：**已经存下来的类名一律不动**，
                // 哪怕它长得像自动名。以前「认得出是自动名就跟着序号走」→ 拖动换位置时名字会变，
                // 而效果里按类名存的引用（目标卡 / 召唤物 / 状态）当场对不上（用户报「目标卡丢失」）。
                // 现在只有**留空**的才按序号自动编号；名字一旦写实（添加 / 打开存档时就会写实）就固定。
                r.LastAutoOrdinal = index + 1;
            }
            else
            {
                // 没填类名：只记「这个位置对应的自动名是第几号」，类名留空（生成端会自己拼）
                if (r.LastAutoOrdinal != index + 1)
                {
                    r.LastAutoOrdinal = index + 1;
                    renumbered++;
                }
            }
            index++;
        }
        return renumbered;
    }

    public string PotionClassName(PotionSpec s, int index) =>
        IsValidIdentifier(s.ClassName) ? s.ClassName.Trim() : CharClass + "Potion" + (index + 1).ToString();

    /// <summary>
    /// 这个名字是不是「本工具自动生成过的药水类名」：<c>&lt;角色类名&gt;Potion&lt;数字&gt;</c>（1 起算）。
    ///
    /// <para>和 <see cref="IsAutoRelicClassName"/> 同一个理由（用户要求第 6 条「药水方面参考遗物的优化方案」）：
    /// 重新生成 / 构建的时候，**没填英文类名**的药水要先清掉以前自动生成的那个类名，再按它**现在的序号**
    /// 重新生成，这样拖动换位置后自动类名会跟着走；但**用户手填的类名绝不能被清掉**，所以必须把两者区分开。</para>
    ///
    /// <para>⚠ 判据是**按名字形状认**（整串正好是 <c>角色类名 + Potion + 数字</c>），
    /// 不加存档字段 —— 理由见 <see cref="IsAutoRelicClassNameFor"/> 的注释（老存档一个字节都不用改）。</para>
    /// </summary>
    public bool IsAutoPotionClassName(string? className) => IsAutoPotionClassNameFor(CharClass, className);

    /// <summary>
    /// 同上，但可以**不依赖一份配好的 <see cref="Naming"/>**（给界面 / 模型用：
    /// <see cref="Sts2CharForge.Core.Profile.PotionSpec"/> 自己不知道角色类名，只想知道
    /// 「这串名字长得像不像自动编号」，所以把角色类名留空 = 只按**形状**判断
    /// —— 任意一串「英文标识 + Potion + 数字」都算）。
    /// </summary>
    public static bool IsAutoPotionClassNameFor(string? charClass, string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return false;
        string s = className.Trim();
        int at = s.LastIndexOf("Potion", StringComparison.Ordinal);
        if (at < 0) return false;
        string prefix = s.Substring(0, at + "Potion".Length);
        string digits = s.Substring(at + "Potion".Length);
        // 前缀必须是「角色类名」本身（给了 charClass 时）或一个合法英文标识
        if (!string.IsNullOrWhiteSpace(charClass))
        {
            if (!string.Equals(prefix, charClass!.Trim() + "Potion", StringComparison.Ordinal)) return false;
        }
        else if (!IsValidIdentifier(prefix)) return false;
        if (digits.Length == 0) return false;
        for (int i = 0; i < digits.Length; i++)
            if (digits[i] < '0' || digits[i] > '9') return false;
        // 编号得是合法正整数：`...Potion0` 不是自动生成出来的（人手打的）
        return int.TryParse(digits, out int n) && n >= 1;
    }

    /// <summary>
    /// 生成前的「药水自动类名重新编号」：语义和 <see cref="RenumberRelicClassNames"/> **完全一致**
    /// （用户要求第 6 条「药水方面参考遗物的优化方案」），只是对象换成 <see cref="PotionSpec"/>：
    /// <list type="bullet">
    /// <item>没填英文类名的 → 记下它**当前序号**对应的自动名（<see cref="PotionSpec.LastAutoOrdinal"/>，
    ///       类名本身**留空** —— 生成端 / 校验器读 <see cref="PotionClassName"/> 时自己会拼出来）；</item>
    /// <item>写着本工具以前自动生成的那个名字的（<see cref="IsAutoPotionClassName"/>）→ 改成**当前序号**对应的名字
    ///       （拖到哪就跟着哪）；</item>
    /// <item>用户手填的（认不出是自动名的）→ 一个字都不动。</item>
    /// </list>
    ///
    /// <para>⚠ 为什么「没填」的**不写实**：一写实就再也分不出「这串名字是工具生成的」还是「用户手打的」——
    /// 用户手打一个正好长得像自动编号的名字时，下一次生成会被当成自动名改写；留空 + 单独记序号，
    /// 两边都不会误判（生成端读 <see cref="PotionClassName"/> 时自己会拼出来）。</para>
    ///
    /// <para>⚠ 为什么自动名也要「跟着拖」：这正是用户要的（「重新生成构建的时候，如果没有填写英文类名，
    /// 自动清除之前自动生成的英文类名，然后按照左侧序列号生成对应的英文类名」）。
    /// 这一步**必须排在 <c>NameStabilizer.Freeze</c> 之前** —— Freeze 会把留空的类名永久写实。</para>
    ///
    /// 返回「按新序号重算了几瓶」。**幂等**：编号正好对上的不会被动。
    /// </summary>
    public int RenumberPotionClassNames(IEnumerable<PotionSpec> potions)
    {
        int renumbered = 0;
        int index = 0;
        foreach (var s in potions)
        {
            if (s is null) { index++; continue; }
            if (IsValidIdentifier(s.ClassName))
            {
                // ⚠ V0.2.346（和遗物 / 卡牌同一个口径，用户要求「生成英文类名后就绑定」）：
                // **已经存下来的类名一律不动**，哪怕它长得像自动名 —— 只有留空的才按序号自动编号。
                // 名字一旦写实（添加 / 打开存档时写实）就固定，拖动换位置不再改它，
                // 效果里按类名存的引用也就不会丢。
                if (s.LastAutoOrdinal != index + 1) s.LastAutoOrdinal = index + 1;
            }
            else
            {
                // 没填类名：只记「这个位置对应的自动名是第几号」，类名留空（生成端会自己拼）
                if (s.LastAutoOrdinal != index + 1)
                {
                    s.LastAutoOrdinal = index + 1;
                    renumbered++;
                }
            }
            index++;
        }
        return renumbered;
    }

    public static string EntryOf(string className) => Slug(className);

    public static bool IsValidIdentifier(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string s = name.Trim();
        if (!char.IsLetter(s[0]) || s[0] > 127) return false;
        return s.All(ch => char.IsLetterOrDigit(ch) && ch < 128);
    }

    public static string Pascal(string text)
    {
        var chars = text.Where(ch => char.IsLetterOrDigit(ch) && ch < 128).ToArray();
        string s = new string(chars);
        if (s.Length == 0) s = "X";
        if (char.IsDigit(s[0])) s = "X" + s;
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>
    /// 驼峰转大写下划线（WeakPower -> WEAK_POWER），与本体 <c>StringHelper.Slugify</c> **逐字同源**。
    ///
    /// ⚠ 必须用本体那对正则，不能自己写「每个大写字母前都插一个下划线」——
    /// 本体是 <c>Regex.Replace(txt, "([A-Za-z0-9]|\G(?!^))([A-Z])", "$1_$2")</c>，
    /// 它是**成对消费**的：匹配到 <c>EV</c> 之后引擎停在 <c>V</c> 后面，下一轮从那个位置继续配一对，
    /// 所以「连续大写」只在**每隔一个**的位置插下划线：
    /// <code>
    ///   EVERelic1  → E_VE_RELIC1      （本体 / 游戏真正查的键）
    ///   ABCRelic1  → A_BC_RELIC1
    ///   AIPet      → A_I_PET
    ///   EVE        → E_V_E
    ///   AnonCard10 → ANON_CARD10
    /// </code>
    /// 以前这里写成「每个大写前都插」，只有「3 个以上连续大写」时才会露馅：
    /// <c>EVERelic1</c> 我们给 <c>E_V_E_RELIC1</c>、游戏查 <c>E_VE_RELIC1</c> ——
    /// 本地化键对不上 ⇒ <c>LocException: Key=E_VE_RELIC1.title not found in table=relics</c>，
    /// 而且它是**在 <c>NCharacterSelectScreen.SelectCharacter</c> 里抛的**（选人界面右侧要显示起始遗物的标题）
    /// ⇒ 选择流程中断、大厅里还是上一个角色 ⇒ 用户看到「选人页是对的，选完变成铁甲战士」。
    ///
    /// 常规驼峰命名（单大写边界）两种写法结果**完全相同**（AnonCard10 / VemiuRelic1 / SevenCrush /
    /// TachiShrimpCard3 / WhiteCloakHeaven / XmlHttpPower / UIButton …都逐字不变），
    /// 所以这次修的是「类名里出现 3 个以上连续大写」那一类**本来就已经坏掉**的存档。
    /// </summary>
    public static string Slug(string className)
    {
        // 本体 StringHelper.CamelCaseRegex：([A-Za-z0-9]|\G(?!^))([A-Z]) → "$1_$2"
        string text = System.Text.RegularExpressions.Regex.Replace(
            className.Trim(), @"([A-Za-z0-9]|\G(?!^))([A-Z])", "$1_$2");
        // 本体 Slugify 最后一步：大写，并滤掉 [^0-9A-Z_]（合法标识符走这一步等于没变）
        var sb = new System.Text.StringBuilder(text.Length + 4);
        foreach (char c in text.ToUpperInvariant())
            if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || c == '_') sb.Append(c);
        return sb.ToString();
    }
}
