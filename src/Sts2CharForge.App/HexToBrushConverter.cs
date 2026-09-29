using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Sts2CharForge.App;

/// <summary>把 "#RRGGBB" / "RRGGBB" / "RRGGBBAA" 转成色块用的 Brush（非法值显示为斜纹/灰色）。</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly Brush Invalid = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? hex = value as string;
        if (string.IsNullOrWhiteSpace(hex)) return Invalid;
        string s = hex.Trim().TrimStart('#');
        try
        {
            if (s.Length == 6)
                return new SolidColorBrush(Color.FromRgb(
                    System.Convert.ToByte(s.Substring(0, 2), 16),
                    System.Convert.ToByte(s.Substring(2, 2), 16),
                    System.Convert.ToByte(s.Substring(4, 2), 16)));
            if (s.Length == 8)
                return new SolidColorBrush(Color.FromArgb(
                    System.Convert.ToByte(s.Substring(6, 2), 16),
                    System.Convert.ToByte(s.Substring(0, 2), 16),
                    System.Convert.ToByte(s.Substring(2, 2), 16),
                    System.Convert.ToByte(s.Substring(4, 2), 16)));
        }
        catch { /* 非法值 → 灰色 */ }
        return Invalid;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>有内容 → Visible，空/null → Collapsed（字符串看是否空白，其它对象只看是否为 null）。</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 注意：这里要能处理任意对象（比如 ListBox.SelectedItem 是 CardSpec），
        // 不能只写 `value as string` —— 那样选中了卡牌也会被判成空，把属性栏整片收起来。
        bool empty = value is null || (value is string s && string.IsNullOrWhiteSpace(s));
        return empty ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true → Collapsed，false → Visible（用于「检测通过时隐藏细节说明」）。</summary>
public sealed class BoolToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// 没选中东西（null）→ Visible，选中了 → Collapsed。
/// 用于「没选卡牌/遗物/药水时，只显示一句『请选择…』，把属性栏和效果栏收起来」。
/// </summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true → 绿色（这条配置没问题），false → 橙红色（还差点什么）。</summary>
public sealed class OkToBrushConverter : IValueConverter
{
    private static readonly Brush Ok = new SolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x3B));
    private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Ok : Bad;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// 「多选」开关 → ListBox 的选择模式。
///
/// 关（默认）= <b>Extended</b>：**Ctrl / Shift 点选就能多选**（Windows 的标准行为），
///              不按 Ctrl 点第二行仍然只选中一行 —— 用户对「多选列表」的默认预期就是这个。
/// 开 = <b>Multiple</b>：直接点一行就切换它的选中状态（不用按 Ctrl），给不想按 Ctrl 的人用。
///
/// 为什么改：以前关着的时候是 <c>Single</c>，于是「Ctrl/Shift 多选」**完全没反应**，
/// 用户报过两次、方向刚好相反 ——
///   · 第一次：Extended 时用户点两行发现还是只选中一行，觉得"多选没生效" → 改成 Multiple；
///   · 第二次：Single 时用户按 Ctrl/Shift 想多选，发现加不上（用户报「还是只能选一个」）。
/// 现在两头都照顾到：默认就有标准多选，想「点一下就加选」再打开「多选」开关。
/// </summary>
public sealed class MultiSelectModeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? System.Windows.Controls.SelectionMode.Multiple : System.Windows.Controls.SelectionMode.Extended;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
