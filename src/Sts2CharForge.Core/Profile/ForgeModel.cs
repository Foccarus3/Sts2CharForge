using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Sts2CharForge.Core.Profile;

/// <summary>
/// 一条「自定义参数」（需求：新建「自定义参数 / 组」页）。
///
/// 参数的名字是**一个字母**（界面上显示成 <c>M=</c>），作用相当于「一个中间变量」：
/// 用户先在「数值」模块 / 卡面公式里写 <c>=M*3</c>，再在这里规定 M 到底是什么 ——
/// 「自己最大生命」「指定敌人的易伤层数」「自己金币数」这类**运行时才知道的值**。
///
/// 求值发生在**生成出来的模组里**（运行时表达式求值器），不是在生成器里算成常量：
/// 这样「自己最大生命」会随战斗中的血量上限变化，「指定敌人的易伤层数」会随层数实时变化。
/// 本文件只负责**存配置**；求值器与公式解析在后续阶段（锻造节点 / 数值模块）接上。
/// </summary>
public sealed class ParamSpec : SpecBase
{
    private string _name = "M";
    private string _note = "";
    private string _source = ParamSources.Value;
    private decimal _value = 1;
    private string _target = ParamTargets.Self;
    private string? _powerId;
    private string _energyGrant = ParamGrants.None;
    private string _resourceGrant = ParamGrants.None;

    /// <summary>
    /// 参数名：**一个字母**（A~Z）。界面上的输入框后面固定跟一个「=」，
    /// 所以这一栏里只填字母本身（填 "M" 而不是 "M="）。
    /// 保留字母见 <see cref="ParamNames.Reserved"/>（X / Y 已经被「所有能量 / 所有额外资源量」占用）。
    /// </summary>
    public string Name
    {
        get => _name;
        set { if (Set(ref _name, ParamNames.Normalize(value))) { Raise(nameof(Display)); Raise(nameof(Label)); } }
    }

    /// <summary>这一栏只是给你自己看得懂的备注（不参与生成）。</summary>
    public string Note
    {
        get => _note;
        set => Set(ref _note, value ?? "");
    }

    /// <summary>
    /// 参数的取值来源，见 <see cref="ParamSources"/>：
    /// Value = 固定数值（同一条记录里的 <see cref="Value"/>）；
    /// CurrentHp / MaxHp / Gold / Energy / MaxEnergy = 运行时从 <see cref="Target"/> 身上读；
    /// Power = 读 <see cref="PowerId"/> 那个增益 / 减益在 <see cref="Target"/> 身上的层数。
    /// </summary>
    public string Source
    {
        get => _source;
        set
        {
            if (!Set(ref _source, ParamSources.Normalize(value))) return;
            Raise(nameof(IsValue));
            Raise(nameof(IsPower));
            Raise(nameof(NeedsTarget));
            Raise(nameof(SourceZh));
            Raise(nameof(Summary));
            Raise(nameof(Display));
        }
    }

    /// <summary>「固定数值」来源时用的那个数（可以填小数，例如 0.5 = 一半）。</summary>
    public decimal Value
    {
        get => _value;
        set { if (Set(ref _value, value)) { Raise(nameof(Summary)); Raise(nameof(Display)); } }
    }

    /// <summary>
    /// 从谁身上取这个值。取值见 <see cref="ParamTargets"/>：
    /// Self（自己）/ ChosenEnemy（指定敌人）/ AllEnemies（全部敌人）/ AllAllies（全部友方）/
    /// Summon（召唤物）。「指定敌人」= 打出这张牌时选中的那个目标。
    /// </summary>
    public string Target
    {
        get => _target;
        set
        {
            if (!Set(ref _target, ParamTargets.Normalize(value))) return;
            Raise(nameof(TargetZh));
            Raise(nameof(Summary));
            Raise(nameof(Display));
        }
    }

    /// <summary>
    /// 增益 / 减益（本体 Power 类名，见 <see cref="Effects.EffectCatalog.Powers"/>）。
    /// 只有 <see cref="Source"/> = Power 时用得到：M = 目标身上这个状态的层数。
    /// </summary>
    public string? PowerId
    {
        get => _powerId;
        set { if (Set(ref _powerId, value)) { Raise(nameof(Summary)); Raise(nameof(Display)); } }
    }

    /// <summary>
    /// 「这个参数给予多少**能量**」的指定（需求：上方为自定义参数，默认显示「给予 X 参数给能量」）。
    /// 取值见 <see cref="ParamGrants"/>：None = 不给予；
    /// 另一个参数名 = 战斗开始时按那个参数的值给能量。
    /// </summary>
    public string EnergyGrant
    {
        get => _energyGrant;
        set
        {
            if (!Set(ref _energyGrant, ParamGrants.Normalize(value))) return;
            Raise(nameof(GrantZh));
            Raise(nameof(Display));
        }
    }

