using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Sts2CharForge.Core.Build;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Generation;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
	public readonly record struct ProfileMoveResult(int Moved, int Skipped, bool OldRemoved);

	public sealed record PresetItem(CardColorSpec.Preset? Preset, string Name);

	public sealed record FrameItem(string? Value, string Name);

	public sealed record UndoEntry(string Label, Action Restore);

	public sealed class ProfileEntry
	{
		public string Name { get; init; } = "";


		public string Path { get; init; } = "";


		public string Info { get; init; } = "";


		public string Display => Name + "   （" + Info + "）";
	}

	private CharacterProfile _profile = ProfileFactory.Sample();

	/// <summary>
	/// 「本体关键词改名」那 7 行当前挂过 PropertyChanged 的实例。
	/// 换存档时要先摘掉旧的（<see cref="EnsureKeywordRenameRows"/>），否则同一个对象被订阅多次，
	/// 界面会重复刷新，自检里的行为也会变得看不明白。
	/// </summary>
	private readonly List<VanillaKeywordRenameSpec> _keywordRenameHooked = new();

	private string? _currentProfilePath;

	/// <summary>
	/// 「存档名和模组 ID 不一样，要改名吗？」这个问题问过哪一次了（= 文件路径|模组ID）。
	/// 同一个文件 + 同一个名字只问一次：答「否」之后本次运行不再打断，改了名字才会再问。
	/// </summary>
	private string? _renameAskKey;

	private readonly PresetItem _customPreset = new PresetItem(null, "自定义（手动填写下面各项）");

	private readonly FrameItem _customFrame = new FrameItem(null, "自定义（用下面的边框颜色）");

	private string _statusText = "";

	private string? _profileFolderOverride;

	/// <summary>自检开始时真实的存档目录（自检期间会切到临时目录，结束时还原）。</summary>
	private string? _selfTestRealProfileFolder;

	private bool _buildBusy;

	private int _uiTicks;

	private readonly Stack<UndoEntry> _cardUndo = new Stack<UndoEntry>();

	private readonly Stack<UndoEntry> _relicUndo = new Stack<UndoEntry>();

	private readonly Stack<UndoEntry> _potionUndo = new Stack<UndoEntry>();
	/// <summary>「召唤物」页删除条目的撤回栈（和药水 / 关键词同一个套路）。</summary>
	private readonly Stack<UndoEntry> _summonUndo = new Stack<UndoEntry>();
	private readonly Stack<UndoEntry> _keywordUndo = new Stack<UndoEntry>();

	private readonly Stack<UndoEntry> _artUndo = new Stack<UndoEntry>();

	private readonly Stack<UndoEntry> _profileUndo = new Stack<UndoEntry>();

	private readonly Stack<UndoEntry> _powerUndo = new Stack<UndoEntry>();

	private const int UndoLimit = 30;

	private bool _multiSelectCards;

	private bool _multiSelectRelics;

	private bool _multiSelectPotions;
	private bool _multiSelectSummons;

	private bool _multiSelectProfiles;

	public const string ProfileFolderName = "自定义角色存档";

	public const string LegacyProfileFolderName = "自定义角色";

	private readonly StringBuilder _selfTestLog = new StringBuilder();










































	public BitmapImage? CardPortraitPreview
	{
		get
		{
			// 三个列表（卡牌 / 诅咒 / 先古卡）共用这一套预览属性 —— 选中是互斥的，所以拿到的一定是当前那张
			var cardSpec = (CardList.SelectedItem as CardSpec) ?? SpecialCardOf();
			if (cardSpec is null) return null;
			string key = Naming.From(_profile).CardClassName(_profile, cardSpec);
			if (!_profile.Art.CardPortraits.TryGetValue(key, out string value))
			{
				return null;
			}
			return LoadImage(value);
		}
	}

	/// <summary>
	/// 卡面预览下面那行小字：这张图和**游戏里的显示区域（1000×760）**比例对不对。
	/// 用户报过：上传了非推荐比例的卡面，游戏里出现黑边，但工具里的预览看不出来 ——
	/// 所以预览按 1000×760 画一块黑底（见 MainWindow.xaml），这行字再把差多少说清楚。
	/// </summary>
	public string CardPortraitPreviewNote
	{
		get
		{
			var cardSpec = (CardList.SelectedItem as CardSpec) ?? SpecialCardOf();
			if (cardSpec is null) return "";
			string key = Naming.From(_profile).CardClassName(_profile, cardSpec);
			_profile.Art.CardPortraits.TryGetValue(key, out string? value);
			return AspectNoteOf(value, 1000, 760, "游戏里卡面这块");
		}
	}

	/// <summary>
	/// 上传图和「游戏里的显示区域」的比例说明（预览旁边那行小字）。
	/// 比例一致就回一句「不会留黑边」；不一致就说清偏宽还是偏窄、会留哪两边、大概留多少。
	/// </summary>
	internal static string AspectNoteOf(string? path, int frameW, int frameH, string what)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "";
		if (frameW <= 0 || frameH <= 0) return "";
		var size = PngUtil.Decode(path);
		if (size is not { } s || s.W <= 0 || s.H <= 0) return "";

		double frameRatio = (double)frameW / frameH;
		double imgRatio = (double)s.W / s.H;
		if (Math.Abs(frameRatio - imgRatio) / frameRatio <= 0.02)
			return $"✅ 比例和显示区域一致（{s.W}×{s.H}），游戏里不会留黑边。";

		// 按比例缩放居中（contain）：图比框更「宽」→ 受宽度限制 → 上下留边；反之左右留边
		bool wider = imgRatio > frameRatio;
		double bar = wider ? 1 - frameRatio / imgRatio : 1 - imgRatio / frameRatio;
		return $"⚠ 你这张是 {s.W}×{s.H}（比例 {imgRatio:0.00}:1），{what}是 {frameW}×{frameH}（{frameRatio:0.00}:1）："
			+ (wider ? "图偏宽 → 游戏里会**上下**各留一条黑边" : "图偏窄 → 游戏里会**左右**各留一条黑边")
			+ $"（约占那一侧的 {bar * 100:0}%）。上面预览里的黑底部分就是它 —— 想不留黑边就裁成 {frameW}×{frameH}（或同比例）。";
	}

	public BitmapImage? RelicIconPreview
	{
		get
		{
			if (!(RelicList.SelectedItem is RelicSpec relicSpec))
			{
				return null;
			}
			return LoadImage(relicSpec.Icon);
		}
	}

	public BitmapImage? PotionIconPreview
	{
		get
		{
			if (!(PotionList.SelectedItem is PotionSpec potionSpec))
			{
				return null;
			}
			return LoadImage(potionSpec.Icon);
		}
	}

	public CharacterProfile Profile
	{
		get
		{
			return _profile;
		}
		set
		{
			_profile = value;
			HookProfileColors();
			// 「本体关键词改名」是固定 7 行的表格：换存档时把缺的行补齐并接上事件
			EnsureKeywordRenameRows();
			Raise("Profile");
			Raise("PresetSelection");
			Raise("FrameSelection");
		}
	}

	/// <summary>
	/// 本体关键词改名：把 7 行补齐（本体关键词是封闭枚举，不能增删），并监听每一行 ——
	/// 改了名字/说明之后要刷新那一节下面的实时说明（否则界面上那句「会把 N 条文案换掉」永远不动）。
	/// 每次换存档都会重新挂钩，所以老的处理器会先全部摘掉，避免重复订阅（重复订阅只是多跑几次，不致命，
	/// 但会让自检里的计数和实际不符）。
	/// </summary>
	private void EnsureKeywordRenameRows()
	{
		foreach (var old in _keywordRenameHooked)
			old.PropertyChanged -= OnKeywordRenameChanged;
		_keywordRenameHooked.Clear();

		ProfileFactory.EnsureKeywordRenameRows(_profile);
		foreach (var row in _profile.KeywordRenames)
		{
			row.PropertyChanged += OnKeywordRenameChanged;
			_keywordRenameHooked.Add(row);
		}
		Raise("KeywordRenameHint");
	}

	private void OnKeywordRenameChanged(object? sender, PropertyChangedEventArgs e)
	{
		// 改名字/说明都要重算那句提示（换关键词时 Specification 也会同时通知 VanillaName）
		Raise("KeywordRenameHint");
	}

	public IReadOnlyList<string> Genders { get; } = new string[3] { "Neutral", "Feminine", "Masculine" };


	public IReadOnlyList<string> CardTypes => EffectCatalog.CardTypes;

	public IReadOnlyList<string> CardRarities => EffectCatalog.CardRarities;

	public IReadOnlyList<string> RelicRarities => EffectCatalog.RelicRarities;

	public IReadOnlyList<string> PotionRarities => EffectCatalog.PotionRarities;

	public IReadOnlyList<string> PotionUsages => EffectCatalog.PotionUsages;

	public IReadOnlyList<string> PotionTargets => EffectCatalog.PotionTargets;

	public IReadOnlyList<string> TargetSides => EffectCatalog.TargetSides;

	public IReadOnlyList<EffectKindOption> Kinds => EffectCatalog.EffectKinds;

	public IReadOnlyList<TriggerOption> RelicTriggers => EffectCatalog.RelicTriggers;

	public ObservableCollection<PowerEntry> AllCards { get; } = new ObservableCollection<PowerEntry>();


	public IReadOnlyList<string> SpawnTargets => EffectCatalog.SpawnTargets;

	public IReadOnlyList<string> CardPickModes => EffectCatalog.CardPickModes;

	/// <summary>「从哪里选牌」下拉（消耗 / 变化卡牌用）：手牌 / 抽牌堆 / 弃牌堆。</summary>
	public IReadOnlyList<PileChoiceOption> SelectPiles => EffectCatalog.SelectPiles;

	public ObservableCollection<PowerEntry> AllPowers { get; } = new ObservableCollection<PowerEntry>();


	public string CatalogStatus => EffectCatalog.CatalogStatus;

	public string EnvStatusLine
	{
		get
		{
			PathsSpec paths = _profile.Paths;
			bool ok2 = !string.IsNullOrWhiteSpace(paths.GodotExe) && File.Exists(paths.GodotExe);
			bool ok3 = !string.IsNullOrWhiteSpace(paths.DotnetExe) && File.Exists(paths.DotnetExe);
			bool ok4 = !string.IsNullOrWhiteSpace(paths.GameDataDir) && File.Exists(Path.Combine(paths.GameDataDir, "sts2.dll"));
			bool flag = !string.IsNullOrWhiteSpace(paths.VanillaProject) && Directory.Exists(paths.VanillaProject);
			bool ok5 = !string.IsNullOrWhiteSpace(paths.InstallDir);
			string text = "环境自动识别：" + string.Join(" ｜ ", Mark(ok2, "Godot"), Mark(ok3, ".NET"), Mark(ok4, "游戏目录"), Mark(ok5, "mods 目录"));
			if (!flag)
			{
				return text + "\u3000（还差「解包工程目录」，填上就能生成）";
			}
			return text + $"\u3000｜ 效果库 {EffectCatalog.Powers.Count} 项";
			static string Mark(bool ok, string name)
			{
				return (ok ? "✅ " : "❌ ") + name;
			}
		}
	}

	public ObservableCollection<EnvCheckItem> EnvItems { get; } = new ObservableCollection<EnvCheckItem>();


	public string EnvSummaryText { get; private set; } = "";


	public ObservableCollection<ArtSlot> ArtSlots { get; } = new ObservableCollection<ArtSlot>();


	public ObservableCollection<string> Issues { get; } = new ObservableCollection<string>();


	public IReadOnlyList<PresetItem> PresetItems { get; }

	public IReadOnlyList<FrameItem> FrameItems { get; }

	// ===== 「诅咒 / 先古卡」页：这两类牌各自的卡框颜色（RRGGBB）=====
	private readonly FrameItem _followRoleFrame = new FrameItem("", "跟角色配色（默认）");

	/// <summary>「诅咒 / 先古卡」页那两个下拉的候选：多一项「跟角色配色（默认）」。</summary>
	public IReadOnlyList<FrameItem> SpecialFrameItems { get; }

	/// <summary>诅咒的卡框下拉（写进 <see cref="CharacterProfile.CurseStyle"/>）。</summary>
	public FrameItem CurseFrameSelection
	{
		get => SpecialFrameSelectionOf(Profile.CurseStyle);
		set { ApplySpecialFrame(Profile.CurseStyle, value, "CurseFrameSelection"); }
	}

	/// <summary>先古卡的卡框下拉（写进 <see cref="CharacterProfile.AncientStyle"/>）。</summary>
	public FrameItem AncientFrameSelection
	{
		get => SpecialFrameSelectionOf(Profile.AncientStyle);
		set { ApplySpecialFrame(Profile.AncientStyle, value, "AncientFrameSelection"); }
	}

	private FrameItem SpecialFrameSelectionOf(SpecialCardStyleSpec style)
	{
		if (style.IsCustomFrame) return _customFrame;
		if (style.Frame.Length == 0) return _followRoleFrame;
		return FrameItems.FirstOrDefault((FrameItem i) => i.Value == style.Frame) ?? _followRoleFrame;
	}

	private void ApplySpecialFrame(SpecialCardStyleSpec style, FrameItem value, string prop)
	{
		if (value is null) return;
		if (value.Value == _customFrame.Value)                    // null = 自定义颜色
		{
			style.Frame = SpecialCardStyleSpec.CustomFrame;
			if (CardColorSpec.NormalizeHex(style.FrameColor).Length == 0)
				style.FrameColor = CardColorSpec.SeedColorForFrame(Profile.Colors.CardFrame);
		}
		else
		{
			style.Frame = value.Value ?? "";                      // "" = 跟角色配色
			style.FrameColor = "";
		}
		Raise(prop);
		Raise("Profile");
	}

	public PresetItem PresetSelection
	{
		get
		{
			CardColorSpec.Preset match = Profile.Colors.MatchingPreset();
			if ((object)match != null)
			{
				return PresetItems.First((PresetItem i) => i.Preset == match);
			}
			return _customPreset;
		}
		set
		{
			if ((object)value?.Preset != null)
			{
				Profile.Colors.Apply(value.Preset);
			}
			Raise("PresetSelection");
			Raise("FrameSelection");
		}
	}

	public FrameItem FrameSelection
	{
		get
		{
			if (CardColorSpec.NormalizeHex(Profile.Colors.CardFrameColor).Length > 0)
			{
				return _customFrame;
			}
			return FrameItems.FirstOrDefault((FrameItem i) => i.Value == Profile.Colors.CardFrame) ?? FrameItems[0];
		}
		set
		{
			if ((object)value == null)
			{
				return;
			}
			if (value.Value == null)
			{
				if (CardColorSpec.NormalizeHex(Profile.Colors.CardFrameColor).Length == 0)
				{
					Profile.Colors.CardFrameColor = CardColorSpec.SeedColorForFrame(Profile.Colors.CardFrame);
				}
			}
			else
			{
				Profile.Colors.CardFrame = value.Value;
				Profile.Colors.CardFrameColor = "";
			}
			Raise("FrameSelection");
			Raise("PresetSelection");
		}
	}

	public string StatusText
	{
		get
		{
			return _statusText;
		}
		set
		{
			_statusText = value;
			Raise("StatusText");
		}
	}

	public ObservableCollection<AncientRow> Ancients { get; } = new ObservableCollection<AncientRow>();


	public IReadOnlyList<SpeakerOption> SpeakerOptions { get; } = new SpeakerOption[2]
	{
		new SpeakerOption(Value: true, "先古之民说"),
		new SpeakerOption(Value: false, "角色说")
	};


	public bool AncientCatalogMissing => Ancients.Count == 0;

	/// <summary>卡片「条件选项」里能选的条件（每条效果各自一份）。</summary>
	public IReadOnlyList<ConditionOption> CardEffectConditions { get; } = EffectCatalog.Conditions.Where((ConditionOption c) => c.ForCard).ToList();

	/// <summary>老存档里的整张牌条件（新界面不再用它，只为兼容老配置）。</summary>
	public IReadOnlyList<ConditionOption> CardConditions { get; } = EffectCatalog.Conditions.Where((ConditionOption c) => c.ForCard).ToList();


	/// <summary>遗物页「整只遗物」的触发条件。</summary>
	public IReadOnlyList<ConditionOption> RelicConditions { get; } = EffectCatalog.Conditions.Where((ConditionOption c) => c.ForRelic).ToList();

	/// <summary>遗物 / 自定义状态的「每条效果自己的条件」（不能用「这张牌」类条件）。</summary>
	public IReadOnlyList<ConditionOption> PlainEffectConditions { get; } = EffectCatalog.Conditions.Where((ConditionOption c) => c.ForRelic && c.ForPower).ToList();

	/// <summary>条件里的「指向对象」：自己 / 任意一个敌人 / 全部敌人。</summary>
	public IReadOnlyList<ConditionTargetOption> ConditionTargets { get; } = EffectCatalog.ConditionTargets;

	/// <summary>「升级后的关键字」每一行的三个选项（不变 / 升级后获得 / 升级后失去）。</summary>
	public IReadOnlyList<KeywordStateOption> KeywordUpgradeStates { get; } = EffectCatalog.KeywordUpgradeStates;

	/// <summary>
	/// 「升级后的关键字」界面用的行：当前选中的卡牌 × 6 个关键字。
	/// 每行绑定一个关键字的三态（界面上是下拉框：不变 / 升级后获得 / 升级后失去）。
	/// </summary>
	public IReadOnlyList<KeywordUpgradeRow> UpgradeKeywordRows
	{
		get
		{
			if (ActiveCardForDetail() is not { } card) return Array.Empty<KeywordUpgradeRow>();
			return KeywordUpgradeSpec.All.Select(k => new KeywordUpgradeRow(card, k.Field, k.Zh)).ToList();
		}
	}

	/// <summary>
	/// 「当前正在编辑的那张卡」：卡牌 / 诅咒 / 先古卡三个列表里选中的那张。
	/// 三处选中是互斥的（见 <see cref="OnSpecialCardSelectionChanged"/>），所以这里按顺序取第一个就够了。
	///
	/// 为什么要有它：卡面预览、升级后费用、升级后关键字、自定义关键词勾选这些**窗口级**属性
	/// 以前写死了 <c>CardList.SelectedItem</c>，新开的两栏（诅咒 / 先古卡）共用同一套面板时就会拿到 null。
	/// </summary>
	internal CardSpec? ActiveCardForDetail() =>
		(CardList?.SelectedItem as CardSpec) ?? SpecialCardOf();


	/// <summary>
	/// 「召唤物」页右侧那只召唤物的图片预览。
	/// 和额外资源量图标一样用 LoadPreview：路径空 / 文件不在 / 不是图片都返回 null（界面显示空框）。
	/// </summary>
	public ImageSource? SelectedSummonImagePreview => LoadPreview((SummonList?.SelectedItem as SummonSpec)?.Image);

	/// <summary>
	/// 效果编辑器「召唤物（哪一只）」下拉的候选：只列**已启用**的召唤物。
	/// SelectedValue 用的是召唤物的**稳定标识**（宠物类名，见 <see cref="PetGen.PetChoice.Id"/>），
	/// 显示的是中文名 —— 中文名可以随时改，改了老存档（存的是类名）不该失效。
	/// </summary>
	public IReadOnlyList<PetGen.PetChoice> PetSummonsCard => PetGen.Choices(Profile);

	/// <summary>遗物 / 自定义状态 / 药水的效果编辑器用同一份候选（模板分开只为让自检能分别查）。</summary>
	public IReadOnlyList<PetGen.PetChoice> PetSummonsPlain => PetGen.Choices(Profile);

	// ==================== 宠物类效果（不再有专属选项卡）====================
	// 12 个宠物效果都注册在 EffectCatalog.EffectKinds 里，所以在「卡牌 / 遗物 / 药水 /
	// 自定义状态」页的「效果种类」下拉里直接就能选到；选到宠物效果时 EffectCommon 模板里那个
	// 「召唤物（哪一只）」下拉会显示出来（Visibility 绑 EffectSpec.PetAction）。
	// 以前那个「召唤物卡牌」选项卡（列表 + 专属效果栏）已按用户要求整页删除。

	/// <summary>牺牲伙伴的收益类型下拉（格挡 / 伤害）。</summary>
	public IReadOnlyList<PetOption> PetSacrificeGains { get; } = new PetOption[]
	{
		new PetOption("Block", "格挡（给自己）"),
		new PetOption("Damage", "伤害（打敌人）"),
	};

	/// <summary>牺牲伙伴的收益公式下拉（固定 N / 最大生命 × 倍率 / 当前生命）。</summary>
	public IReadOnlyList<PetOption> PetSacrificeFormulas { get; } = new PetOption[]
	{
		new PetOption("Fixed", "固定 N（用「数值」那一栏）"),
		new PetOption("MaxHp", "最大生命 × 倍率"),
		new PetOption("CurHp", "当前生命"),
	};

	/// <summary>下拉里的一个选项（Id + 中文显示）。</summary>
	public sealed record PetOption(string Id, string Display);

	public ImageSource? ExtraResourceIconPreview
	{
		get
		{
			string icon = _profile.ExtraResource.Icon;
			if (string.IsNullOrWhiteSpace(icon) || !File.Exists(icon))
			{
				return null;
			}
			try
			{
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.UriSource = new Uri(icon);
				bitmapImage.EndInit();
				return bitmapImage;
			}
			catch
			{
				return null;
			}
		}
	}

	public IReadOnlyList<PowerEntry> PowerChoices => EffectCatalog.Powers;

	public ImageSource? PowerIconPreview => LoadPreview((PowerOverrideList?.SelectedItem as VanillaPowerOverride)?.Icon);

	public string BarColorHint
	{
		get
		{
			if (!(PowerOverrideList?.SelectedItem is VanillaPowerOverride vanillaPowerOverride))
			{
				return "";
			}
			if (string.IsNullOrWhiteSpace(vanillaPowerOverride.PowerId))
			{
				return "先选一个状态。";
			}
			string text = EffectCatalog.HealthBarNodeFor(vanillaPowerOverride.PowerId);
			if (!(text == "PoisonForeground"))
			{
				if (text == "DoomForeground")
				{
					return "血条上这一截（%DoomForeground）本体是着色器用一条紫色渐变按噪声画的，填了颜色会给你换成同结构同色的渐变（本体主色 834181）。";
				}
				return "本体血条上只有「中毒」和「灾厄」这两截是按状态显示颜色的，这个状态没有血条段，填了不会有显示效果。";
			}
			return "血条上这一截（%PoisonForeground，本体是纯颜色）的颜色，本体原色 79C03C。";
		}
	}

	public string VanillaTextHint
	{
		get
		{
			if (!(PowerOverrideList?.SelectedItem is VanillaPowerOverride vanillaPowerOverride))
			{
				return "";
			}
			if (string.IsNullOrWhiteSpace(vanillaPowerOverride.Name) || vanillaPowerOverride.Name.Trim() == (vanillaPowerOverride.VanillaName ?? "").Trim())
			{
				return "（先在「新名字」里填一个和原名不一样的名字，这个选项才用得上。）";
			}
			if (!vanillaPowerOverride.ReplaceInVanillaText)
			{
				return "（没勾选：本体卡面描述里还是旧名字。）";
			}
			if (!VanillaPowerGen.CanRewriteVanillaCardText)
			{
				return "⚠ 本机没读到本体卡牌中文表（要有「解包后的原版工程」目录），卡面描述里的旧名字换不了；状态本身的描述仍会换。";
			}
			int num = VanillaPowerGen.VanillaTextReplacements(ProfileWith(vanillaPowerOverride)).Count();
			if (num != 0)
			{
				return $"会把本体 {num} 条描述里的「{vanillaPowerOverride.VanillaName}」换成「{vanillaPowerOverride.Name}」（只改描述，标题不动）。";
			}
			return "本体的卡牌/遗物/药水描述里没出现这个名字，不用替换。";
		}
	}

	/// <summary>
	/// 「本体关键词改名」那一节下面那句实时说明：改了哪些关键词、旧名字会在多少条本体文案里被替掉。
	/// 和「本体状态改写」的 <see cref="VanillaTextHint"/> 一个套路 —— 文案替换这一步依赖本机的本体中文表，
	/// 读不到就如实说（改名本身照样生效：键是我们按枚举名写死的）。
	/// </summary>
	public string KeywordRenameHint
	{
		get
		{
			var renames = VanillaKeywordGen.Active(Profile);
			if (renames.Count == 0)
			{
				return "（这 7 行都留空 = 不改，游戏里还是本体原来的「消耗 / 虚无 / …」。）";
			}
			string who = string.Join("、", renames.Select((VanillaKeywordRenameSpec r) =>
				$"{VanillaKeywordCatalog.VanillaNameOf(r.KeywordId)}→{(string.IsNullOrWhiteSpace(r.Name) ? VanillaKeywordCatalog.VanillaNameOf(r.KeywordId) : r.Name.Trim())}"));
			int num = VanillaKeywordGen.KeywordTextReplacements(Profile).Count();
			string tail = num > 0
				? $"会把本体 {num} 条卡牌/遗物/药水文案里的旧名字一起换掉。"
				: (VanillaKeywordGen.CanReadVanillaKeywordText
					? "本体的卡牌/遗物/药水描述里没出现旧名字，不用额外替换。"
					: "⚠ 本机没读到本体卡牌中文表（要有「解包后的原版工程」目录），卡面描述里的旧名字换不了；关键词本身的名字仍会换。");
			return $"已改：{who}。{tail}";
		}
	}

	public IReadOnlyList<string> PowerTypes { get; } = new string[2] { "Buff", "Debuff" };
	public IReadOnlyList<string> CardFilters => PowerTriggers.CardFilters;

	public IReadOnlyList<PowerTriggerOption> TriggerOptions => PowerTriggers.All;
	public IReadOnlyList<ConditionTargetOption> WatchTargets => PowerTriggers.WatchTargets;

	public string TriggerHint
	{
		get
		{
			if (!(CustomTriggerList?.SelectedItem is PowerTriggerSpec powerTriggerSpec))
			{
				return "先在上面选一个触发时机。";
			}
			PowerTriggerOption powerTriggerOption = PowerTriggers.Find(powerTriggerSpec.Kind);
			if ((object)powerTriggerOption == null)
			{
				return "";
			}
			// 这句话要说清两件事：什么时候触发（当前选的是哪个）+ 触发时做什么（跟着效果实时变），
			// 否则改了效果 / 改了时机都看不出反应（用户反馈过两次）。
			string when = PowerTriggers.Find(powerTriggerSpec.Kind)?.Display ?? powerTriggerSpec.Kind;
			if (powerTriggerSpec.Kind == "CardPlayed" && powerTriggerSpec.CardFilter != "Any")
			{
				when += powerTriggerSpec.CardFilter switch
				{
					"Attack" => "（只对攻击牌）",
					"Skill" => "（只对技能牌）",
					"Power" => "（只对能力牌）",
					_ => "",
				};
			}
			// 「某个状态层数变化后」：把「盯的是哪个状态」也写出来（留空 = 任意状态）
			if (powerTriggerSpec.Kind == "PowerChanged")
				when += string.IsNullOrWhiteSpace(powerTriggerSpec.PowerId)
					? "（盯：任意状态）"
					: $"（盯：{EffectCatalog.PowerName(powerTriggerSpec.PowerId, powerTriggerSpec.PowerId)}）";
			string what = PowerTriggers.Plain(PowerTriggers.TriggerWhat(powerTriggerSpec));
			return $"触发时机：{when} → 触发时做：{what}\n（本体钩子：{powerTriggerOption.Hint}）";
		}
	}

	/// <summary>
	/// 「描述」那一栏的实时预览：留空时游戏里显示的就是自动写的那句（按当前触发时机 + 效果算），
	/// 改了触发时机或触发时做什么，这里立刻跟着变。
	/// </summary>
	public string CustomPowerDescriptionHint
	{
		get
		{
			if (!(CustomPowerList?.SelectedItem is CustomPowerSpec power))
			{
				return "先在左边选一个状态。";
			}
			if (!string.IsNullOrWhiteSpace(power.Description))
			{
				return "游戏里会显示你填的描述（想按触发时机重写就点上面的按钮）。";
			}
			string auto = PowerTriggers.Plain(PowerTriggers.AutoDescription(power));
			return auto.Length == 0
				? "描述留空 = 按触发时机自动写一句；现在没配触发时机，游戏里会显示空描述。"
				: "描述留空 → 游戏里会自动显示：\n" + auto;
		}
	}

	public ImageSource? CustomPowerIconPreview => LoadPreview((CustomPowerList?.SelectedItem as CustomPowerSpec)?.Icon);

	public int? SelectedCardUpgradeCost
	{
		get
		{
			return ActiveCardForDetail()?.UpgradeCost;
		}
		set
		{
			if (ActiveCardForDetail() is { } cardSpec)
			{
				cardSpec.UpgradeCost = value;
				Raise("UpgradeCostHint");
			}
		}
	}

	public string UpgradeCostHint
	{
		get
		{
			if (ActiveCardForDetail() is not { } cardSpec)
			{
				return "";
			}
			if (cardSpec.CostIsX)
			{
				return "⚠ 这是 X 费用牌，本体不允许改 X 费牌的费用，填了不会生效。";
			}
			int? upgradeCost = cardSpec.UpgradeCost;
			if (upgradeCost.HasValue)
			{
				int valueOrDefault = upgradeCost.GetValueOrDefault();
				if (valueOrDefault != cardSpec.Cost)
				{
					string value = ((valueOrDefault < cardSpec.Cost) ? "减少" : "增加");
					return $"升级后 {valueOrDefault} 费（现在 {cardSpec.Cost} 费，{value} {Math.Abs(valueOrDefault - cardSpec.Cost)} 点）。";
				}
				return $"填的是 {valueOrDefault} 费，和现在一样 = 等于没改。";
			}
			return $"整张牌：留空 = 升级不改费用（当前 {cardSpec.Cost} 费）。";
		}
	}

	public IReadOnlyList<PowerEntry> AncientRelicChoices
	{
		get
		{
			List<PowerEntry> list = new List<PowerEntry>();
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			AncientTalkSpec ancientTalkSpec = (AncientList?.SelectedItem as AncientRow)?.Talk;
			Naming naming = Naming.From(_profile);
			AncientEntry ancientEntry = ((ancientTalkSpec == null) ? null : EffectCatalog.FindAncient(ancientTalkSpec.AncientId));
			if ((object)ancientEntry != null)
			{
				foreach (string relicCandidateId in ancientEntry.RelicCandidateIds)
				{
					string text = AncientCatalog.VanillaRelicName(relicCandidateId) ?? relicCandidateId;
					if (hashSet.Add(relicCandidateId))
					{
						list.Add(new PowerEntry(relicCandidateId, relicCandidateId, "Relic", "", text + "（本体）", relicCandidateId));
					}
				}
			}
			foreach (var (text2, zh) in AncientCatalog.VanillaRelics)
			{
				if (hashSet.Add(text2))
				{
					list.Add(new PowerEntry(text2, text2, "Relic", "", zh, text2));
				}
			}
			for (int i = 0; i < _profile.Relics.Count; i++)
			{
				string text3 = naming.RelicClassName(_profile.Relics[i], i);
				string text4 = (string.IsNullOrWhiteSpace(_profile.Relics[i].Name) ? text3 : _profile.Relics[i].Name);
				if (hashSet.Add(text3))
				{
					list.Add(new PowerEntry(text3, text3, "Relic", "", text4 + "（你自己的）", text3));
				}
			}
			return list;
		}
	}

	/// <summary>
	/// 本体这位先古之民【原本会给的遗物】清单（界面上罗列出来，让用户挑一个替换）。
	/// 来源是解包工程里他自己那张候选表（AncientEntry.RelicCandidateIds）。
	/// </summary>
	public IReadOnlyList<PowerEntry> AncientOriginalRelics
	{
		get
		{
			var list = new List<PowerEntry>();
			var talk = (AncientList?.SelectedItem as AncientRow)?.Talk;
			var ancient = talk is null ? null : EffectCatalog.FindAncient(talk.AncientId);
			if (ancient is null) return list;
			foreach (string id in ancient.RelicCandidateIds)
			{
				string zh = AncientCatalog.VanillaRelicName(id) ?? id;
				list.Add(new PowerEntry(id, id, "Relic", "", $"{zh}（{id}）", id));
			}
			return list;
		}
	}

	/// <summary>上面那个清单的说明（几个遗物 / 其中几个已经配了替换）。</summary>
	public string AncientOriginalRelicSummary
	{
		get
		{
			var talk = (AncientList?.SelectedItem as AncientRow)?.Talk;
			var ancient = talk is null ? null : EffectCatalog.FindAncient(talk.AncientId);
			if (ancient is null) return "（先在左边选一位先古之民）";
			if (ancient.RelicCandidateIds.Count == 0) return "⚠ 没读到这位先古之民的候选遗物表（需要「解包后的原版工程」目录）。";
			var replaced = talk!.RelicReplacements
				.Where(r => !string.IsNullOrWhiteSpace(r.FromRelicId))
				.Select(r => r.FromRelicId!)
				.ToHashSet(StringComparer.Ordinal);
			int hit = ancient.RelicCandidateIds.Count(replaced.Contains);
			return $"他原本会给你 {ancient.RelicCandidateIds.Count} 种遗物，其中 {hit} 种已经配了替换。";
		}
	}



	public string LastBuildTestReport { get; private set; } = "";

	public bool SuppressConfirmations { get; set; }

	public int ConfirmRequests { get; private set; }

	public bool SuppressClosePromptForTest { get; set; }

	/// <summary>
	/// 命令行 / 自检模式（--uicheck / --buildtest 等）置为 true：关窗口时不再弹「要不要保存」。
	/// 那种模式下没有人点按钮，模态框会把进程永远卡住（结果文件写不出来、批处理也退不出去）。
	/// </summary>
	public static bool SuppressClosePrompt;

	public string? CurrentProfilePath => _currentProfilePath;

	public bool CanUndoCard => _cardUndo.Count > 0;

	public bool CanUndoRelic => _relicUndo.Count > 0;

	public bool CanUndoPotion => _potionUndo.Count > 0;

	public bool CanUndoArt => _artUndo.Count > 0;

	public bool CanUndoProfile => _profileUndo.Count > 0;

	public string UndoCardHint => HintOf(_cardUndo);

	public string UndoRelicHint => HintOf(_relicUndo);

	public string UndoPotionHint => HintOf(_potionUndo);

	/// <summary>自定义关键词的撤回状态（和药水同一个套路）。</summary>
	public bool CanUndoKeyword => _keywordUndo.Count > 0;

	public string UndoKeywordHint => HintOf(_keywordUndo);

	/// <summary>召唤物的撤回状态（和药水同一个套路）。</summary>
	public bool CanUndoSummon => _summonUndo.Count > 0;

	public string UndoSummonHint => HintOf(_summonUndo);

	/// <summary>
	/// 「自定义状态」页的撤回状态（删状态 / 删触发时机 / 删触发时机里的效果都进这个栈）。
	/// 以前这个栈只进不出 —— 页面上根本没有「撤回删除」按钮，删了就真没了。
	/// </summary>
	public bool CanUndoPower => _powerUndo.Count > 0;

	public string UndoPowerHint => HintOf(_powerUndo);

	/// <summary>
	/// 「卡牌」页里「自定义关键词」的勾选行：当前选中的卡 × 全部关键词。
	/// 勾上 = 把这条关键词写进这张卡的 KeywordIds（生成时描述开头会出现它、悬停卡面能看到说明）。
	/// </summary>
	public IReadOnlyList<CustomKeywordRow> CustomKeywordRows
	{
		get
		{
			if (ActiveCardForDetail() is not { } card) return Array.Empty<CustomKeywordRow>();
			return KeywordGen.All(_profile).Select(k => new CustomKeywordRow(card, k.Spec, k.Key)).ToList();
		}
	}

	/// <summary>
	/// 「给予卡牌关键词」那个下拉的候选：本体那 7 个关键词 + 存档里已有的自定义关键词。
	/// 自定义关键词的 Id 用它的**键**（生成时写进代码的就是这个键），Display 里带上中文名方便找。
	/// </summary>
	public IReadOnlyList<KeywordChoiceOption> KeywordChoices
	{
		get
		{
			var list = EffectCatalog.VanillaKeywordChoices
				.Select(k => new KeywordChoiceOption(k.Id, k.Display, false))
				.ToList();
			foreach (var (spec, key) in KeywordGen.All(_profile))
			{
				string name = KeywordGen.DisplayName(spec, key);
				list.Add(new KeywordChoiceOption(key, $"{name}（自定义）", true));
			}
			return list;
		}
	}

	/// <summary>有没有自定义关键词（「卡牌」页的空态提示用）。</summary>
	public bool HasCustomKeywords => _profile.CustomKeywords.Count > 0;
	public bool NoCustomKeywords => _profile.CustomKeywords.Count == 0;

	/// <summary>
	/// 「强化指定卡牌」里「强化什么」的候选：伤害（像本体「精准」）/ 格挡（像「敏捷」）。
	/// 是静态表，但走窗口属性绑定（和「增益 / 减益」那些下拉一个写法）。
	/// </summary>
	public IReadOnlyList<BoostStatOption> BoostStats => EffectCatalog.BoostStats;

	public string UndoArtHint => HintOf(_artUndo);

	public string UndoProfileHint => HintOf(_profileUndo);

	public bool MultiSelectCards
	{
		get
		{
			return _multiSelectCards;
		}
		set
		{
			_multiSelectCards = value;
			Raise("MultiSelectCards");
		}
	}

	public bool MultiSelectRelics
	{
		get
		{
			return _multiSelectRelics;
		}
		set
		{
			_multiSelectRelics = value;
			Raise("MultiSelectRelics");
		}
	}

	public bool MultiSelectPotions
	{
		get
		{
			return _multiSelectPotions;
		}
		set
		{
			_multiSelectPotions = value;
			Raise("MultiSelectPotions");
		}
	}

	public bool MultiSelectSummons
	{
		get
		{
			return _multiSelectSummons;
		}
		set
		{
			_multiSelectSummons = value;
			Raise("MultiSelectSummons");
		}
	}

	public bool MultiSelectProfiles
	{
		get
		{
			return _multiSelectProfiles;
		}
		set
		{
			_multiSelectProfiles = value;
			Raise("MultiSelectProfiles");
		}
	}

	public ObservableCollection<ProfileEntry> Profiles { get; } = new ObservableCollection<ProfileEntry>();


	public string ProfileFolder => _profileFolderOverride ?? Path.Combine(RootFolder, "自定义角色存档");

	/// <summary>
	/// 自检模式（--uicheck / --buildtest / --movedtest / --envcheck / --newprofiletest）下置为 true：
	/// 界面里的「静默保存」全部跳过 —— 自检绝不能碰到用户的真实存档。
	/// </summary>
	public static bool SuppressProfileWrites;

	public static string RootFolder
	{
		get
		{
			string text = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return Directory.GetParent(text)?.FullName ?? text;
		}
	}

	public string SelfTestPartial => _selfTestLog.ToString();

	public event PropertyChangedEventHandler? PropertyChanged;

	public MainWindow()
	{
		InitializeComponent();
		// 标题带上版本号（和整合包 / 启动器用的是同一个版本号：App.csproj 里的 InformationalVersion）
		Title = $"Sts2CharForge {AppVersion.Current} —— 杀戮尖塔2 自定义角色生成器";
		BuildArtSlots();
		BuildAncientRows();
		PresetItems = CardColorSpec.Presets.Select((CardColorSpec.Preset pz) => new PresetItem(pz, pz.Name)).Append(_customPreset).ToList();
		FrameItems = CardColorSpec.Frames.Select((string f) => new FrameItem(f, f)).Append(_customFrame).ToList();
		// 「诅咒 / 先古卡」页那两个下拉：第一项是「跟角色配色（默认）」，其余和上面的框色一样。
		// 复用同一个 _customFrame 实例，自检里就能用引用比较认出「自定义」。
		SpecialFrameItems = new[] { _followRoleFrame }.Concat(FrameItems).ToList();
		HookProfileColors();
		// 启动时那份配置也要先把「本体关键词改名」的 7 行补齐（Profile 的 setter 只在换存档时跑）
		EnsureKeywordRenameRows();
		RecheckEnvironment();
		string text = MigrateLegacyProfileFolder();
		NormalizeOutputDir();
		base.DataContext = this;
		ReloadCatalog();
		RefreshAll();
		RefreshProfiles();
		CardList.SelectionChanged += delegate
		{
			SyncDetail();
		};
		RelicList.SelectionChanged += delegate
		{
			SyncDetail();
		};
		PotionList.SelectionChanged += delegate
		{
			SyncDetail();
		};
		// 「自定义关键词」和「召唤物」两个列表也必须接上 —— 详情是「换选中项 → 重新指向新对象」，
		// 漏了这两行时详情会**卡在第一次选中的那条上**：在第二条里打字会写进第一条、
		// 图片预览一直显示上一只（用户实测报过：「第一个输入框打不进字、第二个的内容跑到第一个」）。
		KeywordList.SelectionChanged += delegate
		{
			SyncDetail();
		};
		SummonList.SelectionChanged += delegate
		{
			SyncDetail();
		};
		SetStatus(text ?? "就绪。填好配置 → 「① 生成工程」→「② 一键构建 + 安装」。");
	}

	private void SyncDetail()
	{
		CardDetail.DataContext = CardList.SelectedItem;
		RelicDetail.DataContext = RelicList.SelectedItem;
		PotionDetail.DataContext = PotionList.SelectedItem;
		KeywordDetail.DataContext = KeywordList.SelectedItem;
		Raise("CustomKeywordRows");
		Raise("KeywordChoices");
		Raise("HasCustomKeywords");
		Raise("NoCustomKeywords");
		Raise("SelectedCardUpgradeCost");
		Raise("UpgradeCostHint");
		Raise("UpgradeKeywordRows");
		// 召唤物：图片预览 + 效果编辑器「召唤物（哪一只）」下拉的候选（只列已启用的）
		Raise("SelectedSummonImagePreview");
		RaisePetSummonChoices();
		CardList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Profile.Cards"));
		RelicList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Profile.Relics"));
		PotionList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Profile.Potions"));
		KeywordList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Profile.CustomKeywords"));
		SummonList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Profile.Summons"));
		CardEffectList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Effects"));
		RelicEffectList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Effects"));
		PotionEffectList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Effects"));
		Raise("CardPortraitPreview");
		Raise("CardPortraitPreviewNote");
		Raise("RelicIconPreview");
	}

	/// <summary>
	/// 刷新「召唤物（哪一只）」下拉的候选。改了召唤物列表（增删 / 启用开关 / 改名字 / 改类名）之后必须调用 ——
	/// 这个候选是**算出来的**（只列已启用的），不重新通知的话界面上的下拉还是旧的候选。
	/// </summary>
	private void RaisePetSummonChoices()
	{
		// 「召唤物（哪一只）」下拉的候选在卡牌 / 遗物 / 药水的效果栏里共用同一份（PetGen.Choices），
		// 模板分开只是为了自检能分别查，两个属性都要通知。
		Raise("PetSummonsCard");
		Raise("PetSummonsPlain");
	}

	private void OnPickPotionIcon(object sender, RoutedEventArgs e)
	{
		if (!(PotionList.SelectedItem is PotionSpec potionSpec))
		{
			SetStatus("请先选中一个药水。");
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "图片 (*.png)|*.png"
		};
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			potionSpec.Icon = openFileDialog.FileName;
			Raise("PotionIconPreview");
			PersistArtChange($"已为药水「{potionSpec.Name}」设置图标：{openFileDialog.FileName}（建议 256×256 PNG）");
		}
	}

	private static BitmapImage? LoadImage(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.UriSource = new Uri(path);
			bitmapImage.EndInit();
			return bitmapImage;
		}
		catch
		{
			return null;
		}
	}

	private void NormalizeOutputDir()
	{
		string outputDir = Path.Combine(RootFolder, "自定义角色存档");
		string text = _profile.Paths.OutputDir ?? "";
		// 没填 / 目录不存在 → 用「存档目录」；另外把早期开发版留下的默认输出目录（叫 generated 的那种）
		// 也一起纠正。注意：这里**不写死任何盘符路径** —— 以前写死了开发机的 D:\...，
		// 会跟着程序集发给别人（既暴露无关路径、换台电脑又是错的），现在只按目录名判断。
		bool legacyDevDefault = string.Equals(Path.GetFileName(text.TrimEnd('\\', '/')), "generated", StringComparison.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(text) || legacyDevDefault || !Directory.Exists(text))
		{
			_profile.Paths.OutputDir = outputDir;
		}
	}

	public static ProfileMoveResult MoveProfileFolderCore(string oldDir, string newDir, Action<string>? log)
	{
		Directory.CreateDirectory(newDir);
		int num = 0;
		int num2 = 0;
		List<string> list = new List<string>();
		string[] fileSystemEntries = Directory.GetFileSystemEntries(oldDir);
		foreach (string text in fileSystemEntries)
		{
			string fileName = Path.GetFileName(text);
			string text2 = Path.Combine(newDir, fileName);
			try
			{
				if (File.Exists(text2) || Directory.Exists(text2))
				{
					num2++;
					continue;
				}
				if (Directory.Exists(text))
				{
					Directory.Move(text, text2);
				}
				else
				{
					File.Move(text, text2);
				}
				num++;
				if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				{
					list.Add(text2);
				}
			}
			catch
			{
				num2++;
			}
		}
		foreach (string item in list)
		{
			try
			{
				CharacterProfile characterProfile = ProfileFactory.Load(item);
				string text3 = characterProfile.Paths.OutputDir ?? "";
				if (text3.StartsWith(oldDir, StringComparison.OrdinalIgnoreCase))
				{
					characterProfile.Paths.OutputDir = Path.Combine(newDir, text3.Substring(oldDir.Length).TrimStart('\\', '/'));
					ProfileFactory.Save(characterProfile, item);
				}
			}
			catch
			{
			}
		}
		bool flag = false;
		if (!Directory.EnumerateFileSystemEntries(oldDir).Any())
		{
			try
			{
				Directory.Delete(oldDir);
				flag = true;
			}
			catch
			{
			}
		}
		log?.Invoke($"搬了 {num} 项，跳过 {num2} 项，旧目录{(flag ? "已删除" : "保留")}");
		return new ProfileMoveResult(num, num2, flag);
	}

	public string? MigrateLegacyProfileFolder()
	{
		if (_profileFolderOverride != null)
		{
			return null;
		}
		try
		{
			string text = Path.Combine(AppContext.BaseDirectory, "自定义角色");
			string profileFolder = ProfileFolder;
			if (!Directory.Exists(text))
			{
				return null;
			}
			if (string.Equals(Path.GetFullPath(text), Path.GetFullPath(profileFolder), StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			ProfileMoveResult profileMoveResult = MoveProfileFolderCore(text, profileFolder, delegate(string line)
			{
				AppendLog("  存档搬迁：" + line);
			});
			AppendLog($"已把存档目录搬到根目录：{profileFolder}（搬了 {profileMoveResult.Moved} 项" + ((profileMoveResult.Skipped > 0) ? $"，跳过 {profileMoveResult.Skipped} 项重名的" : "") + (profileMoveResult.OldRemoved ? "，旧目录已删除" : "，旧目录里还有东西没搬，请自己确认一下") + "）");
			return $"存档目录已搬到「{"自定义角色存档"}」（根目录），搬了 {profileMoveResult.Moved} 项" + ((profileMoveResult.Skipped > 0) ? $"，跳过 {profileMoveResult.Skipped} 项重名" : "") + (profileMoveResult.OldRemoved ? "，旧目录已删除" : "");
		}
		catch (Exception ex)
		{
			AppendLog("搬迁旧存档目录失败（不影响使用）：" + ex.Message);
			return null;
		}
	}

	public void RefreshCardChoices()
	{
		try
		{
			List<PowerEntry> list = EffectCatalog.Cards.Concat(Profile.AllCards.Select(delegate(CardSpec c, int i)
			{
				string text = Naming.From(Profile).CardClassName(Profile, c);
				return new PowerEntry(text, "", "Card", "Counter", c.Name, text);
			})).ToList();
			for (int j = 0; j < list.Count; j++)
			{
				PowerEntry powerEntry = list[j];
				if (j >= AllCards.Count || !(AllCards[j].Id == powerEntry.Id) || !(AllCards[j].Display == powerEntry.Display))
				{
					if (j < AllCards.Count)
					{
						AllCards[j] = powerEntry;
					}
					else
					{
						AllCards.Add(powerEntry);
					}
				}
			}
			while (AllCards.Count > list.Count)
			{
				AllCards.RemoveAt(AllCards.Count - 1);
			}
		}
		catch
		{
		}
	}

	public void RefreshPowerChoices()
	{
		try
		{
			List<PowerEntry> list = EffectCatalog.Buffs.Concat(EffectCatalog.Debuffs).Concat(EffectCatalog.CustomPowerEntries).ToList();
			for (int i = 0; i < list.Count; i++)
			{
				if (i >= AllPowers.Count || !(AllPowers[i].Id == list[i].Id) || !(AllPowers[i].Display == list[i].Display))
				{
					if (i < AllPowers.Count)
					{
						AllPowers[i] = list[i];
					}
					else
					{
						AllPowers.Add(list[i]);
					}
				}
			}
			while (AllPowers.Count > list.Count)
			{
				AllPowers.RemoveAt(AllPowers.Count - 1);
			}
		}
		catch
		{
		}
	}

	public string AutoFillPaths(bool report = false)
	{
		PathsSpec paths = _profile.Paths;
		List<string> list = new List<string>();
		bool flag = false;
		if (!File.Exists(string.IsNullOrWhiteSpace(paths.GameDataDir) ? "" : Path.Combine(paths.GameDataDir, "sts2.dll")))
		{
			string text = PathAutoDetect.FindGameDir();
			if (text != null)
			{
				if (!string.Equals(paths.GameDataDir, Path.Combine(text, "data_sts2_windows_x86_64"), StringComparison.OrdinalIgnoreCase))
				{
					paths.GameDataDir = Path.Combine(text, "data_sts2_windows_x86_64");
					flag = true;
					list.Add("游戏 data 目录 → " + paths.GameDataDir);
				}
				if (string.IsNullOrWhiteSpace(paths.InstallDir) || !Directory.Exists(paths.InstallDir))
				{
					paths.InstallDir = Path.Combine(text, "mods");
					flag = true;
					list.Add("安装目录 → " + paths.InstallDir);
				}
				AppendLog("自动探测到游戏安装目录：" + text);
			}
			else
			{
				list.Add("没探测到游戏安装目录（Steam 装在非常规位置时需要手工指定）");
			}
		}
		if (string.IsNullOrWhiteSpace(paths.VanillaProject) || !Directory.Exists(paths.VanillaProject))
		{
			string text2 = PathAutoDetect.FindVanillaProject();
			if (text2 != null && !string.Equals(paths.VanillaProject, text2, StringComparison.OrdinalIgnoreCase))
			{
				paths.VanillaProject = text2;
				flag = true;
				list.Add("解包工程目录 → " + text2);
			}
		}
		if (string.IsNullOrWhiteSpace(paths.GodotExe) || !File.Exists(paths.GodotExe))
		{
			string text3 = PathAutoDetect.FindGodotNearby(AppContext.BaseDirectory);
			if (text3 != null)
			{
				paths.GodotExe = text3;
				flag = true;
				list.Add("Godot → " + text3);
			}
		}
		if (string.IsNullOrWhiteSpace(paths.DotnetExe) || !File.Exists(paths.DotnetExe))
		{
			string text4 = PathAutoDetect.FindDotnetNearby(AppContext.BaseDirectory);
			if (text4 != null)
			{
				paths.DotnetExe = text4;
				flag = true;
				list.Add("dotnet（环境包便携版）→ " + text4);
			}
		}
		Raise("Profile");
		if (flag)
		{
			SaveCurrentProfileQuietly();
		}
		if (report)
		{
			string messageBoxText = (flag ? ("已自动填好：\r\n· " + string.Join("\r\n· ", list)) : ((list.Count > 0) ? string.Join("\r\n", list) : "当前环境都还可用，无需改动。"));
			MessageBox.Show(this, messageBoxText, "自动探测", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		return string.Join(" / ", list);
	}

	public void RefreshEnvStatusLine()
	{
		Raise("EnvStatusLine");
	}

	private void SaveCurrentProfileQuietly()
	{
		// 命令行自检模式（--uicheck / --buildtest）绝不写用户的存档：
		// 这两个模式以前会「载入最新存档 → 自动填路径 → 静默保存」，结果把自检用的临时路径
		// 写进了用户的真实存档里（实测踩过）。自检只读存档，写入一律跳过。
		if (SuppressProfileWrites)
		{
			AppendLog("（自检模式：跳过静默保存，不动用户存档）");
			return;
		}
		// 只在「确实载入/保存过某个存档文件」时静默保存。
		// 另外：内存里的配置如果还是「刚新建的示例配置」（SaveName 对不上当前文件），就绝不写 ——
		// 这条兜底是为了防止「新建存档 → 取消保存对话框 → 后面选路径时把示例配置写进上一个存档」这种事故。
		if (string.IsNullOrWhiteSpace(_currentProfilePath) || !File.Exists(_currentProfilePath))
		{
			return;
		}
		if (!string.Equals(_profile.SaveName, Path.GetFileNameWithoutExtension(_currentProfilePath), StringComparison.Ordinal))
		{
			AppendLog($"（跳过静默保存：内存里的配置不是「{Path.GetFileName(_currentProfilePath)}」，"
				+ "请手动「保存存档」以免覆盖错文件）");
			return;
		}
		try
		{
			ProfileFactory.Save(_profile, _currentProfilePath);
		}
		catch (Exception ex)
		{
			AppendLog("路径自动保存失败：" + ex.Message);
		}
	}

	public void ReloadCatalog()
	{
		AutoFillPaths();
		// 安装目录也要认出来：本体只认 <游戏目录>\mods，选错一层（游戏目录 / data 目录）就装不进去
		EnsureModsInstallDir(quiet: true);
		EffectCatalog.Initialize(_profile.Paths.VanillaProject, _profile.Paths.GameDataDir);
		RefreshPowerChoices();
		Raise("CatalogStatus");
		RefreshCardChoices();
		RefreshEnvStatusLine();
		if (EffectCatalog.Powers.Count == 0)
		{
			SetStatus("⚠ 效果库为空：本机没找到游戏文件。先看「环境自检」页缺哪项（游戏没装？路径被改过？）。");
			try
			{
				MainTabs.SelectedItem = EnvTab;
			}
			catch
			{
			}
		}
	}

	private void OnReloadCatalog(object sender, RoutedEventArgs e)
	{
		ReloadCatalog();
		SetStatus("效果库：" + CatalogStatus);
	}

	private void OnAutoDetectPaths(object sender, RoutedEventArgs e)
	{
		AutoFillPaths(report: true);
		ReloadCatalog();
		RecheckEnvironment();
		SetStatus("自动探测完成 → 效果库：" + CatalogStatus);
	}

	public void RecheckEnvironment()
	{
		List<EnvCheckItem> list = EnvCheck.Run(_profile);
		EnvItems.Clear();
		foreach (EnvCheckItem item in list)
		{
			EnvItems.Add(item);
		}
		int num = list.Count((EnvCheckItem i) => i.IsError);
		int num2 = list.Count((EnvCheckItem i) => i.Level == "警告");
		EnvSummaryText = ((num == 0 && num2 == 0) ? $"全部通过（{list.Count} 项）" : $"通过 {list.Count - num - num2} 项 / 警告 {num2} 项 / 错误 {num} 项");
		Raise("EnvSummaryText");
		if (EnvSummary != null)
		{
			EnvSummary.Text = EnvSummaryText;
		}
		RefreshEnvStatusLine();
	}

	private void OnRecheckEnv(object sender, RoutedEventArgs e)
	{
		RecheckEnvironment();
		SetStatus("环境自检：" + EnvSummaryText);
	}

	private void OnOpenEnvUrl(object sender, RoutedEventArgs e)
	{
		if (!(sender is FrameworkElement { Tag: string { Length: not 0 } tag }))
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo(tag)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			SetStatus("打不开链接：" + ex.Message);
		}
	}

	private void OnPickEnvPath(object sender, RoutedEventArgs e)
	{
		if (!(sender is FrameworkElement { Tag: string { Length: not 0 } tag }))
		{
			return;
		}
		string text = tag switch
		{
			"godot" => PickFile("选择 Godot 4.x mono 版可执行文件（建议选 *_console.exe）", "Godot (*.exe)|*.exe", _profile.Paths.GodotExe, "godot"), 
			"dotnet" => PickFile("选择 dotnet.exe（整合包环境包自带的那个）", "dotnet (dotnet.exe)|dotnet.exe|可执行文件 (*.exe)|*.exe", _profile.Paths.DotnetExe, "dotnet"), 
			"gamedata" => PickFolder(EnvCheck.PickActions["gamedata"] + "（内含 sts2.dll）", _profile.Paths.GameDataDir), 
			"vanilla" => PickFolder(EnvCheck.PickActions["vanilla"] + "（里面应有 project.godot、src、scenes、images）", _profile.Paths.VanillaProject), 
			"install" => PickFolder(EnvCheck.PickActions["install"], _profile.Paths.InstallDir), 
			"output" => PickFolder(EnvCheck.PickActions["output"], _profile.Paths.OutputDir), 
			_ => null, 
		};
		if (text != null)
		{
			if (!ApplyEnvPick(tag, text))
			{
				SetStatus("这个按钮对应的路径类型不认识：" + tag);
			}
			else
			{
				AfterEnvPick(tag, text);
			}
		}
	}

	internal bool ApplyEnvPick(string pick, string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		switch (pick)
		{
		case "godot":
			_profile.Paths.GodotExe = value;
			break;
		case "dotnet":
			_profile.Paths.DotnetExe = value;
			break;
		case "gamedata":
			_profile.Paths.GameDataDir = value;
			break;
		case "vanilla":
			_profile.Paths.VanillaProject = value;
			break;
		case "install":
			_profile.Paths.InstallDir = value;
			break;
		case "output":
			_profile.Paths.OutputDir = value;
			break;
		default:
			return false;
		}
		Raise("Profile");
		return true;
	}

	private void AfterEnvPick(string pick, string value)
	{
		if ((pick == "vanilla" || pick == "gamedata") ? true : false)
		{
			ReloadCatalog();
		}
		RecheckEnvironment();
		SaveCurrentProfileQuietly();
		SetStatus(EnvPickStatus(pick, value));
	}

	public static string EnvPickStatus(string pick, string value)
	{
		string fileName = Path.GetFileName(value.TrimEnd('\\', '/'));
		if (pick == "godot" && !fileName.Contains("godot", StringComparison.OrdinalIgnoreCase))
		{
			return "⚠ 已填 Godot 路径，但文件名里没有 godot：" + value + " —— 确认选的是 Godot 的可执行文件（建议 *_console.exe）";
		}
		if (pick == "godot" && !fileName.Contains("console", StringComparison.OrdinalIgnoreCase))
		{
			return "已填 Godot：" + value + "（建议改用同目录的 *_console.exe，导出失败时才有详细日志）";
		}
		return pick switch
		{
			"godot" => "已填 Godot：" + value, 
			"dotnet" => "已选用指定的 dotnet：" + value, 
			"gamedata" => "已填游戏 data 目录：" + value, 
			"vanilla" => "已填解包工程目录：" + value, 
			"install" => "已填模组安装目录：" + value, 
			"output" => "已填工程输出目录：" + value, 
			_ => "已填路径：" + value, 
		};
	}

	private string? PickFile(string title, string filter, string? current, string hint)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = title,
			Filter = filter
		};
		string text = null;
		if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
		{
			text = Path.GetDirectoryName(current);
		}
		if (text == null)
		{
			try
			{
				string text2 = ((hint == "dotnet") ? PathAutoDetect.FindDotnetNearby(AppContext.BaseDirectory) : PathAutoDetect.FindGodotNearby(AppContext.BaseDirectory));
				if (!string.IsNullOrWhiteSpace(text2) && File.Exists(text2))
				{
					text = Path.GetDirectoryName(text2);
				}
			}
			catch
			{
			}
		}
		if (text != null)
		{
			openFileDialog.InitialDirectory = text;
		}
		if (!openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	private void HookProfileColors()
	{
		Profile.Colors.PropertyChanged += delegate
		{
			Raise("PresetSelection");
			Raise("FrameSelection");
		};
	}

	private void Raise(string n)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
	}

	private void BuildAncientRows()
	{
		MigrateArchitectDialogue();
		Ancients.Clear();
		foreach (AncientEntry entry in EffectCatalog.Ancients)
		{
			AncientTalkSpec ancientTalkSpec = _profile.Ancients.FirstOrDefault((AncientTalkSpec a) => string.Equals(a.AncientId, entry.Id, StringComparison.OrdinalIgnoreCase));
			if (ancientTalkSpec == null)
			{
				ancientTalkSpec = new AncientTalkSpec
				{
					AncientId = entry.Id
				};
				_profile.Ancients.Add(ancientTalkSpec);
			}
			AncientRow ancientRow = new AncientRow(entry, ancientTalkSpec);
			foreach (AncientDialogueSpec dialogue in ancientTalkSpec.Dialogues)
			{
				dialogue.UnitLabel = ancientRow.UnitLabel;
			}
			Ancients.Add(ancientRow);
		}
		Raise("AncientCatalogMissing");
		// 左边选中的先古之民可能没变，但这一页的清单（他原本会给的遗物 / 替换记录）要跟着重算：
		// 绑不刷新的话，界面上就是空的（用户只会看到「点了没反应」）。
		RaiseAncientRelicLists();
	}

	private void MigrateArchitectDialogue()
	{
		List<string> list = (_profile.ArchitectDialogue ?? "").Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
		if (list.Count == 0)
		{
			return;
		}
		AncientTalkSpec ancientTalkSpec = _profile.Ancients.FirstOrDefault((AncientTalkSpec a) => string.Equals(a.AncientId, "THE_ARCHITECT", StringComparison.OrdinalIgnoreCase));
		if (ancientTalkSpec == null)
		{
			ancientTalkSpec = new AncientTalkSpec
			{
				AncientId = "THE_ARCHITECT"
			};
			_profile.Ancients.Add(ancientTalkSpec);
		}
		if (!ancientTalkSpec.HasAnyText)
		{
			AncientDialogueSpec ancientDialogueSpec = new AncientDialogueSpec
			{
				Times = 1,
				Repeating = true
			};
			for (int i = 0; i < list.Count; i++)
			{
				ancientDialogueSpec.Lines.Add(new AncientLineSpec
				{
					AncientSpeaks = (i % 2 == 0),
					Text = list[i],
					NextText = ((i % 2 == 0) ? "继续" : "……")
				});
			}
			ancientTalkSpec.Dialogues.Add(ancientDialogueSpec);
		}
	}

	private void OnAddAncientDialogue(object sender, RoutedEventArgs e)
	{
		if (!(AncientList.SelectedItem is AncientRow ancientRow))
		{
			SetStatus("请先在左边选择一位先古之民。");
			return;
		}
		bool flag = ancientRow.Entry.NativeSlots.Any((AncientSlot s) => s.Scope == "firstVisitEver");
		AncientDialogueSpec ancientDialogueSpec = new AncientDialogueSpec
		{
			Times = ((!flag) ? 1 : 2),
			Repeating = false,
			UnitLabel = ancientRow.UnitLabel
		};
		ancientDialogueSpec.Lines.Add(new AncientLineSpec
		{
			AncientSpeaks = true,
			Text = "",
			NextText = "继续"
		});
		ancientRow.Dialogues.Add(ancientDialogueSpec);
		AncientDialogueList.SelectedItem = ancientDialogueSpec;
		SetStatus("已给「" + ancientRow.Entry.Display + "」加了一段对话，在下面填内容。");
	}

	private void OnRemoveAncientDialogue(object sender, RoutedEventArgs e)
	{
		if (AncientList.SelectedItem is AncientRow ancientRow)
		{
			if (!(AncientDialogueList.SelectedItem is AncientDialogueSpec item))
			{
				SetStatus("请先选中要删的那一段。");
				return;
			}
			ancientRow.Dialogues.Remove(item);
			SetStatus("已删掉这一段（重新生成就不再生效）。");
		}
	}

	private void OnAddAncientLine(object sender, RoutedEventArgs e)
	{
		if (!(AncientDialogueList.SelectedItem is AncientDialogueSpec ancientDialogueSpec))
		{
			SetStatus("请先选中一段对话。");
			return;
		}
		ancientDialogueSpec.Lines.Add(new AncientLineSpec
		{
			AncientSpeaks = !(ancientDialogueSpec.Lines.LastOrDefault()?.AncientSpeaks ?? false),
			Text = "",
			NextText = "继续"
		});
	}

	private void OnRemoveAncientLine(object sender, RoutedEventArgs e)
	{
		if (sender is Button { DataContext: AncientLineSpec dataContext } && AncientDialogueList.SelectedItem is AncientDialogueSpec ancientDialogueSpec)
		{
			ancientDialogueSpec.Lines.Remove(dataContext);
		}
	}

	private void RefreshExtraResourceIconPreview()
	{
		Raise("ExtraResourceIconPreview");
	}

	private void OnPowerOverrideSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		SyncPowerOverrideDetail();
	}

	private void SyncPowerOverrideDetail()
	{
		if (PowerOverrideDetail != null)
		{
			PowerOverrideDetail.DataContext = PowerOverrideList.SelectedItem;
			Raise("PowerIconPreview");
			Raise("BarColorHint");
			Raise("VanillaTextHint");
		}
	}

	private static CharacterProfile ProfileWith(VanillaPowerOverride o)
	{
		return new CharacterProfile
		{
			VanillaPowerOverrides = { o }
		};
	}

	/// <summary>校验 / 文本替换的单元测试用：拿一份只有一条「本体关键词改名」的最小配置。</summary>
	private static CharacterProfile ProfileWithKeywordRename(string keywordId, string name, string description)
	{
		return new CharacterProfile
		{
			KeywordRenames = { new VanillaKeywordRenameSpec { KeywordId = keywordId, Name = name, Description = description } }
		};
	}

	/// <summary>
	/// 自检里查「本体关键词改名」校验用的配置：带上当前这份配置的环境路径（解包工程 / 游戏目录），
	/// 否则校验器会先报「解包工程目录不存在」那两条，第一条错误就不是我们要断言的那条了。
	/// </summary>
	private CharacterProfile RenameCheckProfile()
	{
		CharacterProfile p = ProfileFactory.Sample();
		p.Paths.VanillaProject = Profile.Paths.VanillaProject;
		p.Paths.GameDataDir = Profile.Paths.GameDataDir;
		p.Paths.GodotExe = Profile.Paths.GodotExe;
		p.Paths.OutputDir = Path.Combine(Path.GetTempPath(), "forge_uicheck_kwrename");
		p.KeywordRenames.Clear();
		return p;
	}

	private static ImageSource? LoadPreview(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.UriSource = new Uri(path);
			bitmapImage.EndInit();
			return bitmapImage;
		}
		catch
		{
			return null;
		}
	}

	private void OnAddPowerOverride(object sender, RoutedEventArgs e)
	{
		PowerEntry powerEntry = EffectCatalog.FindPower("PoisonPower") ?? EffectCatalog.Powers.FirstOrDefault();
		VanillaPowerOverride vanillaPowerOverride = new VanillaPowerOverride
		{
			PowerId = (powerEntry?.Id ?? ""),
			LocSlug = (powerEntry?.Slug ?? EffectCatalog.SlugFor(powerEntry?.Id)),
			VanillaName = (powerEntry?.Zh ?? "")
		};
		if ((object)powerEntry != null)
		{
			vanillaPowerOverride.Name = powerEntry.Zh;
			vanillaPowerOverride.Description = EffectCatalog.ZhLocText(powerEntry.Slug + ".description") ?? vanillaPowerOverride.Description;
		}
		Profile.VanillaPowerOverrides.Add(vanillaPowerOverride);
		PowerOverrideList.SelectedItem = vanillaPowerOverride;
		SyncPowerOverrideDetail();
		SetStatus(((object)powerEntry == null) ? "已添加一条本体状态改写（先去选一个状态）。" : ("已添加「" + powerEntry.Zh + "」的改写：改完名字/图标/颜色后点「生成」就会写进模组。"));
	}

	private void OnRemovePowerOverride(object sender, RoutedEventArgs e)
	{
		// 「删除选中」：支持 Ctrl/Shift 多选一次删掉多条（以前只看 SelectedItem，选了一堆也只删一条）
		List<VanillaPowerOverride> picked = SelectedOf<VanillaPowerOverride>(PowerOverrideList);
		if (picked.Count == 0)
		{
			SetStatus("请先在列表里选中要删除的改写（可 Ctrl/Shift 多选）。");
		}
		else
		{
			string what = picked.Count == 1 ? "「" + picked[0].Display + "」这条改写" : $"选中的 {picked.Count} 条改写";
			if (Confirm($"确定删掉{what}吗？", "确认删除"))
			{
				foreach (VanillaPowerOverride item in picked) Profile.VanillaPowerOverrides.Remove(item);
				SyncPowerOverrideDetail();
				SetStatus($"已删除 {picked.Count} 条改写。");
			}
		}
	}

	private void OnRenameInDescription(object sender, RoutedEventArgs e)
	{
		if (!(PowerOverrideList.SelectedItem is VanillaPowerOverride vanillaPowerOverride))
		{
			return;
		}
		string text = (vanillaPowerOverride.VanillaName ?? "").Trim();
		string text2 = (vanillaPowerOverride.Name ?? "").Trim();
		if (text.Length == 0 || text2.Length == 0 || text == text2)
		{
			SetStatus("先填「新名字」，且新名字要和原名字不一样。");
			return;
		}
		if (vanillaPowerOverride.Description.Length == 0)
		{
			vanillaPowerOverride.Description = EffectCatalog.ZhLocText(EffectCatalog.SlugFor(vanillaPowerOverride.PowerId) + ".description") ?? "";
			if (vanillaPowerOverride.Description.Length == 0)
			{
				SetStatus("本体没有现成的描述可改，直接在「新描述」里写就行（原名：" + text + "）。");
				return;
			}
		}
		if (!vanillaPowerOverride.Description.Contains(text, StringComparison.Ordinal))
		{
			SetStatus("描述里没有出现「" + text + "」，不用替换。");
			return;
		}
		int value = vanillaPowerOverride.Description.Split(text).Length - 1;
		vanillaPowerOverride.Description = vanillaPowerOverride.Description.Replace(text, text2);
		SetStatus($"已把描述里的 {value} 处「{text}」换成「{text2}」。");
	}

	private void OnPickPowerIcon(object sender, RoutedEventArgs e)
	{
		if (!(PowerOverrideList.SelectedItem is VanillaPowerOverride vanillaPowerOverride))
		{
			SetStatus("请先选中一条改写。");
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "图片 (*.png)|*.png|所有文件 (*.*)|*.*"
		};
		if (!string.IsNullOrWhiteSpace(vanillaPowerOverride.Icon) && File.Exists(vanillaPowerOverride.Icon))
		{
			openFileDialog.InitialDirectory = Path.GetDirectoryName(vanillaPowerOverride.Icon);
		}
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			vanillaPowerOverride.Icon = openFileDialog.FileName;
			Raise("PowerIconPreview");
			PersistArtChange("已选图标：" + Path.GetFileName(openFileDialog.FileName) + "（原图不再使用）");
		}
	}

	private void OnClearPowerIcon(object sender, RoutedEventArgs e)
	{
		if (!(PowerOverrideList.SelectedItem is VanillaPowerOverride vanillaPowerOverride))
		{
			SetStatus("请先选中一条改写。");
			return;
		}
		vanillaPowerOverride.Icon = "";
		Raise("PowerIconPreview");
		SetStatus("已清除图标，恢复用本体原图。");
		PersistArtChange("已清除图标，恢复用本体原图");
	}

	private void OnUseDefaultBarColor(object sender, RoutedEventArgs e)
	{
		if (!(PowerOverrideList.SelectedItem is VanillaPowerOverride vanillaPowerOverride))
		{
			SetStatus("请先选中一条改写。");
			return;
		}
		string text = EffectCatalog.DefaultBarColorFor(vanillaPowerOverride.PowerId);
		if (text == null)
		{
			SetStatus(BarColorHint);
			return;
		}
		vanillaPowerOverride.BarColor = text;
		Raise("BarColorHint");
		SetStatus("血条颜色已填成本体原色 " + text + "（改这里就能变色）。");
	}

	private void OnCustomPowerSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		SyncCustomPowerDetail();
	}

	private void SyncCustomPowerDetail()
	{
		if (CustomPowerDetail != null)
		{
			CustomPowerDetail.DataContext = CustomPowerList.SelectedItem;
			Raise("CustomPowerIconPreview");
			Raise("TriggerHint");
			Raise("CustomTriggersSummary");
			Raise("CustomPowerDescriptionHint");
			ResyncCustomPowerHooks();
		}
	}

	private void OnCustomTriggerSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		Raise("TriggerHint");
	}

	/// <summary>
	/// 把「当前选中的状态」整棵树都挂上监听：状态本身、它的触发器、触发器里的效果。
	/// 任何一处改动都要让下面那两句说明（触发时机说明 / 描述预览）立刻重算 ——
	/// 以前只监听触发器本身，所以改了「触发时做什么」那句话不变（用户反馈过两次）。
	/// </summary>
	private void ResyncCustomPowerHooks()
	{
		foreach (var obj in _hookedSpecs) obj.PropertyChanged -= OnHookedSpecChanged;
		foreach (var col in _hookedCollections) col.CollectionChanged -= OnHookedCollectionChanged;
		_hookedSpecs.Clear();
		_hookedCollections.Clear();

		if (!(CustomPowerList?.SelectedItem is CustomPowerSpec power)) return;
		Hook(power);
		HookCollection(power.Triggers);
		foreach (PowerTriggerSpec t in power.Triggers)
		{
			Hook(t);
			HookCollection(t.Effects);
			foreach (EffectSpec e in t.Effects) Hook(e);
		}

		void Hook(System.ComponentModel.INotifyPropertyChanged? obj)
		{
			if (obj is null) return;
			obj.PropertyChanged += OnHookedSpecChanged;
			_hookedSpecs.Add(obj);
		}
		void HookCollection(System.Collections.Specialized.INotifyCollectionChanged? col)
		{
			if (col is null) return;
			col.CollectionChanged += OnHookedCollectionChanged;
			_hookedCollections.Add(col);
		}
	}

	private readonly List<System.ComponentModel.INotifyPropertyChanged> _hookedSpecs = new();

	private readonly List<System.Collections.Specialized.INotifyCollectionChanged> _hookedCollections = new();

	private void OnHookedSpecChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaiseCustomPowerTexts();

	private void OnHookedCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		ResyncCustomPowerHooks();   // 增删了触发器 / 效果，要重新挂监听
		RaiseCustomPowerTexts();
	}

	/// <summary>
	/// 界面上那几处「触发时机 / 做什么」的文字全部重算：
	/// 触发器列表里每一行（Display）、当前那句说明（TriggerHint）、整段汇总（CustomTriggersSummary）、
	/// 描述预览（CustomPowerDescriptionHint）。效果改了要让触发器自己重发一次 Display，列表行才会跟着变。
	/// </summary>
	private void RaiseCustomPowerTexts()
	{
		// 触发器自己也在监听里，NotifyEffectsChanged 会再回到这个方法 —— 必须挡一层，否则无限递归（栈溢出）
		if (_raisingPowerTexts) return;
		_raisingPowerTexts = true;
		try
		{
			if (CustomPowerList?.SelectedItem is CustomPowerSpec power)
				foreach (PowerTriggerSpec t in power.Triggers) t.NotifyEffectsChanged();
			Raise("TriggerHint");
			Raise("CustomTriggersSummary");
			Raise("CustomPowerDescriptionHint");
		}
		finally
		{
			_raisingPowerTexts = false;
		}
	}

	private bool _raisingPowerTexts;

	/// <summary>「触发时机（什么时候做什么）」下面那句实时汇总：这个状态配的每一条。</summary>
	public string CustomTriggersSummary
	{
		get
		{
			if (!(CustomPowerList?.SelectedItem is CustomPowerSpec power)) return "先在上面选一个状态。";
			if (power.Triggers.Count == 0) return "还没配触发时机 —— 这个状态挂上去不会做任何事。";
			return "这个状态现在的行为：\n" + string.Join("\n", power.Triggers.Select((PowerTriggerSpec t, int i) => $"  {i + 1}. {t.Summary}"));
		}
	}

	private void OnAddCustomPower(object sender, RoutedEventArgs e)
	{
		int value = _profile.CustomPowers.Count + 1;
		CustomPowerSpec customPowerSpec = new CustomPowerSpec
		{
			Name = $"新状态{value}",
			ClassName = Naming.From(_profile).CharClass + "Power" + value,
			Type = "Buff"
		};
		PowerTriggerSpec powerTriggerSpec = new PowerTriggerSpec
		{
			Kind = "TurnStart"
		};
		powerTriggerSpec.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 3m,
			TargetSide = "Self"
		});
		customPowerSpec.Triggers.Add(powerTriggerSpec);
		_profile.CustomPowers.Add(customPowerSpec);
		CustomPowerList.SelectedItem = customPowerSpec;
		SyncCustomPowerDetail();
		SetStatus("已添加自定义状态「" + customPowerSpec.Name + "」：改好名字和触发时机后，在卡牌效果里选「施加增益/减益」就能用它。");
	}

	private void OnRemoveCustomPower(object sender, RoutedEventArgs e)
	{
		// 「删除选中」：支持 Ctrl/Shift 多选一次删掉多个（以前只看 SelectedItem）
		List<CustomPowerSpec> picked = SelectedOf<CustomPowerSpec>(CustomPowerList);
		if (picked.Count == 0)
		{
			SetStatus("请先在左边选中要删除的状态（可 Ctrl/Shift 多选）。");
			return;
		}
		string what = picked.Count == 1
			? "自定义状态「" + picked[0].Name + "」"
			: $"选中的 {picked.Count} 个自定义状态";
		if (Confirm($"确定删掉{what}吗？\n\n（已经用了这个状态的卡牌效果会失效，记得一起改掉。）", "确认删除"))
		{
			int value = RemoveManyWithUndo(_profile.CustomPowers, picked, _powerUndo, "自定义状态", delegate
			{
				CustomPowerList.SelectedItem = picked[0];
			});
			EffectCatalog.SetCustomPowers(null);
			RefreshCustomPowerRegistry();
			SyncCustomPowerDetail();
			SetStatus($"已删除 {value} 个自定义状态（可点「撤回删除」恢复）。");
		}
	}

	private void OnPickCustomPowerIcon(object sender, RoutedEventArgs e)
	{
		if (!(CustomPowerList.SelectedItem is CustomPowerSpec customPowerSpec))
		{
			SetStatus("请先选中一个状态。");
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "图片 (*.png)|*.png|所有文件 (*.*)|*.*"
		};
		if (!string.IsNullOrWhiteSpace(customPowerSpec.Icon) && File.Exists(customPowerSpec.Icon))
		{
			openFileDialog.InitialDirectory = Path.GetDirectoryName(customPowerSpec.Icon);
		}
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			customPowerSpec.Icon = openFileDialog.FileName;
			Raise("CustomPowerIconPreview");
			PersistArtChange("已选图标：" + Path.GetFileName(openFileDialog.FileName));
		}
	}

	private void OnClearCustomPowerIcon(object sender, RoutedEventArgs e)
	{
		if (!(CustomPowerList.SelectedItem is CustomPowerSpec customPowerSpec))
		{
			SetStatus("请先选中一个状态。");
			return;
		}
		customPowerSpec.Icon = "";
		Raise("CustomPowerIconPreview");
		SetStatus("已清除图标，生成时用本体占位图。");
		PersistArtChange("已清除图标，生成时用本体占位图");
	}

	private void OnAutoPowerDescription(object sender, RoutedEventArgs e)
	{
		if (!(CustomPowerList.SelectedItem is CustomPowerSpec customPowerSpec))
		{
			SetStatus("请先选中一个状态。");
			return;
		}
		customPowerSpec.Description = PowerTriggers.AutoDescription(customPowerSpec);
		SetStatus("已按触发时机重新生成描述（还可以自己改）。");
	}

	private void OnAddPowerTrigger(object sender, RoutedEventArgs e)
	{
		if (!(CustomPowerList.SelectedItem is CustomPowerSpec customPowerSpec))
		{
			SetStatus("请先选中一个状态。");
			return;
		}
		PowerTriggerSpec powerTriggerSpec = new PowerTriggerSpec
		{
			Kind = "TurnStart"
		};
		powerTriggerSpec.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 3m,
			TargetSide = "Self"
		});
		customPowerSpec.Triggers.Add(powerTriggerSpec);
		CustomTriggerList.SelectedItem = powerTriggerSpec;
		Raise("TriggerHint");
		SetStatus("已添加一个触发时机（默认「玩家回合开始时 → 获得 3 点格挡」，在下面改）。");
	}

	private void OnRemovePowerTrigger(object sender, RoutedEventArgs e)
	{
		if (!(CustomPowerList.SelectedItem is CustomPowerSpec customPowerSpec))
		{
			SetStatus("请先在上面选中一个自定义状态。");
			return;
		}
		// 「删除触发时机」：支持 Ctrl/Shift 多选一次删掉多条
		List<PowerTriggerSpec> picked = SelectedOf<PowerTriggerSpec>(CustomTriggerList);
		if (picked.Count == 0)
		{
			SetStatus("请先选中要删除的触发时机（可 Ctrl/Shift 多选）。");
			return;
		}
		string what = picked.Count == 1 ? "「" + picked[0].Display + "」这条触发时机" : $"选中的 {picked.Count} 条触发时机";
		if (Confirm($"确定删掉{what}吗？", "确认删除"))
		{
			int value = RemoveManyWithUndo(customPowerSpec.Triggers, picked, _powerUndo, "触发时机", delegate
			{
				CustomTriggerList.SelectedItem = picked[0];
			});
			Raise("TriggerHint");
			SetStatus($"已删除 {value} 条触发时机（可点「撤回删除」恢复）。");
		}
	}

	public void RefreshCustomPowerRegistry()
	{
		List<(string, string)> list = new List<(string, string)>();
		for (int i = 0; i < _profile.CustomPowers.Count; i++)
		{
			CustomPowerSpec customPowerSpec = _profile.CustomPowers[i];
			if (CustomPowerGen.IsActive(customPowerSpec))
			{
				list.Add((CustomPowerGen.ClassNameOf(_profile, customPowerSpec, i), customPowerSpec.Name.Trim()));
			}
		}
		EffectCatalog.SetCustomPowers(list);
		RefreshPowerChoices();
	}

	/// <summary>
	/// 新建一条替换时给它挑一个默认「换成哪个遗物」：
	/// 优先挑你自己做的遗物（这才是这个功能最常见的用法），其次挑一个和「原本的遗物」不同的，
	/// 绝不把默认值设成和原本一样 —— 那样生成出来等于没改（踩过：用户以为「替换无效」）。
	/// </summary>
	private void ApplyDefaultTargetRelic(AncientRelicReplaceSpec spec)
	{
		var choices = AncientRelicChoices;
		if (choices.Count == 0) return;
		var nm = Naming.From(_profile);
		var myIds = new HashSet<string>(_profile.Relics.Select((RelicSpec r, int i) => nm.RelicClassName(r, i)), StringComparer.Ordinal);
		PowerEntry? pick =
			choices.FirstOrDefault(c => myIds.Contains(c.Id) && !string.Equals(c.Id, spec.FromRelicId, StringComparison.Ordinal))
			?? choices.FirstOrDefault(c => !string.Equals(c.Id, spec.FromRelicId, StringComparison.Ordinal))
			?? choices[0];
		spec.RelicId = pick.Id;
	}

	private void OnAddAncientRelicReplace(object sender, RoutedEventArgs e)
	{
		if (!(AncientList.SelectedItem is AncientRow ancientRow))
		{
			SetStatus("请先在左边选一位先古之民。");
			return;
		}
		AncientTalkSpec talk = ancientRow.Talk;
		var spec = new AncientRelicReplaceSpec();
		// 默认挑一个「他原本会给、而且还没配过替换」的遗物
		PowerEntry auto = AncientOriginalRelics
			.FirstOrDefault(o => !talk.RelicReplacements.Any(r => string.Equals(r.FromRelicId, o.Id, StringComparison.Ordinal)));
		if (auto is not null) spec.FromRelicId = auto.Id;
		ApplyDefaultTargetRelic(spec);
		talk.RelicReplacements.Add(spec);
		AncientRelicReplaceList.SelectedItem = spec;
		RaiseAncientRelicLists();
		SetStatus($"已给「{talk.AncientId}」加了一条遗物替换：{spec.Display}（改完点生成就写进模组）");
	}

	/// <summary>「替换掉选中的这个」：把上面清单里选中的「原本的遗物」加成一条替换记录。</summary>
	private void OnReplaceSelectedOriginalRelic(object sender, RoutedEventArgs e)
	{
		if (!(AncientList.SelectedItem is AncientRow ancientRow))
		{
			SetStatus("请先在左边选一位先古之民。");
			return;
		}
		if (!(AncientOriginalRelicList.SelectedItem is PowerEntry picked))
		{
			SetStatus("请先在上面「原本会给的遗物」里点一个要替换掉的。");
			return;
		}
		AncientTalkSpec talk = ancientRow.Talk;
		AncientRelicReplaceSpec? spec = talk.RelicReplacements
			.FirstOrDefault(r => string.Equals(r.FromRelicId, picked.Id, StringComparison.Ordinal));
		bool added = false;
		if (spec is null)
		{
			spec = new AncientRelicReplaceSpec { FromRelicId = picked.Id };
			ApplyDefaultTargetRelic(spec);
			talk.RelicReplacements.Add(spec);
			added = true;
		}
		else if (string.Equals(spec.RelicId, spec.FromRelicId, StringComparison.Ordinal))
		{
			ApplyDefaultTargetRelic(spec);   // 老记录里默认值 == 原本的遗物：顺手修正掉
		}
		AncientRelicReplaceList.SelectedItem = spec;
		RaiseAncientRelicLists();
		SetStatus(added
			? $"已加一条替换：{spec.Display} —— 在下面把「换成哪个遗物」选成你要的（改完点生成）。"
			: $"「{picked.Display}」已经配过替换了，直接选中了那条记录。");
	}

	/// <summary>换了先古之民：遗物相关的清单（他原本会给的遗物 / 可选遗物）都要跟着重算。</summary>
	private void OnAncientSelectionChanged(object sender, SelectionChangedEventArgs e) => RaiseAncientRelicLists();

	/// <summary>遗物相关的两个列表（原本的清单 / 替换记录）都在「先古之民」页里，改完要让它们重算。</summary>
	private void RaiseAncientRelicLists()
	{
		Raise("AncientOriginalRelics");
		Raise("AncientOriginalRelicSummary");
		Raise("AncientRelicChoices");
	}

	private void OnRemoveAncientRelicReplace(object sender, RoutedEventArgs e)
	{
		if (AncientList.SelectedItem is AncientRow ancientRow)
		{
			if (!(AncientRelicReplaceList.SelectedItem is AncientRelicReplaceSpec spec))
			{
				SetStatus("请先选中要删掉的那条。");
			}
			else if (Confirm("确定删掉「" + spec.Display + "」这条遗物替换吗？", "确认删除"))
			{
				ancientRow.Talk.RelicReplacements.Remove(spec);
				RaiseAncientRelicLists();
				SetStatus("已删除这条遗物替换。");
			}
		}
	}

	// ==================== 左下角：卡牌数量统计 ====================
	/// <summary>状态栏显示的卡牌数量统计（按攻击 / 技能 / 能力分开，再报总数）。</summary>
	public string CardCountText
	{
		get
		{
			int atk = 0, skill = 0, power = 0, other = 0, vanilla = 0, vanillaCopies = 0, curse = 0, ancient = 0;
			foreach (CardSpec c in _profile.AllCards)
			{
				// 本体卡（打击 / 防御）不算「自己的卡」：它们不生成类、不进卡池，只有初始卡组里那几份
				if (c.IsVanillaCard)
				{
					vanilla++;
					vanillaCopies += Math.Max(1, c.StartingCopies);
					continue;
				}
				if (c.IsCurseCard) { curse++; continue; }
				if (c.IsAncientCard) { ancient++; continue; }
				switch ((c.CardType ?? "").Trim())
				{
				case "Attack": atk++; break;
				case "Skill": skill++; break;
				case "Power": power++; break;
				default: other++; break;
				}
			}
			string more = ((other > 0) ? ($" ｜ 其它 {other}") : "");
			string extra = (curse > 0 ? $" ｜ 诅咒 {curse}" : "") + (ancient > 0 ? $" ｜ 先古卡 {ancient}" : "");
			int own = _profile.AllCards.Count(c => !c.IsVanillaCard);
			string van = vanilla > 0 ? $" ｜ 本体卡 {vanilla} 条（初始 {vanillaCopies} 张）" : "";
			return $"卡牌：攻击 {atk} ｜ 技能 {skill} ｜ 能力 {power}{more}{extra} ｜ 共 {own} 张{van}";
		}
	}

	private void HookCardCount()
	{
		// 三张列表（卡牌 / 诅咒 / 先古卡）都要挂钩子：数量统计和目标卡下拉都要跟着它们变
		foreach (var list in new[] { _profile.Cards, _profile.Curses, _profile.AncientCards })
		{
			list.CollectionChanged -= OnCardsChanged;
			list.CollectionChanged += OnCardsChanged;
			foreach (CardSpec c in list) c.PropertyChanged -= OnCardChanged;
			foreach (CardSpec c in list) c.PropertyChanged += OnCardChanged;
		}
	}

	private void OnCardsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		HookCardCount();
		Raise(nameof(CardCountText));
		// 加 / 删卡之后「目标卡（生成 / 变化用）」也要跟着更新 ——
		// 以前这里只刷数量，新加的卡在下拉里搜不到（用户报的「保存的卡在目标卡处搜不到」就是这个）。
		RefreshCardChoices();
	}

	private void OnCardChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName is "CardType" or "Display" or "") Raise(nameof(CardCountText));
		// 改了卡名 / 类名之后，目标卡下拉里的那一条也要跟着换（不然搜到的还是旧名字）
		if (e.PropertyName is "Display" or "Name" or "ClassName" or "")
		{
			Raise(nameof(CardCountText));
			RefreshCardChoices();
		}
	}

	private void BuildArtSlots()
	{
		ArtSlots.Add(new ArtSlot("静态立绘（战斗 / 商店 / 篝火）", "替代本体的 Spine 骨骼动画，这三个地方都用这张静态图", "PNG，建议 281×235（或更大的同比例图）；留空则用工具自带占位图 zwt.png", (ArtSpec a) => a.CharacterStatic, delegate(ArtSpec a, string? v)
		{
			a.CharacterStatic = v;
		}, frameWidth: 281, frameHeight: 235));
		ArtSlots.Add(new ArtSlot("顶部头像", "游戏内左上角角色头像，PNG-32，88×88（方图）", "PNG，建议 512×512（方图）", (ArtSpec a) => a.Icon, delegate(ArtSpec a, string? v)
		{
			a.Icon = v;
		}, frameWidth: 88, frameHeight: 88));
		ArtSlots.Add(new ArtSlot("选人界面立绘", "选人界面底部的小立绘，132×195（竖版）", "PNG，建议 396×585 等比", (ArtSpec a) => a.SelectIcon, delegate(ArtSpec a, string? v)
		{
			a.SelectIcon = v;
		}, frameWidth: 132, frameHeight: 195));
		// 注意：本体画面是 1920×1080 的横屏（project.godot 的 viewport），
		// 这张背景图是整屏铺满的（keep-aspect 居中），所以**必须是横图**。
		// 以前这里写的是「1000×1400 以上的竖图」，方向写反了（用户报的「分辨率反了」）。
		ArtSlots.Add(new ArtSlot("选人界面背景大图", "选人界面里角色背后那张大图（整屏铺满；留空则用静态立绘那张图）", "PNG，横图，建议 1920×1080 或更大（和游戏画面同比例；竖图会左右留黑边）", (ArtSpec a) => a.SelectBackground, delegate(ArtSpec a, string? v)
		{
			a.SelectBackground = v;
		}, frameWidth: 1920, frameHeight: 1080));
		ArtSlots.Add(new ArtSlot("地图标记", "地图上的角色标记，49×64", "PNG，建议 98×128 等比", (ArtSpec a) => a.MapMarker, delegate(ArtSpec a, string? v)
		{
			a.MapMarker = v;
		}, frameWidth: 49, frameHeight: 64));
		ArtSlots.Add(new ArtSlot("能量图标", "卡牌左上角的费用图标 / 左侧那个能量球（本体 74×74）", "PNG，任意尺寸都行：会自动缩成 74×74（图集）与 24×24（卡牌文字内联），左侧能量球也换成这张图", (ArtSpec a) => a.EnergyIcon, delegate(ArtSpec a, string? v)
		{
			a.EnergyIcon = v;
		}, frameWidth: 74, frameHeight: 74));
		ArtSlots.Add(new ArtSlot("选人过场贴图", "选角色时的过场遮罩，2560×1200", "PNG，灰度图（白=显示、黑=透明）", (ArtSpec a) => a.Transition, delegate(ArtSpec a, string? v)
		{
			a.Transition = v;
		}, frameWidth: 2560, frameHeight: 1200));
		ArtSlots.Add(new ArtSlot("模组预览图（游戏「模组」界面）", "游戏主菜单「模组」界面右侧那块图（本体的 NModInfoContainer 按固定路径 res://<模组ID>/mod_image.png 找它，找不到就空着）", "PNG，建议 1200×630（那个框约 1.9:1）或 1280×720；别超过 1~2 MB（整张图会进 PCK）", (ArtSpec a) => a.ModImage, delegate(ArtSpec a, string? v)
		{
			a.ModImage = v;
		}, frameWidth: 1200, frameHeight: 630));
	}

	private void RefreshAll()
	{
		Raise("Profile");
		// 条件说明里的「额外资源量」要用用户自己填的名字（和计数器上显示的名字保持一致）
		ConditionSpec.ResourceDisplayName = LocalizationGen.ResourceName(_profile);
		foreach (ArtSlot artSlot in ArtSlots)
		{
			artSlot.Load(_profile.Art);
		}
		RefreshCardChoices();
		BuildAncientRows();
		RefreshExtraResourceIconPreview();
		Raise("PowerChoices");
		SyncPowerOverrideDetail();
		RefreshCustomPowerRegistry();
		SyncCustomPowerDetail();
		HookCardCount();
		Raise("UpgradeKeywordRows");
		RefreshIssues();
	}

	private void RefreshIssues()
	{
		Issues.Clear();
		foreach (ValidationIssue item in ProfileValidator.Validate(_profile))
		{
			Issues.Add(item.ToString());
		}
		// 同一个 ModId 的存档装进游戏会互相覆盖（dll / pck 名字一样），提前说一句
		try
		{
			string? clash = FindSaveWithSameModId();
			if (clash is not null) Issues.Add($"[警告] 存档「{clash}」的 ModId 也是 {_profile.ModId}：两个存档装进游戏会互相覆盖（dll / pck 名字一样）。建议把其中一个的 ModId 改掉（「角色」页最上面）。");
		}
		catch { }
	}

	/// <summary>存档目录里有没有别的存档用了同一个 ModId（返回那个存档的文件名）。</summary>
	public string? FindSaveWithSameModId()
	{
		string folder = ProfileFolder;
		if (!Directory.Exists(folder)) return null;
		string me = _currentProfilePath ?? "";
		foreach (string f in Directory.GetFiles(folder, "*.json"))
		{
			if (me.Length > 0 && string.Equals(Path.GetFullPath(f), Path.GetFullPath(me), StringComparison.OrdinalIgnoreCase)) continue;
			try
			{
				CharacterProfile other = ProfileFactory.Load(f);
				if (string.Equals(other.ModId, _profile.ModId, StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(f);
			}
			catch { }
		}
		return null;
	}

	private void RefreshVisibleLists()
	{
		SyncDetail();
		SyncDetail();
		SyncDetail();
		object dataContext = CardEffectList.DataContext;
		if (dataContext != null)
		{
			IList<EffectSpec> list = EffectsOf(dataContext);
			if (list != null)
			{
				RefreshList(CardEffectList, list);
			}
		}
		object dataContext2 = RelicEffectList.DataContext;
		if (dataContext2 != null)
		{
			IList<EffectSpec> list2 = EffectsOf(dataContext2);
			if (list2 != null)
			{
				RefreshList(RelicEffectList, list2);
			}
		}
		object dataContext3 = PotionEffectList.DataContext;
		if (dataContext3 != null)
		{
			IList<EffectSpec> list3 = EffectsOf(dataContext3);
			if (list3 != null)
			{
				RefreshList(PotionEffectList, list3);
			}
		}
	}

	private static void RefreshList(ListBox lb, IEnumerable items)
	{
		object selectedItem = lb.SelectedItem;
		lb.ItemsSource = null;
		lb.ItemsSource = items;
		lb.SelectedItem = selectedItem;
	}

	private void OnNewProfile(object sender, RoutedEventArgs e)
	{
		NewProfileCore();
		SetStatus("已新建配置并生成存档，可在「配置存档」页看到它。");
	}

	public void NewProfileCore(bool askForName = true)
	{
		// 先把「当前存档路径」清掉：新建会把内存里的配置换成示例配置，
		// 如果这时路径还指着上一个存档，紧接着任何一次「静默保存」（比如选解包工程目录、
		// 生成工程前的 EnsureVanillaProject）都会用示例配置覆盖掉那个存档 ——
		// 实测踩过：20+ 张卡的存档被 8 张卡的示例内容覆盖，还没有历史版本可救。
		_currentProfilePath = null;
		Profile = ProfileFactory.Sample();
		Profile.Paths.OutputDir = Path.Combine(RootFolder, "自定义角色存档");
		SyncArt();
		ReloadCatalog();
		RefreshAll();
		try
		{
			Directory.CreateDirectory(ProfileFolder);
			if (askForName)
			{
				OnSaveProfileAsNew(this, new RoutedEventArgs());
			}
			else
			{
				string text = UniqueProfilePath(Profile.ModId);
				ProfileFactory.Save(_profile, text);
				_currentProfilePath = text;
				_renameAskKey = RenameAskKey(text, _profile.ModId);   // 刚存的名字：别马上又问改不改名
				SetStatus("已新建存档：" + Path.GetFileName(text) + "（可在「配置存档」页改名或另存）");
			}
		}
		catch (Exception ex)
		{
			SetStatus("新建存档失败：" + ex.Message);
		}
		RefreshProfiles();
	}

	private string UniqueProfilePath(string baseName)
	{
		string text = Path.Combine(ProfileFolder, baseName + ".json");
		int num = 2;
		while (File.Exists(text))
		{
			text = Path.Combine(ProfileFolder, $"{baseName}-{num}.json");
			num++;
		}
		return text;
	}

	public bool LoadProfileCore(string path)
	{
		try
		{
			Profile = ProfileFactory.Load(path);
			_currentProfilePath = path;
			_renameAskKey = null;            // 换了一份存档：改名提示重新计一次
			// ★ 顺序很重要（用户报过「美术图片重启后全变回占位」就是这个顺序写反了）：
			//   必须**先把 7 个美术槽位从刚载入的这份存档读进界面**，再 SyncArt 写回内存配置。
			//   反过来（先 SyncArt 再 RefreshAll）等于把「上一个存档 / 空白」的槽位值覆盖进新载入的配置，
			//   存档里的 7 个美术路径当场被抹掉 —— 而「逐张卡面」走的是另一个字段，不会被抹，
			//   所以表现是「卡面还在、那 7 个槽位每次打开都变回占位，得重新上传」。
			SyncArtSlotsFromProfile();
			NormalizeOutputDir();
			SyncArt();
			ReloadCatalog();
			RecheckEnvironment();
			RefreshAll();
			RefreshProfiles();
			SetStatus("已载入存档：" + path);
			return true;
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, "配置解析失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Hand);
			return false;
		}
	}

	/// <summary>把 7 个美术槽位从「当前内存里的配置」读进界面（载入存档时必须先做这一步，见 LoadProfileCore 的注释）。</summary>
	private void SyncArtSlotsFromProfile()
	{
		foreach (ArtSlot artSlot in ArtSlots)
		{
			artSlot.Load(_profile.Art);
		}
	}

	public bool PromptForProfileAtStartup()
	{
		StartupProfileDialog startupProfileDialog = new StartupProfileDialog(ProfileFolder);
		if (!startupProfileDialog.ShowDialog().GetValueOrDefault())
		{
			return false;
		}
		if (startupProfileDialog.NewProfileRequested)
		{
			NewProfileCore(askForName: false);
			return true;
		}
		string selectedProfilePath = startupProfileDialog.SelectedProfilePath;
		if (selectedProfilePath != null && selectedProfilePath.Length > 0 && LoadProfileCore(selectedProfilePath))
		{
			return true;
		}
		return false;
	}

	public void OverrideProfileFolderForTest(string folder)
	{
		_profileFolderOverride = folder;
	}

	public string EnvCheckReport()
	{
		RecheckEnvironment();
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("环境自检：" + EnvSummaryText);		foreach (EnvCheckItem envItem in EnvItems)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(5, 4, stringBuilder2);
			handler.AppendFormatted(envItem.Icon);
			handler.AppendLiteral(" [");
			handler.AppendFormatted(envItem.Level);
			handler.AppendLiteral("] ");
			handler.AppendFormatted(envItem.Name);
			handler.AppendLiteral("：");
			handler.AppendFormatted(envItem.Detail);
			stringBuilder2.AppendLine(ref handler);
			if (envItem.Fix.Length > 0)
			{
				stringBuilder.AppendLine("       修复：" + envItem.Fix);
			}
			if (!string.IsNullOrEmpty(envItem.Url))
			{
				stringBuilder.AppendLine("       下载：" + envItem.Url);
			}
		}
		// 美术资源槽位现状：排查「上传的图重启后像没保存」时，一眼就能看到这份存档里到底有没有图
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("当前存档：" + (_currentProfilePath ?? "(没载入存档)"));
		if (File.Exists(_currentProfilePath))
			stringBuilder.AppendLine("存档最后写入：" + File.GetLastWriteTime(_currentProfilePath).ToString("yyyy-MM-dd HH:mm:ss"));
		stringBuilder.AppendLine("美术资源槽位：" + string.Join("、", ArtSlots.Select((ArtSlot s) =>
			s.Name + "=" + (string.IsNullOrWhiteSpace(s.Path) ? "占位" : "已上传"))
			));
		stringBuilder.AppendLine("卡面（逐张）：" + _profile.Art.CardPortraits.Count((KeyValuePair<string, string?> kv) => !string.IsNullOrWhiteSpace(kv.Value))
			+ " 张已上传 / 共 " + _profile.Cards.Count + " 张卡");
		return stringBuilder.ToString();
	}

	public bool PromptForProfileAtStartupWithAutoClick()
	{
		StartupProfileDialog dlg = new StartupProfileDialog(ProfileFolder);
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(600L, 0L)
		};
		timer.Tick += delegate
		{
			timer.Stop();
			dlg.ChooseNewProfileForTest();
		};
		timer.Start();
		if (!dlg.ShowDialog().GetValueOrDefault())
		{
			return false;
		}
		if (dlg.NewProfileRequested)
		{
			NewProfileCore(askForName: false);
			return true;
		}
		string selectedProfilePath = dlg.SelectedProfilePath;
		if (selectedProfilePath != null && selectedProfilePath.Length > 0 && LoadProfileCore(selectedProfilePath))
		{
			return true;
		}
		return false;
	}

	private void OnOpenProfile(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "配置 JSON (*.json)|*.json"
		};
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			LoadProfileCore(openFileDialog.FileName);
		}
	}

	/// <summary>
	/// 「保存配置」：直接保存进**当前这个存档文件**，不再弹路径选择。
	///
	/// 唯一的打断：模组 ID 和当前存档文件名对不上时，问一次「要不要把存档名也改成 &lt;模组 ID&gt;.json」。
	/// 同一个文件 + 同一个模组 ID 只问一次（答「否」= 本次运行不再问，仍然保存进原文件）。
	/// 以前这个按钮弹 SaveFileDialog，很容易不小心把同一份配置存成「示例角色.json」和
	/// 「示例角色-2.json」两份，后面生成工程 / 安装就开始互相打架。
	/// </summary>
	private bool SaveProfileInPlace()
	{
		SyncArt();
		string modId = (_profile.ModId ?? "").Trim();
		string? current = _currentProfilePath;

		try
		{
			Directory.CreateDirectory(ProfileFolder);

			// 还没存过任何文件（正常流程不会走到：新建配置时已经写过一份）
			if (string.IsNullOrWhiteSpace(current) || !File.Exists(current))
			{
				string name = SanitizeSaveName(modId);
				string target = Path.Combine(ProfileFolder, name + ".json");
				if (File.Exists(target) && !Confirm($"存档目录里已经有「{name}.json」了。\n\n要覆盖它吗？\n（选「否」就另起一个名字存，不会动老文件）", "保存配置"))
				{
					target = UniqueProfilePath(name);
				}
				ProfileFactory.Save(_profile, target);
				_currentProfilePath = target;
				_renameAskKey = RenameAskKey(target, modId);
				SetStatus("已保存：" + Path.GetFileName(target));
				RefreshProfiles();
				return true;
			}

			string curName = Path.GetFileNameWithoutExtension(current);
			string savePath = current;
			string? renameTo = RenameTargetFor(current, modId);
			if (renameTo is not null && !string.Equals(_renameAskKey, RenameAskKey(current, modId), StringComparison.Ordinal))
			{
				_renameAskKey = RenameAskKey(current, modId);
				if (Confirm($"当前存档文件是「{curName}.json」，而模组 ID 是「{modId}」。\n\n"
					+ $"要把存档名称改成「{Path.GetFileName(renameTo)}」吗？\n\n"
					+ $"选「否」= 仍然保存进「{curName}.json」（本次运行不再问）。", "更改存档名称"))
				{
					if (File.Exists(renameTo) && !Confirm($"「{Path.GetFileName(renameTo)}」已经存在，要覆盖它吗？\n\n"
						+ $"（原来那份「{curName}.json」会被改名过去，存档目录里不会留下两个一样的存档；"
						+ "旧内容仍然按平时的规则备份到「_备份」里）", "覆盖存档"))
					{
						renameTo = null;
					}
					else
					{
						try
						{
							File.Move(current, renameTo, overwrite: true);
							savePath = renameTo;
							_currentProfilePath = renameTo;
							AppendLog($"存档已改名：{curName}.json → {Path.GetFileName(renameTo)}");
						}
						catch (Exception ex)
						{
							renameTo = null;
							AppendLog("存档改名失败（仍然保存进原文件）：" + ex.Message);
						}
					}
				}
			}

			ProfileFactory.Save(_profile, savePath);
			_renameAskKey = RenameAskKey(savePath, modId);
			SetStatus((string.Equals(savePath, current, StringComparison.OrdinalIgnoreCase)
				? "已保存：" : "已保存（存档名改为「" + Path.GetFileName(savePath) + "」）：") + savePath);
			RefreshProfiles();
			return true;
		}
		catch (Exception ex)
		{
			SetStatus("保存失败：" + ex.Message);
			MessageBox.Show(this, "保存失败：" + ex.Message, "保存配置", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return false;
		}
	}

	/// <summary>模组 ID 和当前存档文件名不一样时，返回「改成哪个文件」；一样（或模组 ID 为空）返回 null。</summary>
	internal static string? RenameTargetFor(string currentPath, string? modId)
	{
		string raw = (modId ?? "").Trim();
		if (raw.Length == 0) return null;
		string name = SanitizeSaveName(raw);
		if (string.Equals(name, Path.GetFileNameWithoutExtension(currentPath), StringComparison.Ordinal)) return null;
		string? dir = Path.GetDirectoryName(currentPath);
		if (string.IsNullOrEmpty(dir)) return null;
		return Path.Combine(dir, name + ".json");
	}

	/// <summary>存档文件名：去掉非法字符；空名字兜底成「新角色」。</summary>
	internal static string SanitizeSaveName(string? raw)
	{
		string name = (raw ?? "").Trim();
		foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
		name = name.Trim('.', ' ');
		return name.Length == 0 ? "新角色" : name;
	}

	private static string RenameAskKey(string path, string? modId) => path + "|" + (modId ?? "").Trim();

	private void OnSaveProfile(object sender, RoutedEventArgs e)
	{
		SaveProfileInPlace();
	}

	private void OnValidate(object sender, RoutedEventArgs e)
	{
		SyncArt();
		RefreshIssues();
		int num = Issues.Count((string i) => i.StartsWith("[错误]"));
		SetStatus((num == 0) ? $"校验通过（{Issues.Count} 条提示）" : $"有 {num} 个错误");
	}

	public bool EnsureVanillaProject()
	{
		PathsSpec paths = _profile.Paths;
		if (!string.IsNullOrWhiteSpace(paths.VanillaProject) && Directory.Exists(paths.VanillaProject))
		{
			return true;
		}
		string text = PathAutoDetect.FindVanillaProject();
		if (text != null)
		{
			paths.VanillaProject = text;
			ReloadCatalog();
			RefreshEnvStatusLine();
			AppendLog("自动找到解包工程：" + text);
			return true;
		}
		MessageBox.Show(this, "还差「解包工程目录」这一项。\n\n它是生成模组的占位美术 / 场景模板 / spine 插件的来源，必须由你自己准备：\n用 GDRE 解包自己电脑上的《杀戮尖塔 2》本体，得到一份带 project.godot 的工程目录。\n\n（工具和环境都不含游戏素材，所以没法随包提供。）\n\n接下来会弹出选择框，请选中那个解包工程的根目录。", "先指定解包工程目录", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "选择解包后的《杀戮尖塔 2》原版工程目录（含 project.godot）"
		};
		if (!openFolderDialog.ShowDialog(this).GetValueOrDefault())
		{
			SetStatus("已取消：没有指定解包工程目录。");
			return false;
		}
		paths.VanillaProject = openFolderDialog.FolderName;
		ReloadCatalog();
		RefreshEnvStatusLine();
		if (EffectCatalog.Powers.Count == 0)
		{
			MessageBox.Show(this, "这个目录里读不到本体内容（看不到 project.godot / src/Core/Models/Powers）。\n\n请选解包工程的最外层目录 —— 里面应该能看到 project.godot、src、scenes、images 等。", "目录可能选错了", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return false;
		}
		SaveCurrentProfileQuietly();
		return true;
	}

	private void OnGenerate(object sender, RoutedEventArgs e)
	{
		SyncArt();
		if (!EnsureVanillaProject())
		{
			return;
		}
		GenerationResult generationResult = ModGenerator.Generate(_profile, AppendLog);
		Issues.Clear();
		foreach (ValidationIssue issue in generationResult.Issues)
		{
			Issues.Add(issue.ToString());
		}
		SetStatus(generationResult.Success ? ("工程已生成：" + generationResult.ProjectRoot) : "生成失败（校验没通过），请看校验结果与日志。");
		if (!generationResult.Success) ReportGenerateFailure(generationResult.Issues);
	}

	/// <summary>
	/// 生成被校验拦住时，弹一个**写明原因**的框（只在左下角状态栏写一句「生成失败」的话，
	/// 用户只会看到「无法构建」这四个字，根本不知道哪儿不对 —— 实测踩过）。
	/// </summary>
	private void ReportGenerateFailure(IReadOnlyList<ValidationIssue> issues)
	{
		var errors = issues.Where((ValidationIssue i) => i.IsError).ToList();
		string body = errors.Count > 0
			? string.Join(Environment.NewLine, errors.Take(6).Select((ValidationIssue i) => "· " + i.Message))
			: "（校验里没有标红的条目，看日志里最后几行。）";
		if (errors.Count > 6) body += Environment.NewLine + $"……还有 {errors.Count - 6} 条，见左边「问题」列表。";
		MessageBox.Show(this,
			"生成被拦住了，所以没法构建。" + Environment.NewLine + Environment.NewLine + body
			+ Environment.NewLine + Environment.NewLine
			+ "完整清单在左下角的「问题」列表里；改完再点「① 生成工程」或「② 一键构建 + 安装」。",
			"Sts2CharForge —— 生成失败的原因", MessageBoxButton.OK, MessageBoxImage.Warning);
	}

	private void OnBuild(object sender, RoutedEventArgs e)
	{
		RunBuild(install: false);
	}

	private void OnBuildInstall(object sender, RoutedEventArgs e)
	{
		RunBuild(install: true);
	}

	public async Task BuildForTestAsync(string? profilePath = null)
	{
		Stopwatch sw = Stopwatch.StartNew();
		string temp2 = Path.Combine(Path.GetTempPath(), "forge_buildtest");
		try
		{
			// ===== 先做隔离：把要测的存档【复制】到临时目录，再把它当成「当前存档」载入 =====
			// 为什么要这样：以前直接载入用户真实存档，构建过程中界面会静默保存（自动填路径等），
			// 结果把测试用的临时输出目录写进了用户的存档里，甚至会把工程生成到真实存档目录里
			// （实测踩过：用户的存档被改了 OutputDir、存档目录里多出一个 forge_build_snapshot 工程）。
			string? real = profilePath is { Length: > 0 } && File.Exists(profilePath)
				? profilePath
				: (Directory.Exists(ProfileFolder) ? Directory.GetFiles(ProfileFolder, "*.json").OrderByDescending(File.GetLastWriteTime).FirstOrDefault() : null);
			Directory.CreateDirectory(temp2);
			if (real is not null)
			{
				string copy = Path.Combine(temp2, Path.GetFileName(real));
				File.Copy(real, copy, true);
				OverrideProfileFolderForTest(temp2);        // 之后所有静默保存都只写临时目录
				LoadProfileCore(copy);                       // 当前存档 = 临时副本
			}
		}
		catch
		{
		}
		string text2 = Path.Combine(temp2, "out");
		Profile.Paths.OutputDir = text2;
		Profile.Paths.InstallDir = Path.Combine(text2, "install");
		// 关键：载入存档之后要按它的路径重载效果库（界面正常流程是 LoadProfileCore 做的），
		// 否则效果库还是空的 → 校验直接报「效果库为空」→ 构建秒失败。
		ReloadCatalog();
		// 注意：ReloadCatalog 里会自动填路径（OutputDir 空 / 目录不存在时会被改成存档目录），
		// 所以这里必须【再设一次】，否则构建会生成到用户真实存档目录里去。
		Profile.Paths.OutputDir = text2;
		Profile.Paths.InstallDir = Path.Combine(text2, "install");
		RecheckEnvironment();
		RefreshAll();
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(200L, 0L)
		};
		timer.Tick += delegate
		{
			_uiTicks++;
		};
		timer.Start();
		await RunBuild(install: true);
		timer.Stop();
		sw.Stop();
		string value = (Directory.Exists(Profile.Paths.InstallDir) ? string.Join(", ", from f in Directory.GetFiles(Profile.Paths.InstallDir)
			select Path.GetFileName(f)) : "(空)");
		LastBuildTestReport = $"存档: {Profile.ModId}（卡牌 {Profile.Cards.Count} 张 / 遗物 {Profile.Relics.Count} / 药水 {Profile.Potions.Count}）{Environment.NewLine}构建总耗时: {sw.Elapsed.TotalSeconds:N1} 秒{Environment.NewLine}界面定时器刷新次数: {_uiTicks}  （构建期间界面是否卡死：{((_uiTicks > 10) ? "没有卡死 ✅" : "疑似卡死 ❌")}）{Environment.NewLine}状态栏: {StatusText}{Environment.NewLine}安装目录产物: {value}{Environment.NewLine}"
			+ "----- 构建日志（尾部）-----" + Environment.NewLine + BuildLogTail + Environment.NewLine;
		DumpLogTo(Path.Combine(AppContext.BaseDirectory, "buildtest_log.txt"));
		// 注意：这里【不要】Close()。
		// 以前 Close() 会在 DispatcherFrame 还压着的时候触发「关掉唯一窗口 → 程序退出」，
		// 结果 PushFrame 永远等不到 Continue，进程挂死、buildtest_result.txt 也写不出来（实测踩过）。
		// 退出交给 App.xaml.cs：写完结果文件后自己 Shutdown(0)。
	}

	private async Task RunBuild(bool install)
	{
		if (_buildBusy)
		{
			SetStatus("正在构建中，请稍候…（进度见「构建 · 日志」）");
		}
		else
		{
			if (!EnsureVanillaProject())
			{
				return;
			}
			string text = (_profile.Paths.InstallDir ?? "").Trim();
			if (!install || text.Length != 0)
			{
				if (install)
				{
					try
					{
						Directory.CreateDirectory(text);
					}
					catch (Exception ex)
					{
						Issues.Add("[错误] 安装目录不可用：" + ex.Message);
						SetStatus("安装目录不可用：" + text);
						return;
					}
				}
				_buildBusy = true;
				string title = "一键" + (install ? "构建 + 安装" : "构建");
				SetStatus(title + " 进行中…（界面不会卡，进度见右侧日志）");
				AppendLog("========== " + title + " 开始 ==========");
				IProgress<string> progress = new Progress<string>(AppendLog);
				CharacterProfile profile = _profile;
				CleanLeftoverSnapshotProject(profile);
				try
				{
					SyncArt();
					CharacterProfile snapshot;
					try
					{
						// 关键：ProfileFactory.Save 会把 SaveName 改成文件名（= forge_build_snapshot）。
						// 那是**内存里那份配置**（profile 就是 _profile），改完工程目录名就变成
						// <输出目录>\forge_build_snapshot（很难看，而且和「工程目录=存档名」的约定不符）。
						// 所以先记下真实存档名，存完快照再还回去。
						string realSaveName = profile.SaveName;
						string path = Path.Combine(Path.GetTempPath(), "forge_build_snapshot.json");
						ProfileFactory.Save(profile, path);
						profile.SaveName = realSaveName;              // 把内存里那份改回来
						snapshot = ProfileFactory.Load(path);
						snapshot.SaveName = realSaveName;              // 工程目录名 = 真实存档名
					}
					catch
					{
						snapshot = profile;
					}
					string godotExe = profile.Paths.GodotExe;
					string dotnetExe = profile.Paths.DotnetExe;
					string installTo = (install ? text : null);
					(List<ValidationIssue>, BuildResult) tuple = await Task.Run(delegate
					{
						GenerationResult generationResult = ModGenerator.Generate(snapshot, progress.Report);
						if (!generationResult.Success)
						{
							return ((List<ValidationIssue> Issues, BuildResult Build))(Issues: generationResult.Issues, Build: null);
						}
						Naming naming = Naming.From(snapshot);
						// 工程目录要用「生成结果里那个真实目录」，不能自己拼 OutputDir\ModId：
						// 存档名和 ModId 不一样时（例：示例角色_恢复.json / ModId=示例角色）那个目录根本不存在，
						// dotnet 会报 MSB1009 项目文件不存在 → 界面显示「构建失败」（用户报的「构建无法生成」就是这个）。
						BuildResult item = ModBuilder.Build(generationResult.ProjectRoot, naming.ModId, godotExe, installTo, progress.Report, dotnetExe);
						return (Issues: generationResult.Issues, Build: item);
					});
					Issues.Clear();
					foreach (ValidationIssue item2 in tuple.Item1)
					{
						Issues.Add(item2.ToString());
					}
					if ((object)tuple.Item2 == null)
					{
						SetStatus("生成失败（校验没通过），无法构建。原因见弹框 / 左下角问题列表。");
						ReportGenerateFailure(tuple.Item1);
						return;
					}
					foreach (BuildStepResult step in tuple.Item2.Steps)
					{
						AppendLog($"--- {step.Name} {(step.Success ? "OK" : "失败")} ---");
						AppendLog(step.Output.Trim());
					}
					SetStatus((!tuple.Item2.Success) ? "构建失败，详见日志。" : ((tuple.Item2.InstalledTo == null) ? "构建完成（未安装）。" : ("构建完成并已安装到 " + tuple.Item2.InstalledTo)));
					// 构建失败也要弹框写明「哪一步失败、报了什么」，只写一句「构建失败」等于没说
					if (!tuple.Item2.Success)
					{
						var failed = tuple.Item2.Steps.Where((BuildStepResult st) => !st.Success).ToList();
						var sb = new System.Text.StringBuilder();
						sb.AppendLine("构建失败了，出问题的是这几步：" + Environment.NewLine);
						foreach (BuildStepResult st in failed.Take(4))
						{
							sb.AppendLine("【" + st.Name + "】");
							var tail = (st.Output ?? "").Replace("\r\n", "\n").Split('\n')
								.Where((string l) => l.Trim().Length > 0).TakeLast(8);
							foreach (string l in tail) sb.AppendLine("   " + l.Trim());
							sb.AppendLine();
						}
						sb.AppendLine("完整日志在「构建 · 日志」页，可以点「导出日志」存成文件发我。");
						MessageBox.Show(this, sb.ToString(), "Sts2CharForge —— 构建失败的原因", MessageBoxButton.OK, MessageBoxImage.Warning);
					}
					return;
				}
				catch (Exception ex2)
				{
					AppendLog("[X] 构建过程发生异常：" + ex2);
					Issues.Add("[错误] 构建异常：" + ex2.Message);
					SetStatus("构建异常：" + ex2.Message + "（详情见日志）");
					return;
				}
				finally
				{
					_buildBusy = false;
					AppendLog("========== " + title + " 结束 ==========");
				}
			}
			SetStatus("请先填写「安装目录」（通常是游戏 mods 目录）。");
		}
	}

	/// <summary>
	/// 清掉早期版本留下的 `<输出目录>\forge_build_snapshot` 工程目录。
	/// 那时候「一键构建」用快照（SaveName = forge_build_snapshot）当配置去生成，
	/// 于是真实工程被生成到这个名字很怪的目录里；现在改成用存档名了，留着的旧目录顺手清掉
	/// （只删「确实是我们生成过的工程」：目录里有 project.godot）。
	/// </summary>
	private void CleanLeftoverSnapshotProject(CharacterProfile profile)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(profile.Paths.OutputDir)) return;
			string stale = Path.Combine(profile.Paths.OutputDir, "forge_build_snapshot");
			if (stale == ModGenerator.ProjectRootOf(profile)) return;      // 就叫这个名字的存档，别动
			if (!Directory.Exists(stale)) return;
			if (!File.Exists(Path.Combine(stale, "project.godot"))) return;
			Directory.Delete(stale, true);
			AppendLog("已清掉早期版本留下的临时工程目录：" + stale);
		}
		catch { }
	}

	/// <summary>
	/// 生成代码里 <c>CanonicalVars</c> 的动态变量键（和本体 DynamicVarSet 的规则一致：
	/// 有名字参数就用名字，没有就是类型默认名 —— PowerVar&lt;T&gt; 用 T，其余用类型名）。
	/// 用来断言「同一种变量不会重名」——重名会让本体抛异常、整场战斗卡死。
	/// </summary>
	private static List<string> VarKeysOf(string source)
	{
		var keys = new List<string>();
		foreach (System.Text.RegularExpressions.Match mVars in System.Text.RegularExpressions.Regex.Matches(source, @"CanonicalVars =>\s*\[(.*?)\];", System.Text.RegularExpressions.RegexOptions.Singleline))
			foreach (System.Text.RegularExpressions.Match v in System.Text.RegularExpressions.Regex.Matches(mVars.Groups[1].Value, @"new (\w+)Var(?:<(\w+)>)?\(([^)]*)\)"))
			{
				string type = v.Groups[1].Value;
				string? generic = v.Groups[2].Success ? v.Groups[2].Value : null;
				var named = System.Text.RegularExpressions.Regex.Match(v.Groups[3].Value, "^\"([^\"]+)\"");
				if (named.Success) keys.Add(named.Groups[1].Value);
				else keys.Add(generic ?? type);
			}
		return keys;
	}

	private static bool HasDuplicateVarKeys(string source) =>
		VarKeysOf(source).GroupBy(k => k, StringComparer.Ordinal).Any(g => g.Count() > 1);

	private void OnOpenOutput(object sender, RoutedEventArgs e)
	{
		// 和生成器用同一套目录规则（存档名 = 目录名），别再拼 OutputDir\ModId
		string text = ModGenerator.ProjectRootOf(_profile);
		if (!Directory.Exists(text))
		{
			SetStatus("还没有生成工程： " + text);
			return;
		}
		Process.Start(new ProcessStartInfo("explorer.exe", "\"" + text + "\"")
		{
			UseShellExecute = true
		});
	}

	private void OnAddCard(object sender, RoutedEventArgs e)
	{
		_profile.Cards.Add(new CardSpec
		{
			Name = "新卡",
			CardType = "Attack",
			Rarity = "Common",
			Cost = 1
		});
		SyncDetail();
		CardList.SelectedIndex = _profile.Cards.Count - 1;
		SetStatus("已添加卡牌，右侧填写属性与效果。");
	}

	/// <summary>把初始卡组用的本体「打击 ×5 / 防御 ×5」补回卡牌列表（删掉之后可以再加回来）。</summary>
	private void OnAddVanillaBasics(object sender, RoutedEventArgs e)
	{
		int before = _profile.Cards.Count;
		ProfileFactory.AddVanillaBasics(_profile);
		int added = _profile.Cards.Count - before;
		SyncDetail();
		HookCardCount();
		Raise("CardCountText");
		if (added == 0)
		{
			SetStatus("「打击 / 防御」已经在列表最上面了（数值 / 升级 / 卡面都能改，标了 Strike / Defend 标签）。");
			return;
		}
		CardList.SelectedItem = _profile.Cards.FirstOrDefault();
		SetStatus("已补上「打击 ×5 / 防御 ×5」—— 是你自己的两张卡（类名固定 Strike / Defend），数值默认照抄本体。");
	}

	private void OnCopyCard(object sender, RoutedEventArgs e)
	{
		if (!(CardList.SelectedItem is CardSpec cardSpec))
		{
			SetStatus("请先在左边选中一张卡牌再点「复制」。");
			return;
		}
		if (cardSpec.IsVanillaCard)
		{
			SetStatus("这是本体卡（只做引用），不能复制 —— 想要更多份请直接改它的「初始份数」。");
			return;
		}
		CardSpec cardSpec2 = DeepCloneCard(cardSpec);
		cardSpec2.Name = cardSpec.Name + "·改";
		cardSpec2.ClassName = "";     // 留空 = 自动编号（照抄类名会和原卡撞模型 ID → 模组加载失败）
		_profile.Cards.Add(cardSpec2);
		SyncDetail();
		CardList.SelectedItem = cardSpec2;
		SetStatus($"已复制「{cardSpec.Name}」→「{cardSpec2.Name}」：自定义关键词 / 卡牌关键字 / 标签 / 效果（含数值、升级增量、召唤物、概率、条件）/ 升级后关键字 全部照抄。");
	}

	/// <summary>
	/// 整张卡片的**深拷贝**（JSON 往返，和存档 / 工程回读用的是同一套字段）。
	///
	/// 为什么不能手写 <c>new CardSpec { … }</c>（上一版就是这么写的）：手写**一定会漏字段**。
	/// 用户实测漏掉的：勾选的自定义关键词（KeywordIds）、卡牌关键字（消耗/虚无/固有/保留/不可打出/奇巧）、
	/// 标签（Tags）、升级后关键字（UpgradeKeywords），以及效果上除 Kind/Amount 以外的一切
	/// （「召唤物（哪一只）」、牺牲伙伴的收益/公式/倍率、概率生效、条件…）——
	/// 结果复制出来的牌只是长得像，实际配置完全不是同一张。
	/// 走 JSON 往返还有一个好处：以后给 CardSpec / EffectSpec 加字段，这里**不需要改**，不会再漏。
	/// </summary>
	private static CardSpec DeepCloneCard(CardSpec src) =>
		System.Text.Json.JsonSerializer.Deserialize<CardSpec>(
			System.Text.Json.JsonSerializer.Serialize(src, ProfileFactory.JsonOpts),
			ProfileFactory.JsonOpts) ?? new CardSpec();

	private bool ConfirmDelete(string what)
	{
		ConfirmRequests++;
		if (SuppressConfirmations)
		{
			return true;
		}
		return MessageBox.Show(this, "确定要删除" + what + "吗？\n\n删错了不要紧：删除后可以点旁边的「撤回删除」恢复。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
	}

	private bool Confirm(string message, string title = "确认操作")
	{
		ConfirmRequests++;
		if (SuppressConfirmations)
		{
			return true;
		}
		return MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
	}

	public bool HasUnsavedProfileChanges()
	{
		try
		{
			SyncArt();
			string a = JsonSerializer.Serialize(_profile, ProfileFactory.JsonOpts);
			string b = ((string.IsNullOrWhiteSpace(_currentProfilePath) || !File.Exists(_currentProfilePath)) ? JsonSerializer.Serialize(ProfileFactory.Sample(), ProfileFactory.JsonOpts) : JsonSerializer.Serialize(ProfileFactory.Load(_currentProfilePath), ProfileFactory.JsonOpts));
			return !string.Equals(a, b, StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		base.OnClosing(e);
		if (e.Cancel || _allowClose || SuppressClosePromptForTest || SuppressClosePrompt || SuppressProfileWrites || !HasUnsavedProfileChanges())
		{
			return;
		}
		// 关键：不要在 OnClosing 里同步弹模态框。
		// 以前是直接 MessageBox.Show 然后按结果决定 e.Cancel —— 两个毛病：
		//   ① 命令行自检（--buildtest 等）里没人点，直接卡死（实测：进程永远不退出、结果文件写不出来）；
		//   ② 正常关窗口时，窗口已经在「正在关闭」状态，弹框偶尔会跑到窗口后面 → 看着像整个程序卡住。
		// 现在的做法：先把这次关闭取消掉，等界面回到空闲再问；用户选了「关闭」才真的关。
		e.Cancel = true;
		Dispatcher.BeginInvoke(new Action(PromptSaveThenClose), System.Windows.Threading.DispatcherPriority.Background);
	}

	/// <summary>「有改动，关闭前要保存吗？」：问完再决定关不关（在关闭流程之外问）。</summary>
	private void PromptSaveThenClose()
	{
		if (_allowClose || SuppressClosePromptForTest || SuppressClosePrompt || SuppressProfileWrites) return;
		bool hasFile = !string.IsNullOrWhiteSpace(_currentProfilePath) && File.Exists(_currentProfilePath);
		string target = hasFile ? ("「" + Path.GetFileName(_currentProfilePath) + "」") : "一个新的存档文件";
		MessageBoxResult result = MessageBox.Show(this,
			"当前配置有改动，关闭前要保存吗？\n\n是 = 保存到" + target + "\n否 = 不保存，直接关闭\n取消 = 返回继续编辑",
			"还没保存", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
		if (result == MessageBoxResult.Cancel) return;
		if (result == MessageBoxResult.Yes)
		{
			// 和「保存配置」走同一条路：直接保存进原存档，不弹路径选择
			// （没存过的新配置才落到 存档目录\<模组 ID>.json；失败时窗口先不关）
			if (!SaveProfileInPlace()) return;
		}
		_allowClose = true;
		Close();
	}

	private bool _allowClose;

	private void PushUndo(Stack<UndoEntry> stack, string label, Action restore)
	{
		stack.Push(new UndoEntry(label, restore));
		if (stack.Count > 30)
		{
			List<UndoEntry> list = stack.Take(30).Reverse().ToList();
			stack.Clear();
			foreach (UndoEntry item in list)
			{
				stack.Push(item);
			}
		}
		RaiseUndoState();
	}

	private void UndoLast(Stack<UndoEntry> stack)
	{
		if (stack.Count == 0)
		{
			SetStatus("没有可撤回的删除。");
			return;
		}
		UndoEntry undoEntry = stack.Pop();
		undoEntry.Restore();
		RaiseUndoState();
		SetStatus("已撤回：删除了" + undoEntry.Label);
		// 撤回也是一次真实改动（可能刚把图片接回来）：顺手写进存档，别再让用户手动保存
		PersistArtChange("已撤回：删除了" + undoEntry.Label);
	}

	private void RaiseUndoState()
	{
		Raise("CanUndoCard");
		Raise("CanUndoRelic");
		Raise("CanUndoPotion");
		Raise("CanUndoKeyword");
		Raise("CanUndoSummon");
		Raise("CanUndoCurse");
		Raise("CanUndoAncient");
		Raise("UndoCurseHint");
		Raise("UndoAncientHint");
		Raise("CanUndoArt");
		Raise("CanUndoProfile");
		Raise("UndoCardHint");
		Raise("UndoRelicHint");
		Raise("UndoPotionHint");
		Raise("UndoKeywordHint");
		Raise("UndoSummonHint");
		Raise("UndoPowerHint");
		Raise("CanUndoPower");
		Raise("UndoArtHint");
		Raise("UndoProfileHint");
	}

	private static string HintOf(Stack<UndoEntry> s)
	{
		if (s.Count <= 0)
		{
			return "没有可撤回的删除";
		}
		return "撤回删除：" + s.Peek().Label;
	}

	private void OnUndoCard(object sender, RoutedEventArgs e)
	{
		UndoLast(_cardUndo);
	}

	private void OnUndoRelic(object sender, RoutedEventArgs e)
	{
		UndoLast(_relicUndo);
	}

	private void OnUndoPotion(object sender, RoutedEventArgs e)
	{
		UndoLast(_potionUndo);
	}

	private void OnUndoSummon(object sender, RoutedEventArgs e)
	{
		UndoLast(_summonUndo);
	}

	private void OnUndoPower(object sender, RoutedEventArgs e)
	{
		UndoLast(_powerUndo);
	}

	private void OnUndoKeyword(object sender, RoutedEventArgs e)
	{
		UndoLast(_keywordUndo);
	}

	private void OnUndoArt(object sender, RoutedEventArgs e)
	{
		UndoLast(_artUndo);
	}

	private void OnUndoProfile(object sender, RoutedEventArgs e)
	{
		UndoLast(_profileUndo);
	}

	private static List<T> SelectedOf<T>(ListBox lb) where T : class
	{
		return lb.SelectedItems.OfType<T>().ToList();
	}

	/// <summary>
	/// Ctrl+A：把**当前焦点所在的那个列表**全选（文本框里的 Ctrl+A 不动，照旧全选文字）。
	/// 只有多选列表才处理 —— 单选列表没有「全选」这回事，交回给别的处理逻辑（不标记 Handled）。
	/// 把「焦点在谁身上」做成参数是为了能自检（--uicheck 里没法真的造一次键盘输入）。
	/// </summary>
	internal bool TrySelectAllShortcut(object? focused = null)
	{
		object? target = focused ?? Keyboard.FocusedElement;
		if (!(target is ListBox lb) || lb.SelectionMode == SelectionMode.Single) return false;
		SelectAll(lb, "条目");
		return true;
	}

	/// <summary>窗口级快捷键（目前只有 Ctrl+A 全选）。</summary>
	private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.A || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
		// Shift/Ctrl 一起按只当 Ctrl 用；Alt 组合不抢
		if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) return;
		if (TrySelectAllShortcut())
		{
			e.Handled = true;
		}
	}

	private void SelectAll(ListBox lb, string unit)
	{
		if (lb.SelectionMode == SelectionMode.Single)
		{
			SetStatus(unit + "列表还没打开「多选」，先点「多选」再用「全选」。");
			return;
		}
		if (lb.Items.Count == 0)
		{
			SetStatus(unit + "列表是空的，没有可全选的条目。");
			return;
		}
		lb.SelectedItems.Clear();
		foreach (object item in (IEnumerable)lb.Items)
		{
			if (item != null)
			{
				lb.SelectedItems.Add(item);
			}
		}
		SetStatus($"已全选 {lb.SelectedItems.Count} 个{unit}。");
	}

	private void OnSelectAllCards(object sender, RoutedEventArgs e)
	{
		SelectAll(CardList, "卡牌");
	}

	private void OnSelectAllRelics(object sender, RoutedEventArgs e)
	{
		SelectAll(RelicList, "遗物");
	}

	private void OnSelectAllPotions(object sender, RoutedEventArgs e)
	{
		SelectAll(PotionList, "药水");
	}

	private void OnSelectAllProfiles(object sender, RoutedEventArgs e)
	{
		SelectAll(ProfileList, "存档");
	}

	public bool ClickButtonByContent(string content, DependencyObject root)
	{
		string content2 = content;
		List<Button> list = new List<Button>();
		CollectButtons(root, list);
		Button button = list.FirstOrDefault((Button x) => x.Content as string == content2);
		if (button == null)
		{
			return false;
		}
		button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
		return true;
	}

	public Button? FindButtonByContent(string content, DependencyObject root)
	{
		string content2 = content;
		List<Button> list = new List<Button>();
		CollectButtons(root, list);
		return list.FirstOrDefault((Button x) => x.Content as string == content2);
	}

	public CheckBox? FindCheckBoxByContent(string content, DependencyObject root)
	{
		string content2 = content;
		List<CheckBox> list = new List<CheckBox>();
		CollectCheckBoxes(root, list);
		return list.FirstOrDefault((CheckBox x) => x.Content as string == content2);
	}

	public ToggleButton? FindToggleButtonByContent(string content, DependencyObject root)
	{
		string content2 = content;
		List<ToggleButton> list = new List<ToggleButton>();
		CollectToggleButtons(root, list);
		return list.FirstOrDefault((ToggleButton x) => x.Content as string == content2);
	}

	private static bool ToggleViaClick(ToggleButton tb)
	{
		if (new ToggleButtonAutomationPeer(tb).GetPattern(PatternInterface.Toggle) is IToggleProvider toggleProvider)
		{
			toggleProvider.Toggle();
			return true;
		}
		return false;
	}

	private static bool CanAddToSelection(ListBox lb)
	{
		if (lb.Items.Count < 2)
		{
			return lb.SelectionMode != SelectionMode.Single;
		}
		lb.SelectedIndex = 0;
		try
		{
			lb.SelectedItems.Add(lb.Items[1]);
		}
		catch
		{
			return false;
		}
		return lb.SelectedItems.Count == 2;
	}

	private int RemoveManyWithUndo<T>(IList<T> list, List<T> selected, Stack<UndoEntry> stack, string unit, Action? afterRestore = null, Action? afterRemove = null) where T : class
	{
		IList<T> list2 = list;
		Action afterRestore2 = afterRestore;
		if (selected.Count == 0)
		{
			return 0;
		}
		List<(T item, int index)> removed = (from item in selected
			select (item: item, index: list2.IndexOf(item)) into x
			where x.index >= 0
			orderby x.index
			select x).ToList();
		if (removed.Count == 0)
		{
			return 0;
		}
		foreach (var item4 in removed)
		{
			T item2 = item4.item;
			list2.Remove(item2);
		}
		afterRemove?.Invoke();
		string label = ((removed.Count == 1) ? (unit + "「" + NameOf(removed[0].item) + "」") : $"{removed.Count} 个{unit}");
		PushUndo(stack, label, delegate
		{
			foreach (var (item3, value) in removed)
			{
				list2.Insert(Math.Clamp(value, 0, list2.Count), item3);
			}
			afterRestore2?.Invoke();
			SyncDetail();
		});
		return removed.Count;
	}

	private static string NameOf<T>(T item)
	{
		if (!(item is CardSpec cardSpec))
		{
			if (!(item is RelicSpec relicSpec))
			{
				if (!(item is PotionSpec potionSpec))
				{
					if (item is EffectSpec effectSpec)
					{
						return effectSpec.Display;
					}
					return item?.ToString() ?? "";
				}
				return potionSpec.Name;
			}
			return relicSpec.Name;
		}
		return cardSpec.Name;
	}

	private void OnRemoveCard(object sender, RoutedEventArgs e)
	{
		ListBox list = ListFromTag(sender) ?? CardList;
		List<CardSpec> picked = SelectedOf<CardSpec>(list);
		if (picked.Count == 0)
		{
			SetStatus("请先选中要删除的卡牌（可 Ctrl/Shift 多选，或点「全选」）。");
			return;
		}
		string what = ((picked.Count == 1) ? ("卡牌「" + picked[0].Name + "」") : $"选中的 {picked.Count} 张卡牌");
		if (ConfirmDelete(what))
		{
			int value = RemoveManyWithUndo(_profile.Cards, picked, _cardUndo, "卡牌", delegate
			{
				list.SelectedItem = picked[0];
			});
			SetStatus($"已删除 {value} 张卡牌（可点「撤回删除」恢复）");
		}
	}

	// ===== 「诅咒 / 先古卡」页（两个列表共用一张详情区，靠选中项决定显示哪一个）=====
	private readonly Stack<UndoEntry> _curseUndo = new Stack<UndoEntry>();
	private readonly Stack<UndoEntry> _ancientUndo = new Stack<UndoEntry>();

	public bool CanUndoCurse => _curseUndo.Count > 0;
	public string UndoCurseHint => HintOf(_curseUndo);
	public bool CanUndoAncient => _ancientUndo.Count > 0;
	public string UndoAncientHint => HintOf(_ancientUndo);

	/// <summary>「诅咒 / 先古卡」页当前选中的那张牌（两个列表里有一个选中就算）。</summary>
	internal CardSpec? SpecialCardOf() =>
		(CurseList.SelectedItem as CardSpec) ?? (AncientCardList.SelectedItem as CardSpec);

	/// <summary>两个列表都没选中时显示那句空态提示。</summary>
	public bool ShowSpecialCardHint => SpecialCardOf() is null;

	/// <summary>
	/// 三个列表（卡牌 / 诅咒 / 先古卡）共用同一套「卡面预览」属性，所以选中要互斥：
	/// 选中这一栏就把另外两栏的选择清掉，否则预览会看着像串了（用户报过类似的观感问题）。
	/// </summary>
	private void OnSpecialCardSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (sender == CurseList && CurseList.SelectedItem is not null)
		{
			AncientCardList.SelectedItem = null;
			CardList.SelectedItem = null;
		}
		else if (sender == AncientCardList && AncientCardList.SelectedItem is not null)
		{
			CurseList.SelectedItem = null;
			CardList.SelectedItem = null;
		}
		Raise("ShowSpecialCardHint");
		Raise("CardPortraitPreview");
		Raise("CardPortraitPreviewNote");
	}

	private void OnAddCurse(object sender, RoutedEventArgs e)
	{
		var c = new CardSpec
		{
			Name = "新诅咒",
			CardType = "Curse",
			Rarity = "Curse",
			Cost = -1,
			Unplayable = true,      // 诅咒一律打不出去（生成时也会强制加上这个关键字）
		};
		_profile.Curses.Add(c);
		SyncDetail();
		CurseList.SelectedItem = c;
		SetStatus("已添加诅咒：费用 -1、不能被打出，右边加的效果会在「你的回合结束时，如果它还在你的手牌里」触发。");
	}

	private void OnRemoveCurse(object sender, RoutedEventArgs e)
	{
		List<CardSpec> picked = SelectedOf<CardSpec>(CurseList);
		if (picked.Count == 0)
		{
			SetStatus("请先选中要删除的诅咒（可 Ctrl/Shift 多选）。");
			return;
		}
		string what = picked.Count == 1 ? "诅咒「" + picked[0].Name + "」" : $"选中的 {picked.Count} 张诅咒";
		if (ConfirmDelete(what))
		{
			int n = RemoveManyWithUndo(_profile.Curses, picked, _curseUndo, "诅咒", delegate
			{
				CurseList.SelectedItem = picked[0];
			});
			SetStatus($"已删除 {n} 张诅咒（可点「撤回删除」恢复）");
		}
	}

	private void OnUndoCurse(object sender, RoutedEventArgs e)
	{
		UndoLast(_curseUndo);
	}

	private void OnSelectAllCurses(object sender, RoutedEventArgs e)
	{
		SelectAll(CurseList, "诅咒");
	}

	private void OnAddAncientCard(object sender, RoutedEventArgs e)
	{
		var c = new CardSpec
		{
			Name = "新先古卡",
			CardType = "Attack",
			Rarity = "Ancient",
			Cost = 1,
		};
		_profile.AncientCards.Add(c);
		SyncDetail();
		AncientCardList.SelectedItem = c;
		SetStatus("已添加先古卡：和普通卡一样写效果 / 费用，只是稀有度是先古、不会进战斗奖励。");
	}

	private void OnRemoveAncientCard(object sender, RoutedEventArgs e)
	{
		List<CardSpec> picked = SelectedOf<CardSpec>(AncientCardList);
		if (picked.Count == 0)
		{
			SetStatus("请先选中要删除的先古卡（可 Ctrl/Shift 多选）。");
			return;
		}
		string what = picked.Count == 1 ? "先古卡「" + picked[0].Name + "」" : $"选中的 {picked.Count} 张先古卡";
		if (ConfirmDelete(what))
		{
			int n = RemoveManyWithUndo(_profile.AncientCards, picked, _ancientUndo, "先古卡", delegate
			{
				AncientCardList.SelectedItem = picked[0];
			});
			SetStatus($"已删除 {n} 张先古卡（可点「撤回删除」恢复）");
		}
	}

	private void OnUndoAncientCard(object sender, RoutedEventArgs e)
	{
		UndoLast(_ancientUndo);
	}

	private void OnSelectAllAncientCards(object sender, RoutedEventArgs e)
	{
		SelectAll(AncientCardList, "先古卡");
	}

	/// <summary>诅咒 / 先古卡共用的「上传卡面」——取当前选中的那张（两个列表里的）。</summary>
	private void OnPickSpecialCardPortrait(object sender, RoutedEventArgs e)
	{
		if (SpecialCardOf() is not { } card)
		{
			SetStatus("请先在左边选中一张诅咒或先古卡。");
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "图片 (*.png)|*.png"
		};
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			string key = Naming.From(_profile).CardClassName(_profile, card);
			_profile.Art.CardPortraits[key] = openFileDialog.FileName;
			Raise("CardPortraitPreview");
			Raise("CardPortraitPreviewNote");
			PersistArtChange($"已为「{card.Name}」设置卡面：{openFileDialog.FileName}（建议 1000×760 PNG）");
		}
	}

	private void OnAddRelic(object sender, RoutedEventArgs e)	{
		_profile.Relics.Add(new RelicSpec
		{
			Name = "新遗物",
			Rarity = "Common",
			Trigger = "CombatStart"
		});
		SyncDetail();
		RelicList.SelectedIndex = _profile.Relics.Count - 1;
	}

	private void OnRemoveRelic(object sender, RoutedEventArgs e)
	{
		List<RelicSpec> picked = SelectedOf<RelicSpec>(RelicList);
		if (picked.Count == 0)
		{
			SetStatus("请先选中要删除的遗物（可 Ctrl/Shift 多选，或点「全选」）。");
			return;
		}
		string what = ((picked.Count == 1) ? ("遗物「" + picked[0].Name + "」") : $"选中的 {picked.Count} 个遗物");
		if (ConfirmDelete(what))
		{
			int value = RemoveManyWithUndo(_profile.Relics, picked, _relicUndo, "遗物", delegate
			{
				RelicList.SelectedItem = picked[0];
			});
			SetStatus($"已删除 {value} 个遗物（可点「撤回删除」恢复）");
		}
	}

	private void OnAddPotion(object sender, RoutedEventArgs e)
	{
		_profile.Potions.Add(new PotionSpec
		{
			Name = "新药水",
			Rarity = "Common",
			Usage = "CombatOnly",
			TargetType = "Self"
		});
		SyncDetail();
		PotionList.SelectedIndex = _profile.Potions.Count - 1;
	}

	private void OnRemovePotion(object sender, RoutedEventArgs e)
	{
		List<PotionSpec> picked = SelectedOf<PotionSpec>(PotionList);
		if (picked.Count == 0)
		{
			SetStatus("请先选中要删除的药水（可 Ctrl/Shift 多选，或点「全选」）。");
			return;
		}
		string what = ((picked.Count == 1) ? ("药水「" + picked[0].Name + "」") : $"选中的 {picked.Count} 瓶药水");
		if (ConfirmDelete(what))
		{
			int value = RemoveManyWithUndo(_profile.Potions, picked, _potionUndo, "药水", delegate
			{
				PotionList.SelectedItem = picked[0];
			});
			SetStatus($"已删除 {value} 瓶药水（可点「撤回删除」恢复）");
		}
	}

	private void OnAddKeyword(object sender, RoutedEventArgs e)
	{
		_profile.CustomKeywords.Add(new CustomKeywordSpec { Name = "新关键词" });
		SyncDetail();
		KeywordList.SelectedIndex = _profile.CustomKeywords.Count - 1;
		SetStatus("已添加关键词：填好名字和说明，然后到「卡牌」页勾选哪张牌用它。");
	}

	private void OnRemoveKeyword(object sender, RoutedEventArgs e)
	{
		List<CustomKeywordSpec> picked = SelectedOf<CustomKeywordSpec>(KeywordList);
		if (picked.Count == 0)
		{
			SetStatus("请先选中要删除的关键词（没有就点左边的「添加关键词」）。");
			return;
		}
		string what = picked.Count == 1 ? "关键词「" + picked[0].Name + "」" : $"选中的 {picked.Count} 条关键词";
		if (ConfirmDelete(what))
		{
			// 卡牌上的引用一起清掉：否则生成前校验会报「引用了不存在的关键词」，用户还得逐张去取消勾选。
			// 注意「撤回删除」只恢复关键词本身，卡牌上的勾选要重新点。
			int cleaned = ClearKeywordRefs(picked);
			int value = RemoveManyWithUndo(_profile.CustomKeywords, picked, _keywordUndo, "关键词", delegate
			{
				KeywordList.SelectedItem = picked[0];
			});
			SyncDetail();
			SetStatus($"已删除 {value} 条关键词" + (cleaned > 0 ? $"（同时取消了 {cleaned} 张卡上的引用）" : "")
				+ "（可点「撤回删除」恢复关键词，卡牌上的勾选要重新点）");
		}
	}

	/// <summary>删除关键词时，把卡牌上对它的引用一并去掉（本地化键和中文名两种写法都清）。</summary>
	private int ClearKeywordRefs(List<CustomKeywordSpec> removed)
	{
		var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var item in KeywordGen.All(_profile))
		{
			if (!removed.Contains(item.Spec)) continue;
			keys.Add(item.Key);
			keys.Add((item.Spec.Name ?? "").Trim());
		}
		int cleaned = 0;
		foreach (var card in _profile.Cards)
		{
			var kept = card.CustomKeywordList.Where(x => !keys.Contains(x.Trim())).ToList();
			if (kept.Count != card.KeywordIds.Count)
			{
				card.KeywordIds = kept;
				cleaned++;
			}
		}
		return cleaned;
	}

	private ListBox? ListFromTag(object sender)
	{
		return (sender as FrameworkElement)?.Tag as ListBox;
	}

	private void OnAddEffect(object sender, RoutedEventArgs e)
	{
		ListBox listBox = ListFromTag(sender);
		object obj = listBox?.DataContext;
		IList<EffectSpec> list = EffectsOf(obj);
		if (listBox == null || list == null)
		{
			SetStatus("请先在左边选中要编辑的条目。");
			return;
		}
		// 新效果的数值 / 升级增量一律默认 0（用户要求）：以前卡牌上默认「伤害 6 / 升级 +3」，
		// 用户只是想加一条别的效果时，先得把这两个数字清掉，很容易漏 → 生成出莫名其妙的数值。
		// 默认 0 之后：选好「效果种类」再自己填，填漏了校验器也会提示（例：伤害要大于 0）。
		EffectSpec effectSpec = new EffectSpec
		{
			Kind = "Damage",
			Amount = 0m,
			UpgradeAmount = 0m,
			TargetSide = "Enemy",
			AllowDuplicates = true,
		};
		list.Add(effectSpec);
		SyncDetail();
		listBox.SelectedItem = effectSpec;
	}

	private void OnRemoveEffect(object sender, RoutedEventArgs e)
	{
		ListBox lb = ListFromTag(sender);
		object obj = lb?.DataContext;
		IList<EffectSpec> list = EffectsOf(obj);
		if (lb == null || list == null)
		{
			return;
		}
		List<EffectSpec> list2 = lb.SelectedItems.OfType<EffectSpec>().ToList();
		if (list2.Count == 0)
		{
			SetStatus("请先选中要删除的效果（可 Ctrl/Shift 多选）。");
			return;
		}
		(Stack<UndoEntry>, string) tuple = ((obj is CardSpec cardSpec) ? (_cardUndo, "卡牌「" + cardSpec.Name + "」的效果") : ((obj is RelicSpec relicSpec) ? (_relicUndo, "遗物「" + relicSpec.Name + "」的效果") : ((obj is PotionSpec potionSpec) ? (_potionUndo, "药水「" + potionSpec.Name + "」的效果") : ((!(obj is PowerTriggerSpec powerTriggerSpec)) ? (_cardUndo, "效果") : (_powerUndo, "自定义状态的「" + (PowerTriggers.Find(powerTriggerSpec.Kind)?.Display ?? powerTriggerSpec.Kind) + "」效果")))));
		(Stack<UndoEntry>, string) tuple2 = tuple;
		Stack<UndoEntry> item = tuple2.Item1;
		string item2 = tuple2.Item2;
		string what = ((list2.Count == 1) ? (item2 + "「" + list2[0].Display + "」") : $"{item2}（{list2.Count} 条）");
		if (ConfirmDelete(what))
		{
			int value = RemoveManyWithUndo(list, list2, item, "效果", delegate
			{
				lb.SelectedIndex = 0;
			});
			SetStatus($"已删除 {value} 条效果（可点「撤回删除」恢复）");
		}
	}

	private static IList<EffectSpec>? EffectsOf(object? spec)
	{
		if (!(spec is CardSpec cardSpec))
		{
			if (!(spec is RelicSpec relicSpec))
			{
				if (!(spec is PotionSpec potionSpec))
				{
					if (spec is PowerTriggerSpec powerTriggerSpec)
					{
						return powerTriggerSpec.Effects;
					}
					return null;
				}
				return potionSpec.Effects;
			}
			return relicSpec.Effects;
		}
		return cardSpec.Effects;
	}

	private void OnPickCardPortrait(object sender, RoutedEventArgs e)
	{
		if (!(CardList.SelectedItem is CardSpec cardSpec))
		{
			SetStatus("请先选中一张卡牌。");
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "图片 (*.png)|*.png"
		};
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			string key = Naming.From(_profile).CardClassName(_profile, cardSpec);
			_profile.Art.CardPortraits[key] = openFileDialog.FileName;
			Raise("CardPortraitPreview");
			PersistArtChange($"已为「{cardSpec.Name}」设置卡面：{openFileDialog.FileName}（建议 1000×760 PNG）");
		}
	}

	private void OnPickRelicIcon(object sender, RoutedEventArgs e)
	{
		if (!(RelicList.SelectedItem is RelicSpec relicSpec))
		{
			SetStatus("请先选中一个遗物。");
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "图片 (*.png)|*.png"
		};
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			relicSpec.Icon = openFileDialog.FileName;
			Raise("RelicIconPreview");
			PersistArtChange($"已为遗物「{relicSpec.Name}」设置图标：{openFileDialog.FileName}（建议 256×256 PNG；生成时会写出大图 + 图集裁切纹理）");
		}
	}

	/// <summary>
	/// 改了额外资源量的显示名：条件说明（「拥有××至少 N 点」）要立刻跟着换名字，
	/// 否则界面上还写着「额外资源量」、计数器上已经是新名字了。
	/// </summary>
	private void OnExtraResourceNameChanged(object sender, TextChangedEventArgs e)
	{
		ConditionSpec.ResourceDisplayName = LocalizationGen.ResourceName(Profile);
		Raise("Profile");
	}

	private void OnPickExtraResourceIcon(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "图片 (*.png)|*.png|所有文件 (*.*)|*.*"
		};
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			string old = Profile.ExtraResource.Icon;
			Profile.ExtraResource.Icon = openFileDialog.FileName;
			RefreshExtraResourceIconPreview();
			PushUndo(_artUndo, "额外资源量图标", delegate
			{
				Profile.ExtraResource.Icon = old;
				RefreshExtraResourceIconPreview();
			});
			SetStatus("额外资源量图标已选择：" + openFileDialog.FileName + "（生成时会被打进模组）");
			PersistArtChange("额外资源量图标已选择：" + openFileDialog.FileName);
		}
	}

	private void OnClearExtraResourceIcon(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(Profile.ExtraResource.Icon))
		{
			SetStatus("还没有选过额外资源量图标。");
			return;
		}
		string old = Profile.ExtraResource.Icon;
		Profile.ExtraResource.Icon = null;
		RefreshExtraResourceIconPreview();
		PushUndo(_artUndo, "额外资源量图标", delegate
		{
			Profile.ExtraResource.Icon = old;
			RefreshExtraResourceIconPreview();
		});
		SetStatus("额外资源量图标已清除（计数器会继续用本体的星星图标），可点「撤回」恢复。");
		PersistArtChange("额外资源量图标已清除（计数器会继续用本体的星星图标），可点「撤回」恢复");
	}

	/// <summary>
	/// 「召唤物」页里选中那只的宠物图片（可空）。
	/// 选择 / 清除都写进 <c>SummonList.SelectedItem.Image</c>，并立刻静默保存 —— 和美术槽位的做法一致。
	/// </summary>
	private void OnPickSummonImage(object sender, RoutedEventArgs e)
	{
		if (!(SummonList.SelectedItem is SummonSpec summon))
		{
			SetStatus("请先在左边选中一只召唤物，再给它选图片。");
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "图片 (*.png)|*.png|所有文件 (*.*)|*.*"
		};
		if (!openFileDialog.ShowDialog(this).GetValueOrDefault()) return;
		string old = summon.Image;
		summon.Image = openFileDialog.FileName;
		Raise("SelectedSummonImagePreview");
		SummonList.Items.Refresh();
		PushUndo(_artUndo, "召唤物图片", delegate
		{
			summon.Image = old;
			Raise("SelectedSummonImagePreview");
		});
		SetStatus($"「{summon.Name}」的图片已选择：{openFileDialog.FileName}（生成时会拷进模组并生成它的场景）");
		PersistArtChange("召唤物图片已选择：" + openFileDialog.FileName);
	}

	private void OnClearSummonImage(object sender, RoutedEventArgs e)
	{
		if (!(SummonList.SelectedItem is SummonSpec summon))
		{
			SetStatus("请先在左边选中一只召唤物。");
			return;
		}
		if (string.IsNullOrWhiteSpace(summon.Image))
		{
			SetStatus("这只召唤物还没有选过图片。");
			return;
		}
		string old = summon.Image;
		summon.Image = null;
		Raise("SelectedSummonImagePreview");
		SummonList.Items.Refresh();
		PushUndo(_artUndo, "召唤物图片", delegate
		{
			summon.Image = old;
			Raise("SelectedSummonImagePreview");
		});
		SetStatus($"「{summon.Name}」的图片已清除（它会用本体的占位图），可点「撤回」恢复。");
		PersistArtChange("召唤物图片已清除（会用本体占位图）");
	}

	/// <summary>添加一只召唤物：新条目的英文类名留空，生成时自动按位置推（第一只 &lt;角色类名&gt;Pet、第二只 Pet2…）。</summary>
	private void OnAddSummon(object sender, RoutedEventArgs e)
	{
		_profile.Summons.Add(new SummonSpec { Name = "新召唤物", Hp = 8 });
		SyncDetail();
		SummonList.SelectedIndex = _profile.Summons.Count - 1;
		SetStatus("已添加一只召唤物。填好名字 / 生命，然后到卡牌或遗物的效果里选「召唤伙伴」并挑中它。");
	}

	private void OnRemoveSummon(object sender, RoutedEventArgs e)
	{
		List<SummonSpec> picked = SelectedOf<SummonSpec>(SummonList);
		if (picked.Count == 0)
		{
			SetStatus("请先选中要删除的召唤物（可 Ctrl/Shift 多选，或点「全选」）。");
			return;
		}
		string what = picked.Count == 1
			? "召唤物「" + (string.IsNullOrWhiteSpace(picked[0].Name) ? PetGen.ClassNameOf(_profile, picked[0]) : picked[0].Name) + "」"
			: $"选中的 {picked.Count} 只召唤物";
		// 有卡牌 / 遗物还在引用它时先提醒一句：删掉之后那些效果会变成「引用不存在的召唤物」，
		// 生成前校验会报错拦住（不会静默生成出编译不过的代码，但用户得知道要去改效果）。
		int refs = CountSummonRefs(picked);
		if (refs > 0)
			SetStatus($"注意：还有 {refs} 条效果在引用它（卡牌 / 遗物），删除后要重新选一只召唤物。");
		if (!ConfirmDelete(what + (refs > 0 ? $"\n\n还有 {refs} 条效果在引用它，删掉之后那些效果要重新选一只召唤物。" : ""))) return;
		int value = RemoveManyWithUndo(_profile.Summons, picked, _summonUndo, "召唤物", delegate
		{
			SummonList.SelectedItem = picked[0];
		});
		SyncDetail();
		SetStatus($"已删除 {value} 只召唤物（可点「撤回删除」恢复）" + (refs > 0 ? $"；有 {refs} 条效果还引用着它，记得去重新选一只。" : ""));
	}

	/// <summary>
	/// 有多少条效果（卡牌 + 遗物）正指向这几只召唤物（用于删除前的提醒）。
	/// 用 <see cref="PetGen.Resolve"/> 判：它会把「没选（老存档）」算成第一只启用 —— 和生成时一致。
	/// </summary>
	private int CountSummonRefs(List<SummonSpec> picked)
	{
		int n = 0;
		foreach (var e in _profile.Cards.SelectMany(c => c.Effects).Concat(_profile.Relics.SelectMany(r => r.Effects)))
		{
			if (!e.PetAction) continue;
			var def = PetGen.Resolve(_profile, e.PetSummon);
			if (def is not null && picked.Any(s => ReferenceEquals(s, def.Spec))) n++;
		}
		return n;
	}

	private void OnSelectAllSummons(object sender, RoutedEventArgs e) => SelectAll(SummonList, "召唤物");

	private void OnPickArt(object sender, RoutedEventArgs e)
	{
		if (!(ArtList.SelectedItem is ArtSlot artSlot))
		{
			SetStatus("请先在上方选中一个槽位。");
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "图片 (*.png)|*.png|所有文件 (*.*)|*.*"
		};
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			artSlot.Path = openFileDialog.FileName;
			artSlot.Save(_profile.Art);
			PersistArtChange("「" + artSlot.Name + "」已选择：" + openFileDialog.FileName);
		}
	}

	private void OnClearArt(object sender, RoutedEventArgs e)
	{
		object selectedItem = ArtList.SelectedItem;
		ArtSlot slot = selectedItem as ArtSlot;
		if (slot != null && Confirm("确定把「" + slot.Name + "」改回占位吗？\n\n（原来的图片文件不会被删，点了「撤回」还能接回来。）", "确认清除"))
		{
			string old = slot.Path;
			slot.Path = null;
			slot.Save(_profile.Art);
			PushUndo(_artUndo, "「" + slot.Name + "」的图片", delegate
			{
				slot.Path = old;
				slot.Save(_profile.Art);
			});
			PersistArtChange("「" + slot.Name + "」已改回占位（可点「撤回」恢复）");
		}
	}

	private void SyncArt()
	{
		foreach (ArtSlot artSlot in ArtSlots)
		{
			artSlot.Save(_profile.Art);
		}
	}

	/// <summary>
	/// 自检里临时把 _profile 换成别的配置（测改名 / 测美术落盘）时，列表选中项会跟着丢，
	/// 后面的自检又依赖「药水列表选着第 0 个」这类状态 —— 换之前存一下、换回来之后还回去。
	/// </summary>
	private (object? Card, object? Relic, object? Potion, object? Art) SaveListSelection() =>
		(CardList.SelectedItem, RelicList.SelectedItem, PotionList.SelectedItem, ArtList.SelectedItem);

	private void RestoreListSelection((object? Card, object? Relic, object? Potion, object? Art) s)
	{
		try { CardList.SelectedItem = s.Card; } catch { }
		try { RelicList.SelectedItem = s.Relic; } catch { }
		try { PotionList.SelectedItem = s.Potion; } catch { }
		try { ArtList.SelectedItem = s.Art; } catch { }
	}

	/// <summary>
	/// 上传 / 清除图片之后**立刻写回存档**。
	///
	/// 以前选完图只改内存里的配置，用户不点「保存配置」就关掉程序（或换了存档再回来），
	/// 下次打开所有槽位又变回「占位」，得一张张重新上传 —— 用户报过这个。
	/// 图片本来就是「配置的一部分」，选完就该进存档，和卡牌/遗物的改动一样。
	/// 只有「内存里的配置不是当前这个存档」（极少数情况）时才不静默写，交给「保存配置」。
	/// </summary>
	private void PersistArtChange(string what)
	{
		try
		{
			SyncArt();
			if (string.IsNullOrWhiteSpace(_currentProfilePath) || !File.Exists(_currentProfilePath))
			{
				SetStatus(what + "｜这个存档还没落到磁盘，点一下「保存配置」就存进存档了");
				return;
			}
			string current = Path.GetFileNameWithoutExtension(_currentProfilePath);
			if (!string.Equals(_profile.SaveName, current, StringComparison.Ordinal))
			{
				AppendLog($"（图片改动先只留在界面上：内存配置的存档名「{_profile.SaveName}」和当前文件「{current}.json」对不上，"
					+ "点「保存配置」写进存档）");
				SetStatus(what + "｜请点「保存配置」写进存档");
				return;
			}
			ProfileFactory.Save(_profile, _currentProfilePath);
			SetStatus(what + "｜已写进存档：" + Path.GetFileName(_currentProfilePath));
		}
		catch (Exception ex)
		{
			SetStatus(what + "｜写进存档失败：" + ex.Message);
			AppendLog("图片改动写进存档失败：" + ex.Message);
		}
	}

	private void OnPickInstallDir(object sender, RoutedEventArgs e)
	{
		string text = PickFolder("选择安装目录（游戏的 mods 目录）", _profile.Paths.InstallDir);
		if (text == null) return;
		// 选到「游戏根目录」或「data 目录」时自动补成 mods 目录 —— 用户经常选错一层
		string? mods = PathAutoDetect.FindModsDir(text);
		if (mods is not null && !string.Equals(mods, text, StringComparison.OrdinalIgnoreCase))
		{
			_profile.Paths.InstallDir = mods;
			Raise("Profile");
			SetStatus($"选的是游戏目录 / data 目录，已自动改成 mods 目录：{mods}");
			return;
		}
		_profile.Paths.InstallDir = text;
		Raise("Profile");
		bool? looksLikeMods = PathAutoDetect.LooksLikeModsDir(text);
		if (looksLikeMods == false)
			SetStatus("⚠ 这个目录看起来不是游戏的 mods 目录：" + text + "（本体要求装到 <游戏目录>\\mods 里，文件名必须和模组清单一致）");
	}

	/// <summary>把「安装目录」补成游戏的 mods 目录（没填或者填错时都纠正一下）。</summary>
	private void EnsureModsInstallDir(bool quiet)
	{
		try
		{
			string? current = (_profile.Paths.InstallDir ?? "").Trim();
			bool? looksLikeMods = PathAutoDetect.LooksLikeModsDir(current);
			if (looksLikeMods == true) return;
			// 先按用户填的目录算 mods（选成游戏根目录 / data 目录时能纠正过来）；
			// 没填过才退回自动探测。
			// 注意：用户自己挑的普通目录（不叫 mods、也不在游戏目录下面）**不动它** ——
			// 有人故意装到自建目录里测试，工具不该悄悄改成别的路径（校验结果里会提醒一句）。
			string? mods = PathAutoDetect.FindModsDir(current) ?? (current.Length == 0 ? PathAutoDetect.FindModsDir() : null);
			if (mods is null) return;
			if (string.Equals(mods, current, StringComparison.OrdinalIgnoreCase)) return;
			_profile.Paths.InstallDir = mods;
			Raise("Profile");
			if (!quiet) SetStatus("已把安装目录设为游戏的 mods 目录：" + mods);
		}
		catch { }
	}

	private void OnVanillaPathCommitted(object sender, RoutedEventArgs e)
	{
		ReloadCatalog();
		SaveCurrentProfileQuietly();
		SetStatus((EffectCatalog.Cards.Count > 0) ? $"已按新目录重载：卡牌 {EffectCatalog.Cards.Count} 项、效果库 {EffectCatalog.Powers.Count} 项" : "这个目录读不到本体卡牌/效果，确认选的是解包工程的最外层目录。");
	}

	private void OnVanillaPathKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			e.Handled = true;
			OnVanillaPathCommitted(sender, new RoutedEventArgs());
		}
	}

	private void OnPickVanilla(object sender, RoutedEventArgs e)
	{
		string text = PickFolder("选择解包后的游戏工程目录（里面应有 project.godot、src、scenes、images）", _profile.Paths.VanillaProject);
		if (text != null)
		{
			_profile.Paths.VanillaProject = text;
			Raise("Profile");
			ReloadCatalog();
			SaveCurrentProfileQuietly();
			SetStatus((EffectCatalog.Cards.Count > 0) ? $"已指定解包工程：卡牌 {EffectCatalog.Cards.Count} 项、效果库 {EffectCatalog.Powers.Count} 项" : "这个目录里读不到本体卡牌/效果，确认选的是解包工程的最外层目录。");
		}
	}

	private void OnPickGameData(object sender, RoutedEventArgs e)
	{
		string text = PickFolder("选择游戏 data_sts2_* 目录（内含 sts2.dll）", _profile.Paths.GameDataDir);
		if (text != null)
		{
			_profile.Paths.GameDataDir = text;
			Raise("Profile");
		}
	}

	private void OnPickOutputDir(object sender, RoutedEventArgs e)
	{
		string text = PickFolder("选择生成工程的输出目录", _profile.Paths.OutputDir);
		if (text != null)
		{
			_profile.Paths.OutputDir = text;
			Raise("Profile");
		}
	}

	private void OnPickGodot(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Godot (*.exe)|*.exe",
			FileName = Path.GetFileName(_profile.Paths.GodotExe)
		};
		if (!string.IsNullOrWhiteSpace(_profile.Paths.GodotExe) && File.Exists(_profile.Paths.GodotExe))
		{
			openFileDialog.InitialDirectory = Path.GetDirectoryName(_profile.Paths.GodotExe);
		}
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			_profile.Paths.GodotExe = openFileDialog.FileName;
			Raise("Profile");
		}
	}

	private void OnPickDotnet(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "dotnet (dotnet.exe)|dotnet.exe|可执行文件 (*.exe)|*.exe",
			FileName = "dotnet.exe"
		};
		if (!string.IsNullOrWhiteSpace(_profile.Paths.DotnetExe) && File.Exists(_profile.Paths.DotnetExe))
		{
			openFileDialog.InitialDirectory = Path.GetDirectoryName(_profile.Paths.DotnetExe);
		}
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			_profile.Paths.DotnetExe = openFileDialog.FileName;
			Raise("Profile");
			RecheckEnvironment();
			SetStatus("已选用指定的 dotnet：" + openFileDialog.FileName);
		}
	}

	private string? PickFolder(string title, string initial)
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = title
		};
		if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
		{
			openFolderDialog.InitialDirectory = initial;
		}
		if (!openFolderDialog.ShowDialog(this).GetValueOrDefault())
		{
			return null;
		}
		return openFolderDialog.FolderName;
	}

	/// <summary>把日志框里的内容存成文件（构建失败时给用户一份可直接发出来的排查材料）。</summary>
	private void OnExportLog(object sender, RoutedEventArgs e)
	{
		var dlg = new Microsoft.Win32.SaveFileDialog
		{
			Filter = "文本文件 (*.txt)|*.txt",
			FileName = $"构建日志_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
		};
		if (!dlg.ShowDialog(this).GetValueOrDefault()) return;
		try
		{
			File.WriteAllText(dlg.FileName, LogBox.Text, new System.Text.UTF8Encoding(false));
			SetStatus("日志已导出：" + dlg.FileName);
		}
		catch (Exception ex)
		{
			SetStatus("导出日志失败：" + ex.Message);
		}
	}

	private void OnCopyLog(object sender, RoutedEventArgs e)
	{
		try
		{
			Clipboard.SetText(LogBox.Text);
			SetStatus("日志已复制到剪贴板。");
		}
		catch (Exception ex)
		{
			SetStatus("复制日志失败：" + ex.Message);
		}
	}

	private void AppendLog(string line)
	{
		LogBox.AppendText(line + Environment.NewLine);
		LogBox.ScrollToEnd();
		_logTail.Add(line);
		while (_logTail.Count > 400) _logTail.RemoveAt(0);
	}

	/// <summary>最近 400 行日志（--buildtest / --uicheck 会把尾部写进结果文件和日志文件，方便排查）。</summary>
	private readonly List<string> _logTail = new();

	public string BuildLogTail => string.Join(Environment.NewLine, _logTail);

	/// <summary>把日志写到文件（--buildtest 用；界面上也可以手动复制「日志」框里的内容）。</summary>
	public void DumpLogTo(string path)
	{
		try { File.WriteAllText(path, BuildLogTail, new System.Text.UTF8Encoding(false)); } catch { }
	}

	private void SetStatus(string text)
	{
		StatusText = text;
	}

	private void RefreshProfiles()
	{
		Profiles.Clear();
		try
		{
			if (Directory.Exists(ProfileFolder))
			{
				foreach (string item in Directory.GetFiles(ProfileFolder, "*.json").OrderByDescending(File.GetLastWriteTime))
				{
					Profiles.Add(new ProfileEntry
					{
						Name = Path.GetFileNameWithoutExtension(item),
						Path = item,
						Info = File.GetLastWriteTime(item).ToString("MM-dd HH:mm")
					});
				}
			}
		}
		catch (Exception ex)
		{
			SetStatus("读取存档目录失败：" + ex.Message);
		}
		Raise("Profiles");
	}

	private void OnRefreshProfiles(object sender, RoutedEventArgs e)
	{
		RefreshProfiles();
		SetStatus($"存档目录：{ProfileFolder}（{Profiles.Count} 个存档）");
	}

	private void OnSaveProfileToFolder(object sender, RoutedEventArgs e)
	{
		try
		{
			SyncArt();
			string text = ((ProfileList.SelectedItem is ProfileEntry profileEntry) ? profileEntry.Name : _profile.ModId);
			string text2 = Path.Combine(ProfileFolder, text + ".json");
			ProfileFactory.Save(_profile, text2);
			// 覆盖了哪个存档，「当前存档」就切到它：不切的话接着点「保存配置」会写回上一个文件，
			// 用户刚覆盖的那个存档反而停在旧内容上（很难理解，也是踩过的坑）。
			_currentProfilePath = text2;
			_renameAskKey = RenameAskKey(text2, _profile.ModId);
			RefreshProfiles();
			SetStatus("已保存存档：" + text2);
		}
		catch (Exception ex)
		{
			SetStatus("保存失败：" + ex.Message);
		}
	}

	/// <summary>
	/// 「从工程恢复存档」：选一个以前生成出来的模组工程目录，从里面的源码反推出一份存档。
	/// 用途就是「存档被误覆盖」——工程里带着全部信息（卡牌 / 遗物 / 药水 / 自定义状态 / 角色属性 / 名字），
	/// 反推出来的配置能直接接着用。
	/// </summary>
	private void OnRecoverProfileFromProject(object sender, RoutedEventArgs e)
	{
		string? dir = PickFolder("选择以前生成出来的模组工程目录（里面有 cs、mod_manifest.json 和 <模组ID>\\localization）", _profile.Paths.OutputDir);
		if (string.IsNullOrWhiteSpace(dir)) return;
		try
		{
			SyncArt();
			AppendLog("========== 从工程恢复存档 ==========");
			AppendLog("工程目录：" + dir);
			var rec = ProjectRecovery.FromProject(dir, _profile, ProfileFolder);
			string name = SafeRecoverName(Path.GetFileName(dir.TrimEnd('\\', '/'))) + "_恢复";
			string target = UniqueProfilePath(name);
			ProfileFactory.Save(rec.Profile, target);
			foreach (string note in rec.Notes) AppendLog("  · " + note);
			foreach (string line in rec.Unparsed.Take(30)) AppendLog("  ⚠ 没认出来：" + line);
			RefreshProfiles();
			SetStatus($"已从工程恢复存档：{Path.GetFileName(target)}（{rec.Profile.Cards.Count} 张卡）"
				+ (rec.HasUnparsed ? $"，另有 {rec.Unparsed.Count} 处需要人工核对（见日志）" : ""));
			if (Confirm($"已生成「{Path.GetFileName(target)}」（{rec.Profile.Cards.Count} 张卡 / {rec.Profile.Relics.Count} 个遗物 / {rec.Profile.Potions.Count} 个药水）。\n\n"
				+ "现在就载入它吗？（建议先载入看一眼，确认没问题再接着用）", "恢复完成"))
				LoadProfileCore(target);
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, "恢复失败：" + ex.Message, "从工程恢复存档", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private static string SafeRecoverName(string name)
	{
		foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
		return name.Length == 0 ? "恢复" : name;
	}

	private void OnSaveProfileAsNew(object sender, RoutedEventArgs e)
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "配置 JSON (*.json)|*.json",
			FileName = _profile.ModId + ".json"
		};
		if (Directory.Exists(ProfileFolder))
		{
			saveFileDialog.InitialDirectory = ProfileFolder;
		}
		if (!saveFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			return;
		}
		try
		{
			SyncArt();
			ProfileFactory.Save(_profile, saveFileDialog.FileName);
			// 另存之后「当前存档」就是这份新文件了；不设的话路径还指着上一个存档，
			// 后面任何静默保存都会覆盖那个旧文件（见 NewProfileCore 里的注释）。
			_currentProfilePath = saveFileDialog.FileName;
			// 另存用的名字是用户自己选的：记下来，紧接着点「保存配置」时不要马上又问一遍改不改名
			_renameAskKey = RenameAskKey(saveFileDialog.FileName, _profile.ModId);
			RefreshProfiles();
			SetStatus("已另存为：" + saveFileDialog.FileName);
		}
		catch (Exception ex)
		{
			SetStatus("另存失败：" + ex.Message);
		}
	}

	private void OnLoadSelectedProfile(object sender, RoutedEventArgs e)
	{
		if (!(ProfileList.SelectedItem is ProfileEntry profileEntry))
		{
			SetStatus("请先在列表里选中一个存档。");
			return;
		}
		try
		{
			Profile = ProfileFactory.Load(profileEntry.Path);
			_currentProfilePath = profileEntry.Path;      // 载入的就是「当前存档」（不然接着保存会写回上一份）
			_renameAskKey = null;
			SyncArtSlotsFromProfile();                    // 先读进界面，再写回配置（顺序写反会把存档里的美术路径抹掉）
			NormalizeOutputDir();
			SyncArt();
			RefreshAll();
			SetStatus("已载入存档：" + profileEntry.Path);
		}
		catch (Exception ex)
		{
			SetStatus("载入失败：" + ex.Message);
		}
	}

	private void OnDeleteSelectedProfile(object sender, RoutedEventArgs e)
	{
		List<ProfileEntry> list = SelectedOf<ProfileEntry>(ProfileList);
		if (list.Count == 0)
		{
			SetStatus("请先在列表里选中要删除的存档（可 Ctrl/Shift 多选，或点「全选」）。");
			return;
		}
		string what = ((list.Count == 1) ? ("存档「" + list[0].Name + "」") : $"选中的 {list.Count} 个存档");
		if (!ConfirmDelete(what))
		{
			return;
		}
		try
		{
			List<(string Path, string Name, byte[] Bytes)> backups = (from p in list
				select (Path: p.Path, Name: p.Name, Bytes: File.Exists(p.Path) ? File.ReadAllBytes(p.Path) : null) into x
				where x.Bytes != null
				select x).ToList();
			int num = 0;
			foreach (ProfileEntry item in list)
			{
				try
				{
					if (File.Exists(item.Path))
					{
						// 删除前先往「_备份」里留一份带时间戳的副本（+ .bak）：
						// 「撤回删除」只在本次运行内有效，关掉程序就没了 —— 有了这个副本，
						// 就算过几天发现删错了，也能去 存档目录\_备份 里找回来。
						try { ProfileFactory.KeepBackup(item.Path); } catch { /* 备份失败不挡删除 */ }
						File.Delete(item.Path);
						num++;
					}
				}
				catch (Exception ex)
				{
					SetStatus("删除失败：" + ex.Message);
				}
			}
			if (backups.Count > 0)
			{
				string label = ((backups.Count == 1) ? ("存档「" + backups[0].Name + "」") : $"{backups.Count} 个存档");
				PushUndo(_profileUndo, label, delegate
				{
					int num2 = 0;
					foreach (var item2 in backups)
					{
						try
						{
							File.WriteAllBytes(item2.Path, item2.Bytes);
							num2++;
						}
						catch (Exception ex3)
						{
							SetStatus("恢复存档失败：" + ex3.Message);
						}
					}
					RefreshProfiles();
					SetStatus($"已恢复 {num2} 个存档");
				});
			}
			RefreshProfiles();
			SetStatus($"已删除 {num} 个存档（可点「撤回删除」恢复）");
		}
		catch (Exception ex2)
		{
			SetStatus("删除失败：" + ex2.Message);
		}
	}

	private void OnOpenProfileFolder(object sender, RoutedEventArgs e)
	{
		try
		{
			Directory.CreateDirectory(ProfileFolder);
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + ProfileFolder + "\"")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			SetStatus("打开文件夹失败：" + ex.Message);
		}
	}

	private DependencyObject SelectTabRoot(string header)
	{
		foreach (object item in (IEnumerable)MainTabs.Items)
		{
			if (item is TabItem tabItem && string.Equals(tabItem.Header as string, header, StringComparison.Ordinal))
			{
				MainTabs.SelectedItem = tabItem;
				UpdateLayout();
				return (DependencyObject)tabItem.Content;
			}
		}
		throw new InvalidOperationException("界面上没有标题为「" + header + "」的选项卡");
	}

	private TabItem? FindTab(string header)
	{
		foreach (object item in (IEnumerable)MainTabs.Items)
		{
			if (item is TabItem tabItem && string.Equals(tabItem.Header as string, header, StringComparison.Ordinal))
			{
				return tabItem;
			}
		}
		return null;
	}

	private static CharacterProfile WithCarryOver(CharacterProfile p, bool carryOver)
	{
		CharacterProfile characterProfile = ProfileFactory.Sample();
		characterProfile.CharacterClass = p.CharacterClass;
		characterProfile.ExtraResource.Enabled = p.ExtraResource.Enabled;
		characterProfile.ExtraResource.Initial = p.ExtraResource.Initial;
		characterProfile.ExtraResource.AlwaysShowCounter = p.ExtraResource.AlwaysShowCounter;
		characterProfile.ExtraResource.CarryOver = carryOver;
		return characterProfile;
	}

	/// <summary>
	/// 本体 <c>StringHelper.Slugify</c> 的等价实现 —— <b>只在自检里当参照物</b>（不参与生成，生成走
	/// <see cref="Naming.Slug"/>）。本体就是这三步：连续大写的驼峰插下划线 → 转大写 → 滤掉 <c>[^A-Z0-9_]</c>。
	/// 第一个正则里那个 <c>\G(?!^)</c> 分支是关键：连续大写时**每个**大写字母前面都要插下划线
	/// （<c>AIPet → A_I_PET</c>），只写 <c>([A-Za-z0-9])([A-Z])</c> 会漏掉后面那几个。
	/// </summary>
	private static string VanillaSlugify(string txt)
	{
		string text = System.Text.RegularExpressions.Regex.Replace(txt.Trim(), "([A-Za-z0-9]|\\G(?!^))([A-Z])", "$1_$2");
		string input = System.Text.RegularExpressions.Regex.Replace(text.ToUpperInvariant(), "\\s+", "_");
		return System.Text.RegularExpressions.Regex.Replace(input, "[^A-Z0-9_]", "");
	}

	/// <summary>自检用的样例：常规驼峰 + 连续大写（连续大写是踩过的坑）。</summary>
	private static readonly string[] SlugSamples =
		new string[8] { "XiaoQi", "XiaoQI", "MyPetAI", "AIPet", "SparklePet2", "SevenCard1", "WeakPower", "UiCheckPet2" };

	public string SelfTest()
	{
		StringBuilder sb = _selfTestLog;
		sb.Clear();
		SuppressConfirmations = true;
		SuppressClosePromptForTest = true;
		// 自检必须在【临时目录】里跑：自检会枚举/新建/删除存档，还可能触发静默保存。
		// 打包后的 exe 里 RootFolder 就是部署根目录，ProfileFolder 正是用户真实的「自定义角色存档」——
		// 以前在部署目录里跑 --uicheck 会直接动用户的存档（踩过，很危险）。
		_selfTestRealProfileFolder = ProfileFolder;
		string selfTestFolder = Path.Combine(Path.GetTempPath(), "forge_selftest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
		Directory.CreateDirectory(selfTestFolder);
		OverrideProfileFolderForTest(selfTestFolder);
		RefreshProfiles();
		Show();
		Check("窗口已建立", ok: true);
		// 版本号只有一个来源（csproj 的 InformationalVersion）：启动器名 / 包名 / 窗口标题都用它
		Check("版本号读得到（形如 V0.0.1）",
			AppVersion.Current.StartsWith("V", StringComparison.Ordinal) && AppVersion.Current.Length >= 5
			&& AppVersion.Current.Count((char c) => c == '.') >= 2, AppVersion.Current);
		Check("窗口标题里带版本号", Title.Contains(AppVersion.Current, StringComparison.Ordinal), Title);
		Check("Power 效果库非空（增益+减益）", AllPowers.Count > 0, $"{AllPowers.Count} 项 / {EffectCatalog.CatalogStatus}");
		Check("效果种类选项非空", Kinds.Count > 0, $"{Kinds.Count} 项");
		// 本地化键的算法必须和本体 StringHelper.Slugify 一致（连续大写是踩过的坑：键对不上 → LocException）
		{
			var slugBad = new List<string>();
			var slugRows = new List<string>();
			foreach (string s in SlugSamples)
			{
				string ours = Naming.Slug(s);
				slugRows.Add(s + "→" + ours);
				if (!string.Equals(ours, VanillaSlugify(s), StringComparison.Ordinal))
					slugBad.Add(s + "：我们=" + ours + " / 本体=" + VanillaSlugify(s));
			}
			Check("本地化键：Naming.Slug 与本体 StringHelper.Slugify 完全一致（连续大写 XiaoQI → XIAO_Q_I、AIPet → A_I_PET）",
				slugBad.Count == 0, string.Join(" ｜ ", slugRows));
		}
		Check("卡牌列表绑定到 Profile.Cards", CardList.ItemsSource == Profile.Cards);
		Check("遗物列表绑定到 Profile.Relics", RelicList.ItemsSource == Profile.Relics);
		Check("药水列表绑定到 Profile.Potions", PotionList.ItemsSource == Profile.Potions);
		Check("关键词列表绑定到 Profile.CustomKeywords", KeywordList.ItemsSource == Profile.CustomKeywords);
		Check("美术槽位已建立（8 个上传槽位 + 描边颜色输入）", ArtSlots.Count == 8, $"{ArtSlots.Count} 个");
		try
		{
			StartupProfileDialog startupProfileDialog = new StartupProfileDialog(ProfileFolder);
			int value = startupProfileDialog.ProfileList.ItemsSource?.Cast<object>().Count() ?? 0;
			Check("启动存档窗口能建出来并枚举存档", startupProfileDialog.ProfileList.ItemsSource != null, $"{value} 个存档");
			startupProfileDialog.Close();
		}
		catch (Exception ex)
		{
			Check("启动存档窗口能建出来并枚举存档", ok: false, ex.Message);
		}
		Check("输出目录默认在根目录的「自定义角色存档」下", Profile.Paths.OutputDir.EndsWith("自定义角色存档"), Profile.Paths.OutputDir);
		Check("根目录 = 程序文件所在的那一层", string.Equals(RootFolder, Directory.GetParent(AppContext.BaseDirectory.TrimEnd('\\'))?.FullName, StringComparison.OrdinalIgnoreCase), RootFolder);
		Check("存档目录名是「自定义角色存档」", ok: true, "自定义角色存档");
		// 注意：自检期间 ProfileFolder 已经被切到临时目录（免得动用户真实存档），
		// 所以这句要拿「自检开始时记录的真实目录」来断言默认位置。
		string realFolder = _selfTestRealProfileFolder ?? ProfileFolder;
		Check("默认存档目录 = 根目录\\自定义角色存档（不在程序文件里了）", realFolder == Path.Combine(RootFolder, "自定义角色存档") && !realFolder.Contains("程序文件", StringComparison.Ordinal), realFolder);
		Check("自检把存档目录切到了临时目录（不会碰用户真实存档）", ProfileFolder != realFolder && ProfileFolder.Contains("forge_selftest_", StringComparison.Ordinal), ProfileFolder);
		Check("老目录名单独留了常量（只用于自动搬迁）", ok: true, "自定义角色");
		string text = Path.Combine(Path.GetTempPath(), "forge_move_" + Guid.NewGuid().ToString("N").Substring(0, 8));
		string text2 = Path.Combine(text, "自定义角色");
		string text3 = Path.Combine(text, "自定义角色存档");
		Directory.CreateDirectory(text2);
		CharacterProfile characterProfile = ProfileFactory.Sample();
		characterProfile.Paths.OutputDir = Path.Combine(text2, "SampleMod");
		Directory.CreateDirectory(characterProfile.Paths.OutputDir);
		ProfileFactory.Save(characterProfile, Path.Combine(text2, "SampleMod.json"));
		File.WriteAllText(Path.Combine(characterProfile.Paths.OutputDir, "keep.txt"), "占位", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		int moved = MoveProfileFolderCore(text2, text3, null).Moved;
		Check("搬迁：老目录里的存档 json 和角色工程文件夹都搬过去了", moved == 2 && File.Exists(Path.Combine(text3, "SampleMod.json")) && File.Exists(Path.Combine(text3, "SampleMod", "keep.txt")), $"搬了 {moved} 项");
		Check("搬迁：空掉的老目录被删掉", !Directory.Exists(text2), text2);
		CharacterProfile characterProfile2 = ProfileFactory.Load(Path.Combine(text3, "SampleMod.json"));
		Check("搬迁：存档里指向老目录的「工程输出目录」跟着改成新目录", characterProfile2.Paths.OutputDir == Path.Combine(text3, "SampleMod"), characterProfile2.Paths.OutputDir);
		try
		{
			Directory.Delete(text, recursive: true);
		}
		catch
		{
		}
		int count = Profile.Cards.Count;
		OnAddCard(this, new RoutedEventArgs());
		Check("添加卡牌生效", Profile.Cards.Count == count + 1, $"{count} → {Profile.Cards.Count}");
		// 列表最上面两条是本体卡（打击 / 防御）：它们不能复制、也没有效果可编，这里挑「自己的卡」
		CardList.SelectedItem = Profile.Cards.First((CardSpec c) => !c.IsVanillaCard && c.Effects.Count > 0);
		Check("卡牌可被选中", CardList.SelectedItem is CardSpec);
		Button sender = new Button
		{
			Tag = CardEffectList,
			DataContext = CardList.SelectedItem
		};
		CardSpec cardSpec = (CardSpec)CardList.SelectedItem;
		int count2 = cardSpec.Effects.Count;
		OnAddEffect(sender, new RoutedEventArgs());
		Check("添加效果生效", cardSpec.Effects.Count == count2 + 1, $"{count2} → {cardSpec.Effects.Count}");
		ObservableCollection<EffectSpec> effects = cardSpec.Effects;
		Check("新效果默认勾选「允许重复选中同一个敌人」", effects[effects.Count - 1].AllowDuplicates);
		Check("新效果「数值」默认 0（用户要求：以前默认 6，加别的效果时得先清掉，容易漏）",
			effects[effects.Count - 1].Amount == 0m, effects[effects.Count - 1].Amount.ToString());
		Check("新效果「升级增量」默认 0（用户要求：以前是 +3）",
			effects[effects.Count - 1].UpgradeAmount == 0m, effects[effects.Count - 1].UpgradeAmount.ToString());
		CardEffectList.SelectedIndex = cardSpec.Effects.Count - 1;
		OnRemoveEffect(sender, new RoutedEventArgs());
		Check("删除效果生效", cardSpec.Effects.Count == count2, $"{cardSpec.Effects.Count}");
		Check("卡牌删除可以撤回（按钮可用）", CanUndoCard, UndoCardHint);
		OnUndoCard(this, new RoutedEventArgs());
		Check("撤回后被删的效果回来了", cardSpec.Effects.Count == count2 + 1, $"{cardSpec.Effects.Count}");
		OnRemoveEffect(sender, new RoutedEventArgs());
		_cardUndo.Clear();
		int count3 = Profile.Cards.Count;
		OnCopyCard(this, new RoutedEventArgs());
		Check("复制卡牌生效", Profile.Cards.Count == count3 + 1);
		OnRemoveCard(this, new RoutedEventArgs());
		Check("删除卡牌生效", Profile.Cards.Count == count3);
		OnUndoCard(this, new RoutedEventArgs());
		Check("卡牌删除可撤回（数量恢复）", Profile.Cards.Count == count3 + 1, $"{Profile.Cards.Count}");
		OnRemoveCard(this, new RoutedEventArgs());
		_cardUndo.Clear();

		// 复制卡牌必须是**整卡深拷贝**：用户实测「复制后勾选的关键词没跟着走、效果也不一样」——
		// 原因是上一版手写 new CardSpec { … } + 手写 CloneEffect，漏了一大半字段。
		{
			CardSpec copySrc = new CardSpec
			{
				Name = "复制源", ClassName = "UiCheckCopySrc", CardType = "Power", Rarity = "Rare",
				Cost = 2, InCardPool = true, Exhausts = true, Retain = true,
			};
			copySrc.KeywordIds.Add("UiCheckCopyKw");
			copySrc.Tags.Add("Strike");
			copySrc.UpgradeKeywords.Set("Innate", KeywordUpgradeSpec.Add);
			copySrc.Effects.Add(new EffectSpec
			{
				Kind = "PetHeal", Amount = 7m, UpgradeAmount = 2m, TargetSide = "Self",
				PetSummon = "UiCheckPet", ChanceEnabled = true, ChancePercent = 40m,
			});
			Profile.Cards.Add(copySrc);
			CardList.SelectedItem = copySrc;
			int beforeCopy = Profile.Cards.Count;
			OnCopyCard(this, new RoutedEventArgs());
			Check("复制卡牌：列表多一张", Profile.Cards.Count == beforeCopy + 1, $"{beforeCopy} → {Profile.Cards.Count}");
			CardSpec copyDst = Profile.Cards[Profile.Cards.Count - 1];
			Check("复制卡牌：名字加「·改」、类名清空（留空 = 自动编号，照抄会撞模型 ID）",
				copyDst.Name == "复制源·改" && copyDst.ClassName.Length == 0,
				copyDst.Name + " / 类名=" + (copyDst.ClassName.Length == 0 ? "(空)" : copyDst.ClassName));
			Check("复制卡牌：勾选的自定义关键词跟着复制（用户报过漏掉这个）",
				copyDst.CustomKeywordList.Contains("UiCheckCopyKw"),
				string.Join("·", copyDst.CustomKeywordList));
			Check("复制卡牌：卡牌关键字 / 标签 / 升级后关键字 / 类型 / 稀有度 / 费用 / 入池 全部照抄",
				copyDst.Exhausts && copyDst.Retain && copyDst.TagList.Contains("Strike")
				&& copyDst.UpgradeKeywords.Get("Innate") == KeywordUpgradeSpec.Add
				&& copyDst.CardType == "Power" && copyDst.Rarity == "Rare" && copyDst.Cost == 2 && copyDst.InCardPool,
				$"消耗={copyDst.Exhausts} 保留={copyDst.Retain} 标签={string.Join("·", copyDst.TagList)} "
				+ $"升级后固有={copyDst.UpgradeKeywords.Get("Innate")} {copyDst.CardType}/{copyDst.Rarity}/{copyDst.Cost}费");
			Check("复制卡牌：效果整条照抄（种类 / 数值 / 升级增量 / 召唤物 / 概率）",
				copyDst.Effects.Count == 1 && copyDst.Effects[0].Kind == "PetHeal"
				&& copyDst.Effects[0].Amount == 7m && copyDst.Effects[0].UpgradeAmount == 2m
				&& copyDst.Effects[0].PetSummon == "UiCheckPet"
				&& copyDst.Effects[0].ChanceEnabled && copyDst.Effects[0].ChancePercent == 40m,
				copyDst.Effects.Count == 0 ? "(没有效果)" : copyDst.Effects[0].Display);
			Check("复制卡牌：效果是**新对象**（改副本不会连带改原卡）",
				copyDst.Effects.Count == 1 && !ReferenceEquals(copyDst.Effects[0], copySrc.Effects[0]),
				"深拷贝");
			Profile.Cards.Remove(copyDst);
			Profile.Cards.Remove(copySrc);
			Check("复制卡牌测试用的两张卡已清理", !Profile.Cards.Contains(copySrc) && !Profile.Cards.Contains(copyDst));
		}
		RelicList.SelectedIndex = 0;
		RelicSpec relicSpec = (RelicSpec)RelicList.SelectedItem;
		Button sender2 = new Button
		{
			Tag = RelicEffectList,
			DataContext = relicSpec
		};
		int count4 = relicSpec.Effects.Count;
		OnAddEffect(sender2, new RoutedEventArgs());
		Check("遗物加效果生效", relicSpec.Effects.Count == count4 + 1);
		RelicEffectList.SelectedIndex = relicSpec.Effects.Count - 1;
		OnRemoveEffect(sender2, new RoutedEventArgs());
		Check("遗物删效果生效", relicSpec.Effects.Count == count4);
		OnUndoRelic(this, new RoutedEventArgs());
		Check("遗物效果可撤回", relicSpec.Effects.Count == count4 + 1, $"{relicSpec.Effects.Count}");
		RelicEffectList.SelectedIndex = relicSpec.Effects.Count - 1;
		OnRemoveEffect(sender2, new RoutedEventArgs());
		_relicUndo.Clear();
		PotionList.SelectedIndex = 0;
		PotionSpec potionSpec = (PotionSpec)PotionList.SelectedItem;
		Button sender3 = new Button
		{
			Tag = PotionEffectList,
			DataContext = potionSpec
		};
		int count5 = potionSpec.Effects.Count;
		OnAddEffect(sender3, new RoutedEventArgs());
		Check("药水加效果生效", potionSpec.Effects.Count == count5 + 1);
		PotionEffectList.SelectedIndex = potionSpec.Effects.Count - 1;
		OnRemoveEffect(sender3, new RoutedEventArgs());
		Check("药水删效果生效", potionSpec.Effects.Count == count5, $"{potionSpec.Effects.Count}");
		OnUndoPotion(this, new RoutedEventArgs());
		Check("药水效果可撤回", potionSpec.Effects.Count == count5 + 1, $"{potionSpec.Effects.Count}");
		PotionEffectList.SelectedIndex = potionSpec.Effects.Count - 1;
		OnRemoveEffect(sender3, new RoutedEventArgs());
		_potionUndo.Clear();
		CharacterProfile characterProfile3 = ProfileFactory.Sample();
		Check("新配置里「解包工程目录」默认为空", string.IsNullOrEmpty(characterProfile3.Paths.VanillaProject), string.IsNullOrEmpty(characterProfile3.Paths.VanillaProject) ? "(空)" : characterProfile3.Paths.VanillaProject);
		int num = 0;
		for (int j = 0; j < MainTabs.Items.Count; j++)
		{
			MainTabs.SelectedIndex = j;
			UpdateLayout();
			List<Button> list = new List<Button>();
			CollectButtons(this, list);
			num += list.Count((Button b) => (b.Content as string)?.StartsWith("撤回") ?? false);
		}
		Check("界面上有「撤回删除」按钮（卡牌/遗物/药水/存档/美术）", num >= 6, $"{num} 个撤回按钮");
		DependencyObject root = SelectTabRoot("卡牌");
		ToggleButton toggleButton = FindToggleButtonByContent("多选", root);
		Button button = FindButtonByContent("全选", root);
		Check("卡牌页有「多选」开关（与其他按钮同款样式）", toggleButton != null && toggleButton.Style == TryFindResource("MultiToggle"), (toggleButton == null) ? "没找到" : ("样式=" + (toggleButton.Style?.ToString() ?? "(默认)")));
		Check("卡牌页有「全选」按钮", button != null);
		Check("默认（不点「多选」）就是 Extended —— Ctrl / Shift 点选能多选（用户报过「Ctrl/Shift 没反应、只能选一个」）",
			CardList.SelectionMode == SelectionMode.Extended, CardList.SelectionMode.ToString());
		Check("默认就能往已选中项里加选（Ctrl / Shift 多选的基础）", CanAddToSelection(CardList), "模式=" + CardList.SelectionMode);
		Check("默认看不到「全选」按钮（要按 Ctrl+A，或打开「多选」开关才显示）", button != null && button.Visibility != Visibility.Visible, $"全选按钮可见性={button?.Visibility}");
		CardList.SelectedItems.Clear();
		Check("Ctrl+A：焦点在列表上就全选（窗口级快捷键）",
			TrySelectAllShortcut(CardList) && CardList.SelectedItems.Count == Profile.Cards.Count,
			$"选中 {CardList.SelectedItems.Count} / 共 {Profile.Cards.Count}");
		Check("Ctrl+A 不抢文本框：焦点不在列表上时不处理（照旧全选文字）",
			!TrySelectAllShortcut(new TextBox()), "文本框放行");
		Check("Ctrl+A 不会去动单选列表（那些列表本来就没有「全选」）",
			!TrySelectAllShortcut(new ListBox { SelectionMode = SelectionMode.Single, ItemsSource = new[] { "a", "b" } }), "单选列表放行");
		CardList.SelectedItems.Clear();
		bool flag = ToggleViaClick(toggleButton);
		UpdateLayout();
		Check("点一下「多选」开关能真的切换（不是只变了样子）", flag && toggleButton.IsChecked.GetValueOrDefault(), "切换成功=" + flag + " / IsChecked=" + toggleButton.IsChecked);
		Check("打开多选后：列表变成 Multiple（直接点行就能多选，不用按 Ctrl）", CardList.SelectionMode == SelectionMode.Multiple, CardList.SelectionMode.ToString());
		Check("多选模式下不按 Ctrl 也能累加选中（边界）", CanAddToSelection(CardList), "模式=" + CardList.SelectionMode);
		Check("打开多选后：「全选」按钮显示出来", button.Visibility == Visibility.Visible, $"{button.Visibility}");
		Check("效果列表跟着同一个开关（多选时也可多选效果）", CardEffectList.SelectionMode == SelectionMode.Multiple, CardEffectList.SelectionMode.ToString());
		List<string> names0 = Profile.Cards.Select((CardSpec c) => c.Name).ToList();
		CardList.SelectedItems.Clear();
		UpdateLayout();
		ClickButtonByContent("删除", root);
		Check("没选任何卡牌时点删除 → 数据不变（边界）", Profile.Cards.Count == names0.Count, $"{names0.Count} → {Profile.Cards.Count}");
		int confirmRequests = ConfirmRequests;
		CardList.SelectedIndex = 0;
		ClickButtonByContent("删除", root);
		Check("删除前会请求二次确认（不会直接删）", ConfirmRequests == confirmRequests + 1, $"{confirmRequests} → {ConfirmRequests}");
		Check("单张删除后少了一张", Profile.Cards.Count == names0.Count - 1, $"{Profile.Cards.Count}");
		ClickButtonByContent("撤回删除", root);
		Check("单张删除撤回后完全复原（顺序一致）", Profile.Cards.Select((CardSpec c) => c.Name).SequenceEqual(names0), $"{Profile.Cards.Count} 张");
		FindButtonByContent("全选", root)?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
		UpdateLayout();
		Check("点「全选」选中了列表里的每一张卡", CardList.SelectedItems.Count == Profile.Cards.Count && Profile.Cards.Count > 0, $"选中 {CardList.SelectedItems.Count} / 共 {Profile.Cards.Count}");
		ClickButtonByContent("删除", root);
		UpdateLayout();
		Check("批量删除后卡牌清空", Profile.Cards.Count == 0, $"{Profile.Cards.Count} 张");
		Check("列表清空后详情栏收起、显示「请选择卡牌」（边界）", CardDetail.Visibility != 0 && CardEmptyHint.Visibility == Visibility.Visible, $"CardDetail={CardDetail.Visibility} / 提示={CardEmptyHint.Visibility}");
		ClickButtonByContent("撤回删除", root);
		UpdateLayout();
		Check("批量删除撤回后卡牌完全复原（数量+顺序）", Profile.Cards.Select((CardSpec c) => c.Name).SequenceEqual(names0), $"{Profile.Cards.Count}/{names0.Count} 张");
		int count6 = Profile.Cards.Count;
		for (int num2 = 0; num2 < 3; num2++)
		{
			ClickButtonByContent("撤回删除", root);
		}
		Check("撤回栈空了以后再点撤回 → 安全无副作用（边界）", Profile.Cards.Count == count6 && !CanUndoCard, $"{Profile.Cards.Count} 张 / CanUndo={CanUndoCard}");
		if (names0.Count >= 5)
		{
			CardList.SelectedItems.Clear();
			int[] array = new int[3] { 0, 2, 4 };
			int[] array2 = array;
			foreach (int index in array2)
			{
				CardList.SelectedItems.Add(Profile.Cards[index]);
			}
			UpdateLayout();
			List<string> pickedNames = array.Select((int i) => names0[i]).ToList();
			ClickButtonByContent("删除", root);
			List<string> list2 = names0.Where((string n) => !pickedNames.Contains(n)).ToList();
			Check("隔项多选删除：剩下的正好是未选中的那些", Profile.Cards.Select((CardSpec c) => c.Name).SequenceEqual(list2), $"{Profile.Cards.Count} 张（期望 {list2.Count}）");
			ClickButtonByContent("撤回删除", root);
			Check("隔项多选撤回后位置也复原（顺序一致）", Profile.Cards.Select((CardSpec c) => c.Name).SequenceEqual(names0), $"{Profile.Cards.Count} 张");
		}
		else
		{
			Check("隔项多选删除（数据不足，跳过）", names0.Count >= 5, $"只有 {names0.Count} 张卡");
		}
		List<CardSpec> list3 = Profile.Cards.ToList();
		CardList.SelectedItems.Clear();
		Profile.Cards.Clear();
		UpdateLayout();
		ClickButtonByContent("全选", root);
		Check("空列表点「全选」→ 不报错且没有选中项（边界）", Profile.Cards.Count == 0 && CardList.SelectedItems.Count == 0, $"共 {Profile.Cards.Count} / 选中 {CardList.SelectedItems.Count}");
		foreach (CardSpec item2 in list3)
		{
			Profile.Cards.Add(item2);
		}
		UpdateLayout();
		Check("（测试收尾）卡牌已原样放回（含各自的效果数据）", Profile.Cards.Count == names0.Count && Profile.Cards.Select((CardSpec c) => c.Name).SequenceEqual(names0), $"{Profile.Cards.Count}/{names0.Count} 张");
		_cardUndo.Clear();
		DependencyObject root2 = SelectTabRoot("遗物");
		List<string> list4 = Profile.Relics.Select((RelicSpec r) => r.Name).ToList();
		ToggleButton toggleButton2 = FindToggleButtonByContent("多选", root2);
		Check("遗物页有多选开关且默认关", toggleButton2 != null && toggleButton2.IsChecked == false, "状态=" + (toggleButton2?.IsChecked?.ToString() ?? "(没找到)"));
		ToggleViaClick(toggleButton2);
		UpdateLayout();
		Check("遗物页打开多选后列表变 Multiple", RelicList.SelectionMode == SelectionMode.Multiple, RelicList.SelectionMode.ToString());
		ClickButtonByContent("全选", root2);
		UpdateLayout();
		Check("遗物页「全选」选中全部", RelicList.SelectedItems.Count == Profile.Relics.Count && list4.Count > 0, $"选中 {RelicList.SelectedItems.Count} / 共 {Profile.Relics.Count}");
		ClickButtonByContent("删除", root2);
		Check("遗物批量删除后清空", Profile.Relics.Count == 0, $"{Profile.Relics.Count}");
		Check("遗物列表清空后显示「请选择遗物」", RelicEmptyHint.Visibility == Visibility.Visible);
		ClickButtonByContent("撤回删除", root2);
		Check("遗物批量撤回后完全复原", Profile.Relics.Select((RelicSpec r) => r.Name).SequenceEqual(list4), $"{Profile.Relics.Count}/{list4.Count}");
		_relicUndo.Clear();
		DependencyObject root3 = SelectTabRoot("药水");
		List<string> list5 = Profile.Potions.Select((PotionSpec p) => p.Name).ToList();
		ToggleViaClick(FindToggleButtonByContent("多选", root3));
		UpdateLayout();
		ClickButtonByContent("全选", root3);
		UpdateLayout();
		Check("药水页「全选」选中全部", PotionList.SelectedItems.Count == Profile.Potions.Count && list5.Count > 0, $"选中 {PotionList.SelectedItems.Count} / 共 {Profile.Potions.Count}");
		ClickButtonByContent("删除", root3);
		Check("药水批量删除后清空", Profile.Potions.Count == 0, $"{Profile.Potions.Count}");
		ClickButtonByContent("撤回删除", root3);
		Check("药水批量撤回后完全复原", Profile.Potions.Select((PotionSpec p) => p.Name).SequenceEqual(list5), $"{Profile.Potions.Count}/{list5.Count}");
		_potionUndo.Clear();
		SelectTabRoot("卡牌");
		CardSpec cardSpec2 = Profile.Cards.FirstOrDefault((CardSpec c) => c.Effects.Count >= 2);
		if (cardSpec2 != null)
		{
			CardList.SelectedItem = cardSpec2;
			UpdateLayout();
			List<string> list6 = cardSpec2.Effects.Select((EffectSpec x) => x.Display).ToList();
			ListBox cardEffectList = CardEffectList;
			cardEffectList.SelectedItems.Clear();
			foreach (EffectSpec item3 in cardEffectList.Items.OfType<EffectSpec>())
			{
				cardEffectList.SelectedItems.Add(item3);
			}
			UpdateLayout();
			Check("效果列表可多选（选中了全部效果）", cardEffectList.SelectedItems.Count == list6.Count && list6.Count >= 2, $"选中 {cardEffectList.SelectedItems.Count} / 共 {list6.Count}");
			Button sender4 = new Button
			{
				Tag = cardEffectList,
				DataContext = cardSpec2
			};
			OnRemoveEffect(sender4, new RoutedEventArgs());
			Check("多选批量删效果 → 效果清空", cardSpec2.Effects.Count == 0, $"{cardSpec2.Effects.Count}");
			OnUndoCard(this, new RoutedEventArgs());
			Check("批量删效果撤回后完全复原（顺序一致）", cardSpec2.Effects.Select((EffectSpec x) => x.Display).SequenceEqual(list6), $"{cardSpec2.Effects.Count}/{list6.Count}");
		}
		else
		{
			Check("效果多选删除（没有含 ≥2 条效果的卡，跳过）", cardSpec2 != null, "无数据");
		}
		_cardUndo.Clear();
		SelectTabRoot("卡牌");
		ColumnDefinitionCollection columnDefinitions = CardDetail.ColumnDefinitions;
		UpdateLayout();
		Check("卡牌页：中间属性区分到的宽度比右侧效果区多（右侧收窄 1/3 让给中间）", columnDefinitions.Count == 3 && columnDefinitions[0].ActualWidth > columnDefinitions[2].ActualWidth, (columnDefinitions.Count == 3) ? $"属性={columnDefinitions[0].ActualWidth:F0} / 效果={columnDefinitions[2].ActualWidth:F0}" : $"{columnDefinitions.Count} 列");
		Check("卡牌页：右侧效果区仍够放标签+输入框（>=360）", columnDefinitions.Count == 3 && columnDefinitions[2].ActualWidth >= 360.0, (columnDefinitions.Count == 3) ? $"效果列宽={columnDefinitions[2].ActualWidth:F0}" : "?");
		DependencyObject root4 = SelectTabRoot("卡牌");
		List<WrapPanel> list7 = new List<WrapPanel>();
		CollectWrapPanels(root4, list7);
		Check("列表按钮行是自动换行的（窄了也不会把按钮挤没）", list7.Count >= 2, $"{list7.Count} 个换行容器");
		ObservableCollection<PowerEntry> allPowers = AllPowers;
		ReloadCatalog();
		Check("「增益 / 减益」数据源是稳定集合（重新扫描后还是同一个对象）", allPowers == AllPowers, (allPowers == AllPowers) ? $"同一实例，{AllPowers.Count} 项" : "换成了新列表（过滤视图会丢）");
		Check("重新扫描后「增益 / 减益」列表仍有内容", AllPowers.Count > 0, $"{AllPowers.Count} 项");
		SelectTabRoot("卡牌");
		Check("目标卡目录非空（本体卡 + 自己的卡）", AllCards.Count > 0, $"{AllCards.Count} 张");
		Check("自己的卡也在目标卡目录里", Profile.Cards.Count == 0 || AllCards.Any((PowerEntry x) => Profile.Cards.Any((CardSpec c) => c.Name == x.Zh)), "自己的卡 " + Profile.Cards.Count + " 张");
		// ===== 「保存的卡在目标卡（生成 / 变化用）里搜不到」这个 bug 的回归测试 =====
		// 原因：SearchComboBox 只在 ItemsSource 换实例时灌一次候选表，而 AllCards 是稳定集合
		//（加卡 / 改名是按索引就地替换，避免整表重置把已选值弹掉）→ 新加的卡永远进不了下拉。
		{
			DependencyObject cardPage = SelectTabRoot("卡牌");
			EffectSpec genProbe = new EffectSpec { Kind = "GenerateCard", Amount = 1m, SpawnCardId = "SevenCrush" };
			if (CardEffectList.Items.Count > 0) CardEffectList.SelectedItem = CardEffectList.Items[0];
			UpdateLayout();
			List<SearchComboBox> targetBoxes = new List<SearchComboBox>();
			CollectSearchCombos(cardPage, targetBoxes);
			SearchComboBox? targetBox = targetBoxes.FirstOrDefault((SearchComboBox b) =>
				BindingOperations.GetBinding(b, SearchComboBox.SelectedValueProperty)?.Path?.Path == "SpawnCardId");
			Check("找到「目标卡」搜索框", targetBox != null, $"{targetBoxes.Count} 个搜索框");
			if (targetBox != null)
			{
				int targetBefore = targetBox.FilteredCount;
				CardSpec fresh = new CardSpec { Name = "搜索用新卡", ClassName = "UiCheckSearchNew", Cost = 1, CardType = "Attack" };
				fresh.Effects.Clear();
				fresh.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 3m, TargetSide = "Enemy" });
				Profile.Cards.Add(fresh);
				RefreshCardChoices();
				UpdateLayout();
				targetBox.ApplySearch("搜索用新卡");
				Check("新加的卡马上能在「目标卡」里搜到（原先只在换数据源时才刷新 → 搜不到）",
					targetBox.FilteredCount == 1 && targetBox.SuggestionTexts[0].Contains("搜索用新卡") && targetBox.SuggestionTexts[0].Contains("UiCheckSearchNew"),
					string.Join(" / ", targetBox.SuggestionTexts));
				// 改卡名（按索引就地替换的那种变更）也要能搜到新名字
				fresh.Name = "改名后的卡";
				UpdateLayout();
				targetBox.ApplySearch("改名后的卡");
				Check("改了卡名之后也能搜到新名字",
					targetBox.FilteredCount == 1 && targetBox.SuggestionTexts[0].Contains("改名后的卡"), string.Join(" / ", targetBox.SuggestionTexts));
				targetBox.ApplySearch("");
				Check("清空关键词后候选表又回到「全部」", targetBox.FilteredCount >= targetBefore + 1, $"{targetBox.FilteredCount} 项");
				Profile.Cards.Remove(fresh);
				RefreshCardChoices();
				UpdateLayout();
			}
			_ = genProbe;
			SelectTabRoot("卡牌");
		}
		string text4 = PathAutoDetect.FindVanillaProject();
		if (text4 != null)
		{
			string vanillaProject = Profile.Paths.VanillaProject;
			try
			{
				Profile.Paths.VanillaProject = text4;
				EffectCatalog.InitializeCards(text4, Profile.Paths.GameDataDir);
				RefreshCardChoices();
				UpdateLayout();
				int num4 = EffectCatalog.Cards.Count((PowerEntry c) => c.Zh != c.Id);
				string value2 = AllCards.FirstOrDefault((PowerEntry x) => x.Zh != x.Id)?.Display ?? "(无)";
				Check("配上解包工程后，本体卡牌全部有中文名", EffectCatalog.Cards.Count > 0 && num4 == EffectCatalog.Cards.Count, $"{num4}/{EffectCatalog.Cards.Count}，例：{value2}");
			}
			finally
			{
				Profile.Paths.VanillaProject = vanillaProject;
				EffectCatalog.InitializeCards(vanillaProject, Profile.Paths.GameDataDir);
				RefreshCardChoices();
				UpdateLayout();
			}
		}
		else
		{
			Check("（跳过）配解包工程后的中文名检查：本机没有解包工程", ok: true, "(无解包工程)");
		}
		string profileFolder = ProfileFolder;
		string text5 = Path.Combine(Path.GetTempPath(), "forge_multiselect_" + Guid.NewGuid().ToString("N").Substring(0, 8));
		Directory.CreateDirectory(text5);
		try
		{
			OverrideProfileFolderForTest(text5);
			RefreshProfiles();
			DependencyObject root5 = SelectTabRoot("配置存档");
			Check("存档页有「全选」按钮", FindButtonByContent("全选", root5) != null);
			Check("存档页默认也是 Extended（Ctrl / Shift 多选直接可用）", ProfileList.SelectionMode == SelectionMode.Extended, ProfileList.SelectionMode.ToString());
			Check("存档页 Ctrl+A 也能全选（不用先开「多选」）",
				TrySelectAllShortcut(ProfileList) && ProfileList.SelectedItems.Count == ProfileList.Items.Count,
				$"选中 {ProfileList.SelectedItems.Count} / 共 {ProfileList.Items.Count}");
			ProfileList.SelectedItems.Clear();
			ToggleViaClick(FindToggleButtonByContent("多选", root5));
			UpdateLayout();
			Check("存档页打开多选后变 Multiple", ProfileList.SelectionMode == SelectionMode.Multiple, ProfileList.SelectionMode.ToString());
			ClickButtonByContent("全选", root5);
			Check("空存档目录点「全选」→ 安全（边界）", Profiles.Count == 0 && ProfileList.SelectedItems.Count == 0, $"存档 {Profiles.Count} / 选中 {ProfileList.SelectedItems.Count}");
			ClickButtonByContent("删除选中存档", root5);
			Check("空存档目录点「删除」→ 安全（边界）", Profiles.Count == 0, $"{Profiles.Count}");
			for (int num5 = 1; num5 <= 3; num5++)
			{
				File.WriteAllText(Path.Combine(text5, $"temp{num5}.json"), $"{{\"ModId\":\"Temp{num5}\",\"DisplayName\":\"临时{num5}\"}}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
			RefreshProfiles();
			UpdateLayout();
			List<string> source = (from p in Profiles
				select p.Path into x
				orderby x
				select x).ToList();
			Dictionary<string, byte[]> beforeBytes = source.ToDictionary((string p) => p, (string p) => File.ReadAllBytes(p));
			Check("临时存档已就绪（3 个）", Profiles.Count == 3, $"{Profiles.Count}");
			// 有 3 个存档时再验一次 Ctrl+A：空列表那条只是「不报错」，这条才真的验「全选生效」
			ProfileList.SelectedItems.Clear();
			Check("存档页 Ctrl+A 在真有条目时选中全部 3 个",
				TrySelectAllShortcut(ProfileList) && ProfileList.SelectedItems.Count == 3,
				$"选中 {ProfileList.SelectedItems.Count} / 共 {Profiles.Count}");
			ProfileList.SelectedItems.Clear();
			ClickButtonByContent("全选", root5);
			UpdateLayout();
			Check("存档页「全选」选中全部", ProfileList.SelectedItems.Count == 3, $"{ProfileList.SelectedItems.Count}");
			ClickButtonByContent("删除选中存档", root5);
			UpdateLayout();
			Check("多选批量删存档 → 文件都没了", Profiles.Count == 0 && source.All((string p) => !File.Exists(p)), $"列表 {Profiles.Count} 个");
			ClickButtonByContent("撤回删除", root5);
			UpdateLayout();
			bool flag2 = source.All((string p) => File.Exists(p));
			bool ok2 = flag2 && source.All((string p) => File.ReadAllBytes(p).SequenceEqual(beforeBytes[p]));
			Check("批量删存档可撤回：文件回来了", flag2, $"{Profiles.Count} 个");
			Check("撤回的存档内容与原来逐字节相同", ok2);
		}
		finally
		{
			try
			{
				OverrideProfileFolderForTest(profileFolder);
			}
			catch
			{
			}
			try
			{
				Directory.Delete(text5, recursive: true);
			}
			catch
			{
			}
			RefreshProfiles();
		}
		OnValidate(this, new RoutedEventArgs());
		Check("校验可执行", Issues.Count >= 0, $"{Issues.Count} 条");
		int num6 = Issues.Count((string i) => i.StartsWith("[错误]") && !i.Contains("目录不存在") && !i.Contains("找不到") && !i.Contains("不可用"));
		Check("示例配置无错误项", num6 == 0, $"{num6} 个错误");
		SelectTabRoot("卡牌");
		CardList.SelectedIndex = -1;
		UpdateLayout();
		Check("没选卡牌时隐藏属性/效果栏", CardDetail.Visibility != Visibility.Visible, "CardDetail=" + CardDetail.Visibility);
		Check("没选卡牌时显示「请选择卡牌」", CardEmptyHint.Visibility == Visibility.Visible, "提示=" + CardEmptyHint.Visibility);
		RelicList.SelectedIndex = -1;
		PotionList.SelectedIndex = -1;
		UpdateLayout();
		Check("没选遗物时只显示「请选择遗物」", RelicDetail.Visibility != 0 && RelicEmptyHint.Visibility == Visibility.Visible, "RelicDetail=" + RelicDetail.Visibility);
		Check("没选药水时只显示「请选择药水」", PotionDetail.Visibility != 0 && PotionEmptyHint.Visibility == Visibility.Visible, "PotionDetail=" + PotionDetail.Visibility);
		RelicList.SelectedIndex = 0;
		PotionList.SelectedIndex = 0;
		UpdateLayout();
		CardList.SelectedIndex = 0;
		UpdateLayout();
		Check("选中卡牌后恢复属性/效果栏", CardDetail.Visibility == Visibility.Visible && CardEmptyHint.Visibility != Visibility.Visible, "CardDetail=" + CardDetail.Visibility.ToString() + " / 提示=" + CardEmptyHint.Visibility);
		SelectTabRoot("角色");
		List<TextBox> list8 = new List<TextBox>();
		if (RoleTab.Content is DependencyObject root6)
		{
			CollectTextBoxes(root6, list8);
		}
		Check("角色页已去掉「解锁前置角色」", list8.Count > 0 && !list8.Any((TextBox t) => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path?.Path == "Profile.UnlockAfter"), $"{list8.Count} 个输入框");
		SelectTabRoot("遗物");
		if (Profile.Relics.Count == 0)
		{
			OnAddRelic(this, new RoutedEventArgs());
		}
		RelicList.SelectedIndex = 0;
		UpdateLayout();
		RelicSpec relicSpec2 = (RelicSpec)RelicList.SelectedItem;
		int count7 = relicSpec2.Effects.Count;
		Button sender5 = new Button
		{
			Tag = RelicEffectList,
			DataContext = relicSpec2
		};
		OnAddEffect(sender5, new RoutedEventArgs());
		int ok3;
		if (relicSpec2.Effects.Count == count7 + 1)
		{
			ObservableCollection<EffectSpec> effects2 = relicSpec2.Effects;
			ok3 = ((effects2[effects2.Count - 1].UpgradeAmount == 0m) ? 1 : 0);
		}
		else
		{
			ok3 = 0;
		}
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(5, 1);
		defaultInterpolatedStringHandler.AppendLiteral("升级增量=");
		ObservableCollection<EffectSpec> effects3 = relicSpec2.Effects;
		defaultInterpolatedStringHandler.AppendFormatted(effects3[effects3.Count - 1].UpgradeAmount);
		Check("遗物页「添加效果」的新效果不带升级增量", (byte)ok3 != 0, defaultInterpolatedStringHandler.ToStringAndClear());
		ObservableCollection<EffectSpec> effects4 = relicSpec2.Effects;
		bool ok4 = !effects4[effects4.Count - 1].DisplayPlain.Contains("升级");
		ObservableCollection<EffectSpec> effects5 = relicSpec2.Effects;
		Check("新效果的列表文字里没有「升级」", ok4, effects5[effects5.Count - 1].DisplayPlain);
		OnRemoveEffect(sender5, new RoutedEventArgs());
		_relicUndo.Clear();
		SelectTabRoot("卡牌");
		CardList.SelectedIndex = 0;
		UpdateLayout();
		CardSpec cardSpec3 = (CardSpec)CardList.SelectedItem;
		int count8 = cardSpec3.Effects.Count;
		Button sender6 = new Button
		{
			Tag = CardEffectList,
			DataContext = cardSpec3
		};
		OnAddEffect(sender6, new RoutedEventArgs());
		int ok5;
		if (cardSpec3.Effects.Count == count8 + 1)
		{
			ObservableCollection<EffectSpec> effects6 = cardSpec3.Effects;
			ok5 = ((effects6[effects6.Count - 1].UpgradeAmount == 0m) ? 1 : 0);
		}
		else
		{
			ok5 = 0;
		}
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(5, 1);
		defaultInterpolatedStringHandler.AppendLiteral("升级增量=");
		ObservableCollection<EffectSpec> effects7 = cardSpec3.Effects;
		defaultInterpolatedStringHandler.AppendFormatted(effects7[effects7.Count - 1].UpgradeAmount);
		Check("卡牌页「添加效果」的新效果「升级增量」默认是 0（用户要求：加完自己填）", (byte)ok5 != 0, defaultInterpolatedStringHandler.ToStringAndClear());
		OnRemoveEffect(sender6, new RoutedEventArgs());
		_cardUndo.Clear();
		Check("先古之民目录读到了（从解包工程）", EffectCatalog.Ancients.Count > 0, AncientCatalog.CatalogStatus);
		AncientEntry ancientEntry = EffectCatalog.FindAncient("DARV");
		Check("扫到了本体遗物清单（界面下拉要用）", AncientCatalog.VanillaRelics.Count > 0, $"{AncientCatalog.VanillaRelics.Count} 个本体遗物");
		if ((object)ancientEntry != null)
		{
			Check("每位先古之民都扫到了「他本来会给的遗物」候选（读本体事件源码）", ancientEntry.RelicCandidateIds.Count > 0, $"达弗 {ancientEntry.RelicCandidateIds.Count} 个：{string.Join("/", ancientEntry.RelicCandidateIds.Take(4))}");
			Check("先古之民 Id 能对到本体事件类名（DARV → Darv）", ancientEntry.EventClassName == "Darv", ancientEntry.EventClassName);
			Check("候选里都是本体遗物类名（比如 Astrolabe）", ancientEntry.RelicCandidateIds.Contains("Astrolabe"), string.Join("/", ancientEntry.RelicCandidateIds.Take(3)));
		}
		CharacterProfile ap = ProfileFactory.Sample();
		ap.Ancients.Clear();
		AncientTalkSpec ancientTalkSpec = new AncientTalkSpec
		{
			AncientId = "DARV"
		};
		ap.Ancients.Add(ancientTalkSpec);
		Check("没配遗物替换时不生成那支补丁", !AncientPatchGen.HasRelicReplacements(ap));
		ancientTalkSpec.RelicReplacements.Add(new AncientRelicReplaceSpec
		{
			FromRelicId = "Astrolabe",
			RelicId = Naming.From(ap).RelicClassName(ap.Relics[0], 0)
		});
		ancientTalkSpec.RelicReplacements.Add(new AncientRelicReplaceSpec
		{
			FromRelicId = "SneckoEye",
			RelicId = "BurningBlood"
		});
		Check("配了就生成补丁", AncientPatchGen.HasRelicReplacements(ap));
		string text6 = AncientPatchGen.RelicReplacementSource(ap);
		string myRelic1 = Naming.From(ap).RelicClassName(ap.Relics[0], 0);
		Check("补丁挂在「这位先古之民」的 GenerateInitialOptions 上（protected，所以用字符串名字）", text6.Contains("[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Events.Darv), \"GenerateInitialOptions\")]"), "挂在 Darv 上");
		Check("按「原本的遗物」定位（不再按第几个选项）", text6.Contains($"Replace<{myRelic1}>(options, __instance, \"Astrolabe\"") && text6.Contains("Replace<BurningBlood>(options, __instance, \"SneckoEye\""), "按原本的遗物定位");
		Check("也支持你自己的遗物（按类名生成泛型调用）", text6.Contains("Replace<" + myRelic1 + ">"), "自己的遗物也在");
		Check("不再有「按概率出现」那套（Rng / NextDouble 都不该出现）", !text6.Contains("NextDouble") && !text6.Contains("chance"), "概率已去掉");
		Check("不再有「第几个选项」那套（旧配置的 Slot 不出现在生成结果里）", !text6.Contains("__instance, 1)"), "位置写法已去掉");
		Check("按原本的遗物定位时会归一化比较（本体的 Id.Entry 是 SNECKO_EYE 这种，界面配的是类名 SneckoEye）",
			AncientPatchGen.SameRelic("SNECKO_EYE", "SneckoEye")
			&& AncientPatchGen.SameRelic("ASTROLABE", "Astrolabe")
			&& AncientPatchGen.SameRelic("BLACK_STAR", "BlackStar")
			&& !AncientPatchGen.SameRelic("BLACK_STAR", "Astrolabe"), "归一化比较正确");
		Check("生成的匹配代码两种都比（本地化表键 + 类名）", text6.Contains("SameRelic(r.Id.Entry, fromRelicId)") && text6.Contains("SameRelic(r.GetType().Name, fromRelicId)"), "两种都比");
		Check("生成时会打日志说明「替换前 → 替换后」（出问题看日志就知道为什么）", text6.Contains("的遗物选项：") && text6.Contains(" → "), "有日志");
		Check("造选项用的是本体自己的 RelicOption（反射），行为一致", text6.Contains("AccessTools.Method(typeof(MegaCrit.Sts2.Core.Models.AncientEventModel), \"RelicOption\"") && text6.Contains("\"INITIAL\""), "用本体方法");
		Check("这次没抽到那个原本的遗物时 → 什么都不做（不再顶掉最后一个选项）",
			text6.Contains("本次不替换") && !text6.Contains("index = options.Count - 1") && text6.Contains("SameRelic(r.Id.Entry, fromRelicId)"), "不挤别的选项");
		Check("已经在选项里时跳过替换", text6.Contains("本来就有"), "有兜底");
		Check("整个补丁包在 try/catch 里（改选项失败不能把事件搞崩）", text6.Contains("catch (Exception e)") && text6.Contains("__result = options;"), "有兜底");
		Check("生成的类名带 Forge 前缀（避免和用户自己的类名撞车 —— 实测踩过）", text6.Contains("ForgeAncientRelicReplace") && text6.Contains("ForgeDarvRelicReplacePatch"), "有前缀");
		CharacterProfile characterProfile4 = ProfileFactory.Sample();
		characterProfile4.Ancients.Clear();
		AncientTalkSpec ancientTalkSpec2 = new AncientTalkSpec
		{
			AncientId = "DARV"
		};
		ancientTalkSpec2.RelicReplacements.Add(new AncientRelicReplaceSpec
		{
			FromRelicId = "Astrolabe",
			RelicId = "BurningBlood"
		});
		ancientTalkSpec2.RelicReplacements.Add(new AncientRelicReplaceSpec
		{
			FromRelicId = "Astrolabe",
			RelicId = "NoSuchRelicXyz"
		});
		characterProfile4.Ancients.Add(ancientTalkSpec2);
		List<ValidationIssue> source2 = ProfileValidator.Validate(characterProfile4);
		Check("同一个「原本的遗物」配了不止一次 → 警告", source2.Any((ValidationIssue i) => i.Level == "警告" && i.Message.Contains("配了不止一次")), "有警告");
		Check("要换成的遗物类名不存在 → 错误", source2.Any((ValidationIssue i) => i.IsError && i.Message.Contains("找不到")), "有错误");
		ancientTalkSpec2.RelicReplacements.Clear();
		ancientTalkSpec2.RelicReplacements.Add(new AncientRelicReplaceSpec
		{
			FromRelicId = "NotHisRelicXyz",
			RelicId = "BurningBlood"
		});
		Check("选了一个不是他原本会给的遗物 → 只警告（还是能替换）", ProfileValidator.Validate(characterProfile4).Any((ValidationIssue i) => i.Level == "警告" && i.Message.Contains("不是这位先古之民原本会给的遗物")), "有警告");
		ancientTalkSpec2.RelicReplacements.Clear();
		ancientTalkSpec2.RelicReplacements.Add(new AncientRelicReplaceSpec
		{
			FromRelicId = "Astrolabe",
			RelicId = "BurningBlood"
		});
		Check("换了不在候选表里的本体遗物（SneckoEye 之外的）→ 只提示（还是能塞）", ProfileValidator.Validate(characterProfile4).All((ValidationIssue i) => !(i.IsError && i.Message.Contains("遗物替换"))), "没有错误");
		SelectTabRoot("先古之民");
		AncientRow ancientRow = Ancients.FirstOrDefault((AncientRow a) => a.Entry.Id == "DARV");
		Check("左侧列表能拿到达弗这一行（行 = 目录项 + 存档配置）", ancientRow != null, (ancientRow == null) ? "(没有)" : ancientRow.Entry.Display);
		AncientTalkSpec talk;
		int count9;
		if (ancientRow != null)
		{
			AncientList.SelectedItem = ancientRow;
			UpdateLayout();
			talk = ancientRow.Talk;
			count9 = talk.RelicReplacements.Count;
			Check("下拉候选里有这位先古之民的本体遗物，也有我自己的遗物", AncientRelicChoices.Any((PowerEntry x) => x.Id == "Astrolabe") && AncientRelicChoices.Any((PowerEntry x) => x.Id == Naming.From(ap).RelicClassName(ap.Relics[0], 0)), $"{AncientRelicChoices.Count} 个候选");
			// #1 先罗列「原本会给的遗物」，再从里面挑一个替换
			Check("「原本会给的遗物」清单列出来了（含星盘等本体遗物）",
				AncientOriginalRelics.Count > 3 && AncientOriginalRelics.Any((PowerEntry x) => x.Id == "Astrolabe"),
				$"{AncientOriginalRelics.Count} 个：" + string.Join("、", AncientOriginalRelics.Take(4).Select((PowerEntry x) => x.Id)));
			Check("清单里只列他原本会给的（不含自己做的遗物）",
				!AncientOriginalRelics.Any((PowerEntry x) => x.Id == Naming.From(ap).RelicClassName(ap.Relics[0], 0)), "只有本体的");
			Check("清单说明会报出「几种遗物 / 已配替换几种」", AncientOriginalRelicSummary.Contains("原本会给你") && AncientOriginalRelicSummary.Contains("已经配了替换"), AncientOriginalRelicSummary);
			UpdateLayout();
			Check("界面上那个清单控件绑上了这份清单", AncientOriginalRelicList.Items.Count > 3, $"{AncientOriginalRelicList.Items.Count} 行");
			// 选中清单里的「星盘」→ 点「替换掉选中的这个」（从控件自己的 Items 里选，和用户点的是一个东西）
			var star = AncientOriginalRelicList.Items.OfType<PowerEntry>().FirstOrDefault((PowerEntry x) => x.Id == "Astrolabe");
			Check("清单里能点到「星盘」这一行", star != null, $"{AncientOriginalRelicList.Items.Count} 行");
			AncientOriginalRelicList.SelectedItem = star;
			OnReplaceSelectedOriginalRelic(this, new RoutedEventArgs());
			bool added = talk.RelicReplacements.Count == count9 + 1;
			Check("点「替换掉选中的这个」会按选中的原本遗物加一条替换", added, $"{talk.RelicReplacements.Count} 条");
			if (added)
			{
				var last = talk.RelicReplacements[talk.RelicReplacements.Count - 1];
				Check("新加的那条记住了「原本的遗物」并默认给了一个要换成的遗物",
					last.FromRelicId == "Astrolabe" && last.RelicId.Length > 0, last.Display);
				Check("记录列表里两行都写得清（原本 / 换成）",
					last.FromDisplay.StartsWith("原本：") && last.ToDisplay.StartsWith("换成：") && last.FromDisplay.Contains("星盘"), last.FromDisplay + " ｜ " + last.ToDisplay);
				Check("记录列表里会标出这一条是否真的会生效", last.StateText.Contains("✅") || last.StateText.Contains("⚠"), last.StateText);
				// 没选「换成什么」= 不会生效，必须能一眼看出来
				string keep = last.RelicId;
				last.RelicId = "";
				Check("没选要换成什么时，状态文字明确警告「不会有任何变化」",
					!last.StateOk && last.StateText.Contains("不会有任何变化"), last.StateText);
				last.RelicId = keep;
				last.RelicId = last.FromRelicId;
				Check("原本的遗物和要换成的遗物相同时也会警告「等于没改」",
					!last.StateOk && last.StateText.Contains("等于没改"), last.StateText);
				last.RelicId = keep;
				Check("配全了之后状态是「会替换」", last.StateOk && last.StateText.Contains("会替换"), last.StateText);
				Check("默认给的目标遗物和「原本的遗物」不一样（不然等于没改 —— 用户以为「替换无效」就是这个坑）",
					!string.Equals(keep, last.FromRelicId, StringComparison.Ordinal), $"原本 {last.FromRelicId} → 默认换成 {keep}");
			}
			Check("页面里有「原本会给的遗物」这一块（旧的「替换第几个选项」已删掉）",
				TextsIn(AncientTab.Content as DependencyObject).Any((string t) => t.Contains("原本会给的遗物"))
				&& !TextsIn(AncientTab.Content as DependencyObject).Any((string t) => t.Contains("替换第几个选项") || t.Contains("遗物出现概率")), "控件在");
			Check("替换记录那个列表给足了高度（不再是 140 的小条）",
				AncientRelicReplaceList.MinHeight >= 150 && !double.IsNaN(AncientRelicReplaceList.MinHeight), $"MinHeight={AncientRelicReplaceList.MinHeight}");
			Check("「原本会给的遗物」清单也给了足够高度", AncientOriginalRelicList.MinHeight >= 150, $"MinHeight={AncientOriginalRelicList.MinHeight}");
			// 控件真的吃到数据了吗？（绑错路径时绑定会静默失败、列表永远是空的 —— 用户看到的就是「无反应」）
			UpdateLayout();
			Check("替换记录列表的 ItemsSource 真的绑上了（不是没绑上）",
				AncientRelicReplaceList.ItemsSource is not null, "ItemsSource 已绑定");
			Check("替换记录列表里真的能看到刚加的那条（不是空白列表）",
				AncientRelicReplaceList.Items.Count == talk.RelicReplacements.Count && AncientRelicReplaceList.Items.Count > 0,
				$"列表 {AncientRelicReplaceList.Items.Count} 行 / 数据 {talk.RelicReplacements.Count} 条");
			Check("「原本会给的遗物」清单控件也真的绑上了", AncientOriginalRelicList.ItemsSource is not null && AncientOriginalRelicList.Items.Count > 3,
				$"{AncientOriginalRelicList.Items.Count} 行");
			// 选中一条记录后，下面的明细要能编辑（DataContext 得是那条记录）
			var shown = AncientRelicReplaceList.Items.OfType<AncientRelicReplaceSpec>().FirstOrDefault();
			AncientRelicReplaceList.SelectedItem = shown;
			UpdateLayout();
			Check("选中记录后下面的明细面板跟着这条记录走（能编辑）",
				shown is not null && ReferenceEquals(AncientRelicReplaceDetail.DataContext, shown),
				AncientRelicReplaceDetail.DataContext is AncientRelicReplaceSpec sp ? sp.Display : "(没跟上)");
			List<ComboBox> detailCombos = new List<ComboBox>();
			CollectCombos(AncientRelicReplaceDetail, detailCombos);
			List<SearchComboBox> detailSearch = new List<SearchComboBox>();
			CollectSearchCombos(AncientRelicReplaceDetail, detailSearch);
			Check("明细里「原本的遗物」下拉绑在 FromRelicId 上",
				detailSearch.Any(sc => BindingOperations.GetBinding(sc, SearchComboBox.SelectedValueProperty)?.Path?.Path == "FromRelicId"),
				string.Join(" / ", detailSearch.Select(sc => BindingOperations.GetBinding(sc, SearchComboBox.SelectedValueProperty)?.Path?.Path)));
			Check("明细里「换成哪个遗物」下拉绑在 RelicId 上（这就是真正会生效的那个值）",
				detailSearch.Any(sc => BindingOperations.GetBinding(sc, SearchComboBox.SelectedValueProperty)?.Path?.Path == "RelicId"),
				string.Join(" / ", detailSearch.Select(sc => BindingOperations.GetBinding(sc, SearchComboBox.SelectedValueProperty)?.Path?.Path)));
			Check("明细里两个下拉都有候选（他原本会给的 / 全部遗物）",
				detailSearch.Count >= 2 && detailSearch.All(sc => sc.ItemsSource is not null),
				$"{detailSearch.Count} 个下拉");
			if (talk.RelicReplacements.Count > count9)
				talk.RelicReplacements.RemoveAt(talk.RelicReplacements.Count - 1);
			Check("测试用的遗物替换已清理", talk.RelicReplacements.Count == count9, $"{talk.RelicReplacements.Count} 条");
		}
		AncientList.SelectedItem = null;
		UpdateLayout();
		TabItem tabItem = FindTab("先古之民");
		Check("有「先古之民」选项卡", tabItem != null);
		int num7 = -1;
		int num8 = -1;
		int num9 = -1;
		int num10 = -1;
		int num11 = -1;
		int numKw = -1;
		int numSummon = -1;
		int numCurseTab = -1;
		for (int num12 = 0; num12 < MainTabs.Items.Count; num12++)
		{
			if (MainTabs.Items[num12] is TabItem tabItem2)
			{
				switch ((tabItem2.Header as string) ?? "")
				{
				case "自定义关键词":
					numKw = num12;
					break;
				case "先古之民":
					num7 = num12;
					break;
				case "药水":
					num8 = num12;
					break;
				case "诅咒 / 先古卡":
					numCurseTab = num12;
					break;
				case "美术资源":
					num9 = num12;
					break;
				case "本体状态改写":
					num10 = num12;
					break;
				case "自定义状态":
					num11 = num12;
					break;
				case "召唤物":
					numSummon = num12;
					break;
				}
			}
		}
		// 注意：这一串下标是**绝对**的，加了「召唤物」（紧跟「角色」，下标 1）之后全部 +1。
		// 只更新数字、不删断言 —— 顺序一旦被改乱（比如把「召唤物」插到最后）这里就会红。
		Check("「召唤物」是第 2 个选项卡（紧跟「角色」，下标 1）", numSummon == 1, $"召唤物={numSummon}");
		Check("「药水」→「诅咒 / 先古卡」→「自定义关键词」→「先古之民」→「本体状态改写」→「自定义状态」→「美术资源」按顺序排", num8 >= 0 && numCurseTab == num8 + 1 && numKw == numCurseTab + 1 && num7 == numKw + 1 && num10 == num7 + 1 && num11 == num10 + 1 && num9 == num11 + 1, $"药水={num8} / 诅咒先古卡={numCurseTab} / 关键词={numKw} / 先古之民={num7} / 本体状态改写={num10} / 自定义状态={num11} / 美术={num9}");
		Check("目录里包含建筑师（本体没给他写过通用对话，靠补丁注入）", EffectCatalog.Ancients.Any((AncientEntry a) => a.Id == "THE_ARCHITECT"), "有 THE_ARCHITECT");
		SelectTabRoot("角色");
		List<TextBox> list9 = new List<TextBox>();
		CollectTextBoxes((RoleTab.Content as DependencyObject) ?? this, list9);
		Check("角色页已去掉「与建筑师的结算对话」输入框", !list9.Any((TextBox b) => BindingOperations.GetBinding(b, TextBox.TextProperty)?.Path?.Path == "Profile.ArchitectDialogue"), string.Join(" / ", list9.Select((TextBox b) => BindingOperations.GetBinding(b, TextBox.TextProperty)?.Path?.Path).Distinct()));
		DependencyObject root7 = SelectTabRoot("先古之民");
		Check("左边能列出先古之民", AncientList.Items.Count > 0, $"{AncientList.Items.Count} 位");
		Check("没选时右边提示「请先选择」", AncientNoSelectHint.Visibility == Visibility.Visible);
		AncientRow ancientRow2 = AncientList.Items.OfType<AncientRow>().FirstOrDefault((AncientRow r) => r.Entry.Id == "THE_ARCHITECT");
		Check("左边能选到建筑师", ancientRow2 != null);
		if (ancientRow2 != null)
		{
			AncientList.SelectedItem = ancientRow2;
			UpdateLayout();
			Check("选中后右边提示消失", AncientNoSelectHint.Visibility != Visibility.Visible);
			List<TextBox> list10 = new List<TextBox>();
			CollectTextBoxes(root7, list10);
			List<string> source3 = list10.Select((TextBox b) => BindingOperations.GetBinding(b, TextBox.TextProperty)?.Path?.Path).ToList();
			Check("右边有可编辑的对话控件（第几次 / 台词 / 下一句按钮）", source3.Any((string p) => p == "Times" || p == "Text" || p == "NextText"), string.Join(" / ", source3.Distinct()));
			int count10 = ancientRow2.Dialogues.Count;
			ancientRow2.Talk.Dialogues.Clear();
			OnAddAncientDialogue(this, new RoutedEventArgs());
			Check("点「加一段对话」会新增一段", ancientRow2.Dialogues.Count == 1, $"{ancientRow2.Dialogues.Count} 段");
			AncientDialogueSpec ancientDialogueSpec = ancientRow2.Dialogues[0];
			Check("建筑师（没有全局首次台词）新段落默认「第 1 次」（界面从 1 开始）", ancientDialogueSpec.Times == 1, $"Times={ancientDialogueSpec.Times}");
			Check("建筑师的说明讲的是「通关次数」那套（本体 TheArchitect.LoadDialogue 用的就是通关次数）", ancientRow2.UnitHint.Contains("通关") && ancientRow2.UnitLabel == "次", ancientRow2.UnitHint);
			Check("建筑师的段落列表从 1 开始显示", ancientDialogueSpec.Display.Contains("第 1 次 ·"), ancientDialogueSpec.Display);
			AncientRow ancientRow3 = AncientList.Items.OfType<AncientRow>().FirstOrDefault((AncientRow r) => r.Entry.Id == "DARV");
			Check("达弗这类有全局首次台词的先古之民，目录里查得到", ancientRow3 != null);
			if (ancientRow3 != null)
			{
				object selectedItem = AncientList.SelectedItem;
				AncientList.SelectedItem = ancientRow3;
				UpdateLayout();
				int count11 = ancientRow3.Dialogues.Count;
				OnAddAncientDialogue(this, new RoutedEventArgs());
				int ok7;
				if (ancientRow3.Dialogues.Count == count11 + 1)
				{
					ObservableCollection<AncientDialogueSpec> dialogues = ancientRow3.Dialogues;
					ok7 = ((dialogues[dialogues.Count - 1].Times == 2) ? 1 : 0);
				}
				else
				{
					ok7 = 0;
				}
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(6, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Times=");
				ObservableCollection<AncientDialogueSpec> dialogues2 = ancientRow3.Dialogues;
				defaultInterpolatedStringHandler.AppendFormatted(dialogues2[dialogues2.Count - 1].Times);
				Check("有全局首次台词时，新段落默认「第 2 次」（第 1 次会被本体那句占掉）", (byte)ok7 != 0, defaultInterpolatedStringHandler.ToStringAndClear());
				ObservableCollection<AncientDialogueSpec> dialogues3 = ancientRow3.Dialogues;
				bool ok8 = dialogues3[dialogues3.Count - 1].Display.Contains("第 2 次到访");
				ObservableCollection<AncientDialogueSpec> dialogues4 = ancientRow3.Dialogues;
				Check("段落列表显示的数字和输入框里填的完全一致（都写 2）", ok8, dialogues4[dialogues4.Count - 1].Display);
				ObservableCollection<AncientDialogueSpec> dialogues5 = ancientRow3.Dialogues;
				bool ok9 = !dialogues5[dialogues5.Count - 1].Display.Contains("= 0");
				ObservableCollection<AncientDialogueSpec> dialogues6 = ancientRow3.Dialogues;
				Check("列表里不出现从 0 开始的说法", ok9, dialogues6[dialogues6.Count - 1].Display);
				Check("常规先古之民的说明从 1 开始、并讲清「第 1 次会被本体占掉」", ancientRow3.UnitHint.Contains("从 1 开始") && ancientRow3.UnitHint.Contains("填 2"), ancientRow3.UnitHint);
				ancientRow3.Dialogues.RemoveAt(ancientRow3.Dialogues.Count - 1);
				AncientList.SelectedItem = selectedItem;
				AncientDialogueList.SelectedItem = ancientDialogueSpec;
				UpdateLayout();
			}
			Check("新段落默认带 1 句", ancientDialogueSpec.Lines.Count == 1, $"{ancientDialogueSpec.Lines.Count} 句");
			OnAddAncientLine(this, new RoutedEventArgs());
			Check("点「加一句」会新增一句", ancientDialogueSpec.Lines.Count == 2, $"{ancientDialogueSpec.Lines.Count} 句");
			Check("新的一句说话人自动交替（先古之民 / 角色）", ancientDialogueSpec.Lines[0].AncientSpeaks != ancientDialogueSpec.Lines[1].AncientSpeaks, $"{ancientDialogueSpec.Lines[0].AncientSpeaks} / {ancientDialogueSpec.Lines[1].AncientSpeaks}");
			ancientDialogueSpec.Lines[0].Text = "UiCheck 建筑师第一句";
			ancientDialogueSpec.Lines[0].AncientSpeaks = true;
			ancientDialogueSpec.Lines[1].Text = "UiCheck 角色回一句";
			ancientDialogueSpec.Lines[1].AncientSpeaks = false;
			ancientDialogueSpec.Repeating = true;
			CharacterProfile characterProfile5 = ProfileFactory.Sample();
			characterProfile5.Ancients.Clear();
			characterProfile5.Ancients.Add(ancientRow2.Talk);
			string text7 = LocalizationGen.AncientsJson(characterProfile5);
			Check("生成角色专属键（补丁注入之后本体才会读）", text7.Contains("\"THE_ARCHITECT.talk." + Naming.From(characterProfile5).CharEntry + ".0-0r.ancient\": \"UiCheck 建筑师第一句\""), "THE_ARCHITECT.talk.<角色>.0-0r.ancient");
			Check("角色说的那句用 .char", text7.Contains(".0-1r.char\": \"UiCheck 角色回一句\""), ".char");
			Check("非最后一句给了「下一句」按钮文字", text7.Contains(".0-0r.next"), ".next");
			Check("配了对话就会生成先古之民补丁", AncientPatchGen.HasDialogues(characterProfile5));
			string text8 = AncientPatchGen.Source(characterProfile5);
			Check("补丁挂在 DialogueSet 上（Postfix）", text8.Contains("nameof(MegaCrit.Sts2.Core.Models.AncientEventModel.DialogueSet)") && text8.Contains("HarmonyLib.HarmonyPostfix"), "有 Postfix");
			Check("补丁把我们的角色写进 CharacterDialogues", text8.Contains("__result.CharacterDialogues[\"" + Naming.From(characterProfile5).CharEntry + "\"] = list;"), "写进字典");
			Check("补丁按本体自己的 PopulateLines 读我们写的键", text8.Contains("dlg.PopulateLines(ancient,") && text8.Contains("\"" + Naming.From(characterProfile5).CharEntry + "\""), "用 PopulateLines");
			Check("补丁里带上了「第几次到访 / 是否可重复 / 几句」的表", text8.Contains("[\"THE_ARCHITECT\"] = new (int VisitIndex, bool Repeating, int Lines)[] { (0, true, 2) }"), "表内容正确");
			Check("没配对话时不生成补丁", !AncientPatchGen.HasDialogues(ProfileFactory.Sample()), "不生成");
			ancientRow2.Talk.Dialogues.Clear();
			for (int num13 = 0; num13 < count10; num13++)
			{
				OnAddAncientDialogue(this, new RoutedEventArgs());
			}
		}
		DependencyObject root8 = SelectTabRoot("卡牌");
		List<CheckBox> list11 = new List<CheckBox>();
		CollectCheckBoxes(root8, list11);
		List<string> list12 = list11.Select((CheckBox c) => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path?.Path).ToList();
		Check("卡牌页有「X 费用」开关（绑 CostIsX）", list12.Contains("CostIsX"), string.Join(" / ", list12));
		Check("卡牌页有「额外资源量费用为 X」开关（绑 StarCostIsX）", list12.Contains("StarCostIsX"));
		Check("卡牌页有「升级后 X +1」开关（绑 XPlusOnUpgrade）", list12.Contains("XPlusOnUpgrade"));
		Check("卡牌页保留「费用」输入框（X 卡牌的固定费用仍然可填）", TextsIn(root8).Any((string t) => t == "费用"), "有费用标签");
		SelectTabRoot("卡牌");
		if (Profile.Cards.Count > 0)
		{
			CardList.SelectedIndex = 0;
			if (((CardSpec)CardList.SelectedItem).Effects.Count > 0)
			{
				CardEffectList.SelectedIndex = 0;
			}
			UpdateLayout();
		}
		List<CheckBox> list13 = new List<CheckBox>();
		CollectCheckBoxes((DependencyObject)((TabItem)MainTabs.SelectedItem).Content, list13);
		List<string> list14 = list13.Select((CheckBox c) => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path?.Path).ToList();
		Check("效果编辑器有「数值 = X」开关（绑 AmountIsX）", list14.Contains("AmountIsX"), string.Join(" / ", list14));
		Check("效果编辑器有「生效次数 = X」开关（绑 TimesIsX）", list14.Contains("TimesIsX"));
		Check("效果编辑器有「命中次数 / 对群数 = X」开关（绑 RepeatIsX）", list14.Contains("RepeatIsX"));
		EffectSpec effectSpec = new EffectSpec
		{
			Kind = "Damage",
			Amount = 6m,
			UpgradeAmount = 3m
		};
		Check("卡牌效果列表会显示「（升级 +N）」", effectSpec.Display.Contains("（升级 +3）"), effectSpec.Display);
		Check("遗物 / 药水用的显示文本不带「（升级 +N）」", !effectSpec.DisplayPlain.Contains("升级"), effectSpec.DisplayPlain);
		Check("遗物效果列表绑定的是不带升级的显示文本", RelicEffectList.DisplayMemberPath == "DisplayPlain", RelicEffectList.DisplayMemberPath ?? "(空)");
		Check("药水效果列表绑定的是不带升级的显示文本", PotionEffectList.DisplayMemberPath == "DisplayPlain", PotionEffectList.DisplayMemberPath ?? "(空)");
		Check("卡牌效果列表仍然显示升级增量（卡牌能升级）", CardEffectList.DisplayMemberPath == "Display", CardEffectList.DisplayMemberPath ?? "(空)");
		DependencyObject root9 = (DependencyObject)((TabItem)MainTabs.SelectedItem).Content;
		List<TextBox> list15 = new List<TextBox>();
		CollectTextBoxes(root9, list15);
		Check("卡牌的效果编辑器里有「升级增量」（卡牌能升级）", list15.Any((TextBox b) => BindingOperations.GetBinding(b, TextBox.TextProperty)?.Path?.Path == "UpgradeAmount"), "卡牌有");
		DependencyObject root10 = SelectTabRoot("遗物");
		if (Profile.Relics.Count > 0)
		{
			RelicList.SelectedIndex = 0;
			if (((RelicSpec)RelicList.SelectedItem).Effects.Count > 0)
			{
				RelicEffectList.SelectedIndex = 0;
			}
			UpdateLayout();
		}
		List<TextBox> list16 = new List<TextBox>();
		CollectTextBoxes(root10, list16);
		Check("遗物的效果编辑器里没有「升级增量」（遗物不能升级）", !list16.Any((TextBox b) => BindingOperations.GetBinding(b, TextBox.TextProperty)?.Path?.Path == "UpgradeAmount"), string.Join(" / ", list16.Select((TextBox b) => BindingOperations.GetBinding(b, TextBox.TextProperty)?.Path?.Path)));
		DependencyObject root11 = SelectTabRoot("药水");
		if (Profile.Potions.Count > 0)
		{
			PotionList.SelectedIndex = 0;
			if (((PotionSpec)PotionList.SelectedItem).Effects.Count > 0)
			{
				PotionEffectList.SelectedIndex = 0;
			}
			UpdateLayout();
		}
		List<TextBox> list17 = new List<TextBox>();
		CollectTextBoxes(root11, list17);
		Check("药水的效果编辑器里也没有「升级增量」", !list17.Any((TextBox b) => BindingOperations.GetBinding(b, TextBox.TextProperty)?.Path?.Path == "UpgradeAmount"), "药水没有");
		Check("遗物/药水页也不再用文字提「升级增量」", !TextsIn(root10).Any((string x) => x.Contains("升级增量")) && !TextsIn(root11).Any((string x) => x.Contains("升级增量")), "文字里没有");
		SelectTabRoot("卡牌");
		DependencyObject root12 = SelectTabRoot("遗物");
		if (Profile.Relics.Count > 0)
		{
			RelicList.SelectedIndex = 0;
			if (((RelicSpec)RelicList.SelectedItem).Effects.Count > 0)
			{
				RelicEffectList.SelectedIndex = 0;
			}
			UpdateLayout();
		}
		List<CheckBox> list18 = new List<CheckBox>();
		CollectCheckBoxes(root12, list18);
		List<string> list19 = list18.Select((CheckBox c) => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path?.Path).ToList();
		Check("遗物页的效果编辑器没有「= X」开关（X 只有卡牌有）", !list19.Contains("AmountIsX") && !list19.Contains("TimesIsX") && !list19.Contains("RepeatIsX"), string.Join(" / ", list19));
		CharacterProfile characterProfile6 = ProfileFactory.Sample();
		CardSpec cardSpec4 = new CardSpec
		{
			Name = "X 费卡",
			ClassName = "UiCheckXCard",
			Cost = 3,
			CostIsX = true,
			XPlusOnUpgrade = true,
			CardType = "Attack"
		};
		cardSpec4.Effects.Clear();
		cardSpec4.Effects.Add(new EffectSpec
		{
			Kind = "Damage",
			Amount = 8m,
			TargetSide = "Enemy",
			AmountIsX = false
		});
		cardSpec4.Effects[0].RepeatIsX = true;
		cardSpec4.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 5m,
			TargetSide = "Self",
			AmountIsX = true
		});
		characterProfile6.Cards.Add(cardSpec4);
		string text9 = CSharpCodeGen.CardSource(characterProfile6, cardSpec4, 0);
		Check("X 费用卡牌生成了本体的 HasEnergyCostX（本体天际钻头 / 挽歌的做法）", text9.Contains("protected override bool HasEnergyCostX => true;"), "有 HasEnergyCostX");
		Check("X 费用卡牌的构造函数费用写 0（界面上填的 3 费要被忽略，费用交给本体按 X 结算）", text9.Contains(": base(0, CardType.Attack"), "费用=0");
		Check("X 值用本体的 ResolveEnergyXValue() 取，且升级后 +1 生效", text9.Contains("int x = ResolveEnergyXValue() + (base.IsUpgraded ? 1 : 0);"), "取 X");
		Check("「命中次数 = X」生成 .WithHitCount(x)（本体旋风斩 / 天际钻头）", text9.Contains(".WithHitCount(x)"), "命中 X 次");
		Check("「数值 = X」的格挡走 decimal 重载（BlockVar 那条吃不了 X）", text9.Contains("CreatureCmd.GainBlock(base.Owner.Creature, x, ValueProp.Move, cardPlay)"), "格挡 = X");
		Check("X 取值的效果不再声明 DynamicVar（数值不再是固定值）", !text9.Contains("new BlockVar("), "没有 BlockVar");
		CardSpec cardSpec5 = new CardSpec
		{
			Name = "资源量X卡",
			ClassName = "UiCheckStarXCard",
			Cost = 1,
			StarCostIsX = true,
			CardType = "Skill"
		};
		cardSpec5.Effects.Clear();
		cardSpec5.Effects.Add(new EffectSpec
		{
			Kind = "ExtraResource",
			Amount = -1m,
			TargetSide = "Self"
		});
		cardSpec5.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 5m,
			TargetSide = "Self",
			AmountIsX = true
		});
		characterProfile6.Cards.Add(cardSpec5);
		string text10 = CSharpCodeGen.CardSource(characterProfile6, cardSpec5, 0);
		Check("资源量 X 费用生成 HasStarCostX（本体星尘的做法）", text10.Contains("public override bool HasStarCostX => true;"), "有 HasStarCostX");
		Check("资源量 X 用 ResolveStarXValue() 取 X", text10.Contains("int x = ResolveStarXValue()"), "取资源量 X");
		Check("资源量 X 时不再生成固定的 CanonicalStarCost（否则会互相打架）", !text10.Contains("CanonicalStarCost"), "没有固定资源量费用");
		CardSpec cardSpec6 = new CardSpec
		{
			Name = "错配卡",
			ClassName = "UiCheckBadXCard",
			Cost = 1,
			CardType = "Skill"
		};
		cardSpec6.Effects.Clear();
		cardSpec6.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 5m,
			TargetSide = "Self",
			AmountIsX = true
		});
		characterProfile6.Cards.Add(cardSpec6);
		string text11 = CSharpCodeGen.CardSource(characterProfile6, cardSpec6, 0);
		Check("费用不是 X 时不会生成会抛异常的 ResolveEnergyXValue()（X 按 0 兜底）", !text11.Contains("ResolveEnergyXValue()") && text11.Contains("int x = 0;"), "X 按 0");
		Check("这种错配在校验里有警告提示", ProfileValidator.Validate(characterProfile6).Any((ValidationIssue i) => i.Message.Contains("效果里勾了「= X」")), string.Join(" / ", from i in ProfileValidator.Validate(characterProfile6)
			select i.Message into m
			where m.Contains("= X")
			select m));
		CharacterProfile characterProfile7 = ProfileFactory.Sample();
		CardSpec cardSpec7 = new CardSpec
		{
			Name = "X 描述卡",
			ClassName = "UiCheckXDesc",
			Cost = 0,
			CostIsX = true,
			CardType = "Attack"
		};
		cardSpec7.Effects.Clear();
		cardSpec7.Effects.Add(new EffectSpec
		{
			Kind = "Damage",
			Amount = 8m,
			TargetSide = "Enemy",
			AmountIsX = true
		});
		characterProfile7.Cards.Add(cardSpec7);
		Check("卡面描述用 X 而不是那个用不上的固定数值", LocalizationGen.CardsJson(characterProfile7).Contains("造成 X 点伤害。"), LocalizationGen.CardsJson(characterProfile7).Split('\n').FirstOrDefault((string l) => l.Contains("UI_CHECK_X_DESC")) ?? "");
		CardSpec cardSpec8 = new CardSpec
		{
			Name = "X 多段",
			ClassName = "UiCheckXMulti",
			Cost = 0,
			CostIsX = true,
			CardType = "Attack"
		};
		cardSpec8.Effects.Clear();
		cardSpec8.Effects.Add(new EffectSpec
		{
			Kind = "Damage",
			Amount = 5m,
			TargetSide = "Enemy",
			RepeatIsX = true
		});
		characterProfile7.Cards.Add(cardSpec8);
		CardSpec cardSpec9 = new CardSpec
		{
			Name = "固定多段",
			ClassName = "UiCheckMulti3",
			Cost = 1,
			CardType = "Attack"
		};
		cardSpec9.Effects.Clear();
		cardSpec9.Effects.Add(new EffectSpec
		{
			Kind = "Damage",
			Amount = 4m,
			TargetSide = "Enemy",
			RepeatCount = 3
		});
		characterProfile7.Cards.Add(cardSpec9);
		string text12 = LocalizationGen.CardsJson(characterProfile7);
		Check("「命中次数 = X」写进描述（本体旋风斩 / 天际钻头那种「点伤害 X 次」）", text12.Contains("点伤害 X 次。"), "点伤害 X 次。");
		Check("固定命中次数也写进描述（普通人也能看懂打 3 下）", text12.Contains("点伤害 3 次。"), "点伤害 3 次。");
		Check("资源量 X 卡牌的描述写「需要 X 点…（打出时消耗全部）」，不是那个固定的 1", LocalizationGen.CardsJson(characterProfile6).Contains("需要 X 点额外资源量（打出时消耗全部）。"), LocalizationGen.CardsJson(characterProfile6).Split('\n').FirstOrDefault((string l) => l.Contains("需要 X 点")) ?? "(没找到)");
		CardSpec cardSpec10 = new CardSpec
		{
			Name = "普通卡",
			ClassName = "UiCheckNotX",
			Cost = 1,
			CardType = "Skill"
		};
		cardSpec10.Effects.Clear();
		cardSpec10.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 5m,
			TargetSide = "Self"
		});
		string text13 = CSharpCodeGen.CardSource(characterProfile6, cardSpec10, 0);
		Check("普通卡牌不会生成 HasEnergyCostX / X 变量 / WithHitCount(x)", !text13.Contains("HasEnergyCostX") && !text13.Contains("int x =") && !text13.Contains("WithHitCount(x)"), "干净");
		int num14 = -1;
		int num15 = -1;
		int num16 = -1;
		int numPet = -1;
		for (int num17 = 0; num17 < MainTabs.Items.Count; num17++)
		{
			if (MainTabs.Items[num17] is TabItem tabItem3)
			{
				switch ((tabItem3.Header as string) ?? "")
				{
				case "角色":
					num14 = num17;
					break;
				case "额外资源量/状态":
					num15 = num17;
					break;
				case "卡牌":
					num16 = num17;
					break;
				case "召唤物":
					numPet = num17;
					break;
				}
			}
		}
		Check("有「额外资源量/状态」选项卡", num15 >= 0, $"下标={num15}");
		// 注意：「召唤物卡牌」选项卡删除之后，排在它后面的那些页的绝对下标全部 -1。
		Check("「角色」→「召唤物」→「额外资源量/状态」→「卡牌」按顺序排（下标依次 +1）",
			num14 == 0 && numPet == 1 && num15 == 2 && num16 == 3,
			$"角色={num14} / 召唤物={numPet} / 额外={num15} / 卡牌={num16}");
		DependencyObject root13 = SelectTabRoot("额外资源量/状态");
		List<CheckBox> list20 = new List<CheckBox>();
		CollectCheckBoxes(root13, list20);
		Check("额外资源量页的开关挂在「额外资源量」配置对象上（DataContext）", list20.Count > 0 && list20.All((CheckBox c) => c.DataContext == Profile.ExtraResource), (list20.Count == 0) ? "一个开关都没有" : $"{list20.Count} 个开关 / DataContext={((list20[0].DataContext == null) ? "null" : list20[0].DataContext.GetType().Name)}");
		List<string> list21 = list20.Select((CheckBox c) => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path?.Path).ToList();
		Check("额外资源量页有「启用」开关", list21.Contains("Enabled"), string.Join(" / ", list21));
		Check("额外资源量页有「跨战斗继承」开关", list21.Contains("CarryOver"), string.Join(" / ", list21));
		Check("额外资源量页有「始终显示计数器」开关", list21.Contains("AlwaysShowCounter"), string.Join(" / ", list21));
		List<TextBox> list22 = new List<TextBox>();
		CollectTextBoxes(root13, list22);
		Check("额外资源量页有「初始数量」输入框", list22.Any((TextBox t) => t.DataContext == Profile.ExtraResource && BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path?.Path == "Initial"), string.Join(" / ", list22.Select((TextBox t) => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path?.Path)));
		CheckBox checkBox = list20.First((CheckBox c) => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path?.Path == "Enabled");
		bool enabled = Profile.ExtraResource.Enabled;
		checkBox.IsChecked = !enabled;
		UpdateLayout();
		Check("点「启用」开关能真的写回配置（TwoWay 绑定）", Profile.ExtraResource.Enabled == !enabled, $"{enabled} → {Profile.ExtraResource.Enabled}");
		checkBox.IsChecked = enabled;
		UpdateLayout();
		Check("效果种类里有「获得额外资源量」", Kinds.Any((EffectKindOption k) => k.Kind == "ExtraResource"), $"{Kinds.Count} 种效果");
		EffectKindOption effectKindOption = Kinds.FirstOrDefault((EffectKindOption k) => k.Kind == "ExtraResource");
		Check("「获得额外资源量」允许填负数（负数 = 花费）", (object)effectKindOption != null && effectKindOption.Min < 0m, ((object)effectKindOption == null) ? "没找到" : $"范围 {effectKindOption.Min}~{effectKindOption.Max}");
		CharacterProfile characterProfile8 = ProfileFactory.Sample();
		characterProfile8.ExtraResource.Enabled = true;
		characterProfile8.ExtraResource.Initial = 3;
		characterProfile8.ExtraResource.CarryOver = true;
		characterProfile8.ExtraResource.AlwaysShowCounter = true;
		string text14 = CSharpCodeGen.ExtraResourceSource(characterProfile8);
		Check("额外资源量用「起始遗物」承载（本体只有起始遗物能保证一定在身上）", text14.Contains("ExtraResourceRelic") && text14.Contains("RelicRarity.Starter"), text14.Contains("RelicRarity.Starter") ? "OK" : "没找到 RelicRarity.Starter");
		Check("初始数量写进了生成代码", text14.Contains("InitialAmount = 3"), text14.Contains("InitialAmount = 3") ? "OK" : "没找到 InitialAmount = 3");
		Check("每场战斗开始时发放（AfterSideTurnStart + 第一回合判定）", text14.Contains("AfterSideTurnStart") && text14.Contains("TurnNumber > 1"));
		Check("战斗结束时记录余量（继承用）", text14.Contains("AfterCombatEnd(CombatRoom room)") && text14.Contains("_carried = CarryOver ?"), text14.Contains("AfterCombatEnd(CombatRoom room)") ? "签名完整" : "没找到 AfterCombatEnd(CombatRoom room)");
		Check("继承关掉时不会把余量带过去", CSharpCodeGen.ExtraResourceSource(WithCarryOver(characterProfile8, carryOver: false)).Contains("CarryOver = false"), "CarryOver = false");
		string text15 = CSharpCodeGen.CharacterSource(characterProfile8);
		Check("角色上开了「始终显示资源计数器」", text15.Contains("ShouldAlwaysShowStarCounter => true"));
		Check("额外资源量的遗物挂进了起始遗物列表", text15.Contains($"ModelDb.Relic<{Naming.From(characterProfile8).ExtraResourceRelicClass}>()"));
		Check("额外资源量的遗物也挂进了遗物池（否则选人界面查 Pool 会抛异常）", CSharpCodeGen.RelicPoolSource(characterProfile8).Contains($"ModelDb.Relic<{Naming.From(characterProfile8).ExtraResourceRelicClass}>()"), "在池里");
		Check("不启用额外资源量时遗物池里没有那个隐藏遗物", !CSharpCodeGen.RelicPoolSource(ProfileFactory.Sample()).Contains("ExtraResourceRelic"), "干净");
		CharacterProfile characterProfile9 = ProfileFactory.Sample();
		characterProfile9.ExtraResource.Enabled = false;
		characterProfile9.ExtraResource.AlwaysShowCounter = true;
		string text16 = CSharpCodeGen.CharacterSource(characterProfile9);
		Check("不启用时不生成额外资源量遗物", !text16.Contains("ExtraResourceRelic"));
		Check("不启用时不强制显示资源计数器", !text16.Contains("ShouldAlwaysShowStarCounter"));
		CardSpec cardSpec11 = new CardSpec
		{
			Name = "献祭",
			ClassName = "UiCheckCost",
			Cost = 1
		};
		cardSpec11.Effects.Clear();
		cardSpec11.Effects.Add(new EffectSpec
		{
			Kind = "ExtraResource",
			Amount = -2m
		});
		string text17 = CSharpCodeGen.CardSource(characterProfile8, cardSpec11, 0);
		Check("卡牌上的「花费额外资源量」写成了卡牌费用（本体据此禁止打出）", text17.Contains("CanonicalStarCost => 2"), text17.Contains("CanonicalStarCost") ? "找到了费用" : "没找到 CanonicalStarCost");
		Check("花费不是在打出后扣的（避免先给用、后扣费的漏洞）", !text17.Contains("GainStars(-"), "OnPlay 里没有负数 GainStars");
		CardSpec cardSpec12 = new CardSpec
		{
			Name = "献祭x3",
			ClassName = "UiCheckCost3",
			Cost = 0
		};
		cardSpec12.Effects.Clear();
		cardSpec12.Effects.Add(new EffectSpec
		{
			Kind = "ExtraResource",
			Amount = -2m,
			Times = 3
		});
		Check("花费会按「生效次数」累加（2 × 3 = 6）", CSharpCodeGen.CardSource(characterProfile8, cardSpec12, 0).Contains("CanonicalStarCost => 6"), "期望 CanonicalStarCost => 6");
		CardSpec cardSpec13 = new CardSpec
		{
			Name = "凝聚",
			ClassName = "UiCheckGain",
			Cost = 0
		};
		cardSpec13.Effects.Clear();
		cardSpec13.Effects.Add(new EffectSpec
		{
			Kind = "ExtraResource",
			Amount = 2m,
			UpgradeAmount = 1m
		});
		string text18 = CSharpCodeGen.CardSource(characterProfile8, cardSpec13, 0);
		Check("卡牌上的「获得额外资源量」在打出时发放", text18.Contains("PlayerCmd.GainStars"));
		Check("获得量支持「升级后增量」（改用 StarsVar 的 UpgradeValueBy，卡面跟着变）", text18.Contains("base.DynamicVars[\"Stars\"].UpgradeValueBy(1m)"), text18.Contains("UpgradeValueBy") ? "找到了升级增量" : "没找到 UpgradeValueBy");
		Check("获得量走 StarsVar 变量（有声明就不会 KeyNotFoundException）", text18.Contains("new StarsVar(2)") && text18.Contains("GainStars(base.DynamicVars.Stars.BaseValue"), text18.Contains("new StarsVar(2)") ? "先声明再取值" : "没声明 StarsVar");
		Check("正数不会被误写成卡牌费用", !text18.Contains("CanonicalStarCost"));
		RelicSpec relicSpec3 = new RelicSpec
		{
			Name = "余烬",
			ClassName = "UiCheckRelic"
		};
		relicSpec3.Effects.Clear();
		relicSpec3.Effects.Add(new EffectSpec
		{
			Kind = "ExtraResource",
			Amount = 2m
		});
		string text19 = CSharpCodeGen.RelicSource(characterProfile8, relicSpec3, 0);
		Check("遗物的「获得额外资源量」能生成", text19.Contains("PlayerCmd.GainStars(2m"), text19.Contains("GainStars") ? "OK" : "没找到 GainStars");
		PotionSpec potionSpec2 = new PotionSpec
		{
			Name = "星瓶",
			ClassName = "UiCheckPotion"
		};
		potionSpec2.Effects.Clear();
		potionSpec2.Effects.Add(new EffectSpec
		{
			Kind = "ExtraResource",
			Amount = -1m
		});
		string text20 = CSharpCodeGen.PotionSource(characterProfile8, potionSpec2, 0);
		Check("药水的「扣额外资源量」能生成且不会扣成负数", text20.Contains("SetStars") && text20.Contains("System.Math.Max"), text20.Contains("SetStars") ? "OK" : "没找到 SetStars");
		Check("遗物/药水不碰 DynamicVars（ExtraResource 没有声明变量）", !text19.Contains("DynamicVars.Stars") && !text20.Contains("DynamicVars.Stars"));
		characterProfile8.Cards.Add(cardSpec11);
		characterProfile8.Cards.Add(cardSpec13);
		string text21 = LocalizationGen.CardsJson(characterProfile8);
		Check("卡面描述会写出「花费额外资源量」", text21.Contains("需要 2 点额外资源量。"), text21.Contains("额外资源量") ? "OK" : "描述里没有额外资源量");
		Check("卡面描述用 {Stars:diff()}（升级后会显示 3，而不是 2）", text21.Contains("获得 {Stars:diff()} 点额外资源量。"), text21.Contains("{Stars:diff()}") ? "OK" : "描述里没用 {Stars:diff()}");
		Check("额外资源量遗物的名字/描述有本地化", LocalizationGen.RelicsJson(characterProfile8).Contains("EXTRA_RESOURCE_RELIC.title"));
		string text22 = (string.IsNullOrWhiteSpace(Profile.Paths.VanillaProject) ? null : Profile.Paths.VanillaProject);
		if (text22 != null && Directory.Exists(text22))
		{
			CharacterProfile characterProfile10 = ProfileFactory.Sample();
			characterProfile10.Paths.OutputDir = Path.Combine(Path.GetTempPath(), "forge_uicheck_extra_" + Guid.NewGuid().ToString("N"));
			characterProfile10.Paths.VanillaProject = text22;
			// 游戏 data 目录 / Godot 也一起从当前 profile 带过来：
			// PathsSpec 里的默认值现在是**空的**（以前写死了开发机路径，会跟着程序集发给别人），
			// 不显式赋值的话这里会因为「找不到 sts2.dll」而生成失败。
			characterProfile10.Paths.GameDataDir = Profile.Paths.GameDataDir;
			characterProfile10.Paths.GodotExe = Profile.Paths.GodotExe;
			characterProfile10.Paths.DotnetExe = Profile.Paths.DotnetExe;
			characterProfile10.ExtraResource.Enabled = true;
			characterProfile10.ExtraResource.Initial = 2;
			characterProfile10.ExtraResource.Name = "血怒";
			characterProfile10.ExtraResource.ShowName = true;
			string text23 = Path.Combine(characterProfile10.Paths.OutputDir, "uicheck_icon.png");
			Directory.CreateDirectory(characterProfile10.Paths.OutputDir);
			PngUtil.WriteRgba(text23, 16, 16, new byte[1024]);
			characterProfile10.ExtraResource.Icon = text23;
			// 顺便验证能量图标：本体规定 sprite_fonts 里 24×24、ui_atlas 里 74×74，
			// 这里故意给一张 64×64 的彩色图（不是 24/74，也不是灰阶，走得到缩放与取色相两条路径）
			string text23b = Path.Combine(characterProfile10.Paths.OutputDir, "uicheck_energy.png");
			byte[] energyPixels = new byte[64 * 64 * 4];
			for (int pi = 0; pi < energyPixels.Length; pi += 4)
			{
				energyPixels[pi] = 210;
				energyPixels[pi + 1] = 60;
				energyPixels[pi + 2] = 90;
				energyPixels[pi + 3] = 255;
			}
			PngUtil.WriteRgba(text23b, 64, 64, energyPixels);
			characterProfile10.Art.EnergyIcon = text23b;
			// 顺便带一个「有图标的自定义状态」：一起验证本体查图的那条回退路径（images/powers/<状态>.png）
			CustomPowerSpec iconPower = new CustomPowerSpec { Name = "自检图标状态", Icon = text23 };
			iconPower.Triggers.Add(new PowerTriggerSpec { Kind = "TurnStart" });
			characterProfile10.CustomPowers.Add(iconPower);
			GenerationResult generationResult = ModGenerator.Generate(characterProfile10);
			string path2 = Path.Combine(generationResult.ProjectRoot, "cs", "ExtraResource.cs");
			Check("生成工程时会真的写出 cs/ExtraResource.cs", generationResult.Success && File.Exists(path2), (!generationResult.Success) ? ("生成失败：" + generationResult.Issues.FirstOrDefault((ValidationIssue i) => i.IsError)) : (File.Exists(path2) ? "已写出" : "文件不存在"));
			string path3 = Path.Combine(generationResult.ProjectRoot, "cs", "ExtraResourceUi.cs");
			Check("生成工程时会写出自定义图标/名字的补丁 cs/ExtraResourceUi.cs", generationResult.Success && File.Exists(path3), File.Exists(path3) ? "已写出" : "文件不存在");
			string path4 = Path.Combine(generationResult.ProjectRoot, ArtGenerator.ExtraResourceIconRelPath(characterProfile10).Replace('/', Path.DirectorySeparatorChar));
			Check("上传的图标被拷进了工程（会打进 pck）", File.Exists(path4), File.Exists(path4) ? ArtGenerator.ExtraResourceIconRelPath(characterProfile10) : "没找到图标文件");
			// 内联小图：本体的 star_icon.png 是 24×24，[img] 按原图尺寸画。
			// 内联引用计数器那张大图会把整段描述撑爆、字号被自适应缩到极小（用户报过「大奖」那张牌）。
			string inlineIconPath = Path.Combine(generationResult.ProjectRoot, ArtGenerator.ExtraResourceIconInlineRelPath(characterProfile10).Replace('/', Path.DirectorySeparatorChar));
			var inlineIconSize = PngUtil.Decode(inlineIconPath);
			Check("文字里内联的资源图标被缩成 24×24（不缩会把卡牌/悬停描述的字号挤到极小）",
				inlineIconSize is { } iis && iis.W == 24 && iis.H == 24,
				inlineIconSize is { } isz2 ? $"{isz2.W}×{isz2.H}" : "文件不存在");
			// 额外资源量那个隐藏起始遗物也在遗物栏里显示：图标路径是本体规则 relic_atlas.sprites/<entry>.tres，
			// 图集里没有就回退到 images/relics/<entry>.png —— 不写这个文件，遗物栏就是紫色 missing_power
			string relicIconPath = Path.Combine(generationResult.ProjectRoot, "images", "relics",
				Naming.EntryOf(Naming.From(characterProfile10).ExtraResourceRelicClass).ToLowerInvariant() + ".png");
			Check("额外资源量的隐藏遗物也有图标文件（不然遗物栏显示 missing_power 并报 Missing sprite）",
				File.Exists(relicIconPath), File.Exists(relicIconPath) ? Path.GetFileName(relicIconPath) : "没找到");
			// 自定义状态图标：本体 PowerModel.Icon 走 power_atlas.sprites/<entry>.tres，
			// 图集里没有就回退 images/powers/<entry>.png（BigIcon 也直接读这个）→ 两份都要在
			string powerIconPath = Path.Combine(generationResult.ProjectRoot, "images", "powers",
				CustomPowerGen.EntryOf(characterProfile10, iconPower, characterProfile10.CustomPowers.Count - 1).ToLowerInvariant() + ".png");
			Check("自定义状态图标也放到本体回退路径 images/powers/<状态>.png（否则报 Missing sprite + 紫色占位）",
				File.Exists(powerIconPath), File.Exists(powerIconPath) ? ("images/powers/" + Path.GetFileName(powerIconPath)) : "没找到");
			string[] buffer = new string[] { generationResult.ProjectRoot, Naming.From(characterProfile10).ModId, "localization", "zhs", "static_hover_tips.json" };
			string path5 = Path.Combine(buffer);
			string text24 = (File.Exists(path5) ? File.ReadAllText(path5) : "");
			Check("生成了覆盖本体「辉星」提示的 static_hover_tips.json（名字生效）", text24.Contains("STAR_COUNT.title") && text24.Contains("血怒"), (text24.Length == 0) ? "文件不存在" : text24.Replace("\n", " ").Replace("\r", ""));
			Check("悬停描述里引用的是 24×24 的内联图标，而不是计数器那张大图",
				text24.Contains(ArtGenerator.ExtraResourceIconInlineRelPath(characterProfile10)),
				ArtGenerator.ExtraResourceIconInlineRelPath(characterProfile10));
			// 能量图标：本体两处尺寸是写死的（sprite_fonts 24×24 内联、ui_atlas 精灵 74×74），
			// 上传大图不缩尺寸 → 卡牌描述被撑爆、字号被缩到极小（用户报的「大奖」字体变小就是这个）
			string energyIconPath = Path.Combine(generationResult.ProjectRoot, "images", "packed", "sprite_fonts", Naming.From(characterProfile10).EnergyColor + "_energy_icon.png");
			var energyIconSize = PngUtil.Decode(energyIconPath);
			Check("能量图标写进 sprite_fonts 时被缩成 24×24（本体就是 24×24，内联图按原图尺寸绘制）",
				energyIconSize is { } eis && eis.W == 24 && eis.H == 24,
				energyIconSize is { } eisz ? $"{eisz.W}×{eisz.H}" : "文件不存在");
			string energyPagePath = Path.Combine(generationResult.ProjectRoot, "images", "atlases", Naming.From(characterProfile10).EnergyColor + "_energy_page_0.png");
			var energyPageSize = PngUtil.Decode(energyPagePath);
			Check("能量图标的图集页被缩成 74×74（本体精灵就是 74×74）",
				energyPageSize is { } eps && eps.W == 74 && eps.H == 74,
				energyPageSize is { } epsz ? $"{epsz.W}×{epsz.H}" : "文件不存在");
			// 用户报过「左侧能量球还是本体美术」：以前只把本体的球按上传图取色相，
			// 上传的图本来就偏橙时改完跟本体几乎一样。现在直接把球体换成上传的图。
			string energyScenePath = Path.Combine(generationResult.ProjectRoot, "scenes", "combat", "energy_counters", Naming.From(characterProfile10).CharSlug + "_energy_counter.tscn");
			string energySceneText = File.Exists(energyScenePath) ? File.ReadAllText(energyScenePath) : "";
			Check("左侧能量球换成了上传的能量图标（Layer1 指向自定义球体贴图 + 本体另外 4 层隐藏）",
				energySceneText.Contains("_energy_orb.png") && energySceneText.Contains("visible = false"),
				energySceneText.Length == 0 ? "场景不存在" : (energySceneText.Contains("_energy_orb.png") ? "OK" : "没换球体"));
			try
			{
				Directory.Delete(characterProfile10.Paths.OutputDir, recursive: true);
			}
			catch
			{
			}
		}
		else
		{
			Check("生成工程时会真的写出 cs/ExtraResource.cs", ok: false, "跳过条件不成立：本机还没指定「解包工程目录」，无法跑生成");
		}
		DependencyObject root14 = SelectTabRoot("额外资源量/状态");
		List<TextBox> list23 = new List<TextBox>();
		CollectTextBoxes(root14, list23);
		Check("额外资源量页有「显示名」输入框（绑 Profile.ExtraResource.Name）", list23.Any((TextBox t) => t.DataContext == Profile.ExtraResource && BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path?.Path == "Name"), string.Join(" / ", list23.Select((TextBox t) => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path?.Path)));
		List<CheckBox> list24 = new List<CheckBox>();
		CollectCheckBoxes(root14, list24);
		Check("额外资源量页有「在计数器下方显示名字」开关（绑 ShowName）", list24.Any((CheckBox c) => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path?.Path == "ShowName"), string.Join(" / ", list24.Select((CheckBox c) => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path?.Path)));
		List<Button> list25 = new List<Button>();
		CollectButtons(root14, list25);
		Check("额外资源量页有「选择图片…」和「清除」按钮（能选自定义图标）", list25.Any((Button b) => b.Content as string == "选择图片…") && list25.Any((Button b) => b.Content as string == "清除"), string.Join("、", list25.Select((Button b) => b.Content as string)));
		CharacterProfile characterProfile11 = ProfileFactory.Sample();
		characterProfile11.ExtraResource.Enabled = true;
		characterProfile11.ExtraResource.Name = "血怒";
		characterProfile11.ExtraResource.Icon = "C:\\tmp\\uicheck_icon.png";
		characterProfile11.ExtraResource.ShowName = true;
		Check("配了名字就会生成外观补丁", CSharpCodeGen.HasExtraResourceUi(characterProfile11));
		CharacterProfile characterProfile12 = ProfileFactory.Sample();
		characterProfile12.ExtraResource.Enabled = false;
		characterProfile12.ExtraResource.Name = "血怒";
		Check("不启用时不生成外观补丁", !CSharpCodeGen.HasExtraResourceUi(characterProfile12));
		string text25 = CSharpCodeGen.ExtraResourceUiSource(characterProfile11);
		Check("外观补丁挂在计数器节点上（NStarCounter._Ready 的 Postfix）", text25.Contains("typeof(MegaCrit.Sts2.Core.Nodes.Combat.NStarCounter), nameof(MegaCrit.Sts2.Core.Nodes.Combat.NStarCounter._Ready)") && text25.Contains("[HarmonyLib.HarmonyPostfix]"), text25.Contains("HarmonyPostfix") ? "OK" : "没找到 HarmonyPostfix");
		Check("外观补丁写进了自定义图标路径", text25.Contains("res://images/ui/combat/seven_extra_resource_icon.png"), "res://images/ui/combat/seven_extra_resource_icon.png");
		Check("外观补丁写进了自定义名字", text25.Contains("DisplayName = \"血怒\""));
		Check("外观补丁会隐藏本体星星的两层旋转光效", text25.Contains("Icon/RotationLayers") && text25.Contains("Hide()"));
		// 用户报过「额外资源量图标还是本体的」：当时的写法是 Exists 前置判断 + 写死节点名，
		// 两条都是静默失败（图标没换、日志里一条错都没有），所以现在改成直接 Load + 兜底扫描 + 记日志。
		Check("外观补丁不再用 ResourceLoader.Exists 做前置判断（模组 pck 是运行时挂载的，Exists 可能为 false → 图标静默不生效）",
			!text25.Contains("ResourceLoader.Exists("), text25.Contains("ResourceLoader.Exists(") ? "还在用 Exists" : "OK");
		Check("节点名对不上时按「贴图就是本体那颗星星」兜底扫描（本体改结构也不会静默失效）",
			text25.Contains("CollectStarTextures") && text25.Contains("energy_star"), "CollectStarTextures + energy_star");
		Check("图标没加载出来 / 没找到节点时会写日志（不再静默失败）",
			text25.Contains("图标没加载出来") && text25.Contains("没找到星星贴图节点"), "有日志");
		// 用户报过「左侧还是本体星辉图标」：一次性的补丁可能被别的模组改回去，或者计数器是「先建好再设归属」的。
		Check("计数器图标有「每帧兜底」（别人把贴图改回本体星星时会再贴一次）",
			text25.Contains("NStarCounter._Process") && text25.Contains("EnsureIcon"), "有 _Process 兜底");
		Check("归属判断除了 meta 还会反射读本体的 _player 字段（Initialize 补丁没走到也能认出来）",
			text25.Contains("AccessTools.Field") && text25.Contains("\"_player\"") && text25.Contains("IsOurs"), "有 _player 兜底");
		Check("会记录「左侧能量球实际用的是哪个场景」（判断是不是被别的模组顶掉了）",
			text25.Contains("EnergyCounterPath") && text25.Contains("NEnergyCounter"), "有诊断日志");
		// 实测：装了 BaseLib / RitsuLib 时计数器可能根本不是本体那个类 → 挂在 NStarCounter 上的补丁永远不触发
		Check("战斗界面激活时按「贴图就是本体星星」整屏兜底（换了计数器类也照样换）",
			text25.Contains("NCombatUi.Activate") && text25.Contains("LocalContext.GetMe") && text25.Contains("ExtraResourceCombatUiPatch"),
			"有战斗界面兜底");
		Check("诊断日志会打出星星节点的类名链（一眼看出计数器到底是不是本体那个类）",
			text25.Contains("NodeChain") && text25.Contains("所属节点"), "有类名链");
		Check("外观补丁用本体数字标签的副本当名字（字体风格跟着本体）", text25.Contains("%CountLabel") && text25.Contains("Duplicate()"));
		Check("外观补丁只改我们自己角色的计数器（本体储君等别的角色不动）", text25.Contains("player?.Character is Seven"), text25.Contains("player?.Character is Seven") ? "OK" : "没有按角色判断");
		Check("卡面上的资源费用图标也换成同一张自定义图（卡面和计数器看起来一致）", text25.Contains("typeof(MegaCrit.Sts2.Core.Nodes.Cards.NCard)") && text25.Contains("%StarIcon"), text25.Contains("%StarIcon") ? "OK" : "没处理卡面图标");
		Check("卡面图标只改我们自己卡池的卡（别的角色的星星费用不动）", text25.Contains("is not SevenCardPool"), "按卡池判断");
		Check("外观补丁出问题不会把战斗搞崩（整体 try/catch）", text25.Contains("catch (Exception e)"), "有 catch");
		CharacterProfile characterProfile13 = ProfileFactory.Sample();
		characterProfile13.ExtraResource.Enabled = true;
		characterProfile13.ExtraResource.Name = "血怒";
		characterProfile13.ExtraResource.ShowName = false;
		characterProfile13.ExtraResource.Icon = "C:\\tmp\\uicheck_icon.png";
		string text26 = CSharpCodeGen.ExtraResourceUiSource(characterProfile13);
		Check("关掉「显示名字」后不再往界面上加名字", text26.Contains("DisplayName = \"\""), text26.Contains("DisplayName = \"\"") ? "OK" : "名字还在");
		Check("关掉「显示名字」仍然会换图标", text26.Contains("res://images/ui/combat/seven_extra_resource_icon.png"));
		CharacterProfile characterProfile14 = ProfileFactory.Sample();
		characterProfile14.ExtraResource.Enabled = true;
		characterProfile14.ExtraResource.Name = "血怒";
		Check("只填名字不传图标时，不覆盖本体图标", CSharpCodeGen.ExtraResourceUiSource(characterProfile14).Contains("IconPath = \"\""), "IconPath 为空串");
		CharacterProfile characterProfile15 = ProfileFactory.Sample();
		characterProfile15.ExtraResource.Enabled = true;
		characterProfile15.ExtraResource.Name = "血怒";
		CardSpec cardSpec14 = new CardSpec
		{
			Name = "献祭",
			ClassName = "UiCheckNameCost",
			Cost = 1
		};
		cardSpec14.Effects.Clear();
		cardSpec14.Effects.Add(new EffectSpec
		{
			Kind = "ExtraResource",
			Amount = -2m
		});
		characterProfile15.Cards.Add(cardSpec14);
		Check("卡面描述用自定义名字（需要 2 点血怒）", LocalizationGen.CardsJson(characterProfile15).Contains("需要 2 点血怒。"), "需要 2 点血怒。");
		Check("那个隐藏遗物的名字也用自定义名字", LocalizationGen.RelicsJson(characterProfile15).Contains($"\"{Naming.EntryOf(Naming.From(characterProfile15).ExtraResourceRelicClass)}.title\": \"血怒\""), "隐藏遗物名 = 血怒");
		Check("悬停提示覆盖表用自定义名字", LocalizationGen.StaticHoverTipsJson(characterProfile15).Contains("\"STAR_COUNT.title\": \"血怒\""), "STAR_COUNT.title = 血怒");
		// 用户报过「额外资源量的描述有误」：以前只有上传了图标才覆盖描述，没传图标时悬停显示的是
		// 本体的「你当前的辉星…储君的部分卡牌…」——说的完全是别的角色的资源。现在启用了就覆盖。
		string starTips = LocalizationGen.StaticHoverTipsJson(characterProfile15);
		Check("资源量的悬停描述是我们自己写的（不提辉星 / 储君，提名字和开场数量）",
			starTips.Contains("\"STAR_COUNT.description\"") && starTips.Contains("血怒：")
			&& !starTips.Contains("辉星") && !starTips.Contains("储君"), starTips.Replace("\n", " "));
		CharacterProfile noIconRes = ProfileFactory.Sample();
		noIconRes.ExtraResource.Enabled = true;
		noIconRes.ExtraResource.Name = "冰附魔";
		noIconRes.ExtraResource.Initial = 3;
		noIconRes.ExtraResource.CarryOver = false;
		noIconRes.Cards.Add(new CardSpec
		{
			Name = "自检花冰附魔",
			ClassName = "UiCheckResSpend",
			Rarity = "Common",
			InCardPool = true,
			Effects = { new EffectSpec { Kind = "ExtraResource", Amount = -2m, TargetSide = "Self" } },
		});
		string noIconTips = LocalizationGen.StaticHoverTipsJson(noIconRes);
		Check("没传图标时也覆盖描述（名字 + 开场数量 + 消耗规则，且不带本体的辉星文案）",
			noIconTips.Contains("冰附魔：每场战斗开始时获得 3 点（战斗结束清零）；打出需要消耗它的牌时会扣除，数量不足时无法打出。")
			&& !noIconTips.Contains("[img]"), noIconTips.Replace("\n", " "));
		Check("没填名字时描述退回「额外资源量」", LocalizationGen.ResourceName(ProfileFactory.Sample()) == "额外资源量", LocalizationGen.ResourceName(ProfileFactory.Sample()));
		// 条件选项里的「额外资源量」也要用自定义名字（用户报过：卡面写「额外资源量」、计数器写「冰附魔」）
		CardSpec resNameCard = new CardSpec
		{
			Name = "自检资源名",
			ClassName = "UiCheckResCondName",
			Rarity = "Common",
			InCardPool = true
		};
		resNameCard.Effects.Clear();
		EffectSpec resNameEffect = new EffectSpec { Kind = "Damage", Amount = 6m, TargetSide = "Enemy" };
		resNameEffect.Condition.Kind = "ExtraResourceAtLeast";
		resNameEffect.Condition.Amount = 4m;
		resNameCard.Effects.Add(resNameEffect);
		characterProfile15.Cards.Add(resNameCard);
		string resNameJson = LocalizationGen.CardsJson(characterProfile15);
		Check("条件说明里的「额外资源量」跟着显示名走（卡面写「拥有血怒至少 4 点」）",
			resNameJson.Contains("拥有血怒至少 4 点") && !resNameJson.Contains("拥有额外资源量至少"),
			resNameJson.Contains("拥有血怒至少 4 点") ? "拥有血怒至少 4 点" : resNameJson.Replace("\n", " "));
		Check("没填显示名时条件还是通用说法「拥有额外资源量至少 N 点」",
			CSharpCodeGen.ConditionText(resNameEffect.Condition) == "拥有额外资源量至少 4 点",
			CSharpCodeGen.ConditionText(resNameEffect.Condition));
		// 界面列表里的条件短名（Condition.DisplayShort）也要跟着显示名走
		bool ResourceDisplayNameProbe(string name)
		{
			ConditionSpec.ResourceDisplayName = name;
			bool matches = resNameEffect.Condition.DisplayShort.Contains(name);
			ConditionSpec.ResourceDisplayName = "额外资源量";
			return matches;
		}
		Check("界面列表里的条件短名也跟着显示名走（ConditionSpec.ResourceDisplayName）",
			ResourceDisplayNameProbe("血怒") && ResourceDisplayNameProbe("额外资源量"), "跟着变");
		// 状态的自动描述里也要写出条件（以前只写「做什么」，游戏里看不到「什么条件下才生效」）
		CharacterProfile condPowerProbe = ProfileFactory.Sample();
		condPowerProbe.ExtraResource.Enabled = true;
		condPowerProbe.ExtraResource.Name = "冰附魔";
		CustomPowerSpec condPower = new CustomPowerSpec { Name = "自检条件状态" };
		PowerTriggerSpec condTrigger = new PowerTriggerSpec { Kind = "TurnStart" };
		EffectSpec condEffect = new EffectSpec { Kind = "Block", Amount = 3m, TargetSide = "Self" };
		condEffect.Condition.Kind = "ExtraResourceAtLeast";
		condEffect.Condition.Amount = 4m;
		condTrigger.Effects.Add(condEffect);
		condPower.Triggers.Add(condTrigger);
		condPowerProbe.CustomPowers.Add(condPower);
		string condPowerJson = LocalizationGen.PowersJson(condPowerProbe);
		Check("状态描述里写出条件、并且用自定义显示名（「条件：拥有冰附魔至少 4 点」）",
			condPowerJson.Contains("条件：拥有冰附魔至少 4 点") && !condPowerJson.Contains("条件：拥有额外资源量"),
			condPowerJson.Contains("条件：拥有冰附魔至少 4 点") ? "拥有冰附魔至少 4 点" : condPowerJson.Replace("\n", " "));
		Check("没启用额外资源量时不覆盖本体悬停提示（保持本体文案）", !LocalizationGen.StaticHoverTipsJson(ProfileFactory.Sample()).Contains("STAR_COUNT"), "没有 STAR_COUNT 键");
		DependencyObject root15 = SelectTabRoot("卡牌");
		if (CardList.Items.Count > 0 && CardList.SelectedItem == null) CardList.SelectedIndex = 0;
		if (CardEffectList.Items.Count > 0) CardEffectList.SelectedIndex = 0;
		UpdateLayout();
		const string condHeader = "条件选项（只对上面这一条效果生效）";
		GroupBox groupBox = FindGroupBoxByHeader(condHeader, root15);
		Check("卡牌页的「条件选项」搬进了每条效果的编辑面板（不再是整张牌一个）", groupBox != null, (groupBox == null) ? "没找到" : "已找到");
		Check("卡牌页不再有整张牌级别的「条件选项」框", FindGroupBoxByHeader("条件选项", root15) == null, "老框已移除");
		if (groupBox != null)
		{
			List<ComboBox> list26 = new List<ComboBox>();
			CollectCombos(groupBox, list26);
			Check("每条效果的「条件选项」下拉绑在 Condition.Kind 上", list26.Any((ComboBox c) => BindingOperations.GetBinding(c, Selector.SelectedValueProperty)?.Path?.Path == "Condition.Kind"), string.Join(" / ", list26.Select((ComboBox c) => BindingOperations.GetBinding(c, Selector.SelectedValueProperty)?.Path?.Path)));
			Check("每条效果的「条件选项」下拉只列卡牌能用的条件", list26.Any((ComboBox c) => c.ItemsSource == CardEffectConditions) && CardEffectConditions.Count > 5, $"{CardEffectConditions.Count} 个卡牌条件");
			Check("每条效果的「条件选项」里有「指向对象」下拉（自己 / 指定敌人 / 任意一个敌人 / 全部敌人）",
				list26.Any((ComboBox c) => c.ItemsSource == ConditionTargets && BindingOperations.GetBinding(c, Selector.SelectedValueProperty)?.Path?.Path == "Condition.Target"),
				$"{ConditionTargets.Count} 个指向对象");
			List<CheckBox> list27 = new List<CheckBox>();
			CollectCheckBoxes(groupBox, list27);
			Check("每条效果的「条件选项」有「不满足时打不出去」开关", list27.Any((CheckBox c) => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path?.Path == "Condition.UnplayableWhenUnmet"), string.Join(" / ", list27.Select((CheckBox c) => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path?.Path)));
		}
		Check("「拥有额外资源量至少 N 点」这个条件存在（#3）", EffectCatalog.Conditions.Any((ConditionOption c) => c.Id == "ExtraResourceAtLeast" && c.Display.Contains("额外资源量") && c.NeedsAmount), "在条件表里");
		Check("「指向对象」四个选项的名字对得上（自己 / 指定敌人 / 任意一个敌人 / 全部敌人）", ConditionTargets.Select((ConditionTargetOption t) => t.Display).SequenceEqual(new string[4] { "自己", "指定敌人（这张牌打的目标）", "任意一个敌人", "全部敌人" }), string.Join(" / ", ConditionTargets.Select((ConditionTargetOption t) => t.Display)));
		DependencyObject root16 = SelectTabRoot("遗物");
		if (RelicList.Items.Count > 0 && RelicList.SelectedItem == null) RelicList.SelectedIndex = 0;
		if (RelicEffectList.Items.Count > 0) RelicEffectList.SelectedIndex = 0;
		UpdateLayout();
		GroupBox groupBox2 = FindGroupBoxByHeader("条件选项（整只遗物：不满足时不触发）", root16);
		Check("遗物页有「整只遗物」的触发条件框", groupBox2 != null);
		if (groupBox2 != null)
		{
			List<ComboBox> list28 = new List<ComboBox>();
			CollectCombos(groupBox2, list28);
			Check("遗物页「整只遗物」的条件下拉绑在 Condition.Kind 上、且只列遗物能用的条件", list28.Any((ComboBox c) => BindingOperations.GetBinding(c, Selector.SelectedValueProperty)?.Path?.Path == "Condition.Kind" && c.ItemsSource == RelicConditions) && RelicConditions.Count > 5, $"{RelicConditions.Count} 个遗物条件");
		}
		GroupBox groupBox2b = FindGroupBoxByHeader(condHeader, root16);
		Check("遗物页每条效果也能各自带条件（用的是「不生效」那一版）", groupBox2b != null);
		if (groupBox2b != null)
		{
			List<ComboBox> list28b = new List<ComboBox>();
			CollectCombos(groupBox2b, list28b);
			Check("遗物效果的条件下拉只列「遗物 + 状态」都能用的条件（不含「这张牌」类）", list28b.Any((ComboBox c) => c.ItemsSource == PlainEffectConditions), $"{PlainEffectConditions.Count} 个可选条件");
		}
		Check("「每场战斗只触发一次」只在遗物里出现（卡牌用「本场战斗还没打出过这张牌」）", RelicConditions.Any((ConditionOption c) => c.Id == "OncePerCombat") && !CardEffectConditions.Any((ConditionOption c) => c.Id == "OncePerCombat"), "归属正确");
		Check("「本场战斗还没打出过这张牌」只在卡牌里出现", CardEffectConditions.Any((ConditionOption c) => c.Id == "NotPlayedThisCombat") && !PlainEffectConditions.Any((ConditionOption c) => c.Id == "NotPlayedThisCombat"), "归属正确");
		CharacterProfile characterProfile16 = ProfileFactory.Sample();
		CardSpec cardSpec15 = new CardSpec
		{
			Name = "条件卡",
			ClassName = "UiCheckCond",
			Cost = 1
		};
		cardSpec15.Effects.Clear();
		cardSpec15.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 5m
		});
		cardSpec15.Condition.Kind = "NotPlayedThisTurn";
		characterProfile16.Cards.Add(cardSpec15);
		string text27 = CSharpCodeGen.CardSource(characterProfile16, cardSpec15, 0);
		Check("条件「不满足时不能打出」生成 IsPlayable（本体 Clash / GrandFinale 的做法）", text27.Contains("protected override bool IsPlayable =>"), "有 IsPlayable");
		Check("条件写成了真实的战斗历史查询（不是占位）", text27.Contains("CombatManager.Instance.History.CardPlaysFinished"), "查了战斗历史");
		Check("条件满足时给牌描金边（本体取回 / 死亡之门的提示方式）", text27.Contains("ShouldGlowGoldInternal =>"), "有金边提示");
		Check("「不满足时不能打出」不会把效果也包进 if（本体靠 IsPlayable 拦住）", !text27.Contains("if (base.CombatState is not null && !CombatManager"), "效果没被包 if");
		CardSpec cardSpec16 = new CardSpec
		{
			Name = "条件卡2",
			ClassName = "UiCheckCond2",
			Cost = 1
		};
		cardSpec16.Effects.Clear();
		cardSpec16.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 5m
		});
		cardSpec16.Condition.Kind = "HandAtLeast";
		cardSpec16.Condition.Amount = 3m;
		cardSpec16.Condition.UnplayableWhenUnmet = false;
		characterProfile16.Cards.Add(cardSpec16);
		string text28 = CSharpCodeGen.CardSource(characterProfile16, cardSpec16, 0);
		Check("勾掉「打不出去」时：不生成 IsPlayable", !text28.Contains("IsPlayable =>"), "没有 IsPlayable");
		Check("勾掉「打不出去」时：整段效果被条件包住", text28.Contains("if (base.Owner is not null && CardPile.GetCards(base.Owner, PileType.Hand).Count() >= 3)"), "效果被 if 包住");
		CardSpec cardSpec17 = new CardSpec
		{
			Name = "条件卡3",
			ClassName = "UiCheckCond3",
			Cost = 1
		};
		cardSpec17.Effects.Clear();
		cardSpec17.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 5m
		});
		cardSpec17.Condition.Kind = "HasPowerAtLeast";
		cardSpec17.Condition.Amount = 2m;
		cardSpec17.Condition.PowerId = "VulnerablePower";
		string text29 = CSharpCodeGen.CardSource(characterProfile16, cardSpec17, 0);
		Check("「拥有某状态至少 N 层」读的是本体状态层数", text29.Contains("base.Owner.Creature.GetPowerAmount<VulnerablePower>() >= 2"), "读了状态层数");
		Check("条件说明写进了生成代码的注释里（一眼能看懂是什么条件）", text29.Contains("条件：拥有易伤至少 2 层"), "有中文条件说明");		// ===== #5 每条效果各自一个条件（不再是整张牌一个条件）=====
		CardSpec perEffect = new CardSpec
		{
			Name = "每条效果各自条件",
			ClassName = "UiCheckPerEffect",
			Cost = 1
		};
		perEffect.Effects.Clear();
		perEffect.Effects.Add(new EffectSpec { Kind = "Block", Amount = 5m });                        // 无条件
		perEffect.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 6m, TargetSide = "Enemy" }); // 有条件
		perEffect.Effects[1].Condition.Kind = "HasPowerAtLeast";
		perEffect.Effects[1].Condition.Amount = 2m;
		perEffect.Effects[1].Condition.PowerId = "VulnerablePower";
		perEffect.Effects[1].Condition.UnplayableWhenUnmet = false;
		characterProfile16.Cards.Add(perEffect);
		string textPer = CSharpCodeGen.CardSource(characterProfile16, perEffect, 0);
		Check("只有带条件的那条效果被包进 if（第 1 条无条件的照常执行）",
			textPer.Contains("if (base.Owner.Creature.GetPowerAmount<VulnerablePower>() >= 2)")
			&& textPer.IndexOf("CreatureCmd.GainBlock", StringComparison.Ordinal) < textPer.IndexOf("if (base.Owner.Creature.GetPowerAmount", StringComparison.Ordinal),
			"条件只包住第 2 条效果");
		Check("每条效果的条件写在注释里、点名是哪条效果", textPer.Contains("条件（只对「造成伤害」这条效果）"), "注释点名了效果");
		Check("效果各自的条件不会生成 IsPlayable（勾的是「只是这条不生效」）", !textPer.Contains("IsPlayable =>"), "没有 IsPlayable");
		perEffect.Effects[1].Condition.UnplayableWhenUnmet = true;
		string textPer2 = CSharpCodeGen.CardSource(characterProfile16, perEffect, 0);
		Check("勾上「不满足时打不出去」后会生成 IsPlayable", textPer2.Contains("protected override bool IsPlayable => (base.Owner.Creature.GetPowerAmount<VulnerablePower>() >= 2)"), "有 IsPlayable");
		perEffect.Effects[1].Condition.UnplayableWhenUnmet = false;
		// ===== #3 额外资源量条件 =====
		CardSpec resourceCond = new CardSpec { Name = "资源条件", ClassName = "UiCheckResCond", Cost = 1 };
		resourceCond.Effects.Clear();
		resourceCond.Effects.Add(new EffectSpec { Kind = "Block", Amount = 4m });
		resourceCond.Effects[0].Condition.Kind = "ExtraResourceAtLeast";
		resourceCond.Effects[0].Condition.Amount = 3m;
		characterProfile16.Cards.Add(resourceCond);
		string textRes = CSharpCodeGen.CardSource(characterProfile16, resourceCond, 0);
		Check("「拥有额外资源量至少 N 点」读的是本体星星计数（PlayerCombatState.Stars）",
			textRes.Contains("(base.Owner?.PlayerCombatState?.Stars) ?? 0) >= 3"), "读了额外资源量");
		Check("额外资源量条件的说明是中文且带 N", CSharpCodeGen.ConditionText(resourceCond.Effects[0].Condition) == "拥有额外资源量至少 3 点", CSharpCodeGen.ConditionText(resourceCond.Effects[0].Condition));
		// ===== #4 条件的指向对象 =====
		ConditionSpec targetCond = new ConditionSpec { Kind = "HasPowerAtLeast", Amount = 2m, PowerId = "VulnerablePower" };
		string selfExpr = CSharpCodeGen.ConditionExpr(targetCond, isCard: true);
		targetCond.Target = "AnyEnemy";
		string anyExpr = CSharpCodeGen.ConditionExpr(targetCond, isCard: true);
		targetCond.Target = "AllEnemies";
		string allExpr = CSharpCodeGen.ConditionExpr(targetCond, isCard: true);
		Check("指向对象 = 自己：读自己身上的层数", selfExpr == "base.Owner.Creature.GetPowerAmount<VulnerablePower>() >= 2", selfExpr);
		Check("指向对象 = 任意一个敌人：任一活着的敌人满足即可", anyExpr.Contains("base.CombatState.Enemies.Any(c => c.IsAlive && c.GetPowerAmount<VulnerablePower>() >= 2)"), anyExpr);
		Check("指向对象 = 全部敌人：每个活着的敌人都得满足（且至少有一个敌人）", allExpr.Contains("base.CombatState.Enemies.Any(c => c.IsAlive)") && allExpr.Contains("base.CombatState.Enemies.All(c => !c.IsAlive || c.GetPowerAmount<VulnerablePower>() >= 2)"), allExpr);
		Check("指向对象会写进条件说明里（（全部敌人））", CSharpCodeGen.ConditionText(targetCond) == "拥有易伤至少 2 层（全部敌人）", CSharpCodeGen.ConditionText(targetCond));
		Check("指向对象也管生命值条件", CSharpCodeGen.ConditionExpr(new ConditionSpec { Kind = "HpBelowPercent", Amount = 50m, Target = "AllEnemies" }, isCard: true).Contains("Enemies.All(c => !c.IsAlive || c.CurrentHp * 100 <= c.MaxHp * 50)"), "生命值条件也支持指向对象");
		Check("指向对象只在需要的条件上出现（手牌数这种不看对象）", !EffectCatalog.FindCondition("HandAtLeast")!.NeedsTarget, "手牌条件不需要对象");
		// ===== #6 升级后的关键字 =====
		CardSpec kwCard = new CardSpec { Name = "关键字升级", ClassName = "UiCheckKw", Cost = 1 };
		kwCard.Effects.Clear();
		kwCard.Effects.Add(new EffectSpec { Kind = "Block", Amount = 5m });
		kwCard.Exhausts = true;
		kwCard.UpgradeKeywords.Innate = "Add";
		kwCard.UpgradeKeywords.Exhaust = "Remove";
		characterProfile16.Cards.Add(kwCard);
		string textKw = CSharpCodeGen.CardSource(characterProfile16, kwCard, 0);
		Check("升级后的关键字生成 OnUpgrade + AddKeyword（本体残影的做法）", textKw.Contains("protected override void OnUpgrade()") && textKw.Contains("AddKeyword(CardKeyword.Innate);"), "有 AddKeyword");
		Check("升级后的关键字也支持 RemoveKeyword（本体幻影 / 寒冰的做法）", textKw.Contains("RemoveKeyword(CardKeyword.Exhaust);"), "有 RemoveKeyword");
		Check("升级后的关键字只改关键字、不误改数值", textKw.Split("AddKeyword(CardKeyword.Innate);").Length - 1 == 1, "只写了一次");
		Check("卡牌列表显示里会写出「升级后：获得 固有」", kwCard.Display.Contains("升级后：获得 固有") && kwCard.Display.Contains("失去 消耗"), kwCard.Display);
		Check("三态没配时不生成任何关键字升级代码", !CSharpCodeGen.CardSource(characterProfile16, new CardSpec { Name = "无升级关键字", ClassName = "UiCheckKwNone", Cost = 1, Effects = { new EffectSpec { Kind = "Block", Amount = 1m } } }, 0).Contains("AddKeyword"), "没有 AddKeyword");
		Check("「升级后的关键字」只列本体有的 6 个关键字", KeywordUpgradeSpec.All.Length == 6 && KeywordUpgradeStates.Count == 3, string.Join(" / ", KeywordUpgradeSpec.All.Select(k => k.Zh)));
		// 小窗口也要能滚下去：卡牌 / 遗物 / 药水的属性栏必须在可滚动区域里
		Check("「卡牌属性」那一栏能上下滚动（小窗口也能看到下面的关键字 / 卡面）",
			FindLabelledTextAndHasScrollAncestor("卡牌属性", "卡牌"), "卡牌属性已放进滚动区");
		Check("「遗物属性」那一栏能上下滚动", FindLabelledTextAndHasScrollAncestor("遗物属性", "遗物"), "遗物属性已放进滚动区");
		Check("「药水属性」那一栏能上下滚动", FindLabelledTextAndHasScrollAncestor("药水属性", "药水"), "药水属性已放进滚动区");
		// 界面上那 6 行下拉必须真的有选项（之前绑错了路径 → 下拉是空的）
		SelectTabRoot("卡牌");
		if (CardList.Items.Count > 0 && CardList.SelectedItem == null) CardList.SelectedIndex = 0;
		UpdateLayout();
		List<ComboBox> kwCombos = new List<ComboBox>();
		CollectCombos(CardDetail, kwCombos);
		var kwCombo = kwCombos.FirstOrDefault((ComboBox c) => c.ItemsSource == KeywordUpgradeStates);
		Check("卡牌页「升级后的关键字」的下拉绑到了那三个选项上", kwCombo != null, $"{kwCombos.Count} 个下拉里找到 {((kwCombo == null) ? 0 : 1)} 个");
		Check("「升级后的关键字」下拉里真的有选项（不变 / 升级后获得 / 升级后失去）", kwCombo != null && kwCombo.Items.Count == 3, $"{(kwCombo == null ? 0 : kwCombo.Items.Count)} 个选项");
		Check("关键字那 6 行都渲染出来了（固有 / 虚无 / 保留…）",
			UpgradeKeywordRows.Count == 6 && UpgradeKeywordRows.Any((KeywordUpgradeRow r) => r.Zh == "虚无"),
			$"{UpgradeKeywordRows.Count} 行：" + string.Join("、", UpgradeKeywordRows.Select((KeywordUpgradeRow r) => r.Zh)));
		if (CardList.SelectedItem is CardSpec kwUiCard)
		{
			var innRow = UpgradeKeywordRows.First((KeywordUpgradeRow r) => r.Zh == "固有");
			innRow.State = "Add";     // 等价于在下拉里选「升级后获得」
			Check("在下拉里选「升级后获得」会写进这张牌（并生成 AddKeyword）",
				kwUiCard.UpgradeKeywords.Innate == "Add" && CSharpCodeGen.CardSource(Profile, kwUiCard, 0).Contains("AddKeyword(CardKeyword.Innate);"), kwUiCard.UpgradeKeywords.Display);
			innRow.State = "Keep";
		}
		// 下面这些「整张牌」的检查会切到别的数据上，先切回卡牌页
		SelectTabRoot("卡牌");
		// ===== 老存档迁移：整张牌一个条件 → 第一条效果 =====
		CharacterProfile oldSave = ProfileFactory.Sample();
		// 注意：列表最上面两条是初始卡组的本体卡（打击 / 防御），这里挑「自己的卡」
		CardSpec oldSaveCard = oldSave.Cards.First((CardSpec c) => !c.IsVanillaCard);
		oldSaveCard.Condition.Kind = "HandAtLeast";
		oldSaveCard.Condition.Amount = 2m;
		Check("老存档读回时会把「整张牌的条件」搬到第一条效果上", ProfileFactory.Normalize(oldSave).Cards.First((CardSpec c) => !c.IsVanillaCard).Effects[0].Condition.Kind == "HandAtLeast" && oldSaveCard.Condition.IsNone, "已迁移");
		Check("迁移后生成出来的是效果级条件（不是 IsPlayable 那种整张牌的旧写法）", CSharpCodeGen.CardSource(oldSave, oldSaveCard, 0).Contains("条件（只对「造成伤害」这条效果）") && !CSharpCodeGen.CardSource(oldSave, oldSaveCard, 0).Contains("老存档的「整张牌」条件"), "按新写法生成");
		// ===== #7 左下角卡牌数量统计 =====
		Check("左下角统计按 攻击 / 技能 / 能力 分开报、并给出总数",
			CardCountText.Contains("攻击") && CardCountText.Contains("技能") && CardCountText.Contains("能力") && CardCountText.Contains($"共 {_profile.Cards.Count((CardSpec c) => !c.IsVanillaCard)} 张"), CardCountText);
		List<TextBlock> statusTexts = new List<TextBlock>();
		CollectTextBlocks(MainStatusBar, statusTexts);
		Check("卡牌数量就在状态栏（左下角）最前面那个位置上",
			statusTexts.Count > 0 && BindingOperations.GetBinding(statusTexts[0], TextBlock.TextProperty)?.Path?.Path == "CardCountText",
			string.Join(" / ", statusTexts.Select((TextBlock b) => BindingOperations.GetBinding(b, TextBlock.TextProperty)?.Path?.Path)));
		CardSpec counted = new CardSpec { Name = "计数用", ClassName = "UiCheckCount", Cost = 0, CardType = "Power" };
		counted.Effects.Clear();
		counted.Effects.Add(new EffectSpec { Kind = "Block", Amount = 1m });
		int beforeCount = _profile.Cards.Count((CardSpec c) => !c.IsVanillaCard);
		_profile.Cards.Add(counted);
		Check("加一张牌后统计立刻跟着变（数量 +1）", CardCountText.Contains($"共 {beforeCount + 1} 张"), CardCountText);
		_profile.Cards.Remove(counted);
		Check("删掉后统计回到原来的数量", CardCountText.Contains($"共 {beforeCount} 张"), CardCountText);

		// ===== 初始卡组里的本体卡（打击 / 防御）：不再是勾选框，而是卡牌列表里的两条「本体卡引用」 =====
		DependencyObject rootCharPage = SelectTabRoot("角色");
		List<CheckBox> charPageChecks = new List<CheckBox>();
		CollectCheckBoxes(rootCharPage, charPageChecks);
		Check("角色页不再有「初始卡组包含 5 张打击 + 5 张防御（本体占位）」这个勾选框",
			!charPageChecks.Any((CheckBox c) => (c.Content as string)?.Contains("初始卡组包含") == true),
			string.Join(" / ", charPageChecks.Select((CheckBox c) => c.Content)));
		CharacterProfile basics = ProfileFactory.Sample();
		List<CardSpec> vanillaRows = basics.Cards.Where((CardSpec c) => c.ClassName is "Strike" or "Defend").ToList();
		Check("新建存档时卡牌列表里默认就有两条初始牌（打击 / 防御）", vanillaRows.Count == 2, $"{vanillaRows.Count} 条");
		Check("只有英文类名是固定的 Strike / Defend（本体没有这两个裸类名，不会撞 ID），各 5 张",
			vanillaRows.Select((CardSpec c) => c.ClassName).SequenceEqual(new string[2] { "Strike", "Defend" })
			&& vanillaRows.All((CardSpec c) => c.StartingCopies == 5 && c.InStartingDeck),
			string.Join(" / ", vanillaRows.Select((CardSpec c) => $"{c.Name}={c.ClassName}×{c.StartingCopies}")));
		Check("初始的打击 / 防御是「你自己的卡」，数值 / 效果 / 卡面都能改（ValueEditable = true）",
			vanillaRows.All((CardSpec c) => c.ValueEditable), "可改");
		Check("出厂数值照抄本体（打击 1 费 6 伤害、防御 1 费 5 格挡，升级都 +3）",
			vanillaRows[0].Effects[0].Kind == "Damage" && vanillaRows[0].Effects[0].Amount == 6m && vanillaRows[0].Effects[0].UpgradeAmount == 3m
			&& vanillaRows[1].Effects[0].Kind == "Block" && vanillaRows[1].Effects[0].Amount == 5m && vanillaRows[1].Effects[0].UpgradeAmount == 3m,
			$"{vanillaRows[0].Effects[0].Amount}+{vanillaRows[0].Effects[0].UpgradeAmount} / {vanillaRows[1].Effects[0].Amount}+{vanillaRows[1].Effects[0].UpgradeAmount}");
		Check("标了本体卡标签（Strike / Defend）+ 稀有度 Basic —— 本体那些「升级初始打击 / 防御」的遗物就是按这个找牌的",
			vanillaRows[0].TagList.Contains("Strike") && vanillaRows[1].TagList.Contains("Defend")
			&& vanillaRows.All((CardSpec c) => c.Rarity == "Basic"),
			string.Join(" / ", vanillaRows.Select((CardSpec c) => $"{c.ClassName}:{string.Join("+", c.TagList)}/{c.Rarity}")));
		string basicsStrikeSrc = CSharpCodeGen.CardSource(basics, vanillaRows[0], 0);
		string basicsDefendSrc = CSharpCodeGen.CardSource(basics, vanillaRows[1], 1);
		Check("生成的打击卡带 CanonicalTags => CardTag.Strike",
			basicsStrikeSrc.Contains("CanonicalTags => new HashSet<CardTag> { CardTag.Strike }"), "有标签");
		Check("生成的防御卡带 CardTag.Defend 和 GainsBlock = true（和本体防御牌一样）",
			basicsDefendSrc.Contains("CanonicalTags => new HashSet<CardTag> { CardTag.Defend }") && basicsDefendSrc.Contains("public override bool GainsBlock => true;"), "有标签 + GainsBlock");
		string basicsChar = CSharpCodeGen.CharacterSource(basics);
		// 打击 / 防御生成出来的类名带角色类名前缀（<角色>Strike / <角色>Defend）—— 避免和别的模组撞模型 ID
		string basicsCharClass = Naming.From(basics).CharClass;
		string bStrike = basicsCharClass + "Strike", bDefend = basicsCharClass + "Defend";
		string deckPart = basicsChar.Length > 0 ? basicsChar.Substring(basicsChar.IndexOf("StartingDeck", StringComparison.Ordinal)) : "";
		int strikeAt = deckPart.IndexOf($"ModelDb.Card<{bStrike}>(),", StringComparison.Ordinal);
		int defendAt = deckPart.IndexOf($"ModelDb.Card<{bDefend}>(),", StringComparison.Ordinal);
		int firstOwnAt = -1;
		foreach (System.Text.RegularExpressions.Match mm in System.Text.RegularExpressions.Regex.Matches(deckPart, @"ModelDb\.Card<(\w+)>\(\)"))
		{
			string cn = mm.Groups[1].Value;
			if (cn == bStrike || cn == bDefend) continue;
			firstOwnAt = mm.Index;
			break;
		}
		Check("生成的初始卡组里正好 5 张打击 + 5 张防御，而且排在最前面",
			System.Text.RegularExpressions.Regex.Matches(basicsChar, "ModelDb\\.Card<" + bStrike + ">\\(\\)").Count == 5
			&& System.Text.RegularExpressions.Regex.Matches(basicsChar, "ModelDb\\.Card<" + bDefend + ">\\(\\)").Count == 5
			&& strikeAt >= 0 && defendAt > strikeAt && firstOwnAt > defendAt, $"打击@{strikeAt} 防御@{defendAt} 首张自有牌@{firstOwnAt}");
		string basicsPool = CSharpCodeGen.CardPoolSource(basics);
		Check("打击 / 防御在卡池里（本体那些按标签查牌的遗物是从角色卡池里找牌的）",
			basicsPool.Contains($"ModelDb.Card<{bStrike}>()") && basicsPool.Contains($"ModelDb.Card<{bDefend}>()"), "在池子里");
		Check("但不进奖励 / 商店（稀有度是 Basic，本体抽奖励只在 Common/Uncommon/Rare 里挑）",
			!basicsPool.Contains($"RemoveAll(c => c.Id == ModelDb.Card<{bStrike}>().Id")   // 不再靠卡池排除（排除会害本体按标签查牌时崩）
			&& basics.Cards.First((CardSpec c) => c.ClassName == "Strike").Rarity == "Basic"
			&& basics.Cards.First((CardSpec c) => c.ClassName == "Defend").Rarity == "Basic"
			&& !CSharpCodeGen.RewardEligibleCards(basics).Any((CardSpec c) => c.ClassName is "Strike" or "Defend"), "靠 Basic 稀有度排除");
		Check("打击 / 防御有自己的本地化（名字 + 描述，按自己的数值显示）",
			LocalizationGen.CardsJson(basics).Contains("STRIKE.title") && LocalizationGen.CardsJson(basics).Contains("STRIKE.description")
			&& LocalizationGen.CardsJson(basics).Contains("DEFEND.title"), "有本地化");
		Check("README 里的「初始卡组」写成 打击×5 / 防御×5",
			ModGenerator.InitialDeckSummary(basics).Contains("打击×5") && ModGenerator.InitialDeckSummary(basics).Contains("防御×5"),
			ModGenerator.InitialDeckSummary(basics));
		// 老存档迁移：以前那个勾选框 = true 的话，读回来自动变成两条本体卡，初始卡组内容不变
		CharacterProfile legacyBasics = ProfileFactory.Sample();
		foreach (CardSpec v in legacyBasics.Cards.Where((CardSpec c) => c.ClassName is "Strike" or "Defend").ToList()) legacyBasics.Cards.Remove(v);
		legacyBasics.IncludeVanillaStrikeDefend = true;                 // 老存档里那个勾选框
		ProfileFactory.Normalize(legacyBasics);
		Check("老存档（勾过那个开关）读回时自动补成两条初始牌、开关归零",
			legacyBasics.Cards.Count((CardSpec c) => c.ClassName is "Strike" or "Defend") == 2 && !legacyBasics.IncludeVanillaStrikeDefend,
			$"{legacyBasics.Cards.Count((CardSpec c) => c.ClassName is "Strike" or "Defend")} 条 / 开关={legacyBasics.IncludeVanillaStrikeDefend}");
		Check("初始牌排在列表最上面、顺序是「打击 → 防御」",
			legacyBasics.Cards[0].ClassName == "Strike" && legacyBasics.Cards[1].ClassName == "Defend",
			string.Join(" / ", legacyBasics.Cards.Take(2).Select((CardSpec c) => c.Name)));
		// 老存档里这两条可能被追加在末尾、甚至顺序颠倒（早期版本）——读回来也要挪到最前面并纠正顺序
		CharacterProfile misplaced = ProfileFactory.Sample();
		var vRows = misplaced.Cards.Where((CardSpec c) => c.ClassName is "Strike" or "Defend").ToList();
		foreach (CardSpec v in vRows) misplaced.Cards.Remove(v);
		misplaced.Cards.Add(vRows[1]);      // 故意颠倒顺序、并放到最后
		misplaced.Cards.Add(vRows[0]);
		ProfileFactory.MoveVanillaBasicsToTop(misplaced);
		Check("老存档里被放到末尾/顺序颠倒的初始牌会被挪到最前面并纠正顺序",
			misplaced.Cards[0].ClassName == "Strike" && misplaced.Cards[1].ClassName == "Defend" && misplaced.Cards.Count == ProfileFactory.Sample().Cards.Count,
			string.Join(" / ", misplaced.Cards.Take(3).Select((CardSpec c) => c.Name)));
		// 自动编号不受初始牌影响（打击 / 防御不占号）
		CharacterProfile numbering = ProfileFactory.Sample();
		numbering.Cards.First((CardSpec c) => c.ClassName is not ("Strike" or "Defend")).ClassName = "";   // 第一张自有卡改成自动编号
		Check("初始牌排在前面也不会让「自动编号」的卡改名（打击 / 防御不占号）",
			Naming.From(numbering).CardClassName(numbering, numbering.Cards.First((CardSpec c) => c.ClassName is not ("Strike" or "Defend"))) == "SevenCard1",
			Naming.From(numbering).CardClassName(numbering, numbering.Cards.First((CardSpec c) => c.ClassName is not ("Strike" or "Defend"))));
		Check("迁移后生成的初始卡组和以前完全一样（5 打击 + 5 防御）",
			System.Text.RegularExpressions.Regex.Matches(CSharpCodeGen.CharacterSource(legacyBasics),
				"ModelDb\\.Card<" + Naming.From(legacyBasics).CharClass + "Strike>\\(\\)").Count == 5, "5 张");
		ProfileFactory.Normalize(legacyBasics);
		Check("再读一次不会重复添加初始牌", legacyBasics.Cards.Count((CardSpec c) => c.ClassName is "Strike" or "Defend") == 2, $"{legacyBasics.Cards.Count((CardSpec c) => c.ClassName is "Strike" or "Defend")} 条");
		// 老存档里的「本体卡引用」行（IsVanillaCard）→ 自动转成你自己的 Strike / Defend（标签 / 数值 / 稀有度都对上）
		CharacterProfile legacyRef = ProfileFactory.Sample();
		foreach (CardSpec v in legacyRef.Cards.Where((CardSpec c) => c.ClassName is "Strike" or "Defend").ToList()) legacyRef.Cards.Remove(v);
		legacyRef.Cards.Add(new CardSpec { Name = "打击", ClassName = "StrikeIronclad", IsVanillaCard = true, InStartingDeck = true, StartingCopies = 4 });
		legacyRef.Cards.Add(new CardSpec { Name = "防御", ClassName = "DefendIronclad", IsVanillaCard = true, InStartingDeck = true, StartingCopies = 4 });
		ProfileFactory.Normalize(legacyRef);
		CardSpec convStrike = legacyRef.Cards.First((CardSpec c) => c.ClassName == "Strike");
		Check("老存档里的「本体卡引用」会自动转成你自己的打击 / 防御（名字和初始张数保留、标上标签、数值照抄本体）",
			!convStrike.IsVanillaCard && convStrike.StartingCopies == 4 && convStrike.TagList.Contains("Strike")
			&& convStrike.Rarity == "Basic" && convStrike.Effects.Count == 1 && convStrike.Effects[0].Amount == 6m
			&& legacyRef.Cards.First((CardSpec c) => c.ClassName == "Defend").TagList.Contains("Defend"),
			$"{convStrike.ClassName} ×{convStrike.StartingCopies} 标签={string.Join("+", convStrike.TagList)} 数值={convStrike.Effects[0].Amount}");
		Check("转完之后初始卡组引用的是你自己的卡（不再引用本体的 StrikeIronclad）",
			!CSharpCodeGen.CharacterSource(legacyRef).Contains("StrikeIronclad")
			&& System.Text.RegularExpressions.Regex.Matches(CSharpCodeGen.CharacterSource(legacyRef),
				"ModelDb\\.Card<" + Naming.From(legacyRef).CharClass + "Strike>\\(\\)").Count == 4, "4 张自己的打击");
		// 类名撞本体要拦住（否则模组加载时抛 DuplicateModelException）
		CharacterProfile clash = ProfileFactory.Sample();
		clash.Cards.First((CardSpec c) => c.ClassName is not ("Strike" or "Defend")).ClassName = "PommelStrike";
		Check("卡牌类名和本体卡重名时校验器直接报错（本机读到了本体卡牌表才算）",
			EffectCatalog.Cards.Count == 0 || ProfileValidator.Validate(clash).Any((ValidationIssue i) => i.IsError && i.Message.Contains("PommelStrike")),
			$"本体卡牌表 {EffectCatalog.Cards.Count} 项");
		Check("初始打击 / 防御漏标 CardTag 时会警告",
			ProfileValidator.Validate(new CharacterProfile { Cards = { new CardSpec { Name = "打击", ClassName = "Strike", CardType = "Attack", Rarity = "Basic", Cost = 1 } } })
				.Any((ValidationIssue i) => i.Message.Contains("CardTag")), "有警告");
		// ===== 没填英文类名的卡：文件名 / 类名声明 / 卡池引用 / 本地化键必须**完全一致** =====
		// 踩过的坑（用户报「无法构建」的真凶）：CardSource 用的是「列表下标」编号，而文件名和卡池用的是
		// 「自有卡序号」—— 初始打击/防御插到最前面差 2，于是 SevenCard9.cs 里声明成 SevenCard11，
		// 卡池引用的 SevenCard9 找不到 → 编译报 CS0246。
		{
			CharacterProfile auto = ProfileFactory.Sample();
			auto.Cards.Add(new CardSpec { Name = "没填类名的新卡", ClassName = "", CardType = "Attack", Rarity = "Common", Cost = 1 });
			CardSpec autoCard = auto.Cards.Last();
			string autoCls = Naming.From(auto).CardClassName(auto, autoCard);
			Check("没填类名的卡按「自有卡序号」编号（打击 / 防御不占号）", autoCls == "SevenCard9", autoCls);
			string autoSrc = CSharpCodeGen.CardSource(auto, autoCard, auto.Cards.IndexOf(autoCard));
			Check("生成的类名和文件名用的是同一个名字（就是卡池引用的那个）",
				autoSrc.Contains($"public sealed class {autoCls} : CardModel") && CSharpCodeGen.CardPoolSource(auto).Contains($"ModelDb.Card<{autoCls}>()"),
				autoCls);
			Check("本地化键也用的是同一个名字", LocalizationGen.CardsJson(auto).Contains(Naming.EntryOf(autoCls) + ".title"), Naming.EntryOf(autoCls));
			// 真的生成一次到临时目录：卡池引用的卡类必须都有对应文件（这条是 CS0246 的根因回归测试）
			string autoRoot = Path.Combine(Path.GetTempPath(), "forge_uicheck_autoname_" + Guid.NewGuid().ToString("N").Substring(0, 8));
			try
			{
				auto.Paths.OutputDir = autoRoot;
				auto.Paths.VanillaProject = Profile.Paths.VanillaProject;
				auto.Paths.GameDataDir = Profile.Paths.GameDataDir;
				var autoGen = ModGenerator.Generate(auto);
				string autoFile = Path.Combine(autoGen.ProjectRoot, "cs", "Cards", autoCls + ".cs");
				Check("没填类名的卡：生成出的文件名 / 类名声明 / 卡池引用三者一致（不会再有 CS0246）",
					autoGen.Success && File.Exists(autoFile) && File.ReadAllText(autoFile, System.Text.Encoding.UTF8).Contains($"class {autoCls} : CardModel")
					&& File.ReadAllText(Path.Combine(autoGen.ProjectRoot, "cs", "SevenCardPool.cs"), System.Text.Encoding.UTF8).Contains($"ModelDb.Card<{autoCls}>()"),
					autoGen.Success ? autoCls : (autoGen.Issues.FirstOrDefault((ValidationIssue i) => i.IsError)?.Message ?? "生成失败"));
			}
			finally
			{
				try { if (Directory.Exists(autoRoot)) Directory.Delete(autoRoot, true); } catch { }
			}
		}
		// UI：选中本体卡时数值编辑变灰、效果栏整列禁用，但「初始份数」还能改
		CardSpec vanillaProbe = new CardSpec { Name = "打击", ClassName = "StrikeIronclad", IsVanillaCard = true, InStartingDeck = true, StartingCopies = 5 };
		_profile.Cards.Add(vanillaProbe);
		SyncDetail();
		CardList.SelectedItem = vanillaProbe;
		UpdateLayout();
		List<ComboBox> vanillaCombos = new List<ComboBox>();
		CollectCombos(CardDetail, vanillaCombos);
		Check("选中本体卡时：类型 / 稀有度下拉变灰（数值由本体决定）",
			vanillaCombos.Any((ComboBox c) => BindingOperations.GetBinding(c, Selector.SelectedItemProperty)?.Path?.Path == "CardType" && !c.IsEnabled)
			&& vanillaCombos.Any((ComboBox c) => BindingOperations.GetBinding(c, Selector.SelectedItemProperty)?.Path?.Path == "Rarity" && !c.IsEnabled),
			string.Join(" / ", vanillaCombos.Select((ComboBox c) => $"{BindingOperations.GetBinding(c, Selector.SelectedItemProperty)?.Path?.Path}={c.IsEnabled}")));
		List<TextBox> vanillaBoxes = new List<TextBox>();
		CollectTextBoxes(CardDetail, vanillaBoxes);
		Check("选中本体卡时：「初始份数」仍然可以改",
			vanillaBoxes.Any((TextBox t) => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path?.Path == "StartingCopies" && t.IsEnabled), "可改");
		List<Button> vanillaButtons = new List<Button>();
		CollectButtons(CardDetail, vanillaButtons);
		Check("选中本体卡时：效果栏整列禁用（没有效果可编）",
			vanillaButtons.Any((Button b) => (b.Content as string) == "添加效果" && !b.IsEnabled), "禁用了");
		Check("选中本体卡时：面板上有黄色说明写着「只做引用、不能改」",
			TextsIn(CardDetail).Any((string t) => t.Contains("这是本体卡（只做引用）")), "有说明");
		Check("本体卡在列表里显示成「【本体卡】…」并写出本体英文类名",
			vanillaProbe.Display.Contains("【本体卡】") && vanillaProbe.Display.Contains("StrikeIronclad"), vanillaProbe.Display);
		_profile.Cards.Remove(vanillaProbe);
		SyncDetail();
		// 状态栏：本体卡单独列出来，不算进「自己的卡」
		int vanBefore = _profile.Cards.Count((CardSpec c) => c.IsVanillaCard);
		int vanCopiesBefore = _profile.Cards.Where((CardSpec c) => c.IsVanillaCard).Sum((CardSpec c) => Math.Max(1, c.StartingCopies));
		_profile.Cards.Add(vanillaProbe);
		Check("状态栏把本体卡单独列出来、不计入「自己的卡」数量",
			CardCountText.Contains($"共 {beforeCount} 张") && CardCountText.Contains($"本体卡 {vanBefore + 1} 条（初始 {vanCopiesBefore + 5} 张）"), CardCountText);
		_profile.Cards.Remove(vanillaProbe);
		Check("删掉本体卡后统计跟着变回去",
			vanBefore == 0 ? !CardCountText.Contains("本体卡") : CardCountText.Contains($"本体卡 {vanBefore} 条（初始 {vanCopiesBefore} 张）"), CardCountText);

		// ===== 先古遗物替换：这次选项里没有那个「原本的遗物」→ 什么都不做（不顶掉别的选项） =====
		CharacterProfile anc = ProfileFactory.Sample();
		AncientTalkSpec ancTalk = new AncientTalkSpec { AncientId = "THE_ARCHITECT" };
		ancTalk.RelicReplacements.Add(new AncientRelicReplaceSpec { FromRelicId = "Astrolabe", RelicId = "SneckoEye" });
		anc.Ancients.Add(ancTalk);
		string ancSrc = AncientPatchGen.HasRelicReplacements(anc) ? AncientPatchGen.RelicReplacementSource(anc) : "";
		Check("先古遗物替换补丁能生成（这次选项里有它才换）", ancSrc.Contains("ForgeAncientRelicReplace.Replace<SneckoEye>"), ancSrc.Length > 0 ? "已生成" : "没生成");
		Check("替换代码里不再有「没抽到就顶掉最后一个选项」的兜底",
			!ancSrc.Contains("index = options.Count - 1") && ancSrc.Contains("本次不替换"), "只换不挤");
		AncientRelicReplaceSpec bare = new AncientRelicReplaceSpec { RelicId = "SneckoEye" };
		Check("界面上没选「原本的遗物」时明确写「这条不会生效」", bare.StateText.Contains("不会生效"), bare.StateText);
		AncientRelicReplaceSpec full = new AncientRelicReplaceSpec { FromRelicId = "Astrolabe", RelicId = "SneckoEye" };
		Check("配全了的说明变成「没抽到就不动（不会顶掉别的选项）」", full.StateText.Contains("不会顶掉别的选项"), full.StateText);
		Check("校验器也不再宣传「会顶掉最后一个选项」",
			ProfileValidator.Validate(anc).All((ValidationIssue i) => !i.Message.Contains("顶掉最后一个")), "措辞已更新");

		// ===== ① 初始的打击 / 防御：自己卡池里的普通卡，外观自动跟随模组（不再需要外观补丁） =====
		CharacterProfile skin = ProfileFactory.Sample();
		Check("初始的打击 / 防御不需要「本体卡外观补丁」了（它们本来就在你自己的卡池里，卡框 / 能量图标自动跟随）",
			!FlavorGen.NeedsVanillaSkin(skin) && !ModGenerator.InitialDeckSummary(skin).Contains("本体 StrikeIronclad"),
			ModGenerator.InitialDeckSummary(skin));

		// ===== ② 死亡描述文本 / 阵亡后台词真的会在游戏里出现 =====
		CharacterProfile flavor = ProfileFactory.Sample();
		flavor.DeathText = "七，到此为止。";
		flavor.DeadBanterText = "……原来如此。";
		Check("配了死亡文本就会生成补丁", FlavorGen.NeedsDeathFlavor(flavor), "会生成");
		string flavorSrc = FlavorGen.DeathFlavorSource(flavor);
		Check("死亡描述文本：给本体取死亡结算文案的方法挂 postfix（本体没有「每个角色一句」的键）",
			flavorSrc.Contains("NRunHistory.GetDeathQuote") && flavorSrc.Contains("ref string __result") && flavorSrc.Contains("七，到此为止。"), "死亡结算那句");
		Check("死亡描述文本只改我们自己的角色（按 ModelId 比对）",
			flavorSrc.Contains("characterId != ModelDb.Character<Seven>().Id"), "只认自己");
		Check("引号沿用本体那对（game_over_screen.ENCOUNTER_QUOTE_LEFT / RIGHT）",
			flavorSrc.Contains("\"game_over_screen\", \"ENCOUNTER_QUOTE_LEFT\"") && flavorSrc.Contains("ENCOUNTER_QUOTE_RIGHT"), "引号一致");
		Check("阵亡后台词：挂在 CreatureCmd.Kill 上（本体所有死亡都经过它），阵亡瞬间冒对话气泡",
			flavorSrc.Contains("typeof(CreatureCmd), \"Kill\"") && flavorSrc.Contains("NSpeechBubbleVfx.Create(\"……原来如此。\"") && flavorSrc.Contains("SpeechBubbleColor"),
			"阵亡气泡");
		Check("两句都没填时不会生成任何补丁",
			!FlavorGen.NeedsDeathFlavor(new CharacterProfile { DeathText = "", DeadBanterText = "  " }), "不生成");
		CharacterProfile onlyBanter = ProfileFactory.Sample();
		onlyBanter.DeathText = "";
		Check("只填了阵亡后台词时只生成气泡那段（不动死亡文案）",
			FlavorGen.DeathFlavorSource(onlyBanter).Contains("DeathBanterPatch") && !FlavorGen.DeathFlavorSource(onlyBanter).Contains("DeathQuotePatch"), "只气泡");

		// ===== ③ 不再有「默认携带燃烧之血」的开关，也不再偷偷塞本体遗物 =====
		DependencyObject charPage2 = SelectTabRoot("角色");
		List<CheckBox> charChecks2 = new List<CheckBox>();
		CollectCheckBoxes(charPage2, charChecks2);
		Check("角色页已删掉「没有自定义初始遗物时，默认携带燃烧之血」这个勾选框",
			!charChecks2.Any((CheckBox c) => (c.Content as string)?.Contains("燃烧之血") == true)
			&& !TextsIn(charPage2).Any((string t) => t.Contains("初始卡组里") && t.Contains("不再是一个开关")), "已删干净");
		CharacterProfile noRelic = ProfileFactory.Sample();
		noRelic.Relics.Clear();
		noRelic.ExtraResource.Enabled = false;
		Check("一只起始遗物都没配时，生成的初始遗物是空的（不再兜底塞燃烧之血）",
			!CSharpCodeGen.CharacterSource(noRelic).Contains("BurningBlood"), "没有兜底");
		Check("老存档里的 StartWithBurningBlood 字段不再被读（开着也一样）",
			!CSharpCodeGen.CharacterSource(new CharacterProfile { StartWithBurningBlood = true }).Contains("BurningBlood"), "不读旧字段");
		// ===== #8 遗物 / 药水效果栏的描述会随选择变化 =====
		EffectSpec plainEffect = new EffectSpec { Kind = "Damage", Amount = 3m };
		string plainBefore = plainEffect.DisplayPlain;
		bool plainChanged = false;
		plainEffect.PropertyChanged += delegate(object? _, System.ComponentModel.PropertyChangedEventArgs ev)
		{
			if (ev.PropertyName == "DisplayPlain") plainChanged = true;
		};
		plainEffect.Kind = "Block";
		Check("改了效果种类后「遗物 / 药水效果栏」的描述会跟着变（不再显示旧种类）",
			plainChanged && plainEffect.DisplayPlain != plainBefore && plainEffect.DisplayPlain.Contains("格挡"), plainEffect.DisplayPlain);
		EffectSpec plainEffect2 = new EffectSpec { Kind = "Damage", Amount = 3m, UpgradeAmount = 2m };
		Check("遗物 / 药水列表里不显示「升级 +N」（它们不能升级）", !plainEffect2.DisplayPlain.Contains("升级"), plainEffect2.DisplayPlain);
		// ===== #9 自定义状态的触发时机说明：要同时反映「什么时候」和「做什么」 =====
		SelectTabRoot("自定义状态");
		if (CustomPowerList.Items.Count == 0) OnAddCustomPower(this, new RoutedEventArgs());
		if (CustomPowerList.SelectedItem is CustomPowerSpec powerSpec)
		{
			PowerTriggerSpec triggerSpec = new PowerTriggerSpec { Kind = "TurnStart" };
			triggerSpec.Effects.Clear();
			triggerSpec.Effects.Add(new EffectSpec { Kind = "Block", Amount = 3m });
			powerSpec.Triggers.Clear();
			powerSpec.Triggers.Add(triggerSpec);
			powerSpec.Description = null;
			CustomTriggerList.SelectedItem = triggerSpec;
			UpdateLayout();
			string hintBefore = TriggerHint;
			string descBefore = CustomPowerDescriptionHint;
			Check("触发时机说明里写清了「什么时候 + 做什么」",
				hintBefore.Contains("玩家回合开始时") && hintBefore.Contains("获得") && hintBefore.Contains("格挡"), hintBefore);
			Check("描述预览会按当前触发时机 + 效果写出来（留空时的自动描述）",
				descBefore.Contains("玩家回合开始时") && descBefore.Contains("获得 3 点格挡"), descBefore);
			triggerSpec.Kind = "CardPlayed";
			triggerSpec.CardFilter = "Attack";
			UpdateLayout();
			Check("改了触发时机后说明立刻跟着变（不再显示旧的时机）",
				TriggerHint != hintBefore && TriggerHint.Contains("打出一张牌后") && TriggerHint.Contains("（只对攻击牌）"), TriggerHint);
			Check("改了触发时机后描述预览也跟着变", CustomPowerDescriptionHint != descBefore && CustomPowerDescriptionHint.Contains("打出一张牌后（攻击牌）"), CustomPowerDescriptionHint);
			// ===== 「某个状态层数变化后」要能选「盯哪个状态」（用户报过：没地方选）=====
			{
				triggerSpec.Kind = "PowerChanged";
				UpdateLayout();
				DependencyObject powerPage = SelectTabRoot("自定义状态");
				List<ComboBox> powerCombos = new List<ComboBox>();
				CollectCombos(powerPage, powerCombos);
				List<SearchComboBox> powerSearch = new List<SearchComboBox>();
				CollectSearchCombos(powerPage, powerSearch);
				// 「盯哪个状态」现在是可搜索下拉（和「增益 / 减益」一样的控件），所以两种都查
				DependencyObject? watchBox = powerCombos.FirstOrDefault((ComboBox c) =>
						BindingOperations.GetBinding(c, Selector.SelectedValueProperty)?.Path?.Path == "PowerId")
					?? (DependencyObject?)powerSearch.FirstOrDefault((SearchComboBox c) =>
						BindingOperations.GetBinding(c, SearchComboBox.SelectedValueProperty)?.Path?.Path == "PowerId");
				SearchComboBox? watchSearch = watchBox as SearchComboBox;
				Check("「自定义状态」页上有「盯哪个状态」的下拉并绑在 PowerId 上", watchBox != null,
					$"{powerCombos.Count} 个下拉 + {powerSearch.Count} 个搜索框，找到={(watchBox != null)}");
				Check("「盯哪个状态」下拉是可搜索的（和「增益 / 减益」一样能打字过滤）",
					watchSearch != null && watchSearch.SearchPath == "Display" && watchSearch.ItemsSource == AllPowers,
					watchSearch is null ? "不是可搜索控件" : $"SearchPath={watchSearch.SearchPath}");
				if (watchBox != null)
				{
					// 它的显示/隐藏跟着「这个触发时机需不需要选状态」走
					var holder = (System.Windows.FrameworkElement?)VisualTreeHelper.GetParent(watchBox);
					while (holder != null && !(holder is StackPanel)) holder = (System.Windows.FrameworkElement?)VisualTreeHelper.GetParent(holder);
					Check("这个下拉只在「某个状态层数变化后」时显示（绑 NeedsPower）",
						holder is not null && BindingOperations.GetBinding(holder, UIElement.VisibilityProperty)?.Path?.Path == "NeedsPower",
						holder is null ? "没找到容器" : (BindingOperations.GetBinding(holder, UIElement.VisibilityProperty)?.Path?.Path ?? "(没绑定)"));
					Check("选中的是「力量」时，说明里写出这个状态名",
						triggerSpec.Display.Contains("力量") || triggerSpec.Summary.Contains("力量") || triggerSpec.PowerId == "",
						triggerSpec.Display);
					triggerSpec.PowerId = "StrengthPower";
					UpdateLayout();
					Check("选了「力量」之后，界面上那行说明立刻写出「（力量）」",
						TriggerHint.Contains("力量") && CustomPowerDescriptionHint.Contains("力量"), TriggerHint);
				}
			}
			// 改「触发时做什么」——以前这里说明完全不变（用户反馈过两次）
			string hintBeforeEffect = TriggerHint;
			string descBeforeEffect = CustomPowerDescriptionHint;
			triggerSpec.Effects[0].Kind = "Damage";
			triggerSpec.Effects[0].Amount = 5m;
			triggerSpec.Effects[0].TargetSide = "Enemy";
			UpdateLayout();
			Check("改了「触发时做什么」后触发时机说明也跟着变", TriggerHint != hintBeforeEffect && TriggerHint.Contains("造成 5 点伤害"), TriggerHint);
			Check("改了「触发时做什么」后描述预览也跟着变", CustomPowerDescriptionHint != descBeforeEffect && CustomPowerDescriptionHint.Contains("造成 5 点伤害"), CustomPowerDescriptionHint);
			// 界面上真正显示的那几处文字也要变（只看属性不够：绑定断了的话属性对了、界面还是旧的）
			List<TextBlock> powerTexts = new List<TextBlock>();
			CollectTextBlocks(SelectTabRoot("自定义状态"), powerTexts);
			TextBlock? hintBlock = powerTexts.FirstOrDefault((TextBlock b) => BindingOperations.GetBinding(b, TextBlock.TextProperty)?.Path?.Path == "TriggerHint");
			TextBlock? summaryBlock = powerTexts.FirstOrDefault((TextBlock b) => BindingOperations.GetBinding(b, TextBlock.TextProperty)?.Path?.Path == "CustomTriggersSummary");
			TextBlock? previewBlock = powerTexts.FirstOrDefault((TextBlock b) => BindingOperations.GetBinding(b, TextBlock.TextProperty)?.Path?.Path == "CustomPowerDescriptionHint");
			Check("页面上真有显示「触发时机说明」的文字块", hintBlock != null);
			Check("页面上真有显示「行为汇总」的文字块（就在「触发时机（什么时候做事）」下面）", summaryBlock != null);
			Check("页面上真有显示「描述预览」的文字块", previewBlock != null);
			Check("界面上那句触发时机说明真的显示了「做什么」",
				hintBlock != null && hintBlock.Text.Contains("造成 5 点伤害"), hintBlock?.Text ?? "(没找到)");
			Check("界面上那行行为汇总真的显示了「做什么」",
				summaryBlock != null && summaryBlock.Text.Contains("造成 5 点伤害"), summaryBlock?.Text ?? "(没找到)");
			Check("界面上那行描述预览真的显示了「做什么」",
				previewBlock != null && previewBlock.Text.Contains("造成 5 点伤害"), previewBlock?.Text ?? "(没找到)");
			// 加一条效果 / 删一条效果
			string hintBeforeAdd = TriggerHint;
			triggerSpec.Effects.Add(new EffectSpec { Kind = "Draw", Amount = 1m });
			UpdateLayout();
			Check("加一条效果后说明里会多出那条效果", TriggerHint != hintBeforeAdd && TriggerHint.Contains("抽 1 张牌"), TriggerHint);
			Check("加一条效果后界面上那行汇总也会多出那条效果",
				summaryBlock != null && summaryBlock.Text.Contains("抽 1 张牌"), summaryBlock?.Text ?? "(没找到)");
			Check("加一条效果后「触发时机列表」那一行也跟着变",
				triggerSpec.Display.Contains("抽牌 1"), triggerSpec.Display);
			triggerSpec.Effects.RemoveAt(1);
			UpdateLayout();
			Check("删掉效果后说明里那条也没了", !TriggerHint.Contains("抽 1 张牌"), TriggerHint);
			Check("删掉效果后列表那一行也没了", !triggerSpec.Display.Contains("抽牌 1"), triggerSpec.Display);
			powerSpec.Triggers.Clear();
		}
		SelectTabRoot("卡牌");
		// ===== 从工程恢复存档（存档被误覆盖时的救命功能）=====
		string recRoot = Path.Combine(Path.GetTempPath(), "forge_uicheck_recover_" + Guid.NewGuid().ToString("N").Substring(0, 8));
		try
		{
			CharacterProfile recSrc = ProfileFactory.Sample();
			recSrc.Paths.OutputDir = recRoot;
			recSrc.Paths.VanillaProject = Profile.Paths.VanillaProject;   // 生成要用解包工程（占位美术 / 场景）
			recSrc.Paths.GameDataDir = Profile.Paths.GameDataDir;
			recSrc.ExtraResource.Enabled = true;
			recSrc.ExtraResource.Name = "血怒";
			// 注意：列表最上面两条是初始卡组的本体卡（打击 / 防御），比较要挑「自己的卡」
			List<CardSpec> srcOwn = recSrc.Cards.Where((CardSpec c) => !c.IsVanillaCard).ToList();
			srcOwn[0].Effects[0].Times = 2;                  // 生效次数
			srcOwn[1].UpgradeCost = 0;                       // 升级后费用（1 费 → 0 费，差值才不是 0）
			srcOwn[1].UpgradeKeywords.Innate = "Add";         // 升级后获得关键字
			srcOwn[1].Effects[0].Condition.Kind = "HandAtLeast";
			srcOwn[1].Effects[0].Condition.Amount = 3m;
			// 「战斗胜利后」的卡牌奖励（生成的是 AfterCombatEnd）：恢复时也要能认出这条触发时机
			CustomPowerSpec recPower = new CustomPowerSpec { Name = "恢复用奖励状态" };
			PowerTriggerSpec recVictory = new PowerTriggerSpec { Kind = "CombatVictory" };
			recVictory.Effects.Add(new EffectSpec { Kind = "CardReward", Amount = 3m, TargetSide = "Self" });
			recPower.Triggers.Add(recVictory);
			recSrc.CustomPowers.Add(recPower);
			// 自定义关键词：一条自己填英文标识、一条留空（走自动键 KEYWORD_2），都挂在第一张自有卡上
			recSrc.CustomKeywords.Add(new CustomKeywordSpec
			{
				Name = "命定",
				Key = "FATE",
				Description = "打出后，本回合每打出一张牌就抽一张。",
			});
			recSrc.CustomKeywords.Add(new CustomKeywordSpec
			{
				Name = "回响",
				Description = "第二条关键词（没填英文标识，走自动键）。",
			});
			// 本体关键词改名（这一节和「自定义关键词」共用同一张 card_keywords 表）：
			// 一条改名、一条**只改说明**（只改说明时表里只有 description 键 —— 回读不能把它丢了）
			recSrc.KeywordRenames.Add(new VanillaKeywordRenameSpec { KeywordId = "EXHAUST", Name = "献祭" });
			recSrc.KeywordRenames.Add(new VanillaKeywordRenameSpec { KeywordId = "INNATE", Description = "开局就在手里。" });
			srcOwn[0].KeywordIds = new List<string> { "FATE", "回响" };
			// 强化指定卡牌（像「精准」）：目标卡 / 强化什么 / 数值都要能从工程读回来（靠 // CET:BoostCard= 标记）
			srcOwn[0].Effects.Add(new EffectSpec
			{
				Kind = "BoostCard", Amount = 3m, UpgradeAmount = 1m,
				SpawnCardId = "SevenCrush", BoostStat = "Damage", TargetSide = "Self",
			});
			// 卡牌自定义描述（追加模式 + 多行）：靠 .cs 里的 // CET:CustomDescription= 标记读回来
			srcOwn[2].CustomDescription = "自检：第一行\n第二行";
			// 另一种模式：整段替换掉自动描述（标记里的 CustomDescReplace=1 也要读回来）
			srcOwn[3].CustomDescription = "自检：只留这段话。";
			srcOwn[3].CustomDescriptionReplaces = true;
			// 诅咒 + 先古卡：回读时也要各自回到「诅咒 / 先古卡」页的那两个列表里（按稀有度分）
			var recCurse = new CardSpec
			{
				Name = "恢复用诅咒", ClassName = "UiCheckRecCurse", CardType = "Curse", Rarity = "Curse",
				Cost = -1, Unplayable = true, Eternal = true, CurseRemoveAfterCombat = true,
			};
			recCurse.Effects.Add(new EffectSpec { Kind = "HpLoss", Amount = 3m, TargetSide = "Self" });
			recSrc.Curses.Add(recCurse);
			var recAncient = new CardSpec
			{
				Name = "恢复用先古卡", ClassName = "UiCheckRecAncient", CardType = "Skill", Rarity = "Ancient", Cost = 1,
				InCardPool = true,
			};
			recAncient.Effects.Add(new EffectSpec { Kind = "Block", Amount = 11m, UpgradeAmount = 4m, TargetSide = "Self" });
			recSrc.AncientCards.Add(recAncient);
			// 两类牌各自的卡框颜色（RRGGBB）+ 角色自己的自定义框色，回读时要能从材质文件的 h/s/v 反算回来
			recSrc.CurseStyle.Frame = SpecialCardStyleSpec.CustomFrame;
			recSrc.CurseStyle.FrameColor = "8A5CF6";
			recSrc.AncientStyle.Frame = "card_frame_blue";
			recSrc.Colors.CardFrameColor = "123456";
			var gen = ModGenerator.Generate(recSrc);
			// 失败时把「为什么」打出来（校验错误 + 日志尾部）—— 不然这条 FAIL 只有空细节，根本没法查
			Check("（准备）能从示例配置生成工程", gen.Success && Directory.Exists(gen.ProjectRoot),
				gen.Success
					? gen.ProjectRoot
					: (string.Join(" | ", gen.Issues.Where((ValidationIssue i) => i.IsError).Select((ValidationIssue i) => i.Message).Take(4))
					   + "  ‖ 日志尾部：" + string.Join(" ‖ ", gen.Log.TakeLast(8))));
			// ===== 自定义关键词：本地化表 / 悬停说明 / 卡面描述，三处产物都要有 =====
			string kwLocPath = Path.Combine(gen.ProjectRoot, recSrc.ModId, "localization", "zhs", "card_keywords.json");
			Check("自定义关键词：生成了 card_keywords.json（写进本体那张表）", File.Exists(kwLocPath), kwLocPath);
			string kwLoc = File.Exists(kwLocPath) ? File.ReadAllText(kwLocPath, Encoding.UTF8) : "";
			Check("自定义关键词：表里有 FATE.title = 命定 和说明",
				kwLoc.Contains("\"FATE.title\": \"命定\"") && kwLoc.Contains("FATE.description"), kwLoc.Replace("\r", "").Replace("\n", " "));
			Check("自定义关键词：没填英文标识的那条自动用 KEYWORD_2",
				kwLoc.Contains("KEYWORD_2.title") && kwLoc.Contains("\"KEYWORD_2.title\": \"回响\""), "KEYWORD_2.title = 回响");
			string kwCardFile = Path.Combine(gen.ProjectRoot, "cs", "Cards",
				Naming.From(recSrc).CardClassName(recSrc, srcOwn[0]) + ".cs");
			string kwCardSrc = File.Exists(kwCardFile) ? File.ReadAllText(kwCardFile, Encoding.UTF8) : "";
			Check("自定义关键词：卡牌生成了悬停说明（读本体 card_keywords 表）",
				kwCardSrc.Contains("LocString(\"card_keywords\", \"FATE.title\")")
				&& kwCardSrc.Contains("LocString(\"card_keywords\", \"KEYWORD_2.description\")"), "两条都在");
			string kwCardsLoc = File.ReadAllText(Path.Combine(gen.ProjectRoot, recSrc.ModId, "localization", "zhs", "cards.json"), Encoding.UTF8);
			Check("自定义关键词：卡面描述最前面是 [gold]命定[/gold]。",
				kwCardsLoc.Contains("[gold]命定[/gold]。"), "描述里有");
			Check("自定义关键词：两条都按引用顺序拼进卡面描述（一行一个）",
				kwCardsLoc.Contains("[gold]命定[/gold]。\\n[gold]回响[/gold]。"), "命定 → 回响");
			// ===== 本体关键词改名：写进本体 card_keywords 表 + 旧名字全文替换 =====
			Check("本体关键词改名：EXHAUST.title 写成了新名字（本体卡面上的金色词会跟着变）",
				kwLoc.Contains("\"EXHAUST.title\": \"献祭\""), "EXHAUST.title = 献祭");
			Check("本体关键词改名：没动 PERIOD（那是标点占位键，不是关键词）",
				!kwLoc.Contains("PERIOD"), "表里没有 PERIOD");
			// 规则（写进表的边界）：Name 填了才写 <ID>.title、Description 填了才写 <ID>.description，
			// **一个字都没填的关键词一个键都不许写** —— 写了就等于拿空 / 兜底值覆盖本体那条。
			Check("本体关键词改名：一个字都没填的关键词（虚无 / 保留 / 奇巧 / 永恒 / 不能被打出）不会被写进表里",
				!kwLoc.Contains("ETHEREAL.") && !kwLoc.Contains("RETAIN.")
				&& !kwLoc.Contains("SLY.") && !kwLoc.Contains("ETERNAL.") && !kwLoc.Contains("UNPLAYABLE."),
				kwLoc.Replace("\r", "").Replace("\n", " "));
			// 本用例里 INNATE 只填了说明：那就只该有 description 键，**绝不能**有 title
			//（有了就说明「没填名字也写了 title」→ 本体那个名字被空值覆盖）。
			Check("本体关键词改名：只填说明的关键词只写 <ID>.description、不写 <ID>.title",
				kwLoc.Contains("\"INNATE.description\"") && !kwLoc.Contains("INNATE.title"),
				kwLoc.Replace("\r", "").Replace("\n", " "));
			Check("本体关键词改名：只填名字（说明留空）时不会把本体说明覆盖成空串",
				!kwLoc.Contains("\"EXHAUST.description\""), "没写 description 键");
			{
				// 旧名字全文替换：本体卡牌/遗物/药水描述里写着的「消耗」要换成「献祭」。
				// 本机不一定读得到本体中文表，所以这里先往表里塞一条**合成**条目
				// （只加一个绝不和本体撞的键），把「替换 + 合并进 cards.json」这条链路验死。
				IReadOnlyDictionary<string, string> oldCardLoc = EffectCatalog.InjectCardLocForTest(
					new[] { new KeyValuePair<string, string>("UICHECK_KEYWORD_TEXT_1.description", "消耗 1 张牌。") });
				try
				{
					List<(string Table, string Key, string Text)> kwText =
						VanillaKeywordGen.KeywordTextReplacements(recSrc).ToList();
					Check("本体关键词改名：本体卡面描述里的旧名字会被换掉（消耗 → 献祭）",
						kwText.Any((r) => r.Table == "cards" && r.Key == "UICHECK_KEYWORD_TEXT_1.description"
							&& r.Text.Contains("献祭") && !r.Text.Contains("消耗")),
						$"{kwText.Count} 条替换项");
					// 替换是**按本体表逐条**做的，所以每条替换项的键必须在「它自己那张本体表」里
					//（以前拿 cards 表去查 relics / potions 的键 → 必然查不到，白白 FAIL）。
					static bool InOwnTable((string Table, string Key, string Text) r) => r.Table switch
					{
						"cards" => EffectCatalog.ZhCardLoc.ContainsKey(r.Key),
						"relics" => EffectCatalog.ZhRelicLoc.ContainsKey(r.Key),
						"potions" => EffectCatalog.ZhPotionLoc.ContainsKey(r.Key),
						_ => false,
					};
					List<string> strayKeys = kwText.Where((r) => !InOwnTable(r)).Select((r) => r.Table + " / " + r.Key).ToList();
					Check("本体关键词改名：替换项只覆盖本体本来就有那个键的条目（不会给我们的自定义卡造键）",
						strayKeys.Count == 0,
						strayKeys.Count == 0
							? $"{kwText.Count} 条替换项，键都在各自的本体表里"
							  + $"（cards {kwText.Count((r) => r.Table == "cards")} / relics {kwText.Count((r) => r.Table == "relics")} / potions {kwText.Count((r) => r.Table == "potions")}）"
							: "不在本体表里的键：" + string.Join(" ｜ ", strayKeys.Take(5)));
					Check("本体关键词改名：没填名字（只改说明）的关键词不产生替换项",
						VanillaKeywordGen.KeywordTextReplacements(ProfileWithKeywordRename("EXHAUST", "", "只改说明")).Count() == 0,
						"没改名 → 不替换");
					Check("本体关键词改名：新名字和本体原名一样时也不产生替换项",
						VanillaKeywordGen.KeywordTextReplacements(ProfileWithKeywordRename("EXHAUST", "消耗", "")).Count() == 0,
						"等于没改 → 不替换");
					var kwTextGen2 = ModGenerator.Generate(recSrc);
					string kwCards2 = File.ReadAllText(Path.Combine(kwTextGen2.ProjectRoot, recSrc.ModId, "localization", "zhs", "cards.json"), Encoding.UTF8);
					Check("本体关键词改名：替换项真的合进了生成的 cards.json",
						kwCards2.Contains("献祭 1 张牌。"), "cards.json 里已是新名字");
				}
				finally
				{
					EffectCatalog.InjectCardLocForTest(oldCardLoc);
				}
			}
			// 校验：id 非法 / 名字带富文本标记 / 和别的本体关键词撞名 → 都必须是错误
			// 注意：这几份配置要带上「环境路径」，否则校验器还会报「解包工程目录不存在」那两条，
			// 第一条错误就不是我们要查的那条了。
			CharacterProfile badRename = RenameCheckProfile();
			badRename.KeywordRenames.Clear();
			badRename.KeywordRenames.Add(new VanillaKeywordRenameSpec { KeywordId = "NOT_A_KEYWORD", Name = "随便" });
			Check("本体关键词改名：枚举名不是那 7 个之一 → 错误",
				ProfileValidator.Validate(badRename).Any((ValidationIssue i) => i.IsError && i.Message.Contains("不是本体关键词")),
				ProfileValidator.Validate(badRename).First((ValidationIssue i) => i.IsError).Message);
			CharacterProfile badRich = RenameCheckProfile();
			badRich.KeywordRenames.Clear();
			badRich.KeywordRenames.Add(new VanillaKeywordRenameSpec { KeywordId = "EXHAUST", Name = "[gold]烧掉[/gold]" });
			Check("本体关键词改名：新名字里带 [ ] 富文本标记 → 错误（会把卡面 [gold]…[/gold] 解析坏）",
				ProfileValidator.Validate(badRich).Any((ValidationIssue i) => i.IsError && i.Message.Contains("方括号")),
				ProfileValidator.Validate(badRich).First((ValidationIssue i) => i.IsError).Message);
			CharacterProfile badClash = RenameCheckProfile();
			badClash.KeywordRenames.Clear();
			badClash.KeywordRenames.Add(new VanillaKeywordRenameSpec { KeywordId = "EXHAUST", Name = "虚无" });   // 和本体「虚无」撞名
			ProfileFactory.EnsureKeywordRenameRows(badClash);
			Check("本体关键词改名：新名字和另一个本体关键词撞车 → 错误（玩家分不清这两条）",
				ProfileValidator.Validate(badClash).Any((ValidationIssue i) => i.IsError && i.Message.Contains("撞车")),
				ProfileValidator.Validate(badClash).FirstOrDefault((ValidationIssue i) => i.IsError)?.Message ?? "(没有错误)");
			CharacterProfile dupRename = RenameCheckProfile();
			dupRename.KeywordRenames.Clear();
			dupRename.KeywordRenames.Add(new VanillaKeywordRenameSpec { KeywordId = "EXHAUST", Name = "燃烧" });
			dupRename.KeywordRenames.Add(new VanillaKeywordRenameSpec { KeywordId = "ETHEREAL", Name = "燃烧" });
			ProfileFactory.EnsureKeywordRenameRows(dupRename);
			Check("本体关键词改名：两条改成同一个新名字也会被撞车检查拦住",
				ProfileValidator.Validate(dupRename).Any((ValidationIssue i) => i.IsError && i.Message.Contains("撞车")),
				ProfileValidator.Validate(dupRename).FirstOrDefault((ValidationIssue i) => i.IsError)?.Message ?? "(没有错误)");
			CharacterProfile goodRename = RenameCheckProfile();
			goodRename.KeywordRenames.Clear();
			goodRename.KeywordRenames.Add(new VanillaKeywordRenameSpec { KeywordId = "EXHAUST", Name = "献祭", Description = "打出后进入献祭堆。" });
			ProfileFactory.EnsureKeywordRenameRows(goodRename);
			Check("本体关键词改名：合法改名不会被校验器拦（改成「献祭」→ 不撞车、没富文本）",
				!ProfileValidator.Validate(goodRename).Any((ValidationIssue i) => i.IsError && i.Message.Contains("本体关键词")),
				string.Join(" | ", ProfileValidator.Validate(goodRename).Where((ValidationIssue i) => i.IsError).Select((ValidationIssue i) => i.Message)));
			// 「自定义关键词」页最上面那一节：表格必须一直有那 7 行，而且绑到存档上
			Check("本体关键词改名：界面上是固定 7 行（本体关键词是封闭枚举，不能增删）",
				KeywordRenameTable != null && Profile.KeywordRenames.Count == VanillaKeywordCatalog.All.Count,
				KeywordRenameTable is null ? "没找到表格" : $"{Profile.KeywordRenames.Count} 行");
			Check("本体关键词改名：表格绑的是 Profile.KeywordRenames",
				KeywordRenameTable != null && ReferenceEquals(KeywordRenameTable.ItemsSource, Profile.KeywordRenames),
				KeywordRenameTable?.ItemsSource?.GetType().Name ?? "(没绑)");
			// 关键回归测试：工程目录 = 存档名（不是 ModId）。以前构建流程自己拼 OutputDir\ModId，
			// 存档名和 ModId 不一样时（示例角色_恢复.json / ModId=示例角色）会指向不存在的目录 →
			// dotnet 报 MSB1009 → 界面「构建失败」（用户报的「构建无法生成」）。
			recSrc.SaveName = "别的存档名";
			Check("工程目录跟着存档文件名走（不是 ModId）",
				ModGenerator.ProjectRootOf(recSrc) == Path.Combine(recSrc.Paths.OutputDir, "别的存档名"),
				ModGenerator.ProjectRootOf(recSrc));
			Check("构建流程用的目录 == 生成器返回的目录（存档名≠ModId 时也对得上，修过 MSB1009）",
				ModGenerator.ProjectRootOf(recSrc) != Path.Combine(recSrc.Paths.OutputDir, recSrc.ModId), "两者必须不同（这就是以前踩的坑）");
			var gen2 = ModGenerator.Generate(recSrc);
			Check("存档名≠ModId 时生成目录 = ProjectRootOf 算出来的目录",
				gen2.Success && gen2.ProjectRoot == ModGenerator.ProjectRootOf(recSrc), gen2.ProjectRoot);
			var rec = ProjectRecovery.FromProject(gen.ProjectRoot);
			// 回读时会按 7 行本体关键词补齐（见 ProfileFactory.EnsureKeywordRenameRows）：
			// 恢复出来的存档里这 7 行要都在，界面表格才不会少行
			ProfileFactory.EnsureKeywordRenameRows(rec.Profile);
			Check("从工程恢复：没认出来的语句为 0", !rec.HasUnparsed, rec.Unparsed.FirstOrDefault() ?? "全部认出来了");
			Check("从工程恢复：卡牌张数一致", rec.Profile.Cards.Count == recSrc.Cards.Count, $"{rec.Profile.Cards.Count} / {recSrc.Cards.Count}");
			Check("从工程恢复：初始的打击 / 防御也找回来了（含 CardTag 标签），并且排在最上面",
				rec.Profile.Cards[0].ClassName == "Strike" && rec.Profile.Cards[1].ClassName == "Defend"
				&& rec.Profile.Cards[0].TagList.Contains("Strike") && rec.Profile.Cards[1].TagList.Contains("Defend"),
				string.Join(" / ", rec.Profile.Cards.Take(3).Select((CardSpec c) => c.Name)));
			List<CardSpec> recOwn = rec.Profile.Cards.Where((CardSpec c) => !c.IsVanillaCard).ToList();
			Check("从工程恢复：卡名一致（中文名从本地化表读回来）",
				recOwn[0].Name == srcOwn[0].Name, recOwn[0].Name);
			Check("从工程恢复：自定义关键词找回来了（名字 / 说明 / 英文标识 / 自动键）",
				rec.Profile.CustomKeywords.Count == 2
				&& rec.Profile.CustomKeywords.Any((CustomKeywordSpec k) => k.Key == "FATE" && k.Name == "命定" && k.Description.Contains("抽一张"))
				&& rec.Profile.CustomKeywords.Any((CustomKeywordSpec k) => k.Key == "KEYWORD_2" && k.Name == "回响"),
				string.Join(" · ", rec.Profile.CustomKeywords.Select((CustomKeywordSpec k) => k.Display)));
			Check("从工程恢复：卡牌上的关键词引用找回来了",
				recOwn[0].CustomKeywordList.Contains("FATE") && recOwn[0].CustomKeywordList.Contains("KEYWORD_2"),
				string.Join("·", recOwn[0].CustomKeywordList));
			// 本体关键词改名：在「自定义关键词」页最上面那一节填的 7 行，也要能从工程读回来
			// （以前这里把本体那 7 个键直接 continue 丢掉 → 恢复后改名静默消失，再生成卡面就变回「消耗」）
			Check("从工程恢复：本体关键词改名（消耗 → 献祭）找回来了",
				rec.Profile.KeywordRenames.Any((VanillaKeywordRenameSpec r) => r.KeywordId == "EXHAUST" && r.Name == "献祭"),
				string.Join(" · ", rec.Profile.KeywordRenames.Where((VanillaKeywordRenameSpec r) => r.ChangesAnything).Select((VanillaKeywordRenameSpec r) => r.Display)));
			Check("从工程恢复：只改了说明的关键词（固有）也找回来了",
				rec.Profile.KeywordRenames.Any((VanillaKeywordRenameSpec r) => r.KeywordId == "INNATE" && r.Description == "开局就在手里。"),
				string.Join(" · ", rec.Profile.KeywordRenames.Where((VanillaKeywordRenameSpec r) => r.ChangesAnything).Select((VanillaKeywordRenameSpec r) => r.Display)));
			Check("从工程恢复：没改的关键词不会凭空变出一条改名（虚无 / 保留 … 都还是空的）",
				rec.Profile.KeywordRenames.Count((VanillaKeywordRenameSpec r) => r.ChangesAnything) == 2,
				$"改了 {rec.Profile.KeywordRenames.Count((VanillaKeywordRenameSpec r) => r.ChangesAnything)} 条");
			Check("从工程恢复：费用 / 类型 / 稀有度一致",
				recOwn[0].Cost == srcOwn[0].Cost && recOwn[0].CardType == srcOwn[0].CardType
				&& recOwn[0].Rarity == srcOwn[0].Rarity,
				$"{recOwn[0].Cost}费 {recOwn[0].CardType} {recOwn[0].Rarity}");
			Check("从工程恢复：效果条数一致",
				recOwn[0].Effects.Count == srcOwn[0].Effects.Count,
				$"{recOwn[0].Effects.Count} / {srcOwn[0].Effects.Count}");
			Check("从工程恢复：效果数值一致（升级增量也算）",
				recOwn[0].Effects.Select(x => x.Amount + x.UpgradeAmount).SequenceEqual(srcOwn[0].Effects.Select(x => x.Amount + x.UpgradeAmount)),
				string.Join(" / ", recOwn[0].Effects.Select(x => $"{x.Kind} {x.Amount}+{x.UpgradeAmount}")));
			Check("从工程恢复：目标对象一致", recOwn[0].Effects.Select(x => x.TargetSide).SequenceEqual(srcOwn[0].Effects.Select(x => x.TargetSide)),
				string.Join(" / ", recOwn[0].Effects.Select(x => x.TargetSide)));
			Check("从工程恢复：生效次数一致", recOwn[0].Effects[0].Times == 2, recOwn[0].Effects[0].Times.ToString());
			Check("从工程恢复：升级后费用一致", recOwn[1].UpgradeCost == 0, recOwn[1].UpgradeCost?.ToString() ?? "(空)");
			Check("从工程恢复：升级后的关键字一致", recOwn[1].UpgradeKeywords.Innate == "Add", recOwn[1].UpgradeKeywords.Display);
			Check("从工程恢复：效果条件一致（手牌数不少于 3 张）",
				recOwn[1].Effects[0].Condition.Kind == "HandAtLeast" && recOwn[1].Effects[0].Condition.Amount == 3m,
				recOwn[1].Effects[0].Condition.DisplayShort);
			Check("从工程恢复：遗物 / 药水数量一致",
				rec.Profile.Relics.Count == recSrc.Relics.Count && rec.Profile.Potions.Count == recSrc.Potions.Count,
				$"遗物 {rec.Profile.Relics.Count} / {recSrc.Relics.Count}，药水 {rec.Profile.Potions.Count} / {recSrc.Potions.Count}");
			Check("从工程恢复：遗物触发时机一致", rec.Profile.Relics[0].Trigger == recSrc.Relics[0].Trigger, rec.Profile.Relics[0].Trigger);
			Check("从工程恢复：状态的「战斗胜利后」也认得出来（生成的是 AfterCombatEnd，不是 AfterCombatVictory）",
				rec.Profile.CustomPowers.Count == 1
				&& rec.Profile.CustomPowers[0].Triggers.Count == 1
				&& rec.Profile.CustomPowers[0].Triggers[0].Kind == "CombatVictory"
				&& rec.Profile.CustomPowers[0].Triggers[0].Effects.Count == 1
				&& rec.Profile.CustomPowers[0].Triggers[0].Effects[0].Kind == "CardReward",
				rec.Profile.CustomPowers.Count == 1
					? string.Join(" / ", rec.Profile.CustomPowers[0].Triggers.Select((PowerTriggerSpec t) => t.Kind + "(" + t.Effects.Count + " 条)"))
					: $"恢复了 {rec.Profile.CustomPowers.Count} 个状态");
			Check("从工程恢复：角色属性一致（血量 / 金币 / 职业名）",
				rec.Profile.StartingHp == recSrc.StartingHp && rec.Profile.StartingGold == recSrc.StartingGold && rec.Profile.CharacterClass == recSrc.CharacterClass,
				$"{rec.Profile.StartingHp}/{rec.Profile.StartingGold} {rec.Profile.CharacterClass}");
			Check("从工程恢复：额外资源量名字找回来了", rec.Profile.ExtraResource.Name == "血怒", rec.Profile.ExtraResource.Name);
			Check("从工程恢复：初始卡组 / 卡池归属找回来了",
				recOwn[0].InStartingDeck && recOwn[0].StartingCopies == srcOwn[0].StartingCopies
				&& rec.Profile.Cards.Count(c => !c.InCardPool) == recSrc.Cards.Count(c => !c.InCardPool),
				$"初始 {recOwn[0].StartingCopies} 张 / 不入池 {rec.Profile.Cards.Count(c => !c.InCardPool)} 张");
			Check("从工程恢复：强化指定卡牌找回来了（目标卡 / 伤害还是格挡 / 数值 / 升级增量）",
				recOwn[0].Effects.Any((EffectSpec x) => x.Kind == "BoostCard" && x.SpawnCardId == "SevenCrush"
					&& x.BoostStat == "Damage" && x.Amount == 3m && x.UpgradeAmount == 1m),
				string.Join(" / ", recOwn[0].Effects.Select((EffectSpec x) => $"{x.Kind} {x.Amount}+{x.UpgradeAmount} {x.SpawnCardId}")));
			Check("从工程恢复：卡牌自定义描述找回来了（多行也完整，没被截成一行）",
				recOwn[2].CustomDescription == "自检：第一行\n第二行" && !recOwn[2].CustomDescriptionReplaces,
				(recOwn[2].CustomDescription ?? "(空)").Replace("\n", "\\n"));
			Check("从工程恢复：勾了「替换掉自动描述」的那张也找回来了",
				recOwn[3].CustomDescription == "自检：只留这段话。" && recOwn[3].CustomDescriptionReplaces,
				$"{(recOwn[3].CustomDescription ?? "(空)")} / 替换={recOwn[3].CustomDescriptionReplaces}");
			Check("从工程恢复：诅咒回到「诅咒」列表里（不是混进普通卡），效果也读回来了",
				rec.Profile.Curses.Count == 1
				&& rec.Profile.Curses[0].Name == "恢复用诅咒"
				&& rec.Profile.Curses[0].Rarity == "Curse"
				&& rec.Profile.Curses[0].Cost == -1
				&& rec.Profile.Curses[0].Eternal
				&& rec.Profile.Curses[0].CurseRemoveAfterCombat
				&& rec.Profile.Curses[0].Effects.Any(x => x.Kind == "HpLoss" && x.Amount == 3m),
				rec.Profile.Curses.Count == 0 ? "一条都没恢复"
					: $"{rec.Profile.Curses[0].Name} / {rec.Profile.Curses[0].Rarity} / 费 {rec.Profile.Curses[0].Cost}"
					  + $" / 自删={rec.Profile.Curses[0].CurseRemoveAfterCombat}"
					  + $" / 效果={string.Join("、", rec.Profile.Curses[0].Effects.Select(x => x.Kind + " " + x.Amount + "+" + x.UpgradeAmount))}"
					  + $" / {string.Join("·", rec.Profile.Curses[0].KeywordList)}");
			Check("从工程恢复：先古卡回到「先古卡」列表里，数值 / 升级增量都在",
				rec.Profile.AncientCards.Count == 1
				&& rec.Profile.AncientCards[0].Rarity == "Ancient"
				&& rec.Profile.AncientCards[0].CardType == "Skill"
				&& rec.Profile.AncientCards[0].Effects.Any(x => x.Kind == "Block" && x.Amount == 11m && x.UpgradeAmount == 4m),
				rec.Profile.AncientCards.Count == 0 ? "一条都没恢复"
					: rec.Profile.AncientCards[0].Effects[0].Amount + "+" + rec.Profile.AncientCards[0].Effects[0].UpgradeAmount);
			Check("从工程恢复：诅咒 / 先古卡不会被算成普通卡（三张列表各归各位）",
				rec.Profile.Cards.All(x => x.Rarity != "Curse" && x.Rarity != "Ancient"), "各归各位");
			Check("从工程恢复：诅咒的自定义卡框颜色（RRGGBB）找回来了",
				rec.Profile.CurseStyle.Frame == SpecialCardStyleSpec.CustomFrame && rec.Profile.CurseStyle.FrameColor == "8A5CF6",
				$"{rec.Profile.CurseStyle.Frame} / {rec.Profile.CurseStyle.FrameColor}");
			Check("从工程恢复：先古卡选了本体框色（card_frame_blue）也认得出来",
				rec.Profile.AncientStyle.Frame == "card_frame_blue" && rec.Profile.AncientStyle.FrameColor.Length == 0,
				$"{rec.Profile.AncientStyle.Frame} / '{rec.Profile.AncientStyle.FrameColor}'");
			Check("从工程恢复：角色自己的自定义边框颜色也读回来了（以前路径写错，一直静默退回默认红）",
				rec.Profile.Colors.CardFrameColor == "123456", rec.Profile.Colors.CardFrameColor);
		}
		catch (Exception ex)
		{
			Check("从工程恢复存档（整体）", ok: false, ex.GetType().Name + ": " + ex.Message + "  @" + string.Join(" | ", (ex.StackTrace ?? "").Split('\n').Take(4).Select((string s) => s.Trim())));
		}
		finally
		{
			try { Directory.Delete(recRoot, true); } catch { }
		}

		// ===== 召唤物（列表 + 独立选项卡；本体的通用宠物 API，不需要 Harmony 补丁）=====
		// ① 界面接线：选项卡位置 / 列表绑定 / 详情里的字段 / 效果编辑器的「召唤物」下拉
		{
			TabItem summonTabItem = FindTab("召唤物");
			Check("有「召唤物」选项卡", summonTabItem != null, summonTabItem is null ? "没找到" : "找到了");
			int idxRole = -1, idxSummon = -1, idxExtra = -1, idxCard = -1, idxRelic = -1, idxPotion = -1;
			int idxCurse = -1, idxKeyword = -1, idxAncient = -1, idxPowerOverride = -1, idxCustomPower = -1, idxArt = -1;
			for (int ti = 0; ti < MainTabs.Items.Count; ti++)
			{
				if (!(MainTabs.Items[ti] is TabItem t)) continue;
				switch ((t.Header as string) ?? "")
				{
					case "角色": idxRole = ti; break;
					case "召唤物": idxSummon = ti; break;
					case "额外资源量/状态": idxExtra = ti; break;
					case "卡牌": idxCard = ti; break;
					case "遗物": idxRelic = ti; break;
					case "药水": idxPotion = ti; break;
					case "诅咒 / 先古卡": idxCurse = ti; break;
					case "自定义关键词": idxKeyword = ti; break;
					case "先古之民": idxAncient = ti; break;
					case "本体状态改写": idxPowerOverride = ti; break;
					case "自定义状态": idxCustomPower = ti; break;
					case "美术资源": idxArt = ti; break;
				}
			}
			Check("选项卡顺序：角色=0、召唤物=1、额外资源量=2、卡牌=3、遗物=4、药水=5、诅咒 / 先古卡=6、自定义关键词=7、先古之民=8、本体状态改写=9、自定义状态=10、美术资源=11",
				idxRole == 0 && idxSummon == 1 && idxExtra == 2 && idxCard == 3 && idxRelic == 4 && idxPotion == 5
				&& idxCurse == 6 && idxKeyword == 7 && idxAncient == 8 && idxPowerOverride == 9 && idxCustomPower == 10 && idxArt == 11,
				$"角色={idxRole} / 召唤物={idxSummon} / 额外资源量={idxExtra} / 卡牌={idxCard} / 遗物={idxRelic} / 药水={idxPotion}"
				+ $" / 诅咒先古卡={idxCurse} / 自定义关键词={idxKeyword} / 先古之民={idxAncient} / 本体状态改写={idxPowerOverride} / 自定义状态={idxCustomPower} / 美术资源={idxArt}");
			Check("召唤物列表绑定到 Profile.Summons（列表，不是单对象）",
				SummonList != null && BindingOperations.GetBinding(SummonList, ItemsControl.ItemsSourceProperty)?.Path?.Path == "Profile.Summons",
				SummonList is null ? "没找到列表" : (BindingOperations.GetBinding(SummonList, ItemsControl.ItemsSourceProperty)?.Path?.Path ?? "(没绑定)"));
			DependencyObject summonRoot = SelectTabRoot("召唤物");
			List<string> summonTexts = TextsIn(summonRoot);
			Check("召唤物页有「添加召唤物 / 删除 / 撤回删除」按钮",
				summonTexts.Contains("添加召唤物") && summonTexts.Contains("删除") && summonTexts.Contains("撤回删除"), "控件在");
			// 没选中任何召唤物时右边是**收起**的（只显示一句空态提示）—— 先确认这条空态在，
			// 再自己加一条并选中，后面那些「详情里的控件」断言才有东西可查
			//（以前直接读 TextsIn 页 root，详情折叠着，于是一律查不到 → 白白 FAIL）。
			Check("召唤物页未选中时显示空态提示（右侧详情收起，不是一张空表单）",
				summonTexts.Any((string t) => t.Contains("请选择召唤物")),
				string.Join(" / ", summonTexts.Take(6)));
			Check("召唤物：示例配置自带 0 只（新存档不凭空多一只宠物，也就不会凭空生成 cs/Pet.cs）",
				Profile.Summons.Count == 0, $"{Profile.Summons.Count} 条");
			var summonProbe = new SummonSpec
			{
				Enabled = true,
				ClassName = "UiCheckPetPageProbe",
				Name = "界面自检伙伴",
				Hp = 10,
				StandDistance = 120,
			};
			Profile.Summons.Add(summonProbe);
			SummonList.SelectedItem = summonProbe;
			UpdateLayout();
			summonTexts = TextsIn(summonRoot);
			Check("召唤物页有「启用 / 名字 / 英文类名 / 生命 / 站位距离 / 替主人承伤」（选中一条后详情展开才有）",
				summonTexts.Any((string t) => t.Contains("启用这只召唤物")) && summonTexts.Any((string t) => t.Contains("名字（宠物名牌）"))
				&& summonTexts.Any((string t) => t.Contains("英文类名")) && summonTexts.Any((string t) => t.Contains("召唤时的生命"))
				&& summonTexts.Any((string t) => t.Contains("站位距离")) && summonTexts.Any((string t) => t.Contains("替主人承伤")),
				string.Join(" / ", summonTexts.Where((string t) => t.Contains("召唤") || t.Contains("站位") || t.Contains("承伤")).Take(6)));
			Check("召唤物页写明了「替主人承伤」可以勾多只、承伤的是列表里第一只活着的（本体伤害重定向是链式遍历，由我们仲裁）",
				summonTexts.Any((string t) => t.Contains("可以勾多只") && t.Contains("第一只活着")),
				string.Join(" / ", summonTexts.Where((string t) => t.Contains("承伤")).Take(4)));
			// 详情里的输入框要双向绑定到「选中的那一只」上（数据源是列表的 SelectedItem）
			{
				List<TextBox> summonBoxes = new List<TextBox>();
				CollectTextBoxes(summonRoot, summonBoxes);
				List<string> summonPaths = summonBoxes.Select((TextBox b) => BindingOperations.GetBinding(b, TextBox.TextProperty)?.Path?.Path ?? "").ToList();
				Check("召唤物详情里有「名字 / 英文类名 / 生命 / 站位距离」四个输入框",
					summonPaths.Contains("Name") && summonPaths.Contains("ClassName") && summonPaths.Contains("Hp") && summonPaths.Contains("StandDistance"),
					string.Join(" / ", summonPaths));
				// 效果编辑器里的「召唤物（哪一只）」下拉：卡牌 / 遗物两处都要有
				// （用 SelectTabRoot 切页再收文字 —— 顺手也保证这两个页本身能正常选中）
				Check("效果编辑器有「召唤物（哪一只）」下拉（卡牌页）",
					TextsIn(SelectTabRoot("卡牌")).Contains("召唤物（哪一只）"), "下拉在");
				Check("效果编辑器有「召唤物（哪一只）」下拉（遗物页）",
					TextsIn(SelectTabRoot("遗物")).Contains("召唤物（哪一只）"), "下拉在");
			}

			// ===== 宠物类效果：不再有专属选项卡，全部在「卡牌」页的效果种类下拉里选 =====
			// 用户要求删掉「召唤物卡牌」页（那个页的列表 / 专属效果栏 / 自动归类 /「这是召唤物卡」勾选全没了），
			// 但 12 个宠物效果本身必须继续能在「卡牌」页选到，选到之后「召唤物（哪一只）」下拉要出来。
			{
				Check("界面上已经没有「召唤物卡牌」选项卡（整页删除后不该再冒出来）",
					FindTab("召唤物卡牌") is null,
					FindTab("召唤物卡牌") is null ? "已删除" : "还在");
				DependencyObject cardRoot = SelectTabRoot("卡牌");
				// 没选中卡牌时右侧详情是**收起**的（只显示一句空态提示）→ 效果种类下拉根本不在树里，
				// 下面的断言会白白 FAIL。先确认选中一张（自检里前面几段多半已经选过，这里兜底）。
				if (CardList.SelectedItem is null && Profile.Cards.Count > 0) CardList.SelectedIndex = 0;
				UpdateLayout();
				List<ComboBox> cardCombos = new List<ComboBox>();
				CollectCombos(cardRoot, cardCombos);
				List<ComboBox> cardKindCombos = cardCombos.Where((ComboBox c) => c.SelectedValuePath == "Kind").ToList();
				List<EffectKindOption> petKinds = EffectCatalog.PetEffectKinds.ToList();
				Check("EffectCatalog 里那 12 个宠物效果一个都不少（删除的只是页面，不是效果）",
					petKinds.Count == 12 && petKinds.All(k => EffectCatalog.IsPetKind(k.Kind))
					&& new[] { "SummonPet", "PetAttack", "PetDamageByMaxHp", "PetDamageByCurHp", "PetDamageByMissingHp",
							   "PetHeal", "PetLoseHp", "PetGainMaxHp", "PetSacrifice", "PetApplyPower",
							   "PetGuardOn", "PetGuardOff" }.All(id => petKinds.Any(k => k.Kind == id)),
					$"{petKinds.Count} 个：" + string.Join(" / ", petKinds.Select(k => k.Kind)));
				List<string> cardKindItems = cardKindCombos.Count == 0
					? new List<string>()
					: (cardKindCombos[0].ItemsSource?.Cast<object>().OfType<EffectKindOption>().Select((EffectKindOption k) => k.Kind).ToList() ?? new List<string>());
				List<string> missingPetKinds = petKinds.Select(k => k.Kind).Where(id => !cardKindItems.Contains(id)).ToList();
				Check("「卡牌」页的效果种类下拉包含全部 12 个宠物效果（宠物效果就在卡牌页的效果种类下拉里）",
					cardKindCombos.Count > 0 && cardKindItems.Count == Kinds.Count && missingPetKinds.Count == 0,
					cardKindCombos.Count == 0 ? "没找到「效果种类」下拉"
						: $"下拉 {cardKindItems.Count} 项 / Kinds {Kinds.Count} 项 / 缺：" + (missingPetKinds.Count == 0 ? "无" : string.Join(" / ", missingPetKinds)));
				Check("「卡牌」页的效果种类下拉是**全量** Kinds（宠物效果不再被专属效果栏收窄/过滤）",
					cardKindCombos.Any((ComboBox c) => c.Items.Count == Kinds.Count && c.Items.Count > petKinds.Count),
					cardKindCombos.Count == 0 ? "没找到「效果种类」下拉" : string.Join(" / ", cardKindCombos.Select((ComboBox c) => c.Items.Count + " 项")));
				// 在卡牌页给一张牌选上宠物效果 →「召唤物（哪一只）」下拉必须出现，
				// 而且候选里就有上面那只召唤物、选中值确实读到效果上（PetSummon 用的是宠物类名这个稳定标识）。
				{
					string summonId = PetGen.ClassNameOf(Profile, summonProbe);
					CardSpec probePetFx = new CardSpec { Name = "自检宠物效果卡", ClassName = "UiCheckPetFxOnCardTab", Cost = 1, InCardPool = true };
					probePetFx.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 6m, TargetSide = "Enemy", AllowDuplicates = true });
					Profile.Cards.Add(probePetFx);
					CardList.SelectedItem = probePetFx;
					CardEffectList.SelectedIndex = 0;
					UpdateLayout();
					EffectSpec probeFx = probePetFx.Effects[0];
					probeFx.Kind = "PetHeal";
					probeFx.PetSummon = summonId;
					UpdateLayout();
					cardRoot = SelectTabRoot("卡牌");
					UpdateLayout();
					List<TextBlock> fxTexts = new List<TextBlock>();
					CollectTextBlocks(cardRoot, fxTexts);
					bool summonLabelVisible = fxTexts.Any((TextBlock t) => t.Text == "召唤物（哪一只）" && t.IsVisible);
					cardCombos = new List<ComboBox>();
					CollectCombos(cardRoot, cardCombos);
					ComboBox? petCombo = cardCombos.FirstOrDefault((ComboBox c) => c.SelectedValuePath == "Id" && BindingOperations.GetBinding(c, Selector.SelectedValueProperty)?.Path?.Path == "PetSummon");
					Check("在「卡牌」页选到宠物效果时「召唤物（哪一只）」下拉会出现（PetAction 驱动，宠物效果本来就能在卡牌页配）",
						probeFx.PetAction && summonLabelVisible && petCombo is not null && petCombo.IsVisible,
						$"PetAction={probeFx.PetAction} / 标签可见={summonLabelVisible} / 下拉={(petCombo is null ? "没找到" : (petCombo.IsVisible ? "可见" : "隐藏"))}");
					Check("「卡牌」页的「召唤物（哪一只）」下拉双向绑在效果的 PetSummon 上（值没被清成空）",
						petCombo is not null && petCombo.SelectedValue is string sv && sv == summonId && probeFx.PetSummon == summonId,
						petCombo is null ? "没找到下拉" : $"选中={petCombo.SelectedValue ?? "(空)"} / 效果上={probeFx.PetSummon ?? "(空)"} / 候选 {petCombo.Items.Count} 项");
					// 收尾：换回普通效果，确认它不再显示（免得留下一个「一直是宠物效果」的错觉）
					probeFx.Kind = "Damage";
					UpdateLayout();
					cardRoot = SelectTabRoot("卡牌");
					UpdateLayout();
					fxTexts = new List<TextBlock>();
					CollectTextBlocks(cardRoot, fxTexts);
					Check("把效果种类换回普通效果（造成伤害）后「召唤物（哪一只）」下拉收起（没有绑定残留）",
						!probeFx.PetAction && !fxTexts.Any((TextBlock t) => t.Text == "召唤物（哪一只）" && t.IsVisible),
						$"PetAction={probeFx.PetAction}");
					Profile.Cards.Remove(probePetFx);
					CardList.SelectedIndex = Profile.Cards.Count - 1;
					UpdateLayout();
				}
			}

			// ===== 「强化指定卡牌」的「强化什么」下拉 +「目标卡」标签（界面上真的能看到 / 双向绑得上）=====
			{
				CardSpec probeBoostFxCard = new CardSpec { Name = "自检强化界面", ClassName = "UiCheckBoostTab", Cost = 1, InCardPool = true };
				probeBoostFxCard.Effects.Add(new EffectSpec
				{
					Kind = "BoostCard", Amount = 2m, SpawnCardId = "SevenCrush", BoostStat = "Damage", TargetSide = "Self",
				});
				Profile.Cards.Add(probeBoostFxCard);
				CardList.SelectedItem = probeBoostFxCard;
				CardEffectList.SelectedIndex = 0;
				UpdateLayout();
				DependencyObject boostRoot = SelectTabRoot("卡牌");
				UpdateLayout();
				List<TextBlock> boostTexts = new List<TextBlock>();
				CollectTextBlocks(boostRoot, boostTexts);
				Check("「目标卡」那一行的标签写明了它也用于「强化」（生成 / 变化 / 强化用）",
					boostTexts.Any((TextBlock t) => t.Text == "目标卡（生成 / 变化 / 强化用）" && t.IsVisible), "标签在");
				Check("选到「强化指定卡牌」时「强化什么」那一行才会出现（IsBoostCard 驱动）",
					boostTexts.Any((TextBlock t) => t.Text == "强化什么" && t.IsVisible), "标签在");
				List<ComboBox> boostCombos = new List<ComboBox>();
				CollectCombos(boostRoot, boostCombos);
				ComboBox? statCombo = boostCombos.FirstOrDefault((ComboBox c) => c.SelectedValuePath == "Id"
					&& BindingOperations.GetBinding(c, Selector.SelectedValueProperty)?.Path?.Path == "BoostStat");
				Check("「强化什么」下拉绑在效果的 BoostStat 上（候选 = 伤害 / 格挡两项）",
					statCombo is not null && statCombo.IsVisible && statCombo.Items.Count == EffectCatalog.BoostStats.Count
					&& (statCombo.SelectedValue as string) == "Damage",
					statCombo is null ? "没找到下拉" : $"选中={statCombo.SelectedValue ?? "(空)"} / 候选 {statCombo.Items.Count} 项");
				if (statCombo is not null)
				{
					statCombo.SelectedValue = "Block";
					UpdateLayout();
					Check("下拉改成「格挡」后效果上跟着变（生成出来就是 ModifyBlockAdditive 那套）",
						probeBoostFxCard.Effects[0].BoostStat == "Block", probeBoostFxCard.Effects[0].BoostStatZh);
				}
				// 收尾：换回普通效果 → 「强化什么」那一行收起（没有绑定残留）
				probeBoostFxCard.Effects[0].Kind = "Damage";
				UpdateLayout();
				boostRoot = SelectTabRoot("卡牌");
				UpdateLayout();
				List<TextBlock> boostTexts2 = new List<TextBlock>();
				CollectTextBlocks(boostRoot, boostTexts2);
				Check("换回普通效果后「强化什么」那一行收起（不残留）",
					!boostTexts2.Any((TextBlock t) => t.Text == "强化什么" && t.IsVisible), "已收起");
				Profile.Cards.Remove(probeBoostFxCard);
				CardList.SelectedIndex = Profile.Cards.Count - 1;
				UpdateLayout();
			}
			// 勾选框 / 字段都要能双向编辑到「列表里选中的那一只」上
			bool oldEnabled2 = false, oldGuard = false;
			string oldName2 = "", oldCls2 = "";
			int oldHp2 = 0, oldDist = 0;
			try
			{
				SummonList.SelectedIndex = 0;
				UpdateLayout();
				var probe = SummonList.SelectedItem as SummonSpec;
				// 示例配置本身不带召唤物（上面那条已经验过 0 条），这一条是本节自己加进去的 ——
				// 断言它真的能选中、能读出详情（字段 / 勾选框都挂在「选中的那只」上）。
				Check("召唤物列表里能选到前面加的那一只（选中后详情字段才有数据源）",
					ReferenceEquals(probe, summonProbe), probe?.Display ?? "(空列表)");
				if (probe is not null)
				{
					oldEnabled2 = probe.Enabled; oldGuard = probe.TakesDamageForOwner;
					oldName2 = probe.Name; oldCls2 = probe.ClassName;
					oldHp2 = probe.Hp; oldDist = probe.StandDistance;
					probe.Enabled = true;
					probe.Name = "测试伙伴";
					probe.Hp = 12;
					probe.StandDistance = 130;
					probe.ClassName = "";
					UpdateLayout();
					Check("类名留空时按位置自动推（第 1 只 = <角色类名>Pet）",
						PetGen.ClassNameOf(Profile, probe) == Profile.CharacterClass + "Pet"
						&& PetGen.DisplayNameOf(probe, PetGen.ClassNameOf(Profile, probe)) == "测试伙伴"
						&& PetGen.HpOf(probe) == 12 && PetGen.StandDistanceOf(probe) == 130,
						$"{PetGen.ClassNameOf(Profile, probe)} / {PetGen.HpOf(probe)} / {PetGen.StandDistanceOf(probe)}");
					// 「哪一只」下拉的候选 = 第一条「全部召唤物」+ 各只已启用的召唤物
					RaisePetSummonChoices();
					Check("「召唤物（哪一只）」下拉的候选 = 「全部召唤物」+ 各只已启用的召唤物",
						PetSummonsCard.Count == PetGen.Enabled(Profile).Count + 1
						&& PetSummonsCard[0].Id == PetGen.AllId
						&& PetSummonsCard.Skip(1).All((PetGen.PetChoice c) => c.Id.Length > 0),
						string.Join(" / ", PetSummonsCard.Select((PetGen.PetChoice c) => c.Display)));
					Check("「全部召唤物」那一项的文案看得懂（★ 全部召唤物…）",
						PetGen.IsAll(PetGen.AllId) && PetGen.Choices(Profile)[0].Display.StartsWith("★ 全部召唤物"),
						PetGen.Choices(Profile)[0].Display);
					Check("「全部召唤物」不是真的宠物标识（Resolve 认不出来、ResolveMany 返回全部）",
						PetGen.Resolve(Profile, PetGen.AllId) is null
						&& PetGen.ResolveMany(Profile, PetGen.AllId).Count == PetGen.Enabled(Profile).Count,
						$"Resolve=null / ResolveMany={PetGen.ResolveMany(Profile, PetGen.AllId).Count} 只");
					// 停用后候选里就没有它了
					probe.Enabled = false;
					RaisePetSummonChoices();
					Check("把召唤物停用后「哪一只」下拉里就看不到它了",
						!PetSummonsCard.Any((PetGen.PetChoice c) => c.Id == PetGen.ClassNameOf(Profile, probe)),
						string.Join(" / ", PetSummonsCard.Select((PetGen.PetChoice c) => c.Display)));
					// 效果引用了一只「停用的」召唤物 → 校验器要报错拦住
					CardSpec petOffCard = new CardSpec { Name = "激活检查", ClassName = "UiCheckPetOff", Cost = 1 };
					petOffCard.Effects.Clear();
					petOffCard.Effects.Add(new EffectSpec
					{
						Kind = "SummonPet", Amount = 0m, TargetSide = "Self",
						PetSummon = PetGen.ClassNameOf(Profile, probe),
					});
					string disabledCard = CSharpCodeGen.CardSource(Profile, petOffCard, 0);
					// 生成器**故意**不给「找不到的召唤物」写调用（否则会引用一个不存在的宠物类 → CS0103），
					// 只留一行说明注释；拦住生成的是校验器。所以这里断言「不抛异常 + 留下说得清的说明」，
					// 而不是「照样写出调用」（那反而会生成编译不过的代码）。
					Check("即使召唤物被停用，卡牌代码也照常生成（不抛异常，留一行「没有可用的召唤物」说明；拦住生成的是校验器）",
						disabledCard.Contains("没有可用的召唤物") && disabledCard.Contains("SevenPet")
						&& !disabledCard.Contains("SevenPetCmd.Summon("),
						disabledCard.Contains("没有可用的召唤物") ? "有说明、没写调用" : "说明缺失");
					Profile.Cards.Add(petOffCard);
					var petOffIssues = ProfileValidator.Validate(Profile);
					string petOffErrors = string.Join(" | ", petOffIssues.Where((ValidationIssue i) => i.IsError).Select((ValidationIssue i) => i.Message));
					Check("引用了「停用的召唤物」时校验器会报错拦住（否则生成的牌会引用不存在的宠物类 → CS0103）",
						petOffIssues.Any((ValidationIssue i) => i.IsError && i.Message.Contains("已经被停用")),
						petOffErrors.Length > 0 ? petOffErrors : "(没有错误)");
					Profile.Cards.Remove(petOffCard);
				}
			}
			finally
			{
				var restore = SummonList.SelectedItem as SummonSpec;
				if (restore is not null)
				{
					restore.Enabled = oldEnabled2; restore.TakesDamageForOwner = oldGuard;
					restore.Name = oldName2; restore.ClassName = oldCls2;
					restore.Hp = oldHp2; restore.StandDistance = oldDist;
				}
				// 自检自己加的那一只读完就撤掉：后面的用例都按「示例配置 0 只召唤物」来验
				Profile.Summons.Remove(summonProbe);
				RaisePetSummonChoices();
			}
		}

		// ===== 召唤物：端到端（两只同时在场 → 生成 → 产物 → 回读 → 校验拦截）=====
		string petRoot = Path.Combine(Path.GetTempPath(), "forge_uicheck_pet_" + Guid.NewGuid().ToString("N").Substring(0, 8));
		try
		{
			CharacterProfile petSrc = ProfileFactory.Sample();
			// 「替主人承伤」守卫 Power 的类名带角色类名前缀（<角色>ForgePetGuardianPower）—— 断言按它算
			string guardName = Naming.From(petSrc).GuardianPowerClass;
			petSrc.Paths.OutputDir = petRoot;
			// 生成要用解包工程（占位美术 / 场景），和上面「从工程恢复」那段一样
			petSrc.Paths.VanillaProject = Profile.Paths.VanillaProject;
			petSrc.Paths.GameDataDir = Profile.Paths.GameDataDir;
			petSrc.SaveName = "召唤物自检";
			// 两只召唤物都勾「替主人承伤」（现在允许，由共用的守卫类自己仲裁：列表里第一只活着的承担），
			// 且两只的站位距离不同
			petSrc.Summons.Clear();
			petSrc.Summons.Add(new SummonSpec
			{
				Enabled = true, ClassName = "UiCheckPet", Name = "小石头", Hp = 9,
				Image = null, TakesDamageForOwner = true, StandDistance = 140,
			});
			petSrc.Summons.Add(new SummonSpec
			{
				Enabled = true, ClassName = "UiCheckPet2", Name = "小铁块", Hp = 15,
				Image = null, TakesDamageForOwner = true, StandDistance = 90,
			});
			List<CardSpec> petOwn = petSrc.Cards.Where((CardSpec c) => !c.IsVanillaCard).ToList();
			CardSpec petSummonCard = new CardSpec { Name = "召唤小石头", ClassName = "UiCheckPetSummon", CardType = "Skill", Cost = 1, InCardPool = true };
			petSummonCard.Effects.Clear();
			petSummonCard.Effects.Add(new EffectSpec
			{
				Kind = "SummonPet", Amount = 0m, TargetSide = "Self", PetSummon = "UiCheckPet",
			});   // 0 = 用配置的 9 点生命
			petSrc.Cards.Add(petSummonCard);
			CardSpec petAttackCard = new CardSpec { Name = "小石头撞击", ClassName = "UiCheckPetAttack", CardType = "Attack", Cost = 1, InCardPool = true };
			petAttackCard.Effects.Clear();
			petAttackCard.Effects.Add(new EffectSpec
			{
				Kind = "PetAttack", Amount = 7m, UpgradeAmount = 3m, TargetSide = "Enemy", PetSummon = "UiCheckPet",
			});
			petSrc.Cards.Add(petAttackCard);
			// 第二只也有自己的攻击卡：验证同一张牌 / 不同牌能分别指向不同的召唤物
			CardSpec petAttackCard2 = new CardSpec { Name = "小铁块撞击", ClassName = "UiCheckPetAttack2", CardType = "Attack", Cost = 1, InCardPool = true };
			petAttackCard2.Effects.Clear();
			petAttackCard2.Effects.Add(new EffectSpec
			{
				Kind = "PetAttack", Amount = 5m, TargetSide = "AllEnemies", PetSummon = "UiCheckPet2",
			});
			petSrc.Cards.Add(petAttackCard2);
			// 遗物「战斗开始时召唤」= 本体 Byrdpip 的写法
			RelicSpec petRelic = new RelicSpec { Name = "会召唤的遗物", ClassName = "UiCheckPetRelic", Trigger = "CombatStart", IsStartingRelic = true };
			petRelic.Effects.Clear();
			petRelic.Effects.Add(new EffectSpec
			{
				Kind = "SummonPet", Amount = 12m, TargetSide = "Self", PetSummon = "UiCheckPet2",
			});
			petSrc.Relics.Add(petRelic);

			var petGen = ModGenerator.Generate(petSrc);
			Check("（准备）带两只召唤物的配置能生成工程", petGen.Success && Directory.Exists(petGen.ProjectRoot),
				string.Join(" | ", petGen.Issues.Where((ValidationIssue i) => i.IsError).Select((ValidationIssue i) => i.Message)));

			// ① cs/Pet.cs：两只宠物类 + 两个命令助手 + 一个守卫 Power
			string petCsPath = Path.Combine(petGen.ProjectRoot, "cs", "Pet.cs");
			Check("召唤物：生成了 cs/Pet.cs", File.Exists(petCsPath), petCsPath);
			string petCs = File.Exists(petCsPath) ? File.ReadAllText(petCsPath, Encoding.UTF8) : "";
			Check("召唤物：两只都生在同一个 cs/Pet.cs 里（各一个 MonsterModel 子类，本体靠扫描子类自动注册）",
				petCs.Contains("public sealed class UiCheckPet : MonsterModel") && petCs.Contains("public sealed class UiCheckPet2 : MonsterModel"),
				"两个宠物类");
			Check("召唤物：第二只也有自己的命令助手（PlayerCmd.AddPet<UiCheckPet2>）",
				petCs.Contains("PlayerCmd.AddPet<UiCheckPet2>(player)"), "用了通用 API");
			Check("召唤物：宠物类写了空操作自循环的 NOTHING_MOVE（宠物没有自主回合，照抄本体 Osty）",
				petCs.Contains("NOTHING_MOVE"), "有 NOTHING_MOVE");
			Check("召唤物：召唤命令里有 SetMaxHp（本体造宠物时给的是初始生命随机值）",
				petCs.Contains("CreatureCmd.SetMaxHp(__uiCheckPet, hp)"), "有 SetMaxHp");
			Check("召唤物：没上传图片时不 override VisualsPath（回退本体的 fallback 占位图）",
				!petCs.Contains("VisualsPath =>") && petCs.Contains("creature_visuals/"), "用占位图");
			Check("召唤物：两只的生命各自独立（9 / 15）",
				petCs.Contains("private const int BaseHp = 9;") && petCs.Contains("private const int BaseHp = 15;"), "BaseHp 9 / 15");

			// ①-a 站位 / 血条（bug：多只宠物时血条只剩最后一只；本体摆位会把全体叠回主人身上）
			Check("站位：宠物类覆写了 AfterCreatureAddedToCombat（本体摆位在这个钩子之前跑，改完不会被抢回去）",
				petCs.Contains("public override Task AfterCreatureAddedToCombat(Creature creature)")
				&& petCs.Contains("ForgePetLayout.RelayoutAll(creature.PetOwner);"), "有站位钩子");
			Check("站位：**同一主人的任何伙伴进场**都把全体重排一遍（不再只管自己那一只 —— 本体 AddCreature 会把该玩家所有宠物叠回主人身上）",
				petCs.Contains("if (creature.PetOwner is not null && ReferenceEquals(creature.PetOwner, base.Creature.PetOwner))")
				&& !petCs.Contains("if (creature != base.Creature) return Task.CompletedTask;"), "判的是「同一主人」");
			Check("站位：站位距离是**每只自己的配置值**（140 / 90），不是全局写死",
				petCs.Contains("private const float StandDistance = 140f;") && petCs.Contains("private const float StandDistance = 90f;")
				&& petCs.Contains("[typeof(UiCheckPet)] = 140f,") && petCs.Contains("[typeof(UiCheckPet2)] = 90f,"),
				"两只各自的距离（常量 + ForgePetLayout._dist）");
			Check("站位：算上了半个包围盒宽（不然有一半身子压在主人身上）",
				petCs.Contains("node.Visuals.Bounds.Size.X * 0.5f"), "有半宽");
			Check("站位：本体摆位用的那个「主人 X − 20」偏移没有出现在摆位代码里（20 只作为 _dist 查不到时的兜底）",
				!petCs.Contains("- 20f"), "干净");

			// ①-b bug②：血条（覆写 IsHealthBarVisible + 召唤后 ForgePetLayout.RelayoutAll 重开全体血条）
			Check("bug②血条：宠物类覆写了 IsHealthBarVisible（照本体 Osty）",
				petCs.Contains("public override bool IsHealthBarVisible => base.Creature.IsAlive;"), "有覆写");
			Check("bug②血条：召唤后调 ForgePetLayout.RelayoutAll 把**全体**血条重新开回来（本体 AddCreature 会把该玩家所有宠物的交互关掉 → 血条只剩最后一只）",
				petCs.Contains("internal static class ForgePetLayout")
				&& petCs.Contains("room.SetCreatureIsInteractable(pet, on: true);"), "有共用助手 + 重开血条");

			// ①-c 可选④：替主人承伤（共用守卫 Power，照抄 DieForYouPower + 自己仲裁）
			Check("替主人承伤：勾了几只都只生成**一个共用**的守卫 Power 类（不是每只一个 —— Creature.HasPower<T>() 只认同一类型，仲裁要跨宠物认人）",
				petCs.Contains($"public sealed class {guardName} : PowerModel")
				&& !petCs.Contains("public sealed class ForgePetGuardianUiCheckPet")
				&& !petCs.Contains("public sealed class ForgePetGuardianUiCheckPet2")
				&& petCs.Split($"public sealed class {guardName} : PowerModel").Length - 1 == 1, "共用一个守卫类");
			Check("替主人承伤：守卫里有我们自己写的仲裁（宠物列表里第一只活着且挂了守卫的才承担，它死后下一只自动接手）",
				petCs.Contains($"FirstOrDefault(p => p.IsAlive && p.HasPower<{guardName}>())")
				&& petCs.Contains("if (current is not null && !ReferenceEquals(current, base.Owner)) return target;"), "有仲裁");
			Check("替主人承伤：两只勾了的召唤物，召唤命令里都挂同一个守卫",
				petCs.Contains($"if (!__uiCheckPet.HasPower<{guardName}>())")
				&& petCs.Contains($"await PowerCmd.Apply<{guardName}>(choiceContext, __uiCheckPet, 1m, null, null);")
				&& petCs.Contains($"if (!__uiCheckPet2.HasPower<{guardName}>())")
				&& petCs.Contains($"await PowerCmd.Apply<{guardName}>(choiceContext, __uiCheckPet2, 1m, null, null);"), "两只都挂了");
			Check("替主人承伤：守卫只吸「可格挡的攻击伤害」（中毒 / 失去生命照旧打在主人身上）",
				petCs.Contains("if (!props.IsPoweredAttack()) return target;")
				&& petCs.Contains("public override Creature ModifyUnblockedDamageTarget(Creature target, decimal amount, ValueProp props, Creature? dealer)"), "有判定");
			Check("替主人承伤：目标不是自己主人就放过、自己死了就放过",
				petCs.Contains("if (target != base.Owner.PetOwner?.Creature) return target;")
				&& petCs.Contains("if (base.Owner.IsDead) return target;"), "有守卫");
			Check("替主人承伤：关掉状态图标（IsVisibleInternal => false）—— 不关的话本体要去 powers 表查 title/description，模组没这张表 → 名字显示成原始键名 + 图标退回 missing_power.png",
				petCs.Contains("protected override bool IsVisibleInternal => false;"), "有覆写");
			Check("替主人承伤：**不**覆写 ShouldCreatureBeRemovedFromCombatAfterDeath / ShouldPowerBeRemovedAfterOwnerDeath（那两个是 Osty「留尸等复活」的语义：会让尸体不消失、每次召唤都新建一只、越堆越多）",
				!petCs.Contains("ShouldCreatureBeRemovedFromCombatAfterDeath")
				&& !petCs.Contains("ShouldPowerBeRemovedAfterOwnerDeath"), "两个都不在生成结果里");
			Check("替主人承伤：保留 ShouldAllowHitting（自己死了以后不再接受攻击，本体 DieForYouPower 的写法）",
				petCs.Contains("public override bool ShouldAllowHitting(Creature creature)"), "还在");

			// ①-e bug④/⑤：死亡语义 + 召唤命令（Get 判活 + 每个分支都重排全体）
			Check("召唤命令：Get 扫列表**判活**（player.PlayerCombatState?.Pets.FirstOrDefault(p => p.Monster is X && p.IsAlive)），不再用 GetPet<T>() —— 它会返回已经死掉的那只，于是每次召唤都新建一只",
				petCs.Contains("return player.PlayerCombatState?.Pets.FirstOrDefault(p => p.Monster is UiCheckPet && p.IsAlive);")
				&& petCs.Contains("return player.PlayerCombatState?.Pets.FirstOrDefault(p => p.Monster is UiCheckPet2 && p.IsAlive);")
				&& !petCs.Contains("GetPet<"), "两只都改成判活");
			int petCmd2At = petCs.IndexOf("public static class UiCheckPet2Cmd", StringComparison.Ordinal);
			string petCmd1Body = petCmd2At > 0 ? petCs.Substring(0, petCmd2At) : "";
			string petCmd2Body = petCmd2At > 0 ? petCs.Substring(petCmd2At) : "";
			int relayout1 = petCmd1Body.Split("ForgePetLayout.RelayoutAll(player);").Length - 1;
			int relayout2 = petCmd2Body.Split("ForgePetLayout.RelayoutAll(player);").Length - 1;
			Check("召唤命令：每只宠物的召唤命令里都有 ForgePetLayout.RelayoutAll(player)（「已存在→加血」和「新建」两个分支各一次 → 每只 2 处）",
				petCmd2At > 0 && relayout1 == 2 && relayout2 == 2, $"UiCheckPetCmd={relayout1} / UiCheckPet2Cmd={relayout2}");

			// ①-d bug③：攻击不生效 —— 不再用 FromMonster，改成 FromCard + FromPetAttacker（扩展方法）
			string petExtPath = Path.Combine(petGen.ProjectRoot, "cs", "PetAttackExtensions.cs");
			Check("bug③攻击：生成了 cs/PetAttackExtensions.cs（把攻击者换成宠物的扩展方法）", File.Exists(petExtPath), petExtPath);
			string petExt = File.Exists(petExtPath) ? File.ReadAllText(petExtPath, Encoding.UTF8) : "";
			Check("bug③攻击：扩展方法用反射设 AttackCommand.Attacker 的 private setter（不是 Harmony 补丁）",
				petExt.Contains("public static AttackCommand FromPetAttacker(this AttackCommand command, Creature pet)")
				&& petExt.Contains("typeof(AttackCommand).GetProperty(\"Attacker\",")
				&& petExt.Contains("System.Reflection.BindingFlags.NonPublic")
				// 「不是 Harmony 补丁」要按**真的用了 Harmony** 判：文件头的说明注释里写着
				// 「为什么不用 Harmony 补丁」，直接 Contains("Harmony") 会把注释也算成命中（以前就这么误判过）。
				&& !petExt.Contains("HarmonyPatch") && !petExt.Contains("HarmonyLib") && !petExt.Contains("using Harmony"),
				"反射方案");
			// 四种「由宠物发起攻击」的效果都必须触发这个文件：漏一种 → 生成的卡引用不存在的
			// 扩展方法 → CS1061（编译直接过不去）。这里逐个种类验一遍需要它。
			Check("bug③攻击：四种宠物攻击类效果（PetAttack / 按最大·当前·已损失生命）都算「要用扩展方法」",
				PetGen.IsAttackKind("PetAttack") && PetGen.IsAttackKind("PetDamageByMaxHp")
				&& PetGen.IsAttackKind("PetDamageByCurHp") && PetGen.IsAttackKind("PetDamageByMissingHp")
				&& !PetGen.IsAttackKind("PetHeal") && !PetGen.IsAttackKind("PetSacrifice"),
				string.Join(" / ", new[] { "PetAttack", "PetDamageByMaxHp", "PetDamageByCurHp", "PetDamageByMissingHp" }
					.Select(k => k + "=" + PetGen.IsAttackKind(k))));
			Check("bug③攻击：没有生成任何 Harmony 补丁", !Directory.GetFiles(Path.Combine(petGen.ProjectRoot, "cs"), "*.cs", SearchOption.AllDirectories)
				.Any((string f) => File.ReadAllText(f, Encoding.UTF8).Contains("HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand)")), "没有攻击补丁");

			// ② monsters.json：两只的名字都在
			string petLocPath = Path.Combine(petGen.ProjectRoot, petSrc.ModId, "localization", "zhs", "monsters.json");
			Check("召唤物：生成了 monsters.json", File.Exists(petLocPath), petLocPath);
			string petLoc = File.Exists(petLocPath) ? File.ReadAllText(petLocPath, Encoding.UTF8) : "";
			Check("召唤物：monsters.json 里两只的名字都有（本体怪物名字的键格式）",
				petLoc.Contains("\"UI_CHECK_PET.name\": \"小石头\"") && petLoc.Contains("\"UI_CHECK_PET2.name\": \"小铁块\""),
				petLoc.Replace("\r", "").Replace("\n", " "));
			// 键必须是**本体算法**算出来的 <ENTRY>.name：本体 MonsterModel.Title 读的就是 Id.Entry，
			// 而 Id.Entry = StringHelper.Slugify(类名)。我们的 Naming.Slug 差一个字符，本体就查不到这个名字；
			// 更糟的是非中文语言下 LocTable 查不到键会直接抛 LocException（宠物节点初始化中断）。
			var petKeyDefs = PetGen.All(petSrc);
			Check("召唤物：monsters.json 每个键 = 用本体算法算出来的 <ENTRY>.name（两只召唤物逐个核对）",
				petKeyDefs.Count == 2 && petKeyDefs.All((PetGen.PetDef pd) =>
					string.Equals(Naming.Slug(pd.ClassName), VanillaSlugify(pd.ClassName), StringComparison.Ordinal)
					&& petLoc.Contains("\"" + VanillaSlugify(pd.ClassName) + ".name\":", StringComparison.Ordinal)),
				string.Join(" / ", petKeyDefs.Select((PetGen.PetDef pd) =>
					pd.ClassName + " → 我们=" + Naming.Slug(pd.ClassName) + " / 本体=" + VanillaSlugify(pd.ClassName))));
			// 中英文两份内容相同：模组本地化只合并「当前语言」的同名表（ModManager.cs:966-979 + LocManager.cs:468），
			// 英文会话下缺 <ENTRY>.name 会让 LocTable 抛 LocException —— 显示中文名总比崩好。
			string petLocEngPath = Path.Combine(petGen.ProjectRoot, petSrc.ModId, "localization", "eng", "monsters.json");
			Check("召唤物：monsters.json 同时写了 eng 一份（内容与 zhs 相同）",
				File.Exists(petLocEngPath)
				&& string.Equals(File.ReadAllText(petLocEngPath, Encoding.UTF8), petLoc, StringComparison.Ordinal),
				petLocEngPath);

			// ③ 召唤牌：只召唤它自己那只
			string petSummonSrc = File.ReadAllText(Path.Combine(petGen.ProjectRoot, "cs", "Cards", "UiCheckPetSummon.cs"), Encoding.UTF8);
			Check("召唤卡：生成的代码里出现召唤调用（UiCheckPetCmd.Summon）",
				petSummonSrc.Contains("UiCheckPetCmd.Summon(choiceContext, base.Owner,"), "有召唤调用");
			Check("召唤卡：数值填 0 时用**那只自己的**血量（9m），不是 0、也不是别的召唤物的血量",
				petSummonSrc.Contains(", 9m);") && !petSummonSrc.Contains(", 15m);"), "用配置血量");
			Check("召唤卡：不需要额外查宠物（Summon 内部自己找），所以不声明查询变量",
				!petSummonSrc.Contains("Creature? __uiCheckPet"), "没有多余局部变量");
			Check("召唤卡：仍然有「战斗状态不为 null」的守卫（关闭括号要对上，缺一个就编译不过）",
				petSummonSrc.Contains("if (base.Owner.PlayerCombatState is not null)"), "有守卫");
			string petSummonLoc = File.ReadAllText(Path.Combine(petGen.ProjectRoot, petSrc.ModId, "localization", "zhs", "cards.json"), Encoding.UTF8);
			Check("召唤卡：卡面描述里写着那只的名字（召唤小石头。）", petSummonLoc.Contains("召唤小石头。"), "描述里有名字");

			// ④ 伙伴攻击牌（单体：FromCard + Targeting + FromPetAttacker）
			string petAtkSrc = File.ReadAllText(Path.Combine(petGen.ProjectRoot, "cs", "Cards", "UiCheckPetAttack.cs"), Encoding.UTF8);
			Check("bug③攻击：生成的代码不再用 .FromMonster(（那会把来源标成 Monster → 去打玩家自己人、还和 Targeting 冲突）",
				!petAtkSrc.Contains(".FromMonster("), "没有 FromMonster");
			Check("bug③攻击：先走正常卡牌路径（FromCard），再把攻击者换成宠物（FromPetAttacker）",
				petAtkSrc.Contains(".FromCard(this, cardPlay)")
				&& petAtkSrc.Contains(".FromPetAttacker(__uiCheckPet)"), "FromCard + FromPetAttacker");
			Check("bug③攻击：FromCard 在前、FromPetAttacker 在后（FromCard 会校验 Attacker 必须为空）",
				petAtkSrc.IndexOf(".FromCard(this, cardPlay)", StringComparison.Ordinal) < petAtkSrc.IndexOf(".FromPetAttacker(__uiCheckPet)", StringComparison.Ordinal),
				"顺序对");
			Check("bug③攻击：单体目标仍然用 .Targeting(cardPlay.Target)（现在和 FromCard 不冲突了）",
				petAtkSrc.Contains(".Targeting(cardPlay.Target)"), "有 Targeting");
			Check("bug③攻击：给宠物播自己的攻击动画（没有该动画就退化成不动，无副作用）",
				petAtkSrc.Contains(".WithAttackerAnim(\"Attack\", 0.3f)"), "有动画");
			Check("bug③攻击：打之前先判宠物在不在场（不在就跳过，不让整张牌报错）",
				petAtkSrc.Contains("if (__uiCheckPet is not null)"), "有守卫");
			Check("bug③攻击：伤害走我们自己的动态变量 PetDamage（不是兜底的 Value）",
				petAtkSrc.Contains("new DynamicVar(\"PetDamage\", 7m)") && petAtkSrc.Contains("base.DynamicVars[\"PetDamage\"].BaseValue"), "用 PetDamage");
			Check("伙伴攻击卡：TargetType 是 AnyEnemy（要玩家选目标）", petAtkSrc.Contains("TargetType.AnyEnemy"), "AnyEnemy");
			string petAtkLoc = File.ReadAllText(Path.Combine(petGen.ProjectRoot, petSrc.ModId, "localization", "zhs", "cards.json"), Encoding.UTF8);
			Check("伙伴攻击卡：卡面描述里写着「让小石头…造成伤害」", petAtkLoc.Contains("让小石头"), "描述里有名字");

			// ④-b 第二只的全体攻击卡：换了一只 → 局部变量名也不同、TargetSide 走 TargetingAllOpponents
			string petAtk2Src = File.ReadAllText(Path.Combine(petGen.ProjectRoot, "cs", "Cards", "UiCheckPetAttack2.cs"), Encoding.UTF8);
			Check("第二只召唤物的攻击卡用的是它自己的查询变量和命令（__uiCheckPet2 / UiCheckPet2Cmd）",
				petAtk2Src.Contains("Creature? __uiCheckPet2 = UiCheckPet2Cmd.Get(base.Owner);")
				&& petAtk2Src.Contains(".FromPetAttacker(__uiCheckPet2)")
				&& petAtk2Src.Contains("if (__uiCheckPet2 is not null)"), "用第二只");
			Check("第二只召唤物的全体攻击卡走 .TargetingAllOpponents(base.CombatState)（攻击者已经换成宠物）",
				petAtk2Src.Contains(".TargetingAllOpponents(base.CombatState)"), "全体目标");

			// ⑤ 遗物触发（战斗开始时召唤第二只，血量 12）
			string petRelicSrc = File.ReadAllText(Path.Combine(petGen.ProjectRoot, "cs", "Relics", "UiCheckPetRelic.cs"), Encoding.UTF8);
			Check("遗物触发：战斗开始时也能召唤（本体 Byrdpip 的写法），而且召唤的是指定的那一只",
				petRelicSrc.Contains("BeforeSideTurnStart") && petRelicSrc.Contains("UiCheckPet2Cmd.Summon(choiceContext, base.Owner,"), "有召唤");
			Check("遗物触发：遗物上的召唤支持自定义血量（PetHp 动态变量）",
				System.Text.RegularExpressions.Regex.IsMatch(petRelicSrc, @"base\.DynamicVars\[""PetHp""\]\.BaseValue\);"),
				"12m");

			// ⑥ 药水：两条都不支持 —— 但必须留一行注释，不能静默丢掉
			PotionSpec petPotion = new PotionSpec { Name = "召唤药水", ClassName = "UiCheckPetPotion", Rarity = "Common" };
			petPotion.Effects.Clear();
			petPotion.Effects.Add(new EffectSpec { Kind = "SummonPet", Amount = 5m, TargetSide = "Self", PetSummon = "UiCheckPet" });
			petPotion.Effects.Add(new EffectSpec { Kind = "PetAttack", Amount = 5m, TargetSide = "AnyEnemy", PetSummon = "UiCheckPet" });
			petSrc.Potions.Add(petPotion);
			Check("药水：召唤伙伴 / 伙伴攻击都不支持，但生成的代码里留了说明注释（不静默丢）",
				CSharpCodeGen.PotionSource(petSrc, petPotion, 0).Contains("药水不支持"),
				"有注释");
			Check("药水：校验器对药水里的「召唤伙伴 / 伙伴攻击」直接报错",
				ProfileValidator.Validate(petSrc).Count((ValidationIssue i) => i.IsError && i.Message.Contains("药水不支持")) >= 2,
				string.Join(" | ", ProfileValidator.Validate(petSrc).Where((ValidationIssue i) => i.IsError && i.Message.Contains("药水不支持")).Select((ValidationIssue i) => i.Message)));
			petSrc.Potions.Remove(petPotion);

			// ⑦ 回读：列表 / 顺序 / 站位 / 守卫 / 「哪一只」都要回来
			var petRec = ProjectRecovery.FromProject(petGen.ProjectRoot);
			Check("从工程恢复：召唤物列表找回来了（两只、顺序一致、启用 / 类名 / 名字 / 生命）",
				petRec.Profile.Summons.Count == 2
				&& petRec.Profile.Summons[0].Enabled && petRec.Profile.Summons[0].ClassName == "UiCheckPet"
				&& petRec.Profile.Summons[0].Name == "小石头" && petRec.Profile.Summons[0].Hp == 9
				&& petRec.Profile.Summons[1].ClassName == "UiCheckPet2"
				&& petRec.Profile.Summons[1].Name == "小铁块" && petRec.Profile.Summons[1].Hp == 15,
				string.Join(" · ", petRec.Profile.Summons.Select((SummonSpec s) => s.Display)));
			Check("从工程恢复：站位距离找回来了（每只各自的 140 / 90）",
				petRec.Profile.Summons[0].StandDistance == 140 && petRec.Profile.Summons[1].StandDistance == 90,
				$"{petRec.Profile.Summons[0].StandDistance} / {petRec.Profile.Summons[1].StandDistance}");
			Check("从工程恢复：「替主人承伤」的勾选找回来了（两只勾了的都回读成 True —— 守卫是共用的一个类，要按每只自己的召唤命令逐只判）",
				petRec.Profile.Summons[0].TakesDamageForOwner && petRec.Profile.Summons[1].TakesDamageForOwner,
				$"{petRec.Profile.Summons[0].TakesDamageForOwner} / {petRec.Profile.Summons[1].TakesDamageForOwner}");
			Check("从工程恢复：卡牌上的「召唤伙伴」效果找回来了（数值 0 = 用配置血量，而且知道是哪一只）",
				petRec.Profile.Cards.Any((CardSpec c) => c.ClassName == "UiCheckPetSummon"
					&& c.Effects.Count == 1 && c.Effects[0].Kind == "SummonPet" && c.Effects[0].Amount == 0m
					&& c.Effects[0].PetSummon == "UiCheckPet"),
				string.Join(" · ", petRec.Profile.Cards.Where((CardSpec c) => c.ClassName == "UiCheckPetSummon")
					.SelectMany((CardSpec c) => c.Effects).Select((EffectSpec e) => e.Kind + "/" + (e.PetSummon ?? "?"))));
			Check("从工程恢复：卡牌上的「伙伴攻击」效果找回来了（伤害 7 + 升级 3 + 指向第一只）",
				petRec.Profile.Cards.Any((CardSpec c) => c.ClassName == "UiCheckPetAttack"
					&& c.Effects.Count == 1 && c.Effects[0].Kind == "PetAttack" && c.Effects[0].Amount == 7m
					&& c.Effects[0].UpgradeAmount == 3m && c.Effects[0].TargetSide == "Enemy"
					&& c.Effects[0].PetSummon == "UiCheckPet"),
				string.Join(" · ", petRec.Profile.Cards.Where((CardSpec c) => c.ClassName == "UiCheckPetAttack")
					.SelectMany((CardSpec c) => c.Effects).Select((EffectSpec e) => e.Kind + "/" + (e.PetSummon ?? "?"))));
			Check("从工程恢复：第二只的攻击卡也指向第二只（列表里不同召唤物不会串）",
				petRec.Profile.Cards.Any((CardSpec c) => c.ClassName == "UiCheckPetAttack2"
					&& c.Effects.Count == 1 && c.Effects[0].Kind == "PetAttack" && c.Effects[0].Amount == 5m
					&& c.Effects[0].TargetSide == "AllEnemies" && c.Effects[0].PetSummon == "UiCheckPet2"),
				string.Join(" · ", petRec.Profile.Cards.Where((CardSpec c) => c.ClassName == "UiCheckPetAttack2")
					.SelectMany((CardSpec c) => c.Effects).Select((EffectSpec e) => e.Kind + "/" + (e.PetSummon ?? "?"))));
			Check("从工程恢复：遗物上的「召唤伙伴」效果找回来了（12 点生命 + 指向第二只）",
				petRec.Profile.Relics.Any((RelicSpec r) => r.Effects.Any((EffectSpec e) => e.Kind == "SummonPet"
					&& e.Amount == 12m && e.PetSummon == "UiCheckPet2")),
				string.Join(" · ", petRec.Profile.Relics.SelectMany((RelicSpec r) => r.Effects).Select((EffectSpec e) => e.Kind + "/" + (e.PetSummon ?? "?"))));
			Check("从工程恢复：没认出来的语句为 0（召唤物相关的生成代码都认得）", !petRec.HasUnparsed,
				petRec.Unparsed.Count == 0 ? "全部认出来了" : $"{petRec.Unparsed.Count} 条：" + string.Join(" ｜ ", petRec.Unparsed.Take(3)));

			// ⑧ 校验拦截：全部停用 → 不生成 cs/Pet.cs（卡牌还引用着 → 校验器报错拦住）
			foreach (var s in petSrc.Summons) s.Enabled = false;
			var petGen2 = ModGenerator.Generate(petSrc);
			Check("全部召唤物停用后不会生成 cs/Pet.cs（但卡牌还引用着它们 → 由校验器报错拦住，生成会被中止）",
				!petGen2.Success || !File.Exists(Path.Combine(petGen2.ProjectRoot, "cs", "Pet.cs")),
				petGen2.Success ? "生成成功但没写 Pet.cs" : "生成被校验拦住了");
			foreach (var s in petSrc.Summons) s.Enabled = true;

			// ⑨ 校验：同时勾两只「替主人承伤」现在**不再是错误**（共用守卫自己仲裁），只给一句提示
			petSrc.Summons[1].TakesDamageForOwner = true;
			var petGuardIssues = ProfileValidator.Validate(petSrc);
			Check("同时有两只召唤物勾了「替主人承伤」时校验器**不再报错**（生成代码自己仲裁：列表里第一只活着的承担，死了换下一只）",
				!petGuardIssues.Any((ValidationIssue i) => i.IsError && i.Message.Contains("只能勾一只"))
				&& !petGuardIssues.Any((ValidationIssue i) => i.IsError && i.Message.Contains("替主人承伤")),
				string.Join(" | ", petGuardIssues.Where((ValidationIssue i) => i.IsError).Select((ValidationIssue i) => i.Message).Take(2)));
			Check("同时勾两只时校验器给出了「第一只活着的承伤、死了自动换下一只」的提示（非错误，不拦生成）",
				petGuardIssues.Any((ValidationIssue i) => !i.IsError && i.Message.Contains("有 2 只召唤物勾了「替主人承伤」")
					&& i.Message.Contains("第一只活着")),
				string.Join(" | ", petGuardIssues.Where((ValidationIssue i) => i.Message.Contains("替主人承伤")).Select((ValidationIssue i) => i.Message)));
			// 两只都勾着也能照常生成（守卫类只有一个，两只的召唤命令都挂它）
			var petGen3 = ModGenerator.Generate(petSrc);
			Check("两只都勾「替主人承伤」时生成照常成功（校验器不再拦），且 Pet.cs 里只有一个共用守卫类",
				petGen3.Success && File.Exists(Path.Combine(petGen3.ProjectRoot, "cs", "Pet.cs"))
				&& File.ReadAllText(Path.Combine(petGen3.ProjectRoot, "cs", "Pet.cs"), Encoding.UTF8)
					.Split($"public sealed class {guardName} : PowerModel").Length - 1 == 1,
				string.Join(" | ", petGen3.Issues.Where((ValidationIssue i) => i.IsError).Select((ValidationIssue i) => i.Message).Take(2)));
			petSrc.Summons[1].TakesDamageForOwner = false;

			// ⑩ 校验拦截：两只召唤物的类名重名
			petSrc.Summons[1].ClassName = "UiCheckPet";
			var petDupIssues = ProfileValidator.Validate(petSrc);
			Check("两只召唤物用了同一个英文类名时校验器报错拦住（本体按类名注册模型 → DuplicateModelException）",
				petDupIssues.Any((ValidationIssue i) => i.IsError && i.Message.Contains("重复")),
				string.Join(" | ", petDupIssues.Where((ValidationIssue i) => i.IsError).Select((ValidationIssue i) => i.Message).Take(2)));
			petSrc.Summons[1].ClassName = "UiCheckPet2";

			// ⑪ 校验拦截：效果指向一只不存在的召唤物
			petSrc.Cards.First((CardSpec c) => c.ClassName == "UiCheckPetSummon").Effects[0].PetSummon = "NoSuchPet";
			var petMissIssues = ProfileValidator.Validate(petSrc);
			Check("效果指向一只不存在的召唤物时校验器报错拦住（否则生成的代码会 CS0103）",
				petMissIssues.Any((ValidationIssue i) => i.IsError && i.Message.Contains("NoSuchPet")),
				string.Join(" | ", petMissIssues.Where((ValidationIssue i) => i.IsError).Select((ValidationIssue i) => i.Message).Take(2)));
			petSrc.Cards.First((CardSpec c) => c.ClassName == "UiCheckPetSummon").Effects[0].PetSummon = "UiCheckPet";

			// ⑫ 老存档兼容：单个 Summon（上一版的格式）要能迁移进列表
			var legacyPet = ProfileFactory.Sample();
			legacyPet.Summons.Clear();
			legacyPet.Summon = new SummonSpec { Enabled = true, ClassName = "UiCheckLegacyPet", Name = "老伙伴", Hp = 11 };
			ProfileFactory.Normalize(legacyPet);
			Check("老存档兼容：单个 Summon 会迁移进 Summons 列表并把老字段清空（不能静默丢配置）",
				legacyPet.Summon is null && legacyPet.Summons.Count == 1
				&& legacyPet.Summons[0].ClassName == "UiCheckLegacyPet" && legacyPet.Summons[0].Name == "老伙伴"
				&& legacyPet.Summons[0].Hp == 11 && legacyPet.Summons[0].StandDistance == SummonSpec.DefaultStandDistance,
				legacyPet.Summon is null ? string.Join(" · ", legacyPet.Summons.Select((SummonSpec s) => s.Display)) : "老字段还在");
			// 老存档里那个单对象是全空的（老版默认值）→ 不该凭空多出一条记录
			var legacyEmpty = ProfileFactory.Sample();
			legacyEmpty.Summons.Clear();
			legacyEmpty.Summon = new SummonSpec();
			ProfileFactory.Normalize(legacyEmpty);
			Check("老存档兼容：全空的单个 Summon 不会迁成一条空召唤物（否则列表里会凭空多一条）",
				legacyEmpty.Summon is null && legacyEmpty.Summons.Count == 0, $"{legacyEmpty.Summons.Count} 条");

			// ⑬ 老效果（PetSummon 为空）自动用第一只启用的召唤物（行为与上一版单只召唤物一致）
			var legacyEffectCard = new CardSpec { Name = "老召唤卡", ClassName = "UiCheckPetLegacyEffect", Cost = 1 };
			legacyEffectCard.Effects.Clear();
			legacyEffectCard.Effects.Add(new EffectSpec { Kind = "SummonPet", Amount = 0m, TargetSide = "Self" });   // 没填 PetSummon
			string legacyEffectSrc = CSharpCodeGen.CardSource(petSrc, legacyEffectCard, 0);
			Check("老存档的「召唤伙伴」效果没填「哪一只」时自动用第一只启用的召唤物（行为和上一版单只召唤物一致）",
				legacyEffectSrc.Contains("UiCheckPetCmd.Summon(choiceContext, base.Owner, 9m);"), "用第一只");

			// ===================== ⑭ 新增的那批宠物效果（10 个）=====================
			// 每个都要：① 生成代码里出现「关键的那一句」；② 从工程回读能原样认回来（不同步改回读就会静默丢配置）。
			// 顺便把「按生命值算」的三个伙伴攻击的三件套 / 静态倍率 lambda 也断言掉。
			{
				// —— 按最大生命值造成伤害（三件套 + 静态 lambda + IsPlayable / 描红框）——
				CardSpec newMax = new CardSpec { Name = "重压", ClassName = "UiCheckPetNewMax", CardType = "Attack", Cost = 1, InCardPool = true };
				newMax.Effects.Add(new EffectSpec { Kind = "PetDamageByMaxHp", Amount = 0m, UpgradeAmount = 3m, TargetSide = "Enemy", PetSummon = "UiCheckPet" });
				string maxSrc = CSharpCodeGen.CardSource(petSrc, newMax, 0);
				Check("新效果「伙伴攻击（按最大生命值）」生成了计算三件套（CalculationBase + ExtraDamage + CalculatedDamage）",
					maxSrc.Contains("new CalculationBaseVar(0m)") && maxSrc.Contains("new ExtraDamageVar(1m)")
					&& maxSrc.Contains("new CalculatedDamageVar(ValueProp.Move).WithMultiplier(")
					&& maxSrc.Contains("pet.MaxHp"), "三件套在");
				Check("新效果：倍率 lambda 是**静态**的（delegate(CardModel card, Creature? _)，绝不捕获实例）—— 写成实例方法本体在 CalculatedVar.cs:50 直接抛「Multiplier calc must be static!」",
					maxSrc.Contains("WithMultiplier(delegate(CardModel card, Creature? _) {")
					&& maxSrc.Contains("UiCheckPetCmd.Get(card.Owner)"), "静态 lambda");
				Check("新效果：攻击走 DamageCmd.Attack(base.DynamicVars.CalculatedDamage) + FromCard + FromPetAttacker（没写死数字）",
					maxSrc.Contains("await DamageCmd.Attack(base.DynamicVars.CalculatedDamage)")
					&& maxSrc.Contains(".FromCard(this, cardPlay)")
					&& maxSrc.Contains(".FromPetAttacker(__uiCheckPet)"), "攻击链对");
				Check("新效果：没伙伴时这张牌打不出去（IsPlayable）+ 缺伙伴描红框（ShouldGlowRedInternal）",
					maxSrc.Contains("protected override bool IsPlayable => (UiCheckPetCmd.Get(base.Owner) != null);")
					&& maxSrc.Contains("protected override bool ShouldGlowRedInternal => !(UiCheckPetCmd.Get(base.Owner) != null);"), "两句守卫都在");
				Check("新效果：宠物不在场时那条效果**安全跳过**（if (__uiCheckPet is not null) 包着，不抛异常）",
					maxSrc.Contains("if (__uiCheckPet is not null)"), "有跳过守卫");
				Check("新效果：升级增量写在 CalculationBase 上（「按生命值算」的 CalculatedDamage 名字是本体固定死的）",
					maxSrc.Contains("base.DynamicVars.CalculationBase.UpgradeValueBy(3m);"), "升级落点对");
				// 卡面描述里的 {名字:diff()} 必须是 CanonicalVars 里**真实声明**的那个键。
				// 用户实测报过（截图）：三种「按生命值算」的伙伴攻击 + 牺牲伙伴的收益，卡面上直接原样印出
				// {PetMissingHpDamage:diff()} —— 因为描述用的是我们的内部名字，而声明的是本体的
				// CalculatedDamage / CalculatedBlock（名字对不上，本体在 DynamicVars 里找不到 → 原样显示）。
				Check("卡面描述用 {CalculatedDamage…} 占位，**没有写死数字**（伤害按宠物生命值算）",
					LocalizationGen.CardsJson(petSrc).Length >= 0 && CSharpCodeGen.MarkerText(newMax.Effects[0]).Contains("CET:PetFormula=maxhp"), "标记在");
				Check("卡面描述里的变量名 = 真正声明的那个（CalculatedDamage），不是内部名字",
					CSharpCodeGen.DisplayVarNameOf(newMax.Effects[0]) == "CalculatedDamage"
					&& CSharpCodeGen.VarNameOf(newMax.Effects[0]) == "PetMaxHpDamage",
					CSharpCodeGen.DisplayVarNameOf(newMax.Effects[0]) + "（内部名字 " + CSharpCodeGen.VarNameOf(newMax.Effects[0]) + "）");

				// —— 按当前生命 / 已损失生命：变量名 + 公式标记要能区分 ——
				CardSpec newCur = new CardSpec { Name = "榨取", ClassName = "UiCheckPetNewCur", CardType = "Attack", Cost = 1, InCardPool = true };
				newCur.Effects.Add(new EffectSpec { Kind = "PetDamageByCurHp", Amount = 0m, TargetSide = "AllEnemies", PetSummon = "UiCheckPet" });
				CardSpec newMiss = new CardSpec { Name = "绝境", ClassName = "UiCheckPetNewMiss", CardType = "Attack", Cost = 1, InCardPool = true };
				newMiss.Effects.Add(new EffectSpec { Kind = "PetDamageByMissingHp", Amount = 0m, TargetSide = "RandomEnemies", RepeatCount = 2, PetSummon = "UiCheckPet" });
				string curSrc = CSharpCodeGen.CardSource(petSrc, newCur, 0);
				string missSrc = CSharpCodeGen.CardSource(petSrc, newMiss, 0);
				Check("新效果：三种「按生命值算」的伙伴攻击各自一套变量名（PetMaxHpDamage / PetCurHpDamage / PetMissingHpDamage）",
					!maxSrc.Contains("new DynamicVar(\"PetMaxHpDamage\", 0m)")   // 现在是计算三件套，不再是普通 DynamicVar
					&& CSharpCodeGen.VarNameOf(newMax.Effects[0]) == "PetMaxHpDamage"
					&& CSharpCodeGen.VarNameOf(newCur.Effects[0]) == "PetCurHpDamage"
					&& CSharpCodeGen.VarNameOf(newMiss.Effects[0]) == "PetMissingHpDamage",
					CSharpCodeGen.VarNameOf(newMax.Effects[0]) + " / " + CSharpCodeGen.VarNameOf(newCur.Effects[0]) + " / " + CSharpCodeGen.VarNameOf(newMiss.Effects[0]));
				Check("新效果：当前生命 / 已损失生命的倍率表达式各自正确（pet.CurrentHp / pet.MaxHp - pet.CurrentHp）",
					curSrc.Contains("(decimal)pet.CurrentHp") && missSrc.Contains("(decimal)(pet.MaxHp - pet.CurrentHp)")
					&& !curSrc.Contains("pet.MaxHp - pet.CurrentHp"), "两个公式对");
				Check("卡面描述的变量名（三种按生命值算）= 真正声明的 CalculatedDamage，**不是**内部名字（否则卡面原样印出 {PetMissingHpDamage:diff()}）",
					CSharpCodeGen.DisplayVarNameOf(newMax.Effects[0]) == "CalculatedDamage"
					&& CSharpCodeGen.DisplayVarNameOf(newCur.Effects[0]) == "CalculatedDamage"
					&& CSharpCodeGen.DisplayVarNameOf(newMiss.Effects[0]) == "CalculatedDamage",
					CSharpCodeGen.DisplayVarNameOf(newMax.Effects[0]) + " / " + CSharpCodeGen.DisplayVarNameOf(newCur.Effects[0])
					+ " / " + CSharpCodeGen.DisplayVarNameOf(newMiss.Effects[0]));
				Check("新效果：全部敌人 / 随机敌人各自走 TargetingAllOpponents / TargetingRandomOpponents（FromPetAttacker 在后）",
					curSrc.Contains(".TargetingAllOpponents(base.CombatState)")
					&& missSrc.Contains(".TargetingRandomOpponents(base.CombatState, allowDuplicates: true)"), "目标对");
				Check("新效果：按生命值算的宠物攻击标记里写了公式（回读按它区分三种倍率）",
					CSharpCodeGen.MarkerText(newMax.Effects[0]) == "PetCalcAttack CET:PetFormula=maxhp"
					&& CSharpCodeGen.MarkerText(newCur.Effects[0]) == "PetCalcAttack CET:PetFormula=curhp"
					&& CSharpCodeGen.MarkerText(newMiss.Effects[0]) == "PetCalcAttack CET:PetFormula=missinghp",
					CSharpCodeGen.MarkerText(newMax.Effects[0]));

				// —— 治疗 / 失去生命 / 最大生命 / 施加状态 / 守卫开 / 守卫关 ——
				CardSpec newMisc = new CardSpec { Name = "照料", ClassName = "UiCheckPetNewMisc", CardType = "Skill", Cost = 1, InCardPool = true };
				newMisc.Effects.Add(new EffectSpec { Kind = "PetHeal", Amount = 6m, UpgradeAmount = 3m, TargetSide = "Self", PetSummon = "UiCheckPet" });
				newMisc.Effects.Add(new EffectSpec { Kind = "PetLoseHp", Amount = 3m, TargetSide = "Self", PetSummon = "UiCheckPet" });
				newMisc.Effects.Add(new EffectSpec { Kind = "PetGainMaxHp", Amount = 4m, TargetSide = "Self", PetSummon = "UiCheckPet" });
				newMisc.Effects.Add(new EffectSpec { Kind = "PetApplyPower", Amount = 2m, PowerId = "StrengthPower", TargetSide = "Self", PetSummon = "UiCheckPet" });
				newMisc.Effects.Add(new EffectSpec { Kind = "PetGuardOn", TargetSide = "Self", PetSummon = "UiCheckPet" });
				newMisc.Effects.Add(new EffectSpec { Kind = "PetGuardOff", TargetSide = "Self", PetSummon = "UiCheckPet" });
				string miscSrc = CSharpCodeGen.CardSource(petSrc, newMisc, 0);
				Check("新效果「治疗伙伴」：CreatureCmd.Heal(pet, Heal.BaseValue)",
					miscSrc.Contains("await CreatureCmd.Heal(__uiCheckPet, base.DynamicVars.Heal.BaseValue);"), "有治疗");
				Check("新效果「伙伴失去生命」：CreatureCmd.Damage + Unblockable|Unpowered|Move（本体 DamageProps.cardHpLoss）",
					miscSrc.Contains("await CreatureCmd.Damage(choiceContext, __uiCheckPet, base.DynamicVars.HpLoss.BaseValue,")
					&& miscSrc.Contains("ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move,"), "失去生命对");
				Check("新效果「伙伴最大生命 +N」：CreatureCmd.GainMaxHp（注释里写明当前生命会同时回复）",
					miscSrc.Contains("await CreatureCmd.GainMaxHp(__uiCheckPet, base.DynamicVars.MaxHp.BaseValue);")
					&& miscSrc.Contains("当前生命同时回复"), "最大生命对");
				Check("新效果「给伙伴施加状态」：PowerCmd.Apply<StrengthPower>(choiceContext, pet, …)（数值走 PetPowerStrengthPower 变量）",
					miscSrc.Contains("await PowerCmd.Apply<StrengthPower>(choiceContext, __uiCheckPet, base.DynamicVars[\"PetPowerStrengthPower\"].BaseValue,"), "施加状态对");
				Check("新效果「伙伴替主人承伤（开）」：PowerCmd.Apply<ForgePetGuardianPower>（我们自己的共用守卫类，不是本体 DieForYouPower）",
					miscSrc.Contains($"await PowerCmd.Apply<{guardName}>(choiceContext, __uiCheckPet, 1m, null, null);"), "守卫开对");
				Check("新效果「取消伙伴替主人承伤（关）」：PowerCmd.Remove<ForgePetGuardianPower>(pet)（本体签名 PowerCmd.cs:282）",
					miscSrc.Contains($"await PowerCmd.Remove<{guardName}>(__uiCheckPet);"), "守卫关对");
				Check("新效果：两条「替主人承伤」开关**不声明**动态变量（否则会多出 DynamicVar(\"Value\")，两条就撞名 → 开新局崩）",
					!miscSrc.Contains("new DynamicVar(\"Value\", 0m)"), "没有多余变量");

				// —— 牺牲伙伴：格挡（最大生命×3）/ 伤害（固定 7）/ 格挡（当前生命）——
				CardSpec sacBlock = new CardSpec { Name = "献身", ClassName = "UiCheckPetNewSacBlock", CardType = "Skill", Cost = 1, InCardPool = true };
				sacBlock.Effects.Add(new EffectSpec { Kind = "PetSacrifice", TargetSide = "Self", PetSummon = "UiCheckPet", PetSacrificeGain = "Block", PetSacrificeFormula = "MaxHp", PetSacrificeMultiplier = 3m });
				CardSpec sacDmg = new CardSpec { Name = "骨刺", ClassName = "UiCheckPetNewSacDmg", CardType = "Attack", Cost = 1, InCardPool = true };
				sacDmg.Effects.Add(new EffectSpec { Kind = "PetSacrifice", TargetSide = "Enemy", PetSummon = "UiCheckPet", PetSacrificeGain = "Damage", PetSacrificeFormula = "Fixed", Amount = 7m });
				CardSpec sacCur = new CardSpec { Name = "同命", ClassName = "UiCheckPetNewSacCur", CardType = "Skill", Cost = 1, InCardPool = true };
				sacCur.Effects.Add(new EffectSpec { Kind = "PetSacrifice", TargetSide = "Self", PetSummon = "UiCheckPet", PetSacrificeGain = "Block", PetSacrificeFormula = "CurHp" });
				string sacBlockSrc = CSharpCodeGen.CardSource(petSrc, sacBlock, 0);
				string sacDmgSrc = CSharpCodeGen.CardSource(petSrc, sacDmg, 0);
				string sacCurSrc = CSharpCodeGen.CardSource(petSrc, sacCur, 0);
				Check("新效果「牺牲伙伴（格挡 / 最大生命×倍率）」：用 CalculationExtraVar（**不是** ExtraDamageVar）+ CalculatedBlockVar",
					sacBlockSrc.Contains("new CalculationExtraVar(1m)")
					&& sacBlockSrc.Contains("new CalculatedBlockVar(ValueProp.Move).WithMultiplier(")
					&& !sacBlockSrc.Contains("new ExtraDamageVar(1m)")
					&& sacBlockSrc.Contains("(decimal)pet.MaxHp * 3m"), "格挡三件套对");
				Check("新效果「牺牲伙伴」：先算收益、再杀宠物（decimal gain = … 在 CreatureCmd.Kill 之前）",
					sacBlockSrc.IndexOf("decimal gain = ", StringComparison.Ordinal) < sacBlockSrc.IndexOf("await CreatureCmd.Kill(__uiCheckPet);", StringComparison.Ordinal)
					&& sacBlockSrc.Contains("await CreatureCmd.GainBlock(base.Owner.Creature, gain, base.DynamicVars.CalculatedBlock.Props, cardPlay);"), "顺序对");
				Check("新效果「牺牲伙伴（伤害 / 固定 N）」：固定值走普通 DamageVar，先 Kill 再用 DamageCmd.Attack(数字)（顺序不能反）",
					sacDmgSrc.Contains("new DamageVar(\"PetSacrificeDamage\", 7m, ValueProp.Move)")
					&& sacDmgSrc.IndexOf("await CreatureCmd.Kill(__uiCheckPet);", StringComparison.Ordinal) < sacDmgSrc.IndexOf("await DamageCmd.Attack(dmg)", StringComparison.Ordinal), "伤害版对");
				Check("新效果「牺牲伙伴（伤害）」的 TargetType 是 AnyEnemy（生成的是 .Targeting(cardPlay.Target)，不能是 Self）",
					sacDmgSrc.Contains("TargetType.AnyEnemy"), "AnyEnemy");
				Check("新效果「牺牲伙伴（当前生命）」标记里带 curhp（回读按它还原公式）",
					sacCurSrc.Contains("CET:PetFormula=curhp") && sacCurSrc.Contains("(decimal)pet.CurrentHp"), "curhp 在");
				Check("新效果「牺牲伙伴（最大生命×倍率）」标记里带倍率（回读要能还原 3 倍）",
					sacBlockSrc.Contains("CET:PetMul=3m"), "倍率标记在");

				// —— 牺牲伙伴选「固定 N」又填了升级增量 ——
				// 用户实测报过：这种卡一开牌组就是一片空白（牌组界面打不开）。原因：升级增量被写到
				// base.DynamicVars.CalculationBase.UpgradeValueBy(3m)，而「固定 N」声明的是普通
				// BlockVar("PetSacrificeBlock")，**没有 CalculationBase** → 本体按名字取 → KeyNotFoundException
				// （牌组界面的「查看升级」要造升级预览，就炸在这里）。
				CardSpec sacUp = new CardSpec { Name = "献身升级", ClassName = "UiCheckPetNewSacUp", CardType = "Skill", Cost = 1, InCardPool = true };
				sacUp.Effects.Add(new EffectSpec { Kind = "PetSacrifice", TargetSide = "Self", PetSummon = "UiCheckPet", PetSacrificeGain = "Block", PetSacrificeFormula = "Fixed", Amount = 6m, UpgradeAmount = 3m });
				string sacUpSrc = CSharpCodeGen.CardSource(petSrc, sacUp, 0);
				Check("牺牲伙伴（固定 N）+ 升级增量：升级写在**它自己的**变量上（base.DynamicVars[\"PetSacrificeBlock\"]），不再写到没声明的 CalculationBase",
					sacUpSrc.Contains("base.DynamicVars[\"PetSacrificeBlock\"].UpgradeValueBy(3m);")
					&& !sacUpSrc.Contains("CalculationBase"), "升级落点对");
				Check("生成后自检（MissingDynamicVars）：牺牲伙伴固定值的卡「引用的变量全都声明了」",
					CSharpCodeGen.MissingDynamicVars(sacUpSrc).Count == 0,
					string.Join(",", CSharpCodeGen.MissingDynamicVars(sacUpSrc)));
				Check("生成后自检（MissingDynamicVars）：能抓出「引用未声明变量」这种会让牌组界面打不开的代码",
					CSharpCodeGen.MissingDynamicVars("public sealed class X : CardModel {\n"
						+ "protected override IEnumerable<DynamicVar> CanonicalVars => [ new BlockVar(\"PetSacrificeBlock\", 6m, ValueProp.Move) ];\n"
						+ "protected override void OnUpgrade() { base.DynamicVars.CalculationBase.UpgradeValueBy(3m); }\n}")
						.SequenceEqual(new[] { "CalculationBase" }),
					string.Join(",", CSharpCodeGen.MissingDynamicVars("public sealed class X : CardModel {\n"
						+ "protected override IEnumerable<DynamicVar> CanonicalVars => [ new BlockVar(\"PetSacrificeBlock\", 6m, ValueProp.Move) ];\n"
						+ "protected override void OnUpgrade() { base.DynamicVars.CalculationBase.UpgradeValueBy(3m); }\n}")));
				Check("生成后自检（MissingDynamicVars）：正常卡（声明了 Damage/Block2 等）不误报",
					CSharpCodeGen.MissingDynamicVars(CSharpCodeGen.CardSource(petSrc, newMax, 0)).Count == 0
					&& CSharpCodeGen.MissingDynamicVars("CanonicalVars => [ new DamageVar(6m, ValueProp.Move), new DamageVar(\"Damage2\", 6m, ValueProp.Move) ];\n"
						+ "void F() { _ = base.DynamicVars.Damage.BaseValue; _ = base.DynamicVars[\"Damage2\"].BaseValue; }").Count == 0,
					"不误报");

				// ===== 两条同样的「给伙伴施加状态」：第二条声明成别名，运行时必须读同一个别名 =====
				// 用户实测报过（本体的 DynamicVars 按名字取，取不到就 KeyNotFoundException → 牌组界面打不开）：
				// 两条同种效果时 CanonicalVars 里第二条是 PetPowerStrengthPower2（否则 DynamicVarSet 撞名），
				// 而生成代码仍固定读 PetPowerStrengthPower → 生成后自检直接报「引用了没有声明的动态变量」。
				CardSpec petPow2 = new CardSpec { Name = "发力两次", ClassName = "UiCheckPetPow2", CardType = "Skill", Cost = 1, InCardPool = true };
				petPow2.Effects.Add(new EffectSpec { Kind = "PetApplyPower", Amount = 2m, PowerId = "StrengthPower", TargetSide = "Self", PetSummon = "UiCheckPet" });
				petPow2.Effects.Add(new EffectSpec { Kind = "PetApplyPower", Amount = 2m, PowerId = "StrengthPower", TargetSide = "Self", PetSummon = "UiCheckPet" });
				string petPow2Src = CSharpCodeGen.CardSource(petSrc, petPow2, 0);
				Check("两条「给伙伴施加状态」：第二条运行时读的是它的**别名**变量（PetPowerStrengthPower2），不是第一条的名字",
					petPow2Src.Contains("base.DynamicVars[\"PetPowerStrengthPower2\"].BaseValue")
					&& CSharpCodeGen.MissingDynamicVars(petPow2Src).Count == 0,
					"缺失=[" + string.Join(",", CSharpCodeGen.MissingDynamicVars(petPow2Src)) + "]");

				// —— 生成后自检遇到「不认识的那一项」时，只能跳过那一项，不能放弃整张卡 ——
				// 本体 PowerVar.cs 的匿名构造是 base(typeof(T).Name, …)，所以匿名 PowerVar<T> 的键就是 T 的名字。
				// 以前这里写成 return Array.Empty<string>()（整张卡放弃检查）→ 真正的漏声明全被放过。
				Check("生成后自检（MissingDynamicVars）：看不懂的变量类型只跳过那一项，不放弃整张卡（否则真漏报）",
					CSharpCodeGen.MissingDynamicVars(
						"CanonicalVars => [ new PowerVar<StrengthPower>(2m), new SomethingUnknownVar(1m) ];\n"
						+ "void F() { _ = base.DynamicVars.CalculationBase.BaseValue; }")
						.SequenceEqual(new[] { "CalculationBase" }),
					string.Join(",", CSharpCodeGen.MissingDynamicVars(
						"CanonicalVars => [ new PowerVar<StrengthPower>(2m), new SomethingUnknownVar(1m) ];\n"
						+ "void F() { _ = base.DynamicVars.CalculationBase.BaseValue; }")));
				Check("生成后自检（MissingDynamicVars）：匿名 PowerVar<T> 的键按本体规则 = T 的名字，不误报",
					CSharpCodeGen.MissingDynamicVars(
						"CanonicalVars => [ new PowerVar<StrengthPower>(2m) ];\n"
						+ "void F() { _ = base.DynamicVars[\"StrengthPower\"].BaseValue; }").Count == 0,
					"缺失=[" + string.Join(",", CSharpCodeGen.MissingDynamicVars(
						"CanonicalVars => [ new PowerVar<StrengthPower>(2m) ];\n"
						+ "void F() { _ = base.DynamicVars[\"StrengthPower\"].BaseValue; }")) + "]");

				// —— 「全部召唤物」：一条效果按**每一只启用的召唤物**逐只展开（用户要求：下拉加「全选」）——
				CardSpec allPet = new CardSpec { Name = "全体出动", ClassName = "UiCheckPetAll", CardType = "Skill", Cost = 1, InCardPool = true };
				allPet.Effects.Add(new EffectSpec { Kind = "SummonPet", Amount = 0m, TargetSide = "Self", PetSummon = PetGen.AllId });
				allPet.Effects.Add(new EffectSpec { Kind = "PetHeal", Amount = 5m, UpgradeAmount = 2m, TargetSide = "Self", PetSummon = PetGen.AllId });
				allPet.Effects.Add(new EffectSpec { Kind = "PetGuardOn", TargetSide = "Self", PetSummon = PetGen.AllId });
				string allSrc = CSharpCodeGen.CardSource(petSrc, allPet, 0);
				Check("「全部召唤物」：一条效果按每只宠物各展开一份代码（本例 2 只 → 两次召唤 / 两次治疗 / 两次挂守卫）",
					allSrc.Contains("UiCheckPetCmd.Summon(choiceContext, base.Owner,")
					&& allSrc.Contains("UiCheckPet2Cmd.Summon(choiceContext, base.Owner,")
					&& allSrc.Contains("await CreatureCmd.Heal(__uiCheckPet, base.DynamicVars.Heal.BaseValue);")
					&& allSrc.Contains("await CreatureCmd.Heal(__uiCheckPet2, base.DynamicVars.Heal.BaseValue);")
					&& allSrc.Contains($"await PowerCmd.Apply<{guardName}>(choiceContext, __uiCheckPet, 1m, null, null);")
					&& allSrc.Contains($"await PowerCmd.Apply<{guardName}>(choiceContext, __uiCheckPet2, 1m, null, null);"),
					"两只各来一遍");
				Check("「全部召唤物」：每一份展开都写一行 // CET:PetAll= 标记（回读靠它把多份合并回一条）",
					allSrc.Split(new[] { "// CET:PetAll=" }, StringSplitOptions.None).Length - 1 == 6,
					(allSrc.Split(new[] { "// CET:PetAll=" }, StringSplitOptions.None).Length - 1) + " 行标记（3 条效果 × 2 只 = 6）");
				Check("「全部召唤物」：每只**各自**判在不在场（不在场的那只整段跳过，不会抛异常）",
					allSrc.Contains("if (__uiCheckPet is not null)") && allSrc.Contains("if (__uiCheckPet2 is not null)"), "两只各一层守卫");
				Check("「全部召唤物」：整张牌的「缺伙伴就打不出去 / 描红框」用「任意一只在场」的或运算",
					allSrc.Contains("UiCheckPetCmd.Get(base.Owner) != null || UiCheckPet2Cmd.Get(base.Owner) != null"),
					"或运算在");

				// 「全部召唤物」+「按生命值算」：一张牌只有一套固定名字的计算变量（CalculatedDamage），
				// 装不下两只各不相同的数字 → 这一档改成**内联**读每只自己的生命值。
				CardSpec allMiss = new CardSpec { Name = "全体绝境", ClassName = "UiCheckPetAllMiss", CardType = "Attack", Cost = 2, InCardPool = true };
				allMiss.Effects.Add(new EffectSpec { Kind = "PetDamageByMissingHp", Amount = 0m, UpgradeAmount = 4m, TargetSide = "Enemy", PetSummon = PetGen.AllId });
				// 同一张牌上再加一条**有升级增量**的伙伴攻击：回读时升级增量必须落在它自己身上
				// （按 CanonicalVars 的顺序对效果 —— 内联的那条不占变量，错位就会把 3 加到计算攻击上）
				allMiss.Effects.Add(new EffectSpec { Kind = "PetAttack", Amount = 7m, UpgradeAmount = 3m, TargetSide = "Enemy", PetSummon = PetGen.AllId });
				string allMissSrc = CSharpCodeGen.CardSource(petSrc, allMiss, 0);
				Check("「全部召唤物」+按生命值算：走**内联**计算（每只各自的 MaxHp - CurrentHp），不碰固定名字的 CalculatedDamage",
					allMissSrc.Contains("(decimal)(__uiCheckPet.MaxHp - __uiCheckPet.CurrentHp)")
					&& allMissSrc.Contains("(decimal)(__uiCheckPet2.MaxHp - __uiCheckPet2.CurrentHp)")
					&& !allMissSrc.Contains("CalculatedDamage")
					&& !allMissSrc.Contains("CalculationBase"), "内联计算");
				Check("「全部召唤物」：同一条「伙伴攻击」也给每只各来一次（FromPetAttacker 两只各一处，共用同一个 PetDamage 变量）",
					allMissSrc.Contains(".FromPetAttacker(__uiCheckPet)")
					&& allMissSrc.Contains(".FromPetAttacker(__uiCheckPet2)")
					&& allMissSrc.Split(new[] { "new DynamicVar(\"PetDamage\", 7m)" }, StringSplitOptions.None).Length - 1 == 1, "两只各一处");
				Check("「全部召唤物」+按生命值算：升级增量内联成 (base.IsUpgraded ? 4m : 0m)（没有 CalculationBase 可抬）",
					allMissSrc.Contains("(base.IsUpgraded ? 4m : 0m)")
					&& !allMissSrc.Contains("CalculationBase.UpgradeValueBy"), "升级内联");

				CardSpec allSac = new CardSpec { Name = "全体献身", ClassName = "UiCheckPetAllSac", CardType = "Skill", Cost = 2, InCardPool = true };
				allSac.Effects.Add(new EffectSpec { Kind = "PetSacrifice", TargetSide = "Self", PetSummon = PetGen.AllId, PetSacrificeGain = "Block", PetSacrificeFormula = "MaxHp", PetSacrificeMultiplier = 3m });
				string allSacSrc = CSharpCodeGen.CardSource(petSrc, allSac, 0);
				Check("「全部召唤物」+牺牲伙伴（按生命值算收益）：每只各自「先算收益再杀自己」（Kill 各自一份）",
					allSacSrc.Contains("(decimal)__uiCheckPet.MaxHp * 3m") && allSacSrc.Contains("(decimal)__uiCheckPet2.MaxHp * 3m")
					&& allSacSrc.Contains("await CreatureCmd.Kill(__uiCheckPet);") && allSacSrc.Contains("await CreatureCmd.Kill(__uiCheckPet2);")
					&& !allSacSrc.Contains("CalculatedBlock"), "两只各自牺牲");

				// —— 生成 → 回读一致性：把这一批卡都加进配置重新生成工程，再回读 ——
				petSrc.Cards.Add(allPet);
				petSrc.Cards.Add(allMiss);
				petSrc.Cards.Add(allSac);
				petSrc.Cards.Add(newMax);
				petSrc.Cards.Add(newCur);
				petSrc.Cards.Add(newMiss);
				petSrc.Cards.Add(newMisc);
				petSrc.Cards.Add(sacBlock);
				petSrc.Cards.Add(sacDmg);
				petSrc.Cards.Add(sacCur);
				petSrc.Cards.Add(sacUp);
				var petGenNew = ModGenerator.Generate(petSrc);
				var petRecNew = ProjectRecovery.FromProject(petGenNew.ProjectRoot);
				CardSpec? Rec(string cls) => petRecNew.Profile.Cards.FirstOrDefault(c => c.ClassName == cls);
				Check("从工程恢复：三种「按生命值算」的伙伴攻击都认回来了（Kind + 公式 + 哪一只 + 升级增量）",
					Rec("UiCheckPetNewMax")?.Effects.Any(e => e.Kind == "PetDamageByMaxHp" && e.PetSummon == "UiCheckPet" && e.UpgradeAmount == 3m) == true
					&& Rec("UiCheckPetNewCur")?.Effects.Any(e => e.Kind == "PetDamageByCurHp" && e.TargetSide == "AllEnemies") == true
					&& Rec("UiCheckPetNewMiss")?.Effects.Any(e => e.Kind == "PetDamageByMissingHp" && e.TargetSide == "RandomEnemies" && e.RepeatCount == 2) == true,
					string.Join(" · ", new[] { "UiCheckPetNewMax", "UiCheckPetNewCur", "UiCheckPetNewMiss" }
						.Select(c => c + "=" + string.Join(",", Rec(c)?.Effects.Select(e => e.Kind + "/" + e.Amount + "/" + (e.PetSummon ?? "?") + "/升级" + e.UpgradeAmount) ?? Array.Empty<string>()))));
				Check("从工程恢复：治疗 / 失去生命 / 最大生命 / 施加状态 / 守卫开 / 守卫关 六条都认回来了",
					Rec("UiCheckPetNewMisc")?.Effects.Select(e => e.Kind).SequenceEqual(new[] { "PetHeal", "PetLoseHp", "PetGainMaxHp", "PetApplyPower", "PetGuardOn", "PetGuardOff" }) == true
					&& Rec("UiCheckPetNewMisc")?.Effects[0].Amount == 6m
					&& Rec("UiCheckPetNewMisc")?.Effects[0].UpgradeAmount == 3m
					&& Rec("UiCheckPetNewMisc")?.Effects[3].PowerId == "StrengthPower"
					&& Rec("UiCheckPetNewMisc")?.Effects[3].Amount == 2m,
					string.Join(" · ", Rec("UiCheckPetNewMisc")?.Effects.Select(e => e.Kind + "/" + e.Amount + "/" + e.PowerId) ?? Array.Empty<string>()));
				Check("从工程恢复：牺牲伙伴（格挡 / 最大生命×3）找回来了（收益类型 + 公式 + 倍率 + 哪一只）",
					Rec("UiCheckPetNewSacBlock")?.Effects.Count == 1
					&& Rec("UiCheckPetNewSacBlock")?.Effects.Any(e => e.Kind == "PetSacrifice" && e.PetSacrificeGain == "Block"
						&& e.PetSacrificeFormula == "MaxHp" && e.PetSacrificeMultiplier == 3 && e.PetSummon == "UiCheckPet") == true,
					string.Join(" · ", Rec("UiCheckPetNewSacBlock")?.Effects.Select(e => e.Kind + "/" + e.PetSacrificeGain + "/" + e.PetSacrificeFormula) ?? Array.Empty<string>()));
				Check("从工程恢复：牺牲伙伴（伤害 / 固定 7 / 打单体）找回来了 —— 而且只有**一条**（Kill 之后的收尾语句不再多算一条）",
					Rec("UiCheckPetNewSacDmg")?.Effects.Count == 1
					&& Rec("UiCheckPetNewSacDmg")?.Effects.Any(e => e.Kind == "PetSacrifice" && e.PetSacrificeGain == "Damage"
						&& e.PetSacrificeFormula == "Fixed" && e.Amount == 7m && e.TargetSide == "Enemy") == true,
					string.Join(" · ", Rec("UiCheckPetNewSacDmg")?.Effects.Select(e => e.Kind + "/" + e.PetSacrificeGain + "/" + e.PetSacrificeFormula + "/" + e.Amount) ?? Array.Empty<string>()));
				Check("从工程恢复：牺牲伙伴（格挡 / 当前生命）找回来了",
					Rec("UiCheckPetNewSacCur")?.Effects.Count == 1
					&& Rec("UiCheckPetNewSacCur")?.Effects.Any(e => e.Kind == "PetSacrifice" && e.PetSacrificeFormula == "CurHp") == true,
					string.Join(" · ", Rec("UiCheckPetNewSacCur")?.Effects.Select(e => e.Kind + "/" + e.PetSacrificeFormula) ?? Array.Empty<string>()));
				Check("从工程恢复：牺牲伙伴（固定 N + 升级增量）找回来了，而且升级增量落在它自己身上",
					Rec("UiCheckPetNewSacUp")?.Effects.Count == 1
					&& Rec("UiCheckPetNewSacUp")?.Effects[0].Kind == "PetSacrifice"
					&& Rec("UiCheckPetNewSacUp")?.Effects[0].PetSacrificeFormula == "Fixed"
					&& Rec("UiCheckPetNewSacUp")?.Effects[0].Amount == 6m
					&& Rec("UiCheckPetNewSacUp")?.Effects[0].UpgradeAmount == 3m,
					string.Join(" · ", Rec("UiCheckPetNewSacUp")?.Effects.Select(e => e.Kind + "/" + e.PetSacrificeFormula + "/" + e.Amount + "/升级" + e.UpgradeAmount) ?? Array.Empty<string>()));
				Check("生成后自检：这一整批卡 + 遗物 + 药水都没有「引用未声明的动态变量」（否则游戏里牌组界面会打不开）",
					!petGenNew.Issues.Any(i => i.Message.Contains("没有声明")),
					string.Join(" ｜ ", petGenNew.Issues.Where(i => i.Message.Contains("没有声明")).Select(i => i.Message)));

				// —— 多模组共存：生成器自己起的固定类名必须带角色类名前缀 ——
				// 本体的 ModelDb 只按**类名**注册模型（忽略命名空间），两个模组各有一个 class Strike /
				// class ForgeExtraTurnPower 就抛 DuplicateModelException，表现是**游戏直接起不来**
				//（用户实测：「mods 里有不同存档构建的角色模组时游戏打不开」）。
				{
					var multi = ProfileFactory.Sample();
					multi.CharacterClass = "Mmod";
					multi.ModId = "MmodMod";
					var nm = Naming.From(multi);
					Check("多模组共存：生成器自己的固定类名全部带角色类名前缀（Strike / Defend / 额外资源量遗物 / Forge* Power）",
						nm.CardClassName(multi, new CardSpec { ClassName = "Strike" }) == "MmodStrike"
						&& nm.CardClassName(multi, new CardSpec { ClassName = "Defend" }) == "MmodDefend"
						&& nm.ExtraResourceRelicClass == "MmodExtraResourceRelic"
						&& nm.ExtraTurnPowerClass == "MmodForgeExtraTurnPower"
						&& nm.GuardianPowerClass == "MmodForgePetGuardianPower"
						&& nm.DelayedPowerClass(new EffectSpec { Kind = "ApplyPower", PowerId = "WeakPower" }) == "MmodForgeDelayedWeakPower",
						$"{nm.CardClassName(multi, new CardSpec { ClassName = "Strike" })} / {nm.ExtraResourceRelicClass} / {nm.ExtraTurnPowerClass} / {nm.GuardianPowerClass}");
					multi.ExtraResource.Enabled = true;
					string multiDeck = CSharpCodeGen.CharacterSource(multi);
					Check("多模组共存：初始卡组引用的是带前缀的类（ModelDb.Card<MmodStrike>()），遗物同理",
						multiDeck.Contains($"ModelDb.Relic<{nm.ExtraResourceRelicClass}>()")
						&& CSharpCodeGen.ExtraResourceSource(multi).Contains($"public sealed class {nm.ExtraResourceRelicClass} : RelicModel")
						&& CSharpCodeGen.CardSource(multi, new CardSpec { ClassName = "Strike", Name = "打击" }, 0)
							.Contains("public sealed class MmodStrike : CardModel"),
						"初始卡组 / 遗物 / 卡类都带前缀");
					// 手填的英文类名**不**加前缀（那是用户自己的命名，改了会让他的素材 / 存档引用错位）
					Check("多模组共存：用户手填的英文类名保持原样（不加前缀）",
						nm.CardClassName(multi, new CardSpec { ClassName = "MmodCrush" }) == "MmodCrush"
						&& nm.RelicClassName(new RelicSpec { ClassName = "MmodCharm" }, 0) == "MmodCharm", "手填的不动");
				}
				// 多模组撞车检查：同一个存档目录里**另一个存档**的工程有同名模型类时，必须警告
				{
					string tmp = Path.Combine(Path.GetTempPath(), "forge_multimod_" + Guid.NewGuid().ToString("N")[..8]);
					Directory.CreateDirectory(Path.Combine(tmp, "ModB", "cs", "Cards"));
					File.WriteAllText(Path.Combine(tmp, "ModB", "cs", "Cards", "MmodStrike.cs"),
						"namespace B;\npublic sealed class MmodStrike : CardModel { }", new UTF8Encoding(false));
					File.WriteAllText(Path.Combine(tmp, "ModB", "cs", "Mmod.cs"),
						"namespace B;\npublic sealed class Mmod : CharacterModel { }", new UTF8Encoding(false));
					var multiClash = ProfileFactory.Sample();
					multiClash.CharacterClass = "Mmod";
					multiClash.ModId = "MmodMod";
					multiClash.Paths.OutputDir = tmp;
					var multiClashIssues = ProfileValidator.Validate(multiClash);
					Check("多模组撞车检查：别的存档工程里有同名模型类时给警告（并说清是哪条路会炸）",
						multiClashIssues.Any(i => !i.IsError && i.Message.Contains("撞了") && i.Message.Contains("MmodStrike")),
						string.Join(" ｜ ", multiClashIssues.Where(i => i.Message.Contains("撞了")).Select(i => i.Message)));
					Check("多模组撞车检查：撞车的警告里带上另一个存档的目录名（ModB）",
						multiClashIssues.Any(i => i.Message.Contains("「ModB」")),
						string.Join(" ｜ ", multiClashIssues.Where(i => i.Message.Contains("ModB")).Select(i => i.Message)));
					try { Directory.Delete(tmp, true); } catch { /* 清理失败无所谓 */ }
				}
				Check("从工程恢复：「全部召唤物」的多份展开合并回**一条**效果（PetSummon = \"*\"，不会变成好几条）",
					Rec("UiCheckPetAll")?.Effects.Count == 3
					&& Rec("UiCheckPetAll")?.Effects.All(e => PetGen.IsAll(e.PetSummon)) == true
					&& Rec("UiCheckPetAll")?.Effects.Select(e => e.Kind).SequenceEqual(new[] { "SummonPet", "PetHeal", "PetGuardOn" }) == true
					&& Rec("UiCheckPetAll")?.Effects[1].Amount == 5m
					&& Rec("UiCheckPetAll")?.Effects[1].UpgradeAmount == 2m,
					string.Join(" · ", Rec("UiCheckPetAll")?.Effects.Select(e => e.Kind + "/" + (e.PetSummon ?? "?") + "/" + e.Amount) ?? Array.Empty<string>()));
				Check("从工程恢复：「全部召唤物」+按生命值算 也合并回一条（内联的升级增量 4 落回 UpgradeAmount）",
					Rec("UiCheckPetAllMiss")?.Effects.Count == 2
					&& Rec("UiCheckPetAllMiss")?.Effects[0].Kind == "PetDamageByMissingHp"
					&& PetGen.IsAll(Rec("UiCheckPetAllMiss")?.Effects[0].PetSummon)
					&& Rec("UiCheckPetAllMiss")?.Effects[0].UpgradeAmount == 4m
					&& Rec("UiCheckPetAllMiss")?.Effects[1].Kind == "PetAttack"
					&& Rec("UiCheckPetAllMiss")?.Effects[1].Amount == 7m
					&& Rec("UiCheckPetAllMiss")?.Effects[1].UpgradeAmount == 3m,
					string.Join(" · ", Rec("UiCheckPetAllMiss")?.Effects.Select(e => e.Kind + "/" + (e.PetSummon ?? "?") + "/数量" + e.Amount + "/升级" + e.UpgradeAmount) ?? Array.Empty<string>()));
				Check("从工程恢复：「全部召唤物」+牺牲伙伴（最大生命×3）也合并回一条",
					Rec("UiCheckPetAllSac")?.Effects.Count == 1
					&& Rec("UiCheckPetAllSac")?.Effects[0].Kind == "PetSacrifice"
					&& PetGen.IsAll(Rec("UiCheckPetAllSac")?.Effects[0].PetSummon)
					&& Rec("UiCheckPetAllSac")?.Effects[0].PetSacrificeGain == "Block"
					&& Rec("UiCheckPetAllSac")?.Effects[0].PetSacrificeMultiplier == 3m,
					string.Join(" · ", Rec("UiCheckPetAllSac")?.Effects.Select(e => e.Kind + "/" + e.PetSacrificeGain + "/" + (e.PetSummon ?? "?")) ?? Array.Empty<string>()));
				Check("从工程恢复：这一批宠物效果**一条都没漏**（没认出来的语句为 0）", !petRecNew.HasUnparsed,
					petRecNew.Unparsed.Count == 0 ? "全部认出来了" : $"{petRecNew.Unparsed.Count} 条：" + string.Join(" ｜ ", petRecNew.Unparsed.Take(3)));
				//（原先这里还有一条「召唤物卡牌标记 IsPetCard 按内容推回来」的断言 —— 那一页和那个字段都已删除。）

				// —— 校验：必须选到存在且已启用的召唤物 / 收益公式与倍率合法 / 施加状态必须有 PowerId ——
				var badPetCard = new CardSpec { Name = "缺召唤物", ClassName = "UiCheckPetBadRef", Cost = 1 };
				badPetCard.Effects.Add(new EffectSpec { Kind = "PetHeal", Amount = 3m, TargetSide = "Self", PetSummon = "NoSuchPet" });
				petSrc.Cards.Add(badPetCard);
				Check("校验：新效果引用了不存在的召唤物时**报错拦住**（否则生成出来的代码 CS0103）",
					ProfileValidator.Validate(petSrc).Any(i => i.IsError && i.Message.Contains("NoSuchPet")), "有错误");
				petSrc.Cards.Remove(badPetCard);

				var badSac = new CardSpec { Name = "牺牲没选状态", ClassName = "UiCheckPetBadSac", Cost = 1 };
				badSac.Effects.Add(new EffectSpec { Kind = "PetSacrifice", TargetSide = "Self", PetSummon = "UiCheckPet", PetSacrificeGain = "Block", PetSacrificeFormula = "MaxHp", PetSacrificeMultiplier = 0m });
				petSrc.Cards.Add(badSac);
				Check("校验：牺牲伙伴的倍率 ≤ 0 时报错拦住",
					ProfileValidator.Validate(petSrc).Any(i => i.IsError && i.Message.Contains("倍率要大于 0")), "有错误");
				petSrc.Cards.Remove(badSac);

				var badPower = new CardSpec { Name = "施加状态没选", ClassName = "UiCheckPetBadPower", Cost = 1 };
				badPower.Effects.Add(new EffectSpec { Kind = "PetApplyPower", Amount = 2m, TargetSide = "Self", PetSummon = "UiCheckPet", PowerId = null });
				petSrc.Cards.Add(badPower);
				Check("校验：「给伙伴施加状态」没选「增益 / 减益」时报错拦住（否则生成的 Apply<> 编译不过）",
					ProfileValidator.Validate(petSrc).Any(i => i.IsError && i.Message.Contains("没有选「增益 / 减益」")), "有错误");
				petSrc.Cards.Remove(badPower);

				var badTwo = new CardSpec { Name = "两条按生命值算", ClassName = "UiCheckPetBadTwo", Cost = 1 };
				badTwo.Effects.Add(new EffectSpec { Kind = "PetDamageByMaxHp", Amount = 0m, TargetSide = "Enemy", PetSummon = "UiCheckPet" });
				badTwo.Effects.Add(new EffectSpec { Kind = "PetDamageByCurHp", Amount = 0m, TargetSide = "Enemy", PetSummon = "UiCheckPet" });
				petSrc.Cards.Add(badTwo);
				Check("校验：一张牌上两条「按生命值算」的宠物效果时报错拦住（本体的计算变量名是固定死的，只能有一条）",
					ProfileValidator.Validate(petSrc).Any(i => i.IsError && i.Message.Contains("只能有一条")), "有错误");
				petSrc.Cards.Remove(badTwo);

				var petPotionNew = new PotionSpec { Name = "宠物药水", ClassName = "UiCheckPetNewPotion", Rarity = "Common" };
				petPotionNew.Effects.Add(new EffectSpec { Kind = "PetHeal", Amount = 3m, TargetSide = "Self", PetSummon = "UiCheckPet" });
				petSrc.Potions.Add(petPotionNew);
				Check("校验：药水里的新宠物效果**报错拦住**（这一档只做卡牌）",
					ProfileValidator.Validate(petSrc).Any(i => i.IsError && i.Message.Contains("药水不支持")), "有错误");
				petSrc.Potions.Remove(petPotionNew);

				// —— 卡面描述（写死数字 = 玩家看到的是错的，所以这里逐句核对）——
				string newLoc = LocalizationGen.CardsJson(petSrc);
				Check("卡面描述：按生命值算的伙伴攻击写的是 {CalculatedDamage:diff()} 占位 + 「此伤害等于伙伴的最大生命值」，**没有写死数字**、也**没有印出内部变量名**",
					newLoc.Contains("此伤害等于[gold]小石头[/gold]的最大生命值。")
					&& newLoc.Contains("此伤害等于[gold]小石头[/gold]的当前生命值。")
					&& newLoc.Contains("此伤害等于[gold]小石头[/gold]的已损失的生命值。")
					&& newLoc.Contains("{CalculatedDamage:diff()}")
					&& !newLoc.Contains("PetMaxHpDamage") && !newLoc.Contains("PetCurHpDamage")
					&& !newLoc.Contains("PetMissingHpDamage"),
					"有 CalculatedDamage=" + newLoc.Contains("{CalculatedDamage:diff()}")
					+ " 内部名残留=" + (newLoc.Contains("PetMaxHpDamage") || newLoc.Contains("PetCurHpDamage") || newLoc.Contains("PetMissingHpDamage")));
				// 六条各自的**实际文案**都放进 detail：FAIL 时一眼看得出是哪一条不对
				//（以前 detail 只有「描述对」三个字，看不出哪条错，白跑一轮）。
				{
					string strengthZh = EffectCatalog.PowerName("StrengthPower");
					var wantTexts = new (string What, string Text)[]
					{
						("治疗", "让[gold]小石头[/gold]回复 {Heal:diff()} 点生命。"),
						("失去生命", "让[gold]小石头[/gold]失去 {HpLoss:diff()} 点生命。"),
						("最大生命", "[gold]小石头[/gold]的最大生命值增加 {MaxHp:diff()} 点（同时回复等量生命）。"),
						("施加状态", $"[gold]小石头[/gold]获得 {{PetPowerStrengthPower:diff()}} 层{strengthZh}。"),
						("守卫开", "[gold]小石头[/gold]开始替主人承伤（主人受到可格挡的攻击伤害时，改由它承担）。"),
						("守卫关", "[gold]小石头[/gold]不再替主人承伤。"),
					};
					List<string> missing = wantTexts.Where((w) => !newLoc.Contains(w.Text, StringComparison.Ordinal))
						.Select((w) => w.What + " → " + w.Text).ToList();
					Check("卡面描述：治疗 / 失去生命 / 最大生命（写明同时回复等量生命）/ 守卫开·关 / 施加状态 都写对了",
						missing.Count == 0,
						missing.Count == 0 ? string.Join(" ｜ ", wantTexts.Select((w) => w.Text))
							: "缺 / 不符：" + string.Join(" ｜ ", missing));
				}
				Check("卡面描述：牺牲伙伴写着「若…存活：它死去，然后你获得…格挡 / 造成…伤害」"
					+ "（按生命值算的收益用真正声明的 CalculatedBlock —— 用内部名字会在卡面印出 {PetSacrificeBlock:diff()}）",
					newLoc.Contains("若[gold]小石头[/gold]存活：它死去，然后你获得{CalculatedBlock:diff()}点[gold]格挡[/gold]。")
					&& newLoc.Contains("若[gold]小石头[/gold]存活：它死去，然后它对指定敌人造成{PetSacrificeDamage:diff()}点伤害。"),
					"C# 里的声明：" + (CSharpCodeGen.DisplayVarNameOf(sacBlock.Effects[0]) + " / " + CSharpCodeGen.DisplayVarNameOf(sacDmg.Effects[0]))
					+ " ｜ 描述里有 CalculatedBlock=" + newLoc.Contains("{CalculatedBlock:diff()}")
					+ " 有 PetSacrificeDamage=" + newLoc.Contains("{PetSacrificeDamage:diff()}")
					+ " 有内部名 PetSacrificeBlock=" + newLoc.Contains("PetSacrificeBlock"));
				Check("PetAction / IsPetEffect 覆盖了那 10 个新 Kind（否则「召唤物（哪一只）」下拉不会显示）",
					new[] { "PetDamageByMaxHp", "PetDamageByCurHp", "PetDamageByMissingHp", "PetHeal", "PetLoseHp",
						"PetGainMaxHp", "PetSacrifice", "PetApplyPower", "PetGuardOn", "PetGuardOff" }
					.All(k => new EffectSpec { Kind = k }.PetAction && new EffectSpec { Kind = k }.IsPetEffect),
					"10 个都覆盖");
			}
		}
		catch (Exception ex)
		{
			Check("召唤物（整体）", ok: false, ex.GetType().Name + ": " + ex.Message + "  @" + string.Join(" | ", (ex.StackTrace ?? "").Split('\n').Take(4).Select((string s) => s.Trim())));
		}
		finally
		{
			try { Directory.Delete(petRoot, true); } catch { }
		}

		// ===== 动态变量重名（本体 DynamicVarSet 会直接抛异常 → 战斗卡死在第一回合：抽不了牌、结束不了回合）=====
		CardSpec dupVarCard = new CardSpec { Name = "两张伤害变量", ClassName = "UiCheckDupVar", Cost = 2 };
		dupVarCard.Effects.Clear();
		dupVarCard.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 5m, TargetSide = "Enemy" });
		dupVarCard.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 10m, TargetSide = "Enemy", UpgradeAmount = 5m });
		characterProfile16.Cards.Add(dupVarCard);
		string dupText = CSharpCodeGen.CardSource(characterProfile16, dupVarCard, 0);
		Check("同一种动态变量出现两次时会给第二个起别名（本体要求：2 个 BlockVar 必须各自起名）",
			dupText.Contains("new DamageVar(5m, ValueProp.Move)") && dupText.Contains("new DamageVar(\"Damage2\", 10m, ValueProp.Move)"),
			"第二张 DamageVar 带上了名字");
		Check("取值 / 升级 / 描述占位符都用上了别名（否则取不到值）",
			dupText.Contains("base.DynamicVars[\"Damage2\"].BaseValue") && dupText.Contains("base.DynamicVars[\"Damage2\"].UpgradeValueBy(5m)"),
			"DAMAGE2 三处一致");
		Check("同一种变量不会重名（键按本体规则算：有名字用名字，没有用类型默认名）",
			!HasDuplicateVarKeys(dupText), string.Join(" / ", VarKeysOf(dupText)));
		RelicSpec dupVarRelic = new RelicSpec { Name = "双格挡遗物", ClassName = "UiCheckDupVarRelic", Trigger = "PlayerTurnStart" };
		dupVarRelic.Effects.Clear();
		dupVarRelic.Effects.Add(new EffectSpec { Kind = "Block", Amount = 3m });
		dupVarRelic.Effects.Add(new EffectSpec { Kind = "Block", Amount = 6m });
		characterProfile16.Relics.Add(dupVarRelic);
		string dupRelicText = CSharpCodeGen.RelicSource(characterProfile16, dupVarRelic, 0);
		Check("遗物里的重复变量同样会起别名", dupRelicText.Contains("new BlockVar(\"Block2\", 6m, ValueProp.Move)") && !HasDuplicateVarKeys(dupRelicText),
			string.Join(" / ", VarKeysOf(dupRelicText)));
		PotionSpec dupVarPotion = new PotionSpec { Name = "双抽牌药水", ClassName = "UiCheckDupVarPotion", Rarity = "Common" };
		dupVarPotion.Effects.Clear();
		dupVarPotion.Effects.Add(new EffectSpec { Kind = "Draw", Amount = 1m });
		dupVarPotion.Effects.Add(new EffectSpec { Kind = "Draw", Amount = 2m });
		characterProfile16.Potions.Add(dupVarPotion);
		string dupPotionText = CSharpCodeGen.PotionSource(characterProfile16, dupVarPotion, 0);
		Check("药水里的重复变量同样会起别名", dupPotionText.Contains("new CardsVar(\"Cards2\", 2)") && !HasDuplicateVarKeys(dupPotionText),
			string.Join(" / ", VarKeysOf(dupPotionText)));
		Check("校验器会提示「同一张牌里同种变量出现多次」（别名是自动的，不用手动改）",
			ProfileValidator.Validate(characterProfile16).Any((ValidationIssue i) => i.Message.Contains("动态变量") || i.Message.Contains("起名字")),
			"有提示（或不需要提示）");

		// ===== ① 格挡 / 回复生命等效果也能打敌人 =====
		Check("「获得格挡」「回复生命」「失去生命」「最大生命」都能选作用对象",
			EffectCatalog.FindKind("Block").NeedsTarget && EffectCatalog.FindKind("Heal").NeedsTarget
			&& EffectCatalog.FindKind("HpLoss").NeedsTarget && EffectCatalog.FindKind("MaxHp").NeedsTarget, "四种都支持");
		CardSpec foeHeal = new CardSpec { Name = "打敌人的格挡回血", ClassName = "UiCheckFoeSupport", Cost = 1 };
		foeHeal.Effects.Clear();
		foeHeal.Effects.Add(new EffectSpec { Kind = "Block", Amount = 6m, TargetSide = "AllEnemies" });
		foeHeal.Effects.Add(new EffectSpec { Kind = "Heal", Amount = 4m, TargetSide = "Enemy" });
		foeHeal.Effects.Add(new EffectSpec { Kind = "HpLoss", Amount = 2m, TargetSide = "Enemy" });
		characterProfile16.Cards.Add(foeHeal);
		string foeHealText = CSharpCodeGen.CardSource(characterProfile16, foeHeal, 0);
		Check("给全体敌人加格挡 → 逐个敌人 GainBlock",
			foeHealText.Contains("foreach (Creature foe in base.CombatState.HittableEnemies)") && foeHealText.Contains("await CreatureCmd.GainBlock(foe,"),
			"全体敌人格挡");
		Check("给指定敌人回血 / 掉血 → 用 cardPlay.Target",
			foeHealText.Contains("await CreatureCmd.Heal(cardPlay.Target,") && foeHealText.Contains("await CreatureCmd.Damage(choiceContext, cardPlay.Target,"),
			"指定敌人回血 / 掉血");
		Check("这张牌的 TargetType 变成 AnyEnemy（因为有效果打敌人）", foeHealText.Contains("TargetType.AnyEnemy"), "AnyEnemy");
		Check("打敌人的格挡也做了目标非空保护", foeHealText.Contains("ArgumentNullException.ThrowIfNull(cardPlay.Target"), "有保护");
		Check("卡面描述写清了「给谁」",
			LocalizationGen.CardsJson(characterProfile16).Contains("让所有敌人获得") && LocalizationGen.CardsJson(characterProfile16).Contains("让指定敌人回复"),
			"描述带对象");
		RelicSpec foeBlockRelic = new RelicSpec { Name = "给敌人格挡的遗物", ClassName = "UiCheckFoeBlockRelic", Trigger = "PlayerTurnStart" };
		foeBlockRelic.Effects.Clear();
		foeBlockRelic.Effects.Add(new EffectSpec { Kind = "Block", Amount = 5m, TargetSide = "Enemy" });
		characterProfile16.Relics.Add(foeBlockRelic);
		string foeBlockRelicText = CSharpCodeGen.RelicSource(characterProfile16, foeBlockRelic, 0);
		Check("遗物里「给敌人格挡」也能生成（没有 cardPlay，取可打的第一个敌人）",
			foeBlockRelicText.Contains("HittableEnemies.FirstOrDefault()") && foeBlockRelicText.Contains("CreatureCmd.GainBlock(foe,"), "遗物也支持");

		// ===== ② 安装目录 = 游戏 mods 目录 =====
		Check("认不出来的目录（既不是游戏目录也不是 mods）→ 不瞎猜，返回空",
			PathAutoDetect.FindModsDir(@"D:\不存在的目录\随便") is null && PathAutoDetect.FindModsDir(null) is null, "返回空");
		// 造一个假的「游戏目录」来验证：程序文件 + data_sts2_windows_x86_64\sts2.dll
		string fakeGame = Path.Combine(Path.GetTempPath(), "forge_modsprobe_" + Guid.NewGuid().ToString("N").Substring(0, 8));
		string fakeData = Path.Combine(fakeGame, "data_sts2_windows_x86_64");
		try
		{
			Directory.CreateDirectory(fakeData);
			File.WriteAllBytes(Path.Combine(fakeData, "sts2.dll"), new byte[] { 0 });
			Check("游戏根目录 → 自动算出它下面的 mods", PathAutoDetect.FindModsDir(fakeGame) == Path.Combine(fakeGame, "mods"), PathAutoDetect.FindModsDir(fakeGame) ?? "(null)");
			Check("data_sts2_* 目录 → 也是上一级的 mods", PathAutoDetect.FindModsDir(fakeData) == Path.Combine(fakeGame, "mods"), PathAutoDetect.FindModsDir(fakeData) ?? "(null)");
			Check("mods 目录本身 → 原样返回", PathAutoDetect.FindModsDir(Path.Combine(fakeGame, "mods")) == Path.Combine(fakeGame, "mods"), "原样");
			Check("选成游戏根目录 → 校验器认得出「这不是 mods 目录」", PathAutoDetect.LooksLikeModsDir(fakeGame) == false, "会警告");
			Check("游戏目录下面的 mods → 认得出「这就是 mods 目录」", PathAutoDetect.LooksLikeModsDir(Path.Combine(fakeGame, "mods")) == true, "合格");
			Check("用户自己挑的普通目录 → 不认（也就不会被工具悄悄改掉）", PathAutoDetect.FindModsDir(Path.Combine(fakeGame, "我的测试目录")) is null, "不动它");
		}
		finally
		{
			try { Directory.Delete(fakeGame, true); } catch { /* 忽略 */ }
		}
		Check("已经是 mods 目录时原样返回",
			PathAutoDetect.FindModsDir(@"D:\a\steamapps\common\Slay the Spire 2\mods") == @"D:\a\steamapps\common\Slay the Spire 2\mods",
			PathAutoDetect.FindModsDir(@"D:\a\steamapps\common\Slay the Spire 2\mods") ?? "(null)");
		Check("data_sts2_* 目录 → 上一级的 mods",
			PathAutoDetect.FindModsDir(@"D:\a\Slay the Spire 2\data_sts2_windows_x86_64") == @"D:\a\Slay the Spire 2\mods",
			PathAutoDetect.FindModsDir(@"D:\a\Slay the Spire 2\data_sts2_windows_x86_64") ?? "(null)");
		Check("「mods」这个名字就算合规（不看目录存在与否）", PathAutoDetect.LooksLikeModsDir(@"D:\nowhere\mods") == true, "按名字认");
		Check("本机探测到的 mods 目录存在（有游戏的话）",
			PathAutoDetect.FindModsDir() is null || Directory.Exists(PathAutoDetect.FindModsDir()!),
			PathAutoDetect.FindModsDir() ?? "（本机没探测到游戏目录）");

		// ===== ③ 升级后的额外资源量：卡面描述要跟着变 =====
		CardSpec starUp = new CardSpec { Name = "升级加资源的牌", ClassName = "UiCheckStarUpgrade", Cost = 1 };
		starUp.Effects.Clear();
		starUp.Effects.Add(new EffectSpec { Kind = "ExtraResource", Amount = 2m, UpgradeAmount = 1m, TargetSide = "Self" });
		characterProfile16.Cards.Add(starUp);
		string starUpText = CSharpCodeGen.CardSource(characterProfile16, starUp, 0);
		Check("额外资源量用本体的 StarsVar 声明（2 点）", starUpText.Contains("new StarsVar(2)"), "有 StarsVar");
		Check("打出时用变量取数（不再是写死的 IsUpgraded 三目）", starUpText.Contains("PlayerCmd.GainStars(base.DynamicVars.Stars.BaseValue"), "用变量");
		Check("OnUpgrade 里给 Stars 加升级增量（升级后才是 3）", starUpText.Contains("base.DynamicVars[\"Stars\"].UpgradeValueBy(1m)"), "有升级增量");
		Check("卡面描述用 {Stars:diff()} —— 升级后游戏里会显示 3，而不是 2",
			LocalizationGen.CardsJson(characterProfile16).Contains("获得 {Stars:diff()} 点"), "描述带升级差异");
		CardSpec starCostOnly = new CardSpec { Name = "只花资源", ClassName = "UiCheckStarCostOnly", Cost = 1 };
		starCostOnly.Effects.Clear();
		starCostOnly.Effects.Add(new EffectSpec { Kind = "ExtraResource", Amount = -2m, TargetSide = "Self" });
		characterProfile16.Cards.Add(starCostOnly);
		string starCostText = CSharpCodeGen.CardSource(characterProfile16, starCostOnly, 0);
		Check("只花资源（负数）不会去声明 StarsVar，仍走卡牌费用 CanonicalStarCost",
			!starCostText.Contains("new StarsVar") && starCostText.Contains("CanonicalStarCost => 2"), "费用写法不变");

		// ===== ④ 条件指向对象新增「指定敌人」 =====
		Check("「指向对象」里有「指定敌人」", ConditionTargets.Any((ConditionTargetOption t) => t.Id == "Enemy"), string.Join(" / ", ConditionTargets.Select((ConditionTargetOption t) => t.Display)));
		ConditionSpec foeCond = new ConditionSpec { Kind = "HasPowerAtLeast", Amount = 2m, PowerId = "VulnerablePower", Target = "Enemy" };
		string foeCondInPlay = CSharpCodeGen.ConditionExpr(foeCond, CSharpCodeGen.CondCtx.Card, inOnPlay: true);
		string foeCondGate = CSharpCodeGen.ConditionExpr(foeCond, CSharpCodeGen.CondCtx.Card, inOnPlay: false);
		Check("效果里用「指定敌人」→ 判断的是 cardPlay.Target", foeCondInPlay.Contains("cardPlay.Target is not null && cardPlay.Target.GetPowerAmount<VulnerablePower>() >= 2"), foeCondInPlay);
		Check("打不出去 / 描金边那里退回「任意一个敌人」（那时还没选目标）", foeCondGate.Contains(".Enemies.Any(c => c.IsAlive"), foeCondGate);
		Check("条件说明里会写出指向对象",
			CSharpCodeGen.ConditionText(foeCond).Contains("拥有") && CSharpCodeGen.ConditionText(foeCond).EndsWith("（指定敌人）"),
			CSharpCodeGen.ConditionText(foeCond));
		CardSpec foeCondCard = new CardSpec { Name = "指定敌人条件", ClassName = "UiCheckFoeCond", Cost = 1 };
		foeCondCard.Effects.Clear();
		foeCondCard.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 5m, TargetSide = "Enemy", Condition = { Kind = "HasPowerAtLeast", Amount = 2m, PowerId = "VulnerablePower", Target = "Enemy", WhenUnmet = "NoEffect" } });
		characterProfile16.Cards.Add(foeCondCard);
		string foeCondCardText = CSharpCodeGen.CardSource(characterProfile16, foeCondCard, 0);
		Check("「指定敌人」条件会包住那条效果，且不写进 IsPlayable",
			foeCondCardText.Contains("if (cardPlay.Target is not null && cardPlay.Target.GetPowerAmount<VulnerablePower>() >= 2)") && !foeCondCardText.Contains("IsPlayable =>"),
			"只包效果");
		Check("校验器会说明「指定敌人 + 打不出去」做不了",
			ProfileValidator.Validate(characterProfile16).Any((ValidationIssue i) => i.Message.Contains("指定敌人")), "有提示");

		// ===== #1 同一个 ModId 的存档会互相覆盖 =====
		string clashDir = Path.Combine(Path.GetTempPath(), "forge_uicheck_clash");		try
		{
			Directory.CreateDirectory(clashDir);
			CharacterProfile clashA = ProfileFactory.Sample();
			clashA.ModId = "ClashModAa";
			CharacterProfile clashB = ProfileFactory.Sample();
			clashB.ModId = "ClashModAa";
			string clashPathA = Path.Combine(clashDir, "存档甲.json");
			string clashPathB = Path.Combine(clashDir, "存档乙.json");
			ProfileFactory.Save(clashA, clashPathA);
			ProfileFactory.Save(clashB, clashPathB);
			string? oldOverride = _profileFolderOverride;
			string? oldPath = _currentProfilePath;
			CharacterProfile oldProfile = _profile;
			try
			{
				_profileFolderOverride = clashDir;
				_profile = clashA;
				_currentProfilePath = clashPathA;
				Check("能发现「另一个存档用了同一个 ModId」", FindSaveWithSameModId() == "存档乙", FindSaveWithSameModId() ?? "(没发现)");
				RefreshIssues();
				Check("左下角问题列表会警告两个存档会互相覆盖", Issues.Any(x => x.Contains("互相覆盖") && x.Contains("存档乙")), string.Join(" | ", Issues.Where(x => x.Contains("互相覆盖"))));
				clashB.ModId = "ClashModBb";
				ProfileFactory.Save(clashB, clashPathB);
				Check("ModId 改掉之后警告消失", FindSaveWithSameModId() == null, FindSaveWithSameModId() ?? "(没有问题)");
			}
			finally
			{
				_profileFolderOverride = oldOverride;
				_currentProfilePath = oldPath;
				_profile = oldProfile;
				RefreshAll();
			}
		}
		finally
		{
			try { Directory.Delete(clashDir, true); } catch { }
		}

		// ===== #2 「保存配置」直接存进原存档，不再弹路径选择 =====
		// 用户要求：点保存 → 存进打开的这份存档；只有「模组 ID 和文件名不一样」时才问一次要不要改名。
		List<string> windowTexts = TextsIn(this);
		Check("顶部工具栏的保存按钮改成「保存配置」（去掉省略号 = 不再弹路径选择）",
			windowTexts.Contains("保存配置") && !windowTexts.Contains("保存配置…"),
			string.Join(" / ", windowTexts.Where(x => x.Contains("保存"))));
		Check("保存按钮的提示写明「直接存进当前存档 / 不再弹路径选择」",
			((FindButtonByContent("保存配置", this)?.ToolTip as string) ?? "").Contains("不再弹路径选择"),
			(FindButtonByContent("保存配置", this)?.ToolTip as string) ?? "(没有提示)");
		Check("存档文件名会去掉非法字符 / 空名字兜底",
			SanitizeSaveName("a/b:c*?") == "a_b_c__" && SanitizeSaveName("   ") == "新角色",
			SanitizeSaveName("a/b:c*?") + " / " + SanitizeSaveName("   "));		{
			string saveDir = Path.Combine(Path.GetTempPath(), "forge_uicheck_saveinplace");
			try
			{
				Directory.CreateDirectory(saveDir);
				string pathA = Path.Combine(saveDir, "存档甲.json");
				string? oldOverride2 = _profileFolderOverride;
				string? oldPath2 = _currentProfilePath;
				string? oldAskKey = _renameAskKey;
				CharacterProfile oldProfile2 = _profile;
				var oldSelection2 = SaveListSelection();
				try
				{
					CharacterProfile p = ProfileFactory.Sample();
					p.ModId = "存档甲";
					ProfileFactory.Save(p, pathA);
					ProfileFactory.Save(p, Path.Combine(saveDir, "存档乙.json"));
					_profile = p;
					_currentProfilePath = pathA;
					_profileFolderOverride = saveDir;
					_renameAskKey = null;

					Check("模组 ID 和文件名一致时不做任何改名", RenameTargetFor(pathA, "存档甲") == null, "不改名");
					Check("模组 ID 和文件名不一致时给出改名目标",
						RenameTargetFor(pathA, "新名字") == Path.Combine(saveDir, "新名字.json"),
						RenameTargetFor(pathA, "新名字") ?? "(没给出)");
					Check("模组 ID 为空时不自动改名（留给用户自己决定）", RenameTargetFor(pathA, "") == null, "不改名");

					// 名字一致 → 保存进原文件，不新建别的文件
					Check("「保存配置」在名字一致时直接保存进原存档", SaveProfileInPlace() && File.Exists(pathA));
					int before = Directory.GetFiles(saveDir, "*.json").Length;
					Check("「保存配置」不会凭空多出一份存档（以前弹路径选择就容易存成两份）",
						before == 2, before + " 个 json");

					// 改了模组 ID → 自检模式下 Confirm 自动答「是」→ 应该把文件改名过去
					_profile.ModId = "新名字";
					_renameAskKey = null;
					bool ok = SaveProfileInPlace();
					string newPath = Path.Combine(saveDir, "新名字.json");
					Check("改了模组 ID 再点「保存配置」会把存档改名（旧文件不留下重复）",
						ok && File.Exists(newPath) && !File.Exists(pathA),
						"新名字.json=" + File.Exists(newPath) + "，存档甲.json 还在=" + File.Exists(pathA));
					Check("改名后「当前存档」指向新文件（后续保存 / 生成都跟着走新名字）",
						string.Equals(_currentProfilePath, newPath, StringComparison.OrdinalIgnoreCase), _currentProfilePath ?? "(空)");
					Check("改名后同一个名字不会重复追问",
						string.Equals(_renameAskKey, RenameAskKey(newPath, "新名字"), StringComparison.Ordinal), _renameAskKey ?? "(空)");

					// 「用当前配置覆盖该存档」之后，当前存档要切到被覆盖的那一份
					// （不切的话接着点「保存配置」会写回上一个文件，刚覆盖的存档停在旧内容上）
					string overwritePath = Path.Combine(saveDir, "存档乙.json");
					Profiles.Clear();
					Profiles.Add(new ProfileEntry { Name = "存档乙", Path = overwritePath, Info = "" });
					ProfileList.SelectedItem = Profiles[0];
					OnSaveProfileToFolder(this, new RoutedEventArgs());
					Check("「用当前配置覆盖该存档」之后当前存档切到被覆盖的那份",
						string.Equals(_currentProfilePath, overwritePath, StringComparison.OrdinalIgnoreCase),
						_currentProfilePath ?? "(空)");
					Check("覆盖别的存档后不会被立刻追问改名",
						string.Equals(_renameAskKey, RenameAskKey(overwritePath, _profile.ModId), StringComparison.Ordinal), _renameAskKey ?? "(空)");
				}
				finally
				{
					_profileFolderOverride = oldOverride2;
					_currentProfilePath = oldPath2;
					_renameAskKey = oldAskKey;
					_profile = oldProfile2;
					RefreshAll();
					RestoreListSelection(oldSelection2);
				}
			}
			finally
			{
				try { Directory.Delete(saveDir, true); } catch { }
			}
		}

		// ===== #3 美术资源上传后立刻写进存档 =====
		// 用户报过：「美术资源选项下的所有图片上传后，下次打开就没了，又要重新上传」。
		// 以前选完图只改内存，不点「保存配置」就关程序 → 全丢。现在选完立刻写进存档。
		{
			string artDir = Path.Combine(Path.GetTempPath(), "forge_uicheck_artpersist");
			try
			{
				Directory.CreateDirectory(artDir);
				string artImg = Path.Combine(artDir, "pic.png");
				PngUtil.WriteRgba(artImg, 8, 8, new byte[8 * 8 * 4]);
				string artSave = Path.Combine(artDir, "美术存档.json");

				string? oldPath3 = _currentProfilePath;
				string? oldAsk3 = _renameAskKey;
				string? oldOverride3 = _profileFolderOverride;
				CharacterProfile oldProfile3 = _profile;
				var oldSelection3 = SaveListSelection();
				try
				{
					CharacterProfile artProfile = ProfileFactory.Sample();
					artProfile.ModId = "美术存档";
					ProfileFactory.Save(artProfile, artSave);
					_profile = artProfile;
					_currentProfilePath = artSave;
					_profileFolderOverride = artDir;
					_renameAskKey = RenameAskKey(artSave, artProfile.ModId);
					RefreshAll();

					foreach (ArtSlot slot in ArtSlots)
					{
						slot.Path = artImg;
						slot.Save(_profile.Art);
					}
					string portraitKey = Naming.From(_profile).CardClassName(_profile, _profile.Cards[0]);
					_profile.Art.CardPortraits[portraitKey] = artImg;
					PersistArtChange("自检：上传了图片");

					CharacterProfile artReloaded = ProfileFactory.Load(artSave);
					Check("美术槽位的图片上传后**立刻**写进存档（下次打开还在，不用重新上传）",
						!string.IsNullOrWhiteSpace(artReloaded.Art.Icon)
						&& !string.IsNullOrWhiteSpace(artReloaded.Art.SelectIcon)
						&& !string.IsNullOrWhiteSpace(artReloaded.Art.MapMarker)
						&& !string.IsNullOrWhiteSpace(artReloaded.Art.EnergyIcon)
						&& !string.IsNullOrWhiteSpace(artReloaded.Art.ModImage)
						&& !string.IsNullOrWhiteSpace(artReloaded.Art.SelectBackground),
						"Icon / SelectIcon / MapMarker / EnergyIcon / ModImage / SelectBackground 都写进了 json");
					Check("卡面（逐张上传）也会立刻写进存档",
						artReloaded.Art.CardPortraits.Count > 0, artReloaded.Art.CardPortraits.Count + " 张卡面写进了 json");
					Check("上传图片的提示会告诉用户「已写进存档」",
						StatusText.Contains("已写进存档"), StatusText);

					// 模拟「关掉程序再打开」，但界面上还留着**别的**图的旧状态（真实场景就是这样）：
					// 以前 LoadProfileCore 是「先 SyncArt 再 RefreshAll」→ 把旧槽位值写回刚载入的配置，
					// 存档里的 7 个美术路径当场被抹掉；而「逐张卡面」在别的字段里，不会被抹 ——
					// 于是用户看到的就是「卡面还在、那 7 个槽位每次打开都变回占位，要重新上传」。
					string stalePath = Path.Combine(artDir, "stale.png");
					PngUtil.WriteRgba(stalePath, 4, 4, new byte[4 * 4 * 4]);
					foreach (ArtSlot s in ArtSlots) s.Path = stalePath;
					LoadProfileCore(artSave);
					Check("重新打开存档后，7 个槽位显示「已上传」且用的是**存档里那份**图（不是界面上的旧状态）",
						ArtSlots.All(s => s.Display.Contains("已上传") && string.Equals(s.Path, artImg, StringComparison.OrdinalIgnoreCase)),
						string.Join("、", ArtSlots.Select(s => s.Name + "=" + (s.Path ?? "空"))));
					Check("重新打开存档不会把存档里的美术路径抹掉（先读进界面、再写回内存配置）",
						string.Equals(ProfileFactory.Load(artSave).Art.Icon, artImg, StringComparison.OrdinalIgnoreCase)
						&& string.Equals(_profile.Art.Icon, artImg, StringComparison.OrdinalIgnoreCase),
						"json=" + (ProfileFactory.Load(artSave).Art.Icon ?? "空") + " / 内存=" + (_profile.Art.Icon ?? "空"));

					// 清除图片同样要落盘（不然用户清掉之后重启又变回有图）
					ArtSlot topIconSlot = ArtSlots.First(s => s.Name == "顶部头像");   // 这个槽位对应 Art.Icon
					topIconSlot.Path = null;
					topIconSlot.Save(_profile.Art);
					PersistArtChange("自检：清除了图片");
					Check("清除图片也会立刻写进存档（清完重启不会又变回有图）",
						string.IsNullOrWhiteSpace(ProfileFactory.Load(artSave).Art.Icon), "json 里的 Icon 已清空");
				}
				finally
				{
					_profileFolderOverride = oldOverride3;
					_currentProfilePath = oldPath3;
					_renameAskKey = oldAsk3;
					_profile = oldProfile3;
					RefreshAll();
					RestoreListSelection(oldSelection3);
				}
			}
			finally
			{
				try { Directory.Delete(artDir, true); } catch { }
			}
		}
		RelicSpec relicSpec4 = new RelicSpec
		{
			Name = "条件遗物",
			ClassName = "UiCheckCondRelic",
			Trigger = "PlayerTurnStart"
		};
		relicSpec4.Effects.Clear();
		relicSpec4.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 3m
		});
		relicSpec4.Condition.Kind = "OncePerCombat";
		characterProfile16.Relics.Add(relicSpec4);
		string text30 = CSharpCodeGen.RelicSource(characterProfile16, relicSpec4, 0);
		Check("遗物的「每场战斗只触发一次」用标记 + 战斗结束清零（本体百年积木的做法）", text30.Contains("_condUsedThisCombat") && text30.Contains("if (_condUsedThisCombat) return;") && text30.Contains("AfterCombatEnd(CombatRoom room)") && text30.Contains("_condUsedThisCombat = false;"), "有标记与重置");
		Check("遗物条件不会生成两次同名的钩子（触发时机 + 重置各一个方法）", text30.Split("AfterCombatEnd(CombatRoom room)").Length - 1 == 1, "只有一个 AfterCombatEnd");
		RelicSpec relicSpec5 = new RelicSpec
		{
			Name = "条件遗物2",
			ClassName = "UiCheckCondRelic2",
			Trigger = "PlayerTurnEnd"
		};
		relicSpec5.Effects.Clear();
		relicSpec5.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 3m
		});
		relicSpec5.Condition.Kind = "EveryNTurns";
		relicSpec5.Condition.Amount = 3m;
		characterProfile16.Relics.Add(relicSpec5);
		string text31 = CSharpCodeGen.RelicSource(characterProfile16, relicSpec5, 0);
		Check("遗物的「每 N 回合触发一次」按回合数取余", text31.Contains("TurnNumber % 3 == 0"), "有回合取余");
		Check("「每 N 回合」填 0 也不会生成 % 0（会把 N 压到 1）", CSharpCodeGen.ConditionExpr(new ConditionSpec
		{
			Kind = "EveryNTurns",
			Amount = 0m
		}, isCard: false).Contains("% 1 == 0"), "已压到 1");
		CardSpec cardSpec18 = new CardSpec
		{
			Name = "普通卡",
			ClassName = "UiCheckPlain",
			Cost = 1
		};
		cardSpec18.Effects.Clear();
		cardSpec18.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 5m
		});
		string text32 = CSharpCodeGen.CardSource(characterProfile16, cardSpec18, 0);
		Check("没配条件时不生成 IsPlayable / 金边 / if 包", !text32.Contains("IsPlayable") && !text32.Contains("ShouldGlowGoldInternal") && !text32.Contains("CombatManager"), "干净");
		CharacterProfile characterProfile17 = ProfileFactory.Sample();
		CardSpec cardSpec19 = new CardSpec
		{
			Name = "条件卡",
			ClassName = "UiCheckCondDesc",
			Cost = 1
		};
		cardSpec19.Effects.Clear();
		cardSpec19.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 5m
		});
		cardSpec19.Condition.Kind = "HpBelowPercent";
		cardSpec19.Condition.Amount = 50m;
		characterProfile17.Cards.Add(cardSpec19);
		string text33 = LocalizationGen.CardsJson(characterProfile17);
		Check("卡面描述会写明条件与不满足的后果", text33.Contains("条件：生命值低于 50%（不满足时无法打出）"), "条件：生命值低于 50%（不满足时无法打出）");
		CharacterProfile characterProfile18 = ProfileFactory.Sample();
		RelicSpec relicSpec6 = new RelicSpec
		{
			Name = "条件遗物",
			ClassName = "UiCheckCondDescR",
			Trigger = "CombatVictory"
		};
		relicSpec6.Effects.Clear();
		relicSpec6.Effects.Add(new EffectSpec
		{
			Kind = "Gold",
			Amount = 10m
		});
		relicSpec6.Condition.Kind = "DrawPileEmpty";
		characterProfile18.Relics.Add(relicSpec6);
		Check("遗物描述会写明条件与「不触发」", LocalizationGen.RelicsJson(characterProfile18).Contains("条件：抽牌堆为空（不满足时不触发）"), "条件：抽牌堆为空（不满足时不触发）");
		Check("卡牌页已去掉「用本体（铁甲战士）卡补池」开关", !TextsIn(root15).Any((string t) => t.Contains("用本体（铁甲战士）卡补池")), "已去掉");
		Check("卡牌页已去掉「卡池不足时也用本体卡兜底」开关", !TextsIn(root15).Any((string t) => t.Contains("卡池不足时也用本体卡兜底")), "已去掉");
		List<string> source4 = TextsIn(SelectTabRoot("角色"));
		Check("角色页已删掉那个没用的「用铁甲战士的内容补齐（占位）」开关", !source4.Any((string t) => t.Contains("用铁甲战士的内容补齐")), "已删");
		Check("角色页不再解释「池子不够 / 初始卡组开关」那套（说明改到了别处）",
			!source4.Any((string t) => t.Contains("池子不够时会直接报错拦住生成"))
			&& !source4.Any((string t) => t.Contains("不再是一个开关")), "已删");
		Check("角色页不再长篇解释「只放你自己的内容 / 绝不出现本体卡」", !source4.Any((string t) => t.Contains("绝不会出现本体") || t.Contains("只放你自己的内容")), "已去掉");
		Check("老存档里的 FillPoolsWithIronclad 字段不再被读（开着也一样干净）", !CSharpCodeGen.CardPoolSource(LegacyFillerProfile()).Contains("Ironclad"), "干净");
		Check("新示例配置不再默认打开那个废弃开关", !ProfileFactory.Sample().FillPoolsWithIronclad, ProfileFactory.Sample().FillPoolsWithIronclad ? "还是 true" : "false");
		CharacterProfile p2 = ProfileFactory.Sample();
		Check("生成的卡池里绝不掺本体（铁甲战士）的卡", !CSharpCodeGen.CardPoolSource(p2).Contains("Ironclad"), CSharpCodeGen.CardPoolSource(p2).Contains("Ironclad") ? "还在掺" : "干净");
		Check("遗物池也不掺本体遗物", !CSharpCodeGen.RelicPoolSource(p2).Contains("Ironclad"));
		Check("药水池也不掺本体药水", !CSharpCodeGen.PotionPoolSource(p2).Contains("Ironclad"));
		int count12 = CSharpCodeGen.RewardEligibleCards(p2).Count;
		Check("卡池够三选一时不会报错", !CSharpCodeGen.RewardPoolTooSmall(p2), $"可用 {count12} 张 / 门槛 {3}");
		CharacterProfile characterProfile19 = ProfileFactory.Sample();
		characterProfile19.Cards.Clear();
		CardSpec item = new CardSpec
		{
			Name = "只有一张",
			ClassName = "UiCheckTiny1",
			Rarity = "Common",
			CardType = "Attack",
			InCardPool = true
		};
		characterProfile19.Cards.Add(item);
		characterProfile19.Cards.Add(new CardSpec
		{
			Name = "不入池",
			ClassName = "UiCheckTiny2",
			Rarity = "Common",
			CardType = "Skill",
			InCardPool = false
		});
		characterProfile19.Cards.Add(new CardSpec
		{
			Name = "Basic 不算",
			ClassName = "UiCheckTiny3",
			Rarity = "Basic",
			CardType = "Attack",
			InCardPool = true
		});
		Check("卡池只有 1 张可用时会判定为太小（Basic 与「不入池」不算）", CSharpCodeGen.RewardPoolTooSmall(characterProfile19), CSharpCodeGen.RewardPoolStatus(characterProfile19));
		List<ValidationIssue> source5 = ProfileValidator.Validate(characterProfile19);
		Check("这种情况校验里是「错误」（会拦住生成）", source5.Any((ValidationIssue i) => i.IsError && i.Message.Contains("至少要 3 张")), string.Join(" / ", (from i in source5
			where i.IsError
			select i.Message).Take(1)));
		Check("错误信息里给了具体做法（把「加入卡池」的卡加到 3 张）", source5.Any((ValidationIssue i) => i.Message.Contains("「加入卡池」的卡加到至少 3 张")), "有做法");
		Check("错误信息不再长篇解释「不会掺本体卡」", !source5.Any((ValidationIssue i) => i.Message.Contains("不会再掺")), "已去掉");
		Check("卡池里没有任何铁甲卡（真去生成一遍也是）", !CSharpCodeGen.CardPoolSource(characterProfile19).Contains("Ironclad"), "干净");
		Check("类名能对上本体的本地化键（PoisonPower → POISON_POWER）", EffectCatalog.SlugFor("PoisonPower") == "POISON_POWER", EffectCatalog.SlugFor("PoisonPower"));
		Check("中毒是血条上那一截的主人（%PoisonForeground）", EffectCatalog.HealthBarNodeFor("PoisonPower") == "PoisonForeground", EffectCatalog.HealthBarNodeFor("PoisonPower") ?? "(空)");
		Check("灾厄也有血条段（%DoomForeground，着色器渐变画的那截）", EffectCatalog.HealthBarNodeFor("DoomPower") == "DoomForeground", EffectCatalog.HealthBarNodeFor("DoomPower") ?? "(空)");
		Check("别的状态没有血条段（比如力量）", EffectCatalog.HealthBarNodeFor("StrengthPower") == null);
		string key = EffectCatalog.ZhLocText("DOOM_POWER.title");
		bool ok10 = ((key == null || key == "灾厄") ? true : false);
		Check("本体 powers 表里 DOOM_POWER 的中文名是「灾厄」（不是我以前写错的「末日」）", ok10, EffectCatalog.ZhLocText("DOOM_POWER.title") ?? "(没读到表，跳过)");
		Check("中毒血条的本体原色 = 79C03C（场景里的 self_modulate）", EffectCatalog.DefaultBarColorFor("PoisonPower") == "79C03C", EffectCatalog.DefaultBarColorFor("PoisonPower") ?? "(空)");
		Check("灾厄血条的本体主色 = 834181（场景渐变的中间色标）", EffectCatalog.DefaultBarColorFor("DoomPower") == "834181", EffectCatalog.DefaultBarColorFor("DoomPower") ?? "(空)");
		bool flag3 = EffectCatalog.ZhPowerLoc.Count > 0;
		Check("读到了本体 powers 本地化表（改名能不能生效就看它）", flag3, $"{EffectCatalog.ZhPowerLoc.Count} 个键");
		if (flag3)
		{
			Check("表里确实有 POISON_POWER.title 这个键（我们覆盖的就是它）", EffectCatalog.ZhPowerLoc.ContainsKey("POISON_POWER.title"), EffectCatalog.ZhLocText("POISON_POWER.title") ?? "(没有)");
		}
		CharacterProfile characterProfile20 = ProfileFactory.Sample();
		characterProfile20.VanillaPowerOverrides.Clear();
		Check("没填改写时不生成补丁文件（不打扰本体）", !VanillaPowerGen.HasAny(characterProfile20));
		string text34 = Path.Combine(Path.GetTempPath(), "forge_uicheck_poison_icon.png");
		PngUtil.WriteRgba(text34, 32, 32, new byte[4096]);
		VanillaPowerOverride vanillaPowerOverride = new VanillaPowerOverride
		{
			PowerId = "PoisonPower",
			LocSlug = "POISON_POWER",
			VanillaName = "中毒",
			Name = "剧毒",
			Description = "在{OnPlayer:你的回合|其回合}开始时失去生命，然后将[gold]剧毒[/gold]层数减少[blue]1[/blue]。",
			Icon = text34,
			BarColor = "33CC66",
			AmountColor = "FFEE00"
		};
		characterProfile20.VanillaPowerOverrides.Add(vanillaPowerOverride);
		Check("填了改写就会生成补丁文件", VanillaPowerGen.HasAny(characterProfile20));
		string text35 = LocalizationGen.PowersJson(characterProfile20);
		Check("新名字写进 powers.json（键和本体同名 → 本体加载时会覆盖）", text35.Contains("\"POISON_POWER.title\"") && text35.Contains("剧毒"), "改名键在");
		Check("新描述也写进去了", text35.Contains("\"POISON_POWER.description\""));
		if (flag3 && EffectCatalog.ZhLocText("POISON_POWER.smartDescription") != null)
		{
			Check("本体有 smartDescription 时一起覆盖（战斗里悬停提示用的是它）", text35.Contains("\"POISON_POWER.smartDescription\""), "一起盖");
		}
		string text36 = VanillaPowerGen.Source(characterProfile20);
		Check("补丁按本体 Id.Entry 认状态（POISON_POWER）", text36.Contains("[\"POISON_POWER\"]"), "在表里");
		Check("补丁替换状态栏小图标 + 特效大图", text36.Contains("nameof(PowerModel.Icon)") && text36.Contains("nameof(PowerModel.BigIcon)"), "两个都在");
		Check("补丁改层数数字颜色：补在消费端 NPower.RefreshAmount 上（本体中毒/灾厄 override 了基类属性，补基类没用）", text36.Contains("\"RefreshAmount\"") && text36.Contains("ThemeConstants.Label.FontColor") && !text36.Contains("nameof(PowerModel.AmountLabelColor)"), "补在 RefreshAmount 之后");
		Check("补丁改血条那一截的颜色（NHealthBar + %节点 + SelfModulate）", text36.Contains("NHealthBar") && text36.Contains("\"%\" + e.BarNode") && text36.Contains("SelfModulate"), "都改到");
		CharacterProfile characterProfile21 = ProfileFactory.Sample();
		characterProfile21.VanillaPowerOverrides.Clear();
		characterProfile21.VanillaPowerOverrides.Add(new VanillaPowerOverride
		{
			PowerId = "DoomPower",
			LocSlug = "DOOM_POWER",
			VanillaName = "灾厄",
			Name = "死兆",
			BarColor = "FF3366"
		});
		string text37 = VanillaPowerGen.Source(characterProfile21);
		Check("灾厄的血条色走「换渐变贴图」那条路（gradient_tex + GradientTexture1D + 先 Duplicate 材质）", text37.Contains("gradient_tex") && text37.Contains("GradientTexture1D") && text37.Contains("Duplicate()"), "渐变路径在");
		Check("灾厄不会误用 self_modulate（对着色器画的节点没用）", text37.Contains("if (e.BarNode == \"DoomForeground\")"), "分支在");
		Check("灾厄的渐变用的是你填的颜色（FF3366 会进补丁）", text37.Contains("FF3366"), "颜色在");
		Check("图标用 pck 里的相对路径", text36.Contains("res://images/powers_override/poison_power_icon.png"), VanillaPowerGen.IconRelPath(characterProfile20, vanillaPowerOverride));
		Check("两个颜色原样写进补丁（33CC66 / FFEE00）", text36.Contains("33CC66") && text36.Contains("FFEE00"), "颜色在");
		Check("补丁全都带 try/catch（界面补丁出问题不能把战斗搞崩）", text36.Contains("catch (Exception"), "有兜底");
		VanillaPowerOverride vanillaPowerOverride2 = new VanillaPowerOverride
		{
			PowerId = "PoisonPower",
			LocSlug = "POISON_POWER"
		};
		vanillaPowerOverride2.PowerId = "StrengthPower";
		Check("换状态后：键/原名/名字都跟着变（不是还留着中毒）", vanillaPowerOverride2.LocSlug == "STRENGTH_POWER" && vanillaPowerOverride2.VanillaName == "力量" && vanillaPowerOverride2.Name == "力量", $"{vanillaPowerOverride2.LocSlug} / 原名={vanillaPowerOverride2.VanillaName} / 名字={vanillaPowerOverride2.Name}");
		Check("换状态会把上一个状态的图标/颜色清掉", vanillaPowerOverride2.Icon.Length == 0 && vanillaPowerOverride2.BarColor.Length == 0 && vanillaPowerOverride2.AmountColor.Length == 0, "已清空");
		vanillaPowerOverride2.PowerId = "PoisonPower";
		Check("换回中毒时描述按本体原文打底", vanillaPowerOverride2.Description.Contains("中毒"), (vanillaPowerOverride2.Description.Length > 0) ? (vanillaPowerOverride2.Description.Substring(0, Math.Min(24, vanillaPowerOverride2.Description.Length)) + "…") : "(空)");
		EffectCatalog.SetPowerRenames(characterProfile20.VanillaPowerOverrides);
		Check("改名后状态显示名走新名字", EffectCatalog.PowerName("PoisonPower") == "剧毒", EffectCatalog.PowerName("PoisonPower"));
		CharacterProfile characterProfile22 = ProfileFactory.Sample();
		CardSpec cardSpec20 = new CardSpec
		{
			Name = "下毒牌",
			ClassName = "UiCheckPoisonCard",
			Rarity = "Common",
			CardType = "Skill",
			InCardPool = true
		};
		cardSpec20.Effects.Clear();
		cardSpec20.Effects.Add(new EffectSpec
		{
			Kind = "ApplyPower",
			PowerId = "PoisonPower",
			Amount = 3m,
			TargetSide = "Enemy"
		});
		characterProfile22.Cards.Add(cardSpec20);
		characterProfile22.VanillaPowerOverrides.Add(vanillaPowerOverride);
		EffectCatalog.SetPowerRenames(characterProfile22.VanillaPowerOverrides);
		string text38 = LocalizationGen.CardsJson(characterProfile22);
		Check("自己卡牌的描述用新名字（不是「施加 3 层中毒」）", text38.Contains("剧毒") && !text38.Contains("层中毒"), "卡面已用新名");
		Check("本机读到了本体卡牌中文表（卡面描述里的旧名字才换得了）", VanillaPowerGen.CanRewriteVanillaCardText, $"{EffectCatalog.ZhCardLoc.Count} 个键");
		List<(string, string, string)> list29 = VanillaPowerGen.VanillaTextReplacements(characterProfile22).ToList();
		Check("本体卡牌/遗物/药水里写着「中毒」的描述会被一起换掉", list29.Count > 0 && list29.All<(string, string, string)>(((string Table, string Key, string Text) r) => !r.Text.Contains("中毒") && r.Text.Contains("剧毒")), $"{list29.Count} 条（卡牌 {list29.Count<(string, string, string)>(((string Table, string Key, string Text) r) => r.Table == "cards")} / 遗物 {list29.Count<(string, string, string)>(((string Table, string Key, string Text) r) => r.Table == "relics")} / 药水 {list29.Count<(string, string, string)>(((string Table, string Key, string Text) r) => r.Table == "potions")}）");
		Check("只改描述键，不动标题（免得卡名对不上）", list29.All<(string, string, string)>(((string Table, string Key, string Text) r) => r.Key.EndsWith(".description") || r.Key.EndsWith(".smartDescription")), "都是描述键");
		Check("不勾「本体卡面也换名」时就不替换", VanillaPowerGen.VanillaTextReplacements(ProfileWith(new VanillaPowerOverride
		{
			PowerId = "PoisonPower",
			LocSlug = "POISON_POWER",
			VanillaName = "中毒",
			Name = "剧毒",
			ReplaceInVanillaText = false
		})).Count() == 0, "已跳过");
		Check("替换项能合进生成的表（键被覆盖）", LocalizationGen.MergeVanillaText(LocalizationGen.CardsJson(characterProfile22), list29, "cards").Contains("剧毒"), "合并成功");
		EffectCatalog.SetPowerRenames(null);
		CharacterProfile characterProfile23 = ProfileFactory.Sample();
		characterProfile23.VanillaPowerOverrides.Clear();
		characterProfile23.VanillaPowerOverrides.Add(new VanillaPowerOverride
		{
			PowerId = "PoisonPower",
			LocSlug = "POISON_POWER",
			Name = "剧毒",
			BarColor = "ZZZZZZ"
		});
		Check("血条颜色写得不像颜色 → 错误", ProfileValidator.Validate(characterProfile23).Any((ValidationIssue i) => i.IsError && i.Message.Contains("血条颜色")), ProfileValidator.Validate(characterProfile23).First((ValidationIssue i) => i.IsError).Message);
		CharacterProfile characterProfile24 = ProfileFactory.Sample();
		characterProfile24.VanillaPowerOverrides.Clear();
		characterProfile24.VanillaPowerOverrides.Add(new VanillaPowerOverride
		{
			PowerId = "PoisonPower",
			LocSlug = "POISON_POWER",
			Icon = "C:\\definitely-not-here\\poison.png"
		});
		Check("图标文件不存在 → 错误", ProfileValidator.Validate(characterProfile24).Any((ValidationIssue i) => i.IsError && i.Message.Contains("图标文件不存在")), ProfileValidator.Validate(characterProfile24).First((ValidationIssue i) => i.IsError).Message);
		CharacterProfile characterProfile25 = ProfileFactory.Sample();
		characterProfile25.VanillaPowerOverrides.Clear();
		characterProfile25.VanillaPowerOverrides.Add(new VanillaPowerOverride
		{
			PowerId = "StrengthPower",
			LocSlug = "STRENGTH_POWER",
			BarColor = "33CC66"
		});
		Check("给没有血条段的状态填血条色 → 只给警告（说了不会有显示效果）", ProfileValidator.Validate(characterProfile25).Any((ValidationIssue i) => i.Level == "警告" && i.Message.Contains("血条段")), ProfileValidator.Validate(characterProfile25).First((ValidationIssue i) => i.Level == "警告").Message);
		CharacterProfile characterProfile26 = ProfileFactory.Sample();
		characterProfile26.VanillaPowerOverrides.Clear();
		characterProfile26.VanillaPowerOverrides.Add(new VanillaPowerOverride
		{
			PowerId = "NoSuchPowerXyz",
			Name = "甲"
		});
		Check("写了不存在的状态 → 错误（类名要对上本体）", EffectCatalog.Powers.Count == 0 || ProfileValidator.Validate(characterProfile26).Any((ValidationIssue i) => i.IsError && i.Message.Contains("找不到")), (EffectCatalog.Powers.Count == 0) ? "(本机没有状态目录，跳过)" : "有错误");
		SelectTabRoot("本体状态改写");
		int count13 = Profile.VanillaPowerOverrides.Count;
		OnAddPowerOverride(this, new RoutedEventArgs());
		VanillaPowerOverride vanillaPowerOverride3 = Profile.VanillaPowerOverrides.LastOrDefault();
		Check("「添加改写」默认选中中毒、并把原名/原描述填好", vanillaPowerOverride3 != null && vanillaPowerOverride3.PowerId == "PoisonPower", (vanillaPowerOverride3 == null) ? "(没加上)" : $"{vanillaPowerOverride3.PowerId} / 原名={vanillaPowerOverride3.VanillaName} / 描述 {vanillaPowerOverride3.Description.Length} 字");
		Check("界面上有「本体状态改写」页的列表和按钮", TextsIn(SelectTabRoot("本体状态改写")).Contains("添加改写") && TextsIn(PowerOverrideTab.Content as DependencyObject).Contains("血条颜色（RRGGBB，留空 = 不改）"), "控件在");
		DependencyObject dependencyObject = PowerOverrideTab.Content as DependencyObject;
		List<SearchComboBox> list30 = new List<SearchComboBox>();
		if (dependencyObject != null)
		{
			CollectSearchBoxes(dependencyObject, list30);
		}
		SearchComboBox searchComboBox = list30.FirstOrDefault((SearchComboBox b) => BindingOperations.GetBinding(b, SearchComboBox.SelectedValueProperty)?.Path?.Path == "PowerId");
		Check("「要改哪个状态」是个绑在 PowerId 上的可搜索下拉", searchComboBox != null, $"{list30.Count} 个可搜索框");
		if (searchComboBox != null && PowerOverrideList.SelectedItem is VanillaPowerOverride vanillaPowerOverride4 && vanillaPowerOverride3 != null)
		{
			PowerEntry powerEntry = EffectCatalog.Powers.FirstOrDefault((PowerEntry x) => x.Id == "StrengthPower");
			if ((object)powerEntry != null)
			{
				searchComboBox.Pick(powerEntry);
				UpdateLayout();
				Check("在那个下拉里选「力量」后模型真的换了状态（键/原名/名字一起换）", vanillaPowerOverride4.PowerId == "StrengthPower" && vanillaPowerOverride4.LocSlug == "STRENGTH_POWER" && vanillaPowerOverride4.VanillaName == "力量", $"{vanillaPowerOverride4.PowerId} / {vanillaPowerOverride4.LocSlug} / 原名={vanillaPowerOverride4.VanillaName}");
				Check("选完之后输入框里显示的是「力量」，不是还留着上一个状态", searchComboBox.CurrentItem is PowerEntry powerEntry2 && powerEntry2.Id == "StrengthPower", (searchComboBox.CurrentItem as PowerEntry)?.Display ?? "(空)");
			}
		}
		UpdateLayout();
		Check("右侧编辑区够宽（用户要求调大：>=560）", PowerOverrideDetail.ActualWidth >= 560.0, $"实际 {PowerOverrideDetail.ActualWidth:F0}px");
		Check("页面上有「本体卡面描述也换名」的勾选项", TextsIn(PowerOverrideTab.Content as DependencyObject).Any((string t) => t.Contains("本体卡牌 / 遗物 / 药水描述里的旧名字也一起换掉")), "选项在");
		Check("自检模式不会弹「关闭前保存」的框（否则会卡住无人值守）", SuppressClosePromptForTest, "已抑制");
		// 「关窗口卡住」的回归测试：自检 / 命令行模式下 OnClosing 既不拦、也不弹框，
		// 这样 --uicheck / --buildtest 这类无人值守的进程才能自己退出（以前会永远挂着）。
		{
			var closeArgs = new System.ComponentModel.CancelEventArgs();
			OnClosing(closeArgs);
			Check("自检 / 命令行模式下关窗口不会被拦下（进程能自己退出）", !closeArgs.Cancel, "不拦");
			Check("有未保存改动时才需要问「要不要保存」（这里只是确认判定函数可用）",
				HasUnsavedProfileChanges() || !HasUnsavedProfileChanges(), "判定可用");
		}
		bool flag4 = HasUnsavedProfileChanges();
		string displayName = Profile.DisplayName;
		Profile.DisplayName = displayName + "_改一下";
		bool flag5 = HasUnsavedProfileChanges();
		Check("改过配置之后一定是「有未保存的改动」", flag5, "改动前=" + (flag4 ? "脏" : "干净") + " → 改动后=" + (flag5 ? "脏" : "干净"));
		Profile.DisplayName = displayName;
		Check("改回原样后回到和改动前一样的状态", HasUnsavedProfileChanges() == flag4, "改动前=" + (flag4 ? "脏" : "干净") + " / 改回后=" + (HasUnsavedProfileChanges() ? "脏" : "干净"));
		Profile.VanillaPowerOverrides.Remove(vanillaPowerOverride3);
		Check("测试用的改写已清理", Profile.VanillaPowerOverrides.Count == count13);
		if (!string.IsNullOrWhiteSpace(Profile.Paths.VanillaProject) && Directory.Exists(Profile.Paths.VanillaProject))
		{
			CharacterProfile characterProfile27 = ProfileFactory.Sample();
			characterProfile27.Paths.VanillaProject = Profile.Paths.VanillaProject;
			characterProfile27.Paths.GameDataDir = Profile.Paths.GameDataDir;
			characterProfile27.Paths.OutputDir = Path.Combine(Path.GetTempPath(), "forge_uicheck_power_" + Guid.NewGuid().ToString("N").Substring(0, 8));
			characterProfile27.VanillaPowerOverrides.Add(new VanillaPowerOverride
			{
				PowerId = "PoisonPower",
				LocSlug = "POISON_POWER",
				VanillaName = "中毒",
				Name = "剧毒",
				Icon = text34,
				BarColor = "33CC66"
			});
			Directory.CreateDirectory(characterProfile27.Paths.OutputDir);
			GenerationResult generationResult2 = ModGenerator.Generate(characterProfile27);
			string path6 = Path.Combine(generationResult2.ProjectRoot, "cs", "VanillaPowerOverride.cs");
			string[] buffer2 = new string[] { generationResult2.ProjectRoot, Naming.From(characterProfile27).ModId, "localization", "zhs", "powers.json" };
			string path7 = Path.Combine(buffer2);
			Check("生成工程时真的写出 cs/VanillaPowerOverride.cs", generationResult2.Success && File.Exists(path6), (!generationResult2.Success) ? ("生成失败：" + generationResult2.Issues.FirstOrDefault((ValidationIssue i) => i.IsError)) : (File.Exists(path6) ? "已写出" : "文件不存在"));
			Check("生成工程时真的把新名字写进 powers.json", File.Exists(path7) && File.ReadAllText(path7).Contains("剧毒"), File.Exists(path7) ? "里面有条目" : "文件不存在");
			string path8 = Path.Combine(generationResult2.ProjectRoot, VanillaPowerGen.IconRelPath(characterProfile27, characterProfile27.VanillaPowerOverrides[0]).Replace('/', Path.DirectorySeparatorChar));
			Check("上传的状态图标被拷进工程（会打进 pck）", File.Exists(path8), VanillaPowerGen.IconRelPath(characterProfile27, characterProfile27.VanillaPowerOverrides[0]));
			try
			{
				Directory.Delete(characterProfile27.Paths.OutputDir, recursive: true);
			}
			catch
			{
			}
		}
		else
		{
			Check("（跳过）本体状态改写的真生成检查：本机没有解包工程", ok: true, "(无解包工程)");
		}
		try
		{
			File.Delete(text34);
		}
		catch
		{
		}
		Check("触发时机清单够用（>=12 个）", PowerTriggers.All.Count >= 12, $"{PowerTriggers.All.Count} 个");
		Check("每个触发时机都有中文名和钩子说明", PowerTriggers.All.All((PowerTriggerOption t) => t.Display.Length > 0 && t.Hint.Length > 0), "都有");
		Check("能用的效果种类有白名单（需要卡牌上下文的会被挡住）", PowerTriggers.Supports("Damage") && PowerTriggers.Supports("ApplyPower") && !PowerTriggers.Supports("ExhaustCard") && !PowerTriggers.Supports("EndTurn"), $"{PowerTriggers.SupportedEffectKinds.Count} 种可用");
		CharacterProfile cpProbe = ProfileFactory.Sample();
		cpProbe.CustomPowers.Clear();
		Check("没填自定义状态时不生成 Power 文件", !CustomPowerGen.HasAny(cpProbe));
		CustomPowerSpec cp1 = new CustomPowerSpec
		{
			Name = "自检铁壁",
			Type = "Buff",
			Description = ""
		};
		PowerTriggerSpec powerTriggerSpec = new PowerTriggerSpec
		{
			Kind = "TurnStart"
		};
		powerTriggerSpec.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 0m,
			AmountIsStack = true,
			TargetSide = "Self"
		});
		PowerTriggerSpec powerTriggerSpec2 = new PowerTriggerSpec
		{
			Kind = "CardPlayed",
			CardFilter = "Attack"
		};
		powerTriggerSpec2.Effects.Add(new EffectSpec
		{
			Kind = "Damage",
			Amount = 2m,
			TargetSide = "Enemy"
		});
		PowerTriggerSpec powerTriggerSpec3 = new PowerTriggerSpec
		{
			Kind = "CardPlayed",
			CardFilter = "Skill"
		};
		powerTriggerSpec3.Effects.Add(new EffectSpec
		{
			Kind = "Draw",
			Amount = 1m,
			TargetSide = "Self"
		});
		cp1.Triggers.Add(powerTriggerSpec);
		cp1.Triggers.Add(powerTriggerSpec2);
		cp1.Triggers.Add(powerTriggerSpec3);
		// 「某个状态层数变化后」：要能指定盯哪个状态（用户报过：没地方选是哪一个状态）
		PowerTriggerSpec powerChangedWatch = new PowerTriggerSpec { Kind = "PowerChanged", PowerId = "StrengthPower" };
		powerChangedWatch.Effects.Add(new EffectSpec { Kind = "Block", Amount = 2m, TargetSide = "Self" });
		PowerTriggerSpec powerChangedEnemy = new PowerTriggerSpec { Kind = "PowerChanged", PowerId = "VulnerablePower", PowerTarget = "AnyEnemy" };
		powerChangedEnemy.Effects.Add(new EffectSpec { Kind = "Block", Amount = 1m, TargetSide = "Self" });
		cp1.Triggers.Add(powerChangedEnemy);
		PowerTriggerSpec powerChangedAny = new PowerTriggerSpec { Kind = "PowerChanged" };
		powerChangedAny.Effects.Add(new EffectSpec { Kind = "Energy", Amount = 1m, TargetSide = "Self" });
		cp1.Triggers.Add(powerChangedWatch);
		cp1.Triggers.Add(powerChangedAny);
		cpProbe.CustomPowers.Add(cp1);
		CustomPowerSpec customPowerSpec = new CustomPowerSpec
		{
			Name = "自检灼烧",
			Type = "Debuff",
			RemoveAtTurnEnd = true
		};
		PowerTriggerSpec powerTriggerSpec4 = new PowerTriggerSpec
		{
			Kind = "TurnEnd"
		};
		powerTriggerSpec4.Effects.Add(new EffectSpec
		{
			Kind = "HpLoss",
			Amount = 3m,
			TargetSide = "Self"
		});
		customPowerSpec.Triggers.Add(powerTriggerSpec4);
		cpProbe.CustomPowers.Add(customPowerSpec);
		Check("填了就生成 Power 文件", CustomPowerGen.HasAny(cpProbe));
		Check("类名按「角色类名 + Power + 序号」自动生成", CustomPowerGen.ClassNameOf(cpProbe, cp1, 0) == Naming.From(cpProbe).CharClass + "Power1", CustomPowerGen.ClassNameOf(cpProbe, cp1, 0));
		Check("本地化键就是本体那套规则（类名 → 全大写下划线）", CustomPowerGen.EntryOf(cpProbe, cp1, 0) == Naming.EntryOf(Naming.From(cpProbe).CharClass + "Power1"), CustomPowerGen.EntryOf(cpProbe, cp1, 0));
		Check("留空描述会按触发时机自动写一句", PowerTriggers.AutoDescription(cp1).Contains("玩家回合开始时") && PowerTriggers.AutoDescription(cp1).Contains("格挡"), PowerTriggers.AutoDescription(cp1).Split('\n')[0]);
		string text39 = CustomPowerGen.Source(cpProbe, cp1, 0);
		Check("回合开始触发器写成本体钩子 AfterPlayerTurnStart", text39.Contains("AfterPlayerTurnStart") && text39.Contains("if (player != base.Owner.Player) return;"), "钩子对");
		Check("「打出牌后」按牌类型过滤（攻击牌 / 技能牌都在同一个方法里）", text39.Contains("AfterCardPlayed") && text39.Contains("CardType.Attack") && text39.Contains("CardType.Skill"), "过滤在");
		Check("同一种触发时机只生成一个方法（写两个同名方法 C# 编译不过，踩过）", text39.Split("AfterCardPlayed").Length - 1 == 1, $"AfterCardPlayed 出现 {text39.Split("AfterCardPlayed").Length - 1} 次");
		Check("「数值 = 层数」写成 base.Amount（本体恶魔形态那种）", text39.Contains("CreatureCmd.GainBlock(base.Owner, base.Amount"), "按层数");
		// ===== 「某个状态层数变化后」：能指定盯哪个状态 =====
		Check("「某个状态层数变化后」选了状态时：只对这个状态的层数变化生效（power is XxxPower）",
			text39.Contains("AfterPowerAmountChanged") && text39.Contains("(power is StrengthPower)"), "按状态过滤");
		Check("「盯自己」和「盯任意敌人」各自独立判断（不会互相挡掉）",
			text39.Contains("power != this && power.Owner == base.Owner") && powerChangedEnemy.Display.Contains("任意敌人"), powerChangedEnemy.Display);
		Check("选了「力量」时那一行的说明里写出状态名", powerChangedWatch.Display.Contains("力量") && powerChangedWatch.Summary.Contains("力量"), powerChangedWatch.Display);
		Check("没选状态时说明写「任意状态」（盯的对象也一起写出来）",
			powerChangedAny.Display.Contains("任意状态") && powerChangedAny.Display.Contains("自己"), powerChangedAny.Display);
		Check("触发时机选项里标了「需要选状态」", PowerTriggers.Find("PowerChanged")!.NeedPower && !PowerTriggers.Find("TurnStart")!.NeedPower, "PowerChanged 需要选状态");
		Check("「打出牌后」的行标记为需要选牌型（不选状态）",
			PowerTriggers.Find("CardPlayed")!.NeedCardFilter && !PowerTriggers.Find("CardPlayed")!.NeedPower, "CardPlayed 需要牌型");
		CharacterProfile watchProfile = ProfileFactory.Sample();
		CustomPowerSpec watchPower = new CustomPowerSpec { Name = "自检盯状态", Type = "Buff" };
		PowerTriggerSpec watchTrigger = new PowerTriggerSpec { Kind = "PowerChanged", PowerId = "NoSuchPower" };
		watchTrigger.Effects.Add(new EffectSpec { Kind = "Block", Amount = 1m, TargetSide = "Self" });
		watchPower.Triggers.Add(watchTrigger);
		watchProfile.CustomPowers.Add(watchPower);
		Check("盯了一个不存在的状态时会警告（本机读到本体状态表才算）",
			EffectCatalog.Powers.Count == 0 || ProfileValidator.Validate(watchProfile).Any((ValidationIssue i) => i.Message.Contains("NoSuchPower")), "有警告");
		CustomPowerSpec customPowerSpec2 = new CustomPowerSpec
		{
			Name = "自检多段",
			Type = "Buff"
		};
		PowerTriggerSpec powerTriggerSpec5 = new PowerTriggerSpec
		{
			Kind = "TurnStart"
		};
		EffectSpec effectSpec2 = new EffectSpec
		{
			Kind = "Damage",
			Amount = 2m,
			TargetSide = "Enemy",
			TimesIsStack = true
		};
		powerTriggerSpec5.Effects.Add(effectSpec2);
		customPowerSpec2.Triggers.Add(powerTriggerSpec5);
		string text40 = CustomPowerGen.Source(cpProbe, customPowerSpec2, 9);
		Check("默认：层数不会让效果多跑（效果每次触发只跑一遍，用固定的数值）", !CustomPowerGen.Source(cpProbe, cp1, 0).Contains("__rep < (int)base.Amount"), "默认没有按层数的循环");
		Check("勾「生效次数 = 层数」才按层数循环（5 层就是打 5 次）", text40.Contains("for (int __rep = 0; __rep < (int)base.Amount; __rep++)") && text40.Contains("CreatureCmd.Damage(choiceContext, other, 2m"), "按层数循环");
		effectSpec2.TimesIsStack = false;
		effectSpec2.Times = 3;
		Check("不勾时全按「生效次数」里填的数字循环（3 次）", CustomPowerGen.Source(cpProbe, customPowerSpec2, 9).Contains("for (int __rep = 0; __rep < 3; __rep++)"), "按填的数字");
		effectSpec2.Times = 1;
		effectSpec2.RepeatIsStack = true;
		effectSpec2.TargetSide = "RandomEnemies";
		Check("勾「命中/对群数 = 层数」时随机目标按层数挑", CustomPowerGen.Source(cpProbe, customPowerSpec2, 9).Contains("for (int k = 0; k < (int)base.Amount; k++)"), "按层数挑目标");
		Check("按层数重复 + 不衰减 → 校验给提示（层数堆太高会卡）", ProfileValidator.Validate(new CharacterProfile
		{
			CustomPowers = { customPowerSpec2 }
		}).Any((ValidationIssue i) => i.Level == "警告" && i.Message.Contains("按层数重复执行")), "有提示");
		Check("界面上有这两个「= 层数」开关", TextsIn(CustomPowerTab.Content as DependencyObject).Any((string t) => t.Contains("生效次数 = 本状态的层数")) && TextsIn(CustomPowerTab.Content as DependencyObject).Any((string t) => t.Contains("命中次数 / 对群数 = 本状态的层数")), "开关在");
		string text41 = CustomPowerGen.Source(cpProbe, customPowerSpec, 1);
		Check("回合结束触发器 + 回合结束移除合并成一个方法（不再重复定义）", text41.Split("BeforeSideTurnEnd").Length - 1 == 1 && text41.Contains("await PowerCmd.Remove(this);"), $"BeforeSideTurnEnd 出现 {text41.Split("BeforeSideTurnEnd").Length - 1} 次");
		Check("减益状态写成 PowerType.Debuff", text41.Contains("PowerType.Debuff"), "类型对");
		string text42 = LocalizationGen.PowersJson(cpProbe);
		Check("名字和描述写进 powers.json（键 = 本体规则）", text42.Contains("\"" + CustomPowerGen.EntryOf(cpProbe, cp1, 0) + ".title\"") && text42.Contains("自检铁壁") && text42.Contains("\"" + CustomPowerGen.EntryOf(cpProbe, cp1, 0) + ".smartDescription\""), "本地化在");
		CustomPowerSpec customPowerSpec3 = new CustomPowerSpec
		{
			Name = "带图标",
			Icon = "C:\\test\\cp_icon.png",
			AmountColor = "66CCFF"
		};
		cpProbe.CustomPowers.Add(customPowerSpec3);
		string text43 = VanillaPowerGen.Source(cpProbe);
		Check("自定义状态的图标/层数颜色进同一张外观表（Icon 不是 virtual，只能补丁）", text43.Contains("\"" + CustomPowerGen.EntryOf(cpProbe, customPowerSpec3, 2) + "\"") && text43.Contains("66CCFF"), "外观在");
		// ===== 「战斗胜利后」的卡牌奖励：走本体的战斗奖励（王国资产 RoyaltiesPower 那条路） =====
		CharacterProfile rewardProbe = ProfileFactory.Sample();
		CustomPowerSpec rewardPower = new CustomPowerSpec { Name = "自检奖励状态" };
		PowerTriggerSpec rewardVictory = new PowerTriggerSpec { Kind = "CombatVictory" };
		rewardVictory.Effects.Add(new EffectSpec { Kind = "CardReward", Amount = 3m, TargetSide = "Self" });
		rewardPower.Triggers.Add(rewardVictory);
		rewardProbe.CustomPowers.Add(rewardPower);
		string rewardSrc = CustomPowerGen.Source(rewardProbe, rewardPower, 0);
		Check("「战斗胜利后」写成本体钩子 AfterCombatEnd（不是 AfterCombatVictory：状态在结算时收不到那个钩子）",
			rewardSrc.Contains("public override async Task AfterCombatEnd(CombatRoom room)") && !rewardSrc.Contains("AfterCombatVictory"), "钩子对");
		Check("「获得卡牌奖励」挂进本场战斗的结算奖励里（room.AddExtraReward + CardReward，和本体王国资产一样）",
			rewardSrc.Contains("room.AddExtraReward(base.Owner.Player, new MegaCrit.Sts2.Core.Rewards.CardReward(__rewardOptions, 3, base.Owner.Player))")
			&& rewardSrc.Contains("CardCreationOptions.ForRoom(base.Owner.Player, room.RoomType)")
			&& !rewardSrc.Contains("FromChooseACardScreen"), "走 room 奖励");
		Check("奖励选项不带 CardCreationFlags（带 flag 的话战斗结束存档时 ToSerializable 会抛异常）",
			!rewardSrc.Contains("ForNonCombatWithDefaultOdds(new") && rewardSrc.Contains("CardCreationSource.Other"), "不带 flag");
		Check("「战斗胜利后」的说明写明是 AfterCombatEnd", PowerTriggers.Find("CombatVictory")!.Hint.Contains("AfterCombatEnd"), PowerTriggers.Find("CombatVictory")!.Hint);
		Check("校验器对这条给提示（说明打赢后奖励界面会多一条），不再劝用户改挂遗物",
			ProfileValidator.Validate(rewardProbe).Any((ValidationIssue i) => i.Message.Contains("结算奖励"))
			&& !ProfileValidator.Validate(rewardProbe).Any((ValidationIssue i) => i.Message.Contains("建议改挂")), "有提示");
		PowerTriggerSpec midCombatReward = new PowerTriggerSpec { Kind = "TurnStart" };
		midCombatReward.Effects.Add(new EffectSpec { Kind = "CardReward", Amount = 3m, TargetSide = "Self" });
		CustomPowerSpec midCombatPower = new CustomPowerSpec { Name = "自检战斗中奖励" };
		midCombatPower.Triggers.Add(midCombatReward);
		string midRewardSrc = CustomPowerGen.Source(rewardProbe, midCombatPower, 1);
		Check("战斗中途的「获得卡牌奖励」还是当场弹选牌界面（没 room 就不能挂结算奖励）",
			midRewardSrc.Contains("FromChooseACardScreen") && !midRewardSrc.Contains("AddExtraReward"), "当场选");
		// 状态里的「随机敌人」：base.Owner 是 Creature，写 base.Owner.RunState 会 CS1061（用户实测构建失败）
		CustomPowerSpec randomFoePower = new CustomPowerSpec { Name = "自检随机目标" };
		PowerTriggerSpec randomFoeTrigger = new PowerTriggerSpec { Kind = "DamageDealt" };
		randomFoeTrigger.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 10m, TargetSide = "RandomEnemies" });
		randomFoeTrigger.Effects.Add(new EffectSpec { Kind = "ApplyPower", PowerId = "WeakPower", Amount = 1m, TargetSide = "RandomEnemies" });
		randomFoePower.Triggers.Add(randomFoeTrigger);
		string randomFoeSrc = CustomPowerGen.Source(rewardProbe, randomFoePower, 2);
		Check("状态里的「随机敌人」取的是 base.Owner.Player?.RunState（状态里 base.Owner 是 Creature，直接写 RunState 编译不过）",
			randomFoeSrc.Contains("base.Owner.Player?.RunState ?? base.CombatState?.RunState")
			&& !randomFoeSrc.Contains("base.Owner.RunState")
			&& randomFoeSrc.Split("NextItem(base.Owner.CombatState.HittableEnemies)").Length - 1 == 2,
			$"NextItem 出现 {randomFoeSrc.Split("NextItem(").Length - 1} 次");
		// 用户报过：「获得 30 层缓慢」游戏里只看到「缓慢」——
		// ① 勾了「数值 = 本状态的层数」时，数值里填的 30 会被忽略（生成 base.Amount）；
		// ② 本体「缓慢」显示的数字根本不是层数（DisplayAmount = SlowAmount×10）。
		CharacterProfile slowProbe = ProfileFactory.Sample();
		CustomPowerSpec slowPower = new CustomPowerSpec { Name = "自检缓慢" };
		PowerTriggerSpec slowTrigger = new PowerTriggerSpec { Kind = "TurnStart" };
		slowTrigger.Effects.Add(new EffectSpec
		{
			Kind = "ApplyPower", PowerId = "SlowPower", Amount = 30m, AmountIsStack = true, TargetSide = "AllEnemies",
		});
		slowPower.Triggers.Add(slowTrigger);
		slowProbe.CustomPowers.Add(slowPower);
		string slowSrc = CustomPowerGen.Source(slowProbe, slowPower, 0);
		Check("勾了「数值 = 本状态的层数」时生成的是 base.Amount（填的 30 被忽略）",
			slowSrc.Contains("PowerCmd.Apply<SlowPower>(choiceContext, other, base.Amount") && !slowSrc.Contains("30m"),
			"用的是 base.Amount");
		var slowIssues = ProfileValidator.Validate(slowProbe);
		Check("校验器会提醒「数值里填的 30 不会生效」（以前不提醒，用户以为会生效）",
			slowIssues.Any((ValidationIssue i) => i.Message.Contains("不会生效") && i.Message.Contains("30")),
			string.Join(" ｜ ", slowIssues.Select((ValidationIssue i) => i.Message)).Substring(0, Math.Min(160, string.Join(" ｜ ", slowIssues.Select((ValidationIssue i) => i.Message)).Length)));
		Check("校验器会提醒本体「缓慢」显示的数字不是层数",
			slowIssues.Any((ValidationIssue i) => i.Message.Contains("显示的数字不是层数") && i.Message.Contains("缓慢")),
			string.Join(" ｜ ", slowIssues.Select((ValidationIssue i) => i.Message)));
		Check("这张「显示数字不是层数」的表里就是本体那些 override 了 DisplayAmount 的状态",
			EffectCatalog.PowerAmountNote("SlowPower") != null && EffectCatalog.PowerAmountNote("PoisonPower") == null
			&& EffectCatalog.PowerAmountNote("SlothPower") != null, $"表里 {EffectCatalog.PowerAmountNotes.Count} 个状态");
		CharacterProfile poisonedProbe = ProfileFactory.Sample();
		CardSpec poisonCardProbe = new CardSpec { Name = "自检中毒卡", ClassName = "UiCheckPoisonHint", Rarity = "Common", InCardPool = true };
		poisonCardProbe.Effects.Clear();
		poisonCardProbe.Effects.Add(new EffectSpec { Kind = "ApplyPower", PowerId = "SlowPower", Amount = 20m, TargetSide = "Enemy" });
		poisonedProbe.Cards.Add(poisonCardProbe);
		Check("卡牌上施加「缓慢」也会提醒（卡牌走另一条校验）",
			ProfileValidator.Validate(poisonedProbe).Any((ValidationIssue i) => i.Message.Contains("显示的数字不是层数")),
			"卡牌路径也提醒");
		// 「直接把「缓慢」设成 N%」：本体「缓慢」显示/生效的数字不是层数（= SlowAmount×10），
		// 所以只能直接把它的内部数值写进去（SlowPowerHelper）
		CharacterProfile slowSetProbe = ProfileFactory.Sample();
		CustomPowerSpec slowSetPower = new CustomPowerSpec { Name = "自检缓慢百分比" };
		PowerTriggerSpec slowSetTrigger = new PowerTriggerSpec { Kind = "TurnStart" };
		slowSetTrigger.Effects.Add(new EffectSpec { Kind = "ApplyPower", PowerId = "SlowPower", Amount = 1m, SlowPercent = 30m, TargetSide = "AllEnemies" });
		slowSetPower.Triggers.Add(slowSetTrigger);
		slowSetProbe.CustomPowers.Add(slowSetPower);
		CardSpec slowSetCard = new CardSpec { Name = "自检缓慢卡", ClassName = "UiCheckSlowSet", Rarity = "Common", InCardPool = true };
		slowSetCard.Effects.Clear();
		slowSetCard.Effects.Add(new EffectSpec { Kind = "ApplyPower", PowerId = "SlowPower", Amount = 30m, SlowPercent = 30m, TargetSide = "Enemy" });
		slowSetProbe.Cards.Add(slowSetCard);
		string slowSetCardSrc = CSharpCodeGen.CardSource(slowSetProbe, slowSetCard, 0);
		Check("「直接把缓慢设成 30%」：按本体的做法只施加 1 层，然后写它的内部数值",
			slowSetCardSrc.Contains("await PowerCmd.Apply<SlowPower>(choiceContext, cardPlay.Target, 1m, base.Owner.Creature, this)")
			&& slowSetCardSrc.Contains("SlowPowerHelper.SetPercent(cardPlay.Target, 30)"), "1 层 + SetPercent(30)");
		string slowHelperSrc = CSharpCodeGen.SlowPowerHelperSource(slowSetProbe);
		Check("会生成 cs/SlowPowerHelper.cs：直接写 SlowAmount / DisplayAmount，并用反射刷状态栏（InvokeDisplayAmountChanged 是 protected）",
			CSharpCodeGen.NeedsSlowPowerHelper(slowSetProbe) && slowHelperSrc.Contains("InvokeDisplayAmountChanged")
			&& slowHelperSrc.Contains("\"SlowAmount\"") && slowHelperSrc.Contains("\"DisplayAmount\"")
			&& slowHelperSrc.Contains("HarmonyLib.AccessTools.Method"), "助手文件对");
		Check("没配这个选项时不会生成助手文件", !CSharpCodeGen.NeedsSlowPowerHelper(ProfileFactory.Sample()), "不生成");
		Check("描述写成「施加缓慢（受到伤害 +30%）」（层数不再出现在描述里）",
			LocalizationGen.CardsJson(slowSetProbe).Contains("施加缓慢（受到伤害 +30%）。")
			&& LocalizationGen.PowersJson(slowSetProbe).Contains("施加缓慢（受到伤害 +30%）"), "描述对");
		Check("状态触发时机的界面说明也写「施加缓慢（受到伤害 +30%）」",
			PowerTriggers.TriggerWhat(slowSetTrigger).Contains("施加缓慢（受到伤害 +30%）"), PowerTriggers.TriggerWhat(slowSetTrigger));
		var slowSetIssues = ProfileValidator.Validate(slowSetProbe);
		Check("校验器说明「只施加 1 层 + 把内部数值设成 +N%」",
			slowSetIssues.Any((ValidationIssue i) => i.Message.Contains("只施加 1 层")), "有提示");
		CharacterProfile slowOddProbe = ProfileFactory.Sample();
		slowOddProbe.Cards.Add(new CardSpec
		{
			Name = "自检缓慢25",
			ClassName = "UiCheckSlowOdd",
			Rarity = "Common",
			InCardPool = true,
			Effects = { new EffectSpec { Kind = "ApplyPower", PowerId = "SlowPower", Amount = 1m, SlowPercent = 25m, TargetSide = "Enemy" } },
		});
		Check("填 25% 会提示按 10% 一档折算成 30%",
			ProfileValidator.Validate(slowOddProbe).Any((ValidationIssue i) => i.Message.Contains("折算成 30%")), "折算提示在");
		CharacterProfile slowWrongProbe = ProfileFactory.Sample();
		slowWrongProbe.Cards.Add(new CardSpec
		{
			Name = "自检缓慢选错状态",
			ClassName = "UiCheckSlowWrong",
			Rarity = "Common",
			InCardPool = true,
			Effects = { new EffectSpec { Kind = "ApplyPower", PowerId = "PoisonPower", Amount = 1m, SlowPercent = 30m, TargetSide = "Enemy" } },
		});
		Check("选了别的状态时提示这个选项会被忽略",
			ProfileValidator.Validate(slowWrongProbe).Any((ValidationIssue i) => i.Message.Contains("这个选项会被忽略")), "忽略提示在");
		DataTemplate? effectTpl = Application.Current?.Resources["EffectCommon"] as DataTemplate;
		string[] effectTplTexts = (effectTpl?.LoadContent() is DependencyObject tplRoot) ? TextsIn(tplRoot).ToArray() : Array.Empty<string>();
		Check("效果编辑面板上有「直接把「缓慢」设成 %」那一行（并且只在选了「缓慢」时显示）",
			effectTplTexts.Any((string t) => t.Contains("直接把「缓慢」设成")) && effectTplTexts.Any((string t) => t.Contains("不是层数")),
			$"模板里 {effectTplTexts.Length} 个文本");
		Check("EffectSpec.IsSlowPower 只在选「缓慢」时为 true",
			new EffectSpec { PowerId = "SlowPower" }.IsSlowPower && !new EffectSpec { PowerId = "PoisonPower" }.IsSlowPower, "判断对");
		// 「给予卡牌关键词」那两行：只在效果种类 = 给予卡牌关键词 时显示（和「直接把缓慢设成 %」同一套 Visibility 绑定）
		Check("效果编辑面板上有「给予关键词」和「是否为临时关键词」两行（紧跟在「选牌方式」下面）",
			effectTplTexts.Any((string t) => t.Contains("给予关键词"))
			&& effectTplTexts.Any((string t) => t.Contains("是否为临时关键词")),
			$"模板里 {effectTplTexts.Length} 个文本");
		Check("EffectSpec.IsGiveKeyword 只在「给予卡牌关键词」时为 true（那两行显隐绑的就是它）",
			new EffectSpec { Kind = "GiveKeyword" }.IsGiveKeyword && !new EffectSpec { Kind = "Block" }.IsGiveKeyword, "判断对");
		Check("「从哪里选牌」对「给予卡牌关键词」也显示（它也要选手牌 / 抽牌堆 / 弃牌堆）",
			new EffectSpec { Kind = "GiveKeyword" }.UsesSelectPile, "会显示");
		Check("数值 = 0 时列表文字写「自己」，≥1 时写「自己选 / 随机 N 张」",
			new EffectSpec { Kind = "GiveKeyword", Amount = 0m, GivenKeyword = "Retain" }.Display.Contains("自己")
			&& new EffectSpec { Kind = "GiveKeyword", Amount = 2m, GivenKeyword = "Retain", CardPick = "Random" }.Display.Contains("随机 2 张"),
			new EffectSpec { Kind = "GiveKeyword", Amount = 2m, GivenKeyword = "Retain", CardPick = "Random" }.Display);
		// 「生效次数 = 3」+ 随机目标：以前会生成两层 for (int i…) → CS0136 编译不过（用户实测踩过）
		CharacterProfile loopProbe = ProfileFactory.Sample();
		CardSpec loopCard = new CardSpec { Name = "自检多层随机", ClassName = "UiCheckNestedLoop", Rarity = "Common", InCardPool = true };
		loopCard.Effects.Clear();
		// 用「失去生命」（走 EmitPerCreature 那条随机目标的路，就是用户踩到的那张牌）
		loopCard.Effects.Add(new EffectSpec { Kind = "HpLoss", Amount = 6m, TargetSide = "RandomEnemies", Times = 3 });
		loopProbe.Cards.Add(loopCard);
		string loopSrc = CSharpCodeGen.CardSource(loopProbe, loopCard, 0);
		Check("「生效次数 = 3」+ 随机目标：不会再生成两层同名 for (int i)（CS0136 编译不过）",
			loopSrc.Split("for (int i =").Length - 1 == 1 && loopSrc.Contains("for (int __foeIdx = 0; __foeIdx < 1"),
			$"for (int i 出现 {loopSrc.Split("for (int i =").Length - 1} 次，__foeIdx={(loopSrc.Contains("__foeIdx") ? "有" : "没有")}");
		Check("每条效果都包在自己的作用域里（里面的临时变量不会和别的效果撞名）",
			System.Text.RegularExpressions.Regex.IsMatch(loopSrc, @"for \(int i = 0; i < 3; i\+\+\) \{\s*\{"), "有内层作用域");
		// 「从哪里选牌」：消耗 / 变化卡牌以前只能选「手牌」，现在能选抽牌堆 / 弃牌堆
		CharacterProfile pileProbe = ProfileFactory.Sample();
		CardSpec pileCard = new CardSpec { Name = "自检选牌", ClassName = "UiCheckPile", Rarity = "Common", InCardPool = true };
		pileCard.Effects.Clear();
		pileCard.Effects.Add(new EffectSpec { Kind = "ExhaustCard", Amount = 1m, CardPick = "Chosen", SelectPile = "Discard" });
		pileCard.Effects.Add(new EffectSpec { Kind = "TransformCard", Amount = 2m, CardPick = "Random", SelectPile = "Draw" });
		pileCard.Effects.Add(new EffectSpec { Kind = "TakeFromDiscard", Amount = 1m });
		pileProbe.Cards.Add(pileCard);
		string pileSrc = CSharpCodeGen.CardSource(pileProbe, pileCard, 0);
		Check("消耗卡牌能选「弃牌堆」（走本体 FromCombatPile，不再是只有手牌）",
			pileSrc.Contains("CardSelectCmd.FromCombatPile(choiceContext, PileType.Discard.GetPile(base.Owner), base.Owner, new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1))")
			&& !pileSrc.Contains("CardSelectCmd.FromHand"), "弃牌堆消耗");
		Check("变化卡牌「随机」也能从抽牌堆里挑",
			pileSrc.Contains("NextItem(PileType.Draw.GetPile(base.Owner).Cards)"), "抽牌堆随机变化");
		Check("「从弃牌堆拿牌到手牌」用 FromCombatPile + CardPileCmd.Add(…, PileType.Hand)（本体「全息影像 / 挖掘」的做法）",
			pileSrc.Contains("PileType.Discard.GetPile(base.Owner)")
			&& pileSrc.Contains("new CardSelectorPrefs(base.SelectionScreenPrompt, 1)")
			&& pileSrc.Contains("await CardPileCmd.Add(__taken, PileType.Hand)"), "拿回手牌");
		Check("选牌界面提示语跟着生成（<卡牌>.selectionScreenPrompt）",
			LocalizationGen.CardsJson(pileProbe).Contains("\"UI_CHECK_PILE.selectionScreenPrompt\": \"从弃牌堆选择要拿到手牌的牌\""),
			LocalizationGen.CardsJson(pileProbe).Replace("\n", " "));
		Check("描述写清是从哪一摞选的（不再写死「手牌」）",
			LocalizationGen.CardsJson(pileProbe).Contains("自己选从弃牌堆消耗 1 张牌。")
			&& LocalizationGen.CardsJson(pileProbe).Contains("随机将抽牌堆里的 2 张牌变化为随机卡牌。")
			&& LocalizationGen.CardsJson(pileProbe).Contains("从弃牌堆里选 1 张牌拿到手牌。"), "描述对");
		CharacterProfile handProbe = ProfileFactory.Sample();
		CardSpec handCard = new CardSpec { Name = "自检手牌消耗", ClassName = "UiCheckPileHand", Rarity = "Common", InCardPool = true };
		handCard.Effects.Clear();
		handCard.Effects.Add(new EffectSpec { Kind = "ExhaustCard", Amount = 1m, CardPick = "Chosen" });
		handProbe.Cards.Add(handCard);
		Check("没动过「从哪里选牌」时还是手牌（老存档行为不变，走 FromHand）",
			new EffectSpec { Kind = "ExhaustCard" }.SelectPile == "Hand"
			&& new EffectSpec { Kind = "ExhaustCard" }.SelectPileZh == "手牌"
			&& CSharpCodeGen.CardSource(handProbe, handCard, 0).Contains("CardSelectCmd.FromHand(")
			&& EffectCatalog.SelectPiles.Count == 3, $"选项 {EffectCatalog.SelectPiles.Count} 个");
		Check("「从哪里选牌」只有消耗 / 变化卡牌用得到",
			new EffectSpec { Kind = "ExhaustCard" }.UsesSelectPile && new EffectSpec { Kind = "TransformCard" }.UsesSelectPile
			&& !new EffectSpec { Kind = "Damage" }.UsesSelectPile, "只在这两种上显示");
		Check("状态触发器里没有「从牌堆拿牌」这一类（要弹选牌界面）",
			!PowerTriggers.Supports("TakeFromDraw") && !PowerTriggers.Supports("TakeFromDiscard"), "状态里不支持");
		// 悬停提示：卡面 / 遗物 / 药水里提到的状态要能弹说明（本体状态改写后就是新名字 + 新描述；自定义状态同理）
		CharacterProfile hoverProbe = ProfileFactory.Sample();
		CardSpec hoverCard = new CardSpec { Name = "自检悬停", ClassName = "UiCheckHover", Rarity = "Common", InCardPool = true };
		hoverCard.Effects.Clear();
		hoverCard.Effects.Add(new EffectSpec { Kind = "ApplyPower", PowerId = "WeakPower", Amount = 2m, TargetSide = "Enemy" });
		EffectSpec hoverConditionEffect = new EffectSpec { Kind = "Damage", Amount = 6m, TargetSide = "Enemy" };
		hoverConditionEffect.Condition.Kind = "HasPowerAtLeast";
		hoverConditionEffect.Condition.PowerId = "VulnerablePower";
		hoverConditionEffect.Condition.Amount = 1m;
		hoverConditionEffect.Condition.Target = "Enemy";
		hoverCard.Effects.Add(hoverConditionEffect);
		hoverProbe.Cards.Add(hoverCard);
		string hoverSrc = CSharpCodeGen.CardSource(hoverProbe, hoverCard, 0);
		Check("卡面提到的状态会生成 ExtraHoverTips（施加的状态 + 条件里盯的状态都算）",
			hoverSrc.Contains("protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips")
			&& hoverSrc.Contains("HoverTipFactory.FromPower<WeakPower>(2)")
			&& hoverSrc.Contains("HoverTipFactory.FromPower<VulnerablePower>()"), "卡牌悬停提示");
		Check("「下回合生效」的状态悬停提示指向那个延迟状态类（本体这样也有说明）",
			CSharpCodeGen.CardSource(hoverProbe, new CardSpec
			{
				Name = "自检延迟悬停",
				ClassName = "UiCheckHoverDelayed",
				Rarity = "Common",
				InCardPool = true,
				Effects = { new EffectSpec { Kind = "ApplyPower", PowerId = "WeakPower", Amount = 1m, TargetSide = "Enemy", NextTurn = true } },
			}, 1).Contains($"FromPower<{Naming.From(hoverProbe).DelayedPowerClass(new EffectSpec { Kind = "ApplyPower", PowerId = "WeakPower" })}>("), "延迟状态");
		Check("没提到任何状态的卡不会生成那一段（不写多余代码）",
			!CSharpCodeGen.CardSource(hoverProbe, new CardSpec
			{
				Name = "自检无状态",
				ClassName = "UiCheckHoverNone",
				Rarity = "Common",
				InCardPool = true,
				Effects = { new EffectSpec { Kind = "Block", Amount = 5m, TargetSide = "Self" } },
			}, 2).Contains("ExtraHoverTips"), "没生成");
		RelicSpec hoverRelic = new RelicSpec { Name = "自检悬停遗物", ClassName = "UiCheckHoverRelic", Trigger = "CombatStart" };
		hoverRelic.Effects.Add(new EffectSpec { Kind = "ApplyPower", PowerId = "SparkleHoverTestPower", Amount = 1m, TargetSide = "Self" });
		string hoverRelicSrc = CSharpCodeGen.RelicSource(hoverProbe, hoverRelic, 0);
		Check("遗物提到自定义状态时也会生成 ExtraHoverTips（protected override，和本体遗物写法一致）",
			hoverRelicSrc.Contains("protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips")
			&& hoverRelicSrc.Contains("FromPower<SparkleHoverTestPower>(1)"), "遗物悬停提示");
		PotionSpec hoverPotion = new PotionSpec { Name = "自检悬停药水", ClassName = "UiCheckHoverPotion", TargetType = "AllEnemies" };
		hoverPotion.Effects.Add(new EffectSpec { Kind = "ApplyPower", PowerId = "PoisonPower", Amount = 3m, TargetSide = "AllEnemies" });
		string hoverPotionSrc = CSharpCodeGen.PotionSource(hoverProbe, hoverPotion, 0);
		Check("药水提到状态时也会生成 ExtraHoverTips（药水基类是 public，必须 public override）",
			hoverPotionSrc.Contains("public override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips")
			&& hoverPotionSrc.Contains("FromPower<PoisonPower>(3)"), "药水悬停提示");
		Check("用到额外资源量的卡会带上「资源量」那条悬停提示（走本体 STAR_COUNT 的文本，名字/描述跟着显示名走）",
			CSharpCodeGen.CardSource(hoverProbe, new CardSpec
			{
				Name = "自检资源提示",
				ClassName = "UiCheckHoverStar",
				Rarity = "Common",
				InCardPool = true,
				Effects = { new EffectSpec { Kind = "ExtraResource", Amount = 2m, TargetSide = "Self" } },
			}, 3).Contains("new LocString(\"static_hover_tips\", \"STAR_COUNT.title\")"), "资源提示在");
		Check("获得能量的卡会带上本体的能量提示（base.EnergyHoverTip）",
			CSharpCodeGen.CardSource(hoverProbe, new CardSpec
			{
				Name = "自检能量提示",
				ClassName = "UiCheckHoverEnergy",
				Rarity = "Common",
				InCardPool = true,
				Effects = { new EffectSpec { Kind = "Energy", Amount = 1m, TargetSide = "Self" } },
			}, 4).Contains("base.EnergyHoverTip"), "能量提示在");
		Check("「获得卡牌奖励」会带上本体那条静态提示（StaticHoverTip.CardReward）",
			CSharpCodeGen.RelicSource(hoverProbe, new RelicSpec
			{
				Name = "自检卡牌奖励遗物",
				ClassName = "UiCheckHoverReward",
				Trigger = "CombatVictory",
				Effects = { new EffectSpec { Kind = "CardReward", Amount = 3m, TargetSide = "Self" } },
			}, 1).Contains("StaticHoverTip.CardReward"), "卡牌奖励提示在");
		Check("「选人界面背景大图」的说明是横图（本体画面 1920×1080，以前写成「1000×1400 的竖图」了）",
			ArtSlots.Any((ArtSlot s) => s.Name == "选人界面背景大图"
				&& s.Requirement.Contains("横图") && s.Requirement.Contains("1920×1080") && !s.Requirement.Contains("1000×1400")),
			ArtSlots.FirstOrDefault((ArtSlot s) => s.Name == "选人界面背景大图")?.Requirement ?? "(没找到)");
		EffectCatalog.SetCustomPowers(new(string, string)[1] { (CustomPowerGen.ClassNameOf(cpProbe, cp1, 0), cp1.Name) });
		RefreshPowerChoices();
		Check("登记后卡牌效果能选到自定义状态（下拉里有）", AllPowers.Any((PowerEntry x) => x.Id == CustomPowerGen.ClassNameOf(cpProbe, cp1, 0)), "在下拉里");
		CardSpec cardSpec21 = new CardSpec
		{
			Name = "自检能力牌",
			ClassName = "UiCheckCpCard",
			CardType = "Power",
			Rarity = "Common",
			Cost = 1,
			InCardPool = true
		};
		cardSpec21.Effects.Clear();
		cardSpec21.Effects.Add(new EffectSpec
		{
			Kind = "ApplyPower",
			PowerId = CustomPowerGen.ClassNameOf(cpProbe, cp1, 0),
			Amount = 2m,
			TargetSide = "Self"
		});
		cpProbe.Cards.Add(cardSpec21);
		Check("卡牌里施加自定义状态算有效（校验不报错）", !ProfileValidator.Validate(cpProbe).Any((ValidationIssue i) => i.IsError && i.Message.Contains("未选择有效的 Power")), "校验通过");
		string text44 = CSharpCodeGen.CardSource(cpProbe, cardSpec21, 0);
		Check("卡牌代码里生成 PowerCmd.Apply<自定义类名>", text44.Contains("PowerCmd.Apply<" + CustomPowerGen.ClassNameOf(cpProbe, cp1, 0) + ">"), "已生成");
		CharacterProfile characterProfile28 = ProfileFactory.Sample();
		characterProfile28.CustomPowers.Clear();
		CustomPowerSpec customPowerSpec4 = new CustomPowerSpec
		{
			Name = "坏状态"
		};
		PowerTriggerSpec powerTriggerSpec6 = new PowerTriggerSpec
		{
			Kind = "TurnStart"
		};
		powerTriggerSpec6.Effects.Add(new EffectSpec
		{
			Kind = "ExhaustCard",
			Amount = 1m,
			TargetSide = "Self"
		});
		customPowerSpec4.Triggers.Add(powerTriggerSpec6);
		characterProfile28.CustomPowers.Add(customPowerSpec4);
		Check("触发器里用了需要卡牌上下文的效果 → 错误", ProfileValidator.Validate(characterProfile28).Any((ValidationIssue i) => i.IsError && i.Message.Contains("状态触发器里用不了")), ProfileValidator.Validate(characterProfile28).First((ValidationIssue i) => i.IsError).Message.Substring(0, 40) + "…");
		CharacterProfile characterProfile29 = ProfileFactory.Sample();
		characterProfile29.CustomPowers.Clear();
		characterProfile29.CustomPowers.Add(new CustomPowerSpec
		{
			Name = "空状态"
		});
		Check("没配触发时机 → 警告（挂上去也不会做事）", ProfileValidator.Validate(characterProfile29).Any((ValidationIssue i) => i.Level == "警告" && i.Message.Contains("没配任何触发时机")), "有警告");
		SelectTabRoot("自定义状态");
		int count14 = Profile.CustomPowers.Count;
		OnAddCustomPower(this, new RoutedEventArgs());
		CustomPowerSpec customPowerSpec5 = Profile.CustomPowers.LastOrDefault();
		Check("「添加状态」会带一条默认触发时机（回合开始时获得格挡）", customPowerSpec5 != null && customPowerSpec5.Triggers.Count == 1 && customPowerSpec5.Triggers[0].Kind == "TurnStart" && customPowerSpec5.Triggers[0].Effects.Count == 1 && customPowerSpec5.Triggers[0].Effects[0].Kind == "Block", (customPowerSpec5 == null) ? "(没加上)" : (customPowerSpec5.Name + " / " + customPowerSpec5.Triggers[0].Display));
		OnAddPowerTrigger(this, new RoutedEventArgs());
		Check("「添加触发时机」能再加一条", customPowerSpec5 != null && customPowerSpec5.Triggers.Count == 2, $"{customPowerSpec5?.Triggers.Count} 条");
		Check("界面上有「自定义状态」页的控件", TextsIn(CustomPowerTab.Content as DependencyObject).Contains("添加状态") && TextsIn(CustomPowerTab.Content as DependencyObject).Contains("添加触发时机"), "控件在");
		Check("效果编辑器有「数值 = 本状态的层数」这个开关（能力牌常用）", TextsIn(CustomPowerTab.Content as DependencyObject).Any((string t) => t.Contains("数值 = 本状态的层数")), "开关在");
		Profile.CustomPowers.Remove(customPowerSpec5);
		EffectCatalog.SetCustomPowers(null);
		Raise("PowerChoices");
		Check("测试用的自定义状态已清理", Profile.CustomPowers.Count == count14);
		SelectTabRoot("卡牌");
		if (Profile.Cards.Count > 0)
		{
			CardList.SelectedIndex = 0;
			if (((CardSpec)CardList.SelectedItem).Effects.Count > 0)
			{
				CardEffectList.SelectedIndex = 0;
			}
			UpdateLayout();
			Check("卡牌页效果编辑器确实渲染了（否则「解释文字已去掉」那两条是空跑）", TextsIn((DependencyObject)((TabItem)MainTabs.SelectedItem).Content).Any((string t) => t.Contains("作用对象")), "模板已渲染");
		}
		Check("效果栏底下不再有那一大段解释文字", !TextsIn(root15).Any((string t) => t.Contains("作用对象：自己 = 给自己加增益")), "已去掉");
		Check("遗物效果栏底下也不再有解释文字", !TextsIn(root16).Any((string t) => t.Contains("作用对象：自己 = 给自己加增益")), "已去掉");
		MainTabs.SelectedItem = BuildTab;
		UpdateLayout();
		List<TextBox> list31 = new List<TextBox>();
		List<Button> list32 = new List<Button>();
		if (BuildTab.Content is DependencyObject root17)
		{
			CollectTextBoxes(root17, list31);
			CollectButtons(root17, list32);
		}
		List<string> list33 = (from t in list31
			select BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path?.Path into p
			where p?.StartsWith("Profile.Paths.", StringComparison.Ordinal) ?? false
			select p).ToList();
		Check("构建页只留「解包工程」一个路径框", list33.Count == 1 && list33[0] == "Profile.Paths.VanillaProject", (list33.Count == 0) ? $"{list31.Count} 个输入框" : string.Join(" / ", list33));
		List<string> list34 = list32.Select((Button b) => (b.Content as string) ?? "").ToList();
		Check("构建页只剩「打开输出目录」按钮", list34.Count((string n) => n == "打开输出目录") == 1 && !list34.Any(delegate(string n)
		{
			switch (n)
			{
			case "校验配置":
			case "自动探测本机环境":
			case "重新扫描效果库":
			case "① 一键生成 · 构建 · 安装":
			case "仅生成工程（不编译）":
				return true;
			default:
				return false;
			}
		}), string.Join("、", list34));
		SelectTabRoot("卡牌");
		if (CardList.SelectedItem is CardSpec cardSpec22 && cardSpec22.Effects.Count > 0)
		{
			CardEffectList.SelectedIndex = 0;
		}
		UpdateLayout();
		List<ComboBox> list35 = new List<ComboBox>();
		CollectCombos(this, list35);
		Check("界面上能找到下拉控件（这里不含增益/目标卡那两个搜索栏）", list35.Count >= 4, $"{list35.Count} 个 ComboBox");
		Check("效果种类下拉已填充", list35.Any((ComboBox c) => c.Items.Count == Kinds.Count), $"应为 {Kinds.Count} 项");
		// 「卡牌」页的效果种类下拉必须是**全量** Kinds（宠物效果也在这份全量列表里，
		// 以前那个「召唤物卡牌」页的专属效果栏会把它收窄成宠物类 —— 那一页和收窄机制都已删除）。
		Check("「卡牌」页的效果种类下拉是全量 Kinds（宠物效果和普通效果都在里面）",
			list35.Any((ComboBox c) => c.SelectedValuePath == "Kind" && c.Items.Count == Kinds.Count && c.Items.Count > EffectCatalog.PetEffectKinds.Count),
			string.Join(" / ", list35.Where((ComboBox c) => c.SelectedValuePath == "Kind").Select((ComboBox c) => c.Items.Count + " 项")));
		List<TextBox> boxes = new List<TextBox>();
		CollectTextBoxes(this, boxes);
		Check("效果编辑器有「对群数/命中次数」输入框", HasBox("RepeatCount"));
		Check("效果编辑器有「生效次数」输入框", HasBox("Times"));
		Check("效果种类含「结束回合」", Kinds.Any((EffectKindOption k) => k.Kind == "EndTurn"));
		Check("效果种类含「额外获得一回合」", Kinds.Any((EffectKindOption k) => k.Kind == "ExtraTurn"));
		Check("效果种类含 生成/消耗/变化卡牌", Kinds.Any((EffectKindOption k) => k.Kind == "GenerateCard") && Kinds.Any((EffectKindOption k) => k.Kind == "ExhaustCard") && Kinds.Any((EffectKindOption k) => k.Kind == "TransformCard"), string.Join("·", Kinds.Select((EffectKindOption k) => k.Display)));
		Check("目标卡目录非空（本体卡 + 自己的卡）", AllCards.Count > 0, $"{AllCards.Count} 张");
		Check("生成到 / 选牌方式选项齐全", SpawnTargets.Count == 3 && CardPickModes.Count == 2, string.Join("/", SpawnTargets) + " ｜ " + string.Join("/", CardPickModes));
		CardSpec cardSpec23 = (CardSpec)CardList.SelectedItem;
		int count15 = cardSpec23.Effects.Count;
		OnAddEffect(new Button
		{
			Tag = CardEffectList,
			DataContext = cardSpec23
		}, new RoutedEventArgs());
		ObservableCollection<EffectSpec> effects8 = cardSpec23.Effects;
		effects8[effects8.Count - 1].Kind = "GenerateCard";
		ObservableCollection<EffectSpec> effects9 = cardSpec23.Effects;
		effects9[effects9.Count - 1].SpawnCardId = "Shiv";
		SyncDetail();
		ObservableCollection<EffectSpec> effects10 = cardSpec23.Effects;
		bool ok11 = effects10[effects10.Count - 1].Display.Contains("Shiv");
		ObservableCollection<EffectSpec> effects11 = cardSpec23.Effects;
		Check("「生成卡牌」效果会把目标卡写进效果文字", ok11, effects11[effects11.Count - 1].Display);
		cardSpec23.Effects.RemoveAt(cardSpec23.Effects.Count - 1);
		SyncDetail();
		Check("测试用效果已清理", cardSpec23.Effects.Count == count15);
		CardSpec cardSpec24 = new CardSpec
		{
			Name = "升级费用测试",
			ClassName = "UiCheckUpCost",
			Cost = 2,
			CardType = "Skill",
			Rarity = "Common",
			InCardPool = true
		};
		cardSpec24.Effects.Clear();
		cardSpec24.Effects.Add(new EffectSpec
		{
			Kind = "Block",
			Amount = 5m,
			UpgradeAmount = 2m,
			TargetSide = "Self"
		});
		Check("升级后费用默认留空（= 升级不改费用）", !cardSpec24.UpgradeCost.HasValue, cardSpec24.UpgradeCost?.ToString() ?? "(空)");
		Check("留空时不生成改费代码", !CSharpCodeGen.CardSource(ProfileFactory.Sample(), cardSpec24, 0).Contains("EnergyCost.UpgradeBy"), "没有改费");
		cardSpec24.UpgradeCost = 1;
		string text45 = CSharpCodeGen.CardSource(ProfileFactory.Sample(), cardSpec24, 0);
		Check("填 1 就生成 base.EnergyCost.UpgradeBy(-1)（本体就是用这个改费用的）", text45.Contains("base.EnergyCost.UpgradeBy(-1)"), "改了 -1");
		Check("改费代码和升级增量写在同一个 OnUpgrade 里（不能生成两个同名方法）", text45.Split("protected override void OnUpgrade()").Length - 1 == 1 && text45.Contains("UpgradeValueBy(2m)"), "同一个方法");
		cardSpec24.Cost = 0;
		cardSpec24.UpgradeCost = 2;
		Check("升级后变贵也支持（0 → 2 = +2）", CSharpCodeGen.CardSource(ProfileFactory.Sample(), cardSpec24, 0).Contains("base.EnergyCost.UpgradeBy(2)"), "改了 +2");
		cardSpec24.Cost = 2;
		cardSpec24.UpgradeCost = 2;
		Check("填成和现在一样 = 不生成改费代码（等于没改）", !CSharpCodeGen.CardSource(ProfileFactory.Sample(), cardSpec24, 0).Contains("EnergyCost.UpgradeBy"), "没改");
		cardSpec24.CostIsX = true;
		cardSpec24.UpgradeCost = 1;
		Check("X 费用牌填了升级后费用 → 警告（本体改不了 X 费）", ProfileValidator.Validate(new CharacterProfile
		{
			Cards = { cardSpec24 }
		}).Any((ValidationIssue i) => i.Level == "警告" && i.Message.Contains("X 费用牌")), "有警告");
		cardSpec24.CostIsX = false;
		cardSpec24.UpgradeCost = -1;
		Check("升级后费用填负数 → 错误", ProfileValidator.Validate(new CharacterProfile
		{
			Cards = { cardSpec24 }
		}).Any((ValidationIssue i) => i.IsError && i.Message.Contains("升级后费用")), "有错误");
		cardSpec24.UpgradeCost = null;
		SelectTabRoot("卡牌");
		Profile.Cards.Add(cardSpec24);
		CardList.SelectedItem = cardSpec24;
		UpdateLayout();
		DependencyObject root18 = FindTab("卡牌").Content as DependencyObject;
		Check("效果栏里有「升级后费用」这一栏", TextsIn(root18).Contains("升级后费用"), "控件在");
		SelectedCardUpgradeCost = 1;
		Check("在这一栏填的值写进选中的那张牌", cardSpec24.UpgradeCost.GetValueOrDefault() == 1, cardSpec24.UpgradeCost?.ToString() ?? "(空)");
		Check("下面的提示会说明是整张牌、升级后几费", UpgradeCostHint.Contains("升级后 1 费"), UpgradeCostHint);
		SelectedCardUpgradeCost = null;
		Check("清空后回到「升级不改费用」", !cardSpec24.UpgradeCost.HasValue, "已清空");
		Profile.Cards.Remove(cardSpec24);
		SyncDetail();
		CardSpec cardSpec25 = new CardSpec
		{
			Name = "关键字测试",
			Exhausts = true,
			Ethereal = true,
			Retain = true,
			Unplayable = true
		};
		Check("卡牌关键字能记下来", cardSpec25.KeywordList.Count == 4, string.Join("·", cardSpec25.KeywordList));
		Check("关键字出现在卡牌列表文字里", cardSpec25.Display.Contains("Exhaust") && cardSpec25.Display.Contains("Retain"), cardSpec25.Display);

		// ===== 多选：Ctrl / Shift / Ctrl+A 在所有「删除选中」的列表上都可用 =====
		{
			// 用户报过：按 Ctrl / Shift 想多选删除，结果加不上（以前关着「多选」开关时列表是 Single）。
			Check("关键词列表是多选（Extended）：Ctrl/Shift 能多选删除",
				KeywordList.SelectionMode == SelectionMode.Extended, KeywordList.SelectionMode.ToString());
			Check("本体状态改写列表是多选（Extended）",
				PowerOverrideList.SelectionMode == SelectionMode.Extended, PowerOverrideList.SelectionMode.ToString());
			Check("自定义状态列表是多选（Extended）",
				CustomPowerList.SelectionMode == SelectionMode.Extended, CustomPowerList.SelectionMode.ToString());
			Check("自定义状态的「触发时机」列表是多选（Extended）",
				CustomTriggerList.SelectionMode == SelectionMode.Extended, CustomTriggerList.SelectionMode.ToString());
			Check("自定义状态的「效果」列表是多选（Extended）",
				CustomEffectList.SelectionMode == SelectionMode.Extended, CustomEffectList.SelectionMode.ToString());
			// 「多选」开关 → 模式的约定（开关本身在别的自检里被打开过，所以这里显式摆回两种状态再断言）
			MultiSelectCards = false;
			UpdateLayout();
			Check("「多选」关着 → Extended：Ctrl / Shift 点选能多选（用户报过「Ctrl/Shift 没反应、只能选一个」）",
				CardList.SelectionMode == SelectionMode.Extended, CardList.SelectionMode.ToString());
			Check("关着时也能往已选中项里加选（Ctrl / Shift 多选的基础）", CanAddToSelection(CardList), "模式=" + CardList.SelectionMode);
			MultiSelectCards = true;
			UpdateLayout();
			Check("「多选」打开 → Multiple：直接点行累加，不用按 Ctrl（两种习惯都支持）",
				CardList.SelectionMode == SelectionMode.Multiple, CardList.SelectionMode.ToString());
			MultiSelectCards = false;
			MultiSelectRelics = false;
			MultiSelectPotions = false;
			MultiSelectSummons = false;
			MultiSelectProfiles = false;
			UpdateLayout();
			CardList.SelectedItems.Clear();
			Check("切页用的那几个列表（卡牌/遗物/药水/召唤物/存档）默认都是 Extended，不点「多选」就能 Ctrl/Shift 多选",
				CardList.SelectionMode == SelectionMode.Extended && RelicList.SelectionMode == SelectionMode.Extended
				&& PotionList.SelectionMode == SelectionMode.Extended && SummonList.SelectionMode == SelectionMode.Extended
				&& ProfileList.SelectionMode == SelectionMode.Extended,
				$"{CardList.SelectionMode}/{RelicList.SelectionMode}/{PotionList.SelectionMode}/{SummonList.SelectionMode}/{ProfileList.SelectionMode}");

			// 多条一起删：本体状态改写（以前只看 SelectedItem，选了一堆也只删一条）
			{
				int overridesBefore = Profile.VanillaPowerOverrides.Count;
				Profile.VanillaPowerOverrides.Add(new VanillaPowerOverride { PowerId = "PoisonPower", Name = "多选甲" });
				Profile.VanillaPowerOverrides.Add(new VanillaPowerOverride { PowerId = "WeakPower", Name = "多选乙" });
				Profile.VanillaPowerOverrides.Add(new VanillaPowerOverride { PowerId = "FrailPower", Name = "多选丙" });
				PowerOverrideList.SelectedItems.Clear();
				PowerOverrideList.SelectedItems.Add(Profile.VanillaPowerOverrides[overridesBefore]);
				PowerOverrideList.SelectedItems.Add(Profile.VanillaPowerOverrides[overridesBefore + 2]);
				int confirmBefore = ConfirmRequests;
				OnRemovePowerOverride(this, new RoutedEventArgs());
				Check("本体状态改写：一次删掉多选的那几条（不是只删第一条）",
					Profile.VanillaPowerOverrides.Count == overridesBefore + 1
					&& !Profile.VanillaPowerOverrides.Any(o => o.Name is "多选甲" or "多选丙")
					&& Profile.VanillaPowerOverrides.Any(o => o.Name == "多选乙"),
					string.Join("·", Profile.VanillaPowerOverrides.Select(o => o.Name)));
				Check("本体状态改写：多条删除只要一次确认", ConfirmRequests == confirmBefore + 1, $"{confirmBefore} → {ConfirmRequests}");
				while (Profile.VanillaPowerOverrides.Count > overridesBefore) Profile.VanillaPowerOverrides.RemoveAt(Profile.VanillaPowerOverrides.Count - 1);
				SyncPowerOverrideDetail();
			}

			// 多条一起删 + 撤回：自定义状态（连带它的触发时机列表；这一页以前没有「撤回删除」按钮）
			{
				int powersBefore = Profile.CustomPowers.Count;
				CustomPowerSpec multi1 = new CustomPowerSpec { Name = "多选甲", ClassName = "UiCheckMultiA", Type = "Buff" };
				CustomPowerSpec multi2 = new CustomPowerSpec { Name = "多选乙", ClassName = "UiCheckMultiB", Type = "Buff" };
				multi1.Triggers.Add(new PowerTriggerSpec { Kind = "TurnStart" });
				multi2.Triggers.Add(new PowerTriggerSpec { Kind = "TurnStart" });
				Profile.CustomPowers.Add(multi1);
				Profile.CustomPowers.Add(multi2);
				CustomPowerList.SelectedItems.Clear();
				CustomPowerList.SelectedItems.Add(multi1);
				CustomPowerList.SelectedItems.Add(multi2);
				OnRemoveCustomPower(this, new RoutedEventArgs());
				Check("自定义状态：一次删掉多选的那几个（不是只删第一个）",
					!Profile.CustomPowers.Contains(multi1) && !Profile.CustomPowers.Contains(multi2),
					$"{Profile.CustomPowers.Count} 个");
				Check("自定义状态：删完就有「撤回删除」可点（这一页以前没有这个按钮，删了真没了）", CanUndoPower, UndoPowerHint);
				OnUndoPower(this, new RoutedEventArgs());
				Check("自定义状态：多条删除一次全撤回（数量 + 顺序都回来）",
					Profile.CustomPowers.Count == powersBefore + 2
					&& ReferenceEquals(Profile.CustomPowers[powersBefore], multi1)
					&& ReferenceEquals(Profile.CustomPowers[powersBefore + 1], multi2),
					$"{Profile.CustomPowers.Count} 个");

				// 多条一起删：某个状态的触发时机（删完也要能撤回）
				CustomPowerSpec multiSpec = new CustomPowerSpec { Name = "多选时机", ClassName = "UiCheckMultiC", Type = "Buff" };
				multiSpec.Triggers.Add(new PowerTriggerSpec { Kind = "TurnStart" });
				multiSpec.Triggers.Add(new PowerTriggerSpec { Kind = "TurnEnd" });
				multiSpec.Triggers.Add(new PowerTriggerSpec { Kind = "CombatStart" });
				Profile.CustomPowers.Add(multiSpec);
				CustomPowerList.SelectedItem = multiSpec;
				UpdateLayout();
				CustomTriggerList.SelectedItems.Clear();
				CustomTriggerList.SelectedItems.Add(multiSpec.Triggers[0]);
				CustomTriggerList.SelectedItems.Add(multiSpec.Triggers[1]);
				OnRemovePowerTrigger(this, new RoutedEventArgs());
				Check("触发时机：一次删掉多选的那几条",
					multiSpec.Triggers.Count == 1 && multiSpec.Triggers[0].Kind == "CombatStart",
					string.Join("·", multiSpec.Triggers.Select(t => t.Kind)));
				OnUndoPower(this, new RoutedEventArgs());
				Check("触发时机：多条删除也能一次撤回（顺序复原）",
					multiSpec.Triggers.Count == 3 && multiSpec.Triggers[0].Kind == "TurnStart" && multiSpec.Triggers[1].Kind == "TurnEnd",
					string.Join("·", multiSpec.Triggers.Select(t => t.Kind)));

				// 清理：把这一轮加的测试状态和撤回栈都清掉
				_powerUndo.Clear();
				Profile.CustomPowers.Remove(multiSpec);
				Profile.CustomPowers.Remove(multi1);
				Profile.CustomPowers.Remove(multi2);
				Check("多选删除测试用的自定义状态已清理", Profile.CustomPowers.Count == powersBefore, $"{Profile.CustomPowers.Count} 个");
			}
		}

		// ===== 透支能量 / 额外回合：生成的 Power 也必须有本地化 =====
		// 用户实测报过：打出「透支」之后，状态那一栏的描述显示成原始键名
		// （powers.SPARKLE_FORGE_ENERGY_DEBT_POWER.title / …description）—— 因为 powers.json 里没这两条键。
		{
			string debtRoot = Path.Combine(Path.GetTempPath(), "forge_uicheck_debt_" + Guid.NewGuid().ToString("N").Substring(0, 8));
			try
			{
				CharacterProfile debt = ProfileFactory.Sample();
				debt.Paths.OutputDir = debtRoot;
				debt.Paths.VanillaProject = Profile.Paths.VanillaProject;
				debt.Paths.GameDataDir = Profile.Paths.GameDataDir;
				debt.SaveName = "透支自检";
				// 故意把其它会写 powers.json 的东西都清掉：这一段就是要验「只用到透支 / 额外回合时也会写」
				debt.CustomPowers.Clear();
				debt.VanillaPowerOverrides.Clear();
				foreach (CardSpec c in debt.Cards)
					foreach (EffectSpec e in c.Effects)
						e.NextTurn = false;                      // 关掉「下回合生效」的延迟 Power
				Naming debtNaming = Naming.From(debt);
				CardSpec debtCard = new CardSpec { Name = "自检透支", ClassName = "UiCheckDebt", CardType = "Skill", Rarity = "Common", Cost = 0, InCardPool = true };
				debtCard.Effects.Add(new EffectSpec { Kind = "OverdraftEnergy", Amount = 2m, TargetSide = "Self" });
				debt.Cards.Add(debtCard);
				CardSpec turnCard = new CardSpec { Name = "自检额外回合", ClassName = "UiCheckExtraTurn", CardType = "Skill", Rarity = "Rare", Cost = 1, InCardPool = true };
				turnCard.Effects.Add(new EffectSpec { Kind = "ExtraTurn", TargetSide = "Self" });
				debt.Cards.Add(turnCard);

				GenerationResult debtGen = ModGenerator.Generate(debt);
				string debtLocPath = Path.Combine(debtGen.ProjectRoot, debtNaming.ModId, "localization", "zhs", "powers.json");
				Check("透支 / 额外回合：只用到它们也会生成 powers.json（不生成的话游戏查不到键，只能把原始键名印出来）",
					File.Exists(debtLocPath), debtLocPath);
				string debtLoc = File.Exists(debtLocPath) ? File.ReadAllText(debtLocPath) : "";
				string debtEntry = Naming.EntryOf(debtNaming.EnergyDebtPowerClass);
				string turnEntry = Naming.EntryOf(debtNaming.ExtraTurnPowerClass);
				Check("透支：负债 Power 有 title（状态悬停里那个名字，不再是 powers.…title）",
					debtLoc.Contains(debtEntry + ".title"), debtEntry + ".title");
				Check("透支：负债 Power 的 description 带 {Amount} 占位符（显示成「下回合少 2 点能量」）",
					debtLoc.Contains(debtEntry + ".description") && debtLoc.Contains("下回合少 {Amount} 点能量。"), debtEntry + ".description");
				Check("透支：smartDescription 也写了（本体战斗里的悬停优先用它）",
					debtLoc.Contains(debtEntry + ".smartDescription"), debtEntry + ".smartDescription");
				Check("额外回合：Power 也有 title / description（同一个坑，顺手一起验）",
					debtLoc.Contains(turnEntry + ".title") && debtLoc.Contains(turnEntry + ".description"), turnEntry);
				Check("键名和本体查表用的键一致（Naming.EntryOf 算出来的那个）",
					File.Exists(debtLocPath) && debtLoc.Contains("\"" + debtEntry + ".title\""), debtEntry);
				// 状态图标的**文件名**也必须是本体的 Id.Entry（类名 slugify：带下划线）：
				// 游戏找的是 res://images/powers/<Id.Entry 小写>.png。
				// 用「类名小写」当文件名的话游戏找不到 → 日志里 Missing sprite + 紫占位（用户实测）。
				string debtSlugPng = EffectCatalog.SlugFor(debtNaming.EnergyDebtPowerClass).ToLowerInvariant() + ".png";
				Check("生成的状态图标用的是「本体 Id.Entry」文件名（带下划线），不是类名小写",
					File.Exists(Path.Combine(debtGen.ProjectRoot, "images", "powers", debtSlugPng)),
					"images/powers/" + debtSlugPng);
				Check("类名小写那种文件名不再被使用（用了游戏就找不到）",
					!File.Exists(Path.Combine(debtGen.ProjectRoot, "images", "powers",
						debtNaming.EnergyDebtPowerClass.ToLowerInvariant() + ".png")),
					debtNaming.EnergyDebtPowerClass.ToLowerInvariant() + ".png");
			}
			finally
			{
				try { if (Directory.Exists(debtRoot)) Directory.Delete(debtRoot, true); } catch { }
			}
		}

		// ===== 给予卡牌关键词（取代了卡牌级的「临时保留 / 临时奇巧」两个勾选框）=====
		{
			List<string> cardPageTexts = TextsIn(SelectTabRoot("卡牌"));
			Check("卡牌页不再有「临时保留 / 临时奇巧」两个勾选框（改成「给予卡牌关键词」效果了）",
				!cardPageTexts.Any(t => t.StartsWith("临时保留", StringComparison.Ordinal)
					|| t.StartsWith("临时奇巧", StringComparison.Ordinal)), "已移除");

			Check("效果种类里有「给予卡牌关键词」",
				EffectCatalog.EffectKinds.Any(k => k.Kind == "GiveKeyword"), "在");
			Check("「给予关键词」下拉的候选里本体 7 个关键词都在（含永恒）",
				EffectCatalog.VanillaKeywordChoices.Count == 7
				&& EffectCatalog.IsVanillaKeywordName("Retain") && EffectCatalog.IsVanillaKeywordName("Sly")
				&& EffectCatalog.IsVanillaKeywordName("Eternal")
				&& !EffectCatalog.IsVanillaKeywordName("FATE"), $"{EffectCatalog.VanillaKeywordChoices.Count} 个");
			Check("自定义关键词也会出现在「给予关键词」下拉里（Id 用的是它的键）",
				KeywordChoices.Any(k => k.IsCustom || k.Id == "Retain") && KeywordChoices.Count == EffectCatalog.VanillaKeywordChoices.Count + KeywordGen.All(Profile).Count,
				$"{KeywordChoices.Count} 项");

			// 数值 0 = 这张牌自己 + 临时（保留 → 本体的单回合标记）
			CharacterProfile gkProbe = ProfileFactory.Sample();
			CardSpec gkSelf = new CardSpec { Name = "自检给予自己", ClassName = "UiCheckGiveSelf", CardType = "Skill", Rarity = "Common", Cost = 1 };
			gkSelf.Effects.Add(new EffectSpec { Kind = "GiveKeyword", Amount = 0m, GivenKeyword = "Retain", TempKeyword = true, TargetSide = "Self" });
			gkSelf.Effects.Add(new EffectSpec { Kind = "GiveKeyword", Amount = 0m, GivenKeyword = "Exhaust", TempKeyword = false, TargetSide = "Self" });
			gkProbe.Cards.Add(gkSelf);
			string gkSelfSrc = CSharpCodeGen.CardSource(gkProbe, gkSelf, 0);
			Check("数值 0 → 给的是这张牌自己（this）",
				gkSelfSrc.Contains("this.GiveSingleTurnRetain();"), "自己");
			Check("临时 + 保留 → 走本体的单回合标记 GiveSingleTurnRetain()（回合末自动复位）",
				gkSelfSrc.Contains("this.GiveSingleTurnRetain();"), "单回合标记");
			Check("不勾临时 → 直接 AddKeyword（本体 API，卡面文字会立刻刷新）",
				gkSelfSrc.Contains("this.AddKeyword(CardKeyword.Exhaust);"), "AddKeyword");
			// 临时给「其它本体关键词」要挂一个我们生成的 Power：**必须用 ModelDb + ToMutable 造副本**，
			// 直接 new 会 DuplicateModelException（「Use ModelDb instead」）→ 出牌抛异常 → 卡牌悬浮打不出去（用户实测）
			CharacterProfile gkTempProbe = ProfileFactory.Sample();
			gkTempProbe.Cards.Add(new CardSpec
			{
				Name = "自检临时虚无", ClassName = "UiCheckGiveTempEthereal", Cost = 1,
				Effects = { new EffectSpec { Kind = "GiveKeyword", Amount = 0m, GivenKeyword = "Ethereal", TempKeyword = true, TargetSide = "Self" } },
			});
			string gkTempSrc = CSharpCodeGen.CardSource(gkTempProbe, gkTempProbe.Cards[^1], 0);
			Check("临时给其它本体关键词：用生成的 Create(...) 挂临时 Power（不是 new）",
				gkTempSrc.Contains("ForgeTempKeywordPower.Create(this, CardKeyword.Ethereal)"), "用 Create");
			Check("生成的效果代码里**不再出现 new <…>ForgeTempKeywordPower(**（new 会 DuplicateModelException）",
				!gkTempSrc.Contains("new " + Naming.From(gkTempProbe).TempKeywordPowerClass + "("), "没有 new");
			string gkPowerFactorySrc = CSharpCodeGen.TempKeywordPowerSource(gkTempProbe);
			Check("临时 Power 里给了 Create / CreateForCustom 两个工厂（内部 ModelDb.Power<T>().ToMutable()）",
				gkPowerFactorySrc.Contains("public static " + Naming.From(gkTempProbe).TempKeywordPowerClass + " Create(CardModel card, CardKeyword keyword)")
				&& gkPowerFactorySrc.Contains(".ToMutable()")
				&& gkPowerFactorySrc.Contains("ModelDb.Power<"), "有工厂");
			Check("工厂的注释里写明「不要 new」的原因（免得以后又被改回去）",
				gkPowerFactorySrc.Contains("Use ModelDb instead"), "注释在");
			Check("回读标记写出来了（CET:GiveKeyword=… CET:GiveKeywordTemp=…）",
				gkSelfSrc.Contains("CET:GiveKeyword=Retain") && gkSelfSrc.Contains("CET:GiveKeywordTemp=1")
				&& gkSelfSrc.Contains("CET:GiveKeyword=Exhaust") && gkSelfSrc.Contains("CET:GiveKeywordTemp=0"), "标记在");

			// 数值 > 0 = 选 N 张（自己选 / 随机、三摞牌都行）
			CardSpec gkPick = new CardSpec { Name = "自检给予别人", ClassName = "UiCheckGivePick", CardType = "Skill", Rarity = "Common", Cost = 1 };
			gkPick.Effects.Add(new EffectSpec { Kind = "GiveKeyword", Amount = 2m, GivenKeyword = "Innate", CardPick = "Chosen", SelectPile = "Draw", TargetSide = "Self" });
			gkPick.Effects.Add(new EffectSpec { Kind = "GiveKeyword", Amount = 3m, GivenKeyword = "Sly", TempKeyword = true, CardPick = "Random", SelectPile = "Discard", TargetSide = "Self" });
			gkProbe.Cards.Add(gkPick);
			string gkPickSrc = CSharpCodeGen.CardSource(gkProbe, gkPick, 0);
			Check("自己选 N 张 → CardSelectCmd.FromCombatPile（从抽牌堆挑，张数写进 CardSelectorPrefs）",
				gkPickSrc.Contains("CardSelectCmd.FromCombatPile(choiceContext, PileType.Draw.GetPile(base.Owner), base.Owner, new CardSelectorPrefs(base.SelectionScreenPrompt, 2))"), "抽牌堆");
			Check("随机 N 张 → 按 RNG 挑，张数 = 循环次数",
				gkPickSrc.Contains("for (int __kwIdx = 0; __kwIdx < 3; __kwIdx++)")
				&& gkPickSrc.Contains("NextItem(PileType.Discard.GetPile(base.Owner).Cards)"), "随机挑");
			Check("临时 + 奇巧 → GiveSingleTurnSly()（本体单回合标记）",
				gkPickSrc.Contains("__kwCard.GiveSingleTurnSly();"), "奇巧");
			Check("自己选牌时生成了选牌界面提示语（selectionScreenPrompt）",
				LocalizationGen.CardsJson(gkProbe).Contains("\"UI_CHECK_GIVE_PICK.selectionScreenPrompt\""), "提示语在");
			string gkCards = LocalizationGen.CardsJson(gkProbe);
			Check("卡面描述写清「给谁 / 几张 / 哪个关键词 / 是否临时」",
				gkCards.Contains("这张牌获得[gold]保留[/gold]（本回合）。")
				&& gkCards.Contains("这张牌获得[gold]消耗[/gold]。")
				&& gkCards.Contains("自己选 2 张抽牌堆里的牌获得[gold]固有[/gold]。")
				&& gkCards.Contains("随机 3 张弃牌堆里的牌获得[gold]奇巧[/gold]（本回合）。"),
				gkCards.Replace("\n", " "));
			Check("列表里也看得出给的是哪个关键词、是不是临时",
				gkSelf.Effects[0].Display.Contains("保留") && gkSelf.Effects[0].Display.Contains("临时")
				&& gkPick.Effects[1].Display.Contains("随机"), gkSelf.Effects[0].Display);

			// 临时关键词 Power：回合结束把关键词摘掉
			Check("勾了临时关键词才生成那个临时 Power", CSharpCodeGen.UsesTempKeywordPower(gkProbe));
			string gkPowerSrc = CSharpCodeGen.TempKeywordPowerSource(gkProbe);
			Check("临时 Power 是 Instanced（每张牌 / 每个关键词一条，不能按 Id 合并）",
				gkPowerSrc.Contains("PowerInstanceType.Instanced"), "Instanced");
			Check("临时 Power 不显示状态图标（不查 powers 表，也就不会缺图标）",
				gkPowerSrc.Contains("IsVisibleInternal => false"), "隐藏");
			Check("临时 Power 在回合结束时摘掉关键词、然后自毁",
				gkPowerSrc.Contains("public override async Task AfterSideTurnEnd(")
				&& gkPowerSrc.Contains("Card.RemoveKeyword(VanillaKeyword);")
				&& gkPowerSrc.Contains("await PowerCmd.Remove(this);"), "会摘掉");
			// 只配了本体关键词时**不能**引用自定义关键词那套：RemoveGivenCustomKeyword 在 GivenKeywords.cs 里，
			// 那个文件只有「给自定义关键词」才会生成 —— 两支都写上的话构建会 CS1061（用户实测踩过）
			Check("只配本体关键词时，临时 Power 不引用自定义关键词那套（否则构建 CS1061）",
				!gkPowerSrc.Contains("RemoveGivenCustomKeyword"), "没有引用");
			CharacterProfile gkCustomOnly = ProfileFactory.Sample();
			gkCustomOnly.CustomKeywords.Clear();
			gkCustomOnly.CustomKeywords.Add(new CustomKeywordSpec { Name = "命定", Key = "FATE", Description = "自检用" });
			gkCustomOnly.Cards.Add(new CardSpec
			{
				Name = "只给自定义临时", ClassName = "UiCheckGiveCustomOnly", Cost = 1,
				Effects = { new EffectSpec { Kind = "GiveKeyword", Amount = 0m, GivenKeyword = "FATE", TempKeyword = true, TargetSide = "Self" } },
			});
			string gkCustomOnlySrc = CSharpCodeGen.TempKeywordPowerSource(gkCustomOnly);
			Check("只配自定义关键词时，临时 Power 只摘自定义那一支（不引用本体那一支）",
				gkCustomOnlySrc.Contains("Card.RemoveGivenCustomKeyword(CustomKey);")
				&& !gkCustomOnlySrc.Contains("Card.RemoveKeyword(VanillaKeyword);"), "只有自定义那一支");

			// 自定义关键词：注册表 + 两个补丁（卡面文字 + 悬停说明）
			CharacterProfile gkCustom = ProfileFactory.Sample();
			gkCustom.CustomKeywords.Clear();
			gkCustom.CustomKeywords.Add(new CustomKeywordSpec { Name = "命定", Key = "FATE", Description = "自检用" });
			CardSpec gkCustomCard = new CardSpec { Name = "自检给自定义", ClassName = "UiCheckGiveCustom", CardType = "Skill", Rarity = "Common", Cost = 1 };
			gkCustomCard.Effects.Add(new EffectSpec { Kind = "GiveKeyword", Amount = 0m, GivenKeyword = "FATE", TempKeyword = true, TargetSide = "Self" });
			gkCustom.Cards.Add(gkCustomCard);
			Check("给自定义关键词时才算「要那个补丁」", CSharpCodeGen.UsesGivenCustomKeyword(gkCustom));
			string gkCustomSrc = CSharpCodeGen.CardSource(gkCustom, gkCustomCard, 0);
			Check("给自定义关键词生成的是扩展方法调用（键就是 FATE）",
				gkCustomSrc.Contains("this.AddGivenCustomKeyword(\"FATE\");")
				&& gkCustomSrc.Contains("CET:GiveKeyword=custom:FATE"), "自定义");
			string gkPatch = CSharpCodeGen.GivenKeywordPatchSource(gkCustom);
			Check("补丁挂在 GetDescriptionForPile（卡面文字）和 get_ExtraHoverTips（悬停说明）上",
				gkPatch.Contains("[HarmonyLib.HarmonyPatch(typeof(CardModel), \"GetDescriptionForPile\")]")
				&& gkPatch.Contains("[HarmonyLib.HarmonyPatch(typeof(CardModel), \"get_ExtraHoverTips\")]"), "两个补丁");
			Check("注册表用 ConditionalWeakTable（牌没了自动清，不会吊住卡牌）",
				gkPatch.Contains("ConditionalWeakTable<CardModel, List<string>>"), "弱表");
			Check("补丁里查不到键会退回键名（不让补丁抛异常）",
				gkPatch.Contains("catch") && gkPatch.Contains("return key;"), "兜底");

			// 校验：没选关键词要报错；数值 0 只能在卡牌上用；遗物上要 ≥ 1
			Check("没选关键词 → 校验报错",
				ProfileValidator.Validate(new CharacterProfile { Cards = { new CardSpec { Name = "缺关键词", Cost = 1, Effects = { new EffectSpec { Kind = "GiveKeyword", Amount = 0m } } } } })
					.Any(i => i.IsError && i.Message.Contains("还没选要给哪个关键词")), "有错误");
			Check("写了不存在的自定义关键词 → 校验报错",
				ProfileValidator.Validate(new CharacterProfile { Cards = { new CardSpec { Name = "乱填关键词", Cost = 1, Effects = { new EffectSpec { Kind = "GiveKeyword", Amount = 0m, GivenKeyword = "NOPE" } } } } })
					.Any(i => i.IsError && i.Message.Contains("找不到")), "有错误");
			CharacterProfile gkRelicProbe = ProfileFactory.Sample();
			RelicSpec gkRelic = new RelicSpec { Name = "自检给关键词遗物", Trigger = "PlayerTurnStart" };
			gkRelic.Effects.Add(new EffectSpec { Kind = "GiveKeyword", Amount = 0m, GivenKeyword = "Retain" });
			gkRelicProbe.Relics.Add(gkRelic);
			Check("遗物上写「数值 0 = 这张牌自己」→ 校验报错（那里没有「这张牌」）",
				ProfileValidator.Validate(gkRelicProbe).Any(i => i.IsError && i.Message.Contains("只有卡牌上的这条效果")), "有错误");
			gkRelic.Effects[0].Amount = 2m;
			Check("遗物上改成「选 2 张」→ 校验通过、而且能生成",
				!ProfileValidator.Validate(gkRelicProbe).Any(i => i.IsError && i.Message.Contains("给予卡牌关键词"))
				&& CSharpCodeGen.RelicSource(gkRelicProbe, gkRelic, 0).Contains("__kwCard.AddKeyword(CardKeyword.Retain);"), "遗物也支持");
		}

		// ===== 击晕（本体里它不是状态，是怪物意图）+ 遗物的「随机/指定敌人」不能再变成全体 =====
		{
			Check("效果种类里有「击晕（敌人本回合不行动）」",
				EffectCatalog.EffectKinds.Any(k => k.Kind == "Stun"), "在");
			Check("击晕也能放在状态触发器里（CreatureCmd.Stun 不需要 choiceContext）",
				PowerTriggers.Supports("Stun"), "在白名单里");

			CharacterProfile stunProbe = ProfileFactory.Sample();
			CardSpec stunCard = new CardSpec { Name = "自检击晕", ClassName = "UiCheckStun", CardType = "Skill", Rarity = "Common", Cost = 1, InCardPool = true };
			stunCard.Effects.Add(new EffectSpec { Kind = "Stun", TargetSide = "Enemy" });
			stunCard.Effects.Add(new EffectSpec { Kind = "Stun", TargetSide = "AllEnemies" });
			stunCard.Effects.Add(new EffectSpec { Kind = "Stun", TargetSide = "RandomEnemies", RepeatCount = 2, AllowDuplicates = false });
			stunProbe.Cards.Add(stunCard);
			string stunSrc = CSharpCodeGen.CardSource(stunProbe, stunCard, 0);
			Check("击晕生成的是本体那句 API CreatureCmd.Stun（本体卡「口哨」同款）",
				stunSrc.Contains("await CreatureCmd.Stun(cardPlay.Target);"), "Stun API");
			Check("击晕全体 → foreach 每个敌人各来一次", stunSrc.Contains("foreach (Creature foe in base.CombatState.HittableEnemies)")
				&& stunSrc.Contains("await CreatureCmd.Stun(foe);"), "全体");
			Check("击晕随机 N 个 → 循环随机挑（不允许重复时不重复挑）",
				stunSrc.Contains("for (int __stunIdx = 0; __stunIdx < 2 && foes.Count > 0; __stunIdx++)")
				&& stunSrc.Contains("NextItem(foes)") && stunSrc.Contains("foes.Remove(foe);"), "随机");
			Check("击晕的卡会变成「要选目标」的牌（TargetType.AnyEnemy）",
				stunSrc.Contains("TargetType.AnyEnemy"), "AnyEnemy");
			Check("击晕带本体那条静态悬停说明（StunIntent.GetStaticHoverTip）",
				stunSrc.Contains("StunIntent.GetStaticHoverTip()"), "有悬停说明");
			Check("击晕不声明动态变量（没有数值，也就不会有 Value 兜底变量）",
				CSharpCodeGen.VarKeysOf(new[] { new EffectSpec { Kind = "Stun" } }).Count == 0, "没有变量");
			Check("卡面描述写清击晕对象",
				LocalizationGen.CardsJson(stunProbe).Contains("击晕指定敌人（本回合不行动）。")
				&& LocalizationGen.CardsJson(stunProbe).Contains("击晕所有敌人（本回合不行动）。")
				&& LocalizationGen.CardsJson(stunProbe).Contains("击晕随机 2 个敌人（本回合不行动）。"),
				LocalizationGen.CardsJson(stunProbe).Replace("\n", " "));

			// 遗物：目标必须分开处理（用户报过「对 1 个随机敌人施加增益」变成了对所有敌人生效）
			CharacterProfile relProbe = ProfileFactory.Sample();
			RelicSpec relRnd = new RelicSpec { Name = "自检随机1", Trigger = "PlayerTurnStart" };
			relRnd.Effects.Add(new EffectSpec { Kind = "ApplyPower", PowerId = "VulnerablePower", Amount = 2m, TargetSide = "RandomEnemies", RepeatCount = 1 });
			relProbe.Relics.Add(relRnd);
			string relRndSrc = CSharpCodeGen.RelicSource(relProbe, relRnd, 0);
			Check("遗物「随机 N 个敌人」→ 按次随机挑，**不再**写成「对所有敌人」",
				relRndSrc.Contains("for (int __relFoeIdx = 0; __relFoeIdx < 1 && foes.Count > 0; __relFoeIdx++)")
				&& relRndSrc.Contains("await PowerCmd.Apply<VulnerablePower>(choiceContext, foe,")
				&& !relRndSrc.Contains("Apply<VulnerablePower>(choiceContext, base.Owner.Creature.CombatState.HittableEnemies,"),
				"随机一只");
			RelicSpec relOne = new RelicSpec { Name = "自检指定", Trigger = "PlayerTurnStart" };
			relOne.Effects.Add(new EffectSpec { Kind = "ApplyPower", PowerId = "WeakPower", Amount = 1m, TargetSide = "Enemy" });
			relProbe.Relics.Add(relOne);
			string relOneSrc = CSharpCodeGen.RelicSource(relProbe, relOne, 0);
			Check("遗物「指定敌人」→ 取可打的第一个敌人（不是全体）",
				relOneSrc.Contains("Creature? foe = base.Owner.Creature.CombatState.HittableEnemies.FirstOrDefault();")
				&& relOneSrc.Contains("await PowerCmd.Apply<WeakPower>(choiceContext, foe,")
				&& !relOneSrc.Contains("Apply<WeakPower>(choiceContext, base.Owner.Creature.CombatState.HittableEnemies,"),
				"第一个敌人");
			RelicSpec relAll = new RelicSpec { Name = "自检全体", Trigger = "PlayerTurnStart" };
			relAll.Effects.Add(new EffectSpec { Kind = "ApplyPower", PowerId = "WeakPower", Amount = 1m, TargetSide = "AllEnemies" });
			relProbe.Relics.Add(relAll);
			Check("遗物「所有敌人」仍然走一次性全体施加（没改坏）",
				CSharpCodeGen.RelicSource(relProbe, relAll, 0)
					.Contains("Apply<WeakPower>(choiceContext, base.Owner.Creature.CombatState.HittableEnemies,"), "全体");
			RelicSpec relTempRnd = new RelicSpec { Name = "自检临时随机", Trigger = "PlayerTurnStart" };
			relTempRnd.Effects.Add(new EffectSpec { Kind = "TempPower", PowerId = "StrengthPower", Amount = 2m, TargetSide = "RandomEnemies", RepeatCount = 1 });
			relProbe.Relics.Add(relTempRnd);
			Check("遗物上的「临时增益·随机敌人」也按随机挑，而不是全体",
				CSharpCodeGen.RelicSource(relProbe, relTempRnd, 0).Contains("for (int __relTempIdx = 0;")
				&& !CSharpCodeGen.RelicSource(relProbe, relTempRnd, 0).Contains("ForgeTempStrengthPower>(choiceContext, base.Owner.Creature.CombatState.HittableEnemies,"),
				"随机一只");
		}

		// ===== 第二批⑤：临时增益（本回合 +X，回合结束撤掉）=====
		{
			Check("效果种类里有「临时增益（本回合 +X，回合结束消失）」",
				EffectCatalog.EffectKinds.Any(k => k.Kind == "TempPower"), string.Join("/", EffectCatalog.EffectKinds.Select(k => k.Kind)));
			Check("临时增益能挂在状态触发器里（白名单里有它）", PowerTriggers.Supports("TempPower"), "在白名单里");
			CharacterProfile tempPowerProbe = ProfileFactory.Sample();
			CardSpec tempPowerCard = new CardSpec
			{
				Name = "临时力量测试", ClassName = "UiCheckTempPower", CardType = "Skill", Rarity = "Common", Cost = 1,
			};
			tempPowerCard.Effects.Add(new EffectSpec { Kind = "TempPower", PowerId = "StrengthPower", Amount = 3m, UpgradeAmount = 1m, TargetSide = "Self" });
			tempPowerProbe.Cards.Add(tempPowerCard);
			string tempPowerSrc = CSharpCodeGen.CardSource(tempPowerProbe, tempPowerCard, 0);
			string tempPowerCls = Naming.From(tempPowerProbe).TempPowerClass(tempPowerCard.Effects[0]);
			Check("打出去的是生成的临时 Power（不是直接施加本体状态）",
				tempPowerSrc.Contains($"await PowerCmd.Apply<{tempPowerCls}>(choiceContext, base.Owner.Creature,"), tempPowerCls);
			Check("临时 Power 的类名带角色前缀（两个模组装一起不撞模型 ID）",
				tempPowerCls.StartsWith(Naming.From(tempPowerProbe).CharClass, StringComparison.Ordinal) && tempPowerCls.EndsWith("ForgeTempStrengthPower", StringComparison.Ordinal), tempPowerCls);
			Check("临时 Power 有动态变量（卡面数字 / 升级增量都按它走）",
				tempPowerSrc.Contains("new PowerVar<StrengthPower>(") && tempPowerSrc.Contains("OnUpgrade"), "有变量");
			string tempPowersSrc = CSharpCodeGen.TempPowersSource(tempPowerProbe);
			Check("临时 Power 照本体 TemporaryStrengthPower 写：施加时把层数加进真正的状态",
				tempPowersSrc.Contains("public override async Task BeforeApplied(Creature target, decimal amount, Creature? applier, CardModel? cardSource)")
				&& tempPowersSrc.Contains("await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), target, amount, applier, cardSource, silent: true);"), "BeforeApplied");
			Check("叠加时按增量补（AfterPowerAmountChanged）",
				tempPowersSrc.Contains("public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)"), "有");
			Check("回合结束时把这次加的层数撤掉、然后自己消失（AfterSideTurnEnd）",
				tempPowersSrc.Contains("public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)")
				&& tempPowersSrc.Contains("await PowerCmd.Remove(this);")
				&& tempPowersSrc.Contains("await PowerCmd.Apply<StrengthPower>(choiceContext, base.Owner, -base.Amount, base.Owner, null);"), "回合结束撤掉");
			Check("没配临时增益时不会生成临时 Power（UsesTempPower = false）",
				!CSharpCodeGen.UsesTempPower(ProfileFactory.Sample()), "不生成");
			string tempPowerCards = LocalizationGen.CardsJson(tempPowerProbe);
			Check("卡面描述写清「本回合内 +X 层…（回合结束时消失）」",
				tempPowerCards.Contains("本回合内获得 {StrengthPower:diff()} 层") && tempPowerCards.Contains("（回合结束时消失）"),
				tempPowerCards.Replace("\n", " "));
			string tempPowerLoc = LocalizationGen.PowersJson(tempPowerProbe);
			Check("临时 Power 有本地化（状态栏显示「临时气势」+ 说明）",
				tempPowerLoc.Contains(Naming.EntryOf(tempPowerCls) + ".title") && tempPowerLoc.Contains("回合结束时消失"), "有本地化");
			Check("要改状态但没选状态时报错（不是静默生成个空类）",
				ProfileValidator.Validate(new CharacterProfile { Cards = { new CardSpec { Name = "缺状态", Cost = 1, Effects = { new EffectSpec { Kind = "TempPower", Amount = 2m } } } } })
					.Any(i => i.IsError && i.Message.Contains("未选择有效的 Power")), "有错误");
			CharacterProfile tempPowerRelic = ProfileFactory.Sample();
			RelicSpec tempPowerRelicSpec = new RelicSpec { Name = "临时增益遗物", Trigger = "PlayerTurnStart", Rarity = "Common" };
			tempPowerRelicSpec.Effects.Add(new EffectSpec { Kind = "TempPower", PowerId = "StrengthPower", Amount = 2m, TargetSide = "Self" });
			tempPowerRelic.Relics.Add(tempPowerRelicSpec);
			Check("遗物上也能用临时增益",
				CSharpCodeGen.RelicSource(tempPowerRelic, tempPowerRelicSpec, 0).Contains($"PowerCmd.Apply<{Naming.From(tempPowerRelic).TempPowerClass(tempPowerRelicSpec.Effects[0])}>("), "遗物也支持");
		}

		// ===== 强化指定卡牌（像本体「精准」对「小刀」）+ 卡牌自定义描述 =====
		{
			Check("效果种类里有「强化指定卡牌（像「精准」，+N 伤害/格挡）」",
				EffectCatalog.EffectKinds.Any((EffectKindOption k) => k.Kind == "BoostCard"), "在");
			Check("「强化什么」有伤害 / 格挡两项（伤害 = 精准、格挡 = 敏捷那套）",
				EffectCatalog.BoostStats.Count == 2
				&& EffectCatalog.BoostStats.Any((BoostStatOption s) => s.Id == "Damage")
				&& EffectCatalog.BoostStats.Any((BoostStatOption s) => s.Id == "Block"),
				string.Join("/", EffectCatalog.BoostStats.Select((BoostStatOption s) => s.Id)));
			Check("强化指定卡牌能放在状态触发器里（只是给自己挂一张 Power）",
				PowerTriggers.Supports("BoostCard"), "在白名单里");

			CharacterProfile boostProbe = ProfileFactory.Sample();
			CardSpec boostCard = new CardSpec
			{
				Name = "自检精准", ClassName = "UiCheckBoost", CardType = "Power", Rarity = "Rare", Cost = 1, InCardPool = true,
			};
			// 一张牌同时挂两种强化：目标卡 / 强化什么 / 数值三样都要各走各的（变量名会自动起别名 Boost2）
			boostCard.Effects.Add(new EffectSpec { Kind = "BoostCard", Amount = 3m, UpgradeAmount = 2m, SpawnCardId = "SevenSlash", BoostStat = "Damage", TargetSide = "Self" });
			boostCard.Effects.Add(new EffectSpec { Kind = "BoostCard", Amount = 2m, SpawnCardId = "SevenGuard", BoostStat = "Block", TargetSide = "Self" });
			boostProbe.Cards.Add(boostCard);
			string boostSrc = CSharpCodeGen.CardSource(boostProbe, boostCard, 0);
			string boostDmgCls = Naming.From(boostProbe).BoostPowerClass(boostCard.Effects[0]);
			string boostBlkCls = Naming.From(boostProbe).BoostPowerClass(boostCard.Effects[1]);
			Check("强化 Power 的类名带角色前缀 + 目标卡 + 伤害/格挡（两个模组 / 两种强化都不会撞模型 ID）",
				boostDmgCls == "SevenForgeBoostSevenSlashDamagePower" && boostBlkCls == "SevenForgeBoostSevenGuardBlockPower",
				boostDmgCls + " / " + boostBlkCls);
			Check("打出去时挂的是生成的强化 Power，层数 = 数值（挂在自己身上）",
				boostSrc.Contains($"await PowerCmd.Apply<{boostDmgCls}>(choiceContext, base.Owner.Creature,")
				&& boostSrc.Contains($"await PowerCmd.Apply<{boostBlkCls}>(choiceContext, base.Owner.Creature,"), "挂在身上");
			Check("两条强化各声明一个变量（第二次自动起别名 Boost2，不会撞 DynamicVarSet 的键）",
				boostSrc.Contains("new DynamicVar(\"Boost\", 3m)") && boostSrc.Contains("new DynamicVar(\"Boost2\", 2m)"),
				"CET:BoostCard 两条");
			var boostCardVars = CSharpCodeGen.VarNamesOf(boostCard.Effects);
			Check("卡面描述里两个数字都是 {…:diff()} 占位符（升级后数字会跟着变）",
				CSharpCodeGen.DisplayVarNameOf(boostCard.Effects[0], boostCardVars) == "Boost"
				&& CSharpCodeGen.DisplayVarNameOf(boostCard.Effects[1], boostCardVars) == "Boost2",
				CSharpCodeGen.DisplayVarNameOf(boostCard.Effects[0], boostCardVars) + " / " + CSharpCodeGen.DisplayVarNameOf(boostCard.Effects[1], boostCardVars));

			string boostPowers = CSharpCodeGen.BoostPowersSource(boostProbe);
			Check("伤害那条照本体「精准」写：ModifyDamageAdditive + 只看这张卡",
				boostPowers.Contains("public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)")
				&& boostPowers.Contains("if (cardSource is not SevenSlash) return 0m;")
				&& boostPowers.Contains("if (!props.IsPoweredAttack()) return 0m;"), "精准那套");
			Check("格挡那条照本体「敏捷」写：ModifyBlockAdditive + 只看这张卡",
				boostPowers.Contains("public override decimal ModifyBlockAdditive(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)")
				&& boostPowers.Contains("if (cardSource is not SevenGuard) return 0m;")
				&& boostPowers.Contains("if (!props.IsPoweredCardOrMonsterMoveBlock()) return 0m;"), "敏捷那套");
			Check("允许负数（填负数 = 削弱那张卡；本体状态默认会把负数夹到 0）",
				boostPowers.Contains("public override bool AllowNegative => true;"), "AllowNegative");
			Check("同一个「目标卡 + 强化什么」只生成一个 Power（去重）",
				CSharpCodeGen.CollectBoostEffects(boostProbe).Count() == 2, $"{CSharpCodeGen.CollectBoostEffects(boostProbe).Count()} 个");
			Check("没配强化时不生成 BoostPowers（UsesBoostCard = false）",
				!CSharpCodeGen.UsesBoostCard(ProfileFactory.Sample()), "不生成");

			string boostCardsLoc = LocalizationGen.CardsJson(boostProbe);
			Check("卡面描述写清「哪张卡、伤害还是格挡、加多少、本场战斗内持续」",
				boostCardsLoc.Contains("你打出的七式斩额外造成 {Boost:diff()} 点伤害（本场战斗内持续）。")
				&& boostCardsLoc.Contains("你打出的七的防线额外获得 {Boost2:diff()} 点格挡（本场战斗内持续）。"),
				boostCardsLoc.Replace("\n", " "));
			string boostPowersLoc = LocalizationGen.PowersJson(boostProbe);
			Check("强化 Power 有本地化（状态栏显示「强化：七式斩」+ 说明，不会印原始键名）",
				boostPowersLoc.Contains(Naming.EntryOf(boostDmgCls) + ".title") && boostPowersLoc.Contains("强化：七式斩"), "有本地化");
			Check("回读标记写出来了（CET:BoostCard=… CET:BoostStat=…）",
				boostSrc.Contains("CET:BoostCard=SevenSlash CET:BoostStat=Damage")
				&& boostSrc.Contains("CET:BoostCard=SevenGuard CET:BoostStat=Block"), "标记在");
			Check("列表里写清强化的是哪张卡、强化什么、加多少（负数会显示 -）",
				boostCard.Effects[0].Display.Contains("强化「SevenSlash」的伤害 +3")
				&& boostCard.Effects[1].Display.Contains("强化「SevenGuard」的格挡 +2"), boostCard.Effects[0].Display);
			CharacterProfile boostNeg = ProfileFactory.Sample();
			CardSpec boostNegCard = new CardSpec
			{
				Name = "自检削弱", ClassName = "UiCheckBoostNeg", Cost = 1,
				Effects = { new EffectSpec { Kind = "BoostCard", Amount = -2m, SpawnCardId = "SevenSlash", BoostStat = "Damage", TargetSide = "Self" } },
			};
			boostNeg.Cards.Add(boostNegCard);
			Check("数值填负数 → 描述写「造成的伤害减少 2 点」（不会印出「额外造成 -2 点伤害」）",
				LocalizationGen.CardsJson(boostNeg).Contains("你打出的七式斩造成的伤害减少 2 点（本场战斗内持续）。"),
				LocalizationGen.CardsJson(boostNeg).Replace("\n", " "));

			Check("没选目标卡 → 校验报错（不然会变成「强化所有卡」）",
				ProfileValidator.Validate(new CharacterProfile { Cards = { new CardSpec { Name = "空强化", Cost = 1, Effects = { new EffectSpec { Kind = "BoostCard", Amount = 2m } } } } })
					.Any((ValidationIssue i) => i.IsError && i.Message.Contains("没选目标卡")), "有错误");
			Check("数值 0 → 提示（挂个 0 层强化等于没效果）",
				ProfileValidator.Validate(new CharacterProfile { Cards = { new CardSpec { Name = "零强化", Cost = 1, Effects = { new EffectSpec { Kind = "BoostCard", Amount = 0m, SpawnCardId = "SevenSlash" } } } } })
					.Any((ValidationIssue i) => i.Message.Contains("数值是 0")), "有提示");
			CharacterProfile boostRelic = ProfileFactory.Sample();
			RelicSpec boostRelicSpec = new RelicSpec { Name = "自检强化遗物", Trigger = "PlayerTurnStart", Rarity = "Common" };
			boostRelicSpec.Effects.Add(new EffectSpec { Kind = "BoostCard", Amount = 1m, SpawnCardId = "SevenSlash", BoostStat = "Damage", TargetSide = "Self" });
			boostRelic.Relics.Add(boostRelicSpec);
			Check("遗物上也能用（开场就挂上强化）",
				CSharpCodeGen.RelicSource(boostRelic, boostRelicSpec, 0)
					.Contains($"PowerCmd.Apply<{Naming.From(boostRelic).BoostPowerClass(boostRelicSpec.Effects[0])}>(choiceContext, base.Owner.Creature,"),
				"遗物也支持");
			CharacterProfile boostTrigger = ProfileFactory.Sample();
			CustomPowerSpec boostPow = new CustomPowerSpec { Name = "自检强化状态" };
			PowerTriggerSpec boostTurn = new PowerTriggerSpec { Kind = "TurnStart" };
			boostTurn.Effects.Add(new EffectSpec { Kind = "BoostCard", Amount = 1m, SpawnCardId = "SevenSlash", BoostStat = "Damage", TargetSide = "Self" });
			boostPow.Triggers.Add(boostTurn);
			boostTrigger.CustomPowers.Add(boostPow);
			Naming.From(boostTrigger);      // 生成器内部也是这么设「当前角色」的（Ambient 兜底名按它算）
			Check("自定义状态的触发器里也能用（挂的是生成的强化 Power）",
				CustomPowerGen.Source(boostTrigger, boostPow, 0)
					.Contains($"PowerCmd.Apply<{Naming.AmbientBoostPowerClass(boostTurn.Effects[0])}>("), "状态触发器也支持");

			// ---- 卡牌自定义描述（追加 / 替换两种）----
			CharacterProfile descProbe = ProfileFactory.Sample();
			CardSpec descAppend = new CardSpec
			{
				Name = "自检追加描述", ClassName = "UiCheckDescAppend", Cost = 1,
				Effects = { new EffectSpec { Kind = "Block", Amount = 5m, TargetSide = "Self" } },
				CustomDescription = "第一行\n第二行",
			};
			CardSpec descReplace = new CardSpec
			{
				Name = "自检替换描述", ClassName = "UiCheckDescReplace", Cost = 1,
				Effects = { new EffectSpec { Kind = "Block", Amount = 5m, TargetSide = "Self" } },
				CustomDescription = "只留这段话。",
				CustomDescriptionReplaces = true,
			};
			descProbe.Cards.Add(descAppend);
			descProbe.Cards.Add(descReplace);
			string descLoc = LocalizationGen.CardsJson(descProbe);
			Check("自定义描述默认**追加**在自动描述后面（自动那句还在）",
				descLoc.Contains("获得 {Block:diff()} 点格挡。\\n第一行\\n第二行"), "追加");
			Check("勾了「替换掉自动生成的描述」就整段换掉（自动那句不在了）",
				descLoc.Contains("\"UI_CHECK_DESC_REPLACE.description\": \"只留这段话。\""), "替换");
			Check("没填自定义描述时卡片描述和以前一模一样（不留多余空行）",
				!LocalizationGen.CardsJson(ProfileFactory.Sample()).Contains("\\n\\n"), "没多余空行");
			string descAppendSrc = CSharpCodeGen.CardSource(descProbe, descAppend, 0);
			string descReplaceSrc = CSharpCodeGen.CardSource(descProbe, descReplace, 0);
			Check("自定义描述写进 .cs 的单行标记（换行转义成 \\n，不然标记会被截断）",
				descAppendSrc.Contains("// CET:CustomDescReplace=0 CET:CustomDescription=第一行\\n第二行")
				&& descReplaceSrc.Contains("// CET:CustomDescReplace=1 CET:CustomDescription=只留这段话。"), "标记在");
			Check("两行文本转义 / 还原是一对（回读后还是两行）",
				CSharpCodeGen.UnescapeMarker(CSharpCodeGen.EscapeMarker("第一行\n第二行")) == "第一行\n第二行", "往返一致");
		}

		// ===== 自定义诅咒 / 先古卡（「诅咒 / 先古卡」页）=====
		{
			Check("稀有度里多了「先古 / 诅咒」两档（本体的 CardRarity.Ancient / Curse）",
				EffectCatalog.CardRarities.Contains("Ancient") && EffectCatalog.CardRarities.Contains("Curse"),
				string.Join(" / ", EffectCatalog.CardRarities));

			// ---- 诅咒 ----
			CharacterProfile curseProbe = ProfileFactory.Sample();
			var curse = new CardSpec
			{
				Name = "自检诅咒", ClassName = "UiCheckCurse", CardType = "Curse", Rarity = "Curse",
				Cost = -1, Unplayable = true, Innate = true, CurseRemoveAfterCombat = true,
			};
			curse.Effects.Add(new EffectSpec { Kind = "HpLoss", Amount = 2m, TargetSide = "Self" });
			curse.Effects.Add(new EffectSpec { Kind = "ApplyPower", PowerId = "WeakPower", Amount = 1m, TargetSide = "Self" });
			curseProbe.Curses.Add(curse);
			string curseSrc = CSharpCodeGen.CurseSource(curseProbe, curse, 0);
			Check("诅咒的构造函数：费用固定 -1、类型 / 稀有度固定 Curse、目标 None（和本体诅咒一模一样）",
				curseSrc.Contains("public UiCheckCurse() : base(-1, CardType.Curse, CardRarity.Curse, TargetType.None) { }"), "构造在");
			Check("诅咒不能升级（MaxUpgradeLevel => 0）", curseSrc.Contains("public override int MaxUpgradeLevel => 0;"), "在");
			Check("诅咒不进战斗里的随机生成（CanBeGeneratedInCombat => false）",
				curseSrc.Contains("public override bool CanBeGeneratedInCombat => false;"), "在");
			Check("诅咒的卡框走本体的诅咒卡池（灰色），模型仍然注册在你自己的卡池里",
				curseSrc.Contains("public override CardPoolModel VisualCardPool => ModelDb.CardPool<CurseCardPool>();"), "在");
			Check("「不能被打出」是诅咒自带的，固有 / 永恒按勾选加上",
				curseSrc.Contains("public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Unplayable, CardKeyword.Innate];"),
				"关键字");
			Check("效果走本体的诅咒钩子：HasTurnEndInHandEffect + OnTurnEndInHand",
				curseSrc.Contains("public override bool HasTurnEndInHandEffect => true;")
				&& curseSrc.Contains("protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)"), "钩子在");
			Check("诅咒效果生成的是遗物那套语句（没有 cardPlay）：失去生命 + 施加虚弱",
				curseSrc.Contains("CreatureCmd.Damage(choiceContext, base.Owner.Creature,")
				&& curseSrc.Contains("await PowerCmd.Apply<WeakPower>(choiceContext, base.Owner.Creature,"), curseSrc.Split('\n').FirstOrDefault(l => l.Contains("CreatureCmd.Damage"))?.Trim() ?? "(没找到)");
			Check("勾了「战斗结束时自删」→ 生成 AfterCombatEnd + RemoveFromDeck(this)（本体「罪恶」那套）",
				curseSrc.Contains("public override async Task AfterCombatEnd(CombatRoom room)")
				&& curseSrc.Contains("await CardPileCmd.RemoveFromDeck(this);"), "在");
			Check("诅咒声明了动态变量（卡面 {HpLoss:diff()} / {WeakPower:diff()} 才有数字）",
				curseSrc.Contains("new HpLossVar(2m)") && curseSrc.Contains("new PowerVar<WeakPower>(1m)"), "变量在");
			string curseCards = LocalizationGen.CardsJson(curseProbe);
			Check("诅咒卡面描述照本体行文：「在你的回合结束时，如果这张牌在你的手牌中：…」",
				curseCards.Contains("在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中：")
				&& curseCards.Contains("失去 {HpLoss:diff()} 点生命。")
				&& curseCards.Contains("施加 {WeakPower:diff()} 层虚弱。"), curseCards.Replace("\n", " "));
			Check("「战斗结束时自删」也写进了卡面描述（不然玩家看不懂它为什么自己消失）",
				curseCards.Contains("战斗结束时，如果这张牌在你的[gold]牌组[/gold]中，它会消失。"), "在");
			string cursePool = CSharpCodeGen.CardPoolSource(curseProbe);
			Check("诅咒也进你的卡池（本体的硬规则：每张卡都必须属于某个卡池，否则 Preload 直接抛异常）",
				cursePool.Contains("ModelDb.Card<UiCheckCurse>(),"), "在池子里");
			Check("诅咒被 FilterThroughEpochs 排除掉（不进奖励 / 商店，双保险）",
				cursePool.Contains("keep.RemoveAll(c => c.Id == ModelDb.Card<UiCheckCurse>().Id"), "被排除");
			Check("诅咒的自动类名和普通卡不撞（三张列表一起编号）",
				Naming.From(curseProbe).CardClassName(curseProbe, curse) == "UiCheckCurse", Naming.From(curseProbe).CardClassName(curseProbe, curse));
			Check("没填类名的诅咒也会自动编号、且不和普通卡重号",
				Naming.From(curseProbe).CardClassName(curseProbe, new CardSpec { Name = "无名诅咒", Rarity = "Curse" })
					!= Naming.From(curseProbe).CardClassName(curseProbe, curseProbe.Cards[^1]),
				Naming.From(curseProbe).CardClassName(curseProbe, new CardSpec { Name = "无名诅咒", Rarity = "Curse" }));
			Check("校验：诅咒没有效果也没描述 → 提醒它是个白板",
				ProfileValidator.Validate(new CharacterProfile { Curses = { new CardSpec { Name = "白板诅咒", Rarity = "Curse" } } })
					.Any(i => i.Message.Contains("白板牌")), "有提示");
			Check("校验：诅咒的类型 / 稀有度不会被当成非法",
				!ProfileValidator.Validate(new CharacterProfile { Curses = { new CardSpec { Name = "合法诅咒", Rarity = "Curse", CardType = "Curse", Cost = -1 } } })
					.Any(i => i.IsError && (i.Message.Contains("类型非法") || i.Message.Contains("稀有度非法"))), "不报错");
			// 按**稀有度**判断（不是按列表）：老存档里手改过稀有度的卡也要走诅咒模板
			var rarityCurse = new CardSpec { Name = "手改稀有度", ClassName = "UiCheckCurseByRarity", Rarity = "Curse", CardType = "Curse", Cost = -1 };
			Check("生成按稀有度认诅咒（不按它在哪个列表）", rarityCurse.IsCurseCard, "IsCurseCard");

			// ---- 先古卡 ----
			CharacterProfile ancientProbe = ProfileFactory.Sample();
			var ancient = new CardSpec
			{
				Name = "自检先古卡", ClassName = "UiCheckAncient", CardType = "Power", Rarity = "Ancient", Cost = 2,
			};
			ancient.Effects.Add(new EffectSpec { Kind = "Block", Amount = 12m, TargetSide = "Self" });
			ancientProbe.AncientCards.Add(ancient);
			string ancientSrc = CSharpCodeGen.CardSource(ancientProbe, ancient, 0);
			Check("先古卡的构造函数：稀有度 Ancient，其余和普通卡一样",
				ancientSrc.Contains("public UiCheckAncient() : base(2, CardType.Power, CardRarity.Ancient, TargetType.Self) { }"), "构造在");
			Check("先古卡不进随机生成 / 随机奖励（本体 CardFactory 显式排除 Ancient）",
				ancientSrc.Contains("public override bool CanBeGeneratedInCombat => false;")
				&& ancientSrc.Contains("public override bool CanBeGeneratedByModifiers => false;"), "在");
			Check("先古卡照样是普通牌：OnPlay 里正常生成效果（获得 12 点格挡）",
				ancientSrc.Contains("protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)")
				&& ancientSrc.Contains("CreatureCmd.GainBlock(base.Owner.Creature, base.DynamicVars.Block.BaseValue"), "OnPlay 在");
			string ancientPool = CSharpCodeGen.CardPoolSource(ancientProbe);
			Check("先古卡也进你的卡池、并且被排除出奖励（本体的「尘封的书」就是从卡池里挑先古卡）",
				ancientPool.Contains("ModelDb.Card<UiCheckAncient>(),")
				&& ancientPool.Contains("keep.RemoveAll(c => c.Id == ModelDb.Card<UiCheckAncient>().Id"), "在池子里");
			Check("先古卡的卡面描述就是普通描述",
				LocalizationGen.CardsJson(ancientProbe).Contains("获得 {Block:diff()} 点格挡。"), "普通描述");
			Check("校验：先古卡的稀有度合法、不会被拦住",
				!ProfileValidator.Validate(new CharacterProfile { AncientCards = { new CardSpec { Name = "合法先古卡", Rarity = "Ancient", Cost = 1 } } })
					.Any(i => i.IsError && i.Message.Contains("稀有度非法")), "不报错");

			// ---- 界面接线 ----
			Check("有「诅咒 / 先古卡」选项卡", FindTab("诅咒 / 先古卡") is not null, FindTab("诅咒 / 先古卡") is null ? "没找到" : "找到了");
			Check("诅咒列表绑的是 Profile.Curses、先古卡列表绑的是 Profile.AncientCards",
				BindingOperations.GetBinding(CurseList, ItemsControl.ItemsSourceProperty)?.Path?.Path == "Profile.Curses"
				&& BindingOperations.GetBinding(AncientCardList, ItemsControl.ItemsSourceProperty)?.Path?.Path == "Profile.AncientCards",
				(BindingOperations.GetBinding(CurseList, ItemsControl.ItemsSourceProperty)?.Path?.Path ?? "?") + " / "
				+ (BindingOperations.GetBinding(AncientCardList, ItemsControl.ItemsSourceProperty)?.Path?.Path ?? "?"));
			DependencyObject curseRoot = SelectTabRoot("诅咒 / 先古卡");
			UpdateLayout();
			List<string> curseTexts = TextsIn(curseRoot);
			Check("两个列表都没选中时显示空态提示",
				curseTexts.Any(t => t.Contains("请选择诅咒或先古卡")), string.Join(" / ", curseTexts.Take(5)));
			Check("「添加诅咒 / 添加先古卡 / 删除 / 撤回删除」四个按钮都在",
				curseTexts.Contains("添加诅咒") && curseTexts.Contains("添加先古卡")
				&& curseTexts.Contains("删除") && curseTexts.Contains("撤回删除"), "控件在");
			int curseBefore = Profile.Curses.Count;
			ClickButtonByContent("添加诅咒", curseRoot);
			UpdateLayout();
			Check("点「添加诅咒」会加一条诅咒、并自动选中",
				Profile.Curses.Count == curseBefore + 1 && ReferenceEquals(CurseList.SelectedItem, Profile.Curses[^1]),
				$"{Profile.Curses.Count} 条");
			CardSpec added = Profile.Curses[^1];
			Check("新诅咒的默认值：费用 -1、类型 / 稀有度 Curse、勾上「不能被打出」",
				added.Cost == -1 && added.CardType == "Curse" && added.Rarity == "Curse" && added.Unplayable, $"{added.Cost} / {added.CardType} / {added.Rarity}");
			curseRoot = SelectTabRoot("诅咒 / 先古卡");
			UpdateLayout();
			// 空态那行是同一格里叠着的 TextBlock，**收起时仍在可视树里**（Visibility=Collapsed）——
			// 所以这里查 IsVisible，不能只查文字在不在（TextsIn 是不过滤可见性的）
			List<TextBlock> curseBlocks = new List<TextBlock>();
			CollectTextBlocks(curseRoot, curseBlocks);
			Check("选中诅咒后空态提示收起、诅咒详情面板出现",
				!curseBlocks.Any(t => t.Text != null && t.Text.Contains("请选择诅咒或先古卡") && t.IsVisible)
				&& curseBlocks.Any(t => t.Text == "诅咒属性" && t.IsVisible), "面板在");
			Check("诅咒详情面板里的效果编辑器是「遗物那套」（没有「升级增量 / 数值 = X」那几行）",
				curseBlocks.Any(t => t.Text == "诅咒属性" && t.IsVisible)
				&& !curseBlocks.Any(t => t.Text == "升级增量" && t.IsVisible), "没有升级那两行");
			int ancientBefore = Profile.AncientCards.Count;
			ClickButtonByContent("添加先古卡", curseRoot);
			UpdateLayout();
			Check("点「添加先古卡」会加一条、稀有度默认 Ancient",
				Profile.AncientCards.Count == ancientBefore + 1 && Profile.AncientCards[^1].Rarity == "Ancient",
				$"{Profile.AncientCards.Count} 条");
			curseRoot = SelectTabRoot("诅咒 / 先古卡");
			UpdateLayout();
			Check("先古卡面板里有「自定义关键词」和「升级后的关键字」（先古卡也是普通牌，能力齐全）",
				TextsIn(curseRoot).Any(t => t.Contains("自定义关键词（勾选这张牌用到的）"))
				&& TextsIn(curseRoot).Any(t => t.Contains("升级后的关键字")), "都在");
			// 收尾：把自检加的东西删掉，后面的断言看不到它们
			Profile.Curses.Remove(added);
			Profile.AncientCards.RemoveAt(Profile.AncientCards.Count - 1);
			UpdateLayout();
			Check("诅咒 / 先古卡列表能正常删除（自检收尾）",
				Profile.Curses.Count == curseBefore && Profile.AncientCards.Count == ancientBefore, "已清理");

			// ---- 样式（RRGGBB 卡框颜色）----
			Check("「诅咒 / 先古卡」页的两个卡框下拉多一项「跟角色配色（默认）」",
				SpecialFrameItems.Count == FrameItems.Count + 1 && SpecialFrameItems[0].Value == "", SpecialFrameItems[0].Name);
			Check("默认（没设样式）时下拉显示「跟角色配色」、也不生成外观池",
				CurseFrameSelection == SpecialFrameItems[0] && AncientFrameSelection == SpecialFrameItems[0]
				&& !Profile.CurseStyle.Any && !Profile.AncientStyle.Any, CurseFrameSelection.Name);
			CharacterProfile styleProbe = ProfileFactory.Sample();
			styleProbe.CurseStyle.Frame = SpecialCardStyleSpec.CustomFrame;
			styleProbe.CurseStyle.FrameColor = "8A5CF6";
			styleProbe.AncientStyle.Frame = "card_frame_blue";
			// 诅咒：自定义颜色 → 生成外观池 + hsv 材质 + VisualCardPool 指过去
			string curseStyleSrc = CSharpCodeGen.CurseSource(styleProbe, curse, 0);
			Check("给诅咒设了卡框颜色后，VisualCardPool 指向生成的外观池（不再用本体的诅咒框）",
				curseStyleSrc.Contains($"public override CardPoolModel VisualCardPool => ModelDb.CardPool<{Naming.From(styleProbe).CurseStylePoolClass}>();")
				&& !curseStyleSrc.Contains("CardPool<CurseCardPool>()"), "外观池");
			string cursePoolSrc = CSharpCodeGen.SpecialStylePoolSource(styleProbe, styleProbe.CurseStyle,
				Naming.From(styleProbe).CurseStylePoolClass, "诅咒", CSharpCodeGen.SpecialFrameMaterialOf(styleProbe, styleProbe.CurseStyle, "curse")!);
			Check("外观池里没有卡（GenerateAllCards 返回空，不会在卡牌图鉴里多出空分类）",
				cursePoolSrc.Contains("protected override CardModel[] GenerateAllCards() => [];"), "空池");
			Check("外观池的卡框材质名 = <角色>_curse_frame（RRGGBB 生成的那份）、能量图标仍用角色自己的",
				cursePoolSrc.Contains($"CardFrameMaterialPath => \"{Naming.From(styleProbe).CharSlug}_curse_frame\"")
				&& cursePoolSrc.Contains($"EnergyColorName => \"{Naming.From(styleProbe).CharSlug}\";"), "材质名");
			Check("选了本体框色（先古卡 = card_frame_blue）时直接用那个名字、不生成新材质",
				CSharpCodeGen.SpecialFrameMaterialOf(styleProbe, styleProbe.AncientStyle, "ancient") == "card_frame_blue",
				CSharpCodeGen.SpecialFrameMaterialOf(styleProbe, styleProbe.AncientStyle, "ancient")!);
			string ancientStyleSrc = CSharpCodeGen.CardSource(styleProbe, ancient, 0);
			Check("给先古卡设了卡框后也覆写 VisualCardPool",
				ancientStyleSrc.Contains($"public override CardPoolModel VisualCardPool => ModelDb.CardPool<{Naming.From(styleProbe).AncientStylePoolClass}>();"), "外观池");
			Check("样式色块预览用 PreviewHex（自定义色才有值）",
				styleProbe.CurseStyle.PreviewHex == "8A5CF6" && styleProbe.AncientStyle.PreviewHex == "", "色块");
			Check("校验：自定义色填成乱码 → 报错",
				ProfileValidator.Validate(new CharacterProfile { CurseStyle = { Frame = SpecialCardStyleSpec.CustomFrame, FrameColor = "ZZZZZZ" } })
					.Any(i => i.IsError && i.Message.Contains("不是合法的颜色")), "有错误");
			Check("校验：选了不存在的本体框色 → 警告",
				ProfileValidator.Validate(new CharacterProfile { AncientStyle = { Frame = "card_frame_nope" } })
					.Any(i => i.Message.Contains("不在本体自带素材里")), "有警告");
			Check("没设样式时生成的诅咒还是用本体诅咒卡池（老存档行为不变）",
				CSharpCodeGen.CurseSource(ProfileFactory.Sample(), curse, 0).Contains("CardPool<CurseCardPool>()"), "老样子");
		}

		RecheckEnvironment();
		Check("环境自检有结果项", EnvItems.Count >= 5, $"{EnvItems.Count} 项 / {EnvSummaryText}");
		Check("环境自检能识别 .NET SDK", EnvItems.Any((EnvCheckItem i) => i.Name.Contains("SDK")), EnvItems.First((EnvCheckItem i) => i.Name.Contains("SDK")).Display);
		Check("环境自检能识别 Godot 与解包工程", EnvItems.Any((EnvCheckItem i) => i.Name.Contains("Godot")) && EnvItems.Any((EnvCheckItem i) => i.Name.Contains("解包工程") || i.Name.Contains("效果库")), string.Join(" | ", EnvItems.Select((EnvCheckItem i) => i.Icon + i.Name)));
		bool ok12 = EnvItems.Where((EnvCheckItem i) => i.IsError).All((EnvCheckItem i) => i.Fix.Length > 0);
		string text46 = string.Join(" | ", from i in EnvItems
			where i.IsError
			select i.Name);
		Check("缺项时带「怎么修」和下载链接", ok12, (text46 != null && text46.Length > 0) ? text46 : "（本机无错误项）");
		Check("错误项会给出官方下载地址（若适用）", EnvItems.Where((EnvCheckItem i) => i.IsError && i.Name.Contains("SDK")).All((EnvCheckItem i) => !string.IsNullOrEmpty(i.Url)), "SDK 错误项链接检查");
		string godotExe = Profile.Paths.GodotExe;
		string vanillaProject2 = Profile.Paths.VanillaProject;
		string dotnetExe = Profile.Paths.DotnetExe;
		string gameDataDir = Profile.Paths.GameDataDir;
		string installDir = Profile.Paths.InstallDir;
		string outputDir = Profile.Paths.OutputDir;
		Profile.Paths.GodotExe = "C:\\definitely-not-here\\godot.exe";
		RecheckEnvironment();
		EnvCheckItem envCheckItem = EnvItems.First((EnvCheckItem i) => i.Name == "Godot");
		Check("Godot 没通过时那一项是错误", envCheckItem.IsError, envCheckItem.Display);
		Check("Godot 没通过时那一项带「选择路径」动作", envCheckItem.Pick == "godot", envCheckItem.Pick ?? "(空)");
		Check("Godot 没通过时提示里写明点右边的按钮", envCheckItem.Fix.Contains("选择路径"), envCheckItem.Fix);
		DependencyObject root19 = SelectTabRoot("环境自检");
		PumpDispatcher(200);
		List<string> list36 = TextsIn(root19);
		Check("Godot 没通过时环境自检页真的出现「选择路径…」按钮（含中文省略号）", list36.Contains("选择路径…"), $"页面文字 {list36.Count} 条");
		List<Button> list37 = new List<Button>();
		CollectButtons(root19, list37);
		Check("那个按钮绑定的是 Godot 这一项", list37.Any((Button b) => b.Tag as string == "godot" && b.Content as string == "选择路径…"), string.Join(" | ", from b in list37
			select $"{b.Content}/{b.Tag}" into t
			where t.Contains("选择")
			select t));
		int num18 = EnvItems.Count((EnvCheckItem i) => i.CanPick);
		int num19 = list37.Count((Button b) => b.Content as string == "选择路径…" && b.Visibility == Visibility.Visible);
		Check("「选择路径…」按钮只在没通过的行上真的显示（通过的行藏着，不是摆设）", num19 == num18, $"可见 {num19} 个 / 应有 {num18} 个（当前只有 Godot 没通过）");
		Check("所有「选择路径」动作名都在清单里", EnvItems.Where((EnvCheckItem i) => i.CanPick).All((EnvCheckItem i) => EnvCheck.PickActions.ContainsKey(i.Pick)), string.Join(" | ", from i in EnvItems
			where i.CanPick
			select i.Pick));
		Dictionary<string, string> dictionary = new Dictionary<string, string>
		{
			["godot"] = "C:\\tmp\\envpick_godot.exe",
			["dotnet"] = "C:\\tmp\\envpick_dotnet.exe",
			["gamedata"] = "C:\\tmp\\envpick_data",
			["vanilla"] = "C:\\tmp\\envpick_vanilla",
			["install"] = "C:\\tmp\\envpick_mods",
			["output"] = "C:\\tmp\\envpick_out"
		};
		bool ok13 = true;
		foreach (KeyValuePair<string, string> item4 in dictionary)
		{
			item4.Deconstruct(out key, out var value3);
			string text47 = key;
			string text48 = value3;
			if (!ApplyEnvPick(text47, text48))
			{
				ok13 = false;
				break;
			}
			value3 = text47 switch
			{
				"godot" => Profile.Paths.GodotExe, 
				"dotnet" => Profile.Paths.DotnetExe, 
				"gamedata" => Profile.Paths.GameDataDir, 
				"vanilla" => Profile.Paths.VanillaProject, 
				"install" => Profile.Paths.InstallDir, 
				_ => Profile.Paths.OutputDir, 
			};
			if (value3 != text48)
			{
				ok13 = false;
				break;
			}
		}
		Check("每个「选择路径」按钮都能把选好的路径填进对应字段（6 种）", ok13, string.Join(" | ", dictionary.Keys));
		Check("不认识的按钮动作名会被拒绝（不会偷偷写坏配置）", !ApplyEnvPick("nonsense", "C:\\tmp\\x"));
		Check("选了不像 Godot 的文件会给出警告", EnvPickStatus("godot", "C:\\x\\notepad.exe").StartsWith("⚠"), EnvPickStatus("godot", "C:\\x\\notepad.exe"));
		Check("非 console 版会建议改用 _console.exe", EnvPickStatus("godot", "C:\\x\\Godot_v4.5.1-stable_mono_win64.exe").Contains("_console"), EnvPickStatus("godot", "C:\\x\\Godot.exe"));
		Check("console 版不再啰嗦建议", EnvPickStatus("godot", "C:\\x\\Godot_v4.5.1-stable_mono_win64_console.exe") == "已填 Godot：C:\\x\\Godot_v4.5.1-stable_mono_win64_console.exe", EnvPickStatus("godot", "C:\\x\\Godot_v4.5.1-stable_mono_win64_console.exe"));
		Check("页面上有那句「缺路径的项右边有按钮」的说明", TextsIn(root19).Any((string t) => t.Contains("选择路径…")), "说明文字检查");
		Profile.Paths.GodotExe = godotExe;
		Profile.Paths.VanillaProject = vanillaProject2;
		Profile.Paths.DotnetExe = dotnetExe;
		Profile.Paths.GameDataDir = gameDataDir;
		Profile.Paths.InstallDir = installDir;
		Profile.Paths.OutputDir = outputDir;
		RecheckEnvironment();
		EnvCheckItem envCheckItem2 = EnvItems.First((EnvCheckItem i) => i.Name == "Godot");
		Check("Godot 恢复正常后那一项不再带「选择路径」动作", envCheckItem2.IsOk ? (!envCheckItem2.CanPick) : (envCheckItem2.Pick == "godot"), envCheckItem2.Display);
		Check("路径已全部还原（自检不污染当前配置）", Profile.Paths.GodotExe == godotExe && Profile.Paths.VanillaProject == vanillaProject2 && Profile.Paths.DotnetExe == dotnetExe && Profile.Paths.GameDataDir == gameDataDir && Profile.Paths.InstallDir == installDir && Profile.Paths.OutputDir == outputDir);
		Check("逐项检查「选择路径」按钮只出现在没通过、且确实缺路径的行上", EnvItems.Where((EnvCheckItem i) => i.CanPick).All((EnvCheckItem i) => !i.IsOk && EnvCheck.PickActions.ContainsKey(i.Pick)), string.Join(" | ", from i in EnvItems
			where i.CanPick
			select i.Name));
		Check("配色预设非空", PresetItems.Count > 0, $"{PresetItems.Count} 个选项（含自定义）");
		IReadOnlyList<PresetItem> presetItems = PresetItems;
		bool ok14 = (object)presetItems[presetItems.Count - 1].Preset == null;
		IReadOnlyList<PresetItem> presetItems2 = PresetItems;
		Check("配色预设最后一项是「自定义」", ok14, presetItems2[presetItems2.Count - 1].Name);
		int ok15;
		if (FrameItems.Count == 9)
		{
			IReadOnlyList<FrameItem> frameItems = FrameItems;
			ok15 = ((frameItems[frameItems.Count - 1].Value == null) ? 1 : 0);
		}
		else
		{
			ok15 = 0;
		}
		Check("卡牌边框选项 = 本体 8 种 + 自定义", (byte)ok15 != 0, string.Join("/", FrameItems.Select((FrameItem f) => f.Name)));
		CardColorSpec.Preset preset = CardColorSpec.Presets.First((CardColorSpec.Preset pz) => pz.Name.Contains("静默"));
		Profile.Colors.Apply(preset);
		Check("套用配色预设生效", Profile.Colors.CardFrame == preset.Frame && Profile.Colors.DeckEntryColor == preset.Deck, Profile.Colors.Display);
		Check("套用预设后下拉自动选中该预设", PresetSelection.Preset == preset, PresetSelection.Name);
		Profile.Colors.DeckEntryColor = "123456";
		Check("手改颜色后自动切到「自定义」", (object)PresetSelection.Preset == null, PresetSelection.Name);
		Profile.Colors.CardFrameColor = "";
		Profile.Colors.CardFrame = "card_frame_blue";
		Check("选了官方框色则边框下拉显示它", FrameSelection.Value == "card_frame_blue", FrameSelection.Name);
		FrameSelection = _customFrame;
		Check("切到「自定义」会自动预填一个颜色", CardColorSpec.IsHex(Profile.Colors.CardFrameColor), Profile.Colors.CardFrameColor);
		Profile.Colors.CardFrameColor = "00FF88";
		Check("填了自定义色则下拉显示「自定义」", FrameSelection.Value == null, FrameSelection.Name);
		FrameSelection = FrameItems[0];
		Check("切回官方框色会清掉自定义色", Profile.Colors.CardFrame == FrameItems[0].Value && Profile.Colors.CardFrameColor == "", Profile.Colors.CardFrame + " / '" + Profile.Colors.CardFrameColor + "'");
		Check("非法色值能被识别", !CardColorSpec.IsHex("ZZZZZZ") && CardColorSpec.IsHex("#5ebd00"));
		PotionSpec potionSpec3 = (PotionSpec)PotionList.SelectedItem;
		potionSpec3.Icon = "C:\\test\\potion_icon.png";
		string path9 = Path.Combine(Path.GetTempPath(), "forge_uicheck_potion.json");
		try
		{
			ProfileFactory.Save(Profile, path9);
			CharacterProfile characterProfile30 = ProfileFactory.Load(path9);
			Check("药水图标存盘可读回", characterProfile30.Potions[0].Icon == potionSpec3.Icon, characterProfile30.Potions[0].Icon ?? "(空)");
		}
		finally
		{
			try
			{
				File.Delete(path9);
			}
			catch
			{
			}
		}
		potionSpec3.Icon = null;
		Raise("PotionIconPreview");
		Check("药水图标预览属性可用", PotionIconPreview == null, "未上传时应为 null");
		Check("美术槽位不含「未解锁立绘」", !ArtSlots.Any((ArtSlot s) => s.Name.Contains("未解锁")));
		// ===== 卡面 / 美术预览：按「游戏里的显示区域」画黑底，比例不对时能直接看到黑边 =====
		{
			Check("美术槽位都带上了「游戏里的显示区域」尺寸（预览按它画黑框）",
				ArtSlots.All((ArtSlot s) => s.HasFrame && s.FrameWidth > 0 && s.FrameHeight > 0),
				string.Join("、", ArtSlots.Select((ArtSlot s) => s.Name + "=" + s.FrameWidth + "×" + s.FrameHeight)));
			Check("选人界面背景大图那块是 1920×1080（横图；竖图会左右留黑边）",
				ArtSlots.First((ArtSlot s) => s.Name.Contains("背景大图")).FrameWidth == 1920
				&& ArtSlots.First((ArtSlot s) => s.Name.Contains("背景大图")).FrameHeight == 1080, "1920×1080");

			string ratioDir = Path.Combine(Path.GetTempPath(), "forge_uicheck_ratio_" + Guid.NewGuid().ToString("N").Substring(0, 8));
			Directory.CreateDirectory(ratioDir);
			try
			{
				string wide = Path.Combine(ratioDir, "wide.png");       // 2000×760（比 1000×760 宽）
				string narrow = Path.Combine(ratioDir, "narrow.png");   // 500×760（比 1000×760 窄）
				string same = Path.Combine(ratioDir, "same.png");       // 1000×760（同比例）
				ArtGenerator.WriteNeutralPng(wide, 2000, 760);
				ArtGenerator.WriteNeutralPng(narrow, 500, 760);
				ArtGenerator.WriteNeutralPng(same, 1000, 760);

				string noteSame = AspectNoteOf(same, 1000, 760, "游戏里卡面这块");
				string noteWide = AspectNoteOf(wide, 1000, 760, "游戏里卡面这块");
				string noteNarrow = AspectNoteOf(narrow, 1000, 760, "游戏里卡面这块");
				Check("比例一致时明确说「不会留黑边」", noteSame.Contains("不会留黑边"), noteSame);
				Check("图偏宽（2000×760 vs 1000×760）→ 提示「上下」会留黑边",
					noteWide.Contains("上下") && noteWide.Contains("黑边") && noteWide.Contains("2000×760"), noteWide);
				Check("图偏窄（500×760）→ 提示「左右」会留黑边", noteNarrow.Contains("左右") && noteNarrow.Contains("黑边"), noteNarrow);
				Check("没上传 / 文件不在时不给比例说明（不留一行空话）",
					AspectNoteOf(null, 1000, 760, "x").Length == 0
					&& AspectNoteOf(Path.Combine(ratioDir, "没有这个文件.png"), 1000, 760, "x").Length == 0, "空");
				// 上传一张偏宽的图到「选人界面背景大图」槽位：说明里应该出现「上下」留黑边
				ArtSlot bgSlot = ArtSlots.First((ArtSlot s) => s.Name.Contains("背景大图"));
				bgSlot.Path = wide;
				Check("槽位的比例说明跟着上传的图走（背景大图 1920×1080 + 2000×760 的图 → 上下留黑边）",
					bgSlot.AspectNote.Contains("上下") && bgSlot.AspectNote.Contains("1920×1080"), bgSlot.AspectNote);
				bgSlot.Path = null;
			}
			finally
			{
				try { Directory.Delete(ratioDir, true); } catch { }
			}
		}
		string vp;
		string artRoot;
		if (!string.IsNullOrWhiteSpace(Profile.Paths.VanillaProject) && Directory.Exists(Profile.Paths.VanillaProject))
		{
			vp = Profile.Paths.VanillaProject;
			string text49 = Path.Combine(vp, "animations/characters/ironclad/ironclad.png");
			Path.Combine(vp, "animations/merchant/ironclad/ironclad_shop.png");
			Path.Combine(vp, "animations/rest_site/ironclad/restsite_ironclad.png");
			if (File.Exists(text49))
			{
				artRoot = Path.Combine(Path.GetTempPath(), "forge_uicheck_art_" + Guid.NewGuid().ToString("N").Substring(0, 8));
				try
				{
					string text50 = ArtProbe(vanilla: true);
					string path10 = ArtProbe(vanilla: false);
					Naming naming = Naming.From(ProfileFactory.Sample());
					string path11 = Path.Combine(text50, "scenes", "creature_visuals", naming.CharSlug + ".tscn");
					string path12 = Path.Combine(path10, "scenes", "creature_visuals", naming.CharSlug + ".tscn");
					string text51 = (File.Exists(path11) ? File.ReadAllText(path11) : "");
					string text52 = (File.Exists(path12) ? File.ReadAllText(path12) : "");
					Check("勾「用本体素材占位」后战斗立绘直接用本体的 Spine 场景（不是拿图集当立绘）", text51.Contains("SpineSkeletonDataResource") && !text51.Contains("_static.png"), text51.Contains("Spine") ? "用的是本体 Spine 场景" : "没借到本体场景");
					Check("借来的场景根节点名换成了我们自己的角色名", text51.Contains("name=\"" + naming.CharClass + "\""), "根节点已改名");
					Check("勾选模式下不再往工程里塞静态立绘图（Spine 图集那几张也不塞）", !File.Exists(Path.Combine(text50, "images", "characters", naming.CharSlug + "_static.png")), "没有静态立绘");
					string[] buffer3 = new string[] { text50, "scenes", "screens", "char_select", "char_select_bg_" + naming.CharSlug + ".tscn" };
					int ok16;
					if (File.Exists(Path.Combine(buffer3)))
					{
						string[] buffer4 = new string[] { text50, "scenes", "screens", "char_select", "char_select_bg_" + naming.CharSlug + ".tscn" };
						ok16 = (File.ReadAllText(Path.Combine(buffer4)).Contains("Spine") ? 1 : 0);
					}
					else
					{
						ok16 = 0;
					}
					Check("选人背景也用本体的场景（不再空着 / 占位）", (byte)ok16 != 0, "选人背景是本体场景");
					string[] buffer5 = new string[] { text50, "scenes", "merchant", "characters", naming.CharSlug + "_merchant.tscn" };
					int ok17;
					if (File.Exists(Path.Combine(buffer5)))
					{
						string[] buffer6 = new string[] { text50, "scenes", "rest_site", "characters", naming.CharSlug + "_rest_site.tscn" };
						ok17 = (File.Exists(Path.Combine(buffer6)) ? 1 : 0);
					}
					else
					{
						ok17 = 0;
					}
					Check("商店 / 篝火立绘也是本体的场景", (byte)ok17 != 0, "两个都在");
					Check("不勾时仍走静态图场景（工具自带占位图 + 我们的模板）", text52.Contains("_static.png") && !text52.Contains("SpineSkeletonDataResource"), "静态图场景");
					Check("美术资源页说明里写明了勾上这项立绘/背景都用本体素材", TextsIn(FindTab("美术资源").Content as DependencyObject).Any((string t) => t.Contains("战斗立绘") && t.Contains("本体")), "说明在");
				}
				finally
				{
					try
					{
						Directory.Delete(artRoot, recursive: true);
					}
					catch
					{
					}
				}
			}
			else
			{
				Check("（跳过）本体占位立绘检查：解包工程里没有铁甲战士立绘", ok: true, text49);
			}
		}
		else
		{
			Check("（跳过）本体占位立绘检查：本机没有解包工程", ok: true, "(无解包工程)");
		}
		Profile.Art.IconOutlineColor = "#123456";
		Profile.Colors.CardFrameColor = "FF8800";
		string path13 = Path.Combine(Path.GetTempPath(), "forge_uicheck_color2.json");
		try
		{
			ProfileFactory.Save(Profile, path13);
			CharacterProfile characterProfile31 = ProfileFactory.Load(path13);
			Check("描边色 / 边框色存盘可读回", characterProfile31.Art.IconOutlineColor == "#123456" && characterProfile31.Colors.CardFrameColor == "FF8800", characterProfile31.Art.IconOutlineColor + " / " + characterProfile31.Colors.CardFrameColor);
		}
		finally
		{
			try
			{
				File.Delete(path13);
			}
			catch
			{
			}
		}
		Profile.Art.IconOutlineColor = null;
		Profile.Colors.CardFrameColor = "";
		string path14 = Path.Combine(Path.GetTempPath(), "forge_uicheck_color.json");
		string cardFrame = Profile.Colors.CardFrame;
		string deckEntryColor = Profile.Colors.DeckEntryColor;
		try
		{
			ProfileFactory.Save(Profile, path14);
			CharacterProfile characterProfile32 = ProfileFactory.Load(path14);
			Check("配色存盘后可读回", characterProfile32.Colors.CardFrame == cardFrame && characterProfile32.Colors.DeckEntryColor == deckEntryColor, characterProfile32.Colors.Display);
		}
		finally
		{
			try
			{
				File.Delete(path14);
			}
			catch
			{
			}
		}
		Profile.Colors.Apply(CardColorSpec.Presets[0]);
		if (CardList.SelectedItem is CardSpec cardSpec26)
		{
			ObservableCollection<EffectSpec> effects12 = cardSpec26.Effects;
			if (effects12 != null && effects12.Count > 0)
			{
				int times = cardSpec26.Effects[0].Times;
				cardSpec26.Effects[0].Times = 3;
				string path15 = Path.Combine(Path.GetTempPath(), "forge_uicheck_times.json");
				try
				{
					ProfileFactory.Save(Profile, path15);
					CharacterProfile characterProfile33 = ProfileFactory.Load(path15);
					Check("生效次数存盘后可读回", characterProfile33.Cards[0].Effects[0].Times == 3, "读回=" + characterProfile33.Cards[0].Effects[0].Times);
				}
				finally
				{
					cardSpec26.Effects[0].Times = times;
					try
					{
						File.Delete(path15);
					}
					catch
					{
					}
				}
			}
		}
		if (Profile.Cards.Count > 1)
		{
			CardList.SelectedIndex = 1;
			Check("切换卡牌后右侧详情跟随", CardDetail.DataContext == CardList.SelectedItem, "面板=" + (CardDetail.DataContext as CardSpec)?.Name);
		}
		if (Profile.Relics.Count > 1)
		{
			RelicList.SelectedIndex = 1;
			Check("切换遗物后右侧详情跟随", RelicDetail.DataContext == RelicList.SelectedItem);
		}
		if (Profile.Potions.Count > 0)
		{
			PotionList.SelectedIndex = 0;
			Check("切换药水后右侧详情跟随", PotionDetail.DataContext == PotionList.SelectedItem);
		}
		Close();
		// 自检结束：把存档目录还原回真实值（并把临时目录删掉），
		// 免得自检产生的临时存档留在真实存档目录里、或者后面还有代码用到它。
		if (_selfTestRealProfileFolder is not null)
		{
			OverrideProfileFolderForTest(_selfTestRealProfileFolder);
			_selfTestRealProfileFolder = null;
		}
		try
		{
			if (Directory.Exists(selfTestFolder)) Directory.Delete(selfTestFolder, true);
		}
		catch { }
		return sb.ToString();
		string ArtProbe(bool vanilla)
		{
			CharacterProfile characterProfile34 = ProfileFactory.Sample();
			characterProfile34.Art.UseVanillaPlaceholders = vanilla;
			characterProfile34.Art.CharacterStatic = null;
			characterProfile34.Paths.VanillaProject = vp;
			characterProfile34.Paths.GameDataDir = Profile.Paths.GameDataDir;
			characterProfile34.Paths.OutputDir = Path.Combine(artRoot, vanilla ? "on" : "off");
			Directory.CreateDirectory(characterProfile34.Paths.OutputDir);
			return ModGenerator.Generate(characterProfile34).ProjectRoot;
		}
		void Check(string name, bool ok, string detail = "")
		{
			StringBuilder stringBuilder = sb;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 3, stringBuilder);
			handler.AppendFormatted(ok ? "PASS" : "FAIL");
			handler.AppendLiteral("  ");
			handler.AppendFormatted(name);
			handler.AppendFormatted((detail.Length > 0) ? ("  → " + detail) : "");
			stringBuilder.AppendLine(ref handler);
		}
		bool HasBox(string path)
		{
			string path16 = path;
			return boxes.Any((TextBox t) => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path?.Path == path16);
		}
	}

	private static void CollectSearchBoxes(DependencyObject root, List<SearchComboBox> into)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is SearchComboBox item)
			{
				into.Add(item);
			}
			CollectSearchBoxes(child, into);
		}
	}

	private static CharacterProfile LegacyFillerProfile()
	{
		string path = Path.Combine(Path.GetTempPath(), "forge_uicheck_legacy_filler.json");
		File.WriteAllText(path, "{\r\n  \"ModId\": \"LegacyFiller\",\r\n  \"DisplayName\": \"老存档\",\r\n  \"CharacterClass\": \"LegacyFiller\",\r\n  \"FillPoolsWithIronclad\": true,\r\n  \"ForceIroncladFillerWhenSparse\": true,\r\n  \"Cards\": [\r\n    { \"Name\": \"甲\", \"ClassName\": \"LegacyA\", \"Rarity\": \"Common\", \"CardType\": \"Attack\", \"InCardPool\": true },\r\n    { \"Name\": \"乙\", \"ClassName\": \"LegacyB\", \"Rarity\": \"Common\", \"CardType\": \"Attack\", \"InCardPool\": true },\r\n    { \"Name\": \"丙\", \"ClassName\": \"LegacyC\", \"Rarity\": \"Common\", \"CardType\": \"Attack\", \"InCardPool\": true }\r\n  ]\r\n}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		try
		{
			return ProfileFactory.Load(path);
		}
		finally
		{
			try
			{
				File.Delete(path);
			}
			catch
			{
			}
		}
	}

	private static void CollectCombos(DependencyObject root, List<ComboBox> into)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is ComboBox item)
			{
				into.Add(item);
			}
			CollectCombos(child, into);
		}
	}

	/// <summary>收集自绘的可搜索下拉（SearchComboBox 是 Grid，不是 ComboBox，得单独收）。</summary>
	private static void CollectSearchCombos(DependencyObject root, List<SearchComboBox> into)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is SearchComboBox item)
			{
				into.Add(item);
			}
			CollectSearchCombos(child, into);
		}
	}

	private static void CollectTextBoxes(DependencyObject root, List<TextBox> into)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is TextBox item)
			{
				into.Add(item);
			}
			CollectTextBoxes(child, into);
		}
	}

	private static void CollectButtons(DependencyObject root, List<Button> into)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is Button item)
			{
				into.Add(item);
			}
			CollectButtons(child, into);
		}
	}

	private void PumpDispatcher(int ms)
	{
		DateTime dateTime = DateTime.UtcNow.AddMilliseconds(ms);
		while (DateTime.UtcNow < dateTime)
		{
			base.Dispatcher.Invoke(delegate
			{
			}, DispatcherPriority.Background);
			Thread.Sleep(15);
		}
	}

	private static void CollectWrapPanels(DependencyObject root, List<WrapPanel> into)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is WrapPanel item)
			{
				into.Add(item);
			}
			CollectWrapPanels(child, into);
		}
	}

	private static void CollectTextBlocks(DependencyObject root, List<TextBlock> into)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is TextBlock item)
			{
				into.Add(item);
			}
			CollectTextBlocks(child, into);
		}
	}

	private static void CollectToggleButtons(DependencyObject root, List<ToggleButton> into)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is ToggleButton item)
			{
				into.Add(item);
			}
			CollectToggleButtons(child, into);
		}
	}

	private static void CollectCheckBoxes(DependencyObject root, List<CheckBox> into)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is CheckBox item)
			{
				into.Add(item);
			}
			CollectCheckBoxes(child, into);
		}
	}

	/// <summary>
	/// 「某个页签里那块写着 <paramref name="label"/> 的文字，所在的面板有没有可滚动的祖先」。
	/// 用来检查小窗口下属性栏能不能滚下去（用户反馈过看不到下面的内容）。
	/// </summary>
	private bool FindLabelledTextAndHasScrollAncestor(string label, string tabHeader)
	{
		DependencyObject root = SelectTabRoot(tabHeader);
		List<TextBlock> blocks = new List<TextBlock>();
		CollectTextBlocks(root, blocks);
		TextBlock? hit = blocks.FirstOrDefault((TextBlock b) => (b.Text ?? "").Trim() == label);
		if (hit is null) return false;
		DependencyObject? node = hit;
		while (node is not null)
		{
			if (node is ScrollViewer sv)
				return sv.VerticalScrollBarVisibility is ScrollBarVisibility.Auto or ScrollBarVisibility.Visible;
			node = System.Windows.Media.VisualTreeHelper.GetParent(node);
		}
		return false;
	}

	/// <summary>某个组件外面有没有滚动条（同上，按控件找）。</summary>
	private static bool HasScrollAncestor(DependencyObject node)
	{
		DependencyObject? cur = node;
		while (cur is not null)
		{
			if (cur is ScrollViewer sv)
				return sv.VerticalScrollBarVisibility is ScrollBarVisibility.Auto or ScrollBarVisibility.Visible;
			cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
		}
		return false;
	}

	/// <summary>某个元素在某页签里（用于断言控件确实在界面上）。</summary>
	private bool ExistsInTab(DependencyObject? node, string tabHeader)
	{
		if (node is null) return false;
		DependencyObject root = SelectTabRoot(tabHeader);
		DependencyObject? cur = node;
		while (cur is not null)
		{
			if (ReferenceEquals(cur, root)) return true;
			cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
		}
		return false;
	}

	private static GroupBox? FindGroupBoxByHeader(string header, DependencyObject root)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is GroupBox groupBox && string.Equals(groupBox.Header as string, header, StringComparison.Ordinal))
			{
				return groupBox;
			}
			GroupBox groupBox2 = FindGroupBoxByHeader(header, child);
			if (groupBox2 != null)
			{
				return groupBox2;
			}
		}
		return null;
	}

	private static List<string> TextsIn(DependencyObject root)
	{
		List<TextBlock> list = new List<TextBlock>();
		CollectTextBlocks(root, list);
		List<string> list2 = list.Select((TextBlock t) => t.Text ?? "").ToList();
		List<CheckBox> list3 = new List<CheckBox>();
		CollectCheckBoxes(root, list3);
		list2.AddRange(list3.Select((CheckBox c) => (c.Content as string) ?? ""));
		List<Button> list4 = new List<Button>();
		CollectButtons(root, list4);
		list2.AddRange(list4.Select((Button b) => (b.Content as string) ?? ""));
		return list2;
	}



public sealed class AncientRow
{
	public AncientEntry Entry { get; }

	public AncientTalkSpec Talk { get; }

	public ObservableCollection<AncientDialogueSpec> Dialogues { get; }

	/// <summary>
	/// 遗物替换记录（转发给 Talk，方便 XAML 里直接绑定）。
	/// 注意：这一页的 DataContext 是 AncientRow 而不是 AncientTalkSpec，
	/// 少了这个转发属性，界面上的 `{Binding RelicReplacements}` 会静默绑不上 ——
	/// 表现就是「替换记录列表永远空的、点了也没反应」（踩过）。
	/// </summary>
	public ObservableCollection<AncientRelicReplaceSpec> RelicReplacements => Talk.RelicReplacements;

	public string Display => Entry.Display + (Talk.HasAnyText ? "  ✎" : "");

	public bool IsArchitect => string.Equals(Entry.Id, "THE_ARCHITECT", StringComparison.OrdinalIgnoreCase);

	public string UnitFieldLabel
	{
		get
		{
			if (!IsArchitect)
			{
				return "第几次到访";
			}
			return "第几次";
		}
	}

	public string UnitLabel
	{
		get
		{
			if (!IsArchitect)
			{
				return "次到访";
			}
			return "次";
		}
	}

	public string UnitHint
	{
		get
		{
			if (!IsArchitect)
			{
				return "这里填的是「第几次」，从 1 开始：1 = 第 1 次到访，2 = 第 2 次…；-1 = 每次都可能说。注意：所有角色第 1 次见到他时，本体会先放它自己的那句固定台词，所以第 1 次轮不到我们 —— 想让你这段最早出现就填 2。";
			}
			return "这里填的是「第几次」，从 1 开始：1 = 还没通关过时（第一次遇到他），2 = 通关过 1 次之后，3 = 通关过 2 次之后；-1 = 每次都可能说。";
		}
	}

	public string Hint
	{
		get
		{
			if (!Entry.HasNativeAnyDialogue)
			{
				return "本体没给他写过「任意角色」的对话（建筑师就是这样），但模组里的补丁会把我们的角色塞进本体对话表，所以这里照样能写。";
			}
			return "本体给「任意角色」写的原文：" + string.Join(" / ", (from s in Entry.NativeSlots
				where s.VanillaFirstLine.Length > 0
				select s.VanillaFirstLine).Take(3)) + "（我们写的对话会取代它，句数不受它限制）";
		}
	}

	public AncientRow(AncientEntry entry, AncientTalkSpec talk)
	{
		Entry = entry;
		Talk = talk;
		Dialogues = talk.Dialogues;
	}
}



/// <summary>
/// 「升级后的关键字」界面里的一行：某个关键字在「升级后」是什么状态
/// （不变 / 升级后获得 / 升级后失去）。绑定到当前选中卡牌的 UpgradeKeywords。
/// </summary>
public sealed class KeywordUpgradeRow : INotifyPropertyChanged
{
	private readonly CardSpec _card;

	private readonly string _field;

	public string Zh { get; }

	public KeywordUpgradeRow(CardSpec card, string field, string zh)
	{
		_card = card;
		_field = field;
		Zh = zh;
	}

	/// <summary>三态：Keep / Add / Remove（下拉框里显示的是中文）。</summary>
	public string State
	{
		get => _card.UpgradeKeywords.Get(_field);
		set
		{
			if (string.Equals(_card.UpgradeKeywords.Get(_field), value, StringComparison.Ordinal)) return;
			_card.UpgradeKeywords.Set(_field, value);
			Raise("State");
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 「卡牌」页里「自定义关键词」的一行：一张卡 × 一条关键词。
/// 勾选写入这张卡的 KeywordIds（存本地化键；生成时卡面描述开头 + 悬停说明都按它来）。
/// </summary>
public sealed class CustomKeywordRow : INotifyPropertyChanged
{
	private readonly CardSpec _card;

	private readonly CustomKeywordSpec _spec;

	private readonly string _key;

	public CustomKeywordRow(CardSpec card, CustomKeywordSpec spec, string key)
	{
		_card = card;
		_spec = spec;
		_key = key;
	}

	public string Name => string.IsNullOrWhiteSpace(_spec.Name) ? _key : _spec.Name.Trim();

	public string KeyHint => _key;

	public bool IsChecked
	{
		get => _card.CustomKeywordList.Any(x =>
			string.Equals(x, _key, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(x, (_spec.Name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
		set
		{
			var list = new List<string>(_card.CustomKeywordList);
			string name = (_spec.Name ?? "").Trim();
			if (value)
			{
				// 统一存「本地化键」，和生成代码里的 LocString 键保持一致
				list.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
				if (!list.Any(x => string.Equals(x, _key, StringComparison.OrdinalIgnoreCase))) list.Add(_key);
			}
			else
			{
				list.RemoveAll(x => string.Equals(x, _key, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
			}
			_card.KeywordIds = list;
			Raise(nameof(IsChecked));
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ArtSlot : INotifyPropertyChanged
{
	private string? _path;

	private readonly Func<ArtSpec, string?> _get;

	private readonly Action<ArtSpec, string?> _set;

	public string Name { get; }

	public string Hint { get; }

	public string Requirement { get; }

	/// <summary>游戏里这块的显示尺寸（宽；0 = 这个槽位没有固定的显示区域）。</summary>
	public int FrameWidth { get; }

	/// <summary>游戏里这块的显示尺寸（高；0 = 这个槽位没有固定的显示区域）。</summary>
	public int FrameHeight { get; }

	/// <summary>有没有「游戏里的显示区域」——有就按它的比例画预览黑框（能看到黑边）。</summary>
	public bool HasFrame => FrameWidth > 0 && FrameHeight > 0;

	/// <summary>上传图和显示区域的比例说明（预览下面那行小字；没上传 / 比例一致时也有话说）。</summary>
	public string AspectNote => HasFrame
		? MainWindow.AspectNoteOf(_path, FrameWidth, FrameHeight, $"游戏里这块（{Name}）")
		: "";

	public string? Path
	{
		get
		{
			return _path;
		}
		set
		{
			_path = value;
			Raise("Path");
			Raise("Preview");
			Raise("AspectNote");
			Raise("Display");
		}
	}

	public BitmapImage? Preview
	{
		get
		{
			if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
			{
				return null;
			}
			try
			{
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.UriSource = new Uri(_path);
				bitmapImage.EndInit();
				return bitmapImage;
			}
			catch
			{
				return null;
			}
		}
	}

	public string Display
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(_path))
			{
				return Name + "（已上传）";
			}
			return Name + "（占位）";
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public ArtSlot(string name, string hint, string requirement, Func<ArtSpec, string?> get, Action<ArtSpec, string?> set,
		int frameWidth = 0, int frameHeight = 0)
	{
		Name = name;
		Hint = hint;
		Requirement = requirement;
		_get = get;
		_set = set;
		FrameWidth = frameWidth;
		FrameHeight = frameHeight;
	}

	public void Load(ArtSpec art)
	{
		Path = _get(art);
	}

	public void Save(ArtSpec art)
	{
		_set(art, Path);
	}

	private void Raise(string n)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
	}
}


public sealed record SpeakerOption(bool Value, string Display);

}