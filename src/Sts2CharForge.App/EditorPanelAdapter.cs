using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sts2CharForge.Core.Plugins;

namespace Sts2CharForge.App;

/// <summary>
/// V0.2.388（需求书第 4.2 节）：把插件拿到的受控面板变成**真实 WPF 控件**。
///
/// <para>插件只认 <see cref="IEditorPanel"/> 那几个 Add* 方法；WPF 细节全在这里，
/// 所以插件没法反射主窗口、也碰不到私有控件 —— 这是「受控界面」的实现点。</para>
/// </summary>
public sealed class EditorPanelAdapter : IEditorPanel
{
    private readonly StackPanel _root;

    public EditorPanelAdapter(string pluginId)
    {
        PluginId = pluginId ?? "";
        _root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 2, 0, 2) };
    }

    /// <summary>插件 id（排查用）。</summary>
    public string PluginId { get; }

    /// <summary>渲染用：真正要塞进界面的元素。</summary>
    public FrameworkElement Root => _root;

    public void Clear() => _root.Children.Clear();

    private static StackPanel Row(string? label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        if (!string.IsNullOrEmpty(label))
            row.Children.Add(new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    public IEditorText AddText(string text)
    {
        var tb = new TextBlock { Text = text ?? "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
        _root.Children.Add(tb);
        return new TextAdapter(tb);
    }

    public IEditorTextBox AddTextBox(string label, string value, Action<string>? onChange = null)
    {
        var row = Row(label);
        var box = new TextBox { Width = 320, Text = value ?? "" };
        if (onChange is not null) box.TextChanged += (_, _) => Guard(() => onChange(box.Text));
        row.Children.Add(box);
        _root.Children.Add(row);
        return new TextBoxAdapter(box);
    }

    public IEditorNumberBox AddNumber(string label, decimal value, Action<decimal>? onChange = null)
    {
        var row = Row(label);
        var box = new TextBox { Width = 120, Text = value.ToString(CultureInfo.InvariantCulture) };
        if (onChange is not null)
            box.TextChanged += (_, _) =>
            {
                if (decimal.TryParse(box.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d))
                    Guard(() => onChange(d));
            };
        row.Children.Add(box);
        _root.Children.Add(row);
        return new NumberAdapter(box);
    }

    public IEditorCheckBox AddCheck(string label, bool value, Action<bool>? onChange = null)
    {
        var row = Row(label);
        var cb = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        if (onChange is not null) cb.Click += (_, _) => Guard(() => onChange(cb.IsChecked == true));
        row.Children.Add(cb);
        _root.Children.Add(row);
        return new CheckAdapter(cb);
    }

    public IEditorCombo AddCombo(string label, IReadOnlyList<string> options, string value, Action<string>? onChange = null)
    {
        var row = Row(label);
        var combo = new ComboBox { Width = 260 };
        foreach (string o in options ?? Array.Empty<string>()) combo.Items.Add(o);
        if (!string.IsNullOrEmpty(value) && combo.Items.Contains(value)) combo.SelectedItem = value;
        if (onChange is not null) combo.SelectionChanged += (_, _) => Guard(() => onChange(combo.SelectedItem as string ?? ""));
        row.Children.Add(combo);
        _root.Children.Add(row);
        return new ComboAdapter(combo);
    }

    public IEditorButton AddButton(string text, Action onClick)
    {
        var btn = new Button { Content = text ?? "", MinWidth = 90, Margin = new Thickness(0, 2, 6, 2) };
        btn.Click += (_, _) => Guard(() => onClick());
        _root.Children.Add(btn);
        return new ButtonAdapter(btn);
    }

    /// <summary>V0.2.390：受控列表 —— ListBox + 增删/上下移/复制按钮，选中变化会通知插件。</summary>
    public IEditorList<T> AddList<T>(string label, IReadOnlyList<T> items, Func<T, string> labelOf, Func<T, T>? cloner = null) where T : class
    {
        if (!string.IsNullOrEmpty(label))
            _root.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        var listBox = new ListBox { Width = 220, Height = 200 };
        var side = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(6, 0, 0, 0) };
        host.Children.Add(listBox);
        host.Children.Add(side);
        _root.Children.Add(host);
        var adapter = new ListAdapter<T>(listBox, side, items ?? Array.Empty<T>(), labelOf ?? (x => x?.ToString() ?? ""), cloner, Guard);
        return adapter;
    }

    /// <summary>V0.2.395：文件选择栏（文本框 + 「浏览…」）。插件只拿到用户选的路径。</summary>
    public IEditorFilePicker AddFilePicker(string label, string filter, string value, Action<string>? onChange = null)
    {
        var row = Row(label);
        var box = new TextBox { Width = 320, Text = value ?? "" };
        if (onChange is not null) box.TextChanged += (_, _) => Guard(() => onChange(box.Text));
        var browse = new Button { Content = "浏览…", MinWidth = 72, Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => Guard(() =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = string.IsNullOrWhiteSpace(filter) ? "所有文件|*.*" : filter };
            if (dlg.ShowDialog() == true)
            {
                box.Text = dlg.FileName;
                if (onChange is not null) onChange(dlg.FileName);
            }
        });
        row.Children.Add(box);
        row.Children.Add(browse);
        _root.Children.Add(row);
        return new FilePickerAdapter(box);
    }

    /// <summary>V0.2.395：图片预览（选中的 PNG 直接显示）。</summary>
    public IEditorImagePreview AddImagePreview(string label, int height = 160)
    {
        if (!string.IsNullOrEmpty(label))
            _root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) });
        var img = new Image { Height = height, HorizontalAlignment = HorizontalAlignment.Left };
        var hint = new TextBlock { Text = "（没有图片）", Foreground = Brushes.Gray, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(img);
        row.Children.Add(hint);
        _root.Children.Add(row);
        return new ImagePreviewAdapter(img, hint);
    }

    /// <summary>V0.2.396：音频预览 —— 播放 / 停止（MediaPlayer 出错只提示，不抛）。</summary>
    public IEditorAudioPreview AddAudioPreview(string label)
    {
        var row = Row(label);
        var info = new TextBlock { Text = "（没有音频）", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Width = 260 };
        var play = new Button { Content = "▶ 播放", MinWidth = 72 };
        var stop = new Button { Content = "■ 停止", MinWidth = 72, Margin = new Thickness(6, 0, 0, 0) };
        row.Children.Add(info);
        row.Children.Add(play);
        row.Children.Add(stop);
        _root.Children.Add(row);
        var adapter = new AudioPreviewAdapter(info);
        play.Click += (_, _) => Guard(() => adapter.Play());
        stop.Click += (_, _) => Guard(() => adapter.Stop());
        return adapter;
    }

    /// <summary>V0.2.396：逐帧动画预览 —— 一个文件夹里的图片序列，可翻帧。</summary>
    public IEditorFramePreview AddFramePreview(string label, int height = 160)
    {
        if (!string.IsNullOrEmpty(label))
            _root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) });
        var img = new Image { Height = height, HorizontalAlignment = HorizontalAlignment.Left };
        var info = new TextBlock { Text = "（没有帧）", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(img);
        row.Children.Add(info);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var adapter = new FramePreviewAdapter(img, info);
        var prevBtn = new Button { Content = "◀ 上一帧", MinWidth = 82 };
        var nextBtn = new Button { Content = "下一帧 ▶", MinWidth = 82, Margin = new Thickness(6, 0, 0, 0) };
        prevBtn.Click += (_, _) => Guard(() => adapter.Step(-1));
        nextBtn.Click += (_, _) => Guard(() => adapter.Step(1));
        bar.Children.Add(prevBtn);
        bar.Children.Add(nextBtn);
        _root.Children.Add(row);
        _root.Children.Add(bar);
        return adapter;
    }

    public IEditorPanel AddPanel(string? title = null)
    {
        if (!string.IsNullOrEmpty(title))
            _root.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
        var child = new EditorPanelAdapter(PluginId);
        var border = new Border
        {
            Child = child._root,
            Margin = new Thickness(8, 0, 0, 4),
            BorderThickness = new Thickness(1, 0, 0, 0),
            BorderBrush = Brushes.LightGray,
            Padding = new Thickness(6, 0, 0, 0),
        };
        _root.Children.Add(border);
        return child;
    }

    /// <summary>
    /// V0.2.388：插件回调抛异常时**只在这格提示**，不让主程序崩（需求书 6.3「出错隔离」的第一层）。
    /// 更细的日志由页面宿主收集（见 PluginExtensionPages）。
    /// </summary>
    private static void Guard(Action act)
    {
        try
        {
            act();
        }
        catch (Exception ex)
        {
            try
            {
                MessageBox.Show("插件界面回调出错（已隔离，主程序继续运行）：\n" + ex.GetType().Name + "：" + ex.Message,
                    "插件扩展出错", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch { /* 连弹窗都失败就算了，绝不能把异常抛回主程序 */ }
        }
    }

    /// <summary>V0.2.390：受控列表的实现（ListBox + 侧边按钮）。插件只看到 IEditorList&lt;T&gt;。</summary>
    private sealed class ListAdapter<T> : IEditorList<T> where T : class
    {
        private readonly ListBox _box;
        private readonly Func<T, string> _label;
        private readonly Func<T, T>? _cloner;
        private readonly Action<Action> _guard;
        private readonly List<T> _items;
        private bool _silent;

        public ListAdapter(ListBox box, StackPanel side, IReadOnlyList<T> items, Func<T, string> label,
            Func<T, T>? cloner, Action<Action> guard)
        {
            _box = box; _label = label; _cloner = cloner; _guard = guard;
            _items = new List<T>(items);
            _box.SelectionChanged += (_, _) =>
            {
                if (_silent) return;
                // 插件的事件回调同样要隔离（抛异常不能影响主程序）
                _guard(() => SelectionChanged?.Invoke(Selected));
            };

            void Btn(string text, Action act, bool up = false)
            {
                var b = new Button { Content = text, MinWidth = 76, Margin = new Thickness(0, 0, 0, 3) };
                b.Click += (_, _) => _guard(act);
                side.Children.Add(b);
            }
            Btn("+ 添加", () => { var it = _cloner is not null ? _cloner(CreateBlank()) : CreateBlank(); if (it is not null) Add(it); });
            Btn("✕ 删除", () => { if (Selected is T s) Remove(s); });
            Btn("↑ 上移", () => { if (Selected is T s) MoveUp(s); });
            Btn("↓ 下移", () => { if (Selected is T s) MoveDown(s); });
            Btn("⧉ 复制", () => { if (Selected is T s) Clone(s); });
            Refresh();
        }

        /// <summary>给「+ 添加」用：优先无参构造，没有就克隆一个占位（插件通常会给 cloner）。</summary>
        private static T CreateBlank()
        {
            try { return (T)Activator.CreateInstance(typeof(T), nonPublic: true)!; }
            catch { return null!; }
        }

        public IReadOnlyList<T> Items => _items;

        public T? Selected
        {
            get => _box.SelectedItem is T t ? t : null;
            set
            {
                _silent = true;
                try { _box.SelectedItem = value; } finally { _silent = false; }
                // V0.2.390：代码里改选中项也算「选中变化」（插件常常是代码里 select，不是用户点的）
                _guard(() => SelectionChanged?.Invoke(Selected));
            }
        }

        public event Action<T?>? SelectionChanged;

        private void Sync(T? keep = null)
        {
            object? sel = keep ?? _box.SelectedItem;
            _silent = true;
            try
            {
                _box.Items.Clear();
                foreach (T it in _items) _box.Items.Add(it);
                if (sel is T s2 && _items.Contains(s2)) _box.SelectedItem = s2;
            }
            finally { _silent = false; }
        }

        public void Add(T item)
        {
            if (item is null) return;
            _items.Add(item);
            Sync(item);
            _guard(() => SelectionChanged?.Invoke(Selected));
        }

        public void Remove(T item)
        {
            if (item is null || !_items.Remove(item)) return;
            Sync(_items.Count > 0 ? _items[Math.Min(_items.Count - 1, 0)] : null);
            _guard(() => SelectionChanged?.Invoke(Selected));
        }

        public void MoveUp(T item)
        {
            int at = item is null ? -1 : _items.IndexOf(item);
            if (at <= 0) return;
            (_items[at - 1], _items[at]) = (_items[at], _items[at - 1]);
            Sync(item);
        }

        public void MoveDown(T item)
        {
            int at = item is null ? -1 : _items.IndexOf(item);
            if (at < 0 || at >= _items.Count - 1) return;
            (_items[at + 1], _items[at]) = (_items[at], _items[at + 1]);
            Sync(item);
        }

        public void Clone(T item)
        {
            if (item is null) return;
            T copy;
            if (_cloner is not null) copy = _cloner(item);
            else
            {
                // 没给 cloner：浅拷贝（MemberwiseClone），够用且不会失败
                var m = typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                copy = (T)m!.Invoke(item, null)!;
            }
            int at = _items.IndexOf(item);
            _items.Insert(at + 1, copy);
            Sync(copy);
            _guard(() => SelectionChanged?.Invoke(Selected));
        }

        public void Refresh()
        {
            _silent = true;
            try
            {
                T? keep = Selected;
                _box.Items.Clear();
                foreach (T it in _items) _box.Items.Add(it);
                if (keep is not null && _items.Contains(keep)) _box.SelectedItem = keep;
                else if (_items.Count > 0) _box.SelectedIndex = 0;
            }
            finally { _silent = false; }
        }
    }

    /// <summary>V0.2.395：文件选择栏的适配器。</summary>
    private sealed class FilePickerAdapter : IEditorFilePicker
    {
        private readonly TextBox _tb;
        public FilePickerAdapter(TextBox tb) => _tb = tb;
        public string Value { get => _tb.Text ?? ""; set => _tb.Text = value ?? ""; }
    }

    /// <summary>V0.2.395：图片预览的适配器（坏路径只显示提示，不抛）。</summary>
    /// <summary>V0.2.396：音频预览适配器（MediaPlayer；播不了只提示）。</summary>
    private sealed class AudioPreviewAdapter : IEditorAudioPreview
    {
        private readonly TextBlock _info;
        private System.Windows.Media.MediaPlayer? _player;
        private string _path = "";
        public AudioPreviewAdapter(TextBlock info) => _info = info;
        public string Path
        {
            get => _path;
            set
            {
                _path = value ?? "";
                _info.Text = _path.Length == 0
                    ? "（没有音频）"
                    : (System.IO.File.Exists(_path) ? System.IO.Path.GetFileName(_path) : "（文件不存在：" + System.IO.Path.GetFileName(_path) + "）");
            }
        }
        public void Play()
        {
            if (_path.Length == 0 || !System.IO.File.Exists(_path)) { _info.Text = "（没有可播放的音频）"; return; }
            try
            {
                _player ??= new System.Windows.Media.MediaPlayer();
                _player.Open(new Uri(_path, UriKind.Absolute));
                _player.Play();
                _info.Text = "播放中：" + System.IO.Path.GetFileName(_path);
            }
            catch (Exception ex)
            {
                _info.Text = "（播不了：" + ex.Message + "）";
            }
        }
        public void Stop()
        {
            try { _player?.Stop(); _info.Text = System.IO.Path.GetFileName(_path); } catch { }
        }
    }

    /// <summary>V0.2.396：逐帧动画预览适配器（文件夹里的图片序列，按文件名排序）。</summary>
    private sealed class FramePreviewAdapter : IEditorFramePreview
    {
        private static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
        private readonly Image _img;
        private readonly TextBlock _info;
        private readonly List<string> _frames = new();
        private int _idx;
        public FramePreviewAdapter(Image img, TextBlock info) { _img = img; _info = info; }

        public string Folder
        {
            get => _folder;
            set
            {
                _folder = value ?? "";
                _frames.Clear();
                _idx = 0;
                try
                {
                    if (_folder.Length > 0 && System.IO.Directory.Exists(_folder))
                    {
                        foreach (string f in System.IO.Directory.GetFiles(_folder))
                            if (Exts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())) _frames.Add(f);
                        _frames.Sort(StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch { }
                Show();
            }
        }
        private string _folder = "";

        public int FrameCount { get { _ = 0; return _frames.Count; } }

        public int FrameIndex { get => _idx; set { _idx = Math.Clamp(value, 0, Math.Max(0, _frames.Count - 1)); Show(); } }

        public void Step(int delta) => FrameIndex = _idx + delta;

        // V0.2.400：按 FPS 自动轮播（DispatcherTimer；关掉页面时 Stop 由调用方负责，计时器只持有弱引用语义的自身）
        private System.Windows.Threading.DispatcherTimer? _timer;
        private double _fps = 8;
        public bool IsPlaying => _timer?.IsEnabled == true;

        public double Fps
        {
            get => _fps;
            set
            {
                _fps = Math.Clamp(value <= 0 ? 8 : value, 1, 60);
                if (_timer is not null) _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _fps);
            }
        }

        public void Play()
        {
            if (_frames.Count <= 1) return;
            _timer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / _fps) };
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void Stop()
        {
            try { _timer?.Stop(); } catch { }
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_frames.Count == 0) return;
            _idx = (_idx + 1) % _frames.Count;
            Show();
        }

        private void Show()
        {
            if (_frames.Count == 0)
            {
                _img.Source = null;
                _info.Text = _folder.Length == 0 ? "（没有帧）" : "（这个文件夹里没有图片）";
                return;
            }
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(_frames[_idx], UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                _img.Source = bmp;
                _info.Text = (_idx + 1) + " / " + _frames.Count + "：" + System.IO.Path.GetFileName(_frames[_idx]);
            }
            catch
            {
                _img.Source = null;
                _info.Text = "（这一帧读不出来）";
            }
        }
    }

    private sealed class ImagePreviewAdapter : IEditorImagePreview
    {
        private readonly Image _img;
        private readonly TextBlock _hint;
        private string _path = "";
        public ImagePreviewAdapter(Image img, TextBlock hint) { _img = img; _hint = hint; }
        public string Path
        {
            get => _path;
            set
            {
                _path = value ?? "";
                try
                {
                    if (_path.Length == 0 || !System.IO.File.Exists(_path))
                    {
                        _img.Source = null;
                        _hint.Text = _path.Length == 0 ? "（没有图片）" : "（文件不存在：" + System.IO.Path.GetFileName(_path) + "）";
                        return;
                    }
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(_path, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    _img.Source = bmp;
                    _hint.Text = System.IO.Path.GetFileName(_path);
                }
                catch
                {
                    _img.Source = null;
                    _hint.Text = "（读不出这张图）";
                }
            }
        }
    }

    private sealed class TextAdapter : IEditorText
    {
        private readonly TextBlock _tb;
        public TextAdapter(TextBlock tb) => _tb = tb;
        public string Text { get => _tb.Text; set => _tb.Text = value ?? ""; }
    }

    private sealed class TextBoxAdapter : IEditorTextBox
    {
        private readonly TextBox _tb;
        public TextBoxAdapter(TextBox tb) => _tb = tb;
        public string Text { get => _tb.Text; set => _tb.Text = value ?? ""; }
        public bool ReadOnly { get => _tb.IsReadOnly; set => _tb.IsReadOnly = value; }
    }

    private sealed class NumberAdapter : IEditorNumberBox
    {
        private readonly TextBox _tb;
        public NumberAdapter(TextBox tb) => _tb = tb;
        public decimal Value
        {
            get => decimal.TryParse(_tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d) ? d : 0m;
            set => _tb.Text = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private sealed class CheckAdapter : IEditorCheckBox
    {
        private readonly CheckBox _cb;
        public CheckAdapter(CheckBox cb) => _cb = cb;
        public bool IsChecked { get => _cb.IsChecked == true; set => _cb.IsChecked = value; }
    }

    private sealed class ComboAdapter : IEditorCombo
    {
        private readonly ComboBox _cb;
        public ComboAdapter(ComboBox cb) => _cb = cb;
        public string Value
        {
            get => _cb.SelectedItem as string ?? "";
            set { if (!string.IsNullOrEmpty(value) && _cb.Items.Contains(value)) _cb.SelectedItem = value; }
        }
        public void SetOptions(IReadOnlyList<string> options)
        {
            object? keep = _cb.SelectedItem;
            _cb.Items.Clear();
            foreach (string o in options ?? Array.Empty<string>()) _cb.Items.Add(o);
            if (keep is string s && _cb.Items.Contains(s)) _cb.SelectedItem = s;
        }
    }

    private sealed class ButtonAdapter : IEditorButton
    {
        private readonly Button _b;
        public ButtonAdapter(Button b) => _b = b;
        public string Text { get => _b.Content as string ?? ""; set => _b.Content = value ?? ""; }
        public bool Enabled { get => _b.IsEnabled; set => _b.IsEnabled = value; }
    }
}
