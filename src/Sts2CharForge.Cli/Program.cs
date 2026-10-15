using Sts2CharForge.Core.Build;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Generation;
using Sts2CharForge.Core.Profile;

// Sts2CharForge 命令行：与界面共用同一套核心
//   forge --check                                   自检（效果库数量等）
//   forge --demo profile.json                       写出示例配置
//   forge --profile profile.json                    只生成工程
//   forge --profile profile.json --build            生成 + 编译 + 导出 PCK（**不安装**）
//   forge --profile profile.json --build --install "D:\...\mods"   还要安装到游戏
//   forge --profile profile.json --out "D:\out"     指定输出目录
//   forge --buildcheck                              自检：不给 --install 时**绝不安装**（红线⑧）
//
// ⚠⚠ **安装必须显式给 `--install <mods 目录>`**（V0.2.137 起，红线⑧）：
//   以前这里写的是 `installDir ?? profile.Paths.InstallDir` —— 不给 `--install` 时会**退回用存档里的
//   `Paths.InstallDir`**（那个字段是**给图形界面用的**），于是「只想编译一下」的探针会把模组真的装进
//   存档里写着的游戏 mods 目录（V0.2.135 / V0.2.137 各发生过一次，用户明确要求「以后不需要处理我的存档 /
//   也不要动我的游戏目录」）。现在改成：**`--install` 指哪装哪、没给就一个文件都不装**。

string? profilePath = null, outDir = null, installDir = null, recoverDir = null, sampleGenDir = null, becCheckGodotExe = null, fieldMatrixOut = null, powerProbeOut = null, countProbeOut = null, remProbeOut = null, formulaProbeOut = null, graphModProbeOut = null, multiProbeOut = null, dbgProbeOut = null, descProbeOut = null, petProbeOut = null, newProbeOut = null, energyProbeOut = null;
bool doBuild = false, demo = false, check = false, detect = false, buildCheck = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--profile": profilePath = Next(args, ref i); break;
        case "--out": outDir = Next(args, ref i); break;
        case "--install": installDir = Next(args, ref i); break;
        case "--recover": recoverDir = Next(args, ref i); break;
        case "--samplegen": sampleGenDir = Next(args, ref i); break;
        case "--fieldmatrix": fieldMatrixOut = Next(args, ref i); break;
        case "--powerprobe": powerProbeOut = Next(args, ref i); break;
        case "--countprobe": countProbeOut = Next(args, ref i); break;
        case "--remprobe": remProbeOut = Next(args, ref i); break;
        case "--formulaprobe": formulaProbeOut = Next(args, ref i); break;
        case "--graphmodprobe": graphModProbeOut = Next(args, ref i); break;
        case "--multiprobe": multiProbeOut = Next(args, ref i); break;
        case "--dbgprobe": dbgProbeOut = Next(args, ref i); break;
        case "--descprobe": descProbeOut = Next(args, ref i); break;
        case "--petprobe": petProbeOut = Next(args, ref i); break;
        case "--newprobe": newProbeOut = Next(args, ref i); break;
        case "--energyprobe": energyProbeOut = Next(args, ref i); break;
        case "--build": doBuild = true; break;
        case "--buildcheck": buildCheck = true; break;
        case "--godot": becCheckGodotExe = Next(args, ref i); break;
        case "--demo": demo = true; break;
        case "--check": check = true; break;
        case "--detect": detect = true; break;
        case "-h":
        case "--help": PrintHelp(); return 0;
        default:
            if (args[i].EndsWith(".json", StringComparison.OrdinalIgnoreCase) && profilePath is null) profilePath = args[i];
            break;
    }
}

// --samplegen <dir>：用 ProfileFactory.Sample() 生成一遍，并把每个输出文件的 SHA256 打出来。
// 给「改动前后生成结果逐字节一致」这条红线做回归对照用（本机解包工程从保存过的存档里取）。
if (sampleGenDir is not null)
{
    CharacterProfile sample = ProfileFactory.Sample();
    // 用「解包工程」的搜索（和界面上的路径页同一套：PathAutoDetect）：找到哪个用哪个。
    // 刻意不读用户存档（回归对照不该依赖别人的配置）。
    if (string.IsNullOrWhiteSpace(sample.Paths.VanillaProject))
        sample.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? "";
    if (string.IsNullOrWhiteSpace(sample.Paths.VanillaProject))
        sample.Paths.VanillaProject = PathAutoDetect.FindVanillaProject() ?? "";
    string? sampleGame = Environment.GetEnvironmentVariable("FORGE_GAME");
    if (string.IsNullOrWhiteSpace(sampleGame)) sampleGame = PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(sampleGame) && string.IsNullOrWhiteSpace(sample.Paths.GameDataDir))
        sample.Paths.GameDataDir = Path.Combine(sampleGame, "data_sts2_windows_x86_64");
    sample.Paths.OutputDir = sampleGenDir;
    EffectCatalog.Initialize(sample.Paths.VanillaProject, sample.Paths.GameDataDir);
    var sampleResult = ModGenerator.Generate(sample, m => { });
    if (!sampleResult.Success)
    {
        Console.WriteLine("[X] 示例配置生成失败：");
        foreach (var issue in sampleResult.Issues.Where(x => x.IsError)) Console.WriteLine("    " + issue);
        return 1;
    }
    foreach (string file in Directory.GetFiles(sampleResult.ProjectRoot, "*", SearchOption.AllDirectories)
                 .OrderBy(x => x, StringComparer.Ordinal))
    {
        byte[] bytes = File.ReadAllBytes(file);
        Console.WriteLine(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
            + "  " + Path.GetRelativePath(sampleResult.ProjectRoot, file).Replace('\\', '/'));
    }
    return 0;
}

// --recover：从生成出来的工程反推回配置存档（存档被误覆盖时的救命功能）
if (recoverDir is not null)
{
    try
    {
        CharacterProfile? template = null;
        if (profilePath is not null && File.Exists(profilePath)) template = ProfileFactory.Load(profilePath);
        EffectCatalog.Initialize(template?.Paths.VanillaProject, template?.Paths.GameDataDir);
        RecoveryResult rec = ProjectRecovery.FromProject(recoverDir, template, Path.GetDirectoryName(Path.GetFullPath(outDir ?? Path.Combine(recoverDir, "x.json"))));
        string target = outDir ?? Path.Combine(recoverDir, Path.GetFileName(recoverDir.TrimEnd('\\', '/')) + "_恢复.json");
        ProfileFactory.Save(rec.Profile, target);
        Console.WriteLine("已恢复：" + target);
        foreach (string note in rec.Notes) Console.WriteLine("  · " + note);
        // V0.2.134：锻造节点图（`// CET:ForgeGraph=` 标记）认回来几张 —— 在 notes 之后单独列一行，
        // 让命令行输出里一眼看得到「图到底回没回来」（老工程这一行是 0，**不是错误**）。
        Console.WriteLine($"  · 锻造节点图：{rec.IsForged.Count} 张"
            + (rec.IsForged.Count > 0 ? "（" + string.Join("、", rec.IsForged) + "）" : ""));
        if (rec.HasUnparsed)
        {
            Console.WriteLine($"  ⚠ 有 {rec.Unparsed.Count} 处生成代码没认出来（在界面上人工补一下）：");
            foreach (string line in rec.Unparsed.Take(30)) Console.WriteLine("      " + line);
        }
        return 0;
    }
    catch (Exception e)
    {
        Console.WriteLine("[X] 恢复失败：" + e.Message);
        return 1;
    }
}

// --fieldmatrix <out.tsv>：【开发用】量「每一种效果到底读哪些栏位」。
// 真正的测量在 Core（Sts2CharForge.Core.Effects.EffectFieldUsage）里，和 --uicheck 那条
// 「显示的栏位都被用到、藏起来的都没用到」的断言**共用同一份实现**（不会两处漂移）。
// 输出 TSV：chain / kind / field / read。
if (fieldMatrixOut is not null)
{
    CharacterProfile probeProfile = EffectFieldUsage.CreateScratch();
    if (string.IsNullOrWhiteSpace(probeProfile.Paths.VanillaProject))
        probeProfile.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoDetect.FindVanillaProject() ?? "";
    string? probeGame = Environment.GetEnvironmentVariable("FORGE_GAME");
    if (string.IsNullOrWhiteSpace(probeGame)) probeGame = PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(probeGame) && string.IsNullOrWhiteSpace(probeProfile.Paths.GameDataDir))
        probeProfile.Paths.GameDataDir = Path.Combine(probeGame, "data_sts2_windows_x86_64");
    EffectCatalog.Initialize(probeProfile.Paths.VanillaProject, probeProfile.Paths.GameDataDir);

    const string probeCard = "SevenSlash";
    string probePotion = EffectCatalog.Potions.Count > 0 ? EffectCatalog.Potions[0].Id : "FirePotion";
    var matrixLines = new List<string> { "chain\tkind\tfield\tread" };
    var matrixSummary = new List<string>();
    foreach (string chain in new[] { EffectFieldUsage.CardChain, EffectFieldUsage.RelicChain, EffectFieldUsage.PotionChain })
    {
        foreach (EffectKindOption option in EffectCatalog.EffectKinds)
        {
            IReadOnlyList<(EffectFieldProbe Probe, bool Read)> measured =
                EffectFieldUsage.Measure(probeProfile, chain, option.Kind, probeCard, probePotion, out string? baselineError);
            foreach ((EffectFieldProbe probe, bool read) in measured)
                matrixLines.Add($"{chain}\t{option.Kind}\t{probe.Name}\t{(read ? 1 : 0)}");
            matrixSummary.Add($"[{chain}] {option.Kind}\t"
                + (baselineError is not null
                    ? "基线生成失败: " + baselineError
                    : string.Join(",", measured.Where(m => m.Read).Select(m => m.Probe.Name))));
        }
    }

    File.WriteAllLines(fieldMatrixOut, matrixLines, new System.Text.UTF8Encoding(false));
    Console.WriteLine("== 每种效果真正读到的栏位 ==");
    foreach (string line in matrixSummary) Console.WriteLine(line);
    Console.WriteLine("明细（chain/kind/field/read）已写入：" + fieldMatrixOut);
    return 0;
}

