using System.Text;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 「本体状态改写」的生成：把本体某个状态（增益/减益）的名字 / 描述 / 图标 / 血条颜色 / 层数数字颜色改掉。
///
/// 三条路子（都对着本体源码确认过）：
///   ① 名字 / 描述 —— 本地化表覆盖。本体 LocManager.LoadTablesFromPath 会把自己 pck 里的
///      localization/&lt;语言&gt;/&lt;表名&gt;.json 和 ModManager.GetModdedLocTables(...) 读到的 mod 表
///      MergeWith 到一起（mod 的键盖本体），所以只要往我们的 powers.json 里写同名的键即可，不需要补丁。
///      键 = &lt;Id.Entry&gt; + ".title" / ".description" / ".smartDescription"。
///   ② 图标 —— PowerModel.Icon（状态栏小图标 + 悬停提示）与 PowerModel.BigIcon（施加/闪烁/移除特效）
///      都是按 Id.Entry 拼资源路径读的，没有扩展点，只能给这两个属性打 Postfix 返回我们 pck 里的图。
///   ③ 颜色 —— 层数数字是 PowerModel.AmountLabelColor（virtual，减益默认红）；
///      血条上那截「中毒」是场景节点 %PoisonForeground 的 self_modulate（本体只改它的显隐/偏移，从不改颜色）。
/// </summary>
public static class VanillaPowerGen
{
    /// <summary>这条改写实际生效、且确实改了什么。</summary>
    public static bool IsActive(VanillaPowerOverride o) =>
        o.Enabled && o.ChangesAnything && !string.IsNullOrWhiteSpace(o.PowerId);

    public static List<VanillaPowerOverride> Active(CharacterProfile p) =>
        p.VanillaPowerOverrides.Where(IsActive).ToList();

    public static bool HasAny(CharacterProfile p) =>
        Active(p).Count > 0 || CustomPowerGen.HasAny(p);

    /// <summary>自定义状态里配了图标 / 层数颜色的那些（外观补丁的表里要一起带上）。</summary>
    public static bool NeedsCosmeticPatch(CharacterProfile p) =>
        Active(p).Count > 0
        || CustomPowerGen.Active(p).Any(cp => !string.IsNullOrWhiteSpace(cp.Icon) || !string.IsNullOrWhiteSpace(cp.AmountColor));

    /// <summary>
    /// 本体状态改名 / 改描述要写的本地化键。
    ///
    /// **不再覆盖本体的键**（<c>&lt;SLUG&gt;.title</c> 那种）：本地化表是**全局**的，
    /// 一覆盖，别的角色、百科、以及任何提到这个状态的地方都会变成新名字
    /// （用户实测：sparkle 把易伤改成破绽后，百科里铁甲战士的「战栗」描述也变成了破绽）。
    /// 改成写我们自己的键 <c>&lt;SLUG&gt;_FORGE.title / .description / .smartDescription</c>，
    /// 由生成的 <c>ForgePowerRename</c> 补丁**只在「这一局玩的是我方角色」时**返回它们 ——
    /// 本体键一个字都不动，其它角色 / 主菜单百科永远是原名。
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> LocEntries(CharacterProfile p)
    {
        foreach (var o in Active(p))
        {
            string slug = SlugOf(o);
            if (slug.Length == 0) continue;

            string name = (o.Name ?? "").Trim();
            if (name.Length > 0 && name != (o.VanillaName ?? "").Trim())
                yield return new(ForgeLocKey(slug, "title"), name);

            string desc = (o.Description ?? "").Trim();
            if (desc.Length == 0) continue;
            string vanillaDesc = (EffectCatalog.ZhLocText(slug + ".description") ?? "").Trim();
            if (desc == vanillaDesc) continue;                       // 和本体一样 = 没改，不用写

            yield return new(ForgeLocKey(slug, "description"), desc);
            // 战斗里的悬停提示优先用 smartDescription；只有本体本来就有这个键时才一起写上
            // （凭空加一个键会把提示从「普通」变成「smart」，没必要）
            if (EffectCatalog.ZhLocText(slug + ".smartDescription") is not null)
                yield return new(ForgeLocKey(slug, "smartDescription"), desc);
        }
    }

    /// <summary>我们自己的本地化键（不碰本体键）：<c>&lt;SLUG&gt;_FORGE.title</c> 这种。</summary>
    public static string ForgeLocKey(string slug, string suffix) => slug + "_FORGE." + suffix;

