# 按规范把「整合包 + 更新包」发到 GitHub Releases（一条命令）
#
# 规范（和 README「发布到 GitHub Releases」+ docs\发布规范.md 一致）：
#   · tag        = 程序版本号（V0.0.9，唯一来源：src\Sts2CharForge.App\Sts2CharForge.App.csproj 的 InformationalVersion）
#   · Release 标题 = "Sts2CharForge V0.0.9"
#   · 两个资产（文件名保持原样，方便玩家按名字认）：
#        Sts2CharForge_整合包_V0.0.9.zip   全新安装：程序 + 环境包（Godot / 便携 dotnet）+ GDRE 工具 + 教程
#        Sts2CharForge_更新包_V0.0.9.zip   只有程序文件 + 启动器（几十 MB，覆盖更新用）
#   · Release 正文 = docs\RELEASE_NOTES_<版本>.md（没有就用一句模板兜底）
#   · 发布前自检：两个 zip 都在 / 程序版本对得上 / 整合包里没有存档与游戏文件 / 更新包只有程序文件
#
# 认证（二选一）：
#   ① 装了 GitHub CLI 并 `gh auth login` 过 → 本脚本直接用 gh（最省事）
#   ② 给一个 token：`-Token ghp_xxx` 或环境变量 GITHUB_TOKEN（需要 contents:write 权限）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tools\release_github.ps1 -Repo 你的用户名/仓库名
#   powershell -ExecutionPolicy Bypass -File tools\release_github.ps1 -Repo 你的用户名/仓库名 -Token ghp_xxx
#   powershell -ExecutionPolicy Bypass -File tools\release_github.ps1 -Repo 你的用户名/仓库名 -DryRun   # 只自检、不发
#
# 已经发过同一个 tag 时：不是报错，而是把两个资产的同名文件覆盖掉（gh --clobber / API 先删后传）。
param(
    # 仓库，形如 owner/name。留空时依次尝试：git remote origin → docs\release_repo.txt
    [string]$Repo = "",
    # 留空则用环境变量 GITHUB_TOKEN
    [string]$Token = "",
    # 留空则用 csproj 里的版本号（V0.0.9）
    [string]$Tag = "",
    # 两个 zip 所在目录
    [string]$Root = "D:\ds\s",
    # 部署根目录（用来核对程序版本 / 打包内容）
    [string]$App = "D:\ds\s\4",
    [string]$NotesFile = "",
    [switch]$Prerelease,
    # 只发小包（59.8 MB）：网络慢时先让 Release 可用，370 MB 的整合包以后再补
    [switch]$SkipBundle,
    # 只发整合包
    [switch]$OnlyBundle,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$T3 = Split-Path -Parent $PSScriptRoot

function Step($t) { Write-Host ("=== " + $t) }
function Fail($t) { Write-Host ("[失败] " + $t) -ForegroundColor Red; throw $t }

# ---------- 1) 版本号 & 文件 ----------
$Version = "V0.0.1"
$csproj = Join-Path $T3 "src\Sts2CharForge.App\Sts2CharForge.App.csproj"
if (Test-Path $csproj) {
    $m = [regex]::Match((Get-Content $csproj -Raw), '<InformationalVersion>\s*([^<\s]+)\s*</InformationalVersion>')
    if ($m.Success) { $Version = $m.Groups[1].Value }
}
if ([string]::IsNullOrWhiteSpace($Tag)) { $Tag = $Version }

$bundle = Join-Path $Root "Sts2CharForge_整合包_$Version.zip"
$update = Join-Path $Root "Sts2CharForge_更新包_$Version.zip"
Write-Host "版本号：$Version    tag：$Tag"

# ---------- 2) 发布前自检 ----------
Step "发布前自检"
foreach ($f in @($bundle, $update)) {
    if (-not (Test-Path $f)) { Fail "找不到打包产物：$f（先跑 tools\make_bundle.ps1 / tools\make_update.ps1）" }
}
$bundleMB = [math]::Round((Get-Item $bundle).Length / 1MB, 1)
$updateMB = [math]::Round((Get-Item $update).Length / 1MB, 1)
Write-Host ("  整合包 {0} MB / 更新包 {1} MB" -f $bundleMB, $updateMB)

$exe = Join-Path (Join-Path $App "程序文件") "Sts2CharForge.exe"
if (Test-Path $exe) {
    $exeVer = (Get-Item $exe).VersionInfo.ProductVersion
    if ($exeVer -ne $Version) { Fail "程序版本是 $exeVer，和 zip 的版本 $Version 对不上（先重新打包）" }
    Write-Host "  程序版本对得上：$exeVer"
} else {
    Write-Host "  [提示] 没找到 $exe，跳过程序版本核对"
}

$zipB = [System.IO.Compression.ZipFile]::OpenRead($bundle)
try {
    $names = $zipB.Entries | ForEach-Object { $_.FullName }
    if ($names -match '^自定义角色存档/' ) { Fail "整合包里出现了用户存档（不该发出去）" }
    if ($names -match 'sts2\.dll$' -or $names -match '0Harmony\.dll$') { Fail "整合包里出现了游戏文件（不能分发）" }
    if (-not ($names -contains '程序文件/Sts2CharForge.exe')) { Fail "整合包里没有 程序文件/Sts2CharForge.exe" }
    if (-not ($names | Where-Object { $_ -like '！首次使用先点这个！/GDRE/*.zip' })) { Fail "整合包里没带 GDRE 解包工具 zip（玩家解包本体要用）" }
    if (-not ($names -contains '使用说明.txt')) { Fail "整合包里没有 使用说明.txt" }
    Write-Host ("  整合包自检通过：{0} 个条目（含 GDRE 工具 + 使用说明，无存档 / 无游戏文件）" -f $names.Count)
} finally { $zipB.Dispose() }

$zipU = [System.IO.Compression.ZipFile]::OpenRead($update)
try {
    $unames = $zipU.Entries | ForEach-Object { $_.FullName }
    $stray = $unames | Where-Object { $_ -notlike '程序文件/*' -and $_ -ne "启动 Sts2CharForge_$Version.bat" }
    if ($stray) { Fail ("更新包里出现了不该有的条目：" + ($stray -join '、')) }
    if ($unames -match 'sts2\.dll$') { Fail "更新包里出现了游戏文件" }
    Write-Host ("  更新包自检通过：{0} 个条目（只有 程序文件\ + 启动器）" -f $unames.Count)
} finally { $zipU.Dispose() }

# ---------- 3) 仓库 & 认证 ----------
if ([string]::IsNullOrWhiteSpace($Repo)) {
    try {
        $url = (git -C $T3 config --get remote.origin.url) 2>$null
        if ($url -match 'github\.com[:/]+([^/]+)/([^/\.]+)') { $Repo = "$($Matches[1])/$($Matches[2])" }
    } catch { }
}
if ([string]::IsNullOrWhiteSpace($Repo)) {
    $cfg = Join-Path $T3 "docs\release_repo.txt"
    if (Test-Path $cfg) { $Repo = (Get-Content $cfg -Raw).Trim() }
}
if ([string]::IsNullOrWhiteSpace($Repo) -and -not $DryRun) {
    Fail "不知道该发到哪个仓库：加 -Repo owner/name，或在 docs\release_repo.txt 里写一行，或先 git remote add origin …"
}
if ([string]::IsNullOrWhiteSpace($Repo)) { $Repo = "（DryRun：未指定）" }

# 找 gh：先看 PATH；找不到就退到常见安装路径（winget 装完后，**当前这个窗口**的 PATH 还没刷新）
$ghExe = $null
$ghCmd = Get-Command gh -ErrorAction SilentlyContinue
if ($ghCmd) { $ghExe = $ghCmd.Source }
if (-not $ghExe) {
    foreach ($cand in @("$env:ProgramFiles\GitHub CLI\gh.exe", "${env:ProgramFiles(x86)}\GitHub CLI\gh.exe", "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe")) {
        if ($cand -and (Test-Path $cand)) { $ghExe = $cand; Write-Host "  （PATH 里没有 gh，用绝对路径：$cand）"; break }
    }
}
# 环境变量里没有代理，但 Windows「系统代理」开着 → 自动用上它。
# 为什么要这一步：gh / curl 只认 HTTPS_PROXY 这类环境变量，**不读** Windows 的「系统代理」设置；
# 忘了设就会去直连 api.github.com，然后**卡死在等连接**上（表现为「没有任何输出、CPU 不动」，实测踩过）。
if ([string]::IsNullOrWhiteSpace($env:HTTPS_PROXY) -and [string]::IsNullOrWhiteSpace($env:https_proxy)) {
    try {
        $ie = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -ErrorAction Stop
        if ($ie.ProxyEnable -eq 1 -and $ie.ProxyServer) {
            $proxy = [string]$ie.ProxyServer
            if ($proxy -notmatch '^[a-zA-Z]+://') { $proxy = 'http://' + $proxy }
            $env:HTTP_PROXY = $proxy;  $env:http_proxy = $proxy
            $env:HTTPS_PROXY = $proxy; $env:https_proxy = $proxy
            Write-Host "  检测到系统代理 $($ie.ProxyServer) → 已自动设进环境变量（gh / curl 不读系统代理，只认环境变量）"
        }
    } catch { }
}
if ([string]::IsNullOrWhiteSpace($Token)) { $Token = $env:GITHUB_TOKEN }

# 本机 token 文件（不想每次输 token 就用它；已在 .gitignore 里，不会被提交）
if ([string]::IsNullOrWhiteSpace($Token)) {
    $tokenCfg = Join-Path $T3 "docs\release_token.txt"
    if (Test-Path $tokenCfg) {
        $line = (Get-Content $tokenCfg | Where-Object { $_ -and $_.Trim() -ne "" -and -not $_.TrimStart().StartsWith("#") } | Select-Object -First 1)
        if ($line) { $Token = $line.Trim(); Write-Host "  已读取 docs\release_token.txt 里的 token" }
    }
}
# 调用 gh 的安全包装：临时把 ErrorActionPreference 放宽 + 合并 stderr，
# 否则「release not found」这种**预期内的**输出会被 $ErrorActionPreference="Stop" 当成致命错误中断脚本
# （PS 5.1 的 NativeCommandError 坑，实测踩过）。
function Invoke-Gh {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GhArgs)
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    # stderr 直接写进临时文件：这样 PS 5.1 不会把它包成 NativeCommandError（连 ErrorActionPreference=Stop 也不会中断）
    $errFile = [IO.Path]::GetTempFileName()
    try {
        $out = & $ghExe @GhArgs 2>$errFile
        $code = $LASTEXITCODE
        $errText = ''
        if (Test-Path $errFile) { $errText = (Get-Content $errFile -Raw) }
        $text = ((($out | Out-String) + "`n" + $errText)).Trim()
        # 去掉 PowerShell 给 native stderr 加的装饰行，只留 gh 自己的话（失败信息才好读）
        $text = (($text -split "`n") | Where-Object { $_ -notmatch '^\s*(At |\+ |    \+ CategoryInfo|    \+ FullyQualifiedErrorId|    \+ ~~~~)' }) -join "`n"
        $text = $text.Trim()
        return [pscustomobject]@{ Code = $code; Output = $text }
    } finally {
        Remove-Item $errFile -Force -ErrorAction SilentlyContinue
        $ErrorActionPreference = $old
    }
}
$useGh = ($null -ne $ghExe) -and [string]::IsNullOrWhiteSpace($Token)
Write-Host ("  仓库：{0}    认证方式：{1}" -f $Repo, $(if ($useGh) { "GitHub CLI（gh）" } elseif ($Token) { "token" } else { "（DryRun：都没有，只自检）" }))

