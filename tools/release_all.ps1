# 一条命令走完一次发布：升版本号 → 发布程序 → 打「整合包 + 更新包」→ 发到 GitHub Releases
#
# 为什么要有这个脚本：以前这四步是手敲四条命令、顺序还不能错（比如必须先 package_app
# 再 make_update，否则更新包里是旧程序），发布时容易漏一步。这里把它们串起来并在每步后校验。
#
# 版本号仍然只有一个来源：src\Sts2CharForge.App\Sts2CharForge.App.csproj 里的 <InformationalVersion>。
# 本脚本只负责「改那一个地方」，启动器名 / zip 名 / 窗口标题 / GitHub tag 全都会跟着变。
#
# 约定（用户要求，别再改回去）：
#   · README.md 与 使用说明.txt **只讲功能与用法**，不写版本号、不写更新记录。
#   · 版本变更只出现在两处：csproj 的 InformationalVersion，以及 docs\RELEASE_NOTES_<版本>.md
#     （后者只作为 GitHub Release 正文，不进 README）。
#   · 启动器文件名里的版本号由打包脚本自动拼，同样不需要写进文档。
#
# 用法（最常用）：
#   powershell -ExecutionPolicy Bypass -File tools\release_all.ps1
#       → 把 V0.0.9 升成 V0.0.10，重新发布程序、打两个包、发 GitHub Release
#   powershell -ExecutionPolicy Bypass -File tools\release_all.ps1 -Bump none -NoRelease
#       → 不升版本、不发 GitHub，只重新打包（本地自测用）
#   powershell -ExecutionPolicy Bypass -File tools\release_all.ps1 -Version V0.1.0
#       → 直接指定版本号（忽略 -Bump）
#   powershell -ExecutionPolicy Bypass -File tools\release_all.ps1 -Commit
#       → 发布完成后顺手 git add/commit/push
#
# 发布说明：docs\RELEASE_NOTES_<版本>.md（缺了会提示，-NewNotes 可生成骨架）
param(
    # patch = V0.0.9→V0.0.10；minor = →V0.1.0；major = →V1.0.0；none = 不动版本号
    [ValidateSet('patch','minor','major','none')]
    [string]$Bump = 'patch',
    # 直接指定版本号（形如 V0.0.10）；给了就忽略 -Bump
    [string]$Version = "",
    [string]$T3 = (Split-Path -Parent $PSScriptRoot),
    # 部署根目录（程序安装目录）。留空自动解析：环境变量 STS2FORGE_APP → 仓库同级的 4 → 用户目录下的 Sts2CharForge
    [string]$App = "",
    # 两个 zip 的输出目录。留空 = 仓库的上一级目录
    [string]$OutDir = "",
    [string]$Repo = "",
    [string]$NotesFile = "",
    # 跳过 dotnet publish（复用现有的「程序文件」，只重打包）
    [switch]$SkipBuild,
    # 不重打整合包（370 MB，网络慢时可跳过）
    [switch]$SkipBundle,
    # 不重打更新包
    [switch]$SkipUpdate,
    # 只打包，不发 GitHub
    [switch]$NoRelease,
    # 发布脚本只做自检，不真发
    [switch]$DryRun,
    [switch]$Prerelease,
    # 发布说明缺失时，生成一个骨架文件后退出（让你填内容再重跑）
    [switch]$NewNotes,
    # 全部步骤都不再问「继续吗」
    [switch]$Yes,
    # 结束后顺手提交并推送仓库
    [switch]$Commit
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

function Step($t)  { Write-Host ""; Write-Host ("=== " + $t) -ForegroundColor Cyan }
function Info($t)  { Write-Host ("    " + $t) }
function Fail($t)  { Write-Host ("[失败] " + $t) -ForegroundColor Red; throw $t }

# ---------- 0) 路径 ----------
if (-not (Test-Path (Join-Path $T3 "src\Sts2CharForge.App\Sts2CharForge.App.csproj"))) { Fail "T3 目录不对：$T3" }
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Split-Path -Parent $T3 }
if ([string]::IsNullOrWhiteSpace($App)) {
    if ($env:STS2FORGE_APP) { $App = $env:STS2FORGE_APP }
    else {
        $sibling = Join-Path (Split-Path -Parent $T3) '4'
        if (Test-Path $sibling) { $App = $sibling } else { $App = Join-Path $env:USERPROFILE 'Sts2CharForge' }
    }
}
$csproj = Join-Path $T3 "src\Sts2CharForge.App\Sts2CharForge.App.csproj"
$py  = Join-Path $PSScriptRoot "package_app.ps1"
$mb  = Join-Path $PSScriptRoot "make_bundle.ps1"
$mu  = Join-Path $PSScriptRoot "make_update.ps1"
$rg  = Join-Path $PSScriptRoot "release_github.ps1"
foreach ($p in $py, $mb, $mu) { if (-not (Test-Path $p)) { Fail "缺少脚本：$p" } }

