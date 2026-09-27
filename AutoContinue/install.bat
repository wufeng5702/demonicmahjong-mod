@echo off
rem Thin wrapper: the real logic lives in ..\tools\modbat\install.bat
rem (shared by all three mods, kept out of the repo root on purpose).
call "%~dp0..\tools\modbat\install.bat" AutoContinue %*
exit /b %ERRORLEVEL%
