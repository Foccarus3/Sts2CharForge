using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 【数值】模块里的公式：解析 + 转成生成出来的模组里的 C# 表达式。
///
/// 为什么不直接把用户写的字符串塞进生成代码：
///   · 用户会写错（拼错参数名、括号不配、写了 <c>=</c> 开头），直接塞进 C# 会生成一份**编译不过**的工程，
///     用户看到的是 dotnet 的一堆 CS 错误 —— 所以这里自己先解析一遍，把中文错误说清楚；
///   · <c> / </c> 在 C# 里整数相除会截断，本体数值又都是 decimal，所以统一换成 <c>decimal</c> 运算
///     并把除零夹掉。
///
/// 语法（够用且好写错就有提示）：
///   数字（含小数） / 参数名的字母（M、N…）/ <c>X</c>（当前所有能量）/ <c>Y</c>（当前所有额外资源量）
///   运算符 <c>+ - * / </c>与括号；函数 <c>min(a,b) max(a,b) floor(a) ceil(a) round(a) abs(a)</c>。
///   允许最前面写一个 <c>=</c>（用户习惯：<c>=M*3</c>）。
/// </summary>
public static class ForgeFormula
{
    /// <summary>解析出来的一棵表达式树（叶子 / 一元 / 二元 / 函数调用）。</summary>
    public abstract record Node;

    public sealed record NumberNode(decimal Value) : Node;
    public sealed record VarNode(string Name) : Node;
    public sealed record UnaryNode(char Op, Node Inner) : Node;
    public sealed record BinaryNode(char Op, Node Left, Node Right) : Node;
    public sealed record CallNode(string Func, IReadOnlyList<Node> Args) : Node;

    /// <summary>公式里用到的所有标识符（参数名 / X / Y），去重后按出现顺序返回 —— 用来决定要提前算哪些参数。</summary>
    public static List<string> Identifiers(Node? root)
    {
        var seen = new List<string>();
        void Walk(Node? n)
        {
            switch (n)
            {
                case VarNode v:
                    if (!seen.Contains(v.Name, StringComparer.Ordinal)) seen.Add(v.Name);
                    break;
                case UnaryNode u: Walk(u.Inner); break;
                case BinaryNode b: Walk(b.Left); Walk(b.Right); break;
                case CallNode c: foreach (var a in c.Args) Walk(a); break;
            }
        }
        Walk(root);
        return seen;
    }

