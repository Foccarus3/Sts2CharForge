# 打包整合包（安全版）
#   · 打包前把根目录「自定义角色存档」（用户存档 + 生成的角色工程）备份到稳定位置，打包后原样放回
#   · zip 里【不含】自定义角色存档：那是用户数据，而且生成的工程里带了 sts2.dll 等游戏文件，不能分发
#   · zip 里【不含】.lnk：快捷方式只能记绝对路径，别人的解压位置不同就会打不开；
#     外层只放「启动 Sts2CharForge_<版本>.bat」（跟着自己位置走，解压到哪都能用）
#   · .bat 里【绝不出现中文】：cmd 按本机 ANSI 代码页读批处理文件，UTF-8 的中文会把后面的行一起读坏
#     （表现为 'orks' is not recognized 这类怪报错）。所以启动器用通配符找装着 exe 的目录，
#     不写死「程序文件」四个字（用 for /d 通配找 exe），所以换台电脑/换解压位置都不会失效。
#   · 版本号只有一个来源：src\Sts2CharForge.App\Sts2CharForge.App.csproj 里的 <InformationalVersion>
#     （启动器名 / 程序包 zip / 整合包 zip / 窗口标题都用它）。每次改动往上加一位。
# 用法： powershell -ExecutionPolicy Bypass -File tools\package_app.ps1
param(
    [string]$T3 = (Split-Path -Parent $PSScriptRoot),
    # 默认发布到工作区里的一个中性目录（不带任何本机路径）；实际用的时候用 -App 显式指定部署目录
    [string]$App = (Join-Path (Split-Path -Parent $PSScriptRoot) "_app_out"),
    # 空 = 按 csproj 里的版本号自动取名（杀戮尖塔2角色生成器<V0.0.1>.zip）
    [string]$ZipName = ""
)

$ErrorActionPreference = "Stop"
function Step($t) { Write-Host "=== $t" }

# 版本号：读 App 的 csproj（唯一来源），读不到就退回 V0.0.1
$Version = "V0.0.1"
$csproj = Join-Path $T3 "src\Sts2CharForge.App\Sts2CharForge.App.csproj"
if (Test-Path $csproj) {
    $m = [regex]::Match((Get-Content $csproj -Raw), '<InformationalVersion>\s*([^<\s]+)\s*</InformationalVersion>')
    if ($m.Success) { $Version = $m.Groups[1].Value }
}
if ([string]::IsNullOrWhiteSpace($ZipName)) { $ZipName = "杀戮尖塔2角色生成器$Version.zip" }
$launcherName = "启动 Sts2CharForge_$Version"
Write-Host "版本号：$Version（启动器 $launcherName.bat，程序包 $ZipName）"

$prog = Join-Path $App "程序文件"
# 用户数据（存档 + 生成的角色工程）放在【根目录】的「自定义角色存档」里，跟程序文件分开：
# 覆盖更新程序时只动「程序文件」，不会碰到用户数据。
$userData = Join-Path $App "自定义角色存档"
$legacyUserData = Join-Path $prog "自定义角色"   # 老版本的位置（打包时顺手搬过去）

# 1) 备份用户数据（绝对不能丢：存档 + 生成的角色工程）
$keepRoot = Join-Path $T3 "_packaging_keep"
$keep = Join-Path $keepRoot "自定义角色存档"
if (Test-Path $keepRoot) { Remove-Item $keepRoot -Recurse -Force }
if (Test-Path $userData) {
    New-Item -ItemType Directory -Force $keepRoot | Out-Null
    Copy-Item $userData $keep -Recurse -Force
    $n = (Get-ChildItem $keep -Recurse -File -ErrorAction SilentlyContinue).Count
    Step "已备份用户数据：$n 个文件 -> $keep"
} else {
    Step "没有用户数据需要备份"
}

# 2) 打包版不内嵌效果库数据（发布版不含游戏文本）
$cat = Join-Path $T3 "src\Sts2CharForge.Core\Data\powers_catalog.json"
$aside = "$cat.packaging-aside"
if (Test-Path $cat) { Move-Item $cat $aside -Force }

Get-Process -Name 'Sts2CharForge' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Remove-Item $prog -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $prog | Out-Null

Step "发布自包含版（Release / win-x64）"
dotnet publish (Join-Path $T3 "src\Sts2CharForge.App\Sts2CharForge.App.csproj") `
    -c Release -r win-x64 --self-contained true -p:DebugType=none `
    -o $prog -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（exit $LASTEXITCODE）" }

