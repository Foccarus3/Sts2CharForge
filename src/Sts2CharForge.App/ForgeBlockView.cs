using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.App;

/// <summary>
/// 「锻造」页的**积木编辑器**（类 Scratch 的堆叠块，可拖、可加、可删）。
///
/// 数据流：窗口给一块 <see cref="ForgeGraphSpec"/> → 这里 <see cref="ForgeBlockTree.Build"/> 读成执行树 →
/// 拖动只改这棵树 → 松手时 <see cref="ForgeBlockTree.WriteBack"/> 写回图的 Links → 触发 <see cref="Changed"/>，
/// 窗口收到后标脏 / 刷新。
///
/// 视觉语言（Scratch 那套）：
///   · 帽子块（【触发时机】）= 橙色；
///   · 堆叠块 = 浅灰底 + 左侧色条（颜色沿用节点图里那种模块的 Accent）；
///   · C 形块（【if】/【对象】）= 行尾一个「▸ 里面有缩进的那几块」，内容缩进；
///   · 报告块（【数值】/【条件】）= 淡蓝底 + 圆角大一点；
///   · 块尾一个小凸起（连着的两块之间），示意上下咬合。
/// </summary>
public sealed class ForgeBlockView : Grid
{
    private readonly ScrollViewer _scroll = new();
    private readonly StackPanel _root = new();
    private readonly TextBlock _status = new();
    private readonly Canvas _overlay = new();

    private ForgeGraphSpec? _graph;
    private ForgeBlockTree? _tree;

    // ---- 拖拽状态 ----
    private ForgeBlockTree.TreeBlock? _dragBlock;
    private FrameworkElement? _dragGhost;
    private ForgeBlockTree.TreeBlock? _dropTarget;
    private DropWhere _dropWhere;
    private readonly Dictionary<ForgeBlockTree.TreeBlock, Border> _rows = new();

    /// <summary>树的形状变了（拖拽 / 加块 / 删块）—— 窗口据此标脏并刷新画布。</summary>
    public event Action? Changed;

    public ForgeBlockView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _status.FontSize = 11;
        _status.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(8, 5, 8, 0);
        _status.Text = "积木编辑器：拖块换位置（拖到中间 = 放进里面），块右边 ✚ 加一块、✕ 删一块。";
        SetRow(_status, 0);
        Children.Add(_status);

