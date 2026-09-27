@echo off
rem Thin wrapper: the real logic lives in ..\tools\modbat\build.bat
rem (shared by all three mods, kept out of the repo root on purpose).
call "%~dp0..\tools\modbat\build.bat" SLMenuTrigger %*
exit /b %ERRORLEVEL%
