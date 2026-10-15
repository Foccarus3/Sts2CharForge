using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Sts2CharForge.Core.Profile;

/// <summary>
/// 锻造节点图里的**一个模块**（需求里的【触发时机】【if】【对象】【状态】【数值】【能量】【效果】【且】【或】【条件】）。
///
/// 为什么做得这么"通用"（Kind + 一串字段 + 位置 + 几个端口）：模块会一直加，
/// 每加一种就写一套新的类会让存档 / 回读 / 界面绘制各写一遍。这里统一成
///   · <see cref="Kind"/> 决定它长什么样、有哪些端口（见 <see cref="ForgeGraphCatalog"/>）；
///   · <see cref="Fields"/> 存这个模块自己的设置（键 = <see cref="ForgeNodeField.Key"/>）；
///   · <see cref="X"/>/<see cref="Y"/> 是它在布上的位置（可以拖动）；
///   · 端口不是"存取的值"，而是**从 Kind 算出来的**（第 n 个输入口 / 第 n 个输出口），
///     连线记的是"从哪个模块的第几个出口 → 到哪个模块的第几个入口"。
/// </summary>
public sealed class ForgeNodeSpec : SpecBase
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _kind = "Trigger";
    private double _x = 320;
    private double _y = 160;
    private Dictionary<string, string> _fields = new(StringComparer.Ordinal);
    private EffectSpec? _effectSpec;

    /// <summary>模块在布上的稳定标识（连线记的是它；拖动、改名都不影响）。</summary>
    public string Id
    {
        get => _id;
        set => Set(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    /// <summary>模块种类（<see cref="ForgeGraphCatalog.Find"/> 里那些 Id）。</summary>
    public string Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, string.IsNullOrWhiteSpace(value) ? "Trigger" : value.Trim())) return;
            Raise(nameof(Title));
            Raise(nameof(Display));
        }
    }

    /// <summary>布上的横坐标（左上角，单位 = 画布像素）。</summary>
    public double X
    {
        get => _x;
        set => Set(ref _x, double.IsFinite(value) ? value : 0);
    }

    /// <summary>布上的纵坐标。</summary>
    public double Y
    {
        get => _y;
        set => Set(ref _y, double.IsFinite(value) ? value : 0);
    }

    /// <summary>
    /// 这个模块自己的设置（键 = 字段 Key）。存字符串是为了统一：
    /// 数字 / 下拉 / 复选 / 文本在存档里都是同一格，读老存档也不怕某个字段没了。
    /// </summary>
    public Dictionary<string, string> Fields
    {
        get => _fields;
        set { if (Set(ref _fields, value ?? new Dictionary<string, string>(StringComparer.Ordinal))) { Raise(nameof(Display)); Raise(nameof(Title)); } }
    }

    /// <summary>读一个字段（没有就返回 <paramref name="fallback"/>）。</summary>
    public string Get(string key, string fallback = "") =>
        _fields.TryGetValue(key ?? "", out string? v) ? (v ?? fallback) : fallback;

    /// <summary>写一个字段。</summary>
    public void SetField(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        string v = value ?? "";
        if (_fields.TryGetValue(key, out string? old) && string.Equals(old ?? "", v, StringComparison.Ordinal)) return;
        _fields[key] = v;
        Raise(nameof(Fields));
        Raise(nameof(Title));
        Raise(nameof(Display));
    }

    /// <summary>
    /// 读一个数值字段。
    ///
    /// V0.2.128（用户拍板 Q2=A：数字框里也能直接填公式）之后**不再「解析失败 → 静默回落」**：
    ///   ① 纯数字 → 直接用（老存档一个字符都不变）；
    ///   ② 公式、且生成期能折叠成常数（<c>2*3</c>）→ 用折出来的数；
    ///   ③ 公式里用了 X / Y / 自定义参数 / 内置只读量 → 这里**算不出数**（要在运行时才算），
    ///      返回 fallback，但**校验器会报错**说清是哪个模块哪一栏（见 <c>ForgeGraphGen.Issues</c>），
    ///      真正要生成代码的地方一律走 <c>ForgeNumbers.Resolve</c>（它拿得到整份配置）+ 生成运行时表达式；
    ///   ④ 解析不出来（拼错 / 括号不配 / 不存在的参数）→ 同样返回 fallback，由校验器报错拦住。
    /// 需要「知道到底是哪一档」的调用点请用 <c>ForgeNumbers.Resolve</c>。
    /// </summary>
    public double GetNumber(string key, double fallback = 0)
    {
        var r = Generation.ForgeNumbers.Resolve(null, this, key, fallback);
        return r.Ok && !r.Runtime ? (double)r.Value : fallback;
    }

    public void SetNumber(string key, double value) =>
        SetField(key, value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>读一个开关字段。</summary>
    public bool GetFlag(string key, bool fallback = false)
    {
        string raw = Get(key);
        if (raw.Length == 0) return fallback;
        return raw is "1" or "true" or "True" or "是";
    }

    public void SetFlag(string key, bool value) => SetField(key, value ? "1" : "0");

    /// <summary>
    /// 【效果】模块自己的编辑器模型 —— 就是「卡牌 / 遗物 / 药水 / 自定义状态」四处共用的
    /// <see cref="Profile.EffectSpec"/>（用户拍板 Q1=A：锻造【效果】模块**直接复用**卡牌那套编辑器）。
    ///
    /// 和 <see cref="Fields"/> 的关系（老存档安全的关键，见 <c>Generation.ForgeEffectBridge</c>）：
    ///   · 老存档里**只有** <see cref="Fields"/>（`Effect` / `Amount` / `Times` / `Hits` / `Chance`…），
    ///     打开存档时按老字段**幂等**迁出一个 EffectSpec（<c>FromLegacy=true</c>）；
    ///   · `FromLegacy=true` 时生成端**照旧只读 Fields** —— 老存档生成出来的代码 / 卡面文案逐字不变；
    ///   · 用户在新编辑器里改了任何一格 → 桥把 spec 回写成 Fields（`FromLegacy=false`），
    ///     此后 spec 是唯一事实来源，生成端照旧读 Fields（所以生成器那一边几乎不用改）。
    /// ⚠ **不能加 [JsonIgnore]**（照 V0.2.126 处理 `RelicTriggerSpec.Effects` 的先例）：
    ///   加了的话新存档里的编辑器内容读不回来。
    /// </summary>
    public EffectSpec? EffectSpec
    {
        get => _effectSpec;
        set
        {
            if (Set(ref _effectSpec, value)) { Raise(nameof(Title)); Raise(nameof(Display)); }
        }
    }

    private bool _fromLegacy;

    /// <summary>
    /// 这一份 <see cref="EffectSpec"/> 是不是**从老字段迁出来的**（还没有被新编辑器改过）。
    /// true = 生成端只认 <see cref="Fields"/>（老存档逐字不变）；false = 用 spec 回写 Fields 之后再生成。
    /// </summary>
    public bool EffectFromLegacy { get => _fromLegacy; set => Set(ref _fromLegacy, value); }

    /// <summary>桥上「变更通知已经挂上了」的标记（不写进存档；防止 Rebuild 时重复订阅）。</summary>
    [JsonIgnore] internal bool EffectBridgeBound { get; set; }

    /// <summary>界面上「标题栏跟着效果种类刷新」的订阅标记（不写进存档；防止 Rebuild 时重复订阅）。</summary>
    [JsonIgnore] public bool EffectTitleBound { get; set; }

    /// <summary>这个模块的种类定义（认不出来返回 null，界面会当成"未知模块"画一个红框）。</summary>
    [JsonIgnore] public ForgeNodeKind? KindDef => ForgeGraphCatalog.Find(_kind);

    /// <summary>模块标题（种类名；「数值」模块会带上前面几个字，方便在布上一眼分辨）。</summary>
    [JsonIgnore]
    public string Title
    {
        get
        {
            var def = KindDef;
            if (def is null) return $"未知模块（{_kind}）";
            if (def.Id == "Value")
            {
                string f = Get("Formula").Trim();
                return f.Length == 0 ? def.Display : $"{def.Display}：{Shorten(f, 14)}";
            }
            if (def.HasField("Timing")) return $"{def.Display}：{ForgeTimings.ShortZh(Get("Timing"))}";
            if (def.HasField("Effect")) return $"{def.Display}：{Effects.EffectCatalog.FindKind(Get("Effect", "Damage")).Display}";
            return def.Display;
        }
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

    /// <summary>列表 / 提示里那一行。</summary>
    [JsonIgnore] public string Display => $"{KindDef?.Display ?? _kind} @({X:0},{Y:0})";

    /// <summary>第 <paramref name="index"/> 个输入端口（没有就是 null）。</summary>
    public ForgePort? InputPort(int index) => KindDef?.InputPort(index);

    /// <summary>第 <paramref name="index"/> 个输出端口（没有就是 null）。</summary>
    public ForgePort? OutputPort(int index) => KindDef?.OutputPort(index);

    /// <summary>这个模块在生成代码里的变量名前缀（数值模块用 <c>__v3</c> 这种）。</summary>
    [JsonIgnore] public string VarName => "__fg" + StableSuffix(_id);

    /// <summary>把 Id 压成一段能当 C# 标识符的短后缀（取哈希的前 6 位十六进制）。</summary>
    internal static string StableSuffix(string id)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in id ?? "") { h ^= c; h *= 16777619u; }
            return h.ToString("x8").Substring(0, 6);
        }
    }
}

