using System.Globalization;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 「召唤物」的公共逻辑：宠物类名、本地化键、宠物类与命令助手的源码、最小视觉场景。
///
/// 为什么集中在一个文件里：宠物类、召唤命令、本地化键、视觉场景这几样必须严格对齐 ——
/// 生成代码里 <c>MonsterModel</c> 子类的**类名**会变成游戏里的模型 ID（本体 <c>ModelDb</c> 只按类名算，
/// 忽略命名空间），本地化键 <c>&lt;ENTRY&gt;.name</c> 和视觉场景名又都是从这个类名推出来的；
/// 只要有一处对不上，游戏里就会「宠物没名字」或者「找不到视觉场景」。
///
/// 现在是**列表**：每个勾了启用的召唤物各自一个 <c>MonsterModel</c> 子类 + 一个 <c>&lt;X&gt;Cmd</c>
/// （需要「替主人挨打」时再多一个守卫 Power 类），它们全都写进同一个 <c>cs/Pet.cs</c>；
/// 战斗里可以同时在场（本体 <c>PlayerCombatState._pets</c> 本来就是列表）。
///
/// 走的是什么机制（都在本体的公开 API 上，**不需要任何 Harmony 补丁**）：
///   · 上场：<c>PlayerCmd.AddPet&lt;T&gt;(player)</c>（本体的通用宠物 API，Byrdpip / Pael's Legion 就是这么用的）；
///   · 注册：本体 <c>ModelDb</c> 会扫模组程序集里的 <c>AbstractModel</c> 子类，**只要 public 无参构造函数**；
///   · 回合：宠物没有自主回合，照抄 <c>Osty</c> 的自循环 <c>NOTHING_MOVE</c>（空操作），
///           实际靠玩家出「伙伴攻击」卡驱动；
///   · 站位：覆写 <c>AfterCreatureAddedToCombat</c>（见 <see cref="SummonSpec"/> 的注释：本体把非 Osty 宠物
///           摆在主人 X+20，太近）；
///   · 血条：覆写 <c>IsHealthBarVisible</c> + 召唤后手动 <c>SetCreatureIsInteractable(pet, true)</c>
///           （本体 <c>NCombatRoom.AddCreature</c> 会对非 Osty 宠物关掉交互，战斗中召唤的宠物因此永远没血条）；
///   · 打人：先走正常卡牌路径，再用 <c>FromPetAttacker</c> 扩展方法把攻击者换成宠物（见 PetAttackExtensions）。
/// </summary>
public static class PetGen
{
    /// <summary>默认站位距离（主人 X + N）。本体给非 Osty 宠物硬编码的是 20，看起来像叠在主人身上。</summary>
    public const int DefaultStandDistance = SummonSpec.DefaultStandDistance;

    /// <summary>宠物「替主人挨打」的守卫 Power 类名前缀 + 宠物类名（避免和本体状态撞名）。</summary>
    public const string GuardianPowerPrefix = "ForgePetGuardian";

    /// <summary>一只召唤物在生成期用的全部推导结果（类名 / 本地化键 / 名字 / 血量 / 站位 …）。</summary>
    public sealed record PetDef(
        SummonSpec Spec,
        string Id,             // 稳定标识 = 类名（也写进 EffectSpec.PetSummon）
        string ClassName,
        string Entry,          // 本地化键前缀，如 UI_CHECK_PET
        string DisplayName,
        int Hp,
        bool HasImage,
        bool Guardian,
        int StandDistance);

    /// <summary>所有勾了启用的召唤物（列表顺序 = 生成顺序 = 卡面描述 / 回读顺序）。</summary>
    public static IReadOnlyList<SummonSpec> Enabled(CharacterProfile p) =>
        p.Summons is null ? Array.Empty<SummonSpec>() : p.Summons.Where(s => s is { Enabled: true }).ToList();

    /// <summary>这个配置有没有任何召唤物要生成。</summary>
    public static bool IsActive(CharacterProfile p) => Enabled(p).Count > 0;

