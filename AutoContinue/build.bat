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

dotnet build -c Release -p:GameDir="%GAME_DIR%"
if errorlevel 1 (
    echo.
    echo [build] FAILED. check interop files under: %GAME_DIR%\BepInEx\interop
    exit /b 1
)

echo.
echo [build] OK. run install.bat to copy the plugin.
endlocal