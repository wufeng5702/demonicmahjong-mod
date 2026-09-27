@echo off
rem Shared build driver for the three mods. Lives in tools\modbat\ so the repo root
rem stays a clean set of end-user entry points (install_mods.bat is the only thing to click).
rem   %1 = mod folder name (also the AssemblyName), %2 = optional game dir
rem Each mod keeps a thin build.bat wrapper so `cd ScorePreview && build.bat` still works.
rem NOTE: do not use SHIFT - it also moves %0, which would break %~dp0.
setlocal
set "MOD=%~1"
set "GAME_DIR=%~2"
for %%I in ("%~dp0..\..") do set "ROOT=%%~fI"

if "%MOD%"=="" (
    echo [build] usage: build.bat ^<ModName^> [game dir]
    exit /b 1
)
if not exist "%ROOT%\%MOD%\%MOD%.csproj" (
    echo [build] unknown mod "%MOD%": no file %MOD%\%MOD%.csproj
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
    echo [build] game dir not found. create mod\.env with DEMONIC_MAHJONG_DIR=... or pass a dir:
    echo [build]   build.bat %MOD% D:\path\to\game
    exit /b 1
)

pushd "%ROOT%\%MOD%" >nul
dotnet build -c Release -p:GameDir="%GAME_DIR%"
set "RC=%ERRORLEVEL%"
popd >nul

if not "%RC%"=="0" (
    echo.
    echo [build] FAILED for %MOD%.
    echo [build] if interop\MaJiang.dll / Il2Cppmscorlib.dll missing:
    echo [build]   launch the game once so BepInEx generates interop, then retry.
    echo [build] if BepInEx\core\BepInEx.Core.dll missing:
    echo [build]   check game root, or pass another dir:
    echo [build]     build.bat %MOD% D:\another\game\dir
    exit /b 1
)

echo.
echo [build] OK. run install.bat to copy the plugin.
exit /b 0