    /// <summary>
    /// 「这个参数给予多少**额外资源量**」的指定（同上）。额外资源量 = 「额外资源量/状态」页那个计数器。
    /// </summary>
    public string ResourceGrant
    {
        get => _resourceGrant;
        set
        {
            if (!Set(ref _resourceGrant, ParamGrants.Normalize(value))) return;
            Raise(nameof(GrantZh));
            Raise(nameof(Display));
        }
    }

    /// <summary>界面用：这个参数被指定成「给予量」时的中文说明（没指定就是空串）。</summary>
    [JsonIgnore]
    public string GrantZh
    {
        get
        {
            var parts = new List<string>();
            if (_energyGrant != ParamGrants.None) parts.Add($"给能量 = {ParamGrants.LabelOf(_energyGrant)}");
            if (_resourceGrant != ParamGrants.None) parts.Add($"给额外资源量 = {ParamGrants.LabelOf(_resourceGrant)}");
            return string.Join("；", parts);
        }
    }

    /// <summary>来源是不是「固定数值」（界面：要不要显示数值输入框）。</summary>
    [JsonIgnore] public bool IsValue => ParamSources.Normalize(_source) == ParamSources.Value;
    /// <summary>来源是不是「增益 / 减益」（界面：要不要显示状态下拉）。</summary>
    [JsonIgnore] public bool IsPower => ParamSources.Normalize(_source) == ParamSources.Power;

    /// <summary>这一种来源要不要选「从谁身上取」（固定数值不用；能量 / 金币只能是自己）。</summary>
    [JsonIgnore]
    public bool NeedsTarget => ParamSources.Normalize(_source) is
        ParamSources.CurrentHp or ParamSources.MaxHp or ParamSources.Power;

    /// <summary>界面上参数名后面跟的那个「=」（只读，给绑定用）。</summary>
    [JsonIgnore] public string Label => _name + "=";

    /// <summary>来源的中文名（界面 / 列表 / 卡面公式说明里显示）。</summary>
    [JsonIgnore]
    public string SourceZh => ParamSources.Zh(_source);

    /// <summary>「从谁身上取」的中文名。</summary>
    [JsonIgnore] public string TargetZh => ParamTargets.Zh(_target);

    /// <summary>界面列表里那一行：<c>M= ｜ 自己最大生命</c> 这种；被指定成给予量时把那一句也带上。</summary>
    [JsonIgnore]
    public string Display
    {
        get
        {
            string g = GrantZh;
            return $"{Label} ｜ {Summary}" + (g.Length == 0 ? "" : $" ｜ {g}");
        }
    }

    /// <summary>一句话说明「这个参数最后等于什么」（列表里显示、生成代码注释也用）。</summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            string src = ParamSources.Normalize(_source);
            string val = _value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            return src switch
            {
                ParamSources.Value => $"固定数值 {val}",
                ParamSources.Power => $"{TargetZh}的{Effects.EffectCatalog.PowerName(_powerId, "（还没选状态）")}层数",
                _ => $"{TargetZh}的{ParamSources.Zh(src)}",
            };
        }
    }

    /// <summary>这个参数配置完整了没有（没配好的参数不该被公式引用）。</summary>
    [JsonIgnore]
    public bool IsComplete => ParamSources.Normalize(_source) != ParamSources.Power || !string.IsNullOrWhiteSpace(_powerId);
}

/// <summary>
/// 参数来源的候选（界面下拉）。用字符串存（和项目里其它枚举一个风格：
/// 读老存档 / 手写 JSON 时不会因为多了一个值就整份配置读不进来）。
/// </summary>
public static class ParamSources
{
    public const string Value = "Value";
    public const string MaxHp = "MaxHp";
    public const string CurrentHp = "CurrentHp";
    public const string Gold = "Gold";
    public const string Energy = "Energy";
    public const string MaxEnergy = "MaxEnergy";
    public const string Power = "Power";

    /// <summary>
    /// V0.2.250（用户要求「做成自定义参数」）：目标身上**减益的个数**（几种负面效果，和层数无关，每种算 1）。
    /// 本体「撕碎 Rend」那种「基础 + 每有一种负面效果 × N」就是靠它算的。
    /// </summary>
    public const string DebuffCount = "DebuffCount";