// --powerprobe <out.cs>：【开发用】造一个「自定义状态 + 层数到达 N 触发 + 生成卡牌」的探针，
// 把它生成出来的状态类源码写到文件（查「层数到了却没生成卡牌 / 数值怎么变成层数了」这类问题时用）。
if (powerProbeOut is not null)
{
    CharacterProfile probe = ProfileFactory.Sample();
    if (string.IsNullOrWhiteSpace(probe.Paths.VanillaProject))
        probe.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoDetect.FindVanillaProject() ?? "";
    string? pg = Environment.GetEnvironmentVariable("FORGE_GAME");
    if (string.IsNullOrWhiteSpace(pg)) pg = PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(pg) && string.IsNullOrWhiteSpace(probe.Paths.GameDataDir))
        probe.Paths.GameDataDir = Path.Combine(pg, "data_sts2_windows_x86_64");
    EffectCatalog.Initialize(probe.Paths.VanillaProject, probe.Paths.GameDataDir);

    var probePower = new CustomPowerSpec { Name = "探针层数状态", Enabled = true };
    // ⚠ 故意**不设** TriggerAmount（用户那条「层数到达 N」在界面上根本没有 N 输入框，
    // 所以跑到生成端时它就是默认值）—— 看生成出来的守卫会是什么样。
    var probeTrigger = new PowerTriggerSpec { Kind = "PowerReached", PowerTarget = "Self", PowerId = "ProbeStack" };
    probeTrigger.Effects.Add(new EffectSpec { Kind = "GenerateCard", Amount = 2m, SpawnCardId = "SevenSlash" });
    probeTrigger.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 7m, TargetSide = "Enemy" });
    probePower.Triggers.Add(probeTrigger);

    // 另造一条：盯**自己这个状态**（用户最可能的意图）——看生成的守卫放不放行
    var probeSelf = new CustomPowerSpec { Name = "探针盯自己", ClassName = "ProbeSelfStack", Enabled = true };
    var selfTrigger = new PowerTriggerSpec { Kind = "PowerReached", PowerTarget = "Self", PowerId = "ProbeSelfStack", TriggerAmount = 3m };
    selfTrigger.Effects.Add(new EffectSpec { Kind = "GenerateCard", Amount = 2m, SpawnCardId = "SevenSlash" });
    probeSelf.Triggers.Add(selfTrigger);

    probe.CustomPowers.Clear();
    probe.CustomPowers.Add(probePower);
    probe.CustomPowers.Add(probeSelf);
    probe.CustomPowers.Add(new CustomPowerSpec
    {
        Name = "探针条件", Enabled = true,
        Triggers =
        {
            new PowerTriggerSpec
            {
                Kind = "PowerChanged", PowerTarget = "Self",
                Effects =
                {
                    new EffectSpec
                    {
                        Kind = "GenerateCard", Amount = 2m, SpawnCardId = "SevenSlash",
                        Condition = new ConditionSpec { Kind = "PowerJustReached", Amount = 4m, PowerId = "ProbeStack", WhenUnmet = "Skip" },
                    },
                },
            },
        },
    });

    var probeLines = new List<string>();
    probeLines.Add("// ===== 自动描述（游戏里状态悬停那段文字）=====");
    foreach (CustomPowerSpec cp in probe.CustomPowers)
        probeLines.Add("// " + cp.Name + " → " + PowerTriggers.AutoDescription(cp).Replace("\n", " ⏎ "));
    for (int i = 0; i < probe.CustomPowers.Count; i++)
    {
        probeLines.Add("");
        probeLines.Add("// ===== " + probe.CustomPowers[i].Name + " 的生成源码 =====");
        probeLines.Add(CustomPowerGen.Source(probe, probe.CustomPowers[i], i));
    }
    File.WriteAllLines(powerProbeOut, probeLines, new System.Text.UTF8Encoding(false));
    Console.WriteLine("探针状态类源码已写入：" + powerProbeOut);
    return 0;
}

// --countprobe <out.cs>：【开发用】看「次数 / 概率 / 数值（公式）」生成出来算什么
//（排查「次数填 Y（额外资源量）只触发一次」这类问题）。
if (countProbeOut is not null)
{
    CharacterProfile probe = ProfileFactory.Sample();
    if (string.IsNullOrWhiteSpace(probe.Paths.VanillaProject))
        probe.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoDetect.FindVanillaProject() ?? "";
    string? pg2 = Environment.GetEnvironmentVariable("FORGE_GAME");
    if (string.IsNullOrWhiteSpace(pg2)) pg2 = PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(pg2) && string.IsNullOrWhiteSpace(probe.Paths.GameDataDir))
        probe.Paths.GameDataDir = Path.Combine(pg2, "data_sts2_windows_x86_64");
    EffectCatalog.Initialize(probe.Paths.VanillaProject, probe.Paths.GameDataDir);

    var probeLines = new List<string>();
    var cases = new (string Name, EffectSpec Fx)[]
    {
        ("生效次数（公式）= Y", new EffectSpec { Kind = "Block", Amount = 5m, TargetSide = "Self", TimesFormula = "Y" }),
        ("命中次数（公式）= Y", new EffectSpec { Kind = "Damage", Amount = 5m, TargetSide = "Enemy", RepeatFormula = "Y" }),
        ("概率（公式）= Y",     new EffectSpec { Kind = "Draw", Amount = 1m, TargetSide = "Self", ChanceEnabled = true, ChanceFormula = "Y" }),
        ("数值（公式）= Y",     new EffectSpec { Kind = "Block", Amount = 5m, TargetSide = "Self", AmountFormula = "Y" }),
    };
    int ci = 0;
    foreach ((string name, EffectSpec fx) in cases)
    {
        var card = new CardSpec { Name = "次数探针" + (++ci), ClassName = "CountProbe" + ci, CardType = "Skill", Rarity = "Common", Cost = 1 };
        card.Effects.Add(fx);
        probe.Cards.Add(card);
        probeLines.Add("// ================= " + name + " =================");
        probeLines.Add(CSharpCodeGen.CardSource(probe, card, probe.Cards.Count - 1));
        probeLines.Add("");
    }
    File.WriteAllLines(countProbeOut, probeLines, new System.Text.UTF8Encoding(false));
    Console.WriteLine("次数探针已写入：" + countProbeOut);
    return 0;
}

// --remprobe <out.cs>：【开发用】看「某个状态被移除时」这条触发时机生成出来是什么
//（排查「状态里监听某状态被移除时无法触发」这类问题）。
if (remProbeOut is not null)
{
    CharacterProfile probe = ProfileFactory.Sample();
    if (string.IsNullOrWhiteSpace(probe.Paths.VanillaProject))
        probe.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoDetect.FindVanillaProject() ?? "";
    string? pg3 = Environment.GetEnvironmentVariable("FORGE_GAME");
    if (string.IsNullOrWhiteSpace(pg3)) pg3 = PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(pg3) && string.IsNullOrWhiteSpace(probe.Paths.GameDataDir))
        probe.Paths.GameDataDir = Path.Combine(pg3, "data_sts2_windows_x86_64");
    EffectCatalog.Initialize(probe.Paths.VanillaProject, probe.Paths.GameDataDir);

    var remLines = new List<string>();
    var probes = new (string Name, string Target, string PowerId)[]
    {
        ("盯自己身上任意状态", "Self", ""),
        ("盯自己身上的虚弱", "Self", "VulnerablePower"),
        ("盯敌人身上的虚弱", "AnyEnemy", "VulnerablePower"),
    };
    foreach ((string name, string target, string powerId) in probes)
    {
        var cp = new CustomPowerSpec { Name = "移除探针" + name, ClassName = "RemProbe" + probe.CustomPowers.Count, Enabled = true };
        var trig = new PowerTriggerSpec { Kind = "PowerRemoved", PowerTarget = target, PowerId = powerId };
        trig.Effects.Add(new EffectSpec { Kind = "Block", Amount = 3m, TargetSide = "Self" });
        cp.Triggers.Add(trig);
        probe.CustomPowers.Add(cp);
        remLines.Add("// ================= " + name + "（状态侧） =================");
        remLines.Add(CustomPowerGen.Source(probe, cp, probe.CustomPowers.Count - 1));
        remLines.Add("");
    }
    // V0.2.168：两条**盯的就是它自己这个状态**、但「盯谁」选了「任意敌人」的探针 ——
    // 用户实测报的「某个状态被移除时完全没反应」就是这个形状：老守卫要求「那份状态在**别的**生物身上」，
    // 于是这份状态自己那份被移除时被挡掉。现在按「这份状态自己」生成（守卫 = power.Owner == base.Owner）。
    foreach (var (pname, pk, extra) in new (string, string, Action<PowerTriggerSpec>)[]
    {
        ("移除探针盯自己这份（盯任意敌人）", "PowerRemoved", _ => { }),
        ("到达探针盯自己这份（盯任意敌人）", "PowerReached", t => t.TriggerAmount = 100m),
    })
    {
        var cp = new CustomPowerSpec { Name = pname, ClassName = "RemProbe" + probe.CustomPowers.Count, Enabled = true };
        var trig = new PowerTriggerSpec { Kind = pk, PowerTarget = "AnyEnemy", PowerId = cp.ClassName };
        extra(trig);
        trig.Effects.Add(new EffectSpec { Kind = "Block", Amount = 3m, TargetSide = "Self" });
        cp.Triggers.Add(trig);
        probe.CustomPowers.Add(cp);
        remLines.Add("// ================= " + pname + "（状态侧） =================");
        remLines.Add(CustomPowerGen.Source(probe, cp, probe.CustomPowers.Count - 1));
        remLines.Add("");
    }

    // V0.2.168：「每累计 N 次触发一次」——**从这个状态被施加那一刻开始数**（用户实测：
    // 老写法把上状态之前打过的牌也算进去了，「要打 4 张」上状态前打了 3 张时上状态后只要 1 张就触发）。
    foreach (var (cname, ckind, cscope) in new (string, string, string)[]
    {
        ("计数探针（打牌·本回合）", "CardPlayed", "Turn"),
        ("计数探针（打牌·本场战斗）", "CardPlayed", "Combat"),
        ("计数探针（敌人死亡·本场战斗）", "EnemyDeath", "Combat"),
    })
    {
        var cp = new CustomPowerSpec { Name = cname, ClassName = "RemProbe" + probe.CustomPowers.Count, Enabled = true };
        var trig = new PowerTriggerSpec { Kind = ckind, TriggerAmount = 4m, TriggerScope = cscope };
        trig.Effects.Add(new EffectSpec { Kind = "Block", Amount = 3m, TargetSide = "Self" });
        cp.Triggers.Add(trig);
        probe.CustomPowers.Add(cp);
        remLines.Add("// ================= " + cname + "（状态侧） =================");
        remLines.Add(CustomPowerGen.Source(probe, cp, probe.CustomPowers.Count - 1));
        remLines.Add("");
    }

    var remRelic = new RelicSpec { Name = "移除探针遗物", ClassName = "RemProbeRelic", Rarity = "Common", Trigger = "PowerRemoved", TriggerPowerId = "VulnerablePower" };    remRelic.Effects.Add(new EffectSpec { Kind = "Block", Amount = 3m, TargetSide = "Self" });
    probe.Relics.Add(remRelic);
    remLines.Add("// ================= 遗物侧 =================");
    remLines.Add(CSharpCodeGen.RelicSource(probe, remRelic, probe.Relics.Count - 1));
    remLines.Add("");
    remLines.Add("// ================= 补丁（PowerRemoval.cs） =================");
    remLines.Add(CSharpCodeGen.PowerRemovalSource(probe));
    File.WriteAllLines(remProbeOut, remLines, new System.Text.UTF8Encoding(false));
    // 顺手把这份探针存档也写出来：`forge --profile <它> --out <dir> --build` 就能验
    // 「这几条时机生成出来的代码真的编译得过」（V0.2.168）。
    string remJson = Path.ChangeExtension(remProbeOut, ".json");
    probe.Paths.OutputDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(remProbeOut)) ?? ".", "gen_rem");
    ProfileFactory.Save(probe, remJson);
    Console.WriteLine("移除探针已写入：" + remProbeOut + " / " + remJson);
    return 0;
}

