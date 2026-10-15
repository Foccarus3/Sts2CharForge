using System.Diagnostics;
using System.Text;
using Sts2CharForge.Core.Generation;

namespace Sts2CharForge.Core.Build;

public sealed record BuildStepResult(string Name, bool Success, string Output);

public sealed record BuildResult(bool Success, List<BuildStepResult> Steps, string? InstalledTo)
{
    public string FullLog => string.Join(Environment.NewLine + Environment.NewLine,
        Steps.Select(s => $"### {s.Name} {(s.Success ? "OK" : "失败")}{Environment.NewLine}{s.Output}"));
}

/// <summary>一键构建：dotnet build → Godot 导入 → 导出 PCK →（可选）安装到指定目录。</summary>
public static class ModBuilder
{
    /// <summary>
    /// 跑一遍构建。<b>装不装、装到哪，完全由 <paramref name="installDir"/> 决定</b>：
    /// 为 null / 空白就**一个文件都不装**（跳过第 4 步），非空才把 <c>&lt;ModId&gt;.dll/.pck/.json</c> 拷过去。
    ///
    /// <para>⚠ <b>调用方不许在这里替用户"兜底"填目录</b>（V0.2.137 红线⑧）：
    /// 命令行以前写的是 <c>installDir ?? profile.Paths.InstallDir</c> —— 不给 <c>--install</c> 时会退回用
    /// 存档里的 <c>Paths.InstallDir</c>，于是「只想编译一下」的探针把模组真的装进了存档里写着的游戏
    /// mods 目录。<b>存档里的 InstallDir 只给图形界面用</b>（GUI 自己会把「用户点了安装」和目录一起算好
    /// 再传进来，见 <c>MainWindow.RunBuild</c> 的 <c>installTo = (install ? text : null)</c>）；
    /// 命令行必须把 <c>--install</c> 直接透传，没给就传 null。</para>
    /// </summary>
    public static BuildResult Build(string projectRoot, string modId, string godotExe, string? installDir,
        Action<string>? log = null, string? dotnetExe = null, string? gameDataDir = null)
    {
        var steps = new List<BuildStepResult>();
        string buildDir = Path.Combine(projectRoot, "build");
        Directory.CreateDirectory(buildDir);

        // 用哪个 dotnet：优先配置里指定的（环境包自带的便携版），否则用 PATH 里的
        string dotnet = string.IsNullOrWhiteSpace(dotnetExe) || !File.Exists(dotnetExe) ? "dotnet" : $"\"{dotnetExe}\"";
        string? dotnetRoot = string.IsNullOrWhiteSpace(dotnetExe) || !File.Exists(dotnetExe)
            ? null : Path.GetDirectoryName(dotnetExe);

        // 1) 编译 DLL
        var (ok, output) = Run($"{dotnet} build \"{modId}.csproj\" -c Debug", projectRoot, log, dotnetRoot);
        steps.Add(new BuildStepResult("编译 DLL（dotnet build）", ok, Tail(output, 200)));
        if (!ok) return new BuildResult(false, steps, null);

        // 2) 导入资源 + 3) 导出 PCK
        if (string.IsNullOrWhiteSpace(godotExe) || !File.Exists(godotExe))
        {
            steps.Add(new BuildStepResult("导出 PCK", false, "找不到 Godot 可执行文件，请在界面里选择正确路径后重试。"));
            return new BuildResult(false, steps, null);
        }

        // Godot 导出时自己会调 dotnet 编译 C#，所以也要让它能找到同一个 dotnet
        var (_, out2) = Run($"\"{godotExe}\" --headless --path \"{projectRoot}\" --import", projectRoot, log, dotnetRoot);
        steps.Add(new BuildStepResult("Godot 导入资源", true, Tail(out2, 40)));

        string pck = Path.Combine(buildDir, modId + ".pck");
        string allOutput = "";
        bool pckExists = false;
        for (int attempt = 1; attempt <= 2 && !pckExists; attempt++)
        {
            FsSafe.DeleteFile(pck);
            var (_, out3) = Run($"\"{godotExe}\" --headless --path \"{projectRoot}\" --export-pack \"Windows Desktop\" \"{pck}\"",
                projectRoot, log, dotnetRoot);
            allOutput = out3;
            pckExists = File.Exists(pck);
            if (!pckExists) log?.Invoke($"[重试 {attempt}/2] 没有生成 pck");
        }
        steps.Add(new BuildStepResult("导出 PCK", pckExists, Tail(allOutput, 60)));
        if (!pckExists) return new BuildResult(false, steps, null);

        // 3.5) 校验 pck 的**包格式版本**（V0.2.167）：用哪份 Godot 导出是用户在「路径」页自己指的，
        // 用了比游戏引擎更新的 Godot 时导出的包**本体直接拒收** —— 游戏日志里是
        // `ERROR: Pack version unsupported: 4.` + `Godot errored while loading PCK file X`，
        // 表现是「模组装了、游戏照常进主菜单、但内容一个都没有」（Loaded 0 mods）。
        // 这里当场拦下来：参照物 = 本体自己的 SlayTheSpire2.pck 的包格式版本。
        string? packIssue = CheckPackFormat(pck, gameDataDir);
        if (packIssue is not null)
        {
            steps.Add(new BuildStepResult("校验 PCK 包格式", false, packIssue));
            return new BuildResult(false, steps, null);
        }

        // 4) 安装（**只在 installDir 非空时**做 —— 见 Build 的注释：调用方不许拿存档里的 InstallDir 兜底）
        string? installedTo = null;
        if (!string.IsNullOrWhiteSpace(installDir))
        {
            var sb = new StringBuilder();
            bool all = true;
            try
            {
                Directory.CreateDirectory(installDir);
            }
            catch (Exception ex)
            {
                steps.Add(new BuildStepResult("安装到 " + installDir, false,
                    "安装目录不可用（" + ex.Message + "）。请确认路径存在且可写，或以管理员身份运行。"));
                return new BuildResult(false, steps, null);
            }

            foreach (string f in new[] { modId + ".dll", modId + ".pck", modId + ".json" })
            {
                string src = Path.Combine(buildDir, f);
                if (!File.Exists(src)) { sb.AppendLine("[缺失] " + f); all = false; continue; }
                try
                {
                    // mods 目录里的旧文件常常带只读属性（从压缩包 / 备份恢复出来的），FsSafe 会先清属性再写；
                    // 真失败时它的消息里已经带了「为什么 + 怎么办」
                    FsSafe.Copy(src, Path.Combine(installDir, f), overwrite: true);
                    sb.AppendLine($"[已安装] {f}  ({new FileInfo(src).Length} 字节)");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[失败] {f}：{ex.Message}");
                    sb.AppendLine("  → 常见原因：目标文件正被占用（游戏还在运行？）、带只读属性、或该目录需要管理员权限。");
                    all = false;
                }
            }
            if (all) installedTo = installDir;
            steps.Add(new BuildStepResult("安装到 " + installDir, all, sb.ToString()));
        }

        return new BuildResult(true, steps, installedTo);
    }