    /// <summary>界面下拉的候选（顺序就是用户需求里点名的顺序）。</summary>
    public static IReadOnlyList<ParamSourceOption> All { get; } = new[]
    {
        new ParamSourceOption(Value,      "数值（固定数字）",        "直接填一个数：M=3"),
        new ParamSourceOption(CurrentHp,  "当前血量",               "打出这张牌那一刻的血量"),
        new ParamSourceOption(MaxHp,      "最大生命",               "血量上限（战斗中涨上限也会跟着变）"),
        new ParamSourceOption(Gold,       "金币数",                 "当前持有的金币"),
        new ParamSourceOption(Energy,     "能量数",                 "当前能量"),
        new ParamSourceOption(MaxEnergy,  "最大能量数",             "每回合回满后的能量上限"),
        new ParamSourceOption(Power,      "增益/减益（状态层数）",   "目标身上某个状态的层数"),
        new ParamSourceOption(DebuffCount, "减益的个数（几种负面效果）", "目标身上有**几种**减益，和层数无关（每种算 1）"),
    }
    // ===== 「实时读数」（用户要求：参数页也要能监听这些）=====
    // 直接复用「数值公式」那份内置只读量清单（Generation.FormulaBuiltins.All）——
    // 界面列出来的名字、生成端认的名字、回读认的名字都是同一份，不会出现「参数页有、公式里不认」这种漂移。
    // 它们都是**运行时现算**的（回合数 / 本回合与本场战斗的丢弃·消耗·生成·复制牌数 / 拿到的资源量 /
    // 身上的状态数…），所以「从谁身上取」那一栏对它们没意义（都是自己）。
    // ⚠ 卡牌专属的读数（`FormulaBuiltin.CardOnly`，例：ThisCardDamage —— 表达式里是 `base.…`）
    //   **不列进来**：参数是「打出这张牌那一刻**先算一次**」的（见 EmitCardFormulaPrologue / ParamExpr），
    //   选它当来源会恒为 0（伤害还没打），是个静默失效的坑。想用它直接写进「数值（公式）」那一栏。
    .Concat(Generation.FormulaBuiltins.All.Where(b => !b.CardOnly).Select(b => new ParamSourceOption(
        b.Id, b.Display, "实时读数：每次取值现算（不用填固定数字）")))
    .ToList();

    /// <summary>认不出来的一律当「固定数值」（老存档 / 手写 JSON 兜底）。</summary>
    public static string Normalize(string? v) =>
        All.Any(o => string.Equals(o.Id, v, StringComparison.Ordinal)) ? v! : Value;

    public static string Zh(string? v) =>
        All.FirstOrDefault(o => string.Equals(o.Id, Normalize(v), StringComparison.Ordinal))?.Display
        ?? "数值（固定数字）";

    public static ParamSourceOption? Find(string? v) =>
        All.FirstOrDefault(o => string.Equals(o.Id, Normalize(v), StringComparison.Ordinal));
}

/// <summary>一条参数来源候选项（界面下拉用）。</summary>
public sealed record ParamSourceOption(string Id, string Display, string Hint)
{
    public override string ToString() => Display;
}

/// <summary>
/// 「从谁身上取这个值」的候选。
/// 注意和 <see cref="Effects.EffectCatalog.ConditionTargets"/> 是两套：
/// 那边是条件判断的指向对象，这里是**取值对象**（多了召唤物、全部友方）。
/// </summary>
public static class ParamTargets
{
    public const string Self = "Self";
    public const string ChosenEnemy = "ChosenEnemy";
    public const string RandomEnemy = "RandomEnemy";
    public const string AllEnemies = "AllEnemies";
    public const string AllAllies = "AllAllies";
    public const string Summon = "Summon";

    public static IReadOnlyList<ParamTargetOption> All { get; } = new[]
    {
        new ParamTargetOption(Self,        "自己"),
        new ParamTargetOption(ChosenEnemy, "指定敌人"),
        new ParamTargetOption(RandomEnemy, "随机敌人"),
        new ParamTargetOption(AllEnemies,  "全部敌人"),
        new ParamTargetOption(AllAllies,   "全部友方"),
        new ParamTargetOption(Summon,      "召唤物"),
    };

    public static string Normalize(string? v) =>
        All.Any(o => string.Equals(o.Id, v, StringComparison.Ordinal)) ? v! : Self;

    public static string Zh(string? v) =>
        All.FirstOrDefault(o => string.Equals(o.Id, Normalize(v), StringComparison.Ordinal))?.Display ?? "自己";

    public static ParamTargetOption? Find(string? v) =>
        All.FirstOrDefault(o => string.Equals(o.Id, Normalize(v), StringComparison.Ordinal));
}