# 子脚本一律另起进程跑：它们各自都设了 $ErrorActionPreference / 定义了同名函数，
# 同进程串起来容易互相影响；另起进程还能原样保留每步自己的输出。
# 注意：参数名**不能叫 $Args** —— 那是 PowerShell 的自动变量（Object[]），
# 传 hashtable 进来会直接报「无法把 Object[] 转成 Hashtable」（实测踩过）。
function Invoke-Step {
    param([string]$Title, [string]$Script, [hashtable]$StepArgs)
    Step $Title
    $argv = @('-ExecutionPolicy','Bypass','-File', $Script)
    if ($StepArgs) {
        foreach ($k in $StepArgs.Keys) {
            $v = $StepArgs[$k]
            # 开关参数只写名字（写成 -Prerelease True 会把 "True" 变成位置参数，踩过）
            if ($v -is [bool]) { if ($v) { $argv += ('-' + $k) }; continue }
            if ($null -eq $v -or "$v" -eq '') { continue }
            $argv += ('-' + $k)
            $argv += [string]$v
        }
    }
    & powershell @argv
    if ($LASTEXITCODE -ne 0) { Fail ("$Title 失败（exit $LASTEXITCODE）") }
}

# ---------- 1) 版本号 ----------
Step "版本号"
$raw = [System.IO.File]::ReadAllText($csproj, [System.Text.Encoding]::UTF8)
$m = [regex]::Match($raw, '<InformationalVersion>\s*([^<\s]+)\s*</InformationalVersion>')
if (-not $m.Success) { Fail "csproj 里找不到 <InformationalVersion>：$csproj" }
$current = $m.Groups[1].Value
Info "当前：$current"

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $target = $Version
} elseif ($Bump -eq 'none') {
    $target = $current
} else {
    $v = [regex]::Match($current, '^V(\d+)\.(\d+)\.(\d+)$')
    if (-not $v.Success) { Fail "当前版本号格式不认识：$current（应为 V主.次.修订，例如 V0.0.9）" }
    $a = [int]$v.Groups[1].Value; $b = [int]$v.Groups[2].Value; $c = [int]$v.Groups[3].Value
    switch ($Bump) {
        'patch' { $c = $c + 1 }
        'minor' { $b = $b + 1; $c = 0 }
        'major' { $a = $a + 1; $b = 0; $c = 0 }
    }
    $target = "V$a.$b.$c"
}
if ($target -notmatch '^V\d+\.\d+\.\d+$') { Fail "目标版本号格式不对：$target（应为 V主.次.修订）" }
if ($target -ne $current) { Info ("新版本：$target（-Bump " + $Bump + "）") } else { Info "版本号不变" }

# ---------- 2) 发布说明 ----------
if ([string]::IsNullOrWhiteSpace($NotesFile)) { $NotesFile = Join-Path $T3 ("docs\RELEASE_NOTES_" + $target + ".md") }
Step "发布说明"
Info $NotesFile
if (-not (Test-Path $NotesFile)) {
    if ($NewNotes) {
        $tpl = @"
# Sts2CharForge $target

> 一句话说明这一版做了什么。

## 下载哪个

| 你的情况 | 下载这个 |
| --- | --- |
| 第一次用 | ``Sts2CharForge-bundle-$target.zip``（显示为「整合包」，约 xxx MB） |
| 已经有旧版整合包 | ``Sts2CharForge-update-$target.zip``（显示为「更新包」，约 xx MB）—— 解压覆盖到原来的根目录即可 |

## 本次更新

- 

## 说明

- 程序只读取你本机的游戏文件，不附带任何游戏素材；发布包里也不含任何存档。
"@
        [System.IO.File]::WriteAllText($NotesFile, $tpl, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host ("    已生成骨架：$NotesFile") -ForegroundColor Yellow
        Write-Host "    请填好内容后重新运行本脚本（同样的参数即可）。" -ForegroundColor Yellow
        exit 0
    }
    if (-not $NoRelease) {
        Fail "缺少发布说明：$NotesFile`n        先用 -NewNotes 生成骨架，填好再重跑；或加 -NoRelease 只打包。"
    }
    Write-Host "    [警告] 没有发布说明，发布时会用兜底模板" -ForegroundColor Yellow
}

# ---------- 3) 确认 ----------
$plan = @()
$plan += "部署根目录   : $App"
$plan += "输出目录     : $OutDir"
$plan += "版本号       : $current -> $target"
$plan += "步骤         : " + $(if ($SkipBuild) { "跳过发布程序" } else { "dotnet publish 发布程序" }) +
                              $(if ($SkipBundle) { " / 跳过整合包" } else { " / 打整合包" }) +
                              $(if ($SkipUpdate) { " / 跳过更新包" } else { " / 打更新包" }) +
                              $(if ($NoRelease) { " / 不发 GitHub" } else { " / 发 GitHub Release" })
Write-Host ""
$plan | ForEach-Object { Write-Host ("  " + $_) }
if (-not $Yes) {
    Write-Host ""
    $ans = Read-Host "继续吗？(y/N)"
    if ($ans -notmatch '^[Yy]') { Write-Host "已取消。"; exit 0 }
}

# ---------- 4) 写版本号 ----------
if ($target -ne $current) {
    Step "写入新版本号"
    $hasBom = $false
    $b = [System.IO.File]::ReadAllBytes($csproj)
    if ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF) { $hasBom = $true }
    $new = [regex]::Replace($raw, '<InformationalVersion>\s*[^<\s]+\s*</InformationalVersion>', "<InformationalVersion>$target</InformationalVersion>")
    [System.IO.File]::WriteAllText($csproj, $new, (New-Object System.Text.UTF8Encoding($hasBom)))
    Info "已写入：$target（csproj 原有 BOM = $hasBom，保持不变）"
}

