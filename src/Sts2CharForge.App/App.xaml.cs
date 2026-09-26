using System.IO;
using System.Text;
using System.Windows;

namespace Sts2CharForge.App;

public partial class App : Application
{

    protected override void OnStartup(StartupEventArgs e)
    {
        // 自检 / 命令行模式：绝不写用户的存档（只读）。
        // 这些模式会载入最新存档并自动填路径，以前会把自检用的临时路径静默写回用户存档（实测踩过）。
        if (e.Args.Any(a => a.StartsWith("--", StringComparison.Ordinal)))
        {
            Sts2CharForge.App.MainWindow.SuppressProfileWrites = true;
            // 关窗口时也不要问「要不要保存」：命令行/自检里没人点，进程会永远卡在那儿不退出
            //（实测踩过：--buildtest 结果文件写不出来、进程一直挂着）。
            Sts2CharForge.App.MainWindow.SuppressClosePrompt = true;
        }

        // 全局异常兜底：任何未处理的异常都只弹提示、写日志，绝不让界面闪退
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash_log.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + args.Exception + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch { /* 忽略日志写入失败 */ }

            MessageBox.Show("操作出现异常（已被拦截，程序不会退出）：\n\n" + args.Exception.Message +
                            "\n\n详细信息已写入 crash_log.txt，可发给我定位。",
                "Sts2CharForge", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        base.OnStartup(e);

        // 自检模式：不开界面（会短暂建一个窗口跑断言），把结果写到文件，便于无人值守验证
        if (e.Args.Any(a => a.Equals("--uicheck", StringComparison.OrdinalIgnoreCase)))
        {
            string outFile = Path.Combine(AppContext.BaseDirectory, "uicheck_result.txt");
            try
            {
                var w = new MainWindow();
                string report;
                try { report = w.SelfTest(); }
                catch (Exception ex)
                {
                    // 崩了也要把已经跑出来的断言写出来，否则只剩异常、看不到前面结果
                    report = w.SelfTestPartial + Environment.NewLine + "自检异常（跑到一半崩了）: " + ex;
                }
                File.WriteAllText(outFile, report, new UTF8Encoding(false));
                Shutdown(report.Contains("FAIL") || report.Contains("自检异常") ? 1 : 0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(outFile, "自检异常: " + ex, new UTF8Encoding(false));
                Shutdown(1);
            }
            return;
        }

        // 一键构建自检：真实跑一遍「生成→编译→导出→安装」，同时统计界面是否响应
        if (e.Args.Any(a => a.Equals("--buildtest", StringComparison.OrdinalIgnoreCase)))
        {
            string outFile = Path.Combine(AppContext.BaseDirectory, "buildtest_result.txt");
            try
            {
                var w = new MainWindow();
                // 关键：别让「关窗口」把程序带走 —— 结果文件是在 await 之后写的，
                // 之前用默认的 OnLastWindowClose 时，BuildForTestAsync 里一 Close() 就整个退出，
                // PushFrame 卡死、结果文件写不出来。改成显式退出（写完文件再 Shutdown）。
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                w.Show();
                // 可选：指定要测哪个存档（不传就取存档目录里最新的那个）。
                // 隔离在 BuildForTestAsync 里做：存档会先被复制到临时目录再载入，用户存档一个字节都不动。
                string? profArg = e.Args.FirstOrDefault(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                // 用 DispatcherFrame 等待：消息循环继续跑，才能验证「界面不卡」
                var task = w.BuildForTestAsync(profArg);
                var frame = new System.Windows.Threading.DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false,
                    System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
                File.WriteAllText(outFile, w.LastBuildTestReport, new UTF8Encoding(false));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(outFile, "自检异常: " + ex, new UTF8Encoding(false));
                Shutdown(1);
            }
            return;
        }

        // 「换了台电脑」自检：拿一份路径全是别人机器的存档，看能不能自动修好
        if (e.Args.Any(a => a.Equals("--movedtest", StringComparison.OrdinalIgnoreCase)))
        {
            string outFile = Path.Combine(AppContext.BaseDirectory, "movedtest_result.txt");
            var lines = new List<string>();
            try
            {
                var w = new MainWindow();
                var p = Sts2CharForge.Core.Generation.ProfileFactory.Sample();
                p.Paths.VanillaProject = @"C:\别人的电脑\SlayTheSpire2_解包工程";
                p.Paths.GameDataDir = @"C:\别人的电脑\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64";
                p.Paths.GodotExe = @"C:\别人的电脑\Godot_v4.5.1-stable_mono_win64_console.exe";
                p.Paths.InstallDir = @"C:\别人的电脑\Steam\steamapps\common\Slay the Spire 2\mods";
                p.Paths.OutputDir = @"C:\别人的电脑\输出";

                string tmp = Path.Combine(Path.GetTempPath(), "forge_movedtest.json");
                Sts2CharForge.Core.Generation.ProfileFactory.Save(p, tmp);

                lines.Add("载入别人机器上的存档：" + (w.LoadProfileCore(tmp) ? "成功" : "失败"));
                lines.Add("修正后 游戏 data 目录：" + w.Profile.Paths.GameDataDir);
                lines.Add("修正后 安装目录      ：" + w.Profile.Paths.InstallDir);
                lines.Add("修正后 本体工程目录  ：" + (w.Profile.Paths.VanillaProject ?? "(空)"));
                lines.Add("效果库：" + w.CatalogStatus);
                lines.Add("增益/减益数量：" + w.AllPowers.Count);
                lines.Add("环境自检：" + w.EnvSummaryText);
                foreach (var it in w.EnvItems) lines.Add("  [" + it.Level + "] " + it.Name + " — " + it.Detail);
                File.WriteAllText(outFile, string.Join(Environment.NewLine, lines), new UTF8Encoding(false));
                Shutdown(w.AllPowers.Count > 0 ? 0 : 2);
            }
            catch (Exception ex)
            {
                File.WriteAllText(outFile, "自检异常: " + ex, new UTF8Encoding(false));
                Shutdown(1);
            }
            return;
        }

        // 环境自检模式：把「缺什么、怎么修」写成文本报告（用于自动化验证）
        if (e.Args.Any(a => a.Equals("--envcheck", StringComparison.OrdinalIgnoreCase)))
        {
            string outFile = Path.Combine(AppContext.BaseDirectory, "envcheck_result.txt");
            try
            {
                var w = new MainWindow();
                // 允许把某个存档传进来检查（用于验证「缺东西时能不能正确报出来」）
                string? profArg = e.Args.FirstOrDefault(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                if (profArg is not null && File.Exists(profArg)) w.LoadProfileCore(profArg);
                File.WriteAllText(outFile, w.EnvCheckReport(), new UTF8Encoding(false));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(outFile, "自检异常: " + ex, new UTF8Encoding(false));
                Shutdown(1);
            }
            return;
        }

        // 新建存档自检：模拟「启动窗口里点新建配置」之后的完整流程（不弹对话框）
        if (e.Args.Any(a => a.Equals("--newprofiletest", StringComparison.OrdinalIgnoreCase)))
        {
            string outFile = Path.Combine(AppContext.BaseDirectory, "newprofiletest_result.txt");
            try
            {
                var w = new MainWindow();
                string temp = Path.Combine(Path.GetTempPath(), "forge_newprofiletest");
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { /* 忽略 */ }
                w.OverrideProfileFolderForTest(temp);

                // 用真实启动时的默认 ShutdownMode，才能抓出「启动窗口一关程序就退出」这类问题
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                bool ok = w.PromptForProfileAtStartupWithAutoClick();
                if (!ok)
                {
                    File.WriteAllText(outFile, "启动窗口返回 false（没选存档）", new UTF8Encoding(false));
                    Shutdown(1);
                    return;
                }
                w.Show();
                ShutdownMode = ShutdownMode.OnMainWindowClose;

                // 等 3 秒再检查窗口是否还活着
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    string[] files = Directory.Exists(temp) ? Directory.GetFiles(temp, "*.json") : [];
                    File.WriteAllText(outFile,
                        $"存档目录: {temp}{Environment.NewLine}" +
                        $"新建出的存档: {files.Length} 个 → {string.Join(", ", files.Select(Path.GetFileName))}{Environment.NewLine}" +
                        $"3 秒后主窗口仍可见: {w.IsVisible}{Environment.NewLine}" +
                        $"存档列表项: {w.Profiles.Count}{Environment.NewLine}" +
                        $"状态栏: {w.StatusText}{Environment.NewLine}",
                        new UTF8Encoding(false));
                    Shutdown(0);
                };
                t.Start();
                return;
            }
            catch (Exception ex)
            {
                File.WriteAllText(outFile, "自检异常: " + ex, new UTF8Encoding(false));
                Shutdown(1);
                return;
            }
        }

        // 正常启动：先弹「配置存档」窗口，不选存档就不进主界面（直接退出）。
        // 关键：先切成 OnExplicitShutdown —— 否则启动窗口一关闭，程序里就一个窗口都没有了，
        // 默认的 OnLastWindowClose 会直接触发退出（表现就是「点新建配置没反应 / 存档没建出来」）。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var win = new MainWindow();
        if (!win.PromptForProfileAtStartup())
        {
            Shutdown(0);
            return;
        }
        win.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }
}
