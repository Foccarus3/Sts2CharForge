using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.App;

/// <summary>
/// 锻造页最右侧那块「布」：一块可以拖动模块、把模块用线连起来的画布（类 Blender 的节点编辑器）。
///
/// 为什么整个用代码画、不写成 XAML：模块的种类是**数据驱动**的（见 <see cref="ForgeGraphCatalog"/>），
/// 每加一种模块只改目录就行，不用再写一套 XAML；连线的位置也要按模块的实际高度算，
/// XAML 的 DataTemplate 里拿不到「第 n 个端口在哪个像素」。
///
/// 交互（第一阶段）：
///   · 拖动：按住模块标题栏拖（【能量】模块按需求固定在左上角，不给拖）；
///   · 连线：先点右边一个出口（圆点）→ 再点左边一个入口，就接上了；点空白处取消；
///   · 一个入口只接一条线（再连会替换掉旧的）；不能自己连自己；
///   · 连错了（接了不该接的端口）线画成红色虚线，并且下面给一句原因。
/// </summary>
public sealed class ForgeCanvasView : Grid
{
    // 画布坐标里的尺寸常量（模块宽度 / 标题栏高度 / 端口一行多高）
    private const double NodeWidth = ForgeGraphCatalog.NodeWidth;
    private const double HeaderHeight = ForgeGraphCatalog.HeaderHeight;
    private const double PortRowHeight = ForgeGraphCatalog.PortRowHeight;
    private const double TopPad = 8;
    private const double MinCanvasW = 1400;
    private const double MinCanvasH = 900;

    private readonly Canvas _root = new();
    private readonly Canvas _wireLayer = new();
    private readonly StackPanel _kindBar = new();
    private readonly TextBlock _statusText = new();
    private readonly ScrollViewer _scroll = new();
    private readonly ComboBox _kindPick = new();
    private readonly TextBlock _zoomText = new();

    private readonly Dictionary<string, FrameworkElement> _nodeViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Point> _portPos = new(StringComparer.Ordinal);

    private ForgeGraphSpec? _graph;
    private string _scope = "Card";

    /// <summary>选中的模块（可以多选：框选 / Shift 点选；拖动时一起移动）。</summary>
    private readonly List<ForgeNodeSpec> _selectedNodes = new();

    /// <summary>
    /// 【能量】是**固定模块**（左上角那一块，送上「能量 / 额外资源量」两个数）：
    /// 不参与选中 / 框选 / 全选，也**删不掉**（它没有参数化的必要，删掉只会让 X / Y 用不了）。
    /// 用户要求：「让【能量】无法被选中和删除」。
    /// </summary>
    private static bool IsLocked(ForgeNodeSpec? n) =>
        n is not null && string.Equals(n.Kind, "Energy", StringComparison.Ordinal);

    /// <summary>点到 / 想删【能量】时给的那句话（拦住并说清，不静默）。</summary>
    private const string EnergyLockedNote =
        "【能量】是固定模块（左上角那一块）：它只负责把「当前能量 / 当前额外资源量」送出去给别的模块用，"
        + "所以不能选中、也不能删除 —— 要改就在它自己身上那两栏里改（打出时消耗多少能量 / 额外资源量）。";

    /// <summary>框选用的那一层（虚线框 + 已经画好的选中高亮）。</summary>
    private readonly Canvas _overlay = new();

    /// <summary>框选矩形（拖的时候才创建）。</summary>
    private Rectangle? _bandRect;
    private Point _bandStart;
    private bool _banding;

    /// <summary>缩放（Ctrl + 滚轮改它）。挂在画布的 LayoutTransform 上，连线 / 端口坐标不用另算。</summary>
    private readonly ScaleTransform _zoom = new(1, 1);
    private const double ZoomMin = 0.4;
    private const double ZoomMax = 3.0;

    /// <summary>正在拉的那条线（从哪个模块的第几个出口出发）。</summary>
    private (string NodeId, int Port)? _pending;
    private Line? _pendingLine;

    /// <summary>改变之后回调（界面用来标记"有改动"、刷新问题列表）。</summary>
    public event Action? Changed;

    /// <summary>
    /// 【状态】模块 / 【效果】模块里那个「增益 / 减益」下拉的数据源。
    ///
    /// 为什么不直接用 <see cref="Core.Effects.EffectCatalog.Powers"/>：那只是**本体**扫描出来的
    /// 静态清单，玩家自己配的「自定义状态」不在里面（用户实测报过两次：卡牌页有了、锻造页还是没有）。
    /// 窗口把它那个「包含自定义状态、还会随登记实时刷新」的集合绑过来（见 MainWindow.xaml 里
    /// <c>PowerSource="{Binding PowerChoices, ...}"</c>），这里只负责挑出合法的、按名字排好序。
    /// </summary>
    public static readonly DependencyProperty PowerSourceProperty = DependencyProperty.Register(
        nameof(PowerSource), typeof(IEnumerable), typeof(ForgeCanvasView),
        new PropertyMetadata(null, OnPowerSourceChanged));

    public IEnumerable? PowerSource
    {
        get => (IEnumerable?)GetValue(PowerSourceProperty);
        set => SetValue(PowerSourceProperty, value);
    }

