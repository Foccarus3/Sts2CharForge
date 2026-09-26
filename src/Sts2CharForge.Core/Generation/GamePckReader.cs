using System.Text;
using System.Text.Json;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 直接读游戏本体的 Godot 资源包（SlayTheSpire2.pck），用来取**中文名**等本地化文本。
/// 为什么要读 pck：工具不内嵌任何游戏文本（法律上干净），但用户换台机器、没有解包工程时
/// 也想看到「虚弱」而不是「WeakPower」。pck 里的 localization/*.json 是明文，
/// 而且是玩家自己机器上的文件 —— 运行时读它既不用打包游戏文本，又能有中文名。
///
/// Godot 4.5 的 pck 布局（pack_version 3）：
///   0x00 "GDPC" | 0x04 pack_version | 0x08 ver_major | 0x0C ver_minor | 0x10 ver_patch
///   0x14 pack_flags(bit1=PACK_REL_FILEBASE) | 0x18 file_base(u64) | 0x20 目录偏移(u64, 指向文件末尾)
///   目录： u32 文件数，然后每条 = u32 路径长度 + 路径(补齐4字节) + u64 偏移 + u64 大小 + 16B md5 + u32 flags
/// 偏移在 PACK_REL_FILEBASE 时是相对 file_base 的，要减去 file_base。
/// </summary>
public static class GamePckReader
{
    private static string? _cachedPck;
    private static readonly Dictionary<string, Dictionary<string, string>?> _cache = new(StringComparer.Ordinal);

    /// <summary>在游戏 data 目录附近找 pck（游戏安装目录下叫 SlayTheSpire2.pck）。</summary>
    public static string? FindPck(string? gameDataDir)
    {
        if (_cachedPck is not null && File.Exists(_cachedPck)) return _cachedPck;
        try
        {
            var dirs = new List<string>();
            if (!string.IsNullOrWhiteSpace(gameDataDir))
            {
                dirs.Add(gameDataDir);
                var parent = Directory.GetParent(gameDataDir.TrimEnd('\\', '/'));
                if (parent is not null) dirs.Add(parent.FullName);
            }
            foreach (string d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                string[] pcks = Directory.GetFiles(d, "*.pck");
                string? hit = pcks.FirstOrDefault(f => Path.GetFileName(f).Contains("SlayTheSpire", StringComparison.OrdinalIgnoreCase))
                              ?? pcks.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
                if (hit is not null) { _cachedPck = hit; return hit; }
            }
        }
        catch { /* 找不到就算了 */ }
        return null;
    }

    /// <summary>最近一次读取失败的原因（给界面/日志看，方便定位）。</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>从 pck 里读一个本地化 JSON（例如 localization/zhs/powers.json）并解析成 键→值。</summary>
    public static Dictionary<string, string>? ReadLocalization(string? gameDataDir, string innerPath)
    {
        if (_cache.TryGetValue(innerPath, out var cached)) return cached;

        Dictionary<string, string>? result = null;
        LastError = "";
        try
        {
            string? pck = FindPck(gameDataDir);
            if (pck is null)
            {
                LastError = "没找到游戏 pck（在游戏 data 目录及其上一级里找 *.pck）";
            }
            else
            {
                byte[]? content = ReadEntry(pck, innerPath.Replace('\\', '/'));
                if (content is null)
                {
                    if (LastError.Length == 0) LastError = $"pck 里没有 {innerPath}（或条目读取失败）";
                }
                else
                {
                    result = ParseJsonDict(content);
                    if (result is null) LastError = $"{innerPath} 读到了 {content.Length} 字节但不是可解析的 JSON";
                }
            }
        }
        catch (Exception ex) { LastError = "读取 pck 出错：" + ex.Message; result = null; }

        _cache[innerPath] = result;
        return result;
    }

    /// <summary>把一段字节当 UTF-8 JSON 解析成 键→值；失败返回 null。</summary>
    private static Dictionary<string, string>? ParseJsonDict(byte[] content)
    {
        try
        {
            string text = Encoding.UTF8.GetString(content).TrimStart('\uFEFF', '\0', ' ', '\r', '\n');
            int brace = text.IndexOf('{');
            if (brace < 0) return null;
            int end = text.LastIndexOf('}');
            if (end <= brace) return null;
            text = text[brace..(end + 1)];
            using var doc = JsonDocument.Parse(text);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString()!;
            return map.Count > 0 ? map : null;
        }
        catch { return null; }
    }

    /// <summary>读 pck 里某一个文件的原始字节（只读目录和这一个文件，不会整包读入）。</summary>
    public static byte[]? ReadEntry(string pckPath, string innerPath)
    {
        using var fs = File.OpenRead(pckPath);
        using var br = new BinaryReader(fs);
        if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "GDPC") return null;
        br.ReadUInt32();                                  // pack_version
        br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32(); // 引擎版本
        uint flags = br.ReadUInt32();
        ulong fileBase = br.ReadUInt64();
        ulong dirOffset = br.ReadUInt64();                // 0x20：目录在文件末尾
        bool relBase = (flags & 2) != 0;                  // PACK_REL_FILEBASE

        if (dirOffset == 0 || (long)dirOffset >= fs.Length) return null;
        fs.Seek((long)dirOffset, SeekOrigin.Begin);
        uint count = br.ReadUInt32();

        for (uint i = 0; i < count; i++)
        {
            if (fs.Position + 4 > fs.Length) return null;
            uint len = br.ReadUInt32();
            if (len == 0 || len > 4096) return null;
            string path = Encoding.UTF8.GetString(br.ReadBytes((int)len));
            int pad = (int)((4 - (len % 4)) % 4);
            if (pad > 0) br.ReadBytes(pad);
            ulong off = br.ReadUInt64();
            ulong size = br.ReadUInt64();
            br.ReadBytes(16);                             // md5
            uint entryFlags = br.ReadUInt32();
            if ((entryFlags & 1) != 0) continue;          // 加密条目跳过

            if (!string.Equals(path, innerPath, StringComparison.Ordinal)) continue;
            // 偏移在 PACK_REL_FILEBASE 时相对 file_base；不同 Godot 版本细节可能有差异，
            // 这里把几种可能都试一遍，谁解析得出 JSON 就用谁（自适应）。
            foreach (long start in new[]
                     {
                         relBase ? (long)(off - fileBase) : (long)off,
                         (long)off,
                         relBase ? (long)(off + fileBase) : (long)off - (long)fileBase,
                     })
            {
                if (start < 0 || start >= fs.Length) continue;
                long take = (long)Math.Min(size, (ulong)(fs.Length - start));
                fs.Seek(start, SeekOrigin.Begin);
                var buf = new byte[take];
                int read = 0;
                while (read < take)
                {
                    int n = fs.Read(buf, read, (int)(take - read));
                    if (n <= 0) break;
                    read += n;
                }
                if (read <= 0) continue;
                if (read < take) Array.Resize(ref buf, read);
                if (ParseJsonDict(buf) is not null) return buf;
            }
            LastError = $"找到 {innerPath}（off={off} size={size} flags={entryFlags}）但偏移换算后解析不出 JSON";
            return null;
        }
        return null;
    }
}