// --formulaprobe <out.cs>：【开发用】看「数值公式」在遗物 / 药水 / 自定义状态上生成出来是什么。
if (formulaProbeOut is not null)
{
    CharacterProfile probe = ProfileFactory.Sample();
    if (string.IsNullOrWhiteSpace(probe.Paths.VanillaProject))
        probe.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoDetect.FindVanillaProject() ?? "";
    string? pg4 = Environment.GetEnvironmentVariable("FORGE_GAME");
    if (string.IsNullOrWhiteSpace(pg4)) pg4 = PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(pg4) && string.IsNullOrWhiteSpace(probe.Paths.GameDataDir))
        probe.Paths.GameDataDir = Path.Combine(pg4, "data_sts2_windows_x86_64");
    EffectCatalog.Initialize(probe.Paths.VanillaProject, probe.Paths.GameDataDir);
    probe.CustomParams.Clear();
    probe.CustomParams.Add(new ParamSpec { Name = "M", Source = ParamSources.MaxHp, Target = ParamTargets.Self });
    probe.CustomParams.Add(new ParamSpec { Name = "A", Source = ParamSources.Power, Target = ParamTargets.ChosenEnemy, PowerId = "VulnerablePower" });

    var fl = new List<string>();
    var rel = new RelicSpec { Name = "公式探针遗物", ClassName = "FormulaProbeRelic", Rarity = "Common", Trigger = "PlayerTurnStart" };
    rel.Effects.Add(new EffectSpec { Kind = "Heal", Amount = 3m, TargetSide = "Self", AmountFormula = "M/2" });
    rel.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 3m, TargetSide = "Enemy", AmountFormula = "A+2" });
    probe.Relics.Add(rel);
    fl.Add("// ================= 遗物（回复生命 = M/2、伤害 = A+2） =================");
    fl.Add(CSharpCodeGen.RelicSource(probe, rel, probe.Relics.Count - 1));
    fl.Add("");
    var pot = new PotionSpec { Name = "公式探针药水", ClassName = "FormulaProbePotion", Rarity = "Common", Usage = "CombatOnly", TargetType = "Self" };
    pot.Effects.Add(new EffectSpec { Kind = "Block", Amount = 3m, TargetSide = "Self", AmountFormula = "M/4" });
    probe.Potions.Add(pot);
    fl.Add("// ================= 药水（格挡 = M/4） =================");
    fl.Add(CSharpCodeGen.PotionSource(probe, pot, probe.Potions.Count - 1));
    fl.Add("");
    var pw = new CustomPowerSpec { Name = "公式探针状态", ClassName = "FormulaProbePower", Enabled = true };
    var pwTrig = new PowerTriggerSpec { Kind = "PlayerTurnStart" };
    pwTrig.Effects.Add(new EffectSpec { Kind = "HpLoss", Amount = 3m, TargetSide = "Self", AmountFormula = "M/10" });
    pw.Triggers.Add(pwTrig);
    probe.CustomPowers.Add(pw);
    fl.Add("// ================= 自定义状态（失去生命 = M/10） =================");
    fl.Add(CustomPowerGen.Source(probe, pw, probe.CustomPowers.Count - 1));
    fl.Add("// ================= 状态描述 =================");
    fl.Add(PowerTriggers.AutoDescription(pw));
    fl.Add("// ================= 遗物 / 药水描述（本地化 JSON） =================");
    fl.Add(LocalizationGen.RelicsJson(probe));
    fl.Add(LocalizationGen.PotionsJson(probe));
    File.WriteAllLines(formulaProbeOut, fl, new System.Text.UTF8Encoding(false));
    Console.WriteLine("公式探针已写入：" + formulaProbeOut);
    // 顺带把这份探针存档也存下来（开发用：拿它跑 `forge --profile <它> --out <dir> --build` 验编译）
    string probeJson = Path.ChangeExtension(formulaProbeOut, ".json");
    ProfileFactory.Save(probe, probeJson);
    Console.WriteLine("（顺带）探针存档：" + probeJson);
    return 0;
}

// --graphmodprobe <json>：【开发用】造一份「锻造节点图 + 生成卡牌的附加处理 / 加关键词 / 复制份数」的存档，
// 拿它跑 `forge --profile <它> --out <dir> --build`，验「节点图里那几项真的编译得过」（V0.2.167）。
if (graphModProbeOut is not null)
{
    CharacterProfile probe = ProfileFactory.Sample();
    if (string.IsNullOrWhiteSpace(probe.Paths.VanillaProject))
        probe.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoDetect.FindVanillaProject() ?? "";
    string? pg5 = Environment.GetEnvironmentVariable("FORGE_GAME");
    if (string.IsNullOrWhiteSpace(pg5)) pg5 = PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(pg5)) probe.Paths.GameDataDir = Path.Combine(pg5, "data_sts2_windows_x86_64");
    probe.Paths.OutputDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(graphModProbeOut)) ?? ".", "gen");
    probe.Cards.Add(MakeGraphModCard("锻造附加处理卡", "GraphModCard", "SevenSlash"));
    probe.Cards.Add(MakeGraphModCard("锻造复制份数卡", "GraphCopyCard", "SevenGuard", copy: true));
    ProfileFactory.Save(probe, graphModProbeOut);
    Console.WriteLine("节点图附加处理探针已写入：" + graphModProbeOut);
    return 0;
}

// 造一张「锻造卡」：触发时机「卡牌被打出时」→【效果】生成 / 复制卡牌（带附加处理 + 关键词 [+ 复制份数]）。
static CardSpec MakeGraphModCard(string name, string cls, string target, bool copy = false)
{
    var card = new CardSpec { Name = name, ClassName = cls, CardType = "Skill", Rarity = "Common", Cost = 1, IsForged = true };
    card.ForgeGraph = new ForgeGraphSpec();
    var trig = new ForgeNodeSpec { Kind = "Trigger" };
    trig.SetField("Timing", ForgeTimings.CardPlayed);
    var eff = new ForgeNodeSpec { Kind = "Effect" };
    eff.SetField("Effect", copy ? "CopyCard" : "GenerateCard");
    eff.SetField("Amount", "2");
    eff.SetField("Pick", "Random");
    eff.SetField("SpawnCardId", target);
    eff.SetField("SpawnTo", "Hand");
    card.ForgeGraph.Nodes.AddRange(new[] { trig, eff });
    card.ForgeGraph.Links.Add(new ForgeLinkSpec { From = trig.Id, To = eff.Id, FromPort = 0, ToPort = 0 });
    // 新编辑器那份 spec（附加处理 / 加关键词 / 复制份数都在这上面 —— 老字段里没有它们）
    var spec = ForgeEffectBridge.Ensure(eff, authored: true)!;
    spec.SpawnUpgraded = true;
    spec.SpawnFreeThisTurn = true;
    spec.SpawnKeyword = "Retain";
    if (copy) spec.Copies = 3;
    ForgeEffectBridge.Bind(eff);
    return card;
}

// --multiprobe <json>：【开发用】造一份「一块布上**多块**【触发时机】」的存档（V0.2.168），
// 拿它跑 `forge --profile <它> --out <dir> --build`，验「多块时机真的各生成一个钩子、而且编译得过」。
//   ① 锻造卡 A：打出时（造成伤害）+ 这张牌被消耗时（抽 1 张牌）→ OnPlay 走私有方法，另一条是 AfterCardExhausted override；
//   ② 锻造遗物 B：遗物自己有两条触发时机（战斗开始 / 每回合开始），图上也各有一块 → 两块都**嵌进**遗物钩子里
//      （两个私有方法 ForgeGraphOnTrigger / ForgeGraphOnTrigger2）；
//   ③ 锻造卡 C：两块都设成「打出时」（撞同一个本体钩子）→ 校验器报错、生成端只写第一块（不出坏代码）。
if (multiProbeOut is not null)
{
    CharacterProfile probe = ProfileFactory.Sample();
    if (string.IsNullOrWhiteSpace(probe.Paths.VanillaProject))
        probe.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoDetect.FindVanillaProject() ?? "";
    string? mg = Environment.GetEnvironmentVariable("FORGE_GAME");
    if (string.IsNullOrWhiteSpace(mg)) mg = PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(mg)) probe.Paths.GameDataDir = Path.Combine(mg, "data_sts2_windows_x86_64");
    probe.Paths.OutputDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(multiProbeOut)) ?? ".", "gen");

    probe.Cards.Add(MakeMultiCard());
    probe.Relics.Add(MakeMultiRelic());
    ProfileFactory.Save(probe, multiProbeOut);
    // ③ 撞同一个钩子那一档单独写一份（它会**报错**，和上面那份干净的分开跑构建）
    string clashPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(multiProbeOut)) ?? ".",
        Path.GetFileNameWithoutExtension(multiProbeOut) + "_clash.json");
    var clash = ProfileFactory.Sample();
    clash.Paths = probe.Paths;
    clash.Cards.Add(MakeClashCard());
    ProfileFactory.Save(clash, clashPath);
    Console.WriteLine("多【触发时机】探针已写入：" + multiProbeOut + " / " + clashPath);
    foreach (var c in probe.Cards.Where(x => x is not null && x.IsForged))
        foreach (var (level, msg) in ForgeGraphGen.Issues(c!.ForgeGraph, probe, "Card", null))
            Console.WriteLine($"    [卡牌 {c.ClassName}] {level}：{msg[..Math.Min(70, msg.Length)]}…");
    foreach (var c in clash.Cards.Where(x => x is not null && x.IsForged))
        foreach (var (level, msg) in ForgeGraphGen.Issues(c!.ForgeGraph, clash, "Card", null))
            Console.WriteLine($"    [撞钩子 {c.ClassName}] {level}：{msg[..Math.Min(70, msg.Length)]}…");
    return 0;
}

