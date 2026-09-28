using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

public sealed record GenerationResult(bool Success, string ProjectRoot, List<ValidationIssue> Issues, List<string> Log);

/// <summary>把一份 profile 变成完整的、可构建的模组工程。</summary>
public static class ModGenerator
{
    public static GenerationResult Generate(CharacterProfile profile, Action<string>? log = null)
    {
        var lines = new List<string>();
        void Log(string s) { lines.Add(s); log?.Invoke(s); }

        // 状态改名的「显示名」表：卡面/遗物/药水文字渲染都要用它（必须在校验和生成之前设好）
        EffectCatalog.SetPowerRenames(profile.VanillaPowerOverrides);
        // 自定义状态登记：卡牌/遗物/药水的「施加增益/减益」要能引用它们
        EffectCatalog.SetCustomPowers(profile.CustomPowers
            .Select((cp, i) => (CustomPowerGen.ClassNameOf(profile, cp, i), cp.Name))
            .Where(x => !string.IsNullOrWhiteSpace(x.Item2)));

        var issues = ProfileValidator.Validate(profile);
        if (issues.Any(i => i.IsError))
        {
            Log("配置校验未通过，已中止生成：");
            foreach (var i in issues.Where(i => i.IsError)) Log("  " + i);
            return new GenerationResult(false, "", issues, lines);
        }
        foreach (var i in issues) Log("  " + i);

        var n = Naming.From(profile);
        if (string.IsNullOrWhiteSpace(profile.Paths.OutputDir))
            profile.Paths.OutputDir = Path.Combine(AppContext.BaseDirectory, "自定义角色");

        string root = ProjectRootOf(profile);

        // 每次生成都先清掉旧工程：上一次生成留下的 cs 文件（删掉的卡/状态/遗物）如果留着，
        // 会引用已经不存在的类 → 编译直接失败。清干净重新写，保证「生成出来的 = 当前配置」。
        if (Directory.Exists(root) && LooksLikeGeneratedProject(root))
        {
            try { Directory.Delete(root, true); Log("  已清掉上一次生成的工程（避免旧文件残留导致编译失败）"); }
            catch (Exception ex) { Log("  [警告] 清理旧工程失败（继续生成，可能会有旧文件残留）：" + ex.Message); }
        }
        Directory.CreateDirectory(root);
        Log($"生成工程目录：{root}");

        ProjectFilesGen.WriteText(Path.Combine(root, "project.godot"), ProjectFilesGen.ProjectGodot(n));
        ProjectFilesGen.WriteText(Path.Combine(root, n.ModId + ".csproj"), ProjectFilesGen.Csproj(n));
        ProjectFilesGen.WriteText(Path.Combine(root, n.ModId + ".sln"), ProjectFilesGen.Sln(n));
        ProjectFilesGen.WriteText(Path.Combine(root, "mod_manifest.json"), ProjectFilesGen.Manifest(profile));
        ProjectFilesGen.WriteText(Path.Combine(root, "export_presets.cfg"), ProjectFilesGen.ExportPresets());
        ProjectFilesGen.WriteText(Path.Combine(root, "export_pck.ps1"), ProjectFilesGen.ExportPckScript(n));
        ProjectFilesGen.WriteText(Path.Combine(root, "build.ps1"), ProjectFilesGen.BuildScript(n));
        Log("  工程文件（project.godot / csproj / sln / 清单 / 导出预设 / 构建脚本）");

        string cs = Path.Combine(root, "cs");
        ProjectFilesGen.WriteText(Path.Combine(cs, "GlobalUsings.cs"), CSharpCodeGen.GlobalUsings(profile));
        ProjectFilesGen.WriteText(Path.Combine(cs, n.CharClass + ".cs"), CSharpCodeGen.CharacterSource(profile));
        ProjectFilesGen.WriteText(Path.Combine(cs, n.CardPoolClass + ".cs"), CSharpCodeGen.CardPoolSource(profile));
        ProjectFilesGen.WriteText(Path.Combine(cs, n.RelicPoolClass + ".cs"), CSharpCodeGen.RelicPoolSource(profile));
        ProjectFilesGen.WriteText(Path.Combine(cs, n.PotionPoolClass + ".cs"), CSharpCodeGen.PotionPoolSource(profile));
        ProjectFilesGen.WriteText(Path.Combine(cs, "ModEntry.cs"), PatchesGen.ModEntrySource(profile));
        ProjectFilesGen.WriteText(Path.Combine(cs, "Patches.cs"), PatchesGen.PatchesSource(profile));

        for (int i = 0; i < profile.Cards.Count; i++)
        {
            // 本体卡引用（打击 / 防御）没有自己的类、没有自己的本地化，只有初始卡组里那一行
            if (profile.Cards[i].IsVanillaCard) continue;
            string cardCls = n.CardClassName(profile, profile.Cards[i]);
            ProjectFilesGen.WriteText(Path.Combine(cs, "Cards", cardCls + ".cs"),
                CSharpCodeGen.CardSource(profile, profile.Cards[i], i));
        }

        // ===== 生成后自检：卡池 / 初始卡组里引用的卡类，必须有对应的 cs/Cards/*.cs =====
        // 踩过这个坑：没填英文类名的卡在文件里声明成了另一个名字（列表下标 vs 自有卡序号不一致），
        // 生成出来是「SevenCard9.cs 里写着 class SevenCard11」，编译直接报
        // CS0246: 未能找到类型或命名空间名"SevenCard9"。这里提前拦住并说清楚是哪张卡。
        {
            var writtenCards = profile.Cards.Where(c => !c.IsVanillaCard)
                .Select(c => n.CardClassName(profile, c)).ToHashSet(StringComparer.Ordinal);
            var referenced = System.Text.RegularExpressions.Regex
                .Matches(CSharpCodeGen.CardPoolSource(profile), @"ModelDb\.Card<(\w+)>\(\)")
                .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var missing = referenced.Where(x => !writtenCards.Contains(x)).ToList();
            if (missing.Count > 0)
            {
                issues.Add(new Generation.ValidationIssue("错误", "生成出来的卡池引用了没有生成的卡类："
                    + string.Join("、", missing) + "（这类不一致会让 dotnet 报 CS0246）。"
                    + "常见原因是两张卡取了同一个英文类名，或者卡牌英文类名留空后和别的卡撞名 —— "
                    + "到「卡牌」页给它们填上各自的英文类名再生成。"));
            }
        }
        for (int i = 0; i < profile.Relics.Count; i++)
            ProjectFilesGen.WriteText(Path.Combine(cs, "Relics", n.RelicClassName(profile.Relics[i], i) + ".cs"),
                CSharpCodeGen.RelicSource(profile, profile.Relics[i], i));
        for (int i = 0; i < profile.Potions.Count; i++)
            ProjectFilesGen.WriteText(Path.Combine(cs, "Potions", n.PotionClassName(profile.Potions[i], i) + ".cs"),
                CSharpCodeGen.PotionSource(profile, profile.Potions[i], i));

        if (CSharpCodeGen.UsesExtraTurn(profile))
        {
            ProjectFilesGen.WriteText(Path.Combine(cs, "ExtraTurnPower.cs"), CSharpCodeGen.ExtraTurnPowerSource(profile));
            Log("  已生成「额外获得一回合」用的 ForgeExtraTurnPower");
        }

        if (profile.ExtraResource.Enabled)
        {
            ProjectFilesGen.WriteText(Path.Combine(cs, "ExtraResource.cs"), CSharpCodeGen.ExtraResourceSource(profile));
            Log($"  已生成额外资源量（初始 {profile.ExtraResource.Initial}，{(profile.ExtraResource.CarryOver ? "跨战斗继承" : "每场战斗重置")}）");
        }

        // ===== 召唤物（本体的通用宠物 API，不需要 Harmony 补丁）=====
        if (PetGen.IsActive(profile))
        {
            var pets = PetGen.All(profile);
            ProjectFilesGen.WriteText(Path.Combine(cs, "Pet.cs"), PetGen.Source(profile));
            // 名字写进本体的 monsters 表（逐键合并，只加我们自己的键）；多只召唤物都在同一张表里。
            // **中英文两份都要写**（内容相同）：模组本地化只合并「当前语言」的同名表
            //（ModManager.cs:966-979 + LocManager.cs:468），非中文语言下缺 <ENTRY>.name 会让
            // LocTable 抛 LocException、宠物节点初始化中断 —— 英文会话里显示中文名，总比崩好。
            string petMonsters = PetGen.MonstersJson(profile);
            ProjectFilesGen.WriteText(Path.Combine(root, profile.ModId, "localization", "zhs", "monsters.json"), petMonsters);
            ProjectFilesGen.WriteText(Path.Combine(root, profile.ModId, "localization", "eng", "monsters.json"), petMonsters);
            // 有「伙伴攻击」卡 → 生成「把攻击者换成宠物」的扩展方法（不需要补丁，见 PetGen.AttackExtensionsSource）
            if (PetGen.UsesAttackExtension(profile))
            {
                ProjectFilesGen.WriteText(Path.Combine(cs, "PetAttackExtensions.cs"), PetGen.AttackExtensionsSource());
            }
            // 上传了宠物图 → 拷进工程 + 生成最小场景；没上传就什么都不做（回退本体的 error.png 占位）
            PetGen.GenerateVisuals(profile, root, Log);
            Log($"  已生成召唤物 {pets.Count} 只：" + string.Join("、", pets.Select(d =>
                    $"{d.ClassName}（「{d.DisplayName}」，生命 {d.Hp}，站位 {d.StandDistance}"
                    + (d.Guardian ? "，替主人承伤" : "") + "）"))
                + (pets.Any(d => d.HasImage) ? "，视觉用你上传的图" : "，视觉沿用本体的占位图（可上传自己的 PNG）"));
        }

        if (AncientPatchGen.HasDialogues(profile))
        {
            ProjectFilesGen.WriteText(Path.Combine(cs, "AncientDialoguePatch.cs"), AncientPatchGen.Source(profile));
            Log($"  已生成先古之民对话补丁（{profile.Ancients.Count(a => a.HasAnyText)} 位先古之民）");
        }

        // 初始卡组里的本体卡（打击 / 防御）跟随本模组的卡框颜色与能量图标
        if (FlavorGen.NeedsVanillaSkin(profile))
        {
            ProjectFilesGen.WriteText(Path.Combine(cs, "VanillaCardSkin.cs"), FlavorGen.VanillaSkinSource(profile));
            Log("  已生成本体卡外观补丁（初始卡组里的 "
                + string.Join("、", FlavorGen.VanillaSkinCards(profile)) + " 跟随本模组的卡框/能量图标）");
        }

        // 死亡结算文案 + 阵亡后台词
        if (FlavorGen.NeedsDeathFlavor(profile))
        {
            ProjectFilesGen.WriteText(Path.Combine(cs, "DeathFlavor.cs"), FlavorGen.DeathFlavorSource(profile));
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(profile.DeathText)) bits.Add("死亡结算文案");
            if (!string.IsNullOrWhiteSpace(profile.DeadBanterText)) bits.Add("阵亡后台词气泡");
            Log("  已生成死亡文本补丁（" + string.Join(" + ", bits) + "）");
        }

