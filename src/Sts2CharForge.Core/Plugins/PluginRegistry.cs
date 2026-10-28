using System.Text.Json;
using System.Text.Json.Serialization;
using Sts2CharForge.Core.Effects;

namespace Sts2CharForge.Core.Plugins;

/// <summary>
/// 一条「效果种类」插件（V0.2.315）。
///
/// <para>插件只用**数据 + 代码模板**声明，不需要编译成 DLL —— 这样编辑器升级（甚至换 .NET 版本）后，
/// 老插件照样能被读出来（需求②「插件不会因编辑器的更新而失效」）。</para>
///
/// <para><see cref="Emit"/> 是一段 C# 模板，生成时会替换占位符（见 <c>PluginGen</c>）：
/// <c>{amount}</c> / <c>{self}</c> / <c>{player}</c> / <c>{foe}</c> / <c>{choice}</c> /
/// <c>{cardSelf}</c> / <c>{power}</c>。模板里可以写多行。</para>
/// </summary>
public sealed class PluginEffectSpec
{
    /// <summary>种类 id（英文，生成代码/CET 标记里都用它）。</summary>
    public string Kind { get; set; } = "";

    /// <summary>界面上的中文名。</summary>
    public string Display { get; set; } = "";

    /// <summary>数值单位（「点」/「张」/「层」…）。</summary>
    public string Unit { get; set; } = "点";

    public decimal Min { get; set; }

    public decimal Max { get; set; } = 99m;

    /// <summary>要不要「作用对象」那一栏（自己 / 指定敌人 / 全体敌人…）。</summary>
    public bool NeedsTarget { get; set; }

    /// <summary>要不要「增益 / 减益」那一栏（会替换 <c>{power}</c>）。</summary>
    public bool NeedsPower { get; set; }

    /// <summary>要不要「目标卡」那一栏（会替换 <c>{card}</c>）。</summary>
    public bool NeedsCard { get; set; }

    /// <summary>生成出来的 C# 模板（占位符见类注释）。</summary>
    public string Emit { get; set; } = "";

    /// <summary>一句话说明（编辑器里显示）。</summary>
    public string Note { get; set; } = "";
}

/// <summary>触发时机插件（本版**只登记 + 显示**，生成端的接线在下一版；见 <see cref="PluginPlugins"/> 的说明）。</summary>
public sealed class PluginTriggerSpec
{
    public string Id { get; set; } = "";
    public string Display { get; set; } = "";

    /// <summary>挂在哪个本体钩子上（必须是编辑器已知的钩子 id，如 <c>DamageDealt</c>）。</summary>
    public string Hook { get; set; } = "";

    /// <summary>额外的进门守卫（C# 片段，可为空）。</summary>
    public string Guard { get; set; } = "";

    public bool HasChoiceContext { get; set; } = true;
    public string Note { get; set; } = "";
}

