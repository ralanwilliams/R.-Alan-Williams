# Runs the CV editor and starts it again whenever it stops. Used by the scheduled task that keeps
# editor.ralanwilliams.com available (scripts/Register-EditorTask.ps1, ADR 0004 section 4).
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Start-Editor.ps1
# Loads .env itself, and runs in Production (no launch profile), so an error page through the
# tunnel never shows developer details. Output goes to %LOCALAPPDATA%\cv-editor\editor.log.
# While %LOCALAPPDATA%\cv-editor\off exists, the editor is stopped and stays stopped, also after a
# restart. The tray icon (scripts/Show-EditorTray.ps1) creates and removes that file.
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
. (Join-Path $PSScriptRoot 'Import-DotEnv.ps1')

$logDir = Join-Path $env:LOCALAPPDATA 'cv-editor'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'editor.log'
if ((Test-Path $log) -and (Get-Item $log).Length -gt 10MB) {
    Move-Item -Force $log "$log.old"   # keep one previous log
}
$offFlag = Join-Path $logDir 'off'

function Write-Log([string]$message) {
    Add-Content -Path $log -Value "$(Get-Date -Format o) $message" -Encoding utf8
}

# Stopping the scheduled task ends this script but not the editor it started, which would keep
# port 5180 and lock the build output. So an editor left over from this repository is stopped first.
function Stop-LeftoverEditor {
    $editorDir = Join-Path $root 'src\Cv.Editor'
    Get-Process -Name 'Cv.Editor' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($editorDir, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object {
            Write-Log "Stopping an editor left over from an earlier start (process $($_.Id))"
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
}

$ErrorActionPreference = 'Continue'   # from here on, a failed build or crash is logged and retried
$wasOff = $false
while ($true) {
    if (Test-Path $offFlag) {
        if (-not $wasOff) { Write-Log 'Turned off; waiting to be turned on again' }
        $wasOff = $true
        Start-Sleep -Seconds 2
        continue
    }
    $wasOff = $false

    Stop-LeftoverEditor
    Write-Log 'Building and starting the editor'
    # In a background job, so this loop can stop it when the editor is turned off. The job's first
    # output is its process ID, the last is dotnet's exit code.
    $job = Start-Job -ArgumentList $root, $log -ScriptBlock {
        param($root, $log)
        $PID
        Set-Location $root
        # Written line by line in UTF-8 (Windows PowerShell's *>> writes UTF-16), and shared, so the
        # log can be read while the editor runs. Add-Content in a pipeline would lock it.
        $stream = New-Object IO.FileStream $log, 'Append', 'Write', 'ReadWrite'
        $writer = New-Object IO.StreamWriter $stream, (New-Object Text.UTF8Encoding $false)
        $writer.AutoFlush = $true
        try {
            & dotnet run --project src/Cv.Editor --configuration Release --no-launch-profile 2>&1 |
                ForEach-Object { $writer.WriteLine("$_") }
        } finally {
            $writer.Dispose()
        }
        $LASTEXITCODE
    }
    $output = @()
    while ($output.Count -eq 0 -and $job.State -eq 'Running') {
        Start-Sleep -Milliseconds 200
        $output += @(Receive-Job $job)
    }
    while ($job.State -eq 'Running' -and -not (Test-Path $offFlag)) {
        Start-Sleep -Seconds 2
    }

    if ($job.State -eq 'Running') {
        # The whole process tree: the job, dotnet run, and the build or the editor it started.
        # Logged afterwards, so the job's writer is closed and the two never write at once.
        & taskkill.exe /PID $output[0] /T /F 2>&1 | Out-Null
        Remove-Job $job -Force
        Stop-LeftoverEditor
        Write-Log 'Turned off; stopped the editor'
        $wasOff = $true
        continue
    }
    $output += @(Receive-Job $job)
    Remove-Job $job
    $exitCode = if ($output.Count -gt 1) { $output[-1] } else { 'unknown' }
    Write-Log "The editor stopped (exit code $exitCode); starting it again in 15 seconds"
    for ($i = 0; $i -lt 15 -and -not (Test-Path $offFlag); $i++) { Start-Sleep -Seconds 1 }
}