        if (AncientPatchGen.HasRelicReplacements(profile))
        {
            ProjectFilesGen.WriteText(Path.Combine(cs, "AncientRelicReplace.cs"), AncientPatchGen.RelicReplacementSource(profile));
            var withReplace = profile.Ancients.Where(a => a.HasAnyRelicReplacement).ToList();
            Log($"  已生成先古之民遗物选项替换补丁（{withReplace.Count} 位先古之民："
                + string.Join("、", withReplace.Select(a => a.AncientId)) + "）");
        }

        if (CSharpCodeGen.HasExtraResourceUi(profile))
        {
            ProjectFilesGen.WriteText(Path.Combine(cs, "ExtraResourceUi.cs"), CSharpCodeGen.ExtraResourceUiSource(profile));
            Log($"  已生成额外资源量外观补丁（名字「{LocalizationGen.ResourceName(profile)}」"
                + (ArtGenerator.ExtraResourceIconResPath(profile) is null ? "，图标沿用本体" : "，图标用你上传的图") + "）");
        }

        var delayed = CSharpCodeGen.CollectDelayedEffects(profile).ToList();
        if (delayed.Count > 0)
            ProjectFilesGen.WriteText(Path.Combine(cs, "DelayedPowers.cs"), CSharpCodeGen.DelayedPowersSource(profile));

