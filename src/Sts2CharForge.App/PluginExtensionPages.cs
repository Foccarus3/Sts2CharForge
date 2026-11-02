using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Sts2CharForge.Core.Plugins;

namespace Sts2CharForge.App;

/// <summary>
/// V0.2.388（需求书第 4.2 节）：把**编辑器扩展页面**渲染进主界面。
///
/// <para>每个页面独立 try/catch：某一页崩了只显示一条错误，不影响别的页、不影响主程序
/// （需求书 6.3「出错隔离」）。所有面板都由 <see cref="EditorPanelAdapter"/> 提供，
/// 插件拿不到任意 WPF。</para>
/// </summary>
public static class PluginExtensionPages
{
    /// <summary>装上面板工厂（主程序启动时调一次）。</summary>
    public static void Install() => EditorExtensionRegistry.PanelFactory = id => new EditorPanelAdapter(id);

    /// <summary>
    /// 渲染某个插件的扩展页面。返回 null = 这个插件没有加载成功的扩展（调用方不必显示这一块）。
    /// </summary>
    public static UIElement? BuildSection(PluginSpec plugin, string pluginDir, string outputDir)
    {
        string id = (plugin?.Id ?? "").Trim();
        var status = EditorExtensionRegistry.StatusOf(id);
        var pages = EditorExtensionRegistry.PagesOf(id);
        if (pages.Count == 0) return null;

        var host = new StackPanel { Orientation = Orientation.Vertical };
        host.Children.Add(new TextBlock
        {
            Text = "编辑器扩展：" + (status?.Summary ?? ""),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 4),
        });

        var tabs = new TabControl { MinHeight = 320 };
        foreach (var page in pages)
        {
            var body = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4) };
            string title = "（未命名页）";
            try
            {
                title = string.IsNullOrWhiteSpace(page.Title) ? page.Id : page.Title;
                object built = page.Build();
                if (built is EditorPanelAdapter adapter)
                {
                    body.Children.Add(adapter.Root);
                }
                else if (built is FrameworkElement fe)
                {
                    // 插件绕开受控面板直接给了 WPF 元素：不拒绝，但在审核日志里记一笔
                    EditorExtensionRegistry.StatusOf(id);
                    body.Children.Add(new TextBlock
                    {
                        Text = "⚠ 这一页返回的不是受控面板（IEditorPanel），而是任意 WPF 元素 —— 已按普通控件显示，但这类插件需要人工审核。",
                        Foreground = System.Windows.Media.Brushes.DarkOrange,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 4),
                    });
                    body.Children.Add(fe);
                }
                else
                {
                    body.Children.Add(new TextBlock
                    {
                        Text = "这一页 Build() 没有返回受控面板（返回了 " + (built?.GetType().Name ?? "null") + "）。",
                        Foreground = System.Windows.Media.Brushes.Firebrick,
                        TextWrapping = TextWrapping.Wrap,
                    });
                }
            }
            catch (Exception ex)
            {
                // 插件页面自己炸了：这一页显示错误，别的页照样能用
                body.Children.Add(new TextBlock
                {
                    Text = "页面创建失败（已隔离）：" + ex.GetType().Name + "：" + ex.Message,
                    Foreground = System.Windows.Media.Brushes.Firebrick,
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        }
        host.Children.Add(tabs);
        return host;
    }
}
