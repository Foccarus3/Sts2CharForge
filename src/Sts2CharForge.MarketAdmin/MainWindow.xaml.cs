using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Sts2CharForge.Core.Plugins;

namespace Sts2CharForge.MarketAdmin;

public partial class MainWindow : Window
{
    private static readonly (string Id, string Label)[] Categories =
    [
        ("effects", "效果种类"),
        ("conditions", "条件"),
        ("triggers", "触发时机"),
        ("forge", "锻造模块"),
        ("animation", "动画 / 视觉"),
        ("ui", "UI / 页面"),
        ("content", "卡牌 / 遗物 / 药水扩展"),
        ("tools", "工具 / 自动化"),
        ("assets", "资源包"),
        ("other", "其他"),
    ];

    private readonly Dictionary<string, CheckBox> _categoryBoxes = new(StringComparer.OrdinalIgnoreCase);
    private MarketPublisher.PublishResult? _last;
    private List<MarketPlugin> _existing = new();
    private bool _fillingExisting;

    private sealed class ExistingItem
    {
        public required MarketPlugin Plugin { get; init; }
        public override string ToString()
        {
            var v = Plugin.Latest;
            return $"{Plugin.Name}  ({Plugin.Id})  作者 {Plugin.Author}  最新 {v?.Version ?? "?"}";
        }
    }

    private sealed class AdminConfig
    {
        public string MarketRepo { get; set; } = "";
        public string RawBaseUrl { get; set; } = "https://raw.githubusercontent.com/Foccarus3/Sts2CharForge/main/market/packages";
    }

    private string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sts2CharForge", "marketadmin.json");

    public MainWindow()
    {
        InitializeComponent();
        foreach (var (id, label) in Categories)
        {
            var cb = new CheckBox { Content = label, Tag = id, Margin = new Thickness(0, 2, 14, 2) };
            _categoryBoxes[id] = cb;
            CategoryPanel.Children.Add(cb);
        }
        LoadConfig();
        ZipBox.Text = "";
        ExistingSearchBox.TextChanged += (_, _) => FillExistingCombo();
        StatusText.Text = "选择第三方插件 ZIP，点「解析 ZIP」；或从「已有插件」里选一个删除。";
        Loaded += (_, _) => RefreshExisting();
    }

    private void LoadConfig()
    {
        RawBaseBox.Text = "https://raw.githubusercontent.com/Foccarus3/Sts2CharForge/main/market/packages";
        MinGenBox.Text = "V0.3.74";
        GameVersionBox.Text = "v0.113.x";
        try
        {
            if (File.Exists(ConfigPath))
            {
                var c = JsonSerializer.Deserialize<AdminConfig>(File.ReadAllText(ConfigPath));
                if (c is not null)
                {
                    RepoBox.Text = c.MarketRepo;
                    RawBaseBox.Text = string.IsNullOrWhiteSpace(c.RawBaseUrl) ? RawBaseBox.Text : c.RawBaseUrl;
                    return;
                }
            }
        }
        catch { }
        if (Directory.Exists(@"D:\ds\s\t3")) RepoBox.Text = @"D:\ds\s\t3";
    }

