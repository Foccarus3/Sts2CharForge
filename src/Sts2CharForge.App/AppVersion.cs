using System.Reflection;

namespace Sts2CharForge.App;

/// <summary>
/// 整合包的版本号（<b>唯一来源</b>）：<c>Sts2CharForge.App.csproj</c> 里的 <c>InformationalVersion</c>。
///
/// 启动器文件名（<c>启动 Sts2CharForge_V0.0.1.bat</c>）、程序包 zip、整合包 zip 都由打包脚本读同一个值，
/// 所以只要改那一行、重新打包，三处就一起变。每次改动往上加一位：V0.0.1 → V0.0.2 → …
/// </summary>
public static class AppVersion
{
    /// <summary>形如 <c>V0.0.1</c>；读不到就退回 <c>V0.0.1</c>（不会显示成空）。</summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        try
        {
            string? raw = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                // .NET 8+ 默认会追加 "+<git commit>"，这里只取版本号本身
                int plus = raw.IndexOf('+');
                string v = (plus >= 0 ? raw[..plus] : raw).Trim();
                if (v.Length > 0) return v;
            }
        }
        catch
        {
            // 读不到就算了，下面给默认值
        }
        return "V0.0.1";
    }
}