/// <summary>条件插件（本版只登记 + 显示）。</summary>
public sealed class PluginConditionSpec
{
    public string Id { get; set; } = "";
    public string Display { get; set; } = "";
    public string Expr { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>锻造模块插件（本版只登记 + 显示）。</summary>
public sealed class PluginForgeModuleSpec
{
    public string Kind { get; set; } = "";
    public string Display { get; set; } = "";
    public string Emit { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>额外内容页：插件自带的一页 Markdown（编辑器「插件」页里按插件分栏显示）。</summary>
public sealed class PluginPageSpec
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>正文（Markdown 片段；编辑器按纯文本 + 简单缩进显示）。</summary>
    public string Markdown { get; set; } = "";
}

/// <summary>一个插件的清单（<c>plugins/&lt;目录&gt;/plugin.json</c>）。</summary>
public sealed class PluginSpec
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string ApiVersion { get; set; } = "1";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>插件自述（Markdown）；编辑器「插件」页会显示。</summary>
    public string Readme { get; set; } = "";

    public List<PluginEffectSpec> Effects { get; set; } = new();
    public List<PluginTriggerSpec> Triggers { get; set; } = new();
    public List<PluginConditionSpec> Conditions { get; set; } = new();
    public List<PluginForgeModuleSpec> ForgeModules { get; set; } = new();
    public List<PluginPageSpec> Pages { get; set; } = new();

    /// <summary>
    /// 要一起打包的 C# 文件（相对插件目录的路径，如 <c>cs/MyHelper.cs</c>）。
    /// 生成工程时原样拷到 <c>cs/Plugins/&lt;插件 id&gt;/</c> 下 —— 于是它和 mod 一起编译、一起打包。
    /// </summary>
    public List<string> CsFiles { get; set; } = new();

    /// <summary>要一起打包的其它资源（相对路径，拷到工程根的 <c>plugin_assets/&lt;插件 id&gt;/</c>）。</summary>
    public List<string> AssetFiles { get; set; } = new();

    /// <summary>运行时填的：清单文件所在目录（不参与序列化）。</summary>
    [JsonIgnore] public string Dir { get; set; } = "";

    /// <summary>运行时填的：加载时发现的问题（不参与序列化）。</summary>
    [JsonIgnore] public List<string> Problems { get; } = new();

    [JsonIgnore] public string Display => string.IsNullOrWhiteSpace(Version) ? Name : $"{Name} V{Version}";
}

/// <summary>
/// 插件注册表（V0.2.315）：扫描 <c>&lt;程序目录&gt;/plugins/*/plugin.json</c>，把插件里的
/// 效果种类 / 触发时机 / 条件 / 锻造模块 / 额外页汇总起来给界面和生成端用。
///
/// <para><b>为什么用「数据 + 模板」而不是 DLL</b>：DLL 会绑死编辑器的内部类型，编辑器一升级就加载失败
/// （需求②明确要求不能这样）。这里只认 JSON 清单里的**字符串**，编辑器内部改了实现也照样读得出来。</para>
///
/// <para><b>互相调用同类内容（需求①）</b>：插件里的 <c>Emit</c> 模板可以写任何本体的 API，
/// 也可以调用插件自己 <see cref="PluginSpec.CsFiles"/> 里带的辅助类；编辑器自带的那 65 种效果
/// 用的也是同一套本体 API，所以两边生成的代码可以混在同一个效果列表里。</para>
///
/// <para><b>随 mod 打包（需求③）</b>：<see cref="PluginSpec.CsFiles"/> 会被拷进生成工程
/// <c>cs/Plugins/&lt;id&gt;/</c>，<see cref="PluginSpec.AssetFiles"/> 拷进 <c>plugin_assets/&lt;id&gt;/</c>，
/// 都在 mod 的工程目录里，构建/打包时自然带上。</para>
/// </summary>
public static class PluginRegistry
{
    /// <summary>当前支持的清单版本（插件写 <c>apiVersion</c>；看不懂的一律拒绝并说明）。</summary>
    public const string ApiVersion = "1";

    private static readonly List<PluginSpec> _plugins = new();
    private static readonly List<EffectKindOption> _effectKinds = new();
    private static readonly Dictionary<string, PluginEffectSpec> _effectsById = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>加载到的插件（按目录名排序，结果稳定）。</summary>
    public static IReadOnlyList<PluginSpec> Plugins => _plugins;

    /// <summary>插件贡献的效果种类（合并到界面的「效果种类」下拉里）。</summary>
    public static IReadOnlyList<EffectKindOption> EffectKinds => _effectKinds;

    /// <summary>这个种类是不是插件给的。</summary>
    public static bool IsPluginEffect(string? kind) =>
        kind is not null && _effectsById.ContainsKey(kind.Trim());

    /// <summary>插件贡献的效果规格（拿模板 / 字段开关用）；不是插件的返回 null。</summary>
    public static PluginEffectSpec? EffectOf(string? kind) =>
        kind is not null && _effectsById.TryGetValue(kind.Trim(), out var e) ? e : null;

    /// <summary>插件目录（相对程序目录）：<c>plugins</c>。</summary>
    public static string PluginsDirOf(string appDir) => Path.Combine(appDir, "plugins");

    /// <summary>上一次扫描的时间（界面显示用）。</summary>
    public static string LastScan { get; private set; } = "";

    /// <summary>
    /// 扫描并重新加载所有插件（幂等）。返回加载到的插件数。
    ///
    /// <para>容错原则：**任何一个插件坏掉都不影响其它插件，更不影响编辑器本身** ——
    /// 坏的那个记进 <see cref="PluginSpec.Problems"/>、界面里标红，其余照常。</para>
    /// </summary>
    public static int Load(string appDir)
    {
        _plugins.Clear();
        _effectKinds.Clear();
        _effectsById.Clear();
        LastScan = DateTime.Now.ToString("HH:mm:ss");

        string dir = PluginsDirOf(appDir);
        if (!Directory.Exists(dir)) return 0;

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifests = new List<string>();
        try
        {
            // ① 每个插件一个子目录：plugins/<名字>/plugin.json
            foreach (string sub in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string m = Path.Combine(sub, "plugin.json");
                if (File.Exists(m)) manifests.Add(m);
            }
            // ② 也允许直接摊在 plugins/ 下：plugins/<名字>.json
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                manifests.Add(file);
        }
        catch (Exception e)
        {
            return 0;   // 目录都读不了：当作没有插件（不抛，编辑器照常开）
        }

        foreach (string path in manifests)
        {
            PluginSpec? spec = null;
            try
            {
                string json = File.ReadAllText(path);
                spec = JsonSerializer.Deserialize<PluginSpec>(json, JsonOpts);
            }
            catch (Exception e)
            {
                spec = new PluginSpec { Id = Path.GetFileNameWithoutExtension(path), Name = Path.GetFileNameWithoutExtension(path) };
                spec.Problems.Add("plugin.json 解析失败：" + e.Message);
            }
            if (spec is null) continue;
            spec.Dir = Path.GetDirectoryName(path) ?? dir;
            if (string.IsNullOrWhiteSpace(spec.Id)) spec.Id = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(spec.Name)) spec.Name = spec.Id;

            if (!string.Equals((spec.ApiVersion ?? "").Trim(), ApiVersion, StringComparison.OrdinalIgnoreCase))
                spec.Problems.Add($"清单版本 apiVersion = 「{spec.ApiVersion}」不受支持（本编辑器支持 {ApiVersion}）—— 这个插件不会被加载。");
            if (!seenIds.Add(spec.Id)) spec.Problems.Add($"插件 id「{spec.Id}」和前面某个插件重复 —— 这个插件不会被加载。");

            if (spec.Problems.Count > 0) { _plugins.Add(spec); continue; }

            LoadOne(spec);
            _plugins.Add(spec);
        }
        return _plugins.Count(p => p.Problems.Count == 0);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>把一个（已通过校验的）插件的内容登记进注册表。</summary>
    private static void LoadOne(PluginSpec spec)
    {
        foreach (var fx in spec.Effects)
        {
            if (fx is null) continue;
            string kind = (fx.Kind ?? "").Trim();
            if (kind.Length == 0) { spec.Problems.Add("有一条效果没填 kind。"); continue; }
            if (!IsValidKind(kind)) { spec.Problems.Add($"效果 kind「{kind}」不合法（英文/数字/下划线，且以字母开头）。"); continue; }
            if (EffectCatalog.EffectKinds.Any(k => string.Equals(k.Kind, kind, StringComparison.OrdinalIgnoreCase))
                || _effectsById.ContainsKey(kind))
            {
                spec.Problems.Add($"效果 kind「{kind}」和已有的种类重名 —— 跳过这一条。");
                continue;
            }
            if (string.IsNullOrWhiteSpace(fx.Emit)) { spec.Problems.Add($"效果「{kind}」没写 emit 模板。"); continue; }
            _effectsById[kind] = fx;
            _effectKinds.Add(new EffectKindOption(
                Kind: kind,
                Display: string.IsNullOrWhiteSpace(fx.Display) ? kind : fx.Display.Trim(),
                Unit: string.IsNullOrWhiteSpace(fx.Unit) ? "点" : fx.Unit,
                Min: fx.Min, Max: fx.Max, NeedsTarget: fx.NeedsTarget));
        }

        // 触发时机 / 条件 / 锻造模块：本版**只登记 + 显示**（生成端接线下一版做）。
        // 这里只做重名检查，避免插件之间互相踩。
        foreach (var tr in spec.Triggers.Where(x => x is not null))
        {
            if (string.IsNullOrWhiteSpace(tr.Id)) spec.Problems.Add("有一条触发时机没填 id。");
            else if (!IsValidKind(tr.Id)) spec.Problems.Add($"触发时机 id「{tr.Id}」不合法。");
            else if (string.IsNullOrWhiteSpace(tr.Hook)) spec.Problems.Add($"触发时机「{tr.Id}」没填 hook（要挂在本体哪个钩子上）。");
        }
        foreach (var c in spec.Conditions.Where(x => x is not null))
        {
            if (string.IsNullOrWhiteSpace(c.Id)) spec.Problems.Add("有一条条件没填 id。");
            else if (!IsValidKind(c.Id)) spec.Problems.Add($"条件 id「{c.Id}」不合法。");
            else if (string.IsNullOrWhiteSpace(c.Expr)) spec.Problems.Add($"条件「{c.Id}」没写 expr 表达式。");
        }
        foreach (var m in spec.ForgeModules.Where(x => x is not null))
        {
            if (string.IsNullOrWhiteSpace(m.Kind)) spec.Problems.Add("有一个锻造模块没填 kind。");
            else if (!IsValidKind(m.Kind)) spec.Problems.Add($"锻造模块 kind「{m.Kind}」不合法。");
            else if (string.IsNullOrWhiteSpace(m.Emit)) spec.Problems.Add($"锻造模块「{m.Kind}」没写 emit 模板。");
        }

        // 自带文件必须在插件目录里、且真的存在（越界路径直接拒绝，避免手滑删到别处）
        foreach (var f in spec.CsFiles.Concat(spec.AssetFiles).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string full = Path.GetFullPath(Path.Combine(spec.Dir, f.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Path.GetFullPath(spec.Dir), StringComparison.OrdinalIgnoreCase))
                spec.Problems.Add($"自带文件「{f}」跑到插件目录外面了 —— 不允许。");
            else if (!File.Exists(full))
                spec.Problems.Add($"自带文件「{f}」不存在。");
        }
    }

    private static bool IsValidKind(string kind)
    {
        if (kind.Length == 0 || !char.IsLetter(kind[0])) return false;
        return kind.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    /// <summary>插件贡献的额外页（id / 标题 / 正文 + 所属插件名）。</summary>
    public static IEnumerable<(PluginSpec Plugin, PluginPageSpec Page)> AllPages =>
        _plugins.Where(p => p.Problems.Count == 0)
                .SelectMany(p => p.Pages.Where(x => x is not null).Select(x => (p, x)));
}