    /// <summary>解析公式。失败时返回 null 并把原因写进 <paramref name="error"/>（中文，直接能给用户看）。</summary>
    public static Node? TryParse(string? text, out string error)
    {
        error = "";
        string src = DisplayText(text);
        if (src.Length == 0) { error = "公式是空的（填一个数字，或者写 M*3 这样的式子）。"; return null; }
        try
        {
            var p = new Parser(src);
            Node n = p.ParseExpr();
            p.ExpectEnd();
            return n;
        }
        catch (FormulaError ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// 公式在**卡面上 / 界面上**的写法：去掉首尾空格、去掉用户习惯写在最前面的那个 <c>=</c>。
    /// 卡面描述要原样印出来（「造成 M*3 点伤害」），所以这里的口径必须和解析时完全一致 ——
    /// 实现只有一份，放在 <see cref="EffectSpec.FormulaText"/>（Profile 层，界面也要用）。
    /// </summary>
    public static string DisplayText(string? formula) => EffectSpec.FormulaText(formula);

    private sealed class FormulaError : Exception
    {
        public FormulaError(string message) : base(message) { }
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) { _s = s; }

        private char Cur => _i < _s.Length ? _s[_i] : '\0';
        private void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private bool Take(char c) { SkipWs(); if (Cur != c) return false; _i++; return true; }

        public void ExpectEnd()
        {
            SkipWs();
            if (_i < _s.Length)
            {
                string tail = _s.Substring(_i, Math.Min(12, _s.Length - _i));
                throw new FormulaError($"公式在「{tail}」这里多出来一段，看不懂（只支持 + - * / ( ) 和 min/max 这类函数）。");
            }
        }

        public Node ParseExpr()
        {
            Node left = ParseTerm();
            while (true)
            {
                SkipWs();
                if (Cur == '+') { _i++; left = new BinaryNode('+', left, ParseTerm()); }
                else if (Cur == '-') { _i++; left = new BinaryNode('-', left, ParseTerm()); }
                else return left;
            }
        }

        private Node ParseTerm()
        {
            Node left = ParseUnary();
            while (true)
            {
                SkipWs();
                if (Cur == '*') { _i++; left = new BinaryNode('*', left, ParseUnary()); }
                else if (Cur == '/') { _i++; left = new BinaryNode('/', left, ParseUnary()); }
                else if (Cur == '%') { _i++; left = new BinaryNode('%', left, ParseUnary()); }
                else return left;
            }
        }

        private Node ParseUnary()
        {
            SkipWs();
            if (Cur == '-') { _i++; return new UnaryNode('-', ParseUnary()); }
            if (Cur == '+') { _i++; return ParseUnary(); }
            return ParseAtom();
        }

        private Node ParseAtom()
        {
            SkipWs();
            if (Cur == '(')
            {
                _i++;
                Node inner = ParseExpr();
                if (!Take(')')) throw new FormulaError("括号没配对：有一个 ( 没有对应的 )。");
                return inner;
            }
            if (char.IsDigit(Cur) || Cur == '.')
            {
                int start = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                string num = _s.Substring(start, _i - start);
                if (!decimal.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d))
                    throw new FormulaError($"「{num}」不是一个数字。");
                return new NumberNode(d);
            }
            if (char.IsLetter(Cur) || Cur == '_')
            {
                int start = _i;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
                string name = _s.Substring(start, _i - start);
                SkipWs();
                if (Cur == '(')
                {
                    _i++;
                    var args = new List<Node>();
                    SkipWs();
                    if (Cur != ')')
                    {
                        args.Add(ParseExpr());
                        while (Take(',')) args.Add(ParseExpr());
                    }
                    if (!Take(')')) throw new FormulaError($"函数 {name}( 的括号没配对。");
                    string fn = name.ToLowerInvariant();
                    if (!Functions.Contains(fn))
                        throw new FormulaError($"没有「{name}」这个函数（支持：{string.Join(" / ", Functions)}）。");
                    int want = fn == "min" || fn == "max" ? 2 : 1;
                    if (args.Count != want)
                        throw new FormulaError($"函数 {fn} 要 {want} 个参数，现在给了 {args.Count} 个。");
                    return new CallNode(fn, args);
                }
                // 参数名是「一个字母」；多字母的只认内置只读量（Turn / DiscardedThisTurn…，见 FormulaBuiltins）
                if (name.Length > 1)
                {
                    FormulaBuiltin? builtin = FormulaBuiltins.Find(name);
                    if (builtin is null)
                        throw new FormulaError($"「{name}」不是一个参数，也不是内置只读量。参数名只有一个字母（M、N…）；"
                            + $"想用「当前回合数 / 丢了第几张 / 身上有几个状态」这类实时读数，请用内置名字：{FormulaBuiltins.Names}。");
                    return new VarNode(builtin.Id);
                }
                return new VarNode(name.ToUpperInvariant());
            }
            throw new FormulaError(Cur == '\0'
                ? "公式写到一半就没了（多半是少了一个数字或右括号）。"
                : $"公式里有个看不懂的字符「{Cur}」。");
        }
    }

    private static readonly string[] Functions = { "min", "max", "floor", "ceil", "round", "abs" };

    /// <summary>
    /// 把表达式树翻成生成代码里的 C# 表达式。
    /// <paramref name="resolveVar"/> 负责把标识符换成 C# 片段（参数名 → 提前算好的局部变量；X/Y → 那两个变量）。
    /// 返回 null = 里面有认不出来的标识符（<paramref name="error"/> 里有说明）。
    /// </summary>
    public static string? Emit(Node? root, Func<string, string?> resolveVar, out string error)
    {
        error = "";
        var sb = new StringBuilder();
        if (root is null) { error = "空公式"; return null; }
        if (!EmitNode(root, sb, resolveVar, ref error)) return null;
        return sb.ToString();
    }