        // 本体「缓慢」数值助手：只有配了「直接把缓慢设成 N%」才生成
        if (CSharpCodeGen.NeedsSlowPowerHelper(profile))
        {
            ProjectFilesGen.WriteText(Path.Combine(cs, "SlowPowerHelper.cs"), CSharpCodeGen.SlowPowerHelperSource(profile));
            int nSlow = CSharpCodeGen.SlowPercentEffects(profile).Count();
            Log($"  已生成「缓慢」数值助手（{nSlow} 条效果直接设百分比：本体「缓慢」显示/生效的是每打出一张牌 +10%，跟层数无关）");
        }

        // 自定义状态（能力牌用）：每个状态一个 PowerModel 子类
        int powerCount = 0;
        for (int i = 0; i < profile.CustomPowers.Count; i++)
        {
            var cp = profile.CustomPowers[i];
            if (!CustomPowerGen.IsActive(cp)) continue;
            string cls = CustomPowerGen.ClassNameOf(profile, cp, i);
            ProjectFilesGen.WriteText(Path.Combine(cs, "Powers", cls + ".cs"), CustomPowerGen.Source(profile, cp, i));
            powerCount++;
        }
        if (powerCount > 0)
            Log($"  已生成自定义状态（能力）{powerCount} 个："
                + string.Join("、", CustomPowerGen.Active(profile).Select((cp, k) => cp.Name)));