/// <summary>一条「取值对象」候选项（界面下拉用）。</summary>
public sealed record ParamTargetOption(string Id, string Display)
{
    public override string ToString() => Display;
}

/// <summary>
/// 「这个参数给予多少能量 / 多少额外资源量」的取值（需求：上方为自定义参数，默认显示给予 X 参数给能量、
/// 给予 Y 参数给额外资源量）。
///
/// 存的是**另一个参数的字母**（或者 <see cref="None"/> = 不给予）：
/// 也就是说「能量给多少」这件事本身也是由某个参数算出来的 ——
/// 默认那一套就是 <c>X</c>（当前所有能量）与 <c>Y</c>（当前所有额外资源量），
/// 于是「开局把能量 / 额外资源量给满」是一句话的事。
/// </summary>
public static class ParamGrants
{
    public const string None = "";

    public static string Normalize(string? v)
    {
        string s = (v ?? "").Trim().TrimEnd('=').Trim();
        if (s.Length == 0) return None;                       // 不给予
        return ParamNames.Normalize(s);                       // 其余一律当成一个参数名
    }

    /// <summary>界面下拉里显示的名字（"" = 不给予）。</summary>
    public static string LabelOf(string? v)
    {
        string s = Normalize(v);
        return s.Length == 0 ? "不给予" : s + "=";
    }

    public static string Zh(string? v)
    {
        string s = Normalize(v);
        return s.Length == 0 ? "不给予" : $"参数 {s} 的值";
    }
}

/// <summary>参数名的规则（一个字母 + 保留字）。</summary>
public static class ParamNames
{
    /// <summary>
    /// 已经被占用、不能当自定义参数名的字母：
    /// X = 当前所有能量，Y = 当前所有额外资源量（用户需求里点名的两栏）。
    /// </summary>
    public static IReadOnlyList<string> Reserved { get; } = new[] { "X", "Y" };

    /// <summary>可用字母池（A~Z 去掉保留字），「新建参数」按这个顺序挑第一个没被占的。</summary>
    public static IReadOnlyList<string> Pool { get; } =
        Enumerable.Range('A', 26).Select(c => ((char)c).ToString())
                  .Where(s => !Reserved.Contains(s, StringComparer.Ordinal)).ToList();

    /// <summary>存进配置前规整成「一个大写字母」；认不出来就退回 M（老存档 / 手写 JSON 兜底）。</summary>
    public static string Normalize(string? v)
    {
        string s = (v ?? "").Trim().TrimEnd('=').Trim().ToUpperInvariant();
        if (s.Length == 0) return "M";
        return s.Substring(0, 1);
    }

    /// <summary>
    /// 挑一个**没有被占用**的字母当新参数名（需求：点击新建参数时自动生成一个不会被占用的参数）。
    /// 用满了 A~Z（去掉 X/Y 共 24 个）就返回 null，界面会提示。
    /// </summary>
    public static string? NextFree(IEnumerable<string> taken)
    {
        var used = new HashSet<string>((taken ?? Enumerable.Empty<string>())
            .Select(Normalize), StringComparer.Ordinal);
        return Pool.FirstOrDefault(s => !used.Contains(s));
    }

    /// <summary>这个字母能不能当参数名（保留字 / 空都不行）。</summary>
    public static bool IsUsable(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && Pool.Contains(Normalize(name), StringComparer.Ordinal);
}

/// <summary>
/// 一个「组」（需求：下方为「组」）。
///
/// 组只是一层**分类**：卡牌属性栏右边勾上哪些组，这张牌就属于哪些组。
/// 「只影响在生成器里的查看」= 组不参与生成出来的模组代码，
/// 但「范围选择」可以按组来筛（生成 / 变化卡牌 / 节点里的范围下拉会多出「组：XXX」这一档）。
/// </summary>
public sealed class CardGroupSpec : SpecBase
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "新组";
    private string _note = "";