# ---------- 4) Release 正文 ----------
if ([string]::IsNullOrWhiteSpace($NotesFile)) {
    $cand = Join-Path $T3 "docs\RELEASE_NOTES_$Version.md"
    if (Test-Path $cand) { $NotesFile = $cand }
}
$notesText = ""
if ($NotesFile -and (Test-Path $NotesFile)) {
    $notesText = [IO.File]::ReadAllText((Resolve-Path $NotesFile), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("  发布说明：{0}（{1} 字）" -f (Split-Path $NotesFile -Leaf), $notesText.Length)
} else {
    $notesText = "Sts2CharForge $Version`n`n下载说明：`n- 全新安装：Sts2CharForge_整合包_$Version.zip`n- 已有旧版：下载 Sts2CharForge_更新包_$Version.zip，把 程序文件\ 覆盖到原来的根目录即可"
    Write-Host "  [提示] 没找到 docs\RELEASE_NOTES_$Version.md，用模板兜底"
}
$title = "Sts2CharForge $Version"
# ===== 资产命名规范（关于 GitHub 的一个硬限制）=====
# GitHub 会把上传资产名里的**非 ASCII 字符清洗成 "._"**（实测：Sts2CharForge_更新包_… → Sts2CharForge_._…），
# 而且「整合包 / 更新包」清洗后是同一个名字，会互相撞名。
# 所以：本地 zip 用中文名（给玩家看），GitHub 资产用 ASCII 名 + 中文 label（label 是自由文本，不会被清洗）。
$assetList = @(
    @{ Path = $update; Name = "Sts2CharForge-update-$Version.zip"; Label = "更新包（覆盖更新：只含程序文件 + 启动器）" },
    @{ Path = $bundle; Name = "Sts2CharForge-bundle-$Version.zip"; Label = "整合包（全新安装：程序 + 环境包 + GDRE 工具 + 教程）" }
)
$toUpload = @()
if (-not $OnlyBundle) { $toUpload += $assetList[0] }
if (-not $SkipBundle) { $toUpload += $assetList[1] }
if ($toUpload.Count -eq 0) { Fail "两个包都被 -SkipBundle / -OnlyBundle 排除了，没东西可发" }

# label / 任何中文都要放进 JSON 里发给 GitHub：转成 \uXXXX 纯 ASCII 最保险（各种编码坑都绕开）
function ConvertTo-AsciiJsonEscaped([string]$s) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        $code = [int]$ch
        if ($code -lt 32 -or $code -gt 126) { [void]$sb.AppendFormat('\u{0:x4}', $code) } else { [void]$sb.Append($ch) }
    }
    return $sb.ToString()
}

