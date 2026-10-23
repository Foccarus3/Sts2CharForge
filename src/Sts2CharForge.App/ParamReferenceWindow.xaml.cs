using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Generation;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.App;

/// <summary>
/// 参数参考窗（V0.2.182）。
///
/// <para><b>为什么有它</b>：用户原话「参数占的面积太大了，能不能全删了，不占用生成器内的位置，
/// 然后每次打开生成器时，打开一个参数参考小窗口，并且对里面的参数进行分类」。
/// 以前「可用量」清单是**内嵌**在效果编辑器里的（卡牌 / 遗物药水状态 / 节点图三处各摆一份，
/// 80 条内置只读量铺成一大片）——现在整块删掉，清单只在这里出现，按类分好、写明出处。</para>
///
/// <para><b>数据只有一份</b>：内置只读量直接用 <see cref="FormulaBuiltins.All"/>（生成端认的就是它），
/// 参数取值来源用 <see cref="ParamSources.All"/> —— 界面列出来的名字和生成时认的名字不会漂。</para>
/// </summary>
public partial class ParamReferenceWindow : Window
{
    /// <summary>一类参数（左边那一栏）。</summary>
    public sealed class Category
    {
        public string Title { get; init; } = "";
        public string Hint { get; init; } = "";
        public IReadOnlyList<Entry> Items { get; init; } = new List<Entry>();
        public string CountText => $"{Items.Count} 条";
        public override string ToString() => Title;
    }

    /// <summary>一条参数（右边那两列）。</summary>
    public sealed class Entry
    {
        public string Id { get; init; } = "";
        public string Display { get; init; } = "";
        public override string ToString() => Id;
    }

    private readonly List<Category> _categories = new();

    /// <summary>自检用：现在列了几类（判断「窗真的把内容绑上了」而不是空窗）。</summary>
    internal int CategoryCountForCheck => _categories.Count;

