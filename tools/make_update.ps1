# 打「更新包」zip（只含程序文件，给已经有整合包的人覆盖更新用）
#
#   内容 = 程序文件\** + 启动器「启动 Sts2CharForge_<版本>.bat」
#   不含：环境包（Godot / 便携 dotnet）、「！首次使用先点这个！」、教程附件、用户存档 —— 那些在整合包里已经有了。
#
#   为什么要单独一个包：整合包 300+ MB，每次改一点东西都重下太痛苦；
#   更新包只有程序本身（自包含运行时），解压覆盖到原来的根目录即可，环境包 / 教程都不用动。
#
#   版本号只有一个来源：src\Sts2CharForge.App\Sts2CharForge.App.csproj 里的 <InformationalVersion>
#
# 用法： powershell -ExecutionPolicy Bypass -File tools\make_update.ps1
param(
    [string]$Root = "D:\ds\s\4",
    # 空 = 按 csproj 里的版本号自动取名（Sts2CharForge_更新包_<V0.0.1>.zip）
    [string]$Out  = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path $Root)) { throw "部署根目录不存在：$Root" }

# 版本号：和启动器 / 程序包 zip / 整合包同一个来源（App 的 csproj）
$Version = "V0.0.1"
$csproj = Join-Path (Split-Path -Parent $PSScriptRoot) "src\Sts2CharForge.App\Sts2CharForge.App.csproj"
if (Test-Path $csproj) {
    $m = [regex]::Match((Get-Content $csproj -Raw), '<InformationalVersion>\s*([^<\s]+)\s*</InformationalVersion>')
    if ($m.Success) { $Version = $m.Groups[1].Value }
}
if ([string]::IsNullOrWhiteSpace($Out)) { $Out = "D:\ds\s\Sts2CharForge_更新包_$Version.zip" }
$launcherName = "启动 Sts2CharForge_$Version"
$progName = "程序文件"
Write-Host "版本号：$Version（更新包 $([System.IO.Path]::GetFileName($Out))，启动器 $launcherName.bat）"

$progDir = Join-Path $Root $progName
if (-not (Test-Path (Join-Path $progDir "Sts2CharForge.exe"))) { throw "找不到程序：$progDir\Sts2CharForge.exe（先跑 tools\package_app.ps1 发布一份）" }

# 「包里绝不能出现」的字符串：本机开发/打包目录 + 本机用户名（和整合包同一套规则）
$userName = Split-Path $env:USERPROFILE -Leaf
$leakPatterns = New-Object System.Collections.Generic.List[string]
foreach ($p in @('D:\ds', 'D:\download', '_packaging_keep', 'ds\s\t3', 't3\src')) { $leakPatterns.Add($p) }
if ($userName) { $leakPatterns.Add("C:\Users\$userName") }
$leakScanMaxBytes = 8MB

# 自带的垃圾文件不进包（自检结果 / 崩溃日志 / 日志 / 快捷方式）
$junk = [regex]'^(uicheck|envcheck|buildtest|newprofiletest|movedtest)_result\.txt$|^crash_log\.txt$|\.log$'

if (Test-Path $Out) { Remove-Item $Out -Force }

$zip = [System.IO.Compression.ZipFile]::Open($Out, 'Create')
$count = 0
$skipped = 0
try {
    # ① 程序文件夹整份进去（保持「程序文件/...」这层目录，解压覆盖即可）
    foreach ($f in Get-ChildItem $progDir -Recurse -File -Force) {
        $rel = $progName + '/' + $f.FullName.Substring($progDir.Length + 1).Replace('\', '/')
        if ($f.Extension -eq '.lnk') { $skipped++; continue }
        if ($junk.IsMatch($f.Name)) { $skipped++; continue }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $count++
    }
    # ② 启动器（同版本的 .bat；.lnk 的快捷方式指向绝对路径，不发）
    $launcher = Join-Path $Root "$launcherName.bat"
    if (Test-Path $launcher) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $launcher, "$launcherName.bat", [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $count++
        Write-Host "  已放入启动器：$launcherName.bat"
    } else {
        Write-Host "  [提示] 没找到启动器 $launcherName.bat（根目录里可能还是旧版本号的；更新包就不含启动器了）"
    }
} finally { $zip.Dispose() }

# ===== 打完自检：不许有游戏文件 / 存档 / 本机路径 =====
$bad = New-Object System.Collections.Generic.List[string]
$check = [System.IO.Compression.ZipFile]::OpenRead($Out)
try {
    foreach ($e in $check.Entries) {
        if ($e.Name -eq 'sts2.dll' -or $e.Name -eq '0Harmony.dll') { $bad.Add("游戏文件: " + $e.FullName) | Out-Null }
        if ($e.FullName -like '自定义角色存档/*') { $bad.Add("存档: " + $e.FullName) | Out-Null }
        if (-not $e.FullName.StartsWith($progName + '/') -and $e.FullName -ne "$launcherName.bat") {
            $bad.Add("不该有的条目: " + $e.FullName) | Out-Null
        }
        foreach ($p in $leakPatterns) {
            if ($e.FullName.IndexOf($p, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $bad.Add("路径泄漏（条目名）: " + $e.FullName + "  ← " + $p) | Out-Null
            }
        }
        if ($e.Length -gt 0 -and $e.Length -le $leakScanMaxBytes -and $e.FullName.StartsWith($progName + '/')) {
            try {
                $ms = New-Object System.IO.MemoryStream
                $st = $e.Open(); $st.CopyTo($ms); $st.Close()
                $bytes = $ms.ToArray(); $ms.Dispose()
                $asLatin = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
                $asUni = [System.Text.Encoding]::Unicode.GetString($bytes)
                foreach ($p in $leakPatterns) {
                    if ($asLatin.IndexOf($p, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                        $asUni.IndexOf($p, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                        $bad.Add("路径泄漏（内容）: " + $e.FullName + "  ← " + $p) | Out-Null
                    }
                }
            } catch { }
        }
    }
} finally { $check.Dispose() }

if ($bad.Count -gt 0) {
    Remove-Item $Out -Force
    Write-Host "自检失败：更新包里出现了不该有的条目（已删掉这个 zip）" -ForegroundColor Red
    $bad | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    throw "更新包自检未通过"
}

$size = (Get-Item $Out).Length / 1MB
Write-Host ("完成：{0}  （{1:N1} MB，{2} 个文件；跳过 {3} 个）" -f $Out, $size, $count, $skipped)
Write-Host ("  自检：只含「{0}\」+ 启动器；无存档、无本机路径、无游戏文件" -f $progName) -ForegroundColor Green
