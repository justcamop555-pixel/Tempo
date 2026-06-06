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
REM  Log:     publish-log.txt        - full build output + final result, written
REM           every run so you can review any errors after it finishes.
REM ===========================================================================
setlocal EnableExtensions EnableDelayedExpansion

set "RID=%~1"
if "%RID%"=="" set "RID=win-x64"

set "OUTDIR=bin\publish\%RID%"
set "EXE=%OUTDIR%\Tempo.exe"
set "LOG=%~dp0publish-log.txt"

REM --- Read the version from the project so it can be shown/confirmed ---------
set "VER="
for /f "tokens=2 delims=<>" %%V in ('findstr /i "<Version>" "%~dp0AutoClicker.csproj"') do if not defined VER set "VER=%%V"
if not defined VER set "VER=unknown"

REM --- Start a fresh log ------------------------------------------------------
> "%LOG%" echo Tempo publish log
>> "%LOG%" echo Started : %DATE% %TIME%
>> "%LOG%" echo Version : %VER%
>> "%LOG%" echo Runtime : %RID%
>> "%LOG%" echo ---------------------------------------------------------------------------

echo.
echo  Building Tempo %VER%  (%RID%) ...
echo  (Make sure you bumped the version before building, then tag the release v%VER%.)

REM --- Make sure the .NET SDK is available before we start --------------------
where dotnet >nul 2>&1
if errorlevel 1 (
  echo.
  echo ERROR: The .NET SDK was not found on your PATH.
  echo Install the .NET 8 SDK from https://dotnet.microsoft.com/download
  echo then run this script again.
  >> "%LOG%" echo RESULT  : FAILED - .NET SDK not found on PATH.
  echo.
  echo A log was written to "%LOG%".
  endlocal
  exit /b 1
)

echo.
echo ===========================================================================
echo  Publishing Tempo  ^|  runtime: %RID%  ^|  self-contained single file
echo  (full output is also being saved to publish-log.txt)
echo ===========================================================================
echo.

REM --- Remove any previous output so nothing stale is left behind ------------
if exist "%OUTDIR%" rd /s /q "%OUTDIR%"

REM --- Build. Send all output to the log, then show it so the user sees it ----
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

REM --- Echo the captured build output to the console --------------------------
type "%LOG%"

>> "%LOG%" echo ---------------------------------------------------------------------------
>> "%LOG%" echo Finished: %DATE% %TIME%

if not "%RESULT%"=="0" (
  >> "%LOG%" echo RESULT  : FAILED ^(dotnet exit code %RESULT%^)
  echo.
  echo ===========================================================================
  echo  BUILD FAILED ^(exit code %RESULT%^).
  echo  The errors are saved in: "%LOG%"
  echo  Lines containing "error":
  echo ---------------------------------------------------------------------------
  findstr /I /C:"error" "%LOG%"
  echo ===========================================================================
  endlocal
  exit /b %RESULT%
)

>> "%LOG%" echo RESULT  : SUCCESS

REM --- Sanity check: the build reported success, but did we get the exe? ------
if not exist "%EXE%" (
  >> "%LOG%" echo RESULT  : NO OUTPUT ^(Tempo.exe not found at %EXE%^)
  echo.
  echo ===========================================================================
  echo  BUILD REPORTED SUCCESS, BUT Tempo.exe WAS NOT FOUND:
  echo     %EXE%
  echo  Check the log for details: "%LOG%"
  echo ===========================================================================
  endlocal
  exit /b 1
)

for %%F in ("%EXE%") do >> "%LOG%" echo Output  : %%~fF ^(%%~zF bytes^)

REM --- Compute a SHA-256 checksum so you can publish it for integrity ---------
set "SHA="
for /f "skip=1 tokens=* delims=" %%H in ('certutil -hashfile "%EXE%" SHA256 2^>nul') do if not defined SHA set "SHA=%%H"
if defined SHA (
  >> "%LOG%" echo SHA-256 : %SHA%
)

echo.
echo ===========================================================================
echo  Done. Tempo %VER% built. Your distributable executable is here:
echo     %EXE%
for %%F in ("%EXE%") do echo     size: %%~zF bytes
if defined SHA echo     SHA-256: %SHA%
echo  Build log: "%LOG%"
echo.
echo  Next: create a GitHub release tagged v%VER% and attach this Tempo.exe.
echo  Share that single .exe - users can run it without installing .NET.
echo  (Windows may warn "Unknown publisher" because it isn't code-signed;
echo   choose More info ^> Run anyway.)
echo ===========================================================================
echo.

choice /c YN /n /m "Open the output folder now? [Y/N] "
if errorlevel 2 goto :done
start "" explorer "%OUTDIR%"

:done
endlocal
