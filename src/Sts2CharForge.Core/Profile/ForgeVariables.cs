using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sts2CharForge.Core.Profile;

/// <summary>
/// V0.3.12（用户定稿的「变量页」规格）：用户自定义的**可读写变量**。
///
/// <para>和 <see cref="ParamSpec"/>（只读参数，单字母）不同：变量是**存储**，可以当计数器累加 / 递减，
/// 名字最多 12 位、不限个数；值是一个**公式**（可引用别的变量 / 参数 / X、Y / 内置读数）。</para>
///
/// <para>作用域（用户 2026-11-04 确认）只有两档：不勾 <see cref="Global"/> = **每场战斗重置**；
/// 勾上 = **本局（run）内跨战斗保留**，新开一局清零（**不是**跨局存档）。</para>
/// </summary>
public sealed class VarSpec : SpecBase
{
    private string _name = "";
    private string _note = "";
    private string _formula = "";
    private bool _global;
    private bool _allowNegative;
    private int _max = int.MaxValue;

    /// <summary>变量名：<c>[A-Za-z_][A-Za-z0-9_]*</c>、1~12 位（<see cref="VarNames.Normalize"/> 负责清洗）。</summary>
    public string Name
    {
        get => _name;
        set { if (Set(ref _name, VarNames.Normalize(value))) { Raise(nameof(Display)); Raise(nameof(Label)); } }
    }

    /// <summary>备注（不参与生成）。</summary>
    public string Note { get => _note; set => Set(ref _note, value ?? ""); }

    /// <summary>初值 / 取值公式（空 = 0）。允许引用别的变量（Variables 之间会做循环引用检测）。</summary>
    public string Formula
    {
        get => _formula;
        set { if (Set(ref _formula, (value ?? "").Trim())) Raise(nameof(Display)); }
    }

    /// <summary>☑ 全局 = 本局内跨战斗保留（新开一局清零）。</summary>
    public bool Global { get => _global; set => Set(ref _global, value); }

    /// <summary>计数器是否允许变成负数（默认不勾 = 递减到 0 就停）。</summary>
    public bool AllowNegative { get => _allowNegative; set => Set(ref _allowNegative, value); }

    /// <summary>计数器上限（int.MaxValue = 不封顶）。</summary>
    public int Max { get => _max; set => Set(ref _max, value); }

    /// <summary>列表里显示的那行字。</summary>
    public string Display => Name.Length == 0
        ? "（未命名）"
        : $"{Name} = {(Formula.Length == 0 ? "0" : Formula)}{(Global ? "  [本局]" : "")}";

    /// <summary>输入框前的标签（界面固定跟一个「=」）。</summary>
    public string Label => Name.Length == 0 ? "?" : Name;
}

/// <summary>变量名的规范与校验（界面横幅 / 校验器共用同一份规则）。</summary>
public static class VarNames
{
    /// <summary>最长 12 位（用户确认）。</summary>
    public const int MaxLength = 12;

    private static readonly Regex Ok = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>已经被内置占用的名字：X / Y（能量 / 额外资源量）以及生成端会拼进代码的关键字。</summary>
    public static readonly string[] Reserved =
    {
        "X", "Y",
        "base", "this", "null", "true", "false", "var", "new",
        "Math", "decimal", "int", "bool", "string", "object", "if", "else", "return",
    };

    /// <summary>清洗：去掉首尾空白、把中文/符号换成下划线、超长截断；不做「补默认名」。</summary>
    public static string Normalize(string? raw)
    {
        string s = (raw ?? "").Trim();
        if (s.Length == 0) return "";
        var chars = new List<char>(s.Length);
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c) && c < 128) chars.Add(c);
            else if (c == '_') chars.Add(c);
            else chars.Add('_');
        }
        string t = new string(chars.ToArray());
        while (t.Contains("__", StringComparison.Ordinal)) t = t.Replace("__", "_", StringComparison.Ordinal);
        t = t.Trim('_');
        if (t.Length > MaxLength) t = t.Substring(0, MaxLength);
        if (t.Length > 0 && char.IsDigit(t[0])) t = "v" + t;
        return t;
    }

    /// <summary>校验一个名字；返回 null = 合格，否则是**给人看的原因**。</summary>
    public static string? Check(string? name)
    {
        string s = (name ?? "").Trim();
        if (s.Length == 0) return "名字是空的。";
        if (s.Length > MaxLength) return $"太长（最多 {MaxLength} 位）。";
        if (!Ok.IsMatch(s)) return "只能字母 / 数字 / 下划线，且不能以数字开头。";
        foreach (string r in Reserved)
            if (string.Equals(s, r, StringComparison.Ordinal)) return $"「{s}」是内置保留名，换一个。";
        return null;
    }

    /// <summary>下一个没被占用的名字：a、b…z、a1…a9、b1…（变量页「新建」按钮用）。</summary>
    public static string NextFree(IEnumerable<string?> used)
    {
        var taken = new HashSet<string>(used.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()),
            StringComparer.OrdinalIgnoreCase);
        for (char c = 'a'; c <= 'z'; c++)
        {
            string one = c.ToString();
            if (!taken.Contains(one)) return one;
        }
        for (int i = 2; i < 100; i++)
            for (char c = 'a'; c <= 'z'; c++)
            {
                string two = c.ToString() + i;
                if (!taken.Contains(two)) return two;
            }
        return "v";
    }
}
