using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 锻造页**数字框**里的值（需求原文：「锻造页中所有模块里能填入数值的地方，都能填入参数或者公式」）。
///
/// 用户已拍板（Q2=A）：**保留现在的单行数字框**，允许里面直接填公式文本（不换成多行公式框）。
/// 于是同一个字段有三种可能的写法，处理顺序必须是**先数字、再公式**（红线：默认档逐字不变）：
///
///   ① 纯数字（<c>6</c> / <c>0.5</c>，含 <c>double.TryParse</c> 认的那些老写法）→ 走老路，生成的文本一个字符都不变；
///   ② 公式、但**生成期就能折叠成常数**（<c>2*3</c>、<c>A+3</c> 且 A 是「固定数值」来源的参数）→ 也走老路（用折出来的数）；
///   ③ 公式里用了**运行时才知道的量**（<c>X</c> / <c>Y</c> / 21 条内置只读量 / 运行时参数）
///      → 生成一段 C# 表达式（<see cref="Resolved.Expr"/>），由每个发射器决定怎么用；
///   ④ 解析不出来（拼错、括号不配、写了不存在的参数）→ <see cref="Resolved.Ok"/> = false，
///      **校验器报错**说清是哪个模块哪一栏 —— 绝不再像以前那样「静默回落 fallback」
///      （那正是用户点名要改掉的：填了公式却悄悄变成默认值，生成出来什么都不做还看不出来）。
///
/// 为什么常量折叠要单独一条：卡牌费用（<c>base(cost, …)</c>）、额外资源量费用（<c>CanonicalStarCost</c>）
/// 是**编译期常量**，运行时算不了 —— 那两个栏位只支持 ①②，写了 ③ 由校验器报错拦住（不许静默）。
/// </summary>
public static class ForgeNumbers
{
    /// <summary>公式写在哪套变量上（决定 <c>X</c> / <c>Y</c> / 参数 / 内置量怎么展开）。</summary>
    public enum Place
    {
        /// <summary>执行点序幕之后的写法：用 <c>__X</c> / <c>__Y</c> / <c>__pM</c> 这几个已经算好的局部变量。</summary>
        PrologueVars,
        /// <summary>序幕**之前**的写法（进门守卫 / 血量阈值 / 生效次数）：全部内联，只依赖 <c>base.Owner</c>。</summary>
        Inline,
    }

    /// <summary>一个数值栏的解析结果。</summary>
    /// <param name="Ok">解析成功（纯数字 / 折叠成常数 / 生成出运行时表达式）。</param>
    /// <param name="Runtime">true = 要在运行时才算（<see cref="Expr"/> 是那段 C# 表达式）。</param>
    /// <param name="Value">生成期算得出来的值（<see cref="Runtime"/> 时是 fallback）。</param>
    /// <param name="Expr">运行时表达式（<see cref="Runtime"/> 时才非空）。</param>
    /// <param name="Text">用户填的原文（去空格）。</param>
    /// <param name="Error">解析失败的原因（<see cref="Ok"/> = false 时才非空，中文，直接能给用户看）。</param>
    public readonly record struct Resolved(bool Ok, bool Runtime, decimal Value, string Expr, string Text, string Error)
    {
        public bool Constant => Ok && !Runtime;
    }

    /// <summary>
    /// 图里所有「数字框」栏位（模块种类 + 字段 Key + 界面上的名字）。
    /// 界面提示 / 校验器措辞 / 探针逐个核都用这一份，免得三处各写一张表漂掉。
    /// </summary>
    public static IReadOnlyList<(string Kind, string Key, string Label)> NumberFields { get; } = new[]
    {
        ("Trigger", "HpThreshold", "血量低于（小于 1 = 百分比）"),
        ("Trigger", "PowerAmount", "到达 N 层"),
        ("Trigger", "CountLimit", "仅生效 N 次"),
        ("Energy", "EnergyCost", "打出时消耗的能量"),
        ("Energy", "ResourceCost", "打出时消耗的额外资源量"),
        ("Effect", "Amount", "数值"),
        ("Effect", "Times", "生效次数"),
        ("Effect", "Hits", "命中次数"),
        ("Effect", "Chance", "概率生效（%）"),
        ("Condition", "Amount", "数值 N"),
        ("Condition", "Amount2", "数值 M"),
        ("Relate", "Amount", "右值"),
    };

    /// <summary>这些栏位的值要**编译期常量**（本体 <c>base(cost, …)</c> / <c>CanonicalStarCost</c>），只支持 ①②。</summary>
    public static bool NeedsCompileTimeConst(string kind, string key) =>
        string.Equals(kind, "Energy", StringComparison.Ordinal)
        && key is "EnergyCost" or "ResourceCost";

    /// <summary>界面提示 / 校验器共用的那句话（「这一栏也能填公式」）。</summary>
    public const string FormulaHint =
        "这一栏也能填公式或参数（和【数值】模块同一套）：X = 当前所有能量，Y = 当前所有额外资源量，"
        + "一个字母的自定义参数（要先在「自定义参数 / 组」页定义），"
        + "以及 21 条内置只读量（Turn / HandCount / DiscardedThisTurn…）。支持 + - * / ( ) 和 min/max/floor/ceil/round/abs。"
        + "只写数字的老写法一个字都不变。";

