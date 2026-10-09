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
    /// 需求⑩「卡牌时机 = 被给予自定义关键词」的转发器类名（兜底用当前角色）。
    /// 它就生成在「给予卡牌关键词」那份源码里（见 <see cref="CSharpCodeGen.GivenKeywordPatchSource"/>），
    /// 作用是把「这张牌刚被给了一个自定义关键词」转发给带那个时机的卡自己的方法。
    /// </summary>
    public static string AmbientGivenKeywordTimingClass => _currentCharClass + "ForgeGivenKeywordTiming";

    public string CardClassName(CardSpec c, int index) =>
        IsValidIdentifier(c.ClassName) ? EmittedCardClass(c.ClassName!.Trim()) : CharClass + "Card" + (index + 1).ToString();

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
            return "ForgeDelayed" + stem + Lit.Identifier(card, "");
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
    /// 卡牌类名（没填类名时，按「这是第几张自有卡」自动编号）。
    /// 为什么要单独一个重载：初始卡组里的本体卡（打击 / 防御）排在卡牌列表最上面，
    /// 如果按<b>列表下标</b>编号，没填类名的卡会因为前面多了两条本体卡而集体改名
    /// （类名一变，卡面素材文件名、存档里的引用全都跟着错位）。本体卡不占号，自有卡永远从 1 开始数。
    /// </summary>
    public string CardClassName(CharacterProfile p, CardSpec c)
    {
        if (IsValidIdentifier(c.ClassName)) return EmittedCardClass(c.ClassName!.Trim());
        int own = 0;
        // 自动编号要把**诅咒 / 先古卡**一起数进去（它们和普通卡用同一个命名空间，
        // 各自从 1 开始编号会撞类名 → 本体注册时 DuplicateModelException，模组直接加载不了）。
        foreach (var x in p.AllCards)
        {
            if (ReferenceEquals(x, c)) break;
            if (!x.IsVanillaCard && !x.IsStartingBasic) own++;      // 初始的打击 / 防御不占号
        }
        return CharClass + "Card" + (own + 1).ToString();
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
            string want = CharClass + "Relic" + (index + 1).ToString();
            if (IsValidIdentifier(r.ClassName))
            {
                // 有类名：只有「认得出是自动名」的才跟着序号走（手填的原样保留）；
                // 自动名本来就正好等于当前序号那个名字的，就不用动（幂等：反复生成结果一样）
                if (IsAutoRelicClassName(r.ClassName) && !string.Equals(r.ClassName!.Trim(), want, StringComparison.Ordinal))
                {
                    r.ClassName = want;
                    r.LastAutoOrdinal = index + 1;
                    renumbered++;
                }
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
    /// 驼峰转大写下划线（WeakPower -> WEAK_POWER），与本体 <c>StringHelper.Slugify</c> 完全一致。
    ///
    /// 本体的实现是 <c>Regex.Replace(txt, "([A-Za-z0-9]|\G(?!^))([A-Z])", "$1_$2")</c> 再大写、再滤掉
    /// 非 <c>[A-Z0-9_]</c>。那个 <c>\G(?!^)</c> 分支的效果就是：**连续大写时，每个大写字母前面都插一个下划线**
    /// （前一个字符刚刚被上一次匹配吃掉，也仍然算「前一个是字母/数字」）。
    /// 以前这里多了一个「前一个字符不能是大写」的条件，于是连续大写的名字和本体不等价：
    /// 本体 <c>AIPet</c> → <c>A_I_PET</c>、<c>XiaoQI</c> → <c>XIAO_Q_I</c>，我们只会得到 <c>AIPET</c> / <c>XIAOQI</c> ——
    /// 生成的本地化键和本体算出来的键对不上，非中文语言下 <c>LocTable</c> 直接抛 <c>LocException</c>。
    /// 改完之后「常规驼峰命名」的键**一个都没变**（SevenCard1 → SEVEN_CARD1），只修掉连续大写的缺键坑。
    /// </summary>
    public static string Slug(string className)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < className.Length; i++)
        {
            char c = className[i];
            // 本体条件：i > 0 && 当前字符 [A-Z] && 前一个字符 [A-Za-z0-9]（连续大写时也成立 → 每个大写前都插）
            if (i > 0 && char.IsUpper(c) && char.IsLetterOrDigit(className[i - 1]))
                sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
