using System.Diagnostics;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>一条环境检测结果。
/// Pick 是「这一项缺的是个路径」时的动作名：界面会在该行右边长出「选择路径…」按钮，
/// 点一下直接选（godot / dotnet / gamedata / vanilla / install / output）；通过项留空表示没什么要选的。</summary>
public sealed record EnvCheckItem(string Name, string Level, string Detail, string Fix, string? Url = null, string? Pick = null)
{
    public bool IsOk => Level == "通过";
    public bool IsError => Level == "错误";
    public string Icon => Level switch { "通过" => "✅", "警告" => "⚠️", _ => "❌" };
    /// <summary>这一行是否需要「选择路径…」按钮（XAML 里用它控制显隐）。</summary>
    public bool CanPick => !string.IsNullOrEmpty(Pick);
    /// <summary>一句话：图标 + 名称 + 说明（列表里直接显示这行）。</summary>
    public string Display => $"{Icon}  {Name}：{Detail}";
}

/// <summary>
/// 「跑起来到底缺什么」的检测：把新手最容易卡住的外部依赖逐项查一遍。
/// 任何一项缺失都会在界面上给出【怎么修】、官方下载地址，以及（如果是路径问题）一个「选择路径…」按钮。
/// </summary>
public static class EnvCheck
{
    public const string DotnetUrl = "https://dotnet.microsoft.com/zh-cn/download/dotnet/9.0";
    public const string GodotUrl = "https://godotengine.org/download/windows/";

    /// <summary>界面上那个按钮的文字（自检会拿它断言按钮真的在）。</summary>
    public const string PickButtonText = "选择路径…";

    /// <summary>Pick 动作名 → 这个按钮要选什么（自检用来确认每个动作名都有对应处理）。</summary>
    public static readonly IReadOnlyDictionary<string, string> PickActions = new Dictionary<string, string>
    {
        ["godot"] = "选 Godot 可执行文件",
        ["dotnet"] = "选 dotnet.exe",
        ["gamedata"] = "选游戏 data_sts2_* 目录",
        ["vanilla"] = "选解包后的原版工程目录",
        ["install"] = "选模组安装目录（mods）",
        ["output"] = "选工程输出目录",
    };

    public static List<EnvCheckItem> Run(CharacterProfile p)
    {
        var items = new List<EnvCheckItem>();
        var paths = p.Paths;

        // ① .NET SDK（工具内部要调 dotnet build 编译模组）
        items.Add(CheckDotnetSdk(paths.DotnetExe));

        // ② Godot（导出 PCK）
        string godot = (paths.GodotExe ?? "").Trim();
        if (godot.Length == 0)
            items.Add(new("Godot", "错误", "没有填写 Godot 路径",
                "点这一行右边的「选择路径…」找到 Godot 4.5.1 mono 版的可执行文件（建议选同目录下的 *_console.exe）", GodotUrl, "godot"));
        else if (!File.Exists(godot))
            items.Add(new("Godot", "错误", $"找不到：{godot}",
                "路径填错了，或 Godot 被移动/删除了。点右边的「选择路径…」重新选一次", GodotUrl, "godot"));
        else if (!Path.GetFileName(godot).Contains("console", StringComparison.OrdinalIgnoreCase))
            items.Add(new("Godot", "警告", "用的是非 console 版（能构建，但看不到导出日志）",
                "点右边的「选择路径…」改选同目录下的 *_console.exe，构建失败时才有详细日志", GodotUrl, "godot"));
        else
            items.Add(new("Godot", "通过", godot, ""));

        // ③ 游戏 data 目录（提供 sts2.dll / 0Harmony.dll 作为编译引用）
        string dataDir = (paths.GameDataDir ?? "").Trim();
        if (dataDir.Length == 0)
            items.Add(new("游戏 data 目录", "错误", "没有填写",
                "点右边的「选择路径…」选游戏安装目录下的 data_sts2_windows_x86_64", null, "gamedata"));
        else if (!Directory.Exists(dataDir))
            items.Add(new("游戏 data 目录", "错误", $"目录不存在：{dataDir}",
                "确认游戏已安装，点右边的「选择路径…」重新选 ...\\Slay the Spire 2\\data_sts2_windows_x86_64", null, "gamedata"));
        else
        {
            var missing = new List<string>();
            if (!File.Exists(Path.Combine(dataDir, "sts2.dll"))) missing.Add("sts2.dll");
            if (!File.Exists(Path.Combine(dataDir, "0Harmony.dll"))) missing.Add("0Harmony.dll");
            items.Add(missing.Count == 0
                ? new("游戏 data 目录", "通过", dataDir, "")
                : new("游戏 data 目录", "错误", $"缺少 {string.Join(" / ", missing)}",
                    "目录指错了，或游戏版本不对；点右边的「选择路径…」改选 data_sts2_windows_x86_64", null, "gamedata"));
        }

        // ④ 解包后的原版工程目录（效果库 / 场景 / 占位素材的来源）
        string vanilla = (paths.VanillaProject ?? "").Trim();
        if (vanilla.Length == 0)
            items.Add(new("解包工程目录", "错误", "没有填写",
                "需要一份解包后的原版工程（含 src/Core/Models/Powers 与 scenes/），这是效果库和占位素材的来源；点右边的「选择路径…」选它的最外层目录", null, "vanilla"));
        else if (!Directory.Exists(vanilla))
            items.Add(new("解包工程目录", "错误", $"目录不存在：{vanilla}",
                "这一项必须你自己准备（工具不含游戏素材），点右边的「选择路径…」重新选一次", null, "vanilla"));
        else
        {
            string powersDir = Path.Combine(vanilla, "src/Core/Models/Powers");
            string zhLoc = Path.Combine(vanilla, "localization/zhs/powers.json");
            string spineDir = Path.Combine(vanilla, "addons/spine");
            string scenesDir = Path.Combine(vanilla, "scenes/creature_visuals");

            items.Add(Directory.Exists(powersDir)
                ? new("效果库（Power 列表）", "通过", $"找到 {Directory.GetFiles(powersDir, "*.cs").Length} 个 Power 源码", "")
                : new("效果库（Power 列表）", "错误", $"缺少 {powersDir}",
                    "解包工程不完整；没有它，「增益/减益」下拉会是空的（可手动输入类名应急）。点「选择路径…」换一个完整的解包工程", null, "vanilla"));

            items.Add(File.Exists(zhLoc)
                ? new("中文名称（本地化）", "通过", "localization/zhs/powers.json 存在", "")
                : new("中文名称（本地化）", "警告", "没有 localization/zhs/powers.json",
                    "效果库会只显示英文类名，不影响生成；想显示中文就点「选择路径…」换一个带 localization 的解包工程", null, "vanilla"));

            items.Add(Directory.Exists(spineDir)
                ? new("Spine 插件（本体附带）", "通过", "addons/spine 存在", "")
                : new("Spine 插件（本体附带）", "警告", "缺少 addons/spine",
                    "生成的工程会缺这个 GDExtension（本体角色动画用得上），建议点「选择路径…」换成完整的解包工程", null, "vanilla"));

            items.Add(Directory.Exists(scenesDir)
                ? new("场景模板", "通过", "scenes/creature_visuals 存在", "")
                : new("场景模板", "错误", $"缺少 {scenesDir}",
                    "生成的模组会缺场景文件，进游戏会报错；点「选择路径…」换成完整的解包工程", null, "vanilla"));
        }

        // ⑤ 模组安装目录（游戏 mods）
        string install = (paths.InstallDir ?? "").Trim();
        if (install.Length == 0)
            items.Add(new("模组安装目录", "警告", "没有填写",
                "通常是游戏安装目录下的 mods 文件夹；点右边的「选择路径…」选它，填了才能「一键安装」", null, "install"));
        else if (!Directory.Exists(install))
            items.Add(new("模组安装目录", "警告", $"目录不存在：{install}",
                "构建时会尝试自动创建；如果没权限会失败。也可以点「选择路径…」换一个已有的 mods 目录", null, "install"));
        else
            items.Add(new("模组安装目录", "通过", install, ""));

        // ⑥ 输出目录（生成的角色工程放哪）
        string outDir = (paths.OutputDir ?? "").Trim();
        if (outDir.Length == 0)
            items.Add(new("工程输出目录", "警告", "没有填写（会自动用生成器目录下的 自定义角色）", "", null));
        else
        {
            try
            {
                Directory.CreateDirectory(outDir);
                items.Add(new("工程输出目录", "通过", outDir, ""));
            }
            catch (Exception ex)
            {
                items.Add(new("工程输出目录", "错误", $"不可写：{ex.Message}",
                    "换一个可写的目录：点右边的「选择路径…」", null, "output"));
            }
        }

        return items;
    }

