using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;
using System.Text.Json.Serialization;
using Sts2CharForge.Core.Effects;

namespace Sts2CharForge.Core.Profile;

/// <summary>带属性变更通知的配置对象基类：编辑后界面列表里的文字会自动刷新（无需重建数据源）。</summary>
public abstract class SpecBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        Raise("Display");        // 卡牌 / 药水列表用的显示文字
        Raise("DisplayPlain");   // 遗物 / 药水列表用的是 DisplayPlain（以前没通知它 → 改了列表不刷新，用户报过）
        return true;
    }

    protected void Raise(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}

/// <summary>一份完整的「自定义角色」配置。</summary>
public sealed class CharacterProfile
{
    /// <summary>
    /// 这份配置来自哪个存档文件（不含扩展名），由读写存档时自动填。
    /// **只用来决定「生成到哪个工程目录」**：以前用的是 ModId，于是两个存档只要 ModId 一样
    /// （比如复制一份存档、改个文件名），就会往同一个工程目录里写、互相覆盖彼此的生成文件 →
    /// 构建失败 / 存档互相影响（用户报过）。现在一个存档一个目录，互不干扰。
    /// 注意：dll / pck / json 的文件名仍然用 ModId（游戏按 ModId 认模组），只有目录名跟着存档走。
    /// </summary>
    [JsonIgnore]
    public string? SaveName { get; set; }

    /// <summary>额外资源量/状态（血条下方的第二个计数器）</summary>
    public ExtraResourceSpec ExtraResource { get; set; } = new();

    /// <summary>
    /// 召唤物列表（类似本体亡灵缚者的奥斯提，但完全不改本体、不需要 Harmony 补丁）。
    /// 可以同时配多只、同一场战斗里都在场（每只一个自己的 <c>MonsterModel</c> 子类 + 一套召唤命令）。
    /// </summary>
    public ObservableCollection<SummonSpec> Summons { get; set; } = new();

    /// <summary>
    /// [旧字段·只为读老存档] 老版只能配**一只**召唤伙伴，就存在这个单对象里。
    /// 打开老存档时由 <c>ProfileFactory.Normalize</c> 把它（勾了启用而且填了名字才搬）搬进
    /// <see cref="Summons"/> 并把这里置成 null；生成 / 界面一律只看 <see cref="Summons"/>。
    /// 为什么要保留这个属性：老存档（以及用户手写的 JSON）里还有 <c>"Summon": { ... }</c> 这一段，
    /// 直接删掉属性的话那段配置会被静默丢弃（用户的宠物名字 / 血量 / 图片全没了）。
    /// </summary>
    public SummonSpec? Summon { get; set; }

    /// <summary>和先古之民（达弗 / 妮欧 / 建筑师 …）的对话</summary>
    public ObservableCollection<AncientTalkSpec> Ancients { get; set; } = new();

    /// <summary>改写本体状态（改名 / 描述 / 图标 / 血条颜色 / 层数数字颜色）</summary>
    public ObservableCollection<VanillaPowerOverride> VanillaPowerOverrides { get; set; } = new();

    /// <summary>自定义状态（能力牌用的「你自己的状态」：触发时机 + 效果）</summary>
    public ObservableCollection<CustomPowerSpec> CustomPowers { get; set; } = new();

    public string ModId { get; set; } = "MyCharMod";
    public string ModDisplayName { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "v0.1.0";

    public string CharacterClass { get; set; } = "MyCharacter";
    public string DisplayName { get; set; } = "新角色";
    public string Description { get; set; } = "";
    public int StartingHp { get; set; } = 80;
    public int StartingGold { get; set; } = 99;
    /// <summary>Neutral / Feminine / Masculine</summary>
    public string Gender { get; set; } = "Masculine";
    /// <summary>死亡描述文本（本地化 eventDeathPrevention）</summary>
    public string DeathText { get; set; } = "";
    /// <summary>阵亡后回合结束台词（banter.dead.endTurnPing）</summary>
    public string DeadBanterText { get; set; } = "";
    /// <summary>与最终 Boss「建筑师」的结算对话（每行一句）</summary>
    public string ArchitectDialogue { get; set; } = "";
    public string? UnlockAfter { get; set; }
    public double AttackAnimDelay { get; set; } = 0.15;
    public double CastAnimDelay { get; set; } = 0.25;

    /// <summary>[旧字段·只为读老存档] 老版「初始卡组包含 5 张打击 + 5 张防御」那个勾选框。
    /// 界面上已经删掉了：现在这两张是卡牌列表里的两条**本体卡引用**（<see cref="CardSpec.IsVanillaCard"/>），
    /// 新建存档默认就有、可以改初始张数也能删。老存档里这个字段是 true 的话，
    /// 读进来时会自动补上那两条（初始卡组内容完全不变）并把它置回 false（见 <c>ProfileFactory.Normalize</c>）。</summary>
    public bool IncludeVanillaStrikeDefend { get; set; }
    /// <summary>[旧字段·只为读老存档] 老版「没有自定义初始遗物时，默认携带燃烧之血」那个勾选框。
    /// 界面上已经删掉了，生成时也完全不读它：初始遗物只按「遗物」页里勾了「起始遗物」的那些来，
    /// 一只都没有就是开局没有遗物（不再偷偷塞本体的燃烧之血）。</summary>
    public bool StartWithBurningBlood { get; set; }
    /// <summary>[已废弃·不生效] 早期的「卡池 / 遗物池 / 药水池用铁甲战士的内容补齐（占位）」开关。
    /// 这个开关在界面上已经删掉了，生成时也完全不读它：现在奖励池里只放你自己的内容
    /// （池子不够 3 张奖励可用的卡时会直接报错拦住生成，不再偷偷塞本体卡）。
    /// 保留这个属性只是为了老存档还能正常读进来。</summary>
    public bool FillPoolsWithIronclad { get; set; }
    /// <summary>[已废弃·不生效] 同上。</summary>
    public bool ForceIroncladFillerWhenSparse { get; set; }

    public ObservableCollection<CardSpec> Cards { get; set; } = new();
    public ObservableCollection<RelicSpec> Relics { get; set; } = new();
    public ObservableCollection<PotionSpec> Potions { get; set; } = new();
    /// <summary>
    /// 自定义关键词（见 <see cref="CustomKeywordSpec"/>）。
    /// 本体的 CardKeyword 是**封闭枚举**、模组无法扩展，所以这里不走关键字机制，
    /// 而是「本地化词条 + 卡牌悬停说明」，观感与本体关键词一致。
    /// </summary>
    public ObservableCollection<CustomKeywordSpec> CustomKeywords { get; set; } = new();

    /// <summary>
    /// 本体那 7 个关键词的**显示名 / 说明覆盖**（见 <see cref="VanillaKeywordRenameSpec"/>）。
    /// 和「自定义关键词」的区别：那个是**新增**关键词，这个是**改名**——
    /// 往我们的 localization/zhs/card_keywords.json 里写 EXHAUST.title = 你的名字，
    /// 本体 LocTable.MergeWith 会逐键盖掉本体那条，于是本体卡面上那个金色词、以及悬停提示都跟着变。
    /// 固定 7 条（<see cref="VanillaKeywordCatalog.All"/> 的顺序）。
    /// </summary>
    public ObservableCollection<VanillaKeywordRenameSpec> KeywordRenames { get; set; } = new();

    public ArtSpec Art { get; set; } = new();
    /// <summary>卡牌配色（整个角色生效：边框 / 牌堆底色 / 能量描边）</summary>
    public CardColorSpec Colors { get; set; } = new();
    public PathsSpec Paths { get; set; } = new();
}

/// <summary>一条「效果」，卡牌/遗物/药水共用。</summary>
public sealed class EffectSpec : SpecBase
{
    private string _kind = "Damage";
    private decimal _amount;
    private decimal _upgradeAmount;
    private string _targetSide = "Enemy";
    private int _repeatCount = 1;
    private int _times = 1;
    private bool _allowDuplicates = true;
    private bool _nextTurn;
    private string? _powerId;
    private string? _spawnCardId;
    private string _spawnTo = "Hand";
    private string _cardPick = "Random";
    private bool _amountIsX;
    private bool _timesIsX;
    private bool _repeatIsX;
    private bool _amountIsStack;
    private bool _timesIsStack_;
    private bool _repeatIsStack_;
    private ConditionSpec _condition = new();

    /// <summary>
    /// 条件选项：<b>只对「这一条效果」生效</b>（不是整张牌）。
    /// 不满足时：勾了「不满足时打不出去」→ 整张牌打不出去；否则这一条效果跳过、其它效果照常执行。
    /// </summary>
    public ConditionSpec Condition
    {
        get => _condition;
        set
        {
            if (_condition is not null) _condition.PropertyChanged -= OnConditionChanged;
            if (!Set(ref _condition, value ?? new ConditionSpec())) return;
            _condition.PropertyChanged += OnConditionChanged;
            Raise(nameof(Display));
        }
    }

    private void OnConditionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Raise(nameof(Display));

