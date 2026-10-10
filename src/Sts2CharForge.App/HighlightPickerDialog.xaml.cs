using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Sts2CharForge.Core.Generation;

namespace Sts2CharForge.App;

/// <summary>
/// 「升级后的自定义描述」底下那个「高亮字段…」按钮点开的**多选列表**：
/// 逐字段挑哪些数值占位符写成本体的 <c>{Damage:diff()}</c>（升级后按差异变绿）。
///
/// 粒度 = **这张牌每一条效果各自声明的动态变量**（候选来自 <see cref="CSharpCodeGen.ParamHints"/>，
/// 第二列就是那条效果的中文名）。这不是「数值 / 关键词 / 描述文字」那种粗分类 ——
/// 本体只对**占位符的值**做差异高亮，粗分类映射不到任何可写的语法。
///
/// 三条规矩（见 <see cref="HighlightDiffText"/> 的注释）：
/// 1. 初值 = **从正文里反推**（正文里已经是 <c>{X:diff()}</c> 的就预先勾上）；
/// 2. 正文里**没出现**的字段，勾选框是灰的（勾它不会往你的文案里加字）；
/// 3. 点「确定」只做精确文本替换，一个字都不加。
///
/// 自检（<c>--uicheck</c>）会真的 new 一个这个窗口、改 <see cref="Rows"/> 里的勾选、再调
/// <see cref="ApplyForTest"/> 走一遍完整流程，所以断言测的是**真的这一套**而不是复制的逻辑。
/// </summary>
public partial class HighlightPickerDialog : Window
{
    /// <summary>列表里的一行 = 一个候选字段。</summary>
    public sealed class Row : INotifyPropertyChanged
    {
        private bool _isChecked;

        /// <summary>变量键（<c>Damage</c> / <c>PetHp</c> …）。</summary>
        public string Key { get; init; } = "";

        /// <summary>中文效果名（那条效果的种类显示名）。</summary>
        public string Desc { get; init; } = "";

        /// <summary>列表主文字：勾上会写成什么。</summary>
        public string Display => HighlightDiffText.DiffToken(Key) + (Desc.Length == 0 ? "" : " — " + Desc);

        /// <summary>这一行能不能改（正文里没这个占位符 = 灰的）。</summary>
        public bool CanChange { get; init; }

        /// <summary>右边那行状态说明（已经是高亮 / 现在是普通 / 这段里还没用到）。</summary>
        public string Status { get; init; } = "";

        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; PropertyChanged?.Invoke(this, new(nameof(IsChecked))); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly string _text;

    public ObservableCollection<Row> Rows { get; } = new();

    /// <summary>点「确定」之后为 true（其它两个按钮为 false）。</summary>
    public bool Confirmed { get; private set; }

    /// <summary>点「确定」后重写出来的文本（没点确定时 = 原文，一个字都没动）。</summary>
    public string ResultText { get; private set; } = "";

    /// <summary>点「确定」时真正改了几处。</summary>
    public int ChangedCount { get; private set; }

    /// <summary>每个键改了几处（界面汇总 + 自检用）。</summary>
    public Dictionary<string, int> PerKey { get; private set; } = new(StringComparer.Ordinal);

    /// <summary>用户在取消 / 关窗前最后看到的勾选（自检用来看「初值是不是从正文反推的」）。</summary>
    public List<string> CheckedKeys => Rows.Where(r => r.IsChecked).Select(r => r.Key).ToList();