// 一张锻造卡：两块【触发时机】（打出时 → 造成 6 点伤害；被消耗时 → 抽 1 张牌）。
static CardSpec MakeMultiCard()
{
    var card = new CardSpec { Name = "双时机卡", ClassName = "MultiTriggerCard", CardType = "Skill", Rarity = "Common", Cost = 1, IsForged = true };
    card.ForgeGraph = new ForgeGraphSpec();
    card.ForgeGraph.Nodes.Add(TriggerWith("CardPlayed", "Damage", 0, 0));
    card.ForgeGraph.Nodes.Add(TriggerWith("CardExhausted", "Draw", 0, 260));
    return card;

    ForgeNodeSpec TriggerWith(string timing, string effect, double x, double y)
    {
        var trig = new ForgeNodeSpec { Kind = "Trigger", X = x, Y = y };
        trig.SetField("Timing", timing);
        var eff = new ForgeNodeSpec { Kind = "Effect", X = x + 300, Y = y };
        eff.SetField("Effect", effect);
        eff.SetField("Amount", effect == "Damage" ? "6" : "1");
        card.ForgeGraph!.Nodes.Add(eff);
        card.ForgeGraph.Links.Add(new ForgeLinkSpec { From = trig.Id, To = eff.Id, FromPort = 0, ToPort = 0 });
        return trig;
    }
}

// 一张「两块都设成打出时」的锻造卡（撞同一个本体钩子：校验器要报错、生成端只写第一块）。
static CardSpec MakeClashCard()
{
    var card = new CardSpec { Name = "撞时机卡", ClassName = "ClashTriggerCard", CardType = "Skill", Rarity = "Common", Cost = 1, IsForged = true };
    card.ForgeGraph = new ForgeGraphSpec();
    foreach (double y in new[] { 0.0, 260.0 })
    {
        var trig = new ForgeNodeSpec { Kind = "Trigger", X = 0, Y = y };
        trig.SetField("Timing", ForgeTimings.CardPlayed);
        var eff = new ForgeNodeSpec { Kind = "Effect", X = 300, Y = y };
        eff.SetField("Effect", "Block");
        eff.SetField("Amount", "5");
        card.ForgeGraph.Nodes.Add(trig);
        card.ForgeGraph.Nodes.Add(eff);
        card.ForgeGraph.Links.Add(new ForgeLinkSpec { From = trig.Id, To = eff.Id, FromPort = 0, ToPort = 0 });
    }
    return card;
}

// 一只锻造遗物：遗物自己有两条触发时机，图上也有两块（都嵌进遗物自己的钩子 → 两个私有方法）。
static RelicSpec MakeMultiRelic()
{
    var relic = new RelicSpec { Name = "双时机遗物", ClassName = "MultiTriggerRelic", Rarity = "Common", IsForged = true, TriggerMode = "All" };
    relic.Triggers.Clear();
    relic.Triggers.Add(new RelicTriggerSpec { Kind = ForgeTimings.CombatStart });
    relic.Triggers.Add(new RelicTriggerSpec { Kind = ForgeTimings.PlayerTurnStart });
    relic.ForgeGraph = new ForgeGraphSpec();
    foreach (var (timing, effect, y) in new[] { (ForgeTimings.CombatStart, "Block", 0.0), (ForgeTimings.PlayerTurnStart, "Draw", 260.0) })
    {
        var trig = new ForgeNodeSpec { Kind = "Trigger", X = 0, Y = y };
        trig.SetField("Timing", timing);
        var eff = new ForgeNodeSpec { Kind = "Effect", X = 300, Y = y };
        eff.SetField("Effect", effect);
        eff.SetField("Amount", effect == "Block" ? "4" : "1");
        relic.ForgeGraph.Nodes.Add(trig);
        relic.ForgeGraph.Nodes.Add(eff);
        relic.ForgeGraph.Links.Add(new ForgeLinkSpec { From = trig.Id, To = eff.Id, FromPort = 0, ToPort = 0 });
    }
    return relic;
}

// --dbgprobe <json>：【开发用】造一份「生成调试日志 = 开」+ 锻造节点图（造成伤害 / 获得格挡 / 失去生命 /
// 施加状态 / 抽牌）的存档。用户实测报过：开着调试日志时生成的 cs/Cards/*.cs 编译不过
//（CS8641「else 不能用在语句的开头」+ CS1513 应输入 }）—— 真因是日志那一行挂着行尾注释
// `// CET:DebugLog`，紧跟着拼下一条语句时把**后面整条语句和 foreach 收尾的 }** 一起吃成了注释（V0.2.168 修）。
// 拿它跑 `forge --profile <它> --out <dir> --build` 就能验「日志开着也编译得过」。
if (dbgProbeOut is not null)
{
    CharacterProfile probe = ProfileFactory.Sample();
    if (string.IsNullOrWhiteSpace(probe.Paths.VanillaProject))
        probe.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoDetect.FindVanillaProject() ?? "";
    string? dg = Environment.GetEnvironmentVariable("FORGE_GAME");
    if (string.IsNullOrWhiteSpace(dg)) dg = PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(dg)) probe.Paths.GameDataDir = Path.Combine(dg, "data_sts2_windows_x86_64");
    probe.Paths.OutputDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbgProbeOut)) ?? ".", "gen_dbg");
    probe.DebugLog = true;   // ← 这个探针存在的全部意义

    var dbgCard = new CardSpec { Name = "自检调试日志卡", ClassName = "UiCheckDbgLogCard", CardType = "Skill", Rarity = "Common", Cost = 1, IsForged = true, InCardPool = true };
    dbgCard.ForgeGraph = new ForgeGraphSpec();
    var dbgTrig = new ForgeNodeSpec { Kind = "Trigger" };
    dbgTrig.SetField("Timing", ForgeTimings.CardPlayed);
    dbgCard.ForgeGraph.Nodes.Add(dbgTrig);
    string prevId = dbgTrig.Id;
    // 每条效果各接一个节点、串成一条链（逐个动手的那几档都会走 foreach 循环体 + 日志行）
    foreach (var (fxKind, fxAmount, fxPower) in new (string, string, string)[]
    {
        ("Damage", "6", ""),                        // 逐个敌人：foreach { 日志行 + await … }
        ("Block", "5", ""),                         // 同上
        ("HpLoss", "3", ""),                        // 同上
        ("ApplyPower", "2", "VulnerablePower"),     // PowerBefore / PowerAfter 两行日志
        ("Draw", "1", ""),                          // 单体（不在循环里）
    })
    {
        var fxNode = new ForgeNodeSpec { Kind = "Effect" };
        fxNode.SetField("Effect", fxKind);
        fxNode.SetField("Amount", fxAmount);
        if (fxPower.Length > 0) fxNode.SetField("PowerId", fxPower);
        dbgCard.ForgeGraph.Nodes.Add(fxNode);
        dbgCard.ForgeGraph.Links.Add(new ForgeLinkSpec { From = prevId, To = fxNode.Id, FromPort = 0, ToPort = 0 });
        prevId = fxNode.Id;
    }
    probe.Cards.Add(dbgCard);
    ProfileFactory.Save(probe, dbgProbeOut);
    Console.WriteLine("调试日志探针已写入：" + dbgProbeOut);
    string dbgSrc = CSharpCodeGen.CardSource(probe, dbgCard, probe.Cards.Count - 1);
    int bad = 0;
    foreach (string line in dbgSrc.Replace("\r\n", "\n").Split('\n'))
    {
        if (!line.Contains("CET:DebugLog", StringComparison.Ordinal)) continue;
        // 每一行日志必须**自己占一行结尾**（行尾不许再跟别的语句）：跟着别的东西就会被注释吃掉
        if (!line.TrimEnd().EndsWith("CET:DebugLog", StringComparison.Ordinal))
        {
            bad++;
            Console.WriteLine("    [X] 这行日志后面还跟着别的东西：" + line.Trim());
        }
    }
    Console.WriteLine(bad == 0 ? "    [OK] 每条日志都自成一个行尾注释（后面没有被吃掉的语句）" : $"    [X] 有 {bad} 行日志会吃掉后面的语句");
    return 0;
}

// --descprobe <json>：【开发用】造一份「描述栏里**打了回车换行**」的存档（V0.2.168）：
// 自定义状态的描述 / 卡牌的自定义描述 / 关键词说明都用 CRLF 多行文本。
// 用户实测报的：「自定义状态的描述栏中 如果用回车换行会有报错」——
// 真因是生成端把多行描述塞进**单行 C# 注释**（`/// <summary>…</summary>`）时只把 `\n` 换成空格、
// **留下了 `\r`**（WPF 的 TextBox 打回车是 CRLF）→ C# 把 `\r` 也当换行 ⇒ 注释被截断、后半截变成代码。
// 拿它跑 `forge --profile <它> --out <dir> --build` 验「多行描述也编译得过」。
if (descProbeOut is not null)
{
    CharacterProfile probe = ProfileFactory.Sample();
    if (string.IsNullOrWhiteSpace(probe.Paths.VanillaProject))
        probe.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoDetect.FindVanillaProject() ?? "";
    string? dg2 = Environment.GetEnvironmentVariable("FORGE_GAME");
    if (string.IsNullOrWhiteSpace(dg2)) dg2 = PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(dg2)) probe.Paths.GameDataDir = Path.Combine(dg2, "data_sts2_windows_x86_64");
    probe.Paths.OutputDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(descProbeOut)) ?? ".", "gen_desc");

    // 自定义状态：描述两行（CRLF）
    var cp = new CustomPowerSpec
    {
        Name = "自检多行描述", ClassName = "UiCheckMultiLine", Enabled = true,
        Description = "第一行描述\r\n第二行描述（带 [gold]富文本[/gold]）",
    };
    probe.CustomPowers.Add(cp);
    // 卡牌：自定义描述两行（CRLF）+ 卡面换行
    var card = new CardSpec { Name = "自检多行卡", ClassName = "UiCheckMultiLineCard", CardType = "Skill", Rarity = "Common", Cost = 1, InCardPool = true };
    card.CustomDescription = "第一行卡描述\r\n第二行卡描述";
    card.UpgradedCustomDescription = "升级后第一行\r\n升级后第二行";
    card.Effects.Add(new EffectSpec { Kind = "Block", Amount = 5m, TargetSide = "Self" });
    probe.Cards.Add(card);
    ProfileFactory.Save(probe, descProbeOut);
    Console.WriteLine("多行描述探针已写入：" + descProbeOut);
    string cpSrc = CustomPowerGen.Source(probe, cp, probe.CustomPowers.Count - 1);
    // ⚠ 只数「**后面不是 \n** 的 \r」（裸 CR）—— 生成出来的文件自己的行尾就是 CRLF，那不算问题
    int bareCr = System.Text.RegularExpressions.Regex.Matches(cpSrc, "\r(?!\n)").Count;
    Console.WriteLine(bareCr == 0
        ? "    [OK] 状态源码里没有裸 \\r（多行描述已被压成一行）"
        : $"    [X] 状态源码里有 {bareCr} 个裸 \\r —— 会把 C# 注释截断、编译报错");
    foreach (string line in cpSrc.Replace("\r\n", "\n").Split('\n'))
    {
        if (line.Contains('\r')) Console.WriteLine("        · " + line.Replace("\r", "⟪CR⟫"));
    }
    return 0;
}

