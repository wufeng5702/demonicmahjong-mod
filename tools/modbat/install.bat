@echo off
rem Shared install driver for the three mods. Lives in tools\modbat\ so the repo root
rem stays a clean set of end-user entry points (install_mods.bat is the only thing to click).
rem   %1 = mod folder name (dll name is the same), %2 = optional game dir
rem Each mod keeps a thin install.bat wrapper so `cd ScorePreview && install.bat` still works.
rem NOTE: do not use SHIFT - it also moves %0, which would break %~dp0.
setlocal
set "MOD=%~1"
set "GAME_DIR=%~2"
for %%I in ("%~dp0..\..") do set "ROOT=%%~fI"

if "%MOD%"=="" (
    echo [install] usage: install.bat ^<ModName^> [game dir]
    exit /b 1
)
if not exist "%ROOT%\%MOD%\%MOD%.csproj" (
    echo [install] unknown mod "%MOD%": no file %MOD%\%MOD%.csproj
    exit /b 1
)

rem Arg 2 = game dir; falls back to env DEMONIC_MAHJONG_DIR, then repo root .env. Real path only in .env.
if "%GAME_DIR%"=="" set "GAME_DIR=%DEMONIC_MAHJONG_DIR%"
if "%GAME_DIR%"=="" (
    for /f "usebackq tokens=1,* delims==" %%a in ("%ROOT%\.env") do (
        if /i "%%a"=="DEMONIC_MAHJONG_DIR" set "GAME_DIR=%%b"
    )
)
if "%GAME_DIR%"=="" (
    echo [install] game dir not found. create mod\.env with DEMONIC_MAHJONG_DIR=... or pass a dir:
    echo [install]   install.bat %MOD% D:\path\to\game
    exit /b 1
)

set "PLUGIN=bin\Release\%MOD%.dll"

if not exist "%GAME_DIR%\BepInEx\plugins" (
    echo [install] no BepInEx plugins dir at: "%GAME_DIR%\BepInEx\plugins"
    echo [install] install BepInEx first: run install_mods.bat from the repo root.
    exit /b 1
)
if not exist "%ROOT%\%MOD%\%PLUGIN%" (
    echo [install] missing %MOD%\%PLUGIN%; run build.bat in %MOD% first.
    exit /b 1
)

pushd "%ROOT%\%MOD%" >nul
copy /y "%PLUGIN%" "%GAME_DIR%\BepInEx\plugins\%MOD%.dll" >nul
set "RC=%ERRORLEVEL%"
popd >nul

if not "%RC%"=="0" (
    echo [install] copy FAILED: "%GAME_DIR%\BepInEx\plugins\%MOD%.dll"
    echo [install] most likely the game is running and holds the file lock:
    echo [install]   taskkill //F //IM "Demonic Mahjong.exe"
    echo [install] then re-run install.bat. Also check the target dir is not read-only.
    exit /b 1
)

echo [install] installed: %GAME_DIR%\BepInEx\plugins\%MOD%.dll
echo [install] config %MOD%.yml will be created next to it on first launch.
exit /b 0