    /// <summary>
    /// 组的稳定标识：**名字是可以随便改的**，卡牌引用的是这个 Id。
    /// 用 Id 而不是名字，是为了「改了组名之后已经勾好的卡不会全部掉出这个组」。
    /// </summary>
    public string Id
    {
        get => _id;
        set => Set(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    /// <summary>组名（只在生成器里显示 / 在范围下拉里出现）。</summary>
    public string Name
    {
        get => _name;
        set { if (Set(ref _name, string.IsNullOrWhiteSpace(value) ? "新组" : value.Trim())) Raise(nameof(Display)); }
    }

    /// <summary>备注（不参与生成，纯粹给你自己看）。</summary>
    public string Note
    {
        get => _note;
        set => Set(ref _note, value ?? "");
    }

    /// <summary>界面列表里那一行（组名 + 备注）。</summary>
    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(_note) ? _name : $"{_name} ｜ {_note}";

    private List<string> _powerIds = new();

    /// <summary>
    /// V0.2.255（用户要求「让状态进组」）：这个组里的**状态**（Power 类名，自定义状态 / 本体状态都行）。
    /// 卡牌的归属记在卡自己身上（<c>CardSpec.GroupIds</c>）；状态的归属直接记在组这边 ——
    /// 本体状态没法往它身上加字段，只有这一个位置两种状态都放得下。
    /// 目前只有「清除指定状态」那条效果读它（一次清掉一个组里的所有状态）。
    /// </summary>
    public List<string> PowerIds
    {
        get => _powerIds;
        set { if (Set(ref _powerIds, value ?? new List<string>())) Raise(nameof(PowerIdList)); }
    }

    /// <summary>去掉空值 / 重复之后的状态类名（读老存档、手写 JSON 时兜底）。</summary>
    [JsonIgnore]
    public List<string> PowerIdList =>
        PowerIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();

    private List<string> _audioIds = new();

    /// <summary>
    /// V0.2.265（用户要求「音频也能加入组」）：这个组里的**音频** ——
    /// 存的是「美术资源」页上传的那些音频的 <c>Key</c>（= <c>res://audio/&lt;Key&gt;</c> 里那个文件名，
    /// 也就是 <see cref="EffectSpec.Sfx"/> 里存的东西）。
    ///
    /// <para>用处：「播放音频文件」那条效果勾了「仅能被下个同组音频顶替」时，
    /// 生成端按这张表判断「新播的这段音频和它是不是同组」—— 同组才允许顶替。</para>
    /// </summary>
    public List<string> AudioIds
    {
        get => _audioIds;
        set { if (Set(ref _audioIds, value ?? new List<string>())) Raise(nameof(AudioIdList)); }
    }

    /// <summary>去掉空值 / 重复之后的音频 Key（大小写不敏感去重：Windows 上文件名本来就不分大小写）。</summary>
    [JsonIgnore]
    public List<string> AudioIdList =>
        AudioIds.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
}

/// <summary>
/// 「转化为先古卡」效果里的**一行配对**：左边是「源组」，右边是「换成哪些先古卡」。
///
/// 需求原话：「能够选一组卡全变成不同的先古卡吗」+「来源只用组就行」。所以：
///   · 源组**只用「组」那一套**（自定义组 + 自定义关键词），不做「攻击 / 技能 / 打击防御」那类内置筛选项；
///   · 右边是一个**先古卡池**（本体 19 张 + 你自己的先古卡），这一行里每张匹配到的牌从池子里
///     **按勾选顺序不重复**地各拿一张 → 「一组卡变成不同的先古卡」；
///   · 池子用完了后面那几张**保持原样**（用户明确选的下策 b），不循环复用。
///
/// 为什么一行一张表而不是一个大池子：行和行互不干扰 —— 「打击组 → 池子 A」「防御组 → 池子 B」
/// 两条互不影响，也比「一个池子按顺序发给所有匹配的牌」好预期。
/// </summary>
public sealed class AncientTranscendRow : SpecBase
{
    private string _group = "";
    private List<string> _ancientCards = new();

    /// <summary>
    /// 源组：存范围限定那套写法（<c>Group:组的Id</c> / <c>Keyword:关键词标识</c>）。
    /// 空 = 这行还没配（校验器会报错）。
    /// </summary>
    public string Group
    {
        get => _group;
        set => Set(ref _group, value ?? "");
    }

    /// <summary>
    /// 换成哪些先古卡（存**类名**，和别处的 SpawnCardId 一致：卡牌改类名后由失效引用那套检查兜底）。
    /// 顺序就是分配顺序 —— 第 1 张匹配到的牌拿第 1 个，第 2 张拿第 2 个，一行内不重复。
    /// </summary>
    public List<string> AncientCards
    {
        get => _ancientCards;
        set => Set(ref _ancientCards, value ?? new List<string>());
    }

    /// <summary>界面列表里那一行（源组 → 换成的先古卡们）。</summary>
    [JsonIgnore]
    public string Display => (string.IsNullOrWhiteSpace(_group) ? "（还没选源组）" : _group)
        + " → " + (_ancientCards.Count == 0 ? "（还没选先古卡）" : string.Join("、", _ancientCards));
}
