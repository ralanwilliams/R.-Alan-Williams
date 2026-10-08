# Registers a scheduled task that starts the CV editor when Windows starts, before anyone logs in,
# so a reboot or crash doesn't keep editor.ralanwilliams.com down (ADR 0004, section 4).
# Run it from an elevated PowerShell (Run as administrator):
#   ./scripts/Register-EditorTask.ps1              register and start it
#   ./scripts/Register-EditorTask.ps1 -Unregister  stop and remove it
# The task runs scripts/Start-Editor.ps1 as you, so it uses your .env and finds your Chrome or Edge.
param([switch]$Unregister)

# The ScheduledTasks cmdlets ignore $ErrorActionPreference (they live in a CDXML module), so each
# call below passes -ErrorAction Stop itself.
$ErrorActionPreference = 'Stop'
$taskName = 'CV editor'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Run this from an elevated PowerShell (right-click PowerShell, Run as administrator).'
    return
}

if ($Unregister) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Stop
    Write-Host "Removed the '$taskName' task."
    return
}

$script = Join-Path $PSScriptRoot 'Start-Editor.ps1'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$script`"" `
    -WorkingDirectory (Split-Path $PSScriptRoot -Parent) -ErrorAction Stop
$trigger = New-ScheduledTaskTrigger -AtStartup -ErrorAction Stop
$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) `
    -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ErrorAction Stop

# S4U ("run whether the user is logged on or not", without storing a password): the task runs as
# you before anyone logs in, with internet access, but can't use your credentials for other Windows
# file shares, which the editor doesn't need. No password to type or keep in sync, and it works
# with a Microsoft account that signs in with a PIN.
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $user -LogonType S4U -RunLevel Limited -ErrorAction Stop

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings `
    -Principal $taskPrincipal -Force -ErrorAction Stop | Out-Null
Start-ScheduledTask -TaskName $taskName -ErrorAction Stop
Write-Host "Registered and started '$taskName' as $user. Log: $env:LOCALAPPDATA\cv-editor\editor.log"
