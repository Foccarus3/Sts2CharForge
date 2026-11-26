using System;
using System.Globalization;
using System.Windows.Data;
using Sts2CharForge.Core.Effects;

namespace Sts2CharForge.App;

/// <summary>V0.3.104：「作用对象」下拉的显示名（内置中文 + 插件 display）。</summary>
public sealed class TargetSideNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string id ? EffectCatalog.TargetSideZh(id) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