        if (VanillaPowerGen.NeedsCosmeticPatch(profile))
        {
            ProjectFilesGen.WriteText(Path.Combine(cs, "VanillaPowerOverride.cs"), VanillaPowerGen.Source(profile));
            int nVanilla = VanillaPowerGen.Active(profile).Count;
            Log($"  已生成状态外观补丁（本体状态改写 {nVanilla} 个"
                + (powerCount > 0 ? $" + 自定义状态 {powerCount} 个" : "") + "）");
        }

        Log($"  C# 源码：角色 1 + 池 3 + 卡牌 {profile.Cards.Count(c => !c.IsVanillaCard)}"
            + (profile.Cards.Any(c => c.IsVanillaCard) ? $"（另有本体卡引用 {profile.Cards.Count(c => c.IsVanillaCard)} 条，不生成类）" : "")
            + $" + 遗物 {profile.Relics.Count} + 药水 {profile.Potions.Count} + 自定义状态 {powerCount} + 延迟 Power {delayed.Count}");

        string locRoot = Path.Combine(root, n.ModId, "localization", "zhs");
        // 本体状态改名：本体卡牌/遗物/药水描述里写的旧名字一起换掉（用户要求：卡面描述也得跟着改）
        var vanillaText = VanillaPowerGen.VanillaTextReplacements(profile).ToList();
        if (vanillaText.Count > 0)
            Log($"  本体状态改名：顺带覆盖了 {vanillaText.Count} 条本体本地化条目"
                + $"（卡牌 {vanillaText.Count(t => t.Table == "cards")} / 遗物 {vanillaText.Count(t => t.Table == "relics")} / 药水 {vanillaText.Count(t => t.Table == "potions")}）");
        // 本体关键词改名：同样要把本体卡面描述里写着的旧关键词名（比如「消耗」两个字）换掉
        var keywordText = VanillaKeywordGen.KeywordTextReplacements(profile).ToList();
        if (keywordText.Count > 0)
        {
            vanillaText.AddRange(keywordText);
            Log($"  本体关键词改名：顺带覆盖了 {keywordText.Count} 条本体本地化条目"
                + $"（卡牌 {keywordText.Count(t => t.Table == "cards")} / 遗物 {keywordText.Count(t => t.Table == "relics")} / 药水 {keywordText.Count(t => t.Table == "potions")}）");
        }

