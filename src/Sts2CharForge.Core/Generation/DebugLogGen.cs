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
        string beforeLine = "")
    {
        var sb = new StringBuilder();
        sb.Append(Prefix);
        sb.Append(kindZh).Append('「').Append(display).Append("」(").Append(className).Append(") → ");
        sb.Append(effectText);
        if (!string.IsNullOrWhiteSpace(amountExpr)) sb.Append("；数值=").Append(amountExpr);
        if (!string.IsNullOrWhiteSpace(targetExpr))
        {
            sb.Append("；目标=[").Append(inLoop ? "' + ((" + targetExpr + ")?.Name ?? \"?\") + '" : targetExpr + "?.Name ?? \"?\"");
            if (!inLoop) sb.Append(string.Equals(targetExpr, selfExpr, StringComparison.Ordinal) ? "]（自己）" : "]（不是自己）");
            else sb.Append(']');
        }
        if (beforeLine.Length > 0) sb.Append(beforeLine);
        return sb.ToString();
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
        string beforeLine = "") =>
        LogText(_current?.KindZh ?? "", _current?.Display ?? "", _current?.ClassName ?? "", EffectDisplay(e),
            amountExpr, targetExpr, _current?.SelfExpr ?? "", inLoop, beforeLine);

    // ======================================================================
    // 三个「开关关着就一个字都不写」的发射入口
    // ======================================================================

    /// <summary>效果**对某个目标**动手时那一行（伤害 / 格挡 / 治疗 / 失去生命 … 用它打「实际数值」）。</summary>
    internal static void Target(CodeWriter w, EffectSpec e, string targetExpr, string amountExpr = "", bool inLoop = false)
    {
        if (!On) return;
        w.Line($"Godot.GD.Print({Lit.Str(TextOf(e, amountExpr, targetExpr, inLoop))});   // {Marker}");
    }

    /// <summary>
    /// 同上，但返回**一段可以直接拼在别的语句前面**的文本 —— 给那些「只能拼表达式、拿不到 CodeWriter」
    /// 的发射器用（例：CustomPowerGen 里 EmitFoeBlock 的 lambda）。关着时返回空串。
    /// </summary>
    internal static string TargetInline(EffectSpec e, string targetExpr, string amountExpr = "", bool inLoop = false)
    {
        if (!On) return "";
        return $"Godot.GD.Print({Lit.Str(TextOf(e, amountExpr, targetExpr, inLoop))});   // {Marker} ";
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
        // 「数值=」那一段留空：真正的施加量由下面那个临时变量打（这样日志里不会出现 __dbgAmt1 这种内部名）
        string text = TextOf(e, "", targetExpr, inLoop: false, beforeLine: "；执行前层数=");
        return
            $"{{ decimal {amtVar} = {amtExpr}; " +
            $"PowerModel? {fixVar} = {p}.GetPower<{powerType}>(); " +
            $"Godot.GD.Print({Lit.Str(text)} + ({fixVar} is null ? \"-\" : {fixVar}.Amount.ToString()) + \"（\" + ({fixVar} is null ? \"本来没有这个状态\" : \"已经有了这个状态\") + \"）；施加量=\" + {amtVar}.ToString()); }}   // {Marker}\n";
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
        string text = TextOf(e, "", targetExpr, inLoop: false, beforeLine: "；执行后层数=");
        return
            $"\n{{ PowerModel? __dbgAfter{id} = {p}.GetPower<{powerType}>(); " +
            $"Godot.GD.Print({Lit.Str(text)} + (__dbgAfter{id} is null ? \"-\" : __dbgAfter{id}.Amount.ToString()) + \"（\" + (__dbgAfter{id} is null ? \"现在还是没有\" : \"现在有了\") + \"）\"); }}   // {Marker}";
    }
}
