<#
    Tempo - web installer
    https://justcamop555-pixel.github.io/Tempo/install.ps1

    Run it with:

        irm https://justcamop555-pixel.github.io/Tempo/install.ps1 | iex

    or, to pass options:

        & ([scriptblock]::Create((irm https://justcamop555-pixel.github.io/Tempo/install.ps1))) -Portable

    WHAT IT DOES, in order:
      1. asks GitHub which release is current, and for that release's own SHA-256 digests
      2. downloads the setup zip (or Tempo.exe with -Portable)
      3. CHECKS the download against the digest GitHub published - and stops if it differs
      4. runs the installer that ships inside that zip, which needs no administrator rights
      5. starts Tempo

    It installs per-user, into %LOCALAPPDATA%\Programs\TempoClicker. It never asks for
    administrator, never writes outside your own profile, and uninstalls from
    Settings > Apps like any other program.

    Nothing here is hidden: this file is plain text at the URL above, and every byte it
    installs comes from the official GitHub release, checked before it is run.

    ASCII ONLY, on purpose. Windows PowerShell 5.1 reads a BOM-less file as ANSI, and
    "irm" decodes a response whose Content-Type carries no charset as ISO-8859-1 - either
    one turns a stray em dash into mojibake, and mojibake in the wrong place is a parse
    error in a script the user cannot see failing. Keep every character in this file 7-bit.
#>

[CmdletBinding()]
param(
    # Just put Tempo.exe somewhere and stop - no Start-Menu entry, no uninstall entry.
    [switch] $Portable,

    # Where a portable copy lands (default: your Downloads folder).
    [string] $Dir,

    # Install a specific release, e.g. -Version v1.0.322 (default: the latest).
    [string] $Version,

    # Don't start Tempo when it is done.
    [switch] $NoLaunch
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'   # the progress bar makes Invoke-WebRequest crawl

$Owner = 'justcamop555-pixel'
$Repo  = 'Tempo'

function Say  { param($m) Write-Host "  $m" }
function Step { param($m) Write-Host ""; Write-Host "  $m" -ForegroundColor Cyan }
function Good { param($m) Write-Host "  $m" -ForegroundColor Green }
function Fail {
    param($m)
    Write-Host ""
    Write-Host "  $m" -ForegroundColor Red
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "  Tempo" -ForegroundColor Yellow -NoNewline
Write-Host "  ::  precision auto-clicker for Windows"
Write-Host "  ---------------------------------------------"

# -- 1. is this machine even the right shape? -------------------------------
if ($PSVersionTable.PSVersion.Major -lt 5) {
    Fail "This needs PowerShell 5 or newer. Windows 10 and 11 have it built in."
}
if (-not [Environment]::Is64BitOperatingSystem) {
    Fail "Tempo is 64-bit only, and this looks like a 32-bit Windows."
}
if ([Environment]::OSVersion.Version.Major -lt 10) {
    Fail "Tempo needs Windows 10 or 11."
}

# TLS 1.2 for the download on older hosts that still default to TLS 1.0.
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

# -- 2. which release, and what are its checksums? --------------------------
Step "Looking up the latest release..."
if ($Version) {
    $api = "https://api.github.com/repos/$Owner/$Repo/releases/tags/$Version"
} else {
    $api = "https://api.github.com/repos/$Owner/$Repo/releases/latest"
}

try {
    $release = Invoke-RestMethod -Uri $api -Headers @{ 'User-Agent' = 'Tempo-web-installer' }
} catch {
    Fail "Couldn't reach GitHub to find the release. Check your connection and try again."
}

$tag = $release.tag_name
if (-not $tag) { Fail "GitHub returned a release with no tag - nothing to install." }

$zipAsset = $release.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
$exeAsset = $release.assets | Where-Object { $_.name -eq 'Tempo.exe' } | Select-Object -First 1

if ($Portable) {
    $asset = $exeAsset
} elseif ($zipAsset) {
    $asset = $zipAsset
} else {
    $asset = $exeAsset
}
if (-not $asset) { Fail "Release $tag has no file to download." }

$sizeMb = [math]::Round($asset.size / 1MB, 1)
Good ("Tempo " + $tag.TrimStart('v') + "  ::  " + $asset.name + "  ::  " + $sizeMb + " MB")

# GitHub's own digest for the uploaded file ("sha256:<hex>"). This is the reference that
# does NOT live on your machine, which is exactly what makes checking it worth anything.
$expected = $null
if ($asset.digest -and $asset.digest -match '^sha256:([0-9a-fA-F]{64})$') {
    $expected = $Matches[1].ToLower()
}

# -- 3. download ------------------------------------------------------------
$stamp = [guid]::NewGuid().ToString('N').Substring(0, 8)
$work  = Join-Path ([IO.Path]::GetTempPath()) ("tempo-install-" + $stamp)
New-Item -ItemType Directory -Path $work -Force | Out-Null
$download = Join-Path $work $asset.name

Step "Downloading..."
try {
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $download -UseBasicParsing -Headers @{ 'User-Agent' = 'Tempo-web-installer' }
} catch {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    Fail "The download failed. Check your connection and try again."
}

# -- 4. check it against what GitHub says it should be ----------------------
Step "Checking the download..."
$actual = (Get-FileHash $download -Algorithm SHA256).Hash.ToLower()
if ($expected) {
    if ($actual -ne $expected) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
        Fail ("The downloaded file does NOT match the checksum GitHub published for it." +
              [Environment]::NewLine + "  Nothing was installed." +
              [Environment]::NewLine + "  expected " + $expected +
              [Environment]::NewLine + "  got      " + $actual)
    }
    Good ("SHA-256 matches the release  ::  " + $actual.Substring(0, 16) + "...")
} else {
    # Older releases predate GitHub publishing per-asset digests. Say so rather than
    # pretending a check happened.
    Say "GitHub published no checksum for this asset, so it could not be verified."
    Say ("SHA-256 of what was downloaded: " + $actual)
}

# -- 5. put it where it belongs ---------------------------------------------
if ($Portable -or (-not $zipAsset)) {
    if ($Dir) { $target = $Dir } else { $target = Join-Path $HOME 'Downloads' }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $final = Join-Path $target 'Tempo.exe'
    Copy-Item $download $final -Force
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

    Step "Done."
    Say ("Tempo.exe is in " + $target)
    Say "It is self-contained - double-click it and it runs. Settings live in your AppData folder."
    if (-not $NoLaunch) { Start-Process $final }
    Write-Host ""
    exit 0
}

Step "Installing..."
$unpack = Join-Path $work 'unpacked'
try {
    Expand-Archive -Path $download -DestinationPath $unpack -Force
} catch {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    Fail "The zip could not be unpacked."
}

$installer = Get-ChildItem -Path $unpack -Filter 'install.cmd' -Recurse | Select-Object -First 1
if (-not $installer) {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    Fail "That release's zip has no install.cmd in it."
}

# The zip's own installer does the work: copy into %LOCALAPPDATA%\Programs\TempoClicker,
# Start-Menu shortcut, Settings > Apps entry, no administrator. It is the same script
# anyone unzipping by hand would double-click - this just saves the unzipping.
$proc = Start-Process -FilePath $installer.FullName -ArgumentList '/silent' -WorkingDirectory $installer.DirectoryName -Wait -PassThru -WindowStyle Hidden
$code = $proc.ExitCode

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

if ($code -ne 0) {
    Fail ("The installer stopped with exit code " + $code + ". Nothing was left running.")
}

$installed = Join-Path $env:LOCALAPPDATA 'Programs\TempoClicker\Tempo.exe'
if (-not (Test-Path $installed)) {
    Fail ("The installer finished but Tempo is not where it should be (" + $installed + ").")
}

Step "Done."
Good ("Tempo " + $tag.TrimStart('v') + " is installed.")
Say "Start it from the Start Menu, or from:"
Say ("  " + $installed)
Say "Uninstall any time from Settings > Apps."
if (-not $NoLaunch) { Start-Process $installed }
Write-Host ""
