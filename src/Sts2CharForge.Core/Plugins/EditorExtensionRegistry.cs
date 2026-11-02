using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Sts2CharForge.Core.Plugins;

/// <summary>
/// V0.2.387（需求书第 4.1 / 6 节）：编辑器扩展的**受控加载器**。
///
/// <para>做四件事：</para>
/// <list type="bullet">
///   <item>只有 plugin.json 里显式声明 <c>editorExtension</c> 的插件才会被加载（老插件完全不受影响）；</item>
///   <item>程序集**只能**从该插件自己的目录加载（独立 <see cref="AssemblyLoadContext"/>，可卸载）；</item>
///   <item>入口类型必须实现 <see cref="ICharForgeEditorExtension"/>，API 版本不兼容就拒绝并给出明确提示；</item>
///   <item>所有调用点（加载 / 建页面）都 try/catch 隔离：插件抛异常只让它自己失败，主程序不崩、别的插件不受影响。</item>
/// </list>
/// </summary>
public static class EditorExtensionRegistry
{
    /// <summary>目前支持的扩展 API 版本。</summary>
    public const string SupportedApiVersion = "1";

    private sealed class Entry
    {
        public EditorExtensionStatus Status = new();
        public ICharForgeEditorExtension? Ext;
        public List<IEditorPage> Pages = new();
        public AssemblyLoadContext? Alc;
    }

    private static readonly Dictionary<string, Entry> _byPlugin = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>V0.2.388：面板工厂（由界面层注入；Core 不引用 WPF）。返回 null = 当前没有界面环境。</summary>
    public static Func<string, IEditorPanel?>? PanelFactory;

    /// <summary>所有已知插件的扩展状态（没声明扩展的也在里面，Summary 会写「无编辑器扩展」）。</summary>
    public static IReadOnlyList<EditorExtensionStatus> All()
        => _byPlugin.Values.Select(v => v.Status).OrderBy(s => s.PluginId, StringComparer.OrdinalIgnoreCase).ToList();

    public static EditorExtensionStatus? StatusOf(string pluginId)
        => _byPlugin.TryGetValue(pluginId ?? "", out var e) ? e.Status : null;

    /// <summary>这个插件已经建好的页面（没加载成功 = 空）。</summary>
    public static IReadOnlyList<IEditorPage> PagesOf(string pluginId)
        => _byPlugin.TryGetValue(pluginId ?? "", out var e) ? e.Pages : Array.Empty<IEditorPage>();

