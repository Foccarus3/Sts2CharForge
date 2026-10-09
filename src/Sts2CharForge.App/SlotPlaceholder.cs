namespace Sts2CharForge.App;

using Sts2CharForge.Core.Profile;

/// <summary>
/// 列表左侧的**空槽行**（用户要求：序号按「每块 5 个」预留显示）。
///
/// <para>用户原话：「左侧序号直接显示，可以每次多生成五个序号，比如有 3 个选项时，左侧序号显示 1~5，
/// 加第 6 个的时候，显示 6~10.」—— 所以列表里显示的**行数** = <c>5 × ceil(max(N,1) / 5)</c>
/// （N = 当前条目数）：3 个 → 1~5 行（4、5 是空槽）；5 个 → 1~5；6 个 → 1~10（7~10 是空槽）。
/// 序号本身仍然是**位置号**（1..N 连续），空槽只是把**当前这一块凑满**显示，不是块内 1..5 循环。</para>
///
/// <para>⚠ 这只是**界面**的东西：它只出现在 <c>MainWindow.RelicRows</c> / <c>MainWindow.PotionRows</c>
/// 这两个界面镜像集合里，永远不会进 <c>Profile.Relics</c> / <c>Profile.Potions</c>（真集合是唯一事实来源），
/// 也不会被序列化 / 生成 / 回读 / 校验看到。界面上所有既有链路（选中 / 多选 / 全选 / 拖动 / 增删 / 撤回 / 撤销）
/// 都必须跳过它 —— 靠的是 <see cref="ISlotRow"/> 这个标记接口（<c>IsPlaceholder</c>）+
/// <c>SelectedOf&lt;T&gt;</c> / <c>as RelicSpec</c>（<c>as PotionSpec</c>）的类型过滤（它**不是**它们）。</para>
///
/// <para>历史：V0.2.125 遗物页先做了这个类（当时叫 <c>RelicSlotPlaceholder</c>）；
/// V0.2.127 药水页照抄（用户要求第 6 条「药水方面参考遗物的优化方案」）时抽成通用名字
/// —— 两页的空槽行**是同一个类**，就是 <see cref="Slot"/> 那个行号不同。</para>
///
/// <para>⚠ 实现的是 <see cref="IRelicSlotRow"/>（它本身就继承 <see cref="ISlotRow"/>）：
/// 遗物页的镜像集合 <c>MainWindow.RelicRows</c> 声明的类型还是 V0.2.125 留下的
/// <c>ObservableCollection&lt;IRelicSlotRow&gt;</c>（不想为了改一个名字去动那一页的代码），
/// 药水页那份用通用类型 <c>ObservableCollection&lt;ISlotRow&gt;</c>（<c>MainWindow.PotionRows</c>）
/// —— 同一个类两边都塞得进去。</para>
/// </summary>
public sealed class SlotPlaceholder : IRelicSlotRow
{
	/// <summary>这一行显示第几号（= 行号，1 起算，就是它对应的「位置号」）。</summary>
	public int Slot { get; }

	public SlotPlaceholder(int slot)
	{
		Slot = slot;
	}

	/// <summary>显示成「6.」——和真条目的 <c>OrdinalText</c> 格式一模一样（同一列同一个模板）。</summary>
	public string OrdinalText => Slot >= 1 ? Slot + "." : "";

	/// <summary>空槽行不给提示（<c>ToolTip</c> 绑到 null 时 WPF 本来就不显示）。</summary>
	public string? RowTip => null;

	/// <summary>是空槽：只有灰色序号，没有内容，不能被当成真条目选中。</summary>
	public bool IsPlaceholder => true;

	/// <summary>一行里那个「名称」列的文字：空槽是空的（模板里还会把这一列整块收起来）。</summary>
	public string Display => "";

	public override string ToString() => "空槽 " + OrdinalText;
}
