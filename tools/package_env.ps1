# 打包「环境包」：Godot 4.5.1 mono + 便携版 .NET SDK 9
#   · 两个压缩包配合使用：程序包（杀戮尖塔2角色生成器V0.0.1）+ 环境包
#   · 环境包里只有开源软件（Godot: MIT，.NET: MIT），不含任何游戏素材
#   · .NET SDK 用本机已装的那份做一个「瘦身便携版」：删掉用不到的 F#、老版本运行时、
#     AspNetCore/WindowsDesktop、其它架构的 apphost 等，剩下编译 net9.0 模组所需的部分
#   · 生成的 .NET_ROOT / PATH 由程序在构建时自动设置（见 ModBuilder），
#     便携 SDK 里的 dotnet.exe 由「自动探测本机环境」按钮或「路径」页手工选中
# 用法： powershell -ExecutionPolicy Bypass -File tools\package_env.ps1
param(
    [string]$Stage = "D:\ds\s\Sts2CharForge_环境包",
    [string]$Zip = "D:\ds\s\Sts2CharForge_环境包_win-x64.zip",
    [string]$GodotSrc = "D:\download\slay\godot\Godot_v4.5.1-stable_mono_win64",
    [string]$DotnetSrc = "C:\Program Files\dotnet",
    [string]$SdkVersion = "9.0.318",
    [string]$RuntimeVersion = "9.0.20",
    [switch]$SkipGodot
)

$ErrorActionPreference = "Stop"
function Step($t) { Write-Host "=== $t" }

if (-not $SkipGodot -and -not (Test-Path $GodotSrc)) { throw "找不到 Godot 源目录：$GodotSrc" }
if (-not (Test-Path (Join-Path $DotnetSrc "sdk\$SdkVersion"))) { throw "找不到 .NET SDK $SdkVersion：$DotnetSrc\sdk\$SdkVersion" }

Remove-Item $Stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $Stage | Out-Null

