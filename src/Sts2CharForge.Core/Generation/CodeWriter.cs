using System.Globalization;
using System.Text;

namespace Sts2CharForge.Core.Generation;

/// <summary>带缩进的 C# 源码拼装器。</summary>
public sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    public CodeWriter Line(string text = "")
    {
        if (text.Length == 0) { _sb.AppendLine(); return this; }
        _sb.Append(new string(' ', _indent * 4)).AppendLine(text);
        return this;
    }

    public CodeWriter Raw(string text) { _sb.Append(text); return this; }

    /// <summary>写一行并开启代码块（自动补 "{"）。</summary>
    public CodeWriter Open(string text) { Line(text + " {"); _indent++; return this; }

    public CodeWriter Indent() { _indent++; return this; }
    public CodeWriter Dedent() { _indent = Math.Max(0, _indent - 1); return this; }
    public CodeWriter Close(string text = "}") { _indent = Math.Max(0, _indent - 1); Line(text); return this; }

    public override string ToString() => _sb.ToString();
}

/// <summary>C# 字面量格式化。</summary>
public static class Lit
{
    public static string Str(string? value) =>
        "\"" + (value ?? string.Empty)
            .Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";

    public static string Dec(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "m";
    public static string Int(decimal value) => ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
    public static string Float(double value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "f";

    public static string Identifier(string text, string fallback = "Card")
    {
        var sb = new StringBuilder();
        foreach (char c in text)
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
        string s = sb.ToString();
        if (s.Length == 0) s = fallback;
        if (char.IsDigit(s[0])) s = "C" + s;
        return s;
    }

    public static string Pascal(string text)
    {
        string s = Identifier(text);
        return s.Length == 0 ? "X" : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
