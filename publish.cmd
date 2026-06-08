@echo off
REM ===========================================================================
REM  Tempo - build a self-contained, single-file Windows executable.
REM
REM  Usage:   publish.cmd            (defaults to win-x64)
REM           publish.cmd win-x86
REM           publish.cmd win-arm64
REM
REM  Output:  bin\publish\<rid>\Tempo.exe  - bundles the .NET 8 runtime, so the
REM           end user does NOT need to install anything.
REM  Log:     publish-log.txt        - full build output + final result.
REM ===========================================================================
setlocal EnableExtensions EnableDelayedExpansion

REM --- Remember when we started (for an elapsed-time report at the end) -------
set "T0=%TIME: =0%"

set "RID=%~1"
if "%RID%"=="" set "RID=win-x64"

REM Warn (but continue) if the runtime ID isn't one of the common Windows ones.
if /i not "%RID%"=="win-x64" if /i not "%RID%"=="win-x86" if /i not "%RID%"=="win-arm64" (
  echo.
  echo  NOTE: "%RID%" is not a standard Windows runtime ID.
  echo        The usual choices are win-x64, win-x86 or win-arm64.
  echo        Continuing anyway in case you know what you're doing...
)

set "OUTDIR=bin\publish\%RID%"
set "EXE=%OUTDIR%\Tempo.exe"
set "LOG=%~dp0publish-log.txt"

REM --- Read the version from the project so it can be shown/confirmed ---------
set "VER="
for /f "tokens=2 delims=<>" %%V in ('findstr /i "<Version>" "%~dp0AutoClicker.csproj"') do if not defined VER set "VER=%%V"
if not defined VER set "VER=unknown"

echo.
echo  ===========================================================================
echo    TEMPO  -  PUBLISH
echo    Building version %VER% for %RID%
echo  ===========================================================================

REM --- Start a fresh log ------------------------------------------------------
> "%LOG%" echo Tempo publish log
>> "%LOG%" echo Started : %DATE% %TIME%
>> "%LOG%" echo Version : %VER%
>> "%LOG%" echo Runtime : %RID%
>> "%LOG%" echo ---------------------------------------------------------------------------

REM ===========================================================================
REM  STEP 1 of 5  -  Check the .NET SDK
REM ===========================================================================
echo.
echo  [1/5] Checking for the .NET SDK ...
where dotnet >nul 2>&1
if errorlevel 1 (
  echo        ERROR: The .NET SDK was not found on your PATH.
  echo        Install the .NET 8 SDK from https://dotnet.microsoft.com/download
  echo        then run this script again.
  >> "%LOG%" echo RESULT  : FAILED - .NET SDK not found on PATH.
  echo.
  echo  A log was written to "%LOG%".
  endlocal
  exit /b 1
)

set "DOTNETVER="
for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do if not defined DOTNETVER set "DOTNETVER=%%v"
if not defined DOTNETVER set "DOTNETVER=unknown"
echo        Found .NET SDK %DOTNETVER%.
>> "%LOG%" echo SDK     : %DOTNETVER%

REM ===========================================================================
REM  STEP 2 of 5  -  Show the build configuration
REM ===========================================================================
echo.
echo  [2/5] Build configuration:
echo          Product    : Tempo %VER%
echo          Runtime ID : %RID%
echo          SDK        : %DOTNETVER%
echo          Output     : %OUTDIR%\Tempo.exe
echo          Options    : self-contained, single file, compressed, ReadyToRun
echo          Log file   : publish-log.txt
echo        (Make sure the version was bumped before building, then tag v%VER%.)

REM ===========================================================================
REM  STEP 3 of 5  -  Clean any previous output
REM ===========================================================================
echo.
echo  [3/5] Cleaning for a fresh build ...
set "CLEANED="
if exist "%OUTDIR%" (
  rd /s /q "%OUTDIR%" 2>nul
  set "CLEANED=1"
)
REM Also clear intermediate build artifacts so this is a true from-scratch build.
if exist "%~dp0obj" (
  rd /s /q "%~dp0obj" 2>nul
  set "CLEANED=1"
)
if exist "%~dp0bin\Release" (
  rd /s /q "%~dp0bin\Release" 2>nul
  set "CLEANED=1"
)
if defined CLEANED (
  echo        Cleared previous build output and intermediates.
) else (
  echo        Nothing to clean - already fresh.
)