# 1) Godot（整个目录照搬，包含 GodotSharp 与它自带的 nupkgs —— 离线还原 NuGet 就靠这个）
if (-not $SkipGodot) {
    Step "拷 Godot"
    $dst = Join-Path $Stage "godot"
    New-Item -ItemType Directory -Force $dst | Out-Null
    $godotDir = Join-Path $dst (Split-Path -Leaf $GodotSrc)
    Copy-Item $GodotSrc $godotDir -Recurse -Force

    # 官方 Windows 包不自带许可文件，MIT 要求「随分发附上版权与许可声明」，这里补上
    foreach ($pair in @(@("godot_LICENSE.txt", "LICENSE.txt"), @("godot_COPYRIGHT.txt", "COPYRIGHT.txt"))) {
        $src = Join-Path $PSScriptRoot ("licenses\" + $pair[0])
        if (Test-Path $src) { Copy-Item $src (Join-Path $godotDir $pair[1]) -Force }
        else { Write-Host "  [警告] 缺少许可文件 $src" }
    }

    $mb = (Get-ChildItem $dst -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
    Write-Host ("  godot → {0:N0} MB（含 LICENSE.txt / COPYRIGHT.txt）" -f $mb)
}

# 2) 便携版 .NET SDK
Step "拷 .NET SDK（瘦身）"
$dn = Join-Path $Stage "dotnet"
New-Item -ItemType Directory -Force $dn | Out-Null

# 2.1 宿主
Copy-Item (Join-Path $DotnetSrc "dotnet.exe") $dn -Force
foreach ($f in @("LICENSE.txt", "ThirdPartyNotices.txt")) {
    if (Test-Path (Join-Path $DotnetSrc $f)) { Copy-Item (Join-Path $DotnetSrc $f) $dn -Force }
}
foreach ($d in @("host", "metadata", "sdk-manifests", "templates", "swidtag")) {
    $src = Join-Path $DotnetSrc $d
    if (Test-Path $src) { Copy-Item $src (Join-Path $dn $d) -Recurse -Force }
}

# 2.2 SDK（去掉 F#、其它语言资源、测试宿主、容器工具 —— 编译 C# 用不到）
$sdkDst = Join-Path $dn "sdk\$SdkVersion"
New-Item -ItemType Directory -Force $sdkDst | Out-Null
$drop = @("FSharp", "TestHostNetFramework", "Containers", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hant")
Get-ChildItem (Join-Path $DotnetSrc "sdk\$SdkVersion") | Where-Object { $drop -notcontains $_.Name } | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $sdkDst $_.Name) -Recurse -Force
}

# 2.3 运行时：只要 Microsoft.NETCore.App 的 9.x（模组是 net9.0 类库，不需要桌面/AspNetCore 运行时）
$rtDst = Join-Path $dn "shared\Microsoft.NETCore.App\$RuntimeVersion"
if (-not (Test-Path (Join-Path $DotnetSrc "shared\Microsoft.NETCore.App\$RuntimeVersion"))) {
    $newest = Get-ChildItem (Join-Path $DotnetSrc "shared\Microsoft.NETCore.App") -Directory |
              Where-Object Name -Like "9.*" | Sort-Object Name | Select-Object -Last 1
    if (-not $newest) { throw "找不到 9.x 的 Microsoft.NETCore.App 运行时" }
    $rtDst = Join-Path $dn ("shared\Microsoft.NETCore.App\" + $newest.Name)
    Write-Host "  （用找到的 $($newest.Name)）"
}
New-Item -ItemType Directory -Force $rtDst | Out-Null
Copy-Item (Join-Path $DotnetSrc "shared\Microsoft.NETCore.App\$(Split-Path -Leaf $rtDst)\*") $rtDst -Recurse -Force

# 2.4 编译用的 packs：只留 9.x 的目标包 + x64 的 apphost + NETStandard
#     注意：AspNetCore / WindowsDesktop 的 Ref 包看着用不上，但 .NET SDK 的还原图里默认就带
#     这两项，缺了它们**离线**还原会直接失败（NU1101 找不到包）。实测踩过，别删。
Step "拷 packs（只留 9.x / win-x64）"
foreach ($pack in @("Microsoft.NETCore.App.Ref", "Microsoft.NETCore.App.Host.win-x64",
                    "Microsoft.AspNetCore.App.Ref", "Microsoft.WindowsDesktop.App.Ref",
                    "NETStandard.Library.Ref")) {
    $srcPack = Join-Path $DotnetSrc "packs\$pack"
    if (-not (Test-Path $srcPack)) { Write-Host "  [跳过] 没有 $pack"; continue }
    foreach ($ver in (Get-ChildItem $srcPack -Directory)) {
        # NETStandard.Library.Ref 只有 2.1.0 一个版本，照留；其余只留 9.x
        if ($pack -ne "NETStandard.Library.Ref" -and -not $ver.Name.StartsWith("9.")) { continue }
        $target = Join-Path $dn "packs\$pack\$($ver.Name)"
        New-Item -ItemType Directory -Force $target | Out-Null
        Copy-Item (Join-Path $ver.FullName "*") $target -Recurse -Force
    }
}

$dnMb = (Get-ChildItem $dn -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("  dotnet → {0:N0} MB" -f $dnMb)

# 3) 说明与许可
Step "写说明与许可"
$readme = @'
Sts2CharForge 环境包（Godot + 便携版 .NET SDK）
=============================================

这个包只装两样「开源软件」，用来给 Sts2CharForge（角色生成器）当外部依赖。
它【不含】任何《杀戮尖塔 2》的文件，游戏本体要你自己在 Steam 上买。

一、怎么用（最省事的方式）
  1. 把「程序包」和「环境包」都解压到【同一个文件夹】下，解压后长这样：
        某个文件夹\
            程序文件\             ← 程序包里的
            启动 Sts2CharForge.bat ← 程序包里的
            Sts2CharForge_环境包\
                godot\            ← 环境包里的
                dotnet\           ← 环境包里的
  2. 双击「启动 Sts2CharForge.bat」打开程序。
  3. 到「构建 / 日志」页点一下「自动探测本机环境」：
     它会自动认出这份 Godot 和便携版 dotnet，并写进你的存档。
  4. 到「环境自检」页确认是绿色 ✅。

  如果懒得放一起，也可以解压到任意位置，然后在「构建 / 日志」页的「环境路径」里
  手工选：Godot 选 godot\Godot_v4.5.1-stable_mono_win64\..._console.exe，
  dotnet 选 dotnet\dotnet.exe。

二、里面有什么
  godot\  Godot 4.5.1 stable（mono 版，MIT 许可）
          —— 用来把生成的工程导出成 .pck。
          注意必须用带 console 的那个 exe，构建失败时才有日志。
  dotnet\ .NET SDK 9（MIT 许可）的便携版
          —— 用来把生成的 C# 模组代码编译成 dll。
          程序构建时会自动设好 DOTNET_ROOT 和 PATH，指向这一份，
          所以就算你电脑上没装 .NET、或者装了别的版本，也不影响。

三、还需要你自己准备的（这个包给不了）
  1. 《杀戮尖塔 2》本体（Steam 上买的正版）—— 提供编译用的 sts2.dll / 0Harmony.dll。
  2. 游戏「解包后的原版工程」—— 生成模组时的占位美术、场景模板、spine 插件都从这里取。
     做法见程序包里的「！！！！启动前必看！！！！！\！！！不看用不了！！！\环境构建流程.docx」，
     用 GDRE Tools 解包自己的游戏本体即可（这一步涉及你自己的游戏文件，不能随包分发）。

四、许可
  本包内的软件均为开源软件，可自由再分发，但请保留各自的许可声明：
    · Godot Engine 4.5.1 — MIT License，Copyright (c) 2014-present Godot Engine contributors,
      Copyright (c) 2008-2014 Juan Linietsky, Ariel Manzur.  https://godotengine.org/license
      完整文本见 godot\Godot_v4.5.1-stable_mono_win64\LICENSE.txt；
      内含第三方组件（FreeType、HarfBuzz、SDL、mbedTLS 等）的清单与许可见同目录的 COPYRIGHT.txt。
    · .NET — MIT License，Copyright (c) .NET Foundation and Contributors.  https://github.com/dotnet/runtime
    · 见同目录的「第三方许可.txt」以及 dotnet\LICENSE.txt、dotnet\ThirdPartyNotices.txt。
'@
[System.IO.File]::WriteAllText((Join-Path $Stage "环境包_使用说明.txt"), $readme, (New-Object System.Text.UTF8Encoding($true)))

$lic = @'
本环境包内第三方软件的许可声明
================================

1) Godot Engine 4.5.1（godot\ 目录）
   MIT License

   Copyright (c) 2014-present Godot Engine contributors.
   Copyright (c) 2008-2014 Juan Linietsky, Ariel Manzur.

   Permission is hereby granted, free of charge, to any person obtaining a copy
   of this software and associated documentation files (the "Software"), to deal
   in the Software without restriction, including without limitation the rights
   to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
   copies of the Software, and to permit persons to whom the Software is
   furnished to do so, subject to the following conditions:

   The above copyright notice and this permission notice shall be included in all
   copies or substantial portions of the Software.

   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
   SOFTWARE.

   Godot 还包含若干第三方组件，完整清单见 https://godotengine.org/license

2) .NET SDK 9（dotnet\ 目录）
   MIT License，Copyright (c) .NET Foundation and Contributors.
   完整文本见 dotnet\LICENSE.txt，第三方组件声明见 dotnet\ThirdPartyNotices.txt。

3) 本包不包含任何《Slay the Spire 2》的游戏文件。
'@
[System.IO.File]::WriteAllText((Join-Path $Stage "第三方许可.txt"), $lic, (New-Object System.Text.UTF8Encoding($true)))

