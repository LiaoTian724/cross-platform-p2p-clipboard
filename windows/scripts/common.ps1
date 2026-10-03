$packageRoot = Split-Path -Parent $PSScriptRoot
$corePath = Join-Path $packageRoot 'bin\p2p-clipboard.exe'
$appPath = Join-Path $packageRoot 'P2P Clipboard.exe'
$logsPath = Join-Path $packageRoot 'logs'
$statePath = Join-Path $logsPath 'monitor.json'
$appStatePath = Join-Path $logsPath 'app-status.json'
function Test-NetBirdAddress {
    foreach ($nic in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
        if ($nic.OperationalStatus -ne 'Up') { continue }
        foreach ($address in $nic.GetIPProperties().UnicastAddresses) {
            if ($address.Address.ToString() -eq '100.64.79.70') { return $true }
        }
    }
    return $false
}
function Get-CoreProcesses {
    @(Get-Process -Name 'p2p-clipboard' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.MainModule.FileName -eq $corePath } catch { $false }
    })
}
function Get-PackageApp {
    @(Get-Process -Name 'P2P Clipboard' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.MainModule.FileName -eq $appPath } catch { $false }
    })
}
function Get-AppStatus {
    if (-not (Test-Path -LiteralPath $appStatePath)) { return $null }
    try {
        $state = [IO.File]::ReadAllText($appStatePath) | ConvertFrom-Json
        $process = Get-Process -Id $state.ProcessId -ErrorAction Stop
        if ($process.MainModule.FileName -ne $appPath) { return $null }
        if ($process.StartTime.ToUniversalTime().Ticks.ToString() -ne $state.StartTicks) { return $null }
        if (([DateTime]::UtcNow - [DateTime]::Parse($state.UpdatedUtc).ToUniversalTime()).TotalSeconds -gt 5) { return $null }
        return $state
    } catch { return $null }
}
function Get-PackageMonitor {
    if (-not (Test-Path -LiteralPath $statePath)) { return $null }
    try {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        $process = Get-Process -Id $state.ProcessId -ErrorAction Stop
        if ($process.ProcessName -ne 'powershell') { return $null }
        if ($process.StartTime.ToUniversalTime().Ticks.ToString() -ne $state.StartTicks) { return $null }
        return $process
    } catch { return $null }
}