// --gainprobe：验「GainsBlock（本体透镜附魔 Nimble 的筛法）什么时候自动声明」（V0.2.168）。
if (args.Any(a => a.Equals("--gainprobe", StringComparison.OrdinalIgnoreCase)))
{
    CharacterProfile gp = ProfileFactory.Sample();
    var cases = new List<(string Name, CardSpec Card)>();
    var ok = new CardSpec { Name = "格挡牌", ClassName = "GpOk", CardType = "Skill", Rarity = "Common", Cost = 1 };
    ok.Effects.Add(new EffectSpec { Kind = "Block", Amount = 5m, TargetSide = "Self" });
    var next = new CardSpec { Name = "下回合格挡", ClassName = "GpNext", CardType = "Skill", Rarity = "Common", Cost = 1 };
    next.Effects.Add(new EffectSpec { Kind = "Block", Amount = 5m, TargetSide = "Self", NextTurn = true });
    var neg = new CardSpec { Name = "失去格挡", ClassName = "GpNeg", CardType = "Skill", Rarity = "Common", Cost = 1 };
    neg.Effects.Add(new EffectSpec { Kind = "Block", Amount = -5m, TargetSide = "Self" });
    var hook = new CardSpec { Name = "被消耗才格挡", ClassName = "GpHook", CardType = "Skill", Rarity = "Common", Cost = 1 };
    hook.Effects.Add(new EffectSpec
    {
        Kind = "Block", Amount = 5m, TargetSide = "Self",
        Condition = new ConditionSpec { Kind = "None", Timing = "Exhausted" },
    });
    cases.Add(("获得格挡（正数）", ok));
    cases.Add(("下回合生效", next));
    cases.Add(("负数 = 失去格挡", neg));
    cases.Add(("只挂在「被消耗时」", hook));
    foreach (var (name, card) in cases)
    {
        gp.Cards.Add(card);
        string src = CSharpCodeGen.CardSource(gp, card, gp.Cards.Count - 1);
        bool has = src.Contains("public override bool GainsBlock => true;", StringComparison.Ordinal);
        Console.WriteLine($"    {(has ? "GainsBlock=true " : "（不声明）      ")} {name}  ｜ Effects={card.Effects.Count} NextTurn={card.Effects[0].NextTurn} Amount={card.Effects[0].Amount} Timing={card.Effects[0].Condition?.Timing} Tags={string.Join("+", card.TagList)}");
    }
    // 第二阶段：和自检里一样的写法（先把 4 张全加进去，再按 IndexOf 取下标生成）
    Console.WriteLine("    ---- 第二阶段（4 张一次性加进去，按 IndexOf 生成）----");
    var gp2 = ProfileFactory.Sample();
    var all = cases.Select(x => x.Card).ToList();
    foreach (var card in all) gp2.Cards.Add(card);
    foreach (var (name, card) in cases)
    {
        int idx = gp2.Cards.IndexOf(card);
        string src = CSharpCodeGen.CardSource(gp2, card, idx);
        bool has = src.Contains("public override bool GainsBlock => true;", StringComparison.Ordinal);
        int bare = src.IndexOf("GainsBlock", StringComparison.Ordinal);
        string ctx = bare < 0 ? "(整份源码里没有 GainsBlock)" : "…" + src.Substring(Math.Max(0, bare - 60), Math.Min(120, src.Length - Math.Max(0, bare - 60))).Replace("\r\n", "⏎") + "…";
        Console.WriteLine($"    {(has ? "GainsBlock=true " : "（不声明）      ")} {name}  ｜ idx={idx} 裸词命中={(bare >= 0)} {ctx}");
    }
    return 0;
}

// --infupgradeprobe：验「可被无限升级」的牌每升一级都加数值（V0.2.168，用户实测报的
// 「无限升级怎么升级后不加数值了」）：效果表达式要写成 `(公式 + 增量 * base.CurrentUpgradeLevel)`，
// 卡面用的动态变量也一样（`MaxUpgradeLevel > 1` 时按层数叠加）。
if (args.Any(a => a.Equals("--infupgradeprobe", StringComparison.OrdinalIgnoreCase)))
{
    CharacterProfile ip = ProfileFactory.Sample();
    var infCard = new CardSpec
    {
        Name = "无限升级探针", ClassName = "InfUpProbe", CardType = "Attack", Rarity = "Common", Cost = 1,
        InfinitelyUpgradable = true,
    };
    infCard.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 6m, TargetSide = "Enemy", UpgradeAmount = 3m, AmountFormula = "6" });
    infCard.Effects.Add(new EffectSpec { Kind = "Block", Amount = 5m, TargetSide = "Self", UpgradeAmount = 2m, AmountFormula = "5" });
    ip.Cards.Add(infCard);
    string src = CSharpCodeGen.CardSource(ip, infCard, ip.Cards.Count - 1);
    foreach (string line in src.Replace("\r\n", "\n").Split('\n'))
    {
        string t = line.Trim();
        if (t.Contains("CurrentUpgradeLevel", StringComparison.Ordinal) || t.Contains("MaxUpgradeLevel", StringComparison.Ordinal)
            || t.Contains("IsUpgraded ?", StringComparison.Ordinal) || t.Contains("UpgradeValueBy", StringComparison.Ordinal))
            Console.WriteLine("    " + t);
    }
    Console.WriteLine("    ---- 对照：普通（只能升 1 级）的同一张牌 ----");
    var plain = new CardSpec { Name = "普通探针", ClassName = "InfUpProbePlain", CardType = "Attack", Rarity = "Common", Cost = 1 };
    plain.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 6m, TargetSide = "Enemy", UpgradeAmount = 3m, AmountFormula = "6" });
    ip.Cards.Add(plain);
    foreach (string line in CSharpCodeGen.CardSource(ip, plain, ip.Cards.Count - 1).Replace("\r\n", "\n").Split('\n'))
    {
        string t = line.Trim();
        if (t.Contains("IsUpgraded ?", StringComparison.Ordinal) || t.Contains("MaxUpgradeLevel", StringComparison.Ordinal))
            Console.WriteLine("    " + t);
    }
    // 卡面变量（FormulaVars.cs）里的预览算法
    string vars = CSharpCodeGen.FormulaVarsSource(ip);
    foreach (string line in vars.Replace("\r\n", "\n").Split('\n'))
    {
        string t = line.Trim();
        if (t.StartsWith("BaseValue = _formula", StringComparison.Ordinal)) Console.WriteLine("    [FormulaVars] " + t);
    }
    // 顺手写一份存档：`forge --profile <它> --out <dir> --build` 验「无限升级那张卡编译得过」
    string infJson = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Environment.ProcessPath ?? ".")) ?? ".", "infup.json");
    if (string.IsNullOrWhiteSpace(ip.Paths.VanillaProject))
        ip.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoDetect.FindVanillaProject() ?? "";
    string? ig = Environment.GetEnvironmentVariable("FORGE_GAME") ?? PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(ig)) ip.Paths.GameDataDir = Path.Combine(ig, "data_sts2_windows_x86_64");
    ip.Paths.OutputDir = Path.Combine(Path.GetDirectoryName(infJson) ?? ".", "gen_infup");
    ProfileFactory.Save(ip, infJson);
    Console.WriteLine("    （探针存档：" + infJson + "）");
    return 0;
}

// --starprobe：看「星费 + 回复额外资源量」生成出来是什么（V0.2.168，用户报的
// 「如果这张牌要消耗额外费用的话这一次就会不生效回复额外费用的效果」）。
if (args.Any(a => a.Equals("--starprobe", StringComparison.OrdinalIgnoreCase)))
{
    CharacterProfile sp = ProfileFactory.Sample();
    var cases = new List<(string Name, CardSpec Card)>();
    // ① 固定星费 3（负数效果）+ 回复 2
    var a1 = new CardSpec { Name = "星费3回2", ClassName = "StarP1", CardType = "Skill", Rarity = "Common", Cost = 1, InCardPool = true };
    a1.Effects.Add(new EffectSpec { Kind = "ExtraResource", Amount = -3m, TargetSide = "Self" });
    a1.Effects.Add(new EffectSpec { Kind = "ExtraResource", Amount = 2m, TargetSide = "Self" });
    cases.Add(("固定星费 3 + 回复 2", a1));
    // ② 星费 Y（花光）+ 回复 3
    var a2 = new CardSpec { Name = "星费Y回3", ClassName = "StarP2", CardType = "Skill", Rarity = "Common", Cost = 1, InCardPool = true };
    a2.StarCostFormula = "Y";
    a2.Effects.Add(new EffectSpec { Kind = "ExtraResource", Amount = 3m, TargetSide = "Self" });
    cases.Add(("星费 Y + 回复 3", a2));
    // ③ 星费 Y（花光）+ 回复「数值 = X」（本体那种「把花掉的还回来」）
    var a3 = new CardSpec { Name = "星费Y回X", ClassName = "StarP3", CardType = "Skill", Rarity = "Common", Cost = 1, InCardPool = true };
    a3.StarCostFormula = "Y";
    a3.Effects.Add(new EffectSpec { Kind = "ExtraResource", Amount = 1m, AmountIsX = true, TargetSide = "Self" });
    cases.Add(("星费 Y + 回复「数值 = X」", a3));
    // ④ 只有回复（对照）
    var a4 = new CardSpec { Name = "只回复2", ClassName = "StarP4", CardType = "Skill", Rarity = "Common", Cost = 1, InCardPool = true };
    a4.Effects.Add(new EffectSpec { Kind = "ExtraResource", Amount = 2m, TargetSide = "Self" });
    cases.Add(("（对照）只回复 2", a4));
    foreach (var (name, card) in cases)
    {
        sp.Cards.Add(card);
        string src = CSharpCodeGen.CardSource(sp, card, sp.Cards.Count - 1);
        Console.WriteLine("=== " + name);
        foreach (string line in src.Replace("\r\n", "\n").Split('\n'))
        {
            string t = line.Trim();
            if (t.Contains("Star", StringComparison.Ordinal) || t.Contains("OnUpgrade", StringComparison.Ordinal)
                || t.Contains("CanonicalStarCost", StringComparison.Ordinal) || t.Contains("int x =", StringComparison.Ordinal))
                Console.WriteLine("    " + t);
        }
    }
    return 0;
}