# 用 gh api 把资产名旁边那个中文 label 设上（失败只警告，不影响发布）
function Set-GhAssetLabel([string]$AssetName, [string]$Label) {
    try {
        $raw = (Invoke-Gh api "repos/$Repo/releases/tags/$Tag").Output
        $id = ''
        try {
            $relObj = $raw | ConvertFrom-Json
            $hit = $relObj.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
            if ($hit) { $id = [string]$hit.id }
        } catch { }
        if ($id -notmatch '^\d+$') { Write-Host ("  [提示] 没找到资产 $AssetName 的 id，跳过 label") -ForegroundColor Yellow; return }
        $json = '{"label":"' + (ConvertTo-AsciiJsonEscaped $Label) + '"}'
        $jf = Join-Path $env:TEMP "forge_asset_label_$id.json"
        [IO.File]::WriteAllText($jf, $json, (New-Object System.Text.UTF8Encoding($false)))
        $r = Invoke-Gh api -X PATCH "repos/$Repo/releases/assets/$id" --input $jf
        Remove-Item $jf -Force -ErrorAction SilentlyContinue
        if ($r.Code -ne 0) { Write-Host ("  [警告] 设置 label 失败：" + $r.Output) -ForegroundColor Yellow }
    } catch { }
}

# gh 在 Windows 上会把非 ASCII 文件名传坏 → 用纯 ASCII 名做**硬链接**再上传（同盘不占额外空间）
function New-AsciiHardLink([string]$Src, [string]$AsciiName) {
    $dst = Join-Path $env:TEMP $AsciiName
    Remove-Item $dst -Force -ErrorAction SilentlyContinue
    try { New-Item -ItemType HardLink -Path $dst -Target $Src | Out-Null; return $dst }
    catch { Copy-Item $Src $dst -Force; return $dst }
}