    /// <summary>这条改写用的本体本地化键前缀（优先用配置里记下的，没有就按类名推）。</summary>
    public static string SlugOf(VanillaPowerOverride o)
    {
        string slug = (o.LocSlug ?? "").Trim();
        return slug.Length > 0 ? slug : EffectCatalog.SlugFor(o.PowerId);
    }

    /// <summary>自定义图标在工程里的相对路径（res:// 下）。</summary>
    public static string IconRelPath(CharacterProfile p, VanillaPowerOverride o) =>
        $"images/powers_override/{SlugOf(o).ToLowerInvariant()}_icon.png";

    /// <summary>自定义图标的 res:// 路径（没上传返回 null）。</summary>
    public static string? IconResPath(CharacterProfile p, VanillaPowerOverride o) =>
        string.IsNullOrWhiteSpace(o.Icon) ? null : "res://" + IconRelPath(p, o);

    /// <summary>
    /// 「本体卡牌 / 遗物 / 药水描述里的旧名字也一起换掉」：把本机读到的本体中文表里
    /// 提到这个名字的条目覆盖成替换后的文本（只覆盖真正含旧名的键，别的键一律不碰）。
    /// 用户报过：改名之后卡面描述里还写着「中毒」，看着像没改。
    /// </summary>
    public static IEnumerable<(string Table, string Key, string Text)> VanillaTextReplacements(CharacterProfile p)
    {
        foreach (var o in Active(p))
        {
            if (!o.ReplaceInVanillaText) continue;
            string oldName = (o.VanillaName ?? "").Trim();
            string newName = (o.Name ?? "").Trim();
            if (oldName.Length == 0 || newName.Length == 0 || oldName == newName) continue;

            foreach (var (table, map) in new (string, IReadOnlyDictionary<string, string>)[]
            {
                ("cards", EffectCatalog.ZhCardLoc),
                ("relics", EffectCatalog.ZhRelicLoc),
                ("potions", EffectCatalog.ZhPotionLoc),
            })
            {
                foreach (var kv in map)
                {
                    // 只改描述类的键：标题里含状态名的情况（万一某张卡名就是这个）不动，免得认不出来
                    if (!kv.Key.EndsWith(".description", StringComparison.Ordinal)
                        && !kv.Key.EndsWith(".smartDescription", StringComparison.Ordinal)) continue;
                    if (kv.Value.Length == 0 || !kv.Value.Contains(oldName, StringComparison.Ordinal)) continue;
                    yield return (table, kv.Key, kv.Value.Replace(oldName, newName));
                }
            }
        }
    }

    /// <summary>本机读不读得到本体卡牌中文表（读不到就没法替换卡面描述里的旧名字，界面上要如实说）。</summary>
    public static bool CanRewriteVanillaCardText => EffectCatalog.ZhCardLoc.Count > 0;