    /// <summary>
    /// 一个召唤物的英文类名。
    ///   · 用户填了合法标识符 → 用它；
    ///   · 留空 → 列表里第一只用 <c>&lt;角色类名&gt;Pet</c>（和老版本的单只召唤物保持一致），
    ///     后面几只加序号（<c>&lt;角色类名&gt;Pet2</c> …），否则同类名会撞模型 ID。
    /// 为什么要看**列表下标**而不是「有没有别的宠物」：同一份配置反复生成必须得到同样的类名
    /// （类名决定本地化键、场景文件名、图片文件名，一变全错位）。
    /// </summary>
    public static string ClassNameOf(CharacterProfile p, SummonSpec spec)
    {
        string cls = (spec.ClassName ?? "").Trim();
        if (Naming.IsValidIdentifier(cls)) return cls;

        var all = p.Summons?.ToList() ?? new List<SummonSpec>();
        int index = all.FindIndex(s => ReferenceEquals(s, spec));
        // 不在列表里（老存档迁移时是「准备加进列表的最后一条」）→ 按排在所有现有条目之后算
        if (index < 0) index = all.Count;
        // 「留空的条目」之间的序号：只看排在它前面、且同样留空类名的有几只
        int autoIndex = 0;
        for (int i = 0; i < index && i < all.Count; i++)
            if (!Naming.IsValidIdentifier((all[i].ClassName ?? "").Trim())) autoIndex++;
        string prefix = Naming.From(p).CharClass + "Pet";
        return autoIndex == 0 ? prefix : prefix + (autoIndex + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>宠物类名 → 游戏里的模型 ID / 本地化键前缀（<c>MyPet</c> → <c>MY_PET</c>）。</summary>
    public static string EntryOf(string className) => Naming.EntryOf(className);

    /// <summary>宠物名牌上的名字（没填就用类名，至少不会是空名牌）。</summary>
    public static string DisplayNameOf(SummonSpec spec, string className)
    {
        string name = (spec.Name ?? "").Trim();
        return name.Length > 0 ? name : className;
    }

    /// <summary>血量兜底：非正数按 1（血量 ≤ 0 的宠物一上场就是死的，攻击也会被静默早退）。</summary>
    public static int HpOf(SummonSpec spec) => Math.Max(1, spec.Hp);

    /// <summary>站位距离兜底到 [MinStandDistance, MaxStandDistance]。</summary>
    public static int StandDistanceOf(SummonSpec spec) =>
        Math.Clamp(spec.StandDistance <= 0 ? DefaultStandDistance : spec.StandDistance,
            SummonSpec.MinStandDistance, SummonSpec.MaxStandDistance);

    /// <summary>用户确实上传了可用的宠物图片吗。</summary>
    public static bool HasImage(SummonSpec spec)
    {
        string? img = spec.Image;
        return !string.IsNullOrWhiteSpace(img) && File.Exists(img);
    }

    /// <summary>这只召唤物在生成期用的全部推导结果。</summary>
    public static PetDef DefOf(CharacterProfile p, SummonSpec spec)
    {
        string cls = ClassNameOf(p, spec);
        return new PetDef(spec, cls, cls, EntryOf(cls), DisplayNameOf(spec, cls), HpOf(spec),
            HasImage(spec), spec.TakesDamageForOwner, StandDistanceOf(spec));
    }

    /// <summary>列表里全部启用的召唤物（生成 / 界面下拉都用它）。</summary>
    public static IReadOnlyList<PetDef> All(CharacterProfile p) => Enabled(p).Select(s => DefOf(p, s)).ToList();

    /// <summary>「替主人挨打」的守卫 Power 类名（每个召唤物一个，避免和本体状态撞名）。</summary>
    public static string GuardianPowerClassOf(string petClassName) => GuardianPowerPrefix + petClassName;

    // ==================== 效果 → 召唤物 ====================

    /// <summary>
    /// 「召唤伙伴 / 伙伴攻击」这条效果指的是哪一只。
    ///
    /// <paramref name="id"/>（= <c>EffectSpec.PetSummon</c>）按**稳定标识**（类名）匹配；老存档里它是空的
    /// （上一版只有一只召唤物），那就退回「第一只启用的召唤物」，行为和以前完全一样。
    /// 找不到就返回 <c>null</c>，调用方自己兜底（校验器会拦住这种配置）。
    /// </summary>
    public static PetDef? Resolve(CharacterProfile p, string? id)
    {
        var all = All(p);
        if (all.Count == 0) return null;
        string want = (id ?? "").Trim();
        if (want.Length == 0) return all[0];
        foreach (var d in all)
            if (string.Equals(d.Id, want, StringComparison.Ordinal)) return d;
        // 手写 JSON / 回读兜底：类名大小写不一致，或存的是中文名
        foreach (var d in all)
            if (string.Equals(d.Id, want, StringComparison.OrdinalIgnoreCase)) return d;
        foreach (var d in all)
            if (string.Equals(d.DisplayName, want, StringComparison.Ordinal)) return d;
        return null;
    }

    /// <summary>界面上「召唤物」下拉里的一行：SelectedValue 用 <see cref="Id"/>（稳定标识）。</summary>
    public sealed record PetChoice(string Id, string Name, string Display);

    /// <summary>效果编辑器下拉用的候选（按列表顺序）。</summary>
    public static IReadOnlyList<PetChoice> Choices(CharacterProfile p) =>
        All(p).Select(d => new PetChoice(d.Id, d.DisplayName,
            d.DisplayName + "（" + d.Id + " ｜ 生命 " + d.Hp + "）")).ToList();

    /// <summary>
    /// 这个配置有没有任何「伙伴攻击」效果（有就说明要用扩展方法 FromPetAttacker，
    /// 也就必须生成 <c>cs/PetAttackExtensions.cs</c> —— 否则生成的卡牌会引用一个不存在的扩展方法，CS1061）。
    /// 只看卡牌：遗物上的「伙伴攻击」本来就生成不了（没有玩家选中的目标，生成时会被忽略）。
    /// </summary>
    public static bool UsesAttackExtension(CharacterProfile p) =>
        IsActive(p) && p.Cards.Any(c => c.Effects.Any(e => e.Kind == "PetAttack"));

    // ==================== 视觉 ====================

    /// <summary>用户上传的宠物图在工程里的相对路径（res:// 下）。</summary>
    public static string ImageRelPath(PetDef d) => "images/monsters/" + d.Entry.ToLowerInvariant() + ".png";

    /// <summary>宠物视觉场景在工程里的相对路径（和 <c>MonsterModel.VisualsPath</c> 的默认约定一致）。</summary>
    public static string SceneRelPath(PetDef d) => "scenes/creature_visuals/" + d.Entry.ToLowerInvariant() + ".tscn";

    /// <summary>
    /// 把每只宠物的图片拷进工程，并生成一个最小的 <c>scenes/creature_visuals/&lt;entry&gt;.tscn</c>。
    /// 没上传图片就什么都不做（宠物走本体 fallback 的 error.png 占位，能跑）。
    ///
    /// 场景结构照抄**本工程已经在用的**静态立绘场景（<c>ArtGenerator</c> 生成战斗立绘 / 商店立绘的那段）：
    /// 根节点挂本体脚本 <c>NCreatureVisuals</c>，子节点叫 <c>Visuals</c>（Sprite2D）。
    /// 额外再加四个本体 <c>NCreatureVisuals._Ready()</c> 硬编码 GetNode 要的唯一名节点
    /// <c>%Visuals</c> / <c>%Bounds</c> / <c>%CenterPos</c> / <c>%IntentPos</c>
    /// —— 那里是 <c>GetNode</c>（不是 GetNodeOrNull），少一个就会抛异常，
    /// 所以本体的 <c>fallback.tscn</c> 也有这四个。
    /// </summary>
    public static void GenerateVisuals(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        foreach (var d in All(p))
        {
            if (!d.HasImage) continue;
            string entryLower = d.Entry.ToLowerInvariant();
            string dstPng = Path.Combine(projectRoot, ImageRelPath(d).Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dstPng)!);
            File.Copy(d.Spec.Image!, dstPng, overwrite: true);

            // 和「静态立绘」同样的处理：图太高就等比缩到 ~220px，并让底边落在 y = 0（战斗中的地面线）。
            // 不做的话，一张上千像素的图会有一大半画到地面线以下（看起来就是「宠物不见了」）。
            var (w, h) = ArtGenerator.ReadPngSize(dstPng, 128, 128);
            double scale = h > 220 ? 220.0 / h : 1.0;
            if (scale < 0.02) scale = 0.02;
            int spriteH = (int)Math.Round(h * scale);
            int spriteW = (int)Math.Round(w * scale);
            string scaleLine = Math.Abs(scale - 1.0) < 0.001
                ? ""
                : $"scale = Vector2({Num(scale)}, {Num(scale)})";

            string scene = Path.Combine(projectRoot, SceneRelPath(d).Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(scene)!);
            ProjectFilesGen.WriteText(scene, $"""
[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://src/Core/Nodes/Combat/NCreatureVisuals.cs" id="1_visuals"]
[ext_resource type="Texture2D" path="res://{ImageRelPath(d)}" id="2_tex"]

[node name="{d.ClassName}Visuals" type="Node2D"]
script = ExtResource("1_visuals")

[node name="Visuals" type="Sprite2D" parent="."]
unique_name_in_owner = true
position = Vector2(0, -{spriteH / 2})
{scaleLine}
texture = ExtResource("2_tex")

[node name="Bounds" type="Control" parent="."]
unique_name_in_owner = true
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = {-spriteW / 2 - 20}
offset_top = -{spriteH + 40}
offset_right = {spriteW / 2 + 20}
offset_bottom = -1.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2

[node name="CenterPos" type="Marker2D" parent="."]
unique_name_in_owner = true
position = Vector2(-1, -{spriteH / 2})

[node name="IntentPos" type="Marker2D" parent="."]
unique_name_in_owner = true
position = Vector2(2, -{spriteH + 60})
""");
            log?.Invoke($"  召唤物视觉：{d.DisplayName} → {ImageRelPath(d)} + {SceneRelPath(d)}（{w}×{h}，按 {Num(scale)} 缩放）");
        }
    }

    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    // ==================== 本地化 ====================

    /// <summary>
    /// 召唤物名字的本地化表：<c>localization/zhs/monsters.json</c> 的 <c>&lt;ENTRY&gt;.name</c>
    /// （本体 <c>MonsterModel.Title</c> 读的就是 <c>&lt;Id.Entry&gt;.name</c>，例如 <c>OSTY.name</c>）。
    /// 写进本体的 <c>monsters</c> 表是安全的：本体加载时按「同名文件、逐键合并」，
    /// 我们只加自己的键，不动本体任何怪物的名字。多只召唤物都在同一张表里。
    /// </summary>
    public static string MonstersJson(CharacterProfile p)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var d in All(p)) dict[d.Entry + ".name"] = d.DisplayName;
        return System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    // ==================== C# 源码 ====================