        // 调色板：点一下就往选中块后面加一块（没选中就加在最后）
        var palette = new WrapPanel { Margin = new Thickness(8, 4, 8, 4) };
        foreach (var (kind, label) in Palette)
        {
            var b = new Button
            {
                Content = label,
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = 11,
                ToolTip = $"加一块【{label}】",
            };
            string k = kind;
            b.Click += (_, _) => AddBlock(k);
            palette.Children.Add(b);
        }
        var paletteBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF4, 0xEA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xD8, 0xC0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = palette,
        };
        SetRow(paletteBox, 1);
        Children.Add(paletteBox);

        var host = new Grid();
        host.Children.Add(_scroll);
        host.Children.Add(_overlay);
        _overlay.IsHitTestVisible = false;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.Content = _root;
        _scroll.Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
        _scroll.Padding = new Thickness(12);
        SetRow(host, 2);
        Children.Add(host);
    }

    /// <summary>调色板：能加的块（触发时机 / 能量除外 —— 那两块是固定的）。</summary>
    // 调色板里**没有【能量】**：它是固定模块（左上角那一块，新布上就有一块），
    // 既不能选中也不能删除 —— 摆一个「加一块」的按钮只会多出一块删不掉的重复块。
    private static readonly (string Kind, string Label)[] Palette =
    {
        ("Effect", "效果"), ("Unit", "对象"), ("Value", "数值"), ("Condition", "条件"),
        ("If", "if"), ("And", "且"), ("Or", "或"), ("Power", "状态"),
    };

    /// <summary>自检用：画出来多少块。</summary>
    public int DrawnBlockCount { get; private set; }

    /// <summary>自检用：状态行那句话。</summary>
    public string StatusText => _status.Text;

    /// <summary>自检用：当前树。</summary>
    public ForgeBlockTree? Tree => _tree;

    /// <summary>自检用：拖动一块到另一块（<paramref name="where"/> = 前 / 里 / 后）。返回是否真的动了。</summary>
    public bool DragForCheck(ForgeBlockTree.TreeBlock moving, ForgeBlockTree.TreeBlock target, DropWhere where)
    {
        if (_tree is null) return false;
        bool ok = _tree.Move(moving, target, where);
        if (ok) { WriteBackAndRaise(); Reload(); }
        return ok;
    }

    /// <summary>自检用：按「第 n 块」（深度优先）拿树里的块。</summary>
    public ForgeBlockTree.TreeBlock? BlockAt(int index)
    {
        var all = _tree?.All().ToList();
        return all is not null && index >= 0 && index < all.Count ? all[index] : null;
    }

    /// <summary>把一块布装进编辑器（首次 / 换物品 / 换分页时调用）。</summary>
    public void Show(ForgeGraphSpec? graph)
    {
        _graph = graph;
        _tree = ForgeBlockTree.Build(graph);
        Reload();
    }

    /// <summary>按当前树重画（不动数据）。</summary>
    private void Reload()
    {
        _root.Children.Clear();
        _rows.Clear();
        if (_tree is null || (_tree.Docs.Count == 0 && _tree.Slots.Count == 0))
        {
            DrawnBlockCount = 0;
            _root.Children.Add(new TextBlock
            {
                Text = "（这块布还是空的：点上面调色板里的【效果】加一块，或者切回「节点图」拖一个【触发时机】出来）",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 13,
            });
            _status.Text = "积木编辑器：当前这块布是空的。";
            return;
        }

        foreach (var doc in _tree.Docs)
        {
            AddRow(doc.EntryBlock, 0, "");
            foreach (var b in doc.Children) AddRows(b, 1, "");
        }
        DrawnBlockCount = _rows.Count;
        int maxIndent = _tree.Docs.SelectMany(d => d.Children.Select(c => Depth(c))).DefaultIfEmpty(0).Max();
        _status.Text = $"积木编辑器：{DrawnBlockCount} 块，最深嵌套 {maxIndent} 层。"
            + "拖动块换位置（拖到别的块中间 = 放进它里面）；✚ 在它后面加一块，✕ 删掉它和它的内容。";
    }

    private static int Depth(ForgeBlockTree.TreeBlock b) =>
        b.Children.Count == 0 ? 1 : 1 + b.Children.Max(Depth);

    private void AddRows(ForgeBlockTree.TreeBlock b, int indent, string branch)
    {
        AddRow(b, indent, branch);
        var kids = b.Children.ToList();
        if (b.Kind == "If")
        {
            // 【if】的「条件」那条链挂在 Slots 里（不在 Children 里，因为它接的是**入口**、而下标
            // 和「否」那个**出口**撞车）。不画出来用户就看不见「拿什么在判断」—— 所以先画它。
            if (_tree is not null && _tree.Slots.TryGetValue(b.Node.Id, out var slot))
            {
                _root.Children.Add(BranchHeader("条件", indent + 1));
                AddRows(slot, indent + 2, "条件");
            }
            // 顺序和写回时一致：先「是 / 否」两支（新版出口 0 = 是、1 = 否），再画收口
            foreach (var port in new[] { 0, 1 })
            {
                var inPort = kids.Where(k => k.ParentPort == port).ToList();
                if (inPort.Count == 0) continue;
                string label = port == 0 ? "是" : "否";
                _root.Children.Add(BranchHeader(label, indent + 1));
                foreach (var k in inPort) AddRows(k, indent + 2, label);
            }
            _root.Children.Add(EndMark(indent));
            return;   // 【if】的 Children 全是分支（0 = 是 / 1 = 否），没有「同层级接在后面」的链
        }
        // 别的块：取值口的子块（【效果】的数值 / 指向对象…）标一下口名，执行链直接缩进一层
        foreach (var k in kids)
        {
            string label = branch;
            if (k.IsSlotChild)
            {
                // 插在**入口**里的块：口名要从入口那一侧找（【效果】的「数值」/「指向对象」）
                label = b.Node.InputPort(k.ParentPort)?.Label ?? "槽";
                _root.Children.Add(BranchHeader(label, indent + 1));
                AddRows(k, indent + 2, label);
                continue;
            }
            var port = k.ParentPort > 0 ? b.Node.OutputPort(k.ParentPort) : null;
            if (port is { HasLabel: true }) label = port.Label;
            else if (port is not null && !port.HasLabel) label = "槽";
            if (k.ParentPort > 0) _root.Children.Add(BranchHeader(label, indent + 1));
            AddRows(k, indent + 1, label);
        }
    }

    private FrameworkElement BranchHeader(string label, int indent) => new TextBlock
    {
        Text = label + "：",
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x66, 0x00)),
        Margin = new Thickness(12 + indent * 16, 2, 12, 1),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private FrameworkElement EndMark(int indent) => new TextBlock
    {
        Text = "（if 到这里结束）",
        FontSize = 10,
        Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
        Margin = new Thickness(12 + indent * 16, 1, 12, 2),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    // ==================================================================================
    //  画一块
    // ==================================================================================
    private void AddRow(ForgeBlockTree.TreeBlock b, int indent, string branch)
    {
        var node = b.Node;
        var def = node.KindDef;
        bool isReport = node.Kind is "Value" or "Power" or "Energy" or "Condition" or "And" or "Or";
        bool isContainer = node.Kind is "If" or "Unit";
        Color accent = node.Kind switch
        {
            "Trigger" => Color.FromRgb(0xE0, 0x8C, 0x00),
            "If" => Color.FromRgb(0x2A, 0x6F, 0xB5),
            "Unit" => Color.FromRgb(0x7A, 0x5A, 0xA5),
            "Value" or "Power" or "Energy" => Color.FromRgb(0x2F, 0x6F, 0xB0),
            "Condition" or "And" or "Or" => Color.FromRgb(0x8A, 0x6D, 0x00),
            _ => SafeColor(def?.Accent ?? "#7A2E2E"),
        };

        var row = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var body = new Border
        {
            Background = new SolidColorBrush(isReport ? Color.FromRgb(0xEC, 0xF3, 0xFF) : Color.FromRgb(0xF4, 0xF4, 0xF4)),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(1.6),
            CornerRadius = new CornerRadius(isReport ? 9 : 3),
            Padding = new Thickness(7, 3, 9, 3),
            Margin = new Thickness(0),
            ToolTip = "拖动这一块换位置（拖到别的块中间 = 放进它里面）",
            Cursor = Cursors.SizeAll,
        };
        var pack = new StackPanel { Orientation = Orientation.Horizontal };
        if (node.Kind == "Trigger")
            pack.Children.Add(new TextBlock { Text = "▛", FontSize = 13, Foreground = new SolidColorBrush(accent), Margin = new Thickness(0, 0, 4, 0) });
        pack.Children.Add(new TextBlock { Text = node.Title, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)) });
        string extra = ExtraText(b);
        if (extra.Length > 0)
            pack.Children.Add(new TextBlock { Text = "  " + extra, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), VerticalAlignment = VerticalAlignment.Center });
        if (isContainer)
            pack.Children.Add(new TextBlock { Text = "  ▸ 里面有缩进的那几块", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0xAA, accent.R, accent.G, accent.B)) });
        body.Child = pack;
        Grid.SetColumn(body, 0);
        row.Children.Add(body);

        // 行尾两个小按钮：✚ 在它后面加一块（弹一个小菜单挑种类）、✕ 删掉它
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var add = new Button { Content = "✚", Width = 20, Height = 18, Padding = new Thickness(0), FontSize = 11, ToolTip = "在这块后面 / 里面加一块" };
        add.Click += (_, _) => { _selected = b; ShowKindMenu(add); };
        var del = new Button { Content = "✕", Width = 20, Height = 18, Padding = new Thickness(0), FontSize = 11, ToolTip = "删掉这一块（它的内容也一起没）" };
        del.Click += (_, _) => DeleteBlock(b);
        tools.Children.Add(add);
        tools.Children.Add(del);
        Grid.SetColumn(tools, 1);
        row.Children.Add(tools);

        var host = new Border
        {
            Child = row,
            Margin = new Thickness(12 + indent * 16, 2, 12, 2),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            // 缩进参考线（在谁里面一眼看出来）
            BorderBrush = indent > 0 ? new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B)) : Brushes.Transparent,
            BorderThickness = indent > 0 ? new Thickness(2, 0, 0, 0) : new Thickness(0),
        };
        if (branch.Length > 0 && indent > 0 && node.Kind != "Trigger")
            host.Padding = new Thickness(0);

        // 拖动只挂在「主体」上（按钮不受影响）
        body.Tag = b;
        body.MouseLeftButtonDown += OnBodyMouseDown;
        body.MouseMove += OnBodyMouseMove;
        body.MouseLeftButtonUp += OnBodyMouseUp;

        _rows[b] = body;
        _root.Children.Add(host);
    }

    /// <summary>块右边那串「实际配了什么」。</summary>
    private string ExtraText(ForgeBlockTree.TreeBlock b)
    {
        var node = b.Node;
        var parts = new List<string>();
        if (node.Kind == "Condition" && _tree is not null)
        {
            // 条件块在积木里是内联的：直接给一句中文条件
            return Expr(node);
        }
        var def = node.KindDef;
        if (def is null) return "";
        foreach (var f in def.Fields)
        {
            if (f.Type == ForgeFieldType.Check) { if (node.GetFlag(f.Key)) parts.Add(f.Label); continue; }
            string v = node.Get(f.Key).Trim();
            if (v.Length == 0 || string.Equals(f.Key, "Effect", StringComparison.Ordinal)) continue;
            if (f.Options is { Count: > 0 })
            {
                var opt = f.Options.FirstOrDefault(o => string.Equals(o.Id, v, StringComparison.Ordinal));
                if (opt is null || opt.Display.StartsWith("不限", StringComparison.Ordinal)) continue;
                parts.Add($"{f.Label}：{opt.Display}");
            }
            else if (f.Type == ForgeFieldType.Number && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal num))
            {
                if (num == 0m && f.Default == "0") continue;
                parts.Add($"= {num.ToString("0.##", CultureInfo.InvariantCulture)}");
            }
            else if (f.Type is ForgeFieldType.Text or ForgeFieldType.Formula) parts.Add(v);
        }
        return string.Join("  ", parts);
    }

    private static string Expr(ForgeNodeSpec n)
    {
        if (n.Kind == "Value") { string f = n.Get("Formula").Trim(); return "数值 ＝ " + (f.Length == 0 ? "（空）" : f); }
        if (n.Kind == "Condition")
        {
            string id = n.Get("Cond", "None");
            string t = Sts2CharForge.Core.Effects.EffectCatalog.Conditions.FirstOrDefault(o => o.Id == id)?.Display ?? id;
            if (t.StartsWith("无条件", StringComparison.Ordinal)) return "（还没选条件）";
            string amt = n.Get("Amount").Trim();
            if (amt.Length > 0 && t.Contains(" N ", StringComparison.Ordinal)) t = t.Replace(" N ", " " + amt + " ");
            return t;
        }
        if (n.Kind == "Trigger") return ForgeTimings.Zh(n.Get("Timing"));
        if (n.Kind == "Unit")
        {
            string pick = n.Get("Pick", "Self");
            return ForgeGraphCatalog.UnitPicks.FirstOrDefault(o => o.Id == pick)?.Display ?? pick;
        }
        return n.Title;
    }

    // ==================================================================================
    //  拖拽
    // ==================================================================================
    private void OnBodyMouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ForgeBlockTree.TreeBlock b) return;
        _selected = b;
        _dragStart = e.GetPosition(this);
        _dragBlock = b;
        e.Handled = true;
    }

    private Point _dragStart;
    private ForgeBlockTree.TreeBlock? _selected;

    private void OnBodyMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragBlock is null || e.LeftButton != MouseButtonState.Pressed) return;
        Point now = e.GetPosition(this);
        if (Math.Abs(now.Y - _dragStart.Y) < 6 && Math.Abs(now.X - _dragStart.X) < 6) return;

        if (_dragGhost is null)
        {
            _dragGhost = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x6F, 0xB5)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7, 3, 9, 3),
                Child = new TextBlock { Text = _dragBlock.Node.Title, FontSize = 12, FontWeight = FontWeights.Bold },
            };
            _overlay.Children.Add(_dragGhost);
        }
        Canvas.SetLeft(_dragGhost, now.X + 12);
        Canvas.SetTop(_dragGhost, now.Y + 6);
        HighlightDrop(now);
    }

    private void OnBodyMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragBlock is null) return;
        var moving = _dragBlock;
        var target = _dropTarget;
        DropWhere where = _dropWhere;
        _dragBlock = null;
        _dropTarget = null;
        if (_dragGhost is not null) { _overlay.Children.Remove(_dragGhost); _dragGhost = null; }
        ClearHighlights();
        if (target is not null && (where != DropWhere.Into || ForgeBlockTree.CanHoldChildren(target)))
        {
            if (_tree!.Move(moving, target, where)) { WriteBackAndRaise(); Reload(); }
            else if (_tree.LastRefusal is { Length: > 0 } why) _status.Text = "没放过去：" + why;
        }
    }

    private void HighlightDrop(Point p)
    {
        ClearHighlights();
        ForgeBlockTree.TreeBlock? best = null;
        DropWhere bestWhere = DropWhere.After;
        double bestDist = double.MaxValue;
        foreach (var (block, body) in _rows)
        {
            if (_dragBlock is not null && (ReferenceEquals(block, _dragBlock) || ForgeBlockTree.IsAncestor(_dragBlock, block))) continue;
            Point tl = body.TranslatePoint(new Point(0, 0), this);
            double top = tl.Y, h = Math.Max(1, body.ActualHeight);
            double dist = Math.Abs(p.Y - (top + h / 2));
            if (dist >= bestDist) continue;
            bestDist = dist;
            best = block;
            double rel = (p.Y - top) / h;
            bestWhere = rel < 0.3 ? DropWhere.Before
                : rel > 0.7 ? DropWhere.After
                : ForgeBlockTree.CanHoldChildren(block) ? DropWhere.Into : DropWhere.After;
        }
        if (best is null) return;
        _dropTarget = best;
        _dropWhere = bestWhere;
        if (_rows.TryGetValue(best, out var view))
            view.BorderBrush = new SolidColorBrush(bestWhere == DropWhere.Into
                ? Color.FromRgb(0x1B, 0x9E, 0x4B) : Color.FromRgb(0xE0, 0x8C, 0x00));
    }

    private void ClearHighlights()
    {
        foreach (var (block, body) in _rows)
        {
            var def = block.Node.KindDef;
            Color accent = SafeColor(def?.Accent ?? "#7A2E2E");
            body.BorderBrush = new SolidColorBrush(accent);
        }
    }

    // ==================================================================================
    //  加块 / 删块
    // ==================================================================================

    /// <summary>点 ✚ 弹一个小菜单挑块种类（挑完插在选中块后面 / 里面）。</summary>
    private void ShowKindMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (kind, label) in Palette)
        {
            var mi = new MenuItem { Header = label };
            string k = kind;
            mi.Click += (_, _) => AddBlock(k);
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    private void AddBlock(string kind)
    {
        if (_graph is null) return;
        // 【能量】是固定模块（左上角），布上本来就有一块 —— 不许再加（多出来的那块还删不掉）
        if (string.Equals(kind, "Energy", StringComparison.Ordinal))
        {
            _status.Text = "【能量】是固定模块（左上角那一块）：布上本来就有一块，不用再加，也不能删。";
            return;
        }
        var node = new ForgeNodeSpec { Kind = kind };
        Defaults(node, kind);

        // ⚠ 顺序要紧：**先**照着当前这棵树决定插哪儿，再把节点塞进图 ——
        //   反过来（先塞节点再找位置）会让节点对不上树，写回时就成「没接上的模块」了。
        //   插入规则和拖动完全一样（走 ForgeBlockTree.InsertAfter）：
        //   同一条执行链上的两块是**嵌着的**，插在后面要插进它自己的 Children[0]，不能并排。
        var block = new ForgeBlockTree.TreeBlock { Node = node, ParentPort = 0 };
        ForgeBlockTree.TreeBlock? anchor = _selected;
        if (anchor is not null && _tree is not null && _tree.Locate(anchor).Index >= 0)
        {
            ForgeBlockTree.InsertAfter(anchor, block);
        }
        else
        {
            // 没有选中 / 还没有任何文档：补一个【触发时机】当起点（默认「卡牌被打出时」）
            if (_tree is null || _tree.Docs.Count == 0)
            {
                var trig = new ForgeNodeSpec { Kind = "Trigger" };
                trig.SetField("Timing", ForgeTimings.CardPlayed);
                _graph.Nodes.Add(trig);
                _tree = ForgeBlockTree.Build(_graph);
            }
            _tree.Docs.First().Children.Add(block);
        }
        _graph.Nodes.Add(node);

        _selected = block;
        WriteBackAndRaise();
        Reload();
    }

    /// <summary>
    /// 自检用：加一块 / 删一块（绕过按钮和菜单）。返回新加的那一块；删块返回被删的那一块。
    /// </summary>
    public ForgeBlockTree.TreeBlock? AddBlockForCheck(string kind, ForgeBlockTree.TreeBlock? after)
    {
        _selected = after;
        AddBlock(kind);
        return _selected;
    }

    /// <summary>自检用：删掉一块。</summary>
    public void DeleteBlockForCheck(ForgeBlockTree.TreeBlock block) => DeleteBlock(block);

    /// <summary>自检用：当前这块布里的节点数（改完必须和画布上的数据一模一样）。</summary>
    public int GraphNodeCount => _graph?.Nodes.Count ?? 0;

    /// <summary>新块的默认字段（尽量给个能用的起点，用户再去节点图 / 效果栏细化）。</summary>
    private static void Defaults(ForgeNodeSpec n, string kind)
    {
        switch (kind)
        {
            case "Effect":
                n.SetField("Effect", "Damage");
                n.SetField("Amount", "6");
                break;
            case "Unit": n.SetField("Pick", "Self"); break;
            case "Value": n.SetField("Formula", "6"); break;
            case "Condition": n.SetField("Cond", "HandAtLeast"); n.SetField("Amount", "2"); break;
            case "Power": n.SetField("PowerId", "VulnerablePower"); break;
        }
    }

    private void DeleteBlock(ForgeBlockTree.TreeBlock b)
    {
        if (_graph is null || _tree is null) return;
        if (b.Kind == "Trigger")
        {
            _status.Text = "触发时机是这条链的起点，不能删（要清空就删它后面的块）。";
            return;
        }
        // 先清条件槽：槽是挂在 if 名下的（不在任何 Children 列表里）。删的要是 if，槽得跟着走；
        // 删的要是槽里的条件块，整个槽也没意思了 —— 总之槽里那些节点都得从图里删掉，
        // 不然它们留在树上、节点却被删了，再读回来就成悬空模块。
        foreach (string key in _tree.Slots.Keys.ToList())
        {
            var slot = _tree.Slots[key];
            if (Subtree(slot).Any(x => ReferenceEquals(x, b)) || ReferenceEquals(slot, b))
            {
                foreach (var x in Subtree(slot)) _graph.Nodes.Remove(x.Node);
                _tree.Slots.Remove(key);
            }
        }
        // Remove 会把「跟着一起没的」都报回来（自己 + 指定出口那些子树），它们要从图里删掉；
        // 接回父块的那些不在里面，节点得留着
        foreach (var gone in _tree.Remove(b)) _graph.Nodes.Remove(gone.Node);
        WriteBackAndRaise();
        Reload();
    }

    private static IEnumerable<ForgeBlockTree.TreeBlock> Subtree(ForgeBlockTree.TreeBlock b)
    {
        yield return b;
        foreach (var c in b.Children)
            foreach (var x in Subtree(c)) yield return x;
    }

    private void WriteBackAndRaise()
    {
        if (_graph is null || _tree is null) return;
        _tree.WriteBack(_graph);
        Changed?.Invoke();
    }

    private static Color SafeColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Color.FromRgb(0x88, 0x88, 0x88); }
    }
}
