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

/// <summary>一键构建：dotnet build → Godot 导入 → 导出 PCK → 安装到指定目录。</summary>
public static class ModBuilder
{
    public static BuildResult Build(string projectRoot, string modId, string godotExe, string? installDir,
        Action<string>? log = null, string? dotnetExe = null)
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

        // 4) 安装
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
