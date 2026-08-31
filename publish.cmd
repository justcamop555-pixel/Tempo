@echo off
REM ===========================================================================
REM
REM   TEMPO  -  publish.cmd  (release builder)
REM
REM   Builds a self-contained, single-file Windows executable of Tempo and
REM   packages everything a GitHub release needs: the exe, a SHA-256 checksum,
REM   a one-click installer bundle (<version>.zip), a combined
REM   CHECKSUMS.txt, and - when present - the matching release notes.
REM
REM   USAGE
REM     publish.cmd [rid] [flags]
REM
REM     rid          win-x64 (default) | win-x86 | win-arm64
REM
REM   FLAGS
REM     /help, /?    Show the full help screen and exit.
REM     /quick       Skip the clean phase (incremental build - faster, but use
REM                  a full build for anything you intend to ship).
REM     /nozip       Do not build the setup zip bundle.
REM     /ci          Plain output: no banner art, no live spinner, no prompts.
REM                  Intended for scripted/automated runs.
REM     /open        Open the output folder when done (skips the question).
REM     /noprompt    Never ask anything; same prompts policy as /ci but keeps
REM                  the colourful output.
REM
REM   OUTPUT
REM     bin\publish\<rid>\Tempo.exe            self-contained single file
REM     bin\publish\<rid>\Tempo.exe.sha256     checksum for that exe
REM     bin\publish\<rid>\install.cmd          per-user installer
REM     bin\publish\<rid>\uninstall.cmd        matching uninstaller
REM     bin\publish\<rid>\INSTALL-README.txt   tiny "how to install" note
REM     bin\publish\<ver>.zip                  the bundle users download
REM     bin\publish\CHECKSUMS.txt              SHA-256 of exe + setup zip
REM     publish-log.txt                        full build output + results
REM
REM   EXIT CODES
REM     0  success        1  .NET SDK missing      2  build failed
REM     3  verify failed  4  bad flag / usage
REM
REM ===========================================================================
setlocal EnableExtensions EnableDelayedExpansion

REM Always operate from the folder this script lives in, so the relative
REM AutoClicker.csproj and bin\publish paths resolve to the project no matter
REM what directory the script was launched from (double-click, a different
REM working dir, etc.).
cd /d "%~dp0"

REM Capture the script folder NOW, while %0 still means this script.
REM
REM The flag parser below uses SHIFT, and SHIFT renumbers %0 along with %1..%9 - so
REM once a couple of arguments have been consumed, "%~dp0" no longer refers to
REM publish.cmd at all and degrades to "C:\". Every use of it further down was
REM therefore aimed at the drive root, and that ONE fault produced four separate
REM symptoms that looked unrelated:
REM
REM   * version read as "unknown"       findstr opened C:\AutoClicker.csproj
REM   * six "Access is denied" lines    the log wrote to C:\publish-log.txt
REM   * the clean phase cleaned nothing it deleted C:\obj and C:\bin\Release,
REM                                     which is how a STALE exe got packaged
REM   * no setup zip was ever produced  C:\install.cmd does not exist, so the
REM                                     installer was never staged for zipping
REM
REM The cd above still works because it runs BEFORE the loop. %ROOT% is what keeps
REM working after it.
set "ROOT=%~dp0"

REM ---------------------------------------------------------------------------
REM  Flag parsing. The runtime ID may appear anywhere among the flags.
REM ---------------------------------------------------------------------------
set "RID="
set "OPT_QUICK="
set "OPT_NOZIP="
set "OPT_CI="
set "OPT_OPEN="
set "OPT_NOPROMPT="
set "BADARG="

:parseArgs
if "%~1"=="" goto :argsDone
set "ARG=%~1"
if /i "%ARG%"=="/help"      goto :help
if /i "%ARG%"=="-help"      goto :help
if /i "%ARG%"=="/?"         goto :help
if /i "%ARG%"=="/quick"     set "OPT_QUICK=1"    & goto :nextArg
if /i "%ARG%"=="/nozip"     set "OPT_NOZIP=1"    & goto :nextArg
if /i "%ARG%"=="/ci"        set "OPT_CI=1"       & goto :nextArg
if /i "%ARG%"=="/open"      set "OPT_OPEN=1"     & goto :nextArg
if /i "%ARG%"=="/noprompt"  set "OPT_NOPROMPT=1" & goto :nextArg
REM Anything that doesn't start with a slash is treated as the runtime ID.
set "FIRSTCHAR=%ARG:~0,1%"
if "%FIRSTCHAR%"=="/" (
  set "BADARG=%ARG%"
  goto :nextArg
)
if not defined RID set "RID=%ARG%"
:nextArg
shift
goto :parseArgs
:argsDone

if defined BADARG (
  echo.
  echo   Unknown flag: %BADARG%
  echo   Run  publish.cmd /help  to see every option.
  echo.
  endlocal
  exit /b 4
)

if "%RID%"=="" set "RID=win-x64"
if defined OPT_CI set "OPT_NOPROMPT=1"

REM ---------------------------------------------------------------------------
REM  Colour palette (ANSI). Modern Windows 10/11 consoles render these; on CI or
REM  very old consoles we blank them so the output stays clean plain text.
REM ---------------------------------------------------------------------------
for /f %%E in ('echo prompt $E ^| cmd') do set "ESC=%%E"
set "C_RESET=%ESC%[0m"
set "C_TITLE=%ESC%[38;2;167;139;250m"
set "C_STEP=%ESC%[38;2;96;165;250m"
set "C_OK=%ESC%[38;2;52;211;153m"
set "C_WARN=%ESC%[38;2;251;191;36m"
set "C_ERR=%ESC%[38;2;248;113;113m"
set "C_DIM=%ESC%[38;2;150;160;180m"
set "CHK=%ESC%[38;2;52;211;153m"
if defined OPT_CI (
  set "C_RESET=" & set "C_TITLE=" & set "C_STEP=" & set "C_OK=" & set "C_WARN=" & set "C_ERR=" & set "C_DIM=" & set "CHK="
)

