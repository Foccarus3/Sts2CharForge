using System.Text;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 存档里那个「生成调试日志」开关（<see cref="CharacterProfile.DebugLog"/>）的**唯一实现处**。
///
/// ===== 它是干什么的 =====
/// 用户在游戏里遇到「填了 -2 力量却不生效」这类**运行期**问题时，游戏日志里既没有异常、
/// 也不记录「数值前后」和「打的是哪张牌」—— 根本没法查。打开这个开关后，生成出来的模组会在
/// 每条效果真正执行的地方打一行 <c>Godot.GD.Print</c>（本体日志里就是 <c>[INFO] …</c>），写明：
/// 是谁在跑（哪张牌 / 哪只遗物 / 哪瓶药水 / 哪个自定义状态 + 类名）、效果种类、目标是谁
/// （<c>Creature.Name</c> + 是不是自己）、**执行前 / 执行后的数值**。
///
/// ===== 为什么 ApplyPower 要「执行前 + 执行后」两行 =====
/// 这正是加这个开关的目的：要区分**目标身上本来没有这个状态**（<c>GetPower&lt;T&gt;()</c> 是 null，
/// 层数记 <c>-</c>）与**已经有了**（层数记真实值）两种情形 ——
/// V0.2.139 那个「负值力量不生效」的 bug 之所以难查，就是因为日志里看不出这两种情形。
///
/// ===== 硬要求（红线①）=====
/// **开关关着时生成结果必须与加开关之前逐字节一致**。所以这里每个方法第一步都问
/// <see cref="On"/>（它读的就是存档里那个字段），关着时**一个字都不写**；
/// 开关开着时也**只往前后另加行 / 另加片段**，原本那几条语句一个字符都不改
/// （<see cref="PowerBefore"/> / <see cref="PowerAfter"/> 返回的都是「拼接片段」）。
///
/// ===== 回读（ProjectRecovery）=====
/// 每条生成的日志语句都带 <c>// CET:DebugLog</c> 注释 —— 从工程反推存档时那几行会被跳过
/// （见 <c>ProjectRecovery</c> 里的 <c>CET:DebugLog</c> 跳过规则）。开关默认关着，
/// 老工程里一行都没有，反推结果一个字节都不变。
/// </summary>
internal static class DebugLogGen
{
    /// <summary>生成的日志行统一带这个词（回读靠它把这几行认出来跳过）。</summary>
    internal const string Marker = "CET:DebugLog";

    /// <summary>日志行前缀（用户在游戏日志里按它搜）。</summary>
    internal const string Prefix = "[模组调试] ";

    private static readonly string PrefixLit = Lit.Str(Prefix);

    /// <summary>
    /// 「这条日志用的临时变量」的序号。
    ///
    /// 为什么要它：同一条链（一张牌的 OnPlay 里、一个状态钩子里）可以有**多条**施加状态的效果，
    /// 每条都要「执行前」那个临时变量 —— 同名会在同一个作用域里 CS0136 / CS0128，整个模组编译不过。
    /// 用生成期递增的序号保证唯一。
    ///
    /// ⚠ <c>[ThreadStatic]</c>：自检里会很多次单独生成一张牌（直接调 CardSource，不过 ModGenerator），
    /// 共享一个静态字段在并行时会串号；而且**每次开始生成一个源文件时由 <see cref="Reset"/> 归零** ——
    /// 「同一份存档生成两次文本逐字节相同」是既有红线，序号必须每次从头开始。
    /// </summary>
    [ThreadStatic] private static int _seq;

    private static int NextSeq() => ++_seq;

    /// <summary>开始生成一个新的 C# 源文件时把序号归零（每个 Source 生成器入口调一次）。</summary>
    internal static void Reset() => _seq = 0;

    /// <summary>这份存档打开「生成调试日志」了吗（关着 / 没有 profile 一律 false）。</summary>
    internal static bool Enabled(CharacterProfile? p) => p is { DebugLog: true };

