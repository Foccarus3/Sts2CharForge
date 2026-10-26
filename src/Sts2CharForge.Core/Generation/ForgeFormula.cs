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
///   运算符 <c>+ - * / %</c>与括号；函数 <c>min(a,b) max(a,b) floor(a) ceil(a) round(a) abs(a)</c>。
///   允许最前面写一个 <c>=</c>（用户习惯：<c>=M*3</c>）。
///
/// V0.2.196（用户要求「能不能让生成器使用上述」）：**条件也能写进公式**了 ——
///   · 比较：<c>&gt; &lt; &gt;= &lt;= == !=</c>；
///   · 逻辑：<c>&amp;&amp; || !</c>（数字当条件用 = 「不等于 0」）；
///   · 三元：<c>cond ? a : b</c>（可嵌套，例：符号函数 <c>x &gt; 0 ? 1 : x &lt; 0 ? -1 : 0</c>）；
///   · **布尔当数值自动「真取 1、假取 0」**：<c>(a &gt; 0) * 5</c> 生成 <c>((a &gt; 0) ? 1m : 0m) * 5m</c>，
///     所以「三个数里有几个是正数」「及格就加 100」这类加权写法直接能写；
///   · 也认 C# 那套写法：<c>Math.Abs(x)</c> / <c>Math.Max(1, N/2)</c>、<c>Convert.ToInt32(条件)</c>、
///     字面量 <c>true</c> / <c>false</c>（= 1 / 0）。
///   老公式（只有四则运算 / 那几个函数）生成出来的文本**一个字符都不变**。
/// </summary>
public static class ForgeFormula
{
    /// <summary>解析出来的一棵表达式树（叶子 / 一元 / 二元 / 函数调用 / 比较 / 逻辑 / 三元）。</summary>
    public abstract record Node;

    public sealed record NumberNode(decimal Value) : Node;
    public sealed record VarNode(string Name) : Node;
    public sealed record UnaryNode(char Op, Node Inner) : Node;
    public sealed record BinaryNode(char Op, Node Left, Node Right) : Node;
    public sealed record CallNode(string Func, IReadOnlyList<Node> Args) : Node;
    /// <summary>比较：<c>L op R</c>，op = &gt; &lt; &gt;= &lt;= == !=（结果是布尔）。</summary>
    public sealed record CompareNode(string Op, Node Left, Node Right) : Node;
    /// <summary>逻辑：<c>L op R</c>，op = &amp;&amp; ||（结果是布尔）。</summary>
    public sealed record LogicNode(string Op, Node Left, Node Right) : Node;
    /// <summary>取反：<c>!inner</c>（结果是布尔）。</summary>
    public sealed record NotNode(Node Inner) : Node;
    /// <summary>三元：<c>cond ? then : otherwise</c>（两支都是布尔时整个也是布尔）。</summary>
    public sealed record TernaryNode(Node Cond, Node Then, Node Else) : Node;