// --petprobe <json>：验「自定义状态里放『召唤伙伴』」（V0.2.168，用户实测报的：
// 状态页「生成卡牌时 → 召唤伙伴」被生成拦住了）。生成 → 真编译一条龙。
if (petProbeOut is not null)
{
    CharacterProfile probe = ProfileFactory.Sample();
    if (string.IsNullOrWhiteSpace(probe.Paths.VanillaProject))
        probe.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoFind();
    string? pg = Environment.GetEnvironmentVariable("FORGE_GAME") ?? PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(pg)) probe.Paths.GameDataDir = Path.Combine(pg, "data_sts2_windows_x86_64");
    probe.Paths.OutputDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(petProbeOut)) ?? ".", "gen_pet");
    probe.Summons.Clear();
    probe.Summons.Add(new SummonSpec { Enabled = true, Name = "探针伙伴", ClassName = "UiCheckPetProbe", Hp = 10 });

    // V0.2.168 顺带：节点图里「变化卡牌」的「从哪里选牌」（用户截图报的「不生效」）
    var pileCard = new CardSpec { Name = "选牌堆探针", ClassName = "UiCheckPileCard", CardType = "Skill", Rarity = "Common", Cost = 1, IsForged = true, InCardPool = true };
    pileCard.ForgeGraph = new ForgeGraphSpec();
    var pileTrig = new ForgeNodeSpec { Kind = "Trigger" };
    pileTrig.SetField("Timing", ForgeTimings.CardPlayed);
    var pileEff = new ForgeNodeSpec { Kind = "Effect" };
    pileEff.SetField("Effect", "TransformCard");
    pileEff.SetField("Amount", "1");
    var pileSpec = ForgeEffectBridge.Ensure(pileEff, authored: true)!;
    pileSpec.SelectPile = "Draw";      // 从抽牌堆挑
    pileSpec.SpawnPick = "Fixed";
    ForgeEffectBridge.Bind(pileEff);
    var pileEff2 = new ForgeNodeSpec { Kind = "Effect" };
    pileEff2.SetField("Effect", "CopyCard");
    pileEff2.SetField("Amount", "1");
    var pileSpec2 = ForgeEffectBridge.Ensure(pileEff2, authored: true)!;
    pileSpec2.SelectPile = "Discard";  // 从弃牌堆随机
    pileSpec2.SpawnPick = "Fixed";
    ForgeEffectBridge.Bind(pileEff2);
    pileCard.ForgeGraph.Nodes.AddRange(new[] { pileTrig, pileEff, pileEff2 });
    // V0.2.168：新接的那几档（用户截图报的「删除卡牌（全局：从牌组删牌）」等）
    foreach (var (gk, gAmount) in new[] { ("RemoveCardGlobal", "1"), ("AddCardGlobal", "1"), ("UpgradeCardGlobal", "1"), ("Outbreak", "3"), ("PlaySfx", "100"), ("BoostCard", "2") })
    {
        var ge = new ForgeNodeSpec { Kind = "Effect" };
        ge.SetField("Effect", gk);
        ge.SetField("Amount", gAmount);
        if (gk == "PlaySfx") ge.SetField("Sfx", "event:/sfx/block_gain");
        if (gk == "BoostCard") { var bs = ForgeEffectBridge.Ensure(ge, authored: true)!; bs.SpawnCardId = "SevenSlash"; bs.BoostStat = "Damage"; ForgeEffectBridge.Bind(ge); }
        pileCard.ForgeGraph.Nodes.Add(ge);
        pileCard.ForgeGraph.Links.Add(new ForgeLinkSpec { From = pileEff2.Id, To = ge.Id, FromPort = 0, ToPort = 0 });
    }
    pileCard.ForgeGraph.Links.Add(new ForgeLinkSpec { From = pileTrig.Id, To = pileEff.Id, FromPort = 0, ToPort = 0 });
    pileCard.ForgeGraph.Links.Add(new ForgeLinkSpec { From = pileEff.Id, To = pileEff2.Id, FromPort = 0, ToPort = 0 });
    probe.Cards.Add(pileCard);

    var cp = new CustomPowerSpec { Name = "探针召唤状态", ClassName = "UiCheckPetPower", Enabled = true };
    var trig = new PowerTriggerSpec { Kind = "CardGenerated" };
    // ① 按填的血量召唤 ② 数值 0 = 用「召唤物」页配的血量
    trig.Effects.Add(new EffectSpec { Kind = "SummonPet", Amount = 12m, PetSummon = "UiCheckPetProbe" });
    trig.Effects.Add(new EffectSpec { Kind = "SummonPet", Amount = 0m, PetSummon = "UiCheckPetProbe" });
    cp.Triggers.Add(trig);
    probe.CustomPowers.Add(cp);
    ProfileFactory.Save(probe, petProbeOut);
    Console.WriteLine("召唤探针已写入：" + petProbeOut);
    var issues = ProfileValidator.Validate(probe).Where(x => x.IsError).ToList();
    Console.WriteLine(issues.Count == 0 ? "    [OK] 校验器不再拦「状态里放召唤伙伴」" : "    [X] 仍有错误：" + string.Join(" / ", issues.Select(x => x.Message)));
    string src = CustomPowerGen.Source(probe, cp, probe.CustomPowers.Count - 1);
    foreach (string line in src.Replace("\r\n", "\n").Split('\n'))
    {
        string t = line.Trim();
        if (t.Contains("Cmd.Summon", StringComparison.Ordinal) || t.Contains("PetHp", StringComparison.Ordinal))
            Console.WriteLine("    " + t);
    }
    Console.WriteLine("    状态描述：" + PowerTriggers.AutoDescription(cp).Replace("\n", " ⏎ "));
    return 0;

    static string PathAutoFind() => PathAutoDetect.FindVanillaProject() ?? "";
}

// --energyprobe <json>：V0.2.172（用户要求）—— ①角色页底部也能填「多人催促文本」（和美术页同一份数据）
// ②新增「角色最大能量」（本体 CharacterModel.MaxEnergy 默认 3，生成端一个 override 就够，默认 3 不写）。
// 写出来之后再用 `--profile <这个 json> --out <目录> --build` 真编译一次。
if (energyProbeOut is not null)
{
    CharacterProfile probe = ProfileFactory.Sample();
    probe.DisplayName = "能量探针";
    probe.MaxEnergy = 5;
    probe.Art.TreasureFightText = "别抢！这是我的！！！";
    ProfileFactory.Save(probe, energyProbeOut);
    Console.WriteLine("能量探针已写入：" + energyProbeOut);
    var all = ProfileValidator.Validate(probe).ToList();
    // 只关心和这次改动有关的错误：Sample 存档缺「解包工程目录 / sts2.dll」是本机环境问题（探针不生成工程也会报）
    var errs = all.Where(x => x.IsError
        && !x.Message.Contains("解包工程目录") && !x.Message.Contains("sts2.dll")).ToList();
    Console.WriteLine(errs.Count == 0
        ? "    [OK] 校验器：最大能量 5 / 催促文本 都不拦生成"
        : "    [X] 仍有错误：" + string.Join(" / ", errs.Select(x => x.Message)));
    Console.WriteLine("    [提示] " + string.Join(" / ", all.Where(x => x.Level == "提示" && x.Message.Contains("最大能量")).Select(x => x.Message)));
    string src = CSharpCodeGen.CharacterSource(probe);
    foreach (string line in src.Replace("\r\n", "\n").Split('\n'))
        if (line.Contains("MaxEnergy", StringComparison.Ordinal) || line.Contains("StartingHp", StringComparison.Ordinal))
            Console.WriteLine("    生成：" + line.Trim());
    Console.WriteLine("    催促文本补丁：" + (TreasureGestureGen.NeedsFightText(probe) ? "会生成 TreasureFightTextPatch" : "[X] 没生成！"));
    Console.WriteLine("    默认 3 的角色类里有没有 MaxEnergy："
        + (CSharpCodeGen.CharacterSource(ProfileFactory.Sample()).Contains("MaxEnergy") ? "[X] 有（违反红线）" : "没有（对）"));
    return 0;
}

