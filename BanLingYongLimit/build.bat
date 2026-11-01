@echo off
rem Thin wrapper: the real logic lives in ..\tools\modbat\build.bat
call "%~dp0..\tools\modbat\build.bat" BanLingYongLimit %*
exit /b %ERRORLEVEL%
