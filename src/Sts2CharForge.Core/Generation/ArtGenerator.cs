using System.IO.Compression;
using System.Text.Json.Nodes;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>美术资源：没上传的槽位默认生成中性占位图（不含本体美术），也可选用铁甲战士素材占位；用户上传的图片覆盖对应槽位。</summary>
public static class ArtGenerator
{
    /// <summary>各槽位的建议尺寸（取自本体原图，用户没提供占位图时按这个尺寸生成中性图）。</summary>
    public static readonly (string Slot, string Suggest, int W, int H)[] SlotSpecs =
    [
        ("顶部头像",        "PNG-32 RGBA，88×88（方图，建议 176×176 或 512×512 等比）", 88, 88),
        ("头像描边",        "PNG-32 RGBA，与头像同尺寸 88×88",                          88, 88),
        ("选人界面立绘",    "PNG-32 RGBA，132×195（竖版 ≈0.677:1，建议 396×585 等比）", 132, 195),
        ("选人立绘（未解锁）", "PNG-32 RGBA，与选人立绘同尺寸 132×195（一般是剪影）",      132, 195),
        ("地图标记",        "PNG-32 RGBA，49×64（建议 98×128 等比）",                   49, 64),
        ("能量图标",        "PNG-32 RGBA，74×74（会写进 ui_atlas.tpsheet，方形）",        74, 74),
        ("选人过场贴图",    "PNG 灰度/RGBA，2560×1200（白=显示、黑=透明）",             2560, 1200),
        ("卡面（每张卡）",  "PNG，1000×760（宽屏横版，本体铁甲卡面就是 24bit 无 alpha）", 1000, 760),
        ("遗物图标（每个）", "PNG-32 RGBA，256×256（方图）",                            256, 256),
        ("多人手势 ×4",     "PNG-32 RGBA，422×1200（point/rock/paper/scissors）",       422, 1200),
    ];

    private static IEnumerable<(string Src, string Dst, Func<ArtSpec, string?>? Upload)> AssetMap()
    {
        // 说明：战斗立绘 / 商店立绘 / 篝火立绘 / 选人背景这 4 个场景原本是 Spine 骨骼动画，
        // 现已改为「静态图场景」（见 WriteStaticCharacterScenes），不再从这里复制。
        yield return ("scenes/ui/character_icons/ironclad_icon.tscn", "scenes/ui/character_icons/{slug}_icon.tscn", null);
        yield return ("scenes/combat/energy_counters/ironclad_energy_counter.tscn", "scenes/combat/energy_counters/{slug}_energy_counter.tscn", null);
        yield return ("scenes/vfx/card_trail_ironclad.tscn", "scenes/vfx/card_trail_{slug}.tscn", null);

        yield return ("images/ui/top_panel/character_icon_ironclad.png", "images/ui/top_panel/character_icon_{slug}.png", a => a.Icon);
        yield return ("images/ui/top_panel/character_icon_ironclad_outline.png", "images/ui/top_panel/character_icon_{slug}_outline.png", a => a.IconOutline);
        yield return ("images/packed/character_select/char_select_ironclad.png", "images/packed/character_select/char_select_{slug}.png", a => a.SelectIcon);
        yield return ("images/packed/character_select/char_select_ironclad_locked.png", "images/packed/character_select/char_select_{slug}_locked.png", a => a.SelectIconLocked);
        yield return ("images/packed/map/icons/map_marker_ironclad.png", "images/packed/map/icons/map_marker_{slug}.png", a => a.MapMarker);
        yield return ("images/ui/hands/multiplayer_hand_ironclad_point.png", "images/ui/hands/multiplayer_hand_{slug}_point.png", null);
        yield return ("images/ui/hands/multiplayer_hand_ironclad_rock.png", "images/ui/hands/multiplayer_hand_{slug}_rock.png", null);
        yield return ("images/ui/hands/multiplayer_hand_ironclad_paper.png", "images/ui/hands/multiplayer_hand_{slug}_paper.png", null);
        yield return ("images/ui/hands/multiplayer_hand_ironclad_scissors.png", "images/ui/hands/multiplayer_hand_{slug}_scissors.png", null);
        yield return ("images/ui/transitions/ironclad_transition.png", "images/ui/transitions/{slug}_transition.png", a => a.Transition);
    }

