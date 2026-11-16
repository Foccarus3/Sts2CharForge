using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sts2CharForge.Core.Plugins;

namespace Sts2CharForge.App;

/// <summary>
/// 插件市场 MVP：只读索引 + 搜索 / 分类 / 关键词 + 本机收藏 + 下载安装。
/// 不做点赞 / 上传；上传走 GitHub PR（见 market/README.md）。
/// </summary>
internal sealed class PluginMarketWindow : Window
{
    private readonly string _programDir;
    private readonly Action _reloadPlugins;
    private readonly TextBox _url = new() { Text = PluginMarketplace.DefaultIndexUrl, Margin = new Thickness(0, 0, 6, 0) };
    private readonly TextBox _search = new() { Width = 180, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _category = new() { Width = 150, Margin = new Thickness(0, 0, 8, 0) };
    private readonly CheckBox _favoritesOnly = new() { Content = "只看收藏", VerticalAlignment = VerticalAlignment.Center };
    private readonly ListBox _list = new();
    private readonly TextBox _status = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
        Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 170)),
        Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly Button _install;
    private readonly Button _favoriteButton;
    private readonly Button _refresh;
    private readonly List<MarketPlugin> _all = new();
    private HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private MarketIndex? _index;

    private sealed class Item
    {
        public required MarketPlugin Plugin { get; init; }
        public bool Installed { get; init; }
        public bool Favorite { get; init; }

        public override string ToString()
        {
            var v = Plugin.Latest;
            string fav = Favorite ? "  ★ 已收藏" : "";
            string installed = Installed ? "  [已安装]" : "";
            string cats = string.Join(" / ", (Plugin.Categories ?? new List<string>()).Select(CategoryLabel));
            string keys = string.Join(" ", Plugin.Keywords ?? new List<string>());
            string uploaded = FormatPublished(v?.PublishedAt);
            return $"{Plugin.Name}  ({Plugin.Id})  作者 {Plugin.Author}  最新 {v?.Version ?? "?"}（{uploaded}）{fav}{installed}\n"
                 + $"    {Plugin.Description}\n"
                 + $"    分类：{(cats.Length == 0 ? "其他" : cats)}"
                 + (keys.Length == 0 ? "" : $"　关键词：{keys}");
        }

        private static string FormatPublished(string? publishedAt)
        {
            if (DateTimeOffset.TryParse(publishedAt, out var dt))
                return dt.ToLocalTime().ToString("yyyy-MM-dd");
            return "日期未标注";
        }
    }

    private sealed record CategoryOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly CategoryOption[] Categories =
    [
        new("all", "全部"),
        new("effects", "效果种类"),
        new("conditions", "条件"),
        new("triggers", "触发时机"),
        new("forge", "锻造模块"),
        new("animation", "动画 / 视觉"),
        new("ui", "UI / 页面"),
        new("content", "卡牌 / 遗物 / 药水扩展"),
        new("tools", "工具 / 自动化"),
        new("assets", "资源包"),
        new("other", "其他"),
    ];

    private static string CategoryLabel(string id) =>
        Categories.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))?.Label ?? id;

    public PluginMarketWindow(Window owner, string programDir, Action reloadPlugins)
    {
        _programDir = programDir;
        _reloadPlugins = reloadPlugins;
        LoadFavorites();

        Title = "插件市场";
        Width = 820; Height = 620;
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var filter = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var warn = new TextBlock
        {
            Text = "插件可以包含 C# 代码，会编译进你的模组。市场只做索引、下载和 SHA256 校验，"
                 + "不保证第三方插件安全；安装前请确认作者与来源。收藏只保存在本机。",
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
                Directory.CreateDirectory(PluginMarketplace.PluginsDirOf(_programDir));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = PluginMarketplace.PluginsDirOf(_programDir), UseShellExecute = true,
                });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开失败"); }
        };

        top.Children.Add(new TextBlock { Text = "索引地址：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        top.Children.Add(_url);
        top.Children.Add(_refresh);

        filter.Children.Add(new TextBlock { Text = "搜索：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        filter.Children.Add(_search);
        filter.Children.Add(new TextBlock { Text = "分类：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        filter.Children.Add(_category);
        filter.Children.Add(_favoritesOnly);
        _category.ItemsSource = Categories;
        _category.SelectedIndex = 0;
        _search.TextChanged += (_, _) => ApplyFilter();
        _category.SelectionChanged += (_, _) => ApplyFilter();
        _favoritesOnly.Checked += (_, _) => ApplyFilter();
        _favoritesOnly.Unchecked += (_, _) => ApplyFilter();

        _install = new Button { Content = "安装选中的最新版", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        _install.Click += async (_, _) => await InstallAsync();
        _favoriteButton = new Button { Content = "收藏选中插件", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        _favoriteButton.Click += (_, _) => ToggleFavorite();
        var uninstall = new Button { Content = "卸载选中插件", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        uninstall.Click += (_, _) => UninstallSelected();
        var close = new Button { Content = "关闭", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
        close.Click += (_, _) => Close();
        bottom.Children.Add(_install);
        bottom.Children.Add(_favoriteButton);
        bottom.Children.Add(uninstall);
        bottom.Children.Add(openFolder);
        bottom.Children.Add(close);

        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(filter, Dock.Top);
        DockPanel.SetDock(warn, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(filter);
        root.Children.Add(warn);
        root.Children.Add(bottom);
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        Loaded += async (_, _) => await RefreshAsync();
    }

    private string FavoritesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sts2CharForge", "market_favorites.json");

    private void LoadFavorites()
    {
        try
        {
            if (!File.Exists(FavoritesPath)) return;
            var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FavoritesPath));
            if (ids is not null) _favorites = new HashSet<string>(ids.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        }
        catch { /* 收藏是附加功能：读失败就从空开始 */ }
    }

    private void SaveFavorites()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FavoritesPath)!);
            File.WriteAllText(FavoritesPath, JsonSerializer.Serialize(_favorites.OrderBy(x => x).ToList()));
        }
        catch (Exception ex) { _status.Text = "收藏保存失败：" + ex.Message; }
    }

    private async Task RefreshAsync()
    {
        _refresh.IsEnabled = false;
        _status.Text = "正在读取市场索引…";
        try
        {
            _index = await PluginMarketplace.LoadIndexAsync(_url.Text);
            _all.Clear();
            foreach (var p in (_index.Plugins ?? new List<MarketPlugin>()).Where(x => x is not null))
                _all.Add(p);
            ApplyFilter();
            _status.Text = _all.Count == 0
                ? "市场里还没有插件。发布方式见仓库 market/README.md（GitHub PR）。"
                : $"共 {_all.Count} 个插件；按最新版本排序。收藏只保存在本机，不会上传。";
        }
        catch (Exception ex)
        {
            _status.Text = "读取失败：" + ex.Message;
        }
        finally { _refresh.IsEnabled = true; }
    }

    private void ApplyFilter()
    {
        if (_list is null) return;
        string query = _search.Text.Trim();
        string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string cat = (_category.SelectedItem as CategoryOption)?.Id ?? "all";
        bool favOnly = _favoritesOnly.IsChecked == true;

        var filtered = _all.Where(p =>
        {
            if (favOnly && !_favorites.Contains(p.Id)) return false;
            if (cat != "all" && !(p.Categories ?? new List<string>()).Any(c => string.Equals(c, cat, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (terms.Length == 0) return true;
            string haystack = string.Join("\n", new[]
            {
                p.Name, p.Id, p.Author, p.Description,
                string.Join(" ", p.Keywords ?? new List<string>()),
                string.Join(" ", (p.Categories ?? new List<string>()).Select(CategoryLabel)),
            });
            return terms.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
        })
        .OrderByDescending(p => ParseDate(p.Latest?.PublishedAt) ?? DateTimeOffset.MinValue)
        .ThenByDescending(p => p.Latest?.Version ?? "", StringComparer.OrdinalIgnoreCase)
        .ToList();

        var selectedId = (_list.SelectedItem as Item)?.Plugin.Id;
        _list.Items.Clear();
        foreach (var p in filtered) _list.Items.Add(new Item
        {
            Plugin = p,
            Installed = PluginMarketplace.InstalledDir(_programDir, p.Id) is not null,
            Favorite = _favorites.Contains(p.Id),
        });
        if (_list.Items.Count > 0)
        {
            int select = string.IsNullOrWhiteSpace(selectedId) ? 0
                : Math.Max(0, filtered.FindIndex(p => string.Equals(p.Id, selectedId, StringComparison.OrdinalIgnoreCase)));
            _list.SelectedIndex = select;
        }
    }

    private static DateTimeOffset? ParseDate(string? s) =>
        DateTimeOffset.TryParse(s, out var d) ? d : null;

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
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _status.Text = "安装失败：" + ex.Message;
        }
        finally { _install.IsEnabled = true; }
    }

    private void ToggleFavorite()
    {
        if (_list.SelectedItem is not Item item) { _status.Text = "先选中一个插件。"; return; }
        if (_favorites.Contains(item.Plugin.Id))
        {
            _favorites.Remove(item.Plugin.Id);
            _status.Text = $"已取消收藏：{item.Plugin.Name}";
        }
        else
        {
            _favorites.Add(item.Plugin.Id);
            _status.Text = $"已收藏：{item.Plugin.Name}";
        }
        SaveFavorites();
        ApplyFilter();
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
            ApplyFilter();
        }
        catch (Exception ex) { _status.Text = "卸载失败：" + ex.Message; }
    }
}