# 4) 自检：这一份便携 SDK 能不能跑起来
Step "自检便携 SDK"
$portableDotnet = Join-Path $dn "dotnet.exe"
$env:DOTNET_ROOT = $dn
$env:PATH = $dn + ";" + $env:PATH
$out = & $portableDotnet --list-sdks 2>&1
if ($LASTEXITCODE -ne 0) { throw "便携版 dotnet 跑不起来：$out" }
Write-Host ("  dotnet --list-sdks → " + ($out -join " | "))
$out2 = & $portableDotnet --list-runtimes 2>&1
Write-Host ("  dotnet --list-runtimes → " + (($out2 | Select-String 'NETCore') -join " | "))

# 5) 打 zip（根目录 = Sts2CharForge_环境包\）
Step "打 zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tmpZip = Join-Path $env:TEMP (Split-Path -Leaf $Zip)
Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
[System.IO.Compression.ZipFile]::CreateFromDirectory($Stage, $tmpZip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
Remove-Item $Zip -Force -ErrorAction SilentlyContinue
Move-Item $tmpZip $Zip -Force

$total = (Get-ChildItem $Stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("完成：{0}  （解压后 {1:N0} MB → 压缩包 {2:N1} MB）" -f $Zip, $total, ((Get-Item $Zip).Length / 1MB))