REM ---------------------------------------------------------------------------
REM  Branded TEMPO banner (24-bit colour where the console supports it).
REM ---------------------------------------------------------------------------
if defined OPT_CI goto :afterBanner
where powershell >nul 2>&1
if errorlevel 1 (
  echo.
  echo     =====  T E M P O  =====
  echo            release builder
  echo.
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "$e=[char]27; $b=[char]0x2588; $rows=@('11111011110100010111001111','00100010000110110100101001','00100011100101010111001001','00100010000100010100001001','00100011110100010100001111'); $w=$rows[0].Length; Write-Host ''; for($r=0;$r -lt $rows.Count;$r++){ $line=''; $s=$rows[$r]; for($x=0;$x -lt $s.Length;$x++){ if($s[$x] -eq '1'){ $t=$x/[Math]::Max(1,$w-1); $rr=[int](56+(167-56)*$t); $gg=[int](211+(139-211)*$t); $bb=[int](248+(250-248)*$t); $line=$line+$e+'[38;2;'+$rr+';'+$gg+';'+$bb+'m'+$b } else { $line=$line+' ' } }; Write-Host ('    '+$line+$e+'[0m'); Start-Sleep -Milliseconds 45 }; Write-Host ('    '+$e+'[38;2;150;160;180m'+'auto-clicker - release builder'+$e+'[0m'); Write-Host ''"
)
:afterBanner

REM ---------------------------------------------------------------------------
REM  Paths, version, timers.
REM ---------------------------------------------------------------------------
set "T0=%TIME: =0%"

if /i not "%RID%"=="win-x64" if /i not "%RID%"=="win-x86" if /i not "%RID%"=="win-arm64" (
  echo.
  echo   NOTE: "%RID%" is not a standard Windows runtime ID.
  echo         The usual choices are win-x64, win-x86 or win-arm64.
  echo         Continuing anyway in case you know what you're doing...
)

set "OUTDIR=bin\publish\%RID%"
set "EXE=%OUTDIR%\Tempo.exe"
rem  Relative on purpose. This was "%~dp0publish-log.txt", which resolved to
rem  C:\publish-log.txt after the SHIFT loop (see the note beside "set ROOT" above),
rem  so every ">> %LOG%" answered "Access is denied." and the log stayed empty for
rem  every build. %ROOT% would serve equally well now that it exists; a bare name is
rem  simply the plainest thing that cannot go wrong, phase 0 having already cd'd here.
set "LOG=publish-log.txt"

set "VER="
rem  delims=<> PLUS a space. Without the space, `for /f` drops space from the
rem  delimiter set entirely, so the line's leading indentation becomes token 1 and
rem  token 2 is the literal word "Version" — which is what this printed for years,
rem  and why the zip came out named after a version nobody recognised. With space
rem  back in, the indentation is skipped and token 2 is the number. Verified against
rem  both an indented and a non-indented <Version> line.
rem  TWO separate faults met on this one line, which is why it stayed broken.
rem  First: it read "%~dp0AutoClicker.csproj", and after the SHIFT loop that is
rem  C:\AutoClicker.csproj — findstr reported "Cannot open" and VER fell through to
rem  "unknown". Second: even once findstr could open the file, delims=<> alone drops
rem  space from the delimiter set, so the leading indentation became token 1 and
rem  token 2 was the literal word "Version". Fixing either one alone still gave a
rem  wrong answer. Verified against both an indented and a non-indented line.
for /f "tokens=2 delims=<> " %%V in ('findstr /i "<Version>" AutoClicker.csproj') do if not defined VER set "VER=%%V"
if not defined VER set "VER=unknown"

echo.
echo  %C_TITLE%===========================================================================%C_RESET%
echo    %C_TITLE%TEMPO  -  PUBLISH%C_RESET%
echo    Building version %C_OK%%VER%%C_RESET% for %C_OK%%RID%%C_RESET%
if defined OPT_QUICK echo    Mode: QUICK ^(clean phase skipped^)
if defined OPT_CI    echo    Mode: CI ^(plain output, no prompts^)
echo  ===========================================================================

