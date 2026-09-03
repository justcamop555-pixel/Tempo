<#
  record-build.ps1 — completes a row in the developer build history.

  stamp-build.ps1 writes the row before the build (it knows the id, channel and
  version); this fills in the two columns that only exist afterwards, the exe's
  SHA-256 and its size. A row left with those blank is a build that was stamped and
  then did not produce an exe — which is worth knowing rather than hiding.

    .\record-build.ps1 -Id 260903-0216 -Sha ABC123… -Bytes 106201448
    .\record-build.ps1 -Show 10        -> print the last 10 builds and exit

  The history is developer-only. Nothing in the shipped app reads it, and it lives
  under bin\ so git ignores it.
#>
[CmdletBinding()]
param(
    [string] $Id,
    [string] $Sha,
    [long]   $Bytes = 0,
    [int]    $Show = 0
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
$hist = Join-Path $root 'bin\build-history.tsv'

if ($Show -gt 0) {
    if (-not (Test-Path -LiteralPath $hist)) {
        Write-Host 'No build history yet — it starts at the next publish.'
        exit 0
    }
    $rows = @(Get-Content -LiteralPath $hist | Select-Object -Skip 1 |
              Where-Object { $_.Trim().Length -gt 0 })
    if ($rows.Count -eq 0) { Write-Host 'No builds recorded yet.'; exit 0 }

    Write-Host ''
    Write-Host ('  {0,-14} {1,-8} {2,-9} {3,-12} {4}' -f 'BUILD', 'CHANNEL', 'VERSION', 'SIZE', 'SHA-256')
    Write-Host ('  ' + ('-' * 74))
    foreach ($r in ($rows | Select-Object -Last $Show)) {
        $c = $r -split "`t"
        $size = if ($c.Count -ge 6 -and $c[5]) { '{0:N1} MB' -f ($c[5] / 1MB) } else { '(no exe)' }
        # ASCII "..." rather than an ellipsis: this prints into a cmd console whose
        # codepage is not UTF-8, where the single character came out as "aEUR|".
        $sha  = if ($c.Count -ge 5 -and $c[4]) { $c[4].Substring(0, [Math]::Min(16, $c[4].Length)) + '...' } else { '' }
        Write-Host ('  {0,-14} {1,-8} {2,-9} {3,-12} {4}' -f $c[1], $c[2], $c[3], $size, $sha)
    }
    Write-Host ''
    Write-Host ("  {0} build(s) recorded in {1}" -f $rows.Count, $hist)
    exit 0
}

if (-not $Id) { Write-Error 'record-build.ps1 needs -Id (or -Show N).'; exit 1 }
if (-not (Test-Path -LiteralPath $hist)) { exit 0 }   # nothing stamped; nothing to complete

# Fill the LAST row carrying this id — the id is minute-unique, so there is one.
$lines = @(Get-Content -LiteralPath $hist)
for ($i = $lines.Count - 1; $i -ge 1; $i--) {
    $c = $lines[$i] -split "`t"
    if ($c.Count -ge 4 -and $c[1] -eq $Id) {
        while ($c.Count -lt 6) { $c += '' }
        $c[4] = $Sha
        $c[5] = if ($Bytes -gt 0) { "$Bytes" } else { '' }
        $lines[$i] = ($c -join "`t")
        Set-Content -LiteralPath $hist -Value $lines -Encoding utf8
        exit 0
    }
}
exit 0
