@echo off
setlocal
cd /d "%~dp0"

set "GAME_DIR=%1"
if "%GAME_DIR%"=="" set "GAME_DIR=%DEMONIC_MAHJONG_DIR%"
if "%GAME_DIR%"=="" (
    for /f "usebackq tokens=1,* delims==" %%a in ("%~dp0..\.env") do (
        if /i "%%a"=="DEMONIC_MAHJONG_DIR" set "GAME_DIR=%%b"
    )
)
if "%GAME_DIR%"=="" (
    echo [install] game dir not found. create mod\.env with DEMONIC_MAHJONG_DIR=... or pass a dir:
    echo [install]   install.bat D:\path\to\game
    exit /b 1
)

set "PLUGIN=bin\Release\SLMenuTrigger.dll"

if not exist "%GAME_DIR%\BepInEx\plugins" (
    echo [install] no BepInEx plugins dir at: "%GAME_DIR%\BepInEx\plugins"
    exit /b 1
)
if not exist "%PLUGIN%" (
    echo [install] missing %PLUGIN%; run build.bat first.
    exit /b 1
)

copy /y "%PLUGIN%" "%GAME_DIR%\BepInEx\plugins\SLMenuTrigger.dll" >nul
if errorlevel 1 exit /b 1

echo [install] installed: %GAME_DIR%\BepInEx\plugins\SLMenuTrigger.dll

echo [install] config SLMenuTrigger.yml will be created next to it on first launch.
endlocal