REM Colourful animated divider: a gradient bar that sweeps left-to-right (skipped
REM in CI/plain mode, and silently does nothing if PowerShell isn't available).
if not defined OPT_CI (
  where powershell >nul 2>&1 && powershell -NoProfile -ExecutionPolicy Bypass -Command "$e=[char]27; $b=[char]0x2550; $w=75; for($x=0;$x -lt $w;$x++){ $t=$x/[Math]::Max(1,$w-1); $rr=[int](167+(34-167)*$t); $gg=[int](139+(211-139)*$t); $bb=[int](250+(238-250)*$t); [Console]::Write($e+'[38;2;'+$rr+';'+$gg+';'+$bb+'m'+$b); Start-Sleep -Milliseconds 7 }; [Console]::Write($e+'[0m'); Write-Host ''"
)

> "%LOG%" echo Tempo publish log
>> "%LOG%" echo Started : %DATE% %TIME%
>> "%LOG%" echo Version : %VER%
>> "%LOG%" echo Runtime : %RID%
if defined OPT_QUICK >> "%LOG%" echo Mode    : quick
if defined OPT_CI    >> "%LOG%" echo Mode    : ci
>> "%LOG%" echo ---------------------------------------------------------------------------

REM ===========================================================================
REM  PHASE 1 of 7  -  Environment preflight
REM ===========================================================================
echo.
echo  %C_STEP%[1/7]%C_RESET% Checking the environment ...
echo        %C_DIM%Looking for the .NET 8 SDK, the project file, and enough free disk space.%C_RESET%
set "TP1=%TIME: =0%"

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
echo        %CHK%[OK]%C_RESET% .NET SDK     : %DOTNETVER%
>> "%LOG%" echo SDK     : %DOTNETVER%

REM Warn when the active SDK isn't the 8.x the project targets.
set "SDKMAJOR=%DOTNETVER:~0,1%"
if not "%SDKMAJOR%"=="8" (
  echo        NOTE: project targets .NET 8 but the active SDK is %DOTNETVER%.
  echo              That usually still works ^(SDKs build down-level^) - just FYI.
)

REM Every installed SDK, for the log (handy when builds differ between PCs).
>> "%LOG%" echo Installed SDKs:
for /f "tokens=*" %%s in ('dotnet --list-sdks 2^>nul') do >> "%LOG%" echo    %%s

REM OS, CPU and free disk space on this drive.
set "OSLINE="
for /f "tokens=*" %%o in ('ver') do if not defined OSLINE set "OSLINE=%%o"
echo        Windows      : %OSLINE%
echo        CPU threads  : %NUMBER_OF_PROCESSORS%
>> "%LOG%" echo OS      : %OSLINE%
>> "%LOG%" echo CPU     : %NUMBER_OF_PROCESSORS% threads

set "FREEMB="
where powershell >nul 2>&1
if not errorlevel 1 (
  for /f "usebackq delims=" %%f in (`powershell -NoProfile -Command "[int]((Get-PSDrive -Name (Get-Location).Drive.Name).Free/1MB)" 2^>nul`) do set "FREEMB=%%f"
)
if defined FREEMB (
  echo        Free disk    : !FREEMB! MB on this drive
  >> "%LOG%" echo Disk    : !FREEMB! MB free
  if !FREEMB! lss 600 (
    echo        WARNING: less than 600 MB free - a self-contained build needs
    echo                 room for intermediates. Consider freeing space first.
  )
)

call :SecsBetween "%TP1%" "%TIME: =0%" PH1S

REM ===========================================================================
REM  PHASE 2 of 7  -  Build configuration
REM ===========================================================================
echo.
echo  %C_STEP%[2/7]%C_RESET% Build configuration:
echo          Product    : Tempo %VER%
echo          Runtime ID : %RID%
echo          SDK        : %DOTNETVER%
echo          Output     : %OUTDIR%\Tempo.exe
echo          Options    : self-contained, single file, ReadyToRun
echo                       ^(everything bundled inside the exe - runs from any folder^)
echo          Log file   : publish-log.txt
if defined OPT_NOZIP echo          Setup zip  : SKIPPED ^(/nozip^)
echo        ^(Make sure the version was bumped before building, then tag v%VER%.^)

REM ===========================================================================
REM  PHASE 3 of 7  -  Clean any previous output
REM ===========================================================================
echo.
set "TP3=%TIME: =0%"
set "RECLAIMED="

REM Stop any running Tempo FIRST - including a portable copy launched from this very
REM output folder - before we try to delete anything. A running exe locks its own file
REM and the native DLLs, which would make the clean below silently fail to remove them
REM (leaving stale files behind) and the build fail to overwrite the exe. This runs even
REM in /quick mode (which skips the clean) so the build can still replace the exe. The
REM brief wait lets Windows actually release the handles before we touch the files.
taskkill /f /im Tempo.exe >nul 2>&1
ping -n 2 127.0.0.1 >nul 2>&1

if defined OPT_QUICK (
  echo  %C_STEP%[3/7]%C_RESET% Clean skipped ^(/quick^) - building incrementally.
  >> "%LOG%" echo Clean   : skipped ^(quick mode^)
  goto :afterClean
)
echo  %C_STEP%[3/7]%C_RESET% Cleaning for a fresh build ...
echo        Removing previous output and intermediates:
echo          - %OUTDIR%
echo          - obj\  and  bin\Release\

REM Measure what the old artifacts occupied so we can report space reclaimed.
REM Use the same script-anchored paths the rd commands below delete, so the
REM figure is correct no matter what the current directory is.
where powershell >nul 2>&1
if not errorlevel 1 (
  for /f "usebackq delims=" %%m in (`powershell -NoProfile -Command "$t=0; foreach($d in @('%OUTDIR%','%ROOT%obj','%ROOT%bin\Release')){ if(Test-Path $d){ $t+=(Get-ChildItem $d -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum } }; [int]($t/1MB)" 2^>nul`) do set "RECLAIMED=%%m"
)

set "CLEANED="
if exist "%OUTDIR%" (
  rd /s /q "%OUTDIR%" 2>nul
  set "CLEANED=1"
)
REM If the folder still exists after the delete, something held a lock on it (an open
REM Explorer window on the folder, antivirus, a second Tempo we didn't catch). Flag it
REM so the user knows stale files may remain, rather than failing silently.
set "CLEANFAIL="
if exist "%OUTDIR%" set "CLEANFAIL=1"
if exist "%ROOT%obj" (
  rd /s /q "%ROOT%obj" 2>nul
  set "CLEANED=1"
)
if exist "%ROOT%bin\Release" (
  rd /s /q "%ROOT%bin\Release" 2>nul
  set "CLEANED=1"
)
if defined CLEANED (
  if defined RECLAIMED (
    echo        Cleared previous output and intermediates ^(reclaimed ~!RECLAIMED! MB^).
    >> "%LOG%" echo Clean   : reclaimed ~!RECLAIMED! MB
  ) else (
    echo        Cleared previous build output and intermediates.
    >> "%LOG%" echo Clean   : done
  )
) else (
  echo        Nothing to clean - already fresh.
  >> "%LOG%" echo Clean   : nothing to do
)
if defined CLEANFAIL (
  echo        %C_WARN%WARNING:%C_RESET% could not fully delete %OUTDIR% - some files were
  echo                 locked by another program, so old files may remain. Close any open
  echo                 Tempo window and any Explorer window on that folder, then run
  echo                 publish.cmd again for a truly clean build.
  >> "%LOG%" echo Clean   : WARNING - OUTDIR not fully removed ^(locked files^)
)
:afterClean
call :SecsBetween "%TP3%" "%TIME: =0%" PH3S

REM ===========================================================================
REM  PHASE 4 of 7  -  Build  (this is the slow part)
REM ===========================================================================
echo.
echo  %C_STEP%[4/7]%C_RESET% Building Tempo - this can take a minute or two ...
echo        ^(compiling and embedding the .NET runtime into one .exe^)
echo        Full output is captured to publish-log.txt.
echo.
set "TP4=%TIME: =0%"

REM Any running Tempo (including a portable copy launched from this same output folder)
REM was already stopped at the start of phase 3, before the clean, so dotnet can
REM overwrite Tempo.exe here. We deliberately do NOT delete the existing exe ourselves:
REM if the build fails, the previous working exe stays in place so you can still run the
REM old version instead of being left with nothing.

set "PSOUT=%TEMP%\tempo_build_out.txt"
set "PSERR=%TEMP%\tempo_build_err.txt"
del "%PSOUT%" "%PSERR%" >nul 2>&1

REM Run the build through PowerShell so a live comet + elapsed timer is shown
REM while dotnet works (its output is captured to the log). In /ci mode, or if
REM PowerShell isn't available, fall back to a plain build straight to the log.
set "USEPLAIN="
if defined OPT_CI set "USEPLAIN=1"
where powershell >nul 2>&1
if errorlevel 1 set "USEPLAIN=1"

if defined USEPLAIN (
  if not defined OPT_CI echo        ^(PowerShell not found - building without the live spinner^)
  dotnet publish AutoClicker.csproj -c Release -r %RID% -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false -o "%OUTDIR%" >> "%LOG%" 2>&1
  set "RESULT=!ERRORLEVEL!"
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='SilentlyContinue'; $a=@('publish','AutoClicker.csproj','-c','Release','-r','%RID%','-p:SelfContained=true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:IncludeAllContentForSelfExtract=true','-p:EnableCompressionInSingleFile=true','-p:PublishReadyToRun=true','-p:DebugType=none','-p:DebugSymbols=false','-o','%OUTDIR%'); $p=Start-Process dotnet -ArgumentList $a -NoNewWindow -PassThru -RedirectStandardOutput '%PSOUT%' -RedirectStandardError '%PSERR%'; $w=22; $pos=0; $t=[Diagnostics.Stopwatch]::StartNew(); while(-not $p.HasExited){ $a=@(); for($k=0;$k -lt $w;$k++){ $a+='.' }; $a[$pos %% $w]='#'; $a[($pos-1+$w) %% $w]='='; $a[($pos-2+$w) %% $w]=':'; $bar=-join $a; [Console]::Write([char]13 + '        Building  [' + $bar + ']  ' + ('{0:mm\:ss}' -f $t.Elapsed) + ' elapsed   '); $pos++; Start-Sleep -Milliseconds 70 }; [Console]::Write([char]13 + (' '*72) + [char]13); Get-Content '%PSOUT%','%PSERR%' -ErrorAction SilentlyContinue | Add-Content '%LOG%'; exit $p.ExitCode"
  set "RESULT=!ERRORLEVEL!"
)
del "%PSOUT%" "%PSERR%" >nul 2>&1

>> "%LOG%" echo ---------------------------------------------------------------------------
>> "%LOG%" echo Finished: %DATE% %TIME%
call :SecsBetween "%TP4%" "%TIME: =0%" PH4S

if not "%RESULT%"=="0" (
  >> "%LOG%" echo RESULT  : FAILED ^(dotnet exit code %RESULT%^)
  echo.
  echo  %C_ERR%===========================================================================%C_RESET%
  echo    %C_ERR%BUILD FAILED%C_RESET%  ^(exit code %RESULT%^)
  echo  ---------------------------------------------------------------------------
  echo    Lines containing "error" from the build output:
  echo.
  findstr /I /C:"error" "%LOG%"
  echo.
  echo  ---------------------------------------------------------------------------
  echo    Things that fix most build failures:
  echo      - Make sure the .NET 8 SDK is installed ^(run  dotnet --list-sdks  and
  echo        look for an 8.x entry^); a newer-only SDK can still build it, an
  echo        older one cannot.
  echo      - Make sure you run this from the project folder ^(AutoClicker.csproj
  echo        next to this script^).
  echo      - If errors mention locked files, close any running Tempo.exe and
  echo        any editor holding bin\ or obj\ open, then run again.
  echo      - If errors look like stale-cache weirdness, run without /quick so
  echo        the clean phase wipes obj\ and bin\Release.
  echo      - Full output: "%LOG%"
  echo  ===========================================================================
  endlocal
  exit /b 2
)

>> "%LOG%" echo RESULT  : SUCCESS
echo        %CHK%[OK]%C_RESET% Build succeeded.

REM ===========================================================================
REM  PHASE 5 of 7  -  Verify the output
REM ===========================================================================
echo.
echo  %C_STEP%[5/7]%C_RESET% Verifying the output ...
echo        %C_DIM%Confirming the exe exists, is a sane size, has a checksum, and reports the right version.%C_RESET%
set "TP5=%TIME: =0%"

if not exist "%EXE%" (
  >> "%LOG%" echo RESULT  : NO OUTPUT ^(Tempo.exe not found at %EXE%^)
  echo        %C_ERR%ERROR:%C_RESET% build reported success but Tempo.exe was not found:
  echo           %EXE%
  echo        Check the log for details: "%LOG%"
  endlocal
  exit /b 3
)

set "SIZE="
for %%F in ("%EXE%") do set "SIZE=%%~zF"
>> "%LOG%" echo Output  : %EXE% ^(%SIZE% bytes^)
echo        %CHK%[OK]%C_RESET% Found Tempo.exe ^(%SIZE% bytes^).

REM A self-contained single-file WinForms app is never tiny. If it is, the
REM runtime probably wasn't embedded and the build flags need a look.
set "SIZEMB="
set /a SIZEMB=%SIZE%/1048576 >nul 2>&1
if defined SIZEMB if !SIZEMB! lss 8 (
  echo        %C_WARN%WARNING:%C_RESET% %SIZE% bytes is suspiciously small for a self-contained
  echo                 single-file build. Double-check it runs on a machine
  echo                 without .NET installed before shipping.
)

echo        Computing SHA-256 checksum ...
set "SHA="
rem  Get-FileHash, not certutil: certutil prints LOWERCASE, and every release up to 1.0.318
rem  shipped the uppercase form. Hash comparison is case-insensitive, but the file should
rem  not change shape between releases for no reason.
for /f "usebackq delims=" %%H in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-FileHash -LiteralPath '%EXE%' -Algorithm SHA256).Hash"`) do if not defined SHA set "SHA=%%H"
if defined SHA >> "%LOG%" echo SHA-256 : %SHA%
if defined SHA (
  > "%EXE%.sha256" echo %SHA% *Tempo.exe
  echo        %CHK%[OK]%C_RESET% Wrote checksum file: %EXE%.sha256
)

REM Catch the classic mistake: version bumped in the csproj but an old exe
REM is about to be attached to the release.
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
    echo        %CHK%[OK]%C_RESET% Verified Tempo.exe is version %VER%.
  )
)

