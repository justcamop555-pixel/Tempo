@echo off
REM ===========================================================================
REM
REM   TEMPO  -  uninstall.cmd
REM
REM   Removes Tempo's shortcuts, its Settings > Apps entry, and (after this
REM   window closes) the installed program files. Can optionally back up and/or
REM   remove your saved settings and profiles.
REM
REM   USAGE
REM     uninstall.cmd [flags]
REM
REM   FLAGS
REM     /help, /?    Show help and exit.
REM     /silent      No questions. Removes the program but KEEPS your settings.
REM     /keepdata    Keep saved settings/profiles (and don't ask).
REM     /purge       Also delete saved settings/profiles (and don't ask).
REM     /backup      Before purging, save a zip of your settings to the Desktop.
REM
REM   EXIT CODES
REM     0 success   3 usage
REM
REM ===========================================================================
setlocal EnableExtensions EnableDelayedExpansion
title Tempo Uninstaller

set "OPT_SILENT="
set "OPT_KEEP="
set "OPT_PURGE="
set "OPT_BACKUP="
set "BADARG="

:parse
if "%~1"=="" goto :parsed
set "A=%~1"
if /i "%A%"=="/help"     goto :help
if /i "%A%"=="-help"     goto :help
if /i "%A%"=="/?"        goto :help
if /i "%A%"=="/silent"   set "OPT_SILENT=1" & goto :pnext
if /i "%A%"=="/keepdata" set "OPT_KEEP=1"   & goto :pnext
if /i "%A%"=="/purge"    set "OPT_PURGE=1"  & goto :pnext
if /i "%A%"=="/backup"   set "OPT_BACKUP=1" & goto :pnext
set "BADARG=%A%"
:pnext
shift
goto :parse
:parsed

if defined BADARG (
  echo.
  echo   Unknown flag: %BADARG%
  echo   Run  uninstall.cmd /help  to see the options.
  echo.
  endlocal
  exit /b 3
)

set "INSTALL_DIR=%LOCALAPPDATA%\Programs\TempoClicker"
set "LEGACY_DIR=%LOCALAPPDATA%\Programs\Tempo"
set "SM_LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Tempo.lnk"
set "DESK_LNK=%USERPROFILE%\Desktop\Tempo.lnk"
set "REGKEY=HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Tempo"
set "DATA_DIR=%LOCALAPPDATA%\AutoClicker"
set "LOG=%TEMP%\tempo-uninstall-log.txt"

> "%LOG%" echo Tempo uninstall log  -  %DATE% %TIME%

echo(
echo   ==================================================
echo     Uninstalling Tempo
echo   ==================================================
echo(

REM ---------------------------------------------------------------------------
REM  If Tempo was never installed (e.g. you only ran publish.cmd, or use it
REM  portably), there's nothing here to uninstall. Say so plainly instead of
REM  pretending to remove an installed copy that doesn't exist. Saved settings
REM  in %DATA_DIR% may still exist and can be cleared with /purge.
REM ---------------------------------------------------------------------------
if not exist "%INSTALL_DIR%\Tempo.exe" if not exist "%SM_LNK%" (
  echo   Tempo doesn't appear to be installed for your account.
  echo   ^(Looked in: %INSTALL_DIR%^)
  echo(
  if defined OPT_PURGE (
    if exist "%DATA_DIR%" (
      rd /s /q "%DATA_DIR%" >nul 2>&1
      echo   Saved settings removed ^(/purge^).
      >> "%LOG%" echo Data   : purged ^(not installed^)
    ) else (
      echo   Nothing to remove.
    )
  ) else (
    if exist "%DATA_DIR%" (
      echo   Your saved settings still exist in:
      echo     %DATA_DIR%
      echo   Run  uninstall.cmd /purge  to delete those too.
    ) else (
      echo   Nothing to remove.
    )
    echo(
    echo   If you use Tempo portably, just delete Tempo.exe yourself.
  )
  echo(
  >> "%LOG%" echo RESULT : not installed
  if not defined OPT_SILENT pause
  endlocal
  exit /b 0
)

REM --- close Tempo if it's running (best-effort) ---
tasklist /fi "imagename eq Tempo.exe" 2>nul | find /i "Tempo.exe" >nul
if not errorlevel 1 (
  echo   Closing the running Tempo ...
  taskkill /im Tempo.exe /f >nul 2>&1
  >> "%LOG%" echo Killed : running Tempo.exe
  timeout /t 1 >nul
)

REM --- remove shortcuts ---
set "REMOVED="
if exist "%SM_LNK%"   ( del /f /q "%SM_LNK%"   >nul 2>&1 & set "REMOVED=1" )
if exist "%DESK_LNK%" ( del /f /q "%DESK_LNK%" >nul 2>&1 & set "REMOVED=1" )

REM --- remove the Settings > Apps entry ---
reg delete "%REGKEY%" /f >nul 2>&1

echo   Removed shortcuts and the Settings ^> Apps entry.
>> "%LOG%" echo Removed: shortcuts + registry entry

REM ---------------------------------------------------------------------------
REM  Decide what to do with saved settings/profiles.
REM    flags win; otherwise ask (unless silent, which keeps data).
REM ---------------------------------------------------------------------------
set "DOPURGE="
if defined OPT_PURGE set "DOPURGE=1"
if defined OPT_KEEP set "DOPURGE="
if not defined OPT_PURGE if not defined OPT_KEEP if not defined OPT_SILENT (
  echo(
  if exist "%DATA_DIR%" (
    echo   Your saved settings, profiles, and any downloaded speech model
    echo   ^(can be 100+ MB^) are in:
    echo     %DATA_DIR%
    set /p KEEP=  Also delete all of that? [Y/N] 
    if /i "!KEEP!"=="Y" set "DOPURGE=1"
  )
)

if defined DOPURGE (
  REM Offer (or honour /backup for) a safety zip on the Desktop first.
  set "DOBACKUP="
  if defined OPT_BACKUP set "DOBACKUP=1"
  if not defined OPT_BACKUP if not defined OPT_SILENT (
    if exist "%DATA_DIR%" (
      set /p BK=  Save a backup zip of your settings to the Desktop first? [Y/N] 
      if /i "!BK!"=="Y" set "DOBACKUP=1"
    )
  )
  if defined DOBACKUP (
    where powershell >nul 2>&1
    if not errorlevel 1 (
      set "BKZIP=%USERPROFILE%\Desktop\Tempo-settings-backup.zip"
      if exist "!BKZIP!" del /f /q "!BKZIP!" >nul 2>&1
      powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%DATA_DIR%\*' -DestinationPath '!BKZIP!' -Force" >nul 2>&1
      if exist "!BKZIP!" (
        echo   Backup saved to Desktop: Tempo-settings-backup.zip
        >> "%LOG%" echo Backup : !BKZIP!
      )
    )
  )
  if exist "%DATA_DIR%" rd /s /q "%DATA_DIR%" >nul 2>&1
  echo   Saved settings removed.
  >> "%LOG%" echo Data   : purged
) else (
  echo   Saved settings were kept in:
  echo     %DATA_DIR%
  echo   ^(Run  uninstall.cmd /purge  later if you want to remove them too.^)
  >> "%LOG%" echo Data   : kept
)

echo(
echo   Removing program files. You can close this window.
echo(
>> "%LOG%" echo Files  : scheduling removal of %INSTALL_DIR%

REM  This script lives inside the folder being deleted, so hand the final
REM  removal to a detached cmd that waits for us to exit, then deletes the
REM  folder from a different working directory. Use timeout (with a ping
REM  fallback, in case timeout can't run without a console) for the delay.
start "" /b cmd /c "cd /d %SystemRoot% & (timeout /t 2 /nobreak >nul 2>&1 || ping -n 3 127.0.0.1 >nul 2>&1) & rd /s /q ""%INSTALL_DIR%"""

timeout /t 3 >nul
endlocal
exit /b 0

REM ---------------------------------------------------------------------------
:help
echo.
echo   TEMPO  -  uninstall.cmd
echo.
echo   USAGE
echo     uninstall.cmd [flags]
echo.
echo   FLAGS
echo     /help, /?    Show this help and exit.
echo     /silent      No questions; removes the program but KEEPS your settings.
echo     /keepdata    Keep saved settings/profiles.
echo     /purge       Also delete saved settings/profiles.
echo     /backup      Before purging, save a settings zip to the Desktop.
echo.
echo   EXAMPLES
echo     uninstall.cmd
echo     uninstall.cmd /purge /backup
echo     uninstall.cmd /silent
echo.
echo   Settings/profiles live in:  %%LOCALAPPDATA%%\AutoClicker
echo.
endlocal
exit /b 0
