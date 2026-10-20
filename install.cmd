@echo off
REM ===========================================================================
REM
REM   TEMPO  -  install.cmd  (per-user installer, no administrator rights)
REM
REM   Copies Tempo into your user profile, creates Start Menu (and optional
REM   Desktop) shortcuts, verifies the download against its bundled checksum,
REM   and registers an entry in Settings > Apps so it uninstalls normally.
REM
REM   USAGE
REM     install.cmd [flags]
REM
REM   FLAGS
REM     /help, /?    Show help and exit.
REM     /silent      No questions. Installs to the default location, creates a
REM                  Start Menu shortcut, no Desktop shortcut, does not launch.
REM     /desktop     Create a Desktop shortcut (implied yes; great with /silent).
REM     /nodesktop   Do not create a Desktop shortcut (and don't ask).
REM     /launch      Launch Tempo when done (great with /silent).
REM     /nolaunch    Do not launch and do not ask.
REM
REM   EXIT CODES
REM     0 success   1 Tempo.exe missing   2 copy failed   3 usage   4 checksum
REM
REM ===========================================================================
setlocal EnableExtensions EnableDelayedExpansion
title Tempo Installer

REM ---------------------------------------------------------------------------
REM  Flags
REM ---------------------------------------------------------------------------
set "OPT_SILENT="
set "OPT_DESKTOP="
set "OPT_NODESKTOP="
set "OPT_LAUNCH="
set "OPT_NOLAUNCH="
set "BADARG="

:parse
if "%~1"=="" goto :parsed
set "A=%~1"
if /i "%A%"=="/help"      goto :help
if /i "%A%"=="-help"      goto :help
if /i "%A%"=="/?"         goto :help
if /i "%A%"=="/silent"    set "OPT_SILENT=1"    & goto :pnext
if /i "%A%"=="/desktop"   set "OPT_DESKTOP=1"   & goto :pnext
if /i "%A%"=="/nodesktop" set "OPT_NODESKTOP=1" & goto :pnext
if /i "%A%"=="/launch"    set "OPT_LAUNCH=1"    & goto :pnext
if /i "%A%"=="/nolaunch"  set "OPT_NOLAUNCH=1"  & goto :pnext
if /i "%A%"=="/nomodel"   set "OPT_NOMODEL=1"   & goto :pnext
set "BADARG=%A%"
:pnext
REM  SHIFT /1, not SHIFT: a plain SHIFT moves argument zero too, so after the first flag the
REM  script's own folder was lost. install.cmd /silent then searched for Tempo.exe from the root
REM  of the drive and installed whichever copy a scan of the whole disk found first - on the PC
REM  this was caught on, an old build lying in the Recycle Bin.
shift /1
goto :parse
:parsed

if defined BADARG (
  echo.
  echo   Unknown flag: %BADARG%
  echo   Run  install.cmd /help  to see the options.
  echo.
  endlocal
  exit /b 3
)

set "SRC=%~dp0"
REM  Neither the folder nor the program is named "Tempo": Discord's game detection
REM  lists the Steam game "Tempo" (publisher Aestronauts) as tempo\tempo.exe and
REM  shows anyone running a matching program as playing it. Renaming only the
REM  folder (TempoClicker\Tempo.exe) did NOT stop that, so the installed program is
REM  TempoClicker.exe too. Downloads keep the name Tempo.exe; this installs it under
REM  the new name, and an older install's Tempo.exe is removed once this is in place.
set "INSTALL_DIR=%LOCALAPPDATA%\Programs\TempoClicker"
set "LEGACY_DIR=%LOCALAPPDATA%\Programs\Tempo"
set "EXE=%INSTALL_DIR%\TempoClicker.exe"
set "OLDEXE=%INSTALL_DIR%\Tempo.exe"
set "UNINST=%INSTALL_DIR%\uninstall.cmd"
set "SM_LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Tempo.lnk"
REM  The Desktop Windows actually shows, which is not always the Desktop folder in the user profile:
REM  OneDrive's folder backup moves it into OneDrive, and a shortcut written to the old path landed in
REM  a folder nobody sees - while this script still reported "Desktop shortcut created".
set "DESK_DIR="
for /f "tokens=2,*" %%A in ('reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders" /v Desktop 2^>nul') do (
  if /i "%%A"=="REG_EXPAND_SZ" call set "DESK_DIR=%%B"
  if /i "%%A"=="REG_SZ" set "DESK_DIR=%%B"
)
if not defined DESK_DIR set "DESK_DIR=%USERPROFILE%\Desktop"
set "DESK_LNK=%DESK_DIR%\Tempo.lnk"
set "REGKEY=HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Tempo"
set "LOG=%TEMP%\tempo-install-log.txt"

> "%LOG%" echo Tempo install log  -  %DATE% %TIME%
>> "%LOG%" echo Source : %SRC%
>> "%LOG%" echo Target : %INSTALL_DIR%

echo(
echo   ==================================================
echo     Installing Tempo
echo   ==================================================
echo(

REM ---------------------------------------------------------------------------
REM  Preflight: locate Tempo.exe. It is normally right next to this script (the
REM  case when you run install.cmd from inside the unzipped setup folder). But
REM  people also run it straight from the project root after publishing, where
REM  the exe lives under bin\publish\<rid>\Tempo.exe. So we search, in order:
REM    1. this folder
REM    2. bin\publish\<rid>\            (the publish.cmd output location)
REM    3. any immediate sub-folder      (the "double-folder unzip" case)
REM    4. a deeper recursive scan       (last-resort catch-all)
REM  The first Tempo.exe found becomes the install source.
REM ---------------------------------------------------------------------------
call :FindExe
if not defined SRCEXE (
  echo   ERROR: Tempo.exe was not found.
  echo(
  echo   This installer looked next to itself, in bin\publish\, and in
  echo   sub-folders, but couldn't find Tempo.exe.
  echo(
  echo   FIX - pick whichever matches you:
  echo     - If you UNZIPPED a setup zip: make sure Tempo.exe and install.cmd
  echo       are in the SAME folder, then run install.cmd again.
  echo     - If you just ran publish.cmd: run install.cmd from the project
  echo       folder ^(it will find bin\publish\^<rid^>\Tempo.exe automatically^),
  echo       or copy install.cmd into bin\publish\^<rid^>\ and run it there.
  echo(
  echo   Looked under:
  echo     %SRC%
  echo     %SRC%bin\publish\
  echo(
  >> "%LOG%" echo RESULT : FAILED - Tempo.exe not found
  if not defined OPT_SILENT pause
  endlocal
  exit /b 1
)

REM Adopt the folder that actually contains Tempo.exe as the copy source.
for %%F in ("%SRCEXE%") do set "SRC=%%~dpF"
>> "%LOG%" echo Source : resolved to %SRC%
echo   Found Tempo.exe in:
echo     %SRC%
echo(

if not "%PROCESSOR_ARCHITECTURE%"=="AMD64" if not "%PROCESSOR_ARCHITEW6432%"=="AMD64" if not "%PROCESSOR_ARCHITECTURE%"=="ARM64" (
  echo   NOTE: this build targets 64-bit Windows. Your system reports
  echo         "%PROCESSOR_ARCHITECTURE%". If Tempo won't start, grab the
  echo         matching build for your CPU.
  >> "%LOG%" echo Arch   : %PROCESSOR_ARCHITECTURE% (warned)
)

REM ---------------------------------------------------------------------------
REM  Integrity: if a bundled checksum is present, verify the exe before copy.
REM ---------------------------------------------------------------------------
if exist "%SRC%Tempo.exe.sha256" (
  echo   Verifying download integrity ...
  set "WANT="
  for /f "usebackq tokens=1" %%a in ("%SRC%Tempo.exe.sha256") do if not defined WANT set "WANT=%%a"
  set "GOT="
  for /f "skip=1 tokens=* delims=" %%H in ('certutil -hashfile "%SRC%Tempo.exe" SHA256 2^>nul') do if not defined GOT set "GOT=%%H"
  set "GOT=!GOT: =!"
  set "WANT=!WANT: =!"
  if /i "!GOT!"=="!WANT!" (
    echo   Integrity check passed.
    >> "%LOG%" echo Hash   : OK
  ) else (
    echo(
    echo   WARNING: Tempo.exe does NOT match its bundled checksum.
    echo     expected: !WANT!
    echo     got     : !GOT!
    echo   This copy may be corrupted or tampered with.
    >> "%LOG%" echo Hash   : MISMATCH expected=!WANT! got=!GOT!
    REM A mismatch is often just a line-ending/format difference in the .sha256
    REM or a certutil quirk - not corruption. Warn loudly but do NOT block the
    REM install over it, so genuine users still get Tempo. (Real tampering is
    REM rare and the warning above makes it visible.)
    echo   Continuing anyway - if you did not expect this, re-download from the
    echo   official site: https://justcamop555-pixel.github.io/Tempo/
    >> "%LOG%" echo Hash   : continuing despite mismatch (non-fatal)
  )
) else (
  echo   ^(No bundled checksum found - skipping integrity check.^)
)

REM ---------------------------------------------------------------------------
REM  Upgrade detection: is an older (or newer) Tempo already installed?
REM ---------------------------------------------------------------------------
set "NEWVER=1.0"
where powershell >nul 2>&1 && for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item '%SRC%Tempo.exe').VersionInfo.FileVersion" 2^>nul`) do set "NEWVER=%%v"
set "PREV="
if exist "%OLDEXE%" set "PREV=%OLDEXE%"
if exist "%EXE%" set "PREV=%EXE%"
if defined PREV (
  set "OLDVER=?"
  where powershell >nul 2>&1 && for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item '%PREV%').VersionInfo.FileVersion" 2^>nul`) do set "OLDVER=%%v"
  echo   An existing install was found ^(version !OLDVER!^).
  echo   This will update it to version !NEWVER!.
  >> "%LOG%" echo Upgrade: !OLDVER! -^> !NEWVER!
) else (
  >> "%LOG%" echo Install: fresh, version !NEWVER!
)

REM ---------------------------------------------------------------------------
REM  Close a running Tempo so files aren't locked during copy.
REM ---------------------------------------------------------------------------
REM find by its full path: with Git's Unix tools on the PATH a bare find is GNU find, which would miss a running Tempo
REM Under both names: an install from before the rename runs as Tempo.exe.
for %%N in (TempoClicker.exe Tempo.exe) do (
  tasklist /fi "imagename eq %%N" 2>nul | "%SystemRoot%\System32\find.exe" /i "%%N" >nul
  if not errorlevel 1 (
    echo   Tempo is currently running - closing it first ...
    taskkill /im %%N /f >nul 2>&1
    >> "%LOG%" echo Killed : running %%N before copy
    "%SystemRoot%\System32\timeout.exe" /t 1 >nul
  )
)

echo   Installing to:
echo     %INSTALL_DIR%
echo(

REM ---------------------------------------------------------------------------
REM  Copy files. Roll back the folder if the main copy fails.
REM ---------------------------------------------------------------------------
REM  Retried for about ten seconds: the Tempo.exe being replaced can stay locked for a moment after
REM  Tempo is closed, or while antivirus scans it. The old check only asked whether a Tempo.exe was
REM  there afterwards - on an update there always is - so a failed copy still said "Done" and wrote
REM  the new version number into Settings > Apps over the old program.
set "HADEXE="
if exist "%EXE%" set "HADEXE=1"
if exist "%OLDEXE%" set "HADEXE=1"
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%" >nul 2>&1
set "COPIED="
for /l %%i in (1,1,10) do if not defined COPIED (
  copy /y "%SRC%Tempo.exe" "%EXE%" >nul 2>&1 && set "COPIED=1" || "%SystemRoot%\System32\PING.EXE" -n 2 127.0.0.1 >nul
)
if not defined COPIED (
  if defined HADEXE (
    echo   ERROR: Could not replace the installed Tempo.exe - something is still using it.
    echo   Your current Tempo is unchanged. Close Tempo ^(check the tray^), then run install.cmd again.
    >> "%LOG%" echo RESULT : FAILED - Tempo.exe in use, update not applied
  ) else (
    echo   ERROR: Could not copy Tempo.exe into place.
    echo   Rolling back ...
    rd /s /q "%INSTALL_DIR%" >nul 2>&1
    >> "%LOG%" echo RESULT : FAILED - copy error, rolled back
  )
  echo(
  if not defined OPT_SILENT pause
  endlocal
  exit /b 2
)

REM ---------------------------------------------------------------------------
REM  Migrate from the old "Programs\Tempo" folder (whose path made Discord think
REM  Tempo was a Steam game). Remove the stale copy so it can't be launched -
REM  only now that the new copy is in place, so a failed copy never leaves neither.
REM ---------------------------------------------------------------------------
if exist "%LEGACY_DIR%\Tempo.exe" (
  echo   Removing old install at %LEGACY_DIR% ...
  rd /s /q "%LEGACY_DIR%" >nul 2>&1
  >> "%LOG%" echo Removed: legacy install at %LEGACY_DIR%
)
REM  And this folder's program from before the rename to TempoClicker.exe - again
REM  only now, with the new one in place. A sign-in entry that still starts it is
REM  moved to the new name (Tempo moves shortcuts and taskbar pins itself on its
REM  next start).
if exist "%OLDEXE%" (
  del /q "%OLDEXE%" >nul 2>&1
  >> "%LOG%" echo Removed: %OLDEXE% ^(the program is TempoClicker.exe now^)
)
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Tempo 2>nul | "%SystemRoot%\System32\find.exe" /i "%OLDEXE%" >nul
if not errorlevel 1 (
  reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Tempo /t REG_SZ /d "\"%EXE%\" --startup" /f >nul
  >> "%LOG%" echo Moved  : sign-in entry to %EXE%
)
if exist "%SRC%uninstall.cmd"     ( copy /y "%SRC%uninstall.cmd"     "%UNINST%" >nul ) else if exist "%~dp0uninstall.cmd" ( copy /y "%~dp0uninstall.cmd" "%UNINST%" >nul )
REM  Only the program and its uninstaller. Tempo is ONE file - the speech engine is inside it -
REM  so nothing else from the download folder belongs here. This used to copy every *.dll and
REM  any "runtimes" folder found beside the download (in Downloads: other programs' files), and
REM  Windows looks in a program's own folder first when the program asks for a library by name.
REM  The setup note and the checksum went stale with the first update. What an older install
REM  copied in goes too: the install folder is Tempo's alone.
del /q "%INSTALL_DIR%\*.dll" >nul 2>&1
if exist "%INSTALL_DIR%\runtimes" rd /s /q "%INSTALL_DIR%\runtimes" >nul 2>&1
del /q "%INSTALL_DIR%\INSTALL-README.txt" >nul 2>&1
del /q "%INSTALL_DIR%\Tempo.exe.sha256" >nul 2>&1
>> "%LOG%" echo Copied : TempoClicker.exe and uninstall.cmd

REM ---------------------------------------------------------------------------
REM  Optional: fetch the Base speech model for Tempo's own offline Live Captions
REM  so users don't have to add it by hand. ~140 MB; skipped with /nomodel or if
REM  curl/internet is unavailable (Tempo can still download it later from
REM  Settings > Live Captions, and Windows Live Captions needs no model at all).
REM ---------------------------------------------------------------------------
set "MODEL_DIR=%LOCALAPPDATA%\AutoClicker\models"
set "MODEL_FILE=%MODEL_DIR%\ggml-base.en.bin"
set "MODEL_URL=https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin"
if defined OPT_NOMODEL goto :skipmodel
if exist "%MODEL_FILE%" goto :skipmodel
where curl >nul 2>&1 || goto :skipmodel
set "DOMODEL="
if defined OPT_SILENT set "DOMODEL=1"
if not defined OPT_SILENT (
  echo(
  echo   Tempo's own offline Live Captions need a small speech model ^(~140 MB^).
  set /p MDL=  Download it now so captions work out of the box? [Y/N] 
  if /i "!MDL!"=="Y" set "DOMODEL=1"
)
if defined DOMODEL (
  echo   Downloading speech model ^(~140 MB, one time^) ...
  if not exist "%MODEL_DIR%" mkdir "%MODEL_DIR%" >nul 2>&1
  del /q "%MODEL_FILE%.part" >nul 2>&1
  curl -L --fail --silent --show-error -o "%MODEL_FILE%.part" "%MODEL_URL%"
  REM Validate the download by size before installing it. A dropped connection can
  REM leave a truncated .part that would otherwise be moved into place as a corrupt
  REM model - which makes the offline speech engine crash on load. The base.en model
  REM is ~147 MB, so require at least 100 MB; anything smaller is treated as failed.
  set "MDLSZ=0"
  if exist "%MODEL_FILE%.part" for %%S in ("%MODEL_FILE%.part") do set "MDLSZ=%%~zS"
  if !MDLSZ! GEQ 104857600 (
    move /y "%MODEL_FILE%.part" "%MODEL_FILE%" >nul
    echo   Speech model installed.
    >> "%LOG%" echo Model  : base.en downloaded ^(!MDLSZ! bytes^)
  ) else (
    del /q "%MODEL_FILE%.part" >nul 2>&1
    echo   ^(Model download failed or incomplete - removed the partial file.^)
    echo    You can get it later in Settings ^> Live Captions, or use Windows captions.
    >> "%LOG%" echo Model  : download failed/incomplete ^(partial removed^)
  )
)
:skipmodel

set "VER=%NEWVER%"

echo   Creating Start Menu shortcut ...
call :MakeShortcut "%SM_LNK%" "%EXE%" "%INSTALL_DIR%"

REM ---------------------------------------------------------------------------
REM  Desktop shortcut: flags win; otherwise ask (unless silent).
REM ---------------------------------------------------------------------------
set "MAKEDESK="
if defined OPT_DESKTOP set "MAKEDESK=1"
if defined OPT_NODESKTOP set "MAKEDESK="
if not defined OPT_DESKTOP if not defined OPT_NODESKTOP if not defined OPT_SILENT (
  echo(
  set /p DESK=  Create a Desktop shortcut too? [Y/N] 
  if /i "!DESK!"=="Y" set "MAKEDESK=1"
)
if defined MAKEDESK (
  call :MakeShortcut "%DESK_LNK%" "%EXE%" "%INSTALL_DIR%"
  if exist "%DESK_LNK%" (
    echo   Desktop shortcut created.
    >> "%LOG%" echo Shortcut: desktop
  )
)

REM ---------------------------------------------------------------------------
REM  Register in Settings > Apps (per-user, no admin).
REM ---------------------------------------------------------------------------
reg add "%REGKEY%" /v DisplayName /t REG_SZ /d "Tempo" /f >nul
reg add "%REGKEY%" /v DisplayVersion /t REG_SZ /d "!VER!" /f >nul
reg add "%REGKEY%" /v Publisher /t REG_SZ /d "Tempo" /f >nul
reg add "%REGKEY%" /v InstallLocation /t REG_SZ /d "%INSTALL_DIR%" /f >nul
reg add "%REGKEY%" /v DisplayIcon /t REG_SZ /d "%EXE%" /f >nul
REM  Point the Uninstall button at uninstall.cmd ONLY if it was really copied above —
REM  that copy is conditional on finding one, and this write was not, so any package
REM  shipped without uninstall.cmd left a dead Uninstall button in Settings > Apps.
REM  Tempo.exe cannot go missing: it is the thing being uninstalled.
if exist "%UNINST%" (
  reg add "%REGKEY%" /v UninstallString /t REG_SZ /d "\"%UNINST%\"" /f >nul
  reg add "%REGKEY%" /v QuietUninstallString /t REG_SZ /d "\"%UNINST%\"" /f >nul
) else (
  reg add "%REGKEY%" /v UninstallString /t REG_SZ /d "\"%EXE%\" --uninstall" /f >nul
  reg add "%REGKEY%" /v QuietUninstallString /t REG_SZ /d "\"%EXE%\" --uninstall" /f >nul
)
reg add "%REGKEY%" /v URLInfoAbout /t REG_SZ /d "https://justcamop555-pixel.github.io/Tempo/" /f >nul
reg add "%REGKEY%" /v NoModify /t REG_DWORD /d 1 /f >nul
reg add "%REGKEY%" /v NoRepair /t REG_DWORD /d 1 /f >nul
>> "%LOG%" echo Reg    : Uninstall entry written

echo(
echo   ==================================================
echo     Done - Tempo !VER! is installed.
echo   ==================================================
if exist "%SM_LNK%" (
  echo   Launch it from the Start Menu ^(search "Tempo"^).
) else (
  echo   Tempo is installed here ^(no Start Menu shortcut was created^):
  echo     %EXE%
  echo   You can double-click that file to run it, or make your own shortcut.
)
echo   To remove it later: Settings ^> Apps, or run uninstall.cmd in:
echo     %INSTALL_DIR%
echo(
echo   Your settings, profiles, macros and stats are saved in:
echo     %LOCALAPPDATA%\AutoClicker
echo   ^(That folder has a README explaining each file. It survives updates;
echo    uninstalling can optionally remove it.^)
echo(
>> "%LOG%" echo RESULT : SUCCESS (version !VER!)

REM ---------------------------------------------------------------------------
REM  Launch: flags win; otherwise ask (unless silent).
REM ---------------------------------------------------------------------------
set "DORUN="
if defined OPT_LAUNCH set "DORUN=1"
if defined OPT_NOLAUNCH set "DORUN="
if not defined OPT_LAUNCH if not defined OPT_NOLAUNCH if not defined OPT_SILENT (
  set /p RUN=  Launch Tempo now? [Y/N] 
  if /i "!RUN!"=="Y" set "DORUN=1"
)
if defined DORUN start "" "%EXE%"
echo(
endlocal
exit /b 0

REM ---------------------------------------------------------------------------
:FindExe
REM   Locate Tempo.exe and set SRCEXE to its full path (empty if not found).
REM   Search order: this folder, bin\publish\<rid>\, immediate sub-folders,
REM   then a deeper recursive scan as a catch-all.
set "SRCEXE="
if exist "%SRC%Tempo.exe" set "SRCEXE=%SRC%Tempo.exe" & goto :eof
REM bin\publish\<rid>\Tempo.exe (the publish.cmd output)
if exist "%SRC%bin\publish\" (
  for /f "delims=" %%P in ('dir /b /s "%SRC%bin\publish\Tempo.exe" 2^>nul') do if not defined SRCEXE set "SRCEXE=%%P"
)
if defined SRCEXE goto :eof
REM any immediate sub-folder (double-folder unzip)
for /d %%D in ("%SRC%*") do if not defined SRCEXE if exist "%%~fD\Tempo.exe" set "SRCEXE=%%~fD\Tempo.exe"
if defined SRCEXE goto :eof
REM last resort: recursive scan under this folder
for /f "delims=" %%P in ('dir /b /s "%SRC%Tempo.exe" 2^>nul') do if not defined SRCEXE set "SRCEXE=%%P"
goto :eof

REM ---------------------------------------------------------------------------
:MakeShortcut
REM   %~1 = shortcut path   %~2 = target exe   %~3 = working directory
REM   Try PowerShell first; if it's missing or blocked, fall back to a tiny
REM   VBScript via cscript (present on essentially every Windows). This is the
REM   fix for "some users don't get Tempo" - locked-down PCs often block
REM   PowerShell, which previously left them with no shortcut at all.
where powershell >nul 2>&1
if not errorlevel 1 (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "$w=New-Object -ComObject WScript.Shell; $s=$w.CreateShortcut('%~1'); $s.TargetPath='%~2'; $s.WorkingDirectory='%~3'; $s.IconLocation='%~2,0'; $s.Description='Tempo'; $s.Save()" >nul 2>&1
  if exist "%~1" goto :eof
)
REM Fallback: build the shortcut with a temporary VBScript.
set "VBS=%TEMP%\tempo_lnk_%RANDOM%.vbs"
> "%VBS%" echo Set s = CreateObject("WScript.Shell")
>> "%VBS%" echo Set lnk = s.CreateShortcut("%~1")
>> "%VBS%" echo lnk.TargetPath = "%~2"
>> "%VBS%" echo lnk.WorkingDirectory = "%~3"
>> "%VBS%" echo lnk.IconLocation = "%~2,0"
>> "%VBS%" echo lnk.Description = "Tempo"
>> "%VBS%" echo lnk.Save
cscript //nologo "%VBS%" >nul 2>&1
del "%VBS%" >nul 2>&1
if exist "%~1" goto :eof
echo   ^(Could not create shortcut %~nx1 - you can still run Tempo.exe directly.^)
>> "%LOG%" echo Shortcut: FAILED for %~nx1 (no PowerShell/cscript)
goto :eof

REM ---------------------------------------------------------------------------
:help
echo.
echo   TEMPO  -  install.cmd  (per-user, no admin)
echo.
echo   USAGE
echo     install.cmd [flags]
echo.
echo   FLAGS
echo     /help, /?    Show this help and exit.
echo     /silent      No questions; default location, Start Menu shortcut only.
echo     /desktop     Create a Desktop shortcut.
echo     /nodesktop   Do not create a Desktop shortcut.
echo     /launch      Launch Tempo when finished.
echo     /nolaunch    Do not launch.
echo     /nomodel     Skip downloading the captions speech model.
echo.
echo   EXAMPLES
echo     install.cmd
echo     install.cmd /silent /desktop /launch
echo     install.cmd /nodesktop /nolaunch
echo.
echo   Installs to:  %%LOCALAPPDATA%%\Programs\TempoClicker
echo   Verifies Tempo.exe against Tempo.exe.sha256 when that file is present.
echo   Registers in Settings ^> Apps for normal uninstallation.
echo.
endlocal
exit /b 0