    private void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new AdminConfig
            {
                MarketRepo = RepoBox.Text.Trim(),
                RawBaseUrl = RawBaseBox.Text.Trim(),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 配置是便利功能，失败不挡发布 */ }
    }

    private void OnRefreshExisting(object sender, RoutedEventArgs e) => RefreshExisting();

    private void RefreshExisting()
    {
        try
        {
            string repo = RepoBox.Text.Trim();
            if (repo.Length == 0 || !Directory.Exists(repo))
            {
                _existing = new();
                FillExistingCombo();
                StatusText.Text = "填好市场仓库根目录后点「刷新已有插件」。";
                return;
            }
            var index = MarketPublisher.ReadIndex(repo);
            _existing = (index.Plugins ?? new List<MarketPlugin>())
                .Where(p => p is not null)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            FillExistingCombo();
            StatusText.Text = _existing.Count == 0
                ? "市场里还没有插件。"
                : $"已读取 {_existing.Count} 个已有插件；选择后可以直接删除。";
        }
        catch (Exception ex)
        {
            _existing = new();
            FillExistingCombo();
            StatusText.Text = "读取已有插件失败：" + ex.Message;
        }
    }

    private void FillExistingCombo()
    {
        if (ExistingBox is null) return;
        _fillingExisting = true;
        try
        {
            string q = ExistingSearchBox?.Text.Trim() ?? "";
            var list = _existing.Where(p =>
                q.Length == 0
                || p.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.Author.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (p.Keywords ?? new List<string>()).Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            ExistingBox.Items.Clear();
            foreach (var p in list) ExistingBox.Items.Add(new ExistingItem { Plugin = p });
            if (ExistingBox.Items.Count > 0) ExistingBox.SelectedIndex = 0;
            else
            {
                ExistingVersionBox.Items.Clear();
                ExistingVersionBox.Items.Add("（无）");
            }
        }
        finally { _fillingExisting = false; }
    }

    private void OnExistingPluginSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingExisting) return;
        if (ExistingBox.SelectedItem is not ExistingItem item) return;
        var p = item.Plugin;
        _fillingExisting = true;
        try
        {
            IdBox.Text = p.Id;
            NameBox.Text = p.Name;
            AuthorBox.Text = p.Author;
            DescBox.Text = p.Description;
            LicenseBox.Text = p.License;
            ExistingVersionBox.Items.Clear();
            foreach (var v in (p.Versions ?? new List<MarketVersion>())
                         .OrderByDescending(v => v.Version, StringComparer.OrdinalIgnoreCase))
                ExistingVersionBox.Items.Add(v.Version);
            if (ExistingVersionBox.Items.Count > 0)
            {
                ExistingVersionBox.SelectedIndex = 0;
                VersionBox.Text = ExistingVersionBox.SelectedItem?.ToString() ?? "";
            }
            else
            {
                ExistingVersionBox.Items.Add("（无版本）");
                ExistingVersionBox.SelectedIndex = 0;
                VersionBox.Text = "";
            }
            NotesBox.Text = "";
            StatusText.Text = $"已选中：{p.Name}（{p.Id}）。可以点「删除当前版本」或「删除整个插件」。";
        }
        finally { _fillingExisting = false; }
    }

    private void OnExistingVersionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingExisting) return;
        if (ExistingVersionBox.SelectedItem is string v && !v.StartsWith("（", StringComparison.Ordinal))
            VersionBox.Text = v;
    }

    private void OnConvertRar(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "RAR 压缩包 (*.rar)|*.rar", Title = "选择插件 RAR" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            StatusText.Text = "正在解压 RAR 并转换成 ZIP…";
            string zip = RarConverter.ConvertToZip(dlg.FileName);
            ZipBox.Text = zip;
            StatusText.Text = "RAR 已转换成 ZIP：\n" + zip + "\n下面会自动解析，检查名称 / 版本 / 脚本。";
            OnInspect(sender, e);
        }
        catch (Exception ex)
        {
            StatusText.Text = "RAR 转换失败：" + ex.Message;
        }
    }

    private void OnBrowseZip(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "插件包 (*.zip)|*.zip", Title = "选择第三方插件 ZIP" };
        if (dlg.ShowDialog(this) == true) ZipBox.Text = dlg.FileName;
    }

    private void OnInspect(object sender, RoutedEventArgs e)
    {
        try
        {
            var info = MarketPublisher.Inspect(ZipBox.Text);
            IdBox.Text = info.Id;
            NameBox.Text = info.Name;
            AuthorBox.Text = info.Author;
            VersionBox.Text = info.Version;
            DescBox.Text = info.Description;
            LicenseBox.Text = string.IsNullOrWhiteSpace(info.License) ? "" : info.License;
            KeywordsBox.Text = string.Join(" ", GuessKeywords(info));
            AutoCheckCategories(info);
            StatusText.Text = info.ScriptFiles.Count == 0
                ? "解析成功。"
                : $"解析成功。检测到 {info.ScriptFiles.Count} 个安装辅助脚本，生成市场包时会自动移除：\n"
                  + string.Join("\n", info.ScriptFiles);
            UpdatePreview();
        }
        catch (Exception ex)
        {
            StatusText.Text = "解析失败：" + ex.Message;
        }
    }

    private static IEnumerable<string> GuessKeywords(MarketPublisher.PackageInfo info)
    {
        var parts = (info.Name + " " + info.Description)
            .Split([' ', '/', '、', '，', ',', '；', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length is >= 2 and <= 12)
            .Take(6);
        return parts.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void AutoCheckCategories(MarketPublisher.PackageInfo info)
    {
        string text = (info.Name + " " + info.Description).ToLowerInvariant();
        foreach (var cb in _categoryBoxes.Values) cb.IsChecked = false;
        void Check(string id) { if (_categoryBoxes.TryGetValue(id, out var cb)) cb.IsChecked = true; }

        if (text.Contains("gif") || text.Contains("动画") || text.Contains("spine")) Check("animation");
        if (text.Contains("效果") || text.Contains("effect")) Check("effects");
        if (text.Contains("触发") || text.Contains("trigger")) Check("triggers");
        if (text.Contains("条件") || text.Contains("condition")) Check("conditions");
        if (text.Contains("锻造") || text.Contains("forge")) Check("forge");
        if (text.Contains("页面") || text.Contains("ui")) Check("ui");
        if (text.Contains("卡牌") || text.Contains("遗物") || text.Contains("药水")) Check("content");
        if (text.Contains("工具") || text.Contains("tool")) Check("tools");
        if (text.Contains("资源") || text.Contains("asset")) Check("assets");
        if (!_categoryBoxes.Values.Any(cb => cb.IsChecked == true)) Check("other");
    }

    private List<string> CheckedCategories() =>
        _categoryBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

    private void OnBuild(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveConfig();
            var req = new MarketPublisher.PublishRequest
            {
                SourceZip = ZipBox.Text.Trim(),
                MarketRepo = RepoBox.Text.Trim(),
                RawBaseUrl = RawBaseBox.Text.Trim(),
                Id = IdBox.Text.Trim(),
                Name = NameBox.Text.Trim(),
                Author = AuthorBox.Text.Trim(),
                Version = VersionBox.Text.Trim(),
                Description = DescBox.Text.Trim(),
                License = LicenseBox.Text.Trim(),
                Notes = NotesBox.Text.Trim(),
                MinGeneratorVersion = MinGenBox.Text.Trim(),
                GameVersion = GameVersionBox.Text.Trim(),
                Categories = CheckedCategories(),
                Keywords = KeywordsBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            };
            if (MessageBox.Show(this,
                    $"生成市场包并写入索引？\n\n{req.Id} {req.Version}\n"
                    + "会改写包内 plugin.json 的 id / 名称 / 作者 / 版本 / 简介，并移除 .bat / .ps1。\n"
                    + "不会自动提交 git。",
                    "确认生成", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
            _last = MarketPublisher.Publish(req);
            StatusText.Text = $"生成成功。\n包：{_last.PackagePath}\nSHA256：{_last.Sha256}\n"
                              + $"索引：{_last.IndexPath}\n下载：{_last.DownloadUrl}\n"
                              + (_last.RemovedScripts.Count == 0 ? "" : "已移除脚本：" + string.Join(" / ", _last.RemovedScripts));
            UpdatePreview();
        }
        catch (Exception ex)
        {
            StatusText.Text = "生成失败：" + ex.Message;
        }
    }

    private void OnCommit(object sender, RoutedEventArgs e)
    {
        if (_last is null) { StatusText.Text = "先生成市场包。"; return; }
        try
        {
            var res = MarketPublisher.CommitAndPush(RepoBox.Text.Trim(),
                new[] { _last.PackagePath, _last.IndexPath },
                $"market: {(_last.NewPlugin ? "add" : "update")} {_last.Id} {_last.Version}");
            StatusText.Text = res.Success ? "提交并推送成功。\n" + res.Output : "提交 / 推送失败：\n" + res.Output;
        }
        catch (Exception ex) { StatusText.Text = "提交失败：" + ex.Message; }
    }

    private void OnDeleteVersion(object sender, RoutedEventArgs e)
    {
        string id = IdBox.Text.Trim();
        string version = VersionBox.Text.Trim();
        if (id.Length == 0 || version.Length == 0)
        {
            StatusText.Text = "删除指定版本需要先填 id 和版本号。";
            return;
        }
        if (MessageBox.Show(this,
                $"从市场删除「{id}」的版本 {version}？\n\n"
                + "会删除 market\\index.json 里这一条，并删除本地 market\\packages 里对应的包。\n"
                + "不会自动提交 git，也不会删除 GitHub Release 上的附件。",
                "确认删除版本", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        try
        {
            var r = MarketPublisher.Delete(RepoBox.Text.Trim(), id, version, deleteWholePlugin: false);
            _last = null;
            StatusText.Text = $"已删除版本：{r.Id} {r.Version}\n"
                              + (r.PluginRemoved ? "这个插件已经没有版本，条目也一起移除了。\n" : "")
                              + "索引备份：market\\index.json.bak\n"
                              + (r.DeletedPackages.Count == 0 ? "（没有找到本地包文件）" : "已删除包：\n" + string.Join("\n", r.DeletedPackages));
            RefreshExisting();
        }
        catch (Exception ex) { StatusText.Text = "删除失败：" + ex.Message; }
    }

    private void OnDeletePlugin(object sender, RoutedEventArgs e)
    {
        string id = IdBox.Text.Trim();
        if (id.Length == 0) { StatusText.Text = "删除整个插件需要先填 id。"; return; }
        if (MessageBox.Show(this,
                $"从市场彻底删除「{id}」？\n\n"
                + "会删除整个插件条目和所有本地市场包。\n"
                + "不会自动提交 git，也不会删除 GitHub Release 上的附件。",
                "确认删除插件", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        try
        {
            var r = MarketPublisher.Delete(RepoBox.Text.Trim(), id, null, deleteWholePlugin: true);
            _last = null;
            StatusText.Text = $"已删除插件：{r.Id}\n索引备份：market\\index.json.bak\n"
                              + (r.DeletedPackages.Count == 0 ? "（没有找到本地包文件）" : "已删除包：\n" + string.Join("\n", r.DeletedPackages));
            RefreshExisting();
        }
        catch (Exception ex) { StatusText.Text = "删除失败：" + ex.Message; }
    }

    private void OnOpenRepo(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = RepoBox.Text.Trim();
            if (!Directory.Exists(dir)) { StatusText.Text = "仓库目录不存在。"; return; }
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex) { StatusText.Text = "打开失败：" + ex.Message; }
    }

    private void UpdatePreview()
    {
        try
        {
            string id = IdBox.Text.Trim();
            string version = VersionBox.Text.Trim();
            string download = RawBaseBox.Text.Trim().TrimEnd('/') + "/" + id + "-" + version + ".zip";
            var preview = new
            {
                id,
                name = NameBox.Text.Trim(),
                author = AuthorBox.Text.Trim(),
                version,
                description = DescBox.Text.Trim(),
                license = LicenseBox.Text.Trim(),
                minGeneratorVersion = MinGenBox.Text.Trim(),
                categories = CheckedCategories(),
                keywords = KeywordsBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                download,
                sha256 = "(生成后填写)",
                publishedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            };
            PreviewBox.Text = JsonSerializer.Serialize(preview, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { PreviewBox.Text = ""; }
    }
}
