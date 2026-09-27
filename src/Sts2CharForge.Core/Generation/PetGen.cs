using System.Globalization;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 「召唤伙伴」的公共逻辑：宠物类名、本地化键、宠物类与命令助手的源码、最小视觉场景。
///
/// 为什么集中在一个文件里：宠物类、召唤命令、本地化键、视觉场景这四样必须严格对齐 ——
/// 生成代码里 <c>MonsterModel</c> 子类的**类名**会变成游戏里的模型 ID（本体 <c>ModelDb</c> 只按类名算，
/// 忽略命名空间），本地化键 <c>&lt;ENTRY&gt;.name</c> 和视觉场景名又都是从这个类名推出来的；
/// 只要有一处对不上，游戏里就会「宠物没名字」或者「找不到视觉场景」。
///
/// 走的是什么机制（都在本体的公开 API 上，**不需要任何 Harmony 补丁**）：
///   · 上场：<c>PlayerCmd.AddPet&lt;T&gt;(player)</c>（本体的通用宠物 API，Byrdpip / Pael's Legion 就是这么用的）；
///   · 注册：本体 <c>ModelDb</c> 会扫模组程序集里的 <c>AbstractModel</c> 子类，**只要 public 无参构造函数**；
///   · 回合：宠物没有自主回合，照抄 <c>Osty</c> 的自循环 <c>NOTHING_MOVE</c>（空操作），
///           实际靠玩家出「伙伴攻击」卡驱动；
///   · 名字：<c>localization/zhs/monsters.json</c> 的 <c>&lt;ENTRY&gt;.name</c>；
///   · 外观：<c>MonsterModel.VisualsPath</c> 默认就是 <c>scenes/creature_visuals/&lt;entry 小写&gt;.tscn</c>，
///           没有这个文件会回退本体的 <c>creature_visuals/fallback</c>（一张静态 error.png，能跑）。
///
/// 这一档**不做**替死 / 专属站位 / 随血量缩放 / 跨战斗保留 —— 那些都要补丁（见设计文档第二档）。
/// </summary>
public static class PetGen
{
    /// <summary>「角色」页里配置的召唤生命（没填或填了非正数就按 1 兜底，否则宠物一上场就是死的）。</summary>
    public static int BaseHp(CharacterProfile p) => Math.Max(1, p.Summon?.Hp ?? 0);

    /// <summary>宠物英文类名：用户填了就用用户的，留空自动 <c>&lt;角色类名&gt;Pet</c>。</summary>
    public static string ClassNameOf(CharacterProfile p)
    {
        string cls = (p.Summon?.ClassName ?? "").Trim();
        if (Naming.IsValidIdentifier(cls)) return cls;
        return Naming.From(p).CharClass + "Pet";
    }

    /// <summary>宠物类名 → 游戏里的模型 ID / 本地化键前缀（<c>MyPet</c> → <c>MY_PET</c>）。</summary>
    public static string EntryOf(CharacterProfile p) => Naming.EntryOf(ClassNameOf(p));

    /// <summary>宠物名牌上的名字（没填就用类名，至少不会是空名牌）。</summary>
    public static string DisplayName(CharacterProfile p)
    {
        string name = (p.Summon?.Name ?? "").Trim();
        return name.Length > 0 ? name : ClassNameOf(p);
    }

    /// <summary>这个配置要不要生成宠物相关的一切。</summary>
    public static bool IsActive(CharacterProfile p) => p.Summon is { Enabled: true };

    // ==================== 视觉 ====================

    /// <summary>用户上传的宠物图在工程里的相对路径（res:// 下）。</summary>
    public static string ImageRelPath(CharacterProfile p) =>
        "images/monsters/" + Naming.EntryOf(ClassNameOf(p)).ToLowerInvariant() + ".png";

    /// <summary>宠物视觉场景在工程里的相对路径（和 <c>MonsterModel.VisualsPath</c> 的默认约定一致）。</summary>
    public static string SceneRelPath(CharacterProfile p) =>
        "scenes/creature_visuals/" + Naming.EntryOf(ClassNameOf(p)).ToLowerInvariant() + ".tscn";

    /// <summary>用户确实上传了可用的宠物图片吗。</summary>
    public static bool HasImage(CharacterProfile p)
    {
        string? img = p.Summon?.Image;
        return !string.IsNullOrWhiteSpace(img) && File.Exists(img);
    }

    /// <summary>
    /// 把宠物图片拷进工程，并生成一个最小的 <c>scenes/creature_visuals/&lt;entry&gt;.tscn</c>。
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
        if (!IsActive(p) || !HasImage(p)) return;

