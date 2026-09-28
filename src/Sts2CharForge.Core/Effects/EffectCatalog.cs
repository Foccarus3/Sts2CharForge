using System.Text;
using System.Text.Json;
using Sts2CharForge.Core.Generation;

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

public sealed record TriggerOption(string Id, string Display, string HookSignature);

/// <summary>
/// 一条「条件」选项。
/// ForCard / ForRelic = 这个条件能不能用在卡牌 / 遗物上；
/// ForPower = 能不能用在自定义状态（能力）的触发器效果上；
/// NeedsAmount = 需要填数值（N）；NeedsPower = 需要选一个增益/减益；
/// NeedsTarget = 还需要选「指向对象」（自己 / 敌人 / 全部敌人）。
/// </summary>
public sealed record ConditionOption(string Id, string Display, bool ForCard, bool ForRelic,
    bool NeedsAmount, bool NeedsPower, string Hint, bool ForPower = false, bool NeedsTarget = false);

/// <summary>条件的「指向对象」一条：看自己 / 任意一个敌人 / 全部敌人。</summary>
public sealed record ConditionTargetOption(string Id, string Display);

/// <summary>「从哪里选牌」一条：手牌 / 抽牌堆 / 弃牌堆。</summary>
public sealed record PileChoiceOption(string Id, string Display);

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
        if (!string.IsNullOrWhiteSpace(vanillaProjectDir) && Directory.Exists(Path.Combine(vanillaProjectDir, "src/Core/Models/Powers")))
            return false;                                   // 解包工程可用时优先用它（有中文名）
        try
        {
            string dll = string.IsNullOrWhiteSpace(gameDataDir) ? "" : Path.Combine(gameDataDir, "sts2.dll");
            if (dll.Length == 0 || !File.Exists(dll)) return false;

            var asm = System.Reflection.Assembly.LoadFrom(dll);
            Type? basePower = asm.GetType("MegaCrit.Sts2.Core.Models.PowerModel");
            if (basePower is null) return false;

            // 中文名：从玩家自己游戏本体的 pck 里读（工具不内嵌游戏文本）
            var zh = GamePckReader.ReadLocalization(gameDataDir, "localization/zhs/powers.json");
            RememberZhPowerLoc(zh);

            var list = new List<PowerEntry>();
            foreach (var type in asm.GetTypes())
            {
                if (type.IsAbstract || !basePower.IsAssignableFrom(type)) continue;
                string cls = type.Name;
                if (cls.StartsWith("Mock", StringComparison.Ordinal)) continue;
                string kind = "Buff";
                try
                {
                    // PowerModel 的 Type 通常是常量返回，未初始化实例也能读
                    object inst = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
                    object? v = type.GetProperty("Type")?.GetValue(inst);
                    if (v is not null && v.ToString() == "Debuff") kind = "Debuff";
                }
                catch { /* 读不到就当增益 */ }
                string slug = Slugify(cls);
                string name = ZhName(zh, slug, cls);
                list.Add(new PowerEntry(cls, slug, kind, "Counter", name, cls));
            }
            if (list.Count == 0) return false;
            Powers = list.OrderBy(p => p.Type, StringComparer.Ordinal).ThenBy(p => p.Zh, StringComparer.Ordinal).ToList();
            detail = $"从游戏 sts2.dll 反射读取（{Powers.Count} 项）" + (zh is not null
                ? "，中文名取自游戏本体的 localization/zhs/powers.json"
                : "，只有英文类名（" + (string.IsNullOrEmpty(GamePckReader.LastError) ? "没读到游戏 pck" : "读 pck 失败原因：" + GamePckReader.LastError) + "；可在「路径」指定解包工程目录后点「重新扫描效果库」）");
            return true;
        }
        catch (Exception ex)
        {
            detail = "（从 sts2.dll 反射失败：" + ex.Message + "）";
            return false;
        }
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
        }
        catch { LoadCardsFromGameDll(gameDataDir); }
    }

    /// <summary>
    /// 卡牌目录兜底：换台机器没有解包工程时，从游戏 sts2.dll 反射 CardModel 子类，
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
            var asm = System.Reflection.Assembly.LoadFrom(dll);
            Type? baseCard = asm.GetType("MegaCrit.Sts2.Core.Models.CardModel");
            if (baseCard is null) return;

            var list = new List<PowerEntry>();
            foreach (var type in asm.GetTypes())
            {
                if (type.IsAbstract || !baseCard.IsAssignableFrom(type)) continue;
                string cls = type.Name;
                if (cls.StartsWith("Mock", StringComparison.Ordinal)) continue;
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

    public static IReadOnlyList<EffectKindOption> EffectKinds { get; } = new[]
    {
        new EffectKindOption("Damage",  "造成伤害",     "点", 0,    999, false, true),
        // 格挡 / 回复生命 / 失去生命 / 最大生命 也能指定「作用对象」——
        // 本体的 CreatureCmd.GainBlock / Heal / GainMaxHp / Damage 收的都是 Creature，
        // 所以「给敌人加格挡」「给敌人回血」这类也能做出来（有些怪就是靠这个变强）。
        new EffectKindOption("Block",   "获得格挡",     "点", 0,    999, true,  true),
        new EffectKindOption("Draw",    "抽牌",         "张", 0,    10,  true,  false),
        new EffectKindOption("Energy",  "获得能量",     "点", 0,    10,  true,  false),
        new EffectKindOption("Heal",    "回复生命",     "点", -999, 999, false, true),
        new EffectKindOption("HpLoss",  "失去生命",     "点", 0,    999, false, true),
        new EffectKindOption("MaxHp",   "最大生命",     "点", -999, 999, false, true),
        new EffectKindOption("Gold",    "获得金币",     "枚", 0,    999, false, false),
            new EffectKindOption("ExtraResource", "获得额外资源量", "点", -999, 999, false, false),
        new EffectKindOption("ApplyPower", "施加增益/减益", "层", 1, 99, true, true),
        new EffectKindOption("EndTurn",   "结束回合",       "—", 0, 0, false, false),
        new EffectKindOption("ExtraTurn", "额外获得一回合", "—", 0, 0, false, false),
        new EffectKindOption("GenerateCard",  "生成卡牌", "张", 1, 10, false, false),
        new EffectKindOption("ExhaustCard",   "消耗卡牌", "张", 1, 9,  false, false),
        new EffectKindOption("TransformCard", "变化卡牌", "张", 1, 9,  false, false),
        // 从战斗中的牌堆「挑牌拿到手牌」：本体「搜寻 SecretTechnique / 全息影像 Hologram / 挖掘 Dredge」那种。
        // 走 CardSelectCmd.FromCombatPile + CardPileCmd.Add(..., PileType.Hand)。
        new EffectKindOption("TakeFromDraw",    "从抽牌堆拿牌到手牌（自己选）", "张", 1, 5, false, false),
        new EffectKindOption("TakeFromDiscard", "从弃牌堆拿牌到手牌（自己选）", "张", 1, 5, false, false),
        // ===== 全局（直接改玩家的牌组：跨战斗永久生效）=====
        // 「全局」= 直接动玩家的牌组（PileType.Deck），不是战斗里的手牌 / 抽牌堆。
        // 参考本体：篝火「烹饪」用 CardSelectCmd.FromDeckForRemoval + CardPileCmd.RemoveFromDeck 删牌；
        // 事件 / 遗物往牌组塞牌用 owner.RunState.CreateCard(...) + CardPileCmd.Add(card, PileType.Deck)。
        new EffectKindOption("AddCardGlobal",       "获得卡牌（全局：加进牌组）",     "张", 1, 5, false, false),
        new EffectKindOption("TransformCardGlobal", "变化卡牌（全局：改牌组里的牌）", "张", 1, 5, false, false),
        new EffectKindOption("RemoveCardGlobal",    "删除卡牌（全局：从牌组删牌）",   "张", 1, 5, false, false),
        // 获得卡牌奖励：按本体的奖励卡生成规则抽 N 张（用你角色自己的卡池），让玩家选一张加进牌组。
        // 挂在「战斗胜利后」（状态 / 遗物）时走本体的战斗奖励：room.AddExtraReward(new CardReward(...))，
        // 打赢后结算界面多一条「选一张卡」；挂在其它时机（战斗中）就是当场弹选牌界面。
        new EffectKindOption("CardReward",          "获得卡牌奖励（N 选一）", "张", 1, 5, false, false),
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
    };

    /// <summary>「生成卡牌」的放置位置。</summary>
    public static IReadOnlyList<string> SpawnTargets { get; } = new[] { "Hand", "Draw", "Discard" };
    public static string SpawnTargetZh(string v) => v switch { "Draw" => "抽牌堆", "Discard" => "弃牌堆", _ => "手牌" };

    /// <summary>
    /// 「消耗卡牌 / 变化卡牌」从哪一摞牌里选（界面上那个下拉）。
    /// 手牌 = 本体 CardSelectCmd.FromHand；抽牌堆 / 弃牌堆 = 本体 CardSelectCmd.FromCombatPile。
    /// </summary>
    public static IReadOnlyList<PileChoiceOption> SelectPiles { get; } = new[]
    {
        new PileChoiceOption("Hand", "手牌（本体默认）"),
        new PileChoiceOption("Draw", "抽牌堆（本体「充能 / 净化」那种）"),
        new PileChoiceOption("Discard", "弃牌堆（本体「全息影像 / 头槌 / 挖掘」那种）"),
    };

    public static string SelectPileZh(string? v) => v switch
    {
        "Draw" => "抽牌堆",
        "Discard" => "弃牌堆",
        _ => "手牌",
    };

    /// <summary>「消耗卡牌 / 变化卡牌」的选牌方式。</summary>
    public static IReadOnlyList<string> CardPickModes { get; } = new[] { "Random", "Chosen" };
    public static string CardPickZh(string v) => v == "Chosen" ? "自己选" : "随机";

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
            "本体「怀表」的做法：数本回合打出的牌数（含其他牌）。", true),
        new ConditionOption("HandAtLeast", "手牌数不少于 N 张", true, true, true, false, "数当前手牌张数。", true),
        new ConditionOption("HandAtMost", "手牌数不多于 N 张", true, true, true, false, "数当前手牌张数。", true),
        new ConditionOption("HandOnlyAttack", "手牌里只有攻击牌", true, true, false, false,
            "本体「Clash」的做法：手牌里全是攻击牌（含这张）。", true),
        new ConditionOption("HandOnlySkill", "手牌里只有技能牌", true, true, false, false, "手牌里全是技能牌（含这张）。", true),
        new ConditionOption("DrawPileEmpty", "抽牌堆为空", true, true, false, false,
            "本体「GrandFinale」的做法：抽牌堆已经抽空。", true),
        new ConditionOption("DiscardPileEmpty", "弃牌堆为空", true, true, false, false, "弃牌堆里一张牌都没有。", true),
        new ConditionOption("HpBelowPercent", "生命值低于 N%", true, true, true, false,
            "按最大生命的百分比判断（含等于）。可以选看谁：自己 / 任意一个敌人 / 全部敌人。", true, true),
        new ConditionOption("HasPowerAtLeast", "拥有某状态至少 N 层", true, true, true, true,
            "本体「死亡之门 / 拆解」这类做法：读增益/减益层数。可以选看谁：自己 / 任意一个敌人 / 全部敌人。", true, true),
        new ConditionOption("ExtraResourceAtLeast", "拥有额外资源量至少 N 点", true, true, true, false,
            "额外资源量就是「角色」页里那个自定义资源（界面上那个计数器）。只有你自己才有，所以不用选指向对象。", true),
        new ConditionOption("NoHurtThisTurn", "本回合还没受到过未格挡伤害", true, true, false, false,
            "本体「Spite」的做法：查本回合是否吃到过没被格挡的伤害。", true),
        new ConditionOption("EveryNTurns", "每 N 回合触发一次", false, true, true, false,
            "按战斗回合数取余（第 N、2N、3N…回合满足）。"),
        new ConditionOption("OncePerCombat", "每场战斗只触发一次", false, true, false, false,
            "本体「百年积木」的做法：用标记记住本场用过，战斗结束清零。"),
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

    public static IReadOnlyList<string> CardTypes { get; } = new[] { "Attack", "Skill", "Power" };
    public static IReadOnlyList<string> CardRarities { get; } = new[] { "Basic", "Common", "Uncommon", "Rare" };
    public static IReadOnlyList<string> RelicRarities { get; } = new[] { "Starter", "Common", "Uncommon", "Rare", "Shop" };
    public static IReadOnlyList<string> PotionRarities { get; } = new[] { "Common", "Uncommon", "Rare" };
    public static IReadOnlyList<string> TargetSides { get; } = new[] { "Self", "Enemy", "AllEnemies", "RandomEnemies" };
    public static IReadOnlyList<string> PotionUsages { get; } = new[] { "CombatOnly", "AnyTime" };
    public static IReadOnlyList<string> PotionTargets { get; } = new[] { "Self", "AnyEnemy", "AllEnemies" };

    public static IReadOnlyList<TriggerOption> RelicTriggers { get; } = new[]
    {
        new TriggerOption("CombatStart",    "战斗开始时",   "BeforeSideTurnStart(choiceContext, side, participants, combatState)"),
        new TriggerOption("PlayerTurnStart","每回合开始时", "AfterPlayerTurnStart(choiceContext, player)"),
        new TriggerOption("PlayerTurnEnd",  "每回合结束时", "AfterSideTurnEnd(choiceContext, side, participants)"),
        new TriggerOption("CombatVictory",  "战斗胜利时",   "AfterCombatVictory(room)"),
        new TriggerOption("DamageReceived", "受到伤害时",   "AfterDamageReceived(choiceContext, target, result, props, dealer, cardSource)"),
        new TriggerOption("GoldGained",     "获得金币时",   "AfterGoldGained(player)"),
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