if (Test-Path $aside) { Move-Item $aside $cat -Force }

# 3) 放回用户数据（并兼容老版本：程序文件里如果还有「自定义角色」，一起搬到根目录）
if (Test-Path $legacyUserData) {
    New-Item -ItemType Directory -Force $userData | Out-Null
    foreach ($item in Get-ChildItem $legacyUserData -Force) {
        $dst = Join-Path $userData $item.Name
        if (Test-Path $dst) { continue }        # 重名的不动，宁可不搬也不覆盖
        Move-Item $item.FullName $dst -Force
    }
    if (-not (Get-ChildItem $legacyUserData -Force -ErrorAction SilentlyContinue)) {
        Remove-Item $legacyUserData -Recurse -Force -ErrorAction SilentlyContinue
        Step "老位置的「自定义角色」已搬进根目录「自定义角色存档」并删除"
    } else {
        Step "老位置的「自定义角色」有重名项没搬，保留原样"
    }
}
if (Test-Path $keep) {
    # 注意：不能写 Copy-Item $keep $userData —— 目标目录已存在时会把整个「自定义角色存档」
    # 再套一层复制进去（实测踩过：用户数据从 400 个文件变成 800 个，多出一层同名子目录）。
    # 也不能逐个 Copy-Item 子项：目标子目录已存在时同样会套一层（SevenMod\SevenMod\…）。
    # 用通配路径复制「里面的内容」，而且已有文件不覆盖，才不会套娃。
    New-Item -ItemType Directory -Force $userData | Out-Null
    foreach ($item in Get-ChildItem $keep -Force) {
        $dst = Join-Path $userData $item.Name
        if ($item.PSIsContainer) {
            New-Item -ItemType Directory -Force $dst | Out-Null
            Copy-Item -Path (Join-Path $item.FullName '*') -Destination $dst -Recurse -Force -ErrorAction SilentlyContinue
        } elseif (-not (Test-Path $dst)) {
            Copy-Item $item.FullName $dst -Force
        }
    }
    # 把老版本 bug 留下的套娃目录删掉（X\X 这种）：里层更全就把里层内容提上来，再删里层
    #
    # 注意（踩过，很危险）：生成的模组工程里本来就有一个和工程同名的目录 ——
    # 模组的本地化表放在 <工程>\<ModId>\localization\<语言>\*.json（游戏按 res://<ModId>/localization/ 读它）。
    # 存档名和 ModId 一样时（很常见），这个正常目录长得和「套娃」一模一样，
    # 结果被这里当成套娃删掉 → PCK 里少了 ModId 那一层 → 游戏里名字/描述全没了。
    # 所以：里层带 localization（= 模组本体）的一律不碰，只清真正的重复目录。
    foreach ($sub in Get-ChildItem $userData -Directory -Force -ErrorAction SilentlyContinue) {
        $dup = Join-Path $sub.FullName $sub.Name
        if (-not (Test-Path $dup)) { continue }
        if (Test-Path (Join-Path $dup 'localization')) {
            Step "保留 $($sub.Name)\$($sub.Name)：它是模组的本地化目录（游戏按 res://<ModId>/localization/ 读），不是套娃"
            continue
        }
        $outer = (Get-ChildItem $sub.FullName -Recurse -File -ErrorAction SilentlyContinue |
                  Where-Object { -not $_.FullName.StartsWith($dup) }).Count
        $inner = (Get-ChildItem $dup -Recurse -File -ErrorAction SilentlyContinue).Count
        if ($inner -gt $outer) {
            Copy-Item -Path (Join-Path $dup '*') -Destination $sub.FullName -Recurse -Force -ErrorAction SilentlyContinue
            Step "套娃目录 $($sub.Name)\$($sub.Name) 里层更全（$inner > $outer），已把内容提上来"
        }
        Remove-Item $dup -Recurse -Force -ErrorAction SilentlyContinue
        Step "已清掉套娃目录：$($sub.Name)\$($sub.Name)"
    }
    $n = (Get-ChildItem $userData -Recurse -File -ErrorAction SilentlyContinue).Count
    Step "已放回用户数据：$n 个文件"
}

# 4) 外层：可移植启动器 + 文档
Step "写启动器与文档"

