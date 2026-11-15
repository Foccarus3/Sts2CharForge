using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Sts2CharForge.MarketAdmin;

public partial class App : Application
{
    private static string LogPath => Path.Combine(Path.GetTempPath(), "Sts2CharForge_MarketAdmin_startup.log");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("AppDomain: " + e.ExceptionObject);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("startup " + e.Args.Length + " arg(s) " + string.Join(" ", e.Args));
        if (e.Args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            Log("selftest ok");
            Environment.Exit(0);
            return;
        }
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("Dispatcher: " + e.Exception);
        MessageBox.Show("市场发布器启动失败：\n" + e.Exception.Message
            + "\n\n详细日志：" + LogPath, "Sts2CharForge MarketAdmin",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    private static void Log(string text)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("s") + " " + text + "\n"); } catch { }
    }
}
