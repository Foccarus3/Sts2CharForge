using System.Text;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 「自定义状态覆盖血条」的生成。
///
/// 需求原文（两轮）：「增加一个像中毒一样覆盖血条的显示，只需要调整 RRGGBB」+
/// 「每一个状态在血条上显示的 RRGGBB 应该分别独立，比如当身上有状态 A 和状态 B，
///   应该可以同时看到状态 A 和状态 B 在血条上的覆盖，并且可以选择是从血条左边开始覆盖还是右边开始覆盖」。
///
/// 做法：给本体的 <c>NHealthBar.RefreshForeground()</c> 挂一个 Postfix —— 本体自己算完
/// 红条 / 中毒那截 / 灾厄那截之后，我们**每条状态各用一个自己的节点**摆上去。
///
/// 为什么每条状态要各自一个节点：本体场景里血条上只有 **一截** 绿色（`%PoisonForeground`），
/// 直接改它的颜色的话「两条状态同时在场」只能显示一条（谁后写谁赢）。
/// 所以这里拿那一截当**模板** `Duplicate()` 出 N 份（每个状态一份、按类名命名、挂在同一个 Mask 里），
/// 每条各染各的色、各占各的位置 —— 于是 A 和 B 能同时看见。
///
/// 为什么颜色可以随便填（只要 RRGGBB）：本体血条上那几截用的是**同一张纯白九宫格**
/// （<c>images/ui/combat/health_bar_fill.png</c>），颜色全部来自节点自己的 <c>self_modulate</c>
/// （红条 CC0C0C / 中毒 79C03C / 灾厄是着色器渐变）—— 所以换色本来就是一行的事。
///
/// 几何（两端都对应本体的画法，所以观感和本体一致）：
///   · 从**右边**开始（默认，和中毒一样）：从「当前血量的右端」往左量 N（= 层数）点血的宽度，
///     红条右端跟着往左退；
///   · 从**左边**开始（和灾厄占的那一端一样）：从血条最左端往右量同样宽度，红条左端跟着往右退；
///   · 同一边有几条就**依次往里堆**（互不重叠），中间剩下那一段就是红条；
///   · 两边加起来超过当前血量时，后面放不下的那几条**不画**（宁可少画，也不叠在一起糊成一片）。
///
/// 为什么用 **Postfix**：本体那两截也在这一个方法里摆，跑在它们后面就能接在它们内侧；
/// 而且本体每次刷新都会**从头重算**红条的显隐和偏移，所以我们改坏的部分下一帧就自愈。
///
/// 为什么补丁要**手动打**（不在这个文件里挂 [HarmonyPatch] 特性）：
/// <c>RefreshForeground</c> 是**私有**方法，本体一旦改了名字，带特性的 PatchAll 会抛异常、
/// 整个模组都加载不了（项目里其它打在私有方法上的补丁，见 ModEntry 里那种 try + AccessTools 的写法）。
/// </summary>
public static class HpBarOverlayGen
{
    /// <summary>生成出来的文件名 / 类名。</summary>
    public const string File = "ForgeHpBarOverlay";

    public const string ClassName = "ForgeHpBarOverlay";

    /// <summary>配了「覆盖血条」而且真的会生成出来的那些自定义状态（按界面顺序 = 覆盖条的排列顺序）。</summary>
    public static List<(CustomPowerSpec Spec, int Index)> Entries(CharacterProfile p)
    {
        var list = new List<(CustomPowerSpec, int)>();
        for (int i = 0; i < p.CustomPowers.Count; i++)
        {
            var cp = p.CustomPowers[i];
            if (cp is null || !cp.HasHpBarOverlay) continue;
            if (!CustomPowerGen.IsActive(cp)) continue;      // 没名字 / 停用的状态根本不会生成，引用不到类
            if (NormalizeHex(cp.HpBarColor).Length == 0) continue;   // 颜色写坏了：宁可不上色（校验器会报错拦住）
            list.Add((cp, i));
        }
        return list;
    }

    /// <summary>要不要写这个文件（默认档一个字符都不多）。</summary>
    public static bool Needs(CharacterProfile p) => Entries(p).Count > 0;

