# Regenerates the ADR table in README.md from docs/adr/NNNN-*.md.
#   ./scripts/Update-AdrIndex.ps1           rewrite the table
#   ./scripts/Update-AdrIndex.ps1 -Check    change nothing; exit 1 if the table is out of date
# Each ADR's title comes from its first heading ("# ADR 0001: Title"), and its status and
# date from the "- **Status:**" and "- **Date:**" lines (see docs/adr/template.md).
# The table goes between the adr-index markers in README.md. Runs on Windows PowerShell 5.1
# and PowerShell 7; CI runs it on every push to master (.github/workflows/adr-index.yml).
param([switch]$Check)

$root = Split-Path $PSScriptRoot -Parent
$readmePath = Join-Path $root 'README.md'
$adrDir = Join-Path $root 'docs/adr'
$startMarker = '<!-- adr-index:start -->'
$endMarker = '<!-- adr-index:end -->'

function Get-Field([string]$text, [string]$name) {
    if ($text -match "(?m)^-\s+\*\*${name}:\*\*\s*(.+?)\s*$") { return $Matches[1] }
    return ''
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$rows = Get-ChildItem -Path $adrDir -Filter '*.md' |
    Where-Object { $_.Name -match '^\d{4}-' } |
    Sort-Object Name |
    ForEach-Object {
        $text = [System.IO.File]::ReadAllText($_.FullName, $utf8)
        $number = $_.Name.Substring(0, 4)
        $title = $_.BaseName
        if ($text -match '(?m)^#\s+(?:ADR\s+\d+:\s*)?(.+?)\s*$') { $title = $Matches[1] }
        $cells = @($title, (Get-Field $text 'Status'), (Get-Field $text 'Date')) | ForEach-Object { $_ -replace '\|', '\|' }
        "| [$number](docs/adr/$($_.Name)) | $($cells[0]) | $($cells[1]) | $($cells[2]) |"
    }

$readme = [System.IO.File]::ReadAllText($readmePath, $utf8)
$nl = if ($readme.Contains("`r`n")) { "`r`n" } else { "`n" }
$table = if ($rows) {
    (@('| ADR | Decision | Status | Date |', '|---|---|---|---|') + $rows) -join $nl
} else {
    '_No ADRs yet._'
}

$start = $readme.IndexOf($startMarker)
$end = $readme.IndexOf($endMarker)
if ($start -lt 0 -or $end -lt $start) {
    Write-Error "README.md needs '$startMarker' followed by '$endMarker'."
    exit 2
}
$updated = $readme.Substring(0, $start + $startMarker.Length) + $nl + $table + $nl + $readme.Substring($end)

if ($updated -eq $readme) {
    Write-Host 'ADR index is up to date.'
    exit 0
}
if ($Check) {
    Write-Host 'ADR index is out of date. Run ./scripts/Update-AdrIndex.ps1'
    exit 1
}
[System.IO.File]::WriteAllText($readmePath, $updated, $utf8)
Write-Host "ADR index updated ($(@($rows).Count) ADRs)."