        ProjectFilesGen.WriteText(Path.Combine(locRoot, "characters.json"), LocalizationGen.CharactersJson(profile));
        ProjectFilesGen.WriteText(Path.Combine(locRoot, "cards.json"),
            LocalizationGen.MergeVanillaText(LocalizationGen.CardsJson(profile), vanillaText, "cards"));
        ProjectFilesGen.WriteText(Path.Combine(locRoot, "relics.json"),
            LocalizationGen.MergeVanillaText(LocalizationGen.RelicsJson(profile), vanillaText, "relics"));
        ProjectFilesGen.WriteText(Path.Combine(locRoot, "potions.json"),
            LocalizationGen.MergeVanillaText(LocalizationGen.PotionsJson(profile), vanillaText, "potions"));
        ProjectFilesGen.WriteText(Path.Combine(locRoot, "ancients.json"), LocalizationGen.AncientsJson(profile));
        bool needPowersLoc = delayed.Count > 0 || VanillaPowerGen.LocEntries(profile).Any() || powerCount > 0;
        if (needPowersLoc)
            ProjectFilesGen.WriteText(Path.Combine(locRoot, "powers.json"), LocalizationGen.PowersJson(profile));
        // 自定义名字：覆盖本体的「辉星」悬停提示（本体加载时会把模组的同名表并进来覆盖）
        string hoverTips = LocalizationGen.StaticHoverTipsJson(profile);
        if (hoverTips.Contains("STAR_COUNT", StringComparison.Ordinal))
            ProjectFilesGen.WriteText(Path.Combine(locRoot, "static_hover_tips.json"), hoverTips);
        // 自定义关键词 / 本体关键词改名：都写进本体的 card_keywords 表（逐键合并）。
        // 有关键词改名时也必须写这个文件 —— 不生成的话，改的名字在游戏里根本不生效。
        bool needKeywordsLoc = VanillaKeywordGen.HasAny(profile);
        if (needKeywordsLoc)
            ProjectFilesGen.WriteText(Path.Combine(locRoot, "card_keywords.json"), LocalizationGen.KeywordsJson(profile));
        Log("  本地化：characters / cards / relics / potions / ancients"
            + (needPowersLoc ? " / powers" : "")
            + (needKeywordsLoc ? " / card_keywords（自定义关键词 / 本体关键词改名）" : "")
            + (PetGen.IsActive(profile) ? " / monsters（召唤物的名字）" : ""));

        ArtGenerator.Generate(profile, root, Log);
        ProjectFilesGen.CopyLocalDependencies(profile, root, Log);
        ProjectFilesGen.WriteText(Path.Combine(root, "README.md"), Readme(profile, n));