    /// <summary>生成里的临时变量名（宠物 / 守卫 Power 的局部变量）。</summary>
    private static string VarOf(string className) => "__" + char.ToLowerInvariant(className[0]) + className.Substring(1);

    /// <summary>
    /// <c>cs/Pet.cs</c>：**所有启用的召唤物**（每只一个 <c>MonsterModel</c> 子类 + 一个 <c>&lt;X&gt;Cmd</c>
    /// + 勾了「替主人挨打」时一个守卫 Power 类）。
    ///
    /// 为什么都放一个文件：本体的 <c>ModelDb</c> 是按类型扫的，放几个文件都一样；
    /// 而「从工程恢复存档」要能按固定顺序把列表读回来，一个文件里按顺序排列最省事
    /// （<c>ProjectRecovery.ParseSummon</c> 就是按这个文件里类的出现顺序读的）。
    /// </summary>
    public static string Source(CharacterProfile p)
    {
        var n = Naming.From(p);
        var defs = All(p);

        var w = new CodeWriter();
        w.Line("// <auto-generated> 召唤物（宠物本体 + 召唤命令助手） </auto-generated>")
         .Line($"namespace {n.Namespace};")
         .Line();

        foreach (var d in defs) EmitPetClass(w, d);
        foreach (var d in defs) EmitSummonCmd(w, d);
        foreach (var d in defs.Where(x => x.Guardian)) EmitGuardianPower(w, d);
        return w.ToString();
    }