REM ===========================================================================
REM  STEP 4 of 5  -  Build  (this is the slow part)
REM ===========================================================================
echo.
echo  [4/5] Building Tempo - this can take a minute or two ...
echo        (compiling, embedding the .NET runtime, compressing into one .exe)
echo        Full output is captured to publish-log.txt.
echo.

set "PSOUT=%TEMP%\tempo_build_out.txt"
set "PSERR=%TEMP%\tempo_build_err.txt"
del "%PSOUT%" "%PSERR%" >nul 2>&1

REM Run the build through PowerShell so a live spinner + elapsed timer is shown
REM while dotnet works (its output is captured to the log). If PowerShell isn't
REM available, fall back to a plain build that writes straight to the log.
where powershell >nul 2>&1
if errorlevel 1 (
  echo        ^(PowerShell not found - building without the live spinner^)
  dotnet publish AutoClicker.csproj -c Release -r %RID% -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false -o "%OUTDIR%" >> "%LOG%" 2>&1
  set "RESULT=!ERRORLEVEL!"
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='SilentlyContinue'; $a=@('publish','AutoClicker.csproj','-c','Release','-r','%RID%','-p:SelfContained=true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:EnableCompressionInSingleFile=true','-p:PublishReadyToRun=true','-p:DebugType=none','-p:DebugSymbols=false','-o','%OUTDIR%'); $p=Start-Process dotnet -ArgumentList $a -NoNewWindow -PassThru -RedirectStandardOutput '%PSOUT%' -RedirectStandardError '%PSERR%'; $w=14; $pos=0; $dir=1; $t=[Diagnostics.Stopwatch]::StartNew(); while(-not $p.HasExited){ $bar=(' '*$pos)+'==='+(' '*[Math]::Max(0,$w-$pos-3)); [Console]::Write([char]13 + '        Building  [' + $bar + ']  ' + ('{0:mm\:ss}' -f $t.Elapsed) + ' elapsed   '); $pos+=$dir; if($pos -le 0){$dir=1}; if($pos -ge ($w-3)){$dir=-1}; Start-Sleep -Milliseconds 90 }; [Console]::Write([char]13 + (' '*60) + [char]13); Get-Content '%PSOUT%','%PSERR%' -ErrorAction SilentlyContinue | Add-Content '%LOG%'; exit $p.ExitCode"
  set "RESULT=!ERRORLEVEL!"
)
del "%PSOUT%" "%PSERR%" >nul 2>&1

>> "%LOG%" echo ---------------------------------------------------------------------------
>> "%LOG%" echo Finished: %DATE% %TIME%

if not "%RESULT%"=="0" (
  >> "%LOG%" echo RESULT  : FAILED ^(dotnet exit code %RESULT%^)
  echo.
  echo  ===========================================================================
  echo    BUILD FAILED  (exit code %RESULT%).
  echo    The full output is saved in: "%LOG%"
  echo    Lines containing "error":
  echo  ---------------------------------------------------------------------------
  findstr /I /C:"error" "%LOG%"
  echo  ===========================================================================
  endlocal
  exit /b %RESULT%
)

>> "%LOG%" echo RESULT  : SUCCESS
echo        Build succeeded.

REM ===========================================================================
REM  STEP 5 of 5  -  Verify, checksum and summarise
REM ===========================================================================
echo.
echo  [5/5] Verifying the output ...

REM --- Sanity check: the build reported success, but did we get the exe? ------
if not exist "%EXE%" (
  >> "%LOG%" echo RESULT  : NO OUTPUT ^(Tempo.exe not found at %EXE%^)
  echo        ERROR: build reported success but Tempo.exe was not found:
  echo           %EXE%
  echo        Check the log for details: "%LOG%"
  endlocal
  exit /b 1
)

set "SIZE="
for %%F in ("%EXE%") do set "SIZE=%%~zF"
>> "%LOG%" echo Output  : %EXE% ^(%SIZE% bytes^)
echo        Found Tempo.exe (%SIZE% bytes).

REM --- Compute a SHA-256 checksum so you can publish it for integrity ---------
echo        Computing SHA-256 checksum ...
set "SHA="
for /f "skip=1 tokens=* delims=" %%H in ('certutil -hashfile "%EXE%" SHA256 2^>nul') do if not defined SHA set "SHA=%%H"
if defined SHA >> "%LOG%" echo SHA-256 : %SHA%

