@echo off
rem Thin wrapper: the real logic lives in ..\tools\modbat\build.bat
rem (shared by all mods, kept out of the repo root on purpose).
call "%~dp0..\tools\modbat\build.bat" LingPreview %*
exit /b %ERRORLEVEL%