    /// <summary>Damage / Block / Draw / Energy / Heal / HpLoss / MaxHp / Gold / ApplyPower</summary>
    public string Kind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value))
            {
                Raise(nameof(Display));
                Raise(nameof(UsesSelectPile));
            }
        }
    }
    public decimal Amount { get => _amount; set => Set(ref _amount, value); }
    /// <summary>升级后增量（只有卡牌用得到，遗物/药水不能升级）</summary>
    public decimal UpgradeAmount { get => _upgradeAmount; set => Set(ref _upgradeAmount, value); }

    private bool _chanceEnabled;
    private decimal _chancePercent = 50m;

    /// <summary>
    /// 概率生效：勾上以后这一条效果只有这么多机会会真的执行（不满足时这条效果直接跳过，
    /// 同一张牌 / 遗物 / 状态里的其它效果照常执行）。
    /// 生成出来是 <c>if (base.Owner.RunState.Rng.Niche.NextDouble() * 100.0 &lt; 50m)</c>。
    /// </summary>
    public bool ChanceEnabled
    {
        get => _chanceEnabled;
        set { if (Set(ref _chanceEnabled, value)) Raise(nameof(Display)); }
    }

    /// <summary>生效概率（百分数，1~100）。</summary>
    public decimal ChancePercent
    {
        get => _chancePercent;
        set { if (Set(ref _chancePercent, value)) Raise(nameof(Display)); }
    }

    /// <summary>列表 / 描述里那句「（N% 概率）」（没勾就空）。</summary>
    [JsonIgnore]
    public string ChanceText => ChanceEnabled ? $"（{ChancePercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}% 概率）" : "";

    /// <summary>「召唤 / 伙伴攻击」这条效果指向的召唤物（类名；留空 = 还没选）。</summary>
    [JsonIgnore]
    public string PetSummonTag => (PetSummon ?? "").Trim();
    /// <summary>本条效果对谁生效：Self / Enemy / AllEnemies / RandomEnemies</summary>
    public string TargetSide { get => _targetSide; set => Set(ref _targetSide, value); }
    public int RepeatCount { get => _repeatCount; set => Set(ref _repeatCount, value); }
    /// <summary>生效次数：整条效果重复执行几遍（1~20，1 = 只执行一次）</summary>
    public int Times { get => _times; set => Set(ref _times, value); }
    public bool AllowDuplicates { get => _allowDuplicates; set => Set(ref _allowDuplicates, value); }
    public bool NextTurn { get => _nextTurn; set => Set(ref _nextTurn, value); }
    /// <summary>ApplyPower 用：本体 Power 类名（如 WeakPower）</summary>
    public string? PowerId
    {
        get => _powerId;
        set { if (Set(ref _powerId, value)) { Raise(nameof(Display)); Raise(nameof(IsSlowPower)); } }
    }

    /// <summary>
    /// GenerateCard 用：生成哪张卡（卡牌类名，如 Shiv，也可以是本模组自己的卡）。
    /// TransformCard 用：变化成哪张卡；留空 = 随机变化。
    /// </summary>
    public string? SpawnCardId { get => _spawnCardId; set => Set(ref _spawnCardId, value); }

    /// <summary>GenerateCard 用：生成的卡放到哪：Hand / Draw / Discard</summary>
    public string SpawnTo { get => _spawnTo; set => Set(ref _spawnTo, value); }

    /// <summary>ExhaustCard / TransformCard 用：选牌方式：Random（随机）/ Chosen（自己选）</summary>
    public string CardPick { get => _cardPick; set => Set(ref _cardPick, value); }

    // ===== X 费用卡牌（本体 Whirlwind / 天际钻头 / 挽歌 那种「消耗全部能量」）=====
    // 这三个开关只有卡牌用得到（遗物/药水没有 X 这个概念）。
    /// <summary>数值 = X（X = 打出时消耗的能量/资源量）</summary>
    public bool AmountIsX { get => _amountIsX; set => Set(ref _amountIsX, value); }
    /// <summary>生效次数 = X（整条效果重复 X 遍，本体挽歌那种）</summary>
    public bool TimesIsX { get => _timesIsX; set => Set(ref _timesIsX, value); }
    /// <summary>命中次数 / 对群数 = X（本体旋风斩 / 天际钻头那种）</summary>
    public bool RepeatIsX { get => _repeatIsX; set => Set(ref _repeatIsX, value); }

    /// <summary>
    /// 「自定义状态（能力）」的触发器里用：数值 = <b>本状态的层数</b>（本体恶魔形态「每回合获得等于层数的力量」那种）。
    /// 卡牌/遗物/药水用不到这个开关（它们的数值就是填的数字）。</summary>
    public bool AmountIsStack { get => _amountIsStack; set => Set(ref _amountIsStack, value); }

    /// <summary>
    /// 「自定义状态」的触发器里用：<b>生效次数 = 本状态的层数</b>（整条效果按层数重复执行）。
    /// 例：5 层 + 「造成 2 点伤害」→ 每次触发打 5 次、共 10 点（本体旋风斩那种多段）。
    /// 勾了之后「生效次数」那一栏就不用了。</summary>
    public bool TimesIsStack { get => _timesIsStack_; set => Set(ref _timesIsStack_, value); }

    /// <summary>
    /// 「自定义状态」的触发器里用：<b>命中次数 / 对群数 = 本状态的层数</b>
    /// （只对「随机敌人」这类按次数挑目标的效果有意义）。</summary>
    public bool RepeatIsStack { get => _repeatIsStack_; set => Set(ref _repeatIsStack_, value); }

    private decimal _slowPercent;

    /// <summary>
    /// 本体「缓慢」专用（只在「施加增益/减益」选的是 <c>SlowPower</c> 时出现）：
    /// 施加完之后<b>直接把它的内部数值设成 N%</b>（0 = 不设置，按层数正常施加）。
    ///
    /// 为什么需要这个开关：本体「缓慢」显示/生效的数字都不是「层数」，而是内部 DynamicVar
    /// <c>SlowAmount</c>×10 —— 它由「本回合每打出一张牌 +1」驱动，战斗里每次敌人回合开始清零，
    /// 本体自己施加时也只施加 1 层（BygoneEffigy：PowerCmd.Apply&lt;SlowPower&gt;(…, 1m, …)）。
    /// 所以「施加 30 层缓慢」在游戏里既看不到 30、也吃不到那 30 层的加成。
    /// 填 30 就是「敌人当场受到伤害 +30%」，持续到它下一次回合开始（本体机制如此）。
    /// </summary>
    public decimal SlowPercent
    {
        get => _slowPercent;
        set { if (Set(ref _slowPercent, value)) { Raise(nameof(Display)); Raise(nameof(SlowPercentText)); } }
    }

    /// <summary>界面/列表里那句「直接把「缓慢」设成 N%」（没填就空）。</summary>
    [JsonIgnore]
    public string SlowPercentText => SlowPercent > 0 ? $"（直接把「缓慢」设成 {SlowPercentEffective}%）" : "";

    private string _selectPile = "Hand";

    /// <summary>
    /// 「消耗卡牌 / 变化卡牌」从哪一摞牌里选：Hand（手牌）/ Draw（抽牌堆）/ Discard（弃牌堆）。
    ///
    /// 以前只有手牌这一种（本体 <c>CardSelectCmd.FromHand</c>）；抽牌堆 / 弃牌堆走本体的
    /// <c>CardSelectCmd.FromCombatPile(context, PileType.X.GetPile(player), player, prefs)</c> ——
    /// 本体自己选抽牌堆的是「充能 Charge / 净化 Cleanse / 降灵 Seance」，
    /// 选弃牌堆的是「全息影像 Hologram / 头槌 Headbutt / 挖掘 Dredge / 宇宙冷漠 CosmicIndifference」。
    /// </summary>
    public string SelectPile
    {
        get => _selectPile;
        set { if (Set(ref _selectPile, value)) { Raise(nameof(SelectPileZh)); Raise(nameof(Display)); } }
    }

    /// <summary>「从哪里选牌」的中文（列表 / 描述里用）。</summary>
    [JsonIgnore]
    public string SelectPileZh => SelectPile switch
    {
        "Draw" => "抽牌堆",
        "Discard" => "弃牌堆",
        _ => "手牌",
    };

    /// <summary>这条效果要不要显示「从哪里选牌」（只有消耗 / 变化卡牌用得到）。</summary>
    [JsonIgnore]
    public bool UsesSelectPile => Kind is "ExhaustCard" or "TransformCard";

    /// <summary>本体「缓慢」是 10% 一档（内部 SlowAmount 是整数）：四舍五入到最近的 10%，返回实际生效的百分比。</summary>
    [JsonIgnore]
    public int SlowPercentEffective =>
        SlowPercent <= 0 ? 0 : (int)Math.Round(SlowPercent / 10m, MidpointRounding.AwayFromZero) * 10;

    /// <summary>界面上「直接把「缓慢」设成 N%」那一行只在选了「缓慢」时显示。</summary>
    [JsonIgnore]
    public bool IsSlowPower => string.Equals(PowerId?.Trim(), "SlowPower", StringComparison.Ordinal);

    /// <summary>这条效果是否用到 X（生成代码时要先取 X 值）</summary>
    [JsonIgnore]
    public bool UsesX => AmountIsX || TimesIsX || RepeatIsX;

    /// <summary>
    /// 这条效果是「召唤伙伴 / 伙伴攻击」（做的是宠物，不是自己 / 敌人）。
    /// 界面列表里不要把 TargetSide 显示成「→ 自己」误导人；生成代码时「宠物在不在场」的守卫也按它判断。
    /// </summary>
    [JsonIgnore]
    public bool PetAction => Kind is "SummonPet" or "PetAttack";

    private string? _petSummon;

    /// <summary>
    /// 「召唤伙伴 / 伙伴攻击」指的是**哪一只**召唤物：存它的**稳定标识**（<c>SummonSpec.ClassName</c>，
    /// 也就是宠物 <c>MonsterModel</c> 子类的类名），界面上的下拉显示的是它的中文名。
    ///
    /// 为什么用类名而不是中文名：中文名可以随时改（改完老存档不该失效），而类名决定模型 ID，
    /// 本来就不能随便改（改了等于换了一只）。留空 = 老存档迁移前的写法，生成时自动挑第一只启用的召唤物，
    /// 校验器会提醒一句。
    /// </summary>
    public string? PetSummon
    {
        get => _petSummon;
        set { if (Set(ref _petSummon, value)) Raise(nameof(Display)); }
    }

    /// <summary>GenerateCard 用：生成的卡放到哪 —— 对应的 PileType 名字。</summary>
    [JsonIgnore]
    public string SpawnToPile => SpawnTo switch
    {
        "Draw" => "Draw",
        "Discard" => "Discard",
        _ => "Hand",
    };

    /// <summary>选牌方式的中文（列表文字用）。</summary>
    [JsonIgnore]
    public string CardPickZh => CardPick == "Chosen" ? "自己选" : "随机";

    /// <summary>
    /// 遗物 / 药水列表用的显示文本：它们不能升级，所以不显示「（升级 +N）」这种括号
    /// （老存档里可能残留升级增量，这里也不显示出来）。
    /// </summary>
    [JsonIgnore]
    public string DisplayPlain => UpgradeAmount == 0 ? Display : Display.Replace($"（升级 {(UpgradeAmount > 0 ? "+" : "")}{UpgradeAmount}）", "");

    [JsonIgnore]
    public string Display
    {
        get
        {
            string when = NextTurn ? "（下回合）" : "";
            // 生成 / 消耗 / 变化卡牌：把「哪张卡、放哪、怎么选」显示出来，方便一眼看出有没有填漏
            string extra = Kind switch
            {
                "GenerateCard" => $" ｜ 生成 {(SpawnCardId is { Length: > 0 } sc ? sc : "（未填→Shiv）")} → {SpawnToPile}",
                "TransformCard" => $" ｜ 变为 {(SpawnCardId is { Length: > 0 } tc ? tc : "随机卡")} ｜ {CardPickZh}",
                "ExhaustCard" => $" ｜ {CardPickZh}",
                "AddCardGlobal" => $" ｜ 加进牌组：{(SpawnCardId is { Length: > 0 } ac ? ac : "（未填→Shiv）")}",
                "TransformCardGlobal" => $" ｜ 牌组里的牌变为 {(SpawnCardId is { Length: > 0 } tgc ? tgc : "随机卡")} ｜ {CardPickZh}",
                "RemoveCardGlobal" => $" ｜ 从牌组删牌 ｜ {CardPickZh}",
                _ => "",
            };
            string side = TargetSide switch
            {
                "Enemy" => "单体敌人",
                "AllEnemies" => "全体敌人",
                "RandomEnemies" => RepeatIsX ? "随机 X 个敌人" : $"随机 {RepeatCount} 个敌人",
                _ => "自己",
            };
            string kind = Kind switch
            {
                "Damage" => "伤害",
                "Block" => "格挡",
                "Draw" => "抽牌",
                "Energy" => "能量",
                "Heal" => "生命",
                "HpLoss" => "失去生命",
                "MaxHp" => "最大生命",
                "Gold" => "金币",
                "ApplyPower" => "增益/减益 " + (PowerId ?? "?"),
                "AddCardGlobal" => "获得卡牌（全局）",
                "TransformCardGlobal" => "变化卡牌（全局）",
                "RemoveCardGlobal" => "删除卡牌（全局）",
                "CardReward" => "获得卡牌奖励",
                // 召唤伙伴 / 伙伴攻击：作用对象是宠物，不是「自己 / 敌人」，别显示那个「→ 自己」。
                // 这里写的是选中那只召唤物的**类名**（Profile 层拿不到中文名 —— 那要读召唤物列表，
                // 由 LocalizationGen / 界面负责翻译成中文名）。
                "SummonPet" => $"召唤{(PetSummonTag.Length > 0 ? PetSummonTag : "伙伴")}"
                    + (Amount > 0 ? $"{Amount:0.##} 点生命" : "（用配置的血量）"),
                "PetAttack" => $"{(PetSummonTag.Length > 0 ? PetSummonTag : "伙伴")}攻击 {Amount:0.##}",
                _ => Kind,
            };
            return $"{when}{kind} {(AmountIsX ? "X" : Amount.ToString("0.##"))}{(UpgradeAmount != 0 && !AmountIsX ? $"（升级 {(UpgradeAmount > 0 ? "+" : "")}{UpgradeAmount}）" : "")}"
                 + $"{(TimesIsX ? " ×X 次" : Times > 1 ? $" ×{Times} 次" : "")}"
                 + $"{(RepeatIsX ? "（命中 X 次）" : "")}{ChanceText}{SlowPercentText}"
                 // 召唤 / 伙伴攻击打的是宠物，没有「作用对象」这一说 —— 加了这个尾巴会让人以为「→ 自己」是给宠物加血
                 + (PetAction ? "" : $" → {side}") + extra
                 + (Condition.IsNone ? "" : $"  ｜ 条件：{Condition.DisplayShort}");
        }
    }
}

