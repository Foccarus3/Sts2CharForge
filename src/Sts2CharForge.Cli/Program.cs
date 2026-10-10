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

string? profilePath = null, outDir = null, installDir = null, recoverDir = null, sampleGenDir = null, becCheckGodotExe = null;
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
    installDir, Console.WriteLine, profile.Paths.DotnetExe);

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

⚠ **要装到游戏里必须显式给 `--install <mods 目录>`**（V0.2.137 起）：
   不给 `--install` 时只生成 + 编译 + 导出 PCK，**一个文件都不会装** —— 存档里的 `Paths.InstallDir`
   是**给图形界面用的**，命令行不再拿它兜底（以前会，于是「只想编译一下」的探针会把模组真的装进
   存档里写着的游戏 mods 目录）。
  forge --recover "工程目录" --out "恢复.json"      指定恢复出来的存档路径
  forge --recover "工程目录" --profile 旧.json      用旧存档补「环境路径」这类反推不出来的字段
""");