    private static bool EmitNode(Node n, StringBuilder sb, Func<string, string?> resolveVar, ref string error)
    {
        switch (n)
        {
            case NumberNode num:
                sb.Append("(").Append(num.Value.ToString("0.############################", CultureInfo.InvariantCulture)).Append("m)");
                return true;
            case VarNode v:
            {
                string? rep = resolveVar(v.Name);
                if (rep is null)
                {
                    error = $"公式里的「{v.Name}」不是一个参数：到「自定义参数 / 组」页加一个 {v.Name}=，"
                          + "或者把公式改掉（X = 当前所有能量，Y = 当前所有额外资源量，这两个不用定义）。";
                    return false;
                }
                sb.Append(rep);
                return true;
            }
            case UnaryNode u:
                sb.Append("-(");
                if (!EmitNode(u.Inner, sb, resolveVar, ref error)) return false;
                sb.Append(")");
                return true;
            case BinaryNode b:
            {
                // 除法 / 取余：分母可能为 0（血量上限还没算出来、能量 0…），所以统一走夹零的辅助函数
                if (b.Op == '/')
                {
                    sb.Append("ForgeFormulaRuntime.Div(");
                    if (!EmitNode(b.Left, sb, resolveVar, ref error)) return false;
                    sb.Append(", ");
                    if (!EmitNode(b.Right, sb, resolveVar, ref error)) return false;
                    sb.Append(")");
                    return true;
                }
                if (b.Op == '%')
                {
                    sb.Append("ForgeFormulaRuntime.Mod(");
                    if (!EmitNode(b.Left, sb, resolveVar, ref error)) return false;
                    sb.Append(", ");
                    if (!EmitNode(b.Right, sb, resolveVar, ref error)) return false;
                    sb.Append(")");
                    return true;
                }
                sb.Append('(');
                if (!EmitNode(b.Left, sb, resolveVar, ref error)) return false;
                sb.Append(' ').Append(b.Op).Append(' ');
                if (!EmitNode(b.Right, sb, resolveVar, ref error)) return false;
                sb.Append(')');
                return true;
            }
            case CallNode c:
            {
                string fn = c.Func switch
                {
                    "min" => "System.Math.Min",
                    "max" => "System.Math.Max",
                    "floor" => "System.Math.Floor",
                    "ceil" => "System.Math.Ceiling",
                    "round" => "System.Math.Round",
                    "abs" => "System.Math.Abs",
                    _ => c.Func,
                };
                sb.Append(fn).Append('(');
                for (int i = 0; i < c.Args.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    if (!EmitNode(c.Args[i], sb, resolveVar, ref error)) return false;
                }
                sb.Append(')');
                return true;
            }
            default:
                error = "认不出来的公式";
                return false;
        }
    }

    /// <summary>
    /// 「条件数值」用的解析器：**一切内联**，不依赖任何提前算好的局部变量。
    ///
    /// 为什么条件和别处不同：条件会被写到 <c>IsPlayable</c> / <c>ShouldGlowGoldInternal</c>
    /// 这两个**表达式体属性**里（那里面没法先声明一堆局部变量），所以：
    ///   · 内置只读量 → 直接展开成 <c>base.Owner</c> / <c>base.Owner.Creature</c> 的表达式；
    ///   · <c>X</c> / <c>Y</c> → <c>ForgeFormulaRuntime.Energy/Stars(base.Owner)</c>；
    ///   · 自定义参数 → 把 <see cref="ParamExpr"/> 里的三个局部变量名文本替换掉
    ///     （<c>__fgPlayer</c> → <c>base.Owner</c>、<c>__fgSelf</c> → <c>base.Owner.Creature</c>、
    ///      <c>__fgTarget</c> → <paramref name="playTargetExpr"/>，传 null 表示这个场合没有「打出的目标」）。
    /// </summary>
    public static Func<string, string?> ConditionResolver(CharacterProfile p, string? playTargetExpr) => id => id switch
    {
        "X" => "ForgeFormulaRuntime.Energy(base.Owner)",
        "Y" => "ForgeFormulaRuntime.Stars(base.Owner)",
        _ => FormulaBuiltins.Find(id) is { } builtin
            ? builtin.Expr("base.Owner", "base.Owner.Creature")
            : (p.CustomParams.FirstOrDefault(x => x is not null
                    && string.Equals(ParamNames.Normalize(x.Name), id, StringComparison.Ordinal)) is { } param
                ? InlineParam(param, playTargetExpr)
                : null),
    };

    /// <summary>把参数表达式里的局部变量替换成内联写法（见 <see cref="ConditionResolver"/>）。</summary>
    private static string InlineParam(ParamSpec p, string? playTargetExpr)
    {
        string expr = ParamExpr(p);
        expr = expr.Replace("__fgSelf", "base.Owner.Creature", StringComparison.Ordinal)
                   .Replace("__fgPlayer", "base.Owner", StringComparison.Ordinal);
        if (expr.Contains("__fgTarget", StringComparison.Ordinal))
            expr = expr.Replace("__fgTarget", playTargetExpr ?? "null", StringComparison.Ordinal);
        return expr;
    }