    public ParamReferenceWindow(CharacterProfile? profile = null)
    {
        InitializeComponent();
        // V0.2.240（用户反馈「屏幕小的时候有些地方被遮挡」）：这个窗也先塞进屏幕可用区域。
        // 它是独立窗口、不跟着主窗的「界面缩放」走 —— 屏比 980×660 还小时会被窗口边裁掉。
        try
        {
            Rect work = SystemParameters.WorkArea;
            if (Width > work.Width - 16) Width = Math.Max(420, work.Width - 16);
            if (Height > work.Height - 16) Height = Math.Max(320, work.Height - 16);
        }
        catch { /* 适配失败也不能挡住弹窗 */ }
        _categories = BuildCategories(profile);
        CategoryList.ItemsSource = _categories;
        if (_categories.Count > 0) CategoryList.SelectedIndex = 0;
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is not Category c) return;
        CategoryTitle.Text = c.Title;
        CategoryHint.Text = c.Hint;
        EntryList.ItemsSource = c.Items;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// 组装分类清单（自检直接调它 —— 不用真弹窗）。
    /// 分类口径：卡牌专用读数 / 内置实时读数 / X-Y / 当前存档的自定义参数 / 参数取值来源 / 公式语法。
    /// </summary>
    public static List<Category> BuildCategories(CharacterProfile? profile)
    {
        var cats = new List<Category>();
        // ① 卡牌专用读数（V0.2.242）：ThisCardDamage / ThisCardBlock / 四条「这张牌这次打出的张数」。
        //
        // 用户报「怎么没看到 ThisCardBlock / ThisCardExhaust …」：这几条以前混在「内置实时读数」那张
        // 80+ 行的大表**最下面**，参考窗一开只露出开头十行，得一直滚到底才看得见 —— 等于没有。
        // 现在把 CardOnly 那几条拆成**第一类**（窗口一开就选中它），并且从「内置实时读数」里剔除，
        // 保证「每一条在参考窗里只出现一次」（分类不重不漏）。
        var cardOnly = FormulaBuiltins.All.Where(b => b.CardOnly).ToList();
        if (cardOnly.Count > 0)
            cats.Add(new Category
            {
                Title = "这张牌这一次打出（只能写在卡牌上）",
                Hint = "这几条读的都是「本张牌这一次打出」过程中的数（伤害 / 格挡 / 消耗·丢弃·变化·生成的张数），"
                     + "**只能用在卡牌上**（遗物 / 药水 / 自定义状态没有「这张牌」，选了会报错）。"
                     + "表达式里现算，打出一次各算一次。",
                Items = cardOnly.Select(b => new Entry { Id = b.Id, Display = b.Display }).ToList(),
            });

        // ② 内置只读量：数据源就是生成端那张表（FormulaBuiltins.All），卡牌专用那几条已经在上面单列
        cats.Add(new Category
        {
            Title = "内置实时读数（不用定义，直接写）",
            Hint = "这些名字直接写进公式，每次取值现算。它们**不需要**在「自定义参数 / 组」页定义。",
            Items = FormulaBuiltins.All.Where(b => !b.CardOnly)
                .Select(b => new Entry { Id = b.Id, Display = b.Display }).ToList(),
        });

        // ② 特殊：X / Y
        cats.Add(new Category
        {
            Title = "特殊变量 X / Y",
            Hint = "这两个不用定义，本工具一直认。注意它们读的是「打出这张牌那一刻、扣完费之后」的值。",
            Items = new List<Entry>
            {
                new() { Id = "X", Display = "打出这张牌那一刻的**当前能量**（X 费牌就是那个 X 的数量）" },
                new() { Id = "Y", Display = "当前**额外资源量**（Y 费牌就是那个 Y 的数量）" },
                new() { Id = "XSpent", Display = "这张牌**花掉**的能量（不是 X 费牌 = 0）" },
                new() { Id = "YSpent", Display = "这张牌**花掉**的额外资源量（不是 Y 费牌 = 0）" },
            },
        });

        // ③ 自定义参数（用户自己在「自定义参数 / 组」页建的字母）—— 每个存档不一样，所以按当前存档列
        var paramItems = new List<Entry>();
        if (profile?.CustomParams is not null)
        {
            foreach (var p in profile.CustomParams)
            {
                if (p is null || string.IsNullOrWhiteSpace(p.Name)) continue;
                string src = ParamSources.Zh(p.Source);
                string extra = ParamSourceDetail(p);
                paramItems.Add(new Entry { Id = p.Name, Display = extra.Length == 0 ? src : $"{src}（{extra}）" });
            }
        }
        if (paramItems.Count == 0)
            paramItems.Add(new Entry { Id = "（当前存档没有自定义参数）", Display = "要加的话到「自定义参数 / 组」页新建一个：起个字母名（M / N…）、再选它从哪取值。" });
        cats.Add(new Category
        {
            Title = "自定义参数（你这个存档定义的）",
            Hint = "在「自定义参数 / 组」页里定义：写一个字母名（M / N…）+ 选它从哪取值。定义好之后在公式里直接写这个名字。",
            Items = paramItems,
        });

        // ④ 参数能取的「来源」（新建参数时的下拉候选）
        cats.Add(new Category
        {
            Title = "参数取值来源（新建参数时选）",
            Hint = "「自定义参数」那个下拉里的全部候选 —— 固定数值 / 当前血量 / 状态层数 / 各类实时读数。",
            Items = ParamSources.All.Select(s => new Entry { Id = s.Id, Display = $"{s.Display} —— {s.Hint}" }).ToList(),
        });

        // ⑤ 公式语法
        cats.Add(new Category
        {
            Title = "公式能写什么（语法）",
            Hint = "所有「数值 / 公式」栏位共用同一套写法。",
            Items = new List<Entry>
            {
                new() { Id = "运算符", Display = "+  -  *  /  %  和括号 ( )" },
                new() { Id = "函数", Display = "min(a, b) · max(a, b) · floor(x) · ceil(x) · round(x) · abs(x) · clamp(x, 最小, 最大)" },
                new() { Id = "内置量", Display = "上面「内置实时读数」那一类的全部名字（Turn / HandCount / DeckStatusCount / PlayedThisTurn…）" },
                new() { Id = "参数名", Display = "自己在「自定义参数 / 组」页定义的字母参数（M / N…）" },
                new() { Id = "X / Y", Display = "当前能量 / 当前额外资源量（不用定义）" },
                new() { Id = "⚠ 纯数字那一档", Display = "抽牌 / 生成卡牌 / 消耗这类「数值必须在生成时就定下来」的效果：数值栏只认纯数字（例 1、2+3）或光秃秃一个 X；公式请写进「数值（公式）」那一栏" },
            },
        });
        return cats;
    }

    private void Build(CharacterProfile? profile) => _categories.AddRange(BuildCategories(profile));

    /// <summary>参数来源的补充说明（状态层数要写是哪个状态、固定数值要写填几）。</summary>
    private static string ParamSourceDetail(ParamSpec p)
    {
        if (string.Equals(ParamSources.Normalize(p.Source), ParamSources.Value, System.StringComparison.Ordinal))
            return p.Value.ToString("0.####");
        if (string.Equals(ParamSources.Normalize(p.Source), ParamSources.Power, System.StringComparison.Ordinal))
            return string.IsNullOrWhiteSpace(p.PowerId) ? "还没选状态" : EffectCatalog.PowerName(p.PowerId, p.PowerId);
        return ParamTargets.Zh(p.Target);
    }
}