public sealed class CardSpec : SpecBase
{
    private string _name = "新卡";
    private string _className = "";
    private string _cardType = "Attack";
    private string _rarity = "Common";
    private int _cost = 1;
    private bool _inStartingDeck;
    private int _startingCopies = 1;
    private bool _inCardPool = true;
    private bool _exhausts;
    private bool _ethereal;
    private bool _innate;
    private bool _retain;
    private bool _unplayable;
    private bool _sly;
    private bool _costIsX;
    private bool _starCostIsX;
    private bool _xPlusOnUpgrade;
    private int? _upgradeCost;

    public string Name { get => _name; set => Set(ref _name, value); }
    /// <summary>英文类名，留空自动生成 &lt;角色类名&gt;Card&lt;序号&gt;</summary>
    public string ClassName { get => _className; set => Set(ref _className, value); }
    /// <summary>Attack / Skill / Power</summary>
    public string CardType { get => _cardType; set => Set(ref _cardType, value); }
    /// <summary>Basic / Common / Uncommon / Rare</summary>
    public string Rarity { get => _rarity; set => Set(ref _rarity, value); }
    public int Cost { get => _cost; set => Set(ref _cost, value); }

    // ===== X 费用（本体储君「天际钻头」、亡灵契约师「挽歌」那种）=====
    /// <summary>费用为 X：打出时消耗全部能量，X = 消耗掉的能量（本体 Whirlwind / 天际钻头）。</summary>
    public bool CostIsX { get => _costIsX; set => Set(ref _costIsX, value); }
    /// <summary>额外资源量费用为 X：打出时消耗全部额外资源量，X = 消耗掉的资源量（本体 Stardust）。</summary>
    public bool StarCostIsX { get => _starCostIsX; set => Set(ref _starCostIsX, value); }
    /// <summary>升级后 X +1（本体 Cascade 的做法）。</summary>
    public bool XPlusOnUpgrade { get => _xPlusOnUpgrade; set => Set(ref _xPlusOnUpgrade, value); }

    /// <summary>
    /// 升级后费用（整张牌，不是单条效果）：填了就生成 <c>OnUpgrade()</c> 里的
    /// <c>base.EnergyCost.UpgradeBy(差值)</c>（本体改费用就是这么做的）。
    /// 留空（null）= 升级不改费用。例：1 费卡想升级成 0 费 → 这里填 0。
    /// </summary>
    public int? UpgradeCost { get => _upgradeCost; set => Set(ref _upgradeCost, value); }

    private KeywordUpgradeSpec _upgradeKeywords = new();

    /// <summary>
    /// 升级后的关键字（整张牌的设置，放在「升级后费用」下面）：
    /// 每个关键字三态 —— 不变 / 升级后获得 / 升级后失去。
    /// 本体就是这么做的（例：残影 Afterimage 升级后获得「固有」、幻影 Apparition 升级后失去「虚无」）。
    /// </summary>
    public KeywordUpgradeSpec UpgradeKeywords
    {
        get => _upgradeKeywords;
        set
        {
            if (_upgradeKeywords is not null) _upgradeKeywords.PropertyChanged -= OnUpgradeKeywordsChanged;
            _upgradeKeywords = value ?? new KeywordUpgradeSpec();
            _upgradeKeywords.PropertyChanged += OnUpgradeKeywordsChanged;
            Raise(nameof(Display));
        }
    }

    public CardSpec() => _upgradeKeywords.PropertyChanged += OnUpgradeKeywordsChanged;

    private void OnUpgradeKeywordsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Raise(nameof(Display));
    public bool InStartingDeck { get => _inStartingDeck; set => Set(ref _inStartingDeck, value); }
    public int StartingCopies { get => _startingCopies; set => Set(ref _startingCopies, value); }
    public bool InCardPool { get => _inCardPool; set => Set(ref _inCardPool, value); }

    private bool _isVanilla;

    /// <summary>
    /// 本体卡（**只引用本体的卡，不生成自己的类**）：初始卡组里的「打击 ×5 / 防御 ×5」就是这种。
    ///
    /// 为什么不能生成自己的类：本体的 <c>ModelDb</c> 只用**类名**（忽略命名空间）算 ID，
    /// 自己再定义一个 <c>StrikeIronclad</c> 会在注册时直接抛
    /// <c>DuplicateModelException</c>（"conflict in mod content names"），模组加载当场失败。
    /// 所以这种条目只有 <see cref="ClassName"/>（= 本体英文类名）和初始张数有意义，
    /// 数值 / 效果 / 关键字都由本体决定，界面上是只读的（除了「初始张数 / 放进初始卡组」）。
    /// </summary>
    public bool IsVanillaCard
    {
        get => _isVanilla;
        set { if (Set(ref _isVanilla, value)) { Raise(nameof(Display)); Raise(nameof(ValueEditable)); } }
    }

    /// <summary>界面用：这张牌的数值 / 效果能不能改（本体卡不能，见 <see cref="IsVanillaCard"/>）。</summary>
    [JsonIgnore]
    public bool ValueEditable => !IsVanillaCard;

    private List<string> _tags = new();

    /// <summary>
    /// 本体那套 <c>CardTag</c>（行为无关的元数据，给别的模型查牌用）：
    /// Strike / Defend / Minion / OstyAttack / Shiv。
    ///
    /// 为什么要有它：本体那些「升级你的初始打击 / 防御」「只对打击牌生效」的遗物、事件、卡牌，
    /// 全是按 <c>CardTag</c> 查牌的（妮欧的护符 NeowsTalisman、叶敷剂 LeafyPoultice、大胶囊 LargeCapsule、
    /// 幽灵种子 GhostSeed、完美打击 PerfectedStrike…），有些还会再加上「稀有度 = Basic」这一条。
    /// 所以自己的初始打击 / 防御必须标上 Strike / Defend，否则那些东西会找不到你的牌。
    /// </summary>
    public List<string> Tags
    {
        get => _tags;
        set { if (Set(ref _tags, value ?? new List<string>())) Raise(nameof(Display)); }
    }

    /// <summary>只保留合法值（读老存档 / 手写 JSON 时兜底）。</summary>
    [JsonIgnore]
    public List<string> TagList => Tags.Where(EffectCatalog.CardTags.Contains).Distinct().ToList();

    /// <summary>
    /// 是不是「初始卡组的打击 / 防御」（英文类名固定成 Strike / Defend 的那两张）。
    /// 它们固定排在卡牌列表最前面，而且**不占自动编号的号** —— 否则它们插到最前面会让
    /// 所有没填类名的卡集体改名（类名一变，卡面素材文件名、存档引用全都错位）。
    /// </summary>
    [JsonIgnore]
    public bool IsStartingBasic =>
        string.Equals(ClassName?.Trim(), "Strike", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ClassName?.Trim(), "Defend", StringComparison.OrdinalIgnoreCase);

    // 界面上就是这五个勾选框（本体 CardTag 的全部取值）
    [JsonIgnore] public bool TagStrike { get => TagList.Contains("Strike"); set => SetTag("Strike", value); }
    [JsonIgnore] public bool TagDefend { get => TagList.Contains("Defend"); set => SetTag("Defend", value); }
    [JsonIgnore] public bool TagMinion { get => TagList.Contains("Minion"); set => SetTag("Minion", value); }
    [JsonIgnore] public bool TagOstyAttack { get => TagList.Contains("OstyAttack"); set => SetTag("OstyAttack", value); }
    [JsonIgnore] public bool TagShiv { get => TagList.Contains("Shiv"); set => SetTag("Shiv", value); }

    private void SetTag(string tag, bool on)
    {
        var list = new List<string>(TagList);
        if (on) { if (!list.Contains(tag)) list.Add(tag); }
        else list.Remove(tag);
        Tags = list;
        Raise(nameof(TagStrike)); Raise(nameof(TagDefend)); Raise(nameof(TagMinion));
        Raise(nameof(TagOstyAttack)); Raise(nameof(TagShiv));
    }

    // ===== 自定义关键词（生成器自己的概念，不是本体的 CardKeyword）=====
    private List<string> _keywordIds = new();

    /// <summary>
    /// 这张牌引用了哪些自定义关键词（存 <see cref="CustomKeywordSpec.Key"/>，大小写不敏感）。
    /// 生成时：描述开头会拼上这些关键词的 [gold]名称[/gold]（和本体关键词一样的观感），
    /// 并给这张牌生成对应的悬停说明。
    /// </summary>
    public List<string> KeywordIds
    {
        get => _keywordIds;
        set { if (Set(ref _keywordIds, value ?? new List<string>())) Raise(nameof(CustomKeywordList)); }
    }

    /// <summary>去掉空值/重复后的引用列表（读老存档、手写 JSON 时兜底）。</summary>
    [JsonIgnore]
    public List<string> CustomKeywordList =>
        KeywordIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();

    // ===== 卡牌关键字（本体 CardKeyword，会在卡面上显示为关键字标签）=====
    /// <summary>消耗 Exhaust：打出后进消耗堆，本场战斗不再回来</summary>
    public bool Exhausts { get => _exhausts; set => Set(ref _exhausts, value); }
    /// <summary>虚无 Ethereal：回合结束时若还在手牌则被消耗</summary>
    public bool Ethereal { get => _ethereal; set => Set(ref _ethereal, value); }
    /// <summary>固有 Innate：战斗开始时必定在手牌</summary>
    public bool Innate { get => _innate; set => Set(ref _innate, value); }
    /// <summary>保留 Retain：回合结束时不会被弃掉</summary>
    public bool Retain { get => _retain; set => Set(ref _retain, value); }
    /// <summary>不能被打出 Unplayable</summary>
    public bool Unplayable { get => _unplayable; set => Set(ref _unplayable, value); }
    /// <summary>奇巧 Sly：回合结束前被弃掉则免费打出</summary>
    public bool Sly { get => _sly; set => Set(ref _sly, value); }

    /// <summary>选了哪些关键字（生成 CanonicalKeywords 用）。</summary>
    [JsonIgnore]
    public IReadOnlyList<string> KeywordList
    {
        get
        {
            var list = new List<string>();
            if (Exhausts) list.Add("Exhaust");
            if (Ethereal) list.Add("Ethereal");
            if (Innate) list.Add("Innate");
            if (Retain) list.Add("Retain");
            if (Unplayable) list.Add("Unplayable");
            if (Sly) list.Add("Sly");
            return list;
        }
    }

    public ObservableCollection<EffectSpec> Effects { get; set; } = new();

    /// <summary>
    /// 【旧版字段，只为读老存档兼容】以前「条件」是整张牌的；
    /// 现在条件挂在每条效果上（见 <see cref="EffectSpec.Condition"/>），打开老存档时会被自动搬到第一条效果上、这里清空。
    /// </summary>
    public ConditionSpec Condition { get; set; } = new();

