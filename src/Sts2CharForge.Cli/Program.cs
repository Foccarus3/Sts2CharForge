using Sts2CharForge.Core.Build;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Generation;
using Sts2CharForge.Core.Profile;

// Sts2CharForge 命令行：与界面共用同一套核心
//   forge --check                                   自检（效果库数量等）
//   forge --demo profile.json                       写出示例配置
//   forge --profile profile.json                    只生成工程
//   forge --profile profile.json --build            生成 + 编译 + 导出 PCK
//   forge --profile profile.json --build --install "D:\...\mods"
//   forge --profile profile.json --out "D:\out"     指定输出目录

string? profilePath = null, outDir = null, installDir = null, recoverDir = null;
bool doBuild = false, demo = false, check = false, detect = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--profile": profilePath = Next(args, ref i); break;
        case "--out": outDir = Next(args, ref i); break;
        case "--install": installDir = Next(args, ref i); break;
        case "--recover": recoverDir = Next(args, ref i); break;
        case "--build": doBuild = true; break;
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

if (profilePath is null) { PrintHelp(); return 1; }
if (!File.Exists(profilePath)) { Console.WriteLine("[X] 找不到配置：" + profilePath); return 1; }

CharacterProfile profile;
try { profile = ProfileFactory.Load(profilePath); }
catch (Exception e) { Console.WriteLine("[X] 配置解析失败：" + e.Message); return 1; }

if (outDir is not null) profile.Paths.OutputDir = outDir;

// 效果库（增益/减益列表）是运行时从本机的游戏文件读取的：优先解包工程，其次从 sts2.dll 反射兜底
EffectCatalog.Initialize(profile.Paths.VanillaProject, profile.Paths.GameDataDir);
Console.WriteLine("效果库：" + EffectCatalog.CatalogStatus);
if (installDir is not null) profile.Paths.InstallDir = installDir;
if (string.IsNullOrWhiteSpace(profile.Paths.OutputDir))
    profile.Paths.OutputDir = Path.Combine(AppContext.BaseDirectory, "自定义角色");

Console.WriteLine($"=== 生成「{profile.DisplayName}」（{profile.ModId}）===");
var result = ModGenerator.Generate(profile, Console.WriteLine);
if (!result.Success) { Console.WriteLine("[X] 生成失败。"); return 1; }
if (!doBuild) { Console.WriteLine("工程已生成：" + result.ProjectRoot); return 0; }

Console.WriteLine();
Console.WriteLine("=== 构建 ===");
var build = ModBuilder.Build(result.ProjectRoot, Naming.From(profile).ModId, profile.Paths.GodotExe,
    installDir ?? profile.Paths.InstallDir, Console.WriteLine, profile.Paths.DotnetExe);

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
  forge --profile profile.json --build             生成 + 编译 + 导出 PCK
  forge --profile profile.json --build --install "D:\...\mods"
  forge --profile profile.json --out "D:\out"      指定输出目录
  forge --recover "D:\...\生成的工程目录"           从工程反推回配置存档（存档被误覆盖时的救命功能）
  forge --recover "工程目录" --out "恢复.json"      指定恢复出来的存档路径
  forge --recover "工程目录" --profile 旧.json      用旧存档补「环境路径」这类反推不出来的字段
""");