    /// <summary>一只宠物的 <c>MonsterModel</c> 子类（含站位覆写 + 血条覆写）。</summary>
    private static void EmitPetClass(CodeWriter w, PetDef d)
    {
        w.Line("/// <summary>")
         .Line($"/// 召唤物「{d.DisplayName}」的兽体（本体的 MonsterModel 子类）。")
         .Line("///")
         .Line("/// 为什么不需要注册：本体的 ModelDb 会扫模组程序集里的 AbstractModel 子类，")
         .Line("/// 按**类名**（忽略命名空间）算模型 ID，用 Activator.CreateInstance 造实例 ——")
         .Line("/// 所以这里必须有 public 无参构造函数，而且类名不能和本体的怪物重名、也不能和别的召唤物重名")
         .Line("///（会抛 DuplicateModelException）。")
         .Line("///")
         .Line("/// 为什么没有自主回合：宠物不是敌人，本体不会给它排行动；照抄本体 Osty 的做法 ——")
         .Line("/// 唯一一个 move 是空操作并且自循环（NOTHING_MOVE），实际行为全部由玩家的「伙伴攻击」卡驱动。")
         .Line("///")
         .Line("/// 注意：战斗结束时本体 PlayerCombatState.AfterCombatEnd() 会清空 _pets，所以宠物**不跨战斗**，")
         .Line("/// 每场战斗都要重新召唤（这是本体机制，宠物也不会进存档）。")
         .Line("/// </summary>")
         .Open($"public sealed class {d.ClassName} : MonsterModel")
         .Line($"/// <summary>召唤时的生命（「召唤物」页里配置的 {d.Hp}）。</summary>")
         .Line($"private const int BaseHp = {d.Hp};")
         .Line()
         .Line("/// <summary>站位距离：摆在「主人 X + 这个距离」处（「召唤物」页里配置的）。</summary>")
         .Line($"private const float StandDistance = {Lit.Float(d.StandDistance)};")
         .Line()
         .Line("public override int MinInitialHp => BaseHp;")
         .Line()
         .Line("public override int MaxInitialHp => BaseHp;")
         .Line()
         .Line("/// <summary>")
         .Line("/// 血条可见性（照本体 Osty 的写法）。")
         .Line("///")
         .Line("/// 注意：这一条**只决定建节点那一刻**要不要开交互 —— 本体 NCreature._Ready 里")
         .Line("/// 就一句 ToggleIsInteractable(Entity.Monster.IsHealthBarVisible)。")
         .Line("/// 战斗中召唤出来的宠物随后会被 NCombatRoom.AddCreature 无条件换成「不可交互」")
         .Line("///（那一句对所有非 Osty 宠物都执行），所以真正让血条出现的是召唤命令里那次")
         .Line("/// NCombatRoom.SetCreatureIsInteractable(pet, on: true)。两边都要有。")
         .Line("/// </summary>")
         .Line("public override bool IsHealthBarVisible => base.Creature.IsAlive;")
         .Line()
         .Line("/// <summary>")
         .Line("/// 自己决定站哪儿（本体的默认摆位是给「敌方小怪」用的）。")
         .Line("///")
         .Line("/// 为什么在钩子里改位置就够、不会被本体抢回去：本体的 CreatureCmd.Add 顺序是")
         .Line("///   combatState.AddCreature → CombatManager.AddCreature → NCombatRoom.AddCreature（摆位）")
         .Line("///   → await CombatManager.AfterCreatureAdded → await Hook.AfterCreatureAddedToCombat（这里）")
         .Line("/// 也就是「摆位在前、这个钩子在后」，钩子里改完就是最终位置。")
         .Line("/// 本体给非 Osty 宠物写的是 主人.X − 20 + 半个包围盒宽（单只时 ≈ 主人.X + 20），几乎叠在主人身上。")
         .Line("/// </summary>")
         .Open("public override Task AfterCreatureAddedToCombat(Creature creature)")
         .Line("// 钩子是广播给战斗里所有模型的，只处理自己这一只")
         .Line("if (creature != base.Creature) return Task.CompletedTask;")
         .Line("MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom? room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;")
         .Line("MegaCrit.Sts2.Core.Nodes.Combat.NCreature? me = room?.GetCreatureNode(base.Creature);")
         .Line("MegaCrit.Sts2.Core.Nodes.Combat.NCreature? owner = room?.GetCreatureNode(base.Creature.PetOwner?.Creature);")
         .Line("if (me is null || owner is null) return Task.CompletedTask;")
         .Line()
         .Line("// +半个包围盒宽：本体的 Position 是节点原点（贴图底边中点），不减这一半会有一半身子压在主人身上")
         .Line("me.Position = new Vector2(owner.Position.X + StandDistance + me.Visuals.Bounds.Size.X * 0.5f, owner.Position.Y - 25f);")
         .Line("return Task.CompletedTask;")
         .Close();

        if (d.HasImage)
        {
            w.Line()
             .Line("/// <summary>")
             .Line($"/// 视觉场景：<c>{SceneRelPath(d)}</c>（由你上传的宠物图生成）。")
             .Line("/// 根节点挂着本体的 NCreatureVisuals，里面有它 GetNode 硬编码要的四个唯一名节点")
             .Line("/// %Visuals / %Bounds / %CenterPos / %IntentPos —— 换场景时这四个名字不能改。")
             .Line("/// </summary>")
             .Line($"protected override string VisualsPath => \"res://{SceneRelPath(d)}\";");
        }
        else
        {
            w.Line()
             .Line("// 没有自定义图片：不 override VisualsPath，让本体按约定去找")
             .Line($"// res://scenes/creature_visuals/{d.Entry.ToLowerInvariant()}.tscn；")
             .Line("// 找不到会回退 creature_visuals/fallback（一张静态 error.png）—— 能正常显示、能打、能死。")
             .Line("// 想换成自己的图：到「召唤物」页里上传一张 PNG 再重新生成。");
        }

        w.Line()
         .Line("/// <summary>没有自主回合：唯一一个 move 是空操作，并且自循环（本体 Osty 的做法）。</summary>")
         .Open("protected override MonsterMoveStateMachine GenerateMoveStateMachine()")
         .Line("MoveState nothing = new MoveState(\"NOTHING_MOVE\", (IReadOnlyList<Creature> _) => Task.CompletedTask);")
         .Line("nothing.FollowUpState = nothing;   // 自循环：不这么做的话它的回合会没有下一步可走")
         .Line("// 注意：这里用普通的集合表达式 —— 反编译源码里那个 _003C_003Ez__ReadOnlySingleElementList")
         .Line("// 是编译器的合成类型名，游戏程序集里没有这个公开类型，抄过来会 CS0400 编不过。")
         .Line("return new MonsterMoveStateMachine([nothing], nothing);")
         .Close()
         .Close()
         .Line();
    }