    /// <summary>
    /// 通过临时 .cmd 批处理执行命令并重定向输出到文件。
    /// 注意：批处理**首行必须 chcp 65001**，否则 cmd.exe 会按系统 ANSI 码页(GBK)解析文件，
    /// 路径里的中文（如「自定义角色」）会变成乱码传给 Godot，导致
    /// "Invalid project path specified ... aborting"。
    /// </summary>
    /// <summary>
    /// 导出的 pck 和本体自带的 pck **包格式版本**对不对得上（对不上就返回一句人话；对得上 / 没有参照物返回 null）。
    ///
    /// <para>为什么：Godot 各大版本会改 pck 里的 pack 格式版本号（<c>0x04</c> 那 4 个字节，布局见
    /// <see cref="GamePckReader"/> 头部注释）。游戏引擎按自己的常量校验，不匹配就
    /// <c>ERROR: Pack version unsupported: N.</c> 直接拒收整个包 —— 模组**静默失效**：
    /// dll 照常加载、pck 被丢，游戏照常进主菜单，用户看到的是「装了什么都没发生」。
    /// 用户实测报过：用比游戏新的 Godot 导出 → 两个模组都没加载（`Loaded 0 mods (2 total)`）。</para>
    /// </summary>
    private static string? CheckPackFormat(string pck, string? gameDataDir)
    {
        try
        {
            uint mine = ReadPackFormatVersion(pck);
            if (mine == 0) return null;                        // 读不出来（文件怪）就不下结论
            string? gamePck = GamePckReader.FindPck(gameDataDir);
            if (gamePck is null) return null;                  // 找不到本体包 = 没有参照物
            uint game = ReadPackFormatVersion(gamePck);
            if (game == 0 || game == mine) return null;
            return $"导出的 pck 包格式版本是 {mine}，而本体游戏自带的包是 {game} —— **游戏读不了这个包**"
                + $"（游戏日志里是 `Pack version unsupported: {mine}` + `Godot errored while loading PCK file …`；"
                + "装上去以后这个模组整个不加载，游戏里什么都不会出现）。\n"
                + "  → 多半是「路径」页里的 Godot 比游戏引擎新（例如 4.6+）：换回整合包自带的那份 Godot（4.5.1）重新构建即可。";
        }
        catch { return null; }
    }

