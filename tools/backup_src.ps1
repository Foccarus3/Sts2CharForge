# ============================================================
#  源码快照 + 编码体检（防止 2026-10-08 那次「文件被写坏又发现不了」）
#
#  那次事故：某个工具把 MainWindow.xaml.cs 按错代码页重写了一遍（UTF-8 字节被当 CP936
#  读出来、再按 UTF-8 写回去）—— 文件变成一坨乱码、还丢了字节。因为**很久没提交过 git**
#  （HEAD 还停在 V0.2.51，落后 68 个版本），手边一个干净副本都没有，只能拿 DLL 里的字符串
#  当预言机一点点反推，花了整整一轮。这个脚本就干两件事：
#
#    ① 快照：把源码树（src / tools / docs / 工程文件）按时间戳整份复制到 _备份\源码\，
#       并给每个文件记 SHA256 清单（清单本身就是「干净版本长什么样」的凭据）；
#    ② 体检：扫「被写坏」的特征 —— 非法 UTF-8 字节 / U+FFFD（替换字符）/ 私用区 PUA 字符
#       （U+E000~U+F8FF，昨天那坨乱码就是这类字符），再和最近一次快照对比，
#       报出「哪些文件变了、变的那几个有没有坏味」。
#
#  用法：
#    powershell -ExecutionPolicy Bypass -File tools\backup_src.ps1            # 打一份快照 + 体检
#    powershell -ExecutionPolicy Bypass -File tools\backup_src.ps1 -Check     # 只体检，不复制
#    powershell -ExecutionPolicy Bypass -File tools\backup_src.ps1 -List      # 列出已有快照
#    powershell -ExecutionPolicy Bypass -File tools\backup_src.ps1 -Keep 40   # 改保留份数（默认 30）
#    powershell -ExecutionPolicy Bypass -File tools\backup_src.ps1 -Quiet     # 只输出一行摘要（给脚本调用）
#
#  退出码：0 = 干净；1 = 体检发现问题（非法 UTF-8 / U+FFFD / PUA 有命中）——
#          release_all.ps1 会拿它当「发布前拦住」的判据。
#
#  ⚠ 这个文件必须存成 **UTF-8 带 BOM** 的 .ps1：PowerShell 5.1 读无 BOM 的 UTF-8 会按 GBK 解，
#    中文全乱、脚本连解析都过不去（release_all.ps1 就是这么挂过一次）。
# ============================================================
param(
    [string]$T3 = (Split-Path -Parent $PSScriptRoot),
    [string]$Dest = "",
    [int]$Keep = 30,
    [switch]$Check,
    [switch]$List,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

function Say($t) { if (-not $Quiet) { Write-Host $t } }
function SayRed($t) { if (-not $Quiet) { Write-Host $t -ForegroundColor Red } }
function SayGreen($t) { if (-not $Quiet) { Write-Host $t -ForegroundColor Green } }

if (-not (Test-Path (Join-Path $T3 "src\Sts2CharForge.App\Sts2CharForge.App.csproj"))) { throw "T3 目录不对：$T3" }
if ([string]::IsNullOrWhiteSpace($Dest)) { $Dest = Join-Path (Split-Path -Parent $T3) "_备份\源码" }

# 要被快照 / 体检的顶层内容（**故意只收源码**：bin / obj / _tmp / 输出目录 / 存档 / zip 都不进）
$topItems = @('src', 'tools', 'docs')
$topFiles = @('README.md', '.gitignore')
$textExt = @('.cs', '.xaml', '.md', '.txt', '.ps1', '.bat', '.json', '.csproj', '.sln', '.template', '.cfg', '.gitignore')
# 复制时要排除的目录名（出现在任意层级）
$excludeDirs = @('bin', 'obj', '.godot', 'out', '.vs', '_tmp', 'node_modules')

# ---------------------------------------------------------------- 体检
# 严格 UTF-8 解码器：遇到非法字节直接抛，用来分辨「文件根本不是合法 UTF-8」
$strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)

