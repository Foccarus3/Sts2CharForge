using System.Text;
using System.Text.Json;
using Sts2CharForge.Core.Generation;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Effects;

/// <summary>本体的一条 Power（增益/减益）。</summary>
public sealed record PowerEntry(string Id, string Slug, string Type, string StackType, string Zh, string En)
{
    public bool IsBuff => Type == "Buff";
    public bool Recommended => RecommendedIds.Contains(Id);

    private static readonly HashSet<string> RecommendedIds = new(StringComparer.Ordinal)
    {
        "StrengthPower", "DexterityPower", "RegenPower", "PlatingPower", "ThornsPower", "ArtifactPower",
        "IntangiblePower", "VigorPower", "BlurPower", "BarricadePower", "BufferPower", "AfterimagePower",
        "FeelNoPainPower", "DarkEmbracePower", "RupturePower", "AccuracyPower", "EnvenomPower",
        "WeakPower", "VulnerablePower", "FrailPower", "PoisonPower", "StranglePower", "ConstrictPower",
        "DoomPower", "NoDrawPower", "ConfusedPower", "ShrinkPower", "SlowPower",
    };

    /// <summary>
    /// 下拉框里显示的文字。
    /// 卡牌：`中文名  (英文类名)` —— 两个都要留着，光看中文认不出类名、光看类名认不出是哪张卡；
    /// 增益/减益：`＋/－ 中文名 ★? (类名)`（★ = 常用推荐）。
    /// 注意：卡牌**不能**沿用增益/减益那套前后缀，否则「目标卡」里会出现莫名其妙的「－」（踩过）。
    /// </summary>
    public string Display => Type == "Card"
        ? (string.IsNullOrWhiteSpace(Zh) || Zh == Id ? Id : $"{Zh}  ({Id})")   // 没中文名时别显示成「Abrasive (Abrasive)」
        : $"{(IsBuff ? "＋" : "－")} {Zh}{(Recommended ? " ★" : "")}  ({Id})";
}

public sealed record EffectKindOption(string Kind, string Display, string Unit, decimal Min, decimal Max,
    bool SupportsNextTurn, bool NeedsTarget);

/// <summary>「给予卡牌关键词」里能选的一个关键词（本体枚举名 或 自定义关键词的键）。</summary>
public sealed record KeywordChoiceOption(string Id, string Display, bool IsCustom);

/// <summary>「强化指定卡牌」强化的是什么：伤害 / 格挡（本体「精准」是伤害、「敏捷」是格挡）。</summary>
public sealed record BoostStatOption(string Id, string Display);

public sealed record TriggerOption(string Id, string Display, string HookSignature);

/// <summary>
/// 一条「条件」选项。
/// ForCard / ForRelic = 这个条件能不能用在卡牌 / 遗物上；
/// ForPower = 能不能用在自定义状态（能力）的触发器效果上；
/// NeedsAmount = 需要填数值（N）；NeedsPower = 需要选一个增益/减益；
/// NeedsTarget = 还需要选「指向对象」（自己 / 敌人 / 全部敌人）。
/// </summary>
public sealed record ConditionOption(string Id, string Display, bool ForCard, bool ForRelic,
    bool NeedsAmount, bool NeedsPower, string Hint, bool ForPower = false, bool NeedsTarget = false,
    // 第二个数值（目前只有「生命区间」用得到：N = 下界、M = 上界）
    bool NeedsAmount2 = false,
    // 填负数 = 反向时的说法（null = 这个条件不支持填负数）。界面 / 卡面描述都按它写。
    string? InvertedDisplay = null,
    // 这个条件支持「填负数 = 反向」吗（校验器用它提醒）
    bool SupportsNegative = false,
    // 这个条件底下还要再选一个「子时机」吗（需求⑩ 的「卡牌时机」用得到，见 CardTimings）
    bool NeedsTiming = false);

/// <summary>
/// 「卡牌时机」的一个子时机（需求⑩）。
/// 原文：`条件增加“卡牌时机”，当选择时在底下新增下拉栏，内包含“被消耗”、“被生成”、“被保留”、“被丢弃”、“被变化”，
/// 参考原版“战鼓”，再加一个“被给予自定义关键词”，当此卡被给予关键词时条件生效。`
/// Id = 生成代码 / 存档里的稳定标识（也是 `CET:CardTiming=` 标记里写的那个）。
/// </summary>
public sealed record CardTimingOption(string Id, string Display);

/// <summary>条件的「指向对象」一条：看自己 / 任意一个敌人 / 全部敌人。</summary>
public sealed record ConditionTargetOption(string Id, string Display);

/// <summary>「从哪里选牌」一条：手牌 / 抽牌堆 / 弃牌堆。</summary>
public sealed record PileChoiceOption(string Id, string Display);

/// <summary>「生成卡牌」放到哪一摞（界面上那个「生成到」下拉）。</summary>
public sealed record SpawnTargetOption(string Id, string Display);

/// <summary>「生成 / 变化卡牌」的范围限定的一个选项（Id = 空 / Attack / Skill / Power / Curse / Status）。</summary>
public sealed record SpawnFilterOption(string Id, string Display, string Zh);

/// <summary>
/// 遗物稀有度下拉的一项：<c>Id</c> = 存进存档的英文档位（Starter / Common / …），
/// <c>Display</c> = 界面上显示的中文（初始 / 常见 / …）。
/// 为什么要拆成两项（用户要求第 3 条）：下拉原来直接绑 <see cref="EffectCatalog.RelicRarities"/>
/// 那一串英文字符串，界面上全是英文；而**存的值必须还是英文 id** ——
/// 生成端写的是 <c>RelicRarity.Common</c> 这种本体枚举，校验器也按英文判。
/// </summary>
public sealed record RarityOption(string Id, string Display);

/// <summary>
/// 「取几张」的一档（需求：「消耗任意张手牌」）。
/// Id 空串 = 恰好 N 张（默认，老存档的行为）；UpTo = 最多 N 张；Any = 任意张。
/// </summary>
public sealed record CountModeOption(string Id, string Display);

/// <summary>「取几张」的三档（选牌类效果用；见 <see cref="CountModeOption"/>）。</summary>
public static class CountModes
{
    /// <summary>恰好 N 张（默认；空 Id 是为了让老存档的字段缺失也落回这一档）。</summary>
    public const string Exact = "";

    /// <summary>最多 N 张（可以少选；本体「净化 Purity」那种）。</summary>
    public const string UpTo = "UpTo";

    /// <summary>任意张（上限给一个很大的数，卡面不写数字）。</summary>
    public const string Any = "Any";

    /// <summary>本体那种「没写就是全选」用的超大上限（本体自己写的就是这个数）。</summary>
    public const int AnyMax = 999999999;

    public static IReadOnlyList<CountModeOption> All { get; } = new[]
    {
        new CountModeOption(Exact, "恰好 N 张（默认）"),
        new CountModeOption(UpTo, "最多 N 张（可以少选，本体「净化」那种）"),
        new CountModeOption(Any, "任意张（不写数字，想选几张都行）"),
    };

    public static string Zh(string? id) =>
        All.FirstOrDefault(o => string.Equals(o.Id, id ?? "", StringComparison.Ordinal))?.Display ?? All[0].Display;
}

/// <summary>「生成 / 变化卡牌」是「指定卡」还是「按范围随机」。</summary>
public sealed record SpawnPickOption(string Id, string Display);

/// <summary>
/// 「临时增益」什么时候把这次加的层数撤掉（<see cref="EffectSpec.TempPowerReverse"/>）。
/// </summary>
public static class TempPowerReverses
{
    /// <summary>这一回合结束时撤（默认；本体 FlexPotion 的 TemporaryStrengthPower 就是这套）。</summary>
    public const string TurnEnd = "";

    /// <summary>撑过这一回合和敌人的回合，**下回合开始时**才撤（用户要求：「获得临时 5 点力量 → 下回合失去 5 点力量」）。</summary>
    public const string NextTurnStart = "NextTurn";

    public static IReadOnlyList<CountModeOption> All { get; } = new[]
    {
        new CountModeOption(TurnEnd, "本回合结束时撤掉（默认：只这一回合有效）"),
        new CountModeOption(NextTurnStart, "下回合开始时才撤掉（这一回合 + 敌人的回合都保留）"),
    };

    public static string Zh(string? id) =>
        id == NextTurnStart ? "下回合开始时才撤掉" : "本回合结束时撤掉";
}

/// <summary>
/// 「自定义状态覆盖血条」从哪一端开始盖（<see cref="Profile.CustomPowerSpec.HpBarSide"/>）。
///
/// 需求原文：「可以选择是从血条左边开始覆盖还是右边开始覆盖」。
/// 两档都对应本体自己的画法，所以观感和本体一致：
///   · 右边（默认）= 本体「中毒」那一截：从**当前血量的右端**往左盖，红条右端跟着往左退
///     （读作「这些血是要掉的」）；
///   · 左边 = 本体「灾厄」那一截的位置：从**血条最左端**往右盖，红条左端跟着往右退
///     （读作「最左边这一段被标记了」）。
/// 同一条状态只盖一边；同一边有几条就依次往里堆（互不重叠），中间剩下的是红条。
/// </summary>
public static class HpBarSides
{
    /// <summary>从右边开始（默认，和本体中毒一样）。</summary>
    public const string Right = "";

    /// <summary>从左边开始（和本体灾厄占的那一端一样）。</summary>
    public const string Left = "Left";

    public static IReadOnlyList<CountModeOption> All { get; } = new[]
    {
        new CountModeOption(Right, "从右边开始（像中毒：红条从右边让位）"),
        new CountModeOption(Left, "从左边开始（红条从左边让位）"),
    };

    public static string Normalize(string? v) =>
        string.Equals(v, Left, StringComparison.Ordinal) ? Left : Right;

    public static string Zh(string? v) => Normalize(v) == Left ? "从左边开始" : "从右边开始";
}

/// <summary>
/// 「目标药水」下拉里的一项（需求 ⑨）。
/// Id = <b>本体药水的类名</b>（如 <c>BlockPotion</c>）—— 生成代码里要的就是它：
/// <c>PotionCmd.TryToProcure&lt;MegaCrit.Sts2.Core.Models.Potions.BlockPotion&gt;(玩家)</c>。
/// 为什么不用本地化键（<c>BLOCK_POTION</c>）：本体的模型 ID 是 <c>ModelDb.GetId(type).Entry</c>
/// = <c>StringHelper.Slugify(类名)</c>，而 <c>TryToProcure&lt;T&gt;</c> 只认类型；
/// 扫描时正好是从类名反着算出 Slug 去查中文名的，所以类名才是这一栏的原生标识。
/// </summary>
public sealed record PotionEntry(string Id, string Zh)
{
    /// <summary>下拉里显示「中文名  (类名)」——「目标卡」就是这套写法（光看中文认不出类名）。</summary>
    public string Display => string.IsNullOrWhiteSpace(Zh) || Zh == Id ? Id : $"{Zh}  ({Id})";
}

/// <summary>「升级后的关键字」三态：不变 / 升级后获得 / 升级后失去。</summary>
public sealed record KeywordStateOption(string Id, string Display);

public static class EffectCatalog
{
    /// <summary>效果库（增益/减益列表）。启动时由 <see cref="Initialize"/> 从玩家本机的游戏文件里读出。</summary>
    public static IReadOnlyList<PowerEntry> Powers { get; private set; } = LoadEmbedded();

    /// <summary>效果库数据是从哪儿来的（显示在界面上，方便排查「列表是空的」）。</summary>
    public static string CatalogStatus { get; private set; } =
        Powers.Count > 0 ? "使用内置的开发用 powers_catalog.json" : "尚未加载效果库";

    public static IReadOnlyList<PowerEntry> Buffs =>
        Powers.Where(p => p.IsBuff).OrderByDescending(p => p.Recommended).ThenBy(p => p.Zh, StringComparer.Ordinal).ToList();

    public static IReadOnlyList<PowerEntry> Debuffs =>
        Powers.Where(p => !p.IsBuff).OrderByDescending(p => p.Recommended).ThenBy(p => p.Zh, StringComparer.Ordinal).ToList();

    public static PowerEntry? FindPower(string? id) =>
        id is null ? null : Powers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    // ==================== 本体状态改写（改名 / 描述 / 血条颜色）用的辅助 ====================
    /// <summary>
    /// 本机读到的本体「powers」本地化表（键 → 中文文本，如 POISON_POWER.title → 中毒）。
    /// 改写本体状态时用它：① 界面上显示原名/原描述当参考；② 生成前确认要覆盖的键真的存在（键写错了改名就不生效）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ZhPowerLoc { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>本地化表里这一键的文本（没有就返回 null）。</summary>
    public static string? ZhLocText(string? key) =>
        key is not null && ZhPowerLoc.TryGetValue(key, out string? v) ? v : null;