REM The offline speech-caption engine needs the Whisper native libraries, and a broken
REM or partial NuGet restore can still produce a runnable exe whose captions are
REM silently dead. So confirm they are actually in the build. (Non-fatal: the rest of
REM the app works either way.)
REM
REM LOOK INSIDE THE EXE, NOT BESIDE IT. This used to move "*whisper*.dll"/"*ggml*.dll"
REM out of the publish folder into runtimes\<rid>\native and then check that folder --
REM but with PublishSingleFile + IncludeNativeLibrariesForSelfExtract the natives are
REM bundled INTO Tempo.exe and never appear loose at all. Measured: a publish of this
REM project emits exactly ONE file. So the move matched nothing, the folder it made was
REM always empty, and the check that read it reported "MISSING" on every single build --
REM a warning that could not ever have been true. (The 200-odd loose DLLs that used to
REM sit in bin\publish were debris from older builds, left behind because the clean
REM phase was itself broken; they are gone now.)
REM
REM "ggml" is the token to search for, not "whisper": Whisper.net.dll is MANAGED and is
REM bundled regardless, so its name appears even when the native restore failed. ggml is
REM native-only, so finding it means the native libs really are in there. findstr reads
REM the binary happily; verified it matches ggml and does not match a made-up name.
set "NATIVE="
findstr /m /c:"ggml" "%EXE%" >nul 2>&1 && set "NATIVE=1"
if defined NATIVE (
  echo        %CHK%[OK]%C_RESET% Native speech libraries bundled inside Tempo.exe.
  >> "%LOG%" echo Native  : present ^(bundled in the single-file exe^)
) else (
  echo        %C_WARN%WARNING:%C_RESET% no native speech libraries ^(whisper/ggml^) were found
  echo                 inside Tempo.exe. The app runs, but its offline Live Captions
  echo                 won't work - check that Whisper.net.Runtime restored.
  >> "%LOG%" echo Native  : MISSING ^(no ggml payload inside the exe^)
)

