using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sts2CharForge.Core.Generation;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.App;

/// <summary>
/// 启动时先弹的「配置存档」窗口：不载入/新建存档就关掉它，主界面不会出现（程序直接退出）。
/// </summary>
public partial class StartupProfileDialog : Window
{
    private readonly string _folder;

    /// <summary>选中的存档文件路径（点「载入选中存档」或双击后有效）。</summary>
    public string? SelectedProfilePath { get; private set; }

    /// <summary>是否点了「新建配置」。</summary>
    public bool NewProfileRequested { get; private set; }

    public sealed class Entry
    {
        public string Name { get; init; } = "";
        public string Path { get; init; } = "";
        public string Detail { get; init; } = "";
        public override string ToString() => Name;
    }

    public StartupProfileDialog(string folder)
    {
        InitializeComponent();
        _folder = folder;
        Refresh();
    }

    private void Refresh()
    {
        var list = new List<Entry>();
        try
        {
            if (Directory.Exists(_folder))
            {
                foreach (string f in Directory.GetFiles(_folder, "*.json"))
                {
                    string who = "", cards = "";
                    try
                    {
                        var p = ProfileFactory.Load(f);
                        who = string.IsNullOrWhiteSpace(p.DisplayName) ? p.ModId : p.DisplayName;
                        cards = $"卡牌 {p.Cards.Count} / 遗物 {p.Relics.Count} / 药水 {p.Potions.Count}";
                    }
                    catch { who = "(无法解析)"; }

                    list.Add(new Entry
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        Path = f,
                        Detail = $"{who}   {cards}   修改时间 {File.GetLastWriteTime(f):yyyy-MM-dd HH:mm}",
                    });
                }
            }
        }
        catch { /* 存档目录读不了就显示空列表 */ }

        ProfileList.ItemsSource = list.OrderByDescending(e => File.GetLastWriteTime(e.Path)).ToList();
        if (ProfileList.Items.Count > 0) ProfileList.SelectedIndex = 0;
    }

    private void OnLoad(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not Entry en)
        {
            MessageBox.Show(this, "请先在列表里选中一个存档。", "Sts2CharForge", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedProfilePath = en.Path;
        DialogResult = true;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => OnLoad(sender, e);

    private void OnNew(object sender, RoutedEventArgs e)
    {
        NewProfileRequested = true;
        DialogResult = true;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not Entry en) return;
        if (MessageBox.Show(this, $"确定删除存档「{en.Name}」吗？", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { File.Delete(en.Path); } catch (Exception ex) { MessageBox.Show(this, "删除失败：" + ex.Message); }
        Refresh();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + _folder + "\"") { UseShellExecute = true });
        }
        catch { /* 忽略 */ }
    }

    /// <summary>自检用：等价于点「新建配置」（让自动化测试能走完真实的关闭流程）。</summary>
    public void ChooseNewProfileForTest()
    {
        NewProfileRequested = true;
        DialogResult = true;
    }

    private void OnQuit(object sender, RoutedEventArgs e) => DialogResult = false;
}