    /// <summary>一只召唤物的 <c>&lt;X&gt;Cmd</c>：查询 + 召唤（含站位 / 血条 / 可选守卫 Power）。</summary>
    private static void EmitSummonCmd(CodeWriter w, PetDef d)
    {
        string cmd = d.ClassName + "Cmd";
        string petVar = VarOf(d.ClassName);

        w.Line("/// <summary>")
         .Line($"/// {d.DisplayName}的召唤 / 查询命令。")
         .Line("///")
         .Line("/// 为什么不直接用 OstyCmd.Summon：那个是奥斯提专用包装，内部硬编码 Osty 类型（非 Osty 会抛异常）；")
         .Line("/// 也不要用 AttackCommand.FromOsty（硬校验 osty.Monster is Osty）。")
         .Line("/// 这里走的是本体通用的 PlayerCmd.AddPet&lt;T&gt;，Byrdpip / Pael's Legion 就是这么做的。")
         .Line("/// </summary>")
         .Open($"public static class {cmd}")
         .Line("/// <summary>已召唤的、还活着的宠物（没召唤过 / 已经死了 → null）。</summary>")
         .Open("public static Creature? Get(Player player)")
         .Line("// PlayerCombatState.GetPet<T>() 就是本体自己的查法（Pets 里第一个躺着的同类型宠物）")
         .Line($"Creature? pet = player.PlayerCombatState?.GetPet<{d.ClassName}>();")
         .Line("return pet is { IsAlive: true } ? pet : null;")
         .Close()
         .Line()
         .Line("/// <summary>")
         .Line("/// 召唤伙伴。已经有一只活着的就<b>加最大生命</b>（不重复召唤，照本体 OstyCmd.Summon 的思路）；")
         .Line("/// 没有就新建一只，并把最大生命与当前生命都设成 hp。")
         .Line("/// </summary>")
         .Line($"/// <param name=\"hp\">召唤时的生命。&lt;= 0 时退回「召唤物」页里配置的 {d.Hp}。</param>")
         .Open("public static async Task<Creature> Summon(PlayerChoiceContext choiceContext, Player player, decimal hp)")
         .Line($"hp = hp > 0m ? hp : {d.Hp}m;")
         .Line()
         .Line("Creature? alive = Get(player);")
         .Open("if (alive is not null)")
         .Line("// 已经在场：加最大生命（本体 OstyCmd 对活着的 Osty 就是这么做的 —— 同名牌叠着打不会浪费）")
         .Line("await CreatureCmd.GainMaxHp(alive, hp);")
         .Line("// 顺手回满：GainMaxHp 只抬上限，当前生命不会跟着涨，玩家会觉得「加了上限却还是在残血」")
         .Line("await CreatureCmd.Heal(alive, Math.Max(0m, alive.MaxHp - alive.CurrentHp));")
         .Line("// 血条也照开一次：万一它是「上一只死了、这一只刚复活」之类的路径进来的")
         .Line("MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.SetCreatureIsInteractable(alive, on: true);")
         .Line("return alive;")
         .Close()
         .Line()
         .Line("// 没有（或已经死了）：新召一只。死了的那只本体已经从 _pets 里摘掉了，所以 Get 返回 null。")
         .Line($"Creature {petVar} = await PlayerCmd.AddPet<{d.ClassName}>(player);")
         .Line()
         .Line("// 本体 NewCreature 给的是 MinInitialHp..MaxInitialHp 之间的随机值，所以这里要把上限和当前值都写成 hp：")
         .Line("//   · SetMaxHp 只改上限；")
         .Line("//   · Heal(上限 − 当前) 把当前值补到上限，顺带会走一遍本体的治疗钩子（血条/动画都会刷新）。")
         .Line("// 血量必须 > 0：AttackCommand.Execute 开头就是 if (Attacker.IsDead) return this（静默早退，怎么打都不掉血）。")
         .Line($"await CreatureCmd.SetMaxHp({petVar}, hp);")
         .Line($"await CreatureCmd.Heal({petVar}, Math.Max(0m, hp - {petVar}.CurrentHp));")
         .Line()
         .Line("// 血条：本体 NCombatRoom.AddCreature 对非 Osty 宠物无条件 ToggleIsInteractable(false)，")
         .Line("// 而 NCreature._Ready 只在建节点那一刻按 IsHealthBarVisible 设过一次 ——")
         .Line("// 战斗中召唤出来的宠物因此永远看不到血条（用户实测）。这里补开一次就正常了。")
         .Line("// SetCreatureIsInteractable 是本体公开 API（本体 ReattachPower 就是这么用的），不是补丁。")
         .Line($"MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.SetCreatureIsInteractable({petVar}, on: true);");

        if (d.Guardian)
        {
            string power = GuardianPowerClassOf(d.ClassName);
            w.Line()
             .Line("// 替主人挨打：挂一个守卫 Power（照本体 DieForYouPower 写）。")
             .Line("// 先查一次防止重复挂 —— PowerStackType.Single 的状态重复 Apply 不会有第二个实例，但多一次施加会多播一次特效。")
             .Open($"if (!{petVar}.HasPower<{power}>())")
             .Line($"await PowerCmd.Apply<{power}>(choiceContext, {petVar}, 1m, null, null);")
             .Close();
        }

        w.Line($"return {petVar};")
         .Close()
         .Close()
         .Line();
    }