    /// <summary>生成 cs/VanillaPowerOverride.cs（本体状态改写 + 自定义状态的图标 / 层数颜色 / 血条颜色补丁）。</summary>
    public static string Source(CharacterProfile p)
    {
        var n = Naming.From(p);

        var rows = new StringBuilder();
        void Row(string slug, string iconRes, string amount, string barNode, string barColor) =>
            rows.Append("        [").Append(Lit.Str(slug)).Append("] = new Entry { IconRes = ").Append(Lit.Str(iconRes))
                .Append(", AmountColor = ").Append(Lit.Str(amount))
                .Append(", BarNode = ").Append(Lit.Str(barNode))
                .Append(", BarColor = ").Append(Lit.Str(barColor)).AppendLine(" },");

        // 改名 / 改描述的表：本体 Id.Entry → 我们自己的本地化键前缀
        var renames = new StringBuilder();
        void RenameRow(string slug, bool hasName, bool hasDesc) =>
            renames.Append("        [").Append(Lit.Str(slug)).Append("] = new Rename { Name = ")
                .Append(hasName ? "true" : "false").Append(", Desc = ").Append(hasDesc ? "true" : "false")
                .Append(", Prefix = ").Append(Lit.Str(slug + "_FORGE")).AppendLine(" },");
        foreach (var o in Active(p))
        {
            string rs = SlugOf(o);
            if (rs.Length == 0) continue;
            string newName = (o.Name ?? "").Trim();
            bool hasName = newName.Length > 0 && newName != (o.VanillaName ?? "").Trim();
            string newDesc = (o.Description ?? "").Trim();
            string oldDesc = (EffectCatalog.ZhLocText(rs + ".description") ?? "").Trim();
            bool hasDesc = newDesc.Length > 0 && newDesc != oldDesc;
            if (hasName || hasDesc) RenameRow(rs, hasName, hasDesc);
        }

        foreach (var o in Active(p))
        {
            string barNode = string.IsNullOrWhiteSpace(o.BarColor) ? "" : (EffectCatalog.HealthBarNodeFor(o.PowerId) ?? "");
            Row(SlugOf(o), IconResPath(p, o) ?? "", (o.AmountColor ?? "").Trim(), barNode,
                barNode.Length == 0 ? "" : (o.BarColor ?? "").Trim());
        }
        // 自定义状态：图标和层数颜色走同一张表（它们也一样不能在自己的子类里重写 Icon）
        for (int i = 0; i < p.CustomPowers.Count; i++)
        {
            var cp = p.CustomPowers[i];
            if (!CustomPowerGen.IsActive(cp)) continue;
            if (string.IsNullOrWhiteSpace(cp.Icon) && string.IsNullOrWhiteSpace(cp.AmountColor)) continue;
            Row(CustomPowerGen.EntryOf(p, cp, i), CustomPowerGen.IconResPath(p, cp, i) ?? "",
                (cp.AmountColor ?? "").Trim(), "", "");
        }

        return $$"""
        // <auto-generated> 状态外观（本体状态改写 + 自定义状态）：图标 / 层数颜色 / 血条颜色 </auto-generated>
        namespace {{n.Namespace}};

        /// <summary>要改的本体状态外观表（键 = 本体 Id.Entry，如 POISON_POWER）。</summary>
        internal static class VanillaPowerCosmetic
        {
            internal sealed class Entry
            {
                /// <summary>pck 里的图标路径；空串 = 不改图标</summary>
                internal string IconRes = "";
                /// <summary>层数数字颜色（#RRGGBB 或 RRGGBB）；空串 = 保持本体默认（减益红 / 其它米白）</summary>
                internal string AmountColor = "";
                /// <summary>要改颜色的血条前景节点名（本体只有中毒有：PoisonForeground）；空串 = 不改血条颜色</summary>
                internal string BarNode = "";
                /// <summary>血条那一截的颜色</summary>
                internal string BarColor = "";
            }

            private static readonly Dictionary<string, Entry> Table = new(StringComparer.Ordinal)
            {
        {{rows.ToString().TrimEnd('\r', '\n')}}
            };

            /// <summary>按本体 Id.Entry 查这条改写。</summary>
            internal static bool TryGet(string? id, out Entry entry) => Table.TryGetValue(id ?? "", out entry!);

            /// <summary>需要改血条颜色的那几条（血条一建好就套上去）。</summary>
            internal static IEnumerable<Entry> BarEntries() =>
                Table.Values.Where(v => v.BarNode.Length > 0 && v.BarColor.Length > 0);

            private static readonly Dictionary<string, Texture2D?> Icons = new(StringComparer.Ordinal);

            /// <summary>取这条改写要用的图标；没配 / 加载失败都返回 null（界面保持本体原样）。</summary>
            internal static Texture2D? IconFor(string id, string resPath)
            {
                if (resPath.Length == 0) return null;
                if (Icons.TryGetValue(id, out Texture2D? cached)) return cached;

                Texture2D? tex = null;
                try
                {
                    if (ResourceLoader.Exists(resPath)) tex = GD.Load<Texture2D>(resPath);
                    else Log.Error("本体状态改写：图标不存在，保持本体原图：" + resPath);
                }
                catch (Exception e) { Log.Error("本体状态改写：图标加载失败（已忽略）：" + e.Message); }

                Icons[id] = tex;
                return tex;
            }

            /// <summary>#RRGGBB / RRGGBB → 颜色；写坏了返回 null（不改本体颜色）。</summary>
            internal static Color? ParseColor(string hex)
            {
                string s = (hex ?? "").Trim().TrimStart('#');
                if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
                if (s.Length != 6) return null;
                try { return new Color("#" + s); } catch { return null; }
            }

            /// <summary>
            /// 灾厄那截血条用的渐变：本体（health_bar.tscn）里 DoomForeground 挂着一个 ShaderMaterial，
            /// doom_bar.gdshader 用 gradient_tex 按噪声采样出颜色（**直接覆盖 COLOR.rgb**，所以改 self_modulate 没用）。
            /// 那条渐变是 3 个色标、偏移 0 / 0.514583 / 1，亮度依次约 1 : 0.972 : 0.827。
            /// 我们照同样的结构造一条，但色相/饱和度换成你要的颜色 —— 这样形状和本体一致，只是换了个色。
            /// </summary>
            internal static Texture2D DoomBarTexture(Color c)
            {
                float h = c.H, s = c.S, v = Math.Max(c.V, 0.05f);
                var gradient = new Gradient
                {
                    Offsets = new[] { 0f, 0.514583f, 1f },
                    Colors = new[]
                    {
                        Color.FromHsv(h, s, v * 1.000f),
                        Color.FromHsv(h, s, Math.Min(1f, v * 0.972f)),
                        Color.FromHsv(h, s, v * 0.827f),
                    },
                };
                return new GradientTexture1D { Gradient = gradient, Width = 256 };
            }
        }

        /// <summary>
        /// 本体状态改名 / 改描述 **只在我方角色的这一局里生效** 的开关 + 文本表。
        ///
        /// 为什么不再直接覆盖本体的本地化键：本地化表是全局的，一覆盖，别的角色、百科、
        /// 以及任何提到这个状态的地方都会变成新名字（用户实测：sparkle 把易伤改成破绽后，
        /// 百科里铁甲战士的「战栗」描述也成了破绽）。现在本体键一个字都不动 ——
        /// 我们的新文本放在自己的键 <c>&lt;SLUG&gt;_FORGE.*</c> 里，只有这里的补丁会返回它们。
        ///
        /// 判定「这一局玩的是不是我方角色」用的是本体自己的公开链：
        /// <c>RunManager.Instance.IsInProgress</c> → <c>DebugOnlyGetState()</c> →
        /// <c>LocalContext.GetMe(runState)?.Character</c>（本体的百科页也是这么判的）。
        /// 主菜单 / 别的角色时这条链给 null → 一律返回本体原值。
        /// </summary>
        internal static class ForgePowerRename
        {
            internal sealed class Rename
            {
                internal bool Name;
                internal bool Desc;
                internal string Prefix = "";
            }

            /// <summary>键 = 本体 Id.Entry（如 VULNERABLE_POWER）。</summary>
            private static readonly Dictionary<string, Rename> Table = new(StringComparer.Ordinal)
            {
        {{renames.ToString().TrimEnd('\r', '\n')}}
            };

            private static ulong _cachedFrame = ulong.MaxValue;
            private static bool _cached;

            /// <summary>这一局玩的是不是我方角色（主菜单 / 别的角色 → false）。</summary>
            internal static bool IsMyRun()
            {
                try
                {
                    ulong frame = Godot.Engine.GetProcessFrames();
                    if (frame == _cachedFrame) return _cached;
                    _cachedFrame = frame;
                    _cached = ComputeIsMyRun();
                    return _cached;
                }
                catch { return false; }
            }

            private static bool ComputeIsMyRun()
            {
                if (Table.Count == 0) return false;
                if (!MegaCrit.Sts2.Core.Runs.RunManager.Instance.IsInProgress) return false;
                MegaCrit.Sts2.Core.Entities.Players.Player? me =
                    MegaCrit.Sts2.Core.Context.LocalContext.GetMe(MegaCrit.Sts2.Core.Runs.RunManager.Instance.DebugOnlyGetState());
                return me?.Character is {{n.CharClass}};
            }

            private static bool TryGet(string? entry, out Rename r) => Table.TryGetValue(entry ?? "", out r!);

            /// <summary>这个本体状态有没有被改名（用于「本体卡面文字要不要跟着换」之外的判断）。</summary>
            internal static bool Has(string? entry) => TryGet(entry, out _);

            internal static LocString? Title(string? entry) =>
                TryGet(entry, out Rename r) && r.Name && LocString.Exists("powers", r.Prefix + ".title")
                    ? new LocString("powers", r.Prefix + ".title") : null;

            internal static LocString? Description(string? entry) =>
                TryGet(entry, out Rename r) && r.Desc && LocString.Exists("powers", r.Prefix + ".description")
                    ? new LocString("powers", r.Prefix + ".description") : null;

            internal static LocString? SmartDescription(string? entry) =>
                TryGet(entry, out Rename r) && r.Desc && LocString.Exists("powers", r.Prefix + ".smartDescription")
                    ? new LocString("powers", r.Prefix + ".smartDescription") : null;
        }

        /// <summary>状态名字：只在我方角色的这一局里换成新名字。</summary>
        [HarmonyLib.HarmonyPatch(typeof(PowerModel), nameof(PowerModel.Title), HarmonyLib.MethodType.Getter)]
        internal static class ForgePowerRenameTitlePatch
        {
            [HarmonyLib.HarmonyPostfix]
            private static void AfterTitle(PowerModel __instance, ref LocString __result)
            {
                try
                {
                    if (__instance is null) return;
                    if (!ForgePowerRename.IsMyRun()) return;
                    LocString? mine = ForgePowerRename.Title(__instance.Id.Entry);
                    if (mine is not null) __result = mine;
                }
                catch (Exception ex) { Log.Error("本体状态改名：换名字失败（已忽略）：" + ex.Message); }
            }
        }

        /// <summary>状态描述：同样只在我方角色的这一局里换。</summary>
        [HarmonyLib.HarmonyPatch(typeof(PowerModel), nameof(PowerModel.Description), HarmonyLib.MethodType.Getter)]
        internal static class ForgePowerRenameDescPatch
        {
            [HarmonyLib.HarmonyPostfix]
            private static void AfterDescription(PowerModel __instance, ref LocString __result)
            {
                try
                {
                    if (__instance is null) return;
                    if (!ForgePowerRename.IsMyRun()) return;
                    LocString? mine = ForgePowerRename.Description(__instance.Id.Entry);
                    if (mine is not null) __result = mine;
                }
                catch (Exception ex) { Log.Error("本体状态改名：换描述失败（已忽略）：" + ex.Message); }
            }
        }

        /// <summary>战斗里悬停提示优先用 smartDescription（这个属性不是 virtual，但补丁打在方法本身上，一样生效）。</summary>
        [HarmonyLib.HarmonyPatch(typeof(PowerModel), nameof(PowerModel.SmartDescription), HarmonyLib.MethodType.Getter)]
        internal static class ForgePowerRenameSmartPatch
        {
            [HarmonyLib.HarmonyPostfix]
            private static void AfterSmartDescription(PowerModel __instance, ref LocString __result)
            {
                try
                {
                    if (__instance is null) return;
                    if (!ForgePowerRename.IsMyRun()) return;
                    LocString? mine = ForgePowerRename.SmartDescription(__instance.Id.Entry);
                    if (mine is not null) __result = mine;
                }
                catch (Exception ex) { Log.Error("本体状态改名：换战斗描述失败（已忽略）：" + ex.Message); }
            }
        }

        /// <summary>状态栏那个小图标 + 悬停提示里的图标：换成你上传的图。</summary>
        [HarmonyLib.HarmonyPatch(typeof(PowerModel), nameof(PowerModel.Icon), HarmonyLib.MethodType.Getter)]
        internal static class VanillaPowerIconPatch
        {
            [HarmonyLib.HarmonyPostfix]
            private static void AfterIcon(PowerModel __instance, ref Texture2D __result)
            {
                try
                {
                    if (__instance is null) return;
                    // 只在「这一局玩的是我方角色」时改：别的角色 / 主菜单百科一律保持本体原样
                    if (!ForgePowerRename.IsMyRun()) return;
                    if (!VanillaPowerCosmetic.TryGet(__instance.Id.Entry, out VanillaPowerCosmetic.Entry e)) return;
                    Texture2D? tex = VanillaPowerCosmetic.IconFor(__instance.Id.Entry, e.IconRes);
                    if (tex is not null) __result = tex;
                }
                catch (Exception ex) { Log.Error("本体状态改写：替换图标失败（已忽略）：" + ex.Message); }
            }
        }

        /// <summary>施加 / 闪烁 / 移除特效用的是大图，一起换掉（不然会看到本体那张图）。</summary>
        [HarmonyLib.HarmonyPatch(typeof(PowerModel), nameof(PowerModel.BigIcon), HarmonyLib.MethodType.Getter)]
        internal static class VanillaPowerBigIconPatch
        {
            [HarmonyLib.HarmonyPostfix]
            private static void AfterBigIcon(PowerModel __instance, ref Texture2D __result)
            {
                try
                {
                    if (__instance is null) return;
                    // 只在「这一局玩的是我方角色」时改：别的角色 / 主菜单百科一律保持本体原样
                    if (!ForgePowerRename.IsMyRun()) return;
                    if (!VanillaPowerCosmetic.TryGet(__instance.Id.Entry, out VanillaPowerCosmetic.Entry e)) return;
                    Texture2D? tex = VanillaPowerCosmetic.IconFor(__instance.Id.Entry, e.IconRes);
                    if (tex is not null) __result = tex;
                }
                catch (Exception ex) { Log.Error("本体状态改写：替换大图标失败（已忽略）：" + ex.Message); }
            }
        }

        /// <summary>
        /// 状态层数那个数字的颜色。
        /// 坑：本体「中毒」「灾厄」这两个类**自己 override 了** PowerModel.AmountLabelColor
        /// （PoisonPower.cs / DoomPower.cs: `public override Color AmountLabelColor => _normalAmountLabelColor;`），
        /// 所以补在基类属性上根本轮不到（虚方法派发到派生实现）—— 用户实测「改层数颜色没效果」就是这个原因。
        /// 改成补在消费端：NPower.RefreshAmount() 里会用 Model.AmountLabelColor 覆盖一次 Label 的字体颜色，
        /// 我们在这个方法之后把颜色再盖回去，对所有状态（含 override 的）都生效。
        /// </summary>
        [HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NPower), "RefreshAmount")]
        internal static class VanillaPowerAmountColorPatch
        {
            [HarmonyLib.HarmonyPostfix]
            private static void AfterRefreshAmount(MegaCrit.Sts2.Core.Nodes.Combat.NPower __instance)
            {
                try
                {
                    if (!ForgePowerRename.IsMyRun()) return;
                    string id;
                    try { id = __instance.Model?.Id.Entry ?? ""; } catch { return; }   // 模型还没挂上时本体自己也走空分支
                    if (!VanillaPowerCosmetic.TryGet(id, out VanillaPowerCosmetic.Entry e)) return;
                    Color? c = VanillaPowerCosmetic.ParseColor(e.AmountColor);
                    if (c is null) return;

                    MegaCrit.Sts2.addons.mega_text.MegaLabel? label =
                        __instance.GetNodeOrNull<MegaCrit.Sts2.addons.mega_text.MegaLabel>("%AmountLabel");
                    if (label is not null)
                        label.AddThemeColorOverride(MegaCrit.Sts2.addons.mega_text.ThemeConstants.Label.FontColor, c.Value);
                }
                catch (Exception ex) { Log.Error("本体状态改写：替换层数颜色失败（已忽略）：" + ex.Message); }
            }
        }

        /// <summary>
        /// 血条上那一截的颜色。两种画法要分开处理：
        ///   · 中毒（%PoisonForeground）：本体用节点自己的 self_modulate 画（绿色），代码只改显隐/偏移，
        ///     所以直接设 SelfModulate 就行；
        ///   · 灾厄（%DoomForeground）：颜色是 doom_bar.gdshader 用材质里的 gradient_tex 渐变画的，
        ///     改 self_modulate 会被着色器覆盖 → 得换掉材质里的 gradient_tex（先 Duplicate 一份，
        ///     免得改到场景共享的那个材质）。
        /// </summary>
        [HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar), nameof(MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar._Ready))]
        internal static class VanillaPowerHealthBarPatch
        {
            [HarmonyLib.HarmonyPostfix]
            private static void AfterReady(MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar __instance)
            {
                try
                {
                    if (!ForgePowerRename.IsMyRun()) return;   // 血条颜色也只在我方角色的这一局里改
                    foreach (VanillaPowerCosmetic.Entry e in VanillaPowerCosmetic.BarEntries())
                    {
                        Color? c = VanillaPowerCosmetic.ParseColor(e.BarColor);
                        if (c is null) continue;

                        CanvasItem? node = __instance.GetNodeOrNull<CanvasItem>("%" + e.BarNode);
                        if (node is null) continue;

                        if (e.BarNode == "DoomForeground")
                        {
                            // 灾厄：换材质里的渐变贴图（本体的渐变是紫色，换成你要的色）
                            if (node.Material is ShaderMaterial shared)
                            {
                                var mine = (ShaderMaterial)shared.Duplicate();
                                mine.SetShaderParameter("gradient_tex", VanillaPowerCosmetic.DoomBarTexture(c.Value));
                                node.Material = mine;
                            }
                        }
                        else
                        {
                            node.SelfModulate = c.Value;
                        }
                    }
                }
                catch (Exception ex) { Log.Error("本体状态改写：血条颜色应用失败（已忽略）：" + ex.Message); }
            }
        }
        """;
    }
}
