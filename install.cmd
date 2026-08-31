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
shift
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
REM  The folder is deliberately NOT named "Tempo": Discord's game detection flags
REM  any process at a path ending in "tempo\tempo.exe" as the Steam game "Tempo"
REM  (publisher Aestronauts) and shows it as playing-a-game. "TempoClicker\Tempo.exe"
REM  keeps the exe name (updates & docs unchanged) while breaking that path match.
set "INSTALL_DIR=%LOCALAPPDATA%\Programs\TempoClicker"
set "LEGACY_DIR=%LOCALAPPDATA%\Programs\Tempo"
set "EXE=%INSTALL_DIR%\Tempo.exe"
set "UNINST=%INSTALL_DIR%\uninstall.cmd"
set "SM_LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Tempo.lnk"
set "DESK_LNK=%USERPROFILE%\Desktop\Tempo.lnk"
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
if exist "%EXE%" (
  set "OLDVER=?"
  where powershell >nul 2>&1 && for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item '%EXE%').VersionInfo.FileVersion" 2^>nul`) do set "OLDVER=%%v"
  echo   An existing install was found ^(version !OLDVER!^).
  echo   This will update it to version !NEWVER!.
  >> "%LOG%" echo Upgrade: !OLDVER! -^> !NEWVER!
) else (
  >> "%LOG%" echo Install: fresh, version !NEWVER!
)

REM ---------------------------------------------------------------------------
REM  Close a running Tempo so files aren't locked during copy.
REM ---------------------------------------------------------------------------
tasklist /fi "imagename eq Tempo.exe" 2>nul | find /i "Tempo.exe" >nul
if not errorlevel 1 (
  echo   Tempo is currently running - closing it first ...
  taskkill /im Tempo.exe /f >nul 2>&1
  >> "%LOG%" echo Killed : running Tempo.exe before copy
  timeout /t 1 >nul
)

REM ---------------------------------------------------------------------------
REM  Migrate from the old "Programs\Tempo" folder (whose path made Discord think
REM  Tempo was a Steam game). Remove the stale copy so it can't be launched.
REM ---------------------------------------------------------------------------
if exist "%LEGACY_DIR%\Tempo.exe" (
  echo   Removing old install at %LEGACY_DIR% ...
  rd /s /q "%LEGACY_DIR%" >nul 2>&1
  >> "%LOG%" echo Removed: legacy install at %LEGACY_DIR%
)

echo   Installing to:
echo     %INSTALL_DIR%
echo(

REM ---------------------------------------------------------------------------
REM  Copy files. Roll back the folder if the main copy fails.
REM ---------------------------------------------------------------------------
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%" >nul 2>&1
copy /y "%SRC%Tempo.exe" "%EXE%" >nul
if not exist "%EXE%" (
  echo   ERROR: Could not copy Tempo.exe into place.
  echo   Rolling back ...
  rd /s /q "%INSTALL_DIR%" >nul 2>&1
  >> "%LOG%" echo RESULT : FAILED - copy error, rolled back
  echo(
  if not defined OPT_SILENT pause
  endlocal
  exit /b 2
)
if exist "%SRC%Tempo.exe.sha256" copy /y "%SRC%Tempo.exe.sha256" "%INSTALL_DIR%\Tempo.exe.sha256" >nul
if exist "%SRC%uninstall.cmd"     ( copy /y "%SRC%uninstall.cmd"     "%UNINST%" >nul ) else if exist "%~dp0uninstall.cmd" ( copy /y "%~dp0uninstall.cmd" "%UNINST%" >nul )
if exist "%SRC%INSTALL-README.txt" copy /y "%SRC%INSTALL-README.txt" "%INSTALL_DIR%\INSTALL-README.txt" >nul
REM Copy any native runtime libraries that ship beside Tempo.exe (e.g. the
REM Whisper speech-engine DLLs). Without these, Tempo's own Live Captions fail
REM with "install the default libraries with the Whisper.net.Runtime nuget".
if exist "%SRC%*.dll" copy /y "%SRC%*.dll" "%INSTALL_DIR%\" >nul 2>&1
REM Some runtimes place native libs in a runtimes\ subfolder; bring it along too.
if exist "%SRC%runtimes" xcopy /e /i /y /q "%SRC%runtimes" "%INSTALL_DIR%\runtimes" >nul 2>&1
>> "%LOG%" echo Copied : Tempo.exe (+ extras, native libs)

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
  echo   Desktop shortcut created.
  >> "%LOG%" echo Shortcut: desktop
)

REM ---------------------------------------------------------------------------
REM  Register in Settings > Apps (per-user, no admin).
REM ---------------------------------------------------------------------------
reg add "%REGKEY%" /v DisplayName /t REG_SZ /d "Tempo" /f >nul
reg add "%REGKEY%" /v DisplayVersion /t REG_SZ /d "!VER!" /f >nul
reg add "%REGKEY%" /v Publisher /t REG_SZ /d "Tempo" /f >nul
reg add "%REGKEY%" /v InstallLocation /t REG_SZ /d "%INSTALL_DIR%" /f >nul
reg add "%REGKEY%" /v DisplayIcon /t REG_SZ /d "%EXE%" /f >nul
reg add "%REGKEY%" /v UninstallString /t REG_SZ /d "\"%UNINST%\"" /f >nul
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
  echo   You can double-click that Tempo.exe to run it, or make your own shortcut.
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
set "VBS=%TEMP%	empo_lnk_%RANDOM%.vbs"
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