function Get-Health([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $bad = $false
    $text = ""
    try { $text = $strictUtf8.GetString($bytes) } catch { $bad = $true; $text = [System.Text.Encoding]::UTF8.GetString($bytes) }
    $pua = ([regex]::Matches($text, '[\uE000-\uF8FF]')).Count
    $fffd = ([regex]::Matches($text, '\uFFFD')).Count
    return [pscustomobject]@{ Bad = $bad; Pua = $pua; Fffd = $fffd; Bytes = $bytes.Length; Text = $text }
}

function Test-Healthy([string]$path) {
    $h = Get-Health $path
    return ($h.Bad -or $h.Pua -gt 0 -or $h.Fffd -gt 0)
}

function Get-SourceFiles([string]$root) {
    $list = New-Object System.Collections.ArrayList
    foreach ($item in $topItems) {
        $p = Join-Path $root $item
        if (-not (Test-Path $p)) { continue }
        $files = Get-ChildItem -LiteralPath $p -Recurse -File -ErrorAction SilentlyContinue
        foreach ($f in $files) {
            $skip = $false
            foreach ($d in $excludeDirs) {
                if ($f.FullName -match ("\\" + [regex]::Escape($d) + "\\")) { $skip = $true; break }
            }
            if (-not $skip) { [void]$list.Add($f) }
        }
    }
    foreach ($f in $topFiles) {
        $p = Join-Path $root $f
        if (Test-Path $p) { [void]$list.Add((Get-Item -LiteralPath $p)) }
    }
    return $list
}

function Invoke-Check([string]$root, [string]$snapshotRoot, [int]$skipNewest = 0) {
    $files = Get-SourceFiles $root
    $badFiles = New-Object System.Collections.ArrayList
    foreach ($f in $files) {
        if ($textExt -notcontains $f.Extension.ToLowerInvariant() -and $f.Name -ne '.gitignore') { continue }
        if (Test-Healthy $f.FullName) {
            $h = Get-Health $f.FullName
            $why = @()
            if ($h.Bad) { $why += "非法 UTF-8" }
            if ($h.Fffd -gt 0) { $why += ("U+FFFD×" + $h.Fffd) }
            if ($h.Pua -gt 0) { $why += ("PUA×" + $h.Pua) }
            [void]$badFiles.Add(($f.FullName.Substring($root.Length).TrimStart('\') + "  →  " + ($why -join " / ")))
        }
    }

    # 和上一份快照对比：谁变了、变的那几个有没有坏味
    # $skipNewest = 1 用在「刚打完快照再体检」那条路上 —— 否则等于拿刚做出来的快照跟自己比，
    # 永远报「改了 0 个」（这个坑自己踩过）；-Check 模式不复制，所以用 0。
    $changed = @()
    $added = @()
    $missing = @()
    $snapshotName = "(无快照)"
    if (Test-Path $snapshotRoot) {
        $latest = Get-ChildItem -LiteralPath $snapshotRoot -Directory -ErrorAction SilentlyContinue |
                  Sort-Object Name -Descending | Select-Object -Skip $skipNewest -First 1
        if ($latest) {
            $snapshotName = $latest.Name
            $manifest = Join-Path $latest.FullName "清单.tsv"
            if (Test-Path $manifest) {
                $old = @{}
                foreach ($line in (Get-Content -LiteralPath $manifest -Encoding UTF8)) {
                    $parts = $line -split "`t"
                    if ($parts.Count -ge 3) { $old[$parts[0]] = $parts[1] }
                }
                $now = @{}
                foreach ($f in $files) {
                    $rel = $f.FullName.Substring($root.Length).TrimStart('\')
                    $now[$rel] = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
                }
                foreach ($k in $now.Keys) {
                    if (-not $old.ContainsKey($k)) { $added += $k }
                    elseif ($old[$k] -ne $now[$k]) { $changed += $k }
                }
                foreach ($k in $old.Keys) { if (-not $now.ContainsKey($k)) { $missing += $k } }
            }
        }
    }

    Say ("源码文件：" + $files.Count + " 个；最近快照：" + $snapshotName)
    if ($badFiles.Count -eq 0) { SayGreen "编码体检：干净（没有非法 UTF-8 / U+FFFD / PUA）" }
    else {
        SayRed ("编码体检：**" + $badFiles.Count + " 个文件有坏味**（昨天那次事故就是这个特征）")
        foreach ($b in $badFiles) { SayRed ("   " + $b) }
    }
    Say ("与最近快照相比：改了 " + $changed.Count + " 个 / 新增 " + $added.Count + " 个 / 少了 " + $missing.Count + " 个")
    if ($changed.Count -gt 0 -and -not $Quiet) {
        $show = $changed | Select-Object -First 12
        foreach ($c in $show) { Say ("   改过：" + $c) }
        if ($changed.Count -gt 12) { Say ("   …还有 " + ($changed.Count - 12) + " 个") }
    }
    if ($missing.Count -gt 0 -and -not $Quiet) {
        foreach ($m in ($missing | Select-Object -First 8)) { Say ("   少了：" + $m) }
    }
    return $badFiles.Count
}

# ---------------------------------------------------------------- 快照
function New-Snapshot([string]$root, [string]$snapshotRoot, [int]$keep) {
    $stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
    $target = Join-Path $snapshotRoot ("源码_" + $stamp)
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($item in ($topItems + $topFiles)) {
        $src = Join-Path $root $item
        if (-not (Test-Path $src)) { continue }
        $dst = Join-Path $target $item
        if ((Get-Item -LiteralPath $src).PSIsContainer) {
            # robocopy：/E 含空目录、/XD 排除 bin/obj/_tmp 之类；返回码 < 8 都算成功
            $xd = @()
            foreach ($d in $excludeDirs) { $xd += $d }
            $null = robocopy $src $dst /E /XD @xd /NFL /NDL /NJH /NJS /NP
            if ($LASTEXITCODE -ge 8) { throw ("复制失败（robocopy 返回 " + $LASTEXITCODE + "）：" + $src) }
        } else {
            Copy-Item -LiteralPath $src -Destination $dst -Force
        }
    }
    $files = Get-SourceFiles $target
    $sb = New-Object System.Text.StringBuilder
    foreach ($f in ($files | Sort-Object FullName)) {
        $rel = $f.FullName.Substring($target.Length).TrimStart('\')
        $hash = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
        [void]$sb.AppendLine($rel + "`t" + $hash + "`t" + $f.Length)
    }
    [System.IO.File]::WriteAllText((Join-Path $target "清单.tsv"), $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))
    [System.IO.File]::WriteAllText((Join-Path $target "说明.txt"),
        ("源码快照 " + $stamp + "`r`n文件数：" + $files.Count + "`r`n" +
         "内容：src / tools / docs / README.md / .gitignore（bin / obj / _tmp / 输出 / 存档都不含）`r`n" +
         "怎么还原：把要救的文件从这个目录复制回 " + $root + " 对应位置即可；`r`n" +
         "         想整份比对差异：tools\backup_src.ps1 -Check（会列出改了哪些文件）。`r`n"),
        (New-Object System.Text.UTF8Encoding($true)))

    # 只留最近 $keep 份
    $all = Get-ChildItem -LiteralPath $snapshotRoot -Directory | Sort-Object Name -Descending
    if ($all.Count -gt $keep) {
        foreach ($old in ($all | Select-Object -Skip $keep)) {
            Remove-Item -LiteralPath $old.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    return @{ Path = $target; Files = $files.Count }
}

# ---------------------------------------------------------------- 主流程
if ($List) {
    if (-not (Test-Path $Dest)) { Say "还没有任何快照（" + $Dest + "）"; exit 0 }
    $all = Get-ChildItem -LiteralPath $Dest -Directory | Sort-Object Name -Descending
    Say ("快照目录：" + $Dest + "（共 " + $all.Count + " 份）")
    foreach ($d in $all) {
        $manifest = Join-Path $d.FullName "清单.tsv"
        $n = if (Test-Path $manifest) { (Get-Content -LiteralPath $manifest -Encoding UTF8).Count } else { 0 }
        $size = [math]::Round(((Get-ChildItem -LiteralPath $d.FullName -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 2)
        Say ("   " + $d.Name + "   " + $n + " 个文件  " + $size + " MB")
    }
    exit 0
}

if (-not $Check) { $null = New-Item -ItemType Directory -Path $Dest -Force }
$bad = 0
if ($Check) {
    $bad = Invoke-Check $T3 $Dest
    if ($Quiet) { Write-Host ("编码体检：" + $(if ($bad -eq 0) { "干净" } else { "$bad 个文件有坏味" })) }
    exit $(if ($bad -eq 0) { 0 } else { 1 })
}

$snap = New-Snapshot $T3 $Dest $Keep
Say ("已快照：" + $snap.Path + "（" + $snap.Files + " 个文件）")
# 对比对象是**上一份**快照（跳过刚打出来的这份），否则永远「改了 0 个」
$bad = Invoke-Check $T3 $Dest 1
if ($Quiet) { Write-Host ("快照 " + $snap.Files + " 个文件 → " + $snap.Path + "；编码体检：" + $(if ($bad -eq 0) { "干净" } else { "$bad 个文件有坏味" })) }
exit $(if ($bad -eq 0) { 0 } else { 1 })
