using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Sts2CharForge.App;

/// <summary>
/// 主题化 ComboBox 模板里显示选中项用：
/// 优先按 ComboBox.DisplayMemberPath 取属性；没有写 DisplayMemberPath 时优先 Display，其次 ToString。
/// （WPF 默认模板会自动处理 DisplayMemberPath，自定义模板必须自己补这一步，否则会显示
///  “EffectKindOption { Kind = ... }” 这种对象 ToString。）
/// </summary>
public sealed class ComboDisplayConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        object? item = values.Length > 0 ? values[0] : null;
        string path = values.Length > 1 ? values[1] as string ?? "" : "";
        if (item is null) return "";
        if (!string.IsNullOrWhiteSpace(path))
        {
            var prop = item.GetType().GetProperty(path);
            if (prop is not null) return prop.GetValue(item)?.ToString() ?? "";
        }
        var display = item.GetType().GetProperty("Display");
        if (display is not null) return display.GetValue(item)?.ToString() ?? "";
        return item.ToString() ?? "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