REM --- Tidy: remove redundant loose framework assemblies -----------------------
REM A self-contained single-file Tempo.exe bundles the ENTIRE .NET runtime + WPF +
REM the managed app INSIDE the exe, so a real one is large (well over 100 MB). When
REM that's the case, any loose *managed* framework DLLs next to it (System.*,
REM PresentationFramework*, NAudio*, ...) are duplicates already inside Tempo.exe, or
REM leftovers from an older non-single-file build - so they're removed to leave a
REM clean folder. This ONLY runs when Tempo.exe is genuinely large; if it's small,
REM single-file did NOT bundle and the app NEEDS those DLLs, so we keep them and warn.
REM The native runtime files (coreclr, clrjit, the *_cor3 WPF natives, vcruntime, ...)
REM are NOT managed - they must stay beside a single-file exe - so they're never touched.
set "BIGEXE="
if defined SIZE if %SIZE% GEQ 41943040 set "BIGEXE=1"
if defined BIGEXE (
  rem  del expands its OWN wildcards, against the folder it runs in - which is why
  rem  this pushes into OUTDIR first. The previous version looped a "for %%M in ..."
  rem  set, and a for-set expands wildcards against the CURRENT directory instead:
  rem  the repo root. Every pattern that matched nothing there yielded nothing, so
  rem  its del never ran, and the script still printed that it had pruned them.
  rem  Measured after a full publish on 2026-08-31: the literal names were gone,
  rem  System.*.dll had left 186 files behind and NAudio.*.dll all of them.
  pushd "%OUTDIR%" >nul 2>&1
  if not errorlevel 1 (
    del /f /q System.*.dll Microsoft.CSharp.dll Microsoft.VisualBasic.dll >nul 2>&1
    del /f /q Microsoft.VisualBasic.Core.dll Microsoft.VisualBasic.Forms.dll >nul 2>&1
    del /f /q Microsoft.Win32.Primitives.dll Microsoft.Win32.Registry.dll >nul 2>&1
    del /f /q Microsoft.Win32.Registry.AccessControl.dll Microsoft.Win32.SystemEvents.dll >nul 2>&1
    del /f /q PresentationCore.dll PresentationFramework.dll PresentationFramework-*.dll >nul 2>&1
    del /f /q PresentationFramework.Aero.dll PresentationFramework.Aero2.dll >nul 2>&1
    del /f /q PresentationFramework.AeroLite.dll PresentationFramework.Classic.dll >nul 2>&1
    del /f /q PresentationFramework.Luna.dll PresentationFramework.Royale.dll >nul 2>&1
    del /f /q PresentationUI.dll ReachFramework.dll WindowsBase.dll >nul 2>&1
    del /f /q WindowsFormsIntegration.dll UIAutomationClient.dll >nul 2>&1
    del /f /q UIAutomationClientSideProviders.dll UIAutomationProvider.dll >nul 2>&1
    del /f /q UIAutomationTypes.dll Accessibility.dll netstandard.dll mscorlib.dll >nul 2>&1
    del /f /q DirectWriteForwarder.dll NAudio.dll NAudio.*.dll Whisper.net.dll >nul 2>&1
    popd
  )
  echo        %CHK%[OK]%C_RESET% Single-file bundle confirmed; removed redundant loose managed DLLs.
  rem  Parens ESCAPED. Unescaped, that ")" closes the enclosing "if defined BIGEXE ("
  rem  early, so cmd ran the else-branch too — the build logged BOTH "pruned redundant
  rem  managed DLLs" and "SKIPPED prune (exe too small)" for the same 101 MB exe, and
  rem  printed a warning about a file that was perfectly fine. The truncated log line
  rem  (it ended at "bytes" with no closing paren) is what gave it away.
  >> "%LOG%" echo Tidy    : pruned redundant managed DLLs ^(bundle %SIZE% bytes^)
) else (
  echo        %C_WARN%WARNING:%C_RESET% Tempo.exe is only %SIZE% bytes - single-file may not have
  echo                 bundled the runtime, so loose DLLs were KEPT ^(the app needs them^).
  echo                 Check the build log; only the folder tidiness is affected.
  >> "%LOG%" echo Tidy    : SKIPPED prune ^(exe too small; single-file may have failed^)
)
call :SecsBetween "%TP5%" "%TIME: =0%" PH5S
echo        %C_OK%All checks passed.%C_RESET%

