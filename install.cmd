@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Tempo Installer

REM ===========================================================================
REM  Tempo installer  -  per-user, no administrator rights needed.
REM  Run this from the same folder as Tempo.exe. It copies Tempo into your
REM  user profile, creates Start Menu (and optional Desktop) shortcuts, and
REM  registers an entry in Settings > Apps so it can be uninstalled normally.
REM ===========================================================================

set "SRC=%~dp0"
set "INSTALL_DIR=%LOCALAPPDATA%\Programs\Tempo"
set "EXE=%INSTALL_DIR%\Tempo.exe"
set "UNINST=%INSTALL_DIR%\uninstall.cmd"
set "SM_LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Tempo.lnk"
set "DESK_LNK=%USERPROFILE%\Desktop\Tempo.lnk"
set "REGKEY=HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Tempo"

echo(
echo   ==================================================
echo     Installing Tempo
echo   ==================================================
echo(

if not exist "%SRC%Tempo.exe" (
  echo   ERROR: Tempo.exe was not found next to this installer.
  echo   Keep install.cmd and Tempo.exe together in the same folder.
  echo(
  pause
  exit /b 1
)

echo   Installing to:
echo     %INSTALL_DIR%
echo(

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%" >nul 2>&1
copy /y "%SRC%Tempo.exe" "%EXE%" >nul
if not exist "%EXE%" (
  echo   ERROR: Could not copy Tempo.exe into place.
  echo(
  pause
  exit /b 1
)
if exist "%SRC%Tempo.exe.sha256" copy /y "%SRC%Tempo.exe.sha256" "%INSTALL_DIR%\Tempo.exe.sha256" >nul
if exist "%SRC%uninstall.cmd" copy /y "%SRC%uninstall.cmd" "%UNINST%" >nul

REM --- best-effort: read the version from the exe for the uninstall entry ---
set "VER=1.0"
where powershell >nul 2>&1 && for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item '%EXE%').VersionInfo.FileVersion" 2^>nul`) do set "VER=%%v"

echo   Creating Start Menu shortcut ...
call :MakeShortcut "%SM_LNK%" "%EXE%" "%INSTALL_DIR%"

echo(
set /p DESK=  Create a Desktop shortcut too? [Y/N] 
if /i "!DESK!"=="Y" (
  call :MakeShortcut "%DESK_LNK%" "%EXE%" "%INSTALL_DIR%"
  echo   Desktop shortcut created.
)

REM --- register in Settings > Apps (per-user, no admin) ---
reg add "%REGKEY%" /v DisplayName /t REG_SZ /d "Tempo" /f >nul
reg add "%REGKEY%" /v DisplayVersion /t REG_SZ /d "!VER!" /f >nul
reg add "%REGKEY%" /v Publisher /t REG_SZ /d "Tempo" /f >nul
reg add "%REGKEY%" /v InstallLocation /t REG_SZ /d "%INSTALL_DIR%" /f >nul
reg add "%REGKEY%" /v DisplayIcon /t REG_SZ /d "%EXE%" /f >nul
reg add "%REGKEY%" /v UninstallString /t REG_SZ /d "\"%UNINST%\"" /f >nul
reg add "%REGKEY%" /v NoModify /t REG_DWORD /d 1 /f >nul
reg add "%REGKEY%" /v NoRepair /t REG_DWORD /d 1 /f >nul

echo(
echo   Done - Tempo !VER! is installed.
echo   Launch it from the Start Menu. To remove it later, use
echo   Settings ^> Apps, or run uninstall.cmd in the folder above.
echo(
set /p RUN=  Launch Tempo now? [Y/N] 
if /i "!RUN!"=="Y" start "" "%EXE%"
echo(
exit /b 0

REM ---------------------------------------------------------------------------
:MakeShortcut
REM   %~1 = shortcut path   %~2 = target exe   %~3 = working directory
where powershell >nul 2>&1
if errorlevel 1 (
  echo   ^(PowerShell unavailable - skipped shortcut %~nx1^)
  goto :eof
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$w=New-Object -ComObject WScript.Shell; $s=$w.CreateShortcut('%~1'); $s.TargetPath='%~2'; $s.WorkingDirectory='%~3'; $s.IconLocation='%~2,0'; $s.Description='Tempo'; $s.Save()" >nul 2>&1
goto :eof