    /// <summary>
    /// 现在正在生成「哪一个来源」—— 用 <c>using</c> 包住那一段发射代码，
    /// 深处的发射器（<c>EmitCardEffect</c> 等）就不用再多收一串新参数。
    /// </summary>
    internal sealed class SourceContext : IDisposable
    {
        private readonly SourceContext? _prev;

        internal SourceContext(CharacterProfile? p, string kindZh, string display, string className, string selfExpr)
        {
            Profile = p;
            KindZh = kindZh;
            Display = display;
            ClassName = className;
            SelfExpr = selfExpr;
            _prev = _current;
            _current = this;
        }

        internal CharacterProfile? Profile { get; }
        internal string KindZh { get; }
        internal string Display { get; }
        internal string ClassName { get; }

        /// <summary>「自己」那个生物的表达式（卡牌 / 遗物 = <c>base.Owner.Creature</c>，自定义状态 = <c>base.Owner</c>）。</summary>
        internal string SelfExpr { get; }

        public void Dispose() => _current = _prev;
    }

    [ThreadStatic] private static SourceContext? _current;

    /// <summary>开始生成「哪个来源」的一段（配 <c>using</c> 用）。</summary>
    internal static SourceContext Begin(CharacterProfile? p, string kindZh, string display, string className, string selfExpr) =>
        new(p, kindZh, display, className, selfExpr);

    /// <summary>现在这一段开没开调试日志（关着 / 不在任何来源作用域里都 false）。</summary>
    internal static bool On => Enabled(_current?.Profile);

    // ======================================================================
    // 日志「原文」那几段（自检直接断言这几段；发射时把它们包成语句）
    // ======================================================================

    /// <summary>
    /// 「谁在跑」那一段的**原文**，例：<c>[模组调试] 卡牌「尖啸」(PiercingWail) → 施加增益/减益（状态 StrengthPower）；目标=['铁甲战士']（不是自己）；执行前层数=2（已经有了这个状态）</c>。
    ///
    /// 日志原文里**只出现一次**那个前缀（由这里统一拼），调用方别再自己加。
    /// </summary>
    internal static string LogText(string kindZh, string display, string className, string effectText,
        string amountExpr, string targetExpr = "", string selfExpr = "", bool inLoop = false,
        string beforeLine = "", string groupZh = "")
    {
        var sb = new StringBuilder();
        sb.Append(Prefix);
        sb.Append(kindZh).Append('「').Append(display).Append("」(").Append(className).Append(") → ");
        sb.Append(effectText);
        if (!string.IsNullOrWhiteSpace(amountExpr)) sb.Append("；数值=").Append(amountExpr);
        // V0.2.234：「全部友方 / 全体单位」这类效果是**逐个动手**的，打印点在 foreach **外面**（见
        // CSharpCodeGen.EmitPerCreature），不能引用循环变量 `ally` / `unit`（否则 CS0103：用户实测
        // 「生成调试日志」一开，VitaCard2x.cs(48,160): error CS0103: 当前上下文中不存在名称"ally"，
        // 整个模组编译不过）。这几档改成把「打谁」写成一句固定说明。
        if (!string.IsNullOrWhiteSpace(groupZh))
        {
            sb.Append("；目标=[").Append(groupZh).Append(']');
        }
        // ⚠ 「逐个动手」（inLoop）时这一段**不写进日志原文**：那时目标名要按运行时打
        //（`"…目标=[" + ((__fgFoe)?.Name ?? "?") + "]"`），拼在字符串里会被 Lit.Str 转义成
        // 一串字面量代码（用户实测的日志里打出来就是 `目标=[' + ((x)?.Name ?? \"?\") + ']`）。
        // 调用方（Target / TargetInline）会把 LoopTargetCode 拼在后面。
        else if (!string.IsNullOrWhiteSpace(targetExpr) && !inLoop)
        {
            sb.Append("；目标=[").Append(targetExpr).Append("?.Name ?? \"?\"");
            sb.Append(string.Equals(targetExpr, selfExpr, StringComparison.Ordinal) ? "]（自己）" : "]（不是自己）");
        }
        if (beforeLine.Length > 0) sb.Append(beforeLine);
        return sb.ToString();
    }