    [JsonIgnore]
    public string Display => IsVanillaCard
        // 本体卡：只有类名和初始张数是有意义的，别把费用/稀有度那些默认值显示出来误导人
        ? $"【本体卡】{Name}（{ClassName}）  ｜ 初始 {Math.Max(1, StartingCopies)} 张 ｜ 数值同本体，不可改"
        : $"{Name}  ｜ {CardType} / {Rarity} / {(CostIsX ? "X" : Cost.ToString())} 费{(StarCostIsX ? " + 资源量X" : "")}{(InStartingDeck ? " ｜ 初始牌" : "")}{(InCardPool ? "" : " ｜ 不入池")}"
          + (KeywordList.Count > 0 ? " ｜ " + string.Join("·", KeywordList) : "")
          + (UpgradeKeywords.Any ? " ｜ " + UpgradeKeywords.Display : "")
          + (Condition.IsNone ? "" : "  ｜ 条件：" + Condition.DisplayShort);
}

/// <summary>
/// 「升级后的关键字」：整张牌的设置，每个关键字三态 —— 不变 / 升级后获得 / 升级后失去。
/// 生成 <c>OnUpgrade()</c> 里的 <c>AddKeyword / RemoveKeyword</c>（本体「残影 / 幻影 / 寒冰」就是这么写的）。
/// </summary>
public sealed class KeywordUpgradeSpec : SpecBase
{
    /// <summary>三态取值：Keep（不变）/ Add（升级后获得）/ Remove（升级后失去）</summary>
    public const string Keep = "Keep";
    public const string Add = "Add";
    public const string Remove = "Remove";

    /// <summary>界面上可选的关键字（顺序固定，生成代码时也按这个顺序写）。</summary>
    public static readonly (string Field, string Keyword, string Zh)[] All =
    {
        ("Exhaust", "Exhaust", "消耗"),
        ("Ethereal", "Ethereal", "虚无"),
        ("Innate", "Innate", "固有"),
        ("Retain", "Retain", "保留"),
        ("Unplayable", "Unplayable", "不能打出"),
        ("Sly", "Sly", "奇巧"),
    };

    private string _exhaust = Keep, _ethereal = Keep, _innate = Keep, _retain = Keep, _unplayable = Keep, _sly = Keep;

    public string Exhaust { get => _exhaust; set => SetState(ref _exhaust, value); }
    public string Ethereal { get => _ethereal; set => SetState(ref _ethereal, value); }
    public string Innate { get => _innate; set => SetState(ref _innate, value); }
    public string Retain { get => _retain; set => SetState(ref _retain, value); }
    public string Unplayable { get => _unplayable; set => SetState(ref _unplayable, value); }
    public string Sly { get => _sly; set => SetState(ref _sly, value); }

    private void SetState(ref string field, string? value)
    {
        string v = Normalize(value);
        if (Set(ref field, v)) { Raise(nameof(Any)); Raise(nameof(Display)); }
    }

    private static string Normalize(string? value) => value switch
    {
        Add => Add,
        Remove => Remove,
        _ => Keep,
    };

    public string Get(string field) => field switch
    {
        "Exhaust" => Exhaust,
        "Ethereal" => Ethereal,
        "Innate" => Innate,
        "Retain" => Retain,
        "Unplayable" => Unplayable,
        "Sly" => Sly,
        _ => Keep,
    };

    /// <summary>按字段名写（界面上的每一行关键字用它双向绑定）。</summary>
    public void Set(string field, string? value)
    {
        switch (field)
        {
            case "Exhaust": Exhaust = value; break;
            case "Ethereal": Ethereal = value; break;
            case "Innate": Innate = value; break;
            case "Retain": Retain = value; break;
            case "Unplayable": Unplayable = value; break;
            case "Sly": Sly = value; break;
        }
    }

    /// <summary>关键字的中文名（生成代码时写在注释里）。</summary>
    public static string Zh(string keyword)
    {
        foreach (var k in All)
            if (string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase)) return k.Zh;
        return keyword;
    }

    /// <summary>升级后要「获得」的关键字（生成 AddKeyword 用）。</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Added => All.Where(k => Get(k.Field) == Add).Select(k => k.Keyword).ToList();

    /// <summary>升级后要「失去」的关键字（生成 RemoveKeyword 用）。</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Removed => All.Where(k => Get(k.Field) == Remove).Select(k => k.Keyword).ToList();

    [JsonIgnore]
    public bool Any => Added.Count > 0 || Removed.Count > 0;

    /// <summary>列表里显示的短说明，例如「升级后：获得 固有、失去 消耗」。</summary>
    [JsonIgnore]
    public string Display
    {
        get
        {
            var parts = new List<string>();
            var add = All.Where(k => Get(k.Field) == Add).Select(k => k.Zh).ToList();
            var del = All.Where(k => Get(k.Field) == Remove).Select(k => k.Zh).ToList();
            if (add.Count > 0) parts.Add("获得 " + string.Join("、", add));
            if (del.Count > 0) parts.Add("失去 " + string.Join("、", del));
            return parts.Count == 0 ? "" : "升级后：" + string.Join("；", parts);
        }
    }
}

public sealed class RelicSpec : SpecBase
{
    private string _name = "新遗物";
    private string _className = "";
    private string _rarity = "Common";
    private bool _isStartingRelic;
    private string _trigger = "CombatStart";
    private bool _inRelicPool = true;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string ClassName { get => _className; set => Set(ref _className, value); }
    /// <summary>Starter / Common / Uncommon / Rare / Shop</summary>
    public string Rarity { get => _rarity; set => Set(ref _rarity, value); }
    public bool IsStartingRelic { get => _isStartingRelic; set => Set(ref _isStartingRelic, value); }
    /// <summary>CombatStart / PlayerTurnStart / PlayerTurnEnd / CombatVictory / DamageReceived / GoldGained</summary>
    public string Trigger { get => _trigger; set => Set(ref _trigger, value); }
    public bool InRelicPool { get => _inRelicPool; set => Set(ref _inRelicPool, value); }
    public ObservableCollection<EffectSpec> Effects { get; set; } = new();

    /// <summary>条件选项：整个遗物的效果只有在条件满足时才触发。</summary>
    public ConditionSpec Condition { get; set; } = new();

    /// <summary>遗物图标（用户上传的图片路径；留空 = 用本体遗物图标占位）。建议 256×256 PNG</summary>
    public string? Icon { get; set; }
    /// <summary>遗物未发现时的描边图标（留空 = 复用图标本体）</summary>
    public string? IconOutline { get; set; }

    [JsonIgnore]
    public string Display =>
        $"{Name}  ｜ {Rarity} ｜ {Trigger}{(IsStartingRelic ? " ｜ 初始遗物" : "")}{(InRelicPool ? "" : " ｜ 不入池")}"
        + (Condition.IsNone ? "" : "  ｜ 条件：" + Condition.DisplayShort);
}

/// <summary>
/// 和某位先古之民的对话。
///
/// 本体 AncientEventModel 的对话是按「角色 ID」查字典的（CharacterDialogues["IRONCLAD"] = …），
/// 自定义角色不在那个字典里 → 本工具给模组加一个补丁，运行时把我们的角色塞进去，
/// 于是 <c>&lt;先古之民&gt;.talk.&lt;我们的角色&gt;.&lt;X&gt;-&lt;Y&gt;[r].ancient|char</c> 就能生效，
/// 而且**一段对话写几句由我们自己定**（不像本体给「任意角色」那组每段只有 1 句）。
/// </summary>
/// <summary>
/// 「把这位先古之民原本会给的某个遗物，换成你要的遗物」一条：
/// 左边是这个先古之民候选表里的**原本的遗物**（界面上会罗列出来让你挑），右边是要换成的遗物
/// （本体的其它遗物，或你自己在「遗物」页做的）。
/// 生成时的做法：在他生成完 3 个选项之后，把「原本的遗物」那个选项换成你要的；
/// **那一次选项里没抽到它就不替换**（不会去顶掉别的选项 —— 免得挤掉本来该出现的遗物）。
/// </summary>
public sealed class AncientRelicReplaceSpec : SpecBase
{
    private string _fromRelicId = "";
    private string _relicId = "";
    private int _slot;

    /// <summary>要替换掉的「原本的遗物」类名（这位先古之民候选表里的本体遗物，比如 Astrolabe）</summary>
    public string FromRelicId
    {
        get => _fromRelicId;
        set { if (Set(ref _fromRelicId, value)) RaiseAll(); }
    }

    /// <summary>换成哪个遗物（遗物类名，比如你自己的 SevenCharm）</summary>
    public string RelicId
    {
        get => _relicId;
        set { if (Set(ref _relicId, value)) RaiseAll(); }
    }

    /// <summary>【旧版字段】以前按「第几个选项」替换；老存档读回来时用它兜底，界面上不再出现。</summary>
    public int Slot
    {
        get => _slot;
        set { if (Set(ref _slot, value)) RaiseAll(); }
    }

    private void RaiseAll()
    {
        Raise(nameof(Display));
        Raise(nameof(FromDisplay));
        Raise(nameof(ToDisplay));
        Raise(nameof(StateText));
        Raise(nameof(StateOk));
    }

    private static string Name(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        string name = AncientCatalog.VanillaRelicName(id) ?? "";
        return name.Length > 0 && name != id ? $"{name}（{id}）" : id!;
    }

    /// <summary>列表里第一行：要替换掉的原本的遗物。</summary>
    [JsonIgnore]
    public string FromDisplay => string.IsNullOrWhiteSpace(FromRelicId)
        ? (Slot >= 1 ? $"原本：第 {Slot} 个选项（旧配置）" : "原本：（还没选要替换掉哪个遗物）")
        : "原本：" + Name(FromRelicId);

    /// <summary>列表里第二行：换成什么。</summary>
    [JsonIgnore]
    public string ToDisplay => string.IsNullOrWhiteSpace(RelicId)
        ? "换成：（还没选要换成什么遗物）"
        : "换成：" + Name(RelicId);

    /// <summary>这一条现在到底会不会起作用（列表里用颜色标出来）。</summary>
    [JsonIgnore]
    public bool StateOk => !string.IsNullOrWhiteSpace(RelicId)
                           && !string.Equals(FromRelicId, RelicId, StringComparison.Ordinal);

    /// <summary>状态说明：配全了 / 还差什么。</summary>
    [JsonIgnore]
    public string StateText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RelicId)) return "⚠ 还没选要换成什么遗物 —— 这样生成出来不会有任何变化";
            if (string.Equals(FromRelicId, RelicId, StringComparison.Ordinal)) return "⚠ 和原本的遗物是同一个 —— 等于没改";
            if (string.IsNullOrWhiteSpace(FromRelicId)) return "⚠ 还没选「原本的遗物」—— 不知道要换掉哪一个，这条不会生效";
            return "✅ 会替换：这次选项里正好有它就直接换掉它；没抽到它就不动（不会顶掉别的选项）";
        }
    }

    [JsonIgnore]
    public string Display
    {
        get
        {
            string from = string.IsNullOrWhiteSpace(FromRelicId)
                ? (Slot >= 1 ? $"第 {Slot} 个选项（旧配置）" : "（还没选原本的遗物）")
                : Name(FromRelicId);
            if (string.IsNullOrWhiteSpace(RelicId)) return $"{from} → （还没选要换成什么）";
            return $"{from} → {Name(RelicId)}";
        }
    }
}

/// <summary>一位先古之民的对话配置（我们自己写的）+ 他的遗物选项替换。</summary>
public sealed class AncientTalkSpec : SpecBase
{
    private string _ancientId = "";

    /// <summary>先古之民的 ID（DARV / THE_ARCHITECT / …）</summary>
    public string AncientId { get => _ancientId; set => Set(ref _ancientId, value); }

    /// <summary>和他说的话：一段一段写</summary>
    public ObservableCollection<AncientDialogueSpec> Dialogues { get; set; } = new();

    /// <summary>他给的 3 个遗物选项里，哪几个位置要换成指定的遗物（本体的遗物 + 你自己做的遗物都行）。</summary>
    public ObservableCollection<AncientRelicReplaceSpec> RelicReplacements { get; set; } = new();

