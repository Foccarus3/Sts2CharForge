using System.Text.Json;
using System.Text.Json.Serialization;
using Sts2CharForge.Core.Effects;

namespace Sts2CharForge.Core.Plugins;

/// <summary>
/// 一条「效果种类」插件（V0.2.315）。
///
/// <para>插件只用**数据 + 代码模板**声明，不需要编译成 DLL —— 这样编辑器升级（甚至换 .NET 版本）后，
/// 老插件照样能被读出来（需求②「插件不会因编辑器的更新而失效」）。</para>
///
/// <para><see cref="Emit"/> 是一段 C# 模板，生成时会替换占位符（见 <c>PluginGen</c>）：
/// <c>{amount}</c> / <c>{self}</c> / <c>{player}</c> / <c>{foe}</c> / <c>{choice}</c> /
/// <c>{cardSelf}</c> / <c>{power}</c>。模板里可以写多行。</para>
/// </summary>
public sealed class PluginEffectSpec
{
    /// <summary>种类 id（英文，生成代码/CET 标记里都用它）。</summary>
    public string Kind { get; set; } = "";

    /// <summary>界面上的中文名。</summary>
    public string Display { get; set; } = "";

    /// <summary>
    /// V0.2.359：这个效果在**卡面 / 遗物 / 药水描述**里写的那句话（中文）。
    /// 留空 = 和以前一样：插件效果不产生任何卡面文字（只能靠「自定义描述」手写）。
    ///
    /// <para>可用占位符 <c>{amount}</c>（会替换成这条效果的数值；「数值 = X」时写 <c>X</c>、
    /// 填了「数值公式」时写公式文本）。<b>注意</b>：插件效果不声明动态变量，所以这里写的是**字面数字**，
    /// 不会跟着力量 / 敏捷实时变 —— 要实时变只能等以后支持「动态变量」那一档。</para>
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// V0.2.359：这个效果要挂的**悬停说明**（C# 表达式，原样写进 <c>ExtraHoverTips</c> 的列表里）。
    /// 例：<c>MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromPower&lt;VulnerablePower&gt;()</c>
    /// 或 <c>new MegaCrit.Sts2.Core.HoverTips.HoverTip(new LocString("static_hover_tips", "MY_KEY.title"), …)</c>。
    /// 留空 = 不挂。写错了会在编译期报错（不是静默失效）。
    /// </summary>
    public string HoverTip { get; set; } = "";

    /// <summary>
    /// V0.2.360：这个效果要声明的**动态变量名**（例：<c>Heal</c> / <c>Block</c> / 你自己的名字）。
    /// 填了之后：卡牌 / 遗物 / 药水会真的声明这个变量，`{amount}` 在**卡面**上变成 <c>{变量:diff()}</c>
    /// （升级后能显示对比值）、在**生成代码**里变成 <c>base.DynamicVars["变量"]</c> —— 于是数字会跟着
    /// 力量 / 敏捷 / 升级这些实时算，不再是写死的字面量。
    ///
    /// <para>留空 = 老行为（不声明变量、卡面写字面数字）。<c>X</c> / <c>Y</c> 是保留名，不能用。</para>
    /// </summary>
    public string Var { get; set; } = "";

    /// <summary>数值单位（「点」/「张」/「层」…）。</summary>
    public string Unit { get; set; } = "点";

    public decimal Min { get; set; }

    public decimal Max { get; set; } = 99m;

    /// <summary>要不要「作用对象」那一栏（自己 / 指定敌人 / 全体敌人…）。</summary>
    public bool NeedsTarget { get; set; }

    /// <summary>要不要「增益 / 减益」那一栏（会替换 <c>{power}</c>）。</summary>
    public bool NeedsPower { get; set; }

    /// <summary>要不要「目标卡」那一栏（会替换 <c>{card}</c>）。</summary>
    public bool NeedsCard { get; set; }

    /// <summary>生成出来的 C# 模板（占位符见类注释）。</summary>
    public string Emit { get; set; } = "";

    /// <summary>一句话说明（编辑器里显示）。</summary>
    public string Note { get; set; } = "";
}

/// <summary>触发时机插件（本版**只登记 + 显示**，生成端的接线在下一版；见 <see cref="PluginPlugins"/> 的说明）。</summary>
public sealed class PluginTriggerSpec
{
    public string Id { get; set; } = "";
    public string Display { get; set; } = "";

    /// <summary>
    /// V0.2.362：**自定义触发时机** —— 插件自己写完整的 <c>override</c> 签名（方法名 + 参数），
    /// 生成端原样写进状态类，于是「本体有、但编辑器没收录」的钩子也能用。
    ///
    /// <para>例：<c>async Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)</c>。
    /// 写错（方法名不存在 / 参数对不上）会在**编译期**报错（CS0115 之类），不会静默失效。
    /// 签名里带 <c>PlayerChoiceContext choiceContext</c> 时，生成端会按需补 <c>choiceContext</c> 的取值。</para>
    ///
    /// <para>填了它之后 <see cref="Hook"/> 只当「方法名」用（不用再是编辑器认识的钩子 id）。</para>
    /// </summary>
    public string Signature { get; set; } = "";

    /// <summary>挂在哪个本体钩子上（必须是编辑器已知的钩子 id，如 <c>DamageDealt</c>）。</summary>
    public string Hook { get; set; } = "";

    /// <summary>额外的进门守卫（C# 片段，可为空）。</summary>
    public string Guard { get; set; } = "";

    public bool HasChoiceContext { get; set; } = true;
    public string Note { get; set; } = "";
}

