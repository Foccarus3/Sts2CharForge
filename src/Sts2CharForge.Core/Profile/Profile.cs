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

    /// <summary>
    /// **你自己上传的音频文件**（V0.2.155）：「播放音频文件（我上传的）」这个效果种类用它们。
    ///
    /// <para>本体自己的声音走 FMOD（只能放本体已有的事件），模组**注册不了新的 FMOD 事件**
    /// （bank 里存的是事件 GUID，凭路径注册不了）。所以「放自己的音频」这条路是**绕开 FMOD**：
    /// 生成时把文件复制到工程里的 <c>audio/</c> 目录（会一起打进 PCK），运行时用 Godot 自己的
    /// <c>AudioStreamPlayer</c> + <c>GD.Load&lt;AudioStream&gt;("res://audio/…")</c> 播。</para>
    /// </summary>
    public ObservableCollection<AudioAssetSpec> AudioAssets { get; set; } = new();

    public string ModId { get; set; } = "MyCharMod";
    public string ModDisplayName { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "v0.1.0";

    public string CharacterClass { get; set; } = "MyCharacter";
    public string DisplayName { get; set; } = "新角色";
    public string Description { get; set; } = "";
    public int StartingHp { get; set; } = 80;
    public int StartingGold { get; set; } = 99;

    /// <summary>
    /// 角色**每回合的能量上限**（V0.2.172，用户要求「增加角色最大能量」）。**默认 3 = 本体默认**。
    ///
    /// <para><b>引擎依据</b>：本体 <c>CharacterModel.MaxEnergy => 3</c>（虚属性，见
    /// <c>Core/Models/CharacterModel.cs:97</c>）；<c>Player</c> 建号时取 <c>character.MaxEnergy</c>，
    /// 之后 <c>PlayerCombatState.MaxEnergy => Hook.ModifyMaxEnergy(..., _player.MaxEnergy)</c>、
    /// 每回合 <c>ResetEnergy()</c> 按它回满。所以生成的角色类里写一句
    /// <c>public override int MaxEnergy => N;</c> 就是「这个角色每回合的能量上限 = N」——
    /// **不需要任何补丁**。</para>
    ///
    /// <para>⚠ <b>硬红线</b>：等于 3（本体默认）时生成端**一个字都不写** —— 老存档 / <c>--samplegen</c>
    /// 基线必须逐字不变（见 <c>CSharpCodeGen.CharacterSource</c> 里那段条件发射）。</para>
    /// </summary>
    public int MaxEnergy { get; set; } = 3;

    /// <summary>
    /// 「在地图上画线」（蜡笔 / 羽毛笔）的颜色（V0.2.230，用户要求「自定义在地图上的蜡笔的颜色」）。
    ///
    /// <para><b>引擎依据</b>：地图上画出来的每一条线都是 <c>Line2D</c>，颜色取自
    /// <c>NMapDrawings.CreateLine</c> 里那句 <c>line2D.DefaultColor = player.Character.MapDrawingColor;</c>
    /// （<c>Core/Nodes/Screens/Map/NMapDrawings.cs:321</c>）；
    /// 而 <c>CharacterModel.MapDrawingColor</c> 默认是 <c>Colors.Black</c>
    /// （<c>Core/Models/CharacterModel.cs:187</c>），本体每个角色各自覆盖成一个专属色：
    /// 铁甲战士 <c>CB282B</c>、静默猎手 <c>2F6729</c>、故障机器人 <c>0D638C</c>、
    /// 亡灵缚者 <c>AC0486</c>、摄政王 <c>935206</c>。</para>
    ///
    /// <para>所以生成的角色类里写一句 <c>public override Color MapDrawingColor => new Color("RRGGBB");</c>
    /// 就够了，**不需要任何补丁**；联机时队友看到的就是这个颜色（线本身是画在各自玩家的颜色上）。</para>
    ///
    /// <para>⚠ <b>红线</b>：留空（默认）= 生成端**一个字都不写** = 沿用本体默认黑色，
    /// 老存档 / <c>--samplegen</c> 基线必须逐字不变。非法值由校验器报**错误**（不会悄悄退回黑色）。</para>
    /// </summary>
    public string MapDrawingColor { get; set; } = "";
    /// <summary>Neutral / Feminine / Masculine</summary>
    public string Gender { get; set; } = "Masculine";
    /// <summary>死亡描述文本（本地化 eventDeathPrevention）</summary>
    public string DeathText { get; set; } = "";
    /// <summary>阵亡后回合结束台词（banter.dead.endTurnPing）</summary>
    public string DeadBanterText { get; set; } = "";
    /// <summary>
    /// 多人联机「催促」台词 = 战斗里对方还没结束回合时、右下角那个 <b>Ping</b> 按钮按下后
    /// 冒出的对话气泡（本体键：<c>characters</c> 表的 <c>&lt;角色&gt;.banter.alive.endTurnPing</c>）。
    ///
    /// <para>机制（对着本体源码确认过）：本体的 <c>NPingButton</c> 会在我方**结束回合之后**
    /// 从屏幕右下角滑出来（只在「还有别人没结束回合」时），按一下就走
    /// <c>RunManager.Instance.FlavorSynchronizer.SendEndTurnPing()</c> → 广播
    /// <c>EndTurnPingMessage</c> → 每个客户端在**那个玩家头上**冒 1.5 秒的对话气泡，
    /// 文案取自他自己那个角色的 <c>banter.alive.endTurnPing</c>（阵亡的人取 <c>.dead.</c> 那一档）。
    /// 本体每个角色都有一句（铁甲战士 <c>Make haste.</c> / 静默猎手 <c>…</c> / 故障机器人一串 jitter 乱码…）。</para>
    ///
    /// <para>留空 = 沿用工具一直以来的那句「……继续。」（老存档 / <c>--samplegen</c> 生成结果逐字不变）。
    /// 这一项**不需要任何补丁** —— 它就是我们自己角色的本地化表里的一行。</para>
    ///
    /// <remarks>⚠ 别和 <see cref="ArtSpec.TreasureFightText"/> 搞混：那一句是**宝箱房抢遗物**时
    /// 屏幕中间那行「开抢！！！」（本体键 <c>gameplay_ui.TREASURE_FIGHT_TEXT</c>），和 ping 完全无关。</remarks>
    /// </summary>
    public string EndTurnPingText { get; set; } = "";
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

    /// <summary>
    /// 「生成调试日志」（V0.2.142）：**默认 false**。
    ///
    /// 打开后，生成出来的模组会在每条效果真正执行的地方多打几行 <c>Godot.GD.Print</c>
    /// （游戏日志里的 <c>[INFO] [模组调试] …</c>），写明「谁在跑 / 目标是谁 / 执行前执行后的数值」——
    /// 专门用来排查**运行期**问题（例：填了 -2 力量在游戏里不生效）：这种问题日志里没有异常，
    /// 也看不出「目标身上本来有没有这个状态」，只能靠这个开关打出来。
    ///
    /// 三条硬约束：
    /// <list type="bullet">
    /// <item><b>只影响日志，不影响游戏行为</b>：加的全是「读一下 / 打一行」的语句，不碰任何状态与数值；</item>
    /// <item><b>默认关着时生成结果与 V0.2.141 逐字节一致</b>：这个字段读的就是它自己，
    ///   关着时生成端一行日志都不写（见 <c>DebugLogGen</c>）；</item>
    /// <item><b>必须进存档</b>（不加 <c>[JsonIgnore]</c>）：否则关掉开关再打开就变回 false，
    ///   而且老存档没有这个字段 → 读进来就是 false，行为与现在完全一致。</item>
    /// </list>
    /// </summary>
    public bool DebugLog { get; set; }

    public ObservableCollection<CardSpec> Cards { get; set; } = new();

    /// <summary>
    /// 自定义**诅咒**（「诅咒 / 先古卡」页里的第一个列表）。
    ///
    /// 和 <see cref="Cards"/> 是同一套 <see cref="CardSpec"/>（效果编辑器 / 卡面 / 自定义描述全都复用），
    /// 区别只在生成：费用固定 -1、类型 / 稀有度固定 Curse、自动加上本体的「不能被打出」，
    /// 效果不写在 OnPlay 里而是走本体的 <c>OnTurnEndInHand</c>
    /// （「在你的回合结束时，如果这张牌在你的手牌中……」—— 本体诅咒 Decay / Doubt / Shame / BadLuck 就是这么写的）。
    ///
    /// 为什么不留在一个列表里靠稀有度区分：分开列表后**界面上能一眼看清哪些是诅咒**，
    /// 而且诅咒的详情面板要换掉「费用 / 类型 / 稀有度 / 卡池」那几行（它们对诅咒没有意义）。
    /// 生成 / 卡池 / 素材 仍然按**稀有度**判断（见 <see cref="CardSpec.IsCurseCard"/>），
    /// 所以老存档里手改过稀有度的卡也照样能生成对。
    /// </summary>
    public ObservableCollection<CardSpec> Curses { get; set; } = new();

    /// <summary>
    /// 自定义**先古卡**（「诅咒 / 先古卡」页里的第二个列表）：稀有度 Ancient，
    /// 和普通卡唯一的区别是**不会进战斗奖励 / 随机生成**（本体 <c>CardFactory</c> 里 Ancient 被显式排除），
    /// 拿到它们的方式是先古之民给的遗物（本体「尘封的书 DustyTome」会从你的卡池里随机挑一张先古卡）
    /// 或者你自己的效果（「生成卡牌 / 获得卡牌（全局）」里选它）。
    /// </summary>
    public ObservableCollection<CardSpec> AncientCards { get; set; } = new();

    /// <summary>
    /// 「诅咒 / 先古卡」页里**诅咒**的外观（卡牌边框颜色）。
    /// 生成时会单独造一个「外观池」给诅咒用（本体只允许从**池**上取边框材质：<c>CardModel.FrameMaterial</c>
    /// 就是 <c>VisualCardPool.FrameMaterial</c>，卡本身改不了 —— 本体 Clash / DualWield 也是靠覆写
    /// <c>VisualCardPool</c> 借外观的）。留空 = 完全按老样子（用本体的诅咒卡框）。
    ///
    /// **不支持「自定义颜色」**（<see cref="SpecialCardStyleSpec.AllowsCustomColor"/> = false）：
    /// 这一项已经按用户要求在界面上去掉，老存档里存过的也一律忽略（退回本体诅咒卡框）。
    /// 可选的是「跟角色配色」和本体的那几种框色名。
    /// </summary>
    public SpecialCardStyleSpec CurseStyle
    {
        get
        {
            _curseStyle.AllowsCustomColor = false;
            return _curseStyle;
        }
        set
        {
            _curseStyle = value ?? new SpecialCardStyleSpec();
            // 诅咒去掉「自定义颜色」：老存档里存过的 custom 一律忽略
            //（生成时退回本体诅咒卡框；界面下拉显示「跟角色配色（默认）」；校验器会给一句提示）。
            _curseStyle.AllowsCustomColor = false;
        }
    }

    /// <summary>「诅咒 / 先古卡」页里**先古卡**的外观（卡牌边框颜色）。留空 = 跟角色的卡牌配色一样。</summary>
    public SpecialCardStyleSpec AncientStyle
    {
        get => _ancientStyle;
        set => _ancientStyle = value ?? new SpecialCardStyleSpec();
    }

    private SpecialCardStyleSpec _curseStyle = new();
    private SpecialCardStyleSpec _ancientStyle = new();

    /// <summary>
    /// 这个存档里**所有会生成出来的卡**（普通卡 + 诅咒 + 先古卡）。
    /// 生成 / 本地化 / 卡面素材 / 卡池 / 回读一律遍历它，别再各自去拼 <see cref="Cards"/>。
    /// </summary>
    [JsonIgnore]
    public IEnumerable<CardSpec> AllCards => Cards.Concat(Curses).Concat(AncientCards);

    public ObservableCollection<RelicSpec> Relics { get; set; } = new();
    public ObservableCollection<PotionSpec> Potions { get; set; } = new();

    /// <summary>
    /// 自定义参数（「自定义参数 / 组」页的上半部分，见 <see cref="ParamSpec"/>）。
    ///
    /// 存在的意义：卡面公式 / 锻造页「数值」模块里可以写 <c>=M*3</c>，
    /// 而 M 到底是「自己最大生命」还是「指定敌人的易伤层数」在这里定义 ——
    /// 这些值**打出牌的那一刻才知道**，所以在生成出来的模组里由表达式求值器现算。
    /// </summary>
    public ObservableCollection<ParamSpec> CustomParams { get; set; } = new();

    /// <summary>
    /// 「组」（「自定义参数 / 组」页的下半部分，见 <see cref="CardGroupSpec"/>）。
    /// 卡牌属性栏右边勾选进入组；组只在生成器里用于查看与「范围」筛选，不参与生成出来的模组代码。
    /// </summary>
    public ObservableCollection<CardGroupSpec> CardGroups { get; set; } = new();

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

    /// <summary>
    /// 锻造节点图的**端口版本**（老存档的兼容开关）。
    ///
    /// 0 / 1 = 老版：【if】是「1 个入口 + 3 个出口」（出口 0 = 条件插槽、1 = 是、2 = 否）；
    /// 2 = 新版（用户要求）：【if】的「条件」挪到**左边的入口**（入口 0 = 接入、1 = 条件），
    ///     右边只剩「是 / 否」（出口 0 / 1）。
    ///
    /// 为什么要版本号：端口挪位会改变**下标**的含义（老图的「是」在 1、新的在 0），
    /// 不迁移就会把「是 / 否」两支**悄悄对调**。加载存档时按这个号决定要不要
    /// <see cref="Generation.ForgeGraphGen.MigrateIfPorts"/> 一次。
    /// 缺这个字段（老 JSON）= 0 → 迁移。
    /// </summary>
    public int ForgeGraphPortVersion { get; set; }

    public ArtSpec Art { get; set; } = new();
    /// <summary>
    /// 逐帧动画（把动图拆成帧，替代战斗 / 篝火 / 商店那三处的静态立绘）。
    /// 详见 <c>docs\逐帧动画_吸收设计.md</c>：游戏引擎读不了 GIF，所以**生成时**就拆成 PNG 帧，
    /// 再写成 Godot 的 SpriteFrames 资源，配合生成的运行时 + Harmony 补丁在游戏里逐帧播。
    /// 总开关不勾 = 行为和以前完全一样。
    /// </summary>
    public AnimationSpec Animation { get; set; } = new();    /// <summary>卡牌配色（整个角色生效：边框 / 牌堆底色 / 能量描边）</summary>
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
    // ===== V0.2.132（《卡牌页优化.md》右侧④）=====
    // 「全部友方」这一档**要不要把召唤物排除在外**（默认 false = 老行为：同一边的玩家单位全算，
    // 召唤物也算友方）。只有 TargetSide == "AllAllies" 时它才有意义；默认档生成出来的代码与
    // V0.2.131 **逐字节相同**（见 CSharpCodeGen.AllyFilterSuffix）。
    private bool _allAlliesExcludePets;
    // 「不同效果的概率之间是否独立」：默认 false = 老行为（同一张牌 / 同一只遗物里勾了概率的效果
    // **共用一次掷骰**、各占一段区间，见 ChanceSlices）；true = 这条效果**自己掷一次**、不再占区间。
    // 默认档不写任何新标记 → 老存档生成文本不变。
    private bool _chanceIndependent;
    // 「生成卡牌 + 按范围随机」时：候选 = 这一组范围里的**全部卡**，玩家自己挑一张（不再随机抽候选）
    private bool _spawnChosen;
    private string? _powerId;
    private string? _spawnCardId;
    // 需求 ⑨：「获得药水」那一栏 —— 指定哪一瓶 / 随机一瓶 + 目标药水（类名，和 SpawnCardId 同构）
    private string _potionPick = "Fixed";
    private string? _potionId;
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
    /// 「转化为先古卡」效果（<c>AncientTranscend</c>）的**配对表**：每行 = 源组 → 换成哪些先古卡。
    ///
    /// 做成效果自己的字段而不是新开一个类别，是为了让「一条效果 = 一个触发时机里做的一件事」
    /// 这条约定继续成立（见 <see cref="AncientTranscendRow"/>）。
    /// </summary>
    private List<AncientTranscendRow> _ancientRows = new();

    /// <summary>配对表（见 <see cref="AncientTranscendRow"/>）。</summary>
    public List<AncientTranscendRow> AncientRows
    {
        get => _ancientRows;
        set => Set(ref _ancientRows, value ?? new List<AncientTranscendRow>());
    }

    /// <summary>
    /// 选牌类效果的**取几张**方式（需求原文：「如果我像实现消耗任意张手牌怎么办」+「选择所有手牌」）。
    ///
    /// 空串 = 恰好 N 张（老存档的行为，生成结果一个字符都不变）；
    /// <c>UpTo</c> = 最多 N 张（可以少选，本体「净化 Purity」那种：<c>new CardSelectorPrefs(prompt, 0, N)</c>）；
    /// <c>Any</c> = 任意张（上限给一个很大的数，卡面不写数字）；
    /// <c>All</c> = 全部（**不弹选牌界面**，直接对那一摞里符合条件的每一张动手 —— 本体「恶魔之焰」那种，
    /// 见 <see cref="Generation.CSharpCodeGen"/> 里各发射器的 All 分支）。
    /// </summary>
    private string _countMode = "";

    public string CountMode
    {
        get => _countMode;
        set
        {
            if (!Set(ref _countMode, value ?? "")) return;
            // 换档要当场刷新：列表行摘要（「消耗 所有手牌」）、以及「要不要声明 Cards 变量」那两条判据
            Raise(nameof(CountIsAny));
            Raise(nameof(CountIsAll));
            Raise(nameof(CountHasNoNumber));
            Raise(nameof(Display));
        }
    }

    /// <summary>这条效果要不要显示「取几张」那一栏（只有选牌类效果有意义）。</summary>
    [JsonIgnore]
    public bool UsesCountMode => Kind is "ExhaustCard" or "DiscardCard" or "UpgradeCard"
        or "TakeFromDraw" or "TakeFromDiscard" or "TakeFromExhaust";

    /// <summary>
    /// 界面 XAML 直接绑的「取几张」那一行（V0.2.150）。
    ///
    /// <para>为什么和 <see cref="UsesCountMode"/> 不是同一个判据：那个旧的判据只管 6 种，
    /// 可是机械测量（见 <see cref="EffectFieldPolicy"/>）说明「变化 / 复制 / 重放 / 回合结束自动打出 /
    /// 放置卡牌 / 给予关键词 / 三种全局牌组」也**真的会读**它 —— 以前这些种类上这一栏被藏起来了
    /// （填不了「最多 N 张 / 任意张」）。现在界面按测量结果摆。</para>
    ///
    /// <para>⚠ V0.2.213（用户报的「变化卡牌没有取几张这个选项」）：以前锻造链（<c>IsForgeChain</c>）
    /// 走的是那份**旧的 6 种清单**，于是锻造页上「变化卡牌 / 复制 / 重放 / 放置卡牌 / 给予关键词 /
    /// 三种全局」都看不到这一栏 —— 可锻造链的生成端读的就是同一个 <c>CountMode</c>
    /// （<c>ForgeGraphGen.CountModeMarker</c> + 共用的 <c>SelectPrefsExpr</c>）。现在两条链按同一份
    /// 判据摆（<see cref="EffectFieldPolicy.UsesCountMode"/>），不会再出现「锻造页没得填」。 </para>
    /// </summary>
    [JsonIgnore]
    public bool ShowCountModeRow => EffectFieldPolicy.UsesCountMode(Kind);

    /// <summary>「任意张」：不声明 Cards 变量（没有数字可显示，升级增量也没有落点）。</summary>
    [JsonIgnore]
    public bool CountIsAny => ShowCountModeRow && CountMode == "Any";

    /// <summary>
    /// 「全部」（V0.2.163）：**不弹选牌界面**，直接对那一摞里符合条件的每一张动手
    /// （本体「恶魔之焰」消耗所有手牌那种写法）。卡面写「所有」、不写数字，所以和「任意张」一样
    /// 不声明 Cards 变量、升级增量也没有落点。
    /// </summary>
    [JsonIgnore]
    public bool CountIsAll => ShowCountModeRow && CountMode == "All";

    /// <summary>「任意张 / 全部」都没有一个固定的张数数字（两处共用这一条判据）。</summary>
    [JsonIgnore]
    public bool CountHasNoNumber => CountIsAny || CountIsAll;

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
                // 换效果种类会让「这条效果做的是不是宠物」跟着变 —— 界面上的「召唤物（哪一只）」下拉、
                // 列表里的中文名、牺牲伙伴的「倍率」栏都按它显示，不通知的话界面会停在旧状态。
                Raise(nameof(PetAction));
                // 「转化为先古卡」的配对表按它显示（换个效果种类就该收起来）
                Raise(nameof(IsAncientTranscend));
                Raise(nameof(IsAncientRelicSwap));
                // V0.2.255：「清除指定状态」里的「或者清空一个组」那一行，换效果种类时也要跟着出现 / 收起
                Raise(nameof(UsesPowerGroup));
                // V0.2.171：换种类时把这一种类**用不到**的「命中次数 / 对群数」清掉 —— 那一栏已经收起，
                // 留着只会在生成时被拦住（用户实测：换到「播放音频文件」后，看不见的「命中次数公式 Y」把生成挡了）。
                ClearStaleRepeatFields();
                // 「取几张」那一栏按效果种类显示；「任意张 / 全部」还会影响要不要声明 Cards 变量
                Raise(nameof(UsesCountMode));
                Raise(nameof(CountIsAny));
                Raise(nameof(CountIsAll));
                Raise(nameof(CountHasNoNumber));
                Raise(nameof(IsPetEffect));
                Raise(nameof(PetKindZh));
                Raise(nameof(PetSacrificeUsesMultiplier));
                Raise(nameof(PetSacrificeFormulaZh));
                Raise(nameof(IsPetSacrifice));   // 「收益 / 公式 / 倍率」三行的显隐绑的就是它
                Raise(nameof(IsPetSacrificePower));   // 「状态（牺牲后获得）」那一行的显隐绑的就是它
                Raise(nameof(IsTempPower));           // 「临时增益什么时候撤」那一行的显隐绑的就是它
                Raise(nameof(IsTempPowerNextTurn));
                Raise(nameof(IsGiveKeyword));    // 「给予关键词 / 是否为临时关键词」两行的显隐绑的就是它
                Raise(nameof(IsBoostCard));      // 「强化什么（伤害 / 格挡）」那一行的显隐绑的就是它
                // 「生成 / 变化」那几行（取卡方式 / 范围限定 / 四个附加处理勾选框）的显隐绑的是这两个 ——
                // 不通知的话：把「效果种类」改成「生成卡牌」时那几行**不会出现**（用户报过「找不到在哪限定范围」）。
                Raise(nameof(UsesSpawnOptions));
                Raise(nameof(ShowSpawnFilter));
                Raise(nameof(ShowSpawnPickHint));
                Raise(nameof(ShowSpawnChoice));   // 「多选1」那一行的显隐绑的就是它
                Raise(nameof(ShowCopies));        // 「复制的份数」那一行的显隐绑的就是它
                Raise(nameof(ShowReplayTimes));   // 「重放次数」那一行的显隐绑的就是它
                Raise(nameof(ShowTurnEndPlay));   // 「回合结束时自动打出」那几行的显隐绑的就是它
                Raise(nameof(IsTurnEndPlay));
                Raise(nameof(ShowTurnEndPlay));   // 「回合结束时自动打出」那几行的显隐绑的就是它
                // 「范围界定」（需求 ③：选牌类效果限定只对哪一类牌 / 哪一组牌动手）：
                // 这一行的显隐绑的就是这两个 —— 和上面那几行同一条规矩，只 Raise 一次。
                Raise(nameof(UseCardScopeFilter));
                Raise(nameof(ShowCardScopeFilter));
                Raise(nameof(HasCardScope));
                // V0.2.213：「变化哪些牌（范围 / 指定卡）」那两行的显隐（只有「变化卡牌」用得到）——
                // 换效果种类时也要当场出现 / 收起，漏了就是老坑「换成这个种类了那一行却不出来」。
                Raise(nameof(UsesTransformPick));
                Raise(nameof(HasTransformPick));
                Raise(nameof(HasScopeCardId));
                Raise(nameof(HasTransformSourceFilter));
                // V0.2.214：「只对这张卡（指定）」那一行（丢弃 / 消耗 / 升级 / 复制 / 重放 / 给关键词 /
                // 从三摞拿牌 / 预见）—— 换效果种类时也要当场出现 / 收起。
                Raise(nameof(UsesScopeCardId));
                Raise(nameof(UsesScopeCardFilterRow));
                Raise(nameof(HasCardSourceFilter));
                Raise(nameof(ScopeCardListText));
                Raise(nameof(IsSlowPower));
                // V0.2.150：按种类裁剪的那几行（作用对象 / 增益减益 / 目标卡 / 生成到 / 选牌方式 /
                // 升级增量 / 取几张 / 取卡方式 / 下回合生效）—— 换种类时都要当场出现或收起，
                // 少 Raise 一个就会「换成这个种类了那一行却不出来」（用户报过这类 bug）。
                Raise(nameof(ShowTargetSideRow));
                // V0.2.306：「作用对象」的候选也按种类走（能量 / 金币只给 自己 / 指定队友 / 全部友方）
                Raise(nameof(TargetSideChoices));
                Raise(nameof(ShowPowerRow));
                Raise(nameof(ShowTargetCardRow));
                Raise(nameof(ShowSpawnToRow));
                Raise(nameof(ShowCardPickRow));
                Raise(nameof(ShowUpgradeAmountRow));
                Raise(nameof(ShowCountModeRow));
                Raise(nameof(CountIsAny));
                Raise(nameof(CountIsAll));
                Raise(nameof(CountHasNoNumber));
                Raise(nameof(ShowSpawnPickRow));
                // V0.2.220：「为抽到的牌添加关键词 / 临时关键词」那两行（抽牌 / 拿牌 / 填满手牌）——
                // 换效果种类时也要当场出现 / 收起。
                Raise(nameof(ShowDrawnKeywordRow));
                Raise(nameof(ShowDrawnKeywordTempRow));
                Raise(nameof(HasDrawnKeyword));
                Raise(nameof(DrawnKeywordZh));
                // V0.2.154：「音效」那一行（播放音效）—— 换种类要当场出现 / 收起
                Raise(nameof(IsPlaySfx));
                Raise(nameof(SfxZh));
                // V0.2.155：「音频文件」那一行（播放上传的音频）
                Raise(nameof(IsPlaySoundFile));
                // V0.2.232：音频底下那个「会被下一个自定义音频顶替」的勾也是按种类显隐的 ——
                // 不通知的话把效果种类改成「播放音频文件」时**它不会出现**（用户实测报的「没看到这个」）。
                Raise(nameof(ShowSfxReplaceRow));
                Raise(nameof(ShowSfxSameGroupRow));
                // 「刮削」那两行（条件 / 操作）的显隐绑的就是它 —— 换成别的效果种类时要跟着消失。
                Raise(nameof(ShowScrapeOptions));
                // 需求 ⑨：「获得药水」的「取药方式 / 目标药水」两行的显隐绑的就是这两个 ——
                // 不通知的话把「效果种类」改成「获得药水」时那两行**不会出现**。
                Raise(nameof(UsesPotionOptions));
                Raise(nameof(ShowPotionPicker));
                Raise(nameof(PotionNameZh));
                // 「数值公式」那一栏的显隐与说明文字都按效果种类走（换种类要立刻跟着变）
                Raise(nameof(ShowAmountFormula));
                // 固定数值框的显隐也按它走：支持公式的种类把「数值」框收起来（用户要求：只留公式）
                Raise(nameof(ShowAmountBox));
                Raise(nameof(AmountFormulaHint));
                Raise(nameof(UsesAmountFormula));
                // 次数类公式那几行的显隐（「命中次数 / 复制份数 / 重放次数」只在对应的种类下才有效）
                RaiseCountFormulaBoxes();
                // 「下回合生效」那一行按种类显隐（V0.2.132：卡牌链上它挪到 Grid 第 6 行，
                // 老控件那一份在 StackPanel 里 —— 两边的开关都要通知，换种类时才会当场出现 / 收起）。
                Raise(nameof(ShowNextTurn));
                Raise(nameof(ShowCardNextTurnRow));
                Raise(nameof(ShowPlainNextTurnRow));
                Raise(nameof(ShowCardRepeatRow));
                Raise(nameof(Display));
            }
        }
    }
    public decimal Amount
    {
        get => _amount;
        set
        {
            if (!Set(ref _amount, value)) return;
            // 「数值 0 = 这张牌自己」的那几种（给予关键词 / 回合结束时自动打出 / 丢弃卡牌）：
            // 数值一改，「范围界定」的**含义**（HasCardScope）跟着变 —— 但界面那几行**不再藏起来**
            //（藏了就没法填，用户实测报过「怎么不能填范围了」），只在「填了但这一档用不上」时给提示。
            Raise(nameof(CardScopeSelfOnly));
            Raise(nameof(ShowCardScopeFilter));
            Raise(nameof(HasCardScope));
            Raise(nameof(UsesSelectPile));
            Raise(nameof(DiscardSelfOnly));
            Raise(nameof(Display));
        }
    }
    /// <summary>升级后增量（只有卡牌用得到，遗物/药水不能升级）</summary>
    public decimal UpgradeAmount { get => _upgradeAmount; set => Set(ref _upgradeAmount, value); }

    /// <summary>
    /// **回读专用**：这条效果的「升级增量」是刚从打出时那个三元式
    /// <c>(base.IsUpgraded ? A : B)</c> 算出来的（<c>UpgradeAmount = A - B</c>，见
    /// <c>ProjectRecovery.FillExpr</c>）。
    ///
    /// <para><b>为什么需要它</b>（用户实测报的 BUG：「在生成器填负值的力量不生效」）：生成端对
    /// 「数值公式 + 升级增量」的效果会把**同一个增量写三遍** —— 变量构造第二个参数、打出时那个三元式、
    /// <c>OnUpgrade</c> 里的 <c>UpgradeValueBy(N)</c>。回读时前两处已经算出增量了，第三处是同一个数，
    /// 以前又 <c>+=</c> 加了一遍 → 增量算重一倍（1 变 2），再生成出来就是
    /// <c>(base.IsUpgraded ? 3m : 1m)</c>；升级增量正好把负数抵成 0 时（<c>-2 + 2 = 0</c>）
    /// 那张升级过的牌打出去就完全没效果。</para>
    ///
    /// <para>⚠ 它**只在一次回读过程里有意义**、且 <c>[JsonIgnore]</c>：不写进存档、不参与「生成 → 回读」
    /// 的比对（写了会让同一份配置多出一个字段，逐字节对拍就不是「逐字不变」了）。</para>
    /// </summary>
    [JsonIgnore]
    public bool UpgradeFromTernary { get; set; }

    // ==================== 数值公式（需求原文：「另加一栏『数值公式』」） ====================
    //
    // 想解决的事：数值栏只能填一个死的数字，而用户想要的是「打出这张牌那一刻现算」——
    // 例：造成「自己最大生命 × 3」点伤害、给目标施加「它身上易伤层数」层中毒。
    // 这类值在**生成的时候**根本不知道（要等玩家在战斗里打出这张牌），所以只能生成一个表达式，
    // 让游戏在打出时求值 —— 复用锻造页【数值】模块那一套（ForgeFormula 语法 + 自定义参数 + X/Y）。
    //
    // 为什么只有 5 种效果支持（伤害 / 格挡 / 失去生命 / 回复生命 / 施加增益减益）：
    // 这 5 种的数值是**打出时才读**的（读的是本体的 DynamicVar，生成时只写一句命令）。
    // 其余种类的数值必须在生成时就定下来 —— 例如「抽几张」「生成几张」「消耗几张」要提前分配
    // 动态变量、算好选牌上限（CardSelectorPrefs 里那个数字），塞不进运行时表达式。
    // 遗物 / 药水 / 自定义状态 / 诅咒这一版也不做：它们的调用链没有「打出这张牌」的上下文
    //（校验器会拦住并说清原因）。
    private string _amountFormula = "";

    /// <summary>
    /// 数值公式：<b>空 = 用上面的固定数值</b>（老存档一个字符都不变）；
    /// 写了 = 打出这张牌的那一刻现算，结果取代固定数值。
    ///
    /// 语法和锻造页【数值】模块完全一样：数字、参数名（M / N…，在「自定义参数 / 组」页定义）、
    /// <c>X</c>（当前能量）、<c>Y</c>（当前额外资源量）、<c>+ - * / % ( )</c> 与
    /// <c>min / max / floor / ceil / round / abs</c>，最前面可以写一个 <c>=</c>。
    /// </summary>
    public string AmountFormula
    {
        get => _amountFormula;
        set
        {
            if (!Set(ref _amountFormula, value ?? "")) return;
            Raise(nameof(UsesAmountFormula));
            Raise(nameof(ShowAmountFormula));
            Raise(nameof(AmountFormulaHint));
            Raise(nameof(Display));
        }
    }

    /// <summary>这个效果种类能不能用「数值公式」（见上面那段注释里为什么只有这 5 种）。</summary>
    public static bool FormulaSupports(string? kind) =>
        kind is "Damage" or "Block" or "HpLoss" or "Heal" or "ApplyPower"
        or "Gold";   // V0.2.281（用户问「金币怎么不能填公式」）：获得金币也支持「数值公式」

    /// <summary>
    /// 公式的「显示 / 解析」统一写法：去掉首尾空格、去掉用户习惯写在最前面的那个 <c>=</c>。
    /// 解析（<see cref="Generation.ForgeFormula.TryParse"/>）与卡面 / 界面显示都必须走它 ——
    /// 两处各写一遍的话，界面上看到的和实际算的会不一样。
    /// </summary>
    public static string FormulaText(string? formula)
    {
        string s = (formula ?? "").Trim();
        return s.StartsWith("=", StringComparison.Ordinal) ? s.Substring(1).Trim() : s;
    }

    /// <summary>这条公式在界面 / 卡面上的写法（例：<c>M*3</c>）。</summary>
    [JsonIgnore]
    public string AmountFormulaText => FormulaText(_amountFormula);

    /// <summary>
    /// 这条效果是不是「用数值公式取代了固定数值」。
    /// 生成端只认这一个判据（写公式 ≠ 支持公式，不支持的那些照旧用固定数值）。
    /// </summary>
    [JsonIgnore]
    public bool UsesAmountFormula =>
        FormulaSupports(Kind) && !string.IsNullOrWhiteSpace(_amountFormula);

    /// <summary>
    /// 「数值公式」那一栏要不要显示（不支持完整公式的种类就不摆一个填了会报错的框）。
    ///
    /// <para>⚠ V0.2.131：卡牌链上**所有**效果种类都显示这一栏 —— 用户要求去掉「数值 = X」那个勾选框，
    /// X 改在这个框里写。除了原来那 5 种（完整公式）以外，其余种类**只认光秃秃的 `X`**
    ///（= 原来的「数值 = X」，见 <see cref="AmountIsXEffective"/>），写别的公式由校验器拦住并说清原因。</para>
    /// </summary>
    [JsonIgnore]
    public bool ShowAmountFormula => FormulaSupports(Kind) || IsCardChain;


    /// <summary>
    /// 界面用：「数值」那个固定数值框还要不要显示。
    ///
    /// 用户要求（V0.2.112）：「效果那边只要保留公式就行，去掉数值框，反正数值的功能公式都有并且更多」。
    /// 但公式**目前只做在卡牌上**（遗物 / 药水 / 状态那几条链没有「打出这张牌」那一刻的上下文，
    /// 校验器会拦住），所以只有「**卡牌上的那 5 种**」（伤害 / 格挡 / 失去生命 / 回复生命 / 施加状态）
    /// 才把「数值」框收起来：公式**留空 = 0**（这条效果不显示、也不执行）。
    /// 其余情况（别的效果种类、以及遗物 / 药水 / 状态上的效果）照旧保留「数值」框 ——
    /// 那些地方没有公式可用，收起来就没法填了。
    /// 老存档里卡牌上那 5 种的固定数值会在**打开存档时自动转成公式**（见 <c>ProfileFactory.Load</c>）。
    /// </summary>
    [JsonIgnore]
    public bool ShowAmountBox => !(FormulaSupports(Kind) && IsCardChain);

    private bool _isCardChain;

    /// <summary>
    /// 这条效果是不是正在**卡牌**编辑链里显示（由界面在切换卡牌效果时设置；不写进存档）。
    /// 只有卡牌链上公式才真的可用，所以「数值」框的显隐要看它。
    /// </summary>
    [JsonIgnore]
    public bool IsCardChain
    {
        get => _isCardChain;
        set
        {
            if (_isCardChain == value) return;
            _isCardChain = value;
            Raise(nameof(IsCardChain));
            Raise(nameof(ShowAmountBox));
            // V0.2.132：卡牌链专用那几行（概率 / 下回合生效 / 命中次数·生效次数公式）也按它显隐 ——
            // 不通知的话「在列表里点一条效果」时那几行不会当场出现（老坑：V0.2.107 的触发时机）。
            RaiseCardChainRows();
        }
    }

    /// <summary>卡牌链专用那几行 + 让位给它们的老控件，全部通知一遍（见 App.xaml 的 EffectCommon）。</summary>
    private void RaiseCardChainRows()
    {
        Raise(nameof(ShowCardChanceRow));
        Raise(nameof(ShowCardNextTurnRow));
        Raise(nameof(ShowPlainChanceRow));
        Raise(nameof(ShowPlainNextTurnRow));
        Raise(nameof(ShowCardRepeatRow));
        Raise(nameof(ShowPlainFixedRepeatBox));
        Raise(nameof(ShowCardTimesRow));
        Raise(nameof(ShowPlainFixedTimesBox));
    }

    // ===== V0.2.144：`EffectSpec.IsExpanded`（V0.2.137 那个「折叠行」的纯界面标记）**已删除** =====
    //
    // 用户原话「遗物药水右侧的效果栏能不能同步卡牌的」→ 三页右侧改成卡牌页那种
    // 「上面一行摘要列表 + 下面一整块共享编辑区」，折叠 / 展开这套交互不存在了，
    // 所以那个 [JsonIgnore] 标记也没有存在意义（删掉它不影响任何存档：它本来就不写进 JSON，
    // 生成端也从来没读过它）。

    // ==================================================================================
    //  锻造【效果】模块（V0.2.129）：和「卡牌」页共用同一套 EffectSpec 编辑器
    // ==================================================================================
    //
    // 用户拍板（《已拍板决定.md》Q1=A）：锻造【效果】模块**直接复用**卡牌那套 EffectSpec 编辑器，
    // 不再维护 `ForgeNodeField` 那套平行实现。于是：
    //   · 【效果】模块上的设置 = 一个 EffectSpec（见 ForgeNodeSpec.EffectSpec）；
    //   · 界面就是卡牌那份模板（`EffectCommon`），**出现的可填写项自然和卡牌页一致**；
    //   · 生成端仍然读那块模块的**老字段**（`Fields`）——由 `ForgeEffectBridge` 在两边之间搭桥：
    //     老存档（只有 Fields）→ 按老字段生成，**一个字节都不变**；
    //     用新编辑器改过的（有 EffectSpec）→ 把 spec 回写成 Fields 再生成。
    //
    // 下面这几项就是这条桥要用的：
    //   · IsForgeChain：这条效果**现在显示在锻造【效果】模块里**（由界面设置，不写进存档）——
    //     只有它才会让「数值 / 生效次数 / 命中次数 / 概率」换成**能填公式的文本框**；
    //     卡牌 / 遗物 / 药水 / 自定义状态四处一个字都不变（见 App.xaml 里那几行的 Visibility）。
    //   · ForgeAmountText：锻造图里「数值」那一栏的**原文**（锻造图**所有**种类都能写公式；
    //     卡牌那 5 种走 AmountFormula，其余种类没地方放公式，所以单独存一份原文）。
    //   · TargetSideCustom：「作用对象」这一栏被用户在锻造页上**改过**（老存档是 false =
    //     照老规矩由【对象】模块 / 这张牌选中的目标决定，生成文本逐字不变）。
    // ==================================================================================

    private bool _isForgeChain;

    /// <summary>这条效果是不是正在**锻造【效果】模块**里显示（由界面设置；不写进存档）。</summary>
    [JsonIgnore]
    public bool IsForgeChain
    {
        get => _isForgeChain;
        set
        {
            if (_isForgeChain == value) return;
            _isForgeChain = value;
            Raise(nameof(ShowForgeValueRows));
            Raise(nameof(ShowCardValueRows));
            Raise(nameof(ShowAmountBoxNormal));
            Raise(nameof(ShowAmountFormulaNormal));
            Raise(nameof(ShowFixedTimesBox));
            Raise(nameof(ShowForgeTimesBox));
            Raise(nameof(ShowFixedRepeatBox));
            Raise(nameof(ShowForgeRepeatBox));
            Raise(nameof(ShowChanceRowNormal));
            Raise(nameof(ShowChanceRowForge));
            RaiseCardChainRows();
        }
    }

    /// <summary>锻造链上「数值 / 生效次数 / 命中次数 / 概率」换成能填公式的文本框（见 App.xaml）。</summary>
    [JsonIgnore] public bool ShowForgeValueRows => IsForgeChain;
    /// <summary>卡牌 / 遗物 / 药水 / 自定义状态四处照旧（这一条恒为 !IsForgeChain，外观与绑定都不变）。</summary>
    [JsonIgnore] public bool ShowCardValueRows => !IsForgeChain;
    /// <summary>「数值」那个**固定数值框**（卡牌链上那 5 种会收起来；锻造链一律不显示它，用下面的文本框）。</summary>
    [JsonIgnore] public bool ShowAmountBoxNormal => ShowAmountBox && !IsForgeChain;
    /// <summary>「数值（公式）」那一栏（锻造链不显示：锻造那个「数值」文本框本身就能写公式）。</summary>
    [JsonIgnore] public bool ShowAmountFormulaNormal => ShowAmountFormula && !IsForgeChain;
    /// <summary>固定「生效次数」框（int）。</summary>
    [JsonIgnore] public bool ShowFixedTimesBox => !IsForgeChain;
    /// <summary>锻造链的「生效次数」文本框（能写公式 / 参数）。</summary>
    [JsonIgnore] public bool ShowForgeTimesBox => IsForgeChain;
    /// <summary>固定「对群数 / 命中次数」框（int，按种类收起）。</summary>
    [JsonIgnore] public bool ShowFixedRepeatBox => ShowRepeatCountInput && !IsForgeChain;
    /// <summary>锻造链的「命中次数」文本框（能写公式 / 参数；锻造图里一直摆着，因为本体好几处会读它）。</summary>
    [JsonIgnore] public bool ShowForgeRepeatBox => IsForgeChain;
    /// <summary>「概率生效」勾选框 + 百分比那两件（锻造链不显示：锻造那个「概率」文本框自带公式）。</summary>
    [JsonIgnore] public bool ShowChanceRowNormal => !IsForgeChain;
    /// <summary>锻造链的「概率生效（%）」文本框（100 = 必定，能写公式）。</summary>
    [JsonIgnore] public bool ShowChanceRowForge => IsForgeChain;

    // ===== V0.2.132（《卡牌页优化.md》右侧④：卡牌链的效果编辑器重排）=====
    //
    // 需求给的顺序：升级增量 → 效果种类 → 数值 → 作用对象 → 命中次数 → 生效次数 → 概率 → 下回合生效
    // →（其余按种类只留用得到的）。「概率」和「下回合生效」原来在 App.xaml 那个「按条件出现的行」
    // StackPanel 里（在种类专属项**下面**），现在给卡牌链单独摆到 Grid 的第 5、6 行（正好在
    // 「生效次数」下面、种类专属项上面）；**遗物 / 药水 / 自定义状态 / 锻造链一个字都不动** ——
    // 它们继续用 StackPanel 里那两行老控件，新摆的那两行对它们恒为 Collapsed（见下面这些开关）。
    //
    // 为什么要「老控件 + 新控件」两份：WPF 里一个控件不能同时出现在两个位置。同一份绑定复制一份，
    // 靠 IsCardChain 二选一显示 —— 于是四处老链的控件树、绑定路径、行位置全都是原样。

    /// <summary>卡牌链专用的「概率」行（Graph 第 5 行：勾选框 + 百分比 + 概率公式 + 「是否独立」）。</summary>
    [JsonIgnore] public bool ShowCardChanceRow => IsCardChain;
    /// <summary>卡牌链专用的「下回合生效」行（Graph 第 6 行）。</summary>
    [JsonIgnore] public bool ShowCardNextTurnRow => IsCardChain && ShowNextTurn;
    /// <summary>老「概率」行（StackPanel 里那一份）—— 卡牌链之外照旧显示，卡牌链上让位给上面那一行。</summary>
    [JsonIgnore] public bool ShowPlainChanceRow => ShowChanceRowNormal && !IsCardChain;
    /// <summary>老「下回合生效」勾选框 —— 同上，卡牌链上让位给第 6 行那一份。</summary>
    [JsonIgnore] public bool ShowPlainNextTurnRow => ShowNextTurn && !IsCardChain;
    /// <summary>卡牌链上「命中次数」那一行里的**公式框**（和固定数字框并排）。</summary>
    [JsonIgnore] public bool ShowCardRepeatRow => IsCardChain && ShowRepeatCountInput;
    /// <summary>卡牌链上「命中次数」固定数字框（搬进上面那一行的横排里，老位置那份让位）。</summary>
    [JsonIgnore] public bool ShowPlainFixedRepeatBox => ShowFixedRepeatBox && !IsCardChain;
    /// <summary>卡牌链上「生效次数」那一行里的**公式框**（和固定数字框并排）。
    /// V0.2.150：那 2 种「挂在卡自己钩子上、没有重复落点」的效果不摆这一行（见 EffectFieldPolicy.UsesTimes）。</summary>
    [JsonIgnore] public bool ShowCardTimesRow => IsCardChain && EffectFieldPolicy.UsesTimes(Kind);
    /// <summary>卡牌链上「生效次数」固定数字框（搬进上面那一行的横排里，老位置那份让位）。</summary>
    [JsonIgnore] public bool ShowPlainFixedTimesBox => ShowFixedTimesBox && !IsCardChain;

    private string _forgeAmountText = "";

    /// <summary>
    /// 锻造【效果】模块「数值」那一栏的**原文**（空 = 用 <see cref="Amount" /> / <see cref="AmountFormula"/>）。
    ///
    /// 为什么单独一格：锻造图的数字框**所有种类**都能写公式（V0.2.128 需求 1，用户拍板 Q2=A），
    /// 而卡牌那份只对「伤害 / 格挡 / 失去生命 / 回复生命 / 施加状态」5 种认 <see cref="AmountFormula"/>
    ///（<see cref="UsesAmountFormula"/> 是卡牌链的判据，不能放宽 —— 那会动到卡牌生成）。
    /// 所以锻造链的原文存在这里，回写老字段时**一字不改**（老存档填的是 `6` 还是 `min(2,X)` 都原样落回去）。
    /// </summary>
    public string ForgeAmountText
    {
        get => _forgeAmountText;
        set
        {
            string next = value ?? "";
            if (!Set(ref _forgeAmountText, next)) return;
            // 顺带把 Amount / AmountFormula 也对齐（列表里的 Display / 卡面文案读它们；生成端只认 ForgeAmountText）
            string s = FormulaText(next);
            if (s.Length == 0) return;
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out decimal v)) { Amount = v; AmountFormula = ""; }
            else if (FormulaSupports(Kind)) AmountFormula = next;
        }
    }

    /// <summary>锻造链「数值」栏的显示文字（没填过原文就退回固定数值 / 卡牌那套公式）。</summary>
    [JsonIgnore]
    public string ForgeAmountTextShown => _forgeAmountText.Length > 0
        ? _forgeAmountText
        : UsesAmountFormula ? AmountFormulaText : Amount.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>锻造链「生效次数」文本框的文字（写了公式就是公式本身）。</summary>
    [JsonIgnore]
    public string ForgeTimesText
    {
        get => FormulaText(TimesFormula) is { Length: > 0 } f ? f : Times.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set => ApplyForgeCountText(value, TimesFormula, Times, v => Times = v, f => TimesFormula = f);
    }

    /// <summary>锻造链「命中次数」文本框的文字（= 卡牌那边的「对群数 / 命中次数」）。</summary>
    [JsonIgnore]
    public string ForgeHitsText
    {
        get => FormulaText(RepeatFormula) is { Length: > 0 } f ? f : RepeatCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set => ApplyForgeCountText(value, RepeatFormula, RepeatCount, v => RepeatCount = v, f => RepeatFormula = f);
    }

    /// <summary>锻造链「概率生效（%）」文本框的文字（100 = 必定；写了公式就现算）。</summary>
    [JsonIgnore]
    public string ForgeChanceText
    {
        get => FormulaText(ChanceFormula) is { Length: > 0 } f ? f
            : (!ChanceEnabled ? "100" : ChancePercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        set
        {
            string text = FormulaText(value ?? "");
            if (decimal.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out decimal v))
            {
                ChanceFormula = "";
                ChancePercent = v;
                ChanceEnabled = v != 100m;
            }
            else
            {
                ChanceFormula = text;
                ChanceEnabled = true;
            }
            Raise(nameof(ForgeChanceText));
        }
    }

    /// <summary>锻造链数字框的公共写法：填得进一个数就写固定值、否则当成公式原文（两条路的生成语义一致）。</summary>
    private void ApplyForgeCountText(string? text, string oldFormula, int oldValue, Action<int> setValue, Action<string> setFormula)
    {
        string s = FormulaText(text ?? "");
        if (decimal.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out decimal v))
        {
            setFormula("");
            setValue(Math.Max(1, (int)Math.Round(v)));
        }
        else
        {
            setValue(oldValue);
            setFormula(s);
        }
        Raise(nameof(ForgeTimesText));
        Raise(nameof(ForgeHitsText));
        Raise(nameof(Display));
    }

    private bool _targetSideCustom;

    /// <summary>
    /// 「作用对象」这一栏**被用户在锻造页上改过**（老存档 = false）。
    ///
    /// 为什么需要这个标记：锻造图里「这条效果打谁」本来是由【对象】模块（接到【效果】左边的
    /// 「指向对象」口）决定的，链上没有就按老规矩用「这张牌选中的目标」。新编辑器把卡牌那栏
    /// 「作用对象」也摆出来了 —— 只有用户**真的动过它**，才让这一栏接管目标（老存档的生成文本
    /// 因此逐字不变）。没动过 = 照老路走（见 ForgeGraphGen.EmitEffect）。
    /// </summary>
    public bool TargetSideCustom { get => _targetSideCustom; set => Set(ref _targetSideCustom, value); }

    /// <summary>锻造【效果】模块里那几栏的说明（界面提示用）。</summary>
    [JsonIgnore]
    public string ForgeEffectHint =>
        "这一块和「卡牌」页的效果编辑器是**同一套**（同一份模板、同一份数据）：效果种类底下出现的可填写项完全一致。"
        + "差别只有三处，都是锻造图自己的规矩：①「数值 / 生效次数 / 命中次数 / 概率」可以直接写公式或参数"
        + "（X = 当前能量，Y = 当前额外资源量，内置只读量见「参数参考」窗）；"
        + "②「作用对象」默认由【对象】模块决定（接到【效果】左边那个「指向对象」口），你在这里改过之后以这里为准；"
        + "③【效果】模块上没接的种类 / 栏位会在「配置检查」里被**报错拦住**（不会静默生成一半）。";

    /// <summary>「数值公式」那一栏底下的说明文字。</summary>
    [JsonIgnore]
    public string AmountFormulaHint
    {
        get
        {
            // V0.2.297（用户要求）：公式**实时**算出结果，就写在这一行小字最前面。
            string __preview = Generation.ForgeFormula.Preview(_amountFormula, Generation.PercentPowers.Current);
            string __prefix = __preview.Length == 0 ? "" : __preview + "\n";

            if (!FormulaSupports(Kind))
                return __prefix + $"「{EffectCatalog.FindKind(Kind).Display}」的数值要在生成的时候就得定下来"
                     + "（抽几张 / 生成几张这类要提前分配变量和选牌上限），所以这一栏**只认两种写法**："
                     + "① 一个**纯数字 / 能折成常数的式子**（例：1、2+3）—— 它就是左边的固定数值（工具会自动折好）；"
                     + "② 光秃秃一个 X = 原来的「数值 = X」那个勾选框（打出这张牌那一刻消耗掉的能量，费用框里也要填 X）。"
                     + "想写完整公式（M*3 / max(1, N/2) / Turn*2…）只有这 5 种："
                     + "造成伤害 / 获得格挡 / 失去生命 / 回复生命 / 施加增益减益。";
            // V0.2.164：这三条链也能写公式了 —— 口径和卡牌不同，说清楚（见 ProfileValidator 的同名提示）
            string chain = IsCardChain
                ? "留空 = 0（这条效果就不显示、也不执行）；写了 = 打出这张牌时才求值，卡面上显示算出来的数字。"
                : IsForgeChain
                    ? "留空 = 用左边的固定数值；写了 = 每次触发时现算。"
                    : "留空 = 用左边的固定数值；写了 = 这条效果触发 / 使用时现算"
                      + "（遗物 / 药水悬停时显示的数字也跟着现算；自定义状态的说明里会直接写公式本身）。"
                      + "⚠ 这几条链上没有「打出这张牌」那一刻：X / Y 读的是**当前**能量 / 额外资源量，"
                      + "「指定敌人」来源的参数取的是**可打的第一个敌人**。";
            return __prefix + chain
                 + "可用参数名（M / N…，「自定义参数 / 组」页里定义的那几个）、X = 当前能量、Y = 当前额外资源量；"
                 // V0.2.182：以前这里把 80 条清单**整串**拼进提示文案（那一大片就是用户说的「占面积」）。
                 // 现在只点几个代表 + 指向参考窗（清单只在 ParamReferenceWindow 里看）——
                 // 留几个名字是因为自检要确认「提示里确实提到过内置量」，用户也一眼知道有这东西。
                 + $"还内置了 {Generation.FormulaBuiltins.All.Count} 条**实时只读量**（不用定义，直接写；"
                 + "例：Turn / HandCount / DiscardedThisTurn / PowersOnMe…）—— **完整清单见启动时自动弹出的「参数参考」窗**。"
                 + "支持 + - * / ( ) 和 min / max / floor / ceil / round / abs。例：M*3、max(1, N/2)、Turn*2。";
        }
    }

    // ===== 生成 / 变化卡牌：范围限定 + 「生成出来的卡」的附加处理 =====
    private string _spawnPick = "Fixed";
    private string _spawnFilter = "";
    // 「多选1（候选张数）」：1 = 直接生成；> 1 = 抽这么多张候选让玩家挑 1 张
    private decimal _spawnChoice = 1m;
    private bool _spawnUpgraded, _spawnFree, _spawnFreeThisTurn, _spawnUpgradedThisTurn;
    // 需求 ⑦：「生成 / 变化出来的卡带上关键词」。空 = 不带（默认档，老存档就是它）
    private string _spawnKeyword = "";

    /// <summary>
    /// 生成 / 变化时是「指定卡」还是「按范围随机」：
    /// <c>Fixed</c>（默认，老存档就是它）= 用「目标卡」那一栏选定的卡；
    /// <c>Random</c> = 从卡池里按 <see cref="SpawnFilter"/> 随机抽（本体「炼制药水 / 发现」那种）。
    /// </summary>
    public string SpawnPick
    {
        get => _spawnPick;
        set
        {
            if (Set(ref _spawnPick, value ?? "Fixed"))
            {
                Raise(nameof(IsSpawnRandom));
                Raise(nameof(ShowSpawnFilter));   // 「范围限定」那一行的显隐绑的是它（不通知就不会出现）
                Raise(nameof(ShowSpawnPickHint));
                Raise(nameof(ShowSpawnChoice));   // 「多选1」只在「按范围随机」时出现
                Raise(nameof(Display));
            }
        }
    }

    /// <summary>是不是「按范围随机」（界面上后面那几行要不要显示也跟着它）。</summary>
    [JsonIgnore]
    public bool IsSpawnRandom => string.Equals(_spawnPick, "Random", StringComparison.Ordinal);

    /// <summary>
    /// 随机生成 / 变化的**范围限定**：空 = 不限；Attack / Skill / Power = 攻击 / 技能 / 能力；
    /// Curse / Status = 诅咒 / 状态（按稀有度 / 类型从对应卡池里挑）。
    /// </summary>
    public string SpawnFilter
    {
        get => _spawnFilter;
        set { if (Set(ref _spawnFilter, value ?? "")) { Raise(nameof(SpawnFilterZh)); Raise(nameof(HasCardScope)); Raise(nameof(Display)); } }
    }

    /// <summary>范围限定的中文（界面 / 描述里用）。</summary>
    [JsonIgnore]
    public string SpawnFilterZh => EffectCatalog.SpawnFilterZh(SpawnFilter);

    // ===== 选牌类效果的「范围界定」（需求 ③）=====
    // 需求原文：「所有可选牌的效果种类都增加范围界定（与生成 / 变化卡牌一致）」。
    // 做法（见 docs\选牌类效果范围界定_设计.md）：**复用同一个 SpawnFilter 字段和同一份选项表**
    // （EffectCatalog.SpawnFiltersFor：不限 / 攻击 / 技能 / 能力 / 诅咒 / 状态 / 每个自定义关键词一组），
    // 不新建 CardScopeFilter —— 这样界面上的候选、文案、校验、自定义关键词分组的逻辑全都只有一份。
    //
    // 这 11 个种类就是「从某一摞牌里挑牌 / 对牌动手」的那些。
    // **故意不含 TransformCard（变化卡牌）**：它的 SpawnFilter 已经有含义了 —— 那是
    // 「变出来的新卡从哪个范围里抽」（见 CSharpCodeGen.SpawnPoolExpr / __transformPool）。
    // 同一个字段再拿去限定「挑哪几张牌去变」会变成一句话两个意思，而且会让**老存档**的
    // 「变化卡牌 + 按范围随机 + 范围=攻击」在更新后突然只能挑攻击牌来变（静默变行为）。
    // 「变化卡牌」本来就是需求里说的「与生成 / 变化卡牌一致」的那个**参照物**，它早就有范围限定了。
    // =====

    /// <summary>这个效果种类支不支持「范围界定」（选牌类效果：只对某一类牌 / 某一组牌动手）。</summary>
    [JsonIgnore]
    public bool UseCardScopeFilter => EffectFieldPolicy.UsesCardScopeFilter(Kind);

    // ==================== V0.2.150：按种类裁剪「常显栏位」 ====================
    // 用户：「卡牌页右边的效果栏做的太差了，像造成伤害这个效果，只需要数值（公式）/ 升级增量 /
    // 作用对象 / 命中次数 / 生效次数 / 概率 / 下回合生效 / 随机目标允许攻击同个敌人，
    // 其他的都可以隐藏掉。其他每一条效果你都要问我每一个需要保留什么。」
    //
    // 下面这几行以前是**无条件显示**的（增益减益 / 目标卡 / 生成到 / 选牌方式 / 随机允许重复，
    // 以及所有种类都摆着的「作用对象」）—— 判据现在统一走 EffectFieldPolicy，
    // 那份清单是**机械测量**出来的（见那个类的注释），不是人拍的。
    // 锻造链（IsForgeChain）一律照旧显示：节点图那边的显隐是它自己那套，改卡牌页不动它。

    /// <summary>「作用对象」那一行要不要显示（只有生成端真的会读它的种类才摆）。</summary>
    [JsonIgnore]
    public bool ShowTargetSideRow => IsForgeChain || UsesTargetSideEffective;

    /// <summary>
    /// 这一条效果的「作用对象」到底有没有意义。
    ///
    /// <para>V0.2.200（用户要求「召唤物即死：收益的造成伤害能不能加个选择对象」）：
    /// 「牺牲伙伴」以前一律不摆这一栏 —— 收益是格挡 / 状态时确实没得选（都挂在自己身上），
    /// 但收益是<b>造成伤害</b>时打谁完全说得清，必须能选。所以判据 = 种类清单
    /// <see cref="EffectFieldPolicy.UsesTargetSide"/> ∪ 「牺牲伙伴 + 收益=伤害」。</para>
    ///
    /// <para>显隐（<see cref="ShowTargetSideRow"/>）、列表行尾那个「→ 谁」、
    /// 状态触发器的说明文字、生成端注释都共用这一个判据，别各写一套。</para>
    /// </summary>
    [JsonIgnore]
    public bool UsesTargetSideEffective => EffectFieldPolicy.UsesTargetSide(Kind)
        || (Kind == "PetSacrifice" && EffectFieldPolicy.UsesTargetSideForSacrifice(PetSacrificeGain));

    /// <summary>「牺牲伙伴」的收益选了「造成伤害」（只有这一档才有作用对象可挑）。</summary>
    [JsonIgnore]
    public bool IsPetSacrificeDamage =>
        Kind == "PetSacrifice" && EffectFieldPolicy.UsesTargetSideForSacrifice(PetSacrificeGain);

    /// <summary>
    /// 列表行尾那个「→ 谁」要不要写。
    /// 宠物类效果一律不写（打的是宠物，写「→ 自己」会让人以为在给自己加血）——
    /// 例外是「牺牲伙伴 → 造成伤害」：那一下真的打在敌人身上，必须写。
    /// </summary>
    [JsonIgnore]
    public bool ShowsTargetSideSuffix => UsesTargetSideEffective
        // V0.2.306：能量 / 金币的「作用对象」默认值仍然是 "Enemy"（那一栏以前对它们不显示）=
        // 「没填」，列表尾巴不许写成「→ 单体敌人」（V0.2.161 那条断言盯的就是这个）。
        // 只有真的选了队友那两档才写。
        && !(Kind is "Energy" or "Gold" && TargetSide is not ("Ally" or "AllAllies"))
        && (!PetAction || IsPetSacrificeDamage);

    /// <summary>「增益 / 减益」那一行的显隐（选哪个状态施加 / 翻倍 / 清除 / 暴露）。</summary>
    [JsonIgnore]
    public bool ShowPowerRow => IsForgeChain || EffectFieldPolicy.UsesPower(Kind);

    /// <summary>「目标卡（生成 / 变化 / 强化用）」那一行的显隐。</summary>
    [JsonIgnore]
    public bool ShowTargetCardRow => IsForgeChain
        || EffectFieldPolicy.UsesTargetCard(Kind, plainChain: !IsCardChain);

    /// <summary>「生成到（生成卡牌用）」那一行的显隐。</summary>
    [JsonIgnore]
    public bool ShowSpawnToRow => IsForgeChain || EffectFieldPolicy.UsesSpawnTo(Kind);

    /// <summary>「选牌方式（消耗 / 变化 / 丢弃用）」那一行的显隐。</summary>
    [JsonIgnore]
    public bool ShowCardPickRow => IsForgeChain || EffectFieldPolicy.UsesCardPick(Kind);

    /// <summary>
    /// 「升级增量」那一行的显隐（卡牌页专用）。
    /// 23 种效果生成端根本不读它（击晕 / 结束回合 / 获得药水 / 全局牌组那几种…），全都不摆。
    /// </summary>
    [JsonIgnore]
    public bool ShowUpgradeAmountRow => EffectFieldPolicy.UsesUpgradeAmount(Kind);


    /// <summary>
    /// <b>这条效果是不是「数值 = X」</b>：老勾选框 <see cref="AmountIsX"/>，或者「数值（公式）」框里
    /// 光秃秃写了一个 <c>X</c>（V0.2.131 用户要求去掉那个勾选框，X 改在公式框里写）。
    ///
    /// <para>⚠ 只对**那 5 种支持完整公式的种类以外**生效：那 5 种写 <c>X</c> 走的是既有的公式通道
    /// （<c>__X</c>，值一样但生成的是公式变量），不能同时按「= X」再走一遍 —— 否则同一条效果会被
    /// 两条路各处理一次。</para>
    /// </summary>
    [JsonIgnore]
    public bool AmountIsXEffective =>
        AmountIsX || (Generation.ForgeNumbers.IsBareX(AmountFormula) && !FormulaSupports(Kind));


    /// <summary>
    /// 「范围界定」没有意义的那一档：**数值 0 = 这张牌自己**（给予关键词 / 回合结束时自动打出），
    /// 这时候根本不会去牌堆里挑牌，范围界定自然也不该出现。
    /// </summary>
    [JsonIgnore]
    public bool CardScopeSelfOnly => Kind is "GiveKeyword" or "TurnEndPlay" or "DiscardCard" && Amount <= 0m && !AmountIsXEffective;

    /// <summary>
    /// 「范围界定」那一行要不要显示。
    ///
    /// **必须只按种类判断，不要跟着数值变**（用户实测报过「在锻造里怎么不能填范围了」）：
    /// 新加的效果数值默认是 0，而「数值 0 = 这张牌自己」那几档（给予关键词 / 回合结束自动打出 / 丢弃卡牌）
    /// 在 0 的时候确实不挑牌 —— 但那不代表要把这一行**藏起来**：藏了用户根本没法填，
    /// 也不知道为什么刚才还在、现在没了。现在改成一直显示（只要这个种类真的会挑牌），
    /// 而「这一档填了也不会生效」由校验器给一条提示说清楚。
    /// </summary>
    [JsonIgnore]
    public bool ShowCardScopeFilter => UseCardScopeFilter;

    /// <summary>这一条效果真的设了范围界定（空 = 不限）。生成代码 / 描述都按它判断。</summary>
    [JsonIgnore]
    public bool HasCardScope => UseCardScopeFilter && !CardScopeSelfOnly && !string.IsNullOrWhiteSpace(SpawnFilter);

    /// <summary>范围界定的中文（列表 / 卡面描述里用）。</summary>
    [JsonIgnore]
    public string CardScopeZh => EffectCatalog.SpawnFilterZh(SpawnFilter);

    /// <summary>效果列表里那句「 ｜ 范围界定：攻击」（没设范围就空）。</summary>
    [JsonIgnore]
    public string CardScopeListText => HasCardScope ? $" ｜ 范围界定：{CardScopeZh}" : "";

    // ===== 变化卡牌：只变化「哪几张牌」（V0.2.213，用户报的「没法选择目标卡或者组内的牌进行变化」）=====
    //
    // 为什么必须**另开字段**：变化卡牌的 <see cref="SpawnFilter"/> 已经有含义了 —— 那是
    // 「变出来的新卡从哪个范围里抽」（见 CSharpCodeGen.SpawnPoolExpr / __transformPool）。
    // 同一个字段再拿去限定「挑哪几张去变」就成了一句话两个意思，而且会让老存档的
    // 「变化卡牌 + 按范围随机 + 范围=攻击」在更新后突然只能挑攻击牌来变（静默变行为）。
    // 所以这里新开两个**只属于「变化卡牌」**的字段：
    //   · <see cref="TransformPick"/>   = 按范围挑（类型 / 自定义关键词那一组 / 卡牌组）；
    //   · <see cref="TransformCardId"/> = 指定某一张卡（填了它就以它为准）。
    private string? _transformPick;

    /// <summary>「变化哪些牌（范围）」：空 = 不限（老行为）。取值和「范围界定」同一份选项表。</summary>
    /// <remarks>
    /// ⚠ 用 <see cref="JsonIgnoreCondition.WhenWritingDefault"/> + 可空类型：**空值不写进 JSON** ——
    /// 锻造图的节点会把整份 <see cref="EffectSpec"/> 嵌进 `// CET:ForgeGraph=` 标记里，
    /// 多写两个空键会让没用过这两栏的老存档生成文本变样（红线）。写成 null 就一个字节都不多。
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? TransformPick
    {
        get => _transformPick;
        set
        {
            if (Set(ref _transformPick, string.IsNullOrWhiteSpace(value) ? null : value.Trim()))
            {
                Raise(nameof(TransformPickZh));
                Raise(nameof(HasTransformPick));
                Raise(nameof(HasTransformSourceFilter));
                Raise(nameof(Display));
            }
        }
    }

    /// <summary>「变化哪一张卡（指定）」：空 = 不限。填的是卡牌类名（和「目标卡」同一份候选）。</summary>
    /// <remarks>
    /// V0.2.214：这一栏升级成**所有「从牌堆里挑牌」的效果共用**的「只对这张卡」（正式字段改叫
    /// <see cref="ScopeCardId"/>，见下面那段）。本属性只留作**旧存档兼容**（JSON 键名还是
    /// <c>TransformCardId</c>）：读到就搬到 <see cref="ScopeCardId"/>，写出去永远是空
    /// （新存档一律只写 <c>ScopeCardId</c>，不会两个键都落盘）。
    /// </remarks>
    [JsonPropertyName("TransformCardId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? TransformCardIdLegacy
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                ScopeCardId = value;
        }
    }

    /// <summary>「变化哪些牌」的中文（界面 / 描述里用）。</summary>
    [JsonIgnore]
    public string TransformPickZh => EffectCatalog.SpawnFilterZh(_transformPick);

    /// <summary>「变化哪些牌 / 只变化这张卡」那两行要不要显示（只有「变化卡牌」用得到）。</summary>
    [JsonIgnore]
    public bool UsesTransformPick => string.Equals(Kind, "TransformCard", StringComparison.Ordinal);

    /// <summary>真的设了「变化哪些牌（范围）」（空 = 不限）。</summary>
    [JsonIgnore]
    public bool HasTransformPick => UsesTransformPick && !string.IsNullOrWhiteSpace(_transformPick);

    /// <summary>这条「变化卡牌」有没有限定挑哪几张牌（生成端只在它非空时才套过滤条件 → 老存档逐字不变）。</summary>
    [JsonIgnore]
    public bool HasTransformSourceFilter => HasTransformPick || HasScopeCardId;

    /// <summary>列表里那句「 ｜ 只变化：攻击」（没限定就空）。</summary>
    [JsonIgnore]
    public string TransformPickListText => !UsesTransformPick ? ""
        : HasScopeCardId ? $" ｜ 只变化：{_scopeCardId}"
        : HasTransformPick ? $" ｜ 只变化：{TransformPickZh}"
        : "";

    // ===== 「只对这张卡」（V0.2.214，用户报的「丢弃 / 消耗卡牌无法选择目标卡或者组」）=====
    //
    // 原来只有「范围界定」= 按**类型**（攻击 / 技能 / 能力 / 诅咒 / 状态）或者按**组**（自定义关键词那一组 /
    // 卡牌组）筛，没有「就这一张卡」。于是「只消耗『打击』」这种做法只能靠建一个只含打击的卡牌组，
    // 用户找不到。现在补一个**指定的那张卡**，和「范围界定」是**与**的关系（两个都填就都要满足）：
    //   · 变化卡牌上叫「只变化这张卡」（它的类型/组筛选在 TransformPick，见上面）；
    //   · 丢弃 / 消耗 / 升级 / 复制 / 重放 / 给关键词 / 三摞拿牌上叫「只对这张卡」。
    // 空值不写进 JSON（WhenWritingDefault + 可空）：老存档生成文本 / 图节点里嵌的 EffectSpec 都一个字节不变。
    private string? _scopeCardId;

    /// <summary>「只对这张卡（指定）」：空 = 不限。填的是卡牌类名（和「目标卡」同一份候选）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ScopeCardId
    {
        get => _scopeCardId;
        set
        {
            if (Set(ref _scopeCardId, string.IsNullOrWhiteSpace(value) ? null : value.Trim()))
            {
                Raise(nameof(ScopeCardListText));
                Raise(nameof(HasScopeCardId));
                Raise(nameof(HasCardSourceFilter));
                Raise(nameof(HasTransformSourceFilter));
                Raise(nameof(Display));
            }
        }
    }

    /// <summary>「只对这张卡」那一行要不要显示（所有「从牌堆里挑牌」的效果 + 变化卡牌）。</summary>
    [JsonIgnore]
    public bool UsesScopeCardId => UseCardScopeFilter || UsesTransformPick;

    /// <summary>
    /// 界面上「只对这张卡（指定）」那一行要不要显示：能过滤**来源牌**的种类都显示，
    /// **但变化卡牌除外**（它有自己的「变化哪些牌 / 只变化这张卡」两行，语义是「变哪些」）。
    /// </summary>
    [JsonIgnore]
    public bool UsesScopeCardFilterRow => UsesScopeCardId && !UsesTransformPick;

    /// <summary>真的设了「只对这张卡」。</summary>
    [JsonIgnore]
    public bool HasScopeCardId => UsesScopeCardId && !string.IsNullOrWhiteSpace(_scopeCardId);

    /// <summary>
    /// 这条效果有没有**任何**「挑哪几张牌」的限定（类型 / 组 / 指定的那张卡）。
    /// 生成端只在它非空时才套过滤条件 / 写标记 → 老存档逐字不变（红线）。
    /// </summary>
    [JsonIgnore]
    public bool HasCardSourceFilter => HasCardScope || HasScopeCardId;

    /// <summary>列表里那句「 ｜ 只对：打击」（没设就空）。</summary>
    [JsonIgnore]
    public string ScopeCardListText => HasScopeCardId ? $" ｜ 只对：{_scopeCardId}" : "";

    /// <summary>
    /// 「多选1（候选张数）」：默认 1 = 直接生成（现在的行为）；
    /// 填 3 就是「先抽 3 张候选、玩家挑 1 张生成」（本体「发现 Discovery」那种界面）。
    /// 只对「生成卡牌 + 按范围随机」有意义（指定卡的话 3 张候选都是同一张，没意义）。
    /// </summary>
    public decimal SpawnChoice
    {
        get => _spawnChoice;
        set { if (Set(ref _spawnChoice, value)) { Raise(nameof(HasSpawnChoice)); Raise(nameof(Display)); } }
    }

    /// <summary>要不要用「多选1」（> 1 才是）。</summary>
    [JsonIgnore]
    public bool HasSpawnChoice => SpawnChoice > 1m;

    /// <summary>「多选1」那个输入框要不要显示（只有「生成卡牌 + 按范围随机」用得到）。</summary>
    [JsonIgnore]
    public bool ShowSpawnChoice => ShowSpawnPickRow && IsSpawnRandom;

    // ===== 复制卡牌：复制的份数 =====
    private int _copies = 1;

    /// <summary>
    /// 「复制卡牌」的**复制的份数**：数值（<see cref="Amount"/>）= 从牌堆里选几张，
    /// 这个 = 每一张复制几份（本体「二刀流 DualWield」就是「选 1 张，复制 CardsVar 份」：
    /// <c>selection.CreateClone()</c> + <c>CardPileCmd.AddGeneratedCardToCombat</c>）。
    /// 只有「复制卡牌」这条效果用得到，界面上的输入框也只在选它的时候出现。
    /// </summary>
    public int Copies
    {
        get => _copies;
        set { if (Set(ref _copies, value)) { RaiseCountFormulaBoxes(); Raise(nameof(Display)); } }
    }

    /// <summary>「复制的份数」那个输入框要不要显示。</summary>
    [JsonIgnore]
    public bool ShowCopies => Kind == "CopyCard";

    // ===== 重放卡牌：额外打出几次 =====
    private int _replayTimes = 1;

    /// <summary>
    /// 「重放卡牌」的**重放次数**：数值（<see cref="Amount"/>）= 从牌堆里选几张，
    /// 这个 = 每张**额外打出几次**（本体 <c>CardModel.BaseReplayCount</c>：打出时
    /// <c>GeneratePlayCount = GetEnchantedReplayCount() + 1</c>，所以「重放 1」= 一共打 2 次）。
    /// 本体「转化 Transfigure / 隐藏宝石 HiddenGem」就是 <c>card.BaseReplayCount += N</c>。
    /// </summary>
    public int ReplayTimes
    {
        get => _replayTimes;
        set { if (Set(ref _replayTimes, value)) { RaiseCountFormulaBoxes(); Raise(nameof(Display)); } }
    }

    /// <summary>「重放次数」那个输入框要不要显示。</summary>
    [JsonIgnore]
    public bool ShowReplayTimes => Kind == "ReplayCard";

    // ===== 次数类数值的公式（用户要求⑦：所有能填数值的地方都能写公式，次数类优先）=====
    /// <summary>
    /// 次数类字段各自的公式：键 = <c>Times</c>（生效次数）/ <c>Repeat</c>（命中次数 / 对群数）/
    /// <c>Copies</c>（复制的份数）/ <c>Replay</c>（重放次数），值 = 用户写的公式（空 = 用那个固定数字）。
    ///
    /// 为什么存成一个字典：这四处的存档 / 界面 / 校验 / 回读逻辑完全一样，只差「用在哪一段生成代码里」
    /// （见 <c>CSharpCodeGen.CountFormulaExpr</c>）—— 一个字段四份会让四处都写四遍。
    ///
    /// ⚠ 「概率」没进来：本体的概率是「同一张牌里勾了概率的效果**共用一次掷骰**、各占一段区间」
    /// （见 <c>ChanceSlices</c>），区间是**生成时**按固定百分比算好的，塞不进运行时表达式。
    ///
    /// ⚠ 和「数值公式」一样，**只做在卡牌上**：公式求值要 <c>__fgPlayer</c> / <c>__fgSelf</c> 那两个局部变量，
    /// 只有卡牌那条链（以及锻造节点图那个执行点）会先算它们。遗物 / 药水 / 状态 / 诅咒上填了会被配置检查拦住。
    /// </summary>
    private Dictionary<string, string> _countFormulas = new();

    /// <summary>次数类字段的公式表（键见上面那段注释；空 / 缺键 = 用固定数字）。</summary>
    public Dictionary<string, string> CountFormulas
    {
        get => _countFormulas;
        set
        {
            var next = value ?? new Dictionary<string, string>();
            if (Set(ref _countFormulas, next))
            {
                Raise(nameof(TimesFormula));
                Raise(nameof(RepeatFormula));
                Raise(nameof(CopiesFormula));
                Raise(nameof(ReplayFormula));
                Raise(nameof(Display));
            }
        }
    }

    /// <summary>次数类字段一共能写几种（校验器 / 回读 / 界面共用一份口径）。</summary>
    public static readonly string[] CountFormulaKeys = { "Times", "Repeat", "Copies", "Replay", "Chance" };

    /// <summary>某个次数类字段的中文名（报错 / 界面提示用）。</summary>
    public static string CountFormulaZh(string key) => key switch
    {
        "Times" => "生效次数",
        "Repeat" => "命中次数 / 对群数",
        "Copies" => "复制的份数",
        "Replay" => "重放次数",
        "Chance" => "概率",
        _ => key,
    };

    /// <summary>读一个次数类字段的公式（没设过返回空串）。</summary>
    public string CountFormula(string key) =>
        _countFormulas.TryGetValue(key, out string? v) ? (v ?? "") : "";

    /// <summary>
    /// 「从工程恢复存档」专用：直接把回读到的公式写进表里（**绕开 <see cref="SetCountFormula"/> 的
    /// 界面显隐判断** —— 回读时对象还没进任何列表，`ShowsCountFormula` 那套是按「这一栏显不显示」算的，
    /// 不该拿来决定「要不要还原存档里的值」）。
    /// </summary>
    public void SetCountFormulaFromRecovery(string key, string? value)
    {
        string next = (value ?? "").Trim();
        if (next.Length == 0) _countFormulas.Remove(key);
        else _countFormulas[key] = next;
    }

    private void SetCountFormula(string key, string? value)
    {
        string next = (value ?? "").Trim();
        if (string.Equals(CountFormula(key), next, StringComparison.Ordinal)) return;
        if (next.Length == 0) _countFormulas.Remove(key);
        else _countFormulas[key] = next;
        Raise(key + "Formula");
        Raise(nameof(Display));
    }

    /// <summary>生效次数（整条效果重复几遍）的公式。</summary>
    public string TimesFormula { get => CountFormula("Times"); set => SetCountFormula("Times", value); }

    /// <summary>命中次数 / 对群数的公式。</summary>
    public string RepeatFormula { get => CountFormula("Repeat"); set => SetCountFormula("Repeat", value); }

    /// <summary>复制份数的公式。</summary>
    public string CopiesFormula { get => CountFormula("Copies"); set => SetCountFormula("Copies", value); }

    /// <summary>重放次数的公式。</summary>
    public string ReplayFormula { get => CountFormula("Replay"); set => SetCountFormula("Replay", value); }

    /// <summary>
    /// 概率（%）的公式（用户要求⑦ 批次 3）。
    ///
    /// ⚠ 语义和其它几个不同：**没写公式时**，同一张牌里勾了概率的效果是「**共用一次掷骰**、
    /// 各占一段区间」（见 <c>ChanceSlices</c>，本体也是这个做法）；**写了公式**的那条会从区间里退出，
    /// 改成**自己独立判一次**（<c>__chanceRoll &lt; 现算的百分比</c>）—— 生成时算不出它的区间来。
    /// 所以「一条勾公式 + 另一条勾固定值」时，两条不再互斥（各自独立判定）。校验器会提示这一点。
    /// </summary>
    public string ChanceFormula { get => CountFormula("Chance"); set => SetCountFormula("Chance", value); }

    /// <summary>这个次数类字段在这个效果上能不能写公式（只做在卡牌上，而且要那一栏真的显示）。</summary>
    public bool ShowsCountFormula(string key) => key switch
    {
        "Times" => EffectFieldPolicy.UsesTimes(Kind) && !TimesIsX && !TimesIsStack,   // 生效次数：那 2 种挂在卡自己钩子上的不显示
        "Repeat" => UsesRepeatCount && !RepeatIsX && !RepeatIsStack,   // 命中次数：只对真的会用到它的种类显示（生成端判据；界面那一行见 ShowRepeatCountInput）
        "Copies" => ShowCopies,
        "Replay" => ShowReplayTimes,
        "Chance" => ChanceEnabled,                    // 概率：勾了「概率生效」才能填
        _ => false,
    };

    /// <summary>
    /// 「命中次数 / 对群数」这个字段**真的会被生成端用到**吗？
    ///
    /// 用户报过「命中次数的公式不生效」：以前这个框对所有种类都显示，可生成端只有这几种会读它 ——
    ///   · 「造成伤害」（多段：<c>.WithHitCount(N)</c>）；
    ///   · 作用对象 = 「随机敌人」（N = 挑几个敌人，见 EmitPerCreature / EmitStun）；
    ///   · 几种「伙伴攻击」（每段各打一下）。
    /// 其它种类（抽牌 / 获得格挡 / 施加状态（单体）/ 击晕（单体）/ 失去生命…）压根不看这个数字 ——
    /// 所以现在：这些种类上**不显示**这一栏，校验器也会把填了值 / 填了公式的配置**说清不生效**
    ///（想「整条效果重复几遍」请用「生效次数」）。
    /// </summary>
    [JsonIgnore]
    public bool UsesRepeatCount =>
        TargetSide == "RandomEnemies"
        || (Kind == "Damage" && TargetSide != "Self")
        || Kind is "PetAttack" or "PetDamageByMaxHp" or "PetDamageByCurHp" or "PetDamageByMissingHp";

    /// <summary>界面用：「对群数 / 命中次数」那一行要不要显示（用不到就不摆一个填了没反应的框）。</summary>
    [JsonIgnore]
    public bool ShowRepeatCountInput => UsesRepeatCount || HasStaleRepeatFields;

    /// <summary>
    /// V0.2.171：这一条效果**用不到**「命中次数 / 对群数」，可里面**还留着值**（多半是换效果种类之前填的）。
    ///
    /// <para>用户实测报的：「我并没有填 Y 怎么还这样」—— 卡牌「新卡」第 1 条「播放音频文件（我上传的）」
    /// 上留着 <c>RepeatFormula = "Y"</c>。按种类这一栏是**收起**的（<see cref="UsesRepeatCount"/> = false），
    /// 于是校验器拿它挡住生成，用户却根本找不到在哪里改。所以这一档必须把那一行**露出来**让人清掉
    ///（<see cref="ShowRepeatCountInput"/> 里带上它）。</para>
    /// </summary>
    [JsonIgnore]
    public bool HasStaleRepeatFields => !UsesRepeatCount
        && (RepeatCount > 1 || RepeatIsX || RepeatIsStack || !string.IsNullOrWhiteSpace(RepeatFormula));

    /// <summary>
    /// 把这一条效果用不到的「命中次数 / 对群数」清掉（换效果种类时调一次）。
    /// 生成端本来就不读它们，但留着会被校验拦住 —— 换种类引起的、用户看不见的残留不该留在配置里。
    /// </summary>
    public void ClearStaleRepeatFields()
    {
        if (UsesRepeatCount) return;
        RepeatCount = 1;
        RepeatFormula = "";
        RepeatIsX = false;
        RepeatIsStack = false;
    }

    /// <summary>这个次数类字段设了公式没有（界面显隐 / 生成端判据）。</summary>
    public bool UsesCountFormula(string key) =>
        ShowsCountFormula(key) && !string.IsNullOrWhiteSpace(CountFormula(key));

    /// <summary>这个效果有没有设任何一个次数类公式（生成端 / 回读 / 界面共用）。</summary>
    [JsonIgnore]
    public bool UsesAnyCountFormula =>
        CountFormulaKeys.Any(k => !string.IsNullOrWhiteSpace(CountFormula(k)));

    /// <summary>界面用：次数类公式的四行各自要不要显示（XAML 不能给绑定传参数，所以逐个列出来）。</summary>
    [JsonIgnore] public bool ShowTimesFormulaBox => ShowsCountFormula("Times");
    [JsonIgnore] public bool ShowRepeatFormulaBox => ShowRepeatCountInput && !RepeatIsX && !RepeatIsStack;
    [JsonIgnore] public bool ShowCopiesFormulaBox => ShowsCountFormula("Copies");
    [JsonIgnore] public bool ShowReplayFormulaBox => ShowsCountFormula("Replay");
    [JsonIgnore] public bool ShowChanceFormulaBox => ShowsCountFormula("Chance");

    /// <summary>效果列表里追加的那一小段（「次数公式 生效=Turn / 命中=2+HandCount」）。</summary>
    [JsonIgnore]
    public string CountFormulaListText =>
        " ｜ 次数公式 " + string.Join(" / ", CountFormulaKeys
            .Where(k => !string.IsNullOrWhiteSpace(CountFormula(k)))
            .Select(k => $"{CountFormulaZh(k)}={Generation.ForgeFormula.DisplayText(CountFormula(k))}"));

    private void RaiseCountFormulaBoxes()
    {
        Raise(nameof(ShowTimesFormulaBox));
        Raise(nameof(ShowRepeatFormulaBox));
        Raise(nameof(ShowCopiesFormulaBox));
        Raise(nameof(ShowReplayFormulaBox));
        Raise(nameof(ShowChanceFormulaBox));
        // 「命中次数 / 对群数」那一行本身（换种类 / 换作用对象时要跟着出现或收起）
        Raise(nameof(UsesRepeatCount));
        Raise(nameof(ShowRepeatCountInput));
        Raise(nameof(HasStaleRepeatFields));
        // V0.2.132：卡牌链上「命中次数」那一行是横排（固定框 + 公式框），让位关系也要一起通知
        Raise(nameof(ShowCardRepeatRow));
        Raise(nameof(ShowPlainFixedRepeatBox));
        Raise(nameof(ShowCardTimesRow));
        Raise(nameof(ShowPlainFixedTimesBox));
    }

    /// <summary>次数类公式那一栏底下的说明（写清了哪些链条支持、和「= X / = 层数」不能同时用）。</summary>
    [JsonIgnore]
    public string CountFormulaHint =>
        "留空 = 用左边那个固定次数；写了 = 打出这张牌（或这条遗物 / 药水 / 状态触发）时现算。"
        + "写法与「数值公式」完全一样（参数 M / N…、X 当前能量、Y 当前额外资源量、"
        + "内置只读量 Turn / HandCount / DiscardedThisTurn / PowersOnMe… —— **完整清单见启动时弹出的「参数参考」窗**）。"
        + "⚠ 「生效次数」的公式卡牌 / 遗物 / 药水 / 自定义状态 / 锻造【效果】五处都支持；"
        + "「命中次数 / 复制份数 / 重放次数 / 概率」的公式只在卡牌和锻造【效果】里（其余几栏那几条链的生成端不读它）。"
        + "而且和「= X / = 层数」那几个开关不能同时用（它们都是「改用算出来的值」）。"
        + "算出来的次数会被夹到 1 ~ 20，每次求值现算。"
        + "（不能写公式的地方：卡牌**费用** / 升级增量 / 页面常量（初始血量、初始金币、资源量上下限…）——"
        + "那些必须在生成时就定下来，本体构造函数要的是编译期常量。）";


    // ===== 刮削（ScrapeDraw）：条件 + 操作（用户要求：抽到之后对「满足条件的牌」做「某个操作」）=====
    // 需求原文：「条件设宽泛一定，抽 N，对其中满足一定条件的牌执行特定操作」。
    // 默认值就是老行为（非 0 费 + 丢弃）—— 老存档里没有这两个字段，反序列化后就是这两个默认值，
    // 生成出来的代码与改动前逐字一致（由 --uicheck 钉死）。
    private string _scrapeCond = EffectCatalog.ScrapeCondNonZeroCost;
    private string _scrapeOp = EffectCatalog.ScrapeOpDiscard;

    /// <summary>
    /// 抽完之后「对其中哪些牌动手」：<c>NonZeroCost</c>（默认，本体「刮削」的判据）/
    /// <c>Any</c>（不限）/ Attack / Skill / Power / Curse / Status / <c>Keyword:XXX</c>（自定义关键词那一组）。
    /// 候选见 <see cref="EffectCatalog.ScrapeCondsFor"/>（就是范围限定那一份，前面多两条）。
    /// </summary>
    public string ScrapeCond
    {
        get => _scrapeCond;
        set { if (Set(ref _scrapeCond, value ?? EffectCatalog.ScrapeCondNonZeroCost)) { Raise(nameof(ScrapeCondZh)); Raise(nameof(Display)); } }
    }

    /// <summary>
    /// 对选中的那些牌「执行什么操作」：<c>Discard</c>（默认，丢进弃牌堆）/ <c>Exhaust</c>（消耗掉）/
    /// <c>Upgrade</c>（升级）。候选见 <see cref="EffectCatalog.ScrapeOps"/>。
    /// </summary>
    public string ScrapeOp
    {
        get => _scrapeOp;
        set { if (Set(ref _scrapeOp, value ?? EffectCatalog.ScrapeOpDiscard)) { Raise(nameof(ScrapeOpZh)); Raise(nameof(Display)); } }
    }

    /// <summary>条件的中文（列表 / 描述里用）。</summary>
    [JsonIgnore]
    public string ScrapeCondZh => EffectCatalog.ScrapeCondZhFor(null, ScrapeCond);

    /// <summary>操作的中文（列表 / 描述里用）。</summary>
    [JsonIgnore]
    public string ScrapeOpZh => EffectCatalog.ScrapeOpZh(ScrapeOp);

    /// <summary>「条件 / 操作」那两行要不要显示（只有「刮削」才有这两栏）。</summary>
    [JsonIgnore]
    public bool ShowScrapeOptions => Kind == "ScrapeDraw";

    /// <summary>是不是「回合结束时自动打出」（数值 0 = 这张牌自己；≥ 1 = 从牌堆里选 N 张）。</summary>
    [JsonIgnore]
    public bool IsTurnEndPlay => Kind == "TurnEndPlay";

    /// <summary>「回合结束时自动打出」的说明那一行要不要显示（跟着效果种类走）。</summary>
    [JsonIgnore]
    public bool ShowTurnEndPlay => Kind == "TurnEndPlay";

    /// <summary>界面用：「持续时间（只持续本回合）」那一行要不要显示（V0.2.249 的「减少卡牌费用」用）。</summary>
    [JsonIgnore]
    public bool IsCostDown => Kind == "CostDown";

    private bool _costDownThisTurn;

    /// <summary>
    /// V0.2.249（用户要求「减少指定卡牌类别的费用（类似腐化，刺破帷幕），加在效果种类里」）：
    /// 勾上 = 这个减费**只持续本回合**（回合结束自动撤掉）；不勾 = 本场战斗一直有效（腐化那种）。
    /// 只有 <see cref="Kind"/> = CostDown 时读；默认不勾。
    /// </summary>
    public bool CostDownThisTurn
    {
        get => _costDownThisTurn;
        set { if (Set(ref _costDownThisTurn, value)) Raise(nameof(Display)); }
    }

    /// <summary>生成 / 变化的卡**直接升级**（本场战斗内一直有效）。</summary>
    public bool SpawnUpgraded
    {
        get => _spawnUpgraded;
        set { if (Set(ref _spawnUpgraded, value)) { Raise(nameof(HasSpawnModifier)); Raise(nameof(Display)); } }
    }

    /// <summary>生成 / 变化的卡**免费打出**（费用设为 0，本场战斗内有效）。</summary>
    public bool SpawnFree
    {
        get => _spawnFree;
        set { if (Set(ref _spawnFree, value)) { Raise(nameof(HasSpawnModifier)); Raise(nameof(Display)); } }
    }

    /// <summary>生成 / 变化的卡**仅本回合免费**（回合结束自动恢复原费用）。</summary>
    public bool SpawnFreeThisTurn
    {
        get => _spawnFreeThisTurn;
        set { if (Set(ref _spawnFreeThisTurn, value)) { Raise(nameof(HasSpawnModifier)); Raise(nameof(Display)); } }
    }

    /// <summary>
    /// 生成 / 变化的卡**仅本回合升级**（回合结束恢复成没升级的样子）。
    /// 本体没有这种 API，靠生成的 <c>&lt;角色&gt;ForgeTempUpgradePower</c> 在回合结束时降回来
    /// （只降「这次真的升上去的那几张」，本来就是升级过的牌不动它）。
    /// </summary>
    public bool SpawnUpgradedThisTurn
    {
        get => _spawnUpgradedThisTurn;
        set { if (Set(ref _spawnUpgradedThisTurn, value)) { Raise(nameof(HasSpawnModifier)); Raise(nameof(Display)); } }
    }

    /// <summary>
    /// 生成 / 变化出来的卡**带上的关键词**（需求 ⑦，像「是否升级」那样给的一项）：
    /// 空 = 不带（默认档，老存档就是它）；值是本体关键词的枚举名（<c>Exhaust</c> / <c>Ethereal</c> …）
    /// 或者自定义关键词的引用（下拉里给的是键）。
    ///
    /// 生成代码复用「给予卡牌关键词」那一套（<c>CSharpCodeGen.EmitGiveKeywordTo</c>）：
    /// 本体关键词走 <c>AddKeyword(CardKeyword.X)</c>，自定义关键词走
    /// <c>AddGivenCustomKeyword</c> + 本地化词条 + 悬停说明，所以不另写一份实现。
    /// </summary>
    public string SpawnKeyword
    {
        get => _spawnKeyword;
        set
        {
            if (Set(ref _spawnKeyword, value ?? ""))
            {
                Raise(nameof(HasSpawnKeyword));
                // 只有这一项时也算「要改生成出来的卡」：生成卡牌那条路必须自己 CreateCard 才拿得到实例
                Raise(nameof(HasSpawnModifier));
                Raise(nameof(SpawnKeywordZh));
                Raise(nameof(Display));
            }
        }
    }

    /// <summary>选了「生成 / 变化出来的卡带上关键词」。</summary>
    [JsonIgnore]
    public bool HasSpawnKeyword => !string.IsNullOrWhiteSpace(_spawnKeyword);

    /// <summary>
    /// 「生成出来的卡带上的关键词」在界面上的名字：本体关键词中文名（Retain → 保留）；
    /// 自定义关键词显示用户填的英文标识 / 键（和 <see cref="GivenKeywordZh"/> 一个写法 —— Profile 层拿不到关键词列表）。
    /// </summary>
    [JsonIgnore]
    public string SpawnKeywordZh =>
        string.IsNullOrWhiteSpace(_spawnKeyword) ? "?" : EffectCatalog.VanillaKeywordZh(_spawnKeyword);

    /// <summary>勾了任意一个「生成出来的卡怎么处理」（升级 / 免费 / 加上关键词…）。</summary>
    [JsonIgnore]
    public bool HasSpawnModifier => SpawnUpgraded || SpawnFree || SpawnFreeThisTurn || SpawnUpgradedThisTurn || HasSpawnKeyword;

    /// <summary>这些「生成 / 变化」相关的设置要不要显示（只有生成 / 变化 / 全局加牌这几种用得到）。</summary>
    [JsonIgnore]
    public bool UsesSpawnOptions => Kind is "GenerateCard" or "TransformCard" or "TransformCardGlobal" or "AddCardGlobal"
        // 复制卡牌也支持「复制出来的卡怎么处理」（用户要求：复制出来的卡也能选择免费打出或升级）——
        // 复制出来的本来就是新卡（CreateClone），和「生成卡牌」那一套附加处理完全一样，直接复用。
        or "CopyCard";

    /// <summary>
    /// 「取卡方式（指定卡 / 按范围随机）」那一行 + 它下面那几行（范围限定 / 多选1 / 从范围里自己挑）
    /// 的显隐（V0.2.150）。
    ///
    /// <para>和 <see cref="UsesSpawnOptions"/> 分开：复制卡牌虽然支持**附加处理**，
    /// 但它不读「取卡方式 / 范围限定」（它挑哪张牌由「选牌方式 / 从哪里选牌 / 范围界定」决定），
    /// 所以那一行在它上面是白摆的（机械测量：见 <see cref="EffectFieldPolicy.UsesSpawnPick"/>）。</para>
    /// </summary>
    [JsonIgnore]
    public bool ShowSpawnPickRow => IsForgeChain ? UsesSpawnOptions : EffectFieldPolicy.UsesSpawnPick(Kind);

    /// <summary>「范围限定」那一行要不要显示（只有「按范围随机」才有意义）。</summary>
    [JsonIgnore]
    public bool ShowSpawnFilter => ShowSpawnPickRow && IsSpawnRandom;

    /// <summary>「指定卡」时在下面提示一句「范围限定」在哪（用户报过「找不到在哪限定范围」）。</summary>
    [JsonIgnore]
    public bool ShowSpawnPickHint => ShowSpawnPickRow && !IsSpawnRandom;

    private bool _chanceEnabled;
    private decimal _chancePercent = 100m;   // V0.2.275：默认 100 = 必定（以前 50 会让「没勾也不想掷骰」的效果看起来像 50%）

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

    /// <summary>
    /// 「不同效果的概率之间是否独立」（V0.2.132，《卡牌页优化.md》右侧④）。
    ///
    /// <para><b>默认 false = 老行为</b>：同一张牌 / 同一只遗物 / 同一个触发时机里，所有勾了「概率生效」的效果
    /// **共用一次掷骰**、各占一段区间（第一条 40% + 第二条 60% = 掷出 0~39.999 只跑第一条，
    /// 40~99.999 只跑第二条，见 <c>CSharpCodeGen.ChanceSlices</c>）。</para>
    ///
    /// <para>true = 这条效果**自己独立掷一次**（<c>NextDouble() * 100.0 &lt; 这条的百分比</c>），
    /// 不再参与区间划分 —— 于是「50% 的两条」会各自判定，可能两条都中、也可能两条都不中。</para>
    ///
    /// <para>为什么放在**效果**上而不是整张牌上：界面那一栏长在每条效果自己的「概率」底下（需求原文
    /// 「再其下为概率…并且可以选择不同效果的概率之间是否独立」），而且遗物 / 状态链的「一组」是各条
    /// 触发时机，放在效果上才能让每一条各自决定。默认档<b>不写任何新标记</b>，老存档的生成文本一个字节都不变。</para>
    /// </summary>
    public bool ChanceIndependent
    {
        get => _chanceIndependent;
        set { if (Set(ref _chanceIndependent, value)) Raise(nameof(Display)); }
    }

    /// <summary>概率那一行在效果列表 / 卡面上的文案（写了公式就印公式；选了「独立」再补一句）。</summary>
    [JsonIgnore]
    public string ChanceText => !ChanceEnabled ? ""
        : !string.IsNullOrWhiteSpace(ChanceFormula)
            ? $"（{Generation.ForgeFormula.DisplayText(ChanceFormula)}% 概率{(_chanceIndependent ? "，各自独立判定" : "")}）"
            : $"（{ChancePercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}% 概率{(_chanceIndependent ? "，各自独立判定" : "")}）";

    /// <summary>「召唤 / 伙伴攻击」这条效果指向的召唤物（类名；留空 = 还没选）。</summary>
    [JsonIgnore]
    public string PetSummonTag => (PetSummon ?? "").Trim();
    /// <summary>本条效果对谁生效：Self / Ally / AllAllies / Enemy / RandomEnemies / AllEnemies / AllUnits</summary>
    /// <summary>
    /// 作用对象（这条效果对谁生效）。改它会连带影响「命中次数 / 对群数」那一行要不要显示
    ///（只有「随机敌人」会用到那个数字，见 <see cref="UsesRepeatCount"/>），以及「全部友方」
    /// 那条「不包括召唤物」的子选项要不要显示（见 <see cref="ShowAllyNoPetsOption"/>）。
    /// </summary>
    public string TargetSide
    {
        get => _targetSide;
        set
        {
            if (!Set(ref _targetSide, value)) return;
            Raise(nameof(UsesRepeatCount));
            Raise(nameof(ShowRepeatCountInput));
            Raise(nameof(ShowAllyNoPetsOption));
            Raise(nameof(ShowAllAlliesIncludeSelfRow));   // V0.2.201：「全部友方 · 包括自己」那一行
            Raise(nameof(ShowAllowDuplicatesRow));
            RaiseCountFormulaBoxes();
            Raise(nameof(Display));
        }
    }

    /// <summary>
    /// 界面上「作用对象」下拉的候选（V0.2.306，用户要求「让回能和获得金币的对象能指定队友」）。
    ///
    /// <para>默认 = 全部七档（老行为，一个字都没变）；「获得能量 / 获得金币」作用在**玩家**身上，
    /// 只给「自己 / 指定队友 / 全部友方」三档（见 <see cref="EffectCatalog.TargetSidesFor"/>）。</para>
    ///
    /// <para>⚠ 换过效果种类时旧值可能不在这份候选里（例：先填了「全体敌人」再改成「获得能量」）——
    /// 这时把旧值**补在最后**，别让下拉变成空白（用户会以为配置丢了）；它在生成端不生效，
    /// 校验器会明确报出来。</para>
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> TargetSideChoices
    {
        get
        {
            var list = EffectCatalog.TargetSidesFor(Kind);
            if (list.Any(x => string.Equals(x, TargetSide, StringComparison.Ordinal))) return list;
            var withStale = list.ToList();
            withStale.Add(TargetSide);
            return withStale;
        }
    }

    /// <summary>
    /// 「全部友方」这一档**不包括召唤物**（V0.2.132，需求原文：「全部友方（可以选择包不包括召唤物）」）。
    ///
    /// <para><b>默认 false = 老行为</b>：同一边的玩家单位全算（含召唤物）——
    /// 生成的过滤条件原样是 <c>c =&gt; c.IsAlive &amp;&amp; c != base.Owner.Creature</c>，
    /// 和 V0.2.131 逐字节相同。true 才在末尾补一句 <c>&amp;&amp; c.PetOwner is null</c>
    ///（本体 <c>Creature.PetOwner</c> 非空 = 那是宠物 / 召唤物）。</para>
    /// </summary>
    public bool AllAlliesExcludePets
    {
        get => _allAlliesExcludePets;
        set { if (Set(ref _allAlliesExcludePets, value)) Raise(nameof(Display)); }
    }

    /// <summary>界面 XAML 直接绑：「全部友方」的子选项那一行只在选了「全部友方」时显示。</summary>
    [JsonIgnore]
    public bool ShowAllyNoPetsOption => string.Equals(_targetSide, "AllAllies", StringComparison.Ordinal);

    private bool _allAlliesIncludeSelf;

    /// <summary>
    /// 「全部友方」要不要把**自己**算进去（V0.2.201，用户问「怎么选全部友方包括自己」）。
    ///
    /// <para><b>默认 false = 不含自己</b>：过滤条件原样是
    /// <c>c =&gt; c.IsAlive &amp;&amp; c != base.Owner.Creature</c>（和以前逐字节相同）。
    /// true 就把 <c>c != base.Owner.Creature</c> 这段去掉 → 自己也被算作「友方」。</para>
    ///
    /// <para>⚠ 注意「全体单位」那是**两边所有单位**（含敌人），不是这一档的替代品；
    /// 想「只打自己这一边、连自己一起」就用这个勾。</para>
    /// </summary>
    /// <remarks>
    /// V0.2.201：默认值 false 时**不写进 JSON**（<see cref="JsonIgnoreCondition.WhenWritingDefault"/>）——
    /// 一是老存档看上去一个字都没变，二是生成出来的 <c>// CET:ForgeGraph=</c> 标记里那段节点 JSON
    /// 也保持逐字节一致（那段就是把 EffectSpec 序列化进去的）。
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllAlliesIncludeSelf
    {
        get => _allAlliesIncludeSelf;
        set { if (Set(ref _allAlliesIncludeSelf, value)) Raise(nameof(Display)); }
    }

    /// <summary>界面 XAML 直接绑：「全部友方 · 包括自己」那一行只在选了「全部友方」时显示。</summary>
    [JsonIgnore]
    public bool ShowAllAlliesIncludeSelfRow => string.Equals(_targetSide, "AllAllies", StringComparison.Ordinal);

    /// <summary>
    /// 「随机目标允许攻击同个敌人」那一行要不要显示（V0.2.150）：
    /// 只有「作用对象 = 随机敌人」这一档真的会读它（本体 <c>TargetingRandomOpponents(allowDuplicates:)</c> /
    /// 生成代码里的 <c>foes.Remove(foe)</c>）—— 别的档它不参与生成，所以不摆。
    /// </summary>
    [JsonIgnore]
    public bool ShowAllowDuplicatesRow => EffectFieldPolicy.UsesAllowDuplicates(_targetSide);

    public int RepeatCount { get => _repeatCount; set => Set(ref _repeatCount, value); }
    /// <summary>生效次数：整条效果重复执行几遍（1~20，1 = 只执行一次）</summary>
    public int Times { get => _times; set => Set(ref _times, value); }
    public bool AllowDuplicates { get => _allowDuplicates; set => Set(ref _allowDuplicates, value); }
    public bool NextTurn { get => _nextTurn; set => Set(ref _nextTurn, value); }

    /// <summary>
    /// 「生成卡牌 + 按范围随机」时：**候选 = 这一组范围里的全部卡**，玩家自己挑一张
    /// （用户要求：「生成范围组里的卡，怎么不能在组里选择，只能随机」）。
    /// 勾上之后不再随机抽候选，选牌界面直接列出这一组里的卡（上限 60 张）。
    /// </summary>
    public bool SpawnChosen
    {
        get => _spawnChosen;
        set { if (Set(ref _spawnChosen, value)) { Raise(nameof(ShowSpawnChosen)); Raise(nameof(Display)); } }
    }

    /// <summary>「从范围里自己挑一张」那个勾选框要不要显示（只有「按范围随机」这一档才有意义）。</summary>
    [JsonIgnore]
    public bool ShowSpawnChosen => ShowSpawnPickRow && IsSpawnRandom;

    /// <summary>
    /// 「下回合生效」那个勾选框要不要显示：只有**生成端真的会走延迟分支**的种类才显示
    /// （见 <see cref="EffectCatalog.HonorsNextTurn"/>）——
    /// 以前这个勾对所有种类都摆着，其中 8 个种类生成端根本没实现（勾了等于没勾，用户报过「勾了没反应」）。
    /// <para>V0.2.150 用机械测量复核过：卡牌链上「生成端真的读 NextTurn」的种类**正好**就是这一份
    /// （8 种不读），所以这里不另写清单 —— 两份判据迟早会漂。</para>
    /// </summary>
    [JsonIgnore]
    public bool ShowNextTurn => EffectCatalog.HonorsNextTurn(Kind);

    private string _tempPowerReverse = TempPowerReverses.TurnEnd;

    /// <summary>
    /// 「临时增益（本回合 +X，回合结束消失）」**什么时候把这次加的层数撤掉**：
    ///   · <see cref="TempPowerReverses.TurnEnd"/>（默认）= 这一回合结束时撤（以前只有这一档）；
    ///   · <see cref="TempPowerReverses.NextTurnStart"/> = **撑过这一回合和敌人的回合，下回合开始时才撤**
    ///     —— 你要求的那种「获得临时 5 点力量 → 下一回合开始时失去 5 点力量」。
    ///
    /// 只对 <see cref="Kind"/> == "TempPower" 有意义；其它效果种类读它没有副作用（界面也只在那一档显示）。
    /// </summary>
    public string TempPowerReverse
    {
        get => _tempPowerReverse;
        set
        {
            string v = value == TempPowerReverses.NextTurnStart ? TempPowerReverses.NextTurnStart : TempPowerReverses.TurnEnd;
            if (Set(ref _tempPowerReverse, v)) { Raise(nameof(Display)); Raise(nameof(IsTempPowerNextTurn)); }
        }
    }

    /// <summary>这条临时增益是「下回合开始时才撤」那一档（界面 XAML 直接绑它，所以 setter 必须 Raise）。</summary>
    [JsonIgnore]
    public bool IsTempPowerNextTurn => Kind == "TempPower" && TempPowerReverse == TempPowerReverses.NextTurnStart;

    /// <summary>界面 XAML 直接绑：只有「临时增益」这一种效果才显示「什么时候撤」那一行。</summary>
    [JsonIgnore]
    public bool IsTempPower => Kind == "TempPower";

    /// <summary>「临时增益什么时候撤」的两个候选（界面下拉用）。</summary>
    [JsonIgnore]
    public IReadOnlyList<CountModeOption> TempPowerReverseChoices => TempPowerReverses.All;

        /// <summary>ApplyPower 用：本体 Power 类名（如 WeakPower）</summary>
    public string? PowerId
    {
        get => _powerId;
        set { if (Set(ref _powerId, value)) { Raise(nameof(Display)); Raise(nameof(IsSlowPower)); } }
    }

    private string _powerGroup = "";

    /// <summary>
    /// V0.2.255（用户要求「让状态进组，然后在清空指定状态的效果里可以选组」）：
    /// 「清除指定状态」用来一次清掉**一个组里的所有状态**（组在「组」页建，成员见 <see cref="CardGroupSpec.PowerIds"/>）。
    /// 存的是组 Id；空串 = 不用组（照旧按 <see cref="PowerId"/> 清一个）。
    /// </summary>
    public string PowerGroup
    {
        get => _powerGroup;
        set { if (Set(ref _powerGroup, value ?? "")) { Raise(nameof(Display)); Raise(nameof(UsesPowerGroup)); } }
    }

    /// <summary>界面用：「或者清空一个组」那一行要不要显示（只有「清除指定状态」用得到）。</summary>
    [JsonIgnore]
    public bool UsesPowerGroup => Kind == "ClearPowerOne";

    /// <summary>
    /// GenerateCard 用：生成哪张卡（卡牌类名，如 Shiv，也可以是本模组自己的卡）。
    /// TransformCard 用：变化成哪张卡；留空 = 随机变化。
    /// </summary>
    public string? SpawnCardId { get => _spawnCardId; set => Set(ref _spawnCardId, value); }

    // ===== 需求 ⑨：获得指定 / 随机药水 =====
    /// <summary>
    /// 「获得药水」是「指定药水」还是「随机药水」：
    /// <c>Fixed</c>（默认）= 用「目标药水」那一栏选定的那一瓶；<c>Random</c> = 本体随机一瓶
    /// （本体卡「炼制药水 Alchemize」走的就是 <c>PotionFactory.CreateRandomPotionInCombat</c>）。
    /// </summary>
    public string PotionPick
    {
        get => _potionPick;
        set
        {
            if (Set(ref _potionPick, value ?? "Fixed"))
            {
                Raise(nameof(IsPotionRandom));
                Raise(nameof(ShowPotionPicker));   // 「目标药水」那一行的显隐绑的就是它
                Raise(nameof(Display));
            }
        }
    }

    /// <summary>是不是「随机药水」（随机档不用选目标药水）。</summary>
    [JsonIgnore]
    public bool IsPotionRandom => string.Equals(_potionPick, "Random", StringComparison.Ordinal);

    /// <summary>
    /// 「目标药水」：本体药水的**类名**（如 <c>BlockPotion</c>）。
    /// 生成出来是 <c>PotionCmd.TryToProcure&lt;MegaCrit.Sts2.Core.Models.Potions.BlockPotion&gt;(玩家)</c> ——
    /// 本体 <c>TryToProcure&lt;T&gt;</c> 内部就是 <c>ModelDb.Potion&lt;T&gt;().ToMutable()</c>，
    /// 而模型 ID 是 <c>StringHelper.Slugify(类名)</c>，所以类名是这一栏的原生标识（见 EffectCatalog.PotionEntry）。
    /// </summary>
    public string? PotionId
    {
        get => _potionId;
        set { if (Set(ref _potionId, value)) { Raise(nameof(PotionNameZh)); Raise(nameof(Display)); } }
    }

    /// <summary>「目标药水」的中文名（界面 / 列表里显示用；目录里查不到就显示类名）。</summary>
    [JsonIgnore]
    public string PotionNameZh => EffectCatalog.FindPotionZh(PotionId);

    /// <summary>这些「获得药水」相关的行要不要显示（只有「获得药水」用得到）。</summary>
    [JsonIgnore]
    public bool UsesPotionOptions => Kind == "GainPotion";

    /// <summary>「目标药水」那一行要不要显示（选了「随机药水」时它没有意义）。</summary>
    [JsonIgnore]
    public bool ShowPotionPicker => UsesPotionOptions && !IsPotionRandom;

    /// <summary>GenerateCard 用：生成的卡放到哪：Hand / Draw / Discard</summary>
    public string SpawnTo { get => _spawnTo; set => Set(ref _spawnTo, value); }

    /// <summary>ExhaustCard / TransformCard 用：选牌方式：Random（随机）/ Chosen（自己选）</summary>
    public string CardPick { get => _cardPick; set => Set(ref _cardPick, value); }

    // ===== X 费用卡牌（本体 Whirlwind / 天际钻头 / 挽歌 那种「消耗全部能量」）=====
    // 这三个开关只有卡牌用得到（遗物/药水没有 X 这个概念）。
    /// <summary>数值 = X（X = 打出时消耗的能量/资源量）</summary>
    public bool AmountIsX { get => _amountIsX; set => Set(ref _amountIsX, value); }
    /// <summary>生效次数 = X（整条效果重复 X 遍，本体挽歌那种）</summary>
    public bool TimesIsX { get => _timesIsX; set { if (Set(ref _timesIsX, value)) { RaiseCountFormulaBoxes(); Raise(nameof(Display)); } } }
    /// <summary>命中次数 / 对群数 = X（本体旋风斩 / 天际钻头那种）</summary>
    public bool RepeatIsX { get => _repeatIsX; set { if (Set(ref _repeatIsX, value)) { RaiseCountFormulaBoxes(); Raise(nameof(Display)); } } }

    /// <summary>
    /// 「自定义状态（能力）」的触发器里用：数值 = <b>本状态的层数</b>（本体恶魔形态「每回合获得等于层数的力量」那种）。
    /// 卡牌/遗物/药水用不到这个开关（它们的数值就是填的数字）。</summary>
    public bool AmountIsStack { get => _amountIsStack; set => Set(ref _amountIsStack, value); }

    /// <summary>
    /// 「自定义状态」的触发器里用：<b>生效次数 = 本状态的层数</b>（整条效果按层数重复执行）。
    /// 例：5 层 + 「造成 2 点伤害」→ 每次触发打 5 次、共 10 点（本体旋风斩那种多段）。
    /// 勾了之后「生效次数」那一栏就不用了。</summary>
    public bool TimesIsStack { get => _timesIsStack_; set { if (Set(ref _timesIsStack_, value)) { RaiseCountFormulaBoxes(); Raise(nameof(Display)); } } }

    /// <summary>
    /// 「自定义状态」的触发器里用：<b>命中次数 / 对群数 = 本状态的层数</b>
    /// （只对「随机敌人」这类按次数挑目标的效果有意义）。</summary>
    public bool RepeatIsStack { get => _repeatIsStack_; set { if (Set(ref _repeatIsStack_, value)) { RaiseCountFormulaBoxes(); Raise(nameof(Display)); } } }

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
        "Exhaust" => "消耗牌堆",
        _ => "手牌",
    };

    /// <summary>这条效果要不要显示「从哪里选牌」（消耗 / 变化 / 丢弃 / 升级 / 给予关键词 / 复制 / 重放 / 回合结束用得到）。
    /// 「数值 0 = 这张牌自己」那一档同样**不藏**（用户报过「怎么不能填范围了」）：填了不生效由校验器提示。</summary>
    [JsonIgnore]
    public bool UsesSelectPile => KindUsesSelectPile(Kind);

    /// <summary>
    /// 按**效果种类**判「要不要「从哪里选牌」那一栏」（V0.2.212：锻造页的【效果】模块也要显示这一栏，
    /// 那边只有种类字符串、没有一份 <see cref="EffectSpec"/> —— 所以判据抽成静态方法，两处共用一份，
    /// 不会出现「卡牌页有、锻造页没有」这种各写一套的漂移）。
    /// </summary>
    public static bool KindUsesSelectPile(string? kind) =>
        kind is "ExhaustCard" or "TransformCard" or "DiscardCard" or "UpgradeCard"
        or "GiveKeyword" or "CopyCard" or "ReplayCard" or "TurnEndPlay" or "PlaceCard";

    // ===== 放置卡牌（用户要求的新效果）=====
    private string _placeTo = "Hand";

    /// <summary>
    /// 「放置卡牌」放到哪一摞：<c>Hand</c>（默认）/ <c>Draw</c>（抽牌堆底）/ <c>Discard</c> /
    /// <c>Exhaust</c> / <c>Top</c>（牌顶 = 抽牌堆最上面，本体 <c>CardPilePosition.Top</c>）。
    /// 数值 0 时 = 把这张牌自己放到这里；数值 ≥ 1 时 = 把从「从哪里选牌」挑出来的那几张放到这里。
    /// </summary>
    public string PlaceTo
    {
        get => _placeTo;
        set
        {
            string v = string.IsNullOrWhiteSpace(value) ? "Hand" : value;
            if (Set(ref _placeTo, v)) { Raise(nameof(PlaceToZh)); Raise(nameof(PlaceToChoices)); Raise(nameof(Display)); }
        }
    }

    /// <summary>界面用：「放到哪一摞」下拉的候选（手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆 / 牌顶）。</summary>
    [JsonIgnore]
    public IReadOnlyList<PileChoiceOption> PlaceToChoices => EffectCatalog.PlacePiles;

    /// <summary>「放到哪一摞」的中文（列表 / 卡面文案用）。</summary>
    [JsonIgnore]
    public string PlaceToZh => EffectCatalog.PlacePileZh(_placeTo);

    /// <summary>界面用：「放到哪一摞」那一行只在「放置卡牌」这一档显示。</summary>
    [JsonIgnore]
    public bool UsesPlaceTo => KindUsesPlaceTo(Kind);

    /// <summary>按**效果种类**判「要不要「放到哪一摞」那一栏」（锻造页的【效果】模块共用这一份判据，见 UsesSelectPile）。</summary>
    public static bool KindUsesPlaceTo(string? kind) =>
        // V0.2.226（用户要求：「复制效果能够复制自己或者可以选择从哪里选择复制并且到哪里」）：
        // 复制卡牌也要能选「复制出来的放到哪一摞」（以前写死手牌）。
        kind is "PlaceCard" or "CopyCard";

    // ===== 播放音效（V0.2.154，用户要求「能不能加一个播放音频的效果在效果种类里」）=====
    private string _sfx = "";

    /// <summary>
    /// 要播放的本体 FMOD 事件路径（形如 <c>event:/sfx/block_gain</c>）。
    /// 候选表见 <see cref="EffectCatalog.SfxChoices"/>（本体源码里真实用过的那些），
    /// 但**也可以直接填任何路径** —— 生成的就是一句 <c>SfxCmd.Play("…", 音量)</c>。
    /// </summary>
    public string Sfx
    {
        get => _sfx;
        set { if (Set(ref _sfx, value ?? "")) { Raise(nameof(Display)); Raise(nameof(SfxZh)); } }
    }

    /// <summary>界面用：「音效」那一行只在「播放音效」这一档显示。</summary>
    [JsonIgnore]
    public bool IsPlaySfx => Kind == "PlaySfx";

    /// <summary>界面用：「音频文件」那一行只在「播放音频文件（我上传的）」这一档显示（V0.2.155）。</summary>
    [JsonIgnore]
    public bool IsPlaySoundFile => Kind == "PlaySoundFile";

    private bool _sfxReplace;

    /// <summary>
    /// 「这段音频会被下一个自定义音频顶替」（V0.2.231，用户要求：在音频效果底下加一条「音频是否被下一个自定义音频顶替」）。
    ///
    /// <para><b>现在（不勾，默认）</b>：每次播放都是**新开一个 <c>AudioStreamPlayer</c>**、放完自己回收 ——
    /// 几段音频会**叠在一起**响，上一段不会因为下一段开始而停。</para>
    ///
    /// <para><b>勾上之后</b>：这段音频还放着的时候，只要**又播放了另一个「我上传的音频」**，
    /// 这段会立刻被停掉（新的一段接手）—— 适合「开场音乐 / 长音效」这种不希望叠音的东西。
    /// 实现上由工具生成一个很小的 <c>cs/ForgeSfx.cs</c>（<c>&lt;角色&gt;ForgeSfx.Play(路径, 音量, replaceable: true)</c>），
    /// 里面记着「还在放、且允许被顶替」的那几个播放器，下一段开始时先把它们停掉并回收。</para>
    ///
    /// <para>⚠ <b>红线</b>：整份存档**没有任何一条**勾这个时，生成端一个字都不多写
    /// （不生成 <c>cs/ForgeSfx.cs</c>、播放代码和以前逐字一致）。
    /// 这个字段**默认 false 时不写进 JSON**（<see cref="JsonIgnoreCondition.WhenWritingDefault"/>）——
    /// 锻造图节点的快照里也内嵌着 <c>EffectSpec</c>，写进去会让老存档的 <c>CET:ForgeGraph</c> 那一行变样。</para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SfxReplace
    {
        get => _sfxReplace;
        set
        {
            if (!Set(ref _sfxReplace, value)) return;
            Raise(nameof(Display));
            Raise(nameof(ShowSfxReplaceRow));
        }
    }

    /// <summary>界面用：「被下一个自定义音频顶替」那个勾只在「播放音频文件」这一档显示。</summary>
    [JsonIgnore]
    public bool ShowSfxReplaceRow => IsPlaySoundFile;

    private bool _sfxSameGroupOnly;

    /// <summary>
    /// V0.2.265（用户要求）：「仅能被**下个同组音频**顶替」。
    ///
    /// <para>配合 <see cref="SfxReplace"/> 使用：勾了「被下一个自定义音频顶替」之后，这一段音频还在响时，
    /// 默认**任何**下一段自定义音频都会把它停掉；再勾上这个，就只有**和它同属一个组**的音频能顶替它
    /// （组在「组」页那个「这个组里有哪些音频」里勾）。</para>
    ///
    /// <para>它属于哪一组不写在这一条效果上：同一个音频文件可能在多处播放，所以归属记在
    /// <see cref="CardGroupSpec.AudioIds"/>（组那边）—— 生成时写死进 <c>cs/ForgeSfx.cs</c> 的对照表。</para>
    /// </summary>
    public bool SfxSameGroupOnly
    {
        get => _sfxSameGroupOnly;
        set
        {
            if (!Set(ref _sfxSameGroupOnly, value)) return;
            Raise(nameof(Display));
            Raise(nameof(ShowSfxSameGroupRow));
        }
    }

    /// <summary>界面用：「仅能被下个同组音频顶替」这个勾同样只在「播放音频文件」这一档显示。</summary>
    [JsonIgnore]
    public bool ShowSfxSameGroupRow => IsPlaySoundFile;

    /// <summary>这条音效的中文名（没选 = 「（没选音效）」，不在候选表里就原样显示路径）。</summary>
    [JsonIgnore]
    public string SfxZh => EffectCatalog.SfxDisplay(_sfx);

    /// <summary>
    /// 「数值」那一栏在「播放音效」上的含义 = **音量百分比**：留空 / 0 = 100%（正常音量），
    /// 50 = 一半，200 = 两倍（生成 <c>SfxCmd.Play(路径, 1.5f)</c> 那个 1.5）。
    /// 别的种类照旧（数值 = 力度 / 张数…）。
    /// </summary>
    [JsonIgnore]
    public decimal SfxVolumePercent => Amount <= 0m ? 100m : Amount;


    /// <summary>
    /// 「数值 0 = 这张牌自己」——**只有「丢弃卡牌」这一档**（本批新增）。
    /// 只用来判断**生成语义**（0 这一档不挑牌、不声明张数变量），**不要拿它做界面显隐** ——
    /// 界面按它藏掉一行，用户就填不了了（实测报过「怎么不能填范围了」）。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool DiscardSelfOnly => Kind == "DiscardCard" && Amount <= 0m && !AmountIsXEffective;

    /// <summary>
    /// 这一条效果的可选牌堆（界面下拉的候选）。
    /// 「丢弃卡牌」只有手牌 / 抽牌堆（见 <see cref="EffectCatalog.DiscardPiles"/>）；
    /// 别的效果还是三摞都能选。
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<PileChoiceOption> SelectPileChoices =>
        Kind == "DiscardCard" ? EffectCatalog.DiscardPiles : EffectCatalog.SelectPiles;

    /// <summary>
    /// 「从哪里选牌」那一行的**标签**（措辞按种类变）。
    ///
    /// <para>⚠ 这里必须**短**（V0.2.151 修的坑）：效果栏是一个两列 Grid，第一列是 <c>Width="Auto"</c>
    /// 的标签列、第二列是 <c>*</c> 的输入列 —— 标签文字有多宽，第一列就有多宽，第二列只能吃剩下的。
    /// 以前这里写的是「从哪里选牌（消耗 / 变化 / 复制…；选「牌顶」= 抽牌堆最上面那张，不用挑）」，
    /// 34 个字把第一列撑到约 500px，于是整个编辑区第二列只剩 40px：
    /// 「选牌方式 / 从哪里选牌」下拉变成一条缝、「生效次数（公式）」被挤出可视区
    ///（用户报「次数公式怎么不见了」「放置卡牌效果怎么没反应」就是它）。
    /// 详细说明都放在那一行 ComboBox 的 ToolTip 里（鼠标停上去就有），标签本身保持两个字到六个字。</para>
    /// </summary>
    [JsonIgnore]
    public string SelectPileLabel => Kind switch
    {
        "DiscardCard" => "从哪里丢弃",
        "UpgradeCard" => "从哪里升级",
        _ => "从哪里选牌",
    };

    // ===== 给予卡牌关键词（GiveKeyword）=====
    private string _givenKeyword = "";

    /// <summary>
    /// 「给予卡牌关键词」要给的关键词：本体关键词的枚举名（<c>Retain</c> / <c>Exhaust</c> …）
    /// 或者自定义关键词的英文标识（键）。
    /// </summary>
    public string GivenKeyword
    {
        get => _givenKeyword;
        set { if (Set(ref _givenKeyword, value ?? "")) { Raise(nameof(IsCustomGivenKeyword)); Raise(nameof(Display)); } }
    }

    private bool _tempKeyword;

    /// <summary>
    /// 「是否为临时关键词」：勾上 = 这个关键词**只这一回合**有效，回合结束自动去掉。
    /// 保留 / 奇巧走本体的单回合标记（<c>GiveSingleTurnRetain/Sly</c>），其余的靠生成的
    /// <c>&lt;角色&gt;ForgeTempKeywordPower</c> 在回合结束时摘掉。
    /// </summary>
    public bool TempKeyword
    {
        get => _tempKeyword;
        set { if (Set(ref _tempKeyword, value)) Raise(nameof(Display)); }
    }

    /// <summary>这条效果是在「给予卡牌关键词」。</summary>
    [JsonIgnore]
    public bool IsGiveKeyword => Kind == "GiveKeyword";

    // ===== 抽牌 / 拿牌时「为抽到的牌加关键词」（V0.2.220，用户要求）=====
    private string _drawnKeyword = "";

    /// <summary>
    /// 「为抽到 / 拿到的牌添加关键词」（V0.2.220，用户要求：抽牌 / 拿牌这些效果下多一条选项）：
    /// 空 = 不加（默认档，老存档就是它 —— 生成文本一个字符都不变）；
    /// 值是本体关键词的枚举名（<c>Retain</c> / <c>Exhaust</c> …）或者自定义关键词的英文标识（键）。
    ///
    /// <para>作用的牌 = **这一条效果真的抽到 / 拿到手的那几张**（不是整个手牌），所以用的是
    /// <c>CardPileCmd.Draw</c> / <c>CardSelectCmd</c> 的返回值。实现在
    /// <c>CSharpCodeGen.EmitDrawnKeyword</c>，复用「给予卡牌关键词」那一套（本体关键词走
    /// <c>AddKeyword</c>，自定义关键词走 <c>AddGivenCustomKeyword</c> + 卡面补丁）。</para>
    /// </summary>
    public string DrawnKeyword
    {
        get => _drawnKeyword;
        set
        {
            if (Set(ref _drawnKeyword, value ?? ""))
            {
                Raise(nameof(HasDrawnKeyword));
                Raise(nameof(ShowDrawnKeywordTempRow));
                Raise(nameof(DrawnKeywordZh));
                Raise(nameof(Display));
            }
        }
    }

    /// <summary>选了「为抽到的牌加关键词」。</summary>
    [JsonIgnore]
    public bool HasDrawnKeyword => !string.IsNullOrWhiteSpace(_drawnKeyword);

    private bool _drawnKeywordTemp;

    /// <summary>
    /// 加的是**临时**关键词（只这一回合有效，回合结束自动摘掉）：
    /// 保留 / 奇巧走本体的单回合标记，其余（含自定义）靠生成的那个临时关键词 Power。
    /// </summary>
    public bool DrawnKeywordTemp
    {
        get => _drawnKeywordTemp;
        set { if (Set(ref _drawnKeywordTemp, value)) { Raise(nameof(Display)); } }
    }

    /// <summary>
    /// 界面：「为抽到的牌添加关键词」那一行要不要显示（只有抽牌 / 拿牌 / 填满手牌这几档）。
    /// ⚠ **不带** IsForgeChain：锻造图那条链也真的读它（ForgeGraphGen.EmitEffect 走 EmitDraw / EmitDrawnKeyword），
    /// 和「生成 / 变化出来的卡加关键词」不一样（那一项在图上没有入口）。
    /// </summary>
    [JsonIgnore]
    public bool ShowDrawnKeywordRow => EffectFieldPolicy.UsesDrawnKeyword(Kind);

    /// <summary>界面：「临时关键词」那个勾要不要显示（选了关键词才有意义）。</summary>
    [JsonIgnore]
    public bool ShowDrawnKeywordTempRow => ShowDrawnKeywordRow && HasDrawnKeyword;

    /// <summary>「为抽到的牌加的关键词」在界面上的名字（本体关键词中文名 / 自定义关键词的键）。</summary>
    [JsonIgnore]
    public string DrawnKeywordZh =>
        string.IsNullOrWhiteSpace(_drawnKeyword) ? "?" : EffectCatalog.VanillaKeywordZh(_drawnKeyword);

    // ===== 强化指定卡牌（BoostCard，像本体「精准」对「小刀」）=====
    private string _boostStat = "Damage";

    /// <summary>强化的是伤害还是格挡（见 <see cref="EffectCatalog.BoostStats"/>）。</summary>
    public string BoostStat
    {
        get => _boostStat;
        set { if (Set(ref _boostStat, value)) { Raise(nameof(BoostStatZh)); Raise(nameof(Display)); } }
    }

    /// <summary>界面 / 描述里用：这条效果强化的是伤害还是格挡。</summary>
    [JsonIgnore]
    public string BoostStatZh => BoostStat == "Block" ? "格挡" : "伤害";

    /// <summary>这条效果是在「强化指定卡牌」。</summary>
    [JsonIgnore]
    public bool IsBoostCard => Kind == "BoostCard";

    /// <summary>
    /// 要给的关键词在界面上的名字：本体关键词中文名（Retain → 保留），
    /// 自定义关键词显示用户填的**英文标识**（Profile 层拿不到关键词列表，界面上的下拉会显示中文名）。
    /// </summary>
    [JsonIgnore]
    public string GivenKeywordZh =>
        string.IsNullOrWhiteSpace(_givenKeyword) ? "?"
        : EffectCatalog.VanillaKeywordZh(_givenKeyword);

    /// <summary>
    /// 给的是自定义关键词（不是本体那 7 个）。看名字就能判断：本体关键词都是英文枚举名。
    /// </summary>
    [JsonIgnore]
    public bool IsCustomGivenKeyword =>
        !string.IsNullOrWhiteSpace(_givenKeyword) && !EffectCatalog.IsVanillaKeywordName(_givenKeyword);

    /// <summary>本体「缓慢」是 10% 一档（内部 SlowAmount 是整数）：四舍五入到最近的 10%，返回实际生效的百分比。</summary>
    [JsonIgnore]
    public int SlowPercentEffective =>
        SlowPercent <= 0 ? 0 : (int)Math.Round(SlowPercent / 10m, MidpointRounding.AwayFromZero) * 10;

    /// <summary>界面上「直接把「缓慢」设成 N%」那一行只在选了「缓慢」时显示。</summary>
    [JsonIgnore]
    public bool IsSlowPower => string.Equals(PowerId?.Trim(), "SlowPower", StringComparison.Ordinal);

    /// <summary>
    /// 这条效果是否用到 X（生成代码时要先取 X 值 —— 也就是那句 <c>int x = ResolveEnergyXValue();</c>）。
    ///
    /// <para>⚠ V0.2.131：三个「= X」勾选框从界面上撤掉了，「X」改在公式框里写。
    /// <see cref="AmountIsXEffective"/> 认「非那 5 种」效果上写的光秃秃 <c>X</c>（走 <c>x</c> 这个变量，
    /// 与老勾选框**逐字等价**）；生效次数 / 命中次数那两处走既有的「次数公式」通道
    /// （<c>X</c> 被解析成 <c>__X</c>，由公式序幕声明），**不算**在 <see cref="UsesX"/> 里 ——
    /// 否则会多生成一个没人用的 <c>int x</c>（CS0219）。</para>
    /// </summary>
    [JsonIgnore]
    public bool UsesX => AmountIsXEffective || TimesIsX || RepeatIsX;

    /// <summary>
    /// 这条效果是「召唤伙伴 / 伙伴攻击」（做的是宠物，不是自己 / 敌人）。
    /// 界面列表里不要把 TargetSide 显示成「→ 自己」误导人；生成代码时「宠物在不在场」的守卫也按它判断。
    /// </summary>
    [JsonIgnore]
    public bool PetAction => Kind is "SummonPet" or "PetAttack" || IsPetEffect;

    /// <summary>
    /// 这条效果是不是「转化为先古卡」（界面据此显示那张配对表；生成时也只看这个效果种类）。
    /// </summary>
    [JsonIgnore]
    public bool IsAncientTranscend => Kind == "AncientTranscend";
    /// <summary>
    /// 这条效果是不是「转化为先古遗物」（界面据此把「目标卡」那一栏换成「目标遗物」）。
    /// </summary>
    [JsonIgnore]
    public bool IsAncientRelicSwap => Kind == "AncientRelicSwap";

    /// <summary>
    /// 这条效果是**新增的那批宠物效果**（做的是宠物，不是自己 / 敌人）：
    /// 伙伴攻击（按生命值算）/ 治疗伙伴 / 伙伴失去生命 / 伙伴最大生命 / 牺牲伙伴 / 给伙伴施加状态 / 伙伴替主人承伤 开·关。
    ///
    /// 和 <see cref="PetAction"/> 的区别只在于**要不要先找到宠物**：
    ///   · <c>SummonPet</c> 不需要（Summon 内部自己找）；
    ///   · 这一批（还有 <c>PetAttack</c>）都需要 —— 宠物不在场时整条效果安全跳过（生成 <c>if (__pet is not null)</c>）。
    /// 两个属性都要覆盖这 10 个新 Kind，否则界面上的「召唤物（哪一只）」下拉不会显示。
    /// </summary>
    [JsonIgnore]
    public bool IsPetEffect => Kind is
        "PetDamageByMaxHp" or "PetDamageByCurHp" or "PetDamageByMissingHp"
        or "PetHeal" or "PetLoseHp" or "PetGainMaxHp" or "PetSacrifice" or "PetApplyPower"
        or "PetGuardOn" or "PetGuardOff";

    /// <summary>
    /// 这条效果是「牺牲伙伴」—— 界面上只有它才显示「收益 / 公式 / 倍率」那三行。
    ///
    /// 注意：属性名是**界面 XAML 直接绑的**（<c>Visibility="{Binding IsPetSacrifice, …}"</c>）。
    /// 以前这个属性忘了定义，WPF 绑定失败会静默退回默认值「可见」，于是不管选哪种效果都显示那三行（用户报过）。
    /// 另外 <see cref="Kind"/> 的 setter 必须 Raise 它，否则切换效果种类时界面不刷新。
    /// </summary>
    [JsonIgnore]
    public bool IsPetSacrifice => Kind == "PetSacrifice";

    /// <summary>
    /// 这条「牺牲伙伴」的收益是「获得增益 / 减益」（<c>Gain == "Power"</c>）—— 界面上只有这一档
    /// 才显示「状态（牺牲后获得）」那一行。
    ///
    /// 和 <see cref="IsPetSacrifice"/> 一样是**界面 XAML 直接绑的**
    /// （<c>Visibility="{Binding IsPetSacrificePower, …}"</c>），而且 <see cref="Kind"/> /
    /// <see cref="PetSacrificeGain"/> 的 setter 都必须 Raise 它，否则换档时那一行不会出现 / 不会消失。
    /// </summary>
    [JsonIgnore]
    public bool IsPetSacrificePower => Kind == "PetSacrifice" && PetSacrificeGain == "Power";

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

    // ===== 牺牲伙伴（PetSacrifice）：收益类型 + 收益公式 =====
    private string _petSacrificeGain = "Block";
    private string _petSacrificeFormula = "MaxHp";
    private decimal _petSacrificeMultiplier = 3m;

    /// <summary>
    /// 「牺牲伙伴」的收益类型：<c>Block</c>（格挡）/ <c>Damage</c>（伤害）/
    /// <c>Power</c>（获得增益 / 减益 —— 层数按下面的收益公式算，状态挂在自己身上，<see cref="PowerId"/> 就是它）。
    /// 三种都是「先算收益、再杀宠物」（顺序不能反：宠物死了就取不到生命值）。
    /// </summary>
    public string PetSacrificeGain
    {
        get => _petSacrificeGain;
        set
        {
            if (!Set(ref _petSacrificeGain, NormalizeGain(value))) return;
            Raise(nameof(Display));
            // 收益换成「获得增益 / 减益」（或换走）时，「状态（牺牲后获得）」那一行要跟着显 / 隐。
            Raise(nameof(IsPetSacrificePower));
            // V0.2.200：收益换成 / 换走「造成伤害」时，「作用对象」那一行要跟着显 / 隐
            //（只有在伤害这一档才有得挑 —— 格挡 / 状态都挂在自己身上）。
            Raise(nameof(IsPetSacrificeDamage));
            Raise(nameof(UsesTargetSideEffective));
            Raise(nameof(ShowsTargetSideSuffix));
            Raise(nameof(ShowTargetSideRow));
        }
    }

    /// <summary>
    /// 「牺牲伙伴」的收益公式：<c>Fixed</c>（固定 <see cref="Amount"/>）/
    /// <c>MaxHp</c>（最大生命 × <see cref="PetSacrificeMultiplier"/>）/
    /// <c>CurHp</c>（当前生命）。
    ///
    /// 为什么要有这个下拉：本体的格挡计算变量有两套 —— 固定值走普通 <c>BlockVar</c> /
    /// <c>DamageVar</c>；按生命值算必须走 <c>CalculatedBlockVar</c> / <c>CalculatedDamageVar</c>
    /// 的「计算三件套」（CalculationBase + CalculationExtra/ExtraDamage + Calculated*），
    /// 不然本体在 <c>CalculatedVar.GetExtraVar()</c> 处直接 <c>KeyNotFoundException</c>。
    /// </summary>
    public string PetSacrificeFormula
    {
        get => _petSacrificeFormula;
        set
        {
            if (!Set(ref _petSacrificeFormula, NormalizeFormula(value))) return;
            Raise(nameof(Display));
            // 换公式要通知界面：只有「最大生命 × 倍率」才显示倍率那一栏、说明文字也跟着变。
            // （以前只 Raise 了 Display，于是切到「固定 N」后倍率栏还赖在界面上 —— 用户报过。）
            Raise(nameof(PetSacrificeUsesMultiplier));
            Raise(nameof(PetSacrificeFormulaZh));
        }
    }

    /// <summary>「牺牲伙伴」按最大生命算时的倍率（默认 3）。</summary>
    public decimal PetSacrificeMultiplier
    {
        get => _petSacrificeMultiplier;
        set { if (Set(ref _petSacrificeMultiplier, value)) Raise(nameof(Display)); }
    }

    /// <summary>
    /// 收益类型的归一化：认 <c>Damage</c> / <c>Power</c>，其余（含老存档里的 <c>Block</c>）一律当格挡。
    /// 大小写不敏感（手改过 JSON 的老存档写成 <c>damage</c> 也照样认）。
    /// </summary>
    private static string NormalizeGain(string? v) => v?.Trim().ToLowerInvariant() switch
    {
        "damage" => "Damage",
        "power" => "Power",
        _ => "Block",
    };

    private static string NormalizeFormula(string? v) => v?.Trim() switch
    {
        "Fixed" => "Fixed",
        "CurHp" => "CurHp",
        _ => "MaxHp",
    };

    /// <summary>「牺牲伙伴」收益的中文（列表里显示）。</summary>
    [JsonIgnore]
    public string PetSacrificeGainZh => PetSacrificeGain switch
    {
        "Damage" => "伤害",
        "Power" => "增益 / 减益",
        _ => "格挡",
    };

    /// <summary>「牺牲伙伴」收益公式的中文短说明（列表 / 界面提示用）。</summary>
    [JsonIgnore]
    public string PetSacrificeFormulaZh => PetSacrificeFormula switch
    {
        "Fixed" => $"固定 {Amount:0.##}",
        "CurHp" => "等于伙伴的当前生命值",
        _ => $"最大生命 × {PetSacrificeMultiplier:0.##}",
    };

    /// <summary>要不要显示「倍率」那一栏（只有「最大生命 × 倍率」这条公式用得到）。</summary>
    [JsonIgnore]
    public bool PetSacrificeUsesMultiplier => Kind == "PetSacrifice" && PetSacrificeFormula == "MaxHp";

    /// <summary>GenerateCard 用：生成的卡放到哪 —— 对应的 PileType 名字。</summary>
    [JsonIgnore]
    public string SpawnToPile => SpawnTo switch
    {
        "Draw" => "Draw",
        "Discard" => "Discard",
        // 消耗牌堆：也是本体的「战斗内牌堆」（PileType.IsCombatPile() 认它），
        // 所以走同一套 CardPileCmd 就能把生成出来的牌直接放进去（不经过手牌 / 抽牌堆）。
        "Exhaust" => "Exhaust",
        _ => "Hand",
    };

    /// <summary>选牌方式的中文（列表文字用）。</summary>
    [JsonIgnore]
    public string CardPickZh => CardPick == "Chosen" ? "自己选" : "随机";

    /// <summary>
    /// 遗物 / 药水列表用的显示文本：它们不能升级，所以不显示「（升级 +N）」这种括号
    /// （老存档里可能残留升级增量，这里也不显示出来）。
    ///
    /// <para>V0.2.141：「数值公式 + 升级增量」现在写的是 <c>（升级后 N）</c> —— 两种尾巴一起剥掉，
    /// 否则遗物 / 药水列表会莫名其妙多出一段「升级后」。剥不掉就原样返回（和以前一样，不乱删字符）。</para>
    /// </summary>
    [JsonIgnore]
    public string DisplayPlain
    {
        get
        {
            string s = Display;
            if (UpgradeAmount == 0) return s;
            s = s.Replace($"（升级 {(UpgradeAmount > 0 ? "+" : "")}{UpgradeAmount}）", "");
            return Generation.CSharpCodeGen.ConstantFormulaUpgradeValue(null, this) is { } after
                ? s.Replace($"（升级后 {after:0.##}）", "")
                : s;
        }
    }

    /// <summary>
    /// 新增的那批宠物效果在界面列表里的中文名（不是这批就返回 null，走原来的 Kind 回退）。
    /// 「伙伴」两个字在列表里用**选中的那只召唤物的类名**代替（Profile 层拿不到中文名，
    /// 那要读召唤物列表；卡面描述由 <c>LocalizationGen</c> 换成真正的中文名）。
    /// </summary>
    [JsonIgnore]
    public string? PetKindZh
    {
        get
        {
            string who = PetSummonTag.Length > 0 ? PetSummonTag : "伙伴";
            return Kind switch
            {
                "PetDamageByMaxHp" => $"{who}攻击（按最大生命值）",
                "PetDamageByCurHp" => $"{who}攻击（按当前生命值）",
                "PetDamageByMissingHp" => $"{who}攻击（按已损失的生命值）",
                "PetHeal" => $"治疗{who}",
                "PetLoseHp" => $"{who}失去生命",
                "PetGainMaxHp" => $"{who}最大生命",
                "PetApplyPower" => $"给{who}施加 {PowerId ?? "?"}",
                "PetGuardOn" => $"{who}替主人承伤（开）",
                "PetGuardOff" => $"{who}替主人承伤（关）",
                _ => null,
            };
        }
    }

    [JsonIgnore]
    public string Display
    {
        get
        {
            string when = NextTurn ? "（下回合）" : "";
            // 「取几张 = 全部」那一档**不弹选牌界面、也不是随机**：选牌方式不生效，
            // 所以下面的摘要里一律不写它（和 V0.2.161 同一条规矩：不生效的栏位，文案里也不写）。
            string pickZh = CountIsAll ? "" : CardPickZh;
            static string PickPart(string p) => p.Length == 0 ? "" : $" ｜ {p}";
            // 生成 / 消耗 / 变化卡牌：把「哪张卡、放哪、怎么选」显示出来，方便一眼看出有没有填漏
            string extra = Kind switch
            {
                // 「按范围随机」时不显示目标卡（那时候用的是范围限定），多选1 的候选张数也写出来
                "GenerateCard" => IsSpawnRandom
                    ? $" ｜ 生成「{SpawnFilterZh}」{(HasSpawnChoice ? $"（{SpawnChoice:0.##} 张中选一张）" : "")} → 放入{EffectCatalog.SpawnTargetZh(SpawnToPile)}"
                    : $" ｜ 生成 {(SpawnCardId is { Length: > 0 } sc ? sc : "（未填→Shiv）")} → 放入{EffectCatalog.SpawnTargetZh(SpawnToPile)}",
                "TransformCard" => $" ｜ 变为 {(SpawnCardId is { Length: > 0 } tc ? tc : "随机卡")}{(pickZh.Length == 0 ? "" : $" ｜ {pickZh}")}",
                "ExhaustCard" => $"{PickPart(pickZh)} ｜ 从{SelectPileZh}",
                // 丢弃：把「丢哪一摞 / 怎么选」显示出来（两种牌堆的行为差别挺大，值得一眼看到）
                "DiscardCard" => $" ｜ 丢弃{PickPart(pickZh)} ｜ 从{SelectPileZh}",
                // 升级卡牌 / 预见：都是「选 N 张牌」，把从哪一摞 / 怎么看写出来
                "UpgradeCard" => $" ｜ 升级{PickPart(pickZh)} ｜ 从{SelectPileZh}",
                // 复制卡牌：选几张 / 从哪一摞 / 每张复制几份（份数是它独有的那一栏）
                "CopyCard" => $" ｜ 复制{PickPart(pickZh)} ｜ 从{SelectPileZh} ｜ 每张 {Math.Max(1, Copies)} 份",
                "Scry" => " ｜ 预见（看抽牌堆顶，可丢任意张）",
                "AddCardGlobal" => $" ｜ 加进牌组：{(SpawnCardId is { Length: > 0 } ac ? ac : "（未填→Shiv）")}",
                "TransformCardGlobal" => Amount <= 0m && !AmountIsXEffective
                    ? $" ｜ 牌组里的这张牌变为 {(SpawnCardId is { Length: > 0 } tgc0 ? tgc0 : "随机卡")}（数值 0 = 这张牌自己）"
                    : $" ｜ 牌组里的牌变为 {(SpawnCardId is { Length: > 0 } tgc ? tgc : "随机卡")}{(pickZh.Length == 0 ? "" : $" ｜ {pickZh}")}",
                "RemoveCardGlobal" => Amount <= 0m && !AmountIsXEffective
                    ? " ｜ 从牌组删掉这张牌自己（数值 0 = 这张牌自己）"
                    : $" ｜ 从牌组删牌{PickPart(pickZh)}",
                "UpgradeCardGlobal" => Amount <= 0m && !AmountIsXEffective
                    ? " ｜ 升级牌组里的这张牌（数值 0 = 这张牌自己）"
                    : $" ｜ 升级牌组里的牌{PickPart(pickZh)}",
                // 需求 ⑨：获得药水 —— 把「哪一瓶 / 随机」写出来（只看「获得药水」看不出配了哪一档）
                "GainPotion" => IsPotionRandom
                    ? " ｜ 随机一瓶药水"
                    : $" ｜ 获得「{(string.IsNullOrWhiteSpace(PotionId) ? "（未选）" : PotionNameZh)}」",
                _ => "",
            };
            // 需求 ⑦：「生成 / 变化出来的卡带上关键词」在列表里也写出来（只说一句「生成卡牌」看不出配了它）
            if (UsesSpawnOptions && HasSpawnKeyword)
                extra += $" ｜ 生成出来的卡带上「{SpawnKeywordZh}」";
            // V0.2.220：「为抽到的牌加关键词」（抽牌 / 拿牌 / 填满手牌）—— 同理，列表里要看得出来
            if (HasDrawnKeyword)
                extra += $" ｜ 抽到的牌带上「{DrawnKeywordZh}」" + (DrawnKeywordTemp ? "（临时）" : "");
            string side = TargetSide switch
            {
                "Enemy" => "单体敌人",
                "AllEnemies" => "全体敌人",
                "RandomEnemies" => RepeatIsX ? "随机 X 个敌人" : $"随机 {RepeatCount} 个敌人",
                // V0.2.200：这几档以前一律落到「自己」（「造成伤害 + 全体单位」也会印成「→ 自己」，
                // 明明是照全场打的）。既然「牺牲伙伴 → 伤害」现在能挑这几档，就得写对。
                "AllUnits" => "全体单位",
                "AllAllies" => AllAlliesIncludeSelf ? "全体友方（含自己）" : "全体友方",
                "Ally" => "指定队友",
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
                // 击晕：本体没有这个状态，它是怪物意图（本体卡「口哨」那种），所以单列一条
                "Stun" => "击晕",
                // 强化指定卡（像「精准」）：数值 = 加多少，目标卡 = 强化哪张
                "BoostCard" => $"强化「{(string.IsNullOrWhiteSpace(SpawnCardId) ? "?" : SpawnCardId)}」的{BoostStatZh} "
                    + (Amount >= 0 ? "+" : "") + Amount.ToString("0.##"),
                // 临时增益（本回合 +X，回合结束撤掉）：界面上写清「临时」，别和普通的「施加」看混；
                // 「下回合开始时才撤」那一档也说清（否则两档在界面上长得一样）
                "TempPower" => (TempPowerReverse == TempPowerReverses.NextTurnStart
                    ? "临时增益（下回合开始时撤）"
                    : "临时增益（本回合）") + (PowerId ?? "?"),
                // 给予关键词：数值 = 选几张牌（0 = 这张牌自己），后面跟关键词名与是否临时
                // （本体关键词显示中文名：Retain → 保留；自定义关键词显示它自己的名字）
                "GiveKeyword" => (CountIsAll ? "所有" : Amount <= 0 ? "自己" : (CardPick == "Chosen" ? "自己选 " : "随机 ") + $"{Amount:0.##} 张")
                    + "给予关键词「" + GivenKeywordZh + "」"
                    + (TempKeyword ? "（临时）" : ""),
                "AddCardGlobal" => "获得卡牌（全局）",
                "TransformCardGlobal" => "变化卡牌（全局）",
                "RemoveCardGlobal" => "删除卡牌（全局）",
                "UpgradeCardGlobal" => "升级卡牌（全局：牌组里的牌永久升级）",
                "CardReward" => "获得卡牌奖励",
                // 获得药水（需求 ⑨）：指定 / 随机两档都在这里说清楚（药水栏满 = 什么都不发生）
                "GainPotion" => IsPotionRandom
                    ? "获得随机药水"
                    : "获得指定药水：" + (string.IsNullOrWhiteSpace(PotionId) ? "（还没选「目标药水」）" : PotionNameZh),
                // 毒性爆发 / 大限已至：本体那两张牌的效果（界面上把关键说明写出来）
                "Outbreak" => "毒性爆发（全体上毒并立即触发）",
                "TimesUp" => "大限已至（伤害 = 目标灾厄层数）",
                // 刮削（本体卡「刮削 Scrape」）：界面上把「抽几张 → 对什么条件的牌做什么」写出来，
                // 否则只看得到 Kind 名（条件和操作各有各的档，光看「刮削」两个字分不清）
                "ScrapeDraw" => $"刮削（抽 {Amount:0.##} 张 → 对其中「{ScrapeCondZh}」的牌{ScrapeOpZh}）",
                // V0.2.154：播放音效（数值那一栏 = 音量%）
                "PlaySfx" => $"播放音效：{SfxZh}" + (SfxVolumePercent == 100m ? "" : $"（音量 {SfxVolumePercent:0.##}%）"),
                // V0.2.155：播放上传的音频文件（这里拿不到整份配置，直接显示文件名 / Key）
                "PlaySoundFile" => $"播放音频：{(string.IsNullOrWhiteSpace(Sfx) ? "（没选音频）" : Sfx.Trim())}"
                    + (SfxVolumePercent == 100m ? "" : $"（音量 {SfxVolumePercent:0.##}%）")
                    + (SfxReplace ? "（会被下一个自定义音频顶替）" : ""),
                // 召唤伙伴 / 伙伴攻击：作用对象是宠物，不是「自己 / 敌人」，别显示那个「→ 自己」。
                // 这里写的是选中那只召唤物的**类名**（Profile 层拿不到中文名 —— 那要读召唤物列表，
                // 由 LocalizationGen / 界面负责翻译成中文名）。
                "SummonPet" => $"召唤{(PetSummonTag.Length > 0 ? PetSummonTag : "伙伴")}"
                    + (Amount > 0 ? $"{Amount:0.##} 点生命" : "（用配置的血量）"),
                "PetAttack" => $"{(PetSummonTag.Length > 0 ? PetSummonTag : "伙伴")}攻击 {Amount:0.##}",
                // ===== 新增的那批宠物效果 =====
                // 「牺牲伙伴」的收益公式要写出来（固定 N / 最大生命 × 倍率 / 当前生命），否则界面上只看得到 Kind 名。
                "PetSacrifice" => $"{(PetSummonTag.Length > 0 ? PetSummonTag : "伙伴")}牺牲换{PetSacrificeGainZh}（{PetSacrificeFormulaZh}）",
                _ => PetKindZh ?? Kind,
            };
            // 召唤 / 伙伴攻击：kind 文案里已经写过数值了（「召唤小七 12 点生命」「小七攻击 6」），
            // 行尾再统一追加一次 Amount 会变成「… 12 点生命 12」—— 用户实测报过「多打了一个数值」。
            // 牺牲伙伴的数值已经写在公式说明里（固定 3 / 最大生命 × 3），同理不重复追加。
            string amountPart = PetAction && Kind != "PetHeal" && Kind != "PetLoseHp" && Kind != "PetGainMaxHp"
                ? ""
                // 「取几张 = 全部 / 任意张」（V0.2.163 / V0.2.133）：列表里写「所有 / 任意」而不是那个**不生效**的数字
                //（全部档不弹选牌界面、任意档数字由玩家定，写「3」会让人以为就是 3 张）。
                : CountHasNoNumber
                    ? (CountIsAll ? " 所有" : " 任意张")
                // 「数值公式」：列表里显示**公式本身**，而不是那个只用来占位的固定数字 ——
                // 显示占位值等于骗人（界面上写着「造成伤害 6」，打出时算的却是 M*3）。
                : UsesAmountFormula
                    ? $" 数值公式 {AmountFormulaText}"
                    : $" {(AmountIsXEffective ? "X" : Amount.ToString("0.##"))}";
            // 写了公式时不再显示「（升级 +N）」：公式取代了数值，升级增量对这条不生效
            //（校验器也会给一条提示说清这件事）。
            //
            // ⚠ V0.2.141：**纯常数**公式是例外 —— 生成端对它写的是
            // `(base.IsUpgraded ? 常数+增量 : 常数)`，也就是「升级增量**确实**会改打出时的数值」。
            // 这里把**升级后的最终值**写出来（用户实测报的「在生成器填负值的力量不生效」：
            // 增量 2 把公式 -2 抵成了 0，升级过的那张打出去完全没效果，而列表里当时一个字都不说）。
            // 真·现算公式（`M*2`）折不出常数 → 返回 null，照旧只显示公式本身（写个假数字不如不写）。
            string upgradePart = UpgradeAmount != 0 && !AmountIsXEffective
                ? Generation.CSharpCodeGen.ConstantFormulaUpgradeValue(null, this) is { } afterUpgrade
                    ? $"（升级后 {afterUpgrade:0.##}）"
                    : UsesAmountFormula ? "" : $"（升级 {(UpgradeAmount > 0 ? "+" : "")}{UpgradeAmount}）"
                : "";
            return $"{when}{kind}{amountPart}{upgradePart}"
                 + $"{(TimesIsX ? " ×X 次" : Times > 1 ? $" ×{Times} 次" : "")}"
                 + (UsesAnyCountFormula ? CountFormulaListText : "")
                 + $"{(RepeatIsX ? "（命中 X 次）" : "")}{ChanceText}{SlowPercentText}"
                 // 召唤 / 伙伴攻击打的是宠物，没有「作用对象」这一说 —— 加了这个尾巴会让人以为「→ 自己」是给宠物加血。
                 // V0.2.161（用户实测报的「抽卡、获得能量之类的效果种类的对象没了，但是显示的是对敌人」）：
                 // 「抽牌 / 获得能量 / 生成卡牌…」这些**生成端根本不读作用对象**的种类，界面上那一行已经藏起来了
                 //（V0.2.150 按种类裁剪），可行尾还印着「→ 单体敌人」—— 因为 `_targetSide` 的**默认值就是 "Enemy"**。
                 // 判据必须和显隐**同一份**（`ShowsTargetSideSuffix` = `UsesTargetSideEffective` + 宠物例外）：
                 // 「界面上显示的栏位，生成端一定读；藏起来的，生成端一定不读」—— 描述里也不许写。
                 // V0.2.200：「牺牲伙伴 → 造成伤害」是宠物类里的例外 —— 那一下真的打在敌人身上，要写。
                 + (ShowsTargetSideSuffix ? $" → {side}" : "") + extra + CardScopeListText
                 + (Condition.IsNone ? "" : $"  ｜ 条件：{Condition.DisplayShort}");
        }
    }
}

/// <summary>
/// 一张卡（普通卡 / 诅咒卡 / 先古卡 / 状态卡都是它，靠 <see cref="CardType"/> / <see cref="Rarity"/> 分流）。
///
/// <para>V0.2.140 起也实现 <see cref="ISlotRow"/>：三个卡牌列表页（卡牌 / 诅咒卡 / 先古卡）的左侧列表
/// 照遗物页 / 药水页那套改成「每 5 个一块 + 末尾空槽行」，这两边的行（真卡 = <see cref="CardSpec"/>、
/// 空槽 = 界面那边的 <c>SlotPlaceholder</c>）要塞进同一个 <c>ObservableCollection&lt;ISlotRow&gt;</c>。
/// 和 <see cref="RelicSpec"/> / <see cref="PotionSpec"/> 一模一样：<see cref="IsPlaceholder"/> 恒 false。</para>
/// </summary>
public sealed class CardSpec : SpecBase, ISlotRow
{
    private string _name = "新卡";
    private string _className = "";
    private string _cardType = "Attack";
    private string _rarity = "Common";
    private int _cost = 1;
    private bool _inStartingDeck;
    private int _startingCopies = 1;
    private bool _inCardPool = true;
    private bool _infinitelyUpgradable;
    private bool _exhausts;
    private bool _ethereal;
    private bool _innate;
    private bool _retain;
    private bool _unplayable;
    private bool _sly;
    private bool _eternal;
    private bool _costIsX;
    private bool _starCostIsX;
    private bool _xPlusOnUpgrade;
    private int? _upgradeCost;

    // ⚠ V0.2.130 按用户要求删掉了 `ShowPileCounts`（「卡面显示牌堆张数（{DrawPile}/{DiscardPile}/{ExhaustPile}）」）。
    //   老存档里那个字段（`"ShowPileCounts": true`）读进来会被 System.Text.Json **直接忽略**
    //   （ProfileFactory.JsonOpts 没有开 UnmappedMemberHandling.Disallow，未知成员一律跳过），
    //   不报错、不会拦住加载；再次保存时这个字段自然消失 —— 见 docs\QUEUE.md 第 17 批的口径说明。

    public string Name { get => _name; set => Set(ref _name, value); }
    /// <summary>英文类名，留空自动生成 &lt;角色类名&gt;Card&lt;序号&gt;</summary>
    public string ClassName { get => _className; set => Set(ref _className, value); }
    /// <summary>Attack / Skill / Power</summary>
    public string CardType { get => _cardType; set => Set(ref _cardType, value); }
    /// <summary>Basic / Common / Uncommon / Rare</summary>
    public string Rarity
    {
        get => _rarity;
        set
        {
            if (!Set(ref _rarity, value)) return;
            // V0.2.223：稀有度决定「这张牌是诅咒 / 状态 / 先古卡」，界面那几处显隐都要跟着变
            //（以前只发 Display，于是把稀有度改成「状态」之后「可被无限升级」那一行不会立刻出现）。
            Raise(nameof(IsCurseCard));
            Raise(nameof(IsStatusCard));
            Raise(nameof(IsAncientCard));
            Raise(nameof(ShowInfinitelyUpgradable));
        }
    }
    public int Cost
    {
        get => _cost;
        set
        {
            if (!Set(ref _cost, value)) return;
            // 界面那个费用框绑的是 CostText（写纯数字时会落回这里），提示行也要跟着刷新
            Raise(nameof(CostText));
            Raise(nameof(CardCostHint));
        }
    }

    // ===== X 费用（本体储君「天际钻头」、亡灵契约师「挽歌」那种）=====
    // ⚠ V0.2.131：界面上这三个勾选框**没有了**（用户要求合并进费用框 / 额外资源量费用框 / 升级后费用框）。
    //   字段一个都没删 —— 老存档原样读得回来、生成文本逐字不变；界面上由 CostText / StarCostText /
    //   UpgradeCostText 这三个文本属性**代理**显示与写入（写 "X" / "Y" / "X+1" 就落回这三个老字段）。
    /// <summary>费用为 X：打出时消耗全部能量，X = 消耗掉的能量（本体 Whirlwind / 天际钻头）。</summary>
    public bool CostIsX
    {
        get => _costIsX;
        set
        {
            if (!Set(ref _costIsX, value)) return;
            Raise(nameof(CostText));
            Raise(nameof(CostIsXEffective));
            Raise(nameof(CardCostHint));
        }
    }
    /// <summary>额外资源量费用为 X：打出时消耗全部额外资源量，X = 消耗掉的资源量（本体 Stardust）。</summary>
    public bool StarCostIsX
    {
        get => _starCostIsX;
        set
        {
            if (!Set(ref _starCostIsX, value)) return;
            Raise(nameof(StarCostText));
            Raise(nameof(StarCostIsXEffective));
            Raise(nameof(CardCostHint));
        }
    }
    /// <summary>升级后 X +1（本体 Cascade 的做法）。</summary>
    public bool XPlusOnUpgrade
    {
        get => _xPlusOnUpgrade;
        set
        {
            if (!Set(ref _xPlusOnUpgrade, value)) return;
            Raise(nameof(UpgradeCostText));
            Raise(nameof(CardCostHint));
        }
    }

    /// <summary>
    /// 升级后费用（整张牌，不是单条效果）：填了就生成 <c>OnUpgrade()</c> 里的
    /// <c>base.EnergyCost.UpgradeBy(差值)</c>（本体改费用就是这么做的）。
    /// 留空（null）= 升级不改费用。例：1 费卡想升级成 0 费 → 这里填 0。
    ///
    /// <para>⚠ V0.2.131 起界面上这一栏**搬到中间费用栏下方**（原来在右侧效果编辑器里，用户说重复了），
    /// 但字段本身没动 —— 老存档读回来、生成文本一个字都不变。</para>
    /// </summary>
    public int? UpgradeCost
    {
        get => _upgradeCost;
        set
        {
            if (!Set(ref _upgradeCost, value)) return;
            Raise(nameof(UpgradeCostText));
            Raise(nameof(CardCostHint));
        }
    }

    // ==================================================================================
    //  卡牌费用 / 额外资源量费用（用户 2026 需求《卡牌页优化.md》中间②，V0.2.131）
    //
    //  需求原文：「去掉 X 费用、额外资源量费用为 X、升级后 X+1 这三项，在原本费用框内，
    //  支持填入 A+3 或 X 这种带参数的式子；在费用框同侧右边加上额外资源量费用框……
    //  并且在这两下方增加「升级后费用」和「升级后额外资源量费用」。」
    //
    //  用户拍板（《已拍板决定.md》Q3=A「两者都要」）：
    //    · 「生成期常量折叠」—— 1+1、A+3（A 必须是「固定数值」来源的参数）在**生成时**就算成一个数字，
    //      因为本体 <c>base(cost, …)</c> / <c>CanonicalStarCost</c> 要的是**编译期常量**；
    //    · 「X 沿用本体那套」—— 费用里的 X 只有一个含义：打出时消耗掉的资源量
    //      （能量 → 本体 <c>HasEnergyCostX</c> + <c>ResolveEnergyXValue()</c>；
    //       额外资源量 → <c>HasStarCostX</c> + <c>ResolveStarXValue()</c>）。
    //
    //  ⚠ 老字段 <see cref="CostIsX"/> / <see cref="StarCostIsX"/> / <see cref="XPlusOnUpgrade"/>
    //    一个都没删，生成端**先读它们**（见 <c>CSharpCodeGen.IsCostX</c>）—— 所以老存档生成出来的
    //    代码逐字节不变（红线①）。界面上这三个勾选框没了，改成在这几个框里写 `X` / `Y` / `X+1`：
    //    写 `X` 与勾「X 费用」**完全等价**（走的是同一条生成分支、同一个变量 `x`）。
    //
    //  解析口径完全复用锻造页数字框那一套（<see cref="Generation.ForgeNumbers"/> +
    //  <c>ForgeFormula.TryEvalConst</c>）—— 不另造一个解析器。
    // ==================================================================================

    private string _costFormula = "";

    /// <summary>
    /// 费用框的原文（空 = 用老字段 <see cref="Cost"/>，老存档一个字符都不变）。
    /// 支持三档：纯数字 / 生成期常量折叠公式 / <c>X</c>（全部能量，本体 X 费那套）。
    /// 运行时才知道的量（读血量 / 读状态 / 实时读数参数）由校验器明确拦住并说清原因 —— 本体收不了。
    /// </summary>
    public string CostFormula
    {
        get => _costFormula;
        set
        {
            if (!Set(ref _costFormula, value ?? "")) return;
            Raise(nameof(CostText));
            Raise(nameof(CostIsXEffective));
            Raise(nameof(CostBoxHint));
            Raise(nameof(Display));
        }
    }

    private string _starCostFormula = "";

    /// <summary>
    /// 「额外资源量费用」框的原文（空 = 老规矩：从效果里的「花费额外资源量」（负数）算出来）。
    /// 支持三档：纯数字 / 生成期常量折叠公式 / <c>Y</c>（全部额外资源量，本体 <c>HasStarCostX</c> 那套）。
    /// </summary>
    public string StarCostFormula
    {
        get => _starCostFormula;
        set
        {
            if (!Set(ref _starCostFormula, value ?? "")) return;
            Raise(nameof(StarCostText));
            Raise(nameof(StarCostIsXEffective));
            Raise(nameof(Display));
        }
    }

    private string _upgradeCostFormula = "";

    /// <summary>
    /// 「升级后费用」框的原文（空 = 用老字段 <see cref="UpgradeCost"/>）。
    /// 绝对值那套照旧（<c>EnergyCost.UpgradeBy(差值)</c>）；
    /// 写成 <c>X</c> / <c>X+1</c> 时表示「还是 X 费，升级后 X + N」——
    /// 这就是老勾选框「升级后 X +1」的等价写法（生成 <c>x = ResolveEnergyXValue() + (base.IsUpgraded ? N : 0)</c>）。
    /// </summary>
    public string UpgradeCostFormula
    {
        get => _upgradeCostFormula;
        set
        {
            if (!Set(ref _upgradeCostFormula, value ?? "")) return;
            Raise(nameof(UpgradeCostText));
            Raise(nameof(CardCostHint));
            Raise(nameof(Display));
        }
    }

    private int? _upgradeStarCost;

    /// <summary>
    /// 「升级后额外资源量费用」（绝对数，null = 升级不改额外资源量费用）。
    ///
    /// <para>本体依据（对着 <c>sts2.dll</c> 的元数据核过）：<c>CardModel</c> 上
    /// **有** <c>protected void UpgradeStarCostBy(int addend)</c>（和 <c>OnUpgrade()</c> 同级、同一个
    /// <c>Family</c> 可见性），另有 <c>BaseStarCost</c> / <c>CurrentStarCost</c> /
    /// <c>WasStarCostJustUpgraded</c> / <c>StarCostChanged</c> 这套机制 —— 所以生成端照
    /// <c>base.EnergyCost.UpgradeBy(差值)</c> 的同款思路在 <c>OnUpgrade()</c> 里写
    /// <c>UpgradeStarCostBy(差值)</c> 即可（不需要自己发明一套）。</para>
    /// </summary>
    public int? UpgradeStarCost
    {
        get => _upgradeStarCost;
        set
        {
            if (!Set(ref _upgradeStarCost, value)) return;
            Raise(nameof(UpgradeStarCostText));
            Raise(nameof(CardCostHint));
            Raise(nameof(Display));
        }
    }

    /// <summary>界面上「费用」框里显示的文本：写了个式子就显示式子，否则显示老字段那个数字。</summary>
    [JsonIgnore]
    public string CostText
    {
        get => string.IsNullOrWhiteSpace(_costFormula)
            ? (CostIsX ? "X" : _cost.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : _costFormula;
        set
        {
            string text = (value ?? "").Trim();
            // 纯数字 / 空 → 写回**老字段**，新字段留空：老存档的 JSON 形状与生成文本都不变
            if (text.Length == 0) { Cost = 0; CostIsX = false; CostFormula = ""; return; }
            if (int.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int plain))
            { Cost = plain; CostIsX = false; CostFormula = ""; return; }
            if (string.Equals(Generation.ForgeFormula.DisplayText(text), "X", StringComparison.Ordinal))
            { CostIsX = true; CostFormula = ""; return; }
            CostIsX = false;
            CostFormula = text;
        }
    }

    /// <summary>界面上「额外资源量费用」框里显示的文本。</summary>
    [JsonIgnore]
    public string StarCostText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_starCostFormula)) return _starCostFormula;
            if (StarCostIsX) return "Y";
            int derived = Generation.CSharpCodeGen.StarCostOf(this);
            return derived > 0 ? derived.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        }
        set
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) { StarCostIsX = false; StarCostFormula = ""; return; }
            if (int.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int plain))
            { StarCostIsX = false; StarCostFormula = plain.ToString(System.Globalization.CultureInfo.InvariantCulture); return; }
            if (string.Equals(Generation.ForgeFormula.DisplayText(text), "Y", StringComparison.Ordinal))
            { StarCostIsX = true; StarCostFormula = ""; return; }
            StarCostIsX = false;
            StarCostFormula = text;
        }
    }

    /// <summary>界面上「升级后费用」框里显示的文本。</summary>
    [JsonIgnore]
    public string UpgradeCostText
    {
        get => string.IsNullOrWhiteSpace(_upgradeCostFormula)
            ? (XPlusOnUpgrade ? "X+1" : (_upgradeCost?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""))
            : _upgradeCostFormula;
        set
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) { UpgradeCost = null; XPlusOnUpgrade = false; UpgradeCostFormula = ""; return; }
            if (int.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int plain))
            { UpgradeCost = plain; XPlusOnUpgrade = false; UpgradeCostFormula = ""; return; }
            // X（/ X+1、1+X）：还是 X 费，升级后 X + N —— 等价于老勾选框「升级后 X +1」
            var r = Generation.ForgeNumbers.ResolveCost(null, text, "X", allowPlus: true, "升级后费用");
            if (r.Ok && r.IsX)
            { XPlusOnUpgrade = r.Plus == 1; UpgradeCost = null; UpgradeCostFormula = r.Plus == 1 ? "" : text; return; }
            XPlusOnUpgrade = false;
            UpgradeCostFormula = text;
        }
    }

    /// <summary>界面上「升级后额外资源量费用」框里显示的文本。</summary>
    [JsonIgnore]
    public string UpgradeStarCostText
    {
        // V0.2.148：**原文也要认**（和左边那个「升级后费用」框同款）。
        // 以前这里只显示绝对数：填了 `Y`、或填了一个看不懂的式子时，绝对数是 null → 框子**显示成空**，
        // 看着就像「填不进去」；而原文一直留在 UpgradeStarCostFormula 里 → 校验器那句错误也永远消不掉
        //（用户实测报的：「升级后费用里面填不了 Y 和 X」「填过之后什么都没有也会报错」）。
        get => _upgradeStarCost?.ToString(System.Globalization.CultureInfo.InvariantCulture)
               ?? (string.IsNullOrWhiteSpace(_upgradeStarCostFormula) ? "" : _upgradeStarCostFormula);
        set
        {
            string text = (value ?? "").Trim();
            // ⚠ 清空必须**连原文一起清**：只清 UpgradeStarCost 的话，那句「看不懂」永远消不掉。
            if (text.Length == 0) { UpgradeStarCost = null; UpgradeStarCostFormula = ""; return; }
            if (int.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int plain))
            { UpgradeStarCost = plain; UpgradeStarCostFormula = ""; return; }
            var r = Generation.ForgeNumbers.ResolveCost(null, text, "Y", allowPlus: false, "升级后额外资源量费用");
            if (r.Ok && r.IsConstant) { UpgradeStarCost = r.Value; UpgradeStarCostFormula = ""; return; }
            // 其余两种都留原文：① `Y`（= 升级后还是 Y —— 本体改不了 X 那一档，语义上就是「升级不改」）
            // 是**合法**写法，校验器不报错、生成端也不写任何 UpgradeStarCostBy；
            // ② 看不懂 / `Y+1` 那种由校验器报错说清（V0.2.148 起 `Y` 从「报错」改成「合法 = 不改」）。
            UpgradeStarCost = null;
            UpgradeStarCostFormula = text;
        }
    }

    private string _upgradeStarCostFormula = "";

    /// <summary>「升级后额外资源量费用」解析不了时留的原文（校验器按它报错，不会静默丢）。</summary>
    public string UpgradeStarCostFormula
    {
        get => _upgradeStarCostFormula;
        set { if (Set(ref _upgradeStarCostFormula, value ?? "")) { Raise(nameof(UpgradeStarCostText)); Raise(nameof(CardCostHint)); } }
    }

    /// <summary>这张牌的费用**是不是 X**（老字段 + 费用框里写了 X，两者等价）。</summary>
    [JsonIgnore]
    public bool CostIsXEffective => _costIsX || Generation.ForgeNumbers.IsBareX(_costFormula);

    /// <summary>这张牌的额外资源量费用**是不是 X/Y**（老字段 + 框里写了 Y，两者等价）。</summary>
    [JsonIgnore]
    public bool StarCostIsXEffective => _starCostIsX || Generation.ForgeNumbers.IsBareY(_starCostFormula);

    /// <summary>界面上费用栏底下那句说明（把四档写法 / 编译期常量这件事说清楚）。</summary>
    [JsonIgnore]
    public string CostBoxHint =>
        "费用支持三档：① 数字（2）；② 生成期就算得出来的式子（1+1、A+3 —— A 必须是「自定义参数 / 组」页里"
        + "「固定数值」来源的参数）；③ X = 消耗全部能量（本体「天际钻头 / 旋风斩」那套，X 值 = 打出时消耗掉的能量）。"
        + "本体 base(cost, …) 要的是**编译期常量**，所以读血量 / 读状态那种运行时表达式在费用栏里收不了"
        + "（校验器会拦下来并说清是哪一条）。额外资源量费用栏同理，写 Y = 消耗全部额外资源量（本体「星尘」那套）。"
        + "「升级后费用」留空 = 升级不改费用；填数字 = 升级后的绝对费用；填 X = 升级后还是 X（等于不改）；"
        + "填 X+1 = 还是 X 费、升级后 X 多 1（原来那个勾选框的等价写法）。"
        + "「升级后额外资源量费用」留空 = 不改；填数字 / 生成期常量式子 = 升级后的绝对资源量费用；"
        + "填 Y = 升级后还是 Y（等于不改）—— ⚠ 本体 UpgradeStarCostBy 对「资源量费用 = X」的牌**会直接抛异常**，"
        + "所以「升级后 Y+1」这种写法工具做不了（校验器会拦住并说清）。"
        + "⚠ 另外：**升级改不了「数字费用 ↔ X 费」这一档**（X / Y 是卡牌构造时定死的，升级只能改数字）——"
        + "想「升级后 X 费」做不到；能表达的是「一开始就 X 费 + 升级后 X+1」，或者「升级后费用填数字」（例：1 → 0 费）。";

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

    private List<string> _upgradeAddedCustomKeywords = new();

    /// <summary>
    /// **升级后获得**的自定义关键词（存 <see cref="CustomKeywordSpec.Key"/>，大小写不敏感；空列表 = 不变，老存档就是空）。
    ///
    /// <para>⚠ **只影响显示**：本体的 <c>CardKeyword</c> 是**封闭枚举**（模组加不了新值），所以自定义关键词
    /// 本来就是「卡面那行 <c>[gold]名字[/gold]。</c> + <c>ExtraHoverTips</c> 覆写 + <c>card_keywords</c> 本地化键」
    /// 三块加起来的一个显示等价物 —— 游戏侧**没有任何按自定义关键词判定的逻辑**。
    /// 因此「升级后获得」= 升级后才在卡面上显示这个词、悬停才有说明，**不是**机制层面的变化
    ///（对比：本体关键词走 <c>AddKeyword(CardKeyword.X)</c> 会真的改卡牌状态，别的按关键词判定的逻辑看得见）。</para>
    ///
    /// <para>生成出来是：描述里那行用本体的 <c>{IfUpgraded:show:升级后那段|没升级那段}</c> 包起来，
    /// <c>ExtraHoverTips</c> 按 <c>base.IsUpgraded</c> 条件加/不加那条提示。</para>
    /// </summary>
    public List<string> UpgradeAddedCustomKeywords
    {
        get => _upgradeAddedCustomKeywords;
        set => SetCustomKeywordRefs(ref _upgradeAddedCustomKeywords, value, nameof(UpgradeAddedCustomKeywords));
    }

    private List<string> _upgradeRemovedCustomKeywords = new();

    /// <summary>
    /// **升级后失去**的自定义关键词（语义同 <see cref="UpgradeAddedCustomKeywords"/>，方向相反：
    /// 没升级时显示、升级后不显示）。
    /// </summary>
    public List<string> UpgradeRemovedCustomKeywords
    {
        get => _upgradeRemovedCustomKeywords;
        set => SetCustomKeywordRefs(ref _upgradeRemovedCustomKeywords, value, nameof(UpgradeRemovedCustomKeywords));
    }

    private void SetCustomKeywordRefs(ref List<string> field, List<string>? value, string name)
    {
        if (!Set(ref field, value ?? new List<string>())) return;
        Raise(name);
        Raise(nameof(UpgradeCustomKeywordsAny));
        Raise(nameof(UpgradeCustomKeywordsDisplay));
        Raise(nameof(Display));
    }

    /// <summary>升级后自定义关键词有没有填（列表右侧那行小结 / 校验器用）。</summary>
    [JsonIgnore]
    public bool UpgradeCustomKeywordsAny =>
        _upgradeAddedCustomKeywords.Count > 0 || _upgradeRemovedCustomKeywords.Count > 0;

    /// <summary>列表里显示的短说明，例如「升级后：获得 命定、失去 回声」（没填就返回空串）。</summary>
    [JsonIgnore]
    public string UpgradeCustomKeywordsDisplay
    {
        get
        {
            if (!UpgradeCustomKeywordsAny) return "";
            var parts = new List<string>();
            if (_upgradeAddedCustomKeywords.Count > 0)
                parts.Add("获得 " + string.Join("、", _upgradeAddedCustomKeywords));
            if (_upgradeRemovedCustomKeywords.Count > 0)
                parts.Add("失去 " + string.Join("、", _upgradeRemovedCustomKeywords));
            return "升级后（自定义关键词）：" + string.Join("；", parts);
        }
    }
    public bool InStartingDeck { get => _inStartingDeck; set => Set(ref _inStartingDeck, value); }
    public int StartingCopies { get => _startingCopies; set => Set(ref _startingCopies, value); }
    public bool InCardPool { get => _inCardPool; set => Set(ref _inCardPool, value); }

    private bool _isColorless;

    /// <summary>
    /// **无色卡**（用户要求）：这张牌不属于你角色的卡池，而是进本体的「无色卡池」。
    ///
    /// 本体机制（对着源码确认过）：<c>CardModel.Pool</c> 是**反查** ——
    /// <c>ModelDb.AllCardPools.FirstOrDefault(pool =&gt; pool.AllCardIds.Contains(Id))</c>，
    /// 找不到任何池会抛 <c>InvalidProgramException</c>；而「无色卡池」<c>ColorlessCardPool</c> 的
    /// 成员表是**写死的**（sealed 类），模组只能靠 <c>ModHelper.AddModelToPool&lt;ColorlessCardPool, T&gt;()</c>
    /// 在**游戏初始化之前**登记（见生成的 cs/ModEntry.cs）。
    /// 卡框 / 能量图标前缀就自动是无色那套（<c>card_frame_colorless</c>）—— 所以**不受**本工具的自定义卡框影响。
    /// </summary>
    public bool IsColorless
    {
        get => _isColorless;
        set { if (Set(ref _isColorless, value)) Raise(nameof(Display)); }
    }

    /// <summary>
    /// 可被无限升级：覆写 <c>MaxUpgradeLevel =&gt; int.MaxValue</c>（本体默认是 1，诅咒固定 0）。
    ///
    /// 为什么勾上就真的能「越升越强」：本体的升级流程是
    /// <c>UpgradeInternal() { CurrentUpgradeLevel++; OnUpgrade(); }</c>，
    /// 而我们生成的升级增量**全都写在 <c>OnUpgrade()</c> 里**
    /// （<c>DynamicVars[…].UpgradeValueBy(N)</c>），所以每升一级就再涨一次。
    /// 卡面标题本来就会显示成「名字+N」（本体 CardModel 在 MaxUpgradeLevel &gt; 1 时的行为）。
    /// </summary>
    public bool InfinitelyUpgradable { get => _infinitelyUpgradable; set => Set(ref _infinitelyUpgradable, value); }

    private bool _toothReplace;

    /// <summary>
    /// V0.2.260（用户要求）：这张**先古卡**就是「古老牙齿 ArchaicTooth」替换初始卡牌之后给的那一张。
    ///
    /// 本体 <c>ArchaicTooth.SetupForPlayer</c> 只认它自己那张「初始卡 → 先古卡」表（打击→破击、
    /// 中和→压制…），自定义角色的初始卡不在表里 ⇒ 它会把你的初始卡变成「怀疑 Doubt」。
    /// 勾上之后生成一个 Harmony 前缀：轮到**本模组角色**时用 <c>SetupForTests(初始卡, 这张先古卡)</c>
    /// 直接替本体把两张卡定好（别的角色照旧走本体）。
    /// </summary>
    public bool ToothReplace { get => _toothReplace; set => Set(ref _toothReplace, value); }

    private bool _autoPlayTurnEnd;

    /// <summary>
    /// V0.2.284（用户要求「加一个回合结束自动打出的效果」）：勾上 = **回合结束时自动打出这张牌自己**
    /// （只在自己这一侧回合结束、且这张牌还在手牌里时才打；打出去就进弃牌堆，不会自己循环）。
    /// 生成端用本体的 <c>CardCmd.AutoPlay(choiceContext, this, target)</c>（CardCmd.cs:51）。
    /// </summary>
    public bool AutoPlayAtTurnEnd
    {
        get => _autoPlayTurnEnd;
        set { if (Set(ref _autoPlayTurnEnd, value)) { Raise(nameof(Display)); } }
    }

    private string _toothStarterClass = "";

    /// <summary>
    /// V0.2.261（用户问「那我如何决定是哪张牌被替换呢」）：古老牙齿要**换掉的那张初始卡**的类名。
    ///
    /// 空 = 自动（取「初始卡组」里第一张，即卡牌页勾了「初始牌」的第一张）。
    /// 填了就按它去找 —— 存的是**生成出来的卡牌类名**（和「目标卡」下拉里的 Id 同一个口径），
    /// 这样玩家可以自己指定「古老牙齿换掉的是我哪张初始卡」。
    /// </summary>
    public string ToothStarterClass
    {
        get => _toothStarterClass;
        set => Set(ref _toothStarterClass, value ?? "");
    }

    private bool _dustyTomeGrant;

    /// <summary>
    /// V0.2.260（用户要求）：这张**先古卡**就是「尘封魔典 DustyTome」给的那一张。
    ///
    /// 本体是从角色的卡池里**随机**挑一张先古卡（还排除了古老牙齿那 5 张）；勾上之后
    /// 生成一个 Harmony 前缀，让尘封魔典直接给这张（只对**本模组角色**生效）。
    /// </summary>
    public bool DustyTomeGrant { get => _dustyTomeGrant; set => Set(ref _dustyTomeGrant, value); }

    private string _customDescription = "";

    /// <summary>
    /// 卡牌自定义描述（留空 = 只用自动生成的那段）。
    ///
    /// 里面可以照常写本体的占位符（例如 <c>造成 {Damage:diff()} 点伤害</c> 会显示成升级后的数字、
    /// <c>[gold]…[/gold]</c> 是金色富文本），也可以只写一句风味文字。
    /// 默认**追加**在自动描述后面（不勾 <see cref="CustomDescriptionReplaces"/>）。
    /// </summary>
    public string CustomDescription
    {
        get => _customDescription;
        set { if (Set(ref _customDescription, value ?? "")) { Raise(nameof(HasCustomDescription)); Raise(nameof(Display)); } }
    }

    private bool _customDescriptionReplaces;

    /// <summary>自定义描述是不是**替换**掉自动生成的那段（不勾 = 追加在后面）。</summary>
    public bool CustomDescriptionReplaces
    {
        get => _customDescriptionReplaces;
        set { if (Set(ref _customDescriptionReplaces, value)) Raise(nameof(Display)); }
    }

    private string _upgradedCustomDescription = "";

    /// <summary>
    /// **升级后**的自定义描述（用户要求：「在锻造卡的卡牌自定义描述加一个能够填入卡牌升级后的自定义描述」）。
    ///
    /// 留空 = 升级后和上面那段一样（老存档一个字都不变）。
    /// 填了就跑本体的升级分支语法：生成出来是
    /// <c>{IfUpgraded:show:升级后那段|没升级那段}</c>（本体 <c>ShowIfUpgradedFormatter</c>），
    /// 游戏里按这张牌有没有升级自动显示对应那一版。
    /// 需要它的场合：锻造卡「升级前 / 升级后」两块布干的事不一样（例：升级前打 6、升级后抽 2 张），
    /// 一句话说不清；或者你就是想让升级后换一句风味文字。普通卡也能填（升级后换措辞）。
    /// </summary>
    public string UpgradedCustomDescription
    {
        get => _upgradedCustomDescription;
        set { if (Set(ref _upgradedCustomDescription, value ?? "")) { Raise(nameof(HasUpgradedCustomDescription)); Raise(nameof(Display)); } }
    }

    /// <summary>填了「升级后的自定义描述」（界面上那一栏的提示用）。</summary>
    [JsonIgnore]
    public bool HasUpgradedCustomDescription => !string.IsNullOrWhiteSpace(_upgradedCustomDescription);

    /// <summary>填了自定义描述（界面上那一栏的显隐用）。</summary>
    [JsonIgnore]
    public bool HasCustomDescription => !string.IsNullOrWhiteSpace(_customDescription);

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

    private bool _isForged;

    /// <summary>
    /// 这张牌是**锻造页**造出来的（需求：卡牌页后新增「锻造」页）。
    ///
    /// 和普通卡的区别：
    ///   · 列表里的名字前面带一个 <c>*</c>（一眼看出是锻造卡）；
    ///   · **不需要**填费用 / 「升级后 X + 1」—— 那些由锻造页的节点图决定（触发时机 / 能量模块）；
    ///   · 在卡牌页选中时右侧不显示编辑器，只提示「请在锻造页查看」。
    /// 数据本身还是同一个 <see cref="CardSpec"/>（效果编辑 / 卡面 / 关键词全部复用）。
    /// </summary>
    public bool IsForged
    {
        get => _isForged;
        set
        {
            if (!Set(ref _isForged, value)) return;
            Raise(nameof(Display));
            Raise(nameof(CostFieldVisible));
            Raise(nameof(ForgeEditingHint));
        }
    }

    private List<string> _groupIds = new();

    /// <summary>
    /// 这张牌属于哪些「组」（存 <see cref="CardGroupSpec.Id"/>）。
    /// 界面在「卡牌属性」标题右边用勾选框维护；「范围」下拉里会多出「组：XXX」这一档。
    /// </summary>
    public List<string> GroupIds
    {
        get => _groupIds;
        set { if (Set(ref _groupIds, value ?? new List<string>())) Raise(nameof(GroupIdList)); }
    }

    /// <summary>去掉空值 / 重复之后的组引用（读老存档、手写 JSON 时兜底）。</summary>
    [JsonIgnore]
    public List<string> GroupIdList =>
        GroupIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();

    /// <summary>界面用：这张牌在锻造页里造，所以卡牌页不显示「费用 / 升级后 X + 1」那几行。</summary>
    [JsonIgnore]
    public bool CostFieldVisible => !IsForged;

    /// <summary>界面用：锻造卡在卡牌页右侧显示的那句提示（中间照常改属性，右边效果栏锁死）。</summary>
    [JsonIgnore]
    public string ForgeEditingHint =>
        "这张牌是在「锻造」页用节点图做的。\n\n"
        + "中间那些属性（名称 / 类型 / 稀有度 / 卡池 / 关键字 / 卡面）就在**这里**改；\n"
        + "右边的「效果」由「锻造」页那张节点图决定，这里锁死 —— 要改效果请去「锻造」页。";

    /// <summary>
    /// 锻造节点图（升级前那块布）。只有 <see cref="IsForged"/> 的条目才会有；
    /// 空图 / null = 不接管生成（普通卡与老存档一律是 null，生成的代码逐字不变）。
    /// </summary>
    public ForgeGraphSpec? ForgeGraph { get; set; }

    /// <summary>锻造节点图（升级后那块布）。为 null = 升级后用升级前那张。</summary>
    public ForgeGraphSpec? UpgradedForgeGraph { get; set; }

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
    /// <summary>
    /// 永恒 Eternal：**无法从你的牌组中移除或变化**（本体就是 IsRemovable / IsTransformable 两个开关）。
    /// 本体的诅咒「厄运」就是靠它 + 不能被打出做的。
    /// </summary>
    public bool Eternal { get => _eternal; set => Set(ref _eternal, value); }

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
            if (Eternal) list.Add("Eternal");
            return list;
        }
    }

    // ===== 诅咒 / 先古卡（「诅咒 / 先古卡」页）=====
    /// <summary>
    /// 这是一张**诅咒**：费用固定 -1、类型 / 稀有度固定 Curse，本体自带的「不能被打出」也固定加上，
    /// 效果不写在 OnPlay 里而是走 **OnTurnEndInHand**（「在你的回合结束时，如果这张牌在你的手牌中」）。
    /// </summary>
    [JsonIgnore]
    public bool IsCurseCard => string.Equals(Rarity?.Trim(), "Curse", StringComparison.OrdinalIgnoreCase);

    /// <summary>这是一张**先古卡**：稀有度 Ancient —— 只会被先古遗物 / 你自己的效果拿到，不进普通战斗奖励。</summary>
    [JsonIgnore]
    public bool IsAncientCard => string.Equals(Rarity?.Trim(), "Ancient", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 这是一张**状态牌**（照本体「粘液 Slimed」那套）：稀有度 Status、类型 Status、**不能升级**。
    /// 和诅咒一样不进普通战斗奖励（本体的奖励只在 Common / Uncommon / Rare 里抽）。
    /// 注意：判据是**稀有度**（和 Curse / Ancient 一致），只选类型不选稀有度不算。
    /// </summary>
    [JsonIgnore]
    public bool IsStatusCard => string.Equals(Rarity?.Trim(), "Status", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 界面：特殊卡（诅咒 / 先古 / 状态）那一页要不要显示「可被无限升级」—— V0.2.223（用户实测报的
    /// 「状态牌局内升级好像没生效」）：以前这一页**没有**这个开关（只有「卡牌」页有），而状态牌
    /// 恰恰只能在这一页编辑 → 状态牌根本没机会变成能升级的，生成出来还是
    /// <c>MaxUpgradeLevel => 0</c>，「升级卡牌」自然什么都不做。
    /// 诅咒固定不能升级（<c>CurseSource</c> 里写死），所以诅咒上不显示。
    /// </summary>
    [JsonIgnore]
    public bool ShowInfinitelyUpgradable => !IsCurseCard;

    private bool _curseRemoveAfterCombat;

    /// <summary>
    /// 诅咒专用：战斗结束时如果它还在**牌组**里，就把它自己删掉（本体诅咒「罪恶 Guilty」那套：
    /// 挂满 N 场战斗后自己消失）。只有诅咒用得到。
    /// </summary>
    public bool CurseRemoveAfterCombat
    {
        get => _curseRemoveAfterCombat;
        set { if (Set(ref _curseRemoveAfterCombat, value)) Raise(nameof(Display)); }
    }

    public ObservableCollection<EffectSpec> Effects { get; set; } = new();

    /// <summary>
    /// 【旧版字段，只为读老存档兼容】以前「条件」是整张牌的；
    /// 现在条件挂在每条效果上（见 <see cref="EffectSpec.Condition"/>），打开老存档时会被自动搬到第一条效果上、这里清空。
    /// </summary>
    public ConditionSpec Condition { get; set; } = new();

    /// <summary>
    /// 卡牌列表里显示的整行文本。
    ///
    /// <para>类型 / 稀有度用**中文**（用户要求《卡牌页优化.md》第 2 条 ①「将类型和稀有度的英文替换成中文」）：
    /// 走 <see cref="EffectCatalog.CardTypeZh"/> / <see cref="EffectCatalog.CardRarityZh"/>，
    /// 中文照抄游戏本体的 <c>CARD_TYPE.*</c> / <c>CARD_RARITY.*</c>。
    /// 存进存档的 <see cref="CardType"/> / <see cref="Rarity"/> 还是英文 id —— 生成端读的也是英文 id。</para>
    /// </summary>
    [JsonIgnore]
    public string Display => IsVanillaCard
        // 本体卡：只有类名和初始张数是有意义的，别把费用/稀有度那些默认值显示出来误导人
        ? $"【本体卡】{Name}（{ClassName}）  ｜ 初始 {Math.Max(1, StartingCopies)} 张 ｜ 数值同本体，不可改"
        // 锻造卡名字前面带一个「*」（需求：生成一项前缀带有「*」的卡牌）
        // 费用：锻造卡那两栏在「卡牌」页是藏起来的（费用归布上【能量】模块管）—— 列表里就按布上那个数显示，
        // 免得用户看到「0 费」却打进游戏才发现其实要花 1 点（用户报的坑）。
        : $"{(IsForged ? "*" : "")}{Name}  ｜ {EffectCatalog.CardTypeZh(CardType)} / {EffectCatalog.CardRarityZh(Rarity)} / {(CostIsXEffective ? "X" : (IsForged ? Generation.ForgeGraphGen.EnergyCostOf(ForgeGraph) : Cost).ToString())} 费{StarCostSuffix}{(InStartingDeck ? " ｜ 初始牌" : "")}{(InCardPool ? "" : " ｜ 不入池")}"
          + (KeywordList.Count > 0 ? " ｜ " + string.Join("·", KeywordList) : "")
          + (UpgradeKeywords.Any ? " ｜ " + UpgradeKeywords.Display : "")
          + (UpgradeCustomKeywordsAny ? " ｜ " + UpgradeCustomKeywordsDisplay : "")
          + (Condition.IsNone ? "" : "  ｜ 条件：" + Condition.DisplayShort);

    /// <summary>
    /// 卡牌页费用栏底下那句提示（V0.2.131）：说清这一张牌现在到底几费、升级后几费。
    /// </summary>
    [JsonIgnore]
    public string CardCostHint
    {
        get
        {
            if (IsForged) return "";
            var parts = new List<string>();
            parts.Add(CostIsXEffective
                ? "费用 = X（打出时消耗全部能量，X = 消耗掉的能量）"
                : $"费用 = {Generation.CSharpCodeGen.CardCostOf(this)}");
            if (StarCostIsXEffective) parts.Add("额外资源量 = Y（消耗全部额外资源量）");
            else
            {
                int star = Generation.CSharpCodeGen.StarCostOf(this);
                if (star > 0) parts.Add($"额外资源量 = {star}");
            }
            string now = string.Join("，", parts);
            int? up = _upgradeCost is { } uc ? uc : (int?)null;
            if (!string.IsNullOrWhiteSpace(_upgradeCostFormula))
            {
                var r = Generation.ForgeNumbers.ResolveCost(null, _upgradeCostFormula, "X", allowPlus: true, "升级后费用");
                if (r.Ok && r.IsConstant) up = r.Value;
            }
            int xPlus = Generation.CSharpCodeGen.CostXPlus(this);
            string upText = xPlus > 0 ? $"升级后还是 X，但 X + {xPlus}"
                : up is { } u ? (u == Generation.CSharpCodeGen.CardCostOf(this) ? $"升级后 {u}（和现在一样 = 等于没改）" : $"升级后 {u}")
                : "升级不改费用";
            string starText = _upgradeStarCost is { } us
                ? (us == Generation.CSharpCodeGen.StarCostOf(this) ? $"升级后额外资源量 {us}（和现在一样）" : $"升级后额外资源量 {us}")
                : "";
            return $"整张牌：现在 {now}；{upText}{(starText.Length > 0 ? "；" + starText : "")}。";
        }
    }


    /// <summary>「+ 资源量 N」（列表里显示费用时跟在后面）：锻造卡看布上【能量】模块，普通卡看「资源量X」那个勾。</summary>
    [JsonIgnore]
    private string StarCostSuffix
    {
        get
        {
            if (IsForged)
            {
                int res = Generation.ForgeGraphGen.ResourceCostOf(ForgeGraph);
                return res > 0 ? $" + 资源量{res}" : "";
            }
            // ⚠ 只有**显式配过**的才追加这一段：老存档（只有负数「花费额外资源量」效果、没勾资源量 X）
            //   的列表文字必须逐字不变（老行为就是不追加），所以不能直接拿 StarCostOf 的结果来显示。
            if (StarCostIsXEffective) return " + 资源量X";
            var r = Generation.ForgeNumbers.ResolveCost(null, _starCostFormula, "Y", allowPlus: false, "额外资源量费用");
            return r.IsConstant && r.Value > 0 ? $" + 资源量{r.Value}" : "";
        }
    }

    // ===== 左侧列表的序号（用户要求第 1 条：序号在卡牌名最左侧、从上到下 1..N，
    //       序号是**位置号**、不跟随卡牌移动）=====
    private int _ordinal;

    /// <summary>
    /// 这张卡**在它自己那一页列表里**的当前位置（1 起算），只给界面显示用。
    ///
    /// <para>⚠ 它**不写进存档**（<see cref="JsonIgnoreAttribute"/>）：序号是「当前的排序位置」，
    /// 不是跟着卡牌走的标记 —— 拖动换位置 / 删掉中间一张之后，由界面自己的
    /// <c>RefreshCardOrdinals</c> 重新发一遍。和 <see cref="RelicSpec.Ordinal"/> /
    /// <see cref="PotionSpec.Ordinal"/> 同一套做法（写进存档会给老存档多一个字段）。</para>
    ///
    /// <para>⚠ V0.2.137 口径（用户拍板 A）：**每页列表各自 1..N** —— 普通卡（含本体卡 / 初始的
    /// 打击与防御，它们本来就排在这个列表最上面、就是第 1、2 号）、诅咒卡、先古卡**各自**从 1 数。
    /// 自动英文类名跟的就是这个号（前缀各不相同，见 <see cref="AutoClassNameStem"/>）。</para>
    /// </summary>
    [JsonIgnore]
    public int Ordinal { get => _ordinal; set => Set(ref _ordinal, value); }

    // ===== 「自动类名」的痕迹（用户要求第 1 条：自动的跟序号走、手填的不动）=====
    private int _lastAutoOrdinal;

    /// <summary>
    /// 上一次生成时，这张卡的「自动类名」是第几号
    /// （对应 <c>&lt;角色类名&gt; + <see cref="AutoClassNameStem"/> + 它</c>）。
    ///
    /// <para>⚠ 和遗物 / 药水那边同一个理由：英文类名留空时**类名本身要保持空白**（一写实就再也分不出
    /// 「工具生成的」和「用户手打的」—— 见 <c>Naming.RenumberCardClassNames</c> 的注释）。
    /// 但界面上「英文类名」那一栏总得让用户看到「不填会用哪个名字」，所以单独把**序号**记在这里，
    /// 由 <see cref="AutoClassNameHint"/> 拼出完整名字。不写进存档。</para>
    ///
    /// <para>⚠ V0.2.137：这个号 = 它在**自己那一页列表**里的位置（和 <see cref="Ordinal"/> 同值）。
    /// 老口径（V0.2.130 ~ V0.2.136）是「普通卡 + 诅咒 + 先古卡一条连续编号」，诅咒 / 先古卡的号会比
    /// 各自列表里的行号大 —— 新口径下两者相等（前缀区分三类，不再靠连续编号避撞名）。</para>
    /// </summary>
    [JsonIgnore]
    public int LastAutoOrdinal { get => _lastAutoOrdinal; set => Set(ref _lastAutoOrdinal, value); }

    /// <summary>界面用：不填类名时，这张卡会用哪个自动名（还没生成过就按当前行号推）。</summary>
    [JsonIgnore]
    public string AutoClassNameHint
    {
        get
        {
            int n = _lastAutoOrdinal >= 1 ? _lastAutoOrdinal : _ordinal;
            // V0.2.137：三套前缀（普通卡 Card / 诅咒卡 Curse / 先古卡 Ancient）+ **各页自己的号**。
            // 前缀由界面在 RefreshCardOrdinals 里按稀有度填进 AutoClassNameStem（模型层拿不到
            // 「这份配置的角色类名」和「它在哪个列表里」）；没填时退回 Card。
            string stem = string.IsNullOrWhiteSpace(_autoStem) ? Generation.Naming.CardStem : _autoStem!;
            return CharClass + stem + (n >= 1 ? n : 1);
        }
    }

    // ===== 界面用：这份卡牌当前所属的「自动名前缀」与角色类名（都不写存档）=====
    private string _autoStem = "";
    private string _charClass = "";

    /// <summary>
    /// 界面用：这张卡该用哪一套自动名前缀（Card / Curse / Ancient，由
    /// <c>MainWindow.RefreshCardOrdinals</c> 按稀有度填）。
    /// 模型层自己不知道「角色类名是多少」也看不到所在列表，所以只能由界面点一下 ——
    /// 生成端 / 校验器**不读它**（它们走 <c>Naming.AutoCardClassName</c>，那是唯一的口径来源）。
    /// </summary>
    [JsonIgnore]
    public string AutoClassNameStem
    {
        get => _autoStem;
        set { if (Set(ref _autoStem, value ?? "")) { Raise(nameof(AutoClassNameHint)); Raise(nameof(ClassNameHint)); } }
    }

    /// <summary>界面用：这份配置的角色类名（由界面填，只给「英文类名」那行小字拼完整名字用）。</summary>
    [JsonIgnore]
    public string CharClass
    {
        get => _charClass;
        set { if (Set(ref _charClass, value ?? "")) { Raise(nameof(AutoClassNameHint)); Raise(nameof(ClassNameHint)); } }
    }

    /// <summary>
    /// 界面用：这张卡的英文类名现在是不是「自动生成的那一个」
    /// （判据 = <see cref="Generation.Naming.IsAutoCardClassNameFor"/>，按名字形状认；
    /// 认不出的都当手填）。
    /// </summary>
    [JsonIgnore]
    public bool ClassNameIsAuto =>
        !Generation.Naming.IsValidIdentifier(_className)
        || Generation.Naming.IsAutoCardClassNameFor(null, _className);

    /// <summary>
    /// 界面用：「英文类名」那一栏下面那行小字。
    ///
    /// <para>V0.2.137：序号口径改成「**每页列表各自 1..N**」（和遗物 / 药水页一样），
    /// 所以这句里说清「按**这一页**列表里的序号重算」（旧文案只写「左边列表」，
    /// 诅咒 / 先古卡页上会让人以为是接着普通卡往下数）。</para>
    /// </summary>
    [JsonIgnore]
    public string ClassNameHint => ClassNameIsAuto
        ? "留空 = 自动。现在对应 " + AutoClassNameHint + "（重新生成 / 构建时按**这一页**列表里的序号重算，拖动换位置就跟着变）。"
        : "现在这个名字是**你手填的**：重新生成 / 构建都不会改它（想让它跟着序号走就把这一栏清空）。";

    /// <summary>界面用：左侧列表那一行的提示（序号 + 类名是自动还是手填）。</summary>
    [JsonIgnore]
    public string RowTip => "第 " + (_ordinal >= 1 ? _ordinal : 0) + " 位 ｜ "
        + (ClassNameIsAuto ? "英文类名自动（跟序号走）" : "英文类名手填（不会自动改）") + " ｜ 可以拖动这一行换位置";

    /// <summary>界面用：左侧列表里那一行最左边的序号（1 起算，就是它当前的位置）。</summary>
    [JsonIgnore]
    public string OrdinalText => _ordinal >= 1 ? _ordinal + "." : "";

    /// <summary>界面用：真卡行当然不是空槽（空槽行是界面那边的 <c>SlotPlaceholder</c>，见 ISlotRow）。</summary>
    [JsonIgnore]
    public bool IsPlaceholder => false;
}

/// <summary>
/// 「升级后的关键字」：整张牌的设置，每个关键字三态 —— 不变 / 升级后获得 / 升级后失去。
/// 生成 <c>OnUpgrade()</c> 里的 <c>AddKeyword / RemoveKeyword</c>（本体「残影 / 幻影 / 寒冰」就是这么写的）。
/// </summary>
/// <summary>
/// 「诅咒 / 先古卡」页里每一类牌（诅咒 / 先古卡）自己的**外观**：卡牌边框颜色。
///
/// 为什么只能改边框：本体的卡面外观分几处取，其中
///   · 边框材质 = <c>CardModel.FrameMaterial</c> → <c>VisualCardPool.FrameMaterial</c>（**能**按牌/按类改）；
///   · 能量图标 = <c>CardModel.EnergyIconPath</c> → <c>VisualCardPool.EnergyIconPath</c>（能改，但我们固定用角色自己的）；
///   · 能量图标描边 = <c>NCard</c> 里的 <c>Model.Pool.EnergyOutlineColor</c>、牌堆底色 = <c>card.Pool.DeckEntryCardColor</c>
///     —— 这两个取的是**真实卡池**（卡必须属于它），所以按类改不了，只能跟角色配色。
/// 所以这里只暴露「卡牌边框」，不暴露那两个改不动的（免得给一个填了没反应的选项）。
///
/// 另外先古卡还自带本体的一套「先古外观」：稀有度 Ancient 会换成 beta 卡框贴图 + 先古边框 + 先古横幅
/// （见 <c>CardModel.FramePath</c> / <c>BannerMaterialPath</c> / <c>AncientTextBgPath</c>），
/// 这里的 RRGGBB 是给那套贴图**染色**（和角色配色用的是同一个 hsv 材质机制）。
/// </summary>
public sealed class SpecialCardStyleSpec : SpecBase
{
    /// <summary>「自定义颜色」这个选项的值（<see cref="Frame"/> 用它表示「用 FrameColor」）。</summary>
    public const string CustomFrame = "custom";

    private string _frame = "";
    private string _frameColor = "";

    /// <summary>
    /// 框色：<c>""</c> = 跟角色配色（默认）；本体框色名（card_frame_blue 等）；
    /// <c>"custom"</c> = 用下面的 RRGGBB。
    /// </summary>
    public string Frame
    {
        get => _frame;
        set { if (Set(ref _frame, value ?? "")) { Raise(nameof(IsCustomFrame)); Raise(nameof(Display)); } }
    }

    /// <summary>自定义边框颜色（RRGGBB，<see cref="Frame"/> == "custom" 时生效）。</summary>
    public string FrameColor
    {
        get => _frameColor;
        set
        {
            string v = value ?? "";
            // 直接往这个框里填一个合法颜色 = 就是想用自定义色：自动把下拉切到「自定义」。
            // 不这么做的话，下拉还停在「跟角色配色」时填了颜色也不生效
            //（用户报过「改了 RRGGBB 没反应」——就是踩在这上面）。
            if (CardColorSpec.NormalizeHex(v).Length > 0 && !IsCustomFrame)
            {
                _frame = CustomFrame;
                Raise(nameof(IsCustomFrame));
                Raise(nameof(Frame));
            }
            if (Set(ref _frameColor, v))
            {
                Raise(nameof(PreviewHex));    // 色块预览绑的是它，不通知就看不到颜色变化
                Raise(nameof(Any));
                Raise(nameof(Display));
            }
        }
    }

    /// <summary>选了「自定义颜色」那一项。</summary>
    [JsonIgnore]
    public bool IsCustomFrame => string.Equals(_frame, CustomFrame, StringComparison.Ordinal);

    /// <summary>
    /// 这一类牌**允不允许**「自定义颜色」。诅咒牌不允许（用户要求：诅咒去掉自定义颜色选项）——
    /// 诅咒在本体里的外观就是固定的灰色卡池，染色没有意义。
    /// 老存档 / 老工程里存过 <c>custom</c> 的一律当没设过：<see cref="Any"/> 会是 false，
    /// 生成时退回本体的诅咒卡池、界面上也不会显示色块。
    /// （不是存档字段：由 <see cref="CharacterProfile.CurseStyle"/> 在读取时强制设成 false。）
    /// </summary>
    [JsonIgnore]
    public bool AllowsCustomColor { get; set; } = true;

    /// <summary>配色色块预览用（没填 / 这一类牌不支持自定义颜色 就返回空串，界面显示成透明）。</summary>
    [JsonIgnore]
    public string PreviewHex => IsCustomFrame && AllowsCustomColor ? CardColorSpec.NormalizeHex(_frameColor) : "";

    /// <summary>有没有做任何外观设置（没设置 = 生成时不加任何东西，和以前完全一样）。</summary>
    [JsonIgnore]
    public bool Any => IsCustomFrame
        ? AllowsCustomColor && CardColorSpec.NormalizeHex(_frameColor).Length > 0
        : _frame.Length > 0;

    [JsonIgnore]
    public string Display
    {
        get
        {
            if (!Any) return "";
            return IsCustomFrame ? $"边框 #{CardColorSpec.NormalizeHex(_frameColor)}" : $"边框 {_frame}";
        }
    }
}

public sealed class KeywordUpgradeSpec : SpecBase
{    /// <summary>三态取值：Keep（不变）/ Add（升级后获得）/ Remove（升级后失去）</summary>
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

/// <summary>
/// 遗物上的一条触发时机：**只描述「什么时候触发」**（触发后做什么是**整只遗物共用**的那份效果，
/// 见 <see cref="RelicSpec.Effects"/>）。
///
/// 用户要求（V0.2.126，原话）：「中间的触发时机并不是每条各配效果，而是决定整只遗物的触发时机，
/// 只有同时满足或者满足其中一个才能触发遗物的效果」—— 所以从这一版起：
///   · 效果列表挂在**遗物**上（右侧效果栏能加多条效果种类）；
///   · 触发时机列表只留 Kind / PowerId / Amount / Scope / CardFilter；
///   · 关系（任意一条 / 全部满足）挂在遗物上：<see cref="RelicSpec.TriggerMode"/>。
///
/// V0.2.122~V0.2.125 的老存档里每条时机各带一份 <see cref="Effects"/>（还能勾
/// <see cref="RequireAllTriggers"/>）—— 那两个字段**降级成「只读老存档的遗留字段」**：
/// 载入时由 <c>ProfileFactory.NormalizeRelicEffects</c> 把各条时机的效果按顺序合并进遗物，
/// 合并完就清空 / 置 false，所以新存档里不再写它们。**不能加 [JsonIgnore]**：加了老存档里的
/// 效果就读不进来了。
/// </summary>
public sealed class RelicTriggerSpec : SpecBase
{
    private string _kind = "CombatStart";
    private string? _powerId;
    private decimal _amount = 1m;
    private string _scope = "Turn";
    private string _cardFilter = "Any";
    private bool _requireAllTriggers;

    /// <summary>触发器种类（见 EffectCatalog.RelicTriggers）</summary>
    public string Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            Raise(nameof(Display));
            Raise(nameof(Summary));
            Raise(nameof(ShowPowerFields));
            // V0.2.269（用户报「选了『某个状态层数变化后』还是没看到『层数变化方向』」）：
            // 这一栏的显隐也按 Kind 算 —— 不通知的话，在当前这条时机上换种类**不会**当场出现
            //（只有重新点一次列表才会重算；当时只有 ShowPowerFields 通知了，所以「盯哪个状态」出来了、
            // 方向那一栏却卡在隐藏状态）。
            Raise(nameof(ShowPowerDirectionField));
            Raise(nameof(ShowCardFilterField));
            Raise(nameof(ShowAmountField));
            Raise(nameof(ShowScopeField));
            Raise(nameof(AmountLabel));
            Raise(nameof(SpendHint));
            Raise(nameof(Hint));
        }
    }

    /// <summary>「某个状态层数到达 N 层 / 变化 / 被移除」盯哪个状态（本体 Power 类名；留空 = 任意状态）。</summary>
    public string? PowerId
    {
        get => _powerId;
        set { if (Set(ref _powerId, value)) { Raise(nameof(Hint)); Raise(nameof(Summary)); Raise(nameof(Display)); } }
    }

    private string _powerDirection = "";

    /// <summary>
    /// V0.2.266（用户要求「发时机的某个状态层数变化后能不能细分是上升了还是下降」）：
    /// 只有「某个状态层数变化后」用得到 —— 空 = 上升 / 下降都算（老行为），
    /// <c>Up</c> = 只在层数**上升**时触发，<c>Down</c> = 只在层数**下降**时触发。
    /// </summary>
    public string PowerDirection
    {
        get => _powerDirection;
        set { if (Set(ref _powerDirection, value ?? "")) { Raise(nameof(Summary)); Raise(nameof(Display)); } }
    }

    /// <summary>界面用：方向那一栏只在「某个状态层数变化后」显示。</summary>
    [JsonIgnore]
    public bool ShowPowerDirectionField => Kind == "PowerChanged";

    /// <summary>触发时机上的数值：每累计 N 次 / 每 N 点 / 数值 ≥ N / 到达 N 层（按种类换含义）。</summary>
    public decimal Amount
    {
        get => _amount;
        set { if (Set(ref _amount, value)) { Raise(nameof(Hint)); Raise(nameof(SpendHint)); Raise(nameof(Summary)); Raise(nameof(Display)); } }
    }

    /// <summary>计数类时机的统计范围：<c>Turn</c> = 本回合（默认）/ <c>Combat</c> = 本场战斗。</summary>
    public string Scope
    {
        get => _scope;
        set { if (Set(ref _scope, string.IsNullOrWhiteSpace(value) ? "Turn" : value)) { Raise(nameof(SpendHint)); Raise(nameof(Summary)); } }
    }

    /// <summary>「打出一张牌后」只对哪类牌生效（Any / Attack / Skill / Power / Curse / Status / Keyword:x / Group:x）。</summary>
    public string CardFilter
    {
        get => _cardFilter;
        set
        {
            // ⚠ 空值要**归一成 "Any"**：生成端的老写法是 `if (!string.Equals(TriggerCardFilter, "Any"))`
            // 才加筛选守卫 —— 留个 null 进去，那边会走进「加了筛选」的分支但对 null 判不出类型，
            // 而校验器读 PowerTriggers.CardFilterZh(_cardFilter) 也会炸。手写 JSON / 老工具写出来的
            // 存档里可能有 `"CardFilter": null`，所以在这里兜住。
            string v = string.IsNullOrWhiteSpace(value) ? "Any" : value;
            if (Set(ref _cardFilter, v)) { Raise(nameof(Summary)); Raise(nameof(Display)); }
        }
    }

    /// <summary>
    /// <b>老存档遗留字段</b>（V0.2.122~V0.2.125：这条时机要和同一只遗物上**同样勾了**的那几条
    /// 「都在同一个窗口里成立过」才触发）。
    ///
    /// V0.2.126 起关系模式移到整只遗物上（<see cref="RelicSpec.TriggerMode"/>）——
    /// 载入老存档时只要**有任意一条**勾过它，就切到「全部满足才触发」（用户已确认的迁移口径）。
    /// 迁移完置 false，新存档不再写它。界面不再绑它（没有对应的勾选框了）。
    /// </summary>
    public bool RequireAllTriggers
    {
        get => _requireAllTriggers;
        set { if (Set(ref _requireAllTriggers, value)) { Raise(nameof(Summary)); Raise(nameof(Display)); } }
    }

    /// <summary>这条时机属于哪只遗物（列表增删时由 <see cref="RelicSpec"/> 挂上）。</summary>
    [JsonIgnore]
    public RelicSpec? OwnerRelic { get; set; }

    /// <summary>
    /// <b>老存档遗留字段</b>（V0.2.122~V0.2.125：这条时机各自配的那份效果）。
    ///
    /// ⚠ 它**不能**加 <c>[JsonIgnore]</c>：老存档里的效果就是存在这里的，加了就读不进来
    /// （用户要求原话：「不能加 [JsonIgnore]，否则老存档里的效果读不进来」）。
    /// 载入时由 <c>ProfileFactory.NormalizeRelicEffects</c> 按顺序合并进
    /// <see cref="RelicSpec.Effects"/>，合并完**清空**，所以新存档里是空数组。
    /// </summary>
    public ObservableCollection<EffectSpec> Effects { get; set; } = new();

    /// <summary>界面用：这三栏只在选到那几条时机时才显示。</summary>
    [JsonIgnore]
    public bool ShowPowerFields => Kind is "PowerReached" or "PowerChanged" or "PowerRemoved";

    /// <summary>界面用：「只对哪类牌生效」只在「打出一张牌时 / 一张牌结算完毕时」时显示。
    /// ⚠ V0.2.173：这一栏以前只认 <c>CardPlayed</c> —— 「一张牌结算完毕时」（旧版的「打出一张牌后」）
    /// 明明也有牌型筛选（生成端一直在写 <c>cardPlay.Card.Type</c> 那句守卫），界面上却看不到、改不了。</summary>
    [JsonIgnore]
    public bool ShowCardFilterField => Kind is "CardPlayed" or "CardResolved";

    /// <summary>界面用：「花掉 N 点 + 本回合 / 本场战斗」那两行只在 花掉能量 / 花掉额外资源量 时显示。</summary>
    [JsonIgnore]
    public bool ShowSpendFields => Kind is "EnergySpent" or "StarsSpent";

    /// <summary>界面用：**触发时机上的数值**那一栏要不要显示（和状态那边同一套）。</summary>
    [JsonIgnore]
    public bool ShowAmountField => ShowSpendFields || Generation.CSharpCodeGen.TriggerSupportsAmount(Kind);

    /// <summary>「本回合 / 本场战斗」那一栏要不要显示（只有计数类用得到）。</summary>
    [JsonIgnore]
    public bool ShowScopeField => ShowSpendFields || Generation.CSharpCodeGen.TriggerAmountNeedsScope(Kind);

    /// <summary>数值那一栏的标题（按触发时机换说法）。</summary>
    [JsonIgnore]
    public string AmountLabel => Kind switch
    {
        "EnergySpent" or "StarsSpent" => "花掉多少点触发",
        "DamageReceived" or "DamageDealt" => "伤害至少多少才触发",
        "HpLost" => "失去生命至少多少才触发",
        "BlockGained" => "获得格挡至少多少才触发",
        "PowerReached" => "到达 N 层（层数跨过这一层那一下触发）",
        _ => "每累计几次触发",
    };

    /// <summary>数值那一行底下的说明。</summary>
    [JsonIgnore]
    public string SpendHint => Kind == "PowerReached"
        ? $"填 N = 层数**跨过** N 那一下才触发（例：N=3 时，「从 0/1/2 涨到 3 或更多」的那一次触发；"
          + $"已经是 3 层再加一层不会重复触发）。现在填的是 {Amount:0.##} 层。"
        : Amount <= 0m
        ? (Generation.CSharpCodeGen.TriggerValueExpr(Kind) is not null && !ShowSpendFields
            ? "留空 / 填 0 = 不过滤（老行为）；填 N = **只有数值 ≥ N 的那次才触发**。"
            : ShowSpendFields
                ? "留空 / 填 0 = **只要消耗就触发一次**（这个范围里只触发第一次，之后再不消耗不重复）；"
                  + "填 N = **每累计消耗 N 点触发一次**（例：3 = 每 3 点一次）。"
                : "留空 / 填 0 = 每次都触发（老行为）；填 N = **每累计 N 次触发一次**。")
          + (ShowScopeField ? "范围见下面那栏（本回合 / 本场战斗）。" : "")
        : Generation.CSharpCodeGen.TriggerValueExpr(Kind) is not null && !ShowSpendFields
            ? $"现在只有数值 ≥ {Amount:0.##} 的那次才触发。"
            : $"现在每累计 {(ShowSpendFields ? "花掉 " : "")}{Amount:0.##} {(ShowSpendFields ? "点" : "次")}触发一次，范围 = "
              + (_scope == "Combat" ? "本场战斗" : "本回合") + "。";

    /// <summary>界面用：触发时机那一行底下的说明（说清「留空 = 任意状态」和「跨过才触发」）。</summary>
    [JsonIgnore]
    public string Hint
    {
        get
        {
            if (!ShowPowerFields) return "";
            string who = string.IsNullOrWhiteSpace(_powerId)
                ? "自己身上**任何**状态"
                : $"自己身上的「{EffectCatalog.PowerName(_powerId, _powerId)}」";
            if (string.Equals(_kind, "PowerRemoved", StringComparison.Ordinal))
                return $"「某个状态被移除时」：自己身上的状态**被移除**那一刻触发（层数掉到 0 自然消失、"
                     + "被「清除状态」直接清掉都算）。现在盯的是" + who + "；把「盯哪个状态」留空就是任何状态被移除都触发。"
                     + "⚠ 本体没有「状态被移除」的全局钩子，这条是工具自己打补丁补的（用到时才会多生成一个 cs/PowerRemoval.cs）。"
                     + "「到达 N 层」那一栏在这种时机下**不用填**（不生效）。";
            if (string.Equals(_kind, "PowerChanged", StringComparison.Ordinal))
                return $"「某个状态层数变化时」：只有自己身上的状态层数**一变**（升或降）就触发（每层一次）。"
                     + $"现在盯的是{who}；把「盯哪个状态」留空就是任何状态的层数变化都触发。"
                     + "「到达 N 层」那一栏在这种时机下**不用填**（不生效）。";
            return $"「某个状态层数到达 N 层时」：只在自己身上的状态层数**跨过** {_amount:0.##} 层那一下触发"
                 + $"（之前不到、变化后到了 N 或更多；已经是 N 层再加一层不会重复触发）。现在盯的是{who}；"
                 + "把「盯哪个状态」留空就是任何状态层数变化都触发（那时可用效果自己的条件再筛）。"
                 + "本体的钩子是 AfterPowerAmountChanged（每层变化都会调，所以「上 3 层」只算跨过的那一下）。";
        }
    }

    /// <summary>界面用：列表里那一行（时机名 + 只对哪类牌 + 同时成立标记）。</summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            string when = EffectCatalog.RelicTriggers.FirstOrDefault(t => t.Id == _kind)?.Display ?? _kind;
            if (ShowCardFilterField && !string.Equals(_cardFilter, "Any", StringComparison.Ordinal))
                when += "（" + Sts2CharForge.Core.Effects.PowerTriggers.CardFilterZh(_cardFilter) + "牌）";
            // 「效果」现在挂在整只遗物上（V0.2.126），这条时机只说明「什么时候触发」——
            // 所以这里不再写「几条效果」，也不再有「同时成立」的勾选。
            return when;
        }
    }

    [JsonIgnore]
    public string Display => Summary;
}

/// <summary>
/// 一只遗物。实现了 <see cref="IRelicSlotRow"/>，好让遗物页左侧列表的「一行」和**空槽行**
/// （界面那边的占位项）用同一个模板 / 同一段自检代码读序号。
/// </summary>
public sealed class RelicSpec : SpecBase, IRelicSlotRow
{
    private string _name = "新遗物";
    private string _className = "";
    private string _rarity = "Common";
    private bool _isStartingRelic;
    private bool _inRelicPool = true;

    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>英文类名。留空 = 由工具按位置自动生成（见 Naming.RelicClassName）。</summary>
    public string ClassName
    {
        get => _className;
        set
        {
            if (!Set(ref _className, value)) return;
            // 「英文类名」那一栏下面的小字（自动 / 手填）跟着变
            Raise(nameof(ClassNameIsAuto));
            Raise(nameof(ClassNameHint));
        }
    }
    /// <summary>Starter / Common / Uncommon / Rare / Shop / Ancient（存的是英文 id，界面显示中文）</summary>
    public string Rarity { get => _rarity; set => Set(ref _rarity, value); }
    public bool IsStartingRelic { get => _isStartingRelic; set => Set(ref _isStartingRelic, value); }

    private bool _orobasReplace;

    /// <summary>
    /// V0.2.260（用户要求）：这只**遗物**就是「欧洛巴斯之触 TouchOfOrobas」替换初始遗物之后给的那一只。
    ///
    /// 本体只认它自己那张「初始遗物 → 强化遗物」表（燃烧之血→黑血、蛇之戒→龙之戒…），
    /// 自定义角色的初始遗物不在表里 ⇒ 它会给「头环 Circlet」。勾上之后生成一个 Harmony 前缀：
    /// 轮到本模组角色时用 <c>SetupForTests(初始遗物, 这只遗物)</c> 替本体定好（别的角色照旧）。
    /// </summary>
    public bool OrobasReplace { get => _orobasReplace; set => Set(ref _orobasReplace, value); }

    // ===== 左侧列表的序号（用户要求：每个遗物项最左侧显示序号，自上而下 1、2、3…）=====
    private int _ordinal;

    /// <summary>
    /// 这只遗物在 <c>Profile.Relics</c> 里的**当前位置**（1 起算），只给界面显示用。
    ///
    /// ⚠ 它**不写进存档**（<see cref="JsonIgnoreAttribute"/>）：序号是「当前的排序位置」，
    /// 不是跟着物品走的标记 —— 拖动换位置 / 删掉中间一只之后，由
    /// <c>Naming.RenumberRelicClassNames</c>（生成前）和界面自己的刷新重新发一遍。
    /// 写进存档的话，老存档里会多出一个字段（红线：老存档逐字节不变）。
    /// </summary>
    [JsonIgnore]
    public int Ordinal { get => _ordinal; set => Set(ref _ordinal, value); }

    // ===== 触发时机列表（用户要求：遗物页里能加多条时机；V0.2.126 起时机只管「什么时候触发」）=====
    private ObservableCollection<RelicTriggerSpec> _triggers = new();

    /// <summary>
    /// 构造函数：把「列表增删 → 给每条时机挂 OwnerRelic」这个监听挂上。
    ///
    /// ⚠ 为什么非要有它：字段初始化器 <c>= new()</c> **不走属性 setter**，所以不在构造函数里订阅的话，
    /// 直接 <c>relic.Triggers.Add(...)</c> 加的时机**永远不会**被挂上 OwnerRelic ——
    /// 生成端读时机字段时拿不到主人那只遗物。反序列化（老存档）走的是属性 setter，那条路本来就有订阅。
    /// </summary>
    public RelicSpec() => _triggers.CollectionChanged += OnTriggersChanged;

    /// <summary>这只遗物的触发时机（至少一条；老存档读进来会在这里合成一条）。</summary>
    public ObservableCollection<RelicTriggerSpec> Triggers
    {
        get => _triggers;
        set
        {
            var v = value ?? new ObservableCollection<RelicTriggerSpec>();
            if (ReferenceEquals(_triggers, v)) return;
            _triggers.CollectionChanged -= OnTriggersChanged;
            _triggers = v;
            _triggers.CollectionChanged += OnTriggersChanged;
            foreach (var t in _triggers) if (t is not null) t.OwnerRelic = this;
            Raise(nameof(Display));
            Raise(nameof(TriggerModeHint));
        }
    }

    /// <summary>列表增删时机：给每条时机挂上「主人」，并刷新列表那一行 / 关系开关底下那句说明。</summary>
    private void OnTriggersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (var t in (e.NewItems?.OfType<RelicTriggerSpec>() ?? Enumerable.Empty<RelicTriggerSpec>()))
            t.OwnerRelic = this;
        Raise(nameof(Display));
        Raise(nameof(Trigger));
        Raise(nameof(Effects));
        Raise(nameof(ShowTriggerPowerFields));
        Raise(nameof(TriggerHint));
        Raise(nameof(ShowTriggerAmountField));
        Raise(nameof(ShowTriggerScopeField));
        Raise(nameof(ShowTriggerCardFilterField));
        // 「全部满足才触发」只配 1 条时机时那句「不生效」的提示跟着条数变（见 TriggerModeHint）
        Raise(nameof(TriggerModeHint));
    }

    /// <summary>第 1 条时机（下面的老字段都是它的代理；列表为空时按需造一条出来）。</summary>
    [JsonIgnore]
    public RelicTriggerSpec First
    {
        get
        {
            if (Triggers.Count == 0) Triggers.Add(new RelicTriggerSpec());
            return Triggers[0];
        }
    }

    /// <summary>
    /// 老字段：触发时机（**第 1 条时机的代理**）。老存档 / 老命令行读的就是它。
    /// </summary>
    public string Trigger
    {
        get => First.Kind;
        set
        {
            if (string.Equals(First.Kind, value, StringComparison.Ordinal)) return;
            First.Kind = value;
            Raise(nameof(ShowTriggerPowerFields));
            Raise(nameof(TriggerHint));
            // 「触发时机上的数值」那一栏的显隐 / 标题 / 说明也都是按触发时机算出来的 —— 换了时机必须通知，
            // 否则界面**不会**当场出现那一栏（用户实测：得重新在左边列表点一次遗物才看得到）。
            Raise(nameof(ShowTriggerAmountField));
            Raise(nameof(ShowTriggerScopeField));
            Raise(nameof(TriggerAmountLabel));
            Raise(nameof(TriggerSpendHint));
            // 「只对哪类牌生效」那一栏也只在一种时机下显示（打出一张牌后），换了时机必须通知
            Raise(nameof(ShowTriggerCardFilterField));
            // V0.2.267：「层数变化方向」那一栏同理（只在「某个状态层数变化后」显示）
            Raise(nameof(ShowTriggerPowerDirection));
            Raise(nameof(Display));
        }
    }
    public bool InRelicPool { get => _inRelicPool; set => Set(ref _inRelicPool, value); }

    /// <summary>
    /// 老字段：「只对哪类牌生效」（**第 1 条时机的代理**）。
    /// <c>Any</c>（默认，不过滤）/ <c>Attack</c> / <c>Skill</c> / <c>Power</c> / <c>Curse</c> / <c>Status</c>，
    /// 以及自定义关键词（<c>Keyword:xxx</c>）与「组」（<c>Group:xxx</c>）—— 和状态那边是同一份候选
    /// （<see cref="Effects.PowerTriggers.CardFiltersFor"/>）。别的触发时机读它没有副作用（界面也不显示）。
    /// </summary>
    public string TriggerCardFilter
    {
        get => First.CardFilter;
        set { if (!string.Equals(First.CardFilter, value, StringComparison.Ordinal)) { First.CardFilter = value; Raise(nameof(Display)); } }
    }

    /// <summary>界面用：「只对哪类牌生效」那一栏只在触发时机是「打出一张牌后」时显示。</summary>
    [JsonIgnore]
    public bool ShowTriggerCardFilterField => First.ShowCardFilterField;

    /// <summary>
    /// 这只遗物的效果列表（V0.2.126 起是**整只遗物唯一的一份**）。
    ///
    /// 用户要求原话：「中间的触发时机并不是每条各配效果，而是决定整只遗物的触发时机，
    /// 只有同时满足或者满足其中一个才能触发遗物的效果，另外效果右侧应能添加多个效果种类」
    /// —— 所以效果挂在遗物上（右侧效果栏），触发时机只决定「什么时候跑这份效果」。
    ///
    /// <para>⚠ V0.2.122~V0.2.125 里它是「第 1 条时机的 Effects」的**代理**；现在是真字段。
    /// 老存档里效果存在每条时机上（<see cref="RelicTriggerSpec.Effects"/>），
    /// 载入时由 <c>ProfileFactory.NormalizeRelicEffects</c> 合并上来（幂等）。</para>
    /// </summary>
    public ObservableCollection<EffectSpec> Effects
    {
        get => _effects;
        set
        {
            var v = value ?? new ObservableCollection<EffectSpec>();
            if (ReferenceEquals(_effects, v)) return;
            _effects = v;
            Raise(nameof(Display));
        }
    }

    private ObservableCollection<EffectSpec> _effects = new();

    /// <summary>这只遗物配的效果（V0.2.125 及更早的代码到处读 <c>AllEffects</c>，留着当兼容别名）。</summary>
    [JsonIgnore]
    public IEnumerable<EffectSpec> AllEffects => _effects.Where(e => e is not null);

    // ===== 触发时机「某个状态层数到达 N 层时」（用户要求）—— 老字段，代理到第 1 条时机 =====
    /// <summary>
    /// 「某个状态层数到达 N 层时」触发时机要**盯哪个状态**（本体的 Power 类名）。
    /// 留空 = 自己身上**任何**状态的层数变化都触发（那时可以用效果自己的条件再筛）。
    /// </summary>
    public string? TriggerPowerId
    {
        get => First.PowerId;
        set { if (!string.Equals(First.PowerId, value, StringComparison.Ordinal)) { First.PowerId = value; Raise(nameof(TriggerHint)); Raise(nameof(Display)); } }
    }

    /// <summary>
    /// V0.2.266（用户要求）：层数变化只看「上升」还是只看「下降」（代理到第 1 条时机，
    /// 和 <see cref="TriggerPowerId"/> 同一个写法）。空 = 上升 / 下降都算。
    /// </summary>
    public string TriggerPowerDirection
    {
        get => First.PowerDirection;
        set
        {
            if (string.Equals(First.PowerDirection, value, StringComparison.Ordinal)) return;
            First.PowerDirection = value;
            Raise(nameof(TriggerHint));
            Raise(nameof(Display));
            Raise(nameof(ShowTriggerPowerDirection));
        }
    }

    /// <summary>
    /// V0.2.267（用户报「触发时机『某个状态层数变化后』没看到『层数变化方向』」）：
    /// 锻造遗物那个属性面板（DataTemplate 的 DataContext 就是 RelicSpec）也要显示方向那一栏 ——
    /// 以前只在遗物页 / 状态页的多时机编辑器里加了，这个面板漏了。判据和 <see cref="RelicTriggerSpec.ShowPowerDirectionField"/> 一致。
    /// </summary>
    [JsonIgnore]
    public bool ShowTriggerPowerDirection => string.Equals(First.Kind, "PowerChanged", StringComparison.Ordinal);

    /// <summary>
    /// 「到达多少层」：只有「这一次变化**跨过**了 N 层」那一下才触发
    /// （之前不到 N、变化后到了 N 或更多；已经是 N 层再加一层不重复触发）。
    /// </summary>
    public decimal TriggerAmount
    {
        get => First.Amount;
        set { if (First.Amount != value) { First.Amount = value; Raise(nameof(TriggerHint)); Raise(nameof(TriggerSpendHint)); Raise(nameof(Display)); } }
    }

    /// <summary>界面用：这两个输入框只在触发时机选了「某状态层数到达 N / 变化 / 被移除」时显示。</summary>
    [JsonIgnore]
    public bool ShowTriggerPowerFields => First.ShowPowerFields;

    // ===== 「花掉 N 点能量 / 额外资源量」+「本回合 / 本场战斗」—— 老字段，代理到第 1 条时机 =====
    /// <summary>「花掉多少能量 / 额外资源量」的统计范围：<c>Turn</c> = 本回合（默认）/ <c>Combat</c> = 本场战斗。</summary>
    public string TriggerScope
    {
        get => First.Scope;
        set { if (!string.Equals(First.Scope, value, StringComparison.Ordinal)) { First.Scope = value; Raise(nameof(TriggerSpendHint)); } }
    }

    /// <summary>界面用：「花掉 N 点 + 本回合 / 本场战斗」那两行只在 花掉能量 / 花掉额外资源量 时显示。</summary>
    [JsonIgnore]
    public bool ShowTriggerSpendFields => First.ShowSpendFields;

    /// <summary>数值那一栏的标题（按触发时机换说法）。</summary>
    [JsonIgnore]
    public string TriggerAmountLabel => First.AmountLabel;

    /// <summary>界面用：**触发时机上的数值**那一栏要不要显示（和状态那边同一套）。</summary>
    [JsonIgnore]
    public bool ShowTriggerAmountField => First.ShowAmountField;

    /// <summary>「本回合 / 本场战斗」那一栏要不要显示（只有计数类用得到）。</summary>
    [JsonIgnore]
    public bool ShowTriggerScopeField => First.ShowScopeField;

    /// <summary>数值那一行底下的说明。</summary>
    [JsonIgnore]
    public string TriggerSpendHint => First.SpendHint;

    /// <summary>界面用：触发时机那一行底下的说明（说清「留空 = 任意状态」和「跨过才触发」）。</summary>
    [JsonIgnore]
    public string TriggerHint => First.Hint;

    // ===== 触发时机之间的关系（V0.2.126，用户要求）=====
    //
    // 用户原话：「中间的触发时机并不是每条各配效果，而是决定整只遗物的触发时机，只有同时满足
    // 或者满足其中一个才能触发遗物的效果」。已确认的两档：
    //   · Any（**默认**）：任意一条时机成立就触发遗物的效果；
    //   · All（全部满足才触发）：所有时机都在同一个窗口里成立过才触发一次（效果只跑一次，跑完清标记）。
    // 选 All 时才有「重置范围」（AndScope）：本回合（默认，每回合重新凑）/ 本场战斗（整场凑一次）。
    private string _triggerMode = "Any";

    /// <summary>触发时机之间的关系：<c>Any</c>（默认，满足其中一个就触发）/ <c>All</c>（全部满足才触发）。</summary>
    public string TriggerMode
    {
        get => _triggerMode;
        set
        {
            string v = string.Equals(value, "All", StringComparison.Ordinal) ? "All" : "Any";
            if (!Set(ref _triggerMode, v)) return;
            Raise(nameof(TriggerModeIsAny));
            Raise(nameof(TriggerModeIsAll));
            Raise(nameof(ShowAndScope));
            Raise(nameof(AndScopeHint));
            Raise(nameof(TriggerModeHint));
            Raise(nameof(Display));
        }
    }

    /// <summary>界面用：关系开关的两个单选按钮（WPF 的 RadioButton 绑 bool，所以给两个属性）。</summary>
    [JsonIgnore]
    public bool TriggerModeIsAny
    {
        get => !IsAllTriggers;
        set { if (value) TriggerMode = "Any"; }
    }

    /// <summary>界面用：见 <see cref="TriggerModeIsAny"/>。</summary>
    [JsonIgnore]
    public bool TriggerModeIsAll
    {
        get => IsAllTriggers;
        set { if (value) TriggerMode = "All"; }
    }

    /// <summary>生成端 / 文案 / 校验都用它判「全部满足才触发」。</summary>
    [JsonIgnore]
    public bool IsAllTriggers => string.Equals(_triggerMode, "All", StringComparison.Ordinal);

    private string _andScope = "Turn";

    /// <summary>
    /// 「全部满足才触发」时那几条时机的「成立窗口」重置范围：
    /// <c>Turn</c> = 本回合（默认，每回合重新凑）/ <c>Combat</c> = 本场战斗（整场凑一次就够）。
    /// 只在 <see cref="TriggerMode"/> = <c>All</c> 时显示 / 生效（Any 模式下每条时机各自独立，不需要窗口）。
    /// </summary>
    public string AndScope
    {
        get => _andScope;
        set
        {
            string v = string.IsNullOrWhiteSpace(value) ? "Turn" : value;
            if (Set(ref _andScope, v)) Raise(nameof(AndScopeHint));
        }
    }

    /// <summary>界面用：「重置范围」那一行只在关系的「全部满足才触发」档显示。</summary>
    [JsonIgnore]
    public bool ShowAndScope => IsAllTriggers;

    [JsonIgnore]
    public string AndScopeHint => _andScope == "Combat"
        ? "本场战斗：整场战斗里凑齐一次就行（换战斗重新算）。"
        : "本回合：每个回合重新凑（回合结束清零）。";

    /// <summary>界面用：关系开关底下那句说明。</summary>
    [JsonIgnore]
    public string TriggerModeHint => IsAllTriggers
        ? (Triggers.Count(t => t is not null) <= 1
            ? "⚠ 现在只有 1 条触发时机：「全部满足」和「满足其中一个」是同一条，所以这条设置**不生效**（生成前的检查也会提示）。"
            : "所有触发时机都成立过 → 才跑一次遗物的效果（效果只跑一次，跑完重新凑；凑不齐就永远不跑）。")
        : "任意一条触发时机成立 → 就跑一次遗物的效果（每条时机互相独立）。";

    private bool _isForged;

    /// <summary>
    /// 这只遗物是**锻造页**造出来的（需求：锻造页也能做遗物）。
    /// 列表里的名字前面带 <c>*</c>；遗物页选中它时右侧只提示「请在锻造页查看」。
    /// </summary>
    public bool IsForged
    {
        get => _isForged;
        set
        {
            if (!Set(ref _isForged, value)) return;
            Raise(nameof(Display));
            Raise(nameof(ForgeEditingHint));
        }
    }

    /// <summary>界面用：锻造遗物在遗物页右侧显示的那句提示。</summary>
    [JsonIgnore]
    public string ForgeEditingHint => "这只遗物是在「锻造」页用节点图做的 —— 请到「锻造」页查看与修改。";

    /// <summary>锻造节点图（这块布）。null / 空图 = 不接管生成。</summary>
    public ForgeGraphSpec? ForgeGraph { get; set; }

    /// <summary>条件选项：整个遗物的效果只有在条件满足时才触发。</summary>
    public ConditionSpec Condition { get; set; } = new();

    /// <summary>遗物图标（用户上传的图片路径；留空 = 用本体遗物图标占位）。建议 256×256 PNG</summary>
    public string? Icon { get; set; }
    /// <summary>遗物未发现时的描边图标（留空 = 复用图标本体）</summary>
    public string? IconOutline { get; set; }

    // ===== 自定义描述（用户要求：遗物也能自己写描述）=====
    private string _customDescription = "";
    private bool _customDescriptionReplaces;

    /// <summary>
    /// 自定义描述（留空 = 用自动生成的那段）。
    /// 可以写本体占位符：<c>{Damage:diff()}</c>（升级后会显示升级值）/ <c>{PoisonPower}</c> 之类，
    /// 变量名要和效果里用到的动态变量一致 —— 界面上的「填入参数」按钮会列出能用的那些。
    /// </summary>
    public string CustomDescription
    {
        get => _customDescription;
        set { if (Set(ref _customDescription, value ?? "")) Raise(nameof(HasCustomDescription)); }
    }

    /// <summary>勾上 = 只用你写的这段（不勾 = 自动描述 + 换行 + 你写的这段）。</summary>
    public bool CustomDescriptionReplaces
    {
        get => _customDescriptionReplaces;
        set { if (Set(ref _customDescriptionReplaces, value)) Raise(nameof(Display)); }
    }

    [JsonIgnore]
    public bool HasCustomDescription => !string.IsNullOrWhiteSpace(_customDescription);

    /// <summary>界面用：左侧列表里那一行最左边的序号（1 起算，就是它当前的位置）。</summary>
    [JsonIgnore]
    public string OrdinalText => _ordinal >= 1 ? _ordinal + "." : "";

    /// <summary>界面用：真遗物行当然不是空槽（空槽行是界面那边的 <c>RelicSlotPlaceholder</c>）。</summary>
    [JsonIgnore]
    public bool IsPlaceholder => false;

    // ===== 「自动类名」的痕迹（用户要求第 2 条：自动的跟序号走、手填的不动）=====
    private int _lastAutoOrdinal;

    /// <summary>
    /// 上一次生成时，这只遗物的「自动类名」是第几号（对应 <c>&lt;角色类名&gt;Relic&lt;它&gt;</c>）。
    ///
    /// ⚠ 为什么要有它：英文类名留空时，**类名本身要保持空白**（一写实就再也分不出
    /// 「工具生成的」和「用户手打的」—— 见 <c>Naming.RenumberRelicClassNames</c> 的注释）。
    /// 但界面上「英文类名」那一栏总得让用户看到「不填会用哪个名字」，所以单独把**序号**记在这里，
    /// 由 <see cref="AutoClassNameHint"/> 拼出完整名字。不写进存档（老存档一个字节都不变）。
    /// </summary>
    [JsonIgnore]
    public int LastAutoOrdinal { get => _lastAutoOrdinal; set => Set(ref _lastAutoOrdinal, value); }

    /// <summary>界面用：不填类名时，这只遗物会用哪个自动名（还没生成过就按当前序号推）。</summary>
    [JsonIgnore]
    public string AutoClassNameHint
    {
        get
        {
            int n = _lastAutoOrdinal >= 1 ? _lastAutoOrdinal : _ordinal;
            return "<角色类名>Relic" + (n >= 1 ? n : 1);
        }
    }

    /// <summary>
    /// 界面用：这只遗物的英文类名现在是不是「自动生成的那一个」
    /// （判据 = <see cref="Generation.Naming.IsAutoRelicClassNameFor"/>，按名字形状认；
    /// 认不出的都当手填）。
    /// 「英文类名」那一栏下面的小字用它说明「重新生成时会跟着序号走 / 不会被改」。
    /// </summary>
    [JsonIgnore]
    public bool ClassNameIsAuto =>
        !Generation.Naming.IsValidIdentifier(_className)
        || Generation.Naming.IsAutoRelicClassNameFor(null, _className);

    /// <summary>界面用：「英文类名」那一栏下面那行小字。</summary>
    [JsonIgnore]
    public string ClassNameHint => ClassNameIsAuto
        ? "留空 = 自动。现在对应 " + AutoClassNameHint + "（重新生成 / 构建时按左边列表的序号重算，拖动换位置就跟着变）。"
        : "现在这个名字是**你手填的**：重新生成 / 构建都不会改它（想让它跟着序号走就把这一栏清空）。";

    /// <summary>界面用：左侧列表那一行的提示（序号 + 类名是自动还是手填）。</summary>
    [JsonIgnore]
    public string RowTip => "第 " + (_ordinal >= 1 ? _ordinal : 0) + " 位 ｜ "
        + (ClassNameIsAuto ? "英文类名自动（跟序号走）" : "英文类名手填（不会自动改）") + " ｜ 可以拖动这一行换位置";

    [JsonIgnore]
    public string Display =>
        $"{(IsForged ? "*" : "")}{Name}  ｜ {EffectCatalog.RelicRarityZh(Rarity)} ｜ {Trigger}{(IsStartingRelic ? " ｜ 初始遗物" : "")}{(InRelicPool ? "" : " ｜ 不入池")}"
        + (Condition.IsNone ? "" : "  ｜ 条件：" + Condition.DisplayShort)
        // 「只对哪类牌生效」（第 9 批 ④）：选了筛选就在列表里也写出来（和状态页那条时机的写法一致）
        + (ShowTriggerCardFilterField && !string.Equals(First.CardFilter, "Any", StringComparison.Ordinal)
            ? "  ｜ 只对" + PowerTriggers.CardFilterZh(First.CardFilter) + "牌生效"
            : "")
        // 「多条触发时机」（第 10 批）：不止一条时把条数也写出来，一眼看得出这只遗物挂了几条；
        // 「全部满足才触发」（V0.2.126）也标出来 —— 否则光看列表看不出关系模式。
        + (Triggers.Count > 1
            ? "  ｜ " + Triggers.Count + " 条时机" + (IsAllTriggers ? "（全部满足才触发）" : "（满足其中一个就触发）")
            : "");
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
    private bool _replaceInVanillaText = false;

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
    public string Name { get => _name; set => Set(ref _name, value); }    /// <summary>英文类名（留空自动生成 &lt;角色类名&gt;Power&lt;序号&gt;）</summary>
    public string ClassName { get => _className; set => Set(ref _className, value); }
    /// <summary>描述（留空自动按触发器生成一句话；{Amount} = 层数占位符）</summary>
    public string Description { get => _description; set => Set(ref _description, value); }
    /// <summary>图标（本机图片路径，留空 = 用本体的 missing_power 占位图）</summary>
    public string Icon { get => _icon; set => Set(ref _icon, value); }
    /// <summary>Buff / Debuff（影响状态栏图标的边框颜色与层数数字的默认颜色）</summary>
    public string Type { get => _type; set => Set(ref _type, value); }
    /// <summary>层数数字颜色（RRGGBB，留空 = 本体默认：减益红、其它米白）</summary>
    public string AmountColor { get => _amountColor; set => Set(ref _amountColor, value); }

    // ==================== 覆盖血条（需求：「增加一个像中毒一样覆盖血条的显示，只需要调整 RRGGBB」） ====================
    //
    // 本体血条上那截绿色（中毒）其实是一张**纯白**九宫格（images/ui/combat/health_bar_fill.png），
    // 颜色完全由节点自己的 self_modulate 决定（红条 CC0C0C / 中毒 79C03C / 灾厄紫色都是同一张图）。
    // 所以「换个颜色」天生支持，只要把那一截摆到该在的位置 —— 这正是本补丁在做的事。
    private string _hpBarColor = "";

    /// <summary>
    /// 血条覆盖色（RRGGBB，留空 = 不覆盖）。填了之后，身上有这个状态的生物，血条右端会出现一截这个颜色的条。
    ///
    /// 几何**完全照抄本体中毒**（见 NHealthBar.RefreshForeground）：
    ///   层数 = 会失去的血量 —— 覆盖的是「当前血量」到「当前血量 - 层数」那一段（右端），
    ///   同时红条也跟着缩短（中毒就是这个观感：红的退到覆盖段左边）。
    ///   层数 ≥ 当前血量 = 整条盖住（和中叠的「致命」那一档一样）。
    /// 和本体中毒 / 灾厄共存：那两截先在，我们这一截接在它的左边（= 层数继续往下扣），红条同步再缩。
    ///
    /// 颜色那一栏就是**唯一**要填的东西（需求原文：「只需要调整RRGGBB就行」）。
    /// </summary>
    public string HpBarColor
    {
        get => _hpBarColor;
        set
        {
            if (!Set(ref _hpBarColor, value ?? "")) return;
            // 「从哪边开始盖」那一栏按它显示（没填颜色就没有可盖的东西）
            Raise(nameof(HasHpBarOverlay));
            Raise(nameof(ShowHpBarSide));
            Raise(nameof(Display));
        }
    }

    private string _hpBarSide = HpBarSides.Right;

    /// <summary>
    /// 这一截从血条的**哪一端**开始盖（需求原文：「可以选择是从血条左边开始覆盖还是右边开始覆盖」）。
    /// 空 = 从右边（默认，和本体中毒一样）；<c>Left</c> = 从左边（红条从左边让位）。
    /// 同一条状态只盖一边；同一边有几条就依次往里堆，中间剩下的是红条。
    /// </summary>
    public string HpBarSide
    {
        get => _hpBarSide;
        set
        {
            if (!Set(ref _hpBarSide, HpBarSides.Normalize(value))) return;
            Raise(nameof(HpBarSideZh));
            Raise(nameof(Display));
        }
    }

    /// <summary>界面上那个「从哪边开始盖」下拉要不要显示（填了颜色才有意义）。</summary>
    [JsonIgnore]
    public bool ShowHpBarSide => HasHpBarOverlay;

    /// <summary>界面 / 列表里显示的「左边 / 右边」。</summary>
    [JsonIgnore]
    public string HpBarSideZh => HpBarSides.Zh(_hpBarSide);

    /// <summary>
    /// 这个状态要不要覆盖血条（生成端只认这一个判据）。
    /// 注意「是不是合法 RRGGBB」由校验器管：写坏了会**报错**拦住，不会静默不上色。
    /// </summary>
    [JsonIgnore]
    public bool HasHpBarOverlay => !string.IsNullOrWhiteSpace(_hpBarColor);

    /// <summary>只叠一层（本体有些状态不叠层，重复施加只刷新）</summary>
    public bool Single { get => _single; set => Set(ref _single, value); }
    /// <summary>每回合减几层（0 = 不衰减；减到 0 就移除）</summary>
    public int DecayPerTurn { get => _decayPerTurn; set => Set(ref _decayPerTurn, value); }
    /// <summary>回合结束时直接移除（临时状态：不管多少层，本回合结束就没了）</summary>
    public bool RemoveAtTurnEnd { get => _removeAtTurnEnd; set => Set(ref _removeAtTurnEnd, value); }

    private bool _allowNegativeLayers;

    /// <summary>
    /// **允许负层数**（V0.2.239，用户问「在自定义状态增加状态层数能否到负值」）。
    ///
    /// <para>不勾（默认）：层数最低到 0 —— 「施加增益/减益」填负数时只从**目标身上已有的层数**里扣
    /// （<c>GetPower</c> + <c>ModifyAmount(-N)</c>，扣到 0 就去掉这个状态，和本体「虚弱 / 易伤」一样）。</para>
    ///
    /// <para>勾上：生成的状态类带 <c>public override bool AllowNegative =&gt; true;</c>，
    /// 和本体 力量 / 敏捷 / 专注 同一个口径 —— 目标身上**本来 0 层也能扣成 -N**，
    /// 「施加增益/减益」填负数会直接施加负值（本体「尖啸 PiercingWail」就是这么扣敌人力量的）。
    /// 注意连带效果（都是本体那套）：负层数那一档在游戏里按**减益**显示（红框红字）、
    /// 层数回到正好 0 时状态会被移除、「每回合衰减」仍然是「减到 0 就移除」（不会继续往负数扣）。</para>
    /// </summary>
    public bool AllowNegativeLayers { get => _allowNegativeLayers; set => Set(ref _allowNegativeLayers, value); }

    private string _costReduceScope = "";
    private decimal _costReduceAmount = 1;
    private bool _costReducePerStack;

    /// <summary>
    /// **费用修正的范围**（V0.2.248，用户要求「减少指定卡牌类别的费用（类似腐化、刺破帷幕）」）。
    ///
    /// <para>存的是「范围」那套 Id（<see cref="Effects.EffectCatalog.SpawnFiltersFor"/>：攻击 / 技能 / 能力 /
    /// 诅咒 / 状态 / 任务 / <c>Keyword:xxx</c> 自定义关键词那一组 / <c>Group:xxx</c> 每个组）。
    /// **空串 = 不启用**（生成端一行都不写，老存档逐字不变）。</para>
    ///
    /// <para>生成的是本体的 <c>AbstractModel.TryModifyEnergyCostInCombatLate</c> —— 和
    /// 腐化 Corruption（技能变 0 费）、刺破帷幕 Veilpiercer（虚无牌变 0 费且每打一张扣 1 层）、
    /// 免费攻击 FreeAttackPower 用的是**同一个钩子**。</para>
    /// </summary>
    public string CostReduceScope
    {
        get => _costReduceScope;
        set => Set(ref _costReduceScope, value ?? "");
    }

    /// <summary>
    /// 这一类牌**减多少费**（默认 1）。<c>0</c> = 直接变成 0 费（腐化那种）。
    /// 最低减到 0：生成出来是 <c>Math.Max(0m, 原价 - N)</c>，不会变成负费。
    /// </summary>
    public decimal CostReduceAmount { get => _costReduceAmount; set => Set(ref _costReduceAmount, value); }

    /// <summary>
    /// 勾上 = **按层数**减（每层各减一次，例：3 层 × 每层 1 费 = 减 3 费）。
    /// 不勾（默认）= 只看「有没有这个状态」，和腐化一样减固定那么多。
    /// </summary>
    public bool CostReducePerStack { get => _costReducePerStack; set => Set(ref _costReducePerStack, value); }

    /// <summary>不勾选就先不生效（留着配置以后再用）</summary>
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>触发时机 + 每个时机下要执行的效果。</summary>
    public ObservableCollection<PowerTriggerSpec> Triggers { get; set; } = new();

    private string _andScope = "Turn";

    /// <summary>
    /// 「要和其他时机同时成立」的**重置窗口**（用户要求：本回合 / 本场战斗）：
    /// <c>Turn</c> = 每个回合重新开始算（默认）/ <c>Combat</c> = 整场战斗累计一次。
    /// 只有这个状态上有 ≥ 2 条时机勾了「要和其他时机同时成立」时才显示 / 生效。
    /// </summary>
    public string AndScope
    {
        get => _andScope;
        set { if (Set(ref _andScope, string.IsNullOrWhiteSpace(value) ? "Turn" : value)) Raise(nameof(AndScopeZh)); }
    }

    [JsonIgnore] public string AndScopeZh => _andScope == "Combat" ? "本场战斗" : "本回合";

    /// <summary>勾了「要和其他时机同时成立」的那几条时机。</summary>
    [JsonIgnore]
    public IReadOnlyList<PowerTriggerSpec> AndTriggers =>
        Triggers.Where(t => t is not null && t.RequireAllTriggers).ToList();

    /// <summary>界面用：「重置范围」那一行要不要显示（有 ≥ 2 条时机勾了才显示）。</summary>
    [JsonIgnore] public bool ShowAndScope => AndTriggers.Count >= 2;

    /// <summary>
    /// 锻造节点图（这块布）。为 null / 空 = 这个状态不用节点图，触发时机照上面 <see cref="Triggers"/> 那套走。
    /// 需求：锻造页也要能管「自定义状态」—— 加了这个字段，选中它时右边那块布才有内容可画。
    /// </summary>
    public ForgeGraphSpec? ForgeGraph { get; set; }

    /// <summary>界面用：锻造出来的自定义状态在「自定义状态」页右侧显示的那句提示。</summary>
    [JsonIgnore]
    public string ForgeEditingHint => "这个状态是在「锻造」页用节点图做的 —— 触发时机的效果在那边那块布上改。";

    [JsonIgnore]
    public string Display
    {
        get
        {
            string who = string.IsNullOrWhiteSpace(Name) ? "(还没起名)" : Name;
            string kind = Type == "Debuff" ? "减益" : "增益";
            string tg = Triggers.Count == 0 ? "还没配触发时机" : string.Join(" + ", Triggers.Select(t => t.Display));
            string bar = HasHpBarOverlay ? $"　覆盖血条 {_hpBarColor.Trim().TrimStart('#')}（{HpBarSideZh}）" : "";
            return (Enabled ? "" : "[停用] ") + $"{who}（{kind}）　{tg}{bar}";
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
    public string Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            Raise(nameof(Display));
            Raise(nameof(Summary));
            Raise(nameof(NeedsPower));
            Raise(nameof(ShowPowerDirectionField));
            Raise(nameof(NeedsCardFilter));
            // 和遗物那边同一个坑：这两栏的显隐 / 标题 / 说明都是按触发时机算出来的，换了时机得通知
            //（不然「到达 N 层」/「每累计几次触发」那栏不会当场出现）。
            Raise(nameof(NeedsAmount));
            Raise(nameof(ShowTriggerAmountField));
            Raise(nameof(ShowTriggerScopeField));
            Raise(nameof(TriggerAmountLabel));
            Raise(nameof(TriggerSpendHint));
        }
    }

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

    private string _powerDirection = "";

    /// <summary>
    /// V0.2.266（用户要求）：只有「某个状态层数变化后」用得到 ——
    /// 空 = 上升 / 下降都算（老行为），<c>Up</c> = 只在层数**上升**时触发，<c>Down</c> = 只在层数**下降**时触发。
    /// </summary>
    public string PowerDirection
    {
        get => _powerDirection;
        set { if (Set(ref _powerDirection, value ?? "")) { Raise(nameof(Summary)); Raise(nameof(Display)); } }
    }

    /// <summary>界面用：方向那一栏只在「某个状态层数变化后」显示。</summary>
    [JsonIgnore]
    public bool ShowPowerDirectionField => Kind == "PowerChanged";

    [JsonIgnore] public bool NeedsPower => Kind is "PowerChanged" or "PowerReached" or "PowerRemoved";
    /// <summary>「到达 N 层」那一栏要不要显示（用户要求：两边触发时机菜单相同 → 状态这边也要能填 N）。</summary>
    [JsonIgnore] public bool NeedsAmount => Kind == "PowerReached";

    private decimal _triggerAmount = 1m;

    /// <summary>
    /// 「到达 N 层」那个数值（只有「某个状态层数到达 N 层时」用得到）：
    /// 判的是「这一次变化**跨过**了 N 层」（之前不到 N、变化后到了 N 或更多），和遗物那边同一个口径。
    /// </summary>
    public decimal TriggerAmount
    {
        get => _triggerAmount;
        set { if (Set(ref _triggerAmount, value)) { Raise(nameof(Display)); Raise(nameof(Summary)); Raise(nameof(TriggerSpendHint)); } }
    }

    // ===== 「花掉 N 点能量 / 额外资源量」+「本回合 / 本场战斗」（用户要求：两边触发时机一致）=====
    private string _triggerScope = "Turn";

    /// <summary>「花掉多少能量 / 额外资源量」的统计范围：<c>Turn</c> = 本回合（默认）/ <c>Combat</c> = 本场战斗。</summary>
    public string TriggerScope
    {
        get => _triggerScope;
        set { if (Set(ref _triggerScope, string.IsNullOrWhiteSpace(value) ? "Turn" : value)) Raise(nameof(TriggerSpendHint)); }
    }

    /// <summary>界面用：「花掉 N 点 + 本回合 / 本场战斗」那两行只在 花掉能量 / 花掉额外资源量 时显示。</summary>
    [JsonIgnore]
    public bool ShowTriggerSpendFields => Kind is "EnergySpent" or "StarsSpent";


    /// <summary>
    /// 界面用：**触发时机上的数值**那一栏要不要显示（用户要求）。
    /// 支持的是：花掉能量 / 花掉额外资源量（每 N 点）、打出一张牌后 / 消耗 / 弃掉 / 抽到 / 生成卡牌时（每 N 次）、
    /// 自己受到伤害后 / 造成伤害后（伤害 ≥ N）、自己失去生命时 / 获得格挡后（数值 ≥ N）。
    /// </summary>
    [JsonIgnore]
    public bool ShowTriggerAmountField => ShowTriggerSpendFields || Generation.CSharpCodeGen.TriggerSupportsAmount(Kind);

    /// <summary>「本回合 / 本场战斗」那一栏要不要显示（只有计数类用得到：花能量 / 花资源量 / 打牌 / 消耗…）。</summary>
    [JsonIgnore]
    public bool ShowTriggerScopeField => ShowTriggerSpendFields || Generation.CSharpCodeGen.TriggerAmountNeedsScope(Kind);

    /// <summary>数值那一栏的标题（按触发时机换说法）。</summary>
    [JsonIgnore]
    public string TriggerAmountLabel => Kind switch
    {
        "EnergySpent" or "StarsSpent" => "花掉多少点触发",
        "DamageReceived" or "DamageDealt" => "伤害至少多少才触发",
        "HpLost" => "失去生命至少多少才触发",
        "BlockGained" => "获得格挡至少多少才触发",
        // V0.2.152：这一档以前界面上**没有输入框**（TriggerSupportsAmount 漏了 PowerReached），
        // N 永远停在默认值 1 —— 用户报的两条都是它。
        "PowerReached" => "到达 N 层（层数跨过这一层那一下触发）",
        _ => "每累计几次触发",
    };

    /// <summary>「花掉 N 点」那一行底下的说明。</summary>
    [JsonIgnore]
    public string TriggerSpendHint => Kind == "PowerReached"
        ? $"填 N = 层数**跨过** N 那一下才触发（例：N=3 时，「从 0/1/2 涨到 3 或更多」的那一次触发；"
          + $"已经是 3 层再加一层不会重复触发）。现在填的是 {TriggerAmount:0.##} 层。"
        : TriggerAmount <= 0m
        ? (Kind is "EnergySpent" or "StarsSpent"
            ? "留空 / 填 0 = **只要消耗就触发一次**（这个范围里只触发第一次，之后再消耗不重复）；"
              + "填 N = **每累计消耗 N 点触发一次**（例：3 = 每 3 点一次）。"
            : Generation.CSharpCodeGen.TriggerValueExpr(Kind) is not null
                ? "留空 / 填 0 = 不过滤（老行为）；填 N = **只有数值 ≥ N 的那次才触发**。"
                : "留空 / 填 0 = 每次都触发（老行为）；填 N = **每累计 N 次触发一次**。")
          + (ShowTriggerScopeField ? "范围见下面那栏（本回合 / 本场战斗）。" : "")
        : Kind is "EnergySpent" or "StarsSpent"
            ? $"现在每累计花掉 {TriggerAmount:0.##} 点触发一次，范围 = " + (_triggerScope == "Combat" ? "本场战斗" : "本回合") + "。"
            : Generation.CSharpCodeGen.TriggerValueExpr(Kind) is not null
                ? $"现在只有数值 ≥ {TriggerAmount:0.##} 的那次才触发。"
                : $"现在每累计 {TriggerAmount:0.##} 次触发一次，范围 = " + (_triggerScope == "Combat" ? "本场战斗" : "本回合") + "。";
    [JsonIgnore] public bool NeedsCardFilter => Kind == "CardPlayed";

    private bool _requireAllTriggers;

    /// <summary>
    /// 「要和其他时机同时成立」（用户要求）：勾上之后，这条时机**只有在同一个状态上所有勾了它的时机
    /// 都成立过**（同一个窗口内）才会触发 —— 也就是「几条时机同时满足才触发」。
    ///
    /// 语义（说清楚，免得踩坑）：参与的那几条时机各自照常判断自己那件事；每成立一条就打个标记，
    /// **最后凑齐的那一条**会跑它自己的效果，然后把这一轮的标记清掉（一个窗口只触发一次）。
    /// 所以「两条上都写同样的效果」不会触发两次 —— 效果写在哪一条上，就由那条（如果它是最后凑齐的）跑；
    /// 想「凑齐后做一件事」最稳的写法是：效果只写在其中一条上（另一条只管凑数）。
    ///
    /// 重置窗口由 <see cref="CustomPowerSpec.AndScope"/> 决定（本回合 / 本场战斗）——
    /// 「本场战斗」不需要额外代码（状态本来就是每场战斗一个新实例），「本回合」按回合数自动清零。
    /// </summary>
    public bool RequireAllTriggers
    {
        get => _requireAllTriggers;
        set { if (Set(ref _requireAllTriggers, value)) { Raise(nameof(AndHint)); Raise(nameof(ShowAndScope)); Raise(nameof(Display)); } }
    }

    /// <summary>界面用：那个勾选框下面的一句说明。</summary>
    [JsonIgnore]
    public string AndHint => RequireAllTriggers
        ? "已勾：这条时机要和**同一个状态上别的也勾了它的时机**都在同一个窗口里成立过，才会触发（最后凑齐的那条跑自己的效果）。"
        : "勾上 = 这条时机要和同一个状态上别的也勾了它的时机**同时成立**才触发（例：本回合既打过牌又受过伤）。";

    /// <summary>
    /// 这条时机属于哪个自定义状态（由界面在选中时点一下，不写进存档）。
    /// 「同时成立」的**重置范围**挂在整只状态上，而触发时机自己不知道属于哪个状态。
    /// </summary>
    [JsonIgnore]
    public CustomPowerSpec? OwnerPower
    {
        get => _ownerPower;
        set { _ownerPower = value; Raise(nameof(ShowAndScope)); Raise(nameof(AndScope)); }
    }
    private CustomPowerSpec? _ownerPower;

    /// <summary>界面用：「重置范围」那一行要不要显示（委托给所属状态：勾了「同时成立」的时机 ≥ 2 条才算数）。</summary>
    [JsonIgnore]
    public bool ShowAndScope => OwnerPower?.ShowAndScope == true;

    /// <summary>界面用：「重置范围」的值（读写都落到所属状态上）。</summary>
    [JsonIgnore]
    public string AndScope
    {
        get => OwnerPower?.AndScope ?? "Turn";
        set { if (OwnerPower is not null) OwnerPower.AndScope = value; }
    }

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
            {
                opt += string.IsNullOrWhiteSpace(PowerId)
                    ? $"（盯：{PowerTriggers.WatchTargetZh(PowerTarget)}的任意状态）"
                    : $"（盯：{PowerTriggers.WatchTargetZh(PowerTarget)}的{EffectCatalog.PowerName(PowerId, PowerId)}）";
                // V0.2.266：细分了「上升 / 下降」，列表那一行也要跟着说出来（不然看不出这条只管掉层）
                if (PowerDirection == "Up") opt += "（只在上升时）";
                else if (PowerDirection == "Down") opt += "（只在下降时）";
            }
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
    private decimal _amount2;
    private string? _powerId;
    private string _whenUnmet = "Unplayable";
    private string _target = "Self";
    private string _timing = EffectCatalog.DefaultCardTiming;
    // V0.2.247：「上一张打出的牌带有某关键词 / 是某范围」用得到（默认空串 = 没填）
    private string _keyword = "";
    private string _scope = "";
    // V0.2.288（用户要求「条件增加『指定敌人的意图为（）』」）：下拉选的那个意图（本体 IntentType 的名字）
    private string _intent = "Attack";

    /// <summary>条件种类（EffectCatalog.Conditions 里的 Id；None = 无条件）</summary>
    public string Kind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value))
            {
                Raise(nameof(NeedsAmount));
                Raise(nameof(NeedsAmount2));
                Raise(nameof(NeedsPower));
                Raise(nameof(NeedsTarget));
                Raise(nameof(NeedsTiming));
                Raise(nameof(NeedsKeyword));
                Raise(nameof(NeedsScope));
            Raise(nameof(NeedsIntent));
                Raise(nameof(IsNone));
                Raise(nameof(IsInverted));
                Raise(nameof(IsOutsideRange));
                Raise(nameof(DisplayShort));
                Raise(nameof(Summary));
            }
        }
    }

    /// <summary>
    /// 「卡牌时机」的子时机（需求⑩）：Exhausted / Generated / Retained / Discarded / Transformed / GivenKeyword。
    /// 只有 <see cref="NeedsTiming"/> 为 true 的条件（现在就是「卡牌时机」）用得到它；
    /// 默认值 = 下拉第一项「被消耗」（老存档没有这一项时也是它）。
    ///
    /// 注意：带这个条件的效果**不写进 OnPlay**，而是挂到本体对应钩子的 override 上
    /// （本体「战鼓」DrumOfBattle 的做法），生成端见 CSharpCodeGen.EmitCardTimingHooks。
    /// </summary>
    public string Timing
    {
        get => _timing;
        set
        {
            if (Set(ref _timing, string.IsNullOrWhiteSpace(value) ? EffectCatalog.DefaultCardTiming : value))
            {
                Raise(nameof(TimingZh));
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
        set { if (Set(ref _amount, value)) { Raise(nameof(Summary)); Raise(nameof(DisplayShort)); Raise(nameof(IsInverted)); Raise(nameof(IsOutsideRange)); } }
    }

    /// <summary>
    /// 第二个数值（目前只有「生命值在 N%~M% 之间」用得到：N = 下界、M = 上界）。
    /// **任一填负数 = 判断成「区间外」**（用户要求）。
    /// </summary>
    public decimal Amount2
    {
        get => _amount2;
        set { if (Set(ref _amount2, value)) { Raise(nameof(Summary)); Raise(nameof(DisplayShort)); Raise(nameof(IsOutsideRange)); } }
    }

    /// <summary>这个条件填了负数 = 反向（生命值高于 N% / 少于 N 层…）。</summary>
    [JsonIgnore]
    public bool IsInverted => _amount < 0;

    // ===== 条件数值的公式（用户要求⑦：所有能填数值的地方都能写公式）=====
    private string _amountFormula = "";
    private string _amount2Formula = "";

    /// <summary>
    /// 「数值 N」这一栏的公式（留空 = 用固定数值）。
    /// 生成时替换掉条件表达式里的那个数字 —— 例：「手牌数不少于 N 张」写 `Turn`（第几回合就要求几张）。
    ///
    /// 和「数值公式 / 次数公式」同一套语法与解析器（<c>ForgeFormula</c>：参数 M / N…、X / Y、内置只读量、
    /// min/max 那几类函数）。⚠ 同样**只做在卡牌上**（求值要那两个「自己 / 玩家」的局部变量）。
    /// </summary>
    public string AmountFormula
    {
        get => _amountFormula;
        set
        {
            if (Set(ref _amountFormula, (value ?? "").Trim()))
            {
                Raise(nameof(UsesAmountFormula));
                Raise(nameof(ShowAmountFormula));
                Raise(nameof(DisplayShort));
                Raise(nameof(Summary));
            }
        }
    }

    /// <summary>「数值 M」（生命区间的上界）的公式，同 <see cref="AmountFormula"/>。</summary>
    public string Amount2Formula
    {
        get => _amount2Formula;
        set
        {
            if (Set(ref _amount2Formula, (value ?? "").Trim()))
            {
                Raise(nameof(UsesAmount2Formula));
                Raise(nameof(ShowAmount2Formula));
                Raise(nameof(DisplayShort));
                Raise(nameof(Summary));
            }
        }
    }

    /// <summary>这个条件的「数值 N」写了公式没有（生成端 / 校验器 / 回读共用）。</summary>
    [JsonIgnore]
    public bool UsesAmountFormula => NeedsAmount && !string.IsNullOrWhiteSpace(_amountFormula);

    /// <summary>「数值 M」写了公式没有（只有「生命区间」这类需要第二个数值的条件用得到）。</summary>
    [JsonIgnore]
    public bool UsesAmount2Formula => NeedsAmount2 && !string.IsNullOrWhiteSpace(_amount2Formula);

    /// <summary>界面上「数值 N 的公式」那一格要不要显示（要填数值的条件才有）。</summary>
    [JsonIgnore] public bool ShowAmountFormula => NeedsAmount;

    /// <summary>界面上「数值 M 的公式」那一格要不要显示。</summary>
    [JsonIgnore] public bool ShowAmount2Formula => NeedsAmount2;

    /// <summary>「填负数 = 反向」那句提示：写了公式时不能说「填负数」（公式算出来的正负生成时不知道）。</summary>
    [JsonIgnore]
    public string ConditionFormulaHint =>
        "留空 = 用左边那个固定数字；写了 = 打出这张牌时现算（写法与「数值公式」一样："
        + "参数 M / N…、X 当前能量、Y 当前额外资源量、内置只读量 Turn / HandCount / DiscardedThisTurn / PowersOnMe…"
        + "—— **完整清单见启动时弹出的「参数参考」窗**）。"
        + "⚠ 只做在卡牌上；写了公式时「填负数 = 反向」那套不适用（公式算出来的正负生成时不知道，"
        + "一律按「正面」判断，要反向请写进公式本身或用固定负值）。";

    /// <summary>「生命区间」这类条件填了负数 = 判断成区间外。</summary>

    /// <summary>「生命区间」这类条件填了负数 = 判断成区间外。</summary>
    [JsonIgnore]
    public bool IsOutsideRange =>
        EffectCatalog.FindCondition(_kind)?.NeedsAmount2 == true && (_amount < 0 || _amount2 < 0);

    /// <summary>这个条件有没有写任何一个数值公式（生成端 / 回读 / 界面共用）。</summary>
    [JsonIgnore]
    public bool UsesAnyFormula => UsesAmountFormula || UsesAmount2Formula;

    /// <summary>「拥有某状态」用的增益/减益（本体的 Power 类名）</summary>
    public string? PowerId
    {
        get => _powerId;
        set { if (Set(ref _powerId, value)) Raise(nameof(Summary)); }
    }

    /// <summary>
    /// V0.2.247（用户要求「上一张打出的牌带有某关键词」）：本体 <c>CardKeyword</c> 的枚举名
    /// （Exhaust / Ethereal / Innate / Retain / Unplayable / Sly / Eternal）。
    /// 只有 <see cref="Kind"/> = LastCardKeyword 时读；默认空串。
    /// </summary>
    public string Keyword
    {
        get => _keyword;
        set { if (Set(ref _keyword, value ?? "")) Raise(nameof(Summary)); }
    }

    /// <summary>
    /// V0.2.247（用户要求「上一张打出的牌是某范围」，范围内可以填组）：范围 Id
    /// —— 和「范围界定」同一份候选（<see cref="EffectCatalog.SpawnFiltersFor"/>：攻击 / 技能 / 能力 /
    /// 诅咒 / 状态 / 任务 / 自定义关键词那一组 / 每个**组**）。空串 = 不限（= 任意牌）。
    /// </summary>
    public string Scope
    {
        get => _scope;
        set { if (Set(ref _scope, value ?? "")) Raise(nameof(Summary)); }
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
    /// <summary>要不要显示第二个数值框（「生命值在 N%~M% 之间」用得到）。</summary>
    [JsonIgnore] public bool NeedsAmount2 => EffectCatalog.FindCondition(_kind)?.NeedsAmount2 == true;
    [JsonIgnore] public bool NeedsPower => EffectCatalog.FindCondition(_kind)?.NeedsPower == true;
    [JsonIgnore] public bool NeedsTarget => EffectCatalog.FindCondition(_kind)?.NeedsTarget == true;

    /// <summary>界面用：这个条件底下要不要再显示一个「子时机」下拉（需求⑩ 的「卡牌时机」要）。</summary>
    [JsonIgnore] public bool NeedsTiming => EffectCatalog.FindCondition(_kind)?.NeedsTiming == true;

    /// <summary>界面用：这个条件底下要不要再显示「关键词」下拉（V0.2.247「上一张打出的牌带有某关键词」要）。</summary>
    [JsonIgnore] public bool NeedsKeyword => EffectCatalog.FindCondition(_kind)?.NeedsKeyword == true;

    /// <summary>界面用：这个条件底下要不要再显示「范围（可按组）」下拉（V0.2.247）。</summary>
    [JsonIgnore] public bool NeedsScope => EffectCatalog.FindCondition(_kind)?.NeedsScope == true;
    /// <summary>V0.2.288：这个条件底下要不要再选「敌人的哪种意图」。</summary>
    [JsonIgnore] public bool NeedsIntent => EffectCatalog.FindCondition(_kind)?.NeedsIntent == true;

    /// <summary>V0.2.288：选中的意图（本体 <c>IntentType</c> 的名字：Attack / Defend / Buff …）。</summary>
    public string Intent
    {
        get => _intent;
        set
        {
            if (Set(ref _intent, (value ?? "").Trim())) { Raise(nameof(IntentZh)); Raise(nameof(DisplayShort)); Raise(nameof(Summary)); }
        }
    }

    /// <summary>意图的中文（界面上显示用）。</summary>
    [JsonIgnore] public string IntentZh => EffectCatalog.IntentZh(_intent);

    /// <summary>子时机的中文名（界面上那个下拉里显示的就是它）。</summary>
    [JsonIgnore] public string TimingZh => EffectCatalog.CardTimingZh(_timing);

    /// <summary>界面上「填负数 = 反向」那一句提示要不要显示（条件支持填负数、而且当前没填负数时提示怎么写）。</summary>
    [JsonIgnore] public bool SupportsNegative => EffectCatalog.FindCondition(_kind)?.SupportsNegative == true;

    /// <summary>指向对象的中文（界面上/描述里显示）。</summary>
    [JsonIgnore]
    public string TargetZh => (_target ?? "Self") switch
    {
        "Enemy" => "指定敌人",
        "AnyEnemy" => "任意一个敌人",
        "AllEnemies" => "全部敌人",
        _ => "自己",
    };

    /// <summary>列表里显示的短名（带参数）。
    /// 措辞（含「填负数 = 反向」「生命区间」）统一走 <see cref="EffectCatalog.ConditionZh"/>，界面和卡面描述不会两套说法。</summary>
    [JsonIgnore]
    public string DisplayShort =>
        EffectCatalog.ConditionZh(_kind, _amount, _amount2, _powerId, TargetZh, ResourceDisplayName, _timing,
            UsesAmountFormula ? _amountFormula : null, UsesAmount2Formula ? _amount2Formula : null,
            _keyword, _scope);

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

public sealed class PotionSpec : SpecBase, ISlotRow
{
    private string _name = "新药水";
    private string _className = "";
    private string _rarity = "Common";
    private string _usage = "CombatOnly";
    private string _targetType = "Self";
    private bool _inPotionPool = true;

    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>英文类名。留空 = 由工具按位置自动生成（见 Naming.PotionClassName）。</summary>
    public string ClassName
    {
        get => _className;
        set
        {
            if (!Set(ref _className, value)) return;
            // 「英文类名」那一栏下面的小字（自动 / 手填）跟着变
            Raise(nameof(ClassNameIsAuto));
            Raise(nameof(ClassNameHint));
        }
    }

    /// <summary>Common / Uncommon / Rare（存的是英文 id，界面显示中文）</summary>
    public string Rarity
    {
        get => _rarity;
        set
        {
            if (!Set(ref _rarity, value)) return;
            Raise(nameof(Display));
        }
    }
    /// <summary>CombatOnly / AnyTime</summary>
    public string Usage { get => _usage; set => Set(ref _usage, value); }
    /// <summary>Self / AnyEnemy / AllEnemies</summary>
    public string TargetType { get => _targetType; set => Set(ref _targetType, value); }
    public bool InPotionPool { get => _inPotionPool; set => Set(ref _inPotionPool, value); }

    // ===== 左侧列表的序号（用户要求第 6 条「药水方面参考遗物的优化方案」：
    //       序号显示在药水名最左侧、从上到下 1..N，序号是**位置号**、不跟随药水移动）=====
    private int _ordinal;

    /// <summary>
    /// 这瓶药水在 <c>Profile.Potions</c> 里的**当前位置**（1 起算），只给界面显示用。
    ///
    /// <para>⚠ 它**不写进存档**（<see cref="JsonIgnoreAttribute"/>）：序号是「当前的排序位置」，
    /// 不是跟着物品走的标记 —— 拖动换位置 / 删掉中间一瓶之后，由
    /// <c>Naming.RenumberPotionClassNames</c>（生成前）和界面自己的刷新重新发一遍。
    /// 和 <see cref="RelicSpec.Ordinal"/> 同一套做法（写进存档会给老存档多一个字段）。</para>
    /// </summary>
    [JsonIgnore]
    public int Ordinal { get => _ordinal; set => Set(ref _ordinal, value); }

    // ===== 「自动类名」的痕迹（用户要求：自动的跟序号走、手填的不动）=====
    private int _lastAutoOrdinal;

    /// <summary>
    /// 上一次生成时，这瓶药水的「自动类名」是第几号（对应 <c>&lt;角色类名&gt;Potion&lt;它&gt;</c>）。
    ///
    /// <para>⚠ 和遗物那边同一个理由：英文类名留空时**类名本身要保持空白**（一写实就再也分不出
    /// 「工具生成的」和「用户手打的」—— 见 <c>Naming.RenumberPotionClassNames</c> 的注释）。
    /// 但界面上「英文类名」那一栏总得让用户看到「不填会用哪个名字」，所以单独把**序号**记在这里，
    /// 由 <see cref="AutoClassNameHint"/> 拼出完整名字。不写进存档。</para>
    /// </summary>
    [JsonIgnore]
    public int LastAutoOrdinal { get => _lastAutoOrdinal; set => Set(ref _lastAutoOrdinal, value); }

    /// <summary>界面用：不填类名时，这瓶药水会用哪个自动名（还没生成过就按当前序号推）。</summary>
    [JsonIgnore]
    public string AutoClassNameHint
    {
        get
        {
            int n = _lastAutoOrdinal >= 1 ? _lastAutoOrdinal : _ordinal;
            return "<角色类名>Potion" + (n >= 1 ? n : 1);
        }
    }

    /// <summary>
    /// 界面用：这瓶药水的英文类名现在是不是「自动生成的那一个」
    /// （判据 = <c>Naming.IsAutoPotionClassNameFor</c>，按名字形状认；认不出的都当手填）。
    /// </summary>
    [JsonIgnore]
    public bool ClassNameIsAuto =>
        !Generation.Naming.IsValidIdentifier(_className)
        || Generation.Naming.IsAutoPotionClassNameFor(null, _className);

    /// <summary>界面用：「英文类名」那一栏下面那行小字。</summary>
    [JsonIgnore]
    public string ClassNameHint => ClassNameIsAuto
        ? "留空 = 自动。现在对应 " + AutoClassNameHint + "（重新生成 / 构建时按左边列表的序号重算，拖动换位置就跟着变）。"
        : "现在这个名字是**你手填的**：重新生成 / 构建都不会改它（想让它跟着序号走就把这一栏清空）。";

    /// <summary>界面用：左侧列表那一行的提示（序号 + 类名是自动还是手填）。</summary>
    [JsonIgnore]
    public string RowTip => "第 " + (_ordinal >= 1 ? _ordinal : 0) + " 位 ｜ "
        + (ClassNameIsAuto ? "英文类名自动（跟序号走）" : "英文类名手填（不会自动改）") + " ｜ 可以拖动这一行换位置";

    /// <summary>界面用：左侧列表里那一行最左边的序号（1 起算，就是它当前的位置）。</summary>
    [JsonIgnore]
    public string OrdinalText => _ordinal >= 1 ? _ordinal + "." : "";

    /// <summary>界面用：真药水行当然不是空槽（空槽行是界面那边的 <c>PotionSlotPlaceholder</c>）。</summary>
    [JsonIgnore]
    public bool IsPlaceholder => false;


    private bool _isForged;

    /// <summary>
    /// 这瓶药水是**锻造页**造出来的（需求：锻造页也能做药水）。
    /// 列表里的名字前面带 <c>*</c>；药水页选中它时右侧只提示「请在锻造页查看」。
    /// </summary>
    public bool IsForged
    {
        get => _isForged;
        set
        {
            if (!Set(ref _isForged, value)) return;
            Raise(nameof(Display));
            Raise(nameof(ForgeEditingHint));
        }
    }

    /// <summary>界面用：锻造药水在药水页右侧显示的那句提示。</summary>
    [JsonIgnore]
    public string ForgeEditingHint => "这瓶药水是在「锻造」页用节点图做的 —— 请到「锻造」页查看与修改。";

    /// <summary>锻造节点图（这块布）。null / 空图 = 不接管生成。</summary>
    public ForgeGraphSpec? ForgeGraph { get; set; }

    /// <summary>药水图标（物品栏小图标 + 弹窗大图都用它）。留空则用本体药水图占位。</summary>
    public string? Icon { get; set; }
    /// <summary>药水图标描边（选中高亮用）。留空则跟图标同图。</summary>
    public string? IconOutline { get; set; }

    public ObservableCollection<EffectSpec> Effects { get; set; } = new();

    // ===== 自定义描述（用户要求：药水也能自己写描述）=====
    private string _customDescription = "";
    private bool _customDescriptionReplaces;

    /// <summary>自定义描述（留空 = 用自动生成的那段）。支持 <c>{PoisonPower}</c> 这类本体占位符。</summary>
    public string CustomDescription
    {
        get => _customDescription;
        set { if (Set(ref _customDescription, value ?? "")) Raise(nameof(HasCustomDescription)); }
    }

    /// <summary>勾上 = 只用你写的这段（不勾 = 自动描述 + 换行 + 你写的这段）。</summary>
    public bool CustomDescriptionReplaces
    {
        get => _customDescriptionReplaces;
        set { if (Set(ref _customDescriptionReplaces, value)) Raise(nameof(Display)); }
    }

    [JsonIgnore]
    public bool HasCustomDescription => !string.IsNullOrWhiteSpace(_customDescription);

    /// <summary>
    /// 列表里显示的整行文本。
    ///
    /// <para>稀有度用**中文**（用户要求：药水页照抄遗物页的「稀有度英文改中文」）——
    /// 遗物页 <see cref="RelicSpec.Display"/> 早就是这么做的（<c>EffectCatalog.RelicRarityZh</c>），
    /// 这里改成 <c>EffectCatalog.PotionRarityZh</c>。存进存档的 <see cref="Rarity"/> 还是英文 id。</para>
    /// </summary>
    [JsonIgnore]
    public string Display =>
        $"{(IsForged ? "*" : "")}{Name}  ｜ {EffectCatalog.PotionRarityZh(Rarity)} ｜ {Usage} → {TargetType}{(InPotionPool ? "" : " ｜ 不入池")}";
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

    /// <summary>
    /// 本体那 7 个关键词（顺序固定，界面表格就按这个顺序排）。
    ///
    /// 说明文字直接照抄本体 <c>localization/zhs/card_keywords.json</c> 的原文
    /// （读得到本体文件时以那份为准，这里是读不到时的兜底）。照抄的原因：「本体关键词改名」要判断
    /// 「填的和本体一样 = 等于没改」，兜底文本和本体不一致就会误判、写出多余的覆盖条目。
    /// </summary>
    public static readonly IReadOnlyList<Entry> All = new[]
    {
        new Entry("EXHAUST",    "消耗",       "在战斗结束前移除。"),
        new Entry("ETHEREAL",   "虚无",       "如果这张牌在这个回合结束时留在你的[gold]手牌[/gold]中，则将其[gold]消耗[/gold]。"),
        new Entry("INNATE",     "固有",       "每场战斗开始时这张牌会出现在你的[gold]手牌[/gold]。"),
        new Entry("UNPLAYABLE", "不能被打出", "不能被打出的牌无法被打出。"),
        new Entry("RETAIN",     "保留",       "保留的牌不会在回合结束时被弃掉。"),
        new Entry("SLY",        "奇巧",       "如果这张牌在你的回合结束前从你的[gold]手牌[/gold]中被丢弃，则免费将其打出。"),
        // 永恒不是「打出后回抽牌堆」：本体 CardModel.IsRemovable / IsTransformable 看的就是它 ——
        // 带着这个关键词的牌**无法从牌组里移除或变化**（战斗中不在牌组里时仍可变化）。
        new Entry("ETERNAL",    "永恒",       "无法从你的[gold]牌组[/gold]中移除或变化。"),
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

/// <summary>
/// 用户上传的一条音频（V0.2.155）：「播放音频文件（我上传的）」效果种类引用它。
///
/// <para><b>为什么不用 FMOD</b>：本体自己的声音全是 FMOD 事件（<c>SfxCmd.Play("event:/…")</c>），
/// 而模组**注册不了新的 FMOD 事件**（bank 里只有事件 GUID）。所以这条路绕开 FMOD：
/// 生成时把文件复制进工程 <c>audio/</c>（导出预设是 <c>all_resources</c>，会一起打进 PCK），
/// 运行时用 Godot 的 <c>AudioStreamPlayer</c> 播 <c>res://audio/&lt;Key&gt;</c>。</para>
///
/// <para><see cref="File"/> 存的是**本机绝对路径**（和「上传卡面」那些字段同一个口径：
/// 界面直接记住你选的那个文件，生成时复制）。<see cref="Name"/> 是存档里的稳定标识，
/// 也是生成出来的文件名（效果里存的就是它）。</para>
/// </summary>
public sealed class AudioAssetSpec : SpecBase
{
    private string _name = "";
    private string _file = "";

    /// <summary>显示名 + 存档里的稳定标识（生成时用它当文件名，会自动去掉非法字符）。</summary>
    public string Name
    {
        get => _name;
        set { if (Set(ref _name, value ?? "")) { Raise(nameof(Display)); Raise(nameof(Key)); Raise(nameof(Hint)); } }
    }

    /// <summary>本机那个音频文件的绝对路径（.ogg / .mp3 / .wav）。</summary>
    public string File
    {
        get => _file;
        set { if (Set(ref _file, value ?? "")) { Raise(nameof(Display)); Raise(nameof(Key)); Raise(nameof(Hint)); } }
    }

    /// <summary>
    /// 生成时用的文件名（= <c>res://audio/&lt;Key&gt;</c> 里那个 Key）：<see cref="Name"/> 去掉非法字符
    /// 之后保留原扩展名（只认 .ogg / .mp3 / .wav，别的按 .ogg 处理并让校验器报错）。
    /// 效果里存的 <c>EffectSpec.Sfx</c> 就是它。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key
    {
        get
        {
            string stem = new string((_name ?? "").Trim()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
            if (stem.Length == 0) stem = "sfx";
            string ext = AudioExtOf(_file);
            return stem + ext;
        }
    }

    /// <summary>这个扩展名支持不支持（Godot 内置导入器：ogg / mp3 / wav）—— 看**原始**扩展名。</summary>
    public static bool IsSupportedAudio(string? path)
    {
        string ext = (System.IO.Path.GetExtension(path ?? "") ?? "").Trim().ToLowerInvariant();
        return ext is ".ogg" or ".oga" or ".mp3" or ".wav" or ".wave";
    }

    /// <summary>文件扩展名（不认识的返回 <c>.ogg</c>，让校验器去报「格式不支持」）。</summary>
    public static string AudioExtOf(string? path)
    {
        string ext = (System.IO.Path.GetExtension(path ?? "") ?? "").Trim().ToLowerInvariant();
        // .oga / .ogg 都是 Ogg Vorbis；.wave 也认
        if (ext is ".oga") return ".ogg";
        if (ext is ".wave") return ".wav";
        return ext is ".ogg" or ".mp3" or ".wav" ? ext : ".ogg";
    }

    /// <summary>界面用：这一行的摘要（文件不在本机了就说清楚）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Display
    {
        get
        {
            string name = string.IsNullOrWhiteSpace(_name) ? "（没命名）" : _name.Trim();
            string file = string.IsNullOrWhiteSpace(_file) ? "（没选文件）"
                : System.IO.File.Exists(_file) ? System.IO.Path.GetFileName(_file) : "⚠ 文件不见了";
            return $"{name} · {file} → audio/{Key}";
        }
    }

    /// <summary>界面用：底下那句小字。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Hint => string.IsNullOrWhiteSpace(_file)
        ? "点「上传音频…」选一个 .ogg / .mp3 / .wav 文件。"
        : (IsSupportedAudio(_file)
            ? $"生成时会复制成工程里的 audio/{Key}（一起打进 PCK）。"
            : $"⚠ 这个扩展名（{System.IO.Path.GetExtension(_file)}）本体 / Godot 不认，请换成 .ogg / .mp3 / .wav。");
}

public sealed class ArtSpec{    public string? Icon { get; set; }
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

    /// <summary>
    /// 多人联机宝箱房的那只手：**选择**（指哪个遗物，本体叫 point）。本机 PNG 路径，可留空。
    /// 本体的路径是 <c>res://images/ui/hands/multiplayer_hand_&lt;角色ID小写&gt;_point.png</c>
    /// （<c>CharacterModel.ArmPointingTexture</c>），四个手势各一张。留空 = 用铁甲战士那张占位
    /// （见 <see cref="ArtSpec.UseVanillaPlaceholders"/> 之外的兜底：这四个槽位没上传时**一直**是本体铁甲手，
    /// 因为本体手是 MP 必需的资源，缺了宝箱房会报错）。
    /// </summary>
    public string? GesturePoint { get; set; }
    /// <summary>多人联机宝箱房手势：**剪刀**（石头剪刀布出剪刀那张）。</summary>
    public string? GestureScissors { get; set; }
    /// <summary>多人联机宝箱房手势：**石头**（也是「放弃 / 不抢」时亮出来的那张拳头）。</summary>
    public string? GestureRock { get; set; }
    /// <summary>多人联机宝箱房手势：**布**（伸手去够遗物那张）。</summary>
    public string? GesturePaper { get; set; }
    /// <summary>
    /// 多人联机宝箱房手势：**拿取**（抓住遗物、把它拎回自己那边的那只手）。
    /// 本体没有第 5 张图 —— 它是「布（伸手）→ 石头（拳头攥住）」两步糊出来的，
    /// 所以这个槽位是**我们自己的扩展**：填了才会多生成一张 <c>..._grab.png</c> 和一个
    /// 只在我方角色的这一局里生效的补丁（<c>cs/TreasureGesture.cs</c>），把「攥住」那一步换成这张图。
    /// 留空 = 保持本体表现（布 → 石头）。
    /// </summary>
    public string? GestureGrab { get; set; }

    /// <summary>
    /// 多人联机宝箱房「开抢！！！」那句催促文本（本体键是 <c>gameplay_ui.TREASURE_FIGHT_TEXT</c>）。
    /// 留空 = 沿用本体那句。填了会生成一个补丁：**只在我方角色的这一局里**把那行字换掉，
    /// 不改本体的本地化键（改了的话，别的角色 / 联机另一边的玩家也会跟着变）。
    /// </summary>
    public string TreasureFightText { get; set; } = "";

    /// <summary>
    /// 模组预览图（游戏主菜单「模组」界面右侧那块图）。本机 PNG 路径，可留空。
    ///
    /// 本体的模组界面是按**固定路径**找图的：<c>res://&lt;模组ID&gt;/mod_image.png</c>
    /// （<c>NModInfoContainer.cs:39</c> —— 找不到就把那块留空，只显示名字 / 作者 / 版本 / 说明）。
    /// 所以生成时会把这张图拷到 <c>&lt;工程&gt;\&lt;模组ID&gt;\mod_image.png</c>
    /// （和 localization 同一个目录，一起打进 PCK）。
    /// 建议 1200×630（那个框差不多 1.9:1）或 1280×720，PNG、别超过 1~2 MB。
    /// </summary>
    public string? ModImage { get; set; }
    /// <summary>静态战斗/商店/篝火立绘（替代本体的 Spine 骨骼动画）。留空则用工具自带占位图。</summary>
    public string? CharacterStatic { get; set; }
    /// <summary>没上传的槽位是否用本体（铁甲战士）素材占位。默认 false = 生成中性占位图，
    /// 这样发布出去的模组不含本体美术；本地随手测试可以勾上。</summary>
    public bool UseVanillaPlaceholders { get; set; }

    /// <summary>
    /// 上传的卡面**比目标尺寸大**时，生成时用「面积平均」高质量缩到目标尺寸（V0.2.229）。
    ///
    /// <para>为什么要缩：游戏里卡面**画出来只有 250×190**（先古卡 299×421，见 <c>card.tscn</c> 的
    /// <c>%Portrait</c> 250×190 / <c>%AncientPortrait</c> 598×842 × scale 0.5），而且「卡面」那张纹理
    /// **没有 mipmap**（生成的工程没有 <c>.import</c>，Godot 默认 <c>mipmaps/generate=false</c>、
    /// 滤镜是 Linear）—— 也就是说引擎在画的时候只用**双线性采样 4 个纹素**，缩小 4 倍时 16 个纹素里
    /// 只用到 4 个，细节直接丢掉（发糊 / 出锯齿）。预先用面积平均缩到「游戏实际绘制尺寸的 N 倍」，
    /// 把「丢掉 12 个纹素」变成「刚好用满」，卡面就干净得多。</para>
    ///
    /// <para>档位（<see cref="ArtGenerator.CardArtShrinkModes"/>）：<c>""</c> = 原样（默认，逐字老行为）、
    /// <c>1x</c> / <c>2x</c> / <c>4x</c> = 缩到「显示尺寸 × N」（普通卡 250×190、先古卡 299×421 起算）。
    /// 实测（探针 <c>imgcmp</c>，和「理想的面积平均」比 RMSE）：直接引擎缩 <b>3.42</b>、
    /// 预缩 4×（=旧版那个 1000×760 勾选框）<b>3.42（一模一样，等于没作用）</b>、预缩 2× <b>0.27</b>、
    /// 预缩 1× <b>0.00</b> —— 所以推荐 <c>2x</c>。</para>
    ///
    /// <para>**绝不放大**（比目标小的图一个字都不动）；缩的是拷进工程的那份副本，原图不动。
    /// 老字段 <see cref="FitCardArtToFrame"/>（V0.2.228 的勾选框）为 true 且这里留空时按 <c>2x</c> 处理。</para>
    /// </summary>
    public string CardArtShrink { get; set; } = "";

    /// <summary>
    /// V0.2.228 的旧勾选框「卡面过大时高质量缩小到游戏显示尺寸」（缩到 1000×760 = 4× 显示尺寸）。
    ///
    /// <para>⚠ 实测它和「不缩」**完全等价**（探针 <c>imgcmp</c>：RMSE 3.42 vs 3.42）——
    /// 4× 时引擎仍然只采样 4/16 个纹素。所以 V0.2.229 起改用 <see cref="CardArtShrink"/> 选档，
    /// 这个字段只为老存档兼容保留：true + <see cref="CardArtShrink"/> 为空 ⇒ 按 <c>2x</c> 处理。</para>
    /// </summary>
    public bool FitCardArtToFrame { get; set; }

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

    /// <summary>
    /// 在游戏中隐藏那只「额外资源量遗物」（用户要求）。
    ///
    /// 这只遗物只是**载体**（开场发资源量 / 记跨战斗继承），不是藏品，所以它可以不出现在遗物栏里。
    /// 本体没有「隐藏某个遗物」的接口（遗物栏是遍历 <c>Player.Relics</c> 逐个建的，也没有可见性判断），
    /// <c>RelicModel</c> 上也没有「别显示我」的虚属性可以覆写，
    /// 所以勾上之后工具会多生成一个 <c>cs/HideCarrierRelic.cs</c>：给
    /// <c>NRelicInventory.Add</c> 打一个**后缀补丁** —— 原方法照跑（节点照建、照进遗物栏的节点列表），
    /// 跑完只把这个节点设成 <c>Visible = false</c>（纯视觉隐藏）。
    ///
    /// <para>
    /// ⚠ V0.2.113~V0.2.137 这里打的是**前缀补丁**（遇到这只遗物就 <c>return false</c> 跳过原方法）：
    /// 那会让遗物栏的节点列表比 <c>Player.Relics</c> 少一项，之后再获得任何遗物时按模型算出来的
    /// <c>index</c> 插进这个短列表就**越界**（<c>List.Insert</c> 抛 ArgumentOutOfRangeException，
    /// 异步任务死掉 → 玩家眼里就是卡死）。V0.2.138 起改成后缀，列表长度/顺序永远和模型一致。
    /// </para>
    ///
    /// <para>
    /// 那个文件里**只有 <c>Add</c> 这一个补丁类**。「新获得遗物」的飞入动画**不用**单独挡：
    /// 它只 tween 节点里 icon 的透明度 / 位置 / 缩放、**不碰 <c>Visible</c>**，父节点不可见 ⇒ 整棵子树不渲染、
    /// 动画自己在不可见节点上跑完没有可见效果。（V0.2.138 实现中途多挂过一个挂在 <c>AnimateRelic</c> 上的
    /// **后缀**当「跳过动画」—— 后缀里的 <c>return</c> 只结束它自己、原方法早跑完了，那是空操作，已删。）
    /// </para>
    ///
    /// 遗物本身照旧挂在角色身上 —— 发放 / 继承这些功能一个字都不变（也不影响存档兼容）。
    /// 默认**不勾**（和以前一样看得见），勾上才隐藏。
    /// </summary>
    public bool HideInGame { get; set; }

    /// <summary>界面用：勾选框下面那句说明（两种情况都写清 —— 这个类没有属性通知，写死的文案不会过时）。</summary>
    [JsonIgnore]
    public string HideInGameHint =>
        "勾上 = 生成一个补丁，让这只遗物在游戏里的遗物栏**不显示**（只是节点不可见；"
      + "遗物本身照旧挂在角色身上，发放资源量、跨战斗继承照常生效）；"
      + "不勾 = 和以前一样看得见（占遗物栏里的一格）。改完要重新「生成」才会生效。";

    /// <summary>资源的中文显示名（留空 = 沿用本体的「辉星」）。会写进悬停提示、卡面描述和那个隐藏遗物的名字里。</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 自定义描述（鼠标悬停那个计数器时弹出来的那段话）。留空 = 用工具自动写的那段
    ///（「<名字>：每场战斗开始时获得 N 点…」）。
    /// 填了就**原样**用它；想加自己的图标就写 <c>[img]res://…[/img]</c>（上传了图标的话，
    /// 你这段文字前面会自动补上那张 24×24 的内联小图 —— 除非你自己已经写了 [img]）。
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 这只**隐藏的起始遗物**上还能挂效果（用户要求：把它放到「遗物」页里、可以在其中加效果）。
    ///
    /// 它就是负责「每场战斗开始时发放额外资源量 / 记录跨战斗继承」的那个起始遗物
    /// （生成出来的 <c>&lt;角色&gt;ExtraResourceRelic</c>，稀有度 Starter、不占遗物栏的玩法位置）。
    /// 这里加的效果生成时挂在那只遗物的「**战斗开始时**」（带 choiceContext 的那个钩子）上。
    /// </summary>
    public ObservableCollection<EffectSpec> Effects { get; set; } = new();

    /// <summary>自定义图标：本机 PNG 文件路径（留空 = 沿用本体的星星图标）。
    /// 建议方图（128×128 或 256×256）带透明背景。</summary>
    public string? Icon { get; set; }

    /// <summary>是否在计数器下方显示名字（关掉就只在悬停提示里显示）。</summary>
    public bool ShowName { get; set; } = true;

    /// <summary>
    /// 数量上限（0 = 不限，和本体原来一样）。
    /// 本体没有「上限」这个概念（PlayerCmd.GainStars 直接往上加），所以这条是**我们加的**：
    /// 生成一条 Harmony 前缀把 <c>PlayerCmd.GainStars</c> 的增量夹到不超过上限。
    /// 「增加额外资源量上限值」那个效果涨的就是这个上限（生成出来的一个隐藏状态）。
    /// </summary>
    public int Max { get; set; }
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
    /// 替主人承伤：勾上以后召唤时给它挂一个守卫 Power（照本体 <c>DieForYouPower</c> 写），
    /// 主人受到的**可格挡攻击伤害**改由它承担；它死后本体默认就会把它从战斗里移除
    /// （不覆写 <c>ShouldCreatureBeRemovedFromCombatAfterDeath</c>）。
    ///
    /// 为什么**可以勾多只**：本体的 <c>Hook.ModifyUnblockedDamageTarget</c> 是**链式遍历**
    /// （<c>creature = item.ModifyUnblockedDamageTarget(creature, …)</c>），而本体 DieForYouPower 看到
    /// target 已经不是主人就直接放行 —— 直接靠本体排的话，生效的是「本体监听顺序里的第一只」，不可控。
    /// 所以所有勾了的召唤物共用一个守卫类，钩子里自己仲裁：**只有宠物列表里第一只活着且挂了守卫的**
    /// 才承担，它死后下一只自动接手。校验器只给提示，不拦生成。
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
        + (TakesDamageForOwner ? " ｜ 替主人承伤" : "")
        + (string.IsNullOrWhiteSpace(Image) ? "" : " ｜ 有自定义图片");
}

/// <summary>
/// 逐帧动画（把动图 / GIF 拆成帧动画）的配置。见 <c>docs\逐帧动画_吸收设计.md</c>。
///
/// 为什么是「生成时拆帧」而不是运行时放 GIF：游戏引擎（Godot）读不了 GIF，
/// 而且帧图在游戏里会被缩放 —— 所以工具在生成阶段把 GIF 解成 RGBA 帧、按同一个裁剪框裁好、
/// 写成 PNG + Godot 的 <c>SpriteFrames</c> 资源，再由生成的运行时把本体的动画请求翻成逐帧播放。
///
/// 7 个槽位对应本体自己的动画名（<c>idle_loop / attack / cast / hurt / die / overgrowth_loop / relaxed_loop</c>），
/// **待机是回退目标，必须有**：其它动作没素材时都会退回它。
/// </summary>
public sealed class AnimationSpec : SpecBase
{
    private bool _enabled;
    private string _idle = "", _attack = "", _cast = "", _hurt = "", _die = "", _restSite = "", _merchant = "";
    private bool _alignByFirstFrame = true;
    private string _manualCrop = "";
    private bool _keyBackground;
    private double _keyTolerance = 12;
    private bool _stripOutline;
    private double _outlineLuminance = 170;
    private bool _nearestFilter = true;
    private double _speedScale = 1;
    private double _scale;
    private double _xOffset;
    private double _yOffset;
    private bool _keepRestAndShopAnim;

    /// <summary>总开关。不勾 = 生成结果和以前完全一样（不写任何动画资源 / 运行时代码）。</summary>
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) { Raise(nameof(HasAny)); Raise(nameof(Display)); } } }

    /// <summary>待机（循环）。**必须有** —— 其它动作没素材时都退回它。</summary>
    public string Idle { get => _idle; set => SetPath(ref _idle, value); }
    /// <summary>攻击（本体 <c>attack</c>，不循环）。</summary>
    public string Attack { get => _attack; set => SetPath(ref _attack, value); }
    /// <summary>施法（本体 <c>cast</c>，不循环）。</summary>
    public string Cast { get => _cast; set => SetPath(ref _cast, value); }
    /// <summary>受伤（本体 <c>hurt</c>，不循环）。</summary>
    public string Hurt { get => _hurt; set => SetPath(ref _hurt, value); }
    /// <summary>死亡（本体 <c>die</c>，播完定格在最后一帧）。</summary>
    public string Die { get => _die; set => SetPath(ref _die, value); }
    /// <summary>篝火（本体按章节播 overgrowth_loop / hive_loop / glory_loop）。</summary>
    public string RestSite { get => _restSite; set => SetPath(ref _restSite, value); }
    /// <summary>商店（本体 <c>relaxed_loop</c>）。</summary>
    public string Merchant { get => _merchant; set => SetPath(ref _merchant, value); }

    /// <summary>每帧秒数：0 = 用动图自己的延时（取平均）；填了 = 所有动作都用这个值。</summary>

    /// <summary>
    /// 按首帧自动对齐（推荐）：以「待机第 0 帧」为基准推一个裁剪框，7 个动作**共用**它
    /// —— 这样换动作时角色不会跳位置。关掉则每个动作各自按自己的并集裁剪（只适合素材本来就对齐的情况）。
    /// </summary>
    public bool AlignByFirstFrame { get => _alignByFirstFrame; set => Set(ref _alignByFirstFrame, value); }

    // ==================== V0.2.332：「动画设置」只保留四项 ====================
    // 用户要求：动画页只留「缩放 / X 偏移 / Y 偏移 / 播放速度系数」，其它项从界面撤掉、生成固定用默认值。
    // 下面这几个是**只读占位**：老存档里的值不再被读取（[JsonIgnore]），界面也不再绑定它们；
    // 保留属性只是为了让「预览 / 诊断 / 校验」那些还在引用它们的代码继续编译（值恒为默认）。
    [JsonIgnore] public string ManualCrop { get => ""; set { } }
    [JsonIgnore] public bool KeyBackground { get => false; set { } }
    [JsonIgnore] public double KeyTolerance { get => 12; set { } }
    [JsonIgnore] public bool StripOutline { get => false; set { } }
    [JsonIgnore] public double OutlineLuminance { get => 170; set { } }
    [JsonIgnore] public bool NearestFilter { get => true; set { } }
    [JsonIgnore] public double FrameSeconds { get => 0; set { } }
    [JsonIgnore] public bool KeepRestAndShopAnim { get => false; set { } }

    /// <summary>播放速度系数：1 = 动图原速，0.5 = 半速，2 = 双倍速。</summary>
    public double SpeedScale { get => _speedScale; set { if (Set(ref _speedScale, value)) Raise(nameof(Display)); } }

    /// <summary>缩放：0 = 按「静态立绘高 ÷ 待机内容高」自动算。</summary>
    public double Scale { get => _scale; set => Set(ref _scale, value); }

    /// <summary>
    /// X 偏移（像素，正数往右）：0 = 水平居中。用来把动画整体对齐到本体「血条中心」那一列
    /// （V0.2.331 新增；以前只有 Y 偏移，横向偏了只能靠改素材）。
    /// </summary>
    public double XOffset { get => _xOffset; set => Set(ref _xOffset, value); }

    /// <summary>
    /// Y 偏移：0 = 自动（让「帧内内容底边」落在静态立绘底边所在的位置，也就是脚的位置不变）。
    /// 注意 0 也是合法的手动值语义里的「自动」；要微调就填非 0。
    /// </summary>
    public double YOffset { get => _yOffset; set => Set(ref _yOffset, value); }

    // ==================== V0.2.345：四个数值框改成「文本 + 解析」（用户报「动画页的缩放等不能填小数」）====================
    // 为什么不再把 TextBox 直接绑 double：打 "0." 这种中间态会解析失败，WPF 判这次改动无效
    //（源不更新、部分输入法下光标还会跳回），小数就常常「填不进去」。
    // 现在照工具里 CardSpec.CostText 的老写法：文本原样收下，**只有解析成功才写回数值**；
    // 而且 "0.85" 和 "0,85" 都认（系统区域把小数点设成逗号的人也能填）。
    // null = 还没改动过（界面上显示当前数值）；空串也是合法输入（清空 = 不写回、保持原值）。
    private string? _scaleText, _xOffsetText, _yOffsetText, _speedScaleText;

    [JsonIgnore] public string ScaleText
    {
        get => _scaleText ?? NumText(_scale);
        set { _scaleText = value; if (TryNum(value, out double v)) Scale = v; }
    }

    [JsonIgnore] public string XOffsetText
    {
        get => _xOffsetText ?? NumText(_xOffset);
        set { _xOffsetText = value; if (TryNum(value, out double v)) XOffset = v; }
    }

    [JsonIgnore] public string YOffsetText
    {
        get => _yOffsetText ?? NumText(_yOffset);
        set { _yOffsetText = value; if (TryNum(value, out double v)) YOffset = v; }
    }

    [JsonIgnore] public string SpeedScaleText
    {
        get => _speedScaleText ?? NumText(_speedScale);
        set { _speedScaleText = value; if (TryNum(value, out double v)) SpeedScale = v; }
    }

    /// <summary>数值的默认文本（界面初始值用）。4 位小数够用，且不写成一长串。</summary>
    private static string NumText(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>「0.85」和「0,85」都认；空串 / 乱写 → false（不写回，保持原值）。</summary>
    private static bool TryNum(string? text, out double value)
    {
        value = 0;
        string s = (text ?? "").Trim().Replace(',', '.');
        if (s.Length == 0) return false;
        return double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }


    private void SetPath(ref string field, string? value)
    {
        if (!Set(ref field, value ?? "")) return;
        Raise(nameof(HasAny));
        Raise(nameof(Slots));
        Raise(nameof(Display));
    }

    /// <summary>
    /// 填了路径的槽位：<c>(本体动画名, GIF 路径)</c>，顺序固定为
    /// 待机 / 攻击 / 施法 / 受伤 / 死亡 / 篝火 / 商店（生成和界面都按这个顺序）。
    /// </summary>
    [JsonIgnore]
    public IEnumerable<(string Anim, string Path)> Slots
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Idle)) yield return ("idle_loop", Idle.Trim());
            if (!string.IsNullOrWhiteSpace(Attack)) yield return ("attack", Attack.Trim());
            if (!string.IsNullOrWhiteSpace(Cast)) yield return ("cast", Cast.Trim());
            if (!string.IsNullOrWhiteSpace(Hurt)) yield return ("hurt", Hurt.Trim());
            if (!string.IsNullOrWhiteSpace(Die)) yield return ("die", Die.Trim());
            if (!string.IsNullOrWhiteSpace(RestSite)) yield return ("overgrowth_loop", RestSite.Trim());
            if (!string.IsNullOrWhiteSpace(Merchant)) yield return ("relaxed_loop", Merchant.Trim());
        }
    }

    /// <summary>有没有素材（**只看待机**：其它动作没素材都会退回待机，待机为空等于没做动画）。</summary>
    [JsonIgnore]
    public bool HasAny => !string.IsNullOrWhiteSpace(Idle);

    /// <summary>界面列表 / 存档摘要里那一句话。</summary>
    [JsonIgnore]
    public string Display => !Enabled
        ? "未启用"
        : !HasAny
            ? "已启用但没上传待机"
            : $"{Slots.Count()} 个动作 · {SpeedScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}× · 缩放 {Scale:0.##} · X{XOffset:0.#} Y{YOffset:0.#}";
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
