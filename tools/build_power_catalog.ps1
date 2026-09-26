# 从解包工程自动生成「效果库」数据（Power 列表 + 中文/英文名 + 增益/减益类型）
# 用法： powershell -ExecutionPolicy Bypass -File tools\build_power_catalog.ps1
#       powershell -ExecutionPolicy Bypass -File tools\build_power_catalog.ps1 -Vanilla "<你的解包工程目录>"
# 输出： src\Sts2CharForge.Core\Data\powers_catalog.json

param(
    # 必填：你解包出来的游戏工程目录（该目录下应有 src\Core\Models\Powers）。
    # 这里**不写死任何本机路径** —— 这是开发者自己的解包位置，不该出现在公开仓库里。
    [string]$Vanilla = "",
    [string]$Out = "$PSScriptRoot\..\src\Sts2CharForge.Core\Data\powers_catalog.json"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Vanilla)) {
    throw "请用 -Vanilla 指定解包后的游戏工程目录（例如 -Vanilla `"X:\某处\1`"），该目录下应有 src\Core\Models\Powers"
}

$powersDir = Join-Path $Vanilla "src\Core\Models\Powers"
if (-not (Test-Path $powersDir)) { throw "找不到 Power 源码目录: $powersDir" }

function Read-Loc($lang) {
    $p = Join-Path $Vanilla "localization\$lang\powers.json"
    if (-not (Test-Path $p)) { return @{} }
    $json = Get-Content $p -Raw -Encoding UTF8 | ConvertFrom-Json
    $h = @{}
    foreach ($prop in $json.PSObject.Properties) { $h[$prop.Name] = $prop.Value }
    return $h
}

$zh = Read-Loc 'zhs'
$en = Read-Loc 'eng'

# WeakPower -> WEAK_POWER（与游戏内本地化键一致）
function ConvertTo-Slug([string]$name) {
    return ($name -creplace '([a-z0-9])([A-Z])', '$1_$2').ToUpperInvariant()
}

$list = New-Object System.Collections.ArrayList
$skipped = 0

foreach ($f in Get-ChildItem $powersDir -Filter '*.cs' -File) {
    $cls = $f.BaseName
    if ($cls -like 'Mock*') { continue }         # 测试用假 Power 不要

    $m = Select-String -Path $f.FullName -Pattern 'Type\s*=>\s*PowerType\.(Buff|Debuff)' -Encoding UTF8 | Select-Object -First 1
    if (-not $m) { $skipped++; continue }
    $type = $m.Matches[0].Groups[1].Value

    $slug = ConvertTo-Slug $cls
    $zhName = $zh["$slug.title"]
    $enName = $en["$slug.title"]
    # 只保留有本地化名的（没有名字的通常是内部/派生 Power，不适合给用户选）
    if (-not $zhName -and -not $enName) { $skipped++; continue }

    # 层数型 / 单次型
    $stack = (Select-String -Path $f.FullName -Pattern 'StackType\s*=>\s*PowerStackType\.(Counter|Single|None)' -Encoding UTF8 | Select-Object -First 1)
    $stackType = if ($stack) { $stack.Matches[0].Groups[1].Value } else { "Counter" }

    [void]$list.Add([pscustomobject]@{
        id        = $cls
        slug      = $slug
        type      = $type
        stackType = $stackType
        zh        = if ($zhName) { $zhName } else { $enName }
        en        = if ($enName) { $enName } else { $zhName }
    })
}

$sorted = $list | Sort-Object type, id
$dir = Split-Path $Out -Parent
New-Item -ItemType Directory -Force $dir | Out-Null
$sorted | ConvertTo-Json -Depth 4 | Set-Content $Out -Encoding UTF8

$buffCount = ($sorted | Where-Object { $_.type -eq 'Buff' }).Count
$debuffCount = ($sorted | Where-Object { $_.type -eq 'Debuff' }).Count
"生成完成: $Out"
"  Buff   : $buffCount"
"  Debuff : $debuffCount"
"  跳过   : $skipped（无类型标注或无本地化名）"
