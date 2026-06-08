@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Tempo Uninstaller

REM ===========================================================================
REM  Removes Tempo's shortcuts, its Settings > Apps entry, and (after this
REM  window closes) the installed program files. Optionally removes your saved
REM  settings/profiles too.
REM ===========================================================================

set "INSTALL_DIR=%LOCALAPPDATA%\Programs\Tempo"
set "SM_LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Tempo.lnk"
set "DESK_LNK=%USERPROFILE%\Desktop\Tempo.lnk"
set "REGKEY=HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Tempo"

echo(
echo   Uninstalling Tempo ...
echo(

REM --- close Tempo if it's running (best-effort) ---
taskkill /im Tempo.exe /f >nul 2>&1

REM --- remove shortcuts ---
if exist "%SM_LNK%" del /f /q "%SM_LNK%" >nul 2>&1
if exist "%DESK_LNK%" del /f /q "%DESK_LNK%" >nul 2>&1

REM --- remove the Settings > Apps entry ---
reg delete "%REGKEY%" /f >nul 2>&1

echo   Removed shortcuts and the Apps entry.
echo(
set /p KEEP=  Also delete your saved settings and profiles? [Y/N] 
if /i "!KEEP!"=="Y" (
  if exist "%LOCALAPPDATA%\AutoClicker" rd /s /q "%LOCALAPPDATA%\AutoClicker" >nul 2>&1
  echo   Saved settings removed.
) else (
  echo   Saved settings were kept.
)

echo(
echo   Removing program files. You can close this window.
echo(

REM  This script lives inside the folder being deleted, so hand the final
REM  removal to a detached cmd that waits for us to exit, then deletes the
REM  folder from a different working directory.
start "" /b cmd /c "cd /d %SystemRoot% & ping -n 5 127.0.0.1 >nul & rd /s /q ""%INSTALL_DIR%"""

timeout /t 3 >nul
exit /b 0