// --newprobe <json>：V0.2.170 的三条（节点图里「取卡方式」四档 / 「下回合生效」铺到全部做得了的种类 /
// 要「打出这张牌」那一刻上下文的几种 + 斩杀条件）。生成端写出来之后，再用
// `--profile <这个 json> --out <目录> --build` 真编译一次。
if (newProbeOut is not null)
{
    CharacterProfile probe = ProfileFactory.Sample();
    if (string.IsNullOrWhiteSpace(probe.Paths.VanillaProject))
        probe.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? (PathAutoDetect.FindVanillaProject() ?? "");
    string? ng = Environment.GetEnvironmentVariable("FORGE_GAME") ?? PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(ng)) probe.Paths.GameDataDir = Path.Combine(ng, "data_sts2_windows_x86_64");
    probe.Paths.OutputDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(newProbeOut)) ?? ".", "gen_new");

    // ---------- ①「取卡方式」四档 + 「下回合生效」铺全 + 重放 / 回合结束自动打出 / 放回手牌 ----------
    var c1 = new CardSpec { Name = "取卡方式探针", ClassName = "UiCheckPickMode", CardType = "Skill", Rarity = "Common", Cost = 1, IsForged = true, InCardPool = true };
    c1.ForgeGraph = new ForgeGraphSpec();
    var t1 = new ForgeNodeSpec { Kind = "Trigger" };
    t1.SetField("Timing", ForgeTimings.CardPlayed);
    c1.ForgeGraph.Nodes.Add(t1);
    ForgeNodeSpec chain = t1;
    void AddEffect(string kind, string amount, Action<EffectSpec>? cfg)
    {
        var n = new ForgeNodeSpec { Kind = "Effect" };
        n.SetField("Effect", kind);
        n.SetField("Amount", amount);
        var s = ForgeEffectBridge.Ensure(n, authored: true)!;
        cfg?.Invoke(s);
        ForgeEffectBridge.Bind(n);
        c1.ForgeGraph.Nodes.Add(n);
        c1.ForgeGraph.Links.Add(new ForgeLinkSpec { From = chain.Id, To = n.Id, FromPort = 0, ToPort = 0 });
        chain = n;
    }
    AddEffect("GenerateCard", "1", s => { s.SpawnPick = "Random"; s.SpawnFilter = "Attack"; s.SpawnChoice = 3m; });                      // 多选1
    AddEffect("TransformCard", "1", s => { s.SpawnPick = "Random"; s.SpawnFilter = "Skill"; s.SpawnChosen = true; s.SelectPile = "Hand"; });   // 自己挑一张
    AddEffect("AddCardGlobal", "1", s => { s.SpawnPick = "Random"; s.SpawnFilter = "Attack"; s.SpawnChosen = true; });                  // 全局加牌：自己挑
    AddEffect("TransformCardGlobal", "1", s => { s.SpawnPick = "Random"; s.SpawnFilter = "Attack"; s.SpawnChoice = 3m; });              // 全局变化：多选1
    AddEffect("Damage", "5", s => s.NextTurn = true);     // 下回合生效（以前节点图不支持「造成伤害」）
    AddEffect("Energy", "2", s => s.NextTurn = true);     // 下回合生效（本体现成的 EnergyNextTurnPower）
    AddEffect("ReplayCard", "1", s => s.SelectPile = "Hand");   // 重放卡牌
    AddEffect("TurnEndPlay", "1", null);                        // 回合结束时自动打出
    probe.Cards.Add(c1);

    // ---------- ② 斩杀条件（【条件】= 斩杀 挂在一次「造成伤害」之后）----------
    var c2 = new CardSpec { Name = "斩杀探针", ClassName = "UiCheckFatalProbe", CardType = "Attack", Rarity = "Common", Cost = 1, IsForged = true, InCardPool = true };
    c2.ForgeGraph = new ForgeGraphSpec();
    var t2 = new ForgeNodeSpec { Kind = "Trigger" };
    t2.SetField("Timing", ForgeTimings.CardPlayed);
    var hit = new ForgeNodeSpec { Kind = "Effect" };
    hit.SetField("Effect", "Damage");
    hit.SetField("Amount", "6");
    var cond = new ForgeNodeSpec { Kind = "Condition" };
    cond.SetField("Cond", "Fatal");
    var ifNode = new ForgeNodeSpec { Kind = "If" };
    var fatal = new ForgeNodeSpec { Kind = "Effect" };
    fatal.SetField("Effect", "Damage");
    fatal.SetField("Amount", "9");
    c2.ForgeGraph.Nodes.AddRange(new[] { t2, hit, cond, ifNode, fatal });
    // 新端口：if 的入口 0 = 接入、入口 1 = 条件；出口 0 = 是
    c2.ForgeGraph.Links.Add(new ForgeLinkSpec { From = t2.Id, To = hit.Id, FromPort = 0, ToPort = 0 });
    c2.ForgeGraph.Links.Add(new ForgeLinkSpec { From = hit.Id, To = ifNode.Id, FromPort = 0, ToPort = 0 });
    c2.ForgeGraph.Links.Add(new ForgeLinkSpec { From = cond.Id, To = ifNode.Id, FromPort = 0, ToPort = 1 });
    c2.ForgeGraph.Links.Add(new ForgeLinkSpec { From = ifNode.Id, To = fatal.Id, FromPort = 0, ToPort = 0 });
    probe.Cards.Add(c2);

    // ---------- ③「将此牌放回手牌」挂在「这张牌被丢弃时」上（节点图里它是内联的：牌那时已经不在手牌里）----------
    var c3 = new CardSpec { Name = "放回手牌探针", ClassName = "UiCheckReturnProbe", CardType = "Skill", Rarity = "Common", Cost = 1, IsForged = true, InCardPool = true };
    c3.ForgeGraph = new ForgeGraphSpec();
    var t3 = new ForgeNodeSpec { Kind = "Trigger" };
    t3.SetField("Timing", ForgeTimings.CardDiscarded);
    var back = new ForgeNodeSpec { Kind = "Effect" };
    back.SetField("Effect", "ReturnSelfToHand");
    back.SetField("Amount", "1");
    c3.ForgeGraph.Nodes.AddRange(new[] { t3, back });
    c3.ForgeGraph.Links.Add(new ForgeLinkSpec { From = t3.Id, To = back.Id, FromPort = 0, ToPort = 0 });
    probe.Cards.Add(c3);

    ProfileFactory.Save(probe, newProbeOut);
    Console.WriteLine("V0.2.170 探针已写入：" + newProbeOut);
    var nIssues = ProfileValidator.Validate(probe).Where(x => x.IsError).ToList();
    Console.WriteLine(nIssues.Count == 0 ? "    [OK] 校验器 0 错误" : "    [X] 仍有错误：" + string.Join(" / ", nIssues.Select(x => x.Message)));
    foreach (string cls in new[] { "UiCheckPickMode", "UiCheckFatalProbe", "UiCheckReturnProbe" })
    {
        var card = probe.Cards.First(x => x.ClassName == cls);
        string src = CSharpCodeGen.CardSource(probe, card, 0);
        Console.WriteLine($"    ---- {cls} ----");
        foreach (string line in src.Replace("\r\n", "\n").Split('\n'))
        {
            string t = line.Trim();
            if (t.Contains("GetDistinctForCombat", StringComparison.Ordinal) || t.Contains("FromChooseACardScreen", StringComparison.Ordinal)
                || t.Contains("FromSimpleGrid", StringComparison.Ordinal) || t.Contains("Delayed", StringComparison.Ordinal)
                || t.Contains("__lastAttack", StringComparison.Ordinal) || t.Contains("__fatalOk", StringComparison.Ordinal)
                || t.Contains("WasTargetKilled", StringComparison.Ordinal) || t.Contains("BaseReplayCount", StringComparison.Ordinal)
                || t.Contains("TurnEndPlayPower", StringComparison.Ordinal) || t.Contains("PileType.Hand);", StringComparison.Ordinal))
                Console.WriteLine("      " + t);
        }
    }
    return 0;
}

if (detect)
{
    string? game = PathAutoDetect.FindGameDir();
    if (game is null) { Console.WriteLine("[X] 没探测到游戏安装目录。请在界面「路径」页手工填游戏目录（里面应含 data_sts2_windows_x86_64 和 mods）。"); return 1; }
    Console.WriteLine("游戏目录：" + game);
    Console.WriteLine("data 目录：" + Path.Combine(game, "data_sts2_windows_x86_64"));
    Console.WriteLine("mods 目录：" + Path.Combine(game, "mods"));
    Console.WriteLine(File.Exists(Path.Combine(game, "data_sts2_windows_x86_64", "sts2.dll")) ? "[OK] 找到 sts2.dll" : "[X] 该目录下没有 data_sts2_windows_x86_64\\sts2.dll");
    return 0;
}

if (check)
{
    // --check 不带配置时也自动探一次游戏目录，这样换台电脑直接就能看到效果库
    string? autoGame = PathAutoDetect.FindGameDir();
    if (autoGame is not null)
    {
        Console.WriteLine("自动探测到游戏目录：" + autoGame);
        EffectCatalog.Initialize(null, Path.Combine(autoGame, "data_sts2_windows_x86_64"));
    }
    Console.WriteLine($"效果库：共 {EffectCatalog.Powers.Count} 个本体 Power（增益 {EffectCatalog.Buffs.Count} / 减益 {EffectCatalog.Debuffs.Count}）" + (EffectCatalog.Powers.Count > 0 ? "" : "  ← 需要指定本机的解包工程目录或游戏 data 目录"));
    Console.WriteLine($"效果种类：{EffectCatalog.EffectKinds.Count} 种；遗物触发时机：{EffectCatalog.RelicTriggers.Count} 个");
    Console.WriteLine(EffectCatalog.Powers.Count > 0 ? "[OK] 效果库加载正常" : "[X] 效果库为空（内嵌数据缺失）");
    return EffectCatalog.Powers.Count > 0 ? 0 : 1;
}

if (demo)
{
    string target = profilePath ?? Path.Combine(Directory.GetCurrentDirectory(), "profile.json");
    ProfileFactory.Save(ProfileFactory.Sample(), target);
    Console.WriteLine("已写出示例配置：" + target);
    return 0;
}