    /// <summary>
    /// 本机读到的本体 <c>localization/zhs/card_keywords.json</c>（键 → 中文，如 <c>EXHAUST.title</c> → 消耗）。
    /// 「本体关键词改名」用它做两件事：① 界面上显示原名/原说明当参考；② 判断「和本体一样 = 没改」，
    /// 免得把没改的键也写进我们的表里（那会把本体的空文本/别的内容覆盖成空串）。
    /// 读不到（没解包工程、pck 里没有这个文件）时是空表，那时退回 <see cref="Profile.VanillaKeywordCatalog"/> 的内置兜底文本。
    /// </summary>
    public static IReadOnlyDictionary<string, string> VanillaKeywordLoc { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>本体 card_keywords 表里这一键的文本（没有就返回 null）。</summary>
    public static string? VanillaKeywordText(string? key) =>
        key is not null && VanillaKeywordLoc.TryGetValue(key, out string? v) ? v : null;

    private static void RememberZhPowerLoc(Dictionary<string, string>? map)
    {
        if (map is { Count: > 0 }) ZhPowerLoc = map;
    }

    /// <summary>本体卡牌 / 遗物 / 药水的中文表（改状态名字时要拿它们做「旧名 → 新名」的全文替换）。</summary>
    public static IReadOnlyDictionary<string, string> ZhCardLoc { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, string> ZhRelicLoc { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, string> ZhPotionLoc { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// 【只给自检用】额外塞几条「本体卡牌文案」进 <see cref="ZhCardLoc"/>，返回原来的表以便还原。
    ///
    /// 为什么需要：卡面描述的「旧名 → 新名」替换依赖本机解包工程里的 cards.json，
    /// 而开发机 / CI 上不一定读得到 —— 读不到时那条链路就永远是 0 条替换项，
    /// 自检也就等于没验。塞几条合成的键（绝不和本体撞名）就能把
    /// 「替换 + 合并进生成出来的 cards.json」整条链路验死。
    /// </summary>
    public static IReadOnlyDictionary<string, string> InjectCardLocForTest(IEnumerable<KeyValuePair<string, string>> extra)
    {
        var old = ZhCardLoc;
        var map = new Dictionary<string, string>(ZhCardLoc, StringComparer.Ordinal);
        foreach (var kv in extra) map[kv.Key] = kv.Value;
        ZhCardLoc = map;
        return old;
    }

    /// <summary>
    /// 把本体卡牌 / 遗物 / 药水的中文表也读进来。
    /// 卡牌名只在解包工程里有（游戏 pck 里没有 cards.json），遗物/药水两边都能读。
    /// </summary>
    private static void RememberZhTables(string? vanillaProjectDir, string? gameDataDir)
    {
        var cards = ReadLocalization(Path.Combine(vanillaProjectDir ?? "", "localization/zhs/cards.json"));
        var relics = ReadLocalization(Path.Combine(vanillaProjectDir ?? "", "localization/zhs/relics.json"));
        var potions = ReadLocalization(Path.Combine(vanillaProjectDir ?? "", "localization/zhs/potions.json"));
        if (cards.Count == 0) cards = GamePckReader.ReadLocalization(gameDataDir, "localization/zhs/cards.json") ?? cards;
        if (relics.Count == 0) relics = GamePckReader.ReadLocalization(gameDataDir, "localization/zhs/relics.json") ?? relics;
        if (potions.Count == 0) potions = GamePckReader.ReadLocalization(gameDataDir, "localization/zhs/potions.json") ?? potions;
        if (cards.Count > 0) ZhCardLoc = cards;
        if (relics.Count > 0) ZhRelicLoc = relics;
        if (potions.Count > 0) ZhPotionLoc = potions;

        // 本体关键词的中文表（改名 / 旧名替换都要用）。pck 里有这个文件，解包工程里也有。
        var keywords = ReadLocalization(Path.Combine(vanillaProjectDir ?? "", "localization/zhs/card_keywords.json"));
        if (keywords.Count == 0) keywords = GamePckReader.ReadLocalization(gameDataDir, "localization/zhs/card_keywords.json") ?? keywords;
        if (keywords.Count > 0) VanillaKeywordLoc = keywords;
    }

    // ==================== 状态改名后的「显示名」 ====================
    // 卡面/遗物/药水描述里会写状态名（比如「施加 2 层中毒」）：用户在「本体状态改写」里改了名字之后，
    // 生成出来的文字也要跟着用新名字 —— 所以文字渲染统一走 PowerName()，它先查这张改名表。
    private static readonly Dictionary<string, string> RenamedPowers = new(StringComparer.Ordinal);

    /// <summary>按配置里的「本体状态改写」更新改名表（生成前 / 界面刷新时各调一次）。</summary>
    public static void SetPowerRenames(IEnumerable<Profile.VanillaPowerOverride>? overrides)
    {
        RenamedPowers.Clear();
        if (overrides is null) return;
        foreach (var o in overrides)
        {
            if (!o.Enabled) continue;
            string id = (o.PowerId ?? "").Trim();
            string name = (o.Name ?? "").Trim();
            if (id.Length == 0 || name.Length == 0) continue;
            if (name == (o.VanillaName ?? "").Trim()) continue;      // 没真的改名
            RenamedPowers[id] = name;
        }
    }

    /// <summary>这个状态该显示成什么名字（改过名就用新名字，自定义状态用自定义名）；查不到就用 fallback。</summary>
    public static string PowerName(string? powerId, string fallback = "效果")
    {
        string id = (powerId ?? "").Trim();
        if (id.Length == 0) return fallback;
        if (RenamedPowers.TryGetValue(id, out string? renamed) && renamed.Length > 0) return renamed;
        if (CustomPowers.TryGetValue(id, out string? custom) && custom.Length > 0) return custom;
        return FindPower(id)?.Zh ?? fallback;
    }

    // ==================== 「自定义状态（能力）」登记表 ====================
    // 卡牌/遗物/药水的「施加增益/减益」要能选到自己造的状态，所以这里登记：
    // 类名 → 显示名（PowerId 存的就是类名，生成代码里就是 PowerCmd.Apply<类名>(...)）。
    private static readonly Dictionary<string, string> CustomPowers = new(StringComparer.Ordinal);

    /// <summary>按配置里的自定义状态更新登记表（生成前 / 界面刷新时各调一次）。</summary>
    public static void SetCustomPowers(IEnumerable<(string ClassName, string Display)>? powers)
    {
        CustomPowers.Clear();
        if (powers is null) return;
        foreach (var (cls, name) in powers)
            if (!string.IsNullOrWhiteSpace(cls)) CustomPowers[cls.Trim()] = string.IsNullOrWhiteSpace(name) ? cls : name;
    }

    /// <summary>这个类名是不是配置里自己的自定义状态。</summary>
    public static bool IsCustomPower(string? powerId) =>
        powerId is not null && CustomPowers.ContainsKey(powerId.Trim());

    /// <summary>
    /// 这个状态本体允不允许**负层数**（<c>AllowNegative =&gt; true</c>）。
    ///
    /// 为什么生成端要知道：本体「尖啸 PiercingWail」扣敌人力量走的是
    /// <c>PowerCmd.Apply&lt;StrengthPower&gt;(目标, -6)</c> —— 因为 <c>StrengthPower.AllowNegative =&gt; true</c>，
    /// 目标身上本来 0 层也能扣成 **-6 层**。别的状态（虚弱 / 易伤…）层数不能为负，
    /// 只能从「目标身上已有的层数」里扣（<c>GetPower</c> + <c>ModifyAmount(-N)</c>）。
    /// 本体的名单是扫源码得来的（<c>override bool AllowNegative =&gt; true</c>）：
    /// 力量 / 敏捷 / 专注 / 尖啸 Shriek / 萎缩 Shrink —— 就这 5 个。
    /// </summary>
    public static bool AllowsNegativeLayers(string? powerId) => (powerId ?? "").Trim() is
        "StrengthPower" or "DexterityPower" or "FocusPower" or "ShriekPower" or "ShrinkPower";

    /// <summary>自定义状态的显示名（不是自定义状态就返回 null）。</summary>
    public static string? CustomPowerName(string? powerId) =>
        powerId is not null && CustomPowers.TryGetValue(powerId.Trim(), out string? v) ? v : null;

    /// <summary>所有自定义状态（界面上和本体状态拼在一起给用户选）。</summary>
    public static IReadOnlyList<PowerEntry> CustomPowerEntries =>
        CustomPowers.Select(kv => new PowerEntry(kv.Key, SlugFor(kv.Key), "Buff", "Counter", kv.Value, kv.Key)).ToList();

    /// <summary>
    /// Power 类名 → 本体本地化表的键前缀（PoisonPower → POISON_POWER）。
    /// 本体是 Id.Entry + ".title"，而 Id.Entry 就是把类名按驼峰拆开大写（PoisonPower → POISON_POWER）。
    /// 传进来已经是这种全大写形式（POISON_POWER）就原样返回。
    /// </summary>
    public static string SlugFor(string? powerId)
    {
        string id = (powerId ?? "").Trim();
        if (id.Length == 0) return "";
        bool alreadySlug = id.All(c => !char.IsLower(c));
        return alreadySlug ? id : Slugify(id);
    }

    /// <summary>
    /// 这个状态是不是「血条上那一截」的主人，返回那个前景节点的名字。
    /// 本体血条（scenes/combat/health_bar.tscn）里只有两截是按状态显示的，而且画法不同：
    ///   · 中毒 → %PoisonForeground：纯颜色，场景里 self_modulate = Color(0.47451,0.752941,0.235294)（绿），
    ///     本体代码只改它的显隐/偏移、从不改颜色 → 直接改 self_modulate 就生效；
    ///   · 灾厄 → %DoomForeground：颜色是 doom_bar.gdshader 用 gradient_tex（一条 3 色标渐变）按噪声采样画的，
    ///     改 self_modulate 没用（着色器直接覆盖 COLOR.rgb）→ 要换掉材质里的 gradient_tex 才能改色。
    /// </summary>
    public static string? HealthBarNodeFor(string? powerId) => SlugFor(powerId) switch
    {
        "POISON_POWER" => "PoisonForeground",
        "DOOM_POWER" => "DoomForeground",
        _ => null,
    };

    /// <summary>血条上中毒那一截的本体颜色（场景里的 self_modulate），界面上当默认值/参考。</summary>
    public const string PoisonBarColorDefault = "79C03C";

    /// <summary>灾厄那一截的本体主色（场景里那条渐变的中间色标 0.513726,0.254902,0.505882）。</summary>
    public const string DoomBarColorDefault = "834181";

    /// <summary>血条上这个状态原来是什么颜色（没有可改的血条段就返回 null）。</summary>
    public static string? DefaultBarColorFor(string? powerId) => HealthBarNodeFor(powerId) switch
    {
        "PoisonForeground" => PoisonBarColorDefault,
        "DoomForeground" => DoomBarColorDefault,
        _ => null,
    };

    // ==================== 卡牌目录（生成/变化卡牌时选目标用）====================
    /// <summary>本体卡牌（类名 + 中文名），运行时从解包工程读取；不含任何内嵌游戏文本。</summary>
    public static IReadOnlyList<PowerEntry> Cards { get; private set; } = Array.Empty<PowerEntry>();

    /// <summary>
    /// 兜底：解包工程读不到时，直接从游戏目录的 sts2.dll 里反射出所有 Power 类。
    /// 这样「增益/减益」下拉至少有类名可選（没有中文名、分类只能猜），不会因为换台机器就完全不能构建。
    /// </summary>
    public static bool TryLoadPowersFromGameDll(string? vanillaProjectDir, string? gameDataDir, out string detail)
    {
        detail = "";
        // 解包工程**真的可用**时才优先用它（有中文名）。
        // 注意：不能只判「目录存在」——用户反馈过：整合包里那个 sts2 工程的
        // src\Core\Models\Powers\*.cs 是 268 个 **1 字节的空壳文件**（只留了文件名），
        // 只判目录的话这里直接 return false，连 sts2.dll 都不读了 →
        // 效果库整个为空 → 校验器拦死生成（连不含动画的角色都生成不出来）。
        if (LooksLikeUsableVanillaSources(vanillaProjectDir, "src/Core/Models/Powers")) return false;
        try
        {
            string dll = string.IsNullOrWhiteSpace(gameDataDir) ? "" : Path.Combine(gameDataDir, "sts2.dll");
            if (dll.Length == 0 || !File.Exists(dll)) return false;

            // **元数据读取**（不加载程序集）：Assembly.GetTypes() 在这种「依赖不全」的程序集上
            // 会抛 ReflectionTypeLoadException，.NET 9 上甚至直接把栈打爆（StackOverflowException 抓不住）。
            var types = Sts2Metadata.Powers(dll);
            if (types.Count == 0) return false;

            // 中文名：从玩家自己游戏本体的 pck 里读（工具不内嵌游戏文本）
            var zh = GamePckReader.ReadLocalization(gameDataDir, "localization/zhs/powers.json");
            RememberZhPowerLoc(zh);

            var list = new List<PowerEntry>();
            foreach (var type in types)
            {
                string cls = type.Name;
                string kind = GuessPowerKind(cls);
                string slug = Slugify(cls);
                string name = ZhName(zh, slug, cls);
                list.Add(new PowerEntry(cls, slug, kind, "Counter", name, cls));
            }
            if (list.Count == 0) return false;
            Powers = list.OrderBy(p => p.Type, StringComparer.Ordinal).ThenBy(p => p.Zh, StringComparer.Ordinal).ToList();
            detail = $"从游戏 sts2.dll 读取（元数据，{Powers.Count} 项）" + (zh is not null
                ? "，中文名取自游戏本体的 localization/zhs/powers.json"
                : "，只有英文类名（" + (string.IsNullOrEmpty(GamePckReader.LastError) ? "没读到游戏 pck" : "读 pck 失败原因：" + GamePckReader.LastError) + "；可在「路径」指定解包工程目录后点「重新扫描效果库」）");
            return true;
        }
        catch (Exception ex)
        {
            detail = "（从 sts2.dll 读取失败：" + ex.Message + "）";
            return false;
        }
    }

    /// <summary>
    /// 猜一个状态是增益还是减益（元数据读不到「PowerModel.Type 返回什么」这个**运行时**结果）。
    ///
    /// 依据两处：① 工具内置的效果库里那条同名声明的分类（244 条本体常用状态，覆盖绝大多数）；
    /// ② 名字后缀的常识性兜底（Weak / Vulnerable / Frail / Poison / Doom… 都是减益）。
    /// 猜错了只影响下拉里显示成「＋」还是「－」和分类排序，不影响生成的代码（那个按类名走）。
    /// </summary>
    private static string GuessPowerKind(string cls)
    {
        var known = Powers.FirstOrDefault(p => string.Equals(p.Id, cls, StringComparison.Ordinal));
        if (known is not null) return known.Type;
        foreach (string debuff in DebuffNameHints)
            if (cls.Contains(debuff, StringComparison.OrdinalIgnoreCase)) return "Debuff";
        return "Buff";
    }

    /// <summary>名字里带这些词的按减益算（只是猜分类，见 <see cref="GuessPowerKind"/>）。</summary>
    private static readonly string[] DebuffNameHints =
    {
        "Weak", "Vulnerable", "Frail", "Poison", "Doom", "Constrict", "Slow", "Shrink",
        "Confus", "NoDraw", "NoBlock", "NoHeal", "Burn", "Decay", "Doubt", "Shame", "BadLuck",
        "Entangle", "Hex", "Mark", "Strangle", "Wound", "Bleed", "Curse", "Chill", "Debuff",
    };

    /// <summary>
    /// 这个解包工程目录里的源码**是不是空壳**（只有文件名、没有内容）。
    ///
    /// 为什么需要：用户反馈过 —— 整合包里那个 sts2 工程，`src\Core\Models\Powers\*.cs` 是
    /// **268 个 1 字节的文件**（min = max = 1 字节），`Cards` 那边也有 1192 个。
    /// 只看「目录在不在」会把这种目录当成可用工程，于是放弃读 `sts2.dll`，
    /// 接着正则解析空文件全部落空 → 效果库为空 → 校验器把生成整个拦死。
    ///
    /// 判据：抽样最多 40 个 `*.cs`，**全都不超过 2 字节**才算空壳（有一个有内容就不算）。
    /// 任何异常（没权限 / 目录不存在）都返回 true = 「当它不可用」，这样会老老实实去读 dll。
    /// </summary>
    public static bool LooksLikeUsableVanillaSources(string? vanillaProjectDir, string relativeDir)
    {
        if (string.IsNullOrWhiteSpace(vanillaProjectDir)) return false;
        try
        {
            string dir = Path.Combine(vanillaProjectDir, relativeDir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) return false;
            string[] files = Directory.GetFiles(dir, "*.cs");
            if (files.Length == 0) return false;              // 一个源文件都没有：不算可用
            int n = 0;
            foreach (string f in files)
            {
                if (n++ >= 40) break;
                if (new FileInfo(f).Length > 2) return true;  // 有真内容 → 可用
            }
            return false;                                     // 抽到的全是空壳 → 不可用
        }
        catch { return false; }
    }

    /// <summary>从本地化表里取 &lt;SLUG&gt;.title，取不到就用兜底值。</summary>
    private static string ZhName(Dictionary<string, string>? map, string slug, string fallback)
    {
        if (map is not null && map.TryGetValue(slug + ".title", out string? z) && !string.IsNullOrEmpty(z)) return z;
        return fallback;
    }

    /// <summary>卡牌类名 → 中文名（找不到就原样返回类名）。</summary>
    public static string FindCardZh(string? cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return "卡牌";
        var hit = Cards.FirstOrDefault(c => string.Equals(c.Id, cardId, StringComparison.Ordinal));
        return hit is null ? cardId : $"{hit.Zh}（{cardId}）";
    }

    // ==================== 药水目录（需求 ⑨「目标药水」那一栏的候选）====================
    // 和 Cards 完全同构：从本机解包工程 src/Core/Models/Potions/*.cs 扫**类名**，
    // 再用 StringHelper.Slugify 那套算法算出本地化键去 potions.json 里查中文名。
    // 为什么不从 potions.json 反着扫：那份表里还有一个没有对应类的键（旧药水），
    // 而且本体的 PotionModel 必须靠**类型**才能取出来（TryToProcure<T> / ModelDb.Potion<T>()）。
    /// <summary>本体药水目录（类名 + 中文名）。读不到解包工程时从 sts2.dll 反射。</summary>
    public static IReadOnlyList<PotionEntry> Potions { get; private set; } = Array.Empty<PotionEntry>();

    /// <summary>药水类名 → 中文名（找不到就原样返回类名），卡面描述里写「获得一瓶格挡药水」用它。</summary>
    public static string FindPotionZh(string? potionId)
    {
        if (string.IsNullOrWhiteSpace(potionId)) return "药水";
        var hit = Potions.FirstOrDefault(x => string.Equals(x.Id, potionId.Trim(), StringComparison.Ordinal));
        return hit is null || string.IsNullOrWhiteSpace(hit.Zh) ? potionId.Trim() : hit.Zh;
    }

    /// <summary>这个药水类名在目录里认不认得出来（认不出来时校验器要报错，不能静默生成不存在的类型）。</summary>
    public static bool HasPotion(string? potionId) =>
        !string.IsNullOrWhiteSpace(potionId)
        && Potions.Any(x => string.Equals(x.Id, potionId.Trim(), StringComparison.Ordinal));

    public static void InitializePotions(string? vanillaProjectDir, string? gameDataDir = null)
    {
        try
        {
            string dir = Path.Combine(vanillaProjectDir ?? "", "src/Core/Models/Potions");
            if (string.IsNullOrWhiteSpace(vanillaProjectDir) || !Directory.Exists(dir))
            {
                LoadPotionsFromGameDll(gameDataDir);
                return;
            }
            var zh = ReadLocalization(Path.Combine(vanillaProjectDir, "localization/zhs/potions.json"));
            if (zh.Count == 0) zh = GamePckReader.ReadLocalization(gameDataDir, "localization/zhs/potions.json") ?? zh;

            var list = new List<PotionEntry>();
            foreach (string file in Directory.GetFiles(dir, "*.cs"))
            {
                string cls = Path.GetFileNameWithoutExtension(file);
                if (cls.StartsWith("Mock", StringComparison.Ordinal)) continue;
                string slug = Slugify(cls);
                // 没有本地化名的通常不是正式药水（内部占位）
                if (!zh.ContainsKey(slug + ".title")) continue;
                list.Add(new PotionEntry(cls, zh[slug + ".title"]));
            }
            if (list.Count > 0)
                Potions = list.OrderBy(x => x.Zh, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            else
                LoadPotionsFromGameDll(gameDataDir);   // 工程里没有药水目录 / 没有中文表 → 退回游戏 dll
        }
        catch { LoadPotionsFromGameDll(gameDataDir); }
    }

    /// <summary>没有解包工程时的兜底：从游戏 sts2.dll 读 PotionModel 子类（元数据，不加载程序集；中文名从游戏 pck 里读）。</summary>
    private static void LoadPotionsFromGameDll(string? gameDataDir)
    {
        if (Potions.Count > 0 || string.IsNullOrWhiteSpace(gameDataDir)) return;
        try
        {
            string dll = Path.Combine(gameDataDir, "sts2.dll");
            if (!File.Exists(dll)) return;
            var zh = GamePckReader.ReadLocalization(gameDataDir, "localization/zhs/potions.json");

            var list = new List<PotionEntry>();
            foreach (var type in Sts2Metadata.Potions(dll))
            {
                string cls = type.Name;
                list.Add(new PotionEntry(cls, ZhName(zh, Slugify(cls), cls)));
            }
            if (list.Count > 0)
                Potions = list.OrderBy(x => x.Zh, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        }
        catch { /* 反射失败就只留下空目录（界面上那一栏没有候选，校验器会提示） */ }
    }

    public static void InitializeCards(string? vanillaProjectDir, string? gameDataDir = null)    {
        try
        {
            if (string.IsNullOrWhiteSpace(vanillaProjectDir)) { LoadCardsFromGameDll(gameDataDir); return; }
            string dir = Path.Combine(vanillaProjectDir, "src/Core/Models/Cards");
            if (!Directory.Exists(dir)) { LoadCardsFromGameDll(gameDataDir); return; }
            var zh = ReadLocalization(Path.Combine(vanillaProjectDir, "localization/zhs/cards.json"));

            var list = new List<PowerEntry>();
            foreach (string file in Directory.GetFiles(dir, "*.cs"))
            {
                string cls = Path.GetFileNameWithoutExtension(file);
                if (cls.StartsWith("Mock", StringComparison.Ordinal)) continue;
                string slug = Slugify(cls);
                string? zhName = zh.TryGetValue(slug + ".title", out string? z) ? z : null;
                if (zhName is null) continue;                 // 没有本地化名的通常不是正式卡
                list.Add(new PowerEntry(cls, slug, "Card", "Counter", zhName, cls));
            }
            Cards = list.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            // 空壳工程（用户反馈过：整合包里 src\Core\Models\Cards 是 1192 个 1 字节文件）
            // 会让上面一个都匹配不到 → 退回从 sts2.dll 反射，别让「目标卡」下拉整个空着。
            if (Cards.Count == 0) LoadCardsFromGameDll(gameDataDir);
        }
        catch { LoadCardsFromGameDll(gameDataDir); }
    }

    /// <summary>
    /// 卡牌目录兜底：换台机器没有解包工程时，从游戏 sts2.dll 读 CardModel 子类（**元数据**，不加载程序集），
    /// 中文名从游戏本体 pck 的 localization/zhs/cards.json 读（工具不内嵌游戏文本）。
    /// 这样「生成卡牌 / 变化卡牌」的目标卡下拉照样有中文名可选。
    /// </summary>
    private static void LoadCardsFromGameDll(string? gameDataDir)
    {
        if (Cards.Count > 0 || string.IsNullOrWhiteSpace(gameDataDir)) return;
        try
        {
            string dll = Path.Combine(gameDataDir, "sts2.dll");
            if (!File.Exists(dll)) return;
            var zh = GamePckReader.ReadLocalization(gameDataDir, "localization/zhs/cards.json");

            var list = new List<PowerEntry>();
            foreach (var type in Sts2Metadata.Cards(dll))
            {
                string cls = type.Name;
                string slug = Slugify(cls);
                string name = ZhName(zh, slug, cls);
                list.Add(new PowerEntry(cls, slug, "Card", "Counter", name, cls));
            }
            if (list.Count > 0) Cards = list.OrderBy(c => c.Zh, StringComparer.Ordinal).ToList();
        }
        catch { /* 反射失败就只显示本模组自己的卡 */ }
    }

    /// <summary>
    /// 「召唤伙伴」时不能用的怪物类名。
    ///
    /// 为什么只写死这几个、不去扫玩家机器上的文件：本体的 <c>ModelDb</c> 只按**类名**算模型 ID（忽略命名空间），
    /// 和本体怪物重名会在注册时抛 <c>DuplicateModelException</c>（模组加载当场失败）。
    /// 宠物多半叫 <c>&lt;角色类名&gt;Pet</c>，真正会撞的就是官方的两只宠物（Osty / Byrdpip）和几个通用词，
    /// 所以这里只挡这些 —— 少一处「换台电脑就读不到」的环境依赖。
    /// </summary>
    public static IReadOnlyCollection<string> VanillaMonsterNames { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Osty", "Byrdpip", "ByrdonisEgg", "BattleFriend", "Pet", "Minion", "Familiar",
        };

    /// <summary>
    /// 从玩家自己的游戏文件里加载效果库：优先用解包工程（有中英文名 + 增益/减益分类），
    /// 读不到就退回内置数据（发布版不含该文件，所以正常会走第一条）。
    /// 这样本工具不携带任何游戏文本。
    /// </summary>
    public static void Initialize(string? vanillaProjectDir, string? gameDataDir = null)
    {
        InitializeCards(vanillaProjectDir, gameDataDir);
        // 需求 ⑨：「目标药水」那一栏的候选（和卡牌目录一样从解包工程 / sts2.dll 里读）
        InitializePotions(vanillaProjectDir, gameDataDir);
        RememberZhTables(vanillaProjectDir, gameDataDir);
        // 先古之民的对话槽位也跟着一起读（都在同一个解包工程里）
        AncientCatalog.Initialize(vanillaProjectDir, gameDataDir);
        if (TryLoadPowersFromGameDll(vanillaProjectDir, gameDataDir, out string dllDetail))
        {
            CatalogStatus = dllDetail;
            return;
        }
        if (TryLoadFromVanillaProject(vanillaProjectDir, out string detail))
        {
            CatalogStatus = $"已从本机解包工程读取（{Powers.Count} 项）" + detail;
            return;
        }

        // 第二道保险：解包工程读了个空（目录在、文件却是空壳 / 正则一条都没匹配上）时，
        // **再试一次读 sts2.dll**（上面那次因为「目录看着可用」直接 return 了）。
        // 宁可只有英文类名，也别让效果库整个为空 —— 空的效果库会把生成整个拦死，
        // 用户连一个不含动画的角色都生成不出来（用户实测报过）。
        if (Powers.Count == 0 && TryLoadPowersFromGameDll(null, gameDataDir, out string retryDetail))
        {
            CatalogStatus = retryDetail + "（解包工程目录在，但里面是空壳源码，已改从 sts2.dll 读）";
            return;
        }

        CatalogStatus = Powers.Count > 0
            ? $"未找到解包工程，暂时使用开发机内置列表（{Powers.Count} 项）；可在「路径」里指定本体工程目录后点「重新扫描效果库」"
            : "未找到效果库数据：请在「路径」里指定「本体工程目录」，或直接在「增益/减益」里手动输入 Power 类名";
    }

    /// <summary>解析解包工程的 Power 源码类名 + localization/{zhs,eng}/powers.json 得到完整效果库。</summary>
    public static bool TryLoadFromVanillaProject(string? vanillaProjectDir, out string detail)
    {
        detail = "";
        try
        {
            if (string.IsNullOrWhiteSpace(vanillaProjectDir)) return false;
            string powersDir = Path.Combine(vanillaProjectDir, "src/Core/Models/Powers");
            if (!Directory.Exists(powersDir)) return false;

            var zh = ReadLocalization(Path.Combine(vanillaProjectDir, "localization/zhs/powers.json"));
            var en = ReadLocalization(Path.Combine(vanillaProjectDir, "localization/eng/powers.json"));
            RememberZhPowerLoc(zh);

            var list = new List<PowerEntry>();
            int skipped = 0;
            foreach (string file in Directory.GetFiles(powersDir, "*.cs"))
            {
                string cls = Path.GetFileNameWithoutExtension(file);
                if (cls.StartsWith("Mock", StringComparison.Ordinal)) continue;   // 测试用假 Power

                string text = File.ReadAllText(file);
                // 只收有明确 Buff/Debuff 标注的（和本体一致，派生/内部 Power 排除）
                var m = System.Text.RegularExpressions.Regex.Match(text, @"Type\s*=>\s*PowerType\.(Buff|Debuff)");
                if (!m.Success) { skipped++; continue; }

                string slug = Slugify(cls);
                string? zhName = zh.TryGetValue(slug + ".title", out string? z) ? z : null;
                string? enName = en.TryGetValue(slug + ".title", out string? e) ? e : null;
                if (zhName is null && enName is null) { skipped++; continue; }   // 没本地化名的不适合给用户选

                var st = System.Text.RegularExpressions.Regex.Match(text, @"StackType\s*=>\s*PowerStackType\.(Counter|Single|None)");
                list.Add(new PowerEntry(cls, slug, m.Groups[1].Value,
                    st.Success ? st.Groups[1].Value : "Counter",
                    zhName ?? enName!, enName ?? zhName!));
            }

            if (list.Count == 0) return false;
            Powers = list.OrderBy(p => p.Type, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
            detail = $"（增益 {Powers.Count(p => p.IsBuff)} / 减益 {Powers.Count(p => !p.IsBuff)}，跳过 {skipped} 个内部 Power）";
            return true;
        }
        catch (Exception ex)
        {
            detail = "（解析解包工程失败：" + ex.Message + "）";
            return false;
        }
    }

    private static Dictionary<string, string> ReadLocalization(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path)) return map;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.String)
                    map[prop.Name] = prop.Value.GetString()!;
                else if (prop.Value.ValueKind == JsonValueKind.Object && prop.Value.TryGetProperty("zh", out var zz) && zz.ValueKind == JsonValueKind.String)
                    map[prop.Name] = zz.GetString()!;
        }
        catch { /* 本地化文件缺失/格式不同就只显示类名 */ }
        return map;
    }

    /// <summary>
    /// WeakPower -> WEAK_POWER（与游戏本地化键一致）。
    ///
    /// 必须和生成的本地化键用**同一套算法**（<see cref="Naming.Slug"/>）：两边只要差一个字符，
    /// 「从工程恢复存档」就会查不到 <c>&lt;ENTRY&gt;.name</c> / <c>.title</c>，把名字静默丢成类名。
    /// 原来这里多了一个「前一个字符不能是大写」的条件，连续大写时和本体 StringHelper.Slugify 不等价
    /// （本体 AIPet → A_I_PET，旧算法只会给 AIPET），现在按本体那套条件走。
    /// </summary>
    private static string Slugify(string className)
    {
        var sb = new StringBuilder(className.Length + 8);
        for (int i = 0; i < className.Length; i++)
        {
            char c = className[i];
            // 本体条件（StringHelper.Slugify 的正则 ([A-Za-z0-9]|\G(?!^))([A-Z])）：i > 0、当前 [A-Z]、前一个 [A-Za-z0-9]
            if (i > 0 && char.IsUpper(c) && char.IsLetterOrDigit(className[i - 1])) sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 本体 <c>CardKeyword</c> 枚举里的关键词（「给予卡牌关键词」能给的、以及卡牌页能勾的）。
    /// 本体枚举是封闭的（模组加不了新值），所以自定义关键词只能走我们自己的等价机制。
    /// </summary>
    public static IReadOnlyList<KeywordChoiceOption> VanillaKeywordChoices { get; } = new[]
    {
        new KeywordChoiceOption("Exhaust",    "消耗（打出后移除）", false),
        new KeywordChoiceOption("Ethereal",   "虚无（回合末未打出则消耗）", false),
        new KeywordChoiceOption("Innate",     "固有（开局必在手牌）", false),
        new KeywordChoiceOption("Retain",     "保留（回合末不弃）", false),
        new KeywordChoiceOption("Unplayable", "不能被打出", false),
        new KeywordChoiceOption("Sly",        "奇巧（被弃掉则免费打出）", false),
        // 永恒**不是**「打出后回抽牌堆」（我上一版写错了）：本体 CardModel.IsRemovable / IsTransformable
        // 看的就是它 —— 带着它的牌无法从牌组中移除或变化。
        new KeywordChoiceOption("Eternal",    "永恒（无法从牌组中移除或变化）", false),
    };

    /// <summary>是不是本体关键词的枚举名（用来区分「给本体关键词」和「给自定义关键词」）。</summary>
    public static bool IsVanillaKeywordName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && VanillaKeywordChoices.Any(k => string.Equals(k.Id, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>「强化指定卡牌」能强化的两种数值。</summary>
    public static IReadOnlyList<BoostStatOption> BoostStats { get; } = new[]
    {
        new BoostStatOption("Damage", "伤害（像本体「精准」对「小刀」）"),
        new BoostStatOption("Block", "格挡（像本体「敏捷」）"),
    };

    /// <summary>本体关键词枚举名规范化（大小写按枚举写死的那份）。</summary>
    public static string? NormalizeVanillaKeyword(string? name) =>
        VanillaKeywordChoices.FirstOrDefault(k => string.Equals(k.Id, name?.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

    /// <summary>本体关键词的中文名（界面上显示用，例：Retain → 保留；不是本体关键词就返回原名）。</summary>
    public static string VanillaKeywordZh(string? name)
    {
        string? id = NormalizeVanillaKeyword(name);
        if (id is null) return (name ?? "").Trim();
        string display = VanillaKeywordChoices.First(k => k.Id == id).Display;
        int at = display.IndexOf('（');
        return at > 0 ? display[..at] : display;
    }

    public static IReadOnlyList<EffectKindOption> EffectKinds { get; } = new[]
    {
        new EffectKindOption("Damage",  "造成伤害",     "点", 0,    999, false, true),
        // 格挡 / 回复生命 / 失去生命 / 最大生命 也能指定「作用对象」——
        // 本体的 CreatureCmd.GainBlock / Heal / GainMaxHp / Damage 收的都是 Creature，
        // 所以「给敌人加格挡」「给敌人回血」这类也能做出来（有些怪就是靠这个变强）。
        new EffectKindOption("Block",   "获得格挡（负数 = 失去）", "点", -999, 999, true,  true),
        new EffectKindOption("Draw",    "抽牌",         "张", 0,    10,  true,  false),
        // 负数 = 失去 N 点能量（本体的 PlayerCmd.GainEnergy 第一句就是 `if (!(amount > 0m)) return;` ——
        // 非正数什么都不做，所以负数必须改走 PlayerCmd.LoseEnergy，见 CSharpCodeGen.IsNegativeAmount）。
        new EffectKindOption("Energy",  "获得能量（负数 = 失去）", "点", -10, 10, true,  false),
        // 透支：现在拿 N 点能量，下回合少 N 点（本体的 GainEnergy 会忽略负数，所以走我们生成的负债 Power）
        new EffectKindOption("OverdraftEnergy", "透支能量（下回合少）", "点", 0, 10, true, false),
        new EffectKindOption("Heal",    "回复生命",     "点", -999, 999, false, true),
        new EffectKindOption("HpLoss",  "失去生命",     "点", 0,    999, false, true),
        new EffectKindOption("MaxHp",   "获得最大生命（负数 = 失去）", "点", -999, 999, false, true),
        // 负数 = 扣除 N 枚金币（同上：GainGold 对非正数直接 return，负数走 PlayerCmd.LoseGold，会夹到 0）
        new EffectKindOption("Gold",    "获得金币（负数 = 扣除）", "枚", -999, 999, false, false),
            // 额外资源量（本体的星星计数器）：正数 = 获得、负数 = 卡牌费用（花费）。
        // 支持「下回合生效」：正数走本体的 StarNextTurnPower（AfterEnergyReset 里 GainStars + 自毁）。
        new EffectKindOption("ExtraResource", "获得额外资源量", "点", -999, 999, true, false),
        new EffectKindOption("ApplyPower", "施加增益/减益（负数 = 扣除层数）", "层", -99, 99, true, true),
        // 击晕：本体的「击晕」不是状态（Power），而是**怪物意图**（StunIntent）——
        // 所以它不在上面的增益/减益列表里。本体卡「口哨 Whistle」就是 CreatureCmd.Stun(cardPlay.Target)，
        // 我们照它生成：被打晕的敌人这一回合什么都不做。
        new EffectKindOption("Stun", "击晕（敌人本回合不行动）", "—", 0, 0, false, true),
    // 翻倍当前特定状态（用户要求的新效果）：数值 = 倍数（2 = 翻倍），
    // 「增益 / 减益」那一栏填要翻倍的状态。生成的是先 GetPower<T>() 取那份实例，再
    // PowerCmd.ModifyAmount(choiceContext, 实例, 实例.Amount * (倍数 - 1), 施加者, this)
    // —— 加「(倍数-1) 倍现有层数」就等于翻倍（本体扣/加层数都是走 ModifyAmount）。
    // 数值不声明动态变量（写死在生成代码里，见 CSharpCodeGen.HasNoDynamicVar）。
    new EffectKindOption("DoublePower", "翻倍目标身上的状态层数（数值 = 倍数，2 = 翻倍；0 = 不生效）", "倍", 0, 5, true, true),
    // 暴露（参考本体卡「暴露 Expose」）：清空目标的格挡 + 移除人工制品，再施加 N 层状态
    // （施加哪个状态在「增益 / 减益」栏选）。生成的就是本体那三句：
    //   CreatureCmd.LoseBlock(目标, 目标.Block) → 有 ArtifactPower 就 PowerCmd.Remove<ArtifactPower> → PowerCmd.Apply<X>(N)。
    new EffectKindOption("Expose", "暴露（清空格挡与人工制品，再施加 N 层状态；0 = 不生效）", "层", 0, 99, true, true),
    // 清除目标身上的指定状态（哪个状态在「增益 / 减益」栏选）
    new EffectKindOption("ClearPowerOne", "清除目标的指定状态（在「增益 / 减益」栏选哪个）", "—", 0, 0, true, true),
    // 清除目标身上的所有减益状态
    new EffectKindOption("ClearPowerDebuffs", "清除目标的所有减益状态", "—", 0, 0, true, true),
    // 禁止持有护盾（用户要求的新效果）：施加本体现成的 NoBlockPower（全库只有这一个相关状态），
    // 挂上之后目标就再也拿不到格挡。数值 = 层数（本体的层数即持续回合数；0 = 不生效）。
    // 复用「施加增益/减益」那套生成（ApplyPowerLine），所以负数 = 反向（可用来清掉这个状态）。
    new EffectKindOption("ForbidBlock", "禁止目标获得格挡（施加本体的 NoBlockPower；负数 = 反向扣掉）", "层", -99, 99, true, true),
    // 抽到就减费（用户要求的新效果）：在 N 回合内，每次抽到的牌**本场战斗**的费用 -1。
    // 本体没有现成的状态，用我们自己生成的 <角色>ForgeDrawCostPower：
    // 它在 AfterCardDrawn 里把那张牌的 EnergyCost 用 SetThisCombat 减 1（X 费 / 不能打出的牌不动），
    // 每回合掉 1 层、掉光就消失（层数 = 还能持续几个回合）。
    // 负数 = 反向扣掉层数（复用 ApplyPowerLine，和「禁止获得格挡」同一套）。
    // 注意这条的语义（用户问过「怎么是别的牌减费」）：它给**自己**挂一个持续 N 回合的状态，
    // 这期间**每抽到一张牌，那张牌**本场战斗费用 -1 —— 不是「这张牌自己」减费。
    // 想要「抽到这张牌时、这张牌自己费用 -1」请用另一条：CheapenSelfOnDraw（卡自己的 AfterCardDrawn）。
    new EffectKindOption("CheapenDraw", "抽到就减费：挂 N 回合状态，这期间每抽到一张牌、那张牌费用 -1（负数 = 扣层数）", "回合", -5, 5, false, false),
    // 减少**这张牌自己**本回合所需的费用（用户要求：参考本体「子弹时间」那种「本回合费用变 0」）。
    // 实现 = 本体 CardModel.SetToFreeThisTurn()：EnergyCost.SetThisTurnOrUntilPlayed(0) + SetStarCostThisTurn(0)，
    // 也就是「本回合内费用变 0、打出后恢复」，能量费与额外资源量费一起管。
    // 数值这一栏没有意义（固定就是「变 0」），所以范围锁 0~0，界面上的数值框填什么都一样。
    new EffectKindOption("CheapenSelf", "本回合这张牌自己费用变 0", "费", 0, 0, false, false),
    // 将此牌放回手牌（用户要求）：**纯开关型**效果 ——
    //   数值 0 = 这一条不生效（卡面不写、代码也不生成，见 CSharpCodeGen.IsInertZero 对这个种类的特判）；
    //   数值 1 = 打出这张牌之后，把它从弃牌堆拿回手牌。
    // 本体没有这个效果，但写法是现成的：本体「回响 MakeItSo / 回力镖 Bolas」就是覆写卡自己的钩子
    //   然后 `await CardPileCmd.Add(this, PileType.Hand);`。
    // **不能在 OnPlay 里搬**：OnPlay 跑完本体才把打出的牌放进弃牌堆，那一步会把我们搬的结果覆盖掉 ——
    // 所以生成的是 AfterCardPlayedLate 钩子（本体 MakeItSo 用的就是它，那时牌已经在弃牌堆里了）。
    new EffectKindOption("ReturnSelfToHand", "将此牌放回手牌（值为 1 时生效）", "—", 0, 1, false, false),
    // 抽到这张牌时它自己本场战斗的费用 -N（用户要求：「我想做抽到这张牌后，本场战斗内这张牌的费用-1，
    // 怎么做，现在的有回合数限制」—— 现有的「抽到就减费」是给自己挂一个 N 回合的状态、对所有抽到的牌生效）。
    // 本体现成的做法就是**卡自己覆写 AfterCardDrawn**：本体卡「王者一击 KinglyKick」原文
    //   `if (card != this) return Task.CompletedTask; base.EnergyCost.AddThisCombat(-1); return Task.CompletedTask;`
    // —— 所以这里照抄它（AddThisCombat 自己会夹住，不会变成负数费用），数值 = 每次抽到减多少（可叠加）。
    new EffectKindOption("CheapenSelfOnDraw", "抽到这张牌时它本场战斗的费用 -N（可叠加；0 = 不生效）", "点", 0, 9, false, false),
    // 增加额外资源量上限值（用户要求）：上限 = 额外资源量页填的「数量上限」+ 这个效果的数值。
    // 作用对象固定是自己（上限是玩家自己的资源上限，写给别人没有意义）。
    new EffectKindOption("RaiseResourceCap", "增加额外资源量上限值（上限 = 页面上填的上限 + 这个数值）", "点", -99, 999, false, false),
    // 转化为先古卡（用户要求）：照本体「古老牙齿 ArchaicTooth」的语义 —— 在**牌组**里按组找牌，
    // 原地换成先古卡（批量 CardCmd.Transform），升级 / 附魔继承。
    // 「一组卡变成不同的先古卡」靠效果自己的**配对表**（EffectSpec.AncientRows）：
    // 每行左边选一个「组」，右边勾几张先古卡，行内按勾选顺序不重复地各发一张；池子用完就不换。
    // 数值这一栏没用（换几张由配对表和池子大小决定），所以范围锁 0~0。
    new EffectKindOption("AncientTranscend", "转化为先古卡（按「组」把牌组里的牌换成先古卡，各不相同）", "张", 0, 0, false, false),
    // 转化为先古遗物（用户要求：「先古遗物的替换遗物」）：照本体「欧洛巴斯之触 TouchOfOrobas」——
    // 拾起时把身上的**一个遗物**原地换成另一个（RelicCmd.Replace，位置不变）。
    // 源遗物按「范围限定」筛（不限 / 只换初始遗物 / 按遗物类名），换成的那个写在「目标遗物」栏。
    new EffectKindOption("AncientRelicSwap", "转化为先古遗物（把身上的一个遗物换成另一个，位置不变）", "个", 0, 0, false, false),
    // 禁止生命回复（用户要求的新效果）：给目标挂上本模组自己生成的状态，N 回合内该目标**回复量为 0**。
    // 本体没有任何「治疗量修改」的钩子（全库只有营地的 ModifyRestSiteHealAmount），
    // 所以配一条 Harmony 前缀把 CreatureCmd.Heal 的 ref amount 改成 0（**不跳过**原方法 ——
    // 它是 async Task，跳过会让调用方 await 到 null）。数值 = 回合数，负数 = 反向扣层数。
    // 只做在卡牌上（需要「选中的目标」，和「禁止获得格挡」同一档）。
    new EffectKindOption("ForbidHeal", "禁止目标回复生命（N 回合内回复量为 0；负数 = 扣层数）", "回合", -99, 99, true, true),
        // 强化指定卡（像本体「精准 Accuracy」对「小刀」那样）：给**某一类卡**的伤害或格挡加 N。
        // 生成一个挂在自己身上的 Power，覆写 ModifyDamageAdditive / ModifyBlockAdditive，
        // 只对指定的那张卡生效（本体精准是用 CardTag.Shiv 认牌，我们直接认卡的类型，更精确）。
        new EffectKindOption("BoostCard", "强化指定卡牌（像「精准」，+N 伤害/格挡）", "点", -99, 99, false, false),
        // 临时增益：本回合内 +X 层，回合结束时把这次加的 X 层撤掉（本体 FlexPotion 的
        // TemporaryStrengthPower 就是这套 —— 生成的 <角色>ForgeTemp<状态> 也是照它写的）。
        // 数值 = 层数（不按回合计时），作用对象沿用「施加增益/减益」那一套（自己 / 指定敌人 / 全体敌人）。
        // 数值可以填负数 = 让目标先**失去** N 层、到点还原（用户要的「让敌人失去 5 点临时力量」）。
        // 所以范围是 -99 ~ 99，不是 0 ~ 99。
        new EffectKindOption("TempPower", "临时增益 / 临时减益（本回合 ±X，到点还原）", "层", -99, 99, false, true),
        new EffectKindOption("EndTurn",   "结束回合",       "—", 0, 0, false, false),
        new EffectKindOption("ExtraTurn", "额外获得一回合", "—", 0, 0, false, false),
        new EffectKindOption("GenerateCard",  "生成卡牌", "张", 0, 10, false, false),
        // 获得药水（需求 ⑨）：两档 —— 「指定药水」用下面那个可搜索的「目标药水」栏，
        // 「随机药水」走本体 PotionFactory（本体卡「炼制药水 Alchemize」的做法）。
        // 数值 = 瓶数（写死在生成代码里，和「生成卡牌」一样不声明动态变量，见 HasNoDynamicVar）。
        // 药水栏满时本体 TryToProcure 返回 success=false —— 本效果**什么都不发生**，卡面描述里写明。
        new EffectKindOption("GainPotion",    "获得药水（指定 / 随机一瓶）", "瓶", 0, 5, false, false),
        new EffectKindOption("ExhaustCard",   "消耗卡牌", "张", 0, 9,  false, false),
        // 丢弃卡牌：把手牌 / 抽牌堆里的 N 张牌丢进弃牌堆（不是「消耗」—— 它们还会被洗回来）。
        // 走本体的 CardCmd.Discard（内部 CardPileCmd.Add(card, 弃牌堆) + 派发 AfterCardDiscarded 钩子），
        // 所以「奇巧」这类跟弃牌有关的东西照常触发。可丢的牌堆只有手牌 / 抽牌堆：
        // 弃牌堆里的牌本来就在那儿，丢它没有意义（界面上那一行也只给这两项）。
        // 丢弃卡牌：数值 = 丢几张（1~9），「选牌方式」= 自己选 / 随机，「从哪里丢弃」= 手牌 / 抽牌堆。
        // **数值 0 = 这张牌自己**（用户要求：以前 0 只是「不生效」，但你想要「丢弃自己」这一档）——
        // 那一档生成的是 `await CardCmd.Discard(choiceContext, this);`（没有张数、也不挑牌）。
        new EffectKindOption("DiscardCard",   "丢弃卡牌（数值 0 = 丢弃这张牌自己）", "张", 0, 9,  false, false),
        new EffectKindOption("TransformCard", "变化卡牌", "张", 0, 9,  false, false),
        // 升级卡牌：从手牌 / 抽牌堆 / 弃牌堆里挑 N 张升级（本体「武装 Armaments」的升级部分 + 自选牌堆）。
        // 生成的是 CardCmd.Upgrade（本体自己也是这么升级卡牌的：Apotheosis / Armaments / Whetstone）。
        new EffectKindOption("UpgradeCard",   "升级卡牌", "张", 0, 9,  false, false),
        // 复制卡牌（用户要求的新效果）：数值 = 从牌堆里**选几张**，下面「复制的份数」= 每一张复制几份。
        // 走的是本体「二刀流 DualWield」的做法（那个界面就是「选 1 张、复制 N 份」）：
        // CardSelectCmd.FromHand / FromCombatPile 选出原牌 → card.CreateClone() → CardPileCmd.AddGeneratedCardToCombat(手牌)。
        // 本体 CreateClone 要求那张牌在**战斗牌堆**里（不在战斗牌堆会直接抛异常），
        // 所以「从哪里选牌」只给 手牌 / 抽牌堆 / 弃牌堆 这三摞。
        new EffectKindOption("CopyCard",      "复制卡牌", "张", 0, 5,  false, false),
        // 重放卡牌（用户要求的新效果）：给选中的牌加上「重放 N 次」——
        // 本体的机制是 CardModel.BaseReplayCount（打出时 GeneratePlayCount = GetEnchantedReplayCount() + 1，
        // 所以「重放 2」= 这张牌打出去时连着打 3 次）。本体「转化 Transfigure / 隐藏宝石 HiddenGem /
        // 剑圣 SwordSagePower / 士兵炖菜 SoldiersStew」都是 `card.BaseReplayCount += N` 这一句。
        // 数值 = 从牌堆里选几张（自己选 / 随机），效果底下的「重放次数」= 每张额外打出几次。
        new EffectKindOption("ReplayCard",    "重放卡牌（让选中的牌本场战斗内额外打出 N 次）", "张", 0, 5,  false, false),
        // 回合结束时自动打出（用户要求的新效果）：
        //   · 数值 0 = **这张牌自己**：打出它之后，本回合结束时再自动打出它一次；
        //   · 数值 ≥ 1 = 打出这张牌时先让你（或随机）从「从哪里选牌」那一摞里挑 N 张，
        //     本回合结束时把这 N 张自动打出去。
        // 实现：挂一个生成的内置 Power（<角色>ForgeTurnEndPlayPower，不可见），
        // 它在 BeforeSideTurnEnd 里对记住的那几张牌调 CardCmd.AutoPlay ——
        // BeforeSideTurnEnd 跑在「手牌结算（DoTurnEnd：先 OnTurnEndInHand、再丢弃手牌）」之前，
        // 所以自动打出的牌会正常离开手牌，不会和回合结束的弃牌流程打架。
        new EffectKindOption("TurnEndPlay",   "回合结束时自动打出（数值 0 = 这张牌自己）", "张", 0, 5,  false, false),
        // 预见（一代观者的 Scry）：看抽牌堆顶的 N 张牌，把其中任意张丢进弃牌堆（本体没有这个机制，自己拼）。
        new EffectKindOption("Scry",          "预见（看抽牌堆顶 N 张，丢任意张）", "张", 0, 9, false, false),
        // 刮削（本体卡「刮削 Scrape」）：抽 N 张牌，然后对其中**符合条件的牌**执行**指定的操作**。
        // 条件和操作都是可选的：默认「非 0 费 + 丢弃」= 本体的刮削（判据照本体原样写：
        // EnergyCost.GetWithModifiers(CostModifiers.All) != 0 || CostsX），换成别的就对别的牌动手
        //（条件那一栏的候选见 ScrapeCondsFor，操作见 ScrapeOps）。
        // 数值 = 抽几张（0 = 不生效，守「所有种类都能填 0」那条规矩）；用 CardsVar，卡面 {Cards:diff()} 与升级增量都跟着走。
        new EffectKindOption("ScrapeDraw",    "刮削（抽 N 张 → 对其中符合条件的牌执行选定的操作）", "张", 0, 10, false, false),
        // 从战斗中的牌堆「挑牌拿到手牌」：本体「搜寻 SecretTechnique / 全息影像 Hologram / 挖掘 Dredge」那种。
        // 走 CardSelectCmd.FromCombatPile + CardPileCmd.Add(..., PileType.Hand)。
        new EffectKindOption("TakeFromDraw",    "从抽牌堆拿牌到手牌（自己选）", "张", 0, 5, false, false),
        new EffectKindOption("TakeFromDiscard", "从弃牌堆拿牌到手牌（自己选）", "张", 0, 5, false, false),
        // 从消耗牌堆拿牌到手牌（用户要求：消耗牌堆也能选牌）。
        // 消耗牌堆和另外两摞一样是**战斗牌堆**（PileType.IsCombatPile() 认 Exhaust），
        // 所以走同一个本体 API：CardSelectCmd.FromCombatPile(PileType.Exhaust.GetPile(...)) + CardPileCmd.Add(…, Hand)。
        new EffectKindOption("TakeFromExhaust", "从消耗牌堆拿牌到手牌（自己选）", "张", 0, 5, false, false),
        // 放置卡牌（用户要求的新效果）：
        //   · 数值 0 = 把**这张牌自己**放到下面「放到哪一摞」选的那一摞
        //     （本体「放回手牌 / 置顶」那类写法：`await CardPileCmd.Add(this, PileType.X, position)`）；
        //   · 数值 ≥ 1 = 从「从哪里选牌」那一摞（手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆 / 牌顶）挑 N 张，
        //     再放到「放到哪一摞」（含牌顶 = 抽牌堆最上面）。
        // 走的是本体同一个 API：CardPileCmd.Add(card, PileType.X, CardPilePosition.Top/Bottom)。
        new EffectKindOption("PlaceCard", "放置卡牌（数值 0 = 这张牌自己）", "张", 0, 9, false, false),
        // ===== 单体/群体「成吨」的两种特殊牌（用户点名的本体效果）=====
        // 毒性爆发（本体 Outbreak，静默猎手稀有技能）：给**所有敌人**上 N 层中毒，然后立刻把中毒触发一次。
        // 走的是本体 PowerCmd.Apply<PoisonPower> + PoisonPower.Trigger()（那一下伤害 dealer 是 null，
        // 所以不会算成「你造成的伤害」、也不吃力量）。
        new EffectKindOption("Outbreak", "毒性爆发（全体上毒并立即触发）", "层", 0, 99, false, false),
        // 大限已至（本体 Time's Up，亡灵缚者稀有攻击）：造成等于**目标身上灾厄层数**的伤害。
        // 本体用 CalculatedDamageVar + WithMultiplier(target => target.GetPowerAmount<DoomPower>())，
        // 数值那一栏没有意义（伤害完全由目标的灾厄决定），所以范围是 0~0。
        new EffectKindOption("TimesUp", "大限已至（伤害 = 目标身上的灾厄层数）", "—", 0, 0, false, true),
        // 用「目标卡」填满手牌（本体卡「迫降 CrashLanding」的做法）：算出手牌还差几张
        // （CardPile.MaxCardsInHand - 手牌数），再逐张 CreateCard 一次性加进手牌。
        // 目标卡在「目标卡」那一栏选（和生成 / 变化 / 强化共用同一栏）；数值没有意义（0~0）。
        // 注意**不能**列进 AmountIsStrength —— 否则「数值 0」会把整条效果当成「不存在」丢掉
        // （和「清除指定状态」那几条同一个道理：它们只列进 HasNoDynamicVar）。
        new EffectKindOption("FillHand", "用「目标卡」填满手牌（本体「迫降」那种）", "—", 0, 0, false, false),
        // 给予卡牌关键词：数值 = 选几张牌（**0 = 这张牌自己**），从「选牌方式 / 从哪里选牌」挑，
        // 给它们加上「给予关键词」里选的那个关键词（可以是自定义关键词）；勾「是否为临时关键词」
        // 就只本回合有效（保留 / 奇巧走本体单回合标记，其余的由生成的临时 Power 在回合结束摘掉）。
        new EffectKindOption("GiveKeyword",     "给予卡牌关键词", "张", 0, 9, false, false),
        // ===== 全局（直接改玩家的牌组：跨战斗永久生效）=====
        // 「全局」= 直接动玩家的牌组（PileType.Deck），不是战斗里的手牌 / 抽牌堆。
        // 参考本体：篝火「烹饪」用 CardSelectCmd.FromDeckForRemoval + CardPileCmd.RemoveFromDeck 删牌；
        // 事件 / 遗物往牌组塞牌用 owner.RunState.CreateCard(...) + CardPileCmd.Add(card, PileType.Deck)。
        new EffectKindOption("AddCardGlobal",       "获得卡牌（全局：加进牌组）",     "张", 0, 5, false, false),
        new EffectKindOption("TransformCardGlobal", "变化卡牌（全局：改牌组里的牌）", "张", 0, 5, false, false),
        new EffectKindOption("RemoveCardGlobal",    "删除卡牌（全局：从牌组删牌）",   "张", 0, 5, false, false),
        // 升级卡牌（全局）：升级**牌组**里的牌（永久，跨战斗）。本体「香盒 Pomander / 混沌之香」那种。
        // 和上面那个「升级卡牌」的区别：那个升级的是战斗里的手牌 / 抽牌堆 / 弃牌堆（只影响本场战斗），
        // 这个直接改玩家的牌组（写进存档，和「获得卡牌（全局）」同一档）。
        new EffectKindOption("UpgradeCardGlobal",   "升级卡牌（全局：升级牌组里的牌）", "张", 0, 5, false, false),
        // 获得卡牌奖励：按本体的奖励卡生成规则抽 N 张（用你角色自己的卡池），让玩家选一张加进牌组。
        // 挂在「战斗胜利后」（状态 / 遗物）时走本体的战斗奖励：room.AddExtraReward(new CardReward(...))，
        // 打赢后结算界面多一条「选一张卡」；挂在其它时机（战斗中）就是当场弹选牌界面。
        new EffectKindOption("CardReward",          "获得卡牌奖励（N 选一）", "张", 0, 5, false, false),
        // ===== 召唤伙伴（完全不需要 Harmony 补丁）=====
        // 走本体的通用宠物 API（PlayerCmd.AddPet<T>，Byrdpip / Pael's Legion 就是这么用的），
        // 所以只要有一个 MonsterModel 子类就能上场。用法见「召唤物」页（列表 + 详情）。
        // 数值 = 0 时有特殊含义（用那一只召唤物自己配置的血量），所以下限是 0。
        new EffectKindOption("SummonPet",   "召唤伙伴", "点生命", 0, 999, false, false),
        // 伙伴攻击：attacker 是宠物 —— 生成时先走正常卡牌路径（FromCard + Targeting），
        // 再用扩展方法 FromPetAttacker 把攻击者换成宠物（**不能用 FromMonster**：
        // 那会把来源标成 Monster，GetPossibleTargets() 在 _sourceType == Monster 时硬编码返回玩家自己人）。
        // 目标沿用卡牌的「作用对象」。宠物不在场时这张牌会跳过这一条（不报错）。
        // 只支持卡牌 —— 遗物没有「玩家选中的目标」，宠物该打谁说不清。
        new EffectKindOption("PetAttack",   "伙伴攻击", "点", 0, 999, true, true),
        // ===== 新增的那批宠物效果（都要先选「召唤物（哪一只）」，宠物不在场时安全跳过）=====
        // 三个「按生命值算的伙伴攻击」用的都是**本体自己的计算变量三件套**：
        //   CalculationBaseVar(0) + ExtraDamageVar(1) + CalculatedDamageVar(ValueProp.Move).WithMultiplier(…)
        // 少了任何一件，本体 CalculatedVar.GetExtraVar() 会直接抛 KeyNotFoundException。
        // 倍率 lambda 必须是**静态**的（CalculatedVar.cs:50-53 判 multiplierCalc.Target is AbstractModel 就抛异常），
        // 所以生成的是 delegate(CardModel card, Creature? _) { … }，里头只用参数 card，绝不捕获实例。
        new EffectKindOption("PetDamageByMaxHp",     "伙伴攻击（按最大生命值）",   "点", -999, 999, false, true),
        new EffectKindOption("PetDamageByCurHp",     "伙伴攻击（按当前生命值）",   "点", -999, 999, false, true),
        new EffectKindOption("PetDamageByMissingHp", "伙伴攻击（按已损失生命值）", "点", -999, 999, false, true),
        // 治疗 / 失去生命 / 最大生命：分别走 HealVar / HpLossVar / MaxHpVar（和普通版本同一套变量）。
        new EffectKindOption("PetHeal",     "治疗伙伴",     "点", 0, 999, false, false),
        new EffectKindOption("PetLoseHp",   "伙伴失去生命", "点", 0, 999, false, false),
        // 注意：本体的 GainMaxHp 内部最后会 Heal 等量 → 当前生命也跟着涨（卡面描述里写明了）。
        new EffectKindOption("PetGainMaxHp", "伙伴最大生命 +N", "点", 0, 999, false, false),
        // 牺牲伙伴：收益类型（格挡 / 伤害 / 获得增益·减益）+ 收益公式（固定 N / 最大生命 × 倍率 / 当前生命）。
        // 生成顺序不能反：**先算收益（或层数）、再杀宠物**（宠物死了就取不到生命值）；
        // 收益是「获得增益 / 减益」时最后再 PowerCmd.Apply 把状态挂到自己身上。
        new EffectKindOption("PetSacrifice", "牺牲伙伴换收益", "点", 0, 999, false, true),
        // 给伙伴施加状态：复用现有的「增益 / 减益」下拉（PowerId）+ 层数（Amount）。
        new EffectKindOption("PetApplyPower", "给伙伴施加状态", "层", 0, 99, false, false),
        // 替主人承伤 开 / 关：用我们自己的共用守卫类 ForgePetGuardianPower（不是本体的 DieForYouPower）。
        new EffectKindOption("PetGuardOn",  "伙伴替主人承伤（开）", "—", 0, 0, false, false),
        new EffectKindOption("PetGuardOff", "取消伙伴替主人承伤（关）", "—", 0, 0, false, false),
    };

    /// <summary>
    /// 「哪些效果种类是宠物类效果」这份名单（现在**只为记录 / 校验用** ——
    /// 界面上已经没有专属效果栏了：原先那个「召唤物卡牌」选项卡已按用户要求整页删除，
    /// 12 个宠物效果就在「卡牌」页的「效果种类」下拉（全量 <see cref="EffectKinds"/>）里选）。
    ///
    /// 这份名单仍然有价值：校验器按它给「这一档只支持卡牌 / 必须先选召唤物」之类提示，
    /// 生成端（<c>CSharpCodeGen.IsPetKindForMarker</c>、<c>EffectSpec.IsPetEffect</c>）也按同一份
    /// 事实判断，以后要加界面提示 / 快捷筛选时还能直接用。
    ///
    /// 顺序与主注册表 <see cref="EffectKinds"/> 完全一致（不会两处漂移）。
    /// </summary>
    private static readonly HashSet<string> PetKindIds = new(StringComparer.Ordinal)
    {
        "SummonPet", "PetAttack",
        "PetDamageByMaxHp", "PetDamageByCurHp", "PetDamageByMissingHp",
        "PetHeal", "PetLoseHp", "PetGainMaxHp", "PetSacrifice", "PetApplyPower",
        "PetGuardOn", "PetGuardOff",
    };

    /// <summary>
    /// 宠物类效果（从 <see cref="EffectKinds"/> 里按 <see cref="PetKindIds"/> 筛出来的那份名单）。
    ///
    /// 界面不再有专属效果栏，所以它现在**只为记录 / 校验用**（自检拿它证明「12 个宠物效果
    /// 仍然全都在全量 Kinds 里、在「卡牌」页选得到」）；保留它是为了「哪些是宠物效果」这个事实
    /// 只有一处定义，别的代码 / 以后的功能都能复用。
    /// </summary>
    public static IReadOnlyList<EffectKindOption> PetEffectKinds { get; } =
        EffectKinds.Where(k => PetKindIds.Contains(k.Kind)).ToList();

    /// <summary>这个效果种类是不是「宠物类效果」（和 <c>EffectSpec.IsPetEffect</c> 的名单保持一致）。</summary>
    public static bool IsPetKind(string? kind) => kind is not null && PetKindIds.Contains(kind);

    /// <summary>「生成卡牌」的放置位置。</summary>
    /// <summary>
    /// 「生成卡牌」生成出来的牌放进哪一摞（界面上那个「生成到（生成卡牌用）」下拉）。
    /// 手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆 —— 四者都是本体的「战斗内牌堆」（<c>PileType.IsCombatPile()</c>），
    /// 所以走的是同一套 <c>CardPileCmd.AddToCombatAndPreview / AddGeneratedCardToCombat</c>；
    /// 消耗牌堆的用法是「生成出来就直接用掉 / 不参与抽牌」（例如只是拿它计数或触发消耗类效果）。
    /// </summary>
    public static IReadOnlyList<SpawnTargetOption> SpawnTargets { get; } = new[]
    {
        new SpawnTargetOption("Hand", "手牌（本体默认）"),
        new SpawnTargetOption("Draw", "抽牌堆"),
        new SpawnTargetOption("Discard", "弃牌堆"),
        new SpawnTargetOption("Exhaust", "消耗牌堆（生成出来就直接消耗掉，不参与抽牌）"),
    };

    public static string SpawnTargetZh(string? v) => v switch
    {
        "Draw" => "抽牌堆",
        "Discard" => "弃牌堆",
        "Exhaust" => "消耗牌堆",
        _ => "手牌",
    };

    /// <summary>
    /// 「消耗卡牌 / 变化卡牌 / 升级卡牌 / 丢弃卡牌 / 复制卡牌 / 给予关键词」从哪一摞牌里选（界面上那个下拉）。
    /// 手牌 = 本体 CardSelectCmd.FromHand；其余三摞 = 本体 CardSelectCmd.FromCombatPile。
    /// **消耗牌堆也算战斗牌堆**（本体的 <c>PileType.IsCombatPile()</c> 认 Hand / Draw / Discard / Exhaust / Play），
    /// 所以四摞都能选牌（用户要求把消耗牌堆也加进来：可以从消耗牌堆里把牌拿回来 / 复制 / 给关键词…）。
    /// </summary>
    public static IReadOnlyList<PileChoiceOption> SelectPiles { get; } = new[]
    {
        new PileChoiceOption("Hand", "手牌（本体默认）"),
        new PileChoiceOption("Draw", "抽牌堆（本体「充能 / 净化」那种）"),
        new PileChoiceOption("Discard", "弃牌堆（本体「全息影像 / 头槌 / 挖掘」那种）"),
        new PileChoiceOption("Exhaust", "消耗牌堆（本场战斗已经被消耗掉的牌）"),
        // 用户要求：「选牌 / 取牌方式」里加一个「牌顶」——抽牌堆**最上面那张**，直接拿走，
        // 不问玩家也不随机（取 N 张就从上往下依次拿）。本体抽牌堆的 Cards 是「顶在前」，
        // 和「预见 Scry」看的「抽牌堆顶」是同一个口径。
        new PileChoiceOption("Top", "牌顶（抽牌堆最上面那张，不用挑、也不随机）"),
    };

    public static string SelectPileZh(string? v) => v switch
    {
        "Draw" => "抽牌堆",
        "Discard" => "弃牌堆",
        "Exhaust" => "消耗牌堆",
        "Top" => "牌顶",
        _ => "手牌",
    };

    /// <summary>
    /// 「放置卡牌」放到哪一摞（用户要求：抽牌堆 / 弃牌堆 / 手牌 / 消耗牌堆 / 牌顶）。
    /// 「牌顶」= 抽牌堆**最上面**那张的位置（本体 <c>CardPilePosition.Top</c>），
    /// 「抽牌堆」那一档就是本体的默认位置（牌堆**底**）。
    /// </summary>
    public static IReadOnlyList<PileChoiceOption> PlacePiles { get; } = new[]
    {
        new PileChoiceOption("Hand", "手牌"),
        new PileChoiceOption("Draw", "抽牌堆（牌堆底）"),
        new PileChoiceOption("Discard", "弃牌堆"),
        new PileChoiceOption("Exhaust", "消耗牌堆"),
        new PileChoiceOption("Top", "牌顶（抽牌堆最上面）"),
    };

    public static string PlacePileZh(string? v) => v switch
    {
        "Draw" => "抽牌堆",
        "Discard" => "弃牌堆",
        "Exhaust" => "消耗牌堆",
        "Top" => "牌顶",
        _ => "手牌",
    };

    /// <summary>
    /// 「丢弃卡牌」能丢的那几摞牌：手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆。
    /// 从消耗牌堆丢弃 = 把已经消耗掉的牌挪回弃牌堆（洗牌后会回到抽牌堆）—— 是一条有用的效果
    /// （用户要求把消耗牌堆加进来），所以不再限制成只有前两摞。
    /// </summary>
    public static IReadOnlyList<PileChoiceOption> DiscardPiles { get; } = SelectPiles;

    /// <summary>「消耗卡牌 / 变化卡牌」的选牌方式。</summary>
    public static IReadOnlyList<string> CardPickModes { get; } = new[] { "Random", "Chosen" };
    public static string CardPickZh(string v) => v == "Chosen" ? "自己选" : "随机";

    /// <summary>
    /// 「生成 / 变化卡牌」的**范围限定**（用户要求：可以决定是攻击、技能还是能力，或者是诅咒和状态）。
    /// 空 = 不限（本体卡池里的都能出）。攻击 / 技能 / 能力按 <c>CardType</c> 过滤，
    /// 诅咒 / 状态按 <c>CardRarity</c> 过滤（本体的诅咒 / 状态是另外两个卡池）。
    /// </summary>
    public static IReadOnlyList<SpawnFilterOption> SpawnFilters { get; } = new[]
    {
        new SpawnFilterOption("", "不限（本体卡池里的都能出）", "不限"),
        new SpawnFilterOption("Attack", "攻击牌（本体「发现 / 炼制药水」那种）", "攻击"),
        new SpawnFilterOption("Skill", "技能牌", "技能"),
        new SpawnFilterOption("Power", "能力牌", "能力"),
        new SpawnFilterOption("Curse", "诅咒牌（从诅咒卡池 + 你自己的诅咒里挑）", "诅咒"),
        new SpawnFilterOption("Status", "状态牌（伤口 / 灼伤那种）", "状态"),
    };

    // ==================== 「自定义关键词」当作「卡的组」用 ====================
    // 范围限定里除了上面那 6 条内置的，还会按配置里的每个自定义关键词多一条
    //（Id = "Keyword:<关键词的英文标识>"）—— 于是「带某个关键词的一批牌」就成了一套可复用的组，
    // 随机生成 / 变化都能只从这一组里出牌。
    /// <summary>范围限定里「自定义关键词」那一类选项的 Id 前缀。</summary>
    public const string KeywordFilterPrefix = "Keyword:";

    /// <summary>关键词标识 → 显示名（登记表：Profile 变化时由界面 / 生成器更新）。</summary>
    private static readonly Dictionary<string, string> CustomKeywordNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>按配置里的自定义关键词更新登记表（界面刷新 / 生成前各调一次），让没有 profile 参数的地方也能显示名字。</summary>
    public static void SetCustomKeywords(IEnumerable<(string Key, string Display)>? keywords)
    {
        CustomKeywordNames.Clear();
        if (keywords is null) return;
        foreach (var (key, name) in keywords)
            if (!string.IsNullOrWhiteSpace(key))
                CustomKeywordNames[key.Trim()] = string.IsNullOrWhiteSpace(name) ? key.Trim() : name.Trim();
    }

    /// <summary>这个范围限定的值是不是「按自定义关键词分组」。</summary>
    public static bool IsKeywordFilter(string? v) =>
        v is not null && v.StartsWith(KeywordFilterPrefix, StringComparison.Ordinal);

    // ==================== 「组」（「自定义参数 / 组」页）也当成一套可复用的卡组 ====================
    // 和上面「自定义关键词当组用」完全同构，只是分组依据换成 CardSpec.GroupIds。
    // 需求原文：「在卡牌属性名称右侧可以勾选进入组，当选择范围时可以选择组」——
    // 所以范围限定 / 范围界定 / 刮削条件那三处下拉里，每个组都会多出一条「组：XXX」。
    /// <summary>范围限定里「组」那一类选项的 Id 前缀。</summary>
    public const string GroupFilterPrefix = "Group:";

    /// <summary>这个范围限定的值是不是「按组分组」。</summary>
    public static bool IsCardGroupFilter(string? v) =>
        v is not null && v.StartsWith(GroupFilterPrefix, StringComparison.Ordinal);

    /// <summary>「Group:XXX」里的组内部标识（不是组过滤就返回 null）。</summary>
    public static string? CardGroupFilterId(string? v) =>
        IsCardGroupFilter(v) ? v![GroupFilterPrefix.Length..].Trim() : null;

    /// <summary>组名（配置里查不到就用内部标识本身，绝不返回空）。</summary>
    public static string CardGroupFilterName(CharacterProfile? p, string? v)
    {
        string? id = CardGroupFilterId(v);
        if (string.IsNullOrWhiteSpace(id)) return "组";
        var g = p?.CardGroups?.FirstOrDefault(x => x is not null && string.Equals(x.Id, id, StringComparison.Ordinal));
        if (g is null || string.IsNullOrWhiteSpace(g.Name)) return id;
        return g.Name.Trim();
    }

    /// <summary>「按组分组」时这一组里到底有哪些牌（和 <see cref="KeywordGroupCards"/> 同构）。</summary>
    public static List<CardSpec> CardGroupCards(CharacterProfile? p, string? filterId)
    {
        var list = new List<CardSpec>();
        string? id = CardGroupFilterId(filterId);
        if (p is null || string.IsNullOrWhiteSpace(id)) return list;
        foreach (var c in p.AllCards)
        {
            if (c is null || c.IsVanillaCard) continue;
            if (c.GroupIdList.Contains(id, StringComparer.Ordinal)) list.Add(c);
        }
        return list;
    }

    /// <summary>「Keyword:XXX」里的关键词标识（不是关键词过滤就返回 null）。</summary>
    public static string? KeywordFilterKey(string? v) =>
        IsKeywordFilter(v) ? v![KeywordFilterPrefix.Length..].Trim() : null;

    /// <summary>自定义关键词的显示名（登记表里查不到就用标识本身，绝不返回空）。</summary>
    public static string KeywordFilterName(string? v)
    {
        string? key = KeywordFilterKey(v);
        if (string.IsNullOrWhiteSpace(key)) return "自定义关键词";
        return CustomKeywordNames.TryGetValue(key, out string? name) && name.Length > 0 ? name : key;
    }

    /// <summary>
    /// 范围限定的全部选项（内置 6 条 + 每个自定义关键词一组）。
    /// 界面那个「范围限定」下拉绑这个，所以配置里加 / 删关键词后要重新取一次（见 MainWindow 的刷新）。
    /// </summary>
    public static IReadOnlyList<SpawnFilterOption> SpawnFiltersFor(CharacterProfile? p)
    {
        var list = SpawnFilters.ToList();
        if (p is null) return list;
        foreach (var (spec, key) in KeywordGen.All(p))
        {
            string name = string.IsNullOrWhiteSpace(spec.Name) ? key : spec.Name.Trim();
            list.Add(new SpawnFilterOption(KeywordFilterPrefix + key,
                $"自定义关键词：{name}（带这个关键词的那一组牌）", name));
        }
        // 每个「组」也各一条（需求：当选择范围时可以选择组）
        if (p.CardGroups is not null)
        {
            foreach (var g in p.CardGroups)
            {
                if (g is null || string.IsNullOrWhiteSpace(g.Id)) continue;
                string name = string.IsNullOrWhiteSpace(g.Name) ? g.Id : g.Name.Trim();
                list.Add(new SpawnFilterOption(GroupFilterPrefix + g.Id, $"组：{name}", name));
            }
        }
        return list;
    }

    /// <summary>
    /// 「转化为先古卡」的**源组**候选：只要「组」那一套（自定义关键词 + 自定义组），
    /// 不要内置的「攻击 / 技能 / 能力 / 诅咒 / 状态」（需求原话：「来源只用组就行」）。
    ///
    /// 和 <see cref="SpawnFiltersFor"/> 共用同一份登记表，所以「自定义参数 / 组」页里改了组名 /
    /// 加了关键词，这里跟着变，不用再维护第二份选项。
    /// </summary>
    public static IReadOnlyList<SpawnFilterOption> GroupOnlyFiltersFor(CharacterProfile? p) =>
        SpawnFiltersFor(p)
            .Where(f => IsCardGroupFilter(f.Id) || IsKeywordFilter(f.Id))
            .ToList();

    // ==================== 「转化为先古遗物」的源遗物筛选 ====================
    // 遗物没有「组」这种概念（组是卡的分类），所以这一档用**遗物自己的分类**：
    //   · 不限（身上任何一个遗物，从前往后取第一个）
    //   · 只要是初始遗物（= 本体欧洛巴斯之触的判据：p.Relics.FirstOrDefault(r => r.Rarity == Starter)）
    //   · 指定某一类本体遗物（按类名精确匹配）
    /// <summary>「转化为先古遗物」里「只要初始遗物」这一档的 Id。</summary>
    public const string RelicFilterStarter = "Starter";

    /// <summary>「转化为先古遗物」里「指定某一类本体遗物」那一档的 Id 前缀。</summary>
    public const string RelicFilterPrefix = "Relic:";

    /// <summary>这个筛选值是不是「指定某一类遗物」。</summary>
    public static bool IsSpecificRelicFilter(string? v) =>
        v is not null && v.StartsWith(RelicFilterPrefix, StringComparison.Ordinal);

    /// <summary>「Relic:XXX」里的遗物类名（不是就返回 null）。</summary>
    public static string? RelicFilterClass(string? v) =>
        IsSpecificRelicFilter(v) ? v![RelicFilterPrefix.Length..].Trim() : null;

    /// <summary>源遗物那一档的全部候选（不限 + 初始遗物 + 本体每一只遗物）。</summary>
    public static IReadOnlyList<SpawnFilterOption> RelicSwapFilters()
    {
        var list = new List<SpawnFilterOption>
        {
            new("", "不限（身上第一个遗物）", "不限"),
            new(RelicFilterStarter, "只换初始遗物（本体欧洛巴斯之触的判据）", "初始遗物"),
        };
        foreach (var (id, name) in AncientCatalog.VanillaRelics)
            list.Add(new SpawnFilterOption(RelicFilterPrefix + id, $"只换「{name}」（{id}）", name));
        return list;
    }

    /// <summary>源遗物那一档的中文说明（生成前提示 / 卡面描述用）。</summary>
    public static string RelicSwapFilterZh(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "不限";
        if (string.Equals(v, RelicFilterStarter, StringComparison.Ordinal)) return "初始遗物";
        string? cls = RelicFilterClass(v);
        if (cls is not null) return AncientCatalog.VanillaRelicName(cls) ?? cls;
        return v.Trim();
    }

    public static string SpawnFilterZh(string? v)
    {
        if (IsKeywordFilter(v)) return KeywordFilterName(v);
        if (IsCardGroupFilter(v)) return CardGroupFilterName(null, v);
        foreach (var f in SpawnFilters)
            if (string.Equals(f.Id, v ?? "", StringComparison.Ordinal)) return f.Zh;
        return "不限";
    }

    /// <summary>
    /// 范围限定的中文名（**手上有配置时用这个**）：自定义关键词 / 组优先按 profile 里的名字查，
    /// 查不到才退回登记表 —— 生成 / 卡面描述那几条路都是拿着 profile 的，
    /// 不能依赖「界面刚才刷新过登记表」这个前提（不然描述里会写成英文标识）。
    /// </summary>
    public static string SpawnFilterZhFor(CharacterProfile? p, string? v)
    {
        if (IsKeywordFilter(v) && p is not null)
        {
            string? key = KeywordFilterKey(v);
            foreach (var (spec, k) in KeywordGen.All(p))
                if (string.Equals(k, key ?? "", StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(spec.Name) ? k : spec.Name.Trim();
        }
        if (IsCardGroupFilter(v)) return CardGroupFilterName(p, v);
        return SpawnFilterZh(v);
    }

    /// <summary>
    /// 「按自定义关键词分组」时，这一组里到底有哪些牌。
    ///
    /// 生成端拿它写成**显式牌表**（关键词只是我们自己的文案 + 悬停提示，游戏里没有对应枚举，
    /// 运行时过滤不出来，所以只能在生成期查配置）；校验器拿它报「这一组是空的 / 比候选张数还少」。
    /// 只统计**自己的牌**（本体卡带不了自定义关键词），和卡池里的口径一致。
    /// </summary>
    public static List<CardSpec> KeywordGroupCards(CharacterProfile? p, string? filterId)
    {
        var list = new List<CardSpec>();
        string? key = KeywordFilterKey(filterId);
        if (p is null || string.IsNullOrWhiteSpace(key)) return list;
        foreach (var c in p.AllCards)
        {
            if (c is null || c.IsVanillaCard) continue;
            if (c.CustomKeywordList.Any(k => string.Equals((k ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase)))
                list.Add(c);
        }
        return list;
    }

    // ==================== 「刮削 ScrapeDraw」的条件 / 操作 ====================
    // 需求原文：「效果种类的『抽 N 张牌丢弃其中所有非 0 费牌』，条件设宽泛一定，抽 N，
    //          对其中满足一定条件的牌执行特定操作」。也就是把原来写死的**判据**和**动作**都变成可选的。
    //
    // 条件那一栏**复用上面那份范围限定选项表**（SpawnFiltersFor：不限 / 攻击 / 技能 / 能力 / 诅咒 / 状态 /
    // 每个自定义关键词一组）—— 界面候选、中文名、自定义关键词的查表逻辑全都只有一份，不另造一套。
    // 只额外在前面加两条：非 0 费（老存档的默认值 = 本体「刮削」的判据）和不限。
    //
    // 注意范围限定里的「不限」Id 是**空串**，这里不能用空串当 Id（那样和「没填」分不清），统一换成 "Any"。

    /// <summary>刮削的条件：非 0 费（默认，= 本体「刮削 Scrape」的判据）。</summary>
    public const string ScrapeCondNonZeroCost = "NonZeroCost";
    /// <summary>刮削的条件：不限（抽到的每一张都动手）。</summary>
    public const string ScrapeCondAny = "Any";

    /// <summary>刮削的操作：丢进弃牌堆（默认；洗牌后会回来）。</summary>
    public const string ScrapeOpDiscard = "Discard";
    /// <summary>刮削的操作：消耗掉（本场战斗不再回来）。</summary>
    public const string ScrapeOpExhaust = "Exhaust";
    /// <summary>刮削的操作：升级。</summary>
    public const string ScrapeOpUpgrade = "Upgrade";

    /// <summary>
    /// 刮削「对其中哪些牌动手」（条件）的全部候选：非 0 费 + 不限 + 范围限定那一整份
    ///（攻击 / 技能 / 能力 / 诅咒 / 状态 / 每个自定义关键词一组）。
    /// 界面上那个下拉绑它，所以配置里加 / 删关键词后要重新取一次（见 MainWindow.RefreshCustomKeywordRegistry）。
    /// </summary>
    public static IReadOnlyList<SpawnFilterOption> ScrapeCondsFor(CharacterProfile? p)
    {
        var list = new List<SpawnFilterOption>
        {
            new(ScrapeCondNonZeroCost, "非 0 费的牌（默认，本体「刮削 Scrape」的判据）", "非 0 费"),
            new(ScrapeCondAny, "不限（抽到的每一张都动手）", "不限"),
        };
        foreach (var f in SpawnFiltersFor(p))
        {
            // 范围限定里那条「不限」的 Id 是空串：这里「不限」已经单列成 Any 了，跳过它，
            // 免得把空串当成一个 Id（回读 / 校验都会认不出来）。
            if (string.IsNullOrEmpty(f.Id)) continue;
            list.Add(f);
        }
        return list;
    }

    /// <summary>刮削条件的中文名（非 0 费 / 不限 自己给文案，其余走范围限定那一份查表）。</summary>
    public static string ScrapeCondZhFor(CharacterProfile? p, string? v)
    {
        string id = (v ?? "").Trim();
        if (id.Length == 0 || string.Equals(id, ScrapeCondAny, StringComparison.Ordinal)) return "不限";
        if (string.Equals(id, ScrapeCondNonZeroCost, StringComparison.Ordinal)) return "非 0 费";
        return SpawnFilterZhFor(p, id);   // 攻击 / 技能 / 能力 / 诅咒 / 状态 / 自定义关键词那一组
    }

    /// <summary>这个条件 Id 认不认识（校验器用它报「不认识的条件」）。</summary>
    public static bool IsKnownScrapeCond(string? v)
    {
        string id = (v ?? "").Trim();
        if (id.Length == 0) return true;                                  // 空 = 不限（手改 JSON 的兜底）
        if (id is ScrapeCondNonZeroCost or ScrapeCondAny) return true;
        // 自定义关键词 / 组那一组：这两样是不是真的存在于配置里由「空组」那条检查负责，这里只认前缀
        if (IsKeywordFilter(id)) return true;
        if (IsCardGroupFilter(id)) return true;
        return id is "Attack" or "Skill" or "Power" or "Curse" or "Status";
    }

    /// <summary>
    /// 刮削「对选中的牌执行什么操作」的候选。
    /// 形状和范围限定那条一样（Id / Display / Zh），这样界面上的下拉绑定、中文名查表都是同一套写法。
    /// </summary>
    public static IReadOnlyList<SpawnFilterOption> ScrapeOps { get; } = new[]
    {
        new SpawnFilterOption(ScrapeOpDiscard, "丢弃（丢进弃牌堆，洗牌后会回来）", "丢弃"),
        new SpawnFilterOption(ScrapeOpExhaust, "消耗掉（本场战斗不再回来）", "消耗掉"),
        new SpawnFilterOption(ScrapeOpUpgrade, "升级（本体「武装 Armaments」那种升级）", "升级"),
    };

    /// <summary>刮削操作的中文名。</summary>
    public static string ScrapeOpZh(string? v) => (v ?? "").Trim() switch
    {
        ScrapeOpExhaust => "消耗掉",
        ScrapeOpUpgrade => "升级",
        _ => "丢弃",
    };

    /// <summary>这个操作 Id 认不认识（校验器用它报「不认识的操作」）。</summary>
    public static bool IsKnownScrapeOp(string? v) =>
        (v ?? "").Trim() is "" or ScrapeOpDiscard or ScrapeOpExhaust or ScrapeOpUpgrade;

    /// <summary>
    /// 是不是「默认的刮削」（非 0 费 + 丢弃）。生成端按它决定要不要写条件 / 操作（不写 = 与旧版逐字一致），
    /// 标记行按它决定要不要追加 CET:ScrapeCond / CET:ScrapeOp。
    /// </summary>
    public static bool IsDefaultScrape(string? cond, string? op) =>
        ((cond ?? "").Trim() is "" or ScrapeCondNonZeroCost)
        && ((op ?? "").Trim() is "" or ScrapeOpDiscard);

    /// <summary>「生成 / 变化卡牌」是「指定卡」还是「按范围随机」。</summary>
    public static IReadOnlyList<SpawnPickOption> SpawnPicks { get; } = new[]
    {
        new SpawnPickOption("Fixed", "指定卡（用上面的「目标卡」）"),
        new SpawnPickOption("Random", "按范围随机（用下面的「范围限定」）"),
    };

    /// <summary>
    /// 「获得药水」（需求 ⑨）是「指定药水」还是「随机药水」。
    /// 单独一份、不复用 <see cref="SpawnPicks"/>：那一份的文案是「用上面的「目标卡」」，
    /// 套到药水上会指错栏位（「目标药水」那一行在效果面板里的位置不一样）。
    /// </summary>
    public static IReadOnlyList<SpawnPickOption> PotionPicks { get; } = new[]
    {
        new SpawnPickOption("Fixed", "指定药水（用「目标药水」那一栏）"),
        new SpawnPickOption("Random", "随机药水（本体「炼制药水」那种随机）"),
    };

    public static EffectKindOption FindKind(string kind) =>
        EffectKinds.FirstOrDefault(k => string.Equals(k.Kind, kind, StringComparison.OrdinalIgnoreCase)) ?? EffectKinds[0];

    // ==================== 条件选项 ====================
    /// <summary>
    /// 条件种类。实现方式全部照抄本体「有条件判断」的卡牌 / 遗物：
    /// 劫掠（循环条件）、取回（本回合这张牌还没打出过 + 金边）、死亡之门（本回合施加过某状态）、
    /// 牺牲（Osty 存活）、Clash / GrandFinale（IsPlayable 条件 = 不能打出）、
    /// 怀表（上一回合打出的牌数）、不休陀螺（手牌为空）、百年积木（每场战斗只触发一次）。
    /// </summary>
    public static IReadOnlyList<ConditionOption> Conditions { get; } = new[]
    {
        new ConditionOption("None", "无条件（默认）", true, true, false, false, "不做任何判断，效果一定生效。", true),

        new ConditionOption("NotPlayedThisTurn", "本回合还没打出过这张牌", true, false, false, false,
            "本体「取回」的做法：查战斗历史，本回合这张牌还没被打出过。"),
        new ConditionOption("NotPlayedThisCombat", "本场战斗还没打出过这张牌", true, false, false, false,
            "查战斗历史：这局战斗里这张牌一次都还没被打出过。"),
        new ConditionOption("PlayedAtLeast", "本回合已打出至少 N 张牌", true, true, true, false,
            "本体「怀表」的做法：数本回合打出的牌数（含其他牌）。填负数 = 反向（本回合打出的牌少于 N 张）。",
            true, false, false, "本回合打出的牌少于 N 张", true),
        new ConditionOption("HandAtLeast", "手牌数不少于 N 张", true, true, true, false,
            "数当前手牌张数。填负数 = 反向（手牌数少于 N 张）。", true, false, false, "手牌数少于 N 张", true),
        new ConditionOption("HandAtMost", "手牌数不多于 N 张", true, true, true, false,
            "数当前手牌张数。填负数 = 反向（手牌数多于 N 张）。", true, false, false, "手牌数多于 N 张", true),
        new ConditionOption("HandOnlyAttack", "手牌里只有攻击牌", true, true, false, false,
            "本体「Clash」的做法：手牌里全是攻击牌（含这张）。", true),
        new ConditionOption("HandOnlySkill", "手牌里只有技能牌", true, true, false, false, "手牌里全是技能牌（含这张）。", true),
        new ConditionOption("DrawPileEmpty", "抽牌堆为空", true, true, false, false,
            "本体「GrandFinale」的做法：抽牌堆已经抽空。", true),
        new ConditionOption("DiscardPileEmpty", "弃牌堆为空", true, true, false, false, "弃牌堆里一张牌都没有。", true),
        new ConditionOption("HpBelowPercent", "生命值低于 N%", true, true, true, false,
            "按最大生命的百分比判断（含等于）。填负数 = 反向（生命值**高于** N%）。可以选看谁：自己 / 任意一个敌人 / 全部敌人。",
            true, true, false, "生命值高于 N%", true),
        // 生命区间：两个数值（下界 N% / 上界 M%）。**任一填负数 = 判断成「区间外」**（用户要求）：
        // 例如 20~50 = 生命在 20%~50% 之间；-20~50 = 生命低于 20% 或高于 50%。
        new ConditionOption("HpInRange", "生命值在 N%~M% 之间", true, true, true, false,
            "两个数值都填百分比（下界 / 上界，顺序无所谓，会自动排好）。**任意一个填负数 = 判断成「区间外」**。"
            + "可以选看谁：自己 / 任意一个敌人 / 全部敌人。", true, true, true, "生命值在 N%~M% 之外（区间外）", true),
        new ConditionOption("HasPowerAtLeast", "拥有某状态至少 N 层", true, true, true, true,
            "本体「死亡之门 / 拆解」这类做法：读增益/减益层数。填负数 = 反向（拥有某状态**少于** N 层）。"
            + "可以选看谁：自己 / 任意一个敌人 / 全部敌人。", true, true, false, "拥有某状态少于 N 层", true),
        new ConditionOption("ExtraResourceAtLeast", "拥有额外资源量至少 N 点", true, true, true, false,
            "额外资源量就是「角色」页里那个自定义资源（界面上那个计数器）。只有你自己才有，所以不用选指向对象。"
            + "填负数 = 反向（拥有的额外资源量**少于** N 点）。", true, false, false, "拥有的额外资源量少于 N 点", true),
        // 「层数刚好达到 N 层」（用户要求：拥有某状态至少 N 层 / 达到 N 层）：
        // 「至少 N 层」是一条**随时可查**的条件（上面 HasPowerAtLeast）；这一条是**这一刻刚好跨过 N**——
        // 只有「某个状态层数变化后」那条触发时机上才有意义（那条钩子同时知道「变成多少」和「这次变了多少」），
        // 所以它只给自定义状态用，而且校验器会拦住别的触发时机。
        new ConditionOption("PowerJustReached", "某状态层数刚好达到 N 层（这次变化跨过了 N）", false, false, true, true,
            "只在触发时机「某个状态层数变化后」上有意义：判断「这次变化之前还不到 N 层、变化之后到了 N 层或更多」"
            + "（本体 AfterPowerAmountChanged 给的 power.Amount 是新的、amount 是这一次的增量）。"
            + "想判「只要现在有 N 层就生效」请用上面的「拥有某状态至少 N 层」。",
            ForPower: true),
        new ConditionOption("NoHurtThisTurn", "本回合还没受到过未格挡伤害", true, true, false, false,
            "本体「Spite」的做法：查本回合是否吃到过没被格挡的伤害。", true),
        // 斩杀（用户要求）：本体先古卡 / 稀有卡里那句「[gold]斩杀[/gold]时，…」。
        // 本体源码里就是这三件事（Feed / HandOfGreed / TheHunt 一模一样）：
        //   ① 打之前记下 `cardPlay.Target.Powers.All(p => p.ShouldOwnerDeathTriggerFatal())`
        //      （小怪 / 会复活的目标返回 false —— 本体 MinionPower / ReattachPower）；
        //   ② 伤害走 AttackCommand，然后 `attackCommand.Results.SelectMany(r => r).Any(r => r.WasTargetKilled)`；
        //   ③ 两个都成立才算「斩杀」。
        // 所以这条条件只能给卡牌用，而且要写在**造成伤害那一条效果之后**（它判的是上一条攻击的结果）。
        new ConditionOption("Fatal", "斩杀（这一次攻击杀死了目标）", true, false, false, false,
            "本体「斩杀 Fatal」：这一次攻击**真的把目标打死了**才算满足 —— 生成的是本体那套判据"
            + "（AttackCommand.Results 里 WasTargetKilled，且目标的 Power 都允许触发斩杀，小怪 / 会复活的打不死不算）。"
            + "只能用在卡牌上，而且要写在「造成伤害」那一条效果**后面**（它看的是上一条攻击的结果）。", true),
        // 抽牌堆被洗过牌（用户要求：像本体先古遗物「大～抱抱 BiiigHug」那样）。
        // 本体**没有**任何可查询的「洗过牌」状态（没有事件、没有历史记录、没有计数器），
        // 大~抱抱自己是靠 `override AfterShuffle` 在洗牌那一刻反应的。所以这里分两种给：
        //   · 条件（这两条）= 工具生成一个 **Harmony 补丁**记下「谁、哪一回合、洗了几次」，
        //     于是「本回合洗过 / 本场战斗洗过」能被任何地方查询（卡牌打不出判定、遗物效果门控…）；
        //   · 遗物的触发时机「抽牌堆打乱洗牌时」= 直接覆写 AfterShuffle，**在洗牌那一刻**触发（和大~抱抱一样）。
        // 共同注意点（本体机制）：开局的洗牌不算（走的是 ModifyShuffleOrder，不触发 AfterShuffle）、
        // 战斗结束/收尾阶段也不触发。
        new ConditionOption("ShuffledThisTurn", "抽牌堆本回合被打乱洗过牌", true, true, false, false,
            "本体没有任何「洗过牌」的状态可查，工具会生成一个补丁 + 记录器（记谁、哪一回合、洗了几次）。"
            + "注意：**开局的洗牌不算**（本体开战那次只走 ModifyShuffleOrder），战斗结束阶段也不算。", true),
        new ConditionOption("ShuffledThisCombat", "抽牌堆本场战斗被打乱洗过牌", true, true, false, false,
            "同上，但看的是「这一场战斗里洗过没有」（战斗换一场就重新算）。"),
        new ConditionOption("EveryNTurns", "每 N 回合触发一次", false, true, true, false,
            "按战斗回合数取余（第 N、2N、3N…回合满足）。"),
        new ConditionOption("OncePerCombat", "每场战斗只触发一次", false, true, false, false,
            "本体「百年积木」的做法：用标记记住本场用过，战斗结束清零。"),
        // 卡牌时机（需求⑩）：唯一一个**不是「打出时算一个 bool」**的条件 —— 带它的效果不写进 OnPlay，
        // 而是挂到本体对应钩子的 override 上（本体「战鼓」DrumOfBattle 的做法：
        // override AfterCardExhausted 里判 card == this）。所以它只能给卡牌用，见 CSharpCodeGen.EmitCardTimingHooks。
        new ConditionOption("CardTiming", "卡牌时机（这张牌被消耗 / 被丢弃 / 被保留…时）", true, false, false, false,
            "这一条不是「打出的条件」，而是一个**钩子**：带这个条件的效果不会在打出时执行，"
            + "而是挂到本体对应时机的钩子上（和本体「战鼓」一样 —— 它是 override AfterCardExhausted）。"
            + "所以它的效果里不能用「打出的目标」（那个钩子里没有打出记录），也不能用 X 值 / 伙伴。"
            + "只能用在普通卡牌上。", false, false, false, null, false, true),
    };

    /// <summary>
    /// 「卡牌时机」的 6 个子时机（需求⑩）。顺序 = 界面下拉里的顺序（也是存档里没写 Timing 时的默认值：第一项）。
    ///
    /// 每个子时机**挂哪个钩子**写在 <c>CSharpCodeGen.CardTimingHook</c> 里（连同为什么选那个钩子的说明）：
    /// 被消耗 → AbstractModel.AfterCardExhausted；被丢弃 → AbstractModel.AfterCardDiscarded；
    /// 被生成 → AbstractModel.AfterCardGeneratedForCombat；被保留 → AbstractModel.AfterFlush；
    /// 被变化 → CardModel.AfterTransformedFrom；被给予自定义关键词 → 我们自己的「给予卡牌关键词」那一步。
    /// </summary>
    public static IReadOnlyList<CardTimingOption> CardTimings { get; } = new[]
    {
        new CardTimingOption("Exhausted", "被消耗"),
        new CardTimingOption("Generated", "被生成"),
        new CardTimingOption("Retained", "被保留"),
        new CardTimingOption("Discarded", "被丢弃"),
        new CardTimingOption("Transformed", "被变化"),
        new CardTimingOption("GivenKeyword", "被给予自定义关键词"),
        // 用户要求：再加一个「失去生命时」（这张牌在你手上时你掉血 → 触发）。
        // 挂的钩子是 AbstractModel.AfterCurrentHpChanged(creature, delta)，delta < 0 就是掉了血。
        new CardTimingOption("HpLost", "失去生命时"),
    };

    /// <summary>存档里 Timing 字段的默认值（= 下拉第一项「被消耗」，也是老存档缺这一项时的退路）。</summary>
    public const string DefaultCardTiming = "Exhausted";

    public static CardTimingOption? FindCardTiming(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null
        : CardTimings.FirstOrDefault(t => string.Equals(t.Id, id.Trim(), StringComparison.Ordinal));

    /// <summary>子时机的中文名（认不出来时退回 Id，界面 / 描述里不会出现空白）。</summary>
    public static string CardTimingZh(string? id) => FindCardTiming(id)?.Display ?? (id ?? "").Trim();

    /// <summary>
    /// 子时机写进卡面描述 / 生成代码注释的那句话（「当这张牌被消耗时」这种）。
    /// 界面上的下拉文案是 <see cref="CardTimingOption.Display"/>（就三个字），描述里要成句，所以分两份。
    /// </summary>
    public static string CardTimingPhrase(string? id) => FindCardTiming(id)?.Id switch
    {
        "Exhausted" => "当这张牌被消耗时",
        "Generated" => "当这张牌被生成时",
        "Retained" => "当这张牌被保留时",
        "Discarded" => "当这张牌被丢弃时",
        "Transformed" => "当这张牌被变化（被变成别的牌）时",
        "GivenKeyword" => "当这张牌被给予自定义关键词时",
        _ => "当这张牌被" + CardTimingZh(id) + "时",
    };

    /// <summary>
    /// 条件的「指向对象」：这个条件看谁身上的状态 / 生命值。
    /// 「任意一个敌人」= 只要有一个敌人满足就算满足；「全部敌人」= 每个活着的敌人都得满足。
    /// </summary>
    public static IReadOnlyList<ConditionTargetOption> ConditionTargets { get; } = new[]
    {
        new ConditionTargetOption("Self", "自己"),
        new ConditionTargetOption("Enemy", "指定敌人（这张牌打的目标）"),
        new ConditionTargetOption("AnyEnemy", "任意一个敌人"),
        new ConditionTargetOption("AllEnemies", "全部敌人"),
    };

    /// <summary>
    /// 本体里「状态栏显示的数字不是层数」的那些状态 —— 它们自己 override 了 <c>PowerModel.DisplayAmount</c>。
    ///
    /// 用户实测踩过：「施加 30 层缓慢」在游戏里只看到「缓慢」，没有 30。原因是本体
    /// <c>SlowPower.DisplayAmount => SlowAmount × 10</c>（本回合每打出一张牌 +1 → 显示 +10%），
    /// 跟施加的层数毫无关系；本体自己也只施加 1 层（BygoneEffigy：PowerCmd.Apply&lt;SlowPower&gt;(…, 1m, …)）。
    /// 所以对这类状态，不管填多少层，状态栏那个数字都不会是你填的值。
    ///
    /// 这张表只用来在校验里提醒用户（不参与生成）；键 = 本体的 Power 类名。
    /// 清单是把本机解包工程里 override int DisplayAmount 的 Power 全扫一遍得来的。
    /// </summary>
    public static IReadOnlyDictionary<string, string> PowerAmountNotes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SlowPower"] = "它显示的是「本回合已打出的牌数 × 10」（每打出一张牌，受到伤害 +10%），跟施加的层数无关；本体自己也只施加 1 层。",
            ["SlothPower"] = "它显示的是「本回合已打出的牌数」。",
            ["TenderPower"] = "它显示的是「本回合已打出的牌数」。",
            ["PaleBlueDotPower"] = "它显示的是「5 − 本回合已打出的攻击牌数」。",
            ["AutomationPower"] = "它显示的是「还剩几张牌要用」。",
            ["CacophonyPower"] = "它显示的是它自己内部记的牌数。",
            ["PanachePower"] = "它显示的是它自己内部记的剩余牌数。",
            ["WitheringPresencePower"] = "它显示的是它自己内部记的剩余牌数。",
            ["MonologuePower"] = "它显示的是「已经给了多少点力量」。",
            ["FeralPower"] = "它显示的是「层数 − 已经打出的 0 费攻击牌数」。",
            ["HardenedShellPower"] = "它显示的是「层数 − 本回合已受到的伤害」。",
            ["OrbitPower"] = "它显示的是「4 − 已花能量 % 4」。",
            ["TagTeamPower"] = "它固定显示 1。",
        };

    /// <summary>这个状态显示的数字是不是「不是层数」？是的话给一句说明（不是返回 null）。</summary>
    public static string? PowerAmountNote(string? powerId) =>
        powerId is not null && PowerAmountNotes.TryGetValue(powerId.Trim(), out string? note) ? note : null;

    /// <summary>
    /// 本体那套 <c>CardTag</c>（行为无关的元数据，给别的模型查牌用）。
    /// 本体的初始「打击 / 防御」就是靠它被认出来的 —— 那些「升级初始打击/防御」的遗物
    /// （妮欧的护符 NeowsTalisman、叶敷剂 LeafyPoultice、大胶囊 LargeCapsule、幽灵种子 GhostSeed…）
    /// 都是 <c>card.Tags.Contains(CardTag.Strike)</c> 这种查法，有的还会再加「稀有度 = Basic」。
    /// </summary>
    public static IReadOnlyList<string> CardTags { get; } = new[] { "Strike", "Defend", "Minion", "OstyAttack", "Shiv" };

    /// <summary>CardTag 的中文说明（界面上显示用）。措辞来自本体源码里的实际用途。</summary>
    public static string CardTagZh(string tag) => tag switch
    {
        "Strike" => "Strike（打击：本体的打击牌。本体那些「升级你的初始打击」「只对打击牌生效」的遗物 / 卡牌按它查牌）",
        "Defend" => "Defend（防御：本体的防御牌。同上，防御版；打出时会自动加 GainsBlock）",
        "Minion" => "Minion（仆从：本体「仆从俯冲 / 仆从捐躯 / 仆从强击」那类牌。储君的商店遗物「维特鲁威仆从」会给带这个标签的牌双倍伤害与格挡）",
        "OstyAttack" => "OstyAttack（奥斯提的攻击：亡灵契约师那只骷髅随从「奥斯提」的攻击牌，如碎骨 / 戳击 / 重压 / 榨取；本体多张牌按它计数）",
        "Shiv" => "Shiv（小刀：静默猎手的小刀。精准 / 幻影之刃 / 刀刃陷阱 这些按它生效）",
        _ => tag,
    };

    /// <summary>「升级后的关键字」每一行的三个选项（不变 / 升级后获得 / 升级后失去）。</summary>
    public static IReadOnlyList<KeywordStateOption> KeywordUpgradeStates { get; } = new[]
    {
        new KeywordStateOption("Keep", "不变"),        new KeywordStateOption("Add", "升级后获得"),
        new KeywordStateOption("Remove", "升级后失去"),
    };

    /// <summary>查一位先古之民（对话槽位）。</summary>
    public static AncientEntry? FindAncient(string? id) => AncientCatalog.Find(id);

    public static IReadOnlyList<AncientEntry> Ancients => AncientCatalog.Ancients;

    public static ConditionOption? FindCondition(string? kind) =>
        string.IsNullOrWhiteSpace(kind) ? null
        : Conditions.FirstOrDefault(c => string.Equals(c.Id, kind, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 条件的中文说明（界面列表 / 卡面描述 / 生成代码注释都用这一份，别各写一遍）。
    ///
    /// 处理四件事：
    ///   ① N 的替换；
    ///   ② **填负数 = 反向**（用户要求）：生命值低于 -20 → 「生命值高于 20%」、
    ///      「拥有某状态至少 -2 层」→「拥有某状态少于 2 层」，等等（文案取 <see cref="ConditionOption.InvertedDisplay"/>）；
    ///   ③ 「生命区间」两个数值（N~M，任一为负 = 区间外）；
    ///   ④ 指向对象 / 「额外资源量」的显示名；
    ///   ⑤ 「卡牌时机」的子时机（<paramref name="timing"/>）—— 这一类的文案整句由子时机决定。
    /// </summary>
    public static string ConditionZh(string kind, decimal amount, decimal amount2, string? powerId,
        string targetZh, string? resourceName = null, string? timing = null,
        string? amountFormula = null, string? amount2Formula = null)
    {
        var opt = FindCondition(kind);
        if (opt is null || string.Equals(opt.Id, "None", StringComparison.Ordinal)) return "";
        // 卡牌时机：整句都按子时机写（「当这张牌被消耗时」），没有数值 / 状态 / 指向对象要替换
        if (opt.NeedsTiming) return CardTimingPhrase(timing);
        // 「条件数值写了公式」（用户要求⑦）：文案里就把公式印出来（和「数值公式」同一套观感），
        // 不然界面上写着「手牌数不少于 3 张」，实际判的却是 Turn。
        string f1 = Generation.ForgeFormula.DisplayText(amountFormula);
        string f2 = Generation.ForgeFormula.DisplayText(amount2Formula);
        bool hasF1 = f1.Length > 0, hasF2 = f2.Length > 0;
        bool inverted = !hasF1 && (amount < 0 || (opt.NeedsAmount2 && amount2 < 0));
        string text = inverted && opt.InvertedDisplay is not null ? opt.InvertedDisplay : opt.Display;
        string Abs(decimal v) => Math.Abs(v).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        if (opt.NeedsAmount2)
        {
            // 生命区间：两个数值都按绝对值写（谁填了负数只是表示「区间外」，数值本身还是那个正数），顺序写反了自动排好
            decimal lo = Math.Abs(amount), hi = Math.Abs(amount2);
            if (lo > hi) (lo, hi) = (hi, lo);
            text = text.Replace("N", hasF1 ? f1 : lo.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                       .Replace("M", hasF2 ? f2 : hi.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (opt.NeedsAmount)
        {
            text = text.Replace("N", hasF1 ? f1 : Abs(amount));
        }
        if (opt.NeedsPower) text = text.Replace("某状态", PowerName(powerId, "某状态"));
        if (opt.NeedsTarget && !string.IsNullOrWhiteSpace(targetZh) && targetZh != "自己")
            text += $"（{targetZh}）";
        // 「额外资源量」用界面 / 游戏里真正显示的那个名字（用户可能叫它「冰附魔」）
        if (!string.IsNullOrWhiteSpace(resourceName) && resourceName.Trim() != "额外资源量")
            text = text.Replace("额外资源量", resourceName.Trim());
        return text;
    }

    /// <summary>
    /// 卡牌类型（本体 CardType 枚举里能用的那些）。
    /// 「状态」是照本体「粘液 Slimed」加的那一档：<c>base(1, CardType.Status, CardRarity.Status, TargetType.None)</c>。
    /// </summary>
    public static IReadOnlyList<string> CardTypes { get; } = new[] { "Attack", "Skill", "Power", "Status", "Curse", "Quest" };
    /// <summary>
    /// 卡牌稀有度。除了本体那三档奖励稀有度（含 Basic），还有：
    ///   · Curse —— 诅咒（「诅咒 / 先古卡」页里的第一个列表；费用固定 -1、不能被打出、效果走 OnTurnEndInHand）
    ///   · Ancient —— 先古卡（同一个页面的第二个列表；不进战斗奖励，由先古遗物 / 你自己的效果给）
    ///   · Status —— 状态牌（照本体「粘液 Slimed」加的那一档：类型 Status、稀有度 Status、不能升级）
    /// 本体的 <c>CardFactory</c> 只会在 Common / Uncommon / Rare 里抽奖励，所以把这几种放进卡池也不会
    /// 让它们跑进普通奖励（诅咒 / 状态连稀有度都抽不到，先古卡是被显式排除的）。
    /// </summary>
    public static IReadOnlyList<string> CardRarities { get; } = new[] { "Basic", "Common", "Uncommon", "Rare", "Ancient", "Status", "Curse" };
    /// <summary>
    /// 这个效果种类**真的**支持「下回合生效」吗（单一判据：界面显隐 + 校验器都读它）。
    ///
    /// 为什么不直接用 <see cref="EffectKindOption.SupportsNextTurn"/>：那一栏以前给 8 个种类也写了 true
    ///（透支能量 / 翻倍状态层数 / 暴露 / 清除指定状态 / 清除全部减益 / 禁止获得格挡 / 禁止回复生命 / 伙伴攻击），
    /// 但**生成端根本没有对应的延迟分支** —— 勾了等于没勾（静默失效，用户报过「勾了没反应」）。
    /// 对着 `CSharpCodeGen` 里所有 `e.NextTurn` 分支核对过（卡牌 / 遗物 / 药水 / 自定义状态四条链），
    /// 真正实现的只有下面这五种：
    ///   格挡 → 本体 `BlockNextTurnPower`；抽牌 → 本体 `DrawCardsNextTurnPower`；
    ///   能量 → 本体 `EnergyNextTurnPower`（负数走工具生成的欠费状态）；额外资源量 → 本体 `StarNextTurnPower`；
    ///   施加 / 扣除状态 → 工具生成的 `&lt;角色&gt;ForgeDelayed&lt;状态&gt;` 延迟状态。
    /// 生成 / 变化 / 复制卡牌那几种想要「下回合生成」需要再做一个延迟状态（在队列里，还没做）。
    /// </summary>
    public static bool HonorsNextTurn(string? kind) =>
        kind is "Block" or "Draw" or "Energy" or "ExtraResource" or "ApplyPower"
        // 生成类（V0.2.88 起，V0.2.90 补齐变化 / 复制）：延迟状态里调用同一套发射器
        //（`EmitGenerateCard` / `EmitCopyCard` / `EmitTransformCard` / `EmitGlobalCardEffect`）。
        or "GenerateCard" or "CopyCard" or "TransformCard"
        or "AddCardGlobal" or "TransformCardGlobal" or "RemoveCardGlobal" or "UpgradeCardGlobal";

    // 遗物稀有度。Ancient = 本体「先古遗物」那一档（`RelicRarity.Ancient`）：本体的先古遗物
    //（ArchaicTooth / Astrolabe / BlackStar…）全是这一档，而且**不是**从普通奖励 / 商店里抽出来的，
    // 是先古之民给的 —— 所以选它时界面会提示「想让它在奖励池里出现请换 Common/Uncommon/Rare/Shop」。
    public static IReadOnlyList<string> RelicRarities { get; } = new[] { "Starter", "Common", "Uncommon", "Rare", "Shop", "Ancient" };

    /// <summary>
    /// 遗物稀有度下拉的**界面选项**（用户要求第 3 条：英文改中文 —— 初始 / 常见 / 罕见 / 稀有 / 先古 / 商店）。
    /// <c>Id</c> 还是 <see cref="RelicRarities"/> 里那串英文（存档 / 生成端一个字符都不变），
    /// 只有 <c>Display</c> 是中文；界面用 <c>DisplayMemberPath="Display"</c> +
    /// <c>SelectedValuePath="Id"</c> 绑 <c>RelicSpec.Rarity</c>。
    /// </summary>
    public static IReadOnlyList<RarityOption> RelicRarityOptions { get; } = new[]
    {
        new RarityOption("Starter",  "初始"),
        new RarityOption("Common",   "常见"),
        new RarityOption("Uncommon", "罕见"),
        new RarityOption("Rare",     "稀有"),
        new RarityOption("Ancient",  "先古"),
        new RarityOption("Shop",     "商店"),
    };

    /// <summary>遗物稀有度的中文显示名（列表 / 提示里显示用；认不出来的原样返回）。</summary>
    public static string RelicRarityZh(string? id) =>
        RelicRarityOptions.FirstOrDefault(o => string.Equals(o.Id, id ?? "", StringComparison.Ordinal))?.Display
        ?? (string.IsNullOrWhiteSpace(id) ? "常见" : id!.Trim());

    public static IReadOnlyList<string> PotionRarities { get; } = new[] { "Common", "Uncommon", "Rare" };

    /// <summary>
    /// 药水稀有度下拉的**界面选项**（用户要求：《遗物、药水优化.md》第 6 条「药水方面参考遗物的优化方案」
    /// → 遗物页第 3 条「将中间部分的稀有度显示的英文更改成中文」照抄到药水页）。
    ///
    /// <para><c>Id</c> 还是 <see cref="PotionRarities"/> 里那串英文（存档 / 生成端一个字符都不变），
    /// 只有 <c>Display</c> 是中文；界面用 <c>DisplayMemberPath="Display"</c> +
    /// <c>SelectedValuePath="Id"</c> 绑 <c>PotionSpec.Rarity</c>。</para>
    ///
    /// <para>档位只有三档（<c>Common</c> / <c>Uncommon</c> / <c>Rare</c>）—— 和
    /// <see cref="PotionRarities"/> 一模一样，**没有加档**：本体 <c>PotionRarity</c> 枚举其实还有
    /// <c>None</c> / <c>Event</c> / <c>Token</c>，但那是本体内部用的档（事件 / 代币药水不是从奖励池里抽的），
    /// 工具从来没给过它们，加了反而会让用户配出抽不到的药水。中文名和遗物页**同一套词**
    /// （常见 / 罕见 / 稀有），两页看同一个概念不会出现两个叫法。</para>
    /// </summary>
    public static IReadOnlyList<RarityOption> PotionRarityOptions { get; } = new[]
    {
        new RarityOption("Common",   "常见"),
        new RarityOption("Uncommon", "罕见"),
        new RarityOption("Rare",     "稀有"),
    };

    /// <summary>药水稀有度的中文显示名（列表 / 提示里显示用；认不出来的原样返回）。</summary>
    public static string PotionRarityZh(string? id) =>
        PotionRarityOptions.FirstOrDefault(o => string.Equals(o.Id, id ?? "", StringComparison.Ordinal))?.Display
        ?? (string.IsNullOrWhiteSpace(id) ? "常见" : id!.Trim());

    /// <summary>
    /// 效果的「作用对象」候选。
    ///
    /// 「指定队友 / 全部队友」（用户要求）用的是本体现成的目标档：
    /// <c>TargetType.AnyAlly</c>（任意队友、**不含自己**）+ <c>AllAllies</c>（全部队友），
    /// 本体卡「BelieveInYou」是前者、「BladeSymphony」是后者。
    /// ⚠ 本体注释写得很清楚：**多人时才会弹目标选择，单人你看不到这一档**；
    /// 而且 `CardModel.cs:1742` —— 场上活着的玩家单位 ≤ 1 时 `AnyAlly` 的牌**直接打不出去**。
    /// </summary>
    /// <summary>
    /// 效果的「作用对象」候选（**V0.2.132 起 7 档**，共用一张表：卡牌 / 遗物 / 药水 / 自定义状态 /
    /// 锻造【效果】模块五处的下拉都绑它）。
    ///
    /// <para>档位与含义（需求原文：「作用对象包括『自己，指定队友，全部友方（可以选择包不包括召唤物），
    /// 单体敌人，随机敌人，全部敌人，全体单位』」）：</para>
    /// <list type="bullet">
    /// <item><c>Self</c> 自己。</item>
    /// <item><c>Ally</c> 指定队友（本体 <c>TargetType.AnyAlly</c>，**只有卡牌**有「玩家选中的目标」）。</item>
    /// <item><c>AllAllies</c> 全部友方 —— 同一边的玩家单位、排除自己；「包不包括召唤物」是
    /// <c>EffectSpec.AllAlliesExcludePets</c> 这个子选项（默认 false = 含召唤物，老行为）。</item>
    /// <item><c>Enemy</c> 单体敌人（卡牌用选中的那个目标；遗物 / 药水取可打的第一个敌人）。</item>
    /// <item><c>RandomEnemies</c> 随机敌人 N 个（N = 命中次数）。</item>
    /// <item><c>AllEnemies</c> 全部敌人（<c>CombatState.HittableEnemies</c>）。</item>
    /// <item><c>AllUnits</c> <b>全体单位（V0.2.132 新增）</b> —— 场上两边**所有存活单位**
    /// （自己 + 队友 + 召唤物 + 敌人），取 <c>CombatState.Creatures.Where(c =&gt; c.IsAlive)</c>。</item>
    /// </list>
    ///
    /// <para>⚠ 「指定队友 / 全部队友」用的是本体现成的目标档：
    /// <c>TargetType.AnyAlly</c>（任意队友、**不含自己**）+ <c>AllAllies</c>（全部队友），
    /// 本体卡「BelieveInYou」是前者、「BladeSymphony」是后者。
    /// 本体注释写得很清楚：**多人时才会弹目标选择，单人你看不到这一档**；
    /// 而且 `CardModel.cs:1742` —— 场上活着的玩家单位 ≤ 1 时 `AnyAlly` 的牌**直接打不出去**。</para>
    ///
    /// <para>⚠ 顺序 = 需求里的顺序（自己 / 队友 / 友方 / 单体敌人 / 随机敌人 / 全部敌人 / 全体单位），
    /// 只影响下拉的排列；<b>老档位的生成文本一个字节都没动</b>（生成端按字符串分支，与顺序无关）。</para>
    /// </summary>
    public static IReadOnlyList<string> TargetSides { get; } =
        new[] { "Self", "Ally", "AllAllies", "Enemy", "RandomEnemies", "AllEnemies", "AllUnits" };

    /// <summary>
    /// 作用对象的**中文名**（界面提示 / 校验消息 / 卡面文案共用一份，免得三处各写一套）。
    /// 认不出来的原样返回（手写 JSON 里可能有别的值）。
    /// </summary>
    public static string TargetSideZh(string? id) => (id ?? "").Trim() switch
    {
        "Self" => "自己",
        "Ally" => "指定队友",
        "AllAllies" => "全部友方",
        "Enemy" => "单体敌人",
        "RandomEnemies" => "随机敌人",
        "AllEnemies" => "全部敌人",
        "AllUnits" => "全体单位",
        var other => other,
    };

    /// <summary>
    /// 「全体单位」这一档**生成端真的实现了**的效果种类（V0.2.132）。
    ///
    /// 为什么要这张表：<c>CSharpCodeGen.EmitPerCreature</c> / <c>EmitAttack</c> / <c>EmitStun</c> 以及
    /// 各条目标分支的 <c>default</c> 都是「当成自己」—— 用不支持的种类选了「全体单位」会**静默变成
    /// 对自己**，所以校验器按这份名单**报错拦住**（见 <c>ProfileValidator</c>）。
    /// 名单和生成端逐条对应：伤害 / 格挡 / 回复生命 / 失去生命 / 最大生命 / 施加增益减益 / 临时增益 / 击晕。
    /// </summary>
    public static bool SupportsAllUnits(string? kind) =>
        kind is "Damage" or "Block" or "Heal" or "HpLoss" or "MaxHp" or "ApplyPower" or "TempPower" or "Stun";

    /// <summary>
    /// 「全体单位」在**自定义状态（能力）的触发时机**那条链上真的实现了的种类（V0.2.132）。
    ///
    /// <para>比 <see cref="SupportsAllUnits"/> 窄：状态链的「获得格挡 / 回复生命 / 失去生命 / 最大生命」
    /// 是**手写**的、只打 <c>base.Owner</c>（压根不读作用对象），选「全体单位」会静默变成「对自己」；
    /// 真正按作用对象展开的只有：造成伤害 / 施加增益减益 / 临时增益 / 击晕 / 清除指定状态 / 清除所有减益
    ///（后两个也走 <c>EmitPerCreature</c>）。</para>
    /// </summary>
    public static bool SupportsAllUnitsOnPower(string? kind) =>
        kind is "Damage" or "ApplyPower" or "TempPower" or "Stun" or "ClearPowerOne" or "ClearPowerDebuffs";
    public static IReadOnlyList<string> PotionUsages { get; } = new[] { "CombatOnly", "AnyTime" };
    public static IReadOnlyList<string> PotionTargets { get; } = new[] { "Self", "AnyEnemy", "AllEnemies" };

    public static IReadOnlyList<TriggerOption> RelicTriggers { get; } = new[]
    {
        // 获得时（拿到这只遗物的那一刻，本体钩子 AfterObtained —— 不加参数、局内一次性）：
        // 本体「好吃饼干 YummyCookie / 磨刀石 Whetstone / 爪子 Claws」这些「拾取时生效」的遗物都是它。
        // 钩子签名里没有 choiceContext，需要的那几条效果由生成端自己造一个阻塞式 context
        // （本体的 BlockingPlayerChoiceContext 注释里点名的第一个场景就是 "Relic AfterObtained callbacks"）。
        new TriggerOption("Obtained",       "获得时",       "AfterObtained()"),
        new TriggerOption("CombatStart",    "战斗开始时",   "BeforeSideTurnStart(choiceContext, side, participants, combatState)"),
        new TriggerOption("PlayerTurnStart","玩家回合开始时", "AfterPlayerTurnStart(choiceContext, player)"),
        new TriggerOption("PlayerTurnEnd",  "玩家回合结束时", "AfterSideTurnEnd(choiceContext, side, participants)"),
        new TriggerOption("CombatVictory",  "战斗胜利后",   "AfterCombatVictory(room)"),
        // 战斗结束时（用户要求）：本体 AfterCombatEnd —— 它跑在**房间结算之前**
        //（CombatManager.EndCombatInternal 里的顺序是：Hook.AfterCombatEnd → room.OnCombatEnded()
        //  → Hook.AfterCombatVictory → 发奖励 / 存档），所以「必须在结算前做完」的事挂这一条。
        // ⚠ 和「战斗胜利时」一样**只在打赢那条路上触发**：打输走的是 ProcessPendingLoss
        //（本体在那里只发 CombatEnded 事件、不调任何模型钩子），所以失败时这两条都不会跑。
        new TriggerOption("CombatEnd",      "战斗结束时（房间结算之前）", "AfterCombatEnd(room)"),
        // 受到伤害时 = 本体 AfterDamageReceived：**每一下伤害都触发一次**（14 个本体遗物用的是它）。
        // 本体的敌人攻击很多是「1 点 × N 下」的连击，所以一次「5 点伤害」的攻击会触发 5 次。
        new TriggerOption("DamageReceived", "自己受到伤害后（每一下）", "AfterDamageReceived(choiceContext, target, result, props, dealer, cardSource)"),
        // 受到攻击后 = 本体 AfterAttack：AbstractModel 的注释写明了 multi-attack 时它在**所有命中结束后只跑一次**，
        // 而且它同样带 choiceContext（AttackContext / AttackCommand 都把它传下去了）。
        new TriggerOption("Attacked",       "自己受到攻击后（连击只算一次）", "AfterAttack(choiceContext, command)"),
        new TriggerOption("GoldGained",     "获得金币时",   "AfterGoldGained(player)"),
        // ===== 用户要求新增的五条（都对着本体 AbstractModel 的钩子签名核过）=====
        // 造成伤害时（每一下）= 本体 AfterDamageGiven：**每一下命中都触发**（本体的多段攻击 = 多次）。
        // 和「受到伤害时（每一下）」是同一对钩子（`DamageReceived`）；想「一次攻击只算一次」用 AfterAttack
        //（本体「手里剑 / 苦无 / 折扇」那三只攻击遗物用的就是它）。
        new TriggerOption("DamageDealt",    "自己造成伤害后", "AfterDamageGiven(choiceContext, dealer, result, props, target, cardSource)"),
        // 每消耗一个能量 = 本体 AfterEnergySpent(CardModel card, int amount)：
        // 钩子**没有 choiceContext**（生成端按本体 Hook.cs 的做法自己造一个 HookPlayerChoiceContext，
        // 见 CSharpCodeGen.BootstrapInCombat）；amount 是「这一张牌一共花的」能量 → 按点数各触发一次。
        new TriggerOption("EnergySpent",    "花掉能量后",   "AfterEnergySpent(card, amount)"),
        // 每消耗一个额外资源量 = 本体 AfterStarsSpent(int amount, Player spender)：同样没有 choiceContext、
        // 同样按点数各触发一次（和本体储君那几条「花资源量」的状态一致）。
        new TriggerOption("StarsSpent",     "花掉额外资源量后", "AfterStarsSpent(amount, spender)"),
        // 生成卡牌时 = 本体 AfterCardGeneratedForCombat(CardModel card, Player? creator)：
        // **玩家自己生成**的牌才算（敌人塞给你的状态牌不算 —— 本体注释就是这么写的），每生成一张触发一次。
        new TriggerOption("CardGenerated",  "生成卡牌时",       "AfterCardGeneratedForCombat(card, creator)"),
        // 某个状态层数到达 N 层时（用户要求）：本体 AfterPowerAmountChanged
        // `(choiceContext, power, amount, applier, cardSource)` —— 任何单位的状态层数一变就会调，
        // 所以生成端要自己判「是自己身上的」「是盯的那个状态」「这一次跨过了 N 层」三件事
        //（见 CSharpCodeGen.RelicSource 里那条分支）。这两个参数（盯哪个状态 / 到达多少层）
        // 存在 RelicSpec.TriggerPowerId / TriggerAmount 上，界面只在选了这个时机时显示。
        new TriggerOption("PowerReached",   "某个状态层数到达 N 层时", "AfterPowerAmountChanged(choiceContext, power, amount, applier, cardSource)"),
        // ===== 用户要求「遗物 / 状态 / 卡牌的触发时机合并成同一套」：把状态那边已经有、遗物缺的那些补齐 =====
        // （同一个概念两边用同一个显示名和同一个 id；生成端各自按「遗物 / 状态」的归属判据写守卫。
        //   带 choiceContext 的照旧直接用，没带的由生成端自己造一个 —— 见 CSharpCodeGen.BootstrapInCombat。）
        new TriggerOption("CardPlayed",     "打出一张牌后",     "AfterCardPlayed(choiceContext, cardPlay)"),
        new TriggerOption("CardExhausted",  "消耗一张牌后",     "AfterCardExhausted(choiceContext, card, causedByEthereal)"),
        new TriggerOption("CardDrawn",      "抽一张牌后",       "AfterCardDrawn(choiceContext, card, fromHandDraw)"),
        new TriggerOption("CardDiscarded",  "弃掉一张牌后",     "AfterCardDiscarded(choiceContext, card)"),
        new TriggerOption("HpLost",         "自己失去生命时（每一下）", "AfterCurrentHpChanged(creature, delta)"),
        new TriggerOption("BlockGained",    "自己获得格挡后",   "AfterBlockGained(creature, amount, props, cardSource)"),
        new TriggerOption("EnemyDeath",     "一个敌人死亡后",   "AfterDeath(choiceContext, creature, wasRemovalPrevented, deathAnimLength)"),
        new TriggerOption("EnemyTurnStart", "敌人回合开始时",   "AfterSideTurnStart(side, participants, combatState)"),
        new TriggerOption("EnemyTurnEnd",   "敌人回合结束时",   "BeforeSideTurnEnd(choiceContext, side, participants)"),
        new TriggerOption("EnemyDamaged",   "敌人受到伤害后",   "AfterDamageReceived(choiceContext, target, result, props, dealer, cardSource)"),
        new TriggerOption("EnemyDealtDamage","敌人造成伤害后",  "AfterDamageGiven(choiceContext, dealer, result, props, target, cardSource)"),
        new TriggerOption("EnemyBlockGained","敌人获得格挡后",  "AfterBlockGained(creature, amount, props, cardSource)"),
        new TriggerOption("PowerChanged",   "某个状态层数变化后", "AfterPowerAmountChanged(choiceContext, power, amount, applier, cardSource)"),
        // 「移除某状态时」（用户要求）：本体**没有**任何「状态被移除」的全局钩子 ——
        // `PowerModel.AfterRemoved(Creature oldOwner)` 是**那个状态自己**的实例钩子，遗物 / 别的状态都收不到，
        // 而且「被效果直接清除」（`PowerCmd.Remove`）根本不走层数变化那条路。
        // 所以这条由工具自己打补丁：`PowerModel.RemoveInternal()` 是本体所有移除的唯一出口（见 PowerRemovalSource）。
        new TriggerOption("PowerRemoved",   "某个状态被移除时", "工具补丁：PowerModel.RemoveInternal（本体没有全局的「状态被移除」钩子）"),
        // 抽牌堆打乱洗牌时（用户要求：本体先古遗物「大～抱抱 BiiigHug」就是这条 ——
        // `public override async Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler)`
        // 里判 `shuffler == base.Owner`）。本体只在**战斗中途洗牌**时触发：
        // 开局那次洗牌走的是 ModifyShuffleOrder（不触发 AfterShuffle），战斗结束阶段也不触发。
        new TriggerOption("Shuffle",        "抽牌堆打乱洗牌时", "AfterShuffle(choiceContext, shuffler)"),
    };

    /// <summary>内置的开发用效果库（发布版仓库里不含这个文件，见 .gitignore）。</summary>
    private static IReadOnlyList<PowerEntry> LoadEmbedded()
    {
        try
        {
            const string resourceName = "Sts2CharForge.Core.Data.powers_catalog.json";
            using Stream? stream = typeof(EffectCatalog).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return Array.Empty<PowerEntry>();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            List<PowerEntry>? list = JsonSerializer.Deserialize<List<PowerEntry>>(stream, options);
            return list ?? (IReadOnlyList<PowerEntry>)Array.Empty<PowerEntry>();
        }
        catch { return Array.Empty<PowerEntry>(); }
    }
}
