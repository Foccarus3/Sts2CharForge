using Microsoft.Win32;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 自动探测《杀戮尖塔 2》的安装位置，免去换台电脑就要手工填路径。
/// 只看**路径**，不读也不分发任何游戏内容。
/// </summary>
public static class PathAutoDetect
{
    /// <summary>探测游戏安装目录（含 data_sts2_windows_x86_64 的那个目录）。</summary>
    public static string? FindGameDir()
    {
        foreach (string raw in CandidateRoots())
        {
            string root = Normalize(raw);
            try
            {
                if (!Directory.Exists(root)) continue;
                // 直接就是游戏目录
                if (LooksLikeGame(root)) return Normalize(root);
                // 库目录下的 steamapps\common\<游戏>
                foreach (string sub in new[]
                         {
                             Path.Combine(root, "steamapps", "common", "Slay the Spire 2"),
                             Path.Combine(root, "common", "Slay the Spire 2"),
                             Path.Combine(root, "Slay the Spire 2"),
                         })
                    if (LooksLikeGame(sub)) return Normalize(sub);
                // 再扫一层 steamapps\common
                string common = Path.Combine(root, "steamapps", "common");
                if (Directory.Exists(common))
                    foreach (string d in Directory.GetDirectories(common))
                        if (Path.GetFileName(d).Contains("Slay the Spire", StringComparison.OrdinalIgnoreCase) && LooksLikeGame(d))
                            return Normalize(d);
            }
            catch { /* 忽略无权限的目录 */ }
        }
        return null;
    }

    /// <summary>注册表里的 Steam 路径常用正斜杠，统一成 Windows 反斜杠。</summary>
    private static string Normalize(string p) => p.Replace('/', '\\').TrimEnd('\\');

    /// <summary>
    /// 探测游戏的 **mods 目录**（= 安装目标）：<c>&lt;游戏目录&gt;\mods</c>。
    /// 目录还不存在也返回路径（本体第一次装模组时会自己建），只要游戏目录找得到。
    /// </summary>
    public static string? FindModsDir() => FindModsDir(FindGameDir());