# ---------- 5) 发布程序 ----------
if (-not $SkipBuild) {
    Invoke-Step "发布程序（dotnet publish）" $py @{ T3 = $T3; App = $App }
} else { Step "发布程序"; Info "已跳过（-SkipBuild）" }

$exe = Join-Path $App "程序文件\Sts2CharForge.exe"
if (-not (Test-Path $exe)) { Fail "找不到发布出来的程序：$exe" }
$exeVer = (Get-Item $exe).VersionInfo.ProductVersion
Info ("程序版本：" + $exeVer)
if ($exeVer -ne $target) { Fail "程序版本（$exeVer）和版本号（$target）对不上，发布中止" }

# ---------- 6) 两个包 ----------
$bundleZip = Join-Path $OutDir ("Sts2CharForge_整合包_" + $target + ".zip")
$updateZip = Join-Path $OutDir ("Sts2CharForge_更新包_" + $target + ".zip")
if (-not $SkipBundle) { Invoke-Step "打整合包" $mb @{ Root = $App; Out = $bundleZip } } else { Step "打整合包"; Info "已跳过（-SkipBundle）" }
if (-not $SkipUpdate) { Invoke-Step "打更新包" $mu @{ Root = $App; Out = $updateZip } } else { Step "打更新包"; Info "已跳过（-SkipUpdate）" }

# ---------- 7) 发布到 GitHub ----------
if (-not $NoRelease) {
    if (-not (Test-Path $rg)) { Fail "缺少发布脚本：$rg" }
    $rargs = @{ Tag = $target; Root = $OutDir; App = $App }
    if ($Repo)      { $rargs.Repo = $Repo }
    if (Test-Path $NotesFile) { $rargs.NotesFile = $NotesFile }
    if ($Prerelease) { $rargs.Prerelease = $true }
    if ($DryRun)     { $rargs.DryRun = $true }
    # 哪个包没打，就别让发布脚本去找它
    if ($SkipUpdate) { $rargs.OnlyBundle = $true }
    if ($SkipBundle) { $rargs.SkipBundle = $true }
    Invoke-Step "发布到 GitHub Releases" $rg $rargs
} else { Step "发布到 GitHub"; Info "已跳过（-NoRelease）" }

# ---------- 8) 收尾 ----------
Step "结果"
foreach ($f in $bundleZip, $updateZip) {
    if (Test-Path $f) { Info ("[有] " + [System.IO.Path]::GetFileName($f) + "  " + [math]::Round((Get-Item $f).Length / 1MB, 1) + " MB") }
    else { Info ("[无] " + [System.IO.Path]::GetFileName($f)) }
}
Push-Location $T3
$dirty = @(& git status --porcelain)
Pop-Location
if ($dirty.Count -gt 0) {
    Info ("仓库有 " + $dirty.Count + " 个改动未提交")
    if ($Commit) {
        Step "提交并推送"
        Push-Location $T3
        & git add -A
        & git commit -m ("Sts2CharForge " + $target)
        if ($LASTEXITCODE -ne 0) { Pop-Location; Fail "git commit 失败" }
        & git push
        $pushCode = $LASTEXITCODE
        Pop-Location
        if ($pushCode -ne 0) { Fail "git push 失败（检查网络/代理后手动 push）" }
        Info "已提交并推送"
    } else {
        Write-Host "    别忘了提交并推送（GitHub 上才有源码）：" -ForegroundColor Yellow
        Write-Host ("      cd " + $T3) -ForegroundColor Yellow
        Write-Host ("      git add -A; git commit -m `"Sts2CharForge " + $target + "`"; git push") -ForegroundColor Yellow
    }
} else { Info "仓库没有未提交的改动" }

Write-Host ""
Write-Host ("完成：Sts2CharForge " + $target) -ForegroundColor Green
