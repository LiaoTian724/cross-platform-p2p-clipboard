$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"
if (-not (Test-Path -LiteralPath $appPath)) { throw 'Missing P2P Clipboard.exe' }
Start-Process -FilePath $appPath -WorkingDirectory $packageRoot