// --buildcheck：自检「没给 --install 就绝不安装」（红线⑧，V0.2.137）
//
// 为什么要它：这条红线前两次都是**靠人眼看**「游戏 mods 目录里有没有多出文件」才发现的，
// 而「探针只跑 --build」这个习惯本身挡不住代码里的兜底 —— 所以把行为钉成一条命令，谁都能跑。
//
//   · installDir 参数：**只由 `--install` 传**，**绝不退回 profile.Paths.InstallDir**（那字段给 GUI 用）；
//   · 不给 `--install` 时 `ModBuilder.Build` 收到的永远是 null → 它内部
//     `if (!string.IsNullOrWhiteSpace(installDir))` 那一整块（安装）根本不进；
//   · 给 `--install` 时按老规矩装。（Godot 在不在都行：装的那一步在 Godot 之前，不会因为导出失败被跳过。）
if (buildCheck)
{
    Console.WriteLine("=== --buildcheck：不给 --install 时绝不安装（红线⑧）===");
    CharacterProfile bcProfile = ProfileFactory.Sample();
    string bcRoot = Path.Combine(Path.GetTempPath(), "forge_buildcheck_" + Guid.NewGuid().ToString("N").Substring(0, 8));
    string bcTrap = Path.Combine(bcRoot, "存档里写着的安装目录");
    string bcOut = Path.Combine(bcRoot, "out");
    Directory.CreateDirectory(bcTrap);
    bcProfile.Paths.OutputDir = bcOut;
    // 和 --samplegen 一样先把「解包工程 / 游戏 data」自动探一次（示例配置里这两栏是空的）
    if (string.IsNullOrWhiteSpace(bcProfile.Paths.VanillaProject))
        bcProfile.Paths.VanillaProject = Environment.GetEnvironmentVariable("FORGE_VANILLA") ?? PathAutoDetect.FindVanillaProject() ?? "";
    string? bcGame = Environment.GetEnvironmentVariable("FORGE_GAME") ?? PathAutoDetect.FindGameDir();
    if (!string.IsNullOrWhiteSpace(bcGame) && string.IsNullOrWhiteSpace(bcProfile.Paths.GameDataDir))
        bcProfile.Paths.GameDataDir = Path.Combine(bcGame, "data_sts2_windows_x86_64");
    // 想连「② 给了 --install 就真装」那一档一起验，就用 --godot 指一下本机的 Godot
    //（不给就只验「① 不给就不装」—— 那是红线⑧ 真正关心的那一条）。
    if (!string.IsNullOrWhiteSpace(becCheckGodotExe)) bcProfile.Paths.GodotExe = becCheckGodotExe!;
    // 关键：存档里**故意**写着一个「游戏 mods 目录」（这里用临时目录模拟）——
    // 老代码会拿它去安装，新代码必须一个文件都不往里放。
    bcProfile.Paths.InstallDir = bcTrap;
    EffectCatalog.Initialize(bcProfile.Paths.VanillaProject, bcProfile.Paths.GameDataDir);

    int SnapshotCount() => Directory.Exists(bcTrap) ? Directory.GetFiles(bcTrap, "*", SearchOption.AllDirectories).Length : -1;
    int bcBefore = SnapshotCount();
    Console.WriteLine("存档里的 Paths.InstallDir（陷阱目录）：" + bcTrap);
    Console.WriteLine($"构建前那里有 {bcBefore} 个文件");

    GenerationResult bcGen = ModGenerator.Generate(bcProfile, m => { });
    if (!bcGen.Success)
    {
        Console.WriteLine("[X] 生成失败，无法继续：" + string.Join(" / ", bcGen.Issues.Where(x => x.IsError).Take(3)));
        try { Directory.Delete(bcRoot, true); } catch { }
        return 1;
    }
    // ① 不给 --install：模拟命令行那条 if（installDir 为 null → 传 null）
    string? bcNoInstall = null;
    BuildResult bcBuild = ModBuilder.Build(bcGen.ProjectRoot, Naming.From(bcProfile).ModId, bcProfile.Paths.GodotExe,
        bcNoInstall, null, bcProfile.Paths.DotnetExe);
    int bcAfterNoInstall = SnapshotCount();
    bool bcUntouched = bcAfterNoInstall == bcBefore && bcBuild.InstalledTo is null;
    Console.WriteLine($"① 不给 --install → InstalledTo={(bcBuild.InstalledTo ?? "(null)")}，陷阱目录现在有 {bcAfterNoInstall} 个文件");
    Console.WriteLine(bcUntouched
        ? "[PASS] 没给 --install：陷阱目录一个文件都没多（存档里的 InstallDir 被忽略）"
        : $"[FAIL] 没给 --install 却动了陷阱目录（{bcBefore} → {bcAfterNoInstall}）");

    // ② 给了 --install（指向另一个临时目录）：应该真的装进去
    //    ⚠ 本机这份示例配置里如果没有 Godot，就**不能**造一个假 exe 糊过去：ModBuilder 找到 Godot 才会走到
    //      安装那一步，假 exe 只会让导出失败、连安装都到不了 —— 那样断言就变成假的。这种情况下明确报
    //      「跳过」（退出码仍以 ① 为准），不假装通过。
    string bcRealInstall = Path.Combine(bcRoot, "显式给的安装目录");
    bool bcGodot = !string.IsNullOrWhiteSpace(bcProfile.Paths.GodotExe) && File.Exists(bcProfile.Paths.GodotExe);
    bool bcPositiveSkipped = !bcGodot;
    bool bcPositive = bcPositiveSkipped;
    if (bcPositiveSkipped)
    {
        Console.WriteLine("② [跳过] 本机没有 Godot 可执行文件（示例配置里的 Paths.GodotExe 不存在）→ 装不了，"
            + "「给了 --install 就真装」这一档没验；① 的结论不受影响。");
    }
    else
    {
        BuildResult bcBuild2 = ModBuilder.Build(bcGen.ProjectRoot, Naming.From(bcProfile).ModId, bcProfile.Paths.GodotExe,
            bcRealInstall, null, bcProfile.Paths.DotnetExe);
        int bcInstalled = Directory.Exists(bcRealInstall) ? Directory.GetFiles(bcRealInstall).Length : 0;
        bcPositive = bcBuild2.InstalledTo is not null && bcInstalled >= 3;
        Console.WriteLine(bcPositive
            ? $"[PASS] ② 给了 --install：真的装进 {bcRealInstall}（{bcInstalled} 个文件）"
            : $"[FAIL] ② 给了 --install 却没装成（InstalledTo={(bcBuild2.InstalledTo ?? "(null)")} / {bcInstalled} 个文件）");
        bcPositiveSkipped = false;
    }
    try { Directory.Delete(bcRoot, true); } catch { }
    Console.WriteLine(bcUntouched && bcPositive
        ? "[OK] --buildcheck 通过（① 没给就不装" + (bcPositiveSkipped ? "；② 本机没 Godot，跳过）" : "；② 给了就装）")
        : "[X] --buildcheck 失败");
    return bcUntouched && bcPositive ? 0 : 1;
}

if (profilePath is null) { PrintHelp(); return 1; }
if (!File.Exists(profilePath)) { Console.WriteLine("[X] 找不到配置：" + profilePath); return 1; }

CharacterProfile profile;
try { profile = ProfileFactory.Load(profilePath); }
catch (Exception e) { Console.WriteLine("[X] 配置解析失败：" + e.Message); return 1; }

if (outDir is not null) profile.Paths.OutputDir = outDir;

// 效果库（增益/减益列表）是运行时从本机的游戏文件读取的：优先解包工程，其次从 sts2.dll 反射兜底
EffectCatalog.Initialize(profile.Paths.VanillaProject, profile.Paths.GameDataDir);
Console.WriteLine("效果库：" + EffectCatalog.CatalogStatus);
// ⚠ 只把**显式给的** `--install` 写回配置（它会进生成出来的 build.ps1 / 使用说明）。
// 不给 `--install` 时**一个字都不动存档里的 InstallDir** —— 那个字段是给图形界面用的。
if (installDir is not null) profile.Paths.InstallDir = installDir;
if (string.IsNullOrWhiteSpace(profile.Paths.OutputDir))
    profile.Paths.OutputDir = Path.Combine(AppContext.BaseDirectory, "自定义角色");

Console.WriteLine($"=== 生成「{profile.DisplayName}」（{profile.ModId}）===");
var result = ModGenerator.Generate(profile, Console.WriteLine);
if (!result.Success) { Console.WriteLine("[X] 生成失败。"); return 1; }
if (!doBuild) { Console.WriteLine("工程已生成：" + result.ProjectRoot); return 0; }

Console.WriteLine();
Console.WriteLine("=== 构建 ===");
// ⚠⚠ V0.2.137（红线⑧）：这里**只传显式给的 `--install`**，绝不退回 `profile.Paths.InstallDir`
//    —— 那个字段是**给图形界面用的**（GUI 自己会把「要不要安装」和目录一起算好再传给 ModBuilder）。
//    以前写的是 `installDir ?? profile.Paths.InstallDir`：探针只想编译一下，却因为存档里写着游戏 mods
//    目录而被真的安装（V0.2.135 / V0.2.137 各一次）。
if (installDir is null)
    Console.WriteLine("（没给 --install：只生成 + 编译 + 导出 PCK，**不会安装**到任何地方。）");
var build = ModBuilder.Build(result.ProjectRoot, Naming.From(profile).ModId, profile.Paths.GodotExe,
    installDir, Console.WriteLine, profile.Paths.DotnetExe, profile.Paths.GameDataDir);

foreach (var step in build.Steps)
{
    Console.WriteLine($"--- {step.Name} {(step.Success ? "OK" : "失败")} ---");
    Console.WriteLine(step.Output.Trim());
}

Console.WriteLine();
if (build.Success)
{
    Console.WriteLine(build.InstalledTo is null
        ? $"[OK] 构建完成：{Path.Combine(result.ProjectRoot, "build")}"
        : $"[OK] 构建完成并已安装到：{build.InstalledTo}");
    return 0;
}
Console.WriteLine("[X] 构建失败，详见上面的日志。");
return 1;

static string? Next(string[] a, ref int i) => i + 1 < a.Length ? a[++i] : null;

static void PrintHelp() => Console.WriteLine("""
Sts2CharForge 命令行
  forge --check                                    自检效果库
  forge --detect                                   自动探测游戏安装目录
  forge --demo profile.json                        写出示例配置
  forge --profile profile.json                     生成模组工程
  forge --profile profile.json --build             生成 + 编译 + 导出 PCK（**不会安装**到任何地方）
  forge --profile profile.json --build --install "D:\...\mods"
                                                   还要**安装**到那个 mods 目录（必须显式给这个参数）
  forge --profile profile.json --out "D:\out"      指定输出目录
  forge --samplegen "D:\out"                        把示例配置生成一遍并打出每个文件的 SHA256（改生成端时的回归对照）
  forge --buildcheck                                自检：不给 --install 时**绝不安装**（红线⑧；构建前后都数一遍陷阱目录）
  forge --buildcheck --godot "D:\...\Godot_..._console.exe"
                                                    连「给了 --install 就真装一次」那一档一起验（要本机有 Godot）
  forge --recover "D:\...\生成的工程目录"           从工程反推回配置存档（存档被误覆盖时的救命功能）
  forge --fieldmatrix "D:\out.tsv"                  【开发用】量「65 种效果各读哪些栏位」（卡牌 / 遗物 / 药水三条链）

⚠ **要装到游戏里必须显式给 `--install <mods 目录>`**（V0.2.137 起）：
   不给 `--install` 时只生成 + 编译 + 导出 PCK，**一个文件都不会装** —— 存档里的 `Paths.InstallDir`
   是**给图形界面用的**，命令行不再拿它兜底（以前会，于是「只想编译一下」的探针会把模组真的装进
   存档里写着的游戏 mods 目录）。
  forge --recover "工程目录" --out "恢复.json"      指定恢复出来的存档路径
  forge --recover "工程目录" --profile 旧.json      用旧存档补「环境路径」这类反推不出来的字段
""");