/// <summary>一条连线：从某个模块的第 <see cref="FromPort"/> 个出口 → 某个模块的第 <see cref="ToPort"/> 个入口。</summary>
public sealed class ForgeLinkSpec : SpecBase
{
    private string _from = "";
    private string _to = "";
    private int _fromPort;
    private int _toPort;

    /// <summary>起点模块的 <see cref="ForgeNodeSpec.Id"/>。</summary>
    public string From { get => _from; set => Set(ref _from, value ?? ""); }

    /// <summary>终点模块的 <see cref="ForgeNodeSpec.Id"/>。</summary>
    public string To { get => _to; set => Set(ref _to, value ?? ""); }

    /// <summary>从起点的第几个出口拉出来的（0 = 第一个，对应界面上的第一个圆点）。</summary>
    public int FromPort { get => _fromPort; set => Set(ref _fromPort, value < 0 ? 0 : value); }

    /// <summary>接到终点的第几个入口（0 = 第一个）。</summary>
    public int ToPort { get => _toPort; set => Set(ref _toPort, value < 0 ? 0 : value); }

    [JsonIgnore] public string Display => $"{From}#{FromPort} → {To}#{ToPort}";
}

/// <summary>
/// 一张锻造节点图（一块"布"）。
///
/// 卡牌 / 药水 / 遗物各带一张**升级前**的布；卡牌还能再带一张**升级后**的布
/// （界面上左上角的「升级前 / 升级后」分页，升级后那张布上有一键复制升级前的悬浮按钮）。
/// </summary>
public sealed class ForgeGraphSpec : SpecBase
{
    private List<ForgeNodeSpec> _nodes = new();
    private List<ForgeLinkSpec> _links = new();
    private string _theory = "";