    [JsonIgnore]
    public bool HasAnyText => Dialogues.Any(d => d.Lines.Any(l => !string.IsNullOrWhiteSpace(l.Text)));

    [JsonIgnore]
    public bool HasAnyRelicReplacement => RelicReplacements.Any(r => !string.IsNullOrWhiteSpace(r.RelicId));
}

/// <summary>一段对话（对应 <c>&lt;X&gt;</c> 这个下标；本体一次只挑一段来放）。</summary>
public sealed class AncientDialogueSpec : SpecBase
{
    private int _times = 1;
    private bool _repeating;

    /// <summary>
    /// 第几次说这段（**1 起算**，界面上填几就显示几）：
    /// 1 = 第 1 次，2 = 第 2 次…；-1 = 每次都可能说。
    /// 本体内部用的是 0 起算的 VisitIndex，生成代码时由 <see cref="NativeVisitIndex"/> 换算，界面上不出现 0。
    /// </summary>
    public int Times { get => _times; set { if (Set(ref _times, value)) { Raise(nameof(Display)); Raise(nameof(NativeVisitIndex)); } } }

    /// <summary>说完之后加入「重复池」（以后每次都会随机再放这段，本体的 r 后缀）</summary>
    public bool Repeating { get => _repeating; set { if (Set(ref _repeating, value)) Raise(nameof(Display)); } }

    /// <summary>本体要的 0 起算 VisitIndex（-1 = 任意次 → 生成时写 null）。</summary>
    [JsonIgnore]
    public int NativeVisitIndex => Times < 0 ? -1 : Times - 1;

    public ObservableCollection<AncientLineSpec> Lines { get; set; } = new();

    /// <summary>界面用：「到访」/「通关」（建筑师走通关次数，见界面里的说明）。</summary>
    [JsonIgnore]
    public string UnitLabel { get; set; } = "到访";

    /// <summary>列表里直接显示填的那个数（从 1 开始），和输入框一模一样。</summary>
    [JsonIgnore]
    public string Display =>
        (Times < 0 ? "每次都可能说" : $"第 {Times} {UnitLabel}")
        + $" · {Lines.Count} 句" + (Repeating ? " · 可重复" : "");

    [JsonIgnore]
    public bool HasAnyText => Lines.Any(l => !string.IsNullOrWhiteSpace(l.Text));
}
/// <summary>对话里的一句。</summary>
public sealed class AncientLineSpec : SpecBase
{
    private bool _ancientSpeaks = true;
    private string _text = "";
    private string _nextText = "";

    /// <summary>true = 先古之民说（.ancient），false = 角色说（.char）</summary>
    public bool AncientSpeaks { get => _ancientSpeaks; set { if (Set(ref _ancientSpeaks, value)) Raise(nameof(Display)); } }

    /// <summary>这一句的文本</summary>
    public string Text { get => _text; set => Set(ref _text, value); }

    /// <summary>「下一句」按钮上的字（最后一句用不到）</summary>
    public string NextText { get => _nextText; set => Set(ref _nextText, value); }

    [JsonIgnore]
    public string Display => (AncientSpeaks ? "先古之民：" : "角色：") + (Text.Length > 26 ? Text[..26] + "…" : Text);
}
/// <summary>
/// 改写一个本体状态（增益/减益）的外观：名字 / 描述 / 图标 / 血条颜色 / 层数数字颜色。
///   · 名字与描述：走本地化表覆盖 —— mod 的 localization/zhs/powers.json 会盖上本体同名的键
///     （本体 LocManager 会把 mod 的表 MergeWith 到本体表上，这是本体自己支持的机制）；
///   · 图标与颜色：走生成的 Harmony 补丁（本体没有开关，只能补丁）。
/// 注意：这是**全局**改动 —— 本体其他角色身上的「中毒」也会跟着变成你写的名字/图标/颜色。
/// </summary>
public sealed class VanillaPowerOverride : SpecBase
{
    private bool _enabled = true;
    private string _powerId = "";
    private string _locSlug = "";
    private string _vanillaName = "";
    private string _name = "";
    private string _description = "";
    private string _icon = "";
    private string _barColor = "";
    private string _amountColor = "";
    private bool _replaceInVanillaText = true;

    /// <summary>不勾选就先不生效（留着配置以后再用）</summary>
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>本体状态（Power）的类名，如 PoisonPower。
    /// 换状态时会把「本地化键 / 原名 / 名字 / 描述」一起换成新状态的 —— 否则界面上还显示上一个状态，
    /// 看着像「改了没反应」（用户报过这个）。图标和颜色是上一个状态的，换状态就清掉。</summary>
    public string PowerId
    {
        get => _powerId;
        set
        {
            if (!Set(ref _powerId, value)) return;
            string newSlug = EffectCatalog.SlugFor(value);
            // 从存档读回来时键已经对得上 → 不要覆盖用户存下来的名字/描述
            if (string.Equals(_locSlug, newSlug, StringComparison.Ordinal)) return;

            var p = EffectCatalog.FindPower(value);
            _locSlug = p?.Slug ?? newSlug;
            _vanillaName = p?.Zh ?? "";
            _name = _vanillaName;                                   // 用新状态的本体名打底，接着改就行
            _description = EffectCatalog.ZhLocText((_locSlug.Length > 0 ? _locSlug : newSlug) + ".description") ?? "";
            _icon = "";
            _barColor = "";
            _amountColor = "";
            Raise(nameof(LocSlug));
            Raise(nameof(VanillaName));
            Raise(nameof(Name));
            Raise(nameof(Description));
            Raise(nameof(Icon));
            Raise(nameof(BarColor));
            Raise(nameof(AmountColor));
            Raise(nameof(Display));
        }
    }

    /// <summary>本体 powers 表里的键前缀（如 POISON_POWER）；从目录里选状态时自动记下来，改名的键就靠它</summary>
    public string LocSlug { get => _locSlug; set => Set(ref _locSlug, value); }

    /// <summary>这个状态在本体里的原名（只用于界面显示「原名 → 新名」，不参与生成）</summary>
    public string VanillaName { get => _vanillaName; set => Set(ref _vanillaName, value); }

    /// <summary>新显示名（留空 = 不改名）</summary>
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>新描述（留空 = 不改描述；可以和本体原文一样，那样等于没改）</summary>
    public string Description { get => _description; set => Set(ref _description, value); }

    /// <summary>新图标（本机图片路径，留空 = 不改图标）</summary>
    public string Icon { get => _icon; set => Set(ref _icon, value); }

    /// <summary>血条颜色（RRGGBB，留空 = 不改）。本体血条上只有「中毒」和「灾厄」两截是按状态显示的
    /// （中毒那截是纯颜色；灾厄那截是着色器渐变，会给你换一条件构同色的渐变）</summary>
    public string BarColor { get => _barColor; set => Set(ref _barColor, value); }

    /// <summary>层数数字颜色（RRGGBB，留空 = 本体默认：减益红、其它米白）</summary>
    public string AmountColor { get => _amountColor; set => Set(ref _amountColor, value); }

    /// <summary>改名时顺便把本体卡牌/遗物/药水描述里的旧名字也换掉（推荐：不换的话卡面还写着旧名）</summary>
    public bool ReplaceInVanillaText { get => _replaceInVanillaText; set => Set(ref _replaceInVanillaText, value); }

    /// <summary>这条改写到底改了什么（都空 = 什么都没改，生成时跳过）</summary>
    [JsonIgnore]
    public bool ChangesAnything =>
        !string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(Description)
        || !string.IsNullOrWhiteSpace(Icon) || !string.IsNullOrWhiteSpace(BarColor)
        || !string.IsNullOrWhiteSpace(AmountColor);

    /// <summary>列表里显示的一行。</summary>
    [JsonIgnore]
    public string Display
    {
        get
        {
            var tags = new List<string>();
            if (!string.IsNullOrWhiteSpace(Name) && Name != VanillaName) tags.Add("改名");
            if (!string.IsNullOrWhiteSpace(Description)) tags.Add("改描述");
            if (!string.IsNullOrWhiteSpace(Icon)) tags.Add("换图标");
            if (!string.IsNullOrWhiteSpace(BarColor)) tags.Add("血条色");
            if (!string.IsNullOrWhiteSpace(AmountColor)) tags.Add("数字色");
            string from = string.IsNullOrWhiteSpace(VanillaName) ? PowerId : VanillaName;
            string to = string.IsNullOrWhiteSpace(Name) ? from : Name;
            string tail = tags.Count == 0 ? "（还没填要改什么）" : "（" + string.Join(" / ", tags) + "）";
            return (Enabled ? "" : "[停用] ") + $"{from} → {to}　{tail}";
        }
    }
}

/// <summary>
/// 自定义状态（能力牌用的那个「你自己的状态」）。
/// 工具会生成一个 PowerModel 子类：名字/描述走本地化表，图标/层数颜色走补丁，
/// 触发时机和效果由 <see cref="Triggers"/> 里的每条触发器生成对应钩子方法。
/// </summary>
public sealed class CustomPowerSpec : SpecBase
{
    private string _name = "";
    private string _className = "";
    private string _description = "";
    private string _icon = "";
    private string _type = "Buff";
    private string _amountColor = "";
    private bool _single;
    private int _decayPerTurn;
    private bool _removeAtTurnEnd;
    private bool _enabled = true;

    /// <summary>中文显示名（游戏里状态栏和悬停提示用的名字）</summary>
    public string Name { get => _name; set => Set(ref _name, value); }
    /// <summary>英文类名（留空自动生成 &lt;角色类名&gt;Power&lt;序号&gt;）</summary>
    public string ClassName { get => _className; set => Set(ref _className, value); }
    /// <summary>描述（留空自动按触发器生成一句话；{Amount} = 层数占位符）</summary>
    public string Description { get => _description; set => Set(ref _description, value); }
    /// <summary>图标（本机图片路径，留空 = 用本体的 missing_power 占位图）</summary>
    public string Icon { get => _icon; set => Set(ref _icon, value); }
    /// <summary>Buff / Debuff（影响状态栏图标的边框颜色与层数数字的默认颜色）</summary>
    public string Type { get => _type; set => Set(ref _type, value); }
    /// <summary>层数数字颜色（RRGGBB，留空 = 本体默认：减益红、其它米白）</summary>
    public string AmountColor { get => _amountColor; set => Set(ref _amountColor, value); }
    /// <summary>只叠一层（本体有些状态不叠层，重复施加只刷新）</summary>
    public bool Single { get => _single; set => Set(ref _single, value); }
    /// <summary>每回合减几层（0 = 不衰减；减到 0 就移除）</summary>
    public int DecayPerTurn { get => _decayPerTurn; set => Set(ref _decayPerTurn, value); }
    /// <summary>回合结束时直接移除（临时状态：不管多少层，本回合结束就没了）</summary>
    public bool RemoveAtTurnEnd { get => _removeAtTurnEnd; set => Set(ref _removeAtTurnEnd, value); }
    /// <summary>不勾选就先不生效（留着配置以后再用）</summary>
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>触发时机 + 每个时机下要执行的效果。</summary>
    public ObservableCollection<PowerTriggerSpec> Triggers { get; set; } = new();

    [JsonIgnore]
    public string Display
    {
        get
        {
            string who = string.IsNullOrWhiteSpace(Name) ? "(还没起名)" : Name;
            string kind = Type == "Debuff" ? "减益" : "增益";
            string tg = Triggers.Count == 0 ? "还没配触发时机" : string.Join(" + ", Triggers.Select(t => t.Display));
            return (Enabled ? "" : "[停用] ") + $"{who}（{kind}）　{tg}";
        }
    }
}

