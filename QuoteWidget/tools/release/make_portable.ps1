# 拾句 · 便携版打包脚本
# 用法: powershell -File make_portable.ps1 [-Version "2.3.0"]
# 产物: release\拾句_v版本_便携版.zip（exe + 词库 + 词典 + 使用说明）

param(
    [string]$Version = "2.3.0"
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # tools/release -> 项目根
$releaseDir = Join-Path $projectDir "release"

# 注入 Key（Key 只进产物，不进源码；用 .NET IO 避免 PS5.1 的 ANSI 误读）
$keyFile = Join-Path $PSScriptRoot "zhipu.key"
$settingsCs = Join-Path $projectDir "Models\AppSettings.cs"
$placeholder = "在这里填入你的智谱APIKey"
$injected = $false
if (Test-Path -LiteralPath $keyFile) {
    $key = [System.IO.File]::ReadAllText($keyFile).Trim()
    $src = [System.IO.File]::ReadAllText($settingsCs)
    if ($key -and $src.Contains($placeholder)) {
        [System.IO.File]::WriteAllText($settingsCs, $src.Replace($placeholder, $key))
        $injected = $true
        Write-Host "已注入智谱 Key（发布后自动还原占位符）"
    }
}

Write-Host "[1/3] dotnet publish (单文件 Release)..."
Push-Location $projectDir
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -nologo -v q
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "publish 失败" }
Pop-Location

# 还原源码占位符（Key 只进产物，不进源码）
if ($injected) {
    $src = [System.IO.File]::ReadAllText($settingsCs)
    [System.IO.File]::WriteAllText($settingsCs, $src.Replace($key, $placeholder))
    Write-Host "源码占位符已还原"
}

$publishDir = Join-Path $projectDir "bin\Release\net10.0-windows\win-x64\publish"
$winDir = Join-Path $projectDir "bin\Release\net10.0-windows\win-x64"
$stageDir = Join-Path $releaseDir "stage"
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir | Out-Null

Write-Host "[2/3] 组装便携包..."
Copy-Item (Join-Path $publishDir "QuoteWidget.exe") $stageDir

# 词库与词典（内置四类 + 扩展包 + 离线词典）随包分发
foreach ($name in @("词库", "词典")) {
    $src = Join-Path $winDir $name
    if (Test-Path -LiteralPath $src) {
        Copy-Item -LiteralPath $src -Destination (Join-Path $stageDir $name) -Recurse -Force
    }
}

$readme = @"
拾句 · 桌面名言挂件 v$Version
================================

使用：双击 QuoteWidget.exe 即可（需已安装 .NET 10 桌面运行时）。

本包已内置：
  词库\        全部词库（内置四类 + 扩展包，可编辑增补，也可放自己的 .md/.txt）
  词典\        离线查词词典（可自行扩充词条）

翻译已预配置智谱 GLM-4-Flash（免费引擎），开箱即用；
如需更换引擎，在 设置 → 翻译引擎 里选择并填写对应的 API Key。

数据保存在 %APPDATA%\QuoteWidget\（设置/收藏/日志），删除不影响程序。

退出：托盘图标右键 -> 退出。
"@
Set-Content -Path (Join-Path $stageDir "使用说明.txt") -Value $readme -Encoding UTF8

Write-Host "[3/3] 压缩..."
$zipPath = Join-Path $releaseDir "拾句_v$Version`_便携版.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath
Remove-Item $stageDir -Recurse -Force

$size = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1KB)
Write-Host "完成: $zipPath ($size KB)"
