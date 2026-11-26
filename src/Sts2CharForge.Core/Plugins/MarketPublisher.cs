using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sts2CharForge.Core.Plugins;

/// <summary>
/// 维护者本机用的市场发布器：读第三方插件 ZIP、改 plugin.json、去脚本、重打包、写索引。
/// 不联网、不执行插件代码；上传只走本地 git（见 <see cref="CommitAndPush"/>）。
/// </summary>
public static class MarketPublisher
{
    public sealed record PackageInfo(
        string ZipPath,
        string Id,
        string Name,
        string Author,
        string Version,
        string Description,
        string License,
        IReadOnlyList<string> ScriptFiles);

    public sealed class PublishRequest
    {
        public string SourceZip { get; set; } = "";
        public string MarketRepo { get; set; } = "";
        /// <summary>下载 URL 前缀，例如 https://raw.githubusercontent.com/.../main/market/packages</summary>
        public string RawBaseUrl { get; set; } = "";
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public string License { get; set; } = "";
        public string Notes { get; set; } = "";
        public string GameVersion { get; set; } = "v0.114.x";
        public string MinGeneratorVersion { get; set; } = "";
        public List<string> Categories { get; set; } = new();
        public List<string> Keywords { get; set; } = new();
    }

    public sealed record PublishResult(
        string PackagePath,
        string IndexPath,
        string Sha256,
        string Id,
        string Version,
        string DownloadUrl,
        bool NewPlugin,
        IReadOnlyList<string> RemovedScripts);

    public sealed record GitResult(bool Success, string Output);

    public sealed record DeleteResult(
        string Id,
        string? Version,
        bool PluginRemoved,
        IReadOnlyList<string> DeletedPackages);

    public static string MarketDirOf(string repo) => Path.Combine(repo, "market");
    public static string PackagesDirOf(string repo) => Path.Combine(MarketDirOf(repo), "packages");
    public static string IndexPathOf(string repo) => Path.Combine(MarketDirOf(repo), "index.json");

