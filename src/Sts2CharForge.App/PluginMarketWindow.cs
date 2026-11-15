using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sts2CharForge.Core.Plugins;

namespace Sts2CharForge.App;

/// <summary>
/// 插件市场 MVP：只读索引 + 下载安装。上传走 GitHub PR（见 market/README.md）。
/// </summary>
internal sealed class PluginMarketWindow : Window
{
    private readonly string _programDir;
    private readonly Action _reloadPlugins;
    private readonly TextBox _url = new() { Text = PluginMarketplace.DefaultIndexUrl, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ListBox _list = new();
    private readonly TextBox _status = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
        Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 170)),
        Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly Button _install;
    private readonly Button _refresh;
    private MarketIndex? _index;

    private sealed class Item
    {
        public required MarketPlugin Plugin { get; init; }
        public bool Installed { get; init; }
        public override string ToString()
        {
            var v = Plugin.Latest;
            string installed = Installed ? "  [已安装]" : "";
            return $"{Plugin.Name}  ({Plugin.Id})  作者 {Plugin.Author}  最新 {v?.Version ?? "?"}{installed}\n    {Plugin.Description}";
        }
    }

    public PluginMarketWindow(Window owner, string programDir, Action reloadPlugins)
    {
        _programDir = programDir;
        _reloadPlugins = reloadPlugins;
        Title = "插件市场（MVP）";
        Width = 760; Height = 560;
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var warn = new TextBlock
        {
            Text = "插件可以包含 C# 代码，会编译进你的模组。市场只做索引、下载和 SHA256 校验，"
                 + "不保证第三方插件安全；安装前请确认作者与来源。上传走 GitHub PR，不在客户端里直接上传。",
            TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(160, 80, 0)),
            Margin = new Thickness(0, 0, 0, 8),
        };
        _refresh = new Button { Content = "刷新目录", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        _refresh.Click += async (_, _) => await RefreshAsync();
        var openFolder = new Button { Content = "打开插件文件夹", Padding = new Thickness(10, 4, 10, 4) };
        openFolder.Click += (_, _) =>
        {
            try
            {
                System.IO.Directory.CreateDirectory(PluginMarketplace.PluginsDirOf(_programDir));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = PluginMarketplace.PluginsDirOf(_programDir), UseShellExecute = true,
                });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开失败"); }
        };

        DockPanel.SetDock(_url, Dock.Top);
        top.Children.Add(new TextBlock { Text = "索引地址：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        top.Children.Add(_url);
        top.Children.Add(_refresh);

        _install = new Button { Content = "安装选中的最新版", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        _install.Click += async (_, _) => await InstallAsync();
        var uninstall = new Button { Content = "卸载选中插件", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        uninstall.Click += (_, _) => UninstallSelected();
        var close = new Button { Content = "关闭", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
        close.Click += (_, _) => Close();
        bottom.Children.Add(_install);
        bottom.Children.Add(uninstall);
        bottom.Children.Add(openFolder);
        bottom.Children.Add(close);

        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(warn, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(warn);
        root.Children.Add(bottom);
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _refresh.IsEnabled = false;
        _status.Text = "正在读取市场索引…";
        try
        {
            _index = await PluginMarketplace.LoadIndexAsync(_url.Text);
            _list.Items.Clear();
            foreach (var p in (_index.Plugins ?? new List<MarketPlugin>()).Where(x => x is not null))
                _list.Items.Add(new Item
                {
                    Plugin = p,
                    Installed = PluginMarketplace.InstalledDir(_programDir, p.Id) is not null,
                });
            _status.Text = _list.Items.Count == 0
                ? "市场里还没有插件。发布方式见仓库 market/README.md（GitHub PR）。"
                : $"共 {_list.Items.Count} 个插件。安装只会解压到 plugins/<id>/，不会执行插件代码。";
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _status.Text = "读取失败：" + ex.Message;
        }
        finally { _refresh.IsEnabled = true; }
    }

    private async Task InstallAsync()
    {
        if (_list.SelectedItem is not Item item) { _status.Text = "先选中一个插件。"; return; }
        var v = item.Plugin.Latest;
        if (v is null) { _status.Text = "这个插件没有版本信息。"; return; }
        if (MessageBox.Show(this,
                $"安装「{item.Plugin.Name}」{v.Version}？\n\n"
                + "注意：插件可以包含 C# 代码，会编译进你的模组。\n"
                + "市场只校验 SHA256 和包结构，不保证第三方插件安全。",
                "确认安装", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        _install.IsEnabled = false;
        _status.Text = "正在下载安装…";
        try
        {
            var result = await PluginMarketplace.InstallAsync(item.Plugin, v, _programDir);
            _status.Text = $"已安装：{result.Name} {result.Version}\n{result.Path}";
            _reloadPlugins();
            _list.Items.Refresh();
        }
        catch (Exception ex)
        {
            _status.Text = "安装失败：" + ex.Message;
        }
        finally { _install.IsEnabled = true; }
    }

    private void UninstallSelected()
    {
        if (_list.SelectedItem is not Item item) { _status.Text = "先选中一个插件。"; return; }
        try
        {
            if (!PluginMarketplace.Uninstall(_programDir, item.Plugin.Id))
            {
                _status.Text = "这个插件没有安装。";
                return;
            }
            _status.Text = $"已卸载：{item.Plugin.Name}";
            _reloadPlugins();
            _list.Items.Refresh();
        }
        catch (Exception ex) { _status.Text = "卸载失败：" + ex.Message; }
    }
}