    /// <summary>
    /// 「；目标=[运行时名字]」那一段 —— **C# 代码**（不是日志原文的一部分）。
    ///
    /// 给「逐个动手」（foreach / for 里那几个目标）用：那时目标是谁只有运行时才知道，所以这段要拼在
    /// <c>Lit.Str(日志原文)</c> **后面**一起交给 <c>Godot.GD.Print(...)</c>，
    /// **不能**塞进日志原文里（塞进去会被 Lit.Str 转义成一串字面量代码：用户实测的日志里
    /// 打出来就是 <c>目标=[' + ((x)?.Name ?? \"?\") + ']</c>，看不到真正打到谁）。
    /// </summary>
    private static string TargetCode(string targetExpr, string selfExpr)
    {
        string name = "((" + targetExpr + ")?.Name ?? \"?\")";
        string suffix = string.Equals(targetExpr, selfExpr, StringComparison.Ordinal) ? "（自己）" : "（不是自己）";
        return "\"；目标=[\" + " + name + " + \"]" + suffix + "\"";
    }

    /// <summary>
    /// 效果种类那段日志原文：种类中文名 + 状态类名。负数档要说清是**哪一档**——
    /// 「施加负层数」（这个状态本体允许负层数，例：力量）还是「扣除已有层数」（不允许负层数，例：虚弱）——
    /// 这两档在游戏里的行为完全不同，排查时必须一眼看出来（判据见 <c>EffectCatalog.AllowsNegativeLayers</c>）。
    /// </summary>
    internal static string EffectDisplay(EffectSpec e)
    {
        var sb = new StringBuilder(EffectCatalog.FindKind(e.Kind).Display);
        if (!string.IsNullOrWhiteSpace(e.PowerId)) sb.Append("（状态 ").Append(e.PowerId).Append('）');
        if (e.Kind is "ApplyPower" or "TempPower" && CSharpCodeGen.IsNegativeAmount(e, useX: false))
            sb.Append(EffectCatalog.AllowsNegativeLayers(e.PowerId)
                ? "（实际按「施加负层数」执行：这个状态本体允许负层数，本体「尖啸」就是这么写的）"
                : "（实际按「扣除已有层数」执行：这个状态本体不允许负层数，身上没有时什么都不做）");
        return sb.ToString();
    }

    /// <summary>
    /// 「谁在跑 …」那一段（用**当前来源作用域**里的卡牌 / 遗物 … 名字 + 这条效果的实际正负档）。
    /// </summary>
    private static string TextOf(EffectSpec e, string amountExpr, string targetExpr = "", bool inLoop = false,
        string beforeLine = "", string groupZh = "") =>
        LogText(_current?.KindZh ?? "", _current?.Display ?? "", _current?.ClassName ?? "", EffectDisplay(e),
            amountExpr, targetExpr, _current?.SelfExpr ?? "", inLoop, beforeLine, groupZh);

    // ======================================================================
    // 三个「开关关着就一个字都不写」的发射入口
    // ======================================================================

    /// <summary>
    /// 效果**对某个目标**动手时那一行（伤害 / 格挡 / 治疗 / 失去生命 … 用它打「实际数值」）。
    /// <paramref name="groupZh"/> 非空 = 「全部友方 / 全体单位」那种逐个动手的档：打印点在循环外面，
    /// 目标改成一句固定说明（不能引用循环变量，见 <see cref="LogText"/>）。
    /// </summary>
    internal static void Target(CodeWriter w, EffectSpec e, string targetExpr, string amountExpr = "", bool inLoop = false,
        string groupZh = "")
    {
        if (!On) return;
        w.Line(PrintStatement(e, targetExpr, amountExpr, inLoop, groupZh) + $"   // {Marker}");
    }