    /// <summary>
    /// 这个节点算出来的**是不是布尔**（比较 / 逻辑 / 取反；三元看两支）。
    /// 布尔被当数值用时一律按「真取 1、假取 0」生成（见 <see cref="EmitNode"/>）。
    /// </summary>
    public static bool IsBool(Node n) => n switch
    {
        CompareNode or LogicNode or NotNode => true,
        CallNode c => IsBoolFunc(c.Func),
        TernaryNode t => IsBool(t.Then) && IsBool(t.Else),
        _ => false,
    };

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
                case CompareNode c: Walk(c.Left); Walk(c.Right); break;
                case LogicNode l: Walk(l.Left); Walk(l.Right); break;
                case NotNode t: Walk(t.Inner); break;
                case TernaryNode t: Walk(t.Cond); Walk(t.Then); Walk(t.Else); break;
            }
        }
        Walk(root);
        return seen;
    }

    /// <summary>
    /// 公式原文里是不是**真的用到了**这个标识符（按标识符边界匹配）。
    ///
    /// <para>为什么不直接 <c>Contains</c>：<c>X</c> 这种单字母会被 <c>XSpent</c> / <c>X2</c> 命中，
    /// 而 <c>ThisCardDamage</c> 也会被别的名字的一部分命中 —— 用正则的前后「不是字母数字下划线」
    /// 做边界（<c>\b</c> 不行：它会把 <c>XSpent</c> 开头的 X 也算上）。
    /// 生成端拿它判断「这张牌要不要生成那个字段 / 要不要在每条伤害后面记账」，
    /// 校验器拿它判断「这条读数被用在了不该用的链上」。</para>
    ///
    /// <para>对**解析得动**的公式会以解析结果为准（标识符表），解析不动时退回文本匹配 ——
    /// 校验器随后会报「公式写错了」，这里不重复报。</para>
    /// </summary>
    public static bool MentionsIdentifier(string? formula, string id)
    {
        if (string.IsNullOrWhiteSpace(formula)) return false;
        if (TryParse(formula, out _) is { } root
            && Identifiers(root).Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)))
            return true;
        return System.Text.RegularExpressions.Regex.IsMatch(formula,
            $@"(?<![A-Za-z0-9_]){System.Text.RegularExpressions.Regex.Escape(id)}(?![A-Za-z0-9_])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
        private char Peek(int k) => _i + k < _s.Length ? _s[_i + k] : '\0';
        private void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private bool Take(char c) { SkipWs(); if (Cur != c) return false; _i++; return true; }

        public void ExpectEnd()
        {
            SkipWs();
            if (_i < _s.Length)
            {
                string tail = _s.Substring(_i, Math.Min(12, _s.Length - _i));
                throw new FormulaError($"公式在「{tail}」这里多出来一段，看不懂（支持 + - * / % 四则运算、括号、"
                    + "比较 > < >= <= == !=、逻辑 && || !、三元 cond ? a : b，以及 min/max/floor/ceil/round/abs 这类函数）。");
            }
        }

        /// <summary>一个公式（三元是最低优先级：<c>cond ? a : b</c>）。</summary>
        public Node ParseExpr() => ParseTernary();

        private Node ParseTernary()
        {
            Node cond = ParseOr();
            SkipWs();
            if (Cur != '?') return cond;
            _i++;
            Node then = ParseTernary();
            if (!Take(':'))
                throw new FormulaError("三元运算少了一个冒号：写成「条件 ? 成立时 : 不成立时」（例：a &gt; 0 ? 1 : 0）。");
            Node otherwise = ParseTernary();
            return new TernaryNode(cond, then, otherwise);
        }

        private Node ParseOr()
        {
            Node left = ParseAnd();
            while (true)
            {
                SkipWs();
                if (Cur == '|' && Peek(1) == '|') { _i += 2; left = new LogicNode("||", left, ParseAnd()); }
                else return left;
            }
        }

        private Node ParseAnd()
        {
            Node left = ParseEquality();
            while (true)
            {
                SkipWs();
                if (Cur == '&' && Peek(1) == '&') { _i += 2; left = new LogicNode("&&", left, ParseEquality()); }
                else return left;
            }
        }

        private Node ParseEquality()
        {
            Node left = ParseRelational();
            while (true)
            {
                SkipWs();
                if (Cur == '=' && Peek(1) == '=') { _i += 2; left = new CompareNode("==", left, ParseRelational()); }
                else if (Cur == '!' && Peek(1) == '=') { _i += 2; left = new CompareNode("!=", left, ParseRelational()); }
                else return left;
            }
        }

        private Node ParseRelational()
        {
            Node left = ParseAdditive();
            while (true)
            {
                SkipWs();
                if (Cur == '>')
                {
                    if (Peek(1) == '=') { _i += 2; left = new CompareNode(">=", left, ParseAdditive()); }
                    else { _i++; left = new CompareNode(">", left, ParseAdditive()); }
                }
                else if (Cur == '<')
                {
                    if (Peek(1) == '=') { _i += 2; left = new CompareNode("<=", left, ParseAdditive()); }
                    else { _i++; left = new CompareNode("<", left, ParseAdditive()); }
                }
                else return left;
            }
        }

        private Node ParseAdditive()
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
            if (Cur == '!') { _i++; return new NotNode(ParseUnary()); }
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
                // V0.2.196/197：认 C# 那套写法 —— Math.Abs(x) / Math.Pow(2,3) / Math.PI / double.IsNaN(x) /
                // Convert.ToInt32(条件)。用户会照着 C# 写：剥掉「类型.」前缀之后就是我们自己的名字。
                bool typed = string.Equals(name, "Math", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "MathF", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Convert", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "double", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "decimal", StringComparison.OrdinalIgnoreCase);
                if (typed && Cur == '.')
                {
                    _i++;
                    SkipWs();
                    if (!(char.IsLetter(Cur) || Cur == '_'))
                        throw new FormulaError($"{name}. 后面要跟一个名字（例：{name}.Abs(x) / {name}.PI）。");
                    int st2 = _i;
                    while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
                    name = _s.Substring(st2, _i - st2);
                    SkipWs();
                }
                // 常量：Math.PI / Math.E / Math.Tau（MathF 那两个照同值认 —— 本工具是 decimal 运算，没有单精度这一说）
                if (!typed || Cur != '(')
                {
                    string upper = name.ToUpperInvariant();
                    if (typed && upper is "PI" or "TAU" or "E")
                    {
                        const string pi = "3.14159265358979323846264338327950288419716939937510";
                        const string e = "2.71828182845904523536028747135266249775724709369995";
                        return new NumberNode(decimal.Parse(upper switch
                        {
                            "PI" => pi,
                            "TAU" => "6.28318530717958647692528676655900576839433879875020",
                            _ => e,
                        }, NumberStyles.Float, CultureInfo.InvariantCulture));
                    }
                }
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
                    // 用户习惯把「转成整数」写成 Convert.ToInt32 —— 合到 toint 那一档
                    if (fn == "toint32") fn = "toint";
                    // Math.Round(x, MidpointRounding.AwayFromZero)：第二个参数是一个枚举名，不是数值。
                    // 本工具不需要枚举：认成「传统四舍五入」那一档（roundaway），ToEven 就是默认的 round。
                    if (fn == "round" && args.Count == 2)
                    {
                        if (args[1] is VarNode { Name: "$AWAYFROMZERO" }) return new CallNode("roundaway", new[] { args[0] });
                        if (args[1] is VarNode { Name: "$TOEVEN" }) return new CallNode("round", new[] { args[0] });
                    }
                    if (!Fns.ContainsKey(fn))
                        throw new FormulaError($"没有「{name}」这个函数（支持：{string.Join(" / ", Fns.Keys)}）。"
                            + "常量可以直接写 Math.PI / Math.E / Math.Tau。");
                    var spec0 = Fns[fn];
                    if (args.Count < spec0.MinArgs || args.Count > spec0.MaxArgs)
                        throw new FormulaError($"函数 {fn} 要 {spec0.ArityText} 个参数，现在给了 {args.Count} 个。");
                    return new CallNode(fn, args);
                }
                // true / false 字面量（用户会照着 C# 写）：真取 1、假取 0，和「布尔当数值」同一套口径
                if (string.Equals(name, "true", StringComparison.OrdinalIgnoreCase)) return new NumberNode(1m);
                if (string.Equals(name, "false", StringComparison.OrdinalIgnoreCase)) return new NumberNode(0m);
                // MidpointRounding 的两个枚举名：只有 Math.Round(x, AwayFromZero) 那一处会用得到（见上面）
                if (string.Equals(name, "AwayFromZero", StringComparison.OrdinalIgnoreCase)) return new VarNode("$AWAYFROMZERO");
                if (string.Equals(name, "ToEven", StringComparison.OrdinalIgnoreCase)) return new VarNode("$TOEVEN");
                // 参数名是「一个字母」；多字母的只认内置只读量（Turn / DiscardedThisTurn…，见 FormulaBuiltins）
                if (name.Length > 1)
                {
                    FormulaBuiltin? builtin = FormulaBuiltins.Find(name);
                    if (builtin is null)
                        throw new FormulaError($"「{name}」不是一个参数，也不是内置只读量。参数名只有一个字母（M、N…）；"
                            + $"想用「当前回合数 / 丢了第几张 / 身上有几个状态」这类实时读数，请用内置名字：{FormulaBuiltins.Names}。"
                            + "（true / false 也可以直接写。）");
                    return new VarNode(builtin.Id);
                }
                return new VarNode(name.ToUpperInvariant());
            }
            throw new FormulaError(Cur == '\0'
                ? "公式写到一半就没了（多半是少了一个数字或右括号）。"
                : $"公式里有个看不懂的字符「{Cur}」。");
        }
    }

    /// <summary>函数怎么发：有 decimal 重载的直接调；只有 double 重载的参数转 double、结果转回 decimal；
    /// 本体没有现成重载的自己拼；返回布尔的直接给 true / false。</summary>
    private enum FnKind { Decimal, DoubleCast, Inline, BoolTrue, BoolFalse }

    /// <summary>一个数学函数：C# 名字 + 参数个数（区间）+ 生成方式。</summary>
    private sealed record FnSpec(string Cs, int MinArgs, int MaxArgs, FnKind Kind = FnKind.Decimal)
    {
        public string ArityText => MinArgs == MaxArgs ? MinArgs.ToString(CultureInfo.InvariantCulture)
            : $"{MinArgs}~{MaxArgs}";
    }

    /// <summary>
    /// 公式里能写的函数（V0.2.196 起支持「条件求值」，V0.2.197 补齐 .NET Math 那一套）。
    ///
    /// 为什么分 Decimal / DoubleCast：.NET 的 <c>Math</c> 只有 Abs/Max/Min/Sign/Clamp/Ceiling/Floor/Round/
    /// Truncate 有 decimal 重载，P ow/Sqrt/Log/三角/双曲这些**只有 double**（实测编译过一遍才确定，见
    /// <c>_tmp\mathcheck</c>）—— 所以后者生成 <c>(decimal)System.Math.Xxx((double)(x))</c>。
    /// 精度：三角 / 对数这类本来就是 double 语义，转一手 decimal 不会更差。
    /// </summary>
    private static readonly Dictionary<string, FnSpec> Fns = new(StringComparer.Ordinal)
    {
        // ===== 基础（decimal 重载，最精确）=====
        ["min"] = new("Min", 2, 2),
        ["max"] = new("Max", 2, 2),
        ["abs"] = new("Abs", 1, 1),
        ["sign"] = new("Sign", 1, 1),
        ["clamp"] = new("Clamp", 3, 3),
        // ===== 取整（decimal 重载）=====
        ["floor"] = new("Floor", 1, 1),
        ["ceiling"] = new("Ceiling", 1, 1),
        ["ceil"] = new("Ceiling", 1, 1),
        ["round"] = new("Round", 1, 2),
        ["truncate"] = new("Truncate", 1, 1),
        ["trunc"] = new("Truncate", 1, 1),
        // 传统四舍五入（C# 的 Math.Round(x, MidpointRounding.AwayFromZero)）：2.5 → 3
        ["roundaway"] = new("Round", 1, 2, FnKind.Inline),
        // Convert.ToInt32(条件)：布尔真 1 / 假 0，数值取整
        ["toint"] = new("Truncate", 1, 1),
        // ===== 幂 / 根 / 对数（只有 double 重载）=====
        ["pow"] = new("Pow", 2, 2, FnKind.DoubleCast),
        ["sqrt"] = new("Sqrt", 1, 1, FnKind.DoubleCast),
        ["cbrt"] = new("Cbrt", 1, 1, FnKind.DoubleCast),
        ["exp"] = new("Exp", 1, 1, FnKind.DoubleCast),
        ["log"] = new("Log", 1, 2, FnKind.DoubleCast),
        ["log10"] = new("Log10", 1, 1, FnKind.DoubleCast),
        ["log2"] = new("Log2", 1, 1, FnKind.DoubleCast),
        // ===== 三角（弧度）=====
        ["sin"] = new("Sin", 1, 1, FnKind.DoubleCast),
        ["cos"] = new("Cos", 1, 1, FnKind.DoubleCast),
        ["tan"] = new("Tan", 1, 1, FnKind.DoubleCast),
        ["asin"] = new("Asin", 1, 1, FnKind.DoubleCast),
        ["acos"] = new("Acos", 1, 1, FnKind.DoubleCast),
        ["atan"] = new("Atan", 1, 1, FnKind.DoubleCast),
        ["atan2"] = new("Atan2", 2, 2, FnKind.DoubleCast),
        // ===== 双曲 =====
        ["sinh"] = new("Sinh", 1, 1, FnKind.DoubleCast),
        ["cosh"] = new("Cosh", 1, 1, FnKind.DoubleCast),
        ["tanh"] = new("Tanh", 1, 1, FnKind.DoubleCast),
        ["asinh"] = new("Asinh", 1, 1, FnKind.DoubleCast),
        ["acosh"] = new("Acosh", 1, 1, FnKind.DoubleCast),
        ["atanh"] = new("Atanh", 1, 1, FnKind.DoubleCast),
        ["ieeeremainder"] = new("IEEERemainder", 2, 2, FnKind.DoubleCast),
        // ===== 本体没有现成重载的（自己拼，语义照 .NET 那几个）=====
        ["maxmagnitude"] = new("MaxMagnitude", 2, 2, FnKind.Inline),   // 绝对值大的那个
        ["minmagnitude"] = new("MinMagnitude", 2, 2, FnKind.Inline),   // 绝对值小的那个
        ["copysign"] = new("CopySign", 2, 2, FnKind.Inline),           // 把第二个的符号给第一个
        ["degreestoradians"] = new("DegreesToRadians", 1, 1, FnKind.Inline),
        ["radianstodegrees"] = new("RadiansToDegrees", 1, 1, FnKind.Inline),
        // ===== 判断（本工具的数值是 decimal：既没有 NaN 也没有无穷，所以是**确定的常数**）=====
        ["isnan"] = new("IsNaN", 1, 1, FnKind.BoolFalse),
        ["isinfinity"] = new("IsInfinity", 1, 1, FnKind.BoolFalse),
        ["ispositiveinfinity"] = new("IsPositiveInfinity", 1, 1, FnKind.BoolFalse),
        ["isnegativeinfinity"] = new("IsNegativeInfinity", 1, 1, FnKind.BoolFalse),
        ["isfinite"] = new("IsFinite", 1, 1, FnKind.BoolTrue),
    };

    /// <summary>返回布尔的函数（决定 <see cref="IsBool"/> 与布尔上下文里的生成方式）。</summary>
    private static bool IsBoolFunc(string fn) =>
        Fns.TryGetValue(fn, out var s) && s.Kind is FnKind.BoolTrue or FnKind.BoolFalse;

    /// <summary>
    /// 把表达式树在**生成期**折叠成一个常数。
    ///
    /// 用途（V0.2.128）：锻造页那 12 个「数字框」现在也能填公式，而有些位置要的是**编译期常量**
    /// （卡牌费用 <c>base(cost, …)</c> / 「升级后费用」的差值…）。这些位置上：
    ///   · 只写数字 → 走老路（一个字符都不变）；
    ///   · 写 <c>2*3</c> / <c>A+3</c>（A = 「固定数值」来源的参数）→ 这里算出常数，生成出来还是老样子；
    ///   · 写了 X / Y / 实时读数 / 运行时参数 → 折叠不出来（返回 false，<paramref name="error"/> = 那个名字），
    ///     调用方必须**报错**说清「这里要生成期就算得出来的数」，不许悄悄当默认值。
    ///
    /// <paramref name="constOf"/>：把一个标识符换成常数（认不出来返回 null）。
    /// </summary>
    /// <summary>
    /// V0.2.297（用户要求「公式得出的数值能不能实时显示」）：界面上那一行小字用的预览。
    ///
    /// <para>只含**固定数值参数**的公式 → 直接算出结果（`= 12`）；
    /// 含运行时量（X / Y / 内置只读量 / 没有固定值的参数）→ 说明「打出时才算」；
    /// 公式语法错 → 直接把错因写出来（比等校验器报错快）。</para>
    /// </summary>
    public static string Preview(string? formula, CharacterProfile? p)
    {
        string text = EffectSpec.FormulaText(formula);
        if (text.Length == 0) return "";
        Node? root = TryParse(text, out string parseError);
        if (root is null) return "⚠ 公式有误：" + parseError;
        var ids = Identifiers(root);
        if (ids.Count == 0)
            return TryEvalConst(root, _ => null, out decimal v0, out _)
                ? "= " + v0.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                : "";
        // 只把「有固定值」的参数当常量；其余（X / Y / 内置读数 / 运行时参数）留空 → 算不出来
        decimal? ConstOf(string id)
        {
            if (p is null) return null;
            var param = p.CustomParams?.FirstOrDefault(x => x is not null && string.Equals(ParamNames.Normalize(x.Name), id, StringComparison.OrdinalIgnoreCase));
            if (param is null) return null;
            // 「取值来源 = 固定数值」才算常量；别的来源都要等打出那一刻
            return string.Equals(param.Source, ParamSources.Value, StringComparison.OrdinalIgnoreCase) ? param.Value : null;
        }
        if (TryEvalConst(root, ConstOf, out decimal v, out _))
            return "= " + v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        return "＝出牌时现算（含 X / 状态层数 / 手牌数这类运行时量）";
    }
    public static bool TryEvalConst(Node? root, Func<string, decimal?> constOf, out decimal value, out string error)
    {
        value = 0m;
        error = "";
        return EvalConst(root, constOf, 0, ref value, ref error);
    }

    private static bool EvalConst(Node? n, Func<string, decimal?> constOf, int depth, ref decimal value, ref string error)
    {
        if (n is null) { error = "（空公式）"; return false; }
        if (depth > 32) { error = "（公式嵌得太深）"; return false; }
        switch (n)
        {
            case NumberNode num:
                value = num.Value;
                return true;
            case VarNode v:
            {
                decimal? c = constOf(v.Name);
                if (c is null) { error = v.Name; return false; }     // 运行时才算得出来
                value = c.Value;
                return true;
            }
            case UnaryNode u:
            {
                decimal inner = 0m;
                if (!EvalConst(u.Inner, constOf, depth + 1, ref inner, ref error)) return false;
                value = u.Op == '-' ? -inner : inner;
                return true;
            }
            case BinaryNode b:
            {
                decimal l = 0m, r = 0m;
                if (!EvalConst(b.Left, constOf, depth + 1, ref l, ref error)) return false;
                if (!EvalConst(b.Right, constOf, depth + 1, ref r, ref error)) return false;
                switch (b.Op)
                {
                    case '+': value = l + r; return true;
                    case '-': value = l - r; return true;
                    case '*': value = l * r; return true;
                    case '/':
                        if (r == 0m) { error = "（除以 0）"; return false; }
                        value = l / r;
                        return true;
                    case '%':
                        if (r == 0m) { error = "（对 0 取余）"; return false; }
                        value = l % r;
                        return true;
                    default: error = "（认不出来的运算符）"; return false;
                }
            }
            case CallNode c:
            {
                var args = new decimal[c.Args.Count];
                for (int i = 0; i < c.Args.Count; i++)
                    if (!EvalConst(c.Args[i], constOf, depth + 1, ref args[i], ref error)) return false;
                // V0.2.197：整张 Math 表都能折（写的时候全是常数就生成期算好，比如卡牌费用那种要常量的地方）
                if (!Fns.TryGetValue(c.Func, out var spec0)) { error = "（认不出来的函数）"; return false; }
                switch (spec0.Kind)
                {
                    case FnKind.BoolTrue: value = 1m; return true;
                    case FnKind.BoolFalse: value = 0m; return true;
                }
                double D(int i) => (double)args[i];
                switch (c.Func)
                {
                    case "min": value = Math.Min(args[0], args[1]); return true;
                    case "max": value = Math.Max(args[0], args[1]); return true;
                    case "abs": value = Math.Abs(args[0]); return true;
                    case "sign": value = Math.Sign(args[0]); return true;
                    case "clamp": value = Math.Clamp(args[0], args[1], args[2]); return true;
                    case "floor": value = Math.Floor(args[0]); return true;
                    case "ceiling":
                    case "ceil": value = Math.Ceiling(args[0]); return true;
                    case "truncate":
                    case "trunc":
                    case "toint": value = Math.Truncate(args[0]); return true;
                    case "round": value = c.Args.Count > 1 ? Math.Round(args[0], (int)args[1]) : Math.Round(args[0]); return true;
                    case "roundaway":
                    {
                        double p = c.Args.Count > 1 ? Math.Pow(10, D(1)) : 1.0;
                        double x = D(0) * p;
                        value = (decimal)((x >= 0 ? Math.Floor(x + 0.5) : Math.Ceiling(x - 0.5)) / p);
                        return true;
                    }
                    case "maxmagnitude": value = Math.Abs(args[0]) >= Math.Abs(args[1]) ? args[0] : args[1]; return true;
                    case "minmagnitude": value = Math.Abs(args[0]) <= Math.Abs(args[1]) ? args[0] : args[1]; return true;
                    case "copysign": value = Math.Sign(args[1]) < 0 ? -Math.Abs(args[0]) : Math.Abs(args[0]); return true;
                    case "degreestoradians": value = (decimal)(D(0) * Math.PI / 180.0); return true;
                    case "radianstodegrees": value = (decimal)(D(0) * 180.0 / Math.PI); return true;
                }
                // 其余（pow / sqrt / 对数 / 三角 / 双曲…）：同样按 double 折（和生成出来的代码同一套语义）
                value = (decimal)(c.Func switch
                {
                    "pow" => Math.Pow(D(0), D(1)),
                    "sqrt" => Math.Sqrt(D(0)),
                    "cbrt" => Math.Cbrt(D(0)),
                    "exp" => Math.Exp(D(0)),
                    "log" => c.Args.Count > 1 ? Math.Log(D(0), D(1)) : Math.Log(D(0)),
                    "log10" => Math.Log10(D(0)),
                    "log2" => Math.Log2(D(0)),
                    "sin" => Math.Sin(D(0)),
                    "cos" => Math.Cos(D(0)),
                    "tan" => Math.Tan(D(0)),
                    "asin" => Math.Asin(D(0)),
                    "acos" => Math.Acos(D(0)),
                    "atan" => Math.Atan(D(0)),
                    "atan2" => Math.Atan2(D(0), D(1)),
                    "sinh" => Math.Sinh(D(0)),
                    "cosh" => Math.Cosh(D(0)),
                    "tanh" => Math.Tanh(D(0)),
                    "asinh" => Math.Asinh(D(0)),
                    "acosh" => Math.Acosh(D(0)),
                    "atanh" => Math.Atanh(D(0)),
                    "ieeeremainder" => Math.IEEERemainder(D(0), D(1)),
                    _ => double.NaN,
                });
                if (double.IsNaN((double)value) && !double.IsNaN(c.Func switch { "ieeeremainder" => D(0), _ => 0 }))
                {
                    // 认不出来的函数名 / 算出来 NaN（例：log(-1)）→ 不折，交给运行时（生成代码照旧）
                    error = "（这个函数在生成期算不出来）";
                    return false;
                }
                return true;
            }
            // V0.2.196（用户要求）：比较 / 逻辑 / 三元也能在生成期折成常数（真 1 / 假 0）
            case CompareNode cmp:
            {
                decimal l = 0m, r = 0m;
                if (!EvalConst(cmp.Left, constOf, depth + 1, ref l, ref error)) return false;
                if (!EvalConst(cmp.Right, constOf, depth + 1, ref r, ref error)) return false;
                bool ok = cmp.Op switch
                {
                    ">" => l > r,
                    "<" => l < r,
                    ">=" => l >= r,
                    "<=" => l <= r,
                    "==" => l == r,
                    "!=" => l != r,
                    _ => false,
                };
                value = ok ? 1m : 0m;
                return true;
            }
            case LogicNode lg:
            {
                decimal l = 0m, r = 0m;
                if (!EvalConst(lg.Left, constOf, depth + 1, ref l, ref error)) return false;
                if (!EvalConst(lg.Right, constOf, depth + 1, ref r, ref error)) return false;
                // 数字当条件用 = 「不等于 0」（和 EmitBool 的口径一致）
                bool a2 = l != 0m, b2 = r != 0m;
                value = (lg.Op == "&&" ? a2 && b2 : a2 || b2) ? 1m : 0m;
                return true;
            }
            case NotNode nt:
            {
                decimal inner2 = 0m;
                if (!EvalConst(nt.Inner, constOf, depth + 1, ref inner2, ref error)) return false;
                value = inner2 == 0m ? 1m : 0m;
                return true;
            }
            case TernaryNode tn:
            {
                decimal c3 = 0m, t3 = 0m, e3 = 0m;
                if (!EvalConst(tn.Cond, constOf, depth + 1, ref c3, ref error)) return false;
                if (!EvalConst(tn.Then, constOf, depth + 1, ref t3, ref error)) return false;
                if (!EvalConst(tn.Else, constOf, depth + 1, ref e3, ref error)) return false;
                value = c3 != 0m ? t3 : e3;
                return true;
            }
            default:
                error = "（认不出来的公式）";
                return false;
        }
    }

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

    /// <summary>
    /// 这个片段拼进更大的表达式之前要不要加一层括号。
    /// 判据：**带 `??` 而且整体没被括号包着** —— 空合并的优先级比四则运算低，不加括号会被后面的运算符吞掉
    /// （`x?.MaxHp ?? 0 * 0.04m` 实际是 `x?.MaxHp`，用户实测报的「参数=最大生命、参数*0.04，伤害却等于满血」）。
    /// 已经以 `(` 开头的（内置量 `(p?.TurnNumber ?? 0)`、`(decimal)(…)` 强转、函数调用）都不用再加 ——
    /// 那些本来就不会被吞，多套一层还会改掉生成文本（自检里按原文断言的十几条会跟着红）。
    /// </summary>
    private static bool NeedsParens(string rep) =>
        rep.Contains("??", StringComparison.Ordinal) && !rep.TrimStart().StartsWith("(", StringComparison.Ordinal);

    private static bool EmitNode(Node n, StringBuilder sb, Func<string, string?> resolveVar, ref string error)
    {
        // V0.2.196（用户要求）：**布尔当数值用** → 「真取 1、假取 0」。
        // 例：(a > 0) * 5 生成 ((a > 0) ? 1m : 0m) * 5m；三个数里有几个是正数就直接相加。
        if (IsBool(n))
        {
            sb.Append("((");
            if (!EmitBool(n, sb, resolveVar, ref error)) return false;
            sb.Append(") ? 1m : 0m)");
            return true;
        }
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
                // ⚠ 运算符优先级：参数「最大生命 / 当前生命」生成的是 `x?.MaxHp ?? 0`，
                // 而 `??` 的优先级**比 `*` 低** —— 直接拼进 `* 0.04` 会变成
                // `x?.MaxHp ?? (0 * 0.04)` = **满血值**（用户实测报的：
                // 「参数为最大生命时，造成 参数*0.04 的伤害，结果还是最大生命的伤害」）。
                // 所以凡是带 `??` 的片段一律加一层括号；简单变量 / 本来就有括号的保持原样
                //（生成文本一个字符都不变）。
                sb.Append(NeedsParens(rep) ? "(" + rep + ")" : rep);
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
                if (!Fns.TryGetValue(c.Func, out var spec))
                {
                    error = $"认不出来的函数「{c.Func}」";
                    return false;
                }
                switch (spec.Kind)
                {
                    case FnKind.BoolTrue:
                        sb.Append("(true)");
                        return true;
                    case FnKind.BoolFalse:
                        sb.Append("(false)");
                        return true;
                    case FnKind.DoubleCast:
                    {
                        sb.Append("(decimal)System.Math.").Append(spec.Cs).Append('(');
                        for (int i = 0; i < c.Args.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append("(double)(");
                            if (!EmitNode(c.Args[i], sb, resolveVar, ref error)) return false;
                            sb.Append(')');
                        }
                        sb.Append(')');
                        return true;
                    }
                    case FnKind.Inline:
                        return EmitInlineFn(c, spec, sb, resolveVar, ref error);
                    default:
                    {
                        sb.Append("System.Math.").Append(spec.Cs).Append('(');
                        for (int i = 0; i < c.Args.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            if (!EmitNode(c.Args[i], sb, resolveVar, ref error)) return false;
                        }
                        sb.Append(')');
                        return true;
                    }
                }
            }
            // 三元的条件是布尔，两支按数值（布尔支自动 1/0）
            case TernaryNode t:
                sb.Append('(');
                if (!EmitBool(t.Cond, sb, resolveVar, ref error)) return false;
                sb.Append(" ? ");
                if (!EmitNode(t.Then, sb, resolveVar, ref error)) return false;
                sb.Append(" : ");
                if (!EmitNode(t.Else, sb, resolveVar, ref error)) return false;
                sb.Append(')');
                return true;
            default:
                error = "认不出来的公式";
                return false;
        }
    }

    /// <summary>π 在生成代码里的字面量（和 Math.PI 同一个值）。</summary>
    private const string PiLiteral = "3.14159265358979323846264338327950288419716939937510";

    /// <summary>
    /// 本体没有现成重载的那几个（自己拼表达式）：
    ///   · roundaway —— 传统四舍五入（.NET 的 AwayFromZero）：2.5 → 3；可带小数位数；
    ///   · maxmagnitude / minmagnitude —— 按**绝对值**比大小；
    ///   · copysign —— 把第二个数的符号给第一个；
    ///   · degreestoradians / radianstodegrees —— 角度 ↔ 弧度。
    /// 只用到本体本来就有的 decimal 重载（Abs / Sign / Floor / Ceiling），不会多生成任何东西。
    /// </summary>
    private static bool EmitInlineFn(CallNode c, FnSpec spec, StringBuilder sb, Func<string, string?> resolveVar, ref string error)
    {
        switch (c.Func)
        {
            case "roundaway":
            {
                string p = "1m";
                if (c.Args.Count > 1)
                {
                    // 小数位数：p = 10^N（N 可以是公式，所以用 Pow 现算）
                    var pb = new StringBuilder();
                    var inner = new StringBuilder();
                    if (!EmitNode(c.Args[1], inner, resolveVar, ref error)) return false;
                    pb.Append("((decimal)System.Math.Pow(10, (double)(").Append(inner).Append(")))");
                    p = pb.ToString();
                }
                // ((X >= 0 ? floor(X*p + 0.5) : ceiling(X*p - 0.5)) / p) —— 两层的括号都要：
                // 外层是「除以 p」，里层把三元整个包住（不然除号只作用在 else 那一支上）
                sb.Append("((");
                if (!EmitNode(c.Args[0], sb, resolveVar, ref error)) return false;
                sb.Append(" >= 0m ? System.Math.Floor((");
                if (!EmitNode(c.Args[0], sb, resolveVar, ref error)) return false;
                sb.Append(") * ").Append(p).Append(" + 0.5m) : System.Math.Ceiling((");
                if (!EmitNode(c.Args[0], sb, resolveVar, ref error)) return false;
                sb.Append(") * ").Append(p).Append(" - 0.5m)) / ").Append(p).Append(')');
                return true;
            }
            case "maxmagnitude":
            case "minmagnitude":
            {
                bool max = c.Func == "maxmagnitude";
                sb.Append("(System.Math.Abs(");
                if (!EmitNode(c.Args[0], sb, resolveVar, ref error)) return false;
                sb.Append(") ").Append(max ? ">=" : "<=").Append(" System.Math.Abs(");
                if (!EmitNode(c.Args[1], sb, resolveVar, ref error)) return false;
                sb.Append(") ? ");
                if (!EmitNode(c.Args[0], sb, resolveVar, ref error)) return false;
                sb.Append(" : ");
                if (!EmitNode(c.Args[1], sb, resolveVar, ref error)) return false;
                sb.Append(')');
                return true;
            }
            case "copysign":
                sb.Append("(System.Math.Sign(");
                if (!EmitNode(c.Args[1], sb, resolveVar, ref error)) return false;
                sb.Append(") < 0 ? -System.Math.Abs(");
                if (!EmitNode(c.Args[0], sb, resolveVar, ref error)) return false;
                sb.Append(") : System.Math.Abs(");
                if (!EmitNode(c.Args[0], sb, resolveVar, ref error)) return false;
                sb.Append("))");
                return true;
            case "degreestoradians":
                sb.Append("((");
                if (!EmitNode(c.Args[0], sb, resolveVar, ref error)) return false;
                sb.Append(") * ").Append(PiLiteral).Append("m / 180m)");
                return true;
            case "radianstodegrees":
                sb.Append("((");
                if (!EmitNode(c.Args[0], sb, resolveVar, ref error)) return false;
                sb.Append(") * 180m / ").Append(PiLiteral).Append("m)");
                return true;
            default:
                error = $"认不出来的函数「{c.Func}」";
                return false;
        }
    }

    /// <summary>
    /// 把一棵公式发成**布尔** C# 表达式（用于比较 / 逻辑 / 取反 / 三元的条件，以及 <c>&amp;&amp; || !</c> 的操作数）。
    /// 数值出现在需要布尔的地方 = 「不等于 0」（用户会写 <c>flag ? 1 : 0</c>，flag 是 0/1 的参数）。
    /// </summary>
    private static bool EmitBool(Node n, StringBuilder sb, Func<string, string?> resolveVar, ref string error)
    {
        switch (n)
        {
            case CompareNode c:
            {
                sb.Append('(');
                if (!EmitNode(c.Left, sb, resolveVar, ref error)) return false;
                sb.Append(' ').Append(c.Op).Append(' ');
                if (!EmitNode(c.Right, sb, resolveVar, ref error)) return false;
                sb.Append(')');
                return true;
            }
            case LogicNode l:
            {
                sb.Append('(');
                if (!EmitBool(l.Left, sb, resolveVar, ref error)) return false;
                sb.Append(' ').Append(l.Op).Append(' ');
                if (!EmitBool(l.Right, sb, resolveVar, ref error)) return false;
                sb.Append(')');
                return true;
            }
            case NotNode t:
                sb.Append("!(");
                if (!EmitBool(t.Inner, sb, resolveVar, ref error)) return false;
                sb.Append(')');
                return true;
            // 返回布尔的函数（double.IsNaN 那一族）：本工具的数值是 decimal，没有 NaN 也没有无穷 —— 结论是确定的
            case CallNode call when IsBoolFunc(call.Func):
                sb.Append(Fns[call.Func].Kind == FnKind.BoolTrue ? "(true)" : "(false)");
                return true;
            case TernaryNode tn:
            {
                // 两支都是布尔 → 布尔三元；否则（数值三元）用「不等于 0」判条件
                sb.Append('(');
                if (!EmitBool(tn.Cond, sb, resolveVar, ref error)) return false;
                sb.Append(" ? ");
                if (IsBool(tn)) { if (!EmitBool(tn.Then, sb, resolveVar, ref error)) return false; }
                else if (!EmitNode(tn.Then, sb, resolveVar, ref error)) return false;
                sb.Append(" : ");
                if (IsBool(tn)) { if (!EmitBool(tn.Else, sb, resolveVar, ref error)) return false; }
                else if (!EmitNode(tn.Else, sb, resolveVar, ref error)) return false;
                sb.Append(')');
                return true;
            }
            default:
                sb.Append('(');
                if (!EmitNode(n, sb, resolveVar, ref error)) return false;
                sb.Append(" != 0m)");
                return true;
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
    public static Func<string, string?> ConditionResolver(CharacterProfile p, string? playTargetExpr)
        => InlineResolver(p, "base.Owner", "base.Owner.Creature", playTargetExpr);

    /// <summary>
    /// **全内联**解析器（V0.2.164：「数值公式」铺到遗物 / 药水 / 自定义状态那三条链）：
    /// 三个占位符各由调用方给出**本链**的写法 ——
    /// <list type="bullet">
    /// <item>遗物 / 药水：<c>__fgPlayer</c> = <c>base.Owner</c>（Player）、<c>__fgSelf</c> = <c>base.Owner.Creature</c>；</item>
    /// <item>自定义状态：<c>__fgPlayer</c> = <c>base.Owner.Player</c>、<c>__fgSelf</c> = <c>base.Owner</c>（状态钩子里 Owner 就是 Creature）。</item>
    /// </list>
    /// <paramref name="targetExpr"/> = 「指定敌人」那类参数在这条链上取谁（遗物 / 状态没有「玩家选中的目标」，
    /// 传「可打的第一个敌人」；传 null = 这个场合没有目标，参数会退化成 <c>null</c>，校验器会先拦住）。
    /// </summary>
    public static Func<string, string?> InlineResolver(CharacterProfile p, string playerExpr, string selfExpr,
        string? targetExpr) => id => id switch
    {
        "X" => $"ForgeFormulaRuntime.Energy({playerExpr})",
        "Y" => $"ForgeFormulaRuntime.Stars({playerExpr})",
        _ => FormulaBuiltins.Find(id) is { } builtin
            ? builtin.Expr(playerExpr, selfExpr)
            : (p.CustomParams.FirstOrDefault(x => x is not null
                    && string.Equals(ParamNames.Normalize(x.Name), id, StringComparison.Ordinal)) is { } param
                ? InlineParam(param, playerExpr, selfExpr, targetExpr, p)
                : null),
    };

    /// <summary>把参数表达式里的局部变量替换成内联写法（见 <see cref="ConditionResolver"/> / <see cref="InlineResolver"/>）。</summary>
    private static string InlineParam(ParamSpec p, string playerExpr, string selfExpr, string? targetExpr, CharacterProfile? profile = null)
    {
        string expr = ParamExpr(p, profile);
        expr = expr.Replace("__fgSelf", selfExpr, StringComparison.Ordinal)
                   .Replace("__fgPlayer", playerExpr, StringComparison.Ordinal);
        if (expr.Contains("__fgTarget", StringComparison.Ordinal))
            expr = expr.Replace("__fgTarget", targetExpr ?? "null", StringComparison.Ordinal);
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
    public static string ParamExpr(ParamSpec p, CharacterProfile? profile = null)
    {
        string src = ParamSources.Normalize(p.Source);
        string target = TargetExpr(p.Target);
        // ⚠ 顺序要紧：**先认参数页那 7 个固定来源**（它们能指定「从谁身上取」），
        // 认不出来再看是不是一条实时读数。反过来的话，内置量里同名的（例：MyMaxHp 不算，但
        // 早先 `MaxHp` 就撞过）会把「指定敌人的最大生命」悄悄变成「自己的最大生命」。
        switch (src)
        {
            case ParamSources.CurrentHp: return $"{target}?.CurrentHp ?? 0";
            case ParamSources.MaxHp: return $"{target}?.MaxHp ?? 0";
            case ParamSources.Gold: return "(decimal)(__fgPlayer?.Gold ?? 0)";
            // ⚠ 当前能量在 PlayerCombatState 上（**Player 上没有 Energy**；本体只有 Player.MaxEnergy）——
            // 以前这里生成的是 `__fgPlayer?.Energy`，那是个编译不过的表达式（CS1061），
            // 只要有一个参数选「能量数」并被公式引用，生成的模组就整个编不过。
            // 顺便加了战斗外兜底（战斗外 PlayerCombatState 是 null → 0）。
            case ParamSources.Energy: return "((decimal)(__fgPlayer?.PlayerCombatState?.Energy ?? 0))";
            case ParamSources.MaxEnergy: return "((decimal)(__fgPlayer?.MaxEnergy ?? 0))";
            case ParamSources.Power: return $"ForgeFormulaRuntime.PowerAmount({target}, \"{p.PowerId ?? ""}\")";
            // V0.2.250：目标身上减益的**个数**（几种负面效果，和层数无关）——
            // 本体「撕碎 Rend」的倍率就是这个数（`target.Powers.Count(TypeForCurrentAmount == Debuff)`），
            // 直接复用锻造页【状态列表】用的那个运行时帮手（同一个判据，不用写第二份）。
            case ParamSources.DebuffCount: return $"ForgeFormulaRuntime.PowerCount({target}, \"Debuff\", null)";
        // V0.2.271（用户要求）：牌组里某种牌的数量（范围可含自定义关键词 / 组）
        case ParamSources.DeckCountOf: return DeckCountExpr(p, profile);
        }
        // 用户要求：参数页也能监听那些「实时读数」（回合数 / 丢弃·消耗·生成·复制张数 / 拿到的资源量 /
        // 身上状态数…）—— 它们就是公式内置量那张表（界面 / 生成 / 回读同一份），直接借它的表达式。
        if (FormulaBuiltins.Find(src) is { } builtin)
            return builtin.Expr("__fgPlayer", "__fgSelf");
        return p.Value.ToString("0.############################", CultureInfo.InvariantCulture) + "m";
    }

    /// <summary>
    /// V0.2.271（用户要求「牌组里某种牌数，范围可以是自定义关键词和组」）：
    /// 牌组里符合某个范围的牌数。
    ///
    /// <para>内置牌型（攻击 / 技能 / 能力 / 诅咒 / 状态）走运行时判据 <c>c.Type == CardType.X</c>；
    /// 自定义关键词 / 组在游戏里没有对应枚举，只能在**生成期**把成员解析成类名清单，写成
    /// <c>c is A or B or …</c>（和「生成卡牌」那套范围同一个口径：生成期查表，运行时不再过滤）。</para>
    /// </summary>
    public static string DeckCountExpr(ParamSpec p, CharacterProfile? profile)
    {
        string filter = (p.DeckFilter ?? "").Trim();
        if (filter.Length == 0 || string.Equals(filter, "Any", StringComparison.Ordinal))
            return "(decimal)(__fgPlayer.Deck.Cards.Count)";
        if (filter is "Attack" or "Skill" or "Power" or "Status" or "Curse")
            return $"(decimal)(__fgPlayer.Deck.Cards.Where(__fgC => __fgC.Type == CardType.{filter}).Count())";
        if (profile is null) return "0m";
        var cards = Effects.EffectCatalog.CardSetForFilter(profile, filter);
        if (cards.Count == 0) return "0m";
        var n = Naming.From(profile);
        var names = new List<string>();
        foreach (var c in cards)
        {
            if (c is null) continue;
            string cls = n.CardClassName(profile, c);
            if (cls.Length > 0 && !names.Contains(cls)) names.Add(cls);
        }
        if (names.Count == 0) return "0m";
        // 整副牌都是这些牌时不用逐个判（生成出来的表达式短一点）
        int owned = profile.AllCards.Count(c => c is not null && !c.IsVanillaCard);
        if (owned > 0 && names.Count >= owned) return "(decimal)(__fgPlayer.Deck.Cards.Count)";
        return $"(decimal)(__fgPlayer.Deck.Cards.Count(__fgC => __fgC is {string.Join(" or ", names)}))";
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
            ParamSources.CurrentHp or ParamSources.MaxHp or ParamSources.Power or ParamSources.DebuffCount
            || ParamTargets.Normalize(p.Target) != ParamTargets.Self
            || ParamSources.Normalize(p.Source) is ParamSources.Gold or ParamSources.Energy or ParamSources.MaxEnergy);
}
