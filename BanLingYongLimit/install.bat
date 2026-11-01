@echo off
rem Thin wrapper: the real logic lives in ..\tools\modbat\install.bat
call "%~dp0..\tools\modbat\install.bat" BanLingYongLimit %*
exit /b %ERRORLEVEL%
