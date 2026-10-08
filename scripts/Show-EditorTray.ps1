# Shows a tray icon that turns the CV editor off and on, and says whether it is running.
# Run it as yourself, from a normal (not elevated) PowerShell:
#   ./scripts/Show-EditorTray.ps1              show the icon
#   ./scripts/Show-EditorTray.ps1 -Register    show it now and at every sign-in (a Startup shortcut)
#   ./scripts/Show-EditorTray.ps1 -Unregister  stop showing it at sign-in
# The editor itself runs from the scheduled task (scripts/Register-EditorTask.ps1), which you can't
# start or stop without administrator rights. So the icon only creates or removes
# %LOCALAPPDATA%\cv-editor\off, and scripts/Start-Editor.ps1 stops or starts the editor to match.
# Off stays off after a restart, until you turn it on again (ADR 0004 section 4).
param([switch]$Register, [switch]$Unregister, [int]$Port = 5180)

$ErrorActionPreference = 'Stop'
$taskName = 'CV editor'
$shortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'CV editor tray.lnk'
$arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$PSCommandPath`""
if ($Port -ne 5180) { $arguments += " -Port $Port" }

if ($Unregister) {
    Remove-Item $shortcut -ErrorAction SilentlyContinue
    Write-Host 'The tray icon no longer starts at sign-in. Use Exit on its menu to close it now.'
    return
}
if ($Register) {
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    $link.TargetPath = Join-Path $PSHOME 'powershell.exe'
    $link.Arguments = $arguments
    $link.WorkingDirectory = Split-Path $PSScriptRoot -Parent
    $link.WindowStyle = 7   # minimized, so the console barely shows before it hides
    $link.Description = 'Turns the CV editor off and on'
    $link.Save()
    Start-Process (Join-Path $PSHOME 'powershell.exe') $arguments -WindowStyle Hidden
    Write-Host "The tray icon is showing and will start at every sign-in ($shortcut)."
    return
}

# One icon per user: a second start just exits
$mutex = New-Object Threading.Mutex($false, 'Local\CvEditorTray')
if (-not $mutex.WaitOne(0)) { return }

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()

$stateDir = Join-Path $env:LOCALAPPDATA 'cv-editor'
$offFlag = Join-Path $stateDir 'off'
$log = Join-Path $stateDir 'editor.log'
$url = "http://localhost:$Port"

$cvImage = [Drawing.Image]::FromFile((Join-Path (Split-Path $PSScriptRoot -Parent) 'public\images\CV.png'))

# The website's CV icon with its white paper tinted: each colour channel is multiplied by the
# tint's, so white becomes the tint, the black lines stay black and their soft edges blend.
function New-CvIcon([string]$color) {
    $tint = [Drawing.ColorTranslator]::FromHtml($color)
    $matrix = New-Object Drawing.Imaging.ColorMatrix
    $matrix.Matrix00 = $tint.R / 255
    $matrix.Matrix11 = $tint.G / 255
    $matrix.Matrix22 = $tint.B / 255
    $attributes = New-Object Drawing.Imaging.ImageAttributes
    $attributes.SetColorMatrix($matrix)

    # Centred in a square, since the image is taller than it is wide
    $size = 32
    $height = $size
    $width = [int][Math]::Round($size * $cvImage.Width / $cvImage.Height)
    $bitmap = New-Object Drawing.Bitmap $size, $size
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.DrawImage($cvImage, (New-Object Drawing.Rectangle ([int](($size - $width) / 2)), 0, $width, $height),
        0, 0, $cvImage.Width, $cvImage.Height, [Drawing.GraphicsUnit]::Pixel, $attributes)
    $graphics.Dispose()
    [Drawing.Icon]::FromHandle($bitmap.GetHicon())
}

# Green only while the editor is running; the tooltip and menu tell the other states apart
$onIcon = New-CvIcon '#2ecc55'
$offIcon = New-CvIcon '#e5383b'
$states = @{
    Running  = @{ Icon = $onIcon; Text = 'Running' }
    Starting = @{ Icon = $offIcon; Text = 'Starting (building)...' }
    Stopping = @{ Icon = $offIcon; Text = 'Stopping...' }
    Off      = @{ Icon = $offIcon; Text = 'Off' }
    NoTask   = @{ Icon = $offIcon; Text = "Not running: the '$taskName' task isn't running" }
}

function Get-EditorState {
    $listening = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        Where-Object { $_.Port -eq $Port }
    $off = Test-Path $offFlag
    if ($off) { if ($listening) { return 'Stopping' } else { return 'Off' } }
    if ($listening) { return 'Running' }
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($task -and $task.State -eq 'Running') { return 'Starting' }
    'NoTask'
}

$menu = New-Object Windows.Forms.ContextMenuStrip
$status = $menu.Items.Add('CV editor')
$status.Enabled = $false
[void]$menu.Items.Add('-')
$startItem = $menu.Items.Add('Start')
$stopItem = $menu.Items.Add('Stop')
[void]$menu.Items.Add('-')
$openItem = $menu.Items.Add('Open editor')
$logItem = $menu.Items.Add('Open log')
[void]$menu.Items.Add('-')
$exitItem = $menu.Items.Add('Exit (the editor keeps its state)')

$tray = New-Object Windows.Forms.NotifyIcon
$tray.ContextMenuStrip = $menu
$script:state = $null

function Update-Tray {
    $new = Get-EditorState
    if ($new -eq $script:state) { return }
    $previous = $script:state
    $script:state = $new
    $tray.Icon = $states[$new].Icon
    $tray.Text = "CV editor: $($states[$new].Text)"   # a tooltip holds at most 63 characters
    if ($tray.Text.Length -gt 63) { $tray.Text = $tray.Text.Substring(0, 63) }
    $status.Text = "CV editor: $($states[$new].Text)"
    $startItem.Enabled = $new -in 'Off', 'Stopping', 'NoTask'
    $stopItem.Enabled = $new -in 'Running', 'Starting'
    $openItem.Enabled = $new -eq 'Running'
    if ($previous -and $new -in 'Running', 'Off') {
        $tray.ShowBalloonTip(3000, 'CV editor', $(if ($new -eq 'Running') { "Running at $url" } else { 'Stopped' }), 'Info')
    }
}

function Show-Problem([string]$message) {
    [void][Windows.Forms.MessageBox]::Show($message, 'CV editor', 'OK', 'Warning')
}

$startItem.add_Click({
    try {
        Remove-Item $offFlag -ErrorAction SilentlyContinue
        Update-Tray
        if ($script:state -eq 'NoTask') {
            Show-Problem ("The '$taskName' task isn't running, so nothing will start the editor.`n`n" +
                "Start it from an elevated PowerShell:  Start-ScheduledTask '$taskName'`n" +
                'or register it again with scripts/Register-EditorTask.ps1.')
        }
    } catch { Show-Problem $_.Exception.Message }
})
$stopItem.add_Click({
    try {
        New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
        New-Item -ItemType File -Force -Path $offFlag | Out-Null
        Update-Tray
    } catch { Show-Problem $_.Exception.Message }
})
$openItem.add_Click({ Start-Process $url })
$tray.add_DoubleClick({ if ($script:state -eq 'Running') { Start-Process $url } })
$logItem.add_Click({
    if (Test-Path $log) { Start-Process notepad.exe "`"$log`"" } else { Show-Problem "There is no log yet at $log." }
})
$exitItem.add_Click({
    $timer.Stop()
    $tray.Visible = $false
    [Windows.Forms.Application]::Exit()
})

$timer = New-Object Windows.Forms.Timer
$timer.Interval = 3000
$timer.add_Tick({ try { Update-Tray } catch { } })

Update-Tray
$tray.Visible = $true
$timer.Start()
try {
    [Windows.Forms.Application]::Run()
} finally {
    $tray.Dispose()
    $mutex.ReleaseMutex()
}
