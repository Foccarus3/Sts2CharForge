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

    /// <summary>「额外获得一个回合」用的 Power。</summary>
    public string ExtraTurnPowerClass => CharClass + "ForgeExtraTurnPower";

    /// <summary>「透支能量」用的负债 Power（下回合少 N 点能量；本体的 GainEnergy 会忽略负数，只能自己扣）。</summary>
    public string EnergyDebtPowerClass => CharClass + "ForgeEnergyDebtPower";

    /// <summary>「下回合生效」用的延迟 Power（<c>&lt;角色&gt;ForgeDelayed&lt;状态&gt;</c>）。</summary>
    public string DelayedPowerClass(EffectSpec e) => CharClass + DelayedPowerSuffix(e);

    /// <summary>延迟 Power 的固定后半段（回读时按它认「下回合生效」）。</summary>
    public static string DelayedPowerSuffix(EffectSpec e) => "ForgeDelayed" + (e.PowerId ?? "Power");

    /// <summary>「替主人承伤」共用的守卫 Power（所有召唤物共用一个类，所以只能有一个）。</summary>
    public string GuardianPowerClass => CharClass + "ForgePetGuardianPower";

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
        foreach (var x in p.Cards)
        {
            if (ReferenceEquals(x, c)) break;
            if (!x.IsVanillaCard && !x.IsStartingBasic) own++;      // 初始的打击 / 防御不占号
        }
        return CharClass + "Card" + (own + 1).ToString();
    }

    public string RelicClassName(RelicSpec r, int index) =>
        IsValidIdentifier(r.ClassName) ? r.ClassName.Trim() : CharClass + "Relic" + (index + 1).ToString();

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