REM ===========================================================================
REM  PHASE 6 of 7  -  Package: installer bundle, checksums, release notes
REM ===========================================================================
echo.
echo  %C_STEP%[6/7]%C_RESET% Packaging ...
echo        %C_DIM%Bundling the installer, uninstaller, checksums and release notes into the setup zip.%C_RESET%
set "TP6=%TIME: =0%"

if exist "%ROOT%install.cmd"   copy /y "%ROOT%install.cmd"   "%OUTDIR%\install.cmd"   >nul
if exist "%ROOT%uninstall.cmd" copy /y "%ROOT%uninstall.cmd" "%OUTDIR%\uninstall.cmd" >nul

REM A tiny read-me that ships inside the setup zip, for people who unzip first
REM and wonder what to do.
(
  echo Tempo %VER%  -  two ways to run
  echo --------------------------------------
  echo Keep all of these files together in one folder.
  echo.
  echo PORTABLE ^(no install^):
  echo   Just double-click Tempo.exe. It runs in place and keeps its settings,
  echo   profiles and macros in a "Data" folder right here, so you can move the
  echo   whole folder ^(USB stick, another PC^) and it works anywhere.
  echo.
  echo INSTALLED ^(Start Menu + clean uninstall^):
  echo   Double-click install.cmd ^(no administrator rights needed^), then launch
  echo   Tempo from the Start Menu. Uninstall later from Settings ^> Apps, or with
  echo   uninstall.cmd.
  echo.
  echo Either way, keep the "runtimes" folder next to Tempo.exe
  echo ^(it holds the offline speech-caption engine^).
  echo.
  echo Windows may warn "Unknown publisher" because the app is not
  echo code-signed. Choose "More info" then "Run anyway". You can verify
  echo the download with:  certutil -hashfile Tempo.exe SHA256
) > "%OUTDIR%\INSTALL-README.txt"
echo        Wrote INSTALL-README.txt into the bundle.

set "SETUPZIP=bin\publish\%VER%.zip"
set "MADEZIP="
if defined OPT_NOZIP (
  echo        Setup zip skipped ^(/nozip^).
  >> "%LOG%" echo Zip     : skipped ^(/nozip^)
  goto :afterZip
)
where powershell >nul 2>&1
if not errorlevel 1 (
  if exist "%OUTDIR%\install.cmd" (
    if exist "%SETUPZIP%" del /f /q "%SETUPZIP%" >nul 2>&1
    rem  An EXPLICIT file list, not "%OUTDIR%\*".
    rem
    rem  The wildcard swept up whatever the publish folder happened to contain, and
    rem  with a self-contained single-file build that is a lot of things users must
    rem  never be asked to download: 250 entries, 169 MB, including framework DLLs
    rem  already bundled inside Tempo.exe and a 21 MB LINUX .so
    rem  (runtimes\vulkan\linux-x64\libggml-vulkan-whisper.so) in a Windows release.
    rem  The prune in phase 5 trims some of that, but it is a deny-list and so is
    rem  always one new dependency behind.
    rem
    rem  These five files ARE the bundle, exactly as the OUTPUT section at the top of
    rem  this script documents. Naming them makes the zip correct by construction
    rem  rather than by remembering to exclude things.
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$f=@('Tempo.exe','Tempo.exe.sha256','install.cmd','uninstall.cmd','INSTALL-README.txt') | ForEach-Object { Join-Path '%OUTDIR%' $_ } | Where-Object { Test-Path $_ }; Compress-Archive -Path $f -DestinationPath '%SETUPZIP%' -Force" >nul 2>&1
    if exist "%SETUPZIP%" set "MADEZIP=1"
  )
)
if defined MADEZIP (
  echo        Built installer package: %SETUPZIP%
  >> "%LOG%" echo Zip     : %SETUPZIP%
) else (
  echo        Installer scripts copied next to Tempo.exe.
)
:afterZip