    /// <summary>
    /// 同上，但返回**一段可以直接拼在别的语句前面**的文本 —— 给那些「只能拼表达式、拿不到 CodeWriter」
    /// 的发射器用（例：CustomPowerGen 里 EmitFoeBlock 的 lambda）。关着时返回空串。
    ///
    /// ⚠ **末尾必须带一个换行**（V0.2.168 修，用户实测）：这一段是以行尾注释 <c>// CET:DebugLog</c> 收尾的，
    /// 紧跟着拼下一条语句时，那行注释会把**后面整条语句**（乃至 <c>foreach</c> 收尾的 <c>}</c>）
    /// 一起吃成注释 —— 用户那张卡就是这么变成
    /// <c>CS8641 "else" 不能用在语句的开头</c> + <c>CS1513 应输入 }</c> 的（整个工程编译不过）。
    /// 换成带换行的写法后：日志自己占一行（「从工程反推存档」按行跳过，见 <see cref="PowerBefore"/> 的说明），
    /// 后面那条语句照旧原样执行。
    /// </summary>
    internal static string TargetInline(EffectSpec e, string targetExpr, string amountExpr = "", bool inLoop = false,
        string groupZh = "")
    {
        if (!On) return "";
        return PrintStatement(e, targetExpr, amountExpr, inLoop, groupZh) + $"   // {Marker}\n";
    }

    /// <summary>
    /// 「这一次是第几次打出」那一行（V0.2.236）。
    ///
    /// 用户报「投斧 对某张牌不生效」时最难判的就是这一点：本体 <c>CardModel.OnPlayWrapper</c> 是
    /// <c>for (int i = 0; i &lt; playCount; i++) await OnPlay(...)</c> —— 多打的那一次**会再跑一遍 OnPlay**，
    /// 但界面上（尤其是能力牌，打出后牌就去 <c>PileType.None</c>）常常看不出来。日志里写清「第 2/2 次」
    /// 就能一眼分清两种情形：只有「第 1/1 次」= 遗物/状态压根没改 playCount（例：投斧每场战斗只加成
    /// **第一张**打出的牌，Already used）；出现「第 2/2 次」= 多打的那次真的执行了，
    /// 那么「没效果」只可能是效果本身不可叠加（单一实例的状态第二层看不出来）。
    ///
    /// 只读 <c>cardPlay.PlayIndex</c> / <c>cardPlay.PlayCount</c>（本体 CardModel.cs:1915-1925 赋的值），
    /// 不改任何状态；开关关着时一个字都不写（红线①）。
    /// </summary>
    internal static void PlayCount(CodeWriter w)
    {
        if (!On) return;
        string head = Lit.Str(Prefix + (_current?.KindZh ?? "") + "「" + (_current?.Display ?? "") + "」("
            + (_current?.ClassName ?? "") + ") → 打出第 ") ;
        w.Line($"Godot.GD.Print({head} + (cardPlay.PlayIndex + 1).ToString() + \"/\" + cardPlay.PlayCount.ToString() + \" 次\");   // {Marker}");
    }

    /// <summary>
    /// 一条完整的 <c>Godot.GD.Print(…);</c> 语句（**不含**行尾的 <c>// CET:DebugLog</c> 标记）。
    ///
    /// 「逐个动手」（<paramref name="inLoop"/>）时目标名拼在字符串**后面**按运行时打
    /// （<see cref="LoopTargetCode"/>）—— 塞进日志原文里会被 Lit.Str 转义成一串字面量代码。
    /// </summary>
    private static string PrintStatement(EffectSpec e, string targetExpr, string amountExpr, bool inLoop,
        string groupZh = "")
    {
        string head = Lit.Str(TextOf(e, amountExpr, targetExpr, inLoop: true, groupZh: groupZh));
        if (!string.IsNullOrWhiteSpace(groupZh)) return $"Godot.GD.Print({head});";
        if (!inLoop || string.IsNullOrWhiteSpace(targetExpr)) return $"Godot.GD.Print({head});";
        return $"Godot.GD.Print({head} + {TargetCode(targetExpr, _current?.SelfExpr ?? "")});";
    }

