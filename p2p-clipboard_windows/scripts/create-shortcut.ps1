$ErrorActionPreference = 'Stop'
$base = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $base 'P2P Clipboard.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Missing P2P Clipboard.exe' }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $base '启动 P2P Clipboard.lnk'))
$shortcut.TargetPath = $exe
$shortcut.Arguments = ''
$shortcut.WorkingDirectory = $base
$shortcut.IconLocation = $exe + ',0'
$shortcut.WindowStyle = 1
$shortcut.Description = 'P2P Clipboard：查看 Mac 连接和 NetBird 状态'
$shortcut.Save()
Write-Host '启动快捷方式已更新。'
