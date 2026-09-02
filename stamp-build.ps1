<#
  stamp-build.ps1 — stamps a fresh build ID into Utils\BuildInfo.cs.

  publish.cmd runs this immediately before every build, so the build number moves
  on every publish while the version number stays put. That is the whole point: a
  test build and the release it was cut from carry the SAME version, and without a
  build ID there is nothing on screen to tell them apart.

    .\stamp-build.ps1                  -> next number, channel "test"
    .\stamp-build.ps1 -Channel release -> next number, channel "release"
    .\stamp-build.ps1 -DryRun          -> report what it would do, write nothing

  Prints the new build number on stdout (publish.cmd captures it).

  ASSERT-FIRST: all three constants must match exactly once each, or the file is
  left completely untouched and the script exits non-zero. A half-stamped
  BuildInfo.cs would be worse than none, because it would lie with confidence.
#>
[CmdletBinding()]
param(
    [ValidateSet('test', 'release')]
    [string] $Channel = 'test',

    [string] $Path,

    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

# Resolved HERE, not as a param default: $PSScriptRoot is empty while the param
# block is being bound under "powershell -File", so the default silently became
# Join-Path '' -> a binding error and no stamp at all. publish.cmd sends stderr to
# nul, so it showed up only as "build ID was not stamped".
if (-not $Path) {
    $root = $PSScriptRoot
    if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $Path = Join-Path $root 'Utils\BuildInfo.cs'
}

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Error "BuildInfo.cs not found at: $Path"
    exit 1
}

$text = [System.IO.File]::ReadAllText($Path)

# One regex per constant. Captured groups keep the exact surrounding text, so
# indentation and line endings survive untouched.
$reNumber  = '(?m)^(?<a>\s*public const int Number = )(?<v>-?\d+)(?<b>;)'
$reStamp   = '(?m)^(?<a>\s*public const string StampUtc = ")(?<v>[^"]*)(?<b>";)'
$reChannel = '(?m)^(?<a>\s*public const string Channel = ")(?<v>[^"]*)(?<b>";)'

foreach ($pair in @(@($reNumber, 'Number'), @($reStamp, 'StampUtc'), @($reChannel, 'Channel'))) {
    $n = ([regex]::Matches($text, $pair[0])).Count
    if ($n -ne 1) {
        Write-Error "BuildInfo.cs: '$($pair[1])' matched $n times (expected exactly 1). Nothing written."
        exit 2
    }
}

$current = [int]([regex]::Match($text, $reNumber).Groups['v'].Value)
$next    = $current + 1
$stamp   = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')

if ($DryRun) {
    Write-Host "would stamp: build $next, channel $Channel, $stamp  (currently build $current)"
    exit 0
}

$text = [regex]::Replace($text, $reNumber,  { param($m) $m.Groups['a'].Value + $next    + $m.Groups['b'].Value })
$text = [regex]::Replace($text, $reStamp,   { param($m) $m.Groups['a'].Value + $stamp   + $m.Groups['b'].Value })
$text = [regex]::Replace($text, $reChannel, { param($m) $m.Groups['a'].Value + $Channel + $m.Groups['b'].Value })

# UTF-8 WITH BOM, written through .NET rather than Set-Content: PowerShell 5.1's
# cmdlets default to the ANSI codepage and would mangle the box-drawing characters
# in the comments. The rest of the project's .cs files carry a BOM too.
[System.IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding $true))

# stdout is the contract with publish.cmd — the number, alone, nothing else.
Write-Output $next