    /// <summary>
    /// 「替主人挨打」用的守卫 Power：照抄本体 <c>DieForYouPower</c>。
    ///
    /// 为什么每个召唤物一份类：本体状态是**按类名**注册的（<c>ModifyUnblockedDamageTarget</c> 判的是
    /// 「目标是不是自己的 <c>Owner.PetOwner?.Creature</c>」），一份类只服务一只宠物最直观；
    /// 而且整个存档只允许有一只宠物勾这个开关（见 <see cref="SummonSpec.TakesDamageForOwner"/> 的注释：
    /// 本体的重定向是链式遍历，两个重定向者会让伤害归属不可预期）。
    /// </summary>
    private static void EmitGuardianPower(CodeWriter w, PetDef d)
    {
        string power = GuardianPowerClassOf(d.ClassName);

        w.Line("/// <summary>")
         .Line($"/// 「替主人挨打」：{d.DisplayName}挡在主人前面（照抄本体 DieForYouPower）。")
         .Line("///")
         .Line("/// 只吸「可格挡的攻击伤害」（ValueProp.IsPoweredAttack()）—— 中毒、失去生命这类穿盾伤害照旧打在主人身上，")
         .Line("/// 否则宠物会变成无敌护盾。它自己死了以后战斗结束也不会被挪走")
         .Line("///（ShouldCreatureBeRemovedFromCombatAfterDeath），但宠物本来就不跨战斗。")
         .Line("/// </summary>")
         .Open($"public sealed class {power} : PowerModel")
         .Line("public override PowerType Type => PowerType.Buff;")
         .Line()
         .Line("public override PowerStackType StackType => PowerStackType.Single;")
         .Line()
         .Line("/// <summary>它身上不需要飘一个状态图标（本体 DieForYouPower 也是关掉的）。</summary>")
         .Line("public override bool ShouldPlayVfx => false;")
         .Line()
         .Line("/// <summary>把打在主人身上的可格挡攻击伤害改到自己身上。</summary>")
         .Open("public override Creature ModifyUnblockedDamageTarget(Creature target, decimal amount, ValueProp props, Creature? dealer)")
         .Line("// 不是自己的主人：放过（别去动别人的伤害）")
         .Line("if (target != base.Owner.PetOwner?.Creature) return target;")
         .Line("// 自己已经死了：放过（死的宠物挡不了刀）")
         .Line("if (base.Owner.IsDead) return target;")
         .Line("// 只有「可格挡的攻击伤害」才吸：中毒 / 失去生命这类穿盾伤害照旧打在主人身上")
         .Line("if (!props.IsPoweredAttack()) return target;")
         .Line("return base.Owner;")
         .Close()
         .Line()
         .Line("/// <summary>自己死了以后不再接受攻击（本体 DieForYouPower 的写法）。</summary>")
         .Open("public override bool ShouldAllowHitting(Creature creature)")
         .Line("return creature.IsAlive;")
         .Close()
         .Line()
         .Line("/// <summary>自己死了也不从战斗里挪走（主人还活着）。</summary>")
         .Open("public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(Creature creature)")
         .Line("if (creature != base.Owner) return true;")
         .Line("return false;")
         .Close()
         .Line()
         .Line("/// <summary>主人死了这个状态也不摘（本体 DieForYouPower 的写法）。</summary>")
         .Open("public override bool ShouldPowerBeRemovedAfterOwnerDeath()")
         .Line("return false;")
         .Close()
         .Close()
         .Line();
    }

