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

    /// <summary>
    /// 这个类型上有没有「方法名 + 形参个数」都对得上的方法（V0.2.167：构建前核对生成端**硬编码**的签名）。
    ///
    /// <para>为什么需要它：生成端有几处把本体方法的签名写死了（例：攻击牌一律写
    /// <c>DamageCmd.Attack(…).FromCard(this, cardPlay)</c>）。游戏一更新、签名变了，生成出来的代码就在
    /// **编译期**炸（用户实测报过 <c>CS1501 FromCard 方法没有采用 2 个参数的重载</c>、
    /// <c>CS0535/CS0115 ITemporaryPower</c> 那一批）—— 报错信息完全看不出是「游戏版本不对」。</para>
    ///
    /// <para>⚠ 返回三态：<c>true</c> = 有；<c>false</c> = **类型在、方法或重载不在**（明确的漂移）；<c>null</c> =
    /// dll 读不到 / 类型都没找到（不下结论，调用方别据此报错）。只读元数据，不加载程序集。</para>
    /// </summary>
    public static bool? HasMethod(string? dllPath, string typeSimpleName, string methodName, int paramCount)
    {
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath)) return null;
        try
        {
            using var fs = File.OpenRead(dllPath);
            using var pe = new System.Reflection.PortableExecutable.PEReader(fs);
            var md = pe.GetMetadataReader();
            bool sawType = false;
            foreach (var th in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(th);
                if (!string.Equals(md.GetString(td.Name), typeSimpleName, StringComparison.Ordinal)) continue;
                sawType = true;
                foreach (var mh in td.GetMethods())
                {
                    var m = md.GetMethodDefinition(mh);
                    if (!string.Equals(md.GetString(m.Name), methodName, StringComparison.Ordinal)) continue;
                    // 形参数 = 参数表的行数（不用解码签名，够用了）
                    if (m.GetParameters().Count == paramCount) return true;
                }
                // ⚠ 这里**不能**直接 return false：本体里同名类型可能不止一个
                //（换了命名空间、或某个 mock / 内部类也叫这个名字），
                // 取第一个就下结论会把「其实有」错判成「漂移」→ 用户明明版本对却被拦住。
                // 继续扫完所有同名类型，全都对不上才算漂移。
            }
            return sawType ? false : null;   // 类型在、但没一个重载对得上 → 明确的漂移
        }
        catch { /* 读不出来就当「不知道」 */ }
        return null;
    }

    /// <summary>
    /// 某个类型下某个方法的**所有重载**（形参类型串，例：<c>FromCard(CardModel, CardPlay?)</c>）。
    /// 给「本体签名漂移」那条报错用：直接把用户那份 dll 里真实的签名打出来，
    /// 一眼就能看出本体改成了什么，不用来回问「你的 dll 发我一下」。
    /// 读不到就返回空表（调用方别据此下结论）。
    /// </summary>
    public static IReadOnlyList<string> MethodOverloads(string? dllPath, string typeSimpleName, string methodName)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath)) return list;
        try
        {
            using var fs = File.OpenRead(dllPath);
            using var pe = new System.Reflection.PortableExecutable.PEReader(fs);
            var md = pe.GetMetadataReader();
            foreach (var th in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(th);
                if (!string.Equals(md.GetString(td.Name), typeSimpleName, StringComparison.Ordinal)) continue;
                foreach (var mh in td.GetMethods())
                {
                    var m = md.GetMethodDefinition(mh);
                    if (!string.Equals(md.GetString(m.Name), methodName, StringComparison.Ordinal)) continue;
                    var sig = m.DecodeSignature(new SigNames(), null);
                    string ps = string.Join(", ", sig.ParameterTypes);
                    string one = $"{md.GetString(m.Name)}({ps})";
                    if (!list.Contains(one)) list.Add(one);
                }
            }
        }
        catch { /* 读不出来就算了 */ }
        return list;
    }

    /// <summary>把签名里的类型名解成短名（给 <see cref="MethodOverloads"/> 用；不做完整解析，够读就行）。</summary>
    private sealed class SigNames : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte raw) =>
            r.GetString(r.GetTypeDefinition(h).Name);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte raw) =>
            r.GetString(r.GetTypeReference(h).Name);
        public string GetTypeFromSpecification(MetadataReader r, object? g, TypeSpecificationHandle h, byte raw) => "?";
        public string GetSZArrayType(string e) => e + "[]";
        public string GetArrayType(string e, ArrayShape s) => e + "[…]";
        public string GetByReferenceType(string e) => "ref " + e;
        public string GetPointerType(string e) => e + "*";
        public string GetGenericInstantiation(string g, System.Collections.Immutable.ImmutableArray<string> a) =>
            g + "<" + string.Join(", ", a) + ">";
        public string GetGenericMethodParameter(object? g, int i) => "!!" + i;
        public string GetGenericTypeParameter(object? g, int i) => "!" + i;
        public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
        public string GetModifiedType(string m, string u, bool r) => u;
        public string GetPinnedType(string e) => e;
    }

    /// <summary>
    /// 本工具是照着哪个游戏版本写的（生成端把一些本体方法签名写死了，只在标明过的版本上保证对得上）。
    /// 游戏更新后这个数字要跟着改 —— 改了之后 <see cref="VersionMismatch"/> 才会对旧版游戏报提示。
    /// </summary>
    public const string SupportedGameVersion = "v0.111.0";

    /// <summary>
    /// 读游戏目录里的 <c>release_info.json</c> 拿本体版本（形如 <c>v0.111.0</c>）；读不到返回 null。
    /// 用户报「版本不匹配」时，光看这一处签名抽查容易一头雾水 —— 有这个就能直接把两边版本都报出来。
    /// </summary>
    public static string? GameVersion(string? gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir)) return null;
        try
        {
            string f = Path.Combine(gameDir, "release_info.json");
            if (!File.Exists(f)) return null;
            string text = File.ReadAllText(f);
            var m = System.Text.RegularExpressions.Regex.Match(text, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 游戏版本和工具支持的版本是不是**前两位**对不上（<c>0.111.x</c> 之间的小修订不算）。
    /// 任一边解析不出来就返回 false —— 不确定的时候不打扰用户。
    /// </summary>
    public static bool VersionMismatch(string? gameVersion, string? supported = null)
    {
        static int[]? Parts(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            string s = v.Trim().TrimStart('v', 'V');
            var bits = s.Split('.');
            if (bits.Length < 2) return null;
            if (!int.TryParse(bits[0], out int a) || !int.TryParse(bits[1], out int b)) return null;
            return new[] { a, b };
        }
        var g = Parts(gameVersion);
        var t = Parts(supported ?? SupportedGameVersion);
        if (g is null || t is null) return false;
        return g[0] != t[0] || g[1] != t[1];
    }

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