REM Combined CHECKSUMS.txt: exe + setup zip, ready to paste into the release.
set "ZIPSHA="
if defined MADEZIP (
  for /f "usebackq delims=" %%H in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-FileHash -LiteralPath '%SETUPZIP%' -Algorithm SHA256).Hash"`) do if not defined ZIPSHA set "ZIPSHA=%%H"
)
set "SUMS=bin\publish\CHECKSUMS.txt"
(
  echo Tempo %VER%  -  SHA-256 checksums
  echo Generated %DATE% %TIME%
  echo ---------------------------------------------------------------
  if defined SHA    echo %SHA%  Tempo.exe
  if defined ZIPSHA echo %ZIPSHA%  %VER%.zip
  echo.
  echo Verify a download with:
  echo   certutil -hashfile ^<file^> SHA256
) > "%SUMS%"
echo        Wrote combined checksums: %SUMS%
if defined ZIPSHA >> "%LOG%" echo ZipSHA  : %ZIPSHA%

REM Stage the standalone Tempo.exe (and its checksum) next to the other release
REM artifacts in bin\publish so all the files to upload sit in ONE folder. Attaching
REM this bare Tempo.exe to the release is what powers the in-app one-click updater:
REM UpdateChecker prefers an .exe asset, and only then does "Update now" (install in
REM place) appear instead of just an "Open download page" button.
if exist "%EXE%" (
  copy /y "%EXE%" "bin\publish\Tempo.exe" >nul 2>&1
  if exist "%EXE%.sha256" copy /y "%EXE%.sha256" "bin\publish\Tempo.exe.sha256" >nul 2>&1
  echo        Staged standalone exe for release: bin\publish\Tempo.exe
  >> "%LOG%" echo ExeCopy : bin\publish\Tempo.exe
)

REM If this version's release notes exist, copy them next to the artifacts so
REM attaching/pasting them is one step instead of a hunt.
set "NOTES=%ROOT%release-notes\%VER%.md"
set "NOTESOUT="
if exist "%NOTES%" (
  copy /y "%NOTES%" "bin\publish\RELEASE-NOTES-%VER%.md" >nul
  set "NOTESOUT=bin\publish\RELEASE-NOTES-%VER%.md"
  echo        Release notes found and copied: !NOTESOUT!
  >> "%LOG%" echo Notes   : !NOTESOUT!
) else (
  echo        ^(No release-notes\%VER%.md found - skipping notes copy.^)
)
call :SecsBetween "%TP6%" "%TIME: =0%" PH6S

REM ===========================================================================
REM  PHASE 7 of 7  -  Summary
REM ===========================================================================
set "T1=%TIME: =0%"
call :SecsBetween "%T0%" "%T1%" ELAPSED
set /a "EMIN=ELAPSED/60"
set /a "ESEC=ELAPSED%%60"
>> "%LOG%" echo Elapsed : !EMIN!m !ESEC!s

set "ZIPSIZE="
if defined MADEZIP for %%Z in ("%SETUPZIP%") do set "ZIPSIZE=%%~zZ"
set "ZIPMB="
if defined ZIPSIZE set /a ZIPMB=!ZIPSIZE!/1048576 >nul 2>&1

echo.
REM Celebratory animated sweep on success: a green-to-cyan gradient bar (skipped in
REM CI/plain mode; no-op without PowerShell).
if not defined OPT_CI (
  where powershell >nul 2>&1 && powershell -NoProfile -ExecutionPolicy Bypass -Command "$e=[char]27; $b=[char]0x2588; $w=75; for($x=0;$x -lt $w;$x++){ $t=$x/[Math]::Max(1,$w-1); $rr=[int](52+(96-52)*$t); $gg=[int](211+(165-211)*$t); $bb=[int](153+(250-153)*$t); [Console]::Write($e+'[38;2;'+$rr+';'+$gg+';'+$bb+'m'+$b); Start-Sleep -Milliseconds 6 }; [Console]::Write($e+'[0m'); Write-Host ''"
)
echo  %C_OK%===========================================================================%C_RESET%
echo    %C_OK%DONE%C_RESET%  -  Tempo %C_OK%%VER%%C_RESET% built in !EMIN!m !ESEC!s
echo  ---------------------------------------------------------------------------
echo    Phase times:  env !PH1S!s ^| clean !PH3S!s ^| build !PH4S!s ^| verify !PH5S!s ^| package !PH6S!s
echo  ---------------------------------------------------------------------------
echo    %C_TITLE%ARTIFACTS%C_RESET%
echo      Output dir : %OUTDIR%
echo      Executable : %EXE%
if defined SIZEMB (echo      Size       : %SIZE% bytes  ^(~!SIZEMB! MB^)) else (echo      Size       : %SIZE% bytes)
if defined SHA echo      SHA-256    : %SHA%
if defined SHA echo      Checksum   : %EXE%.sha256
if defined MADEZIP echo      Installer  : %SETUPZIP%  ^(~!ZIPMB! MB^)
echo      Checksums  : %SUMS%
if defined NOTESOUT echo      Notes      : !NOTESOUT!
echo      Build log  : publish-log.txt
if defined DOTNETVER echo      Built with : .NET SDK %DOTNETVER%
echo      Target     : %RID%  ^(self-contained single file; everything bundled, runs anywhere^)
echo      App version: %VER%
echo  ---------------------------------------------------------------------------
echo    %C_TITLE%NEXT STEPS%C_RESET%
echo      0. Run the built Tempo.exe once and click around ^(start/stop a click,
echo         switch tabs, open a macro^) to confirm it works before you ship it.
echo      1. Create a GitHub release tagged  v%VER%
echo      2. Attach BOTH of these so every user is covered:
echo         - Tempo.exe ^(+ Tempo.exe.sha256^)  - REQUIRED for the in-app one-click
echo           updater. With it attached, existing users get an "Update now" button
echo           that downloads and installs in place; without it they only get a
echo           download-page link. Both files are now in bin\publish next to the zip.
echo         - %VER%.zip  - for brand-new users. Serves both ways:
echo           INSTALLED ^(unzip, run install.cmd: shortcuts + uninstall entry, no
echo           admin^) or PORTABLE ^(unzip anywhere, run Tempo.exe; keep the runtimes
echo           folder beside it^). Either way, settings live in your local AppData
echo           ^(the AutoClicker folder^), not beside the exe.
if defined NOTESOUT echo      3. Paste !NOTESOUT! as the release description
echo      4. No .NET install needed for users either way
echo    ^(Windows may warn "Unknown publisher" because it isn't code-signed;
echo     choose More info ^> Run anyway.^)
echo  ---------------------------------------------------------------------------
call :PrintTip
echo  %C_OK%===========================================================================%C_RESET%
echo.