    /// <summary>把颜色统一成 6 位大写十六进制（校验器已经保证合法；这里再兜一层，写坏了返回空串 = 不上色）。</summary>
    public static string NormalizeHex(string? hex)
    {
        string s = (hex ?? "").Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6 || !CardColorSpec.IsHex(s)) return "";
        return s.ToUpperInvariant();
    }

    /// <summary>
    /// 「这个存档带了几条覆盖血条」的一句话（模组加载时打进日志）：
    /// 用户报「覆盖血条没显示」时，先看这一行就知道模组里到底有没有这个功能、
    /// 带的是哪几条状态、什么颜色、从哪一端 —— 免得又出现「日志里一个字都没有」的情况。
    /// </summary>
    public static string LoadSummary(CharacterProfile p)
    {
        var entries = Entries(p);
        string list = string.Join("、", entries.Select(e =>
            (string.IsNullOrWhiteSpace(e.Spec.Name) ? CustomPowerGen.ClassNameOf(p, e.Spec, e.Index) : e.Spec.Name.Trim())
            + $" #{NormalizeHex(e.Spec.HpBarColor)}"
            + (HpBarSides.Normalize(e.Spec.HpBarSide) == HpBarSides.Left ? " 从左" : " 从右")));
        return $"[覆盖血条] 本模组带了 {entries.Count} 条覆盖血条：{list}";
    }

    /// <summary>生成 cs/ForgeHpBarOverlay.cs。</summary>
    public static string Source(CharacterProfile p)
    {
        string ns = Naming.From(p).Namespace;

        // 每条状态一段：有那个状态、层数 > 0 就摆自己那一条（各自一个节点、各自的颜色 / 哪一端）
        var checks = new StringBuilder();
        var entries = Entries(p);
        var names = new StringBuilder();     // 日志里用的中文名（按同一次序）
        for (int i = 0; i < entries.Count; i++)
        {
            string cls = CustomPowerGen.ClassNameOf(p, entries[i].Spec, entries[i].Index);
            string hex = NormalizeHex(entries[i].Spec.HpBarColor);
            string name = (entries[i].Spec.Name ?? "").Trim();
            bool left = HpBarSides.Normalize(entries[i].Spec.HpBarSide) == HpBarSides.Left;

            if (i > 0) names.Append(", ");
            names.Append(Lit.Str(name.Length > 0 ? name : cls));

            checks.Append("            if (creature.GetPower<").Append(cls).Append(">() is { } __hp").Append(i)
                  .AppendLine(" && __hp" + i + ".Amount > 0m)");
            checks.Append("                anyDrawn |= Place(mask, template, parentWidth, maxFgWidth, creature.MaxHp, ").Append(Lit.Str(cls))
                  .Append(", new Color(").Append(Lit.Str(hex)).Append("), left: ").Append(left ? "true" : "false")
                  .Append(", amount: __hp").Append(i).Append(".Amount, index: ").Append(i).AppendLine(",");
            checks.AppendLine("                      ref leftEnd, ref rightEnd, ref anyLeft);");
            checks.Append("            // ").Append(name).Append("　").Append(hex).Append("　")
                  .AppendLine(left ? "从左边开始" : "从右边开始");
        }

        return $$"""
// <auto-generated>由 Sts2CharForge 生成：自定义状态的「覆盖血条」。</auto-generated>
namespace {{ns}};

/// <summary>
/// 「自定义状态覆盖血条」：身上有那些状态的生物，血条上会出现对应颜色的覆盖条，
/// **每条状态各一条、颜色各自独立**（A 和 B 同时在场时两条都能看见），
/// 并且每条可以选**从左边还是从右边**开始盖。
///
/// 几何（两端都对应本体的画法）：
///   · 从右边开始（默认，像中毒）：从「当前血量的右端」往左量「层数」那么多点血的宽度，红条右端跟着往左退；
///   · 从左边开始（和灾厄占的那一端一样）：从血条最左端往右量同样宽度，红条左端跟着往右退；
///   · 同一边有几条就**依次往里堆**（互不重叠），中间剩下那一段就是红条；
///   · 两边加起来超过当前血量时，后面放不下的那几条不画（宁可少画，也不糊成一片）。
///
/// 为什么每条状态要有自己的节点：本体血条上只有**一截**绿色（<c>%PoisonForeground</c>），
/// 改它的颜色的话「两条状态同时在场」只能显示一条（谁后写谁赢）。
/// 所以这里拿那一截当**模板** <c>Duplicate()</c> 出 N 份（每个状态一份、按类名命名、挂在同一个 Mask 里），
/// 每条各染各的色、各占各的位置 —— 于是 A 和 B 能同时看见。
///
/// 为什么颜色能随便填：那截条用的贴图 <c>images/ui/combat/health_bar_fill.png</c> 是**纯白**九宫格，
/// 颜色全部来自节点自己的 <c>self_modulate</c>（红条 / 中毒 / 灾厄都是同一张图上的不同颜色）。
///
/// 补丁是**手动**打上去的（见 ModEntry）：<c>RefreshForeground</c> 是私有方法，
/// 本体改了名字时带特性的 PatchAll 会抛异常、整个模组加载不了，所以单独 try 一次。
/// 两个入口（RefreshForeground 与它的调用方 RefreshValues）都打：画是幂等的（先藏再摆），
/// 少接一个入口的表现就是「状态挂上了但血条上看不到」，所以宁可多接一个。
///
/// ⚠ V0.2.185：以前这里的每一处「拿不到就 return」都是**静默**的 —— 用户实测「覆盖血条没显示出来」
/// 时日志里一个字都没有，根本没法查。现在每条退出理由都有**一次性**日志（不刷屏），
/// 并且第一次真的画上去也会记一条（含层数 / 算出来的那一段 / 血条宽度）。
/// 顺带：太窄的覆盖条会看不见（九宫格左右边距各 6px，宽度小于 12px 基本就没了），
/// 所以宽度跟本体 <c>GetFgWidth</c> 一样有 **12px 下限** —— 层数少也看得见。
/// </summary>
public static class {{ClassName}}
{
    /// <summary>我们造出来的那些覆盖条节点名的前缀（隐藏 / 查找都靠它）。</summary>
    private const string NodePrefix = "ForgeHpBarOverlay_";

    /// <summary>每条状态的中文名（日志里用，次序和下面那串 if 一致）。</summary>
    private static readonly string[] Names = { {{names}} };

    /// <summary>要接进去的那个方法；本体改了名字就返回 null（调用处会记一条警告，模组照常加载）。</summary>
    public static System.Reflection.MethodInfo? Target =>
        HarmonyLib.AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar), "RefreshForeground");

    /// <summary>
    /// 第二个入口：<c>RefreshValues</c>（public，本体在「血量 / 格挡 / 战斗状态变了」时都会调它）。
    /// 它内部会先调一次 <c>RefreshForeground</c>，所以正常情况下这里再画一次是多余的 ——
    /// 留着是为了「本体哪天不再从 RefreshValues 调 RefreshForeground」时不至于整个功能失效。
    /// 画法是幂等的（HideOurs 之后再摆一遍），重复调用不会画出两条。
    /// </summary>
    public static System.Reflection.MethodInfo? ValuesTarget =>
        HarmonyLib.AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar), "RefreshValues");

    /// <summary><c>NHealthBar</c> 上那个私有字段 <c>_creature</c>：RefreshForeground 没有参数，只能从实例上取。</summary>
    private static readonly HarmonyLib.AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar, Creature>? CreatureOf =
        HarmonyLib.AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar, Creature>("_creature");

    /// <summary>「这一次为什么没画」每种理由只报一次日志（这方法是每帧级调用，不能刷屏）。</summary>
    private static int _warned;
    private static void WarnOnce(int bit, string why)
    {
        if ((_warned & (1 << bit)) != 0) return;
        _warned |= 1 << bit;
        Log.Warn("[覆盖血条] 没画出来（这条只报一次）：" + why);
    }

    /// <summary>
    /// 上一次我们自己写进去的 <c>_hpForeground.OffsetRight</c>（我们自己写的那份 + **写之前的本体原值**）。
    ///
    /// <para>V0.2.311（用户报「两个状态却有 3 个颜色」）：本体 <c>RefreshValues</c> 内部会先调一次
    /// <c>RefreshForeground</c>。两个入口都打了补丁时，同一次刷新里我们的 Postfix 会跑**两遍** ——
    /// 第一遍按「本体原值」把红条缩了 12×N 像素，第二遍又拿**已经缩过**的值当基准再缩一次 →
    /// 红条右端比覆盖条左端多退了「一整截」，中间空出来的那一段露出的是本体的浅红「掉血预览条」
    /// （<c>%HpMiddleground</c>），于是 2 条状态看起来像 3 段颜色。</para>
    ///
    /// <para>两头都治：① <c>PatchesGen</c> 现在**只打一个**入口（RefreshValues 只在 RefreshForeground
    /// 找不到时才兜底）；② 这里再记一份「本体原值」，读到的是我们自己写的值就用原值当基准 —— 重复调用幂等。</para>
    /// </summary>
    private sealed class OffsetMemo { public float Written; public float Base; }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar, OffsetMemo> Memo = new();

    /// <summary>每条状态「第一次真的画上去」记一条（方便一眼看出是没触发还是没画出来）。</summary>
    private static int _drawn;
    private static void NoteDrawn(int index, int amount, float from, float to, float barWidth, float maxHp)
    {
        int bit = 1 << index;
        if ((_drawn & bit) != 0) return;
        _drawn |= bit;
        string who = index >= 0 && index < Names.Length ? Names[index] : ("#" + index);
        Log.Info($"[覆盖血条] 画上了：{who}　层数={amount}　那一段=[{from:0.#}, {to:0.#}]（血条宽 {barWidth:0.#}，最大生命 {maxHp}）");
    }

    /// <summary>
    /// 「钩子确实跑起来了、只是这个生物身上没有那几条状态」记一条（只记一次）。
    /// 这一条和上面那些「没画出来」的理由合起来，能把「补丁没打上 / 打了没触发 / 触发了但没状态 / 真画了」
    /// 四种情况分得清清楚楚 —— 之前用户报「没显示」时日志里什么线索都没有。
    /// </summary>
    private static int _ranOnce;
    private static void NoteRanOnce(float barWidth)
    {
        if (_ranOnce != 0) return;
        _ranOnce = 1;
        Log.Info($"[覆盖血条] 钩子跑起来了（血条宽 {barWidth:0.#}），但这个生物身上没有配了覆盖色的状态，所以这次没画。");
    }

    /// <summary>本体把自己那几截摆完之后，我们接着摆自己这些。</summary>
    public static void Postfix(MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar __instance)
    {
        try
        {
            if (__instance is null) return;
            if (CreatureOf is null)
            {
                WarnOnce(0, "拿不到 NHealthBar 上的私有字段 _creature（本体改过血条类）");
                return;
            }
            Creature? creature = CreatureOf(__instance);
            if (creature is null) return;
            // 无限血条（本体那两档）没有「一截血」这回事：本体会在那之前就返回，我们也不插手
            if (creature.HpDisplay != HpDisplay.Normal)
            {
                WarnOnce(1, "这个生物的血条是「无限」档（本体那两档），没有可覆盖的一截血");
                return;
            }

            // 模板就是本体那截绿色（中毒），我们照着它复制；红条用来算「当前血量的右端」
            NinePatchRect? template = __instance.GetNodeOrNull<NinePatchRect>("%PoisonForeground");
            Control? red = __instance.GetNodeOrNull<Control>("%HpForeground");
            if (template is null || red is null)
            {
                WarnOnce(2, "找不到本体血条节点 %PoisonForeground / %HpForeground（本体改了血条场景的节点名）");
                return;
            }
            Control? mask = template.GetParentOrNull<Control>();
            if (mask is null)
            {
                WarnOnce(3, "本体那截绿色的父节点不是 Control（Mask 改了）");
                return;
            }
            float parentWidth = mask.Size.X;
            if (parentWidth <= 0f || creature.MaxHp <= 0 || creature.CurrentHp <= 0)
            {
                // 这一帧还没布局好是很常见的（血条刚建出来那一下），所以措辞别写成「坏了」：
                // 真的画上去了会有 NoteDrawn 那条日志，两边对照着看。
                WarnOnce(4, $"血条还没布局好 / 血量为 0（Mask 宽={parentWidth}，MaxHp={creature.MaxHp}，Hp={creature.CurrentHp}）"
                    + " —— 若之后有「画上了」那条日志，就说明只是这一帧还没准备好");
                return;
            }

            // 和本体 _expectedMaxFgWidth 同一算法（血条容器宽 - 前景左右各 5 的缩进合计 10）
            float maxFgWidth = __instance.HpBarContainer.Size.X - 10f;
            if (maxFgWidth <= 0f) maxFgWidth = parentWidth;

            // 每次刷新先把我们上一次画的全部藏起来（状态没了 / 层数归零就在这一步收尾）
            HideOurs(mask);

            // 左端：让开本体灾厄那一截（它就是从最左端盖的）
            float leftEnd = 0f;
            Control? doom = __instance.GetNodeOrNull<Control>("%DoomForeground");
            if (doom is not null && doom.Visible) leftEnd = Math.Max(0f, parentWidth + doom.OffsetRight);

            // 右端：本体算好的「当前血量的右端」（中毒在场时它已经往左退过一次）。
            // ⚠ 必须用**本体写的**那个值当基准：同一帧里如果我们被叫了两次（两个入口都打上补丁时就是这样），
            //   第二次读到的 `red.OffsetRight` 是第一次我们自己写进去的（已经缩过），再缩一次就会空出一截 ——
            //   V0.2.311 用户报的「两个状态却有 3 个颜色」就是这么来的（空的那截露出浅红的掉血预览条）。
            OffsetMemo memo = Memo.GetValue(__instance, _ => new OffsetMemo());
            float ownOffset = red.OffsetRight;
            float baseOffset = Math.Abs(ownOffset - memo.Written) < 0.01f ? memo.Base : ownOffset;
            float rightEnd = parentWidth + baseOffset;
            bool anyLeft = false;
            bool anyDrawn = false;

{{checks.ToString().TrimEnd('\r', '\n')}}
            // 红条 = 两边让完之后中间剩下的那一段。
            // 左端只在真的有「从左边开始」的覆盖条时才动 —— 没配的时候一个像素都不碰本体（灾厄的观感不变）。
            if (anyLeft) red.OffsetLeft = leftEnd;
            else red.OffsetLeft = 0f;
            red.OffsetRight = rightEnd - parentWidth;
            memo.Base = baseOffset;
            memo.Written = red.OffsetRight;

            // 钩子确实进来了、但身上没有配了覆盖色的状态：记一条（只记一次），
            // 这样「补丁没打上 / 打了没触发 / 触发了但身上没状态 / 真画了」四种情况在日志里分得清。
            if (!anyDrawn) NoteRanOnce(parentWidth);
        }
        catch (Exception e)
        {
            Log.Error("自定义状态覆盖血条失败（已忽略）：" + e);
        }
    }

    /// <summary>把一条覆盖条画到 <c>[a, b]</c>（Mask 里的局部横坐标）这段上；画上了返回 true。</summary>
    private static bool Place(Control mask, NinePatchRect template, float parentWidth, float maxFgWidth, int maxHp,
        string key, Color color, bool left, int amount, int index,
        ref float leftEnd, ref float rightEnd, ref bool anyLeft)
    {
        // 宽度 = 层数 / 最大生命 × 前景宽（和本体 GetFgWidth 同一算法）
        float width = (float)amount / maxHp * maxFgWidth;
        // ⚠ 本体 GetFgWidth 有 12px 下限（`Math.Max(val, 12f)`），这里照抄：
        // 层数少（比如 1 层 / 最大生命 70）时线性算出来只有 3px 上下，
        // 而九宫格左右边距各 6px —— 比 12px 还窄的条**画不出来**，用户报的「没显示」有一半是这个。
        if (width < 12f) width = 12f;
        float a;
        float b;
        if (left)
        {
            a = leftEnd;
            b = Math.Min(leftEnd + width, rightEnd);
            if (b <= a) { WarnOnce(5, "从左边开始的那几条加起来比当前血量还多，后面这条放不下（本体中毒也是这么处理的）"); return false; }
            leftEnd = b;
            anyLeft = true;
        }
        else
        {
            b = rightEnd;
            a = Math.Max(rightEnd - width, leftEnd);
            if (b <= a) { WarnOnce(6, "从右边开始的那几条加起来比当前血量还多，后面这条放不下"); return false; }
            rightEnd = a;
        }

        Control? node = Ensure(mask, template, key);
        if (node is null) { WarnOnce(7, "复制本体那截绿色失败（它不是 NinePatchRect）"); return false; }
        node.SelfModulate = color;
        // 九宫格的左边缘会往左让 patch_margin_left 那么多（和本体摆中毒那一截的写法一致）
        node.OffsetLeft = Math.Max(0f, a - PatchMarginLeft(node));
        node.OffsetRight = b - parentWidth;
        node.Visible = true;
        NoteDrawn(index, amount, a, b, parentWidth, maxHp);
        return true;
    }

    /// <summary>取这个状态自己的覆盖条节点；没有就照着本体那截绿色复制一个（每个状态一份，颜色互不干扰）。</summary>
    private static Control? Ensure(Control mask, NinePatchRect template, string key)
    {
        string name = NodePrefix + key;
        if (mask.GetNodeOrNull<Control>(name) is Control exist) return exist;

        var mine = (NinePatchRect)template.Duplicate();
        mine.Name = name;
        // 复制品要**退出**场景的唯一名注册：不然它会去抢 %PoisonForeground 那个名字的查找
        mine.UniqueNameInOwner = false;
        mine.Visible = false;
        mask.AddChild(mine);        // 加在最后 = 画在红条 / 灾厄之上（我们不和它们重叠，所以只是保险）
        return mine;
    }

    /// <summary>把我们造的那些覆盖条全部藏起来（本体那截绿色 / 灾厄不归我们管）。</summary>
    private static void HideOurs(Control mask)
    {
        foreach (Node child in mask.GetChildren())
            if (child is Control c && c.Name.ToString().StartsWith(NodePrefix, StringComparison.Ordinal))
                c.Visible = false;
    }

    /// <summary>那截条九宫格的左边距（本体也是用它把左边界往左让一点，免得边缘被切）。</summary>
    private static float PatchMarginLeft(Control strip) =>
        strip is NinePatchRect np ? np.PatchMarginLeft : 0f;
}
""";
    }
}