    /// <summary>给一个游戏目录（或 data 目录 / mods 目录本身），算出对应的 mods 目录。</summary>
    public static string? FindModsDir(string? gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir)) return null;
        string dir = Normalize(gameDir);
        if (string.Equals(Path.GetFileName(dir), "mods", StringComparison.OrdinalIgnoreCase)) return dir;
        string name = Path.GetFileName(dir);
        // 传进来的是 data_sts2_* 目录 → 它的上一级才是游戏目录
        if (name.StartsWith("data_sts2", StringComparison.OrdinalIgnoreCase) && Directory.GetParent(dir) is { } parent)
            return Path.Combine(parent.FullName, "mods");
        // 传进来的是游戏根目录
        if (LooksLikeGame(dir)) return Path.Combine(dir, "mods");
        return null;
    }

    /// <summary>
    /// 这个目录看起来是不是「游戏的 mods 目录」？（校验用户填的安装目录用）
    /// 判定：目录名就叫 mods；或者它就在游戏目录下面。
    /// 目录还不存在时返回 null（没法判断，别乱报错）。
    /// </summary>
    public static bool? LooksLikeModsDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        string path = Normalize(dir);
        if (string.Equals(Path.GetFileName(path), "mods", StringComparison.OrdinalIgnoreCase)) return true;
        // 明显选错的情况：选了游戏根目录 / data 目录
        if (Directory.Exists(Path.Combine(path, "data_sts2_windows_x86_64"))) return false;
        if (Directory.Exists(path) && Directory.GetFiles(path, "sts2.dll").Length > 0) return false;
        if (Directory.GetParent(path) is { } parent && LooksLikeGame(parent.FullName)) return true;
        if (!Directory.Exists(path)) return null;                 // 还不存在：不判断
        return false;
    }

    private static bool LooksLikeGame(string dir) =>
        Directory.Exists(Path.Combine(dir, "data_sts2_windows_x86_64")) &&
        File.Exists(Path.Combine(dir, "data_sts2_windows_x86_64", "sts2.dll"));

    /// <summary>候选根目录：Steam 注册表 + libraryfolders.vdf 里的库 + 常见安装位置。</summary>
    private static IEnumerable<string> CandidateRoots()
    {
        var list = new List<string>();

        // 1) Steam 客户端安装位置（注册表）
        foreach (var (hive, path, name) in new (RegistryKey, string, string)[]
                 {
                     (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                 })
        {
            try
            {
                object? v = hive.OpenSubKey(path)?.GetValue(name);
                if (v is string s && s.Length > 0) list.Add(s);
            }
            catch { /* 忽略 */ }
        }

        // 2) libraryfolders.vdf 里的其它库
        foreach (string steam in list.ToList())
        {
            try
            {
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                var matches = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\"");
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    string p = Normalize(System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value));
                    if (p.Length > 0 && !list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p);
                }
            }
            catch { /* 忽略 */ }
        }

        // 3) 常见位置（含从 C 到 H 盘）
        foreach (string drive in new[] { "C", "D", "E", "F", "G", "H" })
        {
            list.Add($@"{drive}:\Program Files (x86)\Steam");
            list.Add($@"{drive}:\Program Files\Steam");
            list.Add($@"{drive}:\Steam");
            list.Add($@"{drive}:\SteamLibrary");
            list.Add($@"{drive}:\Games\Steam");
            list.Add($@"{drive}:\SteamLibrary\steamapps\common");
        }
        return list;
    }

    /// <summary>找一个本机可用的《杀戮尖塔 2》解包工程（project.godot + src/Core）。</summary>
    public static string? FindVanillaProject()
    {
        var roots = new List<string>();
        foreach (string drive in new[] { "C", "D", "E", "F", "G", "H" })
            foreach (string sub in new[] { @"\download\slay", @"\slay", @"\sts2", @"\SlayTheSpire2", @"\godot", @"\", @"\games", @"\Games", @"\dev" })
                roots.Add(drive + ":" + sub);

        foreach (string root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                if (LooksLikeProject(root)) return Normalize(root);
                foreach (string d in Directory.GetDirectories(root))
                    if (LooksLikeProject(d)) return Normalize(d);
            }
            catch { /* 忽略无权限目录 */ }
        }
        return null;
    }

    private static bool LooksLikeProject(string dir) =>
        File.Exists(Path.Combine(dir, "project.godot")) &&
        (Directory.Exists(Path.Combine(dir, "src", "Core")) || Directory.Exists(Path.Combine(dir, "Core")));

    /// <summary>找 Godot 可执行文件：先看包内/程序旁边，再看解包工程附近，最后扫常见目录。</summary>
    public static string? FindGodotNearby(string baseDir)
    {
        // 1) 程序自己旁边（整合包若带了 Godot，就放在 程序文件\godot）
        foreach (string start in new[]
                 {
                     Path.Combine(baseDir, "godot"),
                     Path.Combine(baseDir, "Godot"),
                     baseDir,
                     Path.GetFullPath(Path.Combine(baseDir, "..")),
                 })
        {
            try
            {
                if (!Directory.Exists(start)) continue;
                string? exe = FirstGodotExe(start);
                if (exe is not null) return exe;
            }
            catch { /* 忽略 */ }
        }

        // 2) 解包工程旁边（解包的人通常把 Godot 放在同一个目录下）
        try
        {
            string? proj = FindVanillaProject();
            if (proj is not null)
            {
                string? exe = SearchGodot(Path.GetDirectoryName(proj)!, 3);
                if (exe is not null) return exe;
            }
        }
        catch { /* 忽略 */ }

        // 3) 常见安装位置（只按目录名过滤，不做全盘遍历）
        foreach (string drive in new[] { "C", "D", "E", "F", "G", "H" })
        {
            foreach (string folder in new[]
                     {
                         drive + @":\download", drive + @":\Downloads", drive + @":\tools", drive + @":\Tools",
                         drive + @":\Games", drive + @":\games", drive + @":\dev", drive + @":\Projects",
                         drive + @":\Program Files", drive + @":\Program Files (x86)", drive + @":\",
                     })
            {
                string? exe = SearchGodot(folder, folder.EndsWith(@":\", StringComparison.Ordinal) ? 2 : 3);
                if (exe is not null) return exe;
            }
        }
        return null;
    }

    /// <summary>按目录名过滤地往下找 Godot（深度受限，避免扫描整块硬盘）。</summary>
    private static string? SearchGodot(string dir, int depth)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;

            if (Path.GetFileName(dir).Contains("godot", StringComparison.OrdinalIgnoreCase))
            {
                string? exe = FirstGodotExe(dir);
                if (exe is not null) return exe;
            }
            if (depth <= 0) return null;

            foreach (string sub in Directory.GetDirectories(dir))
            {
                string name = Path.GetFileName(sub);
                bool interesting = Path.GetFileName(dir).Contains("godot", StringComparison.OrdinalIgnoreCase)
                                   || IsContainerName(name);
                if (!interesting) continue;
                string? exe = SearchGodot(sub, depth - 1);
                if (exe is not null) return exe;
            }
        }
        catch { /* 无权限目录忽略 */ }
        return null;
    }

    /// <summary>名字看起来像「装东西的目录」才继续往里找（Godot 通常就在这些目录底下）。</summary>
    private static bool IsContainerName(string name)
    {
        foreach (string key in new[] { "download", "下载", "dev", "project", "工程", "game", "游戏", "tool", "工具",
                                       "slay", "sts2", "spire", "godot", "app", "work", "mod" })
            if (name.Contains(key, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string? FirstGodotExe(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*console.exe", SearchOption.AllDirectories).FirstOrDefault()
                   ?? Directory.GetFiles(dir, "Godot*.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>
    /// 找便携版 .NET SDK 的 dotnet.exe（整合包「环境包」里解压出来的那个）。
    /// 顺序：程序自己旁边 → 上一级、上两级（两个压缩包解压到同一个目录时就是这种情况）→
    /// 各级目录下名字叫 dotnet 的子目录。
    /// </summary>
    public static string? FindDotnetNearby(string baseDir)
    {
        var dirs = new List<string>();
        try
        {
            string cur = baseDir;
            for (int i = 0; i < 3 && !string.IsNullOrEmpty(cur); i++)
            {
                dirs.Add(cur);
                cur = Path.GetFullPath(Path.Combine(cur, ".."));
                if (cur == Path.GetPathRoot(cur)) { dirs.Add(cur); break; }
            }
        }
        catch { /* 忽略 */ }

        foreach (string dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;

                string direct = Path.Combine(dir, "dotnet", "dotnet.exe");
                if (File.Exists(direct)) return direct;

                foreach (string sub in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(sub);
                    if (!name.Contains("dotnet", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("环境", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("sdk", StringComparison.OrdinalIgnoreCase)) continue;

                    string exe = Path.Combine(sub, "dotnet.exe");
                    if (File.Exists(exe)) return exe;

                    // 环境包目录里再套一层（例如 环境包\dotnet\dotnet.exe）
                    string nested = Path.Combine(sub, "dotnet", "dotnet.exe");
                    if (File.Exists(nested)) return nested;
                }
            }
            catch { /* 无权限目录忽略 */ }
        }
        return null;
    }
}