    /// <summary>读取本地市场索引（不联网）。</summary>
    public static MarketIndex ReadIndex(string repo)
    {
        string path = IndexPathOf(repo);
        if (!File.Exists(path)) throw new MarketPublishException("找不到市场索引：" + path);
        var index = JsonSerializer.Deserialize<MarketIndex>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        }) ?? throw new MarketPublishException("index.json 是空文件。");
        index.Plugins ??= new List<MarketPlugin>();
        foreach (var p in index.Plugins) p?.Normalize();
        return index;
    }

    /// <summary>只读 ZIP 里的 plugin.json 和脚本列表，不改任何文件。</summary>
    public static PackageInfo Inspect(string zipPath)
    {
        if (!File.Exists(zipPath)) throw new MarketPublishException("ZIP 不存在：" + zipPath);
        using var zip = ZipFile.OpenRead(zipPath);
        var manifest = FindManifest(zip) ?? throw new MarketPublishException("ZIP 里找不到 plugin.json。");
        string json = ReadText(zip, manifest);
        var obj = ParseObject(json);
        var scripts = zip.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Where(IsScript)
            .ToList();
        return new PackageInfo(
            zipPath,
            Str(obj, "id"), Str(obj, "name"), Str(obj, "author"), Str(obj, "version"),
            Str(obj, "description"), Str(obj, "license"), scripts);
    }

    /// <summary>
    /// 生成市场包 + 更新索引。包内 plugin.json 的 id/name/author/version/description 会按请求改写；
    /// license 只写进索引（避免给老 plugin.json 增加未知字段）。
    /// </summary>
    public static PublishResult Publish(PublishRequest req)
    {
        Normalize(req);
        Validate(req);
        string packages = PackagesDirOf(req.MarketRepo);
        Directory.CreateDirectory(packages);
        string fileName = req.Id + "-" + req.Version + ".zip";
        string outZip = Path.Combine(packages, fileName);
        string indexPath = IndexPathOf(req.MarketRepo);
        if (!File.Exists(indexPath)) throw new MarketPublishException("找不到市场索引：" + indexPath);
        if (File.Exists(outZip)) throw new MarketPublishException("目标包已存在，拒绝覆盖：" + outZip);

        string tempRoot = Path.Combine(Path.GetTempPath(), "sts2cf_publish_" + Guid.NewGuid().ToString("N"));
        string backupDir = Path.Combine(req.MarketRepo, "market", "_inbox");
        Directory.CreateDirectory(backupDir);
        try
        {
            Directory.CreateDirectory(tempRoot);
            ExtractSafe(req.SourceZip, tempRoot);
            string? manifest = Directory.GetFiles(tempRoot, "plugin.json", SearchOption.AllDirectories)
                .OrderBy(f => f.Length).FirstOrDefault();
            if (manifest is null) throw new MarketPublishException("解压后找不到 plugin.json。");
            string sourceRoot = Path.GetDirectoryName(manifest)!;

            var obj = ParseObject(File.ReadAllText(manifest));
            Set(obj, "id", req.Id);
            Set(obj, "name", req.Name);
            Set(obj, "author", req.Author);
            Set(obj, "version", req.Version);
            Set(obj, "description", req.Description);
            File.WriteAllText(manifest,
                obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
                new UTF8Encoding(false));

            var removed = new List<string>();
            foreach (string f in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(sourceRoot, f).Replace('\\', '/');
                if (IsScript(rel))
                {
                    File.Delete(f);
                    removed.Add(rel);
                    continue;
                }
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".dll" or ".exe" or ".bat" or ".cmd" or ".ps1" or ".vbs" or ".scr")
                    throw new MarketPublishException("插件包含不允许的文件：" + rel);
            }

            string backup = Path.Combine(backupDir,
                req.Id + "-" + req.Version + "-original-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".zip");
            File.Copy(req.SourceZip, backup, overwrite: false);

            ZipFile.CreateFromDirectory(sourceRoot, outZip, CompressionLevel.Optimal, includeBaseDirectory: false);
            long packageBytes = new FileInfo(outZip).Length;
            if (packageBytes > 5L * 1024 * 1024)
                throw new MarketPublishException($"市场包 {packageBytes / 1024 / 1024.0:0.0} MB 超过 5 MB；请改用 GitHub Release 手动上传，或精简资源。");
            string sha = Sha256(outZip);
            string download = req.RawBaseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(fileName);
            bool isNew = UpdateIndex(indexPath, req, fileName, download, sha);

            return new PublishResult(outZip, indexPath, sha, req.Id, req.Version, download, isNew, removed);
        }
        catch
        {
            try { if (File.Exists(outZip)) File.Delete(outZip); } catch { }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }

    /// <summary>
    /// 从市场索引删除一个插件或一个版本，并删除本地 market/packages 里对应的包。
    /// 不会操作 GitHub Release；外部下载地址的包需要另外去 Release 删除。
    /// </summary>
    public static DeleteResult Delete(string repo, string id, string? version, bool deleteWholePlugin)
    {
        id = (id ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64
            || !id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'))
            throw new MarketPublishException("id 只能包含字母 / 数字 / _ / -，最长 64。");
        string indexPath = IndexPathOf(repo);
        if (!File.Exists(indexPath)) throw new MarketPublishException("找不到市场索引：" + indexPath);
        string original = File.ReadAllText(indexPath);
        var root = JsonNode.Parse(original) as JsonObject
                   ?? throw new MarketPublishException("index.json 不是 JSON 对象。");
        var plugins = root["plugins"] as JsonArray
                      ?? throw new MarketPublishException("index.json 里没有 plugins 数组。");
        JsonObject? target = null;
        foreach (var n in plugins)
            if (n is JsonObject o && string.Equals(Str(o, "id"), id, StringComparison.OrdinalIgnoreCase))
            { target = o; break; }
        if (target is null) throw new MarketPublishException($"市场里找不到插件「{id}」。");

        var versions = target["versions"] as JsonArray ?? new JsonArray();
        var packages = new List<string>();
        bool pluginRemoved;

        if (deleteWholePlugin)
        {
            foreach (var v in versions)
                if (v is JsonObject vo) CollectPackage(repo, vo, packages);
            plugins.Remove(target);
            // 索引里可能已经丢过版本；把 packages 里同 id 的孤儿包也一并清掉
            string packagesDir = PackagesDirOf(repo);
            if (Directory.Exists(packagesDir))
                foreach (string f in Directory.GetFiles(packagesDir, id + "-*.zip"))
                    packages.Add(f);
            pluginRemoved = true;
        }
        else
        {
            version = (version ?? "").Trim().TrimStart('v', 'V');
            if (version.Length == 0) throw new MarketPublishException("删除指定版本时必须填版本号。");
            JsonObject? hit = null;
            foreach (var v in versions)
                if (v is JsonObject vo && string.Equals(Str(vo, "version"), version, StringComparison.OrdinalIgnoreCase))
                { hit = vo; break; }
            if (hit is null) throw new MarketPublishException($"插件「{id}」没有版本「{version}」。");
            CollectPackage(repo, hit, packages);
            versions.Remove(hit);
            if (versions.Count == 0)
            {
                plugins.Remove(target);
                pluginRemoved = true;
            }
            else
            {
                target["versions"] = versions;
                pluginRemoved = false;
            }
        }

        string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        File.WriteAllText(indexPath + ".bak", original, new UTF8Encoding(false));
        File.WriteAllText(indexPath, json, new UTF8Encoding(false));

        var deleted = new List<string>();
        foreach (string p in packages.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(p)) { File.Delete(p); deleted.Add(p); }
            }
            catch { /* 索引已经移除；包删不掉最多留一个无引用文件 */ }
        }
        return new DeleteResult(id, deleteWholePlugin ? null : version, pluginRemoved, deleted);
    }

    private static void CollectPackage(string repo, JsonObject version, List<string> into)
    {
        string download = Str(version, "download");
        if (!Uri.TryCreate(download, UriKind.Absolute, out var uri)) return;
        string name = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(name)
            || !string.Equals(Path.GetExtension(name), ".zip", StringComparison.OrdinalIgnoreCase))
            return;
        string path = Path.Combine(PackagesDirOf(repo), name);
        if (File.Exists(path)) into.Add(path);
    }

    /// <summary>只 stage 指定包和索引，不改别的文件。</summary>
    public static GitResult CommitAndPush(string repo, IEnumerable<string> files, string message)
    {
        var rel = files.Select(f => Path.GetRelativePath(repo, f)).ToList();
        var add = RunGit(repo, new[] { "add", "--" }.Concat(rel).ToArray());
        if (!add.Success) return add;
        var commit = RunGit(repo, new[] { "commit", "-m", message });
        if (!commit.Success) return commit;
        var push = RunGit(repo, new[] { "push" });
        return push.Success ? new GitResult(true, add.Output + "\n" + commit.Output + "\n" + push.Output)
                            : push;
    }

    public static string Sha256(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    private static GitResult RunGit(string repo, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = repo,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new MarketPublishException("无法启动 git。");
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(120000);
            string output = (stdout + "\n" + stderr).Trim();
            return new GitResult(p.ExitCode == 0, output.Length == 0 ? "(git 无输出)" : output);
        }
        catch (Exception ex) { return new GitResult(false, "git 执行失败：" + ex.Message); }
    }

    private static bool UpdateIndex(string indexPath, PublishRequest req, string fileName, string download, string sha)
    {
        string original = File.ReadAllText(indexPath);
        var root = JsonNode.Parse(original) as JsonObject
                   ?? throw new MarketPublishException("index.json 不是 JSON 对象。");
        var plugins = root["plugins"] as JsonArray ?? new JsonArray();
        if (root["plugins"] is null) root["plugins"] = plugins;

        bool isNew = true;
        JsonObject? existing = null;
        foreach (var n in plugins)
            if (n is JsonObject o && string.Equals(Str(o, "id"), req.Id, StringComparison.OrdinalIgnoreCase))
            { existing = o; isNew = false; break; }

        var versions = existing?["versions"] as JsonArray ?? new JsonArray();
        if (existing?["versions"] is null) { if (existing is not null) existing["versions"] = versions; }
        foreach (var v in versions)
            if (v is JsonObject vo && string.Equals(Str(vo, "version"), req.Version, StringComparison.OrdinalIgnoreCase))
                throw new MarketPublishException($"版本 {req.Version} 已经发布过，拒绝覆盖。请提升版本号。");

        var ver = new JsonObject
        {
            ["version"] = req.Version,
            ["download"] = download,
            ["sha256"] = sha,
            ["notes"] = req.Notes,
            ["gameVersion"] = req.GameVersion,
            ["publishedAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };
        versions.Insert(0, ver);

        if (existing is null)
        {
            existing = new JsonObject
            {
                ["id"] = req.Id,
                ["name"] = req.Name,
                ["author"] = req.Author,
                ["description"] = req.Description,
                ["license"] = req.License,
                ["apiVersion"] = 2,
                ["minGeneratorVersion"] = req.MinGeneratorVersion,
                ["homepage"] = "",
                ["categories"] = ToArray(req.Categories),
                ["keywords"] = ToArray(req.Keywords),
                ["versions"] = versions,
            };
            plugins.Add(existing);
        }
        else
        {
            existing["name"] = req.Name;
            existing["author"] = req.Author;
            existing["description"] = req.Description;
            existing["license"] = req.License;
            existing["minGeneratorVersion"] = req.MinGeneratorVersion;
            existing["categories"] = ToArray(req.Categories);
            existing["keywords"] = ToArray(req.Keywords);
            existing["versions"] = versions;
        }

        string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        File.WriteAllText(indexPath + ".bak", original, new UTF8Encoding(false));
        File.WriteAllText(indexPath, json, new UTF8Encoding(false));
        return isNew;
    }

    private static JsonArray ToArray(List<string> items) =>
        new(items.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => (JsonNode?)JsonValue.Create(x.Trim())).ToArray());

    private static void Normalize(PublishRequest r)
    {
        r.SourceZip = (r.SourceZip ?? "").Trim();
        r.MarketRepo = (r.MarketRepo ?? "").Trim().TrimEnd('\\', '/');
        r.RawBaseUrl = (r.RawBaseUrl ?? "").Trim().TrimEnd('/');
        r.Id = (r.Id ?? "").Trim();
        r.Name = (r.Name ?? "").Trim();
        r.Author = (r.Author ?? "").Trim();
        r.Version = (r.Version ?? "").Trim().TrimStart('v', 'V');
        r.Description = (r.Description ?? "").Trim();
        r.License = (r.License ?? "").Trim();
        r.Notes = (r.Notes ?? "").Trim();
        r.GameVersion = string.IsNullOrWhiteSpace(r.GameVersion) ? "v0.114.x" : r.GameVersion.Trim();
        r.MinGeneratorVersion = string.IsNullOrWhiteSpace(r.MinGeneratorVersion) ? "V0.3.74" : r.MinGeneratorVersion.Trim();
        r.Categories = (r.Categories ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
        r.Keywords = (r.Keywords ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
    }

    private static void Validate(PublishRequest r)
    {
        if (!File.Exists(r.SourceZip)) throw new MarketPublishException("源 ZIP 不存在。");
        if (!Directory.Exists(r.MarketRepo)) throw new MarketPublishException("市场仓库目录不存在。");
        if (r.Id.Length == 0 || r.Id.Length > 64 || !r.Id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'))
            throw new MarketPublishException("id 只能包含字母 / 数字 / _ / -，最长 64。");
        if (r.Version.Length == 0 || !r.Version.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '+'))
            throw new MarketPublishException("版本号不合法。");
        if (r.Name.Length == 0) throw new MarketPublishException("名称不能为空。");
        if (r.Author.Length == 0) throw new MarketPublishException("作者不能为空。");
        if (r.License.Length == 0) throw new MarketPublishException("许可证不能为空。");
        if (r.Categories.Count > 4) throw new MarketPublishException("分类最多选 4 个。");
        if (r.Keywords.Count > 10) throw new MarketPublishException("关键词最多 10 个。");
        if (!Uri.TryCreate(r.RawBaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new MarketPublishException("下载前缀必须是 http(s) URL。");
    }

    private static string? FindManifest(ZipArchive zip)
    {
        return zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name) && string.Equals(e.Name, "plugin.json", StringComparison.OrdinalIgnoreCase))
            .Select(e => (e.FullName.Replace('\\', '/'), Depth: e.FullName.Replace('\\', '/').Count(c => c == '/')))
            .Where(x => x.Depth <= 1)
            .OrderBy(x => x.Depth)
            .Select(x => x.Item1)
            .FirstOrDefault();
    }

    private static string ReadText(ZipArchive zip, string name)
    {
        var e = zip.Entries.FirstOrDefault(x => string.Equals(x.FullName.Replace('\\', '/'), name, StringComparison.OrdinalIgnoreCase))
                ?? throw new MarketPublishException("ZIP 条目不存在：" + name);
        using var s = e.Open();
        using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return r.ReadToEnd();
    }

    private static JsonObject ParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json, nodeOptions: new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
                as JsonObject ?? throw new MarketPublishException("plugin.json 不是 JSON 对象。");
        }
        catch (MarketPublishException) { throw; }
        catch (Exception ex) { throw new MarketPublishException("plugin.json 解析失败：" + ex.Message, ex); }
    }

    private static string Str(JsonObject o, string key) =>
        o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<string>(out var s) ? s ?? "" : "";

    private static void Set(JsonObject o, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) o[key] = value;
    }

    private static bool IsScript(string name)
    {
        string ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".bat" or ".cmd" or ".ps1" or ".vbs" or ".scr";
    }

    private static void ExtractSafe(string zipPath, string destDir)
    {
        string root = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        long total = 0;
        foreach (var e in zip.Entries)
        {
            string rel = e.FullName.Replace('\\', '/');
            if (rel.Length == 0 || rel.EndsWith("/", StringComparison.Ordinal)) continue;
            total += e.Length;
            if (total > 100L * 1024 * 1024) throw new MarketPublishException("解压后超过 100 MB。");
            if (rel.StartsWith("/", StringComparison.Ordinal) || rel.Contains("..", StringComparison.Ordinal))
                throw new MarketPublishException("ZIP 含非法路径：" + e.FullName);
            string full = Path.GetFullPath(Path.Combine(destDir, rel));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new MarketPublishException("ZIP 含路径穿越：" + e.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            e.ExtractToFile(full, overwrite: true);
        }
    }
}

public sealed class MarketPublishException : Exception
{
    public MarketPublishException(string message) : base(message) { }
    public MarketPublishException(string message, Exception inner) : base(message, inner) { }
}