# 启动器正文放在 tools\ 下的模板里：
#   · 启动器 .bat 必须**纯 ASCII**（cmd 按当前代码页读批处理，任何非 ASCII 字节都可能在
#     别的机器上把整行读坏 —— UTF-8 beta 或非中文区域尤其明显，实测踩过）
#   · 中文提示放在 launcher_help.ps1（UTF-8 带 BOM），由 .bat 在失败时调用
$launchTemplate = Join-Path $T3 "tools\launcher.bat.template"
if (-not (Test-Path $launchTemplate)) { throw "缺少启动器模板：$launchTemplate" }
$launch = [System.IO.File]::ReadAllText($launchTemplate, [System.Text.Encoding]::UTF8)
if ($launch -match '[^\x00-\x7F]') { throw "启动器模板里出现了非 ASCII 字符，会导致 cmd 解析错乱：$launchTemplate" }
# 启动器文件名带版本号：启动 Sts2CharForge_V0.0.1.bat（版本号来自 csproj，见文件开头）
# 旧的不带版本号的启动器（.bat / .lnk）顺手删掉，免得一个目录里躺着好几份、用户点错
foreach ($stale in Get-ChildItem $App -File -Force -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -like '启动 Sts2CharForge*' -and $_.Extension -in @('.bat', '.lnk') -and $_.BaseName -ne $launcherName }) {
    Remove-Item $stale.FullName -Force -ErrorAction SilentlyContinue
    Write-Host "  已删除旧启动器 $($stale.Name)"
}
[System.IO.File]::WriteAllText((Join-Path $App "$launcherName.bat"), $launch, (New-Object System.Text.UTF8Encoding($false)))

$helpTemplate = Join-Path $T3 "tools\launcher_help.ps1.template"
if (-not (Test-Path $helpTemplate)) { throw "缺少启动失败说明模板：$helpTemplate" }
$help = [System.IO.File]::ReadAllText($helpTemplate, [System.Text.Encoding]::UTF8)
# 中文提示脚本放进「程序文件」（跟程序放一起），根目录只留启动器 .bat：
# 启动器会先找「程序文件\launcher_help.ps1」，找不到再退回根目录那份（兼容老布局），
# 两个都没有时 .bat 自己用纯 ASCII 提示兜底（那种情况一般是「程序文件」整个没解压出来）。
[System.IO.File]::WriteAllText((Join-Path $prog "launcher_help.ps1"), $help, (New-Object System.Text.UTF8Encoding($true)))
# 根目录如果留着老副本就删掉（避免两份不同步）
$oldHelp = Join-Path $App "launcher_help.ps1"
if (Test-Path $oldHelp) { Remove-Item $oldHelp -Force; Write-Host "  已删除根目录里的旧 launcher_help.ps1（已移入程序文件）" }

# 图标：程序用的是项目里的 waku.ico（已嵌进 exe），「程序文件」里再放一份给快捷方式用
$iconSrc = Join-Path $T3 "src\Sts2CharForge.App\waku.ico"
$iconDst = Join-Path $prog "waku.ico"
if (Test-Path $iconSrc) { Copy-Item $iconSrc $iconDst -Force } else { Write-Host "  [警告] 缺少图标 $iconSrc" }
# 根目录的老图标副本清掉（已移入程序文件）
$oldIcon = Join-Path $App "waku.ico"
if (Test-Path $oldIcon) { Remove-Item $oldIcon -Force; Write-Host "  已删除根目录里的旧 waku.ico（已移入程序文件）" }
# 旧图标清掉
foreach ($old in @("favicon.ico")) {
    $p = Join-Path $App $old
    if (Test-Path $p) { Remove-Item $p -Force; Write-Host "  已删除旧图标 $old" }
}
foreach ($old in @("创建桌面快捷方式.bat")) {
    $p = Join-Path $App $old
    if (Test-Path $p) { Remove-Item $p -Force; Write-Host "  已删除 $old" }
}
foreach ($old in @("make_shortcut.ps1")) {
    $p = Join-Path $prog $old
    if (Test-Path $p) { Remove-Item $p -Force }
}

Copy-Item (Join-Path $T3 "README.md") (Join-Path $App "README.md") -Force

# 使用说明正文放在模板文件里（和 launcher_help.ps1.template 一个套路）：
#   · 好处：正文里可以有任意内容，不和 PowerShell 的 here-string 打架；改文案也不用碰脚本
#   · 模板里写的启动器名字是「启动 Sts2CharForge.bat」，这里按版本号替换一次
$readmeTemplate = Join-Path $T3 "tools\使用说明.template.txt"
if (-not (Test-Path $readmeTemplate)) { throw "缺少使用说明模板：$readmeTemplate" }
$readme = [System.IO.File]::ReadAllText($readmeTemplate, [System.Text.Encoding]::UTF8)
$readme = $readme.Replace("启动 Sts2CharForge.bat", "$launcherName.bat")
[System.IO.File]::WriteAllText((Join-Path $App "使用说明.txt"), $readme, (New-Object System.Text.UTF8Encoding($true)))