    public static void SetDisabled(string pluginId, bool disabled)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return;
        if (disabled) _disabled.Add(pluginId); else _disabled.Remove(pluginId);
        if (_byPlugin.TryGetValue(pluginId, out var e)) e.Status.Disabled = disabled;
    }

    public static bool IsDisabled(string pluginId) => _disabled.Contains(pluginId ?? "");

    /// <summary>清掉全部缓存（重载插件 / 自检用）。</summary>
    public static void Reset()
    {
        foreach (var e in _byPlugin.Values)
        {
            try { foreach (var p in e.Pages) p.Dispose(); } catch { /* 插件自己的 Dispose 抛异常也不能影响主程序 */ }
            try { e.Alc?.Unload(); } catch { }
        }
        _byPlugin.Clear();
    }

    /// <summary>
    /// 加载一个插件的编辑器扩展（幂等：重复调用会先卸掉旧的）。
    /// <paramref name="pluginDir"/> = 插件目录；<paramref name="spec"/> = plugin.json 里的那段声明。
    /// </summary>
    public static EditorExtensionStatus Load(PluginSpec plugin, string pluginDir, string outputDir)
    {
        string id = (plugin?.Id ?? "").Trim();
        if (id.Length == 0) id = Path.GetFileName(pluginDir ?? "");
        var status = new EditorExtensionStatus
        {
            PluginId = id,
            Declared = plugin?.EditorExtension is { Ok: true },
            Entry = (plugin?.EditorExtension?.Entry ?? "").Trim(),
            ApiVersion = (plugin?.EditorExtension?.ApiVersion ?? "").Trim(),
        };
        var entry = new Entry { Status = status };
        _byPlugin[id] = entry;

        if (!status.Declared)
        {
            status.Error = "";
            return status;
        }
        if (_disabled.Contains(id))
        {
            status.Disabled = true;
            return status;
        }

        // ① API 版本（空 = 按 1 处理）
        string want = status.ApiVersion.Length == 0 ? SupportedApiVersion : status.ApiVersion;
        if (!string.Equals(want, SupportedApiVersion, StringComparison.Ordinal))
        {
            status.Error = $"扩展 API 版本 {want} 与当前支持的 {SupportedApiVersion} 不兼容";
            return status;
        }

        // ② 解析入口类型：先用 Type.GetType（内置 / 已被引用的程序集），再只从**插件目录**加载
        Type? type = null;
        try
        {
            type = Type.GetType(status.Entry, throwOnError: false);
            // 已经加载的宿主程序集里也找一遍（插件可能把入口放在主程序集 / 已被引用的库里；
            // Type.GetType 在「程序集名不完全匹配」时会返回 null，这里按类型名兜一层）
            if (type is null)
            {
                string typeName = ExtractTypeName(status.Entry);
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t2 = asm.GetType(typeName, throwOnError: false);
                        if (t2 is not null) { type = t2; break; }
                    }
                    catch { }
                }
            }
            if (type is null)
            {
                string asmName = ExtractAssemblyName(status.Entry);
                if (asmName.Length == 0) { status.Error = "入口写法不对：应为「命名空间.类型, 程序集名」"; return status; }
                string asmPath = FindAssemblyPath(pluginDir ?? "", asmName);
                if (asmPath.Length == 0)
                {
                    status.Error = $"插件目录里找不到程序集 {asmName}.dll";
                    return status;
                }
                var alc = new AssemblyLoadContext("plugin:" + id, isCollectible: true);
                entry.Alc = alc;
                type = alc.LoadFromAssemblyPath(asmPath).GetType(ExtractTypeName(status.Entry), throwOnError: false);
            }
        }
        catch (Exception ex)
        {
            status.Error = "加载入口程序集失败：" + ex.Message;
            return status;
        }
        if (type is null)
        {
            status.Error = "找不到入口类型 " + status.Entry;
            return status;
        }
        if (!typeof(ICharForgeEditorExtension).IsAssignableFrom(type))
        {
            status.Error = "入口类型没有实现 ICharForgeEditorExtension";
            return status;
        }

        // ③ 实例化 + 建页面（全部 try/catch 隔离）
        try
        {
            var ext = (ICharForgeEditorExtension)Activator.CreateInstance(type)!;
            string extApi = (ext.ApiVersion ?? "").Trim();
            if (extApi.Length > 0 && !string.Equals(extApi, SupportedApiVersion, StringComparison.Ordinal))
            {
                status.Error = $"插件自报 API 版本 {extApi} 与 {SupportedApiVersion} 不兼容";
                return status;
            }
            entry.Ext = ext;
            var host = new Host(id, pluginDir ?? "", outputDir ?? "");
            var pages = ext.CreatePages(host) ?? Array.Empty<IEditorPage>();
            entry.Pages = pages.Where(p => p is not null).ToList();
            status.PageCount = entry.Pages.Count;
            status.Loaded = true;
        }
        catch (Exception ex)
        {
            status.Error = "扩展初始化失败：" + ex.GetType().Name + "：" + ex.Message;
            status.Loaded = false;
            entry.Pages.Clear();
        }
        return status;
    }

    private static string ExtractAssemblyName(string entry)
    {
        int at = (entry ?? "").IndexOf(',');
        return at < 0 ? "" : entry[(at + 1)..].Trim();
    }

    private static string ExtractTypeName(string entry)
    {
        int at = (entry ?? "").IndexOf(',');
        return at < 0 ? (entry ?? "").Trim() : entry[..at].Trim();
    }

    /// <summary>只在插件目录（含其子目录）里找程序集 —— 这是「程序集必须来自插件目录」那条审核要求。</summary>
    private static string FindAssemblyPath(string pluginDir, string asmName)
    {
        try
        {
            if (!Directory.Exists(pluginDir)) return "";
            foreach (string f in Directory.EnumerateFiles(pluginDir, asmName + ".dll", SearchOption.AllDirectories))
                return Path.GetFullPath(f);
        }
        catch { }
        return "";
    }

    /// <summary>受控宿主：插件只能看到 id / 目录 / 输出目录 / 写日志。</summary>
    private sealed class Host : IEditorHost
    {
        public Host(string id, string dir, string outDir) { PluginId = id; PluginDir = dir; OutputDir = outDir; }
        public string PluginId { get; }
        public string PluginDir { get; }
        public string OutputDir { get; }
        public IEditorPanel? CreatePanel() => PanelFactory?.Invoke(PluginId);

        public void Log(string message)
        {
            if (_byPlugin.TryGetValue(PluginId, out var e)) e.Status.LastLog = message ?? "";
        }
    }
}
