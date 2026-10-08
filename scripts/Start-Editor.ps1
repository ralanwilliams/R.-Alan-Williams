# Runs the CV editor and starts it again whenever it stops. Used by the scheduled task that keeps
# editor.ralanwilliams.com available (scripts/Register-EditorTask.ps1, ADR 0004 section 4).
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Start-Editor.ps1
# Loads .env itself, and runs in Production (no launch profile), so an error page through the
# tunnel never shows developer details. Output goes to %LOCALAPPDATA%\cv-editor\editor.log.
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
while ($true) {
    Stop-LeftoverEditor
    Write-Log 'Building and starting the editor'
    # Through the pipeline so the log stays UTF-8 (Windows PowerShell's *>> writes UTF-16).
    & dotnet run --project src/Cv.Editor --configuration Release --no-launch-profile 2>&1 |
        ForEach-Object { "$_" } | Add-Content -Path $log -Encoding utf8
    Write-Log "The editor stopped (exit code $LASTEXITCODE); starting it again in 15 seconds"
    Start-Sleep -Seconds 15
}
