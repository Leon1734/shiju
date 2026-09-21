﻿# 拾句 · 推送到 GitHub
# 使用前：先打开你的代理软件（v2rayN 等，本地端口 10809），再运行本脚本。
# 本仓库已配置 git 代理 127.0.0.1:10809；若代理端口不同，请先执行：
#   git config http.proxy http://127.0.0.1:你的端口

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot

# 1) 从凭据管理器读取 GitHub令牌
$cred = "protocol=https`nhost=github.com`n`n" | git credential fill
$token = ($cred | Select-String "^password=").Line.Substring(9)
if (-not $token) { throw "未找到 GitHub 凭据，请先运行 git push 手动登录一次" }

# 2) 创建仓库（已存在则忽略报错）
$headers = @{ Authorization = "token $token"; Accept = "application/vnd.github+json" }
$body = '{"name":"shiju","description":"拾句 · Windows 桌面名言挂件 —— 2400+ 句好话，七彩文字，翻译引擎，WPF/.NET 10","private":false}'
try {
    $r = Invoke-RestMethod -Method Post -Uri "https://api.github.com/user/repos" -Headers $headers -Body $body -Proxy "http://127.0.0.1:10809"
    Write-Host "仓库已创建: $($r.html_url)"
} catch {
    Write-Host "仓库可能已存在（或创建失败）：$($_.Exception.Message)"
}

# 3) 推送
git push -u origin main
Pop-Location
