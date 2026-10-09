namespace Sts2CharForge.Core.Profile;

/// <summary>
/// 列表里「一行」的公共形状：真条目（<see cref="RelicSpec"/> / <see cref="PotionSpec"/>）和**空槽行**
/// （界面那边的占位项 <c>SlotPlaceholder</c>）都实现它，这样列表行的模板 / 自检里「逐行读序号」的代码
/// 只认这一个接口，不用去 cast 具体类型。
///
/// <para>⚠ 空槽行只存在于**界面镜像集合**里（<c>MainWindow.RelicRows</c> / <c>MainWindow.PotionRows</c>），
/// <c>Profile.Relics</c> / <c>Profile.Potions</c> 里永远只有真条目 —— 生成 / 存档 / 回读 / 校验都看不到空槽。</para>
///
/// <para>历史：V0.2.125 遗物页先做了这套（当时叫 <c>IRelicSlotRow</c>）；V0.2.127 药水页照抄，
/// 于是抽成通用接口 —— <see cref="IRelicSlotRow"/> 保留成它的别名，遗物页那边的代码一个字都不用改。</para>
/// </summary>
public interface ISlotRow
{
	/// <summary>界面用：这一行最左边显示的序号（带点，例 <c>"6."</c>；序号还没算出来时是空串）。</summary>
	string OrdinalText { get; }

	/// <summary>界面用：鼠标停在这一行上时的提示（空槽行给 null，不显示提示）。</summary>
	string? RowTip { get; }

	/// <summary>界面用：这一行是不是「空槽」（只有灰色序号、没有内容、不能被当成真条目选中）。</summary>
	bool IsPlaceholder { get; }
}

/// <summary>
/// <see cref="ISlotRow"/> 的历史名字（V0.2.125 遗物页那版）。**就是为了不改遗物页已有代码**才留着它：
/// 遗物页的集合类型 / 自检里的类型参数都还写着这个名字，语义完全一样。
/// 新代码请直接用 <see cref="ISlotRow"/>。
/// </summary>
public interface IRelicSlotRow : ISlotRow
{
}