    public HighlightPickerDialog(string cardName, string text, IReadOnlyList<(string Token, string Desc)> hints)
    {
        InitializeComponent();
        _text = text ?? "";
        ResultText = _text;
        Title = "升级后要高亮的字段 —— " + cardName;
        WhoText.Text = "升级后要高亮的字段 —— " + cardName;

        // 正文里**现在**是高亮的键 / 正文里用到的键（含普通写法）
        var highlighted = new HashSet<string>(HighlightDiffText.HighlightedKeys(_text), StringComparer.Ordinal);
        var used = new HashSet<string>(HighlightDiffText.UsedKeys(_text), StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (token, desc) in hints ?? Array.Empty<(string, string)>())
        {
            string key = HighlightDiffText.KeyOfToken(token);
            if (key.Length == 0 || !seen.Add(key)) continue;
            bool inText = used.Contains(key);
            Rows.Add(new Row
            {
                Key = key,
                Desc = desc ?? "",
                CanChange = inText,
                // 初值从正文反推：已经是 {X:diff()} 的就预先勾上（绝不「没勾就把用户写好的高亮去掉」）
                IsChecked = highlighted.Contains(key),
                Status = highlighted.Contains(key)
                    ? "这段文字里已经是 " + HighlightDiffText.DiffToken(key) + "（升级后按差异变绿）"
                    : inText
                        ? "这段文字里现在是 " + HighlightDiffText.PlainToken(key) + "（普通颜色）；勾上 = 换成高亮写法"
                        : "这段文字里还没用到 " + HighlightDiffText.PlainToken(key) + " —— 勾它**不会**替你加字，要加请用「填入参数…」",
            });
        }
        FieldList.ItemsSource = Rows;
        NoPlaceholderHint.Visibility = HighlightDiffText.Scan(_text).Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Rows.Count == 0)
            SummaryText.Foreground = System.Windows.Media.Brushes.DarkOrange;
        RefreshSummary();
    }

    /// <summary>底部那行实时汇总（勾选一变就重算）。</summary>
    private void RefreshSummary()
    {
        var (_, changed, _) = HighlightDiffText.Apply(_text, CheckedKeys);
        if (Rows.Count == 0)
        {
            SummaryText.Text = "这张牌没有能高亮的数值字段：先给它加一条带数值的效果（伤害 / 格挡 / 抽牌 / 状态层数…），"
                + "再用「填入参数…」把数字换成占位符。";
            return;
        }
        if (changed == 0)
        {
            SummaryText.Text = HighlightDiffText.Scan(_text).Count == 0
                ? "这段文字里一个 {} 占位符都没有 → 点「确定」不会变动任何字符（高亮开关对纯数字文案无效）。"
                : "按现在的勾选，点「确定」不会改动任何字符（文本里已经是这个写法）。";
            return;
        }
        SummaryText.Text = $"点「确定」会改写 {changed} 处占位符（只动 {{…}} 本身，别的一个字都不改）。";
    }

    /// <summary>勾选框一变就刷新底部汇总（DataTemplate 里的 CheckBox 走路由事件冒泡上来）。</summary>
    private void OnRowChecked(object sender, RoutedEventArgs e) => RefreshSummary();

    /// <summary>点「确定」：把正文里已有的占位符按勾选改写成 {X:diff()} / {X}。</summary>
    private void Commit()
    {
        var (text, changed, perKey) = HighlightDiffText.Apply(_text, CheckedKeys);
        ResultText = text;
        ChangedCount = changed;
        PerKey = perKey;
        Confirmed = true;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Commit();
        DialogResult = true;
    }

    /// <summary>「全部不勾（保持原样）」：把所有 <c>{X:diff()}</c> 还原成 <c>{X}</c>（正文里没有的字段本来就没勾）。</summary>
    private void OnClearAll(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows) row.IsChecked = false;
        RefreshSummary();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>
    /// 自检用：等价于点「确定」的**改写动作**（<c>DialogResult</c> 只有在真 ShowDialog 之后才能设，
    /// 所以这里不复用它 —— 自检要的是「同一套改写逻辑」，不是窗口关闭）。
    /// </summary>
    public void ApplyForTest() => Commit();

    /// <summary>自检用：把某个键勾 / 不勾。</summary>
    public bool SetCheckedForTest(string key, bool value)
    {
        var row = Rows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));
        if (row is null) return false;
        row.IsChecked = value;
        return true;
    }
}