if ($DryRun) {
    Step "DryRun：只自检，不发布"
    Write-Host ("  会发 tag={0}  标题={1}" -f $Tag, $title)
    foreach ($a in $toUpload) { Write-Host ("    资产：" + $a.Name + "   ← 本地文件 " + (Split-Path $a.Path -Leaf)) }
    return
}
# ---------- 5) 发布 ----------
$bodyFile = Join-Path $env:TEMP "forge_release_body_$Version.md"
[IO.File]::WriteAllText($bodyFile, $notesText, (New-Object System.Text.UTF8Encoding($false)))

if ($useGh) {
    Step "用 gh 发布"
    Write-Host "  提示：gh 上传附件**不显示进度**（正常现象），大文件走代理要好几分钟，别中途 Ctrl+C。" -ForegroundColor Yellow

    $probe = Invoke-Gh release view $Tag -R $Repo --json tagName
    if ($probe.Code -eq 0) {
        Write-Host "  tag $Tag 已存在 → 覆盖资产 + 更新说明"
    } else {
        Write-Host "  tag 不存在 → 建新的 Release（自动创建 tag）"
        $ghArgs = @('release', 'create', $Tag, '-R', $Repo, '--title', $title, '--notes-file', $bodyFile)
        if ($Prerelease) { $ghArgs += '--prerelease' }
        $r3 = Invoke-Gh @ghArgs
        if ($r3.Code -ne 0) { Fail "gh release create 失败（exit $($r3.Code)）：" + $r3.Output }
    }

    # 写说明 + 取消草稿：gh 建 Release 时若带附件，会先建**草稿**、传完再自动发布；
    # 中途失败就会留下一个「没有附件的草稿」（实测踩过），所以这里显式把说明写上并取消草稿。
    $r2 = Invoke-Gh release edit $Tag -R $Repo --title $title --notes-file $bodyFile --draft=false
    if ($r2.Code -ne 0) { Write-Host ("  [警告] 更新说明 / 取消草稿失败：" + $r2.Output) -ForegroundColor Yellow }

    # 一个一个传（先小后大）：失败时能立刻看出是哪个文件，也不会因为大文件挂掉而丢掉小文件
    $failed = $null
    foreach ($a in $toUpload) {
        # GitHub 会把非 ASCII 资产名清洗成 "._"，所以先做纯 ASCII 硬链接再传，中文放进 label
        $asciiPath = New-AsciiHardLink $a.Path $a.Name
        Write-Host ("  上传 " + $a.Name + " …（{0:N1} MB，没有进度条，耐心等）" -f ((Get-Item $asciiPath).Length / 1MB)) -ForegroundColor Yellow
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $r = Invoke-Gh release upload $Tag $asciiPath -R $Repo --clobber
        $sw.Stop()
        Remove-Item $asciiPath -Force -ErrorAction SilentlyContinue
        if ($r.Code -ne 0) {
            $msg = $r.Output
            if ([string]::IsNullOrWhiteSpace($msg)) { $msg = "（gh 没输出任何信息 → 多半是进程被中断 / 网络断了）" }
            $failed = "$($a.Name) 上传失败（exit $($r.Code)）：$msg"
            break
        }
        Write-Host ("  ✓ " + $a.Name + " 传完（{0:N0} 秒）" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
        Set-GhAssetLabel $a.Name $a.Label
    }
    if ($failed) {
        Write-Host ("  [失败] " + $failed) -ForegroundColor Red
        Write-Host "  接下来：① 直接重跑本脚本（release 已存在 → 走覆盖分支，不会重复）；" -ForegroundColor Yellow
        Write-Host "          ② 小文件用 gh 传、大文件用浏览器拖到 Release 页面（浏览器有进度条，最稳）。" -ForegroundColor Yellow
        throw $failed
    }

    $r4 = Invoke-Gh release view $Tag -R $Repo --json url -q .url
    $finalUrl = if ($r4.Code -eq 0) { $r4.Output } else { "https://github.com/$Repo/releases/tag/$Tag" }
    Write-Host ("完成：{0}" -f $finalUrl) -ForegroundColor Green
    return
}

if ([string]::IsNullOrWhiteSpace($Token)) { Fail "既没有 gh，也没有 token：装 GitHub CLI 并 gh auth login，或给 -Token / 设 GITHUB_TOKEN" }

Step "用 GitHub API 发布（curl 上传，支持大文件）"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
# 代理环境里 Schannel 常常连不上吊销服务器（CRYPT_E_REVOCATION_OFFLINE），关掉这项检查即可
try { [Net.ServicePointManager]::CheckCertificateRevocationList = $false } catch { }
$headers = @{
    Authorization = "Bearer $Token"
    Accept        = "application/vnd.github+json"
    'User-Agent'  = "Sts2CharForge-release-script"
}
$api = "https://api.github.com/repos/$Repo"

# 找一个辅助函数：PS 5.1 的 Invoke-RestMethod 把 -Body 字符串按本地编码发出去，
# 正文里有中文时 GitHub 会回 400「Problems parsing JSON」——所以统一转成 UTF-8 字节再发。
function Invoke-GitHubJson {
    param([string]$Uri, [string]$Method, [string]$Json)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Json)
    return Invoke-RestMethod -Uri $Uri -Headers $headers -Method $Method -Body $bytes -ContentType 'application/json; charset=utf-8' -TimeoutSec 60
}

