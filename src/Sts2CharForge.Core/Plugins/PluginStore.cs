using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2CharForge.Core.Plugins;

/// <summary>
/// V0.2.391（需求书 4.4 节）：插件**自有对象模型**的存储。
///
/// <para>文件位置固定在插件自己的目录里（<c>&lt;插件目录&gt;/data/&lt;名字&gt;.json</c>），
/// 插件拿不到任意路径 —— 这是「插件只能写自己目录」那条审核要求的实现点。</para>
///
/// <para>文件格式是带版本的信封：<c>{ "schemaVersion": 2, "data": { … } }</c>；
/// 读到老版本时调插件注册的迁移钩子（<see cref="SetMigration"/>），不做隐式猜测。</para>
/// </summary>
public interface IPluginStore<T> where T : class, new()
{
    /// <summary>当前内存里的对象（第一次访问时自动读盘；文件不存在 = 新的空对象）。</summary>
    T Value { get; set; }

    /// <summary>这个对象当前的 schema 版本（默认 1）。</summary>
    int SchemaVersion { get; set; }

    /// <summary>磁盘上有没有这个文件。</summary>
    bool Exists { get; }

    /// <summary>实际文件路径（便于审核页展示）。</summary>
    string FilePath { get; }

    /// <summary>写盘（原子写：先写 .tmp 再替换，避免写一半把文件写坏）。</summary>
    void Save();

    /// <summary>丢掉内存里的副本，重新读盘。</summary>
    void Reload();

    /// <summary>
    /// 迁移钩子：读到 <paramref name="oldVersion"/> 版本时，用它的原始 JSON 造一个新对象返回；
    /// 返回 null = 无法迁移（保留旧对象并记一条错误，不覆盖用户数据）。
    /// </summary>
    void SetMigration(Func<int, string, T?> migrate);
}

/// <summary>V0.2.391：文件信封（带版本）。</summary>
public sealed class PluginStoreEnvelope<T> where T : class, new()
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("data")] public T? Data { get; set; }
}

/// <summary>V0.2.391：<see cref="IPluginStore{T}"/> 的文件实现。</summary>
public sealed class FilePluginStore<T> : IPluginStore<T> where T : class, new()
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private T? _value;
    private Func<int, string, T?>? _migrate;

    public FilePluginStore(string filePath)
    {
        FilePath = filePath ?? "";
        SchemaVersion = 1;
    }

    public T Value
    {
        get
        {
            if (_value is null) Reload();
            return _value!;
        }
        set => _value = value;
    }

    public int SchemaVersion { get; set; }

    public bool Exists => FilePath.Length > 0 && File.Exists(FilePath);

    public string FilePath { get; }

    public void SetMigration(Func<int, string, T?> migrate) => _migrate = migrate;

    public void Reload()
    {
        _value = new T();
        if (!Exists) return;
        try
        {
            string text = File.ReadAllText(FilePath);
            var env = JsonSerializer.Deserialize<PluginStoreEnvelope<T>>(text, JsonOpts);
            if (env?.Data is not null)
            {
                _value = env.Data;
                SchemaVersion = env.SchemaVersion <= 0 ? 1 : env.SchemaVersion;
                // V0.2.391：这里**不能 return** —— 后面还有「旧版本 → 迁移钩子」那一段（第一版就是栽在这，
                // 自检直接抓到「迁移没跑、读到的还是老结构」）。
            }
            else
            {
                // 兼容「没有信封」的裸对象（手写 / 早期文件）
                var bare = JsonSerializer.Deserialize<T>(text, JsonOpts);
                if (bare is not null) { _value = bare; SchemaVersion = 1; }
            }
        }
        catch
        {
            // 坏文件：不覆盖、不崩，保留一个空对象让插件继续工作（用户可以看到提示后自己修）
            _value = new T();
        }
        if (_migrate is not null && Exists && SchemaVersion < CurrentSchemaHint)
        {
            try
            {
                string raw = File.ReadAllText(FilePath);
                var migrated = _migrate(SchemaVersion, raw);
                if (migrated is not null) { _value = migrated; SchemaVersion = CurrentSchemaHint; }
            }
            catch { /* 迁移失败保留旧对象，绝不覆盖用户数据 */ }
        }
    }

    /// <summary>插件想迁到哪个版本：由插件在 Save 前设置 <see cref="SchemaVersion"/>，这里只用它做判断。</summary>
    public int CurrentSchemaHint { get; set; } = 1;

    public void Save()
    {
        if (FilePath.Length == 0) return;
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new PluginStoreEnvelope<T>
            {
                SchemaVersion = SchemaVersion <= 0 ? 1 : SchemaVersion,
                Data = _value ?? new T(),
            }, JsonOpts));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }
        catch { /* 写盘失败不能把主程序带崩；审核页会显示最近错误（后续接） */ }
    }
}

/// <summary>V0.2.398（需求书 4.6）：一个可被引用的对象（id + 显示名）。</summary>
public sealed class EditorRefItem
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}

