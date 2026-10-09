using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Sts2CharForge.Core.Effects;

/// <summary>sts2.dll 里的一个类型：类名 + 命名空间 + 基类链（从近到远）、以及它的枚举常量值。</summary>
public sealed record Sts2TypeInfo(string Name, string Namespace, IReadOnlyList<string> BaseChain, IReadOnlyDictionary<string, int> EnumValues)
{
    /// <summary>基类链里有没有这个类名（只看类名，忽略命名空间 —— 本体的类型层级是稳定的）。</summary>
    public bool DerivesFrom(string baseName)
    {
        foreach (string b in BaseChain)
            if (string.Equals(b, baseName, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>这个类型是不是一个枚举。
    ///
    /// **不能**用「有没有字面量字段」判断 —— 那样会把「带 const 的普通类」也算成枚举
    /// （实测：VulnerablePower / WeakPower 里有 <c>const string _damageIncrease</c>，
    /// 于是被当成枚举过滤掉，237 个状态里少了 49 个常用状态）。
    /// 正确的判据是看基类链里有没有 <c>System.Enum</c>。
    /// </summary>
    public bool IsEnum => DerivesFrom("Enum");

    /// <summary>这个枚举的常量值（不是枚举就是空）。</summary>
    public IReadOnlyDictionary<string, int> EnumValues { get; }
}

/// <summary>
/// 只读 **元数据** 扫 sts2.dll（不加载程序集）。
///
/// 为什么不能用 <c>Assembly.LoadFrom(dll).GetTypes()</c>：
///   · 它要求把 9700+ 个类型**全部**解析出来，缺依赖（GodotSharp / 0Harmony / 版本对不上）时
///     会抛 <c>ReflectionTypeLoadException</c>（实测只能拿到 35/9760 个非 null 类型）；
///   · 更糟的是 .NET 9 上它**直接把栈打爆**（<c>RuntimeModule.GetDefinedTypes()</c> 递归枚举嵌套类型），
///     实测在本机 sts2.dll（9760 个类型）上 100% 触发 <c>StackOverflowException</c> ——
///     那是 CLR 的致命错误，**catch 不住**，整个进程当场死。用户实测过「读不到效果库、生成被拦死」。
/// 元数据读取不解析任何依赖，所以既不会抛也不会崩，还快得多。
/// </summary>
public static class Sts2Metadata
{
    private const string PowerBase = "PowerModel";
    private const string CardBase = "CardModel";
    private const string PotionBase = "PotionModel";

    /// <summary>读一次缓存（同一个 dll 反复问不用反复解析）。</summary>
    private static readonly Dictionary<string, IReadOnlyList<Sts2TypeInfo>> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>最近一次失败原因（界面上的「重新扫描效果库」会把它显示出来）。</summary>
    public static string? LastError { get; private set; }

    /// <summary>把 sts2.dll 里所有类型读出来（失败返回空列表并把原因写进 <see cref="LastError"/>）。</summary>
    public static IReadOnlyList<Sts2TypeInfo> ReadTypes(string? dllPath)
    {
        LastError = null;
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath)) { LastError = "sts2.dll 不存在"; return Array.Empty<Sts2TypeInfo>(); }
        if (Cache.TryGetValue(dllPath, out var hit)) return hit;
        try
        {
            var list = ReadCore(dllPath);
            Cache[dllPath] = list;
            return list;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + "：" + ex.Message;
            return Array.Empty<Sts2TypeInfo>();
        }
    }

    /// <summary>派生自某个基类（按类名比，忽略命名空间）的全部类型。</summary>
    public static IReadOnlyList<Sts2TypeInfo> DerivedFrom(string? dllPath, string baseName) =>
        Filter(ReadTypes(dllPath), baseName);

    public static IReadOnlyList<Sts2TypeInfo> Powers(string? dllPath) => DerivedFrom(dllPath, PowerBase);
    public static IReadOnlyList<Sts2TypeInfo> Cards(string? dllPath) => DerivedFrom(dllPath, CardBase);
    public static IReadOnlyList<Sts2TypeInfo> Potions(string? dllPath) => DerivedFrom(dllPath, PotionBase);

    private static IReadOnlyList<Sts2TypeInfo> Filter(IReadOnlyList<Sts2TypeInfo> all, string baseName)
    {
        var list = new List<Sts2TypeInfo>();
        foreach (var t in all)
        {
            if (t.IsEnum) continue;
            if (t.Name.StartsWith("Mock", StringComparison.Ordinal)) continue;
            if (t.Name.Contains('<')) continue;                 // 编译器生成的闭包类
            if (t.DerivesFrom(baseName)) list.Add(t);
        }
        return list;
    }

    private static List<Sts2TypeInfo> ReadCore(string dllPath)
    {
        using var fs = File.OpenRead(dllPath);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();

        // ① 先把每个 TypeDef 的父级记下来（可能指向另一个 TypeDef，也可能是指向程序集外的 TypeRef）
        var defs = new Dictionary<int, (string Name, string Ns, int ParentRow, string? ParentName)>();
        foreach (var h in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(h);
            string name = md.GetString(td.Name);
            string ns = md.GetString(td.Namespace);
            int parentRow = 0;
            string? parentName = null;
            var bt = td.BaseType;
            if (bt.Kind == HandleKind.TypeDefinition)
            {
                parentRow = MetadataTokens.GetRowNumber((TypeDefinitionHandle)bt);
            }
            else if (bt.Kind == HandleKind.TypeReference)
            {
                var tr = md.GetTypeReference((TypeReferenceHandle)bt);
                parentName = md.GetString(tr.Name);
            }
            defs[MetadataTokens.GetRowNumber(h)] = (name, ns, parentRow, parentName);
        }

        // ② 枚举常量值（本体有些开关是 enum，例如 PowerType）；先按类型收集 field 常量
        var enumValues = new Dictionary<int, Dictionary<string, int>>();
        foreach (var h in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(h);
            int row = MetadataTokens.GetRowNumber(h);
            Dictionary<string, int>? vals = null;
            foreach (var fh in td.GetFields())
            {
                var f = md.GetFieldDefinition(fh);
                string fname = md.GetString(f.Name);
                if (fname == "value__") continue;                  // 枚举的实例字段
                if ((f.Attributes & System.Reflection.FieldAttributes.Literal) == 0) continue;
                try
                {
                    var cHandle = f.GetDefaultValue();
                    if (cHandle.IsNil) continue;
                    var constant = md.GetConstant(cHandle);
                    var reader = md.GetBlobReader(constant.Value);
                    int v = reader.Length switch
                    {
                        1 => reader.ReadByte(),
                        2 => reader.ReadInt16(),
                        4 => reader.ReadInt32(),
                        8 => (int)reader.ReadInt64(),
                        _ => 0,
                    };
                    (vals ??= new Dictionary<string, int>(StringComparer.Ordinal))[fname] = v;
                }
                catch { /* 读不到就算了 */ }
            }
            if (vals is not null) enumValues[row] = vals;
        }

        // ③ 组装（父级链可能指向外部的 TypeRef，那一段就用它的类名收尾）
        var result = new List<Sts2TypeInfo>(defs.Count);
        foreach (var pair in defs)
        {
            var (name, ns, parentRow, parentName) = pair.Value;
            var chain = new List<string>();
            int cur = parentRow;
            int guard = 0;
            while (cur != 0 && guard++ < 64)
            {
                if (!defs.TryGetValue(cur, out var p)) break;
                chain.Add(p.Name);
                cur = p.ParentRow;
                parentName = p.ParentName;
            }
            if (parentName is not null) chain.Add(parentName);     // 程序集外的那一层（System.Object 之类）
            var ev = enumValues.TryGetValue(pair.Key, out var v2) ? v2 : new Dictionary<string, int>();
            result.Add(new Sts2TypeInfo(name, ns, chain, ev));
        }
        return result;
    }
}
