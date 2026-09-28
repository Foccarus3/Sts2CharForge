using System.Windows;

namespace Sts2CharForge.App;

/// <summary>
/// 给「效果编辑模板」用的**作用域标记**（附加属性）。
///
/// 为什么需要它：卡牌 / 遗物 / 药水 / 召唤物卡牌四个页面共用同一个
/// <c>EffectCommon</c> 模板（字段完全一样，只是「召唤物卡牌」页的效果种类下拉只该列宠物类效果）。
/// 模板里的 <c>ComboBox</c> 只能靠「往上找最近的 ContentControl 上挂的是什么标记」来区分自己
/// 在哪一页 —— 这样每个 ComboBox 实例的候选列表在**创建时**就定下来了，
/// 不需要一个「当前在哪一页」的全局状态（那种状态一旦不刷新，另一个页面的下拉会被换成宠物列表，
/// 而 TwoWay 的 SelectedValue 在候选里找不到当前值时会写回 null → 静默清掉用户选的效果种类）。
/// </summary>
public static class EffectScope
{
    /// <summary>true = 这份效果栏用在「召唤物卡牌」页（效果种类下拉只列宠物类效果）。</summary>
    public static readonly DependencyProperty Pet = DependencyProperty.RegisterAttached(
        "Pet", typeof(bool), typeof(EffectScope), new PropertyMetadata(false));

    public static bool GetPet(DependencyObject d) => (bool)d.GetValue(Pet);

    public static void SetPet(DependencyObject d, bool value) => d.SetValue(Pet, value);
}