    public List<ForgeNodeSpec> Nodes
    {
        get => _nodes;
        set { if (Set(ref _nodes, value ?? new List<ForgeNodeSpec>())) { Raise(nameof(Display)); Raise(nameof(TriggerNode)); } }
    }

    public List<ForgeLinkSpec> Links
    {
        get => _links;
        set { if (Set(ref _links, value ?? new List<ForgeLinkSpec>())) Raise(nameof(Display)); }
    }

    /// <summary>备注（不参与生成，纯粹给你自己看）。</summary>
    public string Note { get => _theory; set => Set(ref _theory, value ?? ""); }

    /// <summary>这个模块 Id 对应的模块（没有就返回 null）。</summary>
    public ForgeNodeSpec? Node(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : _nodes.FirstOrDefault(n => n is not null && string.Equals(n.Id, id, StringComparison.Ordinal));

    /// <summary>这张布上的触发时机模块（第一块；没有就返回 null）。</summary>
    [JsonIgnore]
    public ForgeNodeSpec? TriggerNode =>
        _nodes.FirstOrDefault(n => n is not null && string.Equals(n.Kind, "Trigger", StringComparison.Ordinal));

    /// <summary>
    /// 这张布上**所有**触发时机模块（按模块顺序）。
    ///
    /// <para>V0.2.168：一块布现在可以有**多块**【触发时机】—— 每块配自己的一条链，
    /// 落在同一个本体钩子上的那几块由校验器拦住（要求合成一块），别的各自生成一个钩子。</para>
    /// <para><see cref="TriggerNode"/> 仍然是「第一块」（老代码 / 老图的行为不变）。</para>
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ForgeNodeSpec> TriggerNodes =>
        _nodes.Where(n => n is not null && string.Equals(n.Kind, "Trigger", StringComparison.Ordinal)).ToList();

    /// <summary>这块布是不是空的（没有模块）。</summary>
    [JsonIgnore] public bool IsEmpty => _nodes.Count == 0;

    /// <summary>接到某个模块第 <paramref name="port"/> 个入口上的那条线（没有就返回 null）。</summary>
    public ForgeLinkSpec? Incoming(string? nodeId, int port) =>
        string.IsNullOrWhiteSpace(nodeId) ? null
        : _links.FirstOrDefault(l => l is not null && string.Equals(l.To, nodeId, StringComparison.Ordinal) && l.ToPort == port);

    /// <summary>从某个模块第 <paramref name="port"/> 个出口拉出去的所有线。</summary>
    public List<ForgeLinkSpec> Outgoing(string? nodeId, int port) =>
        string.IsNullOrWhiteSpace(nodeId) ? new List<ForgeLinkSpec>()
        : _links.Where(l => l is not null && string.Equals(l.From, nodeId, StringComparison.Ordinal) && l.FromPort == port).ToList();

    /// <summary>新建一块布：正中间放一个【触发时机】（需求：其最中间是【触发时机】模块，默认为「卡牌被打出时」）。</summary>
    public static ForgeGraphSpec NewCardGraph()
    {
        var g = new ForgeGraphSpec();
        g.Nodes.Add(new ForgeNodeSpec
        {
            Kind = "Trigger",
            X = ForgeGraphCatalog.CanvasCenterX,
            Y = ForgeGraphCatalog.CanvasCenterY,
            Fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Timing"] = ForgeTimings.CardPlayed,
            },
        });
        // 【能量】模块固定在左上角（需求：有"能量"、"额外资源量"两个栏位，放置于左上角固定）
        g.Nodes.Add(new ForgeNodeSpec
        {
            Kind = "Energy",
            X = ForgeGraphCatalog.EnergyX,
            Y = ForgeGraphCatalog.EnergyY,
        });
        return g;
    }