    public static void Generate(CharacterProfile p, string projectRoot, Action<string>? log = null)
    {
        var n = Naming.From(p);
        int copied = 0, uploaded = 0, missing = 0, neutral = 0;

        // 场景文件里引用的是本体图片路径 → 改写成我们自己的文件，否则上传的图不会生效
        var rewrite = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (srcTemplate, dstTemplate, _) in AssetMap())
            if (dstTemplate.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                rewrite["res://" + srcTemplate.Replace('\\', '/')] =
                    "res://" + dstTemplate.Replace("{slug}", n.CharSlug).Replace('\\', '/');
        rewrite["res://images/packed/sprite_fonts/ironclad_energy_icon.png"] =
            $"res://images/packed/sprite_fonts/{n.EnergyColor}_energy_icon.png";

        // 模组预览图：本体模组界面按**固定路径**找它 —— res://<模组ID>/mod_image.png
        //（NModInfoContainer.cs:39：找不到就把右侧那块留空，只显示名字/作者/版本/说明）。
        // 落点和 localization 同级（<工程>\<模组ID>\），会一起打进 PCK。
        if (!string.IsNullOrWhiteSpace(p.Art.ModImage))
        {
            if (File.Exists(p.Art.ModImage))
            {
                string modImageDst = Path.Combine(projectRoot, n.ModId, "mod_image.png");
                Directory.CreateDirectory(Path.GetDirectoryName(modImageDst)!);
                File.Copy(p.Art.ModImage!, modImageDst, overwrite: true);
                log?.Invoke("  模组预览图：已放到 " + n.ModId + "/mod_image.png（游戏「模组」界面右侧显示）");
            }
            else
            {
                log?.Invoke("  [警告] 模组预览图文件不存在，已跳过：" + p.Art.ModImage);
            }
        }

        foreach (var (srcTemplate, dstTemplate, upload) in AssetMap())
        {
            string src = Path.Combine(p.Paths.VanillaProject, srcTemplate);
            string dst = Path.Combine(projectRoot, dstTemplate.Replace("{slug}", n.CharSlug));

            string? userFile = upload?.Invoke(p.Art);
            if (!string.IsNullOrWhiteSpace(userFile) && File.Exists(userFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(userFile, dst, overwrite: true);
                uploaded++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

            if (dst.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
            {
                // 场景没法用图片代替，仍然来自本体（里面还引用本体的骨骼/VFX）
                if (!File.Exists(src)) { missing++; log?.Invoke($"  [警告] 本体缺少占位场景：{srcTemplate}"); continue; }
                string text = File.ReadAllText(src);
                foreach (var (from, to) in rewrite) text = text.Replace(from, to);
                // 去掉顶层 uid，避免与本体资源 uid 冲突（文本原样进 pck）
                text = System.Text.RegularExpressions.Regex.Replace(text, " uid=\"uid://[^\"]+\"", "");
                File.WriteAllText(dst, text, ProjectFilesGen.Utf8NoBom);
                copied++;
                continue;
            }

            if (p.Art.UseVanillaPlaceholders)
            {
                if (!File.Exists(src)) { missing++; log?.Invoke($"  [警告] 本体缺少占位资源：{srcTemplate}"); continue; }
                File.Copy(src, dst, overwrite: true);
                copied++;
            }
            else
            {
                // 中性占位图：纯色带边框，尺寸照本体原图（读不到就用 256×256）
                var (w, h) = File.Exists(src) ? ReadPngSize(src, 256, 256) : (256, 256);
                WriteNeutralPng(dst, w, h);
                neutral++;
            }
        }

        log?.Invoke($"  美术：占位复制 {copied} 个 / 用户上传 {uploaded} 个 / 中性占位 {neutral} 个 / 缺失 {missing} 个");

        WriteOutlineFromColor(p, projectRoot, log);
        WriteCardFrameMaterial(p, projectRoot, log);
        WriteEnergyCounterRecolor(p, projectRoot, log);

        WriteEnergyIcon(p, projectRoot, log);
        WriteExtraResourceIcon(p, projectRoot, log);
        WriteVanillaPowerIcons(p, projectRoot, log);
        WriteGeneratedPowerIcons(p, projectRoot, log);
        WriteStaticCharacterScenes(p, projectRoot, log);
        WriteTransitionMaterial(p, projectRoot);
        WriteCardPortraits(p, projectRoot, log);
        WriteRelicIcons(p, projectRoot, log);
        WritePotionIcons(p, projectRoot, log);
        // 注意：这里**不再**写 ui_atlas.tpsheet / potion_atlas.tpsheet ——
        // 那份图集精灵表每个模组各带一份、同路径只有一个能生效（后挂载的盖掉前面的），
        // 两个自建角色一起装就会有一个的能量图标消失。改成由生成的 AtlasSpritePatch 运行时供给。
    }

    /// <summary>
    /// 遗物图标：大图 res://images/relics/&lt;id&gt;.png，裁切纹理
    /// res://images/atlases/relic_atlas.sprites/&lt;id&gt;.tres（描边版放 relic_outline_atlas.sprites）。
    /// 找不到对应资源时游戏会自动回退到大图，所以三个文件都要给。
    /// </summary>
    private static void WriteRelicIcons(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        var n = Naming.From(p);

        // 占位用本体遗物图标（燃烧之血）
        string placeholder = Path.Combine(p.Paths.VanillaProject, "images/relics/burning_blood.png");
        if (!File.Exists(placeholder))
        {
            string dir = Path.Combine(p.Paths.VanillaProject, "images/relics");
            placeholder = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.png").FirstOrDefault() ?? ""
                : "";
        }

        for (int i = 0; i < p.Relics.Count; i++)
        {
            var r = p.Relics[i];
            string entry = Naming.EntryOf(n.RelicClassName(r, i)).ToLowerInvariant();

            string? baseSrc = !string.IsNullOrWhiteSpace(r.Icon) && File.Exists(r.Icon) ? r.Icon
                : (p.Art.UseVanillaPlaceholders && File.Exists(placeholder) ? placeholder : null);

            string? outlineSrc = !string.IsNullOrWhiteSpace(r.IconOutline) && File.Exists(r.IconOutline)
                ? r.IconOutline : baseSrc;

            string bigDst = Path.Combine(projectRoot, "images/relics", entry + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(bigDst)!);
            if (baseSrc is not null) File.Copy(baseSrc, bigDst, overwrite: true);
            else WriteNeutralPng(bigDst, 256, 256);           // 中性占位遗物图标
            var (w, h) = ReadPngSize(bigDst, 256, 256);

            ProjectFilesGen.WriteText(Path.Combine(projectRoot, "images/atlases/relic_atlas.sprites", entry + ".tres"), $"""
[gd_resource type="AtlasTexture" load_steps=2 format=3]

[ext_resource type="Texture2D" path="res://images/relics/{entry}.png" id="1_relic"]

[resource]
atlas = ExtResource("1_relic")
region = Rect2(0, 0, {w}, {h})
""");

            string outlineDst = Path.Combine(projectRoot, "images/relics", entry + "_outline.png");
            if (outlineSrc is not null) File.Copy(outlineSrc, outlineDst, overwrite: true);
            else WriteNeutralPng(outlineDst, 256, 256);
            var (ow, oh) = ReadPngSize(outlineDst, w, h);
            ProjectFilesGen.WriteText(Path.Combine(projectRoot, "images/atlases/relic_outline_atlas.sprites", entry + ".tres"), $"""
[gd_resource type="AtlasTexture" load_steps=2 format=3]

[ext_resource type="Texture2D" path="res://images/relics/{entry}_outline.png" id="1_relic"]

[resource]
atlas = ExtResource("1_relic")
region = Rect2(0, 0, {ow}, {oh})
""");
        }
        if (p.Relics.Count > 0)
            log?.Invoke($"  遗物图标：{p.Relics.Count} 个（未上传的用本体遗物图标占位）");
    }

    /// <summary>
    /// 药水图标（和遗物同样的做法）。本体查的是：
    ///   · 小图标/物品栏：atlases/potion_atlas.sprites/&lt;id&gt;.tres（AtlasTexture）
    ///   · 描边高亮：      atlases/potion_outline_atlas.sprites/&lt;id&gt;.tres（可选）
    ///   · 大图（弹窗）：  potions/large/&lt;id&gt;.png
    /// </summary>
    private static void WritePotionIcons(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        if (p.Potions.Count == 0) return;
        var n = Naming.From(p);

        // 占位：优先本体某张药水大图，找不到就用 missing_potion.png
        string placeholder = Path.Combine(p.Paths.VanillaProject, "images/potions/large/attack_potion.png");
        if (!File.Exists(placeholder))
        {
            string dir = Path.Combine(p.Paths.VanillaProject, "images/potions/large");
            placeholder = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").FirstOrDefault() ?? "" : "";
        }
        if (!File.Exists(placeholder))
            placeholder = Path.Combine(p.Paths.VanillaProject, "images/potions/missing_potion.png");

        for (int i = 0; i < p.Potions.Count; i++)
        {
            var s = p.Potions[i];
            string entry = Naming.EntryOf(n.PotionClassName(s, i)).ToLowerInvariant();

            string? baseSrc = !string.IsNullOrWhiteSpace(s.Icon) && File.Exists(s.Icon) ? s.Icon
                : (p.Art.UseVanillaPlaceholders && File.Exists(placeholder) ? placeholder : null);
            string? outlineSrc = !string.IsNullOrWhiteSpace(s.IconOutline) && File.Exists(s.IconOutline)
                ? s.IconOutline : baseSrc;

            // 大图（本体 LargeImagePath = images/potions/large/<id>.png）
            string bigDst = Path.Combine(projectRoot, "images/potions/large", entry + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(bigDst)!);
            if (baseSrc is not null) File.Copy(baseSrc, bigDst, overwrite: true);
            else WriteNeutralPng(bigDst, 128, 128);
            var (w, h) = ReadPngSize(bigDst, 128, 128);

            // 物品栏小图标（AtlasTexture 指向我们自己的大图）
            ProjectFilesGen.WriteText(Path.Combine(projectRoot, "images/atlases/potion_atlas.sprites", entry + ".tres"), $"""
[gd_resource type="AtlasTexture" load_steps=2 format=3]

[ext_resource type="Texture2D" path="res://images/potions/large/{entry}.png" id="1_potion"]

[resource]
atlas = ExtResource("1_potion")
region = Rect2(0, 0, {w}, {h})
""");

            // 描边（选中/高亮用，可选）
            string outlineDst = Path.Combine(projectRoot, "images/potions/large", entry + "_outline.png");
            if (outlineSrc is not null) File.Copy(outlineSrc, outlineDst, overwrite: true);
            else WriteNeutralPng(outlineDst, w, h);
            var (ow, oh) = ReadPngSize(outlineDst, w, h);
            ProjectFilesGen.WriteText(Path.Combine(projectRoot, "images/atlases/potion_outline_atlas.sprites", entry + ".tres"), $"""
[gd_resource type="AtlasTexture" load_steps=2 format=3]

[ext_resource type="Texture2D" path="res://images/potions/large/{entry}_outline.png" id="1_potion"]

[resource]
atlas = ExtResource("1_potion")
region = Rect2(0, 0, {ow}, {oh})
""");
        }
        log?.Invoke($"  药水图标：{p.Potions.Count} 个（未上传的用本体药水图占位，建议自己上传）");
    }

    /// <summary>
    /// 「未上传的槽位用本体素材占位」时，静态立绘用本体哪几张图。
    /// 本体这四个场景本来是 Spine 骨骼动画，但骨骼里用的就是这几张 PNG（解包工程 animations\ 下），
    /// 拿它们当静态立绘，游戏里看起来就是铁甲战士本人。
    /// </summary>
    private const string VanillaBattleArt = @"animations/characters/ironclad/ironclad.png";
    private const string VanillaShopArt = @"animations/merchant/ironclad/ironclad_shop.png";
    private const string VanillaRestArt = @"animations/rest_site/ironclad/restsite_ironclad.png";

    /// <summary>把本体某张立绘拷成我们的文件；本体工程里没有这张图时返回 false（调用方自己回退）。</summary>
    private static bool TryCopyVanillaPortrait(string? vanillaProject, string relPath, string dstPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(vanillaProject)) return false;
            string src = Path.Combine(vanillaProject, relPath.Replace('\\', Path.DirectorySeparatorChar));
            if (!File.Exists(src)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(dstPath)!);
            File.Copy(src, dstPath, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 立绘摆位：按图片高度缩放（高了缩到 ~340px，和本体角色差不多高），并让 Sprite2D 的底边落在 y = 0。
    /// 返回（缩放比例、缩放后高度、场景里那行 scale）。
    /// </summary>
    private static (double Scale, int SpriteH, string ScaleLine) SpriteFit(int w, int h)
    {
        double scale = h > 400 ? 340.0 / h : 1.0;
        if (scale < 0.05) scale = 0.05;
        int spriteH = (int)Math.Round(h * scale);
        string line = Math.Abs(scale - 1.0) < 0.001
            ? ""
            : $"scale = Vector2({scale.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}, {scale.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)})";
        return (scale, spriteH, line);
    }
    /// <summary>
    /// 「未上传的槽位用本体素材占位」时，这四个场景直接借用本体铁甲战士的场景
    /// （Spine 骨骼动画 + 粒子），游戏里看到的就是铁甲战士本人。
    /// 之前的做法是把本体的立绘 PNG 当静态图用 —— 但那几张其实是 Spine 图集
    /// （animations/characters/ironclad/ironclad.png 是 1329×269 的图集页），
    /// 画出来是一堆散件、看着就像「人都看不到」，所以改成借场景。
    /// 元组：本体场景、我们的场景、本体根节点名、我们的根节点名、中文用途。
    /// </summary>
    private static IEnumerable<(string Src, string Dst, string SrcRoot, string DstRoot, string What)> VanillaScenes(Naming n)
    {
        yield return ("scenes/creature_visuals/ironclad.tscn",
                      $"scenes/creature_visuals/{n.CharSlug}.tscn",
                      "Ironclad", n.CharClass, "战斗立绘");
        yield return ("scenes/merchant/characters/ironclad_merchant.tscn",
                      $"scenes/merchant/characters/{n.CharSlug}_merchant.tscn",
                      "IroncladMerchant", n.CharClass + "Merchant", "商店立绘");
        yield return ("scenes/rest_site/characters/ironclad_rest_site.tscn",
                      $"scenes/rest_site/characters/{n.CharSlug}_rest_site.tscn",
                      "IroncladRestSite", n.CharClass + "RestSite", "篝火立绘");
        yield return ("scenes/screens/char_select/char_select_bg_ironclad.tscn",
                      $"scenes/screens/char_select/char_select_bg_{n.CharSlug}.tscn",
                      "IroncladBg", n.CharClass + "Bg", "选人背景");
    }

    /// <summary>
    /// 本体那张「干净的单张立绘」（132×195 的选择界面小立绘）。
    /// 只在拿不到本体场景时当兜底用 —— 别拿 animations\ 下那几张 Spine 图集当立绘（会看不出人形）。
    /// </summary>
    private const string VanillaPortraitArt = "images/packed/character_select/char_select_ironclad.png";

    /// <summary>
    /// 把本体某个场景搬成我们的场景：改写里面的图片引用、去掉 uid（避免和本体资源 uid 撞车）、
    /// 顺便把根节点名换成我们的角色名。本体资源（Spine 数据、图集、VFX）都在游戏自己的 pck 里，不用我们带。
    /// </summary>
    private static bool CopyVanillaSceneAsOurs(string? vanillaProject, string srcRel, string projectRoot, string dstRel,
        string srcRoot, string dstRoot, IReadOnlyDictionary<string, string> rewrite, Action<string>? log, string what)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(vanillaProject)) return false;
            string src = Path.Combine(vanillaProject, srcRel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(src)) { log?.Invoke($"  [警告] 本体缺少{what}场景：{srcRel}"); return false; }

            string text = File.ReadAllText(src);
            foreach (var (from, to) in rewrite) text = text.Replace(from, to);
            text = System.Text.RegularExpressions.Regex.Replace(text, " uid=\"uid://[^\"]+\"", "");
            if (srcRoot != dstRoot) text = text.Replace($"name=\"{srcRoot}\"", $"name=\"{dstRoot}\"");
            string dst = Path.Combine(projectRoot, dstRel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllText(dst, text, ProjectFilesGen.Utf8NoBom);
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"  [警告] 复制本体{what}场景失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 生成四个角色场景：战斗立绘 / 商店立绘 / 篝火立绘 / 选人背景。
    /// 默认用「静态图场景」（本工具的模板 + zwt.png 占位图，用户上传的图也走这条）；
    /// 勾了「未上传的槽位用本体素材占位」且没上传立绘时，直接借本体铁甲战士的那四个场景（Spine 动画）。
    /// </summary>
    private static void WriteStaticCharacterScenes(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        var n = Naming.From(p);

        // 勾了本体占位、而且立绘/背景都没自己传 → 四个场景全用本体的（Spine 骨骼动画，能看到人）
        bool selfPortraits = (!string.IsNullOrWhiteSpace(p.Art.CharacterStatic) && File.Exists(p.Art.CharacterStatic))
                          || (!string.IsNullOrWhiteSpace(p.Art.SelectBackground) && File.Exists(p.Art.SelectBackground));
        if (p.Art.UseVanillaPlaceholders && !selfPortraits)
        {
            var rewrite0 = new Dictionary<string, string>(StringComparer.Ordinal);
            var done = new List<string>();
            foreach (var (srcRel, dstRel, srcRoot, dstRoot, what) in VanillaScenes(n))
                if (CopyVanillaSceneAsOurs(p.Paths.VanillaProject, srcRel, projectRoot, dstRel, srcRoot, dstRoot, rewrite0, log, what))
                    done.Add(what);
            if (done.Count == VanillaScenes(n).Count())
            {
                log?.Invoke($"  角色场景：用本体铁甲战士的场景占位（{string.Join(" / ", done)}，Spine 动画，游戏里就是铁甲战士）");
                return;
            }
            log?.Invoke($"  角色场景：只有 {done.Count} 个能借本体的，剩下的走静态图（{string.Join(" / ", done)}）");
        }
        else if (p.Art.UseVanillaPlaceholders)
        {
            log?.Invoke("  角色场景：你上传了立绘/背景，按上传的来（不借本体场景）");
        }

        string rel = $"images/characters/{n.CharSlug}_static.png";
        string pngPath = Path.Combine(projectRoot, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);

        // 静态立绘来源：① 界面上传 ② 「用本体素材占位」时用本体那张干净的小立绘（132×195）
        //              ③ 工具自带的占位图（内嵌在程序集里，或 exe 旁 Assets\） ④ 中性生成图
        string? userArt = p.Art.CharacterStatic;
        if (!string.IsNullOrWhiteSpace(userArt) && File.Exists(userArt))
        {
            File.Copy(userArt, pngPath, overwrite: true);
            log?.Invoke($"  静态立绘：使用上传的图片 {Path.GetFileName(userArt)}");
        }
        else if (p.Art.UseVanillaPlaceholders && TryCopyVanillaPortrait(p.Paths.VanillaProject, VanillaPortraitArt, pngPath))
        {
            log?.Invoke($"  静态立绘：用本体铁甲战士的立绘占位（{VanillaPortraitArt}，132×195，会放大显示）");
        }
        else if (TryWriteBundledPlaceholder(pngPath))
        {
            log?.Invoke("  静态立绘：使用工具自带的占位图（zwt.png），可在「美术资源」里换成自己的图");
        }
        else
        {
            WriteNeutralPng(pngPath, 281, 235);
            log?.Invoke("  静态立绘：未找到上传图片与自带占位图 → 生成中性占位图");
        }

        string merchantTexRel = rel, restTexRel = rel;   // 商店/篝火默认复用同一张静态立绘
        var (w, h) = ReadPngSize(pngPath, 281, 235);
        string tex = $"res://{rel}";
        string merchantTex = $"res://{merchantTexRel}";
        string restTex = $"res://{restTexRel}";
        string scene = n.CharSlug;

        // 关键：立绘要「脚踩地面」。
        // 本体 fallback 的固定坐标只适合它那张小图；用户上传的立绘往往高达上千像素，
        // 若仍居中画在 (4,-119)，图会有一大半落到地面线以下（看起来就是「人物不见了」）。
        // 所以：① 按图片高度缩放（高了缩到 ~340px，和本体角色差不多高）；
        //       ② 让 Sprite2D 的底边落在 y = 0（战斗中的地面线）。
        double scale = h > 400 ? 340.0 / h : 1.0;
        if (scale < 0.05) scale = 0.05;
        int spriteH = (int)Math.Round(h * scale);
        int spriteW = (int)Math.Round(w * scale);
        string scaleLine = Math.Abs(scale - 1.0) < 0.001
            ? ""
            : $"scale = Vector2({scale.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}, {scale.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)})";
        (double mScale, int mSpriteH, string mScaleLine) = SpriteFit(w, h);
        (double rScale, int rSpriteH, string rScaleLine) = SpriteFit(w, h);

        // ① 战斗立绘：Node2D + NCreatureVisuals.cs，节点名照本体 fallback.tscn
        ProjectFilesGen.WriteText(Path.Combine(projectRoot, "scenes/creature_visuals", scene + ".tscn"), $"""
[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://src/Core/Nodes/Combat/NCreatureVisuals.cs" id="1_visuals"]
[ext_resource type="Texture2D" path="{tex}" id="2_tex"]

[node name="{n.CharClass}Visuals" type="Node2D"]
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

        // ② 商店立绘：根节点必须带 NMerchantCharacter 脚本（本体 Instantiate<NMerchantCharacter>），
        //    第 0 个子节点放静态图；NMerchantCharacter 会等 Spine 就绪，等不到就只打一条警告，不会崩。
        //    同样按尺寸缩放并让底边贴近原点，避免大图跑出画面。
        ProjectFilesGen.WriteText(Path.Combine(projectRoot, "scenes/merchant/characters", scene + "_merchant.tscn"), $"""
[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://src/Core/Nodes/Screens/Shops/NMerchantCharacter.cs" id="1_merchant"]
[ext_resource type="Texture2D" path="{merchantTex}" id="2_tex"]

[node name="{n.CharClass}Merchant" type="Node2D"]
script = ExtResource("1_merchant")

[node name="Visuals" type="Sprite2D" parent="."]
position = Vector2(0, -{mSpriteH / 2})
{mScaleLine}
texture = ExtResource("2_tex")
""");

        // ③ 篝火立绘：必须保留 ControlRoot 与 %Hitbox / %SelectionReticle / %ThoughtBubbleLeft / %ThoughtBubbleRight
        ProjectFilesGen.WriteText(Path.Combine(projectRoot, "scenes/rest_site/characters", scene + "_rest_site.tscn"), $"""
[gd_scene load_steps=4 format=3]

[ext_resource type="Script" path="res://src/Core/Nodes/RestSite/NRestSiteCharacter.cs" id="1_rest"]
[ext_resource type="Texture2D" path="{restTex}" id="2_tex"]
[ext_resource type="PackedScene" path="res://scenes/ui/selection_reticle.tscn" id="3_reticle"]

[node name="{n.CharClass}RestSite" type="Node2D"]
position = Vector2(-2, 42)
scale = Vector2(0.76, 0.76)
script = ExtResource("1_rest")

[node name="Visuals" type="Sprite2D" parent="."]
position = Vector2(0, -{rSpriteH / 2})
{rScaleLine}
texture = ExtResource("2_tex")

[node name="ControlRoot" type="Control" parent="."]
layout_mode = 3
anchors_preset = 0

[node name="SelectionReticle" parent="ControlRoot" instance=ExtResource("3_reticle")]
unique_name_in_owner = true
offset_left = -153.0
offset_top = -350.0
offset_right = 267.0
offset_bottom = 320.0

[node name="Hitbox" type="Control" parent="ControlRoot"]
unique_name_in_owner = true
layout_mode = 3
anchors_preset = 0
offset_left = -155.0
offset_top = -351.0
offset_right = 266.0
offset_bottom = 332.0

[node name="ThoughtBubbleRight" type="Control" parent="ControlRoot"]
unique_name_in_owner = true
layout_mode = 3
anchors_preset = 0
offset_left = 209.209
offset_top = -317.103
offset_right = 209.209
offset_bottom = -317.103

[node name="ThoughtBubbleLeft" type="Control" parent="ControlRoot"]
unique_name_in_owner = true
layout_mode = 3
anchors_preset = 0
offset_left = -73.6836
offset_top = -324.997
offset_right = -73.6836
offset_bottom = -324.997
""");

        // ④ 选人背景：本体是「Spine 立绘 + 光点粒子」。静态版显示一张大图：
        //    优先用「选人界面背景立绘」槽位，留空则用静态立绘那张图，都没有就空场景。
        //
        //    注意：这里必须用 TextureRect 而不是 Sprite2D —— Sprite2D 作为 Control 的子节点时
        //    坐标原点是 Control 的左上角，图会跑到画面外（只看到右下四分之一）。
        //    TextureRect + 全屏锚点 + stretch_mode=5（保持比例居中）会以图片中心为基准铺满并对齐屏幕，
        //    和本体 scenes/ui/character_icons/*_icon.tscn 的写法一致。
        string bgRel = $"images/characters/{n.CharSlug}_selectbg.png";
        bool hasBg = false;
        string? bgSrc = !string.IsNullOrWhiteSpace(p.Art.SelectBackground) && File.Exists(p.Art.SelectBackground)
            ? p.Art.SelectBackground
            : (!string.IsNullOrWhiteSpace(userArt) && File.Exists(userArt) ? userArt : null);
        if (bgSrc is not null)
        {
            try { File.Copy(bgSrc, Path.Combine(projectRoot, bgRel), overwrite: true); hasBg = true; } catch { hasBg = false; }
        }

        ProjectFilesGen.WriteText(Path.Combine(projectRoot, "scenes/screens/char_select", "char_select_bg_" + scene + ".tscn"),
            hasBg
                ? $"""
[gd_scene load_steps=3 format=3]

[ext_resource type="Texture2D" path="res://{bgRel}" id="1_bg"]

[node name="{n.CharClass}Bg" type="Control"]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2

[node name="Visuals" type="TextureRect" parent="."]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
texture = ExtResource("1_bg")
expand_mode = 1
stretch_mode = 5
"""
                : $"""
[gd_scene format=3]

[node name="{n.CharClass}Bg" type="Control"]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
""");

        log?.Invoke($"  已生成 4 个静态立绘场景（无 Spine，用 {rel}，{w}×{h}"
            + (hasBg ? $"，选人背景用 {bgRel}" : "，选人背景为空（该槽位未上传）") + "）");
    }

    /// <summary>
    /// 顶部头像描边：填了颜色就按这个颜色生成描边图（用头像的透明通道做剪影），
    /// 省掉自己画一张描边 PNG。留空则保留前面复制/上传的结果。
    /// </summary>
    private static void WriteOutlineFromColor(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        string hex = CardColorSpec.NormalizeHex(p.Art.IconOutlineColor);
        if (hex.Length == 0) return;

        var n = Naming.From(p);
        string iconPath = Path.Combine(projectRoot, "images/ui/top_panel", $"character_icon_{n.CharSlug}.png");
        string outlinePath = Path.Combine(projectRoot, "images/ui/top_panel", $"character_icon_{n.CharSlug}_outline.png");
        if (!File.Exists(iconPath)) { log?.Invoke("  [警告] 找不到头像，跳过描边上色"); return; }

        var (r, g, b) = HexToRgb(hex);
        if (PngUtil.Silhouette(iconPath, outlinePath, r, g, b))
            log?.Invoke($"  头像描边：已按 #{hex} 自动生成（用头像的透明通道做剪影）");
        else
            log?.Invoke("  [警告] 头像 PNG 格式不支持自动上色（只支持 8bit RGB/RGBA），描边保持原样");
    }

    /// <summary>把工具自带的静态立绘占位图写到目标路径：优先用程序集内嵌的那份，其次找 exe 旁的 Assets\ 目录。</summary>
    private static bool TryWriteBundledPlaceholder(string dstPath)
    {
        try
        {
            const string resName = "Sts2CharForge.Core.Assets.placeholder_character.png";
            using Stream? s = typeof(ArtGenerator).Assembly.GetManifestResourceStream(resName);
            if (s is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dstPath)!);
                using var fs = File.Create(dstPath);
                s.CopyTo(fs);
                return true;
            }
        }
        catch { /* 落到下面的文件方式 */ }

        string bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "placeholder_character.png");
        if (File.Exists(bundled))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dstPath)!);
            File.Copy(bundled, dstPath, overwrite: true);
            return true;
        }
        return false;
    }

    /// <summary>把十六进制拆成 RGB。</summary>
    private static (byte R, byte G, byte B) HexToRgb(string hex)
    {
        string s = CardColorSpec.NormalizeHex(hex);
        if (s.Length < 6) return (255, 255, 255);
        return (System.Convert.ToByte(s.Substring(0, 2), 16),
                System.Convert.ToByte(s.Substring(2, 2), 16),
                System.Convert.ToByte(s.Substring(4, 2), 16));
    }

    /// <summary>
    /// 卡牌边框颜色：本体的边框材质是 hsv.gdshader 的 ShaderMaterial（h/s/v 三个参数）。
    /// 填了自定义颜色就按它的 HSV 生成一份自己的材质，CardFrameMaterialPath 指过去即可。
    /// </summary>
    private static void WriteCardFrameMaterial(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        var n = Naming.From(p);
        string hex = CardColorSpec.NormalizeHex(p.Colors.CardFrameColor);
        if (hex.Length > 0)
        {
            WriteFrameMaterialFile(projectRoot, $"{n.CharSlug}_frame", hex);
            var (r, g, b) = HexToRgb(hex);
            PngUtil.RgbToHsv(r / 255.0, g / 255.0, b / 255.0, out double hh, out double ss, out double vv);
            log?.Invoke($"  卡牌边框：按 #{hex} 生成自定义材质（HSV {hh:0.###}/{ss:0.###}/{vv:0.###}）");
        }
        // 「诅咒 / 先古卡」页给这两类牌单独设的颜色：各自生成一份材质（池子指过去）
        WriteSpecialFrameMaterial(p, projectRoot, p.CurseStyle, "curse", "诅咒", log);
        WriteSpecialFrameMaterial(p, projectRoot, p.AncientStyle, "ancient", "先古卡", log);
    }

    /// <summary>「诅咒 / 先古卡」某一类的自定义边框材质（选了本体框色 / 没设样式时什么都不写）。</summary>
    private static void WriteSpecialFrameMaterial(CharacterProfile p, string projectRoot,
        SpecialCardStyleSpec style, string stem, string what, Action<string>? log)
    {
        if (!style.Any || !style.IsCustomFrame) return;
        string hex = CardColorSpec.NormalizeHex(style.FrameColor);
        if (hex.Length == 0) return;
        var n = Naming.From(p);
        string mat = n.SpecialFrameMaterial(stem);
        WriteFrameMaterialFile(projectRoot, mat, hex);
        var (r, g, b) = HexToRgb(hex);
        PngUtil.RgbToHsv(r / 255.0, g / 255.0, b / 255.0, out double hh, out double ss, out double vv);
        log?.Invoke($"  {what}边框：按 #{hex} 生成自定义材质 {mat}（HSV {hh:0.###}/{ss:0.###}/{vv:0.###}）");
    }

    /// <summary>
    /// 一份「按颜色染色」的卡框材质：本体边框用的是 hsv.gdshader 的 ShaderMaterial（h/s/v 三个参数），
    /// 所以只写这三个参数就能得到任意颜色的卡框（角色配色 / 诅咒 / 先古卡共用这一份生成逻辑）。
    /// </summary>
    private static void WriteFrameMaterialFile(string projectRoot, string name, string hex)
    {
        var (r, g, b) = HexToRgb(hex);
        PngUtil.RgbToHsv(r / 255.0, g / 255.0, b / 255.0, out double hh, out double ss, out double vv);
        ProjectFilesGen.WriteText(Path.Combine(projectRoot, "materials/cards/frames", name + "_mat.tres"), $"""
[gd_resource type="ShaderMaterial" load_steps=2 format=3]

[ext_resource type="Shader" path="res://shaders/hsv.gdshader" id="1_hsv"]

[resource]
resource_local_to_scene = true
shader = ExtResource("1_hsv")
shader_parameter/h = {hh.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}
shader_parameter/s = {ss.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}
shader_parameter/v = {vv.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}
""");
    }

    /// <summary>
    /// 左侧能量球配色：**跟着卡牌能量图标的颜色走**（不再跟着描边色）。
    /// 上传了能量图标 → 取它的主色相，把本体 5 层能量球贴图与粒子光效转到同一色相；
    /// 没上传 → 卡牌能量图标用的就是本体图，能量球也保持本体配色，完全不动。
    /// </summary>
    private static void WriteEnergyCounterRecolor(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        var n = Naming.From(p);
        string sceneDst = Path.Combine(projectRoot, "scenes/combat/energy_counters", $"{n.CharSlug}_energy_counter.tscn");
        if (!File.Exists(sceneDst)) return;                        // 场景本身缺失就跳过

        string? icon = p.Art.EnergyIcon;
        if (string.IsNullOrWhiteSpace(icon) || !File.Exists(icon))
            return;                                                // 没上传图标 → 保持本体配色

        if (!PngUtil.TryGetDominantHue(icon, out double hue))
        {
            log?.Invoke("  左侧能量球：能量图标是灰阶/太透明，取不到主色相 → 保持本体配色");
            return;
        }

        string srcDir = Path.Combine(p.Paths.VanillaProject, "images/ui/combat/energy_counters/ironclad");
        string dstDir = Path.Combine(projectRoot, "images/ui/combat/energy_counters", n.CharSlug);
        if (!Directory.Exists(srcDir)) { log?.Invoke("  [警告] 找不到本体能量球贴图，左侧能量球保持本体配色"); return; }

        string sceneText = File.ReadAllText(sceneDst);
        int done = 0;
        foreach (string src in Directory.GetFiles(srcDir, "*.png"))
        {
            string file = Path.GetFileName(src);
            string dst = Path.Combine(dstDir, file);
            if (PngUtil.HueShiftTo(src, dst, hue)) done++;
            else File.Copy(src, dst, overwrite: true);
            sceneText = sceneText.Replace($"res://images/ui/combat/energy_counters/ironclad/{file}",
                                          $"res://images/ui/combat/energy_counters/{n.CharSlug}/{file}");
        }

        // 能量球周围的粒子 VFX 里颜色也是写死的橙色 → 一起转到目标色相，否则球变蓝了光还是橙的
        int vfx = 0;
        foreach (var (srcRel, dstRel) in new[]
        {
            ("scenes/vfx/energy/ironclad/ironclad_energy_vfx_back.tscn",  $"scenes/vfx/energy/{n.CharSlug}/{n.CharSlug}_energy_vfx_back.tscn"),
            ("scenes/vfx/energy/ironclad/ironclad_energy_vfx_front.tscn", $"scenes/vfx/energy/{n.CharSlug}/{n.CharSlug}_energy_vfx_front.tscn"),
        })
        {
            string srcFile = Path.Combine(p.Paths.VanillaProject, srcRel);
            if (!File.Exists(srcFile)) continue;
            string text = HueShiftColorsInScene(File.ReadAllText(srcFile), hue);
            ProjectFilesGen.WriteText(Path.Combine(projectRoot, dstRel), text);
            sceneText = sceneText.Replace("res://" + srcRel, "res://" + dstRel);
            vfx++;
        }

        // 只改色相是不够的（用户给的图标要是本来就偏橙，改完跟本体球几乎一样，看着就像「还是本体美术」）。
        // 上传了能量图标就把球本身换成这张图：Layer1 用上传的图，其余 4 层（含会自转的 2 层）隐藏。
        string orbRel = $"images/ui/combat/energy_counters/{n.CharSlug}/{n.CharSlug}_energy_orb.png";
        string orbDst = Path.Combine(projectRoot, orbRel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(orbDst)!);
        bool orbOk = PngUtil.Resize(icon, orbDst, 128, 128);
        if (!orbOk) File.Copy(icon, orbDst, overwrite: true);

        string layer1 = $"res://images/ui/combat/energy_counters/{n.CharSlug}/ironclad_orb_layer_1.png";
        if (sceneText.Contains(layer1, StringComparison.Ordinal))
        {
            sceneText = sceneText.Replace(layer1, "res://" + orbRel);
            sceneText = HideSceneNode(sceneText, "RotationLayers");
            sceneText = HideSceneNode(sceneText, "Layer4");
            sceneText = HideSceneNode(sceneText, "Layer5");
            File.WriteAllText(sceneDst, sceneText, ProjectFilesGen.Utf8NoBom);
            log?.Invoke($"  左侧能量球：球体贴图换成了你上传的能量图标（128×128，本体的另外 4 层已隐藏），粒子光效按色相 {hue:0.###} 着色");
            return;
        }

        File.WriteAllText(sceneDst, sceneText, ProjectFilesGen.Utf8NoBom);
        log?.Invoke($"  左侧能量球：{done} 层贴图 + {vfx} 个粒子光效已按能量图标的颜色（色相 {hue:0.###}）重新着色");
    }

    /// <summary>给场景文本里某个节点加一行 visible = false（节点名对不上就原样返回）。</summary>
    private static string HideSceneNode(string sceneText, string nodeName)
    {
        string header = $"[node name=\"{nodeName}\"";
        int at = sceneText.IndexOf(header, StringComparison.Ordinal);
        if (at < 0) return sceneText;
        int lineEnd = sceneText.IndexOf('\n', at);
        if (lineEnd < 0) return sceneText;
        return sceneText.Insert(lineEnd + 1, "visible = false\n");
    }

    /// <summary>把场景文本里写死的颜色（Color(...) 与 PackedColorArray(...)）转到目标色相；近灰白的不动。</summary>
    private static string HueShiftColorsInScene(string text, double targetHue)
    {
        string Shift(string nums)
        {
            var parts = nums.Split(',');
            if (parts.Length < 3) return nums;
            var vals = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!double.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out vals[i])) return nums;

            PngUtil.RgbToHsv(Math.Clamp(vals[0], 0, 1), Math.Clamp(vals[1], 0, 1), Math.Clamp(vals[2], 0, 1),
                out _, out double s, out double v);
            if (s <= 0.08) return nums;     // 灰/白不染色
            PngUtil.HsvToRgb(targetHue, s, v, out double r, out double g, out double b);

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append(r.ToString("0.####", inv)).Append(", ").Append(g.ToString("0.####", inv)).Append(", ").Append(b.ToString("0.####", inv));
            for (int i = 3; i < parts.Length; i++) sb.Append(',').Append(parts[i]);
            return sb.ToString();
        }

        // Color(r, g, b, a)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"Color\(([^()]*)\)",
            m => "Color(" + Shift(m.Groups[1].Value) + ")");

        // PackedColorArray(...)：每 4 个一组
        text = System.Text.RegularExpressions.Regex.Replace(text, @"PackedColorArray\(([^()]*)\)", m =>
        {
            var vals = m.Groups[1].Value.Split(',');
            if (vals.Length % 4 != 0) return m.Value;
            var outVals = new List<string>();
            for (int i = 0; i + 3 < vals.Length; i += 4)
                outVals.Add(Shift(string.Join(",", vals.Skip(i).Take(3))));
            for (int i = 3; i < vals.Length; i += 4) outVals.Add(vals[i].Trim());
            // 重新按 每组4个 的顺序拼回去
            var sb = new System.Text.StringBuilder("PackedColorArray(");
            for (int g = 0; g < vals.Length / 4; g++)
            {
                if (g > 0) sb.Append(", ");
                sb.Append(outVals[g]).Append(", ").Append(vals[g * 4 + 3].Trim());
            }
            return sb.Append(')').ToString();
        });
        return text;
    }

    /// <summary>额外资源量自定义图标在工程里的相对路径（res:// 下）。</summary>
    public static string ExtraResourceIconRelPath(CharacterProfile p)
    {
        var n = Naming.From(p);
        return $"images/ui/combat/{n.CharSlug}_extra_resource_icon.png";
    }

    /// <summary>额外资源量自定义图标的 res:// 路径（没上传时返回 null）。</summary>
    public static string? ExtraResourceIconResPath(CharacterProfile p) =>
        string.IsNullOrWhiteSpace(p.ExtraResource.Icon)
            ? null
            : "res://" + ExtraResourceIconRelPath(p);

    /// <summary>本地化文字里内联引用的资源图标（24×24，和本体 star_icon.png 同尺寸）。</summary>
    public static string ExtraResourceIconInlineRelPath(CharacterProfile p)
    {
        var n = Naming.From(p);
        return $"images/packed/sprite_fonts/{n.CharSlug}_extra_resource_icon.png";
    }

    /// <summary>文字内联版资源图标的 res:// 路径（没上传时返回 null，调用方回退到原本体星星）。</summary>
    public static string? ExtraResourceIconInlineResPath(CharacterProfile p) =>
        string.IsNullOrWhiteSpace(p.ExtraResource.Icon)
            ? null
            : "res://" + ExtraResourceIconInlineRelPath(p);

    /// <summary>
    /// 额外资源量的自定义图标：
    ///   · 计数器 / 遗物栏用的大图：原样拷进 pck（计数器是 128×128 的方框，游戏自己缩放）
    ///   · 文字里内联的小图：**必须缩成 24×24**（本体 sprite_fonts 里的 star_icon.png 就是 24×24），
    ///     否则悬停描述 / 卡牌描述里的 [img] 会按原图尺寸撑爆排版，字体被自适应缩得极小。
    /// 没上传就什么都不做，计数器继续用本体的星星图标。
    /// </summary>
    private static void WriteExtraResourceIcon(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        if (!p.ExtraResource.Enabled) return;
        string? src = p.ExtraResource.Icon;
        bool hasUserIcon = !string.IsNullOrWhiteSpace(src) && File.Exists(src);
        if (!string.IsNullOrWhiteSpace(src) && !hasUserIcon)
            log?.Invoke($"  [警告] 额外资源量图标不存在：{src}");

        // 没上传（或文件没了）时用本体的星星图标当占位：遗物栏那个格子总得有图，
        // 不给文件的话日志会报 Missing sprite 'xxx_extra_resource_relic'、格子显示紫色占位。
        if (!hasUserIcon)
        {
            string star = Path.Combine(p.Paths.VanillaProject, "images/packed/sprite_fonts/star_icon.png");
            if (!File.Exists(star))
            {
                string dir = Path.Combine(p.Paths.VanillaProject, "images/packed/sprite_fonts");
                star = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").FirstOrDefault() ?? "" : "";
            }
            WriteExtraResourceRelicFallback(p, projectRoot, File.Exists(star) ? star : null);
            log?.Invoke("  额外资源量图标：没上传（或文件不存在），计数器 / 遗物栏沿用本体的星星图标");
            return;
        }

        string dst = Path.Combine(projectRoot, ExtraResourceIconRelPath(p).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src!, dst, overwrite: true);

        string inlineRel = ExtraResourceIconInlineRelPath(p);
        string inlineDst = Path.Combine(projectRoot, inlineRel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(inlineDst)!);
        if (!PngUtil.Resize(src!, inlineDst, 24, 24))
            File.Copy(src!, inlineDst, overwrite: true);

        WriteExtraResourceRelicFallback(p, projectRoot, src);

        // 方图最好看（计数器是个 128×128 的方框），顺便提醒一下
        var size = PngUtil.Decode(src!);
        string hint = size is { } s && s.W != s.H ? $"，[警告] 这张图是 {s.W}×{s.H}，不是方图，建议换成方图" : "";
        string small = size is { } s2 && (s2.W != 24 || s2.H != 24) ? $"；文字里的内联图标已自动缩成 24×24（原图 {s2.W}×{s2.H}）" : "";
        log?.Invoke($"  额外资源量图标：已使用你上传的图 {Path.GetFileName(src)}{hint}{small}");
    }

    /// <summary>
    /// 「额外资源量」那个隐藏起始遗物的图标文件。
    ///
    /// 它也在遗物栏里显示，图标路径是本体规则 <c>relic_atlas.sprites/&lt;entry&gt;.tres</c>；
    /// 图集里没有这个精灵时，AtlasResourceLoader 会回退到 <c>images/relics/&lt;entry&gt;.png</c>。
    /// <b>不给这个文件</b>，遗物栏那个格子就是 missing_power（紫色占位）+ 日志里
    /// <c>Missing sprite 'xxx_extra_resource_relic'</c>（用户实测报过）。
    /// </summary>
    private static void WriteExtraResourceRelicFallback(CharacterProfile p, string projectRoot, string? src)
    {
        string relicEntry = Naming.EntryOf(Naming.From(p).ExtraResourceRelicClass).ToLowerInvariant();
        string relicDst = Path.Combine(projectRoot, "images/relics", relicEntry + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(relicDst)!);
        if (src is not null && File.Exists(src))
        {
            if (!PngUtil.Resize(src, relicDst, 128, 128))
                File.Copy(src, relicDst, overwrite: true);
        }
        else if (!PngUtil.Resize(relicDst, relicDst, 128, 128) && !File.Exists(relicDst))
        {
            WriteNeutralPng(relicDst, 128, 128);
        }
        var (rw, rh) = ReadPngSize(relicDst, 128, 128);
        ProjectFilesGen.WriteText(Path.Combine(projectRoot, "images/atlases/relic_atlas.sprites", relicEntry + ".tres"), $"""
[gd_resource type="AtlasTexture" load_steps=2 format=3]

[ext_resource type="Texture2D" path="res://images/relics/{relicEntry}.png" id="1_relic"]

[resource]
atlas = ExtResource("1_relic")
region = Rect2(0, 0, {rw}, {rh})
""");
    }

    /// <summary>
    /// 生成出来的那几个 Power（透支能量 / 额外回合 / 下回合生效 / 临时增益）也要有图标。
    ///
    /// 走的是本体自己的**回退路径**：<c>PowerModel.PackedIconPath</c> 是
    /// <c>res://images/atlases/power_atlas.sprites/&lt;entry&gt;.tres</c>，本体的 AtlasResourceLoader
    /// 在图集里找不到这个精灵时会回退到 <c>res://images/powers/&lt;entry&gt;.png</c>；
    /// 没有这个文件就报 "Missing sprite" 并显示 missing_power（紫色占位）。
    /// <c>PowerModel.BigIcon</c>（施加 / 闪烁那张大图）本来就直接读这个路径。
    /// 所以只要把这张 PNG 放进工程，小图标和大图标就都有了 —— 不需要补丁。
    ///
    /// 图从本体 <c>images/powers/</c> 里挑一张语义最接近的：
    ///   · 透支能量 → <c>energy_next_turn_power.png</c>（本体「下回合能量」，就是同一件事）
    ///   · 额外回合 → <c>borrowed_time_power.png</c>（本体「借来的时间」，时钟那张）
    ///   · 下回合生效 / 临时增益 → 那个状态**自己**的图标（照样能一眼看出是哪个状态）
    /// 这里**不看**「用本体素材占位」那个勾选项：它是管角色立绘 / 背景的，而状态图标没有「中性占位」这种
    /// 说法（一个纯色方块还不如本体图直观）。本体里找不到那张图（没解包工程）才写中性占位图。
    /// </summary>
    private static void WriteGeneratedPowerIcons(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        var n = Naming.From(p);
        var wanted = new List<(string Entry, string VanillaIcon, string Why)>();

        if (CSharpCodeGen.UsesEnergyDebt(p))
            wanted.Add((n.EnergyDebtPowerClass, "energy_next_turn_power.png", "透支能量"));
        if (CSharpCodeGen.UsesEnergyNextTurnDebt(p))
            wanted.Add((n.EnergyNextTurnDebtPowerClass, "energy_next_turn_power.png", "下回合少能量"));
        if (CSharpCodeGen.UsesTempUpgrade(p))
            // 本体没有「升级」主题的状态图，用「学习 / 掌握」这张最接近的（imitation_learning 那张书页）
            wanted.Add((n.TempUpgradePowerClass, "imitation_learning_power.png", "仅本回合升级"));
        if (CSharpCodeGen.UsesExtraTurn(p))
            wanted.Add((n.ExtraTurnPowerClass, "borrowed_time_power.png", "额外回合"));

        string IconOfPower(string? powerId)
        {
            string png = EffectCatalog.SlugFor(powerId).ToLowerInvariant() + ".png";
            return File.Exists(Path.Combine(p.Paths.VanillaProject, "images", "powers", png))
                ? png
                : "energy_next_turn_power.png";     // 那个状态在本体里没有单独的图（例如自定义状态）：用能量图兜底
        }

        foreach (var e in CSharpCodeGen.CollectDelayedEffects(p))
            wanted.Add((n.DelayedPowerClass(e), IconOfPower(e.PowerId), "下回合：" + EffectCatalog.PowerName(e.PowerId)));
        foreach (var e in CSharpCodeGen.CollectTempPowerEffects(p))
            wanted.Add((n.TempPowerClass(e), IconOfPower(e.PowerId), "临时：" + EffectCatalog.PowerName(e.PowerId)));
        // 强化指定卡牌：伤害用本体「精准」那张、格挡用「敏捷」那张（就是同一件事：给某张卡加数值）
        foreach (var e in CSharpCodeGen.CollectBoostEffects(p))
            wanted.Add((n.BoostPowerClass(e),
                e.BoostStat == "Block" ? "dexterity_power.png" : "accuracy_power.png",
                "强化：" + (e.SpawnCardId ?? "?")));

        int copied = 0, neutral = 0;
        foreach (var (entry, vanillaIcon, why) in wanted.DistinctBy(w => w.Entry))
        {
            // 文件名必须是**本体的 Id.Entry**（= 类名按本体规则 slugify，带下划线）：
            // 本体找的是 res://images/powers/<Id.Entry 小写>.png，例如 sparkle_forge_energy_debt_power.png。
            // 直接用「类名小写」（sparkleforgeenergydebtpower.png）**游戏找不到** ——
            // 日志里就是 "Missing sprite 'sparkle_forge_energy_debt_power' in power_atlas" + 紫占位（用户实测）。
            string fileName = EffectCatalog.SlugFor(entry).ToLowerInvariant() + ".png";
            string dst = Path.Combine(projectRoot, "images", "powers", fileName);
            if (File.Exists(dst)) continue;                     // 用户自己的图标（自定义状态）优先，别覆盖
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

            string src = Path.Combine(p.Paths.VanillaProject, "images", "powers", vanillaIcon);
            if (File.Exists(src))
            {
                File.Copy(src, dst, overwrite: true);
                copied++;
            }
            else
            {
                WriteNeutralPng(dst, 256, 256);
                neutral++;
            }
        }
        if (copied + neutral > 0)
            log?.Invoke($"  生成出来的状态的图标：{copied} 个用本体图（透支=下回合能量 / 额外回合=借来的时间 / 下回合·临时=那个状态自己的图）"
                + (neutral > 0 ? $"，{neutral} 个中性占位（本体里找不到那张图：要指定解包工程目录）" : "")
                + "（放 res://images/powers/<本体 Id.Entry 小写>.png，本体在图集里找不到精灵时会回退到它）");
    }

    /// <summary>
    /// 「本体状态改写」的自定义图标：原样拷进 pck（res://images/powers_override/&lt;状态&gt;_icon.png）。
    /// 本体状态栏那个小图标是 64×64 上下的方图，这里不强制改尺寸（游戏会自己缩放），只是不是方图时提醒一句。
    /// </summary>
    private static void WriteVanillaPowerIcons(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        foreach (var o in VanillaPowerGen.Active(p))
        {
            if (string.IsNullOrWhiteSpace(o.Icon)) continue;
            if (!File.Exists(o.Icon)) { log?.Invoke($"  [警告] 本体状态「{o.PowerId}」的图标不存在：{o.Icon}"); continue; }

            string dst = Path.Combine(projectRoot, VanillaPowerGen.IconRelPath(p, o).Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(o.Icon, dst, overwrite: true);

            var size = PngUtil.Decode(o.Icon);
            string hint = size is { } s && s.W != s.H ? $"，[警告] 这张图是 {s.W}×{s.H}，不是方图，建议换成方图" : "";
            log?.Invoke($"  本体状态图标：{o.PowerId} → 已使用你上传的图 {Path.GetFileName(o.Icon)}{hint}");
        }

        // 自定义状态（能力牌用）的图标：放同一个目录
        for (int i = 0; i < p.CustomPowers.Count; i++)
        {
            var cp = p.CustomPowers[i];
            if (!CustomPowerGen.IsActive(cp) || string.IsNullOrWhiteSpace(cp.Icon)) continue;
            if (!File.Exists(cp.Icon)) { log?.Invoke($"  [警告] 自定义状态「{cp.Name}」的图标不存在：{cp.Icon}"); continue; }

            string dst = Path.Combine(projectRoot, CustomPowerGen.IconRelPath(p, cp, i).Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(cp.Icon, dst, overwrite: true);

            // 还要放一份「本体自己会去找」的路径：res://images/powers/<状态>.png
            // 本体的 PowerModel.PackedIconPath = atlases/power_atlas.sprites/<entry>.tres，
            // AtlasResourceLoader 在图集里找不到这个精灵时会回退到 images/powers/<entry>.png；
            // 没有这个文件就会报 "Missing sprite 'xxx' in power_atlas" 并显示 missing_power（紫色占位）。
            // PowerModel.BigIcon（施加/闪烁特效那张大图）本来就直接读 images/powers/<entry>.png，也一起有了。
            WriteCustomPowerAtlasFallback(p, cp, i, projectRoot);

            var size = PngUtil.Decode(cp.Icon);
            string hint = size is { } s && s.W != s.H ? $"，[警告] 这张图是 {s.W}×{s.H}，不是方图，建议换成方图" : "";
            log?.Invoke($"  自定义状态图标：{cp.Name} → 已使用你上传的图 {Path.GetFileName(cp.Icon)}{hint}");
        }
    }

    /// <summary>
    /// 自定义状态图标再放一份到 res://images/powers/&lt;状态&gt;.png：
    /// 这是本体 AtlasResourceLoader 给 power_atlas 准备的「回退路径」（GetPowerFallbackPath），
    /// 也是 PowerModel.BigIcon 直接读的路径。只放 powers_override/ 那份的话，
    /// 凡是绕过 PowerModel.Icon 直接按路径取图的地方都会报 Missing sprite 并显示紫色占位图。
    /// </summary>
    private static void WriteCustomPowerAtlasFallback(CharacterProfile p, CustomPowerSpec cp, int index, string projectRoot)
    {
        string entry = CustomPowerGen.EntryOf(p, cp, index).ToLowerInvariant();
        string dst = Path.Combine(projectRoot, "images/powers", entry + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(cp.Icon, dst, overwrite: true);
        var (w, h) = ReadPngSize(dst, 64, 64);

        // 图集精灵占位文件（本体用 ResourceFormatLoader 接管 .sprites/*.tres，内容按路径解析，这里只为兼容）
        ProjectFilesGen.WriteText(Path.Combine(projectRoot, "images/atlases/power_atlas.sprites", entry + ".tres"), $"""
[gd_resource type="AtlasTexture" load_steps=2 format=3]

[ext_resource type="Texture2D" path="res://images/powers/{entry}.png" id="1_power"]

[resource]
atlas = ExtResource("1_power")
region = Rect2(0, 0, {w}, {h})
""");
    }

    /// <summary>一条「由我们自己供给」的图集精灵（本体 AtlasManager 里的精灵表不会有它）。</summary>
    public readonly record struct AtlasSpriteEntry(string Atlas, string Sprite, string TexturePath, int X, int Y, int W, int H);

    /// <summary>
    /// 能量图标（卡面费用 / 能量悬停提示那个）的图片来源：本体 ui_atlas 里的能量精灵是 74×74。
    /// 三种情况（和 <see cref="WriteEnergyIcon"/> 写出来的文件一一对应）：
    ///   · 上传了图标   → 我们自己的图集页 <c>&lt;color&gt;_energy_page_0.png</c>（缩成 74×74）
    ///   · 用本体素材占位 → 直接引用本体 ui_atlas_0.png 里铁甲战士那一块（1440,1948,74,74）
    ///   · 中性占位     → 我们自己的 <c>&lt;color&gt;_neutral_atlas_0.png</c>
    /// </summary>
    public static AtlasSpriteEntry EnergySpriteEntry(CharacterProfile p)
    {
        var n = Naming.From(p);
        bool userIcon = !string.IsNullOrWhiteSpace(p.Art.EnergyIcon) && File.Exists(p.Art.EnergyIcon);
        bool neutral = !userIcon && !p.Art.UseVanillaPlaceholders;
        string tex = neutral ? $"res://images/atlases/{n.EnergyColor}_neutral_atlas_0.png"
            : userIcon ? $"res://images/atlases/{n.EnergyColor}_energy_page_0.png"
            : "res://images/atlases/ui_atlas_0.png";
        return new AtlasSpriteEntry("ui_atlas", $"card/energy_{n.EnergyColor}", tex,
            userIcon || neutral ? 0 : 1440, userIcon || neutral ? 0 : 1948, 74, 74);
    }

    /// <summary>
    /// 本模组需要「自己供给」的全部图集精灵：能量图标 1 条 + 每支药水 2 条（普通 / 描边）。
    /// 必须在美术生成**之后**调用（药水图标的区域要按刚写出来的 PNG 实际尺寸算）。
    /// </summary>
    public static IReadOnlyList<AtlasSpriteEntry> AtlasSpriteEntries(CharacterProfile p, string projectRoot)
    {
        var n = Naming.From(p);
        var list = new List<AtlasSpriteEntry> { EnergySpriteEntry(p) };
        for (int i = 0; i < p.Potions.Count; i++)
        {
            string entry = Naming.EntryOf(n.PotionClassName(p.Potions[i], i)).ToLowerInvariant();
            string bigRel = $"images/potions/large/{entry}.png";
            string outlineRel = $"images/potions/large/{entry}_outline.png";
            var (w, h) = ReadPngSize(Path.Combine(projectRoot, bigRel.Replace('/', Path.DirectorySeparatorChar)), 77, 78);
            var (ow, oh) = ReadPngSize(Path.Combine(projectRoot, outlineRel.Replace('/', Path.DirectorySeparatorChar)), w, h);
            list.Add(new AtlasSpriteEntry("potion_atlas", entry, "res://" + bigRel, 0, 0, w, h));
            list.Add(new AtlasSpriteEntry("potion_outline_atlas", entry, "res://" + outlineRel, 0, 0, ow, oh));
        }
        return list;
    }

    /// <summary>
    /// 「图集精灵由我们自己供给」的补丁。
    ///
    /// 为什么需要它（用户实测）：本体查能量图标 / 药水图标是
    /// <c>AtlasResourceLoader</c> → <c>AtlasManager.GetSprite(atlas, sprite)</c>，
    /// 而精灵名只存在于图集的 <c>.tpsheet</c> 里 —— 每个模组各带一份 <c>ui_atlas.tpsheet</c> 时，
    /// 同一个路径只会生效一份（后挂载的 pck 盖掉前面的），于是另一个角色的能量图标变成
    /// <c>Missing sprite 'card/energy_xxx' in ui_atlas</c>，游戏里图标直接消失
    ///（<c>ui_atlas</c> 在图集加载器里**没有**回退路径，找不到就是没有）。
    ///
    /// 所以生成时不再写任何 tpsheet，改成在运行时给 AtlasManager 挂前后缀：
    /// 我们自己的精灵直接返回我们自己的纹理 —— 文件名每个模组唯一，永远不会互相覆盖。
    ///
    /// <b>写这个文件必须注意两件事（都踩过）</b>：
    ///   ① <c>[HarmonyPatch]</c> 必须写在**类上**（类级注解）。写在方法上时
    ///      <c>PatchAll</c> 会**静默跳过**整个类 —— 模组加载日志一切正常、补丁却完全没生效
    ///      （实测：方法级注解的补丁不会被打上，且不报任何错）。
    ///   ② 参数用 <c>__args</c> 拿，不依赖本体方法的形参名字（名字对不上会静默失效）。
    /// </summary>
    public static string AtlasSpritePatchSource(CharacterProfile p, string projectRoot)
    {
        var n = Naming.From(p);
        var entries = AtlasSpriteEntries(p, projectRoot);
        var w = new CodeWriter();
        w.Line("// <auto-generated> 能量 / 药水图标：由本模组自己的图直接供给 AtlasManager（不改本体的图集精灵表） </auto-generated>")
         .Line($"namespace {n.Namespace};")
         .Line()
         .Line("/// <summary>")
         .Line("/// 本体查图集精灵走 AtlasManager.GetSprite / HasSprite。")
         .Line("/// 如果去改本体的 ui_atlas.tpsheet：每个模组各带一份、同路径只能生效一份，")
         .Line("/// 同时装两个自建角色时其中一个的能量图标就会 Missing sprite（用户实测）。")
         .Line("/// 所以这里把「我们自己的精灵」在内存里直接供给本体，纹理用各自模组独有的文件。")
         .Line("///")
         .Line("/// 注意：[HarmonyPatch] 一律写在**类上**（写在方法上 PatchAll 会静默跳过整个类）。")
         .Line("/// </summary>")
         .Open($"internal static class {n.AtlasSpritePatchClass}")
         .Line("// (图集, 精灵) → (纹理路径, 区域)。键就是图集精灵表里的 filename（本体查的时候会去掉 .png）")
         .Line("private static readonly Dictionary<(string Atlas, string Sprite), (string Path, Rect2 Region)> Table = new()")
         .Line("{");
        foreach (var e in entries)
            w.Line($"    [({Lit.Str(e.Atlas)}, {Lit.Str(e.Sprite)})] = ({Lit.Str(e.TexturePath)}, new Rect2({e.X}, {e.Y}, {e.W}, {e.H})),");
        w.Line("};")
         .Line()
         .Line("private static readonly Dictionary<(string Atlas, string Sprite), AtlasTexture> Cache = new();")
         .Line()
         .Line("/// <summary>本体问的这两个参数就是 (图集名, 精灵名)；不是我们表里的就返回 false，交回本体。</summary>")
         .Open("internal static bool TryGet(object[] args, out AtlasTexture? texture)")
         .Line("texture = null;")
         .Line("if (args is not { Length: >= 2 } || args[0] is not string atlasName || args[1] is not string spriteName) return false;")
         .Line("string sprite = spriteName.EndsWith(\".png\", StringComparison.OrdinalIgnoreCase) ? spriteName[..^4] : spriteName;")
         .Line("if (!Table.TryGetValue((atlasName, sprite), out var e)) return false;")
         .Open("lock (Cache)")
         .Open("if (Cache.TryGetValue((atlasName, sprite), out AtlasTexture? cached) && GodotObject.IsInstanceValid(cached))")
         .Line("texture = cached;")
         .Line("return true;")
         .Close()
         .Line("Texture2D? tex = ResourceLoader.Load<Texture2D>(e.Path, null, ResourceLoader.CacheMode.Reuse);")
         .Line("if (tex is null) return false;   // 自己的图没打进 pck：交回本体（至少不会崩）")
         .Line("var made = new AtlasTexture { Atlas = tex, Region = e.Region };")
         .Line("Cache[(atlasName, sprite)] = made;")
         .Line("texture = made;")
         .Line("return true;")
         .Close()
         .Close()
         .Close()
         .Line()
         .Line("/// <summary>_Exists 走的是 HasSprite：不拦这一下，本体连 _Load 都不会调用。</summary>")
         .Line("[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Assets.AtlasManager), nameof(MegaCrit.Sts2.Core.Assets.AtlasManager.HasSprite))]")
         .Open($"internal static class {n.AtlasSpritePatchClass}ExistsPatch")
         .Line("[HarmonyLib.HarmonyPrefix]")
         .Open("private static bool Prefix(object[] __args, ref bool __result)")
         .Line($"if (!{n.AtlasSpritePatchClass}.TryGet(__args, out _)) return true;   // 不是我们的精灵：交回本体")
         .Line("__result = true;")
         .Line("return false;")
         .Close()
         .Close()
         .Line()
         .Line("/// <summary>真正取精灵：命中我们自己的表就直接返回我们自己的纹理。</summary>")
         .Line("[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Assets.AtlasManager), nameof(MegaCrit.Sts2.Core.Assets.AtlasManager.GetSprite))]")
         .Open($"internal static class {n.AtlasSpritePatchClass}LoadPatch")
         .Line("[HarmonyLib.HarmonyPrefix]")
         .Open("private static bool Prefix(object[] __args, ref AtlasTexture? __result)")
         .Line($"if (!{n.AtlasSpritePatchClass}.TryGet(__args, out AtlasTexture? texture)) return true;   // 不是我们的精灵：交回本体")
         .Line("__result = texture;")
         .Line("return false;")
         .Close()
         .Close();
        return w.ToString();
    }

    private static void WriteEnergyIcon(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        var n = Naming.From(p);
        bool userIcon = !string.IsNullOrWhiteSpace(p.Art.EnergyIcon) && File.Exists(p.Art.EnergyIcon);
        bool neutral = !userIcon && !p.Art.UseVanillaPlaceholders;
        var sprite = EnergySpriteEntry(p);

        // 74×74 的那张图（卡面费用 / 悬停提示用）。
        // **不再**去改本体的 ui_atlas.tpsheet：那份表每个模组各带一份、同路径只有一个能生效，
        // 两个自建角色一起装就会有一个的能量图标消失（用户实测）。现在由生成的
        // AtlasSpritePatch 直接把这张图供给本体（见 AtlasSpritePatchSource）。
        if (neutral)
        {
            WriteNeutralPng(Path.Combine(projectRoot, "images/atlases", $"{n.EnergyColor}_neutral_atlas_0.png"), 74, 74);
        }
        else if (userIcon)
        {
            // 本体这张精灵就是 74×74：原样塞大图会让卡面上的能量图标尺寸失控（描述 / 布局一起变形）
            string pagePath = Path.Combine(projectRoot, "images/atlases", $"{n.EnergyColor}_energy_page_0.png");
            Directory.CreateDirectory(Path.GetDirectoryName(pagePath)!);
            if (!PngUtil.Resize(p.Art.EnergyIcon!, pagePath, 74, 74))
                File.Copy(p.Art.EnergyIcon!, pagePath, overwrite: true);
            log?.Invoke($"  能量图标：用上传的图片缩成 74×74 存成自己的图集页 {Path.GetFileName(pagePath)}（文字内联图标另存 24×24）");
        }
        // 用本体素材占位时不写自己的页：直接引用本体的 ui_atlas_0.png（铁甲战士那一块）

        ProjectFilesGen.WriteText(Path.Combine(projectRoot, "images/atlases/ui_atlas.sprites/card", $"energy_{n.EnergyColor}.tres"), $"""
[gd_resource type="AtlasTexture" load_steps=2 format=3]

[ext_resource type="Texture2D" path="{sprite.TexturePath}" id="1_ui"]

[resource]
atlas = ExtResource("1_ui")
region = Rect2({sprite.X}, {sprite.Y}, {sprite.W}, {sprite.H})
""");

        string dst = Path.Combine(projectRoot, "images/packed/sprite_fonts", $"{n.EnergyColor}_energy_icon.png");
        string? user = userIcon ? p.Art.EnergyIcon : null;
        string? src = user ?? (p.Art.UseVanillaPlaceholders
            ? Path.Combine(p.Paths.VanillaProject, "images/packed/sprite_fonts/ironclad_energy_icon.png")
            : null);
        if (src is not null && File.Exists(src))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            // 这个文件是卡牌描述里 [img] 内联引用的图标，本体是 24×24（内联图按原图尺寸画）。
            // 上传的大图（比如 512×512）必须缩到 24×24，否则会把卡牌描述撑爆、
            // 游戏的自适应字号就把整段描述缩成蚂蚁字（用户报过「大奖」那张牌）。
            if (!PngUtil.Resize(src, dst, 24, 24))
                File.Copy(src, dst, overwrite: true);
        }
        else
        {
            WriteNeutralPng(dst, 24, 24);   // 中性占位（本体 sprite_font 版就是 24×24）
        }
    }

    private static void WriteTransitionMaterial(CharacterProfile p, string projectRoot)
    {
        var n = Naming.From(p);
        ProjectFilesGen.WriteText(Path.Combine(projectRoot, "materials/transitions", $"{n.CharSlug}_transition_mat.tres"), $$"""
[gd_resource type="ShaderMaterial" load_steps=3 format=3]

[ext_resource type="Texture2D" path="res://images/ui/transitions/{{n.CharSlug}}_transition.png" id="1_trans"]

[sub_resource type="Shader" id="Shader_forge"]
code = "shader_type canvas_item;

uniform sampler2D transitionTex;
uniform float threshold : hint_range(0,1);

void fragment() {
    float falloff = 1.0 - texture(transitionTex, UV).r;
    float remap  = mix(-0.1, 1.1, threshold);
    falloff = step(falloff, remap);
    COLOR.a = falloff;
}
"

[resource]
resource_local_to_scene = true
shader = SubResource("Shader_forge")
shader_parameter/threshold = 0.332
shader_parameter/transitionTex = ExtResource("1_trans")
""");
    }

    private static void WriteCardPortraits(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        var n = Naming.From(p);
        string placeholder = Path.Combine(p.Paths.VanillaProject, "images/packed/card_portraits/ironclad/strike_ironclad.png");
        if (!File.Exists(placeholder))
            placeholder = Path.Combine(p.Paths.VanillaProject, "images/packed/card_portraits/ironclad/bash.png");
        // 先古卡的卡面框是**竖的**：本体的先古卡素材都是 606×852（节点 %AncientPortrait 是 598×842，
        // 而且游戏里直接拉满、不保持比例）。拿 1000×760 的横图去占位会被拉成一坨，所以先古卡单独占位。
        string ancientPlaceholder = Path.Combine(p.Paths.VanillaProject, "images/packed/card_portraits/ancient_beta.png");

        // 卡面素材目录跟**模型所在的卡池**走（Pool.Title）—— 诅咒 / 先古卡也在你自己的卡池里，
        // 所以它们的卡面同样放 images/packed/card_portraits/<你的角色>/ 下
        // （诅咒的 VisualCardPool 只是让卡框显示成本体的灰色，不影响素材路径）。
        foreach (var c in p.AllCards)
        {
            // 本体卡引用（打击 / 防御）：卡面用本体自己的图，不用我们生成占位图
            if (c.IsVanillaCard) continue;
            bool ancient = c.IsAncientCard;
            (int W, int H) size = ancient ? (606, 852) : (1000, 760);
            string cls = n.CardClassName(p, c);
            string entry = Naming.EntryOf(cls).ToLowerInvariant();
            string pngDst = Path.Combine(projectRoot, "images/packed/card_portraits", n.PoolTitle, entry + ".png");
            string tresDst = Path.Combine(projectRoot, "images/atlases/card_atlas.sprites", n.PoolTitle, entry + ".tres");

            string? userArt = p.Art.CardPortraits.TryGetValue(cls, out string? up) ? up : null;
            Directory.CreateDirectory(Path.GetDirectoryName(pngDst)!);
            string? vanillaPlaceholder = ancient && File.Exists(ancientPlaceholder) ? ancientPlaceholder : placeholder;
            if (!string.IsNullOrWhiteSpace(userArt) && File.Exists(userArt))
            {
                File.Copy(userArt, pngDst, overwrite: true);
            }
            else if (p.Art.UseVanillaPlaceholders && File.Exists(vanillaPlaceholder))
            {
                File.Copy(vanillaPlaceholder, pngDst, overwrite: true);
            }
            else
            {
                WriteNeutralPng(pngDst, size.W, size.H);   // 中性占位卡面（先古卡是竖的）
            }

            var (w, h) = ReadPngSize(pngDst, size.W, size.H);
            ProjectFilesGen.WriteText(tresDst, $"""
[gd_resource type="AtlasTexture" load_steps=2 format=3]

[ext_resource type="Texture2D" path="res://images/packed/card_portraits/{n.PoolTitle}/{entry}.png" id="1_card"]

[resource]
atlas = ExtResource("1_card")
region = Rect2(0, 0, {w}, {h})
""");
        }
    }

    /// <summary>
    /// 图集精灵表来自 .tpsheet（TexturePacker JSON）；xxx.sprites/*.tres 只是占位。
    /// 让自己命名的能量图标生效就必须往 tpsheet 里加一条 sprite（否则报 Missing sprite）。
    /// </summary>
    private static void PatchUiAtlasSheet(CharacterProfile p, string projectRoot, Action<string>? log)
    {
        var n = Naming.From(p);
        string src = Path.Combine(p.Paths.VanillaProject, "images/atlases/ui_atlas.tpsheet");
        if (!File.Exists(src)) { log?.Invoke("  [警告] 找不到 ui_atlas.tpsheet，能量图标可能显示为占位图"); return; }

        var node = JsonNode.Parse(File.ReadAllText(src))!;
        var textures = node["textures"]!.AsArray();
        string spriteName = $"card/energy_{n.EnergyColor}.png";
        string outPath = Path.Combine(projectRoot, "images/atlases/ui_atlas.tpsheet");

        foreach (var tex in textures)
            foreach (var sprite in tex!["sprites"]!.AsArray())
                if (string.Equals((string?)sprite!["filename"], spriteName, StringComparison.Ordinal))
                {
                    ProjectFilesGen.WriteText(outPath, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    return;
                }

        string? userIcon = p.Art.EnergyIcon;
        if (!string.IsNullOrWhiteSpace(userIcon) && File.Exists(userIcon))
        {
            // 本体这张精灵就是 74×74：原样塞大图会让卡面上的能量图标尺寸失控（描述 / 布局一起变形）
            string pageName = $"{n.EnergyColor}_energy_page_0.png";
            string pagePath = Path.Combine(projectRoot, "images/atlases", pageName);
            Directory.CreateDirectory(Path.GetDirectoryName(pagePath)!);
            if (!PngUtil.Resize(userIcon, pagePath, 74, 74))
                File.Copy(userIcon, pagePath, overwrite: true);
            int w = 74, h = 74;
            textures.Add(new JsonObject
            {
                ["image"] = pageName,
                ["size"] = new JsonObject { ["w"] = w, ["h"] = h },
                ["sprites"] = new JsonArray(new JsonObject
                {
                    ["filename"] = spriteName,
                    ["region"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = w, ["h"] = h },
                    ["margin"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = 0, ["h"] = 0 },
                }),
            });
            log?.Invoke($"  能量图标：用上传的图片缩成 74×74 新建了图集页 {pageName}（文字内联图标另存 24×24）");
        }
        else
        {
            bool done = false;
            foreach (var tex in textures)
            {
                foreach (var sprite in tex!["sprites"]!.AsArray())
                {
                    if (string.Equals((string?)sprite!["filename"], "card/energy_ironclad.png", StringComparison.Ordinal))
                    {
                        var clone = sprite.DeepClone().AsObject();
                        clone["filename"] = spriteName;
                        tex["sprites"]!.AsArray().Add(clone);
                        done = true;
                        break;
                    }
                }
                if (done) break;
            }
            if (!done) log?.Invoke("  [警告] tpsheet 里找不到 card/energy_ironclad.png，能量图标可能异常");
        }

        ProjectFilesGen.WriteText(outPath, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>写一张中性占位 PNG（纯色 + 深色边框），不含任何本体素材。</summary>
    public static void WriteNeutralPng(string path, int width, int height,
        byte r = 0x3A, byte g = 0x4A, byte b = 0x6A)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        // 原始扫描线：每行 1 字节 filter(0) + RGBA
        var raw = new byte[(width * 4 + 1) * height];
        int p = 0;
        for (int y = 0; y < height; y++)
        {
            raw[p++] = 0;
            for (int x = 0; x < width; x++)
            {
                bool edge = x < 2 || y < 2 || x >= width - 2 || y >= height - 2;
                raw[p++] = edge ? (byte)0x8A : r;
                raw[p++] = edge ? (byte)0x9A : g;
                raw[p++] = edge ? (byte)0xC0 : b;
                raw[p++] = 0xFF;
            }
        }

        byte[] idat;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(raw, 0, raw.Length);
            idat = ms.ToArray();
        }

        var ihdr = new byte[13];
        WriteBe(ihdr, 0, width);
        WriteBe(ihdr, 4, height);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // color type: RGBA
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;

        using var fs = File.Create(path);
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WritePngChunk(fs, "IHDR", ihdr);
        WritePngChunk(fs, "IDAT", idat);
        WritePngChunk(fs, "IEND", []);
    }

    private static void WriteBe(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WritePngChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, data.Length);
        s.Write(len);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        uint crc = 0xFFFFFFFF;
        foreach (byte b in typeBytes) crc = Crc32Step(crc, b);
        foreach (byte b in data) crc = Crc32Step(crc, b);
        crc ^= 0xFFFFFFFF;
        var crcBytes = new byte[4];
        WriteBe(crcBytes, 0, unchecked((int)crc));
        s.Write(crcBytes);
    }

    private static readonly uint[] _crcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    private static uint Crc32Step(uint crc, byte b) => _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

    /// <summary>读 PNG 尺寸（只解析 IHDR）。</summary>
    public static (int Width, int Height) ReadPngSize(string path, int fallbackW, int fallbackH)
    {
        try
        {
            byte[] head = new byte[24];
            using var fs = File.OpenRead(path);
            if (fs.Read(head, 0, 24) < 24) return (fallbackW, fallbackH);
            if (head[0] != 0x89 || head[1] != 0x50) return (fallbackW, fallbackH);
            int w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            int h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return (w > 0 ? w : fallbackW, h > 0 ? h : fallbackH);
        }
        catch { return (fallbackW, fallbackH); }
    }
}