    private static void OnPowerSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ForgeCanvasView view) view.RefreshPowerItems();
    }

    /// <summary>
    /// 数据源「看起来变了」就自己重算一次（幂等）。窗口在**每次往画布指图之前**都会调它：
    /// 下拉是在重建布的时候现搭的，只要有一次「数据源换了但没重画」，用户看到的就是旧清单 ——
    /// 这正是「锻造页状态下拉里没有自定义状态」反复出现的原因，所以这里不再指望调用方记得刷新。
    /// </summary>
    public void RefreshPowerItemsIfStale()
    {
        int want = 0;
        if (PowerSource is not null)
            foreach (object? item in PowerSource)
                if (item is PowerEntry q && q.Id.Length > 0 && !q.Id.StartsWith("-", StringComparison.Ordinal)) want++;
        if (want != _powers.Count) RefreshPowerItems();
    }

    /// <summary>
    /// 当前状态下拉里真正要显示的条目。
    ///
    /// 两条规矩（都是用户实测提的「找不到自定义状态」逼出来的）：
    ///   ① **自定义状态排在最前面**，并且显示名前面加一个 `⭐` —— 本体状态 200+ 条，
    ///      自定义状态排在最后的话，用户翻不到就以为「没有」（截图里就是这样）；
    ///   ② 负数 Id 的那些伪条目（「给予能量」之类）剔掉，它们不是状态。
    /// 两组内部都按显示名排序。
    /// </summary>
    private readonly List<PowerEntry> _powers = new();

    /// <summary>
    /// 按 <see cref="PowerSource"/> 重算下拉条目；数据源换了就重画一次布（下拉是画布重建时现搭的，
    /// 数据源后到的话不重画就还是空列表）。
    /// </summary>
    public void RefreshPowerItems()
    {
        _powers.Clear();
        if (PowerSource is not null)
        {
            foreach (object? item in PowerSource)
            {
                if (item is not PowerEntry p || p.Id.Length == 0 || p.Id.StartsWith("-", StringComparison.Ordinal)) continue;
                // 自定义状态加个 ⭐ 前缀：本体状态一两百条，光靠排序还是会看漏
                _powers.Add(MineRank(p) == 1 ? p with { Zh = "⭐" + p.Zh } : p);
            }
            _powers.Sort((a, b) =>
            {
                int mine = MineRank(b).CompareTo(MineRank(a));   // 自己的排前面
                return mine != 0 ? mine : string.Compare(a.Display, b.Display, StringComparison.CurrentCulture);
            });
        }
        // 构造期这个数据源就会绑上来（那时布还没搭好），所以只在真的画过布之后才重建
        if (_graph is not null || _nodeViews.Count > 0) Rebuild();
    }

    /// <summary>是不是「这个存档自己的自定义状态」（窗口登记过的那些）。</summary>
    private static int MineRank(PowerEntry p) => Core.Effects.EffectCatalog.IsCustomPower(p.Id) ? 1 : 0;

    /// <summary>
    /// 自检用：锻造页【状态】/【效果】模块里那个「增益 / 减益」下拉当前有多少条、都叫什么。
    ///
    /// 存在的意义：用户报过两次「锻造页的状态菜单里没有自定义状态」——
    /// 这块布的下拉是**重建布时现搭**的，数据源换过、搭的时候又用的旧清单，光看界面是看不出来的，
    /// 只能把「下拉里到底有什么」暴露给自检去断言。
    /// </summary>
    public IReadOnlyList<string> PowerItemsForCheck => _powers.Select(p => p.Id).ToList();

    /// <summary>自检用：下拉里排在前面的那几条（用户第一眼看到的是它们 —— 自定义状态必须在这里）。</summary>
    public IReadOnlyList<string> PowerItemsTopForCheck => _powers.Take(8).Select(p => p.Id).ToList();

    public ForgeCanvasView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _root.Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
        _root.Width = MinCanvasW;
        _root.Height = MinCanvasH;
        // 布上的字很小（10~12px），默认的「理想排版」会把小字糊成一团 —— 换成按像素对齐 + ClearType，
        // 模块里的说明文字清楚很多（需求：模块的字清晰度能不能高一点）。
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(_root, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(_root, TextHintingMode.Fixed);
        RenderOptions.SetClearTypeHint(_root, ClearTypeHint.Enabled);
        // 缩放：Ctrl + 滚轮（挂在 ScrollViewer 上，见下面）
        _root.LayoutTransform = _zoom;
        _root.Children.Add(_overlay);
        _root.Children.Add(_wireLayer);
        // 连线层要能吃到右键（删线）；它上面的模块会自己把事件吃掉，不冲突
        _wireLayer.Background = Brushes.Transparent;
        _root.MouseLeftButtonDown += OnCanvasMouseDown;
        _root.MouseMove += OnCanvasMouseMove;
        _root.MouseLeftButtonUp += OnCanvasMouseUp;
        // 右键点连线 = 删掉那条线（连线自己带 Tag=那条 ForgeLinkSpec）
        _root.MouseRightButtonDown += (_, e) =>
        {
            var link = LinkHit(e.OriginalSource as DependencyObject);
            if (link is null) return;
            e.Handled = true;
            RemoveLink(link);
        };
        // Ctrl+A = 全选，Delete = 删掉选中的那一批（键盘焦点在画布上时）
        _root.Focusable = true;
        _root.KeyDown += (_, ke) =>
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ke.Key == System.Windows.Input.Key.A && ctrl) { SelectAll(); ke.Handled = true; }
            else if (ke.Key == System.Windows.Input.Key.Delete) { RemoveSelected(); ke.Handled = true; }
            else if (ctrl && (ke.Key == System.Windows.Input.Key.D0 || ke.Key == System.Windows.Input.Key.NumPad0))
            { ResetZoom(); ke.Handled = true; }                     // Ctrl+0 = 回 100%
            else if (ctrl && (ke.Key == System.Windows.Input.Key.OemPlus || ke.Key == System.Windows.Input.Key.Add))
            { ApplyZoom(_zoom.ScaleX * 1.1, null); ke.Handled = true; }    // Ctrl + = 放大
            else if (ctrl && (ke.Key == System.Windows.Input.Key.OemMinus || ke.Key == System.Windows.Input.Key.Subtract))
            { ApplyZoom(_zoom.ScaleX / 1.1, null); ke.Handled = true; }    // Ctrl - = 缩小
        };

        _scroll.Content = _root;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
        // 滚轮 = 上下滚画布；**Ctrl + 滚轮 = 缩放**（需求：ctrl+滚轮能不能放大缩小布）
        _scroll.PreviewMouseWheel += OnCanvasWheel;

        SetRow(_kindBar, 0);
        Children.Add(_kindBar);
        SetRow(_scroll, 1);
        Children.Add(_scroll);
        SetRow(_statusText, 2);
        Children.Add(_statusText);

        _statusText.Margin = new Thickness(6, 3, 6, 3);
        _statusText.TextWrapping = TextWrapping.Wrap;
        _statusText.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        _statusText.FontSize = 11;

        BuildKindBar();
    }

    /// <summary>当前正在编辑的那张图（null = 没选中东西，整块布空着）。</summary>
    public ForgeGraphSpec? Graph
    {
        get => _graph;
        set
        {
            if (ReferenceEquals(_graph, value)) return;
            _graph = value;
            Rebuild();
        }
    }

    /// <summary>这一块布属于哪一类物品（Card / Potion / Relic）——【触发时机】的候选按它筛。</summary>
    public string Scope
    {
        get => _scope;
        set { if (!string.Equals(_scope, value, StringComparison.Ordinal)) { _scope = value; Rebuild(); } }
    }

    // ==================================================================================
    //  顶部那条「加模块」工具条
    // ==================================================================================

    private void BuildKindBar()
    {
        _kindBar.Orientation = Orientation.Horizontal;
        _kindBar.Margin = new Thickness(6, 4, 6, 2);

        var tip = new TextBlock
        {
            Text = "加模块：",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            FontWeight = FontWeights.Bold,
        };
        _kindBar.Children.Add(tip);

        _kindPick.Width = 190;
        _kindPick.Margin = new Thickness(0, 0, 6, 0);
        _kindBar.Children.Add(_kindPick);

        var add = new Button { Content = "添加", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
        add.Click += (_, _) => AddSelectedKind();
        _kindBar.Children.Add(add);

        var del = new Button { Content = "删除选中", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 12, 0) };
        del.Click += (_, _) => RemoveSelected();
        _kindBar.Children.Add(del);

        var fit = new Button { Content = "全部可见", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
        fit.Click += (_, _) => FitToContent();
        _kindBar.Children.Add(fit);

        var all = new Button { Content = "全选", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 12, 0) };
        all.Click += (_, _) => SelectAll();
        _kindBar.Children.Add(all);

        // 缩放（需求：Ctrl + 滚轮缩放）—— 这里也给按钮，并且把当前倍率写出来
        var zoomOut = new Button { Content = "－", Padding = new Thickness(8, 3, 8, 3), ToolTip = "缩小（Ctrl + 滚轮向下 / Ctrl + -）" };
        zoomOut.Click += (_, _) => ApplyZoom(_zoom.ScaleX / 1.1, null);
        _kindBar.Children.Add(zoomOut);

        _zoomText.VerticalAlignment = VerticalAlignment.Center;
        _zoomText.Margin = new Thickness(6, 0, 6, 0);
        _zoomText.FontSize = 11;
        _zoomText.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        _kindBar.Children.Add(_zoomText);

        var zoomIn = new Button { Content = "＋", Padding = new Thickness(8, 3, 8, 3), ToolTip = "放大（Ctrl + 滚轮向上 / Ctrl + =）" };
        zoomIn.Click += (_, _) => ApplyZoom(_zoom.ScaleX * 1.1, null);
        _kindBar.Children.Add(zoomIn);

        var zoomFit = new Button { Content = "适应窗口", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0), ToolTip = "缩放到刚好装进这块地方" };
        zoomFit.Click += (_, _) => ZoomToFit();
        _kindBar.Children.Add(zoomFit);

        var zoomReset = new Button { Content = "100%", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0), ToolTip = "回到 100%（Ctrl + 0）" };
        zoomReset.Click += (_, _) => ResetZoom();
        _kindBar.Children.Add(zoomReset);
        UpdateZoomText();
    }

    private void UpdateZoomText() => _zoomText.Text = $"{_zoom.ScaleX * 100:0}%";

    private void RefreshKindPick()
    {
        // 同类物品的模块候选：卡牌 / 药水 / 遗物各自能用的触发时机不一样，但别的模块通用
        _kindPick.Items.Clear();
        foreach (var k in ForgeGraphCatalog.All)
        {
            if (string.Equals(k.Id, "Trigger", StringComparison.Ordinal) && _graph?.TriggerNode is not null) continue;   // 一块布一个触发时机
            if (string.Equals(k.Id, "Energy", StringComparison.Ordinal) && _graph?.Nodes.Any(n => n.Kind == "Energy") == true) continue;
            _kindPick.Items.Add(k.Display);
        }
        if (_kindPick.Items.Count > 0) _kindPick.SelectedIndex = 0;
    }

    private ForgeNodeKind? SelectedKind()
    {
        string? name = _kindPick.SelectedItem as string;
        return name is null ? null : ForgeGraphCatalog.All.FirstOrDefault(k => k.Display == name);
    }

    private void AddSelectedKind()
    {
        var kind = SelectedKind();
        if (kind is null || _graph is null) return;
        var (x, y) = ForgeGraphCatalog.DefaultPosition(kind.Id, _graph.Nodes.Count);
        var node = new ForgeNodeSpec { Kind = kind.Id, X = x, Y = y };
        if (kind.Id == "Trigger")
            node.SetField("Timing", ForgeTimings.For(_scope).FirstOrDefault()?.Id ?? ForgeTimings.CardPlayed);
        if (kind.Id == "Effect")
            node.SetField("Effect", "Damage");
        _graph.Nodes.Add(node);
        Rebuild();
        Touch($"已加一个【{kind.Display}】模块 —— 拖动标题栏可以移动，点右边的圆点再点左边的圆点就能连线。");
    }

    private void RemoveSelected()
    {
        if (_graph is null || _selectedNodes.Count == 0) { Touch("先点一下要删的模块（或者框选几个）。"); return; }
        // 【能量】删不掉：它是固定模块（选中那条路也进不来，这里是兜底 —— 比如切换视图前留下的旧选中）
        int picked = _selectedNodes.Count;
        var doomed = _selectedNodes.Where(n => !IsLocked(n)).ToList();
        if (doomed.Count == 0)
        {
            _selectedNodes.Clear();
            UpdateSelectionVisual();
            Touch(EnergyLockedNote);
            return;
        }
        foreach (var node in doomed)
        {
            _graph.Nodes.Remove(node);
            // 连着它的线一起清掉（不留悬空连线）
            _graph.Links.RemoveAll(l => l is not null && (l.From == node.Id || l.To == node.Id));
        }
        _selectedNodes.Clear();
        Rebuild();
        Touch($"已删掉 {doomed.Count} 个模块和它们身上的连线。"
            + (doomed.Count < picked ? "（选中的里面有【能量】，它留着 —— 固定模块删不掉。）" : ""));
    }

    // ==================================================================================
    //  重建整块布
    // ==================================================================================

    /// <summary>把整块布按当前数据重画一遍（改数据之后调用）。</summary>
    public void Rebuild()
    {
        RefreshPowerItemsIfStale();
        RefreshKindPick();
        _nodeViews.Clear();
        _portPos.Clear();
        _root.Children.Clear();
        _root.Children.Add(_wireLayer);      // 连线画在模块**下面**（但连线自己能吃到右键）
        _root.Children.Add(_overlay);
        _wireLayer.Children.Clear();
        _overlay.Children.Clear();
        CancelPending();
        // 选中项跟着数据走：模块被删掉了就从选中列表里去掉；【能量】是固定模块，永远不进选中列表
        _selectedNodes.RemoveAll(n => _graph?.Nodes.Contains(n) != true || IsLocked(n));

        if (_graph is null)
        {
            _wrap("（先在左边选一件物品）", 60, 60);
            return;
        }

        foreach (var node in _graph.Nodes.ToList())
        {
            if (node is null) continue;
            var view = BuildNodeView(node);
            _nodeViews[node.Id] = view;
            _root.Children.Add(view);
        }
        DrawWires();
        UpdateCanvasSize();
        Touch(_graph.Links.Count(l => l is not null && !IsLinkBroken(l)).ToString(CultureInfo.InvariantCulture) + " 条连线");
    }

    private void _wrap(string text, double x, double y)
    {
        var tb = new TextBlock { Text = text, Foreground = Brushes.Gray, FontSize = 13 };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        _root.Children.Add(tb);
    }

    /// <summary>画一个模块：标题栏（拖这里）+ 端口行 + 字段。</summary>
    private FrameworkElement BuildNodeView(ForgeNodeSpec node)
    {
        var kind = node.KindDef;
        bool unknown = kind is null;
        string accent = kind?.Accent ?? "#B00020";
        bool isEnergy = node.Kind == "Energy";

        var border = new Border
        {
            Width = NodeWidth,
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)),
            BorderBrush = new SolidColorBrush(unknown ? Color.FromRgb(0xB0, 0x00, 0x20) : (Color)ColorConverter.ConvertFromString(accent)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.35 },
        };
        Canvas.SetLeft(border, node.X);
        Canvas.SetTop(border, node.Y);

        var stack = new StackPanel();

        // ---- 标题栏 ----
        var header = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)),
            CornerRadius = new CornerRadius(2, 2, 0, 0),
            Padding = new Thickness(6, 3, 4, 3),
            Cursor = isEnergy ? Cursors.Arrow : Cursors.SizeAll,
            ToolTip = kind?.Hint,
        };
        var headerRow = new DockPanel();
        var del = new Button
        {
            Content = "×",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "删掉这个模块（连着它的线一起清掉）",
        };
        del.Click += (_, e) => { e.Handled = true; RemoveNode(node); };
        // 【能量】是固定模块：标题栏上不摆「×」（它删不掉，摆一个按不动的按钮只会让人困惑）
        if (!isEnergy)
        {
            DockPanel.SetDock(del, Dock.Right);
            headerRow.Children.Add(del);
        }
        headerRow.Children.Add(new TextBlock
        {
            Text = node.Title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        header.Child = headerRow;
        // 只留一个 MouseLeftButtonDown：里面会先选中、再开始拖动（两个处理器里前一个把 e.Handled 置了，
        // 后一个根本不会跑，等于「选中」概率性失效）。标题栏上那个「×」自己把事件吞掉，不会触发拖动。
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
            BeginDrag(node, border, e);
        };
        stack.Children.Add(header);

        // ---- 端口行 + 字段 ----
        // 左边一列 = 接入点，右边一列 = 出口（含【if】最上面那个「条件」口）。
        var leftPorts = new List<(int Row, ForgePort Port, int Index)>();
        var rightPorts = new List<(int Row, ForgePort Port, int Index)>();
        int rows = Math.Max(kind?.Inputs.Count ?? 0, kind?.Outputs.Count ?? 0);
        for (int r = 0; r < rows; r++)
        {
            if (kind?.InputPort(r) is { } ip) leftPorts.Add((r, ip, r));
            if (kind?.OutputPort(r) is { } op) rightPorts.Add((r, op, r));
        }
        int rowCount = Math.Max(
            leftPorts.Count == 0 ? 0 : leftPorts.Max(x => x.Row) + 1,
            rightPorts.Count == 0 ? 0 : rightPorts.Max(x => x.Row) + 1);
        for (int r = 0; r < rowCount; r++)
        {
            var row = new Grid { Height = PortRowHeight, Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            foreach (var (lpRow, lp, lpIndex) in leftPorts)
            {
                if (lpRow != r) continue;
                var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
                left.Children.Add(MakePortDot(node, lpIndex, lp, isOutput: false));
                if (lp.HasLabel)
                    left.Children.Add(new TextBlock { Text = lp.Label, FontSize = 10, Foreground = Brushes.DimGray, Margin = new Thickness(2, 0, 0, 0) });
                SetColumn(left, 0);
                row.Children.Add(left);
            }

            foreach (var (rpRow, rp, rpIndex) in rightPorts)
            {
                if (rpRow != r) continue;
                var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                if (rp.HasLabel)
                    right.Children.Add(new TextBlock { Text = rp.Label, FontSize = 10, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 2, 0) });
                right.Children.Add(MakePortDot(node, rpIndex, rp, isOutput: true));
                SetColumn(right, 1);
                row.Children.Add(right);
            }
            stack.Children.Add(row);
        }

        if (kind is not null)
        {
            foreach (var field in kind.Fields)
            {
                var ui = BuildField(node, kind, field);
                if (ui is not null) stack.Children.Add(ui);
            }
        }
        else
        {
            stack.Children.Add(new TextBlock { Text = "认不出来的模块（重新选一次种类）", FontSize = 10, Margin = new Thickness(6, 2, 6, 4), Foreground = Brushes.Firebrick });
        }

        border.Child = stack;
        return border;
    }

    /// <summary>这个端口对象在模块上的下标（入口找 Inputs、出口找 Outputs）。画圆点和连线的 Tag 都用它。</summary>
    private static int PortIndexOf(ForgeNodeSpec node, ForgePort port)
    {
        var ins = node.KindDef?.Inputs;
        if (ins is not null)
            for (int i = 0; i < ins.Count; i++)
                if (ReferenceEquals(ins[i], port) || string.Equals(ins[i].Key, port.Key, StringComparison.Ordinal)) return i;
        var outs = node.KindDef?.Outputs;
        if (outs is not null)
            for (int i = 0; i < outs.Count; i++)
                if (ReferenceEquals(outs[i], port) || string.Equals(outs[i].Key, port.Key, StringComparison.Ordinal)) return i;
        return -1;
    }

    private void RemoveNode(ForgeNodeSpec node)
    {
        if (_graph is null) return;
        // 【能量】删不掉（它的标题栏上那个「×」也一起藏了，这里是兜底）
        if (IsLocked(node)) { Touch(EnergyLockedNote); return; }
        _graph.Nodes.Remove(node);
        _graph.Links.RemoveAll(l => l is not null && (l.From == node.Id || l.To == node.Id));
        _selectedNodes.Remove(node);
        Rebuild();
        Touch("已删掉这个模块和它身上的连线。");
    }

    /// <summary>一个端口圆点：**按住拖**出一条线（拖的时候线跟着鼠标），松手落在对面圆点上就接上。</summary>
    private FrameworkElement MakePortDot(ForgeNodeSpec node, int index, ForgePort port, bool isOutput)
    {
        bool wired = _graph is not null && (isOutput
            ? _graph.Outgoing(node.Id, index).Count > 0
            : _graph.Incoming(node.Id, index) is not null);

        var dot = new Ellipse
        {
            Width = 11,
            Height = 11,
            Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            StrokeThickness = 1.5,
            Fill = wired ? new SolidColorBrush(Color.FromRgb(0x2A, 0x6F, 0xB5)) : Brushes.WhiteSmoke,
            Cursor = Cursors.Cross,
            ToolTip = isOutput
                ? $"{port.Hint}\n（按住它拖到对面的接入点上松手就能接上；也可以先点它再点对面。右键 = 取消）"
                : $"{port.Hint}\n（按住它拖到对面的圆点上松手就能接上；也可以先点一下再点对面。右键 = 取消）",
        };
        // 标记一下这个圆点是"第几个口、进还是出"，连线时读它
        dot.Tag = $"{node.Id}|{index}|{(isOutput ? "out" : "in")}";
        dot.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;    // 别让画布把我们这个点击当成"取消"
            StartWireDrag(node.Id, index, isOutput);
        };
        // 拉线过程中鼠标从别的圆点上经过时，画布收不到移动事件，所以圆点自己转发一份
        dot.MouseMove += (_, e) =>
        {
            if (_pending is null) return;
            e.Handled = true;
            WireTo(e.GetPosition(_root));
        };
        dot.MouseLeftButtonUp += (_, e) =>
        {
            if (_pending is null) return;
            e.Handled = true;
            FinishWireDrag(e.OriginalSource as DependencyObject ?? dot);
        };
        dot.MouseRightButtonDown += (_, e) => { e.Handled = true; CancelPending(); Touch("已取消拉线。"); };
        return dot;
    }

    /// <summary>
    /// 【效果】模块上这一栏跟当前选的效果种类有没有关系 —— 没关系就不显示（不再把二十多栏全摊开）。
    /// 需求原话：「效果的药水选项应当效果种类选择药水后，才能出现选择药水的菜单栏」。
    /// 只藏界面，数据（Fields）照旧留着：换回对应种类时之前填的还在。
    /// </summary>
    private static bool EffectFieldApplies(string key, string effectKind) => key switch
    {
        "SpawnCardId" or "SpawnTo" => effectKind is "GenerateCard",
        "PotionPick" or "PotionId" => effectKind is "GainPotion",
        "Pick" => effectKind is "ExhaustCard" or "DiscardCard" or "UpgradeCard" or "TransformCard" or "CopyCard",
        // 「增益 / 减益」那一栏不藏：它是可搜索下拉，而且「临时增益 / 清除状态」等好几种都要看它，
        // 这里宁可贵一点也不要让用户找不到自己在哪选状态。
        _ => true,
    };

    /// <summary>模块上的一个字段（下拉 / 公式 / 数字 / 勾选）。</summary>
    private FrameworkElement? BuildField(ForgeNodeSpec node, ForgeNodeKind kind, ForgeNodeField field)
    {
        // 【效果】：只显示与当前效果种类有关的那几栏
        if (string.Equals(kind.Id, "Effect", StringComparison.Ordinal)
            && !EffectFieldApplies(field.Key, node.Get("Effect", "Damage")))
            return null;
        // 触发时机：候选按这一类物品筛；血量 / 生效次数只在选了对应时机时才显示
        if (string.Equals(field.Key, "Timing", StringComparison.Ordinal))
        {
            var options = ForgeTimings.For(_scope);
            if (options.Count == 0) return null;
            var pick = new ComboBox { Margin = new Thickness(4, 1, 4, 2), FontSize = 11 };
            foreach (var o in options) pick.Items.Add(o.Display);
            string cur = node.Get(field.Key, options[0].Id);
            var curOpt = options.FirstOrDefault(o => o.Id == cur) ?? options[0];
            pick.SelectedItem = curOpt.Display;
            pick.SelectionChanged += (_, _) =>
            {
                var sel = options.FirstOrDefault(o => o.Display as string == pick.SelectedItem as string);
                if (sel is null) return;
                node.SetField(field.Key, sel.Id);
                Rebuild();      // 换了时机，下面「血量 / 生效次数」的显隐要跟着变
                Touch($"【触发时机】改成「{sel.Display}」：{sel.Hint}");
            };
            return WithLabel(field, pick);
        }
        if (string.Equals(field.Key, "HpThreshold", StringComparison.Ordinal) && !ForgeTimings.NeedsHp(node.Get("Timing"))) return null;
        // 【触发时机】上新加的「盯哪个状态 / 到达 N 层」两栏（第 9 批 ④）：只在
        // 「某个状态层数到达 N 层时 / 层数变化后 / 被移除时」这三条时机上才显示
        //（别的时机上它们没有意义，显示出来只会让用户白填）
        if (string.Equals(kind.Id, "Trigger", StringComparison.Ordinal)
            && field.Key is "PowerId" or "PowerAmount"
            && !ForgeTimings.NeedsPowerWatch(node.Get("Timing"))) return null;
        if ((string.Equals(field.Key, "CountLimit", StringComparison.Ordinal) || string.Equals(field.Key, "CountScope", StringComparison.Ordinal))
            && !ForgeTimings.NeedsCount(node.Get("Timing"))) return null;
        // 「随机目标允许重复选中同一个敌人」只在对象模块选「随机敌人」时出现
        if (string.Equals(field.Key, "AllowDuplicate", StringComparison.Ordinal)
            && !string.Equals(node.Get("Pick", "Self"), "RandomEnemy", StringComparison.Ordinal)) return null;
        // 【对象】选「指定召唤物」时才显示「召唤物名称」（需求）
        if (string.Equals(field.Key, "SummonId", StringComparison.Ordinal)
            && !string.Equals(node.Get("Pick", "Self"), "ChosenSummon", StringComparison.Ordinal)) return null;

        switch (field.Type)
        {
            case ForgeFieldType.Check:
            {
                var cb = new CheckBox
                {
                    Content = field.Label,
                    FontSize = 11,
                    Margin = new Thickness(4, 2, 4, 2),
                    IsChecked = node.GetFlag(field.Key, field.Default == "1"),
                    ToolTip = field.Hint,
                };
                cb.Checked += (_, _) => node.SetFlag(field.Key, true);
                cb.Unchecked += (_, _) => node.SetFlag(field.Key, false);
                return cb;
            }
            case ForgeFieldType.Pick:
            {
                var combo = new ComboBox { Margin = new Thickness(4, 1, 4, 2), FontSize = 11 };
                if (string.Equals(field.Key, "PowerId", StringComparison.Ordinal))
                {
                    // 状态：本体 200+ 条 + 玩家自己的自定义状态，用项目里那个可搜索下拉
                    // （数据源见 PowerSource：窗口把「含自定义状态」的那个集合绑过来）
                    var sc = new SearchComboBox
                    {
                        Margin = new Thickness(4, 1, 4, 2),
                        ItemsSource = _powers,
                        SelectedValuePath = "Id",
                        SearchPath = "Display",
                        SelectedValue = node.Get(field.Key),
                    };
                    sc.SetBinding(SearchComboBox.SelectedValueProperty, new System.Windows.Data.Binding
                    {
                        Source = node,
                        Path = new PropertyPath("Fields[PowerId]"),
                        Mode = System.Windows.Data.BindingMode.TwoWay,
                    });
                    return WithLabel(field, sc);
                }

                IReadOnlyList<ForgeFieldOption> options = field.Key switch
                {
                    "Pick" => ForgeGraphCatalog.UnitPicks,
                    "Effect" => ForgeGraphCatalog.EffectPicks,
                    "Cond" => ForgeGraphCatalog.ConditionPicks,
                    "CountScope" => ForgeTimings.CountScopes,
                    "SpawnCardId" => ForgeGraphCatalog.CardChoices,
                    "PotionId" => ForgeGraphCatalog.PotionChoices,
                    // 【对象】的「召唤物名称」：候选来自当前配置里那些启用的召唤物（唯一标识 = 类名）
                    "SummonId" => SummonChoices(),
                    _ => field.OptionList,
                };
                foreach (var o in options) combo.Items.Add(o.Display);
                string cur = node.Get(field.Key, field.Default.Length > 0 ? field.Default : (options.Count > 0 ? options[0].Id : ""));
                var curOpt = options.FirstOrDefault(o => o.Id == cur);
                if (curOpt is not null) combo.SelectedItem = curOpt.Display;
                combo.SelectionChanged += (_, _) =>
                {
                    var sel = options.FirstOrDefault(o => o.Display as string == combo.SelectedItem as string);
                    if (sel is null) return;
                    node.SetField(field.Key, sel.Id);
                    if (field.Key is "Pick" or "Effect" or "Cond") Rebuild();
                };
                return WithLabel(field, combo);
            }
            case ForgeFieldType.Number:
            {
                var box = new TextBox
                {
                    Margin = new Thickness(4, 1, 4, 2),
                    FontSize = 11,
                    Text = node.Get(field.Key, field.Default),
                    ToolTip = field.Hint,
                };
                box.TextChanged += (_, _) => node.SetField(field.Key, box.Text);
                return WithLabel(field, box);
            }
            case ForgeFieldType.Formula:
            case ForgeFieldType.Text:
            default:
            {
                var box = new TextBox
                {
                    Margin = new Thickness(4, 1, 4, 2),
                    FontSize = 11,
                    Text = node.Get(field.Key, field.Default),
                    ToolTip = field.Hint,
                    AcceptsReturn = field.Type == ForgeFieldType.Formula,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = field.Type == ForgeFieldType.Formula ? 34 : 0,
                };
                box.TextChanged += (_, _) => node.SetField(field.Key, box.Text);
                if (field.Type == ForgeFieldType.Formula)
                {
                    var wrap = new StackPanel();
                    wrap.Children.Add(new TextBlock { Text = field.Label, FontSize = 10, Margin = new Thickness(4, 2, 4, 0), Foreground = Brushes.DimGray });
                    wrap.Children.Add(box);
                    var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 1, 4, 3) };
                    var insert = new Button { Content = "填入参数…", FontSize = 10, Padding = new Thickness(6, 1, 6, 1) };
                    insert.Click += (_, _) => InsertParamInto(box);
                    bar.Children.Add(insert);
                    var hint = new TextBlock
                    {
                        Text = "X = 当前能量，Y = 当前额外资源量",
                        FontSize = 10,
                        Margin = new Thickness(6, 3, 0, 0),
                        Foreground = Brushes.DimGray,
                    };
                    bar.Children.Add(hint);
                    wrap.Children.Add(bar);
                    return wrap;
                }
                return WithLabel(field, box);
            }
        }
    }

    /// <summary>自检用：【对象】的「召唤物名称」这一栏在给定的「对象」选择下会不会显示。</summary>
    public bool UnitSummonFieldVisibleForCheck(ForgeNodeSpec unit, string pick)
    {
        unit.SetField("Pick", pick);
        var kind = ForgeGraphCatalog.Find("Unit");
        var field = kind?.Fields.FirstOrDefault(f => f.Key == "SummonId");
        return field is not null && BuildField(unit, kind!, field) is not null;
    }

    /// <summary>自检用：【触发时机】的某一栏在给定的时机下会不会显示（第 9 批 ④：盯状态的那两栏）。</summary>
    public bool TriggerFieldVisibleForCheck(ForgeNodeSpec trigger, string timing, string fieldKey)
    {
        trigger.SetField("Timing", timing);
        var kind = ForgeGraphCatalog.Find("Trigger");
        var field = kind?.Fields.FirstOrDefault(f => f.Key == fieldKey);
        return field is not null && BuildField(trigger, kind!, field) is not null;
    }

    /// <summary>自检用：【效果】的某一栏在当前选的效果种类下会不会显示（需求：只有对应种类才出现）。</summary>
    public bool EffectFieldVisibleForCheck(ForgeNodeSpec effect, string effectKind, string fieldKey)
    {
        effect.SetField("Effect", effectKind);
        var kind = ForgeGraphCatalog.Find("Effect");
        var field = kind?.Fields.FirstOrDefault(f => f.Key == fieldKey);
        return field is not null && BuildField(effect, kind!, field) is not null;
    }

    /// <summary>「召唤物」下拉的候选（来自当前配置里那些启用的召唤物；Id = 生成用的英文类名）。</summary>
    private IReadOnlyList<ForgeFieldOption> SummonChoices()
    {
        if (Owner is null) return Array.Empty<ForgeFieldOption>();
        return Core.Generation.PetGen.Choices(Owner)
            .Where(c => !Core.Generation.PetGen.IsAll(c.Id))
            .Select(c => new ForgeFieldOption(c.Id, c.Display))
            .ToList();
    }

    private static FrameworkElement WithLabel(ForgeNodeField field, FrameworkElement inner)
    {
        var wrap = new StackPanel { ToolTip = field.Hint };
        wrap.Children.Add(new TextBlock
        {
            Text = field.Label,
            FontSize = 10,
            Margin = new Thickness(4, 3, 4, 0),
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
        });
        wrap.Children.Add(inner);
        return wrap;
    }

    /// <summary>「填入参数…」：列出已定义的参数 + X / Y，点一下插到光标处。</summary>
    private void InsertParamInto(TextBox box)
    {
        var items = new List<string> { "X", "Y" };
        foreach (var p in Owner?.CustomParams ?? new System.Collections.ObjectModel.ObservableCollection<ParamSpec>())
            items.Add(ParamNames.Normalize(p.Name));
        var menu = new ContextMenu();
        foreach (string id in items.Distinct(StringComparer.Ordinal))
        {
            var mi = new MenuItem { Header = id == "X" ? "X（当前所有能量）" : id == "Y" ? "Y（当前所有额外资源量）" : id + "=" };
            mi.Click += (_, _) =>
            {
                int at = box.CaretIndex;
                box.Text = box.Text.Insert(at, id);
                box.CaretIndex = at + id.Length;
            };
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    /// <summary>这块布归属的配置（「填入参数…」要列参数）。</summary>
    public CharacterProfile? Owner { get; set; }

    // ==================================================================================
    //  拖动
    // ==================================================================================

    private Point _dragStart;

    /// <summary>当前挂在标题栏上的拖动处理器（一次只能有一份，重新开始拖动前先摘掉）。</summary>
    private FrameworkElement? _dragHost;

    private MouseEventHandler? _dragMove;
    private MouseButtonEventHandler? _dragUp;

    /// <summary>
    /// 从标题栏开始拖动一块模块。
    /// view 必须是**这个模块自己那个外框**（Border）—— 以前这里写的是 `(Border)header.Parent`，
    /// 而标题栏的 Parent 是装标题栏 + 端口 + 字段的 StackPanel，于是**一点标题栏就抛
    /// InvalidCastException**（「无法把 StackPanel 转成 Border」），选中 / 拖动 / 连线全被这一下打断。
    /// 现在由调用方把外框直接传进来。
    /// </summary>
    private void BeginDrag(ForgeNodeSpec node, Border view, MouseButtonEventArgs e)
    {
        // 【能量】不可选中 / 不可拖动（它固定在左上角）—— 点它就只说明一句
        if (IsLocked(node)) { Touch(EnergyLockedNote); return; }
        SelectNode(node, view);
        // 拖一个 = 把**当前选中的那一批**一起拖（多选之后整块挪动）
        if (!_selectedNodes.Contains(node)) _selectedNodes.Add(node);
        UpdateSelectionVisual();
        var moving = _selectedNodes.Where(n => n is not null && !string.Equals(n.Kind, "Energy", StringComparison.Ordinal)).ToList();
        var startPos = moving.ToDictionary(n => n, n => (n.X, n.Y));
        var host = view;
        _dragStart = e.GetPosition(_root);
        EndDrag();                  // 上一次的处理器先摘干净（不然会叠着一起跑）—— 必须在挂新的之前调
        _dragHost = host;
        _dragMove = (_, me) =>
        {
            if (me.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
            Point now = me.GetPosition(_root);
            double dx = now.X - _dragStart.X, dy = now.Y - _dragStart.Y;
            ApplyMove(moving, startPos, dx, dy);
        };
        _dragUp = (_, _) => EndDrag();
        host.MouseMove += _dragMove;
        host.MouseLeftButtonUp += _dragUp;
        host.CaptureMouse();
        // 别让画布把这一下当成「在空白处按下」而取消选择。
        // 自检里 new 出来的 MouseButtonEventArgs 没有关联的 RoutedEvent，给它置 Handled 会抛异常，所以先判一下。
        if (e.RoutedEvent is not null) e.Handled = true;
    }

    /// <summary>按「起点 + 位移」把一批模块挪过去（拖动时每帧调它）。</summary>
    private void ApplyMove(List<ForgeNodeSpec> moving, Dictionary<ForgeNodeSpec, (double X, double Y)> startPos, double dx, double dy)
    {
        foreach (var n in moving)
        {
            if (!startPos.TryGetValue(n, out var s)) continue;
            n.X = Math.Max(0, s.X + dx);
            n.Y = Math.Max(0, s.Y + dy);
            if (_nodeViews.TryGetValue(n.Id, out var v))
            {
                Canvas.SetLeft(v, n.X);
                Canvas.SetTop(v, n.Y);
            }
        }
        DrawWires();
        UpdateCanvasSize();
    }

    private void EndDrag()
    {
        if (_dragHost is not null)
        {
            if (_dragMove is not null) _dragHost.MouseMove -= _dragMove;
            if (_dragUp is not null) _dragHost.MouseLeftButtonUp -= _dragUp;
            if (_dragHost.IsMouseCaptured) _dragHost.ReleaseMouseCapture();
        }
        _dragHost = null;
        _dragMove = null;
        _dragUp = null;
    }

    /// <summary>这个元素是不是某个按钮（标题栏上的「×」点下去不该变成拖动）。</summary>
    private static bool IsInsideButton(DependencyObject? src)
    {
        for (DependencyObject? cur = src; cur is not null; cur = VisualTreeHelper.GetParent(cur))
            if (cur is ButtonBase) return true;
        return false;
    }

    /// <summary>
    /// 这块模块的外框（Border）。给自检用：自检里造不出真鼠标事件，
    /// 只能直接调它来验「标题栏 → 外框」这条查找路径（上一版就是这条路径上写错了）。
    /// 找不到返回 null。找不到外框时不该抛异常。
    /// </summary>
    public Border? NodeView(ForgeNodeSpec node)
    {
        if (node is null) return null;
        return _nodeViews.TryGetValue(node.Id, out var v) ? v as Border : null;
    }

    /// <summary>
    /// 模拟「在某个模块的标题栏上按下左键开始拖动」——只跑查外框 + 选中 + 挂处理器那一段，
    /// 不伪造鼠标位移（自检没法造真的鼠标事件）。找不到外框时**返回 false 而不是抛异常**。
    /// </summary>
    public bool BeginDragFromHeader(ForgeNodeSpec node)
    {
        var view = NodeView(node);
        if (view is null) return false;
        BeginDrag(node, view, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
        return true;
    }

    /// <summary>把当前选中那批按位移挪一下（自检用；拖动过程中走的是同一套算术）。</summary>
    public void NudgeSelection(double dx, double dy)
    {
        var moving = _selectedNodes.Where(n => n is not null && !string.Equals(n.Kind, "Energy", StringComparison.Ordinal)).ToList();
        ApplyMove(moving, moving.ToDictionary(n => n, n => (n.X, n.Y)), dx, dy);
    }

    /// <summary>某个模块第 index 个端口的圆点（自检里拿它读 Tag，验证“点圆点 → 认端口”那条路）。</summary>
    public Ellipse? PortDot(ForgeNodeSpec node, int index, bool isOutput)
    {
        var view = NodeView(node);
        if (view is null) return null;
        var want = $"{node.Id}|{index}|{(isOutput ? "out" : "in")}";
        // 圆点不是行的直接子元素（行 → StackPanel → 圆点 + 文字），要顺视觉树往下找
        var found = FindDescendant<Ellipse>(view, e => string.Equals(e.Tag as string, want, StringComparison.Ordinal));
        return found;
    }

    /// <summary>在视觉树里找一个符合条件的后代元素（自检排错用）。</summary>
    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && match(t)) return t;
            var deep = FindDescendant(child, match);
            if (deep is not null) return deep;
        }
        return null;
    }

    /// <summary>从圆点的 Tag 里读出「哪一块模块的第几个口」；Tag 不对时返回 null（不该抛异常）。</summary>
    public static (string NodeId, int Port, bool IsOutput)? PortFromTag(object? tag)
    {
        string[] parts = (tag as string ?? "").Split('|');
        if (parts.Length != 3 || parts[0].Length == 0) return null;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)) return null;
        if (parts[2] == "out") return (parts[0], port, true);
        if (parts[2] == "in") return (parts[0], port, false);
        return null;
    }

    /// <summary>
    /// 模拟「点了一个端口圆点」（自检用；真的点圆点走的是同一条 OnPortClick）。
    /// 走圆点的 Tag 而不是直接给参数，这样「Tag 拼错 / 读不出来」这种错也测得到。
    /// </summary>
    public bool PortClickForCheck(ForgeNodeSpec node, int index, bool isOutput)
    {
        var dot = PortDot(node, index, isOutput);
        if (dot is null) return false;
        var at = PortFromTag(dot.Tag);
        if (at is null) return false;
        OnPortClick(at.Value.NodeId, at.Value.Port, at.Value.IsOutput);
        return true;
    }

    /// <summary>立刻结束拖动（自检里造不出 MouseUp，调它把处理器摘掉）。</summary>
    public void EndDragForCheck() => EndDrag();

    // ---- 下面几个是自检用的入口：自检里造不出真的鼠标事件，只能直接调内部那几步 ----

    /// <summary>拉线中那条线还在不在（自检用）。</summary>
    public bool PendingWireVisible => _pendingLine is not null;

    /// <summary>模拟「按住某个圆点开始拉线」（自检用）。</summary>
    public void StartWireDragForCheck(ForgeNodeSpec node, int index, bool isOutput) => StartWireDrag(node.Id, index, isOutput);

    /// <summary>模拟「拉线时鼠标移动」（自检用）。</summary>
    public void WireToForCheck(Point at) => WireTo(at);

    /// <summary>模拟「松手」（自检用）。<paramref name="dropOn"/> = 松手时鼠标底下的元素。</summary>
    public void FinishWireDragForCheck(DependencyObject? dropOn) => FinishWireDrag(dropOn);

    /// <summary>模拟「右键点一条连线」（自检用）。</summary>
    public void RemoveLinkForCheck(ForgeLinkSpec link) => RemoveLink(link);

    /// <summary>布上目前画了几条连线（自检用）。</summary>
    public int WireCountForCheck => _wireLayer.Children.OfType<Path>().Count(w => w.Tag is ForgeLinkSpec);

    /// <summary>「这条线接得上吗」（自检用：直接问那条判定，不真的连线、不改数据）。</summary>
    public bool CouldConnectForCheck(ForgeNodeSpec from, int fromPort, ForgeNodeSpec to, int toPort)
        => ConnectionProblem(from.Id, fromPort, to.Id, toPort) is null;

    /// <summary>「这条线接不上」的原因（自检用，看提示语对不对）。</summary>
    public string? ConnectionProblemForCheck(ForgeNodeSpec from, int fromPort, ForgeNodeSpec to, int toPort)
        => ConnectionProblem(from.Id, fromPort, to.Id, toPort);

    /// <summary>自检用：某个元素算不算「可以当终点的端口」（null = 不算）。</summary>
    public string PortAtForCheck(DependencyObject? at)
    {
        var t = PortAt(at);
        return t is null ? "null" : $"{t.Value.NodeId[..Math.Min(6, t.Value.NodeId.Length)]}#{t.Value.Port}";
    }

    /// <summary>自检用：现在「拉线中」的起点（没在拉线就是 null）。</summary>
    public string PendingForCheck => _pending is null ? "没在拉线" : $"{_pending.Value.NodeId[..Math.Min(6, _pending.Value.NodeId.Length)]}#{_pending.Value.Port}";

    /// <summary>自检用：某个模块的第 index 个出口的 Key（看是不是「来源插槽」）。</summary>
    public string OutputKeyForCheck(ForgeNodeSpec node, int index) => node.OutputPort(index)?.Key ?? "(无)";

    /// <summary>布上已经画出来的模块个数（自检用：和 Graph.Nodes.Count 对不上就说明画还没重建）。</summary>
    public int DrawnNodeCount => _nodeViews.Count;

    /// <summary>画布上模块的 Id 清单（自检排错用）。</summary>
    public string DrawnNodeIds => string.Join(",", _nodeViews.Keys.Select(k => k.Length >= 6 ? k.Substring(0, 6) : k));

    /// <summary>某个模块的**标题栏**（拖动把手）。自检拿它顺视觉树往上走，验证「标题栏 → 外框」这条路径。</summary>
    public FrameworkElement? NodeHeader(ForgeNodeSpec node)
    {
        var view = NodeView(node);
        return (view?.Child as Panel)?.Children.OfType<Border>().FirstOrDefault();
    }

    // ==================================================================================
    //  自检用的钩子（【能量】不可选中 / 不可删除那几条走这些）
    // ==================================================================================

    /// <summary>自检用：当前选中的模块（拷贝一份，改它不影响画布）。</summary>
    public IReadOnlyList<ForgeNodeSpec> SelectedNodesForCheck => _selectedNodes.ToList();

    /// <summary>自检用：清空选中。</summary>
    public void ClearSelectionForCheck()
    {
        _selectedNodes.Clear();
        UpdateSelectionVisual();
    }

    /// <summary>自检用：把一块**硬塞进**选中列表（模拟「旧状态 / 切视图前留下的选中」，验删除会不会跳过固定模块）。</summary>
    public void ForceSelectForCheck(ForgeNodeSpec node)
    {
        if (node is null) return;
        if (!_selectedNodes.Contains(node)) _selectedNodes.Add(node);
        UpdateSelectionVisual();
    }

    /// <summary>自检用：走一遍真实的「删除选中」（键盘 Delete / 工具条那个按钮同一条路）。</summary>
    public void RemoveSelectedForCheck() => RemoveSelected();

    /// <summary>自检用：这块模块的标题栏上有没有那个「×」删除按钮。</summary>
    public bool NodeHeaderHasDeleteForCheck(ForgeNodeSpec node)
    {
        var header = NodeHeader(node) as DependencyObject;
        if (header is null) return false;
        return FindButton(header, "×") is not null;
    }

    private static Button? FindButton(DependencyObject root, string content)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button b && string.Equals(b.Content as string, content, StringComparison.Ordinal)) return b;
            var found = FindButton(child, content);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>「全部可见」/ 工具条上那个百分比按钮：把缩放调回 100%。</summary>
    public void ResetZoom() => ApplyZoom(1.0, null);

    /// <summary>
    /// Ctrl + 滚轮缩放（需求）。缩放中心尽量落在鼠标底下：先算鼠标处那块「布坐标」，
    /// 缩放后再把滚动条挪到同一个布坐标还在鼠标底下。
    /// </summary>
    private void OnCanvasWheel(object sender, MouseWheelEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (!ctrl)
        {
            // 不带 Ctrl：照旧上下滚画布
            _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
            return;
        }
        e.Handled = true;
        double factor = e.Delta > 0 ? 1.1 : (1.0 / 1.1);
        ApplyZoom(_zoom.ScaleX * factor, e.GetPosition(_scroll));
    }

    private void ApplyZoom(double scale, Point? at)
    {
        double next = Math.Clamp(scale, ZoomMin, ZoomMax);
        if (Math.Abs(next - _zoom.ScaleX) < 0.0001) return;
        // 鼠标那一点在「布坐标」里是哪一格（缩放前）
        Point focus = at ?? new Point(_scroll.ViewportWidth / 2, _scroll.ViewportHeight / 2);
        double cx = (_scroll.HorizontalOffset + focus.X) / _zoom.ScaleX;
        double cy = (_scroll.VerticalOffset + focus.Y) / _zoom.ScaleX;

        _zoom.ScaleX = next;
        _zoom.ScaleY = next;
        _root.UpdateLayout();          // 让滚动范围按新尺寸算出来，再进行偏移
        _scroll.UpdateLayout();
        _scroll.ScrollToHorizontalOffset(cx * next - focus.X);
        _scroll.ScrollToVerticalOffset(cy * next - focus.Y);
        Touch($"缩放 {next * 100:0}%（Ctrl + 滚轮调整，Ctrl + 0 回 100%）");
        UpdateZoomText();
    }

    /// <summary>把整块布缩放到刚好装进可视范围（「适应窗口」）。</summary>
    public void ZoomToFit()
    {
        if (_graph is null || _graph.Nodes.Count == 0) return;
        double contentW = _graph.Nodes.Max(n => n.X + NodeWidth + 160);
        double contentH = _graph.Nodes.Max(n =>
            n.Y + HeaderHeight + TopPad
            + Math.Max(n.KindDef?.Inputs.Count ?? 0, n.KindDef?.Outputs.Count ?? 0) * (PortRowHeight + 2)
            + (n.KindDef?.Fields.Count ?? 0) * 46 + 80);
        double vw = _scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : 800;
        double vh = _scroll.ViewportHeight > 0 ? _scroll.ViewportHeight : 600;
        double scale = Math.Min(vw / Math.Max(contentW, 1), vh / Math.Max(contentH, 1));
        ApplyZoom(scale, new Point(0, 0));
        _scroll.ScrollToHorizontalOffset(0);
        _scroll.ScrollToVerticalOffset(0);
    }

    /// <summary>当前缩放（自检用）。</summary>
    public double ZoomForCheck => _zoom.ScaleX;

    /// <summary>自检用：按给定倍率缩放（走的是 Ctrl + 滚轮同一条路）。</summary>
    public void ZoomForCheckSet(double scale) => ApplyZoom(scale, new Point(10, 10));

    /// <summary>全选（Ctrl+A / 工具条上也有一份）。⚠【能量】不参与（固定模块，不可选中）。</summary>
    public void SelectAll()
    {
        if (_graph is null) return;
        _selectedNodes.Clear();
        foreach (var n in _graph.Nodes) if (n is not null && !IsLocked(n)) _selectedNodes.Add(n);
        UpdateSelectionVisual();
        int locked = _graph.Nodes.Count(n => IsLocked(n));
        Touch($"已全选 {_selectedNodes.Count} 个模块 —— 拖任意一个的标题栏可以一起移动；Delete 一起删。"
            + (locked > 0 ? $"（【能量】是固定模块，不参与选中 / 删除。）" : ""));
    }

    /// <summary>框选 / 多选之后，当前选了多少个（自检用）。</summary>
    public int SelectedCount => _selectedNodes.Count;

    /// <summary>把一批模块一起移动（框选之后拖动用的就是它，自检里能直接调）。</summary>
    public void MoveSelection(double dx, double dy)
    {
        foreach (var n in _selectedNodes)
        {
            if (n is null || string.Equals(n.Kind, "Energy", StringComparison.Ordinal)) continue;   // 【能量】固定不动
            n.X = Math.Max(0, n.X + dx);
            n.Y = Math.Max(0, n.Y + dy);
            if (_nodeViews.TryGetValue(n.Id, out var v))
            {
                Canvas.SetLeft(v, n.X);
                Canvas.SetTop(v, n.Y);
            }
        }
        DrawWires();
        UpdateCanvasSize();
    }

    private void SelectNode(ForgeNodeSpec? node, Border? view)
    {
        if (node is null) return;
        // 【能量】不可选中：点它就只说明一句（不然选中了再拖 / 再删又是麻烦）
        if (IsLocked(node)) { Touch(EnergyLockedNote); return; }
        bool add = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (!add) _selectedNodes.Clear();
        if (add && _selectedNodes.Contains(node)) _selectedNodes.Remove(node);
        else if (!_selectedNodes.Contains(node)) _selectedNodes.Add(node);
        UpdateSelectionVisual();
        Touch(_selectedNodes.Count == 1
            ? $"选中【{node.Title}】 —— 拖标题栏移动；Shift 点选 / 在空白处按住拖可以框选多个，「删除选中」一起删。"
            : $"已选中 {_selectedNodes.Count} 个模块 —— 拖动其中任意一个的标题栏，它们会一起移动。");
    }

    /// <summary>把选中状态画出来（选中 = 蓝色粗边框 + 右上角一个「已选」角标）。</summary>
    private void UpdateSelectionVisual()
    {
        foreach (var kv in _nodeViews)
        {
            if (kv.Value is not Border b) continue;
            var node = _graph?.Node(kv.Key);
            bool on = node is not null && _selectedNodes.Contains(node);
            b.BorderThickness = new Thickness(on ? 3 : 2);
            if (on) b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x6F, 0xB5));
            else if (node?.KindDef is { } kd)
                b.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(kd.Accent));
        }
    }

    // ==================================================================================
    //  框选（在空白处按住左键拖出一个虚线框）
    // ==================================================================================

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 点在模块 / 端口 / 连线上时，事件已经被它们自己处理掉了（e.Handled），这里只管空白处
        if (e.OriginalSource is DependencyObject src && IsOnNode(src)) return;
        CancelPending();
        if (!e.Handled) _selectedNodes.Clear();
        UpdateSelectionVisual();
        _banding = true;
        _bandStart = e.GetPosition(_root);
        _bandRect = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x2A, 0x6F, 0xB5)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 0x2A, 0x6F, 0xB5)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_bandRect, _bandStart.X);
        Canvas.SetTop(_bandRect, _bandStart.Y);
        _overlay.Children.Add(_bandRect);
        _root.CaptureMouse();
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_pending is not null && _pendingLine is not null)
        {
            // 正在拉线：线的一头钉在出口圆点上，另一头跟着鼠标
            WireTo(e.GetPosition(_root));
            return;
        }
        if (!_banding || _bandRect is null) return;
        Point now = e.GetPosition(_root);
        double x = Math.Min(now.X, _bandStart.X), y = Math.Min(now.Y, _bandStart.Y);
        double w = Math.Abs(now.X - _bandStart.X), h = Math.Abs(now.Y - _bandStart.Y);
        Canvas.SetLeft(_bandRect, x);
        Canvas.SetTop(_bandRect, y);
        _bandRect.Width = w;
        _bandRect.Height = h;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        // 拉线时松手在空白处（或模块身上）= 没接上；落在入口圆点上时圆点自己已经处理了
        if (_pending is not null)
        {
            FinishWireDrag(null);
            return;
        }
        if (!_banding) return;
        _banding = false;
        _root.ReleaseMouseCapture();
        if (_bandRect is null || _graph is null) return;
        double x = Canvas.GetLeft(_bandRect), y = Canvas.GetTop(_bandRect);
        double w = _bandRect.Width, h = _bandRect.Height;
        _overlay.Children.Remove(_bandRect);
        _bandRect = null;
        if (w < 4 || h < 4) return;      // 只是点了一下空白：等于取消选择

        bool add = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (!add) _selectedNodes.Clear();
        foreach (var node in _graph.Nodes)
        {
            if (node is null || IsLocked(node)) continue;   // 【能量】固定模块：框选也框不上
            // 用模块的外框做命中（宽 = 模块宽度，高按端口 / 字段粗估，和画布尺寸用的是同一套算法）
            double ny = node.Y;
            double nh = HeaderHeight + TopPad
                        + Math.Max(node.KindDef?.Inputs.Count ?? 0, node.KindDef?.Outputs.Count ?? 0) * (PortRowHeight + 2)
                        + (node.KindDef?.Fields.Count ?? 0) * 46 + 20;
            bool hit = node.X < x + w && node.X + NodeWidth > x && ny < y + h && ny + nh > y;
            if (hit && !_selectedNodes.Contains(node)) _selectedNodes.Add(node);
        }
        UpdateSelectionVisual();
        Touch(_selectedNodes.Count == 0 ? "没框到模块。" : $"已框选 {_selectedNodes.Count} 个模块 —— 拖其中一个的标题栏可以一起移动。");
    }

    /// <summary>这个元素是不是在某个模块里面（点在模块上时不该触发框选）。</summary>
    private static bool IsOnNode(DependencyObject src)
    {
        for (DependencyObject? cur = src; cur is not null; cur = VisualTreeHelper.GetParent(cur))
            if (cur is Border b && b.Child is StackPanel && b.Width > 0) return true;
        return false;
    }

    // ==================================================================================
    //  连线
    // ==================================================================================

    /// <summary>
    /// 这条线是「插槽线」（往一个带类型的口里插一块来源）还是「执行链线」。
    ///
    /// 为什么要分：【if】的「接入」是 InputPort(0)、「条件」是 OutputPort(0)，**下标都是 0**，
    /// 所以「这条线属于哪个口」只能靠**来源出口的类型**判（和生成端 / 积木视图同一套判据）：
    ///   · 来源出口有类型（Bool / Number / Object…）→ 插槽线；
    ///   · 来源出口没类型（【触发时机】/【对象】/【效果】/【指向对象】的执行链出口）→ 执行链线。
    /// </summary>
    private bool IsSlotLink(ForgeLinkSpec l)
    {
        if (_graph is null) return false;
        var from = _graph.Node(l.From);
        var to = _graph.Node(l.To);
        if (from is null || to is null) return false;
        var srcOut = from.OutputPort(l.FromPort);
        var slot = to.OutputPort(l.ToPort);
        if (slot is { PullsSource: true }) return srcOut?.AcceptsKind is not null;
        // 带类型的普通入口（【效果】的数值 / 指向对象、【if】的条件、【状态】的归属、【关系符】的左右值…）：
        // 判据是**类型配得上**（不是「同下标没有出口」——【if】的条件在入口 1、出口 1 却是「否」）。
        var inPort = to.InputPort(l.ToPort);
        return inPort is { AcceptsKind: not null } && ForgeGraphCatalog.TypesCompatible(srcOut?.AcceptsKind, inPort.AcceptsKind);
    }

    /// <summary>真正连线：从出口接到入口（拉线松手和「点出口再点入口」都走这里）。</summary>
    private void Connect(string fromNodeId, int fromPort, string toNodeId, int toPort)
    {
        if (_graph is null) return;
        if (string.Equals(fromNodeId, toNodeId, StringComparison.Ordinal) && fromPort == toPort)
        {
            Touch("松手松在了起点自己身上，没接。");
            return;
        }
        string? reason = ConnectionProblem(fromNodeId, fromPort, toNodeId, toPort);
        if (reason is not null)
        {
            // 连错：画一条红虚线并说明原因（需求：当用户连线不当报错后，将报错的地方标红显示）
            DrawBadLink(fromNodeId, fromPort, toNodeId, toPort);
            Touch("⚠ 这样连不行：" + reason);
            return;
        }
        // 一个「口」上只能有一条线 —— 但**分种类去重**：
        // 【if】的「接入」(InputPort(0)) 和「条件」(OutputPort(0)) 下标相同，一个是执行链、一个是插槽，
        // 两条线必须能同时存在。以前一律按 (To,ToPort) 删，于是：
        //   · 连了条件就把执行链那条删掉 → 整块 if 从链上掉下来、什么都不执行；
        //   · 从「条件」口往外拖（用户实测就是这么连的）方向是反的，生成端又看不到 → 条件恒为不成立。
        // 现在按「插槽线 / 执行链线」各自只留一条。顺带清掉反向那个没有意义的残留线（插槽口不是出口）。
        bool newIsSlot = IsSlotLink(new ForgeLinkSpec { From = fromNodeId, To = toNodeId, FromPort = fromPort, ToPort = toPort });
        _graph.Links.RemoveAll(l => l is not null && l.To == toNodeId && l.ToPort == toPort && IsSlotLink(l) == newIsSlot);
        if (newIsSlot)
            _graph.Links.RemoveAll(l => l is not null && l.From == toNodeId && l.FromPort == toPort && l.To != fromNodeId);
        _graph.Links.Add(new ForgeLinkSpec { From = fromNodeId, To = toNodeId, FromPort = fromPort, ToPort = toPort });
        Rebuild();
        Touch("已接上。");
    }

    /// <summary>自检用：走一遍真实的 <see cref="Connect"/>（含去重规则）。</summary>
    public void ConnectForCheck(string fromNodeId, int fromPort, string toNodeId, int toPort) =>
        Connect(fromNodeId, fromPort, toNodeId, toPort);

    /// <summary>自检用：这条线算不算「插槽线」。</summary>
    public bool IsSlotLinkForCheck(ForgeLinkSpec l) => IsSlotLink(l);

    /// <summary>
    /// 按住一个端口圆点开始拉线。出口 = 从它出发；入口 = 反过来从原来那条线的另一端出发（等于把线摘下来重接）。
    /// 两种都进入「拉线中」，之后线跟着鼠标走，松手落在对面圆点上就接上。
    /// </summary>
    private void StartWireDrag(string nodeId, int port, bool isOutput)
    {
        if (_graph is null) return;
        if (isOutput)
        {
            _pending = (nodeId, port);
            ShowPendingWireAtStart();
            Touch($"【{_graph.Node(nodeId)?.Title ?? "模块"}】的出口开始拉线 —— 拖到左边任意一个接入点上松手就接上（松在空白处 = 取消）。");
            return;
        }
        // 接入点：没接过线就当不了起点；接过就把它摘下来，从原来的出口继续拉
        var old = _graph.Incoming(nodeId, port);
        if (old is null)
        {
            Touch("这是接入点（左边那个圆点）：要从**右边**一个出口按住往这边拖。");
            return;
        }
        _graph.Links.Remove(old);
        _pending = (old.From, old.FromPort);
        Rebuild();                       // 旧线先摘掉，视觉上就是「把线拿在手里」
        ShowPendingWireAtStart();
        Touch("把这条线拖到别的接入点上松手 = 换目标；松在空白处 = 删掉这条线。");
    }

    /// <summary>拉线中的那条线：起点先在出口圆点上，鼠标一动就跟着走。</summary>
    private void ShowPendingWireAtStart()
    {
        RemovePendingLine();
        Point start = _pending is not null && PortCenter(_pending.Value.NodeId, _pending.Value.Port, true, out var s)
            ? s
            : new Point(0, 0);
        _pendingLine = MakePendingWire(start, start);
        _root.Children.Add(_pendingLine);
    }

    /// <summary>拉线中：鼠标移动时线跟着走（入口圆点上的移动事件也转发到这里）。</summary>
    private void WireTo(Point now)
    {
        if (_pendingLine is null || _pending is null) return;
        Point start = PortCenter(_pending.Value.NodeId, _pending.Value.Port, true, out var s) ? s : new Point(_pendingLine.X1, _pendingLine.Y1);
        _pendingLine.X1 = start.X;
        _pendingLine.Y1 = start.Y;
        _pendingLine.X2 = now.X;
        _pendingLine.Y2 = now.Y;
    }

    /// <summary>松手：落在接入点（或【效果】那种「来源插槽」）上就接上，落在别处就取消。</summary>
    private void FinishWireDrag(DependencyObject? at)
    {
        if (_pending is null) return;
        var from = _pending.Value;
        _pending = null;
        RemovePendingLine();

        var to = PortAt(at);
        if (to is null)
        {
            DrawWires();
            Touch("松在空白处了，没接上。");
            return;
        }
        Connect(from.NodeId, from.Port, to.Value.NodeId, to.Value.Port);
    }

    /// <summary>鼠标底下是不是一个可以当终点的端口（接入点，或【效果】那种来源插槽）。</summary>
    private (string NodeId, int Port)? PortAt(DependencyObject? at)
    {
        var hit = FindTaggedAncestor(at);
        if (hit is null || PortFromTag(hit.Tag) is not { } t) return null;
        if (!t.IsOutput) return (t.NodeId, t.Port);
        // 出口里只有「来源插槽」（【效果】的数值 / 指向对象）能当终点
        var slot = _graph?.Node(t.NodeId)?.OutputPort(t.Port);
        return slot is not null && !string.Equals(slot.Key, "In", StringComparison.Ordinal) ? (t.NodeId, t.Port) : null;
    }

    /// <summary>鼠标底下那个端口圆点（从事件源顺视觉树找带「模块|口|进出」标记的元素）。</summary>
    private static FrameworkElement? FindTaggedAncestor(DependencyObject? src)
    {
        for (DependencyObject? cur = src; cur is not null; cur = VisualTreeHelper.GetParent(cur))
            if (cur is FrameworkElement fe && PortFromTag(fe.Tag) is not null) return fe;
        return null;
    }

    /// <summary>点了一个端口（不拖动）：出口 = 开始拉线；接入点 = 把刚拉的线接上。</summary>
    private void OnPortClick(string nodeId, int port, bool isOutput)
    {
        if (_graph is null) return;
        if (isOutput)
        {
            _pending = (nodeId, port);
            ShowPendingWireAtStart();
            Touch("已从这个出口开始拉线 —— 现在点左边任意一个接入点接上（点空白处取消）。");
            return;
        }
        if (_pending is null)
        {
            Touch("要先点右边一个出口（圆点）开始拉线，再点这个接入点。");
            return;
        }
        var from = _pending.Value;
        _pending = null;
        RemovePendingLine();
        Connect(from.NodeId, from.Port, nodeId, port);
    }

    /// <summary>这条连线行不行（返回 null = 可以；否则是原因）。</summary>
    private string? ConnectionProblem(string fromId, int fromPort, string toId, int toPort)
    {
        if (_graph is null) return "没有图。";
        if (string.Equals(fromId, toId, StringComparison.Ordinal)) return "不能自己连自己。";
        var from = _graph.Node(fromId);
        var to = _graph.Node(toId);
        if (from is null || to is null) return "模块已经不存在了。";
        var outPort = from.OutputPort(fromPort);
        if (outPort is null) return "起点那个不是出口。";
        // 「往里插来源」的插槽口（只有【if】的「条件」）**不能从它出发**往别处拖：
        // 那样存下来的线方向是反的，生成端按「谁接到条件口上」找来源，根本看不到它 ——
        // 用户实测就是这么连的（从条件口拖到【对象】上），结果条件是恒为「不成立」。
        if (outPort.PullsSource)
            return $"「{outPort.Label}」是**往里插一个来源**的口，不能从它出发往别处拖 —— "
                + "请反过来：从【条件】/【且】/【或】/【关系符】的**结果**出口拖到它上面（右→右）。";
        // 终点可以是普通接入点，也可以是带类型的「收来源」的口（【if】的「条件」/【效果】的数值与对象…）。
        // ⚠️ 新版【if】：入口 0 = 接入、**入口 1 = 条件**（条件不再是一个「出口」），所以这里
        //    直接按**入口**取就行（老存档那种 PullsSource 的口也还认，兼容迁移前的图）。
        bool asSlot;
        var slotPort = to.OutputPort(toPort);
        var inPort = to.InputPort(toPort) ?? (slotPort is { PullsSource: true } ? slotPort : null);
        if (inPort is null) return "终点那个不是接入点（右边那些是出口，要接的话得从它出发）。";
        asSlot = inPort.AcceptsKind is not null || slotPort is { PullsSource: true };
        string? ok = outPort.AcceptsKind;
        string? want = inPort.AcceptsKind;
        bool wantBool = string.Equals(want, "Bool", StringComparison.Ordinal);
        // 「条件」口（【if】/【且】/【或】/【关系符】那种收「成立 / 不成立」的口）：
        //   · 拖一个有类型但**不是**布尔的来源进来（【数值】/【状态】…）→ 拦下来，说清会恒为「不成立」；
        //   · 拖一个**执行链**模块进来（【触发时机】/【对象】/【效果】…，出口没类型）→ 也拦下来：
        //     那两个圆点挨着，用户很容易把「接入」错落到「条件」上（用户实测就是这么连的）。
        if (wantBool && ok is not null && !ForgeGraphCatalog.TypesCompatible(ok, want))
            return $"「{inPort.Label}」要接一个能出「成立 / 不成立」的模块：【条件】/【且】/【或】/【关系符】的**结果**出口；"
                + $"【{from.KindDef?.Display ?? from.Kind}】出的是「{outPort.Label}」，接上去这条判断会**恒为「不成立」**（永远走「否」）。"
                + "要判断「某个敌人身上的某个状态大于 N」：①【状态】的出口 → 【关系符】的「左值」；"
                + "②【数值】的出口 → 【关系符】的「右值」；③【关系符】的「结果」出口 → 拖到这里。";
        if (wantBool && ok is null)
            return $"「{inPort.Label}」收的是「成立 / 不成立」（【条件】/【且】/【或】/【关系符】的结果），"
                + $"【{from.KindDef?.Display ?? from.Kind}】是执行链上的模块，它给不出一个条件 —— 接上去这条判断会**恒为「不成立」**。"
                + (to.Kind == "If" ? "如果你是想把这一块接在 if **前面**，请落到上面那个「接入」口上。" : "");
        // 类型对不上时说清楚（数值出口 ↔ 数值入口；布尔出口 → 条件 / 数值 / 数值入口）
        // 注意**联合类型**：【关系符】的左右两个口是 NumberOrBool（大于 / 小于…接数值，非 / 且 / 或接条件），
        // 所以这里不能直接比字符串相等，要问 ForgeGraphCatalog.TypesCompatible。
        if (ok is not null && want is not null && !ForgeGraphCatalog.TypesCompatible(ok, want))
        {
            bool okBool = string.Equals(ok, "Bool", StringComparison.Ordinal);
            bool okNum = string.Equals(ok, "Number", StringComparison.Ordinal);
            bool wantBoolHere = string.Equals(want, "Bool", StringComparison.Ordinal);
            bool wantNum = string.Equals(want, "Number", StringComparison.Ordinal);
            bool okObj = string.Equals(ok, "Object", StringComparison.Ordinal) || string.Equals(ok, "Unit", StringComparison.Ordinal);
            bool wantObj = string.Equals(want, "Object", StringComparison.Ordinal) || string.Equals(want, "Unit", StringComparison.Ordinal);
            if (wantBoolHere)
                return $"这里要接【条件】（【条件】/【且】/【或】/【关系符】），不能接【{outPort.Label}】。";
            if (okBool && !wantBoolHere)
                return $"这里要接【{want}】，不能接条件（条件只能接到 if / 且 / 或 / 关系符 上）。";
            if (okNum && wantBoolHere)
                return "这里要接【条件】（if / 且 / 或），不能接数值。";
            if (!okBool && !okNum && wantNum && !wantObj)
                return $"【{want}】这里要接数值（【数值】/【状态】/【能量】），不能接【{outPort.Label}】。";
            if (okNum && wantObj)
                return "这里要接【对象】模块的出口，不能接数值。";
            if (okObj && wantNum)
                return "这里要接数值（【数值】/【状态】/【能量】），不能接对象。";
        }
        // 链入口（没有标明接受类型的那些）只收链模块：对象 / 效果 / 触发时机 / if / 且 / 或
        // （「从哪取」的口有自己的接受类型 —— 比如【if】的条件口是 Bool —— 所以不会被这条挡住）
        if (inPort.AcceptsKind is null && !asSlot && from.Kind is "Value" or "Power" or "Energy" or "Condition")
            return "这个入口是执行链的入口，接的是【对象】/【效果】这类模块；条件与数值请接到 if / 效果 上。";
        // 【数值】模块的接入点接的是另一块数值来源：只收数值 / 状态 / 能量
        if (string.Equals(to.Kind, "Value", StringComparison.Ordinal)
            && from.Kind is not ("Value" or "Power" or "Energy"))
            return "【数值】模块的接入点只接【数值】/【状态】/【能量】（要在条件里用，请把条件接到【if】上）。";
        return null;
    }

    private bool IsLinkBroken(ForgeLinkSpec l)
    {
        if (_graph is null) return true;
        var from = _graph.Node(l.From);
        var to = _graph.Node(l.To);
        if (from is null || to is null) return true;
        // 出口那头按 OutputPort 找；接入那头按 InputPort 找
        //（老存档【if】的「条件」是 PullsSource 的那种出口口，所以那一头也要认 OutputPort）
        return from.OutputPort(l.FromPort) is null
            || (to.InputPort(l.ToPort) is null && to.OutputPort(l.ToPort) is not { PullsSource: true });
    }

    private void CancelPending()
    {
        _pending = null;
        RemovePendingLine();
        DrawWires();
    }

    private void RemovePendingLine()
    {
        if (_pendingLine is not null)
        {
            _root.Children.Remove(_pendingLine);
            _wireLayer.Children.Remove(_pendingLine);
            _pendingLine = null;
        }
    }

    /// <summary>端口圆点在画布坐标里的中心点。</summary>
    private bool PortCenter(string nodeId, int port, bool isOutput, out Point center)
    {
        center = default;
        if (!_portPos.TryGetValue(Key(nodeId, port, isOutput), out var p)) return false;
        center = p;
        return true;
    }

    private static string Key(string nodeId, int port, bool isOutput) => $"{nodeId}|{port}|{(isOutput ? "out" : "in")}";

    /// <summary>
    /// 这个口的圆点坐标存在「出口那一侧」的键上吗？
    /// 普通接入点 = 左（false）、普通出口 = 右（true）。
    /// ⚠ 老存档的【if】「条件」曾经是一个**出口**口（PullsSource，画在右边），那种口仍按 true 找；
    ///   新版它已经是左边的普通入口（入口 1），所以走 false。
    /// 画圆点、连线、命中判定三处必须都问这一个函数 —— 以前各写各的，
    /// 结果「条件口」的坐标按左边存、连线却按右边找，线画不出来。
    /// </summary>
    private static bool PortStoredAsOutput(ForgePort? port, bool isOutput)
        => port is { PullsSource: true } || isOutput;

    /// <summary>重新算一遍所有端口的坐标，并把线画出来。</summary>
    private void DrawWires()
    {
        if (_graph is null) return;
        RecomputePorts();
        foreach (var child in _wireLayer.Children.OfType<Path>().ToList()) _wireLayer.Children.Remove(child);
        foreach (var l in _graph.Links)
        {
            if (l is null) continue;
            if (!PortCenter(l.From, l.FromPort, true, out var a)) continue;
            if (!PortCenter(l.To, l.ToPort, PortStoredAsOutput(_graph.Node(l.To)?.InputPort(l.ToPort) ?? _graph.Node(l.To)?.OutputPort(l.ToPort), false), out var b)) continue;
            bool bad = IsLinkBroken(l);
            var wire = MakeWire(a, b, bad, interactive: true);
            wire.Tag = l;                       // 右键点它 = 删掉这条线
            _wireLayer.Children.Add(wire);
            // 线细，右键不好点：再垫一条透明粗线专门吃右键
            var fat = MakeWire(a, b, bad, interactive: true, fat: true);
            fat.Tag = l;
            _wireLayer.Children.Add(fat);
        }
    }

    /// <summary>按模块的实际高度把每个端口圆点的坐标算出来（界面上那个圆点的位置就是它）。</summary>
    private void RecomputePorts()
    {
        _portPos.Clear();
        if (_graph is null) return;
        foreach (var node in _graph.Nodes)
        {
            if (node is null) continue;
            var kind = node.KindDef;
            if (kind is null) continue;
            double y = node.Y + HeaderHeight + TopPad;
            int rows = Math.Max(kind.Inputs.Count, kind.Outputs.Count);
            for (int r = 0; r < rows; r++)
            {
                double cy = y + r * (PortRowHeight + 2) + PortRowHeight / 2;
                // 左边那一列（接入点）
                if (kind.InputPort(r) is { } ip)
                    _portPos[Key(node.Id, r, PortStoredAsOutput(ip, false))] = new Point(node.X, cy);
                // 右边那一列（出口；【if】的「条件」口也画在这儿 —— 它在最上面，先判断它）
                if (kind.OutputPort(r) is { } op)
                    _portPos[Key(node.Id, r, PortStoredAsOutput(op, true))] = new Point(node.X + NodeWidth, cy);
            }
        }
    }

    private static Path MakeWire(Point a, Point b, bool bad, bool interactive = false, bool fat = false)
    {
        var fig = new PathFigure { StartPoint = a, IsClosed = false };
        double dx = Math.Max(30, Math.Abs(b.X - a.X) * 0.45);
        fig.Segments.Add(new BezierSegment(new Point(a.X + dx, a.Y), new Point(b.X - dx, b.Y), b, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        if (fat)
        {
            // 「垫」在线底下的透明粗线：只为右键好点，看不见
            return new Path
            {
                Data = geo,
                Stroke = Brushes.Transparent,
                StrokeThickness = 12,
                IsHitTestVisible = true,
                Cursor = Cursors.Hand,
            };
        }
        return new Path
        {
            Data = geo,
            Stroke = bad ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xE0)),
            StrokeThickness = 2,
            StrokeDashArray = bad ? new DoubleCollection { 4, 3 } : null,
            IsHitTestVisible = interactive,
            Cursor = interactive ? Cursors.Hand : Cursors.Arrow,
            ToolTip = interactive ? "右键点这条线 = 删掉它" : null,
        };
    }

    /// <summary>拉线中那条跟着鼠标走的线（直线，两端随时改）。</summary>
    private static Line MakePendingWire(Point a, Point b) => new()
    {
        X1 = a.X,
        Y1 = a.Y,
        X2 = b.X,
        Y2 = b.Y,
        Stroke = new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x40)),
        StrokeThickness = 2,
        StrokeDashArray = new DoubleCollection { 3, 2 },
        IsHitTestVisible = false,
    };

    /// <summary>鼠标底下的那条连线（右键删线用）。</summary>
    private static ForgeLinkSpec? LinkHit(DependencyObject? src)
    {
        for (DependencyObject? cur = src; cur is not null; cur = VisualTreeHelper.GetParent(cur))
            if (cur is FrameworkElement fe && fe.Tag is ForgeLinkSpec l) return l;
        return null;
    }

    /// <summary>删掉一条连线（在它身上右键）。</summary>
    private void RemoveLink(ForgeLinkSpec link)
    {
        if (_graph is null || link is null) return;
        if (!_graph.Links.Remove(link)) return;
        Rebuild();
        Touch("已删掉这条连线。");
    }

    private void DrawBadLink(string fromId, int fromPort, string toId, int toPort)
    {
        if (!PortCenter(fromId, fromPort, true, out var a)) return;
        if (!PortCenter(toId, toPort, false, out var b)) return;
        var w = MakeWire(a, b, bad: true, interactive: false);
        _wireLayer.Children.Add(w);
        // 2 秒后自动消失，免得看久了以为真有这条线
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { timer.Stop(); _wireLayer.Children.Remove(w); };
        timer.Start();
    }

    private void UpdateCanvasSize()
    {
        if (_graph is null) return;
        double w = MinCanvasW, h = MinCanvasH;
        foreach (var n in _graph.Nodes)
        {
            if (n is null) continue;
            w = Math.Max(w, n.X + NodeWidth + 260);
            double body = HeaderHeight + TopPad + Math.Max(n.KindDef?.Inputs.Count ?? 0, n.KindDef?.Outputs.Count ?? 0) * (PortRowHeight + 2)
                          + (n.KindDef?.Fields.Count ?? 0) * 46 + 40;
            h = Math.Max(h, n.Y + body + 120);
        }
        _root.Width = w;
        _root.Height = h;
    }

    /// <summary>把所有模块都框进可见范围（「全部可见」按钮）。</summary>
    private void FitToContent()
    {
        if (_graph is null || _graph.Nodes.Count == 0) return;
        double minX = _graph.Nodes.Min(n => n.X), minY = _graph.Nodes.Min(n => n.Y);
        foreach (var n in _graph.Nodes) { n.X -= Math.Max(0, minX - 20); n.Y -= Math.Max(0, minY - 20); }
        Rebuild();
        Touch("已把所有模块移到可见范围里。");
    }

    // ==================================================================================
    //  状态提示 / 变更通知
    // ==================================================================================

    private void Touch(string text)
    {
        _statusText.Text = text;
        Changed?.Invoke();
    }

    /// <summary>只刷新底部那句提示（Rebuild 之外的地方用）。</summary>
    public void SetHint(string text) => _statusText.Text = text;
}