# 已存在就先拿到 release id（后面覆盖资产要用）；只有**真的 404** 才去新建，
# 其它错误（401 / 代理 / TLS）要原地报出来，别偷偷去建新 release（会被 400 掩盖真实原因）
$releaseId = $null
$existing = $null
try {
    $existing = Invoke-RestMethod -Uri "$api/releases/tags/$Tag" -Headers $headers -Method Get -TimeoutSec 30
} catch {
    $code = $null
    try { $code = [int]$_.Exception.Response.StatusCode } catch { }
    if ($code -ne 404) { Fail ("查 Release 失败（HTTP $code）：" + $_.Exception.Message) }
}
if ($existing) {
    $releaseId = $existing.id
    Write-Host "  tag $Tag 已存在（id=$releaseId）→ 覆盖资产 + 更新说明"
    $payload = @{ name = $title; body = $notesText; prerelease = [bool]$Prerelease; draft = $false } | ConvertTo-Json -Depth 4
    Invoke-GitHubJson -Uri "$api/releases/$releaseId" -Method Patch -Json $payload | Out-Null
} else {
    Write-Host "  建新 release：$Tag"
    $payload = @{ tag_name = $Tag; name = $title; body = $notesText; draft = $false; prerelease = [bool]$Prerelease } | ConvertTo-Json -Depth 4
    $existing = Invoke-GitHubJson -Uri "$api/releases" -Method Post -Json $payload
    $releaseId = $existing.id
}

