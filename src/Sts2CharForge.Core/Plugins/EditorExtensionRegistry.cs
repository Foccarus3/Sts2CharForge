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
        public Host? Host;
        public List<IEditorPage> Pages = new();
        public AssemblyLoadContext? Alc;
    }

    private static readonly Dictionary<string, Entry> _byPlugin = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>V0.2.388：面板工厂（由界面层注入；Core 不引用 WPF）。返回 null = 当前没有界面环境。</summary>
    public static Func<string, IEditorPanel?>? PanelFactory;

    /// <summary>V0.2.392：生成前的确认回调（界面层注入：显示文件清单，用户点「确定」返回 true）。null = 没有界面环境，不写盘。</summary>
    public static Func<string, EditorGeneratePlan, bool>? ConfirmGenerate;

    /// <summary>所有已知插件的扩展状态（没声明扩展的也在里面，Summary 会写「无编辑器扩展」）。</summary>
    public static IReadOnlyList<EditorExtensionStatus> All()
        => _byPlugin.Values.Select(v => v.Status).OrderBy(s => s.PluginId, StringComparer.OrdinalIgnoreCase).ToList();

    public static EditorExtensionStatus? StatusOf(string pluginId)
        => _byPlugin.TryGetValue(pluginId ?? "", out var e) ? e.Status : null;

    /// <summary>V0.2.394：记一条异常到那条扩展的状态里（审核页显示「最近一次异常」）。</summary>
    public static void RecordError(string pluginId, string message)
    {
        if (_byPlugin.TryGetValue(pluginId ?? "", out var e)) e.Status.LastError = message ?? "";
    }

    /// <summary>V0.2.394：记一次生成输出摘要（审核页显示「最近一次生成输出」）。</summary>
    /// <summary>V0.2.399：这个插件目前有哪些存储（名字 / 版本 / 有没有落盘）—— 插件详情用。</summary>
    public static IReadOnlyList<(string Name, int Version, bool Exists)> StoresOf(string pluginId)
    {
        var list = new List<(string, int, bool)>();
        foreach (var e in _byPlugin.Values)
        {
            if (!string.Equals(e.Status.PluginId, pluginId ?? "", StringComparison.OrdinalIgnoreCase)) continue;
            if (e.Host is null) continue;
            foreach (var kv in e.Host.Stores)
            {
                try
                {
                    var t = kv.Value.GetType();
                    int ver = (int)(t.GetProperty("SchemaVersion")?.GetValue(kv.Value) ?? 0);
                    bool exists = (bool)(t.GetProperty("Exists")?.GetValue(kv.Value) ?? false);
                    list.Add((kv.Key, ver, exists));
                }
                catch { }
            }
        }
        return list;
    }

    /// <summary>
    /// V0.2.399：对某个存储做一次「重新读盘 + 迁移 + 写回」。
    /// 返回 (旧版本, 新版本, 错误)；插件没注册迁移钩子时新版本 == 旧版本（不会假装迁过）。
    /// </summary>
    public static (int Old, int New, string Error) MigrateStore(string pluginId, string name)
    {
        foreach (var e in _byPlugin.Values)
        {
            if (!string.Equals(e.Status.PluginId, pluginId ?? "", StringComparison.OrdinalIgnoreCase)) continue;
            if (e.Host is null || !e.Host.Stores.TryGetValue((name ?? "").Trim(), out object? store) || store is null)
                return (0, 0, "找不到这个存储");
            try
            {
                var t = store.GetType();
                int before = (int)(t.GetProperty("SchemaVersion")?.GetValue(store) ?? 0);
                t.GetMethod("Reload")?.Invoke(store, null);
                int after = (int)(t.GetProperty("SchemaVersion")?.GetValue(store) ?? 0);
                t.GetMethod("Save")?.Invoke(store, null);
                return (before, after, "");
            }
            catch (Exception ex)
            {
                return (0, 0, ex.InnerException?.Message ?? ex.Message);
            }
        }
        return (0, 0, "这个插件没有加载成功的扩展");
    }

    public static void RecordGenerate(string pluginId, string summary)
    {
        if (_byPlugin.TryGetValue(pluginId ?? "", out var e)) e.Status.LastGenerate = summary ?? "";
    }

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
            entry.Host = host;
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

        /// <summary>V0.2.391：插件自有数据存储 —— 路径锁死在插件目录下的 data/ 里，出不去。</summary>
        public IPluginStore<T>? GetStore<T>(string name) where T : class, new()
        {
            string safe = (name ?? "").Trim();
            if (safe.Length == 0) return null;
            foreach (char bad in Path.GetInvalidFileNameChars()) if (safe.Contains(bad)) return null;
            if (safe.Contains("..", StringComparison.Ordinal)) return null;
            if (PluginDir.Length == 0) return null;
            var store = new FilePluginStore<T>(Path.Combine(PluginDir, "data", safe + ".json"));
            Stores[safe] = store;
            return store;
        }

        /// <summary>V0.2.392：先出计划（不写盘）。路径越界 / 空文件名会记进 Problems。</summary>
        public EditorGeneratePlan PlanGenerate(IReadOnlyList<EditorOutputFile> files)
        {
            var plan = new EditorGeneratePlan { PluginId = PluginId, OutputDir = OutputDir };
            foreach (var f in files ?? Array.Empty<EditorOutputFile>())
            {
                if (f is null || string.IsNullOrWhiteSpace(f.Path))
                {
                    plan.Problems.Add("有一个文件没写路径");
                    continue;
                }
                string rel = f.Path.Trim().Replace('\\', '/');
                if (rel.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(rel) || rel.Contains(":" , StringComparison.Ordinal))
                {
                    plan.Problems.Add("路径越界，已拒绝：" + f.Path);
                    continue;
                }
                if (f.IsAsset)
                {
                    string src = Path.GetFullPath(Path.Combine(PluginDir, f.AssetPath));
                    string root2 = Path.GetFullPath(PluginDir);
                    if (!src.StartsWith(root2, StringComparison.OrdinalIgnoreCase) || !File.Exists(src))
                    {
                        plan.Problems.Add("资源不存在或越界：" + f.AssetPath);
                        continue;
                    }
                    plan.Files.Add(new EditorOutputFile { Path = rel, AssetPath = f.AssetPath });
                    continue;
                }
                plan.Files.Add(new EditorOutputFile { Path = rel, Content = f.Content ?? "" });
            }
            if (plan.Files.Count == 0 && plan.Problems.Count == 0) plan.Problems.Add("没有任何文件要生成");
            return plan;
        }

        /// <summary>V0.2.392：确认后写盘；先写 .tmp 再逐个替换，避免留下半成品。</summary>
        public EditorGenerateResult Generate(IReadOnlyList<EditorOutputFile> files)
        {
            var plan = PlanGenerate(files);
            var result = new EditorGenerateResult { Problems = new List<string>(plan.Problems) };
            if (!plan.Ok) return result;
            if (EditorExtensionRegistry.ConfirmGenerate is null)
            {
                result.Problems.Add("当前没有界面环境可以确认生成（已取消）");
                return result;
            }
            bool ok = false;
            try { ok = EditorExtensionRegistry.ConfirmGenerate(PluginId, plan); }
            catch (Exception ex) { result.Problems.Add("确认生成时出错：" + ex.Message); return result; }
            if (!ok) return result;
            result.Confirmed = true;
            var staged = new List<(string tmp, string final)>();
            try
            {
                foreach (var f in plan.Files)
                {
                    string full = Path.GetFullPath(Path.Combine(OutputDir, f.Path));
                    string root = Path.GetFullPath(OutputDir);
                    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Problems.Add("路径越界，已跳过：" + f.Path);
                        continue;
                    }
                    string? dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    string tmp = full + ".tmp";
                    if (f.IsAsset)
                    {
                        string srcFile = Path.GetFullPath(Path.Combine(PluginDir, f.AssetPath));
                        if (!File.Exists(srcFile))
                        {
                            result.Problems.Add("资源不存在，已跳过：" + f.AssetPath);
                            continue;
                        }
                        File.Copy(srcFile, tmp, overwrite: true);
                    }
                    else
                    {
                        File.WriteAllText(tmp, f.Content, new System.Text.UTF8Encoding(false));
                    }
                    staged.Add((tmp, full));
                }
                foreach (var pair in staged)
                {
                    if (File.Exists(pair.final)) File.Delete(pair.final);
                    File.Move(pair.tmp, pair.final);
                    result.Written.Add(pair.final);
                }
            }
            catch (Exception ex)
            {
                result.Problems.Add("写文件失败：" + ex.Message);
                EditorExtensionRegistry.RecordError(PluginId, "写文件失败：" + ex.Message);
                foreach (var pair in staged) { try { if (File.Exists(pair.tmp)) File.Delete(pair.tmp); } catch { } }
            }
            EditorExtensionRegistry.RecordGenerate(PluginId, result.Summary);
            return result;
        }

        /// <summary>V0.2.395：把资源复制进插件自己的 assets/；名字非法 / 源不存在 → 返回空串。</summary>
        public string ImportAsset(string sourcePath, string fileName)
        {
            try
            {
                string src = (sourcePath ?? "").Trim();
                string name = (fileName ?? "").Trim();
                if (src.Length == 0 || name.Length == 0) return "";
                if (!File.Exists(src)) return "";
                if (PluginDir.Length == 0) return "";
                foreach (char bad in Path.GetInvalidFileNameChars()) if (name.Contains(bad)) return "";
                if (name.Contains("..", StringComparison.Ordinal) || name.Contains('/' ) || name.Contains('\\')) return "";
                string dir = Path.Combine(PluginDir, "assets");
                Directory.CreateDirectory(dir);
                string dest = Path.Combine(dir, name);
                File.Copy(src, dest, overwrite: true);
                return "assets/" + name;
            }
            catch (Exception ex)
            {
                EditorExtensionRegistry.RecordError(PluginId, "导入资源失败：" + ex.Message);
                return "";
            }
        }

        private readonly Dictionary<string, Dictionary<string, string>> _refs = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>V0.2.399：发出的存储（名字 → store 实例），插件详情据此显示版本并提供「迁移」。</summary>
        internal readonly Dictionary<string, object> Stores = new(StringComparer.OrdinalIgnoreCase);


        /// <summary>V0.2.398：登记可引用对象（kind → id → 显示名）。</summary>
        public void RegisterRefs(string kind, IReadOnlyList<EditorRefItem> items)
        {
            string k = (kind ?? "").Trim();
            if (k.Length == 0) return;
            if (!_refs.TryGetValue(k, out var map)) { map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); _refs[k] = map; }
            foreach (var it in items ?? Array.Empty<EditorRefItem>())
            {
                if (it is null || string.IsNullOrWhiteSpace(it.Id)) continue;
                map[it.Id.Trim()] = string.IsNullOrWhiteSpace(it.Label) ? it.Id.Trim() : it.Label.Trim();
            }
            if (_byPlugin.TryGetValue(PluginId, out var e)) e.Status.RefCount = _refs.Values.Sum(m => m.Count);
        }

        /// <summary>V0.2.398：查引用。</summary>
        public bool TryResolveRef(string kind, string id, out string label)
        {
            label = "";
            string k = (kind ?? "").Trim(), key = (id ?? "").Trim();
            if (k.Length == 0 || key.Length == 0) return false;
            if (!_refs.TryGetValue(k, out var map)) return false;
            if (!map.TryGetValue(key, out string? v)) return false;
            label = v ?? "";
            return true;
        }

        /// <summary>V0.2.398：插件自己发现的问题（写进审核信息，用户看得见）。</summary>
        public void ReportProblem(string message)
        {
            if (_byPlugin.TryGetValue(PluginId, out var e))
            {
                string m = (message ?? "").Trim();
                if (m.Length == 0) return;
                e.Status.LastError = m;
                e.Status.ProblemCount++;
            }
        }

        public void Log(string message)
        {
            if (_byPlugin.TryGetValue(PluginId, out var e)) e.Status.LastLog = message ?? "";
        }
    }
}
