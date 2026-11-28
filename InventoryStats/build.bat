@echo off
setlocal
call "%~dp0..\tools\modbat\build.bat" InventoryStats %*
exit /b %errorlevel%
