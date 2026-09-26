# 打「整合包」zip（发布用）
#   把部署根目录（默认 D:\ds\s\4）整包压成一个 zip：程序文件 + 环境包（Godot / 便携 dotnet）+ 首次使用说明。
#
#   排除规则（和之前的整合包保持一致）：
#     · 「自定义角色存档」下【只保留顶层 *.json 存档】，生成的角色工程（子目录）不进包：
#       那些工程里带着 sts2.dll 等游戏文件，不能分发
#     · *.zip / *.lnk / 日志 / 自检结果文件不进包（本机快捷方式和上一次的 zip 都不该进）
#       **例外**：「！首次使用先点这个！」里的 zip 要进包 —— 那是 GDRE 打包工具（用户明确要求跟着包发）
#   打完会自检一遍：包里出现 sts2.dll、或「自定义角色存档」下出现非顶层 json，就直接报错删包。
#
#   版本号只有一个来源：src\Sts2CharForge.App\Sts2CharForge.App.csproj 里的 <InformationalVersion>
#   （整合包文件名 / 启动器文件名 / 程序包 zip / 窗口标题都用它）。每次改动往上加一位。
#
# 用法： powershell -ExecutionPolicy Bypass -File tools\make_bundle.ps1
param(
    [string]$Root = "D:\ds\s\4",
    # 空 = 按 csproj 里的版本号自动取名（Sts2CharForge_整合包_<V0.0.1>.zip）
    [string]$Out  = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path $Root)) { throw "部署根目录不存在：$Root" }

# 版本号：和启动器 / 程序包 zip 同一个来源（App 的 csproj）
$Version = "V0.0.1"
$csproj = Join-Path (Split-Path -Parent $PSScriptRoot) "src\Sts2CharForge.App\Sts2CharForge.App.csproj"
if (Test-Path $csproj) {
    $m = [regex]::Match((Get-Content $csproj -Raw), '<InformationalVersion>\s*([^<\s]+)\s*</InformationalVersion>')
    if ($m.Success) { $Version = $m.Groups[1].Value }
}
if ([string]::IsNullOrWhiteSpace($Out)) { $Out = "D:\ds\s\Sts2CharForge_整合包_$Version.zip" }
Write-Host "版本号：$Version"

$userDataName = "自定义角色存档"
# 发布包里**一份存档都不带**（用户要求：包里不要有存档信息、不要有任何能暴露隐私的东西）。
# 以前只保留顶层 *.json，那也会把「你的模组名、卡牌名、角色名」打进去 —— 现在整个目录跳过。
# 注意：$rel 用的是 '/' 分隔符（zip 规范），所以前缀也必须是 '/'。
# 早期写成 '\' 时前缀永远匹配不上 —— 结果把用户的生成工程（含 sts2.dll）一起打进了发布包，实测踩过。
$userDataPrefix = $userDataName + "/"
$junk = [regex]'^(uicheck|envcheck|buildtest|newprofiletest|movedtest)_result\.txt$|^crash_log\.txt$|\.log$'

# 「包里绝不能出现」的字符串（打完逐条目扫：① 条目名全查；② 内容只查文本类文件 + 我们自己的程序集）
# 为什么内容不全查：环境包里的 Godot / .NET SDK / 微软运行时 DLL 是第三方二进制，里面本来就有
# 厂商自己的构建路径；而且短字符串在二进制里**会偶然撞上**（实测 "cwf" 在 CodePages.dll、
# 甚至一张 PNG 的像素数据里都能命中，但整棵树里根本没有 C:\Users\ 这种形式）。
# 所以：本机路径按「盘符 + 具体目录」这种形态查；用户名只连同 \Users\ 一起查。
$userName = Split-Path $env:USERPROFILE -Leaf
$leakPatterns = New-Object System.Collections.Generic.List[string]
# 只放「真会暴露位置」的：开发/打包用的目录、本机下载目录、打包临时区。
# 注意：不要把工具自己的临时目录前缀（forge_selftest / forge_buildtest）算进来 ——
# 那只是自检用的 %TEMP% 子目录名，不含任何个人信息，但它是程序里的字符串字面量，会误报。
foreach ($p in @('D:\ds', 'D:\download', '_packaging_keep', 'ds\s\t3', 't3\src')) { $leakPatterns.Add($p) }
$userLeakPatterns = New-Object System.Collections.Generic.List[string]
if ($userName) {
    $userLeakPatterns.Add("C:\Users\$userName")
    $userLeakPatterns.Add("\Users\$userName")
}
$leakScanMaxBytes = 8MB

function Test-ShouldScanContent([string]$rel, [string]$name) {
    if ($rel -like 'Sts2CharForge_环境包/*') { return ($name -match '\.(txt|md|json|ps1|bat|cmd|cfg|config|ini)$') }
    if ($rel -like '程序文件/*') { return $true }                     # 我们的程序 + 运行时都查（运行时里不会有本机路径）
    return ($name -match '\.(txt|md|json|ps1|bat|cmd|cs|cfg|config)$' -or $name -match '^README')
}

if (Test-Path $Out) { Remove-Item $Out -Force }

