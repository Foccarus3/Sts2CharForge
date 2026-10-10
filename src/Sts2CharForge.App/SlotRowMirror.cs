namespace Sts2CharForge.App;

using System.Collections.ObjectModel;
using System.Windows.Controls;
using Sts2CharForge.Core.Profile;

/// <summary>
/// 「每块 5 个」列表镜像的**唯一一份实现**：把真条目集合（<c>Profile.Cards</c> / <c>Curses</c> /
/// <c>AncientCards</c>）镜像成「真条目 1..N + 末尾空槽行 1..<c>5×ceil(max(N,1)/5)</c>」，
/// 供三个卡牌列表页（卡牌 / 诅咒卡 / 先古卡）共用。
///
/// <para>历史与先例：遗物页 V0.2.125 先做了这一套（<c>MainWindow.SyncRelicRows</c> +
/// <c>SlotPlaceholder</c> + <c>IRelicSlotRow</c>），药水页 V0.2.127 照抄了一份
/// （<c>MainWindow.SyncPotionRows</c> / <c>PotionRows</c>）。V0.2.140 用户要求**卡牌左侧序号
/// 也完全照遗物页那套**（每 5 个一块、空槽直接显示灰色序号），范围是**三个卡牌列表页** ——
/// 三份复制必然漂，所以这一轮把这份逻辑抽成这个类，三页各 new 一个实例
/// （各自的行集合 / 序号 / 自动类名前缀互不干扰）。遗物 / 药水两页**没动**
/// （它们那一份继续用原样代码，避免改既有行为）。</para>
///
/// <para>⚠ 空槽行只活在这个镜像集合里：真集合（<c>Profile.*</c>）是唯一事实来源，
/// 生成 / 存档 / 回读 / 校验都看不到占位项。</para>
///
/// <para>⚠ <see cref="Rows"/> 必须能经 public 路径绑到（WPF <c>PropertyPath</c> 读不到 internal 成员）——
/// 三个页面属性 <c>CardRows</c> / <c>CurseRows</c> / <c>AncientRows</c> 都是 public，
/// 绑的是同一个集合实例（不是拷贝）。</para>
/// </summary>
/// <typeparam name="T">真条目的类型（三个卡牌列表页都是 <see cref="CardSpec"/>）。</typeparam>
internal sealed class SlotRowMirror<T> where T : class, ISlotRow
{
	/// <summary>每「一块」预留多少个序号（用户要求：一次多生成五个序号）。</summary>
	internal const int SlotsPerBlock = 5;

	private readonly Func<CharacterProfile, ObservableCollection<T>?> _listOf;
	private readonly Action? _onSelectionChanged;

	/// <summary>已经挂过 <c>CollectionChanged</c> 的那份存档（换存档时先摘再重挂，避免重复订阅）。</summary>
	private CharacterProfile? _hookedOn;

	/// <summary>当前这份存档（<see cref="Sync"/> 自愈重挂时更新）。</summary>
	private CharacterProfile? _profile;

	/// <summary>
	/// 重入保护（遗物页 V0.2.125 实测踩到）：<c>Rows.Clear()</c> 会让列表的选中项被 WPF 清掉 →
	/// 触发 <c>SelectionChanged → SyncDetail → 又回到这里</c>。重入时两次调用会各自按**自己那份快照**
	/// 把集合铺一遍，后写的那次会把对方的结果盖掉（表现是「加了第 6 个，列表却还是 5 行」这种差一行的陈旧状态）。
	/// 做法：重入只置一个 dirty 标记，由最外层那次循环重算到稳定为止。
	/// </summary>
	private bool _inSync;

	private bool _dirty;

	/// <summary>
	/// 正在铺集合（<c>Rows.Clear()</c> / 重新 Add 之间）。外面据此判断「这一次选中变化是不是铺集合自己引发的」，
	/// 是的话就别递归回 <c>SyncDetail</c>（遗物 / 药水页都是这个套路，见 <see cref="Sync"/> 的注释）。
	/// </summary>
	internal bool IsSyncing => _inSync;

	/// <summary>上一次发过通知的选中项（避免每跑一遍 SyncDetail 都白发一次通知）。</summary>
	private T? _lastNotified;