    /// <summary>读 Godot pck 头里的包格式版本（<c>0x00 "GDPC" | 0x04 pack_version</c>）。读不出来返回 0。</summary>
    private static uint ReadPackFormatVersion(string pck)
    {
        using var fs = File.OpenRead(pck);
        var head = new byte[8];
        if (fs.Read(head, 0, head.Length) < head.Length) return 0;
        if (Encoding.ASCII.GetString(head, 0, 4) != "GDPC") return 0;
        return BitConverter.ToUInt32(head, 4);
    }

    private static (bool Success, string Output) Run(string command, string workingDir, Action<string>? log,
        string? dotnetRoot = null)
    {
        string id = Guid.NewGuid().ToString("N");
        string logFile = Path.Combine(Path.GetTempPath(), "forge_" + id + ".log");
        string batFile = Path.Combine(Path.GetTempPath(), "forge_" + id + ".cmd");

        File.WriteAllText(batFile,
            "@echo off\r\n" +
            "chcp 65001 >nul\r\n" +
            command + " > \"" + logFile + "\" 2>&1\r\n" +
            "exit /b %ERRORLEVEL%\r\n");
        log?.Invoke("$ " + command);

        bool ok = false;
        try
        {
            var psi = new ProcessStartInfo("cmd.exe")
            {
                Arguments = "/c \"" + batFile + "\"",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // 便携版 .NET SDK：告诉 dotnet 自己在哪，并把它排到 PATH 最前面，
            // 这样 Godot 导出时内部调用的 dotnet 也是同一个（否则会用系统里没装 SDK 的那个）。
            if (!string.IsNullOrWhiteSpace(dotnetRoot) && Directory.Exists(dotnetRoot))
            {
                psi.Environment["DOTNET_ROOT"] = dotnetRoot;
                psi.Environment["DOTNET_ROOT(x86)"] = dotnetRoot;
                psi.Environment["PATH"] = dotnetRoot + ";" + Environment.GetEnvironmentVariable("PATH");
                psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
                psi.Environment["DOTNET_NOLOGO"] = "1";
            }

            using var proc = Process.Start(psi);
            if (proc is null) return (false, "无法启动进程");
            proc.WaitForExit();
            ok = proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            return (false, "执行失败：" + ex.Message);
        }
        finally
        {
            try { File.Delete(batFile); } catch { /* 忽略 */ }
        }

        string output = File.Exists(logFile) ? File.ReadAllText(logFile, Encoding.UTF8) : "";
        try { File.Delete(logFile); } catch { /* 忽略 */ }
        return (ok, output);
    }

    private static string Tail(string text, int maxLines)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return lines.Length <= maxLines ? text : string.Join('\n', lines[^maxLines..]);
    }
}