REM Write the checksum to a file you can attach to the release so people can
REM verify the download (certutil -hashfile Tempo.exe SHA256).
if defined SHA (
  > "%EXE%.sha256" echo %SHA% *Tempo.exe
  echo        Wrote checksum file: %EXE%.sha256
)

REM --- Verify the built exe's version matches the project version ------------
REM Catches the classic mistake of bumping the version but attaching an old build.
set "EXEVER="
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "try{(Get-Item '%EXE%').VersionInfo.FileVersion}catch{''}" 2^>nul`) do set "EXEVER=%%V"
if defined EXEVER (
  >> "%LOG%" echo ExeVer  : %EXEVER%
  echo %EXEVER% | findstr /b /c:"%VER%" >nul
  if errorlevel 1 (
    echo.
    echo  WARNING: built Tempo.exe reports version %EXEVER%, but the project says %VER%.
    echo           You may be about to ship an old build - double-check the output folder.
  ) else (
    echo        Verified Tempo.exe is version %VER%.
  )
)

REM ===========================================================================
REM  Bundle a one-click installer so users can install Tempo (Start Menu and
REM  optional Desktop shortcuts, plus an uninstall entry) instead of placing a
REM  bare exe by hand.
REM ===========================================================================
if exist "%~dp0install.cmd"   copy /y "%~dp0install.cmd"   "%OUTDIR%\install.cmd"   >nul
if exist "%~dp0uninstall.cmd" copy /y "%~dp0uninstall.cmd" "%OUTDIR%\uninstall.cmd" >nul

set "SETUPZIP=bin\publish\Tempo-Setup-%VER%.zip"
set "MADEZIP="
where powershell >nul 2>&1
if not errorlevel 1 (
  if exist "%OUTDIR%\install.cmd" (
    if exist "%SETUPZIP%" del /f /q "%SETUPZIP%" >nul 2>&1
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%OUTDIR%\*' -DestinationPath '%SETUPZIP%' -Force" >nul 2>&1
    if exist "%SETUPZIP%" set "MADEZIP=1"
  )
)
if defined MADEZIP (
  echo        Built installer package: %SETUPZIP%
) else (
  echo        Installer scripts copied next to Tempo.exe.
)

REM --- Work out how long the whole thing took --------------------------------
set "T1=%TIME: =0%"
for /f "tokens=1-4 delims=:.," %%a in ("%T0%") do set /a "S0=(((1%%a-100)*60)+(1%%b-100))*60+(1%%c-100)"
for /f "tokens=1-4 delims=:.," %%a in ("%T1%") do set /a "S1=(((1%%a-100)*60)+(1%%b-100))*60+(1%%c-100)"
set /a "ELAPSED=S1-S0"
if !ELAPSED! lss 0 set /a "ELAPSED+=86400"
set /a "EMIN=ELAPSED/60"
set /a "ESEC=ELAPSED%%60"
>> "%LOG%" echo Elapsed : !EMIN!m !ESEC!s

echo.
echo  ===========================================================================
echo    DONE  -  Tempo %VER% built in !EMIN!m !ESEC!s
echo  ---------------------------------------------------------------------------
echo    Executable : %EXE%
echo    Size       : %SIZE% bytes
if defined SHA echo    SHA-256    : %SHA%
if defined SHA echo    Checksum   : %EXE%.sha256  (optional - attach to the release)
echo    Build log  : publish-log.txt
echo  ---------------------------------------------------------------------------
echo    Next steps:
echo      1. Create a GitHub release tagged  v%VER%
echo      2. Easiest for users: attach  Tempo-Setup-%VER%.zip
echo         ^(they unzip and run install.cmd - it creates shortcuts and an
echo          uninstall entry, no admin needed^)
echo      3. Or attach the single Tempo.exe ^(+ Tempo.exe.sha256^) to run it portably
echo      4. No .NET install needed either way
echo    (Windows may warn "Unknown publisher" because it isn't code-signed;
echo     choose More info ^> Run anyway.)
echo  ===========================================================================
echo.

choice /c YN /n /m "Open the output folder now? [Y/N] "
if errorlevel 2 goto :done
start "" explorer "%OUTDIR%"

:done
endlocal
