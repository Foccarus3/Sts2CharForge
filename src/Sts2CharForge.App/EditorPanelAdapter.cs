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