        string dstPng = Path.Combine(projectRoot, ImageRelPath(p).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dstPng)!);
        File.Copy(p.Summon!.Image!, dstPng, overwrite: true);

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

        string scene = Path.Combine(projectRoot, SceneRelPath(p).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(scene)!);
        ProjectFilesGen.WriteText(scene, $"""
[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://src/Core/Nodes/Combat/NCreatureVisuals.cs" id="1_visuals"]
[ext_resource type="Texture2D" path="res://{ImageRelPath(p)}" id="2_tex"]

[node name="{ClassNameOf(p)}Visuals" type="Node2D"]
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
        log?.Invoke($"  宠物视觉：{ImageRelPath(p)} + {SceneRelPath(p)}（{w}×{h}，按 {Num(scale)} 缩放）");
    }

    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    // ==================== 本地化 ====================

    /// <summary>
    /// 宠物名字的本地化表：<c>localization/zhs/monsters.json</c> 的 <c>&lt;ENTRY&gt;.name</c>
    /// （本体 <c>MonsterModel.Title</c> 读的就是 <c>&lt;Id.Entry&gt;.name</c>，例如 <c>OSTY.name</c>）。
    /// 写进本体的 <c>monsters</c> 表是安全的：本体加载时按「同名文件、逐键合并」，
    /// 我们只加自己的键，不动本体任何怪物的名字。
    /// </summary>
    public static string MonstersJson(CharacterProfile p) =>
        System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string> { [EntryOf(p) + ".name"] = DisplayName(p) },
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });

    // ==================== C# 源码 ====================

    /// <summary>
    /// <c>cs/Pet.cs</c>：宠物本体（<c>MonsterModel</c> 子类）+ 召唤 / 查询命令助手。
    /// 只有「角色」页勾了「启用召唤伙伴」才生成。
    /// </summary>
    public static string Source(CharacterProfile p)
    {
        var n = Naming.From(p);
        string cls = ClassNameOf(p);
        int hp = BaseHp(p);
        bool hasScene = HasImage(p);

        var w = new CodeWriter();
        w.Line("// <auto-generated> 召唤伙伴（宠物本体 + 召唤命令助手） </auto-generated>")
         .Line($"namespace {n.Namespace};")
         .Line()
         .Line("/// <summary>")
         .Line($"/// 召唤伙伴「{DisplayName(p)}」的兽体（本体的 MonsterModel 子类）。")
         .Line("///")
         .Line("/// 为什么不需要注册：本体的 ModelDb 会扫模组程序集里的 AbstractModel 子类，")
         .Line("/// 按**类名**（忽略命名空间）算模型 ID，用 Activator.CreateInstance 造实例 ——")
         .Line("/// 所以这里必须有 public 无参构造函数，而且类名不能和本体的怪物重名（会抛 DuplicateModelException）。")
         .Line("///")
         .Line("/// 为什么没有自主回合：宠物不是敌人，本体不会给它排行动；照抄本体 Osty 的做法 ——")
         .Line("/// 唯一一个 move 是空操作并且自循环（NOTHING_MOVE），实际行为全部由玩家的「伙伴攻击」卡驱动")
         .Line("/// （DamageCmd.Attack(n).FromMonster(pet.Monster)）。")
         .Line("///")
         .Line("/// 注意：战斗结束时本体 PlayerCombatState.AfterCombatEnd() 会清空 _pets，所以宠物**不跨战斗**，")
         .Line("/// 每场战斗都要重新召唤（这是本体机制，宠物也不会进存档）。")
         .Line("/// </summary>")
         .Open($"public sealed class {cls} : MonsterModel")
         .Line($"/// <summary>召唤时的生命（「角色」页里配置的 {hp}）。</summary>")
         .Line($"private const int BaseHp = {hp};")
         .Line()
         .Line("public override int MinInitialHp => BaseHp;")
         .Line()
         .Line("public override int MaxInitialHp => BaseHp;");

        if (hasScene)
        {
            w.Line()
             .Line("/// <summary>")
             .Line($"/// 视觉场景：<c>{SceneRelPath(p)}</c>（由你上传的宠物图生成）。")
             .Line("/// 根节点挂着本体的 NCreatureVisuals，里面有它 GetNode 硬编码要的四个唯一名节点")
             .Line("/// %Visuals / %Bounds / %CenterPos / %IntentPos —— 换场景时这四个名字不能改。")
             .Line("/// </summary>")
             .Line($"protected override string VisualsPath => \"res://{SceneRelPath(p)}\";");
        }
        else
        {
            w.Line()
             .Line("// 没有自定义图片：不 override VisualsPath，让本体按约定去找")
             .Line($"// res://scenes/creature_visuals/{Naming.EntryOf(cls).ToLowerInvariant()}.tscn；")
             .Line("// 找不到会回退 creature_visuals/fallback（一张静态 error.png）—— 能正常显示、能打、能死。")
             .Line("// 想换成自己的图：到「角色」页的「召唤伙伴」里上传一张 PNG 再重新生成。");
        }

        w.Line()
         .Line("/// <summary>没有自主回合：唯一一个 move 是空操作，并且自循环（本体 Osty 的做法）。</summary>")
         .Open("protected override MonsterMoveStateMachine GenerateMoveStateMachine()")
         .Line("MoveState nothing = new MoveState(\"NOTHING_MOVE\", (IReadOnlyList<Creature> _) => Task.CompletedTask);")
         .Line("nothing.FollowUpState = nothing;   // 自循环：不这么做的话它的回合会没有下一步可走")
         .Line("return new MonsterMoveStateMachine([nothing], nothing);")
         .Close()
         .Close()
         .Line()
         .Line("/// <summary>")
         .Line($"/// {DisplayName(p)}的召唤 / 查询命令。")
         .Line("///")
         .Line("/// 为什么不直接用 OstyCmd.Summon：那个是奥斯提专用包装，内部硬编码 Osty 类型（非 Osty 会抛异常）；")
         .Line("/// 也不要用 AttackCommand.FromOsty（硬校验 osty.Monster is Osty）。")
         .Line("/// 这里走的是本体通用的 PlayerCmd.AddPet&lt;T&gt;，Byrdpip / Pael's Legion 就是这么做的。")
         .Line("/// </summary>")
         .Open($"public static class {cls}Cmd")
         .Line("/// <summary>已召唤的、还活着的宠物（没召唤过 / 已经死了 → null）。</summary>")
         .Open("public static Creature? Get(Player player)")
         .Line("// PlayerCombatState.GetPet<T>() 就是本体自己的查法（Pets 里第一个躺着的同类型宠物）")
         .Line($"Creature? pet = player.PlayerCombatState?.GetPet<{cls}>();")
         .Line("return pet is { IsAlive: true } ? pet : null;")
         .Close()
         .Line()
         .Line("/// <summary>")
         .Line("/// 召唤伙伴。已经有一只活着的就<b>加最大生命</b>（不重复召唤，照本体 OstyCmd.Summon 的思路）；")
         .Line("/// 没有就新建一只，并把最大生命与当前生命都设成 hp。")
         .Line("/// </summary>")
         .Line("/// <param name=\"hp\">召唤时的生命。&lt;= 0 时退回「角色」页里配置的 " + hp + "。</param>")
         .Open("public static async Task<Creature> Summon(PlayerChoiceContext choiceContext, Player player, decimal hp)")
         .Line($"hp = hp > 0m ? hp : {hp}m;")
         .Line()
         .Line("Creature? alive = Get(player);")
         .Open("if (alive is not null)")
         .Line("// 已经在场：加最大生命（本体 OstyCmd 对活着的 Osty 就是这么做的 —— 同名牌叠着打不会浪费）")
         .Line("await CreatureCmd.GainMaxHp(alive, hp);")
         .Line("// 顺手回满：GainMaxHp 只抬上限，当前生命不会跟着涨，玩家会觉得「加了上限却还是在残血」")
         .Line("await CreatureCmd.Heal(alive, Math.Max(0m, alive.MaxHp - alive.CurrentHp));")
         .Line("return alive;")
         .Close()
         .Line()
         .Line("// 没有（或已经死了）：新召一只。死了的那只本体已经从 _pets 里摘掉了，所以 Get 返回 null。")
         .Line($"Creature pet = await PlayerCmd.AddPet<{cls}>(player);")
         .Line()
         .Line("// 本体 NewCreature 给的是 MinInitialHp..MaxInitialHp 之间的随机值，所以这里要把上限和当前值都写成 hp：")
         .Line("//   · SetMaxHp 只改上限；")
         .Line("//   · Heal(上限 − 当前) 把当前值补到上限，顺带会走一遍本体的治疗钩子（血条/动画都会刷新）。")
         .Line("await CreatureCmd.SetMaxHp(pet, hp);")
         .Line("await CreatureCmd.Heal(pet, Math.Max(0m, hp - pet.CurrentHp));")
         .Line("return pet;")
         .Close()
         .Close();

        return w.ToString();
    }
}
