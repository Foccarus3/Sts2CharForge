using System.Text;
using System.Text.Json;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>生成 Godot/C# 工程文件、清单、导出预设、构建脚本，并拷贝本体依赖。</summary>
public static class ProjectFilesGen
{
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    public static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public static string ProjectGodot(Naming n) => $"""
; Engine configuration file.
; {n.ModId} —— 由 Sts2CharForge 生成

config_version=5

[application]

config/name="{n.ModId}"
config/features=PackedStringArray("4.5", "C#", "Forward Plus")

[dotnet]

project/assembly_name="{n.ModId}"

[editor]

; ★关键★ 关闭"文本资源转二进制"：否则导出时把 .tscn 转 .scn 需要实例化场景，
; 而本工程没有本体资源（Spine/贴图/本体脚本），实例化失败会让场景引用被整段丢弃。
export/convert_text_resources_to_binary=false
""";

    public static string Csproj(Naming n) => $"""
<Project Sdk="Godot.NET.Sdk/4.5.1">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>{n.Namespace}</RootNamespace>
    <AssemblyName>{n.ModId}</AssemblyName>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="sts2">
      <HintPath>$(MSBuildProjectDirectory)\libs\sts2.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(MSBuildProjectDirectory)\libs\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <Target Name="CopyCustomOutput" AfterTargets="Build">
    <ItemGroup>
      <OutputDlls Include="$(OutputPath)\$(AssemblyName).dll" />
      <OutputDlls Include="$(OutputPath)\$(AssemblyName).pdb" />
    </ItemGroup>
    <MakeDir Directories="$(MSBuildProjectDirectory)\build" />
    <Copy SourceFiles="@(OutputDlls)" DestinationFolder="$(MSBuildProjectDirectory)\build" />
    <Copy SourceFiles="$(MSBuildProjectDirectory)\mod_manifest.json"
          DestinationFiles="$(MSBuildProjectDirectory)\build\$(AssemblyName).json"
          Condition="Exists('$(MSBuildProjectDirectory)\mod_manifest.json')" />
  </Target>

</Project>
""";

    public static string Sln(Naming n)
    {
        string g = Guid.NewGuid().ToString().ToUpperInvariant();
        return $$"""
Microsoft Visual Studio Solution File, Format Version 12.00
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "{{n.ModId}}", "{{n.ModId}}.csproj", "{{{g}}}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		ExportDebug|Any CPU = ExportDebug|Any CPU
		ExportRelease|Any CPU = ExportRelease|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{{{g}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{{{g}}}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{{{g}}}.ExportDebug|Any CPU.ActiveCfg = ExportDebug|Any CPU
		{{{g}}}.ExportDebug|Any CPU.Build.0 = ExportDebug|Any CPU
		{{{g}}}.ExportRelease|Any CPU.ActiveCfg = ExportRelease|Any CPU
		{{{g}}}.ExportRelease|Any CPU.Build.0 = ExportRelease|Any CPU
	EndGlobalSection
EndGlobal
""";
    }

