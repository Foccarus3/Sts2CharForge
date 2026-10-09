namespace Sts2CharForge.Core.Profile;

/// <summary>
/// 遗物页左侧列表里「一行」的公共形状：真遗物（<see cref="RelicSpec"/>）和**空槽行**
/// （界面那边的占位项）都实现它，这样列表行的模板 / 自检里「逐行读序号」的代码只认这一个接口，
/// 不用去 cast 具体类型。
///
/// <para>⚠ 空槽行只存在于**界面镜像集合**里（<c>MainWindow.RelicRows</c>），
/// <c>Profile.Relics</c> 里永远只有真遗物 —— 生成 / 存档 / 回读 / 校验都看不到空槽。</para>
/// </summary>
public interface IRelicSlotRow
{
	/// <summary>界面用：这一行最左边显示的序号（带点，例 <c>"6."</c>；序号还没算出来时是空串）。</summary>
	string OrdinalText { get; }

	/// <summary>界面用：鼠标停在这一行上时的提示（空槽行给 null，不显示提示）。</summary>
	string? RowTip { get; }

	/// <summary>界面用：这一行是不是「空槽」（只有灰色序号、没有遗物内容、不能被当成遗物选中）。</summary>
	bool IsPlaceholder { get; }
}
