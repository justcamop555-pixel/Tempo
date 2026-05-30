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
REM ===========================================================================
setlocal

set RID=%1
if "%RID%"=="" set RID=win-x64

echo.
echo Publishing Tempo (self-contained, single file) for %RID% ...
echo.

dotnet publish AutoClicker.csproj -c Release -r %RID% ^
  -p:SelfContained=true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o "bin\publish\%RID%"

if errorlevel 1 (
  echo.
  echo BUILD FAILED.
  exit /b 1
)

echo.
echo Done. Your distributable executable is here:
echo    bin\publish\%RID%\Tempo.exe
echo.
echo Share that single .exe - users can run it without installing .NET.
endlocal