$uploadBase = "https://uploads.github.com/repos/$Repo/releases/$releaseId/assets"
$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
foreach ($a in $toUpload) {
    $name = $a.Name            # ASCII 名（GitHub 会清洗非 ASCII，所以资产名用英文，中文放 label）
    $file = $a.Path            # 本地还是中文名的 zip
    # 同名资产先删重（GitHub 不允许同名；-clobber 是 gh 的玩法，API 这边得自己删）
    $old = Invoke-RestMethod -Uri "$api/releases/$releaseId/assets?per_page=100" -Headers $headers -Method Get -TimeoutSec 30
    foreach ($x in @($old)) {
        if ($x.name -eq $name) {
            Write-Host ("  删除旧资产：" + $name)
            Invoke-RestMethod -Uri "$api/releases/assets/$($x.id)" -Headers $headers -Method Delete -TimeoutSec 30 | Out-Null
        }
    }
    $mb = (Get-Item $file).Length / 1MB
    Write-Host ("  上传 " + $name + " …（{0:N1} MB）" -f $mb) -ForegroundColor Yellow
    $sw = [Diagnostics.Stopwatch]::StartNew()
    if ($curl) {
        # curl 流式上传：--http1.1 避开代理对 HTTP/2 大流量的兼容问题；-H "Expect:" 关掉 100-continue（大 POST 经代理卡死的常见元凶）
        & curl.exe --ssl-no-revoke --progress-bar --http1.1 -H "Expect:" -X POST `
            -H "Authorization: Bearer $Token" -H "Content-Type: application/zip" `
            -H "User-Agent: Sts2CharForge-release-script" --data-binary "@$file" "$uploadBase`?name=$name" -o "$env:TEMP\forge_upload_resp.json"
        if ($LASTEXITCODE -ne 0) { Fail "curl 上传失败（exit $LASTEXITCODE）" }
        $resp = Get-Content "$env:TEMP\forge_upload_resp.json" -Raw
        if ($resp -notmatch '"browser_download_url"') { Fail ("上传返回异常：" + $resp.Substring(0, [Math]::Min(300, $resp.Length))) }
    } else {
        Invoke-RestMethod -Uri "$uploadBase`?name=$name" -Headers $headers -Method Post -InFile $file -ContentType 'application/zip' -TimeoutSec 1800 | Out-Null
    }
    $sw.Stop()
    $secs = [Math]::Max(0.1, $sw.Elapsed.TotalSeconds)
    Write-Host ("  ✓ " + $name + " 传完（{0:N0} 秒，平均 {1:N2} MB/s）" -f $secs, ($mb / $secs)) -ForegroundColor Green
    # 把中文说明写进 label（JSON 里的中文转成 \uXXXX，纯 ASCII，稳）
    try {
        $rel2 = Invoke-RestMethod -Uri "$api/releases/$releaseId/assets?per_page=100" -Headers $headers -Method Get -TimeoutSec 30
        foreach ($x in @($rel2)) {
            if ($x.name -eq $name) {
                $json = '{"label":"' + (ConvertTo-AsciiJsonEscaped $a.Label) + '"}'
                Invoke-GitHubJson -Uri "$api/releases/assets/$($x.id)" -Method Patch -Json $json | Out-Null
            }
        }
    } catch { Write-Host ("  [警告] 设置 label 失败：" + $_.Exception.Message) -ForegroundColor Yellow }
}

$final = "https://github.com/$Repo/releases/tag/$Tag"
Write-Host ("完成：{0}" -f $final) -ForegroundColor Green