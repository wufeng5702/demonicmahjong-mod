@echo off
setlocal
cd /d "%~dp0"

rem Arg 1 = game dir; falls back to env DEMONIC_MAHJONG_DIR, then repo root .env. Real path only in .env.
set "GAME_DIR=%1"
if "%GAME_DIR%"=="" set "GAME_DIR=%DEMONIC_MAHJONG_DIR%"
if "%GAME_DIR%"=="" (
    for /f "usebackq tokens=1,* delims==" %%a in ("%~dp0..\.env") do (
        if /i "%%a"=="DEMONIC_MAHJONG_DIR" set "GAME_DIR=%%b"
    )
)
if "%GAME_DIR%"=="" (
    echo [build] game dir not found. create mod\.env with DEMONIC_MAHJONG_DIR=... or pass a dir:
    echo [build]   build.bat D:\path\to\game
    exit /b 1
)

dotnet build -c Release -p:GameDir="%GAME_DIR%"
if errorlevel 1 (
    echo.
    echo [build] FAILED.
    echo [build] if interop\MaJiang.dll / Il2Cppmscorlib.dll missing:
    echo [build]   launch the game once so BepInEx generates interop, then retry.
    echo [build] if BepInEx\core\BepInEx.Core.dll missing:
    echo [build]   check game root, or pass another dir:
    echo [build]     build.bat D:\another\game\dir
    exit /b 1
)

echo.
echo [build] OK. run install.bat to copy the plugin.
endlocal