/// <summary>自定义状态的一条触发时机：什么时候触发 + 触发时做哪些事。</summary>
public sealed class PowerTriggerSpec : SpecBase
{
    private string _kind = "TurnStart";
    private string _cardFilter = "Any";
    private string _powerId = "";
    private string _powerTarget = "Self";

    /// <summary>触发器种类（见 EffectCatalog.PowerTriggers）</summary>
    public string Kind { get => _kind; set { if (Set(ref _kind, value)) { Raise(nameof(Display)); Raise(nameof(Summary)); Raise(nameof(NeedsPower)); Raise(nameof(NeedsCardFilter)); } } }

    /// <summary>只对某类牌生效：Any / Attack / Skill / Power（只有「打出牌后」用得到）</summary>
    public string CardFilter { get => _cardFilter; set { if (Set(ref _cardFilter, value)) Raise(nameof(Display)); } }

    /// <summary>
    /// 只对「某一个状态」的层数变化生效（只有「某个状态层数变化后」这个触发时机用得到）。
    /// 留空 = 自己以外的**任意**状态层数一变就触发；填了就是「只有这个状态的层数变化才触发」。
    /// 值是本体的 Power 类名（如 StrengthPower），也可以是自己「自定义状态」页做的状态类名。
    /// </summary>
    public string PowerId
    {
        get => _powerId;
        set { if (Set(ref _powerId, value ?? "")) { Raise(nameof(Display)); Raise(nameof(Summary)); } }
    }

    /// <summary>
    /// 「某个状态层数变化后」盯的是谁身上的状态：Self（自己，默认）/ AnyEnemy（任意敌人）。
    /// 那个钩子没有「这张牌打的目标」，所以没有「指定敌人」这一档。
    /// </summary>
    public string PowerTarget
    {
        get => _powerTarget;
        set { if (Set(ref _powerTarget, string.IsNullOrWhiteSpace(value) ? "Self" : value)) { Raise(nameof(Display)); Raise(nameof(Summary)); } }
    }

    [JsonIgnore] public bool NeedsPower => Kind == "PowerChanged";
    [JsonIgnore] public bool NeedsCardFilter => Kind == "CardPlayed";

    /// <summary>只有「打出牌后」才显示「牌类型」下拉；只有「某个状态层数变化后」才显示「哪个状态」下拉。</summary>
    [JsonIgnore] public bool ShowCardFilter => NeedsCardFilter;

    /// <summary>触发时执行的效果（可以加好几条，按顺序执行）</summary>
    public ObservableCollection<EffectSpec> Effects { get; set; } = new();

    /// <summary>
    /// 效果被改了 / 加了 / 删了：让这一行的显示文本（列表里那行、还有界面上那句说明）重新算。
    /// 效果自己不知道自己属于哪个触发器，所以由界面在监听到效果变化时调一下这个方法。
    /// </summary>
    public void NotifyEffectsChanged()
    {
        Raise(nameof(Display));
        Raise(nameof(Summary));
    }

    /// <summary>一句话说明「什么时候 → 做什么」（界面上那句实时说明用它）。</summary>
    [JsonIgnore]
    public string Summary => PowerTriggers.Plain(PowerTriggers.TriggerLine(this));

    [JsonIgnore]
    public string Display
    {
        get
        {
            string opt = PowerTriggers.Find(Kind)?.Display ?? Kind;
            if (Kind == "CardPlayed" && CardFilter != "Any")
                opt += CardFilter switch { "Attack" => "（攻击牌）", "Skill" => "（技能牌）", "Power" => "（能力牌）", _ => "" };
            if (Kind == "PowerChanged")
                opt += string.IsNullOrWhiteSpace(PowerId)
                    ? $"（盯：{PowerTriggers.WatchTargetZh(PowerTarget)}的任意状态）"
                    : $"（盯：{PowerTriggers.WatchTargetZh(PowerTarget)}的{EffectCatalog.PowerName(PowerId, PowerId)}）";
            string fx = Effects.Count == 0 ? "（还没加效果）" : string.Join("，", Effects.Select(e => e.Display));
            return $"{opt} → {fx}";
        }
    }
}

/// <summary>
/// 条件选项：给整张卡牌 / 整个遗物加一个判断。
/// 卡牌：条件满足才生效；不满足时可以选择「不能打出」（本体 Clash / GrandFinale 的做法）
///       或「能打出但效果不生效」。
/// 遗物：条件满足才触发（不满足就直接 return）。
/// 条件本身都用本体已有的判断方式实现（战斗历史、手牌/牌堆、生命值、状态层数等）。
/// </summary>
public sealed class ConditionSpec : SpecBase
{
    private string _kind = "None";
    private decimal _amount = 1;
    private string? _powerId;
    private string _whenUnmet = "Unplayable";
    private string _target = "Self";

    /// <summary>条件种类（EffectCatalog.Conditions 里的 Id；None = 无条件）</summary>
    public string Kind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value))
            {
                Raise(nameof(NeedsAmount));
                Raise(nameof(NeedsPower));
                Raise(nameof(NeedsTarget));
                Raise(nameof(IsNone));
                Raise(nameof(DisplayShort));
                Raise(nameof(Summary));
            }
        }
    }

    /// <summary>
    /// 指向对象：这个条件看的是谁身上的东西（自己 / 指定敌人 / 任意一个敌人 / 全部敌人）。
    /// 只有 <see cref="NeedsTarget"/> 的条件（状态层数、生命值百分比）用得到；
    /// 「全部敌人」= 每个活着的敌人都满足，「任意一个敌人」= 只要有一个满足。
    /// </summary>
    public string Target
    {
        get => _target;
        set
        {
            if (Set(ref _target, value))
            {
                Raise(nameof(TargetZh));
                Raise(nameof(DisplayShort));
                Raise(nameof(Summary));
            }
        }
    }

    /// <summary>数值参数（「至少 N 张」「低于 N%」「至少 N 层」这类条件用得到）</summary>
    public decimal Amount
    {
        get => _amount;
        set { if (Set(ref _amount, value)) Raise(nameof(Summary)); }
    }

    /// <summary>「拥有某状态」用的增益/减益（本体的 Power 类名）</summary>
    public string? PowerId
    {
        get => _powerId;
        set { if (Set(ref _powerId, value)) Raise(nameof(Summary)); }
    }

    /// <summary>卡牌专用：条件不满足时 Unplayable = 不能打出 / NoEffect = 能打出但效果不生效</summary>
    public string WhenUnmet
    {
        get => _whenUnmet;
        set { if (Set(ref _whenUnmet, value)) { Raise(nameof(UnplayableWhenUnmet)); Raise(nameof(Summary)); } }
    }

    /// <summary>界面用：勾上 = 条件不满足时不能打出；取消 = 能打出但效果不生效</summary>
    [JsonIgnore]
    public bool UnplayableWhenUnmet
    {
        get => !string.Equals(_whenUnmet, "NoEffect", StringComparison.OrdinalIgnoreCase);
        set => WhenUnmet = value ? "Unplayable" : "NoEffect";
    }

    [JsonIgnore] public bool IsNone => string.IsNullOrWhiteSpace(_kind) || _kind == "None";
    [JsonIgnore] public bool NeedsAmount => EffectCatalog.FindCondition(_kind)?.NeedsAmount == true;
    [JsonIgnore] public bool NeedsPower => EffectCatalog.FindCondition(_kind)?.NeedsPower == true;
    [JsonIgnore] public bool NeedsTarget => EffectCatalog.FindCondition(_kind)?.NeedsTarget == true;

    /// <summary>指向对象的中文（界面上/描述里显示）。</summary>
    [JsonIgnore]
    public string TargetZh => (_target ?? "Self") switch
    {
        "Enemy" => "指定敌人",
        "AnyEnemy" => "任意一个敌人",
        "AllEnemies" => "全部敌人",
        _ => "自己",
    };

    /// <summary>列表里显示的短名（带参数）</summary>
    [JsonIgnore]
    public string DisplayShort
    {
        get
        {
            var opt = EffectCatalog.FindCondition(_kind);
            if (opt is null || IsNone) return "";
            string text = opt.Display;
            if (opt.NeedsAmount) text = text.Replace("N", _amount.ToString("0.##"));
            if (opt.NeedsPower) text = text.Replace("某状态", EffectCatalog.PowerName(_powerId, "某状态"));
            // 指向对象不是「自己」时补一句（「全部敌人」这种条件光看名字不知道看谁）
            if (opt.NeedsTarget && !string.Equals(_target, "Self", StringComparison.OrdinalIgnoreCase))
                text += $"（{TargetZh}）";
            // 「额外资源量」用界面/游戏里真正显示的那个名字（用户可能叫它「冰附魔」）——
            // 生成卡面描述走的是 LocalizationGen（那边显式传名字），这里是界面列表里的显示。
            if (ResourceDisplayName.Length > 0 && ResourceDisplayName != "额外资源量")
                text = text.Replace("额外资源量", ResourceDisplayName);
            return text;
        }
    }

    /// <summary>
    /// 「额外资源量」在界面上的名字（「额外资源量/状态」页里用户填的那个）。
    /// 界面在载入存档 / 改名字时同步它，好让条件说明和计数器上的名字一致。
    /// 生成出来的卡面描述不依赖它（LocalizationGen 直接读存档里的名字）。
    /// </summary>
    public static string ResourceDisplayName = "额外资源量";

    /// <summary>生成的代码注释 / 卡面描述用的完整说明</summary>
    [JsonIgnore]
    public string Summary => IsNone ? "" : DisplayShort;
}

public sealed class PotionSpec : SpecBase
{
    private string _name = "新药水";
    private string _className = "";
    private string _rarity = "Common";
    private string _usage = "CombatOnly";
    private string _targetType = "Self";
    private bool _inPotionPool = true;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string ClassName { get => _className; set => Set(ref _className, value); }
    /// <summary>Common / Uncommon / Rare</summary>
    public string Rarity { get => _rarity; set => Set(ref _rarity, value); }
    /// <summary>CombatOnly / AnyTime</summary>
    public string Usage { get => _usage; set => Set(ref _usage, value); }
    /// <summary>Self / AnyEnemy / AllEnemies</summary>
    public string TargetType { get => _targetType; set => Set(ref _targetType, value); }
    public bool InPotionPool { get => _inPotionPool; set => Set(ref _inPotionPool, value); }

    /// <summary>药水图标（物品栏小图标 + 弹窗大图都用它）。留空则用本体药水图占位。</summary>
    public string? Icon { get; set; }
    /// <summary>药水图标描边（选中高亮用）。留空则跟图标同图。</summary>
    public string? IconOutline { get; set; }

    public ObservableCollection<EffectSpec> Effects { get; set; } = new();

    [JsonIgnore]
    public string Display =>
        $"{Name}  ｜ {Rarity} ｜ {Usage} → {TargetType}{(InPotionPool ? "" : " ｜ 不入池")}";
}

/// <summary>
/// 一条「自定义关键词」。
///
/// 为什么要这么做：本体的关键词是 <c>CardKeyword</c> **封闭枚举**（消耗 / 虚无 / 固有 / 保留 / 奇巧 /
/// 不能被打出 / 永恒），模组既不能加枚举成员、也没有任何注册 API。但本体的悬停提示本身是通用的：
/// <c>CardModel/RelicModel/PotionModel.ExtraHoverTips</c> 是虚属性，可以塞任意
/// <c>new HoverTip(new LocString(表, 键), new LocString(表, 键))</c>，而本地化表是按「同名文件、逐键合并」
/// 加载的（模组可以往本体的 <c>card_keywords</c> 表里加自己的键）。
///
/// 所以一条自定义关键词由三部分组成（生成时自动产出）：
/// ① 本地化：模组工程里 <c>localization/&lt;语言&gt;/card_keywords.json</c> 的
///    <c>&lt;KEY&gt;.title</c> / <c>&lt;KEY&gt;.description</c>；
/// ② 悬停说明：引用它的卡牌生成 <c>ExtraHoverTips</c>，悬停卡牌时显示「名称 + 说明」；
/// ③ 卡面文字：描述开头自动拼 <c>[gold]名称[/gold]。</c>（和本体 <c>GetCardText()</c> 的观感一致）。
///
/// 全程不改本体、不需要 Harmony 补丁。
/// </summary>
public sealed class CustomKeywordSpec : SpecBase
{
    private string _name = "新关键词";
    private string _key = "";
    private string _description = "";