$zip = [System.IO.Compression.ZipFile]::Open($Out, 'Create')
$count = 0
$skippedUser = 0
$skippedOther = 0
$badFiles = New-Object System.Collections.Generic.List[string]
try {
    foreach ($f in Get-ChildItem $Root -Recurse -File -Force) {
        $rel = $f.FullName.Substring($Root.Length + 1).Replace('\', '/')

        if ($rel.StartsWith($userDataPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            $skippedUser++
            continue
        }
        # 用户自己的图片一律不进包（用户要求：测试用的图标 / 截图都不要发出去）。
        # 只有「程序文件」（工具自带的占位图、图标）和「环境包」里的图片才是工具自身的资源。
        # 例外：「！首次使用先点这个！」里的教程截图是**要发出去的**（用户明确要求保留）
        if ($f.Extension -match '^\.(png|jpg|jpeg|webp|bmp|gif|ico)$' -and
            -not $rel.StartsWith('程序文件/', [StringComparison]::OrdinalIgnoreCase) -and
            -not $rel.StartsWith('Sts2CharForge_环境包/', [StringComparison]::OrdinalIgnoreCase) -and
            -not $rel.StartsWith('！首次使用先点这个！/', [StringComparison]::OrdinalIgnoreCase)) {
            $skippedUser++
            continue
        }
        # .zip / .lnk 一律不进包（上一次的整合包、本机快捷方式都不该进）。
        # 例外：「！首次使用先点这个！」里的 zip 要发出去 —— 那里放的是 GDRE 解包工具包
        # （GDRE_tools-vX-windows.zip，用户明确要求跟着包发；本体的 pck 能解出来全靠它）。
        if ($f.Extension -eq '.zip' -and
            -not $rel.StartsWith('！首次使用先点这个！/', [StringComparison]::OrdinalIgnoreCase)) { $skippedOther++; continue }
        if ($f.Extension -eq '.lnk') { $skippedOther++; continue }
        if ($junk.IsMatch($f.Name)) { $skippedOther++; continue }
        # 游戏本体文件绝不进包（用户工程里会带一份，上面已经排掉了；这里再兜一道）
        if ($f.Name -eq 'sts2.dll' -or $f.Name -eq '0Harmony.dll') { $badFiles.Add($rel) | Out-Null }

        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $count++
    }
} finally { $zip.Dispose() }

# ===== 打完自检 =====
# ① 不许出现游戏文件（sts2.dll / 0Harmony.dll）；② 一份存档都不许有；
# ③ **逐条目扫内容**：本机路径 / 本机用户名 / 工具内部目录名一律不许出现在包里。
$check = [System.IO.Compression.ZipFile]::OpenRead($Out)
try {
    foreach ($e in $check.Entries) {
        if ($e.Name -eq 'sts2.dll' -or $e.Name -eq '0Harmony.dll') { $badFiles.Add("游戏文件: " + $e.FullName) | Out-Null }
        if ($e.FullName.StartsWith($userDataPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            $badFiles.Add("存档: " + $e.FullName) | Out-Null
        }
        # 条目名本身也不能带本机路径
        foreach ($p in $leakPatterns) {
            if ($e.FullName.IndexOf($p, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $badFiles.Add("路径泄漏（条目名）: " + $e.FullName + "  ← " + $p) | Out-Null
            }
        }
        # 内容扫描：只看「文本类文件 + 我们自己的程序」
        if ($e.Length -gt 0 -and $e.Length -le $leakScanMaxBytes -and (Test-ShouldScanContent $e.FullName $e.Name)) {
            try {
                # 用字节读出来按两种编码都比一遍：文本是 UTF-8，程序集里的字符串字面量是 UTF-16
                $ms = New-Object System.IO.MemoryStream
                $st = $e.Open(); $st.CopyTo($ms); $st.Close()
                $bytes = $ms.ToArray(); $ms.Dispose()
                $asLatin = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
                $asUni = [System.Text.Encoding]::Unicode.GetString($bytes)
                foreach ($p in ($leakPatterns + $userLeakPatterns)) {
                    if ($asLatin.IndexOf($p, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                        $asUni.IndexOf($p, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                        $badFiles.Add("路径泄漏（内容）: " + $e.FullName + "  ← " + $p) | Out-Null
                    }
                }
            } catch { }
        }
    }
} finally { $check.Dispose() }

if ($badFiles.Count -gt 0) {
    Remove-Item $Out -Force
    Write-Host "自检失败：包里出现了不该有的文件 / 路径（已删掉这个 zip）" -ForegroundColor Red
    $badFiles | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    throw "整合包自检未通过"
}

$size = (Get-Item $Out).Length / 1MB
Write-Host ("完成：{0}  （{1:N1} MB，{2} 个文件；跳过用户存档/工程 {3} 个 / 其它 {4} 个）" -f $Out, $size, $count, $skippedUser, $skippedOther)
Write-Host ("  隐私自检：包里没有存档、没有本机路径、没有用户名（扫描 {0} 条规则）" -f $leakPatterns.Count) -ForegroundColor Green