/// <summary>条件插件（本版只登记 + 显示）。</summary>
public sealed class PluginConditionSpec
{
    public string Id { get; set; } = "";
    public string Display { get; set; } = "";
    public string Expr { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>锻造模块插件（本版只登记 + 显示）。</summary>
public sealed class PluginForgeModuleSpec
{
    public string Kind { get; set; } = "";
    public string Display { get; set; } = "";
    public string Emit { get; set; } = "";
    public string Note { get; set; } = "";

    /// <summary>
    /// V0.2.368（用户要求）：这个锻造模块在**图上**要显示的额外栏位。
    ///
    /// <para>以前插件锻造模块在图上只有一个【数值】栏 —— 做不出「变化卡牌」那种带参数（目标卡 / 取牌方式 / 范围…）的节点。
    /// 现在按这里声明，每个栏位都会出现在【效果】模块上，emit 里写 <c>{字段key}</c> 就会被换成节点上填的值
    /// （没填用 <see cref="PluginNodeFieldSpec.Default"/>）。不声明 = 老行为，一个像素都不变。</para>
    /// </summary>
    public List<PluginNodeFieldSpec> Fields { get; set; } = new();
}

/// <summary>插件锻造模块上的一个栏位（V0.2.368）。</summary>
public sealed class PluginNodeFieldSpec
{
    /// <summary>字段名（英文/数字/下划线；emit 里写 <c>{Key}</c>）。</summary>
    public string Key { get; set; } = "";

    /// <summary>界面上显示的中文标签。</summary>
    public string Label { get; set; } = "";

    /// <summary><c>text</c> / <c>number</c> / <c>formula</c> / <c>pick</c> / <c>check</c>（默认 text）。</summary>
    public string Type { get; set; } = "text";

    /// <summary>默认值（节点上没填时用它；<c>check</c> 用 <c>1</c> / <c>0</c>）。</summary>
    public string Default { get; set; } = "";

    /// <summary><c>pick</c> 的候选（<c>"值|显示"</c> 或只写值）。</summary>
    public List<string> Options { get; set; } = new();

    /// <summary>鼠标悬停的说明。</summary>
    public string Hint { get; set; } = "";
}

/// <summary>
/// 插件贡献的一条**公式变量**（V0.2.368）：在「数值公式」里当变量名用。
/// </summary>
public sealed class PluginFormulaVarSpec
{
    /// <summary>变量名（英文/数字/下划线，字母开头；不能和内置的重名）。</summary>
    public string Id { get; set; } = "";

    /// <summary>下拉里显示的中文说明。</summary>
    public string Display { get; set; } = "";

    /// <summary>C# 表达式（decimal），可用 <c>{p}</c>（玩家）和 <c>{c}</c>（自己这个 Creature）。</summary>
    public string CSharp { get; set; } = "";
}
/// <summary>插件自定义页上的一个栏位（V0.2.319；V0.2.349 起支持 <c>list</c> 动态行）。</summary>
public sealed class PluginFieldSpec
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>
    /// <c>text</c> / <c>number</c> / <c>bool</c> / <c>pick</c> / <c>multiline</c> / <c>list</c>。
    ///
    /// <para>V0.2.349（「能适配的框架」）：多了 <c>list</c> —— 一个**可增删、可上下移的行集合**，
    /// 每行的子栏位写在 <see cref="Fields"/> 里。插件用它就能表达「招式列表 / 怪物列表 / 掉落表」
    /// 这种动态列表（以前只能做静态表单，所以「完整敌人编辑器」那种界面在插件里做不出来）。</para>
    /// </summary>
    public string Type { get; set; } = "text";

    public string Default { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public string Hint { get; set; } = "";

    /// <summary>
    /// <c>list</c> 字段的**子栏位**（每一行都按这套定义渲染；子栏位自己不能再是 <c>list</c>，
    /// 嵌套列表 v1 不支持，加载时会报一条问题而不是静默失效）。
    /// </summary>
    public List<PluginFieldSpec> Fields { get; set; } = new();

    /// <summary><c>list</c> 字段的行标题口径（例：「招式」→ 每行显示「招式 1」）。空 = 用 <see cref="Label"/>。</summary>
    public string ItemLabel { get; set; } = "";

    /// <summary>
    /// <c>list</c> 字段初始给几行（只在还没有存过任何行时生效；0 = 空列表，用户自己点「添加一行」）。
    /// </summary>
    public int DefaultRows { get; set; }

    /// <summary>
    /// V0.2.356（「能适配的框架」第二部分）：<c>type = "ref"</c> 的**引用类型** —— 下拉里列的是
    /// **当前存档里真实存在的对象**，选中的值存它的**类名 / 组 Id**（稳定标识，和生成端用的是同一个）。
    /// 取值：<c>card</c> / <c>power</c> / <c>relic</c> / <c>potion</c> / <c>pet</c> / <c>group</c>。
    /// 这样插件页就能真引用「存档里的某张卡 / 某个状态」，而不是让用户手抄类名。
    /// </summary>
    public string RefKind { get; set; } = "";

    /// <summary>这个栏位是不是引用型（从当前存档里挑对象）。</summary>
    public bool IsRef => string.Equals((Type ?? "").Trim(), "ref", StringComparison.OrdinalIgnoreCase);

    /// <summary>这个栏位是不是动态行集合。</summary>
    public bool IsList => string.Equals((Type ?? "").Trim(), "list", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 额外内容页（V0.2.315 起：Markdown 说明页；V0.2.319 起：**自定义页框架**）。
///
/// <para>带 <see cref="Fields"/> 的页面 = 编辑器渲染成一张表单（栏位由插件声明），
/// 插件用 <see cref="Emit"/> 描述「把这些值变成什么代码」，编辑器负责：
/// 保存填的值（<c>plugins\&lt;id&gt;\values.json</c>）、实时预览、生成工程时按
/// <see cref="Target"/> 写进工程（于是随 mod 一起打包）。页面**内容完全由插件决定**，
/// 编辑器只提供底盘 —— 这也是「插件不因编辑器升级失效」的同一条思路：只认数据 + 模板。</para>
/// </summary>
/// <summary>
/// 一个「产出」：写哪个文件（<see cref="Target"/>）+ 文件内容模板（<see cref="Emit"/>）。
///
/// <para>V0.2.357：<see cref="ForEach"/> 填了 list 字段的 key = **按行生成多个文件**，
/// 每个文件里都能用该行的子栏位（以及 {index} / {index0}）。不填 = 一个文件。</para>
/// </summary>
public sealed class PluginPageOutput
{
    /// <summary>要写的文件（相对工程根；可用 {id} / 页面标量字段 / 行内子栏位）。</summary>
    public string Target { get; set; } = "";

    /// <summary>文件内容模板（同老 <c>page.emit</c> 的写法：{字段}、{list}…{/list}、{#字段}…{/字段}）。</summary>
    public string Emit { get; set; } = "";

    /// <summary>非空 = 按这个 list 字段**逐行**生成一个文件（行内子栏位在这个 target / emit 里可用）。</summary>
    public string ForEach { get; set; } = "";

    [JsonIgnore] public bool IsRowOutput => !string.IsNullOrWhiteSpace(ForEach);
    [JsonIgnore] public bool Ok => !string.IsNullOrWhiteSpace(Target) && !string.IsNullOrWhiteSpace(Emit);
}
public sealed class PluginPageSpec
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>正文（Markdown 片段；没有 <see cref="Fields"/> 时就是一个纯说明页）。</summary>
    public string Markdown { get; set; } = "";

    /// <summary>
    /// 这一页要挂到哪个页面（V0.2.338 立字段 / V0.2.339 接上界面）：空 = 挂在「插件」页；
    /// 填 <c>card</c> / <c>relic</c> / <c>potion</c> / <c>power</c> / <c>forge</c> / <c>build</c> 之一
    /// = 作为那个生成器页签里的一个**扩展页**显示（见 <see cref="PluginRegistry.KnownTabs"/> /
    /// <see cref="PluginRegistry.ExtensionPagesOf"/>；不改生成器本来的页）。填错的值当没填（留在「插件」页）。
    /// </summary>
    public string Tab { get; set; } = "";

    /// <summary>
    /// 这一页是不是**生成器的新功能页**（V0.2.340）：<c>true</c> = 生成器顶部多一个页签
    /// （像「角色」「药水」那样的一级页），页签内容就是这一页（表单 / 说明页）。
    ///
    /// <para>位置固定在「构建 / 日志」**前面**；和 <see cref="Tab"/> 二选一 ——
    /// 两个都填时按这个算（「插件」页的详情里会写一行说明）。</para>
    /// <para>页里的值存哪、按 <see cref="Target"/> / <see cref="Emit"/> 生成文件，和别的页完全一样
    /// （生成工程时由 <c>PluginGen.WritePages</c> 写进工程，随 mod 编译打包）。</para>
    /// </summary>
    public bool NewTab { get; set; }

    /// <summary>这张页面上的栏位（空 = 纯说明页）。</summary>
    public List<PluginFieldSpec> Fields { get; set; } = new();

    /// <summary>
    /// 生成出来的文件放在工程里的相对路径（支持 <c>{id}</c> 和 <c>{字段key}</c> 占位符），
    /// 例如 <c>cs/Plugins/{id}/Events/{EventClass}.cs</c>。留空 = 这个页面不产出文件（只显示）。
    /// </summary>
    public string Target { get; set; } = "";

    /// <summary>
    /// V0.2.357（用户要求「一页多文件」）：一页可以产出**多个文件**。
    /// 空 = 走老的单个 <see cref="Target"/> + <see cref="Emit"/>（老插件一个字都不用改）；
    /// 非空 = 按这里的每一条各写一个文件（老的那两个字段就被忽略）。
    ///
    /// <para>其中 <see cref="PluginPageOutput.ForEach"/> 非空 = **按那个 list 字段逐行生成一个文件**
    /// （target / emit 里可以用该行的子栏位和 {index} / {index0}）—— 「招式表里每行一个类」就是它。</para>
    /// </summary>
    public List<PluginPageOutput> Outputs { get; set; } = new();

    /// <summary>把栏位值渲染成的文本（C# 等，原样写进 <see cref="Target"/>）。占位符 <c>{字段key}</c>。</summary>
    public string Emit { get; set; } = "";

    /// <summary>V0.2.349：这页自己的问题（list 栏位配置不对等）—— 有问题的页会被跳过，不影响同插件的其它页。</summary>
    [JsonIgnore] public List<string> Problems { get; set; } = new();

    [JsonIgnore] public bool IsForm => Fields.Count > 0 && Fields.Any(f => f is not null && !string.IsNullOrWhiteSpace(f.Key));
    // V0.2.357：有 outputs[] 的页也算「产出」（一页多文件 / 按行产出）
    [JsonIgnore] public bool Produces => IsForm && (Outputs.Any(o => o is not null && o.Ok)
        || (!string.IsNullOrWhiteSpace(Emit) && !string.IsNullOrWhiteSpace(Target)));
}

/// <summary>一个插件的清单（<c>plugins/&lt;目录&gt;/plugin.json</c>）。</summary>
public sealed class PluginSpec
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0";
    /// <summary>
    /// 清单版本。**默认空**（= 插件的 plugin.json 里没写这一项）—— 空会被当成 v1 兼容加载，
    /// 这样「老插件没写版本」和「写了 1」在迁移里都走同一条路，但提示能分清（V0.2.361）。
    /// </summary>
    public string ApiVersion { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>插件自述（Markdown）；编辑器「插件」页会显示。</summary>
    public string Readme { get; set; } = "";

    public List<PluginEffectSpec> Effects { get; set; } = new();
    public List<PluginTriggerSpec> Triggers { get; set; } = new();
    public List<PluginConditionSpec> Conditions { get; set; } = new();
    public List<PluginForgeModuleSpec> ForgeModules { get; set; } = new();
    public List<PluginPageSpec> Pages { get; set; } = new();

    /// <summary>
    /// V0.2.368：插件贡献的**公式变量**（「数值公式」里能用的名字）。
    ///
    /// <para>条目 = <c>{ "id": "DiscardAttackCount", "display": "弃牌堆里的攻击牌数",
    /// "csharp": "(decimal)({p}?.PlayerCombatState?.DiscardPile?.Cards.Count(c => c.Type == CardType.Attack) ?? 0)" }</c>。
    /// <c>{p}</c> = 玩家、<c>{c}</c> = 「自己」那个 Creature（和内置变量同一套占位符）。</para>
    ///
    /// <para>加载时会和内置的 86 条（V0.2.368 起另外多了三摞×五种牌型）合在一起，重名的跳过（内置优先）。</para>
    /// </summary>
    public List<PluginFormulaVarSpec> FormulaVariables { get; set; } = new();

    /// <summary>
    /// 要一起打包的 C# 文件（相对插件目录的路径，如 <c>cs/MyHelper.cs</c>）。
    /// 生成工程时原样拷到 <c>cs/Plugins/&lt;插件 id&gt;/</c> 下 —— 于是它和 mod 一起编译、一起打包。
    /// </summary>
    public List<string> CsFiles { get; set; } = new();

    /// <summary>要一起打包的其它资源（相对路径，拷到工程根的 <c>plugin_assets/&lt;插件 id&gt;/</c>）。</summary>
    public List<string> AssetFiles { get; set; } = new();

    /// <summary>
    /// V0.2.358：这个插件现在是**启用**还是**禁用**（禁用 = 不注册它的效果 / 触发 / 条件 / 页，也不随 mod 打包）。
    /// 开关存在 <c>plugins/_enabled.json</c>（不写进 plugin.json，改插件目录不会丢）。
    /// </summary>
    [JsonIgnore] public bool Enabled { get; set; } = true;

    /// <summary>
    /// V0.2.361：加载时的**非致命提示**（例：这个插件没写 apiVersion、已按 v1 兼容加载）。
    /// 和 <see cref="Problems"/> 的区别：Problems 会让插件**不生效**，Notes 只是告诉你发生了迁移，插件照常可用。
    /// </summary>
    [JsonIgnore] public List<string> Notes { get; } = new();

    /// <summary>运行时填的：清单文件所在目录（不参与序列化）。</summary>
    [JsonIgnore] public string Dir { get; set; } = "";

    /// <summary>运行时填的：加载时发现的问题（不参与序列化）。</summary>
    [JsonIgnore] public List<string> Problems { get; } = new();

    [JsonIgnore] public string Display => string.IsNullOrWhiteSpace(Version) ? Name : $"{Name} V{Version}";
}

/// <summary>
/// 插件注册表（V0.2.315）：扫描 <c>&lt;程序目录&gt;/plugins/*/plugin.json</c>，把插件里的
/// 效果种类 / 触发时机 / 条件 / 锻造模块 / 额外页汇总起来给界面和生成端用。
///
/// <para><b>为什么用「数据 + 模板」而不是 DLL</b>：DLL 会绑死编辑器的内部类型，编辑器一升级就加载失败
/// （需求②明确要求不能这样）。这里只认 JSON 清单里的**字符串**，编辑器内部改了实现也照样读得出来。</para>
///
/// <para><b>互相调用同类内容（需求①）</b>：插件里的 <c>Emit</c> 模板可以写任何本体的 API，
/// 也可以调用插件自己 <see cref="PluginSpec.CsFiles"/> 里带的辅助类；编辑器自带的那 65 种效果
/// 用的也是同一套本体 API，所以两边生成的代码可以混在同一个效果列表里。</para>
///
/// <para><b>随 mod 打包（需求③）</b>：<see cref="PluginSpec.CsFiles"/> 会被拷进生成工程
/// <c>cs/Plugins/&lt;id&gt;/</c>，<see cref="PluginSpec.AssetFiles"/> 拷进 <c>plugin_assets/&lt;id&gt;/</c>，
/// 都在 mod 的工程目录里，构建/打包时自然带上。</para>
/// </summary>
public static class PluginRegistry
{
    /// <summary>
    /// 当前的清单版本。插件写 <c>apiVersion</c>；**旧版本会自动迁移**（见 <see cref="Migrate"/>），
    /// 比它还新的版本会被拒绝并说明（那种插件的字段我们看不懂，硬加载只会静默出错）。
    /// </summary>
    public const string ApiVersion = "2";

    /// <summary>还能自动迁移上来的最低版本（比它更老 = 拒绝，字段差太多）。</summary>
    public const string MinApiVersion = "1";

    private static readonly List<PluginSpec> _plugins = new();
    private static readonly List<EffectKindOption> _effectKinds = new();
    private static readonly Dictionary<string, PluginEffectSpec> _effectsById = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>插件贡献的条件（V0.2.316）：id → 规格 + 下拉项。</summary>
    private static readonly List<ConditionOption> _conditionOptions = new();
    private static readonly Dictionary<string, PluginConditionSpec> _conditionsById = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>插件贡献的触发时机（V0.2.316）：id → 规格（含「挂哪个本体钩子」+ 额外守卫）。</summary>
    private static readonly Dictionary<string, PluginTriggerSpec> _triggersById = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>加载到的插件（按目录名排序，结果稳定）。</summary>
    public static IReadOnlyList<PluginSpec> Plugins => _plugins;

    /// <summary>插件贡献的效果种类（合并到界面的「效果种类」下拉里）。</summary>
    public static IReadOnlyList<EffectKindOption> EffectKinds => _effectKinds;

    /// <summary>这个种类是不是插件给的。</summary>
    public static bool IsPluginEffect(string? kind) =>
        kind is not null && _effectsById.ContainsKey(kind.Trim());

    /// <summary>插件贡献的效果规格（拿模板 / 字段开关用）；不是插件的返回 null。</summary>
    public static PluginEffectSpec? EffectOf(string? kind) =>
        kind is not null && _effectsById.TryGetValue(kind.Trim(), out var e) ? e : null;

    /// <summary>插件目录（相对程序目录）：<c>plugins</c>。</summary>
    public static string PluginsDirOf(string appDir) => Path.Combine(appDir, "plugins");

    /// <summary>上一次扫描的时间（界面显示用）。</summary>
    // ==================== 插件启用/禁用（V0.2.358）====================
    //
    // 存在 plugins/_enabled.json：{ "关掉的": ["插件id", …] } —— **只记关掉的**，所以新装的插件默认启用。
    // 不写进各自的 plugin.json：那份是插件作者的，我们改它会在重新安装 / 升级时被覆盖。

    private static string EnabledPathOf(string appDir) => Path.Combine(PluginsDirOf(appDir), "_enabled.json");
    private static readonly HashSet<string> _disabledIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>这个插件现在是启用的吗。</summary>
    public static bool IsEnabled(string? pluginId) => pluginId is not null && !_disabledIds.Contains(pluginId.Trim());

    /// <summary>只含**启用**的插件（生成端该用这个，不是 <see cref="Plugins"/>）。</summary>
    public static IEnumerable<PluginSpec> ActivePlugins => Plugins.Where(p => p.Enabled && p.Problems.Count == 0);

    /// <summary>切换某个插件的启用状态（只改内存；<see cref="SaveEnabled"/> 才落盘）。返回新的状态。</summary>
    public static bool SetEnabled(string pluginId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return enabled;
        if (enabled) _disabledIds.Remove(pluginId.Trim());
        else _disabledIds.Add(pluginId.Trim());
        return enabled;
    }

    /// <summary>把启用表写回 <c>plugins/_enabled.json</c>（失败返回错误文本，不抛）。</summary>
    public static string? SaveEnabled(string appDir)
    {
        try
        {
            Directory.CreateDirectory(PluginsDirOf(appDir));
            var json = JsonSerializer.Serialize(new { disabled = _disabledIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(EnabledPathOf(appDir), json, new System.Text.UTF8Encoding(false));
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    /// <summary>读启用表（读坏了当「全都启用」，不挡住编辑器）。</summary>
    private static void LoadEnabled(string appDir)
    {
        _disabledIds.Clear();
        try
        {
            string path = EnabledPathOf(appDir);
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("disabled", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (JsonElement e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                        _disabledIds.Add(e.GetString()!.Trim());
        }
        catch { /* 读坏了 = 全启用 */ }
    }

    // ==================== 值按「当前存档」隔离（V0.2.358）====================
    //
    // 老行为：plugins/<id>/values.json —— 所有存档共用一份，换角色还是同一套值（用户报的痛点）。
    // 新行为：plugins/<id>/profiles/<存档 key>/values.json；**老文件仍然会读**（第一次读到就当作这个存档的初值，
    // 不删老文件），所以升级后旧值不会丢。

    /// <summary>当前存档的 key（文件名去扩展名 + 去非法字符；没打开存档时是 "default"）。</summary>
    public static string CurrentProfileKey { get; private set; } = "default";

    /// <summary>界面载入 / 新建存档时调一次：之后插件页的值就按这个存档分目录存。</summary>
    public static void SetCurrentProfile(string? profilePath)
    {
        string key = "default";
        try
        {
            if (!string.IsNullOrWhiteSpace(profilePath))
            {
                string name = Path.GetFileNameWithoutExtension(profilePath.Trim());
                var sb = new System.Text.StringBuilder();
                foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c > 0x7F ? c : '_');
                key = sb.Length > 0 ? sb.ToString() : "default";
            }
        }
        catch { key = "default"; }
        if (string.Equals(key, CurrentProfileKey, StringComparison.OrdinalIgnoreCase)) return;
        CurrentProfileKey = key;
        _pageValues.Clear();     // 缓存是按 pluginId 存的 —— 换了存档必须整体清掉，否则读到上一个存档的值
        _pageRows.Clear();
    }
    public static string LastScan { get; private set; } = "";

    /// <summary>
    /// 扫描并重新加载所有插件（幂等）。返回加载到的插件数。
    ///
    /// <para>容错原则：**任何一个插件坏掉都不影响其它插件，更不影响编辑器本身** ——
    /// 坏的那个记进 <see cref="PluginSpec.Problems"/>、界面里标红，其余照常。</para>
    /// </summary>
    public static int Load(string appDir)
    {
        _plugins.Clear();
        _effectKinds.Clear();
        _effectsById.Clear();
        _conditionOptions.Clear();
        _conditionsById.Clear();
        _triggersById.Clear();
        LastScan = DateTime.Now.ToString("HH:mm:ss");

        LoadEnabled(appDir);   // V0.2.358：先读启用表（禁用表只记「关掉的」）
        string dir = PluginsDirOf(appDir);
        if (!Directory.Exists(dir)) return 0;

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifests = new List<string>();
        try
        {
            // ① 每个插件一个子目录：plugins/<名字>/plugin.json
            foreach (string sub in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string m = Path.Combine(sub, "plugin.json");
                if (File.Exists(m)) manifests.Add(m);
            }
            // ② 也允许直接摊在 plugins/ 下：plugins/<名字>.json
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                manifests.Add(file);
        }
        catch (Exception e)
        {
            return 0;   // 目录都读不了：当作没有插件（不抛，编辑器照常开）
        }

        foreach (string path in manifests)
        {
            PluginSpec? spec = null;
            try
            {
                string json = File.ReadAllText(path);
                spec = JsonSerializer.Deserialize<PluginSpec>(json, JsonOpts);
            }
            catch (Exception e)
            {
                spec = new PluginSpec { Id = Path.GetFileNameWithoutExtension(path), Name = Path.GetFileNameWithoutExtension(path) };
                spec.Problems.Add("plugin.json 解析失败：" + e.Message);
            }
            if (spec is null) continue;
            spec.Dir = Path.GetDirectoryName(path) ?? dir;
            if (string.IsNullOrWhiteSpace(spec.Id)) spec.Id = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(spec.Name)) spec.Name = spec.Id;

            // V0.2.361：版本不再「不等于当前就拒绝」——
            //   · 没写 / 写旧版本（>= MinApiVersion）→ **自动迁移**并记一条 notes（插件照常可用）；
            //   · 写得比当前还新 → 拒绝并说明（那些新字段我们看不懂，硬加载只会静默出错）。
            if (!TryMigrateApiVersion(spec))
                spec.Problems.Add($"清单版本 apiVersion = 「{spec.ApiVersion}」比本编辑器支持的「{ApiVersion}」还新 —— 这个插件不会被加载（请升级编辑器）。");
            if (!seenIds.Add(spec.Id)) spec.Problems.Add($"插件 id「{spec.Id}」和前面某个插件重复 —— 这个插件不会被加载。");

            if (spec.Problems.Count > 0) { _plugins.Add(spec); continue; }
            spec.Enabled = IsEnabled(spec.Id);
            // V0.2.358：禁用的插件**不注册**效果 / 触发 / 条件（生成端等于没有它），但界面里仍然列出来（可以再打开）
            if (spec.Enabled) LoadOne(spec);
            ValidatePages(spec);
            _plugins.Add(spec);
        }
        return _plugins.Count(p => p.Problems.Count == 0);
    }

    /// <summary>
    /// V0.2.349（框架）：页面里 <c>list</c> 栏位的基本检查。**只检查有 list 栏位的页**，老插件一条都不会动。
    /// 问题记到 <see cref="PluginPageSpec.Problems"/>：这页会被跳过（不生成半截文件），同插件的其它页照常。
    /// </summary>
    public static void ValidatePages(PluginSpec spec)
    {
        foreach (PluginPageSpec page in spec.Pages)
        {
            if (page is null) continue;
            string where = "页「" + (string.IsNullOrWhiteSpace(page.Title) ? page.Id : page.Title) + "」";
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PluginFieldSpec f in page.Fields)
            {
                if (f is null || string.IsNullOrWhiteSpace(f.Key)) continue;
                if (!keys.Add(f.Key.Trim())) page.Problems.Add($"{where}：栏位 key「{f.Key}」重复。");
            }
            foreach (PluginFieldSpec f in page.Fields.Where(x => x is not null && x.IsList))
            {
                string key = (f.Key ?? "").Trim();
                int subs = 0;
                foreach (PluginFieldSpec sub in f.Fields)
                {
                    if (sub is null || string.IsNullOrWhiteSpace(sub.Key)) continue;
                    subs++;
                    if (sub.IsList) page.Problems.Add($"{where}：list 栏位「{key}」的子栏位「{sub.Key}」不能再是 list（嵌套列表 v1 不支持）。");
                }
                if (subs == 0) page.Problems.Add($"{where}：list 栏位「{key}」没写子栏位（fields）。");
                int open = CountOccurrences(page.Emit, "{" + key + "}");
                int close = CountOccurrences(page.Emit, "{/" + key + "}");
                if (open != close)
                    page.Problems.Add($"{where}：emit 里「{{{key}}}」块没配对（{open} 个 {{{key}}} / {close} 个 {{/{key}}}）。");
                if (!string.IsNullOrWhiteSpace(page.Target)
                    && page.Target.Contains("{" + key + "}", StringComparison.OrdinalIgnoreCase))
                    page.Problems.Add($"{where}：target 里不能用 list 栏位「{key}」（文件名没法按行展开）。");
                }
            foreach (PluginFieldSpec f in page.Fields.Where(x => x is not null && x.IsRef))
            {
                // V0.2.356：引用型栏位必须写对 refKind，否则下拉永远是空的（那是最难查的一种「没反应」）
                if (!RefKinds.Contains((f.RefKind ?? "").Trim().ToLowerInvariant()))
                    page.Problems.Add($"{where}：ref 栏位「{(f.Key ?? "").Trim()}」的 refKind「{f.RefKind}」不合法（只能是 {string.Join(" / ", RefKinds)}）。");
            }
            // V0.2.357：一页多文件（outputs[]）的检查 —— 每条都要有 target + emit；forEach 必须是存在的 list 字段
            foreach (PluginPageOutput o in page.Outputs)
            {
                if (o is null) continue;
                if (!o.Ok) page.Problems.Add($"{where}：outputs 里有一条没写 target 或 emit（会被跳过）。");
                if (!o.IsRowOutput) continue;
                string want = (o.ForEach ?? "").Trim();
                bool found = page.Fields.Any(x => x is not null && x.IsList
                    && string.Equals((x.Key ?? "").Trim(), want, StringComparison.OrdinalIgnoreCase));
                if (!found) page.Problems.Add($"{where}：outputs 里 forEach = 「{want}」不是这一页的 list 字段。");
            }
        }
    }

    private static int CountOccurrences(string? text, string what)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(what)) return 0;
        int n = 0;
        int at = 0;
        while (true)
        {
            at = text.IndexOf(what, at, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return n;
            n++;
            at += what.Length;
        }
    }
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>把一个（已通过校验的）插件的内容登记进注册表。</summary>
    /// <summary>
    /// V0.2.361：把插件的 <c>apiVersion</c> 迁移到当前版本。返回 false = 版本比当前**还新**，不能加载。
    ///
    /// <para>规则：**没写 = 当 1**（老插件）；<c>MinApiVersion..ApiVersion</c> 之间逐级往上迁；比当前新的拒绝。
    /// 迁移动作记进 <see cref="PluginSpec.Notes"/>，**不改用户磁盘上的 plugin.json**（作者那句版本号保持原样）。</para>
    ///
    /// <para>以后加新版本：把 <see cref="ApiVersion"/> 加一，在这里加一段 <c>case</c> 做字段改名 / 补默认值，
    /// 老插件就永远不用作者手动改。</para>
    /// </summary>
    private static bool TryMigrateApiVersion(PluginSpec spec)
    {
        string raw = (spec.ApiVersion ?? "").Trim();
        if (raw.Length == 0)
        {
            spec.Notes.Add($"没写 apiVersion —— 按 v{MinApiVersion} 兼容加载（建议补上 apiVersion: \"{ApiVersion}\"）。");
            raw = MinApiVersion;
        }
        if (!int.TryParse(raw, out int ver) || ver < 1)
        {
            spec.Notes.Add($"apiVersion「{raw}」看不懂 —— 按 v{MinApiVersion} 兼容加载。");
            ver = int.Parse(MinApiVersion);
        }
        int current = int.Parse(ApiVersion);
        if (ver > current) return false;       // 比我们还新：拒绝
        if (ver < int.Parse(MinApiVersion))
        {
            spec.Notes.Add($"apiVersion = {ver} 太老（最低支持 {MinApiVersion}）—— 已按 v{MinApiVersion} 加载，个别字段可能不生效。");
            ver = int.Parse(MinApiVersion);
        }
        while (ver < current)
        {
            switch (ver)
            {
                case 1:
                    // v1 → v2：**这一档没有结构变化**（v2 只多了可选字段：effects[].var / text / hoverTip、
                    // pages[].outputs、fields[].type = ref / list）。旧插件不写这些字段时行为与 v1 完全一致。
                    // 留这个 case 是给下一档做样板：字段改名 / 补默认值都写在这里。
                    spec.Notes.Add("已从 apiVersion 1 迁移到 2（v2 只多了可选字段，老写法行为不变）。");
                    break;
            }
            ver++;
        }
        return true;
    }

    private static void LoadOne(PluginSpec spec)
    {
        foreach (var fx in spec.Effects)
        {
            if (fx is null) continue;
            string kind = (fx.Kind ?? "").Trim();
            if (kind.Length == 0) { spec.Problems.Add("有一条效果没填 kind。"); continue; }
            if (!IsValidKind(kind)) { spec.Problems.Add($"效果 kind「{kind}」不合法（英文/数字/下划线，且以字母开头）。"); continue; }
            if (EffectCatalog.EffectKinds.Any(k => string.Equals(k.Kind, kind, StringComparison.OrdinalIgnoreCase))
                || _effectsById.ContainsKey(kind))
            {
                spec.Problems.Add($"效果 kind「{kind}」和已有的种类重名 —— 跳过这一条。");
                continue;
            }
            if (string.IsNullOrWhiteSpace(fx.Emit)) { spec.Problems.Add($"效果「{kind}」没写 emit 模板。"); continue; }
            // V0.2.360：动态变量名必须是合法标识符，且不能占 X / Y（那两个是本体的「本回合 X / 额外资源量」）
            if (!string.IsNullOrWhiteSpace(fx.Var))
            {
                string vn = fx.Var.Trim();
                if (!IsValidKind(vn)) spec.Problems.Add($"效果「{kind}」的 var「{vn}」不合法（英文/数字/下划线，且以字母开头）。");
                else if (vn is "X" or "Y") spec.Problems.Add($"效果「{kind}」的 var 不能用「{vn}」—— 那是本体的保留名。");
            }
            // V0.2.359：效果的 text 只认 {amount} —— 写了别的占位符会**原样印在卡面上**，必须报出来
            if (!string.IsNullOrWhiteSpace(fx.Text))
            {
                foreach (System.Text.RegularExpressions.Match mm in System.Text.RegularExpressions.Regex.Matches(fx.Text, @"\{[A-Za-z][A-Za-z0-9_]*(:[^}]*)?\}"))
                    if (!string.Equals(mm.Value, "{amount}", StringComparison.OrdinalIgnoreCase))
                        spec.Problems.Add($"效果「{kind}」的 text 里有个不认识的占位符「{mm.Value}」—— 卡面上只有 {{amount}} 会被替换，其它会原样印出来。");
            }
            _effectsById[kind] = fx;
            _effectKinds.Add(new EffectKindOption(
                Kind: kind,
                Display: string.IsNullOrWhiteSpace(fx.Display) ? kind : fx.Display.Trim(),
                Unit: string.IsNullOrWhiteSpace(fx.Unit) ? "点" : fx.Unit,
                Min: fx.Min, Max: fx.Max, NeedsTarget: fx.NeedsTarget));
        }

        // 触发时机 / 条件 / 锻造模块：本版**只登记 + 显示**（生成端接线下一版做）。
        // 这里只做重名检查，避免插件之间互相踩。
        foreach (var tr in spec.Triggers.Where(x => x is not null))
        {
            if (string.IsNullOrWhiteSpace(tr.Id)) { spec.Problems.Add("有一条触发时机没填 id。"); continue; }
            if (!IsValidKind(tr.Id)) { spec.Problems.Add($"触发时机 id「{tr.Id}」不合法。"); continue; }
            string hook = (tr.Hook ?? "").Trim();
            if (hook.Length == 0) { spec.Problems.Add($"触发时机「{tr.Id}」没填 hook（要挂在本体哪个钩子上）。"); continue; }
            // V0.2.316：触发时机 = 「给本体已有的某个钩子换个名字 + 加一句额外守卫」——
            // V0.2.362：写了 signature = **自定义触发时机**（插件自带完整 override 签名）——
            // 这时 hook 只当方法名用，不再要求是编辑器认识的钩子 id（否则等于又把插件锁死在编辑器那张表里）。
            if (!string.IsNullOrWhiteSpace(tr.Signature))
            {
                string sig = tr.Signature.Trim();
                if (!IsValidKind(hook))
                {
                    spec.Problems.Add($"触发时机「{tr.Id}」的 hook「{hook}」不合法（自定义触发时机时它只当方法名用，要英文/数字/下划线）。");
                    continue;
                }
                if (!sig.Contains(hook + "(", StringComparison.Ordinal))
                {
                    spec.Problems.Add($"触发时机「{tr.Id}」的 signature 里找不到方法名「{hook}(」—— 签名写错的话生成出来会编译不过。");
                    continue;
                }
                if (_triggersById.ContainsKey(tr.Id.Trim()))
                {
                    spec.Problems.Add($"触发时机 id「{tr.Id}」和别的插件重名 —— 跳过这一条。");
                    continue;
                }
                tr.Hook = hook;
                _triggersById[tr.Id.Trim()] = tr;
                continue;
            }
            // 这样插件不必改编辑器的钩子生成器，也不需要新的 override 方法（也就不会因编辑器升级失效）。
            if (EffectCatalog.RelicTriggers.All(x => !string.Equals(x.Id, hook, StringComparison.OrdinalIgnoreCase))
                && PowerTriggers.All.All(x => !string.Equals(x.Kind, hook, StringComparison.OrdinalIgnoreCase)))
            {
                spec.Problems.Add($"触发时机「{tr.Id}」的 hook「{hook}」不是编辑器已知的钩子 —— 跳过这一条。");
                continue;
            }
            if (_triggersById.ContainsKey(tr.Id.Trim()))
            {
                spec.Problems.Add($"触发时机 id「{tr.Id}」和别的插件重名 —— 跳过这一条。");
                continue;
            }
            _triggersById[tr.Id.Trim()] = tr;
        }
        foreach (var c in spec.Conditions.Where(x => x is not null))
        {
            if (string.IsNullOrWhiteSpace(c.Id)) { spec.Problems.Add("有一条条件没填 id。"); continue; }
            if (!IsValidKind(c.Id)) { spec.Problems.Add($"条件 id「{c.Id}」不合法。"); continue; }
            if (string.IsNullOrWhiteSpace(c.Expr)) { spec.Problems.Add($"条件「{c.Id}」没写 expr 表达式。"); continue; }
            if (EffectCatalog.Conditions.Any(x => string.Equals(x.Id, c.Id, StringComparison.OrdinalIgnoreCase))
                || _conditionsById.ContainsKey(c.Id))
            {
                spec.Problems.Add($"条件 id「{c.Id}」和已有的条件重名 —— 跳过这一条。");
                continue;
            }
            // V0.2.316：条件现在**真的接进生成端了**（四链通用；插件条件对卡牌/遗物/状态都开放）。
            _conditionsById[c.Id.Trim()] = c;
            _conditionOptions.Add(new ConditionOption(
                Id: c.Id.Trim(),
                Display: string.IsNullOrWhiteSpace(c.Display) ? c.Id.Trim() : c.Display.Trim(),
                ForCard: true, ForRelic: true, NeedsAmount: false, NeedsPower: false,
                Hint: string.IsNullOrWhiteSpace(c.Note) ? "插件条件：" + c.Expr.Trim() : c.Note.Trim(),
                ForPower: true));
        }
        foreach (var m in spec.ForgeModules.Where(x => x is not null))
        {
            if (string.IsNullOrWhiteSpace(m.Kind)) { spec.Problems.Add("有一个锻造模块没填 kind。"); continue; }
            if (!IsValidKind(m.Kind)) { spec.Problems.Add($"锻造模块 kind「{m.Kind}」不合法。"); continue; }
            if (string.IsNullOrWhiteSpace(m.Emit)) { spec.Problems.Add($"锻造模块「{m.Kind}」没写 emit 模板。"); continue; }
            // V0.2.368：图上额外栏位（key 必须合法且不能和【效果】内置那几栏重名，type 只能是那五种）
            foreach (var nf in m.Fields.Where(x => x is not null))
            {
                string key = (nf.Key ?? "").Trim();
                if (!IsValidKind(key)) { spec.Problems.Add($"锻造模块「{m.Kind}」的字段「{key}」不合法（英文/数字/下划线，字母开头）。"); continue; }
                if (Profile.ForgeGraphCatalog.EffectFieldKeys.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase))) { spec.Problems.Add($"锻造模块「{m.Kind}」的字段「{key}」和【效果】模块内置的栏位重名 —— 换一个名字。"); continue; }
                string ty = (nf.Type ?? "text").Trim().ToLowerInvariant();
                if (ty is not ("text" or "number" or "formula" or "pick" or "check"))
                    spec.Problems.Add($"锻造模块「{m.Kind}」的字段「{key}」类型「{nf.Type}」不认识（只能是 text / number / formula / pick / check）。");
            }
            // V0.2.316：锻造模块 = 「图上的【效果】节点 + 一段模板」，所以直接当效果种类登记 ——
            // 于是它会出现在卡牌页的效果下拉、也会出现在锻造图【效果】节点的候选里（图上就能用）。
            if (EffectCatalog.EffectKinds.Any(k => string.Equals(k.Kind, m.Kind, StringComparison.OrdinalIgnoreCase))
                || _effectsById.ContainsKey(m.Kind))
            {
                spec.Problems.Add($"锻造模块 kind「{m.Kind}」和已有的种类重名 —— 跳过这一条。");
                continue;
            }
            var asEffect = new PluginEffectSpec
            {
                Kind = m.Kind, Display = string.IsNullOrWhiteSpace(m.Display) ? m.Kind : m.Display,
                Unit = "点", Min = 0, Max = 99, Emit = m.Emit, Note = m.Note,
            };
            _effectsById[m.Kind.Trim()] = asEffect;
            _effectKinds.Add(new EffectKindOption(asEffect.Kind, asEffect.Display, asEffect.Unit, 0, 99, false));
        }

        // V0.2.368：插件贡献的公式变量（id 必须合法、不能和内置重名；只认 {p} / {c} 两个占位符）
        foreach (var v in spec.FormulaVariables.Where(x => x is not null))
        {
            string vid = (v.Id ?? "").Trim();
            if (!IsValidKind(vid)) { spec.Problems.Add($"公式变量「{vid}」不合法（英文/数字/下划线，字母开头）。"); continue; }
            if (Generation.FormulaBuiltins.All.Any(c => string.Equals(c.Id, vid, StringComparison.OrdinalIgnoreCase)))
            {
                spec.Problems.Add($"公式变量「{vid}」和已有的变量重名 —— 跳过这一条。");
                continue;
            }
            if (string.IsNullOrWhiteSpace(v.CSharp))
            {
                spec.Problems.Add($"公式变量「{vid}」没写 csharp 表达式。");
                continue;
            }
            foreach (System.Text.RegularExpressions.Match mm in System.Text.RegularExpressions.Regex.Matches(v.CSharp, @"\{[A-Za-z][A-Za-z0-9_]*\}"))
            {
                string ph = mm.Value;
                if (!string.Equals(ph, "{p}", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ph, "{c}", StringComparison.OrdinalIgnoreCase))
                    spec.Problems.Add($"公式变量「{vid}」里有个不认识的占位符「{ph}」—— 只认 {{p}}（玩家）和 {{c}}（自己这个 Creature）。");
            }
        }

        // 自带文件必须在插件目录里、且真的存在（越界路径直接拒绝，避免手滑删到别处）
        foreach (var f in spec.CsFiles.Concat(spec.AssetFiles).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string full = Path.GetFullPath(Path.Combine(spec.Dir, f.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Path.GetFullPath(spec.Dir), StringComparison.OrdinalIgnoreCase))
                spec.Problems.Add($"自带文件「{f}」跑到插件目录外面了 —— 不允许。");
            else if (!File.Exists(full))
                spec.Problems.Add($"自带文件「{f}」不存在。");
        }
    }

    private static bool IsValidKind(string kind)
    {
        if (kind.Length == 0 || !char.IsLetter(kind[0])) return false;
        return kind.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    // ==================== 自定义页面的「填的值」（V0.2.319）====================

    /// <summary>每个插件自己那份 <c>values.json</c> 的缓存（key = 插件 id）。</summary>
    private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _pageValues =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>老位置（升级前所有存档共用一份）。只读，用作第一次的初值。</summary>
    private static string LegacyValuesPathOf(PluginSpec p) => Path.Combine(p.Dir, "values.json");

    /// <summary>V0.2.358：按**当前存档**分目录存（<c>plugins/&lt;id&gt;/profiles/&lt;存档 key&gt;/values.json</c>）。</summary>
    private static string ValuesPathOf(PluginSpec p) =>
        Path.Combine(p.Dir, "profiles", CurrentProfileKey, "values.json");

    /// <summary>读这个插件的页面值（没有就读磁盘上的 values.json；读坏了当空）。</summary>
    private static Dictionary<string, Dictionary<string, string>> ValuesOf(PluginSpec p)
    {
        if (_pageValues.TryGetValue(p.Id, out var cached)) return cached;
        var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string path = ValuesPathOf(p);
            if (!File.Exists(path)) path = LegacyValuesPathOf(p);   // V0.2.358：老文件当这个存档的初值
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(path), JsonOpts);
                if (loaded is not null)
                    foreach (var kv in loaded)
                        if (kv.Value is not null) map[kv.Key] = new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { /* 读坏了就用默认值，不挡住编辑器 */ }
        _pageValues[p.Id] = map;
        return map;
    }

    /// <summary>这一页某个栏位当前的值（没填过 = 清单里的默认值）。</summary>
    public static string PageValueOf(PluginSpec p, PluginPageSpec page, PluginFieldSpec field)
    {
        var values = ValuesOf(p);
        if (values.TryGetValue(page.Id, out var pageMap) && pageMap.TryGetValue(field.Key, out string? v) && v is not null) return v;
        return field.Default ?? "";
    }

    /// <summary>改一页某个栏位的值（只改内存；<see cref="SavePageValues"/> 才落盘）。</summary>
    public static void SetPageValue(PluginSpec p, PluginPageSpec page, PluginFieldSpec field, string? value)
    {
        var values = ValuesOf(p);
        if (!values.TryGetValue(page.Id, out var pageMap))
        {
            pageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            values[page.Id] = pageMap;
        }
        pageMap[field.Key] = value ?? "";
    }

    /// <summary>把填的值写回 <c>plugins\&lt;id&gt;\values.json</c>（失败只记一条，不抛）。</summary>
    public static string? SavePageValues(PluginSpec p)
    {
        try
        {
            var values = ValuesOf(p);
            var plain = values.ToDictionary(kv => kv.Key, kv => kv.Value.ToDictionary(x => x.Key, x => x.Value));
            // V0.2.358：值现在按存档分目录存（profiles/<存档 key>/），目录可能还不存在 —— 先建
            Directory.CreateDirectory(Path.GetDirectoryName(ValuesPathOf(p))!);
            File.WriteAllText(ValuesPathOf(p), JsonSerializer.Serialize(plain, new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    // ==================== 动态列表的行数据（V0.2.349「能适配的框架」）====================
    //
    // 为什么要独立文件：列表是**多行多列**，塞不进 values.json 的 `页面id → {栏位: 字符串}` 那层；
    // 而改 values.json 的格式会让老版本编辑器读到新数据时**整份读坏**（JsonException 一抛就全没了）。
    // 所以单独存 plugins\<id>\rows.json：
    //     { 页面id: { 列表字段key: [ { 子key: 值, … }, … ] } }
    // 老版本编辑器不认识这个文件 = 看不到列表内容，但 values.json 一个字节都不受影响。

    private static readonly Dictionary<string, Dictionary<string, Dictionary<string, List<Dictionary<string, string>>>>> _pageRows =
        new(StringComparer.OrdinalIgnoreCase);

    private static string LegacyRowsPathOf(PluginSpec p) => Path.Combine(p.Dir, "rows.json");

    private static string RowsPathOf(PluginSpec p) =>
        Path.Combine(p.Dir, "profiles", CurrentProfileKey, "rows.json");

    private static Dictionary<string, Dictionary<string, List<Dictionary<string, string>>>> RowsOf(PluginSpec p)
    {
        if (_pageRows.TryGetValue(p.Id, out var cached)) return cached;
        var map = new Dictionary<string, Dictionary<string, List<Dictionary<string, string>>>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string path = RowsPathOf(p);
            if (!File.Exists(path)) path = LegacyRowsPathOf(p);   // V0.2.358：同上
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<Dictionary<string, string>>>>>(File.ReadAllText(path), JsonOpts);
                if (loaded is not null)
                    foreach (var kv in loaded)
                    {
                        if (kv.Value is null) continue;
                        var pageMap = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var fv in kv.Value)
                        {
                            if (fv.Value is null) continue;
                            var rows = new List<Dictionary<string, string>>();
                            foreach (var r in fv.Value)
                                if (r is not null) rows.Add(new Dictionary<string, string>(r, StringComparer.OrdinalIgnoreCase));
                            pageMap[fv.Key] = rows;
                        }
                        map[kv.Key] = pageMap;
                    }
            }
        }
        catch { /* 读坏了就当成空表，不挡住编辑器 */ }
        _pageRows[p.Id] = map;
        return map;
    }

    /// <summary>
    /// 这一页某个 <c>list</c> 栏位当前的行。**返回的是缓存里的可变引用** —— 界面改完调
    /// <see cref="SaveRows"/> 落盘。第一次访问且没有存过时，按 <see cref="PluginFieldSpec.DefaultRows"/>
    /// 生成若干「子栏位都是默认值」的空行。
    /// </summary>
    public static List<Dictionary<string, string>> PageRowsOf(PluginSpec p, PluginPageSpec page, PluginFieldSpec listField)
    {
        var rowsMap = RowsOf(p);
        if (!rowsMap.TryGetValue(page.Id, out var pageMap))
        {
            pageMap = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
            rowsMap[page.Id] = pageMap;
        }
        string key = (listField.Key ?? "").Trim();
        if (!pageMap.TryGetValue(key, out var rows))
        {
            rows = new List<Dictionary<string, string>>();
            int n = Math.Clamp(listField.DefaultRows, 0, 50);
            for (int i = 0; i < n; i++) rows.Add(NewRowOf(listField));
            pageMap[key] = rows;
        }
        return rows;
    }

    /// <summary>新行：每个子栏位都用它自己的默认值。</summary>
    public static Dictionary<string, string> NewRowOf(PluginFieldSpec listField)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in listField.Fields)
        {
            if (sub is null || string.IsNullOrWhiteSpace(sub.Key)) continue;
            row[sub.Key.Trim()] = sub.Default ?? "";
        }
        return row;
    }

    /// <summary>这一行某个子栏位的值（没填 = 子栏位默认值）。</summary>
    public static string RowValueOf(PluginFieldSpec listField, Dictionary<string, string> row, PluginFieldSpec sub)
    {
        string key = (sub.Key ?? "").Trim();
        if (row.TryGetValue(key, out string? v) && v is not null) return v;
        return sub.Default ?? "";
    }

    /// <summary>把行数据写回 <c>plugins\&lt;id&gt;\rows.json</c>（失败只记一条，不抛）。</summary>
    public static string? SaveRows(PluginSpec p)
    {
        try
        {
            var rowsMap = RowsOf(p);
            var plain = rowsMap.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToDictionary(
                    x => x.Key,
                    x => x.Value.Select(r => r.ToDictionary(y => y.Key, y => y.Value)).ToList()));
            Directory.CreateDirectory(Path.GetDirectoryName(RowsPathOf(p))!);   // V0.2.358：同上，先建目录
            File.WriteAllText(RowsPathOf(p), JsonSerializer.Serialize(plain, new JsonSerializerOptions { WriteIndented = true }),
                new System.Text.UTF8Encoding(false));
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
    // ==================== 引用型栏位：从**当前存档**里挑对象（V0.2.356）====================
    //
    // 为的是「插件页能真引用存档里的东西」：招式表要挑一张已有的卡、掉落表要挑一个状态、
    // 事件表要挑一只伙伴…… 以前只能让用户手抄类名（抄错就静默生成不出东西）。
    // 存的是**稳定标识**（类名 / 组 Id），和生成端用的是同一个，不是显示名。

    /// <summary><c>refKind</c> 认得的取值（写错会被报成页面问题，不会静默变成空下拉）。</summary>
    public static IReadOnlyList<string> RefKinds { get; } = new[] { "card", "power", "relic", "potion", "pet", "group" };

    /// <summary>引用型栏位的候选：<c>(Id, Display)</c>；<paramref name="profile"/> 为 null 时返回空表。</summary>
    public static List<(string Id, string Display)> RefOptions(string? refKind, Sts2CharForge.Core.Profile.CharacterProfile? profile)
    {
        var list = new List<(string, string)>();
        if (profile is null) return list;
        var n = Sts2CharForge.Core.Generation.Naming.From(profile);
        switch ((refKind ?? "").Trim().ToLowerInvariant())
        {
            case "card":
                foreach (var c in profile.AllCards)
                {
                    if (c is null) continue;
                    string cls = n.CardClassName(profile, c);
                    if (cls.Length == 0) continue;
                    list.Add((cls, $"{c.Name}（{cls}）"));
                }
                break;
            case "power":
                for (int i = 0; i < profile.CustomPowers.Count; i++)
                {
                    var cp = profile.CustomPowers[i];
                    if (cp is null) continue;
                    // 类名留空 = 生成端按序号自动起（<角色>PowerN）—— 下拉里必须**按同一个口径**显示，
                    // 否则自动命名的状态全都挑不到（那是「下拉是空的」最常见的原因）。
                    string cls = Sts2CharForge.Core.Generation.CustomPowerGen.ClassNameOf(profile, cp, i);
                    list.Add((cls, $"{cp.Name}（{cls}）"));
                }
                break;
            case "relic":
                for (int i = 0; i < profile.Relics.Count; i++)
                {
                    var r = profile.Relics[i];
                    if (r is null) continue;
                    string cls = n.RelicClassName(r, i);
                    list.Add((cls, $"{r.Name}（{cls}）"));
                }
                break;
            case "potion":
                for (int i = 0; i < profile.Potions.Count; i++)
                {
                    var x = profile.Potions[i];
                    if (x is null) continue;
                    string cls = n.PotionClassName(x, i);
                    list.Add((cls, $"{x.Name}（{cls}）"));
                }
                break;
            case "pet":
                for (int i = 0; i < profile.Summons.Count; i++)
                {
                    var sp = profile.Summons[i];
                    if (sp is null) continue;
                    string cls = string.IsNullOrWhiteSpace(sp.ClassName) ? $"#{i + 1}" : sp.ClassName!.Trim();
                    list.Add((cls, $"{sp.Name}（{cls}）"));
                }
                break;
            case "group":
                foreach (var g in profile.CardGroups)
                {
                    if (g is null || string.IsNullOrWhiteSpace(g.Id)) continue;
                    list.Add((g.Id.Trim(), $"{g.Name}（{g.Id.Trim()}）"));
                }
                break;
        }
        return list;
    }

    /// <summary>V0.2.368：某个插件锻造模块声明的图栏位（不是插件模块 / 没声明 = 空表）。</summary>
    public static IReadOnlyList<PluginNodeFieldSpec> NodeFieldsOf(string? kind)
    {
        string k = (kind ?? "").Trim();
        if (k.Length == 0) return Array.Empty<PluginNodeFieldSpec>();
        foreach (var pl in Plugins)
            foreach (var m in pl.ForgeModules)
                if (m is not null && string.Equals((m.Kind ?? "").Trim(), k, StringComparison.OrdinalIgnoreCase))
                    return m.Fields.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Key)).ToList();
        return Array.Empty<PluginNodeFieldSpec>();
    }
    /// <summary>【效果】模块**内置**那几个栏位的键（插件自造栏位不能和它们重名）。</summary>
    private static readonly HashSet<string> BuiltInEffectFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Effect", "PowerId", "Amount", "SpawnCardId", "SpawnTo", "PotionPick", "PotionId", "Pick",
        "SpawnFilter", "CostDownThisTurn", "SelectPile", "PlaceTo", "PlaceAt", "GivenKeyword", "TempKeyword",
        "BoostStat", "Sfx", "Note",
    };