    /// <summary>拆一个栏位的文字（原文 + 去首尾空格）。</summary>
    public static string TextOf(ForgeNodeSpec? node, string key) => (node?.Get(key) ?? "").Trim();

    /// <summary>
    /// 解析一个数字栏位。<paramref name="p"/> 为 null 时只认「纯数字 / 字面量公式」（拿不到自定义参数表）。
    /// </summary>
    public static Resolved Resolve(CharacterProfile? p, ForgeNodeSpec? node, string key, double fallback,
        Place place = Place.PrologueVars, string? playTargetExpr = null)
        => ResolveText(p, TextOf(node, key), fallback, place, playTargetExpr);

    /// <summary>解析一段数字栏位的文字（守卫行那种只拿得到文字、拿不到节点的场合用）。</summary>
    public static Resolved ResolveText(CharacterProfile? p, string? text, double fallback,
        Place place = Place.PrologueVars, string? playTargetExpr = null)
    {
        string src = (text ?? "").Trim();
        if (src.Length == 0) return new Resolved(true, false, (decimal)fallback, "", src, "");

        // ① 老写法：纯数字（double.TryParse 认的 6 / 0.5 / 1e3 全都照旧）——一个字符都不动
        if (double.TryParse(src, NumberStyles.Float, CultureInfo.InvariantCulture, out double plain))
            return new Resolved(true, false, (decimal)plain, "", src, "");

        // ② / ③ / ④：按公式解析
        var root = ForgeFormula.TryParse(src, out string parseError);
        if (root is null) return new Resolved(false, false, (decimal)fallback, "", src, parseError);

        if (ForgeFormula.TryEvalConst(root, id => ConstParam(p, id), out decimal folded, out _))
            return new Resolved(true, false, folded, "", src, "");

        // 运行时：翻成 C# 表达式
        // ⚠ 解析**失败**时 Runtime 必须保持 false：生成端是按 Runtime 决定「写运行时表达式」的，
        //   留个空表达式会生成 `(int)()` 这种编译不过的代码。这时候一律退回 fallback + 由校验器报错拦住。
        if (p is null)
            return new Resolved(false, false, (decimal)fallback, "", src,
                "公式里用了运行时的量，而要生成代码必须先拿到这份配置（这里拿不到自定义参数表）。");

        Func<string, string?> resolver = place == Place.Inline
            ? ForgeFormula.ConditionResolver(p, playTargetExpr)
            : ForgeFormula.CardResolver(p);
        string? expr = ForgeFormula.Emit(root, resolver, out string emitError);
        if (expr is null)
            return new Resolved(false, false, (decimal)fallback, "", src, emitError);
        return new Resolved(true, true, (decimal)fallback, expr, src, "");
    }

    /// <summary>只取一个生成期数值（老调用点用：解析不出来就用 fallback，由校验器负责报错）。</summary>
    public static double Number(CharacterProfile? p, ForgeNodeSpec? node, string key, double fallback)
    {
        var r = Resolve(p, node, key, fallback);
        return r.Runtime ? fallback : (double)r.Value;
    }

    /// <summary>把一个标识符换成常数（单字母 + 「固定数值」来源的参数；其余一律返回 null = 运行时才知道）。</summary>
    private static decimal? ConstParam(CharacterProfile? p, string id)
    {
        if (p is null) return null;
        var param = p.CustomParams.FirstOrDefault(x => x is not null
            && string.Equals(ParamNames.Normalize(x.Name), id, StringComparison.Ordinal));
        if (param is null) return null;
        // ⚠ 参数来源写成某条「实时读数」时它是跑起来才知道的（ParamExpr 会借 FormulaBuiltins 的表达式）
        if (FormulaBuiltins.Find(ParamSources.Normalize(param.Source)) is not null) return null;
        return ParamSources.Normalize(param.Source) == ParamSources.Value ? param.Value : null;
    }

    /// <summary>
    /// 图里所有**要在运行时才算**的数字栏位（纯数字 / 生成期能折叠成常数的都不算）——
    /// 生成端用它把「公式里用到的自定义参数」先声明出来（折叠成常数的那些不需要变量）。
    /// </summary>
    public static IEnumerable<(ForgeNodeSpec Node, string Key, string Text)> FormulaFields(CharacterProfile? p, ForgeGraphSpec? g)
    {
        if (g is null) yield break;
        foreach (var node in g.Nodes)
        {
            if (node is null) continue;
            var def = node.KindDef;
            if (def is null) continue;
            foreach (var (kind, key, _) in NumberFields)
            {
                if (!string.Equals(def.Id, kind, StringComparison.Ordinal)) continue;
                var r = Resolve(p, node, key, 0);
                if (r.Ok && r.Runtime) yield return (node, key, r.Text);
            }
        }
    }
}