    /// <summary>新建一块药水 / 遗物用的布（触发时机按调用方给的默认值）。</summary>
    public static ForgeGraphSpec NewWithTiming(string defaultTiming)
    {
        var g = new ForgeGraphSpec();
        g.Nodes.Add(new ForgeNodeSpec
        {
            Kind = "Trigger",
            X = ForgeGraphCatalog.CanvasCenterX,
            Y = ForgeGraphCatalog.CanvasCenterY,
            Fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Timing"] = defaultTiming,
            },
        });
        return g;
    }

    [JsonIgnore]
    public string Display => IsEmpty ? "（空的布）" : $"{_nodes.Count} 个模块 · {_links.Count} 条连线";
}

/// <summary>
/// 一个端口（模块左边第 n 个圆点 / 右边第 n 个圆点）。
/// </summary>
/// <param name="Key">端口标识（生成 / 语义判定用，不写进存档）。</param>
/// <param name="Label">显示文字（空 = 只画一个圆点）。</param>
/// <param name="Hint">鼠标悬停时的说明。</param>
/// <param name="IsInput">true = 普通接入点，收别人的出口。</param>
/// <param name="AcceptsKind">能接什么（Number / Bool / Object / null = 执行链）。</param>
/// <param name="Multi">保留字段（一个口能不能接多条线）。</param>
/// <param name="PullsSource">
/// 老存档里「**从哪取**」的那种口：老版【if】的「条件」口就是它 —— 看起来像出口（画在右边），
/// 语义上是**往里插一个来源模块**，生成代码时按出口下标读它。
/// ⚠ 新版【if】已经把「条件」改成**左边的普通入口**（用户要求：条件那条链要从左边进来），
/// 所以新图里不再有这种口 —— 这个标记只用来兼容 / 迁移老存档。
/// </param>
public sealed record ForgePort(string Key, string Label, string Hint, bool IsInput, string? AcceptsKind = null,
    bool Multi = false, bool PullsSource = false)
{
    /// <summary>显示文字（空串 = 只画一个圆点，需求：接入点和出口点若有字皆以小字体显示，若无字仅显示一个圆点）。</summary>
    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);
}

/// <summary>模块上的一个可编辑字段。</summary>
public sealed record ForgeNodeField(
    string Key,
    string Label,
    ForgeFieldType Type,
    string Hint = "",
    IReadOnlyList<ForgeFieldOption>? Options = null,
    string Default = "",
    bool Searchable = false,
    bool Wide = false)
{
    public IReadOnlyList<ForgeFieldOption> OptionList => Options ?? Array.Empty<ForgeFieldOption>();
}

/// <summary>字段的编辑方式。</summary>
public enum ForgeFieldType
{
    /// <summary>单行文本。</summary>
    Text,
    /// <summary>多行文本（公式）。</summary>
    Formula,
    /// <summary>下拉。</summary>
    Pick,
    /// <summary>勾选框。</summary>
    Check,
    /// <summary>数字。</summary>
    Number,
}

/// <summary>下拉里的一项。</summary>
public sealed record ForgeFieldOption(string Id, string Display)
{
    public override string ToString() => Display;
}

/// <summary>一种模块（【触发时机】【数值】【能量】…）。端口和字段都由它的种类决定。</summary>
public sealed record ForgeNodeKind(
    string Id,
    string Display,
    string Category,
    string Accent,
    string Hint,
    IReadOnlyList<ForgePort> Inputs,
    IReadOnlyList<ForgePort> Outputs,
    IReadOnlyList<ForgeNodeField> Fields,
    bool Fixed = false,
    string? Scope = null)   // Card / Potion / Relic / null = 都能用
{
    public ForgePort? InputPort(int i) => i >= 0 && i < Inputs.Count ? Inputs[i] : null;
    public ForgePort? OutputPort(int i) => i >= 0 && i < Outputs.Count ? Outputs[i] : null;
    public bool HasField(string key) => Fields.Any(f => string.Equals(f.Key, key, StringComparison.Ordinal));
}