    /// <summary>插件贡献的条件（下拉项，合并进 <see cref="EffectCatalog.AllConditions"/>）。</summary>
    public static IReadOnlyList<ConditionOption> ConditionOptions => _conditionOptions;

    /// <summary>这个条件 id 是不是插件给的。</summary>
    public static bool IsPluginCondition(string? id) =>
        id is not null && _conditionsById.ContainsKey(id.Trim());

    /// <summary>插件条件的规格（拿到 expr 模板用）；不是插件条件返回 null。</summary>
    public static PluginConditionSpec? ConditionOf(string? id) =>
        id is not null && _conditionsById.TryGetValue(id.Trim(), out var c) ? c : null;

    /// <summary>这个触发时机 id 是不是插件给的。</summary>
    public static bool IsPluginTrigger(string? id) =>
        id is not null && _triggersById.ContainsKey(id.Trim());

    /// <summary>插件贡献的触发时机规格；不是插件时机返回 null。</summary>
    public static PluginTriggerSpec? TriggerOf(string? id) =>
        id is not null && _triggersById.TryGetValue(id.Trim(), out var t) ? t : null;

    /// <summary>插件时机的清单（下拉合并用）。</summary>
    public static IReadOnlyDictionary<string, PluginTriggerSpec> Triggers => _triggersById;

