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
echo  [3/5] Cleaning previous output ...
if exist "%OUTDIR%" (
  rd /s /q "%OUTDIR%"
  echo        Removed old %OUTDIR%.
) else (
  echo        Nothing to clean.
)

REM ===========================================================================
REM  STEP 4 of 5  -  Build  (this is the slow part)
REM ===========================================================================
echo.
echo  [4/5] Building Tempo - this can take a minute or two, please wait ...
echo        (compiling, embedding the .NET runtime, compressing into one .exe)
echo        Live progress is being written to publish-log.txt.

dotnet publish AutoClicker.csproj -c Release -r %RID% ^
  -p:SelfContained=true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:PublishReadyToRun=true ^
  -p:DebugType=none ^
  -p:DebugSymbols=false ^
  -o "%OUTDIR%" >> "%LOG%" 2>&1

set "RESULT=%ERRORLEVEL%"

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
echo    Build log  : publish-log.txt
echo  ---------------------------------------------------------------------------
echo    Next steps:
echo      1. Create a GitHub release tagged  v%VER%
echo      2. Attach this single Tempo.exe to the release
echo      3. Users run it directly - no .NET install needed
echo    (Windows may warn "Unknown publisher" because it isn't code-signed;
echo     choose More info ^> Run anyway.)
echo  ===========================================================================
echo.

choice /c YN /n /m "Open the output folder now? [Y/N] "
if errorlevel 2 goto :done
start "" explorer "%OUTDIR%"

:done
endlocal
