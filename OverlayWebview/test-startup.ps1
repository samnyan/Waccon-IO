param(
    [Parameter(Mandatory = $true)]
    [string]$Executable
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$publishDirectory = Split-Path -Parent $resolvedExecutable
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("OverlayWebview-startup-" + [Guid]::NewGuid().ToString('N'))
$process = $null

function Invoke-SmokeTest([string]$environmentName, [string]$description, [bool]$forceLogging = $true) {
    $previousValue = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
    $previousLoggingValue = [Environment]::GetEnvironmentVariable('OVERLAYWEBVIEW_FORCE_LOGGING', 'Process')
    try {
        [Environment]::SetEnvironmentVariable($environmentName, '1', 'Process')
        $loggingValue = if ($forceLogging) { '1' } else { $null }
        [Environment]::SetEnvironmentVariable('OVERLAYWEBVIEW_FORCE_LOGGING', $loggingValue, 'Process')
        $script:process = Start-Process -FilePath $testExecutable -WindowStyle Hidden -PassThru
    }
    finally {
        [Environment]::SetEnvironmentVariable($environmentName, $previousValue, 'Process')
        [Environment]::SetEnvironmentVariable('OVERLAYWEBVIEW_FORCE_LOGGING', $previousLoggingValue, 'Process')
    }

    if (-not $script:process.WaitForExit(10000)) {
        $logPath = Join-Path $testDirectory 'OverlayWebview.log'
        $log = if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Raw } else { '(No diagnostic log was written.)' }
        throw "$description smoke test failed: process did not exit within 10 seconds.`n$log"
    }

    $script:process.Refresh()
    if ($script:process.ExitCode -ne 0) {
        throw "$description smoke test failed: process exited with code $($script:process.ExitCode)."
    }
}

try {
    New-Item -ItemType Directory -Path $testDirectory | Out-Null
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $testDirectory -Recurse
    Remove-Item -LiteralPath (Join-Path $testDirectory 'OverlayWebview.log') -Force -ErrorAction SilentlyContinue
    $testConfig = '{"target":null,"web":null,"overlay":null,"logging":null}'
    Set-Content -LiteralPath (Join-Path $testDirectory 'overlay_config.json') -Encoding utf8 -Value $testConfig

    $testExecutable = Join-Path $testDirectory (Split-Path -Leaf $resolvedExecutable)
    Invoke-SmokeTest 'OVERLAYWEBVIEW_TRAY_EXIT_SMOKE_TEST' 'Logging-disabled startup' $false
    if (Test-Path -LiteralPath (Join-Path $testDirectory 'OverlayWebview.log')) {
        throw 'Logging-disabled startup smoke test failed: diagnostic log was created.'
    }

    Invoke-SmokeTest 'OVERLAYWEBVIEW_TRAY_EXIT_SMOKE_TEST' 'Startup and tray-exit'
    Invoke-SmokeTest 'OVERLAYWEBVIEW_SETTINGS_BRIDGE_SMOKE_TEST' 'Settings bridge'
    $logPath = Join-Path $testDirectory 'OverlayWebview.log'
    $log = if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Raw } else { '' }
    if ($log -notmatch 'Transparent document script registered\.') {
        throw 'Transparency smoke test failed: the document-created CSS script was not registered.'
    }

    Write-Output 'Startup, tray-exit, and settings-bridge smoke tests passed.'
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $testDirectory) {
        for ($attempt = 1; $attempt -le 10; $attempt++) {
            try {
                Remove-Item -LiteralPath $testDirectory -Recurse -Force -ErrorAction Stop
                break
            }
            catch {
                if ($attempt -eq 10) {
                    throw
                }

                Start-Sleep -Milliseconds 500
            }
        }
    }
}
