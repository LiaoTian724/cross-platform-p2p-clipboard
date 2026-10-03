$ErrorActionPreference = 'Stop'
$base = Split-Path -Parent $PSScriptRoot
$data = Join-Path $base 'data'
New-Item -ItemType Directory -Force -Path $data | Out-Null
$env:TEMP = $data
$env:TMP = $data
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler not found.' }
$exe = Join-Path $base 'P2P Clipboard.exe'
& $compiler /nologo /codepage:65001 /target:winexe /platform:x64 "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/win32icon:$PSScriptRoot\clipboard.ico" "$PSScriptRoot\P2PClipboardApp.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host 'P2P Clipboard.exe built.'