# 本机自用的真实快捷方式（不进 zip）：名字也带版本号
foreach ($stale in Get-ChildItem $App -File -Force -ErrorAction SilentlyContinue |
                  Where-Object { $_.Extension -eq '.lnk' -and $_.Name -like '启动 Sts2CharForge*' -and $_.BaseName -ne $launcherName }) {
    Remove-Item $stale.FullName -Force -ErrorAction SilentlyContinue
}
try {
    $ws = New-Object -ComObject WScript.Shell
    $lnk = $ws.CreateShortcut((Join-Path $App "$launcherName.lnk"))
    $lnk.TargetPath = (Join-Path $prog "Sts2CharForge.exe")
    $lnk.WorkingDirectory = $prog
    $lnk.IconLocation = (Join-Path $prog "waku.ico") + ",0"
    $lnk.Description = "Sts2CharForge $Version —— 杀戮尖塔2 自定义角色生成器"
    $lnk.Save()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($ws) | Out-Null
} catch { Write-Host "（本机快捷方式生成失败，忽略：$($_.Exception.Message)）" }

# 4.5) 清掉自检/日志留下的垃圾文件（这些是本地跑测试产生的，不该进发布包）
Step "清理自检产物"
$junk = Get-ChildItem $prog -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(uicheck|envcheck|buildtest|newprofiletest|movedtest)_result\.txt$|^crash_log\.txt$|\.log$' }
foreach ($j in $junk) { Remove-Item $j.FullName -Force -ErrorAction SilentlyContinue }
Write-Host ("  清掉 {0} 个（自检结果 / 崩溃日志）" -f $junk.Count)

# 5) 打 zip：跳过用户数据、.lnk、所有 zip 和日志类文件
#    （踩过的坑：目录里如果留着上一次的 zip，会被一起打进新 zip，体积直接翻倍）
Step "打 zip（排除 自定义角色存档 / *.lnk / *.zip / 日志）"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tmpZip = Join-Path $env:TEMP $ZipName
Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue

# 覆盖已有的同名 zip 之前，先备份一份到 _packaging_keep
$finalZipEarly = Join-Path $App $ZipName
if (Test-Path $finalZipEarly) {
    New-Item -ItemType Directory -Force $keepRoot | Out-Null
    Move-Item $finalZipEarly (Join-Path $keepRoot ($ZipName + ".旧")) -Force
    Step "已把旧的同名 zip 备份到 $keepRoot"
}

$skip = @(
    ($userData + "\")
)
$zip = [System.IO.Compression.ZipFile]::Open($tmpZip, 'Create')
$count = 0; $skipped = 0
try {
    foreach ($f in Get-ChildItem $App -Recurse -File -Force) {
        $bad = $false
        foreach ($s in $skip) { if ($f.FullName.StartsWith($s, [StringComparison]::OrdinalIgnoreCase)) { $bad = $true } }
        if ($f.Extension -eq ".lnk") { $bad = $true }
        # .zip 一律不进包（免得把上一次的整合包再打一遍），**但**「！首次使用先点这个！」里的教程/工具 zip 要带
        # （用户要求：GDRE 工具那个 GDRE_tools-*.zip 必须跟着包发出去）
        if ($f.Extension -eq ".zip" -and -not $f.FullName.StartsWith((Join-Path $App "！首次使用先点这个！"), [StringComparison]::OrdinalIgnoreCase)) { $bad = $true }
        if ($f.Name -match '^(uicheck|envcheck|buildtest|newprofiletest|movedtest)_result\.txt$|^crash_log\.txt$|\.log$') { $bad = $true }
        if ($bad) { $skipped++; continue }
        $rel = $f.FullName.Substring($App.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $count++
    }
} finally { $zip.Dispose() }

$finalZip = Join-Path $App $ZipName
Remove-Item $finalZip -Force -ErrorAction SilentlyContinue  # 生成物已在上一步备份，这里直接覆盖
Move-Item $tmpZip $finalZip -Force

Write-Host ("完成：{0}  （{1:N1} MB，{2} 个文件，跳过 {3} 个）" -f $finalZip, ((Get-Item $finalZip).Length / 1MB), $count, $skipped)