    public static string Manifest(CharacterProfile p)
    {
        var n = Naming.From(p);
        var obj = new Dictionary<string, object?>
        {
            ["id"] = n.ModId,
            ["name"] = string.IsNullOrWhiteSpace(p.ModDisplayName) ? p.DisplayName + " (" + n.ModId + ")" : p.ModDisplayName,
            ["author"] = p.Author,
            ["description"] = p.Description,
            ["version"] = p.Version,
            ["has_pck"] = true,
            ["has_dll"] = true,
            ["dependencies"] = Array.Empty<object>(),
            ["affects_gameplay"] = true,
        };
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    public static string ExportPresets() => """
[preset.0]

name="Windows Desktop"
platform="Windows Desktop"
runnable=true
advanced_options=false
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter="images/atlases/ui_atlas.tpsheet,images/atlases/potion_atlas.tpsheet,images/atlases/potion_outline_atlas.tpsheet"
exclude_filter="libs/*, build/*, cs/*, addons/*, bin/*, .idea/*, .godot/*, *.csproj, *.sln, *.md, *.ps1, *.user"
export_path=""
encryption_include_filters=""
encryption_exclude_filters=""
seed=0
encrypt_pck=false
encrypt_directory=false
script_export_mode=2

[preset.0.options]

custom_template/debug=""
custom_template/release=""
debug/export_console_wrapper=1
binary_format/embed_pck=false
texture_format/s3tc_bptc=true
texture_format/etc2_astc=false
binary_format/architecture="x86_64"
codesign/enable=false
application/modify_resources=true
""";

    public static string ExportPckScript(Naming n) => $$"""
# 导入资源 + 导出 PCK + 校验（由 Sts2CharForge 生成）
param(
    [string]$Godot = "",
    [string]$Preset = "Windows Desktop",
    [string]$Out = "build/{{n.ModId}}.pck"
)
$ErrorActionPreference = "Continue"
$proj = $PSScriptRoot
if (-not $Godot) {
    $cfg = Join-Path $proj "godot_path.txt"
    if (Test-Path $cfg) { $Godot = (Get-Content $cfg -Raw -Encoding UTF8).Trim() }
}
if (-not $Godot -or -not (Test-Path $Godot)) { Write-Host "[X] 找不到 Godot：$Godot" -ForegroundColor Red; exit 1 }

Write-Host "=== 1/3 导入资源 ===" -ForegroundColor Cyan
& $Godot --headless --path $proj --import 2>&1 | Select-String -Pattern 'ERROR: Cannot|Parse Error' | Select-Object -First 8

Write-Host "=== 2/3 导出 PCK ===" -ForegroundColor Cyan
$pck = Join-Path $proj $Out
& $Godot --headless --path $proj --export-pack $Preset $pck 2>&1 | Select-String -Pattern 'DONE|savepack|Failed to export' | Select-Object -First 5

Write-Host "=== 3/3 校验 ===" -ForegroundColor Cyan
if (-not (Test-Path $pck)) { Write-Host "[X] 没有生成 pck" -ForegroundColor Red; exit 1 }
$bytes = [System.IO.File]::ReadAllBytes($pck)
$text = [System.Text.Encoding]::UTF8.GetString($bytes)
Write-Host ("pck: {0} ({1} 字节)" -f $Out, $bytes.Length)
foreach ($p in @('mod_manifest.json', '{{n.ModId}}/localization/zhs/characters.json', 'scenes/creature_visuals/{{n.CharSlug}}.tscn', 'images/atlases/ui_atlas.tpsheet')) {
    $ok = $text.Contains($p)
    Write-Host ("  [{0}] {1}" -f $(if ($ok) { 'OK' } else { 'XX' }), $p) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}
""";

    public static string BuildScript(Naming n) => $$"""
# 构建 DLL + 导出 PCK + 安装（由 Sts2CharForge 生成）
# -Dotnet：dotnet.exe 的完整路径。留空 = 用 PATH 里的 dotnet。
#          整合包的「环境包」里带了便携版 .NET SDK，路径会自动写进 dotnet_path.txt。
param([string]$Install = "", [string]$Dotnet = "")
$ErrorActionPreference = "Continue"
$proj = $PSScriptRoot
if (-not $Dotnet) {
    $cfg = Join-Path $proj "dotnet_path.txt"
    if (Test-Path $cfg) { $Dotnet = (Get-Content $cfg -Raw -Encoding UTF8).Trim() }
}
if (-not $Dotnet) { $Dotnet = "dotnet" }
Push-Location $proj
try {
    Write-Host "=== 1/3 编译 DLL ===" -ForegroundColor Cyan
    & $Dotnet build "{{n.ModId}}.csproj" -c Debug
    if ($LASTEXITCODE -ne 0) { Write-Host "[X] 编译失败" -ForegroundColor Red; exit 1 }

    Write-Host "=== 2/3 导出 PCK ===" -ForegroundColor Cyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $proj "export_pck.ps1")

    if ($Install) {
        Write-Host "=== 3/3 安装到 $Install ===" -ForegroundColor Cyan
        $build = Join-Path $proj "build"
        Copy-Item (Join-Path $build "{{n.ModId}}.dll"),(Join-Path $build "{{n.ModId}}.pck"),(Join-Path $build "{{n.ModId}}.json") $Install -Force
        Get-ChildItem $Install -Filter "{{n.ModId}}.*" | Select-Object Name,Length | Format-Table -AutoSize
    } else {
        Write-Host "=== 3/3 跳过安装（未指定 -Install）===" -ForegroundColor Yellow
    }
}
finally { Pop-Location }
""";

    /// <summary>把本体的 sts2.dll / 0Harmony.dll 与 spine 插件拷进工程（这些不进 pck）。</summary>
    public static void CopyLocalDependencies(CharacterProfile p, string projectRoot, Action<string>? log = null)
    {
        string libs = Path.Combine(projectRoot, "libs");
        Directory.CreateDirectory(libs);
        foreach (string dll in new[] { "sts2.dll", "0Harmony.dll" })
        {
            string src = Path.Combine(p.Paths.GameDataDir, dll);
            if (File.Exists(src)) { File.Copy(src, Path.Combine(libs, dll), overwrite: true); log?.Invoke($"  libs/{dll}"); }
            else log?.Invoke($"  [警告] 找不到 {src}");
        }

        string spineSrc = Path.Combine(p.Paths.VanillaProject, "addons", "spine");
        if (Directory.Exists(spineSrc))
        {
            CopyDirectory(spineSrc, Path.Combine(projectRoot, "addons", "spine"));
            log?.Invoke("  addons/spine/（GDExtension）");
        }
        else log?.Invoke($"  [警告] 找不到 {spineSrc}，编辑器里 Spine 节点可能无法预览（不影响导出）");

        WriteText(Path.Combine(projectRoot, "godot_path.txt"), p.Paths.GodotExe);
        WriteText(Path.Combine(projectRoot, "dotnet_path.txt"), p.Paths.DotnetExe ?? "");
        WriteNuGetConfig(p, projectRoot, log);
    }

    /// <summary>
    /// 写一份 nuget.config，把 NuGet 源指向 Godot 编辑器自带的包目录
    /// （`GodotSharp\Tools\nupkgs` 里有 Godot.NET.Sdk / GodotSharp / 源生成器）。
    /// 这样**离线**也能还原：别人装上游戏和 Godot（或用整合包的环境包）就能编译，
    /// 不必连 nuget.org，也不会因为网络问题卡住。
    /// </summary>
    public static void WriteNuGetConfig(CharacterProfile p, string projectRoot, Action<string>? log = null)
    {
        string godot = (p.Paths.GodotExe ?? "").Trim();
        string? godotDir = godot.Length > 0 && File.Exists(godot) ? Path.GetDirectoryName(godot) : null;
        string? nupkgs = godotDir is null ? null : Path.Combine(godotDir, "GodotSharp", "Tools", "nupkgs");

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<configuration>");
        sb.AppendLine("  <packageSources>");
        sb.AppendLine("    <clear />");
        if (nupkgs is not null && Directory.Exists(nupkgs))
        {
            sb.AppendLine("    <!-- Godot 编辑器自带的 NuGet 包（含 Godot.NET.Sdk），离线可用 -->");
            sb.AppendLine($"    <add key=\"godot-local\" value=\"{System.Security.SecurityElement.Escape(nupkgs)}\" />");
            log?.Invoke("  nuget.config（离线源：Godot 自带的 nupkgs）");
        }
        else
        {
            sb.AppendLine("    <!-- 没找到 Godot 自带的 nupkgs，退回官方源 -->");
            sb.AppendLine("    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" protocolVersion=\"3\" />");
            log?.Invoke("  [警告] 没找到 Godot 自带的 nupkgs，nuget.config 用的是官方源（离线会还原失败）");
        }
        sb.AppendLine("  </packageSources>");
        sb.AppendLine("</configuration>");
        WriteText(Path.Combine(projectRoot, "nuget.config"), sb.ToString());
    }

    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string dst = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst, overwrite: true);
        }
    }

    /// <summary>写文本文件（默认无 BOM；.ps1 需要 BOM 才能在 Windows PowerShell 5.1 下正确读中文）。</summary>
    public static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // 生成物带 <auto-generated> 标记，编译器要求显式 #nullable 指令（否则 CS8669）
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
            content.StartsWith("// <auto-generated>", StringComparison.Ordinal))
        {
            int nl = content.IndexOf('\n');
            content = content[..(nl + 1)] + "#nullable enable\n" + content[(nl + 1)..];
        }

        bool needBom = path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(path, content, needBom ? Utf8Bom : Utf8NoBom);
    }
}
