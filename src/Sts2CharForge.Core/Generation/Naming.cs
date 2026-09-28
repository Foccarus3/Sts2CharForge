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
        return new Naming(Pascal(modId), modId, charClass, charEntry, charSlug, charSlug, charSlug,
            charClass + "CardPool", charClass + "RelicPool", charClass + "PotionPool");
    }

    public string CardClassName(CardSpec c, int index) =>
        IsValidIdentifier(c.ClassName) ? c.ClassName.Trim() : CharClass + "Card" + (index + 1).ToString();

    /// <summary>
    /// 卡牌类名（没填类名时，按「这是第几张自有卡」自动编号）。
    /// 为什么要单独一个重载：初始卡组里的本体卡（打击 / 防御）排在卡牌列表最上面，
    /// 如果按<b>列表下标</b>编号，没填类名的卡会因为前面多了两条本体卡而集体改名
    /// （类名一变，卡面素材文件名、存档里的引用全都跟着错位）。本体卡不占号，自有卡永远从 1 开始数。
    /// </summary>
    public string CardClassName(CharacterProfile p, CardSpec c)
    {
        if (IsValidIdentifier(c.ClassName)) return c.ClassName.Trim();
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
