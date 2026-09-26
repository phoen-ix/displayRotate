# Helpers for ci.yml's installer-smoke job. Dot-source them: . .github/scripts/installer-smoke-helpers.ps1
# Adapted from pawse's. DisplayRotate writes no log, so "started" is proven by the tray
# instance's single-instance mutex - the same signal the installer itself watches.

$script:MutexName = 'Local\DisplayRotate-single-instance-4d9e21'

function Test-AppMutex {
    $m = $null
    if ([System.Threading.Mutex]::TryOpenExisting($script:MutexName, [ref]$m)) {
        $m.Dispose()
        return $true
    }
    return $false
}

# The tray instance is up: the process exists and holds its mutex. Started through
# explorer.exe by /RESTART, so it is not a child of anything here - poll.
function Wait-AppStarted([int]$maxSeconds = 60) {
    $deadline = (Get-Date).AddSeconds($maxSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((Get-Process DisplayRotate -ErrorAction SilentlyContinue) -and (Test-AppMutex)) {
            # Still there a moment later: it did not start and immediately fall over.
            Start-Sleep -Seconds 2
            if (-not (Get-Process DisplayRotate -ErrorAction SilentlyContinue)) { throw "DisplayRotate started, then exited" }
            "DisplayRotate is running in the tray"
            return
        }
        Start-Sleep -Milliseconds 500
    }
    throw "DisplayRotate did not start within $maxSeconds s"
}

# Gone, and quickly: the installer asks over the quit event first and only force-kills as a
# fallback, after up to ten seconds and a (silently answered) prompt. Under the ceiling means
# the quit channel worked.
function Wait-AppGone([int]$maxSeconds) {
    $started = Get-Date
    while (Get-Process DisplayRotate -ErrorAction SilentlyContinue) {
        if (((Get-Date) - $started).TotalSeconds -gt 60) { throw "DisplayRotate is still running after 60 s" }
        Start-Sleep -Milliseconds 250
    }
    $took = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
    "DisplayRotate was gone after $took s"
    if ($took -gt $maxSeconds) { throw "DisplayRotate took $took s to go - that is the taskkill fallback, not the quit channel" }
}

function Wait-SetupDone([string]$setup) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($setup)
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline -and (Get-Process -Name $name -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 500 }
    if (Get-Process -Name $name -ErrorAction SilentlyContinue) { throw "$name is still running after 120 s" }
}

function Wait-Removed([string]$key, [string]$dir) {
    # An NSIS uninstaller re-launches itself from %TEMP%, so the process we started returns at
    # once - poll for the result. It also removes its own folder through a delayed cmd, a
    # second or two after the rest.
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-Path $key) -and -not (Test-Path $dir)) { return }
        Start-Sleep -Milliseconds 500
    }
    if (Test-Path $key) { throw "the uninstall left $key in the registry" }
    if (Test-Path $dir) { throw "the uninstall left $dir behind: $((Get-ChildItem $dir -Force).Name -join ', ')" }
}

# What an uninstall must take with it for this account: settings, update downloads, and an
# autostart entry pointing at the install being removed.
function Set-AppTraces([string]$installDir) {
    $settings = Join-Path $env:APPDATA 'DisplayRotate'
    New-Item -ItemType Directory -Force -Path $settings | Out-Null
    '{ "overlayDurationMs": 2000 }' | Set-Content (Join-Path $settings 'settings.json') -Encoding utf8
    New-Item -ItemType Directory -Force -Path (Join-Path $env:TEMP 'DisplayRotate-update-0123456789ab') | Out-Null
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (-not (Test-Path $run)) { New-Item -Path $run -Force | Out-Null }
    Set-ItemProperty -Path $run -Name 'DisplayRotate' -Value ('"' + (Join-Path $installDir 'DisplayRotate.exe') + '"')
}

function Assert-NoAppTraces {
    $left = @()
    if (Test-Path (Join-Path $env:APPDATA 'DisplayRotate')) { $left += "$env:APPDATA\DisplayRotate" }
    if (Get-ChildItem $env:TEMP -Directory -Filter 'DisplayRotate-update-*' -ErrorAction SilentlyContinue) { $left += "$env:TEMP\DisplayRotate-update-*" }
    $run = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
    if ($run -and $run.PSObject.Properties['DisplayRotate']) { $left += 'the HKCU Run value' }
    if ($left) { throw "the uninstall left traces behind: $($left -join '; ')" }
    "no traces left"
}
