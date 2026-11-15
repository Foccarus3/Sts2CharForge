using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2CharForge.Core.Plugins;

/// <summary>
/// 插件市场（MVP）：只读静态索引 + 下载 zip + 安全解压安装。
///
/// <para>索引放在 GitHub 仓库的 <c>market/index.json</c>，插件包放 <c>market/packages/</c> 或 Release。
/// 生成器不执行插件代码，只解压到 <c>plugins/&lt;id&gt;/</c>；下次重新加载插件才会读清单。</para>
///
/// <para>上传不在客户端里做（避免把任意代码 / 账号体系塞进 WPF）；作者走 GitHub PR，
/// CI 校验 <c>market/index.json</c> 后合并，见 <c>market/README.md</c>。</para>
/// </summary>
public static class PluginMarketplace
{
    /// <summary>默认索引地址（仓库里的静态文件；可被界面上的自定义 URL 覆盖）。</summary>
    public const string DefaultIndexUrl =
        "https://raw.githubusercontent.com/Foccarus3/Sts2CharForge/main/market/index.json";

    /// <summary>插件包上限（防止市场被拿来分发大文件）。</summary>
    public const long MaxPackageBytes = 25L * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Sts2CharForge-Market/1.0");
        return c;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string PluginsDirOf(string programDir) => Path.Combine(programDir, "plugins");