    /// <summary>查 .NET SDK：能跑 dotnet 且至少有一个 9.x 的 SDK。
    /// dotnetExe 非空时用它（整合包环境包里的便携版），否则用 PATH 里的 dotnet。</summary>
    public static EnvCheckItem CheckDotnetSdk(string? dotnetExe = null)
    {
        string exe = (dotnetExe ?? "").Trim();
        bool explicitPath = exe.Length > 0;
        if (explicitPath && !File.Exists(exe))
            return new(".NET SDK", "错误", $"指定的 dotnet 不存在：{exe}",
                "点右边的「选择路径…」重新选 dotnet.exe，或清空这一项改用系统 PATH 里的 dotnet", DotnetUrl, "dotnet");

        var (ok, output) = RunCapture(explicitPath ? $"\"{exe}\" --list-sdks" : "dotnet --list-sdks", 20000);
        if (!ok || output.Trim().Length == 0)
            return new(".NET SDK", "错误", "找不到 dotnet 命令（编译模组必须要有 .NET 9 SDK）",
                "解压整合包的「环境包」后点「自动探测本机环境」，或点右边的「选择路径…」选它里面的 dotnet.exe；也可以自己安装 .NET 9 SDK 后重启本程序", DotnetUrl, "dotnet");

        var versions = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.Split(' ')[0])
            .Where(v => Version.TryParse(v.Split('-')[0], out _))
            .ToList();

        bool has9 = versions.Any(v => v.StartsWith("9.", StringComparison.Ordinal));
        if (!has9)
            return new(".NET SDK", "错误", $"只找到 SDK：{string.Join(", ", versions)}（缺少 9.x）",
                "生成的是 net9.0 工程，需要 .NET 9 SDK（也可以点「选择路径…」选环境包里的 dotnet.exe）", DotnetUrl, "dotnet");

        string from = explicitPath ? "（环境包自带）" : "";
        return new(".NET SDK", "通过", from + "已安装 " + string.Join(" / ", versions.Where(v => v.StartsWith("9.", StringComparison.Ordinal))), "");
    }

    /// <summary>跑一条命令并抓输出（用于探测 dotnet；失败不抛异常）。</summary>
    private static (bool Ok, string Output) RunCapture(string command, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c " + command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return (false, "");
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(true); } catch { /* 忽略 */ }
                return (false, "");
            }
            return (proc.ExitCode == 0, stdout + stderr);
        }
        catch { return (false, ""); }
    }
}