    // ======================================================================
    // ApplyPower：执行前 / 执行后各一行（**本开关的重点**）
    // ======================================================================

    /// <summary>
    /// 「执行前」那一行 + 记下执行前的层数。返回**追加在原来那条语句前面**的片段
    /// （原来是空字符串时，这个片段就是那条效果的整段代码）。
    ///
    /// 打印的层数：目标身上**本来没有**这个状态时记 <c>-</c> 并另标一句「本来没有这个状态」，
    /// 已经有则是那个数字 —— 这两种情形正是要区分的。
    /// 「施加量」打的是**真实生效的那个数**（生成出来的表达式先赋给一个临时变量，再把它 ToString 出来）。
    ///
    /// 形状：<c>{ 记层数 + 打第一行 }</c> —— 用一对花括号把这一行**单独关起来**，原因是
    /// 「从工程反推存档」是按行认效果的：日志行必须自成一行的 `// CET:DebugLog` 才跳得掉，
    /// 挂在原来那条语句行尾就会把它整行一起跳过（丢效果）。
    /// </summary>
    internal static string PowerBefore(EffectSpec e, string targetExpr, string powerType, string amtExpr)
    {
        if (!On) return "";
        int id = NextSeq();
        string amtVar = "__dbgAmt" + id;
        string fixVar = "__dbgFix" + id;
        string p = "(" + targetExpr + ")";
        // 「数值=」那一段留空：真正的施加量由下面那个临时变量打（这样日志里不会出现 __dbgAmt1 这种内部名）。
        // 目标名拼在字符串**外面**（TargetCode）—— 逐个动手时它只有运行时才知道（见那里的说明）。
        string text = TextOf(e, "", targetExpr, inLoop: true, beforeLine: "");
        return
            $"{{ decimal {amtVar} = {amtExpr}; " +
            $"PowerModel? {fixVar} = {p}.GetPower<{powerType}>(); " +
            $"Godot.GD.Print({Lit.Str(text)} + {TargetCode(targetExpr, _current?.SelfExpr ?? "")} + \"；执行前层数=\" + ({fixVar} is null ? \"-\" : {fixVar}.Amount.ToString()) + \"（\" + ({fixVar} is null ? \"本来没有这个状态\" : \"已经有了这个状态\") + \"）；施加量=\" + {amtVar}.ToString()); }}   // {Marker}\n";
    }

    /// <summary>
    /// 「执行后」那一行。返回**追加在原来那条语句后面**的片段（原来那条语句一个字符都不改）。
    /// 和「执行前」一样自成一个代码块 + 单独一行 <c>// CET:DebugLog</c>（回读按行跳过，见 <see cref="PowerBefore"/>）。
    /// </summary>
    internal static string PowerAfter(EffectSpec e, string targetExpr, string powerType)
    {
        if (!On) return "";
        int id = _seq;                       // 紧跟在 PowerBefore 后面调用，用同一个序号
        string p = "(" + targetExpr + ")";
        string text = TextOf(e, "", targetExpr, inLoop: true, beforeLine: "");
        return
            $"\n{{ PowerModel? __dbgAfter{id} = {p}.GetPower<{powerType}>(); " +
            $"Godot.GD.Print({Lit.Str(text)} + {TargetCode(targetExpr, _current?.SelfExpr ?? "")} + \"；执行后层数=\" + (__dbgAfter{id} is null ? \"-\" : __dbgAfter{id}.Amount.ToString()) + \"（\" + (__dbgAfter{id} is null ? \"现在还是没有\" : \"现在有了\") + \"）\"); }}   // {Marker}\n";
    }
}
