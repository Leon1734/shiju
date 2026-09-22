# 端到端升级测试：在新版本 2.3.4 的机器上，驱动 2.3.3 旧版本完成在线升级
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$UIA = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$root = $UIA::RootElement

function Find-Window([string]$title) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $title)
    $w = $root.FindFirst($Scope::Children, $cond)
    if (-not $w) { $w = $root.FindFirst($Scope::Descendants, $cond) }
    return $w
}

function Invoke-ByName($parent, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)
    $el = $parent.FindFirst($Scope::Descendants, $cond)
    if (-not $el) { return $false }
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}

Write-Host "[1] 等待设置窗口..."
$win = $null
for ($i = 0; $i -lt 30 -and -not $win; $i++) { Start-Sleep -Milliseconds 500; $win = Find-Window "设置 - 拾句" }
if (-not $win) { Write-Host "FAIL: 设置窗口未出现"; exit 1 }
Write-Host "OK: 找到设置窗口"

Write-Host "[2] 点击 检查更新..."
if (-not (Invoke-ByName $win "检查更新")) { Write-Host "FAIL: 未找到按钮"; exit 1 }
Start-Sleep -Seconds 3

Write-Host "[3] 等待升级确认对话框..."
$dlg = $null
for ($i = 0; $i -lt 20 -and -not $dlg; $i++) { Start-Sleep -Milliseconds 500; $dlg = Find-Window "拾句 · 软件升级" }
if (-not $dlg) { Write-Host "FAIL: 确认对话框未出现"; exit 1 }
Add-Type -AssemblyName System.Windows.Forms
try { $dlg.SetFocus() } catch { }
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
Write-Host "OK: 已用回车确认默认按钮（是），等待下载与自动重启（旧进程退出、批处理替换 exe 后拉起新版本）"

for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 1
    $p = Get-Process -Name QuoteWidget -ErrorAction SilentlyContinue |
         Where-Object { $_.Path -like "*updatetest*" } | Select-Object -First 1
    if ($p) {
        $ver = $p.MainModule.FileVersionInfo.FileVersion
        $size = [math]::Round((Get-Item $p.Path).Length / 1KB)
        if ($ver -like "2.3.6*") {
            Write-Host "SUCCESS: 升级完成，进程版本 = $ver，exe 大小 = $size KB（单文件包已替换）"
            exit 0
        } else {
            Write-Host "  ...进程版本仍为 $ver，等待替换"
        }
    }
}
Write-Host "TIMEOUT: 等待升级完成超时"
exit 1