    /// <summary>卡面上显示的名字（本体关键词也是中文，例如「消耗」「虚无」）。</summary>
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>
    /// 英文标识：本地化键的前缀（<c>&lt;KEY&gt;.title</c>）。留空时生成器按序号自动补（<c>KEYWORD_1</c>…）。
    /// 只能用 ASCII 字母/数字/下划线，且不能与本体那 8 个关键词的键撞名。
    /// </summary>
    public string Key { get => _key; set => Set(ref _key, value); }

    /// <summary>鼠标悬停在卡牌上时显示的说明文字（可以写多行）。</summary>
    public string Description { get => _description; set => Set(ref _description, value); }

    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(Key) ? Name : $"{Name}  ｜ {Key}";
}

/// <summary>
/// 本体关键词清单（枚举名 + 本体中文名/说明）。界面下拉、校验、旧名全文替换都靠它。
///
/// 为什么要写死这份清单：本体的 <c>CardKeyword</c> 是**封闭枚举**，文案在
/// <c>localization/zhs/card_keywords.json</c>（键 = <c>StringHelper.Slugify(枚举名)</c>）。
/// 枚举一共有 7 个正式关键词（另有 <c>NONE</c> 与 <c>PERIOD</c> 两个非关键词的占位键），
/// 模组既读不到枚举清单、也不该为了显示 7 行去反射游戏 dll，所以顺序和枚举名在这里固定下来。
/// <see cref="Vanilla"/> 只是**兜底**：本机读得到本体 <c>card_keywords.json</c> 时一律以那份为准
/// （见 <c>EffectCatalog.VanillaKeywordLoc</c>），读不到也能把界面/替换跑起来。
/// </summary>
public static class VanillaKeywordCatalog
{
    /// <summary>本体关键词一条：枚举名 + 本体中文名（+ 本体中文说明，读不到就是空串）。</summary>
    public sealed record Entry(string Id, string VanillaName, string VanillaDescription);

    /// <summary>本体那 7 个关键词（顺序固定，界面表格就按这个顺序排）。</summary>
    public static readonly IReadOnlyList<Entry> All = new[]
    {
        new Entry("EXHAUST",    "消耗",       "打出后进入消耗堆，本场战斗内不再回到牌堆。"),
        new Entry("ETHEREAL",   "虚无",       "若这张牌在你的回合结束时仍在手牌中，将其消耗。"),
        new Entry("INNATE",     "固有",       "战斗开始时，这张牌必定在你的起始手牌中。"),
        new Entry("UNPLAYABLE", "不能被打出", "这张牌不能被打出。"),
        new Entry("RETAIN",     "保留",       "回合结束时，这张牌不会被弃掉。"),
        new Entry("SLY",        "奇巧",       "若这张牌因弃牌离开手牌，则免费打出。"),
        new Entry("ETERNAL",    "永恒",       "这张牌不能被消耗（消耗效果对它无效）。"),
    };

    /// <summary>这个 id 是不是那 7 个之一（大小写不敏感，读手写 JSON 时兜底）。</summary>
    public static bool IsKnown(string? id) => ById(id) is not null;

    /// <summary>按枚举名查一条（大小写不敏感）。</summary>
    public static Entry? ById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        string want = id.Trim();
        foreach (var e in All)
            if (string.Equals(e.Id, want, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    /// <summary>本体中文名（不知道这个 id 就原样返回）。</summary>
    public static string VanillaNameOf(string? id) => ById(id)?.VanillaName ?? (id ?? "").Trim();
}

/// <summary>
/// 一条「本体关键词改名」：把本体某个关键词（消耗 / 虚无 / 固有 / 保留 / 奇巧 / 不能被打出 / 永恒）
/// 在游戏里显示的**名字**（和悬停提示里的**说明**）换成你自己的。
///
/// 机制（本体自己支持的，不需要任何 Harmony 补丁）：
///   本体加载本地化时会把模组的 <c>localization/&lt;语言&gt;/card_keywords.json</c> **逐键合并**进本体那张表
///   （<c>LocTable.MergeWith</c>，模组的键盖本体的），所以只要写 <c>EXHAUST.title = 你的名字</c> 就生效。
///   卡面文字是本体 <c>CardKeywordExtensions.GetCardText()</c> 拼的 <c>[gold]&lt;本地化 title&gt;[/gold]。</c>，
///   改了 title 卡面上那个金色词自动跟着变；本体卡牌**描述正文**里写着的旧中文名（比如「消耗」两个字）
///   属于纯文本，另外由 <c>VanillaKeywordGen.KeywordTextReplacements</c> 覆盖本体表里那些键来换掉。
///
/// 注意：这是**全局**改动 —— 本体其他角色的卡上出现的同一个关键词也会跟着改名。
/// </summary>
public sealed class VanillaKeywordRenameSpec : SpecBase
{
    private string _keywordId = "";
    private string _vanillaName = "";
    private string _name = "";
    private string _description = "";

    /// <summary>本体枚举名（EXHAUST / ETHEREAL / INNATE / UNPLAYABLE / RETAIN / SLY / ETERNAL）。</summary>
    public string KeywordId
    {
        get => _keywordId;
        set
        {
            if (!Set(ref _keywordId, (value ?? "").Trim())) return;
            // 名字只是界面上的参考文本（生成完全不读它），换关键词时跟着换一下更直观
            _vanillaName = VanillaKeywordCatalog.VanillaNameOf(_keywordId);
            Raise(nameof(VanillaName));
            Raise(nameof(Display));
        }
    }

    /// <summary>这个关键词在本体里的原名（只用于界面显示「原名 → 新名」，不参与生成）。</summary>
    public string VanillaName
    {
        get => _vanillaName.Length > 0 ? _vanillaName : VanillaKeywordCatalog.VanillaNameOf(_keywordId);
        set => Set(ref _vanillaName, value ?? "");
    }

    /// <summary>新显示名（留空 = 不改名）。</summary>
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }

    /// <summary>新说明（留空 = 不改说明；可以和本体原文一样，那样等于没改）。</summary>
    public string Description { get => _description; set => Set(ref _description, value ?? ""); }

    /// <summary>这条到底改了什么（都空 = 没改，生成时跳过）。</summary>
    [JsonIgnore]
    public bool ChangesAnything => !string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(Description);

    /// <summary>列表 / 表格里显示的一行。</summary>
    [JsonIgnore]
    public string Display
    {
        get
        {
            string from = VanillaName;
            string to = string.IsNullOrWhiteSpace(Name) ? from : Name.Trim();
            var tags = new List<string>();
            if (!string.IsNullOrWhiteSpace(Name)) tags.Add("改名");
            if (!string.IsNullOrWhiteSpace(Description)) tags.Add("改说明");
            string tail = tags.Count == 0 ? "（还没填要改什么）" : "（" + string.Join(" / ", tags) + "）";
            return $"{from} → {to}　{tail}";
        }
    }
}

/// <summary>
/// 卡牌配色：本体就是卡池上的三个字段（边框材质 / 牌堆底色 / 能量图标描边），对整个角色的所有卡生效。
/// </summary>
public sealed class CardColorSpec : SpecBase
{
    private string _cardFrame = "card_frame_red";
    private string _cardFrameColor = "";
    private string _deckEntryColor = "D62000";
    private string _energyOutlineColor = "802020";

    /// <summary>本体 materials/cards/frames 里实际存在的框色（写成 CardFrameMaterialPath 的值）。</summary>
    public static readonly string[] Frames =
    [
        "card_frame_red",       // 铁甲战士
        "card_frame_green",     // 静默猎手
        "card_frame_blue",      // 缺陷
        "card_frame_pink",      // 亡灵绑定者
        "card_frame_orange",    // 摄政
        "card_frame_colorless", // 无色
        "card_frame_curse",     // 诅咒
        "card_frame_quest",     // 任务
    ];

    public sealed record Preset(string Name, string Frame, string Deck, string Outline);

    /// <summary>本体各角色的配色，一键套用。</summary>
    public static readonly Preset[] Presets =
    [
        new("铁甲红（默认）", "card_frame_red",       "D62000", "802020"),
        new("静默绿",         "card_frame_green",     "5EBD00", "1A6625"),
        new("缺陷蓝",         "card_frame_blue",      "3EB3ED", "1D5673"),
        new("亡灵粉",         "card_frame_pink",      "CD4EED", "803367"),
        new("摄政橙",         "card_frame_orange",    "E36600", "803D0E"),
        new("无色灰",         "card_frame_colorless", "A3A3A3", "606060"),
        new("诅咒紫灰",       "card_frame_curse",     "585B61", "2B2D31"),
        new("任务深蓝",       "card_frame_quest",     "24476A", "431E14"),
    ];

    /// <summary>卡牌边框材质（不含 _mat.tres 后缀）。留空或配合 CardFrameColor 使用。</summary>
    public string CardFrame { get => _cardFrame; set => Set(ref _cardFrame, value); }

    /// <summary>
    /// 卡牌边框颜色（RRGGBB，可留空）。填了就按这个颜色的 HSV 生成一份自己的边框材质
    /// （本体边框材质是 hsv.gdshader 的 ShaderMaterial，h/s/v 三个参数），
    /// 这样边框颜色可以任意指定，不再局限于本体那 8 种框色。
    /// </summary>
    public string CardFrameColor { get => _cardFrameColor; set => Set(ref _cardFrameColor, value); }

    /// <summary>牌堆/牌组里这张卡显示的底色（#RRGGBB 或 RRGGBB）。</summary>
    public string DeckEntryColor { get => _deckEntryColor; set => Set(ref _deckEntryColor, value); }

    /// <summary>能量图标的描边色（#RRGGBB 或 RRGGBB）。</summary>
    public string EnergyOutlineColor { get => _energyOutlineColor; set => Set(ref _energyOutlineColor, value); }

    [System.Text.Json.Serialization.JsonIgnore]
    public string HexPreview => $"{NormalizeHex(DeckEntryColor)} / {NormalizeHex(EnergyOutlineColor)}";

    /// <summary>清洗成 Godot 能识别的 RRGGBB / RRGGBBAA（非法值返回空串）。</summary>
    public static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string s = value.Trim().TrimStart('#').ToUpperInvariant();
        if (s.Length is not (6 or 8)) return "";
        foreach (char c in s)
            if (!Uri.IsHexDigit(c)) return "";
        return s;
    }

    public static bool IsHex(string? value) => NormalizeHex(value).Length > 0;

    /// <summary>当前三项是否正好等于某个预设（用于界面上自动选中/切到「自定义」）。</summary>
    public Preset? MatchingPreset()
    {
        if (NormalizeHex(CardFrameColor).Length > 0) return null;   // 用了自定义边框色 → 不算任何预设
        foreach (var preset in Presets)
            if (CardFrame == preset.Frame
                && NormalizeHex(DeckEntryColor) == NormalizeHex(preset.Deck)
                && NormalizeHex(EnergyOutlineColor) == NormalizeHex(preset.Outline))
                return preset;
        return null;
    }

    /// <summary>某个官方框色对应的「种子颜色」（从预设表里取，用于选「自定义」时预填一个相近色）。</summary>
    public static string SeedColorForFrame(string? frame)
    {
        foreach (var preset in Presets)
            if (preset.Frame == frame) return preset.Deck;
        return "D62000";
    }