    /// <summary>插件贡献的额外页（id / 标题 / 正文 + 所属插件名）。</summary>
    public static IEnumerable<(PluginSpec Plugin, PluginPageSpec Page)> AllPages =>
        _plugins.Where(p => p.Problems.Count == 0)
                .SelectMany(p => p.Pages.Where(x => x is not null).Select(x => (p, x)));

    // ==================== 扩展页挂到哪个生成器页签（V0.2.339）====================

    /// <summary>
    /// <c>pages[].tab</c> 认得的取值（V0.2.339）。空 = 挂在「插件」页；
    /// 这几个值 = 挂到对应的生成器页签当**扩展页**（卡牌 / 遗物 / 药水 / 自定义状态 / 锻造 / 构建·日志）。
    /// </summary>
    public static readonly IReadOnlyList<string> KnownTabs = new[] { "card", "relic", "potion", "power", "forge", "build" };

    /// <summary>这个 <c>tab</c> 值认不认识（空串也算认识 = 挂「插件」页；写错的返回 false，界面按「插件」页显示并说明）。</summary>
    public static bool IsKnownTab(string? tab)
    {
        string t = (tab ?? "").Trim();
        return t.Length == 0 || KnownTabs.Any(k => string.Equals(k, t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 某个生成器页签上的扩展页：<c>pages[].tab</c> 指向它的那些页（按清单顺序）。
    /// 挂载点在 <c>MainWindow.RebuildPluginExtensionPages</c>；写错 tab 值的页**不在这里**（它们留在「插件」页）。
    /// </summary>
    public static IEnumerable<(PluginSpec Plugin, PluginPageSpec Page)> ExtensionPagesOf(string tab)
    {
        string want = (tab ?? "").Trim();
        if (want.Length == 0) return Array.Empty<(PluginSpec, PluginPageSpec)>();
        return AllPages.Where(x => string.Equals((x.Page.Tab ?? "").Trim(), want, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 声明成「生成器新功能页」的页（V0.2.340，<c>pages[].newTab = true</c>）：每个都会变成 MainTabs 里
    /// 一个**顶层页签**（挂在「构建 / 日志」前面）。顺序 = 插件加载顺序 + 清单里的页顺序。
    /// 挂载点在 <c>MainWindow.RebuildPluginTopLevelTabs</c>。
    /// </summary>
    public static IEnumerable<(PluginSpec Plugin, PluginPageSpec Page)> NewTabPages =>
        AllPages.Where(x => x.Page.NewTab);
}