if defined OPT_OPEN (
  start "" explorer "%OUTDIR%"
  goto :done
)
if defined OPT_NOPROMPT goto :done

choice /c YN /n /m "Open the output folder now? [Y/N] "
if errorlevel 2 goto :done
start "" explorer "%OUTDIR%"

:done
endlocal
exit /b 0

REM ===========================================================================
REM  Help screen
REM ===========================================================================
:help
echo.
echo   TEMPO  -  publish.cmd
echo   Build a self-contained, single-file Windows release of Tempo and
echo   package everything a GitHub release needs.
echo.
echo   USAGE
echo     publish.cmd [rid] [flags]
echo.
echo   RUNTIME ID ^(rid^)
echo     win-x64     64-bit Intel/AMD  ^(default^)
echo     win-x86     32-bit, for old machines
echo     win-arm64   ARM64 Windows
echo.
echo   FLAGS
rem  "echo:" here, not "echo ". ECHO scans its argument for /? and prints its OWN
rem  usage when it finds one — so the line documenting Tempo's /? flag replaced
rem  itself with "Displays messages, or turns command-echoing on or off." in the
rem  middle of the flag list. Carets do not help (^? and a %variable% both still
rem  trigger it, tested); the colon form is the one that suppresses the check.
echo:    /help, /?   Show this help and exit.
echo     /quick      Skip the clean phase ^(faster incremental build^).
echo     /nozip      Do not build the setup zip bundle.
echo     /ci         Plain output: no banner, no spinner, no prompts.
echo     /open       Open the output folder when done.
echo     /noprompt   Never ask anything ^(keeps colourful output^).
echo.
echo   EXAMPLES
echo     publish.cmd
echo     publish.cmd win-x86
echo     publish.cmd /quick /open
echo     publish.cmd win-arm64 /ci
echo.
echo   OUTPUT  ^(under bin\publish^)
echo     ^<rid^>\Tempo.exe              self-contained single file
echo     ^<rid^>\Tempo.exe.sha256       checksum for the exe
echo     ^<rid^>\install.cmd            per-user installer
echo     ^<rid^>\uninstall.cmd          matching uninstaller
echo     ^<rid^>\INSTALL-README.txt     how-to-install note
echo     ^<ver^>.zip                    the bundle users download
echo     CHECKSUMS.txt                SHA-256 of the exe and the zip
echo     RELEASE-NOTES-^<ver^>.md       copied if release-notes\^<ver^>.md exists
echo.
echo   EXIT CODES
echo     0 success   1 SDK missing   2 build failed   3 verify failed   4 usage
echo.
endlocal
exit /b 0

REM ===========================================================================
REM  Subroutines
REM ===========================================================================

REM :SecsBetween  "<hh:mm:ss.cc>" "<hh:mm:ss.cc>"  OUTVAR
REM   Difference in whole seconds between two %TIME% stamps (midnight-safe).
:SecsBetween
setlocal
set "A=%~1"
set "B=%~2"
for /f "tokens=1-4 delims=:.," %%a in ("%A%") do set /a "SA=(((1%%a-100)*60)+(1%%b-100))*60+(1%%c-100)"
for /f "tokens=1-4 delims=:.," %%a in ("%B%") do set /a "SB=(((1%%a-100)*60)+(1%%b-100))*60+(1%%c-100)"
set /a "D=SB-SA"
if %D% lss 0 set /a "D+=86400"
endlocal & set "%~3=%D%"
goto :eof

REM :PrintTip  -  one rotating, genuinely useful tip per run.
:PrintTip
set /a "TIPN=%RANDOM% %% 12"
if "%TIPN%"=="0"  echo    Tip: publish.cmd /quick skips the clean phase for fast iteration builds.
if "%TIPN%"=="1"  echo    Tip: publish.cmd /ci gives plain, prompt-free output for scripts.
if "%TIPN%"=="2"  echo    Tip: users can verify downloads with  certutil -hashfile Tempo.exe SHA256
if "%TIPN%"=="3"  echo    Tip: the version lives in 4 places - csproj, AboutForm, Program.cs, CHANGELOG.
if "%TIPN%"=="4"  echo    Tip: publish.cmd win-x86 builds for old 32-bit machines.
if "%TIPN%"=="5"  echo    Tip: CHECKSUMS.txt is ready to paste into the GitHub release body.
if "%TIPN%"=="6"  echo    Tip: weird build errors? Run without /quick to wipe obj\ and bin\Release.
if "%TIPN%"=="7"  echo    Tip: the website shows the newest release automatically - just publish it.
if "%TIPN%"=="8"  echo    Tip: tag releases exactly  v%VER%  so the update checker can find them.
if "%TIPN%"=="9"  echo    Tip: publish.cmd /open jumps straight to the output folder when done.
if "%TIPN%"=="10" echo    Tip: attach BOTH the setup zip and the bare exe - users differ.
if "%TIPN%"=="11" echo    Tip: publish-log.txt holds the full compiler output from this run.
goto :eof