    /// <summary>下载并解析市场索引。</summary>
    public static async Task<MarketIndex> LoadIndexAsync(string? url = null, CancellationToken ct = default)
    {
        url = string.IsNullOrWhiteSpace(url) ? DefaultIndexUrl : url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new MarketplaceException("市场地址必须是 http(s) URL。");
        try
        {
            using var resp = await Http.GetAsync(uri, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new MarketplaceException($"市场索引下载失败：HTTP {(int)resp.StatusCode}");
            await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var index = await JsonSerializer.DeserializeAsync<MarketIndex>(s, JsonOpts, ct).ConfigureAwait(false)
                        ?? throw new MarketplaceException("市场索引是空文件。");
            index.Plugins ??= new List<MarketPlugin>();
            foreach (var p in index.Plugins) p?.Normalize();
            return index;
        }
        catch (MarketplaceException) { throw; }
        catch (Exception ex) { throw new MarketplaceException("市场索引读取失败：" + ex.Message, ex); }
    }

    /// <summary>
    /// 下载并安装一个插件版本。默认拒绝覆盖已存在的插件目录；
    /// 用户要更新请先卸载 / 手动删目录，避免覆盖本地修改。
    /// </summary>
    public static async Task<MarketInstallResult> InstallAsync(
        MarketPlugin plugin, MarketVersion? version, string programDir, CancellationToken ct = default)
    {
        plugin.Normalize();
        version ??= plugin.Latest;
        if (string.IsNullOrWhiteSpace(plugin.Id) || version is null)
            throw new MarketplaceException("插件索引缺少 id 或版本。");
        if (!IsSafeId(plugin.Id))
            throw new MarketplaceException($"插件 id「{plugin.Id}」不合法。");
        if (!Uri.TryCreate(version.DownloadUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new MarketplaceException("插件下载地址必须是 http(s) URL。");

        string pluginsDir = PluginsDirOf(programDir);
        string target = Path.Combine(pluginsDir, plugin.Id);
        if (Directory.Exists(target))
            throw new MarketplaceException($"插件「{plugin.Name}」已经安装（{target}）。请先卸载或手动删掉旧目录。");

        Directory.CreateDirectory(pluginsDir);
        string tempZip = Path.Combine(Path.GetTempPath(), "sts2cf_market_" + Guid.NewGuid().ToString("N") + ".zip");
        string tempDir = Path.Combine(Path.GetTempPath(), "sts2cf_market_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode)
                    throw new MarketplaceException($"插件下载失败：HTTP {(int)resp.StatusCode}");
                long? len = resp.Content.Headers.ContentLength;
                if (len is > MaxPackageBytes) throw new MarketplaceException("插件包超过 25 MB，已拒绝。");
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = File.Create(tempZip);
                await CopyCappedAsync(src, dst, MaxPackageBytes, ct).ConfigureAwait(false);
            }

            string actualHash = await Sha256Async(tempZip, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(version.Sha256)
                && !string.Equals(actualHash, version.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new MarketplaceException($"SHA256 校验失败（索引 {version.Sha256}，实际 {actualHash}）。");

            Directory.CreateDirectory(tempDir);
            ExtractSafe(tempZip, tempDir);

            string? manifest = FindManifest(tempDir);
            if (manifest is null) throw new MarketplaceException("插件包里找不到 plugin.json。");
            string sourceRoot = Path.GetDirectoryName(manifest)!;
            string json = await File.ReadAllTextAsync(manifest, ct).ConfigureAwait(false);
            string? id;
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            }
            catch (Exception ex) { throw new MarketplaceException("plugin.json 解析失败：" + ex.Message, ex); }

            if (!string.Equals(id?.Trim(), plugin.Id, StringComparison.OrdinalIgnoreCase))
                throw new MarketplaceException($"插件包 id「{id}」和索引「{plugin.Id}」不一致，已拒绝。");

            // 从临时目录搬进 plugins/<id>；同一磁盘直接 Move，跨盘退回复制。
            try { Directory.Move(sourceRoot, target); }
            catch (IOException)
            {
                CopyDirectory(sourceRoot, target);
                Directory.Delete(sourceRoot, recursive: true);
            }
            return new MarketInstallResult(plugin.Id, plugin.Name, version.Version, target, actualHash);
        }
        finally
        {
            TryDeleteFile(tempZip);
            TryDeleteDir(tempDir);
        }
    }

    /// <summary>已安装插件的目录（不解析清单，只判断目录）。</summary>
    public static string? InstalledDir(string programDir, string id)
    {
        if (!IsSafeId(id)) return null;
        string dir = Path.Combine(PluginsDirOf(programDir), id);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>删掉一个已安装插件目录（只删 plugins/&lt;id&gt; 这一层）。</summary>
    public static bool Uninstall(string programDir, string id)
    {
        string? dir = InstalledDir(programDir, id);
        if (dir is null) return false;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    public static bool IsSafeId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 64
        && id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    private static string? FindManifest(string dir)
    {
        // 包可以是「plugin.json 在根」或「一个顶层文件夹里」；取路径最短的那个。
        return Directory.GetFiles(dir, "plugin.json", SearchOption.AllDirectories)
            .Select(f => (File: f, Depth: f[(dir.Length + 1)..].Count(ch => ch is '\\' or '/')))
            .Where(x => x.Depth <= 1)
            .OrderBy(x => x.Depth)
            .Select(x => x.File)
            .FirstOrDefault();
    }

    private static async Task CopyCappedAsync(Stream src, Stream dst, long cap, CancellationToken ct)
    {
        byte[] buf = new byte[81920];
        long total = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            total += n;
            if (total > cap) throw new MarketplaceException("插件包超过 25 MB，已拒绝。");
            await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
        }
    }

    private static void ExtractSafe(string zipPath, string destDir)
    {
        string root = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > 2000) throw new MarketplaceException("插件包文件数超过 2000，已拒绝。");
        long totalBytes = 0;
        foreach (var entry in zip.Entries)
        {
            string rel = entry.FullName.Replace('\\', '/');
            if (rel.Length == 0 || rel.EndsWith("/", StringComparison.Ordinal)) continue;
            totalBytes += entry.Length;
            if (totalBytes > 100L * 1024 * 1024)
                throw new MarketplaceException("插件解压后超过 100 MB，已拒绝。");
            if (rel.StartsWith("/", StringComparison.Ordinal) || rel.Contains("..", StringComparison.Ordinal))
                throw new MarketplaceException($"插件包含非法路径：{entry.FullName}");
            string ext = Path.GetExtension(rel).ToLowerInvariant();
            if (ext is ".dll" or ".exe" or ".bat" or ".cmd" or ".ps1" or ".vbs" or ".scr")
                throw new MarketplaceException($"插件包不允许携带可执行文件：{entry.FullName}");
            string full = Path.GetFullPath(Path.Combine(destDir, rel));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new MarketplaceException($"插件包含路径穿越：{entry.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, overwrite: true);
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(src, f);
            string to = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to, overwrite: true);
        }
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var s = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(s, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteFile(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
    private static void TryDeleteDir(string p) { try { if (Directory.Exists(p)) Directory.Delete(p, true); } catch { } }
}

public sealed class MarketIndex
{
    public int SchemaVersion { get; set; } = 1;
    public List<MarketPlugin>? Plugins { get; set; } = new();
}

public sealed class MarketPlugin
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string License { get; set; } = "";
    public int ApiVersion { get; set; } = 2;
    public string MinGeneratorVersion { get; set; } = "";
    public string Homepage { get; set; } = "";
    public List<string>? Categories { get; set; } = new();
    public List<string>? Keywords { get; set; } = new();
    public List<MarketVersion>? Versions { get; set; } = new();

    public MarketVersion? Latest
    {
        get
        {
            Normalize();
            return Versions!.OrderByDescending(v => VersionKey(v.Version)).FirstOrDefault();
        }
    }

    public void Normalize()
    {
        Versions ??= new List<MarketVersion>();
        Versions.RemoveAll(v => v is null);
        foreach (var v in Versions) v.Normalize();
        Id = (Id ?? "").Trim();
        Name = (Name ?? "").Trim();
        Author = (Author ?? "").Trim();
        Description = (Description ?? "").Trim();
        License = (License ?? "").Trim();
        MinGeneratorVersion = (MinGeneratorVersion ?? "").Trim();
        Homepage = (Homepage ?? "").Trim();
        Categories = (Categories ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Keywords = (Keywords ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string VersionKey(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "000000000";
        var nums = v.Trim().TrimStart('v', 'V').Split('.', '-', '+')
            .Select(x => int.TryParse(x, out int n) ? n : 0).Take(4).ToArray();
        return string.Join(".", nums.Select(n => n.ToString("D6")));
    }
}

public sealed class MarketVersion
{
    public string Version { get; set; } = "";
    [JsonPropertyName("download")]
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Notes { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string PublishedAt { get; set; } = "";

    public void Normalize()
    {
        Version = (Version ?? "").Trim();
        DownloadUrl = (DownloadUrl ?? "").Trim();
        Sha256 = (Sha256 ?? "").Trim();
        Notes = (Notes ?? "").Trim();
        GameVersion = (GameVersion ?? "").Trim();
        PublishedAt = (PublishedAt ?? "").Trim();
    }
}

public sealed record MarketInstallResult(string Id, string Name, string Version, string Path, string Sha256);

public sealed class MarketplaceException : Exception
{
    public MarketplaceException(string message) : base(message) { }
    public MarketplaceException(string message, Exception inner) : base(message, inner) { }
}
