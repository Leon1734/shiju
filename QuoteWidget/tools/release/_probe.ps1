$settingsCs = 'E:\Workspace_AI\ZCODE\demo_0820\QuoteWidget\Models\AppSettings.cs'
$keyFile = 'E:\Workspace_AI\ZCODE\demo_0820\QuoteWidget	oolselease\zhipu.key'
$placeholder = "在这里填入你的智谱APIKey"
$src = [System.IO.File]::ReadAllText($settingsCs)
$key = [System.IO.File]::ReadAllText($keyFile).Trim()
Write-Host ("src length: " + $src.Length)
Write-Host ("contains placeholder: " + $src.Contains($placeholder))
Write-Host ("key length: " + $key.Length)
Write-Host ("key: " + $key.Substring(0, 12))