    /// <summary>套用一份预设。</summary>
    public void Apply(Preset preset)
    {
        CardFrame = preset.Frame;
        CardFrameColor = "";          // 用预设时清掉自定义颜色，回到本体官方框色
        DeckEntryColor = preset.Deck;
        EnergyOutlineColor = preset.Outline;
        NotifyChanged();
    }

    /// <summary>让界面刷新派生字段（预览文字等）。</summary>
    public void NotifyChanged()
    {
        Raise(nameof(HexPreview));
        Raise(nameof(Display));
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Display => $"{CardFrame} · {NormalizeHex(DeckEntryColor)}";
}

public sealed class ArtSpec{
    public string? Icon { get; set; }
    public string? IconOutline { get; set; }
    /// <summary>顶部头像描边颜色（RRGGBB / RRGGBBAA）。填了就按这个颜色自动生成描边图，
    /// 不用再自己传描边 PNG；留空则沿用上传的描边图或中性占位。</summary>
    public string? IconOutlineColor { get; set; }
    public string? SelectIcon { get; set; }
    /// <summary>选人界面背景大图（角色背后那张大立绘）。留空则自动用静态立绘那张图。</summary>
    public string? SelectBackground { get; set; }
    public string? SelectIconLocked { get; set; }
    public string? MapMarker { get; set; }
    public string? EnergyIcon { get; set; }
    public string? Transition { get; set; }
    /// <summary>静态战斗/商店/篝火立绘（替代本体的 Spine 骨骼动画）。留空则用工具自带占位图。</summary>
    public string? CharacterStatic { get; set; }
    /// <summary>没上传的槽位是否用本体（铁甲战士）素材占位。默认 false = 生成中性占位图，
    /// 这样发布出去的模组不含本体美术；本地随手测试可以勾上。</summary>
    public bool UseVanillaPlaceholders { get; set; }
    public Dictionary<string, string> CardPortraits { get; set; } = new();
}

/// <summary>
/// 额外资源量/状态：对应游戏本体的「星星」资源 —— 它和能量一样是个计数器，
/// 显示在角色血条下方、与状态同一行（本体储君用的就是它）。
/// 本工具把它开放成一个可配置的"额外资源量"：可设初始数量、是否跨战斗继承。
/// </summary>
public sealed class ExtraResourceSpec
{
    /// <summary>是否启用（不启用就不生成任何相关代码）。</summary>
    public bool Enabled { get; set; }

    /// <summary>每场战斗开始时的初始数量。</summary>
    public int Initial { get; set; }

    /// <summary>是否跨战斗继承。
    /// false = 每场战斗开始时固定发放「初始数量」；
    /// true  = 每场战斗开始时发放「初始数量 + 上一场战斗结束时剩下的数量」。</summary>
    public bool CarryOver { get; set; }

    /// <summary>是否始终显示计数器（关掉的话，只有数量大于 0 时游戏才会显示它）。</summary>
    public bool AlwaysShowCounter { get; set; } = true;

    /// <summary>资源的中文显示名（留空 = 沿用本体的「辉星」）。会写进悬停提示、卡面描述和那个隐藏遗物的名字里。</summary>
    public string Name { get; set; } = "";

    /// <summary>自定义图标：本机 PNG 文件路径（留空 = 沿用本体的星星图标）。
    /// 建议方图（128×128 或 256×256）带透明背景。</summary>
    public string? Icon { get; set; }

    /// <summary>是否在计数器下方显示名字（关掉就只在悬停提示里显示）。</summary>
    public bool ShowName { get; set; } = true;
}
/// <summary>
/// 召唤物（奥斯提式的基础伙伴）。
///
/// 走的是什么机制（都在本体的公开 API 上，**不需要任何 Harmony 补丁**）：
///   · 上场：<c>PlayerCmd.AddPet&lt;T&gt;(player)</c>（本体 Byrdpip / Pael's Legion 就是这么用的）；
///   · 生成类：模组里的 <c>MonsterModel</c> 子类会被本体自动扫进 <c>ModelDb</c>（按类名注册）；
///   · 站位：覆写 <c>AfterCreatureAddedToCombat</c> —— 本体的 <c>CreatureCmd.Add</c> 顺序是
///     「加进战斗 → 摆位 → 跑 AfterCreatureAddedToCombat 钩子」，所以在钩子里改位置不会被抢回去
///     （本体的 <c>NCombatRoom.AddCreature</c> 把非 Osty 的宠物摆在主人 X+20 处，离得太近了）；
///   · 血条：本体的 <c>NCombatRoom.AddCreature</c> 对非 Osty 宠物无条件 <c>ToggleIsInteractable(false)</c>，
///     而 <c>NCreature._Ready</c> 只在建节点时按 <c>IsHealthBarVisible</c> 设一次 ——
///     战斗中召唤的宠物因此永远没血条，必须召唤后自己 <c>SetCreatureIsInteractable(pet, true)</c>；
///   · 指挥它打人：先走正常卡牌路径（<c>FromCard</c> + <c>Targeting</c>），再用扩展方法
///     <c>FromPetAttacker</c> 把攻击者换成宠物 —— 本体 <c>AttackCommand</c> 的 <c>FromMonster</c> 会把来源标成
///     Monster（于是 <c>GetPossibleTargets()</c> 硬编码返回「玩家自己人」），还会强制
///     <c>TargetingAllOpponents</c>（先把 <c>_combatState</c> 设上），和 <c>Targeting</c> 互相冲突，用不了。
/// 仍然不做的事：跨战斗保留（<c>PlayerCombatState.AfterCombatEnd()</c> 会清空宠物，
/// 宠物每场战斗都要重新召唤，这是本体机制）。
/// </summary>
public sealed class SummonSpec : SpecBase
{
    private bool _enabled = true;
    private string _className = "";
    private string _name = "";
    private int _hp = 8;
    private string? _image;
    private bool _takesDamageForOwner;
    private int _standDistance = DefaultStandDistance;

    /// <summary>默认站位距离（主人 X + 110）。本体的奥斯提是 150~250，所以 110 比它近、比本体的 20 远得多。</summary>
    public const int DefaultStandDistance = 110;

    /// <summary>不勾选就完全不生成这只召唤物的代码（用到它的效果会被校验器报错拦住）。</summary>
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>英文类名（<c>MonsterModel</c> 子类的名字）。留空自动用 <c>&lt;角色类名&gt;Pet</c>（多只时按序号）。
    /// 本体的 <c>ModelDb</c> 只按<b>类名</b>算模型 ID（忽略命名空间），所以不能和本体的怪物重名、也不能互相重名。</summary>
    public string ClassName { get => _className; set => Set(ref _className, value); }

    /// <summary>中文名：显示在宠物名牌上（写进 <c>localization/zhs/monsters.json</c> 的 <c>&lt;ENTRY&gt;.name</c>）。</summary>
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>召唤时的生命值（同时作为它的最小 / 最大初始生命）。没有配置血量的召唤卡就用这个数。
    /// 必须 &gt; 0 —— 血量 ≤ 0 的宠物一上场就是死的，而死的宠物打不出任何伤害
    /// （<c>AttackCommand.Execute</c> 开头就 <c>if (Attacker.IsDead) return this;</c> 静默早退）。</summary>
    public int Hp { get => _hp; set => Set(ref _hp, value); }

    /// <summary>
    /// 宠物图片：本机 PNG 路径（可空）。
    /// 上传了就复制进工程、生成一个最小的 <c>scenes/creature_visuals/&lt;entry&gt;.tscn</c> 并 override
    /// <c>VisualsPath</c>；没上传就不生成场景，宠物走本体的 <c>creature_visuals/fallback</c> 占位
    /// （一张静态 error.png，能正常显示、能打、能死）。
    /// </summary>
    public string? Image { get => _image; set => Set(ref _image, value); }

    /// <summary>
    /// 替主人挨打：勾上以后召唤时给它挂一个守卫 Power（照本体 <c>DieForYouPower</c> 写），
    /// 主人受到的**可格挡攻击伤害**改由它承担；它死了以后战斗结束不会把它挪走
    /// （<c>ShouldCreatureBeRemovedFromCombatAfterDeath</c>）。
    ///
    /// 为什么整个存档**只能勾一只**：本体的 <c>Hook.ModifyUnblockedDamageTarget</c> 是**链式遍历**
    /// （<c>creature = item.ModifyUnblockedDamageTarget(creature, …)</c>），同时存在两个重定向者时
    /// 第二个看到的「target」已经是第一个换过的生物了，伤害最终归谁完全不可预期
    /// （本体自己也只有 <c>DieForYouPower</c> 这一款）。校验器会拦住这种配置。
    /// </summary>
    public bool TakesDamageForOwner { get => _takesDamageForOwner; set => Set(ref _takesDamageForOwner, value); }

    /// <summary>
    /// 站位距离：召唤时摆在「主人 X + 这个距离」处（越大越靠右、离主人越远；Y 固定比主人高 25 像素）。
    /// 本体对非 Osty 宠物硬编码成主人 X+20，看起来像叠在主人身上，所以这里自己摆。
    /// 本体的奥斯提是 150~250，默认给 110。
    /// </summary>
    public int StandDistance
    {
        get => _standDistance;
        set { if (Set(ref _standDistance, value)) Raise(nameof(Display)); }
    }

    /// <summary>界面上「站位距离」用的合法范围（太近会叠在主人身上，太远会跑出画面）。</summary>
    public const int MinStandDistance = 20;
    public const int MaxStandDistance = 600;

    [JsonIgnore]
    public string Display =>
        (Enabled ? "" : "[停用] ")
        + $"召唤物：{(string.IsNullOrWhiteSpace(Name) ? "(还没起名)" : Name.Trim())}"
        + $"  ｜ 生命 {Hp}"
        + (string.IsNullOrWhiteSpace(ClassName) ? " ｜ 类名自动" : " ｜ " + ClassName.Trim())
        + $" ｜ 站位 {StandDistance}"
        + (TakesDamageForOwner ? " ｜ 替主人挨打" : "")
        + (string.IsNullOrWhiteSpace(Image) ? "" : " ｜ 有自定义图片");
}

public sealed class PathsSpec
{
    /// <summary>解包后的原版工程目录。默认**留空**：这是唯一需要用户自己指定的路径，
    /// 界面上一开始就该是空的（以前默认写死了开发机的路径，换台电脑看着莫名其妙）。
    /// 程序启动时会自动在常见位置找一遍，找不到才让用户选。</summary>
    public string VanillaProject { get; set; } = "";
    /// <summary>
    /// 游戏 data 目录 / Godot / 安装目录的默认值一律**留空**：
    /// 以前这里写死了开发机上的路径，跟着程序集一起发给别人 ——
    /// 既暴露了无关的私人路径，换台电脑又全是错的。现在全部留空，靠
    /// <see cref="Generation.PathAutoDetect"/> 在程序启动时自动探测（注册表 Steam 路径 + 常见目录），
    /// 探不到再让用户选（「环境自检」页有选择按钮）。
    /// </summary>
    public string GameDataDir { get; set; } = "";
    public string GodotExe { get; set; } = "";
    /// <summary>dotnet.exe 的完整路径。留空 = 用 PATH 里的 dotnet；
    /// 整合包的「环境包」里带了便携版 .NET SDK，选上它就不必再自己装 SDK。</summary>
    public string DotnetExe { get; set; } = "";
    /// <summary>生成工程的输出目录（默认 = 生成器目录\自定义角色）</summary>
    public string OutputDir { get; set; } = "";
    /// <summary>安装目录（本体的 mods 目录）。默认留空，启动时自动探测。</summary>
    public string InstallDir { get; set; } = "";
}