    /// <summary>
    /// 卡牌（以及锻造那套布）里的标识符解析：<c>X</c> / <c>Y</c> → 生成端提前算好的局部变量；
    /// 内置只读量（<c>Turn</c> / <c>DiscardedThisTurn</c>…）→ 现算的 C# 表达式；
    /// 其余单字母 → 「自定义参数 / 组」页里定义的那个参数（提前算成 <c>__pM</c>）。
    ///
    /// 内置量写成 <c>__fgPlayer</c> / <c>__fgSelf</c> 的表达式，所以生成端的「用到才声明」那套
    /// （见 <see cref="ForgeGraphGen.EmitCardFormulaPrologue"/>）会自动把那两个变量补上 —— 不用另改。
    /// </summary>
    public static Func<string, string?> CardResolver(CharacterProfile p) => id => id switch
    {
        "X" => "__X",
        "Y" => "__Y",
        _ => FormulaBuiltins.Find(id) is { } builtin
            ? builtin.Expr("__fgPlayer", "__fgSelf")
            : (p.CustomParams.Any(x => x is not null && string.Equals(ParamNames.Normalize(x.Name), id, StringComparison.Ordinal))
                ? ParamVar(id)
                : null),
    };

    /// <summary>
    /// 一个「参数引用」在生成代码里的写法（参数名 → C# 片段）。
    /// 生成端会先把用到的参数算成局部变量 <c>__pM</c>，所以这里返回的就是那个变量名。
    /// </summary>
    public static string ParamVar(string letter) => "__p" + ParamNames.Normalize(letter);

    /// <summary>
    /// 把一条自定义参数翻成运行时求值的 C# 表达式（**打出牌的那一刻现算**）。
    /// 需求里点名的几种：自己最大生命 / 指定敌人的易伤层数 / 金币数 / 能量数…
    /// </summary>
    public static string ParamExpr(ParamSpec p)
    {
        string src = ParamSources.Normalize(p.Source);
        // 用户要求：参数页也能监听那些「实时读数」（回合数 / 丢弃·消耗·生成·复制张数 / 拿到的资源量 /
        // 身上状态数…）—— 它们就是公式内置量那张表（界面 / 生成 / 回读同一份），直接借它的表达式。
        if (FormulaBuiltins.Find(src) is { } builtin)
            return builtin.Expr("__fgPlayer", "__fgSelf");
        string target = TargetExpr(p.Target);
        return src switch
        {
            ParamSources.CurrentHp => $"{target}?.CurrentHp ?? 0",
            ParamSources.MaxHp => $"{target}?.MaxHp ?? 0",
            ParamSources.Gold => "(decimal)(__fgPlayer?.Gold ?? 0)",
            ParamSources.Energy => "(__fgPlayer?.Energy ?? 0m)",
            ParamSources.MaxEnergy => "(__fgPlayer?.MaxEnergy ?? 0m)",
            ParamSources.Power => $"ForgeFormulaRuntime.PowerAmount({target}, \"{p.PowerId ?? ""}\")",
            _ => p.Value.ToString("0.############################", CultureInfo.InvariantCulture) + "m",
        };
    }

    /// <summary>
    /// 「从谁身上取」那个对象在生成代码里的写法。
    /// 说明：<c>__fgSelf</c> / <c>__fgTarget</c> 是生成端在每个执行点先算好的两个变量
    /// （自己 / 这一支链当前的目标），所以参数表达式不需要自己再去找目标。
    /// </summary>
    public static string TargetExpr(string? target) => ParamTargets.Normalize(target) switch
    {
        ParamTargets.ChosenEnemy => "__fgTarget",
        ParamTargets.RandomEnemy => "ForgeFormulaRuntime.RandomEnemy(__fgPlayer)",
        ParamTargets.AllEnemies => "ForgeFormulaRuntime.FirstEnemy(__fgPlayer)",
        ParamTargets.AllAllies => "__fgSelf",
        ParamTargets.Summon => "ForgeFormulaRuntime.FirstSummon(__fgPlayer)",
        _ => "__fgSelf",
    };

    /// <summary>这条公式用到的参数里，有没有「要用到目标 / 玩家」的（生成端据此决定要不要先算那两个变量）。</summary>
    public static bool NeedsTarget(IEnumerable<ParamSpec> usedParams) =>
        usedParams.Any(p => ParamSources.Normalize(p.Source) is
            ParamSources.CurrentHp or ParamSources.MaxHp or ParamSources.Power
            || ParamTargets.Normalize(p.Target) != ParamTargets.Self
            || ParamSources.Normalize(p.Source) is ParamSources.Gold or ParamSources.Energy or ParamSources.MaxEnergy);
}
