using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Sts2CharForge.App;

/// <summary>
/// 可搜索下拉框（**完全自绘**：一个输入框 + 一列自己生成的建议行）。
///
/// 为什么不用 WPF 的 ComboBox / ListBox：
///   · IsEditable 的 ComboBox：只要它有选中项，就会把用户打的字拽回成选中项（"打不进字 / 删不掉"）。
///   · ListBox + CollectionView：会跟着视图的"当前项"自动选中第一条，一过滤就选中第一个
///     （"只会保留第一条 / 改不掉"）。
/// 这里干脆两个都不用了：建议列表就是我自己塞进 StackPanel 的一行行 Border，
/// 高亮、点选、文字全部由本控件说了算，**值只在用户明确点选/回车时才改变**。
/// </summary>
public sealed class SearchComboBox : Grid
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SearchComboBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(nameof(SelectedValue), typeof(object), typeof(SearchComboBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedValueChanged));

    public static readonly DependencyProperty SelectedValuePathProperty =
        DependencyProperty.Register(nameof(SelectedValuePath), typeof(string), typeof(SearchComboBox),
            new PropertyMetadata(null, OnSelectedValuePathChanged));

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(SearchComboBox),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SearchPathProperty =
        DependencyProperty.Register(nameof(SearchPath), typeof(string), typeof(SearchComboBox),
            new PropertyMetadata("Display"));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public string SelectedValuePath
    {
        get => (string)GetValue(SelectedValuePathProperty);
        set => SetValue(SelectedValuePathProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public string SearchPath
    {
        get => (string)GetValue(SearchPathProperty);
        set => SetValue(SearchPathProperty, value);
    }

    private readonly TextBox _box = new();
    private readonly Popup _popup = new();
    private readonly StackPanel _panel = new();
    private readonly ScrollViewer _scroll = new();

    private readonly List<object> _all = new();
    private readonly List<object> _shown = new();
    private readonly List<Border> _rows = new();
    private int _highlight = -1;
    private bool _writingText;   // 正在由本控件写 Text（不要当成用户输入）
    private bool _opening;       // 正在打开弹窗（此时焦点变化是我们自己造成的）
    private string _pickedText = "";   // 上一次 Pick 写进输入框的文字

    private static readonly Dictionary<string, PropertyInfo?> PropCache = new();

    public string CurrentFilter { get; private set; } = "";
    public int FilteredCount => _shown.Count;
    public List<object> FilteredItems => _shown.ToList();
    public IReadOnlyList<object> Suggestions => _shown;
    public IReadOnlyList<string> SuggestionTexts => _shown.Select(TextOf).ToList();
    public int HighlightedIndex => _highlight;
    public bool IsPopupOpen => _popup.IsOpen;
    public TextBox InputBox => _box;

    /// <summary>弹窗里的内容是否可聚焦（自检用；必须为 false，否则会抢走输入框焦点）。</summary>
    public bool PopupContentFocusable => _scroll.Focusable || _panel.Focusable;

    /// <summary>当前值对应的项（找不到就是 null）。</summary>
    public object? CurrentItem => FindByValue(SelectedValue);

    public SearchComboBox()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var field = Application.Current?.TryFindResource("Field") as Style;
        if (field is not null) _box.Style = field;
        _box.TextChanged += (_, _) => { if (!_writingText) ApplySearch(_box.Text); };
        // 焦点进来时只在"用户点/切进来"时展开全部；弹窗打开后我们把焦点抢回输入框时不能再来一次，
        // 否则会把刚打进去的过滤词清掉（自检抓到过：打字后列表还是全部 244 项）
        _box.GotKeyboardFocus += (_, _) => { if (!_opening) OpenAll(); };
        _box.PreviewMouseLeftButtonDown += (_, _) => OpenAll();
        _box.KeyDown += OnBoxKeyDown;
        _box.LostKeyboardFocus += OnBoxLostFocus;
        Children.Add(_box);

        _scroll.Content = _panel;
        _scroll.MaxHeight = 280;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        // 关键：弹窗里的东西**都不能抢焦点**。ScrollViewer/StackPanel 默认可聚焦，
        // 一开弹窗焦点就被它们拿走 → 输入框打不进字；紧接着输入框失焦又会把弹窗关掉 → 看着像"拉不下来"。
        _scroll.Focusable = false;
        _scroll.IsTabStop = false;
        _panel.Focusable = false;
        _panel.IsHitTestVisible = true;
        _panel.Background = Brushes.White;
        _panel.Margin = new Thickness(0);

        _popup.Child = _scroll;
        _popup.StaysOpen = false;
        _popup.AllowsTransparency = true;
        _popup.Focusable = false;
        _popup.PlacementTarget = _box;
        _popup.Placement = PlacementMode.Bottom;
        _popup.Opened += (_, _) =>
        {
            if (_box.ActualWidth > 1) _popup.Width = _box.ActualWidth;
            _opening = true;
            try { _box.Focus(); }        // 焦点始终留在输入框
            finally { _opening = false; }
        };
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (SearchComboBox)d;
        // 关键：数据源是**稳定集合**（AllCards / AllPowers 都是同一个 ObservableCollection 实例，
        // 加卡 / 改卡名时按索引就地替换，避免整表重置把已选中的值弹掉）。
        // 以前这里只在 ItemsSource **换实例**时灌一次 _all，于是「保存的卡」永远进不了候选列表 ——
        // 目标卡（生成 / 变化用）里搜不到自己的卡就是这么来的。
        // 所以这里订阅集合变更：Add / Remove / Replace / Reset 都要重新灌，并保持当前过滤词。
        if (c._observed is not null)
        {
            c._observed.CollectionChanged -= c.OnSourceCollectionChanged;
            c._observed = null;
        }
        if (e.NewValue is INotifyCollectionChanged ncc)
        {
            c._observed = ncc;
            ncc.CollectionChanged += c.OnSourceCollectionChanged;
        }
        c.ReloadAll();
        c.RebuildSuggestions(c._box?.Text?.Trim() ?? "");
        c.SyncTextFromValue();
    }

    private INotifyCollectionChanged? _observed;

    /// <summary>数据源加了卡 / 改了卡名（按索引替换）时，把候选表重新灌一遍。</summary>
    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ReloadAll();
        // 弹窗开着就按当前输入重新过滤；没开就只更新列表，别自己弹出来
        string filter = (_box?.Text ?? "").Trim();
        RebuildSuggestions(filter);
        if (_popup.IsOpen) ShowPopup();
    }

    /// <summary>把 ItemsSource 里的项抄进 _all（候选表）。</summary>
    private void ReloadAll()
    {
        _all.Clear();
        if (ItemsSource is IEnumerable src)
            foreach (object? o in src)
                if (o is not null) _all.Add(o);
    }

    private static void OnSelectedValuePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SearchComboBox)d).SyncTextFromValue();

    private static void OnSelectedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SearchComboBox)d).SyncTextFromValue();

    // ---------------- 过滤与展开 ----------------

    /// <summary>按关键词过滤建议（包含匹配、忽略大小写）。空关键词 = 全部。</summary>
    public void ApplySearch(string text)
    {
        RebuildSuggestions((text ?? "").Trim());
        if (CurrentFilter.Length > 0)
            Dispatcher.BeginInvoke(new Action(ShowPopup), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>展开下拉并显示全部（点/聚焦输入框时用）。</summary>
    public void OpenAll()
    {
        RebuildSuggestions("");
        ShowPopup();
    }

    private void ShowPopup()
    {
        if (_shown.Count == 0) { _popup.IsOpen = false; return; }
        if (_box.ActualWidth > 1) _popup.Width = _box.ActualWidth;
        _popup.IsOpen = true;
    }

    /// <summary>焦点是不是落在弹窗里面。</summary>
    private bool IsInsidePopup(DependencyObject node)
    {
        for (DependencyObject? cur = node; cur is not null; cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur))
        {
            if (ReferenceEquals(cur, _popup) || ReferenceEquals(cur, _scroll) || ReferenceEquals(cur, _panel)) return true;
            if (ReferenceEquals(cur, this)) return false;
        }
        return false;
    }

    private void RebuildSuggestions(string filter)
    {
        CurrentFilter = filter;
        _shown.Clear();
        foreach (var item in _all)
            if (filter.Length == 0 || TextOf(item).Contains(filter, StringComparison.OrdinalIgnoreCase))
                _shown.Add(item);

        _panel.Children.Clear();
        _rows.Clear();
        _highlight = _shown.Count > 0 ? 0 : -1;
        for (int i = 0; i < _shown.Count; i++) _panel.Children.Add(CreateRow(_shown[i], i));
        UpdateHighlightVisual();
    }

    private Border CreateRow(object item, int index)
    {
        UIElement content = ItemTemplate is not null
            ? new ContentPresenter { Content = item, ContentTemplate = ItemTemplate }
            : new TextBlock { Text = TextOf(item), TextTrimming = TextTrimming.CharacterEllipsis };

        var border = new Border
        {
            Child = content,
            Padding = new Thickness(6, 4, 6, 4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        border.MouseLeftButtonDown += (_, e) => { e.Handled = true; Pick(item); };
        border.MouseEnter += (_, _) => { _highlight = index; UpdateHighlightVisual(); };
        _rows.Add(border);
        return border;
    }

    private void UpdateHighlightVisual()
    {
        var hl = new SolidColorBrush(Color.FromRgb(0xCF, 0xE4, 0xFF));
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].Background = i == _highlight ? hl : Brushes.Transparent;
    }

    // ---------------- 选择 ----------------

    /// <summary>选中某一项：写回绑定值 + 把名字补全到输入框 + 收起下拉。</summary>
    public void Pick(object? item)
    {
        if (item is null) return;
        _popup.IsOpen = false;
        SetText(TextOf(item));
        _pickedText = TextOf(item);
        SelectedValue = ValueOf(item);        // 只有这里会改值
        RebuildSuggestions("");
    }

    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (_shown.Count == 0) return;
                _highlight = (_highlight + 1) % _shown.Count;
                UpdateHighlightVisual();
                e.Handled = true;
                break;
            case Key.Up:
                if (_shown.Count == 0) return;
                _highlight = (_highlight - 1 + _shown.Count) % _shown.Count;
                UpdateHighlightVisual();
                e.Handled = true;
                break;
            case Key.Enter:
                if (_shown.Count == 0) return;
                Pick(_shown[_highlight >= 0 && _highlight < _shown.Count ? _highlight : 0]);
                e.Handled = true;
                break;
            case Key.Escape:
                _popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void OnBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        bool intoPopup = e.NewFocus is DependencyObject nd && IsInsidePopup(nd);
        HandleLostFocus(intoPopup);
    }

    /// <summary>
    /// 失焦时的处理（真实失焦需要窗口在前台，自检里拿不到焦点，所以把实现单独拿出来，事件和自检走同一份代码）。
    /// 这里**绝不能**把用户打的字换掉：以前失焦时会"还原成当前值对应的名字"，
    /// 结果弹窗关闭 / 焦点抖动时把正在输入的内容顶掉，表现就是用户报的「打不进字 / 删不掉 / 只剩第一条」。
    /// 现在只在"用户把整条名字打全了"这种情况下才顺手选中它（等于打完名字按 tab）。
    /// </summary>
    internal void HandleLostFocus(bool focusMovedIntoPopup)
    {
        if (_opening) return;
        if (focusMovedIntoPopup) return;      // 焦点进了弹窗 → 别收
        _popup.IsOpen = false;
        string typed = _box.Text.Trim();
        if (typed.Length == 0) return;
        if (string.Equals(typed, _pickedText, StringComparison.Ordinal)) return;   // 还是上次选中的文字，不用再选

        var exact = _all.FirstOrDefault(x => string.Equals(TextOf(x).Trim(), typed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) Pick(exact);
    }

    /// <summary>把当前值对应的名字显示到输入框；值被清空时把输入框也清空。</summary>
    private void SyncTextFromValue()
    {
        var item = FindByValue(SelectedValue);
        if (item is not null) { SetText(TextOf(item)); _pickedText = TextOf(item); return; }

        // 值被清成 null（新建效果就是这种情况）：输入框跟着空。
        // 留着上一次的名字会出两种怪事：看着像"值还在"，而且失焦时会被当成"用户打全了名字"自动选回去。
        if (SelectedValue is null && _box.Text.Length > 0) { SetText(""); _pickedText = ""; RebuildSuggestions(""); }
    }

    private void SetText(string text)
    {
        _writingText = true;
        try { _box.Text = text; }
        finally { _writingText = false; }
    }

    private object? FindByValue(object? value)
    {
        if (value is null) return null;
        string path = SelectedValuePath;
        foreach (var item in _all)
        {
            object? v = string.IsNullOrWhiteSpace(path) ? item : ReadProp(item, path);
            if (Equals(v, value)) return item;
            if (v is string s && value is string sv && string.Equals(s, sv, StringComparison.OrdinalIgnoreCase)) return item;
        }
        return null;
    }

    private object? ValueOf(object item)
    {
        string path = SelectedValuePath;
        return string.IsNullOrWhiteSpace(path) ? item : ReadProp(item, path);
    }

    private string TextOf(object? item)
    {
        if (item is null) return "";
        string path = SearchPath;
        if (string.IsNullOrWhiteSpace(path)) return item.ToString() ?? "";
        return ReadProp(item, path) as string ?? item.ToString() ?? "";
    }

    private static object? ReadProp(object item, string name)
    {
        var type = item.GetType();
        string key = type.FullName + "|" + name;
        if (!PropCache.TryGetValue(key, out var pi))
        {
            pi = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            PropCache[key] = pi;
        }
        return pi?.GetValue(item);
    }
}
