# Loads KEY=VALUE lines from a .env file into the current PowerShell session.
# Dot-source it so the variables stay set:   . ./scripts/Import-DotEnv.ps1
# Blank lines and lines starting with # are skipped. Values are taken verbatim
# (everything after the first '='), so connection strings with ';' work as-is.
param([string]$Path = (Join-Path $PSScriptRoot '..\.env'))

if (-not (Test-Path $Path)) {
    Write-Error "No .env at $Path. Copy .env.example to .env and fill it in."
    return
}

foreach ($line in Get-Content $Path) {
    $trimmed = $line.Trim()
    if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
    $eq = $trimmed.IndexOf('=')
    if ($eq -lt 1) { continue }
    $name = $trimmed.Substring(0, $eq).Trim()
    $value = $trimmed.Substring($eq + 1).Trim()
    if ($value.Contains('<')) {
        Write-Warning "$name still has a <placeholder>; fill it in .env"
    }
    Set-Item -Path "env:$name" -Value $value
    Write-Host "Set $name"
}
