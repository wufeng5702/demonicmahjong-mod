@echo off
setlocal
cd /d "%~dp0"

rem Arg 1 = game dir; falls back to env DEMONIC_MAHJONG_DIR (.env at repo root), then old default.
set "GAME_DIR=%1"
if "%GAME_DIR%"=="" set "GAME_DIR=%DEMONIC_MAHJONG_DIR%"
if "%GAME_DIR%"=="" (
    for /f "usebackq tokens=1,* delims==" %%a in ("%~dp0..\.env") do (
        if /i "%%a"=="DEMONIC_MAHJONG_DIR" set "GAME_DIR=%%b"
    )
)
if "%GAME_DIR%"=="" set "GAME_DIR=E:\Program Files (x86)\Steam\steamapps\common\DemonicMahjong"

set "PLUGIN=bin\Release\AutoContinue.dll"

if not exist "%GAME_DIR%\BepInEx\plugins" (
    echo [install] no BepInEx plugins dir at: "%GAME_DIR%\BepInEx\plugins"
    exit /b 1
)
if not exist "%PLUGIN%" (
    echo [install] missing %PLUGIN%; run build.bat first.
    exit /b 1
)

copy /y "%PLUGIN%" "%GAME_DIR%\BepInEx\plugins\AutoContinue.dll" >nul
if errorlevel 1 exit /b 1
echo [install] installed: %GAME_DIR%\BepInEx\plugins\AutoContinue.dll
echo [install] config AutoContinue.yml will be created next to it on first launch.
endlocal