	public SlotRowMirror(Func<CharacterProfile, ObservableCollection<T>?> listOf, Action? onSelectionChanged)
	{
		_listOf = listOf;
		_onSelectionChanged = onSelectionChanged;
	}

	/// <summary>列表真正绑的那个集合：真条目（按真集合的顺序）+ 末尾的空槽行。</summary>
	public ObservableCollection<ISlotRow> Rows { get; } = new();

	/// <summary>
	/// 界面上「当前选中的那一个真条目」：选中项是真条目就是它；点中空槽行时**保持上一个**
	/// （点空槽不该把右边详情清空）。详情栏的数据上下文绑它。
	/// </summary>
	public T? UiSelection { get; private set; }

	/// <summary>
	/// 列表当前选中的那一行（含空槽行）。三个页面各自在 <c>SelectionChanged</c> 里把它接上
	/// —— 镜像本身不持有 <c>ListBox</c> 引用（窗口构造时还没有这个控件变量可用）。
	/// </summary>
	internal object? SelectedRow { get; set; }

	/// <summary>
	/// 三页各自的 <c>ListBox</c>（构造时窗口还没建完，所以事后在 <c>SelectionChanged</c> 里挂上）。
	/// 只给 <see cref="ClearSelection"/> 用 —— 互斥时必须把列表选中项也一起清掉。
	/// </summary>
	internal ListBox? View { get; set; }

	/// <summary>
	/// 详情栏 / 「请选择…」提示的显隐判据：列表里有没有选中**一行**（空槽行也算选了 ——
	/// 空槽行照样是列表里的一行，右侧详情不该闪没；真的取消选中时 <see cref="SelectedRow"/> 是 null）。
	/// </summary>
	internal bool HasSelection => SelectedRow is not null;

	/// <summary>序号行数：<c>5 × ceil(max(N,1) / 5)</c>（N=0 也显示 1~5，用户要的是「直接显示序号」）。</summary>
	internal static int RowCountOf(int itemCount)
	{
		int n = Math.Max(itemCount, 1);
		return ((n + SlotsPerBlock - 1) / SlotsPerBlock) * SlotsPerBlock;
	}

	/// <summary>
	/// 把真集合铺成「真条目 1..N + 空槽 N+1..5×ceil(N/5)」，并认一遍界面选中的真条目。
	/// 幂等：什么都没变就直接返回（原地不动，保住列表的选中项 / 滚动位置）。
	/// </summary>
	internal void Sync(CharacterProfile profile)
	{
		IList<T>? list = _listOf(profile);
		if (list is null) return;
		// ⚠ 先自愈「挂错存档」：有几处代码（历史自检）是直接给 Profile 字段赋值的，不走属性 setter，
		// 于是 CollectionChanged 还挂在**上一份存档**的集合上 —— 表现是「真集合已经是新的了，
		// 列表里还是旧那几条」（遗物页实测踩到）。这里发现不一致就重挂一次。
		if (!ReferenceEquals(_hookedOn, profile)) Hook(profile);
		if (_inSync)
		{
			_dirty = true;
			return;
		}
		_inSync = true;
		try
		{
			do
			{
				_dirty = false;
				SyncCore(list);
			}
			while (_dirty);
		}
		finally
		{
			_inSync = false;
		}
		RefreshUiSelection(list);
	}

	/// <summary>
	/// 明确「列表这一行都不选了」：把喂进来的选中行清掉、并且**无条件**把 <see cref="UiSelection"/>
	/// 也清成 null。
	///
	/// <para>为什么要单独一个方法：三栏互斥时是把另外两栏的 <c>SelectedItem</c> 置 null ——
	/// 直接赋值若值没变，WPF 不会再触发 <c>SelectionChanged</c>，我们这个 <see cref="SelectedRow"/>
	/// 就还停在旧那一行；而 <see cref="RefreshUiSelection"/> 的默认语义是「它指向的那条还在真集合里就留着」
	/// （这是为了「点空槽保持上一张」），于是旧选中会被留住。</para>
	/// </summary>
	internal void ClearSelection()
	{
		// 两边一起清：光清镜像的话，紧接着的 RefreshUiSelection 会拿「列表还选着那一行」
		// （SelectedRow 还指着它）把 UiSelection 又填回来 —— 三栏互斥就白做了。
		if (View is not null) View.SelectedItem = null;
		SelectedRow = null;
		SetUiSelection(null);
	}

