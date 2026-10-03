$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"
$app = @(Get-PackageApp)
if ($app.Count -gt 0) {
    $request = Start-Process -FilePath $appPath -ArgumentList '--stop' -PassThru
    $request.WaitForExit(5000) | Out-Null
    foreach ($process in $app) {
        if (-not $process.WaitForExit(8000)) { throw '软件未响应退出请求，请在状态窗口中点击退出软件。' }
    }
}
$monitor = Get-PackageMonitor
if ($monitor) {
    Stop-Process -Id $monitor.Id -Force
    $monitor.WaitForExit(5000) | Out-Null
}
foreach ($core in @(Get-CoreProcesses)) {
    Stop-Process -Id $core.Id -Force
    $core.WaitForExit(5000) | Out-Null
}
Remove-Item -LiteralPath $statePath -ErrorAction SilentlyContinue
Write-Host 'P2P Clipboard 已停止。'