    /// <summary>
    /// <c>cs/PetAttackExtensions.cs</c>：把「伙伴攻击」的攻击者从玩家换成宠物。
    ///
    /// 为什么需要它（本体源码 <c>Core/Commands/Builders/AttackCommand.cs</c> 为证）：
    ///   · <c>FromMonster</c>（257-267）把来源标成 <c>SourceType.Monster</c>，而
    ///     <c>GetPossibleTargets()</c>（163-182）在 <c>_sourceType == Monster</c> 时**硬编码返回
    ///     <c>_combatState.PlayerCreatures</c>** —— 玩家侧的宠物用它会去打玩家自己人；
    ///     它还顺手 <c>TargetingAllOpponents</c>（266）把 <c>_combatState</c> 设上，
    ///     于是 <c>Targeting</c>（273-286）会抛 "Already set to target opponents of attacker"；
    ///   · 反过来先 <c>Targeting</c> 再 <c>FromMonster</c>，<c>TargetingAllOpponents</c> 又抛 "Targets already set."；
    ///   · <c>FromOsty</c>（238-251）才是正确形状（攻击者换成宠物、来源仍是 Card），但它硬校验
    ///     <c>osty.Monster is Osty</c>，我们的宠物用不了。
    /// 所以：先走正常卡牌路径（<c>FromCard</c> 把 <c>_sourceType</c> 设成 <c>Card</c>，攻击历史 / hook 都正常），
    /// 再用反射把 <c>Attacker</c> 换成宠物 —— <c>Attacker</c> 是 <c>public Creature? Attacker { get; private set; }</c>
    /// （<c>:110</c>），反射的 <c>SetValue</c> 能写 private setter。
    /// 之后 <c>GetPossibleTargets()</c> 走 <c>:179 GetOpponentsOf(Attacker)</c>（宠物是玩家侧 → 拿到敌人），
    /// <c>Execute</c> 里 <c>dealer: Attacker</c> / <c>cardSource: ModelSource as CardModel</c>（<c>:669</c>）也正确。
    /// **不需要任何 Harmony 补丁。**
    /// </summary>
    public static string AttackExtensionsSource()
    {
        var w = new CodeWriter();
        w.Line("// <auto-generated> 让召唤物当攻击者（「伙伴攻击」用） </auto-generated>")
         .Line("// 注意：这个文件自己带 using —— AttackCommand 在 MegaCrit.Sts2.Core.Commands.Builders，")
         .Line("// 而生成的 GlobalUsings.cs 里没有那个命名空间（卡牌代码只用到 DamageCmd，不需要它）。")
         .Line("using MegaCrit.Sts2.Core.Commands.Builders;")
         .Line()
         .Line("namespace Sts2CharForge.Pets;")
         .Line()
         .Line("/// <summary>")
         .Line("/// AttackCommand 的扩展：把攻击者换成我们的宠物。")
         .Line("///")
         .Line("/// 用法（生成器产出的样子）：")
         .Line("///   await DamageCmd.Attack(base.DynamicVars[\"PetDamage\"].BaseValue)")
         .Line("///       .FromCard(this, cardPlay)        // 来源 = 这张牌，攻击历史与 hook 都正常")
         .Line("///       .Targeting(cardPlay.Target)      // 单体；全体 / 随机各有对应写法")
         .Line("///       .FromPetAttacker(__pet)          // ← 把 Attacker 换成宠物")
         .Line("///       .WithHitFx(\"vfx/vfx_attack_slash\")")
         .Line("///       .Execute(choiceContext);")
         .Line("///")
         .Line("/// 为什么用反射而不是 FromMonster：参见本文件上面的长注释（FromMonster 会去打自己人）。")
         .Line("/// 为什么不用 Harmony 补丁：反射改一个 private setter 就够了，补丁会牵连所有模型。")
         .Line("/// </summary>")
         .Open("public static class PetAttackExtensions")
         .Line("/// <summary>把这条攻击的攻击者换成 <paramref name=\"pet\"/>（来源仍是那张卡）。</summary>")
         .Open("public static AttackCommand FromPetAttacker(this AttackCommand command, Creature pet)")
         .Line("System.Reflection.PropertyInfo? prop = typeof(AttackCommand).GetProperty(\"Attacker\",")
         .Line("    System.Reflection.BindingFlags.Instance")
         .Line("    | System.Reflection.BindingFlags.Public")
         .Line("    | System.Reflection.BindingFlags.NonPublic);")
         .Line("// private setter 反射可以直接设（本体这个属性是 public … { get; private set; }）")
         .Line("prop?.SetValue(command, pet);")
         .Line("return command;")
         .Close()
         .Close();
        return w.ToString();
    }
}
