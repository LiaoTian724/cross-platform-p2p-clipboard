. "$PSScriptRoot\common.ps1"
$state = Get-AppStatus
$app = @(Get-PackageApp)
$core = @(Get-CoreProcesses)
Write-Host ('软件：' + $(if ($app.Count -gt 0) { '正在运行' } else { '未运行' }))
Write-Host ('同步核心：' + $(if ($core.Count -gt 0) { '正在运行' } else { '未运行' }))
Write-Host ('NetBird：' + $(if (Test-NetBirdAddress) { '已就绪' } else { '未就绪' }))
Write-Host '本机监听地址：100.64.79.70:55515'
Write-Host 'Mac 地址：100.64.25.71:55516'
if ($state) {
    Write-Host ('连接状态：' + $state.State)
    Write-Host ('Windows Peer ID：' + $state.PeerId)
    Write-Host ('最近连接事件：' + $state.LastEvent)
} elseif ($app.Count -gt 0) {
    Write-Host '连接状态：暂无实时状态，请打开软件窗口查看。'
} else {
    Write-Host '连接状态：软件未运行'
}
Write-Host ('日志：' + $logsPath)