	/// <summary>
	/// 认一遍「界面上当前选中的那一个真条目」（选中项是空槽行 / 已失效时就保持 / 清空）。
	///
	/// <para><paramref name="forceClear"/>：调用方已经明确把列表选中项清成「一行都没选」了
	/// （例：三栏互斥时把另外两栏的 <c>SelectedItem</c> 置 null），这时**不许**再拿
	/// 「它指向的那条还在不在真集合里」当理由把旧选中留住 —— 那种情况必须直接清成 null，
	/// 否则 <c>UiSelection</c> 会指着上一张卡（V0.2.140 实测：加一张状态卡，右侧详情里却是上一张先古卡）。</para>
	/// </summary>
	internal void RefreshUiSelection(IList<T>? list = null, bool forceClear = false)
	{
		list ??= _profile is null ? null : _listOf(_profile);
		if (list is null) return;
		// 判据是 `is T` 而不是 cast：空槽行 SlotPlaceholder 不是 T —— 这一条就是
		// 「空槽不可能变成 UiSelection / 污染 CardSpec 相关逻辑」的保证。
		if (!forceClear && SelectedRow is T sel && list.Contains(sel))
		{
			SetUiSelection(sel);
			return;
		}
		// forceClear：列表已经没选中任何一行 → 直接清空。
		if (forceClear)
		{
			SetUiSelection(null);
			return;
		}
		// 选中的不是真条目（空槽行）或者本来是空选中：UiSelection 只在「它指向的那条还在集合里」时留着，
		// 被删掉 / 换存档就清空 —— 这样「一条都没选」时右侧详情照旧收起来。
		if (UiSelection is not null && !list.Contains(UiSelection)) SetUiSelection(null);
	}

	private void SetUiSelection(T? value)
	{
		UiSelection = value;
		if (!ReferenceEquals(_lastNotified, value))
		{
			_lastNotified = value;
			_onSelectionChanged?.Invoke();
		}
	}

	/// <summary>只挂一次：真集合增删 → 重排序号 + 重铺空槽。</summary>
	private void Hook(CharacterProfile profile)
	{
		Unhook();
		_profile = profile;
		_hookedOn = profile;
		if (_listOf(profile) is { } list) list.CollectionChanged += OnCollectionChanged;
	}

	private void Unhook()
	{
		if (_hookedOn is not null && _listOf(_hookedOn) is { } old) old.CollectionChanged -= OnCollectionChanged;
	}

	private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		if (_profile is { } p) Sync(p);
	}

	private void SyncCore(IList<T> list)
	{
		int total = RowCountOf(list.Count);
		// 真条目那一截按位置对齐；末尾那一截必须是「槽号 = 行号」的空槽。对不上就整体重建。
		bool inPlace = Rows.Count == total;
		if (inPlace)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (!ReferenceEquals(Rows[i], list[i])) { inPlace = false; break; }
			}
		}
		if (inPlace)
		{
			for (int i = list.Count; i < total; i++)
			{
				if (Rows[i] is not SlotPlaceholder ph || ph.Slot != i + 1) { inPlace = false; break; }
			}
		}
		if (inPlace) return;
		Rows.Clear();
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i] is not null) Rows.Add(list[i]);
		}
		// 末尾补齐空槽：序号按「行号」给（1~5 / 1~10 / 1~15…），空槽在模板里显示成灰色序号
		for (int i = list.Count; i < total; i++)
		{
			Rows.Add(new SlotPlaceholder(i + 1));
		}
	}

	/// <summary>自检用：镜像集合里的真条目那一截（末尾的空槽不算）。</summary>
	internal List<T> RealRows()
	{
		var found = new List<T>();
		foreach (ISlotRow row in Rows)
		{
			if (row is T t) found.Add(t);
		}
		return found;
	}

	/// <summary>自检用：镜像集合里的空槽行（按顺序）。</summary>
	internal List<SlotPlaceholder> PlaceholderRows()
	{
		var found = new List<SlotPlaceholder>();
		foreach (ISlotRow row in Rows)
		{
			if (row is SlotPlaceholder ph) found.Add(ph);
		}
		return found;
	}
}