        Log("生成完成 ✅");
        // 生成后自检如果发现了错误（例如卡池引用了没生成的卡类），就不能装作成功 ——
        // 让界面弹框说明原因，而不是等 dotnet 抛一句看不懂的 CS0246。
        bool okAfterGen = !issues.Any(i => i.IsError);
        if (!okAfterGen)
        {
            Log("生成后自检没通过，已按失败处理：");
            foreach (var i in issues.Where(i => i.IsError)) Log("  " + i);
        }
        return new GenerationResult(okAfterGen, root, issues, lines);
    }

    /// <summary>
    /// 这个配置会生成到哪个工程目录。
    /// 目录名用**存档文件名**（不是 ModId）：两个存档即使 ModId 相同也是各自的目录，
    /// 不会互相覆盖生成文件（用户报过「存档之间互相影响 / 改了存档名就构建失败」）。
    /// 注意：**别再自己拼 `OutputDir\ModId`** —— 存档名和 ModId 不一样时那是个不存在的目录
    /// （踩过：`dotnet build` 报 MSB1009 项目文件不存在 → 界面显示「构建失败」）。
    /// </summary>
    public static string ProjectRootOf(CharacterProfile profile)
    {
        var n = Naming.From(profile);
        string outputDir = string.IsNullOrWhiteSpace(profile.Paths.OutputDir)
            ? Path.Combine(AppContext.BaseDirectory, "自定义角色")
            : profile.Paths.OutputDir;
        return Path.Combine(outputDir, SafeFolderName(profile.SaveName) ?? n.ModId);
    }

    /// <summary>存档名 → 安全的目录名（去掉路径非法字符；空/非法就返回 null 让调用方退回 ModId）。</summary>
    private static string? SafeFolderName(string? saveName)
    {
        string s = (saveName ?? "").Trim();
        if (s.Length == 0) return null;
        foreach (char bad in Path.GetInvalidFileNameChars()) s = s.Replace(bad, '_');
        s = s.Trim().TrimEnd('.');
        return s.Length == 0 ? null : s;
    }

    /// <summary>这个目录看着像「本工具生成过的工程」吗？（清理旧工程前的安全阀，别把随便一个目录删了）</summary>
    private static bool LooksLikeGeneratedProject(string dir)
    {
        try
        {
            return File.Exists(Path.Combine(dir, "project.godot"))
                   || File.Exists(Path.Combine(dir, "mod_manifest.json"))
                   || Directory.Exists(Path.Combine(dir, "cs"));
        }
        catch { return false; }
    }
    /// <summary>README / 日志里那句「初始卡组」的说明（本体卡引用按「打击 ×5」这种写法列出来）。</summary>
    public static string InitialDeckSummary(CharacterProfile p)
    {
        var parts = new List<string>();
        foreach (var c in p.Cards.Where(c => c.IsVanillaCard))
            parts.Add($"{c.Name}（本体 {c.ClassName}）×{Math.Max(1, c.StartingCopies)}");
        var own = p.Cards.Where(c => !c.IsVanillaCard && c.InStartingDeck)
            .Select(c => $"{c.Name}×{Math.Max(1, c.StartingCopies)}").ToList();
        if (own.Count > 0) parts.Add("自定义初始牌：" + string.Join("、", own));
        return parts.Count == 0 ? "（只有后面重新抽到/获得的牌，开局是空的）" : string.Join(" + ", parts);
    }

    private static string Readme(CharacterProfile p, Naming n) => $"""
# {n.ModId} —— 自定义角色「{p.DisplayName}」

> 由 **Sts2CharForge** 生成。改配置后重新生成会覆盖 `cs/` 与本地化文件，手改请另存。

## 角色
- 类名 / 模型 ID：`{n.CharClass}` / `{n.CharEntry}`
- 生命 {p.StartingHp} · 金币 {p.StartingGold} · 性别 {p.Gender}
- 初始卡组：{InitialDeckSummary(p)}
- 初始遗物：{(p.Relics.Any(r => r.IsStartingRelic) ? "自定义遗物" : (p.ExtraResource.Enabled ? "隐藏的资源量遗物（额外资源量用）" : "无（开局不带遗物）"))}

## 自定义内容
| 类型 | 数量 |
|---|---|
| 卡牌 | {p.Cards.Count(c => !c.IsVanillaCard)} |
| 遗物 | {p.Relics.Count} |
| 药水 | {p.Potions.Count} |

## 构建 / 安装
```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File build.ps1 -Install "{p.Paths.InstallDir}"
```

## 内置的本体兼容补丁（别删）
1. 文本资源不转二进制（`project.godot` 的 `[editor]` 段）——否则导出时场景丢引用变空壳；
2. 精英 / Boss 结算保护 —— 本体按 6 个官方角色硬编码判断，自定义角色会抛异常导致不结算；
3. 建筑师通关对话注入 —— 缺了会通关结算空引用；
4. 音效 / 成就重定向到铁甲战士；
5. 能量图标 tpsheet 补丁 —— 让自己命名的 `card/energy_{n.EnergyColor}` 能被图集找到。

## 美术资源路径（未上传时用铁甲战士占位）
- 战斗立绘：`scenes/creature_visuals/{n.CharSlug}.tscn`（Spine 骨骼动画，当前引用铁甲战士的骨骼）
- 头像：`images/ui/top_panel/character_icon_{n.CharSlug}.png`
- 选人立绘：`images/packed/character_select/char_select_{n.CharSlug}.png`
- 能量图标：`images/atlases/ui_atlas.sprites/card/energy_{n.EnergyColor}.tres` + tpsheet 条目
- 卡面：`images/packed/card_portraits/{n.PoolTitle}/<卡牌ID小写>.png`（建议 1000×760）

## 构建环境
- 解包工程：`{p.Paths.VanillaProject}`
- 游戏 data：`{p.Paths.GameDataDir}`
- Godot：`{p.Paths.GodotExe}`
""";
}
